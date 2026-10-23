using System.Text;
using System.Xml.Linq;

namespace BG3ItemManager.Services;

/// <summary>Loads user-supplied/extracted BG3 localisation files. Does not modify BG3 data.</summary>
public static class LocalizationService
{
    public sealed class Localizations
    {
        public Dictionary<string, string> German { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> English { get; } = new(StringComparer.OrdinalIgnoreCase);
        public int Files { get; internal set; }
        public int Errors { get; internal set; }
        public List<string> Warnings { get; } = new();

        public string Resolve(string raw, string language)
        {
            string handle = NormalizeHandle(raw);
            if (string.IsNullOrWhiteSpace(handle)) return "";
            var lookup = language.Equals("German", StringComparison.OrdinalIgnoreCase) ? German : English;
            if (lookup.TryGetValue(handle, out var value)) return value;
            // A literal name is not necessarily a localization handle.
            return LooksLikeHandle(handle) ? "" : handle;
        }
    }

    public static string NormalizeHandle(string? raw)
    {
        string value = (raw ?? "").Trim();
        int sep = value.LastIndexOf(';');
        if (sep > 0 && int.TryParse(value[(sep + 1)..], out _)) value = value[..sep];
        return value;
    }

    private static bool LooksLikeHandle(string handle) =>
        handle.StartsWith("ls::TranslatedString", StringComparison.OrdinalIgnoreCase) ||
        (handle.Length >= 20 && handle[0] is 'h' or 'H' && handle.Skip(1).All(c => char.IsAsciiLetterOrDigit(c)));

    public static Localizations Read(IEnumerable<string> roots)
    {
        var result = new Localizations();
        foreach (var root in roots)
        {
            if (!Directory.Exists(root)) continue;
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                         .Where(IsLocalizationFile).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                var language = LanguageFromPath(file);
                if (language is null) continue;
                result.Files++;
                try
                {
                    var destination = language == "German" ? result.German : result.English;
                    if (file.EndsWith(".loca", StringComparison.OrdinalIgnoreCase)) ReadLoca(file, destination);
                    else ReadXml(file, destination);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException or InvalidDataException or ArgumentException)
                {
                    result.Errors++;
                    if (result.Warnings.Count < 15) result.Warnings.Add($"Lokalisierung {Path.GetFileName(file)}: {ex.Message}");
                }
            }
        }
        return result;
    }

    private static bool IsLocalizationFile(string path)
    {
        var extension = Path.GetExtension(path);
        return path.Contains("Localization", StringComparison.OrdinalIgnoreCase) &&
               (extension.Equals(".loca", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".xml", StringComparison.OrdinalIgnoreCase));
    }

    private static string? LanguageFromPath(string path)
    {
        // Only accept a language path segment or filename prefix, not arbitrary substring matches.
        var parts = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        foreach (string part in parts.Reverse())
        {
            if (part.Equals("German", StringComparison.OrdinalIgnoreCase) || part.StartsWith("German.", StringComparison.OrdinalIgnoreCase) || part.StartsWith("German_", StringComparison.OrdinalIgnoreCase)) return "German";
            if (part.Equals("English", StringComparison.OrdinalIgnoreCase) || part.StartsWith("English.", StringComparison.OrdinalIgnoreCase) || part.StartsWith("English_", StringComparison.OrdinalIgnoreCase)) return "English";
        }
        return null;
    }

    private static void ReadXml(string path, Dictionary<string, string> output)
    {
        var document = XDocument.Load(path, LoadOptions.None);
        foreach (var content in document.Descendants().Where(e => e.Name.LocalName == "content"))
        {
            string? handle = (string?)content.Attribute("contentuid");
            if (!string.IsNullOrWhiteSpace(handle)) output[handle] = content.Value.Trim();
        }
    }

    // LOCA v1: magic(4), count(u32), textOffset(u32), count * [key(64),version(u16),length(u32)], UTF-8 text blob.
    private static void ReadLoca(string path, Dictionary<string, string> output)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: false);
        if (stream.Length < 12 || Encoding.ASCII.GetString(reader.ReadBytes(4)) != "LOCA")
            throw new InvalidDataException("LOCA-Kopf ungültig");
        uint count = reader.ReadUInt32();
        uint offset = reader.ReadUInt32();
        const int rowSize = 70;
        if (count > 2_000_000 || offset < 12L + (long)count * rowSize || offset > stream.Length)
            throw new InvalidDataException("LOCA-Index außerhalb der Dateigrenzen");
        var records = new List<(string Key, uint Length)>((int)count);
        long totalLength = 0;
        for (int i = 0; i < count; i++)
        {
            var keyBytes = reader.ReadBytes(64);
            if (keyBytes.Length != 64) throw new InvalidDataException("LOCA-Index unvollständig");
            string key = Encoding.UTF8.GetString(keyBytes).TrimEnd('\0');
            _ = reader.ReadUInt16(); // language-specific version of translated string
            uint length = reader.ReadUInt32();
            totalLength += length;
            if (totalLength > stream.Length - offset) throw new InvalidDataException("LOCA-Textdaten unvollständig");
            records.Add((key, length));
        }
        stream.Position = offset;
        foreach (var row in records)
        {
            if (row.Length > 16_777_216) throw new InvalidDataException("LOCA-Eintrag zu groß");
            var bytes = reader.ReadBytes((int)row.Length);
            if (bytes.Length != row.Length) throw new InvalidDataException("LOCA-Eintrag abgeschnitten");
            if (row.Key.Length > 0) output[row.Key] = Encoding.UTF8.GetString(bytes).TrimEnd('\0');
        }
    }
}
