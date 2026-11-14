namespace KinahLedger.Session;

/// <summary>
/// "Not a drop" (Steven, 2026-11-13: "i bought the egos and they counted as drops it fucked up my totals"): the player takes
/// one pickup the tracker counted back out of every count it went into, and Undo puts it back. A farming pickup leaves the
/// 15-minute count (so it is never sent, when that count has not gone yet), the farming run, the zone and all-zones views
/// and Today (the week's figures read Today and the day history); a run's pickup leaves the run (a finished one is sent
/// again as a correction). The feed line and the recent pickups (the panel over the game, the rare-drop alert) lose it.
/// The window takes back the quest points it gave and saves Today and the list (NotDrops) at once.
/// </summary>
public sealed partial class TrackerSession
{
    /// <summary>What one "Not a drop" took out and from where, for its Undo.</summary>
    public sealed record NotDropTaken(ItemAcquired Pickup, string Where, List<(SessionEngine Engine, SessionEngine.Taken Taken)> Engines, DateTime? Run, FeedLine? Line, int LineAt, Pickup? Recent, int RecentAt)
    {
        /// <summary>It came out of some count (an engine or a run).</summary>
        public bool Any => Engines.Count > 0 || Run != null;
        /// <summary>What Today gave back, kept in not-drops.json so an Undo after a restart can put it back there.</summary>
        public SessionEngine.Taken? Day { get; init; }
    }

    /// <summary>
    /// Takes one pickup out (<paramref name="line"/>: its feed line when there is one, so it leaves the feed too). The engines
    /// are this count's own (the 15-minute count, the farming run, the views); <paramref name="day"/> is Today when the
    /// window has it apart from <see cref="DayView"/> (after a restart the feed and the other counts are new, and Today alone
    /// still holds the pickup).
    /// </summary>
    public NotDropTaken NotADrop(ItemAcquired a, string where, FeedLine? line = null, SessionEngine? day = null)
    {
        var zone = where.Contains(" · ") ? where[(where.IndexOf(" · ", StringComparison.Ordinal) + 3)..] : null;
        var taken = new List<(SessionEngine, SessionEngine.Taken)>();
        DateTime? run = null;
        SessionEngine.Taken? dayTaken = null;
        if (LiveFeed.TabOf(where) == FeedTab.Events) run = Instances.TakeItem(a.Time, a.ItemId, a.Count, a.Bound);
        else
        {
            var seen = new HashSet<SessionEngine>(ReferenceEqualityComparer.Instance);
            var dayEngine = day ?? DayView;
            foreach (var e in new[] { Engine, Secondary }.Concat(Views()).Append(dayEngine))
            {
                if (e == null || !seen.Add(e)) continue;
                // Today holds the whole day (seeded from today.json); the others only what came since they began
                bool isDay = ReferenceEquals(e, dayEngine);
                if (e.Unapply(a, zone, isDay ? null : e.Started.AddSeconds(-5)) is { } t) { taken.Add((e, t)); if (isDay) dayTaken = t; }
            }
        }
        int lineAt = line != null ? Live.Remove(line) : -1;
        Pickup? recent = null; int recentAt = -1;
        var text = $"{_items.NameOf(a.ItemId)} ×{a.Count}" + (a.Bound ? " (bound)" : "");
        lock (_pickups)
        {
            recentAt = _pickups.FindLastIndex(p => p.What == text && Math.Abs((p.At - a.Time).TotalSeconds) < 1);
            if (recentAt >= 0) { recent = _pickups[recentAt]; _pickups.RemoveAt(recentAt); }
        }
        CrashLog.Note($"not a drop (the player): {text} at {a.Time.ToLocalTime():HH:mm:ss} ({where}): taken out of {(run != null ? "its run" : taken.Count + " count(s)")}");
        return new NotDropTaken(a, where, taken, run, line, lineAt, recent, recentAt) { Day = dayTaken };
    }

    /// <summary>Undo of <see cref="NotADrop"/>: the pickup counts again everywhere it was taken from.</summary>
    public void UndoNotADrop(NotDropTaken t)
    {
        foreach (var (e, tk) in t.Engines) e.Restore(tk);
        if (t.Run is { } start) Instances.PutItem(start, t.Pickup.ItemId, t.Pickup.Count, t.Pickup.Bound);
        if (t.Line != null && t.LineAt >= 0) Live.PutBack(t.Line, t.LineAt);
        if (t.Recent != null) lock (_pickups) _pickups.Insert(Math.Clamp(t.RecentAt, 0, _pickups.Count), t.Recent);
        CrashLog.Note($"not a drop undone: {_items.NameOf(t.Pickup.ItemId)} ×{t.Pickup.Count} at {t.Pickup.Time.ToLocalTime():HH:mm:ss} counts again");
    }
}
