using KinahLedger.Packets;

namespace KinahLedger.Parser;

/// <summary>
/// An enemy player you killed or helped kill, newest first on the PvP panel: their name, Kill or Assist (with who landed
/// it), and your fight with them as a record of its own (<see cref="Fight"/>: your damage on them by skill, hit by hit), which
/// the meter's skills card shows as it shows a member's in a boss fight.
/// </summary>
/// <param name="Seq">This kill's number since the tracker started: its row's key.</param>
public sealed record PvpKill(long Seq, DateTime At, uint VictimId, string Name, bool Assist, string Killer, Encounter Fight)
{
    /// <summary>Your damage on them in the fight that ended with their death (0: no hit of yours on them was seen).</summary>
    public long Damage => Fight.You?.Damage ?? 0;
    /// <summary>Your hits on them in that fight.</summary>
    public int Hits => Fight.You?.Abilities.Sum(a => a.Hits) ?? 0;
}

/// <summary>
/// The PvP panel's data (Steven, 2026-11-20: "instead of having the DPS meter in PVP ... a panel which shows your last 3 kills
/// - the dmg you did to that person and if it was a kill or an assist and break it down the same way that it does for a pve
/// fight"). Fed the game's fight events as they are decoded (TrackerSession.RawFight), so a kill shows at once:
/// <list type="bullet">
/// <item>Your hits on enemy players (FightKind.Hit: the target, the damage, the skill) are kept per target.</item>
/// <item>A player's death the game names you the killer of is a Kill. An enemy player's death by someone else's hand is an
///   Assist when you hit them in the <see cref="FightTally.AssistSeconds"/> before it (or up to <see cref="FightTally.HitAfter"/>
///   after: such a death waits that long for your hit). The kill tally's rules (Packets/FightTally.cs), so the panel and the
///   PvP count agree. Each death counts once (the same player within <see cref="SameDeathSeconds"/>).</item>
/// <item>The fight with them: your hits on them up to their death, back to a gap of more than <see cref="FightGapSeconds"/>.
///   Real data only: the damage and skill of each hit as the game sent them. The game's hits on players carry no hit kind,
///   so no crit or back-attack figure; damage over time and summons' hits are not in them.</item>
/// </list>
/// Thread-safe: fed on the packet reader's thread, read by the window.
/// </summary>
public sealed class PvpKills
{
    /// <summary>The kills the panel lists.</summary>
    public const int Kept = 3;
    /// <summary>Hits on one player with gaps of up to this are one fight with them.</summary>
    public const double FightGapSeconds = FightTally.AssistSeconds;
    /// <summary>The same player's death again within this long is the same death (as the kill tally's).</summary>
    public const double SameDeathSeconds = 5;
    /// <summary>Hits kept per player, and players kept.</summary>
    const int HitsPerPlayer = 400, Players = 256;

    readonly object _gate = new();
    readonly Dictionary<uint, List<(DateTime At, int Skill, long Damage)>> _hits = new();
    readonly Dictionary<uint, DateTime> _counted = new();
    /// <summary>Enemy players' deaths by others' hands with no hit of yours yet: an assist if your hit comes within HitAfter.</summary>
    readonly List<FightEvent> _pending = new();
    readonly List<PvpKill> _last = new();
    long _seq;
    int _version;

    /// <summary>A kill or assist was added (on the thread that brought it).</summary>
    public event Action? Changed;
    /// <summary>Your character's name and class (for your row on a fight's skills card), asked when a kill is added.</summary>
    public Func<(string Name, string Class)>? Self { get; set; }
    /// <summary>Where the fights are: "Abyss" or the arena's name (set by the session).</summary>
    public string Place { get; set; } = "Abyss";

    /// <summary>Goes up with each kill or assist added.</summary>
    public int Version { get { lock (_gate) return _version; } }

    /// <summary>The last kills, newest first (at most <see cref="Kept"/>).</summary>
    public IReadOnlyList<PvpKill> Last { get { lock (_gate) return _last.ToList(); } }

    public void Take(FightEvent e)
    {
        bool added = false;
        lock (_gate)
        {
            if (e.Kind == FightKind.Hit) added = Hit(e);
            else if (e.Kind == FightKind.Kill) added = Kill(e);
            if (added) _version++;
        }
        if (added) { try { Changed?.Invoke(); } catch (Exception ex) { CrashLog.Write("pvp kills", ex); } }
    }

    bool Hit(FightEvent e)
    {
        if (e.VictimId == 0 || e.Delta <= 0) return false;
        if (!_hits.TryGetValue(e.VictimId, out var l))
        {
            if (_hits.Count >= Players) foreach (var id in _hits.OrderBy(kv => kv.Value.Count > 0 ? kv.Value[^1].At : DateTime.MinValue).Take(Players / 4).Select(kv => kv.Key).ToList()) _hits.Remove(id);
            _hits[e.VictimId] = l = new();
        }
        l.Add((e.At, (int)Math.Min(e.MonsterId, int.MaxValue), e.Delta));
        if (l.Count > HitsPerPlayer) l.RemoveRange(0, l.Count - HitsPerPlayer);
        // a death that waited for this hit: an assist now
        bool added = false;
        foreach (var d in _pending.Where(d => d.VictimId == e.VictimId && (e.At - d.At).TotalSeconds <= FightTally.HitAfter && e.At >= d.At.AddSeconds(-FightTally.AssistSeconds)).ToList())
        {
            _pending.Remove(d);
            if (!CountedAlready(d.VictimId, d.At)) { Add(d, assist: true); added = true; }
        }
        _pending.RemoveAll(d => (e.At - d.At).TotalSeconds > FightTally.HitAfter);
        return added;
    }

    bool Kill(FightEvent e)
    {
        _pending.RemoveAll(d => (e.At - d.At).TotalSeconds > FightTally.HitAfter);
        if (e.Flags.HasFlag(FightFlags.VictimMe) || !e.Flags.HasFlag(FightFlags.VictimPlayer)) return false;
        if (e.Flags.HasFlag(FightFlags.Mine))
        {
            if (CountedAlready(e.VictimId, e.At)) return false;
            Add(e, assist: false);
            return true;
        }
        if (!e.Flags.HasFlag(FightFlags.OtherKiller) || !e.Flags.HasFlag(FightFlags.VictimEnemy) || e.VictimId == 0) return false;
        if (HitsOn(e.VictimId, e.At).Count == 0) { _pending.Add(e); return false; }
        if (CountedAlready(e.VictimId, e.At)) return false;
        Add(e, assist: true);
        return true;
    }

    bool CountedAlready(uint victim, DateTime at)
    {
        if (victim == 0) return false;
        if (_counted.TryGetValue(victim, out var was) && Math.Abs((at - was).TotalSeconds) < SameDeathSeconds) return true;
        _counted[victim] = at;
        if (_counted.Count > Players) foreach (var k in _counted.Where(kv => Math.Abs((at - kv.Value).TotalMinutes) > 5).Select(kv => kv.Key).ToList()) _counted.Remove(k);
        return false;
    }

    /// <summary>
    /// Your hits on the player in the fight their death at <paramref name="at"/> ended: the last one no later than HitAfter
    /// past it and no earlier than AssistSeconds before it, then back from there while the gaps stay within FightGapSeconds.
    /// </summary>
    List<(DateTime At, int Skill, long Damage)> HitsOn(uint victim, DateTime at)
    {
        if (victim == 0 || !_hits.TryGetValue(victim, out var l)) return new();
        var upTo = l.Where(h => (h.At - at).TotalSeconds <= FightTally.HitAfter).OrderBy(h => h.At).ToList();
        if (upTo.Count == 0 || (at - upTo[^1].At).TotalSeconds > FightTally.AssistSeconds) return new();
        int from = upTo.Count - 1;
        while (from > 0 && (upTo[from].At - upTo[from - 1].At).TotalSeconds <= FightGapSeconds) from--;
        return upTo.GetRange(from, upTo.Count - from);
    }

    void Add(FightEvent e, bool assist)
    {
        var hits = HitsOn(e.VictimId, e.At);
        var (you, cls) = ("", "");
        try { if (Self?.Invoke() is { } s) (you, cls) = (s.Name.Trim(), s.Class.Trim()); } catch (Exception) { }
        var seq = ++_seq;
        var start = hits.Count > 0 && hits[0].At < e.At ? hits[0].At : e.At;
        var fight = new Encounter
        {
            Id = $"pvp-{seq}", Mode = "fight", Kind = Place, Opponent = e.VictimName.Trim(),
            Start = start, End = e.At, LastAt = e.At, Outcome = "kill", HasDamage = true,
            YourKills = assist ? 0 : 1, PlayerKills = assist ? 0 : 1,
        };
        var f = new Fighter { Id = 1, Name = you, Class = cls, You = true };
        foreach (var (at, skill, dmg) in hits)
        {
            f.Damage += dmg;
            f.Hit(at);
            var a = f.Ability(skill);
            a.Damage += dmg; a.Hits++;
            a.StreamHits = (a.StreamHits ?? 0) + 1; a.StreamDamage = (a.StreamDamage ?? 0) + dmg;
            a.Max = Math.Max(a.Max ?? 0, dmg); a.Min = a.Min is { } mn ? Math.Min(mn, dmg) : dmg;
            f.PerSecond = Fighter.AddAt(f.PerSecond, Math.Max(0, (int)(at - start).TotalSeconds), dmg);
        }
        fight.Roster.Add(f);
        _last.Insert(0, new PvpKill(seq, e.At, e.VictimId, e.VictimName.Trim(), assist, assist ? e.KillerName.Trim() : "", fight));
        if (_last.Count > Kept) _last.RemoveRange(Kept, _last.Count - Kept);
    }

    /// <summary>
    /// The panel as a record the meter's looks draw (Encounter.Pvp): one row per kill, newest first, its figure your damage on
    /// them, its share column Kill or Assist. With no kill yet, a waiting card ("No PvP kills yet"). Never kept.
    /// </summary>
    public Encounter Board(DateTime now) => BoardOf(Last, Place, now);

    public static Encounter BoardOf(IReadOnlyList<PvpKill> kills, string place, DateTime now)
    {
        var newest = kills.Count > 0 ? kills[0].At : now;
        var b = new Encounter
        {
            Id = "pvp-board", Mode = "fight", Kind = place, Start = newest, End = newest, LastAt = newest,
            HasDamage = kills.Count > 0, Waiting = kills.Count == 0, Pvp = kills,
        };
        foreach (var k in kills) b.Roster.Add(new Fighter { Id = k.Seq, Name = k.Name, Class = k.Assist ? Ui.ClassArt.PvpAssist : Ui.ClassArt.PvpKill, Damage = k.Damage, FirstHit = k.Fight.Start, LastHit = k.At });
        return b;
    }
}
