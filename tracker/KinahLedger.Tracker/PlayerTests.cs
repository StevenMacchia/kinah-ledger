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
        Check("vendor cost not typed yet: profit as the site has it", $"{CraftMath.WithVendor(c, _ => null)}", "(92640, 1)");
        Check("a typed vendor cost comes off the profit", $"{CraftMath.WithVendor(c, id => id == 300 ? 2_640 : null)}", "(90000, 0)");

        // where to farm: the player's bracket when published there, else every player's
        PlayerBoards.Load(null, FarmJson);
        string Here(string? b, string z) => PlayerBoards.Here(b, z) is { } h ? $"{h.Row.ValueHr:N0} {(h.AtGear ? "at gear" : "all")}" : "none";
        Check("farm: at the player's gear", Here("1400-1599", "Night Dew Cave"), "1,500,000 at gear");
        Check("farm: bracket not published there: every player's", Here("1400-1599", "Crimson Isle"), "900,000 all");
        Check("farm: no gear score: every player's", Here(null, "Night Dew Cave"), "1,310,000 all");
        Check("farm: a zone nobody published", Here("1400-1599", "Western Cantas Valley"), "none");
        var (top, atGear) = PlayerBoards.Top("1600-1799", 5);
        Check("farm: Full view list falls back to every player's, best first", $"{atGear} " + string.Join(", ", top.Select(z => z.Zone)), "False Night Dew Cave, Crimson Isle");
        PlayerBoards.Load(null, "{\"brackets\":{}}");

        // the gear score's bracket, as runs are shared
        Check("gear score 1450: bracket and payload", $"{GearScore.Bracket(1450)} {GearScore.Payload(1450)}", "1400-1599 gs:1400-1599");
        Check("gear score 1600 starts the next bracket; unset is unknown", $"{GearScore.Bracket(1600)} {GearScore.Payload(0)}", "1600-1799 unknown");

        // price alerts: once per crossing, re-armed when the price goes back
        var dir = Path.Combine(Path.GetTempPath(), "KinahLedger-player-test");
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
        return ok;
    }
}
