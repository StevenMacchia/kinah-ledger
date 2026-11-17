using System.Text.Json;
using KinahLedger.Data;

namespace KinahLedger.Session;

/// <summary>
/// Today (0.8.50), part of parse-test: the all-zones and Today counts are fed every line the zone view gets and keep it
/// across a zone change; a day's count carries across a restart (today.json) and counts on from there; a file from another
/// day is left alone; the strip's slots split drops from kills and gathering, pin one zone, and say where each came from;
/// runs belong to today by their last loot. Writes only to a scratch folder.
/// </summary>
public static class TodayTests
{
    public static bool Run(ItemDictionary items, Settings settings)
    {
        bool ok = true;
        void Check(string what, string got, string expect)
        {
            bool pass = got == expect; ok &= pass;
            Console.WriteLine($"  {what,-62} -> {got,-40} {(pass ? "ok" : "MISMATCH, expected " + expect)}");
        }
        Console.WriteLine("\nToday: all zones and the day kept across zone changes and restarts (0.8.50):");
        var keepDir = CrashLog.DirOverride;
        var dir = TestTemp.Path("kl-today-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        CrashLog.DirOverride = dir;
        try
        {
            // a. the live session feeds both counts; a zone change starts the zone view over and leaves them alone
            var s = new TrackerSession(null, settings, items, "Test");
            var all = new SessionEngine(items); var day = new SessionEngine(items);
            s.AllView = all; s.DayView = day;
            var t0 = DateTime.UtcNow;
            // each pickup as the reader counts it (TrackerSession.Count, where every count is fed), so the fan-out is what is tested
            s.ApplyZone("Western Cantas Valley", t0);
            s.Count(new KinahChanged(t0.AddSeconds(1), 431, false));
            s.Count(new KinahChanged(t0.AddSeconds(2), 388, false));
            long zoneA = s.ZoneView.Totals().Kinah;
            Check("zone A: the zone view counted the Kinah", zoneA.ToString(), "819");
            Check("zone A: all zones and today counted the same", $"{all.Totals().Kinah == zoneA} {day.Totals().Kinah == zoneA}", "True True");
            // a run started holds the all-zones count (Steven, 2026-11-15: "switching to a run should stop the tracker's kinah per
            // hour in a zone"): the run's Kinah and time are the run's; the count goes on when the run stops, today counts all
            {
                var s2 = new TrackerSession(null, settings, items, "Test");
                var all2 = new SessionEngine(items); var day2 = new SessionEngine(items);
                s2.AllView = all2; s2.DayView = day2;
                var r0 = DateTime.UtcNow;
                s2.ApplyZone("Western Cantas Valley", r0);
                s2.Count(new KinahChanged(r0.AddSeconds(1), 500, false));
                var run = new SessionEngine(items);
                s2.Secondary = run;
                s2.Count(new KinahChanged(r0.AddSeconds(2), 900, false));
                string during = $"{all2.Totals().Kinah} {run.Totals().Kinah} {all2.Paused} {day2.Totals().Kinah}";
                var fresh = new SessionEngine(items);
                s2.AllView = fresh;   // Start run's fresh all-zones count, set while the run is under way: held from the start
                bool freshHeld = fresh.Paused;
                s2.AllView = all2;
                s2.Secondary = null;
                s2.Count(new KinahChanged(r0.AddSeconds(3), 700, false));
                Check("a run under way holds the all-zones count: before / run / held / today, then the fresh one held, then after",
                    $"{during} {freshHeld} {all2.Totals().Kinah} {all2.Paused} {day2.Totals().Kinah}", "500 900 True 1400 True 1200 False 2100");
            }
            var t1 = t0.AddSeconds(30);
            // 0.10.70 (Steven, 2026-11-13: "dont restart the area every time u go somewhere else"): a new zone is a move only
            // once it has been read for a minute (or behind a loading screen); until then the zone view goes on
            s.ApplyZone("Eastern Cantas Valley", t1);
            Check("zone B read: the zone view goes on until the move holds", s.ZoneView.Totals().Kinah.ToString(), "819");
            Check("zone B: all zones and today keep zone A", $"{all.Totals().Kinah == zoneA} {day.Totals().Kinah == zoneA}", "True True");
            s.Count(new KinahChanged(t1.AddSeconds(1), 977, false));
            s.ApplyZone("Eastern Cantas Valley", t1.AddSeconds(TrackerSession.SteadySeconds + 3));
            long zoneB = s.ZoneView.Totals().Kinah;
            Check("zone B for a minute: the zone view starts over from its first read; B counted on top of A in both", $"{zoneB} {all.Totals().Kinah} {day.Totals().Kinah} {s.Place}", "977 1796 1796 Eastern Cantas Valley");
            Check("today knows both zones", string.Join(",", day.ZoneSnapshot().Where(kv => kv.Value.Kinah > 0).Select(kv => kv.Key).OrderBy(x => x)), "Eastern Cantas Valley,Western Cantas Valley");
            // a walk into a neighbour and back, or a read gone wrong: nothing starts over
            var t2 = t1.AddMinutes(3);
            s.ApplyZone("Eastern Cantas Campsite", t2); s.Count(new KinahChanged(t2.AddSeconds(5), 100, false));
            s.ApplyZone("Eastern Cantas Valley", t2.AddSeconds(25));
            Check("...20 s in a neighbouring zone and back: the zone view goes on, its pickup in it", $"{s.ZoneView.Totals().Kinah} {s.Place}", "1077 Eastern Cantas Valley");
            // behind a loading screen (a teleport), at once
            var t3 = t2.AddMinutes(2);
            s.LoadingSeen(t3); s.ApplyZone("Western Cantas Valley", t3.AddSeconds(4));
            Check("...after a loading screen the move is at once", $"{s.ZoneView.Totals().Kinah} {s.Place}", "0 Western Cantas Valley");
            // an expedition's areas are one place: its own zone view, never started over between its rooms
            s.ApplyZone("Path of Atonement", t3.AddMinutes(1)); var inRun = s.ZoneView;
            s.ApplyZone("Fire Corridor", t3.AddMinutes(3)); s.ApplyZone("Mirror Altar", t3.AddMinutes(3.5));
            Check("...an expedition's areas are one place, its zone view kept between its rooms", $"{ReferenceEquals(inRun, s.ZoneView)} {s.Place}", "True Fire Temple");

            // b. a day's count through today.json and back: the same figures, and it counts on
            var e = new SessionEngine(items); var t = DateTime.UtcNow;
            long stone = items.IdOf("Qzx Todaytest Stone"), hide = items.IdOf("Qzx Todaytest Hide"), ore = items.IdOf("Orichalcum Ore");
            void Kill(int kinah) { e.Apply(new KinahChanged(t, kinah, false)); e.Apply(new CurrencyGained(t, "Oculazen Soul Points", 1)); }
            e.SetZone("Western Cantas Valley");
            for (int i = 0; i < 6; i++) Kill(1000);
            for (int i = 0; i < 3; i++) e.Apply(new ItemAcquired(t, stone, 1, false));
            e.Apply(new ItemAcquired(t, hide, 1, true));
            e.SetZone("Altamia Canyon");
            for (int i = 0; i < 4; i++) Kill(500);
            for (int i = 0; i < 2; i++) e.Apply(new ItemAcquired(t, stone, 1, false));
            for (int i = 0; i < 5; i++) e.Apply(new ItemAcquired(t, ore, 1, false));
            var json = JsonSerializer.Serialize(e.ToCarry(DayStore.Today), SessionEngine.CarryJson);
            var back = JsonSerializer.Deserialize<SessionEngine.Carry>(json, SessionEngine.CarryJson)!;
            var f = new SessionEngine(items); f.Seed(back);
            string Sum(SessionEngine x) { var tt = x.Totals(); return $"{tt.Kinah} {tt.Kills} {x.Items.GetValueOrDefault(stone)} {x.ItemsBound.GetValueOrDefault(hide)} {x.Items.GetValueOrDefault(ore)}"; }
            Check("seeded from today.json: the same totals", Sum(f), Sum(e));
            var fz = f.ZoneSnapshot();
            Check("...and the same zones", $"{fz.GetValueOrDefault("Western Cantas Valley")?.Items.GetValueOrDefault(stone)} {fz.GetValueOrDefault("Altamia Canyon")?.Items.GetValueOrDefault(stone)} {fz.GetValueOrDefault("Altamia Canyon")?.Kinah}", "3 2 2000");
            Check("...and the same enemies' Kinah", f.MobSnapshot().Sum(m => m.Kinah).ToString(), e.MobSnapshot().Sum(m => m.Kinah).ToString());
            f.SetZone("Altamia Canyon");
            f.Apply(new ItemAcquired(t, stone, 1, false)); f.Apply(new KinahChanged(t, 700, false)); f.Apply(new CurrencyGained(t, "Oculazen Soul Points", 1));
            Check("it counts on after the restart", $"{f.Items.GetValueOrDefault(stone)} {f.Totals().Kinah} {f.Totals().Kills}", $"6 {e.Totals().Kinah + 700} {e.Totals().Kills + 1}");
            Check("the carried time stays in the clocks", $"{f.Elapsed >= TimeSpan.FromSeconds(back.Elapsed)} {f.Active.TotalSeconds >= back.Active}", "True True");

            var path = Path.Combine(dir, "today.json");
            DayStore.Save(e, DayStore.Today, path);
            Check("today.json loads today", DayStore.Load(items, path).Totals().Kinah.ToString(), e.Totals().Kinah.ToString());
            var old = e.ToCarry("2020-01-01");
            File.WriteAllText(path, JsonSerializer.Serialize(old, SessionEngine.CarryJson));
            Check("a file from another day starts a fresh day", DayStore.Load(items, path).Totals().Kinah.ToString(), "0");
            File.WriteAllText(path, "{ not json");
            Check("a broken file starts a fresh day", DayStore.Load(items, path).Totals().Kinah.ToString(), "0");

            // c. the strip's slots: drops from kills and gathering apart, a pinned zone, where each came from
            var all3 = TodayLoot.Farm(e, items);
            Check("drops from kills and gathering apart", $"{string.Join(",", all3.Gathered.Select(d => items.NameOf(d.Id)))} | {string.Join(",", all3.Drops.Select(d => items.NameOf(d.Id)).OrderBy(x => x))}", "Orichalcum Ore | Qzx Todaytest Hide,Qzx Todaytest Stone");
            var st = all3.Drops.First(d => d.Id == stone);
            Check("a drop says where it came from, most first", string.Join(",", st.Where.Select(w => $"{w.Where} {w.Count}")), "Western Cantas Valley 3,Altamia Canyon 2");
            Check("a bound copy is counted, and worth nothing", $"{all3.Drops.First(d => d.Id == hide).Count} {all3.Drops.First(d => d.Id == hide).Bound} {all3.Drops.First(d => d.Id == hide).Worth}", "1 1 0");
            Check("the day's zones", string.Join(",", all3.Zones.Select(z => z.Zone).OrderBy(x => x)), "Altamia Canyon,Western Cantas Valley");
            var pin = TodayLoot.Farm(e, items, "Altamia Canyon");
            Check("a pinned zone: its loot, its Kinah and its kills only", $"{pin.Drops.First(d => d.Id == stone).Count} {pin.Drops.Any(d => d.Id == hide)} {pin.Kinah} {pin.Kills}", "2 False 2000 4");
            Check("the whole day: every zone", $"{all3.Kinah} {all3.Kills}", "8000 10");

            // d. runs belong to today by their last loot (a run that ran past midnight is today's)
            var midnight = DateTime.Today;
            var today1 = new InstanceRun { Zone = "Urugugu Canyon", Kind = "Expedition", Start = midnight.AddHours(14).ToUniversalTime(), LastLoot = midnight.AddHours(14.1).ToUniversalTime(), Kinah = 600_000 };
            today1.Items[stone] = 7; today1.BoundItems[hide] = 1;
            var late = new InstanceRun { Zone = "Shugo Festival", Kind = "Event", Start = midnight.AddMinutes(-5).ToUniversalTime(), LastLoot = midnight.AddMinutes(3).ToUniversalTime() };
            late.Items[stone] = 2;
            var yday = new InstanceRun { Zone = "Urugugu Canyon", Kind = "Expedition", Start = midnight.AddHours(-5).ToUniversalTime(), LastLoot = midnight.AddHours(-4.9).ToUniversalTime(), Kinah = 500_000 };
            Check("which runs are today's", string.Join(",", new[] { today1, late, yday }.Select(r => TodayLoot.IsToday(r, midnight))), "True,True,False");
            var rt = TodayLoot.Runs(new[] { today1, late }, items, _ => false);
            Check("today's runs: count, places, Kinah", $"{rt.Runs} {rt.Places} {rt.Kinah}", "2 2 600000");
            var rs = rt.Items.First(x => x.Id == stone);
            Check("a run item: summed, with its places", $"{rs.Count} " + string.Join(",", rs.Where.Select(w => $"{w.Where} {w.Count}")), "9 Urugugu Canyon 7,Shugo Festival 2");
            // all gear binds on equip (Steven, 2026-11-17): a run's gear keeps its row of its own with each piece's count
            // (2026-11-10: "i want to see how many of X type of gear i got"), and a piece with a tradable id is worth its Market
            // price, in the run's value; the bound copy of a pair (the id the item list says isn't tradable, as a reward table
            // picks it) and a piece the game called bound are counted and add nothing
            const long glovesT = 210540028, glovesB = 210540044, earsT = 310240028;   // Corrupted Judicator Gloves, tradable and not; Murute Earrings
            if (items.IsGear(glovesT) && items.IsGear(glovesB) && items.IsGear(earsT))
            {
                Check("gear binds on equip: bound by its id only (the pair's bound copy), a dropped piece's id is tradable",
                    $"{items.BoundId(glovesT)} {items.BoundId(glovesB)} {items.BoundId(earsT)} {items.BoundId(stone)}", "False True False False");
                PriceBook.SetBoard(new() { ["Corrupted Judicator Gloves"] = 100_000, ["Murute Earrings"] = 40_000 });
                try
                {
                    var fire = new InstanceRun { Zone = "Fire Temple", Kind = "Expedition", Start = midnight.AddHours(16).ToUniversalTime(), LastLoot = midnight.AddHours(16.1).ToUniversalTime(), Kinah = 800_000, UnknownGear = 1 };
                    fire.Items[glovesT] = 2; fire.Items[glovesB] = 1; fire.BoundItems[earsT] = 1; fire.Items[stone] = 3;
                    var fr = TodayLoot.Runs(new[] { fire }, items, _ => false);
                    long two = (long)Math.Round(100_000 * 2 * PriceBook.SellerKeeps);
                    Check("a run's gear: a row of its own, each piece's count, the tradable pieces at their price",
                        $"{string.Join(",", fr.Items.Select(x => items.NameOf(x.Id)))} | gear {fr.Gear}: {string.Join(", ", fr.GearSlots.Select(s => $"{s.Id} x{s.Count} bound {s.Bound} worth {(s.Worth == two ? "2 at the price" : s.Worth.ToString())}").OrderBy(x => x))}",
                        $"Qzx Todaytest Stone | gear 5: {glovesT} x2 bound 0 worth 2 at the price, {glovesB} x1 bound 1 worth 0, {earsT} x1 bound 1 worth 0");
                    Check("...the gear's worth is in the run's value", $"{fr.GearWorth == two} {fr.Value == fr.Kinah + fr.Items.Sum(x => x.Worth) + two} {fr.GearBound}", "True True 2");
                    // the site gets the bound copy among items_bound, so it never values it either
                    var sent = Upload.InstanceRunPayload.From(new[] { fire }, id => items.KeyOf(id), null, items.BoundId)![0];
                    Check("...and the site: every piece in items, the bound ones (by id or by the game's word) in items_bound",
                        $"{sent.items.GetValueOrDefault(glovesT.ToString())} {sent.items.GetValueOrDefault(glovesB.ToString())} | bound {string.Join(",", (sent.items_bound ?? new()).OrderBy(kv => kv.Key).Select(kv => $"{kv.Key} {kv.Value}"))}",
                        $"2 1 | bound {glovesB} 1,{earsT} 1");
                    // the farming side: a piece of gear dropped in the open world sells too; a bound copy counted as a drop doesn't
                    var fe = new SessionEngine(items);
                    fe.Apply(new ItemAcquired(midnight.AddHours(17).ToUniversalTime(), glovesT, 1, false));
                    fe.Apply(new ItemAcquired(midnight.AddHours(17).ToUniversalTime(), glovesB, 1, false));
                    Check("open-world gear: the tradable piece sells, the bound copy is bound however it was counted",
                        string.Join(", ", fe.ItemRows().OrderBy(r => r.Id).Select(r => $"{r.Id} {r.Count}+{r.Bound}b")), $"{glovesT} 1+0b, {glovesB} 0+1b");
                }
                finally { PriceBook.SetBoard(new()); }
            }
            else Check("a run's gear: the test's gear ids are in the item list", "missing", "found");

            // e. each page's strip is its own day (0.8.51): Farming says only farming, Events only the runs
            string Line(List<(string Fig, string Word)> l) => string.Join(" ·", l.Select(n => n.Fig + n.Word));
            var fl = TodayLoot.FarmLine(all3);
            Check("the Farming line: farming time, then the kills", string.Join(",", fl.Select(n => n.Word.Trim())) + " " + fl[^1].Fig, "farming,kills 10");
            Check("...and never a run", fl.Any(n => n.Word.Contains("run") || n.Word.Contains("expedition") || n.Word.Contains("event")).ToString(), "False");
            Check("the Events line: expeditions, then events", Line(TodayLoot.RunsLine(rt)), "1 expedition ·1 event");
            var odd = new InstanceRun { Zone = "Night Dew Cave", Kind = "", Start = midnight.AddHours(15).ToUniversalTime(), LastLoot = midnight.AddHours(15.2).ToUniversalTime(), Kinah = 1, Suspect = "looks like several runs" };
            var exp2 = new InstanceRun { Zone = "Urugugu Canyon", Kind = "expedition", Start = midnight.AddHours(16).ToUniversalTime(), LastLoot = midnight.AddHours(16.1).ToUniversalTime() };
            var rt3 = TodayLoot.Runs(new[] { today1, late, odd, exp2 }, items, _ => false);
            Check("...a run of neither kind as a run, kinds in any case", Line(TodayLoot.RunsLine(rt3)), "2 expeditions ·1 event ·1 run");
            Check("...and never farming", TodayLoot.RunsLine(rt3).Any(n => n.Word.Contains("farm") || n.Word.Contains("kill") || n.Word.Contains("gather")).ToString(), "False");
            Check("a run left out of the averages adds nothing", $"{rt3.Counted} {rt3.Kinah}", "3 600000");
            var gatherOnly = new TodayFarm(0, 0, 12, 1800, new(), new(), new(), new());
            Check("a day of gathering only says gathering", Line(TodayLoot.FarmLine(gatherOnly)), "30 min gathering ·12 attempts");
            // f. one way to write how long (2026-10-28): minutes, then hours and minutes; never "0:21" beside "5:19"
            Check("how long, in words", string.Join(",", new[] { 20.0, 59, 60, 21 * 60 + 40, 3600, 2 * 3600 + 41 * 60 + 59 }.Select(TodayLoot.Dur)),
                "under a minute,under a minute,1 min,21 min,1 h,2 h 41 min");
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch (Exception) { /* a file still open */ }
            CrashLog.DirOverride = keepDir;
        }
        return ok;
    }
}
