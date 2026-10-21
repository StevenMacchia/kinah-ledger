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
/// Rows only count once seen in two reads in a row (a one-off misread or a name plate never does), items only when
/// the item list knows them, and Kinah only from 10 up, so a misread can't add a kill.
/// </summary>
public sealed class LootCrossCheck
{
    public const double MatchSeconds = 12, WaitSeconds = 4;
    public const long MinKinah = 10;

    readonly ItemDictionary _items;
    readonly LootPopupTracker _tracker = new();
    List<LootPopup.Row> _lastRaw = new();

    // what the chat read lately (kept MatchSeconds) and the list's rows still waiting for a chat match
    readonly List<(DateTime At, string Key, bool Used)> _chat = new();
    readonly List<(DateTime At, LootPopup.Row Row, string Key)> _waiting = new();

    public int Added { get; private set; }
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
        int late = _added.FindIndex(x => x.Key == key && Math.Abs((now - x.At).TotalSeconds) <= MatchSeconds);
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
        // a row counts only once it is in two reads in a row
        var steady = raw.Where(r => _lastRaw.Any(p => p.Key == r.Key)).ToList();
        _lastRaw = raw;
        foreach (var row in _tracker.NewRows(steady))
        {
            string? key = null;
            if (row.IsKinah) { if (row.Amount >= MinKinah) key = KinahKey(row.Amount); }
            else if (_items.Knows(row.Name)) key = ItemKey(_items.IdOf(row.Name), row.Count);
            if (key != null) _waiting.Add((now, row, key));
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
