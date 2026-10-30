using System.Text.Json;
using KinahLedger.Data;

namespace KinahLedger.Session;

/// <summary>
/// Today's count across restarts of the tracker (0.8.50, Steven 2026-10-27: "see your drops today in farming and
/// expedition"). The window's Today strip reads one engine fed every line since midnight; when the tracker closes, its
/// figures go to today.json, and the next start seeds a fresh engine with them so the day counts on. Display only: what is
/// shared to the site comes from the other counts, as before.
/// </summary>
public sealed partial class SessionEngine
{
    /// <summary>Readable time counted before this engine started (Today's earlier stretches); added to <see cref="Elapsed"/>.</summary>
    TimeSpan _carried;

    /// <summary>The item list this count names and prices its items by: what days.json's day entry is worked out with (DayStore.Save).</summary>
    internal ItemDictionary ItemBook => _items;

    /// <summary>
    /// What a day's count carries across a restart: the totals the Farming tables and the Today strip read, by zone and by
    /// enemy. Not the reader's own working state (recent pickups, kill evidence, crafting), which starts fresh.
    /// </summary>
    public sealed class Carry
    {
        /// <summary>The local calendar day it belongs to, "2026-10-27".</summary>
        public string Day { get; set; } = "";
        public double Elapsed { get; set; }
        public double Active { get; set; }
        public long Kinah { get; set; }
        public long KinahBound { get; set; }
        public long KinahSkipped { get; set; }
        public int KillsSeen { get; set; }
        public int Defeated { get; set; }
        public int Xp { get; set; }
        public int KinahDrops { get; set; }
        public int CurrencyLines { get; set; }
        public int Events { get; set; }
        public int Nodes { get; set; }
        public int NodesFailed { get; set; }
        public int Plentiful { get; set; }
        public int Bonus { get; set; }
        public int Rewards { get; set; }
        public int PetCrystals { get; set; }
        public Dictionary<long, int> Items { get; set; } = new();
        public Dictionary<long, int> ItemsBound { get; set; } = new();
        public Dictionary<string, int> Pets { get; set; } = new();
        public Dictionary<string, ZoneStats> Zones { get; set; } = new();
        public List<MobStats> Mobs { get; set; } = new();
        public Dictionary<string, SkillStats> Skills { get; set; } = new();
        public Dictionary<string, NodeStats> GatherNodes { get; set; } = new();
    }

    /// <summary>The count's figures as of now, for today.json.</summary>
    public Carry ToCarry(string day)
    {
        var zones = ZoneSnapshot();
        lock (_lock)
        {
            return new Carry
            {
                Day = day,
                Elapsed = ((Ended ?? _pausedAt ?? DateTime.UtcNow) - Started - _paused + _carried).TotalSeconds,
                Active = _active + TrailingLocked(),
                Kinah = Kinah, KinahBound = KinahBound, KinahSkipped = KinahSkipped,
                KillsSeen = KillsSeen, Defeated = KillsDefeated, Xp = KillsXp, KinahDrops = KinahDrops, CurrencyLines = CurrencyLines,
                Events = Events, Nodes = Nodes, NodesFailed = NodesFailed, Plentiful = Plentiful, Bonus = BonusFinds, Rewards = RewardsSkipped, PetCrystals = PetCrystals,
                Items = new Dictionary<long, int>(Items), ItemsBound = new Dictionary<long, int>(ItemsBound), Pets = new Dictionary<string, int>(Pets),
                Zones = zones,
                Mobs = _mobs.Values.Select(m => new MobStats { Name = m.Name, Zone = m.Zone, KinahDrops = m.KinahDrops, CurrencyLines = m.CurrencyLines, Crystals = m.Crystals, Kinah = m.Kinah, Drops = new Dictionary<long, int>(m.Drops) }).ToList(),
                Skills = Skills.ToDictionary(kv => kv.Key.ToString(), kv => new SkillStats { Nodes = kv.Value.Nodes, Failed = kv.Value.Failed, Plentiful = kv.Value.Plentiful, Items = kv.Value.Items, Bonus = kv.Value.Bonus }),
                GatherNodes = GatherNodes.ToDictionary(kv => kv.Key, kv => new NodeStats { Family = kv.Value.Family, Ok = kv.Value.Ok, Failed = kv.Value.Failed }, StringComparer.OrdinalIgnoreCase),
            };
        }
    }

    /// <summary>
    /// Starts this (fresh) engine from an earlier stretch's figures: everything after counts on top. Each counter of kills
    /// starts from its own saved value, so the kills read the same as before the restart and grow from there.
    /// </summary>
    public void Seed(Carry c)
    {
        lock (_lock)
        {
            _carried += TimeSpan.FromSeconds(Math.Max(0, c.Elapsed));
            _active += Math.Max(0, c.Active);
            Kinah += c.Kinah; KinahBound += c.KinahBound; KinahSkipped += c.KinahSkipped;
            KillsSeen += c.KillsSeen; KillsDefeated += c.Defeated; KillsXp += c.Xp; KinahDrops += c.KinahDrops; CurrencyLines += c.CurrencyLines;
            Events += c.Events; Nodes += c.Nodes; NodesFailed += c.NodesFailed; Plentiful += c.Plentiful; BonusFinds += c.Bonus; RewardsSkipped += c.Rewards; PetCrystals += c.PetCrystals;
            foreach (var (id, n) in c.Items) Items[id] = Items.GetValueOrDefault(id) + n;
            foreach (var (id, n) in c.ItemsBound) ItemsBound[id] = ItemsBound.GetValueOrDefault(id) + n;
            foreach (var (k, n) in c.Pets) Pets[k] = Pets.GetValueOrDefault(k) + n;
            foreach (var (k, z) in c.Zones)
            {
                if (!Zones.TryGetValue(k, out var to)) Zones[k] = to = new ZoneStats();
                to.Seconds += z.Seconds; to.Active += z.Active; to.Kinah += z.Kinah; to.KinahDrops += z.KinahDrops; to.CurrencyLines += z.CurrencyLines;
                to.Defeated += z.Defeated; to.Xp += z.Xp; to.Pets += z.Pets; to.Drops += z.Drops; to.Nodes += z.Nodes; to.KillsSeen += z.KillsSeen;
                foreach (var (p, n) in z.PetsByName) to.PetsByName[p] = to.PetsByName.GetValueOrDefault(p) + n;
                foreach (var (id, n) in z.Items) to.Items[id] = to.Items.GetValueOrDefault(id) + n;
                foreach (var (id, n) in z.ItemsBound) to.ItemsBound[id] = to.ItemsBound.GetValueOrDefault(id) + n;
            }
            foreach (var m in c.Mobs)
            {
                var key = m.Zone + "|" + m.Name;
                if (!_mobs.TryGetValue(key, out var to)) _mobs[key] = to = new MobStats { Name = m.Name, Zone = m.Zone };
                to.KinahDrops += m.KinahDrops; to.CurrencyLines += m.CurrencyLines; to.Crystals += m.Crystals; to.Kinah += m.Kinah;
                foreach (var (id, n) in m.Drops) to.Drops[id] = to.Drops.GetValueOrDefault(id) + n;
            }
            foreach (var (k, s) in c.Skills)
            {
                if (!Enum.TryParse<KinahLedger.Data.GatherFamily>(k, out var f)) continue;
                var to = Skill(f);
                to.Nodes += s.Nodes; to.Failed += s.Failed; to.Plentiful += s.Plentiful; to.Items += s.Items; to.Bonus += s.Bonus;
            }
            foreach (var (k, s) in c.GatherNodes)
            {
                if (!GatherNodes.TryGetValue(k, out var to)) GatherNodes[k] = to = new NodeStats { Family = s.Family };
                to.Ok += s.Ok; to.Failed += s.Failed;
            }
        }
    }

    /// <summary>The JSON for today.json: the stats classes keep their counters in fields.</summary>
    public static readonly JsonSerializerOptions CarryJson = new() { IncludeFields = true };
}
