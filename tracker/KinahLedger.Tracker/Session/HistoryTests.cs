using System.Text.Json;
using KinahLedger.Data;

namespace KinahLedger.Session;

/// <summary>
/// History (0.9), part of parse-test: days.json keeps a day's farming as LOOT's Today side figures it and reads back the
/// same; the midnight rollover finalizes the old day and starts a fresh one; only the last 60 days are kept; a day the
/// tracker didn't run is nothing and a day before history began is "before"; the first run seeds the file from today.json;
/// runs fall on the local day they started, across a UTC midnight; and today's bar is the Today side's total. Writes only
/// to a scratch folder.
/// </summary>
public static class HistoryTests
{
    public static bool Run(ItemDictionary items, Settings settings)
    {
        bool ok = true;
        void Check(string what, string got, string expect)
        {
            bool pass = got == expect; ok &= pass;
            Console.WriteLine($"  {what,-62} -> {got,-40} {(pass ? "ok" : "MISMATCH, expected " + expect)}");
        }
        Console.WriteLine("\nHistory: days.json, the Week side's days and the runs by day (0.9):");
        var keepDir = CrashLog.DirOverride;
        var dir = TestTemp.Path("kl-history-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        CrashLog.DirOverride = dir;
        PriceBook.SetBoard(new() { ["Spiritstone"] = 7_000, ["Orichalcum Ore"] = 2_000 });
        try
        {
            var today = DateTime.Today;
            string Key(int ago) => DayStore.DayOf(today.AddDays(-ago));
            long spirit = items.IdOf("Spiritstone"), ore = items.IdOf("Orichalcum Ore");
            SessionEngine Day(int kinahKills, int spirits)
            {
                var e = new SessionEngine(items); var t = DateTime.UtcNow;
                e.SetZone("Western Cantas Valley");
                for (int i = 0; i < kinahKills; i++) { e.Apply(new KinahChanged(t, 1000, false)); e.Apply(new CurrencyGained(t, "Oculazen Soul Points", 1)); }
                for (int i = 0; i < spirits; i++) e.Apply(new ItemAcquired(t, spirit, 1, false));
                e.SetZone("Altamia Canyon");
                for (int i = 0; i < 2; i++) { e.Apply(new KinahChanged(t, 500, false)); e.Apply(new CurrencyGained(t, "Oculazen Soul Points", 1)); }
                for (int i = 0; i < 4; i++) e.Apply(new ItemAcquired(t, ore, 1, false));
                return e;
            }

            // a. a day's entry through days.json and back: the made, Kinah, kills and zones LOOT's Today side works out
            var todayPath = Path.Combine(dir, "today.json");
            var daysPath = Path.Combine(dir, "days.json");
            var e1 = Day(6, 3);
            var live = TodayLoot.Farm(e1, items);
            DayStore.Save(e1, Key(0), todayPath);
            Check("today.json's save writes days.json beside it", File.Exists(daysPath).ToString(), "True");
            var f1 = DayHistory.Load(daysPath);
            var d1 = f1.Get(Key(0));
            Check("the day's entry: made, Kinah, kills as the Today side", d1 == null ? "none" : $"{d1.Made} {d1.Kinah} {d1.Kills}", $"{live.Made} {live.Kinah} {live.Kills}");
            Check("...made is Kinah plus loot after the Market's cut", (live.Made > live.Kinah).ToString(), "True");
            Check("...and its zones, most made first", d1 == null ? "none" : string.Join(",", d1.Zones.Select(z => $"{z.Zone} {z.Made} {z.Kills}")),
                string.Join(",", live.Zones.Select(z => $"{z.Zone} {z.Made} {z.Kills}")));
            Check("history begins with the first day written", f1.Since, Key(0));
            var json = JsonSerializer.Serialize(f1);
            var back = JsonSerializer.Deserialize<DayFile>(json)!;
            Check("days.json round trip", JsonSerializer.Serialize(back), json);
            e1.Apply(new KinahChanged(DateTime.UtcNow, 700, false));
            DayStore.Save(e1, Key(0), todayPath);
            var f1b = DayHistory.Load(daysPath);
            Check("a later save replaces the day's entry, never adds one", $"{f1b.Days.Count} {f1b.Get(Key(0))?.Kinah}", $"1 {live.Kinah + 700}");
            File.WriteAllText(daysPath, "{ not json");
            Check("a broken days.json reads as no history", DayHistory.Load(daysPath).Days.Count.ToString(), "0");
            File.Delete(daysPath);

            // b. the midnight rollover: yesterday's count saved for good, a fresh today
            var yday = Day(10, 5);
            var ydayMade = TodayLoot.Farm(yday, items).Made;
            Check("no rollover while the day is the same", (DayStore.Roll(yday, Key(1), Key(1), todayPath) == null).ToString(), "True");
            var fresh = DayStore.Roll(yday, Key(1), Key(0), todayPath);
            Check("at midnight a fresh count starts", fresh == null ? "none" : $"{fresh.Totals().Kinah} {fresh.Totals().Kills}", "0 0");
            var afterRoll = DayHistory.Load(daysPath);
            Check("...and yesterday's last figure is kept", afterRoll.Get(Key(1))?.Made.ToString() ?? "none", ydayMade.ToString());
            fresh!.SetZone("Eastern Cantas Valley");
            fresh.Apply(new KinahChanged(DateTime.UtcNow, 900, false)); fresh.Apply(new CurrencyGained(DateTime.UtcNow, "Oculazen Soul Points", 1));
            DayStore.Save(fresh, Key(0), todayPath);
            var rolled = DayHistory.Days(7, fresh, items, null, DayHistory.Load(daysPath));
            Check("the week: yesterday as it ended, today the fresh count", $"{rolled[5].Farm.Made} {rolled[6].Farm.Made} {rolled[6].IsToday}", $"{ydayMade} {TodayLoot.Farm(fresh, items).Made} True");
            Check("...seven days, oldest first, ending today", $"{rolled.Count} {rolled[0].Key} {rolled[6].Key}", $"7 {Key(6)} {Key(0)}");

            // c. only the last 60 days are kept
            var many = new DayFile { Since = Key(80) };
            for (int i = 79; i >= 0; i--) many.Days.Add(new FarmDay { Day = Key(i), Made = 1000 + i, Active = 600 });
            var prunePath = Path.Combine(dir, "days-prune.json");
            DayHistory.Save(many, prunePath, today);
            var pruned = DayHistory.Load(prunePath);
            Check("the 60-day prune: how many, the oldest and newest kept", $"{pruned.Days.Count} {pruned.Days[0].Day} {pruned.Days[^1].Day}", $"{DayHistory.KeepDays} {Key(DayHistory.KeepDays - 1)} {Key(0)}");
            Check("...history's start stays where it was", pruned.Since, Key(80));

            // d. a day the tracker didn't run is nothing; a day before history began is "before"
            var gaps = new DayFile { Since = Key(4) };
            foreach (var ago in new[] { 3, 1 }) gaps.Days.Add(new FarmDay { Day = Key(ago), Made = 500_000, Kinah = 400_000, Active = 3600, Kills = 200 });
            var gw = DayHistory.Days(7, null, items, null, gaps);
            Check("before history: the days before it began", string.Join("", gw.Select(d => d.BeforeHistory ? "B" : ".")), "BB.....");
            Check("a missing day in the week is nothing", $"{gw[4].Farm.Made} {gw[4].Farm.Any} {gw[2].Farm.Made} {gw[2].BeforeHistory}", "0 False 0 False");
            Check("...the kept days read as kept", $"{gw[3].Farm.Made} {gw[5].Farm.Kills}", "500000 200");
            Check("the week's totals leave the days before history out", $"{DayHistory.Totals(gw.Where(d => !d.BeforeHistory)).Made} {DayHistory.Totals(gw).Active}", "1000000 7200");
            var none = DayHistory.Days(7, null, items, null, new DayFile());
            Check("no history yet: it begins today", string.Join("", none.Select(d => d.BeforeHistory ? "B" : ".")), "BBBBBB.");

            // e. the first run seeds days.json from today.json: history starts today, or with the day today.json holds
            var seedDir = Path.Combine(dir, "seed"); Directory.CreateDirectory(seedDir);
            var seedToday = Path.Combine(seedDir, "today.json");
            DayHistory.Seed(items, seedToday);
            var s0 = DayHistory.Load(Path.Combine(seedDir, "days.json"));
            Check("first run, no today.json: history starts today", $"{s0.Since} {s0.Days.Count}", $"{Key(0)} 0");
            File.Delete(Path.Combine(seedDir, "days.json"));
            var carried = Day(4, 2);
            File.WriteAllText(seedToday, JsonSerializer.Serialize(carried.ToCarry(Key(1)), SessionEngine.CarryJson));
            DayHistory.Seed(items, seedToday);
            var s1 = DayHistory.Load(Path.Combine(seedDir, "days.json"));
            Check("first run after an update: today.json's day kept, history from it", $"{s1.Since} {s1.Get(Key(1))?.Made}", $"{Key(1)} {TodayLoot.Farm(carried, items).Made}");
            DayHistory.Seed(items, seedToday);
            Check("...seeding again adds nothing", DayHistory.Load(Path.Combine(seedDir, "days.json")).Days.Count.ToString(), "1");

            // f. runs fall on the local day they started, also across a UTC midnight (a player at UTC-5: 18:00 and 21:00 the 28th, 01:00 the 29th)
            var tz = TimeZoneInfo.CreateCustomTimeZone("KL-test-5", TimeSpan.FromHours(-5), "KL test UTC-5", "KL test UTC-5");
            var d28 = new DateTime(2026, 10, 28, 0, 0, 0, DateTimeKind.Utc);
            InstanceRun R(DateTime startUtc, string zone, string kind, long kinah, string ev = "", string suspect = "") =>
                new() { Zone = zone, Kind = kind, Event = ev, Start = startUtc, LastLoot = startUtc.AddMinutes(8), Kinah = kinah, Suspect = suspect };
            var runs = new List<InstanceRun>
            {
                R(d28.AddHours(23), "Urugugu Canyon", "Expedition", 600_000),                       // 18:00 local, the 28th
                R(d28.AddHours(26), "Urugugu Canyon", "expedition", 580_000),                       // past UTC midnight: 21:00 local, still the 28th
                R(d28.AddHours(27), "Shugo Festival", "Event", 0, ev: "Hidden Lugi"),              // 22:00 local, the 28th
                R(d28.AddHours(28), "Kashapa", InstanceLog.WorldBoss, 150_000),                     // 23:00 local, the 28th
                R(d28.AddHours(30), "Urugugu Canyon", "Expedition", 3_000_000, suspect: "looks like several runs"),   // 01:00 local, the 29th
            };
            var byDay = DayHistory.RunsByDay(runs, tz);
            Check("runs by the local day they started, across a UTC midnight", string.Join(",", byDay.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key} {kv.Value.Runs}")), "2026-10-28 4,2026-10-29 1");
            var r28 = byDay["2026-10-28"];
            Check("a day's runs: counted, Kinah, kinds in words", $"{r28.Counted} {r28.Kinah} | {DayHistory.KindsLine(r28)}", "4 1330000 | 2 expeditions · 1 event · 1 world boss");
            Check("...by name: an event's own name, else the place, most first", string.Join(",", r28.Names.Select(n => $"{n.Name} ×{n.Count}")), "Urugugu Canyon ×2,Kashapa ×1,Hidden Lugi ×1");
            var r29 = byDay["2026-10-29"];
            Check("a run left out of the averages adds no Kinah, still counts as a run", $"{r29.Runs} {r29.Counted} {r29.Kinah}", "1 0 0");
            var tzWeek = DayHistory.Days(7, null, items, runs, new DayFile(), new DateTime(2026, 10, 29, 12, 0, 0), tz);
            Check("the week's runs: the 28th and today (the 29th)", $"{tzWeek[5].Key} {tzWeek[5].Runs.Runs} {tzWeek[6].Key} {tzWeek[6].Runs.Runs} {tzWeek[0].Runs.Runs}", "2026-10-28 4 2026-10-29 1 0");
            Check("the week's runs, counted and Kinah", $"{DayHistory.Totals(tzWeek).Runs} {DayHistory.Totals(tzWeek).Counted} {DayHistory.Totals(tzWeek).RunKinah}", "5 4 1330000");

            // g. today's bar is LOOT's Today side total: the same engine read the same way, kept days or not
            var todayEngine = Day(9, 7);
            var kept = new DayFile { Since = Key(10) };
            kept.Days.Add(new FarmDay { Day = Key(0), Made = 1 });   // a stale entry from a minute ago: today is live
            var tw = DayHistory.Days(7, todayEngine, items, null, kept);
            Check("today's bar equals the Today side's total", tw[6].Farm.Made.ToString(), TodayLoot.Farm(todayEngine, items).Made.ToString());
            Check("...and a kept day reads its saved figure", DayHistory.FromCarry(todayEngine.ToCarry(Key(0)), items).Made.ToString(), TodayLoot.Farm(todayEngine, items).Made.ToString());
        }
        finally
        {
            PriceBook.SetBoard(new());
            try { Directory.Delete(dir, true); } catch (Exception) { /* a file still open */ }
            CrashLog.DirOverride = keepDir;
        }
        return ok;
    }
}
