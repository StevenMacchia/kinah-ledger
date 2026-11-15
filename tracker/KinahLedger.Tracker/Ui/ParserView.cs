using KinahLedger.Parser;

namespace KinahLedger.Ui;

/// <summary>
/// What the Parser's views show of a fight (Parser v2), built from the record and the records kept before it. Every figure
/// comes from a field the record really has; a figure the record doesn't carry yet (crit, max hit, casts, the per-second
/// timeline, damage taken, the death recap) is null, and the column, tab or moment that would show it stays hidden until it
/// does. Nothing is ever made up to fill a gap.
/// </summary>
public sealed class FightView
{
    public Encounter E = null!;
    public DateTime Now;
    public List<MemberView> Members = new();
    /// <summary>Your best kill of this boss before this one, and your last pull of it (any outcome), from the records kept.</summary>
    public PullRef? Best, Last;

    public MemberView? You => Members.FirstOrDefault(m => m.F.You);
    public bool HasDamage => E.HasDamage;
    public double GroupDps => E.GroupDps(Now);
    public double Seconds => E.DpsSeconds(Now);

    // what the record carries: each view shows its part only when there's something to show
    public bool HasCrit => Members.Any(m => m.Crit != null);
    public bool HasMax => Members.Any(m => m.MaxHit != null);
    public bool HasCasts => Members.Any(m => m.Abilities.Any(a => a.Casts != null));
    public bool HasTimeline => Members.Any(m => m.PerSecond is { Count: > 1 });
    public bool HasTaken => Members.Any(m => m.Taken != null);
    public bool HasDeathLog => Members.Any(m => m.DeathLog.Count > 0);
    public bool HasBuffs => Members.Any(m => m.Buffs is { Count: > 0 });
    public bool HasCompare => You is { Damage: > 0 } && (Best != null || Last != null);
    public int Deaths => Members.Sum(m => m.Deaths);

    public static FightView Of(Encounter e, DateTime now, IEnumerable<Encounter>? past)
    {
        var v = new FightView { E = e, Now = now };
        var shown = e.HasDamage ? e.Ranked().Where(f => !f.Other).ToList() : e.Ranked().Where(f => !f.Other).ToList();
        int rank = 0;
        foreach (var f in shown)
        {
            var m = new MemberView { F = f, Rank = ++rank, Dps = e.HasDamage ? e.Dps(f, now) : 0, Share = e.Share(f), Damage = f.Damage, Deaths = f.Deaths };
            // the ids of one ability as one row (a spirit's attack at each rank), each named for the member's class
            foreach (var a in Abilities.Merged(f.Abilities).OrderByDescending(a => a.Damage))
                m.Abilities.Add(new AbilityView { Skill = a.Skill, Name = Abilities.Name(a.Skill, f.Class), Damage = a.Damage, Hits = a.Hits, Share = a.Damage / (double)Math.Max(1, f.Damage), Dps = a.Damage / Math.Max(1, e.DpsSeconds(f, now)) });
            Depth(m, f);
            v.Members.Add(m);
        }
        if (past != null && e.ModeKind == ParserMode.Bosses && e.Boss.Length > 0) (v.Best, v.Last) = Records(e, past);
        return v;
    }

    /// <summary>
    /// The deeper figures, read from the record when it carries them (store v2: crits, max and min hit, casts and hit kinds per
    /// ability; per-second damage; damage taken; the death log). A record without them leaves every one null.
    /// </summary>
    static void Depth(MemberView m, Fighter f)
    {
        // the record has no such fields yet: each one lights up here as the store carries it
    }

    /// <summary>Your best kill of this boss and your last pull of it before this record (yours only: other members' figures stay on this PC and in this view).</summary>
    public static (PullRef? Best, PullRef? Last) Records(Encounter e, IEnumerable<Encounter> past)
    {
        bool Same(Encounter p) => p.Id != e.Id && p.ModeKind == ParserMode.Bosses && p.HasDamage && !p.Live
            && (e.BossId > 0 && p.BossId > 0 ? p.BossId == e.BossId : string.Equals(p.Boss, e.Boss, StringComparison.OrdinalIgnoreCase))
            && p.Start < e.Start && p.You is { Damage: > 0 };
        var list = past.Where(Same).ToList();
        PullRef Ref(Encounter p) { var end = p.End ?? p.LastAt; var you = p.You!; return new PullRef { E = p, Dps = p.Dps(you, end), Seconds = p.Duration(end).TotalSeconds, When = p.Start, Abilities = Abilities.Merged(you.Abilities).ToDictionary(a => a.Skill, a => a.Damage / Math.Max(1, p.DpsSeconds(you, end))) }; }
        var best = list.Where(p => p.Outcome == "kill").Select(Ref).OrderByDescending(r => r.Dps).FirstOrDefault();
        var last = list.OrderByDescending(p => p.Start).Select(Ref).FirstOrDefault();
        return (best, last);
    }
}

public sealed class MemberView
{
    public Fighter F = null!;
    public int Rank; public double Dps, Share; public long Damage; public int Deaths;
    public List<AbilityView> Abilities = new();
    public bool You => F.You;
    /// <summary>Crit rate over every hit; the biggest hit. Null until the record carries crits and max hits.</summary>
    public double? Crit; public long? MaxHit;
    /// <summary>Damage in each second from the pull (store v2); null until then.</summary>
    public List<long>? PerSecond;
    /// <summary>Damage taken, all of it and by source (store v2); null until then.</summary>
    public long? Taken; public List<(string Source, int Skill, long Damage, int Hits)>? TakenBy;
    /// <summary>Each death with its time, the killing blow and the last five hits taken (store v2); empty until then.</summary>
    public List<DeathView> DeathLog = new();
    /// <summary>Each buff's time up over the fight (store v2); null until then.</summary>
    public List<(string Name, double Seconds, double Share)>? Buffs;
    public double ActiveSeconds => F.ActiveSeconds;
}

public sealed class AbilityView
{
    public int Skill; public string Name = ""; public long Damage; public int Hits; public double Share, Dps;
    public double Average => Hits > 0 ? Damage / (double)Hits : 0;
    /// <summary>Store v2 figures: null until the record carries them.</summary>
    public int? Casts; public double? Crit; public long? Max, Min; public Dictionary<string, int>? Kinds;
}

public sealed class DeathView { public double At; public double? BossHpLeft; public string Killer = ""; public List<(double At, string Source, long Amount, double? HpAfter, bool Crit)> Last = new(); }

/// <summary>A kept pull of the same boss: your DPS in it, how long it took, when, and your DPS by ability.</summary>
public sealed class PullRef { public Encounter E = null!; public double Dps, Seconds; public DateTime When; public Dictionary<int, double> Abilities = new(); }
