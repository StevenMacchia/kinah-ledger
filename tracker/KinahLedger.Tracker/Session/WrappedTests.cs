using System.Globalization;
using KinahLedger.Data;

namespace KinahLedger.Session;

/// <summary>
/// Week wrapped and Day wrapped (2026-11-11), part of parse-test: the sheet's figures from hand-made days and runs. The
/// total is farming's made plus the runs' figure the view passes; the hour's rate is the Week side's; days before history
/// add nothing; the top zone is the one that made most (ten minutes or more there), else the run place; the best run is
/// the counted one worth most; the biggest drop is the one worth most, a better grade first on a tie; the change is set
/// against the period before only when history covered all of it; and a week of one day, or a day of a few minutes, is a
/// thin sheet that says what will appear and when, never a figure of 0. Reads and writes no file.
/// </summary>
public static class WrappedTests
{
    public static bool Run(ItemDictionary items, Settings settings)
    {
        bool ok = true;
        void Check(string what, string got, string expect)
        {
            bool pass = got == expect; ok &= pass;
            Console.WriteLine($"  {what,-62} -> {got,-40} {(pass ? "ok" : "MISMATCH, expected " + expect)}");
        }
        Console.WriteLine("\nWeek wrapped and Day wrapped: the recap sheet's figures (2026-11-11):");
        var today = new DateTime(2026, 11, 11);
        static string Fk(double v) => Math.Round(v).ToString("N0", CultureInfo.InvariantCulture);
        string Grade(string item) => item switch { "Greater manastone" => "Legend", "Spiritstone" => "Rare", _ => "" };

        // a day: its farming by zone (zone, minutes, made, best drop and its worth), its runs (count, Kinah), before history or not
        HistoryDay Day(int ago, (string Zone, int Mins, long Made, string Best, long BestWorth)[]? zones = null, int runs = 0, long runKinah = 0, bool before = false)
        {
            var date = today.AddDays(-ago);
            var f = new FarmDay { Day = DayStore.DayOf(date) };
            foreach (var z in zones ?? Array.Empty<(string, int, long, string, long)>())
            {
                f.Zones.Add(new FarmDayZone { Zone = z.Zone, Active = z.Mins * 60, Made = z.Made, Kills = z.Mins * 4, Best = z.Best, BestCount = z.Best.Length > 0 ? 3 : 0, BestWorth = z.BestWorth });
                f.Made += z.Made; f.Kinah += z.Made / 2; f.Active += z.Mins * 60; f.Kills += z.Mins * 4;
            }
            var r = runs == 0 ? RunsDay.None : new RunsDay(runs, runs, runKinah, new() { new RunKind("Expedition", runs) }, new() { new RunName("Urugugu Canyon", runs, runKinah) });
            return new HistoryDay(date, f.Day, ago == 0, before, f, r);
        }
        WrappedRun Run(int ago, string name, long value, bool counted = true) => new(name, name, "Expedition", today.AddDays(-ago), value, 420, counted);
        WrappedPeriod Period(List<HistoryDay> days, long runsMade = 0, params WrappedRun[] runs) => new(days, runs, runsMade);

        // a. the week's figures: farming's made and time as the Week side sums them, the runs' made (their counted values), days played
        var week = new List<HistoryDay>
        {
            Day(6, before: true), Day(5, new[] { ("Western Cantas Valley", 40, 500_000L, "Spiritstone", 60_000L) }), Day(4),
            Day(3, new[] { ("Kraka Footpath", 30, 450_000L, "Greater manastone", 60_000L), ("Western Cantas Valley", 30, 300_000L, "", 0L) }, runs: 2, runKinah: 1_100_000),
            Day(2), Day(1, runs: 1, runKinah: 600_000), Day(0, new[] { ("Altamia Canyon", 5, 900_000L, "Thick hide", 20_000L) }),
        };
        var wRuns = new[] { Run(3, "Urugugu Canyon", 500_000), Run(3, "Urugugu Canyon", 640_000), Run(1, "Vakron Sky Island", 560_000), Run(1, "Vakron Sky Island", 2_000_000, counted: false) };
        var wf = Wrapped.Of(Wrapped.Week, Period(week, 1_700_000, wRuns), null, Array.Empty<WrappedDrop>(), Grade, null);
        Check("the week: farmed, its time, the runs' figure, the total", $"{wf.Farmed} {wf.FarmSeconds} {wf.FromRuns} {wf.Total}", "2150000 6300 1700000 3850000");
        Check("...the hour's rate is farming's made over its time", Fk(wf.PerHour ?? -1), Fk(2_150_000 / (6300 / 3600.0)));
        Check("...days played: farming or a run, of the week's seven", $"{wf.DaysPlayed} of {wf.Days}", "4 of 7");
        var runsBefore = new List<HistoryDay> { Day(6, before: true, runs: 2, runKinah: 1), Day(5, runs: 1, runKinah: 1) }.Concat(Enumerable.Range(0, 5).Select(a => Day(4 - a))).ToList();
        Check("...runs before farming history began still count, never past the week's days", $"{Wrapped.Of(Wrapped.Week, Period(runsBefore), null, Array.Empty<WrappedDrop>(), Grade, null).DaysPlayed} of {runsBefore.Count}", "2 of 7");
        Check("...runs: all of them, the counted ones", $"{wf.Runs} {wf.Counted}", "4 3");
        Check("...a week of four days is no thin sheet", wf.Thin.ToString(), "False");

        // b. the top zone: the one that made most over the week, ten minutes or more there; with no farming, the run place
        Check("the top zone: most made over the week (a 5-minute zone left out)", $"{wf.Top?.Name} {wf.Top?.Made} {wf.Top?.IsRun}", "Western Cantas Valley 800000 False");
        var runsOnly = Wrapped.Of(Wrapped.Week, Period(new List<HistoryDay> { Day(1, runs: 3, runKinah: 1), Day(0, runs: 1, runKinah: 1) }, 1_990_000, Run(1, "Urugugu Canyon", 600_000), Run(1, "Urugugu Canyon", 590_000), Run(1, "Fire Temple", 700_000), Run(0, "Fire Temple", 100_000)),
            null, Array.Empty<WrappedDrop>(), Grade, null);
        Check("...no farming: the run place that made most, and its runs", $"{runsOnly.Top?.Name} {runsOnly.Top?.Made} {runsOnly.Top?.Runs} {runsOnly.Top?.IsRun}", "Urugugu Canyon 1190000 2 True");

        // c. the best run: the counted run worth most (a flagged one, however big, is left out)
        Check("the best run: the counted one worth most", $"{wf.BestRun?.Name} {wf.BestRun?.Value}", "Urugugu Canyon 640000");
        var wt = Wrapped.Text(wf, Fk);
        Check("...named BEST RUN with two or more counted", wt.Rows.FirstOrDefault(r => r.Kind == "run")?.Label ?? "none", "BEST RUN");
        var oneRun = Wrapped.Of(Wrapped.Day, Period(new List<HistoryDay> { Day(0, runs: 1, runKinah: 1) }, 640_000, Run(0, "Urugugu Canyon", 640_000)), null, Array.Empty<WrappedDrop>(), Grade, null);
        Check("...and THE RUN with one", Wrapped.Text(oneRun, Fk).Rows.FirstOrDefault(r => r.Kind == "run")?.Label ?? "none", "THE RUN");
        var farmOnly = Wrapped.Of(Wrapped.Week, Period(week.Select(d => d with { Runs = RunsDay.None }).ToList()), null, Array.Empty<WrappedDrop>(), Grade, null);
        Check("...no runs: the week names its best day of farming", $"{farmOnly.BestRun == null} {farmOnly.BestDay?.Key}", $"True {DayStore.DayOf(today)}");

        // d. the biggest drop: worth most; a better grade first on a tie; a run's reward counts too
        Check("the biggest drop: worth most, the better grade on a tie", $"{wf.Drop?.Name} {wf.Drop?.Grade} {wf.Drop?.Where}", "Greater manastone Legend Kraka Footpath");
        var withRun = Wrapped.Of(Wrapped.Week, Period(week, 1_700_000, wRuns), null, new[] { new WrappedDrop("Artisan's Ultimate Refining Stone", "Unique", 1, 290_400, "Vakron Sky Island", today.AddDays(-1)) }, Grade, null);
        Check("...a run's reward worth more is the biggest", $"{withRun.Drop?.Name} {withRun.Drop?.Worth}", "Artisan's Ultimate Refining Stone 290400");

        // e. the change against the period before: only when history covered all of it and it had play
        var lastWeek = Enumerable.Range(7, 7).Select(a => Day(a, a % 2 == 0 ? new[] { ("Western Cantas Valley", 60, 770_000L, "", 0L) } : null)).ToList();
        var vs = Wrapped.Of(Wrapped.Week, Period(week, 1_700_000, wRuns), Period(lastWeek, 770_000), Array.Empty<WrappedDrop>(), Grade, null);
        Check("vs last week: its farming and runs, the change", $"{vs.Before} {vs.Change:0.000}", $"{3 * 770_000 + 770_000} {(3_850_000 - 3_080_000) / 3_080_000.0:0.000}");
        var vt = Wrapped.Text(vs, Fk);
        Check("...said up, in the up tone, against what it was", $"{vt.Change} | {vt.ChangeLabel} | {vt.Tone}", "▲ 25% | vs last week, 3,080,000 | 1");
        var down = Wrapped.Of(Wrapped.Week, Period(week, 1_700_000, wRuns), Period(lastWeek, 3_000_000), Array.Empty<WrappedDrop>(), Grade, null);
        Check("...and down, in the down tone", $"{Wrapped.Text(down, Fk).Change} {Wrapped.Text(down, Fk).Tone}", "▼ 27% -1");
        var small = Enumerable.Range(7, 7).Select(a => Day(a, a == 8 ? new[] { ("Western Cantas Valley", 20, 550_000L, "", 0L) } : null)).ToList();
        Check("...seven times the week before reads as a multiple", Wrapped.Text(Wrapped.Of(Wrapped.Week, Period(week, 1_700_000, wRuns), Period(small), Array.Empty<WrappedDrop>(), Grade, null), Fk).Change, "▲ 7×");
        var same = Wrapped.Of(Wrapped.Week, Period(week, 1_700_000, wRuns), Period(lastWeek, 3_850_000 - 3_080_000 + 770_000), Array.Empty<WrappedDrop>(), Grade, null);
        Check("...a change under half a percent is about the same", $"{Wrapped.Text(same, Fk).Change} {Wrapped.Text(same, Fk).Tone}", "about the same 0");
        var partly = lastWeek.Select((d, i) => i == 0 ? d with { BeforeHistory = true } : d).ToList();
        Check("...a week before history began is set against nothing", Wrapped.Of(Wrapped.Week, Period(week, 1_700_000, wRuns), Period(partly, 770_000), Array.Empty<WrappedDrop>(), Grade, null).Before?.ToString() ?? "none", "none");
        var idle = Enumerable.Range(7, 7).Select(a => Day(a)).ToList();
        Check("...nor a week with no play", Wrapped.Of(Wrapped.Week, Period(week, 1_700_000, wRuns), Period(idle), Array.Empty<WrappedDrop>(), Grade, null).Before?.ToString() ?? "none", "none");

        // a run past midnight belongs to the day it started: yesterday's sheet has it, today's doesn't, a week has it once
        var lateStart = new DateTime(today.Year, today.Month, today.Day, 23, 50, 0, DateTimeKind.Utc).AddDays(-1);
        var late = new List<InstanceRun> { new() { Zone = "Urugugu Canyon", Kind = "Expedition", Start = lateStart, LastLoot = lateStart.AddMinutes(30), Kinah = 600_000 } };
        Check("a run past midnight: yesterday's, not today's, once in the week",
            $"{Wrapped.RunsIn(late, today.AddDays(-1), today.AddDays(-1), TimeZoneInfo.Utc).Count} {Wrapped.RunsIn(late, today, today, TimeZoneInfo.Utc).Count} {Wrapped.RunsIn(late, today.AddDays(-6), today, TimeZoneInfo.Utc).Count}", "1 0 1");
        var dayVs = Wrapped.Of(Wrapped.Day, Period(new List<HistoryDay> { Day(0, new[] { ("Western Cantas Valley", 30, 300_000L, "", 0L) }) }), Period(new List<HistoryDay> { Day(1, new[] { ("Western Cantas Valley", 300, 3_000_000L, "", 0L) }) }), Array.Empty<WrappedDrop>(), Grade, null);
        Check("a day is set against nothing: today so far isn't yesterday whole", $"{dayVs.Before?.ToString() ?? "none"} \"{Wrapped.Text(dayVs, Fk).Change}\"", "none \"\"");

        // f. the thin cutoff: a week needs two days of play, a day ten minutes of farming or a run
        var oneDay = Wrapped.Of(Wrapped.Week, Period(Enumerable.Range(0, 7).Select(a => a == 0 ? Day(0, new[] { ("Western Cantas Valley", 80, 1_200_000L, "", 0L) }) : Day(a)).Reverse().ToList()), null, Array.Empty<WrappedDrop>(), Grade, null);
        var oneText = Wrapped.Text(oneDay, Fk);
        Check("a week of one day is thin, and says what will appear and when", $"{oneDay.Thin} | {oneText.ThinHead}", "True | Your week wraps up after two days of play.");
        Check("...and what there is so far", oneText.ThinSoFar, "So far: 1 day of play, 1 h 20 min farming, 1,200,000 made. Play on another day this week and it fills in.");
        HistoryDay Farmed(int mins) => Day(0, new[] { ("Western Cantas Valley", mins, 90_000L * mins, "", 0L) });
        var nine = Wrapped.Of(Wrapped.Day, Period(new List<HistoryDay> { Farmed(9) }), null, Array.Empty<WrappedDrop>(), Grade, null);
        var ten = Wrapped.Of(Wrapped.Day, Period(new List<HistoryDay> { Farmed(10) }), null, Array.Empty<WrappedDrop>(), Grade, null);
        Check("a day of 9 minutes is thin, of 10 is not", $"{nine.Thin} {ten.Thin}", "True False");
        Check("...the thin day says when it fills in", $"{Wrapped.Text(nine, Fk).ThinHead} | {Wrapped.Text(nine, Fk).ThinSoFar}", "Your day wraps up after 10 minutes of farming or your first run. | So far today: 9 min farming, 810,000 made.");
        Check("...a day with a run and no farming is not", oneRun.Thin.ToString(), "False");
        var none = Wrapped.Of(Wrapped.Day, Period(new List<HistoryDay> { Day(0) }), null, Array.Empty<WrappedDrop>(), Grade, null);
        Check("...nothing at all: thin, and says so", $"{none.Thin} | {Wrapped.Text(none, Fk).ThinSoFar}", "True | Nothing counted yet today.");

        // g. the words: no tile or line without a figure, the split only when both made something
        Check("the week's tiles: time, the hour's rate, days played", string.Join(" | ", wt.Stats.Select(s => $"{s.Fig} {s.Label}")), $"1 h 45 min farming | {Fk(2_150_000 / 1.75)} an hour farming | 4 of 7 days played");
        Check("...the split: farming and the runs", wt.Split, "2,150,000 farming · 1,700,000 from 4 runs");
        var rt = Wrapped.Text(oneRun, Fk);
        Check("a day of one run: its time in runs and the run; no split, no average of one", $"{string.Join(" | ", rt.Stats.Select(s => $"{s.Fig} {s.Label}"))} | split \"{rt.Split}\"", "7 min in runs | 1 run | split \"\"");
        Check("...the run named once, without the total again; no place of one run", string.Join(" | ", rt.Rows.Select(r => $"{r.Kind} {r.Name} \"{r.Fig}\"")), "run Urugugu Canyon \"\"");
        Check("...and no figure of 0 anywhere on the sheet", (rt.Stats.Any(s => s.Fig is "0" or "–") || rt.Rows.Any(r => r.Fig is "0" or "–")).ToString(), "False");
        var crafted = Wrapped.Text(Wrapped.Of(Wrapped.Day, Period(new List<HistoryDay> { Farmed(30) }), null, Array.Empty<WrappedDrop>(), Grade, new WrappedCraft(-39_065, 3, true)), Fk);
        Check("crafting: the session's profit, signed, at most with a price missing", crafted.Rows.FirstOrDefault(r => r.Kind == "craft") is { } cr ? $"{cr.Name} | {cr.Fig}" : "none", "3 crafts this session | ≤ −39,065");
        Check("...and none without a craft", Wrapped.Text(ten, Fk).Rows.Any(r => r.Kind == "craft").ToString(), "False");

        // h. the period and the file
        Check("the week's days, and a day", $"{Wrapped.Range(today.AddDays(-6), today)} | {Wrapped.Range(new DateTime(2026, 10, 30), new DateTime(2026, 11, 5))} | {Wrapped.Range(today, today)}",
            "Thu 5 to Wed 11 Nov | Fri 30 Oct to Thu 5 Nov | Wed 11 Nov");
        Check("Save picture's file name", Wrapped.FileName(Wrapped.Week, new DateTime(2026, 11, 11, 22, 41, 0)), "kinah-ledger-2026-11-11-2241-week-wrapped.png");
        return ok;
    }
}
