using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using BG3ItemManager.Models;

namespace BG3ItemManager.Services;

/// <summary>Parses human-readable root templates from manually extracted files and from the automatic PAK cache.</summary>
public static class ExtractedModImporter
{
    public static string ExtractedDirectory => Path.Combine(CatalogService.DataDirectory, "ExtractedMods");
    public static string OutputPath => Path.Combine(CatalogService.DataDirectory, "imported-items.json");
    public sealed record ImportResult(int Files, int Items, int Errors, int LocalizationFiles, int LocalizedNames, int Icons, IReadOnlyList<string> Warnings);

    /// <summary>WPF bitmap encoding is run on an STA worker so the window stays responsive.</summary>
    public static Task<ImportResult> ImportAsync(CancellationToken cancellation = default)
    {
        var completion = new TaskCompletionSource<ImportResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var worker = new Thread(() =>
        {
            try
            {
                cancellation.ThrowIfCancellationRequested();
                completion.SetResult(Import());
            }
            catch (OperationCanceledException) { completion.SetCanceled(cancellation); }
            catch (Exception ex) { completion.SetException(ex); }
        }) { IsBackground = true, Name = "BG3 Icon Importer" };
        worker.SetApartmentState(ApartmentState.STA);
        worker.Start();
        return completion.Task;
    }

    private static readonly Regex StatsEntry = new(@"^\s*new\s+entry\s+""(?<id>[^""]+)""", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex StatsUsing = new(@"^\s*using\s+""(?<id>[^""]+)""", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex StatsType = new(@"^\s*type\s+""(?<type>[^""]+)""", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex StatsData = new(@"^\s*data\s+""(?<key>[^""]+)""\s+""(?<val>[^""]*)""", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static ImportResult Import()
    {
        Directory.CreateDirectory(ExtractedDirectory);
        var items = new Dictionary<string, ItemRecord>(StringComparer.OrdinalIgnoreCase);
        var warnings = new List<string>();
        int files = 0, errors = 0;
        var roots = new List<(string root, string source)> { (ExtractedDirectory, "") };
        if (Directory.Exists(ModArchiveImporter.CacheDirectory))
        {
            // Import original game resources first, patches next, installed mods last.
            // Later entries replace earlier ones when the same template/handle is overridden.
            foreach (var dir in Directory.EnumerateDirectories(ModArchiveImporter.CacheDirectory)
                         .OrderBy(d => SourceOrder(ReadSource(d)))
                         .ThenBy(d => ReadSource(d), StringComparer.OrdinalIgnoreCase))
            {
                if (Path.GetFileName(dir).StartsWith("_import_", StringComparison.Ordinal)) continue;
                foreach (var sub in new[] { "extracted", "converted" })
                    if (Directory.Exists(Path.Combine(dir, sub)))
                    roots.Add((Path.Combine(dir, sub), ReadSource(dir)));
            }
        }
        // First, read templates. Converted LSX supersedes binary files, which are not parsed.
        foreach (var (root, sourceOverride) in roots)
        {
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                         .Where(p => Path.GetExtension(p).Equals(".lsx", StringComparison.OrdinalIgnoreCase)))
            {
                files++;
                try
                {
                    string source = SourceOf(root, sourceOverride, file);
                    var doc = XDocument.Load(file, LoadOptions.None);
                    foreach (var node in doc.Descendants().Where(n => n.Name.LocalName == "node" && (string?)n.Attribute("id") == "GameObjects"))
                    {
                        var attributes = node.Elements().Where(a => a.Name.LocalName == "attribute" && a.Attribute("id") is not null)
                            .GroupBy(a => (string)a.Attribute("id")!, StringComparer.OrdinalIgnoreCase)
                            .ToDictionary(g => g.Key, g => (string?)g.Last().Attribute("value") ?? (string?)g.Last().Attribute("handle") ?? "", StringComparer.OrdinalIgnoreCase);
                        string Get(string key) => attributes.GetValueOrDefault(key, "");
                        string uuid = Get("MapKey");
                        if (!Guid.TryParse(uuid, out _) || !Get("Type").Equals("item", StringComparison.OrdinalIgnoreCase)) continue;
                        var technical = Get("Name");
                        items[uuid] = new ItemRecord
                        {
                            Uuid = uuid, TechnicalName = technical,
                            Category = string.IsNullOrWhiteSpace(Get("ItemType")) ? "Unklassifiziert" : Get("ItemType"),
                            Source = source, IconId = Get("Icon"),
                            NameHandle = Get("DisplayName"), DescriptionHandle = Get("Description"),
                            Description = $"RootTemplate aus {Path.GetFileName(file)}. Spawnfähigkeit noch nicht verifiziert."
                        };
                    }
                }
                catch (Exception ex) when (ex is System.Xml.XmlException or IOException or UnauthorizedAccessException)
                {
                    errors++;
                    if (warnings.Count < 15) warnings.Add($"{Path.GetFileName(file)}: {ex.Message}");
                }
            }
        }
        // Collect stats in both passes so "using" inheritance can be resolved across files.
        var statsByName = new Dictionary<string, StatEntry>(StringComparer.OrdinalIgnoreCase);
        var entrySources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (root, sourceOverride) in roots)
        {
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                         .Where(p => Path.GetExtension(p).Equals(".txt", StringComparison.OrdinalIgnoreCase)
                                     && p.Contains("Stats", StringComparison.OrdinalIgnoreCase)))
            {
                files++;
                try
                {
                    foreach (var entry in ParseStats(File.ReadLines(file)))
                    {
                        statsByName[entry.Name] = entry;
                        entrySources[entry.Name] = SourceOf(root, sourceOverride, file);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    errors++;
                    if (warnings.Count < 15) warnings.Add($"{Path.GetFileName(file)}: {ex.Message}");
                }
            }
        }
        Dictionary<string, string> ResolveStats(StatEntry entry, HashSet<string> visiting)
        {
            if (!visiting.Add(entry.Name)) return new(StringComparer.OrdinalIgnoreCase);
            var values = !string.IsNullOrWhiteSpace(entry.Parent) && statsByName.TryGetValue(entry.Parent, out var parent)
                ? ResolveStats(parent, visiting)
                : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (key, val) in entry.Values) values[key] = val;
            visiting.Remove(entry.Name);
            return values;
        }
        foreach (var entry in statsByName.Values)
        {
            var values = ResolveStats(entry, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            if (!values.TryGetValue("RootTemplate", out string? uuid) || !Guid.TryParse(uuid, out _)) continue;
            if (!items.TryGetValue(uuid, out var item))
            {
                item = new ItemRecord
                {
                    Uuid = uuid, Source = entrySources[entry.Name],
                    Description = "Aus Stats-RootTemplate-Zuordnung; Spawnfähigkeit noch nicht verifiziert."
                };
                items[uuid] = item;
            }
            if (string.IsNullOrWhiteSpace(item.TechnicalName)) item.TechnicalName = entry.Name;
            item.StatsName = entry.Name;
            if (!string.IsNullOrWhiteSpace(entry.Type)) item.Category = entry.Type;
            if (string.IsNullOrWhiteSpace(item.NameHandle)) item.NameHandle = values.GetValueOrDefault("DisplayName", "");
            if (string.IsNullOrWhiteSpace(item.DescriptionHandle)) item.DescriptionHandle = values.GetValueOrDefault("Description", "");
            if (string.IsNullOrWhiteSpace(item.IconId)) item.IconId = values.GetValueOrDefault("Icon", "");
            item.Properties = values.Where(pair => pair.Key is not "RootTemplate" and not "DisplayName" and not "Description" and not "Icon")
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        }
        var localizations = LocalizationService.Read(roots.Select(r => r.root));
        errors += localizations.Errors;
        warnings.AddRange(localizations.Warnings.Take(Math.Max(0, 15 - warnings.Count)));
        int localizedNames = 0;
        foreach (var item in items.Values)
        {
            item.NameDe = localizations.Resolve(item.NameHandle, "German");
            item.NameEn = localizations.Resolve(item.NameHandle, "English");
            item.DescriptionDe = localizations.Resolve(item.DescriptionHandle, "German");
            item.DescriptionEn = localizations.Resolve(item.DescriptionHandle, "English");
            if (!string.IsNullOrWhiteSpace(item.NameDe) || !string.IsNullOrWhiteSpace(item.NameEn)) localizedNames++;
        }
        var iconResult = IconService.LinkIcons(items.Values, roots.Select(r => r.root));
        errors += iconResult.ConversionErrors;
        warnings.AddRange(iconResult.Warnings.Take(Math.Max(0, 20 - warnings.Count)));
        Directory.CreateDirectory(CatalogService.DataDirectory);
        File.WriteAllText(OutputPath, JsonSerializer.Serialize(items.Values.OrderBy(i => i.Source).ThenBy(i => i.DisplayName),
            new JsonSerializerOptions { WriteIndented = true }));
        return new ImportResult(files, items.Count, errors, localizations.Files, localizedNames, iconResult.Linked, warnings);
    }

    private static string ReadSource(string dir)
    {
        string path = Path.Combine(dir, "source.txt");
        try { return File.Exists(path) ? File.ReadAllText(path).Trim() : Path.GetFileName(dir); }
        catch (IOException) { return Path.GetFileName(dir); }
    }

    private static int SourceOrder(string source)
    {
        if (source.StartsWith("BG3 · Patch", StringComparison.OrdinalIgnoreCase)) return 1;
        if (source.StartsWith("BG3 ·", StringComparison.OrdinalIgnoreCase)) return 0;
        if (source.StartsWith("Mod ·", StringComparison.OrdinalIgnoreCase)) return 2;
        return 3;
    }

    private static string SourceOf(string root, string sourceOverride, string path)
    {
        if (!string.IsNullOrWhiteSpace(sourceOverride)) return sourceOverride;
        string relative = Path.GetRelativePath(root, path);
        var separator = relative.IndexOf(Path.DirectorySeparatorChar);
        return separator >= 0 ? relative[..separator] : "Manueller LSX-Import";
    }

    internal sealed record StatEntry(string Name, string Type, string Parent, Dictionary<string, string> Values);
    internal static List<StatEntry> ParseStats(IEnumerable<string> lines)
    {
        var result = new List<StatEntry>();
        string? currentName = null, type = "", parent = "";
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        void Flush()
        {
            if (!string.IsNullOrWhiteSpace(currentName)) result.Add(new StatEntry(currentName, type, parent, values));
        }
        foreach (string line in lines)
        {
            Match entry = StatsEntry.Match(line);
            if (entry.Success)
            {
                Flush();
                currentName = entry.Groups["id"].Value;
                type = "";
                parent = "";
                values = new(StringComparer.OrdinalIgnoreCase);
                continue;
            }
            if (currentName is null) continue;
            Match u = StatsUsing.Match(line);
            if (u.Success) { parent = u.Groups["id"].Value; continue; }
            Match t = StatsType.Match(line);
            if (t.Success) { type = t.Groups["type"].Value; continue; }
            Match data = StatsData.Match(line);
            if (data.Success) values[data.Groups["key"].Value] = data.Groups["val"].Value;
        }
        Flush();
        return result;
    }
}
