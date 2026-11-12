using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using KinahLedger.Data;
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
/// Only a crossing seen as it happens: never on starting, another zone, character or day, Undo, a price change, or in a
/// demo or render (KL_MOMENT holds one there, with KL_MOTION_AT). With Windows's animations off there is no light and no
/// best tag (the ring says it); the milestone's tag still shows, still, since its words are the moment itself.
/// </summary>
public partial class MainWindow
{
    readonly Milestones _milestones = new();
    /// <summary>The count the hour was last looked at for, and how many counts there have been: another one (Undo putting one back too) starts its looks over.</summary>
    SessionEngine? _bestsOf;
    int _bestsCount;
    /// <summary>The best's key and the day's (day and character) at the last look: a tag still up for another one goes.</summary>
    string _bestsKey = "", _dayKey = "";
    /// <summary>The count's tradeable pieces by item and its Kinah at the last look while the chase showed, what this tick's pickups are told from; empty otherwise.</summary>
    readonly Dictionary<long, int> _bestsPieces = new();
    long _bestsKinah;
    bool _bestsLooked;
    /// <summary>Today's count last looked at: another one (the new day's at midnight, a character's day loaded, the history put in place) is taken as it is, not said.</summary>
    SessionEngine? _dayOf;
    Border? _bestTag, _dayTag;
    TextBlock? _dayTagText;
    DispatcherTimer? _bestTagHold, _dayTagHold;

    /// <summary>The tags' height, the gap between them, the room before them, and the share of its size a tag grows in from.</summary>
    const double TagHeight = 18, TagGap = 3, TagsLeft = 12, TagFrom = 0.7;
    /// <summary>The best tag growing in, the day's (it comes with the light, so a little slower), and a tag fading out.</summary>
    const double TagInMs = 420, DayTagInMs = 600, TagOutMs = 360;
    /// <summary>How long each tag stays: long enough to catch with a glance away from the game (the day's says a figure, so a little longer).</summary>
    static readonly TimeSpan BestTagHold = TimeSpan.FromSeconds(6), DayTagHold = TimeSpan.FromSeconds(8);
    /// <summary>The ring's pop on a new best: how long, how far it swells, where in its course it peaks, its glow's spread and strength.</summary>
    const double RingPopMs = 500, RingPopScale = 1.08, RingPopPeak = 0.3, RingGlowBlur = 20, RingGlowStrength = 0.95;

    /// <summary>
    /// The hero's moments for the count on screen, each tick (from FillRate, with its hours, Kinah and the chase's figures):
    /// the light and a tag when this hour or today's Kinah has just passed a mark.
    /// </summary>
    void FillBests(SessionEngine e, double hours, long kinah, double nowHr, double bestHr, bool warm)
    {
        MakeBestTags();
        bool shown = nowHr > 0 && bestHr > 0 && !warm, past = shown && nowHr >= bestHr;
        bool said = false;
        if (!_demo)
        {
            string day = DayStore.Today, who = Characters.Current?.Id ?? "";
            bool sameCount = ReferenceEquals(e, _bestsOf);
            if (!sameCount) { _bestsOf = e; _bestsCount++; }
            var key = $"{who}|{_part}|{CountWhere()}|{_watcher.Chat?.ZoneView?.Zone ?? e.Zone}|{_bestsCount}";
            if (key != _bestsKey) { _bestsKey = key; TagOff(_bestTag!, _bestTagHold!); }
            // the hour without what was picked up since the last look, at the prices now: past the best already means the prices moved it
            double picked = shown ? PickedSinceLastLook(e, kinah, sameCount) : ForgetPieces();
            said = _milestones.Best(day, key, shown, past, nowHr - picked / hours >= bestHr);
            var today = _watcher.DayView;
            bool fresh = !ReferenceEquals(today, _dayOf);
            _dayOf = today;
            if (day + "|" + who != _dayKey) { _dayKey = day + "|" + who; TagOff(_dayTag!, _dayTagHold!); }
            if (_milestones.Day(day, who, today.Kinah, fresh) is long mark) DayMoment(mark);
        }
        ShowBestTag(past, said);
    }

    /// <summary>What was picked up since the last look while the chase showed (Milestones.Picked); nothing on a count's first look. This look becomes the last.</summary>
    double PickedSinceLastLook(SessionEngine e, long kinah, bool sameCount)
    {
        var rows = e.ItemRows().Select(r => (r.Id, r.Name, r.Count)).ToList();
        double worth = sameCount && _bestsLooked
            ? Milestones.Picked(_bestsKinah, kinah, _bestsPieces, rows, n => PriceBook.Price(n) * PriceBook.SellerKeeps)
            : 0;
        _bestsPieces.Clear();
        foreach (var (id, _, count) in rows) _bestsPieces[id] = count;
        _bestsKinah = kinah; _bestsLooked = true;
        return worth;
    }

    /// <summary>No chase shown: no last look to tell pickups from (none is needed, nothing can be said on the next tick).</summary>
    double ForgetPieces()
    {
        _bestsPieces.Clear(); _bestsLooked = false;
        return 0;
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
            if (TagFits(tag)) TagIn(tag, TagInMs, _bestTagHold!);
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
        // the window's glow, so it is as soft on a light look as every other glow there
        var glow = QGlow("Mint", RingGlowBlur, RingGlowStrength);
        double strength = glow.Opacity;
        glow.Opacity = 0;
        host.Effect = glow;
        Motion.Play(host, RingPopMs, p =>
        {
            double up = p < RingPopPeak ? Motion.Out(p / RingPopPeak) : 1 - Motion.Out((p - RingPopPeak) / (1 - RingPopPeak));
            scale.ScaleX = scale.ScaleY = 1 + (RingPopScale - 1) * up;
            glow.Opacity = strength * up;
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
        if (TagFits(tag)) TagIn(tag, DayTagInMs, _dayTagHold!);
        Motion.LightSweep(HeadLight, HeadRate, "Gold");
    }

    /// <summary>
    /// Whether a tag fits between the hour's unit and the ring's column: the tags are drawn over the card, not laid out, so a
    /// wide figure with a long unit never pushes anything; a tag with no room isn't shown (the light still crosses the figure).
    /// </summary>
    bool TagFits(Border tag)
    {
        if (BestTags.Parent is not FrameworkElement row || row.Parent is not FrameworkElement column) return false;
        // the pill's own width from its words (a put-away pill measures as nothing)
        tag.Child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double width = TagsLeft + tag.Child.DesiredSize.Width + tag.Padding.Left + tag.Padding.Right + tag.BorderThickness.Left + tag.BorderThickness.Right;
        double room = column.ActualWidth - BestTags.TranslatePoint(new Point(0, 0), column).X;
        return width <= room;
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

    /// <summary>A tag going after its hold: it fades out (at once with animations off).</summary>
    static void TagGone(Border tag, DispatcherTimer hold)
    {
        hold.Stop();
        if (!Motion.On || !tag.IsVisible) { TagOff(tag, hold); return; }
        Motion.Play(tag, TagOutMs, p => tag.Opacity = 1 - Motion.Out(p), () => { tag.Visibility = Visibility.Collapsed; tag.Opacity = 1; });
    }

    /// <summary>A tag put away at once, its hold and any course on it stopped (another count, character or day; the hour back under).</summary>
    static void TagOff(Border tag, DispatcherTimer hold)
    {
        hold.Stop();
        Motion.Halt(tag);
        TagRest(tag);
        tag.Visibility = Visibility.Collapsed;
    }

    static void TagRest(Border tag)
    {
        tag.Opacity = 1;
        tag.ClearValue(UIElement.RenderTransformProperty);
    }

    /// <summary>
    /// The two tags after the hour's unit, built once with their holds: "★ New best" level with the unit's first line, the
    /// day's with its second. They sit on a canvas, drawn over the card and never laid out, so neither showing moves anything.
    /// </summary>
    void MakeBestTags()
    {
        if (_bestTag != null) return;
        var best = MomentTag("★ New best", "MintTint", "Mint", "Mint", out _);
        best.ToolTip = "This hour is past your best hour in this zone. It becomes your best here if it holds until you leave the zone, end the run or close the tracker, after ten minutes or more there.";
        var day = MomentTag("", "FlagBg", "FlagLine", "FlagInk", out _dayTagText);
        Canvas.SetLeft(best, TagsLeft); Canvas.SetLeft(day, TagsLeft);
        Canvas.SetTop(day, TagHeight + TagGap);
        BestTags.Children.Add(best);
        BestTags.Children.Add(day);
        var bestHold = new DispatcherTimer { Interval = BestTagHold };
        bestHold.Tick += (_, _) => TagGone(best, bestHold);
        var dayHold = new DispatcherTimer { Interval = DayTagHold };
        dayHold.Tick += (_, _) => TagGone(day, dayHold);
        _bestTag = best; _dayTag = day; _bestTagHold = bestHold; _dayTagHold = dayHold;
    }

    /// <summary>A small pill in the look's brushes, put away.</summary>
    static Border MomentTag(string text, string fill, string line, string ink, out TextBlock label)
    {
        label = QT(text, 11, FontWeights.SemiBold, ink);
        var tag = new Border
        {
            Height = TagHeight, CornerRadius = new CornerRadius(TagHeight / 2), BorderThickness = new Thickness(1), Padding = new Thickness(8, 0, 8, 0),
            Visibility = Visibility.Collapsed, Child = label,
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
