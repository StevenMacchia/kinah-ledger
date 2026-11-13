using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Effects;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

/// <summary>
/// Buddy's motion (the Card look; Steven, 2026-11-11: "doesnt look like the in-app buddy got much animation or cool things?"),
/// the Ticker's kin (Ui/HudTicker.Motion.cs). Once per change, nothing loops; through Ui/Motion.cs, so a render or the Options
/// preview (Still) and Windows's animations off show none of it.
/// Since 2026-11-12 it moves like the game's own pop-ups (Steven: "like the game's own UI"): short, eased out, small.
/// - the hour and the session flip to their new figures on a pickup, character by character (Ui/Motion.Flip.cs), the hour
///   green for a moment as it rises, and the card's edge lights softly gold;
/// - the hot streak bar under the figures fills as pickups come quickly and drains as they slow (Ui/HudStreak.cs);
/// - the ring of your best here sweeps round to its new share, its percent counting with it, and pulses once in mint the
///   moment you pass your best;
/// - the newest pickup's tile grows in (a rare grade's glows once) and its name and worth in the head pop from 110%;
/// - a rare drop's coloured frame beats twice as it lands, and the drop over the figures makes its entrance with its
///   sparks (Ui/HudRare.Motion.cs).
/// </summary>
public sealed partial class HudCard
{
    /// <summary>This card's own keys: the Options preview's card never moves the overlay's.</summary>
    readonly string _key = "hud-card|" + Guid.NewGuid().ToString("N")[..8] + "|";
    string _lastTotal = "", _landedId = "", _lastRare = "";
    bool _wasBeat, _ringDrawn;
    /// <summary>The figures' runs as last built, which a roll under way writes into (the figures are rebuilt as the clock moves).</summary>
    Run? _rateRun, _totalRun;
    readonly Dictionary<string, double> _shown = new();
    /// <summary>The hot streak bar under the figures, this far below them (into the card's padding).</summary>
    readonly StreakBar _streak = new();
    const double StreakBelow = -4;
    /// <summary>Past your best the ring swells this much and glows this strong at most; a rare frame's glow dips to this between its beats.</summary>
    const double BestSwell = 0.10, BestGlow = 0.6, RareLow = .55;

    bool Moves => !Still && Motion.On;

    /// <summary>The session total rose since the last update (a pickup): the figures roll and the edge lights.</summary>
    bool TotalRose(string total)
    {
        bool rose = _lastTotal.Length > 0 && total != _lastTotal && HudTicker.Num(total) is double t && HudTicker.Num(_lastTotal) is double was && t > was;
        _lastTotal = total;
        return rose;
    }

    /// <summary>
    /// A figure's run (just built with <paramref name="text"/>) flipped in from the figure last shown (on a pickup), its changed
    /// characters coming up into place right to left (Ui/Motion.Flip.cs), the hour tinted green as it rises; else just noted,
    /// so the next flip starts from it.
    /// </summary>
    void Roll(TextBlock host, string part, Func<Run?> run, string text, bool roll, bool tint, bool force = false)
    {
        if (HudTicker.Num(text) is not double v || v <= 0) { _shown.Remove(part); return; }
        bool had = _shown.TryGetValue(part, out var from);
        _shown[part] = v;
        if (!roll || !had || !(Moves || force) || from <= 0 || Amounts.Short(from) == text) return;
        // rolled through its values again, not flipped (Steven, 2026-11-12, of 0.10.57: "blinks/flashes instead of rolling")
        var gold = Rgb(GoldC); var good = Rgb(MintC);
        double ms = tint ? Motion.RollMs * 2 : Motion.RollMs;
        Motion.Play(host, ms, p =>
        {
            double q = Motion.Out(p * ms / Motion.RollMs);
            var shown = from + (v - from) * q;
            _shown[part] = shown;
            if (run() is not { } r) return;
            r.Text = p >= 1 ? text : Amounts.Short(shown);
            if (tint) r.Foreground = new SolidColorBrush(Motion.Blend(good, gold, Math.Clamp((p - 0.25) / 0.75, 0, 1)));
        }, () => { _shown[part] = v; if (run() is { } r) { r.Text = text; if (tint) r.Foreground = Gold; } });
    }

    /// <summary>The card's edge lights softly gold on a pickup and fades back to its frame line (600 ms); not while a rare drop's frame holds it.</summary>
    void EdgeGlow()
    {
        if (_ring.Visibility == Visibility.Visible) return;
        Motion.EdgeLight(_card, Rgb(GoldC), FrameLine);
    }

    /// <summary>The pickups seen, to tell one just made from a price refresh, a session loaded or cleared, or an Undo.</summary>
    readonly FreshPicks _picks = new();

    /// <summary>
    /// The ring of your best here at <paramref name="share"/>: on a pickup just made (<paramref name="rose"/>) swept round from
    /// the share last shown (600 ms, its percent counting with it), any other change set as it is; the moment it passes your
    /// best, one mint pulse.
    /// </summary>
    void DrawRing(double share, bool beat, bool rose)
    {
        var tone = Rgb(GoldC);   // gold beaten or not, as the window's ring (colour pass, 2026-11-13); the star and the pulse say it is beaten
        void Draw(double s)
        {
            _ringHost.Children.Clear();
            _ringHost.Children.Add(Ring(44, 4.5, s, tone, beat ? "★" : $"{Math.Round(Math.Min(s, 1) * 100):0}", beat ? "best" : "of best"));
        }
        if (Moves && !rose && !Motion.Playing(_ringHost)) Motion.ForgetShare(_key + "ring");   // set as it is: no slide
        if (Moves) Motion.Slide(_ringHost, _key + "ring", share, Draw); else Draw(share);
        if (beat && !_wasBeat && _ringDrawn && Moves) BestPulse();
        _wasBeat = beat; _ringDrawn = true;
    }

    /// <summary>Past your best: the ring swells 10% and glows mint once (900 ms).</summary>
    void BestPulse()
    {
        var host = _ringHost;
        var s = new ScaleTransform(1, 1);
        host.RenderTransformOrigin = new Point(0.5, 0.5);
        host.RenderTransform = s;
        var glow = new DropShadowEffect { Color = Rgb(MintC), ShadowDepth = 0, BlurRadius = 22, Opacity = 0 };
        host.Effect = glow;
        // its own course (on the chase line), so the ring's sweep on the host and the session's roll run alongside
        Motion.Play(_chase, 900, p =>
        {
            double up = p < 0.3 ? Motion.Out(p / 0.3) : 1 - Motion.Out((p - 0.3) / 0.7);
            s.ScaleX = s.ScaleY = 1 + BestSwell * up;
            glow.Opacity = BestGlow * up;
        }, () => { host.ClearValue(UIElement.RenderTransformProperty); if (host.Effect == glow) host.ClearValue(UIElement.EffectProperty); });
    }

    /// <summary>A new pickup: its tile grows in (a rare grade glows once) and the head's name and worth pop from 110%.</summary>
    void Arrived(FrameworkElement tile, Pick p)
    {
        Motion.Land(tile, false, p.Rare || Motion.Glows(p.Grade) ? p.Grade : null);
        Motion.Pop(_newest, Motion.PopFrom, new Point(1, 0.5));
    }

    /// <summary>A rare drop landing: its coloured frame's glow beats twice and settles at its held strength (1.4 s).</summary>
    void RareBeat()
    {
        Motion.Play(_ring, 1400, p =>
        {
            double beat = Math.Abs(Math.Sin(p * Math.PI * 2.5));
            _ring.Opacity = p < 0.8 ? RareLow + (1 - RareLow) * beat : 1;
        }, () => _ring.Opacity = 1);
    }

    /// <summary>
    /// For a render of the motion (KL_MOTION_AT): the figures flipping in from 85%, the streak bar filling from half its value,
    /// the ring swept from 70% of its share, the newest tile landing, a rare frame beating and its drop making its entrance.
    /// </summary>
    internal void MotionFrame(IReadOnlyDictionary<string, string> values)
    {
        string V(string k) => values.TryGetValue(k, out var v) ? v : "";
        if (HudTicker.Num(V("rate")) is double r) { _shown["rate"] = r * 0.85; Roll(_rate, "rate", () => _rateRun, V("rate"), true, true, force: true); }
        if (HudTicker.Num(V("total")) is double t) { _shown["total"] = t * 0.85; Roll(_total, "total", () => _totalRun, V("total"), true, false, force: true); }
        _streak.Replay(values, 0.5);
        EdgeGlow();
        var (share, _, beat) = Chase(values);
        if (share >= 0)
        {
            var tone = Rgb(GoldC);
            Motion.ReplaySlide(_ringHost, _key + "ring", share * 0.7, share, s => { _ringHost.Children.Clear(); _ringHost.Children.Add(Ring(44, 4.5, s, tone, beat ? "★" : $"{Math.Round(Math.Min(s, 1) * 100):0}", beat ? "best" : "of best")); });
        }
        if (_tiles.Children.Count > 0 && _tiles.Children[0] is FrameworkElement t0 && Picks(values).FirstOrDefault() is { } fp) Arrived(t0, fp);
        if (_ring.Visibility == Visibility.Visible) RareBeat();
        if (_drop.Made is { } made) HudRare.Enter(made);
    }

    /// <summary>For the options self-test: the rare drop drawn over the figures, while one shows.</summary>
    internal FrameworkElement? RareDropForTest => _drop.Made;

    /// <summary>For the options self-test: the hour's and the session's runs as last built, and the hot streak bar.</summary>
    internal (Run? Rate, Run? Total, StreakBar Streak) FiguresForTest => (_rateRun, _totalRun, _streak);
}
