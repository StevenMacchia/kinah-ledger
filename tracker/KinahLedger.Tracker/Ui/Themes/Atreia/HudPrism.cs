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
    const double Cut = 7, PadX = 9;

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
    readonly StackPanel _note = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(11, 4, 12, 5) };

    // the fight: one crystal, its head, HP, rows and foot on it
    readonly Grid _fight = new() { HorizontalAlignment = HorizontalAlignment.Left, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 8) };
    readonly TacSketch _fightGlass;
    readonly StackPanel _fightBody = new() { Margin = new Thickness(PadX, 8, PadX, 8) };
    readonly Border _timersInFight = new() { Margin = new Thickness(0, 0, 0, 7), HorizontalAlignment = HorizontalAlignment.Left };
    readonly Grid _fightFx = new() { IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Top };
    /// <summary>The clock of the sheen down the bars (its own element, so it never cuts another course short).</summary>
    readonly Border _sweepClock = new() { Width = 0, Height = 0, IsHitTestVisible = false };
    readonly TranslateTransform _fightShift = new();
    FrameworkElement? _head;

    public HudPrism() : base(bare: true)
    {
        // the look's own rows (Ui/Themes/ThemeRows.cs): each a clear glass bar splitting the light at its edge
        UseRows(PrismRow.Pitch, m => new PrismRow(this), 15, GapLine);
        _lineGlass = new TacSketch((dc, s) => Crystal(dc, s, _opacity, -1, Colors.White));
        _lineBand.RenderTransform = _lineBandX;
        _lineFx.Children.Add(_lineBand);
        _lineFx.SizeChanged += (_, e) => _lineFx.Clip = Octa(new Rect(1, .5, Math.Max(0, e.NewSize.Width - 2), Math.Max(0, e.NewSize.Height - 3)), Cut);
        // the words straight on the crystal's dark glass: no shade effect needed (or paid for) on it
        _line.Children.Add(_lineGlass); _line.Children.Add(_lineFx); _line.Children.Add(_lineBody); _line.Children.Add(_lineAnchor);
        _fightGlass = new TacSketch((dc, s) => Crystal(dc, s, _opacity, -1, Colors.White));
        _fight.Children.Add(_fightGlass); _fight.Children.Add(_fightBody); _fight.Children.Add(_fightFx); _fight.Children.Add(_sweepClock);
        _fight.RenderTransform = _fightShift;
        _noteHost.Children.Add(new TacSketch((dc, s) => Crystal(dc, s, _opacity, -1, Colors.White, cut: 5, glint: false)));
        _noteHost.Children.Add(_note);
        _runHeadLit = Lit(_runHead); _runHeadLit.Visibility = Visibility.Collapsed;
        _body.Children.Add(_fight); _body.Children.Add(Lit(_timersUp)); _body.Children.Add(_runHeadLit); _body.Children.Add(_line); _body.Children.Add(_rare); _body.Children.Add(_noteHost);
        _root.Children.Add(_body);
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
    /// The crystal: a slab of nearly neutral smoked glass with cut corners. A drop under it, the smoke (lighter at the top), a
    /// lighter facet falling from the top-left with a faint ridge, a darker one at the bottom-right, the table line inset from
    /// the rim, the bevel (bright along the top and left, dim at the bottom and right), the top-left cut lit, and the light
    /// entering there split into a thin streak of spectrum along the top edge. <paramref name="sheen"/> 0 to 1: a band of
    /// spectrum crossing it; <paramref name="tint"/> a wash of colour from the left (a rare drop's grade) at <paramref name="tintA"/>.
    /// </summary>
    static void Crystal(DrawingContext dc, Size s, double opacity, double sheen, Color sheenC, double cut = Cut, bool glint = true, Color? tint = null, double tintA = 0, double tintW = 1)
    {
        if (s.Width < 6 || s.Height < 6) return;
        double w = s.Width, h = s.Height;
        var r = new Rect(1, .5, w - 2, h - 3);
        var shape = Octa(r, cut);
        // the drop: a soft dark edge just below and round it (inside its own room)
        dc.PushTransform(new TranslateTransform(0, 1.5));
        dc.DrawGeometry(null, Mod.P(Mod.Br(Colors.Black, .24), 2), shape);
        dc.Pop();
        double a = Math.Clamp(.66 + .3 * opacity, .74, .96);
        dc.DrawGeometry(Mod.Grad(true, (Mod.A(SmokeC, a), 0), (Mod.A(DeepC, Math.Min(.97, a + .04)), 1)), null, shape);
        dc.PushClip(shape);
        if (tint is { } tc && tintA > 0 && tintW > 0)
        {
            var tw = w * Math.Clamp(tintW, 0, 1);
            dc.DrawRectangle(Mod.Grad(false, (Mod.A(tc, tintA), 0), (Mod.A(tc, tintA * .45), .6), (Mod.A(tc, 0), 1)), null, new Rect(0, 0, tw, h));
        }
        // the facets: a lighter plane from the top-left with its ridge, a darker one at the bottom-right
        double fx = Math.Min(w * .5, 150);
        dc.DrawGeometry(Mod.Grad(true, (Mod.A(Colors.White, .07), 0), (Mod.A(Colors.White, .015), 1)), null, Poly(new Point(0, 0), new Point(fx, 0), new Point(fx * .35, h), new Point(0, h)));
        dc.DrawLine(Mod.P(Mod.Grad(true, (Mod.A(Colors.White, .08), 0), (Mod.A(Colors.White, 0), .8)), 1), new Point(fx, 0), new Point(fx * .35, h));
        dc.DrawGeometry(Mod.Br(Colors.Black, .16), null, Poly(new Point(w, h * .3), new Point(w, h), new Point(w - Math.Min(w * .4, 120), h)));
        if (sheen is > 0 and < 1)
        {
            // the refraction sheen: a slanted band of spectrum crossing left to right, a white core in it
            double x = -70 + (w + 140) * sheen, k = Math.Sin(Math.PI * sheen);
            var band = Poly(new Point(x + 14, 0), new Point(x + 54, 0), new Point(x + 40, h), new Point(x, h));
            dc.DrawGeometry(SpecGrad(new Point(x, 0), new Point(x + 54, 0), .34 * k, fade: true), null, band);
            dc.DrawLine(Mod.P(Mod.Br(sheenC, .5 * k), 1.5), new Point(x + 34, 0), new Point(x + 20, h));
        }
        // the top's light: a pixel of white along the top edge, brightest left of centre
        dc.DrawRectangle(Mod.Grad(false, (Mod.A(Colors.White, .62), 0), (Mod.A(Colors.White, .32), .45), (Mod.A(Colors.White, .05), 1)), null, new Rect(cut, 1, Math.Max(0, w - 2 * cut), 1));
        dc.Pop();
        // the table: a faint line inset from the rim
        dc.DrawGeometry(null, Mod.P(Mod.Br(Colors.White, .065), 1), Octa(new Rect(3.5, 3.5, Math.Max(0, w - 7), Math.Max(0, h - 7)), Math.Max(0, cut - 2.5)));
        // the bevel: the rim lit from the top-left
        var rim = new LinearGradientBrush { MappingMode = BrushMappingMode.Absolute, StartPoint = new Point(0, 0), EndPoint = new Point(Math.Min(w, 260), h) };
        rim.GradientStops.Add(new GradientStop(Mod.A(Colors.White, .58), 0)); rim.GradientStops.Add(new GradientStop(Mod.A(Colors.White, .2), .55)); rim.GradientStops.Add(new GradientStop(Mod.A(Colors.White, .13), 1));
        rim.Freeze();
        dc.DrawGeometry(null, new Pen(rim, 1), shape);
        // the cuts: the top-left one bright, the top-right one lit, the bottom ones dim
        dc.DrawLine(Mod.P(Mod.Br(Colors.White, .95), 1.2), new Point(r.Left, r.Top + cut), new Point(r.Left + cut, r.Top));
        dc.DrawLine(Mod.P(Mod.Br(Colors.White, .45), 1), new Point(r.Right - cut, r.Top), new Point(r.Right, r.Top + cut));
        if (!glint) return;
        // the light entering at the top-left cut, split: six hairlines of spectrum along the top edge, fading out
        double gx = r.Left + cut * .55, len = Math.Min(64, w * .3);
        for (int i = 0; i < Spec.Length; i++)
        {
            double y = 2.2 + i * .85, x1 = gx + 6 + i * 1.5;
            var p = new LinearGradientBrush { MappingMode = BrushMappingMode.Absolute, StartPoint = new Point(x1 - 6, 0), EndPoint = new Point(x1 + len, 0) };
            p.GradientStops.Add(new GradientStop(Mod.A(Spec[i], .8), 0)); p.GradientStops.Add(new GradientStop(Mod.A(Spec[i], 0), 1)); p.Freeze();
            dc.DrawLine(new Pen(p, .8), new Point(x1 - 4, y), new Point(x1 + len, y + .4));
        }
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
            var w = Tt(Waiting(f.Abyss), 12.5, Soft, FontWeights.SemiBold); w.Margin = new Thickness(mark ? 0 : 7, 6, 0, 6);
            _lineBody.Children.Add(w);
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
            var glass = new TacSketch((dc, s) => Crystal(dc, s, _opacity, -1, c, cut: 6, glint: false));
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
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 5, 16, 5) };
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
            if (PartyBeforeHit(e)) { _fightBody.Children.Add(new Border { Height = 6 }); PutRows(_fightBody, e, now, inner); }
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
                _fightBody.Children.Add(new Border { Height = 3 });
            }
            else _fightBody.Children.Add(new Border { Height = 7 });
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
    /// one above). One course of <paramref name="ms"/>.
    /// </summary>
    void SweepAll(double ms = 1150)
    {
        if (Meter is not { } m) return;
        var rows = m.Members.Select(x => m.RowOf(x.Id) as PrismRow).Where(r => r != null).Cast<PrismRow>().ToList();
        if (rows.Count == 0) return;
        Motion.Play(_sweepClock, ms, t =>
        {
            for (int i = 0; i < rows.Count; i++) rows[i].SweepAt(Seg(t, i * .07, i * .07 + .68));
        }, () => { foreach (var r in rows) r.SweepAt(-1); });
    }

    /// <summary>A row took the lead (PrismRow tells it): its crown has flashed in; the sheen sweeps the bars.</summary>
    internal void NewLeader() { if (Moves) SweepAll(); }

    /// <summary>The top row's crown flashes in (the fight's first leader).</summary>
    void CrownTop()
    {
        if (Meter is not { } m || m.Members.FirstOrDefault(x => x.Rank == 1 && !x.Pinned) is not { } top) return;
        (m.RowOf(top.Id) as PrismRow)?.Crown();
    }

    // ================================================================ the moments

    /// <summary>A pickup: a band of spectrum crosses the Buddy's crystal and the worth comes in beside it on a small crystal, then fades.</summary>
    protected override void Pickup(Pick p, bool held)
    {
        if (KinahHidden || _line.Visibility != Visibility.Visible) return;
        var worth = p.Worth.Length > 0 ? "+" + p.Worth.TrimStart('+') : p.Count;
        var words = new StackPanel { Margin = new Thickness(9, 3, 11, 4) };
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
        _lineBand.Fill = Band;
        Motion.Play(chip, 1900, t =>
        {
            LineSheen(t < .5 ? Seg(t, 0, .5) : -1);
            double k = Out(Seg(t, .1, .3));
            shift.X = -10 * (1 - k);
            chip.Opacity = t < .82 ? k : 1 - Seg(t, .82, 1);
        }, () => { LineSheen(-1); _lineAnchor.Children.Remove(chip); });
    }

    /// <summary>A rare drop: its crystal's colour blooms out to the right with a sheen across it, the tile popping in.</summary>
    protected override void RareDrop(Pick p, bool held)
    {
        if (_rareWash is not { } wash || wash.RenderTransform is not ScaleTransform bloom) return;
        var tile = _rareTile;
        var band = _rareBand;
        var bx = band?.RenderTransform as TranslateTransform;
        var sc = new ScaleTransform(1, 1);
        if (tile != null) { tile.RenderTransformOrigin = new Point(.5, .5); tile.RenderTransform = sc; }
        if (band != null) band.Visibility = Visibility.Visible;
        Motion.Play(wash, 1500, t =>
        {
            bloom.ScaleX = Math.Max(.001, Out(Seg(t, 0, .45)));
            double flash = t < .2 ? Seg(t, 0, .2) : 1 - Seg(t, .2, .8);
            wash.Opacity = .66 + .34 * flash;
            sc.ScaleX = sc.ScaleY = .65 + .35 * Back(Seg(t, .05, .4), 2.2);
            double sh = Seg(t, .05, .6);
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
        Motion.Play(art, 1800, t =>
        {
            double shown = was + (1 - was) * Out(Seg(t, 0, .5));
            full.Clip = shown >= .999 ? null : Wedge(new Point(21, 21), 24, shown);
            LineSheen(t is > .3 and < .85 ? Seg(t, .3, .85) : -1);
        }, () => { _beat = beat; full.Visibility = Visibility.Collapsed; full.Clip = null; art.InvalidateVisual(); LineSheen(-1); _lineBand.Fill = Band; });
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

    /// <summary>An entry opened: the first timer's chip lights mint round its edge and settles.</summary>
    protected override void EntryOpened(TimerBoard.Item i, bool held)
    {
        if (_firstChip is not { } chip) return;
        var glow = new DropShadowEffect { Color = MintC2, ShadowDepth = 0, BlurRadius = 10, Opacity = 0 };
        chip.Effect = glow;
        Motion.Play(chip, 1200, t =>
        {
            double k = t < .25 ? Seg(t, 0, .25) : 1 - Seg(t, .25, 1);
            glow.Opacity = .9 * k;
        }, () => chip.Effect = null);
    }

    /// <summary>A fight started: the crystal rises a few pixels as it fades up, the leader's crown flashes in and the sheen sweeps the bars.</summary>
    protected override void FightStarted(Encounter e, bool held)
    {
        Motion.Play(_fight, 520, t =>
        {
            double k = Out(t);
            _fightShift.Y = 8 * (1 - k);
            _fightBody.Opacity = k;
        }, () => { _fightShift.Y = 0; _fightBody.Opacity = 1; });
        CrownTop();
        SweepAll();
    }

    /// <summary>A fight ended: a crystal plate tinted mint (coral for a wipe) lays over the head with the outcome and the clock, then lifts; a kill sweeps the bars.</summary>
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
        Motion.Play(_fightFx, 2400, t =>
        {
            lay.ScaleX = Math.Max(.001, Out(Seg(t, 0, .22)));
            words.Opacity = t < .84 ? Seg(t, .08, .22) : 1 - Seg(t, .84, 1);
            band.Opacity = t < .84 ? 1 : 1 - Seg(t, .84, 1);
            if (head != null) head.Opacity = t < .84 ? 1 - Seg(t, 0, .12) : Seg(t, .84, 1);
        }, () => { _fightFx.Children.Clear(); if (head != null) head.Opacity = 1; });
        if (!wipe) SweepAll(1300);
    }
}
