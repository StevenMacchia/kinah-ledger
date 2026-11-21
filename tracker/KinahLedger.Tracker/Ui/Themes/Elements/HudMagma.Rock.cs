using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

/// <summary>
/// Magma's stone and fire, drawn once and reused: the basalt's grain (a tile of noise made once), a slab's chipped outline
/// and the lava cracks along its edges (both made once per size), a molten flow with its cooled crust plates (the plates made
/// once per row width), the molten crown, and the small basalt chips. Every part is a cached frozen geometry or brush, so a
/// draw only fills them.
/// </summary>
sealed partial class HudMagma
{
    // ---------------------------------------------------------------- the palette
    static readonly Color BasaltHiC = Color.FromRgb(0x24, 0x1A, 0x15), BasaltC = Color.FromRgb(0x1A, 0x13, 0x10), BasaltLoC = Color.FromRgb(0x10, 0x0B, 0x09),
        TrenchC = Color.FromRgb(0x0C, 0x08, 0x07), CrustC = Color.FromRgb(0x2E, 0x20, 0x19), CrustLoC = Color.FromRgb(0x17, 0x0F, 0x0B), RimC = Color.FromRgb(0x05, 0x03, 0x02),
        BevelC = Color.FromRgb(0x4A, 0x37, 0x2C),
        LavaDeepC = Color.FromRgb(0x52, 0x0E, 0x05), LavaRedC = Color.FromRgb(0xB0, 0x24, 0x08), LavaC = Color.FromRgb(0xEE, 0x57, 0x12), LavaHotC = Color.FromRgb(0xFF, 0x9A, 0x2A),
        LavaYelC = Color.FromRgb(0xFF, 0xD2, 0x5E), WhiteHotC = Color.FromRgb(0xFF, 0xF6, 0xDC), AshC = Color.FromRgb(0x8C, 0x84, 0x7E), InkC = Color.FromRgb(0x2E, 0x10, 0x04);

    static readonly SolidColorBrush WhiteHot = Frozen(WhiteHotC), LavaHot = Frozen(LavaHotC), LavaYel = Frozen(LavaYelC);

    // ---------------------------------------------------------------- the basalt's grain

    /// <summary>The basalt's grain: a 128 px tile of soft noise (pits darker, flecks lighter), made once and tiled.</summary>
    static readonly ImageBrush Grain = MakeGrain();

    static ImageBrush MakeGrain()
    {
        const int N = 128;
        var rnd = new Random(1307);
        double[,] Lattice(int n) { var l = new double[n, n]; for (int i = 0; i < n; i++) for (int j = 0; j < n; j++) l[i, j] = rnd.NextDouble(); return l; }
        var scales = new[] { (Cells: 6, W: .5, L: Lattice(6)), (Cells: 16, W: .32, L: Lattice(16)), (Cells: 32, W: .18, L: Lattice(32)) };
        static double S(double t) => t * t * (3 - 2 * t);
        double Noise(int x, int y)
        {
            double v = 0;
            foreach (var (cells, w, l) in scales)
            {
                double fx = (double)x * cells / N, fy = (double)y * cells / N;
                int x0 = (int)fx % cells, y0 = (int)fy % cells, x1 = (x0 + 1) % cells, y1 = (y0 + 1) % cells;
                double tx = S(fx - Math.Floor(fx)), ty = S(fy - Math.Floor(fy));
                double a = l[x0, y0] + (l[x1, y0] - l[x0, y0]) * tx;
                double b = l[x0, y1] + (l[x1, y1] - l[x0, y1]) * tx;
                v += w * (a + (b - a) * ty);
            }
            return v;
        }
        var px = new byte[N * N * 4];
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                double d = Noise(x, y) - .5, roll = rnd.NextDouble();
                (byte R, byte G, byte B, double A) c = d > 0 ? ((byte)0x6E, (byte)0x58, (byte)0x4A, Math.Min(1, d * 2.6) * .42) : ((byte)0, (byte)0, (byte)0, Math.Min(1, -d * 2.8) * .55);
                if (roll < .006) c = (0x86, 0x70, 0x62, .26);        // a mineral fleck
                else if (roll < .022) c = (0, 0, 0, .62);           // a gas pit
                int i = (y * N + x) * 4;
                px[i] = (byte)(c.B * c.A); px[i + 1] = (byte)(c.G * c.A); px[i + 2] = (byte)(c.R * c.A); px[i + 3] = (byte)(255 * c.A);
            }
        var bmp = BitmapSource.Create(N, N, 96, 96, PixelFormats.Pbgra32, null, px, N * 4);
        bmp.Freeze();
        var b = new ImageBrush(bmp) { TileMode = TileMode.Tile, ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(0, 0, N, N), Stretch = Stretch.None };
        b.Freeze();
        return b;
    }

    // ---------------------------------------------------------------- the slab and its cracks

    static readonly Dictionary<(int, int, int), (Geometry Shape, Geometry Cracks)> Slabs = new();

    /// <summary>A slab's chipped outline and the lava cracks along its edges, made once for a size and seed.</summary>
    static (Geometry Shape, Geometry Cracks) SlabOf(Size s, int seed)
    {
        var key = ((int)Math.Round(s.Width), (int)Math.Round(s.Height), seed);
        if (Slabs.TryGetValue(key, out var hit)) return hit;
        if (Slabs.Count > 96) Slabs.Clear();
        double w = key.Item1 - 1.5, h = key.Item2 - 1.5;   // the outline .75 in, so its rim stays inside the slab's room
        var rnd = new Random(seed * 7919 + key.Item1 * 31 + key.Item2);
        double J(double a) => (rnd.NextDouble() - .5) * 2 * a;

        // the outline: chamfered corners, the edges chipped a pixel in
        const double Ch = 4;
        var pts = new List<Point>();
        void Edge(Point a, Point b, double step, Vector inward)
        {
            double len = (b - a).Length;
            int n = Math.Max(1, (int)(len / step));
            for (int i = 0; i < n; i++)
            {
                var p = a + (b - a) * ((double)i / n);
                pts.Add((i == 0 ? p : p + inward * (rnd.NextDouble() * 1.1)) + new Vector(.75, .75));
            }
        }
        Edge(new Point(Ch, 0), new Point(w - Ch, 0), 11, new Vector(0, 1));
        Edge(new Point(w - Ch, 0), new Point(w, Ch), 99, new Vector(0, 0));
        Edge(new Point(w, Ch), new Point(w, h - Ch), 9, new Vector(-1, 0));
        Edge(new Point(w, h - Ch), new Point(w - Ch, h), 99, new Vector(0, 0));
        Edge(new Point(w - Ch, h), new Point(Ch, h), 11, new Vector(0, -1));
        Edge(new Point(Ch, h), new Point(0, h - Ch), 99, new Vector(0, 0));
        Edge(new Point(0, h - Ch), new Point(0, Ch), 9, new Vector(1, 0));
        Edge(new Point(0, Ch), new Point(Ch, 0), 99, new Vector(0, 0));
        var shape = new StreamGeometry();
        using (var g = shape.Open())
        {
            g.BeginFigure(pts[0], true, true);
            g.PolyLineTo(pts.Skip(1).ToList(), true, true);
        }
        shape.Freeze();

        // the cracks: each comes in from an edge, runs along it a few pixels in, and goes out again, with small branches
        var cracks = new StreamGeometry();
        using (var g = cracks.Open())
        {
            void Crack(bool top, double x0, double len)
            {
                double edgeY = top ? 0 : h, dir = top ? 1 : -1, maxD = Math.Min(7, h * .18);
                var line = new List<Point>();
                double x = x0, d = 0;
                line.Add(new Point(x, edgeY + dir * .2));
                double end = Math.Min(w - 8, x0 + len);
                // in at a slant, wandering along a few pixels in, out at a slant
                while (x < end)
                {
                    x += 2.5 + rnd.NextDouble() * 4;
                    double toEnd = end - x;
                    double target = toEnd < 7 ? 0 : Math.Clamp(d + J(1.6) + (d < 2 ? 1.2 : 0), 1.2, maxD);
                    d = toEnd < 7 ? Math.Max(0, d - 2.2) : target;
                    line.Add(new Point(Math.Min(x, end + 4), edgeY + dir * Math.Max(.2, d)));
                    if (d > 1.5 && toEnd > 8 && rnd.NextDouble() < .3)
                    {
                        // a branch: a short forked split off the crack, towards the edge or deeper in
                        var at = line[^1];
                        bool toEdge = rnd.NextDouble() < .5;
                        var m = new Point(at.X + 1.5 + rnd.NextDouble() * 2.5, toEdge ? at.Y - dir * d * .55 : at.Y + dir * (1 + rnd.NextDouble() * 1.6));
                        var e2 = new Point(m.X + 1 + rnd.NextDouble() * 2.5, toEdge ? edgeY + dir * .2 : m.Y + dir * (.6 + rnd.NextDouble()));
                        g.BeginFigure(at, false, false); g.PolyLineTo(new[] { m, e2 }, true, true);
                    }
                }
                g.BeginFigure(line[0], false, false);
                g.PolyLineTo(line.Skip(1).ToList(), true, true);
            }
            int nTop = Math.Max(1, (int)(w / 150) + (w > 120 ? 1 : 0)), nBot = Math.Max(1, (int)(w / 170));
            for (int i = 0; i < nTop; i++)
            {
                double span = (w - 30) / nTop, x0 = 14 + span * i + rnd.NextDouble() * span * .35;
                Crack(true, x0, Math.Min(span * .7, 34 + rnd.NextDouble() * 46));
            }
            for (int i = 0; i < nBot; i++)
            {
                double span = (w - 30) / nBot, x0 = 16 + span * i + rnd.NextDouble() * span * .4;
                Crack(false, x0, Math.Min(span * .6, 26 + rnd.NextDouble() * 40));
            }
            // a split down one side, near a corner
            if (h > 26)
            {
                bool right = rnd.NextDouble() < .5;
                double x = right ? w : 0, d = right ? -1 : 1, y = Ch + 3 + rnd.NextDouble() * 4;
                var side = new List<Point> { new(x + d * .3, y) };
                for (int i = 0; i < 3; i++) { y += 3 + rnd.NextDouble() * 3; side.Add(new Point(x + d * (1.8 + rnd.NextDouble() * 2.2), y)); }
                side.Add(new Point(x + d * .3, y + 3));
                g.BeginFigure(side[0], false, false); g.PolyLineTo(side.Skip(1).ToList(), true, true);
            }
        }
        cracks.Freeze();
        var made = ((Geometry)shape, (Geometry)cracks);
        Slabs[key] = made;
        return made;
    }

    static readonly Pen RimPen = Mod.P(Frozen(RimC), 1.4);

    /// <summary>
    /// A basalt slab filling <paramref name="s"/>: charcoal stone (a lighter top, the grain over it), a dark rim and a warm
    /// bevel along its top, the lava's heat glowing up from its foot, and the glowing cracks along its edges.
    /// <paramref name="heat"/> 0..1 flares the cracks and the foot's glow (a moment); <paramref name="tint"/> turns the
    /// cracks' fire to a colour (a rare drop's grade); <paramref name="ash"/> cools them (a wipe, a pause).
    /// </summary>
    static void Slab(DrawingContext dc, Size s, double alpha, int seed, double heat = 0, Color? tint = null, bool ash = false, bool cracks = true)
    {
        if (s.Width < 8 || s.Height < 8) return;
        var (shape, crack) = SlabOf(s, seed);
        var r = new Rect(0, 0, s.Width, s.Height);
        dc.PushClip(shape);
        dc.DrawRectangle(Mod.Grad(true, (Mod.A(BasaltHiC, alpha), 0), (Mod.A(BasaltC, alpha), .45), (Mod.A(BasaltLoC, alpha), 1)), null, r);
        dc.PushOpacity(Math.Min(1, alpha + .05));
        dc.DrawRectangle(Grain, null, r);
        dc.Pop();
        // the heat from below: a faint orange rising off the slab's foot (grey when cooled)
        var foot = ash ? AshC : tint ?? LavaC;
        double fh = Math.Min(s.Height * .55, 26);
        dc.DrawRectangle(Mod.Grad(true, (Mod.A(foot, 0), 0), (Mod.A(foot, (ash ? .06 : .13) + .22 * heat), 1)), null, new Rect(0, s.Height - fh, s.Width, fh));
        // the bevel: a warm light along the top edge
        dc.DrawRectangle(Mod.Grad(true, (Mod.A(BevelC, .55), 0), (Mod.A(BevelC, 0), 1)), null, new Rect(0, 0, s.Width, 2.2));
        if (cracks) Cracks(dc, crack, heat, tint, ash);
        dc.Pop();
        dc.DrawGeometry(null, RimPen, shape);
    }

    /// <summary>
    /// A slab's flare (a moment's heat, drawn once over the slab; its opacity is the heat): the foot's glow risen, the cracks
    /// burning yellow-white with a wide glow (in <paramref name="tint"/> for a rare drop's grade).
    /// </summary>
    static void Flare(DrawingContext dc, Size s, int seed, Color? tint = null)
    {
        if (s.Width < 8 || s.Height < 8) return;
        var (shape, crack) = SlabOf(s, seed);
        var c = tint ?? LavaC;
        dc.PushClip(shape);
        double fh = Math.Min(s.Height * .6, 28);
        dc.DrawRectangle(Mod.Grad(true, (Mod.A(c, 0), 0), (Mod.A(c, .26), 1)), null, new Rect(0, s.Height - fh, s.Width, fh));
        dc.DrawGeometry(null, Mod.P(Mod.Br(c, .5), 8, true), crack);
        dc.DrawGeometry(null, Mod.P(Mod.Br(tint is { } t ? Mod.Mix(t, WhiteHotC, .4) : LavaYelC, 1), 1.8, true), crack);
        dc.DrawGeometry(null, Mod.P(Mod.Br(WhiteHotC, 1), .8, true), crack);
        dc.Pop();
    }

    /// <summary>The cracks' fire: a wide soft glow, the molten body, a white-hot core (cooled: a dull red seam).</summary>
    static void Cracks(DrawingContext dc, Geometry crack, double heat, Color? tint = null, bool ash = false)
    {
        if (ash)
        {
            dc.DrawGeometry(null, Mod.P(Mod.Br(Color.FromRgb(0x5A, 0x1C, 0x10), .8), 1.4, true), crack);
            return;
        }
        var body = tint is { } t ? Mod.Mix(t, LavaHotC, .25) : LavaHotC;
        var glow = tint ?? LavaC;
        dc.DrawGeometry(null, Mod.P(Mod.Br(glow, .3 + .3 * heat), 5 + 3 * heat, true), crack);
        dc.DrawGeometry(null, Mod.P(Mod.Br(body, 1), 1.6, true), crack);
        dc.DrawGeometry(null, Mod.P(Mod.Br(Mod.Mix(LavaYelC, WhiteHotC, .3 + .7 * heat), .85 + .15 * heat), .6, true), crack);
    }

    // ---------------------------------------------------------------- the molten flow

    static readonly Dictionary<(int, int, int), Geometry> Fissures = new();

    /// <summary>
    /// The cooled crust's fissures over a flow <paramref name="w"/> by <paramref name="h"/> (made once): jagged splits from its
    /// top to its foot every 9 to 17 px, joined here and there by a split across, so the crust reads as plates of rock.
    /// </summary>
    static Geometry FissuresOf(double w, double h, int seed)
    {
        var key = ((int)Math.Ceiling(w), (int)Math.Round(h * 2), seed);
        if (Fissures.TryGetValue(key, out var hit)) return hit;
        if (Fissures.Count > 160) Fissures.Clear();
        var rnd = new Random(seed * 104729 + key.Item1);
        double J(double a) => (rnd.NextDouble() - .5) * 2 * a;
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            double x = 6 + rnd.NextDouble() * 6;
            List<Point>? last = null;
            while (x < key.Item1)
            {
                // a split from the top to the foot, zigzagging
                var line = new List<Point> { new(x + J(2), -.5) };
                int n = h > 14 ? 3 : 2;
                for (int i = 1; i < n; i++) line.Add(new Point(x + J(3.2), h * i / n + J(h * .1)));
                line.Add(new Point(x + J(2), h + .5));
                c.BeginFigure(line[0], false, false); c.PolyLineTo(line.Skip(1).ToList(), true, true);
                // now and then a split across to the one before, or a short spur off it
                if (last != null && rnd.NextDouble() < .55)
                {
                    int k = 1 + rnd.Next(Math.Max(1, line.Count - 2));
                    var a = last[Math.Min(k, last.Count - 1)]; var b = line[k];
                    c.BeginFigure(a, false, false); c.PolyLineTo(new[] { new Point((a.X + b.X) / 2 + J(2), (a.Y + b.Y) / 2 + J(2.5)), b }, true, true);
                }
                else if (rnd.NextDouble() < .5)
                {
                    var a = line[1];
                    c.BeginFigure(a, false, false); c.LineTo(new Point(a.X - 3 - rnd.NextDouble() * 4, a.Y + J(3)), true, true);
                }
                last = line;
                x += 9 + rnd.NextDouble() * 8;
            }
        }
        g.Freeze();
        Fissures[key] = g;
        return g;
    }

    /// <summary>
    /// A molten flow in <paramref name="r"/>, <paramref name="fx"/> long: the cooled crust over its root (dark rock split by
    /// glowing fissures), thinning out into open lava, orange to a narrow yellow-white front; rounded like a channel (shaded at
    /// its top and foot); the front's glow. <paramref name="heat"/> 0..1: how hot it runs (the leader's at 1: a brighter
    /// front, a bigger glow, less crust). Drawn only when the bar changes; the heat pulse is a layer of its own moved over it.
    /// </summary>
    static void Flow(DrawingContext dc, Rect r, double fx, double heat, int seed, bool plates = true, double radius = 2.5)
    {
        if (fx < 1 || r.Height < 2) return;
        fx = Math.Min(fx, r.Width);
        var bar = new Rect(r.X, r.Y, fx, r.Height);
        var shape = Mod.Pill(bar, Math.Min(radius, fx / 2));
        double hot = Math.Clamp(heat, 0, 1);
        double lip = Math.Min(1, 9 / fx);
        var lava = new LinearGradientBrush { MappingMode = BrushMappingMode.Absolute, StartPoint = new Point(r.X, 0), EndPoint = new Point(r.X + fx, 0) };
        lava.GradientStops.Add(new GradientStop(LavaDeepC, 0));
        lava.GradientStops.Add(new GradientStop(Mod.Mix(LavaDeepC, LavaRedC, .5 + .3 * heat), .4));
        lava.GradientStops.Add(new GradientStop(Mod.Mix(LavaRedC, LavaC, .35 + .45 * heat), Math.Max(.4, 1 - lip * 3)));
        lava.GradientStops.Add(new GradientStop(Mod.Mix(LavaC, LavaHotC, .3 + .7 * hot), 1 - lip));
        lava.GradientStops.Add(new GradientStop(Mod.Mix(LavaHotC, LavaYelC, .3 + .7 * hot), 1));
        lava.Freeze();
        dc.PushClip(shape);
        dc.DrawRectangle(lava, null, bar);
        // the cooled crust: dark rock over the root, fading out into the open lava; its fissures glow
        if (plates && fx > 12)
        {
            double end = Math.Max(8, fx * (.6 - .22 * heat)), fade = Math.Max(10, fx * .22);
            double e0 = Math.Clamp((end - r.X + r.X) / fx, 0, 1), e1 = Math.Clamp((end + fade) / fx, 0, 1);
            var crust = new LinearGradientBrush { MappingMode = BrushMappingMode.Absolute, StartPoint = new Point(r.X, 0), EndPoint = new Point(r.X + fx, 0) };
            crust.GradientStops.Add(new GradientStop(Mod.A(CrustC, .97), 0));
            crust.GradientStops.Add(new GradientStop(Mod.A(Color.FromRgb(0x26, 0x18, 0x12), .93), e0 * .7));
            crust.GradientStops.Add(new GradientStop(Mod.A(Color.FromRgb(0x2A, 0x14, 0x0C), .8), e0));
            crust.GradientStops.Add(new GradientStop(Mod.A(Color.FromRgb(0x3A, 0x12, 0x06), 0), e1));
            crust.Freeze();
            dc.DrawRectangle(crust, null, bar);
            var glow = new LinearGradientBrush { MappingMode = BrushMappingMode.Absolute, StartPoint = new Point(r.X, 0), EndPoint = new Point(r.X + fx, 0) };
            glow.GradientStops.Add(new GradientStop(Mod.A(LavaRedC, .5), 0));
            glow.GradientStops.Add(new GradientStop(Mod.A(LavaC, .7), e0 * .65));
            glow.GradientStops.Add(new GradientStop(Mod.A(LavaHotC, .9), e0));
            glow.GradientStops.Add(new GradientStop(Mod.A(LavaHotC, 0), e1));
            glow.Freeze();
            var cracks = FissuresOf(r.Width, r.Height, seed);
            dc.PushTransform(new TranslateTransform(r.X, r.Y));
            dc.PushClip(new RectangleGeometry(new Rect(0, 0, Math.Min(fx, end + fade), r.Height)));
            var gl = glow.Clone(); gl.Transform = new TranslateTransform(-r.X, 0); gl.Freeze();
            dc.DrawGeometry(null, Mod.P(gl, 1.05, true), cracks);
            dc.Pop(); dc.Pop();
        }
        // the channel's roundness: shade at the top and the foot
        dc.DrawRectangle(Mod.Grad(true, (Mod.A(Colors.Black, .3), 0), (Mod.A(Colors.Black, 0), .3), (Mod.A(Colors.Black, 0), .62), (Mod.A(Colors.Black, .42), 1)), null, bar);
        // the front: a white-hot lip
        double fw = Math.Min(2.5, fx);
        dc.DrawRectangle(Mod.Grad(true, (Mod.A(WhiteHotC, .95), 0), (Mod.A(LavaYelC, 1), .5), (Mod.A(LavaHotC, .95), 1)), null, new Rect(r.X + fx - fw, r.Y, fw, r.Height));
        dc.Pop();
        // the front's glow, out past the lip onto the stone
        double gr = 5 + 6 * hot;
        var fg = new RadialGradientBrush(Mod.A(LavaHotC, .3 + .3 * heat), Mod.A(LavaC, 0));
        fg.Freeze();
        dc.DrawEllipse(fg, null, new Point(r.X + fx, r.Y + r.Height / 2), gr, r.Height * .75);
        // the crust's rim round the flow
        dc.DrawGeometry(null, Mod.P(Mod.Br(Color.FromRgb(0x1C, 0x0C, 0x06), .85), .9), shape);
    }

    // ---------------------------------------------------------------- the molten crown

    static readonly Geometry CrownShape = Frz(Geometry.Parse("M 1,12 L 0.6,3.6 L 4.6,7.2 L 8,0.6 L 11.4,7.2 L 15.4,3.6 L 15,12 Z"));
    static readonly Geometry CrownDrips = Frz(Geometry.Parse("M 3.6,12 Q 4.4,15.4 5.2,12 Z M 10.4,12 Q 11.3,14.6 12.1,12 Z"));
    static Geometry Frz(Geometry g) { g.Freeze(); return g; }

    /// <summary>
    /// The molten crown (16 by 13 at <paramref name="at"/>): lava from a yellow crest down to deep orange at its band, two
    /// drips off its foot, a dark crust edge. <paramref name="hot"/> 0..1 whitens it (just risen from the pool, still glowing).
    /// </summary>
    static void Crown(DrawingContext dc, Point at, double hot = 0)
    {
        dc.PushTransform(new TranslateTransform(at.X, at.Y));
        var fill = Mod.Grad(true, (Mod.Mix(LavaYelC, WhiteHotC, .4 + .6 * hot), 0), (Mod.Mix(LavaHotC, WhiteHotC, .6 * hot), .4), (Mod.Mix(LavaC, LavaYelC, .5 * hot), .78), (Mod.Mix(LavaRedC, LavaHotC, .5 * hot), 1));
        dc.DrawGeometry(fill, null, CrownDrips);
        dc.DrawGeometry(fill, Mod.P(Frozen(Color.FromRgb(0x24, 0x0A, 0x03)), .9), CrownShape);
        dc.DrawLine(Mod.P(Mod.Br(Color.FromRgb(0x3A, 0x10, 0x04), .7), .8), new Point(1.3, 9.6), new Point(14.7, 9.6));
        dc.DrawEllipse(WhiteHot, null, new Point(8, 1.8), .9, .9);
        dc.Pop();
    }

    /// <summary>The lava pool under the crown: a glowing ellipse, <paramref name="k"/> its flare (0 calm, 1 bright and wide).</summary>
    static void Pool(DrawingContext dc, Point c, double k)
    {
        var g = new RadialGradientBrush(Mod.A(Mod.Mix(LavaHotC, LavaYelC, k), .7 + .3 * k), Mod.A(LavaC, 0));
        g.Freeze();
        dc.DrawEllipse(g, null, c, 8 + 2 * k, 2.4 + 1.2 * k);
    }

    // ---------------------------------------------------------------- chips and words

    static readonly SolidColorBrush ChipGround = Frozen(Color.FromArgb(0xEE, 0x15, 0x0E, 0x0B));

    /// <summary>A small basalt chip with a glowing rim (★ DPS, the heal mark, AVG): <paramref name="filled"/> white-hot with dark words (YOU).</summary>
    static Border Chip(string s, Brush fg, Color edge, bool filled = false, double size = 8.5)
    {
        var t = Tt(s, size, filled ? Frozen(InkC) : fg, FontWeights.Bold);
        return new Border
        {
            Child = t, CornerRadius = new CornerRadius(3), Padding = new Thickness(4, 0, 4, 1), VerticalAlignment = VerticalAlignment.Center,
            Background = filled ? Mod.Grad(true, (WhiteHotC, 0), (LavaYelC, 1)) : ChipGround, BorderThickness = new Thickness(1),
            BorderBrush = Frozen(Mod.A(edge, filled ? 1 : .8)),
        };
    }

    /// <summary>The tight dark shade under the words on stone and lava: a hard ring round each glyph, a pixel down.</summary>
    static DropShadowEffect Shade() => new() { Color = Color.FromRgb(0x0A, 0x03, 0x00), Direction = 270, ShadowDepth = .8, BlurRadius = 3, Opacity = 1 };

    /// <summary>The warm underglow under a big white-hot figure (one effect, on one text).</summary>
    static DropShadowEffect Underglow(double opacity = .9) => new() { Color = Color.FromRgb(0xFF, 0x5A, 0x00), Direction = 270, ShadowDepth = 1.5, BlurRadius = 12, Opacity = opacity };

    /// <summary>Words given the tight shade.</summary>
    static T Shaded<T>(T el) where T : FrameworkElement { el.Effect = Shade(); return el; }

    /// <summary>A lava seam across (the foot's rule, the card's): a thin glowing crack, brightest in its middle, cooling to its ends.</summary>
    static FrameworkElement Seam(Thickness m, double a = 1)
    {
        return new TacSketch((dc, s) =>
        {
            if (s.Width < 10) return;
            var rnd = new Random((int)s.Width);
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                double y = s.Height / 2;
                c.BeginFigure(new Point(0, y), false, false);
                for (double x = 5; x < s.Width; x += 5 + rnd.NextDouble() * 6) c.LineTo(new Point(x, y + (rnd.NextDouble() - .5) * 1.6), true, true);
                c.LineTo(new Point(s.Width, y), true, true);
            }
            g.Freeze();
            dc.DrawGeometry(null, Mod.P(Mod.Grad(false, (Mod.A(LavaC, 0), 0), (Mod.A(LavaC, .3 * a), .5), (Mod.A(LavaC, 0), 1)), 4, true), g);
            dc.DrawGeometry(null, Mod.P(Mod.Grad(false, (Mod.A(LavaRedC, .2 * a), 0), (Mod.A(LavaHotC, .95 * a), .5), (Mod.A(LavaRedC, .2 * a), 1)), 1.1, true), g);
        }) { Height = 5, Margin = m };
    }

    /// <summary>The pickup strip's tiles each set in basalt: a stone rim round the tile and a lava crack glowing across its corner.</summary>
    static FrameworkElement? Set(FrameworkElement? strip)
    {
        if (strip is not Panel row) return strip;
        int n = 0;
        foreach (var cell in row.Children.OfType<Grid>())
        {
            if (cell.Tag is not ("pick-tile" or "pick-best")) continue;
            int seed = n++;
            cell.Children.Insert(0, new TacSketch((dc, s) =>
            {
                var r = new Rect(-2, -2, s.Width + 4, s.Height + 4);
                dc.DrawRoundedRectangle(Mod.Grad(true, (BasaltHiC, 0), (BasaltLoC, 1)), Mod.P(Frozen(RimC), 1), r, 5, 5);
                dc.PushOpacity(.9); dc.DrawRoundedRectangle(Grain, null, r, 5, 5); dc.Pop();
            }));
            cell.Children.Add(new TacSketch((dc, s) =>
            {
                // the crack across one corner of the rim
                bool tr = seed % 2 == 0;
                double w = s.Width, h = s.Height;
                var g = new StreamGeometry();
                using (var c = g.Open())
                {
                    if (tr) { c.BeginFigure(new Point(w - 8, -2), false, false); c.PolyLineTo(new[] { new Point(w - 5, -.6), new Point(w - 2.5, -1.2), new Point(w + .6, 1.6), new Point(w + 1.2, 6) }, true, true); }
                    else { c.BeginFigure(new Point(-1.2, h - 7), false, false); c.PolyLineTo(new[] { new Point(-.6, h - 3), new Point(1.6, h + .6), new Point(5, h + 1.2), new Point(8, h + 2) }, true, true); }
                }
                g.Freeze();
                dc.DrawGeometry(null, Mod.P(Mod.Br(LavaC, .35), 3, true), g);
                dc.DrawGeometry(null, Mod.P(Mod.Br(LavaHotC, 1), 1.1, true), g);
            }));
        }
        return strip;
    }
}
