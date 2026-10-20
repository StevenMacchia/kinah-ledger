using KinahLedger.Data;
using KinahLedger.Protocol;

namespace KinahLedger.Session;

/// <summary>Running totals for one farming session and the per-hour rates derived from them.</summary>
public sealed class SessionEngine
{
    readonly ItemDictionary _items;
    readonly object _lock = new();

    public DateTime Started { get; } = DateTime.UtcNow;
    public DateTime? Ended { get; private set; }
    public long Kinah { get; private set; }
    public long KinahBound { get; private set; }
    public int KillsDefeated { get; private set; }
    public int KillsXp { get; private set; }
    public int KinahDrops { get; private set; }
    /// <summary>Best available kill count: kill lines, experience lines, or Kinah pickups (one per mob in open-world farming).</summary>
    public int Kills => Math.Max(KillsDefeated, Math.Max(KillsXp, KinahDrops));
    public int Events { get; private set; }
    public long? ZoneId { get; private set; }
    public long? InstanceId { get; private set; }
    public Dictionary<long, int> Items { get; } = new();
    public Dictionary<long, int> ItemsBound { get; } = new();

    public SessionEngine(ItemDictionary items) { _items = items; }

    public TimeSpan Elapsed => (Ended ?? DateTime.UtcNow) - Started;
    /// <summary>Never below one second, so rates are defined from the first event.</summary>
    public double Hours => Math.Max(Elapsed.TotalHours, 1.0 / 3600);

    public void Apply(GameEvent ev)
    {
        lock (_lock)
        {
            Events++;
            switch (ev)
            {
                case ItemAcquired a:
                    var d = a.Bound ? ItemsBound : Items;
                    d[a.ItemId] = d.GetValueOrDefault(a.ItemId) + a.Count;
                    break;
                case KinahChanged k:
                    if (k.Bound) KinahBound += k.Delta; else Kinah += k.Delta;
                    if (k.Delta > 0) KinahDrops++;
                    break;
                case MobKilled:
                    KillsDefeated++;
                    break;
                case XpGained:
                    KillsXp++;
                    break;
                case ZoneChanged z:
                    ZoneId = z.ZoneId;
                    InstanceId = z.InstanceId;
                    break;
            }
        }
    }

    public void Finish() => Ended ??= DateTime.UtcNow;

    /// <summary>A consistent snapshot of the totals, taken under the same lock the reader thread uses.</summary>
    public (long Kinah, long KinahBound, long Drops, int Kills, int Events) Totals()
    {
        lock (_lock) return (Kinah, KinahBound, Items.Values.Sum() + ItemsBound.Values.Sum(), Kills, Events);
    }

    public string ItemName(long id) => _items.NameOf(id);

    public List<(long Id, string Name, int Count, int Bound)> ItemRows()
    {
        lock (_lock)
        {
            return Items.Keys.Union(ItemsBound.Keys)
                .Select(id => (id, ItemName(id), Items.GetValueOrDefault(id), ItemsBound.GetValueOrDefault(id)))
                .OrderByDescending(r => r.Item3 + r.Item4)
                .ToList();
        }
    }
}
