using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace KinahLedger.Ui;

/// <summary>
/// The Esports looks' shared moment pieces (2026-11-21, motion next): a short spray of confetti or sparks, and the eases a
/// broadcast package moves with. Every piece is a small frozen shape built when the moment starts, moved by its transforms
/// and faded by its opacity on one course, and removed when the course ends: nothing is drawn again while it plays.
/// </summary>
static class EsportsFx
{
    static readonly Dictionary<Color, SolidColorBrush> Fills = new();

    static SolidColorBrush Br(Color c)
    {
        if (Fills.TryGetValue(c, out var b)) return b;
        b = new SolidColorBrush(c); b.Freeze();
        return Fills[c] = b;
    }

    static double Seg(double p, double a, double b) => Math.Clamp((p - a) / (b - a), 0, 1);

    /// <summary>
    /// A spray of <paramref name="n"/> pieces (at most 12) from <paramref name="o"/> on <paramref name="layer"/>, fanned between
    /// <paramref name="from"/> and <paramref name="to"/> degrees (0 right, 90 down), flying out up to <paramref name="reach"/> px,
    /// then falling <paramref name="fall"/> px as they fade. Confetti: slips and squares that turn and flip. Sparks
    /// (<paramref name="sparks"/>): thin streaks along their flight that shrink as they go. Starts <paramref name="delay"/> into
    /// its course of <paramref name="ms"/>.
    /// </summary>
    internal static void Spray(Panel layer, Point o, int n, double reach, double from, double to, Color[] colors, double ms = 1100,
        double fall = 24, double delay = 0, bool sparks = false)
    {
        n = Math.Clamp(n, 1, 12);
        var host = new Canvas { IsHitTestVisible = false };
        layer.Children.Add(host);
        var parts = new List<Action<double>>(n);
        for (int i = 0; i < n; i++)
        {
            double u = (i + .5) / n, v = (i * 0.618034 + .11) % 1, z = (i * 0.414214 + .3) % 1;
            double deg = from + (to - from) * u + (v - .5) * 10, ang = deg * Math.PI / 180;
            double dist = reach * (.55 + .45 * z);
            var c = colors[i % colors.Length];
            double w = sparks ? 8 + 6 * v : i % 3 == 0 ? 5.5 : 3.6, h = sparks ? 2.2 : i % 3 == 0 ? 5.5 : 8.5;
            var el = new Rectangle { Width = w, Height = h, Fill = Br(c), Opacity = 0, IsHitTestVisible = false, RadiusX = sparks ? 1 : .6, RadiusY = sparks ? 1 : .6 };
            var flip = new ScaleTransform(1, 1);
            var turn = new RotateTransform(sparks ? deg : v * 180);
            var at = new TranslateTransform(o.X - w / 2, o.Y - h / 2);
            el.RenderTransformOrigin = new Point(.5, .5);
            el.RenderTransform = new TransformGroup { Children = { flip, turn, at } };
            host.Children.Add(el);
            double born = delay + (i % 4) * .02, sway = (z - .5) * 8;
            parts.Add(p =>
            {
                double q = Seg(p, born, 1);
                if (q <= 0) { el.Opacity = 0; return; }
                double k = Motion.Out(Math.Min(1, q / .42));
                at.X = o.X - w / 2 + Math.Cos(ang) * dist * k + (sparks ? 0 : sway * Math.Sin(v * 6 + q * 9) * q);
                at.Y = o.Y - h / 2 + Math.Sin(ang) * dist * k + fall * q * q;
                if (sparks) flip.ScaleX = Math.Max(.15, 1.3 - 1.1 * q);
                else { turn.Angle = v * 180 + (z - .5) * 900 * q; flip.ScaleX = Math.Cos(v * 6 + q * 15); }
                el.Opacity = q < .05 ? q / .05 : q > .68 ? Math.Max(0, 1 - (q - .68) / .32) : 1;
            });
        }
        Motion.Play(host, ms, p => { foreach (var a in parts) a(p); }, () => layer.Children.Remove(host));
    }

    /// <summary>A shake that dies away over <paramref name="a"/> to <paramref name="b"/> of a course: <paramref name="amp"/> px at first, <paramref name="hz"/> swings.</summary>
    internal static double Shake(double t, double a, double b, double amp, double hz = 5)
    {
        if (t <= a || t >= b) return 0;
        double k = (t - a) / (b - a);
        return amp * (1 - k) * (1 - k) * Math.Sin(k * hz * 2 * Math.PI);
    }

    /// <summary>A punch: from <paramref name="big"/> down through 1 with an overshoot to rest at 1 (<paramref name="k"/> 0 to 1).</summary>
    internal static double Punch(double k, double big, double over = 2.6)
    {
        if (k <= 0) return big;
        if (k >= 1) return 1;
        double p = k - 1, back = p * p * ((over + 1) * p + over) + 1;
        return big + (1 - big) * back;
    }
}
