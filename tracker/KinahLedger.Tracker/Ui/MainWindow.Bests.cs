using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using KinahLedger.Session;

namespace KinahLedger.Ui;

/// <summary>
/// A personal best and a day's milestone get a moment on the hero (Steven, 2026-11-11: "cool and flashy"):
/// - when this hour passes your best here while you play, one gold light crosses the figure and a mint "★ New best" tag
///   grows in after its unit. The tag stays while the hour is past the best (the ring's ★ says the same) and comes and
///   goes quietly with it after that; the light and the growing in happen once per count. Buddy's mint pulse on the
///   overlay marks the same moment;
/// - when today's Kinah passes a milestone (1M, 2.5M, 5M and on: Session/Milestones.cs), the light crosses the figure and
///   a gold "1M Kinah today" tag shows under the other for a few seconds.
/// Only a crossing seen as it happens: never on starting, another zone, character or day, Undo, or in a demo or render
/// (KL_MOMENT holds one there, with KL_MOTION_AT). With Windows's animations off the tags still show, without the light or
/// any movement: the best tag is a state worth reading, and the milestone's words are the moment itself.
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
    DispatcherTimer? _dayTagHold;

    /// <summary>The tags' height, the gap between them, and the share of its size a tag grows in from.</summary>
    const double TagHeight = 18, TagGap = 3, TagFrom = 0.7;
    /// <summary>The best tag growing in, the day's (it comes with the light, so a little slower), and the day's fading out.</summary>
    const double TagInMs = 420, DayTagInMs = 600, TagOutMs = 360;
    /// <summary>How long the day's tag stays: long enough to catch with a glance away from the game.</summary>
    static readonly TimeSpan DayTagHold = TimeSpan.FromSeconds(8);

    /// <summary>
    /// The hero's moments for the count on screen, each tick (from FillRate, with the chase's own figures): the best tag as
    /// the hour stands, and the light when this hour or today's Kinah has just passed a mark.
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

    /// <summary>The best tag as the hour stands; on the tick it was just passed (and the hero is on screen), it grows in under the light.</summary>
    void ShowBestTag(bool past, bool said)
    {
        var tag = _bestTag!;
        if (said && Motion.On && HeadRate.IsVisible)
        {
            TagIn(tag, TagInMs);
            Motion.LightSweep(HeadLight, HeadRate, "Gold");
            return;
        }
        if (past == (tag.Visibility == Visibility.Visible)) return;
        Motion.Halt(tag);
        TagRest(tag);
        tag.Visibility = past ? Visibility.Visible : Visibility.Hidden;
    }

    /// <summary>Today's Kinah just passed <paramref name="mark"/>: the light, and the day's tag for <see cref="DayTagHold"/>.</summary>
    void DayMoment(long mark)
    {
        if (!HeadRate.IsVisible) return;
        var tag = _dayTag!;
        _dayTagText!.Text = Amounts.Short(mark) + " Kinah today";
        tag.ToolTip = $"Today's Kinah passed {Amounts.Full(mark)}: the loose Kinah picked up farming since midnight on this PC, as LOOT's Today side counts it.";
        _dayTagHold!.Stop();
        if (Motion.On)
        {
            TagIn(tag, DayTagInMs);
            Motion.LightSweep(HeadLight, HeadRate, "Gold");
        }
        else { Motion.Halt(tag); TagRest(tag); tag.Visibility = Visibility.Visible; }
        // a render holds the frame and closes; only a live window takes the tag away again
        if (Motion.FrozenAt == null) _dayTagHold.Start();
    }

    /// <summary>The day's tag goes after its hold: it fades out (at once with animations off), keeping its place.</summary>
    void DayTagGone()
    {
        _dayTagHold!.Stop();
        var tag = _dayTag!;
        if (!Motion.On || !tag.IsVisible) { Motion.Halt(tag); TagRest(tag); tag.Visibility = Visibility.Hidden; return; }
        Motion.Play(tag, TagOutMs, p => tag.Opacity = 1 - Motion.Out(p), () => { tag.Visibility = Visibility.Hidden; tag.Opacity = 1; });
    }

    /// <summary>A tag coming in: from <see cref="TagFrom"/> of its size and clear, growing out of its left edge, eased.</summary>
    static void TagIn(Border tag, double ms)
    {
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

    static void TagRest(Border tag)
    {
        tag.Opacity = 1;
        tag.ClearValue(UIElement.RenderTransformProperty);
    }

    /// <summary>
    /// The two tags after the hour's unit, built once: "★ New best" on the unit's first line, the day's on its second. Both
    /// keep their place while hidden, so neither showing moves anything.
    /// </summary>
    void MakeBestTags()
    {
        if (_bestTag != null) return;
        _bestTag = MomentTag("★ New best", "MintTint", "Mint", "Mint", out _);
        _bestTag.ToolTip = "This hour is past your best hour in this zone. It becomes your best here if it holds until you leave the zone, end the run or close the tracker, after ten minutes or more there.";
        _dayTag = MomentTag("", "FlagBg", "FlagLine", "FlagInk", out _dayTagText);
        _dayTag.Margin = new Thickness(0, TagGap, 0, 0);
        Grid.SetRow(_dayTag, 1);
        BestTags.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        BestTags.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        BestTags.Children.Add(_bestTag);
        BestTags.Children.Add(_dayTag);
        _dayTagHold = new DispatcherTimer { Interval = DayTagHold };
        _dayTagHold.Tick += (_, _) => DayTagGone();
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
    /// best here (the chase drawn past a best 5% under it), and today's Kinah as if it had just passed its first mark. The
    /// figure itself is shown settled, so the frame shows the moment's light rather than the pickup's roll.
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
}
