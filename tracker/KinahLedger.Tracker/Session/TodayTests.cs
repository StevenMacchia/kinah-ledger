using System.Text.Json;
using KinahLedger.Data;

namespace KinahLedger.Session;

/// <summary>
/// Today (0.8.49), part of parse-test: the all-zones and Today counts are fed every line the zone view gets and keep it
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
        Console.WriteLine("\nToday: all zones and the day kept across zone changes and restarts (0.8.49):");
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
            var t1 = t0.AddSeconds(30);
            s.ApplyZone("Eastern Cantas Valley", t1);
            Check("zone B: the zone view starts over", s.ZoneView.Totals().Kinah.ToString(), "0");
            Check("zone B: all zones and today keep zone A", $"{all.Totals().Kinah == zoneA} {day.Totals().Kinah == zoneA}", "True True");
            s.Count(new KinahChanged(t1.AddSeconds(1), 977, false));
            long zoneB = s.ZoneView.Totals().Kinah;
            Check("zone B counted on top of A in both", $"{zoneB} {all.Totals().Kinah} {day.Totals().Kinah}", "977 1796 1796");
            Check("today knows both zones", string.Join(",", day.ZoneSnapshot().Where(kv => kv.Value.Kinah > 0).Select(kv => kv.Key).OrderBy(x => x)), "Eastern Cantas Valley,Western Cantas Valley");

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
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch (Exception) { /* a file still open */ }
            CrashLog.DirOverride = keepDir;
        }
        return ok;
    }
}
