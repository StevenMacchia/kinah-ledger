using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace KinahLedger.Ui;

/// <summary>
/// Small always-on-top readout over the game: Kinah per hour (what the mobs paid), and what the hour's drops are worth
/// at the lowest Market price on the server. Drag it anywhere; its opacity is a slider in Settings.
/// </summary>
public sealed class HudWindow : Window
{
    readonly TextBlock _kinah = new(), _value = new();

    public HudWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.Manual;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        // top-centre by default: the top-right belongs to the minimap and its zone title, the bottom-left to the chat
        Left = SystemParameters.WorkArea.Left + SystemParameters.WorkArea.Width / 2 - 120; Top = SystemParameters.WorkArea.Top + 16;

        var grid = new Grid { MinWidth = 230 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition()); grid.RowDefinitions.Add(new RowDefinition());
        Row(grid, 0, "KINAH / HR", _kinah, Color.FromRgb(0xF1, 0xD0, 0x8C));
        Row(grid, 1, "DROPS VALUE / HR", _value, Color.FromRgb(0x9B, 0x8C, 0xFF));

        var accent = new Border { Height = 2, Background = new SolidColorBrush(Color.FromRgb(0x4C, 0xC3, 0xF5)), CornerRadius = new CornerRadius(1), Margin = new Thickness(0, 0, 0, 8) };
        var inner = new StackPanel(); inner.Children.Add(accent); inner.Children.Add(grid);
        Content = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xEC, 0x0A, 0x0F, 0x18)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x80, 0x4C, 0xC3, 0xF5)),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(14, 10, 14, 11),
            Child = inner
        };
        _kinah.Text = "—"; _value.Text = "—";
        MouseLeftButtonDown += (_, _) => { try { DragMove(); } catch (InvalidOperationException) { } };
    }

    static void Row(Grid grid, int row, string label, TextBlock number, Color color)
    {
        var l = new TextBlock { Text = label, FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(0x7A, 0x8A, 0xA3)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, row == 0 ? 0 : 6, 16, 0) };
        number.FontSize = 22; number.FontWeight = FontWeights.Bold; number.Foreground = new SolidColorBrush(color);
        number.FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI");
        number.HorizontalAlignment = HorizontalAlignment.Right; number.VerticalAlignment = VerticalAlignment.Center; number.Margin = new Thickness(0, row == 0 ? 0 : 6, 0, 0);
        Grid.SetRow(l, row); Grid.SetColumn(l, 0); Grid.SetRow(number, row); Grid.SetColumn(number, 1);
        grid.Children.Add(l); grid.Children.Add(number);
    }

    /// <param name="kinahHr">Kinah picked up per hour.</param>
    /// <param name="dropsValueHr">What the hour's tradeable drops sell for at the lowest Market price, or a dash with no price known.</param>
    public void Update(string kinahHr, string dropsValueHr) { _kinah.Text = kinahHr; _value.Text = dropsValueHr; }
}
