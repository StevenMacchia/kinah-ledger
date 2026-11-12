using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace KinahLedger.Ui;

/// <summary>
/// Staged motion (2026-11-11, the run-end recap): a card that plays several effects one after another as it opens, each
/// starting a little after the last, on one course, so the player reads it in order (the card arrives, its figures count up,
/// its tiles land, a tag sweeps in). One course drives them all, so KL_MOTION_AT holds the whole sequence at that point for a
/// render, and with motion off every effect shows settled at once. Nothing loops: it plays once when the card opens.
/// </summary>
public static partial class Motion
{
    /// <summary>A card arriving takes this long; leaving, this long (the same way back, quicker).</summary>
    public const double EnterMs = 300, LeaveMs = 170;
    /// <summary>How far a card arriving rises (px) and the size it grows from.</summary>
    const double EnterRise = 14, EnterScale = 0.96;

    /// <summary>
    /// The effects of a sequence, each with when it starts (ms after the first) and how long it takes. A step gets its own
    /// 0 to 1 point and is called only when that point moves, so a step done (or not started) costs nothing per frame.
    /// </summary>
    public sealed class Cues
    {
        sealed class Cue { public required double At, Ms; public required Action<double> Step; public double Shown = -1; }
        readonly List<Cue> _cues = new();

        /// <summary>When the last effect is done (ms).</summary>
        public double Length { get; private set; }

        public Cues Add(double at, double ms, Action<double> step)
        {
            _cues.Add(new Cue { At = at, Ms = ms, Step = step });
            Length = Math.Max(Length, at + ms);
            return this;
        }

        /// <summary>Every effect where it is <paramref name="ms"/> into the sequence.</summary>
        internal void Seek(double ms)
        {
            foreach (var c in _cues)
            {
                double p = c.Ms > 0 ? Math.Clamp((ms - c.At) / c.Ms, 0, 1) : ms >= c.At ? 1 : 0;
                if (p == c.Shown) continue;
                c.Shown = p;
                c.Step(p);
            }
        }
    }

    /// <summary>
    /// Plays <paramref name="cues"/> once on <paramref name="host"/>'s course: every effect set to its start at once (so no
    /// frame shows the card settled first), then each in its turn. With motion off, every effect shows settled.
    /// </summary>
    public static void Sequence(UIElement host, Cues cues, Action? done = null)
    {
        if (!On) { cues.Seek(cues.Length); done?.Invoke(); return; }
        cues.Seek(0);
        Play(host, cues.Length, p => cues.Seek(p * cues.Length), () => { cues.Seek(cues.Length); done?.Invoke(); });
    }

    /// <summary>A card arriving: it fades in as it rises a little and grows from 96% to its size (its layout never changes).</summary>
    public static Action<double> Enter(FrameworkElement card)
    {
        var (scale, shift) = Lift(card);
        return p =>
        {
            var q = Out(p);
            card.Opacity = q;
            shift.Y = EnterRise * (1 - q);
            scale.ScaleX = scale.ScaleY = EnterScale + (1 - EnterScale) * q;
        };
    }

    /// <summary>
    /// A card leaving: the way it arrived, backwards and quicker, from wherever it is (it may still be arriving), then
    /// <paramref name="then"/> takes it away. With motion off, or in a held render, it goes at once.
    /// </summary>
    public static void Leave(FrameworkElement card, Action then)
    {
        if (!On || FrozenAt != null) { then(); return; }
        var (scale, shift) = Lift(card);
        double o0 = card.Opacity, y0 = shift.Y, s0 = scale.ScaleX;
        Play(card, LeaveMs, p =>
        {
            var q = Out(p);
            card.Opacity = o0 * (1 - q);
            shift.Y = y0 + (EnterRise - y0) * q;
            scale.ScaleX = scale.ScaleY = s0 + (EnterScale - s0) * q;
        }, then);
    }

    /// <summary>The card's own size and place transforms, made once and kept, so arriving and leaving move the same ones.</summary>
    static (ScaleTransform Scale, TranslateTransform Shift) Lift(FrameworkElement card)
    {
        if (card.RenderTransform is TransformGroup { Children: { Count: 2 } g } && g[0] is ScaleTransform s && g[1] is TranslateTransform t) return (s, t);
        s = new ScaleTransform(1, 1); t = new TranslateTransform();
        card.RenderTransformOrigin = new Point(0.5, 0.5);
        card.RenderTransform = new TransformGroup { Children = { s, t } };
        return (s, t);
    }

    /// <summary>
    /// A figure counting up from nothing to <paramref name="to"/> as <paramref name="fmt"/> writes it, eased, the way
    /// <see cref="Number"/> rolls one. With <paramref name="tint"/> it starts in that colour and fades back to its own (the
    /// brush it had when this was made, put back at the end).
    /// </summary>
    public static Action<double> CountUp(TextBlock tb, double to, Func<double, string> fmt, Color? tint = null)
    {
        var text = fmt(to);
        var own = tb.ReadLocalValue(TextBlock.ForegroundProperty);
        var ownColor = (tb.Foreground as SolidColorBrush)?.Color ?? Colors.White;
        var from = tint ?? ownColor;
        var lit = tint != null ? new SolidColorBrush(from) : null;
        return p =>
        {
            tb.Text = p >= 1 ? text : fmt(to * Out(p));
            if (lit == null) return;
            if (p >= 1)
            {
                if (own == DependencyProperty.UnsetValue) tb.ClearValue(TextBlock.ForegroundProperty); else tb.SetValue(TextBlock.ForegroundProperty, own);
                return;
            }
            if (!ReferenceEquals(tb.Foreground, lit)) tb.Foreground = lit;
            // the tint holds for the first quarter, then fades back as the figure settles
            lit.Color = Mix(from, ownColor, Math.Clamp((p - 0.25) / 0.75, 0, 1));
        };
    }

    /// <summary>How long a slot's landing takes: a rare grade's glows, which runs longer (<see cref="Land"/>).</summary>
    public static double LandCourse(string? grade) => Glows(grade) ? GlowMs : LandMs;

    /// <summary>
    /// A slot landing in a sequence: hidden until its turn, then <see cref="Land"/>'s landing (grows in, and a Legend, Unique
    /// or Epic one glows once, in <paramref name="glow"/> when given: a card drawn dark keeps its dark palette whatever the
    /// window's look). Give it <see cref="LandCourse"/> as its length. Held in a render, the landing is held at its own point.
    /// </summary>
    public static Action<double> Landing(FrameworkElement slot, string? grade, Color? glow = null)
    {
        bool started = false;
        void Tint() { if (glow is { } c && slot.Effect is DropShadowEffect d) d.Color = c; }
        return p =>
        {
            if (FrozenAt != null)
            {
                slot.Opacity = 1;
                var was = FrozenAt;
                FrozenAt = p;
                try { Land(slot, false, grade); } finally { FrozenAt = was; }
                Tint();
                return;
            }
            if (p <= 0) { if (!started) slot.Opacity = 0; return; }
            if (started) return;
            started = true;
            slot.Opacity = 1;
            // its whole turn already past (the first frame came late, or motion is off and the sequence shows settled)
            if (p >= 1) return;
            Land(slot, false, grade);
            Tint();
            slot.Opacity = 0;   // the landing's first frame, before its course ticks
        };
    }

    /// <summary>Something that arrives in its turn: fades in as it travels the last few px into place (from the right with <paramref name="dx"/>, from below with <paramref name="dy"/>).</summary>
    public static Action<double> Arrive(FrameworkElement el, double dx, double dy)
    {
        var shift = new TranslateTransform();
        el.RenderTransform = shift;
        return p =>
        {
            var q = Out(p);
            el.Opacity = q;
            shift.X = dx * (1 - q);
            shift.Y = dy * (1 - q);
        };
    }

    /// <summary>
    /// A glint: a band of <paramref name="light"/> passes once across <paramref name="box"/>'s <paramref name="fill"/>, left to
    /// right (the box's background becomes that fill with the band in it, off its edges before and after).
    /// </summary>
    public static Action<double> Glint(Border box, Color fill, Color light)
    {
        var slide = new TranslateTransform(-1, 0);
        box.Background = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5), RelativeTransform = slide,
            GradientStops = { new(fill, 0.3), new(light, 0.5), new(fill, 0.7) },
        };
        return p => slide.X = -1 + 2 * p;
    }
}
