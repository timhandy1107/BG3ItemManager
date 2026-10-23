namespace BG3ItemManager.Services;
public static class ModDiscovery
{
    public static string ModDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Larian Studios", "Baldur's Gate 3", "Mods");
    public static IReadOnlyList<string> FindPackages() => Directory.Exists(ModDirectory)
        ? Directory.EnumerateFiles(ModDirectory, "*.pak", SearchOption.TopDirectoryOnly).OrderBy(x => x).ToList()
        : Array.Empty<string>();
    public static IReadOnlyList<string> FindNewPackages() => FindPackages().Select(Path.GetFileNameWithoutExtension).Where(x => x != null).Select(x => x!).ToList();
}
