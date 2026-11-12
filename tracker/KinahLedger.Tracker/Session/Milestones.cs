namespace KinahLedger.Session;

/// <summary>
/// When the window marks a moment (Ui/MainWindow.Bests.cs): this hour passing your best here, and today's Kinah passing a
/// milestone. Only a crossing seen as it happens counts. The first look at a count (the tracker starting, another zone,
/// page, character or day, a count put back by Undo, a day loaded again) only notes where it stands, so a figure already
/// past on that look says nothing. A milestone is said at most once per character and day; a best at most once per count.
/// Kept apart from WPF, so the self-test can drive it tick by tick.
/// </summary>
public sealed class Milestones
{
    /// <summary>
    /// Today's Kinah marks. The demo's 632K of loose Kinah in 42 minutes is about 900K an hour, so 1M is roughly a first hour
    /// of farming, 2.5M an evening, 5M a long day and 10M on the heaviest days; the marks above are there so a big day still
    /// has one to reach. Each is two to two and a half times the one before, so a mark stays an event, never every half hour.
    /// </summary>
    public static readonly long[] DayMarks = { 1_000_000, 2_500_000, 5_000_000, 10_000_000, 25_000_000, 50_000_000, 100_000_000 };

    /// <summary>The day the notes below are for ("2026-11-11", DayStore.Today); a new day forgets them.</summary>
    string _day = "";
    /// <summary>The highest mark each character has passed today, as its place in <see cref="DayMarks"/> (-1: none yet).</summary>
    readonly Dictionary<string, int> _passed = new();

    /// <summary>The last look at each count's hour: shown against a best, at or past it, its pickups, and whether it was said.</summary>
    readonly record struct Look(bool Shown, bool Past, int Events, bool Said);
    readonly Dictionary<string, Look> _looks = new();

    void OnDay(string day)
    {
        if (day == _day) return;
        _day = day; _passed.Clear(); _looks.Clear();
    }

    /// <summary>The highest mark at or under <paramref name="kinah"/>, as its place in <see cref="DayMarks"/>; -1 under the first.</summary>
    public static int MarkAt(long kinah)
    {
        int i = -1;
        while (i + 1 < DayMarks.Length && kinah >= DayMarks[i + 1]) i++;
        return i;
    }

    /// <summary>
    /// Today's Kinah for <paramref name="character"/>, looked at each tick: the mark it just passed, or null. Several passed at
    /// once give the highest. <paramref name="fresh"/>: the count under it was loaded or replaced since the last look (the
    /// day read again for this character, the history put in place), so where it stands is noted without a word. A mark
    /// passed once stays passed for the day, so Kinah taken off (Looks wrong?) and picked up again doesn't say it twice.
    /// </summary>
    public long? Day(string day, string character, long kinah, bool fresh)
    {
        OnDay(day);
        int at = MarkAt(kinah);
        bool seen = _passed.TryGetValue(character, out var was);
        if (!seen || fresh) { _passed[character] = seen ? Math.Max(was, at) : at; return null; }
        if (at <= was) return null;
        _passed[character] = at;
        return DayMarks[at];
    }

    /// <summary>
    /// This hour against your best here, looked at each tick, for one count (<paramref name="count"/>: the character, the page,
    /// where it counts and which count it is). True once: on the tick the hour reaches the best after a tick where the chase
    /// showed it under, with a pickup in between. Never on a count's first look, while it warms up (<paramref name="shown"/>
    /// false: no chase), on a price change alone (no pickup), or again for the same count after it dips under and passes again.
    /// </summary>
    public bool Best(string day, string count, bool shown, bool past, int events)
    {
        OnDay(day);
        bool had = _looks.TryGetValue(count, out var was);
        bool now = had && was.Shown && !was.Past && shown && past && events > was.Events && !was.Said;
        _looks[count] = new Look(shown, past, events, now || (had && was.Said));
        return now;
    }
}
