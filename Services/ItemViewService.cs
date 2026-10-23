using BG3ItemManager.Models;

namespace BG3ItemManager.Services;

/// <summary>Darstellung und Filterung vorhandener Importdaten ohne erneutes Entpacken.</summary>
public static class ItemViewService
{
    public static bool HasReadableName(ItemRecord item)
    {
        string label = !string.IsNullOrWhiteSpace(item.NameDe) ? item.NameDe.Trim() : item.NameEn.Trim();
        if (label.Length == 0) return false;
        if (label.Equals(item.Uuid, StringComparison.OrdinalIgnoreCase)) return false;
        // Template IDs that happen to be used as fallback labels are not player-facing names.
        if ((label.Equals(item.TechnicalName, StringComparison.OrdinalIgnoreCase) ||
             label.Equals(item.StatsName, StringComparison.OrdinalIgnoreCase)) &&
            (label.Contains('_') || label.StartsWith("S_", StringComparison.OrdinalIgnoreCase)))
            return false;
        return true;
    }

    public static string DisplayCategory(ItemRecord item)
    {
        string raw = (item.Category ?? "").Trim();
        string rawLower = raw.ToLowerInvariant();
        string name = (item.TechnicalName + " " + item.StatsName + " " + item.NameEn + " " + item.NameDe).ToLowerInvariant();
        string stats = item.StatsName.ToLowerInvariant();
        bool Has(string value) => name.Contains(value, StringComparison.OrdinalIgnoreCase);
        bool HasProp(string value) => item.Properties?.ContainsKey(value) == true;

        if (rawLower.Contains("weapon") || HasProp("WeaponType") || HasProp("WeaponProperties") || 
            Has("wpn_") || Has("sword") || Has("greatsword") || Has("bow") || Has("dagger")) return "Waffen";
        if (rawLower.Contains("armor") || rawLower.Contains("armour") || HasProp("ArmorType") ||
            Has("arm_") || Has("armour") || Has("armor") || Has("shield")) return "Rüstungen & Schilde";
        if (Has("amulet") || Has("necklace") || Has("ring_") || Has("ring of") || Has("circlet") ||
            rawLower.Contains("jewelry")) return "Schmuck";
        if (rawLower.Contains("potion") || Has("potion") || Has("elixir") || Has("poison") || Has("grenade")) return "Tränke & Verbrauchsgüter";
        if (rawLower.Contains("scroll") || Has("scroll")) return "Schriftrollen";
        if (rawLower.Contains("food") || Has("food") || Has("camp_supply")) return "Nahrung";
        if (rawLower.Contains("book") || Has("book_") || Has("note_") || Has("letter_")) return "Bücher & Dokumente";
        if (rawLower.Contains("key") || Has("key_")) return "Schlüssel";
        if (rawLower.Contains("clothing") || Has("cloth_") || Has("robe")) return "Kleidung";
        if (rawLower is "object" or "item" or "equipment") return "Sonstige Gegenstände";
        if (raw.Length == 0 || rawLower is "unklassifiziert" or "sonstiges") return "Unklassifiziert";
        return raw;
    }
}
