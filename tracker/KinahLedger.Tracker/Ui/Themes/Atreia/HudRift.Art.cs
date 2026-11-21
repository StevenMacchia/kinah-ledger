using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

/// <summary>
/// Rift's drawn parts, shared by the Buddy, the meter, its rows and the card: the Abyss glass (violet-black, a nebula and a
/// field of stars made once, a tear of light along its top), the fissure of rift light at a bar's leading edge, and the
/// crown of three crystal shards. All of it static geometry and frozen brushes: a draw stays cheap.
/// </summary>
sealed partial class HudRift
{
    // the palette: the Abyss's void, its violet, the rift's light (a near-white cyan), starlight for the figures
    internal static readonly Color VoidC = Color.FromRgb(0x0D, 0x07, 0x16), DuskC = Color.FromRgb(0x17, 0x0A, 0x2C), DeepVioletC = Color.FromRgb(0x2A, 0x13, 0x55),
        VioletC = Color.FromRgb(0x8E, 0x5C, 0xFF), LilacC = Color.FromRgb(0xC2, 0xA8, 0xFF), CyanC = Color.FromRgb(0x9C, 0xF2, 0xFF), LightC = Color.FromRgb(0xE6, 0xFC, 0xFF),
        StarC = Color.FromRgb(0xF7, 0xF4, 0xFF), MintC2 = Color.FromRgb(0x6C, 0xF5, 0xC2), CoralC = Color.FromRgb(0xFF, 0x86, 0x98), GoldC2 = Color.FromRgb(0xFF, 0xD2, 0x7A),
        WarnC2 = Color.FromRgb(0xFF, 0xB0, 0x86), HpAC = Color.FromRgb(0xFF, 0x4F, 0x8B), HpBC = Color.FromRgb(0xB8, 0x52, 0xFF);

    /// <summary>The field of stars: a tile of faint points (lilac, white, a few cyan; the brightest with a small cross), made once.</summary>
    static readonly ImageBrush Stars = MakeStars();

    static ImageBrush MakeStars()
    {
        const int n = 160;
        var px = new byte[n * n * 4];
        var rnd = new Random(2611);
        void Put(int x, int y, Color c, double a)
        {
            x = (x % n + n) % n; y = (y % n + n) % n;
            int i = (y * n + x) * 4;
            byte al = (byte)Math.Clamp(Math.Round(a * 255), 0, 255);
            if (al <= px[i + 3]) return;
            // premultiplied BGRA
            px[i] = (byte)(c.B * al / 255); px[i + 1] = (byte)(c.G * al / 255); px[i + 2] = (byte)(c.R * al / 255); px[i + 3] = al;
        }
        for (int k = 0; k < 132; k++)
        {
            int x = rnd.Next(n), y = rnd.Next(n);
            double r = rnd.NextDouble(), a = .12 + .6 * r * r * r;
            double pick = rnd.NextDouble();
            var c = pick < .55 ? Color.FromRgb(0xEE, 0xE8, 0xFF) : pick < .85 ? LilacC : CyanC;
            Put(x, y, c, a);
            if (a > .42) { Put(x + 1, y, c, a * .35); Put(x - 1, y, c, a * .35); Put(x, y + 1, c, a * .35); Put(x, y - 1, c, a * .35); }
        }
        var bmp = BitmapSource.Create(n, n, 96, 96, PixelFormats.Pbgra32, null, px, n * 4);
        bmp.Freeze();
        var b = new ImageBrush(bmp) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, n, n), ViewportUnits = BrushMappingMode.Absolute, Stretch = Stretch.None };
        b.Freeze();
        return b;
    }

    static RadialGradientBrush Radial(Point center, double rx, double ry, params (Color C, double At)[] stops)
    {
        var b = new RadialGradientBrush { Center = center, GradientOrigin = center, RadiusX = rx, RadiusY = ry };
        foreach (var (c, at) in stops) b.GradientStops.Add(new GradientStop(c, at));
        b.Freeze();
        return b;
    }

    // the nebula: a violet cloud at the top left, a cold blue one low at the right (relative, so one brush fits every panel)
    static readonly RadialGradientBrush NebulaA = Radial(new Point(.16, .05), .55, 1.15, (Mod.A(Color.FromRgb(0x7A, 0x38, 0xFF), .24), 0), (Mod.A(Color.FromRgb(0x7A, 0x38, 0xFF), .08), .55), (Mod.A(Color.FromRgb(0x7A, 0x38, 0xFF), 0), 1));
    static readonly RadialGradientBrush NebulaB = Radial(new Point(.88, .95), .5, 1.0, (Mod.A(Color.FromRgb(0x2E, 0x5E, 0xFF), .14), 0), (Mod.A(Color.FromRgb(0x2E, 0x5E, 0xFF), 0), 1));
    // the bloom under the tear, and a pulse of light running along it (an ellipse filling the rect it is drawn in)
    static readonly RadialGradientBrush TearBloom = Radial(new Point(.5, .5), .5, .5, (Mod.A(VioletC, .42), 0), (Mod.A(VioletC, .14), .55), (Mod.A(VioletC, 0), 1));
    static readonly RadialGradientBrush PulseGlow = Radial(new Point(.5, .5), .5, .5, (Mod.A(Colors.White, 1), 0), (Mod.A(CyanC, .6), .3), (Mod.A(VioletC, .2), .6), (Mod.A(VioletC, 0), 1));
    static readonly RadialGradientBrush EdgeGlow = Radial(new Point(.5, .5), .5, .5, (Mod.A(CyanC, .5), 0), (Mod.A(VioletC, .22), .5), (Mod.A(VioletC, 0), 1));
    static readonly RadialGradientBrush Spark = Radial(new Point(.5, .5), .5, .5, (Mod.A(Colors.White, 1), 0), (Mod.A(CyanC, .7), .3), (Mod.A(VioletC, .25), .6), (Mod.A(VioletC, 0), 1));

    /// <summary>A fixed jitter in -1..1 for the point <paramref name="i"/> of a tear (the same tear every draw).</summary>
    static double Jit(int i, int seed = 0) => ((((i + seed * 31) * 7919 + 104729) % 97) / 48.0) - 1;

    /// <summary>
    /// A tear of light from <paramref name="x0"/> to <paramref name="x1"/> along <paramref name="y"/>: a thin jagged sliver,
    /// thickest in its middle (<paramref name="thick"/>), drawn to nothing at its ends.
    /// </summary>
    static Geometry TearAcross(double x0, double x1, double y, double thick, int seed = 0)
    {
        int n = Math.Max(6, (int)((x1 - x0) / 16));
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            var top = new Point[n + 1]; var bot = new Point[n + 1];
            for (int i = 0; i <= n; i++)
            {
                double t = (double)i / n, x = x0 + (x1 - x0) * t, half = thick / 2 * Math.Pow(Math.Sin(Math.PI * t), .7);
                double jy = (i == 0 || i == n) ? 0 : Jit(i, seed) * .55;
                top[i] = new Point(x, y + jy - half); bot[i] = new Point(x, y + jy + half);
            }
            c.BeginFigure(top[0], true, true);
            for (int i = 1; i <= n; i++) c.LineTo(top[i], true, false);
            for (int i = n; i >= 0; i--) c.LineTo(bot[i], true, false);
        }
        g.Freeze();
        return g;
    }

    /// <summary>A fissure down from <paramref name="top"/> for <paramref name="h"/> at <paramref name="x"/>: a jagged sliver <paramref name="w"/> wide, pointed at its ends.</summary>
    static Geometry TearDown(double x, double top, double h, double w, int seed = 0)
    {
        const int n = 5;
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            var l = new Point[n + 1]; var r = new Point[n + 1];
            for (int i = 0; i <= n; i++)
            {
                double t = (double)i / n, y = top + h * t, half = w / 2 * Math.Pow(Math.Sin(Math.PI * (.08 + .84 * t)), .5);
                double jx = (i == 0 || i == n) ? 0 : Jit(i, seed) * 1.1;
                l[i] = new Point(x + jx - half, y); r[i] = new Point(x + jx + half, y);
            }
            c.BeginFigure(l[0], true, true);
            for (int i = 1; i <= n; i++) c.LineTo(l[i], true, false);
            for (int i = n; i >= 0; i--) c.LineTo(r[i], true, false);
        }
        g.Freeze();
        return g;
    }

    /// <summary>
    /// The Abyss glass: a soft drop under it (outside its shape only), the violet-black body, the nebula, the stars, a faint
    /// light falling from the top, the rim, and the tear of light along its top edge with its violet bloom. <paramref name="pulse"/>
    /// 0 to 1: a bright point of light running along the tear (in <paramref name="tint"/>); <paramref name="flare"/> lifts the tear.
    /// </summary>
    static double Out(double p) => Motion.Out(p);

    internal static void RiftPanel(DrawingContext dc, Size s, double radius, double opacity, double pulse = -1, Color? tint = null, double flare = 0, bool shadow = true, double tear = 1, bool wound = false)
    {
        if (s.Width < 6 || s.Height < 6) return;
        var r = new Rect(.5, .5, s.Width - 1, s.Height - 1);
        var shape = Mod.Pill(r, radius);
        if (shadow)
        {
            // the drop: rings outside the shape, deeper below it (kept inside the window's room)
            for (int k = 1; k <= 5; k++)
            {
                var o = new Rect(r.X - k * 1.1, r.Y - k * .5 + 2.5, r.Width + k * 2.2, r.Height + k * 1.5 + 1.5);
                var ring = new CombinedGeometry(GeometryCombineMode.Exclude, Mod.Pill(o, radius + k), shape);
                ring.Freeze();
                dc.DrawGeometry(Mod.Br(Colors.Black, .085 - k * .013), null, ring);
            }
        }
        double a = Math.Clamp(.56 + .38 * opacity, .66, .95);
        dc.DrawGeometry(Mod.Grad(true, (Mod.A(DuskC, a), 0), (Mod.A(VoidC, Math.Min(.97, a + .05)), 1)), null, shape);
        dc.PushClip(shape);
        var all = new Rect(0, 0, s.Width, s.Height);
        dc.DrawRectangle(NebulaA, null, all);
        dc.DrawRectangle(NebulaB, null, all);
        dc.PushOpacity(.85);
        dc.DrawRectangle(Stars, null, all);
        dc.Pop();
        dc.DrawRectangle(Mod.Grad(true, (Mod.A(LilacC, .07), 0), (Mod.A(LilacC, 0), .4)), null, all);
        if (wound) tear = 0;
        if (tear > 0)
        {
            // the tear: its bloom falling into the glass, the sliver of light along the top, a white core in it
            var c = tint ?? CyanC;
            double x0 = radius * .35, x1 = s.Width - radius * .35, k = Math.Clamp(tear + flare, 0, 2.2), k1 = Math.Min(1, k);
            dc.PushOpacity(Math.Min(1, .95 * k));
            dc.DrawRectangle(TearBloom, null, new Rect(s.Width * .04, -14 - 3 * flare, s.Width * .92, 32 + 6 * flare));
            dc.Pop();
            // a halo of violet round the sliver, the sliver violet to the rift's light, then its white core
            dc.DrawGeometry(Mod.Grad(false, (Mod.A(VioletC, 0), 0), (Mod.A(VioletC, .55 * k1), .25), (Mod.A(Mod.Mix(VioletC, c, .5), .7 * k1), .5), (Mod.A(VioletC, .55 * k1), .75), (Mod.A(VioletC, 0), 1)),
                null, TearAcross(x0, x1, 2.2, 5.5 + 1.5 * flare, 1));
            dc.DrawGeometry(Mod.Grad(false, (Mod.A(VioletC, 0), 0), (Mod.A(Mod.Mix(VioletC, c, .45), .95 * k1), .18), (Mod.A(c, k1), .5), (Mod.A(Mod.Mix(VioletC, c, .45), .95 * k1), .82), (Mod.A(VioletC, 0), 1)),
                null, TearAcross(x0, x1, 2.2, 3.0 + .8 * flare));
            dc.DrawGeometry(Mod.Grad(false, (Mod.A(Colors.White, 0), .1), (Mod.A(Colors.White, Math.Min(1, .95 * k)), .5), (Mod.A(Colors.White, 0), .9)), null, TearAcross(x0, x1, 2.2, 1.3, 3));
            if (pulse is > 0 and < 1)
            {
                // the pulse: a point of light running left to right along the tear, brightest midway
                double px = x0 + (x1 - x0) * Out(pulse), pk = Math.Min(1, 1.6 * Math.Sin(Math.PI * pulse));
                var pc = tint ?? CyanC;
                // its trail: the tear lit behind it, fading back towards where it came from
                double tx = Math.Max(x0, px - 120);
                dc.PushOpacity(pk);
                dc.DrawRectangle(Mod.Grad(false, (Mod.A(pc, 0), 0), (Mod.A(Mod.Mix(pc, Colors.White, .4), .55), 1)), null, new Rect(tx, 0, Math.Max(0, px - tx), 6));
                dc.DrawRectangle(tint is { } t2 ? Radial(new Point(.5, .5), .5, .5, (Mod.A(Colors.White, 1), 0), (Mod.A(t2, .6), .3), (Mod.A(t2, 0), 1)) : PulseGlow, null, new Rect(px - 40, -12, 80, 28));
                dc.DrawEllipse(Frozen(Colors.White), null, new Point(px, 2.2), 2.4, 1.6);
                dc.Pop();
            }
        }
        dc.Pop();
        // the rim: a violet hairline, lighter along the top
        dc.DrawGeometry(null, Mod.P(Mod.Grad(true, (Mod.A(LilacC, .34), 0), (Mod.A(VioletC, .16), .5), (Mod.A(VioletC, .22), 1)), 1), shape);
        // the wound over it all, breaking the top edge
        if (wound) dc.DrawDrawing(WoundArt(Math.Round(s.Width), tint ?? CyanC));
    }

    /// <summary>The wound's widest point, as a share of the panel's width (the rip and the bursts start there).</summary>
    internal const double WoundPeak = .32, WoundY = 5;

    static readonly Dictionary<(double W, Color C), Drawing> WoundCache = new();

    /// <summary>
    /// The wound in space along a panel's top edge, made once per width and colour and frozen: a jagged lens of light up to
    /// 8 px wide at <see cref="WoundPeak"/>, violet halo, cyan body, white core, cracks branching off it, light bleeding down
    /// from it in drips, streaks of warped light (violet and cyan, a pixel apart) along it, motes round it and a violet bloom
    /// falling into the glass. Its upper edge breaks a few pixels past the panel's top (the window's room above it).
    /// </summary>
    internal static Drawing WoundArt(double w, Color c)
    {
        if (WoundCache.TryGetValue((w, c), out var d)) return d;
        if (WoundCache.Count > 60) WoundCache.Clear();
        var g = new DrawingGroup();
        var rnd = new Random(7177);
        double x0 = -4, x1 = w + 4, y = WoundY, peak = WoundPeak;
        // a gentle bend along it, so it reads as torn rather than ruled
        double Bend(double t) => .9 * Math.Sin(Math.PI * 2.3 * t + .6);
        double Half(double t)
        {
            double u = t < peak ? .5 * t / peak : .5 + .5 * (t - peak) / (1 - peak);
            return Math.Pow(Math.Sin(Math.PI * u), 1.6);
        }
        Geometry Lens(double thick, int seed)
        {
            var r = new Random(seed);
            int n = 2 * Math.Max(8, (int)((x1 - x0) / 44));
            var top = new Point[n + 1]; var bot = new Point[n + 1];
            for (int i = 0; i <= n; i++)
            {
                double t = (double)i / n + (i == 0 || i == n ? 0 : (r.NextDouble() - .5) * 1.1 / n), x = x0 + (x1 - x0) * t, h = thick / 2 * (.12 + .88 * Half(t)), yc = y + Bend(t);
                double up = i % 2 == 0 ? .45 + .45 * r.NextDouble() : .12 + .25 * r.NextDouble(), dn = i % 2 == 1 ? .65 + .65 * r.NextDouble() : .2 + .3 * r.NextDouble();
                top[i] = new Point(x, yc - h * up); bot[i] = new Point(x, yc + h * dn);
            }
            var sg = new StreamGeometry();
            using (var sc = sg.Open())
            {
                sc.BeginFigure(top[0], true, true);
                for (int i = 1; i <= n; i++) sc.LineTo(top[i], true, false);
                for (int i = n; i >= 0; i--) sc.LineTo(bot[i], true, false);
            }
            sg.Freeze();
            return sg;
        }
        using (var dc = g.Open())
        {
            // the bloom: violet light falling into the glass from the wound, brightest under its widest point
            dc.DrawRectangle(Radial(new Point(peak, .22), .6, .9, (Mod.A(Color.FromRgb(0x7B, 0x2C, 0xFF), .7), 0), (Mod.A(Color.FromRgb(0x7B, 0x2C, 0xFF), .28), .45), (Mod.A(VioletC, 0), 1)), null, new Rect(-6, -6, w + 12, 42));
            // the streaks of warped light along it: violet and cyan, a pixel apart, as space bends round the wound
            for (int i = 0; i < 7; i++)
            {
                double t = .1 + .8 * rnd.NextDouble(), len = Math.Min(w * .3, 18 + 46 * rnd.NextDouble()), sx = Math.Clamp(x0 + (x1 - x0) * t - len / 2, 4, Math.Max(4, w - len - 4));   // inside the panel's width
                double sy = y + (rnd.Next(2) == 0 ? -1 : 1) * (2.5 + 3 * Half(t) + 4 * rnd.NextDouble());
                if (sy < -3) sy = y + 4 + 3 * rnd.NextDouble();
                var sv = Mod.Grad(false, (Mod.A(Color.FromRgb(0xC2, 0x4D, 0xFF), 0), 0), (Mod.A(Color.FromRgb(0xC2, 0x4D, 0xFF), .55), .5), (Mod.A(Color.FromRgb(0xC2, 0x4D, 0xFF), 0), 1));
                var scy = Mod.Grad(false, (Mod.A(c, 0), 0), (Mod.A(c, .6), .5), (Mod.A(c, 0), 1));
                dc.DrawRectangle(sv, null, new Rect(sx - 2, sy, len, .9));
                dc.DrawRectangle(scy, null, new Rect(sx + 2, sy + 1, len, .9));
            }
            // the wound's heat: a hot white-cyan glow round its widest point
            dc.DrawRectangle(Radial(new Point(.5, .5), .5, .5, (Mod.A(LightC, .75), 0), (Mod.A(CyanC, .35), .4), (Mod.A(VioletC, 0), 1)), null, new Rect(x0 + (x1 - x0) * peak - Math.Min(90, (x1 - x0) * peak), y - 11, 2 * Math.Min(90, (x1 - x0) * peak), 26));   // never past the panel's left
            // the wound: halo, body, core
                        var edgeC = Color.FromRgb(0xC7, 0x7D, 0xFF);
            dc.DrawGeometry(Mod.Grad(false, (Mod.A(VioletC, 0), 0), (Mod.A(VioletC, .75), .12), (Mod.A(edgeC, .9), peak), (Mod.A(VioletC, .75), .88), (Mod.A(VioletC, 0), 1)), null, Lens(20, 11));
            dc.DrawGeometry(Mod.Grad(false, (Mod.A(edgeC, .2), 0), (Mod.A(Mod.Mix(edgeC, c, .5), .95), .12), (Mod.A(c, 1), peak), (Mod.A(Mod.Mix(edgeC, c, .5), .95), .88), (Mod.A(edgeC, .2), 1)), null, Lens(16, 12));
            var raw = Color.FromRgb(0xFF, 0x2E, 0x9A);
            dc.DrawGeometry(Mod.Grad(false, (Mod.A(raw, 0), .05), (Mod.A(raw, .55), .18), (Mod.A(raw, .95), peak), (Mod.A(raw, .55), .7), (Mod.A(raw, 0), .95)), null, Lens(9.5, 14));
            dc.DrawGeometry(Mod.Grad(false, (Mod.A(Colors.White, .3), 0), (Mod.A(Colors.White, .9), .15), (Mod.A(Colors.White, 1), peak), (Mod.A(Colors.White, .9), .85), (Mod.A(Colors.White, .3), 1)), null, Lens(5.5, 13));
            dc.DrawGeometry(Mod.Grad(false, (Mod.A(Color.FromRgb(0x05, 0x02, 0x0C), 0), peak - .16), (Mod.A(Color.FromRgb(0x05, 0x02, 0x0C), .95), peak), (Mod.A(Color.FromRgb(0x05, 0x02, 0x0C), 0), peak + .2)), null, Lens(1.8, 15));
            // the cracks branching off it, and the light bleeding down from it
            var crackFill = Mod.Grad(false, (Mod.A(LightC, .95), 0), (Mod.A(CyanC, 0), 1));
            for (int i = 0; i < 9; i++)
            {
                double t = .12 + .76 * rnd.NextDouble(), bx = x0 + (x1 - x0) * t;
                bool up = rnd.NextDouble() < .3;
                double len = up ? 4 + 3 * rnd.NextDouble() : 6 + 12 * rnd.NextDouble(), ang = (rnd.NextDouble() - .5) * 1.6, wid = .7 + .9 * Half(t);
                double ex = Math.Clamp(bx + Math.Sin(ang) * len * 1.6, 2, w - 2), ey = y + (up ? -1 : 1) * Math.Cos(ang) * len;
                double mx = (bx + ex) / 2 + (rnd.NextDouble() - .5) * 3, my = (y + ey) / 2;
                var cg = new StreamGeometry();
                using (var sc = cg.Open())
                {
                    sc.BeginFigure(new Point(bx - wid, y), true, true);
                    sc.LineTo(new Point(mx, my), true, false);
                    sc.LineTo(new Point(ex, ey), true, false);
                    sc.LineTo(new Point(mx + .4, my), true, false);
                    sc.LineTo(new Point(bx + wid, y), true, false);
                }
                cg.Freeze();
                dc.DrawGeometry(Mod.Br(Mod.Mix(c, Colors.White, .4), .85), null, cg);
            }
            for (int i = 0; i < 2; i++)
            {
                double t = peak + (i == 0 ? -.06 : .08), dx = x0 + (x1 - x0) * t, len = i == 0 ? 13 : 9, dw = 1.1;
                double top = y + Bend(t) + 3 + 6 * Half(t);
                dc.DrawRectangle(Mod.Grad(true, (Mod.A(Color.FromRgb(0xC7, 0x7D, 0xFF), .85), 0), (Mod.A(VioletC, .45), .5), (Mod.A(VioletC, 0), 1)), null, new Rect(dx - dw / 2, top, dw, len));
                dc.DrawEllipse(Mod.Br(Color.FromRgb(0xC7, 0x7D, 0xFF), .9), null, new Point(dx, top + len * .72), .7, 1);
            }
            // motes of light round it
            for (int i = 0; i < 14; i++)
            {
                double t = .1 + .8 * rnd.NextDouble(), mx = x0 + (x1 - x0) * t, my = y + (rnd.NextDouble() - .35) * 16;
                if (my < -2) my = -2;
                dc.DrawEllipse(Mod.Br(rnd.Next(3) == 0 ? CyanC : Colors.White, .45 + .5 * rnd.NextDouble()), null, new Point(mx, my), .5 + .6 * rnd.NextDouble(), .5 + .6 * rnd.NextDouble());
            }
        }
        g.Freeze();
        WoundCache[(w, c)] = g;
        return g;
    }

    /// <summary>
    /// A capsule's torn ends (the rare drop's pill, the end plate): at each end a jagged fringe of light with six teeth, a bloom
    /// of violet light round it and two still shards beside it, as if the capsule had been torn out of the glass. Inside its size.
    /// </summary>
    internal static void TornEnds(DrawingContext dc, Size s, Color c)
    {
        double h = s.Height;
        var lit = Mod.Mix(c, Colors.White, .4);
        foreach (var left in new[] { true, false })
        {
            double ex = left ? h * .32 : s.Width - h * .32, dir = left ? 1 : -1;
            dc.DrawRectangle(Radial(new Point(.5, .5), .5, .5, (Mod.A(Color.FromRgb(0xA8, 0x55, 0xF7), .6), 0), (Mod.A(Color.FromRgb(0xA8, 0x55, 0xF7), 0), 1)), null, new Rect(ex - 15, -2, 30, h + 4));
            var g = new StreamGeometry();
            using (var sc = g.Open())
            {
                sc.BeginFigure(new Point(ex, 2), true, true);
                for (int i = 0; i < 6; i++)
                {
                    double y0 = 2 + (h - 4) * i / 6, y1 = 2 + (h - 4) * (i + .5) / 6;
                    sc.LineTo(new Point(ex + dir * (3.5 + (i % 2) * 1.5), y1), true, false);
                    sc.LineTo(new Point(ex + dir * .6, 2 + (h - 4) * (i + 1) / 6), true, false);
                }
                sc.LineTo(new Point(ex - dir * 1.2, h - 2), true, false);
                sc.LineTo(new Point(ex - dir * 1.2, 2), true, false);
            }
            g.Freeze();
            dc.DrawGeometry(Mod.Grad(true, (Mod.A(lit, .95), 0), (Mod.A(c, .9), .5), (Mod.A(lit, .95), 1)), null, g);
            // two still shards beside it
            foreach (var (dx, dy, len) in new[] { (9.0, h * .3, 6.0), (12.0, h * .7, 4.5) })
            {
                var sh = new StreamGeometry();
                double x = ex + dir * dx, yy = dy;
                using (var sc = sh.Open())
                {
                    sc.BeginFigure(new Point(x, yy - len / 2), true, true);
                    sc.LineTo(new Point(x + 1.2, yy), true, false);
                    sc.LineTo(new Point(x, yy + len / 2), true, false);
                    sc.LineTo(new Point(x - 1.2, yy), true, false);
                }
                sh.Freeze();
                dc.DrawGeometry(Mod.Br(lit, .8), null, sh);
            }
        }
    }

    /// <summary>The deep field: fewer, larger, brighter stars, a layer behind the glass's own that drifts only during a moment (parallax).</summary>
    internal static readonly ImageBrush DeepStars = MakeDeepStars();

    static ImageBrush MakeDeepStars()
    {
        const int n = 220;
        var px = new byte[n * n * 4];
        var rnd = new Random(4421);
        void Put(int x, int y, Color c, double a)
        {
            x = (x % n + n) % n; y = (y % n + n) % n;
            int i = (y * n + x) * 4;
            byte al = (byte)Math.Clamp(Math.Round(a * 255), 0, 255);
            if (al <= px[i + 3]) return;
            px[i] = (byte)(c.B * al / 255); px[i + 1] = (byte)(c.G * al / 255); px[i + 2] = (byte)(c.R * al / 255); px[i + 3] = al;
        }
        for (int k = 0; k < 26; k++)
        {
            int x = rnd.Next(n), y = rnd.Next(n);
            var c = rnd.NextDouble() < .6 ? Colors.White : rnd.NextDouble() < .5 ? CyanC : LilacC;
            double a = .45 + .5 * rnd.NextDouble();
            Put(x, y, c, a);
            Put(x + 1, y, c, a * .5); Put(x - 1, y, c, a * .5); Put(x, y + 1, c, a * .5); Put(x, y - 1, c, a * .5);
            if (a > .8) { Put(x + 2, y, c, a * .2); Put(x - 2, y, c, a * .2); Put(x, y + 2, c, a * .2); Put(x, y - 2, c, a * .2); }
        }
        var bmp = BitmapSource.Create(n, n, 96, 96, PixelFormats.Pbgra32, null, px, n * 4);
        bmp.Freeze();
        var b = new ImageBrush(bmp) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, n, n), ViewportUnits = BrushMappingMode.Absolute, Stretch = Stretch.None };
        b.Freeze();
        return b;
    }

    /// <summary>A shard of light, drawn once at its own origin (pointing up), for the bursts to fling: white at its tip, cyan to violet at its foot.</summary>
    internal static TacSketch ShardOf(double len, double wid)
    {
        var geo = new StreamGeometry();
        using (var c = geo.Open())
        {
            c.BeginFigure(new Point(0, -len * .6), true, true);
            c.LineTo(new Point(wid / 2, len * .1), true, false);
            c.LineTo(new Point(0, len * .4), true, false);
            c.LineTo(new Point(-wid / 2, len * .1), true, false);
        }
        geo.Freeze();
        var fill = Mod.Grad(true, (Colors.White, 0), (LightC, .35), (CyanC, .7), (VioletC, 1));
        return new TacSketch((dc, _) =>
        {
            dc.DrawRectangle(Mod.Grad(true, (Mod.A(LightC, .75), 0), (Mod.A(VioletC, 0), 1)), null, new Rect(-.7, len * .3, 1.4, len * 1.6));
            dc.DrawRectangle(Spark, null, new Rect(-wid * 1.6, -len * .7, wid * 3.2, len * 1.2));
            dc.DrawGeometry(fill, null, geo);
        }) { IsHitTestVisible = false };
    }

    /// <summary>
    /// The fissure of rift light at a bar's leading edge (<paramref name="x"/>): a bloom inside the bar rising to it, light
    /// spilling past it into the dark, a cyan rim and a white core down a jagged line. <paramref name="k"/> its strength
    /// (the leader's the brightest, a flare above 1).
    /// </summary>
    internal static void Fissure(DrawingContext dc, double x, double top, double h, Color tone, double k, int seed = 0)
    {
        k = Math.Clamp(k, 0, 2.4);
        double bw = Math.Min(14, Math.Max(0, x - 1));
        // drawn at the edge from a drawing made once for its height, strength, colour and seed (a row's redraw allocates nothing)
        dc.PushTransform(new TranslateTransform(x, top));
        dc.DrawDrawing(FissureArt(Math.Round(h * 2) / 2, Math.Round(k * 20) / 20, tone, seed & 7, Math.Round(bw)));
        dc.Pop();
    }

    static readonly Dictionary<(double H, double K, Color Tone, int Seed, double Bw), Drawing> FissureCache = new();

    /// <summary>A fissure at x 0, y 0 (see <see cref="Fissure"/>), made once and frozen for its height, strength, colour, seed and the bloom's room behind it.</summary>
    static Drawing FissureArt(double h, double k, Color tone, int seed, double bw)
    {
        var key = (h, k, tone, seed, bw);
        if (FissureCache.TryGetValue(key, out var d)) return d;
        if (FissureCache.Count > 400) FissureCache.Clear();
        var g = new DrawingGroup();
        using (var dc = g.Open())
        {
            double k1 = Math.Min(1, k);
            var glow = Mod.Mix(VioletC, tone, .35);
            if (bw > 0) dc.DrawRectangle(Mod.Grad(false, (Mod.A(glow, 0), 0), (Mod.A(Mod.Mix(glow, Colors.White, .4), .7 * k1), 1)), null, new Rect(-bw, 0, bw, h));
            // the light spilling out past the edge into the dark, an ellipse of cyan going violet
            dc.PushOpacity(k1);
            dc.DrawRectangle(EdgeGlow, null, new Rect(-7 - 2 * k, -1, 14 + 4 * k, h + 2));
            dc.Pop();
            dc.DrawGeometry(Mod.Br(VioletC, .6 * k1), null, TearDown(-.5, 0, h, 4.2 + 1.2 * k, seed));
            dc.DrawGeometry(Mod.Br(CyanC, .9 * k1), null, TearDown(-.5, 0, h, 2.6 + .8 * k, seed));
            dc.DrawGeometry(Mod.Br(Colors.White, Math.Min(1, .25 + .65 * k)), null, TearDown(-.5, .5, h - 1, 1.3 + .35 * k, seed));
            if (k > 1)
            {
                // a flare: the light breaking out above and below the bar
                double f = k - 1;
                dc.PushOpacity(Math.Min(1, f));
                dc.DrawRectangle(Spark, null, new Rect(-6 - 4 * f, -3 - 2 * f, 12 + 8 * f, h + 6 + 4 * f));
                dc.Pop();
            }
        }
        g.Freeze();
        FissureCache[key] = g;
        return g;
    }

    /// <summary>
    /// A pulse of rift light running along a panel's tear, as a layer over the panel (clipped to its rounded shape): a point of
    /// light with a soft glow and a trail behind it, drawn once and only moved (a transform) and faded (opacity) as it runs,
    /// so the panel under it is never drawn again.
    /// </summary>
    internal sealed class RiftPulse : Canvas
    {
        readonly Canvas _spot = new() { IsHitTestVisible = false };
        readonly TranslateTransform _at = new();
        readonly ScaleTransform _trail = new(1, 1, 120, 0);
        readonly double _radius;
        Color? _tint = Colors.Transparent;

        public RiftPulse(double radius)
        {
            _radius = radius; IsHitTestVisible = false; ClipToBounds = true; Opacity = 0;
            _spot.RenderTransform = _at;
            _at.X = 124;
            Children.Add(_spot);
            Tint(null);
            SizeChanged += (_, _) =>
            {
                double r = Math.Min(_radius, ActualHeight / 2);
                var g = new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight), r, r); g.Freeze();
                Clip = g;
            };
        }

        /// <summary>The pulse's colour (null: the rift's own cyan light); made again only when it changes.</summary>
        public void Tint(Color? tint)
        {
            if (tint == _tint) return;
            _tint = tint;
            var c = tint ?? CyanC;
            _spot.Children.Clear();
            var trail = new System.Windows.Shapes.Rectangle { Width = 120, Height = 5, Fill = Mod.Grad(false, (Mod.A(c, 0), 0), (Mod.A(Mod.Mix(c, Colors.White, .4), .6), 1)) };
            Canvas.SetLeft(trail, -120); Canvas.SetTop(trail, 0);
            trail.RenderTransform = _trail;
            var glow = new System.Windows.Shapes.Rectangle { Width = 80, Height = 28, Fill = tint is { } t ? Radial(new Point(.5, .5), .5, .5, (Mod.A(Colors.White, 1), 0), (Mod.A(t, .6), .3), (Mod.A(t, 0), 1)) : PulseGlow };
            Canvas.SetLeft(glow, -40); Canvas.SetTop(glow, -12);
            var dot = new System.Windows.Shapes.Ellipse { Width = 4.8, Height = 3.2, Fill = Brushes.White };
            Canvas.SetLeft(dot, -2.4); Canvas.SetTop(dot, .6);
            _spot.Children.Add(trail); _spot.Children.Add(glow); _spot.Children.Add(dot);
        }

        /// <summary>The pulse at <paramref name="t"/> of its run along the tear (outside 0..1: gone).</summary>
        public void At(double t)
        {
            // (at rest the light waits where its trail stays inside the glass: nothing ever drawn past the window)
            if (t is <= 0 or >= 1) { Opacity = 0; _at.X = 124; _trail.ScaleX = 1; return; }
            double x0 = _radius * .35, x1 = ActualWidth - _radius * .35;
            _at.X = x0 + (x1 - x0) * Motion.Out(t);
            _trail.ScaleX = Math.Clamp((_at.X - 2) / 120, .01, 1);   // the trail never reaches back past the glass's left
            Opacity = Math.Min(1, 1.6 * Math.Sin(Math.PI * t));
        }
    }

    /// <summary>
    /// The crown: three crystal shards floating over a point (<paramref name="cx"/>, its base at <paramref name="by"/>), the
    /// middle one tall, the outer ones leaning out; white at their tips to the rift's violet at their feet, a dark edge so
    /// they hold over snow, a soft violet glow round them. <paramref name="glow"/> lifts the glow (a landing).
    /// </summary>
    internal static void Crown(DrawingContext dc, double cx, double by, double scale = 1, double glow = 0)
    {
        dc.PushOpacity(Math.Min(1, .75 + .25 * glow));
        dc.DrawRectangle(Radial(new Point(.5, .5), .5, .5, (Mod.A(VioletC, .7 + .3 * glow), 0), (Mod.A(VioletC, .25 + .2 * glow), .5), (Mod.A(VioletC, 0), 1)), null, new Rect(cx - 10 * scale, by - 12 * scale, 20 * scale, 14 * scale));
        dc.Pop();
        var fill = Mod.Grad(true, (Colors.White, 0), (LightC, .3), (CyanC, .6), (VioletC, 1));
        var edge = Mod.P(Mod.Br(Color.FromRgb(0x14, 0x05, 0x2E), .95), .9);
        void Shard(double x, double h, double w, double lean)
        {
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                double tx = x + lean * h, ty = by - h;
                c.BeginFigure(new Point(x - w / 2, by - h * .22), true, true);
                c.LineTo(new Point(tx, ty), true, false);
                c.LineTo(new Point(x + w / 2, by - h * .22), true, false);
                c.LineTo(new Point(x, by), true, false);
            }
            g.Freeze();
            dc.DrawGeometry(fill, edge, g);
        }
        Shard(cx - 4.6 * scale, 6.8 * scale, 3.6 * scale, -.34);
        Shard(cx + 4.6 * scale, 6.8 * scale, 3.6 * scale, .34);
        Shard(cx, 10 * scale, 4.4 * scale, 0);
        // the band the shards stand on: a slim arc of light
        var band = new StreamGeometry();
        using (var c = band.Open())
        {
            c.BeginFigure(new Point(cx - 7 * scale, by - .4 * scale), true, true);
            c.QuadraticBezierTo(new Point(cx, by + 1.6 * scale), new Point(cx + 7 * scale, by - .4 * scale), true, false);
            c.LineTo(new Point(cx + 6.4 * scale, by + .9 * scale), true, false);
            c.QuadraticBezierTo(new Point(cx, by + 2.9 * scale), new Point(cx - 6.4 * scale, by + .9 * scale), true, false);
        }
        band.Freeze();
        dc.DrawGeometry(Mod.Grad(false, (VioletC, 0), (LightC, .5), (VioletC, 1)), edge, band);
    }
}
