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
    /// <summary>Crafting, apart from farming: recipes, tries, successes and what came out. Never drops, kills, farming time or zone numbers.</summary>
    readonly CraftLedger _craft = new();
    /// <summary>Enchant results by "item|from|to"; a level the line didn't show is "?".</summary>
    public Dictionary<string, Odds> Enchants { get; } = new();
    /// <summary>Item lines that were a craft's own output ("Artisan's Orichalcum Longsword acquired." after the craft): recorded under crafting, not as drops.</summary>
    public int CraftedSkipped { get; private set; }

    /// <summary>Copies of the crafting ledger, safe to read from the UI thread.</summary>
    public List<CraftLedger.Recipe> CraftSnapshot() { lock (_lock) return _craft.Snapshot(); }

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
        /// <summary>Seconds of farming in the zone: time within a minute of a loot, kill or gather line. What the zone's per-hour rates divide by.</summary>
        public double Active;
        public Dictionary<string, int> PetsByName = new();
        /// <summary>Items picked up in this zone, by item id, tradeable and bound apart like the run-level maps. What the site files under this zone, so a run that moved zones never credits one zone with another's drops.</summary>
        public Dictionary<long, int> Items = new(), ItemsBound = new();
        public int Kills => Math.Max(Math.Max(Defeated, Xp), Math.Max(KinahDrops, CurrencyLines));
        public double Hours => Math.Max(Seconds / 3600, 1.0 / 3600);
    }
    public const string UnknownZone = "Unknown zone";
    public Dictionary<string, ZoneStats> Zones { get; } = new();
    string? _zone; DateTime _zoneSince = DateTime.UtcNow;
    public string? Zone { get { lock (_lock) return _zone; } }
    ZoneStats Z() { var key = _zone ?? UnknownZone; if (!Zones.TryGetValue(key, out var z)) Zones[key] = z = new ZoneStats(); return z; }

    public SessionEngine(ItemDictionary items) { _items = items; }

    // ---- quest rewards ----
    // Quest and title Kinah mostly comes marked (Bound) and is skipped as a reward. Some doesn't ("A total payment of
    // 34,916 Kina has been completed." then "Acquired 34,916 Kina."), and one such line outweighed an hour of mobs. So a
    // single unbound Kinah line far above what this count's mobs pay is taken for a reward too: more than 25 times the
    // median of the last pickups (and over 5,000), or, before five pickups are known, over 25,000.
    public const long OutlierFloor = 5_000, OutlierBeforeKnown = 25_000;
    public const int OutlierTimes = 25;
    /// <summary>Kinah from lines taken for rewards (bound, a payment, or far above the mobs' usual amount): shown, never farming income.</summary>
    public long KinahSkipped { get; private set; }
    readonly List<long> _recentPickups = new();

    bool RewardSizedLocked(long delta)
    {
        if (_recentPickups.Count < 5) return delta > OutlierBeforeKnown;
        var sorted = _recentPickups.OrderBy(x => x).ToList();
        long median = sorted[sorted.Count / 2];
        return delta > Math.Max(OutlierFloor, median * OutlierTimes);
    }

    // ---- Kinah by enemy, a best guess ----
    // The target bar at the top of the game names the enemy being fought. Kinah, drops and Soul Crystals read within
    // TargetGraceSeconds of the bar last showing a name are credited to that name. With several enemies on the player the
    // bar shows one of them, so every figure here is a guess, and is labelled one wherever it is shown.
    public const double TargetGraceSeconds = 8;
    string? _target; DateTime _targetAt;

    /// <summary>What one enemy paid, as far as the target bar can tell: kills, Kinah, drops by item and Soul Crystals, in the zone it was credited in.</summary>
    public sealed class MobStats
    {
        public string Name = "", Zone = UnknownZone;
        public int KinahDrops, CurrencyLines, Crystals;
        public long Kinah;
        public Dictionary<long, int> Drops = new();
        public int Kills => Math.Max(KinahDrops, CurrencyLines);
    }
    /// <summary>By zone and name ("zone|name"): the same enemy in two zones is two rows, never mixed.</summary>
    readonly Dictionary<string, MobStats> _mobs = new();

    /// <summary>The enemy on the target bar and when it was last seen there. A null name keeps the last one, so its grace window runs out on its own.</summary>
    public void SetTarget(string? name, DateTime at)
    {
        lock (_lock)
        {
            if (name == null) return;
            _target = name; _targetAt = at;
            _recentTargets.RemoveAll(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));
            _recentTargets.Add((name, at));
            if (_recentTargets.Count > 4) _recentTargets.RemoveAt(0);
        }
    }

    // the last few enemies on the bar, newest last: with several on the player, a drop the current target can't drop
    // goes to another one just fought that can
    readonly List<(string Name, DateTime At)> _recentTargets = new();

    MobStats Mob(string name)
    {
        var zone = _zone ?? UnknownZone;
        var key = zone + "|" + name;
        if (!_mobs.TryGetValue(key, out var m)) _mobs[key] = m = new MobStats { Name = name, Zone = zone };
        return m;
    }

    MobStats? MobFor(DateTime t)
    {
        if (_target == null || Math.Abs((t - _targetAt).TotalSeconds) > TargetGraceSeconds) return null;
        return Mob(_target);
    }

    /// <summary>
    /// The enemy a drop goes to: the one on the bar, unless its loot table says it can't drop the item; then the most
    /// recent other enemy within the grace window whose table has it; else nobody (the drop still counts as a drop).
    /// An enemy without a known table keeps the drop, as before.
    /// </summary>
    MobStats? MobForDrop(DateTime t, long itemId)
    {
        var near = _recentTargets.Where(r => Math.Abs((t - r.At).TotalSeconds) <= TargetGraceSeconds).OrderByDescending(r => r.At).ToList();
        if (near.Count == 0) return null;
        var book = LootBook.Current; var item = _items.NameOf(itemId);
        // an item on no enemy's table is one the tables don't cover: it stays with the enemy on the bar
        if (book.SourcesOf(item).Count == 0 || book.CanDrop(near[0].Name, item, itemId) != false) return Mob(near[0].Name);
        foreach (var r in near.Skip(1)) if (book.CanDrop(r.Name, item, itemId) == true) { ReassignedDrops++; return Mob(r.Name); }
        UnplacedDrops++;
        return null;
    }

    /// <summary>Drops moved from the enemy on the bar to another one just fought, because only the other's loot table has the item.</summary>
    public int ReassignedDrops { get; private set; }
    /// <summary>Drops no enemy just fought can drop, by their loot tables: counted as drops, credited to no enemy.</summary>
    public int UnplacedDrops { get; private set; }

    /// <summary>Copies of the per-enemy counters, most Kinah first, safe to read from the UI thread.</summary>
    public List<MobStats> MobSnapshot()
    {
        lock (_lock) return _mobs.Values.OrderByDescending(m => m.Kinah).ThenByDescending(m => m.Kills)
            .Select(m => new MobStats { Name = m.Name, Zone = m.Zone, KinahDrops = m.KinahDrops, CurrencyLines = m.CurrencyLines, Crystals = m.Crystals, Kinah = m.Kinah, Drops = new Dictionary<long, int>(m.Drops) }).ToList();
    }

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

    // Farming time: the app notices farming from the chat itself. Every loot, kill or gather line keeps the farming
    // clock running; a minute with none and the clock stops until the next one, so time spent in town, trading or
    // away from the keyboard never dilutes a per-hour rate. Counts and drop rates (per kill, per attempt) don't depend
    // on time and keep counting regardless.
    const double GraceSeconds = 60;
    double _active; DateTime? _lastFarmAt;

    void Farmed(DateTime now)
    {
        if (_lastFarmAt is { } last)
        {
            double gap = Math.Clamp((now - last).TotalSeconds, 0, GraceSeconds);
            _active += gap; Z().Active += gap;
        }
        _lastFarmAt = now;
    }

    /// <summary>Seconds of the grace minute still running after the last farming line, up to the clock's end.</summary>
    double TrailingLocked()
    {
        if (_lastFarmAt is not { } last) return 0;
        var end = Ended ?? _pausedAt ?? DateTime.UtcNow;
        return Math.Clamp((end - last).TotalSeconds, 0, GraceSeconds);
    }

    /// <summary>Time spent farming: within a minute of a loot, kill or gather line. What every per-hour rate divides by.</summary>
    public TimeSpan Active { get { lock (_lock) return TimeSpan.FromSeconds(_active + TrailingLocked()); } }
    /// <summary>Never below one second, so rates are defined from the first event.</summary>
    public double ActiveHours => Math.Max(Active.TotalHours, 1.0 / 3600);
    /// <summary>Readable time that wasn't farming: in town, trading, away.</summary>
    public TimeSpan Idle { get { var i = Elapsed - Active; return i < TimeSpan.Zero ? TimeSpan.Zero : i; } }

    public void Apply(GameEvent ev)
    {
        lock (_lock)
        {
            Events++;
            // a Kinah line far above what the mobs pay is a quest or payment reward, not farming: counted like bound Kinah
            if (ev is KinahChanged { Bound: false, Delta: > 0 } big && RewardSizedLocked(big.Delta))
            {
                CrashLog.Note($"Kinah line far above what the mobs pay, taken for a reward and not counted as farming: {big.Delta:N0}");
                ev = new KinahChanged(big.Time, big.Delta, true);
            }
            // a craft's own item line belongs to crafting alone: not a drop, not a zone's item, not farming time
            if (ev is ItemAcquired made && _craft.TakeOutput(_items.NameOf(made.ItemId), made.Count, made.Time))
            {
                CraftedSkipped++;
                return;
            }
            // what counts as farming: loot, Kinah, kills, gathers (failed ones too), Soul Crystals and Soul Points.
            // Crafts, enchants, Market lines and quest rewards don't: they happen in town.
            if (ev is ItemAcquired or KinahChanged { Bound: false } or MobKilled or XpGained or GatherFailed or PetCrystal or CurrencyGained) Farmed(DateTime.UtcNow);
            switch (ev)
            {
                case ItemAcquired a:
                    var d = a.Bound ? ItemsBound : Items;
                    d[a.ItemId] = d.GetValueOrDefault(a.ItemId) + a.Count;
                    var zi = a.Bound ? Z().ItemsBound : Z().Items;
                    zi[a.ItemId] = zi.GetValueOrDefault(a.ItemId) + a.Count;
                    var g = GatheringCatalog.Find(_items.NameOf(a.ItemId));
                    if (g != null)
                    {
                        var sk = Skill(g.Skill); sk.Items += a.Count;
                        if (g.IsNode) { Nodes++; sk.Nodes++; Z().Nodes++; Mark(_recentGathers, a.Time); _lastSkill = g.Skill; _lastSkillAt = a.Time; if (a.Count >= 2) { Plentiful++; sk.Plentiful++; } }
                        if (g.Bonus) { BonusFinds += a.Count; sk.Bonus += a.Count; }
                    }
                    else
                    {
                        Z().Drops += a.Count;
                        if (MobForDrop(a.Time, a.ItemId) is { } md) md.Drops[a.ItemId] = md.Drops.GetValueOrDefault(a.ItemId) + a.Count;
                    }
                    break;
                case PetCrystal p:
                    Pets[p.Name] = Pets.GetValueOrDefault(p.Name) + p.Count;
                    PetCrystals += p.Count;
                    var zp = Z(); zp.Pets += p.Count; zp.PetsByName[p.Name] = zp.PetsByName.GetValueOrDefault(p.Name) + p.Count;
                    if (MobFor(p.Time) is { } mp) mp.Crystals += p.Count;
                    break;
                case GatherFailed f:
                    Nodes++; NodesFailed++; Z().Nodes++; Mark(_recentGathers, f.Time);
                    var ff = _lastSkill != null && (f.Time - _lastSkillAt).TotalSeconds <= 45 ? _lastSkill : GatherSkills.FromSkillText(f.Skill);
                    if (ff != null) { var sk = Skill(ff.Value); sk.Nodes++; sk.Failed++; }
                    break;
                case KinahChanged k:
                    // bound Kinah comes from quests, titles and rewards, not from mobs: tracked, but not farming income or a kill
                    if (k.Bound) { KinahBound += k.Delta; KinahSkipped += k.Delta; RewardsSkipped++; }
                    else
                    {
                        Kinah += k.Delta;
                        if (k.Delta > 0)
                        {
                            KinahDrops++; var zk = Z(); zk.Kinah += k.Delta; zk.KinahDrops++; Mark(_recentKills, k.Time);
                            _recentPickups.Add(k.Delta); if (_recentPickups.Count > 50) _recentPickups.RemoveAt(0);
                            if (MobFor(k.Time) is { } mk) { mk.Kinah += k.Delta; mk.KinahDrops++; }
                        }
                    }
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
                    if (MobFor(c.Time) is { } mc) mc.CurrencyLines++;
                    Currencies[c.Name] = Currencies.GetValueOrDefault(c.Name) + c.Amount;
                    break;
                case CraftResult cr:
                    _craft.OnResult(cr);
                    break;
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
        lock (_lock)
        {
            // crafts by the recipe's item key, from the crafting ledger
            var crafts = new Dictionary<string, Odds>();
            foreach (var r in _craft.Snapshot())
            {
                var key = r.Name.Length >= 2 && r.Name != "?" ? _items.KeyOf(_items.IdOf(r.Name)) : "?";
                if (!crafts.TryGetValue(key, out var o)) crafts[key] = o = new Odds();
                o.Ok += r.Ok; o.Fail += r.Fail;
            }
            return (crafts, Enchants.ToDictionary(kv => kv.Key, kv => new Odds { Ok = kv.Value.Ok, Fail = kv.Value.Fail }));
        }
    }

    /// <summary>Craft and enchant attempts in this count.</summary>
    public (int Crafts, int CraftsOk, int Enchants, int EnchantsOk) OddsTotals()
    {
        lock (_lock) { var c = _craft.Snapshot(); return (c.Sum(o => o.Attempts), c.Sum(o => o.Ok), Enchants.Values.Sum(o => o.Attempts), Enchants.Values.Sum(o => o.Ok)); }
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
                copy[k] = new ZoneStats { Seconds = v.Seconds + (k == (_zone ?? UnknownZone) && Ended == null && _pausedAt == null ? (now - _zoneSince).TotalSeconds : 0), Active = v.Active + (k == (_zone ?? UnknownZone) ? TrailingLocked() : 0), Kinah = v.Kinah, KinahDrops = v.KinahDrops, CurrencyLines = v.CurrencyLines, Defeated = v.Defeated, Xp = v.Xp, Pets = v.Pets, Drops = v.Drops, Nodes = v.Nodes, PetsByName = new Dictionary<string, int>(v.PetsByName), Items = new Dictionary<long, int>(v.Items), ItemsBound = new Dictionary<long, int>(v.ItemsBound) };
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
