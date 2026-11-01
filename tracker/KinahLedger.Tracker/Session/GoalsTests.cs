using System.Globalization;
using KinahLedger.Data;

namespace KinahLedger.Session;

/// <summary>
/// Goals (0.9), part of parse-test: today's progress for each kind (Kinah from farming as LOOT's Today side counts it,
/// an item from farming, gathering and runs' rewards, runs by kind), the pace line, the three-goal cap, a goal said
/// reached once a day (also after goals.json is read again), one set when already reached never said, a removed goal
/// staying removed, and the amounts a player types. Writes only to a scratch folder.
/// </summary>
public static class GoalsTests
{
    public static bool Run(ItemDictionary items, Settings settings)
    {
        bool ok = true;
        void Check(string what, string got, string expect)
        {
            bool pass = got == expect; ok &= pass;
            Console.WriteLine($"  {what,-62} -> {got,-40} {(pass ? "ok" : "MISMATCH, expected " + expect)}");
        }
        static string Fk(double v) => v >= 1e6 ? $"{v / 1e6:0.00}M" : v >= 1e4 ? $"{v / 1e3:0}K" : v.ToString("0", CultureInfo.InvariantCulture);
        Console.WriteLine("\nGoals: today's progress, the pace, three at most, said once a day (0.9):");
        var keepDir = CrashLog.DirOverride;
        var dir = TestTemp.Path("kl-goals-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        CrashLog.DirOverride = dir;
        try
        {
            var now = DateTime.UtcNow;
            long stone = items.IdOf("Qzx Goaltest Stone"), ore = items.IdOf("Qzx Goaltest Ore"), powder = items.IdOf("Qzx Goaltest Powder");
            string stoneName = items.NameOf(stone);

            // a. today's count: loose Kinah from kills, bound Kinah apart; a dropped item, its bound copy and a gathered one
            var day = new SessionEngine(items);
            day.Seed(new SessionEngine.Carry
            {
                Day = DayStore.Today, Active = 3600, Elapsed = 3900, Kinah = 150_000, KinahBound = 40_000, KillsSeen = 50, KinahDrops = 50,
                Items = new() { [stone] = 8, [ore] = 12 }, ItemsBound = new() { [stone] = 1 },
            });
            day.SetZone("Western Cantas Valley");
            day.Apply(new KinahChanged(now, 2_000, false));   // a kill's Kinah: counts
            day.Apply(new KinahChanged(now, 9_000, true));    // bound Kinah (a quest's): never a farming goal's
            // today's runs: an expedition with the stone on its reward screen, a flagged one (its loot left out, still a run), an event
            InstanceRun RunOf(string kind, double hoursAgo, params (long Id, int N, bool Bound)[] loot)
            {
                var r = new InstanceRun { Zone = kind == "Event" ? "Shugo Festival" : "Urugugu Canyon", Kind = kind, Start = now.AddHours(-hoursAgo), LastLoot = now.AddHours(-hoursAgo).AddMinutes(8) };
                foreach (var (id, n, b) in loot) (b ? r.BoundItems : r.Items)[id] = n;
                return r;
            }
            var runs = new List<InstanceRun>
            {
                RunOf("Expedition", 2, (stone, 3, false), (powder, 6, false)),
                RunOf("Expedition", 1, (stone, 50, false)),
                RunOf("Event", 0.5, (powder, 2, true)),
            };
            runs[1].Suspect = "not a run";
            Check("a flagged run is out of the averages", runs[1].OutOfAverages.ToString(), "True");
            var c = GoalCounts.Day(day, runs, now);
            Check("Kinah: loose Kinah from farming, as LOOT's Today side", $"{c.Kinah} {c.Kinah == TodayLoot.Farm(day, items).Kinah}", "152000 True");
            Check("runs: any, expeditions, events", $"{c.Runs} {c.Expeditions} {c.Events}", "3 2 1");

            Goal Kinah(long target) => new() { Kind = Goal.KindKinah, Target = target };
            Goal Item(string name, long target) => new() { Kind = Goal.KindItem, Name = name, Target = target };
            Goal Runs(string which, long target) => new() { Kind = Goal.KindRuns, Which = which, Target = target };
            string P(Goal g) { var p = Goals.Progress(g, c, items); return $"{p.Have}/{p.Target} {(p.Done ? "done" : "to go " + p.ToGo)}"; }
            Check("Kinah goal 300K", P(Kinah(300_000)), "152000/300000 to go 148000");
            Check("item: dropped, bound copy and the counted run's reward", P(Item(stoneName, 20)), "12/20 to go 8");
            Check("item: gathered", P(Item(items.NameOf(ore), 12)), "12/12 done");
            Check("item from runs only (one tradeable, one bound)", P(Item(items.NameOf(powder), 10)), "8/10 to go 2");
            Check("runs: expeditions", P(Runs(Goal.Expeditions, 2)), "2/2 done");
            Check("runs: events", P(Runs(Goal.Events, 3)), "1/3 to go 2");
            Check("runs: any run", P(Runs(Goal.AnyRun, 5)), "3/5 to go 2");
            Check("nothing counted yet: no Kinah, no runs", $"{Goals.Progress(Kinah(1), GoalCounts.Day(null, new List<InstanceRun>(), now), items).Have} {GoalCounts.None.Runs}", "0 0");

            // b. the pace: what is left over today's rate for that thing, as the card's line; none before a minute or once reached
            var pc = new GoalCounts(150_000, 3600, new Dictionary<long, long> { [stone] = 9 }, new Dictionary<long, long> { [powder] = 4 }, 2, 2, 0, 7200, 7200, 0);
            string Pace(Goal g, GoalCounts counts) => Goals.PaceText(Goals.Progress(g, counts, items));
            Check("pace: 150K an hour, 300K goal", Pace(Kinah(300_000), pc), "about 1 h");
            Check("pace: 9 an hour, 20 goal", Pace(Item(stoneName, 20), pc), "about 1 h 13 min");
            Check("pace: 2 expeditions in 2 h, 5 goal", Pace(Runs(Goal.Expeditions, 5), pc), "about 3 h");
            Check("pace: an item only runs gave has none", Pace(Item(items.NameOf(powder), 10), pc), "");
            Check("pace: none in the first minute", Pace(Kinah(300_000), pc with { FarmSeconds = 40 }), "");
            Check("pace: none once reached", Pace(Kinah(100_000), pc), "");
            Check("pace: under a minute", Pace(Kinah(150_010), pc), "under a minute");
            Check("a week is the days summed (the seam)", $"{GoalCounts.Sum(new[] { pc, pc }).Kinah} {GoalCounts.Sum(new[] { pc, pc }).ItemCount(stone)}", "300000 18");

            // c. three at most; goals.json keeps three, drops what can't be shown
            var goals = new List<Goal>();
            Check("three goals fit", $"{Goals.Add(goals, Kinah(300_000))} {Goals.Add(goals, Item(stoneName, 20))} {Goals.Add(goals, Runs(Goal.Expeditions, 2))}", "True True True");
            Check("a fourth is refused", $"{Goals.Add(goals, Kinah(1_000_000))} {goals.Count}", "False 3");
            Check("one that can't be shown is refused", $"{Goals.Add(new List<Goal>(), Item("", 5))} {Goals.Add(new List<Goal>(), Kinah(0))}", "False False");
            var path = Goals.PathFor();
            File.WriteAllText(path, "[" + string.Join(",", Enumerable.Range(1, 5).Select(i => $"{{\"Kind\":\"kinah\",\"Target\":{i}000}}")) + ",{\"Kind\":\"item\",\"Target\":5}]");
            Check("a file with five keeps three", string.Join(",", Goals.Load().Select(g => g.Target)), "1000,2000,3000");
            File.WriteAllText(path, "[{\"Kind\":\"dance\",\"Target\":5},{\"Kind\":\"item\",\"Target\":5},{\"Kind\":\"runs\",\"Which\":\"events\",\"Target\":4},{\"Kind\":\"kinah\",\"Target\":0}]");
            Check("unknown kinds, nameless items, no amount dropped", string.Join(",", Goals.Load().Select(g => g.Kind + g.Target)), "runs4");
            File.WriteAllText(path, "not json");
            Check("a broken file is no goals", Goals.Load().Count.ToString(), "0");

            // d. reaching one is said once a day, also after goals.json is read again; a new day says it again
            var today = DayStore.Today;
            var said = Goals.JustReached(goals, _ => c, items, today);
            Check("the reached one is said", string.Join(" | ", said.Select(g => Goals.ReachedText(g, Fk))), "Goal reached · 2 expeditions");
            Check("...once", Goals.JustReached(goals, _ => c, items, today).Count.ToString(), "0");
            Goals.Save(goals);
            var back = Goals.Load();
            Check("goals.json keeps all three, in order", string.Join(",", back.Select(Goals.Label)), $"Kinah from farming,{stoneName},Expeditions");
            Check("after a restart it isn't said again", Goals.JustReached(back, _ => c, items, today).Count.ToString(), "0");
            Check("the next day it is said again", Goals.JustReached(back, _ => c, items, "2099-01-01").Count.ToString(), "1");
            Check("words: Kinah, an item, one run", $"{Goals.ReachedText(Kinah(300_000), Fk)} / {Goals.What(Item(stoneName, 20), 20, Fk)} / {Goals.What(Runs(Goal.Events, 1), 1, Fk)}",
                $"Goal reached · 300K from farming / 20 {stoneName} / 1 event");

            // e. a goal set when it is already reached counts as said: nothing is announced for what was done before it
            var late = Item(items.NameOf(ore), 10);
            Goals.Settle(late, Goals.Progress(late, c, items), today);
            Check("set when already reached: not said", Goals.JustReached(new List<Goal> { late }, _ => c, items, today).Count.ToString(), "0");
            var raised = late.Copy(); raised.Target = 40;
            Goals.Settle(raised, Goals.Progress(raised, c, items), today);
            Check("raised past today's count: said when reached", $"{raised.SaidDay.Length} {Goals.JustReached(new List<Goal> { raised }, _ => c with { Farmed = new Dictionary<long, long> { [ore] = 40 } }, items, today).Count}", "0 1");

            // f. a goal removed stays removed
            back.RemoveAt(1);
            Goals.Save(back);
            Check("removed: two left, the item gone", string.Join(",", Goals.Load().Select(Goals.Label)), "Kinah from farming,Expeditions");

            // g. amounts as typed
            Check("amounts: 300K 1.5M 20 1,000 2.4", string.Join(" ", new[] { "300K", "1.5M", "20", "1,000", "2.4" }.Select(s => Goals.ParseAmount(s)?.ToString() ?? "-")), "300000 1500000 20 1000 2");
            Check("amounts refused: words, nothing, zero, below zero", string.Join(" ", new[] { "abc", "", "0", "-5" }.Select(s => Goals.ParseAmount(s)?.ToString() ?? "-")), "- - - -");
        }
        catch (Exception ex) { Console.WriteLine("  goals tests threw: " + ex); ok = false; }
        finally
        {
            CrashLog.DirOverride = keepDir;
            try { Directory.Delete(dir, true); } catch (Exception) { }
        }
        return ok;
    }
}
