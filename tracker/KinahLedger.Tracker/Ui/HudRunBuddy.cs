namespace KinahLedger.Ui;

/// <summary>
/// The Buddy's dungeon-run values (MainWindow.HudRun.cs works them out) as the looks read them, and their words. One reading
/// for every look that draws the run, so the figures and their wording never differ between looks.
/// </summary>
static class RunBuddy
{
    /// <summary>
    /// "runState" ("run", "clear", "between" or ""), "runPlace", "runMode", "runClock" (the run's clock, or its clear), "runBest"
    /// (your fastest clear here in this mode), "runPace" ("−0:12", "+0:07", "±0:00") and "runPaceSign" (-1 ahead, 1 behind, 0),
    /// "runToday" (this run's number today), "runClears" (clears here in this mode), "splits" (the last two bosses down: lines
    /// of boss, split, delta, sign or "best"), "runVerdict" ("best", "rank", "off", "first"), "runRank",
    /// "runKinah" (the run's worth once its rewards were read), "runAvg" (your average run here), and between runs
    /// "runTodayRuns", "runTodayKinah" and "runTodayAvg" (today here: runs, worth, average clear).
    /// </summary>
    public static readonly string[] Keys = { "runState", "runPlace", "runMode", "runClock", "runBest", "runPace", "runPaceSign", "runToday", "runClears", "splits", "runVerdict", "runRank", "runKinah", "runAvg", "runTodayRuns", "runTodayKinah", "runTodayAvg" };

    public sealed record Split(string Boss, string Clock, string Delta, int Sign, bool Best);

    public sealed record State(string Kind, string Place, string Mode, string Clock, string Best, string Pace, int PaceSign, string Today, string Clears,
        IReadOnlyList<Split> Splits, string Verdict, int Rank, string Kinah, string Avg, string TodayRuns, string TodayKinah, string TodayAvg)
    {
        public bool Run => Kind == "run";
        public bool Clear => Kind == "clear";
        public bool Between => Kind == "between";
        /// <summary>The dungeon and its mode: "Ferocious Horn Den · Easy".</summary>
        public string Head => Mode.Length > 0 ? $"{Place} · {Mode}" : Place;
    }

    /// <summary>The run state in <paramref name="v"/>, or null out of a run (and while the reader is paused, a drop is held, crafting or in the Abyss: those keep their own lines).</summary>
    public static State? Of(IReadOnlyDictionary<string, string> v)
    {
        string V(string k) => v.TryGetValue(k, out var s) ? s : "";
        var kind = V("runState");
        if (kind is not ("run" or "clear" or "between") || V("runPlace").Length == 0) return null;
        if (V("abyss") == "1" || V("craft") == "1") return null;
        var splits = new List<Split>();
        foreach (var line in V("splits").Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var p = line.Split('\t');
            if (p.Length < 4) continue;
            splits.Add(new Split(p[0], p[1], p[2], p[3] == "best" ? -1 : int.TryParse(p[3], out var s) ? s : 0, p[3] == "best"));
        }
        return new State(kind, V("runPlace"), V("runMode"), V("runClock"), V("runBest"), V("runPace"), int.TryParse(V("runPaceSign"), out var ps) ? ps : 0, V("runToday"), V("runClears"),
            splits, V("runVerdict"), int.TryParse(V("runRank"), out var r) ? r : 0, V("runKinah"), V("runAvg"), V("runTodayRuns"), V("runTodayKinah"), V("runTodayAvg"));
    }

    /// <summary>A sign of the run values, for a look's redraw check (the clock changes it every second).</summary>
    public static string Sig(IReadOnlyDictionary<string, string> v) => string.Join("|", Keys.Select(k => v.TryGetValue(k, out var s) ? s : ""));

    /// <summary>
    /// The count line: "Run 3 today · 38 clears" (each part only with its figure); between runs your clears and fastest
    /// ("38 clears · fastest 4:12", the clears alone when the figures show the fastest).
    /// </summary>
    public static string Count(State s)
    {
        if (s.Between) return s.TodayRuns.Length > 0 ? LifetimeLine(s) : s.Clears.Length == 0 ? "" : s.Clears == "1" ? L.T("1 clear") : L.F("{0} clears", s.Clears);
        var parts = new List<string>();
        if (s.Today.Length > 0 && !s.Between) parts.Add(L.F("Run {0} today", s.Today));
        if (s.Clears.Length > 0) parts.Add(s.Clears == "1" ? L.T("1 clear") : L.F("{0} clears", s.Clears));
        return string.Join(" · ", parts);
    }

    /// <summary>The clear's verdict in words: "NEW BEST", "2nd fastest", "First clear here", else your best ("Best 4:12").</summary>
    public static string Verdict(State s) => s.Verdict switch
    {
        "best" => L.T("NEW BEST"),
        "first" => L.T("First clear here"),
        "rank" => s.Rank switch { 2 => L.T("2nd fastest"), 3 => L.T("3rd fastest"), 4 => L.T("4th fastest"), _ => L.T("5th fastest") },
        _ => s.Best.Length > 0 ? L.F("Best {0}", s.Best) : "",
    };

    /// <summary>Between runs, today here: "2 runs · 335K · avg 4:26" (each part only with its figure); "" with no run here today.</summary>
    public static string TodayLine(State s)
    {
        if (s.TodayRuns.Length == 0) return "";
        var parts = new List<string> { s.TodayRuns == "1" ? L.T("1 run") : L.F("{0} runs", s.TodayRuns) };
        if (s.TodayKinah.Length > 0) parts.Add(s.TodayKinah);
        if (s.TodayAvg.Length > 0) parts.Add(L.F("avg {0}", s.TodayAvg));
        return string.Join(" · ", parts);
    }

    /// <summary>Between runs, for all time: "38 clears · fastest 4:12"; "" before the first clear.</summary>
    public static string LifetimeLine(State s)
    {
        var parts = new List<string>();
        if (s.Clears.Length > 0) parts.Add(s.Clears == "1" ? L.T("1 clear") : L.F("{0} clears", s.Clears));
        if (s.Best.Length > 0) parts.Add(L.F("fastest {0}", s.Best));
        return string.Join(" · ", parts);
    }
}
