using System.Windows;
using System.Windows.Media.Animation;

namespace KinahLedger.Ui;

public static partial class Motion
{
    /// <summary>
    /// A course that runs again and again (the companion's idle: its float, its wings, its glow), at most
    /// <paramref name="fps"/> frames a second so an overlay over the game stays cheap. With motion off it holds the start;
    /// in a render's frame (KL_MOTION_AT, <see cref="FrozenMs"/>) it holds that point. <see cref="Halt"/> ends it.
    /// </summary>
    internal static void Loop(UIElement el, double ms, Action<double> step, int fps = 30)
    {
        if (FrozenAt is double at) { step(FrozenMs is double fm ? fm % Math.Max(1, ms) / Math.Max(1, ms) : at); return; }
        if (!On) { step(0); return; }
        var token = ++_token;
        Runs.AddOrUpdate(el, new Course { Step = step, Token = token });
        var a = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(ms)) { RepeatBehavior = RepeatBehavior.Forever };
        Timeline.SetDesiredFrameRate(a, fps);
        el.BeginAnimation(TProperty, a);
    }
}
