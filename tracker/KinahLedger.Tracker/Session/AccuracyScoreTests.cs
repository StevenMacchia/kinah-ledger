using System.Text.Json;
using KinahLedger.Data;
using KinahLedger.Ui;
using KinahLedger.Upload;

namespace KinahLedger.Session;

/// <summary>
/// The Accuracy card's model (AccuracyScore, 2026-10-24), part of parse-test: the verified share on synthetic tallies, the
/// things to check, not enough counted, the Kinah counter's reconciliation, the miscount report's prefill, the pictures
/// kept around a thing to check, the run payload's acc, the audit clause, and the session's own wiring (a Kina line the
/// counter confirms or refuses). Writes only to a scratch folder.
/// </summary>
public static class AccuracyScoreTests
{
    public static bool Run(ItemDictionary items, Settings settings)
    {
        bool ok = true;
        void Check(string what, string got, string expect)
        {
            bool pass = got == expect; ok &= pass;
            Console.WriteLine($"  {what,-62} -> {got,-40} {(pass ? "ok" : "MISMATCH, expected " + expect)}");
        }
        Console.WriteLine("\nAccuracy card: verified by the game's own numbers (2026-10-24):");
        var keepDir = CrashLog.DirOverride;
        var dir = Path.Combine(Path.GetTempPath(), "kl-accscore-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        CrashLog.DirOverride = dir;
        try
        {
            var t0 = new DateTime(2026, 10, 24, 17, 40, 0, DateTimeKind.Utc);
            AccuracyScore Kina(int n, DateTime? at = null)
            {
                var a = new AccuracyScore();
                for (int i = 0; i < n; i++) a.Counted((at ?? t0).AddSeconds(i * 5), $"{400 + i:N0} Kinah", true, "k:" + (400 + i), "counter", true);
                return a;
            }

            // 1. every pickup confirmed: 100%, verified, nothing to check
            var all = Kina(12).Snapshot();
            var av = AccuracyCardView.From(all);
            Check("all confirmed: 100%, Verified, nothing to check", $"{all.Percent}% {all.Word} | {av.Line} | {av.Secondary} | {av.Hud}",
                "100% Verified | Verified by the game's own numbers: 100% | Nothing to check | ✓ 100% verified");

            // 2. one Kina line the counter refused: in the share and on the list
            var r = Kina(12);
            r.Refused(t0.AddSeconds(70), "Acquired 2367 Kina.", 2367);
            var rs = r.Snapshot(); var rv = AccuracyCardView.From(rs);
            Check("one refused: the share drops (12 of 13), Mostly verified", $"{rs.Verified}/{rs.Judged} {rs.Percent}% {rs.Word}", "12/13 92% Mostly verified");
            Check("...on the list, with why, and the panel says so", $"{rs.ToCheck.Count} {rs.ToCheck[0].Reason} | {rv.Secondary} | {rv.Hud}", "1 refused by the counter | 1 pickup unsure · tap to see | ? 1 to check");
            Check("...the row in plain words", $"{rv.Rows[0].Text} | {rv.Rows[0].Why}", "2,367 Kinah | The Kinah counter didn't rise for it, so it wasn't counted.");
            r.Dismiss(rs.ToCheck[0].Id); var dm = r.Snapshot();
            Check("...Fine takes it off the list; the share stays", $"{dm.ToCheck.Count} {dm.Percent}%", "0 92%");

            // 3. a partly read event run: one thing to check, its slots in the share
            var pr = new InstanceRun { Zone = "Shugo Festival", Kind = "Event", Event = "Hidden Lugi", Start = t0.AddMinutes(10), LastLoot = t0.AddMinutes(14),
                FromPicture = true, PartlyRead = true, SlotsUnread = 5, UnknownGear = 9, UnknownItems = 4, PictureSlots = 46, Pictures = { "20261024-1606-event-rewards-1.png" } };
            var ps = Kina(12).Snapshot(new[] { pr }); var pv = AccuracyCardView.From(ps);
            var item = ps.ToCheck.Single();
            Check("a partly read event: on the list, slots not read in the share",
                $"{item.Text} | {item.Reason} | {item.Handle[..4]} | {ps.SlotsRead} read {ps.SlotsUnread} not | {ps.Percent}%", "Hidden Lugi: 5 reward slots not read, 9 gear not named | partly read card | run: | 32 read 14 not | 75%");
            Check("...the card says so (not a pickup), Check these under 80%", $"{pv.Secondary} | {ps.Word}", "1 to check · tap to see | Check these");

            // 4. under ten judged events: not enough counted yet
            var few = Kina(5).Snapshot(); var fv = AccuracyCardView.From(few);
            Check("under 10 events: not enough counted yet, no panel line", $"{few.Enough} {few.State} | {fv.Line} | {fv.Secondary} | {fv.Hud ?? "none"} | {fv.Discord ?? "none"}",
                "False NotEnough | Not enough counted yet | Nothing to check | none | none");

            // 5. rows: a chat pickup the list matched is verified; a row no chat line confirmed is on the list; a late chat line saves one
            var rowA = Kina(10);
            rowA.Counted(t0.AddSeconds(100), "Thick hide ×1", false, "i:thick hidex1", null, false);
            rowA.RowMatched("i:thick hidex1", t0.AddSeconds(101));
            rowA.RowWaiting(t0.AddSeconds(110), "Spiritstone ×1", "i:spiritstonex1");
            rowA.RowWaiting(t0.AddSeconds(111), "Odyle ×2", "i:odylex2");
            rowA.RowConfirmed("i:odylex2", t0.AddSeconds(118));
            rowA.Counted(t0.AddSeconds(118), "Odyle ×2", false, "i:odylex2", null, false);
            rowA.Tick(t0.AddSeconds(140));
            var ra = rowA.Snapshot();
            Check("rows: matched and late-confirmed verified, one no chat line", $"{ra.RowsConfirmed} confirmed {ra.RowsNoChat} no chat | {ra.ToCheck.Single().Text} {ra.ToCheck.Single().Reason} | {ra.Verified}/{ra.Judged}",
                "2 confirmed 1 no chat | Spiritstone ×1 no chat line | 12/13");
            // a count assumed: the pickup stays counted, never verified, and is on the list
            var ca = Kina(10);
            ca.Counted(t0.AddSeconds(200), "Odyle ×1", false, "i:odylex1", null, false);
            ca.RowMatched("i:odylex1", t0.AddSeconds(200));
            ca.CountAssumed(t0.AddSeconds(202), "Acquired Odyle");
            var cs = ca.Snapshot();
            Check("count assumed 1: unverified though the list saw it, on the list", $"{cs.Verified}/{cs.Judged} {cs.CountsAssumed} | {cs.ToCheck.Single().Text} {cs.ToCheck.Single().Reason}", "10/11 1 | Odyle ×1 count assumed 1");

            // 6. the Kinah counter against the count: exact, under, over; a spend or a sale is left out
            AccuracyScore.Reconciliation? Rec(params (long Bal, long Cnt)[] rd) { var a = new AccuracyScore(); for (int i = 0; i < rd.Length; i++) a.Reading(rd[i].Bal, rd[i].Cnt, t0.AddSeconds(i * 3)); return a.Snapshot().Counter; }
            Check("counter: exact", Rec((1000, 0), (1463, 463), (1900, 900))?.Words ?? "none", "exact");
            Check("counter: under (a pickup the count missed)", Rec((1000, 0), (1800, 500))?.Words ?? "none", "under by 300");
            Check("counter: over (a line counted twice)", Rec((1000, 0), (1300, 500))?.Words ?? "none", "over by 200");
            Check("counter: a spend and a sale left out", Rec((1000, 0), (400, 0), (863, 463), (60_863, 463), (61_163, 763))?.Words ?? "none", "exact");
            Check("counter: never read: none", Rec()?.Words ?? "none", "none");

            // 7. the miscount report's prefill for a list item: time, text, why, the moment its pictures are kept around
            var refused = rs.ToCheck[0];
            var pf = AccuracyCardView.Prefill(refused, sendPictures: true);
            Check("prefill: the report's first line, its moment, pictures when Sharing allows",
                $"{pf.Why} | {pf.Focus:O} | {pf.SendPictures} {AccuracyCardView.Prefill(refused, false).SendPictures}",
                $"{t0.AddSeconds(70).ToLocalTime():HH:mm:ss} 2,367 Kinah: refused by the counter | {t0.AddSeconds(70):O} | True False");
            var pfr = AccuracyCardView.Prefill(item, true);
            Check("prefill: a run has no moment, its reward pictures named", $"{pfr.Focus?.ToString() ?? "none"} | {pfr.Why.Contains("20261024-1606-event-rewards-1.png")}", "none | True");

            // 8. the pictures and reads around a thing to check outlive the minute, for its Report
            var buf = new ReportBuffer();
            buf.Lines("chat", new[] { "Acquired 2367 Kina." }, t0);
            buf.Lines("chat", new[] { "Acquired 410 Kina." }, t0.AddSeconds(30));
            buf.Pin(t0);
            buf.Lines("chat", new[] { "Acquired 400 Kina." }, t0.AddMinutes(5));   // the minute moves on
            var folder = buf.Write(dir, "test", Array.Empty<string>(), t0.AddMinutes(5), focus: t0);
            var trace = File.ReadAllLines(Path.Combine(folder, "trace.jsonl"));
            Check("pinned: the moment's read is in its report minutes later", $"{buf.Held.Reads} held, {buf.PinnedAround(t0).Reads} pinned | {trace.Length} in report | {trace[0].Contains("2367")}", "1 held, 1 pinned | 2 in report | True");

            // 9. the run payload's acc: the coordinator's names, and none when nothing was judged
            var runA = Kina(20, t0.AddMinutes(30));
            runA.Refused(t0.AddMinutes(30).AddSeconds(50), "Acquired 999 Kina.", 999);
            runA.Delay(t0.AddMinutes(30).AddSeconds(5), 0.8); runA.Delay(t0.AddMinutes(30).AddSeconds(9), 1.2); runA.Delay(t0.AddMinutes(30).AddSeconds(12), 2.0);
            var exp = new InstanceRun { Zone = "Urugugu Canyon", Kind = "Expedition", Start = t0.AddMinutes(30), LastLoot = t0.AddMinutes(32), FromPicture = true, Kinah = 600_000, Items = { [items.IdOf("Odyle")] = 3 } };
            exp.Acc = runA.ForRun(exp);
            var pay = InstanceRunPayload.From(new[] { exp }, id => items.KeyOf(id))!;
            var accJson = JsonSerializer.Serialize(pay[0].acc);
            Check("run acc: the names the site reads", string.Join(",", JsonDocument.Parse(accJson).RootElement.EnumerateObject().Select(p => p.Name)),
                "status,kina_confirmed,rows_confirmed,unsure,not_read,partly_read,delay_ms,ver");
            Check("...values: 20 of 21 Kina, one unsure, partly", $"{exp.Acc!.status} {exp.Acc.kina_confirmed} {exp.Acc.rows_confirmed?.ToString() ?? "null"} {exp.Acc.unsure} {exp.Acc.not_read} {exp.Acc.partly_read} {exp.Acc.delay_ms} {exp.Acc.ver == RunPayload.AppVersion}",
                "partly 0.952 null 1 0 False 1200 True");
            Check("...the payload JSON carries it as acc", JsonSerializer.Serialize(pay[0]).Contains("\"acc\":{\"status\":\"partly\"") ? "acc" : "missing", "acc");
            var clean = Kina(8, t0.AddHours(1));
            var cr = new InstanceRun { Zone = "Urugugu Canyon", Kind = "Expedition", Start = t0.AddHours(1), LastLoot = t0.AddHours(1).AddMinutes(1) };
            Check("run acc: all confirmed is verified", clean.ForRun(cr)?.status ?? "none", "verified");
            var short3 = Kina(3, t0.AddHours(2));
            var sr = new InstanceRun { Zone = "Urugugu Canyon", Kind = "Expedition", Start = t0.AddHours(2), LastLoot = t0.AddHours(2).AddMinutes(1) };
            Check("run acc: under 5 events unconfirmed, with a note", $"{short3.ForRun(sr)?.status} {short3.ForRun(sr)?.note}", "unconfirmed fewer than 5 counted");
            var none = new InstanceRun { Zone = "Urugugu Canyon", Kind = "Expedition", Start = t0.AddHours(5), LastLoot = t0.AddHours(5).AddMinutes(1), Kinah = 1 };
            none.Acc = new AccuracyScore().ForRun(none);
            Check("run acc: nothing judged, nothing sent", $"{none.Acc?.status ?? "null"} {JsonSerializer.Serialize(InstanceRunPayload.From(new[] { none }, id => id.ToString())![0]).Contains("\"acc\"")}", "null False");
            var ev = new AccuracyScore();
            var evRun = new InstanceRun { Zone = "Shugo Festival", Kind = "Event", Event = "Hidden Lugi", Start = t0.AddHours(3), LastLoot = t0.AddHours(3).AddMinutes(4), FromPicture = true, PictureSlots = 45 };
            Check("run acc: an event read in full from its cards (no Kina judged) is verified", $"{ev.ForRun(evRun)?.status} {ev.ForRun(evRun)?.kina_confirmed?.ToString() ?? "null"}", "verified null");

            // 10. the once-a-minute audit line carries one short clause; the session's own wiring feeds the score
            string[] AuditLines() { try { return File.ReadAllLines(CrashLog.ReadsPath).Where(l => l.Contains("audit: ")).ToArray(); } catch (Exception) { return Array.Empty<string>(); } }
            var s = new TrackerSession(null, settings, items, "Test") { Vote = false };
            for (int i = 0; i < 12; i++) s.Score.Counted(t0.AddSeconds(i), $"{500 + i} Kinah", true, "k:" + (500 + i), "counter", true);
            var a0 = DateTime.UtcNow;   // the audit's own clock starts at the session's birth
            s.Audit(a0); s.NotePass(500); s.Audit(a0.AddSeconds(61));
            Check("audit line: verified 100%", AuditLines().LastOrDefault()?.Contains("· verified 100%") == true ? "verified 100%" : AuditLines().LastOrDefault() ?? "no line", "verified 100%");
            var w = new TrackerSession(null, settings, items, "Test") { Vote = false };
            var b0 = t0.AddMinutes(20);
            w.CheckBalance(100_000, b0); w.CheckBalance(100_000, b0.AddSeconds(3));
            w.ProcessChat(new List<string> { "Acquired 463 Kina." }, b0.AddSeconds(4));
            w.CheckBalance(100_463, b0.AddSeconds(5)); w.CheckBalance(100_463, b0.AddSeconds(8));
            w.ProcessChat(new List<string> { "Acquired 463 Kina.", "Acquired 2367 Kina." }, b0.AddSeconds(9));
            w.CheckBalance(100_463, b0.AddSeconds(11)); w.CheckBalance(100_463, b0.AddSeconds(14)); w.CheckBalance(100_463, b0.AddSeconds(17));
            w.ProcessChat(new List<string> { "Acquired 463 Kina.", "Acquired 2367 Kina." }, b0.AddSeconds(31));
            var ws = w.Score.Snapshot();
            Check("session wiring: a confirmed line and a refused one reach the score", $"{ws.KinaConfirmed} confirmed {ws.KinaRefused} refused | {ws.Counter?.Words ?? "none"}", "1 confirmed 1 refused | exact");
        }
        finally { CrashLog.DirOverride = keepDir; try { Directory.Delete(dir, true); } catch (Exception) { } }
        return ok;
    }
}
