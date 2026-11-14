using System.Text.Json.Serialization;
using KinahLedger.Packets;

namespace KinahLedger.Parser;

/// <summary>
/// One boss fight: from the boss's pull to its death, the group's wipe or the player leaving. Only inside a Transcendence,
/// an Expedition or a Sanctuary, only for a boss (Parser/BossParser.cs decides), one per boss: the next boss is a new one.
/// Kept on this PC in the character's encounters.jsonl (Parser/EncounterStore.cs); never uploaded.
/// </summary>
public sealed class Encounter
{
    /// <summary>Short and unique: the file's key for this fight.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..12];
    /// <summary>"Transcendence", "Expedition" or "Sanctuary".</summary>
    public string Kind { get; set; } = "";
    /// <summary>The instance's name ("Shattered Arkanis").</summary>
    public string Instance { get; set; } = "";
    /// <summary>The game's id for this copy of the instance (0 = unknown): a re-entry after a disconnect is the same copy.</summary>
    public long InstanceId { get; set; }
    public string Boss { get; set; } = "";
    public long BossId { get; set; }
    /// <summary>The instance's last boss on the boss list: killing it clears the instance.</summary>
    public bool FinalBoss { get; set; }
    public DateTime Start { get; set; }
    /// <summary>When it ended; null while it is under way.</summary>
    public DateTime? End { get; set; }
    /// <summary>The last thing heard of the fight: an open fight with nothing new for a long while is given up at this time.</summary>
    public DateTime LastAt { get; set; }
    /// <summary>"kill", "wipe", "left"; "" while under way.</summary>
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
    /// <summary>The tracker version that kept it.</summary>
    public string App { get; set; } = "";

    [JsonIgnore] public bool Live => End == null;
    [JsonIgnore] public BossOutcome? Result => Outcome switch { "kill" => BossOutcome.Kill, "wipe" => BossOutcome.Wipe, "left" => BossOutcome.Left, _ => null };

    /// <summary>How long the fight took (to now while it is under way, never less than 0).</summary>
    public TimeSpan Duration(DateTime now) { var d = (End ?? now) - Start; return d < TimeSpan.Zero ? TimeSpan.Zero : d; }

    /// <summary>The player's own row, if the group was known.</summary>
    [JsonIgnore] public Fighter? You => Roster.FirstOrDefault(f => f.You);

    /// <summary>A member's damage per second over the fight; 0 when damage isn't known.</summary>
    public double Dps(Fighter f, DateTime now) { var s = Duration(now).TotalSeconds; return HasDamage && s >= 1 ? f.Damage / s : 0; }

    /// <summary>The group's damage per second.</summary>
    public double GroupDps(DateTime now) { var s = Duration(now).TotalSeconds; return HasDamage && s >= 1 ? Roster.Sum(f => f.Damage) / s : 0; }

    /// <summary>The members by damage when known, else you first and then by Combat Power.</summary>
    public IEnumerable<Fighter> Ranked() => HasDamage
        ? Roster.OrderByDescending(f => f.Damage).ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
        : Roster.OrderByDescending(f => f.You).ThenByDescending(f => f.CombatPower).ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase);

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
    [JsonIgnore] public string OutcomeLabel => Outcome switch { "kill" => "Killed", "wipe" => "Wiped", "left" => "Left", _ => "In progress" };
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
    /// <summary>Joined after the pull.</summary>
    public bool Late { get; set; }
    /// <summary>Left the group during the fight.</summary>
    public bool Gone { get; set; }
    public long Damage { get; set; }
    public int Deaths { get; set; }

    public static Fighter From(GroupMember m) => new() { Id = m.Id, Name = m.Name.Trim(), Class = m.Class.Trim(), CombatPower = Math.Max(0, m.CombatPower), ItemLevel = Math.Max(0, m.ItemLevel), You = m.You };
}

/// <summary>An enemy that died near the player during a fight.</summary>
public sealed class KillNear
{
    public DateTime At { get; set; }
    public string Name { get; set; } = "";
    public bool Boss { get; set; }
}
