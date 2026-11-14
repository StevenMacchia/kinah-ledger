namespace KinahLedger.Packets;

// What the boss-fight parser (Parser/BossParser.cs) is told about the fight around the player. Only what happened is
// here; how it is recognised is the decoder's (klnet.dll, not part of this source). A build without the decoder sends
// none of these, and the Parser page says so.

/// <summary>The content the parser keeps fights for. Other: any place that isn't one of the three (the open world, a town, an arena).</summary>
public enum ContentKind { Other = 0, Transcendence = 1, Expedition = 2, Sanctuary = 3 }

/// <summary>How a boss fight ended: the boss died, the group did, or the player left (or the fight was given up with no result).</summary>
public enum BossOutcome { Kill = 0, Wipe = 1, Left = 2 }

/// <summary>One thing the game said about the fight, at the time its message came.</summary>
public abstract record FightEvent(DateTime At);

/// <summary>The player entered an instance: its kind, its name as the game names it, and the game's id for this copy of it (0 = unknown).</summary>
public sealed record InstanceEntered(DateTime At, ContentKind Kind, string Name, long InstanceId) : FightEvent(At);

/// <summary>The player left the instance (back to the world, a town, the character screen).</summary>
public sealed record InstanceLeft(DateTime At) : FightEvent(At);

/// <summary>A boss was engaged: its name and the game's id for this spawn of it.</summary>
public sealed record BossEngaged(DateTime At, string Boss, long BossId) : FightEvent(At);

/// <summary>A boss fight ended, with how (0 BossId = the fight under way).</summary>
public sealed record BossEnded(DateTime At, long BossId, BossOutcome Outcome) : FightEvent(At);

/// <summary>One member of the player's group. 0 = not known (Combat Power, item level); You marks the player.</summary>
public sealed record GroupMember(long Id, string Name, string Class, int CombatPower, int ItemLevel, bool You);

/// <summary>The whole group as it is now, the player included (alone: just the player).</summary>
public sealed record GroupChanged(DateTime At, IReadOnlyList<GroupMember> Members) : FightEvent(At);

/// <summary>Damage one unit dealt another (SourceId matches a GroupMember's Id).</summary>
public sealed record DamageDealt(DateTime At, long SourceId, long TargetId, long Amount) : FightEvent(At);

/// <summary>A unit near the player died: an enemy (Boss when the game marks it so) or a group member.</summary>
public sealed record UnitDied(DateTime At, long Id, string Name, bool Boss) : FightEvent(At);

/// <summary>The reader lost some of the game's traffic here: what follows doesn't fully continue what came before.</summary>
public sealed record FeedGap(DateTime At) : FightEvent(At);

/// <summary>Where the fight events come from: the packet reader, when its decoder reads them; a recording in a test.</summary>
public interface IFightFeed
{
    /// <summary>The next fight event, in order, or null when none is waiting.</summary>
    FightEvent? NextFight();
}

/// <summary>Fight events from a list (tests, a recording, the demo).</summary>
public sealed class ListFightFeed : IFightFeed
{
    readonly Queue<FightEvent> _q;
    public ListFightFeed(IEnumerable<FightEvent> events) => _q = new(events);
    public void Add(FightEvent e) => _q.Enqueue(e);
    public FightEvent? NextFight() => _q.TryDequeue(out var e) ? e : null;
}
