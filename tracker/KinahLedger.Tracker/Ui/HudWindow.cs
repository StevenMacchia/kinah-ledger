using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace KinahLedger.Ui;

/// <summary>Small always-on-top readout over the game. Drag it anywhere.</summary>
public sealed class HudWindow : Window
{
    readonly TextBlock _big = new(), _small = new(), _foot = new();

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
        Left = SystemParameters.WorkArea.Right - 360; Top = SystemParameters.WorkArea.Top + 24;

        _big.FontSize = 21; _big.FontWeight = FontWeights.Bold; _big.Foreground = new SolidColorBrush(Color.FromRgb(0xF1, 0xD0, 0x8C));
        _big.FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI");
        _small.FontSize = 12.5; _small.Foreground = new SolidColorBrush(Color.FromRgb(0xBA, 0xC6, 0xD6)); _small.Margin = new Thickness(0, 4, 0, 0); _small.LineHeight = 18;
        _foot.FontSize = 11.5; _foot.Foreground = new SolidColorBrush(Color.FromRgb(0x7A, 0x8A, 0xA3)); _foot.Margin = new Thickness(0, 6, 0, 0); _foot.TextTrimming = TextTrimming.CharacterEllipsis; _foot.MaxWidth = 300;

        var stack = new StackPanel { MinWidth = 300 };
        stack.Children.Add(_big); stack.Children.Add(_small); stack.Children.Add(_foot);
        var accent = new Border { Height = 2, Background = new SolidColorBrush(Color.FromRgb(0x4C, 0xC3, 0xF5)), CornerRadius = new CornerRadius(1), Margin = new Thickness(0, 0, 0, 8) };
        var inner = new StackPanel(); inner.Children.Add(accent); inner.Children.Add(stack);
        Content = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xEC, 0x0A, 0x0F, 0x18)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x80, 0x4C, 0xC3, 0xF5)),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(14, 10, 14, 11),
            Child = inner
        };
        _big.Text = "Kinah Ledger"; _small.Text = "waiting for loot lines…";
        MouseLeftButtonDown += (_, _) => { try { DragMove(); } catch (InvalidOperationException) { } };
    }

    public void Update(string big, string small, string foot) { _big.Text = big; _small.Text = small; _foot.Text = foot; }
}
