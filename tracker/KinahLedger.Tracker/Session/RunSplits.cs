using KinahLedger.Parser;

namespace KinahLedger.Session;

/// <summary>
/// A dungeon run as a speedrun (Steven, 2026-11-18: "when in a run maybe the buddy should switch to better information about
/// the run too? like maybe we can say how many completions, fastest run, etc"). Built only from what is stored: the runs log
/// (instances.jsonl: where a run started, what it made) and the kept boss fights (encounters.jsonl: each boss's kill, the
/// instance and its mode). A run's split for a boss is the boss's kill (the fight's end) less the run's start; its clear is the
/// final boss's split (bosses.txt "final"). Runs compare only with runs of the same instance in the same mode ("Easy",
/// "Conquest", a Transcendence's stage), a mode not known being a mode of its own. Apart from the window so the self-test can
/// check the arithmetic.
/// </summary>
public static class RunSplits
{
    /// <summary>A boss down in a run: its name, its split (seconds from the run's start) and whether it clears the instance.</summary>
    public sealed record Kill(string Boss, double At, bool Final);

    /// <summary>
    /// An earlier run of the place: its start, mode, bosses down, worth, whether its worth counts in averages and it was today,
    /// and whether its times hold (<see cref="KillsIn"/>: no boss pulled before the run's logged start).
    /// </summary>
    public sealed record Past(DateTime Start, string Mode, IReadOnlyList<Kill> Kills, long Value, bool Counted, bool Today, bool Timed = true)
    {
        /// <summary>The final boss died in it: a clear, timed or not.</summary>
        public bool Cleared => Kills.Any(k => k.Final);
        /// <summary>Its clear time (the final boss's split), or null when it wasn't cleared or its times don't hold.</summary>
        public double? Clear => Timed && Kills.FirstOrDefault(k => k.Final) is { At: >= MinClearSeconds } k ? k.At : null;
        public double? SplitOf(string boss) => Timed ? Kills.FirstOrDefault(k => SameBoss(k.Boss, boss))?.At : null;
    }

    /// <summary>A clear quicker than this is a run whose start was missed: never a best.</summary>
    public const double MinClearSeconds = 20;
    /// <summary>A boss pulled more than this before the run's logged start: the run was logged late, its times would read short.</summary>
    public const double StartSlackSeconds = 2;
    /// <summary>The verdict shows this long after the final boss died, and until this long after the rewards were read when they came within <see cref="PaidWaitSeconds"/>.</summary>
    public const double ClearHoldSeconds = 20, PaidHoldSeconds = 8, PaidWaitSeconds = 60;
    /// <summary>A run with no run after it takes kills until this long after its last sign of life.</summary>
    public const double TailMinutes = 2;
    /// <summary>The verdict names the place among the clears up to this rank ("3rd fastest"); further down it gives the best.</summary>
    public const int RankShown = 5;

    public static bool SamePlace(string a, string b) => BossBook.Strip(a ?? "").Trim().Equals(BossBook.Strip(b ?? "").Trim(), StringComparison.OrdinalIgnoreCase);
    public static bool SameBoss(string a, string b) => string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
    public static bool SameMode(string a, string b) => string.Equals((a ?? "").Trim(), (b ?? "").Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>The boss kills kept for <paramref name="place"/>, oldest first: named bosses (not wave stages) that died.</summary>
    public static List<Encounter> KillsOf(IEnumerable<Encounter> fights, string place) =>
        fights.Where(e => e.ModeKind == ParserMode.Bosses && e.Outcome == "kill" && !e.Wave && e.End != null && e.Boss.Trim().Length > 0 && SamePlace(e.Instance, place))
              .OrderBy(e => e.End).ToList();

    /// <summary>
    /// The bosses down in [<paramref name="from"/>, <paramref name="until"/>) from <paramref name="kills"/> (KillsOf), each boss
    /// once (its first kill), the run's mode as its fights named it (the one most of them named; "" when none did), and whether
    /// its times hold: a boss pulled before <paramref name="from"/> means the run was logged late (Steven's Ferocious Horn Den
    /// run of 2026-11-16 20:21 had Kwapo pulled 7 s before it and down 11 s after: a 3:41 clear that was a minute longer).
    /// </summary>
    public static (List<Kill> Kills, string Mode, bool Timed) KillsIn(IReadOnlyList<Encounter> kills, DateTime from, DateTime until)
    {
        var list = new List<Kill>();
        var modes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        bool timed = true;
        foreach (var e in kills)
        {
            var end = e.End!.Value;
            if (end < from || end >= until) continue;
            if (e.GameMode.Trim().Length > 0) modes[e.GameMode.Trim()] = modes.GetValueOrDefault(e.GameMode.Trim()) + 1;
            if (list.Any(k => SameBoss(k.Boss, e.Boss))) continue;
            if ((from - e.Start).TotalSeconds > StartSlackSeconds) timed = false;
            list.Add(new Kill(e.Boss.Trim(), (end - from).TotalSeconds, e.FinalBoss));
        }
        var mode = modes.Count == 0 ? "" : modes.OrderByDescending(m => m.Value).First().Key;
        return (list, mode, timed);
    }

    /// <summary>
    /// The earlier runs of <paramref name="place"/> with their bosses: each logged run (<paramref name="runs"/>, every place,
    /// so a run ends where the next one starts) takes the kills from its start to the next run's start (the last one to a few
    /// minutes after its last sign of life). The run under way (<paramref name="current"/>'s start) bounds the one before it
    /// and is left out.
    /// </summary>
    public static List<Past> History(IEnumerable<InstanceRun> runs, IReadOnlyList<Encounter> kills, string place, Func<InstanceRun, long> value, DateTime midnightLocal, DateTime? current)
    {
        var starts = runs.Select(r => r.Start).Concat(current is DateTime c ? new[] { c } : Array.Empty<DateTime>()).Distinct().OrderBy(t => t).ToList();
        var list = new List<Past>();
        foreach (var r in runs.OrderBy(r => r.Start))
        {
            if (current is DateTime cur && r.Start == cur) continue;
            if (!SamePlace(r.Zone, place) && !(r.Family.Length > 0 && SamePlace(r.Family, place))) continue;
            int i = starts.BinarySearch(r.Start);
            var last = new[] { r.Left, r.LastLoot, r.PaidAt }.Where(t => t != null).Select(t => t!.Value).DefaultIfEmpty(r.Start).Max();
            var until = i >= 0 && i + 1 < starts.Count ? starts[i + 1] : last.AddMinutes(TailMinutes);
            if (until > r.Start.AddHours(3)) until = r.Start.AddHours(3);
            var (k, mode, timed) = KillsIn(kills, r.Start, until);
            list.Add(new Past(r.Start, mode, k, value(r), !r.OutOfAverages && r.LastLoot != null, TodayLoot.IsToday(r, midnightLocal), timed));
        }
        return list;
    }

    /// <summary>A split as the Buddy shows it: the boss, its time, how it stands against your best split for that boss here (null: none to compare), and a new best split.</summary>
    public sealed record SplitLine(string Boss, double At, double? Delta, bool BestSplit);

    /// <summary>What the Buddy says of a run here, in this mode.</summary>
    public sealed record View
    {
        /// <summary>Your fastest clear here in this mode before this run (seconds), null for none.</summary>
        public double? Best { get; init; }
        /// <summary>The run's pace against that fastest run: its splits where it has them, else its clear (seconds: below 0 ahead, above 0 behind; null: nothing to say yet).</summary>
        public double? Pace { get; init; }
        public List<SplitLine> Splits { get; init; } = new();
        /// <summary>This run's clear (null until the final boss died), its place among the clears here (1: a new best) and whether it is the first.</summary>
        public double? Clear { get; init; }
        public int Rank { get; init; }
        public bool FirstClear { get; init; }
        /// <summary>Clears here in this mode, this one included (timed or not: the final boss died).</summary>
        public int Clears { get; init; }
        /// <summary>The average worth of the earlier counted runs here in this mode (null: fewer than <see cref="ZoneCompare.RunEarlierMin"/>).</summary>
        public double? AvgValue { get; init; }
        /// <summary>Today here in this mode, earlier runs only: runs, their worth (counted ones), the average clear (null: no clear today).</summary>
        public int TodayRuns { get; init; }
        public long TodayValue { get; init; }
        public double? TodayAvgClear { get; init; }
    }

    /// <summary>
    /// The run under way (or just cleared) at <paramref name="clock"/> seconds with <paramref name="kills"/> down, against
    /// <paramref name="history"/> (History of the place). <paramref name="kills"/> empty and clock 0: the place between runs.
    /// A run whose times don't hold (<paramref name="timed"/> false, KillsIn) is set against nothing: no deltas, no pace, no rank.
    /// </summary>
    public static View Now(string mode, double clock, IReadOnlyList<Kill> kills, IReadOnlyList<Past> history, bool timed = true)
    {
        var same = history.Where(p => SameMode(p.Mode, mode)).ToList();
        var clears = same.Where(p => p.Clear != null).ToList();
        int cleared = same.Count(p => p.Cleared);
        var pb = clears.OrderBy(p => p.Clear).FirstOrDefault();
        double? best = pb?.Clear;
        var splits = new List<SplitLine>();
        foreach (var k in kills)
        {
            var earlier = same.Select(p => p.SplitOf(k.Boss)).Where(x => x != null).Select(x => x!.Value).ToList();
            double? bestSplit = timed && earlier.Count > 0 ? earlier.Min() : null;
            splits.Add(new SplitLine(k.Boss, k.At, bestSplit is double b ? k.At - b : null, bestSplit is double b2 && k.At < b2));
        }
        double? clear = kills.FirstOrDefault(k => k.Final) is { At: >= MinClearSeconds } fk ? fk.At : null;
        // the pace (a speedrun timer's): the last boss down against the fastest run's split for it; once the clock passes the
        // fastest run's next split without that boss down, the time lost so far. With no splits of it to go by, its clear.
        double? pace = null;
        if (pb != null && timed)
        {
            if (clear is double c) pace = c - pb.Clear!.Value;
            else
            {
                foreach (var k in kills) if (pb.SplitOf(k.Boss) is double at) pace = k.At - at;
                var next = pb.Kills.Where(p => !kills.Any(k => SameBoss(k.Boss, p.Boss))).OrderBy(p => p.At).FirstOrDefault();
                if (next != null && clock > next.At) pace = Math.Max(pace ?? double.MinValue, clock - next.At);
            }
        }
        var counted = same.Where(p => p.Counted).ToList();
        var today = same.Where(p => p.Today).ToList();
        var todayClears = today.Where(p => p.Clear != null).Select(p => p.Clear!.Value).ToList();
        return new View
        {
            Best = best, Pace = pace, Splits = splits, Clear = clear,
            Rank = clear is double cc && timed ? 1 + clears.Count(p => p.Clear < cc) : 0,
            FirstClear = clear != null && cleared == 0,
            Clears = cleared + (clear != null ? 1 : 0),
            AvgValue = counted.Count >= ZoneCompare.RunEarlierMin ? counted.Average(p => (double)p.Value) : null,
            TodayRuns = today.Count, TodayValue = today.Where(p => p.Counted).Sum(p => p.Value),
            TodayAvgClear = todayClears.Count > 0 ? todayClears.Average() : null,
        };
    }

    /// <summary>A boss's short name for a tight line: its last word ("Watchdog Kwapo" is "Kwapo"), the whole name when that is short or one word.</summary>
    public static string Short(string boss)
    {
        var parts = boss.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return boss.Trim();
        var last = parts[^1];
        return last.Length >= 4 && char.IsLetter(last[0]) ? last : boss.Trim();
    }
}
