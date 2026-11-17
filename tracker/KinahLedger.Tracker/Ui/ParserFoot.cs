using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Effects;
using KinahLedger.Parser;

namespace KinahLedger.Ui;

/// <summary>
/// The meter's foot in every look (Steven, 2026-11-15: "have the bottom be total AND group dps avg"): the fight's total on
/// the left, the group's DPS on the right; once the fight is held (Always on, 5 s without fighting; a pull's kill or wipe)
/// the group figure is the whole fight's average and carries the green "Avg. DPS" badge. The plates the rows wear at the
/// end of a boss fight (TOP DPS gold, TOP HEALS green) are drawn here too, so every look has the same ones.
/// </summary>
static class ParserFoot
{
    /// <summary>
    /// "Total 10.5M        Group DPS 126.8K", or held "Total 10.5M        Group 126.8K [AVG. DPS]". <paramref name="lead"/>:
    /// the outcome said before the total ("Killed in 2:31"), when the look has no other place for it.
    /// </summary>
    /// <summary>You are the only one on the meter (no member but you with damage): the group's figure is yours.</summary>
    public static bool SoloYou(Encounter e) => e.Members.Any(f => f.You) && e.Members.Where(f => !f.Other && f.Damage > 0).All(f => f.You);

    /// <summary>The seconds a peak is measured over: the best stretch of this many seconds in a row.</summary>
    public const int PeakSeconds = 5;

    /// <summary>
    /// The foot's right-hand figure: its word, the figure, and whether it is the held fight's average (the AVG. DPS badge).
    /// With others on the meter, the group's DPS. Alone, the group's figure is your row's again (Steven, 2026-11-17: "isnt
    /// the avg thing redundant?"), so it says something the row doesn't: your best on this fight before this one ("Best"),
    /// else your best <see cref="PeakSeconds"/> seconds in a row ("Peak"), else nothing (Word "").
    /// </summary>
    public static (string Word, string Value, bool Held) Right(Encounter e, DateTime now)
    {
        bool held = e.HeldAverage;
        bool pulls = e.ModeKind == ParserMode.Run && (e.BossView || !e.TargetsSplit);
        if (!SoloYou(e))
            return (L.T(held ? "Group" : pulls ? "Group DPS (boss pulls)" : "Group DPS"), e.GroupDpsText(held ? e.End ?? now : now), held);
        double best = 0;
        try { best = ParserRows.BestOf?.Invoke(e)?.Dps ?? 0; } catch (Exception) { best = 0; }
        if (best > 0) return (L.T("Best"), Amounts.Meter(best), false);
        if (PeakDps(e) is { } peak) return (L.T("Peak"), Amounts.Meter(peak), false);
        return ("", "", false);
    }

    /// <summary>Your damage over your best <see cref="PeakSeconds"/> seconds in a row, a second; null with fewer seconds than that one more.</summary>
    public static double? PeakDps(Encounter e)
    {
        if (e.You?.PerSecond is not { } ps || ps.Count <= PeakSeconds) return null;
        long sum = 0, top = 0;
        for (int i = 0; i < ps.Count; i++)
        {
            sum += ps[i];
            if (i >= PeakSeconds) sum -= ps[i - PeakSeconds];
            if (i >= PeakSeconds - 1 && sum > top) top = sum;
        }
        return top > 0 ? top / (double)PeakSeconds : null;
    }

    public static Grid Line(Encounter e, DateTime now, double size = 12, TextBlock? lead = null)
    {
        var g = new Grid { Tag = "parser-foot" };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = new TextBlock { FontSize = size, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        Typography.SetNumeralAlignment(left, FontNumeralAlignment.Tabular);
        if (lead != null)
        {
            left.Inlines.Add(new Run(lead.Text) { Foreground = lead.Foreground, FontWeight = FontWeights.Bold });
            left.Inlines.Add(new Run("   ") { FontSize = size - 1 });
        }
        left.Inlines.Add(new Run(L.T("Total ")) { Foreground = HudKit.Muted, FontWeight = FontWeights.SemiBold, FontSize = size - 0.5 });
        left.Inlines.Add(new Run(Amounts.Meter(e.Credited)) { Foreground = HudKit.Ink, FontWeight = FontWeights.Bold, FontSize = size + 1 });
        g.Children.Add(left);
        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
        // the group's DPS (a run's counts its boss pulls only, said so); alone, your best on this fight or your peak (Right)
        var (word, value, held) = Right(e, now);
        var gt = new TextBlock { FontSize = size, VerticalAlignment = VerticalAlignment.Center, Tag = word.Length > 0 ? "foot-right" : null };
        Typography.SetNumeralAlignment(gt, FontNumeralAlignment.Tabular);
        if (word.Length > 0)
        {
            gt.Inlines.Add(new Run(word + " ") { Foreground = HudKit.Muted, FontWeight = FontWeights.SemiBold, FontSize = size - 0.5 });
            gt.Inlines.Add(new Run(value) { Foreground = HudKit.Ink, FontWeight = FontWeights.Bold, FontSize = size + 1 });
            right.Children.Add(gt);
        }
        if (e.Live && !e.Waiting) SawLive(e.Id);
        if (held)
        {
            var badge = AvgBadge();
            right.Children.Add(badge);
            // a fight seen ending: the badge stamps in (a foot drawn again picks the stamp up where it was)
            string key = "avg|" + e.Id;
            if (Seen.Contains(e.Id)) Motion.Mark(key);
            Stamp(badge, key);
        }
        Grid.SetColumn(right, 1); g.Children.Add(right);
        System.Windows.Automation.AutomationProperties.SetName(g, e.FootText(now));
        return g;
    }

    // the fights a foot drew live (the last few): only the end of one of them stamps the badge in
    static readonly HashSet<string> Seen = new();
    static readonly Queue<string> SeenOrder = new();
    static void SawLive(string id)
    {
        if (!Seen.Add(id)) return;
        SeenOrder.Enqueue(id);
        while (SeenOrder.Count > 32) Seen.Remove(SeenOrder.Dequeue());
    }

    /// <summary>The badge's stamp: down from a third larger onto its place, a touch past and back, fading in.</summary>
    public const double StampMs = 320;

    static void Stamp(Border badge, string key)
    {
        var st = new ScaleTransform(1, 1);
        // grown from near its left edge: at its largest it spills 3 px left (inside its 6 px gap, never on the group's figure)
        // and the rest right, into the panel's edge
        badge.RenderTransformOrigin = new Point(0.2, 0.5);
        badge.RenderTransform = st;
        Motion.Resume(badge, key, StampMs, p =>
        {
            double s = 1.35 - 0.35 * Motion.Back(Math.Min(1, p / 0.8), 1.4);
            st.ScaleX = st.ScaleY = s;
            badge.Opacity = Math.Clamp(p / 0.35, 0, 1);
        }, () => { st.ScaleX = st.ScaleY = 1; badge.Opacity = 1; });
    }

    /// <summary>The held figure's mark: a small green outline pill, "AVG. DPS", quieter than the TOP HEALS plate.</summary>
    public static Border AvgBadge() => new()
    {
        Tag = "avg-badge", Background = HudKit.B(HudKit.MintC, 0.14), BorderBrush = HudKit.B(HudKit.MintC, 0.6), BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(3), Padding = new Thickness(4, 0, 4, 0), Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
        ToolTip = L.T("The whole fight's average: the fight is over"),
        Child = new TextBlock { Text = L.T(Encounter.AvgBadge).ToUpperInvariant(), FontSize = 8.5, FontWeight = FontWeights.Bold, Foreground = HudKit.Mint, Margin = new Thickness(0, 0, 0, 0.5) },
    };

    /// <summary>
    /// A row's plate at the end of a boss fight: gold "TOP DPS" or green "TOP HEALS". Small (13 px), a metal gradient with a
    /// light top edge and a soft glow, dark letters; it sits in the name's room, so the row never widens.
    /// </summary>
    public static Border Plate(bool heals)
    {
        Color hi = heals ? HudKit.Rgb(0x8BF0BC) : HudKit.Rgb(0xFFE29A), mid = heals ? HudKit.Rgb(0x3DD68C) : HudKit.Rgb(0xF5B942), lo = heals ? HudKit.Rgb(0x1E9E62) : HudKit.Rgb(0xC98A1C);
        var fill = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        fill.GradientStops.Add(new GradientStop(hi, 0)); fill.GradientStops.Add(new GradientStop(mid, 0.45)); fill.GradientStops.Add(new GradientStop(lo, 1));
        fill.Freeze();
        return new Border
        {
            Tag = heals ? "top-heals" : "top-dps", Height = 13, CornerRadius = new CornerRadius(2.5), Padding = new Thickness(4, 0, 4, 0), Margin = new Thickness(6, 0, 0, 0),
            Background = fill, BorderBrush = HudKit.B(heals ? 0xC4FFE0u : 0xFFF1C6u, 0.7), BorderThickness = new Thickness(0, 0.8, 0, 0), VerticalAlignment = VerticalAlignment.Center,
            Effect = new DropShadowEffect { Color = mid, BlurRadius = 8, ShadowDepth = 0, Opacity = 0.55 },
            ToolTip = L.T(heals ? "The fight's top healer" : "The fight's top DPS"),
            Child = new TextBlock { Text = L.T(heals ? "TOP HEALS" : "TOP DPS"), FontSize = 8, FontWeight = FontWeights.Black, Foreground = HudKit.B(heals ? 0x062A17u : 0x2A1A00u), VerticalAlignment = VerticalAlignment.Center },
        };
    }
}
