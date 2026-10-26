namespace KinahLedger.Session;

/// <summary>
/// The Kinah counter as the deciding witness for Kina (2026-10-24, the 131029 farming clip: 52 kills' Kina lines, the
/// counter rose by exactly their sum, 15,660, while the chat path missed four lines and counted three twice).
///
/// While the counter is read and trusted (a reading at least every <see cref="LiveSeconds"/>), each Kina chat line is a
/// candidate, not a count: it counts when the counter rose by its amount (or by the sum of a burst), and the rises go
/// into a pool the candidates are matched against. A line whose amount the counter didn't rise for (a re-read of a line
/// already counted, a line pushed up by a scroll and read as new, a misread amount like "2367" for 236) is refused once
/// two readings after it show no room for it. A rise nothing claims, after <see cref="SettleSeconds"/>, is counted from
/// the counter, named after the uncounted Kina lines the chat showed when their amounts make it up exactly (a fragment,
/// the last line of a read that couldn't be lined up), else as Kina from the counter. A fall (Kinah spent) can't be
/// judged: what waits then counts as read, and the pool starts again. Without a trusted counter nothing changes: the
/// session counts each line as before. Pure arithmetic, no screen: parse-test drives it, the bank feeds it "balance" reads.
/// </summary>
public sealed class KinaWitness
{
    /// <summary>The counter is a witness while a trusted reading is at most this old.</summary>
    public const double LiveSeconds = 15;
    /// <summary>A candidate waits at most this long for a reading that judges it.</summary>
    public const double WaitSeconds = 20;
    /// <summary>Readings after a candidate before it can be refused (the counter moves in the frame its line appears).</summary>
    public const int ReadingsToRefuse = 2;
    /// <summary>A rise no line claims is counted from the counter once it has held this long.</summary>
    public const double SettleSeconds = 8;
    /// <summary>A rise no line claims and larger than this is a sale, mail or a claim, not a kill's Kina: logged, never counted.</summary>
    public const long MaxFromCounter = 5_000;
    /// <summary>Uncounted Kina lines the chat showed are kept this long, to name a rise.</summary>
    public const double SeenSeconds = 60;

    /// <summary>A Kina line waiting for the counter. <see cref="Tag"/> is the caller's event; null for Kina counted elsewhere (bound, a payment) that only explains a rise.</summary>
    public sealed record Candidate(long Amount, DateTime At, string Line, object? Tag);

    /// <summary>What a reading or an offer decided. FromCounter: amounts counted from the counter, each with the chat line it is named after (null: none).</summary>
    /// <summary>Absorbed: lines whose rise was already counted from the counter (their line came late): neither counted again nor refused.</summary>
    public sealed record Outcome(List<Candidate> Confirmed, List<Candidate> Refused, List<Candidate> Unjudged, List<(long Amount, string? Line, DateTime At)> FromCounter, List<string> Notes, List<Candidate> Absorbed)
    {
        public static Outcome Empty() => new(new(), new(), new(), new(), new(), new());
        public bool Any => Confirmed.Count + Refused.Count + Unjudged.Count + FromCounter.Count + Notes.Count + Absorbed.Count > 0;
    }

    /// <summary>A line with the amount of one confirmed this lately is a suspected repeat: it counts only on a rise of exactly its amount.</summary>
    public const double RepeatSeconds = 15;
    bool Suspect(long amount, DateTime now) => _done.Any(d => d.Amount == amount && (now - d.At).TotalSeconds <= RepeatSeconds);

    /// <summary>A line late for a rise already counted from the counter is that rise's line, for this long.</summary>
    public const double CreditSeconds = 20;
    // amounts counted from the counter lately, which a late line takes instead of being counted again
    readonly List<(long Amount, DateTime At)> _credit = new();

    sealed class Wait { public Candidate C = null!; public int Readings; }
    readonly List<Wait> _pending = new();
    readonly Dictionary<long, (DateTime At, string Line)> _seen = new();
    readonly List<(long Amount, DateTime At)> _done = new();
    long? _last; DateTime _lastAt = DateTime.MinValue;
    long _pool; DateTime _poolSince, _poolFirst;

    public int Confirmed { get; private set; }
    public int Refused { get; private set; }
    public long FromCounter { get; private set; }
    public int Readings { get; private set; }

    /// <summary>A trusted reading came lately: Kina lines wait for the counter.</summary>
    public bool Live(DateTime now) => _last != null && (now - _lastAt).TotalSeconds <= LiveSeconds && (_lastAt - now).TotalSeconds <= LiveSeconds;
    /// <summary>The Kina waiting for the counter (not yet in the count).</summary>
    public long PendingSum => _pending.Where(w => w.C.Tag != null).Sum(w => w.C.Amount);
    public long Pool => _pool;

    /// <summary>A Kina line read: it counts when the counter rose by its amount. Decided at once when the pool already holds it.</summary>
    public Outcome Offer(Candidate c, DateTime now)
    {
        var o = Outcome.Empty();
        _credit.RemoveAll(x => (now - x.At).TotalSeconds > CreditSeconds);
        int ci = Suspect(c.Amount, now) ? -1 : _credit.FindIndex(x => x.Amount >= c.Amount);
        if (ci >= 0 && (_pool < c.Amount || _credit[ci].Amount == c.Amount))
        {
            var (amt, at) = _credit[ci];
            if (amt == c.Amount) _credit.RemoveAt(ci); else _credit[ci] = (amt - c.Amount, at);
            _done.Add((c.Amount, now)); _seen.Remove(c.Amount);   // a second line of this amount now is a suspected repeat
            if (c.Tag != null) o.Absorbed.Add(c);
            return o;
        }
        _pending.Add(new Wait { C = c });
        Match(o, now);
        return o;
    }

    /// <summary>A Kina amount the chat showed (any read, counted or not): kept to name a rise no line claims.</summary>
    public void Seen(long amount, string line, DateTime now)
    {
        if (amount <= 0) return;
        if (_done.Any(d => d.Amount == amount && (now - d.At).TotalSeconds <= SeenSeconds)) return;
        _seen[amount] = _seen.TryGetValue(amount, out var v) ? (v.At, v.Line) : (now, line);
    }

    /// <summary>A trusted reading of the counter.</summary>
    public Outcome Observe(long value, DateTime at)
    {
        var o = Outcome.Empty();
        Readings++;
        if (_last == null) { _last = value; _lastAt = at; return o; }
        if ((at - _lastAt).TotalSeconds > LiveSeconds)
        {
            // the counter went unread longer than a witness may (Tick let the lines waiting count as read, and the lines since
            // counted without it): what it rose meanwhile is theirs. This reading starts the pool again, and nothing is
            // counted from that rise (2026-10-26: a fight's 4,076 Kina counted from its lines and again from the counter)
            if (value != _last.Value || _pool != 0) o.Notes.Add($"the Kinah counter read again after {(at - _lastAt).TotalSeconds:0} s ({value - _last.Value:+#,0;-#,0;0} meanwhile, Kina counted without it): the counter's pool starts again from here");
            foreach (var w in _pending.Where(w => w.C.Tag != null)) o.Unjudged.Add(w.C);
            _pending.Clear();
            _last = value; _lastAt = at; _pool = 0;
            return o;
        }
        long delta = value - _last.Value;
        _last = value; _lastAt = at;
        if (delta < 0)
        {
            // Kinah spent (a teleport, a repair, a purchase): what waits can't be judged against this reading
            foreach (var w in _pending.Where(w => w.C.Tag != null)) o.Unjudged.Add(w.C);
            _pending.Clear();
            if (_pool != 0 || o.Unjudged.Count > 0) o.Notes.Add($"the Kinah counter fell {-delta:N0} (Kinah spent): {o.Unjudged.Count} Kina line(s) waiting count as read, the counter's pool starts again");
            _pool = 0;
            return o;
        }
        // the pool settles once quiet: SettleSeconds after its newest rise
        if (delta > 0) { if (_pool <= 0) _poolFirst = at; _poolSince = at; _pool += delta; }
        foreach (var w in _pending) if (at > w.C.At) w.Readings++;
        Match(o, at);
        Judge(o, at, reading: true);
        return o;
    }

    /// <summary>Between readings: a counter gone quiet releases what waits (counted as read); old candidates are judged.</summary>
    public Outcome Tick(DateTime now)
    {
        var o = Outcome.Empty();
        if (_pending.Count == 0) return o;
        if (!Live(now))
        {
            foreach (var w in _pending.Where(w => w.C.Tag != null)) o.Unjudged.Add(w.C);
            if (o.Unjudged.Count > 0) o.Notes.Add($"the Kinah counter hasn't been read for {(now - _lastAt).TotalSeconds:0} s: {o.Unjudged.Count} Kina line(s) count as read");
            _pending.Clear();
            return o;
        }
        Judge(o, now, reading: false);
        return o;
    }

    /// <summary>Everything still waiting, as read (a stop, the end of a replay); a rise no line claims is settled now.</summary>
    public Outcome Flush(DateTime now)
    {
        var o = Outcome.Empty();
        Judge(o, now.AddSeconds(WaitSeconds + SettleSeconds), reading: true, final: true);
        foreach (var w in _pending.Where(w => w.C.Tag != null)) o.Unjudged.Add(w.C);
        _pending.Clear();
        return o;
    }

    /// <summary>Candidates whose amounts make up the pool exactly (oldest first, at most 12 tried): confirmed together.</summary>
    void Match(Outcome o, DateTime now)
    {
        if (_pool <= 0 || _pending.Count == 0) return;
        var ws = _pending.Take(12).ToList();
        int n = ws.Count;
        int best = -1;
        // the fewest lines that make it up, the oldest on a tie: mask order with a popcount
        for (int k = 1; k <= n && best < 0; k++)
            for (int mask = 1; mask < (1 << n) && best < 0; mask++)
            {
                if (System.Numerics.BitOperations.PopCount((uint)mask) != k) continue;
                long sum = 0;
                for (int b = 0; b < n; b++) if ((mask & (1 << b)) != 0) sum += ws[b].C.Amount;
                if (sum == _pool) best = mask;
            }
        if (best < 0) return;
        for (int b = 0; b < n; b++)
            if ((best & (1 << b)) != 0) Confirm(o, ws[b], now);
    }

    void Confirm(Outcome o, Wait w, DateTime now)
    {
        _pending.Remove(w);
        _pool -= w.C.Amount;
        _done.Add((w.C.Amount, now));
        _seen.Remove(w.C.Amount);
        if (w.C.Tag != null) { o.Confirmed.Add(w.C); Confirmed++; }
    }

    void Judge(Outcome o, DateTime now, bool reading, bool final = false)
    {
        _done.RemoveAll(d => (now - d.At).TotalSeconds > SeenSeconds);
        foreach (var k in _seen.Where(kv => (now - kv.Value.At).TotalSeconds > SeenSeconds).Select(kv => kv.Key).ToList()) _seen.Remove(k);
        // oldest first: a candidate judged now fits the pool (part of a rise, the rest a line still to come or missed) or is refused
        foreach (var w in _pending.ToList())
        {
            bool due = final || (w.Readings >= ReadingsToRefuse) || (now - w.C.At).TotalSeconds >= WaitSeconds;
            if (!due) continue;
            // a suspected repeat (the same amount confirmed moments ago) counts only on a rise of exactly its amount
            if (w.C.Amount <= _pool && (_pool == w.C.Amount || w.C.Tag == null || !Suspect(w.C.Amount, now))) { Confirm(o, w, now); continue; }
            // no reading came after it (the end of a replay, a stop): the counter never had a chance to show it
            if (final && w.Readings == 0) continue;
            _pending.Remove(w);
            if (w.C.Tag == null) continue;   // Kina counted elsewhere the counter never showed: nothing to undo
            o.Refused.Add(w.C); Refused++;
        }
        // a rise no line claims, held a while with nothing waiting that could claim it
        if (_pool > 0 && (final || (now - _poolSince).TotalSeconds >= SettleSeconds) && !_pending.Any(w => w.C.Amount <= _pool))
        {
            long pool = _pool;
            _pool = 0;
            if (pool > MaxFromCounter) { o.Notes.Add($"the Kinah counter rose {pool:N0} that no Kina line claims: more than a kill's Kina (a sale, mail or a claim), not counted"); return; }
            // named after the uncounted Kina lines the chat showed, when their amounts make it up exactly
            var seen = _seen.Where(kv => kv.Key <= pool).OrderBy(kv => kv.Value.At).Take(12).ToList();
            int pick = -1;
            for (int mask = 1; mask < (1 << seen.Count) && pick < 0; mask++)
            {
                long sum = 0;
                for (int b = 0; b < seen.Count; b++) if ((mask & (1 << b)) != 0) sum += seen[b].Key;
                if (sum == pool) pick = mask;
            }
            if (pick > 0)
                for (int b = 0; b < seen.Count; b++)
                {
                    if ((pick & (1 << b)) == 0) continue;
                    o.FromCounter.Add((seen[b].Key, seen[b].Value.Line, seen[b].Value.At));
                    _seen.Remove(seen[b].Key);
                    _credit.Add((seen[b].Key, now));
                }
            else { o.FromCounter.Add((pool, null, _poolFirst)); _credit.Add((pool, now)); }
            FromCounter += pool;
        }
    }
}
