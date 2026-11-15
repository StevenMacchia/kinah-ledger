namespace KinahLedger.Packets;

// What the boss-fight parser (Parser/BossParser.cs) is told about the fight around the player. Only what happened is
// here; how it is recognised is the decoder's (klnet.dll, not part of this source). A build without the decoder sends
// none of these, and the Parser page says so.

/// <summary>The content the parser keeps fights for. Other: any place that isn't one of the three (the open world, a town, an arena).</summary>
public enum ContentKind { Other = 0, Transcendence = 1, Expedition = 2, Sanctuary = 3 }

/// <summary>How a boss fight ended: the boss died, the group did, or the player left (or the fight was given up with no result).</summary>
public enum BossOutcome { Kill = 0, Wipe = 1, Left = 2 }

/// <summary>One thing the game said about the fight, at the time its message came.</summary>
public abstract record BossEvent(DateTime At);

/// <summary>The player entered an instance: its kind, its name as the game names it, and the game's id for this copy of it (0 = unknown).</summary>
public sealed record InstanceEntered(DateTime At, ContentKind Kind, string Name, long InstanceId) : BossEvent(At);

/// <summary>The player left the instance (back to the world, a town, the character screen).</summary>
public sealed record InstanceLeft(DateTime At) : BossEvent(At);

/// <summary>The instance's mode as the game names it (Conquest, Exploration ...), once, a few seconds after InstanceEntered.</summary>
public sealed record InstanceMode(DateTime At, string Mode) : BossEvent(At);

/// <summary>A boss was engaged: its name and the game's id for this spawn of it.</summary>
public sealed record BossEngaged(DateTime At, string Boss, long BossId) : BossEvent(At);

/// <summary>A boss fight ended, with how (0 BossId = the fight under way).</summary>
public sealed record BossEnded(DateTime At, long BossId, BossOutcome Outcome) : BossEvent(At);

/// <summary>One member of the player's group. 0 = not known (Combat Power, item level); You marks the player.</summary>
public sealed record GroupMember(long Id, string Name, string Class, int CombatPower, int ItemLevel, bool You, string Server = "");

/// <summary>The whole group as it is now, the player included (alone: just the player).</summary>
public sealed record GroupChanged(DateTime At, IReadOnlyList<GroupMember> Members) : BossEvent(At);

/// <summary>
/// Damage one unit dealt another (SourceId matches a GroupMember's Id; 0 = HP lost with no hit to name a source). SourceName
/// and SourceIsYou when known. Skill is the ability (0 = none named; negative: a damage over time, minus its effect's id,
/// Parser/Abilities.cs); Hits the hits in it (0 = not counted). Reason: for SourceId 0, why no one is credited (0 = not
/// said; see Encounter.UncreditedWhy).
/// </summary>
public sealed record DamageDealt(DateTime At, long SourceId, long TargetId, long Amount, string SourceName = "", bool SourceIsYou = false, int Skill = 0, int Hits = 0, int Reason = 0) : BossEvent(At);

/// <summary>A boss's HP now and its max, after a change while it is fought (the Parser's HP bar); 0 = not known.</summary>
public sealed record BossHealth(DateTime At, long BossId, long Hp, long Max) : BossEvent(At);

/// <summary>A unit near the player died: an enemy (Boss when the game marks it so) or a group member; who killed it and with what, when known (0 = not).</summary>
public sealed record UnitDied(DateTime At, long Id, string Name, bool Boss, long KillerId = 0, int KillerSkill = 0) : BossEvent(At);

/// <summary>
/// One hit of a member's (Source: who it counts for, a summon's owner; Actor: who dealt it), its part of the boss's HP fall
/// (Amount), by Skill (as DamageDealt's). NoRow: a part with no single hit under it (count its damage, not a hit). Tick: a
/// damage over time's tick. Cast and N tie hits to their cast. Result, Marks and Multi are the hit's kind as it came,
/// unnamed until they are mapped. Why: for Source 0, the reason no one is credited (as DamageDealt.Reason).
/// </summary>
public sealed record HitDealt(DateTime At, long Source, long Actor, long Target, long Amount, int Skill, int Cast, int N, bool NoRow, bool Tick,
    int Result, int Marks, int Multi, int Why) : BossEvent(At);

/// <summary>A hit a member took: from Attacker (Npc: its kind, 0 = not known), Amount, by Skill.</summary>
public sealed record HitTaken(DateTime At, long Attacker, long Target, long Amount, int Skill, long Npc) : BossEvent(At);

/// <summary>A cast of Skill by Actor, counted for Source (a summon's owner).</summary>
public sealed record CastMade(DateTime At, long Source, long Actor, int Skill, int Cast) : BossEvent(At);

/// <summary>An effect put on (On) or taken off a member, in one of their slots: its id (the skill's id x 100 + n), and its length in ms (on: as given, -1 none; off: how long it was on).</summary>
public sealed record EffectChanged(DateTime At, long Target, int Slot, int Effect, bool On, long Ms) : BossEvent(At);

/// <summary>The reader lost some of the game's traffic here: what follows doesn't fully continue what came before.</summary>
public sealed record FeedGap(DateTime At) : BossEvent(At);

/// <summary>Where the fight events come from: the packet reader, when its decoder reads them; a recording in a test.</summary>
public interface IBossFeed
{
    /// <summary>The next fight event, in order, or null when none is waiting.</summary>
    BossEvent? NextBossEvent();
}

/// <summary>Fight events from a list (tests, a recording, the demo).</summary>
public sealed class ListBossFeed : IBossFeed
{
    readonly Queue<BossEvent> _q;
    public ListBossFeed(IEnumerable<BossEvent> events) => _q = new(events);
    public void Add(BossEvent e) => _q.Enqueue(e);
    public BossEvent? NextBossEvent() => _q.TryDequeue(out var e) ? e : null;
}
