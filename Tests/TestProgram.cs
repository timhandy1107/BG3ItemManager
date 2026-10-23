using System.Text;
using BG3ItemManager.Services;

// Small dependency-free integration test; runs before Windows publishing.
static void Assert(bool test, string error)
{
    if (!test) throw new Exception("Importtest fehlgeschlagen: " + error);
}

// Local installed-game fixture. Do not open/copy real game files in tests.
string fakeGame = Path.Combine(Path.GetTempPath(), "BG3GameFixture_" + Guid.NewGuid().ToString("N"));
try
{
    Directory.CreateDirectory(Path.Combine(fakeGame, "Data", "Localization"));
    foreach (var name in new[] { "Shared.pak", "Icons.pak", "Gustav.pak", "Patch8_HotFix10.pak", "Textures.pak", "Voice.pak" })
        File.WriteAllText(Path.Combine(fakeGame, "Data", name), "fixture");
    File.WriteAllText(Path.Combine(fakeGame, "Data", "Localization", "English.pak"), "fixture");
    Assert(GameDiscovery.NormalizeInstallPath(fakeGame) is not null, "BG3-Spielordner erkennen");
    var selected = GameDiscovery.FindCorePackages(fakeGame).Select(Path.GetFileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
    Assert(selected.SetEquals(new[] { "Shared.pak", "Icons.pak", "Gustav.pak", "Patch8_HotFix10.pak", "English.pak" }), "Nur Item/Icons/Sprachen/Patches einlesen");
    Console.WriteLine("PASS: BG3-Installationsordner und selektive Archivwahl");
}
finally
{
    if (Directory.Exists(fakeGame)) Directory.Delete(fakeGame, true);
}

const string uuid = "11111111-2222-3333-4444-555555555555";
const string titleHandle = "h12345678g1234g1234g1234g123456789abc";
const string descHandle = "h87654321g4321g4321g4321gcba987654321";
string dir = Path.Combine(ExtractedModImporter.ExtractedDirectory, "_IntegrationTest_" + Guid.NewGuid().ToString("N"));
try
{
    Directory.CreateDirectory(Path.Combine(dir, "RootTemplates"));
    Directory.CreateDirectory(Path.Combine(dir, "Stats", "Generated", "Data"));
    Directory.CreateDirectory(Path.Combine(dir, "Localization", "German"));
    Directory.CreateDirectory(Path.Combine(dir, "Localization", "English"));
    Directory.CreateDirectory(Path.Combine(dir, "Images"));
    File.WriteAllText(Path.Combine(dir, "RootTemplates", "item.lsx"),
        $"""<?xml version="1.0"?><save><region id="Templates"><node id="GameObjects"><attribute id="MapKey" type="FixedString" value="{uuid}"/><attribute id="Type" type="FixedString" value="item"/><attribute id="Name" type="LSString" value="TEST_SWORD"/><attribute id="DisplayName" type="TranslatedString" handle="{titleHandle}"/></node></region></save>""");
    File.WriteAllText(Path.Combine(dir, "Stats", "Generated", "Data", "Weapon.txt"),
        $"""
        new entry "BASE_TEST_SWORD"
        type "Weapon"
        data "Rarity" "Legendary"
        data "Boosts" "UnlockSpell(Test)"

        new entry "TEST_SWORD"
        using "BASE_TEST_SWORD"
        type "Weapon"
        data "RootTemplate" "{uuid}"
        data "Icon" "TEST_ICON"
        data "Description" "{descHandle};1"
        """);
    File.WriteAllText(Path.Combine(dir, "Localization", "German", "German.loca.xml"),
        $"<contentList><content contentuid=\"{titleHandle}\" version=\"1\">Testsäbel</content><content contentuid=\"{descHandle}\" version=\"1\">Beschreibung DE</content></contentList>");
    // Binary LOCA fixture exercises UTF-8 and proper index/text-offset layout.
    var english = new[] { (titleHandle, "Test Sabre"), (descHandle, "Description EN") };
    using (var fs = File.Create(Path.Combine(dir, "Localization", "English", "English.loca")))
    using (var writer = new BinaryWriter(fs, Encoding.UTF8))
    {
        writer.Write(Encoding.ASCII.GetBytes("LOCA"));
        writer.Write((uint)english.Length);
        writer.Write((uint)(12 + 70 * english.Length));
        foreach (var (key, value) in english)
        {
            byte[] keyBytes = new byte[64];
            Encoding.UTF8.GetBytes(key).CopyTo(keyBytes, 0);
            writer.Write(keyBytes);
            writer.Write((ushort)1);
            writer.Write((uint)(Encoding.UTF8.GetByteCount(value) + 1));
        }
        foreach (var (_, value) in english)
        {
            writer.Write(Encoding.UTF8.GetBytes(value));
            writer.Write((byte)0);
        }
    }
    // Minimal valid transparent PNG; test only file association, not image decoding.
    File.WriteAllBytes(Path.Combine(dir, "Images", "TEST_ICON.png"),
        Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9YkMYE8AAAAASUVORK5CYII="));
    var result = ExtractedModImporter.Import();
    var item = CatalogService.Load().Single(i => i.Uuid.Equals(uuid, StringComparison.OrdinalIgnoreCase));
    Assert(result.Errors == 0, "Dateifehler: " + string.Join(", ", result.Warnings));
    Assert(item.NameDe == "Testsäbel", "deutscher Name");
    Assert(item.NameEn == "Test Sabre", "englischer Name aus binärer LOCA");
    Assert(item.DescriptionDe == "Beschreibung DE", "deutsche Beschreibung");
    Assert(item.DescriptionEn == "Description EN", "englische Beschreibung");
    Assert(item.Properties.GetValueOrDefault("Rarity") == "Legendary", "geerbte Rarity");
    Assert(item.Properties.GetValueOrDefault("Boosts") == "UnlockSpell(Test)", "geerbte Boosts");
    Assert(item.IconPath.EndsWith("TEST_ICON.png", StringComparison.OrdinalIgnoreCase), "Iconpfad");
    Assert(LocalizationService.NormalizeHandle(descHandle + ";1") == descHandle, "Handle-Version");
    Console.WriteLine("PASS: LSX + Stats-Vererbung + DE-XML + EN-LOCA + Icon-Zuordnung");
}
finally
{
    try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch (IOException) { }
    // Import creates JSON; remove fixture record so it never leaks into the user's catalog.
    if (File.Exists(ExtractedModImporter.OutputPath))
    {
        try
        {
            var all = System.Text.Json.JsonSerializer.Deserialize<List<BG3ItemManager.Models.ItemRecord>>(File.ReadAllText(ExtractedModImporter.OutputPath));
            if (all is not null)
                File.WriteAllText(ExtractedModImporter.OutputPath, System.Text.Json.JsonSerializer.Serialize(all.Where(i => i.Uuid != uuid)));
        }
        catch (Exception) { }
    }
}

// Synthetic atlas regression: no real BG3 files, DDS decoder or texconv installation required.
// The IconUVList region intentionally sits outside TextureAtlasInfo (common BG3 layout).
string atlasFixture = Path.Combine(ExtractedModImporter.ExtractedDirectory, "_AtlasTest_" + Guid.NewGuid().ToString("N"));
string atlasImageRoot = Path.Combine(atlasFixture, "SeparatedImagePackage");
string atlasMetadataRoot = Path.Combine(atlasFixture, "SeparatedMetadataPackage");
string atlasImage = Path.Combine(atlasImageRoot, "Public", "TestAtlas", "Assets", "Textures", "Icons", "AtlasExample.png");
string atlasDefinition = Path.Combine(atlasMetadataRoot, "Public", "TestAtlas", "GUI", "Icons_Items.lsx");
string? generatedTile = null;
try
{
    Directory.CreateDirectory(Path.GetDirectoryName(atlasImage)!);
    Directory.CreateDirectory(Path.GetDirectoryName(atlasDefinition)!);
    File.WriteAllText(atlasDefinition, """
        <save>
          <region id="TextureAtlasInfo"><node id="root"><children>
            <node id="TextureAtlasPath"><attribute id="Path" type="LSString" value="Assets/Textures/Icons/AtlasExample.png"/></node>
          </children></node></region>
          <region id="IconUVList"><node id="root"><children>
            <node id="IconUV">
              <attribute id="MapKey" type="FixedString" value="ATLAS_TEST_ICON"/>
              <attribute id="U1" type="float" value="0.5"/>
              <attribute id="U2" type="float" value="1.0"/>
              <attribute id="V1" type="float" value="0.0"/>
              <attribute id="V2" type="float" value="1.0"/>
            </node>
          </children></node></region>
        </save>
        """);
    var uvEntry = IconAtlasIndex.ReadFile(atlasDefinition).Single();
    Assert(uvEntry.IconId == "ATLAS_TEST_ICON", "IconUV-MapKey");
    Assert(IconAtlasIndex.PixelBounds(uvEntry, 128, 64) is (64, 0, 64, 64), "UV in 64x64-Kachel auflösen");
    RunSta(() =>
    {
        // Left tile red, right tile blue in Bgra32.
        byte[] pixels = new byte[128 * 64 * 4];
        for (int y = 0; y < 64; y++)
        for (int x = 0; x < 128; x++)
        {
            int offset = (y * 128 + x) * 4;
            if (x < 64) pixels[offset + 2] = 255;
            else pixels[offset] = 255;
            pixels[offset + 3] = 255;
        }
        var image = System.Windows.Media.Imaging.BitmapSource.Create(128, 64, 96, 96,
            System.Windows.Media.PixelFormats.Bgra32, null, pixels, 128 * 4);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));
        using (var stream = File.Create(atlasImage)) encoder.Save(stream);

        var record = new BG3ItemManager.Models.ItemRecord { Uuid = Guid.NewGuid().ToString(), IconId = "ATLAS_TEST_ICON" };
        var result = IconService.LinkIcons(new[] { record }, new[] { atlasMetadataRoot, atlasImageRoot });
        Assert(result.Linked == 1 && result.AtlasIcons == 1 && result.ConversionErrors == 0,
            "Icon-Schnitt: " + string.Join("; ", result.Warnings));
        Assert(File.Exists(record.IconAbsolutePath), "geschnittenes PNG wurde gespeichert");
        generatedTile = record.IconAbsolutePath;
        using var croppedFile = File.OpenRead(record.IconAbsolutePath);
        var crop = System.Windows.Media.Imaging.BitmapDecoder.Create(croppedFile,
            System.Windows.Media.Imaging.BitmapCreateOptions.None,
            System.Windows.Media.Imaging.BitmapCacheOption.OnLoad).Frames[0];
        Assert(crop.PixelWidth == 64 && crop.PixelHeight == 64, "Kachelabmessungen 64x64");
        byte[] pixel = new byte[4];
        var bgra = new System.Windows.Media.Imaging.FormatConvertedBitmap(crop,
            System.Windows.Media.PixelFormats.Bgra32, null, 0);
        bgra.CopyPixels(new System.Windows.Int32Rect(0, 0, 1, 1), pixel, 4, 0);
        Assert(pixel[0] == 255 && pixel[2] == 0, "rechte blaue Kachel und nicht das rote Nachbar-Icon");
    });
    Console.WriteLine("PASS: archivuebergreifender IconUVList/Atlas-Import + PNG-Kachel + Bildzuordnung");
}
finally
{
    try { if (Directory.Exists(atlasFixture)) Directory.Delete(atlasFixture, recursive: true); } catch (IOException) { }
    try { if (generatedTile is not null && File.Exists(generatedTile)) File.Delete(generatedTile); } catch (IOException) { }
}

// Local IPC protocol regression: simulate the Script Extender side without starting BG3.
string testBridgeDir = Path.Combine(Path.GetTempPath(), "BG3BridgeFixture_" + Guid.NewGuid().ToString("N"));
try
{
    var bridge = new ExtenderBridgeService(testBridgeDir);
    Directory.CreateDirectory(testBridgeDir);
    Assert(!bridge.IsBridgeAlive(), "Ohne Heartbeat offline");
    File.WriteAllText(bridge.StatusPath, "{\"version\":1,\"ready\":true}");
    Assert(bridge.IsBridgeAlive(), "Aktueller Heartbeat wird erkannt");
    File.SetLastWriteTimeUtc(bridge.StatusPath, DateTime.UtcNow.AddMinutes(-1));
    Assert(!bridge.IsBridgeAlive(), "Alter Heartbeat wird nicht als Verbindung angezeigt");

    using var testCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    var responder = Task.Run(async () =>
    {
        while (!File.Exists(bridge.RequestPath))
            await Task.Delay(25, testCts.Token);
        string request = await File.ReadAllTextAsync(bridge.RequestPath, testCts.Token);
        using var doc = System.Text.Json.JsonDocument.Parse(request);
        Assert(doc.RootElement.GetProperty("action").GetString() == "ping", "Ping-Befehl");
        string id = doc.RootElement.GetProperty("id").GetString()!;
        string reply = System.Text.Json.JsonSerializer.Serialize(new
        {
            id, ok = true, message = "pong", characters = new[] { new { id = "member-1", name = "Held" } }
        });
        await File.WriteAllTextAsync(bridge.ResponsePath, reply, testCts.Token);
    }, testCts.Token);
    BridgeReply ping = await bridge.SendAsync("ping", timeout: TimeSpan.FromSeconds(4), cancellationToken: testCts.Token);
    await responder;
    Assert(ping.Ok && ping.Message == "pong" && ping.Characters.Count == 1, "Antwort-ID / JSON-Parsing");

    bool rejected = false;
    try { await bridge.SendAsync("give_item", "not-a-guid", "member-1", 1); }
    catch (ArgumentException) { rejected = true; }
    Assert(rejected, "Ungültige Item-UUID abweisen");
    rejected = false;
    try { await bridge.SendAsync("give_item", Guid.NewGuid().ToString(), "member-1", 100); }
    catch (ArgumentOutOfRangeException) { rejected = true; }
    Assert(rejected, "Menge über 99 abweisen");
    Console.WriteLine("PASS: host-lokale Datei-Brücke, Antworten, Heartbeat und Eingabevalidierung");
}
finally { if (Directory.Exists(testBridgeDir)) Directory.Delete(testBridgeDir, recursive: true); }

static void RunSta(Action action)
{
    Exception? failure = null;
    var thread = new System.Threading.Thread(() =>
    {
        try { action(); }
        catch (Exception ex) { failure = ex; }
    });
    thread.SetApartmentState(System.Threading.ApartmentState.STA);
    thread.Start();
    thread.Join();
    if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
}


// Catalog presentation reads existing imported-items.json; no re-import or cache writes.
var labeledItem = new BG3ItemManager.Models.ItemRecord
{
    Uuid = "b8fa6f32-0244-4f9f-8bc4-2a1d2b3c4d5e",
    NameDe = "Testsäbel",
    NameEn = "Test Sabre",
    TechnicalName = "WPN_TEST_SWORD",
    Category = "Weapon"
};
Assert(ItemViewService.HasReadableName(labeledItem), "Benanntes Item anzeigen");
Assert(ItemViewService.DisplayCategory(labeledItem) == "Waffen", "Waffe klassifizieren");
var technicalOnlyItem = new BG3ItemManager.Models.ItemRecord {
    Uuid = Guid.NewGuid().ToString(), NameEn = "S_TEST_LEVER", TechnicalName = "S_TEST_LEVER",
    Category = "Unklassifiziert"
};
Assert(!ItemViewService.HasReadableName(technicalOnlyItem), "Technische Vorlagen standardmaessig ausblenden");
var amuletItem = new BG3ItemManager.Models.ItemRecord { NameEn = "Spellcrux Amulet", Category = "Object" };
Assert(ItemViewService.DisplayCategory(amuletItem) == "Schmuck", "Amulett kategorisieren");
Console.WriteLine("PASS: Item-Kategorien und Filter ohne Neuimport");
