using KinahLedger.Ocr;
using KinahLedger.Parser;
using KinahLedger.Session;

namespace KinahLedger.Ui;

/// <summary>
/// The Buddy in a dungeon run (Steven, 2026-11-18: "when in a run maybe the buddy should switch to better information about the
/// run too? like maybe we can say how many completions, fastest run, etc"; the set he approved: "Yes, build those"). Worked
/// out here and handed to the looks as the "run*" values (RunBuddy.Keys), from the runs log and the kept boss fights only
/// (Session/RunSplits.cs):
/// <list type="bullet">
/// <item>"run": the run clock against your fastest clear here in this mode, its pace, the last bosses' splits;</item>
/// <item>"clear": for a few seconds after the final boss died, the clear and how it ranks, and the run's worth against your average run here;</item>
/// <item>"between": in the dungeon with no run going, today here and your clears and fastest here.</item>
/// </list>
/// No figure without data behind it: a first run here has the clock alone and says so.
/// </summary>
public partial class MainWindow
{
    /// <summary>What the run values are made from (the live state, or the renders' sample).</summary>
    internal sealed record RunBuddyFacts(string State, string Place, string Mode, double Clock, int RunToday, RunSplits.View View, long? RunValue);

    /// <summary>A signed clock: "−0:12" ahead, "+0:07" behind (a true minus).</summary>
    internal static string SignedClock(double seconds) => (seconds < 0 ? "−" : "+") + BossParser.Clock(TimeSpan.FromSeconds(Math.Round(Math.Abs(seconds))));

    /// <summary>-1 ahead, 1 behind, 0 level (inside half a second).</summary>
    static int SignOf(double seconds) => Math.Abs(seconds) < .5 ? 0 : Math.Sign(seconds);

    static string RunClock(double seconds) => BossParser.Clock(TimeSpan.FromSeconds(Math.Max(0, Math.Floor(seconds))));

    /// <summary>The looks' run values from <paramref name="f"/> (every key, "" for none); null facts: all "".</summary>
    internal static Dictionary<string, string> RunBuddyValues(RunBuddyFacts? f)
    {
        var v = RunBuddy.Keys.ToDictionary(k => k, _ => "");
        if (f == null || f.State.Length == 0) return v;
        var w = f.View;
        v["runState"] = f.State;
        v["runPlace"] = f.Place;
        v["runMode"] = f.Mode;
        v["runBest"] = w.Best is double b ? RunClock(b) : "";
        v["runClears"] = w.Clears > 0 ? w.Clears.ToString("N0") : "";
        if (f.State == "between")
        {
            v["runTodayRuns"] = w.TodayRuns > 0 ? w.TodayRuns.ToString("N0") : "";
            v["runTodayKinah"] = w.TodayRuns > 0 && w.TodayValue > 0 ? Fk(w.TodayValue) : "";
            v["runTodayAvg"] = w.TodayAvgClear is double t ? RunClock(t) : "";
            return v;
        }
        v["runToday"] = f.RunToday > 0 ? f.RunToday.ToString("N0") : "";
        double clock = f.State == "clear" && w.Clear is double c ? c : f.Clock;
        v["runClock"] = RunClock(clock);
        if (w.Pace is double p) { v["runPace"] = SignOf(p) == 0 ? "±0:00" : SignedClock(p); v["runPaceSign"] = SignOf(p).ToString(); }
        // the last two bosses down, oldest first: "Kwapo\t1:02\t−0:05\t-1"; a new best split "best"; nothing to compare with ""
        v["splits"] = string.Join("\n", w.Splits.TakeLast(2).Select(s =>
            $"{RunSplits.Short(s.Boss)}\t{RunClock(s.At)}\t{(s.Delta is double d ? (SignOf(d) == 0 ? "±0:00" : SignedClock(d)) : "")}\t{(s.Delta is double d2 ? (s.BestSplit ? "best" : SignOf(d2).ToString()) : "")}"));
        if (f.State == "clear")
        {
            v["runVerdict"] = w.FirstClear ? "first" : w.Rank == 1 ? "best" : w.Rank <= RunSplits.RankShown ? "rank" : "off";
            v["runRank"] = w.Rank > 0 ? w.Rank.ToString() : "";
            if (f.RunValue is long rv) v["runKinah"] = (rv > 0 ? "+" : "") + Fk(rv);
            v["runAvg"] = w.AvgValue is double a ? Fk(a) : "";
        }
        else if (w.Best == null) v["runVerdict"] = "first";
        return v;
    }

    /// <summary>The run values in the window's set (UpdateHud, each tick); any failure leaves them empty and the Buddy as it was.</summary>
    void HudDungeonRunValues()
    {
        RunBuddyFacts? f = null;
        try { f = RunBuddyNow(DateTime.UtcNow); }
        catch (Exception ex) { CrashLog.Write("the Buddy's run figures", ex); }
        foreach (var (k, v) in RunBuddyValues(f)) _hudValues[k] = v;
    }

    string _runKey = "";
    List<RunSplits.Past> _runHistory = new();
    List<RunSplits.Past>? _runHistoryWithCur;
    List<Encounter> _runKills = new();
    List<InstanceRun> _runRuns = new();
    /// <summary>The clear last shown, held its few seconds even when the run closes under it (the player walked out).</summary>
    (RunBuddyFacts Facts, DateTime Start, DateTime Until)? _runClearHeld;

    /// <summary>The run's state now, from the runs log, the kept boss fights and the Parser's place; null out of a dungeon.</summary>
    RunBuddyFacts? RunBuddyNow(DateTime now)
    {
        if (_demo || _watcher.Chat?.Instances is not { } log) return null;
        var cur = log.CurrentRun;
        if (cur != null && (cur.Kind is "Event" or InstanceLog.WorldBoss || !BossBook.Shared.Knows(cur.Zone))) cur = null;
        // a clear still held: shown its few seconds unless a new run began, the player walking out included
        if (_runClearHeld is { } held)
        {
            if (now >= held.Until || cur != null && cur.Start != held.Start) _runClearHeld = null;
            else if (cur == null || log.Leaving) return held.Facts;
        }
        var zone = _watcher.Chat?.ZoneView?.Zone ?? "";
        string place;
        if (cur != null) place = cur.Zone;
        else if (zone.Length > 0 && ZoneLocator.KindOf(zone) is { } kind && kind != "Event" && BossBook.Shared.Knows(ZoneLocator.RunName(zone))) place = ZoneLocator.RunName(zone);
        else return null;
        if (cur != null && log.Leaving) return null;   // out in the open world: the farming figures are back
        var parser = ParserFights;
        string mode = parser != null && RunSplits.SamePlace(parser.Where.Instance, place) ? parser.GameMode : "";
        var midnight = DateTime.Today;
        var key = $"{place}|{cur?.Start:O}|{TrackerSession.FightStore.Version}|{now:yyyyMMddHHmm}|{midnight:yyyyMMdd}";
        if (key != _runKey)
        {
            _runKey = key;
            _runKills = RunSplits.KillsOf(TrackerSession.FightStore.All(), place);
            _runRuns = WeekRunList();
            _runHistory = RunSplits.History(_runRuns, _runKills, place, RunValue, midnight, cur?.Start);
            _runHistoryWithCur = null;
        }
        if (cur == null)
        {
            // between runs: the mode the Parser says, else the one you last ran here
            if (mode.Length == 0) mode = _runHistory.LastOrDefault(p => p.Mode.Length > 0)?.Mode ?? "";
            var view = RunSplits.Now(mode, 0, Array.Empty<RunSplits.Kill>(), _runHistory);
            return new RunBuddyFacts("between", place, mode, 0, 0, view, null);
        }
        var (kills, killMode, timed) = RunSplits.KillsIn(_runKills, cur.Start, DateTime.MaxValue);
        if (mode.Length == 0) mode = killMode;
        double clock = Math.Max(0, (now - cur.Start).TotalSeconds);
        var v = RunSplits.Now(mode, clock, kills, _runHistory, timed);
        int runToday = _runRuns.Count(r => r.Start != cur.Start && (RunSplits.SamePlace(r.Zone, place) || r.Family.Length > 0 && RunSplits.SamePlace(r.Family, place)) && TodayLoot.IsToday(r, midnight)) + 1;
        // the run's worth once its rewards were read (before that it is only what dropped on the way)
        var facts = new RunBuddyFacts("run", place, mode, clock, runToday, v, cur.PaidAt != null ? RunValue(cur) : null);
        if (v.Clear is not double c) return facts;
        // cleared: the verdict its few seconds, waiting for the rewards (a minute at most) so the worth can join it; then
        // today here and your clears
        var clearAt = cur.Start.AddSeconds(c);
        var until = clearAt.AddSeconds(RunSplits.ClearHoldSeconds);
        if (cur.PaidAt is DateTime p) { if ((p - clearAt).TotalSeconds <= RunSplits.PaidWaitSeconds) until = Max(until, p.AddSeconds(RunSplits.PaidHoldSeconds)); }
        else until = Max(until, clearAt.AddSeconds(RunSplits.PaidWaitSeconds));
        if (now < until)
        {
            var clear = facts with { State = "clear" };
            _runClearHeld = (clear, cur.Start, until);
            return clear;
        }
        // the cleared run is today's too now (worked out once while the player stays in)
        _runHistoryWithCur ??= RunSplits.History(_runRuns.Any(r => r.Start == cur.Start) ? _runRuns : _runRuns.Append(cur).ToList(), _runKills, place, RunValue, midnight, null);
        return new RunBuddyFacts("between", place, mode, 0, 0, RunSplits.Now(mode, 0, Array.Empty<RunSplits.Kill>(), _runHistoryWithCur), null);
    }

    static DateTime Max(DateTime a, DateTime b) => a > b ? a : b;

    /// <summary>
    /// The renders' sample runs (Ferocious Horn Den on Easy, its three bosses, 38 earlier clears, the fastest 4:12): "run" (0:48
    /// in, no boss down yet), "split" (Kwapo down ahead of your best split), "ahead" (Duanka down 11 s ahead of the fastest run),
    /// "behind" (past the fastest run's clear with Nuakum still up), "newbest" and "clear" (the verdict: a new best, then 2nd
    /// fastest with the run's worth against your average), "between" (today here and your clears), "first" (no history here:
    /// the clock alone). Each through RunSplits, as the live values are.
    /// </summary>
    internal static RunBuddyFacts RunBuddySample(string kind)
    {
        const string place = "Ferocious Horn Den", mode = "Easy";
        var today = DateTime.Today.ToUniversalTime().AddHours(10);
        RunSplits.Past Run(double kwapo, double duanka, double nuakum, long value, bool isToday, int n) => new(today.AddMinutes(-10 * n), mode,
            new List<RunSplits.Kill> { new("Watchdog Kwapo", kwapo, false), new("Necromancer Duanka", duanka, false), new("Ferocious Horn Nuakum", nuakum, true) }, value, true, isToday);
        var history = new List<RunSplits.Past>();
        // 38 earlier clears: the fastest 4:12 (Kwapo 1:07, Duanka 2:41), two of them today
        for (int i = 0; i < 36; i++) history.Add(Run(70 + i % 7, 172 + i % 11, 268 + i * 3, 140_000 + i * 1_200, false, 40 - i));
        history.Add(Run(67, 161, 252, 171_000, true, 2));
        history.Add(Run(74, 175, 281, 164_000, true, 1));
        RunSplits.Kill K(string boss, double at, bool final = false) => new(boss, at, final);
        var kills = kind switch
        {
            "split" => new List<RunSplits.Kill> { K("Watchdog Kwapo", 62) },
            "ahead" => new List<RunSplits.Kill> { K("Watchdog Kwapo", 62), K("Necromancer Duanka", 150) },
            "behind" => new List<RunSplits.Kill> { K("Watchdog Kwapo", 75), K("Necromancer Duanka", 166) },
            "newbest" => new List<RunSplits.Kill> { K("Watchdog Kwapo", 62), K("Necromancer Duanka", 151), K("Ferocious Horn Nuakum", 241, true) },
            "clear" => new List<RunSplits.Kill> { K("Watchdog Kwapo", 66), K("Necromancer Duanka", 165), K("Ferocious Horn Nuakum", 257, true) },
            "first" => new List<RunSplits.Kill> { K("Watchdog Kwapo", 81) },
            _ => new List<RunSplits.Kill>(),
        };
        double clock = kind switch { "run" => 48, "split" => 64, "ahead" => 200, "behind" => 271, "first" => 104, _ => 0 };
        if (kind == "first") history.Clear();
        string state = kind switch { "newbest" or "clear" => "clear", "between" => "between", _ => "run" };
        var view = RunSplits.Now(mode, clock, kills, history);
        return new RunBuddyFacts(state, place, mode, clock, kind == "first" ? 1 : 3, view, state == "clear" ? (kind == "newbest" ? 182_000 : 158_000) : null);
    }
}
