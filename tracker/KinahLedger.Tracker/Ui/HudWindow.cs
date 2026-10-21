using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace KinahLedger.Ui;

/// <summary>
/// A short, wide strip over the game with three figures side by side: Kinah per hour (what the mobs paid), what the
/// hour's drops are worth at the lowest Market price, and the total estimated value so far (Kinah picked up plus the
/// drops' value). Drag it anywhere; its opacity is a slider in Settings.
/// </summary>
public sealed class HudWindow : Window
{
    readonly TextBlock _kinah = new(), _value = new(), _total = new();

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
        Left = SystemParameters.WorkArea.Left + SystemParameters.WorkArea.Width / 2 - 230; Top = SystemParameters.WorkArea.Top + 12;

        var row = new Grid();
        for (int i = 0; i < 5; i++) row.ColumnDefinitions.Add(new ColumnDefinition { Width = i % 2 == 0 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
        Cell(row, 0, "KINAH / HR", _kinah, Color.FromRgb(0xF1, 0xD0, 0x8C));
        Divider(row, 1);
        Cell(row, 2, "DROPS VALUE / HR", _value, Color.FromRgb(0x9B, 0x8C, 0xFF));
        Divider(row, 3);
        Cell(row, 4, "TOTAL ESTIMATED VALUE", _total, Color.FromRgb(0x5F, 0xE0, 0x8A));

        Content = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xEC, 0x0A, 0x0F, 0x18)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x80, 0x4C, 0xC3, 0xF5)),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(6, 5, 6, 6),
            MinWidth = 460,
            Child = row
        };
        _kinah.Text = "—"; _value.Text = "—"; _total.Text = "—";
        MouseLeftButtonDown += (_, _) => { try { DragMove(); } catch (InvalidOperationException) { } };
    }

    static void Cell(Grid row, int col, string label, TextBlock number, Color color)
    {
        var stack = new StackPanel { Margin = new Thickness(10, 0, 10, 0), HorizontalAlignment = HorizontalAlignment.Center };
        var l = new TextBlock { Text = label, FontSize = 9.5, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(0x7A, 0x8A, 0xA3)), HorizontalAlignment = HorizontalAlignment.Center };
        number.FontSize = 19; number.FontWeight = FontWeights.Bold; number.Foreground = new SolidColorBrush(color);
        number.FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI");
        number.HorizontalAlignment = HorizontalAlignment.Center; number.Margin = new Thickness(0, -1, 0, 0);
        stack.Children.Add(l); stack.Children.Add(number);
        Grid.SetColumn(stack, col);
        row.Children.Add(stack);
    }

    static void Divider(Grid row, int col)
    {
        var d = new Border { Width = 1, Background = new SolidColorBrush(Color.FromArgb(0x60, 0x4C, 0xC3, 0xF5)), Margin = new Thickness(0, 4, 0, 4) };
        Grid.SetColumn(d, col);
        row.Children.Add(d);
    }

    /// <param name="kinahHr">Kinah picked up per hour.</param>
    /// <param name="dropsValueHr">What the hour's tradeable drops sell for at the lowest Market price, or a dash with no price known.</param>
    /// <param name="total">Kinah picked up plus the drops' Market value so far.</param>
    public void Update(string kinahHr, string dropsValueHr, string total) { _kinah.Text = kinahHr; _value.Text = dropsValueHr; _total.Text = total; }
}
