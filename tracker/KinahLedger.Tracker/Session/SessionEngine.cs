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
    public int Kills => Math.Max(KillsSeen, Math.Max(Math.Max(KillsDefeated, KillsXp), Math.Max(KinahDrops, CurrencyLines)));
    /// <summary>Kills as separate pieces of evidence agree on them: see <see cref="KillEvidence"/>.</summary>
    public int KillsSeen { get; private set; }
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
    static void Mark(Queue<DateTime> q, DateTime t) { q.Enqueue(t); while (q.Count > 0 && (t - q.Peek()).TotalMinutes > MixedMinutes + 1) q.Dequeue(); }
    // the distinct minutes with a kill and with a gather, for how focused the run was (RunPayload.focus)
    readonly HashSet<long> _killMinutes = new(), _gatherMinutes = new();
    void MarkKill(DateTime t) { Mark(_recentKills, t); _killMinutes.Add(t.Ticks / TimeSpan.TicksPerMinute); }
    void MarkGather(DateTime t) { Mark(_recentGathers, t); _gatherMinutes.Add(t.Ticks / TimeSpan.TicksPerMinute); }

    /// <summary>Farming and gathering at once: both a kill and a gather in this many minutes, and the tiles show both.</summary>
    public const double MixedMinutes = 5;

    /// <summary>
    /// True while both kills and gathers happened in the last <see cref="MixedMinutes"/> (Steven, 2026-10-23: the top tiles
    /// flipped between the farming and the gathering set while he did both). It ends only once one of them has been
    /// absent that long, so a stretch of mobs between two nodes doesn't flip the tiles back and forth.
    /// </summary>
    public bool MixedAt(DateTime now)
    {
        lock (_lock)
            return _recentKills.Count > 0 && _recentGathers.Count > 0
                && (now - _recentKills.Last()).TotalMinutes <= MixedMinutes && (now - _recentGathers.Last()).TotalMinutes <= MixedMinutes;
    }

    /// <summary>
    /// How much of the run was one activity: max(K, G) / (K + G), K and G the distinct minutes with a kill and with a
    /// gather, rounded to 0.05; null when neither happened. The site keeps runs under 0.8 out of its per-hour figures.
    /// </summary>
    public double? Focus
    {
        get
        {
            lock (_lock)
            {
                int k = _killMinutes.Count, g = _gatherMinutes.Count;
                if (k + g == 0) return null;
                return Math.Round(Math.Round(Math.Max(k, g) / (double)(k + g) / 0.05) * 0.05, 2);
            }
        }
    }

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
        public double Seconds; public long Kinah; public int KinahDrops, CurrencyLines, Defeated, Xp, Pets, Drops, Nodes, KillsSeen;
        /// <summary>Seconds of farming in the zone: time within a minute of a loot, kill or gather line. What the zone's per-hour rates divide by.</summary>
        public double Active;
        public Dictionary<string, int> PetsByName = new();
        /// <summary>Items picked up in this zone, by item id, tradeable and bound apart like the run-level maps. What the site files under this zone, so a run that moved zones never credits one zone with another's drops.</summary>
        public Dictionary<long, int> Items = new(), ItemsBound = new();
        public int Kills => Math.Max(KillsSeen, Math.Max(Math.Max(Defeated, Xp), Math.Max(KinahDrops, CurrencyLines)));
        public double Hours => Math.Max(Seconds / 3600, 1.0 / 3600);
    }
    public const string UnknownZone = "Unknown zone";
    public Dictionary<string, ZoneStats> Zones { get; } = new();
    string? _zone; DateTime _zoneSince = DateTime.UtcNow;
    public string? Zone { get { lock (_lock) return _zone; } }
    // ---- kills, from the evidence that agrees on them ----
    // One kill shows up as several things: its Kinah (a chat line, a row in the loot list beside the character, matched
    // to one pickup), its Soul Points line, a "Defeated" line. Any one of them can be lost to a fast scroll or a misread;
    // each counted alone undercounts, and adding them overcounts. So each piece joins the newest kill of the last few
    // seconds that doesn't have that kind of evidence yet, and starts a kill of its own when none fits (2026-10-22).
    /// <summary>One kill as its evidence came in: the kinds seen (k Kinah, s Soul Points line, l Soul row in the loot list, d Defeated line) and the enemy it was credited to by name.</summary>
    sealed class KillRec { public DateTime At; public string Kinds = ""; public string? Mob; }
    readonly List<KillRec> _kills = new();
    public const double KillJoinSeconds = 3;
    KillRec KillEvidence(DateTime t, char kind)
    {
        // the oldest kill in reach that still lacks this kind of evidence: evidence pairs up in order
        KillRec? join = null;
        for (int i = _kills.Count - 1; i >= 0; i--)
        {
            var k = _kills[i];
            double d = (t - k.At).TotalSeconds;
            if (d > KillJoinSeconds) break;
            // evidence can come in after a later kill's (a Kina line waits for the Kinah counter, KinaWitness): the same reach either side
            if (d < -KillJoinSeconds) continue;
            if (k.Kinds.Contains(kind)) continue;
            // a Soul Points line and a Soul row are the same thing seen twice: either one names the kill
            if ((kind == 's' && k.Kinds.Contains('l')) || (kind == 'l' && k.Kinds.Contains('s'))) { if (join == null || k.At < join.At) join = k; continue; }
            join = k;
        }
        if (join != null) { join.Kinds += kind; return join; }
        var rec = new KillRec { At = t, Kinds = kind.ToString() };
        _kills.Add(rec);
        if (_kills.Count > 64) _kills.RemoveAt(0);
        KillsSeen++; Z().KillsSeen++;
        return rec;
    }

    /// <summary>The quest tracker counted one more kill ("Defeat ... (21/25)" to 22): evidence q for <see cref="KillEvidence"/>.</summary>
    public void QuestStep(DateTime t) { lock (_lock) _questSteps.Add(t); }

    // A quest step claims a kill its Kinah or Soul line already made, within QuestClaimSeconds either side, once that
    // evidence has had time to come in; only a step with no kill around it is a kill of its own (one whose loot lines
    // were lost). Joining it straight away made kills of its own too often: 46 to 49 for 40 in the synthetic fights.
    readonly List<DateTime> _questSteps = new();
    public const double QuestClaimSeconds = 6;

    /// <summary>Settles the quest steps old enough to have their kill's evidence in.</summary>
    public void SettleQuest(DateTime now, bool final = false)
    {
        lock (_lock)
        {
            for (int i = 0; i < _questSteps.Count; i++)
            {
                var q = _questSteps[i];
                if (!final && (now - q).TotalSeconds < QuestClaimSeconds) continue;
                var claim = _kills.Where(k => !k.Kinds.Contains('q') && Math.Abs((k.At - q).TotalSeconds) <= QuestClaimSeconds)
                                  .OrderBy(k => Math.Abs((k.At - q).TotalSeconds)).FirstOrDefault();
                if (claim != null) claim.Kinds += 'q';
                else
                {
                    _kills.Add(new KillRec { At = q, Kinds = "q" });
                    if (_kills.Count > 64) _kills.RemoveAt(0);
                    KillsSeen++; Z().KillsSeen++;
                }
                _questSteps.RemoveAt(i--);
            }
        }
    }

    // ---- the enemies fighting the player, from their nameplates (Nameplates: a red health bar under a gold name) ----
    // How many of each are up, and the ones whose bar just went: an enemy whose bar vanishes as its loot lands is the one
    // that died (Steven, 2026-10-22: "look at the char on screen to see which healthbars around it are being fought").
    readonly Dictionary<string, int> _engaged = new(StringComparer.OrdinalIgnoreCase);
    sealed class Gone { public string Name = ""; public DateTime At; public bool Kinah, Soul; }
    readonly List<Gone> _vanished = new();
    public const double VanishSeconds = 4;

    /// <summary>One read of the nameplates: every engaged enemy by name, as often as it shows.</summary>
    public void SetEngaged(IEnumerable<string> names, DateTime at)
    {
        lock (_lock)
        {
            var now = names.GroupBy(n => n, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
            foreach (var (n, c) in _engaged) { int left = now.GetValueOrDefault(n); for (int i = left; i < c; i++) _vanished.Add(new Gone { Name = n, At = at }); }
            _engaged.Clear(); foreach (var (n, c) in now) _engaged[n] = c;
            _vanished.RemoveAll(v => (at - v.At).TotalSeconds > VanishSeconds * 3);
        }
    }

    /// <summary>
    /// The enemy whose bar went in the seconds before this loot; null when none did. Each one gives one Kinah pickup and
    /// one soul (kind 'k' or 's'), so two enemies falling together are two kills, and a kill's soul line still sees the
    /// enemy its Kinah went to.
    /// </summary>
    string? TakeVanished(DateTime t, char kind, Func<string, bool>? fits = null)
    {
        var v = _vanished.FirstOrDefault(v => (t - v.At).TotalSeconds is >= -1 and <= VanishSeconds && !(kind == 'k' ? v.Kinah : v.Soul) && (fits == null || fits(v.Name)));
        if (v == null) return null;
        if (kind == 'k') v.Kinah = true; else v.Soul = true;
        return v.Name;
    }

    /// <summary>
    /// A kill named by its soul (a Soul Points line, or a "Soul: Drakan Probe" row in the loot list): one kill of that
    /// enemy, and the Kinah that landed just before it goes with it, whatever the target bar read. Once per kill.
    /// </summary>
    void CreditSoul(string kind, DateTime t, KillRec rec)
    {
        if (rec.Mob != null || kind.Length < 3) return;
        var zone = _zone ?? UnknownZone;
        // first the enemies whose bars just went and those still fighting, then the target bar, then this zone's
        var seen = _vanished.Where(v => (t - v.At).TotalSeconds is >= -1 and <= VanishSeconds && !v.Soul).OrderByDescending(v => v.At).Select(v => v.Name)
            .Concat(_engaged.Keys)
            .Concat(_recentTargets.Where(r => Math.Abs((t - r.At).TotalSeconds) <= TargetGraceSeconds).OrderByDescending(r => r.At).Select(r => r.Name)).ToList();
        var enemy = KinahLedger.Ocr.TargetReader.EnemyForSoul(kind, seen, _mobs.Values.Where(m => m.Zone == zone).OrderByDescending(m => m.Kills).Select(m => m.Name).ToList());
        // a soul that fits no enemy on screen (a soul named for a beast, from a humanoid): the enemy whose bar just went
        if (!seen.Contains(enemy, StringComparer.OrdinalIgnoreCase) && TakeVanished(t, 's') is { } went) enemy = went;
        else if (seen.Contains(enemy, StringComparer.OrdinalIgnoreCase)) TakeVanished(t, 's', n => string.Equals(n, enemy, StringComparison.OrdinalIgnoreCase));
        if (enemy.Length == 0) return;
        var ms = Mob(SameAs(enemy));
        rec.Mob = ms.Name;
        ms.CurrencyLines++;
        int last = _kinahCredits.FindLastIndex(x => !x.Soul && (t - x.At).TotalSeconds is >= -1 and <= SoulKinahSeconds);
        if (last >= 0)
        {
            var (at, amt, to, _) = _kinahCredits[last];
            if (!ReferenceEquals(to, ms)) { if (to != null) { to.Kinah -= amt; to.KinahDrops--; } ms.Kinah += amt; ms.KinahDrops++; }
            _kinahCredits[last] = (at, amt, ms, true);
        }
        // until the bar names someone else, what lands next belongs to this enemy too
        _target = ms.Name; _targetAt = t;
        _recentTargets.RemoveAll(r => string.Equals(r.Name, ms.Name, StringComparison.OrdinalIgnoreCase));
        _recentTargets.Add((ms.Name, t)); if (_recentTargets.Count > 4) _recentTargets.RemoveAt(0);
    }

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
    DateTime _craftWindowAt = DateTime.MinValue;
    /// <summary>When the crafting window was last seen open (its skill tabs in the band, or its own text over the chat).</summary>
    public void SetCraftWindow(DateTime at) { lock (_lock) if (at > _craftWindowAt) _craftWindowAt = at; }
    /// <summary>The <see cref="GameEvent.Via"/> of a craft read from the crafting panel's bars.</summary>
    public const string CraftPanelVia = "crafting panel";
    /// <summary>The crafting panel is on screen, crafting this recipe ("" when its text wasn't read).</summary>
    public void CraftPanelSeen(string recipe, DateTime at) { lock (_lock) _craft.PanelSeen(recipe, at); }
    public bool CraftPanelActive(DateTime at) { lock (_lock) return _craft.PanelActive(at); }
    bool CraftWindowOpen(DateTime t) => KinahLedger.Ocr.TargetReader.CraftOpen(_craftWindowAt, t);

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

    /// <summary>How long before its Soul Points line a kill's Kinah can land and still be that kill's.</summary>
    public const double SoulKinahSeconds = 3;
    // the last Kinah pickups and who they were credited to; Soul tells whether a Soul Points line already confirmed it
    readonly List<(DateTime At, long Delta, MobStats? To, bool Soul)> _kinahCredits = new();

    /// <summary>The enemy a Kinah pickup was credited to (the target bar's, or the one a Soul Points line named since), for the live feed; null when nobody.</summary>
    public string? CreditedTo(DateTime at, long delta)
    {
        lock (_lock)
        {
            for (int i = _kinahCredits.Count - 1; i >= 0; i--)
                if (_kinahCredits[i].At == at && _kinahCredits[i].Delta == delta) return _kinahCredits[i].To?.Name;
            return null;
        }
    }

    /// <summary>An enemy name read slightly differently from one already in this zone's rows ("Dracunt Herbalist") is that one.</summary>
    string SameAs(string name)
    {
        var zone = _zone ?? UnknownZone;
        foreach (var m in _mobs.Values)
        {
            if (m.Zone != zone || string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
            int max = Math.Max(1, Math.Min(m.Name.Length, name.Length) / 8);
            if (Math.Abs(m.Name.Length - name.Length) <= max && Lev(m.Name.ToLowerInvariant(), name.ToLowerInvariant()) <= max) return m.Name;
        }
        return name;
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
        // an enemy left with nothing (its Kinah moved to the one a Soul Points line named) is no row
        lock (_lock) return _mobs.Values.Where(m => m.Kills > 0 || m.Kinah != 0 || m.Crystals > 0 || m.Drops.Count > 0).OrderByDescending(m => m.Kinah).ThenByDescending(m => m.Kills)
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
            // an area left alone (Nightmare): nothing there is farming, a run or a drop rate (Steven, 2026-10-22)
            if (KinahLedger.Ocr.ZoneLocator.KindOf(_zone) == KinahLedger.Ocr.ZoneLocator.Ignored) return;
            Events++;
            // a Kinah line far above what the mobs pay is a quest or payment reward, not farming: counted like bound Kinah
            if (ev is KinahChanged { Bound: false, Delta: > 0 } big && RewardSizedLocked(big.Delta))
            {
                CrashLog.Note($"Kinah line far above what the mobs pay, taken for a reward and not counted as farming: {big.Delta:N0}");
                ev = new KinahChanged(big.Time, big.Delta, true);
            }
            // the item the crafting panel is making, picked up while it is up or just gone: the craft's own output, already
            // counted from the panel's race, so neither a drop nor a second craft. Every other pickup is what it is: crafting
            // never takes a pickup (Steven, 2026-10-23: "it needs to not be drops for crafting")
            // With the crafting window open, an item a recipe makes is set aside the same way: likely a craft whose bars weren't
            // seen, so it is no craft (no bars, no craft) and no drop either
            if (ev is ItemAcquired made && (_craft.IsPanelOutput(_items.NameOf(made.ItemId), made.Time)
                || (CraftWindowOpen(made.Time) && RecipeBook.ForOutput(_items.NameOf(made.ItemId)) != null)))
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
                    if (a.Unconfirmed) _unconfirmed[a.ItemId] = _unconfirmed.GetValueOrDefault(a.ItemId) + a.Count;
                    var g = GatheringCatalog.Find(_items.NameOf(a.ItemId));
                    if (g != null)
                    {
                        var sk = Skill(g.Skill); sk.Items += a.Count;
                        if (g.IsNode) { Nodes++; sk.Nodes++; Z().Nodes++; MarkGather(a.Time); _lastSkill = g.Skill; _lastSkillAt = a.Time; if (a.Count >= 2) { Plentiful++; sk.Plentiful++; } }
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
                    Nodes++; NodesFailed++; Z().Nodes++; MarkGather(f.Time);
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
                            KinahDrops++; var zk = Z(); zk.Kinah += k.Delta; zk.KinahDrops++; MarkKill(k.Time);
                            KillEvidence(k.Time, 'k');
                            _recentPickups.Add(k.Delta); if (_recentPickups.Count > 50) _recentPickups.RemoveAt(0);
                            // the enemy whose bar just went, else the one on the target bar
                            var mk = TakeVanished(k.Time, 'k') is { } went ? Mob(went) : MobFor(k.Time);
                            if (mk != null) { mk.Kinah += k.Delta; mk.KinahDrops++; }
                            _kinahCredits.Add((k.Time, k.Delta, mk, false)); if (_kinahCredits.Count > 8) _kinahCredits.RemoveAt(0);
                        }
                    }
                    break;
                case RewardReceived:
                    RewardsSkipped++;
                    break;
                case MobKilled mk:
                    KillsDefeated++; Z().Defeated++; MarkKill(mk.Time);
                    KillEvidence(mk.Time, 'd');
                    break;
                case XpGained:
                    KillsXp++; Z().Xp++;
                    break;
                case CurrencyGained c:
                    // only Soul Points come one per kill; another currency ("Stigma Shard points") is kept, never a kill
                    if (c.Name.EndsWith("Soul Points", StringComparison.OrdinalIgnoreCase)) { CurrencyLines++; Z().CurrencyLines++; MarkKill(c.Time); }
                    // a Soul Points line names the kind of enemy that died: one kill of it, and the Kinah of that kill goes with it,
                    // whatever the target bar read (Steven, 2026-10-22: three different enemies killed, one shown)
                    if (c.Name.EndsWith("Soul Points", StringComparison.OrdinalIgnoreCase))
                    {
                        var rec = KillEvidence(c.Time, 's');
                        if (c.Enemy.Length >= 3) CreditSoul(c.Enemy, c.Time, rec);
                        else if (rec.Mob == null && MobFor(c.Time) is { } mc) { mc.CurrencyLines++; rec.Mob = mc.Name; }
                    }

                    Currencies[c.Name] = Currencies.GetValueOrDefault(c.Name) + c.Amount;
                    break;
                case EnemySoul es:
                    // a "Soul: <enemy>" row in the loot list beside the character: every enemy drops its soul, so a new row is a kill of it
                    MarkKill(es.Time); Z();
                    CreditSoul(es.Kind, es.Time, KillEvidence(es.Time, 'l'));
                    break;
                case CraftResult cr:
                    // the crafting panel's races are the count, and the only one: a craft line from the chat or anywhere else
                    // makes no craft (Steven, 2026-10-23: "no bars, no craft entry")
                    if (cr.Via == CraftPanelVia) _craft.OnPanel(cr);
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
                copy[k] = new ZoneStats { Seconds = v.Seconds + (k == (_zone ?? UnknownZone) && Ended == null && _pausedAt == null ? (now - _zoneSince).TotalSeconds : 0), Active = v.Active + (k == (_zone ?? UnknownZone) ? TrailingLocked() : 0), Kinah = v.Kinah, KinahDrops = v.KinahDrops, CurrencyLines = v.CurrencyLines, Defeated = v.Defeated, Xp = v.Xp, Pets = v.Pets, Drops = v.Drops, Nodes = v.Nodes, KillsSeen = v.KillsSeen, PetsByName = new Dictionary<string, int>(v.PetsByName), Items = new Dictionary<long, int>(v.Items), ItemsBound = new Dictionary<long, int>(v.ItemsBound) };
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

    readonly Dictionary<long, int> _unconfirmed = new();
    /// <summary>Copies of an item the chat never confirmed: from the loot list, or a guessed name. Shown as a tag on its row.</summary>
    public int Unconfirmed(long id) { lock (_lock) return _unconfirmed.GetValueOrDefault(id); }

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
