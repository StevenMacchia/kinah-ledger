namespace KinahLedger.Protocol;

/// <summary>Game events the tracker cares about, independent of how they are encoded on the wire.</summary>
public abstract record GameEvent(DateTime Time);

public sealed record ItemAcquired(DateTime Time, long ItemId, int Count, bool Bound) : GameEvent(Time);
public sealed record KinahChanged(DateTime Time, long Delta, bool Bound) : GameEvent(Time);
public sealed record MobKilled(DateTime Time, long MobId) : GameEvent(Time);
public sealed record ZoneChanged(DateTime Time, long ZoneId, long? InstanceId) : GameEvent(Time);
