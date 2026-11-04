using KinahLedger.Data;
using KinahLedger.Session;

namespace KinahLedger;

/// <summary>
/// The player features' math (0.8.12), run by market-test: best crafts as the site sends them and the profit the
/// recipe card shows, vendor costs taken off, where to farm by gear bracket, the gear score's bracket, and price
/// alerts firing once per crossing. The JSON samples are the backend's own output (kinah-ledger-site, crafts.ts and
/// farm.ts), so a change of shape on either side fails here.
/// </summary>
static class PlayerTests
{
    const string CraftsJson = "{\"key\":\"na_e-siel-elyos\",\"scope\":\"server\",\"seller_keeps\":0.88,\"min_obs\":3,\"crafts\":[{\"item\":\"100\",\"name\":\"Test Helm\",\"profession\":\"Armorsmithing\",\"level\":30,\"makes\":1,\"success\":0.8,\"combo\":0.2,\"upgrade\":{\"item\":\"101\",\"price\":400000},\"price\":100000,\"n_obs\":10,\"materials\":[{\"item\":\"200\",\"qty\":2,\"price\":10000,\"vendor\":false},{\"item\":\"300\",\"qty\":1,\"price\":null,\"vendor\":true}],\"cost\":20000,\"vendor\":1,\"value\":140800,\"profit\":92640,\"margin\":4.632}]}";
    const string FarmJson = "{\"key\":\"na_e-siel-elyos\",\"scope\":\"server\",\"publish_rule\":{\"min_runs\":5,\"min_installs\":3},\"brackets\":{" +
        "\"all\":[{\"zone\":\"Night Dew Cave\",\"n_runs\":6,\"n_installs\":3,\"minutes\":360,\"published\":true,\"value_hr\":1310000,\"kina_hr\":880000,\"priced_share\":1},{\"zone\":\"Crimson Isle\",\"n_runs\":9,\"n_installs\":4,\"minutes\":500,\"published\":true,\"value_hr\":900000,\"kina_hr\":700000,\"priced_share\":1}]," +
        "\"1400-1599\":[{\"zone\":\"Night Dew Cave\",\"n_runs\":5,\"n_installs\":3,\"minutes\":300,\"published\":true,\"value_hr\":1500000,\"kina_hr\":900000,\"priced_share\":1},{\"zone\":\"Crimson Isle\",\"n_runs\":2,\"n_installs\":1,\"minutes\":60,\"published\":false,\"value_hr\":null,\"kina_hr\":null,\"priced_share\":null}]}}";

    public static bool Run()
    {
        bool ok = true;
        void Check(string what, string got, string want) { bool pass = got == want; ok &= pass; Console.WriteLine($"  {what,-62} -> {got}  {(pass ? "ok" : "MISMATCH, expected " + want)}"); }

        // best crafts: the site's row, and the same sum on the recipe card
        var crafts = PlayerBoards.ParseCrafts(CraftsJson);
        var c = crafts.Single();
        Check("best craft parsed from the site's JSON", $"{c.Name} #{c.Id} ×{c.Makes} {c.Success:P0} combo {c.Combo:P0} up {c.UpgradeId}@{c.UpgradePrice:N0} cost {c.Cost:N0} profit {c.Profit:N0} mats {c.Materials.Count} vendor {c.Materials.Count(m => m.Vendor)}",
            "Test Helm #100 ×1 80% combo 20% up 101@400,000 cost 20,000 profit 92,640 mats 2 vendor 1");
        Check("the card's profit per try is the site's", $"{CraftMath.ProfitPerTry(0.8, 0.2, 100_000, 400_000, 20_000, 0.88):N0}", "92,640");
        Check("no upgrade price: a combo is worth the normal result", $"{CraftMath.ProfitPerTry(0.95, 0.25, 30_000, null, 18_000, 0.88):N0}", $"{0.95 * 0.88 * 30_000 - 18_000:N0}");
        // the shared craft math: a recipe whose combo makes two, at a price list; the chance counted when the book lacks one
        var ingot = new RecipeBook.Recipe("Test Ingot", "Alchemy", "Test Ingot", 10, new(), null, null, new()) { ComboChance = 0.3 };
        var judged = CraftMath.Judge(ingot, _ => 1_000, 5_000, 0.8, 0.88);
        Check("judged: a combo that makes two, profit and break-even", $"{Math.Round(judged!.Profit):N0} {judged.Doubles} {judged.Combo:P0} {Math.Round(judged.BreakEven ?? 0)}", "4,152 True 30% 546");
        Check("judged: no price for the output, no verdict", $"{CraftMath.Judge(ingot, _ => null, 5_000, 0.8, 0.88) == null}", "True");
        Check("success chance: listed", $"{CraftMath.SuccessUsed(0.73, 5, 2)}", "(0.73, Listed)");
        Check("success chance: listed, 20 crafts at the same rate", $"{CraftMath.SuccessUsed(0.75, 20, 15)}", "(0.75, Listed)");
        Check("success chance: listed, 20 crafts at another rate: yours", $"{CraftMath.SuccessUsed(0.73, 20, 13)}", "(0.65, Yours)");
        Check("success chance: unlisted, 9 crafts: unknown, counted as 1", $"{CraftMath.SuccessUsed(null, 9, 9)}", "(1, Unknown)");
        Check("success chance: unlisted, 10 crafts: measured", $"{CraftMath.SuccessUsed(null, 10, 7)}", "(0.7, Observed)");
        Check("vendor cost not typed yet: profit as the site has it", $"{CraftMath.WithVendor(c, _ => null)}", "(92640, 1)");
        Check("a typed vendor cost comes off the profit", $"{CraftMath.WithVendor(c, id => id == 300 ? 2_640 : null)}", "(90000, 0)");
        // a row the site already priced the vendor material in (the API knows the NPC prices now): not charged twice
        var priced = c with { Materials = c.Materials.Select(m => m.Vendor ? m with { Price = 2_640 } : m).ToList(), Profit = 90_000 };
        Check("a vendor material the site priced is not charged again", $"{CraftMath.WithVendor(priced, _ => 2_640)}", "(90000, 0)");
        Check("a farmed vendor item the site priced comes back on the profit", $"{CraftMath.WithFarmed(priced, id => id == 300).Craft.Profit}", "92640");
        // "I have these": the site took the farmed material's 2 × 10,000 off, so it comes back; a farmed vendor item isn't charged
        var fm = CraftMath.WithFarmed(c, id => id == 200);
        Check("a farmed material's cost goes back on the profit", $"{fm.Craft.Profit} cost {fm.Craft.Cost} farmed {fm.Farmed} mats {fm.Craft.Materials.Count}", "112640 cost 0 farmed 1 mats 1");
        var fv = CraftMath.WithFarmed(c, id => id == 300);
        Check("a farmed vendor item: not charged, not unpriced", $"{CraftMath.WithVendor(fv.Craft, _ => 2_640)} farmed {fv.Farmed}", "(92640, 0) farmed 1");
        Check("nothing farmed: the craft as it was", $"{CraftMath.WithFarmed(c, _ => false).Craft.Profit} {CraftMath.WithFarmed(c, _ => false).Farmed}", "92640 0");

        // one age for the list: when the site built it from the server's board, not each listing's capture
        PlayerBoards.Load("{\"generated_at\":\"2026-10-24T00:20:59Z\",\"crafts\":[]}", null);
        Check("best crafts: the list's age is the board's (generated_at)", PlayerBoards.CraftsBoardAt?.ToString("yyyy-MM-dd HH:mm") ?? "none", "2026-10-24 00:20");
        Check("a row's source line has no age of its own", PriceBook.Describe(new PriceBook.Quote(200_000, PriceBook.From.Feed, 8, true, DateTime.UtcNow.AddHours(-1)), withAge: false), "aion2.exchange · lowest of 8 listings");

        // where to farm: the player's bracket when published there, else every player's
        PlayerBoards.Load(null, FarmJson);
        string Here(string? b, string z) => PlayerBoards.Here(b, z) is { } h ? $"{h.Row.ValueHr:N0} {(h.AtGear ? "at gear" : "all")}" : "none";
        Check("farm: at the player's gear", Here("1400-1599", "Night Dew Cave"), "1,500,000 at gear");
        Check("farm: bracket not published there: every player's", Here("1400-1599", "Crimson Isle"), "900,000 all");
        Check("farm: no gear score: every player's", Here(null, "Night Dew Cave"), "1,310,000 all");
        Check("farm: a zone nobody published", Here("1400-1599", "Western Cantas Valley"), "none");
        var (top, atGear) = PlayerBoards.Top("1600-1799", 5);
        Check("farm: Full view list falls back to every player's, best first", $"{atGear} " + string.Join(", ", top.Select(z => z.Zone)), "False Night Dew Cave, Crimson Isle");
        // every region pooled (Where to farm now's default): the line falls back to it, saying "everywhere", when the server has no row
        PlayerBoards.Load(null, FarmJson, "{\"brackets\":{\"all\":[{\"zone\":\"Western Cantas Valley\",\"n_runs\":30,\"n_installs\":9,\"minutes\":100,\"published\":true,\"value_hr\":2000000},{\"zone\":\"Night Dew Cave\",\"n_runs\":80,\"n_installs\":20,\"minutes\":400,\"published\":true,\"value_hr\":1100000}]}}");
        string Everywhere(string? b, string z) => PlayerBoards.Here(b, z) is { } h ? $"{h.Row.ValueHr:N0} {(h.Everywhere ? "everywhere" : "server")}" : "none";
        Check("farm: the server's own row first", Everywhere("1400-1599", "Night Dew Cave"), "1,500,000 server");
        Check("farm: no row on the server: the all-regions one, said so", Everywhere("1400-1599", "Western Cantas Valley"), "2,000,000 everywhere");
        Check("farm: Full view list over every region by default", string.Join(", ", PlayerBoards.Top(null, 5, allRegions: true).Zones.Select(z => z.Zone)) + " | " + string.Join(", ", PlayerBoards.Top(null, 5).Zones.Select(z => z.Zone)),
            "Western Cantas Valley, Night Dew Cave | Night Dew Cave, Crimson Isle");
        // never listed (0.9.11): the pooled "Unknown zone" row (any case) and an hour over the ceiling, a bad price and not a farm
        PlayerBoards.Load(null, FarmJson, "{\"brackets\":{\"all\":[{\"zone\":\"unknown ZONE\",\"n_runs\":90,\"n_installs\":30,\"minutes\":900,\"published\":true,\"value_hr\":1900000},{\"zone\":\"Altamia Canyon\",\"n_runs\":12,\"n_installs\":5,\"minutes\":300,\"published\":true,\"value_hr\":32000000},{\"zone\":\"Night Dew Cave\",\"n_runs\":80,\"n_installs\":20,\"minutes\":400,\"published\":true,\"value_hr\":1100000}]}}");
        Check("farm: no unknown zone, no impossible hour", string.Join(", ", PlayerBoards.Top(null, 5, allRegions: true).Zones.Select(z => z.Zone)) + $" | {Here(null, SessionEngine.UnknownZone)}", "Night Dew Cave | none");
        PlayerBoards.Load(null, "{\"brackets\":{}}", "{\"brackets\":{}}");

        // the gear score's bracket, as runs are shared
        Check("gear score 1450: bracket and payload", $"{GearScore.Bracket(1450)} {GearScore.Payload(1450)}", "1400-1599 gs:1400-1599");
        Check("gear score 1600 starts the next bracket; unset is unknown", $"{GearScore.Bracket(1600)} {GearScore.Payload(0)}", "1600-1799 unknown");

        // price alerts: once per crossing, re-armed when the price goes back
        var dir = TestTemp.Path("KinahLedger-player-test");
        Directory.CreateDirectory(dir); var file = Path.Combine(dir, "alerts.json"); if (File.Exists(file)) File.Delete(file);
        PriceAlerts.DirOverride = dir; PriceAlerts.Reset();
        const string siel = "Siel (Elyos)";
        PriceAlerts.Set(siel, "Fine Diamond Gemstone", above: false, 1_000);
        PriceAlerts.Set(siel, "Odyle", above: true, 4_000);
        PriceAlerts.Set("Israphel (Asmodian)", "Odyle", above: true, 1);
        var prices = new Dictionary<string, long> { ["Fine Diamond Gemstone"] = 1_141, ["Odyle"] = 3_480 };
        var t = DateTime.UtcNow;
        string Tick() { var f = PriceAlerts.Check(siel, n => prices.TryGetValue(n, out var p) ? p : null, t = t.AddMinutes(15)); return f.Count == 0 ? "-" : string.Join(", ", f.Select(x => $"{x.Alert.Item} {x.Price:N0}")); }
        var seq = new List<string> { Tick() };
        prices["Fine Diamond Gemstone"] = 980; seq.Add(Tick());
        prices["Fine Diamond Gemstone"] = 950; seq.Add(Tick());
        prices["Fine Diamond Gemstone"] = 1_100; seq.Add(Tick());
        prices["Fine Diamond Gemstone"] = 990; prices["Odyle"] = 4_200; seq.Add(Tick());
        Check("alerts: crossing fires once, back over re-arms, other servers apart", string.Join(" | ", seq), "- | Fine Diamond Gemstone 980 | - | - | Fine Diamond Gemstone 990, Odyle 4,200");
        PriceAlerts.Set(siel, "Odyle", above: false, 3_000);
        PriceAlerts.Reset();   // as after a restart: read back from the file
        Check("alerts: one per item, a new rule replaces it, kept in the file", string.Join(", ", PriceAlerts.For(siel).OrderBy(a => a.Item).Select(a => $"{a.Item} {a.Rule} {(a.Armed ? "armed" : "fired")}")),
            "Fine Diamond Gemstone below 1,000 fired, Odyle below 3,000 armed");
        Check("alerts: remove", $"{PriceAlerts.Remove(siel, "Odyle")} {PriceAlerts.For(siel).Count}", "True 1");
        PriceAlerts.DirOverride = null; PriceAlerts.Reset();

        // the recipe book: the player's faction's materials, and the recipe that makes as many as the site priced
        var was = RecipeBook.PreferFaction;
        RecipeBook.PreferFaction = 'E'; var e = RecipeBook.ForOutput("Wind Serum");
        RecipeBook.PreferFaction = 'A'; var a = RecipeBook.ForOutput("Wind Serum");
        Check("recipe book: Elyos and Asmodian materials differ", e != null && a != null ? $"{e.Faction}:{e.Materials[0].Item} {a.Faction}:{a.Materials[0].Item}" : "missing", e != null && a != null && e.Materials[0].Item != a.Materials[0].Item ? $"E:{e.Materials[0].Item} A:{a.Materials[0].Item}" : "two factions");
        RecipeBook.PreferFaction = 'E';
        Check("recipe book: the recipe that makes ×30", $"{RecipeBook.For("Sapphire Powder", 30)?.OutputQty} {RecipeBook.For("Sapphire Powder", 30)?.Materials[0].Qty}", "30 10");
        RecipeBook.PreferFaction = was;

        Prices(Check);
        return ok;
    }

    /// <summary>
    /// Prices (2026-10-23, "prices are super inaccurate"): the Siel Expert's Orichalcum Longsword case. The board's 43,500
    /// is the lowest of 42 aion2.exchange listings; Steven saw 80,000 in game; the 330K was the recipe's COST / TRY.
    /// </summary>
    static void Prices(Action<string, string, string> Check)
    {
        var dir = TestTemp.Path("KinahLedger-price-test");
        Directory.CreateDirectory(dir);
        foreach (var f in new[] { "prices.json", "typed-prices.json", "vendor-prices.json", "farmed-materials.json" }) if (File.Exists(Path.Combine(dir, f))) File.Delete(Path.Combine(dir, f));
        var oldPath = PriceBook.PathOverride;
        PriceBook.PathOverride = Path.Combine(dir, "prices.json"); PriceBook.Reset();
        try
        {
            const string sword = "Expert's Orichalcum Longsword";
            var now = DateTime.UtcNow; var seen = now.AddMinutes(-40).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");
            // the site's board as price-pick.ts writes it (names in place of ids, so no item list is needed)
            var board = "{\"items\":[" +
                $"{{\"item\":\"{sword}\",\"lowest_ask\":43500,\"stale\":false,\"n_obs\":42,\"price\":43500,\"price_from\":\"feed\",\"readings\":42,\"sure\":true,\"price_seen_at\":\"{seen}\",\"region_price\":192000,\"region_servers\":7}}," +
                "{\"item\":\"Chaos Longsword\",\"lowest_ask\":5000000,\"stale\":false,\"n_obs\":1,\"price\":5000000,\"price_from\":\"feed\",\"readings\":1,\"sure\":false}," +
                "{\"item\":\"Splendent Orichalcum Longsword\",\"price\":139998,\"price_from\":\"feed\",\"readings\":12,\"sure\":true}," +
                "{\"item\":\"Expert's Refining Stone\",\"price\":59000,\"price_from\":\"feed\",\"readings\":350,\"sure\":true}," +
                "{\"item\":\"Fine Orichalcum Ore\",\"price\":997,\"price_from\":\"feed\",\"readings\":40,\"sure\":true}," +
                "{\"item\":\"Fine Odyle\",\"price\":3098,\"price_from\":\"feed\",\"readings\":117,\"sure\":true}," +
                "{\"item\":\"Old Board Item\",\"lowest_ask\":777,\"stale\":false,\"n_obs\":1}," +
                $"{{\"item\":\"Four Day Item\",\"price\":6000,\"price_from\":\"feed\",\"readings\":20,\"sure\":true,\"price_seen_at\":\"{now.AddDays(-4):yyyy-MM-dd'T'HH:mm:ss'Z'}\"}}," +
                "{\"item\":\"Typed Old Item\",\"price\":5000,\"price_from\":\"feed\",\"readings\":30,\"sure\":true}," +
                "{\"item\":\"Sold Item\",\"price\":7000,\"price_from\":\"sold\",\"readings\":9,\"sure\":true,\"price_seen_at\":null}," +
                "{\"item\":\"Artisan's Splendent Orichalcum Longsword\",\"price\":23999999,\"price_from\":\"feed\",\"readings\":12,\"sure\":true}]}";
            PriceBook.SetBoardQuotes(PriceBook.ParseQuotes(board, null));
            PriceBook.SetRegionBoards(new[] { 2_000_000L, 2_100_000L, 1_900_000L }.Select(p => new Dictionary<string, long> { ["Artisan's Splendent Orichalcum Longsword"] = p }));

            string Q(string n) => PriceBook.QuoteOf(n) is { } q ? $"{q.Price:N0} {q.From} {(q.Sure ? "sure" : "unsure")}" : "none";
            Check("the sword on the board: the lowest of 42 listings, sure", Q(sword) + " · " + PriceBook.Describe(PriceBook.QuoteOf(sword)!, now), "43,500 Feed sure · aion2.exchange · lowest of 42 listings · 40 min ago");
            Check("a lone listing is shown, unsure, and out of totals", $"{Q("Chaos Longsword")} · Price {PriceBook.Price("Chaos Longsword")?.ToString() ?? "null"} · {PriceBook.Describe(PriceBook.QuoteOf("Chaos Longsword")!, now)}", "5,000,000 Feed unsure · Price null · aion2.exchange · 1 listing · board checked just now · unsure");
            Check("an older board's single listing is unsure too", Q("Old Board Item"), "777 Feed unsure");
            // 2026-11-03: a board price last seen 4 days ago is unsure; a sold average says when the board was checked
            Check("a 4-day-old board price is unsure and out of totals", $"{Q("Four Day Item")} · Price {PriceBook.Price("Four Day Item")?.ToString() ?? "null"} · {PriceBook.Describe(PriceBook.QuoteOf("Four Day Item")!, now)}", "6,000 Feed unsure · Price null · aion2.exchange · lowest of 20 listings · 4 days ago · unsure");
            Check("a sold average with no time says when the board was checked", $"{Q("Sold Item")} · {PriceBook.Describe(PriceBook.QuoteOf("Sold Item")!, now)}", "7,000 Sold sure · sold average · 9 sold · board checked just now");
            // 2026-11-03: a typed price over 7 days old loses to a sure board price; with no board price it stands
            PriceBook.SetTyped("Typed Old Item", 9_000, now.AddDays(-8)); PriceBook.SetTyped("Typed Only Item", 7_000, now.AddDays(-8)); PriceBook.SetTyped("Typed New Item", 9_000, now.AddDays(-6));
            var typedCmp = PriceBook.QuoteOf("Typed Old Item")!;
            Check("an 8-day-old typed price loses to the board's", $"{Q("Typed Old Item")} · {PriceBook.Describe(typedCmp, now)}", $"5,000 Feed sure · you typed 9,000 on {now.AddDays(-8).ToLocalTime():yyyy-MM-dd}; the board says 5,000 · aion2.exchange · lowest of 30 listings · board checked just now");
            PriceBook.SetBoardQuotes(PriceBook.ParseQuotes("{\"items\":[{\"item\":\"Typed New Item\",\"price\":5000,\"price_from\":\"feed\",\"readings\":30,\"sure\":true}]}", null));
            Check("a typed price with no board price stands, and a 6-day-old one beats the board", $"{Q("Typed Only Item")} · {Q("Typed New Item")}", "7,000 Typed sure · 9,000 Typed sure");
            PriceBook.SetBoardQuotes(PriceBook.ParseQuotes(board, null));
            // the site's pick stands even far over the other servers' middle: it was judged against the region with every listing
            // (2026-11-03: the tracker's cruder middle threw out Siel's Artisan's Steamed Cypri, 44,994 with 57 listings)
            Check("the site's pick stands over the other servers' middle", Q("Artisan's Splendent Orichalcum Longsword"), "23,999,999 Feed sure");
            Check("the 330K was the recipe's cost per try, not the sword's price", $"cost/try {RecipeBook.CostPerTry(sword, out var unp):N0} ({unp} unpriced) · sword {PriceBook.Price(sword):N0}", "cost/try 450,274 (0 unpriced) · sword 43,500");

            // 2026-11-03: a Market read is kept only for a real item, and not 10x off the board (the owner's prices.json held "dawn legion"=461)
            var known = new ItemDictionary(); known.Remember(-1, sword); known.Remember(-2, "Chaos Longsword");
            PriceBook.Items = known;
            PriceBook.Observe("Dawn Legion", 461, now); PriceBook.Observe(sword, 435_000, now); PriceBook.Observe(sword, 4_000, now);
            Check("a junk name and an ask 10x the board are refused", $"dawn {PriceBook.QuoteOf("Dawn Legion")?.Price.ToString() ?? "none"} · sword {Q(sword)}", "dawn none · sword 43,500 Feed sure");
            File.WriteAllText(PriceBook.PathOverride!, "{\"prices\":{\"dawn legion\":461,\"t the party\":160000,\"" + sword.ToLowerInvariant() + "\":80000},\"seen\":{\"dawn legion\":\"" + seen + "\",\"t the party\":\"" + seen + "\",\"" + sword.ToLowerInvariant() + "\":\"" + seen + "\"}}");
            PriceBook.Reset();
            Check("stored keys that are not items are dropped on load", $"{PriceBook.QuoteOf("dawn legion")?.Price.ToString() ?? "none"} · {Q(sword)}", "none · 80,000 Market sure");
            File.Delete(PriceBook.PathOverride!); PriceBook.Reset();

            // his Market read wins at once (read with sharing off too: only sending follows the switch)
            PriceBook.Observe(sword, 80_000, now.AddMinutes(-2));
            Check("this PC's Market read wins over the board", Q(sword) + " · " + PriceBook.Describe(PriceBook.QuoteOf(sword)!, now) + $" · board still {PriceBook.BoardOf(sword)?.Price:N0}", "80,000 Market sure · your Market read · 2 min ago · board still 43,500");
            PriceBook.SetTyped(sword, 82_000, now.AddMinutes(-1));
            Check("a typed price is newer: it wins", Q(sword), "82,000 Typed sure");
            PriceBook.Observe(sword, 79_000, now);
            Check("a newer Market read replaces the typed one", Q(sword), "79,000 Market sure");
            PriceBook.Reset();
            Check("typed and read prices are kept on this PC", $"{PriceBook.TypedPrice(sword):N0} {Q(sword)}", "82,000 79,000 Market sure");
            PriceBook.SetTyped("Chaos Longsword", 4_200_000, now);
            Check("a typed price for an unsure item makes it count", $"{PriceBook.Price("Chaos Longsword"):N0}", "4,200,000");

            // best crafts at his own prices: his 80,000 for the sword in place of the board's 43,500
            var c = new PlayerBoards.BestCraft(1, sword, "Blacksmithing", 15, 1, 0.768285, 0.192, 2, 1_630_000, 43_500, 42,
                new() { new(10, 1, 139_998, false), new(11, 5, 59_000, false), new(12, 6, 997, false), new(13, 3, 3_098, false) }, 450_274, 0, -214_845);
            Check("best craft: the site's profit when there's no own price", CraftMath.OwnProfit(c, _ => null, 0.88).ToString("N0"), "-214,845");
            Check("best craft: his read of the sword redoes the profit", CraftMath.OwnProfit(c, id => id == 1 ? 80_000 : null, 0.88).ToString("N0"), $"{Math.Round(CraftMath.ProfitPerTry(0.768285, 0.192, 80_000, 1_630_000, 450_274, 0.88)):N0}");
        }
        finally
        {
            PriceBook.PathOverride = oldPath; PriceBook.Items = null; PriceBook.Reset();
            PriceBook.SetBoard(new()); PriceBook.SetRegionBoards(Array.Empty<Dictionary<string, long>>());
        }
    }
}
