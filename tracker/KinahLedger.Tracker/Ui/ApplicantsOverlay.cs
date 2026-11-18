using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using KinahLedger.Session;

namespace KinahLedger.Ui;

/// <summary>
/// The Applicants window (Steven, 2026-11-18: "a pop up window for party leaders who are waiting for people in a queue to
/// display the people who are asking to be invited to the party ... Game only shows one at a time but sometimes u have like
/// 10 requests"): every pending request to join the group the player leads, newest at the bottom, in the overlay's theme
/// (Ui/ApplicantsFace.cs). It shows only while someone is waiting and folds away when the list empties. Reading only: the
/// game keeps accepting and declining.
///
/// It moves as the timers' strip does (Ui/TimersOverlay.cs): press anywhere on it and drag; it remembers the spot. The first
/// time, with no spot kept, it sits just under the Kinah overlay (or at the left of the main screen's middle). Click-through
/// is a setting, off by default; with it on, the left edge stays grabbable and shows a grip while the pointer is over it. It
/// never takes focus and stays out of Alt+Tab.
/// </summary>
sealed class ApplicantsOverlay : Window
{
    readonly Grid _host = new();
    readonly Border _faceSlot = new();
    readonly FrameworkElement _grip;
    readonly ApplicantPrefs _prefs;
    readonly DispatcherTimer _hover;
    IntPtr _hwnd;
    bool _gripLive, _placed;
    string _sig = "";
    double _opacity = 0.94, _scale = 1;

    /// <summary>Where the Kinah overlay is now (WPF units), null when it isn't shown: the first spot is just under it.</summary>
    public Func<Rect?>? KinahBox { get; set; }
    /// <summary>The overlay's theme now (HudThemes.Of the look in use).</summary>
    public Func<string> Theme { get; set; } = () => HudThemes.Classic;

    const double GripWidth = 16;

    /// <summary>For the self-tests and renders: windows made while it is set are transparent and parked off every screen.</summary>
    internal static bool InvisibleForTest;

    public ApplicantsOverlay(ApplicantPrefs prefs)
    {
        _prefs = prefs;
        if (InvisibleForTest) Opacity = 0;
        Title = InvisibleForTest ? "Kinah Ledger applicants (parked test window)" : "Kinah Ledger applicants";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        FontFamily = HudKit.Text;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        // the grip: two columns of three dots at the left edge, shown while it can be grabbed there (click-through on)
        var grip = new Canvas { Width = 7, Height = 11, Margin = new Thickness(4, 0, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed, IsHitTestVisible = false };
        for (int c = 0; c < 2; c++) for (int r = 0; r < 3; r++)
        {
            var d = new Ellipse { Width = 2.4, Height = 2.4, Fill = HudKit.Muted };
            Canvas.SetLeft(d, c * 4.2); Canvas.SetTop(d, r * 4.2); grip.Children.Add(d);
        }
        _grip = grip;
        _host.Children.Add(_faceSlot);
        _host.Children.Add(_grip);
        _host.Cursor = System.Windows.Input.Cursors.SizeAll;
        Content = _host;
        SourceInitialized += (_, _) => { _hwnd = new WindowInteropHelper(this).Handle; ApplyStyle(); };
        MouseLeftButtonDown += (_, _) =>
        {
            try { DragMove(); } catch (InvalidOperationException) { }
            KeepOnScreen();
            _prefs.X = Left; _prefs.Y = Top; _prefs.Save();
            _placed = true;
        };
        _hover = new DispatcherTimer(TimeSpan.FromMilliseconds(60), DispatcherPriority.Background, (_, _) => PollHover(), Dispatcher);
        _hover.Stop();
        IsVisibleChanged += (_, _) => { if (IsVisible && _prefs.ClickThrough) _hover.Start(); else _hover.Stop(); };
        Closed += (_, _) => _hover.Stop();
        new WindowInteropHelper(this).EnsureHandle();   // the never-focus style is set before it is ever shown
        WindowStartupLocation = WindowStartupLocation.Manual;
        if (_prefs.X is { } x && _prefs.Y is { } y && !InvisibleForTest) { Left = x; Top = y; _placed = true; KeepOnScreen(); }
        else { Left = -32000; Top = -32000; }
    }

    /// <summary>The Kinah overlay's opacity and size (Options › Buddy): the face is drawn again at them.</summary>
    public void Match(double opacity, double scale)
    {
        _opacity = Math.Clamp(opacity, 0.3, 1);
        _scale = Math.Clamp(scale, HudPrefs.MinScale, HudPrefs.MaxScale);
        _host.LayoutTransform = Math.Abs(_scale - 1) < 0.001 ? Transform.Identity : new ScaleTransform(_scale, _scale);
        _sig = "";
    }

    /// <summary>
    /// The pending applicants at <paramref name="now"/>: none hides it (it folds away), else they are drawn (again only when
    /// a row, the theme or a row's "ago" changes) and it shows, never activated.
    /// </summary>
    public void Show(IReadOnlyList<PartyApplicants.Applicant> items, DateTime now)
    {
        if (items.Count == 0) { if (IsVisible) Hide(); _sig = ""; return; }
        string theme = Theme();
        var sig = theme + "|" + _opacity + "|" + string.Join(",", items.Select(a => a.Key + ":" + PartyApplicants.Ago(a.At, now)));
        if (sig != _sig)
        {
            _sig = sig;
            _faceSlot.Child = ApplicantsFace.Build(theme, items, now, _opacity);
        }
        if (!IsVisible) Show();
        if (!_placed) PlaceFirst();
    }

    /// <summary>The face shown now (renders and tests).</summary>
    internal FrameworkElement? FaceForTest => _faceSlot.Child as FrameworkElement;

    [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);
    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }

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
        SetGrip(over, over && p.X < Left + (GripWidth + 6) * _scale);
    }

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

    /// <summary>Click-through as the settings say, now.</summary>
    public void SetClickThrough(bool on)
    {
        _gripLive = false;
        _grip.Visibility = Visibility.Collapsed;
        ApplyStyle();
        if (on && IsVisible) _hover.Start(); else _hover.Stop();
    }

    /// <summary>The first spot, with none kept: just under the Kinah overlay, left edges lined up; else the left of the main screen's middle.</summary>
    void PlaceFirst()
    {
        UpdateLayout();
        if (InvisibleForTest) { Left = -12000; Top = -12000; _placed = true; return; }
        if (KinahBox?.Invoke() is { } k) { Left = k.Left; Top = k.Bottom + 8; }
        else { Left = SystemParameters.WorkArea.Left + 24; Top = SystemParameters.WorkArea.Top + SystemParameters.WorkArea.Height * 0.35; }
        KeepOnScreen();
        _placed = true;
    }

    /// <summary>Reset position: the spot forgotten; it sits under the Kinah overlay again (now if shown, else the next time).</summary>
    public void ResetSpot()
    {
        _prefs.X = null; _prefs.Y = null; _prefs.Save();
        _placed = false;
        if (IsVisible) PlaceFirst();
    }

    /// <summary>The window wholly inside one screen's work area (the screen it overlaps most, else the nearest).</summary>
    public void KeepOnScreen()
    {
        if (InvisibleForTest) return;
        double scale = 1;
        try { scale = VisualTreeHelper.GetDpi(this).DpiScaleX; } catch (Exception) { }
        double w = ActualWidth > 0 ? ActualWidth : 300, h = ActualHeight > 0 ? ActualHeight : 80;
        var px = new System.Drawing.Rectangle((int)Math.Round(Left * scale), (int)Math.Round(Top * scale), (int)Math.Round(w * scale), (int)Math.Round(h * scale));
        var area = System.Windows.Forms.Screen.FromRectangle(px).WorkingArea;
        int x = Math.Clamp(px.X, area.Left, Math.Max(area.Left, area.Right - px.Width));
        int y = Math.Clamp(px.Y, area.Top, Math.Max(area.Top, area.Bottom - px.Height));
        Left = x / scale; Top = y / scale;
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int index);
    /// <summary>For the self-test: the window's extended style (never activates; click-through when set).</summary>
    internal long ExStyleForTest => _hwnd == IntPtr.Zero ? 0 : GetWindowLongPtr(_hwnd, -20).ToInt64();
    internal void GripForTest(bool shown, bool live) => SetGrip(shown, live);
    internal bool GripShownForTest => _grip.Visibility == Visibility.Visible;
    internal bool PlacedForTest => _placed;
}
