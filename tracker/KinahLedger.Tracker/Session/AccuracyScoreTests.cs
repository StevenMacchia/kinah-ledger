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
        var dir = TestTemp.Path("kl-accscore-" + Guid.NewGuid().ToString("N")[..8]);
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

            // 2026-10-25: the measure is "of what a second source could check, how much matched"; a pickup one source saw
            // is not checked, never against it, and a percentage shows only when something was contradicted. The old
            // expectations (a "verified 75%", "Mostly verified", "Check these") went with the old share, which counted every
            // one-source pickup and reward slot against the player (20% and 28% on 2026-10-24 with nothing wrong).
            string Counts(AccuracyCardView v) => string.Join(",", v.Counts.Select(c => c.Count));

            // 1. every pickup confirmed: all matched, nothing to look at, calm everywhere
            var all = Kina(12).Snapshot();
            var av = AccuracyCardView.From(all);
            Check("all confirmed: all matched, nothing to look at", $"{av.Chip} | {av.Line} | {Counts(av)} | {av.Hud} | {av.Discord}",
                "✓ Nothing looked wrong | Checked by the game: all 12 matched | 12,12,0,0 | ✓ Nothing looked wrong | ✓ Checked by the game: all 12 matched");

            // 2. Steven's evening of 2026-10-24 (audit 19:08:38, "verified 20%"): crafting in town, three Kina pickups the
            // counter confirmed and twelve things only one source saw, nothing contradicted. No percentage, no alarm
            var town = Kina(3);
            for (int i = 0; i < 12; i++) town.Counted(t0.AddSeconds(100 + i * 20), "Sapphire Powder ×1", false, "i:sapphire powderx1", null, false);
            var ts = town.Snapshot(); var tv = AccuracyCardView.From(ts);
            Check("one-source pickups, nothing against: calm, no %, the old share was 20%", $"{tv.Chip} | {tv.Line} | {Counts(tv)} | {tv.Hud ?? "none"} | old {ts.Percent}%",
                "✓ Nothing looked wrong | Checked by the game: all 3 matched | 3,3,0,12 | none | old 20%");
            Check("...the audit line says both", AccuracyScore.AuditClause(ts), "checked 3, matched 3, 0 to look at, 12 not checked · old verified share 20%");
            var once = new AccuracyScore();
            for (int i = 0; i < 12; i++) once.Counted(t0.AddSeconds(i * 20), "Sapphire Powder ×1", false, "i:sapphire powderx1", null, false);
            var ov = AccuracyCardView.From(once.Snapshot());
            Check("nothing a second source could check: nothing looked wrong", $"{ov.Chip} | {ov.Line} | {Counts(ov)}", "✓ Nothing looked wrong | Nothing looked wrong | 0,0,0,12");

            // 3. one Kina line the counter refused: contradicted, so the share shows, and it is on the list
            var r = Kina(12);
            r.Refused(t0.AddSeconds(70), "Acquired 2367 Kina.", 2367);
            var rs = r.Snapshot(); var rv = AccuracyCardView.From(rs);
            Check("one refused: 12 of 13 matched, the % shows", $"{rs.Matched}/{rs.Checked} {rs.Contradicted} | {rv.Line} | {Counts(rv)}", "12/13 1 | Checked by the game: 12 of 13 matched (92%) | 13,12,1,0");
            Check("...on the list, with why; the chip and the panel say so", $"{rs.ToCheck.Count} {rs.ToCheck[0].Reason} | {rv.Chip} {rv.Tone} | {rv.Hud} | {rv.Discord}",
                "1 refused by the counter | 1 to look at warn | 1 to look at | Checked by the game: 12 of 13 matched (92%)");
            Check("...the row in plain words", $"{rv.Rows[0].Text} | {rv.Rows[0].Why}", "2,367 Kinah | The Kinah counter didn't rise for it, so it wasn't counted.");
            r.Dismiss(rs.ToCheck[0].Id); var dm = r.Snapshot(); var dv = AccuracyCardView.From(dm);
            Check("...Fine takes it off the list; the share stays", $"{dm.ToCheck.Count} {dm.MatchPercent}% | {dv.Chip} {dv.Tone}", "0 92% | ✓ All looked at good");
            // 0.8.34 (Steven, 2026-10-25: a low accuracy % "makes the tool look useless"): what a player sees with Show
            // accuracy details off. Nothing while nothing needs a look; with something to look at, only that, never a figure
            var pAll = AccuracyCardView.From(all, details: false); var pTown = AccuracyCardView.From(ts, details: false);
            Check("player view, all fine: no chip, no line, no counts, no panel or Discord line",
                $"'{pAll.Chip}' '{pAll.Line}' {pAll.Counts.Count} {pAll.Hud ?? "none"} {pAll.Discord ?? "none"} | '{pTown.Chip}' {pTown.Hud ?? "none"}", "'' '' 0 none none | '' none");
            var rsFresh = Kina(12); rsFresh.Refused(t0.AddSeconds(70), "Acquired 2367 Kina.", 2367); var pr0 = AccuracyCardView.From(rsFresh.Snapshot(), details: false);
            // 0.9.0 (Steven, 2026-10-30): not even "1 to look at"; Report a miscount is the one way in. Still on the list underneath
            Check("player view, one refused: no chip, no line, no panel line, no % anywhere; the check is still kept",
                $"'{pr0.Chip}' {pr0.Tone} | '{pr0.Line}' | {pr0.Counts.Count} | {pr0.Hud ?? "none"} | {pr0.Discord ?? "none"} | {pr0.Rows.Count}",
                "'' good | '' | 0 | none | none | 1");
            Check("...details on: the full card as before", AccuracyCardView.From(rsFresh.Snapshot(), details: true).Line, "Checked by the game: 12 of 13 matched (92%)");
            var small = Kina(5); small.Refused(t0.AddSeconds(70), "Acquired 999 Kina.", 999);
            var smv = AccuracyCardView.From(small.Snapshot());
            Check("under 10 checked: the counts, no %, no Discord line", $"{smv.Line} | {smv.Hud} | {smv.Discord ?? "none"}", "Checked by the game: 5 of 6 matched | 1 to look at | none");

            // 4. a partly read event run: one thing to look at; its slots read off the picture are not checked, never against
            var pr = new InstanceRun { Zone = "Shugo Festival", Kind = "Event", Event = "Hidden Lugi", Start = t0.AddMinutes(10), LastLoot = t0.AddMinutes(14),
                FromPicture = true, PartlyRead = true, SlotsUnread = 5, UnknownGear = 9, UnknownItems = 4, PictureSlots = 46, Pictures = { "20261024-1606-event-rewards-1.png" } };
            var ps = Kina(12).Snapshot(new[] { pr }); var pv = AccuracyCardView.From(ps);
            var item = ps.ToCheck.Single();
            Check("a partly read event: on the list, its read slots not checked",
                $"{item.Text} | {item.Reason} | {item.Handle[..4]} | {ps.SlotsRead} read {ps.SlotsUnread} not | {Counts(pv)}", "Hidden Lugi: 5 reward slots not read, 9 gear not named | partly read card | run: | 32 read 14 not | 12,12,1,32");
            Check("...the card says so, and nothing against the 12 that matched", $"{pv.Chip} | {pv.Line} | old {ps.Percent}%", "1 to look at | Checked by the game: all 12 matched | old 75%");

            // 5. little counted: no panel line and no Discord line until ten were checked; nothing at all says so
            var few = Kina(5).Snapshot(); var fv = AccuracyCardView.From(few);
            Check("under 10 checked, nothing to look at: no panel line", $"{fv.Chip} | {fv.Line} | {fv.Hud ?? "none"} | {fv.Discord ?? "none"}",
                "✓ Nothing looked wrong | Checked by the game: all 5 matched | none | none");
            var none0 = AccuracyCardView.From(new AccuracyScore().Snapshot());
            Check("nothing counted yet", $"{none0.Line} | {none0.Hud ?? "none"} | {none0.HasRows}", "Nothing counted yet | none | False");

            // 6. rows: a chat pickup the list matched is verified; a row no chat line confirmed is on the list; a late chat line saves one
            var rowA = Kina(10);
            rowA.Counted(t0.AddSeconds(100), "Thick hide ×1", false, "i:thick hidex1", null, false);
            rowA.RowMatched("i:thick hidex1", t0.AddSeconds(101));
            rowA.RowWaiting(t0.AddSeconds(110), "Spiritstone ×1", "i:spiritstonex1");
            rowA.RowWaiting(t0.AddSeconds(111), "Odyle ×2", "i:odylex2");
            rowA.RowConfirmed("i:odylex2", t0.AddSeconds(118));
            rowA.Counted(t0.AddSeconds(118), "Odyle ×2", false, "i:odylex2", null, false);
            rowA.Tick(t0.AddSeconds(140));
            var ra = rowA.Snapshot();
            Check("rows: matched and late-confirmed verified, one no chat line (contradicted)", $"{ra.RowsConfirmed} confirmed {ra.RowsNoChat} no chat | {ra.ToCheck.Single().Text} {ra.ToCheck.Single().Reason} | {ra.Matched}/{ra.Checked}",
                "2 confirmed 1 no chat | Spiritstone ×1 no chat line | 12/13");
            // a count assumed: the pickup stays counted, not checked (its count never showed), and is on the list
            var ca = Kina(10);
            ca.Counted(t0.AddSeconds(200), "Odyle ×1", false, "i:odylex1", null, false);
            ca.RowMatched("i:odylex1", t0.AddSeconds(200));
            ca.CountAssumed(t0.AddSeconds(202), "Acquired Odyle");
            var cs = ca.Snapshot(); var cv = AccuracyCardView.From(cs);
            Check("count assumed 1: not checked though the list saw it, on the list", $"{cs.Matched}/{cs.Checked} {cs.NotChecked} not checked {cs.CountsAssumed} assumed | {cs.ToCheck.Single().Text} {cs.ToCheck.Single().Reason} | {cv.Chip} | {cv.Line}",
                "10/10 1 not checked 1 assumed | Odyle ×1 count assumed 1 | 1 to look at | Checked by the game: all 10 matched");
            var lone = new AccuracyScore();
            lone.Counted(t0.AddSeconds(300), "Odyle ×1", false, "i:odylex1", null, false); lone.CountAssumed(t0.AddSeconds(301), "Acquired Odyle");
            Check("...with nothing checked at all, the line says so plainly", AccuracyCardView.From(lone.Snapshot()).Line, "Nothing a second source could check yet");

            // 7. the Kinah counter against the count: exact, under, over; a spend or a sale is left out
            AccuracyScore.Reconciliation? Rec(params (long Bal, long Cnt)[] rd) { var a = new AccuracyScore(); for (int i = 0; i < rd.Length; i++) a.Reading(rd[i].Bal, rd[i].Cnt, t0.AddSeconds(i * 3)); return a.Snapshot().Counter; }
            Check("counter: exact", Rec((1000, 0), (1463, 463), (1900, 900))?.Words ?? "none", "exact");
            Check("counter: under (a pickup the count missed)", Rec((1000, 0), (1800, 500))?.Words ?? "none", "under by 300");
            Check("counter: over (a line counted twice)", Rec((1000, 0), (1300, 500))?.Words ?? "none", "over by 200");
            Check("counter: a spend and a sale left out", Rec((1000, 0), (400, 0), (863, 463), (60_863, 463), (61_163, 763))?.Words ?? "none", "exact");
            Check("counter: never read: none", Rec()?.Words ?? "none", "none");

            // 8. the miscount report's prefill for a list item: time, text, why, the moment its pictures are kept around
            var refused = rs.ToCheck[0];
            var pf = AccuracyCardView.Prefill(refused, sendPictures: true);
            Check("prefill: the report's first line, its moment, pictures when Sharing allows",
                $"{pf.Why} | {pf.Focus:O} | {pf.SendPictures} {AccuracyCardView.Prefill(refused, false).SendPictures}",
                $"{t0.AddSeconds(70).ToLocalTime():HH:mm:ss} 2,367 Kinah: refused by the counter | {t0.AddSeconds(70):O} | True False");
            var pfr = AccuracyCardView.Prefill(item, true);
            Check("prefill: a run has no moment, its reward pictures named", $"{pfr.Focus?.ToString() ?? "none"} | {pfr.Why.Contains("20261024-1606-event-rewards-1.png")}", "none | True");

            // 9. the pictures and reads around a thing to look at outlive the minute, for its Report
            var buf = new ReportBuffer();
            buf.Lines("chat", new[] { "Acquired 2367 Kina." }, t0);
            buf.Lines("chat", new[] { "Acquired 410 Kina." }, t0.AddSeconds(30));
            buf.Pin(t0);
            buf.Lines("chat", new[] { "Acquired 400 Kina." }, t0.AddMinutes(5));   // the minute moves on
            var folder = buf.Write(dir, "test", Array.Empty<string>(), t0.AddMinutes(5), focus: t0);
            var trace = File.ReadAllLines(Path.Combine(folder, "trace.jsonl"));
            Check("pinned: the moment's read is in its report minutes later", $"{buf.Held.Reads} held, {buf.PinnedAround(t0).Reads} pinned | {trace.Length} in report | {trace[0].Contains("2367")}", "1 held, 1 pinned | 2 in report | True");

            // 10. the run payload's acc: the coordinator's names, and none when nothing was judged. Since 2026-10-25 three
            // names follow them (checked, matched, not_checked: the player's measure); the eight before keep their meaning
            // and order, and the site's accBlock drops names it doesn't know
            var runA = Kina(20, t0.AddMinutes(30));
            runA.Refused(t0.AddMinutes(30).AddSeconds(50), "Acquired 999 Kina.", 999);
            runA.Delay(t0.AddMinutes(30).AddSeconds(5), 0.8); runA.Delay(t0.AddMinutes(30).AddSeconds(9), 1.2); runA.Delay(t0.AddMinutes(30).AddSeconds(12), 2.0);
            var exp = new InstanceRun { Zone = "Urugugu Canyon", Kind = "Expedition", Start = t0.AddMinutes(30), LastLoot = t0.AddMinutes(32), FromPicture = true, Kinah = 600_000, Items = { [items.IdOf("Odyle")] = 3 } };
            exp.Acc = runA.ForRun(exp);
            var pay = InstanceRunPayload.From(new[] { exp }, id => items.KeyOf(id))!;
            var accJson = JsonSerializer.Serialize(pay[0].acc);
            Check("run acc: the names the site reads", string.Join(",", JsonDocument.Parse(accJson).RootElement.EnumerateObject().Select(p => p.Name)),
                "status,kina_confirmed,rows_confirmed,unsure,not_read,partly_read,delay_ms,ver,checked,matched,not_checked");
            Check("...values: 20 of 21 Kina, one unsure, partly", $"{exp.Acc!.status} {exp.Acc.kina_confirmed} {exp.Acc.rows_confirmed?.ToString() ?? "null"} {exp.Acc.unsure} {exp.Acc.not_read} {exp.Acc.partly_read} {exp.Acc.delay_ms} {exp.Acc.ver == RunPayload.AppVersion}",
                "partly 0.952 null 1 0 False 1200 True");
            Check("...the player's measure beside them: 21 checked, 20 matched, its reward slot not checked", $"{exp.Acc.@checked} {exp.Acc.matched} {exp.Acc.not_checked}", "21 20 1");
            var halfQuiet = Kina(10, t0.AddMinutes(40));
            for (int i = 0; i < 10; i++) halfQuiet.Counted(t0.AddMinutes(40).AddSeconds(60 + i * 5), $"{700 + i:N0} Kinah", true, "k:" + (700 + i), "unjudged", true);
            var hq = halfQuiet.ForRun(new InstanceRun { Zone = "Urugugu Canyon", Kind = "Expedition", Start = t0.AddMinutes(40), LastLoot = t0.AddMinutes(42) })!;
            Check("...the counter quiet for half the run: kina_confirmed as before, all 10 checked matched", $"{hq.kina_confirmed} {hq.status} | {hq.@checked} {hq.matched} {hq.not_checked}", "0.5 unconfirmed | 10 10 10");
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
            Check("...its slots read off the picture: not checked", $"{ev.ForRun(evRun)?.@checked} {ev.ForRun(evRun)?.matched} {ev.ForRun(evRun)?.not_checked}", "0 0 45");
            // a reward screen's coin (0.8.60, the first reading reports: Kina shares of 0 or none): on its picture alone it is
            // no chat line the counter could confirm, so it is out of the Kina share and not counted beside its slot; with the
            // counter's rise it is a confirmed Kina line. 0.8.58 counted it as one the counter missed: a share of 0, unconfirmed
            var cs0 = t0.AddHours(4);
            var coinRun = new InstanceRun { Zone = "Urugugu Canyon", Kind = "Expedition", Start = cs0, LastLoot = cs0.AddMinutes(1), FromPicture = true, PictureSlots = 1 };
            AccuracyScore Coin(string witness)
            {
                var sc = new AccuracyScore();
                for (int i = 0; i < 6; i++) sc.Counted(cs0.AddSeconds(10 + i), $"Odyle ×{i + 1}", false, "i:odyle" + i, "row", false);
                sc.Counted(cs0.AddSeconds(30), "600,000 Kinah", true, "k:600000", witness, true);
                return sc;
            }
            var onPicture = Coin("picture").ForRun(coinRun)!; var byCounter = Coin("counter").ForRun(coinRun)!;
            Check("run acc: a reward coin on its picture alone is no Kina line; with the counter's rise it is a confirmed one",
                $"{onPicture.kina_confirmed?.ToString() ?? "null"} {onPicture.status} | {byCounter.kina_confirmed?.ToString() ?? "null"} {byCounter.status}", "null verified | 1 verified");

            // 11. the once-a-minute audit line carries one short clause; the session's own wiring feeds the score
            string[] AuditLines() { try { return File.ReadAllLines(CrashLog.ReadsPath).Where(l => l.Contains("audit: ")).ToArray(); } catch (Exception) { return Array.Empty<string>(); } }
            var s = new TrackerSession(null, settings, items, "Test") { Vote = false };
            for (int i = 0; i < 12; i++) s.Score.Counted(t0.AddSeconds(i), $"{500 + i} Kinah", true, "k:" + (500 + i), "counter", true);
            var a0 = DateTime.UtcNow;   // the audit's own clock starts at the session's birth
            s.Audit(a0); s.NotePass(500); s.Audit(a0.AddSeconds(61));
            const string clause = "· checked 12, matched 12, 0 to look at, 0 not checked · old verified share 100%";
            Check("audit line: what the player sees, then the old share", AuditLines().LastOrDefault()?.Contains(clause) == true ? "both" : AuditLines().LastOrDefault() ?? "no line", "both");
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
