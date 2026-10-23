using System.Text.Json;

namespace BG3ItemManager.Services;

public static class DivineToolSettings
{
    private sealed class Settings { public string DivinePath { get; set; } = ""; }
    private static string ConfigPath => Path.Combine(CatalogService.DataDirectory, "tool-settings.json");

    public static string? FindDivine()
    {
        string? saved = null;
        try
        {
            if (File.Exists(ConfigPath))
                saved = JsonSerializer.Deserialize<Settings>(File.ReadAllText(ConfigPath))?.DivinePath;
        }
        catch (JsonException) { /* damaged config: fall back to standard locations */ }
        var candidates = new[]
        {
            saved,
            Environment.GetEnvironmentVariable("BG3_DIVINE_PATH"),
            Path.Combine(AppContext.BaseDirectory, "Tools", "Divine.exe"),
            Path.Combine(AppContext.BaseDirectory, "Tools", "LSLib", "Divine.exe"),
            Path.Combine(AppContext.BaseDirectory, "Tools", "LSLib", "Tools", "Divine.exe")
        };
        return candidates.FirstOrDefault(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path));
    }

    public static void SavePath(string path)
    {
        if (!File.Exists(path) || !string.Equals(Path.GetFileName(path), "Divine.exe", StringComparison.OrdinalIgnoreCase))
            throw new FileNotFoundException("Bitte die Datei Divine.exe aus LSLib auswählen.");
        Directory.CreateDirectory(CatalogService.DataDirectory);
        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(new Settings { DivinePath = Path.GetFullPath(path) }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
