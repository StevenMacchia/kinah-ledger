using System.Text.Json;

namespace KinahLedger.Data;

/// <summary>
/// Market prices by item name, so drops and gathered materials can be valued in Kinah. Two sources, nearest first:
/// the lowest ask this PC read on the Market in the last day (kept in %LOCALAPPDATA%\KinahLedger\prices.json), then
/// the community price board on kinahledger.com for the player's server. Nothing is valued with made-up numbers: an
/// item neither source has a price for is left out of the value, and the window says how many were.
/// </summary>
public static class PriceBook
{
    sealed record Seen(long price, DateTime at);

    static readonly object Lock = new();
    static Dictionary<string, Seen>? _local;
    static Dictionary<string, long> _board = new();

    /// <summary>How long an ask read on this PC stays the price before the board's takes over again.</summary>
    public static TimeSpan LocalFor { get; set; } = TimeSpan.FromHours(24);
    /// <summary>What a seller keeps of a sale: the Market takes a listing fee and a sales tax, about 12% together.</summary>
    public static double SellerKeeps { get; set; } = 0.88;
    public static DateTime? BoardAt { get; private set; }
    public static int BoardItems { get { lock (Lock) return _board.Count; } }
    /// <summary>Tests point this elsewhere so the player's own file is never touched.</summary>
    public static string? PathOverride { get; set; }

    public static bool Any { get { lock (Lock) return Local().Count > 0 || _board.Count > 0; } }

    /// <summary>The price of one, as a buyer pays it: this PC's lowest ask of the last day, else the board's.</summary>
    public static long? Price(string name)
    {
        var k = Norm(name);
        lock (Lock)
        {
            if (Local().TryGetValue(k, out var s) && DateTime.UtcNow - s.at <= LocalFor) return s.price;
            return _board.TryGetValue(k, out var b) ? b : null;
        }
    }

    /// <summary>An ask read on the Market: kept when it is the lowest of the last day for that item.</summary>
    public static void Observe(string name, long unitPrice, DateTime at)
    {
        if (unitPrice <= 0) return;
        var k = Norm(name);
        lock (Lock)
        {
            var local = Local();
            if (local.TryGetValue(k, out var s) && at - s.at <= LocalFor && s.price <= unitPrice) return;
            local[k] = new Seen(unitPrice, at);
            Save(local);
        }
    }

    /// <summary>The community board for the player's server, replacing the last one.</summary>
    public static void SetBoard(Dictionary<string, long> prices)
    {
        lock (Lock) { _board = prices.Where(kv => kv.Value > 0).ToDictionary(kv => Norm(kv.Key), kv => kv.Value); BoardAt = DateTime.UtcNow; }
    }

    /// <summary>
    /// Reads a /prices/&lt;server&gt;.json board: per item the lowest ask while it is fresh, else the 24-hour median, the
    /// 7-day median, the 7-day median of what actually sold, then the reference price the site fetches from other price
    /// sites (ref_price). Items keyed by dictionary ID are named through the dictionary.
    /// </summary>
    public static Dictionary<string, long> ParseBoard(string json, ItemDictionary? items)
    {
        var d = new Dictionary<string, long>();
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("items", out var arr) || arr.ValueKind != JsonValueKind.Array) return d;
        foreach (var e in arr.EnumerateArray())
        {
            if (!e.TryGetProperty("item", out var it) || it.ValueKind != JsonValueKind.String) continue;
            var name = it.GetString() ?? "";
            if (items != null && long.TryParse(name, out var id)) name = items.NameOf(id);
            bool stale = e.TryGetProperty("stale", out var st) && st.ValueKind == JsonValueKind.True;
            long? p = (stale ? null : Num(e, "lowest_ask")) ?? Num(e, "median_24h") ?? Num(e, "median_7d") ?? Num(e, "sold_median_7d") ?? Num(e, "ref_price");
            if (p is long v && v > 0 && name.Length > 0) d[name] = v;
        }
        return d;
    }

    static long? Num(JsonElement e, string prop) => e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var x) ? (long)Math.Round(x) : null;

    /// <summary>The prices.json file: { "updated": "…", "prices": { "Orichalcum Ore": 1950 }, "seen": { "Orichalcum Ore": "…" } }.</summary>
    static Dictionary<string, Seen> Local()
    {
        if (_local != null) return _local;
        _local = new Dictionary<string, Seen>();
        try
        {
            var path = PathOverride ?? Path.Combine(CrashLog.Dir, "prices.json");
            if (!File.Exists(path)) return _local;
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (!doc.RootElement.TryGetProperty("prices", out var p)) return _local;
            doc.RootElement.TryGetProperty("seen", out var seen);
            foreach (var kv in p.EnumerateObject())
            {
                if (!kv.Value.TryGetInt64(out var v)) continue;
                var at = seen.ValueKind == JsonValueKind.Object && seen.TryGetProperty(kv.Name, out var a) && a.TryGetDateTime(out var t) ? t.ToUniversalTime() : DateTime.MinValue;
                _local[Norm(kv.Name)] = new Seen(v, at);
            }
        }
        catch (Exception ex) { CrashLog.Write("prices", ex); }
        return _local;
    }

    static void Save(Dictionary<string, Seen> local)
    {
        try
        {
            var path = PathOverride ?? Path.Combine(CrashLog.Dir, "prices.json");
            var doc = new
            {
                updated = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"),
                prices = local.ToDictionary(kv => kv.Key, kv => kv.Value.price),
                seen = local.ToDictionary(kv => kv.Key, kv => kv.Value.at.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'")),
            };
            File.WriteAllText(path, JsonSerializer.Serialize(doc));
        }
        catch (Exception ex) { CrashLog.Write("prices", ex); }
    }

    static string Norm(string s) => string.Join(' ', s.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
}
