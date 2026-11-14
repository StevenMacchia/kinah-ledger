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
/// </summary>
sealed class TimersOverlay : Window
{
    readonly StackPanel _rows = new();
    readonly StackPanel _menu = new() { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 6, 0, 0) };
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
        Content = new Border
        {
            Child = body, Padding = new Thickness(9, 5, 9, 6), CornerRadius = new CornerRadius(7),
            Background = new SolidColorBrush(Color.FromArgb(0xA6, 0x0B, 0x0C, 0x10)),
        };
        BuildMenu();
        SourceInitialized += (_, _) => { _hwnd = new WindowInteropHelper(this).Handle; ApplyStyle(); };
        MouseLeftButtonDown += (_, e) =>
        {
            if (e.OriginalSource is DependencyObject d && CraftWindow.Interactive(d)) return;
            try { DragMove(); } catch (InvalidOperationException) { }
            _prefs.X = Left; _prefs.Y = Top; _prefs.Save();
        };
        MouseRightButtonUp += (_, e) => { ToggleMenu(_menu.Visibility != Visibility.Visible); e.Handled = true; };
        // Ctrl held: the strip takes the mouse to be moved or set; let go, clicks pass to the game again
        _keys = new DispatcherTimer(TimeSpan.FromMilliseconds(120), DispatcherPriority.Background, (_, _) => PollKeys(), Dispatcher);
        _keys.Stop();
        Place();
        new WindowInteropHelper(this).EnsureHandle();   // the never-focus style is set before it is ever shown
        IsVisibleChanged += (_, _) => { if (IsVisible) _keys.Start(); else _keys.Stop(); };
        Closed += (_, _) => _keys.Stop();
    }

    /// <summary>Where it was left, else the top right of the main screen; always on a screen.</summary>
    void Place()
    {
        WindowStartupLocation = WindowStartupLocation.Manual;
        double l = _prefs.X ?? SystemParameters.WorkArea.Right - StripWidth - 40, t = _prefs.Y ?? SystemParameters.WorkArea.Top + 160;
        Left = Math.Clamp(l, SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 60);
        Top = Math.Clamp(t, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 30);
    }

    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
    const int VK_CONTROL = 0x11;

    void PollKeys()
    {
        bool ctrl = (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0;
        if (ctrl != _interactive) { _interactive = ctrl; ApplyStyle(); }
    }

    void ApplyStyle()
    {
        if (_hwnd == IntPtr.Zero) return;
        Native.SetClickThrough(_hwnd, _prefs.ClickThrough && !_interactive && _menu.Visibility != Visibility.Visible);
    }

    /// <summary>Click-through as the settings say, now.</summary>
    public void SetClickThrough(bool on) { BuildMenu(); ApplyStyle(); }

    void BuildMenu()
    {
        _menu.Children.Clear();
        Button Item(string text, Action act)
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
        if (!open && !_hasRows && IsVisible) Hide();
    }

    /// <summary>The rows: the watched timers near now, soonest first. None: hidden until one comes near (unless its menu is open).</summary>
    public void Show(IReadOnlyList<TimerBoard.Item> items, DateTime now)
    {
        _rows.Children.Clear();
        _hasRows = items.Count > 0;
        foreach (var i in items)
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
        bool show = _hasRows || _menu.Visibility == Visibility.Visible;
        if (!show) { if (IsVisible) Hide(); return; }
        if (!_hasRows) _rows.Children.Add(Text("Nothing due", Faint, 11));
        if (!IsVisible) Show();   // never activated (ShowActivated off, and the window's style)
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int index);
    /// <summary>For the options self-test: the window's extended style (never activates; click-through when set).</summary>
    internal long ExStyleForTest => _hwnd == IntPtr.Zero ? 0 : GetWindowLongPtr(_hwnd, -20).ToInt64();
    /// <summary>For the options self-test and renders: the strip as if Ctrl were held, or let go.</summary>
    internal void InteractiveForTest(bool on) { _interactive = on; ApplyStyle(); }

    static TextBlock Text(string s, Color c, double size, FontWeight? w = null, bool wrap = false) => new()
    {
        Text = s, FontSize = size, Foreground = new SolidColorBrush(c), FontFamily = HudKit.Text, FontWeight = w ?? FontWeights.Normal,
        TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
    };
}
