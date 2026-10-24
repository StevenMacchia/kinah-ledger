using System.Text.Json;

namespace KinahLedger.Data;

/// <summary>
/// Market prices by item name, so drops, gathered materials and crafts can be valued in Kinah. Every price is a quote
/// that says where it is from, how many readings stand behind it and how old it is (Steven, 2026-10-23: "prices are
/// super inaccurate"). Nearest first:
///   1. yours: a price the player typed on a card (typed-prices.json) or the lowest ask this PC read on the Market in
///      the last day (prices.json), whichever is newer. Read with sharing off too; nothing leaves the PC then.
///   2. the server's board on kinahledger.com, as the site picked it: players' reads, what it sold for, or the
///      aion2.exchange listing, with its readings and age. Dropped when it is over 3x the other servers' middle.
///   3. the other servers of the region and faction: their middle, marked ≈.
/// A price with fewer than 2 readings is "unsure": it is shown (dimmed) but <see cref="Price"/> leaves it out, so no
/// total, value per hour or profit leans on one listing. Nothing is valued with made-up numbers.
/// </summary>
public static class PriceBook
{
    sealed record Seen(long price, DateTime at);

    /// <summary>Where a price is from. Market and Typed are the player's own.</summary>
    public enum From { None, Market, Server, Region, Typed, Sold, Feed }

    /// <summary>One price as shown: the figure, where it is from, how many readings back it, whether it counts, when it was seen.</summary>
    public sealed record Quote(long Price, From From, int Readings, bool Sure, DateTime? At)
    {
        public bool Mine => From is From.Market or From.Typed;
    }

    /// <summary>A board item as the site sends it (price-pick.ts on the site), plus the other servers' middle when it has one.</summary>
    public sealed record BoardQuote(long Price, From From, int Readings, bool Sure, DateTime? At, long? Region = null, int RegionServers = 0);

    static readonly object Lock = new();
    static Dictionary<string, Seen>? _local, _typed;
    static Dictionary<string, BoardQuote> _board = new();
    /// <summary>The middle price on the other servers of the player's region and faction, and how many servers had one.</summary>
    static Dictionary<string, (long Price, int Servers)> _region = new();

    /// <summary>How long an ask read on this PC stays the price before the board's takes over again.</summary>
    public static TimeSpan LocalFor { get; set; } = TimeSpan.FromHours(24);
    /// <summary>What a seller keeps of a sale: the Market takes a listing fee and a sales tax, about 12% together.</summary>
    public static double SellerKeeps { get; set; } = 0.88;
    /// <summary>Readings a price needs to count in totals and profit; an ask this many times the other servers' middle is dropped.</summary>
    public const int SureReadings = 2;
    public const double OutlierX = 3;
    public static DateTime? BoardAt { get; private set; }
    public static int BoardItems { get { lock (Lock) return _board.Count; } }
    /// <summary>Tests point this elsewhere so the player's own file is never touched.</summary>
    public static string? PathOverride { get; set; }

    public static bool Any { get { lock (Lock) return Local().Count > 0 || Typed().Count > 0 || _board.Count > 0; } }

    /// <summary>
    /// The price of one for totals, values and profit: the player's own, else the board's or the region's, and only when
    /// it is sure. An unsure price (one listing) gives null here; <see cref="QuoteOf"/> still shows it.
    /// </summary>
    public static long? Price(string name) => QuoteOf(name) is { Sure: true } q ? q.Price : null;

    /// <summary>The best price there is for an item, sure or not, with where it is from; null when there is none.</summary>
    public static Quote? QuoteOf(string name)
    {
        var k = Norm(name);
        lock (Lock)
        {
            // the player's own: the newer of what they typed and what this PC read (a read lasts a day, a typed price until replaced)
            Seen? read = Local().TryGetValue(k, out var s) && DateTime.UtcNow - s.at <= LocalFor ? s : null;
            Seen? typed = Typed().TryGetValue(k, out var t) ? t : null;
            if (typed != null && (read == null || typed.at >= read.at)) return new Quote(typed.price, From.Typed, 1, true, typed.at);
            if (read != null) return new Quote(read.price, From.Market, 1, true, read.at);
            var reg = _region.TryGetValue(k, out var r) && r.Servers >= SureReadings ? r : ((long Price, int Servers)?)null;
            if (_board.TryGetValue(k, out var b) && !(reg is { } rg && b.Price > OutlierX * rg.Price))
            {
                // a sure board price; an unsure one gives way to a sure region middle, else it is shown as unsure
                if (b.Sure || reg == null) return new Quote(b.Price, b.From, b.Readings, b.Sure, b.At);
            }
            if (reg is { } rr) return new Quote(rr.Price, From.Region, rr.Servers, true, null);
            if (_region.TryGetValue(k, out var one)) return new Quote(one.Price, From.Region, one.Servers, false, null);
            return null;
        }
    }

    /// <summary>Where an item's price comes from (what <see cref="QuoteOf"/> would give), or None.</summary>
    public static From SourceOf(string name) => QuoteOf(name)?.From ?? From.None;

    /// <summary>The board's line for an item, even when the player's own price is used, for a card's "Siel's board" row.</summary>
    public static BoardQuote? BoardOf(string name) { lock (Lock) return _board.TryGetValue(Norm(name), out var b) ? b : null; }

    /// <summary>The other servers' middle for an item, and how many servers.</summary>
    public static (long Price, int Servers)? RegionOf(string name) { lock (Lock) return _region.TryGetValue(Norm(name), out var r) ? r : null; }

    /// <summary>
    /// Where a price is from, its readings and age, in words: "your Market read · 2 min ago", "aion2.exchange · lowest of
    /// 42 listings · 40 min ago", "aion2.exchange · 1 listing · unsure", "other servers' middle · 7 servers".
    /// </summary>
    public static string Describe(Quote q, DateTime? now = null, bool withAge = true)
    {
        var parts = new List<string>();
        switch (q.From)
        {
            case From.Typed: parts.Add("you typed it"); break;
            case From.Market: parts.Add("your Market read"); break;
            case From.Server: parts.Add(q.Readings == 1 ? "a player's read" : $"players' reads · {q.Readings} asks"); break;
            case From.Sold: parts.Add($"sold average · {q.Readings} sold"); break;
            case From.Feed: parts.Add(q.Readings == 1 ? "aion2.exchange · 1 listing" : $"aion2.exchange · lowest of {q.Readings} listings"); break;
            case From.Region: parts.Add(q.Readings == 1 ? "another server's price" : $"other servers' middle · {q.Readings} servers"); break;
        }
        if (withAge && q.At is DateTime at) parts.Add(Ago(at, now ?? DateTime.UtcNow));
        if (!q.Sure) parts.Add("unsure");
        return string.Join(" · ", parts);
    }

    /// <summary>"just now", "12 min ago", "3 h ago", "2 days ago".</summary>
    public static string Ago(DateTime at, DateTime now)
    {
        var d = now - at;
        if (d.TotalMinutes < 1) return "just now";
        if (d.TotalHours < 1) return $"{(int)d.TotalMinutes} min ago";
        if (d.TotalDays < 1) return $"{(int)d.TotalHours} h ago";
        return (int)d.TotalDays == 1 ? "1 day ago" : $"{(int)d.TotalDays} days ago";
    }

    /// <summary>
    /// The other servers' boards (same region and faction): per item, the middle of their prices and how many servers.
    /// Used where the server's own board has no price or an outlier, and marked ≈.
    /// </summary>
    public static void SetRegionBoards(IEnumerable<Dictionary<string, long>> boards)
    {
        var all = new Dictionary<string, List<long>>();
        foreach (var b in boards)
            foreach (var (name, p) in b)
                if (p > 0) { var k = Norm(name); if (!all.TryGetValue(k, out var l)) all[k] = l = new(); l.Add(p); }
        // the lower middle for an even count, so one joke ask never lifts it (as the site's regionMiddles)
        var med = all.ToDictionary(kv => kv.Key, kv => { var l = kv.Value; l.Sort(); return (l[(l.Count - 1) / 2], l.Count); });
        lock (Lock) _region = med;
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
            Save(local, LocalPath);
        }
    }

    /// <summary>A price the player typed on a card ("what I see in the game"): used from now on, until a newer read or another typed price.</summary>
    public static void SetTyped(string name, long price, DateTime at)
    {
        var k = Norm(name);
        lock (Lock)
        {
            var d = Typed();
            if (price > 0) d[k] = new Seen(price, at); else d.Remove(k);
            Save(d, TypedPath);
        }
    }

    public static long? TypedPrice(string name) { lock (Lock) return Typed().TryGetValue(Norm(name), out var t) ? t.price : null; }

    /// <summary>
    /// What the player pays an NPC for a vendor material (a "(Bound)" one, never on the Market), typed in the crafting
    /// breakdown and kept on this PC in vendor-prices.json, so recipes that need it get a full cost. With nothing typed,
    /// the NPC price shipped in data/vendor-prices.json is used; a null there is no price.
    /// </summary>
    static Dictionary<string, long>? _vendor;
    static Dictionary<string, long> Vendor()
    {
        if (_vendor != null) return _vendor;
        _vendor = new();
        try
        {
            var path = Path.Combine(Dir, "vendor-prices.json");
            if (File.Exists(path)) foreach (var (k, v) in JsonSerializer.Deserialize<Dictionary<string, long>>(File.ReadAllText(path)) ?? new()) _vendor[Norm(k)] = v;
        }
        catch (Exception ex) { CrashLog.Write("vendor prices", ex); }
        return _vendor;
    }

    public static long? VendorPrice(string name) => TypedVendorPrice(name) ?? ShippedVendorPrice(name);

    /// <summary>The NPC cost the player typed for a vendor material, or null.</summary>
    public static long? TypedVendorPrice(string name) { lock (Lock) return Vendor().TryGetValue(Norm(name), out var v) && v > 0 ? v : null; }

    /// <summary>The NPC price shipped in data/vendor-prices.json (updated like the other data files), or null when it is unknown.</summary>
    public static long? ShippedVendorPrice(string name) { lock (Lock) return Shipped().TryGetValue(Norm(name), out var v) && v is > 0 ? v : null; }

    static Dictionary<string, long?>? _shipped;
    static Dictionary<string, long?> Shipped()
    {
        if (_shipped != null) return _shipped;
        _shipped = new();
        try
        {
            using var s = DataFiles.Open("vendor-prices.json");
            if (s == null) return _shipped;
            using var doc = JsonDocument.Parse(s);
            if (!doc.RootElement.TryGetProperty("prices", out var p) || p.ValueKind != JsonValueKind.Object) return _shipped;
            foreach (var kv in p.EnumerateObject())
                _shipped[Norm(kv.Name)] = kv.Value.ValueKind == JsonValueKind.Number && kv.Value.TryGetInt64(out var v) && v > 0 ? (long?)v : null;
        }
        catch (Exception ex) { CrashLog.Write("shipped vendor prices", ex); }
        return _shipped;
    }

    /// <summary>Tests: shipped NPC prices in place of the file's; null reads the file again.</summary>
    public static void SetShippedVendor(Dictionary<string, long?>? prices)
    {
        lock (Lock) _shipped = prices?.ToDictionary(kv => Norm(kv.Key), kv => kv.Value);
    }

    /// <summary>
    /// Materials the player ticked "I have these" for (farmed, gathered or already owned): they cost nothing in cash, so a
    /// recipe's cost leaves them out. Kept on this PC in farmed-materials.json.
    /// </summary>
    static HashSet<string>? _farmed;
    static HashSet<string> FarmedSet()
    {
        if (_farmed != null) return _farmed;
        _farmed = new();
        try
        {
            var path = Path.Combine(Dir, "farmed-materials.json");
            if (File.Exists(path)) foreach (var n in JsonSerializer.Deserialize<List<string>>(File.ReadAllText(path)) ?? new()) _farmed.Add(Norm(n));
        }
        catch (Exception ex) { CrashLog.Write("farmed materials", ex); }
        return _farmed;
    }

    public static bool Farmed(string name) { lock (Lock) return FarmedSet().Contains(Norm(name)); }

    /// <summary>Ticks (or unticks) "I have these" for a material.</summary>
    public static void SetFarmed(string name, bool on)
    {
        lock (Lock)
        {
            var d = FarmedSet();
            if (on) d.Add(Norm(name)); else d.Remove(Norm(name));
            try { File.WriteAllText(Path.Combine(Dir, "farmed-materials.json"), JsonSerializer.Serialize(d.OrderBy(x => x, StringComparer.Ordinal).ToList())); }
            catch (Exception ex) { CrashLog.Write("farmed materials", ex); }
        }
    }

    /// <summary>Sets (or, with 0, clears) what the NPC charges for one.</summary>
    public static void SetVendorPrice(string name, long price)
    {
        lock (Lock)
        {
            var d = Vendor();
            if (price > 0) d[Norm(name)] = price; else d.Remove(Norm(name));
            try { File.WriteAllText(Path.Combine(Dir, "vendor-prices.json"), JsonSerializer.Serialize(d)); }
            catch (Exception ex) { CrashLog.Write("vendor prices", ex); }
        }
    }

    /// <summary>A board of plain prices (tests and layout renders): each counts as a sure board price.</summary>
    public static void SetBoard(Dictionary<string, long> prices) =>
        SetBoardQuotes(prices.Where(kv => kv.Value > 0).ToDictionary(kv => kv.Key, kv => new BoardQuote(kv.Value, From.Server, SureReadings, true, null)));

    /// <summary>The community board for the player's server, replacing the last one.</summary>
    public static void SetBoardQuotes(Dictionary<string, BoardQuote> quotes)
    {
        lock (Lock) { _board = quotes.Where(kv => kv.Value.Price > 0).ToDictionary(kv => Norm(kv.Key), kv => kv.Value); BoardAt = DateTime.UtcNow; }
    }

    /// <summary>
    /// Reads a /prices/&lt;server&gt;.json board. A board with the site's price pick gives each item its picked price,
    /// source, readings, age and the other servers' middle. An older board: the lowest ask while it is fresh, else the
    /// 24-hour, 7-day and sold medians, with its listings as the readings. Items keyed by ID are named through the dictionary.
    /// </summary>
    public static Dictionary<string, BoardQuote> ParseQuotes(string json, ItemDictionary? items)
    {
        var d = new Dictionary<string, BoardQuote>();
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("items", out var arr) || arr.ValueKind != JsonValueKind.Array) return d;
        foreach (var e in arr.EnumerateArray())
        {
            if (!e.TryGetProperty("item", out var it) || it.ValueKind != JsonValueKind.String) continue;
            var name = it.GetString() ?? "";
            if (items != null && long.TryParse(name, out var id)) name = items.NameOf(id);
            if (name.Length == 0) continue;
            long? region = Num(e, "region_price"); int regionN = (int)(Num(e, "region_servers") ?? 0);
            if (e.TryGetProperty("price_from", out var pf))
            {
                // the site's pick (price-pick.ts); a null price means every ask was an outlier
                if (Num(e, "price") is not long p || p <= 0) continue;
                var from = pf.ValueKind == JsonValueKind.String ? pf.GetString() switch { "player" => From.Server, "sold" => From.Sold, _ => From.Feed } : From.Feed;
                bool sure = e.TryGetProperty("sure", out var su) && su.ValueKind == JsonValueKind.True;
                d[name] = new BoardQuote(p, from, (int)(Num(e, "readings") ?? 0), sure, Time(e, "price_seen_at"), region, regionN);
                continue;
            }
            bool stale = e.TryGetProperty("stale", out var st) && st.ValueKind == JsonValueKind.True;
            int nObs = (int)(Num(e, "n_obs") ?? 0);
            // one listing that jumped over tenfold in a day is a joke ask, not a price (2026-10-22: an Ethereal Longsword at
            // 10,000,011 from a single observation, 5,000× its day before; it sells for about 2,000): left out
            if (nObs <= 1 && e.TryGetProperty("change_24h", out var ch) && ch.ValueKind == JsonValueKind.Number && ch.GetDouble() >= 10) continue;
            long? fresh = stale ? null : Num(e, "lowest_ask");
            long? v = fresh ?? Num(e, "median_24h") ?? Num(e, "median_7d") ?? Num(e, "sold_median_7d") ?? Num(e, "ref_price");
            if (v is not long price || price <= 0) continue;
            bool player = e.TryGetProperty("lowest_from", out var lf) && lf.ValueKind == JsonValueKind.String && lf.GetString() == "player";
            d[name] = new BoardQuote(price, player ? From.Server : From.Feed, nObs, nObs >= SureReadings, fresh != null ? Time(e, "lowest_seen_at") : null, region, regionN);
        }
        return d;
    }

    /// <summary>A board's prices by name, sure or not: what the other servers' boards give the region middle (a lower middle, so one listing can't lift it).</summary>
    public static Dictionary<string, long> ParseBoard(string json, ItemDictionary? items) =>
        ParseQuotes(json, items).ToDictionary(kv => kv.Key, kv => kv.Value.Price);

    static long? Num(JsonElement e, string prop) => e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var x) ? (long)Math.Round(x) : null;
    static DateTime? Time(JsonElement e, string prop) => e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String && v.TryGetDateTime(out var t) ? t.ToUniversalTime() : null;

    static string Dir => Path.GetDirectoryName(PathOverride ?? Path.Combine(CrashLog.Dir, "prices.json"))!;
    static string LocalPath => PathOverride ?? Path.Combine(CrashLog.Dir, "prices.json");
    static string TypedPath => Path.Combine(Dir, "typed-prices.json");

    /// <summary>The prices.json file: { "updated": "…", "prices": { "Orichalcum Ore": 1950 }, "seen": { "Orichalcum Ore": "…" } }.</summary>
    static Dictionary<string, Seen> Local() => _local ??= Load(LocalPath, "prices");
    /// <summary>typed-prices.json, the same shape: the prices the player typed on cards.</summary>
    static Dictionary<string, Seen> Typed() => _typed ??= Load(TypedPath, "typed prices");

    static Dictionary<string, Seen> Load(string path, string what)
    {
        var d = new Dictionary<string, Seen>();
        try
        {
            if (!File.Exists(path)) return d;
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (!doc.RootElement.TryGetProperty("prices", out var p)) return d;
            doc.RootElement.TryGetProperty("seen", out var seen);
            foreach (var kv in p.EnumerateObject())
            {
                if (!kv.Value.TryGetInt64(out var v)) continue;
                var at = seen.ValueKind == JsonValueKind.Object && seen.TryGetProperty(kv.Name, out var a) && a.TryGetDateTime(out var t) ? t.ToUniversalTime() : DateTime.MinValue;
                d[Norm(kv.Name)] = new Seen(v, at);
            }
        }
        catch (Exception ex) { CrashLog.Write(what, ex); }
        return d;
    }

    static void Save(Dictionary<string, Seen> d, string path)
    {
        try
        {
            var doc = new
            {
                updated = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"),
                prices = d.ToDictionary(kv => kv.Key, kv => kv.Value.price),
                seen = d.ToDictionary(kv => kv.Key, kv => kv.Value.at.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'")),
            };
            File.WriteAllText(path, JsonSerializer.Serialize(doc));
        }
        catch (Exception ex) { CrashLog.Write("prices", ex); }
    }

    /// <summary>Forgets what was loaded from files, for tests that switch folders.</summary>
    public static void Reset() { lock (Lock) { _local = null; _typed = null; _vendor = null; _farmed = null; } }

    static string Norm(string s) => string.Join(' ', s.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
}
