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
/// - the hour and the session roll to their new figures on a pickup, the hour green for a moment as it rises, and the card's
///   edge lights gold;
/// - the ring of your best here sweeps round to its new share, its percent counting with it, and pulses once in mint the
///   moment you pass your best;
/// - the newest pickup's tile grows in (a rare grade's glows once) and its name and worth in the head pop;
/// - a rare drop's coloured frame beats twice as it lands.
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

    bool Moves => !Still && Motion.On;

    /// <summary>The session total rose since the last update (a pickup): the figures roll and the edge lights.</summary>
    bool TotalRose(string total)
    {
        bool rose = _lastTotal.Length > 0 && total != _lastTotal && HudTicker.Num(total) is double t && HudTicker.Num(_lastTotal) is double was && t > was;
        _lastTotal = total;
        return rose;
    }

    /// <summary>
    /// A figure's run rolled from the figure last shown to <paramref name="text"/> (on a pickup), the hour tinted green as it
    /// rises; else just noted, so the next roll starts from it.
    /// </summary>
    void Roll(TextBlock host, string part, Func<Run?> run, string text, bool roll, bool tint, bool force = false)
    {
        if (HudTicker.Num(text) is not double v || v <= 0) { _shown.Remove(part); return; }
        bool had = _shown.TryGetValue(part, out var from);
        _shown[part] = v;
        if (!roll || !had || !(Moves || force) || from <= 0 || Amounts.Short(from) == text) return;
        var gold = Rgb(GoldC); var good = Rgb(MintC);
        double ms = tint ? Motion.RollMs * 2 : Motion.RollMs;
        Motion.Play(host, ms, p =>
        {
            double q = Motion.Out(p * ms / Motion.RollMs);
            var shown = from + (v - from) * q;
            _shown[part] = shown;
            if (run() is not { } r) return;
            r.Text = p >= 1 ? text : Amounts.Short(shown);
            if (tint) { double k = Math.Clamp((p - 0.25) / 0.75, 0, 1); r.Foreground = new SolidColorBrush(Mix(good, gold, k)); }
        }, () => { _shown[part] = v; if (run() is { } r) { r.Text = text; if (tint) r.Foreground = Gold; } });
    }

    static Color Mix(Color a, Color b, double t) => Color.FromArgb(255, (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

    /// <summary>The card's edge lights gold on a pickup and fades back to its frame line (900 ms); not while a rare drop's frame holds it.</summary>
    void EdgeGlow()
    {
        if (_ring.Visibility == Visibility.Visible) return;
        var gold = Rgb(GoldC); var line = (FrameLine as SolidColorBrush)?.Color ?? Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF);
        var b = new SolidColorBrush(gold);
        _card.BorderBrush = b;
        Motion.Play(_card, 900, p =>
        {
            double a = 1 - Motion.Out(p);
            b.Color = Color.FromArgb((byte)(line.A + (0xE6 - line.A) * a), (byte)(line.R + (gold.R - line.R) * a), (byte)(line.G + (gold.G - line.G) * a), (byte)(line.B + (gold.B - line.B) * a));
        }, () => { if (_ring.Visibility != Visibility.Visible) _card.BorderBrush = FrameLine; });
    }

    /// <summary>
    /// The ring of your best here at <paramref name="share"/>: swept round from the share last shown (600 ms, its percent counting
    /// with it); the moment it passes your best, one mint pulse.
    /// </summary>
    void DrawRing(double share, bool beat)
    {
        var tone = Rgb(beat ? MintC : GoldC);
        void Draw(double s)
        {
            _ringHost.Children.Clear();
            _ringHost.Children.Add(Ring(44, 4.5, s, tone, beat ? "★" : $"{Math.Round(Math.Min(s, 1) * 100):0}", beat ? "best" : "of best"));
        }
        if (Moves) Motion.Slide(_ringHost, _key + "ring", share, Draw); else Draw(share);
        if (beat && !_wasBeat && _ringDrawn && Moves) BestPulse();
        _wasBeat = beat; _ringDrawn = true;
    }

    /// <summary>Past your best: the ring swells 18% and glows mint once (900 ms).</summary>
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
            s.ScaleX = s.ScaleY = 1 + 0.18 * up;
            glow.Opacity = 0.95 * up;
        }, () => { host.ClearValue(UIElement.RenderTransformProperty); if (host.Effect == glow) host.ClearValue(UIElement.EffectProperty); });
    }

    /// <summary>A new pickup: its tile grows in (a rare grade glows once) and the head's name and worth pop from 115%.</summary>
    void Arrived(FrameworkElement tile, Pick p)
    {
        Motion.Land(tile, false, p.Rare || Motion.Glows(p.Grade) ? p.Grade : null);
        var s = new ScaleTransform(1.15, 1.15);
        _newest.RenderTransformOrigin = new Point(1, 0.5);
        _newest.RenderTransform = s;
        Motion.Play(_newest, 420, q => s.ScaleX = s.ScaleY = 1 + 0.15 * (1 - Motion.Out(q)), () => _newest.ClearValue(UIElement.RenderTransformProperty));
    }

    /// <summary>A rare drop landing: its coloured frame's glow beats twice and settles at its held strength (1.4 s).</summary>
    void RareBeat()
    {
        Motion.Play(_ring, 1400, p =>
        {
            double beat = Math.Abs(Math.Sin(p * Math.PI * 2.5));
            _ring.Opacity = p < 0.8 ? .3 + .7 * beat : 1;
        }, () => _ring.Opacity = 1);
    }

    /// <summary>For a render of the motion (KL_MOTION_AT): the figures risen from 85%, the ring swept from 70% of its share, the newest tile landing, a rare frame beating.</summary>
    internal void MotionFrame(IReadOnlyDictionary<string, string> values)
    {
        string V(string k) => values.TryGetValue(k, out var v) ? v : "";
        if (HudTicker.Num(V("rate")) is double r) { _shown["rate"] = r * 0.85; Roll(_rate, "rate", () => _rateRun, V("rate"), true, true, force: true); }
        if (HudTicker.Num(V("total")) is double t) { _shown["total"] = t * 0.85; Roll(_total, "total", () => _totalRun, V("total"), true, false, force: true); }
        EdgeGlow();
        var (share, _, beat) = Chase(values);
        if (share >= 0)
        {
            var tone = Rgb(beat ? MintC : GoldC);
            Motion.ReplaySlide(_ringHost, _key + "ring", share * 0.7, share, s => { _ringHost.Children.Clear(); _ringHost.Children.Add(Ring(44, 4.5, s, tone, beat ? "★" : $"{Math.Round(Math.Min(s, 1) * 100):0}", beat ? "best" : "of best")); });
        }
        if (_tiles.Children.Count > 0 && _tiles.Children[0] is FrameworkElement t0 && Picks(values).FirstOrDefault() is { } fp) Arrived(t0, fp);
        if (_ring.Visibility == Visibility.Visible) RareBeat();
    }
}
