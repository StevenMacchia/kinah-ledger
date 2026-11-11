using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace KinahLedger.Ui;

/// <summary>
/// The window's motion (2026-11-11, Steven: "professional level" animations), three kinds, each short and only on a change:
/// - a figure that changes rolls to its new value, with a short green tint when money came in (<see cref="Number"/>);
/// - a slot that lands (a new item, or more of one) grows in, and a Legend, Unique or Epic one glows once in its grade's
///   colour (<see cref="Begin"/>, <see cref="Slot"/>, <see cref="End"/>);
/// - a section opens and folds smoothly, its chevron turning (<see cref="Open"/>, <see cref="Close"/>, <see cref="Turn"/>).
/// Nothing loops and nothing plays without a change, so the tracker costs the game nothing while it sits still. Off in a
/// render (<see cref="Still"/>) and when Windows's animations are off. KL_MOTION_AT=0..1 holds every effect at that point
/// instead of playing it, for a render of the frame.
/// </summary>
public static class Motion
{
    /// <summary>No motion at all: the demo and the review loop's renders, which must come out the same each time.</summary>
    public static bool Still { get; set; }

    /// <summary>A render's frame: each effect is held at this point of its course instead of played (KL_MOTION_AT).</summary>
    internal static double? FrozenAt { get; set; } =
        double.TryParse(Environment.GetEnvironmentVariable("KL_MOTION_AT"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var f) ? Math.Clamp(f, 0, 1) : null;

    public static bool On => FrozenAt != null || (!Still && SystemParameters.ClientAreaAnimation);

    /// <summary>A course's eased point: quick to start, settling in (cubic ease-out).</summary>
    internal static double Out(double p) => 1 - Math.Pow(1 - Math.Clamp(p, 0, 1), 3);

    /* ---------------- the driver: one 0 to 1 course per element ---------------- */

    public static readonly DependencyProperty TProperty = DependencyProperty.RegisterAttached("T", typeof(double), typeof(Motion),
        new PropertyMetadata(0.0, (d, e) => { if (Runs.TryGetValue(d, out var r)) r.Step((double)e.NewValue); }));

    sealed class Course { public required Action<double> Step; public int Token; }
    static readonly ConditionalWeakTable<DependencyObject, Course> Runs = new();
    static int _token;

    /// <summary>Whether an effect is under way on this element.</summary>
    internal static bool Playing(UIElement el) => Runs.TryGetValue(el, out _);

    /// <summary>
    /// Runs <paramref name="step"/> from 0 to 1 over <paramref name="ms"/>, then 1 and <paramref name="done"/>. A new course
    /// on the element replaces the one under way (its done doesn't run).
    /// </summary>
    internal static void Play(UIElement el, double ms, Action<double> step, Action? done = null)
    {
        if (FrozenAt is double at) { step(at); if (at >= 1) done?.Invoke(); return; }
        var token = ++_token;
        Runs.AddOrUpdate(el, new Course { Step = step, Token = token });
        var a = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(ms)) { FillBehavior = FillBehavior.HoldEnd };
        a.Completed += (_, _) =>
        {
            if (!Runs.TryGetValue(el, out var r) || r.Token != token) return;
            Runs.Remove(el);
            el.BeginAnimation(TProperty, null);
            step(1);
            done?.Invoke();
        };
        el.BeginAnimation(TProperty, a);
    }

    /// <summary>Ends the course under way where it is, without its last step.</summary>
    internal static void Halt(UIElement el)
    {
        if (!Runs.TryGetValue(el, out _)) return;
        Runs.Remove(el);
        el.BeginAnimation(TProperty, null);
    }

    static Color Brush(string key, Color fallback) => Application.Current?.TryFindResource(key) is SolidColorBrush b ? b.Color : fallback;
    static Color Mix(Color a, Color b, double t) => Color.FromArgb(
        (byte)(a.A + (b.A - a.A) * t), (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

    /* ---------------- figures that roll ---------------- */

    sealed class Fig { public double Shown, Target; public bool Tinted; public object? Fg; }
    static readonly ConditionalWeakTable<TextBlock, Fig> Figs = new();
    // the last value shown under each key, so a figure rebuilt with its card (a chip) still rolls from where it was
    static readonly Dictionary<string, double> Last = new();

    /// <summary>The roll of a figure, in ms (the green tint after money came in takes twice as long to fade).</summary>
    public const double RollMs = 450;

    /// <summary>
    /// Shows <paramref name="value"/> in <paramref name="tb"/> as <paramref name="fmt"/> writes it. When the figure shown under
    /// <paramref name="key"/> was another one, it rolls there; a rise of over <paramref name="tintRise"/> (a share of the old
    /// value; negative: never) tints it green for a moment. The same value again (each tick) leaves a roll under way alone.
    /// <paramref name="roll"/> false: the figure moved with the clock, not with a count (an hour's rate as time passes), and is
    /// set without a roll, or left to a roll under way.
    /// </summary>
    public static void Number(TextBlock tb, string key, double value, Func<double, string> fmt, double tintRise = 0, bool roll = true)
    {
        var text = fmt(value);
        bool had = Last.TryGetValue(key, out var was);
        Last[key] = value;
        var fig = Figs.GetValue(tb, _ => new Fig { Shown = had ? was : value, Target = had ? was : value });
        if (Playing(tb) && (fig.Target == value || !roll)) return;
        double from = Playing(tb) ? fig.Shown : was;
        roll = roll && On && had && from > 0 && value > 0 && fmt(from) != text;
        if (!roll)
        {
            Halt(tb); Untint(tb, fig);
            tb.Text = text; fig.Shown = fig.Target = value;
            return;
        }
        bool tint = tintRise >= 0 && value > from * (1 + tintRise);
        fig.Target = value;
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
        double ms = tint ? RollMs * 2 : RollMs;
        Play(tb, ms, p =>
        {
            var q = Out(p * ms / RollMs);
            fig.Shown = from + (value - from) * q;
            tb.Text = p >= 1 ? text : fmt(fig.Shown);
            if (brush != null) brush.Color = Mix(good, baseColor, Math.Clamp((p - 0.25) / 0.75, 0, 1));
        }, () => { fig.Shown = value; Untint(tb, fig); });
    }

    /// <summary>The colour a tinted figure fades back to: its own brush, as set before the tint.</summary>
    static Color ResolvedBase(TextBlock tb, Fig fig)
    {
        var now = tb.Foreground;
        Untint(tb, fig);
        var c = (tb.Foreground as SolidColorBrush)?.Color ?? Colors.White;
        fig.Fg = tb.ReadLocalValue(TextBlock.ForegroundProperty); fig.Tinted = true;
        tb.Foreground = now;
        return c;
    }

    /// <summary>The figure's own colour back (a theme brush stays a theme brush).</summary>
    static void Untint(TextBlock tb, Fig fig)
    {
        if (!fig.Tinted) return;
        fig.Tinted = false;
        if (fig.Fg == DependencyProperty.UnsetValue || fig.Fg == null) tb.ClearValue(TextBlock.ForegroundProperty);
        else tb.SetValue(TextBlock.ForegroundProperty, fig.Fg);
        fig.Fg = null;
    }

    /// <summary>A figure with nothing to show (" ", "–"): any roll stops, its colour comes back, and the next value shows without a roll.</summary>
    public static void Blank(TextBlock tb, string key, string text)
    {
        Halt(tb);
        if (Figs.TryGetValue(tb, out var fig)) { Untint(tb, fig); Figs.Remove(tb); }
        Last.Remove(key);
        tb.Text = text;
    }

    /// <summary>The last value shown under a key, if any.</summary>
    internal static double? LastOf(string key) => Last.TryGetValue(key, out var v) ? v : null;

    /// <summary>For a render of the motion: the figure as if it had been <paramref name="from"/> and just moved to <paramref name="to"/>.</summary>
    internal static void Replay(TextBlock tb, string key, double from, double to, Func<double, string> fmt, double tintRise = 0)
    {
        Last[key] = from;
        if (Figs.TryGetValue(tb, out var fig)) { fig.Shown = fig.Target = from; }
        Number(tb, key, to, fmt, tintRise);
    }

    /// <summary>Forgets every figure's last value (a new session, Undo): the next one shows without a roll.</summary>
    public static void ForgetFigures() => Last.Clear();

    /* ---------------- slots that land ---------------- */

    static readonly Dictionary<string, Dictionary<string, long>> Seen = new(), Next = new();
    static readonly Dictionary<string, List<(FrameworkElement El, bool More, string? Grade)>> Pending = new();

    /// <summary>A landing (new) takes this long; a glow, this long.</summary>
    public const double LandMs = 280, GlowMs = 1400;

    /// <summary>At most this many new slots in one draw are drops landing; more is another view (a zone, Show all, a new day).</summary>
    public const int MaxLanding = 3;

    /// <summary>A host's slots are about to be drawn again (call before its first <see cref="Slot"/>).</summary>
    public static void Begin(string host) { Next[host] = new(); Pending[host] = new(); }

    /// <summary>A slot drawn in a host, by a key that stays the same from draw to draw (its item), with its count.</summary>
    public static void Slot(FrameworkElement el, string host, string key, long count, string? grade = null)
    {
        if (!Next.TryGetValue(host, out var next) || next.ContainsKey(key)) return;
        next[key] = count;
        if (!Seen.TryGetValue(host, out var was)) return;
        bool isNew = !was.TryGetValue(key, out var c);
        if (isNew || count > c) Pending[host].Add((el, !isNew, grade));
    }

    /// <summary>
    /// A host drawn: its new slots land and the ones with more grow for a moment. Not on its first draw, and not when what
    /// changed looks like another view: a slot it showed gone, or more than <see cref="MaxLanding"/> new ones.
    /// </summary>
    public static void End(string host)
    {
        if (!Next.Remove(host, out var next)) return;
        var list = Pending.Remove(host, out var l) ? l : new();
        bool first = !Seen.TryGetValue(host, out var was);
        Seen[host] = next;
        if (first || !On || list.Count == 0) return;
        if (was!.Keys.Any(k => !next.ContainsKey(k)) || list.Count(x => !x.More) > MaxLanding) return;
        foreach (var (el, more, grade) in list) Land(el, more, grade);
    }

    /// <summary>The grades that glow when they land.</summary>
    public static bool Glows(string? grade) => grade is "Legend" or "Unique" or "Epic";

    /// <summary>One slot landing: a new one grows in from 90% as it fades in, more of one swells 8% and settles; a rare one glows.</summary>
    internal static void Land(FrameworkElement el, bool more, string? grade)
    {
        var scale = new ScaleTransform(1, 1);
        el.RenderTransformOrigin = new Point(0.5, 0.5);
        el.RenderTransform = scale;
        double opacity = el.Opacity;
        DropShadowEffect? glow = null;
        if (Glows(grade))
        {
            glow = new DropShadowEffect { Color = Brush("Grade" + grade, Colors.Gold), ShadowDepth = 0, BlurRadius = 18, Opacity = 0 };
            el.Effect = glow;
        }
        double ms = glow != null ? GlowMs : LandMs;
        Play(el, ms, p =>
        {
            var q = Out(p * ms / LandMs);
            double s = more ? 1.08 - 0.08 * q : 0.9 + 0.1 * q;
            scale.ScaleX = scale.ScaleY = s;
            if (!more) el.Opacity = opacity * q;
            // the glow: up in the first sixth, held, then fading out faster toward the end
            if (glow != null) glow.Opacity = p < 0.15 ? 0.95 * p / 0.15 : 0.95 * (1 - Math.Pow((p - 0.15) / 0.85, 2));
        }, () => { el.Opacity = opacity; if (glow != null && el.Effect == glow) el.ClearValue(UIElement.EffectProperty); el.ClearValue(UIElement.RenderTransformProperty); });
    }

    /* ---------------- sections that open and fold ---------------- */

    /// <summary>A section opening or folding takes this long.</summary>
    public const double OpenMs = 220, CloseMs = 170, TurnMs = 170;

    /// <summary>
    /// A section just shown (made visible, or drawn open after a click) opens: from no height to its own, fading in. Waits for
    /// its first layout to know its height; the height cap is set before that frame is drawn, so it never shows at full first.
    /// </summary>
    public static void Open(FrameworkElement body)
    {
        if (!On) return;
        var maxLocal = body.ReadLocalValue(FrameworkElement.MaxHeightProperty);
        var clipLocal = body.ReadLocalValue(UIElement.ClipToBoundsProperty);
        void Restore()
        {
            if (maxLocal == DependencyProperty.UnsetValue) body.ClearValue(FrameworkElement.MaxHeightProperty); else body.SetValue(FrameworkElement.MaxHeightProperty, maxLocal);
            if (clipLocal == DependencyProperty.UnsetValue) body.ClearValue(UIElement.ClipToBoundsProperty); else body.SetValue(UIElement.ClipToBoundsProperty, clipLocal);
            body.Opacity = 1;
        }
        // clear until its course starts, so no frame shows it at full height first
        if (FrozenAt == null) body.Opacity = 0;
        EventHandler? laid = null;
        int passes = 0;
        laid = (_, _) =>
        {
            // a body that never takes a height (empty, or its card folded meanwhile) is let go after a few layouts, shown as it is
            if (body.ActualHeight <= 0) { if (++passes > 12) { body.LayoutUpdated -= laid; Restore(); } return; }
            body.LayoutUpdated -= laid;
            double full = body.ActualHeight;
            body.ClipToBounds = true;
            Play(body, OpenMs, p => { var q = Out(p); body.MaxHeight = Math.Max(0.01, full * q); body.Opacity = q; }, Restore);
        };
        body.LayoutUpdated += laid;
    }

    /// <summary>A section folding: down to no height, fading out, then <paramref name="then"/> (it hides or redraws it).</summary>
    public static void Close(FrameworkElement body, Action then)
    {
        if (!On || body.ActualHeight <= 0 || FrozenAt != null) { then(); return; }
        double full = body.ActualHeight;
        var maxLocal = body.ReadLocalValue(FrameworkElement.MaxHeightProperty);
        var clipLocal = body.ReadLocalValue(UIElement.ClipToBoundsProperty);
        body.ClipToBounds = true;
        Play(body, CloseMs, p => { var q = Out(p); body.MaxHeight = Math.Max(0.01, full * (1 - q)); body.Opacity = 1 - q; }, () =>
        {
            then();
            if (maxLocal == DependencyProperty.UnsetValue) body.ClearValue(FrameworkElement.MaxHeightProperty); else body.SetValue(FrameworkElement.MaxHeightProperty, maxLocal);
            if (clipLocal == DependencyProperty.UnsetValue) body.ClearValue(UIElement.ClipToBoundsProperty); else body.SetValue(UIElement.ClipToBoundsProperty, clipLocal);
            body.Opacity = 1;
        });
    }

    /// <summary>A chevron turning from one angle to another (folded 0, open 90).</summary>
    public static void Turn(System.Windows.Shapes.Path chev, double from, double to)
    {
        var rt = new RotateTransform(On ? from : to);
        chev.RenderTransformOrigin = new Point(0.5, 0.5);
        chev.RenderTransform = rt;
        if (!On) return;
        Play(chev, TurnMs, p => rt.Angle = from + (to - from) * Out(p));
    }
}
