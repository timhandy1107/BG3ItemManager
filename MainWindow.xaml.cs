using System.Diagnostics;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Media.Imaging;
using BG3ItemManager.Models;
using BG3ItemManager.Services;
namespace BG3ItemManager;
public partial class MainWindow : Window
{
    private List<ItemRecord> _items = new();
    private readonly ExtenderBridgeService _bridge = new();
    private readonly DispatcherTimer _bridgeTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private List<BridgeCharacter> _characters = new();
    public MainWindow()
    {
        InitializeComponent();
        Reload();
        _bridgeTimer.Tick += (_, _) => UpdateBridgeIndicator();
        _bridgeTimer.Start();
        Closed += (_, _) => _bridgeTimer.Stop();
        UpdateBridgeIndicator();
    }
    private void Reload()
    {
        try
        {
            _items = CatalogService.Load();
            var packages = ModDiscovery.FindPackages();
            var game = GameDiscovery.FindInstallPath();
            StatusText.Text = $"Katalog – BG3: {(game is null ? "Spielordner nicht erkannt" : game)} · {packages.Count} Mod-Pakete · {_items.Count} Gegenstände";
            var category = CategoryBox.SelectedItem as string ?? "Alle Kategorien";
            var source = SourceBox.SelectedItem as string ?? "Alle Quellen";
            SetOptions(CategoryBox, new[] { "Alle Kategorien" }.Concat(_items.Select(i => i.Category).Distinct().OrderBy(x => x)), category);
            SetOptions(SourceBox, new[] { "Alle Quellen" }.Concat(_items.Select(i => i.Source).Distinct().OrderBy(x => x)), source);
            ApplyFilters();
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Katalogfehler", MessageBoxButton.OK, MessageBoxImage.Error); }
    }
    private static void SetOptions(ComboBox box, IEnumerable<string> options, string selected)
    {
        box.ItemsSource = options.ToArray();
        box.SelectedItem = box.Items.Contains(selected) ? selected : box.Items[0];
    }
    private void ApplyFilters()
    {
        if (ItemsGrid is null || SearchBox is null || CategoryBox is null || SourceBox is null) return;
        string q = SearchBox.Text.Trim();
        string cat = CategoryBox.SelectedItem as string ?? "Alle Kategorien";
        string src = SourceBox.SelectedItem as string ?? "Alle Quellen";
        var filtered = _items.Where(i => (cat == "Alle Kategorien" || i.Category == cat)
            && (src == "Alle Quellen" || i.Source == src)
            && (q.Length == 0 || new[] { i.NameDe, i.NameEn, i.TechnicalName, i.StatsName, i.Uuid, i.Description, i.DescriptionDe, i.DescriptionEn }.Any(s => s.Contains(q, StringComparison.OrdinalIgnoreCase)))).ToList();
        ItemsGrid.ItemsSource = filtered;
        CountText.Text = $"{filtered.Count} / {_items.Count} Gegenstände";
    }
    private void FiltersChanged(object sender, EventArgs e) => ApplyFilters();
    private async void ImportExtractedClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button button) button.IsEnabled = false;
        try
        {
            var result = await ExtractedModImporter.ImportAsync();
            Reload();
            MessageBox.Show($"Gelesene Dateien: {result.Files}\nVorlagen: {result.Items}\nLokalisierungsdateien: {result.LocalizationFiles}\nAufgelöste Namen: {result.LocalizedNames}\nVerknüpfte Einzelbilder: {result.Icons}\nFehler: {result.Errors}\n\n" + string.Join("\n", result.Warnings), "LSX-Import");
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Importfehler", MessageBoxButton.OK, MessageBoxImage.Error); }
        finally { if (sender is Button button) button.IsEnabled = true; }
    }
    private void SelectGameClicked(object sender, RoutedEventArgs e)
    {
        // WPF does not require an extra library for this folder picker on .NET 8 Windows.
        var dialog = new OpenFolderDialog { Title = "BG3 Installationsordner auswählen (enthält Data\\Shared.pak)" };
        if (dialog.ShowDialog(this) == true)
        {
            try { GameDiscovery.SaveInstallPath(dialog.FolderName); Reload(); }
            catch (Exception ex) { MessageBox.Show(ex.Message, "BG3-Ordner", MessageBoxButton.OK, MessageBoxImage.Error); }
        }
    }
    private void SelectDivineClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "LSLib Divine (Divine.exe)|Divine.exe|EXE-Dateien (*.exe)|*.exe", Title = "Divine.exe aus LSLib wählen" };
        if (dialog.ShowDialog(this) == true)
        {
            try { DivineToolSettings.SavePath(dialog.FileName); MessageBox.Show("Divine.exe wurde gespeichert. Jetzt kannst du .pak-Mods automatisch importieren.", "LSLib eingerichtet"); }
            catch (Exception ex) { MessageBox.Show(ex.Message, "LSLib-Einrichtung", MessageBoxButton.OK, MessageBoxImage.Error); }
        }
    }
    private void SelectTexconvClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "DirectXTex texconv (texconv.exe)|texconv.exe|EXE-Dateien (*.exe)|*.exe", Title = "Microsoft DirectXTex texconv.exe auswählen" };
        if (dialog.ShowDialog(this) == true)
        {
            try { TexconvToolSettings.SavePath(dialog.FileName); MessageBox.Show("texconv.exe wurde gespeichert. Beim nächsten Import können DDS-Icons aus dem Spiel verwendet werden.", "Icon-Konverter eingerichtet"); }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Icon-Konverter", MessageBoxButton.OK, MessageBoxImage.Error); }
        }
    }
    private async void ImportPakClicked(object sender, RoutedEventArgs e)
    {
        PakImportButton.IsEnabled = false;
        try
        {
            var progress = new Progress<string>(message => StatusText.Text = "Import läuft – " + message);
            var result = await ModArchiveImporter.ImportAsync(progress);
            Reload();
            var summary = $"Gefundene BG3-/Mod-Archive: {result.PackagesFound}\nNeu verarbeitet: {result.PackagesProcessed}\nAus Cache übernommen: {result.PackagesSkipped}\nGefundene Vorlagen: {result.Templates}\nGelesene Text/LSX-Dateien: {result.ParsedFiles}\nLokalisierungsdateien: {result.LocalizationFiles}\nAufgelöste Namen: {result.LocalizedNames}\nVerknüpfte Einzelbilder: {result.Icons}\nWarnungen bei Konvertierung: {result.ConversionWarnings}\nFehler: {result.Errors}";
            if (result.Warnings.Count > 0) summary += "\n\n" + string.Join("\n", result.Warnings);
            MessageBox.Show(summary, "PAK-Import abgeschlossen");
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "PAK-Import fehlgeschlagen", MessageBoxButton.OK, MessageBoxImage.Error); }
        finally { PakImportButton.IsEnabled = true; }
    }
    private void OpenExtractedClicked(object sender, RoutedEventArgs e) => OpenFolder(ExtractedModImporter.ExtractedDirectory);
    private void ReloadClicked(object sender, RoutedEventArgs e) => Reload();
    private static void OpenFolder(string path) { Directory.CreateDirectory(path); Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true }); }
    private void OpenModsClicked(object sender, RoutedEventArgs e) => OpenFolder(ModDiscovery.ModDirectory);
    private void OpenDataClicked(object sender, RoutedEventArgs e) => OpenFolder(CatalogService.DataDirectory);
    private ItemRecord? Selected => ItemsGrid.SelectedItem as ItemRecord;
    private void ItemSelected(object sender, SelectionChangedEventArgs e)
    {
        var i = Selected;
        ItemName.Text = i?.DisplayName ?? "Gegenstand auswählen";
        ItemEnglish.Text = i is null || string.IsNullOrWhiteSpace(i.NameEn) ? "" : $"Englisch: {i.NameEn}";
        ItemTechnical.Text = i is null ? "" : $"Technisch: {i.TechnicalName}   Stats: {i.StatsName}   Icon-ID: {i.IconId}";
        ItemCategory.Text = i is null ? "" : $"Kategorie: {i.Category}";
        ItemSource.Text = i is null ? "" : $"Quelle: {i.Source}";
        ItemUuid.Text = i is null ? "" : $"UUID: {i.Uuid}";
        ItemDescription.Text = i?.DisplayDescription ?? "";
        ItemProperties.Text = i?.PropertyText ?? "";
        ItemIconHint.Text = i is null ? "" : string.IsNullOrWhiteSpace(i.IconPath) ? "Bild nicht gefunden oder Icon-Atlas noch nicht verarbeitet" : "";
        ItemIcon.Source = null;
        if (i is not null && !string.IsNullOrWhiteSpace(i.IconPath))
        {
            var full = Path.GetFullPath(Path.Combine(CatalogService.DataDirectory, i.IconPath));
            if (full.StartsWith(Path.GetFullPath(CatalogService.DataDirectory) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && File.Exists(full))
                try { ItemIcon.Source = new BitmapImage(new Uri(full)); } catch { /* unsupported image */ }
        }
    }
    private void ItemMenuOpened(object sender, RoutedEventArgs e)
    {
        if (sender is ContextMenu menu) menu.IsEnabled = Selected is not null;
    }
    private void ItemRightClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject element)
        {
            while (element is not null && element is not DataGridRow) element = System.Windows.Media.VisualTreeHelper.GetParent(element);
            if (element is DataGridRow row) ItemsGrid.SelectedItem = row.Item;
        }
    }
    private void UpdateBridgeIndicator()
    {
        bool live = _bridge.IsBridgeAlive();
        BridgeStatusText.Text = live
            ? "Script Extender: lokale Brücke antwortet per Heartbeat · Spielstand für Befehle erforderlich"
            : "Script Extender: keine aktive Brücke – Installationshinweise in README.md";
        BridgeStatusText.Foreground = live ? Brushes.LightGreen : Brushes.Orange;
    }

    private async void PingBridgeClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            var reply = await _bridge.SendAsync("ping");
            UpdateBridgeIndicator();
            MessageBox.Show(reply.Message, "Script Extender: Verbindungstest", MessageBoxButton.OK,
                reply.Ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Keine Verbindung", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private async Task<bool> ReloadCharactersAsync()
    {
        try
        {
            var reply = await _bridge.SendAsync("list_characters");
            if (!reply.Ok)
            {
                MessageBox.Show(reply.Message, "Charaktere konnten nicht geladen werden", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            _characters = reply.Characters
                .Where(c => !string.IsNullOrWhiteSpace(c.Id))
                .DistinctBy(c => c.Id, StringComparer.OrdinalIgnoreCase)
                .ToList();
            UpdateBridgeIndicator();
            return _characters.Count > 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Keine Charakterantwort", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
    }

    private async void RefreshCharactersClicked(object sender, RoutedEventArgs e)
    {
        if (await ReloadCharactersAsync())
            MessageBox.Show(string.Join("\n", _characters.Select(c => c.Name)),
                "Erkannte Gruppenmitglieder", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void OpenBridgeFolderClicked(object sender, RoutedEventArgs e) => OpenFolder(_bridge.DirectoryPath);

    private async void GiveClicked(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } item) return;
        if (!Guid.TryParse(item.Uuid, out _))
        {
            MessageBox.Show("Dieser Eintrag besitzt keine gültige UUID.", "Ungültiges Item");
            return;
        }
        // A new list is fetched per invocation: no outdated recipient from another save.
        if (!await ReloadCharactersAsync()) return;
        var dialog = new GiveItemDialog(item.DisplayName, _characters) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.SelectedCharacter is null) return;
        try
        {
            var reply = await _bridge.SendAsync("give_item", item.Uuid,
                dialog.SelectedCharacter.Id, dialog.Quantity);
            MessageBox.Show(reply.Message, reply.Ok ? "Befehl gesendet" : "Gegenstandsvergabe fehlgeschlagen",
                MessageBoxButton.OK, reply.Ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Gegenstandsvergabe nicht bestätigt", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CopyUuidClicked(object sender, RoutedEventArgs e) { if (Selected is { } i) Clipboard.SetText(i.Uuid); }
    private void InspectClicked(object sender, RoutedEventArgs e) { if (Selected is { } i) MessageBox.Show($"{i.DisplayName}\nEnglisch: {i.NameEn}\n\n{i.DisplayDescription}\n\n{i.PropertyText}\n\nUUID: {i.Uuid}\nQuelle: {i.Source}", "Gegenstand untersuchen (importierte Daten)"); }
}
