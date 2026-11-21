namespace KinahLedger.Parser;

/// <summary>
/// The Parser page's Timeline tab of a kept boss pull (Ui/ParserTimeline.cs draws it): the pull from its first damage to its
/// end, lane by lane, every mark a real event of the record.
/// <list type="bullet">
/// <item>Your abilities: one lane each, biggest first, the damage it did in each second (a band) and each cast as it landed
/// (a mark; its hits' damage on hover). The first <see cref="TopLanes"/> are shown, the rest folded into one lane. Each lane's
/// damage is the sum of its seconds, which the parser adds from the same hits as the ability's total (Encounter.AbilityRows).</item>
/// <item>Your biggest hits (Fighter.TopHits) and the group's damage spikes (the seconds the group dealt the most).</item>
/// <item>The group: each member's damage by second, each death with its recap (the last hits taken, the killing blow last).</item>
/// <item>The boss's HP by second with the moments it crossed 75, 50 and 25%, when the record has it.</item>
/// </list>
/// Your lanes, casts and hits are kept from 1.0.38 on (your own row only); a pull kept before has none and no Timeline tab.
/// Nothing is made up: a lane the record doesn't carry is not drawn.
/// </summary>
public sealed class PullTimeline
{
    public Encounter E = null!;
    public Fighter You = null!;
    /// <summary>The pull's length in seconds (first damage to its end), and the whole seconds drawn.</summary>
    public double Length;
    public int N;
    /// <summary>Your abilities by the figures (boss damage, or all targets), biggest first; <see cref="Folded"/> holds those after the first <see cref="TopLanes"/>.</summary>
    public List<Lane> Abilities = new();
    public Lane? Folded;
    public List<MemberLane> Members = new();
    /// <summary>The group's damage in each second (the figures' kind), and its biggest seconds.</summary>
    public long[] Group = Array.Empty<long>();
    public List<Spike> Spikes = new();
    public List<Hit> BigHits = new();
    public List<Death> Deaths = new();
    /// <summary>The boss's HP left (0 to 1) at the end of each second; null when the record has none. The 75/50/25% crossings.</summary>
    public double[]? Hp;
    public List<Phase> Phases = new();
    /// <summary>Deaths the record counted (Fighter.Deaths); above <see cref="Deaths"/>' count, some came with no recap.</summary>
    public int DeathsCounted;

    public const int TopLanes = 6;
    public const int SpikesShown = 3;
    /// <summary>Two spikes are at least this far apart (seconds), so three marks are three moments.</summary>
    public const int SpikeGap = 8;
    public static readonly double[] PhaseMarks = { 0.75, 0.5, 0.25 };

    public sealed class Lane
    {
        public int Skill; public string Name = "";
        /// <summary>The pull's own figure for the ability (Encounter.AbilityRows), and the sum of the lane's seconds: the same.</summary>
        public long Damage, LaneDamage;
        public long[] PerSecond = Array.Empty<long>();
        /// <summary>Its casts as they landed, in time order; null when the record carries none for it (damage over time, a proc).</summary>
        public List<CastMark>? Casts;
        /// <summary>The pull's count of its casts (Encounter.AbilityRows' Casts); its share of your damage.</summary>
        public int? CastCount;
        public double Share;
        /// <summary>The abilities the folded lane holds (null on an ability's own lane).</summary>
        public List<Lane>? Of;
    }

    public sealed class MemberLane
    {
        public Fighter F = null!;
        public long Damage;
        public long[] PerSecond = Array.Empty<long>();
        public int Deaths;
    }

    public sealed record Spike(int Second, long Damage);
    public sealed record Hit(double At, int Skill, string Name, long Amount, bool? Crit);
    public sealed record Phase(double Mark, double At);
    public sealed class Death
    {
        public Fighter F = null!;
        public double At; public double? BossHpLeft;
        public string Killer = ""; public int KillerSkill;
        public List<TakenHit> Last = new();
    }

    /// <summary>A pull the tab is for: a kept boss pull of the Bosses mode, with your lanes in it (kept from 1.0.38 on).</summary>
    public static bool Has(Encounter e) =>
        !e.Live && e.ModeKind == ParserMode.Bosses && e.Boss.Length > 0 && e.HasDamage && e.You is { } y && y.Abilities.Any(a => a.Seconds != null || a.CastLog != null);

    /// <summary>The ability lanes' seconds as the figures count them: boss damage when the pull's figures are the boss's.</summary>
    static Dictionary<int, long>? SecondsOf(Encounter e, AbilityShare a) => e.BossView ? a.BossSeconds : a.Seconds;

    public static PullTimeline? Of(Encounter e, DateTime? now = null)
    {
        if (!Has(e)) return null;
        var you = e.You!;
        var end = e.End ?? e.LastAt;
        var t = new PullTimeline { E = e, You = you, Length = Math.Max(0, (end - e.Start).TotalSeconds) };
        // the seconds drawn: the pull's own length, or longer when a list runs past it (a late hit's second)
        int n = (int)Math.Ceiling(t.Length - 0.001);
        foreach (var f in e.Members) n = Math.Max(n, ((e.BossView ? f.BossPerSecond : f.PerSecond)?.Count ?? 0));
        foreach (var a in you.Abilities) if (SecondsOf(e, a) is { Count: > 0 } d) n = Math.Max(n, d.Keys.Max() + 1);
        t.N = Math.Max(1, n);

        // your abilities, grouped as the inspect view groups them (Abilities.Canon), each lane the sum of its ids' seconds
        long yours = Math.Max(1, e.Figure(you));
        var rows = e.AbilityRows(you);
        var byCanon = you.Abilities.GroupBy(a => KinahLedger.Parser.Abilities.Canon(a.Skill)).ToDictionary(g => g.Key, g => g.ToList());
        var lanes = new List<Lane>();
        foreach (var r in rows)
        {
            var ids = byCanon.GetValueOrDefault(r.Skill) ?? new();
            var ps = new long[t.N];
            foreach (var a in ids)
                if (SecondsOf(e, a) is { } d)
                    foreach (var (s, v) in d) ps[Math.Clamp(s, 0, t.N - 1)] += v;
            var casts = ids.Any(a => a.CastLog != null) ? ids.SelectMany(a => a.CastLog ?? new()).OrderBy(c => c.At).ToList() : null;
            lanes.Add(new Lane { Skill = r.Skill, Name = KinahLedger.Parser.Abilities.Name(r.Skill, you.Class), Damage = e.Figure(r), LaneDamage = ps.Sum(), PerSecond = ps, Casts = casts, CastCount = r.Casts, Share = e.Figure(r) / (double)yours });
        }
        t.Abilities = lanes;
        if (lanes.Count > TopLanes + 1)
        {
            var rest = lanes.Skip(TopLanes).ToList();
            var ps = new long[t.N];
            foreach (var l in rest) for (int i = 0; i < t.N; i++) ps[i] += l.PerSecond[i];
            var casts = rest.Any(l => l.Casts != null) ? rest.SelectMany(l => l.Casts ?? new()).OrderBy(c => c.At).ToList() : null;
            t.Folded = new Lane { Skill = 0, Name = "", Damage = rest.Sum(l => l.Damage), LaneDamage = ps.Sum(), PerSecond = ps, Casts = casts,
                CastCount = rest.Any(l => l.CastCount != null) ? rest.Sum(l => l.CastCount ?? 0) : null, Share = rest.Sum(l => l.Share), Of = rest };
        }

        // the group: each member's damage by second, and the group's
        t.Group = new long[t.N];
        foreach (var f in e.Ranked().Where(f => !f.Other))
        {
            var l = e.BossView ? f.BossPerSecond : f.PerSecond;
            var ps = new long[t.N];
            if (l != null) for (int i = 0; i < l.Count; i++) ps[Math.Min(i, t.N - 1)] += l[i];
            for (int i = 0; i < t.N; i++) t.Group[i] += ps[i];
            t.Members.Add(new MemberLane { F = f, Damage = e.Figure(f), PerSecond = ps, Deaths = f.Deaths });
            t.DeathsCounted += f.Deaths;
            foreach (var d in f.DeathLog ?? new())
                t.Deaths.Add(new Death { F = f, At = d.At, BossHpLeft = d.BossHpLeft, Killer = d.Killer, KillerSkill = d.KillerSkill, Last = d.Last.OrderBy(h => h.At).ToList() });
        }
        t.Deaths = t.Deaths.OrderBy(d => d.At).ToList();
        // the spikes: the group's biggest seconds, a few seconds apart
        foreach (var (v, i) in t.Group.Select((v, i) => (v, i)).OrderByDescending(x => x.v))
        {
            if (v <= 0 || t.Spikes.Count >= SpikesShown) break;
            if (t.Spikes.Any(s => Math.Abs(s.Second - i) < SpikeGap)) continue;
            t.Spikes.Add(new Spike(i, v));
        }
        t.Spikes = t.Spikes.OrderBy(s => s.Second).ToList();
        // your biggest hits, a crit only once a crit kind is picked (Encounter.CritKey)
        foreach (var h in you.TopHits ?? new())
            t.BigHits.Add(new Hit(h.At, h.Skill, KinahLedger.Parser.Abilities.Name(KinahLedger.Parser.Abilities.Canon(h.Skill), you.Class), h.Amount,
                Encounter.CritKey is { } ck && h.Kinds != null ? h.Kinds.Contains(ck) : null));

        // the boss's HP and its phases: each mark at the moment the HP crossed it (between the two seconds' values)
        if (e.BossHpPerSecond is { Count: > 1 } hps)
        {
            var hp = new double[t.N];
            for (int i = 0; i < t.N; i++) hp[i] = Math.Clamp(i < hps.Count ? hps[i] : hps[^1], 0, 1);
            if (e.Outcome == "kill") hp[^1] = 0;
            t.Hp = hp;
            t.Phases = PhasesOf(hp);
        }
        return t;
    }

    /// <summary>
    /// When the HP crossed each of 75, 50 and 25%: hp[i] is the HP at the end of second i (time i + 1), full at 0 s; the
    /// crossing is placed between the two values around it. A mark never crossed (a wipe above it) has none.
    /// </summary>
    public static List<Phase> PhasesOf(double[] hp)
    {
        var list = new List<Phase>();
        foreach (var mark in PhaseMarks)
        {
            // a pull that began well under a mark (the boss already hurt) never crossed it in this pull
            if (hp.Length == 0 || hp[0] < mark - 0.05) continue;
            double before = 1, at0 = 0;
            for (int i = 0; i < hp.Length; i++)
            {
                double now = hp[i], at1 = i + 1;
                if (now <= mark && before > mark)
                {
                    double k = before - now > 1e-9 ? (before - mark) / (before - now) : 1;
                    list.Add(new Phase(mark, Math.Round(at0 + k * (at1 - at0), 1)));
                    break;
                }
                before = now; at0 = at1;
            }
        }
        return list;
    }

    /// <summary>Your DPS in a span of the pull (the band's hover), from the abilities' seconds: damage over the seconds.</summary>
    public long DamageIn(Lane l, int from, int to)
    {
        long sum = 0;
        for (int i = Math.Max(0, from); i <= Math.Min(N - 1, to); i++) sum += l.PerSecond[i];
        return sum;
    }
}
