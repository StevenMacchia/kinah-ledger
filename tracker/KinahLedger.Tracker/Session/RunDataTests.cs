using System.Drawing;
using System.Text.Json;
using KinahLedger.Data;
using KinahLedger.Ocr;

namespace KinahLedger.Session;

/// <summary>
/// Run data rules (2026-10-24), part of parse-test: gear never stacks in a reward table and a K or M count only on what
/// stacks by the thousand; a table read while another reward screen is up is refused, and a false run's shape is not
/// kept; old records are marked on load (in memory, said once, the file untouched); "in progress" needs loot or a table;
/// a run's pieces; synthetic ids keep their names across a restart. Writes only to a scratch folder.
/// </summary>
public static class RunDataTests
{
    public static bool Run(ItemDictionary items, Settings settings)
    {
        bool ok = true;
        void Check(string what, string got, string expect)
        {
            bool pass = got == expect; ok &= pass;
            Console.WriteLine($"  {what,-62} -> {got,-40} {(pass ? "ok" : "MISMATCH, expected " + expect)}");
        }
        Console.WriteLine("\nRun data: gear in tables, false runs, old records, in progress, pieces, names (2026-10-24):");
        var keepDir = CrashLog.DirOverride;
        var dir = Path.Combine(Path.GetTempPath(), "kl-rundata-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        CrashLog.DirOverride = dir;
        try
        {
            var t0 = new DateTime(2026, 10, 24, 18, 3, 25, DateTimeKind.Utc);
            var box = new Rectangle(0, 0, 10, 10);
            RewardGrid.Slot Slot(string grade, string text, string key, double d) => new(box, grade, text, RewardGrid.ParseCount(text), new() { (key, d), ("Other", 120) });

            // a. the 14:12:48 Urugugu table's own slots: Divine Canyon Pauldrons "x1K" twice, Enhance Stone "x4.0K"
            var pauldron = Slot("Legend", "1K", "Icon_GM_0072_T05_Shoulder", 52);
            var stone = Slot("Common", "4.0K", "icon_item_usable_enchant_bless_stone_003", 59);
            var s = new TrackerSession(null, settings, items, "Test");
            s.Engine.SetZone("Urugugu Canyon"); s.Instances.Zone("Urugugu Canyon", t0);
            s.ApplyGrid(new List<RewardGrid.Slot> { pauldron, pauldron, stone }, t0.AddMinutes(9));
            var run = s.Instances.CurrentRun!;
            string Named(InstanceRun r) => string.Join(", ", r.Items.Concat(r.BoundItems).OrderBy(kv => items.NameOf(kv.Key)).Select(kv => $"{items.NameOf(kv.Key)} x{kv.Value}"));
            Check("a table: gear \"x1K\" counts 1 a piece, a material x4.0K 4,000", Named(run), "Divine Canyon Pauldrons x2, Enhance Stone x4000");
            var log = File.ReadAllText(CrashLog.ReadsPath);
            Check("...and the clamp is logged", $"{log.Split('\n').Count(l => l.Contains("gear never stacks: x1K counts 1"))}", "2");
            var potion = items.AllItems().FirstOrDefault(i => i.Category == "Potion");
            if (potion != null)
                Check("a potion read x1.0K keeps its figures, not a thousand", $"{TrackerSession.TableCount(items, new[] { potion }, Slot(potion.Grade, "1.0K", "k", 30), out var why)} {why != null}", "1 True");
            Check("a plain count is left as read", $"{TrackerSession.TableCount(items, items.AllWithIcons().Where(i => i.Name == "Enhance Stone").ToList(), Slot("Common", "14", "k", 30), out var w2)} {w2 == null}", "14 True");
            Check("pieces: each piece of gear, each other item once", $"{run.Pieces(items.IsGear)}", "(2, 1)");

            // b. a table read while the Daeva Pass Level Reward is up (14:03:29) is that screen's
            var b = new TrackerSession(null, settings, items, "Test");
            b.Engine.SetZone("Urugugu Village"); b.Instances.Zone("Urugugu Village", t0);
            b.Target.MarkReward(t0.AddSeconds(4.3), "Daeva Pass Level Reward");
            b.ApplyGrid(new List<RewardGrid.Slot> { Slot("Legend", "", "Icon_GM_0037_T04_Boots", 36), Slot("Common", "i.0K", "icon_battlepass", 59), stone }, t0.AddSeconds(4.9));
            var br = b.Instances.CurrentRun!;
            Check("a table under another reward screen's title: refused", $"{br.FromPicture} {br.Tables} {br.Items.Count}", "False 0 0");
            Check("...and logged", File.ReadAllText(CrashLog.ReadsPath).Contains("reward table refused: the Daeva Pass Level Reward screen is up") ? "logged" : "no line", "logged");

            // b. the false run's shape: no Kinah, the minimap alone, under 60 s, its one table within 15 s of entry
            InstanceRun Shape(Action<InstanceRun>? f = null)
            {
                var r = new InstanceRun { Zone = "Urugugu Canyon", Kind = "Expedition", Start = t0, LastLoot = t0.AddSeconds(5), Witnesses = "minimap 14:03:25", Tables = 1, TableAt = t0.AddSeconds(4), FromPicture = true };
                r.Items[1] = 1000; f?.Invoke(r); return r;
            }
            Check("false run: the 14:03 shape", $"{InstanceLog.FalseRun(Shape())}", "True");
            Check("...not with Kinah, a chat witness, 60 s, a late table or none",
                $"{InstanceLog.FalseRun(Shape(r => r.Kinah = 600_000))} {InstanceLog.FalseRun(Shape(r => r.Witnesses += " · chat: joined event 14:03:20"))} {InstanceLog.FalseRun(Shape(r => r.LastLoot = t0.AddSeconds(60)))} {InstanceLog.FalseRun(Shape(r => r.TableAt = t0.AddSeconds(16)))} {InstanceLog.FalseRun(Shape(r => r.Witnesses = ""))}",
                "False False False False False");
            var fl = new InstanceLog();
            fl.Zone("Urugugu Village", t0);
            fl.RewardTable(t0.AddSeconds(4), new GameEvent[] { new ItemAcquired(t0.AddSeconds(4), 1, 1000, false) { Via = "reward table" } }, 0);
            fl.Zone("Windy Valley", t0.AddSeconds(5));   // one read of the next area: the run stays open
            fl.Close();
            Check("a false run closing: not kept", $"{fl.Snapshot().Count} {File.Exists(Path.Combine(dir, "instances.jsonl"))}", "0 False");
            Check("...logged as not a run", File.ReadAllText(CrashLog.ReadsPath).Contains("not a run: Urugugu Canyon") ? "logged" : "no line", "logged");

            // d. in progress: an open run with loot or a table; a run just opened, or split open, with nothing in it is not
            var ip = new InstanceLog();
            ip.Zone("Urugugu Canyon", t0);
            string Ip() => $"{ip.Open ?? "-"} / {ip.InProgress ?? "-"}";
            Check("in progress: a run just opened, nothing in it", Ip(), "Urugugu Canyon / -");
            ip.Loot(new KinahChanged(t0.AddMinutes(2), 10_000, true));
            Check("...with loot it is", Ip(), "Urugugu Canyon / Urugugu Canyon");
            ip.RewardTable(t0.AddMinutes(9), new GameEvent[] { new KinahChanged(t0.AddMinutes(9), 600_000, false) { Via = "reward table" } }, 0);
            ip.TableStarting(t0.AddMinutes(11));   // a second table: the first closes, the next opens empty
            Check("...after a split the next run is open but not in progress", Ip(), "Urugugu Canyon / -");
            ip.Close();

            // c. old records: marked in memory on load, said once ever, the file untouched
            InstanceRun Rec(int minute, double secs, long kinah, bool pic = true, string zone = "Urugugu Canyon", string witnesses = "minimap", string ended = "time for rewards over")
            {
                var st = new DateTime(2026, 10, 23, 12, 0, 0, DateTimeKind.Utc).AddMinutes(minute);
                return new InstanceRun { Zone = zone, Kind = "Expedition", Start = st, LastLoot = st.AddSeconds(secs), Kinah = kinah, FromPicture = pic, Witnesses = witnesses.Length > 0 ? $"{witnesses} 12:00:00" : "", Ended = ended, Items = new() { [1] = 3 } };
            }
            var recs = new List<InstanceRun>
            {
                Rec(0, 508, 0, pic: false, witnesses: "", ended: ""),     // the 17:48 Oct 23 chat-counted record, Kinah 0
                Rec(20, 2224, 3_000_000, pic: false, witnesses: "", ended: ""),   // the 18:26 Oct 23 record: five runs
                Rec(70, 433, 600_000), Rec(80, 485, 614_000), Rec(90, 353, 600_000), Rec(100, 373, 600_000),
                Rec(200, 568, 600_000, zone: "Vakron Sky Island"), Rec(210, 517, 43_000, zone: "Vakron Sky Island"), Rec(220, 682, 10_000, zone: "Vakron Sky Island"), Rec(230, 606, 0, zone: "Vakron Sky Island"),
                Shape(r => { r.Start = r.Start.AddDays(-1); r.LastLoot = r.LastLoot!.Value.AddDays(-1); r.TableAt = r.TableAt!.Value.AddDays(-1); }),
            };
            var file = Path.Combine(dir, "instances.jsonl");
            File.WriteAllLines(file, recs.Select(r => JsonSerializer.Serialize(r)));
            var before = File.ReadAllText(file);
            var loaded = new InstanceLog();
            string Marks(InstanceLog l) => string.Join(" | ", l.Snapshot().Where(r => r.OutOfAverages).Select(r => $"{r.Start:HH:mm} {(r.OldUnread ? "old" : "")}{(r.Suspect.Length > 0 ? (r.OldUnread ? ", " : "") + r.Suspect : "")}"));
            Check("old records marked: chat-counted, five in one, the false run", Marks(loaded), "12:00 old | 12:20 old, looks like several runs | 18:03 not a run");
            Check("...a Vakron run of 600,000 beside unread coins is not flagged", $"{loaded.Snapshot().Count(r => r.Zone == "Vakron Sky Island" && r.Suspect.Length > 0)}", "0");
            Check("...the file is not rewritten", File.ReadAllText(file) == before ? "same" : "changed", "same");
            int Lines() => File.ReadAllText(CrashLog.ReadsPath).Split('\n').Count(l => l.Contains("old run record:"));
            Check("...one log line per mark", $"{Lines()}", "4");
            var again = new InstanceLog();
            Check("...once ever: a second load says nothing more", $"{Lines()} {Marks(again).Split(" | ").Length}", "4 3");
            Check("...and they stay out of the replay of unsent runs", $"{again.Unsent(DateTime.MinValue).Count}", "0");

            // e. synthetic ids keep their names across a restart
            File.Delete(file);
            var d1 = new ItemDictionary();
            long odd = d1.IdOf("Zzyzx Shard");
            var nl = new InstanceLog(); nl.UseNames(d1);
            nl.Zone("Urugugu Canyon", t0);
            nl.Loot(new ItemAcquired(t0.AddMinutes(1), odd, 2, false) { Via = "reward table" });
            nl.Close();
            var d2 = new ItemDictionary();
            Check("a restart without the name: unknown before the runs load", d2.NameOf(odd), $"item {odd}");
            new InstanceLog().UseNames(d2);
            Check("...named again from the run's record", $"{odd < 0} {d2.NameOf(odd)}", "True Zzyzx Shard");
        }
        finally { CrashLog.DirOverride = keepDir; try { Directory.Delete(dir, true); } catch (Exception) { } }
        return ok;
    }
}
