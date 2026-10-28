using System.Text.RegularExpressions;
using KinahLedger.Data;

namespace KinahLedger.Session;

/// <summary>
/// Reliability rules (2026-10-25), part of parse-test: reads.log is turned over to reads.old.log instead of being wiped,
/// repeated notes are written once and counted, pictures have a daily budget per kind, reward-shots and reports are pruned
/// by age and size (never a run's end of the last week, never a young or unsent report), and the clean-up runs on its own
/// thread and waits for a read pass. Everything runs in scratch folders and with a stand-in clock; the accuracy queue's
/// checks are in `accuracy-test`, beside the site stand-in.
/// </summary>
public static class ReliabilityTests
{
    public static bool Run(ItemDictionary items, Settings settings)
    {
        bool ok = true;
        void Check(string what, string got, string expect)
        {
            bool pass = got == expect; ok &= pass;
            Console.WriteLine($"  {what,-62} -> {(got.Length > 60 ? got[..60] + "…" : got),-40} {(pass ? "ok" : "MISMATCH, expected " + expect)}");
        }
        Console.WriteLine("\nReliability: reads.log turned over, repeats counted, pictures budgeted, folders pruned (2026-10-25):");
        var keepDir = CrashLog.DirOverride; var keepMax = CrashLog.ReadsMaxBytes; var keepWindow = CrashLog.BurstSeconds;
        var dir = TestTemp.Path("kl-reliab-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        CrashLog.DirOverride = dir;
        CrashLog.ResetBursts();
        try
        {
            string[] Lines() { try { return File.ReadAllLines(CrashLog.ReadsPath); } catch (Exception) { return Array.Empty<string>(); } }
            int Count(string text) => Lines().Count(l => l.Contains(text));

            // a. reads.log is turned over at its size, never wiped: the lines before it are in reads.old.log
            CrashLog.ReadsMaxBytes = 3000;
            for (int i = 0; i < 60; i++) CrashLog.Read($"a{i:000} " + new string('x', 40), true);
            var old1 = File.Exists(CrashLog.ReadsOldPath) ? File.ReadAllText(CrashLog.ReadsOldPath) : "";
            var new1 = File.ReadAllText(CrashLog.ReadsPath);
            Check("past its size, reads.log becomes reads.old.log", $"{old1.Contains("a000 ")} {!new1.Contains("a000 ")} {new1.Contains("a059 ")}", "True True True");
            Check("...the new file says so first, and no line is lost", $"{Lines()[0].Contains("turned over")} {Regex.Matches(old1 + new1, @"a\d{3} ").Count}", "True 60");
            for (int i = 0; i < 120; i++) CrashLog.Read($"b{i:000} " + new string('x', 40), true);
            var old2 = File.ReadAllText(CrashLog.ReadsOldPath);
            Check("a second turn-over replaces the older history, keeping two files", $"{!old2.Contains("a000 ")} {old2.Contains("b0")} {File.ReadAllText(CrashLog.ReadsPath).Contains("b119 ")}", "True True True");
            var both = File.ReadLines(CrashLog.ReadsOldPath).Concat(File.ReadLines(CrashLog.ReadsPath)).ToList();
            Check("the tail for a report runs across the turn-over", string.Join("|", CrashLog.ReadsTail(100)) == string.Join("|", both.TakeLast(100)) ? "same" : "different", "same");
            Check("...and reads.log alone when it holds enough", $"{CrashLog.ReadsTail(3).Count} {CrashLog.ReadsTail(3).Last().Contains("b119 ")}", "3 True");
            // a replay finds the zones in reads.old.log too
            var rl = Path.Combine(dir, "replay-reads"); Directory.CreateDirectory(rl);
            File.WriteAllText(Path.Combine(rl, "reads.old.log"), "10:00:00.0  ??  zone: Aria\n");
            File.WriteAllText(Path.Combine(rl, "reads.log"), "10:05:00.0  ??  zone: Bria\n");
            string Zones(string? older) => string.Join(",", ReplaySignals.FromReadsLog(Path.Combine(rl, "reads.log"), new DateTime(2026, 10, 25), older).Where(s => s.Kind == "zone").Select(s => s.Value));
            Check("replay: the zones of reads.old.log come first", $"{Zones(Path.Combine(rl, "reads.old.log"))} | {Zones(null)}", "Aria,Bria | Bria");

            // b. a note that flaps is written once and counted; the audit line and the lines replays read never are
            CrashLog.ReadsMaxBytes = 5_000_000;
            var t = new DateTime(2026, 10, 25, 12, 0, 0);
            CrashLog.Wall = () => t;
            File.WriteAllText(CrashLog.ReadsPath, "");
            for (int i = 0; i < 7; i++) { CrashLog.NoteBurst("crafting panel up"); t = t.AddSeconds(2); CrashLog.NoteBurst("crafting panel gone"); t = t.AddSeconds(2); }
            Check("up and gone seven times in half a minute: one line each", $"{Count("crafting panel up")} {Count("crafting panel gone")}", "1 1");
            t = t.AddSeconds(40);
            CrashLog.Note("audit: last 60 s · kills counted +0");
            var up = Lines().Where(l => l.Contains("crafting panel up")).ToList();
            Check("the minute over, the count is one line: (×6 more)", $"{up.Count} {up.Last().Contains("(×6 more, last at 12:00:24)")} {Lines().Last().Contains("audit:")}", "2 True True");
            CrashLog.NoteBurst("crafting panel up");
            Check("...and the next one after it is written again", $"{Count("crafting panel up")}", "3");
            for (int i = 0; i < 3; i++) CrashLog.Note("audit: last 60 s · kills counted +0");
            Check("the audit line is never held back", $"{Count("audit:")}", "4");
            for (int i = 0; i < 8; i++) CrashLog.Note("zone: Aria");
            Check("zone lines (a replay reads them) are never held back", $"{Count("zone: Aria")}", "8");
            for (int i = 0; i < 8; i++) CrashLog.Note("some note that repeats");
            Check("any other note: the first five a minute, the rest counted", $"{Count("some note that repeats")}", "5");
            t = t.AddSeconds(61);
            CrashLog.Note("audit: tick");
            Check("...the rest as one line", $"{Count("some note that repeats")} {Lines().Any(l => l.Contains("some note that repeats  (×3 more"))}", "6 True");
            for (int i = 0; i < 3; i++) CrashLog.Read("Acquired 3 Kina.", true);
            Check("chat lines are never collapsed", $"{Count("Acquired 3 Kina.")}", "3");
            CrashLog.NoteBurst("not in the item list, not counted: Aria Essence"); CrashLog.NoteBurst("not in the item list, not counted: Bria Essence");
            CrashLog.NoteBurst("not in the item list, not counted: Aria Essence");
            Check("different names are different lines", $"{Count("Aria Essence")} {Count("Bria Essence")}", "1 1");
            CrashLog.NoteBurst("flapper");
            File.WriteAllText(CrashLog.ReadsPath, "");
            CrashLog.NoteBurst("flapper");
            Check("a log someone emptied starts its counts over", $"{Count("flapper")}", "1");
            CrashLog.NoteBurst("pending count"); CrashLog.NoteBurst("pending count"); CrashLog.NoteBurst("pending count");
            CrashLog.FlushHeld();
            Check("the end of a run writes the counts still held", $"{Count("pending count")} {Lines().Any(l => l.Contains("pending count  (×2 more"))}", "2 True");
            CrashLog.Clock = () => new DateTime(2026, 10, 25, 1, 2, 3);
            for (int i = 0; i < 8; i++) CrashLog.Note("replayed note");
            CrashLog.Clock = null;
            Check("a replay (its own clock) is not collapsed or trimmed", $"{Count("replayed note")}", "8");
            CrashLog.Wall = () => DateTime.Now;
            CrashLog.BurstSeconds = keepWindow;

            // c. report pictures that may be sent are marked when they weren't
            var md = Path.Combine(dir, "marks"); Directory.CreateDirectory(md);
            bool Has() => ReportBuffer.HasSendablePictures(md);
            bool none = Has();
            File.WriteAllLines(Path.Combine(md, "frames.jsonl"), new[] { "{\"t\":\"2026-10-25T10:00:00Z\",\"k\":\"chat\",\"f\":\"frames/c.png\",\"x\":1,\"y\":2}" });
            bool chatOnly = Has();
            File.AppendAllLines(Path.Combine(md, "frames.jsonl"), new[] { "{\"t\":\"2026-10-25T10:00:01Z\",\"k\":\"loot\",\"f\":\"frames/l.png\",\"x\":1,\"y\":2}" });
            Check("a report's pictures that may be sent: none, chat only, a loot list", $"{none} {chatOnly} {Has()}", "False False True");
            ReportBuffer.MarkUnsent(md, "the site answered 500");
            bool marked = File.Exists(Path.Combine(md, "unsent.txt"));
            ReportBuffer.ClearUnsent(md);
            Check("...unsent.txt when the send failed, off when it went", $"{marked} {File.Exists(Path.Combine(md, "unsent.txt"))}", "True False");

            // d. every kind of picture has its own budget a day; crafting frames can't crowd out a run's end
            var bd = Path.Combine(dir, "budget"); Directory.CreateDirectory(bd);
            var day = new DateTime(2026, 10, 24, 20, 0, 0);
            var stamp = day.ToString("yyyyMMdd");
            for (int i = 0; i < 203; i++) File.WriteAllBytes(Path.Combine(bd, $"{stamp}-{191300 + i:000000}-Aria-crafting.jpg"), Array.Empty<byte>());
            Check("203 crafting frames today (2026-10-24 19:13): crafting is full", $"{ShotBudget.Room(bd, day, "crafting")}", "False");
            Check("...a reward table, an event card and a Daeva Pass still fit", $"{ShotBudget.Room(bd, day, "reward-table")} {ShotBudget.Room(bd, day, "event-rewards")} {ShotBudget.Room(bd, day, "reward-table-settled")} {ShotBudget.Room(bd, day, "Auldors-Sanctum-Daeva-Pass")} {ShotBudget.Room(bd, day, "in-run")}", "True True True True True");
            for (int i = 0; i < ShotBudget.RunEndPerDay; i++) ShotBudget.Took(bd, day, i % 2 == 0 ? "event-rewards" : "reward-table");
            Check("a run's end has 200 of its own; then only run ends are full", $"{ShotBudget.Room(bd, day, "event-rewards")} {ShotBudget.Room(bd, day, "reward-table")} {ShotBudget.Room(bd, day, "in-run")} {ShotBudget.Room(bd, day, "Quna-Shop")}", "False False True True");
            Check("the next day starts over", $"{ShotBudget.Room(bd, day.AddDays(1), "crafting")}", "True");
            Check("crafting 20 a day, 40 s apart; run ends 200", $"{ShotBudget.CraftingPerDay} {ShotBudget.CraftingGapSeconds} {ShotBudget.RunEndPerDay}", "20 40 200");
            string K(string n) => ShotBudget.Of(n).ToString();
            Check("kinds by name", $"{K("20261024-1-A-crafting.jpg")} {K("20261024-1-A-in-run.jpg")} {K("20261024-1-A-event-rewards.jpg")} {K("20261024-1-A-reward-table-settled.jpg")} {K("20261024-1-A-Quna-Shop.jpg")} {K("20261024-1-A-Daeva-Pass.jpg")}",
                "Crafting InRun RunEnd RunEnd Screen Screen");

            // e. reward-shots: by age (a run's end 14 days, the rest as before) and by size (400 MB; non-run first)
            var now = DateTime.Now;
            string Shots(string name) { var d = Path.Combine(dir, name); Directory.CreateDirectory(d); return d; }
            void Pic(string d, string n, double daysOld, int bytes = 100) { var f = Path.Combine(d, n); File.WriteAllBytes(f, new byte[bytes]); File.SetLastWriteTime(f, now.AddDays(-daysOld)); }
            var ages = Shots("ages");
            Pic(ages, "20261001-101010-Zone-reward-table.jpg", 20); Pic(ages, "20261012-101010-Zone-event-rewards.jpg", 10);
            Pic(ages, "20261023-101010-Zone-crafting.jpg", 2); Pic(ages, "20261023-101011-Zone-in-run.jpg", 0.5);
            Pic(ages, "20261015-101010-Zone-Quna-Shop.jpg", 3); Pic(ages, "20261015-101011-Zone-Daeva-Pass.jpg", 10);
            Pic(ages, "20261022-101011-Zone-Daeva-Pass.jpg", 3); Pic(ages, "mine.jpg", 40);
            int agesGone = TrackerSession.PruneRewardShots(ages, now);
            Check("by age: a run's end 14 days, a shop 2, crafting 1, other screens 7", $"{agesGone} {string.Join(" ", Directory.GetFiles(ages).Select(Path.GetFileName).OrderBy(n => n).Select(n => n!.Length > 22 ? n[16..] : n))}",
                "4 Zone-event-rewards.jpg Zone-Daeva-Pass.jpg Zone-in-run.jpg mine.jpg");
            string SizeSet(string name)
            {
                // A to C a run's end from the last week, D and E older, F and G other reward screens; 100 bytes each
                var d = Shots(name);
                Pic(d, "20261024-100001-A-reward-table.jpg", 1); Pic(d, "20261023-100002-B-event-rewards.jpg", 2); Pic(d, "20261022-100003-C-reward-table-settled.jpg", 3);
                Pic(d, "20261017-100004-D-reward-table.jpg", 8); Pic(d, "20261016-100005-E-event-rewards.jpg", 9);
                Pic(d, "20261024-100006-F-Daeva-Pass.jpg", 1); Pic(d, "20261024-100007-G-Attendance-Rewards.jpg", 1.1);
                return d;
            }
            string Left(string d) => string.Join(" ", Directory.GetFiles(d).Select(f => Path.GetFileName(f).Split('-')[2]).OrderBy(n => n, StringComparer.Ordinal));
            var s1 = SizeSet("size1"); int g1 = TrackerSession.PruneRewardShots(s1, now, maxBytes: 500);
            Check("too big: other screens go first, oldest first, all run ends stay", $"{g1} {Left(s1)}", "2 A B C D E");
            var s2 = SizeSet("size2"); int g2 = TrackerSession.PruneRewardShots(s2, now, maxBytes: 300);
            Check("...still too big: run ends older than a week, oldest first", $"{g2} {Left(s2)}", "4 A B C");
            var s3 = SizeSet("size3"); int g3 = TrackerSession.PruneRewardShots(s3, now, maxBytes: 100);
            Check("...but the last week's run ends are never trimmed for size", $"{g3} {Left(s3)}", "4 A B C");

            // f. reports: 7 days or 200 MB; never one under an hour old, nor one unsent (for two weeks)
            var rnow = new DateTime(2026, 10, 25, 12, 0, 0);
            string Reports(string name) { var r = Path.Combine(dir, name); Directory.CreateDirectory(Path.Combine(r, "reports")); return r; }
            void Rep(string root, string name, int bytes = 1000, bool unsent = false)
            {
                var d = Path.Combine(root, "reports", name); Directory.CreateDirectory(Path.Combine(d, "frames"));
                File.WriteAllBytes(Path.Combine(d, "frames", "loot-1.png"), new byte[bytes]);
                if (unsent) File.WriteAllText(Path.Combine(d, "unsent.txt"), "x");
            }
            string RepLeft(string root) => string.Join(" ", Directory.GetFileSystemEntries(Path.Combine(root, "reports")).Select(Path.GetFileName).OrderBy(n => n));
            var r1 = Reports("rep1");
            Rep(r1, "20261015-090000"); Rep(r1, "20261022-090000"); Rep(r1, "20261015-100000", unsent: true); Rep(r1, "20261009-100000", unsent: true);
            Rep(r1, "20261025-113000"); Rep(r1, "20261025-113000-2");
            Directory.CreateDirectory(Path.Combine(r1, "reports", "keep-me")); File.WriteAllText(Path.Combine(r1, "reports", "note.txt"), "x");
            int rg1 = Housekeeping.PruneReports(r1, rnow);
            Check("reports by age: 10 days goes; 3 days, an hour-young and an unsent one stay", $"{rg1} {RepLeft(r1)}", "2 20261015-100000 20261022-090000 20261025-113000 20261025-113000-2 keep-me note.txt");
            var r2 = Reports("rep2");
            Rep(r2, "20261022-120000"); Rep(r2, "20261023-120000"); Rep(r2, "20261024-120000"); Rep(r2, "20261025-113000"); Rep(r2, "20261021-120000", unsent: true);
            int rg2 = Housekeeping.PruneReports(r2, rnow, maxBytes: 3500);
            Check("reports by size: the oldest first, until it fits", $"{rg2} {RepLeft(r2)}", "2 20261021-120000 20261024-120000 20261025-113000");
            int rg3 = Housekeeping.PruneReports(r2, rnow, maxBytes: 1);
            Check("...a young or unsent report is never trimmed for size", $"{rg3} {RepLeft(r2)}", "1 20261021-120000 20261025-113000");

            // g. the clean-up runs on its own thread, once an hour, and waits for a read pass
            var hroot = dir;
            var hs = Path.Combine(hroot, "reward-shots"); Directory.CreateDirectory(hs);
            var pic = Path.Combine(hs, "20261001-101010-Zone-in-run.jpg");
            File.WriteAllBytes(pic, new byte[10]); File.SetLastWriteTime(pic, now.AddDays(-5));
            Housekeeping.Forget();
            var keepWait = Housekeeping.IdleWaitSeconds;
            Housekeeping.IdleWaitSeconds = 0.3;
            Housekeeping.PassStarted();
            bool started = Housekeeping.RunIfDue(now, hroot);
            bool ended = Housekeeping.WaitForLast(TimeSpan.FromSeconds(10));
            bool kept = File.Exists(pic);
            bool soon = Housekeeping.RunIfDue(now.AddMinutes(4), hroot);
            Housekeeping.PassEnded();
            Check("a pass that never ends: nothing deleted, tried again in 5 minutes", $"{started} {ended} {kept} {soon}", "True True True False");
            Housekeeping.IdleWaitSeconds = keepWait;
            bool later = Housekeeping.RunIfDue(now.AddMinutes(6), hroot);
            bool done = Housekeeping.WaitForLast(TimeSpan.FromSeconds(10));
            Check("...then it deletes what is old", $"{later} {done} {File.Exists(pic)}", "True True False");
            Check("...and says so in reads.log", $"{Count("housekeeping: 1 old picture(s)")}", "1");
            bool hourly = Housekeeping.RunIfDue(now.AddMinutes(30), hroot);
            Check("at most once an hour", $"{hourly}", "False");
            File.WriteAllBytes(pic, new byte[10]); File.SetLastWriteTime(pic, now.AddDays(-5));
            Housekeeping.Forget();
            Housekeeping.PassStarted();
            bool s4 = Housekeeping.RunIfDue(now, hroot);
            Thread.Sleep(400);
            bool waiting = File.Exists(pic);
            Housekeeping.PassEnded();
            bool d4 = Housekeeping.WaitForLast(TimeSpan.FromSeconds(10));
            Check("a read pass running: it waits; the pass over, it deletes", $"{s4} {waiting} {d4} {File.Exists(pic)}", "True True True False");

            // h. one ceiling over every picture folder (Steven, 2026-10-28: "this folder created for the users should never be
            // above like 5 gigs of screenshot and it should auto clear"): past it the oldest go first from any picture folder,
            // an unsent report included (whole); data and icons are never touched
            var croot = Path.Combine(dir, "ceiling");
            var old0 = DateTime.UtcNow.AddDays(-3);
            void Put(string rel, int bytes, int hoursAgo)
            {
                var p = Path.Combine(croot, rel); Directory.CreateDirectory(Path.GetDirectoryName(p)!);
                File.WriteAllBytes(p, new byte[bytes]); File.SetLastWriteTimeUtc(p, old0.AddHours(-hoursAgo));
            }
            Put("reward-shots/a.jpg", 1000, 50); Put("proof/b.png", 1000, 40);
            Put("reports/20261020-100000/frames/loot-1.png", 1000, 30); Put("reports/20261020-100000/unsent.txt", 1, 30);
            Directory.SetLastWriteTimeUtc(Path.Combine(croot, "reports", "20261020-100000", "frames"), old0.AddHours(-30));
            Directory.SetLastWriteTimeUtc(Path.Combine(croot, "reports", "20261020-100000"), old0.AddHours(-30));
            Put("reward-shots/c.jpg", 1000, 20); Put("cards/d.png", 1000, 10);
            Put("data/prices.json", 5000, 60); Put("icons/e.png", 5000, 60);
            int c1 = Housekeeping.PruneToCeiling(croot, maxBytes: 2500);
            string Have() => string.Join(" ", Directory.EnumerateFiles(croot, "*", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(croot, f).Replace('\\', '/')).OrderBy(n => n, StringComparer.Ordinal));
            Check("ceiling: over it, the oldest go from every picture folder, an unsent report whole; data and icons untouched",
                $"{c1} {Have()}", "3 cards/d.png data/prices.json icons/e.png reward-shots/c.jpg");
            int c2 = Housekeeping.PruneToCeiling(croot, maxBytes: 2500);
            Check("...under it, nothing goes", $"{c2}", "0");
            Check("...the ceiling is under 5 GB, so an hour of pictures can't take the folder past 5 GB",
                $"{Housekeeping.CeilingMB < 5 * 1024} {string.Join(",", Housekeeping.PictureFolders)}", "True reward-shots,reports,proof,cards,labels");
        }
        finally
        {
            Housekeeping.IdleWaitSeconds = 20;
            CrashLog.Clock = null; CrashLog.Wall = () => DateTime.Now; CrashLog.ReadsMaxBytes = keepMax; CrashLog.BurstSeconds = keepWindow;
            CrashLog.ResetBursts();
            CrashLog.DirOverride = keepDir;
            try { Directory.Delete(dir, true); } catch (Exception) { }
        }
        return ok;
    }
}
