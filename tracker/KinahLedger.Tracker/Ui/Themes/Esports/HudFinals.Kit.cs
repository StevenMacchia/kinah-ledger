using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using Path = System.Windows.Shapes.Path;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

/// <summary>
/// Finals' parts (the Esports theme's fourth look: a world championship's grand final on the stream): the palette of matte
/// black and brushed gold, the plate every panel is cut from (black under a stage light falling from the top, a brushed-gold
/// rim and a thin gold line inside it), the trophy cup over the leader, the medal, the confetti, and the tall condensed caps.
/// </summary>
sealed partial class HudFinals
{
    // ---- the palette: matte black, graphite, brushed gold; mint and coral only where they mean something (heals, a wipe)
    static readonly Color Black0 = Color.FromRgb(0x06, 0x06, 0x07), Black1 = Color.FromRgb(0x13, 0x13, 0x15),
        AuC = Color.FromRgb(0xE3, 0xB5, 0x4E), AuLightC = Color.FromRgb(0xFF, 0xE1, 0x8E), AuPaleC = Color.FromRgb(0xFF, 0xF1, 0xC8), AuDeepC = Color.FromRgb(0x9A, 0x70, 0x26),
        BronzeC = Color.FromRgb(0x3C, 0x2C, 0x0E), SpotC = Color.FromRgb(0xFF, 0xF0, 0xCC),
        MintC = Color.FromRgb(0x6C, 0xF0, 0xAE), CoralC = Color.FromRgb(0xFF, 0x7E, 0x6E), HpAC = Color.FromRgb(0xB8, 0x24, 0x30), HpBC = Color.FromRgb(0xFF, 0x5E, 0x48),
        WarnC2 = Color.FromRgb(0xFF, 0xB0, 0x76);
    static readonly SolidColorBrush White = Frozen(Colors.White), Soft = Mod.Br(Colors.White, .84), Quiet = Mod.Br(Colors.White, .62), Faint = Mod.Br(Colors.White, .42),
        Au = Frozen(AuC), AuLight = Frozen(AuLightC), AuPale = Frozen(AuPaleC), AuSoft = Mod.Br(AuLightC, .8), Mint2 = Frozen(MintC), Coral = Frozen(CoralC);

    /// <summary>Bahnschrift (every Windows 10 and 11 has it): condensed for the titles and labels, its tabular digits for the figures.</summary>
    static readonly FontFamily Bahn = HudThemes.Bahn;

    /// <summary>Brushed gold metal, for a rim or a fill: light and dark bands across it, as a lathe leaves them.</summary>
    static readonly LinearGradientBrush GoldMetal = MakeGoldMetal();

    static LinearGradientBrush MakeGoldMetal()
    {
        var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
        foreach (var (c, at) in new[] { (0x7A5A1E, 0.0), (0xE8C46A, .16), (0xFFF0B8, .28), (0xC9993E, .42), (0x8C6420, .56), (0xE3B54E, .72), (0xFFE6A0, .84), (0x9C7424, 1.0) })
            b.GradientStops.Add(new GradientStop(Color.FromRgb((byte)(c >> 16), (byte)(c >> 8), (byte)c), at));
        b.Freeze();
        return b;
    }

    /// <summary>Gold lettering for the headline figures: pale at the top, rich in the middle, deep at the foot.</summary>
    static readonly LinearGradientBrush GoldInk = Mod.Grad(true, (Color.FromRgb(0xFF, 0xF6, 0xD8), 0), (Color.FromRgb(0xFF, 0xDB, 0x80), .45), (Color.FromRgb(0xF0, 0xBC, 0x52), .62), (Color.FromRgb(0xD0, 0x9A, 0x3A), 1));

    /// <summary>The fine horizontal grain of brushed metal: a tile of faint light and dark streaks, made once.</summary>
    static readonly ImageBrush Brushed = MakeBrushed();

    static ImageBrush MakeBrushed()
    {
        const int w = 192, h = 24;
        var px = new byte[w * h * 4];
        var rnd = new Random(2026);
        for (int y = 0; y < h; y++)
        {
            double row = rnd.NextDouble() * 2 - 1, seg = 0;
            for (int x = 0; x < w; x++)
            {
                if (x % 16 == 0) seg = rnd.NextDouble() * 2 - 1;
                double v = row * .75 + seg * .25;
                byte a = (byte)Math.Min(255, Math.Abs(v) * 46), c = v > 0 ? (byte)255 : (byte)0;
                int i = (y * w + x) * 4;
                px[i] = px[i + 1] = px[i + 2] = (byte)(c * a / 255); px[i + 3] = a;
            }
        }
        var bmp = System.Windows.Media.Imaging.BitmapSource.Create(w, h, 96, 96, PixelFormats.Pbgra32, null, px, w * 4);
        bmp.Freeze();
        var b = new ImageBrush(bmp) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, w, h), ViewportUnits = BrushMappingMode.Absolute, Stretch = Stretch.None };
        b.Freeze();
        return b;
    }

    // ================================================================ the plate

    /// <summary>
    /// The plate every panel is: a soft drop under it (outside its shape, inside the window's room), matte black deepening to
    /// its foot, a stage light falling from above its top edge, a brushed-gold rim, a thin gold line inside it and a gleam on
    /// the rim's top. Drawn once (again only when its size or the opacity setting changes); the lights that cross it are
    /// <see cref="Band"/>s over it.
    /// </summary>
    static void Plate(DrawingContext dc, Size s, double radius, double opacity, bool shadow = true, bool spot = true, double rim = 1, int lamps = 0, bool beams = false, bool rig = false)
    {
        PlateBody(dc, s, radius, opacity, shadow, spot, rim);
        if (lamps > 0) Rig(dc, s, lamps, beams, rig);
        if (rim > 0 && lamps > 0) Brackets(dc, s);
    }

    /// <summary>
    /// The lighting rig over a plate's top edge: a brushed-gold truss with <paramref name="n"/> lamp heads hanging from it, each
    /// with a lit lens; with <paramref name="beams"/> each lamp throws a faint cone of light down into the plate (drawn once;
    /// the meter's own beams are a layer that moves in its moments, <see cref="StageBeams"/>).
    /// </summary>
    static void Rig(DrawingContext dc, Size s, int n, bool beams, bool big)
    {
        double w = s.Width;
        if (beams)
        {
            dc.PushClip(Mod.Pill(new Rect(.75, .75, w - 1.5, s.Height - 1.5), 4));
            for (int i = 0; i < n; i++)
            {
                double x = LampX(w, n, i), ang = BeamRest(n, i) * Math.PI / 180, len = s.Height * 1.1, spread = Math.Min(70, s.Height * .9);
                var g = new StreamGeometry();
                using (var c = g.Open())
                {
                    Point P(double px, double py) => new(x + px * Math.Cos(ang) - py * Math.Sin(ang), 6 + px * Math.Sin(ang) + py * Math.Cos(ang));
                    c.BeginFigure(P(-2, 0), true, true); c.LineTo(P(2, 0), true, false); c.LineTo(P(spread / 2, len), true, false); c.LineTo(P(-spread / 2, len), true, false);
                }
                g.Freeze();
                dc.DrawGeometry(BeamFillStatic, null, g);
            }
            dc.Pop();
        }
        // the truss: a slim gold bar along the top edge, a lattice of dark slits in it
        if (big)
        {
            // the meter's rig: a real girder across the whole top, two gold chords with a lattice of diagonals between them
            var g = new Rect(5, -2, Math.Max(0, w - 10), 7.5);
            dc.DrawRectangle(Frozen(Color.FromRgb(0x1C, 0x16, 0x09)), null, g);
            var lattice = new StreamGeometry();
            using (var c = lattice.Open())
                for (double x = g.X + 2; x + 6 <= g.Right - 1; x += 6)
                {
                    c.BeginFigure(new Point(x, g.Y + 1), false, false); c.LineTo(new Point(x + 6, g.Bottom - 1), true, false);
                    c.BeginFigure(new Point(x + 6, g.Y + 1), false, false); c.LineTo(new Point(x, g.Bottom - 1), true, false);
                }
            lattice.Freeze();
            dc.DrawGeometry(null, Mod.P(Mod.Br(AuC, .55), .8), lattice);
            dc.DrawRectangle(GoldMetal, null, new Rect(g.X, g.Y, g.Width, 1.4));
            dc.DrawRectangle(GoldMetal, null, new Rect(g.X, g.Bottom - 1.4, g.Width, 1.4));
        }
        else
        {
            var truss = new Rect(12, -1.5, Math.Max(0, w - 24), 4);
            dc.DrawRectangle(Frozen(Color.FromRgb(0x2A, 0x21, 0x0E)), Mod.P(GoldMetal, 1), truss);
            for (double x = truss.X + 5; x < truss.Right - 3; x += 7) dc.DrawRectangle(Mod.Br(Colors.Black, .55), null, new Rect(x, truss.Y + 1, 2.5, 2));
        }
        for (int i = 0; i < n; i++)
        {
            double x = LampX(w, n, i);
            // the lamp head: a dark housing rimmed in gold under the truss, its lens lit
            if (big)
            {
                // a stage lamp: a can hung from the girder, its hot lens glowing at its mouth
                dc.DrawEllipse(LampGlow, null, new Point(x, 12.5), 13, 7);
                var can = new Rect(x - 7, 3, 14, 10);
                dc.DrawRoundedRectangle(Frozen(Color.FromRgb(0x18, 0x14, 0x0B)), Mod.P(GoldMetal, 1.1), can, 2, 2);
                dc.DrawRectangle(Mod.Br(AuC, .5), null, new Rect(x - 5, 5.5, 10, 1));
                dc.DrawEllipse(LensFill, null, new Point(x, 12.4), 6, 2.6);
            }
            else
            {
                var head = new Rect(x - 5.5, 1.5, 11, 6.5);
                dc.DrawRoundedRectangle(Frozen(Color.FromRgb(0x16, 0x13, 0x0C)), Mod.P(GoldMetal, 1), head, 1.5, 1.5);
                dc.DrawEllipse(LensFill, null, new Point(x, 7.2), 4.2, 2);
            }
        }
    }

    /// <summary>The lamps' places across a plate: evenly, in from its corners.</summary>
    static double LampX(double w, int n, int i) => Math.Round(w * (i + 1) / (n + 1));

    /// <summary>A beam's angle at rest (degrees, 0 straight down): the outer lamps aim inward, the inner ones cross a little.</summary>
    static double BeamRest(int n, int i)
    {
        double mid = (n - 1) / 2.0, k = (i - mid) / Math.Max(1, mid);
        return -k * 22 + (i % 2 == 0 ? 5 : -5);
    }

    static readonly Brush LampGlow = MakeLampGlow();
    static Brush MakeLampGlow()
    {
        var b = new RadialGradientBrush();
        b.GradientStops.Add(new GradientStop(Mod.A(AuPaleC, .55), 0));
        b.GradientStops.Add(new GradientStop(Mod.A(AuC, 0), 1));
        b.Freeze();
        return b;
    }
    static readonly Brush BeamFillStatic = Mod.Grad(true, (Mod.A(SpotC, .13), 0), (Mod.A(SpotC, .05), .45), (Mod.A(SpotC, 0), 1));
    static readonly Brush LensFill = MakeLens();
    static Brush MakeLens()
    {
        var b = new RadialGradientBrush();
        b.GradientStops.Add(new GradientStop(Colors.White, 0));
        b.GradientStops.Add(new GradientStop(AuPaleC, .5));
        b.GradientStops.Add(new GradientStop(Mod.A(AuC, .6), 1));
        b.Freeze();
        return b;
    }

    /// <summary>Gold corner brackets just outside the rim, as a trophy plinth's corners: an L of brushed gold at each corner.</summary>
    static void Brackets(DrawingContext dc, Size s)
    {
        const double L = 11, o = 2.2;
        var pen = new Pen(GoldMetal, 2) { StartLineCap = PenLineCap.Square, EndLineCap = PenLineCap.Square, LineJoin = PenLineJoin.Miter };
        pen.Freeze();
        double l = -o + 1, t = -o + 1, r = s.Width + o - 1, b = s.Height + o - 1;
        foreach (var (x, y, dx, dy) in new[] { (l, t, 1, 1), (r, t, -1, 1), (l, b, 1, -1), (r, b, -1, -1) })
        {
            var g = new StreamGeometry();
            using (var c = g.Open()) { c.BeginFigure(new Point(x + dx * L, y), false, false); c.LineTo(new Point(x, y), true, false); c.LineTo(new Point(x, y + dy * L), true, false); }
            g.Freeze();
            dc.DrawGeometry(null, pen, g);
        }
    }

    static void PlateBody(DrawingContext dc, Size s, double radius, double opacity, bool shadow, bool spot, double rim)
    {
        if (s.Width < 6 || s.Height < 6) return;
        var r = new Rect(.75, .75, s.Width - 1.5, s.Height - 1.5);
        var shape = Mod.Pill(r, radius);
        if (shadow)
            for (int k = 1; k <= 6; k++)
            {
                var o = new Rect(r.X - k, r.Y - k * .5 + 3, r.Width + k * 2, r.Height + k * 1.5 + 1);
                var ring = new CombinedGeometry(GeometryCombineMode.Exclude, Mod.Pill(o, radius + k), shape);
                ring.Freeze();
                dc.DrawGeometry(Mod.Br(Colors.Black, .07 - k * .009), null, ring);
            }
        double a = Math.Clamp(.66 + .32 * opacity, .74, .97);
        dc.DrawGeometry(Mod.Grad(true, (Mod.A(Black1, a), 0), (Mod.A(Black0, Math.Min(.98, a + .04)), 1)), null, shape);
        dc.PushClip(shape);
        if (spot)
        {
            // the stage light: a warm pool from above the top edge's middle, fading before the foot
            var c = new Point(s.Width / 2, -s.Height * .08);
            var light = new RadialGradientBrush { MappingMode = BrushMappingMode.Absolute, Center = c, GradientOrigin = c, RadiusX = Math.Max(60, s.Width * .58), RadiusY = Math.Max(40, s.Height * .92) };
            light.GradientStops.Add(new GradientStop(Mod.A(SpotC, .15), 0));
            light.GradientStops.Add(new GradientStop(Mod.A(SpotC, .055), .5));
            light.GradientStops.Add(new GradientStop(Mod.A(SpotC, 0), 1));
            light.Freeze();
            dc.DrawRectangle(light, null, new Rect(0, 0, s.Width, s.Height));
        }
        dc.Pop();
        // the rim: brushed gold, a thin gold line inside it, a gleam along the top's middle
        if (rim > 0)
        {
            dc.PushOpacity(rim);
            dc.DrawGeometry(null, Mod.P(GoldMetal, 1.5), shape);
            var inner = new Rect(r.X + 3, r.Y + 3, Math.Max(0, r.Width - 6), Math.Max(0, r.Height - 6));
            dc.DrawGeometry(null, Mod.P(Mod.Br(AuC, .24), 1), Mod.Pill(inner, Math.Max(0, radius - 2)));
            dc.DrawRectangle(Mod.Grad(false, (Mod.A(AuPaleC, 0), 0), (Mod.A(AuPaleC, .95), .5), (Mod.A(AuPaleC, 0), 1)), null, new Rect(s.Width * .3, .25, s.Width * .4, 1.2));
            dc.Pop();
        }
    }

    /// <summary>A gold rule across, brightest in its middle (under the fight's head, in the card).</summary>
    static FrameworkElement GoldRule(Thickness m, double a = .85) => new TacSketch((dc, s) =>
        dc.DrawRectangle(Mod.Grad(false, (Mod.A(AuC, 0), 0), (Mod.A(AuLightC, a), .5), (Mod.A(AuC, 0), 1)), null, new Rect(0, 0, s.Width, 1))) { Height = 1, Margin = m };

    // ================================================================ the words

    /// <summary>A text in Bahnschrift (condensed when <paramref name="cond"/>), tabular digits, never cut.</summary>
    static TextBlock T(string s, double size, Brush fg, FontWeight w, bool cond = false)
    {
        var t = Tt(s, size, fg, w, Bahn);
        if (cond) t.FontStretch = FontStretches.Condensed;
        return t;
    }

    /// <summary>A label in the broadcast's caps: upper case, and in Latin letters a hair of air between them.</summary>
    static string Caps(string s)
    {
        var u = s.ToUpperInvariant();
        if (u.Any(ch => ch > 0x24F)) return u;
        var sb = new System.Text.StringBuilder(u.Length * 2);
        for (int i = 0; i < u.Length; i++)
        {
            sb.Append(u[i]);
            if (i < u.Length - 1 && u[i] != ' ' && u[i + 1] != ' ') sb.Append(' ');
        }
        return sb.ToString();
    }

    /// <summary>A label in small condensed caps (TOTAL, RARE DROP, the card's words).</summary>
    static TextBlock Label(string s, double size, Brush fg) => T(Caps(s), size, fg, FontWeights.SemiBold, cond: true);

    /// <summary>The tight dark shade under words that sit on a bar (the leader's is gold): 1 px down, soft, near black.</summary>
    static DropShadowEffect Shade(double o = .9) => new() { Color = Colors.Black, Direction = 270, ShadowDepth = 1, BlurRadius = 3, Opacity = o };

    /// <summary>
    /// A small black chip with a gold rim (★ DPS, AVG, the outcome); <paramref name="filled"/>: brushed gold with black words
    /// (YOU). <paramref name="edge"/> another rim's colour (the heal mark's mint).
    /// </summary>
    static Border Chip(string s, Brush? fg = null, Color? edge = null, bool filled = false, double size = 10.5)
    {
        var t = T(s, size, filled ? Frozen(Color.FromRgb(0x14, 0x10, 0x06)) : fg ?? AuLight, FontWeights.Bold, cond: true);
        t.Margin = new Thickness(0, -.5, 0, 0);
        return new Border
        {
            Child = t, CornerRadius = new CornerRadius(2.5), Padding = new Thickness(4.5, 0, 4.5, 1), VerticalAlignment = VerticalAlignment.Center, BorderThickness = new Thickness(1),
            Background = filled ? GoldMetal : Mod.Br(Black0, .82), BorderBrush = filled ? Mod.Br(Colors.Black, .85) : Mod.Br(edge ?? AuC, edge == null ? .7 : .65),
        };
    }

    // ================================================================ the trophy

    /// <summary>The cup, 14 × 16: the bowl, the stem, the base and the plinth (filled); the handles are drawn as strokes.</summary>
    static readonly Geometry CupShape = FreezeG(Geometry.Parse("M2.6,1 L11.4,1 L11.4,4.4 C11.4,7.5 9.4,9.5 7,9.5 C4.6,9.5 2.6,7.5 2.6,4.4 Z M6.1,9.3 L7.9,9.3 L7.6,11.6 L6.4,11.6 Z M4.4,11.6 L9.6,11.6 L9.6,13 L4.4,13 Z M3.3,13.5 L10.7,13.5 L10.7,15.2 L3.3,15.2 Z"));
    static readonly Geometry HandleShape = FreezeG(Geometry.Parse("M2.9,2.3 C0.1,2.3 0.2,6.6 3.5,7.1 M11.1,2.3 C13.9,2.3 13.8,6.6 10.5,7.1"));
    static readonly Geometry SparkShape = FreezeG(Geometry.Parse("M0,-4.5 L1,-1 L4.5,0 L1,1 L0,4.5 L-1,1 L-4.5,0 L-1,-1 Z"));

    static Geometry FreezeG(Geometry g) { g.Freeze(); return g; }

    /// <summary>
    /// A band of light that crosses its host on a slant (a sweep, a glint): one small rectangle with a frozen gradient, moved by
    /// its transform and faded by its opacity, clipped to the host. Nothing is laid out or redrawn while it moves (Steven,
    /// 2026-11-20: "cinematic ... lightweight - not bulky").
    /// </summary>
    sealed class Band : Canvas
    {
        readonly Rectangle _r;
        readonly TranslateTransform _x = new();
        readonly double _w;

        public Band(Color c, double a, double width = 34, double skew = -24)
        {
            _w = width;
            ClipToBounds = true; IsHitTestVisible = false; Opacity = 0;
            _r = new Rectangle { Width = width, Height = 40, Fill = Mod.Grad(false, (Mod.A(c, 0), 0), (Mod.A(c, a), .5), (Mod.A(c, 0), 1)), RenderTransform = new TransformGroup { Children = { new SkewTransform(skew, 0), _x } } };
            Children.Add(_r);
            SizeChanged += (_, e) => { if (e.HeightChanged) _r.Height = Math.Max(1, e.NewSize.Height); };
        }

        /// <summary>A light of another colour for the next course (a rare drop's grade), set once before it plays.</summary>
        public void Tint(Color c, double a) => _r.Fill = Mod.Grad(false, (Mod.A(c, 0), 0), (Mod.A(c, a), .5), (Mod.A(c, 0), 1));

        /// <summary>The band's place across the host, 0 to 1 (outside: hidden), brightest in the middle of its course.</summary>
        public void At(double p)
        {
            if (p is not (> 0 and < 1)) { Opacity = 0; return; }
            double w = ActualWidth > 0 ? ActualWidth : 300;
            if (ActualHeight > 0 && Math.Abs(_r.Height - ActualHeight) > .5) _r.Height = ActualHeight;
            _x.X = -_w - 24 + (w + _w + 48) * p;
            Opacity = Math.Sin(Math.PI * p);
        }
    }

    /// <summary>
    /// The trophy: a gold cup (14 × 16 drawn, <paramref name="scale"/> times that on screen) with a dark outline, a gleam band
    /// that can run over it and a spark at its lip. <see cref="Rise"/> plays its entrance (one course: transforms and opacity
    /// only); at rest it simply stands.
    /// </summary>
    sealed class Trophy : Grid
    {
        readonly TranslateTransform _gleamAt = new(-12, 0);
        readonly Rectangle _gleam;
        readonly ScaleTransform _sparkScale = new(0, 0);
        readonly TranslateTransform _lift = new();
        readonly ScaleTransform _grow = new(1, 1);
        readonly double _scale;

        static readonly Brush CupFill = Mod.Grad(true, (Color.FromRgb(0xFF, 0xF6, 0xD0), 0), (Color.FromRgb(0xF6, 0xCB, 0x62), .36), (Color.FromRgb(0xCF, 0x98, 0x36), .68), (Color.FromRgb(0x8A, 0x60, 0x1C), 1));
        static readonly Brush GleamFill = Mod.Grad(false, (Mod.A(Colors.White, 0), 0), (Mod.A(Colors.White, .95), .5), (Mod.A(Colors.White, 0), 1));
        static readonly Brush Outline = Mod.Br(Colors.Black, .85), HandleGold = Frozen(Color.FromRgb(0xE8, 0xB8, 0x52)), SparkWhite = Frozen(Colors.White);

        public Trophy(double scale = 1)
        {
            _scale = scale;
            Width = 14 * scale; Height = 16 * scale; IsHitTestVisible = false; ClipToBounds = false;
            var art = new Grid { Width = 14, Height = 16, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, RenderTransform = new ScaleTransform(scale, scale) };
            art.Children.Add(new Path { Data = HandleShape, Stroke = Outline, StrokeThickness = 2.8 });
            art.Children.Add(new Path { Data = CupShape, Fill = Outline, Stroke = Outline, StrokeThickness = 1.6, StrokeLineJoin = PenLineJoin.Round });
            art.Children.Add(new Path { Data = HandleShape, Stroke = HandleGold, StrokeThickness = 1.2 });
            art.Children.Add(new Path { Data = CupShape, Fill = CupFill });
            // a star struck on the bowl
            art.Children.Add(new Path { Data = StarShape, Fill = Mod.Br(Color.FromRgb(0x8A, 0x60, 0x1C), .8) });
            // a fixed highlight down the bowl's left: polished metal
            art.Children.Add(new Path { Data = SpecShape, Stroke = Mod.Br(Colors.White, .85), StrokeThickness = .9, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
            // the gleam: a slanted white band inside the cup's shape, moved across it
            _gleam = new Rectangle { Width = 5, Height = 18, Fill = GleamFill, RenderTransform = new TransformGroup { Children = { new SkewTransform(-28, 0), _gleamAt } }, Opacity = 0 };
            art.Children.Add(new Canvas { Clip = CupShape, IsHitTestVisible = false, Children = { _gleam } });
            var spark = new Path { Data = SparkShape, Fill = SparkWhite, RenderTransform = _sparkScale, Opacity = .95 };
            var sparkHost = new Canvas { IsHitTestVisible = false };
            Canvas.SetLeft(spark, 11.8); Canvas.SetTop(spark, 1.4);
            sparkHost.Children.Add(spark);
            art.Children.Add(sparkHost);
            Children.Add(art);
            RenderTransformOrigin = new Point(.5, 1);
            RenderTransform = new TransformGroup { Children = { _grow, _lift } };
        }

        /// <summary>The cup landing in place (a pop), its gleam crossing, the spark at its lip flaring: one course, <paramref name="ms"/> long.</summary>
        public void Rise(double ms = 1100)
        {
            Motion.Play(this, ms, t =>
            {
                double up = Seg(t, 0, .42);
                _lift.Y = 9 * _scale * (1 - Math.Min(1.08, Back(up, 2.2)));
                _grow.ScaleX = _grow.ScaleY = .55 + .45 * Math.Min(1.08, Back(up, 1.8));
                Opacity = Seg(t, 0, .18);
                Shine(Seg(t, .38, .78), Seg(t, .5, .95));
            }, Rest);
        }

        /// <summary>The gleam's and the spark's places in their own courses (0 to 1; outside: none).</summary>
        public void Shine(double gleam, double spark)
        {
            _gleamAt.X = -8 + 26 * gleam; _gleam.Opacity = gleam is > 0 and < 1 ? Math.Sin(Math.PI * gleam) : 0;
            _sparkScale.ScaleX = _sparkScale.ScaleY = spark is > 0 and < 1 ? 1.3 * Math.Sin(Math.PI * spark) : 0;
        }

        /// <summary>Hidden for a moment (the trophy is in flight to this row).</summary>
        public void Away() { Motion.Halt(this); Opacity = 0; }

        /// <summary>The cup as it stands.</summary>
        public void Rest()
        {
            _lift.Y = 0; _grow.ScaleX = _grow.ScaleY = 1; Opacity = 1; Shine(-1, -1);
        }
    }

    static readonly Geometry SpecShape = FreezeG(Geometry.Parse("M4,2.2 C3.9,4.6 4.3,6.4 5.4,7.8"));
    static readonly Geometry StarShape = FreezeG(Geometry.Parse("M7,3.1 L7.75,4.65 L9.45,4.85 L8.2,6 L8.5,7.7 L7,6.9 L5.5,7.7 L5.8,6 L4.55,4.85 L6.25,4.65 Z"));

    // ================================================================ the stage beams

    /// <summary>
    /// The stage lights behind the meter's head: a lamp's cone of warm light from each lamp head on the truss, crossing down
    /// over the head (the rows' tiles cover them below). Paths with one frozen gradient, placed once per width; at rest they
    /// stand at their angles, and in a moment (<see cref="Swing"/>) they sweep: rotations and opacity only.
    /// </summary>
    sealed class StageBeams : Canvas
    {
        readonly List<(Path P, RotateTransform R, double At)> _beams = new();
        readonly int _n;
        double _w = -1;

        static readonly Brush ConeFill = Mod.Grad(true, (Mod.A(SpotC, .5), 0), (Mod.A(SpotC, .26), .3), (Mod.A(SpotC, .1), .6), (Mod.A(SpotC, 0), 1));
        static readonly Geometry Cone = FreezeG(Geometry.Parse("M-2.2,0 L2.2,0 L36,150 L-36,150 Z"));

        public StageBeams(int n)
        {
            _n = n;
            ClipToBounds = true; IsHitTestVisible = false; Opacity = .85;
            for (int i = 0; i < n; i++)
            {
                var r = new RotateTransform(BeamRest(n, i));
                var p = new Path { Data = Cone, Fill = ConeFill, RenderTransform = r, IsHitTestVisible = false };
                Canvas.SetTop(p, 6);
                Children.Add(p);
                _beams.Add((p, r, BeamRest(n, i)));
            }
        }

        /// <summary>The lamps' places for the panel's width (set again only when it changes).</summary>
        public void Fit(double w)
        {
            if (Math.Abs(w - _w) < .5) return;
            _w = w;
            for (int i = 0; i < _n; i++) Canvas.SetLeft(_beams[i].P, LampX(w, _n, i));
        }

        /// <summary>The beams sweeping, <paramref name="amp"/> degrees each way, settling back to rest by the course's end, brighter mid-course.</summary>
        public void Swing(double t, double amp)
        {
            double k = Math.Clamp(t, 0, 1), fade = 1 - k;
            for (int i = 0; i < _beams.Count; i++)
            {
                var (_, r, rest) = _beams[i];
                r.Angle = rest + amp * Math.Sin(Math.PI * 2 * (k * 1.25 + i * .23)) * fade;
            }
            Opacity = .85 + .15 * Math.Sin(Math.PI * k);
        }

        /// <summary>A sweep as one course of <paramref name="ms"/>.</summary>
        public void Play(double ms, double amp) => Motion.Play(this, ms, t => Swing(t, amp), () => Swing(1, 0));
    }

    // ================================================================ the pyro

    static readonly Brush RayFill = Mod.Grad(false, (Mod.A(AuC, 0), 0), (Mod.A(AuLightC, .85), .6), (Colors.White, 1));
    static readonly Brush FlashFill = MakeFlash();
    static Brush MakeFlash()
    {
        var b = new RadialGradientBrush();
        b.GradientStops.Add(new GradientStop(Mod.A(Colors.White, .95), 0));
        b.GradientStops.Add(new GradientStop(Mod.A(AuLightC, .6), .3));
        b.GradientStops.Add(new GradientStop(Mod.A(AuC, 0), 1));
        b.Freeze();
        return b;
    }

    /// <summary>
    /// A kill's pyro: a gold burst from each top corner of the panel, a flash and a spray of sparks flying out and dropping,
    /// then a second smaller burst a beat later. One course on <paramref name="layer"/> (clipped to the panel): every piece a
    /// shape with a frozen brush, moved by its transforms and faded by its opacity.
    /// </summary>
    static void Pyro(Canvas layer, double w, double h, double ms = 1200)
    {
        layer.Children.Clear();
        if (w < 60 || h < 40) return;
        var parts = new List<Action<double>>();
        void Burst(double cx, double cy, int side, int rays, double reach, double at, double len)
        {
            var flash = new Ellipse { Width = 70, Height = 70, Fill = FlashFill, Opacity = 0, IsHitTestVisible = false, RenderTransformOrigin = new Point(.5, .5) };
            var fs = new ScaleTransform(.2, .2);
            flash.RenderTransform = new TransformGroup { Children = { fs, new TranslateTransform(cx - 35, cy - 35) } };
            layer.Children.Add(flash);
            parts.Add(p =>
            {
                double q = Math.Clamp((p - at) / .35, 0, 1);
                fs.ScaleX = fs.ScaleY = .2 + 1.3 * Motion.Out(q);
                flash.Opacity = q is > 0 and < 1 ? (q < .2 ? q / .2 : 1 - (q - .2) / .8) : 0;
            });
            for (int k = 0; k < rays; k++)
            {
                double u = (k + .5) / rays, ang = -24 + 84 * u;   // fanned from along the top edge to steeply down-inward, kept off the rows' figures
                if (side > 0) ang = 180 - ang;
                double my = .7 + .3 * ((k * .618) % 1);
                var ray = new Rectangle { Width = len, Height = 2.8, Fill = RayFill, RadiusX = 1.2, RadiusY = 1.2, Opacity = 0, IsHitTestVisible = false };
                var stretch = new ScaleTransform(1, 1);
                var out1 = new TranslateTransform();
                var drop = new TranslateTransform();
                ray.RenderTransform = new TransformGroup { Children = { stretch, out1, new RotateTransform(ang), new TranslateTransform(cx, cy - 1.2), drop } };
                layer.Children.Add(ray);
                parts.Add(p =>
                {
                    double q = Math.Clamp((p - at) / .6, 0, 1);
                    out1.X = 4 + reach * my * Motion.Out(q);
                    stretch.ScaleX = .4 + 1.2 * Math.Sin(Math.PI * Math.Min(1, q * 1.4));
                    drop.Y = 14 * q * q;
                    ray.Opacity = q is > 0 and < 1 ? (q < .08 ? q / .08 : 1 - q * q) : 0;
                });
            }
        }
        Burst(10, 6, -1, 14, Math.Min(120, w * .38), 0, 18);
        Burst(w - 10, 6, 1, 14, Math.Min(120, w * .38), 0, 18);
        Burst(10, 6, -1, 8, Math.Min(80, w * .26), .26, 13);
        Burst(w - 10, 6, 1, 8, Math.Min(80, w * .26), .26, 13);
        Motion.Play(layer, ms, p => { foreach (var a in parts) a(p); }, () => layer.Children.Clear());
    }

    // ================================================================ the confetti

    /// <summary>At most this many pieces of confetti on a kill.</summary>
    const int ConfettiN = 24;

    static readonly Brush ConfettiWhite = Mod.Br(Colors.White, .95), ConfettiGold = Frozen(Color.FromRgb(0xFF, 0xE1, 0x8E)), ConfettiDeep = Frozen(Color.FromRgb(0xE3, 0xB5, 0x4E));

    /// <summary>
    /// A kill's confetti: <see cref="ConfettiN"/> gold and white pieces (slips and squares) fired in from the panel's two top
    /// corners over the champion's banner, each to its own place across, then fluttering down to the stage's floor
    /// (<paramref name="floor"/>, the rows' top) and on behind it, turning and flipping as they go. The layer's clip keeps
    /// them to the banner band, out of its words: the rows' figures are never covered. One course of transforms and opacity
    /// (no layout), nothing past the window's edge.
    /// </summary>
    static void Confetti(Canvas layer, double width, double floor, double ms = 2600)
    {
        layer.Children.Clear();
        if (width < 40 || floor < 24) return;
        var pieces = new List<(FrameworkElement El, RotateTransform Turn, ScaleTransform Flip, TranslateTransform At, double X0, double Y0, double Xa, double Ya, double Yend, double Drift, double Spin, double Sway, double Phase, double Born)>();
        for (int i = 0; i < ConfettiN; i++)
        {
            double u = (i * 0.618034) % 1, v = (i * 0.414214 + .2) % 1, z = (i * 0.7548) % 1;
            bool white = i % 3 == 2, square = i % 5 == 4;
            var fill = white ? ConfettiWhite : i % 2 == 0 ? ConfettiDeep : ConfettiGold;
            var el = square ? new Rectangle { Width = 6.5, Height = 6.5, Fill = fill } : new Rectangle { Width = i % 2 == 0 ? 5 : 4.2, Height = i % 2 == 0 ? 11 : 9, Fill = fill };
            var turn = new RotateTransform(u * 180);
            var flip = new ScaleTransform(1, 1);
            var at = new TranslateTransform(-60, -60);
            el.RenderTransformOrigin = new Point(.5, .5);
            el.RenderTransform = new TransformGroup { Children = { flip, turn, at } };
            el.IsHitTestVisible = false;
            el.Opacity = 0;
            layer.Children.Add(el);
            int side = i % 2 == 0 ? -1 : 1;
            double x0 = side < 0 ? 8 : width - 12, y0 = 12;
            double xa = x0 - side * width * (.05 + .42 * u), ya = 3 + 9 * v;
            pieces.Add((el, turn, flip, at, x0, y0, xa, ya, floor + 8 + 10 * z, (z - .5) * 34, (v - .5) * 720, 3 + 6 * u, v * 6.28, (i / 2 % 6) * .03));
        }
        Motion.Play(layer, ms, p =>
        {
            foreach (var (el, turn, flip, at, x0, y0, xa, ya, yend, drift, spin, sway, phase, born) in pieces)
            {
                double q = Math.Clamp((p - born) / (1 - born), 0, 1);
                if (q <= 0) { el.Opacity = 0; continue; }
                if (q < .26) { double k = Motion.Out(q / .26); at.X = x0 + (xa - x0) * k; at.Y = y0 + (ya - y0) * k; }
                else
                {
                    // from the top of its flight, fluttering down and drifting, slowing as it goes
                    double f = (q - .26) / .74;
                    at.X = xa + drift * f + sway * Math.Sin(phase + q * 10);
                    at.Y = ya + (yend - ya) * Math.Pow(f, 1.3);
                }
                turn.Angle = phase * 30 + spin * q;
                flip.ScaleX = Math.Cos(phase + q * 14);
                el.Opacity = q < .04 ? q / .04 : q > .78 ? Math.Max(0, 1 - (q - .78) / .22) : 1;
            }
        }, () => layer.Children.Clear());
    }

    // ================================================================ the medal

    /// <summary>
    /// The Buddy's ring as a gold medal: two ribbon tails behind it, a black disc, a graphite track, your best's share as a
    /// brushed-gold arc with round ends (full once beaten), hairlines in gold round and inside it. <paramref name="glint"/>:
    /// a bright point at the arc's head (your best passed). 46 × 46, the disc's centre at (23, 21).
    /// </summary>
    static void Medal(DrawingContext dc, double share, bool beat, double complete, double glint, bool empty = false)
    {
        const double cx = 23, cy = 21, r = 16.5;
        var center = new Point(cx, cy);
        foreach (int side in new[] { -1, 1 })
        {
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(new Point(cx + side * 7.5, cy + 12), true, true);
                c.LineTo(new Point(cx + side * 1.5, cy + 15), true, false);
                c.LineTo(new Point(cx + side * 5, cy + 24.4), true, false);
                c.LineTo(new Point(cx + side * 7.6, cy + 21.8), true, false);
                c.LineTo(new Point(cx + side * 11, cy + 23.2), true, false);
            }
            g.Freeze();
            dc.DrawGeometry(Mod.Grad(true, (Color.FromRgb(0x30, 0x26, 0x12), 0), (Color.FromRgb(0x16, 0x12, 0x08), 1)), Mod.P(Mod.Br(AuC, empty ? .35 : .85), 1), g);
        }
        dc.DrawEllipse(Mod.Grad(true, (Color.FromRgb(0x22, 0x1D, 0x13), 0), (Black0, 1)), null, center, r + 2.6, r + 2.6);
        dc.DrawEllipse(null, Mod.P(Mod.Br(AuC, empty ? .3 : .6), 1), center, r + 2.6, r + 2.6);
        dc.DrawEllipse(null, Mod.P(Frozen(Color.FromRgb(0x30, 0x2C, 0x24)), 3.6), center, r, r);
        if (empty) return;
        double shown = Math.Clamp(beat ? 1 : share, 0, 1);
        shown += (1 - shown) * Math.Clamp(complete, 0, 1);
        if (shown >= .999) dc.DrawEllipse(null, Mod.P(GoldMetal, 3.6), center, r, r);
        else if (shown > .004)
        {
            double a = 2 * Math.PI * shown;
            var fig = new PathFigure { StartPoint = new Point(cx, cy - r) };
            fig.Segments.Add(new ArcSegment(new Point(cx + r * Math.Sin(a), cy - r * Math.Cos(a)), new Size(r, r), 0, a > Math.PI, SweepDirection.Clockwise, true));
            var pg = new PathGeometry(); pg.Figures.Add(fig); pg.Freeze();
            dc.DrawGeometry(null, Mod.P(GoldMetal, 3.6, round: true), pg);
        }
        dc.DrawEllipse(null, Mod.P(Mod.Br(AuC, .3), .8), center, r - 2.8, r - 2.8);
        if (glint is > 0 and < 1)
        {
            double ah = 2 * Math.PI * shown;
            var p = new Point(cx + r * Math.Sin(ah), cy - r * Math.Cos(ah));
            dc.DrawEllipse(Mod.Br(AuPaleC, .4), null, p, 5.5, 5.5);
            dc.DrawEllipse(White, null, p, 2.6, 2.6);
        }
    }

    // ================================================================ the pickup tiles

    /// <summary>
    /// The pickup strip's tiles in black and gold: each item's slot on black with a gold frame (a graded item keeps its grade's
    /// frame: that colour says something). Restyles the tiles of the strip the kit built, in place.
    /// </summary>
    static FrameworkElement GoldTiles(FrameworkElement strip)
    {
        foreach (var b in Boxes(strip))
        {
            bool graded = b.BorderBrush is SolidColorBrush sb && Graded(sb.Color);
            b.Background = TileGround;
            if (!graded) b.BorderBrush = Mod.Br(AuC, .8);
            b.CornerRadius = new CornerRadius(3);
        }
        return strip;
    }

    static readonly Brush TileGround = MakeTileGround();
    static Brush MakeTileGround()
    {
        var b = new RadialGradientBrush { GradientOrigin = new Point(.5, .25), Center = new Point(.5, .3), RadiusX = .8, RadiusY = .8 };
        b.GradientStops.Add(new GradientStop(Color.FromRgb(0x2C, 0x25, 0x18), 0));
        b.GradientStops.Add(new GradientStop(Color.FromRgb(0x0A, 0x0A, 0x0B), 1));
        b.Freeze();
        return b;
    }

    /// <summary>A frame in a grade's colour (green, blue, gold, orange ...), not the kit's plain grey.</summary>
    static bool Graded(Color c)
    {
        int max = Math.Max(c.R, Math.Max(c.G, c.B)), min = Math.Min(c.R, Math.Min(c.G, c.B));
        return c.A > 0xC0 && max - min > 60;
    }

    /// <summary>The item boxes in a strip: a Border whose face holds the item's picture.</summary>
    static IEnumerable<Border> Boxes(DependencyObject d)
    {
        foreach (var ch in LogicalTreeHelper.GetChildren(d).OfType<DependencyObject>())
        {
            if (ch is Border { Child: Grid face } b && face.Children.OfType<Image>().Any()) { yield return b; continue; }
            foreach (var x in Boxes(ch)) yield return x;
        }
    }
}
