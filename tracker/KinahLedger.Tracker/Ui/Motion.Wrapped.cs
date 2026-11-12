using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace KinahLedger.Ui;

/// <summary>
/// A recap sheet's entrance (Week wrapped and Day wrapped, 2026-11-11): when it opens, its parts land one after another as
/// one short sequence (the total counting up, the tiles and the highlights rising in, the biggest drop landing and glowing
/// in its grade, the change against last week stamped last), all settled within 2.5 s. It plays once,
/// on the open; nothing loops. One course drives every part, so KL_MOTION_AT holds the whole sequence at that point for a
/// render, and with motion off (<see cref="Still"/>, or Windows's animations off) the sheet shows settled at once. Only
/// opacity and render transforms move: the sheet keeps its size from the first frame.
/// </summary>
public static partial class Motion
{
    /// <summary>A count-up's figure is written beforehand at this many points along its course, so a frame only picks one.</summary>
    const int CountSteps = 60;

    /// <summary>One part of a sequence: when it starts (ms after the open), how long it takes, and what it shows at each point of its own course (0 to 1).</summary>
    internal readonly record struct Cue(double At, double Ms, Action<double> Step);

    /// <summary>
    /// Plays a sheet's cues as one course over <paramref name="sheet"/>, each at its own time, until the last one ends.
    /// Without motion every cue is set at its end at once.
    /// </summary>
    internal static void Sequence(UIElement sheet, Cue[] cues)
    {
        if (!On) { Settle(cues); return; }
        double ms = 0;
        foreach (var c in cues) ms = Math.Max(ms, c.At + c.Ms);
        Play(sheet, ms, p =>
        {
            double t = p * ms;
            for (int i = 0; i < cues.Length; i++) cues[i].Step(Math.Clamp((t - cues[i].At) / cues[i].Ms, 0, 1));
        });
    }

    /// <summary>Every cue at its end: the sheet as it is once its sequence is over (with no motion, and for its picture).</summary>
    internal static void Settle(Cue[] cues) { foreach (var c in cues) c.Step(1); }

    /// <summary>An ease that runs a little past its end and settles back (ease-out-back): a part that lands with some weight.</summary>
    internal static double Back(double p)
    {
        const double c1 = 1.70158, c3 = c1 + 1;
        double q = Math.Clamp(p, 0, 1) - 1;
        return 1 + c3 * q * q * q + c1 * q * q;
    }

    /// <summary>A part rising into place: from <paramref name="from"/> px below and clear to where it sits, eased.</summary>
    internal static Action<double> Rise(FrameworkElement el, double from = 12)
    {
        var shift = new TranslateTransform(0, from);
        el.RenderTransform = shift;
        el.Opacity = 0;
        return q => { var e = Out(q); el.Opacity = e; shift.Y = from * (1 - e); };
    }

    /// <summary>A figure counting up from nothing to <paramref name="value"/>, quick at first and settling on it.</summary>
    internal static Action<double> CountUp(TextBlock tb, double value, Func<double, string> fmt)
    {
        var texts = new string[CountSteps + 1];
        for (int i = 0; i < CountSteps; i++) texts[i] = fmt(value * i / CountSteps);
        texts[CountSteps] = fmt(value);
        tb.Text = texts[0];
        return q => tb.Text = texts[(int)Math.Round(Out(q) * CountSteps)];
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
            if (q >= 1 && tile.Effect == glow) tile.ClearValue(UIElement.EffectProperty);
        };
    }
}
