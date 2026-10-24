using System.Text;

namespace KinahLedger.Ocr;

/// <summary>
/// A loot line read while it was still fading in comes out garbled ("ria acquired" for "Aria acquired.", "»eqwacquired."
/// for a second gather landing a moment after the first). It matches no item, so nothing is counted, and on the next read
/// the clean line sits where the garbled one did, so the line tracker takes it for a line already seen. Fast gathers
/// (a critical success, a node in one tap) lost their second item this way.
///
/// So a new line that looks like loot but parsed to nothing is remembered with its place, counted from the bottom of the
/// chat. On the next read the line now at that place (moved up by the new lines that arrived under it) is taken for one
/// more parse when it has changed and is still close to what was read before, so a different line that scrolled into
/// the place is never counted. A line still garbled the same way waits for one more read; after that it is dropped.
/// </summary>
public sealed class Reread
{
    sealed record Pending(string Raw, int FromBottom, DateTime At, int Tries);
    readonly List<Pending> _pending = new();

    public int Recovered { get; private set; }

    /// <summary>Loot-shaped: an acquired or obtained line, or a Kina line.</summary>
    public static bool LooksLikeLoot(string line)
    {
        var k = Key(line);
        // a letter or two off the verb is still loot ("Asquired Fine Odyle x4.", "Acquiied", "Obtaincd"): the burst dropped
        // Fine Odyle x4 from the 10:06 event rewards over one letter (Steven, 2026-10-23)
        return k.Contains("acqui") || k.Contains("obtain") || k.Contains("cquired") || k.Contains("quired") || k.Contains("acqu") || k.Contains("btain")
            || k.EndsWith("kina") || k.EndsWith("kinah") || System.Text.RegularExpressions.Regex.IsMatch(k, @"[a-z]x\d{1,4}$");
    }

    /// <summary>A new line that looked like loot and parsed to nothing, at its place counted from the bottom (0 = the last line).</summary>
    public void Remember(string raw, int fromBottom, DateTime at)
    {
        if (_pending.Count < 8) _pending.Add(new Pending(raw, fromBottom, at, 0));
    }

    /// <summary>
    /// The lines of this read worth one more parse: what now sits where a garbled loot line was. <paramref name="freshCount"/>
    /// is how many new lines this read added under the old ones. Call once per read, before remembering this read's lines.
    /// </summary>
    public List<string> Take(IReadOnlyList<string> lines, int freshCount, DateTime now)
    {
        var take = new List<string>();
        if (_pending.Count == 0) return take;
        if (lines.Count == 0) { _pending.RemoveAll(p => (now - p.At).TotalSeconds > 4); return take; }   // faded: wait a little
        var keep = new List<Pending>();
        foreach (var p in _pending)
        {
            int pos = p.FromBottom + freshCount;
            if (pos >= lines.Count) continue;
            var cand = lines[lines.Count - 1 - pos];
            if (cand == p.Raw)
            {
                // still the same garbled text: one more look, then let it go
                if (p.Tries < 1 && (now - p.At).TotalSeconds <= 4) keep.Add(p with { FromBottom = pos, Tries = p.Tries + 1 });
                continue;
            }
            if (!Near(Key(cand), Key(p.Raw))) continue;
            take.Add(cand);
        }
        _pending.Clear();
        _pending.AddRange(keep);
        Recovered += take.Count;
        return take;
    }

    /// <summary>The same line read twice, the first time while it faded in: within about two letters in five.</summary>
    static bool Near(string a, string b)
    {
        if (a.Length < 6 || b.Length < 3) return false;
        int max = Math.Max(2, (int)(Math.Max(a.Length, b.Length) * 0.4));
        return Math.Abs(a.Length - b.Length) <= max && Lev(a, b) <= max;
    }

    static int Lev(string a, string b)
    {
        var prev = new int[b.Length + 1]; var cur = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++) prev[j] = j;
        for (int i = 1; i <= a.Length; i++)
        {
            cur[0] = i;
            for (int j = 1; j <= b.Length; j++) cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            (prev, cur) = (cur, prev);
        }
        return prev[b.Length];
    }

    /// <summary>Every line of a read, by key, with when it was last on screen; keys older than ten minutes go.</summary>
    /// <summary>The lines of a read not seen in the last ten minutes (<paramref name="recent"/>, as kept by <see cref="Remember"/>).</summary>
    public static List<string> Unseen(Dictionary<string, DateTime> recent, IReadOnlyList<string> lines, DateTime now) =>
        lines.Where(l => Key(l) is { Length: > 0 } k && !(recent.TryGetValue(k, out var at) && (now - at).TotalMinutes <= 10)).ToList();

    public static void Remember(Dictionary<string, DateTime> recent, IReadOnlyList<string> lines, DateTime now)
    {
        foreach (var l in lines) { var k = Key(l); if (k.Length > 0) recent[k] = now; }
        if (recent.Count > 600) foreach (var k in recent.Where(kv => (now - kv.Value).TotalMinutes > 10).Select(kv => kv.Key).ToList()) recent.Remove(k);
    }

    /// <summary>
    /// A burst: more loot than the chat shows at once arrived between two reads, so this read shares no line with the last
    /// one. Only then (a read that merely failed to line up shares most of its lines with the last and gives nothing here),
    /// its loot-shaped lines that are not close to any line seen in the last ten minutes are new.
    /// </summary>
    public static List<string> Burst(IReadOnlyList<string> last, IReadOnlyList<string> cur, Dictionary<string, DateTime> recent, DateTime now)
    {
        var take = new List<string>();
        if (cur.Count < 3) return take;
        if (last.Count == 0)
        {
            // the chat came back from a fade with a whole reward at once (the Accursed Sword event's eleven lines, 2026-10-22
            // 14:35): nothing to line up with. From the bottom up, the loot lines not seen in the last ten minutes are new,
            // up to the first line that was; history shown by a hover ends with a line already seen, so it adds nothing.
            // Only once the tracker has been reading a while, so a fresh start never takes the history for new.
            if (recent.Count < 20 || (now - recent.Values.Min()).TotalMinutes < 2) return take;
            var since = recent.Where(kv => (now - kv.Value).TotalMinutes <= 10).Select(kv => kv.Key).ToList();
            for (int i = cur.Count - 1; i >= 0; i--)
            {
                var k = Key(cur[i]);
                if (k.Length == 0) continue;
                if (since.Any(p => Same(k, p))) break;
                if (LooksLikeLoot(cur[i]) && !cur[i].TrimStart().StartsWith('[')) take.Insert(0, cur[i]);
            }
            return take;
        }
        var lastKeys = last.Select(Key).Where(k => k.Length > 0).ToList();
        if (cur.Any(l => { var k = Key(l); return k.Length > 0 && lastKeys.Any(p => Near(k, p) || k == p); })) return take;   // overlaps: not a burst
        var seen = recent.Where(kv => (now - kv.Value).TotalMinutes <= 10).Select(kv => kv.Key).ToList();
        foreach (var l in cur)
        {
            if (!LooksLikeLoot(l) || l.TrimStart().StartsWith('[')) continue;
            var k = Key(l);
            if (seen.Any(p => p == k || Near(k, p))) continue;
            take.Add(l);
        }
        return take;
    }

    /// <summary>
    /// A read that couldn't be lined up but does share lines with the last one (the quest tracker's lines garbled
    /// differently in the two reads, so no run of lines matched): its lines after the last line both reads share are new,
    /// less the last read's own lines after that point that are still on screen. Only loot-shaped lines are returned.
    /// Lines are compared tightly, numbers exactly, so "Acquired 1,192 Kina." is never taken for "Acquired 431 Kina."
    /// (2026-10-21 17:51: "Spiritstone acquired.", 1,192 Kina and 3 Soul Crystals were lost to such a read).
    /// </summary>
    public static List<string> AfterOverlap(IReadOnlyList<string> last, IReadOnlyList<string> cur)
    {
        var take = new List<string>();
        if (last.Count == 0 || cur.Count == 0) return take;
        var lastKeys = last.Select(Key).ToList();
        int atCur = -1, atLast = -1;
        for (int i = cur.Count - 1; i >= 0 && atCur < 0; i--)
        {
            var k = Key(cur[i]);
            if (k.Length < 8) continue;                       // "(9/15)" and the like are no anchor
            for (int j = lastKeys.Count - 1; j >= 0; j--) if (Same(k, lastKeys[j])) { atCur = i; atLast = j; break; }
        }
        if (atCur < 0) return take;                           // nothing shared: a burst, handled by Burst
        var still = lastKeys.Skip(atLast + 1).ToList();       // the last read's lines below its match may still be on screen
        foreach (var l in cur.Skip(atCur + 1))
        {
            var k = Key(l);
            // the last read's own line still on screen, however garbled either time: the same digits and close letters
            // (2026-10-24 13:12:08: "cquihed 408 Kina" then "Acquired 408 Kina." was counted a second time)
            int m = still.FindIndex(p => Same(k, p));
            if (m < 0) m = still.FindIndex(p => Digits(p) == Digits(k) && Near(k, p));
            if (m >= 0) { still.RemoveAt(m); continue; }
            if (!LooksLikeLoot(l) || l.TrimStart().StartsWith('[')) continue;
            take.Add(l);
        }
        return take;
    }

    /// <summary>
    /// The chat settled after reads that couldn't be lined up (Steven, 2026-10-23, Hidden Lugi: the event's rewards came in
    /// all at once while the chat faded in, three reads garbled three ways, then a clean one; none of it was counted).
    /// <paramref name="prev"/> is the read this one lined up with, so it is as clean as this one. From its bottom up, its
    /// loot lines are new up to the first line that was on screen before the garbled reads began (<paramref name="before"/>,
    /// keys of the lines seen in the ten minutes before): the chat is in order, so anything above that is older. Lines
    /// already counted while it was garbled (<paramref name="counted"/>) are left out, and a line this read shows whole
    /// ("Acquired Refining Stone x6." where the last read had cut it at "Refining Stone") is taken in its whole form.
    /// </summary>
    public static List<string> Settled(IReadOnlyList<string> prev, IReadOnlyList<string> cur, ICollection<string> before, IReadOnlyList<string> counted) => Settled(prev, cur, before, counted, out _);

    /// <summary>The same, saying whether the new lines hold an achievement ("One-of-a-Kind Gear III achieved."): then they are its reward.</summary>
    public static List<string> Settled(IReadOnlyList<string> prev, IReadOnlyList<string> cur, ICollection<string> before, IReadOnlyList<string> counted, out bool achievement)
    {
        var used = new HashSet<int>();
        achievement = false;
        var cand = new List<(int I, string Line, string K)>();
        for (int i = prev.Count - 1; i >= 0; i--)
        {
            var line = prev[i]; var k = Key(line);
            if (k.Length == 0) continue;
            if (before.Any(p => Same(k, p))) break;
            // an achievement line that stays on screen reads a little differently each time: new only if nothing like it was seen
            if (IsAchievement(line) && !before.Any(p => Near(k, p))) achievement = true;
            if (!LooksLikeLoot(line) || line.TrimStart().StartsWith('[')) continue;
            // garbled reads line up with each other too ("Acquire d Refining stone" three reads running): only a line whose
            // verb was read whole is a clean read of it. A verb glued to the name ("Riverside Pauldtonsacquired.", the second
            // of two Pauldrons on 2026-10-24 13:07, read so in every read) is whole once the line is put back in shape
            if (!CleanVerb(ChatParser.CleanSystemLine(line))) continue;
            cand.Add((i, line, k));
        }
        // lines already counted while it was garbled are left out, each counted line standing for ONE line on screen: two
        // identical lines are two pickups, and a counted line takes the line most like it (before 2026-10-24 a counted line
        // left out every line near it, so one counted Pauldrons line would have hidden a second)
        var taken = Consume(cand.Select(c => c.K).ToList(), counted.Select(Key).ToList());
        var take = new List<string>();
        foreach (var (i, line, k) in cand.Where((c, x) => !taken[x]).OrderBy(c => c.I))
        {
            // the same line, a little more of it (its count): most of it already read, and each fuller line used once
            int f = -1;
            for (int j = 0; j < cur.Count && f < 0; j++)
            {
                var ck = Key(cur[j]);
                if (!used.Contains(j) && ck.Length > k.Length && k.Length >= 10 && k.Length * 10 >= ck.Length * 7 && ck.StartsWith(k, StringComparison.Ordinal) && CleanVerb(ChatParser.CleanSystemLine(cur[j]))) f = j;
            }
            if (f >= 0) used.Add(f);
            take.Add(f >= 0 ? cur[f] : line);
        }
        return take;
    }

    /// <summary>
    /// Which of <paramref name="lines"/> (keys) a line in <paramref name="counted"/> already stands for: each counted line
    /// for one line at most, the closest pairs first (the same text, then fewest letters apart), so a second identical
    /// line on screen is never covered by the first one's count.
    /// </summary>
    internal static bool[] Consume(IReadOnlyList<string> lines, IReadOnlyList<string> counted)
    {
        var taken = new bool[lines.Count];
        var usedC = new bool[counted.Count];
        var pairs = new List<(int D, int L, int C)>();
        for (int l = 0; l < lines.Count; l++)
            for (int c = 0; c < counted.Count; c++)
                if (lines[l] == counted[c] || Near(counted[c], lines[l])) pairs.Add((lines[l] == counted[c] ? -1 : Lev(lines[l], counted[c]), l, c));
        foreach (var (_, l, c) in pairs.OrderBy(p => p.D).ThenBy(p => p.L))
        {
            if (taken[l] || usedC[c]) continue;
            taken[l] = true; usedC[c] = true;
        }
        return taken;
    }

    /// <summary>When each line was first on screen, by key; kept ten minutes after it was last seen. Call once per read.</summary>
    public static void NoteFirstSeen(Dictionary<string, (DateTime First, DateTime Last)> seen, IReadOnlyList<string> lines, DateTime now)
    {
        foreach (var l in lines) { var k = Key(l); if (k.Length == 0) continue; seen[k] = seen.TryGetValue(k, out var v) && (now - v.Last).TotalMinutes <= 10 ? (v.First, now) : (now, now); }
        if (seen.Count > 800) foreach (var k in seen.Where(kv => (now - kv.Value.Last).TotalMinutes > 10).Select(kv => kv.Key).ToList()) seen.Remove(k);
    }

    /// <summary>The first time a line like this one (the same, or a misread or two apart) was on screen; null when never.</summary>
    public static DateTime? FirstSeen(string line, Dictionary<string, (DateTime First, DateTime Last)> seen)
    {
        var k = Key(line);
        if (k.Length == 0) return null;
        DateTime? best = null;
        foreach (var (key, v) in seen) if ((key == k || Near(k, key)) && (best == null || v.First < best)) best = v.First;
        return best;
    }

    /// <summary>An achievement line ("Fortress Guardian Notun III achieved."), never a player's message.</summary>
    // A quest done ("Protect the Odyle Collector completed.") is the same: what prints with it is the quest's reward
    // (Steven, 2026-10-23 16:22: its 200 Natura Soul Crystals landed in Soul Crystals from kills). Not a craft or a gather.
    public static bool IsAchievement(string line) => !line.TrimStart().StartsWith('[')
        && System.Text.RegularExpressions.Regex.IsMatch(line.Trim(), @"\b(?:achieved|completed)\W{0,3}$", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
        && !System.Text.RegularExpressions.Regex.IsMatch(line, @"craft|gather|extract|enchant|refin|design", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    /// <summary>"acquired" or "obtained" read as a whole word: "Acquire d", "AcquirGdylO" and "Hatquired" are garbled.</summary>
    static bool CleanVerb(string line) => System.Text.RegularExpressions.Regex.IsMatch(line, @"(?:^|[\s(])(?:acquired|obtained)(?:[\s.!,]|$)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    /// <summary>One line read twice: the same digits, and the letters a misread or two apart at most.</summary>
    internal static bool Same(string a, string b)
    {
        if (a == b) return true;
        if (!new string(a.Where(char.IsDigit).ToArray()).Equals(new string(b.Where(char.IsDigit).ToArray()))) return false;
        if (a.Length < 8 || b.Length < 8) return false;
        int max = Math.Max(1, Math.Min(a.Length, b.Length) / 8);
        return Math.Abs(a.Length - b.Length) <= max && Lev(a, b) <= max;
    }

    static string Key(string s)
    {
        var sb = new StringBuilder();
        foreach (var c in s.ToLowerInvariant()) if (char.IsLetterOrDigit(c)) sb.Append(c);
        return sb.ToString();
    }

    static string Digits(string k) => new(k.Where(char.IsDigit).ToArray());
}

/// <summary>
/// A pickup counted from a chat line whose count was lost or unreadable ("Acquired Odyle" for "Acquired Odyle x7.",
/// 2026-10-24 13:07; the loot list showed no row for it), counted as one for now. The line stays on screen for seconds
/// and the next reads often show its count whole, so its place (counted from the bottom, moved up by the lines that
/// arrive under it) is looked at in the next reads: when the line there, or one either side, is the same item with a
/// count, that count is the pickup's and the session adds the difference. When no read shows one within
/// <see cref="Reads"/> reads or <see cref="Seconds"/>, the session logs "quantity unsure, 1 assumed" and tallies it.
/// </summary>
public sealed class CountWatch
{
    public const int Reads = 3;
    public const double Seconds = 5;

    /// <summary>One watched pickup: the line as read, its item (ChatParser.Meaning's key without the count), the event counted, its place.</summary>
    public sealed record Pending(string Raw, string Item, object Event, int FromBottom, DateTime At, int Looks);
    readonly List<Pending> _pending = new();

    public int Waiting => _pending.Count;

    /// <summary>A pickup counted as one from a line with no readable count, at its place from the bottom (0 = the last line).</summary>
    public void Watch(string raw, string item, object ev, int fromBottom, DateTime at)
    {
        if (_pending.Count < 16) _pending.Add(new Pending(raw, item, ev, fromBottom, at, 0));
    }

    /// <summary>The item part of a ChatParser.Meaning key ("i:odyle" of "i:odylex7"), or "" for no item.</summary>
    public static string ItemOf(string key)
    {
        if (!key.StartsWith("i:")) return "";
        int x = key.LastIndexOf('x');
        return x > 2 && int.TryParse(key[(x + 1)..], out _) ? key[..x] : key;
    }

    /// <summary>The count of a ChatParser.Meaning key ("i:odylex7" is 7), or 0.</summary>
    public static int CountOf(string key)
    {
        int x = key.LastIndexOf('x');
        return key.StartsWith("i:") && x > 2 && int.TryParse(key[(x + 1)..], out var n) ? n : 0;
    }

    /// <summary>
    /// One read: <paramref name="freshCount"/> new lines arrived under the old ones. Returns the watched pickups whose count
    /// this read shows (with that count and the line it was read from), and those whose time to look is over (Count 0).
    /// <paramref name="meaning"/> is ChatParser.Meaning; <paramref name="hasCount"/> says whether a line names its count.
    /// Call once per read, before this read's own unsure lines are watched.
    /// </summary>
    public List<(Pending P, int Count, string Line)> Look(IReadOnlyList<string> lines, int freshCount, DateTime now, Func<string, (string Key, bool Exact)> meaning, Func<string, bool> hasCount)
    {
        var done = new List<(Pending, int, string)>();
        if (_pending.Count == 0) return done;
        var keep = new List<Pending>();
        var claimed = new HashSet<int>();
        foreach (var p in _pending)
        {
            int pos = p.FromBottom + freshCount;
            string? found = null; int at = -1;
            if (lines.Count > 0)
                // its own place first, then a line either side (a read that didn't line up moves it by one)
                foreach (int d in new[] { 0, 1, -1 })
                {
                    int i = lines.Count - 1 - (pos + d);
                    if (i < 0 || i >= lines.Count || claimed.Contains(i)) continue;
                    var m = meaning(lines[i]);
                    if (m.Key.Length == 0 || ItemOf(m.Key) != p.Item || !hasCount(lines[i])) continue;
                    found = lines[i]; at = i; break;
                }
            if (found != null)
            {
                claimed.Add(at);
                done.Add((p, Math.Max(1, CountOf(meaning(found).Key)), found));
                continue;
            }
            var next = p with { FromBottom = lines.Count > 0 ? pos : p.FromBottom, Looks = p.Looks + (lines.Count > 0 ? 1 : 0) };
            if (next.Looks >= Reads || (now - p.At).TotalSeconds > Seconds) done.Add((p, 0, ""));
            else keep.Add(next);
        }
        _pending.Clear();
        _pending.AddRange(keep);
        return done;
    }

    /// <summary>Everything still watched, its time over (a stop, the end of a replay).</summary>
    public List<Pending> Flush() { var all = _pending.ToList(); _pending.Clear(); return all; }
}
