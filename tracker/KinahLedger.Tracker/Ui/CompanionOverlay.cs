using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace KinahLedger.Ui;

/// <summary>
/// The companion's own small window over the game (Ui/Companion.cs): the sprite and its bubble's room, in the overlay
/// theme in use and at the Kinah overlay's size and opacity. It moves as the Applicants window does: press on the companion
/// and drag; it remembers where the companion sits. The first time it sits just right of the Kinah overlay (or at the left
/// of the main screen). Click-through is a setting, off by default; with it on, the companion itself stays grabbable and
/// shows a grip while the pointer is over it. The bubble opens on the side with room: to the right, or to the left when the
/// companion sits in the right half of its screen. It never takes focus and stays out of Alt+Tab.
///
/// One reaction at a time: a moment that ranks higher than the one playing takes over at once, a lesser one waits its turn
/// (three at most wait; a moment waiting more than 8 s is dropped as stale).
/// </summary>
sealed class CompanionOverlay : Window
{
    readonly Grid _host = new();
    readonly Border _faceSlot = new();
    readonly FrameworkElement _grip;
    readonly CompanionPrefs _prefs;
    readonly DispatcherTimer _hover;
    readonly List<CompanionMoment> _queue = new();
    CompanionFace? _face;
    CompanionMoment? _now;
    IntPtr _hwnd;
    bool _gripLive, _placed, _wanted;
    string _sig = "";
    double _opacity = 0.94, _scale = 1;
    string _theme = HudThemes.Classic;
    CompanionSprite.Mood _mood = CompanionSprite.Mood.Idle;

    /// <summary>Where the Kinah overlay is now (WPF units), null when it isn't shown: the first spot is just right of it.</summary>
    public Func<Rect?>? KinahBox { get; set; }

    /// <summary>For the self-tests and renders: windows made while it is set are transparent and parked off every screen.</summary>
    internal static bool InvisibleForTest;

    public CompanionOverlay(CompanionPrefs prefs)
    {
        _prefs = prefs;
        if (InvisibleForTest) Opacity = 0;
        Title = InvisibleForTest ? "Kinah Ledger companion (parked test window)" : "Kinah Ledger companion";
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
        // the grip: two columns of three dots under the companion, shown while it can be grabbed (click-through on)
        var grip = new Canvas { Width = 7, Height = 11, Margin = new Thickness(0, 0, 0, 1), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, Visibility = Visibility.Collapsed, IsHitTestVisible = false };
        for (int c = 0; c < 2; c++) for (int r = 0; r < 3; r++)
        {
            var d = new Ellipse { Width = 2.4, Height = 2.4, Fill = HudKit.Muted };
            Canvas.SetLeft(d, c * 4.2); Canvas.SetTop(d, r * 4.2); grip.Children.Add(d);
        }
        _grip = grip;
        _host.Children.Add(_faceSlot);
        _host.Children.Add(_grip);
        Content = _host;
        SourceInitialized += (_, _) => { _hwnd = new WindowInteropHelper(this).Handle; ApplyStyle(); };
        MouseLeftButtonDown += (_, _) =>
        {
            try { DragMove(); } catch (InvalidOperationException) { }
            KeepOnScreen();
            Remember();
            _placed = true;
            Rebuild(force: false);   // dragged into the other half of the screen: the bubble opens on the other side
        };
        _hover = new DispatcherTimer(TimeSpan.FromMilliseconds(60), DispatcherPriority.Background, (_, _) => PollHover(), Dispatcher);
        _hover.Stop();
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible && _prefs.ClickThrough) _hover.Start(); else _hover.Stop();
            if (IsVisible) _face?.Sprite.Start(); else _face?.Sprite.Stop();
        };
        Closed += (_, _) => { _hover.Stop(); _face?.Sprite.Stop(); };
        new WindowInteropHelper(this).EnsureHandle();   // the never-focus style is set before it is ever shown
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = -32000; Top = -32000;
        if (_prefs.X is { } && _prefs.Y is { } && !InvisibleForTest) _placed = true;
    }

    /// <summary>The overlay theme and the Kinah overlay's opacity and size (Settings › Buddy): the face is drawn again in them.</summary>
    public void Match(string theme, double opacity, double scale)
    {
        _theme = theme;
        _opacity = Math.Clamp(opacity, 0.3, 1);
        _scale = Math.Clamp(scale, HudPrefs.MinScale, HudPrefs.MaxScale);
        _host.LayoutTransform = Math.Abs(_scale - 1) < 0.001 ? Transform.Identity : new ScaleTransform(_scale, _scale);
        Rebuild(force: false);
    }

    /// <summary>The bubble's side: on the left while the companion sits in the right half of its screen.</summary>
    bool BubbleLeftAt(double spriteX)
    {
        if (InvisibleForTest) return false;
        var area = WorkAreaAt(spriteX, Top);
        return spriteX + CompanionSprite.Side * _scale / 2 > (area.Left + area.Right) / 2;
    }

    /// <summary>The companion's spot on screen (WPF units): the window's corner, past the bubble's room when it opens to the left.</summary>
    double SpriteX => _face is { BubbleLeft: true, Lines: true } ? Left + CompanionFace.BubbleRoom * _scale : Left;

    void Remember() { _prefs.X = SpriteX; _prefs.Y = Top; _prefs.Save(); }

    /// <summary>The face made again when the theme, the opacity, the lines or the bubble's side changed (or <paramref name="force"/>), the companion kept where it is.</summary>
    void Rebuild(bool force) => Build(IsVisible && _face != null ? SpriteX : _prefs.X ?? 0, force);

    /// <summary>The face for the companion at <paramref name="spriteX"/>: made again if anything it is drawn from changed, the window put so the companion sits there.</summary>
    void Build(double spriteX, bool force)
    {
        bool left = _prefs.Lines && BubbleLeftAt(spriteX);
        var sig = _theme + "|" + _opacity + "|" + _prefs.Lines + "|" + left;
        if (force || sig != _sig || _face == null)
        {
            _sig = sig;
            _face?.Sprite.Stop();
            _face = new CompanionFace(_theme, _opacity, _prefs.Lines, left);
            _face.Sprite.Rested += Next;
            _face.Sprite.SetMood(_mood);
            _faceSlot.Child = _face;
            _grip.HorizontalAlignment = left ? HorizontalAlignment.Right : HorizontalAlignment.Left;
            _grip.Margin = new Thickness(left ? 0 : 6, 0, left ? 6 : 0, 2);
            _now = null; _queue.Clear();
            if (IsVisible) _face.Sprite.Start();
        }
        if (IsVisible && !InvisibleForTest) Left = spriteX - (left ? CompanionFace.BubbleRoom * _scale : 0);
    }

    /// <summary>Shown or not: while the game is open and the companion is on. Never activated.</summary>
    public void ShowWanted(bool on)
    {
        _wanted = on;
        if (on && !IsVisible) { Show(); if (!_placed) PlaceFirst(); else PlaceKept(); }
        else if (!on && IsVisible) Hide();
    }

    public bool Wanted => _wanted;

    /// <summary>The mood at rest (a fight on, a long quiet).</summary>
    public void SetMood(CompanionSprite.Mood m)
    {
        _mood = m;
        _face?.Sprite.SetMood(m);
    }

    /// <summary>A moment to react to: now if nothing plays or it ranks higher than what plays, else after it.</summary>
    public void Play(CompanionMoment m)
    {
        if (_face == null) Rebuild(force: true);
        if (_now != null && _face!.Sprite.Playing != null && m.Rank <= _now.Rank)
        {
            _queue.Add(m);
            if (_queue.Count > 3) _queue.RemoveAt(_queue.IndexOf(_queue.MinBy(q => q.Rank)!));
            return;
        }
        Start(m);
    }

    void Start(CompanionMoment m)
    {
        _now = m;
        _face!.Sprite.React(m.Kind, m.Tone);
        if (_prefs.Lines) _face.Say(m);
    }

    /// <summary>A reaction ended: the best of those waiting (none older than 8 s) plays next.</summary>
    void Next()
    {
        _now = null;
        var now = DateTime.UtcNow;
        _queue.RemoveAll(q => (now - q.At).TotalSeconds > 8);
        if (_queue.Count == 0) return;
        var next = _queue.MaxBy(q => q.Rank)!;
        _queue.Remove(next);
        Start(next);
    }

    /// <summary>The face shown now (renders and tests), the reaction playing and the moments waiting.</summary>
    internal CompanionFace? FaceForTest => _face;
    internal CompanionMoment? PlayingForTest => _face?.Sprite.Playing != null ? _now : null;
    internal int QueuedForTest => _queue.Count;
    /// <summary>For the tests: the reaction under way ended now.</summary>
    internal void EndReactionForTest() { _face?.Hush(); if (_face?.Sprite.Playing != null) _face.Sprite.EndNow(); else Next(); }

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
        double sx = SpriteX, side = CompanionSprite.Side * _scale;
        bool over = p.X >= sx && p.X < sx + side && p.Y >= Top && p.Y < Top + side;
        SetGrip(over, over);
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

    /// <summary>The lines switched: the bubble's room comes or goes, the companion kept where it is.</summary>
    public void LinesChanged() => Rebuild(force: true);

    void PlaceKept()
    {
        if (InvisibleForTest) { Left = -12000; Top = -12000; return; }
        Top = _prefs.Y ?? Top;
        Build(_prefs.X ?? 0, force: false);
        KeepOnScreen();
    }

    /// <summary>The first spot, with none kept: just right of the Kinah overlay, tops lined up; else the left of the main screen's middle.</summary>
    void PlaceFirst()
    {
        if (InvisibleForTest) { Left = -12000; Top = -12000; _placed = true; return; }
        double x, y;
        if (KinahBox?.Invoke() is { } k) { x = k.Right + 8; y = k.Top; }
        else { x = SystemParameters.WorkArea.Left + 24; y = SystemParameters.WorkArea.Top + SystemParameters.WorkArea.Height * 0.35; }
        Top = y;
        Build(x, force: false);
        _placed = true;
        KeepOnScreen();
    }

    /// <summary>Reset position: the spot forgotten; it sits beside the Kinah overlay again (now if shown, else the next time).</summary>
    public void ResetSpot()
    {
        _prefs.X = null; _prefs.Y = null; _prefs.Save();
        _placed = false;
        if (IsVisible) PlaceFirst();
    }

    static Rect WorkAreaAt(double x, double y)
    {
        try
        {
            var a = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point((int)Math.Round(x), (int)Math.Round(y))).WorkingArea;
            return new Rect(a.Left, a.Top, a.Width, a.Height);
        }
        catch (Exception) { return SystemParameters.WorkArea; }
    }

    /// <summary>The window wholly inside one screen's work area (the screen it overlaps most, else the nearest).</summary>
    public void KeepOnScreen()
    {
        if (InvisibleForTest) return;
        double scale = 1;
        try { scale = VisualTreeHelper.GetDpi(this).DpiScaleX; } catch (Exception) { }
        double w = ActualWidth > 0 ? ActualWidth : 340, h = ActualHeight > 0 ? ActualHeight : 96;
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
