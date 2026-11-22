using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Path = System.Windows.Shapes.Path;

namespace KinahLedger.Ui;

/// <summary>
/// The Atreia looks' moments (motion next, 2026-11-21; Steven: "cinematic and beautiful ... while keeping it light"): the
/// parts each look's moments are made of, in that look's own shapes and colours. Every moment is one course on a stage of its
/// own (a canvas over the look, clipped to the window, gone when the moment ends): a wind-up, a hit and a settle. Its parts
/// are built once when it starts (frozen shapes and brushes) and only moved, turned, scaled and faded; nothing is drawn again
/// while it plays, nothing at all between moments, and no shade or blur effect on any of it.
/// </summary>
static class AtreiaFx
{
    // ================================================================ easing

    public static double Seg(double p, double a, double b) => Math.Clamp((p - a) / (b - a), 0, 1);
    public static double Out(double p) => Motion.Out(p);
    static double InOut(double p) { p = Math.Clamp(p, 0, 1); return p * p * (3 - 2 * p); }

    /// <summary>A flash over [<paramref name="a"/>, <paramref name="b"/>]: quick up to its peak at <paramref name="peak"/>, eased down after; 0 outside.</summary>
    public static double Flash(double t, double a, double peak, double b) =>
        t <= a || t >= b ? 0 : t < peak ? Out(Seg(t, a, peak)) : 1 - InOut(Seg(t, peak, b));

    /// <summary>
    /// A punch's scale over [<paramref name="a"/>, <paramref name="b"/>]: a short wind-up down to 1 - <paramref name="dip"/>,
    /// a hit up to 1 + <paramref name="over"/>, then a damped spring that ends exactly on 1.
    /// </summary>
    public static double Punch(double t, double a, double b, double dip, double over)
    {
        double u = Seg(t, a, b);
        if (u <= 0 || u >= 1) return 1;
        if (u < .16) return 1 - dip * Out(u / .16);
        if (u < .36) return 1 - dip + (dip + over) * Out((u - .16) / .2);
        double v = (u - .36) / .64;
        return 1 + over * Math.Cos(Math.PI * 1.5 * v) * Math.Exp(-2.6 * v);
    }

    /// <summary>A decaying shake over [<paramref name="a"/>, <paramref name="b"/>]: <paramref name="amp"/> px at first, <paramref name="cycles"/> swings, 0 at its end.</summary>
    public static double Shake(double t, double a, double b, double amp, double cycles)
    {
        double u = Seg(t, a, b);
        return u <= 0 || u >= 1 ? 0 : amp * (1 - u) * (1 - u) * Math.Sin(2 * Math.PI * cycles * u);
    }

    // ================================================================ brushes and shapes (frozen)

    public static Color A(Color c, double a) => Color.FromArgb((byte)Math.Round(255 * Math.Clamp(a, 0, 1)), c.R, c.G, c.B);
    public static Color Mix(Color a, Color b, double t) => Color.FromArgb((byte)(a.A + (b.A - a.A) * t), (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
    public static SolidColorBrush Br(Color c, double a = 1) { var b = new SolidColorBrush(A(c, a * c.A / 255.0)); b.Freeze(); return b; }

    /// <summary>A soft round glow: <paramref name="c"/> at <paramref name="a"/> in its middle, gone at its rim.</summary>
    public static RadialGradientBrush Glow(Color c, double a, Color? core = null)
    {
        // a hot core falling off fast (light, not a disc)
        var b = new RadialGradientBrush();
        b.GradientStops.Add(new GradientStop(A(core ?? c, a), 0));
        b.GradientStops.Add(new GradientStop(A(core ?? c, a * .7), .08));
        b.GradientStops.Add(new GradientStop(A(c, a * .3), .25));
        b.GradientStops.Add(new GradientStop(A(c, a * .08), .6));
        b.GradientStops.Add(new GradientStop(A(c, 0), 1));
        b.Freeze();
        return b;
    }

    static Geometry G(string path) { var g = Geometry.Parse(path); g.Freeze(); return g; }

    /// <summary>A cut gem's chip: a long diamond along +x, about 2 by 1, round 0,0.</summary>
    public static readonly Geometry Diamond = G("M -1,0 L -0.2,-0.42 L 1,0 L -0.2,0.42 Z");
    /// <summary>A shard of glass along +x: a thin uneven splinter.</summary>
    public static readonly Geometry Shard = G("M -1,-0.1 L 0.15,-0.32 L 1,0.02 L -0.35,0.26 Z");
    /// <summary>A four-point star of light.</summary>
    public static readonly Geometry Star = G("M 0,-1 L 0.16,-0.16 L 1,0 L 0.16,0.16 L 0,1 L -0.16,0.16 L -1,0 L -0.16,-0.16 Z");
    /// <summary>A tongue of flame (an ember) pointing along +x.</summary>
    public static readonly Geometry Flame = G("M 1,0 C 0.3,-0.12 -0.2,-0.42 -0.7,-0.32 C -1.05,-0.22 -1.05,0.22 -0.7,0.32 C -0.2,0.42 0.3,0.12 1,0 Z");
    /// <summary>A feather along +x: a long vane with its quill.</summary>
    public static readonly Geometry Feather = G("M 1,0 C 0.55,-0.36 -0.35,-0.36 -0.8,-0.06 L -1,0 L -0.8,0.06 C -0.35,0.3 0.55,0.3 1,0 Z");
    /// <summary>A round mote.</summary>
    public static readonly Geometry Dot = G("M -1,0 A 1,1 0 1 1 1,0 A 1,1 0 1 1 -1,0 Z");

    // ================================================================ the stage

    /// <summary>
    /// A stage for one moment: a canvas of its own on the look's effect layer <paramref name="fx"/> (a canvas laid over the
    /// look's root), clipped to the window (the root and the room round it), so nothing a moment flings ever leaves it.
    /// </summary>
    public static Canvas Stage(Canvas fx, FrameworkElement root)
    {
        var m = root.Margin;
        var clip = new RectangleGeometry(new Rect(-m.Left, -m.Top, Math.Max(1, root.ActualWidth + m.Left + m.Right), Math.Max(1, root.ActualHeight + m.Top + m.Bottom)));
        clip.Freeze();
        var st = new Canvas { IsHitTestVisible = false, Clip = clip };
        fx.Children.Add(st);
        return st;
    }

    /// <summary>Where <paramref name="p"/> of <paramref name="el"/> stands on <paramref name="fx"/>, or null when it isn't laid out under it.</summary>
    public static Point? At(FrameworkElement? el, Canvas fx, Point p)
    {
        if (el == null || el.ActualWidth <= 0 || !el.IsVisible || !el.IsDescendantOf(fx.Parent as Visual ?? fx)) return null;
        try { return el.TranslatePoint(p, fx); } catch (InvalidOperationException) { return null; }
    }

    /// <summary>The box of <paramref name="el"/> on <paramref name="fx"/>, or null.</summary>
    public static Rect? Box(FrameworkElement? el, Canvas fx) =>
        At(el, fx, new Point(0, 0)) is { } p ? new Rect(p, new Size(el!.ActualWidth, el.ActualHeight)) : null;

    /// <summary>Plays one moment on its own stage and takes the stage away at its end (or at once, when nothing was put on it).</summary>
    public static void Play(Canvas fx, Canvas stage, double ms, Action<double> step, Action? done = null)
    {
        Motion.Play(stage, ms, step, () => { fx.Children.Remove(stage); done?.Invoke(); });
    }

    /// <summary>Every part of a moment at <paramref name="t"/>.</summary>
    public static void All(List<Action<double>> parts, double t) { foreach (var p in parts) p(t); }

    // ================================================================ the parts

    /// <summary>
    /// Bits flung by a moment (shards, embers, feathers, motes): each a frozen shape moved by one matrix and faded, with its
    /// own start, flight, fall, spin, sway and life inside the course. About a dozen at most a moment.
    /// </summary>
    public sealed class Bits
    {
        readonly Canvas _host;
        readonly List<(Path P, MatrixTransform M, Point O, Vector V, double Fall, double R0, double Spin, double S0, double S1, double D, double L, double Sway)> _b = new();
        /// <summary>The room the bits fly in: the stage's clip less 6 px each way (a bit never reaches the window's edge).</summary>
        readonly Rect _room;
        public Bits(Canvas host)
        {
            _host = host;
            var r = host.Clip?.Bounds ?? Rect.Empty;
            _room = r.IsEmpty || r.Width < 20 || r.Height < 20 ? Rect.Empty : new Rect(r.X + 6, r.Y + 6, r.Width - 12, r.Height - 12);
        }

        /// <summary>One bit: from <paramref name="o"/> along <paramref name="v"/> (eased out), falling <paramref name="fall"/> px by its end, sized <paramref name="s0"/> to <paramref name="s1"/>, alive from <paramref name="delay"/> for <paramref name="life"/> of the course.</summary>
        public void Add(Geometry g, Brush fill, Point o, Vector v, double fall = 0, double rot0 = 0, double spin = 0, double s0 = 4, double s1 = 4, double delay = 0, double life = 1, double sway = 0)
        {
            var m = new MatrixTransform();
            var p = new Path { Data = g, Fill = fill, RenderTransform = m, Opacity = 0, IsHitTestVisible = false };
            _host.Children.Add(p);
            _b.Add((p, m, o, v, fall, rot0, spin, s0, s1, delay, life, sway));
        }

        /// <summary>
        /// A burst of <paramref name="n"/> bits out of <paramref name="o"/>: spread round <paramref name="dir"/> degrees by
        /// <paramref name="spread"/>, from <paramref name="r0"/> px out to about <paramref name="dist"/>, each turned along its flight.
        /// </summary>
        public void Burst(int n, Geometry g, Func<int, Brush> fill, Point o, double dist, double size, double delay, double life,
            double fall = 0, double dir = 0, double spread = 360, double r0 = 0, double spin = 0, int seed = 7, double sway = 0, double squash = 1)
        {
            var rnd = new Random(seed);
            for (int i = 0; i < n; i++)
            {
                double a = (dir + (spread >= 360 ? 360.0 * (i + .5 * rnd.NextDouble()) / n : -spread / 2 + spread * (n == 1 ? .5 : (i + .3 * (rnd.NextDouble() - .5)) / (n - 1)))) * Math.PI / 180;
                var u = new Vector(Math.Cos(a), Math.Sin(a) * squash);
                double d = dist * (.7 + .5 * rnd.NextDouble()), s = size * (.7 + .6 * rnd.NextDouble());
                Add(g, fill(i), o + u * r0, u * d, fall * (.7 + .6 * rnd.NextDouble()), a * 180 / Math.PI, spin * (rnd.NextDouble() - .5) * 2,
                    s, s * .55, delay + .06 * rnd.NextDouble(), life * (.85 + .15 * rnd.NextDouble()), sway);
            }
        }

        public void At(double t)
        {
            foreach (var b in _b)
            {
                double u = (t - b.D) / b.L;
                if (u <= 0 || u >= 1) { if (b.P.Opacity != 0) b.P.Opacity = 0; continue; }
                double k = Out(u);
                var at = b.O + b.V * k + new Vector(b.Sway * Math.Sin(2 * Math.PI * 1.5 * u), b.Fall * u * u);
                double s = b.S0 + (b.S1 - b.S0) * u;
                if (!_room.IsEmpty)
                {
                    // kept inside the room by its size: a bit that would reach the edge slides along it instead
                    double e = s;
                    at = new Point(Math.Clamp(at.X, _room.Left + e, Math.Max(_room.Left + e, _room.Right - e)), Math.Clamp(at.Y, _room.Top + e, Math.Max(_room.Top + e, _room.Bottom - e)));
                }
                var m = Matrix.Identity;
                m.Scale(s, s);
                m.Rotate(b.R0 + b.Spin * u + (b.Sway != 0 ? 18 * Math.Sin(2 * Math.PI * 1.5 * u) : 0));
                m.Translate(at.X, at.Y);
                b.M.Matrix = m;
                b.P.Opacity = u < .1 ? u / .1 : 1 - InOut(Seg(u, .5, 1));
            }
        }
    }

    /// <summary>A ring of light (an ellipse <paramref name="w"/> by <paramref name="h"/>, round <paramref name="c"/>) that bursts out from <paramref name="s0"/> to <paramref name="s1"/> of its size and fades over [<paramref name="a"/>, <paramref name="b"/>].</summary>
    public static Action<double> Ring(Canvas st, Point c, double w, double h, Brush stroke, double th, double s0, double s1, double a, double b, double peak = 1)
    {
        var sc = new ScaleTransform(s0, s0, w / 2, h / 2);
        var e = new Ellipse { Width = w, Height = h, Stroke = stroke, StrokeThickness = th, Opacity = 0, RenderTransform = sc, IsHitTestVisible = false };
        Canvas.SetLeft(e, c.X - w / 2); Canvas.SetTop(e, c.Y - h / 2);
        st.Children.Add(e);
        return t =>
        {
            double u = Seg(t, a, b);
            sc.ScaleX = sc.ScaleY = s0 + (s1 - s0) * Out(u);
            e.Opacity = u is > 0 and < 1 ? peak * (u < .08 ? u / .08 : 1 - u) : 0;
        };
    }

    /// <summary>The outline of <paramref name="r"/> (its corners <paramref name="radius"/>) rippling out <paramref name="grow"/> px each way and fading over [<paramref name="a"/>, <paramref name="b"/>].</summary>
    public static Action<double> Ripple(Canvas st, Rect r, double radius, Brush stroke, double th, double grow, double a, double b, double peak = 1)
    {
        var sc = new ScaleTransform(1, 1, r.Width / 2, r.Height / 2);
        var e = new Rectangle { Width = r.Width, Height = r.Height, RadiusX = radius, RadiusY = radius, Stroke = stroke, StrokeThickness = th, Opacity = 0, RenderTransform = sc, IsHitTestVisible = false };
        Canvas.SetLeft(e, r.X); Canvas.SetTop(e, r.Y);
        st.Children.Add(e);
        return t =>
        {
            double u = Seg(t, a, b), k = Out(u);
            sc.ScaleX = 1 + 2 * grow * k / Math.Max(1, r.Width); sc.ScaleY = 1 + 2 * grow * k / Math.Max(1, r.Height);
            e.Opacity = u is > 0 and < 1 ? peak * (u < .1 ? u / .1 : 1 - u) : 0;
        };
    }

    /// <summary>A soft glow <paramref name="w"/> by <paramref name="h"/> round <paramref name="c"/> that swells from <paramref name="s0"/> and flashes over [<paramref name="a"/>, <paramref name="b"/>], at its brightest at <paramref name="peak"/>.</summary>
    public static Action<double> Bloom(Canvas st, Point c, double w, double h, Brush fill, double a, double peak, double b, double s0 = .4, double max = 1)
    {
        var sc = new ScaleTransform(s0, s0, w / 2, h / 2);
        var e = new Ellipse { Width = w, Height = h, Fill = fill, Opacity = 0, RenderTransform = sc, IsHitTestVisible = false };
        Canvas.SetLeft(e, c.X - w / 2); Canvas.SetTop(e, c.Y - h / 2);
        st.Children.Add(e);
        return t =>
        {
            sc.ScaleX = sc.ScaleY = s0 + (1 - s0) * Out(Seg(t, a, peak + (peak - a)));
            e.Opacity = max * Flash(t, a, peak, b);
        };
    }

    /// <summary>
    /// A burst of rays round <paramref name="c"/> (drawn once: <paramref name="n"/> thin wedges from <paramref name="r0"/> to
    /// <paramref name="r1"/>, each fading to its tip in its own colour), swelling from half its size, turning
    /// <paramref name="turn"/> degrees and flashing over [<paramref name="a"/>, <paramref name="b"/>].
    /// </summary>
    public static Action<double> Rays(Canvas st, Point c, int n, double r0, double r1, double width, Func<int, Color> color, double a, double peak, double b, double turn = 20, double max = .9, double s0 = .55)
    {
        var dg = new DrawingGroup();
        using (var dc = dg.Open())
        {
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(-r1, -r1, 2 * r1, 2 * r1));
            for (int i = 0; i < n; i++)
            {
                double ang = 2 * Math.PI * i / n + (i % 2) * .08, len = i % 2 == 0 ? r1 : r1 * .72;
                var u = new Vector(Math.Cos(ang), Math.Sin(ang)); var side = new Vector(-u.Y, u.X);
                var from = new Point(u.X * r0, u.Y * r0); var to = new Point(u.X * len, u.Y * len);
                var col = color(i);
                var br = new LinearGradientBrush { MappingMode = BrushMappingMode.Absolute, StartPoint = from, EndPoint = to };
                br.GradientStops.Add(new GradientStop(A(col, .95), 0)); br.GradientStops.Add(new GradientStop(A(col, 0), 1)); br.Freeze();
                var g = new StreamGeometry();
                using (var x = g.Open()) { x.BeginFigure(from + side * (width / 2), true, true); x.LineTo(to, true, false); x.LineTo(from - side * (width / 2), true, false); }
                g.Freeze();
                dc.DrawGeometry(br, null, g);
            }
        }
        dg.Freeze();
        var img = new DrawingImage(dg); img.Freeze();
        var sc = new ScaleTransform(s0, s0, r1, r1); var rot = new RotateTransform(0, r1, r1);
        var el = new Image { Source = img, Width = 2 * r1, Height = 2 * r1, Opacity = 0, IsHitTestVisible = false, RenderTransform = new TransformGroup { Children = { sc, rot } } };
        Canvas.SetLeft(el, c.X - r1); Canvas.SetTop(el, c.Y - r1);
        st.Children.Add(el);
        return t =>
        {
            double u = Seg(t, a, b);
            sc.ScaleX = sc.ScaleY = s0 + (1 - s0) * Out(Seg(t, a, peak + (peak - a)));
            rot.Angle = turn * Out(u);
            el.Opacity = max * Flash(t, a, peak, b);
        };
    }

    /// <summary>
    /// A pillar of light <paramref name="w"/> wide falling onto <paramref name="foot"/> from <paramref name="h"/> above it in
    /// <paramref name="c"/> (a white core): it drops in over [<paramref name="a"/>, <paramref name="hit"/>], then narrows and fades by <paramref name="b"/>.
    /// </summary>
    public static Action<double> Pillar(Canvas st, Point foot, double w, double h, Color c, double a, double hit, double b)
    {
        var halo = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        halo.GradientStops.Add(new GradientStop(A(c, 0), 0)); halo.GradientStops.Add(new GradientStop(A(c, .55), .3)); halo.GradientStops.Add(new GradientStop(A(Mix(c, Colors.White, .7), .95), .5));
        halo.GradientStops.Add(new GradientStop(A(c, .55), .7)); halo.GradientStops.Add(new GradientStop(A(c, 0), 1)); halo.Freeze();
        var fade = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        fade.GradientStops.Add(new GradientStop(Colors.Transparent, 0)); fade.GradientStops.Add(new GradientStop(Colors.Black, .45)); fade.GradientStops.Add(new GradientStop(Colors.Black, 1)); fade.Freeze();
        var sc = new ScaleTransform(1, 0, w / 2, 0);
        var r = new Rectangle { Width = w, Height = h, Fill = halo, OpacityMask = fade, Opacity = 0, RenderTransform = sc, IsHitTestVisible = false };
        Canvas.SetLeft(r, foot.X - w / 2); Canvas.SetTop(r, foot.Y - h);
        st.Children.Add(r);
        return t =>
        {
            sc.ScaleY = Out(Seg(t, a, hit));
            sc.ScaleX = 1 - .75 * InOut(Seg(t, hit, b));
            r.Opacity = t <= a || t >= b ? 0 : t < hit ? 1 : 1 - InOut(Seg(t, hit + (b - hit) * .25, b));
        };
    }

    /// <summary>A veil over <paramref name="r"/>: clear in its middle, <paramref name="c"/> at its edges (a vignette), at most <paramref name="max"/>, over [<paramref name="a"/>, <paramref name="b"/>] peaking at <paramref name="peak"/>.</summary>
    public static Action<double> Veil(Canvas st, Rect r, Color c, double max, double a, double peak, double b)
    {
        var br = new RadialGradientBrush { RadiusX = .62, RadiusY = .75 };
        br.GradientStops.Add(new GradientStop(A(c, .12), 0)); br.GradientStops.Add(new GradientStop(A(c, .3), .55)); br.GradientStops.Add(new GradientStop(A(c, .95), 1)); br.Freeze();
        var e = new Rectangle { Width = r.Width, Height = r.Height, Fill = br, Opacity = 0, IsHitTestVisible = false, RadiusX = 4, RadiusY = 4 };
        Canvas.SetLeft(e, r.X); Canvas.SetTop(e, r.Y);
        st.Children.Add(e);
        return t => e.Opacity = max * Flash(t, a, peak, b);
    }

    /// <summary>A jagged crack across <paramref name="r"/> (drawn once from <paramref name="seed"/>) that runs from its left over [<paramref name="a"/>, <paramref name="run"/>] and fades by <paramref name="b"/>.</summary>
    public static Action<double> Crack(Canvas st, Rect r, Color c, int seed, double a, double run, double b)
    {
        var rnd = new Random(seed);
        var g = new StreamGeometry();
        double y = r.Height * (.35 + .2 * rnd.NextDouble());
        using (var x = g.Open())
        {
            x.BeginFigure(new Point(0, y), false, false);
            double px = 0;
            while (px < r.Width)
            {
                px = Math.Min(r.Width, px + 10 + 18 * rnd.NextDouble());
                y = Math.Clamp(y + (rnd.NextDouble() - .5) * 22, 6, r.Height - 6);
                x.LineTo(new Point(px, y), true, true);
                if (rnd.NextDouble() < .35)
                {
                    // a short branch off the main crack
                    var bp = new Point(px + 6 + 8 * rnd.NextDouble(), Math.Clamp(y + (rnd.NextDouble() < .5 ? -1 : 1) * (8 + 8 * rnd.NextDouble()), 2, r.Height - 2));
                    x.LineTo(bp, true, true); x.LineTo(new Point(px, y), false, true);
                }
            }
        }
        g.Freeze();
        var mask = new RectangleGeometry(new Rect(0, -4, 0, r.Height + 8));
        var glow = new Path { Data = g, Stroke = Br(c, .55), StrokeThickness = 4, StrokeLineJoin = PenLineJoin.Bevel, IsHitTestVisible = false };
        var core = new Path { Data = g, Stroke = Br(Mix(c, Colors.White, .65)), StrokeThickness = 1.3, StrokeLineJoin = PenLineJoin.Bevel, IsHitTestVisible = false };
        var host = new Canvas { Opacity = 0, Clip = mask, IsHitTestVisible = false };
        host.Children.Add(glow); host.Children.Add(core);
        Canvas.SetLeft(host, r.X); Canvas.SetTop(host, r.Y);
        st.Children.Add(host);
        return t =>
        {
            mask.Rect = new Rect(0, -4, r.Width * Out(Seg(t, a, run)), r.Height + 8);
            host.Opacity = t <= a || t >= b ? 0 : t < run ? 1 : 1 - InOut(Seg(t, run, b));
        };
    }

    /// <summary>
    /// A streak of light across <paramref name="c"/> (a lens's flare line): <paramref name="len"/> long, <paramref name="th"/>
    /// thick, white in its middle and <paramref name="edge"/> along it; it shoots out from its middle over
    /// [<paramref name="a"/>, <paramref name="peak"/>] and fades by <paramref name="b"/>.
    /// </summary>
    public static Action<double> Streak(Canvas st, Point c, double len, double th, Color edge, double a, double peak, double b, double max = 1)
    {
        var br = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        br.GradientStops.Add(new GradientStop(A(edge, 0), 0)); br.GradientStops.Add(new GradientStop(A(edge, .8), .3));
        br.GradientStops.Add(new GradientStop(A(Colors.White, 1), .5)); br.GradientStops.Add(new GradientStop(A(edge, .8), .7)); br.GradientStops.Add(new GradientStop(A(edge, 0), 1)); br.Freeze();
        var sc = new ScaleTransform(0, 1, len / 2, th / 2);
        var e = new Ellipse { Width = len, Height = th, Fill = br, Opacity = 0, RenderTransform = sc, IsHitTestVisible = false };
        Canvas.SetLeft(e, c.X - len / 2); Canvas.SetTop(e, c.Y - th / 2);
        st.Children.Add(e);
        return t =>
        {
            sc.ScaleX = Out(Seg(t, a, peak + (peak - a) * 1.5));
            sc.ScaleY = 1 - .6 * Seg(t, peak, b);
            e.Opacity = max * Flash(t, a, peak, b);
        };
    }

    /// <summary>
    /// A star of cracks out of <paramref name="c"/> (glass struck there; drawn once from <paramref name="seed"/>): <paramref name="n"/>
    /// jagged arms up to <paramref name="len"/> long (squashed to <paramref name="squash"/> of it upright) and a broken ring joining
    /// them near the middle; it spreads from the point over [<paramref name="a"/>, <paramref name="run"/>] and fades by <paramref name="b"/>.
    /// </summary>
    public static Action<double> Shatter(Canvas st, Point c, int n, double len, Color col, int seed, double a, double run, double b, double squash = .6, double wobble = .35, int segs = 4, bool ring = true)
    {
        var rnd = new Random(seed);
        var g = new StreamGeometry();
        var mids = new List<Point>();
        using (var x = g.Open())
        {
            for (int i = 0; i < n; i++)
            {
                double ang = 2 * Math.PI * (i + .35 * rnd.NextDouble()) / n, l = len * (.55 + .45 * rnd.NextDouble());
                var p = new Point(0, 0);
                x.BeginFigure(p, false, false);
                for (int k = 1; k <= segs; k++)
                {
                    double r = l * k / segs, wob = (rnd.NextDouble() - .5) * wobble;
                    p = new Point(Math.Cos(ang + wob) * r, Math.Sin(ang + wob) * r * squash);
                    x.LineTo(p, true, true);
                    if (k == Math.Max(1, segs / 2)) mids.Add(p);
                }
            }
            // the broken ring between the arms, near the middle
            for (int i = 0; i < mids.Count; i++)
                if (ring && rnd.NextDouble() < .7) { x.BeginFigure(mids[i], false, false); x.LineTo(mids[(i + 1) % mids.Count], true, true); }
        }
        g.Freeze();
        var sc = new ScaleTransform(.2, .2);
        var host = new Canvas { Opacity = 0, IsHitTestVisible = false, RenderTransform = sc };
        host.Children.Add(new Path { Data = g, Stroke = Br(col, .5), StrokeThickness = 3.4, StrokeLineJoin = PenLineJoin.Bevel, IsHitTestVisible = false });
        host.Children.Add(new Path { Data = g, Stroke = Br(Mix(col, Colors.White, .7)), StrokeThickness = 1.1, StrokeLineJoin = PenLineJoin.Bevel, IsHitTestVisible = false });
        Canvas.SetLeft(host, c.X); Canvas.SetTop(host, c.Y);
        st.Children.Add(host);
        return t =>
        {
            sc.ScaleX = sc.ScaleY = .2 + .8 * Out(Seg(t, a, run));
            host.Opacity = t <= a || t >= b ? 0 : t < run ? 1 : 1 - InOut(Seg(t, run, b));
        };
    }

    /// <summary>
    /// A line of light across <paramref name="c"/> closing on its middle (a tear sealing): whole at [<paramref name="a"/>],
    /// drawn in to nothing by <paramref name="b"/>, a hot point left at its middle as it goes.
    /// </summary>
    public static Action<double> Seal(Canvas st, Point c, double len, double th, Color col, double a, double b)
    {
        var br = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        br.GradientStops.Add(new GradientStop(A(col, .2), 0)); br.GradientStops.Add(new GradientStop(A(col, .95), .4));
        br.GradientStops.Add(new GradientStop(A(Mix(col, Colors.White, .7), 1), .5)); br.GradientStops.Add(new GradientStop(A(col, .95), .6)); br.GradientStops.Add(new GradientStop(A(col, .2), 1)); br.Freeze();
        var sc = new ScaleTransform(1, 1, len / 2, th / 2);
        var e = new Ellipse { Width = len, Height = th, Fill = br, Opacity = 0, RenderTransform = sc, IsHitTestVisible = false };
        Canvas.SetLeft(e, c.X - len / 2); Canvas.SetTop(e, c.Y - th / 2);
        st.Children.Add(e);
        return t =>
        {
            double u = Seg(t, a, b);
            sc.ScaleX = Math.Max(.01, 1 - InOut(u));
            sc.ScaleY = 1 + .6 * u;
            e.Opacity = t <= a || t >= b ? 0 : u < .08 ? u / .08 : 1 - Seg(u, .85, 1);
        };
    }

    /// <summary>The hour on a Buddy (the biggest words under <paramref name="root"/> with their unit beside them), or null: a moment punches it.</summary>
    public static FrameworkElement? Hour(DependencyObject root) =>
        Biggest(root) is { } tb ? tb.Parent is StackPanel { Orientation: Orientation.Horizontal } row ? row : tb : null;

    /// <summary>The biggest words under <paramref name="root"/> (the hour on a Buddy), or null: a moment punches it.</summary>
    public static TextBlock? Biggest(DependencyObject root)
    {
        TextBlock? best = null;
        void Walk(DependencyObject d)
        {
            if (d is TextBlock tb && tb.IsVisible && (best == null || tb.FontSize > best.FontSize)) best = tb;
            foreach (var ch in LogicalTreeHelper.GetChildren(d).OfType<DependencyObject>()) Walk(ch);
        }
        Walk(root);
        return best;
    }

    /// <summary>
    /// The figure's punch: <paramref name="tb"/> scaled round its left middle (so it never grows into what is left of it),
    /// up to 1 + <paramref name="over"/> over [<paramref name="a"/>, <paramref name="b"/>] with a dip after, ending on 1.
    /// </summary>
    public static Action<double> Pop(FrameworkElement? tb, double a, double b, double over, bool middle = false)
    {
        if (tb == null || tb.ActualHeight <= 0) return _ => { };
        var sc = ScaleOn(tb, new Point(middle ? tb.ActualWidth / 2 : 0, tb.ActualHeight / 2));
        return t =>
        {
            double u = Seg(t, a, b);
            sc.ScaleX = sc.ScaleY = u <= 0 || u >= 1 ? 1 : u < .3 ? 1 + over * Out(u / .3) : 1 + over * Math.Cos(Math.PI * 1.5 * (u - .3) / .7) * Math.Exp(-2.2 * (u - .3) / .7);
        };
    }

    /// <summary>The pickup strip's newest tile under <paramref name="root"/> (the strip puts it first), or null: a pickup pops it.</summary>
    public static FrameworkElement? NewestTile(DependencyObject root)
    {
        if (root is FrameworkElement { Tag: "pick-tile" } fe) return fe;
        foreach (var ch in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            if (NewestTile(ch) is { } hit) return hit;
        return null;
    }

    /// <summary>A scale set round a point of the element (its punch): the element's own transform becomes this, kept after.</summary>
    public static ScaleTransform ScaleOn(FrameworkElement el, Point center)
    {
        if (el.RenderTransform is ScaleTransform s) { s.CenterX = center.X; s.CenterY = center.Y; return s; }
        var sc = new ScaleTransform(1, 1, center.X, center.Y);
        el.RenderTransform = sc;
        return sc;
    }
}
