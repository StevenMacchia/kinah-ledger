using KinahLedger.Data;

namespace KinahLedger.Session;

/// <summary>One slot of the Today strip: an item, how many came today, what the sellable copies are worth after the Market's cut, and where they came from.</summary>
public sealed record TodaySlot(long Id, string Name, long Count, long Bound, long Worth, bool Priced, bool Gear, bool Currency, List<(string Where, long Count)> Where);

/// <summary>A farming zone today: time farming, kills, and what it made (Kinah plus loot after the Market's cut).</summary>
public sealed record TodayZone(string Zone, double Active, int Kills, long Made);

/// <summary>Today's farming, every zone or one: the slots the strip draws and the figures its line says.</summary>
public sealed record TodayFarm(long Kinah, int Kills, int Attempts, double Active, List<TodaySlot> Drops, List<TodaySlot> Gathered, List<TodayZone> Zones, List<(string Where, long Count)> KinahWhere)
{
    public long DropWorth => Drops.Sum(d => d.Worth);
    public long GatherWorth => Gathered.Sum(d => d.Worth);
    public long Made => Kinah + DropWorth + GatherWorth;
    public bool Any => Kinah > 0 || Drops.Count + Gathered.Count > 0 || Kills > 0 || Attempts > 0;
}

/// <summary>Today's expeditions and events: every run since midnight, and the loot of the counted ones.</summary>
public sealed record TodayRuns(int Runs, int Counted, int Places, long Kinah, long Value, List<TodaySlot> Items, List<(string Where, long Count)> KinahWhere);

/// <summary>
/// The Today strip's figures (0.8.50), apart from the window so a self-test can check them. Farming is what the Farming
/// tables count (drops from kills, gathering apart, the same split as the tables); runs are the reward pictures of the
/// runs since midnight, counted as the Events page counts them (flagged runs left out of the loot and the value).
/// </summary>
public static class TodayLoot
{
    /// <summary>A run belongs to today when its last loot (or its start, with none yet) came after midnight on this PC.</summary>
    public static bool IsToday(InstanceRun r, DateTime midnightLocal) => (r.LastLoot ?? r.Start).ToLocalTime() >= midnightLocal;

    /// <summary>Sellable copies at the lowest Market price, after the Market's cut; null when the item has no price.</summary>
    static long? Worth(string name, long sellable) => PriceBook.Price(name) is long p ? (long)Math.Round(p * sellable * PriceBook.SellerKeeps) : null;

    static string ZoneName(string z) => z == SessionEngine.UnknownZone ? "Zone not read" : z;

    /// <param name="zone">One zone's farming (a pinned chip), or null for every zone.</param>
    public static TodayFarm Farm(SessionEngine day, ItemDictionary items, string? zone = null)
    {
        var zones = day.ZoneSnapshot();
        var t = day.Totals();
        Dictionary<long, int> its, bound; long kinah; int kills, attempts; double active;
        if (zone != null)
        {
            var z = zones.GetValueOrDefault(zone) ?? new SessionEngine.ZoneStats();
            its = z.Items; bound = z.ItemsBound; kinah = z.Kinah; kills = z.Kills; attempts = z.Nodes; active = z.Active;
        }
        else
        {
            its = new Dictionary<long, int>(day.Items); bound = new Dictionary<long, int>(day.ItemsBound);
            kinah = t.Kinah; kills = t.Kills; attempts = t.Nodes; active = day.Active.TotalSeconds;
        }
        var drops = new List<TodaySlot>(); var gathered = new List<TodaySlot>();
        foreach (var id in its.Keys.Union(bound.Keys))
        {
            long sell = its.GetValueOrDefault(id), b = bound.GetValueOrDefault(id);
            if (sell + b <= 0) continue;
            var name = items.NameOf(id);
            var w = sell > 0 ? Worth(name, sell) : null;
            var where = zone != null ? new List<(string, long)> { (ZoneName(zone), sell + b) }
                : zones.Where(kv => kv.Value.Items.GetValueOrDefault(id) + kv.Value.ItemsBound.GetValueOrDefault(id) > 0)
                    .Select(kv => (ZoneName(kv.Key), (long)(kv.Value.Items.GetValueOrDefault(id) + kv.Value.ItemsBound.GetValueOrDefault(id))))
                    .OrderByDescending(x => x.Item2).ToList();
            var slot = new TodaySlot(id, name, sell + b, b, w ?? 0, w != null, items.IsGear(id), false, where);
            (GatheringCatalog.Find(name) != null ? gathered : drops).Add(slot);
        }
        static List<TodaySlot> Order(List<TodaySlot> l) => l.OrderByDescending(s => s.Worth).ThenByDescending(s => s.Count).ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList();
        // the zones of the day with something in them, most made first; time on a loading screen or in town alone isn't a zone
        var list = zones.Where(kv => kv.Value.Kinah > 0 || kv.Value.Kills > 0 || kv.Value.Nodes > 0 || kv.Value.Items.Count + kv.Value.ItemsBound.Count > 0)
            .Select(kv =>
            {
                long made = kv.Value.Kinah + kv.Value.Items.Sum(x => Worth(items.NameOf(x.Key), x.Value) ?? 0);
                return new TodayZone(kv.Key, kv.Value.Active, kv.Value.Kills, made);
            })
            .OrderByDescending(z => z.Made).ThenByDescending(z => z.Active).ToList();
        var kinahWhere = (zone != null ? zones.Where(kv => kv.Key == zone) : zones).Where(kv => kv.Value.Kinah > 0)
            .Select(kv => (ZoneName(kv.Key), kv.Value.Kinah)).OrderByDescending(x => x.Kinah).ToList();
        return new TodayFarm(kinah, kills, attempts, active, Order(drops), Order(gathered), list, kinahWhere);
    }

    /// <param name="today">The runs since midnight (<see cref="IsToday"/>).</param>
    /// <param name="isCurrency">Currencies (Abyss Points, tokens) are amounts, not pieces: their own slots at the end.</param>
    public static TodayRuns Runs(IReadOnlyList<InstanceRun> today, ItemDictionary items, Func<long, bool> isCurrency)
    {
        var counted = today.Where(r => !r.OutOfAverages).ToList();
        var count = new Dictionary<long, long>(); var bound = new Dictionary<long, long>(); var where = new Dictionary<long, Dictionary<string, long>>();
        var kinahWhere = new Dictionary<string, long>();
        long kinah = 0;
        foreach (var r in counted)
        {
            kinah += r.Kinah;
            if (r.Kinah > 0) kinahWhere[r.Label] = kinahWhere.GetValueOrDefault(r.Label) + r.Kinah;
            void Add(long id, int c, bool b)
            {
                if (c <= 0) return;
                count[id] = count.GetValueOrDefault(id) + c;
                if (b) bound[id] = bound.GetValueOrDefault(id) + c;
                if (!where.TryGetValue(id, out var w)) where[id] = w = new Dictionary<string, long>();
                w[r.Label] = w.GetValueOrDefault(r.Label) + c;
            }
            foreach (var (id, c) in r.Items) Add(id, c, false);
            foreach (var (id, c) in r.BoundItems) Add(id, c, true);
        }
        var slots = new List<TodaySlot>();
        foreach (var (id, c) in count)
        {
            var name = items.NameOf(id);
            bool gear = items.IsGear(id), curr = isCurrency(id);
            long b = bound.GetValueOrDefault(id);
            // gear from a run is bound to the character, and currencies aren't sold: neither is in the value (as RunValue)
            long? w = gear || curr ? null : Worth(name, c - b);
            slots.Add(new TodaySlot(id, name, c, gear ? c : b, w ?? 0, w != null, gear, curr, where[id].Select(kv => (kv.Key, kv.Value)).OrderByDescending(x => x.Value).ToList()));
        }
        // as the Events page's loot row: the most valuable materials, then the gear best grade first, then the currencies
        static int Rank(TodaySlot s) => s.Currency ? 2 : s.Gear ? 1 : 0;
        slots = slots.OrderBy(Rank).ThenByDescending(s => s.Worth).ThenByDescending(s => s.Gear ? GradeRank(items.Get(s.Id)?.Grade ?? "") : 0).ThenByDescending(s => s.Count).ToList();
        long value = kinah + slots.Sum(s => s.Worth);
        int places = today.Select(r => r.Zone).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        return new TodayRuns(today.Count, counted.Count, places, kinah, value, slots, kinahWhere.Select(kv => (kv.Key, kv.Value)).OrderByDescending(x => x.Value).ToList());
    }

    static int GradeRank(string g) => g switch { "Epic" => 4, "Unique" => 3, "Legend" => 2, "Rare" => 1, _ => 0 };
}
