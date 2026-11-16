using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using KinahLedger.Parser;

namespace KinahLedger.Ui;

/// <summary>
/// The boss fight over the game (Steven, 2026-11-13: "We should be able to toggle on and off and drag the overlay too"):
/// the boss, the clock, the group with Combat Power and item level, and DPS when the game gives damage. It shows only
/// during a boss fight (and for a few seconds after, with the result), only while the game or the tracker is in front,
/// never takes focus (a tool window that doesn't activate), stays where it was dragged, and lets clicks through when
/// asked. Drawn in the panel's own dark palette (HudKit), like the panel over the game, whatever the window's look.
/// </summary>
public sealed class ParserOverlay : Window
{
    /// <summary>The overlay on screen, if any: the readers step around it.</summary>
    public static ParserOverlay? Current { get; private set; }

    /// <summary>The player dropped it somewhere (pixels, the window's top-left).</summary>
    public event Action<int, int, int>? Dropped;   // left, top, and the meter's bottom edge (what it keeps)
    /// <summary>The overlay's own menu (a right-click): a mode picked, hide it, clicks through.</summary>
    public event Action<ParserMode>? ModePicked;
    public event Action? HideAsked;
    /// <summary>A click (no drag) on the overlay: the window switches the meter compact or expanded.</summary>
    public event Action? Clicked;
    public event Action? ClickThroughAsked;
    /// <summary>Each member's Combat Power and item level beside their DPS (Options › In game › Parser › Group details).</summary>
    public bool ShowDetails { get; set; }
    /// <summary>The mode the menu ticks (set by the window).</summary>
    public ParserMode Mode { get; set; }

    readonly bool _still;
    readonly ParserPanel _panel = new(glass: HudGlass.On);   // the separate overlay: the overlays' glass (Ui/HudGlass.cs)
    readonly TextBlock _hint;
    /// <summary>
    /// Clicks go through to the game (Click-through, off by default). The overlay can still be moved: with the pointer over
    /// it a grab handle shows on its top edge, and the handle alone takes the pointer, so a drag from it moves the overlay.
    /// </summary>
    bool _clickThrough;
    readonly Border _grab;
    readonly System.Windows.Threading.DispatcherTimer _grabWatch;
    bool _grabbing;
    public const double Width0 = ParserPrefs.DefaultWidth + 10;   // Buddy's width (HudCard, 360)

    /// <param name="still">A drawing (layout renders): not the overlay the readers step around.</param>
    public ParserOverlay(bool still = false)
    {
        _still = still;
        // its pictures loaded before the first draw: one first loaded while the window isn't shown yet draws blank
        foreach (var art in new[] { "boss", "dungeon", "medal", "rift" }) GameArt.Image(art);
        if (!still) { Current = this; Closed += (_, _) => { if (Current == this) Current = null; }; }
        ShowActivated = false;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Title = "Kinah Ledger fight overlay";
        FontFamily = HudKit.Text;
        FontWeight = FontWeights.Medium;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        Left = -10000; Top = -10000;   // placed once it has laid out (Place)
        // the hint under it, as the Kinah overlay's "drag me": a small dark pill, shown with the pointer over it
        _hint = new TextBlock { FontSize = 11.5, Foreground = HudKit.Muted, Padding = new Thickness(8, 2, 8, 3), Background = HudKit.B(HudKit.PanelC, 0.88), Margin = new Thickness(0, 3, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left, Visibility = Visibility.Collapsed };
        // the grab handle: a short tab hanging from the top edge, only while clicks go through and the pointer is over it
        _grab = new Border
        {
            Width = 46, Height = 13, CornerRadius = new CornerRadius(0, 0, 7, 7), Background = HudKit.B(HudKit.BadgeLineC), HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 0, 0), Visibility = Visibility.Collapsed, Cursor = System.Windows.Input.Cursors.SizeAll,
            Child = new TextBlock { Text = "• • •", FontSize = 8, Foreground = HudKit.Muted, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, -2, 0, 0) },
        };
        System.Windows.Automation.AutomationProperties.SetName(_grab, "Drag to move the overlay");
        _panelFace = HudGlass.On ? HudGlass.Ring(_panel) : _panel;   // the glass's dark outer hairline round it
        _face.Child = _panelFace;
        _faceGrid = new Grid { Children = { _face, _grab } };
        Content = new StackPanel { Children = { _faceGrid, _hint } };
        _grabWatch = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(80) };
        _grabWatch.Tick += (_, _) => WatchGrab();
        IsVisibleChanged += (_, _) => { if (!IsVisible) DropEdge(); if (IsVisible && _clickThrough && !_still) _grabWatch.Start(); else { _grabWatch.Stop(); HideGrab(); } };
        AddWidthDrag();
        MouseLeftButtonDown += (_, _) =>
        {
            if ((_clickThrough && !_grabbing) || _still) return;
            double l0 = Left, t0 = Top;
            try { DragMove(); } catch (InvalidOperationException) { return; }
            // a press that didn't move it is a click: it switches the meter compact or expanded (Parser v2)
            if (Math.Abs(Left - l0) < 0.5 && Math.Abs(Top - t0) < 0.5 && !_grabbing) Clicked?.Invoke();
            var h = new WindowInteropHelper(this).Handle;
            if (h != IntPtr.Zero && GetWindowRect(h, out var r)) { _anchorBottom = r.Top + FaceHeightPx(); Dropped?.Invoke(r.Left, r.Top, _anchorBottom.Value); }
            if (_clickThrough) WatchGrab();
        };
        MouseEnter += (_, _) => ShowHint(); MouseLeave += (_, _) => ShowHint();
        // its small menu: what the Parser keeps, hide, clicks through (never while clicks go through: the game has them)
        MouseRightButtonUp += (_, ev) => { if (_clickThrough || _still) return; ev.Handled = true; OpenMenu(); };
        SourceInitialized += (_, _) => Native.SetClickThrough(new WindowInteropHelper(this).Handle, _clickThrough);
        SizeChanged += (_, _) => { PlacePending(); KeepBottom(); };
    }

    // ---- the meter's bottom edge stays put (Steven, 2026-11-15: "make sure that the bottom of the DPS meter is always the
    // source of truth - when the window needs to get bigger, it grows UP not down") ----

    readonly Grid _faceGrid;
    /// <summary>The meter's bottom edge on screen, in pixels: where it was placed or dropped. The drag hint under it doesn't count.</summary>
    int? _anchorBottom;

    /// <summary>The meter's own height on screen, in pixels (the hint under it left out).</summary>
    int FaceHeightPx() => (int)Math.Round(_faceGrid.ActualHeight * _scale * VisualTreeHelper.GetDpi(this).DpiScaleY);

    /// <summary>
    /// The overlays' size (Options › In game › Size, the Kinah overlay's): the Parser's own window grows or shrinks with it,
    /// so at Buddy's width it is as wide as Buddy on screen at any size (Steven, 2026-11-15: "its obviously not the buddy width
    /// by default its shorter": the Kinah overlay was at 110%, the Parser at 100%).
    /// </summary>
    public void SetScale(double scale)
    {
        var s = Math.Clamp(double.IsFinite(scale) ? scale : 1, HudPrefs.MinScale, HudPrefs.MaxScale);
        if (Math.Abs(s - _scale) < 0.001) return;
        _scale = s;
        if (Math.Abs(s - 1) < 0.001) ((FrameworkElement)Content).LayoutTransform = Transform.Identity;
        else { var t = new ScaleTransform(s, s); t.Freeze(); ((FrameworkElement)Content).LayoutTransform = t; }
    }
    double _scale = 1;
    /// <summary>For the options test: the size the window draws at.</summary>
    internal double ScaleForTest => _scale;

    /// <summary>The meter grew or shrank: the window moves so its bottom edge stays where it was, and it grows up.</summary>
    void KeepBottom()
    {
        if (_placeAt != null || _placeIn != null || _anchorBottom is not int b) return;
        var h = new WindowInteropHelper(this).Handle;
        if (h == IntPtr.Zero || !IsVisible || !GetWindowRect(h, out var r)) return;
        int faceH = FaceHeightPx();
        if (faceH <= 0) return;
        int want = b - faceH;
        if (r.Top != want) Native.MoveTo(h, r.Left, want);
    }

    // ---- its width, dragged from its own edges (Steven, 2026-11-15: "let the user drag size/width from the overlay itself") ----

    /// <summary>The player let go of an edge: the meter's new width at 100% (MainWindow keeps it in parser.json).</summary>
    public event Action<double>? FightWidthSet;
    /// <summary>-1 while the left edge is held, 1 the right, 0 none.</summary>
    int _edge;
    double _edgeX0, _edgeW0, _edgeLeft0;
    /// <summary>While clicks go through: the pointer is over an edge, and the window has taken it (WatchGrab).</summary>
    bool _overEdge;
    const double EdgeGrip = 8;

    /// <summary>
    /// A press on the meter's left or right edge widens or narrows it (ahead of the window's own drag, which moves it), the
    /// same as the Kinah overlay's meter; dragging the left edge keeps the right edge where it was. Not while clicks go
    /// through, except over an edge: there the window takes the pointer (as over the grab handle), so its edges widen it even
    /// while clicks go through to the game everywhere else (Steven, 2026-11-15: "i cant drag its edges", click-through on).
    /// </summary>
    void AddWidthDrag()
    {
        PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (_still || (_clickThrough && !_overEdge)) return;
            int edge = EdgeAt(e.GetPosition(this));
            if (edge == 0) return;
            _edge = edge; _edgeX0 = ScreenX(e); _edgeW0 = HudStackLook.FightWidth; _edgeLeft0 = Left;
            CaptureMouse();
            e.Handled = true;
        };
        PreviewMouseMove += (_, e) =>
        {
            if (_still) return;
            if (_edge == 0)
            {
                if (!_clickThrough) Cursor = EdgeAt(e.GetPosition(this)) != 0 ? System.Windows.Input.Cursors.SizeWE : System.Windows.Input.Cursors.SizeAll;
                return;
            }
            e.Handled = true;
            double w = Math.Clamp(Math.Round(_edgeW0 + _edge * (ScreenX(e) - _edgeX0) / _scale), HudStackLook.FightWidthMin, HudStackLook.FightWidthMax);
            if (w == HudStackLook.FightWidth) return;
            HudStackLook.FightWidth = w;
            ApplyFightWidth();
            if (_edge < 0) Left = _edgeLeft0 - (w - _edgeW0) * _scale;   // the right edge stays put
        };
        PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (_edge == 0) return;
            e.Handled = true;
            EndEdge();
            ReleaseMouseCapture();
        };
        LostMouseCapture += (_, _) => EndEdge();
    }

    void EndEdge()
    {
        if (_edge == 0) return;
        bool moved = _edge < 0 && Math.Abs(Left - _edgeLeft0) > 0.5;
        _edge = 0;
        Cursor = _clickThrough ? null : System.Windows.Input.Cursors.SizeAll;
        if (HudStackLook.FightWidth != _edgeW0) FightWidthSet?.Invoke(HudStackLook.FightWidth);
        // the left edge moved the window: its new spot is kept as a drop's (the bottom edge where it was)
        var h = new WindowInteropHelper(this).Handle;
        if (moved && h != IntPtr.Zero && GetWindowRect(h, out var r)) { _anchorBottom = r.Top + FaceHeightPx(); Dropped?.Invoke(r.Left, r.Top, _anchorBottom.Value); }
    }

    double ScreenX(System.Windows.Input.MouseEventArgs e) => PointToScreen(e.GetPosition(this)).X / VisualTreeHelper.GetDpi(this).DpiScaleX;

    /// <summary>-1 over the meter's left edge, 1 over its right, 0 elsewhere (the hint under it left out).</summary>
    internal int EdgeAt(Point p)
    {
        if (_faceGrid.ActualWidth < 1) return 0;
        try
        {
            var t = _faceGrid.TransformToAncestor(this);
            var tl = t.Transform(new Point(0, 0));
            var br = t.Transform(new Point(_faceGrid.ActualWidth, _faceGrid.ActualHeight));
            if (p.Y < tl.Y || p.Y > br.Y) return 0;
            if (p.X >= tl.X && p.X <= tl.X + EdgeGrip) return -1;
            if (p.X <= br.X && p.X >= br.X - EdgeGrip) return 1;
        }
        catch (InvalidOperationException) { }
        return 0;
    }

    /// <summary>For the options test: the meter's frame width, in DIPs.</summary>
    internal double FaceWidthForTest => _faceGrid.ActualWidth;

    /// <summary>For the options test: the meter's bottom edge pinned where it is now; and that edge and the window's top, in pixels.</summary>
    internal void AnchorBottomForTest() { var h = new WindowInteropHelper(this).Handle; if (h != IntPtr.Zero && GetWindowRect(h, out var r)) _anchorBottom = r.Top + FaceHeightPx(); }
    internal (int Top, int FaceBottom) EdgesForTest { get { var h = new WindowInteropHelper(this).Handle; return h != IntPtr.Zero && GetWindowRect(h, out var r) ? (r.Top, r.Top + FaceHeightPx()) : (0, 0); } }

    /// <summary>The overlay's shortcut, said in the drag hint ("Ctrl+Shift+P").</summary>
    /// <summary>
    /// The shortcut as the hint and the menu say it: "" when there is none ("Off", the default since 0.10.87, or anything that
    /// isn't a usable shortcut), so they never say "Off hides it".
    /// </summary>
    public string HotkeyText { get => _hotkeyText; set => _hotkeyText = Platform.Hotkey.Parse(value)?.ToString() ?? ""; }
    string _hotkeyText = "";

    /// <summary>For the options test: the drag hint's words and the menu's hide line.</summary>
    internal (string Hint, string Hide) HintForTest { get { ShowHint(); return (_hint.Text, (string)((System.Windows.Controls.MenuItem)BuildMenu().Items[5]).Header); } }

    /// <summary>The overlay's menu, in its own dark look: the three modes (the one on ticked, what each counts on hover), then hide and clicks through.</summary>
    internal ContextMenu BuildMenu()
    {
        var menu = new ContextMenu { Background = HudKit.B(HudKit.Panel2C), BorderBrush = HudKit.FrameLine, Foreground = HudKit.Ink, FontFamily = HudKit.Text, FontSize = 12.5, Padding = new Thickness(2, 4, 2, 4) };
        menu.Items.Add(new MenuItem { Header = L.T("What the Parser keeps"), IsEnabled = false, Foreground = HudKit.Faint, FontSize = 11.5 });
        foreach (var md in new[] { ParserMode.Bosses, ParserMode.Run, ParserMode.Fights })
        {
            var item = new MenuItem { Header = L.T(BossParser.Name(md)), IsCheckable = true, IsChecked = md == Mode, ToolTip = L.T(BossParser.Counts(md)), Foreground = HudKit.Ink };
            var pick = md;
            item.Click += (_, _) => ModePicked?.Invoke(pick);
            menu.Items.Add(item);
        }
        menu.Items.Add(new Separator { Background = HudKit.FrameLine });
        var hide = new MenuItem { Header = HotkeyText.Length > 0 ? L.F("Hide the overlay ({0})", HotkeyText) : L.T("Hide the overlay"), Foreground = HudKit.Ink };
        hide.Click += (_, _) => HideAsked?.Invoke();
        menu.Items.Add(hide);
        var through = new MenuItem { Header = L.T("Let clicks through to the game"), Foreground = HudKit.Ink, ToolTip = L.T("Drag the handle on its top edge to move it then; turn it off again on the Parser tab") };
        through.Click += (_, _) => ClickThroughAsked?.Invoke();
        menu.Items.Add(through);
        return menu;
    }

    void OpenMenu()
    {
        var menu = BuildMenu();
        menu.PlacementTarget = this;
        menu.IsOpen = true;
    }

    void ShowHint()
    {
        // the edges widen it (either mode): said in the hint, the one place it can be found
        _hint.Text = _clickThrough ? L.T("drag the handle to move  ·  an edge to resize") : HotkeyText.Length > 0 ? L.F("drag to move  ·  an edge to resize  ·  right-click for the mode  ·  {0} hides it", HotkeyText) : L.T("drag to move  ·  an edge to resize  ·  right-click for the mode");
        _hint.Visibility = !_still && (_clickThrough ? _grab.Visibility == Visibility.Visible : IsMouseOver) ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Clicks go through to the game now: the rows' hover cards stay away then (only an edge or the handle takes the pointer).</summary>
    internal bool ClickThroughNow => _clickThrough;

    /// <summary>An edge drag or an edge under the pointer let go of (click-through switched, the handle hidden): nothing left holding the pointer.</summary>
    void DropEdge()
    {
        _overEdge = false;
        if (_edge != 0) { EndEdge(); if (IsMouseCaptured) ReleaseMouseCapture(); }
    }

    public void SetClickThrough(bool on)
    {
        DropEdge();
        _clickThrough = on;
        _grabbing = false;
        var h = new WindowInteropHelper(this).Handle;
        if (h != IntPtr.Zero) Native.SetClickThrough(h, on);
        Cursor = on ? null : System.Windows.Input.Cursors.SizeAll;
        if (on && IsVisible && !_still) _grabWatch.Start(); else { _grabWatch.Stop(); HideGrab(); }
        ShowHint();
    }

    [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);
    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }

    /// <summary>
    /// While clicks go through: the pointer over the overlay shows the grab handle, and over the handle the window takes the
    /// pointer (so a press there drags it); anywhere else the game has it. A window that lets clicks through hears no mouse,
    /// so the pointer is looked at a dozen times a second while the overlay shows.
    /// </summary>
    void WatchGrab()
    {
        if (!_clickThrough || !IsVisible || _still) { HideGrab(); return; }
        var h = new WindowInteropHelper(this).Handle;
        if (h == IntPtr.Zero || !GetCursorPos(out var c) || !GetWindowRect(h, out var r)) return;
        if (_edge != 0) return;   // an edge held: the window keeps the pointer until it is let go
        bool over = c.X >= r.Left && c.X < r.Right && c.Y >= r.Top && c.Y < r.Bottom;
        bool onGrab = false;
        _overEdge = false;
        if (over)
        {
            if (_grab.Visibility != Visibility.Visible) { _grab.Visibility = Visibility.Visible; UpdateLayout(); }
            var dpi = VisualTreeHelper.GetDpi(this);
            var tl = _grab.TranslatePoint(new Point(0, 0), this);
            int gx = r.Left + (int)Math.Round(tl.X * dpi.DpiScaleX), gy = r.Top + (int)Math.Round(tl.Y * dpi.DpiScaleY);
            int gw = (int)Math.Round(_grab.ActualWidth * _scale * dpi.DpiScaleX), gh = (int)Math.Round(_grab.ActualHeight * _scale * dpi.DpiScaleY);
            onGrab = c.X >= gx - 4 && c.X < gx + gw + 4 && c.Y >= gy - 2 && c.Y < gy + gh + 4;
            // an edge of the meter takes the pointer too (a drag there widens it)
            _overEdge = !onGrab && EdgeAt(new Point((c.X - r.Left) / dpi.DpiScaleX, (c.Y - r.Top) / dpi.DpiScaleY)) != 0;
            Cursor = _overEdge ? System.Windows.Input.Cursors.SizeWE : null;
            onGrab |= _overEdge;
        }
        else HideGrab();
        if (onGrab != _grabbing)
        {
            _grabbing = onGrab;
            Native.SetClickThrough(h, !onGrab);
        }
        ShowHint();
    }

    void HideGrab()
    {
        if (_edge == 0) _overEdge = false;
        _grab.Visibility = Visibility.Collapsed;
        if (_grabbing) { _grabbing = false; var h = new WindowInteropHelper(this).Handle; if (h != IntPtr.Zero) Native.SetClickThrough(h, _clickThrough); }
        ShowHint();
    }

    /// <summary>How solid the background is, as the Kinah overlay's (Options › In game): the same setting for both.</summary>
    public void SetOpacity(double opacity) { _opacity = opacity; _panel.SetOpacity(opacity); _lookFace?.SetOpacity(opacity); }
    double _opacity = -1;

    // ---- its look (Settings › In game › Parser › Overlay look) ----

    readonly Border _face = new();
    readonly FrameworkElement _panelFace;
    HudStackLook? _lookFace;
    string _lookKey = ParserPrefs.StandardLook;

    /// <summary>
    /// The window's look: "standard" (its own glass panel) or a look drawn for fights (Auto, Capsule, Meter, Float), drawn
    /// as it is in a dungeon, the fight alone (its Kinah figures away): the Kinah overlay keeps its own look.
    /// </summary>
    public string Look
    {
        get => _lookKey;
        set
        {
            var k = ParserPrefs.LookOf(value);
            if (k == _lookKey) return;
            _lookKey = k;
            _lookFace = MakeLook(k, _still);
            if (_lookFace != null && _opacity >= 0) _lookFace.SetOpacity(_opacity);
            _face.Child = _lookFace == null ? _panelFace : _lookFace is IHudFightAbove ? null : _lookFace.Root;
        }
    }

    /// <summary>A fight look for the Parser's own window, its Kinah part put away; null for the standard panel.</summary>
    static HudStackLook? MakeLook(string key, bool still)
    {
        HudStackLook? l = key switch
        {
            HudLooks.Auto => new HudAuto { Still = still }, HudLooks.Capsule => new HudCapsule { Still = still },
            HudLooks.Meter => new HudMeter { Still = still }, HudLooks.Float => new HudFloat { Still = still }, _ => null,
        };
        if (l == null) return null;
        l.Update(new Dictionary<string, string>());
        l.HideKinah(true);
        return l;
    }

    /// <summary>Settings › In game › Parser › Width moved: a fight look drawn again at the new width.</summary>
    public void ApplyFightWidth() { _lookFace?.ApplyWidth(); _panel.Width = HudStackLook.FightWidth + 10; }

    /// <summary>
    /// A look's face in a fight as the Parser's own window draws it (Settings' look row): the standard panel, or a fight
    /// look with its Kinah part away. Nothing animates; in the window's face and figures.
    /// </summary>
    public static FrameworkElement PreviewFace(string look, Encounter e, DateTime now, ParserMode mode, bool details)
    {
        FrameworkElement face;
        if (MakeLook(ParserPrefs.LookOf(look), still: true) is { } l)
        {
            if (l is IHudFightAbove fa) face = fa.FightAbove(e, now) ?? new Border();
            else { l.ShowFight(e, now); face = l.Root; }
        }
        else
        {
            var p = new ParserPanel(glass: HudGlass.On) { Mode = mode, ShowDetails = details };
            p.Draw(e, now);
            face = HudGlass.On ? HudGlass.Ring(p) : p;
        }
        var host = new Border { Child = face };
        TextElement.SetFontFamily(host, HudKit.Text);
        TextElement.SetFontWeight(host, FontWeights.Medium);
        TextOptions.SetTextFormattingMode(host, TextFormattingMode.Display);
        Typography.SetNumeralAlignment(host, FontNumeralAlignment.Tabular);
        return host;
    }
    /// <summary>The Kinah overlay's frame (its corner and padding for the figures picked), so the two look alike.</summary>
    public void SetFrame(double radius, Thickness padding) => _panel.SetFrame(radius, padding);

    /// <summary>For the options test: the grab handle shows (while clicks go through and the pointer is over the overlay).</summary>
    internal bool GrabShownForTest => _grab.Visibility == Visibility.Visible;
    internal void ShowGrabForTest() { _grab.Visibility = Visibility.Visible; ShowHint(); }

    // ---- where it goes ----

    (int X, int Y, int? Bottom)? _placeAt;
    System.Drawing.Rectangle? _placeIn;

    /// <summary>
    /// Puts it where it was dropped last (its bottom edge where the meter's was, when known: it grows up from there), or by
    /// default near the top left, right of the party list: clear of the party list, the minimap (top-right), the chat
    /// (bottom-left) and the skill bar (bottom).
    /// </summary>
    public void Place(int? left, int? top, int? bottom, System.Drawing.Rectangle? game)
    {
        // a place on a screen that is still there (a monitor unplugged since: the default place)
        _placeAt = left is int l && (bottom ?? top) is int y0 && System.Windows.Forms.Screen.AllScreens.Any(s => s.WorkingArea.Contains(l + 20, bottom != null ? y0 - 20 : y0 + 20)) ? (l, top ?? y0, bottom) : null;
        _placeIn = game;
        _anchorBottom = null;
        PlacePending();
    }

    void PlacePending()
    {
        var h = new WindowInteropHelper(this).Handle;
        if (h == IntPtr.Zero || ActualWidth < 1) return;
        if (_placeAt is { } p)
        {
            int y = p.Bottom is int b ? b - FaceHeightPx() : p.Y;
            Native.MoveTo(h, p.X, y);
            _anchorBottom = y + FaceHeightPx();
            _placeAt = null; _placeIn = null; return;
        }
        if (_placeIn is not { } a) return;
        _placeIn = null;
        // right of the party list and its effects (the left edge to about a third of the height across), under the currencies
        int top = a.Top + (int)Math.Round(a.Height * 0.16);
        Native.MoveTo(h, a.Left + (int)Math.Round(a.Height * 0.36), top);
        _anchorBottom = top + FaceHeightPx();
    }

    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }

    System.Drawing.Rectangle PixelBounds()
    {
        var h = new WindowInteropHelper(this).Handle;
        if (h == IntPtr.Zero || !IsVisible || !GetWindowRect(h, out var r)) return System.Drawing.Rectangle.Empty;
        return System.Drawing.Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
    }

    bool _readHidden;
    /// <summary>True when the overlay should be on screen (set by the window's tick); a read that hid it shows it again only then.</summary>
    public bool Wanted { get; set; }

    /// <summary>A screen read with the overlay out of the way when it sits over the area read (as the panel over the game does).</summary>
    public static async Task HiddenIfOver(System.Drawing.Rectangle area, Func<Task> read)
    {
        var o = Current;
        bool hide = false;
        if (o != null) { try { hide = await o.Dispatcher.InvokeAsync(() => o.PixelBounds().IntersectsWith(area)); } catch (Exception) { hide = false; } }
        if (hide) { try { await o!.Dispatcher.InvokeAsync(() => { o._readHidden = true; o.Hide(); }); await Task.Delay(120); } catch (Exception) { hide = false; } }
        try { await read(); }
        finally
        {
            if (hide) { try { await o!.Dispatcher.InvokeAsync(() => { o._readHidden = false; if (o.Wanted) o.Show(); }); } catch (Exception) { /* closing */ } }
        }
    }

    /// <summary>Shown or hidden as the window's tick says, never while a read has it out of the way.</summary>
    public void ShowWanted(bool on)
    {
        Wanted = on;
        if (_readHidden) return;
        if (on && !IsVisible) { Show(); KeepBottom(); }   // a meter that changed size while hidden still sits on its bottom edge
        else if (!on && IsVisible) Hide();
    }

    // ---- what it shows ----

    /// <summary>The record (the panel draws it: Ui/ParserPanel below, shared with the Kinah overlay's parser section).</summary>
    public void Draw(Encounter e, DateTime now)
    {
        if (_lookFace is IHudFightAbove fa)
        {
            var el = fa.FightAbove(e, now);
            if (!ReferenceEquals(_face.Child, el)) _face.Child = el;
            return;
        }
        if (_lookFace != null) { _lookFace.ShowFight(e, now); return; }
        _panel.Mode = Mode; _panel.ShowDetails = ShowDetails;
        _panel.Draw(e, now);
    }
}

/// <summary>
/// What the Parser shows over the game, drawn once for both places it can sit (Steven, 2026-11-14: "expand by default but
/// give option in settings to separate"): the Kinah overlay's parser section (Ui/HudWindow.Parser.cs) and the separate
/// overlay window (above). The record's head (its art, title, clock), what it is and where, the boss's HP or a run's pull
/// and splits, a fight's kills, the members who have dealt damage on meter bars in their class's colour, and its foot.
/// </summary>
public sealed class ParserPanel : Border
{
    readonly StackPanel _body = new();
    readonly ParserRows _prows = new();
    readonly string _hpPlace = Motion.HpPlace();
    string _sig = "";
    /// <summary>The mode the record is counted in, and whether each member's Combat Power and item level show.</summary>
    public ParserMode Mode { get; set; }
    public bool ShowDetails { get; set; }

    static readonly SolidColorBrush Coral = HudKit.B(HudKit.RecC), Text2 = HudKit.B(0xC9CDDA), Track = HudKit.B(0xFFFFFF, 0.08);

    /// <summary>The glass (Ui/HudGlass.cs): the separate Parser overlay's; inside Badge, Buddy or Pop the panel takes their frame.</summary>
    readonly bool _glass;

    public ParserPanel(bool glass = false)
    {
        _glass = glass;
        Width = HudStackLook.FightWidth + 10;   // the meter's width (Settings › In game › Parser › Width, or an edge dragged), 360 by default: Buddy's
        // the Kinah overlay's frame exactly (Ui/HudWindow.cs): its surface at the same opacity, its 1 px line, radius 12, its padding
        Background = new SolidColorBrush(Color.FromArgb(0xF0, 0x0B, 0x0C, 0x10)); BorderBrush = HudKit.FrameLine; BorderThickness = new Thickness(1);
        var (radius, pad) = HudWindow.FrameShape(HudFields.Parse(null));
        CornerRadius = new CornerRadius(radius); Padding = pad;
        if (_glass) { HudGlass.Apply(this, 0.94); Typography.SetNumeralAlignment(this, FontNumeralAlignment.Tabular); }
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        TextElement.SetFontFamily(this, HudKit.Text);
        TextElement.SetFontWeight(this, FontWeights.Medium);
        Child = _body;
        foreach (var art in new[] { "boss", "dungeon", "medal", "rift" }) GameArt.Image(art);
    }

    /// <summary>The Kinah overlay's corner and padding (HudWindow.FrameShape for its figures).</summary>
    public void SetFrame(double radius, Thickness padding) { CornerRadius = new CornerRadius(_glass ? HudGlass.Radius : radius); Padding = padding; }

    /// <summary>How solid the background is, 0.3 to 1: the Kinah overlay's own setting (Options › In game), so the two match.</summary>
    public void SetOpacity(double opacity)
    {
        var a = (byte)Math.Round(255 * Math.Clamp(opacity, 0.3, 1));
        if (_glass) { Background = HudGlass.Fill(opacity); return; }
        var b = new SolidColorBrush(Color.FromArgb(a, 0x0B, 0x0C, 0x10)); b.Freeze();
        Background = b;
    }

    // ---- what it shows ----

    /// <summary>The fight: under way (the clock running), or just ended (its result at the foot).</summary>
    public void Draw(Encounter e, DateTime now)
    {
        var sig = $"{e.Id}:{(int)(e.Duration(now).TotalSeconds * 10)}:{e.Outcome}:{e.Roster.Count}:{e.Roster.Sum(f => f.Damage)}:{e.Partial}:{e.BossHp}:{string.Join(",", e.Roster.Select(f => f.CombatPower))}:{e.Boss}:{e.Splits.Count}:{e.YourKills}:{e.YourDeaths}:{e.Roster.Sum(f => f.Deaths)}:{ParserRows.ExpandedPref}:{ParserRows.HealBarsPref}:{Mode}:{ShowDetails}:{Encounter.ActiveBasis}:{Encounter.BossOnly}:{e.BigHits?.LastOrDefault()?.Seq}:{e.GroupHealing}:{e.Roster.Sum(f => f.HealingEffective)}:{e.Roster.Sum(f => f.HealingEstimated)}";
        Width = HudStackLook.FightWidth + 10;
        sig += $":{Width}";
        if (sig == _sig) return;
        _sig = sig;
        _body.Children.Clear();
        // the head: the boss's art, its name, the clock
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var art = GameArt.Element(e.ModeKind == ParserMode.Run ? "dungeon" : e.ModeKind == ParserMode.Fights && e.Boss.Length == 0 ? (e.Kind == "Abyss" ? "rift" : "medal") : "boss", 22); art.Margin = new Thickness(0, 0, 8, 0);
        head.Children.Add(art);
        var name = T(e.Title, _glass ? 14 : 13.5, FontWeights.Bold, HudKit.Ink); name.TextTrimming = TextTrimming.CharacterEllipsis; name.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(name, 1); head.Children.Add(name);
        var clock = e.Waiting ? T(L.T("Ready"), 13, FontWeights.SemiBold, HudKit.Muted) : Num(BossParser.Clock(e.Duration(now)), _glass ? 18 : 16, FontWeights.Bold, e.Live ? HudKit.Gold : HudKit.Ink); clock.VerticalAlignment = VerticalAlignment.Center;
        clock.Margin = new Thickness(10, 0, 0, 0);
        Grid.SetColumn(clock, 2); head.Children.Add(clock);
        _body.Children.Add(head);
        var where = e.Where;
        // the mode, said once at the head of the sub line, so a glance tells a run from a pull
        var mode = e.Always ? L.T("Always on") : L.T(BossParser.Name(e.ModeKind));
        var sub = T(where.Length > 0 ? $"{mode} · {where}" : mode, 11, FontWeights.Medium, HudKit.Faint);
        sub.TextTrimming = TextTrimming.CharacterEllipsis; sub.Margin = new Thickness(30, 1, 0, 0);
        if (e.Partial)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = sub.Margin };
            sub.Margin = new Thickness(0);
            row.Children.Add(sub);
            row.Children.Add(T(L.T("  ·  partial"), 11, FontWeights.SemiBold, HudKit.Warn));
            row.ToolTip = string.Join("; ", e.PartialWhy);
            _body.Children.Add(row);
        }
        else _body.Children.Add(sub);
        // a dungeon before its first pull (Ui/MainWindow.Parser.cs DungeonMeter): the meter waits here, no rows, no foot
        if (e.Waiting)
        {
            var w = T(e.ReadyText, 12.5, FontWeights.SemiBold, HudKit.Muted); w.Margin = new Thickness(30, 8, 0, 2);
            _body.Children.Add(w);
            BorderBrush = _glass ? HudGlass.Edge : HudKit.FrameLine;
            return;
        }

        // a run: the pull under way ("Now: the boss" and its clock) and the bosses done so far
        if (e.ModeKind == ParserMode.Run && e.Live)
        {
            var pull = e.Splits.LastOrDefault(x => x.Outcome == "");
            if (pull != null)
            {
                var now1 = new Grid { Margin = new Thickness(0, 7, 0, 0) };
                now1.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                now1.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var nl = new StackPanel { Orientation = Orientation.Horizontal };
                nl.Children.Add(T(L.T("Now"), 11, FontWeights.SemiBold, HudKit.Gold));
                var nb = T(pull.Boss, 12, FontWeights.Bold, HudKit.Ink); nb.Margin = new Thickness(6, 0, 0, 0); nb.TextTrimming = TextTrimming.CharacterEllipsis; nb.MaxWidth = 220;
                nl.Children.Add(nb);
                now1.Children.Add(nl);
                var pc = T(BossParser.Clock(TimeSpan.FromSeconds(pull.Seconds)), 12, FontWeights.Bold, HudKit.Gold);
                Grid.SetColumn(pc, 1); now1.Children.Add(pc);
                _body.Children.Add(now1);
            }
            var done = e.Splits.Where(x => x.Outcome != "").ToList();
            if (done.Count > 0)
            {
                var wp = new WrapPanel { Margin = new Thickness(0, 5, 0, 0) };
                foreach (var x in done.TakeLast(4))
                {
                    var c = x.Outcome == "kill" ? HudKit.Mint : x.Outcome == "wipe" ? Coral : HudKit.Muted;
                    var chip = new Border { CornerRadius = new CornerRadius(6), Padding = new Thickness(6, 1, 6, 2), Margin = new Thickness(0, 0, 4, 3), Background = HudKit.B(0xFFFFFF, 0.06),
                        Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { T(x.Outcome == "kill" ? "✓" : x.Outcome == "wipe" ? "✕" : "·", 10.5, FontWeights.Bold, c), Pad(T($" {Short(x.Boss)} {BossParser.Clock(TimeSpan.FromSeconds(x.Seconds))}", 10.5, FontWeights.Medium, Text2)) } } };
                    wp.Children.Add(chip);
                }
                if (done.Count > 4) wp.Children.Add(T($"+{done.Count - 4}", 10.5, FontWeights.Medium, HudKit.Faint));
                _body.Children.Add(wp);
            }
        }
        // a fight: your kills (players apart) and deaths
        if (e.ModeKind == ParserMode.Fights && (e.YourKills > 0 || e.YourDeaths > 0))
        {
            var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            if (e.YourKills > 0) line.Children.Add(T((e.YourKills == 1 ? L.F("{0} kill", e.YourKills) : L.F("{0} kills", e.YourKills)) + (e.PlayerKills > 0 ? " · " + (e.PlayerKills == 1 ? L.F("{0} player", e.PlayerKills) : L.F("{0} players", e.PlayerKills)) : ""), 12, FontWeights.Bold, HudKit.Mint));
            if (e.YourDeaths > 0) { var d = T((e.YourKills > 0 ? "   " : "") + (e.YourDeaths == 1 ? L.F("{0} death", e.YourDeaths) : L.F("{0} deaths", e.YourDeaths)), 12, FontWeights.Bold, Coral); line.Children.Add(d); }
            _body.Children.Add(line);
        }

        // the boss's HP, when the game gives it
        if (e.BossHpLeft is { } left && e.Live)
        {
            var hp = new Grid { Margin = new Thickness(0, 7, 0, 0) };
            hp.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            hp.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var track = new Grid { Height = _glass ? 4 : 6, VerticalAlignment = VerticalAlignment.Center };
            track.Children.Add(new Border { Background = _glass ? HudGlass.Track : Track, CornerRadius = new CornerRadius(3) });
            var fill = new Border { Background = left < 0.34 ? Coral : HudKit.Gold, CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left };
            track.SizeChanged += (_, _) => fill.Width = Math.Max(left > 0 ? 3 : 0, track.ActualWidth * left);
            track.Children.Add(fill);
            // a big drop leaves a red chunk that shrinks onto the fill
            Motion.HpGhost(track, _hpPlace, e.Id, left, now, 3);
            hp.Children.Add(track);
            // "47% · 1.82M left" (Steven, 2026-11-14: the HP left beside the boss's %)
            var pct = HpText(e, 11.5, left < 0.34 ? Coral : HudKit.Ink, HudKit.Muted); pct.Margin = new Thickness(8, 0, 0, 0);
            Grid.SetColumn(pct, 1); hp.Children.Add(pct);
            _body.Children.Add(hp);
        }

        // the group (Parser v2, Ui/ParserRows.cs): the rows are kept between draws, so a change moves
        if (e.Roster.Count > 0)
        {
            _prows.Margin = new Thickness(-6, 8, -6, 0);
            ParserRows.Detach(_prows); _body.Children.Add(_prows);
            _prows.Show(e, now);
        }
        // the foot: the fight's total and the group's DPS; the result before them once it's over
        var foot = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        foot.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        foot.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var sep = new Border { Height = 1, Background = _glass ? HudGlass.Rule : HudKit.Sep, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, -4, 0, 0) };
        Grid.SetColumnSpan(sep, 2); foot.Children.Add(sep);
        // the outcome once it's over ("Killed in 2:31"), said before the figures
        TextBlock? outcome = null;
        if (!e.Live)
        {
            var took = BossParser.Clock(e.Duration(now));
            var (text, brush) = e.Outcome switch
            {
                "kill" when e.ModeKind == ParserMode.Fights => (L.F("{0} in {1}", e.OutcomeLabel, took), HudKit.Mint),
                "kill" => (L.F("Killed in {0}", took), HudKit.Mint),
                "cleared" => (L.F("Cleared in {0}", took), HudKit.Mint),
                "wipe" => (L.F("Wiped after {0}", took), Coral),
                "died" => (L.F("Died after {0}", took), Coral),
                "ended" => (L.F("Ended after {0}", took), HudKit.Muted),
                _ => (L.F("Left after {0}", took), HudKit.Muted),
            };
            outcome = T(text, 12, FontWeights.Bold, brush);
        }
        if (e.HasDamage)
        {
            // the fight's total and the group's DPS (Steven, 2026-11-15); held, the group figure is the fight's average, badged
            var line = ParserFoot.Line(e, now, 12, outcome);
            Grid.SetColumnSpan(line, 2); foot.Children.Add(line);
        }
        else if (outcome != null) foot.Children.Add(outcome);
        else
        {
            int near = e.Kills.Count(k => !k.Boss && !k.Yours);
            foot.Children.Add(T(L.T(e.YourKills > 0 ? "Your kills" : near > 0 ? "Kills near you" : "Fighting"), 12, FontWeights.SemiBold, HudKit.Muted));
            var v = T(e.YourKills > 0 ? e.YourKills.ToString(CultureInfo.InvariantCulture) : near > 0 ? near.ToString(CultureInfo.InvariantCulture) : "", 14, FontWeights.Bold, HudKit.Ink);
            Typography.SetNumeralAlignment(v, FontNumeralAlignment.Tabular);
            Grid.SetColumn(v, 1); foot.Children.Add(v);
        }
        _body.Children.Add(foot);
        BorderBrush = e.Live ? (_glass ? HudGlass.Edge : HudKit.FrameLine) : e.Tone == "good" ? HudKit.B(HudKit.MintC, 0.55) : e.Tone == "bad" ? HudKit.B(HudKit.RecC, 0.55) : _glass ? HudGlass.Edge : HudKit.FrameLine;
    }

    /// <summary>"1.82M left": the boss's HP left, or "" when the game hasn't given its HP.</summary>
    public static string HpLeft(Encounter e) => e.BossMaxHp > 0 ? L.F("{0} left", Amounts.Short(Math.Max(0, e.BossHp))) : "";

    /// <summary>"47% · 1.82M left": the share in <paramref name="pctInk"/>, bold, and the HP left after it in <paramref name="restInk"/>; tabular digits.</summary>
    public static TextBlock HpText(Encounter e, double size, Brush pctInk, Brush restInk)
    {
        var t = new TextBlock { FontSize = size, VerticalAlignment = VerticalAlignment.Center };
        Typography.SetNumeralAlignment(t, FontNumeralAlignment.Tabular);
        if (e.BossHpLeft is not { } left) return t;
        t.Inlines.Add(new Run($"{Math.Round(left * 100):0}%") { FontWeight = FontWeights.Bold, Foreground = pctInk });
        t.Inlines.Add(new Run($" · {HpLeft(e)}") { FontWeight = FontWeights.SemiBold, Foreground = restInk });
        System.Windows.Automation.AutomationProperties.SetName(t, $"Boss HP {Math.Round(left * 100):0}%, {HpLeft(e)}");
        return t;
    }

    static TextBlock Pad(TextBlock t) { t.Margin = new Thickness(0); return t; }
    /// <summary>A boss's name cut to its last word or two for a split chip ("Kromede's Desire", "Rotan").</summary>
    static string Short(string boss)
    {
        // "Talisra of the Void" is Talisra, "Silver Blade Rotan" is Rotan, "Kromede's Desire" stays whole
        int of = boss.IndexOf(" of ", StringComparison.OrdinalIgnoreCase);
        if (of > 0) return boss[..of];
        var w = boss.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return w.Length <= 2 ? boss : w[^1];
    }
    static TextBlock T(string text, double size, FontWeight w, Brush b) => new() { Text = text, FontSize = size, FontWeight = w, Foreground = b, VerticalAlignment = VerticalAlignment.Center };
    /// <summary>A figure that changes as hits land: tabular digits, so its column never twitches.</summary>
    static TextBlock Num(string text, double size, FontWeight w, Brush b) { var t = T(text, size, w, b); Typography.SetNumeralAlignment(t, FontNumeralAlignment.Tabular); return t; }
}
