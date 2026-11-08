using KinahLedger.Data;

namespace KinahLedger.Session;

/// <summary>Save as CSV (0.10.11), part of parse-test: cells are quoted as a spreadsheet expects, and the loot and runs files say what the window says.</summary>
public static class CsvExportTests
{
    public static bool Run(ItemDictionary items, Settings settings)
    {
        bool ok = true;
        void Check(string what, string got, string expect)
        {
            bool pass = got == expect; ok &= pass;
            Console.WriteLine($"  {what,-62} -> {got,-60} {(pass ? "ok" : "MISMATCH, expected " + expect)}");
        }
        Console.WriteLine("\nSave as CSV (0.10.11): quoting, today's loot, the runs:");

        Check("a plain cell is bare", CsvExport.Cell("Thick hide"), "Thick hide");
        Check("a comma quotes the cell", CsvExport.Cell("Siel, Elyos"), "\"Siel, Elyos\"");
        Check("a quote is doubled inside quotes", CsvExport.Cell("the \"big\" one"), "\"the \"\"big\"\" one\"");
        Check("the file name carries the day", CsvExport.FileName("loot", new DateTime(2026, 11, 6, 14, 2, 0)), "kinah-ledger-loot-2026-11-06.csv");

        var farm = new TodayFarm(632_300, 161, 7, 2520,
            Drops: new()
            {
                new TodaySlot(1, "Thick hide", 41, 0, 39_688, true, false, false, new() { ("Western Cantas Valley", 30), ("Night Dew Cave", 11) }),
                new TodaySlot(2, "Aether shard", 7, 0, 0, false, false, false, new() { ("Western Cantas Valley", 7) }),
            },
            Gathered: new() { new TodaySlot(3, "Orichalcum Ore, fine", 4, 1, 3_432, true, false, false, new() { ("Western Cantas Valley", 4) }) },
            Zones: new(), KinahWhere: new() { ("Western Cantas Valley", 500_000), ("Night Dew Cave", 132_300) });
        var loot = CsvExport.Loot(farm).Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.TrimEnd('\r')).ToList();
        Check("loot: the header", loot[0], CsvExport.LootHeader);
        Check("loot: the Kinah row first, with where it came from", loot[1], "kinah,Kinah,632300,0,632300,yes,Western Cantas Valley 500000; Night Dew Cave 132300");
        Check("loot: a priced drop", loot[2], "drop,Thick hide,41,0,39688,yes,Western Cantas Valley 30; Night Dew Cave 11");
        Check("loot: one with no price yet", loot[3], "drop,Aether shard,7,0,0,no,Western Cantas Valley 7");
        Check("loot: a gathered item with a comma in its name is quoted", loot[4], "gathered,\"Orichalcum Ore, fine\",4,1,3432,yes,Western Cantas Valley 4");
        Check("loot: five lines in all", loot.Count.ToString(), "5");

        // the runs: names from the item list when it has them, "item <id>" otherwise, so the check uses ids the list won't know
        var t0 = new DateTime(2026, 11, 6, 19, 45, 0, DateTimeKind.Local).ToUniversalTime();
        var paid = new InstanceRun { Zone = "Urugugu Canyon", Kind = "Expedition", Start = t0, LastLoot = t0.AddMinutes(9).AddSeconds(2), Kinah = 702_000, Kills = 40, Items = new() { [900_001] = 6, [900_002] = 1 }, BoundItems = new() { [900_003] = 2 } };
        var unread = new InstanceRun { Zone = "Shugo Festival", Kind = "Event", Event = "Hidden Lugi", Family = "Shugo Festival", Start = t0.AddHours(1), LastLoot = t0.AddHours(1).AddMinutes(5), Kills = 3, FromPicture = true, KinahNotRead = true, SlotsUnread = 2 };
        var runs = CsvExport.Runs(new[] { paid, unread }, items, r => r.Kinah + 1_000).Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.TrimEnd('\r')).ToList();
        Check("runs: the header", runs[0], CsvExport.RunsHeader);
        Check("runs: a paid run, items by count then name", runs[1], "2026-11-06 19:45:00,Expedition,Urugugu Canyon,,,542,702000,yes,40,703000,no,item 900001 ×6; item 900002 ×1,item 900003 ×2,");
        Check("runs: a run whose Kinah wasn't read: blank Kinah, left out, a note", runs[2], "2026-11-06 20:45:00,Event,Shugo Festival,Hidden Lugi,Shugo Festival,300,,no,3,1000,yes,,,Kinah not read; 2 slots not read");

        // crafts (0.10.26): a priced recipe, one whose cost is only a floor, one with an unpriced output
        var powder = new CraftLedger.Recipe { Name = "Sapphire Powder", Ok = 2, Fail = 0, Made = { ["Sapphire Powder"] = 6 }, Tries = { [3] = 2 } };
        var greaves = new CraftLedger.Recipe { Name = "Orichalcum Greaves", Ok = 1, Fail = 1, Upgraded = 0, NotUpgraded = 1, Made = { ["Orichalcum Greaves"] = 1 }, Tries = { [1] = 2 } };
        var ring = new CraftLedger.Recipe { Name = "Diamond Ring", Ok = 1, Fail = 0, Unseen = 1, Made = { ["Diamond Ring"] = 1 } };
        var prices = new Dictionary<string, long> { ["Sapphire Powder"] = 10_000, ["Orichalcum Greaves"] = 200_000 };
        var crafts = CsvExport.Crafts(new[] { powder, greaves, ring },
            r => r.Name switch { "Sapphire Powder" => (3_580L, true), "Orichalcum Greaves" => (78_130L, false), _ => ((long?)null, false) },
            name => prices.TryGetValue(name, out var p) ? p : null, keeps: 0.88).Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.TrimEnd('\r')).ToList();
        Check("crafts: the header", crafts[0], CsvExport.CraftsHeader);
        Check("crafts: a priced recipe, six made, the profit", crafts[1], "Sapphire Powder,2,2,0,6,Sapphire Powder ×6,3580,yes,52800,49220,0,0,0");
        Check("crafts: a cost that is only a floor says at least; a lost roll", crafts[2], "Orichalcum Greaves,2,1,1,1,Orichalcum Greaves ×1,78130,at least,176000,97870,0,1,0");
        Check("crafts: no recipe book, no price: blanks, and a craft not seen", crafts[3], "Diamond Ring,1,1,0,1,Diamond Ring ×1,,no,,,0,0,1");

        // the week (0.10.26): a day before the history, a farming day with zones, a day of runs
        var mon = new DateTime(2026, 11, 2);
        var days = new List<HistoryDay>
        {
            new(mon, "2026-11-02", false, true, new FarmDay(), RunsDay.None),
            new(mon.AddDays(1), "2026-11-03", false, false, new FarmDay { Day = "2026-11-03", Made = 1_420_000, Kinah = 811_400, Active = 6300.4, Kills = 420, Zones = { new FarmDayZone { Zone = "Kraka Footpath", Made = 560_000 }, new FarmDayZone { Zone = "Pilgrim's Ridge", Made = 330_000 } } }, RunsDay.None),
            new(mon.AddDays(2), "2026-11-04", true, false, new FarmDay { Day = "2026-11-04" }, new RunsDay(5, 4, 2_750_000, new() { new RunKind("Expedition", 5) }, new() { new RunName("Urugugu Canyon", 5, 2_750_000) })),
        };
        var week = CsvExport.Week(days).Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.TrimEnd('\r')).ToList();
        Check("week: the header", week[0], CsvExport.WeekHeader);
        Check("week: a day before the history is its date only", week[1], "2026-11-02,,,,,,,,,,");
        Check("week: a farming day with its zones", week[2], "2026-11-03,1420000,811400,6300,420,0,Kraka Footpath 560000; Pilgrim's Ridge 330000,0,0,0,");
        Check("week: a day of runs", week[3], "2026-11-04,0,0,0,0,0,,5,4,2750000,Urugugu Canyon 5");
        return ok;
    }
}
