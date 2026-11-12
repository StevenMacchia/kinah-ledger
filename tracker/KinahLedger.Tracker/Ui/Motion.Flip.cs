using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace KinahLedger.Ui;

/// <summary>
/// The overlay's figures flip (2026-11-12, Steven: a "digit flip counter", in the feel of the game's own UI): on a pickup a
/// figure changes from its old text to its new one in one step, and each character that changed comes up into its place
/// from below and fades in, the rightmost first and each one to its left a little after, like an odometer ticking over.
/// The characters are lined up from the right, so "894K" to "1.27M" flips every one and "1.21M" to "1.27M" only the 7.
/// The TextBlock keeps its text and its layout: the motion is per-character TextEffects (a TranslateTransform and a
/// Foreground brush each), taken off again at the end, so the figure is drawn exactly as before once it has flipped. One
/// course through <see cref="Play"/>, so KL_MOTION_AT holds it at its point for a render and with motion off the figure is
/// just set. A figure the clock alone moved (the hour drifting) is set without a flip. The window's own figures still
/// roll (<see cref="Number"/>); the flip is the overlay's.
/// </summary>
public static partial class Motion
{
    /// <summary>One character's flip takes this long; each character to its left starts this much later.</summary>
    public const double FlipMs = 220, FlipStagger = 35;

    /// <summary>A rising figure's green tint fades back to its own colour over this long (the window's roll takes twice <see cref="RollMs"/>).</summary>
    public const double FlipTintMs = 500;

    /// <summary>How far below its place a character starts, in ems of the figure's size.</summary>
    const double FlipRise = 0.6;

    /// <summary>
    /// The characters of <paramref name="to"/> that differ from <paramref name="from"/>, the two lined up from the right
    /// (a character with none under it in the old text is a change too): their indexes in <paramref name="to"/>, rightmost first.
    /// </summary>
    internal static List<int> FlipChanged(string from, string to)
    {
        var changed = new List<int>();
        for (int k = 0; k < to.Length; k++)
        {
            int i = to.Length - 1 - k, j = from.Length - 1 - k;
            if (j < 0 || from[j] != to[i]) changed.Add(i);
        }
        return changed;
    }

    /// <summary>How long a flip of this many characters takes: the first one's, and the stagger for each after it.</summary>
    internal static double FlipCourse(int changed) => changed <= 0 ? 0 : FlipMs + FlipStagger * (changed - 1);

    /// <summary>
    /// Puts a flip's effects on <paramref name="owner"/>, a TextBlock showing the figure as its Text (its first character at
    /// <paramref name="start"/> 0) or the Run the figure is (1: a Run's start takes position 0): each changed character drawn
    /// <see cref="FlipRise"/> ems below its place, unseen. The step moves them to <paramref name="at"/> ms in, in
    /// <paramref name="ink"/>'s colour of the moment (the figure's own, or its tint); the course takes <paramref name="ms"/>.
    /// </summary>
    internal static Action<double> FlipFx(DependencyObject owner, int start, string from, string to, double em, Func<Color> ink, out double ms)
    {
        var changed = FlipChanged(from, to);
        ms = FlipCourse(changed.Count);
        var dp = owner is TextBlock ? TextBlock.TextEffectsProperty : TextElement.TextEffectsProperty;
        var fx = new TextEffectCollection();
        var parts = new List<(TranslateTransform Move, SolidColorBrush Ink)>();
        double rise = FlipRise * em;
        foreach (var i in changed)
        {
            var move = new TranslateTransform(0, rise);
            var brush = new SolidColorBrush(Colors.Transparent);
            fx.Add(new TextEffect { PositionStart = start + i, PositionCount = 1, Transform = move, Foreground = brush });
            parts.Add((move, brush));
        }
        owner.SetValue(dp, fx);
        return at =>
        {
            var c = ink();
            for (int k = 0; k < parts.Count; k++)
            {
                double local = Math.Clamp((at - k * FlipStagger) / FlipMs, 0, 1);
                parts[k].Move.Y = rise * (1 - Out(local));
                parts[k].Ink.Color = Color.FromArgb((byte)Math.Round(c.A * local), c.R, c.G, c.B);
            }
        };
    }

    /// <summary>A figure's flip taken off: it is drawn as its text again.</summary>
    internal static void Unflip(DependencyObject owner) => owner.ClearValue(owner is TextBlock ? TextBlock.TextEffectsProperty : TextElement.TextEffectsProperty);

    /// <summary>Whether a flip's effects are on a figure now (for the self-test and a render's check).</summary>
    internal static TextEffectCollection? FlipOn(DependencyObject owner) =>
        owner.GetValue(owner is TextBlock ? TextBlock.TextEffectsProperty : TextElement.TextEffectsProperty) is TextEffectCollection { Count: > 0 } c ? c : null;

    /// <summary>
    /// Shows <paramref name="value"/> in <paramref name="tb"/> as <paramref name="fmt"/> writes it. When the figure shown under
    /// <paramref name="key"/> was another one and <paramref name="flip"/>, the new text flips in (its changed characters up
    /// from below, right to left); a rise of over <paramref name="tintRise"/> (a share of the old value; negative: never)
    /// tints it green for a moment. The same value again (each tick) leaves a flip under way alone; any other change that
    /// isn't a flip (the clock moving the hour) is set at once, ending a flip under way.
    /// </summary>
    public static void FlipNumber(TextBlock tb, string key, double value, Func<double, string> fmt, double tintRise = 0, bool flip = true)
    {
        var text = fmt(value);
        bool had = Last.TryGetValue(key, out var was);
        Last[key] = value;
        var fig = Figs.GetValue(tb, _ => new Fig { Shown = value, Target = value });
        if (Playing(tb) && fig.Target == value)
        {
            if (tb.Text != text) tb.Text = text;   // its owner wrote it again: the flip's characters stay where they are
            return;
        }
        var from = had && was > 0 ? fmt(was) : "";
        flip = flip && On && from.Length > 0 && value > 0 && from != text;
        Halt(tb); Unflip(tb);
        fig.Shown = fig.Target = value;
        tb.Text = text;
        if (!flip) { Untint(tb, fig); return; }
        bool tint = tintRise >= 0 && value > was * (1 + tintRise);
        SolidColorBrush? brush = null; Color baseColor = default, good = default;
        if (tint)
        {
            if (!fig.Tinted) { fig.Fg = tb.ReadLocalValue(TextBlock.ForegroundProperty); fig.Tinted = true; baseColor = (tb.Foreground as SolidColorBrush)?.Color ?? Colors.White; }
            else baseColor = ResolvedBase(tb, fig);
            good = Brush("Good", Color.FromRgb(0x3D, 0xD6, 0x8C));
            brush = new SolidColorBrush(good);
            tb.Foreground = brush;
        }
        else Untint(tb, fig);
        var step = FlipFx(tb, 0, from, text, tb.FontSize, () => (tb.Foreground as SolidColorBrush)?.Color ?? Colors.White, out var ms);
        double course = tint ? Math.Max(ms, FlipTintMs) : ms;
        Play(tb, course, p =>
        {
            double at = p * course;
            if (brush != null) brush.Color = Mix(good, baseColor, Math.Clamp((at / FlipTintMs - 0.2) / 0.8, 0, 1));
            step(at);
        }, () => { Unflip(tb); Untint(tb, fig); });
    }

    /// <summary>For a render of the motion: the figure as if it had been <paramref name="from"/> and just flipped to <paramref name="to"/>.</summary>
    internal static void ReplayFlip(TextBlock tb, string key, double from, double to, Func<double, string> fmt, double tintRise = 0)
    {
        Last[key] = from;
        Halt(tb);
        if (Figs.TryGetValue(tb, out var fig)) fig.Shown = fig.Target = from;
        FlipNumber(tb, key, to, fmt, tintRise);
    }

    /// <summary>
    /// A figure that is a Run in its TextBlock (Buddy's hour and session, the first inline of their lines): its new text
    /// (written already) flips in from <paramref name="from"/> on <paramref name="host"/>'s course. <paramref name="tint"/>:
    /// the rising hour's green, fading back to <paramref name="own"/> over <see cref="FlipTintMs"/>; null, the run's own colour.
    /// </summary>
    internal static void FlipRun(TextBlock host, Run run, string from, Color own, Color? tint, Action? done = null)
    {
        var to = run.Text;
        Halt(host);
        Unflip(run);
        if (!On || from.Length == 0 || from == to) { done?.Invoke(); return; }
        SolidColorBrush? brush = null;
        if (tint is Color good) { brush = new SolidColorBrush(good); run.Foreground = brush; }
        var step = FlipFx(run, 1, from, to, run.FontSize, () => (run.Foreground as SolidColorBrush)?.Color ?? own, out var ms);
        double course = tint != null ? Math.Max(ms, FlipTintMs) : ms;
        Play(host, course, p =>
        {
            double at = p * course;
            if (brush != null && tint is Color g) brush.Color = Mix(g, own, Math.Clamp((at / FlipTintMs - 0.2) / 0.8, 0, 1));
            step(at);
        }, () => { Unflip(run); done?.Invoke(); });
    }
}
