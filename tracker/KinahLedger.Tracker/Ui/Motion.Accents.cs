using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace KinahLedger.Ui;

/// <summary>
/// The overlay's accents (0.10.55, Badge's motion, Ui/HudWindow.BadgeMotion.cs): the small marks a change leaves on a look,
/// written once here so Badge doesn't carry a third copy of what Pop and Buddy each drew for themselves. Each plays once, on
/// a change, through <see cref="Play"/>, so KL_MOTION_AT holds it for a render and <see cref="On"/> false shows none of it.
/// - an overlay figure rolls through <see cref="Number"/> when it reads back as a number (<see cref="Figure"/>);
/// - a frame's edge lights in a tone and fades back to its line (<see cref="EdgeLight"/>);
/// - something just changed pops from a little larger and settles (<see cref="Pop"/>);
/// - a meter that passed its mark swells and glows once (<see cref="Pulse"/>);
/// - a rare drop's colour beats twice and holds (<see cref="Beats"/>).
/// </summary>
public static partial class Motion
{
    /// <summary>An edge lighting up fades back to its line in this long; a pop settles in this long; a pulse, this long.</summary>
    public const double EdgeMs = 900, PopMs = 420, PulseMs = 900;

    /// <summary>How solid a lit edge starts (as Pop's pill and Buddy's card light theirs).</summary>
    const byte EdgeAlpha = 0xE6;

    /// <summary>A rare drop's beats: down to this strength between them, and held full from this far into the course.</summary>
    const double BeatLow = 0.35, BeatsEnd = 0.8;

    /// <summary>A pulse is up at its fullest this far into its course, then settles.</summary>
    const double PulsePeak = 0.3;

    /// <summary>Two colours mixed, <paramref name="t"/> of the way from <paramref name="a"/> to <paramref name="b"/> (alpha too).</summary>
    internal static Color Blend(Color a, Color b, double t) => Mix(a, b, Math.Clamp(t, 0, 1));

    /// <summary>
    /// An overlay figure as the overlay writes it ("1.27M", "894K"): when it reads back as a number it goes through
    /// <see cref="Number"/> under <paramref name="key"/> (rolled when <paramref name="roll"/>, tinted on a rise of over
    /// <paramref name="tintRise"/>); anything else ("warming up", "") is shown as it is and the next number shows without a roll.
    /// </summary>
    internal static void Figure(TextBlock tb, string key, string text, double tintRise, bool roll)
    {
        if (HudTicker.Num(text) is double v && v > 0) Number(tb, key, v, Amounts.Short, tintRise, roll);
        else Blank(tb, key, text);
    }

    /// <summary>
    /// A frame's edge lights in <paramref name="tone"/> and fades back to <paramref name="rest"/>, its own line (900 ms): a
    /// pickup's mark on a look. A frame another colour took meanwhile (a rare drop's) keeps that colour.
    /// </summary>
    internal static void EdgeLight(Border edge, Color tone, Brush rest)
    {
        if (!On) return;
        var line = (rest as SolidColorBrush)?.Color ?? Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF);
        var lit = Color.FromArgb(EdgeAlpha, tone.R, tone.G, tone.B);
        var b = new SolidColorBrush(lit);
        edge.BorderBrush = b;
        Play(edge, EdgeMs, p => b.Color = Mix(line, lit, 1 - Out(p)), () => { if (edge.BorderBrush == b) edge.BorderBrush = rest; });
    }

    /// <summary>
    /// Something that just changed (a pickup's worth, its name) pops: drawn <paramref name="from"/> times its size, it settles
    /// to its own over <see cref="PopMs"/>, growing from <paramref name="origin"/> (0 to 1 across it), so it stays in its place.
    /// </summary>
    internal static void Pop(FrameworkElement el, double from, Point origin)
    {
        if (!On) return;
        var s = new ScaleTransform(from, from);
        el.RenderTransformOrigin = origin;
        el.RenderTransform = s;
        Play(el, PopMs, p => s.ScaleX = s.ScaleY = 1 + (from - 1) * (1 - Out(p)), () => el.ClearValue(UIElement.RenderTransformProperty));
    }

    /// <summary>
    /// A meter that just passed its mark (your best here beaten): <paramref name="host"/> swells by <paramref name="swell"/> and
    /// glows once in <paramref name="tone"/>, up quickly and settling (900 ms). The course runs on <paramref name="course"/>, so a
    /// sweep under way on the host itself goes on alongside it.
    /// </summary>
    internal static void Pulse(FrameworkElement host, UIElement course, Color tone, double swell, double blur)
    {
        if (!On) return;
        var s = new ScaleTransform(1, 1);
        host.RenderTransformOrigin = new Point(0.5, 0.5);
        host.RenderTransform = s;
        var glow = new DropShadowEffect { Color = tone, ShadowDepth = 0, BlurRadius = blur, Opacity = 0 };
        host.Effect = glow;
        Play(course, PulseMs, p =>
        {
            double up = p < PulsePeak ? Out(p / PulsePeak) : 1 - Out((p - PulsePeak) / (1 - PulsePeak));
            s.ScaleX = s.ScaleY = 1 + swell * up;
            glow.Opacity = 0.95 * up;
        }, () => { host.ClearValue(UIElement.RenderTransformProperty); if (host.Effect == glow) host.ClearValue(UIElement.EffectProperty); });
    }

    /// <summary>
    /// A rare drop's colour at point <paramref name="p"/> of its course, 0.35 to 1: full as it lands, then down and back up
    /// twice (two beats), held full from four fifths in, with no jump anywhere along the way.
    /// </summary>
    internal static double Beats(double p) => p >= BeatsEnd ? 1 : BeatLow + (1 - BeatLow) * Math.Abs(Math.Cos(Math.Clamp(p, 0, 1) / BeatsEnd * 2 * Math.PI));
}
