using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Path = System.Windows.Shapes.Path;

namespace KinahLedger.Ui;

/// <summary>
/// The Elements looks' moment kit (Frost, Storm, Magma, Tide; "motion next", 2026-11-21): the easing of a beat (wind-up,
/// hit, settle), a damped shake, and a handful of short-lived bits (shards, sparks, embers, drops) that are shapes made once
/// and only moved, turned, scaled and faded by their transforms, then removed. Each moment builds its own layer on the look's
/// top canvas, plays it as its own course (so a second moment never strands the first's bits) and takes it away when done.
/// </summary>
static class ElemFx
{
    public static double Seg(double p, double a, double b) => Math.Clamp((p - a) / (b - a), 0, 1);
    public static double Out(double p) => Motion.Out(p);
    public static double In(double p) { p = Math.Clamp(p, 0, 1); return p * p; }
    public static double Back(double p, double s = 1.7) { p = Math.Clamp(p, 0, 1) - 1; return p * p * ((s + 1) * p + s) + 1; }

    /// <summary>A hit's light over [a, b]: a sharp rise to its peak at <paramref name="peak"/>, then a long soft fall.</summary>
    public static double Hit(double t, double a, double peak, double b) =>
        t <= a || t >= b ? 0 : t < peak ? Out(Seg(t, a, peak)) : Math.Pow(1 - Seg(t, peak, b), 2);

    /// <summary>A damped shake over [a, b]: <paramref name="amp"/> px, <paramref name="cycles"/> swings, dying away (0 outside).</summary>
    public static double Shake(double t, double a, double b, double amp, double cycles)
    {
        if (t <= a || t >= b) return 0;
        double x = Seg(t, a, b);
        return amp * Math.Sin(2 * Math.PI * cycles * x) * (1 - x) * (1 - x);
    }

    /// <summary>A punch of scale: from <paramref name="from"/> up past 1 and settling (Back), 1 outside [a, b].</summary>
    public static double Punch(double t, double a, double b, double from, double over = 2.4) => t <= a ? from : t >= b ? 1 : from + (1 - from) * Back(Seg(t, a, b), over);

    /// <summary>Where <paramref name="el"/>'s point (<paramref name="fx"/>, <paramref name="fy"/> of its size) sits on <paramref name="on"/>, once both are laid out.</summary>
    public static Point? At(FrameworkElement? el, Visual on, double fx = .5, double fy = .5)
    {
        if (el == null || !el.IsVisible || !el.IsArrangeValid) return null;
        try { return el.TranslatePoint(new Point(el.ActualWidth * fx, el.ActualHeight * fy), (UIElement)on); }
        catch (InvalidOperationException) { return null; }
    }

    static readonly Dictionary<(Color, int, int), Brush> Glows = new();

    /// <summary>A soft round glow of <paramref name="c"/>, strongest at its heart (made once per colour).</summary>
    public static Brush GlowBrush(Color c, double a = 1, double core = .0)
    {
        var key = (c, (int)(a * 100), (int)(core * 100));
        if (Glows.TryGetValue(key, out var b)) return b;
        var g = new RadialGradientBrush { GradientOrigin = new Point(.5, .5), Center = new Point(.5, .5), RadiusX = .5, RadiusY = .5 };
        g.GradientStops.Add(new GradientStop(Mod.A(Mod.Mix(c, Colors.White, core), a), 0));
        g.GradientStops.Add(new GradientStop(Mod.A(c, a * .55), .35));
        g.GradientStops.Add(new GradientStop(Mod.A(c, 0), 1));
        g.Freeze();
        return Glows[key] = g;
    }

    /// <summary>
    /// One moment's bits on a layer of its own: each a shape made once, flung from a point along its line (slowing as it
    /// goes), pulled down by its gravity, turning, growing or shrinking, faded in fast and out over its last half.
    /// </summary>
    public sealed class Bits
    {
        sealed record Bit(FrameworkElement El, TranslateTransform T, RotateTransform R, ScaleTransform S, Point From, Vector V, double G, double A0, double Spin, double Delay, double Life, double S0, double S1, double Peak, bool Face);
        readonly List<Bit> _bits = new();
        public readonly Canvas Layer = new() { IsHitTestVisible = false };
        Vector _shift;

        /// <summary>Moves every bit's start (the moment's point, found once laid out).</summary>
        public Vector Shift { get => _shift; set => _shift = value; }

        public int Count => _bits.Count;

        /// <summary>
        /// A bit: <paramref name="el"/> (centred on its point) from <paramref name="from"/> along <paramref name="v"/> (px over its
        /// life), <paramref name="g"/> px of fall by its end, turned <paramref name="angle"/>° and <paramref name="spin"/>° more
        /// over its life (<paramref name="face"/>: turned along its line), between <paramref name="delay"/> and
        /// delay + <paramref name="life"/> of the course, scaled <paramref name="s0"/> to <paramref name="s1"/>, at most <paramref name="peak"/> opaque.
        /// </summary>
        public void Add(FrameworkElement el, Point from, Vector v, double g = 0, double angle = 0, double spin = 0, double delay = 0, double life = 1, double s0 = 1, double s1 = 1, double peak = 1, bool face = false)
        {
            var t = new TranslateTransform(); var r = new RotateTransform(); var s = new ScaleTransform(1, 1);
            el.RenderTransformOrigin = new Point(.5, .5);
            el.RenderTransform = new TransformGroup { Children = { s, r, t } };
            el.IsHitTestVisible = false;
            el.Opacity = 0;
            Layer.Children.Add(el);
            _bits.Add(new Bit(el, t, r, s, from, v, g, face ? Math.Atan2(v.Y, v.X) * 180 / Math.PI + angle : angle, spin, delay, life, s0, s1, peak, face));
        }

        /// <summary>The bits at <paramref name="p"/> of the course (outside 0 to 1: none shown).</summary>
        public void Step(double p)
        {
            foreach (var b in _bits)
            {
                double l = (p - b.Delay) / Math.Max(.001, b.Life);
                if (p < 0 || l <= 0 || l >= 1) { if (b.El.Opacity != 0) b.El.Opacity = 0; continue; }
                double q = Out(l);
                double w = b.El.Width is var ww && double.IsFinite(ww) ? ww : 0, h = b.El.Height is var hh && double.IsFinite(hh) ? hh : 0;
                b.T.X = b.From.X + _shift.X + b.V.X * q - w / 2;
                b.T.Y = b.From.Y + _shift.Y + b.V.Y * q + b.G * l * l - h / 2;
                b.R.Angle = b.A0 + b.Spin * l;
                b.S.ScaleX = b.S.ScaleY = b.S0 + (b.S1 - b.S0) * l;
                b.El.Opacity = b.Peak * (l < .1 ? l / .1 : 1 - Seg(l, .5, 1));
            }
        }
    }

    /// <summary>
    /// Plays <paramref name="bits"/> on the look's top canvas over <paramref name="ms"/>, their points taken from
    /// <paramref name="anchor"/>'s point (<paramref name="fx"/>, <paramref name="fy"/>) once it is laid out (none: as given),
    /// <paramref name="also"/> stepped with them; then the layer is taken away.
    /// </summary>
    public static void Play(Canvas sky, Bits bits, FrameworkElement? anchor, double fx, double fy, double ms, Action<double>? also = null, Action? done = null)
    {
        sky.Children.Add(bits.Layer);
        bool found = anchor == null, laid = false;
        var nudge = bits.Shift;
        Motion.Play(bits.Layer, ms, t =>
        {
            // a part drawn anew just now (the moment's own redraw) is laid out once, here, before its point is taken
            if (!found && At(anchor, sky, fx, fy) is null && !laid) { laid = true; sky.UpdateLayout(); }
            if (!found && At(anchor, sky, fx, fy) is { } p) { bits.Shift = (Vector)p + nudge; found = true; }
            bits.Step(found ? t : -1);
            also?.Invoke(t);
        }, () => { bits.Step(-1); sky.Children.Remove(bits.Layer); done?.Invoke(); });
    }

    /// <summary>A shape made once: <paramref name="g"/> (frozen) in <paramref name="fill"/>, <paramref name="w"/> x <paramref name="h"/>.</summary>
    public static Path Shape(Geometry g, Brush fill, double w, double h, Pen? edge = null)
    {
        var p = new Path { Data = g, Fill = fill, Width = w, Height = h, Stretch = Stretch.Fill, IsHitTestVisible = false };
        if (edge != null) { p.Stroke = edge.Brush; p.StrokeThickness = edge.Thickness; }
        return p;
    }

    /// <summary>A round glow <paramref name="w"/> x <paramref name="h"/> of <paramref name="c"/>.</summary>
    public static Ellipse Glow(Color c, double w, double h, double a = 1, double core = .4) => new() { Width = w, Height = h, Fill = GlowBrush(c, a, core), IsHitTestVisible = false };

    /// <summary>A ring's outline <paramref name="w"/> x <paramref name="h"/>, <paramref name="th"/> px, in <paramref name="c"/> (a shockwave scaled out).</summary>
    public static Ellipse Ring(Color c, double w, double h, double th, double a = 1) => new() { Width = w, Height = h, Stroke = Frozen(Mod.A(c, a)), StrokeThickness = th, IsHitTestVisible = false };

    /// <summary>An echo of a chip's outline (<paramref name="w"/> x <paramref name="h"/>, corners <paramref name="radius"/>), scaled out and faded.</summary>
    public static Rectangle Outline(Color c, double w, double h, double th, double radius = 3, double a = 1) => new() { Width = w, Height = h, RadiusX = radius, RadiusY = radius, Stroke = Frozen(Mod.A(c, a)), StrokeThickness = th, IsHitTestVisible = false };

    static SolidColorBrush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

    static Geometry Fz(Geometry g) { g.Freeze(); return g; }

    /// <summary>A long thin splinter (a shard of ice, a spark), pointing right.</summary>
    public static readonly Geometry Splint = Fz(Geometry.Parse("M0,2 L5,0 L16,1.6 L5,4 Z"));
    /// <summary>A broken shard, uneven, pointing right.</summary>
    public static readonly Geometry ShardGeo = Fz(Geometry.Parse("M0,3 L4,0 L13,2 L9,6 L3,7 Z"));
    /// <summary>A four-point star (a glint).</summary>
    public static readonly Geometry Star4 = Fz(Geometry.Parse("M6,0 L7.2,4.8 L12,6 L7.2,7.2 L6,12 L4.8,7.2 L0,6 L4.8,4.8 Z"));
    /// <summary>A teardrop, its point up (a drop thrown up, an ember).</summary>
    public static readonly Geometry Drop = Fz(Geometry.Parse("M5,0 C6,4 10,7 10,10 C10,13 7.8,15 5,15 C2.2,15 0,13 0,10 C0,7 4,4 5,0 Z"));

    /// <summary>A bolt of lightning from the top of a <paramref name="w"/> x <paramref name="h"/> box to its foot, jagged by <paramref name="seed"/>, as one outline (filled).</summary>
    public static Geometry Bolt(double w, double h, int seed, double width = 2.2)
    {
        var rnd = new Random(seed);
        var pts = new List<Point> { new(w / 2, 0) };
        int n = 7;
        for (int i = 1; i < n; i++) pts.Add(new Point(w / 2 + (rnd.NextDouble() * 2 - 1) * w * .42, h * i / n + (rnd.NextDouble() - .5) * h / n * .5));
        pts.Add(new Point(w / 2, h));
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            // the stroke as a thin polygon, wider at the top
            c.BeginFigure(new Point(pts[0].X - width / 2, pts[0].Y), true, true);
            for (int i = 1; i < pts.Count; i++) c.LineTo(new Point(pts[i].X - width / 2 * (1 - .6 * i / pts.Count), pts[i].Y), true, false);
            for (int i = pts.Count - 1; i >= 0; i--) c.LineTo(new Point(pts[i].X + width / 2 * (1 - .6 * i / pts.Count), pts[i].Y), true, false);
        }
        g.Freeze();
        return g;
    }
}
