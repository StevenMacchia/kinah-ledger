namespace KinahLedger.Packets;

/// <summary>What a fight event from the decoder is: a kill the player is in, or a change of the player's Rank Points.</summary>
public enum FightKind { Kill = 1, RankPoints = 2 }

/// <summary>What the decoder says about a kill (the decoder's interface version 3).</summary>
[Flags]
public enum FightFlags
{
    None = 0,
    /// <summary>The player killed it.</summary>
    Mine = 1,
    /// <summary>The game named no killer: a kill of the player's can be one (it pays the player all the same).</summary>
    NoKiller = 2,
    VictimPlayer = 4,
    VictimMonster = 8,
    /// <summary>The player died.</summary>
    VictimMe = 16,
    /// <summary>What the victim was isn't known.</summary>
    VictimUnseen = 32,
}

/// <summary>
/// A fight event: a kill the player is in (their kill, their death, or one that named no killer), or a Rank Points change.
/// MonsterId says which monster died (0 for a player); names are a player victim's and the killer's.
/// </summary>
public sealed record FightEvent(DateTime At, FightKind Kind, FightFlags Flags, uint VictimId, uint KillerId, uint MonsterId,
    long Delta, long Total, string VictimName, string KillerName);

/// <summary>
/// Turns the game's fight events into the player's kills and deaths (Steven, 2026-11-13: "count abyss pvp kills vs. mob
/// kills"). A kill the game says is the player's counts, a player or a monster as the game says. A kill that names no
/// killer counts as the player's when their Rank Points rose with it (up to 1.5 s after) and no kill of their own took
/// that rise: in the Abyss every kill of the player's pays Rank Points (55 of 56 rises in the 2026-11-13 test came with
/// one, the 56th with a kill that named no killer). A death counts once, with the killer's name when the game gave one.
/// Events are held <see cref="HoldSeconds"/>, so the rise after a kill is in before the kill is decided.
/// </summary>
public sealed class FightTally
{
    public const double HoldSeconds = 3, RiseAfter = 1.5, RiseBefore = 0.3, OwnRise = 0.6;

    /// <summary>A decided fight: the player's kill (of a player or a monster) or the player's death.</summary>
    public sealed record Outcome(DateTime At, bool Death, bool Player, bool Credited, string Name, uint MonsterId);

    readonly List<FightEvent> _held = new();
    readonly HashSet<FightEvent> _usedRise = new(ReferenceEqualityComparer.Instance);
    readonly HashSet<FightEvent> _paired = new(ReferenceEqualityComparer.Instance);   // the player's kills that have their rise
    /// <summary>Rank Points the player gained, from the game's messages.</summary>
    public long RankPoints { get; private set; }

    public void Add(FightEvent e) { _held.Add(e); if (e.Kind == FightKind.RankPoints && e.Delta > 0) RankPoints += e.Delta; }

    /// <summary>What is decided by <paramref name="now"/>: every held event at least <see cref="HoldSeconds"/> old.</summary>
    public List<Outcome> Take(DateTime now, bool all = false)
    {
        var res = new List<Outcome>();
        _held.Sort((a, b) => a.At.CompareTo(b.At));
        // a kill of the player's takes the rise of its own Rank Points first (the same moment), whatever is decided now
        foreach (var k in _held.Where(e => e.Kind == FightKind.Kill && e.Flags.HasFlag(FightFlags.Mine) && !_paired.Contains(e)).ToList())
            if (Rise(k.At, -OwnRise, OwnRise) is { } r) { _usedRise.Add(r); _paired.Add(k); }
        while (_held.Count > 0 && (all || (now - _held[0].At).TotalSeconds >= HoldSeconds))
        {
            var e = _held[0]; _held.RemoveAt(0); _paired.Remove(e);
            if (e.Kind != FightKind.Kill) { _usedRise.Remove(e); continue; }
            if (e.Flags.HasFlag(FightFlags.VictimMe)) { res.Add(new Outcome(e.At, true, e.KillerName.Length > 0, false, e.KillerName, 0)); continue; }
            bool player = e.Flags.HasFlag(FightFlags.VictimPlayer);
            if (e.Flags.HasFlag(FightFlags.Mine)) { res.Add(new Outcome(e.At, false, player, false, e.VictimName, e.MonsterId)); continue; }
            if (e.Flags.HasFlag(FightFlags.NoKiller) && Rise(e.At, -RiseBefore, RiseAfter) is { } rise)
            {
                _usedRise.Add(rise);
                res.Add(new Outcome(e.At, false, player, true, e.VictimName, e.MonsterId));
            }
        }
        return res;
    }

    FightEvent? Rise(DateTime at, double from, double to) => _held.FirstOrDefault(r => r.Kind == FightKind.RankPoints && r.Delta > 0 && !_usedRise.Contains(r)
        && (r.At - at).TotalSeconds >= from && (r.At - at).TotalSeconds <= to);

    /// <summary>A new connection: what is held is from the last one, decided as it stands.</summary>
    public List<Outcome> Flush() => Take(DateTime.MaxValue, all: true);
}
