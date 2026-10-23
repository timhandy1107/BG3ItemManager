using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BG3ItemManager.Services;

namespace BG3ItemManager;

internal sealed class GiveItemDialog : Window
{
    private readonly ComboBox _characterBox;
    private readonly TextBox _quantityBox;
    public BridgeCharacter? SelectedCharacter => _characterBox.SelectedItem as BridgeCharacter;
    public int Quantity { get; private set; } = 1;

    public GiveItemDialog(string itemName, IReadOnlyList<BridgeCharacter> characters)
    {
        Title = "Gegenstand an Charakter geben";
        Width = 430;
        Height = 285;
        MinWidth = 360;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(26, 31, 43));
        Foreground = Brushes.White;
        FontFamily = new FontFamily("Segoe UI");
        var stack = new StackPanel { Margin = new Thickness(20) };
        stack.Children.Add(new TextBlock
        {
            Text = itemName, FontWeight = FontWeights.SemiBold, FontSize = 16,
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 14)
        });
        stack.Children.Add(new TextBlock { Text = "Empfänger (aktuelle Gruppe)" });
        _characterBox = new ComboBox { ItemsSource = characters, SelectedIndex = 0, Margin = new Thickness(0, 5, 0, 12), Padding = new Thickness(5) };
        stack.Children.Add(_characterBox);
        stack.Children.Add(new TextBlock { Text = "Menge (1–99)" });
        _quantityBox = new TextBox { Text = "1", Margin = new Thickness(0, 5, 0, 16), Padding = new Thickness(6) };
        stack.Children.Add(_quantityBox);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "Abbrechen", Padding = new Thickness(16, 6, 16, 6), Margin = new Thickness(0, 0, 8, 0) };
        cancel.Click += (_, _) => DialogResult = false;
        actions.Children.Add(cancel);
        var confirm = new Button { Content = "Gegenstand geben", Padding = new Thickness(16, 6, 16, 6) };
        confirm.Click += (_, _) =>
        {
            if (_characterBox.SelectedItem is not BridgeCharacter ||
                !int.TryParse(_quantityBox.Text, out int q) || q is < 1 or > 99)
            {
                MessageBox.Show("Bitte einen Charakter und eine Menge von 1 bis 99 wählen.", "Ungültige Eingabe");
                return;
            }
            Quantity = q;
            DialogResult = true;
        };
        actions.Children.Add(confirm);
        stack.Children.Add(actions);
        Content = stack;
    }
}
