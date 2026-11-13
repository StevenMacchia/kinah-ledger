namespace KinahLedger.Packets;

/// <summary>
/// One update of the inventory from the game's traffic: the stacks it changed, together. A pickup is one item going up; a
/// craft's materials, a potion drunk or a stack moved go down (or move between stacks of one item) in the same update.
/// </summary>
public sealed record ItemBatch(DateTime At, IReadOnlyList<ItemChange> Rows)
{
    /// <summary>Something in this update went down (a craft's materials, spending, a use, a stack gone).</summary>
    public bool Fell => Rows.Any(r => r.BatchFell || (!r.First && r.Delta < 0));

    /// <summary>The distinct items this update changed.</summary>
    public int Items => Rows.Select(r => r.ItemId).Distinct().Count();

    /// <summary>Each item's change over the update (stacks of one item added together); Unknown when a stack's earlier amount isn't known.</summary>
    public IReadOnlyList<(long ItemId, long Net, bool Unknown)> Net() =>
        Rows.GroupBy(r => r.ItemId).Select(g => (g.Key, g.Where(r => !r.First).Sum(r => r.Delta), g.Any(r => r.First))).ToList();

    /// <summary>The changes in the order the decoder gave them, cut into updates by their batch.</summary>
    public static List<ItemBatch> Cut(IEnumerable<ItemChange> changes)
    {
        var o = new List<ItemBatch>();
        List<ItemChange>? cur = null;
        foreach (var c in changes)
        {
            if (cur == null || cur[0].Batch != c.Batch) { cur = new List<ItemChange>(); o.Add(new ItemBatch(c.At, cur)); }
            cur.Add(c);
        }
        return o;
    }
}
