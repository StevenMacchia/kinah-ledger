using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace KinahLedger.Ui;

/// <summary>
/// The parts a rare drop's moment is made of (2026-11-11, Steven: "cool and flashy"), shared by the window's moment
/// (Ui/MainWindow.RareMoment.cs) and the overlay's rare drop card (Ui/HudRare.Motion.cs), so the two read as one event:
/// a ring that bursts out from the item's tile and fades, a short burst of sparks in the grade's colour flying out from it
/// with the ring (the overlay's, 2026-11-12), a band of light that runs across the name in the grade's colour, and the
/// price rolling up to its figure. Each is a step of one course run through <see cref="Play"/>, so
/// KL_MOTION_AT holds it for a render and nothing of it shows with motion off.
/// </summary>
public static partial class Motion
{
    /// <summary>How far a course at <paramref name="p"/> is through its part from <paramref name="from"/> to <paramref name="to"/>: 0 before it, 1 after.</summary>
    internal static double Span(double p, double from, double to) => Math.Clamp((p - from) / (to - from), 0, 1);

    /// <summary>The ring a tile bursts with: an outline circle the tile's size in the colour, unseen until <see cref="Burst"/> moves it.</summary>
    internal static Ellipse BurstRing(Color c, double size, double thickness) => new()
    {
        Width = size, Height = size, Stroke = new SolidColorBrush(c), StrokeThickness = thickness, Opacity = 0, IsHitTestVisible = false,
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform(1, 1),
    };

    /// <summary>
    /// The ring at <paramref name="r"/> of its burst (0 to 1): it starts on the tile's edge and grows by <paramref name="grow"/>
    /// of its size, quick at first, fading as it goes; unseen before and after.
    /// </summary>
    internal static void Burst(Ellipse ring, double r, double grow)
    {
        if (r <= 0 || r >= 1) { ring.Opacity = 0; return; }
        var s = (ScaleTransform)ring.RenderTransform;
        s.ScaleX = s.ScaleY = 1 + grow * Out(r);
        ring.Opacity = 0.9 * (1 - r);
    }

    /* ---------------- the sparks ---------------- */

    /// <summary>A burst's sparks: this many, and they fly and fade over this long.</summary>
    public const int SparkCount = 12;
    public const double SparkMs = 650;

    /// <summary>Each spark's reach, as a share of the burst's (so they end at different distances), and its turn off the even spacing, in radians.</summary>
    static readonly double[] SparkReach = { 1, 0.78, 0.92, 0.7, 0.96, 0.82 }, SparkTurn = { 0, 0.18, -0.12, 0.1, -0.2, 0.06 };

    /// <summary>
    /// A burst of sparks for a tile <paramref name="size"/> wide: <see cref="SparkCount"/> dots <paramref name="dot"/> across
    /// in the colour (every other one lit toward white), on a canvas the tile's size to sit over its cell, centred
    /// on it. Unseen until <see cref="Sparks"/> moves them; put it under the tile in its host, so they come out from behind it.
    /// </summary>
    internal static Canvas BurstSparks(Color c, double size, double dot)
    {
        var canvas = new Canvas { Width = size, Height = size, IsHitTestVisible = false, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var lit = Mix(c, Colors.White, 0.6);   // lit well toward white, so they read over the drop's own colour wash
        for (int i = 0; i < SparkCount; i++)
        {
            var e = new Ellipse { Width = dot, Height = dot, Fill = new SolidColorBrush(i % 2 == 0 ? lit : c), Opacity = 0, RenderTransform = new TranslateTransform() };
            Canvas.SetLeft(e, (size - dot) / 2); Canvas.SetTop(e, (size - dot) / 2);
            canvas.Children.Add(e);
        }
        return canvas;
    }

    /// <summary>
    /// The sparks at <paramref name="r"/> of their burst (0 to 1): each flies out from a third of the way to the tile's edge
    /// to its own reach, at most <paramref name="reach"/> past the tile's edge (a dot's whole width included, so none ever
    /// passes it), quick at first and settling, fading as it goes and gone by the end; unseen before and after.
    /// </summary>
    internal static void Sparks(Canvas sparks, double r, double reach)
    {
        bool shown = r > 0 && r < 1;
        double size = sparks.Width, n = sparks.Children.Count;
        for (int i = 0; i < sparks.Children.Count; i++)
        {
            if (sparks.Children[i] is not Ellipse e) continue;
            if (!shown) { e.Opacity = 0; continue; }
            double dot = e.Width, far = size / 2 + reach - dot / 2, near = size / 6;
            double d = near + (far * SparkReach[i % SparkReach.Length] - near) * Out(r);
            double a = 2 * Math.PI * i / n + SparkTurn[i % SparkTurn.Length] - Math.PI / 2;
            var move = (TranslateTransform)e.RenderTransform;
            move.X = d * Math.Cos(a); move.Y = d * Math.Sin(a);
            // up at once, then fading out, faster toward the end
            e.Opacity = (r < 0.08 ? r / 0.08 : 1) * (1 - r * r);
        }
    }

    /// <summary>The share of a course of <paramref name="ms"/> the sparks take (their <see cref="SparkMs"/>), from <paramref name="from"/> in.</summary>
    internal static double SparkSpan(double ms, double from = 0) => from + SparkMs / ms;

    /// <summary>The sweep's half width, as a share of the name: the band of light is about a third of the name wide.</summary>
    const double SweepHalf = 0.16;

    /// <summary>
    /// A name's fill for the sweep: its grade colour with a band of that colour, lit most of the way to white, which
    /// <see cref="Sweep"/> moves across it. Unfrozen, so its stops can move; given back to the frozen colour when it ends.
    /// </summary>
    internal static LinearGradientBrush SweepBrush(Color c)
    {
        var lit = Mix(c, Colors.White, 0.7);
        var b = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
        b.GradientStops.Add(new GradientStop(c, -2 * SweepHalf));
        b.GradientStops.Add(new GradientStop(lit, -SweepHalf));
        b.GradientStops.Add(new GradientStop(c, 0));
        return b;
    }

    /// <summary>The band at <paramref name="s"/> of its run (0 to 1): from just off the name's left end to just past its right, eased.</summary>
    internal static void Sweep(LinearGradientBrush b, double s)
    {
        double mid = -SweepHalf + (1 + 2 * SweepHalf) * Out(s);
        b.GradientStops[0].Offset = mid - SweepHalf;
        b.GradientStops[1].Offset = mid;
        b.GradientStops[2].Offset = mid + SweepHalf;
    }

    /// <summary>
    /// A price ("28,000", "≈1.2M") rolled in to <paramref name="q"/> of its figure, as the window and the overlay write
    /// figures (Amounts.Short), its "≈" kept; the price itself at the end, and anything that isn't a figure as it is.
    /// </summary>
    internal static string RollIn(string price, double q)
    {
        if (q >= 1) return price;
        var about = price.StartsWith('≈') ? "≈" : "";
        return HudTicker.Num(price[about.Length..]) is double v ? about + Amounts.Short(Math.Round(v * Out(q))) : price;
    }
}
