using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace KinahLedger.Ui;

/// <summary>
/// A recap sheet's own effects (Week wrapped and Day wrapped, 2026-11-11), played in its staged sequence
/// (<see cref="Sequence"/>, Ui/Motion.Sequence.cs) beside the shared ones (<see cref="Arrive"/>, <see cref="CountUp"/>): the
/// biggest drop's tile landing with some weight and glowing once in its grade, whatever the grade, and the change against
/// last week stamped on last. Each is a step of the one course, so KL_MOTION_AT holds it and with motion off it shows
/// settled. Only opacity, render transforms and a glow that goes when it is done: the sheet keeps its size throughout.
/// </summary>
public static partial class Motion
{
    /// <summary>An ease that runs a little past its end and settles back (ease-out-back): a part that lands with some weight.</summary>
    internal static double Back(double p)
    {
        const double c1 = 1.70158, c3 = c1 + 1;
        double q = Math.Clamp(p, 0, 1) - 1;
        return 1 + c3 * q * q * q + c1 * q * q;
    }

    /// <summary>A badge stamped on: from a third larger and clear down to its size, a little past and back.</summary>
    internal static Action<double> Stamp(FrameworkElement el)
    {
        var scale = new ScaleTransform(1, 1);
        el.RenderTransformOrigin = new Point(0.5, 0.5);
        el.RenderTransform = scale;
        el.Opacity = 0;
        return q => { el.Opacity = Math.Min(1, q * 2.5); scale.ScaleX = scale.ScaleY = 1.33 - 0.33 * Back(q); };
    }

    /// <summary>The share of a <see cref="GlowLand"/> course its landing takes; the glow fades over the rest.</summary>
    const double LandShare = 0.3;

    /// <summary>
    /// An item tile landing and glowing once in <paramref name="tone"/> (its grade's brush): it grows in from 60% with some
    /// weight while the glow comes up, then the glow fades out and goes, so the settled tile costs nothing to draw.
    /// </summary>
    internal static Action<double> GlowLand(FrameworkElement tile, string tone)
    {
        var scale = new ScaleTransform(0.6, 0.6);
        tile.RenderTransformOrigin = new Point(0.5, 0.5);
        tile.RenderTransform = scale;
        tile.Opacity = 0;
        var glow = new DropShadowEffect { Color = Brush(tone, Colors.Gold), ShadowDepth = 0, BlurRadius = 24, Opacity = 0 };
        tile.Effect = glow;
        return q =>
        {
            double land = Math.Clamp(q / LandShare, 0, 1);
            scale.ScaleX = scale.ScaleY = 0.6 + 0.4 * Back(land);
            tile.Opacity = Math.Min(1, land * 2);
            glow.Opacity = q < LandShare ? land : 1 - Math.Pow((q - LandShare) / (1 - LandShare), 2);
            if (q >= 1 && tile.Effect == glow) tile.ClearValue(UIElement.EffectProperty);        };
    }
}
