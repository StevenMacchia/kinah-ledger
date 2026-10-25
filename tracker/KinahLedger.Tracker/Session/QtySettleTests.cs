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
        return ok;
    }
}
