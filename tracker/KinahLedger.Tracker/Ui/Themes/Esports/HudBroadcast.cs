using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using KinahLedger.Parser;
using KinahLedger.Session;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

/// <summary>
/// Broadcast (the Esports theme, looks pack 3, 2026-11-20): a tournament stream's lower third. Every part is a dark navy
/// plate cut on a 12° slant, stacked and spaced like a broadcast package, with an electric-cyan bar on its left edge and
/// condensed bold type (Bahnschrift Condensed). The fight's head is three plates in a row: a red LIVE tag with its dot, the
/// boss on a navy plate, the clock on a cyan tile; the boss's HP a slanted strip under it. Each member is a slanted rank
/// chip, a plate whose fill is the bar (the class's colour, deepening to a hard angled leading edge with speed lines behind
/// it) and a score tile with the DPS. The leader's chip is cyan with a small crown. The Buddy is a lower-third bar: the hour
/// as its headline, the session under it, your best as a stat tile at its left, the pickups on a slanted strip, the timers
/// as slanted chips.
///
/// The moments are a broadcast's transitions: a stinger (a cyan wipe with a hard white edge) crosses the plates on a
/// fight's start, a kill and a new leader; the plates wipe in on their slant; the leader's chip fills cyan as its crown
/// lands; a row that climbs gets a quick white wipe; a kill lays a slanted KILLED banner over the head. Lightweight (Steven,
/// 2026-11-20, "cinematic ... lightweight - not bulky"): every part is drawn once when its figures change; a moment only
/// moves transforms and opacities (a wipe is a slanted clip slid by a transform, a stinger a frozen band slid across its
/// plate's clip, a bar a fill slid under its plate's clip), nothing is laid out or drawn again while it plays.
/// </summary>
sealed partial class HudBroadcast : ModernLook
{
    // the package: navy ink, electric cyan, the LIVE red
    static readonly Color InkC = Color.FromRgb(0x06, 0x0A, 0x16), Ink2C = Color.FromRgb(0x12, 0x1B, 0x33), CyanC = Color.FromRgb(0x1C, 0xE3, 0xFF),
        LiveC = Color.FromRgb(0xFF, 0x1F, 0x45), MintC2 = Color.FromRgb(0x4D, 0xF5, 0xA8), CoralC = Color.FromRgb(0xFF, 0x7A, 0x86),
        HpC = Color.FromRgb(0xFF, 0x3D, 0x62), GoldC2 = Color.FromRgb(0xFF, 0xCF, 0x5A), WarnC2 = Color.FromRgb(0xFF, 0xB0, 0x86), GreyC = Color.FromRgb(0x8A, 0x92, 0xA6);
    static readonly SolidColorBrush White = Frozen(Colors.White), Soft = Frozen(Mod.A(Colors.White, .84)), Quiet = Frozen(Mod.A(Colors.White, .62)),
        Cyan = Frozen(CyanC), InkB = Frozen(InkC), Mint2 = Frozen(Color.FromRgb(0x8C, 0xFF, 0xC6)), Coral = Frozen(CoralC);

    /// <summary>The package's slant: tan 12°, a plate's top this much of its height right of its foot.</summary>
    const double Tan = 0.2126;

    /// <summary>The package's face: Bahnschrift (condensed for the figures and the tags).</summary>
    static readonly FontFamily Cond = HudThemes.Bahn;

    // the room round the look: the shade stays inside the window; 98 at the right for the pickup's chip beside the Buddy
    readonly Grid _root = new() { Margin = new Thickness(8, 8, 98, 10), HorizontalAlignment = HorizontalAlignment.Left };
    readonly StackPanel _body = new() { HorizontalAlignment = HorizontalAlignment.Left };
    double _opacity = 0.9;

    // the Buddy: the lower-third bar, its best tile, the hour, the session, the strip and the timer chips
    const double LineLean = 8;
    readonly Grid _line = new() { HorizontalAlignment = HorizontalAlignment.Left };
    readonly TacSketch _lineArt;
    readonly Sweep _lineSweep = new(LineLean);
    readonly StackPanel _lineBody = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(LineLean / 2 + 4, 5, LineLean / 2 + 10, 7) };
    readonly Canvas _lineAnchor = Tac.Anchor(HorizontalAlignment.Right, VerticalAlignment.Center);
    readonly StackPanel _timers = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    readonly Border _timersInLine = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
    /// <summary>The timer chips over the line, when the pickup strip takes their room on it.</summary>
    readonly Border _timersUp = new() { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(2, 0, 0, 4), Visibility = Visibility.Collapsed };
    /// <summary>The pickup strip in Broadcast: 24 px tiles on a slanted navy strip, a cyan rule, a gold star.</summary>
    static readonly PickStrip.Look Strip = new(24, 4, true, Frozen(Mod.A(CyanC, .45)), Frozen(GoldC2));
    bool _dim, _accentLeft = true;
    Slab? _bestTile;
    ScaleTransform? _bestBar;
    double _share = -1;
    Slab? _firstChip;

    // the rare drop under the Buddy, and the notice line
    readonly Grid _rare = new() { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0), Visibility = Visibility.Collapsed };
    readonly Grid _note = new() { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0), Visibility = Visibility.Collapsed };

    // the fight: the head's plates, the HP strip, the rows and the foot
    readonly Grid _fight = new() { HorizontalAlignment = HorizontalAlignment.Left, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 8) };
    readonly StackPanel _fightBody = new();
    readonly Border _timersInFight = new() { Margin = new Thickness(0, 0, 0, 6), HorizontalAlignment = HorizontalAlignment.Left };
    Grid? _head;
    FrameworkElement? _hpLine;
    readonly List<Slab> _headSlabs = new();
    Slab? _footSlab;
    string _leadFight = "";
    long _leadId;

    public HudBroadcast() : base(bare: true)
    {
        // the look's own rows: a rank chip, a plate filled to the share, a score tile
        UseRows(BcRow.Pitch, m => new BcRow(), 15, GapLine);
        _lineArt = new TacSketch(DrawLinePlate);
        _lineSweep.Shape = s => Cut(new Rect(0, 0, s.Width, s.Height), LineLean);
        _line.Children.Add(_lineArt); _line.Children.Add(_lineSweep); _line.Children.Add(Shaded(_lineBody)); _line.Children.Add(_lineAnchor);
        _fight.Children.Add(_fightBody);
        _runHeadSlab = new Slab(_runHead, InkC, .9, CyanC, 18, padX: 6) { Visibility = Visibility.Collapsed, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(6, 0, 0, 3) };
        _body.Children.Add(_fight); _body.Children.Add(_timersUp); _body.Children.Add(_runHeadSlab); _body.Children.Add(_line); _body.Children.Add(_rare); _body.Children.Add(_note);
        _root.Children.Add(_body);
        System.Windows.Documents.TextElement.SetFontFamily(_root, Cond);
        TextOptions.SetTextFormattingMode(_root, TextFormattingMode.Display);
    }

    public override FrameworkElement Root => _root;
    public override Thickness Inset => _root.Margin;
    public override double Corner => 0;
    public override FrameworkElement? FightFrame => _fight.Visibility == Visibility.Visible ? _fight : null;

    public override void SetOpacity(double opacity)
    {
        // the plates take the setting (never so faint the words lose their ground)
        _opacity = Math.Clamp(opacity, 0.3, 1);
        Ground = .62 + .38 * _opacity;
        _lineArt.InvalidateVisual();
    }

    /// <summary>The plates' strength from the opacity setting: about .96 at the usual 90%.</summary>
    static double Ground = .962;

    protected override string LineSig(Figures f) => base.LineSig(f) + "|" + (Rare(Values)?.Id ?? "") + "|" + (Picks(Values).FirstOrDefault()?.Id ?? "") + "|" + Dimmed(Values) + "|" + PickStrip.Sig(Values, Strip) + "|" + RunBuddy.Sig(Values);

    static bool Dimmed(IReadOnlyDictionary<string, string> v) => (v.TryGetValue("show", out var s) && s == "dim") || (v.TryGetValue("paused", out var p) && p.Length > 0);

    // ================================================================ the kit

    /// <summary>The tight shade under the words on a plate: 1 px down, 2 px soft.</summary>
    static DropShadowEffect Shade() => new() { Color = Colors.Black, Direction = 270, ShadowDepth = 1, BlurRadius = 3, Opacity = .9 };

    /// <summary>The wide soft halo round words straight on the game (8 px), so a bright scene behind them goes a shade darker.</summary>
    static DropShadowEffect Halo() => new() { Color = Colors.Black, ShadowDepth = 0, BlurRadius = 10, Opacity = .62 };

    /// <summary>Words on a plate: the tight shade on the group.</summary>
    static T Shaded<T>(T el) where T : FrameworkElement { el.Effect = Shade(); return el; }

    /// <summary>Words straight on the game: the tight shade on them, the halo round them.</summary>
    static Border Lit(FrameworkElement el)
    {
        el.Effect = Shade();
        return new Border { Child = el, Effect = Halo(), HorizontalAlignment = el.HorizontalAlignment, VerticalAlignment = el.VerticalAlignment };
    }

    /// <summary>A text in Bahnschrift, condensed (<paramref name="tight"/>) for the figures and the tags; tabular digits.</summary>
    static TextBlock Bc(string s, double size, Brush fg, FontWeight? w = null, bool tight = true)
    {
        var t = Tt(s, size, fg, w ?? FontWeights.Bold, Cond);
        if (tight) t.FontStretch = FontStretches.Condensed;
        return t;
    }

    /// <summary>A parallelogram on the package's slant inside <paramref name="r"/>: its top <paramref name="lean"/> right of its foot.</summary>
    static Geometry Cut(Rect r, double lean) => Tac.Lean(r, Math.Max(0, Math.Min(lean, r.Width)));

    /// <summary>The stinger's bands, one per colour, frozen: a cyan (or any) wash with a hard white leading edge.</summary>
    static readonly Dictionary<Color, Brush> Bands = new();

    static Brush BandBrush(Color c)
    {
        if (Bands.TryGetValue(c, out var b)) return b;
        var g = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        g.GradientStops.Add(new GradientStop(Mod.A(c, 0), 0));
        g.GradientStops.Add(new GradientStop(Mod.A(c, .45), .68));
        g.GradientStops.Add(new GradientStop(Mod.A(Colors.White, .92), .8));
        g.GradientStops.Add(new GradientStop(Mod.A(Colors.White, 0), .87));
        g.Freeze();
        return Bands[c] = g;
    }

    /// <summary>
    /// The stinger on a plate: a frozen band on the slant, clipped to its plate's shape, slid across it by a transform
    /// (<see cref="At"/> 0 to 1; outside, hidden). Built once per size; a moment moves only its transform and its opacity.
    /// </summary>
    sealed class Sweep : Canvas
    {
        readonly System.Windows.Shapes.Path _band = new() { IsHitTestVisible = false };
        readonly TranslateTransform _x = new();
        readonly double _lean;
        double _p = -1, _bw;
        Color _c;
        /// <summary>The plate's shape in this sweep's size (its clip); a plain rectangle when unset.</summary>
        public Func<Size, Geometry>? Shape;

        public Sweep(double lean)
        {
            _lean = lean;
            IsHitTestVisible = false;
            Visibility = Visibility.Collapsed;
            _band.RenderTransform = _x;
            Children.Add(_band);
            SizeChanged += (_, _) => { Fit(); Place(); };
        }

        Size _fit;

        /// <summary>Its clip and band made again (the plate's shape changed without its size).</summary>
        public void Refit() { _fit = default; Fit(); Place(); }

        void Fit()
        {
            var s = RenderSize;
            if (s.Width < 1 || s.Height < 1) return;
            _fit = s;
            var clip = Shape?.Invoke(s) ?? new RectangleGeometry(new Rect(s));
            if (clip.CanFreeze) clip.Freeze();
            Clip = clip;
            _bw = Math.Max(34, s.Height * 1.6);
            var g = Cut(new Rect(0, 0, _bw + _lean, s.Height), _lean);
            _band.Data = g;
        }

        void Place()
        {
            var s = RenderSize;
            if (_band.Data == null || s != _fit) Fit();
            _x.X = -_bw - _lean + (s.Width + _bw + 2 * _lean) * Math.Clamp(_p, 0, 1);
        }

        /// <summary>The band <paramref name="p"/> of its way across (-1: gone), in <paramref name="c"/>, at <paramref name="strength"/>.</summary>
        public void At(double p, Color c, double strength = 1)
        {
            _p = p;
            bool on = p is >= 0 and <= 1 && strength > 0;
            if (!on) { if (Visibility != Visibility.Collapsed) Visibility = Visibility.Collapsed; return; }
            if (_c != c || _band.Fill == null) { _c = c; _band.Fill = BandBrush(c); }
            _band.Opacity = Math.Clamp(strength, 0, 1);
            if (Visibility != Visibility.Visible) Visibility = Visibility.Visible;
            Place();
        }
    }

    /// <summary>
    /// A wipe on the slant: the element's clip, a parallelogram slid in from the left by a transform (<see cref="At"/> 0: none
    /// shown, 1: all, the clip taken off). Words are never under a wipe (they fade): only plates.
    /// </summary>
    sealed class Wiper
    {
        readonly FrameworkElement _el;
        readonly double _lean;
        readonly TranslateTransform _x = new();
        Geometry? _g;
        Size _s;

        public Wiper(FrameworkElement el, double lean) { _el = el; _lean = lean; }

        public void At(double k)
        {
            if (k >= 1) { if (_el.Clip != null) _el.Clip = null; return; }
            var s = _el.RenderSize;
            if (_g == null || s != _s)
            {
                _s = s;
                double w = s.Width + 2 * _lean + 2;
                _g = Cut(new Rect(-_lean - 1, -1, w, s.Height + 2), _lean).Clone();
                _g.Transform = _x;
            }
            if (_el.Clip != _g) _el.Clip = _g;
            _x.X = -(s.Width + 2 * _lean + 2) * (1 - Math.Clamp(k, 0, 1));
        }
    }

    /// <summary>
    /// A slanted plate with its words: a navy (or any colour) parallelogram, lighter a touch at its top, a bold bar on its left
    /// edge, a hairline of light along its top. A moment wipes it in on its slant (<see cref="Wipe"/>), sends the stinger across
    /// it (<see cref="SweepAt"/>) or flashes it (<see cref="FlashAt"/>): transforms and opacities only. <see cref="Paint"/>
    /// draws what a plate adds (a wash, a track), drawn once with the plate.
    /// </summary>
    sealed class Slab : Grid
    {
        readonly Grid _layers = new() { IsHitTestVisible = false };
        readonly TacSketch _art;
        readonly System.Windows.Shapes.Path _flash = new() { IsHitTestVisible = false, Opacity = 0 };
        readonly Sweep _sweep;
        readonly Wiper _wiper;
        Color _flashC;
        public Color Fill, Accent;
        public double Alpha, AccentW, Lean;
        public bool Rule = true;
        /// <summary>A hard offset shadow under the plate (px down and right; 0 none), the package's depth.</summary>
        public double Shadow;
        /// <summary>A second plate peeking out behind this one, up and right (a broadcast's layered plates).</summary>
        public Color? Back;
        /// <summary>A gloss on the plate's top half.</summary>
        public bool Gloss = true;
        public Action<DrawingContext, Size, Geometry, double>? Paint;
        public FrameworkElement? Content { get; }

        /// <param name="h">The plate's height (its slant is worked out from it); NaN: as its words need, slanted as for 24.</param>
        public Slab(FrameworkElement? content, Color fill, double alpha, Color? accent = null, double h = double.NaN, double padX = 7, double padY = 0, double accentW = 3.5)
        {
            Fill = fill; Alpha = alpha; Accent = accent ?? Colors.Transparent; AccentW = accent == null ? 0 : accentW;
            Lean = (double.IsNaN(h) ? 24 : h) * Tan;
            Height = h;
            VerticalAlignment = VerticalAlignment.Center;
            _art = new TacSketch(Draw);
            _sweep = new Sweep(Lean) { Shape = s => Cut(new Rect(0, 0, s.Width, s.Height), Math.Min(Lean, s.Width / 3)) };
            _layers.Children.Add(_art); _layers.Children.Add(_flash); _layers.Children.Add(_sweep);
            Children.Add(_layers);
            _wiper = new Wiper(_layers, Lean);
            if (content != null)
            {
                Content = content;
                content.Margin = new Thickness(Lean / 2 + AccentW + padX, padY, Lean / 2 + padX, padY);
                content.VerticalAlignment = VerticalAlignment.Center;
                Children.Add(content);
            }
            SizeChanged += (_, e) => { var g = Cut(new Rect(0, 0, e.NewSize.Width, e.NewSize.Height), Math.Min(Lean, e.NewSize.Width / 3)); g.Freeze(); _flash.Data = g; };
        }

        /// <summary>The plate's parts that a wipe clips (a layer added here is wiped with it).</summary>
        public Grid Layers => _layers;

        /// <summary>The wipe on the slant: 0 nothing, 1 the whole plate.</summary>
        public double Wipe { set => _wiper.At(value); }

        public void SweepAt(double p, Color? c = null, double strength = 1) => _sweep.At(p, c ?? CyanC, strength);

        public void FlashAt(double a, Color c)
        {
            if (_flashC != c || _flash.Fill == null) { _flashC = c; _flash.Fill = Frozen(Mod.A(c, .78)); }
            _flash.Opacity = Math.Clamp(a, 0, 1);
        }

        /// <summary>Drawn again: only when what it shows changes, never by a moment.</summary>
        public void Redraw() => _art.InvalidateVisual();

        void Draw(DrawingContext dc, Size s)
        {
            if (s.Width < 2 || s.Height < 2) return;
            double l = Math.Min(Lean, s.Width / 3);
            var shape = Cut(new Rect(0, 0, s.Width, s.Height), l);
            double a = Math.Min(1, Alpha * (Fill == InkC || Fill == Ink2C ? Ground : 1));
            if (Back is { } bk)
            {
                dc.PushTransform(new TranslateTransform(5, -3));
                dc.DrawGeometry(Mod.Grad(false, (Mod.A(bk, .15), 0), (Mod.A(bk, .85), 1)), null, shape);
                dc.Pop();
            }
            if (Shadow > 0)
            {
                dc.PushTransform(new TranslateTransform(Shadow, Shadow));
                dc.DrawGeometry(ShadowB, null, shape);
                dc.Pop();
            }
            dc.DrawGeometry(Mod.Grad(true, (Mod.A(Mod.Mix(Fill, Colors.White, .08), a), 0), (Mod.A(Fill, a), .55), (Mod.A(Mod.Mix(Fill, Colors.Black, .12), a), 1)), null, shape);
            if (Gloss) dc.DrawGeometry(GlossB, null, shape);
            Paint?.Invoke(dc, s, shape, l);
            if (AccentW > 0) dc.DrawGeometry(Frozen(Accent), null, Cut(new Rect(0, 0, AccentW + l, s.Height), l));
            if (Rule) { dc.PushClip(shape); dc.DrawRectangle(Frozen(Mod.A(Colors.White, .16)), null, new Rect(0, 0, s.Width, 1)); dc.Pop(); }
        }
    }

    /// <summary>The package's depth: the hard offset shadow under a plate, the gloss on its top half (frozen, shared).</summary>
    static readonly Brush ShadowB = Frozen(Mod.A(Colors.Black, .55)),
        GlossB = Mod.Grad(true, (Mod.A(Colors.White, .17), 0), (Mod.A(Colors.White, .05), .46), (Mod.A(Colors.White, 0), .5), (Mod.A(Colors.Black, 0), .8), (Mod.A(Colors.Black, .14), 1));

    /// <summary>A class's colour made deep and vivid for a bar's fill (light pastels like the gladiator's blue went pale under white names).</summary>
    static Color Vivid(Color c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0, max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
        double h = d == 0 ? 0 : max == r ? ((g - b) / d % 6 + 6) % 6 : max == g ? (b - r) / d + 2 : (r - g) / d + 4;
        double sat = Math.Max(max == 0 ? 0 : d / max, .84), val = Math.Clamp(max, .62, h is >= .5 and < 1.6 ? .8 : .86);
        double cc = val * sat, x = cc * (1 - Math.Abs(h % 2 - 1)), m = val - cc;
        var (r1, g1, b1) = h < 1 ? (cc, x, 0.0) : h < 2 ? (x, cc, 0.0) : h < 3 ? (0.0, cc, x) : h < 4 ? (0.0, x, cc) : h < 5 ? (x, 0.0, cc) : (cc, 0.0, x);
        return Color.FromRgb((byte)Math.Round((r1 + m) * 255), (byte)Math.Round((g1 + m) * 255), (byte)Math.Round((b1 + m) * 255));
    }

    /// <summary>The foot of a slanted plate from <paramref name="y0"/> down to its bottom, as a shape of its own (a bar along it).</summary>
    static Geometry FootStrip(Size s, double lean, double y0)
    {
        double XL(double y) => lean * (1 - y / s.Height);
        double XR(double y) => s.Width - lean * y / s.Height;
        var g = new StreamGeometry();
        using (var x = g.Open())
        {
            x.BeginFigure(new Point(XL(y0), y0), true, true);
            x.LineTo(new Point(XR(y0), y0), true, false);
            x.LineTo(new Point(XR(s.Height), s.Height), true, false);
            x.LineTo(new Point(XL(s.Height), s.Height), true, false);
        }
        g.Freeze();
        return g;
    }

    /// <summary>A slanted tag (LIVE, READY, an outcome): a filled plate with condensed caps in <paramref name="ink"/>.</summary>
    static Slab Tag(string s, Color fill, Brush ink, double h, double size, bool dot = false)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        if (dot) sp.Children.Add(new System.Windows.Shapes.Ellipse { Width = 6, Height = 6, Fill = ink, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
        sp.Children.Add(Bc(s, size, ink));
        return new Slab(sp, fill, 1, null, h, padX: 5) { Rule = false };
    }

    /// <summary>A small slanted mark on a row (★ DPS, the heal mark, AVG): navy with a coloured edge; <paramref name="filled"/> white with ink words (YOU).</summary>
    static Slab Mark(string s, Color? edge = null, bool filled = false, double size = 10.5)
    {
        var t = Bc(s, size, filled ? InkB : Frozen(edge is { } e ? Mod.Mix(e, Colors.White, .35) : Colors.White));
        return new Slab(t, filled ? Colors.White : InkC, filled ? 1 : .9, filled ? null : edge ?? CyanC, 15, padX: 2.5, accentW: 2) { Rule = false };
    }

    // ================================================================ the Buddy

    protected override void DrawLine(Figures f)
    {
        _lineBody.Children.Clear();
        _bestTile = null; _bestBar = null; _timerUp = false; _stripTimerW = -1;
        if (_timersInLine.Child != null) _timersInLine.Child = null;
        _dim = Dimmed(Values);
        _line.Opacity = _dim ? .66 : 1;
        _accentLeft = true;
        bool mark = f.Abyss && f.Counted && f.Icon.Length > 0;
        if (mark) { var m = AbyssMark(f.Icon, 34); m.Margin = new Thickness(2, 0, 9, 0); _lineBody.Children.Add(m); }
        // in a dungeon run: the run's clock, splits and verdict in place of the hour (HudBroadcast.Run.cs)
        var rs = RunNow();
        _runHeadSlab.Visibility = Visibility.Collapsed;
        if (rs != null) DrawRun(rs);
        else if (!HasHour(f))
        {
            var w = Bc(Waiting(f.Abyss), 14, Soft, FontWeights.SemiBold); w.Margin = new Thickness(mark ? 0 : 6, 6, 4, 6);
            // in the Abyss before the first points: what has happened so far under it ("2 kills / Kinah"), never an empty line
            var so = f.Abyss ? Sub(f, false) : null;
            if (so != null && so.Inlines.Count > 0)
            {
                var col = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(mark ? 0 : 6, 3, 4, 3) };
                w.Margin = new Thickness(0); col.Children.Add(w); col.Children.Add(so);
                _lineBody.Children.Add(col);
            }
            else _lineBody.Children.Add(w);
        }
        else
        {
            if (HasRing(f))
            {
                // your best here as the bar's stat tile: the share big, a cyan bar under it to the share (mint and a star once beaten)
                _share = f.Share;
                _bestTile = BestTile(f);
                _lineBody.Children.Add(_bestTile);
                _accentLeft = false;
            }
            else if (!mark) _lineBody.Children.Add(new Border { Width = 6 });
            // the hour as the headline, its unit small; the session under it
            var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var top = new StackPanel { Orientation = Orientation.Horizontal };
            var rate = Bc(f.Rate, f.Warm ? 17 : 27, f.Warm ? Soft : White, f.Warm ? FontWeights.SemiBold : FontWeights.Bold);
            rate.Tag = "buddy-rate";
            rate.Margin = new Thickness(0, f.Warm ? 0 : -3, 0, f.Warm ? 0 : -2);
            top.Children.Add(rate);
            if (f.Unit.Length > 0) { var u = Bc(f.Unit, 12.5, Cyan, FontWeights.Bold); u.VerticalAlignment = VerticalAlignment.Bottom; u.Margin = new Thickness(3, 0, 0, 3); top.Children.Add(u); }
            words.Children.Add(top);
            // farming, the pickups as the strip beside the hour where it fits (the best drop and the last three), the session
            // alone under it; with the timer chips at the line's end and no room left beside them, the chips go over the line
            var sub = Sub(f, true);
            FrameworkElement? strip = null, under = null;
            if (StripTime(f))
            {
                var bare = Sub(f, false);
                double baseW = _lineBody.Margin.Left + _lineBody.Margin.Right + _lineBody.Children.OfType<FrameworkElement>().Sum(e => Mod.W(e) + e.Margin.Left + e.Margin.Right), timerW = LineTimerW();
                double wordsW = Math.Max(Mod.W(top), Mod.W(bare)), oldW = Math.Max(Mod.W(top), Mod.W(sub));
                foreach (var look in StripFall(Strip))
                {
                    if ((strip = StripIn(StripRoom(FightWidth - 10, baseW + timerW, wordsW, oldW, 12 + StripPad), look)) != null) break;
                    if (timerW > 0 && (strip = StripIn(StripRoom(FightWidth - 10, baseW, wordsW, oldW + timerW, 12 + StripPad), look)) != null) { _timerUp = true; break; }
                }
                if (strip != null) { sub = bare; strip = StripPlate(strip); strip.Margin = new Thickness(12, 0, 0, 0); }
                // no room beside the hour: under the session, never dropped (Steven, 2026-11-19)
                else if ((under = StripUnder(Strip, false)) != null) sub = bare;
                _stripTimerW = timerW;
            }
            if (sub.Inlines.Count > 0) words.Children.Add(sub);
            if (under != null) words.Children.Add(under);
            _lineBody.Children.Add(words);
            if (strip != null) _lineBody.Children.Add(strip);
        }
        _lineBody.Children.Add(_timersInLine);
        PlaceTimers();
        _lineArt.InvalidateVisual();

        // the rare drop the window holds: a plate in its grade's colour, its tile, the RARE DROP tag and its name
        _rare.Children.Clear();
        if (!KinahHidden && Rare(Values) is { } r)
        {
            var c = ColorOf(r.Color, r.Grade);
            _rareC = c;
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 3) };
            var tile = Tile(r, 26); tile.Margin = new Thickness(0, 0, 8, 0);
            _rareTile = tile;
            row.Children.Add(tile);
            var mk = Tag(L.T("RARE DROP"), GoldC2, InkB, 17, 11); mk.Margin = new Thickness(0, 0, 8, 0); mk.Shadow = 1.5;
            row.Children.Add(mk);
            var nm = Bc(r.Name, 15, Frozen(Mod.Mix(c, Colors.White, .7)), FontWeights.Bold, tight: false);
            row.Children.Add(nm);
            if (r.Count.Length > 0) { var cn = Bc(" " + r.Count, 12, Soft, FontWeights.SemiBold); cn.Margin = new Thickness(2, 1, 0, 0); row.Children.Add(cn); }
            var plate = new Slab(Shaded(row), InkC, .95, c, 34, padX: 6, accentW: 5) { Shadow = 2.5, Back = GoldC2 };
            plate.Paint = (dc, s, shape, l) =>
            {
                // the grade's colour washing in from the left
                var b = new LinearGradientBrush { MappingMode = BrushMappingMode.Absolute, StartPoint = new Point(0, 0), EndPoint = new Point(s.Width, 0) };
                b.GradientStops.Add(new GradientStop(Mod.A(c, .34), 0));
                b.GradientStops.Add(new GradientStop(Mod.A(c, .08), .5));
                b.GradientStops.Add(new GradientStop(Mod.A(c, 0), 1));
                b.Freeze();
                dc.DrawGeometry(b, null, shape);
            };
            _rarePlate = plate;
            _rare.Children.Add(plate);
            _rare.Visibility = Visibility.Visible;
        }
        else { _rare.Visibility = Visibility.Collapsed; _rarePlate = null; _rareTile = null; }

        // the notice: a slim plate under the Buddy, its words in their tone, the warning sign before a pause
        _note.Children.Clear();
        if (f.Note.Length > 0 && !KinahHidden)
        {
            bool warn = ReferenceEquals(f.NoteTone, Warn);
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            if (warn) { var wi = WarnIcon(); wi.Width = wi.Height = 13; wi.Margin = new Thickness(0, 0, 5, 0); sp.Children.Add(wi); }
            var tone = warn ? WarnC2 : ReferenceEquals(f.NoteTone, Gold) ? GoldC2 : ReferenceEquals(f.NoteTone, Mint) ? MintC2 : Color.FromRgb(0xF2, 0xF4, 0xF8);
            var t = Bc(f.Note, 13, Frozen(Mod.Mix(tone, Colors.White, .2)), FontWeights.SemiBold, tight: false); t.TextTrimming = TextTrimming.CharacterEllipsis; t.MaxWidth = 380;
            sp.Children.Add(t);
            _note.Children.Add(new Slab(Shaded(sp), InkC, .9, tone, 22, padX: 5, accentW: 3) { HorizontalAlignment = HorizontalAlignment.Left, Shadow = 2.5 });
            _note.Visibility = Visibility.Visible;
        }
        else _note.Visibility = Visibility.Collapsed;
    }

    /// <summary>The pickup strip's own plate: the room it adds round the tiles.</summary>
    const double StripPad = 4 + 32 * Tan + 2;

    /// <summary>The strip on its slanted navy plate.</summary>
    static Slab StripPlate(FrameworkElement strip) => new(strip, Ink2C, .95, null, 32, padX: 2) { VerticalAlignment = VerticalAlignment.Center };

    /// <summary>Your best here as the Buddy's stat tile: the share big ("91%"), a cyan bar along its foot to the share; mint with a star once beaten.</summary>
    Slab BestTile(Figures f)
    {
        FrameworkElement words;
        if (f.Beat)
        {
            // beaten: the cyan tile, NEW BEST over a navy star
            var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            var kick = Bc(L.T("NEW BEST"), 9, InkB); kick.HorizontalAlignment = HorizontalAlignment.Center;
            var star = Bc("★", 19, InkB); star.HorizontalAlignment = HorizontalAlignment.Center; star.Margin = new Thickness(0, -3, 0, -2);
            sp.Children.Add(kick); sp.Children.Add(star);
            words = sp;
        }
        else
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            sp.Children.Add(Bc(Pct(f.Share), 20, White));
            var pc = Bc("%", 11, Cyan); pc.VerticalAlignment = VerticalAlignment.Bottom; pc.Margin = new Thickness(1, 0, 0, 3); sp.Children.Add(pc);
            words = sp;
        }
        var t = new Slab(words, f.Beat ? CyanC : Ink2C, 1, null, 36, padX: 4) { MinWidth = 52, Margin = new Thickness(-LineLean / 2 - 4, -5, 10, -7), Rule = false };
        t.Height = double.NaN; t.VerticalAlignment = VerticalAlignment.Stretch;
        if (!f.Beat)
        {
            // the bar along its foot: a track, and the cyan bar scaled to the share (a moment runs it on by its scale)
            var track = new System.Windows.Shapes.Path { Fill = Frozen(Mod.A(Colors.White, .14)), IsHitTestVisible = false };
            var bar = new System.Windows.Shapes.Path { Fill = Cyan, IsHitTestVisible = false };
            var sc = new ScaleTransform(Math.Clamp(f.Share, 0, 1), 1);
            bar.RenderTransform = sc;
            _bestBar = sc;
            t.SizeChanged += (_, e) => { var g = FootStrip(e.NewSize, t.Lean, e.NewSize.Height - 4); track.Data = g; bar.Data = g; };
            t.Layers.Children.Insert(1, track); t.Layers.Children.Insert(2, bar);
        }
        return t;
    }

    /// <summary>The line under the hour: the session, the latest pickup and its worth (<paramref name="pick"/>); in the Abyss its stats.</summary>
    TextBlock Sub(Figures f, bool pick)
    {
        var sub = new TextBlock { FontFamily = Cond, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Frozen(Mod.A(Colors.White, .8)), Margin = new Thickness(1, 0, 0, 0) };
        System.Windows.Documents.Typography.SetNumeralAlignment(sub, FontNumeralAlignment.Tabular);
        void Dot() { if (sub.Inlines.Count > 0) sub.Inlines.Add(new System.Windows.Documents.Run("  /  ") { Foreground = Frozen(Mod.A(CyanC, .7)) }); }
        if (f.Total.Length > 0) { sub.Inlines.Add(new System.Windows.Documents.Run(f.Total) { FontWeight = FontWeights.Bold, Foreground = White }); sub.Inlines.Add(new System.Windows.Documents.Run(" " + f.TotalWord)); }
        foreach (var (fig, word, tone) in f.Stats)
        {
            Dot();
            var c = ReferenceEquals(tone, Rec) ? CoralC : ReferenceEquals(tone, Gold) ? GoldC2 : Colors.White;
            sub.Inlines.Add(new System.Windows.Documents.Run(fig) { FontWeight = FontWeights.Bold, Foreground = Frozen(c) });
            sub.Inlines.Add(new System.Windows.Documents.Run(" " + word));
        }
        if (pick && !f.Abyss && Picks(Values).FirstOrDefault() is { } p)
        {
            Dot();
            sub.Inlines.Add(new System.Windows.Documents.Run(p.Name.Length > 22 ? p.Name[..21].TrimEnd() + "…" : p.Name));
            var worth = p.Worth.Length > 0 ? "+" + p.Worth.TrimStart('+') : p.Count;
            if (worth.Length > 0) sub.Inlines.Add(new System.Windows.Documents.Run(" " + worth) { FontWeight = FontWeights.Bold, Foreground = p.Worth.Length > 0 ? Mint2 : Soft });
        }
        return sub;
    }

    /// <summary>The timer chips' room at the line's end, 0 with none.</summary>
    double LineTimerW() => _timers.Children.Count > 0 && !KinahHidden ? 10 + Mod.W(_timers) : 0;

    /// <summary>The timers' room the line's strip was fitted to (-1: no strip time); chips that change it draw the line again.</summary>
    double _stripTimerW = -1;

    /// <summary>The timer chips sit over the line, to leave its room to the pickup strip.</summary>
    bool _timerUp;

    Color _rareC;
    Slab? _rarePlate;
    FrameworkElement? _rareTile;

    /// <summary>The Buddy's plate: navy on the slant, a cyan bar under it (grey while paused), the cyan edge at its left.</summary>
    void DrawLinePlate(DrawingContext dc, Size s)
    {
        if (s.Width < 4 || s.Height < 4) return;
        var shape = Cut(new Rect(0, 0, s.Width, s.Height), LineLean);
        double a = Math.Min(1, .9 * Ground);
        // the layered plates: a cyan one peeking out behind, up and right; a hard shadow under
        dc.PushTransform(new TranslateTransform(6, -4));
        dc.DrawGeometry(Mod.Grad(false, (Mod.A(_dim ? GreyC : CyanC, .1), 0), (Mod.A(_dim ? GreyC : CyanC, .8), 1)), null, shape);
        dc.Pop();
        dc.PushTransform(new TranslateTransform(3, 3));
        dc.DrawGeometry(ShadowB, null, shape);
        dc.Pop();
        dc.DrawGeometry(Mod.Grad(true, (Mod.A(Color.FromRgb(0x12, 0x1A, 0x30), a), 0), (Mod.A(InkC, a), .6), (Mod.A(InkC, a), 1)), null, shape);
        var acc = _dim ? GreyC : CyanC;
        dc.DrawGeometry(GlossB, null, shape);
        dc.PushClip(shape);
        dc.DrawRectangle(Frozen(Mod.A(Colors.White, .14)), null, new Rect(0, 0, s.Width, 1));
        dc.DrawRectangle(Frozen(acc), null, new Rect(0, s.Height - 3, s.Width, 3));
        dc.Pop();
        if (_accentLeft) dc.DrawGeometry(Frozen(acc), null, Cut(new Rect(0, 0, 4.5 + LineLean, s.Height), LineLean));
    }

    protected override void KinahShown(bool shown)
    {
        _line.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
        if (!shown) { _note.Visibility = Visibility.Collapsed; _rare.Visibility = Visibility.Collapsed; }
        _fight.Margin = new Thickness(0, 0, 0, shown ? 8 : 0);
        PlaceTimers();
    }

    // ================================================================ the timers

    protected override void DrawTimers(IReadOnlyList<TimerBoard.Item> due, DateTime now)
    {
        _timers.Children.Clear();
        _firstChip = null;
        foreach (var i in due.Take(2))
        {
            var (text, tone) = TimerWords(i, now);
            bool first = _timers.Children.Count == 0;
            var c = tone == TimerBadge.Tone.On ? MintC2 : tone == TimerBadge.Tone.Soon ? GoldC2 : Color.FromRgb(0xC8, 0xD0, 0xE0);
            var t = Bc(text, first ? 13.5 : 12, first ? White : Soft, first ? FontWeights.Bold : FontWeights.SemiBold);
            if (tone == TimerBadge.Tone.On) t.Foreground = Frozen(Mod.Mix(MintC2, Colors.White, .4));
            var chip = new Slab(Shaded(t), InkC, .92, c, first ? 24 : 20, padX: first ? 6 : 5, accentW: first ? 4 : 3)
            {
                Margin = new Thickness(first ? 0 : 3 - (20 * Tan), 0, 0, 0), Tag = first ? "timer-chip" : null, Shadow = 2,
            };
            if (first) _firstChip = chip;
            _timers.Children.Add(chip);
        }
        // the line's pickup strip was fitted beside the timer chips: chips that come, go or change their width fit it again
        if (_stripTimerW >= 0 && Figs is { } f && Math.Abs(LineTimerW() - _stripTimerW) > .5) DrawLine(f);
        PlaceTimers();
    }

    void PlaceTimers()
    {
        bool inside = KinahHidden && _fight.Visibility == Visibility.Visible, up = !inside && _timerUp;
        if (inside)
        {
            if (_timersInLine.Child == _timers) _timersInLine.Child = null;
            if (_timersUp.Child == _timers) _timersUp.Child = null;
            if (_timersInFight.Child != _timers) _timersInFight.Child = _timers;
        }
        else if (up)
        {
            if (_timersInLine.Child == _timers) _timersInLine.Child = null;
            if (_timersInFight.Child == _timers) _timersInFight.Child = null;
            if (_timersUp.Child != _timers) _timersUp.Child = _timers;
        }
        else
        {
            if (_timersInFight.Child == _timers) _timersInFight.Child = null;
            if (_timersUp.Child == _timers) _timersUp.Child = null;
            if (_timersInLine.Child != _timers && _timersInLine.Parent != null) _timersInLine.Child = _timers;
        }
        bool any = _timers.Children.Count > 0;
        _timersInFight.Visibility = inside && any ? Visibility.Visible : Visibility.Collapsed;
        _timersUp.Visibility = up && any ? Visibility.Visible : Visibility.Collapsed;
        _timersInLine.Visibility = !inside && !up && any ? Visibility.Visible : Visibility.Collapsed;
    }

    // ================================================================ the fight

    const double HeadH = 26;

    protected override void DrawFight(Encounter? e, DateTime now)
    {
        _fightBody.Children.Clear();
        _headSlabs.Clear(); _footSlab = null; _hpLine = null;
        if (e == null) { _fight.Visibility = Visibility.Collapsed; PlaceTimers(); _leadFight = ""; return; }
        double w = FightWidth - 10;
        _fight.Width = w;
        _fightBody.Children.Add(_timersInFight);
        // the head: three plates in a row, the LIVE tag (the outcome once it's over, READY before the pull), the boss, the clock
        var head = new Grid { Height = HeadH };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); head.ColumnDefinitions.Add(new ColumnDefinition()); head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        double join = 3 - HeadH * Tan;   // the plates' gap on the slant
        Slab tag;
        var outcome = OutcomeText(e);
        if (e.Waiting) tag = Tag(L.T("READY"), CyanC, InkB, HeadH, 13.5);
        else if (outcome.Length > 0)
        {
            bool wipe = e.Outcome == "wipe";
            tag = Tag(outcome.ToUpper(CultureInfo.CurrentCulture), wipe ? LiveC : MintC2, wipe ? White : InkB, HeadH, 13.5);
            tag.Tag = "outcome";
        }
        else tag = Tag(L.T("LIVE"), LiveC, White, HeadH, 13.5, dot: true);
        tag.Shadow = 2.5;
        head.Children.Add(tag);
        var title = Bc(e.Title, 15, White, FontWeights.SemiBold, tight: false); title.TextTrimming = TextTrimming.CharacterEllipsis;
        var plate = new Slab(Shaded(title), InkC, .94, CyanC, HeadH, padX: 6) { Margin = new Thickness(join, 0, 0, 0), Shadow = 2.5, Back = CyanC };
        Grid.SetColumn(plate, 1); head.Children.Add(plate);
        _headSlabs.Add(tag); _headSlabs.Add(plate);
        if (!e.Waiting)
        {
            bool live = outcome.Length == 0;
            var clock = Bc(Clock(e, now), 19, live ? InkB : White); clock.Tag = "fight-clock";
            var ck = new Slab(clock, live ? CyanC : Ink2C, live ? 1 : .96, null, HeadH, padX: 6) { Margin = new Thickness(join, 0, 0, 0), Rule = !live, Shadow = 2.5 };
            Grid.SetColumn(ck, 2); head.Children.Add(ck);
            _headSlabs.Add(ck);
        }
        _head = head;
        _fightBody.Children.Add(head);
        if (e.Waiting)
        {
            var wt = Bc(e.ReadyText, 13, Soft, FontWeights.SemiBold, tight: false); wt.TextTrimming = TextTrimming.CharacterEllipsis;
            _fightBody.Children.Add(new Slab(Shaded(wt), InkC, .9, null, 22, padX: 6) { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 5, 0, 0), Shadow = 2 });
            if (PartyBeforeHit(e)) { _fightBody.Children.Add(new Border { Height = 6 }); PutRows(_fightBody, e, now, w); }
        }
        else
        {
            if (HpOf(e) is { } hp)
            {
                // the boss's HP: a slanted strip under the head, ticks at the thirds; the figure and what's left on a navy tab at its end
                var line = new Grid { Margin = new Thickness(0, 3, 0, 0), Height = 18 };
                line.ColumnDefinitions.Add(new ColumnDefinition()); line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var track = new Grid { Height = 8, VerticalAlignment = VerticalAlignment.Center };
                track.Children.Add(new TacSketch((dc, s) =>
                {
                    double l = s.Height * Tan * 2, x1 = s.Width * Math.Clamp(hp, 0, 1);
                    var shape = Cut(new Rect(0, 0, s.Width, s.Height), l);
                    dc.DrawGeometry(Frozen(Mod.A(InkC, .9 * Ground)), null, shape);
                    dc.PushClip(shape);
                    if (x1 > .5)
                    {
                        dc.DrawGeometry(Mod.Grad(false, (Mod.A(Mod.Mix(HpC, Colors.Black, .25), 1), 0), (HpC, 1)), null, Cut(new Rect(-l, 1.5, x1 + l, s.Height - 3), l));
                        dc.DrawGeometry(Frozen(Mod.Mix(HpC, Colors.White, .5)), null, Cut(new Rect(x1 - 2, 1.5, 2 + l, s.Height - 3), l));
                    }
                    for (int k = 1; k <= 2; k++) { double x = Math.Round(s.Width * k / 3); dc.DrawGeometry(Frozen(Mod.A(Colors.Black, .8)), null, Cut(new Rect(x - 1, 0, 2 + l, s.Height), l)); }
                    dc.Pop();
                }));
                HpGhost(track, e, hp, now, 0);
                line.Children.Add(track);
                var hl = new TextBlock { FontFamily = Cond, FontStretch = FontStretches.Condensed, FontSize = 12, Foreground = Soft, VerticalAlignment = VerticalAlignment.Center };
                System.Windows.Documents.Typography.SetNumeralAlignment(hl, FontNumeralAlignment.Tabular);
                hl.Inlines.Add(new System.Windows.Documents.Run($"{Math.Round(hp * 100):0}%") { FontWeight = FontWeights.Bold, Foreground = hp < .34 ? Frozen(Color.FromRgb(0xFF, 0x9A, 0xA8)) : White });
                var left = HpLeftText(e);
                if (left.Length > 0) hl.Inlines.Add(new System.Windows.Documents.Run("  " + left) { FontWeight = FontWeights.SemiBold });
                hl.Tag = "hp-text";
                var tab = new Slab(Shaded(hl), InkC, .9, null, 18, padX: 6) { Margin = new Thickness(3 - 8 * Tan * 2, 0, 0, 0), Rule = false };
                Grid.SetColumn(tab, 1); line.Children.Add(tab);
                _hpLine = line;
                _fightBody.Children.Add(line);
                _fightBody.Children.Add(new Border { Height = 5 });
            }
            else _fightBody.Children.Add(new Border { Height = 6 });
            PutRows(_fightBody, e, now, w);
            if (e.HasDamage) _fightBody.Children.Add(Foot(e, now));
        }
        _fight.Visibility = Visibility.Visible;
        PlaceTimers();
        // a new leader: the crown lands on their chip and the stinger crosses the plates
        long top = Meter?.Members.FirstOrDefault(m => m.Rank == 1 && !m.CpOnly)?.Id ?? 0;
        if (_leadFight == e.Id && top != 0 && _leadId != 0 && top != _leadId && e.Live && Moves) NewLeader(_leadId);
        _leadFight = e.Id; _leadId = top;
    }

    /// <summary>The rows as built now, top first (a moment walks them).</summary>
    List<(BcRow Row, int Index)> RowsNow()
    {
        var l = new List<(BcRow, int)>();
        if (Meter is not { } m) return l;
        foreach (var mm in m.Members)
            if (m.RowOf(mm.Id) is BcRow b) l.Add((b, l.Count));
        return l;
    }

    // ================================================================ the moments

    /// <summary>A pickup: the stinger crosses the Buddy in mint and the worth wipes in on a slanted chip beside it, then fades.</summary>
    protected override void Pickup(Pick p, bool held)
    {
        if (KinahHidden || _line.Visibility != Visibility.Visible) return;
        var worth = p.Worth.Length > 0 ? "+" + p.Worth.TrimStart('+') : p.Count;
        var words = new StackPanel();
        words.Children.Add(Bc(worth, 17, p.Worth.Length > 0 ? Frozen(Mod.Mix(MintC2, Colors.White, .3)) : White));
        var nm = Bc(p.Name.Length > 13 ? p.Name[..12].TrimEnd() + "…" : p.Name, 12.5, White, FontWeights.SemiBold); nm.Margin = new Thickness(0, -3, 0, 0);
        words.Children.Add(nm);
        var chip = new Slab(Shaded(words), InkC, .96, MintC2, 36, padX: 3, accentW: 3) { MaxWidth = 96, Shadow = 2.5 };
        Canvas.SetLeft(chip, -LineLean / 2 + 1); Canvas.SetTop(chip, -18);
        _lineAnchor.Children.Clear(); _lineAnchor.Children.Add(chip);
        Motion.Play(chip, 1900, t =>
        {
            _lineSweep.At(t < .45 ? Seg(t, 0, .45) : -1, MintC2);
            chip.Wipe = Out(Seg(t, .12, .32));
            chip.SweepAt(t is > .2 and < .5 ? Seg(t, .2, .5) : -1, MintC2);
            if (chip.Content != null) chip.Content.Opacity = Seg(t, .22, .34);
            chip.Opacity = t < .84 ? 1 : 1 - Seg(t, .84, 1);
        }, () => { _lineSweep.At(-1, MintC2); _lineAnchor.Children.Remove(chip); });
    }

    /// <summary>A rare drop: its plate wipes in on the slant in the grade's colour with a flash and the stinger, the tile popping in.</summary>
    protected override void RareDrop(Pick p, bool held)
    {
        if (_rarePlate is not { } plate) return;
        var tile = _rareTile;
        var sc = new ScaleTransform(1, 1);
        if (tile != null) { tile.RenderTransformOrigin = new Point(.5, .5); tile.RenderTransform = sc; }
        var c = _rareC;
        // the big one: the stinger crosses the whole Buddy in the grade's colour first, then the plate slams in behind it, a
        // touch big and settling (a pickup is a small mint chip at the side: this is the grade's colour across everything)
        var slam = new ScaleTransform(1, 1);
        var jolt = new TranslateTransform();
        plate.RenderTransformOrigin = new Point(0, .5);
        plate.RenderTransform = new TransformGroup { Children = { slam, jolt } };
        if (_line.Visibility == Visibility.Visible) Motion.Play(_lineSweep, 700, t => _lineSweep.At(t is > 0 and < 1 ? t : -1, c, 1), () => _lineSweep.At(-1, c));
        // shards of the grade's colour off the tile as it stamps down
        EsportsFx.Spray(plate, new Point(26, 18), 8, 30, -170, -10, new[] { c, Mod.Mix(c, Colors.White, .5), Colors.White }, 1000, fall: 10, delay: .26, sparks: true);
        Motion.Play(plate, 1500, t =>
        {
            slam.ScaleX = slam.ScaleY = t < .1 ? 1.12 : EsportsFx.Punch(Seg(t, .1, .4), 1.12, 2.4);
            jolt.X = EsportsFx.Shake(t, .26, .42, 2.5, 2.5);
            jolt.Y = -8 * (1 - Out(Seg(t, .06, .3)));
            plate.Wipe = Out(Seg(t, .06, .3));
            plate.FlashAt(t < .25 ? Seg(t, .1, .25) * .8 : (1 - Seg(t, .25, .8)) * .8, c);
            plate.SweepAt(t is > .25 and < .7 ? Seg(t, .25, .7) : -1, Mod.Mix(c, Colors.White, .3));
            if (plate.Content != null) plate.Content.Opacity = Seg(t, .12, .3);
            sc.ScaleX = sc.ScaleY = t < .18 ? 1.6 : EsportsFx.Punch(Seg(t, .18, .42), 1.6, 2.6);
            if (_rareTile is { } rt) rt.Opacity = Seg(t, .14, .2);
        }, () => { plate.Wipe = 1; plate.FlashAt(0, c); plate.SweepAt(-1); if (plate.Content != null) plate.Content.Opacity = 1; sc.ScaleX = sc.ScaleY = 1; slam.ScaleX = slam.ScaleY = 1; jolt.X = jolt.Y = 0; if (_rareTile is { } rt) rt.Opacity = 1; });
    }

    /// <summary>Your best passed: the tile's bar runs on to full, the tile flashes white to mint and its star punches in, a mint stinger crosses the bar.</summary>
    protected override void BestPassed(bool held)
    {
        if (_bestTile is not { } tile) return;
        double was = _share is > 0 and < 1 ? _share : .86;
        var bar = _bestBar;
        var star = tile.Content;
        var punch = new ScaleTransform(1, 1);
        if (star != null) { star.RenderTransformOrigin = new Point(.5, .5); star.RenderTransform = punch; }
        // the replay's lower third: a NEW BEST plate in mint wipes out beside the Buddy on the slant, the package's chevrons
        // after it, a white flash on landing; the tile punches and the whole Buddy takes a hard white frame (a pickup's chip is
        // navy with a mint edge: this is the plate lit)
        var nb = Bc(L.T("New best").ToUpper(System.Globalization.CultureInfo.CurrentCulture), 17, InkB);
        nb.MaxWidth = 78; nb.TextTrimming = TextTrimming.CharacterEllipsis;
        var plate = new Slab(nb, CyanC, 1, null, 28, padX: 4) { Rule = false, Shadow = 2.5, Back = MintC2 };
        var ps = new ScaleTransform(1, 1);
        plate.RenderTransformOrigin = new Point(0, .5);
        plate.RenderTransform = ps;
        Canvas.SetLeft(plate, -LineLean / 2 + 2); Canvas.SetTop(plate, -14);
        _lineAnchor.Children.Clear(); _lineAnchor.Children.Add(plate);
        var whole = new System.Windows.Shapes.Path { Fill = Frozen(Mod.A(Colors.White, .5)), IsHitTestVisible = false, Opacity = 0 };
        if (_line.ActualWidth > 4) whole.Data = Cut(new Rect(0, 0, _line.ActualWidth, _line.ActualHeight), LineLean);
        _line.Children.Add(whole);
        // cyan and white shards off the star as it punches
        EsportsFx.Spray(tile, new Point(tile.ActualWidth > 0 ? tile.ActualWidth / 2 : 24, tile.ActualHeight > 0 ? tile.ActualHeight / 2 : 24), 8, 24, -200, 20, new[] { CyanC, Colors.White, MintC2 }, 1000, fall: 8, delay: .3, sparks: true);
        Motion.Play(tile, 1800, t =>
        {
            if (bar != null) bar.ScaleX = was + (1 - was) * Out(Seg(t, 0, .3));
            tile.FlashAt(t < .3 ? 0 : t < .38 ? Seg(t, .3, .38) : 1 - Seg(t, .38, .85), Colors.White);
            punch.ScaleX = punch.ScaleY = t < .3 ? 1 - .1 * Seg(t, .18, .3) : EsportsFx.Punch(Seg(t, .3, .56), 1.45, 2.8);
            _lineSweep.At(t is > .3 and < .75 ? Seg(t, .3, .75) : -1, MintC2);
            whole.Opacity = t is >= .3 and < .4 ? .9 * (1 - Seg(t, .3, .4)) : 0;
            plate.Wipe = t < .84 ? Out(Seg(t, .3, .44)) : 1 - Seg(t, .84, .96);
            if (plate.Content != null) plate.Content.Opacity = t < .84 ? Seg(t, .38, .48) : 1 - Seg(t, .84, .9);
            ps.ScaleX = ps.ScaleY = t < .3 ? 1.15 : EsportsFx.Punch(Seg(t, .3, .6), 1.15, 2.4);
            plate.FlashAt(t is > .4 and < .62 ? .8 * (1 - Seg(t, .4, .62)) : 0, Colors.White);
            plate.SweepAt(t is > .48 and < .8 ? Seg(t, .48, .8) : -1, Colors.White);
        }, () =>
        {
            if (bar != null) bar.ScaleX = Math.Clamp(was, 0, 1); tile.FlashAt(0, Colors.White); punch.ScaleX = punch.ScaleY = 1; _lineSweep.At(-1, MintC2);
            _lineAnchor.Children.Remove(plate); _line.Children.Remove(whole);
        });
    }

    /// <summary>
    /// An entry opened, as a package's bumper: the first timer's chip wipes off and back in on its slant, landing a touch big
    /// with a mint flash, then the stinger's light crosses it and it settles.
    /// </summary>
    protected override void EntryOpened(TimerBoard.Item i, bool held)
    {
        if (_firstChip is not { } chip) return;
        var sc = new ScaleTransform(1, 1);
        chip.RenderTransformOrigin = new Point(0, .5);
        chip.RenderTransform = sc;
        var words = chip.Content;
        Motion.Play(chip, 1150, t =>
        {
            // out (a beat of nothing), wiped back in on the slant, the words after it
            chip.Wipe = t < .1 ? 1 - Out(Seg(t, 0, .1)) : Out(Seg(t, .16, .34));
            if (words != null) words.Opacity = t < .1 ? 1 - Seg(t, 0, .1) : Seg(t, .26, .38);
            sc.ScaleX = sc.ScaleY = t < .16 ? 1 : EsportsFx.Punch(Seg(t, .16, .56), 1.16, 2.6);
            chip.FlashAt(t < .3 ? 0 : (1 - Seg(t, .3, .62)) * .7, MintC2);
            chip.SweepAt(t is > .4 and < .85 ? Seg(t, .4, .85) : -1, MintC2);
        }, () => { chip.Wipe = 1; if (words != null) words.Opacity = 1; sc.ScaleX = sc.ScaleY = 1; chip.FlashAt(0, MintC2); chip.SweepAt(-1); });
    }

    /// <summary>
    /// A fight started: the branded stinger wipes across the whole meter (HudBroadcast.Stingers.cs); as its plates leave, the
    /// head's plates wipe in one after another, then each row's on their slant (a broadcast's build-in), the stinger's light
    /// crossing them as they land, the crown landing on the leader's chip, the foot last.
    /// </summary>
    protected override void FightStarted(Encounter e, bool held)
    {
        if (held && MomentHook == "leader") { LeaderTicker(); return; }
        var rows = RowsNow();
        var foot = _footSlab;
        var hpLine = _hpLine;
        var heads = _headSlabs.ToList();
        var o = OpenParts(e);
        Motion.Play(_fight, 1300, t =>
        {
            OpenStinger(t, o);
            for (int k = 0; k < heads.Count; k++)
            {
                var s = heads[k];
                s.Wipe = Out(Seg(t, .3 + k * .04, .44 + k * .04));
                if (s.Content != null) s.Content.Opacity = Seg(t, .36 + k * .04, .46 + k * .04);
                s.SweepAt(Band(t, .44 + k * .03, .22));
            }
            if (hpLine != null) hpLine.Opacity = Seg(t, .38, .48);
            foreach (var (r, i) in rows)
            {
                r.Enter = Out(Seg(t, .32 + i * .045, .5 + i * .045));
                r.SweepAt(Band(t, .44 + i * .04, .22), CyanC);
                if (r.Leader) r.Crown = Seg(t, .56, .8);
            }
            if (foot != null)
            {
                double at = .38 + rows.Count * .045;
                foot.Wipe = Out(Seg(t, at, at + .16));
                if (foot.Content != null) foot.Content.Opacity = Seg(t, at + .08, at + .18);
                foot.SweepAt(Band(t, at + .08, .2));
            }
        }, () =>
        {
            _fight.Children.Remove(o.Fx);
            foreach (var s in heads) { s.Wipe = 1; s.SweepAt(-1); if (s.Content != null) s.Content.Opacity = 1; }
            if (hpLine != null) hpLine.Opacity = 1;
            foreach (var (r, _) in rows) { r.Enter = 1; r.SweepAt(-1, CyanC); r.Crown = 1; }
            if (foot != null) { foot.Wipe = 1; foot.SweepAt(-1); if (foot.Content != null) foot.Content.Opacity = 1; }
        });
    }

    /// <summary>The stinger's place on a plate it reaches at <paramref name="at"/> of a course: 0 to 1 over the next <paramref name="span"/> (-1 outside).</summary>
    static double Band(double t, double at, double span = .32) => t >= at && t <= at + span ? (t - at) / span : -1;

    /// <summary>A new leader: the NEW LEADER ticker with their player card over the head, the crown landing, the stinger down the rows.</summary>
    void NewLeader(long was) => LeaderTicker(was);

    /// <summary>A fight ended (a kill, a wipe, a clear): the VICTORY (DEFEAT) stinger across the rows (HudBroadcast.Stingers.cs).</summary>
    protected override void FightEnded(Encounter e, bool held)
    {
        if (e.Outcome is not ("kill" or "wipe" or "cleared") || _head == null) return;
        VictoryStinger(e);
    }
}
