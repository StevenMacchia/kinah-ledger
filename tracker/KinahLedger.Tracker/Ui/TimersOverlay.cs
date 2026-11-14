using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using KinahLedger.Session;

namespace KinahLedger.Ui;

/// <summary>
/// The timers overlay (0.10.85, Steven 2026-11-14: "a separate overlay ... with a light chime sound, separate from the parser
/// and separate from the kinah tracker"): a small panel over the game with the next few events and world bosses, counting
/// down, shown only while one is within a few minutes (5 by default) and hidden otherwise. Its own window, apart from the Kinah panel and the parser: always on top, never takes focus (a click on it never
/// brings the tracker over the game), out of Alt+Tab, and with click-through on, clicks pass to the game. Off, it drags from
/// anywhere and remembers where it was put (timers.json). Which timers it shows, how many and the chime are set in the
/// Timers window's Customize (Ui/MainWindow.Timers.cs).
/// </summary>
sealed class TimersOverlay : Window
{
    readonly StackPanel _rows = new();
    readonly TextBlock _title;
    readonly TimerPrefs _prefs;
    IntPtr _hwnd;

    static readonly Color Ink = Color.FromRgb(0xED, 0xEF, 0xF5), Faint = Color.FromRgb(0x9A, 0xA1, 0xB4), Good = Color.FromRgb(0x3D, 0xD6, 0x8C),
        Gold = Color.FromRgb(0xF5, 0xB9, 0x42), Warn = Color.FromRgb(0xF7, 0xA0, 0x72);
    const double Width0 = 250;

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
        _title = new TextBlock { Text = "TIMERS", FontSize = 10.5, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Faint), Margin = new Thickness(0, 0, 0, 4), FontFamily = HudKit.Text };
        var body = new StackPanel { Width = Width0 };
        body.Children.Add(_title);
        body.Children.Add(_rows);
        Content = new Border
        {
            Child = body, Padding = new Thickness(12, 8, 12, 9), CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(Color.FromArgb(0xD8, 0x0B, 0x0C, 0x10)), BorderBrush = new SolidColorBrush(Color.FromArgb(0x60, 0x2E, 0x33, 0x46)), BorderThickness = new Thickness(1),
            Cursor = Cursors.SizeAll,
        };
        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            Native.SetClickThrough(_hwnd, _prefs.ClickThrough);
        };
        MouseLeftButtonDown += (_, _) =>
        {
            try { DragMove(); } catch (InvalidOperationException) { }
            _prefs.X = Left; _prefs.Y = Top; _prefs.Save();
        };
        Place();
        new WindowInteropHelper(this).EnsureHandle();   // the never-focus style is set before it is ever shown
    }

    /// <summary>Where it was left, else the top right of the main screen; always on a screen.</summary>
    void Place()
    {
        WindowStartupLocation = WindowStartupLocation.Manual;
        double l = _prefs.X ?? SystemParameters.WorkArea.Right - Width0 - 60, t = _prefs.Y ?? SystemParameters.WorkArea.Top + 140;
        Left = Math.Clamp(l, SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 80);
        Top = Math.Clamp(t, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 40);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int index);
    /// <summary>For the options self-test: the window's extended style (never activates; click-through when set).</summary>
    internal long ExStyleForTest => _hwnd == IntPtr.Zero ? 0 : GetWindowLongPtr(_hwnd, -20).ToInt64();

    /// <summary>Click-through on or off, now.</summary>
    public void SetClickThrough(bool on) { if (_hwnd != IntPtr.Zero) Native.SetClickThrough(_hwnd, on); }

    /// <summary>The rows: the timers near now, soonest first. None: the overlay is hidden until one comes near.</summary>
    public void Show(IReadOnlyList<TimerBoard.Item> items, DateTime now)
    {
        _rows.Children.Clear();
        if (items.Count == 0) { if (IsVisible) Hide(); return; }
        foreach (var i in items)
        {
            var g = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            bool soon = i.State == TimerBoard.Kind.Waiting && i.AtPc is { } at && at - now <= TimeSpan.FromMinutes(Math.Max(10, _prefs.ChimeMinutes));
            var tone = i.State switch { TimerBoard.Kind.On => Good, TimerBoard.Kind.Due => Warn, _ => soon ? Gold : Ink };
            var left = new StackPanel();
            left.Children.Add(Text(i.Name, i.State == TimerBoard.Kind.Waiting && !soon ? Ink : tone, 12.5));
            left.Children.Add(Text(i.Group + (i.Sub.Length > 0 ? " · " + i.Sub : ""), Faint, 10.5));
            g.Children.Add(left);
            var right = Text(i.Big, tone, 13.5, FontWeights.Medium);
            right.Margin = new Thickness(10, 0, 0, 0); right.VerticalAlignment = VerticalAlignment.Center;
            System.Windows.Documents.Typography.SetNumeralAlignment(right, FontNumeralAlignment.Tabular);
            Grid.SetColumn(right, 1); g.Children.Add(right);
            _rows.Children.Add(g);
        }
        if (!IsVisible) Show();   // never activated (ShowActivated off, and the window's style)
    }

    static TextBlock Text(string s, Color c, double size, FontWeight? w = null, bool wrap = false) => new()
    {
        Text = s, FontSize = size, Foreground = new SolidColorBrush(c), FontFamily = HudKit.Text, FontWeight = w ?? FontWeights.Normal,
        TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
    };
}
