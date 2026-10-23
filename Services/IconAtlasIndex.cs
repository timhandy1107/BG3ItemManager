using System.Globalization;
using System.Xml.Linq;

namespace BG3ItemManager.Services;

/// <summary>Pure XML reader for BG3 TextureAtlasInfo/IconUV definitions. No image/OS dependencies.</summary>
public static class IconAtlasIndex
{
    public sealed record Entry(string IconId, string AtlasPath, double U1, double V1, double U2, double V2,
        string DefinitionPath);

    public static IReadOnlyList<Entry> ReadFile(string file)
    {
        var doc = XDocument.Load(file, LoadOptions.None);
        // One atlas per standard Icons_Items.lsx; multiple TextureAtlasInfo regions may also be present.
        var entries = new List<Entry>();
        var regions = doc.Descendants().Where(e => e.Name.LocalName == "region"
                    && string.Equals((string?)e.Attribute("id"), "TextureAtlasInfo", StringComparison.OrdinalIgnoreCase)).ToList();
        var paths = regions.SelectMany(r => r.Descendants().Where(e => IsNode(e, "TextureAtlasPath")))
            .Select(e => Attr(e, "Path")).Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (paths.Count != 1) return entries; // Ambiguous mapping: never silently assign the wrong texture.
        // Some BG3/mod atlases store the UV entries in a separate IconUVList region.
        foreach (var uv in doc.Descendants().Where(e => IsNode(e, "IconUV")))
        {
            string id = Attr(uv, "MapKey");
            if (string.IsNullOrWhiteSpace(id) || !TryUv(uv, "U1", out double u1)
                || !TryUv(uv, "U2", out double u2) || !TryUv(uv, "V1", out double v1)
                || !TryUv(uv, "V2", out double v2)) continue;
            if (!(u1 >= 0 && v1 >= 0 && u2 <= 1 && v2 <= 1 && u2 > u1 && v2 > v1)) continue;
            entries.Add(new Entry(id, paths[0], u1, v1, u2, v2, file));
        }
        return entries;
    }

    /// <summary>Compute pixel bounds from normalised UVs, using image's actual size (not nominal atlas metadata).</summary>
    public static (int X, int Y, int Width, int Height)? PixelBounds(Entry entry, int width, int height)
    {
        if (width < 1 || height < 1) return null;
        int x1 = Math.Clamp((int)Math.Round(entry.U1 * width, MidpointRounding.AwayFromZero), 0, width);
        int y1 = Math.Clamp((int)Math.Round(entry.V1 * height, MidpointRounding.AwayFromZero), 0, height);
        int x2 = Math.Clamp((int)Math.Round(entry.U2 * width, MidpointRounding.AwayFromZero), 0, width);
        int y2 = Math.Clamp((int)Math.Round(entry.V2 * height, MidpointRounding.AwayFromZero), 0, height);
        return x2 > x1 && y2 > y1 ? (x1, y1, x2 - x1, y2 - y1) : null;
    }

    private static bool IsNode(XElement e, string type) => e.Name.LocalName == "node"
        && string.Equals((string?)e.Attribute("id"), type, StringComparison.OrdinalIgnoreCase);
    private static string Attr(XElement node, string key) => node.Elements()
        .Where(e => e.Name.LocalName == "attribute" && string.Equals((string?)e.Attribute("id"), key, StringComparison.OrdinalIgnoreCase))
        .Select(e => (string?)e.Attribute("value") ?? "").LastOrDefault() ?? "";
    private static bool TryUv(XElement node, string key, out double value)
        => double.TryParse(Attr(node, key), NumberStyles.Float, CultureInfo.InvariantCulture, out value)
           && double.IsFinite(value);
}
