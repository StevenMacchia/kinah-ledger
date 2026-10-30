using System.Globalization;
using System.Text.Json;
using KinahLedger.Data;

namespace KinahLedger.Session;

/// <summary>
/// One goal (0.9, Goals B of kinah-bench/mock-goals-history, Steven's pick 2026-10-29): Kinah from farming, an item, or
/// runs, to reach in a period (today, from midnight on this PC, is the only one so far). Kept in goals.json until removed;
/// each day's progress is that day's count.
/// </summary>
public sealed class Goal
{
    public const string KindKinah = "kinah", KindItem = "item", KindRuns = "runs";
    public const string AnyRun = "any", Expeditions = "expeditions", Events = "events";
    public const string Today = "today";

    public string Kind { get; set; } = KindKinah;
    /// <summary>How much: Kinah, pieces of the item, or runs.</summary>
    public long Target { get; set; }
    /// <summary>The item's name as the item list writes it (item goals).</summary>
    public string Name { get; set; } = "";
    /// <summary>Which runs count (runs goals): expeditions, events, or any run.</summary>
    public string Which { get; set; } = AnyRun;
    /// <summary>
    /// The period it counts over. Only "today" is built: a "week" would sum the day-history store's days with
    /// <see cref="GoalCounts.Sum"/> and be measured by the same <see cref="Goals.Progress"/>.
    /// </summary>
    public string Period { get; set; } = Today;
    /// <summary>The day ("2026-10-29", DayStore.DayOf) the tracker last said this goal was reached: once a day, also across a restart.</summary>
    public string SaidDay { get; set; } = "";

    public Goal Copy() => (Goal)MemberwiseClone();
}

/// <summary>How far one goal is: what the period has, the target, and the seconds to go at the period's pace (null with no pace yet).</summary>
public sealed record GoalProgress(long Have, long Target, double? SecondsToGo)
{
    public bool Done => Target > 0 && Have >= Target;
    public long ToGo => Math.Max(0, Target - Have);
    /// <summary>0 to 1, for the ring and the bar.</summary>
    public double Fraction => Target <= 0 ? 0 : Math.Clamp(Have / (double)Target, 0, 1);
}

/// <summary>
/// What one period counted, the figures goals are measured against. <see cref="Day"/> builds today's from the Today count
/// and today's runs; a week (not built) would be <see cref="Sum"/> over the day-history store's days plus today's, read by
/// the same <see cref="Goals.Progress"/>.
/// </summary>
/// <param name="Kinah">Loose Kinah from farming: the Today count's (LOOT's Today side), never bound Kinah or a run's rewards.</param>
/// <param name="FarmSeconds">Time farming (the Today count's active time), what the Kinah's and items' pace divide by.</param>
/// <param name="Farmed">Items picked up out in the world (drops and gathering, bound or not), by item id.</param>
/// <param name="FromRuns">Items from the reward screens of the period's runs (the counted ones, as the Events page's loot), by item id.</param>
/// <param name="RunSeconds">Seconds since the period's first run started (any run, expeditions, events): the runs' pace.</param>
public sealed record GoalCounts(long Kinah, double FarmSeconds, IReadOnlyDictionary<long, long> Farmed, IReadOnlyDictionary<long, long> FromRuns,
    int Runs, int Expeditions, int Events, double RunSeconds, double ExpeditionSeconds, double EventSeconds)
{
    public static readonly GoalCounts None = new(0, 0, new Dictionary<long, long>(), new Dictionary<long, long>(), 0, 0, 0, 0, 0, 0);

    /// <summary>How many of an item the period has: picked up farming and gathering, and from runs' reward screens.</summary>
    public long ItemCount(long id) => Farmed.GetValueOrDefault(id) + FromRuns.GetValueOrDefault(id);

    /// <param name="day">The Today count (GameWatcher.DayView), or null before there is one.</param>
    /// <param name="runs">Today's runs (TodayLoot.IsToday), the ones a pending Clear hides left out.</param>
    public static GoalCounts Day(SessionEngine? day, IReadOnlyList<InstanceRun> runs, DateTime nowUtc)
    {
        long kinah = 0; double farm = 0;
        var farmed = new Dictionary<long, long>();
        if (day != null)
        {
            // the same Kinah the Today side of LOOT shows (TodayLoot.Farm, every zone): loose Kinah picked up out in the world;
            // bound Kinah is KinahBound, and a run's Kinah goes to its record (InstanceLog), never to this count
            kinah = day.Totals().Kinah;
            farm = day.Active.TotalSeconds;
            foreach (var (id, n) in Copy(day.Items)) farmed[id] = farmed.GetValueOrDefault(id) + n;
            foreach (var (id, n) in Copy(day.ItemsBound)) farmed[id] = farmed.GetValueOrDefault(id) + n;
        }
        // an item goal also counts what today's runs gave (cheap: the records are in memory for the Events page): the
        // counted runs' reward screens, tradeable and bound, as TodayLoot.Runs counts their loot. A run's loot never reaches
        // the Today count (TrackerSession routes it to the run), so nothing is counted twice
        var fromRuns = new Dictionary<long, long>();
        foreach (var r in runs.Where(r => !r.OutOfAverages))
        {
            foreach (var (id, n) in Copy(r.Items)) fromRuns[id] = fromRuns.GetValueOrDefault(id) + n;
            foreach (var (id, n) in Copy(r.BoundItems)) fromRuns[id] = fromRuns.GetValueOrDefault(id) + n;
        }
        // every run counts as a run, flagged or not (the Events page's line counts them the same way)
        static bool Is(InstanceRun r, string kind) => string.Equals(r.Kind, kind, StringComparison.OrdinalIgnoreCase);
        var exp = runs.Where(r => Is(r, "Expedition")).ToList();
        var ev = runs.Where(r => Is(r, "Event")).ToList();
        double Since(List<InstanceRun> l) => l.Count == 0 ? 0 : Math.Max(0, (nowUtc - l.Min(r => r.Start)).TotalSeconds);
        return new GoalCounts(kinah, farm, farmed, fromRuns, runs.Count, exp.Count, ev.Count, Since(runs.ToList()), Since(exp), Since(ev));
    }

    /// <summary>Several periods as one (what a week's goals would read: the day-history store's days and today).</summary>
    public static GoalCounts Sum(IEnumerable<GoalCounts> days)
    {
        long kinah = 0; double farm = 0, rs = 0, es = 0, vs = 0; int runs = 0, exp = 0, ev = 0;
        var farmed = new Dictionary<long, long>(); var fromRuns = new Dictionary<long, long>();
        foreach (var d in days)
        {
            kinah += d.Kinah; farm += d.FarmSeconds; runs += d.Runs; exp += d.Expeditions; ev += d.Events; rs += d.RunSeconds; es += d.ExpeditionSeconds; vs += d.EventSeconds;
            foreach (var (id, n) in d.Farmed) farmed[id] = farmed.GetValueOrDefault(id) + n;
            foreach (var (id, n) in d.FromRuns) fromRuns[id] = fromRuns.GetValueOrDefault(id) + n;
        }
        return new GoalCounts(kinah, farm, farmed, fromRuns, runs, exp, ev, rs, es, vs);
    }

    /// <summary>A copy of a count the reader may be adding to on its own thread (a run still open): tried again if it changed mid-copy.</summary>
    static List<KeyValuePair<long, int>> Copy(Dictionary<long, int> d)
    {
        for (int i = 0; ; i++)
        {
            try { return d.ToList(); }
            catch (InvalidOperationException) when (i < 3) { }
            catch (InvalidOperationException) { return new(); }
        }
    }
}

/// <summary>
/// Goals (0.9): up to three, kept in goals.json beside today.json (CrashLog.Dir) until removed, never sent anywhere. Each is
/// measured against its period's <see cref="GoalCounts"/>; reaching one is said once a day (Goal.SaidDay, saved), so a
/// restart never says it again. No sound, ever.
/// </summary>
public static class Goals
{
    public const int Max = 3;

    public static string PathFor() => Path.Combine(CrashLog.Dir, "goals.json");

    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    /// <summary>A goal that can be shown: a kind it knows, an amount, a name for an item, a period that is built.</summary>
    public static bool Valid(Goal? g) => g != null && g.Target > 0 && g.Period == Goal.Today
        && (g.Kind == Goal.KindKinah || (g.Kind == Goal.KindItem && g.Name.Trim().Length > 0) || (g.Kind == Goal.KindRuns && g.Which is Goal.AnyRun or Goal.Expeditions or Goal.Events));

    /// <summary>The goals as goals.json holds them: the ones that can be shown, at most <see cref="Max"/>; none with no file or a bad one.</summary>
    public static List<Goal> Load(string? path = null)
    {
        try
        {
            var p = path ?? PathFor();
            if (!File.Exists(p)) return new();
            var list = JsonSerializer.Deserialize<List<Goal?>>(File.ReadAllText(p), Json) ?? new();
            return list.Where(Valid).Select(g => g!).Take(Max).ToList();
        }
        catch (Exception ex) { CrashLog.Write("goals load", ex); return new(); }
    }

    /// <summary>Writes the goals (to a temporary file first, so a crash mid-write never leaves half a file).</summary>
    public static void Save(IReadOnlyList<Goal> goals, string? path = null)
    {
        try
        {
            var p = path ?? PathFor();
            Directory.CreateDirectory(Path.GetDirectoryName(p)!);
            var tmp = p + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(goals.Take(Max).ToList(), Json));
            File.Move(tmp, p, overwrite: true);
        }
        catch (Exception ex) { CrashLog.Write("goals save", ex); }
    }

    /// <summary>Adds a goal; false (and nothing added) with <see cref="Max"/> already set or a goal that can't be shown.</summary>
    public static bool Add(List<Goal> goals, Goal g)
    {
        if (goals.Count >= Max || !Valid(g)) return false;
        goals.Add(g);
        return true;
    }

    /// <summary>How far a goal is in the counts given (today's, or a longer period's), and the time to go at their pace.</summary>
    public static GoalProgress Progress(Goal g, GoalCounts c, ItemDictionary items)
    {
        long have; double? rate = null;   // per second
        switch (g.Kind)
        {
            case Goal.KindItem:
            {
                var id = items.IdOf(g.Name);
                long farmed = c.Farmed.GetValueOrDefault(id);
                have = c.ItemCount(id);
                // the pace is farming's: a run's rewards come in lumps, so an item only runs give has no pace
                if (farmed > 0 && c.FarmSeconds >= 60) rate = farmed / c.FarmSeconds;
                break;
            }
            case Goal.KindRuns:
            {
                var (n, secs) = g.Which switch
                {
                    Goal.Expeditions => ((long)c.Expeditions, c.ExpeditionSeconds),
                    Goal.Events => ((long)c.Events, c.EventSeconds),
                    _ => ((long)c.Runs, c.RunSeconds),
                };
                have = n;
                if (have > 0 && secs >= 60) rate = have / secs;
                break;
            }
            default:
                have = c.Kinah;
                if (have > 0 && c.FarmSeconds >= 60) rate = have / c.FarmSeconds;
                break;
        }
        long togo = Math.Max(0, g.Target - have);
        return new GoalProgress(have, g.Target, togo > 0 && rate is > 0 ? togo / rate : null);
    }

    /// <summary>"about 1 h 10 min" at the period's pace; "" when reached or with no pace yet (the card leaves the line out).</summary>
    public static string PaceText(GoalProgress p)
    {
        if (p.Done || p.SecondsToGo is not double s) return "";
        return s < 60 ? "under a minute" : "about " + TodayLoot.Dur(s);
    }

    /// <summary>
    /// A goal just set or changed: one already reached in today's count counts as said, so the panel never announces
    /// something done before the goal was set. One not reached yet will be said when it is.
    /// </summary>
    public static void Settle(Goal g, GoalProgress p, string day) => g.SaidDay = p.Done ? day : "";

    /// <summary>
    /// The goals reached and not yet said on <paramref name="day"/>, each marked said (the caller saves goals.json, so a
    /// restart doesn't say them again). Once per goal per day.
    /// </summary>
    public static List<Goal> JustReached(IReadOnlyList<Goal> goals, Func<Goal, GoalCounts> counts, ItemDictionary items, string day)
    {
        var said = new List<Goal>();
        foreach (var g in goals)
        {
            if (g.SaidDay == day || !Progress(g, counts(g), items).Done) continue;
            g.SaidDay = day;
            said.Add(g);
        }
        return said;
    }

    /// <summary>The slot's short name: "Kinah from farming", the item's name, "Expeditions".</summary>
    public static string Label(Goal g) => g.Kind switch
    {
        Goal.KindItem => g.Name,
        Goal.KindRuns => g.Which switch { Goal.Expeditions => "Expeditions", Goal.Events => "Events", _ => "Runs" },
        _ => "Kinah from farming",
    };

    /// <summary>An amount of the goal's thing in words: "300K from farming", "20 Spiritstone", "8 expeditions".</summary>
    public static string What(Goal g, long n, Func<double, string> fk) => g.Kind switch
    {
        Goal.KindItem => $"{N(n)} {g.Name}",
        Goal.KindRuns => $"{N(n)} {RunWord(g.Which, n)}",
        _ => $"{fk(n)} from farming",
    };

    /// <summary>What the panel and the status line say once: "Goal reached · 300K from farming".</summary>
    public static string ReachedText(Goal g, Func<double, string> fk) => "Goal reached · " + What(g, g.Target, fk);

    static string RunWord(string which, long n) => which switch
    {
        Goal.Expeditions => n == 1 ? "expedition" : "expeditions",
        Goal.Events => n == 1 ? "event" : "events",
        _ => n == 1 ? "run" : "runs",
    };

    static string N(long n) => n.ToString("N0", CultureInfo.InvariantCulture);

    /// <summary>An amount as typed: "300K", "1.5M", "20", "1,000". Null for anything else, nothing, or zero.</summary>
    public static long? ParseAmount(string text)
    {
        var s = (text ?? "").Trim().Replace(",", "").Replace(" ", "").Replace("_", "");
        if (s.Length == 0) return null;
        double mult = 1;
        switch (char.ToUpperInvariant(s[^1]))
        {
            case 'K': mult = 1e3; s = s[..^1]; break;
            case 'M': mult = 1e6; s = s[..^1]; break;
            case 'B': mult = 1e9; s = s[..^1]; break;
        }
        if (!double.TryParse(s, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var v)) return null;
        var n = (long)Math.Round(v * mult);
        return n is > 0 and <= 1_000_000_000_000 ? n : null;
    }
}
