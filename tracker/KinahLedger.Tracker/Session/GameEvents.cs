namespace KinahLedger.Session;

/// <summary>Game events the tracker counts, as read from the chat lines on screen.</summary>
public abstract record GameEvent(DateTime Time)
{
    /// <summary>
    /// How the pickup was read, for the recent pickups strip and the "from loot list" tag: "chat", "chat · recovered" (a
    /// line read again or counted from a read that didn't line up), "loot list" (the list beside the character, which the
    /// chat never confirmed), or with " · guessed" when the name was partly hidden and taken as its closest item.
    /// </summary>
    public string Via { get; init; } = "";
    /// <summary>True when the chat never confirmed this pickup as read: from the loot list, or a guessed name, unless a second read of a rare drop confirmed it (RareGate).</summary>
    public bool Unconfirmed => !Via.Contains("confirmed") && (Via.Contains("loot list") || Via.Contains("guessed"));
}

public sealed record ItemAcquired(DateTime Time, long ItemId, int Count, bool Bound) : GameEvent(Time);
public sealed record KinahChanged(DateTime Time, long Delta, bool Bound) : GameEvent(Time);
public sealed record MobKilled(DateTime Time, long MobId) : GameEvent(Time);
/// <summary>Points-type pickups (Soul Points, Abyss Points). Guaranteed per kill, so they mark kills and never count as drops.</summary>
/// <param name="Enemy">For Soul Points: the kind of enemy the line names, as read ("Obtained 1 Drakan Probe Soul Points."). One line per kill, so it says which enemy died.</param>
public sealed record CurrencyGained(DateTime Time, string Name, long Amount, string Enemy = "") : GameEvent(Time);
/// <summary>A pet Soul Crystal ("Obtained 1 Natura Soul Crystals."). Tracked by zone for pet farming; never a drop, never a kill.</summary>
/// <summary>A "Soul: Drakan Probe (Bound)" row in the loot list beside the character: the soul every enemy drops, so one kill of that kind.</summary>
public sealed record EnemySoul(DateTime Time, string Kind) : GameEvent(Time);
public sealed record PetCrystal(DateTime Time, string Name, int Count) : GameEvent(Time);
/// <summary>A gather attempt that yielded nothing ("Essence Extraction failed"). Counts as a node attempt.</summary>
public sealed record GatherFailed(DateTime Time, string Skill) : GameEvent(Time);
/// <summary>A quest, title or box reward ("Acquired Artisan's Refining Stone (Bound) x3."). Logged, never counted as farming.</summary>
public sealed record RewardReceived(DateTime Time, string Name, int Count) : GameEvent(Time);
/// <summary>An experience gain line. Counted as a kill when the chat tab doesn't show kill lines themselves.</summary>
public sealed record XpGained(DateTime Time, long Amount) : GameEvent(Time);

/// <summary>One of the player's own Market listings sold: what, how many, for how much in total. Never who bought it (the game doesn't say).</summary>
public sealed record MarketSold(DateTime Time, string Item, int Qty, long Total) : GameEvent(Time);
/// <summary>The player listed something on the Market: kept on this PC only, so a later sale can say how long it took.</summary>
public sealed record MarketListed(DateTime Time, string Item, int Qty, long Price) : GameEvent(Time);
/// <summary>One of the player's own listings expired unsold.</summary>
public sealed record MarketExpired(DateTime Time, string Item, int Qty) : GameEvent(Time);
/// <summary>A craft finished: the item and whether it worked. Materials and cost aren't shown in chat, so they aren't counted.</summary>
/// <param name="Combo">The combo roll after a craft (the crafting panel turns blue): whether that craft's item was upgraded, not another craft.</param>
public sealed record CraftResult(DateTime Time, string Item, bool Ok, bool Combo = false) : GameEvent(Time)
{
    /// <summary>What one success of this recipe makes, from the panel's recipe level (RecipeBook.YieldFor); null when it isn't known.</summary>
    public int? Makes { get; init; }
}
/// <summary>An enchant attempt: the item, the step (+From to +To, -1 when the line didn't show it) and whether it worked.</summary>
public sealed record EnchantResult(DateTime Time, string Item, int From, int To, bool Ok) : GameEvent(Time);
