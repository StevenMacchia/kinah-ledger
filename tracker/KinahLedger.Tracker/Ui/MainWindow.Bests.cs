using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using KinahLedger.Session;

namespace KinahLedger.Ui;

/// <summary>
/// A personal best and a day's milestone get a moment on the hero (Steven, 2026-11-11: "cool and flashy"):
/// - when this hour passes your best here while you play, one gold light crosses the figure, a mint "★ New best" tag grows
///   in after its unit for a few seconds, and the ring swells and brightens once, so the eye lands on it after: the ring's
///   ★ is what says it from then on (a tag that stayed would say it twice). Once per count; Buddy's mint pulse on the
///   overlay marks the same moment;
/// - when today's Kinah passes a milestone (1M, 2.5M, 5M and on: Session/Milestones.cs), the light crosses the figure and
///   a gold "1M Kinah today" tag shows under the other for a few seconds.
/// Only a crossing seen as it happens: never on starting, another zone, character or day, Undo, or in a demo or render
/// (KL_MOMENT holds one there, with KL_MOTION_AT). With Windows's animations off there is no light and no best tag (the
/// ring says it); the milestone's tag still shows, still, since its words are the moment itself.
/// </summary>
public partial class MainWindow
{
    readonly Milestones _milestones = new();
    /// <summary>The count the hour was last looked at for, and how many counts there have been: another one (Undo putting one back too) starts its looks over.</summary>
    SessionEngine? _bestsOf;
    int _bestsCount;
    /// <summary>Today's count last looked at: one loaded again (another character's day, the history put in place) is noted, not said.</summary>
    SessionEngine? _dayOf;
    Border? _bestTag, _dayTag;
    TextBlock? _dayTagText;
    DispatcherTimer? _bestTagHold, _dayTagHold;

    /// <summary>The tags' height, the gap between them, and the share of its size a tag grows in from.</summary>
    const double TagHeight = 18, TagGap = 3, TagFrom = 0.7;
    /// <summary>The best tag growing in, the day's (it comes with the light, so a little slower), and a tag fading out.</summary>
    const double TagInMs = 420, DayTagInMs = 600, TagOutMs = 360;
    /// <summary>How long each tag stays: long enough to catch with a glance away from the game (the day's says a figure, so a little longer).</summary>
    static readonly TimeSpan BestTagHold = TimeSpan.FromSeconds(6), DayTagHold = TimeSpan.FromSeconds(8);
    /// <summary>The ring's pop on a new best: how long, how far it swells, where in its course it peaks, and its glow's spread.</summary>
    const double RingPopMs = 500, RingPopScale = 1.08, RingPopPeak = 0.3, RingGlowBlur = 20;

    /// <summary>
    /// The hero's moments for the count on screen, each tick (from FillRate, with the chase's own figures): the light and a
    /// tag when this hour or today's Kinah has just passed a mark.
    /// </summary>
    void FillBests(SessionEngine e, double nowHr, double bestHr, bool warm)
    {
        MakeBestTags();
        bool shown = nowHr > 0 && bestHr > 0 && !warm, past = shown && nowHr >= bestHr;
        bool said = false;
        if (!_demo)
        {
            string day = DayStore.Today, who = Characters.Current?.Id ?? "";
            if (!ReferenceEquals(e, _bestsOf)) { _bestsOf = e; _bestsCount++; }
            said = _milestones.Best(day, $"{who}|{_part}|{CountWhere()}|{_watcher.Chat?.ZoneView?.Zone ?? e.Zone}|{_bestsCount}", shown, past, e.Events);
            var today = _watcher.DayView;
            bool fresh = !ReferenceEquals(today, _dayOf);
            _dayOf = today;
            if (_milestones.Day(day, who, today.Totals().Kinah, fresh) is long mark) DayMoment(mark);
        }
        ShowBestTag(past, said);
    }

    /// <summary>
    /// The hour has just passed your best here (and the hero is on screen): the light, the best tag for <see cref="BestTagHold"/>,
    /// and the ring's pop. Nothing with animations off: the ring says it. A tag still up when the hour falls back under the
    /// best goes at once, so it never says more than the ring.
    /// </summary>
    void ShowBestTag(bool past, bool said)
    {
        var tag = _bestTag!;
        if (said && Motion.On && HeadRate.IsVisible)
        {
            TagIn(tag, TagInMs, _bestTagHold!);
            Motion.LightSweep(HeadLight, HeadRate, "Gold");
            RingPop();
        }
        else if (!past && tag.Visibility == Visibility.Visible) TagOff(tag, _bestTagHold!);
    }

    /// <summary>The ring of your best here swells 8% and settles, glowing mint as it does (no layout: a render transform and an effect).</summary>
    void RingPop()
    {
        var host = RingHost;
        var scale = new ScaleTransform(1, 1);
        host.RenderTransformOrigin = new Point(0.5, 0.5);
        host.RenderTransform = scale;
        var glow = new DropShadowEffect { Color = (TryFindResource("Mint") as SolidColorBrush)?.Color ?? Colors.MediumSeaGreen, ShadowDepth = 0, BlurRadius = RingGlowBlur, Opacity = 0 };
        host.Effect = glow;
        Motion.Play(host, RingPopMs, p =>
        {
            double up = p < RingPopPeak ? Motion.Out(p / RingPopPeak) : 1 - Motion.Out((p - RingPopPeak) / (1 - RingPopPeak));
            scale.ScaleX = scale.ScaleY = 1 + (RingPopScale - 1) * up;
            glow.Opacity = up;
        }, () =>
        {
            host.ClearValue(UIElement.RenderTransformProperty);
            if (host.Effect == glow) host.ClearValue(UIElement.EffectProperty);
        });
    }

    /// <summary>Today's Kinah just passed <paramref name="mark"/>: the light, and the day's tag for <see cref="DayTagHold"/> (still, with animations off).</summary>
    void DayMoment(long mark)
    {
        if (!HeadRate.IsVisible) return;
        var tag = _dayTag!;
        _dayTagText!.Text = Amounts.Short(mark) + " Kinah today";
        tag.ToolTip = $"Today's Kinah passed {Amounts.Full(mark)}: the loose Kinah picked up farming since midnight on this PC, as LOOT's Today side counts it.";
        TagIn(tag, DayTagInMs, _dayTagHold!);
        Motion.LightSweep(HeadLight, HeadRate, "Gold");
    }

    /// <summary>
    /// A tag coming in for its <paramref name="hold"/>: from <see cref="TagFrom"/> of its size and clear, growing out of its
    /// left edge, eased; shown at once with animations off. A render holds the frame and closes, so only a live window
    /// starts the hold that takes it away again.
    /// </summary>
    static void TagIn(Border tag, double ms, DispatcherTimer hold)
    {
        hold.Stop();
        if (Motion.FrozenAt == null) hold.Start();
        if (!Motion.On) { Motion.Halt(tag); TagRest(tag); tag.Visibility = Visibility.Visible; return; }
        var scale = new ScaleTransform(TagFrom, TagFrom);
        tag.RenderTransformOrigin = new Point(0, 0.5);
        tag.RenderTransform = scale;
        tag.Opacity = 0;
        tag.Visibility = Visibility.Visible;
        Motion.Play(tag, ms, p =>
        {
            double q = Motion.Out(p);
            scale.ScaleX = scale.ScaleY = TagFrom + (1 - TagFrom) * q;
            tag.Opacity = q;
        }, () => TagRest(tag));
    }

    /// <summary>A tag going after its hold: it fades out (at once with animations off), keeping its place.</summary>
    static void TagGone(Border tag, DispatcherTimer hold)
    {
        hold.Stop();
        if (!Motion.On || !tag.IsVisible) { TagOff(tag, hold); return; }
        Motion.Play(tag, TagOutMs, p => tag.Opacity = 1 - Motion.Out(p), () => { tag.Visibility = Visibility.Hidden; tag.Opacity = 1; });
    }

    /// <summary>A tag hidden at once, its hold and any course on it stopped.</summary>
    static void TagOff(Border tag, DispatcherTimer hold)
    {
        hold.Stop();
        Motion.Halt(tag);
        TagRest(tag);
        tag.Visibility = Visibility.Hidden;
    }

    static void TagRest(Border tag)
    {
        tag.Opacity = 1;
        tag.ClearValue(UIElement.RenderTransformProperty);
    }

    /// <summary>
    /// The two tags after the hour's unit, built once with their holds: "★ New best" on the unit's first line, the day's on
    /// its second. Both keep their place while hidden, so neither showing moves anything.
    /// </summary>
    void MakeBestTags()
    {
        if (_bestTag != null) return;
        var best = MomentTag("★ New best", "MintTint", "Mint", "Mint", out _);
        best.ToolTip = "This hour is past your best hour in this zone. It becomes your best here if it holds until you leave the zone, end the run or close the tracker, after ten minutes or more there.";
        var day = MomentTag("", "FlagBg", "FlagLine", "FlagInk", out _dayTagText);
        day.Margin = new Thickness(0, TagGap, 0, 0);
        Grid.SetRow(day, 1);
        BestTags.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        BestTags.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        BestTags.Children.Add(best);
        BestTags.Children.Add(day);
        var bestHold = new DispatcherTimer { Interval = BestTagHold };
        bestHold.Tick += (_, _) => TagGone(best, bestHold);
        var dayHold = new DispatcherTimer { Interval = DayTagHold };
        dayHold.Tick += (_, _) => TagGone(day, dayHold);
        _bestTag = best; _dayTag = day; _bestTagHold = bestHold; _dayTagHold = dayHold;
    }

    /// <summary>A small pill in the look's brushes, hidden.</summary>
    static Border MomentTag(string text, string fill, string line, string ink, out TextBlock label)
    {
        label = QT(text, 11, FontWeights.SemiBold, ink);
        var tag = new Border
        {
            Height = TagHeight, CornerRadius = new CornerRadius(TagHeight / 2), BorderThickness = new Thickness(1), Padding = new Thickness(8, 0, 8, 0),
            HorizontalAlignment = HorizontalAlignment.Left, Visibility = Visibility.Hidden, Child = label,
        };
        tag.SetResourceReference(Border.BackgroundProperty, fill);
        tag.SetResourceReference(Border.BorderBrushProperty, line);
        return tag;
    }

    /// <summary>
    /// For a render of the moments (KL_MOMENT=best, day, or both, with KL_MOTION_AT): the hour as if it had just passed your
    /// best here (the chase and the ring drawn past a best 5% under it, the ring popping), and today's Kinah as if it had
    /// just passed its first mark. The figure itself is shown settled, so the frame shows the moment's light rather than
    /// the pickup's roll.
    /// </summary>
    void BestsFrameForRender()
    {
        var which = Environment.GetEnvironmentVariable("KL_MOMENT") ?? "";
        if (which.Length == 0) return;
        MakeBestTags();
        // the figure settled at its value, so the frame shows the light alone, not the pickup's green roll under it
        var key = "hero|" + _part + "|" + CountWhere();
        if (Motion.LastOf(key) is double v && v > 0) Motion.Replay(HeadRate, key, v, v, x => Fk(x));
        if (which.Contains("best") && _lastRateHr > 0)
        {
            FillChase(_lastRateHr, _lastRateHr / RenderBestUnder, false);
            ShowBestTag(true, true);
        }
        if (which.Contains("day")) DayMoment(Milestones.DayMarks[0]);
    }

    /// <summary>A render's best: this far under the hour shown.</summary>
    const double RenderBestUnder = 1.05;

    /// <summary>
    /// For options-test: the hour just past its best in the window as it stands (held at <see cref="Motion.FrozenAt"/>, or
    /// still): whether the best tag shows and how clear it is, and the ring's scale; then the hour back under, which takes
    /// the tag away.
    /// </summary>
    internal (bool Tag, double Opacity, double RingScale, bool GoneUnder) BestMomentForTest()
    {
        MakeBestTags();
        ShowBestTag(true, true);
        var shown = (_bestTag!.Visibility == Visibility.Visible, _bestTag.Opacity, (RingHost.RenderTransform as ScaleTransform)?.ScaleX ?? 1);
        ShowBestTag(false, false);
        return (shown.Item1, shown.Item2, shown.Item3, _bestTag.Visibility != Visibility.Visible);
    }
}
