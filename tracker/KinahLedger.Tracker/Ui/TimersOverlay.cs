using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using KinahLedger.Session;

namespace KinahLedger.Ui;

/// <summary>
/// The separate timer strip (Settings › Timers › Separate timer overlay; by default the timers are a line on top of the Kinah
/// overlay, Ui/HudWindow.Timers.cs). A sibling of the Kinah overlay (Steven, 2026-11-14: "it doesnt match aesthetic of the
/// kinah tracker at all"): the Kinah strip's own surface, corner, padding, faces, sizes and colours, and its opacity and size
/// from Options › In game. One line, "● Shugo 4:52 | Kashapa 11:59", soonest first, gold in a timer's last 5 minutes;
/// shown only while a watched timer is within the minutes set, hidden otherwise.
///
/// It moves the way the Kinah overlay does ("Nothing else do u have to go in options to move ui, just click and drag"):
/// press anywhere on it and drag; it remembers the spot. The first time it shows, with no spot kept, it sits just above the
/// Kinah overlay. Click-through is a setting, off by default; with it on, the strip's left edge stays grabbable and shows a
/// grip while the pointer is over the strip, so it can still be dragged. It never takes focus and stays out of Alt+Tab.
/// </summary>
sealed class TimersOverlay : Window
{
    readonly StackPanel _line = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    readonly Border _frame;
    readonly FrameworkElement _grip;
    readonly Ellipse _dot;
    readonly TimerPrefs _prefs;
    readonly DispatcherTimer _hover;
    IntPtr _hwnd;
    bool _gripLive, _placed;

    /// <summary>Where the Kinah overlay is now (WPF units, its window's box), null when it isn't shown: the first spot is just above it.</summary>
    public Func<Rect?>? KinahBox { get; set; }

    // the Kinah strip's own (Ui/HudWindow.cs): surface, line, faces and colours
    static readonly Color Surface = Color.FromRgb(0x0B, 0x0C, 0x10);
    static readonly SolidColorBrush FrameLine = Frozen(Color.FromRgb(0x23, 0x27, 0x36)), Ink = Frozen(Color.FromRgb(0xED, 0xEF, 0xF5)),
        Gold = Frozen(Color.FromRgb(0xF5, 0xB9, 0x42)), Muted = Frozen(Color.FromRgb(0xA2, 0xA8, 0xBA)), Rule = Frozen(Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF));
    static SolidColorBrush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
    /// <summary>The grabbable left edge with click-through on, in WPF units at 1x.</summary>
    const double GripWidth = 16;

    public TimersOverlay(TimerPrefs prefs)
    {
        _prefs = prefs;
        Title = "Kinah Ledger timers";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        FontFamily = HudKit.Text;
        FontWeight = FontWeights.Medium;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        _dot = new Ellipse { Width = 6, Height = 6, Fill = Gold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Color.FromRgb(0xF5, 0xB9, 0x42), BlurRadius = 8, ShadowDepth = 0, Opacity = 0.7 } };
        // the grip: two columns of three dots, shown at the left edge while it can be grabbed there (click-through on)
        var grip = new Canvas { Width = 7, Height = 11, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
        for (int c = 0; c < 2; c++) for (int r = 0; r < 3; r++)
        {
            var d = new Ellipse { Width = 2.4, Height = 2.4, Fill = Muted };
            Canvas.SetLeft(d, c * 4.2); Canvas.SetTop(d, r * 4.2); grip.Children.Add(d);
        }
        _grip = grip;
        var row = new StackPanel { Orientation = Orientation.Horizontal, Children = { _grip, _dot, _line } };
        _frame = new Border
        {
            Background = Frozen(Color.FromArgb(0xF0, Surface.R, Surface.G, Surface.B)), BorderBrush = FrameLine, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 6, 12, 6), Child = row, Cursor = Cursors.SizeAll,
        };
        Content = _frame;
        SourceInitialized += (_, _) => { _hwnd = new WindowInteropHelper(this).Handle; ApplyStyle(); };
        // plain click and drag, as the Kinah overlay: the spot is kept on a screen and remembered
        MouseLeftButtonDown += (_, _) =>
        {
            try { DragMove(); } catch (InvalidOperationException) { }
            KeepOnScreen();
            _prefs.X = Left; _prefs.Y = Top; _prefs.Save();
            _placed = true;
        };
        // with click-through on, the pointer over the strip shows the grip, and over the grip the strip takes the mouse
        _hover = new DispatcherTimer(TimeSpan.FromMilliseconds(60), DispatcherPriority.Background, (_, _) => PollHover(), Dispatcher);
        _hover.Stop();
        IsVisibleChanged += (_, _) => { if (IsVisible && _prefs.ClickThrough) _hover.Start(); else _hover.Stop(); };
        Closed += (_, _) => _hover.Stop();
        new WindowInteropHelper(this).EnsureHandle();   // the never-focus style is set before it is ever shown
        WindowStartupLocation = WindowStartupLocation.Manual;
        if (_prefs.X is { } x && _prefs.Y is { } y) { Left = x; Top = y; _placed = true; KeepOnScreen(); }
        else { Left = -32000; Top = -32000; }   // placed above the Kinah overlay once it has laid out (PlaceFirst)
    }

    /// <summary>Opacity of the surface and the size, as the Kinah overlay's (Options › In game).</summary>
    public void Match(double opacity, double scale)
    {
        var a = (byte)Math.Round(255 * Math.Clamp(opacity, 0.3, 1));
        _frame.Background = Frozen(Color.FromArgb(a, Surface.R, Surface.G, Surface.B));
        var s = Math.Clamp(scale, HudPrefs.MinScale, HudPrefs.MaxScale);
        _frame.LayoutTransform = Math.Abs(s - 1) < 0.001 ? Transform.Identity : new ScaleTransform(s, s);
    }

    [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);
    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }

    /// <summary>The pointer in WPF units, through this window's DPI.</summary>
    Point? Pointer()
    {
        if (!GetCursorPos(out var p)) return null;
        double k = 1;
        try { k = VisualTreeHelper.GetDpi(this).DpiScaleX; } catch (Exception) { }
        return new Point(p.X / k, p.Y / k);
    }

    void PollHover()
    {
        if (!_prefs.ClickThrough) { SetGrip(false, false); return; }
        if (Pointer() is not { } p) return;
        bool over = p.X >= Left && p.X < Left + ActualWidth && p.Y >= Top && p.Y < Top + ActualHeight;
        double gripEdge = Left + (GripWidth + 10) * Scale;
        SetGrip(over, over && p.X < gripEdge);
    }

    double Scale => _frame.LayoutTransform is ScaleTransform t ? t.ScaleX : 1;

    /// <summary>The grip shown (pointer over the strip) and live (pointer on it: the strip takes the mouse for a drag).</summary>
    void SetGrip(bool shown, bool live)
    {
        _grip.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
        if (live == _gripLive) return;
        _gripLive = live;
        ApplyStyle();
    }

    void ApplyStyle()
    {
        if (_hwnd == IntPtr.Zero) return;
        Native.SetClickThrough(_hwnd, _prefs.ClickThrough && !_gripLive);
    }

    /// <summary>Click-through as the settings say, now (off by default: it drags like the Kinah overlay).</summary>
    public void SetClickThrough(bool on)
    {
        _gripLive = false;
        _grip.Visibility = Visibility.Collapsed;
        ApplyStyle();
        if (on && IsVisible) _hover.Start(); else _hover.Stop();
    }

    /// <summary>
    /// The first spot, with none kept: just above the Kinah overlay, their left edges lined up (6 px between, as the timer
    /// line sits on it); with the Kinah overlay off, the top centre of the main screen, where the Kinah overlay would start.
    /// </summary>
    void PlaceFirst()
    {
        UpdateLayout();
        double w = ActualWidth, h = ActualHeight;
        if (KinahBox?.Invoke() is { } k) { Left = k.Left; Top = k.Top - h - 6; }
        else { Left = SystemParameters.WorkArea.Left + (SystemParameters.WorkArea.Width - w) / 2; Top = SystemParameters.WorkArea.Top + 12; }
        KeepOnScreen();
        _placed = true;
    }

    /// <summary>Reset position: the spot forgotten; it sits above the Kinah overlay again (now if shown, else the next time).</summary>
    public void ResetSpot()
    {
        _prefs.X = null; _prefs.Y = null; _prefs.Save();
        _placed = false;
        if (IsVisible) PlaceFirst();
    }

    /// <summary>
    /// The strip wholly inside one screen's work area: the screen it overlaps most, else the nearest. Screens are in device
    /// pixels and the window in WPF units, so through this window's DPI.
    /// </summary>
    public void KeepOnScreen()
    {
        double scale = 1;
        try { scale = VisualTreeHelper.GetDpi(this).DpiScaleX; } catch (Exception) { }
        double w = ActualWidth > 0 ? ActualWidth : 180, h = ActualHeight > 0 ? ActualHeight : 30;
        var px = new System.Drawing.Rectangle((int)Math.Round(Left * scale), (int)Math.Round(Top * scale), (int)Math.Round(w * scale), (int)Math.Round(h * scale));
        var area = System.Windows.Forms.Screen.FromRectangle(px).WorkingArea;
        int x = Math.Clamp(px.X, area.Left, Math.Max(area.Left, area.Right - px.Width));
        int y = Math.Clamp(px.Y, area.Top, Math.Max(area.Top, area.Bottom - px.Height));
        Left = x / scale; Top = y / scale;
    }

    /// <summary>The watched timers near now, soonest first. None: hidden until one comes near.</summary>
    public void Show(IReadOnlyList<TimerBoard.Item> items, DateTime now)
    {
        if (items.Count == 0) { if (IsVisible) Hide(); return; }
        _line.Children.Clear();
        bool anyGold = false;
        for (int n = 0; n < items.Count; n++)
        {
            var i = items[n];
            if (n > 0) _line.Children.Add(new Border { Width = 1, Height = 16, Background = Rule, Margin = new Thickness(10, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center });
            var left = i.AtPc is { } at ? at - now : TimeSpan.Zero;
            bool gold = left <= TimeSpan.FromMinutes(TimerPrefs.GoldMinutes);
            anyGold |= gold;
            _line.Children.Add(new TextBlock { Text = i.Short, FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = Ink, VerticalAlignment = VerticalAlignment.Center });
            var c = new TextBlock { Text = TimerBoard.Clock(left), FontSize = 13, FontWeight = FontWeights.Bold, Foreground = gold ? Gold : Muted, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            System.Windows.Documents.Typography.SetNumeralAlignment(c, FontNumeralAlignment.Tabular);
            _line.Children.Add(c);
        }
        _dot.Fill = anyGold ? Gold : Muted;
        if (!IsVisible) Show();   // never activated (ShowActivated off, and the window's style)
        if (!_placed) PlaceFirst();
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int index);
    /// <summary>For the options self-test: the window's extended style (never activates; click-through when set).</summary>
    internal long ExStyleForTest => _hwnd == IntPtr.Zero ? 0 : GetWindowLongPtr(_hwnd, -20).ToInt64();
    /// <summary>For the options self-test and renders: the pointer over the strip (shown) and on its grip (live).</summary>
    internal void GripForTest(bool shown, bool live) => SetGrip(shown, live);
    internal bool GripShownForTest => _grip.Visibility == Visibility.Visible;
    /// <summary>For the options self-test: the timers on the line, and the surface (corner, padding, line colour).</summary>
    internal int ItemsForTest => _line.Children.OfType<TextBlock>().Count() / 2;
    internal Border FrameForTest => _frame;
}
