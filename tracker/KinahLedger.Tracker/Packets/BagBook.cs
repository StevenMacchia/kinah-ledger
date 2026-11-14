namespace KinahLedger.Packets;

/// <summary>
/// The character's whole inventory from the game's messages (Steven, 2026-11-13: "bag value"): the game sends every stack
/// it holds once at the start of each connection to its server (login, a teleport to another server, the Abyss), never in
/// between, and every change after that. So the inventory is known from the first such moment the reader sees, and stays
/// exact with each change; until then, or after the reading began in the middle of a connection, it is unknown.
///
/// Stacks are kept by their id (the game gives them out again at each login, so totals are by item, never by stack across
/// connections). Kinah, bound Kinah and Abyss Points are kept apart: the decoder says which rows they are.
/// Thread-safe: the reading thread writes, the window reads copies.
/// </summary>
public sealed class BagBook
{
    readonly object _gate = new();
    readonly Dictionary<long, (long ItemId, long Amount, int Container)> _stacks = new();
    readonly Dictionary<CurrencyKind, long> _money = new();
    List<BagRecord>? _filling;
    int _said;

    /// <summary>When the inventory became known on this connection (its snapshot), null while it isn't.</summary>
    public DateTime? KnownAt { get; private set; }
    /// <summary>The first snapshot since the tracker started, and the items then: "since the session started".</summary>
    public DateTime? FirstAt { get; private set; }
    Dictionary<long, long> _first = new();
    /// <summary>Snapshots taken in so far.</summary>
    public int Snapshots { get; private set; }
    /// <summary>The rows of the last snapshot: the game's count and the rows read.</summary>
    public (int Said, int Read) LastRows { get; private set; }

    /// <summary>A new connection: the inventory is unknown until its snapshot comes.</summary>
    public void Unknown() { lock (_gate) { KnownAt = null; _stacks.Clear(); _money.Clear(); _filling = null; } }

    /// <summary>One record of a snapshot: its start, a stack, its end (which makes it the inventory).</summary>
    public void Take(BagRecord r)
    {
        lock (_gate)
        {
            switch (r.Kind)
            {
                case BagRecord.Begin: _filling = new List<BagRecord>(); _said = r.Count; break;
                case BagRecord.Stack: _filling?.Add(r); break;
                case BagRecord.End when _filling != null:
                    _stacks.Clear(); _money.Clear();
                    foreach (var s in _filling)
                    {
                        if (s.Currency is CurrencyKind k) _money[k] = _money.GetValueOrDefault(k) + s.Amount;
                        else _stacks[s.StackId] = (s.ItemId, s.Amount, s.Container);
                    }
                    LastRows = (_said, _filling.Count);
                    _filling = null;
                    KnownAt = r.At; Snapshots++;
                    if (FirstAt == null) { FirstAt = r.At; _first = TotalsLocked(); }
                    break;
            }
        }
    }

    /// <summary>A stack's change (the inventory's update): kept when the inventory is known.</summary>
    public void Take(ItemChange c)
    {
        lock (_gate)
        {
            if (KnownAt == null) return;
            if (c.Op == ItemOp.Gone) _stacks.Remove(c.StackId);
            else
            {
                int box = _stacks.TryGetValue(c.StackId, out var was) ? was.Container : 0;
                _stacks[c.StackId] = (c.ItemId, c.Total, box);
            }
        }
    }

    /// <summary>A currency's amount now.</summary>
    public void Take(CurrencyChange c) { lock (_gate) if (KnownAt != null && c.What != CurrencyKind.RankPoints) _money[c.What] = c.Amount; }

    /// <summary>The items held now, by item id (stacks added up), leaving out the gear worn (<paramref name="worn"/> false); null when unknown.</summary>
    public Dictionary<long, long>? Totals(bool worn = false) { lock (_gate) return KnownAt == null ? null : TotalsLocked(worn); }

    Dictionary<long, long> TotalsLocked(bool worn = false)
    {
        var t = new Dictionary<long, long>();
        foreach (var (id, n, box) in _stacks.Values)
            if (n > 0 && (worn || box != Worn)) t[id] = t.GetValueOrDefault(id) + n;
        return t;
    }

    /// <summary>The items held at the first snapshot since the tracker started (gear worn left out); empty before one.</summary>
    public Dictionary<long, long> FirstTotals { get { lock (_gate) return new Dictionary<long, long>(_first); } }

    /// <summary>A currency's amount now, null when unknown.</summary>
    public long? Money(CurrencyKind k) { lock (_gate) return KnownAt != null && _money.TryGetValue(k, out var v) ? v : null; }

    /// <summary>The container the game keeps the gear worn in.</summary>
    public const int Worn = 11;
}
