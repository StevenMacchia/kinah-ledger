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
        new("feed", "Newest line and rate", "", "The last thing counted and the rate beside it, as the window's feed"),
        new("time", "Timer", "", "How long you've farmed here"),
        new("rate", "Total per hour", "/ hr", "Kinah plus loot after tax: the window's big number"),
        new("kinah", "Kinah per hour", "Kinah / hr", "Kinah picked up, per hour"),
        new("loot", "Loot per hour", "loot / hr", "What the hour's loot sells for, after tax; hidden when it is the whole hour, as in gathering"),
        new("total", "Session total", "total", "Kinah plus loot after tax, so far"),
        new("best", "Your best here", "best here", "Your best hour in this zone"),
        new("last", "Last pickup", "", "The last thing picked up"),
    };

    /// <summary>The newest feed line and the rate beside it (direction C, 0.8.30), the same the window's live card shows.</summary>
    public const string Default = "feed";
    /// <summary>The default before 0.8.30 (the clock, the hour's total, the session total); a panel still on it moves to the feed once.</summary>
    public const string OldDefault = "time,rate,total";

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
/// window itself so the panel and the window always agree. Like a game overlay it shows only while the game is in front
/// ("show"), and locked it lets clicks through to the game ("unlocked"); unlocked, drag it anywhere. Its opacity is a
/// slider in Options. Everything it shows comes in through Update's values, worked out by the window.
/// </summary>
public sealed class HudWindow : Window
{
    readonly Dictionary<string, TextBlock> _cells = new();
    /// <summary>The words beside a figure ("/ hr"), by key: the rate's goes while it warms up.</summary>
    readonly Dictionary<string, TextBlock> _labels = new();
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
        if (hide) { try { await h!.Dispatcher.InvokeAsync(() => { h._readHidden = true; h.Hide(); }); await Task.Delay(120); } catch (Exception) { hide = false; } }
        try { await read(); }
        finally
        {
            // back as it was, unless the game left the front meanwhile (ShowAs): then it stays out of the way
            if (hide) { try { await h!.Dispatcher.InvokeAsync(() => { h._readHidden = false; if (h._shownAs != "hidden") h.Show(); }); } catch (Exception) { /* closing */ } }
        }
    }

    /// <summary>True while HiddenIfOver has the panel hidden for a read: ShowAs leaves showing it to the read's end.</summary>
    bool _readHidden;
    /// <summary>How the panel shows now ("show" value): "" full, "dim" paused, "hidden" out of the way.</summary>
    string _shownAs = "";
    /// <summary>The paused look: the panel stays where it is, faded, while the chat can't be read.</summary>
    const double DimOpacity = 0.45;

    /// <summary>
    /// Follows the game (the "show" value): "" shows the panel, "dim" fades it to the paused look, "hidden" fades it out in
    /// a blink and then hides it for real, so it never sits over another program. Only a change of state animates.
    /// </summary>
    void ShowAs(string how)
    {
        if (how == _shownAs) return;
        bool wasHidden = _shownAs == "hidden";
        _shownAs = how;
        // the Card and the Ticker show the pause themselves (a grey dot, the clock faded, the reason in orange): they stay solid
        double to = how == "hidden" ? 0 : how == "dim" ? (_look == null ? DimOpacity : 1) : 1;
        var fade = new System.Windows.Media.Animation.DoubleAnimation(to, TimeSpan.FromMilliseconds(how == "hidden" ? 120 : 180));
        if (how == "hidden") fade.Completed += (_, _) => { if (_shownAs == "hidden") Hide(); };
        else if (wasHidden && !IsVisible && !_readHidden) { Opacity = 0; Show(); }
        BeginAnimation(OpacityProperty, fade);
    }

    // frozen: the readers touch this class from their own threads (HiddenIfOver) before the panel exists, and an unfrozen
    // brush made there can't be drawn on the window's thread (2026-10-22: every window refresh failed, crafts never showed)
    static readonly Brush Muted = Frozen(Color.FromRgb(0x7D, 0x84, 0xA8));
    static SolidColorBrush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

    /// <summary>The look the panel is drawn in (HudLooks: the strip, the Card or the Ticker); set when it is made.</summary>
    public string Look { get; }
    /// <summary>The Card or the Ticker, when the panel is drawn in one: it takes the same values as the strip.</summary>
    readonly IHudLook? _look;
    /// <summary>What is on the window: the strip's frame, or the look's root.</summary>
    readonly FrameworkElement _content;

    /// <param name="fields">The figures to show (HudFields keys); the default set when null. The strip's only: the Card and the Ticker show a fixed set.</param>
    /// <param name="look">HudLooks.Strip, Card or Ticker.</param>
    /// <param name="still">A drawing (layout renders, the Options preview): nothing animates, and it is not the panel the readers step around.</param>
    public HudWindow(IEnumerable<string>? fields = null, string look = HudLooks.Strip, bool still = false)
    {
        if (fields != null) _fields = HudFields.Parse(string.Join(",", fields));
        Look = HudLooks.Parse(look);
        _look = Look switch { HudLooks.Card => new HudCard { Still = still }, HudLooks.Ticker => new HudTicker { Still = still }, _ => null };
        if (!still)
        {
            Current = this;
            Closed += (_, _) => { if (Current == this) Current = null; };
        }
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

        _acc = new TextBlock { FontSize = 12.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 4, 0, 0), Visibility = Visibility.Collapsed };
        _note = new TextBlock { FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = Gold, Margin = new Thickness(0, 4, 0, 0), Visibility = Visibility.Collapsed, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 420 };
        _alert = new TextBlock { FontSize = 13.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 6, 0, 0), Visibility = Visibility.Collapsed, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 420 };
        _frame = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xF0, 0x0A, 0x0C, 0x18)),
            BorderBrush = FrameBrush,
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(_fields.Count == 1 && _fields[0] == "feed" ? 8 : 12),
            Padding = _fields.Count == 1 && _fields[0] == "feed" ? new Thickness(10, 6, 12, 6) : new Thickness(16, 10, 20, 10),
            Child = _look == null ? new StackPanel { Children = { _row, _note, _acc, _alert, _dragHint } } : null
        };
        _content = _look?.Root ?? _frame;
        // unlocked: a faint dashed outline on the frame's edge says it can be moved
        var corner = _look?.Corner ?? _frame.CornerRadius.TopLeft;
        _outline = new Rectangle { Stroke = OutlineBrush, StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 4, 3 }, RadiusX = corner, RadiusY = corner,
            IsHitTestVisible = false, Visibility = Visibility.Collapsed, Margin = _look?.Inset ?? new Thickness(0) };
        var face = new Grid { Children = { _content, _outline } };
        if (_look == null) Content = face;
        else
        {
            // the looks' "drag me" sits under them, in a small dark pill
            _dragHint.Margin = new Thickness(0);
            _dragHint.Padding = new Thickness(8, 2, 8, 3);
            _dragHint.Background = Frozen(Color.FromArgb(0xE0, 0x0A, 0x0C, 0x18));
            _dragHint.HorizontalAlignment = HorizontalAlignment.Left;
            var hintRow = new Border { Child = _dragHint, Margin = new Thickness(_look.Inset.Left, 0, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
            Content = new StackPanel { Children = { face, hintRow } };
        }
        // a drag the player made is the one place remembered (Dragged); placing it by code is not
        MouseLeftButtonDown += (_, _) => { try { DragMove(); Dragged?.Invoke(); } catch (InvalidOperationException) { } };
        MouseEnter += (_, _) => ShowDragHint(); MouseLeave += (_, _) => ShowDragHint();
        SourceInitialized += (_, _) => Native.SetClickThrough(new WindowInteropHelper(this).Handle, _unlock.Length == 0);
        SizeChanged += (_, _) => PlacePending();
    }

    /// <summary>The player dropped the panel somewhere (unlocked): the window remembers Left and Top.</summary>
    public event Action? Dragged;

    /// <summary>A screen area (pixels) whose top centre the panel goes to once it has laid out; null when placed.</summary>
    System.Drawing.Rectangle? _placeOn;
    /// <summary>The Ticker's first place: its left edge this far left of the area's centre (at 1x), instead of centred.</summary>
    double? _placeLeftOfCentre;

    /// <summary>
    /// Puts the panel at the top centre of this screen area, in pixels (the game's monitor, or the main one): clear of
    /// the minimap at the top-right and the chat at the bottom-left. Before the panel's first layout it waits for it, so
    /// the centre is worked out from the panel's real width.
    /// </summary>
    /// <param name="leftOfCentre">Instead of centred, the panel's left edge this far left of the centre (at 1x): the Ticker's place, clear of the target's name.</param>
    public void PlaceTopCentre(System.Drawing.Rectangle area, double? leftOfCentre = null)
    {
        _placeOn = area;
        _placeLeftOfCentre = leftOfCentre;
        PlacePending();
    }

    void PlacePending()
    {
        if (_placeOn is not { } a || ActualWidth < 1) return;
        var h = new WindowInteropHelper(this).Handle;
        if (h == IntPtr.Zero) return;
        _placeOn = null;
        var dpi = VisualTreeHelper.GetDpi(this);
        int w = (int)Math.Round(ActualWidth * dpi.DpiScaleX);
        int x = _placeLeftOfCentre is { } off ? a.Left + Math.Max(0, a.Width / 2 - (int)Math.Round(off * dpi.DpiScaleX)) : a.Left + Math.Max(0, (a.Width - w) / 2);
        Native.MoveTo(h, x, a.Top + (int)Math.Round(12 * dpi.DpiScaleY));
    }

    /// <summary>The panel's size, 0.7 to 2 (Options › In game): everything in it grows or shrinks together.</summary>
    public void SetScale(double scale)
    {
        var s = Math.Clamp(scale, HudPrefs.MinScale, HudPrefs.MaxScale);
        if (Math.Abs(s - 1) < 0.001) _content.LayoutTransform = Transform.Identity;
        else { var t = new ScaleTransform(s, s); t.Freeze(); _content.LayoutTransform = t; }
        _outline.RadiusX = _outline.RadiusY = (_look?.Corner ?? _frame.CornerRadius.TopLeft) * s;
        if (_look != null) { var i = _look.Inset; _outline.Margin = new Thickness(i.Left * s, i.Top * s, i.Right * s, i.Bottom * s); }
    }

    static readonly SolidColorBrush OutlineBrush = Frozen(Color.FromArgb(0x8C, 0xC9, 0xCF, 0xE6));
    readonly Rectangle _outline;
    readonly TextBlock _dragHint = new() { Text = "drag me", FontSize = 11.5, Foreground = Muted, Margin = new Thickness(0, 3, 0, 0), Visibility = Visibility.Collapsed };
    /// <summary>The "unlocked" value: "" locked (clicks go through to the game), "1" unlocked, "move" unlocked for a while from the tray.</summary>
    string _unlock = "";

    /// <summary>Locks or unlocks the panel ("unlocked" value): click-through and never focused, or draggable with its outline.</summary>
    void ApplyLock(string how)
    {
        if (how == _unlock) return;
        _unlock = how;
        var h = new WindowInteropHelper(this).Handle;
        if (h != IntPtr.Zero) Native.SetClickThrough(h, how.Length == 0);
        _outline.Visibility = how.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        ShowDragHint();
    }

    /// <summary>"drag me" while it is being moved from the tray, and under the pointer while it is unlocked.</summary>
    void ShowDragHint() => _dragHint.Visibility = _unlock == "move" || (_unlock.Length > 0 && IsMouseOver) ? Visibility.Visible : Visibility.Collapsed;

    static readonly SolidColorBrush FrameBrush = Frozen(Color.FromRgb(0x32, 0x3A, 0x66));   // frozen, like Muted
    readonly Border _frame;
    readonly TextBlock _alert;
    readonly TextBlock _acc;
    /// <summary>One line under the figures from the "notice" value ("Update ready · installs when you close the tracker").</summary>
    readonly TextBlock _note;
    static readonly Brush AccGood = Frozen(Color.FromRgb(0x62, 0xD9, 0x8B)), AccCheck = Frozen(Color.FromRgb(0xF2, 0xA3, 0x5E));

    /// <summary>
    /// The accuracy line under the figures ("✓ Nothing looked wrong", "2 to look at"), as the window's Accuracy chip
    /// works it out; null hides it (nothing to look at and not enough checked yet). Orange with things to look at.
    /// </summary>
    public void SetAccuracy(string? text, bool toCheck)
    {
        _look?.SetAccuracy(text, toCheck);
        _acc.Text = text ?? "";
        _acc.Foreground = toCheck ? AccCheck : AccGood;
        _acc.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }
    System.Windows.Threading.DispatcherTimer? _alertTimer;

    /// <summary>
    /// A rare drop: the panel's border and a line under the figures in the item's grade colour, for a few seconds
    /// (0.8, Steven 2026-10-22). A second alert while one shows replaces it and starts the time again.
    /// </summary>
    public void Alert(string text, Color color, double seconds = 5)
    {
        if (_look != null) return;   // the Card and the Ticker draw the drop from the window's "rare" values, held as long
        _alert.Text = text; _alert.Foreground = new SolidColorBrush(color); _alert.Visibility = Visibility.Visible;
        _frame.BorderBrush = new SolidColorBrush(color); _frame.BorderThickness = new Thickness(2);
        _alertTimer?.Stop();
        _alertTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
        _alertTimer.Tick += (_, _) => { _alertTimer?.Stop(); _alert.Visibility = Visibility.Collapsed; _frame.BorderBrush = FrameBrush; _frame.BorderThickness = new Thickness(1); };
        _alertTimer.Start();
    }

    /// <summary>
    /// How solid the panel's background is, 0.3 to 1 (Options › In game). Only the background: the figures stay fully
    /// readable at 30%, and the window's own opacity is left to follow the game (ShowAs).
    /// </summary>
    public void SetOpacity(double opacity)
    {
        var a = (byte)Math.Round(255 * Math.Clamp(opacity, 0.3, 1));
        _frame.Background = Frozen(Color.FromArgb(a, 0x0A, 0x0C, 0x18));
        _look?.SetOpacity(opacity);
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
        _row.Children.Clear(); _cells.Clear(); _labels.Clear(); _feedLine = null; _timeDot = null;
        for (int i = 0; i < _fields.Count; i++)
        {
            var key = _fields[i]; var f = HudFields.All.First(x => x.Key == key);
            var number = new TextBlock { Text = "", VerticalAlignment = VerticalAlignment.Bottom };
            var cell = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(i == 0 ? 0 : 20, 0, 0, 0) };
            switch (key)
            {
                case "time":
                    // the recording dot (red only during a run, the "run" value) and the clock
                    _timeDot = new Ellipse { Width = 8, Height = 8, Fill = Muted, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 7, 0) };
                    cell.Children.Add(_timeDot);
                    number.FontFamily = new FontFamily("Cascadia Mono, Consolas"); number.FontSize = 14; number.Foreground = new SolidColorBrush(Color.FromRgb(0xE9, 0xEF, 0xF7)); number.VerticalAlignment = VerticalAlignment.Center;
                    number.Text = "0:00";
                    break;
                case "feed":
                    // the newest line, then the rate: "● Spiritstone +7.5K | 1.27M an hour"; a flagged run says so instead
                    BuildFeed(cell);
                    _row.Children.Add(cell);
                    continue;
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
            if (f.Label.Length > 0)
            {
                var label = new TextBlock { Text = f.Label, FontSize = 12.5, Foreground = Muted, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(6, 0, 0, key == "rate" ? 2 : 1) };
                cell.Children.Add(label);
                _labels[key] = label;
            }
            _row.Children.Add(cell);
            _cells[key] = number;
        }
    }

    /// <summary>The rate while it warms up: there, but plainly not the figure yet.</summary>
    const double WarmOpacity = 0.55;
    /// <summary>The timer cell's dot: red during a run, grey otherwise.</summary>
    Ellipse? _timeDot;
    static readonly SolidColorBrush RunRed = Frozen(Color.FromRgb(0xFF, 0x6B, 0x5E));
    TextBlock? _feedText, _feedValue, _feedRate, _feedUnit, _flagText;
    FrameworkElement? _feedLine, _flagLine, _feedSep;

    static readonly SolidColorBrush Gold = Frozen(Color.FromRgb(0xF8, 0xD5, 0x98)), Ink = Frozen(Color.FromRgb(0xEE, 0xEC, 0xF6));

    /// <summary>The feed cell: a live dot, the newest line and its worth, a rule, the rate and its unit; and the flagged-run line it swaps to.</summary>
    void BuildFeed(StackPanel cell)
    {
        var dot = new Ellipse { Width = 6, Height = 6, Fill = Gold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Color.FromRgb(0xF8, 0xD5, 0x98), BlurRadius = 8, ShadowDepth = 0, Opacity = 0.7 } };
        cell.Children.Add(dot);
        var mono = new FontFamily("Cascadia Mono, Consolas");
        var line = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        _feedText = new TextBlock { FontSize = 12.5, Foreground = Ink, MaxWidth = 220, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        _feedValue = new TextBlock { FontSize = 12.5, FontFamily = mono, Foreground = Gold, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        line.Children.Add(_feedText); line.Children.Add(_feedValue);
        _feedSep = new Border { Width = 1, Height = 16, Background = Frozen(Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF)), Margin = new Thickness(10, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
        line.Children.Add(_feedSep);
        _feedRate = new TextBlock { FontSize = 12.5, FontFamily = mono, Foreground = Ink, VerticalAlignment = VerticalAlignment.Center };
        _feedUnit = new TextBlock { FontSize = 12, Foreground = Muted, Margin = new Thickness(4, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        line.Children.Add(_feedRate); line.Children.Add(_feedUnit);
        _feedLine = line; cell.Children.Add(line);
        var flag = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
        flag.Children.Add(new System.Windows.Shapes.Path { Data = Geometry.Parse("M3.5,14 V2.5 M3.5,3 H11.5 L9.9,5.8 L11.5,8.6 H3.5"), Stroke = Gold, StrokeThickness = 1.4, Width = 13, Height = 13, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        _flagText = new TextBlock { FontSize = 12.5, Foreground = Ink, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 260, TextTrimming = TextTrimming.CharacterEllipsis };
        flag.Children.Add(_flagText);
        flag.Children.Add(new TextBlock { Text = "fix in tracker", FontSize = 12.5, Foreground = Gold, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        _flagLine = flag; cell.Children.Add(flag);
        // the dot goes with the line, not with a flag
        cell.Tag = dot;
    }

    /// <summary>
    /// The figures by key (HudFields), as the window worked them out. A missing one hides with its label (2026-10-25: a
    /// blank loot left "loot / hr" standing alone), and the first one showing sits at the panel's edge.
    /// </summary>
    public void Update(IReadOnlyDictionary<string, string> values)
    {
        string V(string k) => values.TryGetValue(k, out var v) ? v : "";
        ShowAs(V("show"));
        ApplyLock(V("unlocked"));
        if (_look != null) { _look.Update(values); return; }
        // a start or stop's flash ("Run started") over a waiting notice, for its few seconds
        bool flash = V("flash").Length > 0;
        _note.Text = flash ? V("flash") : V("notice");
        _note.Foreground = flash ? Ink : Gold;
        _note.Visibility = _note.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (_timeDot != null) _timeDot.Fill = V("run") == "1" ? RunRed : Muted;
        bool warm = V("warm") == "1";
        if (_feedLine != null)
        {
            bool flagged = V("flag").Length > 0;
            _feedLine.Visibility = flagged ? Visibility.Collapsed : Visibility.Visible;
            _flagLine!.Visibility = flagged ? Visibility.Visible : Visibility.Collapsed;
            if (((FrameworkElement)_feedLine.Parent).Tag is UIElement dot) dot.Visibility = flagged ? Visibility.Collapsed : Visibility.Visible;
            _flagText!.Text = V("flag");
            _feedText!.Text = V("feedText").Length > 0 ? V("feedText") : "Waiting for the first count";
            _feedValue!.Text = V("feedValue");
            _feedRate!.Text = V("feedRate"); _feedUnit!.Text = V("feedRate").Length > 0 ? V("feedUnit") : "";
            _feedSep!.Visibility = V("feedRate").Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            _feedRate.Opacity = warm ? WarmOpacity : 1;
        }
        foreach (var (key, cell) in _cells)
        {
            cell.Text = values.TryGetValue(key, out var v) && v.Length > 0 ? v : "";
            if (cell.Parent is FrameworkElement box) box.Visibility = cell.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        // warming up ("warm"): the hour's figure reads "warming up", dimmed and without its "/ hr"
        if (_cells.TryGetValue("rate", out var rate)) rate.Opacity = warm ? WarmOpacity : 1;
        if (_labels.TryGetValue("rate", out var rateLabel)) rateLabel.Visibility = warm ? Visibility.Collapsed : Visibility.Visible;
        bool first = true;
        foreach (FrameworkElement c in _row.Children)
        {
            if (c.Visibility != Visibility.Visible) continue;
            c.Margin = new Thickness(first ? 0 : 20, 0, 0, 0); first = false;
        }
    }

    /// <summary>
    /// The panel in a look, drawn from these values as the layout renders draw it, to show inside another window (Options ›
    /// In game shows the three looks with the game closed). Nothing animates, and the live panel is left alone.
    /// </summary>
    public static FrameworkElement Preview(string look, IEnumerable<string>? fields, IReadOnlyDictionary<string, string> values, string? accuracy = null)
    {
        var h = new HudWindow(fields, look, still: true);
        if (accuracy != null) h.SetAccuracy(accuracy, true);
        h.Update(values);
        var face = (FrameworkElement)h.Content;
        h.Content = null;
        h.Close();
        return face;
    }
}
