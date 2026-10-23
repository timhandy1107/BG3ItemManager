namespace BG3ItemManager.Models;
public sealed class ItemRecord
{
    public string Uuid { get; set; } = "";
    public string NameDe { get; set; } = "";
    public string NameEn { get; set; } = "";
    public string TechnicalName { get; set; } = "";
    public string StatsName { get; set; } = "";
    public string IconId { get; set; } = "";
    public string Category { get; set; } = "Sonstiges";
    public string Source { get; set; } = "Unbekannt";
    public string Description { get; set; } = "";
    public string DescriptionDe { get; set; } = "";
    public string DescriptionEn { get; set; } = "";
    public string NameHandle { get; set; } = "";
    public string DescriptionHandle { get; set; } = "";
    public string IconPath { get; set; } = "";
    public Dictionary<string, string> Properties { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    // Absolute local cache path used by WPF thumbnail binding; never allow traversal outside Data.
    public string IconAbsolutePath
    {
        get
        {
            if (string.IsNullOrWhiteSpace(IconPath)) return "";
            try
            {
                string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "Data"));
                string full = Path.GetFullPath(Path.Combine(root, IconPath));
                return full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                    && File.Exists(full) ? full : "";
            }
            catch (Exception e) when (e is ArgumentException or IOException or NotSupportedException) { return ""; }
        }
    }

    public string DisplayCategory => BG3ItemManager.Services.ItemViewService.DisplayCategory(this);
    public string DisplayName => !string.IsNullOrWhiteSpace(NameDe) ? NameDe
        : !string.IsNullOrWhiteSpace(NameEn) ? NameEn
        : !string.IsNullOrWhiteSpace(TechnicalName) ? TechnicalName : Uuid;
    public string DisplayDescription => !string.IsNullOrWhiteSpace(DescriptionDe) ? DescriptionDe
        : !string.IsNullOrWhiteSpace(DescriptionEn) ? DescriptionEn : Description;
    public string PropertyText => Properties is { Count: > 0 }
        ? string.Join(Environment.NewLine, Properties.OrderBy(k => k.Key).Select(k => $"{k.Key}: {k.Value}"))
        : "Keine auslesbaren Gegenstandswerte vorhanden.";
}
