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
    /// <summary>The group's DPS, null under Encounter.MinDpsSeconds (shown "–", no 1 s floor).</summary>
    public double? GroupDpsOrNull => E.GroupDpsOf(Now);
    public double Seconds => E.DpsSeconds(Now);

    // what the record carries: each view shows its part only when there's something to show
    public bool HasCrit => Members.Any(m => m.Crit != null);
    public bool HasBack => Members.Any(m => m.Back != null);
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
            // the figures as the record counts them (the honest pass): boss damage or all targets (Encounter.Figure), DPS null
            // under 3 s, hits as counted (none made up) with the average over the counted ones
            var m = new MemberView { F = f, Rank = ++rank, Dps = e.HasDamage ? e.Dps(f, now) : 0, DpsOrNull = e.DpsOf(f, now), Share = e.Share(f), Damage = e.HasDamage ? e.Figure(f) : f.Damage, Deaths = f.Deaths };
            double secs = e.DpsSeconds(f, now);
            // the ids of one ability as one row (a spirit's attack at each rank), each named for the member's class
            foreach (var a in e.AbilityRows(f))
                m.Abilities.Add(new AbilityView { Skill = a.Skill, Name = Abilities.Name(a.Skill, f.Class), Damage = e.Figure(a), Hits = e.Hits(a), Avg = e.Average(a), Share = e.Figure(a) / (double)Math.Max(1, e.Figure(f)),
                    DpsOrNull = secs >= Encounter.MinDpsSeconds ? e.Figure(a) / secs : null, Casts = a.Casts, Max = a.Max, Min = a.Min,
                    Crit = Encounter.CritKey is { } ck && a.StreamHits is > 0 and var sh && a.Kinds != null ? Encounter.KindTotals(a, ck).Hits / (double)sh : null,
                    Back = Encounter.BackKey is { } bk && a.StreamHits is > 0 and var sb && a.Kinds != null ? Encounter.KindTotals(a, bk).Hits / (double)sb : null });
            Depth(m, f, e);
            v.Members.Add(m);
        }
        if (past != null && e.ModeKind == ParserMode.Bosses && e.Boss.Length > 0) (v.Best, v.Last) = Records(e, past);
        return v;
    }

    /// <summary>
    /// The deeper figures, read from the record when it carries them (store v2: crits, max and min hit, casts and hit kinds per
    /// ability; per-second damage; damage taken; the death log). A record without them leaves every one null.
    /// </summary>
    static void Depth(MemberView m, Fighter f, Encounter e)
    {
        m.MaxHit = f.Abilities.Max(a => a.Max);
        // crit: only the kind Encounter.CritKey names (ParserPrefs.CritSource, none by default); over the hits seen one by one
        if (Encounter.CritKey is { } key && f.Abilities.Sum(a => a.StreamHits ?? 0) is > 0 and var seen && f.Abilities.Any(a => a.Kinds != null))
        {
            var crits = f.Abilities.Select(a => Encounter.KindTotals(a, key)).ToList();
            m.Crit = crits.Sum(c => c.Hits) / (double)seen;
            long dealt = f.Abilities.Sum(a => a.StreamDamage ?? 0);
            if (dealt > 0 && f.Abilities.Any(a => a.KindDamage != null)) m.CritShare = crits.Sum(c => c.Damage) / (double)dealt;
            if (f.Abilities.Any(a => a.KindMax != null)) m.MaxCrit = crits.Max(c => c.Max);
        }
        // back attacks: only when Encounter.BackKey names the kind (ParserPrefs.BackSource, none by default); over the hits seen
        if (Encounter.BackKey is { } bkey && f.Abilities.Sum(a => a.StreamHits ?? 0) is > 0 and var seenB && f.Abilities.Any(a => a.Kinds != null))
            m.Back = f.Abilities.Sum(a => Encounter.KindTotals(a, bkey).Hits) / (double)seenB;
        // damage per second as the figures count it (the boss's, or all targets')
        m.PerSecond = e.BossView ? f.BossPerSecond : f.PerSecond;
        if (f.Taken is { Count: > 0 } taken)
        {
            m.Taken = taken.Sum(x => x.Damage);
            m.TakenBy = taken.OrderByDescending(x => x.Damage).Select(x => (Source(x.Source, x.Skill), x.Skill, x.Damage, x.Hits)).ToList();
        }
        if (f.DeathLog is { Count: > 0 } deaths)
            m.DeathLog = deaths.Select(d => new DeathView
            {
                At = d.At, BossHpLeft = d.BossHpLeft, Killer = Source(d.Killer, d.KillerSkill),
                Last = d.Last.Select(h => (h.At, Source(h.Source, h.Skill), h.Amount, h.HpAfter, false)).ToList(),
            }).ToList();
        if (f.Buffs is { Count: > 0 } buffs) m.Buffs = buffs.Select(b => (Abilities.Name(b.Skill, f.Class), b.Seconds, b.Share)).ToList();
    }

    /// <summary>Who dealt a hit taken, in words: the attacker's name when known (the boss), else its skill's when the skill list names it, else "".</summary>
    static string Source(string name, int skill) => name.Length > 0 ? name : Abilities.Resolve(skill) != null ? Abilities.Name(skill) : "";

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
    /// <summary>The member's DPS, null under Encounter.MinDpsSeconds (shown "–").</summary>
    public double? DpsOrNull;
    public List<AbilityView> Abilities = new();
    public bool You => F.You;
    /// <summary>Crit rate over every hit; the biggest hit. Null until the record carries crits and max hits.</summary>
    public double? Crit; public long? MaxHit;
    /// <summary>The crits' share of the damage of the hits seen, and the biggest crit: null unless a crit kind is picked (ParserPrefs.CritSource) and the record carries it.</summary>
    public double? CritShare; public long? MaxCrit;
    /// <summary>The share of the hits seen that came from behind: null unless a back-attack kind is picked (ParserPrefs.BackSource) and the record carries it.</summary>
    public double? Back;
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
    public int Skill; public string Name = ""; public long Damage; public int Hits; public double Share;
    /// <summary>The ability's DPS (null under Encounter.MinDpsSeconds) and its average a counted hit (null with none counted): both shown "–".</summary>
    public double? DpsOrNull, Avg;
    public double Dps => DpsOrNull ?? 0;
    public double Average => Avg ?? 0;
    /// <summary>Store v2 figures: null until the record carries them.</summary>
    public int? Casts; public double? Crit, Back; public long? Max, Min; public Dictionary<string, int>? Kinds;
}

public sealed class DeathView { public double At; public double? BossHpLeft; public string Killer = ""; public List<(double At, string Source, long Amount, double? HpAfter, bool Crit)> Last = new(); }

/// <summary>A kept pull of the same boss: your DPS in it, how long it took, when, and your DPS by ability.</summary>
public sealed class PullRef { public Encounter E = null!; public double Dps, Seconds; public DateTime When; public Dictionary<int, double> Abilities = new(); }
