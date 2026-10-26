using System.Text.Json.Serialization;

namespace KinahLedger.Session;

/// <summary>
/// How much of what the tracker counted the game's own numbers back up (Steven, 2026-10-24: "players should see how
/// accurate their own runs are, from the game's own numbers, with no labels"). Built only from what the tracker already
/// decides while it reads; nothing here reads the screen. The session hands it each pickup as counted, each line or row
/// set aside for want of a second witness, the Kinah counter's readings and the chat vote's delays; the card, the panel
/// over the game, the audit line, the Discord card and the run payload all read the same <see cref="Summary"/>.
///
/// THE RULES, in plain words (also in docs/accuracy-pipeline.md, "Session accuracy"):
///
/// 1. A <b>judged event</b> is a pickup the tracker counted (Kinah or an item, from the chat, the loot list or the
///    Kinah counter), a pickup it set aside because no second source backed it (a Kina line the counter didn't rise for,
///    a loot list row no chat line confirmed), or a slot on a reward picture (each slot read, and each slot not read).
/// 2. A judged event is <b>verified</b> when a second source the game shows agrees with it: the Kinah counter rose by
///    its amount (or the pickup was counted from that rise), the loot list showed the pickup the chat counted (or the chat
///    showed the row the list counted), a gathering bar finished on Success for the row, a rare drop's second read
///    confirmed it, or it is a slot read off a reward picture and named. A pickup whose count no read showed (count
///    assumed 1) is never verified, whatever else saw it.
/// 3. <b>What the player sees</b> (2026-10-25, Steven: "VERIFIED % BEING LOW COULD CHURN USERS FAST"): of the pickups a
///    second source could check, how many matched. <b>Checked</b> = matched + contradicted; <b>matched</b> = the verified
///    events of rule 2 but the reward slots (a slot read off its picture has no second source); <b>contradicted</b> = a
///    Kina line the counter refused, a loot list row no chat line confirmed.
///    A pickup only one source saw (the chat with the counter quiet, a crafted item, a reward slot read off its picture,
///    a count assumed 1) is <b>not checked</b>, never counted against. The share (matched over checked, rounded down, so
///    100% means every single one) shows only when something was contradicted; otherwise the card says "Nothing looked
///    wrong" or "Checked by the game: all 412 matched". On 2026-10-24 at 19:08 and 19:34 the old share below read 20% and
///    28% while Steven crafted in town with nothing contradicted: every one-source pickup counted against it.
/// 4. <b>Things to look at</b>: every Kina line the counter refused, every loot list row no chat line confirmed, every
///    pickup whose count was assumed 1, and every run whose reward picture was read only in part or not at all. "Fine"
///    takes one off the list for this session; the share doesn't change (it is what the game's numbers say).
/// 5. <b>The old verified share</b>, verified over judged with every judged event in it (one-source pickups and reward
///    slots too), is kept for the audit line only, so logs before and after compare; the window never shows it.
/// 6. Informational, never in the share: loot list rows that went before their text settled (unsure rows), lines that
///    were not rows (a nameplate, a title), Kina lines counted while the counter couldn't judge them (it fell or went
///    quiet), the name decoder's agree / disagree / other, the counter reconciliation and the median counting delay.
/// 7. <b>Counter reconciliation</b>: between trusted readings of the Kinah counter, its rise against the Kinah counted
///    in the same stretch (bound Kina included, as the counter holds it). A fall (Kinah spent) or a rise over
///    <see cref="SaleRise"/> (a sale, mail, a claim) is left out and the next stretch starts from there. Exact, or the
///    count is under or over the counter by N.
/// 8. <b>Counting delay</b>: the chat vote's time from a line's first read to the read it counted on, the median.
///
/// Per run (the run payload's <c>acc</c>): the same rules over the run's own time (its start to its last loot, plus
/// <see cref="RunTailSeconds"/> for the counter's verdicts) and its own reward picture; see <see cref="ForRun"/>.
/// </summary>
public sealed class AccuracyScore
{
    /// <summary>Checked pickups before a share is shown, and before the panel over the game says anything but things to look at.</summary>
    public const int MinEvents = 10;
    /// <summary>Judged events before a run's status can be anything but "unconfirmed".</summary>
    public const int MinRunEvents = 5;
    /// <summary>The run status's thresholds (<see cref="ForRun"/>, the site's own rule too); the session's card no longer has a status.</summary>
    public const double VerifiedAt = 0.95, MostlyAt = 0.80;
    /// <summary>A counter rise larger than this in one stretch is a sale, mail or a claim, not loot: left out of the reconciliation.</summary>
    public const long SaleRise = 20_000;
    /// <summary>A loot row and the chat line for the same pickup are this close in time at most (LootCrossCheck.MatchSeconds plus a read).</summary>
    public const double MatchSeconds = 15;
    /// <summary>The counter judges a run's last Kina lines up to this long after its last loot.</summary>
    public const double RunTailSeconds = 30;
    const int MaxEntries = 20_000, MaxChecks = 500;

    public enum Why { Refused, NoChatLine, CountAssumed, PartlyRead, NotRead }

    /// <summary>The short reason, as the model and the logs name it.</summary>
    public static string Short(Why w) => w switch
    {
        Why.Refused => "refused by the counter",
        Why.NoChatLine => "no chat line",
        Why.CountAssumed => "count assumed 1",
        Why.PartlyRead => "partly read card",
        _ => "rewards not read",
    };

    /// <summary>The reason as a player reads it under the item, one plain sentence.</summary>
    public static string Plain(Why w) => w switch
    {
        Why.Refused => "The Kinah counter didn't rise for it, so it wasn't counted.",
        Why.NoChatLine => "Only the loot list showed it and no chat line did, so it wasn't counted.",
        Why.CountAssumed => "Its count never showed, so 1 was counted.",
        Why.PartlyRead => "Some reward slots weren't read.",
        _ => "Its rewards weren't read. The pictures are kept.",
    };

    /// <summary>
    /// One thing to check. <see cref="Handle"/> is what the miscount report finds its pictures by: the moment, as
    /// frames.jsonl writes it ("O" format, UTC; ReportBuffer.Pin keeps the pictures around it), or "run:" and the run's
    /// start for a run (its reward pictures are in reward-shots, listed in <see cref="Pictures"/>).
    /// </summary>
    public sealed record Check(int Id, DateTime At, string Text, Why Why, string Handle, IReadOnlyList<string> Pictures)
    {
        public string Reason => Short(Why);
        public bool Pickup => Why is Why.Refused or Why.NoChatLine or Why.CountAssumed;
    }

    sealed class Entry
    {
        public DateTime At; public string What = ""; public string Key = ""; public bool Kina;
        public bool ByCounter, ByRow, ByRace, BySecondRead, CountAssumed, CounterLive, Unjudged;
        public bool Verified => (ByCounter || ByRow || ByRace || BySecondRead) && !CountAssumed;
        public bool RowJudged => ByRow || ByRace;
    }

    readonly object _lock = new();
    readonly List<Entry> _counted = new();
    readonly List<(DateTime At, string Text, Why Why, bool Kina, bool Row)> _setAside = new();
    readonly List<Check> _checks = new();
    readonly HashSet<int> _fine = new();
    readonly HashSet<string> _runChecks = new();
    readonly List<(string Key, DateTime At)> _rowAhead = new();   // a row confirmation that came before its pickup was counted
    readonly List<(string Key, DateTime At, string What)> _waitingRows = new();
    readonly List<(DateTime At, double Seconds)> _delays = new();
    int _nextId = 1;
    long? _lastBalance, _lastCounted;
    long _rise, _countedRise;
    int _stretches;

    /// <summary>A thing to check was added (the session pins the miscount report's pictures around it).</summary>
    public event Action<Check>? Added;

    // ---------------------------------------------------------------- what the session hands in

    /// <summary>
    /// A pickup as counted (TrackerSession.Feed): <paramref name="key"/> is the cross-check's key ("k:463", "i:name x2"),
    /// <paramref name="witness"/> the second source already known when it counted ("counter", "race", "second read",
    /// "row", "unjudged"; null: none yet). <paramref name="counterLive"/>: the Kinah counter was read and trusted then.
    /// </summary>
    public void Counted(DateTime at, string what, bool kina, string key, string? witness, bool counterLive)
    {
        lock (_lock)
        {
            var e = new Entry { At = at, What = what, Key = key, Kina = kina, CounterLive = counterLive || witness is "counter" or "unjudged" };
            switch (witness)
            {
                case "counter": e.ByCounter = true; break;
                case "race": e.ByRace = true; break;
                case "second read": e.BySecondRead = true; break;
                case "row": e.ByRow = true; break;
                case "unjudged": e.Unjudged = true; break;
            }
            // the loot list's row for it was matched before it counted (a Kina line waiting for the counter, a late chat line)
            int ahead = key.Length == 0 ? -1 : _rowAhead.FindIndex(r => r.Key == key && Math.Abs((r.At - at).TotalSeconds) <= MatchSeconds);
            if (ahead >= 0) { e.ByRow = true; _rowAhead.RemoveAt(ahead); }
            // a row that waited for its chat line: this is it, the row is no longer a miss
            int w = key.Length == 0 ? -1 : _waitingRows.FindIndex(r => r.Key == key && Math.Abs((r.At - at).TotalSeconds) <= MatchSeconds);
            if (w >= 0) { e.ByRow = true; _waitingRows.RemoveAt(w); }
            _counted.Add(e);
            if (_counted.Count > MaxEntries) _counted.RemoveRange(0, _counted.Count - MaxEntries);
        }
    }

    /// <summary>The loot list showed a pickup the chat read (LootCrossCheck matched the row to the line read at <paramref name="chatAt"/>).</summary>
    public void RowMatched(string key, DateTime chatAt)
    {
        lock (_lock)
        {
            var e = _counted.LastOrDefault(x => x.Key == key && !x.ByRow && Math.Abs((x.At - chatAt).TotalSeconds) <= MatchSeconds);
            if (e != null) { e.ByRow = true; return; }
            _rowAhead.Add((key, chatAt));
            _rowAhead.RemoveAll(r => (chatAt - r.At).TotalSeconds > MatchSeconds * 2);
        }
    }

    /// <summary>A Kina line the counter refused (KinaWitness): set aside, and one thing to check.</summary>
    public void Refused(DateTime at, string line, long amount)
    {
        // the amount only: the line as read is in the report's trace, kept around this moment
        var text = $"{amount:N0} Kinah";
        lock (_lock) _setAside.Add((at, text, Why.Refused, true, false));
        AddCheck(at, text, Why.Refused, Stamp(at), Array.Empty<string>());
    }

    /// <summary>A loot list row that waits for a chat line (not counted yet). Judged by <see cref="Tick"/>: a miss once its time is up.</summary>
    public void RowWaiting(DateTime at, string what, string key)
    {
        lock (_lock) _waitingRows.Add((key, at, what));
    }

    /// <summary>The chat line for a row that waited came (LootCrossCheck: "came late: counted now"); the row is no miss.</summary>
    public void RowConfirmed(string key, DateTime at)
    {
        lock (_lock)
        {
            int w = _waitingRows.FindIndex(r => r.Key == key && Math.Abs((r.At - at).TotalSeconds) <= MatchSeconds);
            if (w >= 0) _waitingRows.RemoveAt(w);
            _rowAhead.Add((key, at));
        }
    }

    /// <summary>Rows that waited past <see cref="MatchSeconds"/> with no chat line become things to check.</summary>
    public void Tick(DateTime now)
    {
        List<(string Key, DateTime At, string What)> due;
        lock (_lock)
        {
            due = _waitingRows.Where(r => (now - r.At).TotalSeconds > MatchSeconds).ToList();
            _waitingRows.RemoveAll(r => (now - r.At).TotalSeconds > MatchSeconds);
            foreach (var r in due) _setAside.Add((r.At, r.What, Why.NoChatLine, r.Key.StartsWith("k:"), true));
        }
        foreach (var r in due) AddCheck(r.At, r.What, Why.NoChatLine, Stamp(r.At), Array.Empty<string>());
    }

    /// <summary>A pickup counted as one because no read showed its count ("quantity unsure, 1 assumed").</summary>
    public void CountAssumed(DateTime at, string line)
    {
        string what;
        lock (_lock)
        {
            // the pickup it was: the latest item counted at or before it whose name the line holds, else the latest item
            var lower = line.ToLowerInvariant();
            var e = _counted.LastOrDefault(x => !x.Kina && !x.CountAssumed && x.At <= at.AddSeconds(1) && (at - x.At).TotalSeconds <= 30 && NameOf(x.What).Length > 0 && lower.Contains(NameOf(x.What).ToLowerInvariant()))
                ?? _counted.LastOrDefault(x => !x.Kina && !x.CountAssumed && x.At <= at.AddSeconds(1) && (at - x.At).TotalSeconds <= 30);
            if (e != null) e.CountAssumed = true;
            what = e?.What ?? $"\"{Trim(line)}\"";
        }
        AddCheck(at, what, Why.CountAssumed, Stamp(at), Array.Empty<string>());
    }

    /// <summary>A trusted reading of the Kinah counter, with the Kinah counted so far (bound included; a line still waiting for the counter is not counted yet).</summary>
    public void Reading(long balance, long counted, DateTime at)
    {
        lock (_lock)
        {
            if (_lastBalance is long b && _lastCounted is long c)
            {
                long rise = balance - b, cRise = counted - c;
                // a fall is Kinah spent, a big rise a sale or mail: that stretch can't be judged, the next starts here
                if (rise >= 0 && rise <= SaleRise && cRise >= 0) { _rise += rise; _countedRise += cRise; if (rise > 0 || cRise > 0) _stretches++; }
            }
            _lastBalance = balance; _lastCounted = counted;
        }
    }

    /// <summary>One counted line's delay, from its first read to the read it counted on (ChatVoter).</summary>
    public void Delay(DateTime at, double seconds)
    {
        if (seconds < 0 || seconds > 120) return;
        lock (_lock) { _delays.Add((at, seconds)); if (_delays.Count > 2000) _delays.RemoveAt(0); }
    }

    /// <summary>"Fine": the player looked and it is right. Off the list for this session; the share doesn't change.</summary>
    public void Dismiss(int id) { lock (_lock) _fine.Add(id); }

    void AddCheck(DateTime at, string text, Why why, string handle, IReadOnlyList<string> pictures)
    {
        Check c;
        lock (_lock)
        {
            if (_checks.Count >= MaxChecks) return;
            c = new Check(_nextId++, at, text, why, handle, pictures);
            _checks.Add(c);
        }
        try { Added?.Invoke(c); } catch (Exception ex) { CrashLog.Write("accuracy check", ex); }
    }

    static string Stamp(DateTime at) => at.ToUniversalTime().ToString("O");
    static string Trim(string s) { s = s.Trim(); return s.Length > 60 ? s[..60] + "…" : s; }
    /// <summary>"Thick hide ×2 (bound)" → "Thick hide".</summary>
    static string NameOf(string what) { int x = what.LastIndexOf(" ×", StringComparison.Ordinal); return x > 0 ? what[..x] : what; }

    // ---------------------------------------------------------------- what the card, the panel and the logs read

    /// <summary>The counter's rise against the Kinah counted over the stretches it could judge: null before a stretch was judged.</summary>
    public sealed record Reconciliation(long CounterRise, long CountedRise)
    {
        /// <summary>Counted minus the counter: 0 exact, below 0 the count is under the counter, above 0 over it.</summary>
        public long Gap => CountedRise - CounterRise;
        public string Words => Gap == 0 ? "exact" : Gap < 0 ? $"under by {-Gap:N0}" : $"over by {Gap:N0}";
    }

    /// <summary>Everything the card and the payload show, at one moment.</summary>
    /// <param name="Matched">Pickups a second source agreed with (rule 3).</param>
    /// <param name="Contradicted">Pickups a second source said no to: Kina lines the counter refused, loot list rows no chat line confirmed.</param>
    /// <param name="NotChecked">Counted pickups and reward slots only one source saw: never counted against.</param>
    public sealed record Summary(
        int Judged, int Verified,
        int Matched, int Contradicted, int NotChecked,
        int KinaCounted, int KinaConfirmed, int KinaRefused, int KinaUnjudged, int KinaNoCounter, bool CounterRead,
        int RowsConfirmed, int RowsNoChat, int RowsUnsure, int NotRows,
        int RunsFull, int RunsPartly, int RunsNotRead, int SlotsRead, int SlotsUnread,
        int CountsAssumed,
        // counted pickups no second source backed: not things to check, since nothing contradicts them
        int SeenOnce,
        long DecodeAgree, long DecodeDisagree, long DecodeOther,
        Reconciliation? Counter, double? MedianDelaySeconds, IReadOnlyList<Check> ToCheck)
    {
        /// <summary>Pickups a second source could check: matched plus contradicted.</summary>
        public int Checked => Matched + Contradicted;
        /// <summary>Matched over checked, 0 to 1; null with nothing checked.</summary>
        public double? MatchShare => Checked > 0 ? (double)Matched / Checked : null;
        /// <summary>The share as shown: a whole percent, rounded down (only every single one is 100%).</summary>
        public int MatchPercent => MatchShare is double s ? (int)Math.Floor(s * 100 + 1e-9) : 0;
        /// <summary>A percentage is shown only when something was contradicted, and only once enough was checked to mean something.</summary>
        public bool ShowsPercent => Contradicted > 0 && Checked >= MinEvents;
        /// <summary>Enough checked for the panel over the game and the Discord card to say so.</summary>
        public bool EnoughChecked => Checked >= MinEvents;

        /// <summary>The old verified share (rule 5): verified over judged, 0 to 1; null with nothing judged. For the audit line only.</summary>
        public double? Share => Judged > 0 ? (double)Verified / Judged : null;
        /// <summary>The old share as a whole percent, rounded down. For the audit line only.</summary>
        public int Percent => Share is double s ? (int)Math.Floor(s * 100 + 1e-9) : 0;
        public bool Enough => Judged >= MinEvents;
    }

    /// <summary>
    /// The session's score now. <paramref name="runs"/> are the runs counted in this session (their reward pictures);
    /// <paramref name="rowsUnsure"/>, <paramref name="notRows"/> the loot list's informational tallies; the decoder's
    /// tallies come from <see cref="Accuracy.Shadows"/>.
    /// </summary>
    public Summary Snapshot(IEnumerable<InstanceRun>? runs = null, int rowsUnsure = 0, int notRows = 0, Dictionary<string, long>? shadows = null)
    {
        foreach (var r in runs ?? Enumerable.Empty<InstanceRun>()) NoteRun(r);
        lock (_lock)
        {
            var rl = (runs ?? Enumerable.Empty<InstanceRun>()).ToList();
            var (slotsRead, slotsUnread) = Slots(rl);
            int judged = _counted.Count + _setAside.Count + slotsRead + slotsUnread;
            int matched = _counted.Count(e => e.Verified);
            int verified = matched + slotsRead;
            var kina = _counted.Where(e => e.Kina).ToList();
            var open = _checks.Where(c => !_fine.Contains(c.Id)).ToList();
            var sh = shadows ?? new();
            // not checked: counted with no second source's word on it, and reward slots read off their picture (a slot
            // not read isn't counted, so it isn't here: its run is on the list)
            return new Summary(judged, verified, matched, _setAside.Count, _counted.Count - matched + slotsRead,
                kina.Count, kina.Count(e => e.ByCounter), _setAside.Count(s => s.Why == Why.Refused), kina.Count(e => e.Unjudged), kina.Count(e => !e.CounterLive),
                kina.Any(e => e.CounterLive) || _setAside.Any(s => s.Why == Why.Refused),
                _counted.Count(e => e.RowJudged), _setAside.Count(s => s.Row), rowsUnsure, notRows,
                rl.Count(r => r.FromPicture && !r.ToLookAt), rl.Count(r => r.ToLookAt),
                rl.Count(r => r.RewardsNotRead), slotsRead, slotsUnread,
                _counted.Count(e => e.CountAssumed), _counted.Count(e => !e.Verified && !e.CountAssumed), sh.GetValueOrDefault("decode_agree"), sh.GetValueOrDefault("decode_disagree"), sh.GetValueOrDefault("decode_other"),
                _stretches > 0 ? new Reconciliation(_rise, _countedRise) : null, Median(_delays.Select(d => d.Seconds)), open);
        }
    }

    /// <summary>A run's reward slots: read and named (verified), and not read (unknown gear counts as not read: its piece is there, its item isn't known).</summary>
    static (int Read, int Unread) Slots(IEnumerable<InstanceRun> runs)
    {
        int read = 0, unread = 0;
        foreach (var r in runs)
        {
            if (!r.FromPicture) continue;
            int all = r.PictureSlots > 0 ? r.PictureSlots : r.Items.Count + r.BoundItems.Count + r.SlotsUnread;
            int miss = Math.Min(all, r.SlotsUnread + r.UnknownGear);
            read += Math.Max(0, all - miss); unread += miss;
        }
        return (read, unread);
    }

    /// <summary>A run read only in part, or not at all, is one thing to check (once).</summary>
    void NoteRun(InstanceRun r)
    {
        bool partly = r.ToLookAt;
        if (!partly && !r.RewardsNotRead) return;
        var handle = "run:" + Stamp(r.Start);
        lock (_lock) if (!_runChecks.Add(handle)) return;
        var label = r.Event.Length > 0 ? r.Event : r.Zone;
        var parts = new List<string>();
        if (r.SlotsUnread > 0 || (r.UnknownGear == 0 && r.SlotDoubts.Count == 0)) parts.Add($"{r.SlotsUnread} reward slot{(r.SlotsUnread == 1 ? "" : "s")} not read");
        if (r.UnknownGear > 0) parts.Add($"{r.UnknownGear} gear not named");
        // a count the picture read that the chat or a second read of the screen disagreed with (2026-10-25)
        if (r.SlotDoubts.Count > 0) parts.Add($"{r.SlotDoubts.Count} reward count{(r.SlotDoubts.Count == 1 ? "" : "s")} in doubt");
        var text = r.RewardsNotRead ? $"{label}: rewards not read" : $"{label}: {string.Join(", ", parts)}";
        AddCheck(r.LastLoot ?? r.Start, text, r.RewardsNotRead ? Why.NotRead : Why.PartlyRead, handle, r.Pictures.ToList());
    }

    static double? Median(IEnumerable<double> xs)
    {
        var l = xs.OrderBy(x => x).ToList();
        if (l.Count == 0) return null;
        return l.Count % 2 == 1 ? l[l.Count / 2] : (l[l.Count / 2 - 1] + l[l.Count / 2]) / 2;
    }

    // ---------------------------------------------------------------- per run, for the run payload

    /// <summary>
    /// The run's accuracy for the site (InstanceRunPayload.acc), or null when nothing was judged in it. Over the run's
    /// own time, from its start to its last loot plus <see cref="RunTailSeconds"/>:
    /// <list type="bullet">
    /// <item><c>kina_confirmed</c>: Kina pickups the counter confirmed over Kina pickups and refused lines; null when no Kina line
    /// in the run was judged by a read counter.</item>
    /// <item><c>rows_confirmed</c>: loot list rows confirmed by a chat line (or a gathering bar's Success) over those and
    /// rows no chat line confirmed; null when no row was judged.</item>
    /// <item><c>unsure</c>: things to check in the run (refused, no chat line, count assumed, and the run's own reward picture
    /// when it was read in part).</item>
    /// <item><c>status</c> (the coordinator's rule, 2026-10-24): "verified" when kina_confirmed ≥ 0.95, not partly read and nothing
    /// unsure; "partly" when partly read or unsure but kina_confirmed ≥ 0.8; else "unconfirmed". With kina_confirmed null
    /// (no Kina judged: an event paid in cards) the run's verified share stands in for it. Under <see cref="MinRunEvents"/>
    /// judged events: "unconfirmed", with the note "fewer than 5 counted".</item>
    /// </list>
    /// </summary>
    public RunAccuracy? ForRun(InstanceRun r)
    {
        var from = r.Start; var to = (r.LastLoot ?? r.Start).AddSeconds(RunTailSeconds);
        bool In(DateTime t) => t >= from && t <= to;
        lock (_lock)
        {
            var c = _counted.Where(e => In(e.At)).ToList();
            var aside = _setAside.Where(s => In(s.At)).ToList();
            var (slotsRead, slotsUnread) = Slots(new[] { r });
            int judged = c.Count + aside.Count + slotsRead + slotsUnread;
            if (judged == 0 && !r.PartlyRead) return null;
            int verified = c.Count(e => e.Verified) + slotsRead;
            var kina = c.Where(e => e.Kina).ToList();
            int refused = aside.Count(s => s.Why == Why.Refused);
            bool counter = kina.Any(e => e.CounterLive) || refused > 0;
            double? kinaShare = counter && kina.Count + refused > 0 ? Round((double)kina.Count(e => e.ByCounter) / (kina.Count + refused)) : null;
            int rowsOk = c.Count(e => e.RowJudged), rowsMiss = aside.Count(s => s.Row);
            double? rowShare = rowsOk + rowsMiss > 0 ? Round((double)rowsOk / (rowsOk + rowsMiss)) : null;
            int unsure = aside.Count + c.Count(e => e.CountAssumed) + (r.ToLookAt ? 1 : 0);
            var delays = _delays.Where(d => In(d.At)).Select(d => d.Seconds).ToList();
            double? med = Median(delays);
            double share = judged > 0 ? (double)verified / judged : 0;
            double test = kinaShare ?? share;
            string status, note = "";
            if (judged < MinRunEvents) { status = "unconfirmed"; note = $"fewer than {MinRunEvents} counted"; }
            else if (test >= VerifiedAt && !r.PartlyRead && unsure == 0) status = "verified";
            else if ((r.PartlyRead || unsure > 0) && test >= MostlyAt) status = "partly";
            else status = "unconfirmed";
            // the player's measure (rule 3), added beside the fields above, whose meaning stays: the site can show matched
            // over checked and leave not_checked out of any share
            int matched = c.Count(e => e.Verified);
            return new RunAccuracy(status, kinaShare, rowShare, unsure, r.SlotsUnread, r.PartlyRead, med is double m ? (int)Math.Round(m * 1000) : null,
                Upload.RunPayload.AppVersion, @checked: matched + aside.Count, matched: matched, not_checked: c.Count - matched + slotsRead, note: note.Length > 0 ? note : null);
        }
    }

    static double Round(double v) => Math.Round(v, 3);

    /// <summary>
    /// The audit line's clause: what the player sees ("checked 412, matched 410 (99%), 2 to look at, 37 not checked"),
    /// then the old share for comparison with older logs ("old verified share 20%", or "not enough counted yet").
    /// </summary>
    public static string AuditClause(Summary s) =>
        $"checked {s.Checked}, matched {s.Matched}" + (s.Contradicted > 0 && s.Checked > 0 ? $" ({s.MatchPercent}%)" : "")
        + $", {s.ToCheck.Count} to look at, {s.NotChecked} not checked · old verified share "
        + (s.Enough ? $"{s.Percent}%" : $"not enough counted yet ({s.Judged})");
}

/// <summary>
/// One run's accuracy as the site gets it (InstanceRunPayload.acc, since 2026-10-24; absent on older trackers and on runs
/// with nothing judged). See <see cref="AccuracyScore.ForRun"/> for each field's rule. Since 2026-10-25 also
/// <c>checked</c>, <c>matched</c> and <c>not_checked</c>, the player's measure (AccuracyScore rule 3): pickups a second
/// source could check, those that matched, and those only one source saw. The fields before them keep their meaning;
/// the site's accBlock drops fields it doesn't know, so older sites are unaffected.
/// </summary>
public sealed record RunAccuracy(
    string status,
    double? kina_confirmed,
    double? rows_confirmed,
    int unsure,
    int not_read,
    bool partly_read,
    int? delay_ms,
    string ver,
    int @checked = 0,
    int matched = 0,
    int not_checked = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? note = null);
