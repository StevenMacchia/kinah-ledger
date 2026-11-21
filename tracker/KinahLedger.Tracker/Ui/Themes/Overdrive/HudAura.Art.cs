using System.Windows;
using System.Windows.Media;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

/// <summary>
/// Aura's drawn parts: the dark core plates with slanted ends, the aura's flame tongues (vector paths, built once per size
/// and seeded per member, so a row's fire keeps its shape), the energy that fills a gauge (a soft body, a bright core line),
/// the static jagged lightning round the leader, and the burning ring. Every geometry and brush is frozen; a moment only
/// moves or fades the parts these draw.
/// </summary>
sealed partial class HudAura
{
    // the look's own colours: the energy's azure (the Buddy, the head), the leader's gold, the lightning, the boss's red energy
    static readonly Color KiC = Color.FromRgb(0x2F, 0x9B, 0xFF), KiHotC = Color.FromRgb(0xC8, 0xEC, 0xFF),
        AuGoldC = Color.FromRgb(0xFF, 0xC4, 0x2E), AuGoldHotC = Color.FromRgb(0xFF, 0xF4, 0xC8), BoltC = Color.FromRgb(0x9F, 0xDD, 0xFF),
        HpEnC = Color.FromRgb(0xFF, 0x2D, 0x55), HpHotC = Color.FromRgb(0xFF, 0xC9, 0xD3), CoreTopC = Color.FromRgb(0x10, 0x12, 0x1A), CoreBotC = Color.FromRgb(0x04, 0x05, 0x08),
        WipeC = Color.FromRgb(0xE0, 0x3A, 0x4A);

    static T Frz<T>(T f) where T : Freezable { f.Freeze(); return f; }

    /// <summary>A plate with both ends slanted (an energy cell): the top edge runs <paramref name="slant"/> further right than the foot.</summary>
    static Geometry Plate(Rect r, double slant)
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

    /// <summary>A vertical gradient in absolute units from <paramref name="y0"/> to <paramref name="y1"/>.</summary>
    static LinearGradientBrush VGrad(double y0, double y1, params (Color C, double At)[] stops)
    {
        var b = new LinearGradientBrush { MappingMode = BrushMappingMode.Absolute, StartPoint = new Point(0, y0), EndPoint = new Point(0, y1) };
        foreach (var (c, at) in stops) b.GradientStops.Add(new GradientStop(c, at));
        b.Freeze();
        return b;
    }

    static Pen Pn(Color c, double a, double w, bool round = true) => Frz(new Pen(Mod.Br(c, a), w) { LineJoin = round ? PenLineJoin.Round : PenLineJoin.Miter, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round });

    /// <summary>
    /// The dark core a row sits on: a near-black slanted plate, the aura's soft glow round it in <paramref name="tone"/>
    /// (<paramref name="glow"/> its strength), a rim lit along its top edge.
    /// </summary>
    static void CorePlate(DrawingContext dc, Rect r, double slant, Color tone, double glow, Color? rim = null, double rimA = .5, double coreA = .9)
    {
        var g = Plate(r, slant);
        if (glow > 0)
        {
            dc.DrawGeometry(null, Pn(tone, .09 * glow, 6), g);
            dc.DrawGeometry(null, Pn(tone, .26 * glow, 2.6), g);
        }
        dc.DrawGeometry(VGrad(r.Top, r.Bottom, (Mod.A(CoreTopC, coreA), 0), (Mod.A(CoreBotC, Math.Min(1, coreA + .04)), 1)), null, g);
        var rc = rim ?? Mod.Mix(tone, Colors.White, .25);
        dc.DrawGeometry(null, Pn(rc, rimA * .55, 1), g);
        // the lit top edge
        dc.DrawLine(Pn(Mod.Mix(rc, Colors.White, .3), Math.Min(1, rimA * 1.35), 1.1), new Point(r.Left + slant + 1, r.Top + .5), new Point(r.Right - 1, r.Top + .5));
    }

    /// <summary>
    /// A row of flame tongues standing on <paramref name="baseY"/> from <paramref name="x0"/> to <paramref name="x1"/>, the
    /// tallest <paramref name="maxH"/>: curved teardrops of mixed widths (about 0.55 to 1.45 of <paramref name="width"/>) and
    /// heights, leaning one way, every so often a tall one; seeded, so the same member's fire keeps its shape. They taper
    /// toward both ends.
    /// </summary>
    static Geometry Tongues(double x0, double x1, double baseY, double maxH, int seed, double width, double minK)
    {
        var rnd = new Random(seed);
        var g = new StreamGeometry { FillRule = FillRule.Nonzero };
        using (var c = g.Open())
        {
            double x = x0;
            int n = 0;
            while (x1 - x0 > 4)
            {
                double b = width * (.55 + .9 * rnd.NextDouble()) / 2;
                double cx = x + b * .7;
                if (cx > x1 - 2) break;
                bool hero = n++ % 5 == 2;
                double k = hero ? .9 + .1 * rnd.NextDouble() : minK + (1 - minK) * Math.Pow(rnd.NextDouble(), .8) * .85;
                double edge = Math.Clamp(Math.Min(cx - x0, x1 - cx) / 18, 0, 1);
                double t = maxH * k * (.4 + .6 * edge);
                x += b * (1.05 + .5 * rnd.NextDouble());
                if (t < 1.5) continue;
                double lean = (rnd.NextDouble() < .85 ? 1 : -1) * (.16 + .2 * rnd.NextDouble()) * t;
                var tip = new Point(cx + lean, baseY - t);
                c.BeginFigure(new Point(cx - b, baseY), true, true);
                c.BezierTo(new Point(cx - b * 1.12, baseY - t * .38), new Point(cx - b * .15 + lean * .45, baseY - t * .78), tip, true, true);
                c.BezierTo(new Point(cx + b * .4 + lean * .55, baseY - t * .6), new Point(cx + b * 1.15, baseY - t * .28), new Point(cx + b, baseY), true, true);
            }
        }
        g.Freeze();
        return g;
    }

    static readonly Color InkC = Color.FromRgb(0x0A, 0x0D, 0x14);

    /// <summary>
    /// The aura's fire: a dark ink outline round the whole silhouette (so it holds over a bright sky), the outer tongues in
    /// <paramref name="tone"/>, a brighter middle layer, and a white-hot core about 40% as tall.
    /// </summary>
    static void Flames(DrawingContext dc, double x0, double x1, double baseY, double maxH, int seed, Color tone, Color hot, double a = 1, double width = 15)
    {
        if (x1 - x0 < 4 || maxH < 1) return;
        // the fire's own colour: the tone saturated a quarter more, so a muted class colour still burns
        var fire = Saturate(tone, 1.25);
        var outer = Tongues(x0, x1, baseY, maxH, seed, width, .35);
        var rim = outer.GetOutlinedPathGeometry(.25, ToleranceType.Absolute);
        rim.Freeze();
        dc.DrawGeometry(null, Frz(new Pen(VGrad(baseY, baseY - maxH, (Mod.A(InkC, .75 * a), 0), (Mod.A(InkC, .4 * a), .6), (Mod.A(InkC, .08 * a), 1)), 2) { LineJoin = PenLineJoin.Round }), rim);
        dc.DrawGeometry(VGrad(baseY, baseY - maxH, (Mod.A(fire, .97 * a), 0), (Mod.A(Mod.Mix(fire, Colors.White, .62), .95 * a), .3), (Mod.A(fire, .7 * a), .62), (Mod.A(fire, 0), 1)), null, outer);
        var mid = Tongues(x0 + 2, x1 - 2, baseY, maxH * .7, seed * 7 + 3, width * .75, .35);
        var midC = Mod.Mix(tone, hot, .55);
        dc.DrawGeometry(VGrad(baseY, baseY - maxH * .7, (Mod.A(midC, .95 * a), 0), (Mod.A(midC, .85 * a), .6), (Mod.A(midC, .3 * a), 1)), null, mid);
        var core = Tongues(x0 + 3, x1 - 3, baseY, maxH * .5, seed * 31 + 7, width * .55, .5);
        dc.DrawGeometry(VGrad(baseY, baseY - maxH * .5, (Mod.A(Colors.White, a), 0), (Mod.A(Colors.White, .9 * a), .45), (Mod.A(hot, .35 * a), 1)), null, core);
    }

    /// <summary><paramref name="c"/> with its saturation times <paramref name="k"/> (its lightness kept).</summary>
    static Color Saturate(Color c, double k)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0, max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), l = (max + min) / 2;
        if (max - min < 1e-6) return c;
        double s = l > .5 ? (max - min) / (2 - max - min) : (max - min) / (max + min), h;
        if (max == r) h = (g - b) / (max - min) + (g < b ? 6 : 0); else if (max == g) h = (b - r) / (max - min) + 2; else h = (r - g) / (max - min) + 4;
        h /= 6; s = Math.Min(1, s * k);
        double q = l < .5 ? l * (1 + s) : l + s - l * s, p = 2 * l - q;
        double T(double t) { t = t < 0 ? t + 1 : t > 1 ? t - 1 : t; return t < 1 / 6.0 ? p + (q - p) * 6 * t : t < .5 ? q : t < 2 / 3.0 ? p + (q - p) * (2 / 3.0 - t) * 6 : p; }
        return Color.FromArgb(c.A, (byte)Math.Round(255 * T(h + 1 / 3.0)), (byte)Math.Round(255 * T(h)), (byte)Math.Round(255 * T(h - 1 / 3.0)));
    }

    /// <summary>A jagged bolt from <paramref name="a"/> to <paramref name="b"/> (seeded), with a short fork off its middle.</summary>
    static Geometry Bolt(Point a, Point b, int seed, double amp, int segs = 6, bool fork = true)
    {
        var rnd = new Random(seed);
        var g = new StreamGeometry();
        double dx = b.X - a.X, dy = b.Y - a.Y, len = Math.Max(1, Math.Sqrt(dx * dx + dy * dy)), nx = -dy / len, ny = dx / len;
        Point? mid = null;
        using (var c = g.Open())
        {
            c.BeginFigure(a, false, false);
            for (int i = 1; i <= segs; i++)
            {
                double t = (double)i / segs, o = i == segs ? 0 : (rnd.NextDouble() * 2 - 1) * amp;
                var p = new Point(a.X + dx * t + nx * o, a.Y + dy * t + ny * o);
                c.LineTo(p, true, false);
                if (i == segs / 2) mid = p;
            }
            if (fork && mid is { } m)
            {
                double side = rnd.NextDouble() < .5 ? -1 : 1;
                var f1 = new Point(m.X + dx * .18 + nx * amp * 1.6 * side, m.Y + dy * .18 + ny * amp * 1.6 * side);
                var f2 = new Point(f1.X + dx * .14 + nx * amp * .6 * side, f1.Y + dy * .14 + ny * amp * .6 * side);
                c.BeginFigure(m, false, false);
                c.LineTo(f1, true, false); c.LineTo(f2, true, false);
            }
        }
        g.Freeze();
        return g;
    }

    /// <summary>A bolt lit: a wide soft glow in <paramref name="glow"/>, a tighter one, and a white core.</summary>
    static void DrawBolt(DrawingContext dc, Geometry bolt, double a = 1, double core = 2.5, Color? glow = null)
    {
        var gc = glow ?? AuGoldC;
        dc.DrawGeometry(null, Pn(InkC, .45 * a, core + 3), bolt);
        dc.DrawGeometry(null, Pn(gc, .3 * a, core + 6), bolt);
        dc.DrawGeometry(null, Pn(gc, .85 * a, core + 2.5), bolt);
        dc.DrawGeometry(null, Pn(Colors.White, a, core), bolt);
    }

    /// <summary>
    /// A gauge of pure energy filling <paramref name="r"/> (clipped to the plate's shape): a soft body in <paramref name="tone"/>,
    /// lighter toward its foot, a faint plasma streak, and the bright core line along the foot with its glow.
    /// </summary>
    static void Energy(DrawingContext dc, Rect r, double slant, Color tone, double k = 1)
    {
        var g = Plate(r, slant);
        var deep = Mod.Mix(tone, Colors.Black, .4);
        dc.PushClip(g);
        dc.DrawRectangle(VGrad(r.Top, r.Bottom, (Mod.A(deep, .78 * k), 0), (Mod.A(Mod.Mix(tone, Colors.Black, .2), .7 * k), .6), (Mod.A(tone, .72 * k), 1)), null, r);
        // the bright top edge and its glow down into the body
        dc.DrawRectangle(VGrad(r.Top, r.Top + 6, (Mod.A(Mod.Mix(tone, Colors.White, .3), .45 * k), 0), (Mod.A(tone, 0), 1)), null, new Rect(r.X, r.Top, r.Width, 6));
        dc.DrawRectangle(Mod.Br(Mod.Mix(tone, Colors.White, .55), .95 * k), null, new Rect(r.X, r.Top, r.Width, 2));
        // the core line along the foot
        dc.DrawRectangle(Mod.Br(Mod.Mix(tone, Colors.White, .45), .8 * k), null, new Rect(r.X, r.Bottom - 2, r.Width, 1.2));
        dc.Pop();
    }

    /// <summary>The gauge's front: a bright upright core, its glow, and one tall tongue licking up off the plate's edge.</summary>
    static void Front(DrawingContext dc, Size s, double plateTop, double plateH, Color tone, double tongue)
    {
        double cx = s.Width / 2, cy = plateTop + plateH / 2;
        var glow = new RadialGradientBrush(Mod.A(Mod.Mix(tone, Colors.White, .35), .6), Mod.A(tone, 0)); glow.Freeze();
        dc.DrawEllipse(glow, null, new Point(cx, cy), s.Width / 2, plateH * .62);
        if (tongue > 0)
        {
            var t = Tongues(cx - 4.5, cx + 4.5, plateTop + 3, tongue, 11, 7, 1);
            dc.DrawGeometry(VGrad(plateTop + 3, plateTop + 3 - tongue, (Mod.A(Mod.Mix(tone, Colors.White, .55), .95), 0), (Mod.A(tone, .6), .55), (Mod.A(tone, 0), 1)), null, t);
        }
        dc.DrawRectangle(VGrad(plateTop, plateTop + plateH, (Mod.A(Colors.White, .55), 0), (Mod.A(Mod.Mix(tone, Colors.White, .75), 1), .75), (Mod.A(Colors.White, 1), 1)), null, new Rect(cx - 1, plateTop + 1, 2, plateH - 1.5));
    }

    /// <summary>A soft round burst of light (an orb, a flash): <paramref name="c"/> at its heart, gone at its edge.</summary>
    static void Orb(DrawingContext dc, Point at, double rx, double ry, Color c, double a = 1, Color? heart = null)
    {
        var b = new RadialGradientBrush();
        b.GradientStops.Add(new GradientStop(Mod.A(heart ?? Mod.Mix(c, Colors.White, .7), a), 0));
        b.GradientStops.Add(new GradientStop(Mod.A(c, .65 * a), .35));
        b.GradientStops.Add(new GradientStop(Mod.A(c, 0), 1));
        b.Freeze();
        dc.DrawEllipse(b, null, at, rx, ry);
    }

    /// <summary>
    /// The burning ring: a dark disc, a faint track, the filled arc (your best's share) in <paramref name="tone"/> with a white-hot
    /// core, and flame tongues standing out from the filled arc only (the aura is the ring's fill).
    /// </summary>
    static void AuraRing(DrawingContext dc, Size s, double shown, Color tone, Color hot, int seed)
    {
        double c = s.Width / 2, r = c - 12;
        var center = new Point(c, c);
        dc.DrawEllipse(Mod.Br(CoreBotC, .78), null, center, r + 1, r + 1);
        dc.DrawEllipse(null, Pn(Colors.White, .2, 3.2), center, r, r);
        shown = Math.Clamp(shown, 0, 1);
        if (shown < .004) return;
        // the flames out from the filled arc
        var rnd = new Random(seed);
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            double arc = 2 * Math.PI * shown;
            int n = Math.Max(1, (int)Math.Round(11 * shown));
            for (int i = 0; i < n; i++)
            {
                double a = arc * (i + .5) / n, half = .26 + .08 * rnd.NextDouble(), len = 8 + 4 * rnd.NextDouble(), lean = (rnd.NextDouble() - .2) * .3;
                Point P(double ang, double rad) => new(c + rad * Math.Sin(ang), c - rad * Math.Cos(ang));
                ctx.BeginFigure(P(a - half, r), true, true);
                ctx.BezierTo(P(a - half * .8, r + len * .45), P(a + lean - half * .25, r + len * .7), P(a + lean, r + len), true, true);
                ctx.BezierTo(P(a + lean + half * .25, r + len * .65), P(a + half * .9, r + len * .4), P(a + half, r), true, true);
            }
        }
        g.Freeze();
        var fb = new RadialGradientBrush { GradientOrigin = new Point(.5, .5), RadiusX = .5, RadiusY = .5 };
        double inner = r / c;
        fb.GradientStops.Add(new GradientStop(Mod.A(Mod.Mix(tone, Colors.White, .3), .95), inner));
        fb.GradientStops.Add(new GradientStop(Mod.A(tone, .6), inner + (1 - inner) * .45));
        fb.GradientStops.Add(new GradientStop(Mod.A(tone, 0), 1));
        fb.Freeze();
        dc.DrawGeometry(fb, null, g);
        if (shown >= .999)
        {
            dc.DrawEllipse(null, Pn(tone, .9, 4), center, r, r);
            dc.DrawEllipse(null, Pn(hot, 1, 1.6), center, r, r);
            return;
        }
        double sweep = 2 * Math.PI * shown;
        var fig = new PathFigure { StartPoint = new Point(c, c - r) };
        fig.Segments.Add(new ArcSegment(new Point(c + r * Math.Sin(sweep), c - r * Math.Cos(sweep)), new Size(r, r), 0, sweep > Math.PI, SweepDirection.Clockwise, true));
        var pg = new PathGeometry(); pg.Figures.Add(fig); pg.Freeze();
        dc.DrawGeometry(null, Pn(tone, .95, 4), pg);
        dc.DrawGeometry(null, Pn(hot, 1, 1.6), pg);
    }
}
