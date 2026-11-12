using System.Text.Json;

namespace KinahLedger.Data;

/// <summary>
/// Market prices by item name, so drops, gathered materials and crafts can be valued in Kinah. Every price is a quote
/// that says where it is from, how many readings stand behind it and how old it is (Steven, 2026-10-23: "prices are
/// super inaccurate"). Nearest first:
///   1. yours: a price the player typed on a card (typed-prices.json) or the lowest ask this PC read on the Market in
///      the last day (prices.json), whichever is newer. Read with sharing off too; nothing leaves the PC then.
///   2. the server's board on kinahledger.com, as the site picked it: players' reads, what it sold for, or the
///      aion2.exchange listing, with its readings and age, fetched every 15 minutes.
/// Only the player's own server prices anything (Steven, 2026-11-09: "they should be based on ur charcters server in
/// real time"). The other servers' middle is shown on the item card for reference, never used as the price: on
/// 2026-11-08 Siel had no Lesser Manastone listed and the other servers' middle (80,000, more than the Intermediate
/// Manastone five of them make) set the craft's cost. A price with fewer than 2 readings, or last seen over a day ago,
/// is "unsure": it is marked (~, dimmed) but it counts. Every item gets some price (Steven, 2026-11-09: "everything need
/// SOME type of price", after Siel's Inina, one listing at 5,550, added nothing to a gathering session): the server's own
/// price first, sure or not; then the asks the site set aside; only when the server has nothing at all, the other
/// servers' middle, marked ≈.
/// </summary>
public static class PriceBook
{
    sealed record Seen(long price, DateTime at);

    /// <summary>Where a price is from. Market and Typed are the player's own.</summary>
    public enum From { None, Market, Server, Region, Typed, Sold, Feed }

    /// <summary>One price as shown: the figure, where it is from, how many readings back it, whether it counts, when it was seen.</summary>
    /// <param name="Note">Said first by <see cref="Describe"/> when something is worth telling: a typed price that lost to the board.</param>
    public sealed record Quote(long Price, From From, int Readings, bool Sure, DateTime? At, string? Note = null)
    {
        public bool Mine => From is From.Market or From.Typed;
    }

    /// <summary>A board item as the site sends it (price-pick.ts on the site), plus the other servers' middle when it has one.</summary>
    /// <param name="Picked">The site's pick (price-pick.ts), already judged against the item's own level and the other servers: the tracker never second-guesses it.</param>
    public sealed record BoardQuote(long Price, From From, int Readings, bool Sure, DateTime? At, long? Region = null, int RegionServers = 0, bool Picked = false);

    static readonly object Lock = new();
    static Dictionary<string, Seen>? _local, _typed;
    static Dictionary<string, BoardQuote> _board = new();
    /// <summary>The middle price on the other servers of the player's region and faction, and how many servers had one.</summary>
    static Dictionary<string, (long Price, int Servers)> _region = new();

    /// <summary>How long an ask read on this PC stays the price before the board's takes over again.</summary>
    public static TimeSpan LocalFor { get; set; } = TimeSpan.FromHours(24);
    /// <summary>The share of a sale the Market takes as its listing fee and sales tax together (the 12% the window names).</summary>
    public const double FeeRate = 0.12;
    /// <summary>Options › General › Show amounts before the Market fee: every figure the window works out is then the sale's gross.</summary>
    public static bool ShowGross { get; set; }
    /// <summary>What a seller keeps of a sale: 88%, or all of it while <see cref="ShowGross"/> is on.</summary>
    public static double SellerKeeps => ShowGross ? 1 : 1 - FeeRate;
    /// <summary>The full wording for a figure: "after the 12% Market fee" (the HUD card, the first tooltips), or "before the Market fee".</summary>
    public static string Fee => ShowGross ? "before the Market fee" : $"after the {FeeRate:P0} Market fee";
    /// <summary>The short wording, where space is tight: "after the Market's cut", or "before the Market's cut".</summary>
    public static string Cut => ShowGross ? "before the Market's cut" : "after the Market's cut";
    /// <summary>Readings a price needs to count in totals and profit; an ask this many times the other servers' middle is dropped.</summary>
    public const int SureReadings = 2;
    public const double OutlierX = 3;
    /// <summary>A board price last seen longer ago than this is unsure (2026-11-03: a price weeks old stood as sure; 2026-11-09:
    /// a day, so a price is the server's market as it is now); over <see cref="OldAfter"/> it is shown dimmed as old.</summary>
    public static readonly TimeSpan BoardMaxAge = TimeSpan.FromHours(24);
    public static readonly TimeSpan OldAfter = TimeSpan.FromHours(24);
    /// <summary>A typed price older than this gives way to a sure board price (2026-11-03: a typed price had no age limit).</summary>
    public static readonly TimeSpan TypedFor = TimeSpan.FromDays(7);
    public static DateTime? BoardAt { get; private set; }
    static ItemDictionary? _items;
    static bool _purgeLogged;
    /// <summary>
    /// The item list a Market read is checked against (set when the Market reader starts). With none, reads are not checked
    /// (tests). Setting it also drops the stored reads that are not items (2026-11-03: 87 keys, "dawn legion", "t the party").
    /// </summary>
    public static ItemDictionary? Items
    {
        get => _items;
        set { lock (Lock) { _items = value; if (_local != null) PurgeUnknown(_local); } }
    }
    public static int BoardItems { get { lock (Lock) return _board.Count; } }
    /// <summary>Tests point this elsewhere so the player's own file is never touched.</summary>
    public static string? PathOverride { get; set; }

    public static bool Any { get { lock (Lock) return Local().Count > 0 || Typed().Count > 0 || _board.Count > 0; } }

    /// <summary>
    /// The price of one for totals, values and profit: the best there is (<see cref="QuoteOf"/>), sure or not. An unsure
    /// one counts; the window marks it. Null only when no server has the item at all.
    /// </summary>
    public static long? Price(string name) => QuoteOf(name) is { Price: > 0 } q ? q.Price : null;

    /// <summary>
    /// What a bought material costs a craft: <see cref="Price"/>, but never the other servers' middle. A thin price on the
    /// server counts (the card flags it); with none on the server the material is unpriced and the cost a floor (2026-11-08:
    /// with no Lesser Manastone on Siel, the other servers' 80,000 made an Intermediate Manastone craft cost 400K).
    /// </summary>
    public static long? CostPrice(string name) => QuoteOf(name) is { Price: > 0, From: not From.Region } q ? q.Price : null;

    /// <summary>The best price there is for an item, sure or not, with where it is from; null when there is none.</summary>
    public static Quote? QuoteOf(string name)
    {
        var k = Norm(name);
        lock (Lock)
        {
            // the player's own: the newer of what they typed and what this PC read (a read lasts a day, a typed price until replaced)
            Seen? read = Local().TryGetValue(k, out var s) && DateTime.UtcNow - s.at <= LocalFor ? s : null;
            Seen? typed = Typed().TryGetValue(k, out var t) ? t : null;
            if (typed != null && (read == null || typed.at >= read.at))
            {
                var typedQuote = new Quote(typed.price, From.Typed, 1, true, typed.at);
                // a week-old typed price gives way to a sure board price; with none, it stands
                if (DateTime.UtcNow - typed.at <= TypedFor || SharedQuote(k) is not { Sure: true, From: From.Server or From.Sold or From.Feed } board) return typedQuote;
                return board with { Note = $"you typed {typed.price:N0} on {typed.at.ToLocalTime():yyyy-MM-dd}; the board says {board.Price:N0}" };
            }
            if (read != null) return new Quote(read.price, From.Market, 1, true, read.at);
            return SharedQuote(k);
        }
    }

    /// <summary>The best price that is not the player's own: the server's board, sure or not, else the other servers' middle
    /// (unsure, ≈); null when neither has the item. Call with the lock held.</summary>
    static Quote? SharedQuote(string k)
    {
        Quote? region = _region.TryGetValue(k, out var g) && g.Price > 0 ? new Quote(g.Price, From.Region, g.Servers, false, null) : null;
        if (!_board.TryGetValue(k, out var b)) return region;
        // 2026-11-03: Siel's Artisan's Steamed Cypri at 44,994 (57 listings) was thrown out as 3x a 14,000 region middle
        // that two lone asks had pulled down, and the card said 12.3K. The site's pick was already judged against the
        // region with every listing in hand (price-pick.ts): it stands. The 3x rule is for an older board's lowest ask,
        // which is then no price at all: the other servers' middle never stands in for the server's own.
        var reg = _region.TryGetValue(k, out var r) && r.Servers >= SureReadings ? r : ((long Price, int Servers)?)null;
        if (reg is { } rg && !b.Picked && b.Price > OutlierX * rg.Price) return region;
        // one last seen over a day ago is unsure however many readings it had
        bool sure = b.Sure && !(b.At is DateTime seenAt && DateTime.UtcNow - seenAt > BoardMaxAge);
        return new Quote(b.Price, b.From, b.Readings, sure, b.At);
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
        if (q.Note != null) parts.Add(q.Note);
        switch (q.From)
        {
            case From.Typed: parts.Add("you typed it"); break;
            case From.Market: parts.Add("your Market read"); break;
            case From.Server: parts.Add(q.Readings == 1 ? "a player's read" : $"players' reads · {q.Readings} asks"); break;
            case From.Sold: parts.Add($"sold average · {q.Readings} sold"); break;
            case From.Feed: parts.Add(q.Readings == 1 ? "aion2.exchange · 1 listing" : $"aion2.exchange · lowest of {q.Readings} listings"); break;
            case From.Region: parts.Add(q.Readings == 1 ? "another server's price" : $"other servers' middle · {q.Readings} servers"); break;
        }
        if (withAge)
        {
            // a board price the site gave no time for (a sold average, an older board) is as old as the last check of the board
            if (q.At is DateTime at) parts.Add(Ago(at, now ?? DateTime.UtcNow));
            else if (q.From is From.Server or From.Sold or From.Feed && BoardAt is DateTime bat) parts.Add("board checked " + Ago(bat, now ?? DateTime.UtcNow));
        }
        if (!q.Sure) parts.Add("unsure");
        return string.Join(" · ", parts);
    }

    /// <summary>True when a price was last seen over a day ago (<see cref="OldAfter"/>); one with no time of its own is not judged.</summary>
    public static bool IsOld(Quote q, DateTime? now = null) => q.At is DateTime at && (now ?? DateTime.UtcNow) - at > OldAfter;

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
    /// Shown on the item card for reference and used to spot an older board's joke ask; never the price itself.
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

    /// <summary>
    /// Whether a unit price sits within <paramref name="factor"/>x either side of the board's (or the other servers') price
    /// for the item. False when there is no price to judge it by: an item's listings, named from the line above them, are
    /// shared only when they agree with what is already known (MarketReader.ListingAsks).
    /// </summary>
    public static bool NearKnown(string name, long unitPrice, double factor)
    {
        if (unitPrice <= 0) return false;
        var k = Norm(name);
        lock (Lock)
        {
            long? rp = _board.TryGetValue(k, out var bq) && bq.Price > 0 ? bq.Price : _region.TryGetValue(k, out var rq) && rq.Price > 0 ? rq.Price : null;
            return rp is long r && unitPrice * factor >= r && unitPrice <= factor * r;
        }
    }

    /// <summary>An ask read on the Market: kept when it is the lowest of the last day for that item.</summary>
    public static void Observe(string name, long unitPrice, DateTime at)
    {
        if (unitPrice <= 0) return;
        var k = Norm(name);
        lock (Lock)
        {
            if (_items != null && _items.Find(name) == null)
            {
                CrashLog.Note($"Market read refused: \"{name}\" at {unitPrice:N0} is not an item in the list.");
                return;
            }
            long? refPrice = _board.TryGetValue(k, out var bq) ? bq.Price : _region.TryGetValue(k, out var rq) ? rq.Price : null;
            if (refPrice is long rp && rp > 0 && (unitPrice * 10 < rp || unitPrice > OutlierX * rp))
            {
                CrashLog.Note($"Market read refused: {name} at {unitPrice:N0} is out of range of {rp:N0} on the board.");
                return;
            }
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

    /// <summary>
    /// "I have these": ticked, or held bound (<see cref="BoundHeld"/>). Either way the material costs nothing in a craft.
    /// </summary>
    public static bool Farmed(string name) { lock (Lock) { var k = Norm(name); return FarmedSet().Contains(k) || BoundSet().Contains(Norm(Unbound(name))); } }

    /// <summary>Ticks (or unticks) "I have these" for a material. Unticking also lets go of the bound copies it was held for.</summary>
    public static void SetFarmed(string name, bool on)
    {
        lock (Lock)
        {
            var d = FarmedSet();
            if (on) d.Add(Norm(name)); else d.Remove(Norm(name));
            try { File.WriteAllText(Path.Combine(Dir, "farmed-materials.json"), JsonSerializer.Serialize(d.OrderBy(x => x, StringComparer.Ordinal).ToList())); }
            catch (Exception ex) { CrashLog.Write("farmed materials", ex); }
            if (!on && BoundSet().Remove(Norm(Unbound(name)))) SaveBound();
        }
    }

    /// <summary>
    /// Materials the player has picked up bound copies of (2026-11-12, Steven: his bound Lesser Manastones "cant even be sold",
    /// and the Crafting part said he lost 500K a craft making Manastones by charging them at the Market's price). A bound copy
    /// has no Market value, so a material held bound costs nothing in a craft (<see cref="Farmed"/>), and what a craft makes
    /// from it is bound as well (RecipeBook.BoundCraft). The game's crafting window doesn't say which copies a craft used, so
    /// having picked up bound ones is the sign. Kept on this PC in bound-held.json; unticking "I have these" lets one go.
    /// </summary>
    static HashSet<string>? _boundHeld;
    static HashSet<string> BoundSet()
    {
        if (_boundHeld != null) return _boundHeld;
        _boundHeld = new();
        try
        {
            var path = Path.Combine(Dir, "bound-held.json");
            if (File.Exists(path)) foreach (var n in JsonSerializer.Deserialize<List<string>>(File.ReadAllText(path)) ?? new()) _boundHeld.Add(Norm(n));
        }
        catch (Exception ex) { CrashLog.Write("bound held", ex); }
        return _boundHeld;
    }

    static void SaveBound()
    {
        try { File.WriteAllText(Path.Combine(Dir, "bound-held.json"), JsonSerializer.Serialize(BoundSet().OrderBy(x => x, StringComparer.Ordinal).ToList())); }
        catch (Exception ex) { CrashLog.Write("bound held", ex); }
    }

    static string Unbound(string name) => System.Text.RegularExpressions.Regex.Replace(name, @"\s*\(bound\)\s*$", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();

    /// <summary>Whether the player has picked up bound copies of this material.</summary>
    public static bool BoundHeld(string name) { lock (Lock) return BoundSet().Contains(Norm(Unbound(name))); }

    /// <summary>Bound copies picked up (their names, with or without "(Bound)"): kept from now on. Returns the ones new to the list.</summary>
    public static List<string> NoteBoundHeld(IEnumerable<string> names)
    {
        var added = new List<string>();
        lock (Lock)
        {
            var d = BoundSet();
            foreach (var n in names) { var k = Norm(Unbound(n)); if (k.Length > 0 && d.Add(k)) added.Add(Unbound(n)); }
            if (added.Count > 0) SaveBound();
        }
        return added;
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
                // the site's pick (price-pick.ts); a null price means it set every ask aside (against the other servers, or as
                // too few). They are still the server's market as it is, so the lowest of them stands, unsure (2026-11-09:
                // Siel's Inina Pasta, 2 asks at 8,500 against a 2,221 middle on 3 servers, had no price at all)
                if (Num(e, "price") is not long p || p <= 0)
                {
                    if (e.TryGetProperty("dropped", out var dr) && dr.ValueKind == JsonValueKind.Object && Num(dr, "price") is long dp && dp > 0)
                        d[name] = new BoardQuote(dp, From.Feed, (int)(Num(e, "listings") ?? 0), false, Time(e, "lowest_seen_at"), region, regionN, Picked: true);
                    continue;
                }
                var from = pf.ValueKind == JsonValueKind.String ? pf.GetString() switch { "player" => From.Server, "sold" => From.Sold, _ => From.Feed } : From.Feed;
                bool sure = e.TryGetProperty("sure", out var su) && su.ValueKind == JsonValueKind.True;
                d[name] = new BoardQuote(p, from, (int)(Num(e, "readings") ?? 0), sure, Time(e, "price_seen_at"), region, regionN, Picked: true);
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

    /// <summary>
    /// A board's prices by name. With <paramref name="sureOnly"/>, only the sure ones: what the other servers' boards give the
    /// region middle (a lower middle, so one listing can't lift it; and no lone ask in it, so one can't sink it either).
    /// </summary>
    public static Dictionary<string, long> ParseBoard(string json, ItemDictionary? items, bool sureOnly = false) =>
        ParseQuotes(json, items).Where(kv => !sureOnly || kv.Value.Sure).ToDictionary(kv => kv.Key, kv => kv.Value.Price);

    static long? Num(JsonElement e, string prop) => e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var x) ? (long)Math.Round(x) : null;
    static DateTime? Time(JsonElement e, string prop) => e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String && v.TryGetDateTime(out var t) ? t.ToUniversalTime() : null;

    static string Dir => Path.GetDirectoryName(PathOverride ?? Path.Combine(CrashLog.Dir, "prices.json"))!;
    static string LocalPath => PathOverride ?? Path.Combine(CrashLog.Dir, "prices.json");
    static string TypedPath => Path.Combine(Dir, "typed-prices.json");

    /// <summary>The prices.json file: { "updated": "…", "prices": { "Orichalcum Ore": 1950 }, "seen": { "Orichalcum Ore": "…" } }.</summary>
    static Dictionary<string, Seen> Local()
    {
        if (_local != null) return _local;
        _local = Load(LocalPath, "prices");
        PurgeUnknown(_local);
        return _local;
    }

    /// <summary>Drops stored reads whose name is not an item in the list and saves; logged once with the count.</summary>
    static void PurgeUnknown(Dictionary<string, Seen> d)
    {
        if (_items == null) return;
        var bad = d.Keys.Where(k => _items.Find(k) == null).ToList();
        if (bad.Count == 0) return;
        foreach (var k in bad) d.Remove(k);
        if (!_purgeLogged) { _purgeLogged = true; CrashLog.Note($"Prices: dropped {bad.Count} stored Market reads that are not items."); }
        Save(d, LocalPath);
    }
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
    public static void Reset() { lock (Lock) { _local = null; _typed = null; _vendor = null; _farmed = null; _boundHeld = null; } }

    static string Norm(string s) => string.Join(' ', s.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
}
