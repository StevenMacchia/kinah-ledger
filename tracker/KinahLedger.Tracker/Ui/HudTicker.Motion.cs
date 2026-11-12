using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

/// <summary>
/// The Ticker's motion (Steven, 2026-11-11: "i want more animation in the overlay in the game"), on top of the toasts
/// sliding in and fading out. Each plays once when something changes; nothing loops, so the overlay costs the game
/// nothing while it sits still. Through Ui/Motion.cs, so a render (Still) and Windows's animations off show none of it.
/// - the pill's hour and session roll to their new figures on a pickup, the hour green for a moment as it rises, and
///   the pill's edge lights gold;
/// - the toasts already in the stack glide down to their new places when a new one comes in on top;
/// - a new pickup's worth pops and its tile grows in (a Legend, Unique, Epic or Special one glows once in its colour);
/// - a new top drop gets one gold sheen across it;
/// - a rare drop's glow beats twice as it lands.
/// </summary>
public sealed partial class HudTicker
{
    /// <summary>This Ticker's own figure keys: the Options preview's Ticker never rolls the overlay's.</summary>
    readonly string _key = "hud-ticker|" + Guid.NewGuid().ToString("N")[..8] + "|";
    string _lastTotal = "";

    bool Moves => !Still && Motion.On;

    /// <summary>"1.27M", "894K", "12,345" back to a number, when the overlay's own format writes it the same way; else null.</summary>
    internal static double? Num(string text)
    {
        var m = Regex.Match(text.Trim(), @"^([\d,]*\.?\d+)([KMB]?)$");
        if (!m.Success || !double.TryParse(m.Groups[1].Value.Replace(",", ""), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v)) return null;
        v *= m.Groups[2].Value switch { "K" => 1e3, "M" => 1e6, "B" => 1e9, _ => 1 };
        return Amounts.Short(v) == text.Trim() ? v : null;
    }

    /// <summary>A pill figure: rolled when the session total moved (a pickup), else set (the hour moving with the clock).</summary>
    void Fig(TextBlock tb, string part, string text, double tint, bool roll)
    {
        if (Num(text) is double v && v > 0) Motion.Number(tb, _key + part, v, Amounts.Short, tint, roll && Moves);
        else Motion.Blank(tb, _key + part, text);
    }

    /// <summary>The pickups seen, to tell one just made from a price refresh, a session loaded or cleared, or an Undo.</summary>
    readonly FreshPicks _picks = new();

    /// <summary>
    /// The pill's figures for this update; true when the session total rose with a pickup just made (<paramref name="picked"/>),
    /// so its edge lights. A total that moved any other way (a price refresh, another session) is set without a roll.
    /// </summary>
    bool PillFigures(string rate, string total, bool warm, bool picked)
    {
        bool rose = picked && _lastTotal.Length > 0 && total != _lastTotal && Num(total) is double t && Num(_lastTotal) is double was && t > was;
        _lastTotal = total;
        if (warm) Motion.Blank(_rate, _key + "rate", rate); else Fig(_rate, "rate", rate, 0.01, rose);
        Fig(_total, "total", total, -1, rose);
        if (rose && Moves) PillGlow();
        return rose;
    }

    /// <summary>The pill's edge lights gold and fades back to its frame line (900 ms).</summary>
    void PillGlow()
    {
        var gold = Rgb(GoldC); var line = (FrameLine as SolidColorBrush)?.Color ?? Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF);
        var b = new SolidColorBrush(gold);
        _pill.BorderBrush = b;
        Motion.Play(_pill, 900, p =>
        {
            double a = 1 - Motion.Out(p);
            b.Color = Color.FromArgb((byte)(line.A + (0xE6 - line.A) * a), (byte)(line.R + (gold.R - line.R) * a), (byte)(line.G + (gold.G - line.G) * a), (byte)(line.B + (gold.B - line.B) * a));
        }, () => _pill.BorderBrush = FrameLine);
    }

    /// <summary>Where each toast in the stack sits now, before it is laid out again (for the glide down).</summary>
    Dictionary<string, double> StackPlaces()
    {
        var at = new Dictionary<string, double>();
        if (!Moves || _stack.ActualHeight <= 0) return at;
        foreach (var (k, t) in _toasts)
            if (t.Place.Parent == _stack && t.Place.IsVisible) at[k] = t.Place.TranslatePoint(new Point(0, 0), _stack).Y;
        return at;
    }

    /// <summary>The toasts that stayed glide from where they were to their new places (220 ms), instead of jumping.</summary>
    void Glide(Dictionary<string, double> before)
    {
        if (before.Count == 0) return;
        _stack.UpdateLayout();
        foreach (var (k, t) in _toasts)
        {
            if (!before.TryGetValue(k, out var y0)) continue;
            double dy = y0 - t.Place.TranslatePoint(new Point(0, 0), _stack).Y;
            if (Math.Abs(dy) < 1) continue;
            var move = new TranslateTransform(0, dy);
            t.Place.RenderTransform = move;
            Motion.Play(t.Place, 220, p => move.Y = dy * (1 - Motion.Out(p)), () => t.Place.ClearValue(UIElement.RenderTransformProperty));
        }
    }

    /// <summary>A new pickup's toast: its worth pops from 135% and its tile grows in, a rare grade's with one glow.</summary>
    static void PickPop(Border body, Pick p)
    {
        if (body.Child is not Grid g) return;
        if (g.Children.OfType<TextBlock>().FirstOrDefault(x => Equals(x.Tag, "worth")) is { } worth)
        {
            var s = new ScaleTransform(1.35, 1.35);
            worth.RenderTransformOrigin = new Point(1, 0.5);
            worth.RenderTransform = s;
            Motion.Play(worth, 420, q => s.ScaleX = s.ScaleY = 1 + 0.35 * (1 - Motion.Out(q)), () => worth.ClearValue(UIElement.RenderTransformProperty));
        }
        if (g.Children.Count > 0 && g.Children[0] is FrameworkElement tile) Motion.Land(tile, false, p.Rare || Motion.Glows(p.Grade) ? p.Grade : null);
    }

    /// <summary>A new top drop: one gold sheen runs across the toast (900 ms).</summary>
    static void Sheen(Border body)
    {
        if (body.Child is not Grid g) return;
        var band = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        band.GradientStops.Add(new GradientStop(Rgb(GoldC, 0), 0));
        band.GradientStops.Add(new GradientStop(Rgb(GoldC, .38), .5));
        band.GradientStops.Add(new GradientStop(Rgb(GoldC, 0), 1));
        var move = new TranslateTransform(-120, 0);
        var r = new Rectangle { Width = 90, Fill = band, IsHitTestVisible = false, HorizontalAlignment = HorizontalAlignment.Left, RenderTransform = move, Margin = new Thickness(-6, -2, 0, -2) };
        Grid.SetColumnSpan(r, Math.Max(1, g.ColumnDefinitions.Count));
        g.ClipToBounds = true;
        g.Children.Add(r);
        Motion.Play(r, 900, p => move.X = -120 + 400 * Motion.Out(p), () => g.Children.Remove(r));
    }

    /// <summary>A rare drop landing: its glow beats twice and settles at its held strength (1.4 s); the toast swells 4% with the first beat.</summary>
    static void RareBeat(Border outer)
    {
        if (outer.Child is not Grid g || g.Children.Count < 2 || g.Children[0] is not FrameworkElement glow) return;
        var body = g.Children[1] as FrameworkElement;
        var s = new ScaleTransform(1, 1);
        if (body != null) { body.RenderTransformOrigin = new Point(0.5, 0.5); body.RenderTransform = s; }
        Motion.Play(glow, 1400, p =>
        {
            // two beats (up to 1, down to .35) then up to its held 1
            double beat = Math.Abs(Math.Sin(p * Math.PI * 2.5));
            glow.Opacity = p < 0.8 ? .35 + .65 * beat : 1;
            if (body != null) s.ScaleX = s.ScaleY = p < 0.2 ? 1 + 0.04 * Math.Sin(p / 0.2 * Math.PI) : 1;
        }, () => { glow.Opacity = 1; body?.ClearValue(UIElement.RenderTransformProperty); });
    }

    /// <summary>For a render of the motion (KL_MOTION_AT): the pill's figures as if the session had just risen from 85%, the stack's motions held.</summary>
    internal void MotionFrame(IReadOnlyDictionary<string, string> values)
    {
        string V(string k) => values.TryGetValue(k, out var v) ? v : "";
        if (Num(V("rate")) is double r) Motion.Replay(_rate, _key + "rate", r * 0.85, r, Amounts.Short, 0.01);
        if (Num(V("total")) is double t) Motion.Replay(_total, _key + "total", t * 0.85, t, Amounts.Short, -1);
        PillGlow();
        foreach (var x in _toasts.Values)
        {
            if (x.Kind == "rare") RareBeat(x.Body);
            else if (x.Kind == "top") Sheen(x.Body);
        }
        if (_toasts.Values.FirstOrDefault(x => x.Kind == "pick" && _stack.Children.IndexOf(x.Place) >= 0) is { } first && Picks(values).FirstOrDefault() is { } fp) PickPop(first.Body, fp);
    }
}
