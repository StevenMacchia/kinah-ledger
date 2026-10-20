namespace KinahLedger.Protocol;

/// <summary>Game events the tracker cares about, independent of how they are encoded on the wire.</summary>
public abstract record GameEvent(DateTime Time);

public sealed record ItemAcquired(DateTime Time, long ItemId, int Count, bool Bound) : GameEvent(Time);
public sealed record KinahChanged(DateTime Time, long Delta, bool Bound) : GameEvent(Time);
public sealed record MobKilled(DateTime Time, long MobId) : GameEvent(Time);
/// <summary>Points-type pickups (Soul Points, Abyss Points). Guaranteed per kill, so they mark kills and never count as drops.</summary>
public sealed record CurrencyGained(DateTime Time, string Name, long Amount) : GameEvent(Time);
/// <summary>A pet Soul Crystal ("Obtained 1 Natura Soul Crystals."). Tracked by zone for pet farming; never a drop, never a kill.</summary>
public sealed record PetCrystal(DateTime Time, string Name, int Count) : GameEvent(Time);
/// <summary>A gather attempt that yielded nothing ("Essence Extraction failed"). Counts as a node attempt.</summary>
public sealed record GatherFailed(DateTime Time, string Skill) : GameEvent(Time);
/// <summary>A quest, title or box reward ("Acquired Artisan's Refining Stone (Bound) x3."). Logged, never counted as farming.</summary>
public sealed record RewardReceived(DateTime Time, string Name, int Count) : GameEvent(Time);
/// <summary>An experience gain line. Counted as a kill when the chat tab doesn't show kill lines themselves.</summary>
public sealed record XpGained(DateTime Time, long Amount) : GameEvent(Time);
public sealed record ZoneChanged(DateTime Time, long ZoneId, long? InstanceId) : GameEvent(Time);
