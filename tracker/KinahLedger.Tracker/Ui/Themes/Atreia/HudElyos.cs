using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using KinahLedger.Parser;
using KinahLedger.Session;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

/// <summary>
/// Elyos (the Atreia theme; looks pack 2, 2026-11-20): the radiance of the Elyos, light and gold over sky. Every part sits
/// on a plate of deep midnight-to-royal blue with its corners cut, framed in a gold-leaf hairline with a faint inner line, a
/// gold diamond on each cut corner and a crest on the top edge; a warm light falls from the plate's top. Titles and small
/// words are set in a serif (Palatino Linotype, small capitals); figures in Bahnschrift, crisp and tabular; names in Inter.
/// The meter's rows: Roman-numeral ranks in gold, the class art in a gold ring, each bar the class's colour washed with warm
/// light, a gold top edge and an ivory leading edge; you a gold edge at the row's left; the leader wears wings of light round
/// their numeral. The Buddy: the ring a gold halo, the hour big in ivory, the pickup tiles set in gold.
/// The moments: a sunlit glint sweeps the bars (a fight's start, a kill, a new leader, a row that climbs); the wings unfurl
/// round rank I when someone takes the lead; a pickup sends a glint across the Buddy's plate and its worth floats in beside
/// it; a rare drop blooms in its grade's colour; your best passed fills the halo and it flares; an entry opening lights its
/// chip in gold; a fight rises in; a kill lays a gold band with the outcome over the fight's head.
/// </summary>
sealed partial class HudElyos : ModernLook
{
    // ---- the palette: night and royal blue, gold leaf, ivory
    static readonly Color NightC = Color.FromRgb(0x0A, 0x12, 0x30), RoyalC = Color.FromRgb(0x13, 0x22, 0x4A), AbyssC = Color.FromRgb(0x05, 0x09, 0x18),
        GoldHiC = Color.FromRgb(0xF7, 0xDE, 0x96), GoldMidC = Color.FromRgb(0xDC, 0xB4, 0x62), GoldLoC = Color.FromRgb(0xB5, 0x86, 0x2F),
        IvoryC = Color.FromRgb(0xFF, 0xF8, 0xEA), WarmC = Color.FromRgb(0xFF, 0xE6, 0xB0), MintC2 = Color.FromRgb(0x84, 0xF2, 0xC4),
        CoralC = Color.FromRgb(0xFF, 0xA0, 0x98), HpAC = Color.FromRgb(0xB0, 0x1E, 0x34), HpBC = Color.FromRgb(0xF4, 0x62, 0x52),
        WarnC2 = Color.FromRgb(0xFF, 0xB0, 0x86), GreyC = Color.FromRgb(0x7A, 0x82, 0x98);
    static readonly SolidColorBrush Ivory = Frozen(IvoryC), Soft = Frozen(Mod.A(IvoryC, .84)), Quiet = Frozen(Mod.A(IvoryC, .62)),
        GoldInk = Frozen(Color.FromRgb(0xF0, 0xD3, 0x8C)), GoldHi = Frozen(GoldHiC), Mint2 = Frozen(MintC2), Coral = Frozen(CoralC);

    /// <summary>The serif for titles and small words (every Windows has one of these).</summary>
    static readonly FontFamily Serif = new("Palatino Linotype, Constantia, Georgia, Cambria");
    /// <summary>The figures' face: crisp, tabular, a touch narrower than Inter.</summary>
    static readonly FontFamily Fig = HudThemes.Bahn;

    // the room round the look: the plates' shadow and crest stay inside the window; 98 at the right for the pickup's chip
    readonly Grid _root = new() { Margin = new Thickness(8, 8, 98, 10), HorizontalAlignment = HorizontalAlignment.Left };
    readonly StackPanel _body = new() { HorizontalAlignment = HorizontalAlignment.Left };
    double _opacity = 0.9;

    // the Buddy: its plate, the halo, the hour, the session and the timer chips
    readonly Grid _line = new() { HorizontalAlignment = HorizontalAlignment.Left };
    readonly TacSketch _lineArt;
    readonly StackPanel _lineBody = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(9, 6, 14, 6) };
    readonly Canvas _lineAnchor = Tac.Anchor(HorizontalAlignment.Right, VerticalAlignment.Center);
    readonly StackPanel _timers = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    readonly Border _timersInLine = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
    /// <summary>The timer chips over the line, when the pickup strip takes their room on it.</summary>
    readonly Border _timersUp = new() { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(4, 0, 0, 6), Visibility = Visibility.Collapsed };
    /// <summary>The pickup strip in Elyos: 24 px tiles each set in gold (<see cref="Gild"/>), a gold rule, a gold star.</summary>
    static readonly PickStrip.Look Strip = new(24, 5, true, Frozen(Mod.A(GoldHiC, .45)), Frozen(GoldHiC));
    bool _dim;
    TacSketch? _ringArt;
    FrameworkElement? _ringFull;
    System.Windows.Shapes.Ellipse? _ringFlare;
    double _share = -1;
    bool _beat;
    Border? _firstChip;
    // the sunlit glint over the Buddy's plate: one band (a frozen brush) moved by its transform, clipped to the plate when it starts
    readonly Canvas _lineSheenHost = new() { IsHitTestVisible = false };
    readonly System.Windows.Shapes.Rectangle _lineSheen = new() { Width = 56, Fill = SheenBrush, Opacity = 0, IsHitTestVisible = false };
    readonly TranslateTransform _lineSheenShift = new();
    static readonly LinearGradientBrush SheenBrush = Lin(new Point(0, 0), new Point(1, 0), (Mod.A(WarmC, 0), 0), (Mod.A(WarmC, .26), .42), (Mod.A(Colors.White, .34), .55), (Mod.A(WarmC, 0), 1));

    // the rare drop under the Buddy, and the notice line
    readonly Grid _rare = new() { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 5, 0, 0), Visibility = Visibility.Collapsed };
    readonly Border _note = new()
    {
        HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 5, 0, 0), Visibility = Visibility.Collapsed, CornerRadius = new CornerRadius(2),
        Padding = new Thickness(8, 3, 10, 4), BorderThickness = new Thickness(1),
    };

    // the fight: its plate, head, HP, rows and foot
    const double Pad = 10;
    readonly Grid _fight = new() { HorizontalAlignment = HorizontalAlignment.Left, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 3, 0, 10) };
    readonly TacSketch _fightArt;
    readonly StackPanel _fightBody = new() { Margin = new Thickness(Pad, 9, Pad, 8) };
    readonly Border _timersInFight = new() { Margin = new Thickness(0, 0, 0, 7), HorizontalAlignment = HorizontalAlignment.Left };
    readonly Canvas _fightFx = new() { IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Left };
    readonly TranslateTransform _fightShift = new();
    FrameworkElement? _head;
    string _leadFight = "";
    long _leadId;

    public HudElyos() : base(bare: true)
    {
        // the look's own rows (Ui/Themes/ThemeRows.cs): gold-edged class bars on the plate
        UseRows(ElyosRow.Pitch, m => new ElyosRow(), 15, GapLine);
        _lineArt = new TacSketch((dc, s) => PlateArt(dc, s, _dim ? .55 : _opacity, crest: true, dim: _dim));
        _lineSheen.RenderTransform = new TransformGroup { Children = { new SkewTransform(-28, 0), _lineSheenShift } };
        _lineSheenHost.Children.Add(_lineSheen);
        _line.Children.Add(_lineArt); _line.Children.Add(_lineSheenHost); _line.Children.Add(_lineBody); _line.Children.Add(_lineAnchor);
        _fightArt = new TacSketch((dc, s) => PlateArt(dc, s, _opacity, crest: true));
        _fight.Children.Add(_fightArt); _fight.Children.Add(_fightBody); _fight.Children.Add(_fightFx);
        _fight.RenderTransform = _fightShift;
        _runHeadLit = Lit(_runHead); _runHeadLit.Visibility = Visibility.Collapsed;
        _body.Children.Add(_fight); _body.Children.Add(_timersUp); _body.Children.Add(_runHeadLit); _body.Children.Add(_line); _body.Children.Add(_rare); _body.Children.Add(_note);
        _root.Children.Add(_body);
        System.Windows.Documents.TextElement.SetFontFamily(_root, Mod.Face);
        TextOptions.SetTextFormattingMode(_root, TextFormattingMode.Display);
    }

    public override FrameworkElement Root => _root;
    public override Thickness Inset => _root.Margin;
    public override double Corner => 0;
    public override FrameworkElement? FightFrame => _fight.Visibility == Visibility.Visible ? _fight : null;

    public override void SetOpacity(double opacity)
    {
        // the plates take the setting (never so faint the words are lost over snow)
        _opacity = Math.Clamp(opacity, 0.3, 1);
        _lineArt.InvalidateVisual(); _fightArt.InvalidateVisual();
    }

    protected override string LineSig(Figures f) => base.LineSig(f) + "|" + (Rare(Values)?.Id ?? "") + "|" + (Picks(Values).FirstOrDefault()?.Id ?? "") + "|" + Dimmed(Values) + "|" + PickStrip.Sig(Values, Strip) + "|" + RunBuddy.Sig(Values);

    static bool Dimmed(IReadOnlyDictionary<string, string> v) => (v.TryGetValue("show", out var s) && s == "dim") || (v.TryGetValue("paused", out var p) && p.Length > 0);

    // ================================================================ the plate

    /// <summary>A rectangle with its corners cut by <paramref name="c"/> (the plate's shape), frozen.</summary>
    static Geometry Plate(Rect r, double c)
    {
        c = Math.Max(0, Math.Min(c, Math.Min(r.Width, r.Height) / 2));
        var g = new StreamGeometry();
        using (var x = g.Open())
        {
            x.BeginFigure(new Point(r.Left + c, r.Top), true, true);
            x.LineTo(new Point(r.Right - c, r.Top), true, false);
            x.LineTo(new Point(r.Right, r.Top + c), true, false);
            x.LineTo(new Point(r.Right, r.Bottom - c), true, false);
            x.LineTo(new Point(r.Right - c, r.Bottom), true, false);
            x.LineTo(new Point(r.Left + c, r.Bottom), true, false);
            x.LineTo(new Point(r.Left, r.Bottom - c), true, false);
            x.LineTo(new Point(r.Left, r.Top + c), true, false);
        }
        g.Freeze();
        return g;
    }

    static LinearGradientBrush Lin(Point a, Point b, params (Color C, double At)[] stops)
    {
        var br = new LinearGradientBrush { StartPoint = a, EndPoint = b };
        foreach (var (c, at) in stops) br.GradientStops.Add(new GradientStop(c, at));
        br.Freeze();
        return br;
    }

    /// <summary>Gold leaf: light and deep gold in bands along the frame, as leaf catches the light.</summary>
    static readonly LinearGradientBrush Leaf = Lin(new Point(0, 0), new Point(1, .35),
        (GoldHiC, 0), (GoldMidC, .22), (GoldLoC, .42), (GoldHiC, .62), (GoldMidC, .8), (GoldLoC, 1));
    static readonly Pen LeafPen = Frz(new Pen(Leaf, 1));
    static readonly Pen InnerPen = Frz(new Pen(Frozen(Mod.A(GoldHiC, .17)), .8));
    static readonly Pen DiamondEdge = Frz(new Pen(Frozen(Mod.A(AbyssC, .7)), .7));
    /// <summary>A filled gold: light at the top, deep at the foot (YOU, the diamonds, the wings).</summary>
    static readonly LinearGradientBrush GoldFill = Lin(new Point(0, 0), new Point(0, 1), (Color.FromRgb(0xFF, 0xF0, 0xC4), 0), (GoldHiC, .4), (GoldMidC, .75), (GoldLoC, 1));
    // the corner strokes, fading away from the corner (right, left, down, up)
    static readonly LinearGradientBrush FadeR = Lin(new Point(0, 0), new Point(1, 0), (GoldHiC, 0), (Mod.A(GoldHiC, 0), 1)),
        FadeL = Lin(new Point(1, 0), new Point(0, 0), (GoldHiC, 0), (Mod.A(GoldHiC, 0), 1)),
        FadeD = Lin(new Point(0, 0), new Point(0, 1), (GoldHiC, 0), (Mod.A(GoldHiC, 0), 1)),
        FadeU = Lin(new Point(0, 1), new Point(0, 0), (GoldHiC, 0), (Mod.A(GoldHiC, 0), 1));
    /// <summary>The warm light falling from the top's middle.</summary>
    static readonly RadialGradientBrush SkyGlow = MakeGlow();

    static RadialGradientBrush MakeGlow()
    {
        var b = new RadialGradientBrush { Center = new Point(.5, 0), GradientOrigin = new Point(.5, 0), RadiusX = .62, RadiusY = 1 };
        b.GradientStops.Add(new GradientStop(Mod.A(WarmC, .15), 0));
        b.GradientStops.Add(new GradientStop(Mod.A(WarmC, .05), .55));
        b.GradientStops.Add(new GradientStop(Mod.A(WarmC, 0), 1));
        b.Freeze();
        return b;
    }

    static Pen Frz(Pen p) { p.Freeze(); return p; }

    /// <summary>A small gold diamond (a cut corner's, the crest's, a rule's middle).</summary>
    static void Diamond(DrawingContext dc, Point m, double r, Brush? fill = null)
    {
        var g = new StreamGeometry();
        using (var x = g.Open())
        {
            x.BeginFigure(new Point(m.X, m.Y - r), true, true);
            x.LineTo(new Point(m.X + r, m.Y), true, false);
            x.LineTo(new Point(m.X, m.Y + r), true, false);
            x.LineTo(new Point(m.X - r, m.Y), true, false);
        }
        g.Freeze();
        dc.DrawGeometry(fill ?? GoldFill, DiamondEdge, g);
    }

    /// <summary>
    /// The Elyos plate: a soft drop under it, night-to-royal blue (deeper at its foot), the warm light from the top, the gold
    /// leaf frame with a faint inner line, gold strokes and a diamond at each cut corner, a crest on the top edge.
    /// <paramref name="edge"/>: the frame washed in another colour (a rare drop's grade). Drawn only when the plate's size or
    /// the opacity setting changes: the moments' light crosses it as an element of its own.
    /// </summary>
    internal static void PlateArt(DrawingContext dc, Size s, double opacity, bool crest = false, bool shadow = true, double cut = 6,
        Color? edge = null, bool dim = false)
    {
        if (s.Width < 14 || s.Height < 12) return;
        var r = new Rect(.5, .5, s.Width - 1, s.Height - 1);
        var shape = Plate(r, cut);
        if (shadow)
        {
            // the drop: rings outside the shape, deeper below (kept inside the window's room)
            for (int k = 1; k <= 4; k++)
            {
                var o = Plate(new Rect(r.X - k * .8, r.Y - k * .4 + 2, r.Width + k * 1.6, r.Height + k * 1.2 + 1), cut + k * .5);
                var ring = new CombinedGeometry(GeometryCombineMode.Exclude, o, shape);
                ring.Freeze();
                dc.DrawGeometry(Frozen(Mod.A(Colors.Black, .1 - k * .02)), null, ring);
            }
        }
        double a = Math.Clamp(.8 + .18 * opacity, .8, .98);
        dc.DrawGeometry(Mod.Grad(true, (Mod.A(dim ? Mod.Mix(RoyalC, GreyC, .3) : RoyalC, a), 0), (Mod.A(NightC, a), .55), (Mod.A(AbyssC, Math.Min(.98, a + .02)), 1)), null, shape);
        dc.PushClip(shape);
        if (!dim) dc.DrawRectangle(SkyGlow, null, new Rect(0, 0, s.Width, Math.Min(s.Height, 80)));
        // a line of light along the top edge
        dc.DrawRectangle(Mod.Grad(false, (Mod.A(IvoryC, 0), 0), (Mod.A(IvoryC, .2), .3), (Mod.A(IvoryC, .2), .7), (Mod.A(IvoryC, 0), 1)), null, new Rect(cut, 1.5, Math.Max(0, s.Width - 2 * cut), 1));
        dc.Pop();
        // the frames
        dc.DrawGeometry(null, edge is { } ec ? Frz(new Pen(Mod.Grad(false, (Mod.Mix(ec, GoldHiC, .35), 0), (ec, .5), (Mod.Mix(ec, GoldLoC, .3), 1)), 1)) : LeafPen, shape);
        if (s.Height > 26) dc.DrawGeometry(null, InnerPen, Plate(new Rect(r.X + 3, r.Y + 3, r.Width - 6, r.Height - 6), Math.Max(1, cut - 1.2)));
        // the cut corners: a gold diamond on each, gold strokes along the edges from it
        double L = r.Left, T = r.Top, R = r.Right, B = r.Bottom, len = Math.Min(12, Math.Min(s.Width, s.Height) / 4);
        dc.DrawRectangle(FadeR, null, new Rect(L + cut, T - .3, len, 1.6)); dc.DrawRectangle(FadeD, null, new Rect(L - .3, T + cut, 1.6, len));
        dc.DrawRectangle(FadeL, null, new Rect(R - cut - len, T - .3, len, 1.6)); dc.DrawRectangle(FadeD, null, new Rect(R - 1.3, T + cut, 1.6, len));
        dc.DrawRectangle(FadeR, null, new Rect(L + cut, B - 1.3, len, 1.6)); dc.DrawRectangle(FadeU, null, new Rect(L - .3, B - cut - len, 1.6, len));
        dc.DrawRectangle(FadeL, null, new Rect(R - cut - len, B - 1.3, len, 1.6)); dc.DrawRectangle(FadeU, null, new Rect(R - 1.3, B - cut - len, 1.6, len));
        double d = cut / 2, dr = Math.Min(2.6, cut * .45);
        Diamond(dc, new Point(L + d, T + d), dr); Diamond(dc, new Point(R - d, T + d), dr);
        Diamond(dc, new Point(L + d, B - d), dr); Diamond(dc, new Point(R - d, B - d), dr);
        if (crest && s.Width > 80)
        {
            // the crest: a diamond on the top edge's middle, a fine gold line flaring out from it each way
            double cx = Math.Round(s.Width / 2);
            dc.DrawRectangle(FadeL, null, new Rect(cx - 30, T - .3, 26, 1.6));
            dc.DrawRectangle(FadeR, null, new Rect(cx + 4, T - .3, 26, 1.6));
            Diamond(dc, new Point(cx, T + .5), 3.2);
        }
    }

    /// <summary>An ornamental rule across: a gold line fading at both ends, a small diamond at its middle (before the foot, in the card).</summary>
    static FrameworkElement Rule(Thickness m) => new TacSketch((dc, s) =>
    {
        double y = Math.Round(s.Height / 2);
        dc.DrawRectangle(Mod.Grad(false, (Mod.A(GoldHiC, 0), 0), (Mod.A(GoldHiC, .5), .4), (Mod.A(GoldHiC, .5), .6), (Mod.A(GoldHiC, 0), 1)), null, new Rect(0, y - .5, s.Width, 1));
        Diamond(dc, new Point(Math.Round(s.Width / 2), y), 2.4);
    }) { Height = 7, Margin = m };

    // ================================================================ words

    /// <summary>The tight shade under words that sit straight on the game (the run's head): 1 px down, 3 px soft.</summary>
    static DropShadowEffect Shade() => new() { Color = Colors.Black, Direction = 270, ShadowDepth = 1, BlurRadius = 3, Opacity = .95 };

    /// <summary>The wide soft halo round words on the game, so a bright scene behind them goes a shade darker.</summary>
    static DropShadowEffect Halo() => new() { Color = Colors.Black, ShadowDepth = 0, BlurRadius = 10, Opacity = .62 };

    /// <summary>Words lit for the game: the tight shade on them, the halo round them.</summary>
    static Border Lit(FrameworkElement el)
    {
        el.Effect = Shade();
        return new Border { Child = el, Effect = Halo(), HorizontalAlignment = el.HorizontalAlignment, VerticalAlignment = el.VerticalAlignment };
    }

    /// <summary>A small word in the serif's small capitals, in gold (TOTAL, the card's heads).</summary>
    static TextBlock Caps(string s, double size, Brush? fg = null, FontWeight? w = null)
    {
        var t = Tt(s, size, fg ?? GoldInk, w ?? FontWeights.Bold, Serif);
        System.Windows.Documents.Typography.SetCapitals(t, FontCapitals.SmallCaps);
        return t;
    }

    /// <summary>A figure in Bahnschrift.</summary>
    static TextBlock F(string s, double size, Brush fg, FontWeight? w = null) => Tt(s, size, fg, w ?? FontWeights.SemiBold, Fig);

    /// <summary>A small gold-edged plate (★ DPS, the heal mark, AVG, READY): <paramref name="filled"/> gold with night-blue words (YOU).</summary>
    static Border Mark(string s, Color? ink = null, bool filled = false, double size = 8.5)
    {
        var c = ink ?? GoldHiC;
        var t = Tt(s, size, filled ? Frozen(NightC) : Frozen(c), FontWeights.Bold);
        return new Border
        {
            Child = t, CornerRadius = new CornerRadius(2), Padding = new Thickness(4, 0, 4, 1), VerticalAlignment = VerticalAlignment.Center,
            Background = filled ? GoldFill : Frozen(Mod.A(AbyssC, .82)), BorderThickness = new Thickness(1),
            BorderBrush = filled ? Frozen(GoldHiC) : Frozen(Mod.A(c, .72)),
        };
    }

    /// <summary>Each pickup tile of a strip set in gold: a gold-leaf ring just outside its own grade frame.</summary>
    static FrameworkElement Gild(FrameworkElement strip)
    {
        if (strip is Panel p)
            foreach (var g in p.Children.OfType<Grid>())
                if (g.Tag is "pick-tile" or "pick-best")
                    g.Children.Insert(0, new Border { BorderBrush = Leaf, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6.5), Margin = new Thickness(-1.5), IsHitTestVisible = false });
        return strip;
    }

    // ================================================================ the Buddy

    protected override void DrawLine(Figures f)
    {
        _lineBody.Children.Clear();
        _ringArt = null; _timerUp = false; _stripTimerW = -1;
        if (_timersInLine.Child != null) _timersInLine.Child = null;
        _dim = Dimmed(Values);
        _lineBody.Opacity = _dim ? .62 : 1;
        bool mark = f.Abyss && f.Counted && f.Icon.Length > 0;
        if (mark) { var m = AbyssMark(f.Icon, 38); m.Margin = new Thickness(0, 0, 10, 0); _lineBody.Children.Add(m); }
        // in a dungeon run: the run's clock, splits and verdict in place of the hour (HudElyos.Run.cs)
        var rs = RunNow();
        _runHeadLit.Visibility = Visibility.Collapsed;
        if (rs != null) DrawRun(rs);
        else if (!HasHour(f))
        {
            var w = Tt(Waiting(f.Abyss), 12.5, Soft, FontWeights.SemiBold); w.Margin = new Thickness(mark ? 0 : 4, 6, 4, 6);
            _lineBody.Children.Add(w);
        }
        else
        {
            if (HasRing(f))
            {
                _share = f.Share; _beat = f.Beat;
                var ring = new Grid { Width = 42, Height = 42, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 11, 0) };
                _ringArt = new TacSketch(DrawRing, 42, 42);
                ring.Children.Add(_ringArt);
                _ringFull = new TacSketch(DrawHalo, 42, 42) { Opacity = f.Beat ? 1 : 0 };
                ring.Children.Add(_ringFull);
                _ringFlare = new System.Windows.Shapes.Ellipse { Width = 42, Height = 42, Stroke = Frozen(Mod.A(WarmC, .9)), StrokeThickness = 1.6, Opacity = 0, IsHitTestVisible = false, RenderTransform = new ScaleTransform(1, 1, 21, 21) };
                ring.Children.Add(_ringFlare);
                var pc = F(f.Beat ? "★" : Pct(f.Share), 13.5, f.Beat ? GoldHi : Ivory); pc.HorizontalAlignment = HorizontalAlignment.Center;
                ring.Children.Add(pc);
                _lineBody.Children.Add(ring);
            }
            else if (!mark) _lineBody.Children.Add(new Border { Width = 3 });
            // the hour big in ivory, its unit small in the serif's gold; the session and the latest pickup under it
            var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 1) };
            var top = new StackPanel { Orientation = Orientation.Horizontal };
            var rate = F(f.Rate, f.Warm ? 15 : 24, f.Warm ? Soft : Ivory, f.Warm ? FontWeights.Medium : FontWeights.SemiBold);
            rate.Tag = "buddy-rate";
            top.Children.Add(rate);
            if (f.Unit.Length > 0)
            {
                var u = Tt(f.Unit, 12, GoldInk, FontWeights.SemiBold, Serif); u.FontStyle = FontStyles.Italic;
                u.VerticalAlignment = VerticalAlignment.Bottom; u.Margin = new Thickness(2, 0, 0, f.Warm ? 1 : 3); top.Children.Add(u);
            }
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
                    if ((strip = StripIn(StripRoom(FightWidth - 10, baseW + timerW, wordsW, oldW, 14), look)) != null) break;
                    if (timerW > 0 && (strip = StripIn(StripRoom(FightWidth - 10, baseW, wordsW, oldW + timerW, 14), look)) != null) { _timerUp = true; break; }
                }
                if (strip != null) { sub = bare; strip.Margin = new Thickness(14, 0, 0, 0); Gild(strip); }
                // no room beside the hour: under the session, never dropped (Steven, 2026-11-19)
                else if ((under = StripUnder(Strip, false)) != null) { sub = bare; Gild(under); under.Margin = new Thickness(2, 7, 0, 2); }
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

        // the rare drop the window holds: a plate framed in its grade's colour, its tile, RARE DROP and its name
        _rare.Children.Clear();
        if (!KinahHidden && Rare(Values) is { } r)
        {
            var c = ColorOf(r.Color, r.Grade);
            _rare.Children.Add(new TacSketch((dc, s) => PlateArt(dc, s, _opacity, edge: c, cut: 5)));
            // its grade's light from the left (drawn once), blooming out on the drop by its scale; a flash of it over the plate
            var light = Mod.Grad(false, (Mod.A(c, .42), 0), (Mod.A(c, .16), .6), (Mod.A(c, 0), 1));
            var art = new TacSketch((dc, s) => { dc.PushClip(Plate(new Rect(.5, .5, s.Width - 1, s.Height - 1), 5)); dc.DrawRectangle(light, null, new Rect(0, 0, s.Width, s.Height)); dc.Pop(); })
            { RenderTransform = new ScaleTransform(1, 1), RenderTransformOrigin = new Point(0, .5) };
            var flash = new TacSketch((dc, s) => { dc.PushClip(Plate(new Rect(.5, .5, s.Width - 1, s.Height - 1), 5)); dc.DrawRectangle(Mod.Grad(false, (Mod.A(c, .45), 0), (Mod.A(c, .2), .6), (Mod.A(c, 0), 1)), null, new Rect(0, 0, s.Width, s.Height)); dc.Pop(); }) { Opacity = 0 };
            _rareArt = art; _rareFlashArt = flash;
            _rare.Children.Add(art); _rare.Children.Add(flash);
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 5, 16, 5) };
            var tile = Tile(r, 26); tile.Margin = new Thickness(0, 0, 9, 0);
            _rareTile = tile;
            row.Children.Add(tile);
            var mk = Mark(L.T("RARE DROP"), size: 8); mk.Margin = new Thickness(0, 0, 8, 0);
            row.Children.Add(mk);
            var nm = Tt(r.Name, 13, Frozen(Mod.Mix(c, IvoryC, .5)), FontWeights.Bold);
            row.Children.Add(nm);
            if (r.Count.Length > 0) { var cn = F(" " + r.Count, 11.5, Soft); cn.Margin = new Thickness(2, 1, 0, 0); row.Children.Add(cn); }
            _rare.Children.Add(row);
            _rare.Visibility = Visibility.Visible;
        }
        else { _rare.Visibility = Visibility.Collapsed; _rareArt = null; _rareFlashArt = null; _rareTile = null; }

        // the notice: its words in their tone on a small night plate, the warning sign before a pause
        if (f.Note.Length > 0 && !KinahHidden)
        {
            bool warn = ReferenceEquals(f.NoteTone, Warn);
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            if (warn) { var wi = WarnIcon(); wi.Width = wi.Height = 13; wi.Margin = new Thickness(0, 0, 6, 0); sp.Children.Add(wi); }
            var tone = warn ? WarnC2 : ReferenceEquals(f.NoteTone, Gold) ? GoldHiC : ReferenceEquals(f.NoteTone, Mint) ? MintC2 : IvoryC;
            var t = Tt(f.Note, 11.5, Frozen(tone), FontWeights.SemiBold); t.TextTrimming = TextTrimming.CharacterEllipsis; t.MaxWidth = 380;
            sp.Children.Add(t);
            _note.Child = sp;
            _note.Background = Frozen(Mod.A(NightC, .9));
            _note.BorderBrush = Frozen(Mod.A(warn ? WarnC2 : GoldHiC, .45));
            _note.Visibility = Visibility.Visible;
        }
        else { _note.Child = null; _note.Visibility = Visibility.Collapsed; }
    }

    /// <summary>The line under the hour: the session, the latest pickup and its worth (<paramref name="pick"/>); in the Abyss its stats.</summary>
    TextBlock Sub(Figures f, bool pick)
    {
        var sub = new TextBlock { FontFamily = Mod.Face, FontSize = 10.5, FontWeight = FontWeights.Medium, Foreground = Frozen(Mod.A(IvoryC, .8)), Margin = new Thickness(1, 0, 0, 0) };
        System.Windows.Documents.Typography.SetNumeralAlignment(sub, FontNumeralAlignment.Tabular);
        void Dot() { if (sub.Inlines.Count > 0) sub.Inlines.Add(new System.Windows.Documents.Run("  ◆  ") { Foreground = Frozen(Mod.A(GoldHiC, .55)), FontSize = 6.5, BaselineAlignment = BaselineAlignment.Center }); }
        if (f.Total.Length > 0) { sub.Inlines.Add(new System.Windows.Documents.Run(f.Total) { FontWeight = FontWeights.SemiBold, Foreground = Ivory }); sub.Inlines.Add(new System.Windows.Documents.Run(" " + f.TotalWord)); }
        foreach (var (fig, word, tone) in f.Stats)
        {
            Dot();
            var c = ReferenceEquals(tone, Rec) ? CoralC : ReferenceEquals(tone, Gold) ? GoldHiC : IvoryC;
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

    TacSketch? _rareArt, _rareFlashArt;
    FrameworkElement? _rareTile;

    /// <summary>
    /// The halo's track and your best's share: a faint gold ring on a night disc, the share as a gold-leaf arc (round caps, a
    /// dark line under it, a bead of light at its head). Beaten, <see cref="DrawHalo"/> lies over it.
    /// </summary>
    void DrawRing(DrawingContext dc, Size s)
    {
        double c = s.Width / 2, r = c - 4.5;
        var center = new Point(c, c);
        dc.DrawEllipse(Frozen(Mod.A(AbyssC, .55)), null, center, r - 1.5, r - 1.5);
        dc.DrawEllipse(null, Mod.P(Frozen(Mod.A(GoldHiC, .1)), 6), center, r, r);
        dc.DrawEllipse(null, Mod.P(Frozen(Mod.A(GoldLoC, .45)), 2.4), center, r, r);
        double shown = Math.Clamp(_beat ? 1 : _share, 0, 1);
        if (shown < .004 || shown >= .999) return;
        double a = 2 * Math.PI * shown;
        var fig = new PathFigure { StartPoint = new Point(c, c - r) };
        fig.Segments.Add(new ArcSegment(new Point(c + r * Math.Sin(a), c - r * Math.Cos(a)), new Size(r, r), 0, a > Math.PI, SweepDirection.Clockwise, true));
        var pg = new PathGeometry(); pg.Figures.Add(fig); pg.Freeze();
        dc.DrawGeometry(null, Mod.P(Frozen(Mod.A(AbyssC, .6)), 5.4, round: true), pg);
        dc.DrawGeometry(null, Mod.P(ArcGold, 3.4, round: true), pg);
        dc.DrawEllipse(Frozen(Color.FromRgb(0xFF, 0xF6, 0xD8)), null, new Point(c + r * Math.Sin(a), c - r * Math.Cos(a)), 1.9, 1.9);
    }

    static readonly LinearGradientBrush ArcGold = Lin(new Point(0, 0), new Point(1, 1), (GoldHiC, 0), (GoldMidC, .6), (GoldLoC, 1));

    /// <summary>The full halo (your best beaten): the whole ring in gold over a soft gold glow, twelve short rays of light round it.</summary>
    static void DrawHalo(DrawingContext dc, Size s)
    {
        double c = s.Width / 2, r = c - 4.5;
        var center = new Point(c, c);
        dc.DrawEllipse(Frozen(Mod.A(AbyssC, .55)), null, center, r - 1.5, r - 1.5);
        dc.DrawEllipse(null, Mod.P(Frozen(Mod.A(GoldHiC, .22)), 6.5), center, r, r);
        dc.DrawEllipse(null, Mod.P(GoldFill, 3.4), center, r, r);
        var ray = Mod.P(Frozen(Mod.A(GoldHiC, .8)), 1.2, round: true);
        for (int k = 0; k < 12; k++)
        {
            double a = k * Math.PI / 6, sn = Math.Sin(a), cs = Math.Cos(a), r0 = r + 2.6, r1 = r + (k % 3 == 0 ? 4.4 : 3.6);
            dc.DrawLine(ray, new Point(c + r0 * sn, c - r0 * cs), new Point(c + r1 * sn, c - r1 * cs));
        }
    }

    protected override void KinahShown(bool shown)
    {
        _line.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
        if (!shown) { _note.Visibility = Visibility.Collapsed; _rare.Visibility = Visibility.Collapsed; }
        _fight.Margin = new Thickness(0, 3, 0, shown ? 10 : 0);
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
            var c = tone == TimerBadge.Tone.On ? MintC2 : tone == TimerBadge.Tone.Soon ? GoldHiC : Color.FromRgb(0xC8, 0xD2, 0xEA);
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            // a small diamond in the timer's tone
            double d = first ? 6 : 5;
            sp.Children.Add(new System.Windows.Shapes.Rectangle
            {
                Width = d, Height = d, Fill = Frozen(c), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 7, 0),
                RenderTransform = new RotateTransform(45, d / 2, d / 2),
            });
            var t = Tt(text, first ? 11.5 : 10, first ? Ivory : Soft, first ? FontWeights.Bold : FontWeights.SemiBold);
            if (tone == TimerBadge.Tone.On) t.Foreground = Frozen(Mod.Mix(MintC2, IvoryC, .45));
            sp.Children.Add(t);
            var chip = new Border
            {
                Child = sp, CornerRadius = new CornerRadius(2), Padding = first ? new Thickness(8, 3, 10, 4) : new Thickness(7, 2, 9, 3), VerticalAlignment = VerticalAlignment.Center,
                Background = Frozen(Mod.A(NightC, .92)), BorderThickness = new Thickness(1),
                BorderBrush = tone == TimerBadge.Tone.On ? Frozen(Mod.A(MintC2, .6)) : first ? Leaf : Frozen(Mod.A(GoldHiC, .35)),
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
        double inner = w - 2 * Pad;
        _fightBody.Children.Add(_timersInFight);
        // the head: the boss in the serif, the outcome once it's over, the clock in gold
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition()); head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = Tt(e.Title, 15.5, Ivory, FontWeights.Bold, Serif); title.TextTrimming = TextTrimming.CharacterEllipsis;
        head.Children.Add(title);
        var right = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 0, 0) };
        if (e.Waiting) right.Children.Add(Mark(L.T("READY"), MintC2, size: 9.5));
        else
        {
            var outcome = OutcomeText(e);
            if (outcome.Length > 0)
            {
                bool wipe = e.Outcome == "wipe";
                var st = Caps(outcome, 11.5, wipe ? Coral : GoldHi);
                right.Children.Add(new Border
                {
                    Child = st, CornerRadius = new CornerRadius(2), Padding = new Thickness(7, 0, 7, 1), Background = Frozen(Mod.A(AbyssC, .7)), BorderThickness = new Thickness(1),
                    BorderBrush = wipe ? Frozen(Mod.A(CoralC, .6)) : Leaf, Margin = new Thickness(0, 1, 9, 0), VerticalAlignment = VerticalAlignment.Center, Tag = "outcome",
                });
            }
            var clock = F(Clock(e, now), 19, GoldHi); clock.Tag = "fight-clock";
            right.Children.Add(clock);
        }
        Grid.SetColumn(right, 1); head.Children.Add(right);
        _head = head;
        _fightBody.Children.Add(head);
        if (e.Waiting)
        {
            var wt = Tt(e.ReadyText, 12.5, Soft, FontWeights.Medium); wt.Margin = new Thickness(0, 5, 0, 1); wt.TextWrapping = TextWrapping.Wrap;
            _fightBody.Children.Add(wt);
            if (PartyBeforeHit(e)) { _fightBody.Children.Add(new Border { Height = 7 }); PutRows(_fightBody, e, now, inner); }
        }
        else
        {
            if (HpOf(e) is { } hp)
            {
                // the boss's HP: a slim crimson bar in a gold hairline, ticks at the thirds, "47%  ·  1.84M left" at its right
                var hpLine = new Grid { Margin = new Thickness(0, 6, 0, 0) };
                hpLine.ColumnDefinitions.Add(new ColumnDefinition()); hpLine.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var track = new Grid { Height = 7, VerticalAlignment = VerticalAlignment.Center };
                track.Children.Add(new TacSketch((dc, s) =>
                {
                    var r = new Rect(.5, .5, s.Width - 1, s.Height - 1);
                    dc.DrawRectangle(Frozen(Mod.A(AbyssC, .85)), null, r);
                    double x1 = r.Width * Math.Clamp(hp, 0, 1);
                    if (x1 > .5)
                    {
                        var fill = new Rect(r.X, r.Y, x1, r.Height);
                        dc.DrawRectangle(Mod.Grad(false, (HpAC, 0), (HpBC, 1)), null, fill);
                        dc.DrawRectangle(Mod.Grad(true, (Mod.A(WarmC, .35), 0), (Mod.A(WarmC, 0), .6)), null, fill);
                        dc.DrawRectangle(Frozen(Mod.A(Color.FromRgb(0xFF, 0xE0, 0xC8), .9)), null, new Rect(r.X + x1 - 1, r.Y, 1, r.Height));
                    }
                    for (int k = 1; k <= 2; k++) { double x = Math.Round(r.X + r.Width * k / 3); dc.DrawRectangle(Frozen(Mod.A(AbyssC, .75)), null, new Rect(x - 1, r.Y, 2, r.Height)); }
                    dc.DrawRectangle(null, Frz(new Pen(Frozen(Mod.A(GoldHiC, .5)), 1)), r);
                }));
                HpGhost(track, e, hp, now, 0);
                hpLine.Children.Add(track);
                var hl = new TextBlock { FontFamily = Fig, FontSize = 11.5, Foreground = Soft, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
                System.Windows.Documents.Typography.SetNumeralAlignment(hl, FontNumeralAlignment.Tabular);
                hl.Inlines.Add(new System.Windows.Documents.Run($"{Math.Round(hp * 100):0}%") { FontWeight = FontWeights.SemiBold, Foreground = hp < .34 ? Frozen(Color.FromRgb(0xFF, 0x9A, 0x90)) : Ivory });
                var left = HpLeftText(e);
                if (left.Length > 0) hl.Inlines.Add(new System.Windows.Documents.Run("  ·  " + left) { FontWeight = FontWeights.Normal });
                hl.Tag = "hp-text";
                Grid.SetColumn(hl, 1); hpLine.Children.Add(hl);
                _fightBody.Children.Add(hpLine);
                _fightBody.Children.Add(new Border { Height = 8 });
            }
            else _fightBody.Children.Add(new Border { Height = 8 });
            PutRows(_fightBody, e, now, inner);
            if (e.HasDamage)
            {
                _fightBody.Children.Add(Rule(new Thickness(0, 4, 0, 3)));
                _fightBody.Children.Add(Foot(e, now));
            }
        }
        _fight.Visibility = Visibility.Visible;
        PlaceTimers();
        Leader(e);
    }

    /// <summary>A new leader in the fight seen live: the wings unfurl round their numeral and a glint runs along their bar.</summary>
    void Leader(Encounter e)
    {
        var lead = Meter?.Members.FirstOrDefault(m => m.Rank == 1 && !m.CpOnly && !m.Pvp);
        long id = lead?.Id ?? 0;
        bool same = e.Id == _leadFight;
        if (same && id != 0 && _leadId != 0 && id != _leadId && e.Live && Moves && Meter?.RowOf(id) is ElyosRow row) { row.Crown(); row.Glint(80); }
        _leadFight = e.Id;
        if (id != 0) _leadId = id;
    }

    /// <summary>The glint down the bars: each row's in turn, top to bottom (<paramref name="step"/> ms apart).</summary>
    void GlintRows(double step = 70)
    {
        if (Meter is not { } m) return;
        int i = 0;
        foreach (var mm in m.Members) if (m.RowOf(mm.Id) is ElyosRow r) r.Glint(60 + step * i++);
    }

    // ================================================================ the moments

    /// <summary>The glint across the Buddy's plate at <paramref name="t"/> (0 to 1; outside it, none): its band moved and faded, nothing redrawn.</summary>
    void LineSheen(double t)
    {
        if (t is <= 0 or >= 1) { _lineSheen.Opacity = 0; return; }
        double w = _line.ActualWidth;
        _lineSheenShift.X = -70 + (w + 110) * t;
        _lineSheen.Opacity = Math.Sin(Math.PI * t);
    }

    /// <summary>The glint's band fitted to the plate as it stands (once, when a moment starts).</summary>
    void FitSheen()
    {
        double w = _line.ActualWidth, h = _line.ActualHeight;
        _lineSheen.Height = h;
        _lineSheenHost.Clip = Plate(new Rect(.5, .5, Math.Max(1, w - 1), Math.Max(1, h - 1)), 6);
    }

    /// <summary>A pickup: a sunlit glint crosses the Buddy's plate and the worth floats in beside it on a small gold-edged plate, then fades.</summary>
    protected override void Pickup(Pick p, bool held)
    {
        if (KinahHidden || _line.Visibility != Visibility.Visible) return;
        var worth = p.Worth.Length > 0 ? "+" + p.Worth.TrimStart('+') : p.Count;
        var words = new StackPanel { Margin = new Thickness(9, 2, 10, 3) };
        words.Children.Add(F(worth, 15, p.Worth.Length > 0 ? Frozen(Mod.Mix(MintC2, IvoryC, .25)) : Ivory));
        var nm = Tt(p.Name.Length > 13 ? p.Name[..12].TrimEnd() + "…" : p.Name, 9.5, Soft, FontWeights.SemiBold); nm.Margin = new Thickness(0, -1, 0, 0);
        words.Children.Add(nm);
        var chip = new Border
        {
            Child = words, MaxWidth = 88, CornerRadius = new CornerRadius(2), Background = Frozen(Mod.A(NightC, .94)), BorderThickness = new Thickness(1),
            BorderBrush = Leaf,
        };
        var shift = new TranslateTransform();
        chip.RenderTransform = shift;
        chip.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(chip, 6); Canvas.SetTop(chip, -chip.DesiredSize.Height / 2);
        _lineAnchor.Children.Clear(); _lineAnchor.Children.Add(chip);
        _line.UpdateLayout(); FitSheen();
        Motion.Play(chip, 1900, t =>
        {
            LineSheen(Seg(t, 0, .55));
            double k = Out(Seg(t, .1, .3));
            shift.X = -10 * (1 - k);
            chip.Opacity = t < .82 ? k : 1 - Seg(t, .82, 1);
        }, () => { LineSheen(-1); _lineAnchor.Children.Remove(chip); });
    }

    /// <summary>A rare drop: its grade's light blooms out to the right with a flash, the tile popping in.</summary>
    protected override void RareDrop(Pick p, bool held)
    {
        if (_rareArt is not { } art || _rareFlashArt is not { } flash || art.RenderTransform is not ScaleTransform bloom) return;
        var tile = _rareTile;
        var sc = new ScaleTransform(1, 1);
        if (tile != null) { tile.RenderTransformOrigin = new Point(.5, .5); tile.RenderTransform = sc; }
        Motion.Play(art, 1500, t =>
        {
            bloom.ScaleX = Math.Max(.01, Out(Seg(t, 0, .45)));
            flash.Opacity = t < .2 ? Seg(t, 0, .2) : 1 - Seg(t, .2, .8);
            sc.ScaleX = sc.ScaleY = .65 + .35 * Back(Seg(t, .05, .4), 2.2);
        }, () => { bloom.ScaleX = 1; flash.Opacity = 0; sc.ScaleX = sc.ScaleY = 1; });
    }

    /// <summary>Your best passed: the full halo fades in over the arc, a ring of light flares out from it, and a glint crosses the plate.</summary>
    protected override void BestPassed(bool held)
    {
        if (_ringFull is not { } full || _ringFlare is not { } flare || flare.RenderTransform is not ScaleTransform fs) return;
        _line.UpdateLayout(); FitSheen();
        Motion.Play(full, 1800, t =>
        {
            full.Opacity = Out(Seg(t, 0, .4));
            double f = Seg(t, .35, .85);
            fs.ScaleX = fs.ScaleY = .8 + .2 * Out(f);
            flare.Opacity = f is > 0 and < 1 ? 1 - f : 0;
            LineSheen(Seg(t, .35, .9));
        }, () => { full.Opacity = 1; flare.Opacity = 0; LineSheen(-1); });
    }

    /// <summary>An entry opened: the first timer's chip glows gold round its edge and settles (the glow's opacity alone moves).</summary>
    protected override void EntryOpened(TimerBoard.Item i, bool held)
    {
        if (_firstChip is not { } chip) return;
        var glow = new DropShadowEffect { Color = GoldHiC, ShadowDepth = 0, BlurRadius = 10, Opacity = 0 };
        chip.Effect = glow;
        var edge = chip.BorderBrush;
        chip.BorderBrush = GoldHi;
        Motion.Play(chip, 1200, t => glow.Opacity = .9 * (t < .25 ? Seg(t, 0, .25) : 1 - Seg(t, .25, 1)),
            () => { chip.Effect = null; chip.BorderBrush = edge; });
    }

    /// <summary>A fight started: the plate rises in as it fades up, a glint runs down the bars and the wings unfurl round rank I.</summary>
    protected override void FightStarted(Encounter e, bool held)
    {
        Motion.Play(_fight, 520, t =>
        {
            double k = Out(t);
            _fightShift.Y = 8 * (1 - k);
            _fight.Opacity = k;
        }, () => { _fightShift.Y = 0; _fight.Opacity = 1; });
        GlintRows();
        if (Meter?.Members.FirstOrDefault(m => m.Rank == 1 && !m.CpOnly) is { } lead && Meter.RowOf(lead.Id) is ElyosRow row) { row.Crown(); _leadId = lead.Id; _leadFight = e.Id; }
    }

    /// <summary>
    /// A fight ended: a band laid over the fight's head (gold for a kill or a clear, coral for a wipe) with the outcome in the
    /// serif's capitals and the clock, then it lifts; a kill's glint runs down the bars.
    /// </summary>
    protected override void FightEnded(Encounter e, bool held)
    {
        if (e.Outcome is not ("kill" or "wipe" or "cleared")) return;
        bool wipe = e.Outcome == "wipe";
        var c = wipe ? CoralC : GoldHiC;
        if (_head is not { } head) return;
        _fight.UpdateLayout();
        var at = head.TranslatePoint(new Point(0, 0), _fight);
        double bw = Math.Max(0, _fight.ActualWidth - 2 * (Pad - 5)), bh = Math.Max(26, head.ActualHeight + 8);
        var band = new Grid { Width = bw, Height = bh };
        var bloom = new ScaleTransform(0, 1);
        var art = new TacSketch((dc, s) =>
        {
            dc.DrawRectangle(Mod.Grad(false, (Mod.A(AbyssC, .96), 0), (Mod.A(NightC, .94), 1)), null, new Rect(0, 0, s.Width, s.Height));
            dc.DrawRectangle(Mod.Grad(false, (Mod.A(c, .42), 0), (Mod.A(c, .12), .7), (Mod.A(c, 0), 1)), null, new Rect(0, 0, s.Width, s.Height));
            dc.DrawRectangle(Mod.Grad(false, (Mod.A(c, .95), 0), (Mod.A(c, .4), .7), (Mod.A(c, 0), 1)), null, new Rect(0, 0, s.Width, 1));
            dc.DrawRectangle(Mod.Grad(false, (Mod.A(c, .95), 0), (Mod.A(c, .4), .7), (Mod.A(c, 0), 1)), null, new Rect(0, s.Height - 1, s.Width, 1));
        }) { RenderTransform = bloom, RenderTransformOrigin = new Point(0, .5) };
        band.Children.Add(art);
        var words = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        words.Children.Add(Caps(OutcomeText(e), 19, wipe ? Coral : GoldHi));
        var ck = F(Clock(e.Duration(e.End ?? DateTime.UtcNow)), 13, Frozen(Mod.Mix(c, IvoryC, .55))); ck.Margin = new Thickness(10, 3, 0, 0);
        words.Children.Add(ck);
        band.Children.Add(words);
        Canvas.SetLeft(band, Pad - 5); Canvas.SetTop(band, at.Y - (bh - head.ActualHeight) / 2);
        _fightFx.Children.Clear(); _fightFx.Children.Add(band);
        if (!wipe) GlintRows(60);
        Motion.Play(_fightFx, 2400, t =>
        {
            bloom.ScaleX = Out(Seg(t, 0, .22));
            words.Opacity = t < .84 ? Seg(t, .08, .22) : 1 - Seg(t, .84, 1);
            band.Opacity = t < .84 ? 1 : 1 - Seg(t, .84, 1);
            head.Opacity = t < .84 ? 1 - Seg(t, 0, .12) : Seg(t, .84, 1);
        }, () => { _fightFx.Children.Clear(); head.Opacity = 1; });
    }
}
