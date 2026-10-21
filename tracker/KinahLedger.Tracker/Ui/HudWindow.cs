using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;

namespace KinahLedger.Ui;

/// <summary>
/// One line over the game, like the site's compact mode: a recording dot and the farming clock, then Kinah per hour,
/// what the hour's drops are worth at the lowest Market price, and the total estimated value so far (Kinah picked up
/// plus the drops' value). Drag it anywhere; its opacity is a slider in Settings.
/// </summary>
public sealed class HudWindow : Window
{
    readonly TextBlock _time = new(), _kinah = new(), _value = new(), _total = new();

    /// <summary>The panel on screen right now, if any: the readers step around it.</summary>
    public static HudWindow? Current { get; private set; }

    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }

    /// <summary>Where the panel is on screen, in pixels (not DIPs), or empty when it isn't shown.</summary>
    System.Drawing.Rectangle PixelBounds()
    {
        var h = new WindowInteropHelper(this).Handle;
        if (h == IntPtr.Zero || !IsVisible || !GetWindowRect(h, out var r)) return System.Drawing.Rectangle.Empty;
        return System.Drawing.Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
    }

    /// <summary>
    /// Runs a screen read with the panel out of the way: when the panel sits over the area about to be read (dragged onto
    /// the minimap corner, say), it is hidden for the read and shown again after, so the game's own text is what gets read
    /// and the panel never counts as a window covering the game.
    /// </summary>
    public static async Task HiddenIfOver(System.Drawing.Rectangle area, Func<Task> read)
    {
        var h = Current;
        bool hide = false;
        if (h != null) { try { hide = await h.Dispatcher.InvokeAsync(() => h.PixelBounds().IntersectsWith(area)); } catch (Exception) { hide = false; } }
        // a hidden window can still be on screen for a frame or two: give the compositor a moment before the capture
        if (hide) { try { await h!.Dispatcher.InvokeAsync(h.Hide); await Task.Delay(120); } catch (Exception) { hide = false; } }
        try { await read(); }
        finally { if (hide) { try { await h!.Dispatcher.InvokeAsync(h.Show); } catch (Exception) { /* closing */ } } }
    }

    static readonly Brush Muted = new SolidColorBrush(Color.FromRgb(0x7D, 0x84, 0xA8));

    public HudWindow()
    {
        Current = this;
        Closed += (_, _) => { if (Current == this) Current = null; };
        ShowActivated = false;   // never takes focus from the game, on start or when shown again after a read
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
        Left = SystemParameters.WorkArea.Left + SystemParameters.WorkArea.Width / 2 - 260; Top = SystemParameters.WorkArea.Top + 12;

        var row = new Grid();
        for (int i = 0; i < 4; i++) row.ColumnDefinitions.Add(new ColumnDefinition { Width = i == 0 ? GridLength.Auto : new GridLength(1, GridUnitType.Star) });

        // the recording dot and the farming clock
        var clock = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 22, 0) };
        clock.Children.Add(new Ellipse { Width = 8, Height = 8, Fill = new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x5E)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 7, 0) });
        _time.FontFamily = new FontFamily("Cascadia Mono, Consolas"); _time.FontSize = 14; _time.Foreground = new SolidColorBrush(Color.FromRgb(0xE9, 0xEF, 0xF7)); _time.VerticalAlignment = VerticalAlignment.Center;
        clock.Children.Add(_time);
        Grid.SetColumn(clock, 0); row.Children.Add(clock);

        Cell(row, 1, _kinah, "Kinah / hr", Color.FromRgb(0xF8, 0xD5, 0x98), HorizontalAlignment.Left);
        Cell(row, 2, _value, "loot / hr", Color.FromRgb(0xEE, 0xEC, 0xF6), HorizontalAlignment.Center);
        Cell(row, 3, _total, "total value", Color.FromRgb(0x5E, 0xD8, 0xCB), HorizontalAlignment.Right);

        Content = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xF0, 0x0A, 0x0C, 0x18)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x32, 0x3A, 0x66)),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(16, 10, 20, 10),
            MinWidth = 520,
            Child = row
        };
        _time.Text = "0:00"; _kinah.Text = "—"; _value.Text = "—"; _total.Text = "—";
        MouseLeftButtonDown += (_, _) => { try { DragMove(); } catch (InvalidOperationException) { } };
    }

    /// <summary>A figure then its label on one line: "61K Kinah/hr".</summary>
    static void Cell(Grid row, int col, TextBlock number, string label, Color color, HorizontalAlignment align)
    {
        var line = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = align, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, col < 3 ? 18 : 0, 0) };
        number.FontSize = 16; number.FontWeight = FontWeights.SemiBold; number.Foreground = new SolidColorBrush(color);
        number.FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI"); number.VerticalAlignment = VerticalAlignment.Bottom;
        line.Children.Add(number);
        line.Children.Add(new TextBlock { Text = label, FontSize = 12.5, Foreground = Muted, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(6, 0, 0, 1) });
        Grid.SetColumn(line, col);
        row.Children.Add(line);
    }

    /// <param name="time">The farming clock.</param>
    /// <param name="kinahHr">Kinah picked up per hour.</param>
    /// <param name="dropsValueHr">What the hour's tradeable drops sell for at the lowest Market price, or a dash with no price known.</param>
    /// <param name="total">Kinah picked up plus the drops' Market value so far.</param>
    public void Update(string time, string kinahHr, string dropsValueHr, string total) { _time.Text = time; _kinah.Text = kinahHr; _value.Text = dropsValueHr; _total.Text = total; }
}
