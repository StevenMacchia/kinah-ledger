using System.Globalization;
using System.Text.Json.Serialization;

namespace KinahLedger.Parser;

/// <summary>
/// Your best kill of a fight as it went, second by second (the PB ghost's racing line): your damage and the group's in each
/// second from the pull's start, as the record's figures count them (damage to the boss when <see cref="Boss"/>), and the
/// boss's HP left at the end of each second. Kept with the fight's record (FightRecord.Ghost) when the best is set, so it
/// outlives the fight read back. A record kept before 1.0.38 has none until its best fight is found among the fights kept
/// (FightRecords.GhostFor), or a new best sets one; with none, no ghost is shown.
/// </summary>
public sealed class GhostCurve
{
    /// <summary>The fight it is (Encounter.Id).</summary>
    public string Fight { get; set; } = "";
    /// <summary>The figures are damage to the boss (the fight's BossView when it was kept); a meter counting every target never races it.</summary>
    public bool Boss { get; set; }
    /// <summary>The pull's length, start to the kill (seconds), and your DPS and the group's over it: the race's finish line.</summary>
    public double Seconds { get; set; }
    public double Dps { get; set; }
    public double GroupDps { get; set; }
    /// <summary>Your damage, the group's (credited, as the rows add up), in each second; comma-separated whole numbers.</summary>
    public string You { get; set; } = "";
    public string Group { get; set; } = "";
    /// <summary>The boss's HP left at the end of each second in ten-thousandths (10000 = full), never rising; "" when the HP wasn't known.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public string Hp { get; set; } = "";

    /// <summary>Per-second lists, read from the strings once, and their running sums (a point's damage so far in one step).</summary>
    long[]? _you, _group; double[]? _hp, _youSum, _groupSum;
    [JsonIgnore] internal long[] YouPer => _you ??= Longs(You);
    [JsonIgnore] internal long[] GroupPer => _group ??= Longs(Group);
    [JsonIgnore] internal double[] HpPer => _hp ??= Hp.Length == 0 ? Array.Empty<double>() : Longs(Hp).Select(x => x / (double)HpUnit).ToArray();

    const int HpUnit = 10_000;

    static long[] Longs(string s) => s.Length == 0 ? Array.Empty<long>()
        : s.Split(',').Select(x => long.TryParse(x, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0).ToArray();
    static string Join(IEnumerable<long> xs) => string.Join(",", xs.Select(x => x.ToString(CultureInfo.InvariantCulture)));

    /// <summary>
    /// The curve of a kept kill, or null when it carries no damage by the second (a record from before store v2) or no damage
    /// of yours. Each list is scaled so it adds up to the figure the pull's page shows (a hit promoted to the boss's after it
    /// landed, a start moved: the seconds may miss a little of it), so the finish is the pull's own DPS.
    /// </summary>
    public static GhostCurve? Of(Encounter e)
    {
        if (e.You is not { } you || e.Figure(you) <= 0) return null;
        bool boss = e.BossView;
        List<long>? Per(Fighter f) => boss ? f.BossPerSecond : f.PerSecond;
        if (Per(you) is not { Count: > 0 } mine) return null;
        var end = e.End ?? e.LastAt;
        double secs = Math.Max(Encounter.MinDpsSeconds, e.Duration(end).TotalSeconds);
        int n = Math.Max(1, (int)Math.Ceiling(secs));
        long[] Pad(List<long>? l) { var a = new long[n]; if (l != null) for (int i = 0; i < Math.Min(n, l.Count); i++) a[i] = l[i]; return a; }
        long[] Scaled(long[] a, long total)
        {
            long sum = a.Sum();
            if (sum <= 0 || sum == total) return a;
            double k = total / (double)sum;
            var r = a.Select(x => (long)Math.Round(x * k)).ToArray();
            // the rounding's remainder in the last second with damage, so the list adds up exactly
            long rest = total - r.Sum();
            for (int i = r.Length - 1; i >= 0 && rest != 0; i--) if (r[i] > 0 || i == 0) { r[i] += rest; rest = 0; }
            return r;
        }
        var y = Scaled(Pad(mine), e.Figure(you));
        var g = new long[n];
        foreach (var m in e.Members) { var p = Pad(Per(m)); for (int i = 0; i < n; i++) g[i] += p[i]; }
        g = Scaled(g, e.Credited);
        string hp = "";
        if (e.BossHpPerSecond is { Count: > 0 } hps)
        {
            double low = 1;
            var list = new long[n];
            for (int i = 0; i < n; i++) { if (i < hps.Count) low = Math.Min(low, Math.Clamp(hps[i], 0, 1)); list[i] = (long)Math.Round(low * HpUnit); }
            if (e.Outcome == "kill") list[^1] = 0;
            hp = Join(list);
        }
        return new GhostCurve
        {
            Fight = e.Id, Boss = boss, Seconds = secs,
            Dps = e.Figure(you) / secs, GroupDps = e.Credited / secs,
            You = Join(y), Group = Join(g), Hp = hp,
        };
    }

    /// <summary>
    /// The instant of the ghost's pull (seconds from its start) that stands beside yours: by the boss's HP when both pulls
    /// know it (the moment the ghost's boss was as low as yours is now: the same point in the fight, so a kill lines up with
    /// the ghost's kill), else the same time into the pull. <see cref="Seconds"/> or more: the ghost has finished.
    /// </summary>
    public (double At, bool ByHp) InstantFor(double seconds, double? hpLeft)
    {
        var hp = HpPer;
        if (hpLeft is not { } h || hp.Length == 0) return (Math.Min(seconds, Seconds), false);
        h = Math.Clamp(h, 0, 1);
        if (h >= 1) return (0, true);
        // as low as the ghost's boss ever got (its kill), or lower: the ghost's finish
        if (h <= hp[^1] + 1e-12) return (Seconds, true);
        // the points (0, full) and (i + 1, HP left at the end of second i), the HP never rising: the first second it is as low
        // as h, found by halving (a 20-minute pull is 11 steps)
        int lo = 0, hi = hp.Length - 1;
        while (lo < hi) { int mid = (lo + hi) / 2; if (hp[mid] <= h) hi = mid; else lo = mid + 1; }
        double t0 = lo, h0 = lo == 0 ? 1 : hp[lo - 1], t1 = Math.Min(lo + 1, Seconds), h1 = hp[lo];
        double at = h0 - h1 > 1e-12 ? t0 + (t1 - t0) * (h0 - h) / (h0 - h1) : t1;
        return (Math.Clamp(at, 0, Seconds), true);
    }

    /// <summary>Your DPS and the group's in the ghost's pull at <paramref name="at"/> seconds in (its damage so far over that time); its finish from <see cref="Seconds"/> on.</summary>
    public (double You, double Group) DpsAt(double at)
    {
        if (at >= Seconds - 1e-9) return (Dps, GroupDps);
        double over = Math.Max(at, Encounter.MinDpsSeconds);
        _youSum ??= Sums(YouPer); _groupSum ??= Sums(GroupPer);
        return (SoFar(YouPer, _youSum, at) / over, SoFar(GroupPer, _groupSum, at) / over);
    }

    static double[] Sums(long[] per) { var s = new double[per.Length + 1]; for (int i = 0; i < per.Length; i++) s[i + 1] = s[i] + per[i]; return s; }

    /// <summary>The damage of a per-second list up to <paramref name="at"/> seconds: whole seconds, then the share of the one under way.</summary>
    static double SoFar(long[] per, double[] sums, double at)
    {
        if (at <= 0 || per.Length == 0) return 0;
        int whole = Math.Min(per.Length, (int)Math.Floor(at));
        return sums[whole] + (whole < per.Length ? per[whole] * (at - whole) : 0);
    }
}

/// <summary>
/// Your pace against your best on this fight at one moment: your DPS beside the ghost's at the same point, and the change
/// (+0.062 is 6.2% ahead). <see cref="Final"/>: the pull is over, a kill, measured against your best before it (the record,
/// as the PB chip): <see cref="NewBest"/> when it beat it.
/// </summary>
public sealed record GhostPace(double You, double Ghost, double Delta, double? GroupDelta, bool ByHp, bool Final)
{
    public bool Ahead => Delta >= 0;
    public bool NewBest => Final && Delta > 0;
    /// <summary>Where the ghost's bar would end on your row: your bar's length times the ghost's DPS over yours.</summary>
    public double BarOf(double yourBar) => You > 0 ? Math.Clamp(yourBar * Ghost / You, 0, 1) : 0;
}

/// <summary>
/// The PB ghost (1.0.38): while you fight a boss you've killed before, the meter races your best kill of that fight (the boss
/// in that instance and mode, FightRecords.KeyOf), like a racing game's ghost: a slim line on your row's bar where you'd be at
/// your best's pace, and "+6.2% vs best" (green) or "−3.1% vs best" (red), live; at a kill, "New best by 4.1%!" or "2.3% off
/// your best", the same figure as the PB chip. Settings › DPS meter › PB ghost (on), and Group pace (off). Bosses only, and
/// never with DPS over active time (the ghost's seconds are the fight's).
/// </summary>
public static class PbGhost
{
    /// <summary>Settings › DPS meter › PB ghost (parser.json Ghost; on unless turned off). Set by the window.</summary>
    public static bool Shown { get; set; } = true;
    /// <summary>The group's pace beside yours (parser.json GhostGroup; off unless turned on).</summary>
    public static bool GroupShown { get; set; }
    /// <summary>The ghost and the best before a fight (the window's, from the records: FightRecords.GhostFor and the record's DpsBefore).</summary>
    public static Func<Encounter, (GhostCurve? Curve, double BestDps)>? Source { get; set; }
    /// <summary>No pace in a pull's first seconds: a hit or two is no pace.</summary>
    public const double FromSeconds = 3;

    // asked once a fight, so the ghost a kill raced stays its ghost after the kill set a new best
    static readonly object Gate = new();
    static readonly List<(string Id, GhostCurve? Curve, double Best)> Asked = new();

    /// <summary>Forget the ghosts asked for (a render or a test giving new ones).</summary>
    public static void Forget() { lock (Gate) Asked.Clear(); _last = null; _given++; }

    static (GhostCurve? Curve, double Best) For(Encounter e)
    {
        lock (Gate)
        {
            foreach (var a in Asked) if (a.Id == e.Id) return (a.Curve, a.Best);
        }
        (GhostCurve? c, double b) got = (null, 0);
        try { if (Source?.Invoke(e) is { } s) got = s; } catch (Exception) { got = (null, 0); }
        lock (Gate)
        {
            Asked.Add((e.Id, got.c, got.b));
            if (Asked.Count > 8) Asked.RemoveAt(0);
        }
        return got;
    }

    /// <summary>A pull the ghost races: a boss pull (not a wave, not the PvP panel) with your damage in it.</summary>
    public static bool Races(Encounter e) => e.ModeKind == ParserMode.Bosses && !e.Wave && e.Pvp == null && e.HasDamage && !e.Waiting
        && (e.Boss.Length > 0 || e.BossId > 0) && e.You is { } y && e.Figure(y) > 0 && !Encounter.ActiveBasis;

    // a draw asks for the pace three times (the frame's heat, the line, the look's signature): worked out once for it
    static (string Id, DateTime Now, DateTime? End, long Hp, long Mine, bool Shown, bool Group, bool Active, bool BossOnly, GhostPace? Pace)? _last;

    /// <summary>Your pace now (null: no ghost here, switched off, too early, a wipe).</summary>
    public static GhostPace? Of(Encounter e, DateTime now)
    {
        long mine = e.You is { } y0 ? e.Figure(y0) : -1;
        if (_last is { } l && l.Id == e.Id && l.Now == now && l.End == e.End && l.Hp == e.BossHp && l.Mine == mine && l.Shown == Shown && l.Group == GroupShown
            && l.Active == Encounter.ActiveBasis && l.BossOnly == Encounter.BossOnly) return l.Pace;
        var pace = Work(e, now);
        _last = (e.Id, now, e.End, e.BossHp, mine, Shown, GroupShown, Encounter.ActiveBasis, Encounter.BossOnly, pace);
        return pace;
    }

    static GhostPace? Work(Encounter e, DateTime now)
    {
        if (!Shown || !Races(e)) return null;
        var (curve, best) = For(e);
        if (!e.Live)
        {
            // over: a kill against the best before it (the record, as the PB chip says it); a wipe or leaving says nothing
            if (e.Outcome != "kill" || best <= 0) return null;
            var end = e.End ?? now;
            double you = e.Dps(e.You!, end);
            double? group = curve is { GroupDps: > 0 } c0 && c0.Boss == e.BossView ? e.GroupDps(end) / c0.GroupDps - 1 : null;
            return new GhostPace(you, best, you / best - 1, group, curve?.HpPer.Length > 0, Final: true);
        }
        if (curve == null) return null;
        return LiveAt(e, now, curve);
    }

    /// <summary>
    /// The pace of a pull at <paramref name="now"/> against <paramref name="curve"/>, as the live meter works it out (also for a
    /// kept pull at its kill, the bench's check that the race ends on the final figures).
    /// </summary>
    public static GhostPace? LiveAt(Encounter e, DateTime now, GhostCurve curve)
    {
        if (e.You is not { } me || curve.Boss != e.BossView || curve.Dps <= 0) return null;
        double t = e.DpsSeconds(now);
        if (t < FromSeconds && e.Live) return null;
        var (at, byHp) = curve.InstantFor(t, HpLow(e, now));
        if (at < Encounter.MinDpsSeconds) return null;   // the boss hasn't fallen yet where the ghost's had: no pace yet
        var (gy, gg) = curve.DpsAt(at);
        double you = e.Dps(me, now);
        if (gy <= 0) return null;
        double? group = gg > 0 ? e.GroupDps(now) / gg - 1 : null;
        return new GhostPace(you, gy, you / gy - 1, group, byHp, Final: false);
    }

    /// <summary>The boss's HP left, at its lowest so far (a heal back up doesn't move the race back); null when not known.</summary>
    static double? HpLow(Encounter e, DateTime now)
    {
        if (e.BossHpLeft is not { } h) return null;
        if (!e.Live && e.Outcome == "kill") return 0;
        if (e.BossHpPerSecond is { Count: > 0 } hps)
        {
            int upTo = Math.Min(hps.Count, e.SecondOf(now) + 1);
            for (int i = 0; i < upTo; i++) h = Math.Min(h, hps[i]);
        }
        return Math.Clamp(h, 0, 1);
    }

    /// <summary>
    /// The ghost in a draw's signature (a look draws again when it changes): the switches, and a count that moves when the
    /// ghosts are given afresh (a render's or a test's). Never the pace itself: it moves only with the damage and the time,
    /// which draw the look again already, and a signature that changed with each tenth of a percent drew whole looks again for
    /// nothing (meter-load, 2026-11-20: +0.1 of a core).
    /// </summary>
    public static string Sig(Encounter? e, DateTime now) => !Shown ? "" : GroupShown ? "gg" + _given : "g" + _given;
    static int _given;

    // ---- the words (every look says them the same)

    /// <summary>"+6.2%" or "−3.1%" (a true minus).</summary>
    public static string Pct(double delta) => (delta >= 0 ? "+" : "−") + (Math.Abs(delta) * 100).ToString("0.0", CultureInfo.InvariantCulture) + "%";

    /// <summary>The chip's words: live "+6.2% vs best"; a kill "New best by 4.1%!" or "2.3% off your best".</summary>
    public static string Words(GhostPace p) => !p.Final ? Ui.L.F("{0} vs best", Pct(p.Delta))
        : p.NewBest ? Ui.L.F("New best by {0}!", Pct(p.Delta).TrimStart('+'))
        : Ui.L.F("{0} off your best", Pct(p.Delta).TrimStart('−', '+'));

    /// <summary>The chip's short words, when the full ones don't fit: "+6.2%", "PB +4.1%", "−2.3%".</summary>
    public static string Short(GhostPace p) => p.NewBest ? Ui.L.F("PB {0}", Pct(p.Delta)) : Pct(p.Delta);

    /// <summary>The group's pace beside it: "group +3.0%".</summary>
    public static string GroupWords(double delta) => Ui.L.F("group {0}", Pct(delta));

    /// <summary>What the chip's tooltip says: the two figures and how they stand beside each other.</summary>
    public static string Tip(GhostPace p) => p.Final
        ? Ui.L.F("Your DPS {0} against your best here, {1}.", Ui.Amounts.Meter(p.You), Ui.Amounts.Meter(p.Ghost))
        : Ui.L.F("Your DPS {0}; your best kill here had {1} at this point of the fight.", Ui.Amounts.Meter(p.You), Ui.Amounts.Meter(p.Ghost))
          + (p.ByHp ? " " + Ui.L.T("Lined up by the boss's HP.") : " " + Ui.L.T("Lined up by the time into the pull."));
}
