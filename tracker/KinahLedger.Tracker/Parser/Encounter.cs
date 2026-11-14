using System.Text.Json.Serialization;
using KinahLedger.Packets;

namespace KinahLedger.Parser;

/// <summary>
/// One record of the Parser (Parser/BossParser.cs decides), by its <see cref="Mode"/>: a boss pull ("boss": the pull to
/// the boss's death, a wipe or leaving, in a Transcendence, an Expedition or a Sanctuary), an instance run ("run":
/// entering to leaving, each boss a <see cref="Split"/>), or a fight anywhere ("fight": the first hit or kill to a pause).
/// Kept on this PC in the character's encounters.jsonl (Parser/EncounterStore.cs); never uploaded.
/// </summary>
public sealed class Encounter
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
    /// <summary>Damage dealt to the boss itself, by everyone, when known.</summary>
    public long BossDamage { get; set; }
    /// <summary>The boss's HP now (at the end, for a kept fight) and its max, when the game gave them; 0 = not known.</summary>
    public long BossHp { get; set; }
    public long BossMaxHp { get; set; }
    /// <summary>The share of the boss's HP left, 0 to 1, or null when not known.</summary>
    [JsonIgnore] public double? BossHpLeft => BossMaxHp > 0 ? Math.Clamp(BossHp / (double)BossMaxHp, 0, 1) : null;
    /// <summary>The tracker version that kept it.</summary>
    public string App { get; set; } = "";
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
    [JsonIgnore] public BossOutcome? Result => Outcome switch { "kill" => BossOutcome.Kill, "wipe" => BossOutcome.Wipe, "left" => BossOutcome.Left, _ => null };

    /// <summary>How long the fight took (to now while it is under way, never less than 0).</summary>
    public TimeSpan Duration(DateTime now) { var d = (End ?? now) - Start; return d < TimeSpan.Zero ? TimeSpan.Zero : d; }

    /// <summary>The player's own row, if the group was known.</summary>
    [JsonIgnore] public Fighter? You => Roster.FirstOrDefault(f => f.You);

    /// <summary>The group's members: every row but Other (HP lost with no hit: damage over time, lost traffic).</summary>
    [JsonIgnore] public IEnumerable<Fighter> Members => Roster.Where(f => !f.Other);

    /// <summary>The seconds DPS is over: a run's boss pulls (its damage is the bosses'), else the whole record.</summary>
    public double DpsSeconds(DateTime now) => ModeKind == ParserMode.Run && Splits.Count > 0 ? Splits.Sum(x => x.Seconds) : Duration(now).TotalSeconds;

    /// <summary>
    /// DPS over each member's active time (Settings › In game › Parser › DPS over active time, parser.json DpsActive):
    /// off (the default), every member's DPS is over the fight's length (a run's: its boss pulls); on, over the time they
    /// were dealing damage, first hit to last with pauses over <see cref="Fighter.PauseSeconds"/> s left out. Set by the window.
    /// </summary>
    public static bool ActiveBasis { get; set; }

    /// <summary>The seconds a member's DPS is over, by <see cref="ActiveBasis"/>.</summary>
    public double DpsSeconds(Fighter f, DateTime now) => ActiveBasis && !f.Other && f.Damage > 0 ? Math.Max(1, f.ActiveSeconds) : DpsSeconds(now);

    /// <summary>A member's damage per second (over the fight, a run's over its boss pulls, or over their active time); 0 when damage isn't known.</summary>
    public double Dps(Fighter f, DateTime now)
    {
        if (!HasDamage || DpsSeconds(now) < 1) return 0;
        return f.Damage / DpsSeconds(f, now);
    }

    /// <summary>A member's share of the group's damage, 0 to 1 (0 when damage isn't known).</summary>
    public double Share(Fighter f) { long all = Roster.Sum(x => x.Damage); return HasDamage && all > 0 ? f.Damage / (double)all : 0; }

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

    /// <summary>The group's damage per second.</summary>
    public double GroupDps(DateTime now) { var s = DpsSeconds(now); return HasDamage && s >= 1 ? Roster.Sum(f => f.Damage) / s : 0; }

    /// <summary>
    /// The rows the live card, the overlay and the list show (Steven, 2026-11-14: "don't count [a member] until they have at
    /// least 1 [damage]"): with damage counted, only members who have dealt some (and Other); before any damage, everyone.
    /// The detail's roster lists everyone.
    /// </summary>
    public IEnumerable<Fighter> Shown() => HasDamage ? Ranked().Where(f => f.Damage > 0) : Ranked();

    /// <summary>The members by damage when known, else you first and then by Combat Power.</summary>
    public IEnumerable<Fighter> Ranked() => HasDamage
        ? Roster.OrderBy(f => f.Other).ThenByDescending(f => f.Damage).ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
        : Roster.Where(f => !f.Other).OrderByDescending(f => f.You).ThenByDescending(f => f.CombatPower).ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase);

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

/// <summary>One member of the group in a fight.</summary>
public sealed class Fighter
{
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
    /// <summary>Joined after the pull.</summary>
    public bool Late { get; set; }
    /// <summary>Left the group during the fight.</summary>
    public bool Gone { get; set; }
    /// <summary>The "Other" row: HP the boss lost with no hit to name a source (damage over time, lost traffic), so the rows add up to the HP lost.</summary>
    public bool Other { get; set; }
    /// <summary>Known only from its damage (no group list yet): Combat Power and item level not known.</summary>
    public bool FromDamage { get; set; }
    public long Damage { get; set; }
    public int Deaths { get; set; }
    /// <summary>Damage by ability (the game's skill id; 0: damage over time and the rest), with its hits: a member's breakdown.</summary>
    public List<AbilityShare> Abilities { get; set; } = new();

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
        }
        if (LastHit == null || at > LastHit) LastHit = at;
    }

    public void AddAbility(int skill, long amount, int hits)
    {
        var a = Abilities.FirstOrDefault(x => x.Skill == skill);
        if (a == null) Abilities.Add(a = new AbilityShare { Skill = skill });
        a.Damage += amount;
        a.Hits += Math.Max(1, hits);
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

/// <summary>One ability's share of a member's damage: the skill (0 for damage over time and the rest), its damage and hits.</summary>
public sealed class AbilityShare
{
    public int Skill { get; set; }
    public long Damage { get; set; }
    public int Hits { get; set; }
    public double Average => Hits > 0 ? Damage / (double)Hits : 0;
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
