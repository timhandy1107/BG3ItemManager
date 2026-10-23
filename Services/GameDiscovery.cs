using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace BG3ItemManager.Services;

/// <summary>
/// Discovers the installed BG3 data directory. Reads only local game files; never alters them.
/// The package allowlist deliberately excludes multi-gigabyte media and texture archives.
/// </summary>
public static class GameDiscovery
{
    private sealed class Settings { public string InstallationPath { get; set; } = ""; }
    private static string SettingsPath => Path.Combine(CatalogService.DataDirectory, "game-settings.json");
    private static readonly Regex SteamLibrary = new("\\\"path\\\"\\s+\\\"(?<path>[^\\\"]+)\\\"", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string? FindInstallPath()
    {
        string? saved = null;
        try
        {
            if (File.Exists(SettingsPath))
                saved = JsonSerializer.Deserialize<Settings>(File.ReadAllText(SettingsPath))?.InstallationPath;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { }

        var candidates = new List<string?>
        {
            saved,
            Environment.GetEnvironmentVariable("BG3_INSTALL_DIR")
        };
        if (OperatingSystem.IsWindows())
        {
            // Steam's installation path is not necessarily on C: (additional Steam libraries).
            string? steamPath = null;
            try { steamPath = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string; }
            catch (Exception e) when (e is UnauthorizedAccessException or IOException) { }
            try { steamPath ??= Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", null) as string; }
            catch (Exception e) when (e is UnauthorizedAccessException or IOException) { }
            if (!string.IsNullOrWhiteSpace(steamPath)) AddSteamLibraries(steamPath, candidates);
            foreach (string drive in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady).Select(d => d.RootDirectory.FullName))
            {
                candidates.Add(Path.Combine(drive, "Program Files (x86)", "Steam", "steamapps", "common", "Baldurs Gate 3"));
                candidates.Add(Path.Combine(drive, "SteamLibrary", "steamapps", "common", "Baldurs Gate 3"));
                candidates.Add(Path.Combine(drive, "GOG Games", "Baldurs Gate 3"));
            }
        }
        return candidates.Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => NormalizeInstallPath(s!))
            .FirstOrDefault(path => path is not null);
    }

    public static string? NormalizeInstallPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try
        {
            string full = Path.GetFullPath(path.Trim().Trim('"'));
            if (Path.GetFileName(full).Equals("Data", StringComparison.OrdinalIgnoreCase)) full = Directory.GetParent(full)?.FullName ?? full;
            return Directory.Exists(Path.Combine(full, "Data")) && File.Exists(Path.Combine(full, "Data", "Shared.pak"))
                ? full : null;
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or IOException or UnauthorizedAccessException) { return null; }
    }

    public static void SaveInstallPath(string path)
    {
        string normalized = NormalizeInstallPath(path) ?? throw new DirectoryNotFoundException("Kein BG3-Spielordner gefunden (erwartet: Data\\Shared.pak). Bitte den Ordner 'Baldurs Gate 3' auswählen.");
        Directory.CreateDirectory(CatalogService.DataDirectory);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(new Settings { InstallationPath = normalized }, new JsonSerializerOptions { WriteIndented = true }));
    }

    public static IReadOnlyList<string> FindCorePackages(string? installPath)
    {
        if (installPath is null) return Array.Empty<string>();
        string directory = Path.Combine(installPath, "Data");
        if (!Directory.Exists(directory)) return Array.Empty<string>();

        var coreNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "Shared.pak", "Gustav.pak", "GustavDev.pak", "Game.pak", "Icons.pak" };
        var packages = Directory.EnumerateFiles(directory, "*.pak", SearchOption.TopDirectoryOnly)
            .Where(f => coreNames.Contains(Path.GetFileName(f)) || Path.GetFileName(f).StartsWith("Patch", StringComparison.OrdinalIgnoreCase))
            // Put the main resources first, then patches. PAK archives with large videos/textures are intentionally excluded.
            .OrderBy(p => Path.GetFileName(p).StartsWith("Patch", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
            .ThenBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();

        string localization = Path.Combine(directory, "Localization");
        foreach (var language in new[] { "English.pak", "German.pak" })
        {
            var path = Path.Combine(localization, language);
            if (File.Exists(path)) packages.Add(path);
        }
        return packages;
    }

    private static void AddSteamLibraries(string steamPath, List<string?> candidates)
    {
        string root = steamPath.Replace('/', Path.DirectorySeparatorChar);
        var libraries = new List<string> { root };
        var vdf = Path.Combine(root, "steamapps", "libraryfolders.vdf");
        try
        {
            if (File.Exists(vdf))
                libraries.AddRange(SteamLibrary.Matches(File.ReadAllText(vdf))
                    .Select(m => m.Groups["path"].Value.Replace("\\\\", "\\").Replace('/', Path.DirectorySeparatorChar)));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        foreach (string library in libraries.Distinct(StringComparer.OrdinalIgnoreCase))
            candidates.Add(Path.Combine(library, "steamapps", "common", "Baldurs Gate 3"));
    }
}
