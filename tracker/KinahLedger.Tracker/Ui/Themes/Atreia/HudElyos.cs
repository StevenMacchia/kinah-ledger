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

    // the room round the look: the plates' shadow and winged crest stay inside the window; 48 at the left for the leader's great
    // wing (and the feathers it sheds), 98 at the right for the other wing and the pickup's chip
    readonly Grid _root = new() { Margin = new Thickness(48, 24, 98, 10), HorizontalAlignment = HorizontalAlignment.Left };
    readonly StackPanel _body = new() { HorizontalAlignment = HorizontalAlignment.Left };
    double _opacity = 0.9;

    // the Buddy: its plate, the halo, the hour, the session and the timer chips
    readonly Grid _line = new() { HorizontalAlignment = HorizontalAlignment.Left };
    readonly TacSketch _lineArt;
    readonly StackPanel _lineBody = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(9, 6, 14, 6) };
    /// <summary>The Buddy's words: a dungeon run's head (inside the plate, over the figures) and the line itself.</summary>
    readonly StackPanel _lineStack = new() { HorizontalAlignment = HorizontalAlignment.Left };
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
    TacSketch? _ringRays;
    static readonly Transform RingRaysAt = Fz(new TranslateTransform(21, 21));
    double _share = -1;
    bool _beat;
    Border? _firstChip;
    // the sunlit glint over the Buddy's plate: one band (a frozen brush) moved by its transform, clipped to the plate when it starts
    readonly Canvas _lineSheenHost = new() { IsHitTestVisible = false };
    readonly System.Windows.Shapes.Rectangle _lineSheen = new() { Width = 72, Fill = SheenBrush, Opacity = 0, IsHitTestVisible = false };
    readonly TranslateTransform _lineSheenShift = new();
    static readonly LinearGradientBrush SheenBrush = Lin(new Point(0, 0), new Point(1, 0), (Mod.A(WarmC, 0), 0), (Mod.A(WarmC, .4), .4), (Mod.A(Colors.White, .62), .55), (Mod.A(WarmC, 0), 1));

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
    readonly StackPanel _fightBody = new() { Margin = new Thickness(Pad, 10, Pad, 10) };
    readonly Border _timersInFight = new() { Margin = new Thickness(0, 0, 0, 7), HorizontalAlignment = HorizontalAlignment.Left };
    // the fight's moments (the beam, the gold, the burst, the outcome's band), kept inside the fight's box
    readonly Canvas _fightFx = new() { IsHitTestVisible = false, ClipToBounds = true };
    readonly TranslateTransform _fightShift = new();
    /// <summary>The fight plate's punch (a kill's hit), under its shift.</summary>
    readonly ScaleTransform _fightPunch = new();
    /// <summary>The moments' layer over the whole look (AtreiaFx): each moment a stage of its own on it, clipped to the window.</summary>
    readonly Canvas _fx = new() { IsHitTestVisible = false };
    FrameworkElement? _head;
    // the leader's great wings: behind the plate, rooted on its edges at the leader's row, rising to its top edge
    readonly TacSketch _bigWingL, _bigWingR;
    readonly ScaleTransform _bigWingLScale = new(), _bigWingRScale = new();
    readonly RotateTransform _bigWingLTurn = new(), _bigWingRTurn = new();
    double _wingY = double.NaN;
    readonly TacSketch _lineWings;
    string _leadFight = "";
    long _leadId;

    public HudElyos() : base(bare: true)
    {
        // the look's own rows (Ui/Themes/ThemeRows.cs): gold-edged class bars on the plate
        UseRows(ElyosRow.Pitch, m => new ElyosRow(), 15, GapLine);
        // the crest only with nothing over the plate (a run's head or the timer chips sit there): nothing is ever drawn over words
        _lineArt = new TacSketch((dc, s) => PlateArt(dc, s, _dim ? .55 : _opacity, crest: !LineCovered, dim: _dim));
        _lineSheen.RenderTransform = new TransformGroup { Children = { new SkewTransform(-28, 0), _lineSheenShift } };
        _lineSheenHost.Children.Add(_lineSheen);
        _lineWings = new TacSketch(DrawLineWings);
        _line.Children.Add(_lineWings);
        _lineStack.Children.Add(_runHeadLit); _lineStack.Children.Add(_lineBody);
        _line.Children.Add(_lineArt); _line.Children.Add(_lineSheenHost); _line.Children.Add(_lineStack); _line.Children.Add(_lineAnchor);
        _fightArt = new TacSketch((dc, s) => PlateArt(dc, s, _opacity, crest: true, band: 4));
        _bigWingL = new TacSketch((dc, s) => DrawBigWing(dc, s, false)) { RenderTransform = new TransformGroup { Children = { _bigWingLScale, _bigWingLTurn } }, Opacity = 0 };
        _bigWingR = new TacSketch((dc, s) => DrawBigWing(dc, s, true)) { RenderTransform = new TransformGroup { Children = { _bigWingRScale, _bigWingRTurn } }, Opacity = 0 };
        _fight.Children.Add(_fightArt); _fight.Children.Add(_fightBody); _fight.Children.Add(_fightFx);
        // the wings are laid out after the rows (they read where the leader's row stands as they are drawn), drawn behind the plate
        Panel.SetZIndex(_bigWingL, -1); Panel.SetZIndex(_bigWingR, -1);
        _fight.Children.Add(_bigWingL); _fight.Children.Add(_bigWingR);
        _fight.RenderTransform = new TransformGroup { Children = { _fightPunch, _fightShift } };
        _body.Children.Add(_fight); _body.Children.Add(_timersUp); _body.Children.Add(_line); _body.Children.Add(_rare); _body.Children.Add(_note);
        _root.Children.Add(_body); _root.Children.Add(_fx);
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
    static readonly Pen InnerPen = Frz(new Pen(Frozen(Mod.A(Color.FromRgb(0x7F, 0xC4, 0xFF), .55)), .9));
    static readonly Pen InnerGlow = Frz(new Pen(Frozen(Mod.A(Color.FromRgb(0x7F, 0xC4, 0xFF), .14)), 3));
    /// <summary>The sky in a plate's head: ivory light at its top edge fading through azure.</summary>
    static readonly LinearGradientBrush HeadSky = Lin(new Point(0, 0), new Point(0, 1), (Mod.A(Color.FromRgb(0xFF, 0xF8, 0xEA), .34), 0), (Mod.A(Color.FromRgb(0x7F, 0xC4, 0xFF), .3), .42), (Mod.A(Color.FromRgb(0x7F, 0xC4, 0xFF), 0), 1));
    /// <summary>The pearl band in a plate's frame: white at its top fading to pearl.</summary>
    static readonly LinearGradientBrush PearlBand = Lin(new Point(0, 0), new Point(0, 1), (Color.FromRgb(0xFF, 0xFD, 0xF6), 0), (Color.FromRgb(0xEE, 0xE7, 0xD7), .5), (Color.FromRgb(0xD6, 0xCB, 0xB2), 1));
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
        Color? edge = null, bool dim = false, double band = 0, double crestScale = 1)
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
        if (!dim) { dc.DrawRectangle(HeadSky, null, new Rect(0, 0, s.Width, Math.Min(s.Height * .6, 54))); dc.DrawRectangle(SkyGlow, null, new Rect(0, 0, s.Width, Math.Min(s.Height, 80))); }
        // a line of light along the top edge
        dc.DrawRectangle(Mod.Grad(false, (Mod.A(IvoryC, 0), 0), (Mod.A(IvoryC, .2), .3), (Mod.A(IvoryC, .2), .7), (Mod.A(IvoryC, 0), 1)), null, new Rect(cut, 1.5, Math.Max(0, s.Width - 2 * cut), 1));
        dc.Pop();
        // the frames
        dc.DrawGeometry(null, edge is { } ec ? Frz(new Pen(Mod.Grad(false, (Mod.Mix(ec, GoldHiC, .35), 0), (ec, .5), (Mod.Mix(ec, GoldLoC, .3), 1)), 1)) : LeafPen, shape);
        if (band > 0)
        {
            // the frame's pearl band between gold hairlines, the sky's line inside it
            double h = band / 2 + .5;
            dc.DrawGeometry(null, Frz(new Pen(PearlBand, band)), Plate(new Rect(r.X + h, r.Y + h, r.Width - 2 * h, r.Height - 2 * h), Math.Max(1, cut - h * .6)));
            double g2 = band + 1;
            dc.DrawGeometry(null, LeafPen, Plate(new Rect(r.X + g2, r.Y + g2, r.Width - 2 * g2, r.Height - 2 * g2), Math.Max(1, cut - g2 * .6)));
            double a3 = band + 3;
            var inner = Plate(new Rect(r.X + a3, r.Y + a3, r.Width - 2 * a3, r.Height - 2 * a3), Math.Max(1, cut - a3 * .6));
            dc.DrawGeometry(null, InnerGlow, inner); dc.DrawGeometry(null, InnerPen, inner);
        }
        else if (s.Height > 26) { var inner = Plate(new Rect(r.X + 3, r.Y + 3, r.Width - 6, r.Height - 6), Math.Max(1, cut - 1.2)); dc.DrawGeometry(null, InnerGlow, inner); dc.DrawGeometry(null, InnerPen, inner); }
        // the cut corners: a gold diamond on each, gold strokes along the edges from it
        double L = r.Left, T = r.Top, R = r.Right, B = r.Bottom, len = Math.Min(12, Math.Min(s.Width, s.Height) / 4);
        dc.DrawRectangle(FadeR, null, new Rect(L + cut, T - .3, len, 1.6)); dc.DrawRectangle(FadeD, null, new Rect(L - .3, T + cut, 1.6, len));
        dc.DrawRectangle(FadeL, null, new Rect(R - cut - len, T - .3, len, 1.6)); dc.DrawRectangle(FadeD, null, new Rect(R - 1.3, T + cut, 1.6, len));
        dc.DrawRectangle(FadeR, null, new Rect(L + cut, B - 1.3, len, 1.6)); dc.DrawRectangle(FadeU, null, new Rect(L - .3, B - cut - len, 1.6, len));
        dc.DrawRectangle(FadeL, null, new Rect(R - cut - len, B - 1.3, len, 1.6)); dc.DrawRectangle(FadeU, null, new Rect(R - 1.3, B - cut - len, 1.6, len));
        double d = cut / 2, dr = Math.Min(2.6, cut * .45);
        Diamond(dc, new Point(L + d, T + d), dr); Diamond(dc, new Point(R - d, T + d), dr);
        Diamond(dc, new Point(L + d, B - d), dr); Diamond(dc, new Point(R - d, B - d), dr);
        if (crest && s.Width > 110)
        {
            // the winged crest: a gold sun on the top edge's middle, its wings along the edge, a fine gold line flaring on from each
            double cx = Math.Round(s.Width / 2);
            dc.DrawRectangle(FadeL, null, new Rect(cx - 66, T - .3, 34, 1.6));
            dc.DrawRectangle(FadeR, null, new Rect(cx + 32, T - .3, 34, 1.6));
            dc.PushTransform(new MatrixTransform(crestScale, 0, 0, crestScale, cx, T + .5));
            dc.DrawDrawing(CrestWings);
            dc.Pop();
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
        _lineBody.Margin = new Thickness(9, 6, 14, 6);
        if (rs != null) DrawRun(rs);
        else if (!HasHour(f))
        {
            // and the figures there are under the words (in the Abyss: kills and Kinah): information, never an empty plate
            var w = Tt(Waiting(f.Abyss), 12.5, Soft, FontWeights.SemiBold);
            var sub = Sub(f, false);
            bool more = sub.Inlines.Count > 0;
            var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(mark ? 0 : 4, more ? 3 : 6, 4, more ? 3 : 6) };
            words.Children.Add(w);
            if (more) words.Children.Add(sub);
            _lineBody.Children.Add(words);
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
                // the halo's rays for your best passed: the kill's burst, small, turning round the ring (hidden at rest)
                _ringRays = new TacSketch((dc, _) => { dc.PushTransform(RingRaysAt); dc.DrawDrawing(BurstRays); dc.Pop(); }) { Opacity = 0, RenderTransform = new ScaleTransform(.1, .1, 21, 21) };
                ring.Children.Insert(0, _ringRays);
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
            // the drop's burst behind its tile (hidden at rest)
            _rareRays = new TacSketch((dc, _) => { dc.PushTransform(RareRaysAt); dc.DrawDrawing(BurstRays); dc.Pop(); })
            { Opacity = 0, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, RenderTransform = new ScaleTransform(.05, .05, 19, 18) };
            _rare.Children.Add(_rareRays);
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

    TacSketch? _rareArt, _rareFlashArt, _rareRays;
    static readonly Transform RareRaysAt = Fz(new TranslateTransform(19, 18));
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
        LineAboveChanged();
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
        PlaceWings();
        Leader(e);
    }

    /// <summary>
    /// The great wings shown while someone leads with damage (the meter's top row), redrawn with each fight draw: where the
    /// leader's row stands is read as they are drawn (layout done by then), so no layout is forced here.
    /// </summary>
    void PlaceWings()
    {
        _wingsOn = Meter is { } m && m.Members.FirstOrDefault() is { Rank: 1, CpOnly: false, Pvp: false } && _fight.Visibility == Visibility.Visible;
        if (!Motion.Playing(_bigWingL)) _bigWingL.Opacity = _bigWingR.Opacity = _wingsOn ? 1 : 0;
        _bigWingL.InvalidateVisual(); _bigWingR.InvalidateVisual();
    }

    bool _wingsOn;

    /// <summary>
    /// A great wing (the right one when <paramref name="right"/>): the frozen wing rooted 3 px inside the plate's edge at the
    /// middle of the leader's row, scaled so its tip reaches 6 px over the plate's top (kept at least 72% as wide as tall a
    /// scale), shown only outside the plate, as if it spread from behind it.
    /// </summary>
    void DrawBigWing(DrawingContext dc, Size s, bool right)
    {
        if (!_wingsOn || s.Width < 40 || Meter is not { } m || !m.IsDescendantOf(_fight)) return;
        double y = m.TranslatePoint(new Point(0, 0), _fight).Y + ElyosRow.H / 2;
        if (y < 20) return;
        _wingY = y;
        var box = WingBox;
        // the tips rise 18 px over the plate's top; the left wing kept to its 44 px of room, the right spread wide in its 98
        double sy = Math.Clamp((y + 18) / -box.Top, .3, 1.25), sx = Math.Max(sy, .72);
        sx = right ? Math.Min(1.6 * sx, 84 / -box.Left) : Math.Min(sx, 42 / -box.Left);
        double x0 = right ? s.Width - 3 : 3;
        var sc = right ? _bigWingRScale : _bigWingLScale;
        var tu = right ? _bigWingRTurn : _bigWingLTurn;
        if (sc.CenterX != x0 || sc.CenterY != y) { sc.CenterX = x0; sc.CenterY = y; tu.CenterX = x0; tu.CenterY = y; }
        dc.PushClip(new RectangleGeometry(right ? new Rect(s.Width - .5, -60, 140, s.Height + 120) : new Rect(-140, -60, 140.5, s.Height + 120)));
        dc.PushTransform(new MatrixTransform(sx, 0, 0, sy, x0, y));
        dc.DrawDrawing(right ? WingR : WingL);
        dc.Pop(); dc.Pop();
    }

    /// <summary>Whether words sit at the Buddy's top edge: the timer chips moved up over it, or a run's head inside its top.</summary>
    bool LineCovered => _runHeadLit.Visibility == Visibility.Visible || _timersUp.Visibility == Visibility.Visible;

    bool _lineCovered;

    /// <summary>The plate and its wings drawn again when what sits over the plate comes or goes (the crest shown or left out).</summary>
    void LineAboveChanged()
    {
        if (LineCovered == _lineCovered) return;
        _lineCovered = LineCovered;
        _lineArt.InvalidateVisual(); _lineWings.InvalidateVisual();
    }

    /// <summary>The Buddy's small wings: the same wing, a third the size, behind each end of its plate.</summary>
    void DrawLineWings(DrawingContext dc, Size s)
    {
        if (s.Width < 60 || s.Height < 20) return;
        var box = WingBox;
        // the tips rise over the plate's top only with nothing above it, else they stop at its edge
        double sy = Math.Min(.55, (LineCovered ? s.Height - 7 : s.Height - 2) / -box.Top), sx = .62, y = s.Height - 6;
        dc.PushClip(new RectangleGeometry(new Rect(-80, -40, 80.5, s.Height + 80)));
        dc.PushTransform(new MatrixTransform(sx, 0, 0, sy, 3, y)); dc.DrawDrawing(WingL); dc.Pop();
        dc.Pop();
        dc.PushClip(new RectangleGeometry(new Rect(s.Width - .5, -40, 80, s.Height + 80)));
        dc.PushTransform(new MatrixTransform(sx, 0, 0, sy, s.Width - 3, y)); dc.DrawDrawing(WingR); dc.Pop();
        dc.Pop();
    }

    /// <summary>The great wings unfurl (after <paramref name="delay"/> ms): they spread out from behind the plate's edges past the row with a small overshoot.</summary>
    void UnfurlWings(double delay = 0)
    {
        if (!_wingsOn) return;
        double total = delay + 1100;
        Motion.Play(_bigWingL, total, q =>
        {
            double p = Math.Clamp((q * total - delay) / 1100, 0, 1);
            double k = Back(Seg(p, 0, .5), 1.5);
            _bigWingLScale.ScaleX = _bigWingRScale.ScaleX = .05 + .95 * k;
            _bigWingLScale.ScaleY = _bigWingRScale.ScaleY = .25 + .75 * Math.Min(1.04, k);
            _bigWingL.Opacity = _bigWingR.Opacity = Math.Clamp(p / .15, 0, 1);
        }, () => { _bigWingLScale.ScaleX = _bigWingRScale.ScaleX = _bigWingLScale.ScaleY = _bigWingRScale.ScaleY = 1; _bigWingL.Opacity = _bigWingR.Opacity = 1; });
    }

    /// <summary>A kill's flare of the wings: they swing out 12 degrees and up a touch, then settle back.</summary>
    void FlareWings()
    {
        if (!_wingsOn) return;
        Motion.Play(_bigWingL, 900, p =>
        {
            double k = p < .3 ? Out(p / .3) : 1 - Out((p - .3) / .7);
            _bigWingLTurn.Angle = -12 * k; _bigWingRTurn.Angle = 12 * k;
            _bigWingLScale.ScaleY = _bigWingRScale.ScaleY = 1 + .06 * k;
        }, () => { _bigWingLTurn.Angle = _bigWingRTurn.Angle = 0; _bigWingLScale.ScaleY = _bigWingRScale.ScaleY = 1; });
    }

    /// <summary>A new leader in the fight seen live: the wings unfurl round their numeral and a glint runs along their bar.</summary>
    void Leader(Encounter e)
    {
        var lead = Meter?.Members.FirstOrDefault(m => m.Rank == 1 && !m.CpOnly && !m.Pvp);
        long id = lead?.Id ?? 0;
        bool same = e.Id == _leadFight;
        if (same && id != 0 && _leadId != 0 && id != _leadId && e.Live && Moves && Meter?.RowOf(id) is ElyosRow row) { row.Crown(); row.Glint(80); UnfurlWings(); }
        _leadFight = e.Id;
        if (id != 0) _leadId = id;
    }

    /// <summary>The glint down the bars: each row's in turn, top to bottom (<paramref name="step"/> ms apart).</summary>
    void GlintRows(double step = 70, double first = 60)
    {
        if (Meter is not { } m) return;
        int i = 0;
        foreach (var mm in m.Members) if (m.RowOf(mm.Id) is ElyosRow r) r.Glint(first + step * i++);
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
        var shift = new TranslateTransform(); var pop = new ScaleTransform(1, 1);
        chip.RenderTransform = new TransformGroup { Children = { pop, shift } };
        chip.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(chip, 6); Canvas.SetTop(chip, -chip.DesiredSize.Height / 2);
        _lineAnchor.Children.Clear(); _lineAnchor.Children.Add(chip);
        _line.UpdateLayout(); FitSheen();
        // the newest tile in the strip pops (1.15 and back in 0.22 s): the look's own copy of the strip, only scaled
        var slot = AtreiaFx.Pop(AtreiaFx.NewestTile(_line), 0, .116, .15, middle: true);
        Motion.Play(chip, 1900, t =>
        {
            slot(t);
            LineSheen(Seg(t, 0, .45));
            double k = Back(Seg(t, .06, .24), 3);
            shift.X = -14 * (1 - k);
            pop.ScaleX = pop.ScaleY = AtreiaFx.Punch(t, .06, .4, 0, .05);
            chip.Opacity = t < .82 ? Math.Min(1, Seg(t, .06, .16)) : 1 - Seg(t, .82, 1);
        }, () => { LineSheen(-1); _lineAnchor.Children.Remove(chip); });
    }

    /// <summary>A rare drop: its grade's light blooms out to the right with a flash, the tile popping in.</summary>
    protected override void RareDrop(Pick p, bool held)
    {
        if (_rareArt is not { } art || _rareFlashArt is not { } flash || art.RenderTransform is not ScaleTransform bloom) return;
        var tile = _rareTile;
        var sc = new ScaleTransform(1, 1);
        if (tile != null) { tile.RenderTransformOrigin = new Point(.5, .5); tile.RenderTransform = sc; }
        // a gift from on high: a pillar of divine light in the grade's colour falls onto the tile, which ducks and punches up,
        // a ring of that light goes out, motes of light and a few golden feathers rise off it and drift down
        var c = ColorOf(p.Color, p.Grade);
        var st = AtreiaFx.Stage(_fx, _root);
        var parts = new List<Action<double>>();
        if (AtreiaFx.At(tile, _fx, new Point(13, 13)) is { } at)
        {
            parts.Add(AtreiaFx.Pillar(st, at + new Vector(0, 6), 40, 90, Mod.Mix(c, WarmC, .25), 0, .15, .58));
            parts.Add(AtreiaFx.Bloom(st, at, 100, 62, AtreiaFx.Glow(c, .9, IvoryC), .12, .18, .6));
            parts.Add(AtreiaFx.Rays(st, at, 14, 12, 40, 3.4, i => i % 2 == 0 ? Mod.Mix(c, IvoryC, .4) : GoldHiC, .14, .22, .7, turn: 18, max: .9));
            var bits = new AtreiaFx.Bits(st);
            bits.Burst(6, AtreiaFx.Star, i => AtreiaFx.Br(i % 2 == 0 ? IvoryC : Mod.Mix(c, IvoryC, .3)), at, 34, 4.5, .15, .6, fall: -8, dir: -60, spread: 200, r0: 10, spin: 90, seed: 41);
            bits.Burst(5, AtreiaFx.Feather, i => AtreiaFx.Br(i % 2 == 0 ? GoldHiC : IvoryC, .95), at, 30, 7, .17, .8, fall: 30, dir: -70, spread: 160, r0: 8, spin: 60, seed: 43, sway: 4);
            parts.Add(bits.At);
        }
        AtreiaFx.Play(_fx, st, 1700, t =>
        {
            AtreiaFx.All(parts, t);
            bloom.ScaleX = Math.Max(.01, Out(Seg(t, .15, .55)));
            flash.Opacity = AtreiaFx.Flash(t, .14, .22, .85);
            sc.ScaleX = sc.ScaleY = AtreiaFx.Punch(t, .05, .62, .2, .25);
            if (_rareRays is { RenderTransform: ScaleTransform rs } rr)
            {
                double b = Seg(t, .14, .7);
                rs.ScaleX = rs.ScaleY = .05 + .17 * Out(b);
                rr.Opacity = b is > 0 and < 1 ? Math.Sin(Math.PI * Math.Pow(b, .6)) : 0;
            }
        }, () => { bloom.ScaleX = 1; flash.Opacity = 0; sc.ScaleX = sc.ScaleY = 1; if (_rareRays != null) _rareRays.Opacity = 0; });
    }

    /// <summary>Your best passed: the full halo fades in over the arc, a ring of light flares out from it, and a glint crosses the plate.</summary>
    protected override void BestPassed(bool held)
    {
        if (_ringFull is not { } full || _ringFlare is not { } flare || flare.RenderTransform is not ScaleTransform fs) return;
        _line.UpdateLayout(); FitSheen();
        // the halo punching as it lights, a burst of sunlight behind it, motes of light and golden feathers flying off
        var st = AtreiaFx.Stage(_fx, _root);
        var extra = new List<Action<double>>();
        if (full.Parent is FrameworkElement ring && AtreiaFx.At(ring, _fx, new Point(21, 21)) is { } rc)
        {
            var halo = AtreiaFx.ScaleOn(ring, new Point(21, 21));
            extra.Add(t => halo.ScaleX = halo.ScaleY = AtreiaFx.Punch(t, 0, .92, .08, .24));
            extra.Add(AtreiaFx.Pop(AtreiaFx.Hour(_lineBody), .3, .46, .08));
            extra.Add(AtreiaFx.Bloom(st, rc, 80, 80, AtreiaFx.Glow(WarmC, .95, IvoryC), .3, .36, .7));
            extra.Add(AtreiaFx.Rays(st, rc, 16, 22, 34, 3.6, i => i % 2 == 0 ? GoldHiC : WarmC, .3, .38, .8, turn: 20, max: .95));
            // the Buddy's wings beat twice with the punch (their drawing only scaled up and down from the plate's middle)
            if (_lineWings.ActualHeight > 0)
            {
                var beat = AtreiaFx.ScaleOn(_lineWings, new Point(_lineWings.ActualWidth / 2, _lineWings.ActualHeight / 2));
                extra.Add(t => { double u = Seg(t, .3, .7); beat.ScaleY = u is > 0 and < 1 ? 1 + .14 * Math.Sin(2 * Math.PI * 2 * u) * (1 - u) : 1; });
            }
            var bits = new AtreiaFx.Bits(st);
            bits.Burst(6, AtreiaFx.Star, i => AtreiaFx.Br(i % 2 == 0 ? IvoryC : GoldHiC), rc, 26, 4.2, .32, .5, fall: -6, dir: 0, spread: 240, r0: 20, spin: 90, seed: 47);
            bits.Burst(5, AtreiaFx.Feather, i => AtreiaFx.Br(i % 2 == 0 ? GoldHiC : IvoryC, .95), rc, 24, 7, .34, .62, fall: 22, dir: 0, spread: 200, r0: 18, spin: 60, seed: 53, sway: 4);
            extra.Add(bits.At);
        }
        AtreiaFx.Play(_fx, st, 1800, t => AtreiaFx.All(extra, t));
        Motion.Play(full, 1800, t =>
        {
            full.Opacity = Out(Seg(t, 0, .4));
            double f = Seg(t, .3, .8);
            fs.ScaleX = fs.ScaleY = 1;
            flare.Opacity = 0;   // (the thin ring spilled past the plate: the sun's rays carry the burst)
            if (_ringRays is { RenderTransform: ScaleTransform rs } rr)
            {
                double b = Seg(t, .25, .85);
                rs.ScaleX = rs.ScaleY = .1 + .18 * Out(b);
                rr.Opacity = b is > 0 and < 1 ? Math.Sin(Math.PI * Math.Pow(b, .6)) : 0;
            }
            LineSheen(Seg(t, .3, .85));
        }, () => { full.Opacity = 1; flare.Opacity = 0; if (_ringRays != null) _ringRays.Opacity = 0; LineSheen(-1); });
    }

    /// <summary>
    /// An entry opened: the sun rises behind the chip: a burst of golden rays turns out from it, the chips duck and punch
    /// out, warm light flashes on it, a gold ring ripples off its edge and motes of light rise from it; then it rests.
    /// </summary>
    protected override void EntryOpened(TimerBoard.Item i, bool held)
    {
        if (_firstChip is not { } chip || AtreiaFx.Box(chip, _fx) is not { } r) return;
        var st = AtreiaFx.Stage(_fx, _root);
        var pop = AtreiaFx.ScaleOn(_timers, chip.TranslatePoint(new Point(chip.ActualWidth / 2, chip.ActualHeight / 2), _timers));
        var c = new Point(r.X + r.Width / 2, r.Y + r.Height / 2);
        var parts = new List<Action<double>>
        {
            AtreiaFx.Rays(st, c, 16, r.Height * .5, r.Width * .5 + 18, 3.6, k => k % 2 == 0 ? GoldHiC : WarmC, .06, .18, .7, turn: 14, max: .85, s0: .7),
            AtreiaFx.Bloom(st, c, r.Width + 40, r.Height + 30, AtreiaFx.Glow(WarmC, .85, IvoryC), .1, .18, .58, s0: .5),
            AtreiaFx.Ripple(st, r, 2, AtreiaFx.Br(GoldHiC), 1.8, 6, .14, .45),
        };
        var bits = new AtreiaFx.Bits(st);
        bits.Burst(8, AtreiaFx.Star, k => AtreiaFx.Br(k % 2 == 0 ? IvoryC : GoldHiC), c, 16, 3.8, .14, .66, fall: -10, dir: -90, spread: 300, r0: r.Width / 2, spin: 90, seed: 59, squash: .42);
        parts.Add(bits.At);
        AtreiaFx.Play(_fx, st, 1150, t =>
        {
            pop.ScaleX = pop.ScaleY = AtreiaFx.Punch(t, 0, .72, .06, .08);
            AtreiaFx.All(parts, t);
        }, () => pop.ScaleX = pop.ScaleY = 1);
    }

    /// <summary>
    /// A fight started: a beam of light (a white core, gold falling off to 40 px) drops onto the plate's crest and flares there,
    /// then the plate opens out from the beam to both sides behind a molten edge (a clip on the plate that widens, never a
    /// fill over the words), the words fading up once it is open; a glint runs down the bars and the wings unfurl. Transforms, opacity and one clip only.
    /// </summary>
    protected override void FightStarted(Encounter e, bool held)
    {
        _fight.UpdateLayout();
        double w = _fight.ActualWidth, h = _fight.ActualHeight;
        _fightFx.Children.Clear();
        if (w > 20 && h > 20)
        {
            double cx = Math.Round(w / 2);
            var beamScale = new ScaleTransform(1, 0);
            var beam = new System.Windows.Shapes.Rectangle { Width = 40, Height = h, Fill = BeamBrush, OpacityMask = BeamFade, RenderTransform = beamScale, RenderTransformOrigin = new Point(.5, 0) };
            Canvas.SetLeft(beam, cx - 20);
            var halo = new System.Windows.Shapes.Rectangle { Width = 150, Height = h, Fill = BeamHalo, OpacityMask = BeamFade, RenderTransform = beamScale, RenderTransformOrigin = new Point(.5, 0) };
            Canvas.SetLeft(halo, cx - 75);
            _fightFx.Children.Add(halo);
            var flareScale = new ScaleTransform(.2, .2, 40, 20);
            var flare = new System.Windows.Shapes.Ellipse { Width = 80, Height = 40, Fill = FlareBrush, Opacity = 0, RenderTransform = flareScale };
            Canvas.SetLeft(flare, cx - 40); Canvas.SetTop(flare, -14);
            // the molten edges that lead the opening, one each way
            var edgeL = new System.Windows.Shapes.Rectangle { Width = 3, Height = h, Fill = MoltenBrush, Opacity = 0 };
            var edgeR = new System.Windows.Shapes.Rectangle { Width = 3, Height = h, Fill = MoltenBrush, Opacity = 0 };
            var eL = new TranslateTransform(cx, 0); var eR = new TranslateTransform(cx, 0);
            edgeL.RenderTransform = eL; edgeR.RenderTransform = eR;
            _fightFx.Children.Add(beam); _fightFx.Children.Add(edgeL); _fightFx.Children.Add(edgeR); _fightFx.Children.Add(flare);
            var open = new RectangleGeometry(new Rect(cx, -20, 0, h + 40));
            _fightArt.Clip = open; _fightBody.Opacity = 0;
            Motion.Play(_fightFx, 1000, t =>
            {
                double drop = Seg(t, 0, .12), wipe = Out(Seg(t, .12, .36));
                beamScale.ScaleY = Out(drop);
                beam.Opacity = halo.Opacity = t < .18 ? 1 : 1 - Seg(t, .18, .3);
                double f = Seg(t, .08, .4);
                flareScale.ScaleX = flareScale.ScaleY = .2 + .9 * Out(f);
                flare.Opacity = f is > 0 and < 1 ? Math.Sin(Math.PI * Math.Pow(f, .5)) : 0;
                double half = (w / 2 + 2) * wipe;
                open.Rect = new Rect(cx - half, -20, 2 * half, h + 40);
                eL.X = cx - half - 1.5; eR.X = cx + half - 1.5;
                edgeL.Opacity = edgeR.Opacity = wipe is > 0 and < 1 ? 1 - wipe * .6 : 0;
                _fightBody.Opacity = Seg(t, .26, .36);   // only once the plate is open wide (no words ghosting past the opening)
                _fightShift.Y = 5 * (1 - Out(Seg(t, .14, .5)));
            }, () => { _fightFx.Children.Clear(); _fightArt.Clip = null; _fightBody.Opacity = 1; _fightShift.Y = 0; });
        }
        GlintRows(70, 450);
        if (Meter?.Members.FirstOrDefault(m => m.Rank == 1 && !m.CpOnly) is { } lead && Meter.RowOf(lead.Id) is ElyosRow row) { row.Crown(); UnfurlWings(200); _leadId = lead.Id; _leadFight = e.Id; }
    }

    static readonly RadialGradientBrush FlareBrush = Fz(new RadialGradientBrush
    {
        GradientStops = { new GradientStop(Mod.A(Colors.White, .95), 0), new GradientStop(Mod.A(Color.FromRgb(0xFF, 0xE6, 0xB0), .6), .35), new GradientStop(Mod.A(Color.FromRgb(0xFF, 0xE6, 0xB0), 0), 1) },
    });
    static readonly LinearGradientBrush MoltenBrush = Lin(new Point(0, 0), new Point(1, 0), (Color.FromArgb(0, 0xFF, 0x9A, 0x3C), 0), (Color.FromRgb(0xFF, 0xD2, 0x7A), .5), (Color.FromArgb(0, 0xFF, 0x9A, 0x3C), 1));

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
        double bw = Math.Max(0, _fight.ActualWidth - 6), bh = Math.Max(24, head.ActualHeight + 6);
        var band = new Grid { Width = bw, Height = bh };
        var bloom = new ScaleTransform(0, 1);
        // the strip: molten gold for a kill or a clear (coral for a wipe), lighter at its top, dark hairlines along both edges
        var strip = wipe ? Lin(new Point(0, 0), new Point(0, 1), (Color.FromRgb(0xB8, 0x5A, 0x5C), 0), (Color.FromRgb(0x86, 0x2C, 0x34), .55), (Color.FromRgb(0x5E, 0x1A, 0x22), 1))
            : Lin(new Point(0, 0), new Point(0, 1), (Color.FromRgb(0xFF, 0xF4, 0xCE), 0), (GoldHiC, .35), (GoldMidC, .75), (GoldLoC, 1));
        var art = new TacSketch((dc, s) =>
        {
            dc.DrawRectangle(strip, null, new Rect(0, 0, s.Width, s.Height));
            dc.DrawRectangle(Frozen(Mod.A(AbyssC, .7)), null, new Rect(0, 0, s.Width, 1));
            dc.DrawRectangle(Frozen(Mod.A(AbyssC, .7)), null, new Rect(0, s.Height - 1, s.Width, 1));
            dc.DrawRectangle(Frozen(Mod.A(Colors.White, .55)), null, new Rect(0, 1, s.Width, 1));
        }) { RenderTransform = bloom, RenderTransformOrigin = new Point(0, .5) };
        band.Children.Add(art);
        var words = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        words.Children.Add(Caps(OutcomeText(e), 17, wipe ? Ivory : Frozen(Color.FromRgb(0x0E, 0x15, 0x30))));
        var ck = F(Clock(e.Duration(e.End ?? DateTime.UtcNow)), 13.5, wipe ? Soft : Frozen(Color.FromRgb(0x0E, 0x15, 0x30))); ck.Margin = new Thickness(10, 2, 0, 0);
        words.Children.Add(ck);
        band.Children.Add(words);
        Canvas.SetLeft(band, 3); Canvas.SetTop(band, at.Y - (bh - head.ActualHeight) / 2);
        _fightFx.Children.Clear();
        // a kill (or a clear): a flare of light on the crest, the wings swinging out
        System.Windows.Shapes.Ellipse? flare = null;
        ScaleTransform? flareScale = null;
        if (!wipe)
        {
            double cx = Math.Round(_fight.ActualWidth / 2);
            flareScale = new ScaleTransform(.2, .2, 50, 22);
            flare = new System.Windows.Shapes.Ellipse { Width = 100, Height = 44, Fill = FlareBrush, Opacity = 0, RenderTransform = flareScale, IsHitTestVisible = false };
            Canvas.SetLeft(flare, cx - 50); Canvas.SetTop(flare, -18);
            FlareWings();
        }
        _fightFx.Children.Add(band);
        if (flare != null) _fightFx.Children.Add(flare);
        if (!wipe) GlintRows(60);
        else DroopWings();
        // on the look's stage: a kill is divine light breaking from the crest (a burst of golden rays, a white-gold flare line
        // along the top, a ring of light, golden feathers flung out and drifting down) as the plate punches; a wipe is the
        // light failing: the wings droop, the plate shakes, night closes in at its edges, grey feathers fall
        var st = AtreiaFx.Stage(_fx, _root);
        var parts = new List<Action<double>>();
        double fw = _fight.ActualWidth;
        if (AtreiaFx.Box(_fight, _fx) is { } b)
        {
            var crest = new Point(b.X + b.Width / 2, b.Y + 4);
            var bits = new AtreiaFx.Bits(st);
            if (!wipe)
            {
                parts.Add(AtreiaFx.Bloom(st, crest, 190, 100, AtreiaFx.Glow(WarmC, 1, Colors.White), 0, .05, .36, s0: .3));
                parts.Add(AtreiaFx.Rays(st, crest, 20, 10, b.Width * .55, 5, i => i % 2 == 0 ? GoldHiC : IvoryC, .01, .08, .55, turn: 10, max: .7));
                parts.Add(AtreiaFx.Streak(st, crest, b.Width * 1.1, 8, GoldHiC, 0, .06, .4));
                bits.Burst(10, AtreiaFx.Feather, i => AtreiaFx.Br(i % 3 == 0 ? IvoryC : i % 3 == 1 ? GoldHiC : WarmC, .95), crest + new Vector(0, 10), b.Width * .34, 9, .04, .9,
                    fall: 46, dir: 90, spread: 220, r0: 8, spin: 80, seed: 61, sway: 6, squash: .6);
            }
            else
            {
                parts.Add(AtreiaFx.Veil(st, new Rect(b.X + 1, b.Y + 1, b.Width - 2, b.Height - 2), AbyssC, .85, .02, .16, .68));
                parts.Add(AtreiaFx.Bloom(st, crest, 130, 60, AtreiaFx.Glow(CoralC, .7), 0, .04, .3, s0: .8));
                var rnd = new Random(67);
                for (int k = 0; k < 5; k++)
                {
                    var o = new Point(b.X + 16 + (b.Width - 32) * (k + .5) / 5, b.Y + 6 + 30 * rnd.NextDouble());
                    bits.Add(AtreiaFx.Feather, AtreiaFx.Br(k % 3 == 0 ? Mod.Mix(GreyC, IvoryC, .5) : GreyC, .9), o, new Vector((rnd.NextDouble() - .5) * 4, 22), fall: 64 + 20 * rnd.NextDouble(),
                        rot0: 70 + 40 * rnd.NextDouble(), spin: (rnd.NextDouble() - .5) * 60, s0: 10 + 3 * rnd.NextDouble(), s1: 8, delay: .04 + .1 * rnd.NextDouble(), life: .5, sway: 2);
                }
            }
            parts.Add(bits.At);
        }
        _fightPunch.CenterX = fw / 2; _fightPunch.CenterY = 0;
        Motion.Play(st, 2400, t =>
        {
            double k = Math.Min(1, t * 2400 / 1500);
            AtreiaFx.All(parts, k);
            if (wipe) _fightShift.X = AtreiaFx.Shake(k, .02, .46, 5, 3.5);
            else _fightPunch.ScaleX = _fightPunch.ScaleY = AtreiaFx.Punch(k, 0, .5, .012, .026);
            if (flare != null && flareScale != null)
            {
                double f = Seg(t, 0, .3);
                flareScale.ScaleX = flareScale.ScaleY = .2 + 1.1 * Out(f);
                flare.Opacity = f is > 0 and < 1 ? Math.Sin(Math.PI * Math.Pow(f, .5)) : 0;
            }
            bloom.ScaleX = Out(Seg(t, 0, .22));
            // the band's words and the head never show together (their words would sit over each other)
            words.Opacity = t < .82 ? Seg(t, .12, .24) : 1 - Seg(t, .82, .9);
            band.Opacity = t < .82 ? 1 : 1 - Seg(t, .82, .92);
            head.Opacity = t < .9 ? 1 - Seg(t, 0, .12) : Seg(t, .9, 1);
        }, () => { _fx.Children.Remove(st); _fightFx.Children.Clear(); head.Opacity = 1; _fightShift.X = 0; _fightPunch.ScaleX = _fightPunch.ScaleY = 1; });
    }

    /// <summary>A wipe's droop of the wings: they sink 10 degrees and fold a touch, then lift back to their place.</summary>
    void DroopWings()
    {
        if (!_wingsOn) return;
        Motion.Play(_bigWingL, 1400, p =>
        {
            // down 12 degrees, easing in, held 0.3 s, then back to rest with a small overshoot
            double k = p < .22 ? Math.Pow(p / .22, 2) : p < .44 ? 1 : 1 - Back(Seg(p, .44, 1), 1.6);
            _bigWingLTurn.Angle = 12 * k; _bigWingRTurn.Angle = -12 * k;
            _bigWingLScale.ScaleY = _bigWingRScale.ScaleY = 1 - .08 * k;
            _bigWingL.Opacity = _bigWingR.Opacity = 1 - .35 * k;
        }, () => { _bigWingLTurn.Angle = _bigWingRTurn.Angle = 0; _bigWingLScale.ScaleY = _bigWingRScale.ScaleY = 1; _bigWingL.Opacity = _bigWingR.Opacity = 1; });
    }
}
