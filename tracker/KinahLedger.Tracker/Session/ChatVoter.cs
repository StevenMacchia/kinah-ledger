namespace KinahLedger.Session;

/// <summary>
/// Multi-frame voting on new chat lines (Steven, 2026-10-22). A new loot line stays on screen for several reads, and the
/// recognizer can garble one of them ("Acquired 460 Kina." for 463, a name a letter off). So each new line waits for up
/// to <see cref="Reads"/> readings or <see cref="MaxSeconds"/>, then the reading most of them agree on is the one parsed:
/// the same Kinah amount, or the same item name, an exact name beating a guessed one on a tie. A line that scrolled or
/// faded away before that goes with what was read. Lines come out in the order they came in, so a payment or a purchase
/// still claims the lines after it.
/// </summary>
public sealed class ChatVoter
{
    public const int Reads = 3;
    public const double MaxSeconds = 1.0;

    sealed class Pending { public string First = ""; public DateTime At; public readonly List<string> Readings = new(); public bool Gone; }
    readonly List<Pending> _pending = new();

    /// <summary>How many lines a vote changed, for the log and the audit.</summary>
    public int Changed { get; private set; }

    /// <summary>Lines still waiting for their vote.</summary>
    public int Waiting => _pending.Count;

    /// <summary>
    /// Set by the session (2026-10-24): whether the loot list beside the character shows, with its icon, a row that means
    /// exactly what this reading means (the same Kinah amount, the same item and count). Two places read apart agreeing
    /// is what the vote waits for, so such a line counts on the read it came in, the reading the list agrees with.
    /// </summary>
    public Func<string, bool>? Confirmed { get; set; }

    /// <summary>Fragments ("3 Kina.") whose whole line the next reads showed in their place.</summary>
    public int Fragments { get; private set; }

    /// <summary>Lines counted before their vote was over because the loot list showed the same pickup.</summary>
    public int Fast { get; private set; }

    /// <summary>For each loot line counted (a line with a meaning), the seconds from the read it came in to the read it counted on; the last 500.</summary>
    public List<double> Latencies { get; } = new();
    /// <summary>Each of those as it is measured, with when it counted (AccuracyScore's median counting delay).</summary>
    public Action<DateTime, double>? Measured { get; set; }

    /// <summary>
    /// One read: the new lines it brought (at the bottom of <paramref name="read"/>) and the whole read. Returns the lines
    /// whose vote is over, oldest first, each as the reading chosen. <paramref name="meaning"/> says what a line counts as
    /// ("k:463" or "i:raw leather x1", and whether the name was exact; "" for nothing).
    /// </summary>
    public List<string> Push(IReadOnlyList<string> fresh, IReadOnlyList<string> read, DateTime now, Func<string, (string Key, bool Exact)> meaning)
    {
        // the lines already waiting are above this read's new ones; matched from the bottom up, in order
        int cursor = read.Count - fresh.Count - 1;
        for (int p = _pending.Count - 1; p >= 0; p--)
        {
            var w = _pending[p];
            if (w.Gone) continue;
            // the same text anywhere a few lines up (a line missing from this read shifts it), a near reading only in its
            // own place: a neighbour a digit or a letter off is another pickup more often than a misread
            int found = -1;
            for (int i = cursor; i >= 0 && i >= cursor - 3; i--) if (Key(w.First) == Key(read[i])) { found = i; break; }
            if (found < 0 && cursor >= 0 && Close(w.First, read[cursor])) found = cursor;
            // a fragment read while the line faded in ("3 Kina.", "d 382 Kina.", "508 Kina.": nothing to count) and now the
            // whole line in its place, ending the same way: the same line, read better (2026-10-24, 293, 382 and 508 lost)
            if (found < 0 && cursor >= 0 && Key(w.First) is { Length: >= 4 } fk && meaning(w.First).Key.Length == 0
                && Key(read[cursor]) is var rk && rk.Length > fk.Length && rk.EndsWith(fk, StringComparison.Ordinal) && meaning(read[cursor]).Key.Length > 0) { found = cursor; Fragments++; }
            if (found >= 0) { w.Readings.Add(read[found]); cursor = found - 1; }
            else w.Gone = true;
        }
        foreach (var f in fresh) { var w = new Pending { First = f, At = now }; w.Readings.Add(f); _pending.Add(w); }
        var outp = new List<string>();
        while (_pending.Count > 0)
        {
            var w = _pending[0];
            string? sure = null;
            if (w.Readings.Count < Reads && !w.Gone && (now - w.At).TotalSeconds < MaxSeconds)
            {
                // the loot list shows this pickup: no vote needed (in order still: only the first waiting line can go)
                sure = Confirmed == null ? null : w.Readings.LastOrDefault(r => Confirmed(r));
                if (sure == null) break;
                Fast++;
            }
            var chosen = sure ?? Choose(w, meaning);
            if (meaning(chosen).Key.Length > 0) { Latencies.Add((now - w.At).TotalSeconds); if (Latencies.Count > 500) Latencies.RemoveAt(0); Measured?.Invoke(now, (now - w.At).TotalSeconds); }
            outp.Add(chosen);
            _pending.RemoveAt(0);
        }
        return outp;
    }

    /// <summary>Everything still waiting, as read so far (a stop, a pause).</summary>
    public List<string> Flush(Func<string, (string Key, bool Exact)> meaning)
    {
        var outp = _pending.Select(w => Choose(w, meaning)).ToList();
        _pending.Clear();
        return outp;
    }

    string Choose(Pending w, Func<string, (string Key, bool Exact)> meaning)
    {
        if (w.Readings.Count == 1) return w.First;
        var read = w.Readings.Select(r => (Text: r, M: meaning(r))).Where(x => x.M.Key.Length > 0).ToList();
        if (read.Count == 0) return w.First;
        var best = read.GroupBy(x => x.M.Key)
            .Select(g => (Key: g.Key, Count: g.Count(), Exact: g.Any(x => x.M.Exact), Text: g.First().Text))
            .OrderByDescending(g => g.Count).ThenByDescending(g => g.Exact).First();
        if (best.Key != meaning(w.First).Key)
        {
            Changed++;
            CrashLog.Note($"voted over {w.Readings.Count} reads: \"{w.First}\" is \"{best.Text}\"");
        }
        return best.Text;
    }

    static string Key(string s) => new string(s.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    /// <summary>
    /// The same line read twice: letters a few apart at most, and the same digits, or one digit lost to a garbled
    /// character ("46&" for 468). Two Kinah lines differ only in their digits, and a digit read as another can't be told
    /// from another pickup ("386" and "486" were both real at 19:00), so that is never merged.
    /// </summary>
    static bool Close(string a, string b)
    {
        var x = Key(a); var y = Key(b);
        if (x == y) return true;
        if (x.Length < 6 || y.Length < 6) return false;
        string Num(string s) => new string(s.Where(char.IsDigit).ToArray());
        string nx = Num(x), ny = Num(y);
        if (nx != ny && !(Math.Abs(nx.Length - ny.Length) == 1 && Lev(nx, ny) == 1)) return false;
        int max = Math.Max(2, Math.Min(x.Length, y.Length) / 4);
        if (Math.Abs(x.Length - y.Length) > max) return false;
        return Lev(x, y) <= max;
    }

    static int Lev(string x, string y)
    {
        var prev = new int[y.Length + 1]; var cur = new int[y.Length + 1];
        for (int j = 0; j <= y.Length; j++) prev[j] = j;
        for (int i = 1; i <= x.Length; i++)
        {
            cur[0] = i;
            for (int j = 1; j <= y.Length; j++) cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + (x[i - 1] == y[j - 1] ? 0 : 1));
            (prev, cur) = (cur, prev);
        }
        return prev[y.Length];
    }
}
