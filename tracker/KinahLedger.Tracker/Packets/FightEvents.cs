namespace KinahLedger.Packets;

// The boss parser's events (klnet.dll's interface version 4), as the tracker reads them from PacketReader.NextFight().
// They come only inside a Transcendence, Expedition or Sanctuary instance, between InstanceEntered and InstanceLeft.
// Shapes agreed between the decoder binding (Decoder.cs) and the Parser tab (TrackerSession.Parser.cs); nothing here says
// how the game's traffic carries them.

public enum ContentKind { Other = 0, Transcendence = 1, Expedition = 2, Sanctuary = 3 }

public enum BossOutcome { Kill = 0, Wipe = 1, Left = 2 }

public abstract record FightEvent(DateTime At);

/// <summary>Into a boss instance. Name as the game names it; InstanceId a serial, new for each entry.</summary>
public sealed record InstanceEntered(DateTime At, ContentKind Kind, string Name, long InstanceId) : FightEvent(At);

public sealed record InstanceLeft(DateTime At) : FightEvent(At);

/// <summary>A boss pull: the first HP it lost. BossId is the boss's entity (one per spawn); every BossEngaged gets one BossEnded.</summary>
public sealed record BossEngaged(DateTime At, string Boss, long BossId) : FightEvent(At);

/// <summary>The pull's end: killed, a wipe or reset (out of combat with HP left, or back to full), or Left (the instance or the connection ended first).</summary>
public sealed record BossEnded(DateTime At, long BossId, BossOutcome Outcome) : FightEvent(At);

/// <summary>A boss's HP now and its max; sent with every change while it is fought.</summary>
public sealed record BossHealth(DateTime At, long BossId, long Hp, long Max) : FightEvent(At);

/// <summary>The whole roster each time, the player included.</summary>
public sealed record GroupChanged(DateTime At, IReadOnlyList<GroupMember> Members) : FightEvent(At);

/// <summary>A group member; 0 when not known.</summary>
public sealed record GroupMember(long Id, string Name, string Class, int CombatPower, int ItemLevel, bool You);

/// <summary>
/// HP a boss lost to one source. SourceId matches GroupMember.Id; SourceId 0 is "other" (a fall no hit came with: damage over
/// time, or traffic lost). A pull's DamageDealt adds up to the HP the boss lost in it. SourceName when the decoder knows it.
/// </summary>
public sealed record DamageDealt(DateTime At, long SourceId, long TargetId, long Amount, string SourceName = "", bool SourceIsYou = false) : FightEvent(At);

/// <summary>A death in the instance (kills near you); Boss when it was one of the bosses.</summary>
public sealed record UnitDied(DateTime At, long Id, string Name, bool Boss) : FightEvent(At);

/// <summary>The reader lost traffic here: a pull's damage may be short of what happened.</summary>
public sealed record FeedGap(DateTime At) : FightEvent(At);
