using System.Text.Json;

namespace BG3ItemManager.Services;

/// <summary>Local path to Microsoft's DirectXTex texconv.exe; no downloaded executables are run automatically.</summary>
public static class TexconvToolSettings
{
    private sealed class Settings { public string TexconvPath { get; set; } = ""; }
    private static string ConfigPath => Path.Combine(CatalogService.DataDirectory, "texconv-settings.json");

    public static string? FindTexconv()
    {
        string? saved = null;
        try
        {
            if (File.Exists(ConfigPath))
                saved = JsonSerializer.Deserialize<Settings>(File.ReadAllText(ConfigPath))?.TexconvPath;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
        string[] candidates =
        {
            saved ?? "", Environment.GetEnvironmentVariable("BG3_TEXCONV_PATH") ?? "",
            Path.Combine(AppContext.BaseDirectory, "Tools", "texconv.exe"),
            Path.Combine(AppContext.BaseDirectory, "texconv.exe")
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    public static void SavePath(string path)
    {
        if (!File.Exists(path) || !Path.GetFileName(path).Equals("texconv.exe", StringComparison.OrdinalIgnoreCase))
            throw new FileNotFoundException("Bitte die Datei texconv.exe aus Microsoft DirectXTex auswählen.");
        Directory.CreateDirectory(CatalogService.DataDirectory);
        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(new Settings { TexconvPath = Path.GetFullPath(path) }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
