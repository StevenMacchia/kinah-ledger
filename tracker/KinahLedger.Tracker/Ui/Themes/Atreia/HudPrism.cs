using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using KinahLedger.Parser;
using KinahLedger.Session;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

/// <summary>
/// Prism (the Atreia theme; looks pack 2, 2026-11-20): cut crystal. Every panel is a slab of nearly neutral smoked glass with
/// its corners cut like a gem's: a bright bevel along its top and left edges, a faint table line inset from the rim, a lighter
/// facet falling from the top-left and a darker one at the bottom-right, and at the top-left cut a thin streak of spectrum
/// where the light enters. Each meter row is a clear glass bar (a touch of the class's colour in it, a class-coloured notch at
/// its left) whose leading edge splits the light: six hairlines, red to violet, fanning out from the edge, widest on the
/// leader's. The figures are pure white; names white with a tight dark shade. Rank 1 wears a small triangular prism that casts
/// a rainbow ray behind the leader's class art; it flashes in when someone takes the lead. A refraction sheen (a band of
/// spectrum light) sweeps down the bars on a fight's start, a kill and a new leader; a row that climbs catches a glint of it.
/// The Buddy is one crystal: the ring of your best drawn as a spectrum arc, the hour big in white, the pickups as tiles with a
/// faint spectral rim; a pickup's sheen crosses it, a rare drop's crystal blooms in its grade's colour.
/// Light by design (Steven, 2026-11-20: "cinematic ... lightweight - not bulky"): the crystals are drawn only when their
/// size changes; every moment moves art drawn once (the sheen's band, the crown's ray and bloom, the rare drop's colour,
/// the end plate) by its transform and opacity; no shade effect on anything on the crystal, which updates every tick.
/// </summary>
sealed partial class HudPrism : ModernLook
{
    static readonly Color MintC2 = Color.FromRgb(0x5D, 0xF2, 0xA6), MarkMintC = Color.FromRgb(0x7D, 0xFF, 0xC0),
        CoralC = Color.FromRgb(0xFF, 0x86, 0x86), HpC = Color.FromRgb(0xFF, 0x5E, 0x7A), DeepC = Color.FromRgb(0x08, 0x09, 0x0C), SmokeC = Color.FromRgb(0x17, 0x19, 0x1F),
        WarnC2 = Color.FromRgb(0xFF, 0xB0, 0x86), GoldC2 = Color.FromRgb(0xFF, 0xCF, 0x6A);
    static readonly SolidColorBrush White = Frozen(Colors.White), Soft = Frozen(Mod.A(Colors.White, .84)), Quiet = Frozen(Mod.A(Colors.White, .62)),
        Mint2 = Frozen(Color.FromRgb(0x8C, 0xFF, 0xC6)), MarkMint = Frozen(MarkMintC), Coral = Frozen(CoralC);

    /// <summary>The light split by the prism, red to violet.</summary>
    static readonly Color[] Spec =
    {
        Color.FromRgb(0xFF, 0x4F, 0x64), Color.FromRgb(0xFF, 0x9B, 0x3D), Color.FromRgb(0xFF, 0xE5, 0x4F),
        Color.FromRgb(0x5B, 0xF0, 0x8E), Color.FromRgb(0x4F, 0xAE, 0xFF), Color.FromRgb(0xAE, 0x78, 0xFF),
    };

    /// <summary>The cut of a panel's corners, and the meter panel's inner room.</summary>
    const double Cut = 7, PadX = 11;

    // the room round the look: the crystal's drop stays inside the window; 98 at the right for the pickup's chip beside the Buddy
    readonly Grid _root = new() { Margin = new Thickness(8, 8, 98, 10), HorizontalAlignment = HorizontalAlignment.Left };
    readonly StackPanel _body = new() { HorizontalAlignment = HorizontalAlignment.Left };
    double _opacity = 0.9;

    // the Buddy: its crystal, the ring, the hour, the session and the timer chips
    readonly Grid _line = new() { HorizontalAlignment = HorizontalAlignment.Left };
    readonly TacSketch _lineGlass;
    readonly StackPanel _lineBody = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(7, 7, 14, 7) };
    readonly Canvas _lineAnchor = Tac.Anchor(HorizontalAlignment.Right, VerticalAlignment.Center);
    readonly StackPanel _timers = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    readonly Border _timersInLine = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
    /// <summary>The timer chips over the line, when the pickup strip takes their room on it.</summary>
    readonly Border _timersUp = new() { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(2, 0, 0, 5), Visibility = Visibility.Collapsed };
    /// <summary>The pickup strip on the crystal: 24 px tiles beside the hour, a spectrum rule, a gold star.</summary>
    static readonly PickStrip.Look Strip = new(24, 4, true, SpecRule(), Frozen(Color.FromRgb(0xFF, 0xD7, 0x7A)));
    bool _dim;
    TacSketch? _ringArt;
    double _share = -1;
    bool _beat;
    /// <summary>The Buddy's sheen: the band drawn once, slid across the crystal by its transform (clipped to the crystal's cut outline).</summary>
    readonly Grid _lineFx = new() { IsHitTestVisible = false, ClipToBounds = true };
    readonly Rectangle _lineBand = new() { Width = 58, HorizontalAlignment = HorizontalAlignment.Left, Visibility = Visibility.Collapsed, IsHitTestVisible = false };
    readonly TranslateTransform _lineBandX = new();
    /// <summary>The ring's full spectrum, drawn once per ring, revealed round by a wedge clip as your best is passed.</summary>
    TacSketch? _ringFull;
    Border? _firstChip;

    // the rare drop under the Buddy, and the notice line
    readonly Grid _rare = new() { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 5, 0, 0), Visibility = Visibility.Collapsed };
    readonly Grid _noteHost = new() { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 5, 0, 0), Visibility = Visibility.Collapsed };
    readonly StackPanel _note = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 4, 12, 5) };

    // the fight: one crystal, its head, HP, rows and foot on it
    readonly Grid _fight = new() { HorizontalAlignment = HorizontalAlignment.Left, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 8) };
    readonly TacSketch _fightGlass;
    readonly StackPanel _fightBody = new() { Margin = new Thickness(PadX, 10, PadX, 10) };
    readonly Border _timersInFight = new() { Margin = new Thickness(0, 0, 0, 7), HorizontalAlignment = HorizontalAlignment.Left };
    readonly Grid _fightFx = new() { IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Top };
    /// <summary>The moments over the whole crystal: the fight's opening light (white in, split to the rows) and a kill's facets lighting in turn.</summary>
    readonly Grid _momentFx = new() { IsHitTestVisible = false };
    /// <summary>The clock of the sheen down the bars (its own element, so it never cuts another course short).</summary>
    readonly Border _sweepClock = new() { Width = 0, Height = 0, IsHitTestVisible = false };
    readonly TranslateTransform _fightShift = new();
    /// <summary>The fight crystal's punch (a kill's hit), under its shift.</summary>
    readonly ScaleTransform _fightPunch = new();
    /// <summary>The moments' layer over the whole look (AtreiaFx): each moment a stage of its own on it, clipped to the window.</summary>
    readonly Canvas _fx = new() { IsHitTestVisible = false };
    FrameworkElement? _head;

    public HudPrism() : base(bare: true)
    {
        // the look's own rows (Ui/Themes/ThemeRows.cs): each a clear glass bar splitting the light at its edge
        UseRows(PrismRow.Pitch, m => new PrismRow(this), 15, GapLine);
        _lineGlass = new TacSketch((dc, s) => Crystal(dc, s, _opacity, -1, Colors.White));
        _lineBand.RenderTransform = _lineBandX;
        _lineFx.Children.Add(_lineBand);
        _lineFx.SizeChanged += (_, e) => _lineFx.Clip = ShardShape(e.NewSize, Cut);
        // the words straight on the crystal's dark glass: no shade effect needed (or paid for) on it
        _line.Children.Add(_lineGlass); _line.Children.Add(_lineFx); _line.Children.Add(_lineBody); _line.Children.Add(_lineAnchor);
        _fightGlass = new TacSketch((dc, s) => Crystal(dc, s, _opacity, -1, Colors.White));
        _fight.Children.Add(_fightGlass); _fight.Children.Add(_fightBody); _fight.Children.Add(_fightFx); _fight.Children.Add(_momentFx); _fight.Children.Add(_sweepClock);
        _fight.RenderTransform = new TransformGroup { Children = { _fightPunch, _fightShift } };
        _noteHost.Children.Add(new TacSketch((dc, s) => Crystal(dc, s, _opacity, -1, Colors.White, cut: 5, glint: false)));
        _noteHost.Children.Add(_note);
        _runHeadLit = Lit(_runHead); _runHeadLit.Visibility = Visibility.Collapsed;
        _body.Children.Add(_fight); _body.Children.Add(Lit(_timersUp)); _body.Children.Add(_runHeadLit); _body.Children.Add(_line); _body.Children.Add(_rare); _body.Children.Add(_noteHost);
        _root.Children.Add(_body); _root.Children.Add(_fx);
        System.Windows.Documents.TextElement.SetFontFamily(_root, Mod.Face);
        TextOptions.SetTextFormattingMode(_root, TextFormattingMode.Display);
    }

    public override FrameworkElement Root => _root;
    public override Thickness Inset => _root.Margin;
    public override double Corner => 4;
    public override FrameworkElement? FightFrame => _fight.Visibility == Visibility.Visible ? _fight : null;

    public override void SetOpacity(double opacity)
    {
        // the smoke's depth takes the setting (never so clear the words lose their ground)
        _opacity = Math.Clamp(opacity, 0.3, 1);
        _lineGlass.InvalidateVisual(); _fightGlass.InvalidateVisual();
        foreach (var c in _noteHost.Children.OfType<TacSketch>()) c.InvalidateVisual();
    }

    protected override string LineSig(Figures f) => base.LineSig(f) + "|" + (Rare(Values)?.Id ?? "") + "|" + (Picks(Values).FirstOrDefault()?.Id ?? "") + "|" + Dimmed(Values) + "|" + PickStrip.Sig(Values, Strip) + "|" + RunBuddy.Sig(Values);

    static bool Dimmed(IReadOnlyDictionary<string, string> v) => (v.TryGetValue("show", out var s) && s == "dim") || (v.TryGetValue("paused", out var p) && p.Length > 0);

    // ================================================================ the shade and the crystal

    /// <summary>The tight shade under every glyph: 1 px down, 3 px soft, nearly black.</summary>
    static DropShadowEffect Shade() => new() { Color = Colors.Black, Direction = 270, ShadowDepth = 1, BlurRadius = 3, Opacity = .95 };

    /// <summary>The wide soft halo round a group of words straight on the game (off the crystal), so a bright scene behind them goes a shade darker.</summary>
    static DropShadowEffect Halo() => new() { Color = Colors.Black, ShadowDepth = 0, BlurRadius = 10, Opacity = .66 };

    /// <summary>Words on the crystal: the tight shade on them (one effect for the group).</summary>
    static T Shaded<T>(T el) where T : FrameworkElement { el.Effect = Shade(); return el; }

    /// <summary>Words straight on the game: the tight shade on them, the halo round them.</summary>
    static Border Lit(FrameworkElement el)
    {
        el.Effect = Shade();
        return new Border { Child = el, Effect = Halo(), HorizontalAlignment = el.HorizontalAlignment, VerticalAlignment = el.VerticalAlignment };
    }

    /// <summary>A colour along the spectrum, 0 red to 1 violet.</summary>
    static Color SpecAt(double t)
    {
        t = Math.Clamp(t, 0, 1) * (Spec.Length - 1);
        int i = Math.Min(Spec.Length - 2, (int)t);
        return Mod.Mix(Spec[i], Spec[i + 1], t - i);
    }

    /// <summary>The spectrum across a span (absolute points), at <paramref name="a"/>, faded out at both ends when <paramref name="fade"/>.</summary>
    static LinearGradientBrush SpecGrad(Point from, Point to, double a, bool fade = false)
    {
        var b = new LinearGradientBrush { MappingMode = BrushMappingMode.Absolute, StartPoint = from, EndPoint = to };
        if (fade) b.GradientStops.Add(new GradientStop(Mod.A(Spec[0], 0), 0));
        for (int i = 0; i < Spec.Length; i++) b.GradientStops.Add(new GradientStop(Mod.A(Spec[i], a), fade ? .12 + .76 * i / (Spec.Length - 1) : (double)i / (Spec.Length - 1)));
        if (fade) b.GradientStops.Add(new GradientStop(Mod.A(Spec[^1], 0), 1));
        b.Freeze();
        return b;
    }

    /// <summary>The pickup strip's rule: a short upright spectrum.</summary>
    static Brush SpecRule()
    {
        var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        for (int i = 0; i < Spec.Length; i++) b.GradientStops.Add(new GradientStop(Mod.A(Spec[i], .85), (double)i / (Spec.Length - 1)));
        b.Freeze();
        return b;
    }

    /// <summary>A rectangle with its corners cut by <paramref name="c"/> (a gem's outline), frozen.</summary>
    static Geometry Octa(Rect r, double c)
    {
        c = Math.Max(0, Math.Min(c, Math.Min(r.Width, r.Height) / 2));
        var g = new StreamGeometry();
        using (var x = g.Open())
        {
            x.BeginFigure(new Point(r.Left + c, r.Top), true, true);
            x.LineTo(new Point(r.Right - c, r.Top), true, false); x.LineTo(new Point(r.Right, r.Top + c), true, false);
            x.LineTo(new Point(r.Right, r.Bottom - c), true, false); x.LineTo(new Point(r.Right - c, r.Bottom), true, false);
            x.LineTo(new Point(r.Left + c, r.Bottom), true, false); x.LineTo(new Point(r.Left, r.Bottom - c), true, false);
            x.LineTo(new Point(r.Left, r.Top + c), true, false);
        }
        g.Freeze();
        return g;
    }

    static Geometry Poly(params Point[] p)
    {
        var g = new StreamGeometry();
        using (var x = g.Open()) { x.BeginFigure(p[0], true, true); for (int i = 1; i < p.Length; i++) x.LineTo(p[i], true, false); }
        g.Freeze();
        return g;
    }

    /// <summary>
    /// A crystal's outline and its table: a shard with its top-left and bottom-right corners cut deep (<paramref name="cut"/>
    /// twice over) and the other two cut shallow, and the same shape inset by the bevel. Vertices clockwise from the top
    /// edge's left end, so edge k runs from vertex k to k+1: 0 top, 1 the top-right cut, 2 right, 3 the bottom-right cut,
    /// 4 bottom, 5 the bottom-left cut, 6 left, 7 the top-left cut.
    /// </summary>
    static (Point[] O, Point[] I) ShardOf(Size s, double cut)
    {
        var r = new Rect(1, .5, Math.Max(0, s.Width - 2), Math.Max(0, s.Height - 3));
        double lim = Math.Min(r.Width, r.Height) / 2.4, big = Math.Min(cut * 2, lim), small = Math.Min(cut * .7, lim);
        double b = Math.Clamp(Math.Min(r.Width, r.Height) / 10, 2, 4.5);
        static Point[] Of(Rect q, double big, double small) => new[]
        {
            new Point(q.Left + big, q.Top), new Point(q.Right - small, q.Top), new Point(q.Right, q.Top + small), new Point(q.Right, q.Bottom - big),
            new Point(q.Right - big, q.Bottom), new Point(q.Left + small, q.Bottom), new Point(q.Left, q.Bottom - small), new Point(q.Left, q.Top + big),
        };
        var inner = new Rect(r.X + b, r.Y + b, Math.Max(0, r.Width - 2 * b), Math.Max(0, r.Height - 2 * b));
        return (Of(r, big, small), Of(inner, Math.Max(0, big - b * .6), Math.Max(0, small - b * .6)));
    }

    /// <summary>The outline of a crystal of this size (a clip for what moves on it).</summary>
    static Geometry ShardShape(Size s, double cut) => Poly(ShardOf(s, cut).O);

    static Point Lerp(Point a, Point b, double t) => new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);

    /// <summary>
    /// The crystal: a shard of nearly neutral smoked glass. A drop under it; the smoke; on its face (when
    /// <paramref name="glint"/>) the light refracted through it: three slanting pools of spectrum; the bevel's eight facets, each lit as the light from the top-left falls on it, the deep
    /// top-left cut carrying the light entering split into a full spectrum and the bottom-right cut the light leaving; the
    /// ridges between the facets, the table's rim, the outer rim and a sparkle where the light enters. Drawn when its size
    /// changes only. <paramref name="tint"/>: a wash of colour from the left at <paramref name="tintA"/> (a plate's tone).
    /// </summary>
    static void Crystal(DrawingContext dc, Size s, double opacity, double sheen, Color sheenC, double cut = Cut, bool glint = true, Color? tint = null, double tintA = 0, double tintW = 1, bool pools = true)
    {
        if (s.Width < 8 || s.Height < 8) return;
        double w = s.Width, h = s.Height;
        var (o, i) = ShardOf(s, cut);
        var outer = Poly(o); var table = Poly(i);
        // the drop: a dark edge just below and round it (inside its own room)
        dc.PushTransform(new TranslateTransform(0, 1.5));
        dc.DrawGeometry(null, Mod.P(Mod.Br(Colors.Black, .26), 2), outer);
        dc.Pop();
        double a = Math.Clamp(.66 + .3 * opacity, .74, .96);
        dc.DrawGeometry(Mod.Grad(true, (Mod.A(SmokeC, a), 0), (Mod.A(DeepC, Math.Min(.97, a + .04)), 1)), null, outer);
        dc.PushClip(table);
        if (tint is { } tc && tintA > 0 && tintW > 0)
            dc.DrawRectangle(Mod.Grad(false, (Mod.A(tc, tintA), 0), (Mod.A(tc, tintA * .45), .6), (Mod.A(tc, 0), 1)), null, new Rect(0, 0, w * Math.Clamp(tintW, 0, 1), h));
        if (glint && pools) Caustics(dc, w, h, o[0].X);
        dc.Pop();
        // the bevel's facets, lit from the top-left: the cuts facing the light bright, the bottom and its cuts in shade
        double[] lit = { .15, .2, .05, -.18, -.32, -.2, .1, .26 };
        for (int k = 0; k < 8; k++)
        {
            var quad = Poly(o[k], o[(k + 1) % 8], i[(k + 1) % 8], i[k]);
            dc.DrawGeometry(lit[k] > 0 ? Mod.Br(Colors.White, lit[k]) : Mod.Br(Colors.Black, -lit[k]), null, quad);
        }
        if (glint)
        {
            // the deep cuts split the light: the top-left one (where it enters) a full spectrum across the facet, the bottom-right fainter
            SpecFacet(dc, o[7], o[0], i[7], i[0], .62);
            SpecFacet(dc, o[3], o[4], i[3], i[4], .3);
        }
        // the ridges between the facets, the table's rim, the outer rim lit from the top-left
        for (int k = 0; k < 8; k++) dc.DrawLine(Mod.P(Mod.Br(Colors.White, k is 7 or 0 or 1 ? .42 : .16), .8), o[k], i[k]);
        dc.DrawGeometry(null, Mod.P(Mod.Br(Colors.White, .16), .8), table);
        var rim = new LinearGradientBrush { MappingMode = BrushMappingMode.Absolute, StartPoint = new Point(0, 0), EndPoint = new Point(Math.Min(w, 260), h) };
        rim.GradientStops.Add(new GradientStop(Mod.A(Colors.White, .75), 0)); rim.GradientStops.Add(new GradientStop(Mod.A(Colors.White, .24), .5)); rim.GradientStops.Add(new GradientStop(Mod.A(Colors.White, .14), 1));
        rim.Freeze();
        dc.DrawGeometry(null, new Pen(rim, 1), outer);
        dc.DrawLine(Mod.P(Mod.Br(Colors.White, .95), 1.2), o[7], o[0]);
        if (glint && w > 60) Sparkle(dc, o[0], 5.5);
    }

    /// <summary>A bevel facet split into six bands of spectrum, red at its outer edge to violet at the table's, at <paramref name="a"/>.</summary>
    static void SpecFacet(DrawingContext dc, Point o0, Point o1, Point i0, Point i1, double a)
    {
        for (int k = 0; k < Spec.Length; k++)
        {
            double t0 = (double)k / Spec.Length, t1 = (double)(k + 1) / Spec.Length;
            dc.DrawGeometry(Mod.Br(Spec[k], a), null, Poly(Lerp(o0, i0, t0), Lerp(o1, i1, t0), Lerp(o1, i1, t1), Lerp(o0, i0, t1)));
        }
    }

    /// <summary>A four-point sparkle of white light (where the light enters the crystal).</summary>
    static void Sparkle(DrawingContext dc, Point c, double r)
    {
        dc.DrawGeometry(Mod.Br(Colors.White, .95), null, Poly(new Point(c.X, c.Y - r), new Point(c.X + r * .18, c.Y - r * .18), new Point(c.X + r, c.Y), new Point(c.X + r * .18, c.Y + r * .18),
            new Point(c.X, c.Y + r), new Point(c.X - r * .18, c.Y + r * .18), new Point(c.X - r, c.Y), new Point(c.X - r * .18, c.Y - r * .18)));
    }

    /// <summary>
    /// The light refracted across a crystal's face, drawn once: two broad pools of spectrum slanting down from the top (the
    /// light entering at the top-left cut, spread) and a fainter one rising from the foot, faint enough that the words
    /// on the face read as clearly as on plain smoke.
    /// </summary>
    static void Caustics(DrawingContext dc, double w, double h, double x0)
    {
        void Pool(double xa, double xb, double xc, double xd, double alpha)
        {
            // a band from the top edge (xa..xb) to the bottom (xd..xc), its spectrum across its width
            double dx = (xc + xd) / 2 - (xa + xb) / 2, dy = h, len = Math.Sqrt(dx * dx + dy * dy), wid = (xb - xa) * dy / len;
            var n = new Vector(dy / len, -dx / len);
            var st = new Point(xa, 0);
            var b = new LinearGradientBrush { MappingMode = BrushMappingMode.Absolute, StartPoint = st, EndPoint = st + n * wid };
            b.GradientStops.Add(new GradientStop(Mod.A(Spec[0], 0), 0));
            for (int k = 0; k < Spec.Length; k++) b.GradientStops.Add(new GradientStop(Mod.A(Spec[k], alpha), .1 + .8 * k / (Spec.Length - 1)));
            b.GradientStops.Add(new GradientStop(Mod.A(Spec[^1], 0), 1));
            b.Freeze();
            dc.DrawGeometry(b, null, Poly(new Point(xa, 0), new Point(xb, 0), new Point(xc, h), new Point(xd, h)));
        }
        Pool(x0, x0 + w * .2, w * .58, w * .4, .1);
        Pool(w * .66, w * .74, w * .97, w * .86, .08);
        // a third pool rising from the foot towards the top-right: the light bouncing back off the bottom-right cut
        Pool(w * .3, w * .38, w * .2, w * .06, .06);
    }

    /// <summary>A small crystal chip (TOP DPS, the heal mark, AVG): smoke, a rim lit at the top; <paramref name="filled"/> white with dark words (YOU).</summary>
    static Border Mark(string s, Brush? fg = null, Color? edge = null, bool filled = false, double size = 8.5)
    {
        var t = Tt(s, size, filled ? Frozen(Color.FromRgb(0x0E, 0x10, 0x14)) : fg ?? White, FontWeights.Bold);
        return new Border
        {
            Child = t, CornerRadius = new CornerRadius(2), Padding = new Thickness(4, 0, 4, 1), VerticalAlignment = VerticalAlignment.Center,
            Background = filled ? Mod.Grad(true, (Colors.White, 0), (Color.FromRgb(0xDC, 0xE2, 0xEC), 1)) : Frozen(Mod.A(DeepC, .7)), BorderThickness = new Thickness(1),
            BorderBrush = filled ? White : Mod.Grad(true, (Mod.A(edge ?? Colors.White, edge == null ? .62 : .8), 0), (Mod.A(edge ?? Colors.White, edge == null ? .2 : .4), 1)),
        };
    }

    /// <summary>A hairline across the crystal (the foot's, the card's): white, fading at its ends.</summary>
    static FrameworkElement Hairline(Thickness m, double a = .24) => new TacSketch((dc, s) =>
        dc.DrawRectangle(Mod.Grad(false, (Mod.A(Colors.White, a * .15), 0), (Mod.A(Colors.White, a), .3), (Mod.A(Colors.White, a * .15), 1)), null, new Rect(0, 0, s.Width, 1))) { Height = 1, Margin = m };

    // ================================================================ the Buddy

    protected override void DrawLine(Figures f)
    {
        _lineBody.Children.Clear();
        _ringArt = null; _ringFull = null; _timerUp = false; _stripTimerW = -1;
        if (_timersInLine.Child != null) _timersInLine.Child = null;
        _dim = Dimmed(Values);
        _line.Opacity = _dim ? .62 : 1;
        bool mark = f.Abyss && f.Counted && f.Icon.Length > 0;
        if (mark) { var m = AbyssMark(f.Icon, 36); m.Margin = new Thickness(2, 0, 9, 0); _lineBody.Children.Add(m); }
        // in a dungeon run: the run's clock, splits and verdict in place of the hour (HudPrism.Run.cs)
        var rs = RunNow();
        _runHeadLit.Visibility = Visibility.Collapsed;
        if (rs != null) DrawRun(rs);
        else if (!HasHour(f))
        {
            // waiting, the figures there are under it (in the Abyss: "2 kills · Kinah"): information, never an empty plate
            var w = Tt(Waiting(f.Abyss), 12.5, Soft, FontWeights.SemiBold);
            var sub = Sub(f, false);
            var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(mark ? 0 : 7, sub.Inlines.Count > 0 ? 3 : 6, 0, sub.Inlines.Count > 0 ? 3 : 6) };
            words.Children.Add(w);
            if (sub.Inlines.Count > 0) words.Children.Add(sub);
            _lineBody.Children.Add(words);
        }
        else
        {
            if (HasRing(f))
            {
                _share = f.Share; _beat = f.Beat;
                var ring = new Grid { Width = 42, Height = 42, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
                _ringArt = new TacSketch(DrawRing, 42, 42);
                ring.Children.Add(_ringArt);
                _ringFull = new TacSketch((dc, s) => SpecArc(dc, new Point(21, 21), 21 - 3.5, 1, 3.2, false), 42, 42) { Visibility = Visibility.Collapsed };
                ring.Children.Add(_ringFull);
                var pc = Tt(f.Beat ? "★" : Pct(f.Share), 13, White, FontWeights.Bold); pc.HorizontalAlignment = HorizontalAlignment.Center;
                ring.Children.Add(pc);
                _lineBody.Children.Add(ring);
            }
            else if (!mark) _lineBody.Children.Add(new Border { Width = 6 });
            // the hour big, its unit small; the session and the latest pickup under it
            var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 1, 0, 1) };
            var top = new StackPanel { Orientation = Orientation.Horizontal };
            var rate = Tt(f.Rate, f.Warm ? 15 : 22, f.Warm ? Soft : White, f.Warm ? FontWeights.SemiBold : FontWeights.Bold);
            rate.Tag = "buddy-rate";
            top.Children.Add(rate);
            if (f.Unit.Length > 0) { var u = Tt(f.Unit, 11, Soft, FontWeights.SemiBold); u.VerticalAlignment = VerticalAlignment.Bottom; u.Margin = new Thickness(2, 0, 0, 4); top.Children.Add(u); }
            words.Children.Add(top);
            // farming, the pickups as the strip beside the hour where it fits (the best drop and the last three), the session
            // alone under it; with the timer chips at the line's end and no room left beside them, the chips go over the line
            var sub = Sub(f, true);
            FrameworkElement? strip = null, under = null;
            if (StripTime(f))
            {
                var bare = Sub(f, false);
                double baseW = _lineBody.Margin.Left + _lineBody.Margin.Right + _lineBody.Children.OfType<FrameworkElement>().Sum(Mod.W), timerW = LineTimerW();
                double wordsW = Math.Max(Mod.W(top), Mod.W(bare)), oldW = Math.Max(Mod.W(top), Mod.W(sub));
                foreach (var look in StripFall(Strip))
                {
                    if ((strip = StripIn(StripRoom(FightWidth - 10, baseW + timerW, wordsW, oldW, 12), look)) != null) break;
                    if (timerW > 0 && (strip = StripIn(StripRoom(FightWidth - 10, baseW, wordsW, oldW + timerW, 12), look)) != null) { _timerUp = true; break; }
                }
                if (strip != null) { sub = bare; strip.Margin = new Thickness(12, 0, 0, 0); Rims(strip); }
                // no room beside the hour: under the session, never dropped (Steven, 2026-11-19)
                else if ((under = StripUnder(Strip, false)) != null) { sub = bare; Rims(under); }
                _stripTimerW = timerW;
            }
            if (sub.Inlines.Count > 0) words.Children.Add(sub);
            if (under != null) words.Children.Add(under);
            _lineBody.Children.Add(words);
            if (strip != null) _lineBody.Children.Add(strip);
        }
        _lineBody.Children.Add(_timersInLine);
        PlaceTimers();

        // the rare drop the window holds: its own crystal washed in its grade's colour, its tile, RARE DROP and its name
        _rare.Children.Clear();
        if (!KinahHidden && Rare(Values) is { } r)
        {
            var c = ColorOf(r.Color, r.Grade);
            var glass = new TacSketch((dc, s) => Crystal(dc, s, _opacity, -1, c, cut: 5, glint: false));
            _rare.Children.Add(glass);
            // the grade's colour washed in from the left: a rectangle drawn once, bloomed by its scale and flashed by its opacity
            var tint = new Rectangle
            {
                Fill = Mod.Grad(false, (Mod.A(c, .88), 0), (Mod.A(c, .4), .6), (Mod.A(c, 0), 1)), Margin = new Thickness(2, 1.5, 2, 3.5), Opacity = .66,
                RenderTransform = new ScaleTransform(1, 1), IsHitTestVisible = false,
            };
            var fx = new Grid { ClipToBounds = true, IsHitTestVisible = false };
            var band = new Rectangle { Width = 58, Fill = Band, HorizontalAlignment = HorizontalAlignment.Left, Visibility = Visibility.Collapsed, RenderTransform = new TranslateTransform() };
            fx.Children.Add(band);
            _rare.Children.Add(tint); _rare.Children.Add(fx);
            _rareWash = tint; _rareBand = band;
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(11, 5, 16, 5) };
            var tile = Tile(r, 26); tile.Margin = new Thickness(0, 0, 8, 0);
            _rareTile = tile;
            row.Children.Add(tile);
            var mk = Mark(L.T("RARE DROP"), size: 8); mk.Margin = new Thickness(0, 0, 7, 0);
            row.Children.Add(mk);
            var nm = Tt(r.Name, 13, Frozen(Mod.Mix(c, Colors.White, .5)), FontWeights.Bold);
            row.Children.Add(nm);
            if (r.Count.Length > 0) { var cn = Tt(" " + r.Count, 11, Soft, FontWeights.SemiBold); cn.Margin = new Thickness(2, 1, 0, 0); row.Children.Add(cn); }
            _rare.Children.Add(Shaded(row));   // words over the grade's colour: the tight shade (one effect, a rare moment)
            _rare.Visibility = Visibility.Visible;
        }
        else { _rare.Visibility = Visibility.Collapsed; _rareWash = null; _rareBand = null; _rareTile = null; }

        // the notice: its words in their tone on a small crystal under the Buddy, the warning sign before a pause
        _note.Children.Clear();
        if (f.Note.Length > 0 && !KinahHidden)
        {
            bool warn = ReferenceEquals(f.NoteTone, Warn);
            if (warn) { var wi = WarnIcon(); wi.Width = wi.Height = 13; wi.Margin = new Thickness(0, 0, 5, 0); _note.Children.Add(wi); }
            var tone = warn ? WarnC2 : ReferenceEquals(f.NoteTone, Gold) ? GoldC2 : ReferenceEquals(f.NoteTone, Mint) ? MintC2 : Color.FromRgb(0xF2, 0xF4, 0xF8);
            var t = Tt(f.Note, 11.5, Frozen(tone), FontWeights.SemiBold); t.TextTrimming = TextTrimming.CharacterEllipsis; t.MaxWidth = 380;
            _note.Children.Add(t);
            _noteHost.Visibility = Visibility.Visible;
        }
        else _noteHost.Visibility = Visibility.Collapsed;
    }

    /// <summary>The pickup strip's tiles each given a faint spectral rim: a thin line of spectrum along the tile's top.</summary>
    static void Rims(FrameworkElement strip)
    {
        if (strip is not Panel p) return;
        foreach (var cell in p.Children.OfType<Grid>())
        {
            double size = Strip.Size;
            cell.Children.Add(new TacSketch((dc, s) =>
            {
                var g = SpecGrad(new Point(2, 0), new Point(size - 2, 0), .75, fade: true);
                dc.DrawRectangle(g, null, new Rect(3, .5, size - 6, 1));
                dc.DrawRectangle(SpecGrad(new Point(2, 0), new Point(size - 2, 0), .3, fade: true), null, new Rect(3, size - 1.5, size - 6, 1));
            }, size, size) { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center });
        }
    }

    /// <summary>The line under the hour: the session, the latest pickup and its worth (<paramref name="pick"/>); in the Abyss its stats.</summary>
    TextBlock Sub(Figures f, bool pick)
    {
        var sub = new TextBlock { FontFamily = Mod.Face, FontSize = 10.5, FontWeight = FontWeights.Medium, Foreground = Frozen(Mod.A(Colors.White, .86)), Margin = new Thickness(1, 1, 0, 0) };
        System.Windows.Documents.Typography.SetNumeralAlignment(sub, FontNumeralAlignment.Tabular);
        void Dot() { if (sub.Inlines.Count > 0) sub.Inlines.Add(new System.Windows.Documents.Run("  ·  ") { Foreground = Quiet }); }
        if (f.Total.Length > 0) { sub.Inlines.Add(new System.Windows.Documents.Run(f.Total) { FontWeight = FontWeights.SemiBold, Foreground = White }); sub.Inlines.Add(new System.Windows.Documents.Run(" " + f.TotalWord)); }
        foreach (var (fig, word, tone) in f.Stats)
        {
            Dot();
            var c = ReferenceEquals(tone, Rec) ? CoralC : ReferenceEquals(tone, Gold) ? GoldC2 : Colors.White;
            sub.Inlines.Add(new System.Windows.Documents.Run(fig) { FontWeight = FontWeights.SemiBold, Foreground = Frozen(c) });
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

    Rectangle? _rareWash, _rareBand;
    FrameworkElement? _rareTile;

    /// <summary>The ring of your best: a dark disc, a faint track, and your best's share as an arc of spectrum (red at the top, round to violet at full).</summary>
    void DrawRing(DrawingContext dc, Size s)
    {
        double c = s.Width / 2, r = c - 3.5;
        var center = new Point(c, c);
        dc.DrawEllipse(Frozen(Mod.A(DeepC, .55)), null, center, r + 2, r + 2);
        dc.DrawEllipse(null, Mod.P(Frozen(Mod.A(Colors.White, .16)), 3), center, r, r);
        dc.DrawEllipse(null, Mod.P(Frozen(Mod.A(Colors.White, .1)), .8), center, r - 3.2, r - 3.2);
        double shown = Math.Clamp(_beat ? 1 : _share, 0, 1);
        if (shown < .004) return;
        SpecArc(dc, center, r, Math.Min(shown, 1), 3.2, shown < .999);
        if (shown >= .999)
            // full: a white glint where the spectrum closes on itself
            dc.DrawEllipse(Frozen(Mod.A(Colors.White, .95)), null, new Point(c, c - r), 1.6, 1.6);
    }

    /// <summary>An arc of spectrum from the top clockwise to <paramref name="to"/> of a full turn, each part in its colour round the ring; round ends when open.</summary>
    static void SpecArc(DrawingContext dc, Point center, double r, double to, double width, bool caps)
    {
        int n = Math.Max(2, (int)Math.Ceiling(to * 48));
        Point At(double f) => new(center.X + r * Math.Sin(2 * Math.PI * f), center.Y - r * Math.Cos(2 * Math.PI * f));
        for (int k = 0; k < n; k++)
        {
            double f0 = to * k / n, f1 = Math.Min(to, to * (k + 1) / n + .004);
            var fig = new PathFigure { StartPoint = At(f0) };
            fig.Segments.Add(new ArcSegment(At(f1), new Size(r, r), 0, false, SweepDirection.Clockwise, true));
            var g = new PathGeometry(); g.Figures.Add(fig); g.Freeze();
            dc.DrawGeometry(null, new Pen(Frozen(SpecAt((f0 + f1) / 2)), width), g);
        }
        if (!caps) return;
        dc.DrawEllipse(Frozen(Spec[0]), null, At(0), width / 2, width / 2);
        dc.DrawEllipse(Frozen(SpecAt(to)), null, At(to), width / 2, width / 2);
    }

    protected override void KinahShown(bool shown)
    {
        _line.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
        if (!shown) { _noteHost.Visibility = Visibility.Collapsed; _rare.Visibility = Visibility.Collapsed; }
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
            var c = tone == TimerBadge.Tone.On ? MintC2 : tone == TimerBadge.Tone.Soon ? GoldC2 : Color.FromRgb(0xE6, 0xE9, 0xF0);
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            // a small cut gem in the tone's colour
            double d = first ? 7 : 6;
            sp.Children.Add(new TacSketch((dc, s) =>
            {
                double m = s.Width / 2;
                dc.DrawGeometry(Frozen(c), null, Poly(new Point(m, 0), new Point(s.Width, m), new Point(m, s.Height), new Point(0, m)));
                dc.DrawGeometry(Frozen(Mod.A(Colors.White, .55)), null, Poly(new Point(m, 0), new Point(s.Width, m), new Point(m, m)));
            }, d, d) { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
            var t = Tt(text, first ? 11.5 : 10, first ? White : Soft, first ? FontWeights.Bold : FontWeights.SemiBold);
            if (tone == TimerBadge.Tone.On) t.Foreground = Frozen(Mod.Mix(MintC2, Colors.White, .45));
            sp.Children.Add(t);
            var chip = new Border
            {
                Child = sp, CornerRadius = new CornerRadius(3), Padding = first ? new Thickness(7, 3, 9, 4) : new Thickness(6, 2, 8, 3), VerticalAlignment = VerticalAlignment.Center,
                Background = Mod.Grad(true, (Mod.A(SmokeC, .88), 0), (Mod.A(DeepC, .92), 1)), BorderThickness = new Thickness(1),
                BorderBrush = tone == TimerBadge.Tone.On ? Mod.Grad(true, (Mod.A(MintC2, .9), 0), (Mod.A(MintC2, .35), 1)) : Mod.Grad(true, (Mod.A(Colors.White, .55), 0), (Mod.A(Colors.White, .14), 1)),
                Margin = new Thickness(first ? 0 : 5, 0, 0, 0), Tag = first ? "timer-chip" : null,
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

    protected override void DrawFight(Encounter? e, DateTime now)
    {
        _fightBody.Children.Clear();
        if (e == null) { _fight.Visibility = Visibility.Collapsed; _fightFx.Children.Clear(); PlaceTimers(); return; }
        double w = FightWidth - 10;
        _fight.Width = w;
        double inner = w - 2 * PadX;
        _fightBody.Children.Add(_timersInFight);
        // the head: the boss, the outcome once it's over, the clock
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition()); head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = Tt(e.Title, 14, White, FontWeights.SemiBold); title.TextTrimming = TextTrimming.CharacterEllipsis;
        head.Children.Add(title);
        var right = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 0, 0) };
        if (e.Waiting) right.Children.Add(Mark(L.T("READY"), MarkMint, MarkMintC, size: 9.5));
        else
        {
            var outcome = OutcomeText(e);
            if (outcome.Length > 0)
            {
                bool wipe = e.Outcome == "wipe";
                var oc = wipe ? Color.FromRgb(0xFF, 0x9A, 0x9A) : MarkMintC;
                var st = Mark(outcome, Frozen(oc), oc, size: 10);
                st.Margin = new Thickness(0, 1, 8, 0); st.Tag = "outcome";
                right.Children.Add(st);
            }
            var clock = Tt(Clock(e, now), 18, White, FontWeights.Bold); clock.Tag = "fight-clock";
            right.Children.Add(clock);
        }
        Grid.SetColumn(right, 1); head.Children.Add(right);
        _head = head;
        _fightBody.Children.Add(_head);
        if (e.Waiting)
        {
            var wt = Tt(e.ReadyText, 12.5, Soft, FontWeights.SemiBold); wt.Margin = new Thickness(0, 5, 0, 1); wt.TextTrimming = TextTrimming.CharacterEllipsis;
            _fightBody.Children.Add(wt);
            if (PartyBeforeHit(e)) { _fightBody.Children.Add(new Border { Height = 7 }); PutRows(_fightBody, e, now, inner); }
        }
        else
        {
            if (HpOf(e) is { } hp)
            {
                // the boss's HP: a slim cut-glass line, rose with a light along its top, diamond notches at the thirds
                var track = new Grid { Height = 5, Margin = new Thickness(0, 7, 0, 0) };
                track.Children.Add(new TacSketch((dc, s) =>
                {
                    double y = .5, h = 4, x1 = s.Width * Math.Clamp(hp, 0, 1);
                    dc.DrawRectangle(Frozen(Mod.A(Colors.Black, .45)), null, new Rect(0, y, s.Width, h));
                    dc.DrawRectangle(Frozen(Mod.A(Colors.White, .14)), null, new Rect(0, y, s.Width, h));
                    if (x1 > .5)
                    {
                        dc.DrawRectangle(Mod.Grad(false, (Mod.Mix(HpC, Colors.Black, .1), 0), (Mod.Mix(HpC, Colors.White, .2), 1)), null, new Rect(0, y, x1, h));
                        dc.DrawRectangle(Frozen(Mod.A(Colors.White, .5)), null, new Rect(0, y, x1, 1));
                        dc.DrawRectangle(White, null, new Rect(Math.Max(0, x1 - 1.2), y, 1.2, h));
                    }
                    for (int k = 1; k <= 2; k++)
                    {
                        double x = Math.Round(s.Width * k / 3);
                        dc.DrawGeometry(Frozen(Mod.A(DeepC, .95)), null, Poly(new Point(x, y - .5), new Point(x + 2, y + h / 2), new Point(x, y + h + .5), new Point(x - 2, y + h / 2)));
                    }
                }));
                HpGhost(track, e, hp, now, 0);
                _fightBody.Children.Add(track);
                var hl = new TextBlock { FontFamily = Mod.Face, FontSize = 10.5, Foreground = Soft, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 2, 0, 0) };
                System.Windows.Documents.Typography.SetNumeralAlignment(hl, FontNumeralAlignment.Tabular);
                hl.Inlines.Add(new System.Windows.Documents.Run($"{Math.Round(hp * 100):0}%") { FontWeight = FontWeights.Bold, Foreground = hp < .34 ? Frozen(Color.FromRgb(0xFF, 0x9A, 0xA4)) : White });
                var left = HpLeftText(e);
                if (left.Length > 0) hl.Inlines.Add(new System.Windows.Documents.Run("  ·  " + left) { FontWeight = FontWeights.Medium });
                hl.Tag = "hp-text";
                _fightBody.Children.Add(hl);
                _fightBody.Children.Add(new Border { Height = 6 });   // the crown's prism rises into this room and the line's
            }
            else _fightBody.Children.Add(new Border { Height = 18 });
            PutRows(_fightBody, e, now, inner);
            if (e.HasDamage)
            {
                _fightBody.Children.Add(Hairline(new Thickness(0, 6, 0, 5)));
                _fightBody.Children.Add(Foot(e, now));
            }
        }
        _fight.Visibility = Visibility.Visible;
        PlaceTimers();
    }

    // ================================================================ the sheen and the crown

    /// <summary>
    /// The refraction sheen: a band of spectrum light sweeps along each bar, row by row down the meter (each a beat after the
    /// one above). One course of <paramref name="ms"/>, held back by <paramref name="leadMs"/>.
    /// </summary>
    void SweepAll(double ms = 1150, double leadMs = 0)
    {
        if (Meter is not { } m) return;
        var rows = m.Members.Select(x => m.RowOf(x.Id) as PrismRow).Where(r => r != null).Cast<PrismRow>().ToList();
        if (rows.Count == 0) return;
        double total = ms + leadMs;
        Motion.Play(_sweepClock, total, t =>
        {
            double q = Math.Max(0, (t * total - leadMs) / ms);
            for (int i = 0; i < rows.Count; i++) rows[i].SweepAt(q <= 0 ? -1 : Seg(q, i * .07, i * .07 + .68));
        }, () => { foreach (var r in rows) r.SweepAt(-1); });
    }

    /// <summary>A row took the lead (PrismRow tells it): its crown has flared in; the sheen sweeps the bars.</summary>
    internal void NewLeader() { if (Moves) SweepAll(); }

    /// <summary>The top row (the leader's), or null.</summary>
    PrismRow? TopRow() => Meter is { } m && m.Members.FirstOrDefault(x => x.Rank == 1 && !x.Pinned) is { } top ? m.RowOf(top.Id) as PrismRow : null;

    /// <summary>
    /// The fight's opening light: a beam of white light shoots in from the crystal's left edge to the crown's prism, and out
    /// of the prism the light splits into a ray to each bar's leading edge in that member's colour, fading as the bars take
    /// it (the sheen sweeps them). Drawn once (where the rows sit when it is drawn), moved by its scale and opacity.
    /// </summary>
    void OpeningLight(double ms)
    {
        var top = TopRow();
        if (top == null || Meter is not { } m) return;
        var rows = m.Members.Select(x => m.RowOf(x.Id) as PrismRow).Where(r => r != null).Cast<PrismRow>().ToList();
        var beamScale = new ScaleTransform(.001, 1);
        var beam = new TacSketch((dc, s) =>
        {
            var at = top.PrismIn(_momentFx);
            var b = new LinearGradientBrush { MappingMode = BrushMappingMode.Absolute, StartPoint = new Point(2, 0), EndPoint = at };
            b.GradientStops.Add(new GradientStop(Mod.A(Colors.White, .25), 0)); b.GradientStops.Add(new GradientStop(Mod.A(Colors.White, 1), 1)); b.Freeze();
            dc.DrawGeometry(b, null, Poly(new Point(2, at.Y - 5), at + new Vector(0, -1), at + new Vector(0, 1), new Point(2, at.Y + 5)));
            var glow = new RadialGradientBrush(Mod.A(Colors.White, .9), Mod.A(Colors.White, 0)); glow.Freeze();
            dc.DrawEllipse(glow, null, at, 9, 9);
        }) { RenderTransform = beamScale };
        // a ray to each bar, each its own part (drawn once), shot out of the prism a row after another; each ends in the
        // bar's first half, short of the figures, and is gone by a little over half a second
        // (built once the rows are laid out: a fight's start is told as it is first drawn, before its layout)
        List<(TacSketch El, ScaleTransform Grow)>? rays = null;
        void Build()
        {
        rays = new List<(TacSketch El, ScaleTransform Grow)>();
        var from0 = top.PrismIn(_momentFx) + new Vector(12, 0);
        double reach = Math.Max(60, _fight.ActualWidth * .46);
        foreach (var r in rows)
        {
            var to0 = r.EdgeAt(_momentFx);
            if (to0.X > reach) to0 = new Point(reach, to0.Y);
            var n0 = to0 - from0; if (n0.Length < 4) continue;
            var c = Mod.Mix(r.Tone, Colors.White, .2);
            var grow = new ScaleTransform(0, 0, from0.X, from0.Y);
            var ray = new TacSketch((dc, s) =>
            {
                var n = n0; n.Normalize(); var side = new Vector(-n.Y, n.X);
                // a soft halo round a bright core, white out of the prism into the member's colour
                var halo = new LinearGradientBrush { MappingMode = BrushMappingMode.Absolute, StartPoint = from0, EndPoint = to0 };
                halo.GradientStops.Add(new GradientStop(Mod.A(Colors.White, .3), 0)); halo.GradientStops.Add(new GradientStop(Mod.A(c, .4), 1)); halo.Freeze();
                dc.DrawGeometry(halo, null, Poly(from0 + side * 1.5, to0 + side * 4.5, to0 - side * 4.5, from0 - side * 1.5));
                var b = new LinearGradientBrush { MappingMode = BrushMappingMode.Absolute, StartPoint = from0, EndPoint = to0 };
                b.GradientStops.Add(new GradientStop(Mod.A(Colors.White, 1), 0)); b.GradientStops.Add(new GradientStop(Mod.A(Mod.Mix(c, Colors.White, .4), 1), .4)); b.GradientStops.Add(new GradientStop(Mod.A(c, .9), 1)); b.Freeze();
                dc.DrawGeometry(b, null, Poly(from0 + side * .7, to0 + side * 1.6, to0 - side * 1.6, from0 - side * .7));
                var spark = new RadialGradientBrush(Mod.A(Colors.White, .95), Mod.A(c, 0)); spark.Freeze();
                dc.DrawEllipse(spark, null, to0, 7, 7);
            }) { Opacity = 0, RenderTransform = grow };
            rays.Add((ray, grow));
        }
        foreach (var r in rays) _momentFx.Children.Insert(0, r.El);
        }
        _momentFx.Children.Clear();
        _momentFx.Children.Add(beam);
        bool Laid() => _momentFx.ActualWidth > 0 && rows.All(r => r.IsLaid);
        if (Laid()) Build();
        Motion.Play(_momentFx, ms, t =>
        {
            if (rays == null && Laid()) Build();
            // the crystal rises in fast: its words whole by 0.15 s
            _fightShift.Y = 8 * (1 - Out(Seg(t, 0, .16)));
            _fightBody.Opacity = Out(Seg(t, 0, .12));
            beamScale.ScaleX = Math.Max(.001, Out(Seg(t, 0, .14)));
            beam.Opacity = t < .18 ? 1 : 1 - Seg(t, .18, .32);
            for (int i = 0; i < (rays?.Count ?? 0); i++)
            {
                double a = .14 + .03 * i, k = Out(Seg(t, a, a + .09));
                rays![i].Grow.ScaleX = rays[i].Grow.ScaleY = Math.Max(.001, k);
                rays[i].El.Opacity = t < a ? 0 : t < a + .2 ? 1 : 1 - Seg(t, a + .2, a + .32);
            }
        }, () => { _momentFx.Children.Clear(); _fightShift.Y = 0; _fightBody.Opacity = 1; });
    }

    /// <summary>A kill: the crystal's eight bevel facets light up one after another round it, white flaring into each one's own colour of the spectrum.</summary>
    void FacetsLight(double ms)
    {
        var size = new Size(_fight.ActualWidth, _fight.ActualHeight);
        if (size.Width < 20 || size.Height < 20) return;
        var (o, i) = ShardOf(size, Cut);
        var lights = new List<System.Windows.Shapes.Path>();
        _momentFx.Children.Clear();
        for (int k = 0; k < 8; k++)
        {
            int e = (7 + k) % 8;   // from the top-left cut, round clockwise
            var c = Mod.Mix(Spec[k % Spec.Length], Colors.White, .45);
            var path = new System.Windows.Shapes.Path
            {
                Data = Poly(o[e], o[(e + 1) % 8], i[(e + 1) % 8], i[e]), Opacity = 0, IsHitTestVisible = false,
                Fill = Frozen(Mod.A(c, .95)), Stroke = Frozen(Mod.A(Colors.White, .9)), StrokeThickness = 2.2, StrokeLineJoin = PenLineJoin.Bevel,
            };
            lights.Add(path); _momentFx.Children.Add(path);
        }
        Motion.Play(_momentFx, ms, t =>
        {
            for (int k = 0; k < lights.Count; k++)
            {
                double a = Seg(t, k * .075, k * .075 + .14), b = Seg(t, k * .075 + .2, k * .075 + .5);
                lights[k].Opacity = a * (1 - b);
            }
        }, () => _momentFx.Children.Clear());
    }

    // ================================================================ the moments

    /// <summary>A pickup: a band of spectrum crosses the Buddy's crystal and the worth comes in beside it on a small crystal, then fades.</summary>
    protected override void Pickup(Pick p, bool held)
    {
        if (KinahHidden || _line.Visibility != Visibility.Visible) return;
        var worth = p.Worth.Length > 0 ? "+" + p.Worth.TrimStart('+') : p.Count;
        var words = new StackPanel { Margin = new Thickness(12, 4, 11, 5) };
        words.Children.Add(Tt(worth, 14, p.Worth.Length > 0 ? Frozen(Mod.Mix(MintC2, Colors.White, .25)) : White, FontWeights.Bold));
        var nm = Tt(p.Name.Length > 13 ? p.Name[..12].TrimEnd() + "…" : p.Name, 9.5, Soft, FontWeights.SemiBold); nm.Margin = new Thickness(0, -2, 0, 0);
        words.Children.Add(nm);
        var chip = new Grid { MaxWidth = 88 };
        chip.Children.Add(new TacSketch((dc, s) => Crystal(dc, s, _opacity, -1, Colors.White, cut: 5, glint: false)));
        chip.Children.Add(words);
        var shift = new TranslateTransform();
        chip.RenderTransform = shift;
        chip.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(chip, 6); Canvas.SetTop(chip, -chip.DesiredSize.Height / 2);
        _lineAnchor.Children.Clear(); _lineAnchor.Children.Add(chip);
        _lineBand.Fill = Glint;
        var pop = new ScaleTransform(1, 1); chip.RenderTransform = new TransformGroup { Children = { pop, shift } };
        // the newest tile in the strip pops (1.15 and back in 0.22 s): the look's own copy of the strip, only scaled
        var slot = AtreiaFx.Pop(AtreiaFx.NewestTile(_lineBody), 0, .116, .2, middle: true);
        Motion.Play(chip, 1900, t =>
        {
            slot(t);
            LineSheen(t < .4 ? Seg(t, 0, .4) : -1);
            double k = Back(Seg(t, .06, .24), 3);
            shift.X = -14 * (1 - k);
            pop.ScaleX = pop.ScaleY = AtreiaFx.Punch(t, .06, .4, .0, .05);
            chip.Opacity = t < .82 ? k : 1 - Seg(t, .82, 1);
        }, () => { LineSheen(-1); _lineAnchor.Children.Remove(chip); _lineBand.Fill = Band; });
    }

    /// <summary>A pickup's glint: a plain band of white light (the spectrum's band is the rare drop's and the fight's).</summary>
    static readonly Brush Glint = MakeGlint();
    static Brush MakeGlint()
    {
        var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        // two narrow bands, the first brighter: a highlight sliding over glass
        foreach (var (at, a) in new[] { (0.0, 0.0), (.2, 0.0), (.3, .72), (.4, 0.0), (.58, 0.0), (.66, .5), (.74, 0.0), (1.0, 0.0) })
            b.GradientStops.Add(new GradientStop(Mod.A(Colors.White, a), at));
        b.Freeze();
        return b;
    }

    /// <summary>
    /// A rare drop (the big one, in its grade's colour; a pickup is only a sheen and a chip): a pillar of white light falls
    /// onto the tile and splits there, the tile ducks and punches up, a burst of rays (the grade's colour and the spectrum)
    /// and a ring of light go out from it, cut chips of the gem fly off and fall, and the grade's colour blooms out across its
    /// crystal with a sheen. One course; every part drawn once, then moved and faded.
    /// </summary>
    protected override void RareDrop(Pick p, bool held)
    {
        if (_rareWash is not { } wash || wash.RenderTransform is not ScaleTransform bloom) return;
        var tile = _rareTile;
        var band = _rareBand;
        var bx = band?.RenderTransform as TranslateTransform;
        var sc = new ScaleTransform(1, 1);
        if (tile != null) { tile.RenderTransformOrigin = new Point(.5, .5); tile.RenderTransform = sc; }
        if (band != null) band.Visibility = Visibility.Visible;
        var c = ColorOf(p.Color, p.Grade);
        var st = AtreiaFx.Stage(_fx, _root);
        var parts = new List<Action<double>>();
        if (AtreiaFx.At(tile, _fx, new Point(13, 13)) is { } at)
        {
            parts.Add(AtreiaFx.Pillar(st, at + new Vector(0, 6), 38, 84, c, 0, .16, .58));
            parts.Add(AtreiaFx.Bloom(st, at, 96, 58, AtreiaFx.Glow(c, .85, Colors.White), .13, .19, .6));
            parts.Add(AtreiaFx.Rays(st, at, 12, 11, 42, 3.4, i => i % 2 == 0 ? Mod.Mix(c, Colors.White, .45) : SpecAt(i / 11.0), .14, .22, .72, turn: 28, max: .9));
            // the pillar's spectrum fringes, red to its left, violet to its right
            parts.Add(AtreiaFx.Pillar(st, at + new Vector(-12, 6), 10, 78, Spec[0], .02, .17, .5));
            parts.Add(AtreiaFx.Pillar(st, at + new Vector(12, 6), 10, 78, Spec[5], .02, .17, .5));
            var bits = new AtreiaFx.Bits(st);
            bits.Burst(10, AtreiaFx.Diamond, i => AtreiaFx.Br(i % 3 == 0 ? Colors.White : i % 3 == 1 ? Mod.Mix(c, Colors.White, .2) : SpecAt(i / 9.0)), at + new Vector(6, 0), 36, 4.6, .16, .7,
                fall: 26, dir: -30, spread: 120, r0: 8, spin: 220, seed: 23);
            parts.Add(bits.At);
        }
        AtreiaFx.Play(_fx, st, 1700, t =>
        {
            AtreiaFx.All(parts, t);
            bloom.ScaleX = Math.Max(.001, Out(Seg(t, .15, .5)));
            wash.Opacity = .66 + .34 * AtreiaFx.Flash(t, .14, .22, .85);
            sc.ScaleX = sc.ScaleY = AtreiaFx.Punch(t, .06, .62, .2, .25);
            double sh = Seg(t, .2, .7);
            if (band != null && bx != null) { bx.X = -60 + (_rare.ActualWidth + 120) * sh; band.Opacity = Math.Sin(Math.PI * sh); }
        }, () => { bloom.ScaleX = 1; wash.Opacity = .66; sc.ScaleX = sc.ScaleY = 1; if (band != null) band.Visibility = Visibility.Collapsed; });
    }

    /// <summary>The Buddy's sheen at <paramref name="p"/> of its crossing (outside 0 to 1: none): the band's transform and opacity only.</summary>
    void LineSheen(double p)
    {
        bool on = p is > 0 and < 1;
        if (on != (_lineBand.Visibility == Visibility.Visible)) _lineBand.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        if (!on) return;
        _lineBandX.X = -64 + (_line.ActualWidth + 128) * p;
        _lineBand.Opacity = Math.Sin(Math.PI * p);
    }

    /// <summary>Your best passed: the ring's spectrum runs on round to full and a mint-cored sheen crosses the crystal.</summary>
    protected override void BestPassed(bool held)
    {
        if (_ringArt is not { } art || _ringFull is not { } full) return;
        double was = _share is > 0 and < 1 ? _share : .86;
        bool beat = _beat;
        // the ring under it as it was (your old share); the full spectrum over it, revealed round by a wedge
        _beat = false; _share = was; art.InvalidateVisual();
        full.Visibility = Visibility.Visible;
        _lineBand.Fill = BandMint;
        // the bloom as the spectrum closes: the ring ducks and punches out, a crown of spectrum rays turns out of it, a ring of
        // white light bursts, cut chips of every colour fly off it (to the right: the window's edge is close on the left)
        var st = AtreiaFx.Stage(_fx, _root);
        var parts = new List<Action<double>>();
        ScaleTransform? pop = null;
        if (art.Parent is FrameworkElement ring && AtreiaFx.At(ring, _fx, new Point(21, 21)) is { } c)
        {
            pop = AtreiaFx.ScaleOn(ring, new Point(21, 21));
            parts.Add(AtreiaFx.Bloom(st, c, 74, 74, AtreiaFx.Glow(Colors.White, .75), .3, .36, .66));
            parts.Add(AtreiaFx.Rays(st, c, 14, 22, 34, 4.2, i => SpecAt(i / 13.0), .3, .38, .8, turn: 40, max: 1));
            var bits = new AtreiaFx.Bits(st);
            bits.Burst(10, AtreiaFx.Diamond, i => AtreiaFx.Br(i % 4 == 0 ? Colors.White : SpecAt(i / 9.0)), c, 26, 4.6, .32, .55, fall: 12, dir: 0, spread: 140, r0: 20, spin: 220, seed: 5);
            parts.Add(bits.At);
        }
        parts.Add(AtreiaFx.Pop(AtreiaFx.Hour(_lineBody), .3, .46, .08));
        AtreiaFx.Play(_fx, st, 1800, t =>
        {
            double shown = was + (1 - was) * Out(Seg(t, 0, .3));
            full.Clip = shown >= .999 ? null : Wedge(new Point(21, 21), 24, shown);
            LineSheen(t is > .28 and < .62 ? Seg(t, .28, .62) : -1);
            if (pop != null) pop.ScaleX = pop.ScaleY = AtreiaFx.Punch(t, 0, .92, .08, .24);
            AtreiaFx.All(parts, t);
        }, () => { _beat = beat; full.Visibility = Visibility.Collapsed; full.Clip = null; art.InvalidateVisual(); LineSheen(-1); _lineBand.Fill = Band; if (pop != null) pop.ScaleX = pop.ScaleY = 1; });
    }

    /// <summary>A wedge from the top clockwise over <paramref name="part"/> of a turn (a ring's reveal), frozen.</summary>
    static Geometry Wedge(Point c, double r, double part)
    {
        double a = 2 * Math.PI * Math.Clamp(part, 0, 1);
        var g = new StreamGeometry();
        using (var x = g.Open())
        {
            x.BeginFigure(c, true, true);
            x.LineTo(new Point(c.X, c.Y - r), false, false);
            x.ArcTo(new Point(c.X + r * Math.Sin(a), c.Y - r * Math.Cos(a)), new Size(r, r), 0, a > Math.PI, SweepDirection.Clockwise, false, false);
        }
        g.Freeze();
        return g;
    }

    /// <summary>
    /// An entry opened: the chips duck and punch out (the timer row, which outlives the chip redrawn each second), a mint
    /// glow flashes on the chip, a band of spectrum crosses it, a mint and a white ring ripple off its edge and cut chips of
    /// spectrum spring off its ends; then it rests.
    /// </summary>
    protected override void EntryOpened(TimerBoard.Item i, bool held)
    {
        if (_firstChip is not { } chip || AtreiaFx.Box(chip, _fx) is not { } r) return;
        var st = AtreiaFx.Stage(_fx, _root);
        var pop = AtreiaFx.ScaleOn(_timers, chip.TranslatePoint(new Point(chip.ActualWidth / 2, chip.ActualHeight / 2), _timers));
        var c = new Point(r.X + r.Width / 2, r.Y + r.Height / 2);
        var parts = new List<Action<double>>
        {
            AtreiaFx.Bloom(st, c, r.Width + 34, r.Height + 26, AtreiaFx.Glow(MintC2, .7), .12, .2, .6),
            AtreiaFx.Ripple(st, r, 3, AtreiaFx.Br(Mod.Mix(MintC2, Colors.White, .3)), 1.8, 6, .14, .45),
        };
        // the band of spectrum across the chip (clipped to it)
        var lane = new Canvas { Width = r.Width, Height = r.Height, ClipToBounds = true, IsHitTestVisible = false };
        Canvas.SetLeft(lane, r.X); Canvas.SetTop(lane, r.Y);
        var bandX = new TranslateTransform(-40, 0);
        lane.Children.Add(new Rectangle { Width = 34, Height = r.Height, Fill = Band, RenderTransform = bandX, IsHitTestVisible = false });
        st.Children.Add(lane);
        parts.Add(t => { double u = Seg(t, .16, .6); bandX.X = -40 + (r.Width + 50) * Out(u); lane.Opacity = u is > 0 and < 1 ? 1 : 0; });
        var bits = new AtreiaFx.Bits(st);
        bits.Burst(8, AtreiaFx.Diamond, k => AtreiaFx.Br(SpecAt(k / 7.0)), c, 18, 5, .15, .62, fall: 6, r0: r.Width / 2, spin: 160, seed: 11, squash: .42);
        parts.Add(bits.At);
        AtreiaFx.Play(_fx, st, 1150, t =>
        {
            pop.ScaleX = pop.ScaleY = AtreiaFx.Punch(t, 0, .72, .06, .08);
            AtreiaFx.All(parts, t);
        }, () => pop.ScaleX = pop.ScaleY = 1);
    }

    /// <summary>A fight started: the crystal rises a few pixels as it fades up, white light enters the crown's prism and splits into the rows' colours, the sheen sweeps the bars.</summary>
    protected override void FightStarted(Encounter e, bool held)
    {
        // the opening light, one course: the crystal rises in (its words whole by 0.15 s), white light shoots to the crown's
        // prism and splits into a ray to each bar, row after row, gone by about 0.6 s; the sheen sweeps the bars
        if (TopRow() == null)
            Motion.Play(_fight, 360, t => { double k = Out(t); _fightShift.Y = 8 * (1 - k); _fightBody.Opacity = k; }, () => { _fightShift.Y = 0; _fightBody.Opacity = 1; });
        OpeningLight(1300);
        TopRow()?.Crown(leadMs: 200);
        SweepAll(1150, 300);
    }

    /// <summary>A fight ended: a crystal plate tinted mint (coral for a wipe) lays over the head with the outcome and the clock, then lifts; a kill lights the crystal's facets in turn round it and sweeps the bars.</summary>
    protected override void FightEnded(Encounter e, bool held)
    {
        if (e.Outcome is not ("kill" or "wipe" or "cleared")) return;
        bool wipe = e.Outcome == "wipe";
        var c = wipe ? CoralC : MintC2;
        var band = new Grid { Height = 32, Margin = new Thickness(-3, -5, -3, 0) };
        // the plate drawn once, laid on by its scale from the left
        var lay = new ScaleTransform(.001, 1);
        var plate = new TacSketch((dc, s) => Crystal(dc, s, 1, -1, Colors.White, cut: 6, tint: c, tintA: .42)) { RenderTransform = lay };
        band.Children.Add(plate);
        var words = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        words.Children.Add(Tt(OutcomeText(e), 18, White, FontWeights.Bold));
        var ck = Tt("  " + Clock(e.Duration(e.End ?? DateTime.UtcNow)), 12, Frozen(Mod.Mix(c, Colors.White, .5)), FontWeights.SemiBold); ck.Margin = new Thickness(4, 3, 0, 0);
        words.Children.Add(ck);
        Shaded(words);   // words over the tinted plate: the tight shade (one effect, a moment)
        band.Children.Add(words);
        _fightFx.Margin = new Thickness(PadX, 8, PadX, 0);
        _fightFx.Children.Clear(); _fightFx.Children.Add(band);
        var head = _head;
        // the moment over the whole crystal, on its own stage: a kill shatters light out of the head (a white flash, a crown
        // of spectrum rays, a ring of light, splinters of every colour) and the crystal punches; a wipe cracks it: a coral
        // crack runs across its head, the crystal shakes, its edges go dark and grey splinters fall out of the crack
        var st = AtreiaFx.Stage(_fx, _root);
        var parts = new List<Action<double>>();
        var fb = AtreiaFx.Box(_fight, _fx);
        double hy = head is { ActualHeight: > 0 } && head.IsDescendantOf(_fight) ? head.TranslatePoint(new Point(0, head.ActualHeight / 2), _fight).Y : 22;
        var pc = new Point(_fight.ActualWidth / 2, hy);
        if (fb is { } b)
        {
            var hit = new Point(b.X + pc.X, b.Y + hy);
            var bits = new AtreiaFx.Bits(st);
            if (!wipe)
            {
                // the hit: a hot white core and a flare line across the head, the light splitting into a crown of spectrum
                // rays, a soft ring of light running out, splinters of every colour
                parts.Add(AtreiaFx.Bloom(st, hit, 150, 90, AtreiaFx.Glow(Colors.White, 1), 0, .05, .34, s0: .3));
                parts.Add(AtreiaFx.Streak(st, hit, b.Width * 1.05, 7, Color.FromRgb(0xB8, 0xE4, 0xFF), 0, .06, .4));
                parts.Add(AtreiaFx.Rays(st, hit, 18, 8, b.Width * .46, 4.5, i => SpecAt(i / 17.0), .02, .08, .5, turn: 14, max: .62));
                bits.Burst(8, AtreiaFx.Diamond, i => AtreiaFx.Br(i % 4 == 0 ? Colors.White : SpecAt(i / 7.0)), hit, b.Width * .34, 8, .04, .42, fall: 16, r0: 12, spin: 320, seed: 3, squash: .5);
            }
            else
            {
                // struck glass: a star of cracks out of the crystal's head, its edges going dark, grey splinters falling out
                var at = new Point(b.X + b.Width * .58, b.Y + hy + 8);
                parts.Add(AtreiaFx.Veil(st, new Rect(b.X + 1, b.Y + 1, b.Width - 2, b.Height - 4), Color.FromRgb(0x12, 0x04, 0x08), .75, .02, .14, .62));
                parts.Add(AtreiaFx.Bloom(st, at, 70, 44, AtreiaFx.Glow(CoralC, .9, Colors.White), 0, .03, .22, s0: .6));
                // six straight cracks out of one point, at once, gone by t .26; three dark chips of the glass drop out of it
                parts.Add(AtreiaFx.Shatter(st, at, 6, 66, CoralC, 4, .01, .05, .42, squash: .55, wobble: .06, segs: 2, ring: false));
                for (int k = 0; k < 3; k++)
                {
                    var o = at + new Vector((k - 1) * 14, 2 + 3 * (k % 2));
                    bits.Add(AtreiaFx.Shard, AtreiaFx.Br(Color.FromRgb(0x2A, 0x2C, 0x34), .95), o, new Vector((k - 1) * 3, 0), fall: 14,
                        rot0: 70 + 25 * k, spin: (k - 1) * 40, s0: 6, s1: 5, delay: .05 + .03 * k, life: .36);
                }
            }
            parts.Add(bits.At);
        }
        _fightPunch.CenterX = pc.X; _fightPunch.CenterY = pc.Y;
        // the course in the plate's 2.4 s; the light's parts play in its first 1.5 s
        AtreiaFx.Play(_fx, st, 2400, t =>
        {
            double k = Math.Min(1, t * 2400 / 1500);
            AtreiaFx.All(parts, k);
            if (wipe) _fightShift.X = AtreiaFx.Shake(k, .02, .48, 5, 3.5);
            else _fightPunch.ScaleX = _fightPunch.ScaleY = AtreiaFx.Punch(k, 0, .5, .012, .026);
            lay.ScaleX = Math.Max(.001, Out(Seg(t, 0, .22)));
            // the plate's words and the head never show together (their words would sit over each other)
            words.Opacity = t < .82 ? Seg(t, .12, .24) : 1 - Seg(t, .82, .9);
            band.Opacity = t < .82 ? 1 : 1 - Seg(t, .82, .92);
            if (head != null) head.Opacity = t < .9 ? 1 - Seg(t, 0, .12) : Seg(t, .9, 1);
        }, () => { _fightFx.Children.Clear(); if (head != null) head.Opacity = 1; _fightShift.X = 0; _fightPunch.ScaleX = _fightPunch.ScaleY = 1; });
        if (!wipe) { FacetsLight(1100); SweepAll(1300); }
    }
}
