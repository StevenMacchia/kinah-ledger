using System.Globalization;
using KinahLedger.Parser;

namespace KinahLedger.Session;

/// <summary>
/// The Buddy's run figures (RunSplits, MainWindow.RunBuddyValues; 2026-11-18), part of parse-test: logged runs take the boss
/// kills between their start and the next run's, each boss once, the mode their fights named; a run's split is the kill less
/// the run's start and its clear the final boss's; bests, best splits, ranks and averages count only runs of the same mode;
/// the pace follows the fastest run's splits and turns to time lost once the clock passes its next split; a first run here
/// has no best and says so. No file is written.
/// </summary>
public static class RunSplitsTests
{
    public static bool Run()
    {
        bool ok = true;
        void Check(string what, string got, string expect)
        {
            bool pass = got == expect; ok &= pass;
            Console.WriteLine($"  {what,-62} -> {got,-40} {(pass ? "ok" : "MISMATCH, expected " + expect)}");
        }
        static string S(double? v) => v is double d ? d.ToString("0.#", CultureInfo.InvariantCulture) : "none";
        Console.WriteLine("\nThe Buddy in a dungeon run: splits, bests, pace and the clear (2026-11-18):");

        const string place = "Ferocious Horn Den";
        var t0 = new DateTime(2026, 11, 18, 15, 0, 0, DateTimeKind.Utc);
        Encounter Kill(string boss, DateTime end, string mode = "Easy", bool final = false, string outcome = "kill", string instance = place) => new()
        {
            Mode = "boss", Kind = "Expedition", Instance = instance, GameMode = mode, Boss = boss, FinalBoss = final, Start = end.AddSeconds(-20), End = end, Outcome = outcome,
        };
        InstanceRun Logged(DateTime start, double minutes, long kinah, string zone = place) => new() { Zone = zone, Kind = "Expedition", Start = start, LastLoot = start.AddMinutes(minutes), Kinah = kinah };
        var fights = new List<Encounter>
        {
            Kill("Necromancer Duanka", t0.AddSeconds(-30)),                                   // the run before: not this one's
            Kill("Watchdog Kwapo", t0.AddSeconds(70)), Kill("Watchdog Kwapo", t0.AddSeconds(75)),   // a boss twice: its first kill
            Kill("Necromancer Duanka", t0.AddSeconds(170), mode: ""),                       // the mode not named on one fight
            Kill("Mau Totem Shaman", t0.AddSeconds(200), outcome: "wipe"),                  // a wipe: no split
            Kill("Ferocious Horn Nuakum", t0.AddSeconds(260), final: true),
            Kill("Watchdog Kwapo", t0.AddMinutes(6).AddSeconds(66)),
            Kill("Necromancer Duanka", t0.AddMinutes(6).AddSeconds(160)),
            Kill("Ferocious Horn Nuakum", t0.AddMinutes(6).AddSeconds(250), final: true),
            Kill("Watchdog Kwapo", t0.AddMinutes(12).AddSeconds(50), mode: "Conquest"),
            Kill("Ferocious Horn Nuakum", t0.AddMinutes(12).AddSeconds(200), mode: "Conquest", final: true),
            Kill("Silver Blade Rotan", t0.AddMinutes(6).AddSeconds(30), instance: "Fire Temple"),
        };
        var runs = new List<InstanceRun> { Logged(t0, 5, 150_000), Logged(t0.AddMinutes(6), 5, 170_000), Logged(t0.AddMinutes(12), 4, 400_000), Logged(t0.AddMinutes(20), 1, 0, "Fire Temple") };
        var kills = RunSplits.KillsOf(fights, place);
        var hist = RunSplits.History(runs, kills, place, r => r.Kinah, DateTime.Today.AddDays(-30), current: null);
        Check("a run takes its own kills, each boss once, no wipe, no other place", string.Join(",", hist[0].Kills.Select(k => $"{RunSplits.Short(k.Boss)}@{k.At}")), "Kwapo@70,Duanka@170,Nuakum@260");
        Check("...its mode the one its fights named", $"{hist[0].Mode}|{hist[2].Mode}", "Easy|Conquest");
        Check("...its clear the final boss's split; the other place's run left out", $"{S(hist[0].Clear)}|{S(hist[1].Clear)}|{hist.Count}", "260|250|3");

        // a run under way in Easy, 2:30 in, Kwapo down at 1:02: best 4:10 (Easy only), Kwapo's best split 1:06, ahead by 4 s
        var now = RunSplits.Now("Easy", 150, new[] { new RunSplits.Kill("Watchdog Kwapo", 62, false) }, hist);
        Check("best is the fastest clear in the same mode only", S(now.Best), "250");
        Check("a split against your best split for that boss (a new best split)", $"{S(now.Splits[0].Delta)}|{now.Splits[0].BestSplit}", "-4|True");
        Check("pace: the last boss against the fastest run's split", S(now.Pace), "-4");
        var late = RunSplits.Now("Easy", 175, new[] { new RunSplits.Kill("Watchdog Kwapo", 62, false) }, hist);
        Check("...time lost once the clock passes the fastest run's next split", S(late.Pace), "15");
        var none = RunSplits.Now("Easy", 40, Array.Empty<RunSplits.Kill>(), hist);
        Check("...nothing to say before the fastest run's first split", S(none.Pace), "none");
        var clear = RunSplits.Now("Easy", 255, new[] { new RunSplits.Kill("Watchdog Kwapo", 62, false), new RunSplits.Kill("Ferocious Horn Nuakum", 255, true) }, hist);
        Check("a clear: its time, its rank among the clears here, the clears with it", $"{S(clear.Clear)}|{clear.Rank}|{clear.Clears}|{clear.FirstClear}", "255|2|3|False");
        Check("...the run's pace at the end against the fastest clear", S(clear.Pace), "5");
        Check("...no average with fewer than three earlier runs", S(clear.AvgValue), "none");
        var first = RunSplits.Now("Hard", 100, new[] { new RunSplits.Kill("Watchdog Kwapo", 80, false), new RunSplits.Kill("Ferocious Horn Nuakum", 240, true) }, hist);
        Check("a mode never run before: no best, no deltas, the first clear", $"{S(first.Best)}|{S(first.Splits[0].Delta)}|{first.FirstClear}|{first.Rank}", "none|none|True|1");
        var more = hist.Concat(new[] { hist[0] with { Value = 200_000 }, hist[1] with { Value = 190_000 } }).ToList();
        var avg = RunSplits.Now("Easy", 0, Array.Empty<RunSplits.Kill>(), more);
        Check("the average run here over the counted runs of the mode", S(avg.AvgValue), "177500");
        Check("between runs: today here (runs, worth, average clear)", $"{avg.TodayRuns}|{avg.TodayValue}|{S(avg.TodayAvgClear)}", "4|710000|255");

        // a run logged late (a boss pulled before its start): a clear, but no time from it, never a best
        var t1 = t0.AddMinutes(30);
        var lateKills = RunSplits.KillsOf(fights.Concat(new[] { Kill("Watchdog Kwapo", t1.AddSeconds(8)), Kill("Ferocious Horn Nuakum", t1.AddSeconds(150), final: true) }), place);
        var lateHist = RunSplits.History(runs.Append(Logged(t1, 3, 160_000)).ToList(), lateKills, place, r => r.Kinah, DateTime.Today.AddDays(-30), current: null);
        var lateRun = lateHist.Last();
        var lateView = RunSplits.Now("Easy", 0, Array.Empty<RunSplits.Kill>(), lateHist);
        Check("a run logged late: cleared, no clear time, never the best", $"{lateRun.Timed}|{lateRun.Cleared}|{S(lateRun.Clear)}|{S(lateView.Best)}|{lateView.Clears}", "False|True|none|250|3");
        var lateNow = RunSplits.Now("Easy", 150, new[] { new RunSplits.Kill("Watchdog Kwapo", 62, false) }, hist, timed: false);
        Check("...the run under way logged late: no deltas, no pace", $"{S(lateNow.Splits[0].Delta)}|{S(lateNow.Pace)}", "none|none");

        // the run under way bounds the one before it and is left out of the history
        var cur = RunSplits.History(runs, kills, place, r => r.Kinah, DateTime.Today, current: t0.AddMinutes(6));
        Check("the run under way is left out; the one before ends at its start", $"{cur.Count}|{string.Join(",", cur[0].Kills.Select(k => RunSplits.Short(k.Boss)))}", "2|Kwapo,Duanka,Nuakum");

        // the values the looks read
        var facts = new Ui.MainWindow.RunBuddyFacts("run", place, "Easy", 150, 3, now, null);
        var v = Ui.MainWindow.RunBuddyValues(facts);
        Check("values: the clock, the best, the pace with a true minus", $"{v["runClock"]}|{v["runBest"]}|{v["runPace"]}|{v["runPaceSign"]}", "2:30|4:10|−0:04|-1");
        Check("...the split as the looks read it", v["splits"].Replace("\t", " "), "Kwapo 1:02 −0:04 best");
        var cv = Ui.MainWindow.RunBuddyValues(new Ui.MainWindow.RunBuddyFacts("clear", place, "Easy", 300, 3, clear, 158_000));
        Check("...the clear's verdict, the clock its clear, the worth", $"{cv["runVerdict"]}|{cv["runRank"]}|{cv["runClock"]}|{cv["runKinah"]}|{cv["runAvg"]}", "rank|2|4:15|+158K|");
        var fv = Ui.MainWindow.RunBuddyValues(new Ui.MainWindow.RunBuddyFacts("run", place, "Hard", 100, 1, RunSplits.Now("Hard", 100, Array.Empty<RunSplits.Kill>(), hist), null));
        Check("...a first run here: the clock alone, \"first\"", $"{fv["runClock"]}|{fv["runBest"]}|{fv["runPace"]}|{fv["runVerdict"]}", "1:40|||first");
        Check("...out of a run, every value empty", string.Concat(Ui.MainWindow.RunBuddyValues(null).Values), "");
        return ok;
    }
}
