using KinahLedger.Data;
using KinahLedger.Ocr;

namespace KinahLedger.Session;

/// <summary>
/// The chat and the loot list beside the character, checked against each other. The chat is the source: every pickup it
/// reads counts as before. A row in the loot list is matched to a chat pickup of the same Kinah amount, or the same item
/// and count, read within <see cref="MatchSeconds"/> of it, and each chat pickup matches one row only, so two identical
/// drops need two chat lines. A row the chat still hasn't matched <see cref="WaitSeconds"/> after it appeared is a pickup
/// the chat read missed (fast kills, a line lost while the chat scrolled): it is added then, once, and logged. Rewards,
/// purchases and crafted items the chat read count as matches too, so nothing the chat set aside comes back as a drop.
///
/// A row is known by what it is (the item list's item and count, or the Kinah amount), not by its place in the list, and
/// a pickup is new when more rows of it show than were already credited while it stayed up: two identical drops are two
/// rows. An item counts from one read when the item list knows it (the list is read about once a second and a row is up
/// about three, so during fast kills a row can be read only once; 2026-10-21 17:51 a Spiritstone was lost that way).
/// Kinah counts only from 10 up and only once two reads agree, so a misread amount can't add a kill.
/// </summary>
public sealed class LootCrossCheck
{
    public const double MatchSeconds = 12, WaitSeconds = 4;
    public const long MinKinah = 10;

    readonly ItemDictionary _items;
    /// <summary>How long a row stays credited after it was last read; a row is up about three seconds.</summary>
    public const double HoldSeconds = 3.5;
    // per pickup key: how many rows of it were credited, when one was last read, and reads in a row with fewer showing
    readonly Dictionary<string, (int Count, DateTime Seen, int Fewer)> _credited = new();
    // Kinah keys read once and not yet confirmed by a second read
    readonly Dictionary<string, (int Count, DateTime At)> _kinahOnce = new();

    // what the chat read lately (kept MatchSeconds) and the list's rows still waiting for a chat match
    readonly List<(DateTime At, string Key, bool Used)> _chat = new();
    readonly List<(DateTime At, LootPopup.Row Row, string Key)> _waiting = new();

    public int Added { get; private set; }
    // Kinah rows the list showed in one read only, never confirmed by a second read or the chat
    readonly List<(long Amount, DateTime At)> _singleKinah = new();
    /// <summary>How long a one-read Kinah row waits for the balance counter to confirm it.</summary>
    public const double SingleSeconds = 30;

    /// <summary>
    /// True (and used up) when the list showed this Kinah amount once in the last <see cref="SingleSeconds"/> and nothing
    /// else counted it: with the balance counter's gap of exactly that amount, two sources agree on a pickup.
    /// </summary>
    public bool TakeSingleKinah(long amount, DateTime now)
    {
        int i = _singleKinah.FindIndex(x => x.Amount == amount && (now - x.At).TotalSeconds <= SingleSeconds);
        if (i < 0) return false;
        _singleKinah.RemoveAt(i);
        return true;
    }
    /// <summary>Kinah lines the chat read twice that the list showed once, skipped.</summary>
    public int Duplicates { get; private set; }
    /// <summary>How close in time a second chat read of the same amount is taken for the same line.</summary>
    public const double DupSeconds = 3;
    // the most rows of each Kinah amount the list showed at once, lately
    readonly Dictionary<string, (int Count, DateTime At)> _listShown = new();

    /// <summary>The same pickup key, allowing one misread digit in a Kinah amount of three digits or more.</summary>
    static bool Same(string a, string b)
    {
        if (a == b) return true;
        if (!a.StartsWith("k:") || !b.StartsWith("k:") || a.Length != b.Length || a.Length < 5) return false;
        int diff = 0; for (int i = 2; i < a.Length; i++) if (a[i] != b[i]) diff++;
        return diff == 1;
    }
    public int Matched { get; private set; }

    public LootCrossCheck(ItemDictionary items) { _items = items; }

    static string KinahKey(long amount) => "k:" + amount;
    static string ItemKey(long id, int count) => "i:" + id + "x" + count;

    // rows added from the list, kept MatchSeconds: a chat line for the same pickup read late is then the same pickup
    readonly List<(DateTime At, string Key)> _added = new();

    /// <summary>
    /// Every pickup the chat read (before any reward or craft rule), so the list's rows can be matched to it. True when
    /// the list already added this pickup (the chat line came late): the caller then skips it, so it counts once.
    /// </summary>
    public bool Chat(GameEvent ev, DateTime now)
    {
        string? key = ev switch
        {
            KinahChanged k when k.Delta > 0 => KinahKey(k.Delta),
            ItemAcquired a => ItemKey(a.ItemId, a.Count),
            RewardReceived r when r.Name.Length >= 2 => ItemKey(_items.IdOf(r.Name), r.Count),
            _ => null,
        };
        if (key == null) return false;
        // the same Kinah amount read again within a few seconds while the list showed it only once: the chat counted one
        // line twice (2026-10-22 11:01: "Acquired 352 Kina." from two reads). When the list showed it as often, it stays.
        if (ev is KinahChanged && _listShown.TryGetValue(key, out var ls) && (now - ls.At).TotalSeconds <= MatchSeconds)
        {
            int chatSame = _chat.Count(x => x.Key == key && (now - x.At).TotalSeconds <= DupSeconds);
            if (chatSame >= ls.Count) { CrashLog.Note($"the chat read {key[2..]} Kina again within {DupSeconds:0} s and the loot list showed it {ls.Count}×: counted once"); Duplicates++; return true; }
        }
        int late = _added.FindIndex(x => Same(x.Key, key) && Math.Abs((now - x.At).TotalSeconds) <= MatchSeconds);
        if (late >= 0 && ev is not RewardReceived)
        {
            _added.RemoveAt(late);
            CrashLog.Note("a chat line for a pickup the loot list already added came late: counted once");
            return true;
        }
        _chat.Add((now, key, false));
        Settle(now);
        return false;
    }

    /// <summary>One read of the loot list. Its new rows wait for a chat match; returns the rows that waited long enough unmatched, as pickups to add.</summary>
    public List<GameEvent> Popup(List<LootPopup.Row> raw, DateTime now)
    {
        // what this read shows, by pickup: unknown names and Kinah under 10 never count
        var shown = new Dictionary<string, (int Count, LootPopup.Row Row)>();
        foreach (var row in raw)
        {
            string? key = row.IsKinah ? (row.Amount >= MinKinah ? KinahKey(row.Amount) : null)
                        : _items.Knows(row.Name) ? ItemKey(_items.IdOf(row.Name), row.Count) : null;
            if (key == null) continue;
            shown[key] = (shown.TryGetValue(key, out var x) ? x.Count + 1 : 1, row);
        }
        foreach (var (key, (n, row)) in shown)
        {
            if (row.IsKinah) _listShown[key] = (Math.Max(n, _listShown.TryGetValue(key, out var lsn) && (now - lsn.At).TotalSeconds <= MatchSeconds ? lsn.Count : 0), now);
            var had = _credited.TryGetValue(key, out var c) && (now - c.Seen).TotalSeconds <= HoldSeconds ? c : (0, now, 0);
            int more = n - had.Item1;
            if (more > 0 && row.IsKinah)
            {
                // Kinah: only once a second read shows the same amount as often
                if (!(_kinahOnce.TryGetValue(key, out var once) && (now - once.At).TotalSeconds <= HoldSeconds && once.Count >= n)) { _kinahOnce[key] = (n, now); more = 0; }
                else _kinahOnce.Remove(key);
            }
            if (more > 0)
            {
                for (int i = 0; i < more; i++) _waiting.Add((now, row, key));
                CrashLog.Note($"loot list: {(row.IsKinah ? row.Amount.ToString("N0") + " Kina" : row.Name + " x" + row.Count)}" + (more > 1 ? $" ({more} rows)" : ""));
                _credited[key] = (n, now, 0);
            }
            else if (n < had.Item1)
                // fewer rows of it now: an older one expired. Two reads in a row like that, and the lower count holds,
                // so a new identical drop after it is still seen as new; a single short read (a row the reader missed) changes nothing
                _credited[key] = had.Item3 >= 1 ? (n, now, 0) : (had.Item1, now, had.Item3 + 1);
            else _credited[key] = (had.Item1, now, 0);
        }
        // gone from two reads in a row, over a second and a half: its rows expired, and the next one like it is a new pickup
        foreach (var k in _credited.Keys.Where(k => !shown.ContainsKey(k)).ToList())
        {
            var c = _credited[k];
            if (c.Fewer >= 1 && (now - c.Seen).TotalSeconds >= 1.5) _credited.Remove(k);
            else _credited[k] = (c.Count, c.Seen, c.Fewer + 1);
        }
        foreach (var k in _credited.Where(kv => (now - kv.Value.Seen).TotalSeconds > HoldSeconds).Select(kv => kv.Key).ToList()) _credited.Remove(k);
        foreach (var k in _kinahOnce.Where(kv => (now - kv.Value.At).TotalSeconds > HoldSeconds).Select(kv => kv.Key).ToList())
        {
            // read once and never again: not enough alone, but kept a while for the balance counter to confirm
            if (long.TryParse(k[2..], out var amt)) { _singleKinah.Add((amt, _kinahOnce[k].At)); if (_singleKinah.Count > 20) _singleKinah.RemoveAt(0); }
            _kinahOnce.Remove(k);
        }
        return Settle(now);
    }

    /// <summary>Matches waiting rows to chat pickups; returns the rows that waited past WaitSeconds unmatched, as events.</summary>
    List<GameEvent> Settle(DateTime now)
    {
        var add = new List<GameEvent>();
        for (int i = 0; i < _waiting.Count; i++)
        {
            var (at, row, key) = _waiting[i];
            int c = _chat.FindIndex(x => !x.Used && x.Key == key && Math.Abs((x.At - at).TotalSeconds) <= MatchSeconds);
            // a Kinah amount the chat misread by one digit (460 for the list's 463) is the same pickup
            if (c < 0 && row.IsKinah) c = _chat.FindIndex(x => !x.Used && Same(x.Key, key) && Math.Abs((x.At - at).TotalSeconds) <= MatchSeconds);
            if (c >= 0)
            {
                _chat[c] = (_chat[c].At, _chat[c].Key, true);
                Matched++;
                _waiting.RemoveAt(i--);
                continue;
            }
            if ((now - at).TotalSeconds < WaitSeconds) continue;
            add.Add(row.IsKinah ? new KinahChanged(at, row.Amount, false) : new ItemAcquired(at, _items.IdOf(row.Name), row.Count, false));
            CrashLog.Note($"the loot list showed {(row.IsKinah ? row.Amount.ToString("N0") + " Kina" : row.Name + " x" + row.Count)} and the chat read missed it: counted from the list");
            Added++;
            _added.Add((now, key));
            _waiting.RemoveAt(i--);
        }
        _chat.RemoveAll(x => (now - x.At).TotalSeconds > MatchSeconds * 2);
        _added.RemoveAll(x => (now - x.At).TotalSeconds > MatchSeconds);
        return add;
    }
}
