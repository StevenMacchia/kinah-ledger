using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using KinahLedger.Ocr;
using static KinahLedger.Ui.HudKit;
using DPoint = System.Drawing.Point;
using DRect = System.Drawing.Rectangle;
using DSize = System.Drawing.Size;

namespace KinahLedger.Ui;

/// <summary>
/// Place your panel (the approved mock, kinah-bench/mock-hud-next/f4-placement.png): a guide over the game window that
/// dims it by half and draws the panel in its current look inside a dashed gold outline, to drag anywhere or snap to one
/// of three spots (HudSpots: top centre, under the minimap, left of the target bar). Faint outlines show where the game's
/// own target bar, minimap, loot list and chat box sit, from the readers' regions; one that is off this screen gets a
/// marker on the edge with how far it is. Enter saves the spot for the look (the same per-look spot a drag saves), Esc
/// puts it back. Opens once on first run after the setup check, and from Options › In game and the tray.
/// <para>Two windows over the game's part of its monitor: the veil and the outlines in one that clicks go through, and the
/// panel, its tag and the chooser in a transparent one above it, so only those take the mouse.</para>
/// </summary>
public sealed class PlacementGuide : Window
{
    readonly string _look;
    readonly DRect _game, _view;
    readonly DRect? _chat;
    readonly double _scale;
    readonly Action<DPoint> _save;
    /// <summary>The veil, the game's parts, the ghosts of the other spots, the edge markers and the help bar: clicks go through.</summary>
    readonly Canvas _veil = new() { ClipToBounds = true, IsHitTestVisible = false };
    /// <summary>The panel, its tag and the chooser.</summary>
    readonly Canvas _top = new() { ClipToBounds = true };
    readonly Grid _panel;
    readonly FrameworkElement _preview;
    readonly Border _tag, _chooser;
    readonly Dictionary<string, (Border Seg, TextBlock Text)> _segs = new();
    Window? _veilWindow;
    /// <summary>Screen pixels to a DIP on the guide's monitor (1 in a layout render).</summary>
    double _dpi = 1;
    /// <summary>The panel's window, in screen pixels: where it goes and how big it is at its size (HudPrefs.Scale).</summary>
    DPoint _pos;
    DSize _size;
    /// <summary>The snap spot the panel sits at, or null after a drag left it elsewhere.</summary>
    string? _chosen;
    bool _done;
    Point _dragFrom;
    DPoint _dragStart;

    /// <summary>The room the Card and the Ticker keep around themselves for a rare drop's glow (HudCard/HudTicker.Inset); the Strip has none.</summary>
    const double LookInset = 10;
    /// <summary>The dashed outline sits this far outside the panel's edge (the mock's 7 px).</summary>
    const double OutlineGap = 7;
    /// <summary>A drop this close to a snap spot (DIPs) lands on it.</summary>
    const double Magnet = 18;

    static readonly SolidColorBrush VeilBrush = B(0x070914, .50), PartLine = B(CommonC, .55), PartFill = B(CommonC, .04), LabelInk = B(0xDCE0F0),
        LabelBg = B(0x090B15, .78), GhostLine = B(GoldC, .55), GhostFill = B(GoldC, .05), GhostLabelBg = B(0x090B15, .65), EdgeBg = B(0x090B15, .82),
        EdgeLine = B(CommonC, .5), PanelBg = B(PanelC, .94), SegOn = B(0xEBAE4C, .14), SegOnLine = B(GoldC, .4), TagInk = B(0x261702),
        KbdOnTag = B(0x261702, .14), KbdOnTagLine = B(0x261702, .35), Grab = Frozen(Color.FromArgb(1, 0, 0, 0));
    static readonly LinearGradientBrush TagFill = MakeTagFill();
    static LinearGradientBrush MakeTagFill() { var b = new LinearGradientBrush(Rgb(GoldC), Rgb(SecGoldC), 90); b.Freeze(); return b; }
    static readonly DoubleCollection Dashes = Freeze(new DoubleCollection { 4, 3 }), Dots = Freeze(new DoubleCollection { 1, 2 }), GoldDashes = Freeze(new DoubleCollection { 3, 2 });
    static DoubleCollection Freeze(DoubleCollection d) { d.Freeze(); return d; }
    static readonly Geometry DragIcon = MakeDragIcon();
    static Geometry MakeDragIcon() { var g = Geometry.Parse("M8,1.5 V14.5 M1.5,8 H14.5 M6,3.6 L8,1.5 L10,3.6 M6,12.4 L8,14.5 L10,12.4 M3.6,6 L1.5,8 L3.6,10 M12.4,6 L14.5,8 L12.4,10"); g.Freeze(); return g; }

    /// <param name="look">The look the panel is drawn and saved in (HudLooks).</param>
    /// <param name="fields">The Strip's figures.</param>
    /// <param name="values">What the panel shows (the window's sample figures, so it is drawn at its full size).</param>
    /// <param name="scale">The panel's size (HudPrefs.Scale).</param>
    /// <param name="game">The game window, in screen pixels.</param>
    /// <param name="view">The part of the screen the guide covers: the game on its monitor (a smaller part of it in a layout render).</param>
    /// <param name="chat">The chat reader's own area, once it has found the chat; else where the game puts it by default.</param>
    /// <param name="start">Where the panel's window is now (pixels); null starts it at the look's first spot.</param>
    /// <param name="save">Enter: the spot to keep for the look, in pixels.</param>
    public PlacementGuide(string look, IEnumerable<string> fields, IReadOnlyDictionary<string, string> values, double scale, DRect game, DRect view, DRect? chat, DPoint? start, Action<DPoint> save)
    {
        _look = HudLooks.Parse(look);
        _game = game; _view = view; _chat = chat; _save = save;
        _scale = Math.Clamp(scale, HudPrefs.MinScale, HudPrefs.MaxScale);

        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Title = "Place your panel";
        FontFamily = HudKit.Text;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        TextOptions.SetTextFormattingMode(_veil, TextFormattingMode.Display);

        // the panel as it looks on the game, at its size, never live: the look's own preview (HudWindow.Preview)
        _preview = HudWindow.Preview(_look, fields, values);
        _preview.IsHitTestVisible = false;
        if (Math.Abs(_scale - 1) > 0.001) { var t = new ScaleTransform(_scale, _scale); t.Freeze(); _preview.LayoutTransform = t; }
        _panel = new Grid { Background = Grab, Cursor = Cursors.SizeAll, Children = { _preview } };
        AutomationPropertiesName(_panel, "Your panel: drag it, then press Enter");
        _panel.MouseLeftButtonDown += (_, e) => { _dragFrom = e.GetPosition(_top); _dragStart = _pos; _panel.CaptureMouse(); _chosen = null; Layout(); e.Handled = true; };
        _panel.MouseMove += (_, e) =>
        {
            if (!_panel.IsMouseCaptured) return;
            var p = e.GetPosition(_top);
            MoveTo(new DPoint(_dragStart.X + (int)Math.Round((p.X - _dragFrom.X) * _dpi), _dragStart.Y + (int)Math.Round((p.Y - _dragFrom.Y) * _dpi)));
            PlaceTop();
        };
        _panel.MouseLeftButtonUp += (_, _) => { if (!_panel.IsMouseCaptured) return; _panel.ReleaseMouseCapture(); Dropped(); };
        _panel.LostMouseCapture += (_, _) => Layout();

        _tag = MakeTag();
        _chooser = MakeChooser();
        _top.Children.Add(_panel); _top.Children.Add(_tag); _top.Children.Add(_chooser);
        _top.Background = Brushes.Transparent;

        MeasurePanel();
        // where it is now, kept on this screen; else the look's first spot (the Ticker's is left of the target bar)
        if (start is { } s) { _pos = s; MoveTo(s); _chosen = HudSpots.All.FirstOrDefault(x => Near(Spot(x.Key).Location, _pos, 2))?.Key; }
        else Snap(_look == HudLooks.Ticker ? HudSpots.Target : HudSpots.Top);

        PreviewKeyDown += (_, e) => { if (Press(e.Key, Keyboard.Modifiers)) e.Handled = true; };
        SourceInitialized += (_, _) => FitToView(this);
        Loaded += (_, _) => { FitToView(this); _dpi = VisualTreeHelper.GetDpi(this).DpiScaleX; Refit(); };
        DpiChanged += (_, e) => { _dpi = e.NewDpi.DpiScaleX; Dispatcher.InvokeAsync(() => { FitToView(this); if (_veilWindow != null) FitToView(_veilWindow); Refit(); }); };
        Deactivated += (_, _) => { if (_panel.IsMouseCaptured) _panel.ReleaseMouseCapture(); };
        Closed += (_, _) => { _done = true; _veilWindow?.Close(); _veilWindow = null; };
    }

    static void AutomationPropertiesName(DependencyObject d, string name) => System.Windows.Automation.AutomationProperties.SetName(d, name);

    /// <summary>Shows the guide over the game: the veil window (clicks go through it) and this one above it, focused for Enter and Esc.</summary>
    public void Open(Window owner)
    {
        _veilWindow = new Window
        {
            WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent, Topmost = true, ShowInTaskbar = false,
            ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.Manual, ShowActivated = false, Owner = owner,
            Title = "Place your panel", Content = _veil, FontFamily = HudKit.Text,
        };
        _veilWindow.SourceInitialized += (_, _) => { var h = new WindowInteropHelper(_veilWindow).Handle; Native.SetClickThrough(h, true); FitToView(_veilWindow); };
        _veilWindow.Loaded += (_, _) => { if (_veilWindow != null) FitToView(_veilWindow); };
        _veilWindow.Show();
        Owner = _veilWindow;   // always above the veil
        Content = _top;
        Show();
        Activate();
    }

    /// <summary>The whole guide as one drawing, for the layout renders: the veil under the panel and its chooser, at 1 px to a DIP.</summary>
    internal FrameworkElement ShotFace()
    {
        _dpi = 1;
        Refit();
        return new Grid { Width = _view.Width, Height = _view.Height, ClipToBounds = true, Children = { _veil, _top } };
    }

    /// <summary>The panel's window now, in screen pixels (for the self-test).</summary>
    internal DRect PanelPx => new(_pos, _size);
    /// <summary>The snap spot the panel sits at, or null (for the self-test).</summary>
    internal string? Chosen => _chosen;

    /// <summary>Where the panel's window goes for a snap spot, in pixels: inside the game, 12 DIPs from its edges.</summary>
    internal DRect Spot(string key) => HudSpots.At(key, _game, _size, _dpi);

    /// <summary>A snap spot from the chooser (or 1, 2, 3): the panel moves there and the spot lights up.</summary>
    internal void Snap(string key)
    {
        _pos = Spot(key).Location;
        _chosen = key;
        Layout();
    }

    /// <summary>
    /// A key in the guide: Enter keeps the spot, Esc puts the panel back as it was, 1 to 3 snap, the arrows nudge a pixel
    /// (ten with Shift). True when the key was the guide's.
    /// </summary>
    internal bool Press(Key key, ModifierKeys mods = ModifierKeys.None)
    {
        int step = (mods & ModifierKeys.Shift) != 0 ? 10 : 1;
        switch (key)
        {
            case Key.Enter: Finish(true); return true;
            case Key.Escape: Finish(false); return true;
            case Key.D1: case Key.NumPad1: Snap(HudSpots.All[0].Key); return true;
            case Key.D2: case Key.NumPad2: Snap(HudSpots.All[1].Key); return true;
            case Key.D3: case Key.NumPad3: Snap(HudSpots.All[2].Key); return true;
            case Key.Left: Nudge(-step, 0); return true;
            case Key.Right: Nudge(step, 0); return true;
            case Key.Up: Nudge(0, -step); return true;
            case Key.Down: Nudge(0, step); return true;
        }
        return false;
    }

    void Nudge(int dx, int dy) { MoveTo(new DPoint(_pos.X + (int)Math.Round(dx * _dpi), _pos.Y + (int)Math.Round(dy * _dpi))); _chosen = null; Layout(); }

    void Finish(bool save)
    {
        if (_done) return;
        _done = true;
        if (save) _save(_pos);
        Close();
    }

    /// <summary>A drop: onto a snap spot when it lands close to one, so the chooser says where it is.</summary>
    void Dropped()
    {
        foreach (var s in HudSpots.All)
            if (Near(Spot(s.Key).Location, _pos, (int)Math.Round(Magnet * _dpi))) { Snap(s.Key); return; }
        _chosen = null;
        Layout();
    }

    static bool Near(DPoint a, DPoint b, int within) => Math.Abs(a.X - b.X) <= within && Math.Abs(a.Y - b.Y) <= within;

    /// <summary>At a new scaling: the panel measured again, and kept at its snap spot (or where it was dropped).</summary>
    void Refit()
    {
        MeasurePanel();
        _chosen ??= HudSpots.All.FirstOrDefault(x => Near(Spot(x.Key).Location, _pos, (int)Math.Ceiling(2 * _dpi)))?.Key;
        if (_chosen is { } spot) Snap(spot);
        else { MoveTo(_pos); Layout(); }
    }

    /// <summary>The panel's window size in pixels, from its preview at its size.</summary>
    void MeasurePanel()
    {
        _preview.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var d = _preview.DesiredSize;
        _size = new DSize(Math.Max(1, (int)Math.Ceiling(d.Width * _dpi)), Math.Max(1, (int)Math.Ceiling(d.Height * _dpi)));
    }

    /// <summary>Moves the panel, kept inside the game on this screen.</summary>
    void MoveTo(DPoint p)
    {
        var area = DRect.Intersect(_game, _view);
        if (area.IsEmpty) area = _view;
        _pos = new DPoint(Math.Clamp(p.X, area.Left, Math.Max(area.Left, area.Right - _size.Width)), Math.Clamp(p.Y, area.Top, Math.Max(area.Top, area.Bottom - _size.Height)));
    }

    /// <summary>The look's own edge inside its window (the window keeps room for the glow), in DIPs.</summary>
    double Inset => _look == HudLooks.Strip ? 0 : LookInset * _scale;
    double X(int px) => (px - _view.Left) / _dpi;
    double Y(int px) => (px - _view.Top) / _dpi;
    Rect Dip(DRect r) => new(X(r.Left), Y(r.Top), r.Width / _dpi, r.Height / _dpi);
    /// <summary>The look itself, without the room around it, in DIPs on the guide.</summary>
    Rect Visible(DRect window) { var r = Dip(window); var i = Inset; return new Rect(r.X + i, r.Y + i, Math.Max(1, r.Width - 2 * i), Math.Max(1, r.Height - 2 * i)); }

    static void FitToView(Window w, DRect view)
    {
        var h = new WindowInteropHelper(w).Handle;
        if (h != IntPtr.Zero) SetWindowPos(h, IntPtr.Zero, view.X, view.Y, view.Width, view.Height, SWP_NOZORDER | SWP_NOACTIVATE);
    }
    void FitToView(Window w) => FitToView(w, _view);

    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    const uint SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10;

    /* ---------------- drawing ---------------- */

    /// <summary>Draws the veil layer again and puts the panel, its tag and the chooser where the panel is.</summary>
    void Layout()
    {
        double w = _view.Width / _dpi, h = _view.Height / _dpi;
        _veil.Width = _top.Width = w; _veil.Height = _top.Height = h;
        _veil.Children.Clear();
        _veil.Children.Add(new Rectangle { Width = w, Height = h, Fill = VeilBrush });
        var taken = new List<Rect>();
        var here = Visible(new DRect(_pos, _size));

        // the bar along the bottom: what to do, and the two keys
        var help = HelpBar();
        Put(_veil, help, (w - help.DesiredSize.Width) / 2, h - 12 - help.DesiredSize.Height);
        taken.Add(new Rect(Canvas.GetLeft(help), Canvas.GetTop(help), help.DesiredSize.Width, help.DesiredSize.Height));

        // the game's own parts, where the readers look for them; off this screen, a marker on the edge says where
        var parts = new (string Name, DRect Box)[]
        {
            ("target bar", HudSpots.TargetBar(_game)), ("minimap", HudSpots.MinimapBox(_game)),
            ("loot list", LootPopup.Region(_game)), ("chat box", _chat is { } c && c.IntersectsWith(_game) ? c : HudSpots.ChatBox(_game)),
        };
        foreach (var (name, box) in parts)
        {
            if (box.IntersectsWith(_view)) PartBox(name, box, name == "target bar", here);
            else EdgeMarker(name, box, taken, w, h);
        }

        // the other snap spots on this screen, as dotted ghosts the panel's size
        foreach (var s in HudSpots.All)
        {
            if (s.Key == _chosen) continue;
            var spot = Spot(s.Key);
            if (!spot.IntersectsWith(_view)) continue;
            var g = Visible(spot);
            if (Math.Abs(g.X - here.X) < 4 && Math.Abs(g.Y - here.Y) < 4) continue;
            Ghost(s.Name, g);
        }
        PaintChooser();   // first: the lit spot's bold name is wider, and the chooser is centred on its measured width
        PlaceTop();
    }

    /// <summary>The panel, the gold outline round it, and its tag and chooser under it (over it when it sits low).</summary>
    void PlaceTop()
    {
        double w = _top.Width, h = _top.Height;
        var win = Dip(new DRect(_pos, _size));
        _panel.Width = win.Width; _panel.Height = win.Height;
        _panel.Children.Clear();
        _panel.Children.Add(_preview);
        var vis = Visible(new DRect(_pos, _size));
        double ox = vis.X - win.X, oy = vis.Y - win.Y;
        // the dashed gold outline 7 px outside the look, and a handle on each corner
        _panel.Children.Add(new Rectangle
        {
            Stroke = Gold, StrokeThickness = 2, StrokeDashArray = GoldDashes, RadiusX = 15, RadiusY = 15, IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(ox - OutlineGap, oy - OutlineGap, 0, 0), Width = vis.Width + 2 * OutlineGap, Height = vis.Height + 2 * OutlineGap,
            Effect = new DropShadowEffect { Color = Rgb(GoldC), BlurRadius = 18, ShadowDepth = 0, Opacity = .25 },
        });
        var marks = new Canvas { IsHitTestVisible = false };
        foreach (var (a, b) in new[] { (-11.0, -11.0), (vis.Width + 2, -11.0), (-11.0, vis.Height + 2), (vis.Width + 2, vis.Height + 2) })
        {
            var hd = new Border { Width = 9, Height = 9, Background = Gold, BorderBrush = TagInk, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(2) };
            Canvas.SetLeft(hd, ox + a); Canvas.SetTop(hd, oy + b);
            marks.Children.Add(hd);
        }
        _panel.Children.Add(marks);
        Canvas.SetLeft(_panel, win.X); Canvas.SetTop(_panel, win.Y);

        _tag.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        _chooser.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double tw = _tag.DesiredSize.Width, th = _tag.DesiredSize.Height, cw = _chooser.DesiredSize.Width, ch = _chooser.DesiredSize.Height;
        double cx = vis.X + vis.Width / 2;
        double tagY = vis.Bottom + 16, chooserY = tagY + 42;
        if (chooserY + ch > h - 60) { tagY = vis.Top - 16 - th; chooserY = tagY - 42 + (th - ch) / 2 - 4; }   // low on the screen: above it instead
        Canvas.SetLeft(_tag, Math.Clamp(cx - tw / 2, 8, Math.Max(8, w - tw - 8))); Canvas.SetTop(_tag, Math.Max(8, tagY));
        Canvas.SetLeft(_chooser, Math.Clamp(cx - cw / 2, 8, Math.Max(8, w - cw - 8))); Canvas.SetTop(_chooser, Math.Max(8, chooserY));
    }

    static void Put(Canvas c, FrameworkElement e, double x, double y)
    {
        Canvas.SetLeft(e, x); Canvas.SetTop(e, y);
        c.Children.Add(e);
    }

    static TextBlock Label(string text, double size, Brush fg, FontWeight? weight = null) =>
        new() { Text = text, FontSize = size, Foreground = fg, FontFamily = HudKit.Text, FontWeight = weight ?? FontWeights.Normal, VerticalAlignment = VerticalAlignment.Center };

    /// <summary>A dashed or dotted box with something inside (WPF's border draws no dashes): the stroke under the content.</summary>
    static Grid Boxed(FrameworkElement? child, Brush stroke, double thickness, DoubleCollection dash, double radius, Brush? fill, Thickness pad)
    {
        var g = new Grid();
        g.Children.Add(new Rectangle { Stroke = stroke, StrokeThickness = thickness, StrokeDashArray = dash, RadiusX = radius, RadiusY = radius, Fill = fill });
        if (child != null) g.Children.Add(new Border { Padding = pad, Child = child });
        return g;
    }

    /// <summary>"Game · minimap": a small dark label for the game's parts.</summary>
    static Border PartLabel(string name, Brush? fg = null, Brush? bg = null) => new()
    {
        Background = bg ?? LabelBg, CornerRadius = new CornerRadius(4), Padding = new Thickness(8, 2, 8, 2),
        Child = Label(name, 12, fg ?? LabelInk, FontWeights.SemiBold),
    };

    /// <summary>
    /// One of the game's parts in place: a faint dashed outline and its label above it (under it at the top of the screen,
    /// and beside it when the panel sits over that spot).
    /// </summary>
    void PartBox(string name, DRect box, bool labelRight, Rect panel)
    {
        var r = Dip(box);
        var outline = Boxed(null, PartLine, 1.5, Dashes, 8, PartFill, new Thickness(0));
        outline.Width = r.Width; outline.Height = r.Height;
        Put(_veil, outline, r.X, r.Y);
        var label = PartLabel("Game · " + name);
        label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double lw = label.DesiredSize.Width, lh = label.DesiredSize.Height;
        double lx = labelRight ? r.Right - lw : r.X, ly = r.Y - lh - 6;
        if (labelRight || ly < 8) ly = r.Bottom + 6;
        var round = new Rect(panel.X - OutlineGap - 6, panel.Y - OutlineGap - 6, panel.Width + 2 * OutlineGap + 12, panel.Height + 2 * OutlineGap + 50);   // with its tag
        if (round.IntersectsWith(new Rect(lx, ly, lw, lh)))
        {
            // beside the box, on the side away from the panel
            bool right = panel.X + panel.Width / 2 < r.X + r.Width / 2;
            lx = right ? r.Right + 8 : r.X - lw - 8;
            ly = r.Y + (r.Height - lh) / 2;
            if (round.IntersectsWith(new Rect(lx, ly, lw, lh))) { lx = right ? Math.Max(r.Right, round.Right) + 8 : Math.Min(r.X, round.X) - lw - 8; }
        }
        Put(_veil, label, Math.Clamp(lx, 8, Math.Max(8, _veil.Width - lw - 8)), Math.Clamp(ly, 8, Math.Max(8, _veil.Height - lh - 8)));
    }

    /// <summary>Another snap spot: a dotted ghost the panel's size with the spot's name in its corner.</summary>
    void Ghost(string name, Rect r)
    {
        var g = Boxed(null, GhostLine, 1.5, Dots, 10, GhostFill, new Thickness(0));
        g.Width = r.Width; g.Height = r.Height;
        Put(_veil, g, r.X, r.Y);
        var label = new Border
        {
            Background = GhostLabelBg, CornerRadius = new CornerRadius(4), Padding = new Thickness(8, 2, 8, 2),
            Child = Label(name, 12, Gold, FontWeights.Medium),
        };
        Put(_veil, label, r.X + 8, r.Y + 8);
    }

    /// <summary>
    /// One of the game's parts off this screen: a marker on the nearest edge, "Game · minimap 983 px right, top-right
    /// corner →", stacked clear of the help bar and the other markers.
    /// </summary>
    void EdgeMarker(string name, DRect box, List<Rect> taken, double w, double h)
    {
        int left = _view.Left - box.Right, right = box.Left - _view.Right, above = _view.Top - box.Bottom, below = box.Top - _view.Bottom;
        var gaps = new (string Side, int Gap)[] { ("left", left), ("right", right), ("above", above), ("below", below) };
        var (side, gap) = gaps.MaxBy(g => g.Gap);
        bool sideways = side is "left" or "right";
        bool justOff = gap <= (sideways ? _view.Width : _view.Height) * 0.08;
        string how = justOff ? side switch { "left" => "just left of this screen", "right" => "just right of this screen", "above" => "just above this screen", _ => "just below this screen" }
            : $"{gap:N0} px {side}";
        string arrow = (left > 0, right > 0, above > 0, below > 0) switch
        {
            (true, _, true, _) => "↖", (true, _, _, true) => "↙", (_, true, true, _) => "↗", (_, true, _, true) => "↘",
            (true, _, _, _) => "←", (_, true, _, _) => "→", (_, _, true, _) => "↑", _ => "↓",
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var arrowText = Label(arrow, 15, Gold); arrowText.Margin = new Thickness(side == "right" ? 6 : 0, 0, side == "right" ? 0 : 6, 0);
        if (side != "right") row.Children.Add(arrowText);
        row.Children.Add(Label("Game · " + name, 12.5, LabelInk, FontWeights.SemiBold));
        var detail = Label(how + ", " + WhereIn(box), 12.5, Faint); detail.Margin = new Thickness(6, 0, 0, 0);
        row.Children.Add(detail);
        if (side == "right") row.Children.Add(arrowText);
        var marker = Boxed(row, EdgeLine, 1, Dashes, 6, EdgeBg, new Thickness(10, 4, 10, 4));
        marker.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double mw = marker.DesiredSize.Width, mh = marker.DesiredSize.Height;
        double cx = (box.Left + box.Width / 2.0 - _view.Left) / _dpi, cy = (box.Top + box.Height / 2.0 - _view.Top) / _dpi;
        double x, y;
        switch (side)
        {
            case "right": x = w - 10 - mw; y = Math.Clamp(cy - mh / 2, 10, Math.Max(10, h - 10 - mh)); break;
            case "left": x = 10; y = Math.Clamp(cy - mh / 2, 10, Math.Max(10, h - 10 - mh)); break;
            case "above": y = 10; x = Math.Clamp(cx - mw / 2, 10, Math.Max(10, w - 10 - mw)); break;
            default: y = h - 12 - mh; x = Math.Clamp(cx - mw / 2, 10, Math.Max(10, w - 10 - mw)); break;
        }
        // clear of the help bar and the markers already placed: up the screen (down from the top edge)
        for (int i = 0; i < 12 && taken.Any(t => t.IntersectsWith(new Rect(x, y, mw, mh))); i++) y += side == "above" ? mh + 8 : -(mh + 8);
        taken.Add(new Rect(x, y, mw, mh));
        Put(_veil, marker, x, y);
    }

    /// <summary>Where a part sits in the game, in words: "top-right corner", "bottom centre", "left of centre".</summary>
    string WhereIn(DRect box)
    {
        double fx = (box.Left + box.Width / 2.0 - _game.Left) / Math.Max(1, _game.Width), fy = (box.Top + box.Height / 2.0 - _game.Top) / Math.Max(1, _game.Height);
        string v = fy < 0.33 ? "top" : fy > 0.67 ? "bottom" : "";
        string hz = fx < 0.25 ? "left" : fx > 0.75 ? "right" : fx is >= 0.45 and <= 0.55 ? "centre" : fx < 0.5 ? "left of centre" : "right of centre";
        if (v.Length == 0) return hz == "centre" ? "the centre" : hz;
        return hz switch { "left" or "right" => $"{v}-{hz} corner", "centre" => $"{v} centre", _ => $"{v}, {hz}" };
    }

    /// <summary>A key in the help bar and the tag: "Enter", "Esc".</summary>
    static Border Kbd(string key, Brush fg, Brush bg, Brush line, FontWeight weight) => new()
    {
        Background = bg, BorderBrush = line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(6, 1, 6, 1),
        VerticalAlignment = VerticalAlignment.Center, Child = Label(key, 11.5, fg, weight),
    };

    /// <summary>"Place your panel · Drag it, or pick a snap spot · Enter save · Esc put it back", along the bottom.</summary>
    static Border HelpBar()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        void Add(FrameworkElement e, double gap) { e.Margin = new Thickness(gap, 0, 0, 0); row.Children.Add(e); }
        Add(Label("Place your panel", 13, Ink, FontWeights.SemiBold), 0);
        Add(Label("Drag it, or pick a snap spot", 13, Muted), 14);
        Add(Kbd("Enter", Ink, Slot, BadgeLine, FontWeights.SemiBold), 14);
        Add(Label("save", 13, Muted), 6);
        Add(Kbd("Esc", Ink, Slot, BadgeLine, FontWeights.SemiBold), 14);
        Add(Label("put it back", 13, Muted), 6);
        var bar = new Border { Background = PanelBg, BorderBrush = FrameLine, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(16, 0, 16, 0), Height = 36, Child = row };
        bar.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return bar;
    }

    /// <summary>The gold tag under the panel: "Drag me, then press Enter".</summary>
    Border MakeTag()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(new System.Windows.Shapes.Path { Data = DragIcon, Stroke = TagInk, StrokeThickness = 1.5, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round, Width = 15, Height = 15, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center });
        var words = Label("Drag me, then press", 13, TagInk, FontWeights.Bold); words.Margin = new Thickness(7, 0, 7, 0);
        row.Children.Add(words);
        row.Children.Add(Kbd("Enter", TagInk, KbdOnTag, KbdOnTagLine, FontWeights.Bold));
        return new Border
        {
            Height = 28, Padding = new Thickness(9, 0, 12, 0), CornerRadius = new CornerRadius(14), Background = TagFill, Child = row, IsHitTestVisible = false,
            Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 10, ShadowDepth = 2, Direction = 270, Opacity = .45 },
        };
    }

    /// <summary>The "SNAP TO" pill: the three spots, the one the panel sits at lit in gold; a click moves it there.</summary>
    Border MakeChooser()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var k = Label("SNAP TO", 11, Faint, FontWeights.SemiBold); k.Margin = new Thickness(0, 0, 10, 0);
        row.Children.Add(k);
        for (int i = 0; i < HudSpots.All.Length; i++)
        {
            var s = HudSpots.All[i];
            if (i > 0) { var dot = Label("·", 13, Faint); dot.Margin = new Thickness(4, 0, 4, 0); row.Children.Add(dot); }
            var text = Label(s.Name, 13, Muted);
            var seg = new Border { CornerRadius = new CornerRadius(13), Padding = new Thickness(11, 5, 11, 5), Background = Brushes.Transparent, BorderThickness = new Thickness(1), BorderBrush = Brushes.Transparent, Child = text, Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center };
            AutomationPropertiesName(seg, "Snap to " + s.Name);
            seg.MouseLeftButtonUp += (_, e) => { Snap(s.Key); e.Handled = true; };
            seg.MouseEnter += (_, _) => { if (_chosen != s.Key) text.Foreground = Ink; };
            seg.MouseLeave += (_, _) => PaintChooser();
            _segs[s.Key] = (seg, text);
            row.Children.Add(seg);
        }
        return new Border
        {
            Height = 34, Padding = new Thickness(14, 0, 5, 0), CornerRadius = new CornerRadius(17), Background = PanelBg, BorderBrush = FrameLine, BorderThickness = new Thickness(1), Child = row,
            Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 12, ShadowDepth = 2, Direction = 270, Opacity = .45 },
        };
    }

    void PaintChooser()
    {
        foreach (var (key, (seg, text)) in _segs)
        {
            bool on = key == _chosen;
            seg.Background = on ? SegOn : Brushes.Transparent;
            seg.BorderBrush = on ? SegOnLine : Brushes.Transparent;
            text.Foreground = on ? Gold : Muted;
            text.FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal;
        }
    }
}
