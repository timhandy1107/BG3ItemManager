using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BG3ItemManager.Services;

/// <summary>
/// Imports BG3 core resource archives and user-installed mods using the external Norbyte LSLib Divine.exe.
/// Never writes into the game's installation, mod folder or savegames.
/// </summary>
public static class ModArchiveImporter
{
    public static string CacheDirectory => Path.Combine(CatalogService.DataDirectory, "ModCache");
    private static string StatePath => Path.Combine(CacheDirectory, "import-state.json");

    private sealed class CacheRecord
    {
        public long Length { get; set; }
        public long LastWriteUtcTicks { get; set; }
        public bool Converted { get; set; }
    }

    public sealed record ImportResult(int PackagesFound, int PackagesProcessed, int PackagesSkipped,
        int ConversionWarnings, int Templates, int ParsedFiles, int LocalizationFiles,
        int LocalizedNames, int Icons, int Errors, IReadOnlyList<string> Warnings);

    public static async Task<ImportResult> ImportAsync(IProgress<string>? progress = null, CancellationToken cancellation = default)
    {
        string divine = DivineToolSettings.FindDivine()
            ?? throw new FileNotFoundException("LSLib/Divine.exe fehlt. Über 'Divine.exe auswählen' einmalig den Pfad festlegen.");
        Directory.CreateDirectory(CacheDirectory);
        var state = LoadState();
        // Only a small, explicitly selected list of gameplay and localization packages is read.
        // Video, voices, textures, virtual textures and animations are never extracted.
        var install = GameDiscovery.FindInstallPath();
        var packages = GameDiscovery.FindCorePackages(install)
            .Concat(ModDiscovery.FindPackages())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        int processed = 0, skipped = 0, conversionWarnings = 0, errors = 0;
        var warnings = new List<string>();
        foreach (var package in packages)
        {
            cancellation.ThrowIfCancellationRequested();
            var info = new FileInfo(package);
            // Use full path, not just the file name, for the cache identity.
            string identity = Path.GetFullPath(package);
            string folder = Path.GetFileNameWithoutExtension(package) + "_" +
                Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..12];
            string target = Path.Combine(CacheDirectory, folder);
            if (state.TryGetValue(identity, out var last)
                && last.Length == info.Length && last.LastWriteUtcTicks == info.LastWriteTimeUtc.Ticks
                && last.Converted && Directory.Exists(target))
            {
                skipped++;
                continue;
            }
            progress?.Report($"Entpacke: {info.Name}");
            string staging = Path.Combine(CacheDirectory, "_import_" + Guid.NewGuid().ToString("N"));
            string extracted = Path.Combine(staging, "extracted");
            string converted = Path.Combine(staging, "converted");
            try
            {
                Directory.CreateDirectory(extracted);
                await RunDivine(divine, cancellation, "-g", "bg3", "-a", "extract-package", "-s", package, "-d", extracted);
                bool convertedSuccessfully = true;
                var resources = Directory.EnumerateFiles(extracted, "*", SearchOption.AllDirectories)
                    .Count(f => string.Equals(Path.GetExtension(f), ".lsf", StringComparison.OrdinalIgnoreCase));
                if (resources > 0)
                {
                    progress?.Report($"Konvertiere {resources} LSF-Dateien: {info.Name}");
                    Directory.CreateDirectory(converted);
                    try
                    {
                        await RunDivine(divine, cancellation, "-g", "bg3", "-a", "convert-resources",
                            "--input-format", "lsf", "--output-format", "lsx", "-s", extracted, "-d", converted);
                    }
                    catch (Exception e) when (e is IOException or InvalidOperationException)
                    {
                        conversionWarnings++;
                        convertedSuccessfully = false;
                        if (warnings.Count < 15) warnings.Add($"{info.Name}: LSF-Konvertierung unvollständig ({e.Message}). LSX/TXT werden trotzdem gelesen.");
                    }
                }
                bool fromGame = install is not null && Path.GetFullPath(package).StartsWith(
                    Path.GetFullPath(Path.Combine(install, "Data")) + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase);
                string sourceName = fromGame ? "BG3 · " + Path.GetFileNameWithoutExtension(package)
                                                 : "Mod · " + Path.GetFileNameWithoutExtension(package);
                File.WriteAllText(Path.Combine(staging, "source.txt"), sourceName);
                // Keep the old successful cache until new extraction succeeds.
                if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
                Directory.Move(staging, target);
                state[identity] = new CacheRecord { Length = info.Length, LastWriteUtcTicks = info.LastWriteTimeUtc.Ticks, Converted = convertedSuccessfully };
                SaveState(state);
                processed++;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                errors++;
                if (warnings.Count < 15) warnings.Add($"{info.Name}: {e.Message}");
            }
            finally
            {
                try { if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true); }
                catch (IOException) { /* best effort clean-up */ }
            }
        }
        progress?.Report("Lese extrahierte Gegenstände ...");
        var catalog = await ExtractedModImporter.ImportAsync(cancellation);
        warnings.AddRange(catalog.Warnings.Take(Math.Max(0, 20 - warnings.Count)));
        return new ImportResult(packages.Count, processed, skipped, conversionWarnings,
            catalog.Items, catalog.Files, catalog.LocalizationFiles, catalog.LocalizedNames, catalog.Icons,
            errors + catalog.Errors, warnings);
    }

    private static Dictionary<string, CacheRecord> LoadState()
    {
        try
        {
            if (File.Exists(StatePath))
                return JsonSerializer.Deserialize<Dictionary<string, CacheRecord>>(File.ReadAllText(StatePath))
                    ?? new(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception e) when (e is IOException or JsonException) { /* rebuild index */ }
        return new(StringComparer.OrdinalIgnoreCase);
    }

    private static void SaveState(Dictionary<string, CacheRecord> state)
    {
        string temporary = StatePath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, StatePath, overwrite: true);
    }

    private static async Task RunDivine(string exe, CancellationToken token, params string[] args)
    {
        using var process = new Process { StartInfo = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = Path.GetDirectoryName(exe) ?? AppContext.BaseDirectory
        } };
        foreach (string arg in args) process.StartInfo.ArgumentList.Add(arg);
        if (!process.Start()) throw new IOException("Divine.exe konnte nicht gestartet werden.");
        try { await process.WaitForExitAsync(token); }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            throw;
        }
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"Divine.exe hat den Fehlercode {process.ExitCode} zurückgegeben.");
    }
}
