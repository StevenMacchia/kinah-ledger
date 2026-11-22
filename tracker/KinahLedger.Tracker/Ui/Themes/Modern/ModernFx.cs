using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace KinahLedger.Ui;

/// <summary>
/// The Modern looks' moment parts (2026-11-21, "take the animation to the next level while keeping it light"): a few shapes
/// drawn once when a moment starts (a band of light, a glint, a bloom, a ring, an outline, a comet of light) and moved only by
/// their transforms and opacity while it plays, on a layer of their own (<see cref="ModernLook.FxLayer"/>) that goes when it
/// ends. Nothing here redraws per frame, nothing is blurred, and no moment builds more than about a dozen parts.
/// </summary>
static class ModFx
{
    /// <summary>A quick start and a long, exact settle (exponential ease-out): the Swiss look's snap.</summary>
    public static double Expo(double p) => p >= 1 ? 1 : 1 - Math.Pow(2, -10 * Math.Clamp(p, 0, 1));

    /// <summary>Slow, then quick (cubic ease-in): an exit.</summary>
    public static double In(double p) { p = Math.Clamp(p, 0, 1); return p * p * p; }

    /// <summary>Smooth both ends (a light's travel).</summary>
    public static double InOut(double p) { p = Math.Clamp(p, 0, 1); return p < .5 ? 4 * p * p * p : 1 - Math.Pow(-2 * p + 2, 3) / 2; }

    /// <summary>0 up to 1 and back to 0 over the course.</summary>
    public static double Bell(double p) => p <= 0 || p >= 1 ? 0 : Math.Sin(Math.PI * p);

    /// <summary>In over <paramref name="a"/>..<paramref name="b"/>, held, out over <paramref name="c"/>..<paramref name="d"/>.</summary>
    public static double Life(double t, double a, double b, double c, double d) =>
        t < b ? Math.Clamp((t - a) / (b - a), 0, 1) : 1 - Math.Clamp((t - c) / (d - c), 0, 1);

    /// <summary>A damped shake's offset: <paramref name="cycles"/> swings dying out over the course, 0 at both ends.</summary>
    public static double Shake(double p, double amp, double cycles = 3) =>
        p <= 0 || p >= 1 ? 0 : amp * Math.Sin(p * cycles * 2 * Math.PI) * (1 - p) * (1 - p);

    /// <summary>A scale punch: from <paramref name="from"/> past 1 and back to rest (an overshoot of about <paramref name="over"/>).</summary>
    public static double Punch(double p, double from, double over = 1.7)
    {
        p = Math.Clamp(p, 0, 1) - 1;
        double b = p * p * ((over + 1) * p + over) + 1;
        return from + (1 - from) * b;
    }

    /// <summary>An element's box in <paramref name="to"/>'s coordinates (its transforms counted), or empty when it isn't laid out there.</summary>
    public static Rect In(FrameworkElement el, Visual to)
    {
        try { return el.ActualWidth > 0 && el.IsDescendantOf(to) ? el.TransformToVisual(to).TransformBounds(new Rect(el.RenderSize)) : Rect.Empty; }
        catch (InvalidOperationException) { return Rect.Empty; }
    }

    /// <summary>A part's transforms: scale and turn about its centre (or <paramref name="origin"/>), then a shift.</summary>
    public sealed class Xf
    {
        public readonly ScaleTransform S = new(1, 1);
        public readonly RotateTransform R = new(0);
        public readonly TranslateTransform T = new();
        public Xf(UIElement el, Point? origin = null)
        {
            var g = new TransformGroup();
            g.Children.Add(S); g.Children.Add(R); g.Children.Add(T);
            el.RenderTransformOrigin = origin ?? new Point(.5, .5);
            el.RenderTransform = g;
        }
        public double Scale { set { S.ScaleX = S.ScaleY = value; } }
    }

    /// <summary><paramref name="el"/> on the layer with its top-left at (<paramref name="x"/>, <paramref name="y"/>).</summary>
    public static T Put<T>(Canvas layer, T el, double x, double y) where T : UIElement
    {
        Canvas.SetLeft(el, x); Canvas.SetTop(el, y);
        layer.Children.Add(el);
        return el;
    }

    /// <summary><paramref name="el"/> (its Width and Height set) centred on <paramref name="c"/>.</summary>
    public static T Centre<T>(Canvas layer, T el, Point c) where T : FrameworkElement => Put(layer, el, c.X - el.Width / 2, c.Y - el.Height / 2);

    /// <summary>
    /// A band of light: <paramref name="w"/> wide at its core, <paramref name="h"/> tall, leaning <paramref name="slant"/> px
    /// over its height, soft at both sides, in <paramref name="c"/> at <paramref name="a"/> at its brightest; its box is
    /// w + slant wide. Drawn once.
    /// </summary>
    public static FrameworkElement Band(double w, double h, Color c, double a, double slant = 0)
    {
        var b = Mod.Grad(false, (Mod.A(c, 0), 0), (Mod.A(c, a * .55), .3), (Mod.A(c, a), .5), (Mod.A(c, a * .55), .7), (Mod.A(c, 0), 1));
        return new TacSketch((dc, s) =>
        {
            var g = new StreamGeometry();
            using (var x = g.Open())
            {
                x.BeginFigure(new Point(slant, 0), true, true);
                x.LineTo(new Point(slant + w, 0), true, false);
                x.LineTo(new Point(w, h), true, false);
                x.LineTo(new Point(0, h), true, false);
            }
            g.Freeze();
            dc.DrawGeometry(b, null, g);
        }, w + Math.Abs(slant), h);
    }

    /// <summary>A four-point glint (a lens's star): a soft core and two long thin rays, <paramref name="r"/> from its centre to a ray's tip.</summary>
    public static FrameworkElement Glint(Color c, double r, double a = 1)
    {
        var core = new RadialGradientBrush(Mod.A(Colors.White, a), Mod.A(c, 0)) { GradientOrigin = new Point(.5, .5) };
        core.GradientStops.Insert(1, new GradientStop(Mod.A(c, .55 * a), .35));
        core.Freeze();
        var ray = Mod.Br(Mod.Mix(c, Colors.White, .6), a);
        return new TacSketch((dc, s) =>
        {
            var m = new Point(r, r);
            dc.DrawEllipse(core, null, m, r * .42, r * .42);
            foreach (var (dx, dy) in new[] { (1.0, 0.0), (0.0, 1.0) })
            {
                double nx = -dy, ny = dx, wide = r * .1;
                var g = new StreamGeometry();
                using (var x = g.Open())
                {
                    x.BeginFigure(new Point(m.X - dx * r, m.Y - dy * r), true, true);
                    x.LineTo(new Point(m.X + nx * wide, m.Y + ny * wide), true, false);
                    x.LineTo(new Point(m.X + dx * r, m.Y + dy * r), true, false);
                    x.LineTo(new Point(m.X - nx * wide, m.Y - ny * wide), true, false);
                }
                g.Freeze();
                dc.DrawGeometry(ray, null, g);
            }
            dc.DrawEllipse(Mod.Br(Colors.White, a), null, m, r * .09, r * .09);
        }, 2 * r, 2 * r);
    }

    /// <summary>A soft round bloom of light: <paramref name="c"/> at <paramref name="a"/> in its middle, nothing at its edge.</summary>
    public static Ellipse Bloom(Color c, double w, double h, double a)
    {
        var b = new RadialGradientBrush { GradientOrigin = new Point(.5, .5) };
        b.GradientStops.Add(new GradientStop(Mod.A(c, a), 0));
        b.GradientStops.Add(new GradientStop(Mod.A(c, a * .45), .45));
        b.GradientStops.Add(new GradientStop(Mod.A(c, 0), 1));
        b.Freeze();
        return new Ellipse { Width = w, Height = h, Fill = b, IsHitTestVisible = false };
    }

    /// <summary>A soft ring of light (a donut of glow), for a halo going out.</summary>
    public static Ellipse Halo(Color c, double d, double a)
    {
        var b = new RadialGradientBrush { GradientOrigin = new Point(.5, .5) };
        b.GradientStops.Add(new GradientStop(Mod.A(c, 0), .55));
        b.GradientStops.Add(new GradientStop(Mod.A(c, a), .82));
        b.GradientStops.Add(new GradientStop(Mod.A(c, 0), 1));
        b.Freeze();
        return new Ellipse { Width = d, Height = d, Fill = b, IsHitTestVisible = false };
    }

    /// <summary>A thin ring (to burst out from a part).</summary>
    public static Ellipse Ring(Color c, double d, double th, double a = 1) =>
        new() { Width = d, Height = d, Stroke = Mod.Br(c, a), StrokeThickness = th, IsHitTestVisible = false };

    /// <summary>A rounded outline the size of <paramref name="r"/> (a frame's echo, a chip's ping).</summary>
    public static Border Outline(Rect r, double radius, Color c, double th, double a = 1, Brush? fill = null) =>
        new() { Width = r.Width, Height = r.Height, CornerRadius = new CornerRadius(radius), BorderBrush = Mod.Br(c, a), BorderThickness = new Thickness(th), Background = fill, IsHitTestVisible = false };

    /// <summary>A flat block (the Swiss look's wipes), its corners <paramref name="radius"/>.</summary>
    public static Border Block(double w, double h, Brush fill, double radius = 0) =>
        new() { Width = w, Height = h, Background = fill, CornerRadius = new CornerRadius(radius), IsHitTestVisible = false };

    /// <summary>
    /// The point at <paramref name="u"/> (0 to 1, wrapping) round a rounded rectangle's outline, clockwise from the start of
    /// its top edge.
    /// </summary>
    public static Point OnRound(Rect r, double rad, double u)
    {
        rad = Math.Max(0, Math.Min(rad, Math.Min(r.Width, r.Height) / 2));
        double w = r.Width - 2 * rad, h = r.Height - 2 * rad, arc = Math.PI * rad / 2;
        double total = 2 * w + 2 * h + 4 * arc;
        if (total <= 0) return r.TopLeft;
        double d = ((u % 1) + 1) % 1 * total;
        Point Arc(double cx, double cy, double a0, double f) { double a = a0 + f * Math.PI / 2; return new Point(cx + rad * Math.Cos(a), cy + rad * Math.Sin(a)); }
        if (d < w) return new Point(r.Left + rad + d, r.Top); d -= w;
        if (d < arc) return Arc(r.Right - rad, r.Top + rad, -Math.PI / 2, d / arc); d -= arc;
        if (d < h) return new Point(r.Right, r.Top + rad + d); d -= h;
        if (d < arc) return Arc(r.Right - rad, r.Bottom - rad, 0, d / arc); d -= arc;
        if (d < w) return new Point(r.Right - rad - d, r.Bottom); d -= w;
        if (d < arc) return Arc(r.Left + rad, r.Bottom - rad, Math.PI / 2, d / arc); d -= arc;
        if (d < h) return new Point(r.Left, r.Bottom - rad - d); d -= h;
        return Arc(r.Left + rad, r.Top + rad, Math.PI, Math.Min(1, d / arc));
    }

    /// <summary>The place (0 to 1, as <see cref="OnRound"/> counts) of the point on the top edge at <paramref name="x"/>.</summary>
    public static double TopAt(Rect r, double rad, double x)
    {
        rad = Math.Max(0, Math.Min(rad, Math.Min(r.Width, r.Height) / 2));
        double total = 2 * (r.Width - 2 * rad) + 2 * (r.Height - 2 * rad) + 2 * Math.PI * rad;
        return total <= 0 ? 0 : Math.Clamp(x - r.Left - rad, 0, r.Width - 2 * rad) / total;
    }

    /// <summary>
    /// A comet of light running along a hairline: a bright head with a soft glow and a short tail of fading points (five
    /// parts), placed each frame by their shifts only.
    /// </summary>
    public sealed class Comet
    {
        readonly List<(FrameworkElement El, TranslateTransform T, double Half, double A)> _parts = new();

        public Comet(Canvas layer, Color c, double size = 1)
        {
            void Add(FrameworkElement el, double a)
            {
                var t = new TranslateTransform();
                el.RenderTransform = t; el.Opacity = 0;
                Put(layer, el, 0, 0);
                _parts.Add((el, t, el.Width / 2, a));
            }
            Add(Bloom(c, 14 * size, 14 * size, .9), 1);
            Add(new Ellipse { Width = 3.6 * size, Height = 3.6 * size, Fill = Mod.Br(Mod.Mix(c, Colors.White, .75)) }, 1);
            for (int k = 1; k <= 3; k++)
            {
                double d = (3.2 - .55 * k) * size;
                Add(new Ellipse { Width = d, Height = d, Fill = Mod.Br(Mod.Mix(c, Colors.White, .35)) }, .75 - .2 * k);
            }
        }

        /// <param name="path">The point at a place on the line (0 to 1).</param>
        /// <param name="u">The head's place.</param>
        /// <param name="tail">How far back the tail reaches (in the line's 0 to 1).</param>
        public void At(Func<double, Point> path, double u, double tail, double opacity)
        {
            for (int i = 0; i < _parts.Count; i++)
            {
                var (el, t, half, a) = _parts[i];
                double back = i < 2 ? 0 : tail * (i - 1) / 3;
                var p = path(u - back);
                t.X = p.X - half; t.Y = p.Y - half;
                el.Opacity = Math.Clamp(opacity, 0, 1) * a;
            }
        }
    }
}
