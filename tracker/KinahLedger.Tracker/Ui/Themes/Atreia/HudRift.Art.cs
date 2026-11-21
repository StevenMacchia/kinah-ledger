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

    internal static void RiftPanel(DrawingContext dc, Size s, double radius, double opacity, double pulse = -1, Color? tint = null, double flare = 0, bool shadow = true, double tear = 1)
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
    }

    /// <summary>
    /// The fissure of rift light at a bar's leading edge (<paramref name="x"/>): a bloom inside the bar rising to it, light
    /// spilling past it into the dark, a cyan rim and a white core down a jagged line. <paramref name="k"/> its strength
    /// (the leader's the brightest, a flare above 1).
    /// </summary>
    internal static void Fissure(DrawingContext dc, double x, double top, double h, Color tone, double k, int seed = 0)
    {
        k = Math.Clamp(k, 0, 2.4);
        double k1 = Math.Min(1, k);
        var glow = Mod.Mix(VioletC, tone, .35);
        double bw = Math.Min(14, Math.Max(0, x - 1));
        if (bw > 0) dc.DrawRectangle(Mod.Grad(false, (Mod.A(glow, 0), 0), (Mod.A(Mod.Mix(glow, Colors.White, .4), .7 * k1), 1)), null, new Rect(x - bw, top, bw, h));
        // the light spilling out past the edge into the dark, an ellipse of cyan going violet
        dc.PushOpacity(k1);
        dc.DrawRectangle(EdgeGlow, null, new Rect(x - 7 - 2 * k, top - 1, 14 + 4 * k, h + 2));
        dc.Pop();
        dc.DrawGeometry(Mod.Br(VioletC, .6 * k1), null, TearDown(x - .5, top, h, 4.2 + 1.2 * k, seed));
        dc.DrawGeometry(Mod.Br(CyanC, .9 * k1), null, TearDown(x - .5, top, h, 2.6 + .8 * k, seed));
        dc.DrawGeometry(Mod.Br(Colors.White, Math.Min(1, .25 + .65 * k)), null, TearDown(x - .5, top + .5, h - 1, 1.3 + .35 * k, seed));
        if (k > 1)
        {
            // a flare: the light breaking out above and below the bar for a moment
            double f = k - 1;
            dc.PushOpacity(Math.Min(1, f));
            dc.DrawRectangle(Spark, null, new Rect(x - 6 - 4 * f, top - 3 - 2 * f, 12 + 8 * f, h + 6 + 4 * f));
            dc.Pop();
        }
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
        readonly double _radius;
        Color? _tint = Colors.Transparent;

        public RiftPulse(double radius)
        {
            _radius = radius; IsHitTestVisible = false; ClipToBounds = true; Opacity = 0;
            _spot.RenderTransform = _at;
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
            var glow = new System.Windows.Shapes.Rectangle { Width = 80, Height = 28, Fill = tint is { } t ? Radial(new Point(.5, .5), .5, .5, (Mod.A(Colors.White, 1), 0), (Mod.A(t, .6), .3), (Mod.A(t, 0), 1)) : PulseGlow };
            Canvas.SetLeft(glow, -40); Canvas.SetTop(glow, -12);
            var dot = new System.Windows.Shapes.Ellipse { Width = 4.8, Height = 3.2, Fill = Brushes.White };
            Canvas.SetLeft(dot, -2.4); Canvas.SetTop(dot, .6);
            _spot.Children.Add(trail); _spot.Children.Add(glow); _spot.Children.Add(dot);
        }

        /// <summary>The pulse at <paramref name="t"/> of its run along the tear (outside 0..1: gone).</summary>
        public void At(double t)
        {
            if (t is <= 0 or >= 1) { Opacity = 0; return; }
            double x0 = _radius * .35, x1 = ActualWidth - _radius * .35;
            _at.X = x0 + (x1 - x0) * Motion.Out(t);
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
        Shard(cx - 4.4 * scale, 6.6 * scale, 3.4 * scale, -.32);
        Shard(cx + 4.4 * scale, 6.6 * scale, 3.4 * scale, .32);
        Shard(cx, 9.6 * scale, 4.2 * scale, 0);
    }
}
