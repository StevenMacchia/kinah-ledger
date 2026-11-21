using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

/// <summary>
/// Storm's drawn parts: the storm-cloud glass (a cut-cornered panel of dark slate-indigo with cloud mottling drawn once, a
/// charged rim), the lightning (jagged bolts from a seeded random, so a row's bolt is the same in every draw and every
/// render: a wide blue glow, a blue body and a white core, all pens, no blur), the bolt glyph of the lead, and the arc rim
/// round a pickup tile.
/// </summary>
sealed partial class HudStorm
{
    // the storm's palette: night slate and indigo, the charge's blue-white, a breath of violet
    static readonly Color Deep0C = Color.FromRgb(0x0A, 0x0D, 0x1A), Deep1C = Color.FromRgb(0x15, 0x1A, 0x33), VoltC = Color.FromRgb(0x8F, 0xD8, 0xFF),
        ArcC = Color.FromRgb(0xE8, 0xF6, 0xFF), VioletC = Color.FromRgb(0x9B, 0x7C, 0xFF), CloudC = Color.FromRgb(0x5A, 0x6C, 0xB8),
        HpAC = Color.FromRgb(0xFF, 0x3F, 0x6C), HpBC = Color.FromRgb(0xFF, 0x7E, 0xC8);
    static readonly SolidColorBrush Arc = Frozen(ArcC), Volt = Frozen(VoltC), VoltInk = Frozen(Mod.Mix(VoltC, Colors.White, .35));

    /* ---------------- the cut-cornered shape ---------------- */

    /// <summary>A panel's shape: its top-left and bottom-right corners cut by <paramref name="big"/>, the other two by <paramref name="small"/>.</summary>
    static Geometry Cut(Rect r, double big, double small)
    {
        big = Math.Max(0, Math.Min(big, Math.Min(r.Width, r.Height) / 2)); small = Math.Min(small, big);
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(r.Left + big, r.Top), true, true);
            c.LineTo(new Point(r.Right - small, r.Top), true, false);
            c.LineTo(new Point(r.Right, r.Top + small), true, false);
            c.LineTo(new Point(r.Right, r.Bottom - big), true, false);
            c.LineTo(new Point(r.Right - big, r.Bottom), true, false);
            c.LineTo(new Point(r.Left + small, r.Bottom), true, false);
            c.LineTo(new Point(r.Left, r.Bottom - small), true, false);
            c.LineTo(new Point(r.Left, r.Top + big), true, false);
        }
        g.Freeze();
        return g;
    }

    /* ---------------- the clouds ---------------- */

    /// <summary>The cloud mottling in the glass: soft light and dark patches of value noise, a seamless tile made once.</summary>
    static readonly ImageBrush Clouds = MakeClouds();

    static double Smooth(double t) => t * t * (3 - 2 * t);

    static double Noise(double[,] g, double u, double v)
    {
        int nx = g.GetLength(0), ny = g.GetLength(1);
        double fx = u * nx, fy = v * ny;
        int x0 = (int)Math.Floor(fx), y0 = (int)Math.Floor(fy);
        double tx = Smooth(fx - x0), ty = Smooth(fy - y0);
        int x1 = (x0 + 1) % nx, y1 = (y0 + 1) % ny; x0 %= nx; y0 %= ny;
        double a = g[x0, y0] + (g[x1, y0] - g[x0, y0]) * tx, b = g[x0, y1] + (g[x1, y1] - g[x0, y1]) * tx;
        return a + (b - a) * ty;
    }

    static ImageBrush MakeClouds()
    {
        const int w = 240, h = 120;
        var rnd = new Random(2611);
        double[,] Lattice(int nx, int ny) { var a = new double[nx, ny]; for (int x = 0; x < nx; x++) for (int y = 0; y < ny; y++) a[x, y] = rnd.NextDouble(); return a; }
        var o1 = Lattice(6, 3); var o2 = Lattice(12, 6); var o3 = Lattice(24, 12);
        var px = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                double u = (double)x / w, v = (double)y / h;
                double n = .55 * Noise(o1, u, v) + .3 * Noise(o2, u, v) + .15 * Noise(o3, u, v);
                int i = (y * w + x) * 4;
                if (n > .5)
                {
                    // a lit cloud: cool indigo light (premultiplied)
                    double a = Math.Min(1, (n - .5) * 3.2) * .30;
                    px[i] = (byte)(CloudC.B * a); px[i + 1] = (byte)(CloudC.G * a); px[i + 2] = (byte)(CloudC.R * a); px[i + 3] = (byte)(255 * a);
                }
                else
                {
                    // a deep pocket: darker
                    double a = Math.Min(1, (.5 - n) * 3) * .28;
                    px[i + 3] = (byte)(255 * a);
                }
            }
        var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Pbgra32, null, px, w * 4);
        bmp.Freeze();
        var b = new ImageBrush(bmp) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, w, h), ViewportUnits = BrushMappingMode.Absolute, Stretch = Stretch.Fill };
        b.Freeze();
        return b;
    }

    /// <summary>The glass's rim: a faint blue hairline, its joins round (a cut corner's mitre never pokes past the panel).</summary>
    static readonly Pen RimPen = MakeRimPen();
    static Pen MakeRimPen() { var p = new Pen(Mod.Br(VoltC, .2), 1) { LineJoin = PenLineJoin.Round }; p.Freeze(); return p; }

    /// <summary>
    /// The storm's crest: a few uneven cumulus heads billowing over a panel's top edge (x, y, rx, ry), seeded by its width so they
    /// hold still: the tallest (the storm's head, first in the list) two thirds across, lower heads either side, low shoulders of
    /// cloud between them.
    /// </summary>
    static List<(double X, double Y, double Rx, double Ry)> Puffs(double w, double big, double small, double crest)
    {
        var list = new List<(double, double, double, double)>();
        if (crest < 3 || w < 60) return list;
        var rnd = new Spark(97 + (int)w);
        double[] at = w < 220 ? new[] { .24, .64, .88 } : w < 420 ? new[] { .16, .38, .64, .87 } : new[] { .12, .29, .47, .65, .85 };
        int hero = Array.IndexOf(at, at.OrderBy(f => Math.Abs(f - .65)).First());
        double y = crest + 2.5;
        var heads = new List<(double, double, double, double)>();
        for (int i = 0; i < at.Length; i++)
        {
            double x = Math.Max(big + 10, Math.Min(w - small - 12, w * at[i] + (rnd.Next() - .5) * 10));
            bool h = i == hero;
            double ry = h ? crest + 1.5 : crest * (.36 + .16 * rnd.Next());
            double rx = Math.Min(h ? crest * 1.75 : crest * (1.05 + .4 * rnd.Next()), Math.Min(x - 1.5, w - 2.5 - x));
            heads.Add((x, y, rx, Math.Min(ry, y - .8)));
        }
        list.Add(heads[hero]);
        for (int i = 0; i < heads.Count; i++) if (i != hero) list.Add(heads[i]);
        // the shoulders: low cloud running between the heads, so the crest is one storm, not a row of beads
        for (int i = 0; i + 1 < heads.Count; i++)
        {
            double mx = (heads[i].Item1 + heads[i + 1].Item1) / 2, half = (heads[i + 1].Item1 - heads[i].Item1) / 2;
            list.Add((mx, y, Math.Min(half + 4, Math.Min(mx - 1.5, w - 2.5 - mx)), crest * (.22 + .06 * rnd.Next())));
        }
        return list;
    }

    static readonly Dictionary<string, Geometry> Shapes = new();

    /// <summary>A panel's silhouette: the cut-cornered body under a crest of storm cloud (<paramref name="crest"/> px tall over it), made once per size.</summary>
    static Geometry StormShape(Size s, double big, double small, double crest)
    {
        var key = $"{s.Width:0.#}|{s.Height:0.#}|{big}|{small}|{crest}";
        if (Shapes.TryGetValue(key, out var g0)) return g0;
        var r = new Rect(.5, crest + .5, Math.Max(1, s.Width - 1), Math.Max(1, s.Height - crest - 1));
        Geometry g = Cut(r, big, small);
        foreach (var (x, y, rx, ry) in Puffs(s.Width, big, small, crest))
            g = Geometry.Combine(g, new EllipseGeometry(new Point(x, y), rx, ry), GeometryCombineMode.Union, null);
        g.Freeze();
        if (Shapes.Count > 96) Shapes.Clear();
        Shapes[key] = g;
        return g;
    }

    /// <summary>
    /// The storm-cloud glass: a soft drop outside it, the slate-indigo ground (lighter at the top, the night at its foot), the
    /// clouds, a violet breath in its lower right; over its top (<paramref name="crest"/>) a crest of storm cloud, its heads
    /// lit from within and a bolt caught inside the tallest; the charged rim (bright along the cloud tops, a white charge on the
    /// cut corner, violet along the foot). Drawn once and kept (StormGlass).
    /// </summary>
    static void StormPanel(DrawingContext dc, Size s, double opacity, double big = 9, double small = 3, double lit = 0, bool drop = true, double crest = 0)
    {
        if (s.Width < 6 || s.Height < 6 + crest) return;
        var r = new Rect(.5, crest + .5, s.Width - 1, s.Height - crest - 1);
        var body = Cut(r, big, small);
        var shape = crest > 0 ? StormShape(s, big, small, crest) : body;
        if (drop)
            for (int k = 1; k <= 5; k++)
            {
                var o = new Rect(r.X - k, r.Y - k * .6 + 2, r.Width + 2 * k, r.Height + k * 1.6);
                var ring = new CombinedGeometry(GeometryCombineMode.Exclude, Cut(o, big + k * .4, small + k * .4), shape);
                ring.Freeze();
                dc.DrawGeometry(Mod.Br(Colors.Black, .075 - k * .012), null, ring);
            }
        double a = Math.Clamp(.7 + .26 * opacity, .78, .96);
        dc.DrawGeometry(Mod.Grad(true, (Mod.A(Deep1C, a), 0), (Mod.A(Deep0C, Math.Min(.96, a + .04)), 1)), null, shape);
        dc.PushClip(shape);
        dc.PushOpacity(Math.Min(1, .85 + .9 * lit));
        dc.DrawRectangle(Clouds, null, new Rect(0, 0, s.Width, s.Height));
        dc.Pop();
        // a violet breath low at the right, a cool light falling from the top
        var vb = new RadialGradientBrush(Mod.A(VioletC, .16), Mod.A(VioletC, 0)) { Center = new Point(.92, 1), GradientOrigin = new Point(.92, 1), RadiusX = .55, RadiusY = 1.1 };
        vb.Freeze();
        dc.DrawRectangle(vb, null, new Rect(0, 0, s.Width, s.Height));
        dc.DrawRectangle(Mod.Grad(true, (Mod.A(VoltC, .07), 0), (Mod.A(VoltC, 0), .5)), null, new Rect(0, 0, s.Width, s.Height));
        if (crest > 0)
        {
            var puffs = Puffs(s.Width, big, small, crest);
            // each head's volume: lit from the upper left by the storm's own light, a shade lighter than the glass so the crest
            // stands off the panel and the sky, its underside in shadow
            foreach (var (x, y, rx, ry) in puffs)
            {
                var v = new RadialGradientBrush { Center = new Point(.4, .32), GradientOrigin = new Point(.32, .18), RadiusX = .72, RadiusY = .8 };
                v.GradientStops.Add(new GradientStop(Mod.A(Color.FromRgb(0x4A, 0x5B, 0xA3), .8), 0));
                v.GradientStops.Add(new GradientStop(Mod.A(Color.FromRgb(0x2E, 0x38, 0x68), .5), .55));
                v.GradientStops.Add(new GradientStop(Mod.A(Deep0C, .3), 1));
                v.Freeze();
                dc.DrawEllipse(v, null, new Point(x, y), rx, ry);
            }
            if (puffs.Count > 0)
            {
                // the storm's head: its underbelly lit from inside by the charge, and a bolt caught in it, forking
                var (hx, hy, hrx, hry) = puffs[0];
                var belly = new RadialGradientBrush(Mod.A(Color.FromRgb(0xCF, 0xE3, 0xFF), .42), Mod.A(VoltC, 0)) { Center = new Point(.5, .5), GradientOrigin = new Point(.5, .6) };
                belly.Freeze();
                dc.DrawEllipse(belly, null, new Point(hx + hrx * .1, hy - hry * .15), hrx * .85, hry * .75);
                // the other heads: the same light, about a third as strong
                int heads = (puffs.Count + 1) / 2;
                var small2 = new RadialGradientBrush(Mod.A(Color.FromRgb(0xCF, 0xE3, 0xFF), .16), Mod.A(VoltC, 0)) { Center = new Point(.5, .5), GradientOrigin = new Point(.5, .65) };
                small2.Freeze();
                for (int i = 1; i < heads && i < puffs.Count; i++) { var (x2, y2, rx2, ry2) = puffs[i]; dc.DrawEllipse(small2, null, new Point(x2, y2 - ry2 * .2), rx2 * .8, ry2 * .7); }
                // one clean zigzag trunk down to the right, one fork off its second turn down to the left: no crossing strokes
                Point P(double fx, double fy) => new(Math.Clamp(hx + hrx * fx, 6, s.Width - 7), Math.Clamp(hy - hry + (crest + 3 - (hy - hry)) * fy, 5.5, crest + 4));
                var trunk = new List<Point> { P(-.42, .12), P(-.02, .36), P(-.2, .5), P(.3, .78), P(.12, .84), P(.5, 1) };
                DrawBolt(dc, trunk, 1, 1.6);
                DrawBolt(dc, new List<Point> { trunk[2], P(-.38, .72), P(-.3, .78), P(-.55, 1) }, .75, .9);
            }
            // where the cloud meets the glass: a shadow under the crest
            dc.DrawRectangle(Mod.Grad(true, (Mod.A(Deep0C, .4), 0), (Mod.A(Deep0C, 0), 1)), null, new Rect(0, crest + 2, s.Width, 6));
        }
        if (lit > 0) dc.DrawRectangle(Mod.Grad(true, (Mod.A(ArcC, .24 * lit), 0), (Mod.A(VoltC, .07 * lit), 1)), null, new Rect(0, 0, s.Width, s.Height));
        dc.Pop();
        // the rim: a faint blue all round, bright along the cloud tops (or the charged top edge), violet along the foot, a white charge on the top-left cut
        dc.DrawGeometry(null, RimPen, shape);
        if (crest > 0)
        {
            var lip = new LinearGradientBrush { MappingMode = BrushMappingMode.Absolute, StartPoint = new Point(0, 0), EndPoint = new Point(0, crest + 6) };
            lip.GradientStops.Add(new GradientStop(Mod.A(ArcC, .9), 0)); lip.GradientStops.Add(new GradientStop(Mod.A(Color.FromRgb(0xA8, 0xCC, 0xFF), .6), .5)); lip.GradientStops.Add(new GradientStop(Mod.A(Color.FromRgb(0xA8, 0xCC, 0xFF), .5), .8)); lip.GradientStops.Add(new GradientStop(Mod.A(VoltC, .12), 1));
            lip.Freeze();
            var pen = new Pen(lip, 1) { LineJoin = PenLineJoin.Round }; pen.Freeze();   // 1 px: its half-width ends on the bounds
            dc.DrawGeometry(null, pen, shape);
        }
        else dc.DrawRectangle(Mod.Grad(false, (Mod.A(VoltC, .75), 0), (Mod.A(VoltC, .45), .35), (Mod.A(VoltC, .12), 1)), null, new Rect(r.Left + big, r.Top, Math.Max(0, r.Width - big - small), 1));
        dc.DrawRectangle(Mod.Grad(false, (Mod.A(VioletC, 0), 0), (Mod.A(VioletC, .45), .7), (Mod.A(VioletC, .2), 1)), null, new Rect(r.Left + small, r.Bottom - 1, Math.Max(0, r.Width - big - small), 1));
        dc.DrawLine(Mod.P(Mod.Br(VoltC, .35), 3.5, round: true), new Point(r.Left + 1.8, r.Top + big), new Point(r.Left + big, r.Top + 1.8));   // its glow inside the panel's bounds
        dc.DrawLine(Mod.P(Mod.Br(ArcC, .95), 1.3, round: true), new Point(r.Left + 1.2, r.Top + big), new Point(r.Left + big, r.Top + 1.2));
        // the rim's live arcs (seeded, still): one off the charged corner along the top edge (no crest), one along the foot from the far corner
        if (r.Width > 80)
        {
            double len = Math.Min(90, r.Width * .3);
            if (crest <= 0)
            {
                // (kept under the panel's top: its glow never reaches past the bounds)
                var top = BoltPts(new Point(r.Left + big - 1, r.Top + 2), new Point(r.Left + big + len, r.Top + 2), 313, 1.6, 4).Select(q => new Point(q.X, Math.Max(q.Y, r.Top + 1.6))).ToList();
                DrawBolt(dc, top, .8, .5);
            }
            DrawBolt(dc, BoltPts(new Point(r.Right - big - 4, r.Bottom - 3), new Point(r.Right - big - 4 - len, r.Bottom - 3), 331, 1, 4), .85, .5, Mod.Mix(VioletC, VoltC, .35));
        }
    }

    /// <summary>
    /// A panel of storm glass that draws itself once: its drawing is kept (frozen) and drawn again as it is until its size or
    /// its strength changes, so a moment that moves things over it never redraws the glass.
    /// </summary>
    sealed class StormGlass : FrameworkElement
    {
        readonly Func<double> _strength;
        readonly double _big, _small, _crest;
        readonly bool _drop;
        DrawingGroup? _g;
        Size _s;
        double _o = -1;

        public StormGlass(Func<double> strength, double big, double small, bool drop = true, double crest = 0)
        {
            _strength = strength; _big = big; _small = small; _drop = drop; _crest = crest;
            IsHitTestVisible = false; SnapsToDevicePixels = true;
        }

        protected override void OnRender(DrawingContext dc)
        {
            var s = RenderSize;
            double o = _strength();
            if (_g == null || s != _s || Math.Abs(o - _o) > .001)
            {
                _s = s; _o = o;
                var g = new DrawingGroup();
                using (var gc = g.Open()) StormPanel(gc, s, o, _big, _small, 0, _drop, _crest);
                g.Freeze();
                _g = g;
            }
            dc.DrawDrawing(_g);
        }
    }

    /// <summary>The light of a thunderclap over a panel: a frozen white-blue wash, shown by its opacity, clipped to the panel's silhouette.</summary>
    static System.Windows.Shapes.Rectangle Flash(FrameworkElement over, double big, double small, double crest = 0)
    {
        var r = new System.Windows.Shapes.Rectangle
        {
            Fill = Mod.Grad(true, (Mod.A(ArcC, .62), 0), (Mod.A(VoltC, .24), .45), (Mod.A(VioletC, .12), 1)), Opacity = 0, Visibility = Visibility.Collapsed, IsHitTestVisible = false,
        };
        over.SizeChanged += (_, _) => r.Clip = crest > 0 ? StormShape(new Size(over.ActualWidth, over.ActualHeight), big, small, crest)
            : Cut(new Rect(.5, .5, Math.Max(1, over.ActualWidth - 1), Math.Max(1, over.ActualHeight - 1)), big, small);
        return r;
    }

    /// <summary>Shows a flash at <paramref name="a"/> (0 to 1).</summary>
    static void Lite(UIElement flash, double a)
    {
        flash.Opacity = Math.Clamp(a, 0, 1);
        flash.Visibility = a > .01 ? Visibility.Visible : Visibility.Collapsed;
    }

    /* ---------------- the lightning ---------------- */

    /// <summary>A small seeded random (xorshift): the same seed draws the same bolt every time.</summary>
    struct Spark
    {
        uint _s;
        public Spark(int seed) { _s = unchecked((uint)seed * 2654435761u ^ 0x9E3779B9u); if (_s == 0) _s = 1; Next(); Next(); }
        public double Next() { _s ^= _s << 13; _s ^= _s >> 17; _s ^= _s << 5; return (_s & 0xFFFFFF) / (double)0x1000000; }
        public double Signed() => Next() * 2 - 1;
    }

    /// <summary>A bolt's points from <paramref name="a"/> to <paramref name="b"/>: halved <paramref name="depth"/> times, each midpoint pushed aside by up to <paramref name="jag"/> (halving).</summary>
    static List<Point> BoltPts(Point a, Point b, int seed, double jag, int depth = 4)
    {
        var rnd = new Spark(seed);
        var pts = new List<Point> { a, b };
        double j = jag;
        for (int d = 0; d < depth; d++)
        {
            var next = new List<Point>(pts.Count * 2) { pts[0] };
            for (int i = 1; i < pts.Count; i++)
            {
                var p = pts[i - 1]; var q = pts[i];
                double dx = q.X - p.X, dy = q.Y - p.Y, len = Math.Max(.001, Math.Sqrt(dx * dx + dy * dy));
                double off = rnd.Signed() * j;
                next.Add(new Point((p.X + q.X) / 2 - dy / len * off, (p.Y + q.Y) / 2 + dx / len * off));
                next.Add(q);
            }
            pts = next;
            j *= .55;
        }
        return pts;
    }

    static StreamGeometry BoltLine(IReadOnlyList<Point> pts, int upTo = int.MaxValue)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(pts[0], false, false);
            for (int i = 1; i < Math.Min(pts.Count, upTo); i++) c.LineTo(pts[i], true, true);
        }
        g.Freeze();
        return g;
    }

    /// <summary>A bolt drawn: a wide soft glow in <paramref name="glow"/>, a blue body, a white core (<paramref name="w"/> scales them); <paramref name="reach"/> how much of it has struck.</summary>
    static void DrawBolt(DrawingContext dc, IReadOnlyList<Point> pts, double alpha, double w = 1, Color? glow = null, double reach = 1)
    {
        if (alpha <= .01 || pts.Count < 2) return;
        int upTo = Math.Max(2, (int)Math.Ceiling(pts.Count * Math.Clamp(reach, 0, 1)));
        var g = BoltLine(pts, upTo);
        var gc = glow ?? VoltC;
        dc.DrawGeometry(null, Mod.P(Mod.Br(gc, .22 * alpha), 6 * w, round: true), g);
        dc.DrawGeometry(null, Mod.P(Mod.Br(gc, .6 * alpha), 2.4 * w, round: true), g);
        dc.DrawGeometry(null, Mod.P(Mod.Br(ArcC, alpha), 1.1 * w, round: true), g);
    }

    /// <summary>The lead's bolt glyph in <paramref name="r"/>: white-blue, a blue glow round it.</summary>
    static void Glyph(DrawingContext dc, Rect r, double alpha = 1)
    {
        if (alpha <= .01 || r.Width < 1) return;
        Point P(double x, double y) => new(r.X + r.Width * x, r.Y + r.Height * y);
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(P(.62, 0), true, true);
            c.LineTo(P(.06, .58), true, false); c.LineTo(P(.46, .58), true, false); c.LineTo(P(.3, 1), true, false);
            c.LineTo(P(.94, .38), true, false); c.LineTo(P(.54, .38), true, false); c.LineTo(P(.78, 0), true, false);
        }
        g.Freeze();
        dc.DrawGeometry(null, Mod.P(Mod.Br(VoltC, .4 * alpha), 3.2, round: true), g);
        dc.DrawGeometry(Mod.Br(ArcC, alpha), Mod.P(Mod.Br(VoltC, .9 * alpha), .8), g);
    }

    /* ---------------- the pickup tiles' arc rim ---------------- */

    /// <summary>The pickup strip in Storm: each tile ringed in a blue arc rim, two of its corners charged white.</summary>
    static T ArcRim<T>(T strip) where T : FrameworkElement
    {
        if (strip is not Panel row) return strip;
        foreach (var cell in row.Children.OfType<Grid>())
        {
            if (cell.Children.Count == 0 || cell.Children[0] is not FrameworkElement tile) continue;
            double size = double.IsNaN(tile.Width) ? 24 : tile.Width;
            var rim = new TacSketch((dc, s) =>
            {
                var r = new Rect(-1.5, -1.5, size + 3, size + 3);
                dc.DrawRoundedRectangle(null, Mod.P(Mod.Br(VoltC, .5), 1), r, 6, 6);
                // the charge: two arcs of light on the corners, top-left and bottom-right
                var arcs = new StreamGeometry();
                using (var c = arcs.Open())
                {
                    c.BeginFigure(new Point(r.Left, r.Top + 10), false, false);
                    c.LineTo(new Point(r.Left, r.Top + 6), true, false);
                    c.ArcTo(new Point(r.Left + 6, r.Top), new Size(6, 6), 0, false, SweepDirection.Clockwise, true, false);
                    c.LineTo(new Point(r.Left + 10, r.Top), true, false);
                    c.BeginFigure(new Point(r.Right, r.Bottom - 10), false, false);
                    c.LineTo(new Point(r.Right, r.Bottom - 6), true, false);
                    c.ArcTo(new Point(r.Right - 6, r.Bottom), new Size(6, 6), 0, false, SweepDirection.Clockwise, true, false);
                    c.LineTo(new Point(r.Right - 10, r.Bottom), true, false);
                }
                arcs.Freeze();
                dc.DrawGeometry(null, Mod.P(Mod.Br(VoltC, .4), 3, round: true), arcs);
                dc.DrawGeometry(null, Mod.P(Mod.Br(ArcC, .95), 1.1, round: true), arcs);
            }, size, size) { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
            cell.Children.Insert(1, rim);
        }
        return strip;
    }
}
