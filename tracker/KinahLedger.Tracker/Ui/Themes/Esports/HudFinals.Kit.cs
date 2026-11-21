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
    static void Plate(DrawingContext dc, Size s, double radius, double opacity, bool shadow = true, bool spot = true, double rim = 1)
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
    /// The leader's trophy: a gold cup 14 × 16 with a dark outline, a gleam band that can run over it and a spark at its lip.
    /// <see cref="Rise"/> plays its entrance (one course: transforms and opacity only); at rest it simply stands.
    /// </summary>
    sealed class Trophy : Grid
    {
        readonly TranslateTransform _gleamAt = new(-12, 0);
        readonly Rectangle _gleam;
        readonly ScaleTransform _sparkScale = new(0, 0);
        readonly TranslateTransform _lift = new();
        readonly ScaleTransform _grow = new(1, 1);

        static readonly Brush CupFill = Mod.Grad(true, (Color.FromRgb(0xFF, 0xF4, 0xC8), 0), (Color.FromRgb(0xF4, 0xC6, 0x5C), .38), (Color.FromRgb(0xC9, 0x93, 0x34), .7), (Color.FromRgb(0x8A, 0x60, 0x1C), 1));
        static readonly Brush GleamFill = Mod.Grad(false, (Mod.A(Colors.White, 0), 0), (Mod.A(Colors.White, .95), .5), (Mod.A(Colors.White, 0), 1));

        public Trophy()
        {
            Width = 14; Height = 16; IsHitTestVisible = false; ClipToBounds = false;
            Children.Add(new Path { Data = HandleShape, Stroke = Mod.Br(Colors.Black, .85), StrokeThickness = 2.8 });
            Children.Add(new Path { Data = CupShape, Fill = Mod.Br(Colors.Black, .85), Stroke = Mod.Br(Colors.Black, .85), StrokeThickness = 1.6, StrokeLineJoin = PenLineJoin.Round });
            Children.Add(new Path { Data = HandleShape, Stroke = Frozen(Color.FromRgb(0xE8, 0xB8, 0x52)), StrokeThickness = 1.2 });
            Children.Add(new Path { Data = CupShape, Fill = CupFill });
            // the gleam: a slanted white band inside the cup's shape, moved across it
            _gleam = new Rectangle { Width = 5, Height = 18, Fill = GleamFill, RenderTransform = new TransformGroup { Children = { new SkewTransform(-28, 0), _gleamAt } }, Opacity = 0 };
            Children.Add(new Canvas { Clip = CupShape, IsHitTestVisible = false, Children = { _gleam } });
            var spark = new Path { Data = SparkShape, Fill = Frozen(Colors.White), RenderTransform = _sparkScale, Opacity = .95 };
            var sparkHost = new Canvas { IsHitTestVisible = false };
            Canvas.SetLeft(spark, 11.8); Canvas.SetTop(spark, 1.4);
            sparkHost.Children.Add(spark);
            Children.Add(sparkHost);
            RenderTransformOrigin = new Point(.5, 1);
            RenderTransform = new TransformGroup { Children = { _grow, _lift } };
        }

        /// <summary>The cup rising into place (a pop), its gleam crossing, the spark at its lip flaring: one course, <paramref name="ms"/> long.</summary>
        public void Rise(double ms = 1100)
        {
            Motion.Play(this, ms, t =>
            {
                double up = Seg(t, 0, .42);
                _lift.Y = 9 * (1 - Math.Min(1.08, Back(up, 2.2)));
                _grow.ScaleX = _grow.ScaleY = .55 + .45 * Math.Min(1.06, Back(up, 1.8));
                Opacity = Seg(t, 0, .18);
                double g = Seg(t, .38, .78);
                _gleamAt.X = -8 + 26 * g; _gleam.Opacity = g is > 0 and < 1 ? Math.Sin(Math.PI * g) : 0;
                double sp = Seg(t, .5, .95);
                _sparkScale.ScaleX = _sparkScale.ScaleY = sp is > 0 and < 1 ? Math.Sin(Math.PI * sp) : 0;
            }, Rest);
        }

        /// <summary>The cup as it stands.</summary>
        public void Rest()
        {
            _lift.Y = 0; _grow.ScaleX = _grow.ScaleY = 1; Opacity = 1; _gleam.Opacity = 0; _sparkScale.ScaleX = _sparkScale.ScaleY = 0;
        }
    }

    // ================================================================ the confetti

    /// <summary>At most this many pieces of confetti on a kill.</summary>
    const int ConfettiN = 14;

    static readonly Brush ConfettiWhite = Mod.Br(Colors.White, .95), ConfettiGold = Frozen(Color.FromRgb(0xFF, 0xE1, 0x8E)), ConfettiDeep = Frozen(Color.FromRgb(0xE3, 0xB5, 0x4E));

    /// <summary>
    /// A kill's confetti: <see cref="ConfettiN"/> gold and white pieces (slips and a few squares) fired from the panel's two
    /// top corners (under the head) as a stage's cannons do, arcing in over the rows and fluttering down through the panel, turning and
    /// flipping as they go, fading at the end. One course of transforms and opacity (no layout); the layer clips them to the
    /// panel, so nothing falls past the window's edge. Cheap: plain shapes with frozen brushes, no effects.
    /// </summary>
    static void Confetti(Canvas layer, double width, double height, double fromY, double ms = 2200)
    {
        layer.Children.Clear();
        if (width < 40 || height < 40) return;
        var pieces = new List<(FrameworkElement El, RotateTransform Turn, ScaleTransform Flip, TranslateTransform At, double X0, double Vx, double Lift, double Fall, double Spin, double Sway, double Phase)>();
        for (int i = 0; i < ConfettiN; i++)
        {
            double u = (i * 0.618034) % 1, v = (i * 0.414214 + .2) % 1;
            bool white = i % 3 == 2, square = i % 5 == 4;
            var fill = white ? ConfettiWhite : i % 2 == 0 ? ConfettiDeep : ConfettiGold;
            var el = square ? new Rectangle { Width = 5, Height = 5, Fill = fill } : new Rectangle { Width = i % 2 == 0 ? 3.8 : 3.2, Height = i % 2 == 0 ? 9 : 7.5, Fill = fill };
            var turn = new RotateTransform(u * 180);
            var flip = new ScaleTransform(1, 1);
            var at = new TranslateTransform(-50, -50);
            el.RenderTransformOrigin = new Point(.5, .5);
            el.RenderTransform = new TransformGroup { Children = { flip, turn, at } };
            el.IsHitTestVisible = false;
            el.Opacity = 0;
            layer.Children.Add(el);
            // half from each top corner, flying in toward the middle (each to its own depth across)
            int side = i % 2 == 0 ? -1 : 1;
            double x0 = side < 0 ? 10 : width - 14;
            pieces.Add((el, turn, flip, at, x0, -side * width * (.12 + .4 * u), 3 + 8 * v, Math.Max(30, height - fromY - 14) * (.62 + .38 * ((i * 0.7548) % 1)), (v - .5) * 600, 3 + 5 * u, v * 6.28));
        }
        Motion.Play(layer, ms, p =>
        {
            foreach (var (el, turn, flip, at, x0, vx, lift, fall, spin, sway, phase) in pieces)
            {
                // thrown up for the first fifth, then fluttering down, slowing to a drift
                at.Y = p < .2 ? fromY - lift * Motion.Out(p / .2) : fromY - lift + (fall + lift) * Math.Pow((p - .2) / .8, 1.25);
                at.X = x0 + vx * Motion.Out(p) + sway * Math.Sin(phase + p * 10);
                turn.Angle = phase * 30 + spin * p;
                flip.ScaleX = Math.Cos(phase + p * 14);
                el.Opacity = p < .05 ? p / .05 : p > .76 ? Math.Max(0, 1 - (p - .76) / .24) : 1;
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
