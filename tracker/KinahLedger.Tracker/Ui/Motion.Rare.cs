using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;

namespace KinahLedger.Ui;

/// <summary>
/// The parts a rare drop's moment is made of (2026-11-11, Steven: "cool and flashy"), shared by the window's moment
/// (Ui/MainWindow.RareMoment.cs) and the overlay's rare drop card (Ui/HudRare.Motion.cs), so the two read as one event:
/// a ring that bursts out from the item's tile and fades, a band of light that runs across the name in the grade's
/// colour, and the price rolling up to its figure. Each is a step of one course run through <see cref="Play"/>, so
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
