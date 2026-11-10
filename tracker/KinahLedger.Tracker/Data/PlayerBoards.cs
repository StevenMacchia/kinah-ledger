using System.Net.Http;
using System.Text.Json;

namespace KinahLedger.Data;

/// <summary>
/// Two boards kinahledger.com builds for each server, read like the price board (nothing is sent), every 15 minutes
/// while sharing is on: the best crafts today (/crafts/{server}.json: every recipe at the server's lowest asks, profit
/// per try) and where to farm (/farm/{server}.json: what players' shared open-world runs made an hour by zone and
/// gear bracket, last 7 days) and, for Where to farm now, the same board pooled over every region (/farm/all.json, 0.9.9).
/// Empty until the first fetch; the window hides what isn't there.
/// </summary>
public static class PlayerBoards
{
    public sealed record Material(long Id, int Qty, long? Price, bool Vendor);
    public sealed record BestCraft(long Id, string Name, string Profession, int? Level, int Makes, double Success, double Combo, long? UpgradeId, long? UpgradePrice,
        long Price, int NObs, List<Material> Materials, long Cost, long Value, long Profit);
    public sealed record FarmZone(string Zone, int Runs, int Players, int Minutes, bool Published, long? ValueHr, long? KinaHr);

    static readonly object Lock = new();
    static List<BestCraft> _crafts = new();
    static Dictionary<string, List<FarmZone>> _farm = new(StringComparer.OrdinalIgnoreCase), _farmAll = new(StringComparer.OrdinalIgnoreCase);
    public static DateTime? CraftsAt { get; private set; }
    /// <summary>When the site built the best crafts from the server's board (its generated_at): the age the list shows.</summary>
    public static DateTime? CraftsBoardAt { get; private set; }
    public static DateTime? FarmAt { get; private set; }
    public static int MinRuns { get; private set; } = 5;
    public static int MinPlayers { get; private set; } = 3;
    public const string AllGear = "all";
    /// <summary>
    /// The most a zone's row may say players make an hour (Kinah plus loot, after the Market fee) and still be listed. Above
    /// it the figure is a bad price, not a farm: on 2026-11-03 gear listed at absurd lowest asks inflated the loot's worth
    /// and put a zone near 32M an hour at the top of Where to farm now. Real open-world hours are well under 5M.
    /// </summary>
    public const long MaxValueHr = 5_000_000;

    public static IReadOnlyList<BestCraft> Crafts { get { lock (Lock) return _crafts; } }

    public static async Task RefreshAsync(HttpClient http, string origin, string serverKey)
    {
        try
        {
            var r = await http.GetAsync($"{origin}/crafts/{serverKey}.json");
            if (r.IsSuccessStatusCode) { var body = await r.Content.ReadAsStringAsync(); var c = ParseCrafts(body); lock (Lock) { _crafts = c; CraftsAt = DateTime.UtcNow; CraftsBoardAt = GeneratedAt(body); } }
        }
        catch (Exception ex) { CrashLog.Note("site crafts: " + ex.Message); }
        try
        {
            var r = await http.GetAsync($"{origin}/farm/{serverKey}.json");
            if (r.IsSuccessStatusCode) LoadFarm(await r.Content.ReadAsStringAsync());
        }
        catch (Exception ex) { CrashLog.Note("site farm: " + ex.Message); }
        // every region pooled (what Where to farm now shows first); a site without it leaves the server's board to stand in
        try
        {
            var r = await http.GetAsync($"{origin}/farm/all.json");
            if (r.IsSuccessStatusCode) LoadFarm(await r.Content.ReadAsStringAsync(), all: true);
        }
        catch (Exception ex) { CrashLog.Note("site farm, all regions: " + ex.Message); }
    }

    public static List<BestCraft> ParseCrafts(string json)
    {
        var outp = new List<BestCraft>();
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("crafts", out var arr) || arr.ValueKind != JsonValueKind.Array) return outp;
        foreach (var c in arr.EnumerateArray())
        {
            var mats = new List<Material>();
            if (c.TryGetProperty("materials", out var ms) && ms.ValueKind == JsonValueKind.Array)
                foreach (var m in ms.EnumerateArray())
                    mats.Add(new Material(LongOf(Str(m, "item")), (int)(Num(m, "qty") ?? 1), Num(m, "price") is double p ? (long)p : null, m.TryGetProperty("vendor", out var v) && v.ValueKind == JsonValueKind.True));
            long? upId = null, upPrice = null;
            if (c.TryGetProperty("upgrade", out var u) && u.ValueKind == JsonValueKind.Object) { upId = LongOf(Str(u, "item")); upPrice = Num(u, "price") is double up ? (long)up : null; }
            var name = Str(c, "name");
            if (name.Length == 0) continue;
            outp.Add(new BestCraft(LongOf(Str(c, "item")), name, Str(c, "profession"), Num(c, "level") is double lv ? (int)lv : null, (int)(Num(c, "makes") ?? 1),
                Num(c, "success") ?? 0, Num(c, "combo") ?? 0, upId, upPrice, (long)(Num(c, "price") ?? 0), (int)(Num(c, "n_obs") ?? 0), mats,
                (long)(Num(c, "cost") ?? 0), (long)(Num(c, "value") ?? 0), (long)(Num(c, "profit") ?? 0)));
        }
        return outp;
    }

    public static void LoadFarm(string json, bool all = false)
    {
        var d = new Dictionary<string, List<FarmZone>>(StringComparer.OrdinalIgnoreCase);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        int minRuns = 5, minPlayers = 3;
        if (root.TryGetProperty("publish_rule", out var pr)) { minRuns = (int)(Num(pr, "min_runs") ?? 5); minPlayers = (int)(Num(pr, "min_installs") ?? 3); }
        if (root.TryGetProperty("brackets", out var br) && br.ValueKind == JsonValueKind.Object)
            foreach (var b in br.EnumerateObject())
            {
                var list = new List<FarmZone>();
                if (b.Value.ValueKind == JsonValueKind.Array)
                    foreach (var z in b.Value.EnumerateArray())
                    {
                        // never a row the tracker can't trust (0.9.11): the pooled runs whose zone wasn't read, or an hour no one could make
                        var zone = Str(z, "zone");
                        if (zone.Length == 0 || string.Equals(zone, Session.SessionEngine.UnknownZone, StringComparison.OrdinalIgnoreCase) || Num(z, "value_hr") > MaxValueHr) continue;
                        list.Add(new FarmZone(zone, (int)(Num(z, "n_runs") ?? 0), (int)(Num(z, "n_installs") ?? 0), (int)(Num(z, "minutes") ?? 0),
                            z.TryGetProperty("published", out var p) && p.ValueKind == JsonValueKind.True, Num(z, "value_hr") is double vh ? (long)vh : null, Num(z, "kina_hr") is double kh ? (long)kh : null));
                    }
                d[b.Name] = list;
            }
        lock (Lock) { if (all) _farmAll = d; else { _farm = d; MinRuns = minRuns; MinPlayers = minPlayers; } FarmAt = DateTime.UtcNow; }
    }

    /// <summary>
    /// What players make an hour in a zone: on the player's own server first (at their gear bracket when that row is published,
    /// else every player's there), and when the server's board has no published row for the zone, the pooled all-regions
    /// one (<c>Everywhere</c>), so the line isn't hidden because the server's row is thin. Null when none is published.
    /// AtGear says which row it is.
    /// </summary>
    public static (FarmZone Row, bool AtGear, bool Everywhere)? Here(string? bracket, string? zone)
    {
        if (string.IsNullOrWhiteSpace(zone)) return null;
        lock (Lock)
        {
            FarmZone? Find(Dictionary<string, List<FarmZone>> board, string b) => board.TryGetValue(b, out var l) ? l.FirstOrDefault(z => z.Published && z.ValueHr != null && string.Equals(z.Zone, zone, StringComparison.OrdinalIgnoreCase)) : null;
            (FarmZone, bool)? In(Dictionary<string, List<FarmZone>> board)
            {
                if (!string.IsNullOrEmpty(bracket) && Find(board, bracket) is { } g) return (g, true);
                return Find(board, AllGear) is { } a ? (a, false) : null;
            }
            if (In(_farm) is { } mine) return (mine.Item1, mine.Item2, false);
            return In(_farmAll) is { } all ? (all.Item1, all.Item2, true) : null;
        }
    }

    /// <summary>
    /// The best zones for a bracket (published only, most per hour first), else every player's when the bracket has none; from
    /// the all-regions board when <paramref name="allRegions"/>, else the player's server's.
    /// </summary>
    public static (List<FarmZone> Zones, bool AtGear) Top(string? bracket, int n, bool allRegions = false)
    {
        lock (Lock)
        {
            var board = allRegions ? _farmAll : _farm;
            List<FarmZone> Of(string b) => board.TryGetValue(b, out var l) ? l.Where(z => z.Published && z.ValueHr != null).OrderByDescending(z => z.ValueHr).Take(n).ToList() : new();
            if (!string.IsNullOrEmpty(bracket) && Of(bracket) is { Count: > 0 } g) return (g, true);
            return (Of(AllGear), false);
        }
    }

    /// <summary>For tests and layout renders: replace the boards.</summary>
    public static void Load(string? craftsJson, string? farmJson, string? farmAllJson = null)
    {
        if (farmAllJson != null) LoadFarm(farmAllJson, all: true);
        if (craftsJson != null) { var c = ParseCrafts(craftsJson); lock (Lock) { _crafts = c; CraftsAt = DateTime.UtcNow; CraftsBoardAt = GeneratedAt(craftsJson); } }
        if (farmJson != null) LoadFarm(farmJson);
    }

    static DateTime? GeneratedAt(string json)
    {
        try { using var d = JsonDocument.Parse(json); return d.RootElement.TryGetProperty("generated_at", out var g) && g.ValueKind == JsonValueKind.String && g.TryGetDateTime(out var t) ? t.ToUniversalTime() : null; }
        catch (Exception) { return null; }
    }

    static long LongOf(string s) => long.TryParse(s, out var v) ? v : 0;
    static double? Num(JsonElement e, string p) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;
    static string Str(JsonElement e, string p) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
}

/// <summary>The profit of one craft try, the same way on the site's best crafts list and on a recipe card.</summary>
public static class CraftMath
{
    /// <summary>
    /// success × what a success sells for after the Market's cut − what the try costs. A success turns into the upgrade
    /// with chance <paramref name="combo"/> (valued as the normal result when the upgrade has no price); materials are
    /// lost on a fail.
    /// </summary>
    public static double ProfitPerTry(double success, double combo, double normal, double? upgrade, double cost, double keeps)
        => success * keeps * ((1 - combo) * normal + combo * (upgrade ?? normal)) - cost;

    /// <summary>Where the success chance a card or the HUD counts with came from.</summary>
    public enum RateKind { Listed, Yours, Observed, Unknown }

    /// <summary>Crafts before a measured success rate stands in for a missing listed one, and before it overrules a listed one.</summary>
    public const int ObservedFrom = 10, YoursFrom = 20;

    /// <summary>
    /// The success chance to count with, never assumed 100% as a fact. The listed chance, unless the player has at least
    /// <see cref="YoursFrom"/> crafts and their own rate differs from it (then theirs). With no listed chance, the measured rate
    /// from <see cref="ObservedFrom"/> crafts; with fewer the chance is unknown and is counted as 100% only as an upper bound.
    /// </summary>
    public static (double Rate, RateKind Kind) SuccessUsed(double? listed, int attempts, int ok)
    {
        double? mine = attempts > 0 ? (double)ok / attempts : null;
        if (listed is double l)
            return attempts >= YoursFrom && mine is double m && Math.Round(m * 100) != Math.Round(l * 100) ? (m, RateKind.Yours) : (l, RateKind.Listed);
        return attempts >= ObservedFrom && mine is double o ? (o, RateKind.Observed) : (1, RateKind.Unknown);
    }

    /// <summary>
    /// How often a craft that worked turns into this upgrade: the combo's chance times the chance the combo then succeeds
    /// (the site's best crafts use the same). Null when the recipe doesn't list one.
    /// </summary>
    public static double? UpgradeChance(RecipeBook.Recipe recipe, RecipeBook.Great g) => g.Chance is double c ? c * (recipe.ComboSuccess ?? 1) : null;

    /// <summary>
    /// One recipe's verdict at a price list: the profit per craft, the combo that went into it, and the sell price of the
    /// item that makes it break even. The card and the HUD both read this, so they never disagree.
    /// </summary>
    /// <param name="Combo">The chance a craft turns into the upgrade, or into two (<paramref name="Doubles"/>); 0 when the upgrade has no price.</param>
    /// <param name="UpgradeUnpriced">The recipe has an upgrade the price list doesn't price: it is counted as the base item.</param>
    /// <param name="BreakEven">The Market price of the output (each) at which a craft makes nothing; null when nothing sells for that.</param>
    public sealed record Verdict(double Profit, double Combo, bool Doubles, bool UpgradeUnpriced, double? BreakEven);

    /// <summary>
    /// The verdict of <paramref name="recipe"/> costing <paramref name="cost"/> a craft, with <paramref name="rate"/> of crafts
    /// working; null when the output has no price (nothing to sell). A won combo makes two when the recipe has no upgrade item.
    /// </summary>
    public static Verdict? Judge(RecipeBook.Recipe recipe, Func<string, long?> price, double cost, double rate, double keeps)
    {
        if (price(recipe.Output) is not long op) return null;
        double normal = op * recipe.OutputQty;
        var up = recipe.Upgrade;
        double combo = up != null && UpgradeChance(recipe, up) is double gc ? gc : 0;
        double? upPrice = up != null && price(up.Item) is long upp ? upp * up.Qty : null;
        // no upgrade item: a won combo makes the craft twice over, at the combo's chance times its own success
        bool doubles = up == null && recipe.ComboChance is > 0 && normal > 0;
        if (doubles) { combo = recipe.ComboChance!.Value * (recipe.ComboSuccess ?? 1); upPrice = normal * 2; }
        bool unpriced = up != null && combo > 0 && upPrice == null;
        if (upPrice == null) combo = 0;
        double profit = ProfitPerTry(rate, combo, normal, upPrice, cost, keeps);
        // cost = rate × keeps × (units × price + combo × upgrade value): solve for the price
        double? even = null;
        double units = recipe.OutputQty * (doubles ? 1 + combo : 1 - combo);
        if (rate > 0 && keeps > 0 && units > 0)
        {
            double e = (cost / (rate * keeps) - (doubles ? 0 : combo * (upPrice ?? 0))) / units;
            if (e > 0) even = e;
        }
        return new Verdict(profit, combo, doubles, unpriced, even);
    }

    /// <summary>Listings under which a board price is a thin market: a few asks, any of them someone's guess.</summary>
    public const int ThinListings = 10;

    /// <summary>
    /// The bought materials whose price is a thin market that a craft can't be judged on: the server's board prices it on
    /// fewer than <see cref="ThinListings"/> listings and the craft's share of it alone costs more than what the craft makes
    /// (2026-11-08: Siel's Lesser Manastone at 53,333 from 6 asks, so 5 of them cost 267K for one Intermediate Manastone
    /// at 80K; Steven: "u gotta get the prices at least CLOSE to right always"). The price is kept, as the server's market
    /// as it is; the card and the HUD mark the profit unsure. A player's own price (typed or read) and a ticked "I have
    /// these" material are never thin. On the board's materials alone, one costing more than the output is common (an
    /// Artisan's sword takes an Expert's Splendent one): it is the few listings that make it suspect.
    /// </summary>
    public static List<(string Item, int Listings)> Thin(RecipeBook.Recipe recipe, Func<string, PriceBook.Quote?> quote, Func<string, bool> farmed)
    {
        var thin = new List<(string, int)>();
        if (quote(recipe.Output) is not { } oq) return thin;
        double makes = (double)oq.Price * recipe.OutputQty;
        foreach (var m in recipe.Materials)
        {
            if (m.Item.Contains("(Bound)", StringComparison.OrdinalIgnoreCase) || farmed(m.Item)) continue;
            // every server price counts in the cost (0.10.33), a one-listing ask too; the other servers' middle never does
            if (quote(m.Item) is not { Mine: false } q || q.From == PriceBook.From.Region || q.Readings >= ThinListings) continue;
            if ((double)q.Price * m.Qty > makes) thin.Add((m.Item, q.Readings));
        }
        return thin;
    }

    /// <summary>"Lesser Manastone on 6 listings", "A on 3 listings and B on 1 listing": the thin materials, for the card's notes.</summary>
    public static string ThinWords(IReadOnlyList<(string Item, int Listings)> thin) =>
        string.Join(" and ", thin.Select(t => $"{t.Item} on {t.Listings} listing{(t.Listings == 1 ? "" : "s")}"));

    /// <summary>
    /// A best craft's profit at this player's own prices where they have one (typed, or read on their PC: they win over the
    /// board), the site's for the rest. With no own price at all it is the site's profit unchanged.
    /// </summary>
    public static long OwnProfit(PlayerBoards.BestCraft c, Func<long, long?> own, double keeps)
    {
        bool any = false;
        long? Own(long id) { var p = own(id); if (p is > 0) { any = true; return p; } return null; }
        double cost = 0;
        foreach (var m in c.Materials.Where(m => !m.Vendor)) cost += (Own(m.Id) ?? m.Price ?? 0) * m.Qty;
        var output = Own(c.Id) ?? c.Price;
        var upgrade = c.UpgradeId is long u ? Own(u) ?? c.UpgradePrice : null;
        if (!any) return c.Profit;
        return (long)Math.Round(ProfitPerTry(c.Success, c.Combo, (double)output * c.Makes, upgrade, cost, keeps));
    }

    /// <summary>
    /// A best craft with the vendor costs this player typed added: where the site could not price a vendor material, its
    /// profit leaves it out. A vendor material the site did price (rows since the site learned the NPC prices) is already
    /// in the profit and is not charged again. Unpriced counts the vendor materials still without a cost.
    /// </summary>
    public static (long Profit, int Unpriced) WithVendor(PlayerBoards.BestCraft c, Func<long, long?> vendorPrice)
    {
        long extra = 0; int unpriced = 0;
        foreach (var m in c.Materials.Where(m => m.Vendor && m.Price is not > 0))
            if (vendorPrice(m.Id) is long p && p > 0) extra += p * m.Qty; else unpriced++;
        return (c.Profit - extra, unpriced);
    }

    /// <summary>
    /// A best craft with the materials this player ticked "I have these" for taken out of its cost: the site took a farmed
    /// material's price off the profit, so it is added back; a farmed vendor material is dropped from the list, so it is
    /// neither charged its NPC price nor counted as unpriced. Done before <see cref="OwnProfit"/> and <see cref="WithVendor"/>,
    /// which then price only what is still bought. Farmed counts the ticked materials.
    /// </summary>
    public static (PlayerBoards.BestCraft Craft, int Farmed) WithFarmed(PlayerBoards.BestCraft c, Func<long, bool> isFarmed)
    {
        long back = 0; int farmed = 0;
        var keep = new List<PlayerBoards.Material>();
        foreach (var m in c.Materials)
        {
            if (!isFarmed(m.Id)) { keep.Add(m); continue; }
            farmed++;
            if (m.Price is long p) back += p * m.Qty;
        }
        if (farmed == 0) return (c, 0);
        return (c with { Materials = keep, Cost = c.Cost - back, Profit = c.Profit + back }, farmed);
    }
}

/// <summary>
/// The player's gear score (Options, or the first-run screen) as the bracket runs are shared and compared under:
/// 200 wide ("1400-1599"), sent as "gs:1400-1599" so the site can tell it from the fixed bracket every tracker sent
/// before 0.8.12. Unset sends "unknown", which counts only among every player's runs.
/// </summary>
public static class GearScore
{
    public const int Width = 200;

    public static string? Bracket(int score) => score > 0 && score < 100_000 ? $"{score / Width * Width}-{score / Width * Width + Width - 1}" : null;

    public static string Payload(int score) => Bracket(score) is { } b ? "gs:" + b : "unknown";
}
