using System.Net.Http;
using System.Text.Json;

namespace KinahLedger.Data;

/// <summary>
/// Two boards kinahledger.com builds for each server, read like the price board (nothing is sent), every 15 minutes
/// while sharing is on: the best crafts today (/crafts/{server}.json: every recipe at the server's lowest asks, profit
/// per try) and where to farm (/farm/{server}.json: what players' shared open-world runs made an hour by zone and
/// gear bracket, last 7 days). Empty until the first fetch; the window hides what isn't there.
/// </summary>
public static class PlayerBoards
{
    public sealed record Material(long Id, int Qty, long? Price, bool Vendor);
    public sealed record BestCraft(long Id, string Name, string Profession, int? Level, int Makes, double Success, double Combo, long? UpgradeId, long? UpgradePrice,
        long Price, int NObs, List<Material> Materials, long Cost, long Value, long Profit);
    public sealed record FarmZone(string Zone, int Runs, int Players, int Minutes, bool Published, long? ValueHr, long? KinaHr);

    static readonly object Lock = new();
    static List<BestCraft> _crafts = new();
    static Dictionary<string, List<FarmZone>> _farm = new(StringComparer.OrdinalIgnoreCase);
    public static DateTime? CraftsAt { get; private set; }
    /// <summary>When the site built the best crafts from the server's board (its generated_at): the age the list shows.</summary>
    public static DateTime? CraftsBoardAt { get; private set; }
    public static DateTime? FarmAt { get; private set; }
    public static int MinRuns { get; private set; } = 5;
    public static int MinPlayers { get; private set; } = 3;
    public const string AllGear = "all";

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

    public static void LoadFarm(string json)
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
                        list.Add(new FarmZone(Str(z, "zone"), (int)(Num(z, "n_runs") ?? 0), (int)(Num(z, "n_installs") ?? 0), (int)(Num(z, "minutes") ?? 0),
                            z.TryGetProperty("published", out var p) && p.ValueKind == JsonValueKind.True, Num(z, "value_hr") is double vh ? (long)vh : null, Num(z, "kina_hr") is double kh ? (long)kh : null));
                d[b.Name] = list;
            }
        lock (Lock) { _farm = d; MinRuns = minRuns; MinPlayers = minPlayers; FarmAt = DateTime.UtcNow; }
    }

    /// <summary>
    /// What players make an hour in a zone: at the player's gear bracket when that row is published, else every player's
    /// (gear unknown or not enough players at it yet). Null when neither is published. AtGear says which one it is.
    /// </summary>
    public static (FarmZone Row, bool AtGear)? Here(string? bracket, string? zone)
    {
        if (string.IsNullOrWhiteSpace(zone)) return null;
        lock (Lock)
        {
            FarmZone? Find(string b) => _farm.TryGetValue(b, out var l) ? l.FirstOrDefault(z => z.Published && z.ValueHr != null && string.Equals(z.Zone, zone, StringComparison.OrdinalIgnoreCase)) : null;
            if (!string.IsNullOrEmpty(bracket) && Find(bracket) is { } g) return (g, true);
            return Find(AllGear) is { } a ? (a, false) : null;
        }
    }

    /// <summary>The best zones for a bracket (published only, most per hour first), else every player's when the bracket has none.</summary>
    public static (List<FarmZone> Zones, bool AtGear) Top(string? bracket, int n)
    {
        lock (Lock)
        {
            List<FarmZone> Of(string b) => _farm.TryGetValue(b, out var l) ? l.Where(z => z.Published && z.ValueHr != null).OrderByDescending(z => z.ValueHr).Take(n).ToList() : new();
            if (!string.IsNullOrEmpty(bracket) && Of(bracket) is { Count: > 0 } g) return (g, true);
            return (Of(AllGear), false);
        }
    }

    /// <summary>For tests and layout renders: replace both boards.</summary>
    public static void Load(string? craftsJson, string? farmJson)
    {
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
