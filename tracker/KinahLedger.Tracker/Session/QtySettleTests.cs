using KinahLedger.Data;
using KinahLedger.Ocr;

namespace KinahLedger.Session;

/// <summary>
/// parse-test's checks for counts read cut (2026-10-25, Steven: "kukuru x3 was read as kukuru x1"; branch
/// tracker/qty-settle). His trace.log, 11:33:20 to 11:35:39 local, shows two Kukuru gathers, two "Acquired Kukuru x3."
/// lines in every read to the end, and 0.8.33 counting 4: the first gather filed as "the inventory at work" (the storage
/// lines a moment before were still new), and the second line, read cut as "Acquired Kukuru" by a read that lined up one
/// line off, counted as a third pickup, "quantity unsure, 1 assumed". The replay case is
/// tests/replays/text-2026-10-25-kukuru-cut-count; these are the same rules on reads small enough to follow by hand:
/// a line whose count was lost is a copy of the item at any count (no second pickup without a witness of one), its
/// count once read twice at its place amends the pickup (never a second one), one read is not enough, a gather the
/// gathering panel just named is never the inventory's, and real second pickups, gathered or farmed, still count twice.
/// Writes only to parse-test's scratch log.
/// </summary>
public static class QtySettleTests
{
    public static bool Run(ItemDictionary items, Settings settings)
    {
        bool ok = true;
        void Check(string what, string got, string expect)
        {
            bool pass = got == expect; ok &= pass;
            Console.WriteLine($"  {what,-62} -> {(got.Length > 60 ? got[..60] + "…" : got),-40} {(pass ? "ok" : "MISMATCH, expected " + expect)}");
        }
        Console.WriteLine("\nCounts read cut: a provisional count settles, a cut re-read is no pickup (2026-10-25, Kukuru x3 as x1):");
        int N(TrackerSession ss, string name) => ss.Engine.Items.Where(kv => items.NameOf(kv.Key) == name).Sum(kv => kv.Value);
        string Log() { try { return File.ReadAllText(CrashLog.ReadsPath); } catch (Exception) { return ""; } }
        // what a scenario wrote to reads.log (the log is shared by every parse-test check)
        int mark = 0;
        void Mark() => mark = Log().Length;
        string Since() { var l = Log(); return mark <= l.Length ? l[mark..] : l; }
        void Dump(string about)
        {
            foreach (var nl in Since().Split('\n').Where(x => x.Contains(about)).TakeLast(30)) Console.WriteLine("      note: " + nl.TrimEnd());
        }

        // ---- 0. Steven's reads themselves: the replay case, its count printed here too (parse-test stops a CI run before the
        // replay-bank step prints a failing case's count) ----
        const string Case = "text-2026-10-25-kukuru-cut-count";
        string? caseDir = null;
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            for (var d = new DirectoryInfo(start); d != null && caseDir == null; d = d.Parent)
                if (Directory.Exists(Path.Combine(d.FullName, "tracker", "tests", "replays", Case))) caseDir = Path.Combine(d.FullName, "tracker", "tests", "replays", Case);
        if (caseDir == null) Console.WriteLine($"  ({Case} not found from here: the replay-bank step runs it)");
        else
        {
            var only = TestTemp.Path("kl-qty-case-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(Path.Combine(only, Case));
            foreach (var f in Directory.GetFiles(caseDir)) File.Copy(f, Path.Combine(only, Case, Path.GetFileName(f)));
            var keepDir = CrashLog.DirOverride;
            List<ReplayBank.Result> res;
            try { res = ReplayBank.Run(only, settings); }
            finally { CrashLog.DirOverride = keepDir; try { Directory.Delete(only, true); } catch (Exception) { } }
            var r = res.Single();
            Check("11:33 to 11:35 as read (replay case): Kukuru 6, Orichalcum Ore 1", r.Status == "pass" ? "pass" : r.Detail, "pass");
            if (r.Status != "pass") Console.WriteLine("      " + r.Detail);
        }

        var t0 = new DateTime(2026, 10, 25, 15, 34, 0, DateTimeKind.Utc);
        var top = new List<string> { "[P1] so the boss", "[P2] spawns at ten", "[P3] ok thanks" };
        List<string> R(params string[] more) => top.Concat(more).ToList();

        // ---- 1. the gather's line read again cut, after a read that lined up one line off (11:34:31) ----
        // A read three lines long that shares only its top two with the next one lines that one up on them, so the line
        // counted a moment ago comes back as new, here cut: "Acquired Kukuru". Its count is unknown, so it may be the
        // counted x3 line: with no new loot list row, gathering race or kill behind it, it is that line, and nothing is
        // counted or assumed for it (0.8.33: a third pickup, 1, then its place read x3 and 2 more)
        TrackerSession Gathers(Action<TrackerSession>? second, string[] after, int afterReads)
        {
            var s = new TrackerSession(null, settings, items, "Test") { Vote = false };
            s.ProcessChat(R(), t0);
            s.ProcessChat(R("Acquired Kukuru x3."), t0.AddSeconds(1));
            s.ProcessChat(new List<string> { top[0], top[1], "[P9] wts" }, t0.AddSeconds(3));
            second?.Invoke(s);
            s.ProcessChat(R("Acquired Kukuru"), t0.AddSeconds(5));
            for (int i = 0; i < afterReads; i++) s.ProcessChat(R(after[Math.Min(i, after.Length - 1)]), t0.AddSeconds(6 + i));
            s.FlushVotes(t0.AddSeconds(6 + afterReads));
            return s;
        }
        {
            Mark();
            var before = Accuracy.Get("qty_assumed");
            var s = Gathers(null, new[] { "Acquired Kukuru x3." }, 9);
            Check("a cut re-read of a counted line, no witness: one pickup", N(s, "Kukuru").ToString(), "3");
            Check("...and it is not \"1 assumed\"", $"{s.CountsAssumed} {Since().Contains("\"Acquired Kukuru\": quantity unsure")} {Accuracy.Get("qty_assumed") - before}", "0 False 0");
            Check("...left out as the line counted before", Since().Contains("chat: 'Acquired Kukuru' seen again after a scroll").ToString(), "True");
            if (N(s, "Kukuru") != 3) Dump("Kukuru");
        }

        // ---- 2. a real second gather whose line was read cut: provisional 1, settled x3 once read twice ----
        {
            Mark();
            var s = Gathers(x => x.GatherOutcome(t0.AddSeconds(4.6), true, false), new[] { "Acquired Kukuru x3." }, 9);
            Check("a second gather (its race), its line cut: x3 once read twice", N(s, "Kukuru").ToString(), "6");
            Check("...the correction logged", Since().Contains("Kukuru: quantity settled x3 (was 1 assumed), read twice at").ToString(), "True");
            Check("...amended, not a pickup of its own: no \"1 assumed\"", $"{s.CountsAssumed}", "0");
            if (N(s, "Kukuru") != 6) Dump("Kukuru");
        }

        // ---- 3. a count read once is not enough (the 11:34 reads gave "x3" and no count, read after read) ----
        {
            Mark();
            var s = Gathers(x => x.GatherOutcome(t0.AddSeconds(4.6), true, false), new[] { "Acquired Kukuru x3.", "Acquired Kukuru" }, 18);
            Check("its count read once in 15 s, cut again after: 1 assumed", $"{N(s, "Kukuru")} {s.CountsAssumed}", "4 1");
            Check("...logged as before", Since().Contains("\"Acquired Kukuru\": quantity unsure, 1 assumed").ToString(), "True");
            if (N(s, "Kukuru") != 4) Dump("Kukuru");
        }

        // ---- 4. a real second gather with its line whole still counts twice ----
        {
            var s = new TrackerSession(null, settings, items, "Test") { Vote = false };
            s.ProcessChat(R(), t0);
            s.GatherOutcome(t0.AddSeconds(0.6), true, false);
            s.ProcessChat(R("Acquired Kukuru x3."), t0.AddSeconds(1));
            s.ProcessChat(R("Acquired Kukuru x3."), t0.AddSeconds(2));
            s.GatherOutcome(t0.AddSeconds(9.6), true, false);
            for (int i = 0; i < 4; i++) s.ProcessChat(R("Acquired Kukuru x3.", "Acquired Kukuru x3."), t0.AddSeconds(10 + i));
            s.FlushVotes(t0.AddSeconds(14));
            Check("two gathers, two whole \"Acquired Kukuru x3.\" lines: 6", N(s, "Kukuru").ToString(), "6");
        }

        // ---- 5. farming: a drop's line read cut settles the same way; two real drops count twice ----
        {
            Mark();
            var s = new TrackerSession(null, settings, items, "Test") { Vote = false };
            s.ProcessChat(R(), t0);
            s.ProcessChat(R("Acquired 301 Kina.", "Acquired Spiritstone"), t0.AddSeconds(1));
            for (int i = 0; i < 5; i++) s.ProcessChat(R("Acquired 301 Kina.", "Acquired Spiritstone x4."), t0.AddSeconds(2 + i));
            s.FlushVotes(t0.AddSeconds(7));
            Check("a drop with its kill, its line cut, then x4 twice: 4", N(s, "Spiritstone").ToString(), "4");
            Check("...the correction logged", Since().Contains("Spiritstone: quantity settled x4 (was 1 assumed), read twice at").ToString(), "True");
            if (N(s, "Spiritstone") != 4) Dump("Spiritstone");
        }
        {
            var s = new TrackerSession(null, settings, items, "Test") { Vote = false };
            s.ProcessChat(R(), t0);
            for (int i = 0; i < 3; i++) s.ProcessChat(R("Acquired 301 Kina.", "Acquired Spiritstone x4."), t0.AddSeconds(1 + i));
            for (int i = 0; i < 4; i++) s.ProcessChat(R("Acquired 301 Kina.", "Acquired Spiritstone x4.", "Acquired 288 Kina.", "Acquired Spiritstone x4."), t0.AddSeconds(8 + i));
            s.FlushVotes(t0.AddSeconds(12));
            Check("two kills, two whole \"Acquired Spiritstone x4.\" lines: 8", N(s, "Spiritstone").ToString(), "8");
        }

        // ---- 6. the gathering panel's own gather is never the inventory's (11:34:09) ----
        // storage lines a few seconds before keep the inventory's window open (each is new for 20 s, the window runs 10 s
        // past it); the panel named Kukuru 2 s before the line: a gather (0.8.33: "the inventory at work", never counted)
        {
            var s = new TrackerSession(null, settings, items, "Test") { Vote = false };
            s.ProcessChat(R(), t0);
            s.ProcessChat(R("Added Quick Register items to the list.", "Stored item(s) in Storage."), t0.AddSeconds(1));
            for (int i = 0; i < 3; i++) s.ProcessChat(R("Added Quick Register items to the list.", "Stored item(s) in Storage."), t0.AddSeconds(3 + i * 2));
            // the panel as trace.log read it at 11:34:20 (its lines' tops at 5120x2160)
            s.ProcessList(new List<(int, string)> { (1531, "Profes x1"), (1621, "Kukuru x1"), (1687, "Success x1") }, t0.AddSeconds(8));
            for (int i = 0; i < 4; i++) s.ProcessChat(R("Added Quick Register items to the list.", "Stored item(s) in Storage.", "Acquired Kukuru x3."), t0.AddSeconds(10 + i));
            s.FlushVotes(t0.AddSeconds(14));
            Check("storage 9 s before, the panel naming Kukuru: a gather", N(s, "Kukuru").ToString(), "3");
            // ...and a stack out of storage with no gathering panel stays the inventory's (AccuracyTests: Spiritstone x6)
            var s2 = new TrackerSession(null, settings, items, "Test") { Vote = false };
            s2.ProcessChat(R(), t0);
            s2.ProcessChat(R("Stored item(s) in Storage."), t0.AddSeconds(1));
            for (int i = 0; i < 4; i++) s2.ProcessChat(R("Stored item(s) in Storage.", "Acquired Kukuru x3."), t0.AddSeconds(3 + i));
            s2.FlushVotes(t0.AddSeconds(7));
            Check("...the same line with no panel or race: the inventory's", N(s2, "Kukuru").ToString(), "0");
        }

        // ---- 7. the rules alone: ItemRepeatGuard at any count, CountWatch read twice ----
        {
            var r0 = t0.AddMinutes(10);
            var none = new ItemRepeatGuard.Screen(1, DateTime.MinValue);
            // 11:34 as the guard saw it: two x3 lines, each with its loot list row, then "Acquired Kukuru" at 11:34:31
            var g = new ItemRepeatGuard();
            g.Row("Kukuru", r0.AddSeconds(9.3), 1);
            g.Counted("Kukuru", 3, r0.AddSeconds(9.1), r0.AddSeconds(9.1), null);
            g.Row("Kukuru", r0.AddSeconds(24.3), 2);
            g.Counted("Kukuru", 3, r0.AddSeconds(22.6), r0.AddSeconds(24.1), null);
            Check("guard: the cut line, judged at any count, no witness", $"{g.Judge("Kukuru", 1, r0.AddSeconds(31.3), r0.AddSeconds(32.9), null, none, false, unsure: true).V} {g.Judge("Kukuru", 1, r0.AddSeconds(31.3), r0.AddSeconds(36.4), null, none, false, unsure: true).V}", "Wait Repeat");
            Check("...judged on its count 1 alone (0.8.33): a pickup", g.Judge("Kukuru", 1, r0.AddSeconds(31.3), r0.AddSeconds(32.9), null, none, false).V.ToString(), "Count");
            Check("...a read showing three Kukuru lines: a line of its own", g.Judge("Kukuru", 1, r0.AddSeconds(31.3), r0.AddSeconds(32.9), null, new(3, DateTime.MinValue), false, unsure: true).V.ToString(), "Count");
            g.Race(r0.AddSeconds(30.8), "", GatherFamily.Unknown);
            Check("...with a gathering race of its own: a pickup", g.Judge("Kukuru", 1, r0.AddSeconds(31.3), r0.AddSeconds(32.9), null, none, false, unsure: true).V.ToString(), "Count");
            g.Counted("Kukuru", 1, r0.AddSeconds(31.3), r0.AddSeconds(32.9), null, unsure: true);
            Check("a whole x3 line after the cut copy, no new witness: that copy", g.Judge("Kukuru", 3, r0.AddSeconds(35.4), r0.AddSeconds(41), null, none, false).V.ToString(), "Repeat");
            g.Settle("Kukuru", 3);
            Check("...settled at x3: a whole x3 still that copy, a name-first x1 not", $"{g.Judge("Kukuru", 3, r0.AddSeconds(35.4), r0.AddSeconds(41), null, none, false).V} {g.Judge("Kukuru", 1, r0.AddSeconds(42), r0.AddSeconds(42), null, none, false).V}", "Repeat Count");
            // farming: a drop with its kill, then its line read cut
            var f = new ItemRepeatGuard();
            f.Kill(r0.AddSeconds(59.9), "KinahChanged");
            f.Counted("Spiritstone", 4, r0.AddSeconds(60), r0.AddSeconds(60.5), null);
            Check("guard, farming: a cut re-read of a drop, no new kill", f.Judge("Spiritstone", 1, r0.AddSeconds(70), r0.AddSeconds(76), null, none, false, unsure: true).V.ToString(), "Repeat");
            f.Kill(r0.AddSeconds(69.8), "KinahChanged");
            Check("...with a kill of its own: a second drop", f.Judge("Spiritstone", 1, r0.AddSeconds(70), r0.AddSeconds(76), null, none, false, unsure: true).V.ToString(), "Count");

            var w = new CountWatch();
            var p = new ChatParser(items);
            List<CountWatch.Done> Look(IReadOnlyList<string> read, double at) => w.Look(read, 0, r0.AddSeconds(at), p.Meaning, ChatParser.HasCount);
            var cut = new List<string> { "[P1] a", "Acquired Kukuru x3.", "Acquired Kukuru" };
            var whole = new List<string> { "[P1] a", "Acquired Kukuru x3.", "Acquired Kukuru x3." };
            var e1 = new object();
            w.Watch("Acquired Kukuru", "i:kukuru", e1, 0, r0);
            int d12 = Look(cut, 1).Count + Look(cut, 2).Count;
            // (0.8.33 looked one line either side whenever its own place had no count, and took the x3 above it)
            Check("watch: its own place still cut, x3 beside it: nothing read", $"{d12} {w.Waiting}", "0 1");
            var d3 = Look(whole, 3); var d4 = Look(whole, 4);
            Check("watch: x3 at its place once, then twice: settled on the second", $"{d3.Count} {d4.Count} {(d4.Count > 0 ? d4[0].Count : 0)} {(d4.Count > 0 ? d4[0].When.Length : 0)}", "0 1 3 2");
            var e2 = new object();
            w.Watch("Acquired Kukuru", "i:kukuru", e2, 0, r0.AddSeconds(10), held: true);
            int h12 = Look(whole, 11).Count + Look(whole, 12).Count;
            Check("watch, held by the repeat guard: read twice, not settled", $"{h12} {w.Waiting}", "0 1");
            w.Drop(e2);
            Check("...left out by the guard: dropped, never settled or assumed", $"{w.Waiting} {Look(whole, 30).Count}", "0 0");
            var e3 = new object();
            w.Watch("Acquired Kukuru", "i:kukuru", e3, 0, r0.AddSeconds(40), held: true);
            Look(whole, 41); Look(whole, 42);
            w.Release(e3);
            var h3 = Look(whole, 43);
            Check("...counted by the guard: settles on the reads it kept", $"{h3.Count} {(h3.Count > 0 ? h3[0].Count : 0)}", "1 3");
            var e4 = new object();
            w.Watch("Acquired Kukuru", "i:kukuru", e4, 0, r0.AddSeconds(50));
            int over = 0;
            for (int i = 1; i <= 16; i++) over += Look(cut, 50 + i).Count(x => x.Count == 0);
            Check("watch: no count read twice in 15 s: time over (1 assumed)", $"{over} {w.Waiting}", "1 0");
            // its own place shows another line (a read that didn't line up): the line beside it
            var e5 = new object();
            w.Watch("Acquired Kukuru", "i:kukuru", e5, 0, r0.AddSeconds(80));
            var shifted = new List<string> { "[P1] a", "Acquired Kukuru x3.", "Acquired 485 Kina." };
            var s1 = Look(shifted, 81); var s2 = Look(shifted, 82);
            Check("watch: its own place another line: the line beside it", $"{s1.Count} {s2.Count} {(s2.Count > 0 ? s2[0].Count : 0)}", "0 1 3");
        }
        return ok;
    }
}
