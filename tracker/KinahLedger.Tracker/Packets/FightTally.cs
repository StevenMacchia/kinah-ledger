namespace KinahLedger.Packets;

/// <summary>
/// What a fight event from the decoder is: a kill the player is in, a change of the player's Rank Points, or (the decoder's
/// interface version 12) the player's own damage on a player of the other faction, one hit at a time.
/// </summary>
public enum FightKind { Kill = 1, RankPoints = 2, Hit = 3 }

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
    /// <summary>A player's death by someone else's hand, the killer named (version 12): the player's only as an assist.</summary>
    OtherKiller = 64,
    /// <summary>The victim is a player of the other faction (version 12; on the player's own kills too, and on every Hit).</summary>
    VictimEnemy = 128,
}

/// <summary>
/// A fight event: a kill the player is in (their kill, their death, or one that named no killer), a Rank Points change, or
/// a hit of the player's on an enemy player (Hit: VictimId and VictimName the target, KillerId the player, Delta the damage).
/// MonsterId says which monster died (0 for a player; a Hit's skill); names are a player victim's and the killer's.
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
/// Assists (Steven, 2026-11-18: "assists should count as kills in abyss"): an enemy player's death by someone else's hand
/// counts as the player's PvP kill, marked an assist, when the player hit that enemy in the <see cref="AssistSeconds"/>
/// before it. On 2026-11-18 the gaps between Steven's last hit and the death were 0.0, 0.0, 0.1, 3.1, 4.2, 6.6 and 17.0 s;
/// the next nearest were some 700 s. Each death counts once; allies never (the player's hits land only on the other
/// faction), nor the player's own deaths, nor monsters. A decoder before version 12 sends neither, so no assists.
/// </summary>
public sealed class FightTally
{
    public const double HoldSeconds = 3, RiseAfter = 1.5, RiseBefore = 0.3, OwnRise = 0.6;
    /// <summary>How long before an enemy player's death a hit of the player's makes it an assist; HitAfter: a hit in the same moment, a little after the kill row.</summary>
    public const double AssistSeconds = 30, HitAfter = 0.5;

    /// <summary>
    /// A decided fight: the player's kill (of a player or a monster) or the player's death. Assist: a player's death by
    /// Killer's hand that the player hit HitBefore seconds before (a PvP kill all the same, Player true).
    /// </summary>
    public sealed record Outcome(DateTime At, bool Death, bool Player, bool Credited, string Name, uint MonsterId, bool Assist = false, string Killer = "", double HitBefore = 0);

    /// <summary>The player's hits on each enemy player lately (version 12), for the assists.</summary>
    readonly Dictionary<uint, List<DateTime>> _hits = new();
    /// <summary>The enemy players' deaths counted lately: one death is counted once, however it came.</summary>
    readonly Dictionary<uint, DateTime> _counted = new();

    readonly List<FightEvent> _held = new();
    readonly HashSet<FightEvent> _usedRise = new(ReferenceEqualityComparer.Instance);
    readonly HashSet<FightEvent> _paired = new(ReferenceEqualityComparer.Instance);   // the player's kills that have their rise
    /// <summary>Rank Points the player gained, from the game's messages.</summary>
    public long RankPoints { get; private set; }

    public void Add(FightEvent e)
    {
        if (e.Kind == FightKind.Hit)
        {
            if (e.VictimId == 0 || e.Delta <= 0) return;
            if (!_hits.TryGetValue(e.VictimId, out var l)) _hits[e.VictimId] = l = new();
            l.Add(e.At);
            // what no death can use any more goes: older than the window before the oldest death still to be decided (a
            // replay adds a whole connection before deciding anything, so a later fight's hits never push out an earlier one's)
            if (l.Count > 64 || _hits.Count > 256)
            {
                var from = e.At;
                foreach (var k in _held) if (k.Kind == FightKind.Kill && k.At < from) from = k.At;
                var cut = from.AddSeconds(-AssistSeconds - 1);
                if (l.Count > 64) l.RemoveAll(t => t < cut);
                if (_hits.Count > 256)
                    foreach (var id in _hits.Where(kv => kv.Value.Count == 0 || kv.Value[^1] < cut).Select(kv => kv.Key).ToList()) _hits.Remove(id);
            }
            return;
        }
        _held.Add(e); if (e.Kind == FightKind.RankPoints && e.Delta > 0) RankPoints += e.Delta;
    }

    /// <summary>The player's last hit on <paramref name="victim"/> in the <see cref="AssistSeconds"/> before <paramref name="at"/>, or null.</summary>
    DateTime? HitBefore(uint victim, DateTime at)
    {
        if (!_hits.TryGetValue(victim, out var l)) return null;
        DateTime? last = null;
        foreach (var t in l)
            if ((at - t).TotalSeconds <= AssistSeconds && (t - at).TotalSeconds <= HitAfter && (last == null || t > last)) last = t;
        return last;
    }

    /// <summary>True when this enemy player's death was counted already (the same victim within a few seconds); else it is now.</summary>
    bool CountedAlready(uint victim, DateTime at)
    {
        if (victim == 0) return false;
        if (_counted.TryGetValue(victim, out var was) && Math.Abs((at - was).TotalSeconds) < 5) return true;
        _counted[victim] = at;
        if (_counted.Count > 256) foreach (var k in _counted.Where(kv => Math.Abs((at - kv.Value).TotalMinutes) > 5).Select(kv => kv.Key).ToList()) _counted.Remove(k);
        return false;
    }

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
            bool enemy = player && e.Flags.HasFlag(FightFlags.VictimEnemy);
            if (e.Flags.HasFlag(FightFlags.Mine))
            {
                if (enemy && CountedAlready(e.VictimId, e.At)) continue;
                res.Add(new Outcome(e.At, false, player, false, e.VictimName, e.MonsterId));
                continue;
            }
            // an enemy player's death by someone else's hand: an assist when the player hit them in the 30 s before
            if (e.Flags.HasFlag(FightFlags.OtherKiller))
            {
                if (enemy && HitBefore(e.VictimId, e.At) is { } hit && !CountedAlready(e.VictimId, e.At))
                    res.Add(new Outcome(e.At, false, true, false, e.VictimName, 0, Assist: true, Killer: e.KillerName, HitBefore: Math.Max(0, (e.At - hit).TotalSeconds)));
                continue;
            }
            // a player's death that names no killer is never counted as yours (Steven, 2026-11-16: the Buddy said 82 PvP he
            // "definitely" didn't have: 82 such deaths of the same four players near him, each with a rise his own monster
            // kills paid). A PvP kill counts only when the game names you
            if (player) continue;
            // nor a death of something named after a player that is no player (version 12 tells those apart: a summon or an
            // object of a player's, MayB's 35 of 2026-11-16 among them), when the decoder still names it
            if (!e.Flags.HasFlag(FightFlags.VictimMonster) && e.VictimName.Length > 0) continue;
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
    public List<Outcome> Flush() { var res = Take(DateTime.MaxValue, all: true); _hits.Clear(); _counted.Clear(); return res; }
}
