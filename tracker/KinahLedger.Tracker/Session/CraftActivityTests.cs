using KinahLedger.Data;
using KinahLedger.Ocr;

namespace KinahLedger.Session;

/// <summary>
/// parse-test's checks for when the activity is Crafting (0.10.87; Steven, 2026-11-14: "crafting session shouldn't open on
/// tracker until a craft is complete. Farming should always switch back on gather or kill"), on his 2026-11-13 minutes as
/// reads.log has them: the crafting window and panel, each race, and the gathers and kills around them. The trace keeps no
/// crafting window or panel, so a replay can't show this; these are the log's own times.
/// </summary>
public static class CraftActivityTests
{
    public static bool Run(ItemDictionary items, Settings settings)
    {
        bool ok = true;
        void Check(string what, string got, string expect)
        {
            bool pass = got == expect; ok &= pass;
            Console.WriteLine($"  {what,-62} -> {got,-40} {(pass ? "ok" : "MISMATCH, expected " + expect)}");
        }
        Console.WriteLine("\nCrafting: only once a craft is done; a kill or gather is Farming at once (0.10.87):");
        var keepDir = CrashLog.DirOverride;
        var dir = TestTemp.Path("kl-craftact-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        CrashLog.DirOverride = dir;
        try
        {
            // 2026-11-13, local -5 h: 18:17:13 is 23:17:13 UTC
            DateTime T(string hms) => DateTime.SpecifyKind(new DateTime(2026, 11, 13).Add(TimeSpan.Parse(hms)).AddHours(5), DateTimeKind.Utc);
            var s = new TrackerSession(null, settings, items, "Test");
            long ore = items.IdOf("Orichalcum Ore"), odyle = items.IdOf("Odyle");
            // what the activity was at each look, with when it changed, and the page as the window follows it (ticks every half second)
            var follow = new PageFollow(); string page = PageFollow.Farming;
            var pages = new List<string>();
            void Look(DateTime t) { s.UpdateDoing(t); if (follow.Step(s.Doing.Current.Kind, t, true) is { } p && p != page) { page = p; pages.Add($"{t.AddHours(-5):HH:mm:ss} {p}"); } }
            void Until(DateTime from, DateTime to) { for (var t = from; t <= to; t = t.AddSeconds(0.5)) Look(t); }
            string Kind() => s.Doing.Current.Kind.ToString();
            void Gather(string at, long id) { s.Count(new ItemAcquired(T(at), id, 1, false)); Look(T(at)); }
            void Window(string at) => s.Target.MarkCraft(T(at));
            void Panel(string recipe, string at) => s.PanelUp(recipe, T(at));
            void Race(string recipe, string at, bool ok2) { s.PanelRace(recipe, ok2, false, T(at)); Look(T(at)); }

            s.ApplyZone("Ellun River Midstream", T("18:16:00"));
            Gather("18:17:13", ore); Gather("18:17:21", ore);
            // 18:17:26 the crafting window; the panel and the first race (a fail) at 18:17:48
            Window("18:17:26"); Until(T("18:17:26"), T("18:17:37"));
            string browsing = Kind();
            Panel("Wind Serum", "18:17:37"); Until(T("18:17:37"), T("18:17:48"));
            string queueUp = Kind();
            Race("Wind Serum", "18:17:48", false);
            string firstRace = Kind();
            foreach (var at in new[] { "18:18:00", "18:18:06", "18:18:17", "18:18:24", "18:18:32", "18:18:37", "18:18:43", "18:18:54", "18:18:59", "18:19:03", "18:19:09", "18:19:14", "18:19:23" })
            { Panel("Wind Serum", at); Race("Wind Serum", at, true); }
            // 18:19:25 the window over the queue, the panel gone; the guards' queue at 18:19:58
            Window("18:19:25"); Until(T("18:19:23"), T("18:19:58"));
            string between = Kind();
            foreach (var at in new[] { "18:20:04", "18:20:12", "18:20:27", "18:20:38", "18:20:46" }) { Panel("Orichalcum Guard", at); Race("Orichalcum Guard", at, true); }
            Until(T("18:20:46"), T("18:22:50"));
            Check("the window opened, recipes browsed: not Crafting", browsing, "Farming");
            Check("...the serum queue up, its first race not done: not yet", queueUp, "Farming");
            Check("...its first race done (a fail): Crafting", firstRace, "Crafting");
            Check("...the window over the queue between the two queues: still", between, "Crafting");
            // the gather at 18:22:54 after the crafts: Farming at once
            Gather("18:22:54", ore);
            Check("a gather after the crafts: Farming at once", Kind(), "Farming");
            foreach (var at in new[] { "18:23:21", "18:23:57", "18:24:30", "18:24:42" }) Gather(at, ore);

            // 18:24:53 the crafting window for a moment, nothing crafted, gathering on: never Crafting (it held a minute before)
            Window("18:24:53"); Until(T("18:24:53"), T("18:24:59"));
            string glance = Kind();
            Gather("18:24:59", odyle); Until(T("18:24:59"), T("18:25:05"));
            Check("the window for a glance while gathering: Farming throughout", $"{glance} {Kind()}", "Farming Farming");

            // 18:35:55 in the Abyss, the window opened and closed, nothing crafted, idle before: stays Idle
            s.ApplyZone("Latesran Western Root", T("18:35:06"));
            Until(T("18:35:06"), T("18:35:54"));
            string before = Kind();
            Window("18:35:55"); Until(T("18:35:55"), T("18:36:20"));
            Check("the window opened and shut with nothing crafted: no Crafting", $"{before} {Kind()}", "Idle Idle");

            // a kill right after a craft: Farming at once, and the page with it
            s.ApplyZone("Ellun River Midstream", T("18:40:00"));
            Window("18:40:00"); Panel("Wind Serum", "18:40:02"); Race("Wind Serum", "18:40:08", true);
            Until(T("18:40:08"), T("18:40:12"));
            string crafted = Kind();
            s.Count(new MobKilled(T("18:40:13"), 0)); Look(T("18:40:13"));
            Check("a kill right after a craft: Farming at once", $"{crafted} {Kind()}", "Crafting Farming");
            // the page follows each real change at once and only those: one switch to Crafting per crafting stretch
            Check("the page: Crafting on the first done craft, Farming on the gather or kill",
                string.Join(", ", pages), "18:17:48 Crafting, 18:22:54 Farming, 18:40:08 Crafting, 18:40:13 Farming");
        }
        finally { CrashLog.DirOverride = keepDir; try { Directory.Delete(dir, true); } catch (Exception) { } }
        return ok;
    }
}
