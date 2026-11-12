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
/// - on a pickup the hour, Kinah, loot, best and session figures roll to their new values, the per-hour ones green for a
///   moment as they rise, and the panel's edge lights gold;
/// - the ring of your best here sweeps round to its new share, and swells and glows mint once the moment you pass your best;
/// - a new pickup's worth pops and the feed's live dot flashes in its grade's colour (gold for Kinah);
/// - a rare drop's coloured frame beats twice as it lands, its spark popping with it;
/// - the notice, accuracy and rare drop lines cross-fade when they change, fade in when they come and out when they go.
/// </summary>
public sealed partial class HudWindow
{
    /// <summary>A drawing (the layout renders' strip, the Options preview): nothing of Badge moves.</summary>
    readonly bool _still;
    /// <summary>This panel's own figure keys: the Options preview never rolls the overlay's.</summary>
    readonly string _key = "hud-strip|" + Guid.NewGuid().ToString("N")[..8] + "|";
    string _lastTotal = "", _pickId = "";
    DateTime _pickAt = DateTime.MinValue;
    bool _picksSeen, _ringDrawn, _wasBeat;
    /// <summary>The colour of the rare drop on the frame now, which its beat runs up to.</summary>
    Color _alertTone;
    /// <summary>The feed dot's glow at rest, which its flash settles back to.</summary>
    double _dotGlow;
    readonly Dictionary<TextBlock, LineFade> _fades = new();

    bool Moves => !_still && _look == null && Motion.On;

    /// <summary>The figures that roll on a pickup, with the rise that tints them green: the per-hour ones and your best do, the session doesn't.</summary>
    static readonly (string Key, double TintRise)[] Rolled = { ("rate", RiseTint), ("kinah", RiseTint), ("loot", RiseTint), ("best", RiseTint), ("total", NoTint) };
    const double RiseTint = 0.01, NoTint = -1;

    /// <summary>The ring on Badge (smaller than Buddy's): its size and line.</summary>
    const double RingSize = 30, RingStroke = 3.5;
    /// <summary>Past your best: the ring swells this much and glows this wide (inside the panel's padding).</summary>
    const double BestSwell = 0.12, BestBlur = 10;
    /// <summary>A new pickup's worth pops from this size; the last pickup's name from this one (at its 240 px widest, still inside the panel's right padding).</summary>
    const double WorthPop = 1.15, LastPop = 1.06;
    /// <summary>The feed's live dot on a pickup: it swells to this many times its size and is back in this long.</summary>
    const double DotSwell = 2, DotMs = 700;
    /// <summary>The share of the dot's course spent lighting up; the rest fades back.</summary>
    const double DotPeak = 0.25;
    /// <summary>A pickup older than this when it reaches the panel is history (a session loaded), not one just made.</summary>
    static readonly TimeSpan FreshFor = TimeSpan.FromSeconds(10);
    /// <summary>A rare drop's beat on the frame, and its spark's pop.</summary>
    const double AlertBeatMs = 1400, SparkPop = 1.5, SparkBlur = 8;
    /// <summary>A state line fades out in this long and the next fades in in this long, rising this far into its place.</summary>
    const double LineOutMs = 180, LineInMs = 240, LineRise = 3;
    /// <summary>A render's held frame: the figures rolled from this share of their value, the ring swept from this share of its own.</summary>
    const double ReplayFrom = 0.85, RingReplayFrom = 0.7;

    /// <summary>The session total rose since the last update (a pickup), from a figure above nothing: a session just loaded doesn't count.</summary>
    bool TotalRose(string total)
    {
        bool rose = total != _lastTotal && HudTicker.Num(total) is double t && HudTicker.Num(_lastTotal) is double was && was > 0 && t > was;
        _lastTotal = total;
        return rose;
    }

    /// <summary>
    /// Badge's motion for this update, after it is drawn: the figures roll on a pickup, a pickup just made lands, the edge
    /// lights and the notice line cross-fades when it changed.
    /// </summary>
    void BadgeMoved(IReadOnlyDictionary<string, string> values, bool rose)
    {
        if (_still) return;
        string V(string k) => values.TryGetValue(k, out var v) ? v : "";
        foreach (var (k, tint) in Rolled)
            if (_cells.TryGetValue(k, out var tb)) Motion.Figure(tb, _key + k, V(k), tint, rose && Moves);
        if (_feedLine != null && _feedRate != null) Motion.Figure(_feedRate, _key + "feedRate", V("feedRate"), RiseTint, rose && Moves);
        var fresh = FreshPickup(Picks(values).FirstOrDefault());
        bool landed = fresh != null && Moves;
        if (landed) PickupLanded(ToneOf(fresh));
        if ((rose || landed) && Moves && _alert.Visibility != Visibility.Visible) Motion.EdgeLight(_frame, Rgb(GoldC), FrameBrush);
        Fade(_note, _noteSign);
        // the lines Update doesn't write are as they stand on the first draw, so the first rare drop or accuracy line fades in
        Seed(_acc, _accSign); Seed(_alert, _alertSign);
    }

    /// <summary>A state line's face noted as it stands, if it hasn't been yet (the panel's first draw).</summary>
    void Seed(TextBlock text, Image sign)
    {
        if (!_fades.ContainsKey(text)) _fades[text] = new LineFade { To = FaceOf(text, sign) };
    }

    /// <summary>The newest pickup when it is one just made: not on the first draw, not an older one (an Undo), not one from minutes ago (a session loaded).</summary>
    Pick? FreshPickup(Pick? front)
    {
        bool fresh = _picksSeen && front != null && front.Id != _pickId && front.At > _pickAt && DateTime.UtcNow - front.At < FreshFor;
        _picksSeen = true;
        _pickId = front?.Id ?? "";
        if (front != null) _pickAt = front.At;
        return fresh ? front : null;
    }

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
            double up = p < DotPeak ? Motion.Out(p / DotPeak) : 1 - Motion.Out((p - DotPeak) / (1 - DotPeak));
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

    /// <summary>The ring at a share: the gold share of your best here, or mint with a star once it is beaten.</summary>
    void RingAt(double s, bool beat)
    {
        _ringHost.Children.Clear();
        _ringHost.Children.Add(Ring(RingSize, RingStroke, s, Rgb(beat ? MintC : GoldC), beat ? "★" : $"{Math.Round(s * 100):0}"));
    }

    /// <summary>
    /// The ring of your best here at <paramref name="share"/> (-1: none). On a pickup it sweeps round from the share last shown;
    /// the clock moving the hour's share a point is set as it is. The moment it passes your best, one mint pulse.
    /// </summary>
    void DrawRing(double share, bool beat, bool rose)
    {
        var key = _key + "ring";
        if (share < 0)
        {
            Motion.Halt(_ringHost);
            _ringHost.Children.Clear();
            Motion.ForgetShare(key);
            _ringDrawn = _wasBeat = false;
            return;
        }
        if (!Moves) RingAt(share, beat);
        else
        {
            if (!rose && !Motion.Playing(_ringHost)) Motion.ForgetShare(key);
            Motion.Slide(_ringHost, key, share, s => RingAt(s, beat));
        }
        if (beat && !_wasBeat && _ringDrawn && Moves) Motion.Pulse(_ringHost, _chaseLine, Rgb(MintC), BestSwell, BestBlur);
        _wasBeat = beat; _ringDrawn = true;
    }

    /// <summary>A rare drop on the frame (Alert): its line comes in, the frame beats twice in its colour and its spark pops.</summary>
    void AlertLanded(Color tone)
    {
        _alertTone = tone;
        Fade(_alert, _alertSign);
        if (Moves) AlertBeat();
    }

    /// <summary>The frame's colour beats twice and holds (1.4 s), the spark the line starts with popping and glowing with the first beat.</summary>
    void AlertBeat()
    {
        var tone = _alertTone;
        if (_frame.BorderBrush is not SolidColorBrush { IsFrozen: false } edge) return;
        var spark = new ScaleTransform(SparkPop, SparkPop);
        _alertSign.RenderTransformOrigin = new Point(0.5, 0.5);
        _alertSign.RenderTransform = spark;
        var glow = new DropShadowEffect { Color = tone, ShadowDepth = 0, BlurRadius = SparkBlur, Opacity = 0 };
        _alertSign.Effect = glow;
        Motion.Play(_frame, AlertBeatMs, p =>
        {
            double k = Motion.Beats(p);
            edge.Color = Color.FromArgb((byte)Math.Round(255 * k), tone.R, tone.G, tone.B);
            double settle = Math.Clamp(p * AlertBeatMs / Motion.PopMs, 0, 1);
            spark.ScaleX = spark.ScaleY = 1 + (SparkPop - 1) * (1 - Motion.Out(settle));
            glow.Opacity = k * (1 - p * p);   // the spark's glow beats with the frame and is gone by the end
        }, () =>
        {
            edge.Color = tone;
            _alertSign.ClearValue(UIElement.RenderTransformProperty);
            if (_alertSign.Effect == glow) _alertSign.ClearValue(UIElement.EffectProperty);
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

    /// <summary>
    /// For a render of the motion (KL_MOTION_AT): the figures risen from 85%, the edge lit, the ring swept from 70% of its
    /// share (pulsing past your best), the newest pickup landing, each state line showing coming in, and a rare drop's frame beating.
    /// </summary>
    void BadgeFrame(IReadOnlyDictionary<string, string> values)
    {
        string V(string k) => values.TryGetValue(k, out var v) ? v : "";
        foreach (var (k, tint) in Rolled)
            if (_cells.TryGetValue(k, out var tb) && HudTicker.Num(V(k)) is double v && v > 0) Motion.Replay(tb, _key + k, v * ReplayFrom, v, Amounts.Short, tint);
        if (_feedLine != null && _feedRate != null && HudTicker.Num(V("feedRate")) is double r && r > 0) Motion.Replay(_feedRate, _key + "feedRate", r * ReplayFrom, r, Amounts.Short, RiseTint);
        if (_alert.Visibility != Visibility.Visible) Motion.EdgeLight(_frame, Rgb(GoldC), FrameBrush);
        var (share, _, beat) = Chase(values);
        if (share >= 0 && V("warm") != "1")
        {
            Motion.ReplaySlide(_ringHost, _key + "ring", share * RingReplayFrom, share, s => RingAt(s, beat));
            if (beat) Motion.Pulse(_ringHost, _chaseLine, Rgb(MintC), BestSwell, BestBlur);
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
