using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using KinahLedger.Session;

namespace KinahLedger.Ui;

/// <summary>
/// The timers overlay (0.10.85, Steven 2026-11-14: "the timers for events + world bosses ... in the same overlay window.
/// 5 min til shugo, dimensional invasion, spacetime rift and the bosses, and it should be minimal where you can just put
/// it somewhere out of sight out of mind"): a narrow translucent strip of short rows, "Shugo 4:52", "Kusan 12:10", soonest
/// first, gold in a timer's last 5 minutes. It shows only while a watched timer is within the minutes set (5 by default)
/// and is hidden otherwise.
///
/// Its own window, apart from the Kinah panel and the parser: always on top, never takes focus, out of Alt+Tab, and
/// click-through by default, so it never blocks the game. While Ctrl is held it takes the mouse: drag it anywhere (it
/// remembers where), right-click for its small menu (click-through, Settings, hide). Which timers it shows and the chime are
/// set in Options › Timers.
///
/// Placement (Steven, 2026-11-14: "let the user customize where the timer goes even if one is not up"): Move overlay, on
/// the Timers page and in Settings › Timers, shows the strip at once, outlined and clickable, with the next watched timers
/// (or a sample when none are queued) and a Done button. Drag it anywhere; Done or Esc keeps the spot (inside the nearest
/// screen's work area, whatever the monitors), and it goes back to hidden until a timer is due.
/// </summary>
sealed class TimersOverlay : Window
{
    readonly StackPanel _rows = new();
    readonly StackPanel _menu = new() { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 6, 0, 0) };
    readonly StackPanel _placeBar = new() { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 6, 0, 0) };
    readonly Border _frame;
    bool _placing;
    IReadOnlyList<TimerBoard.Item> _sample = Array.Empty<TimerBoard.Item>();
    /// <summary>Placement ended (Done or Esc); the spot is saved.</summary>
    public Action? PlacementDone { get; set; }
    /// <summary>Reset was pressed in the placement bar (the spot is back at its default).</summary>
    public Action? ResetPressed { get; set; }
    readonly TimerPrefs _prefs;
    readonly DispatcherTimer _keys;
    IntPtr _hwnd;
    bool _interactive, _hasRows;

    /// <summary>The menu's actions, given by the main window.</summary>
    public Action? OpenSettings { get; set; }
    public Action? HideOverlay { get; set; }
    public Action<bool>? SetClickThroughPref { get; set; }

    static readonly Color Ink = Color.FromRgb(0xE4, 0xE7, 0xEE), Faint = Color.FromRgb(0x9A, 0xA1, 0xB4), Gold = Color.FromRgb(0xF5, 0xB9, 0x42);
    const double StripWidth = 150;

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
        var body = new StackPanel { Width = StripWidth };
        body.Children.Add(_rows);
        body.Children.Add(_menu);
        body.Children.Add(_placeBar);
        _frame = new Border
        {
            Child = body, Padding = new Thickness(9, 5, 9, 6), CornerRadius = new CornerRadius(7),
            Background = new SolidColorBrush(Color.FromArgb(0xA6, 0x0B, 0x0C, 0x10)),
            BorderThickness = new Thickness(1.5), BorderBrush = Brushes.Transparent,
        };
        Content = _frame;
        BuildMenu();
        BuildPlaceBar();
        SourceInitialized += (_, _) => { _hwnd = new WindowInteropHelper(this).Handle; ApplyStyle(); };
        MouseLeftButtonDown += (_, e) =>
        {
            if (e.OriginalSource is DependencyObject d && CraftWindow.Interactive(d)) return;
            try { DragMove(); } catch (InvalidOperationException) { }
            KeepOnScreen();
            _prefs.X = Left; _prefs.Y = Top; _prefs.Save();
        };
        MouseRightButtonUp += (_, e) => { if (!_placing) ToggleMenu(_menu.Visibility != Visibility.Visible); e.Handled = true; };
        // Ctrl held: the strip takes the mouse to be moved or set; let go, clicks pass to the game again
        _keys = new DispatcherTimer(TimeSpan.FromMilliseconds(120), DispatcherPriority.Background, (_, _) => PollKeys(), Dispatcher);
        _keys.Stop();
        Place();
        new WindowInteropHelper(this).EnsureHandle();   // the never-focus style is set before it is ever shown
        IsVisibleChanged += (_, _) => { if (IsVisible) _keys.Start(); else _keys.Stop(); };
        Closed += (_, _) => _keys.Stop();
    }

    /// <summary>The spot it starts at with none saved: the top right of the main screen's work area.</summary>
    public static Point DefaultSpot => new(SystemParameters.WorkArea.Right - StripWidth - 40, SystemParameters.WorkArea.Top + 160);

    /// <summary>Where it was left, else the default spot; always on a screen.</summary>
    void Place()
    {
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = _prefs.X ?? DefaultSpot.X; Top = _prefs.Y ?? DefaultSpot.Y;
        KeepOnScreen();
    }

    /// <summary>Back to the default spot, and that saved (no spot kept).</summary>
    public void ResetSpot()
    {
        _prefs.X = null; _prefs.Y = null; _prefs.Save();
        Place();
    }

    /// <summary>
    /// The strip wholly inside one screen's work area: the screen it overlaps most, else the nearest (a monitor unplugged, a
    /// spot from a bigger desktop). Screens are in device pixels and the window in WPF units, so through this window's DPI.
    /// </summary>
    public void KeepOnScreen()
    {
        double scale = 1;
        try { scale = VisualTreeHelper.GetDpi(this).DpiScaleX; } catch (Exception) { }
        double w = ActualWidth > 0 ? ActualWidth : StripWidth + 20, h = ActualHeight > 0 ? ActualHeight : 60;
        var px = new System.Drawing.Rectangle((int)Math.Round(Left * scale), (int)Math.Round(Top * scale), (int)Math.Round(w * scale), (int)Math.Round(h * scale));
        var area = System.Windows.Forms.Screen.FromRectangle(px).WorkingArea;
        int x = Math.Clamp(px.X, area.Left, Math.Max(area.Left, area.Right - px.Width));
        int y = Math.Clamp(px.Y, area.Top, Math.Max(area.Top, area.Bottom - px.Height));
        Left = x / scale; Top = y / scale;
    }

    /* ---------------- placement ---------------- */

    /// <summary>True while the player is placing it: shown, outlined, clickable.</summary>
    public bool Placing => _placing;

    void BuildPlaceBar()
    {
        _placeBar.Children.Clear();
        _placeBar.Children.Add(new Border { Height = 1, Background = new SolidColorBrush(Color.FromArgb(0x50, 0x9A, 0xA1, 0xB4)), Margin = new Thickness(0, 0, 0, 4) });
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.Children.Add(Text("Drag to place", Faint, 10.5));
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        var reset = MenuButton("Reset", () => { ResetSpot(); ResetPressed?.Invoke(); });
        reset.Margin = new Thickness(0, 0, 10, 0);
        var done = MenuButton("Done", () => FinishPlacing());
        done.FontWeight = FontWeights.SemiBold;
        buttons.Children.Add(reset); buttons.Children.Add(done);
        Grid.SetColumn(buttons, 1); g.Children.Add(buttons);
        _placeBar.Children.Add(g);
    }

    /// <summary>Shown now, outlined and clickable, with <paramref name="sample"/> when no timer is due; Done or Esc ends it.</summary>
    public void StartPlacing(IReadOnlyList<TimerBoard.Item> sample, DateTime now)
    {
        _sample = sample;
        _placing = true;
        ToggleMenu(false);
        _frame.BorderBrush = new SolidColorBrush(Gold);
        _placeBar.Visibility = Visibility.Visible;
        ApplyStyle();
        Show(Array.Empty<TimerBoard.Item>(), now);
        KeepOnScreen();
    }

    /// <summary>Placement over: the spot kept (on a screen) and saved, clicks through again, hidden until a timer is due.</summary>
    public void FinishPlacing()
    {
        if (!_placing) return;
        _placing = false;
        _frame.BorderBrush = Brushes.Transparent;
        _placeBar.Visibility = Visibility.Collapsed;
        KeepOnScreen();
        _prefs.X = Left; _prefs.Y = Top; _prefs.Save();
        ApplyStyle();
        if (!_hasRows && IsVisible) Hide();
        PlacementDone?.Invoke();
    }

    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
    const int VK_CONTROL = 0x11, VK_ESCAPE = 0x1B;

    void PollKeys()
    {
        if (_placing && (GetAsyncKeyState(VK_ESCAPE) & 0x8000) != 0) { FinishPlacing(); return; }   // it never has focus, so Esc is read like Ctrl
        bool ctrl = (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0;
        if (ctrl != _interactive) { _interactive = ctrl; ApplyStyle(); }
    }

    void ApplyStyle()
    {
        if (_hwnd == IntPtr.Zero) return;
        Native.SetClickThrough(_hwnd, _prefs.ClickThrough && !_interactive && !_placing && _menu.Visibility != Visibility.Visible);
    }

    /// <summary>Click-through as the settings say, now.</summary>
    public void SetClickThrough(bool on) { BuildMenu(); ApplyStyle(); }

    Button MenuButton(string text, Action act)
    {
            var b = new Button
            {
                Content = text, FontSize = 11, Padding = new Thickness(0, 2, 0, 2), HorizontalContentAlignment = HorizontalAlignment.Left,
                Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = new SolidColorBrush(Ink), Cursor = Cursors.Hand,
                FontFamily = HudKit.Text, Focusable = false,
            };
            // plain text, not the app's button look: a bare presenter, a faint tint on hover
            var face = new FrameworkElementFactory(typeof(Border));
            face.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            face.SetValue(Border.PaddingProperty, new Thickness(0, 2, 0, 2));
            var content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Left);
            face.AppendChild(content);
            b.Template = new ControlTemplate(typeof(Button)) { VisualTree = face };
            b.MouseEnter += (_, _) => b.Foreground = new SolidColorBrush(Gold);
            b.MouseLeave += (_, _) => b.Foreground = new SolidColorBrush(Ink);
            b.Click += (_, _) => act();
            return b;
    }

    void BuildMenu()
    {
        _menu.Children.Clear();
        Button Item(string text, Action act) => MenuButton(text, act);
        _menu.Children.Add(new Border { Height = 1, Background = new SolidColorBrush(Color.FromArgb(0x50, 0x9A, 0xA1, 0xB4)), Margin = new Thickness(0, 0, 0, 4) });
        _menu.Children.Add(Item(_prefs.ClickThrough ? "Click-through: on" : "Click-through: off", () => { SetClickThroughPref?.Invoke(!_prefs.ClickThrough); BuildMenu(); ApplyStyle(); }));
        _menu.Children.Add(Item("Settings…", () => { ToggleMenu(false); OpenSettings?.Invoke(); }));
        _menu.Children.Add(Item("Hide the overlay", () => { ToggleMenu(false); HideOverlay?.Invoke(); }));
        _menu.Children.Add(Item("Done", () => ToggleMenu(false)));
        _menu.Children.Add(Text("Hold Ctrl to move or set it.", Faint, 10, wrap: true));
    }

    /// <summary>The small menu open or shut (open, the strip stays clickable and shown).</summary>
    public void ToggleMenu(bool open)
    {
        if (open) BuildMenu();
        _menu.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        ApplyStyle();
        if (!open && !_hasRows && !_placing && IsVisible) Hide();
    }

    /// <summary>The rows: the watched timers near now, soonest first. None: hidden until one comes near (unless its menu is open).</summary>
    public void Show(IReadOnlyList<TimerBoard.Item> items, DateTime now)
    {
        _rows.Children.Clear();
        _hasRows = items.Count > 0;
        // placing with nothing due: the next watched timers (or a sample) stand in, so there is a strip to place
        foreach (var i in _hasRows || !_placing ? items : _sample)
        {
            var left = i.AtPc is { } at ? at - now : TimeSpan.Zero;
            var tone = left <= TimeSpan.FromMinutes(TimerPrefs.GoldMinutes) ? Gold : Ink;
            var g = new Grid { Margin = new Thickness(0, 1, 0, 1) };
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.Children.Add(Text(i.Short, tone, 12));
            var c = Text(TimerBoard.Clock(left), tone, 12, FontWeights.Medium);
            c.Margin = new Thickness(8, 0, 0, 0);
            System.Windows.Documents.Typography.SetNumeralAlignment(c, FontNumeralAlignment.Tabular);
            Grid.SetColumn(c, 1); g.Children.Add(c);
            _rows.Children.Add(g);
        }
        bool show = _hasRows || _placing || _menu.Visibility == Visibility.Visible;
        if (!show) { if (IsVisible) Hide(); return; }
        if (!_hasRows && !(_placing && _sample.Count > 0)) _rows.Children.Add(Text("Nothing due", Faint, 11));
        if (!IsVisible) Show();   // never activated (ShowActivated off, and the window's style)
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int index);
    /// <summary>For the options self-test: the window's extended style (never activates; click-through when set).</summary>
    internal long ExStyleForTest => _hwnd == IntPtr.Zero ? 0 : GetWindowLongPtr(_hwnd, -20).ToInt64();
    /// <summary>For the options self-test and renders: the strip as if Ctrl were held, or let go.</summary>
    internal void InteractiveForTest(bool on) { _interactive = on; ApplyStyle(); }
    /// <summary>For the options self-test: the rows drawn.</summary>
    internal int RowsForTest => _rows.Children.OfType<Grid>().Count();

    static TextBlock Text(string s, Color c, double size, FontWeight? w = null, bool wrap = false) => new()
    {
        Text = s, FontSize = size, Foreground = new SolidColorBrush(c), FontFamily = HudKit.Text, FontWeight = w ?? FontWeights.Normal,
        TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
    };
}
