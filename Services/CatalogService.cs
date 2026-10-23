using System.Text.Json;
using BG3ItemManager.Models;

namespace BG3ItemManager.Services;

public static class CatalogService
{
    public static string DataDirectory => Path.Combine(AppContext.BaseDirectory, "Data");
    public static string CatalogPath => Path.Combine(DataDirectory, "items.json");

    public static List<ItemRecord> Load()
    {
        Directory.CreateDirectory(DataDirectory);
        if (!File.Exists(CatalogPath)) File.WriteAllText(CatalogPath, "[]");
        var records = new Dictionary<string, ItemRecord>(StringComparer.OrdinalIgnoreCase);
        // Imported data is the base; manually maintained items.json takes precedence.
        foreach (string path in new[] { ExtractedModImporter.OutputPath, CatalogPath })
        {
            if (!File.Exists(path)) continue;
            try
            {
                var imported = JsonSerializer.Deserialize<List<ItemRecord>>(File.ReadAllText(path),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
                foreach (var item in imported)
                    if (Guid.TryParse(item.Uuid, out _)) records[item.Uuid] = item;
            }
            catch (JsonException e) { throw new InvalidDataException($"Ungültige JSON-Datei: {path}", e); }
        }
        return records.Values.OrderBy(x => x.DisplayName).ToList();
    }
}
