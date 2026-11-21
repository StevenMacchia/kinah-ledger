using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

/// <summary>
/// Frost's ice: the palette, the crystal shapes (a shard is a rectangle sliced at two opposite corners, the cut a crystal's
/// facet), the glacial panel (deep navy-teal ice with facet planes, hairline cracks running in from its edges, a cold light
/// from its top-left, a lit rim, rime branching in from its corners), the ice bars (a translucent crystal in a cool tint of the
/// class's colour, crystal planes through it, rime along its top edge, a slanted icy-white leading edge), the snowflake
/// crown and the ice chips. Every shape is made once per size and kept (frozen); a moment only moves a clip or a light.
/// </summary>
sealed partial class HudFrost
{
    // ---- the palette: glacial navy-teal, ice white, cold cyan light; mint, gold and coral only where they mean something
    static readonly Color TopC = Color.FromRgb(0x0D, 0x2A, 0x3A), BotC = Color.FromRgb(0x07, 0x16, 0x21), DeepC = Color.FromRgb(0x04, 0x0E, 0x16),
        IceC = Color.FromRgb(0xA8, 0xE6, 0xFF), FrostC = Color.FromRgb(0xEA, 0xFA, 0xFF), CyanC = Color.FromRgb(0x5C, 0xD6, 0xFF), RimC = Color.FromRgb(0x9E, 0xE2, 0xFF),
        MintC2 = Color.FromRgb(0x72, 0xF5, 0xC8), MarkMintC = Color.FromRgb(0x8C, 0xFF, 0xD6), CoralC = Color.FromRgb(0xFF, 0x8C, 0x9C), HpC = Color.FromRgb(0xFF, 0x5E, 0x7E),
        GreyC = Color.FromRgb(0x7E, 0x90, 0x9C), WarnC2 = Color.FromRgb(0xFF, 0xB8, 0x8C), GoldC2 = Color.FromRgb(0xFF, 0xD3, 0x76), InkC = Color.FromRgb(0x05, 0x1C, 0x2A);
    static readonly SolidColorBrush White = Frozen(Color.FromRgb(0xF6, 0xFD, 0xFF)), Soft = Frozen(Mod.A(Color.FromRgb(0xDC, 0xF2, 0xFF), .9)), Quiet = Frozen(Mod.A(Color.FromRgb(0xB8, 0xDC, 0xEE), .74)),
        Mint2 = Frozen(Color.FromRgb(0x96, 0xFF, 0xD8)), MarkMint = Frozen(MarkMintC), Coral = Frozen(CoralC), IceInk = Frozen(Color.FromRgb(0xBF, 0xEE, 0xFF));

    // ================================================================ the shapes

    /// <summary>A shard: <paramref name="r"/> with its top-left and bottom-right corners cut by <paramref name="a"/>, the other two by <paramref name="b"/>.</summary>
    static Geometry Shard(Rect r, double a, double b)
    {
        double m = Math.Max(0, Math.Min(r.Width, r.Height) / 2);
        a = Math.Clamp(a, 0, m); b = Math.Clamp(b, 0, m);
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(r.Left + a, r.Top), true, true);
            c.LineTo(new Point(r.Right - b, r.Top), true, false);
            c.LineTo(new Point(r.Right, r.Top + b), true, false);
            c.LineTo(new Point(r.Right, r.Bottom - a), true, false);
            c.LineTo(new Point(r.Right - a, r.Bottom), true, false);
            c.LineTo(new Point(r.Left + b, r.Bottom), true, false);
            c.LineTo(new Point(r.Left, r.Bottom - b), true, false);
            c.LineTo(new Point(r.Left, r.Top + a), true, false);
        }
        g.Freeze();
        return g;
    }

    /// <summary>The cool tint of a class's colour the ice takes (its hue kept, a third of the way to glacier blue).</summary>
    static Color IceTint(Color c) => Mod.Mix(c, Color.FromRgb(0x9C, 0xDE, 0xFF), .4);

    // ================================================================ rime

    /// <summary>
    /// A frost feather from <paramref name="o"/>: a stem curving gently as it grows, short barbs off both sides at about sixty
    /// degrees, longest near its root and dwindling to its tip (window frost grows so), now and then a smaller feather off it.
    /// </summary>
    static void Feather(StreamGeometryContext c, Random rnd, Point o, double ang, double len, double barb, double bend, int depth)
    {
        var p = o;
        c.BeginFigure(p, false, false);
        const double step = 1.25;
        int n = (int)(len / step);
        var pts = new List<(Point P, double A)>();
        for (int i = 1; i <= n; i++)
        {
            ang += bend * step + (rnd.NextDouble() - .5) * .05;
            p = new Point(p.X + Math.Cos(ang) * step, p.Y + Math.Sin(ang) * step);
            c.LineTo(p, true, false);
            pts.Add((p, ang));
        }
        for (int i = 2; i < pts.Count; i++)
        {
            double at = (double)i / pts.Count, l = barb * Math.Pow(1 - at, .7) * (.6 + .5 * rnd.NextDouble()) + .4;
            var (q, a) = pts[i];
            foreach (var side in new[] { 1.0, -1.0 })
            {
                if (rnd.Next(7) == 0) continue;
                double b = a + side * (1.0 + (rnd.NextDouble() - .5) * .2);
                c.BeginFigure(q, false, false); c.LineTo(new Point(q.X + Math.Cos(b) * l, q.Y + Math.Sin(b) * l), true, false);
            }
            if (depth > 0 && i % (int)Math.Max(4, pts.Count / 3.0) == 0 && at < .7)
                Feather(c, rnd, q, a + (rnd.Next(2) == 0 ? .85 : -.85), len * (1 - at) * .55, barb * .7, -bend, depth - 1);
        }
    }

    /// <summary>
    /// Rime at a corner: frost feathers growing in from a cut corner (along the two edges and one into the face), a few loose
    /// crystals settled near it; made once. The corner's cut is <paramref name="cut"/>, the longest feather <paramref name="len"/>;
    /// the corner is at (0,0), the face towards +x, +y.
    /// </summary>
    static Geometry MakeRime(double cut, double len, int seed)
    {
        var rnd = new Random(seed);
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            double barb = Math.Max(1.6, len * .13);
            // along the top edge and down the side edge only: never in across the face, where a title starts
            Feather(c, rnd, new Point(cut * .8, cut * .2 + 1), .06, len, barb * .8, -.004, 1);
            Feather(c, rnd, new Point(cut * .2 + 1, cut * .8), 1.5, len * .5, barb * .8, .004, 0);
            // a few loose crystals settled near the corner: tiny six-armed stars
            for (int i = 0; i < 2; i++)
            {
                var p = new Point(cut + 6 + rnd.NextDouble() * len * .6, 3 + rnd.NextDouble() * 2.5);
                double s = .9 + rnd.NextDouble() * .6;
                for (int k = 0; k < 3; k++) { double a = k * Math.PI / 3 + .2; c.BeginFigure(new Point(p.X - Math.Cos(a) * s, p.Y - Math.Sin(a) * s), false, false); c.LineTo(new Point(p.X + Math.Cos(a) * s, p.Y + Math.Sin(a) * s), true, false); }
            }
        }
        g.Freeze();
        return g;
    }

    /// <summary>The rime of a big cut corner (a panel's top-left), of a small one (its other corners), and a tile's.</summary>
    static readonly Geometry RimeBig = MakeRime(9, 30, 7), RimeSmall = MakeRime(3.5, 18, 19), RimeTiny = MakeRime(3, 8, 23);
    static readonly Pen RimePen = Mod.P(Mod.Br(FrostC, .8), .65, round: true), RimeGlow = Mod.P(Mod.Br(CyanC, .17), 2.4, round: true);

    /// <summary>Rime at a corner of <paramref name="r"/>: <paramref name="sx"/>, <paramref name="sy"/> ±1 pick the corner.</summary>
    static void Rime(DrawingContext dc, Geometry rime, Rect r, double sx, double sy, double opacity = 1)
    {
        if (opacity <= .01) return;
        double x = sx > 0 ? r.Left : r.Right, y = sy > 0 ? r.Top : r.Bottom;
        dc.PushTransform(new MatrixTransform(sx, 0, 0, sy, x, y));
        if (opacity < 1) dc.PushOpacity(opacity);
        dc.DrawGeometry(null, RimeGlow, rime);
        dc.DrawGeometry(null, RimePen, rime);
        if (opacity < 1) dc.Pop();
        dc.Pop();
    }

    /// <summary>
    /// Hoarfrost along a bar's top edge, 1000 px of it made once (each draw clips it to the bar): small crystals hanging from
    /// the edge, a spike down with two twigs, irregular in spacing and height so it never reads as a pattern.
    /// </summary>
    static readonly Geometry RimeEdge = MakeRimeEdge();
    static Geometry MakeRimeEdge()
    {
        var rnd = new Random(41);
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            double x = 3;
            while (x < 1000)
            {
                // a cluster of hanging crystals, now and then one grown into a small feather
                int n = 2 + rnd.Next(4);
                for (int i = 0; i < n; i++)
                {
                    bool big = rnd.Next(5) == 0;
                    double h = big ? 4.2 + rnd.NextDouble() * 1.6 : 1.2 + rnd.NextDouble() * 2.2, lean = (rnd.NextDouble() - .5) * (big ? 1.6 : 1);
                    c.BeginFigure(new Point(x, .5), false, false); c.LineTo(new Point(x + lean, .5 + h), true, false);
                    if (big)
                        for (int k = 1; k <= 2; k++)
                        {
                            var m = new Point(x + lean * k / 3, .5 + h * k / 3);
                            double l = 1.6 - .4 * k;
                            c.BeginFigure(m, false, false); c.LineTo(new Point(m.X - l, m.Y + l * .8), true, false);
                            c.BeginFigure(m, false, false); c.LineTo(new Point(m.X + l, m.Y + l * .8), true, false);
                        }
                    x += 1.3 + rnd.NextDouble() * 1.3;
                }
                x += 5 + rnd.NextDouble() * 12;
            }
        }
        g.Freeze();
        return g;
    }

    /// <summary>A few larger frost crystals along a bar's top edge (every 60 to 110 px, a feathered spike and a smaller one), 1000 px made once.</summary>
    static readonly Geometry RimeTufts = MakeTufts();
    static Geometry MakeTufts()
    {
        var rnd = new Random(77);
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            double x = 22;
            while (x < 1000)
            {
                foreach (var (dx, h) in new[] { (0.0, 6.5 + rnd.NextDouble() * 1.5), (3.2, 3.5 + rnd.NextDouble()) })
                {
                    var top = new Point(x + dx, .6);
                    c.BeginFigure(top, false, false); c.LineTo(new Point(top.X + .3, top.Y + h), true, false);
                    for (int k = 1; k <= (h > 5 ? 3 : 1); k++)
                    {
                        var m = new Point(top.X + .1 * k, top.Y + h * k / 4.2);
                        double l = (h > 5 ? 2.6 : 1.6) - .5 * k;
                        c.BeginFigure(m, false, false); c.LineTo(new Point(m.X - l, m.Y + l * .9), true, false);
                        c.BeginFigure(m, false, false); c.LineTo(new Point(m.X + l, m.Y + l * .9), true, false);
                    }
                }
                x += 60 + rnd.NextDouble() * 50;
            }
        }
        g.Freeze();
        return g;
    }
    static readonly Pen TuftPen = Mod.P(Mod.Br(Colors.White, .95), .8, round: true);
    static readonly Brush RimeBand = Mod.Grad(true, (Mod.A(Colors.White, .62), 0), (Mod.A(Colors.White, 0), 1));

    /// <summary>The crystal planes in an ice bar: faint slanted lines at an irregular spacing, 1000 px made once.</summary>
    static readonly Geometry Planes = MakePlanes();
    static Geometry MakePlanes()
    {
        var rnd = new Random(5);
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            double x = 14;
            while (x < 1040)
            {
                c.BeginFigure(new Point(x, -2), false, false); c.LineTo(new Point(x - 11, 44), true, false);
                if (rnd.Next(3) == 0) { c.BeginFigure(new Point(x + 3, -2), false, false); c.LineTo(new Point(x - 8, 44), true, false); }
                x += 22 + rnd.NextDouble() * 30;
            }
        }
        g.Freeze();
        return g;
    }
    static readonly Pen PlanePen = Mod.P(Mod.Br(Colors.White, .11), .8), RimeEdgePen = Mod.P(Mod.Br(FrostC, .82), .7, round: true);

    // ================================================================ the panel

    /// <summary>A panel's drawn parts, made once per size (its shape, its drop, its facets and cracks).</summary>
    sealed class IceArt
    {
        Size _size;
        public Geometry Shape = Geometry.Empty, Inner = Geometry.Empty, Facets = Geometry.Empty, Cracks = Geometry.Empty, Crest = Geometry.Empty, CrestShade = Geometry.Empty, Ridges = Geometry.Empty, Cores = Geometry.Empty, Ticks = Geometry.Empty, Collars = Geometry.Empty;
        public Geometry[] Shadow = Array.Empty<Geometry>();
        public Rect R;
        public readonly double A, B;
        /// <summary>How tall the crystal shards jutting from its top corners grow (1: up to 12 px; 0: none), and how long its icicles hang (1: up to 9 px).</summary>
        public readonly double CrestK, IcicleK;
        /// <summary>Where along the bottom edge the icicles keep off (0 to 1 from the left): another panel's crest sits under it there.</summary>
        public double IcicleFrom = 0;
        readonly int _seed;
        public IceArt(double a, double b, int seed, double crest = 0, double icicles = 0) { A = a; B = b; _seed = seed; CrestK = crest; IcicleK = icicles; }

        public void Fit(Size s)
        {
            if (s == _size) return;
            _size = s;
            R = new Rect(.5, .5, Math.Max(1, s.Width - 1), Math.Max(1, s.Height - 1));
            Shape = Shard(R, A, B);
            var ir = R; ir.Inflate(-1.5, -1.5);
            Inner = Shard(ir, Math.Max(0, A - .6), Math.Max(0, B - .6));
            // the drop under it: rings outside the shape, deeper below (kept inside the window's room)
            var rings = new List<Geometry>();
            for (int k = 1; k <= 4; k++)
            {
                var o = new Rect(R.X - k * .9, R.Y - k * .4 + 2, R.Width + k * 1.8, R.Height + k * 1.3 + 1);
                var ring = new CombinedGeometry(GeometryCombineMode.Exclude, Shard(o, A + k * .4, B + k * .4), Shape);
                ring.Freeze(); rings.Add(ring);
            }
            Shadow = rings.ToArray();
            double w = R.Width, h = R.Height;
            // the crystal's facet planes: two big slanted faces catching the light
            var f = new StreamGeometry();
            using (var c = f.Open())
            {
                c.BeginFigure(new Point(R.Left, R.Top), true, true); c.LineTo(new Point(R.Left + w * .36, R.Top), true, false); c.LineTo(new Point(R.Left + w * .14, R.Bottom), true, false); c.LineTo(new Point(R.Left, R.Bottom), true, false);
                c.BeginFigure(new Point(R.Left + w * .58, R.Top), true, true); c.LineTo(new Point(R.Left + w * .7, R.Top), true, false); c.LineTo(new Point(R.Left + w * .55, R.Bottom), true, false); c.LineTo(new Point(R.Left + w * .47, R.Bottom), true, false);
            }
            f.Freeze(); Facets = f;
            // hairline cracks running in from the edges, a fork in some
            var rnd = new Random(_seed);
            var k2 = new StreamGeometry();
            using (var c = k2.Open())
            {
                void Crack(Point p, double ang, double len, int forks)
                {
                    c.BeginFigure(p, false, false);
                    double done = 0;
                    while (done < len)
                    {
                        double step = 2.5 + rnd.NextDouble() * 4;
                        ang += (rnd.NextDouble() - .5) * .9;
                        p = new Point(p.X + Math.Cos(ang) * step, p.Y + Math.Sin(ang) * step);
                        c.LineTo(p, true, false);
                        done += step;
                        if (forks > 0 && done > len * .45 && rnd.Next(2) == 0) { forks--; Crack(p, ang + (rnd.Next(2) == 0 ? .7 : -.7), (len - done) * .7, 0); c.BeginFigure(p, false, false); }
                    }
                }
                Crack(new Point(R.Left + w * (.2 + .1 * rnd.NextDouble()), R.Top), Math.PI / 2 + .3, Math.Min(h * .35, 16), 1);
                Crack(new Point(R.Left + w * (.62 + .12 * rnd.NextDouble()), R.Top), Math.PI / 2 - .4, Math.Min(h * .3, 13), 1);
                Crack(new Point(R.Right, R.Top + h * (.3 + .2 * rnd.NextDouble())), Math.PI - .2, Math.Min(w * .1, 18), 1);
                Crack(new Point(R.Left + w * (.4 + .2 * rnd.NextDouble()), R.Bottom), -Math.PI / 2 + .35, Math.Min(h * .3, 12), 1);
                Crack(new Point(R.Left, R.Top + h * (.55 + .2 * rnd.NextDouble())), .25, Math.Min(w * .08, 14), 0);
            }
            k2.Freeze(); Cracks = k2;
            MakeCrest(rnd);
        }

        /// <summary>
        /// The block of ice's silhouette: crystal prisms jutting up and out from its top corners (a big cluster at the big cut,
        /// a smaller one at the other), each with a lit face and a shaded face either side of its ridge; icicles hanging in
        /// clusters from its bottom edge, faceted the same way. Made once per size, drawn behind the slab so they grow out
        /// from under its edge.
        /// </summary>
        void MakeCrest(Random rnd)
        {
            var lit = new StreamGeometry();
            var shade = new StreamGeometry();
            var ridges = new StreamGeometry();
            var cores = new StreamGeometry();
            var ticks = new StreamGeometry();
            Geometry collars = Geometry.Empty;
            void Lump(Point p, double rx, double ry)
            {
                var e = new EllipseGeometry(p, rx, ry);
                collars = ReferenceEquals(collars, Geometry.Empty) ? e : new CombinedGeometry(GeometryCombineMode.Union, collars, e);
            }
            using (var c = lit.Open())
            using (var d = shade.Open())
            using (var g = ridges.Open())
            using (var e = cores.Open())
            using (var h = ticks.Open())
            {
                void Prism(Point root, double ang, double len, double w)
                {
                    double ca = Math.Cos(ang), sa = Math.Sin(ang), nx = -sa, ny = ca;
                    Point At(double along, double side) => new(root.X + ca * len * along + nx * w * side, root.Y + sa * len * along + ny * w * side);
                    var tip = At(1, 0);
                    c.BeginFigure(At(0, 1), true, true); c.LineTo(At(.7, .85), true, false); c.LineTo(tip, true, false); c.LineTo(At(.7, .12), true, false); c.LineTo(At(0, .12), true, false);
                    d.BeginFigure(At(0, .12), true, true); d.LineTo(At(.7, .12), true, false); d.LineTo(tip, true, false); d.LineTo(At(.7, -.85), true, false); d.LineTo(At(0, -1), true, false);
                    g.BeginFigure(At(.05, .12), false, false); g.LineTo(At(.7, .12), true, false); g.LineTo(tip, true, false);
                    // the refraction: a cyan core down the shaded face; the hot white specular near the tip of the lit face
                    e.BeginFigure(At(.06, -.08), true, true); e.LineTo(At(.66, -.08), true, false); e.LineTo(At(.9, -.02), true, false); e.LineTo(At(.66, -.5), true, false); e.LineTo(At(.06, -.55), true, false);
                    h.BeginFigure(At(.56, .55), false, false); h.LineTo(At(.84, .22), true, false);
                    // and a frost collar swallowing the rim where it grows out (a flat lump over the edge, barely into the face)
                    Lump(new Point(root.X, Math.Max(R.Top + .5, root.Y - 2.5)), w * 1.1 + 1.5, Math.Min(3, w * .4 + 1.2));
                }
                if (CrestK > 0)
                {
                    double k = CrestK, up = -Math.PI / 2;
                    // the big cluster at the top-left cut, fanning up and out over the corner (never over the face's words: each
                    // grows from under the edge, outwards), the tallest in its middle
                    var tl = new (double X, double Ang, double Len, double W)[] { (A * .1, up - 1.05, 26, 5.2), (A * .55, up - .62, 44, 7.5), (A + 2, up - .28, 46, 8.5), (A + 10, up + .02, 34, 6.5), (A + 19, up + .22, 21, 4.8), (A + 28, up + .36, 12, 3.4) };
                    foreach (var (x, ang, len, w) in tl) Prism(new Point(R.Left + x, R.Top + (x < A ? A - x : 0) + 3), ang, len * k, w * Math.Sqrt(k));
                    // its asymmetric counterpart at the top-right, about two thirds its weight
                    var tr = new (double X, double Ang, double Len, double W)[] { (B * .3, up + .95, 20, 4.6), (B + 1, up + .5, 34, 6.5), (B + 9, up + .18, 26, 5.4), (B + 18, up - .08, 14, 3.6) };
                    foreach (var (x, ang, len, w) in tr) Prism(new Point(R.Right - x, R.Top + (x < B ? B - x : 0) + 3), ang, len * k, w * Math.Sqrt(k));
                }
                if (IcicleK > 0)
                {
                    // a few real icicles in clusters off-centre along the bottom edge, uneven, the longest in a cluster's middle
                    double x = R.Left + Math.Max(A, B) + 10 + R.Width * IcicleFrom, end = R.Right - A - 10;
                    double gap = Math.Max(46, (end - x) / 3.2);
                    var top = R.Bottom - 2;
                    int clusters = 0;
                    while (x < end && clusters++ < 3)
                    {
                        // a cluster: one hero icicle with a short one either side or after it
                        int n = 2 + rnd.Next(2), hero = n == 3 ? 1 : 0;
                        for (int i = 0; i < n && x < end; i++)
                        {
                            bool big = i == hero;
                            double len = (big ? 19 + rnd.NextDouble() * 3 : 7 + rnd.NextDouble() * 4) * IcicleK, w = big ? 4.4 + rnd.NextDouble() * .6 : 2.4 + rnd.NextDouble() * .6;
                            if (big) Lump(new Point(x, R.Bottom - 1), w * 1.3, 1.9);
                            var tip = new Point(x + (rnd.NextDouble() - .5) * 1.2, top + len);
                            c.BeginFigure(new Point(x - w, top), true, true); c.LineTo(new Point(x + w * .15, top), true, false); c.LineTo(tip, true, false);
                            d.BeginFigure(new Point(x + w * .15, top), true, true); d.LineTo(new Point(x + w, top), true, false); d.LineTo(tip, true, false);
                            g.BeginFigure(new Point(x + w * .15, top + 1), false, false); g.LineTo(new Point(tip.X, tip.Y - 2), true, false);
                            x += w * 2 + 1.5 + rnd.NextDouble() * 2.5;
                        }
                        x += gap * (.8 + .4 * rnd.NextDouble());
                    }
                }
            }
            lit.Freeze(); shade.Freeze(); ridges.Freeze(); cores.Freeze(); ticks.Freeze(); collars.Freeze();
            Crest = lit; CrestShade = shade; Ridges = ridges; Cores = cores; Ticks = ticks; Collars = collars;
        }
    }

    // the crystal's two faces: lit (white to pale ice) and shaded (glacier blue to deep teal), a dark outline and a white ridge
    static readonly Brush CrestLit = Mod.Grad(false, (Mod.A(Colors.White, .97), 0), (Mod.A(Color.FromRgb(0xC4, 0xEE, 0xFF), .95), 1)),
        CrestShadeFill = Mod.Grad(false, (Mod.A(Color.FromRgb(0x6C, 0xC4, 0xEC), .96), 0), (Mod.A(Color.FromRgb(0x1C, 0x5E, 0x84), .97), 1));
    static readonly Pen CrestEdge = Mod.P(Mod.Br(Color.FromRgb(0x02, 0x10, 0x1C), .85), .9), CrestRidge = Mod.P(Mod.Br(Colors.White, .9), .7, round: true),
        CrestTick = Mod.P(Mod.Br(Colors.White), 1.4, round: true), CollarEdge = Mod.P(Mod.Br(Color.FromRgb(0x02, 0x10, 0x1C), .5), .7);
    static readonly Brush CrestCore = Mod.Grad(false, (Mod.A(Color.FromRgb(0x7F, 0xF0, 0xFF), .75), 0), (Mod.A(CyanC, .2), 1)),
        CollarFill = Mod.Grad(true, (Mod.A(Colors.White, .92), 0), (Mod.A(Color.FromRgb(0x9E, 0xDD, 0xF6), .85), .55), (Mod.A(Color.FromRgb(0x3C, 0x8C, 0xB4), .8), 1));
    static readonly Pen CrackPen = Mod.P(Mod.Br(Color.FromRgb(0xC8, 0xF0, 0xFF), .17), .7), CrackShade = Mod.P(Mod.Br(Colors.Black, .32), .7), InnerPen = Mod.P(Mod.Br(Colors.Black, .38), 1);
    /// <summary>The bevelled ice rim: 2 px, lit at the top-left, falling to deep shade at the bottom-right.</summary>
    static readonly Pen RimPen = Mod.P(Bevel(RimC), 2);
    static Brush Bevel(Color c)
    {
        var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
        b.GradientStops.Add(new GradientStop(Mod.A(Mod.Mix(c, Colors.White, .45), .98), 0));
        b.GradientStops.Add(new GradientStop(Mod.A(c, .55), .35));
        b.GradientStops.Add(new GradientStop(Mod.A(c, .28), .7));
        b.GradientStops.Add(new GradientStop(Mod.A(Color.FromRgb(0x02, 0x0A, 0x12), .9), 1));
        b.Freeze();
        return b;
    }

    /// <summary>
    /// The glacial panel: its drop, the deep navy-teal ice (lighter at its top), a cold light from the top-left, two facet
    /// planes, hairline cracks in from the edges, the dark inner line and the lit rim, the specular line along its top, rime at
    /// its corners. <paramref name="opacity"/> is the window's setting; <paramref name="sheen"/> 0 to 1 a cold glint crossing
    /// it; <paramref name="frost"/> 0 to 1 a breath of frost over the whole face (a moment's flash); <paramref name="tint"/>
    /// lights it in a grade's colour (a rare drop).
    /// </summary>
    static void IcePanel(DrawingContext dc, Size s, IceArt art, double opacity, double sheen = -1, double frost = 0, Color? tint = null, bool shadow = true, double rimeK = 1)
    {
        if (s.Width < 8 || s.Height < 8) return;
        art.Fit(s);
        var r = art.R;
        if (shadow) for (int k = 0; k < art.Shadow.Length; k++) dc.DrawGeometry(Mod.Br(Colors.Black, .1 - k * .02), null, art.Shadow[k]);
        // the silhouette: the crystal shards and the icicles, behind the slab (growing out from under its edge)
        if (art.CrestK > 0 || art.IcicleK > 0)
        {
            dc.DrawGeometry(CrestLit, CrestEdge, art.Crest);
            dc.DrawGeometry(CrestShadeFill, CrestEdge, art.CrestShade);
            dc.DrawGeometry(CrestCore, null, art.Cores);
            dc.DrawGeometry(null, CrestRidge, art.Ridges);
            dc.DrawGeometry(null, CrestTick, art.Ticks);
        }
        double a = Math.Clamp(.6 + .32 * opacity, .7, .95);
        dc.DrawGeometry(Mod.Grad(true, (Mod.A(TopC, a), 0), (Mod.A(BotC, Math.Min(.97, a + .04)), 1)), null, art.Shape);
        dc.PushClip(art.Shape);
        // the cold light from the top-left (a tint's colour in a rare drop's plate)
        var gc = tint ?? CyanC;
        var glow = new RadialGradientBrush { Center = new Point(.12, 0), GradientOrigin = new Point(.12, 0), RadiusX = .75, RadiusY = 1.3 };
        glow.GradientStops.Add(new GradientStop(Mod.A(gc, tint != null ? .36 : .16), 0));
        glow.GradientStops.Add(new GradientStop(Mod.A(gc, tint != null ? .1 : .04), .55));
        glow.GradientStops.Add(new GradientStop(Mod.A(gc, 0), 1));
        glow.Freeze();
        dc.DrawRectangle(glow, null, r);
        dc.DrawGeometry(Mod.Br(Colors.White, .035), null, art.Facets);
        dc.DrawRectangle(Mod.Grad(true, (Mod.A(Colors.White, .07), 0), (Mod.A(Colors.White, 0), .42)), null, r);
        dc.PushTransform(new TranslateTransform(0, 1)); dc.DrawGeometry(null, CrackShade, art.Cracks); dc.Pop();
        dc.DrawGeometry(null, CrackPen, art.Cracks);
        if (frost > 0) dc.DrawRectangle(Mod.Grad(true, (Mod.A(FrostC, .24 * frost), 0), (Mod.A(IceC, .08 * frost), 1)), null, r);
        if (sheen is > 0 and < 1)
        {
            // the cold glint: a slanted band of light crossing left to right, a bright core in a pale blue haze
            double x = -70 + (r.Width + 140) * sheen;
            var band = new StreamGeometry();
            using (var g = band.Open()) { g.BeginFigure(new Point(x, 0), true, true); g.LineTo(new Point(x + 40, 0), true, false); g.LineTo(new Point(x + 14, s.Height), true, false); g.LineTo(new Point(x - 26, s.Height), true, false); }
            band.Freeze();
            double k = Math.Sin(Math.PI * sheen);
            dc.DrawGeometry(Mod.Grad(false, (Mod.A(IceC, 0), 0), (Mod.A(IceC, .16 * k), .35), (Mod.A(Colors.White, .34 * k), .55), (Mod.A(IceC, 0), 1)), null, band);
        }
        // the specular line along the top, between the cuts
        dc.DrawRectangle(Mod.Grad(false, (Mod.A(Colors.White, 0), 0), (Mod.A(Colors.White, .42), .18), (Mod.A(Colors.White, .2), .7), (Mod.A(Colors.White, 0), 1)), null, new Rect(r.Left + art.A, r.Top + 1, Math.Max(0, r.Width - art.A - art.B), 1));
        dc.DrawGeometry(null, InnerPen, art.Inner);
        if (rimeK > 0)
        {
            Rime(dc, RimeBig, r, 1, 1, rimeK);
            Rime(dc, RimeSmall, r, -1, 1, .85 * rimeK);
            Rime(dc, RimeTiny, r, 1, -1, .6 * rimeK);
            Rime(dc, RimeSmall, r, -1, -1, .7 * rimeK);
        }
        dc.Pop();
        dc.DrawGeometry(null, tint is { } t ? Mod.P(Bevel(t), 2) : RimPen, art.Shape);
        // the frost collars where the crystals grow out, over the rim (so they grow from the ice, not sit on it)
        if (art.CrestK > 0 || art.IcicleK > 0) dc.DrawGeometry(CollarFill, CollarEdge, art.Collars);
    }

    // ================================================================ the ice bar

    /// <summary>
    /// An ice bar in <paramref name="r"/>, filled <paramref name="fx"/> px: translucent crystal in a cool tint of
    /// <paramref name="c"/> (lighter at its top, deeper at its foot, clouding white towards its end), slanted crystal planes
    /// through it, a specular line, hoarfrost along its top edge (to <paramref name="rimeTo"/> of its length), and an icy-white
    /// leading edge cut on a slant with a cold glow behind it. <paramref name="lead"/>: the clearest and brightest, a glint at its tip.
    /// </summary>
    static void IceBar(DrawingContext dc, Rect r, double fx, Color c, bool lead, double rimeTo = 1, double strength = 1, bool tip = true, bool edge = true)
    {
        if (fx < 1) return;
        var k = BarInk.Of(c, lead, strength);
        double sl = Math.Min(3, fx / 2);
        var body = new StreamGeometry();
        using (var g = body.Open())
        {
            g.BeginFigure(new Point(r.Left, r.Top), true, true);
            g.LineTo(new Point(r.Left + fx + sl, r.Top), true, false);
            g.LineTo(new Point(r.Left + fx - sl, r.Bottom), true, false);
            g.LineTo(new Point(r.Left, r.Bottom), true, false);
        }
        body.Freeze();
        dc.DrawGeometry(k.Body, null, body);
        dc.PushClip(body);
        // clouding towards the end: the ice frosts white as it nears its edge
        double fl = Math.Min(fx, 90);
        dc.DrawRectangle(k.Cloud, null, new Rect(r.Left + fx - fl, r.Top, fl + sl, r.Height));
        dc.PushTransform(new TranslateTransform(r.Left, r.Top));
        dc.DrawGeometry(null, PlanePen, Planes);
        dc.Pop();
        // the specular: a soft band down from the top and one bright line
        dc.DrawRectangle(k.Spec, null, new Rect(r.Left, r.Top, fx + sl, r.Height));
        dc.DrawRectangle(k.Line, null, new Rect(r.Left, r.Top + 1, fx + sl, .8));
        // the hoarfrost along the top edge
        if (rimeTo > 0)
        {
            dc.PushClip(new RectangleGeometry(new Rect(r.Left, r.Top, (fx + sl) * Math.Clamp(rimeTo, 0, 1), r.Height)));
            dc.PushTransform(new TranslateTransform(r.Left, r.Top));
            dc.DrawGeometry(null, RimeEdgePen, RimeEdge);
            dc.Pop(); dc.Pop();
        }
        // the cold glow behind the leading edge
        double gw = Math.Min(5, fx);
        if (edge) dc.DrawRectangle(k.Glow, null, new Rect(r.Left + fx - gw - sl, r.Top, gw + 2 * sl, r.Height));
        dc.Pop();
        // the leading edge: a crisp icy-white cut on the slant
        if (edge) dc.DrawLine(k.Edge, new Point(r.Left + fx + sl - .3, r.Top + .4), new Point(r.Left + fx - sl - .3, r.Bottom - .4));
        if (lead && tip) dc.DrawRectangle(SparkleBrush, null, new Rect(r.Left + fx + sl - 5.5, r.Top - 3.5, 10, 10));
    }

    /// <summary>An ice bar's brushes for a colour, made once (frozen) and kept: a bar drawn again makes nothing new but its shape.</summary>
    sealed record BarInk(Brush Body, Brush Cloud, Brush Spec, Brush Line, Brush Glow, Pen Edge)
    {
        static readonly Dictionary<(Color, bool, double), BarInk> Made = new();
        public static BarInk Of(Color c, bool lead, double strength)
        {
            if (Made.TryGetValue((c, lead, strength), out var k)) return k;
            var tint = IceTint(c);
            double a = (lead ? .84 : .64) * strength;
            k = new BarInk(
                Mod.Grad(true, (Mod.A(Mod.Mix(tint, Colors.White, .3), a), 0), (Mod.A(tint, a * .8), .42), (Mod.A(Mod.Mix(tint, DeepC, .42), a * .92), 1)),
                Mod.Grad(false, (Mod.A(FrostC, 0), 0), (Mod.A(FrostC, (lead ? .2 : .12) * strength), 1)),
                Mod.Grad(true, (Mod.A(Colors.White, .2 * strength), 0), (Mod.A(Colors.White, 0), .5)),
                Mod.Br(Colors.White, (lead ? .5 : .34) * strength),
                Mod.Grad(false, (Mod.A(IceC, 0), 0), (Mod.A(Mod.Mix(tint, Colors.White, .55), (lead ? .62 : .4) * strength), 1)),
                Mod.P(Mod.Br(FrostC, (lead ? 1 : .82) * strength), lead ? 1.7 : 1.3));
            if (Made.Count < 256) Made[(c, lead, strength)] = k;
            return k;
        }
    }

    /// <summary>A four-pointed glint of cold light (a star of two thin diamonds over a soft core).</summary>
    static void Sparkle(DrawingContext dc, Point p, double s, double a)
    {
        if (s <= .3 || a <= .01) return;
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            double w = s * .2;
            c.BeginFigure(new Point(p.X, p.Y - s), true, true); c.LineTo(new Point(p.X + w, p.Y), true, false); c.LineTo(new Point(p.X, p.Y + s), true, false); c.LineTo(new Point(p.X - w, p.Y), true, false);
            c.BeginFigure(new Point(p.X - s, p.Y), true, true); c.LineTo(new Point(p.X, p.Y - w), true, false); c.LineTo(new Point(p.X + s, p.Y), true, false); c.LineTo(new Point(p.X, p.Y + w), true, false);
        }
        g.Freeze();
        var core = new RadialGradientBrush(Mod.A(Colors.White, .7 * a), Mod.A(CyanC, 0));
        core.Freeze();
        dc.DrawEllipse(core, null, p, s * .7, s * .7);
        dc.DrawGeometry(Mod.Br(Colors.White, a), null, g);
    }

    // ================================================================ the moving parts, painted once

    /// <summary>
    /// A drawing painted once into a frozen brush over <paramref name="box"/> (an element exactly that size wears it): the
    /// parts a moment moves are these, so a moment only moves a transform or an opacity, never draws again.
    /// </summary>
    static DrawingBrush Paint(Rect box, Action<DrawingContext> draw)
    {
        var dg = new DrawingGroup();
        using (var dc = dg.Open())
        {
            dc.DrawRectangle(Brushes.Transparent, null, box);
            dc.PushClip(new RectangleGeometry(box));
            draw(dc);
            dc.Pop();
        }
        var b = new DrawingBrush(dg) { Viewbox = box, ViewboxUnits = BrushMappingMode.Absolute, Stretch = Stretch.Fill };
        b.Freeze();
        return b;
    }

    /// <summary>An element of <paramref name="w"/> x <paramref name="h"/> wearing a painted brush, centred in its cell, taking no clicks.</summary>
    static Border Piece(Brush paint, double w, double h, double opacity = 1) => new()
    {
        Width = w, Height = h, Background = paint, IsHitTestVisible = false, Opacity = opacity,
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, RenderTransformOrigin = new Point(.5, .5),
    };

    /// <summary>The snowflake's six arms (<paramref name="arms"/>), or its branches alone, radius <paramref name="r"/> at the origin.</summary>
    static Geometry FlakeGeo(double r, bool arms)
    {
        var g = new StreamGeometry();
        using (var s = g.Open())
            for (int i = 0; i < 6; i++)
            {
                double a = -Math.PI / 2 + i * Math.PI / 3, ca = Math.Cos(a), sa = Math.Sin(a);
                if (arms) { s.BeginFigure(new Point(ca * r * .2, sa * r * .2), false, false); s.LineTo(new Point(ca * r, sa * r), true, false); continue; }
                foreach (var (at, bl) in new[] { (.48, .36), (.74, .26) })
                {
                    var p = new Point(ca * r * at, sa * r * at);
                    foreach (var side in new[] { Math.PI / 3, -Math.PI / 3 })
                    {
                        double b = a + side;
                        s.BeginFigure(p, false, false); s.LineTo(new Point(p.X + Math.Cos(b) * r * bl, p.Y + Math.Sin(b) * r * bl), true, false);
                    }
                }
            }
        g.Freeze();
        return g;
    }

    static void InkFlake(DrawingContext dc, Geometry g)
    {
        dc.DrawGeometry(null, Mod.P(Mod.Br(DeepC, .75), 2.9, round: true), g);
        dc.DrawGeometry(null, Mod.P(Mod.Br(CyanC, .5), 2.1, round: true), g);
        dc.DrawGeometry(null, Mod.P(Mod.Br(FrostC), 1.15, round: true), g);
    }

    /// <summary>A hexagon of radius <paramref name="r"/> at the origin, turned by <paramref name="turn"/>.</summary>
    static Geometry Hex(double r, double turn = 0)
    {
        var hex = new StreamGeometry();
        using (var s = hex.Open())
            for (int i = 0; i < 6; i++) { double a = i * Math.PI / 3 + turn; var p = new Point(Math.Cos(a) * r, Math.Sin(a) * r); if (i == 0) s.BeginFigure(p, true, true); else s.LineTo(p, true, false); }
        hex.Freeze();
        return hex;
    }

    /// <summary>The crown in parts a moment grows one after another: the arms with the heart, the branches, the cold light behind, the ring of frost it flings out.</summary>
    const double FlakeR = 8.8;
    static readonly DrawingBrush FlakeArms = Paint(new Rect(-10.5, -10.5, 21, 21), dc =>
    {
        InkFlake(dc, FlakeGeo(FlakeR, true));
        dc.DrawGeometry(Mod.Br(IceC), Mod.P(Mod.Br(Colors.White), .8), Hex(FlakeR * .27));
    });
    static readonly DrawingBrush FlakeBranches = Paint(new Rect(-10.5, -10.5, 21, 21), dc => InkFlake(dc, FlakeGeo(FlakeR, false)));
    /// <summary>The flash freeze: a great snowflake in cold light that blooms out behind the crown when someone takes the lead.</summary>
    static readonly DrawingBrush FlakeBloom = Paint(new Rect(-25, -25, 50, 50), dc =>
    {
        dc.DrawEllipse(MakeGlow(CyanC, .5), null, new Point(0, 0), 24, 24);
        foreach (var arms in new[] { true, false })
        {
            var g = FlakeGeo(20, arms);
            dc.DrawGeometry(null, Mod.P(Mod.Br(CyanC, .45), 3.4, round: true), g);
            dc.DrawGeometry(null, Mod.P(Mod.Br(Colors.White, .95), 1.4, round: true), g);
        }
    });
    /// <summary>The flash freeze's first frames: the great snowflake's arms thick and white-hot in a wide cyan glow.</summary>
    static readonly DrawingBrush FlakeBloomHot = Paint(new Rect(-25, -25, 50, 50), dc =>
    {
        dc.DrawEllipse(MakeGlow(Colors.White, .55), null, new Point(0, 0), 16, 16);
        foreach (var arms in new[] { true, false })
        {
            var g = FlakeGeo(20, arms);
            dc.DrawGeometry(null, Mod.P(Mod.Br(CyanC, .5), 8, round: true), g);
            dc.DrawGeometry(null, Mod.P(Mod.Br(Colors.White, .95), 3.2, round: true), g);
        }
    });
    /// <summary>Your best passed: a starburst of ice crystals flung out round the ring.</summary>
    static readonly DrawingBrush Starburst = Paint(new Rect(-48, -48, 96, 96), dc =>
    {
        var rnd = new Random(3);
        for (int i = 0; i < 18; i++)
        {
            double a = i * Math.PI * 2 / 18 + (rnd.NextDouble() - .5) * .15, r0 = 21, len = i % 2 == 0 ? 25 : 14;
            double ca = Math.Cos(a), sa = Math.Sin(a), nx = -sa, ny = ca, w = i % 2 == 0 ? 3 : 2;
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                var b = new Point(ca * r0, sa * r0); var tip = new Point(ca * (r0 + len), sa * (r0 + len));
                c.BeginFigure(new Point(b.X + nx * w, b.Y + ny * w), true, true); c.LineTo(tip, true, false); c.LineTo(new Point(b.X - nx * w, b.Y - ny * w), true, false);
            }
            g.Freeze();
            dc.DrawGeometry(Mod.Br(Colors.White), Mod.P(Mod.Br(Color.FromRgb(0x1C, 0x5E, 0x84), .8), .9), g);
        }
    });
    /// <summary>A splinter of ice a kill shatters off (a long kite of clear ice with a lit edge).</summary>
    static readonly DrawingBrush Splinter = Paint(new Rect(-6, -2.5, 12, 5), dc =>
    {
        var g = new StreamGeometry();
        using (var c = g.Open()) { c.BeginFigure(new Point(-5.5, 0), true, true); c.LineTo(new Point(-1, -2), true, false); c.LineTo(new Point(5.5, 0), true, false); c.LineTo(new Point(1, 2), true, false); }
        g.Freeze();
        dc.DrawGeometry(Mod.Grad(true, (Colors.White, 0), (Mod.A(IceC, .9), .5), (Mod.A(Color.FromRgb(0x3A, 0x9C, 0xC8), .95), 1)), Mod.P(Mod.Br(DeepC, .6), .5), g);
    });
    static readonly Brush FlakeGlow = MakeGlow(IceC, .6);
    static readonly DrawingBrush FrostRing = Paint(new Rect(-15, -15, 30, 30), dc =>
    {
        dc.DrawGeometry(null, Mod.P(Mod.Br(CyanC, .35), 2.6), Hex(12, Math.PI / 6));
        dc.DrawGeometry(null, Mod.P(Mod.Br(FrostC, .95), 1), Hex(12, Math.PI / 6));
    });
    /// <summary>A glint of cold light (<see cref="Sparkle"/>) on a 12 px square.</summary>
    static readonly DrawingBrush SparkleBrush = Paint(new Rect(-6, -6, 12, 12), dc => Sparkle(dc, new Point(0, 0), 5.4, 1));

    static Brush MakeGlow(Color c, double a)
    {
        var b = new RadialGradientBrush(Mod.A(c, a), Mod.A(c, 0));
        b.GradientStops.Insert(1, new GradientStop(Mod.A(c, a * .35), .5));
        b.Freeze();
        return b;
    }

    /// <summary>The fresh frost veil riding the creeping front (clear, to white, to clear).</summary>
    static readonly Brush VeilBrush = Mod.Grad(false, (Mod.A(FrostC, 0), 0), (Mod.A(Colors.White, .5), .86), (Mod.A(FrostC, 0), 1));
    /// <summary>The cold glint: a narrow slanted blade of light (a row's), a wide band (a panel's).</summary>
    static readonly Geometry BladeGeo = MakeBlade(16, 40, 9), BandGeo = MakeBlade(46, 120, 30);
    static Geometry MakeBlade(double w, double h, double lean)
    {
        var g = new StreamGeometry();
        using (var c = g.Open()) { c.BeginFigure(new Point(lean, 0), true, true); c.LineTo(new Point(lean + w * .6, 0), true, false); c.LineTo(new Point(w * .6, h), true, false); c.LineTo(new Point(0, h), true, false); }
        g.Freeze();
        return g;
    }
    static readonly Brush BladeBrush = Mod.Grad(false, (Mod.A(IceC, 0), 0), (Mod.A(Colors.White, .36), .5), (Mod.A(IceC, 0), 1));
    static readonly Brush BandBrush = Mod.Grad(false, (Mod.A(IceC, 0), 0), (Mod.A(IceC, .16), .35), (Mod.A(Colors.White, .36), .55), (Mod.A(IceC, 0), 1));

    /// <summary>A slanted glint of light as a shape a moment slides (its geometry and brush frozen), out of sight to start.</summary>
    static System.Windows.Shapes.Path Glint(Geometry g, Brush b) => new() { Data = g, Fill = b, IsHitTestVisible = false, RenderTransform = new TranslateTransform(-400, 0) };

    /// <summary>A panel's frost breath: the same ice shape filled with pale frost, drawn once; a moment sets its opacity.</summary>
    static TacSketch FrostBreath(IceArt art, Color? tint = null, bool fill = true) => new((dc, s) =>
    {
        if (s.Width < 8) return;
        art.Fit(s);
        var c = tint ?? FrostC;
        if (!fill) { dc.DrawGeometry(null, Mod.P(Mod.Br(CyanC, .45), 5), art.Shape); dc.DrawGeometry(null, Mod.P(Mod.Br(Colors.White), 2), art.Shape); return; }
        dc.DrawGeometry(Mod.Grad(true, (Mod.A(Mod.Mix(c, Colors.White, .4), .32), 0), (Mod.A(c, .1), 1)), Mod.P(Mod.Br(Mod.Mix(c, Colors.White, .5), .9), 1.4), art.Shape);
    }) { Opacity = 0, Visibility = Visibility.Hidden };

    /// <summary>A moment's overlay at <paramref name="opacity"/> (hidden, so not drawn at all, at nothing).</summary>
    static void Show(UIElement el, double opacity)
    {
        el.Opacity = Math.Clamp(opacity, 0, 1);
        var v = opacity > .003 ? Visibility.Visible : Visibility.Hidden;
        if (el.Visibility != v) el.Visibility = v;
    }

    // ================================================================ the rows' brushes
    // (here, after the palette they're made from: static fields of a partial class run file by file)
    // the rows' brushes and pens (frozen, shared by every row)
    static readonly Brush SlabFill = Mod.Grad(true, (Mod.A(Color.FromRgb(0x0A, 0x22, 0x30), .84), 0), (Mod.A(DeepC, .9), 1)),
        FigurePlate = Mod.Grad(true, (Mod.A(Color.FromRgb(0x06, 0x1A, 0x26), .78), 0), (Mod.A(DeepC, .86), 1)),
        HealLine = Mod.Br(MintC2, .9), YouSpine = Mod.Grad(true, (Colors.White, 0), (Mod.A(IceC, .9), 1)), DpsHalo = MakeGlow(CyanC, .26),
        ClimbTrail = Mod.Grad(false, (Mod.A(IceC, 0), 0), (Mod.A(Colors.White, .9), 1));
    static readonly Pen FigureRim = Mod.P(Mod.Br(CyanC, .28), .8);
    /// <summary>The crown's heart: a dark hexagon of deep ice, a lit rim, where the rank's numeral sits.</summary>
    static readonly DrawingBrush FlakeHeart = Paint(new Rect(-7.5, -7.5, 15, 15), dc =>
        dc.DrawGeometry(Mod.Br(Color.FromRgb(0x05, 0x16, 0x22), .96), Mod.P(Mod.Br(FrostC, .95), 1.1), Hex(6.4, Math.PI / 6)));
    static readonly Pen LeadHalo = Mod.P(Mod.Br(CyanC, .2), 3), ClimbPen = Mod.P(Mod.Br(FrostC, .9), 1.4),
        YouRim = Mod.P(Mod.Grad(true, (Mod.A(FrostC, .9), 0), (Mod.A(RimC, .5), 1)), 1.2),
        LeadRim = Mod.P(Mod.Grad(true, (Mod.A(RimC, .62), 0), (Mod.A(RimC, .16), 1)), 1),
        RowRim = Mod.P(Mod.Grad(true, (Mod.A(RimC, .34), 0), (Mod.A(RimC, .16), 1)), 1);

    // ================================================================ chips

    /// <summary>
    /// A small ice chip (TOP DPS, the heal mark, AVG, READY): a shard of dark ice with a lit rim in <paramref name="edge"/>
    /// and a facet of light at its left; <paramref name="filled"/>: clear ice-white with dark words (YOU).
    /// </summary>
    static Grid Chip(string s, Brush? fg = null, Color? edge = null, bool filled = false, double size = 8.5)
    {
        var t = Tt(s, size, filled ? Frozen(InkC) : fg ?? White, FontWeights.Bold);
        t.Margin = new Thickness(5, 0, 5, 1);
        var e = edge ?? RimC;
        var g = new Grid { VerticalAlignment = VerticalAlignment.Center };
        g.Children.Add(new TacSketch((dc, sz) =>
        {
            if (sz.Width < 4) return;
            var r = new Rect(.5, .5, sz.Width - 1, sz.Height - 1);
            var sh = Shard(r, 3.2, 1);
            if (filled) { dc.DrawGeometry(Mod.Grad(true, (Colors.White, 0), (Color.FromRgb(0xBE, 0xEB, 0xFF), 1)), Mod.P(Mod.Br(Colors.White), 1), sh); return; }
            dc.DrawGeometry(Mod.Grad(true, (Mod.A(TopC, .94), 0), (Mod.A(DeepC, .94), 1)), null, sh);
            dc.PushClip(sh);
            dc.DrawRectangle(Mod.Grad(false, (Mod.A(Colors.White, .1), 0), (Mod.A(Colors.White, 0), .6)), null, r);
            dc.Pop();
            dc.DrawGeometry(null, Mod.P(Mod.Grad(true, (Mod.A(e, .95), 0), (Mod.A(e, .45), 1)), 1), sh);
        }));
        g.Children.Add(t);
        return g;
    }

    /// <summary>
    /// Pickup tiles framed in ice: each tile of the strip (or one tile) gets a frosted, cut-cornered frame over its grade's,
    /// rime on two corners, a pixel outside it and taking no room.
    /// </summary>
    static T IceFramed<T>(T tiles, double size) where T : FrameworkElement
    {
        void Frame(Grid cell)
        {
            var f = new TacSketch((dc, s) =>
            {
                var r = new Rect(.5, .5, s.Width - 1, s.Height - 1);
                var sh = Shard(r, 4, 1.5);
                dc.DrawGeometry(null, Mod.P(Mod.Br(DeepC, .85), 2.4), sh);
                dc.DrawGeometry(null, Mod.P(Mod.Grad(true, (Mod.A(FrostC, .95), 0), (Mod.A(IceC, .5), .5), (Mod.A(IceC, .8), 1)), 1.1), sh);
                Rime(dc, RimeTiny, r, 1, 1, .9);
            }) { Width = size + 4, Height = size + 4, Margin = new Thickness(-2), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            cell.Children.Insert(Math.Min(1, cell.Children.Count), f);
        }
        if (tiles is Panel p && tiles.Tag is "pick-strip")
            foreach (var c in p.Children.OfType<Grid>().Where(c => c.Tag is "pick-tile" or "pick-best")) Frame(c);
        else if (tiles is Grid one) Frame(one);
        return tiles;
    }

    /// <summary>A frost line across a panel (the foot's, the card's): brightest in its middle, a small diamond of ice there.</summary>
    static FrameworkElement Hairline(Thickness m, double a = .32) => new TacSketch((dc, s) =>
    {
        dc.DrawRectangle(Mod.Grad(false, (Mod.A(RimC, 0), 0), (Mod.A(RimC, a), .5), (Mod.A(RimC, 0), 1)), null, new Rect(0, 1, s.Width, 1));
        double cx = Math.Round(s.Width / 2);
        var d = new StreamGeometry();
        using (var c = d.Open()) { c.BeginFigure(new Point(cx, -.5), true, true); c.LineTo(new Point(cx + 3, 1.5), true, false); c.LineTo(new Point(cx, 3.5), true, false); c.LineTo(new Point(cx - 3, 1.5), true, false); }
        d.Freeze();
        dc.DrawGeometry(Mod.Br(FrostC, Math.Min(1, a * 2.6)), null, d);
    }) { Height = 3, Margin = m, IsHitTestVisible = false };

    // ================================================================ the shade

    /// <summary>The tight shade under words: 1 px down, 3 px soft, near black.</summary>
    static DropShadowEffect Shade() => new() { Color = Color.FromRgb(0x00, 0x08, 0x10), Direction = 270, ShadowDepth = 1, BlurRadius = 3, Opacity = .95 };

    /// <summary>The wide soft halo round words straight on the game, so a bright scene behind them goes a shade darker.</summary>
    static DropShadowEffect Halo() => new() { Color = Color.FromRgb(0x00, 0x06, 0x0C), ShadowDepth = 0, BlurRadius = 10, Opacity = .66 };

    /// <summary>The faint cyan halo round a big ice-white figure (breath-cold light).</summary>
    static DropShadowEffect ColdGlow(double a = .75) => new() { Color = CyanC, ShadowDepth = 0, BlurRadius = 8, Opacity = a };

    /// <summary>Words lit for the game (outside a panel): the tight shade on them, the halo round them.</summary>
    static Border Lit(FrameworkElement el)
    {
        el.Effect = Shade();
        return new Border { Child = el, Effect = Halo(), HorizontalAlignment = el.HorizontalAlignment, VerticalAlignment = el.VerticalAlignment };
    }
}
