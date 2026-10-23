using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Media.Imaging;
using BG3ItemManager.Models;

namespace BG3ItemManager.Services;

/// <summary>
/// Resolves real PNG/JPEG icons or extracts BG3 icon tiles from TextureAtlasInfo + DDS using texconv.
/// Only icon images referenced by imported item templates are cropped into Data/IconCache.
/// </summary>
public static class IconService
{
    private static readonly HashSet<string> ImageExt = new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg" };
    public sealed record Result(int Linked, int AtlasIcons, int ConversionErrors, IReadOnlyList<string> Warnings);

    public static Result LinkIcons(IEnumerable<ItemRecord> records, IEnumerable<string> sourceRoots)
    {
        var items = records.Where(i => !string.IsNullOrWhiteSpace(i.IconId)).ToList();
        var roots = sourceRoots.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var warnings = new List<string>();
        if (items.Count == 0 || roots.Length == 0) return new Result(0, 0, 0, warnings);

        string dataRoot = Path.GetFullPath(CatalogService.DataDirectory);
        string imageCache = Path.Combine(dataRoot, "IconCache");
        Directory.CreateDirectory(imageCache);
        var direct = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var atlasEntries = new Dictionary<string, (IconAtlasIndex.Entry entry, string image)>(StringComparer.OrdinalIgnoreCase);
        var globalImages = new Dictionary<string, List<(string virtualPath, string absolutePath)>>(StringComparer.OrdinalIgnoreCase);
        var definitions = new List<IconAtlasIndex.Entry>();

        // Pass 1: collect textures across ALL game/patch/mod packages. Base game atlas metadata
        // and its DDS texture often live in different PAK archives.
        foreach (string root in roots)
        {
            foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                string extension = Path.GetExtension(file);
                if (ImageExt.Contains(extension) || extension.Equals(".dds", StringComparison.OrdinalIgnoreCase))
                {
                    string stem = Path.GetFileNameWithoutExtension(file);
                    if (!globalImages.TryGetValue(stem, out var group))
                    {
                        group = new();
                        globalImages[stem] = group;
                    }
                    group.Add((Path.GetRelativePath(root, file).Replace('\\', '/'), file));
                    if (ImageExt.Contains(extension)) direct[stem] = file;
                }
            }
            foreach (string file in Directory.EnumerateFiles(root, "*.lsx", SearchOption.AllDirectories)
                         .Where(p => Path.GetFileName(p).Contains("Icon", StringComparison.OrdinalIgnoreCase)
                                  || p.Contains("GUI", StringComparison.OrdinalIgnoreCase)))
            {
                try { definitions.AddRange(IconAtlasIndex.ReadFile(file)); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Xml.XmlException)
                {
                    if (warnings.Count < 12) warnings.Add($"Icon-Atlas {Path.GetFileName(file)}: {e.Message}");
                }
            }
        }
        // Pass 2: resolve to matching game-relative paths. If a patch or mod overrides the
        // same virtual asset path, the later extracted package wins.
        foreach (var uv in definitions)
        {
            var image = FindAtlasImage(uv, globalImages);
            if (image is not null) atlasEntries[uv.IconId] = (uv, image);
        }
        int linked = 0, atlasLinked = 0, errors = 0;
        string? texconv = TexconvToolSettings.FindTexconv();
        var decoded = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        bool needsTexconv = false;
        foreach (var item in items)
        {
            if (direct.TryGetValue(item.IconId, out var directPath) && IsWithin(dataRoot, directPath))
            {
                item.IconPath = Path.GetRelativePath(dataRoot, directPath);
                linked++;
                continue;
            }
            if (!atlasEntries.TryGetValue(item.IconId, out var atlas)) continue;
            string image = atlas.image;
            if (Path.GetExtension(image).Equals(".dds", StringComparison.OrdinalIgnoreCase))
            {
                if (texconv is null) { needsTexconv = true; continue; }
                if (!decoded.TryGetValue(image, out string? converted))
                {
                    try { converted = ConvertDds(image, texconv, imageCache); }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException
                                                 or System.ComponentModel.Win32Exception)
                    {
                        converted = null; errors++;
                        if (warnings.Count < 12) warnings.Add($"DDS {Path.GetFileName(image)}: {e.Message}");
                    }
                    decoded[image] = converted;
                }
                if (converted is null) continue;
                image = converted;
            }
            try
            {
                string iconFile = CropAndCache(atlas.entry, image, imageCache);
                item.IconPath = Path.GetRelativePath(dataRoot, iconFile);
                linked++;
                atlasLinked++;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException
                                         or ArgumentException or FormatException or NotSupportedException or System.Runtime.InteropServices.COMException)
            {
                errors++;
                if (warnings.Count < 12) warnings.Add($"Icon {item.IconId}: {e.Message}");
            }
        }
        if (needsTexconv) warnings.Add("DDS-Icons noch nicht importiert: texconv.exe auswählen (Microsoft DirectXTex).");
        return new Result(linked, atlasLinked, errors, warnings);
    }

    private static string? FindAtlasImage(IconAtlasIndex.Entry entry,
        Dictionary<string, List<(string virtualPath, string absolutePath)>> index)
    {
        string atlas = entry.AtlasPath.Replace('\\', '/').TrimStart('/');
        if (!index.TryGetValue(Path.GetFileNameWithoutExtension(atlas), out var candidates)) return null;
        string normalized = atlas.TrimStart('/');
        string def = entry.DefinitionPath.Replace('\\', '/');
        int gui = def.LastIndexOf("/GUI/", StringComparison.OrdinalIgnoreCase);
        // Typical: .../Public/<Module>/GUI/Icons_Items.lsx references Assets/Textures/.../Atlas.dds.
        var module = gui >= 0 ? def[..gui].Split('/').LastOrDefault() : null;
        string expected = normalized.StartsWith("Public/", StringComparison.OrdinalIgnoreCase)
            ? normalized : module is null ? normalized : $"Public/{module}/{normalized}";
        var matches = candidates.Where(c => c.virtualPath.Equals(expected, StringComparison.OrdinalIgnoreCase)).ToList();
        if (matches.Count > 0) return matches[^1].absolutePath;
        // Some atlas definitions already use an absolute game-relative Assets/... path.
        // Only use a suffix fallback if unambiguous to avoid mixing different mods' icons.
        matches = candidates.Where(c => c.virtualPath.EndsWith("/" + normalized, StringComparison.OrdinalIgnoreCase)
            || c.virtualPath.Equals(normalized, StringComparison.OrdinalIgnoreCase)).ToList();
        var distinctPaths = matches.Select(c => c.virtualPath).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (distinctPaths.Count == 1) return matches[^1].absolutePath;
        return null;
    }

    private static string CropAndCache(IconAtlasIndex.Entry entry, string image, string outputDirectory)
    {
        var file = new FileInfo(image);
        string id = HashKey($"{entry.IconId}|{image}|{file.Length}|{file.LastWriteTimeUtc.Ticks}|" +
            $"{entry.U1:R},{entry.V1:R},{entry.U2:R},{entry.V2:R}");
        string result = Path.Combine(outputDirectory, id + ".png");
        if (File.Exists(result)) return result;
        using var stream = File.OpenRead(image);
        var bitmap = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
        var bounds = IconAtlasIndex.PixelBounds(entry, bitmap.PixelWidth, bitmap.PixelHeight)
            ?? throw new InvalidDataException("Ungültiger Bildausschnitt in IconUV.");
        var cropped = new CroppedBitmap(bitmap, new Int32Rect(bounds.X, bounds.Y, bounds.Width, bounds.Height));
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(cropped));
        string temporary = result + ".tmp";
        try
        {
            using (var destination = File.Create(temporary)) encoder.Save(destination);
            File.Move(temporary, result, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        return result;
    }

    private static string ConvertDds(string dds, string texconv, string cacheDirectory)
    {
        var input = new FileInfo(dds);
        string hash = HashKey($"{dds}|{input.Length}|{input.LastWriteTimeUtc.Ticks}");
        string folder = Path.Combine(cacheDirectory, "_atlases", hash);
        string image = Path.Combine(folder, Path.GetFileNameWithoutExtension(dds) + ".png");
        if (File.Exists(image)) return image;
        Directory.CreateDirectory(folder);
        var start = new ProcessStartInfo(texconv)
        {
            UseShellExecute = false, CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(texconv) ?? AppContext.BaseDirectory
        };
        foreach (string arg in new[] { "-nologo", "-ft", "png", "-y", "-o", folder, dds })
            start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new IOException("texconv.exe konnte nicht gestartet werden.");
        if (!process.WaitForExit(120_000))
        {
            process.Kill(entireProcessTree: true);
            throw new IOException("texconv.exe hat nach 120 Sekunden nicht geantwortet.");
        }
        if (process.ExitCode != 0 || !File.Exists(image))
            throw new IOException($"texconv.exe: Fehlercode {process.ExitCode} bei {Path.GetFileName(dds)}");
        return image;
    }

    private static string HashKey(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..32];

    private static bool IsWithin(string root, string file) =>
        Path.GetFullPath(file).StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);
}
