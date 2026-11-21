namespace KinahLedger.Parser;

/// <summary>
/// Pull against pull (the Parser page's Compare tab): one boss pull beside another pull of the same fight (the boss in the same
/// instance and mode, FightRecords.KeyOf), by default your best kill of it. For you, and for each member whose damage both
/// pulls have: DPS and fight length, then every ability's damage, DPS, casts, crit rate and share of the member's damage, both
/// pulls side by side. Every figure is the pull's own, as its page counts it (Encounter.Figure, DpsSeconds, AbilityRows): a
/// figure one of the two pulls doesn't carry (casts, crit) is null, and nothing is guessed. It says what changed, never why.
/// </summary>
public sealed class PullCompare
{
    /// <summary>The pull opened (A) and the one it is measured against (B), with the instants their figures are at.</summary>
    public Encounter A = null!, B = null!;
    public DateTime AAt, BAt;
    /// <summary>
    /// The figures are damage to the boss in both pulls (each pull's BossView); false when either counts every target (the
    /// Boss / All targets choice, or an older pull that kept no boss damage apart): then both count every target.
    /// </summary>
    public bool BossOnly;
    /// <summary>You first, then the members whose damage both pulls have, by this pull's damage.</summary>
    public List<MemberCompare> Members = new();
    public MemberCompare? You => Members.FirstOrDefault(m => m.You);

    /// <summary>Each pull's length (start to end).</summary>
    public double ALength => A.Duration(AAt).TotalSeconds;
    public double BLength => B.Duration(BAt).TotalSeconds;

    /// <summary>The figures of a fighter in a pull, as this compare counts them.</summary>
    long Fig(Fighter f) => BossOnly ? f.BossDamage : f.Damage;
    long Fig(AbilityShare a) => BossOnly ? a.BossDamage : a.Damage;
    int HitsOf(AbilityShare a) => BossOnly ? a.BossHits : a.Hits;

    /// <summary>A pull another can be measured against: a kept boss pull with your damage in it.</summary>
    public static bool Comparable(Encounter e) => e.ModeKind == ParserMode.Bosses && !e.Live && e.HasDamage && !e.Wave && e.Pvp == null
        && (e.Boss.Length > 0 || e.BossId > 0) && e.You is { Damage: > 0 };

    /// <summary>The other pulls of this one's fight that it can be measured against, newest first.</summary>
    public static List<Encounter> Others(Encounter e, IEnumerable<Encounter> past)
    {
        if (e.ModeKind != ParserMode.Bosses || e.Boss.Length == 0 && e.BossId <= 0) return new();
        string key = FightRecords.KeyOf(e);
        return past.Where(p => p.Id != e.Id && Comparable(p) && FightRecords.KeyOf(p) == key)
            .GroupBy(p => p.Id).Select(g => g.First())
            .OrderByDescending(p => p.Start).ToList();
    }

    /// <summary>Your DPS in a kept pull (at its end), as its page says it.</summary>
    public static double YourDps(Encounter p) => p.You is { } y ? p.Dps(y, p.End ?? p.LastAt) : 0;

    /// <summary>
    /// The pull measured against by default: your best kill of the fight (the kills FightRecords counts, by your DPS), else
    /// with no other kill your best pull of it; null with no other pull.
    /// </summary>
    public static Encounter? Best(IReadOnlyList<Encounter> others) =>
        others.Where(FightRecords.Counts).OrderByDescending(YourDps).FirstOrDefault()
        ?? others.OrderByDescending(YourDps).FirstOrDefault();

    /// <summary>Pull <paramref name="a"/> against <paramref name="b"/>, each at its end (a live pull at <paramref name="aAt"/>).</summary>
    /// <param name="nameOfA">The name a member of <paramref name="a"/> is matched by in <paramref name="b"/>, when it isn't the one
    /// it carries: a share picture's copy with the other names hidden ("Chanter", MeterShare.Hidden) is still matched by the real
    /// names, which the other pull carries.</param>
    public static PullCompare? Of(Encounter a, Encounter b, DateTime? aAt = null, Func<Fighter, string>? nameOfA = null)
    {
        if (a.You is not { } ya || b.You is not { } yb) return null;
        var c = new PullCompare { A = a, B = b, AAt = a.Live ? aAt ?? a.LastAt : a.End ?? a.LastAt, BAt = b.End ?? b.LastAt, BossOnly = a.BossView && b.BossView };
        if (c.Fig(ya) <= 0 || c.Fig(yb) <= 0) return null;
        c.Members.Add(c.MemberOf(ya, yb));
        // the other members by name (the game's ids for them change from one entry to the next), when both pulls have their damage
        var bByName = b.Members.Where(f => !f.You && f.Name.Trim().Length > 0).GroupBy(f => f.Name.Trim(), StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        foreach (var fa in a.Members.Where(f => !f.You && c.Fig(f) > 0).OrderByDescending(c.Fig))
            if (bByName.TryGetValue((nameOfA?.Invoke(fa) ?? fa.Name).Trim(), out var fb) && c.Fig(fb) > 0) c.Members.Add(c.MemberOf(fa, fb));
        return c;
    }

    MemberCompare MemberOf(Fighter fa, Fighter fb)
    {
        double sa = Math.Max(A.DpsSeconds(fa, AAt), Encounter.MinDpsSeconds), sb = Math.Max(B.DpsSeconds(fb, BAt), Encounter.MinDpsSeconds);
        long ca = A.Members.Sum(Fig), cb = B.Members.Sum(Fig);
        var m = new MemberCompare
        {
            A = fa, B = fb, You = fa.You, DamageA = Fig(fa), DamageB = Fig(fb), SecondsA = sa, SecondsB = sb,
            ShareA = ca > 0 ? Fig(fa) / (double)ca : 0, ShareB = cb > 0 ? Fig(fb) / (double)cb : 0,
        };
        var ra = Rows(fa); var rb = Rows(fb);
        bool critA = CritOn(fa), critB = CritOn(fb);
        foreach (var skill in ra.Keys.Union(rb.Keys))
        {
            ra.TryGetValue(skill, out var x); rb.TryGetValue(skill, out var y);
            m.Skills.Add(new SkillCompare
            {
                Skill = skill,
                DamageA = x != null ? Fig(x) : 0, DamageB = y != null ? Fig(y) : 0,
                DpsA = x != null ? Fig(x) / sa : 0, DpsB = y != null ? Fig(y) / sb : 0,
                HitsA = x != null ? HitsOf(x) : 0, HitsB = y != null ? HitsOf(y) : 0,
                // casts: only when the pull counts them (store v2); a skill a pull counts casts for but didn't use is 0 casts
                CastsA = CastsOn(fa) ? x?.Casts ?? 0 : null, CastsB = CastsOn(fb) ? y?.Casts ?? 0 : null,
                CritA = critA ? CritOf(x) : null, CritB = critB ? CritOf(y) : null,
                ShareA = m.DamageA > 0 && x != null ? Fig(x) / (double)m.DamageA : 0, ShareB = m.DamageB > 0 && y != null ? Fig(y) / (double)m.DamageB : 0,
            });
        }
        m.Skills = m.Skills.OrderByDescending(s => Math.Max(s.DpsA, s.DpsB)).ThenBy(s => s.Skill).ToList();
        return m;
    }

    /// <summary>A fighter's abilities, the ids of one ability as one (Encounter.AbilityRows' grouping), every one with damage in this compare's figures.</summary>
    Dictionary<int, AbilityShare> Rows(Fighter f) => f.Abilities
        .GroupBy(a => Abilities.Canon(a.Skill))
        .Select(g => new AbilityShare
        {
            Skill = g.Key, Damage = g.Sum(a => a.Damage), Hits = g.Sum(a => a.Hits), BossDamage = g.Sum(a => a.BossDamage), BossHits = g.Sum(a => a.BossHits),
            Casts = g.Any(a => a.Casts != null) ? g.Sum(a => a.Casts ?? 0) : null,
            StreamHits = g.Any(a => a.StreamHits != null) ? g.Sum(a => a.StreamHits ?? 0) : null,
            Kinds = g.Any(a => a.Kinds != null) ? g.Where(a => a.Kinds != null).SelectMany(a => a.Kinds!).GroupBy(k => k.Key).ToDictionary(k => k.Key, k => k.Sum(v => v.Value)) : null,
        })
        .Where(a => Fig(a) > 0).ToDictionary(a => a.Skill);

    /// <summary>The pull counts casts for this fighter (store v2): any of their abilities carries them.</summary>
    static bool CastsOn(Fighter f) => f.Abilities.Any(a => a.Casts != null);
    /// <summary>A crit kind is picked (ParserPrefs.CritSource) and the fighter's hits carry kinds.</summary>
    static bool CritOn(Fighter f) => Encounter.CritKey != null && f.Abilities.Any(a => a.Kinds != null && (a.StreamHits ?? 0) > 0);
    /// <summary>An ability's crit rate over its hits seen one by one; null when it has none seen (or no ability: not used).</summary>
    static double? CritOf(AbilityShare? a) => a is { StreamHits: > 0 and var sh, Kinds: not null } && Encounter.CritKey is { } k ? a.Kinds.GetValueOrDefault(k) / (double)sh : null;
}

/// <summary>One member in both pulls: their damage, DPS and share of the group's in each, and their abilities side by side.</summary>
public sealed class MemberCompare
{
    public Fighter A = null!, B = null!;
    public bool You;
    public long DamageA, DamageB;
    /// <summary>The seconds each pull's DPS is over for them (Encounter.DpsSeconds, never under MinDpsSeconds).</summary>
    public double SecondsA, SecondsB;
    public double ShareA, ShareB;
    public double DpsA => DamageA / SecondsA;
    public double DpsB => DamageB / SecondsB;
    /// <summary>The change in DPS, and as a share of the other pull's (null when it had none).</summary>
    public double DpsDelta => DpsA - DpsB;
    public double? DpsChange => DpsB > 0 ? DpsA / DpsB - 1 : null;
    /// <summary>Their abilities, the biggest (in either pull) first.</summary>
    public List<SkillCompare> Skills = new();
    public bool HasCasts => Skills.Any(s => s.CastsA != null) && Skills.Any(s => s.CastsB != null);
    public bool HasCrit => Skills.Any(s => s.CritA != null) && Skills.Any(s => s.CritB != null);

    /// <summary>
    /// The abilities that lost the most DPS against the other pull (the change in their damage a second, which adds up to the
    /// member's change), at most <paramref name="n"/>, biggest loss first; none when no ability lost any.
    /// </summary>
    public List<SkillCompare> Losses(int n = 3) => Skills.Where(s => s.DpsDelta < 0).OrderBy(s => s.DpsDelta).ThenBy(s => s.Skill).Take(n).ToList();
    /// <summary>The abilities that gained the most, as <see cref="Losses"/>.</summary>
    public List<SkillCompare> Gains(int n = 3) => Skills.Where(s => s.DpsDelta > 0).OrderByDescending(s => s.DpsDelta).ThenBy(s => s.Skill).Take(n).ToList();
}

/// <summary>One ability in both pulls (A this one, B the other): 0 damage in a pull it wasn't used in; casts and crit null where that pull doesn't carry them.</summary>
public sealed class SkillCompare
{
    public int Skill;
    public long DamageA, DamageB;
    public double DpsA, DpsB;
    public int HitsA, HitsB;
    public int? CastsA, CastsB;
    public double? CritA, CritB;
    public double ShareA, ShareB;
    public bool UsedA => DamageA > 0;
    public bool UsedB => DamageB > 0;
    public double DpsDelta => DpsA - DpsB;
    /// <summary>The change in its damage a second as a share of the other pull's; null when it wasn't used there.</summary>
    public double? DpsChange => DpsB > 0 ? DpsA / DpsB - 1 : null;
    /// <summary>The change in its damage as a share of the other pull's; null when it wasn't used there.</summary>
    public double? DamageChange => DamageB > 0 ? DamageA / (double)DamageB - 1 : null;
    public int? CastsDelta => CastsA is { } a && CastsB is { } b ? a - b : null;
    public double? CritDelta => CritA is { } a && CritB is { } b ? a - b : null;
}
