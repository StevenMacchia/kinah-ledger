using System.Globalization;
using KinahLedger.Data;

namespace KinahLedger.Session;

/// <summary>
/// The quest engine (phase 1, 2026-11-08), part of parse-test: the chains and the next step, tiers from the odds and the
/// grade, points by kind × step × tier × horizon, the live list's order (Kinah this session, then the two materials, then
/// the rest by time to go), a quest's tree, completion once per scope with the hour's cap, the score, and quests.json.
/// Writes only to a scratch folder.
/// </summary>
public static class QuestsTests
{
    public static bool Run(ItemDictionary items, Settings settings)
    {
        bool ok = true;
        void Check(string what, string got, string expect)
        {
            bool pass = got == expect; ok &= pass;
            Console.WriteLine($"  {what,-62} -> {got,-44} {(pass ? "ok" : "MISMATCH, expected " + expect)}");
        }
        static string Fk(double v) => v >= 1e6 ? $"{v / 1e6:0.##}M" : v >= 1e4 ? $"{v / 1e3:0}K" : v.ToString("0", CultureInfo.InvariantCulture);
        Console.WriteLine("\nQuests: chains, tiers, points, the live list, trees, completion and the score (phase 1):");
        var keepDir = CrashLog.DirOverride;
        var dir = TestTemp.Path("kl-quests-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        CrashLog.DirOverride = dir;
        try
        {
            // a. the chains: the next step above a count, past the chain's end round thousands or the last step's size
            Check("stack chain: 0, 4, 5, 99, 100, 999, 1000, 12345", string.Join(",", new long[] { 0, 4, 5, 99, 100, 999, 1000, 12345 }.Select(n => Quests.NextStep(Quests.StackChain, n))), "5,5,10,100,150,1000,2000,20000");
            Check("Kinah this session: 0, 100K, 999K, 20M, 23M", string.Join(",", new long[] { 0, 100_000, 999_000, 20_000_000, 23_000_000 }.Select(n => Fk(Quests.NextStep(Quests.KinahSession, n)))), "100K,250K,1M,30M,30M");
            Check("Kinah this week: 0, 5M, 500M", string.Join(",", new long[] { 0, 5_000_000, 500_000_000 }.Select(n => Fk(Quests.NextStep(Quests.KinahWeek, n)))), "5M,10M,600M");
            Check("step index: 5 is 0, 100 is 5, 1000 is 10, 3000 is 12", string.Join(",", new long[] { 5, 100, 1000, 3000 }.Select(t => Quests.StepIndex(Quests.StackChain, t))), "0,5,10,12");

            // b. tiers from the odds and the grade
            Check("tier: 1 in 4 common, 1 in 12, 1 in 50, 1 in 54, Legend 1 in 3", string.Join(",", new[] { Quests.TierOf(4, "Common"), Quests.TierOf(12, "Common"), Quests.TierOf(50, "Rare"), Quests.TierOf(54, ""), Quests.TierOf(3, "Legend") }), "1,2,2,3,3");
            Check("tier with no odds: Common, Rare, Epic", string.Join(",", new[] { Quests.TierOf(null, "Common"), Quests.TierOf(null, "Rare"), Quests.TierOf(null, "Epic") }), "1,2,3");

            // c. points: base × step × tier × horizon
            Check("points: 5 Spiritstone tier 1 session", Quests.PointsFor(QuestKind.Material, Horizon.Session, 5, 1).ToString(), "8");
            Check("points: 100 Spiritstone tier 1 session (step 6)", Quests.PointsFor(QuestKind.Material, Horizon.Session, 100, 1).ToString(), "48");
            Check("points: 100 of a tier 3 material today", Quests.PointsFor(QuestKind.Material, Horizon.Today, 100, 3).ToString(), "180");
            Check("points: 100K this session, 500K today, 5M this week", $"{Quests.PointsFor(QuestKind.Kinah, Horizon.Session, 100_000, 1)} {Quests.PointsFor(QuestKind.Kinah, Horizon.Today, 500_000, 1)} {Quests.PointsFor(QuestKind.Kinah, Horizon.Week, 5_000_000, 1)}", "10 15 30");
            Check("points: 3 runs today, a best run, 50 gathers", $"{Quests.PointsFor(QuestKind.Runs, Horizon.Today, 3, 1)} {Quests.PointsFor(QuestKind.BestRun, Horizon.Today, 1, 2)} {Quests.PointsFor(QuestKind.Gather, Horizon.Session, 50, 1)}", "36 96 18");

            // d. the live list: a 42-minute session, 1.3M Kinah, Spiritstone 26 (last picked up, worth 7.5K), Thick hide 41 (1.1K), Odyle 12 (2.3K), two Urugugu runs today, 30 gathers
            var start = new DateTime(2026, 11, 8, 14, 0, 0, DateTimeKind.Utc);
            var now = start.AddMinutes(42);
            var mats = new List<QuestMaterial> { new("Thick hide", 1, 1_100), new("Spiritstone", 2, 7_499), new("Odyle", 1, 2_300), new("Rare Dust", 3, 50_000) };
            QuestCounts Counts(long kinah, double secs, Dictionary<string, long>? items = null, Dictionary<string, int>? runs = null, int gathers = 0)
                => new(kinah, secs, items ?? new(), runs ?? new(), gathers);
            var session = Counts(1_300_000, 42 * 60, new() { ["Thick hide"] = 41, ["Spiritstone"] = 26, ["Odyle"] = 13, ["Rare Dust"] = 1 }, gathers: 30);
            var today = Counts(4_200_000, 3 * 3600, new() { ["Thick hide"] = 120, ["Spiritstone"] = 61, ["Odyle"] = 12, ["Rare Dust"] = 2 }, new() { ["Urugugu Canyon"] = 2 }, gathers: 30);
            var week = Counts(18_000_000, 12 * 3600, null, new() { ["Urugugu Canyon"] = 9, ["Shugo Festival"] = 3 });
            var x = new QuestInputs(session, today, week, mats, new[] { "Spiritstone", "Thick hide" }, new[] { new BestHere("Urugugu Canyon", 700_000, 650_000) }, start, now);
            var live = Quests.Live(x);
            string Row(Quest q) => Quests.Title(q, Fk) + $" {q.Have}/{q.Target} {Quests.Pts(q.Points)}";
            Check("row 1: Kinah this session", Row(live[0]), "2M this session 1300000/2000000 50 pts");
            Check("row 2: the material last picked up", Row(live[1]), "30 Spiritstone 26/30 51 pts");
            Check("row 3: the most valuable other stack", Row(live[2]), "50 Thick hide 41/50 40 pts");
            Check("a stray drop (1) has no quest; the longer horizons are not on the list (they are the trees' branches)", $"{live.Any(q => q.Subject == "Rare Dust")} {live.Any(q => q.Horizon == Horizon.Week || (q.Horizon == Horizon.Today && q.Kind != QuestKind.Runs && q.Kind != QuestKind.BestRun))}", "False False");
            Check("the rest by time to go, nearest first", string.Join(" | ", live.Skip(3).Select(q => Quests.Title(q, Fk))),
                "20 Odyle | 50 gathers | 3 Urugugu Canyon runs | Beat your best run · Urugugu Canyon");
            Check("every kind is there once per chain", $"{live.Count(q => q.Kind == QuestKind.Kinah)} kinah, {live.Count(q => q.Kind == QuestKind.Runs)} runs, {live.Count(q => q.Kind == QuestKind.BestRun)} best, {live.Count} in all",
                "1 kinah, 1 runs, 1 best, 7 in all");
            Check("the best run quest: beat 700K, 650K so far", live.First(q => q.Kind == QuestKind.BestRun) is var b ? $"{Quests.Title(b, Fk)} {Fk(b.Have)}/{Fk(b.Target)} {b.Done}" : "", "Beat your best run · Urugugu Canyon 650K/700K False");
            Check("the time to go at the pace: 2M at 1.3M in 42 min", Math.Round(live[0].SecondsToGo!.Value / 60).ToString(), "23");
            var early = Quests.Live(x with { Session = session with { Seconds = 90 } });
            Check("before two minutes: no session quest, the runs today still there", $"{early.Count(q => q.Horizon == Horizon.Session)} {early.Count > 0} {Quests.Title(early[0], Fk)}", "0 True 3 Urugugu Canyon runs");
            Check("no feed: the most valuable stack leads", Quests.Title(Quests.Live(x with { LastFarmed = Array.Empty<string>() })[1], Fk), "30 Spiritstone");

            // e. a quest's tree: the chain upward and the branches over the longer horizons
            var tree = Quests.Tree(live[1], x);
            Check("Spiritstone's chain upward", string.Join(" → ", tree.Up.Select(q => $"{q.Target} ({q.Points})")), "50 (64) → 100 (77) → 150 (90)");
            Check("Spiritstone's branches: today only (the week counts no items)", string.Join(" | ", tree.Branches.Select(Row)), "100 Spiritstone 61/100 115 pts");
            var ktree = Quests.Tree(live[0], x);
            Check("Kinah's branches: today and the week", string.Join(" | ", ktree.Branches.Select(q => Quests.Title(q, Fk))), "5M today | 25M this week");
            Check("a runs quest branches to the week, not the session", string.Join(" | ", Quests.Tree(live.First(q => q.Kind == QuestKind.Runs), x).Branches.Select(q => Quests.Title(q, Fk))), "10 Urugugu Canyon runs");
            Check("a best run has no chain", $"{Quests.Tree(b, x).Up.Count} {Quests.Tree(b, x).Branches.Count}", "0 0");

            // f. completion: every step passed is credited once per scope; the cap; a count that jumps credits each step
            var log = new QuestLog();
            var done = Quests.Advance(log, x, now.ToLocalTime());
            Check("steps passed at first look: Kinah 100K..1M (4), Spiritstone 5..20 (3), Thick hide 5..30 (4), Odyle 5,10 (2), gathers 10,25 (2) this session",
                $"{done.Count(d => d.Horizon == Horizon.Session && d.Kind == QuestKind.Kinah)} {done.Count(d => d.Subject == "Spiritstone" && d.Horizon == Horizon.Session)} {done.Count(d => d.Subject == "Thick hide" && d.Horizon == Horizon.Session)} {done.Count(d => d.Subject == "Odyle" && d.Horizon == Horizon.Session)} {done.Count(d => d.Kind == QuestKind.Gather && d.Horizon == Horizon.Session)}",
                "4 3 4 2 2");
            Check("today's and the week's steps too; the best run not beaten", $"{done.Count(d => d.Horizon == Horizon.Today) > 0} {done.Count(d => d.Horizon == Horizon.Week) > 0} {done.Any(d => d.Kind == QuestKind.BestRun)}", "True True False");
            long worth = done.Where(d => d.Horizon == Horizon.Session).Sum(d => (long)d.Worth), credited = done.Where(d => d.Horizon == Horizon.Session).Sum(d => (long)d.Points);
            Check("the cap: an hour of play earns 400 from the session's quests at most", $"{credited} of {worth} {credited <= Quests.CapPerHour}", $"{Math.Min(worth, Quests.CapPerHour)} of {worth} True");
            var heavy = x with { Session = session with { Kinah = 20_000_000 } };
            var heavyDone = Quests.Advance(new QuestLog(), heavy, now.ToLocalTime());
            Check("...a count that jumped to 20M at once: 400 credited, the rest logged as worth", $"{heavyDone.Where(d => d.Horizon == Horizon.Session).Sum(d => d.Points)} of {heavyDone.Where(d => d.Horizon == Horizon.Session).Sum(d => d.Worth)}", "400 of 859");
            Check("today's and the week's quests are not capped", done.Where(d => d.Horizon != Horizon.Session).All(d => d.Points == d.Worth).ToString(), "True");
            Check("the same look again credits nothing", Quests.Advance(log, x, now.ToLocalTime()).Count.ToString(), "0");
            var later = x with { Session = session with { Kinah = 2_100_000, Seconds = 70 * 60 }, Today = today with { Kinah = 5_000_000 }, NowUtc = now.AddMinutes(28), BestRuns = new[] { new BestHere("Urugugu Canyon", 700_000, 720_000) } };
            var more = Quests.Advance(log, later, later.NowUtc.ToLocalTime());
            Check("later: 2M this session, 5M today and the best run beaten", string.Join(" | ", more.Select(d => Quests.Title(d, Fk))), "2M this session | 5M today | Beat your best run · Urugugu Canyon");
            Check("the second hour lifts the cap", $"{more.Sum(d => d.Points)} {Quests.CapLeft(log, later)}", $"{more.Sum(d => d.Worth)} {800 - credited - more.Where(d => d.Horizon == Horizon.Session).Sum(d => d.Worth)}");
            // a new session: the session quests are done again, today's are not
            var next = later with { SessionStartUtc = now.AddHours(5), NowUtc = now.AddHours(5).AddMinutes(10), Session = session with { Kinah = 150_000, Seconds = 600, Items = new Dictionary<string, long> { ["Spiritstone"] = 6 }, Gathers = 0 } };
            var again = Quests.Advance(log, next, next.NowUtc.ToLocalTime());
            Check("a new session: its own 100K and 5 Spiritstone again, nothing of today's twice", string.Join(" | ", again.Select(d => Quests.Title(d, Fk))), "100K this session | 5 Spiritstone");
            Check("scopes: session, day, week", $"{Quests.ScopeOf(Horizon.Session, start, now)} {Quests.ScopeOf(Horizon.Today, start, new DateTime(2026, 11, 8))} {Quests.ScopeOf(Horizon.Week, start, new DateTime(2026, 11, 8))}", "s:2026-11-08T14:00:00.000Z d:2026-11-08 w:2026-W45");

            // g. the score
            var score = Quests.Score(log, next.SessionStartUtc, next.NowUtc.ToLocalTime());
            Check("score: this session, all time", $"{score.Session} {score.AllTime == log.Done.Sum(d => d.Points)} {score.Today >= score.Session} {score.Week >= score.Today}", $"{again.Sum(d => d.Points)} True True True");
            Check("the Done fold: this session's, newest first", string.Join(" | ", Quests.DoneThisSession(log, next.SessionStartUtc).Select(d => Quests.Title(d, Fk))), "100K this session | 5 Spiritstone");

            // h. quests.json: saved and read back, a broken file is a fresh log, old entries leave
            Quests.Save(log);
            var back = Quests.Load();
            Check("quests.json keeps the log", $"{back.Done.Count == log.Done.Count} {back.AllTimePoints == log.AllTimePoints} {back.Done[0].Scope == log.Done[0].Scope}", "True True True");
            File.WriteAllText(Quests.PathFor(), "not json");
            Check("a broken file is a fresh log", $"{Quests.Load().Done.Count} {Quests.Load().AllTimePoints}", "0 0");
            back.Done[0].DoneAtUtc = now.AddDays(-90);
            long allTime = back.AllTimePoints;
            Quests.Advance(back, next with { NowUtc = now.AddDays(1) }, now.AddDays(1).ToLocalTime());
            Check("an entry older than 60 days leaves; the all-time total keeps its points", $"{back.Done.Any(d => d.DoneAtUtc == now.AddDays(-90))} {back.AllTimePoints >= allTime}", "False True");

            // i. words
            Check("titles and points", $"{Quests.Title(live[0], Fk)} / {Quests.Pts(12)} / {Quests.Pts(12, plus: true)} / {Quests.TierWord(3)}", "2M this session / 12 pts / +12 pts / rare");
        }
        catch (Exception ex) { Console.WriteLine("  quests tests threw: " + ex); ok = false; }
        finally
        {
            CrashLog.DirOverride = keepDir;
            try { Directory.Delete(dir, true); } catch (Exception) { }
        }
        return ok;
    }
}
