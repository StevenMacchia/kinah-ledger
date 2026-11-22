using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using KinahLedger.Parser;
using KinahLedger.Session;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

/// <summary>
/// Hype (the Esports theme, 2026-11-20): a sponsor's energy-drink package at full volume, still readable at a glance. Every
/// panel is a black plate with its top-left and bottom-right corners cut, a brushed-chrome bevel round it, a faint carbon
/// weave in it, a hard drop under it, an electric-lime stripe and a hot-magenta tab along its top and static speed lines
/// streaking behind its head. Words are Bahnschrift Bold Condensed in italics (the outcome in Impact), figures italic, bold,
/// tabular and white. The meter's rows are angled bars with chrome caps, the leader's in lime with a hard glow, the figures on
/// a black plate at the row's end; rank 1 wears a lightning-bolt badge. The Buddy: the hour huge in chrome-lime, the run's
/// ring as a lime gauge, the pickups in chrome tiles. The moments are loud and short: a lead change slams the bolt badge in
/// (a big overshoot, a shockwave ring, one white frame on the row) and races a chrome glint along every bar while speed lines
/// streak across the panel (also on a fight's start and a kill); a pickup's worth slams in beside the Buddy with a lime glint
/// across its plate; a rare drop's plate flashes its grade; your best passed sweeps the gauge to full; an opened entry's chip
/// punches lime; a fight's end drops a black band with the outcome over its head.
/// </summary>
sealed partial class HudHype : ModernLook
{
    static readonly Color LimeC = Color.FromRgb(0xC6, 0xFF, 0x00), LimeDeepC = Color.FromRgb(0x2E, 0x3E, 0x00), LimeInkC = Color.FromRgb(0xE2, 0xFF, 0x7A),
        MagC = Color.FromRgb(0xFF, 0x2B, 0xD6), MagHotC = Color.FromRgb(0xFF, 0x5A, 0x8C), MagInkC = Color.FromRgb(0xFF, 0x8F, 0xE6),
        InkC = Color.FromRgb(0x05, 0x06, 0x08), SteelC = Color.FromRgb(0x1B, 0x1F, 0x25), TrackC = Color.FromRgb(0x23, 0x28, 0x30), GreyC = Color.FromRgb(0x8A, 0x90, 0xA0),
        HealC = Color.FromRgb(0x6B, 0xFF, 0xB8), WarnC2 = Color.FromRgb(0xFF, 0xB0, 0x86), GoldC2 = Color.FromRgb(0xFF, 0xCF, 0x6A), MintC2 = Color.FromRgb(0x5D, 0xF2, 0xA6);
    static readonly SolidColorBrush White = Frozen(Colors.White), Soft = Frozen(Mod.A(Colors.White, .84)), Quiet = Frozen(Mod.A(Colors.White, .62)),
        Lime = Frozen(LimeC), LimeInk = Frozen(LimeInkC), Mag = Frozen(MagC), MagInk = Frozen(MagInkC), Heal = Frozen(HealC), Black = Frozen(Color.FromRgb(0x08, 0x09, 0x0B));

    /// <summary>The look's face: Bahnschrift, set condensed, bold and italic by <see cref="Ht"/>.</summary>
    static readonly FontFamily Face = HudThemes.Bahn;
    /// <summary>The look's one headline face (the outcome word).</summary>
    static readonly FontFamily Punch = HudThemes.Impact;
    /// <summary>The headline face for ko / zh words (Impact has no Hangul or Han): the bold system CJK faces.</summary>
    static readonly FontFamily PunchCjk = new("Malgun Gothic, Microsoft JhengHei UI, Microsoft JhengHei, Segoe UI");
    /// <summary>The plates' cut corners.</summary>
    const double Cut = 9;

    // the room round the look: the plates' hard drop inside the window; 98 at the right for the pickup's worth beside the Buddy
    readonly Grid _root = new() { Margin = new Thickness(8, 8, 98, 10), HorizontalAlignment = HorizontalAlignment.Left };
    readonly StackPanel _body = new() { HorizontalAlignment = HorizontalAlignment.Left };
    double _opacity = 0.9;

    // the Buddy: its plate, the gauge, the hour, the session and the timer chips
    readonly Grid _line = new() { HorizontalAlignment = HorizontalAlignment.Left };
    readonly TacSketch _linePlate;
    readonly StackPanel _lineBody = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(13, 7, 24, 11) };
    readonly Canvas _lineAnchor = Tac.Anchor(HorizontalAlignment.Right, VerticalAlignment.Center);
    readonly StackPanel _timers = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    readonly Border _timersInLine = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
    /// <summary>The timer chips over the line, when the pickup strip takes their room on it.</summary>
    readonly Border _timersUp = new() { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(4, 0, 0, 5), Visibility = Visibility.Collapsed };
    /// <summary>The pickup strip in Hype: 24 px tiles in chrome frames beside the hour, a lime rule, a lime star.</summary>
    static readonly PickStrip.Look Strip = new(26, 4, true, Frozen(Mod.A(LimeC, .55)), Frozen(LimeC));
    bool _dim;
    TacSketch? _gaugeArt;
    double _share = -1, _complete, _gaugeFlash;
    bool _beat;
    /// <summary>The glint that races the Buddy's plate on a moment: two frozen slanted bands on a canvas clipped to the plate, moved by a transform.</summary>
    readonly Canvas _sheenHost = new() { IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    readonly TranslateTransform _sheenAt = new();
    Grid? _firstChip;

    // the rare drop under the Buddy, and the notice line
    readonly Grid _rare = new() { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0), Visibility = Visibility.Collapsed };
    readonly Grid _note = new() { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(2, 4, 0, 0), Visibility = Visibility.Collapsed };

    // the fight: its plate, head, HP, rows and foot, the moments' layer over it
    readonly Grid _fight = new() { HorizontalAlignment = HorizontalAlignment.Left, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 6) };
    readonly TacSketch _fightPlate;
    readonly StackPanel _fightBody = new() { Margin = new Thickness(14, 10, 24, 13) };
    readonly Border _timersInFight = new() { Margin = new Thickness(0, 0, 0, 7), HorizontalAlignment = HorizontalAlignment.Left };
    readonly Grid _fightFx = new() { IsHitTestVisible = false };
    /// <summary>The burst's speed lines: frozen gradient bars behind the meter's rows, moved by transforms, the canvas clipped to the plate.</summary>
    readonly Canvas _streaks = new() { IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    readonly List<(System.Windows.Shapes.Rectangle Bar, TranslateTransform At)> _streakBars = new();
    readonly Grid _bandHost = new() { IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Top };
    readonly TranslateTransform _fightShift = new();
    readonly SkewTransform _fightSkew = new();
    FrameworkElement? _head;

    public HudHype() : base(bare: true)
    {
        // the look's own rows (Ui/Themes/ThemeRows.cs): angled bars, the figures on a black plate, the leader's bar lime
        var fit = new HypeFit();
        var lead = new HypeLead();
        UseRows(HypeRow.Pitch, m => new HypeRow(fit, lead, this), 15, GapLine);
        _linePlate = new TacSketch((dc, s) => Plate(dc, s, _opacity, LimeC, s.Height, Cut, _dim, tail: true));
        // the glint's two bands, built once: a soft lime lead and a hard white core, leaning as the plate's stripes do
        var band = new Canvas { RenderTransform = _sheenAt };
        band.Children.Add(new System.Windows.Shapes.Path { Data = Para(new Rect(0, 0, 46, 120), 30), Fill = Mod.Grad(false, (Mod.A(LimeC, 0), 0), (Mod.A(LimeC, .32), 1)) });
        band.Children.Add(new System.Windows.Shapes.Path { Data = Para(new Rect(44, 0, 16, 120), 30), Fill = Frozen(Mod.A(Colors.White, .5)) });
        _sheenHost.Children.Add(band);
        _line.Children.Add(_linePlate); _line.Children.Add(_sheenHost); _line.Children.Add(_lineBody); _line.Children.Add(_lineAnchor);
        _fightPlate = new TacSketch((dc, s) => Plate(dc, s, _opacity, LimeC, 48, tail: true));
        foreach (var (_, _, _, l, t, c) in Streaks)
        {
            var at = new TranslateTransform();
            var bar = new System.Windows.Shapes.Rectangle { Width = l, Height = t * 1.6, Fill = c == 0 ? StreakLime : c == 1 ? StreakWhite : StreakMag, RenderTransform = at, Opacity = 0 };
            _streaks.Children.Add(bar); _streakBars.Add((bar, at));
        }
        _fightFx.Children.Add(_bandHost);
        _fight.Children.Add(_fightPlate); _fight.Children.Add(_streaks); _fight.Children.Add(_fightBody); _fight.Children.Add(_fightFx);
        _fight.RenderTransform = new TransformGroup { Children = { _fightSkew, _fightShift } };
        // the run's head sits over the game: on its own small black tag, never loose on a bright sky
        _runHeadLit = new Border { Child = Chip(_runHead, Frozen(Mod.A(InkC, .86)), Frozen(Mod.A(LimeC, .6)), new Thickness(9, 1, 10, 2), 4), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(4, 0, 0, 3), Visibility = Visibility.Collapsed };
        _body.Children.Add(_fight); _body.Children.Add(Lit(_timersUp)); _body.Children.Add(_runHeadLit); _body.Children.Add(_line); _body.Children.Add(_rare); _body.Children.Add(_note);
        _root.Children.Add(_body);
        System.Windows.Documents.TextElement.SetFontFamily(_root, Face);
        TextOptions.SetTextFormattingMode(_root, TextFormattingMode.Display);
    }

    public override FrameworkElement Root => _root;
    public override Thickness Inset => _root.Margin;
    public override double Corner => 0;
    public override FrameworkElement? FightFrame => _fight.Visibility == Visibility.Visible ? _fight : null;

    public override void SetOpacity(double opacity)
    {
        // the plates take the setting, never so faint the words lose their black
        _opacity = Math.Clamp(opacity, 0.3, 1);
        _linePlate.InvalidateVisual(); _fightPlate.InvalidateVisual();
    }

    protected override string LineSig(Figures f) => base.LineSig(f) + "|" + (Rare(Values)?.Id ?? "") + "|" + (Picks(Values).FirstOrDefault()?.Id ?? "") + "|" + Dimmed(Values) + "|" + PickStrip.Sig(Values, Strip) + "|" + RunBuddy.Sig(Values);

    static bool Dimmed(IReadOnlyDictionary<string, string> v) => (v.TryGetValue("show", out var s) && s == "dim") || (v.TryGetValue("paused", out var p) && p.Length > 0);

    // ================================================================ the kit

    /// <summary>A text in the look's face: Bahnschrift condensed, bold, in italics (<paramref name="italic"/>), tabular digits.</summary>
    static TextBlock Ht(string s, double size, Brush fg, FontWeight? weight = null, bool italic = true, FontFamily? face = null)
    {
        // ko and zh fall back to Malgun / JhengHei, which have no semibold: their words take the bold so they keep their weight
        var w = weight ?? FontWeights.Bold;
        if (w == FontWeights.SemiBold && L.Code != "en") w = FontWeights.Bold;
        var t = Mod.Text(s, size, fg, w, face ?? Face);
        if (face == null) t.FontStretch = FontStretches.Condensed;
        if (italic) t.FontStyle = FontStyles.Italic;
        // the slant leans past the last glyph: a pixel or two of air so it is never clipped
        t.Padding = new Thickness(0, 0, italic ? Math.Ceiling(size * .08) : 0, 0);
        return t;
    }

    /// <summary>The tight shade under words straight on the game: 1 px down, 2 px soft.</summary>
    static DropShadowEffect Shade() => new() { Color = Colors.Black, Direction = 270, ShadowDepth = 1, BlurRadius = 2.5, Opacity = .9 };

    /// <summary>The wide soft halo round words straight on the game (over the Buddy, the run's head).</summary>
    static DropShadowEffect Halo() => new() { Color = Colors.Black, ShadowDepth = 0, BlurRadius = 10, Opacity = .62 };

    /// <summary>Words on a plate: the black plate is their ground, so no effect is spent on them (Steven, 2026-11-20: "lightweight").</summary>
    static T Shaded<T>(T el) where T : FrameworkElement => el;

    /// <summary>Words straight on the game: the tight shade on them, the halo round them.</summary>
    static Border Lit(FrameworkElement el)
    {
        el.Effect = Shade();
        return new Border { Child = el, Effect = Halo(), HorizontalAlignment = el.HorizontalAlignment, VerticalAlignment = el.VerticalAlignment };
    }

    /// <summary>A parallelogram leaning right by <paramref name="slant"/> over its height (the look's every chip, bar and tag).</summary>
    internal static Geometry Para(Rect r, double slant)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(r.Left + slant, r.Top), true, true);
            c.LineTo(new Point(r.Right, r.Top), true, false);
            c.LineTo(new Point(r.Right - slant, r.Bottom), true, false);
            c.LineTo(new Point(r.Left, r.Bottom), true, false);
        }
        g.Freeze();
        return g;
    }

    /// <summary>How far a plate's right edge leans in over its height (the whole package leans forward, as its type does).</summary>
    static double LeanOf(double h) => Math.Min(14, h * .17);

    /// <summary>How far a plate's left edge leans (its top further right): with the right edge, the plate reads as a parallelogram.</summary>
    static double LeanL(double h) => Math.Min(8, h * .15);

    /// <summary>A plate's shape: its top-left and bottom-right corners cut, its right edge leaning forward (<see cref="LeanOf"/>).</summary>
    static Geometry PlateGeo(Rect r, double cut)
    {
        double lean = LeanOf(r.Height), h = r.Height, notch = Math.Min(14, h * .25);
        double XL(double y) => r.Left + LeanL(h) * (r.Bottom - y) / h;
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(XL(r.Top) + cut, r.Top), true, true);
            c.LineTo(new Point(r.Right, r.Top), true, false);
            c.LineTo(new Point(r.Right - lean * (h - cut) / h, r.Bottom - cut), true, false);
            c.LineTo(new Point(r.Right - lean - cut, r.Bottom), true, false);
            c.LineTo(new Point(r.Left + notch, r.Bottom), true, false);
            c.LineTo(new Point(XL(r.Bottom - notch), r.Bottom - notch), true, false);
            c.LineTo(new Point(XL(r.Top + cut), r.Top + cut), true, false);
        }
        g.Freeze();
        return g;
    }

    /// <summary>Brushed chrome, top to bottom: a bright top, the dark horizon, a cooler light under it.</summary>
    static readonly LinearGradientBrush Chrome = Mod.Grad(true, (Color.FromRgb(0xF7, 0xF9, 0xFC), 0), (Color.FromRgb(0xAE, 0xB6, 0xC0), .3), (Color.FromRgb(0x4A, 0x50, 0x59), .5),
        (Color.FromRgb(0xDD, 0xE2, 0xE8), .56), (Color.FromRgb(0x84, 0x8C, 0x96), 1));
    /// <summary>The hour's chrome-lime: a hot white-lime top, the lime's dark horizon, lime under it.</summary>
    static readonly LinearGradientBrush ChromeLime = Mod.Grad(true, (Color.FromRgb(0xF8, 0xFF, 0xDC), 0), (Color.FromRgb(0xDA, 0xFF, 0x4A), .44), (Color.FromRgb(0x8C, 0xC0, 0x00), .5),
        (Color.FromRgb(0xC6, 0xFF, 0x00), .62), (Color.FromRgb(0xEC, 0xFF, 0x9C), 1));
    /// <summary>A speed line's fades (left to right: gone to full), in lime, white and magenta.</summary>
    static readonly LinearGradientBrush StreakLime = Mod.Grad(false, (Mod.A(LimeC, 0), 0), (Mod.A(LimeC, 1), 1)), StreakWhite = Mod.Grad(false, (Mod.A(Colors.White, 0), 0), (Mod.A(Colors.White, 1), 1)),
        StreakMag = Mod.Grad(false, (Mod.A(MagC, 0), 0), (Mod.A(MagC, 1), 1));

    /// <summary>The plates' carbon weave: a faint diagonal hatch, made once.</summary>
    static readonly DrawingBrush Weave = MakeWeave();

    static DrawingBrush MakeWeave()
    {
        var g = new GeometryDrawing(null, Mod.P(Frozen(Mod.A(Colors.White, .045)), 1), new LineGeometry(new Point(0, 6), new Point(6, 0)));
        var b = new DrawingBrush(g) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 6, 6), ViewportUnits = BrushMappingMode.Absolute, Viewbox = new Rect(0, 0, 6, 6), ViewboxUnits = BrushMappingMode.Absolute, Stretch = Stretch.None };
        b.Freeze();
        return b;
    }

    /// <summary>The static speed lines: (height share, start, length (shares of the region), thickness, strength, colour 0 lime 1 white 2 magenta).</summary>
    static readonly (double Y, double X, double L, double T, double A, int C)[] Lines =
    {
        (.12, .50, .34, 1.6, .26, 0), (.22, .72, .20, 1, .22, 1), (.34, .44, .42, 2.2, .14, 0), (.44, .80, .16, 1, .26, 1), (.55, .60, .26, 1.4, .16, 2),
        (.66, .86, .12, 1, .22, 0), (.78, .54, .32, 1, .12, 1), (.90, .70, .24, 2, .14, 0),
    };

    /// <summary>Speed lines in <paramref name="r"/>, leaning up to the right, fading in from their tails; <paramref name="k"/> scales their strength.</summary>
    static void SpeedLines(DrawingContext dc, Rect r, double k = 1)
    {
        foreach (var (y, x, l, t, a, c) in Lines)
        {
            double px = r.Left + r.Width * x, py = r.Top + r.Height * y, len = r.Width * l;
            dc.PushTransform(new RotateTransform(-14, px, py));
            dc.PushOpacity(Math.Min(1, a * k));
            dc.DrawRectangle(c == 0 ? StreakLime : c == 1 ? StreakWhite : StreakMag, null, new Rect(px, py, len, t));
            dc.Pop(); dc.Pop();
        }
    }

    /// <summary>
    /// A plate: the hard drop under it, the black (a touch of steel at its top), the carbon weave, speed lines behind its top
    /// <paramref name="linesH"/> px (right of its first fifth), the lime stripe and the magenta tab along its top, the chrome
    /// bevel and a dark line inside it. Drawn inside its element: the drop takes 3 px at the right and 4 at the foot.
    /// </summary>
    static void Plate(DrawingContext dc, Size s, double opacity, Color accent, double linesH, double cut = Cut, bool dim = false, bool tail = false)
    {
        if (s.Width < 12 || s.Height < 12) return;
        var r = new Rect(1, 1, s.Width - 4, s.Height - 5);
        dc.DrawGeometry(Frozen(Mod.A(Colors.Black, .5)), null, PlateGeo(new Rect(r.X + 3, r.Y + 4, r.Width, r.Height), cut));
        var shape = PlateGeo(r, cut);
        double a = Math.Clamp(.62 + .34 * opacity, .78, .97);
        dc.DrawGeometry(Mod.Grad(true, (Mod.A(SteelC, a), 0), (Mod.A(InkC, a), .45), (Mod.A(InkC, a), 1)), null, shape);
        dc.PushClip(shape);
        dc.DrawRectangle(Weave, null, r);
        if (!dim) SpeedLines(dc, new Rect(r.Left + r.Width * .2, r.Top, r.Width * .8, Math.Min(linesH, r.Height)));
        // the power spine: a lime bar down the left edge under the cut, notched like a gauge
        if (tail)
        {
            double y0 = r.Top + cut + 3, y1 = r.Bottom - Math.Min(14, r.Height * .25) - 2, ll = LeanL(r.Height);
            double XL(double y) => r.Left + ll * (r.Bottom - y) / r.Height;
            var spine = new StreamGeometry();
            using (var c = spine.Open())
            {
                c.BeginFigure(new Point(XL(y0), y0), true, true);
                c.LineTo(new Point(XL(y0) + 5, y0), true, false);
                c.LineTo(new Point(XL(y1) + 5, y1), true, false);
                c.LineTo(new Point(XL(y1), y1), true, false);
            }
            spine.Freeze();
            dc.DrawGeometry(Frozen(dim ? GreyC : accent), null, spine);
            for (double y = y0 + 6; y < y1 - 2; y += 8) dc.DrawRectangle(Frozen(Mod.A(Colors.Black, .65)), null, new Rect(XL(y) - 1, y, 7, 1.5));
        }
        dc.Pop();
        // the speed tail: three lime slashes trailing off the leaning right edge, the plate's silhouette broken
        if (tail)
        {
            // three chevron blades (lime, magenta, lime) flying off the leaning right edge, the head's height, black-edged
            double lean = LeanOf(r.Height), bh = Math.Min(r.Height - cut - 4, 34), top = r.Top + 2;
            double ex = r.Right - lean * (bh / 2) / r.Height;
            for (int i = 0; i < 3; i++)
            {
                double x = ex + 3 + i * 8.5, w = 5, d = 7, ym = top + bh / 2;
                var blade = new StreamGeometry();
                using (var c = blade.Open())
                {
                    c.BeginFigure(new Point(x, top), true, true);
                    c.LineTo(new Point(x + w, top), true, false);
                    c.LineTo(new Point(x + w + d, ym), true, false);
                    c.LineTo(new Point(x + w, top + bh), true, false);
                    c.LineTo(new Point(x, top + bh), true, false);
                    c.LineTo(new Point(x + d, ym), true, false);
                }
                blade.Freeze();
                dc.DrawGeometry(Frozen(dim ? GreyC : i == 1 ? MagC : Mod.A(accent, i == 0 ? 1 : .75)), Mod.P(Frozen(Mod.A(Colors.Black, .85)), 1), blade);
            }
        }
        // the stripe: lime from the cut along a third of the top, a gap, a magenta tab
        var ac = dim ? GreyC : accent;
        double x0 = r.Left + cut + 2, len = Math.Min(170, r.Width * .34);
        dc.DrawGeometry(Frozen(ac), null, Para(new Rect(x0, r.Top + .5, len, 3), 3));
        dc.DrawGeometry(Frozen(dim ? GreyC : MagC), null, Para(new Rect(x0 + len + 4, r.Top + .5, 16, 3), 3));
        dc.DrawGeometry(null, Mod.P(Chrome, 1.6), shape);
        dc.DrawGeometry(null, Mod.P(Frozen(Mod.A(Colors.Black, .7)), 1), PlateGeo(new Rect(r.X + 1.8, r.Y + 1.8, r.Width - 3.6, r.Height - 3.6), cut - .8));
    }

    /// <summary>A slanted chip round <paramref name="content"/>: its fill, an optional edge, the content padded inside.</summary>
    static Grid Chip(FrameworkElement content, Brush fill, Brush? edge, Thickness pad, double slant = 4)
    {
        var g = new Grid { VerticalAlignment = VerticalAlignment.Center };
        g.Children.Add(new TacSketch((dc, s) => { if (s.Width > 2) dc.DrawGeometry(fill, edge != null ? Mod.P(edge, 1) : null, Para(new Rect(.5, .5, s.Width - 1, s.Height - 1), slant)); }));
        content.Margin = pad;
        g.Children.Add(content);
        return g;
    }

    /// <summary>A mark (★ DPS, ✚, AVG): black with a lime (or <paramref name="edge"/>) edge and ink; <paramref name="filled"/> white with black words (YOU).</summary>
    static Grid Mark(string s, Brush? ink = null, Color? edge = null, bool filled = false, double size = 10)
    {
        var t = Ht(s, size, filled ? Black : ink ?? LimeInk, FontWeights.Bold);
        return Chip(t, filled ? White : Frozen(Mod.A(Colors.Black, .82)), filled ? null : Frozen(Mod.A(edge ?? LimeC, .85)), new Thickness(5, 0, 4, 1), 3);
    }

    /// <summary>A chrome hairline across a plate, bright at its middle.</summary>
    static FrameworkElement Hairline(Thickness m) => new TacSketch((dc, s) =>
        dc.DrawRectangle(Mod.Grad(false, (Mod.A(Colors.White, .05), 0), (Mod.A(Colors.White, .32), .3), (Mod.A(LimeC, .4), .6), (Mod.A(Colors.White, .05), 1)), null, new Rect(0, 0, s.Width, 1))) { Height = 1, Margin = m };

    /// <summary>Chrome frames over a pickup strip's tiles (drawn on top of each tile's own edge; no width taken).</summary>
    static FrameworkElement ChromeTiles(FrameworkElement strip, double size)
    {
        if (strip is not Panel row) return strip;
        foreach (var cell in row.Children.OfType<Grid>())
            if (cell.Tag is "pick-tile" or "pick-best")
                cell.Children.Insert(Math.Min(1, cell.Children.Count), ChromeFrame(size));
        return strip;
    }

    /// <summary>A chrome frame <paramref name="size"/> px square: the bevel, a dark line inside it.</summary>
    static TacSketch ChromeFrame(double size) => new((dc, s) =>
    {
        dc.DrawRoundedRectangle(null, Mod.P(Chrome, 1.6), new Rect(.8, .8, s.Width - 1.6, s.Height - 1.6), 3, 3);
        dc.DrawRoundedRectangle(null, Mod.P(Frozen(Mod.A(Colors.Black, .55)), 1), new Rect(2.1, 2.1, s.Width - 4.2, s.Height - 4.2), 2, 2);
    }, size, size) { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };

    /// <summary>A small lightning bolt in <paramref name="r"/> (its top at the right, its foot at the left).</summary>
    internal static Geometry Bolt(Rect r)
    {
        double x = r.Left, y = r.Top, w = r.Width, h = r.Height;
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(x + w * .62, y), true, true);
            c.LineTo(new Point(x + w * .08, y + h * .58), true, false);
            c.LineTo(new Point(x + w * .46, y + h * .56), true, false);
            c.LineTo(new Point(x + w * .28, y + h), true, false);
            c.LineTo(new Point(x + w * .94, y + h * .38), true, false);
            c.LineTo(new Point(x + w * .55, y + h * .40), true, false);
            c.LineTo(new Point(x + w * .82, y), true, false);
        }
        g.Freeze();
        return g;
    }

    // ================================================================ the Buddy

    protected override void DrawLine(Figures f)
    {
        _lineBody.Children.Clear();
        _gaugeArt = null; _timerUp = false; _stripTimerW = -1;
        if (_timersInLine.Child != null) _timersInLine.Child = null;
        _dim = Dimmed(Values);
        _line.Opacity = _dim ? .66 : 1;
        bool mark = f.Abyss && f.Counted && f.Icon.Length > 0;
        if (mark) { var m = AbyssMark(f.Icon, 36); m.Margin = new Thickness(2, 0, 9, 0); _lineBody.Children.Add(m); }
        // in a dungeon run: the run's clock, splits and verdict in place of the hour (HudHype.Run.cs)
        var rs = RunNow();
        _runHeadLit.Visibility = Visibility.Collapsed;
        if (rs != null) DrawRun(rs);
        else if (!HasHour(f))
        {
            var w = Ht(Waiting(f.Abyss), 13.5, Soft, FontWeights.SemiBold); w.Margin = new Thickness(mark ? 0 : 6, 7, 0, 6);
            // in the Abyss before the first points: what has happened so far under it ("2 kills / Kinah"), never an empty line
            var so = f.Abyss ? Sub(f, false) : null;
            if (so != null && so.Inlines.Count > 0)
            {
                var col = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(mark ? 0 : 6, 3, 0, 2) };
                w.Margin = new Thickness(0); col.Children.Add(w); col.Children.Add(so);
                _lineBody.Children.Add(col);
            }
            else _lineBody.Children.Add(w);
        }
        else
        {
            if (HasRing(f))
            {
                _share = f.Share; _beat = f.Beat; _complete = 0;
                var ring = new Grid { Width = 46, Height = 46, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
                _gaugeArt = new TacSketch(DrawGauge, 46, 46);
                ring.Children.Add(_gaugeArt);
                var pc = Ht(f.Beat ? "★" : Pct(f.Share), 14.5, f.Beat ? Lime : White, FontWeights.Bold); pc.HorizontalAlignment = HorizontalAlignment.Center; pc.Margin = new Thickness(1, 0, 0, 2);
                ring.Children.Add(pc);
                _lineBody.Children.Add(ring);
            }
            else if (!mark) _lineBody.Children.Add(new Border { Width = 4 });
            // the hour huge in chrome-lime, its unit small; the session and the latest pickup under it
            var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var top = new StackPanel { Orientation = Orientation.Horizontal };
            var rate = Ht(f.Rate, f.Warm ? 16 : 30, f.Warm ? Soft : ChromeLime, f.Warm ? FontWeights.SemiBold : FontWeights.Bold);
            rate.Tag = "buddy-rate";
            if (!f.Warm) rate.Margin = new Thickness(0, -3, 0, -3);
            top.Children.Add(rate);
            if (f.Unit.Length > 0) { var u = Ht(f.Unit, 12, LimeInk, FontWeights.SemiBold); u.VerticalAlignment = VerticalAlignment.Bottom; u.Margin = new Thickness(2, 0, 0, f.Warm ? 1 : 3); top.Children.Add(u); }
            words.Children.Add(top);
            // farming, the pickups as the strip beside the hour where it fits (the best drop and the last three), the session
            // alone under it; with the timer chips at the line's end and no room left beside them, the chips go over the line
            var sub = Sub(f, true);
            FrameworkElement? strip = null, under = null;
            if (StripTime(f))
            {
                var bare = Sub(f, false);
                double baseW = _lineBody.Margin.Left + _lineBody.Margin.Right + 4 + _lineBody.Children.OfType<FrameworkElement>().Sum(Mod.W), timerW = LineTimerW();
                double wordsW = Math.Max(Mod.W(top), Mod.W(bare)), oldW = Math.Max(Mod.W(top), Mod.W(sub));
                foreach (var look in StripFall(Strip))
                {
                    if ((strip = StripIn(StripRoom(FightWidth - 10, baseW + timerW, wordsW, oldW, 12), look)) != null) break;
                    if (timerW > 0 && (strip = StripIn(StripRoom(FightWidth - 10, baseW, wordsW, oldW + timerW, 12), look)) != null) { _timerUp = true; break; }
                }
                if (strip != null) { sub = bare; strip.Margin = new Thickness(12, 0, 0, 0); ChromeTiles(strip, Strip.Size); }
                // no room beside the hour: under the session, never dropped (Steven, 2026-11-19)
                else if ((under = StripUnder(Strip, false)) != null) { sub = bare; ChromeTiles(under, Strip.Size); }
                _stripTimerW = timerW;
            }
            if (sub.Inlines.Count > 0) words.Children.Add(sub);
            if (under != null) words.Children.Add(under);
            _lineBody.Children.Add(words);
            if (strip != null) _lineBody.Children.Add(strip);
        }
        _lineBody.Children.Add(_timersInLine);
        PlaceTimers();
        _linePlate.InvalidateVisual();

        // the rare drop the window holds: a plate striped in its grade's colour, its tile in chrome, RARE DROP and its name
        _rare.Children.Clear();
        if (!KinahHidden && Rare(Values) is { } r)
        {
            var c = ColorOf(r.Color, r.Grade);
            var plate = new TacSketch((dc, s) =>
            {
                if (s.Width < 16) return;
                var slab = Para(new Rect(1, 1, s.Width - 10, s.Height - 6), 9);
                dc.DrawGeometry(Mag, null, Para(new Rect(6, 5, s.Width - 10, s.Height - 6), 9));
                dc.DrawGeometry(Mod.Grad(false, (Color.FromRgb(0xA8, 0xE0, 0x00), 0), (LimeC, .35), (Color.FromRgb(0xE4, 0xFF, 0x7A), 1)), Mod.P(Black, 1.2), slab);
                dc.PushClip(slab);
                dc.DrawRectangle(Frozen(c), null, new Rect(0, 0, 17, s.Height));
                dc.DrawRectangle(Frozen(Mod.A(Colors.White, .45)), null, new Rect(0, 1, s.Width, 1.5));
                dc.Pop();
            });
            _rarePlate = plate;
            _rare.Children.Add(plate);
            // the flash in the grade's colour, drawn once, shown by its opacity on the moment
            _rareFlash = new TacSketch((dc, s) => { if (s.Width > 16) dc.DrawGeometry(White, null, Para(new Rect(1, 1, s.Width - 10, s.Height - 6), 9)); }) { Opacity = 0 };
            _rare.Children.Add(_rareFlash);
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(21, 5, 26, 9) };
            var tileHost = new Grid { Width = 26, Height = 26, Margin = new Thickness(0, 0, 9, 0) };
            tileHost.Children.Add(Tile(r, 26)); tileHost.Children.Add(ChromeFrame(26));
            _rareTile = tileHost;
            row.Children.Add(tileHost);
            var mk = Chip(Ht(L.T("RARE DROP"), 11, Lime, FontWeights.Bold), Black, null, new Thickness(8, 1, 8, 1), 5);
            mk.Margin = new Thickness(0, 0, 9, 0);
            row.Children.Add(mk);
            var nm = Ht(r.Name, 16, Black, FontWeights.Bold);
            row.Children.Add(nm);
            if (r.Count.Length > 0) { var cn = Ht(" " + r.Count, 13, Black, FontWeights.SemiBold); cn.Margin = new Thickness(2, 1, 0, 0); row.Children.Add(cn); }
            _rare.Children.Add(Shaded(row));
            _rare.Visibility = Visibility.Visible;
        }
        else { _rare.Visibility = Visibility.Collapsed; _rareFlash = null; _rareTile = null; }

        // the notice: its words in their tone on a small black plate under the Buddy, the warning sign before a pause
        _note.Children.Clear();
        if (f.Note.Length > 0 && !KinahHidden)
        {
            bool warn = ReferenceEquals(f.NoteTone, Warn);
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            if (warn) { var wi = WarnIcon(); wi.Width = wi.Height = 13; wi.Margin = new Thickness(0, 0, 5, 0); sp.Children.Add(wi); }
            var tone = warn ? WarnC2 : ReferenceEquals(f.NoteTone, Gold) ? GoldC2 : ReferenceEquals(f.NoteTone, Mint) ? MintC2 : Color.FromRgb(0xF2, 0xF4, 0xF8);
            var t = Ht(f.Note, 12, Frozen(tone), FontWeights.SemiBold); t.TextTrimming = TextTrimming.CharacterEllipsis; t.MaxWidth = 380;
            sp.Children.Add(t);
            _note.Children.Add(Chip(Shaded(sp), Frozen(Mod.A(InkC, .88)), Frozen(Mod.A(warn ? WarnC2 : Colors.White, warn ? .7 : .3)), new Thickness(9, 3, 10, 4), 5));
            _note.Visibility = Visibility.Visible;
        }
        else _note.Visibility = Visibility.Collapsed;
    }

    /// <summary>The line under the hour: the session, the latest pickup and its worth (<paramref name="pick"/>); in the Abyss its stats.</summary>
    TextBlock Sub(Figures f, bool pick)
    {
        var sub = Ht("", 12.5, Frozen(Mod.A(Colors.White, .88)), FontWeights.SemiBold);
        sub.Margin = new Thickness(1, 1, 0, 0);
        void Dot() { if (sub.Inlines.Count > 0) sub.Inlines.Add(new System.Windows.Documents.Run("  /  ") { Foreground = Frozen(Mod.A(LimeC, .7)) }); }
        if (f.Total.Length > 0) { sub.Inlines.Add(new System.Windows.Documents.Run(f.Total) { FontWeight = FontWeights.Bold, Foreground = White }); sub.Inlines.Add(new System.Windows.Documents.Run(" " + f.TotalWord)); }
        foreach (var (fig, word, tone) in f.Stats)
        {
            Dot();
            var c = ReferenceEquals(tone, Rec) ? MagInkC : ReferenceEquals(tone, Gold) ? GoldC2 : Colors.White;
            sub.Inlines.Add(new System.Windows.Documents.Run(fig) { FontWeight = FontWeights.Bold, Foreground = Frozen(c) });
            sub.Inlines.Add(new System.Windows.Documents.Run(" " + word));
        }
        if (pick && !f.Abyss && Picks(Values).FirstOrDefault() is { } p)
        {
            Dot();
            sub.Inlines.Add(new System.Windows.Documents.Run(p.Name.Length > 22 ? p.Name[..21].TrimEnd() + "…" : p.Name));
            var worth = p.Worth.Length > 0 ? "+" + p.Worth.TrimStart('+') : p.Count;
            if (worth.Length > 0) sub.Inlines.Add(new System.Windows.Documents.Run(" " + worth) { FontWeight = FontWeights.Bold, Foreground = p.Worth.Length > 0 ? Lime : Soft });
        }
        return sub;
    }

    /// <summary>The timer chips' room at the line's end, 0 with none.</summary>
    double LineTimerW() => _timers.Children.Count > 0 && !KinahHidden ? 10 + Mod.W(_timers) : 0;

    /// <summary>The timers' room the line's strip was fitted to (-1: no strip time); chips that change it draw the line again.</summary>
    double _stripTimerW = -1;

    /// <summary>The timer chips sit over the line, to leave its room to the pickup strip.</summary>
    bool _timerUp;

    TacSketch? _rareFlash, _rarePlate;
    FrameworkElement? _rareTile;

    /// <summary>
    /// The glint racing the Buddy's plate at <paramref name="p"/> (0 to 1; outside: put away): its canvas clipped to the plate
    /// when it shows, its bands moved by a transform and faded in and out (nothing is drawn again).
    /// </summary>
    void Sheen(double p)
    {
        if (p is < 0 or > 1 || _line.ActualWidth < 12) { _sheenHost.Visibility = Visibility.Collapsed; return; }
        if (_sheenHost.Visibility != Visibility.Visible)
        {
            _sheenHost.Clip = PlateGeo(new Rect(1, 1, _line.ActualWidth - 4, _line.ActualHeight - 5), Cut);
            _sheenHost.Visibility = Visibility.Visible;
        }
        _sheenAt.X = -70 + (_line.ActualWidth + 80) * p;
        _sheenHost.Opacity = Math.Sin(Math.PI * p);
    }

    /// <summary>
    /// The run's ring as a lime gauge: a 270° steel track with ticks every tenth, your best's share as a lime arc (full once
    /// beaten) with a magenta needle at its end; on your best passed it flares.
    /// </summary>
    void DrawGauge(DrawingContext dc, Size s)
    {
        double c = s.Width / 2, r = c - 4;
        dc.DrawEllipse(Frozen(Mod.A(Colors.Black, .55)), Mod.P(Frozen(Mod.A(Colors.White, .14)), 1), new Point(c, c), r + 3, r + 3);
        const double a0 = 135, sweep = 270;
        Point At(double deg, double rr) { double t = deg * Math.PI / 180; return new Point(c + rr * Math.Cos(t), c + rr * Math.Sin(t)); }
        Geometry Arc(double from, double to, double rr)
        {
            var fig = new PathFigure { StartPoint = At(from, rr) };
            fig.Segments.Add(new ArcSegment(At(to, rr), new Size(rr, rr), 0, to - from > 180, SweepDirection.Clockwise, true));
            var pg = new PathGeometry(); pg.Figures.Add(fig); pg.Freeze();
            return pg;
        }
        dc.DrawGeometry(null, Mod.P(Frozen(TrackC), 4.2), Arc(a0, a0 + sweep, r));
        for (int k = 0; k <= 10; k++)
        {
            double d = a0 + sweep * k / 10;
            bool major = k % 5 == 0;
            dc.DrawLine(Mod.P(Frozen(Mod.A(Colors.White, major ? .6 : .3)), major ? 1.4 : 1), At(d, r - 4.5), At(d, r - (major ? 8.5 : 6.5)));
        }
        double share = Math.Clamp(_beat ? 1 : _share, 0, 1);
        double shown = share + (1 - share) * Math.Clamp(_complete, 0, 1);
        if (shown > .004)
        {
            double end = a0 + sweep * Math.Min(shown, 1);
            var arc = Arc(a0, Math.Max(a0 + .5, end), r);
            if (_beat || _complete >= 1 || _gaugeFlash > 0) dc.DrawGeometry(null, Mod.P(Frozen(Mod.A(LimeC, .25 + .4 * _gaugeFlash)), 8), arc);
            dc.DrawGeometry(null, Mod.P(Frozen(LimeC), 4.2), arc);
            // the needle: a magenta tick across the arc's end, white at its heart
            dc.DrawLine(Mod.P(Frozen(MagC), 2.6), At(end, r - 4.5), At(end, r + 3.5));
            dc.DrawLine(Mod.P(White, 1), At(end, r - 3), At(end, r + 2));
        }
    }

    protected override void KinahShown(bool shown)
    {
        _line.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
        if (!shown) { _note.Visibility = Visibility.Collapsed; _rare.Visibility = Visibility.Collapsed; }
        _fight.Margin = new Thickness(0, 0, 0, shown ? 6 : 0);
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
            var c = tone == TimerBadge.Tone.On ? LimeC : tone == TimerBadge.Tone.Soon ? GoldC2 : Color.FromRgb(0xE6, 0xE9, 0xF0);
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            double d = first ? 7 : 5.5;
            sp.Children.Add(new TacSketch((dc, s) => dc.DrawGeometry(Frozen(c), null, Para(new Rect(0, 0, s.Width, s.Height), d * .45)), d, d) { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
            var t = Ht(text, first ? 12.5 : 11, first ? White : Soft, first ? FontWeights.Bold : FontWeights.SemiBold);
            if (tone == TimerBadge.Tone.On) t.Foreground = LimeInk;
            sp.Children.Add(t);
            var chip = Chip(Shaded(sp), Frozen(Mod.A(InkC, .9)), tone == TimerBadge.Tone.On ? Lime : Chrome, first ? new Thickness(9, 3, 11, 4) : new Thickness(8, 2, 10, 3), 5);
            chip.Margin = new Thickness(first ? 0 : 5, 0, 0, 0);
            chip.Tag = first ? "timer-chip" : null;
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
        // the head's tabs ride up onto the plate's top edge, breaking its line (under the timer chips, in a dungeon, they sit inside)
        if (_head != null) _head.Margin = inside && any ? new Thickness(0, 0, 0, 2) : new Thickness(-6, -15, 0, 2);
    }

    // ================================================================ the fight

    protected override void DrawFight(Encounter? e, DateTime now)
    {
        _fightBody.Children.Clear();
        if (e == null) { _fight.Visibility = Visibility.Collapsed; _bandHost.Children.Clear(); PlaceTimers(); return; }
        double w = FightWidth - 10;
        _fight.Width = w;
        double inner = w - _fightBody.Margin.Left - _fightBody.Margin.Right;
        _fightBody.Children.Add(_timersInFight);
        // the head: the boss, the outcome once it's over, the clock
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition()); head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        // the boss on a lime sponsor tab, black heavy italic
        var title = Ht(e.Title, 16, Black, FontWeights.Bold); title.TextTrimming = TextTrimming.CharacterEllipsis;
        var tab = Chip(title, Lime, Black, new Thickness(10, 1, 12, 1), 7);
        _titleTab = tab;
        tab.HorizontalAlignment = HorizontalAlignment.Left;
        head.Children.Add(tab);
        var right = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 0, 0) };
        if (e.Waiting) right.Children.Add(Chip(Ht(L.T("READY"), 11, Black, FontWeights.Bold), Lime, null, new Thickness(7, 1, 7, 1), 4));
        else
        {
            var outcome = OutcomeText(e);
            if (outcome.Length > 0)
            {
                bool wipe = e.Outcome == "wipe";
                var st = Chip(Ht(outcome.ToUpperInvariant(), 11, wipe ? White : Black, FontWeights.Bold), wipe ? Mag : Lime, null, new Thickness(7, 1, 7, 1), 4);
                st.Margin = new Thickness(0, 1, 9, 0); st.Tag = "outcome";
                _outcomeChip = st;
                right.Children.Add(st);
            }
            // the clock on a black slab edged in chrome, white and huge: the broadcast's game clock
            var clock = Ht(Clock(e, now), 21, White, FontWeights.Bold); clock.Tag = "fight-clock";
            var slab = Chip(clock, Black, Chrome, new Thickness(9, -2, 9, -2), 6);
            right.Children.Add(slab);
        }
        Grid.SetColumn(right, 1); head.Children.Add(right);
        if (!(OutcomeText(e).Length > 0)) _outcomeChip = null;
        _head = head;
        _fightBody.Children.Add(_head);
        if (e.Waiting)
        {
            var wt = Ht(e.ReadyText, 13, Soft, FontWeights.SemiBold); wt.Margin = new Thickness(0, 6, 0, 2); wt.TextTrimming = TextTrimming.CharacterEllipsis;
            _fightBody.Children.Add(Shaded(wt));
            if (PartyBeforeHit(e)) { _fightBody.Children.Add(new Border { Height = 7 }); PutRows(_fightBody, e, now, inner); }
        }
        else
        {
            if (HpOf(e) is { } hp)
            {
                // the boss's HP: an angled magenta bar with chrome caps and ticks at the thirds, the figure and what's left under its right end
                var track = new Grid { Height = 8, Margin = new Thickness(0, 7, 0, 0) };
                track.Children.Add(new TacSketch((dc, s) =>
                {
                    double h = s.Height, x1 = 5 + (s.Width - 10) * Math.Clamp(hp, 0, 1);
                    dc.DrawGeometry(Frozen(TrackC), Mod.P(Frozen(Mod.A(Colors.Black, .8)), 1), Para(new Rect(0, 0, s.Width, h), 4));
                    if (x1 > 5.5)
                    {
                        dc.DrawGeometry(Mod.Grad(false, (Color.FromRgb(0x9A, 0x0E, 0x7E), 0), (MagC, .7), (MagHotC, 1)), null, Para(new Rect(3, 1, x1 - 3, h - 2), 3.5));
                        dc.DrawRectangle(Frozen(Mod.A(Colors.White, .35)), null, new Rect(5, 1, Math.Max(0, x1 - 7), 1));
                    }
                    for (int k = 1; k <= 2; k++) { double x = Math.Round(5 + (s.Width - 10) * k / 3); dc.DrawGeometry(Frozen(Mod.A(Colors.Black, .8)), null, Para(new Rect(x - 1.5, 0, 3, h), 2)); }
                    dc.DrawGeometry(Chrome, null, Para(new Rect(0, 0, 6, h), 4));
                    dc.DrawGeometry(Chrome, null, Para(new Rect(s.Width - 6, 0, 6, h), 4));
                }));
                HpGhost(track, e, hp, now, 0);
                _fightBody.Children.Add(track);
                var hl = Ht("", 12.5, Frozen(Mod.A(Colors.White, .9)), FontWeights.SemiBold); hl.HorizontalAlignment = HorizontalAlignment.Right; hl.Margin = new Thickness(0, 2, 0, 0);
                hl.Inlines.Add(new System.Windows.Documents.Run($"{Math.Round(hp * 100):0}%") { FontWeight = FontWeights.Bold, Foreground = hp < .34 ? MagInk : White });
                var left = HpLeftText(e);
                if (left.Length > 0) hl.Inlines.Add(new System.Windows.Documents.Run("  /  " + left));
                hl.Tag = "hp-text";
                _fightBody.Children.Add(Shaded(hl));
                _fightBody.Children.Add(new Border { Height = 3 });
            }
            else _fightBody.Children.Add(new Border { Height = 8 });
            PutRows(_fightBody, e, now, inner);
            if (e.HasDamage)
            {
                _fightBody.Children.Add(Hairline(new Thickness(0, 7, 0, 5)));
                _fightBody.Children.Add(Foot(e, now));
            }
        }
        _fight.Visibility = Visibility.Visible;
        _fightPlate.InvalidateVisual();
        PlaceTimers();
    }

    // ================================================================ the moments

    /// <summary>
    /// The hype burst (a fight's start, a kill, a new leader): speed lines streak across the meter's plate and a chrome glint
    /// races along every bar, row after row; <paramref name="crown"/> slams the bolt badge onto the top row as well.
    /// </summary>
    internal void Burst(bool crown)
    {
        if (Meter is not { } meter || _fight.Visibility != Visibility.Visible) return;
        int i = 0;
        foreach (var m in meter.Members)
            if (meter.RowOf(m.Id) is HypeRow r) r.Shine(i++ * 70, crown && m.Rank == 1);
        if (crown && meter.Members.FirstOrDefault(m => m.Rank == 1 && !m.CpOnly) is { } top) CutIn(top.Name);
        bool placed = false;
        Motion.Play(_streaks, 900, t =>
        {
            double w = _fight.ActualWidth, h = _fight.ActualHeight;
            if (w < 12 || h < 12) return;
            if (!placed)
            {
                // once per burst: the canvas clipped to the plate, each line at its height
                placed = true;
                _streaks.Clip = PlateGeo(new Rect(1, 1, w - 4, h - 5), Cut);
                for (int k = 0; k < _streakBars.Count; k++) Canvas.SetTop(_streakBars[k].Bar, Math.Round(h * Streaks[k].Y));
                _streaks.Visibility = Visibility.Visible;
            }
            for (int k = 0; k < _streakBars.Count; k++)
            {
                var (_, v, d, l, _, _) = Streaks[k];
                var (bar, at) = _streakBars[k];
                double p = Math.Clamp((t - d) / (1 - d), 0, 1);
                if (p <= 0 || p >= 1) { bar.Opacity = 0; continue; }
                at.X = -l + (w + 2 * l) * Math.Min(1, p * v * 1.15);
                bar.Opacity = .95 * Math.Sin(Math.PI * p);
            }
        }, () => _streaks.Visibility = Visibility.Collapsed);
    }

    /// <summary>A word drawn as vector lettering (the cut-in's art, not a text of the layout): its outline geometry, frozen.</summary>
    static Geometry LetterGeo(string s, double size, FontFamily face, FontWeight weight, FontStretch stretch)
    {
        var ft = new FormattedText(s, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(face, FontStyles.Italic, weight, stretch), size, Brushes.Black, 1.0);
        var g = ft.BuildGeometry(new Point(0, 0));
        g.Freeze();
        return g;
    }

    /// <summary>Vector lettering in black (the cut-in's "#1").</summary>
    static System.Windows.Shapes.Path Lettering(string s, double size, FontFamily face, FontWeight weight, FontStretch stretch) =>
        new() { Data = LetterGeo(s, size, face, weight, stretch), Fill = Black };

    Grid? _cutIn;
    System.Windows.Shapes.Path? _cutName;
    /// <summary>The head's title tab and outcome chip: the moments hide them under their own words (the clock stays).</summary>
    FrameworkElement? _titleTab, _outcomeChip;
    readonly ScaleTransform _cutScale = new(1, 1);
    readonly SkewTransform _cutSkew = new();
    Border? _cutFlash;
    readonly TranslateTransform _cutShift = new();

    /// <summary>
    /// The new leader's cut-in: "#1" in Impact and their name, black on a lime slab, slamming in at the meter's top right from
    /// twice its size, held, then whipped off to the left (950 ms). Built once; the name is set once per cut-in; only its
    /// transforms and opacity move.
    /// </summary>
    void CutIn(string name)
    {
        if (_cutIn == null)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            var num = Lettering("#1", 22, Punch, FontWeights.Normal, FontStretches.Normal);
            num.VerticalAlignment = VerticalAlignment.Bottom;
            sp.Children.Add(num);
            _cutName = new System.Windows.Shapes.Path { Fill = Black, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(7, 0, 0, 0) };
            sp.Children.Add(_cutName);
            var slab = Chip(sp, Mod.Grad(false, (Color.FromRgb(0xA8, 0xE0, 0x00), 0), (LimeC, .3), (Color.FromRgb(0xE4, 0xFF, 0x7A), 1)), Black, new Thickness(11, 3, 13, 3), 8);
            var under = new Border { Background = Frozen(MagC), Margin = new Thickness(5, 4, -4, -4) };
            under.RenderTransform = new SkewTransform(-14, 0);
            _cutFlash = new Border { Background = White, Opacity = 0, Margin = new Thickness(3, 0, 3, 0), RenderTransform = new SkewTransform(-14, 0), RenderTransformOrigin = new Point(.5, .5) };   // sized by the slab, never sizing it
            // in the title tab's place at the plate's top-left: the clock and the HP stay in view
            _cutIn = new Grid { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(6, -6, 0, 0), Opacity = 0, IsHitTestVisible = false };
            _cutIn.Children.Add(under); _cutIn.Children.Add(slab); _cutIn.Children.Add(_cutFlash);
            _cutIn.RenderTransformOrigin = new Point(0, .5);
            _cutIn.RenderTransform = new TransformGroup { Children = { _cutSkew, _cutScale, _cutShift } };
            _fightFx.Children.Add(_cutIn);
        }
        if (_titleTab != null) _cutIn!.MinWidth = Math.Max(0, _titleTab.ActualWidth + 2);
        _cutName!.Data = LetterGeo(name.Length > 14 ? name[..13].TrimEnd() + "…" : name, 21, Face, FontWeights.Bold, FontStretches.Condensed);
        var cut = _cutIn;
        var flash = _cutFlash!;
        var tab = _titleTab;
        Motion.Play(cut, 620, t =>
        {
            // slam: 1.6x and leaning -12 deg to its size and upright in 140 ms, a white flash plate on impact, held, whipped off left
            double k = Seg(t, 0, .22), o = Seg(t, .78, 1);
            _cutScale.ScaleX = _cutScale.ScaleY = k <= 0 ? 1.6 : Math.Max(.94, 1.6 - .6 * Back(k, 3));
            _cutSkew.AngleX = -12 * (1 - Out(k));
            _cutShift.X = 20 * (1 - Out(k)) + 60 * o * o;
            flash.Opacity = t is >= .2 and < .33 ? 1 - Seg(t, .2, .33) : 0;
            cut.Opacity = t < .78 ? Math.Min(1, k * 3) : 1 - Seg(t, .78, .9);
            if (tab != null) tab.Opacity = t < .78 ? 1 - Seg(t, 0, .12) : Seg(t, .9, 1);
        }, () => { cut.Opacity = 0; if (tab != null) tab.Opacity = 1; flash.Opacity = 0; _cutScale.ScaleX = _cutScale.ScaleY = 1; _cutSkew.AngleX = 0; _cutShift.X = 0; });
    }

    /// <summary>The moving speed lines: (height share, speed, delay, length px, thickness, colour), streaking left to right.</summary>
    static readonly (double Y, double V, double D, double L, double T, int C)[] Streaks =
    {
        (.16, 1.0, .00, 120, 2, 0), (.27, 1.3, .10, 70, 1, 1), (.39, .9, .05, 160, 1.5, 2), (.50, 1.2, .18, 90, 2.5, 0),
        (.61, 1.0, .08, 140, 1, 1), (.72, 1.4, .22, 80, 2, 0), (.84, .95, .14, 110, 1.5, 1), (.93, 1.2, .02, 60, 1, 2),
    };

    /// <summary>A pickup: a lime glint races across the Buddy's plate and the worth slams in beside it on a black tag, then fades.</summary>
    protected override void Pickup(Pick p, bool held)
    {
        if (KinahHidden || _line.Visibility != Visibility.Visible) return;
        var worth = p.Worth.Length > 0 ? "+" + p.Worth.TrimStart('+') : p.Count;
        var words = new StackPanel();
        words.Children.Add(Ht(worth, 21, p.Worth.Length > 0 ? Lime : White, FontWeights.Bold));
        var nm = Ht(p.Name.Length > 13 ? p.Name[..12].TrimEnd() + "…" : p.Name, 10, Soft, FontWeights.SemiBold); nm.Margin = new Thickness(0, -2, 0, 0);
        words.Children.Add(nm);
        var chip = Chip(Shaded(words), Frozen(Mod.A(InkC, .92)), Chrome, new Thickness(9, 2, 11, 4), 6);
        chip.MaxWidth = 88;
        var shift = new TranslateTransform();
        var sc = new ScaleTransform(1, 1);
        chip.RenderTransformOrigin = new Point(0, .5);
        chip.RenderTransform = new TransformGroup { Children = { sc, shift } };
        chip.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(chip, 6); Canvas.SetTop(chip, -chip.DesiredSize.Height / 2);
        _lineAnchor.Children.Clear(); _lineAnchor.Children.Add(chip);
        Motion.Play(chip, 1900, t =>
        {
            Sheen(t < .42 ? Seg(t, 0, .42) : -1);
            // the slam: in from the left a touch big, overshooting, landing hard
            double k = Seg(t, .04, .2);
            shift.X = -14 * (1 - Out(k));
            sc.ScaleX = sc.ScaleY = k <= 0 ? 1.5 : Math.Max(.8, 1.5 - .5 * Back(k, 3));
            chip.Opacity = t < .82 ? Math.Min(1, k * 3) : 1 - Seg(t, .82, 1);
        }, () => { Sheen(-1); _lineAnchor.Children.Remove(chip); });
    }

    /// <summary>A rare drop: its plate unrolls from the left (a clip widening) flashing its grade, the tile slams in from big.</summary>
    protected override void RareDrop(Pick p, bool held)
    {
        if (_rareFlash is not { } flash) return;
        var tile = _rareTile;
        var sc = new ScaleTransform(1, 1);
        if (tile != null) { tile.RenderTransformOrigin = new Point(.5, .5); tile.RenderTransform = sc; }
        // the slab skids in from the right leaning hard, overshoots upright, flashes white on landing; the tile slams in
        var skew = new SkewTransform(0, 0);
        var at = new TranslateTransform();
        _rare.RenderTransformOrigin = new Point(0, 1);
        _rare.RenderTransform = new TransformGroup { Children = { skew, at } };
        // the landing in the grade's colour (a pickup is lime on black: this floods the slab with the grade), shards of it off
        // the tile and a hard jolt
        var gc = ColorOf(p.Color, p.Grade);
        var flood = new TacSketch((dc, s) => { if (s.Width > 16) dc.DrawGeometry(Frozen(Mod.A(gc, .9)), null, Para(new Rect(1, 1, s.Width - 10, s.Height - 6), 9)); }) { Opacity = 0, IsHitTestVisible = false, RenderTransformOrigin = new Point(1, .5), RenderTransform = new ScaleTransform(1, 1) };
        var floodSc = (ScaleTransform)flood.RenderTransform;
        _rare.Children.Insert(Math.Max(0, _rare.Children.IndexOf(flash)), flood);
        Point o = new(30, 20);
        try { if (tile != null && tile.ActualWidth > 0) o = tile.TranslatePoint(new Point(tile.ActualWidth / 2, tile.ActualHeight / 2), _rare); } catch (InvalidOperationException) { }
        EsportsFx.Spray(_rare, o, 9, 34, -175, 5, new[] { gc, Mod.Mix(gc, Colors.White, .5), Colors.White }, 1000, fall: 12, delay: .2, sparks: true);
        Motion.Play(flash, 1200, t =>
        {
            double k = Seg(t, 0, .32);
            at.X = 40 * (1 - Out(k)) + EsportsFx.Shake(t, .22, .4, 3, 2.5);
            skew.AngleX = k <= 0 ? -24 : -24 + 24 * Back(k, 3.2);
            _rare.Opacity = Math.Min(1, k * 4);
            flash.Opacity = t is >= .22 and < .3 ? .45 * (1 - Seg(t, .22, .3)) : 0;
            // the slab floods with the grade as it lands, holds through the stamp and the shards, then the lime wipes back in from the left
            flood.Opacity = t < .05 ? 0 : Math.Min(1, Seg(t, .05, .12) * 1.4);
            floodSc.ScaleX = 1 - Out(Seg(t, .6, .74));
            double s = Seg(t, .1, .26);
            sc.ScaleX = sc.ScaleY = s <= 0 ? 0 : 1.7 - .7 * Back(s, 3.2);
        }, () => { at.X = 0; skew.AngleX = 0; _rare.Opacity = 1; flash.Opacity = 0; sc.ScaleX = sc.ScaleY = 1; _rare.Children.Remove(flood); });
    }

    /// <summary>Your best passed: the gauge sweeps on round to full in lime and flares, a lime glint races the plate.</summary>
    protected override void BestPassed(bool held)
    {
        if (_gaugeArt is not { } art) return;
        double was = _share is > 0 and < 1 ? _share : .86;
        bool beat = _beat;
        // the hype: as the needle pins, the gauge slams bigger and back, confetti bursts off it, and a NEW BEST slab in lime
        // smashes in beside the Buddy (a pickup's tag is black with a lime figure: this is the whole slab lit)
        var gauge = art.Parent as Grid;
        var gsc = new ScaleTransform(1, 1);
        if (gauge != null) { gauge.RenderTransformOrigin = new Point(.5, .5); gauge.RenderTransform = gsc; }
        var word = Lettering(L.T("New best").ToUpperInvariant(), 15.5, Punch, FontWeights.Normal, FontStretches.Normal);
        var slab = Chip(word, Mod.Grad(false, (Color.FromRgb(0xA8, 0xE0, 0x00), 0), (LimeC, .3), (Color.FromRgb(0xE4, 0xFF, 0x7A), 1)), Black, new Thickness(10, 4, 12, 4), 8);
        var tag = new Grid();
        tag.Children.Add(new Border { Background = Mag, Margin = new Thickness(4, 4, -4, -4), RenderTransform = new SkewTransform(-14, 0) });
        tag.Children.Add(slab);
        var hit = new Border { Background = White, Opacity = 0, IsHitTestVisible = false, Margin = new Thickness(3, 0, 3, 0), RenderTransform = new SkewTransform(-14, 0), RenderTransformOrigin = new Point(.5, .5) };
        tag.Children.Add(hit);
        var tsc = new ScaleTransform(1, 1);
        var tshift = new TranslateTransform();
        tag.RenderTransformOrigin = new Point(0, .5);
        tag.RenderTransform = new TransformGroup { Children = { new SkewTransform(0, 0), tsc, tshift } };
        var tskew = (SkewTransform)((TransformGroup)tag.RenderTransform).Children[0];
        tag.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(tag, 4); Canvas.SetTop(tag, -tag.DesiredSize.Height / 2);
        tag.Opacity = 0;
        _lineAnchor.Children.Clear(); _lineAnchor.Children.Add(tag);
        if (gauge != null)
        {
            double gx = -(_line.ActualWidth > 0 ? _line.ActualWidth : 220) + 36;
            EsportsFx.Spray(_lineAnchor, new Point(gx, 0), 5, 40, -38, 22, new[] { LimeC, MagC, Colors.White }, 1300, fall: 8, delay: .36);
            EsportsFx.Spray(_lineAnchor, new Point(gx, 0), 5, 34, 158, 218, new[] { MagC, LimeC, Colors.White }, 1300, fall: 8, delay: .38);
        }
        Motion.Play(art, 1800, t =>
        {
            _beat = false; _share = was;
            _complete = Out(Seg(t, 0, .4));
            if (t >= .4) { _beat = beat; _complete = 1; }
            _gaugeFlash = t < .4 ? 0 : 1 - Seg(t, .4, .9);
            if (t <= .92) art.InvalidateVisual();
            Sheen(t is > .3 and < .8 ? Seg(t, .3, .8) : -1);
            gsc.ScaleX = gsc.ScaleY = t < .4 ? 1 - .05 * Seg(t, .25, .4) : EsportsFx.Punch(Seg(t, .4, .62), 1.28, 3);
            double k = Seg(t, .4, .52), o = Seg(t, .84, 1);
            tsc.ScaleX = k <= 0 ? 1.1 : EsportsFx.Punch(k, 1.1, 3); tsc.ScaleY = k <= 0 ? 1.7 : EsportsFx.Punch(k, 1.7, 3);
            tskew.AngleX = -14 * (1 - Out(k));
            tshift.X = -18 * (1 - Out(k)) - 12 * o;
            tag.Opacity = t < .4 ? 0 : t < .84 ? Math.Min(1, k * 3) : 1 - o;
            hit.Opacity = t is >= .5 and < .6 ? .9 * (1 - Seg(t, .5, .6)) : 0;
        }, () => { _complete = 0; _beat = beat; _gaugeFlash = 0; Sheen(-1); art.InvalidateVisual(); gsc.ScaleX = gsc.ScaleY = 1; _lineAnchor.Children.Remove(tag); });
    }

    /// <summary>
    /// An entry opened: the first timer's chip slams in like the leader's cut-in, from 1.7x and leaning hard, lands with one
    /// white frame and a lime shockwave going out round it, and settles upright. Transforms and opacity only.
    /// </summary>
    protected override void EntryOpened(TimerBoard.Item i, bool held)
    {
        if (_firstChip is not { } chip) return;
        var sc = new ScaleTransform(1, 1);
        var skew = new SkewTransform(0, 0);
        chip.RenderTransformOrigin = new Point(.5, .5);
        chip.RenderTransform = new TransformGroup { Children = { skew, sc } };
        // the impact's white frame over the chip and its lime shockwave, made for this moment and gone after it
        var flash = new Border { Background = White, Opacity = 0, IsHitTestVisible = false, RenderTransform = new SkewTransform(-14, 0), RenderTransformOrigin = new Point(.5, .5) };
        var wave = new Border { BorderBrush = Lime, BorderThickness = new Thickness(2.2), Opacity = 0, IsHitTestVisible = false, Margin = new Thickness(-2), RenderTransformOrigin = new Point(.5, .5) };
        var waveSc = new ScaleTransform(1, 1);
        wave.RenderTransform = new TransformGroup { Children = { new SkewTransform(-14, 0), waveSc } };
        chip.Children.Add(flash); chip.Children.Add(wave);
        Motion.Play(chip, 900, t =>
        {
            double k = Seg(t, 0, .2);
            sc.ScaleX = sc.ScaleY = k <= 0 ? 1.3 : EsportsFx.Punch(k, 1.3, 3);
            skew.AngleX = -16 * (1 - Out(k));
            chip.Opacity = Math.Min(1, .1 + k * 3);
            flash.Opacity = t is >= .18 and < .3 ? .9 * (1 - Seg(t, .18, .3)) : 0;
            double w = Seg(t, .18, .6);
            waveSc.ScaleX = 1 + .1 * Out(w); waveSc.ScaleY = 1 + .45 * Out(w);
            wave.Opacity = w is > 0 and < 1 ? 1 - w : 0;
        }, () => { sc.ScaleX = sc.ScaleY = 1; skew.AngleX = 0; chip.Opacity = 1; chip.Children.Remove(flash); chip.Children.Remove(wave); });
    }

    /// <summary>A fight started: the meter snaps upright from a forward lean, the bolt badge slams onto the leader and the burst plays.</summary>
    protected override void FightStarted(Encounter e, bool held)
    {
        // leaning forward from its foot (the left edge stays inside the window), straightening with a small kick
        _fightSkew.CenterY = _fight.ActualHeight > 0 ? _fight.ActualHeight : 200;
        Motion.Play(_fight, 420, t =>
        {
            _fightSkew.AngleX = -16 * (1 - Out(t));
            _fightShift.X = -5 * (1 - Back(t, 1.6));
            _fightBody.Opacity = Math.Min(1, .6 + t * 5);
        }, () => { _fightSkew.AngleX = 0; _fightShift.X = 0; _fightBody.Opacity = 1; });
        Burst(crown: true);
    }

    /// <summary>
    /// A fight ended: a black band with lime edges (magenta for a wipe) slams over the fight's head from the left with the outcome
    /// in Impact and the clock, then lifts; a kill bursts the bars as well.
    /// </summary>
    protected override void FightEnded(Encounter e, bool held)
    {
        if (e.Outcome is not ("kill" or "wipe" or "cleared")) return;
        bool wipe = e.Outcome == "wipe";
        var c = wipe ? MagC : LimeC;
        // the giant word: vector lettering twice the head's height, a magenta (a wipe: black) copy knocked behind it, breaking
        // the plate's top edge; Impact for Latin, the bold CJK face for ko / zh (Impact has no Hangul or Han)
        var word = OutcomeText(e).ToUpperInvariant();
        bool latin = word.All(ch => ch < 0x2E80);
        var geo = LetterGeo(word, latin ? 34 : 30, latin ? Punch : PunchCjk, latin ? FontWeights.Normal : FontWeights.Bold, FontStretches.Normal);
        var copy = new System.Windows.Shapes.Path { Data = geo, Fill = wipe ? Black : Mag, RenderTransform = new TranslateTransform(4, 3) };
        var main = new System.Windows.Shapes.Path { Data = geo, Fill = Frozen(c), Stroke = Black, StrokeThickness = latin ? 1.6 : 1 };
        var letters = new Grid { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(16, -7 - geo.Bounds.Top, 0, 0) };
        letters.Children.Add(copy); letters.Children.Add(main);
        var wsc = new ScaleTransform(1, 1);
        letters.RenderTransformOrigin = new Point(.3, .5);
        letters.RenderTransform = new TransformGroup { Children = { new SkewTransform(-8, 0), wsc } };
        // the impact ring round the word, going out once
        double gw = geo.Bounds.Width;
        var ring = new System.Windows.Shapes.Ellipse { Width = 140, Height = 30, Stroke = Frozen(c), StrokeThickness = 3, Opacity = 0, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        double cx = Math.Max(78, 16 + gw / 2);
        ring.Margin = new Thickness(cx - 70, -2, 0, 0);
        var rsc = new ScaleTransform(.5, .5, 70, 15);
        ring.RenderTransform = rsc;
        _bandHost.Children.Clear(); _bandHost.Children.Add(ring); _bandHost.Children.Add(letters);
        var tab = _titleTab;
        var chip = _outcomeChip;
        if (wipe)
        {
            // a wipe is the other way round: no slam and no burst. The word drags in heavy from the right and lands with a thud,
            // its black copy tearing sideways (a broken feed), the whole meter shudders and the rows go dark for a beat
            var tear = (TranslateTransform)copy.RenderTransform;
            var fall = new TranslateTransform();
            ((TransformGroup)letters.RenderTransform).Children.Add(fall);
            Motion.Play(_bandHost, 1900, t =>
            {
                double k = Seg(t, 0, .16), o = Seg(t, .8, 1);
                fall.X = 46 * (1 - Out(k)); fall.Y = t < .16 ? 0 : 3.5 * Math.Sin(Math.PI * Seg(t, .16, .26));
                wsc.ScaleX = wsc.ScaleY = 1;
                letters.Opacity = t < .8 ? Math.Min(1, k * 2.5) : 1 - Seg(t, .8, .9);
                tear.X = 4 + (t is > .16 and < .5 ? 9 * Math.Sin(t * 140) * (1 - Seg(t, .16, .5)) : 0);
                tear.Y = 3;
                ring.Opacity = 0;
                _fightShift.X = t < .16 ? 0 : EsportsFx.Shake(t, .16, .42, 5, 3);
                _fightBody.Opacity = t < .16 ? 1 : .5 + .5 * Out(Seg(t, .2, .55));
                double hide = t < .8 ? 1 - Seg(t, 0, .03) : Seg(t, .9, 1);
                if (tab != null) tab.Opacity = hide;
                if (chip != null) chip.Opacity = hide;
            }, () => { _bandHost.Children.Clear(); _fightShift.X = 0; _fightBody.Opacity = 1; if (tab != null) tab.Opacity = 1; if (chip != null) chip.Opacity = 1; });
            return;
        }
        // a kill: the slam, one white frame on the plate, the shockwave ring and a spray of lime, magenta and white confetti off the word
        // the white frame in the plate's own cut shape (the plate as laid out; none before the first layout)
        var whiteout = new System.Windows.Shapes.Path { Fill = Frozen(Mod.A(Colors.White, .5)), Opacity = 0, IsHitTestVisible = false };
        if (_fight.ActualWidth > 12 && _fight.ActualHeight > 12) whiteout.Data = PlateGeo(new Rect(1, 1, _fight.ActualWidth - 4, _fight.ActualHeight - 5), Cut);
        _fightFx.Children.Insert(0, whiteout);
        // fired off the word's end along the head (never up past the window's top), drifting down over the plate as they fade
        EsportsFx.Spray(_bandHost, new Point(16 + gw, 14), 12, 130, 6, 48, new[] { LimeC, MagC, Colors.White, LimeC }, 1500, fall: 34, delay: .1);
        Motion.Play(_bandHost, 1900, t =>
        {
            double k = Seg(t, 0, .14), o = Seg(t, .8, 1);
            wsc.ScaleX = wsc.ScaleY = k <= 0 ? 2.1 : Math.Max(.86, 2.1 - 1.1 * Back(k, 3.6));
            letters.Opacity = t < .8 ? Math.Min(1, k * 3) : 1 - Seg(t, .8, .9);
            double rg = Seg(t, .12, .5);
            rsc.ScaleX = rsc.ScaleY = .5 + .6 * Out(rg);
            ring.Opacity = rg is > 0 and < 1 ? 1 - rg : 0;
            whiteout.Opacity = t is >= .12 and < .22 ? 1 - Seg(t, .12, .22) : 0;
            _fightShift.Y = t < .13 ? 0 : Math.Abs(EsportsFx.Shake(t, .13, .3, 3.5, 1.5));
            double hide = t < .8 ? 1 - Seg(t, 0, .03) : Seg(t, .9, 1);
            if (tab != null) tab.Opacity = hide;
            if (chip != null) chip.Opacity = hide;
        }, () => { _bandHost.Children.Clear(); _fightFx.Children.Remove(whiteout); _fightShift.Y = 0; if (tab != null) tab.Opacity = 1; if (chip != null) chip.Opacity = 1; });
        Burst(crown: false);
    }
}
