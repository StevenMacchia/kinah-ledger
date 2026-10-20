using KinahLedger.Data;

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
    /// <summary>Points pickups (Soul Points and the like): one line per kill, never a drop.</summary>
    public int CurrencyLines { get; private set; }
    public Dictionary<string, long> Currencies { get; } = new();
    /// <summary>Quest, title and box rewards seen and deliberately left out of the farming numbers.</summary>
    public int RewardsSkipped { get; private set; }
    /// <summary>Gathering: node attempts, one per primary-resource line (ore, log, Odyle, herb, food) plus failed attempts.</summary>
    public int Nodes { get; private set; }
    public int NodesFailed { get; private set; }
    /// <summary>Gathering: lines with x2 or more, the Plentiful proc.</summary>
    public int Plentiful { get; private set; }
    /// <summary>Per gathering skill: nodes, failed attempts and Plentiful procs. Rates are computed inside a skill, never across skills.</summary>
    public sealed class SkillStats { public int Nodes, Failed, Plentiful, Items, Bonus; }
    public Dictionary<GatherFamily, SkillStats> Skills { get; } = new();
    SkillStats Skill(GatherFamily f) { if (!Skills.TryGetValue(f, out var s)) Skills[f] = s = new SkillStats(); return s; }
    /// <summary>Gathering: gemstones and higher-tier materials, the finds worth tracking per node.</summary>
    public int BonusFinds { get; private set; }
    /// <summary>Best available kill count: kill lines, experience lines, or Kinah pickups (one per mob in open-world farming).</summary>
    public int Kills => Math.Max(Math.Max(KillsDefeated, KillsXp), Math.Max(KinahDrops, CurrencyLines));
    public int Events { get; private set; }
    public Dictionary<long, int> Items { get; } = new();
    public Dictionary<long, int> ItemsBound { get; } = new();

    /// <summary>Pet Soul Crystals by name, kept out of the drop tables. Rated per hour and per zone, never per kill.</summary>
    public Dictionary<string, int> Pets { get; } = new();
    public int PetCrystals { get; private set; }

    // the skill of the last gather, so "Essence Extraction failed" right after an ore counts against mining: the game
    // prints that line for a failed gather of any kind, and the skill isn't in it
    GatherFamily? _lastSkill; DateTime _lastSkillAt;

    /// <summary>Successes and failures for one craft recipe or one enchant step.</summary>
    public sealed class Odds { public int Ok, Fail; public int Attempts => Ok + Fail; }
    /// <summary>Craft results by item key (dictionary ID, or the name read when the item isn't in the dictionary).</summary>
    public Dictionary<string, Odds> Crafts { get; } = new();
    /// <summary>Enchant results by "item|from|to"; a level the line didn't show is "?".</summary>
    public Dictionary<string, Odds> Enchants { get; } = new();
    /// <summary>A craft's own item line ("Orichalcum Ingot acquired.") follows the craft: it is the craft's output, not a drop.</summary>
    public int CraftedSkipped { get; private set; }
    long _craftedId; DateTime _craftedAt; string _lastCraft = ""; DateTime _lastCraftAt;

    /// <summary>The player's own Market activity seen in chat during this count: what sold, for how much, and what expired.</summary>
    public int Sold { get; private set; }
    public long SoldKinah { get; private set; }
    public int Listed { get; private set; }
    public int Expired { get; private set; }

    /// <summary>
    /// The Kinah balance from the currency counter at the top-left of the game, first and last good read. The difference is
    /// net income: pickups minus repairs, purchases and fees. Only the difference, rounded, is ever shared; never the balance.
    /// </summary>
    public long? BalanceStart { get; private set; }
    public long? BalanceEnd { get; private set; }
    public long? NetKinah { get { lock (_lock) return BalanceStart is long s && BalanceEnd is long e ? e - s : null; } }

    public void SetBalance(long? balance)
    {
        if (balance is not long b) return;
        lock (_lock) { BalanceStart ??= b; BalanceEnd = b; }
    }

    // when the last kills and gathers happened, so the window can show what the player is doing now
    readonly Queue<DateTime> _recentKills = new(), _recentGathers = new();
    static void Mark(Queue<DateTime> q, DateTime t) { q.Enqueue(t); while (q.Count > 0 && (t - q.Peek()).TotalMinutes > 3) q.Dequeue(); }

    /// <summary>
    /// "Gathering" or "Open world" from the last three minutes (more gathers or more kills), null when neither happened.
    /// A count that started at the nodes and moved on to mobs shows Kinah per hour again, whatever came first.
    /// </summary>
    public string? RecentKind
    {
        get
        {
            lock (_lock)
            {
                var now = DateTime.UtcNow;
                int kills = _recentKills.Count(t => (now - t).TotalMinutes <= 3), gathers = _recentGathers.Count(t => (now - t).TotalMinutes <= 3);
                return kills == 0 && gathers == 0 ? null : gathers > kills ? "Gathering" : "Open world";
            }
        }
    }

    /// <summary>What happened in one zone: time spent there plus the counters the "best spot" tables need.</summary>
    public sealed class ZoneStats
    {
        public double Seconds; public long Kinah; public int KinahDrops, CurrencyLines, Defeated, Xp, Pets, Drops, Nodes;
        public Dictionary<string, int> PetsByName = new();
        public int Kills => Math.Max(Math.Max(Defeated, Xp), Math.Max(KinahDrops, CurrencyLines));
        public double Hours => Math.Max(Seconds / 3600, 1.0 / 3600);
    }
    public const string UnknownZone = "Unknown zone";
    public Dictionary<string, ZoneStats> Zones { get; } = new();
    string? _zone; DateTime _zoneSince = DateTime.UtcNow;
    public string? Zone { get { lock (_lock) return _zone; } }
    ZoneStats Z() { var key = _zone ?? UnknownZone; if (!Zones.TryGetValue(key, out var z)) Zones[key] = z = new ZoneStats(); return z; }

    public SessionEngine(ItemDictionary items) { _items = items; }

    /// <summary>Called whenever the zone is re-read. Credits the time since the last call to the zone the player was in; a null read keeps the last known zone.</summary>
    public void SetZone(string? zone)
    {
        lock (_lock)
        {
            var now = DateTime.UtcNow;
            if (_pausedAt == null) Z().Seconds += (now - _zoneSince).TotalSeconds;   // time before the first successful read lands on "Unknown zone"
            if (zone != null) _zone = zone;
            _zoneSince = now;
        }
    }

    DateTime? _pausedAt; TimeSpan _paused;
    public bool Paused { get { lock (_lock) return _pausedAt != null; } }

    /// <summary>Stops the clock while the game can't be seen (covered, minimized): rates count only the time something could be read.</summary>
    public void Pause()
    {
        lock (_lock)
        {
            if (Ended != null || _pausedAt != null) return;
            var now = DateTime.UtcNow;
            Z().Seconds += (now - _zoneSince).TotalSeconds;
            _zoneSince = now; _pausedAt = now;
        }
    }

    public void Resume() { lock (_lock) ResumeLocked(); }

    void ResumeLocked()
    {
        if (_pausedAt is not { } at) return;
        var now = DateTime.UtcNow;
        _paused += now - at; _pausedAt = null; _zoneSince = now;
    }

    /// <summary>Adds time to a zone directly: used by the layout demo and by tests.</summary>
    public void CreditZone(string zone, double seconds)
    {
        lock (_lock) { _zone = zone; _zoneSince = DateTime.UtcNow; Z().Seconds += seconds; }
    }

    /// <summary>Time the game could be read: paused stretches are left out.</summary>
    public TimeSpan Elapsed { get { lock (_lock) return (Ended ?? _pausedAt ?? DateTime.UtcNow) - Started - _paused; } }
    /// <summary>Never below one second, so rates are defined from the first event.</summary>
    public double Hours => Math.Max(Elapsed.TotalHours, 1.0 / 3600);

    public void Apply(GameEvent ev)
    {
        lock (_lock)
        {
            Events++;
            switch (ev)
            {
                case ItemAcquired a when a.ItemId == _craftedId && (a.Time - _craftedAt).TotalSeconds <= 5:
                    _craftedId = 0; CraftedSkipped++;
                    break;
                case ItemAcquired a:
                    var d = a.Bound ? ItemsBound : Items;
                    d[a.ItemId] = d.GetValueOrDefault(a.ItemId) + a.Count;
                    var g = GatheringCatalog.Find(_items.NameOf(a.ItemId));
                    if (g != null)
                    {
                        var sk = Skill(g.Skill); sk.Items += a.Count;
                        if (g.IsNode) { Nodes++; sk.Nodes++; Z().Nodes++; Mark(_recentGathers, a.Time); _lastSkill = g.Skill; _lastSkillAt = a.Time; if (a.Count >= 2) { Plentiful++; sk.Plentiful++; } }
                        if (g.Bonus) { BonusFinds += a.Count; sk.Bonus += a.Count; }
                    }
                    else Z().Drops += a.Count;
                    break;
                case PetCrystal p:
                    Pets[p.Name] = Pets.GetValueOrDefault(p.Name) + p.Count;
                    PetCrystals += p.Count;
                    var zp = Z(); zp.Pets += p.Count; zp.PetsByName[p.Name] = zp.PetsByName.GetValueOrDefault(p.Name) + p.Count;
                    break;
                case GatherFailed f:
                    Nodes++; NodesFailed++; Z().Nodes++; Mark(_recentGathers, f.Time);
                    var ff = _lastSkill != null && (f.Time - _lastSkillAt).TotalSeconds <= 45 ? _lastSkill : GatherSkills.FromSkillText(f.Skill);
                    if (ff != null) { var sk = Skill(ff.Value); sk.Nodes++; sk.Failed++; }
                    break;
                case KinahChanged k:
                    // bound Kinah comes from quests, titles and rewards, not from mobs: tracked, but not farming income or a kill
                    if (k.Bound) { KinahBound += k.Delta; RewardsSkipped++; }
                    else { Kinah += k.Delta; if (k.Delta > 0) { KinahDrops++; var zk = Z(); zk.Kinah += k.Delta; zk.KinahDrops++; Mark(_recentKills, k.Time); } }
                    break;
                case RewardReceived:
                    RewardsSkipped++;
                    break;
                case MobKilled mk:
                    KillsDefeated++; Z().Defeated++; Mark(_recentKills, mk.Time);
                    break;
                case XpGained:
                    KillsXp++; Z().Xp++;
                    break;
                case CurrencyGained c:
                    CurrencyLines++; Z().CurrencyLines++; Mark(_recentKills, c.Time);
                    Currencies[c.Name] = Currencies.GetValueOrDefault(c.Name) + c.Amount;
                    break;
                case CraftResult cr:
                {
                    // "Crafting failed." may not name the item: it is the recipe crafted just before, when there is one
                    var name = cr.Item.Length >= 2 ? cr.Item : (cr.Time - _lastCraftAt).TotalMinutes <= 2 ? _lastCraft : "";
                    var key = name.Length >= 2 ? _items.KeyOf(_items.IdOf(name)) : "?";
                    if (!Crafts.TryGetValue(key, out var o)) Crafts[key] = o = new Odds();
                    if (cr.Ok) { o.Ok++; _craftedId = _items.IdOf(name); _craftedAt = cr.Time; } else o.Fail++;
                    if (name.Length >= 2) { _lastCraft = name; _lastCraftAt = cr.Time; }
                    break;
                }
                case EnchantResult en:
                {
                    var item = en.Item.Length >= 2 ? _items.KeyOf(_items.IdOf(en.Item)) : "?";
                    var key = $"{item}|{(en.From >= 0 ? en.From.ToString() : "?")}|{(en.To >= 0 ? en.To.ToString() : "?")}";
                    if (!Enchants.TryGetValue(key, out var o)) Enchants[key] = o = new Odds();
                    if (en.Ok) o.Ok++; else o.Fail++;
                    break;
                }
                case MarketSold s:
                    Sold++; SoldKinah += s.Total;
                    break;
                case MarketListed:
                    Listed++;
                    break;
                case MarketExpired:
                    Expired++;
                    break;
            }
        }
    }

    /// <summary>Copies of the craft and enchant counters, safe to read from the UI thread and to share.</summary>
    public (Dictionary<string, Odds> Crafts, Dictionary<string, Odds> Enchants) OddsSnapshot()
    {
        lock (_lock) return (Crafts.ToDictionary(kv => kv.Key, kv => new Odds { Ok = kv.Value.Ok, Fail = kv.Value.Fail }), Enchants.ToDictionary(kv => kv.Key, kv => new Odds { Ok = kv.Value.Ok, Fail = kv.Value.Fail }));
    }

    /// <summary>Craft and enchant attempts in this count.</summary>
    public (int Crafts, int CraftsOk, int Enchants, int EnchantsOk) OddsTotals()
    {
        lock (_lock) return (Crafts.Values.Sum(o => o.Attempts), Crafts.Values.Sum(o => o.Ok), Enchants.Values.Sum(o => o.Attempts), Enchants.Values.Sum(o => o.Ok));
    }

    public void Finish()
    {
        lock (_lock)
        {
            if (Ended != null) return;
            ResumeLocked();
            Ended = DateTime.UtcNow;
            Z().Seconds += (Ended.Value - _zoneSince).TotalSeconds;
        }
    }

    /// <summary>A consistent snapshot of the totals, taken under the same lock the reader thread uses.</summary>
    public (long Kinah, long KinahBound, long Drops, int Kills, int Events, int Nodes, int Bonus, int Rewards, int Failed, int Plentiful, int Pets) Totals()
    {
        lock (_lock) return (Kinah, KinahBound, Items.Values.Sum() + ItemsBound.Values.Sum(), Kills, Events, Nodes, BonusFinds, RewardsSkipped, NodesFailed, Plentiful, PetCrystals);
    }

    /// <summary>A copy of the per-zone counters with the time credited up to now, safe to read from the UI thread.</summary>
    public Dictionary<string, ZoneStats> ZoneSnapshot()
    {
        lock (_lock)
        {
            var now = Ended ?? DateTime.UtcNow;
            var copy = new Dictionary<string, ZoneStats>();
            foreach (var (k, v) in Zones)
                copy[k] = new ZoneStats { Seconds = v.Seconds + (k == (_zone ?? UnknownZone) && Ended == null && _pausedAt == null ? (now - _zoneSince).TotalSeconds : 0), Kinah = v.Kinah, KinahDrops = v.KinahDrops, CurrencyLines = v.CurrencyLines, Defeated = v.Defeated, Xp = v.Xp, Pets = v.Pets, Drops = v.Drops, Nodes = v.Nodes, PetsByName = new Dictionary<string, int>(v.PetsByName) };
            return copy;
        }
    }

    public Dictionary<string, int> PetSnapshot() { lock (_lock) return new Dictionary<string, int>(Pets); }

    /// <summary>A copy of the per-skill counters, safe to read from the UI thread.</summary>
    public Dictionary<GatherFamily, SkillStats> SkillSnapshot()
    {
        lock (_lock) return Skills.ToDictionary(kv => kv.Key, kv => new SkillStats { Nodes = kv.Value.Nodes, Failed = kv.Value.Failed, Plentiful = kv.Value.Plentiful, Items = kv.Value.Items, Bonus = kv.Value.Bonus });
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
