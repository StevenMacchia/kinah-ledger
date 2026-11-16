using System.Text.Json.Serialization;
using KinahLedger.Packets;

namespace KinahLedger.Parser;

/// <summary>
/// One record of the Parser (Parser/BossParser.cs decides), by its <see cref="Mode"/>: a boss pull ("boss": the pull to
/// the boss's death, a wipe or leaving, in a Transcendence, an Expedition or a Sanctuary), an instance run ("run":
/// entering to leaving, each boss a <see cref="Split"/>), or a fight anywhere ("fight": the first hit or kill to a pause).
/// Kept on this PC in the character's encounters.jsonl (Parser/EncounterStore.cs); never uploaded.
/// </summary>
public sealed class Encounter : IJsonOnDeserialized
{
    /// <summary>Short and unique: the file's key for this record.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..12];
    /// <summary>"boss", "run" or "fight" (BossParser.ModeKey); a record kept before the modes is a boss pull.</summary>
    public string Mode { get; set; } = "boss";
    [JsonIgnore] public ParserMode ModeKind => BossParser.ModeOf(Mode);
    /// <summary>"Transcendence", "Expedition" or "Sanctuary".</summary>
    public string Kind { get; set; } = "";
    /// <summary>The instance's name ("Shattered Arkanis").</summary>
    public string Instance { get; set; } = "";
    /// <summary>The instance's mode as the game names it ("Conquest"), "" when not known: the header says "Ferocious Horn Den · Conquest".</summary>
    public string GameMode { get; set; } = "";
    /// <summary>The game's id for this copy of the instance (0 = unknown): a re-entry after a disconnect is the same copy.</summary>
    public long InstanceId { get; set; }
    public string Boss { get; set; } = "";
    public long BossId { get; set; }
    /// <summary>The instance's last boss on the boss list: killing it clears the instance.</summary>
    public bool FinalBoss { get; set; }
    /// <summary>A wave stage (bosses.txt "wave": Ferocious Horn Den's Totem room), not a boss: a split in Full run, never a boss pull of its own.</summary>
    public bool Wave { get; set; }
    public DateTime Start { get; set; }
    /// <summary>When it ended; null while it is under way.</summary>
    public DateTime? End { get; set; }
    /// <summary>The last thing heard of the fight: an open fight with nothing new for a long while is given up at this time.</summary>
    public DateTime LastAt { get; set; }
    /// <summary>The last damage dealt in it, and the boss's last HP fall: a wipe or leaving ends there, never later (null before any).</summary>
    public DateTime? LastDamageAt { get; set; }
    public DateTime? LastHpFallAt { get; set; }
    /// <summary>A pull: "kill", "wipe", "left". A run: "cleared" (its final boss died), "left". A fight: "died", "kill" (you killed), "ended". "" while under way.</summary>
    public string Outcome { get; set; } = "";
    /// <summary>
    /// Some of the fight wasn't seen: the reader lost traffic, the tracker restarted, the game reconnected. The fight is
    /// still one fight (never split in two); its damage and kills may be short, and the page says so.
    /// </summary>
    public bool Partial { get; set; }
    /// <summary>What made it partial, in words ("2 gaps in the game's traffic", "the tracker restarted").</summary>
    public List<string> PartialWhy { get; set; } = new();
    /// <summary>The group at the pull (and anyone who joined during the fight, marked Late).</summary>
    public List<Fighter> Roster { get; set; } = new();
    /// <summary>Enemies that died near the player during the fight (adds, the boss itself last when it died).</summary>
    public List<KillNear> Kills { get; set; } = new();
    /// <summary>True when the game's messages gave damage for this fight: only then are damage and DPS shown (never made up).</summary>
    public bool HasDamage { get; set; }
    /// <summary>Damage dealt to the boss itself (a run's: to its bosses; a wave's: to anything while it is open), by everyone, credited or not, when known: the HP the boss lost.</summary>
    public long BossDamage { get; set; }
    /// <summary>
    /// Damage no hit named a source for (HP lost with no hit to credit), to any target and to the boss alone. It is in no
    /// member and no row (Steven, 2026-11-14: "no 'Other'"); the group card says how much of the boss's HP is credited
    /// (<see cref="Coverage"/>) instead.
    /// </summary>
    public long Uncredited { get; set; }
    public long UncreditedBoss { get; set; }
    /// <summary>The uncredited damage by the reason given for it (DamageDealt.Reason; 0 = none given).</summary>
    public Dictionary<int, long> UncreditedWhy { get; set; } = new();

    /// <summary>A reason for uncredited damage in the coverage hover's words (DamageDealt.Reason, 1 to 7).</summary>
    public static string WhyWords(int reason) => reason switch
    {
        1 or 2 or 3 => "a moment the tracker missed",
        4 => "someone outside the group",
        5 => "the boss's surroundings (not a player)",
        6 or 7 => "an attacker the tracker couldn't place",
        _ => "another reason",
    };
    /// <summary>
    /// Each member's damage to the boss is kept apart from their damage to everything (Fighter.BossDamage): records kept
    /// since the parser's honest pass. An older record blended the two and shows all targets.
    /// </summary>
    public bool TargetsSplit { get; set; }
    /// <summary>The boss's HP now (at the end, for a kept fight) and its max, when the game gave them; 0 = not known.</summary>
    public long BossHp { get; set; }
    public long BossMaxHp { get; set; }
    /// <summary>The share of the boss's HP left, 0 to 1, or null when not known.</summary>
    [JsonIgnore] public double? BossHpLeft => BossMaxHp > 0 ? Math.Clamp(BossHp / (double)BossMaxHp, 0, 1) : null;
    /// <summary>The tracker version that kept it.</summary>
    public string App { get; set; } = "";
    /// <summary>
    /// The boss's HP left (0 to 1) at the end of each second from <see cref="Start"/> (store v2, a pull's timeline); null
    /// for a record kept before, or with no HP known.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<double>? BossHpPerSecond { get; set; }

    /// <summary>The second of the record an instant falls in (0 at <see cref="Start"/>; before it, 0).</summary>
    public int SecondOf(DateTime at) => (int)Math.Max(0, Math.Floor((at - Start).TotalSeconds));

    /// <summary>
    /// The record's start moves (a pull starts at its first damage): the per-second lists move with it, so second 0 is
    /// still the start (seconds before a new later start dropped; an earlier start adds seconds in front).
    /// </summary>
    public void MoveStart(DateTime to)
    {
        int shift = (int)Math.Floor((to - Start).TotalSeconds);
        Start = to;
        if (shift == 0) return;
        static void Move<T>(List<T>? l, int k, T fill)
        {
            if (l == null) return;
            if (k > 0) l.RemoveRange(0, Math.Min(k, l.Count));
            else l.InsertRange(0, Enumerable.Repeat(fill, -k));
        }
        Move(BossHpPerSecond, shift, 1.0);
        foreach (var f in Roster) { Move(f.PerSecond, shift, 0L); Move(f.BossPerSecond, shift, 0L); }
    }
    /// <summary>Gaps in the game's traffic during it.</summary>
    public int Gaps { get; set; }
    /// <summary>A run's boss pulls, in order (a live run's copy ends with the pull under way).</summary>
    public List<Split> Splits { get; set; } = new();
    /// <summary>Your own kills (players among them) and deaths, from the game's kill events: runs and fights.</summary>
    public int YourKills { get; set; }
    public int PlayerKills { get; set; }
    public int YourDeaths { get; set; }
    /// <summary>A fight's boss pull, how it ended ("kill", "wipe", "left"): the fight ends as it did.</summary>
    public string BossResult { get; set; } = "";
    /// <summary>The last player you killed (a fight's name when no boss was pulled).</summary>
    public string Opponent { get; set; } = "";

    /// <summary>
    /// What the record is called: a pull its boss, a run its instance, a fight its boss, else the player you fought ("vs
    /// Kaelith"), else "PvP fight" or "Fight".
    /// </summary>
    [JsonIgnore] public string Title => ModeKind switch
    {
        ParserMode.Run => BossBook.Strip(Instance).Length > 0 ? BossBook.Strip(Instance) : "Run",
        ParserMode.Fights => Boss.Length > 0 ? Boss : Opponent.Length > 0 ? "vs " + Opponent : PlayerKills > 0 ? "PvP fight" : "Fight",
        _ => Boss,
    };

    [JsonIgnore] public bool Live => End == null;

    /// <summary>
    /// A fight outside Transcendence, Expeditions and Sanctuaries (the open world, the Abyss). Steven, 2026-11-14: "you can
    /// have open world count for the parser inside of the app but it shouldn't log it anywhere ever": counted live, never
    /// kept (no record, no file, no log line with its figures), gone when it ends.
    /// </summary>
    [JsonIgnore] public bool OpenWorld => ModeKind == ParserMode.Fights && KindOf(Kind) == ContentKind.Other;
    [JsonIgnore] public BossOutcome? Result => Outcome switch { "kill" => BossOutcome.Kill, "wipe" => BossOutcome.Wipe, "left" => BossOutcome.Left, _ => null };

    /// <summary>How long the fight took (to now while it is under way, never less than 0).</summary>
    public TimeSpan Duration(DateTime now) { var d = (End ?? now) - Start; return d < TimeSpan.Zero ? TimeSpan.Zero : d; }

    /// <summary>The player's own row, if the group was known.</summary>
    [JsonIgnore] public Fighter? You => Roster.FirstOrDefault(f => f.You);

    /// <summary>The group's members (an older record's Other row is folded into <see cref="Uncredited"/> as it is read).</summary>
    [JsonIgnore] public IEnumerable<Fighter> Members => Roster.Where(f => !f.Other);

    /// <summary>
    /// A record kept before the honest pass had an "Other" row for the damage no hit named: read, it becomes the record's
    /// uncredited damage (to the boss, as far as the boss's damage goes) and the row is gone.
    /// </summary>
    void IJsonOnDeserialized.OnDeserialized()
    {
        var others = Roster.Where(f => f.Other).ToList();
        if (others.Count == 0) return;
        long sum = others.Sum(o => o.Damage);
        foreach (var o in others) Roster.Remove(o);
        Uncredited += sum;
        UncreditedBoss = Math.Min(BossDamage, UncreditedBoss + sum);
    }

    /// <summary>
    /// Member figures are boss damage (the group card's Boss / All targets, parser.json Targets): on (the default), a boss
    /// pull's and a run's figures, DPS and shares count damage to the boss only; off, to every target, adds too. Set by the window.
    /// </summary>
    public static bool BossOnly { get; set; } = true;

    /// <summary>This record's figures are boss damage: <see cref="BossOnly"/>, a pull or a run, kept apart (<see cref="TargetsSplit"/>).</summary>
    [JsonIgnore] public bool BossView => BossOnly && TargetsSplit && ModeKind != ParserMode.Fights && !Wave;

    /// <summary>A member's damage as the figures count it: to the boss in <see cref="BossView"/>, else to every target.</summary>
    public long Figure(Fighter f) => BossView ? f.BossDamage : f.Damage;
    public long Figure(AbilityShare a) => BossView ? a.BossDamage : a.Damage;
    /// <summary>An ability's counted hits and average a hit as the figures count them (null: no hit counted).</summary>
    public int Hits(AbilityShare a) => BossView ? a.BossHits : a.Hits;
    public double? Average(AbilityShare a) => BossView ? a.BossAverage : a.Average;

    /// <summary>
    /// A member's abilities as the inspect view lists them: the ids of one ability as one row (Abilities.Canon: a spirit's
    /// attack at each rank, a skill's damage over time by any of its effects), every figure summed, biggest first as the
    /// figures count them; abilities with nothing in this view left out.
    /// </summary>
    public List<AbilityShare> AbilityRows(Fighter f) => f.Abilities
        .GroupBy(a => Abilities.Canon(a.Skill))
        .Select(g => new AbilityShare
        {
            Skill = g.Key, Damage = g.Sum(a => a.Damage), Hits = g.Sum(a => a.Hits), Uncounted = g.Sum(a => a.Uncounted),
            BossDamage = g.Sum(a => a.BossDamage), BossHits = g.Sum(a => a.BossHits), BossUncounted = g.Sum(a => a.BossUncounted),
            // store v2 (null when no id of the ability carries it)
            Casts = g.Any(a => a.Casts != null) ? g.Sum(a => a.Casts ?? 0) : null, StreamHits = g.Any(a => a.StreamHits != null) ? g.Sum(a => a.StreamHits ?? 0) : null,
            Max = g.Max(a => a.Max), Min = g.Min(a => a.Min),
            StreamDamage = g.Any(a => a.StreamDamage != null) ? g.Sum(a => a.StreamDamage ?? 0) : null,
            Kinds = Merge(g.Select(a => a.Kinds), (x, y) => x + y), KindDamage = Merge(g.Select(a => a.KindDamage), (x, y) => x + y), KindMax = Merge(g.Select(a => a.KindMax), Math.Max),
            Picks = g.Aggregate(0, (p, a) => p | a.Picks),
        })
        .Where(a => Figure(a) > 0).OrderByDescending(Figure).ToList();

    static Dictionary<string, T>? Merge<T>(IEnumerable<Dictionary<string, T>?> parts, Func<T, T, T> add) where T : struct
    {
        Dictionary<string, T>? all = null;
        foreach (var d in parts)
            if (d != null)
                foreach (var (k, v) in d) { all ??= new(); all[k] = all.TryGetValue(k, out var had) ? add(had, v) : v; }
        return all;
    }

    /// <summary>
    /// The hit kind shown as crit (ParserPrefs.CritSource, set by the window): null, the default, shows none; "result:3" or
    /// "mark:3" picks one of the two candidates the hits carry, once the game's own figures have settled which is crit.
    /// </summary>
    public static string? CritKey { get; set; }

    /// <summary>The kind key for a CritSource value ("result3", "mark08"); null for "none" or anything else.</summary>
    public static string? CritKeyOf(string? source) => source switch { "result3" => "result:3", "mark08" => "mark:3", _ => null };

    /// <summary>The hit kind shown as a back attack (ParserPrefs.BackSource, set by the window): null, the default, shows none; "side:1" the hits from behind.</summary>
    public static string? BackKey { get; set; }
    /// <summary>The kind key for a BackSource value ("side1"); null for "none" or anything else.</summary>
    public static string? BackKeyOf(string? source) => source == "side1" ? "side:1" : null;

    /// <summary>A kind's hits, damage and biggest hit in an ability (all 0 when the ability carries no kinds).</summary>
    public static (int Hits, long Damage, long Max) KindTotals(AbilityShare a, string key) =>
        (a.Kinds?.GetValueOrDefault(key) ?? 0, a.KindDamage?.GetValueOrDefault(key) ?? 0, a.KindMax?.GetValueOrDefault(key) ?? 0);

    /// <summary>Damage to adds (anything but the boss) can be told apart: the Boss / All targets switch has something to switch.</summary>
    [JsonIgnore] public bool HasAdds => TargetsSplit && ModeKind != ParserMode.Fights && !Wave && Members.Any(f => f.Damage > f.BossDamage);

    /// <summary>The group's credited damage as the figures count it: the sum of the rows, what group DPS and shares are of.</summary>
    [JsonIgnore] public long Credited => Members.Sum(Figure);

    /// <summary>
    /// The share of the HP the boss lost that is credited to a member, 0 to 1 (null when the boss's damage isn't known): the
    /// group card's "99.8% of the boss's HP credited".
    /// </summary>
    [JsonIgnore] public double? Coverage => HasDamage && BossDamage > 0 ? Math.Clamp((BossDamage - UncreditedBoss) / (double)BossDamage, 0, 1) : null;

    /// <summary>
    /// The seconds DPS is over: a run's boss pulls when its figures are the bosses' damage (and an older run's, which counted
    /// that way), else the whole record.
    /// </summary>
    // Damage per second is over the real time since the start (Steven, 2026-11-15, on 0.10.106's combat time, which held the
    // figure while he walked away: "i left and didnt kill anything for a while and it ... didnt drop the dps ... at all"):
    // fighting raises it, every second without a hit lowers it, and the meter shows that as it happens
    public double DpsSeconds(DateTime now) => ModeKind == ParserMode.Run && Splits.Count > 0 && (BossView || !TargetsSplit) ? Splits.Sum(x => x.Seconds) : Duration(now).TotalSeconds;

    /// <summary>
    /// A DPS is over at least this long: the meter shows a figure from the first hit, never a hit's damage over a split second
    /// (Steven, 2026-11-15: "the dps meter takes like 2-3 seconds after first hit to start working ... that's too long"; it
    /// showed "–" for the first 3 s).
    /// </summary>
    public const double MinDpsSeconds = 1;

    /// <summary>
    /// DPS over each member's active time (Settings › In game › Parser › DPS over active time, parser.json DpsActive):
    /// off (the default), every member's DPS is over the fight's length (a run's: its boss pulls); on, over the time they
    /// were dealing damage, first hit to last with pauses over <see cref="Fighter.PauseSeconds"/> s left out. Set by the window.
    /// </summary>
    public static bool ActiveBasis { get; set; }

    /// <summary>The seconds a member's DPS is over, by <see cref="ActiveBasis"/>.</summary>
    public double DpsSeconds(Fighter f, DateTime now) => ActiveBasis && !f.Other && f.Damage > 0 ? f.ActiveSeconds : DpsSeconds(now);

    /// <summary>
    /// A member's damage per second (their <see cref="Figure(Fighter)"/> over the fight, a run's over its boss pulls, or over
    /// their active time, never under <see cref="MinDpsSeconds"/>); null when damage isn't known.
    /// </summary>
    public double? DpsOf(Fighter f, DateTime now)
    {
        if (!HasDamage) return null;
        return Figure(f) / Math.Max(DpsSeconds(f, now), MinDpsSeconds);
    }

    /// <summary><see cref="DpsOf"/>, 0 when it isn't a figure.</summary>
    public double Dps(Fighter f, DateTime now) => DpsOf(f, now) ?? 0;

    /// <summary>A member's DPS as the rows say it ("3,851", "12.4K"); "–" when it isn't a figure yet.</summary>
    public string DpsText(Fighter f, DateTime now) => DpsOf(f, now) is { } d ? Ui.Amounts.Short(d) : "–";

    /// <summary>
    /// The seconds a member was in the group during the record: from the start (or their joining, when they joined late) to
    /// the end (or their leaving).
    /// </summary>
    public double PresentSeconds(Fighter f, DateTime now)
    {
        var from = f.Late && f.JoinedAt is { } j && j > Start ? j : Start;
        var to = End ?? now;
        if (f.Gone && f.LeftAt is { } l && l < to) to = l;
        return Math.Max(0, (to - from).TotalSeconds);
    }

    /// <summary>The part of a member's time in the group in which they dealt no damage, 0 to 1; null before a hit or under <see cref="MinDpsSeconds"/>.</summary>
    public double? IdleShare(Fighter f, DateTime now)
    {
        double present = PresentSeconds(f, now);
        if (f.FirstHit == null || present < MinDpsSeconds) return null;
        return Math.Clamp(1 - f.ActiveSeconds / present, 0, 1);
    }

    /// <summary>
    /// Your DPS while farming (the Parser page's figure): your damage over your fighting time (Fighter.FightingSeconds),
    /// so the walk between monsters doesn't pull it down; 0 before a hit of yours.
    /// </summary>
    public double FarmDps() => You is { Damage: > 0 } me ? me.Damage / Math.Max(FarmMinSeconds, me.FightingSeconds) : 0;
    /// <summary>The Kinah line's farming figure is never over less than this: a mob killed in a second isn't 200K a second there.</summary>
    public const double FarmMinSeconds = 3;   // never over less than 3 s: a mob killed in a second isn't 200K a second

    /// <summary>
    /// A member's share of the group's damage, 0 to 1 (0 when damage isn't known). Over the members only (Steven, 2026-11-14:
    /// "there should be no 'other' in the parser"): damage no hit named (damage over time) is in no row, so the rows add up to 100%.
    /// </summary>
    public double Share(Fighter f) { long all = Credited; return HasDamage && all > 0 && !f.Other ? Figure(f) / (double)all : 0; }

    /// <summary>A share as the rows say it: "25.1%", "100%".</summary>
    public static string Pct(double share) => share >= 0.9995 ? "100%" : (share * 100).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "%";

    /// <summary>The sub line under a record's title: its instance (when the title isn't it), the game's mode for it, its kind.</summary>
    [JsonIgnore] public string Where
    {
        get
        {
            var bits = new List<string>();
            var i = BossBook.Strip(Instance);
            if (i.Length > 0 && i != Title && i != Kind) bits.Add(i);
            if (GameMode.Length > 0) bits.Add(GameMode);
            if (Kind.Length > 0) bits.Add(Kind);
            return string.Join(" · ", bits);
        }
    }

    /// <summary>"Ferocious Horn Den · Conquest": the instance with the game's mode for it, when known.</summary>
    [JsonIgnore] public string Place { get { var i = BossBook.Strip(Instance); return GameMode.Length == 0 ? i : i.Length == 0 ? GameMode : $"{i} · {GameMode}"; } }

    /// <summary>The group's damage per second: the credited damage (the rows' sum, as the figures count it) over the fight; null under <see cref="MinDpsSeconds"/>.</summary>
    public double? GroupDpsOf(DateTime now) => HasDamage ? Credited / Math.Max(DpsSeconds(now), MinDpsSeconds) : null;
    public double GroupDps(DateTime now) => GroupDpsOf(now) ?? 0;
    public string GroupDpsText(DateTime now) => GroupDpsOf(now) is { } d ? Ui.Amounts.Short(d) : "–";

    // ---- the meter's foot and its marks (Steven, 2026-11-15: "have the bottom be total AND group dps avg ... a green badge
    // next to it that says 'Avg. DPS' ... a gold plate [TOP DPS]") ----

    /// <summary>
    /// The meter holds a finished fight (an open-world fight held after 5 s without fighting, a pull's kill or wipe): its
    /// figures are the whole fight's average, not live, and the foot's group DPS carries the "Avg. DPS" badge.
    /// </summary>
    [JsonIgnore] public bool HeldAverage => !Live && HasDamage && !Waiting;

    /// <summary>"Total 10.5M · Group DPS 126.8K" (live) or "Total 10.5M · Group 126.8K · Avg. DPS" (held): the foot's words, as every look draws them.</summary>
    public string FootText(DateTime now) => !HasDamage ? "" : HeldAverage
        ? $"Total {Ui.Amounts.Short(Credited)} · Group {GroupDpsText(End ?? now)} · {AvgBadge}"
        : $"Total {Ui.Amounts.Short(Credited)} · Group DPS {GroupDpsText(now)}";

    /// <summary>The held figure's badge.</summary>
    public const string AvgBadge = "Avg. DPS";

    /// <summary>
    /// The end of a boss fight in a dungeon (a pull's kill or wipe, a run cleared, in an Expedition, Transcendence or Sanctuary),
    /// with two or more members who dealt damage: the meter puts TOP DPS (and TOP HEALS when heals are known) on their rows.
    /// </summary>
    [JsonIgnore] public bool TopsShown => !Live && HasDamage && !Waiting && Outcome is "kill" or "wipe" or "cleared"
        && (Boss.Length > 0 || ModeKind == ParserMode.Run) && KindOf(Kind) is ContentKind.Transcendence or ContentKind.Expedition or ContentKind.Sanctuary
        && Members.Count(f => Figure(f) > 0) >= 2;

    /// <summary>The member with the fight's highest DPS (the one TOP DPS marks), or null when <see cref="TopsShown"/> is off.</summary>
    public Fighter? TopDps(DateTime now) => !TopsShown ? null : Members.Where(f => Figure(f) > 0).OrderByDescending(f => Dps(f, End ?? now)).ThenByDescending(Figure).FirstOrDefault();

    /// <summary>
    /// The fight's top healer (TOP HEALS), or null: only when heal figures are known for this fight, and only someone who healed
    /// another member (a damage dealer's own self-heals and potions never make them the top healer). Ranked by healing done,
    /// which is exact; the effective part is an estimate (<see cref="Fighter.HealingEstimated"/>).
    /// </summary>
    public Fighter? TopHealer(DateTime now) => !TopsShown ? null : Members.Where(f => f.HealingOthers > 0).OrderByDescending(f => f.Healing).ThenByDescending(f => f.HealingOthers).FirstOrDefault();

    // ---- healing (klnet's heals on the hit stream) ----

    /// <summary>Some member's healing is known in this record (a decoder that reads heals, and a heal came).</summary>
    [JsonIgnore] public bool HasHealing => Members.Any(f => f.Healing > 0);

    /// <summary>
    /// A member's healing per second: their healing done (self-heals and potions in) over the same seconds the group's DPS is
    /// over (the fight's real time, a run's boss pulls; never under <see cref="MinDpsSeconds"/>); null when they healed nothing.
    /// </summary>
    public double? HpsOf(Fighter f, DateTime now) => f.Healing > 0 ? f.Healing / Math.Max(DpsSeconds(now), MinDpsSeconds) : null;

    /// <summary>The effective part per second (overheal left out): an estimate when <see cref="Fighter.HealingEstimated"/> says so.</summary>
    public double? EffectiveHpsOf(Fighter f, DateTime now) => f.Healing > 0 ? f.HealingEffective / Math.Max(DpsSeconds(now), MinDpsSeconds) : null;

    /// <summary>The group's healing done in the record.</summary>
    [JsonIgnore] public long GroupHealing => Members.Sum(f => f.Healing);

    /// <summary>What a member's meter bar measures: their figure when damage is known, else Combat Power.</summary>
    public double Measure(Fighter f) => HasDamage ? Figure(f) : f.CombatPower;

    /// <summary>The meter's top: the biggest member's figure when known, else Combat Power (at least 1).</summary>
    public double TopMeasure() => Math.Max(1, Members.Select(Measure).DefaultIfEmpty(1).Max());

    /// <summary>
    /// Not a record: the card the overlay shows in a dungeon before its first pull (Steven, 2026-11-14: "turn into the parser
    /// when you're in a dungeon ... persist throughout the dungeon"). Never kept.
    /// </summary>
    [JsonIgnore] public bool Waiting { get; set; }

    /// <summary>The dungeon's card before its first pull: the instance's name, no clock, no rows.</summary>
    public static Encounter Ready(ContentKind kind, string instance, string gameMode, DateTime now) => new()
    {
        Id = "ready", Mode = "boss", Kind = KindName(kind), Instance = instance, GameMode = gameMode,
        Boss = BossBook.Strip(instance).Length > 0 ? BossBook.Strip(instance) : KindName(kind),
        Start = now, End = now, LastAt = now, Waiting = true,
    };

    /// <summary>The Always on meter before its first hit outside a dungeon: the place's name, no clock, no rows.</summary>
    public static Encounter ReadyAnywhere(string place, DateTime now) => new()
    {
        Id = "ready", Mode = "fight", Kind = "", Instance = place, Boss = place,
        Start = now, End = now, LastAt = now, Waiting = true, Always = true,
    };

    /// <summary>The Always on meter's own tally (never a record of the mode): its sub line says "Always on", not the mode's name.</summary>
    [JsonIgnore] public bool Always { get; set; }

    /// <summary>
    /// The record's last big hits (a member's hit of <see cref="BigHitTimes"/> their average hit or more, from the hit stream),
    /// newest last, at most <see cref="BigHitsKept"/>: the meter's pop-ups. Never kept in the fight store; the live copy carries
    /// them (BossParser.Copy). Null until the stream gives one.
    /// </summary>
    [JsonIgnore] public List<BigHit>? BigHits { get; set; }
    public const int BigHitsKept = 8;
    public const double BigHitTimes = 3;
    /// <summary>A member's hits counted before their average is a measure for a big hit.</summary>
    public const int BigHitAfter = 8;

    /// <summary>What a ready card says under its name: a dungeon waits for its first pull, the meter anywhere else for a hit.</summary>
    [JsonIgnore] public string ReadyText => ModeKind == ParserMode.Fights ? "Waiting for the first hit" : "Waiting for the first pull";

    /// <summary>
    /// The rows the live card, the overlay and the list show (Steven, 2026-11-14: "don't count [a member] until they have at
    /// least 1 [damage]"): with damage counted, only members who have dealt some; before any damage, everyone. The detail's
    /// roster lists every member. Never an "Other" row (Steven, 2026-11-14).
    /// </summary>
    public IEnumerable<Fighter> Shown() => HasDamage ? Ranked().Where(f => f.Damage > 0) : Ranked();

    /// <summary>The members by damage when known, else you first and then by Combat Power.</summary>
    public IEnumerable<Fighter> Ranked() => HasDamage
        ? Members.OrderByDescending(Figure).ThenByDescending(f => f.Damage).ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
        : Members.OrderByDescending(f => f.You).ThenByDescending(f => f.CombatPower).ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase);

    public static string KindName(ContentKind k) => k switch
    {
        ContentKind.Transcendence => "Transcendence", ContentKind.Expedition => "Expedition", ContentKind.Sanctuary => "Sanctuary", _ => "",
    };

    public static ContentKind KindOf(string? name) => name?.Trim().ToLowerInvariant() switch
    {
        "transcendence" => ContentKind.Transcendence, "expedition" => ContentKind.Expedition, "sanctuary" => ContentKind.Sanctuary, _ => ContentKind.Other,
    };

    public static string OutcomeName(BossOutcome o) => o switch { BossOutcome.Kill => "kill", BossOutcome.Wipe => "wipe", _ => "left" };

    /// <summary>"Killed", "Wiped", "Left", "In progress": the outcome as the page says it.</summary>
    [JsonIgnore] public string OutcomeLabel => Outcome switch
    {
        "kill" when ModeKind == ParserMode.Fights && Boss.Length == 0 => YourKills == 1 ? "1 kill" : YourKills > 1 ? $"{YourKills} kills" : "Killed",
        "kill" => "Killed", "wipe" => "Wiped", "left" => "Left", "cleared" => "Cleared", "died" => "Died", "ended" => "Ended", _ => "In progress",
    };

    /// <summary>The outcome's tone: "good" (a kill, cleared), "bad" (a wipe, a death), "quiet" (left, ended), "live".</summary>
    [JsonIgnore] public string Tone => Outcome switch { "kill" or "cleared" => "good", "wipe" or "died" => "bad", "" => "live", _ => "quiet" };
}

/// <summary>
/// A member's big hit (the meter's pop-up): its order in the stream (Seq, the same for one hit in every record), whose it
/// counts for, how much, and whether it was a crit (null: the record carries no crit data, so it is never called one).
/// </summary>
public sealed record BigHit(long Seq, long Member, long Amount, bool? Crit, DateTime At);

/// <summary>One member of the group in a fight.</summary>
public sealed class Fighter
{
    /// <summary>The member's hits from the stream so far, and their damage: the big hit's measure (never kept or copied).</summary>
    [JsonIgnore] internal int StreamHitCount;
    [JsonIgnore] internal long StreamHitSum;

    public long Id { get; set; }
    public string Name { get; set; } = "";
    /// <summary>The class as the game names it ("Gladiator"), "" when unknown.</summary>
    public string Class { get; set; } = "";
    /// <summary>Combat Power; 0 = not known.</summary>
    public int CombatPower { get; set; }
    /// <summary>Item level; 0 = not known.</summary>
    public int ItemLevel { get; set; }
    public bool You { get; set; }
    /// <summary>The member's server ("Israphel"), "" when not known: the expanded row and hover only.</summary>
    public string Server { get; set; } = "";
    /// <summary>Joined after the pull (whether or not damage had been dealt by then), and when.</summary>
    public bool Late { get; set; }
    public DateTime? JoinedAt { get; set; }
    /// <summary>Left the group during the fight, and when.</summary>
    public bool Gone { get; set; }
    public DateTime? LeftAt { get; set; }
    /// <summary>An older record's "Other" row (HP lost with no hit to name a source): read only, folded into Encounter.Uncredited.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool Other { get; set; }
    /// <summary>Known only from its damage (no group list yet): Combat Power and item level not known.</summary>
    public bool FromDamage { get; set; }
    /// <summary>Damage to every target, and to the boss alone (Encounter.TargetsSplit; a run's bosses, a wave's everything).</summary>
    public long Damage { get; set; }
    public long BossDamage { get; set; }
    /// <summary>
    /// Healing done in the fight, in HP as dealt: on others, on themselves and potions (0 = none or not known: nothing is shown).
    /// Exact: the heals as the game dealt them. Shields are never in it.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public long Healing { get; set; }
    /// <summary>
    /// The part of <see cref="Healing"/> that fit under the target's max HP (overheal left out). Each heal's part is cut at the
    /// target's HP as last seen; where the target's HP confirmed it, it is exact, else an estimate (<see cref="HealingEstimated"/>).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public long HealingEffective { get; set; }
    /// <summary>The part of <see cref="HealingEffective"/> no HP change confirmed: above 0, the effective figure is an estimate.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public long HealingEstimated { get; set; }
    /// <summary>Healing the member did on themselves (their own skills and potions), within <see cref="Healing"/>.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public long HealingSelf { get; set; }
    /// <summary>Of that, potions.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public long HealingPotions { get; set; }
    /// <summary>Healing done on other members.</summary>
    [JsonIgnore] public long HealingOthers => Healing - HealingSelf;
    /// <summary>The effective healing is an estimate (some of it wasn't confirmed by the target's HP).</summary>
    [JsonIgnore] public bool HealingIsEstimate => HealingEstimated > 0;
    /// <summary>Healing by ability (a heal over time's ticks under their negative effect id, as damage over time is): a member's breakdown.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<HealShare>? Heals { get; set; }
    public int Deaths { get; set; }
    /// <summary>Damage by ability (the game's skill id; 0: damage over time no skill named), with its hits: a member's breakdown.</summary>
    public List<AbilityShare> Abilities { get; set; } = new();

    // ---- store v2 (the hit stream): each null in a record kept before, or until its data comes ----
    /// <summary>Damage to every target, and to the boss, in each second from Encounter.Start (index = second).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<long>? PerSecond { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<long>? BossPerSecond { get; set; }
    /// <summary>Damage the member took, by skill and attacker.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<TakenShare>? Taken { get; set; }
    /// <summary>Each death: when, the boss's HP then, the killer and the killing skill, the last hits taken.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<DeathRecord>? DeathLog { get; set; }
    /// <summary>Effects on the member: how long each was on during the record.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<BuffUptime>? Buffs { get; set; }

    /// <summary>Adds damage at its second to a per-second list (made on first use); the list.</summary>
    internal static List<long> AddAt(List<long>? list, int second, long amount)
    {
        list ??= new();
        while (list.Count <= second) list.Add(0);
        list[second] += amount;
        return list;
    }

    /// <summary>The member's row for a skill, made (with no damage yet) when the hit stream names it first.</summary>
    internal AbilityShare Ability(int skill)
    {
        var a = Abilities.FirstOrDefault(x => x.Skill == skill);
        if (a == null) Abilities.Add(a = new AbilityShare { Skill = skill });
        return a;
    }

    /// <summary>A pause in a member's damage longer than this (seconds) is left out of their active time.</summary>
    public const double PauseSeconds = 20;
    /// <summary>The member's first and last hit in this record; null before any.</summary>
    public DateTime? FirstHit { get; set; }
    public DateTime? LastHit { get; set; }
    /// <summary>Seconds the member was dealing damage: first hit to last, pauses over <see cref="PauseSeconds"/> left out.</summary>
    public double ActiveSeconds { get; set; }

    /// <summary>A hit of the member's at <paramref name="at"/>: their active time grows by the time since their last, unless that was a pause.</summary>
    public void Hit(DateTime at)
    {
        FirstHit ??= at;
        if (LastHit is { } last && at > last)
        {
            double gap = (at - last).TotalSeconds;
            if (gap <= PauseSeconds) ActiveSeconds += gap;
            // fighting time: a gap over StretchGapSeconds (walking to the next monster) closes the stretch of hits
            if (gap > StretchGapSeconds) { StretchesSeconds += Math.Max(1, (last - (StretchStart ?? last)).TotalSeconds); StretchStart = at; }
        }
        StretchStart ??= at;
        if (LastHit == null || at > LastHit) LastHit = at;
    }

    /// <summary>A gap in a member's hits longer than this (seconds) is time between fights, not fighting: the farming DPS leaves it out.</summary>
    public const double StretchGapSeconds = 5;
    /// <summary>The fighting time of the stretches of hits already closed, and the start of the one under way.</summary>
    public double StretchesSeconds { get; set; }
    public DateTime? StretchStart { get; set; }

    /// <summary>
    /// Seconds spent fighting: stretches of hits with gaps of at most <see cref="StretchGapSeconds"/>, each at least 1 s, the
    /// one under way included. The farming DPS is over this, so walking between monsters doesn't lower it.
    /// </summary>
    public double FightingSeconds => StretchesSeconds + (StretchStart is { } s && LastHit is { } l ? Math.Max(1, (l - s).TotalSeconds) : 0);

    /// <summary>
    /// A share of damage by one ability (a damage over time is its own, negative skill): its damage, and its hits when they
    /// were counted. A share with no count adds no made-up hit, and its damage is left out of the average a hit.
    /// </summary>
    public void AddAbility(int skill, long amount, int hits, bool boss = false)
    {
        var a = Abilities.FirstOrDefault(x => x.Skill == skill);
        if (a == null) Abilities.Add(a = new AbilityShare { Skill = skill });
        a.Damage += amount;
        if (hits > 0) a.Hits += hits; else a.Uncounted += amount;
        if (!boss) return;
        a.BossDamage += amount;
        if (hits > 0) a.BossHits += hits; else a.BossUncounted += amount;
    }

    /// <summary>The game's number for the class, when it came as one (0 = not); the class itself once it is paired (Parser/ClassNumbers.cs).</summary>
    public int ClassNumber { get; set; }

    public static Fighter From(GroupMember m)
    {
        var f = new Fighter { Id = m.Id, Name = m.Name.Trim(), CombatPower = Math.Max(0, m.CombatPower), ItemLevel = m.ItemLevel > 0 ? m.ItemLevel : 0, You = m.You, Server = m.Server?.Trim() ?? "" };
        f.SetClass(m.Class);
        return f;
    }

    /// <summary>A class as it came: a name, or the game's number (named when paired).</summary>
    public void SetClass(string cls)
    {
        var c = cls.Trim();
        if (c.Length == 0) return;
        if (int.TryParse(c, out var n)) { ClassNumber = n; if (ClassNumbers.Name(n) is { } known) Class = known; }
        else Class = c;
    }
}

/// <summary>
/// One ability's share of a member's damage: the skill (Parser/Abilities.cs names it: 0 damage over time no skill named,
/// negative a skill's damage over time), its damage and counted hits, to every target and to the boss alone.
/// </summary>
public sealed class AbilityShare
{
    public int Skill { get; set; }
    public long Damage { get; set; }
    /// <summary>Hits counted (0: none came with a count); Uncounted is the damage that came with no count.</summary>
    public int Hits { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public long Uncounted { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public long BossDamage { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public int BossHits { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public long BossUncounted { get; set; }
    /// <summary>The average a counted hit (damage that came with no count left out); null with no hit counted, shown "–".</summary>
    [JsonIgnore] public double? Average => Hits > 0 ? Math.Max(0, Damage - Uncounted) / (double)Hits : null;
    [JsonIgnore] public double? BossAverage => BossHits > 0 ? Math.Max(0, BossDamage - BossUncounted) / (double)BossHits : null;

    // ---- store v2 (the hit stream): null in a record kept before, or until its data comes ----
    /// <summary>Casts of the skill.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? Casts { get; set; }
    /// <summary>Hits seen one by one (the denominator for Max, Min and Kinds), and the biggest and smallest of them.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? StreamHits { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public long? Max { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public long? Min { get; set; }
    /// <summary>The hits' kinds as they came, counted by an internal key, unnamed until mapped (never shown before then).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public Dictionary<string, int>? Kinds { get; set; }
    /// <summary>The damage of the hits seen one by one, and by kind with each kind's biggest hit (store v2, since the crit candidates): crit share and max crit.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public long? StreamDamage { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public Dictionary<string, long>? KindDamage { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public Dictionary<string, long>? KindMax { get; set; }
    /// <summary>The specialties the skill's hits named (Abilities.PicksOf, bits 1 to 5): a pick that changes none of its hits isn't seen.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public int Picks { get; set; }
}

/// <summary>Healing by one ability: healing done (HP as dealt), its effective part, the heals (ticks for a heal over time), and how many were on the healer.</summary>
public sealed class HealShare
{
    public int Skill { get; set; }
    public long Healing { get; set; }
    public long Effective { get; set; }
    public int Hits { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public long Self { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool Potion { get; set; }
}

/// <summary>Damage a member took from one attacker's skill: Source is the boss's name when the boss dealt it, else "".</summary>
public sealed class TakenShare
{
    public int Skill { get; set; }
    public string Source { get; set; } = "";
    public long SourceId { get; set; }
    public long Damage { get; set; }
    public int Hits { get; set; }
}

/// <summary>One death of a member's: seconds from the record's start, the boss's HP left then (0 to 1), the killer and killing skill, the last hits taken (up to 5, the killing blow last).</summary>
public sealed class DeathRecord
{
    public double At { get; set; }
    public double? BossHpLeft { get; set; }
    public int KillerSkill { get; set; }
    public string Killer { get; set; } = "";
    public long KillerId { get; set; }
    public List<TakenHit> Last { get; set; } = new();
}

/// <summary>A hit taken, in a death's recap: seconds from the record's start, the skill, the attacker, the damage; HP after it when known.</summary>
public sealed class TakenHit
{
    public double At { get; set; }
    public int Skill { get; set; }
    public string Source { get; set; } = "";
    public long Amount { get; set; }
    public double? HpAfter { get; set; }
}

/// <summary>An effect on a member: its id (the skill's id x 100 + n), the skill to name it by (Abilities.Name), seconds on during the record and its share (0 to 1) of the member's time in the group.</summary>
public sealed class BuffUptime
{
    public int Id { get; set; }
    public int Skill { get; set; }
    public double Seconds { get; set; }
    public double Share { get; set; }
}

/// <summary>An enemy that died near the player during a record, or one the player killed (Yours; a player when Player).</summary>
public sealed class KillNear
{
    public DateTime At { get; set; }
    public string Name { get; set; } = "";
    public bool Boss { get; set; }
    public bool Player { get; set; }
    public bool Yours { get; set; }
}

/// <summary>One boss pull inside a run: when, how long, how it ended, the group's DPS.</summary>
public sealed class Split
{
    public string Boss { get; set; } = "";
    public DateTime Start { get; set; }
    public double Seconds { get; set; }
    /// <summary>"kill", "wipe", "left"; "" for the pull under way.</summary>
    public string Outcome { get; set; } = "";
    public bool Final { get; set; }
    /// <summary>A wave stage (the Totem room), not a boss.</summary>
    public bool Wave { get; set; }
    public double Dps { get; set; }
    public bool Partial { get; set; }
    /// <summary>The boss's HP left (0 to 1) when known, for the pull under way.</summary>
    public double? HpLeft { get; set; }

    public static Split From(Encounter pull, DateTime now, bool live) => new()
    {
        Boss = pull.Boss, Start = pull.Start, Seconds = Math.Round(pull.Duration(now).TotalSeconds, 1), Outcome = live ? "" : pull.Outcome,
        Final = pull.FinalBoss, Wave = pull.Wave, Dps = Math.Round(pull.GroupDps(now)), Partial = pull.Partial, HpLeft = pull.BossHpLeft,
    };
}
