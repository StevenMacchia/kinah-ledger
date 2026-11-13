using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

/// <summary>
/// Badge's motion (the Strip look, 0.10.55; Steven, 2026-11-11: "cool and flashy"), so the default look moves like Pop
/// (Ui/HudTicker.Motion.cs) and Buddy (Ui/HudCard.Motion.cs). Once per change, nothing loops; through Ui/Motion.cs, so a
/// render, the Options preview (still) and Windows's animations off show none of it.
/// Since 2026-11-12 it moves like the game's own pop-ups (Steven: "like the game's own UI"): short, eased out, small.
/// - on a pickup just made (Ui/HudFreshPicks.cs: not a price refresh, a session loaded or cleared, another character or an
///   Undo) the hour, Kinah, loot, best and session figures flip to their new values character by character
///   (Ui/Motion.Flip.cs), the per-hour ones green for a moment as they rise, and the panel's edge lights softly gold;
/// - the hot streak bar along the bottom edge fills as pickups come quickly and drains as they slow (Ui/HudStreak.cs);
/// - the ring of your best here sweeps round to its new share, and when the sweep reaches your best it turns mint with its
///   star, swelling and glowing once;
/// - a new pickup's worth pops from 110% and the feed's live dot flashes in its grade's colour (gold for Kinah);
/// - a rare drop's coloured frame beats twice as it lands, its spark popping with it and a burst of sparks in its colour
///   flying out from it (Motion.Sparks, the rare card's own on Pop and Buddy);
/// - the notice, accuracy and rare drop lines cross-fade when they change, fade in when they come and out when they go.
/// </summary>
public sealed partial class HudWindow
{
    /// <summary>A drawing (the layout renders' strip, the Options preview): nothing of Badge moves.</summary>
    readonly bool _still;
    /// <summary>This panel's own figure keys: the Options preview never rolls the overlay's.</summary>
    readonly string _key = "hud-strip|" + Guid.NewGuid().ToString("N")[..8] + "|";
    readonly FreshPicks _picks = new();
    /// <summary>The pickup this update brought, if it was one just made (PickedUp, for BadgeMoved).</summary>
    Pick? _landed;
    bool _ringDrawn, _wasBeat;
    /// <summary>Your best was just passed: the ring pulses when its sweep gets there.</summary>
    bool _pulseDue;
    /// <summary>The ring a sweep under way moves (built once per sweep), or null when the ring is drawn still.</summary>
    SweepRing? _sweep;
    /// <summary>The colour of the rare drop on the frame now, which its beat runs up to.</summary>
    Color _alertTone;
    /// <summary>The feed dot's glow at rest, which its flash settles back to.</summary>
    double _dotGlow;
    readonly Dictionary<TextBlock, LineFade> _fades = new();
    /// <summary>The hot streak bar along the bottom edge; it sits this far above the frame's inner edge.</summary>
    readonly StreakBar _streak = new();
    const double StreakEdge = 2;

    bool Moves => !_still && _look == null && Motion.On;

    /// <summary>The figures that roll on a pickup, with the rise that tints them green: the per-hour ones and your best do, the session doesn't.</summary>
    static readonly (string Key, double TintRise)[] Rolled = { ("rate", RiseTint), ("kinah", RiseTint), ("loot", RiseTint), ("best", RiseTint), ("total", NoTint) };
    const double RiseTint = 0.01, NoTint = -1;

    /// <summary>The ring on Badge (smaller than Buddy's): its size and line.</summary>
    const double RingSize = 30, RingStroke = 3.5;
    /// <summary>Past your best: the ring swells this much and glows this wide (inside the panel's padding).</summary>
    const double BestSwell = 0.07, BestBlur = 10;
    /// <summary>A new pickup's worth pops from this size; the last pickup's name from this one (at its 240 px widest, still inside the panel's right padding).</summary>
    const double WorthPop = Motion.PopFrom, LastPop = 1.06;
    /// <summary>The feed's live dot on a pickup: it swells to this many times its size and is back in this long.</summary>
    const double DotSwell = 1.6, DotMs = 500;
    /// <summary>The share of the dot's course spent lighting up; the rest fades back.</summary>
    const double DotPeak = 0.25;
    /// <summary>A rare drop's beat on the frame, and its spark's pop, settling and glow (small enough to stay inside the panel's padding).</summary>
    const double AlertBeatMs = 1400, SparkPop = 1.15, SparkSettleMs = 420, SparkBlur = 4;
    /// <summary>The burst round the spark: its dots this wide, reaching a quarter of the 14 px sign past its edge (inside the panel's padding).</summary>
    const double BurstDot = 2, BurstReach = 3.5;
    /// <summary>A sweeping ring's arc: hidden below this share, and short of a closed circle at most (HudKit.Ring's own bounds).</summary>
    const double MinArc = 0.004, MaxArc = 0.999;
    /// <summary>A sweep this close to its share has got there.</summary>
    const double SweepDone = 1e-6;
    /// <summary>A state line fades out in this long and the next fades in in this long, rising this far into its place.</summary>
    const double LineOutMs = 180, LineInMs = 240, LineRise = 3;
    /// <summary>A render's held frame: the figures rolled from this share of their value, the ring swept from this share of its own.</summary>
    const double ReplayFrom = 0.85, RingReplayFrom = 0.7;

    /// <summary>
    /// Whether this update brings a pickup just made (before anything is drawn; BadgeMoved lands it): the one thing Badge's
    /// figures roll, its ring sweeps and its edge lights for. When the pickups went away or back in time (a session cleared,
    /// another character, an Undo), what Badge showed is forgotten, so this draw is a plain first draw.
    /// </summary>
    bool PickedUp(IReadOnlyDictionary<string, string> values)
    {
        if (_still) return false;
        _landed = _picks.Next(values, out var back);
        if (back) ForgetShown();
        return _landed != null;
    }

    /// <summary>Badge's figures and ring as if never drawn: the next values show as they are, without a roll or a sweep.</summary>
    void ForgetShown()
    {
        foreach (var (k, _) in Rolled)
            if (_cells.TryGetValue(k, out var tb)) Motion.Blank(tb, _key + k, tb.Text);
        if (_feedRate != null) Motion.Blank(_feedRate, _key + "feedRate", _feedRate.Text);
        Motion.Halt(_ringHost);
        Motion.ForgetShare(_key + "ring");
        _ringDrawn = _wasBeat = _pulseDue = false;
    }

    /// <summary>
    /// Badge's motion for this update, after it is drawn: on a pickup just made the figures roll, the pickup lands and the
    /// edge lights; the notice line cross-fades when it changed.
    /// </summary>
    void BadgeMoved(IReadOnlyDictionary<string, string> values, bool picked)
    {
        if (_still) return;
        string V(string k) => values.TryGetValue(k, out var v) ? v : "";
        bool roll = picked && Moves;
        foreach (var (k, tint) in Rolled)
            if (_cells.TryGetValue(k, out var tb)) RollFigure(tb, k, V(k), tint, roll);
        if (_feedLine != null && _feedRate != null) RollFigure(_feedRate, "feedRate", V("feedRate"), RiseTint, roll);
        if (roll)
        {
            PickupLanded(ToneOf(_landed));
            if (_alert.Visibility != Visibility.Visible) Motion.EdgeLight(_frame, Rgb(GoldC), FrameBrush);
        }
        Fade(_note, _noteSign);
        // the lines Update doesn't write are as they stand on the first draw, so the first rare drop or accuracy line fades in
        Seed(_acc, _accSign); Seed(_alert, _alertSign);
    }

    /// <summary>A state line's face noted as it stands, if it hasn't been yet (the panel's first draw).</summary>
    void Seed(TextBlock text, Image sign)
    {
        if (!_fades.ContainsKey(text)) _fades[text] = new LineFade { To = FaceOf(text, sign) };
    }

    /// <summary>
    /// A figure through <see cref="Motion.Figure"/>, flipped when <paramref name="roll"/>. A flip writes the new text at once
    /// (its characters come up into place), so the panel, sized to what it shows, changes width once, not with each frame.
    /// </summary>
    void RollFigure(TextBlock tb, string part, string text, double tint, bool roll)
    {
        if (roll && tb.Parent is FrameworkElement box && HudTicker.Num(text) is double v && v > 0)
        {
            // the old text's width is still the laid-out one: the new text was written this update and isn't laid out yet
            double w = Math.Ceiling(Math.Max(tb.ActualWidth, TextWidth(tb, text)));
            tb.MinWidth = w;
            // its own course (on the cell's box, which nothing else moves) ends with the longest roll, the tinted one
            Motion.Play(box, Motion.RollMs * 2, _ => { }, () => tb.ClearValue(MinWidthProperty));
        }
        Motion.Figure(tb, _key + part, text, tint, roll);
    }

    /// <summary>How wide <paramref name="text"/> is in the figure's own face and size, as the panel draws it.</summary>
    static double TextWidth(TextBlock tb, string text) => new FormattedText(text, System.Globalization.CultureInfo.CurrentUICulture, tb.FlowDirection,
        new Typeface(tb.FontFamily, tb.FontStyle, tb.FontWeight, tb.FontStretch), tb.FontSize, Brushes.Black, null,
        TextOptions.GetTextFormattingMode(tb), VisualTreeHelper.GetDpi(tb).PixelsPerDip).WidthIncludingTrailingWhitespace;

    /// <summary>A pickup's colour on the panel: gold for Kinah, its grade's for a graded item, the live dot's own mint for a common one (or none known).</summary>
    static Color ToneOf(Pick? p) => p == null || (!p.Kinah && p.Grade is "" or "Common") ? Rgb(MintC) : p.Kinah ? Rgb(GoldC) : GradeColor(p.Grade);

    /// <summary>A pickup lands: its worth on the feed pops, the live dot flashes in <paramref name="tone"/>, and the last pickup's name pops.</summary>
    void PickupLanded(Color tone)
    {
        if (_feedLine is { Visibility: Visibility.Visible } && _feedValue != null)
        {
            // from its left edge, so it grows into the gap before the rule, not into the item's name
            if (_feedValue.Text.Length > 0) Motion.Pop(_feedValue, WorthPop, new Point(0, 0.5));
            if (((FrameworkElement)_feedLine.Parent).Tag is Ellipse dot && dot.Visibility == Visibility.Visible) DotFlash(dot, tone);
        }
        if (_cells.TryGetValue("last", out var last) && last.Text.Length > 0) Motion.Pop(last, LastPop, new Point(0, 0.5));
    }

    /// <summary>The feed's live dot lights in <paramref name="tone"/>, swelling and glowing, then fades back to its mint (700 ms).</summary>
    void DotFlash(Ellipse dot, Color tone)
    {
        var mint = Rgb(MintC);
        var glow = dot.Effect as DropShadowEffect;
        // the glow's own strength, as BuildFeed set it: not a flash's raised one when another pickup lands mid-flash
        if (!Motion.Playing(dot)) _dotGlow = glow?.Opacity ?? 0;
        double rest = _dotGlow;
        var fill = new SolidColorBrush(tone);
        dot.Fill = fill;
        var s = new ScaleTransform(1, 1);
        dot.RenderTransformOrigin = new Point(0.5, 0.5);
        dot.RenderTransform = s;
        Motion.Play(dot, DotMs, p =>
        {
            double up = Motion.Swell(p, DotPeak);
            s.ScaleX = s.ScaleY = 1 + (DotSwell - 1) * up;
            // the tone holds through the swell and turns back to mint as it settles
            fill.Color = Motion.Blend(mint, tone, p < DotPeak ? 1 : up);
            if (glow != null) { glow.Color = fill.Color; glow.Opacity = rest + (1 - rest) * up; }
        }, () =>
        {
            dot.Fill = Mint;
            if (glow != null) { glow.Color = mint; glow.Opacity = rest; }
            dot.ClearValue(UIElement.RenderTransformProperty);
        });
    }

    /// <summary>The ring at a share, drawn still: the gold share of your best here, or mint with a star once it is beaten.</summary>
    void RingAt(double s, bool beat)
    {
        _sweep = null;
        _ringHost.Children.Clear();
        _ringHost.Children.Add(Ring(RingSize, RingStroke, s, Rgb(GoldC), beat ? "★" : $"{Math.Round(s * 100):0}"));
    }

    /// <summary>The ring a sweep moves: HudKit's ring built once, its arc and its figure changed in place at each step.</summary>
    sealed class SweepRing
    {
        public required ArcSegment Arc;
        public required FrameworkElement Line;
        public required TextBlock Figure;
        public int Shown = -1;
    }

    /// <summary>
    /// The ring at <paramref name="s"/> on its way to <paramref name="share"/>: gold with its percent while it sweeps, the
    /// arc and figure moved in place; drawn still once it gets there, mint with its star if that is past your best (and the
    /// pulse, if your best was just passed).
    /// </summary>
    void RingStep(double s, double share, bool beat)
    {
        if (Math.Abs(s - share) < SweepDone)
        {
            RingAt(share, beat);
            if (beat && _pulseDue && Moves) Motion.Pulse(_ringHost, _chaseLine, Rgb(MintC), BestSwell, BestBlur);
            _pulseDue = false;
            return;
        }
        if ((_sweep ??= NewSweep()) is not { } r) { RingAt(s, false); return; }
        double c = RingSize / 2, rad = (RingSize - RingStroke) / 2, a = 2 * Math.PI * Math.Clamp(s, 0, MaxArc);
        r.Arc.Point = new Point(c + rad * Math.Sin(a), c - rad * Math.Cos(a));
        r.Arc.IsLargeArc = a > Math.PI;
        r.Line.Visibility = s > MinArc ? Visibility.Visible : Visibility.Collapsed;
        int pct = (int)Math.Round(s * 100);
        if (pct != r.Shown) { r.Shown = pct; r.Figure.Text = pct.ToString(); }
    }

    /// <summary>
    /// HudKit's gold ring at half, put on the host with an arc of its own the sweep can move (HudKit freezes its geometry);
    /// null if the ring is ever drawn another way, and the sweep then draws it still at each step.
    /// </summary>
    SweepRing? NewSweep()
    {
        var face = Ring(RingSize, RingStroke, 0.5, Rgb(GoldC), "50");
        if (face.Children.OfType<System.Windows.Shapes.Path>().FirstOrDefault() is not { } line
            || face.Children.OfType<StackPanel>().FirstOrDefault()?.Children.OfType<TextBlock>().FirstOrDefault() is not { } figure) return null;
        double c = RingSize / 2, rad = (RingSize - RingStroke) / 2;
        var arc = new ArcSegment(new Point(c, c + rad), new Size(rad, rad), 0, false, SweepDirection.Clockwise, true);
        var fig = new PathFigure { StartPoint = new Point(c, c - rad), IsClosed = false, IsFilled = false };
        fig.Segments.Add(arc);
        var geo = new PathGeometry();
        geo.Figures.Add(fig);
        line.Data = geo;
        _ringHost.Children.Clear();
        _ringHost.Children.Add(face);
        return new SweepRing { Arc = arc, Line = line, Figure = figure };
    }

    /// <summary>
    /// The ring of your best here at <paramref name="share"/> (-1: none). On a pickup just made it sweeps round from the share
    /// last shown; any other change (the clock moving the hour, a price refresh) is set as it is. Passing your best, it turns
    /// mint and pulses once when the sweep gets there.
    /// </summary>
    void DrawRing(double share, bool beat, bool picked)
    {
        var key = _key + "ring";
        if (share < 0)
        {
            Motion.Halt(_ringHost);
            _ringHost.Children.Clear();
            _sweep = null;
            Motion.ForgetShare(key);
            _ringDrawn = _wasBeat = _pulseDue = false;
            return;
        }
        if (beat && !_wasBeat && _ringDrawn && Moves) _pulseDue = true;
        if (!Moves) RingAt(share, beat);
        else
        {
            if (!picked && !Motion.Playing(_ringHost)) Motion.ForgetShare(key);
            Motion.Slide(_ringHost, key, share, s => RingStep(s, share, beat));
        }
        _wasBeat = beat; _ringDrawn = true;
    }

    /// <summary>A rare drop on the frame (Alert): its line comes in, the frame beats twice in its colour, its spark pops and sparks burst from it.</summary>
    void AlertLanded(Color tone)
    {
        _alertTone = tone;
        Fade(_alert, _alertSign);
        if (Moves) AlertBeat();
    }

    /// <summary>
    /// The frame's colour beats twice and holds (1.4 s), the spark the line starts with popping and glowing with the first
    /// beat, and a burst of sparks in the drop's colour flying out from it in the first 650 ms (under the sign, in its row,
    /// taking no room: they reach a quarter of the sign past its edge, inside the panel's padding).
    /// </summary>
    void AlertBeat()
    {
        var tone = _alertTone;
        if (_frame.BorderBrush is not SolidColorBrush { IsFrozen: false } edge) return;
        var spark = new ScaleTransform(SparkPop, SparkPop);
        _alertSign.RenderTransformOrigin = new Point(0.5, 0.5);
        _alertSign.RenderTransform = spark;
        var glow = new DropShadowEffect { Color = tone, ShadowDepth = 0, BlurRadius = SparkBlur, Opacity = 0 };
        _alertSign.Effect = glow;
        var row = _alertSign.Parent as Panel;
        foreach (var old in row?.Children.OfType<Canvas>().ToList() ?? new()) row!.Children.Remove(old);   // an earlier drop's burst cut short
        var burst = Motion.BurstSparks(tone, _alertSign.Width, BurstDot);
        burst.Margin = new Thickness(0, 0, -_alertSign.Width, 0);
        row?.Children.Insert(0, burst);
        double sparkEnd = Motion.SparkSpan(AlertBeatMs);
        Motion.Play(_frame, AlertBeatMs, p =>
        {
            double k = Motion.Beats(p);
            edge.Color = Color.FromArgb((byte)Math.Round(255 * k), tone.R, tone.G, tone.B);
            double settle = Math.Clamp(p * AlertBeatMs / SparkSettleMs, 0, 1);
            spark.ScaleX = spark.ScaleY = 1 + (SparkPop - 1) * (1 - Motion.Out(settle));
            glow.Opacity = k * (1 - p * p);   // the spark's glow beats with the frame and is gone by the end
            Motion.Sparks(burst, Motion.Span(p, 0, sparkEnd), BurstReach);
        }, () =>
        {
            edge.Color = tone;
            _alertSign.ClearValue(UIElement.RenderTransformProperty);
            if (_alertSign.Effect == glow) _alertSign.ClearValue(UIElement.EffectProperty);
            row?.Children.Remove(burst);
        });
    }

    /* ---------------- the state lines' cross-fade ---------------- */

    /// <summary>What a state line shows: whether it shows at all, its sign, its colour and its words, run by run with each one's own colour.</summary>
    sealed class LineFace
    {
        public required bool Shown;
        public ImageSource? Sign;
        public Brush? Fg;
        public required (string Text, object Fg)[] Runs;

        /// <summary>The same line on screen (two hidden lines are the same whatever they held).</summary>
        public bool Same(LineFace o) => Shown == o.Shown && (!Shown || (Sign == o.Sign && Tone(Fg) == Tone(o.Fg) && Runs.Length == o.Runs.Length
            && Runs.Zip(o.Runs).All(x => x.First.Text == x.Second.Text && Tone(x.First.Fg) == Tone(x.Second.Fg))));

        static Color? Tone(object? b) => (b as SolidColorBrush)?.Color;
    }

    /// <summary>A state line's fade: the line it shows and the one it goes to, and whether the old one is still on screen.</summary>
    sealed class LineFade
    {
        public LineFace? From, To;
        public bool ShowingFrom;
    }

    static LineFace FaceOf(TextBlock text, Image sign) => new()
    {
        Shown = text.Visibility == Visibility.Visible, Sign = sign.Source, Fg = text.Foreground,
        Runs = text.Inlines.OfType<Run>().Select(r => (r.Text, r.ReadLocalValue(TextElement.ForegroundProperty))).ToArray(),
    };

    static void Show(LineFace face, TextBlock text, Image sign)
    {
        text.Inlines.Clear();
        foreach (var (t, fg) in face.Runs)
        {
            var r = new Run(t);
            if (fg is Brush b) r.Foreground = b;
            text.Inlines.Add(r);
        }
        text.Foreground = face.Fg;
        sign.Source = face.Sign;
        text.Visibility = face.Shown ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// A state line just written by its owner (Update's notice, SetAccuracy, Alert and its end): when it changed, the old line
    /// fades out and the new one fades in, rising into its place; one that comes fades in, one that goes fades out first.
    /// Not on the panel's first draw. While the old line fades, its owner writing the new one again leaves the old on screen.
    /// </summary>
    void Fade(TextBlock text, Image sign)
    {
        if (_still || _look != null) return;
        if (!_fades.TryGetValue(text, out var f)) _fades[text] = f = new LineFade();
        var row = (FrameworkElement)text.Parent;
        var now = FaceOf(text, sign);
        if (f.To == null) { f.To = now; return; }
        if (now.Same(f.To))
        {
            if (f.ShowingFrom && Motion.Playing(row)) Show(f.From!, text, sign);
            return;
        }
        var from = f.ShowingFrom && Motion.Playing(row) ? f.From! : f.To;
        f.To = now;
        if (!Moves || (!from.Shown && !now.Shown)) { Settle(f, row); return; }
        CrossFade(f, from, row, text, sign);
    }

    /// <summary>The line as it is now, with no fade under way.</summary>
    static void Settle(LineFade f, FrameworkElement row)
    {
        Motion.Halt(row);
        f.ShowingFrom = false;
        row.Opacity = 1;
        row.ClearValue(UIElement.RenderTransformProperty);
    }

    /// <summary>
    /// One cross-fade on a line's row, sign and words together: <paramref name="from"/> out (from where its fade had got to)
    /// over the first part, then the line it goes to in, rising <see cref="LineRise"/> px into its place.
    /// </summary>
    void CrossFade(LineFade f, LineFace from, FrameworkElement row, TextBlock text, Image sign)
    {
        var to = f.To!;
        f.From = from;
        f.ShowingFrom = false;   // its owner has just written the new line
        double outMs = from.Shown ? LineOutMs : 0, inMs = to.Shown ? LineInMs : 0, ms = outMs + inMs, cut = outMs / ms;
        double start = row.Opacity;
        var rise = new TranslateTransform();
        row.RenderTransform = rise;
        Motion.Play(row, ms, p =>
        {
            if (p < cut)
            {
                if (!f.ShowingFrom) { Show(from, text, sign); f.ShowingFrom = true; }
                row.Opacity = start * (1 - Motion.Out(p / cut));
                rise.Y = 0;
                return;
            }
            if (f.ShowingFrom) { Show(to, text, sign); f.ShowingFrom = false; }
            double q = cut >= 1 ? 1 : Motion.Out((p - cut) / (1 - cut));
            row.Opacity = q;
            rise.Y = LineRise * (1 - q);
        }, () =>
        {
            if (f.ShowingFrom) Show(to, text, sign);
            f.ShowingFrom = false;
            row.Opacity = 1;
            row.ClearValue(UIElement.RenderTransformProperty);
        });
    }

    /// <summary>For the options self-test: the hot streak bar along the bottom edge.</summary>
    internal StreakBar StreakForTest => _streak;

    /// <summary>
    /// For a render of the motion (KL_MOTION_AT): the figures flipping in from 85%, the streak bar filling from half its
    /// value, the edge lit, the ring swept from 70% of its share (pulsing past your best), the newest pickup landing, each
    /// state line showing coming in, and a rare drop's frame beating with its burst.
    /// </summary>
    void BadgeFrame(IReadOnlyDictionary<string, string> values)
    {
        string V(string k) => values.TryGetValue(k, out var v) ? v : "";
        foreach (var (k, tint) in Rolled)
            if (_cells.TryGetValue(k, out var tb) && HudTicker.Num(V(k)) is double v && v > 0) Motion.Replay(tb, _key + k, v * ReplayFrom, v, Amounts.Short, tint);
        if (_feedLine != null && _feedRate != null && HudTicker.Num(V("feedRate")) is double r && r > 0) Motion.Replay(_feedRate, _key + "feedRate", r * ReplayFrom, r, Amounts.Short, RiseTint);
        _streak.Replay(values, 0.5);
        if (_alert.Visibility != Visibility.Visible) Motion.EdgeLight(_frame, Rgb(GoldC), FrameBrush);
        var (share, _, beat) = Chase(values);
        if (share >= 0 && V("warm") != "1")
        {
            // past your best the pulse is what plays (it follows the sweep, which a held frame can't show both of)
            if (beat) { RingAt(share, true); Motion.Pulse(_ringHost, _chaseLine, Rgb(MintC), BestSwell, BestBlur); }
            else Motion.ReplaySlide(_ringHost, _key + "ring", share * RingReplayFrom, share, s => RingStep(s, share, false));
        }
        PickupLanded(ToneOf(Picks(values).FirstOrDefault()));   // the Strip's render states carry no pickups: a common one's mint then
        foreach (var (text, sign) in new[] { (_note, _noteSign), (_acc, _accSign), (_alert, _alertSign) })
        {
            if (text.Visibility != Visibility.Visible) continue;
            if (!_fades.TryGetValue(text, out var f)) _fades[text] = f = new LineFade();
            f.To = FaceOf(text, sign);
            CrossFade(f, new LineFace { Shown = false, Runs = Array.Empty<(string, object)>() }, (FrameworkElement)text.Parent, text, sign);
        }
        if (_alert.Visibility == Visibility.Visible) AlertBeat();
    }
}
