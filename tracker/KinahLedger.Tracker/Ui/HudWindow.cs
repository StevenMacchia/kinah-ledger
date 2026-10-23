using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;

namespace KinahLedger.Ui;

/// <summary>The figures the panel over the game can show, in the order it shows them. The player picks which in Options › In game.</summary>
public static class HudFields
{
    /// <param name="Key">Kept in ui.json (UserPrefs.HudFields).</param>
    /// <param name="Name">The choice in Options.</param>
    /// <param name="Label">Beside the figure on the panel; empty for the clock and the last pickup.</param>
    public sealed record Field(string Key, string Name, string Label, string Hint);

    public static readonly Field[] All =
    {
        new("time", "Timer", "", "How long you've farmed here"),
        new("rate", "Total per hour", "/ hr", "Kinah plus loot after tax: the window's big number"),
        new("kinah", "Kinah per hour", "Kinah / hr", "Kinah picked up, per hour"),
        new("loot", "Loot per hour", "loot / hr", "What the hour's loot sells for, after tax"),
        new("total", "Session total", "total", "Kinah plus loot after tax, so far"),
        new("best", "Your best here", "best here", "Your best hour in this zone"),
        new("last", "Last pickup", "", "The last thing picked up"),
    };

    /// <summary>The clock, the hour's total and the session total, the same figures the window shows.</summary>
    public const string Default = "time,rate,total";

    /// <summary>The saved list as keys in panel order; never empty (the clock at least).</summary>
    public static List<string> Parse(string? saved)
    {
        var want = (saved ?? Default).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var keys = All.Where(f => want.Contains(f.Key)).Select(f => f.Key).ToList();
        return keys.Count > 0 ? keys : new() { "time" };
    }

    public static string Join(IEnumerable<string> keys) => string.Join(",", Parse(string.Join(",", keys)));
}

/// <summary>
/// One line over the game, like the site's compact mode: the figures the player picked (HudFields), worked out by the
/// window itself so the panel and the window always agree. Drag it anywhere; its opacity is a slider in Options.
/// </summary>
public sealed class HudWindow : Window
{
    readonly Dictionary<string, TextBlock> _cells = new();
    readonly StackPanel _row = new() { Orientation = Orientation.Horizontal };
    List<string> _fields = HudFields.Parse(null);

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

    // frozen: the readers touch this class from their own threads (HiddenIfOver) before the panel exists, and an unfrozen
    // brush made there can't be drawn on the window's thread (2026-10-22: every window refresh failed, crafts never showed)
    static readonly Brush Muted = Frozen(Color.FromRgb(0x7D, 0x84, 0xA8));
    static Brush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

    /// <param name="fields">The figures to show (HudFields keys); the default set when null.</param>
    public HudWindow(IEnumerable<string>? fields = null)
    {
        if (fields != null) _fields = HudFields.Parse(string.Join(",", fields));
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

        Build();

        _alert = new TextBlock { FontSize = 13.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 6, 0, 0), Visibility = Visibility.Collapsed, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 420 };
        _frame = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xF0, 0x0A, 0x0C, 0x18)),
            BorderBrush = FrameBrush,
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(16, 10, 20, 10),
            Child = new StackPanel { Children = { _row, _alert } }
        };
        Content = _frame;
        MouseLeftButtonDown += (_, _) => { try { DragMove(); } catch (InvalidOperationException) { } };
    }

    static readonly SolidColorBrush FrameBrush = new(Color.FromRgb(0x32, 0x3A, 0x66));
    readonly Border _frame;
    readonly TextBlock _alert;
    System.Windows.Threading.DispatcherTimer? _alertTimer;

    /// <summary>
    /// A rare drop: the panel's border and a line under the figures in the item's grade colour, for a few seconds
    /// (0.8, Steven 2026-10-22). A second alert while one shows replaces it and starts the time again.
    /// </summary>
    public void Alert(string text, Color color, double seconds = 5)
    {
        _alert.Text = text; _alert.Foreground = new SolidColorBrush(color); _alert.Visibility = Visibility.Visible;
        _frame.BorderBrush = new SolidColorBrush(color); _frame.BorderThickness = new Thickness(2);
        _alertTimer?.Stop();
        _alertTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
        _alertTimer.Tick += (_, _) => { _alertTimer?.Stop(); _alert.Visibility = Visibility.Collapsed; _frame.BorderBrush = FrameBrush; _frame.BorderThickness = new Thickness(1); };
        _alertTimer.Start();
    }

    /// <summary>Shows these figures, in panel order; the next Update fills them.</summary>
    public void SetFields(IEnumerable<string> keys)
    {
        var next = HudFields.Parse(string.Join(",", keys));
        if (next.SequenceEqual(_fields)) return;
        _fields = next;
        Build();
    }

    void Build()
    {
        _row.Children.Clear(); _cells.Clear();
        for (int i = 0; i < _fields.Count; i++)
        {
            var key = _fields[i]; var f = HudFields.All.First(x => x.Key == key);
            var number = new TextBlock { Text = "—", VerticalAlignment = VerticalAlignment.Bottom };
            var cell = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(i == 0 ? 0 : 20, 0, 0, 0) };
            switch (key)
            {
                case "time":
                    // the recording dot and the clock
                    cell.Children.Add(new Ellipse { Width = 8, Height = 8, Fill = new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x5E)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 7, 0) });
                    number.FontFamily = new FontFamily("Cascadia Mono, Consolas"); number.FontSize = 14; number.Foreground = new SolidColorBrush(Color.FromRgb(0xE9, 0xEF, 0xF7)); number.VerticalAlignment = VerticalAlignment.Center;
                    number.Text = "0:00";
                    break;
                case "last":
                    number.FontSize = 13.5; number.Foreground = new SolidColorBrush(Color.FromRgb(0xC9, 0xCF, 0xE6)); number.MaxWidth = 240; number.TextTrimming = TextTrimming.CharacterEllipsis; number.VerticalAlignment = VerticalAlignment.Center;
                    number.Text = "";
                    break;
                default:
                    var color = key switch
                    {
                        "rate" => Color.FromRgb(0xF8, 0xD5, 0x98), "kinah" => Color.FromRgb(0xF8, 0xD5, 0x98), "total" => Color.FromRgb(0x5E, 0xD8, 0xCB),
                        "best" => Color.FromRgb(0xB9, 0xBF, 0xDA), _ => Color.FromRgb(0xEE, 0xEC, 0xF6),
                    };
                    number.FontSize = key == "rate" ? 18 : 16; number.FontWeight = FontWeights.SemiBold; number.Foreground = new SolidColorBrush(color);
                    number.FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI");
                    break;
            }
            cell.Children.Add(number);
            if (f.Label.Length > 0) cell.Children.Add(new TextBlock { Text = f.Label, FontSize = 12.5, Foreground = Muted, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(6, 0, 0, key == "rate" ? 2 : 1) });
            _row.Children.Add(cell);
            _cells[key] = number;
        }
    }

    /// <summary>The figures by key (HudFields), as the window worked them out; a missing one shows a dash.</summary>
    public void Update(IReadOnlyDictionary<string, string> values)
    {
        foreach (var (key, cell) in _cells)
            cell.Text = values.TryGetValue(key, out var v) && v.Length > 0 ? v : key == "last" ? "" : "—";
    }
}
