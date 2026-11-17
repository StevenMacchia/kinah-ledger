using System.Text.Json;
using KinahLedger.Data;

namespace KinahLedger.Session;

/// <summary>
/// The one-time re-count of old reward tables (Session/RunRecount.cs), part of parse-test. First the rules on made-up
/// runs (which runs are looked at, which pictures are a run's, when a new read is taken), then the whole pass on a
/// scratch data folder: the replay bank's 2026-11-11 17:29 Urugugu Canyon table (tracker/tests/replays, 27 slots that
/// 0.10.53 read as 34 and 39) kept as a run's picture, the run filed with a phantom Unique bow and eight slots of floor
/// on top of what the table shows. Writes only to a scratch folder.
/// </summary>
public static class RunRecountTests
{
    static string? TablePicture()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        {
            var p = Path.Combine(d.FullName, "tracker", "tests", "replays", "table-2026-11-11-urugugu-1729", "table.jpg");
            if (File.Exists(p)) return p;
            p = Path.Combine(d.FullName, "tests", "replays", "table-2026-11-11-urugugu-1729", "table.jpg");
            if (File.Exists(p)) return p;
        }
        return null;
    }

    public static bool Run(ItemDictionary items, Settings settings)
    {
        bool ok = true;
        void Check(string what, string got, string expect)
        {
            bool pass = got == expect; ok &= pass;
            Console.WriteLine($"  {what,-62} -> {got,-40} {(pass ? "ok" : "MISMATCH, expected " + expect)}");
        }
        Console.WriteLine("\nRe-count of old reward tables (runs counted before 0.10.54, read again from their pictures):");

        // ---- the rules, on made-up runs ----
        var at = new DateTime(2026, 11, 11, 22, 29, 17, DateTimeKind.Utc);
        var gearIds = items.AllItems().Where(i => items.IsGear(i.Id)).Take(3).Select(i => i.Id).ToList();
        var mat = items.AllItems().First(i => !items.IsGear(i.Id) && i.Category != "Currency" && i.Category.Length > 0).Id;
        InstanceRun Run(int gear, int unknown = 0, long kinah = 600_000)
        {
            var r = new InstanceRun { Zone = "Urugugu Canyon", Kind = "Expedition", Start = at.AddMinutes(-8), LastLoot = at, PaidAt = at, TableAt = at, Tables = 1, FromPicture = true, Kinah = kinah };
            for (int i = 0; i < gear; i++) r.Items[gearIds[i % gearIds.Count]] = r.Items.GetValueOrDefault(gearIds[i % gearIds.Count]) + 1;
            r.Items[mat] = 4000;
            r.UnknownItems = r.SlotsUnread = unknown;
            return r;
        }
        var gearOf = RunRecount.GearOf(items);
        Check("slots: each piece of gear, each stack, each unread slot, the coin", $"{RunRecount.Slots(Run(3, 2), gearOf)} {RunRecount.Gear(Run(3, 2), gearOf)}", "7 3");
        string J(InstanceRun s, InstanceRun? n) { var v = RunRecount.Judge(s, n, gearOf); return v.Take ? "take" : v.Same ? "same" : "left: " + v.Why; }
        Check("fewer slots, no more unnamed: taken", J(Run(5, 3), Run(4, 1)), "take");
        Check("the same rewards: nothing to do", J(Run(4, 1), Run(4, 1)), "same");
        Check("more slots than the run had: left", J(Run(3), Run(4)), "left: the new read has more slots (6) than the run (5)");
        Check("a coin read otherwise: left, the Kinah never changes", J(Run(5), Run(4, kinah: 400_000)), "left: its coin read 400,000, the run has 600,000");
        Check("more slots unnamed than the run had: left", J(Run(5, 1), Run(3, 2)), "left: the new read leaves more slots unnamed (2) than the run (1)");
        var named = Run(3, 1); var swapped = Run(3, 1); swapped.Items.Remove(gearIds[0]); swapped.Items[gearIds[2]]++;
        var filledIn = Run(4, 0);
        Check("as many slots, one the run named named otherwise: left", J(Run(3, 1), swapped), "left: as many slots, but an item the run named is named otherwise");
        Check("as many slots, a slot not read before named now: taken", J(named, filledIn), "take");
        Check("no table read from the pictures: left", J(Run(5), new InstanceRun { FromPicture = false }), "left: its pictures weren't read as a table");
        var sb = Run(3); sb.Items.Remove(gearIds[0]); sb.BoundItems[gearIds[0]] = 1; sb.Bound = 1;
        var rb = Run(3);   // the picture alone can't tell the bound piece: the chat said it live
        RunRecount.KeepBound(sb, rb);
        Check("what the run had bound stays bound in the new read", $"{J(sb, rb)} {rb.BoundItems.GetValueOrDefault(gearIds[0])} {rb.Items.ContainsKey(gearIds[0])} {rb.Bound}", "same 1 False 1");
        Check("looked at: an expedition's one table before the fix", RunRecount.NotEligible(Run(1), RunRecount.FixedReaderAt) ?? "yes", "yes");
        var late = Run(1); late.TableAt = RunRecount.FixedReaderAt.AddMinutes(1);
        var two = Run(1); two.Tables = 2;
        var ev = Run(1); ev.Kind = "Event";
        var fixedBefore = Run(1); fixedBefore.GearRepaired = at;
        Check("...not after it, two tables, an event, a repaired one", string.Join(" | ", new[] { late, two, ev, fixedBefore }.Select(r => RunRecount.NotEligible(r, RunRecount.FixedReaderAt))),
            "read by the fixed reader | not one table | not an expedition | repaired before, its correction kept as it is");
        string Name(DateTime t, bool settled) => $"{t.ToLocalTime():yyyyMMdd-HHmmss}-Urugugu-Canyon-reward-table{(settled ? "-settled" : "")}.jpg";
        var pics = new[] { Name(at.AddSeconds(1), false), Name(at.AddSeconds(3), true), Name(at.AddMinutes(9), false) }.Select(RunRecount.Parse).Select(p => p!).ToList();
        Check("its pictures: the first one after its table, then the settled", string.Join(", ", RunRecount.PicturesFor(Run(1), pics).Select(p => p.Settled ? "settled" : "first")), "first, settled");
        pics.Add(RunRecount.Parse(Name(at.AddSeconds(5), false))!);
        Check("two first pictures by its table: which is its own isn't sure", RunRecount.PicturesFor(Run(1), pics).Count.ToString(), "0");
        var stored = Run(5, 3); var read = Run(4, 1);
        var took = RunRecount.Recounted(stored, read, new[] { "a.jpg" }, items, at.AddDays(2));
        Check("taken: the counts that went down kept for the site's correction", $"{took.RepairedFrom.GetValueOrDefault(gearIds[1])} {took.RepairedFrom.ContainsKey(mat)} {took.Resend} {took.Kinah:N0} {took.Recounted != null}", "2 False True 600,000 True");
        stored.Unsent = true; stored.KinahNotRead = true;
        Check("...a run the site never had isn't sent as a correction", RunRecount.Recounted(stored, read, new[] { "a.jpg" }, items, at).Resend.ToString(), "False");

        // ---- a clear lead names a slot past the sure distance (0.10.134) ----
        {
            KinahLedger.Ocr.RewardGrid.Slot S(params (string, double)[] icons) => new(new System.Drawing.Rectangle(0, 0, 135, 135), "Legend", "", 1, icons.ToList());
            string C(KinahLedger.Ocr.RewardGrid.Slot sl) => TrackerSession.CloseEnough(sl, sl.Icons[0].Key, sl.Icons[0].Distance) ? "named" : "not";
            Check("a slot's match: 62 named; 76.7 with the next at 109.9 named; 86 by 88 not; 82 by 120 not",
                string.Join(" ", new[] { S(("a", 62), ("b", 70)), S(("a", 76.7), ("b", 109.9)), S(("a", 86), ("b", 88)), S(("a", 82), ("b", 120)) }.Select(C)), "named named not not");
        }

        // ---- the re-read (0.10.134): runs with slots not read, read again with the reader that sees the slot's backing ----
        {
            var now = at.AddDays(5);
            Check("re-read: a run with a slot not read, read before the new reader", RunRecount.NotEligibleReread(Run(3, 1), now) ?? "yes", "yes");
            var all = Run(3); var after = Run(3, 1); after.TableAt = now.AddMinutes(1); var done = Run(3, 1); done.Reread = at;
            Check("...not one with every slot named, read since, or read again already", string.Join(" | ", new[] { all, after, done }.Select(r => RunRecount.NotEligibleReread(r, now))),
                "every slot named | read by the reader that sees the backing | read again already");
            string JR(InstanceRun s, InstanceRun? n) { var v = RunRecount.Judge(s, n, gearOf, id => items.NameOf(id)); return v.Take ? "take" : v.Same ? "same" : "left: " + v.Why; }
            // the run: 3 gear, the material, one slot not read, its bound coin left at 0; the read: the same plus the slot named
            var st = Run(3, 1, kinah: 0); var rd = Run(4, 0, kinah: 800_000);
            Check("...the slot named, its bound coin the run left at 0: taken", JR(st, rd), "take");
            Check("...nothing more named: not taken", JR(Run(3, 1, kinah: 0), Run(3, 1, kinah: 800_000)), "same");
            // the same piece under the other id of its name (a tradable and a bound copy): the same piece, never a rename
            var twin = items.AllItems().GroupBy(i => i.Name).FirstOrDefault(g => g.Count() >= 2 && g.All(i => items.IsGear(i.Id)));
            if (twin != null)
            {
                var ids = twin.Select(i => i.Id).ToList();
                var other = items.AllItems().First(i => items.IsGear(i.Id) && !ids.Contains(i.Id) && items.NameOf(i.Id) != twin.Key).Id;
                var s2 = Run(0, 1, kinah: 0); s2.Items[ids[0]] = 1;
                var r2 = Run(0, 0, kinah: 0); r2.Items[ids[1]] = 1; r2.Items[other] = 1;
                var m2 = RunRecount.Recounted(s2, r2, new[] { "b.jpg" }, items, now, reread: true);
                Check("...the run's own id kept, only the newly named added", $"{JR(s2, r2)} {m2.Items.GetValueOrDefault(ids[0])} {m2.Items.ContainsKey(ids[1])} {m2.Items.GetValueOrDefault(other)} {m2.RaisedFrom.GetValueOrDefault(other, -1)} {m2.SlotsUnread} {m2.RepairedFrom.Count}",
                    "take 1 False 1 0 0 0");
            }
            var merged = RunRecount.Recounted(st, rd, new[] { "c.jpg" }, items, now, reread: true);
            Check("...merged: the run's Kinah kept, what it raised kept for the site, sent again", $"{merged.Kinah} {merged.RaisedFrom.Count} {merged.Resend} {merged.Reread != null} {merged.Recounted == null}", "0 1 True True True");
            // the correction: raised counts go only to a site that takes corrections that add
            var cfg0 = Update.RemoteConfig.Current;
            try
            {
                Update.RemoteConfig.Current = new Update.RemoteConfig { CorrectionsAdd = false };
                bool offNone = KinahLedger.Upload.RunCorrection.Of(merged, id => id.ToString()) == null;
                Update.RemoteConfig.Current = new Update.RemoteConfig { CorrectionsAdd = true };
                var fix = KinahLedger.Upload.RunCorrection.Of(merged, id => id.ToString());
                Check("...its correction: none while the site takes no additions; then the counts before (the gear piece it had one of)", $"{offNone} {fix?.was.Count} {fix?.was.Values.FirstOrDefault()}", "True 1 1");
            }
            finally { Update.RemoteConfig.Current = cfg0; }
        }

        // ---- the whole pass, on the bank's 17:29 Urugugu table ----
        var pic = TablePicture();
        if (pic == null) { Check("the 17:29 Urugugu table in tracker/tests/replays", "missing", "found"); return ok; }
        if (!ReplayBank.OcrAvailable()) { Console.WriteLine("  (no text recognition here: the pass over a picture is skipped)"); return ok; }
        var keepDir = CrashLog.DirOverride;
        var dir = TestTemp.Path("kl-recount-" + Guid.NewGuid().ToString("N")[..8]);
        var logs = Path.Combine(dir, "logs");
        var data = Path.Combine(dir, "data");
        var shots = Path.Combine(data, "reward-shots");
        var c1 = Path.Combine(data, "characters", "c1");
        Directory.CreateDirectory(logs); Directory.CreateDirectory(shots); Directory.CreateDirectory(c1);
        CrashLog.DirOverride = logs; Characters.Reset();
        try
        {
            // the picture as the run's own (named for the table's local time, as SaveShot names it), and as another
            // run's whose Kinah the table's coin doesn't show
            var other = at.AddDays(-1);
            File.Copy(pic, Path.Combine(shots, Name(at.AddSeconds(1), false)));
            File.Copy(pic, Path.Combine(shots, Name(other.AddSeconds(1), false)));
            // what the fixed reader reads from it
            var empty = new InstanceRun { Zone = "Urugugu Canyon", Kind = "Expedition", Start = at.AddMinutes(-8), LastLoot = at, PaidAt = at, TableAt = at, Tables = 1, FromPicture = true, Kinah = 600_000 };
            var notes = new List<string>();
            var now = RunRecount.ReadAsync(empty, new[] { Path.Combine(shots, Name(at.AddSeconds(1), false)) }, settings, items, "Siel (Elyos)", Array.Empty<InstanceRun>(), notes).GetAwaiter().GetResult()!;
            Check("the table read again: its slots, its coin as the run's", $"{RunRecount.Slots(now, gearOf)} {now.Kinah:N0} {now.FromPicture}", "27 600,000 True");
            Check("...and its notes kept off reads.log", $"{notes.Count > 0} {File.Exists(Path.Combine(logs, "reads.log")) && File.ReadAllText(Path.Combine(logs, "reads.log")).Contains("reward table (first row")}", "True False");
            // the run as 0.10.53 filed it: the table's items, a Unique bow from the floor, eight slots of planks not read
            var bow = items.AllItems().FirstOrDefault(i => i.Name.Contains("Tranquility Bow", StringComparison.OrdinalIgnoreCase) && items.IsGear(i.Id))?.Id ?? gearIds[0];
            var phantom = now.Clone();
            phantom.Items[bow] = phantom.Items.GetValueOrDefault(bow) + 1;
            phantom.UnknownItems += 8; phantom.SlotsUnread += 8;
            for (int i = 0; i < 8; i++) phantom.UnknownSlots.Add($"Common x1 · {Name(at.AddSeconds(1), false)}");
            phantom.Recounted = null; phantom.TableChat = new();
            var coinOff = phantom.Clone(); coinOff.Start = other.AddMinutes(-8); coinOff.TableAt = coinOff.PaidAt = coinOff.LastLoot = other; coinOff.Kinah = 900_000;
            var noPic = phantom.Clone(); noPic.Start = at.AddDays(-2); noPic.TableAt = noPic.PaidAt = noPic.LastLoot = at.AddDays(-2).AddMinutes(8);
            var after = phantom.Clone(); after.Start = RunRecount.FixedReaderAt.AddHours(1); after.TableAt = after.PaidAt = after.LastLoot = after.Start.AddMinutes(8);
            var file = Path.Combine(c1, "instances.jsonl");
            File.WriteAllLines(file, new[] { noPic, coinOff, phantom, after }.Select(r => JsonSerializer.Serialize(r)));
            var original = File.ReadAllLines(file);
            var plan = RunRecount.PlanAsync(data, shots, settings, items, RunRecount.FixedReaderAt, _ => "Siel (Elyos)").GetAwaiter().GetResult();
            Check("looked at: the three before the fix", plan.Looked.ToString(), "3");
            Check("changed: the 17:29 run, 36 slots back to 27, the bow gone", plan.Changes.Count == 1 ? $"{plan.Changes[0].Verdict.SlotsBefore} -> {plan.Changes[0].Verdict.SlotsAfter}, {plan.GearRemoved} gear, bow {plan.Changes[0].After.Items.ContainsKey(bow) && plan.Changes[0].After.Items[bow] >= phantom.Items[bow]}" : $"{plan.Changes.Count} changed",
                $"{RunRecount.Slots(phantom, gearOf)} -> 27, 1 gear, bow False");
            Check("left: no picture, a coin the table doesn't show", string.Join(" | ", plan.Left.OrderBy(l => l.Run.Start).Select(l => l.Why)), "no table picture kept | its coin read 600,000, the run has 900,000");
            Check("nothing written by the plan", File.ReadAllLines(file).SequenceEqual(original).ToString(), "True");
            int written = RunRecount.Apply(plan);
            var lines = File.ReadAllLines(file);
            var got = lines.Select(l => JsonSerializer.Deserialize<InstanceRun>(l)!).ToList();
            Check("written: one line, in its place, the others as they were", $"{written} {lines.Length} {lines[0] == original[0]} {lines[1] == original[1]} {lines[2] == original[2]} {lines[3] == original[3]}", "1 4 True True False True");
            Check("...its Kinah, times and pictures; marked recounted and to correct", $"{got[2].Kinah:N0} {got[2].Start == phantom.Start} {got[2].Pictures.Count} {got[2].Recounted != null} {got[2].Resend} {got[2].RepairedFrom.GetValueOrDefault(bow)}", $"600,000 True 1 True True {phantom.Items[bow]}");
            Check("...the slots of floor gone, the file before kept beside it", $"{got[2].UnknownItems} {File.ReadAllLines(Path.Combine(c1, RunRecount.BackupName)).SequenceEqual(original)}", $"{now.UnknownItems} True");
            Check("the site's correction: was the bow, now none", Upload.RunCorrection.Of(got[2], id => items.KeyOf(id)) is { } fix ? $"{fix.was.GetValueOrDefault(items.KeyOf(bow))} {Upload.InstanceRunPayload.From(new[] { got[2] }, id => items.KeyOf(id))![0].seconds}" : "no correction", $"{phantom.Items[bow]} 0");
            var again = RunRecount.PlanAsync(data, shots, settings, items, RunRecount.FixedReaderAt, _ => "Siel (Elyos)").GetAwaiter().GetResult();
            Check("a second pass: the recounted run isn't looked at again", $"{again.Looked} {again.Changes.Count}", "2 0");
            Check("a line changed meanwhile is never written over", $"{RunRecount.Apply(plan)}", "0");
        }
        finally { CrashLog.DirOverride = keepDir; Characters.Reset(); }
        return ok;
    }
}
