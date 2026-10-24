using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using KinahLedger.Data;
using KinahLedger.Session;

namespace KinahLedger.Upload;

/// <summary>A random ID per install, used only so the site can drop duplicate or spam uploads. `reset-id` replaces it.</summary>
public static class InstallId
{
    static string PathFor()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KinahLedger");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "install.id");
    }

    public static string Get()
    {
        var p = PathFor();
        if (File.Exists(p)) { var v = File.ReadAllText(p).Trim(); if (v.Length >= 16) return v; }
        var id = Guid.NewGuid().ToString("N");
        File.WriteAllText(p, id);
        return id;
    }

    public static void Reset() { var p = PathFor(); if (File.Exists(p)) File.Delete(p); }

    /// <summary>
    /// A short form of the install ID a player can paste into their profile on the site, so this PC's runs show up there.
    /// Optional: without it runs stay anonymous. It says nothing about who the player is.
    /// </summary>
    public static string LinkCode() { var id = Get(); return $"KL-{id.Substring(0, 4).ToUpperInvariant()}-{id.Substring(4, 2).ToUpperInvariant()}"; }
}

/// <summary>
/// Exactly what leaves the PC when a run is shared: the server it was played on (as chosen in the window, so the site
/// can keep drops per server) and the totals the public tables need. No character name, account, class, level,
/// chat text or screenshots. Since 0.7: the change in the Kinah balance over the run (rounded, never the balance
/// itself) and craft and enchant outcome counts. Since 0.7.2: the expedition and event runs that finished meanwhile
/// (area, length, Kinah, kills, tradeable items), each sent once.
/// </summary>
public sealed record RunPayload(
    string app,
    string region,
    string server_key,
    string activity,
    string kind,
    string? zone,
    string gear,
    int minutes,
    int kills,
    int nodes,
    long kinah,
    long kinah_bound,
    Dictionary<string, int> items,
    Dictionary<string, int> items_bound,
    Dictionary<string, int> pets,
    Dictionary<string, ZoneRun> zones,
    string install,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? kinah_net = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] OddsPayload? odds = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] List<MobRun>? mobs = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] List<InstanceRunPayload>? instance_runs = null,
    // since 0.8.12: max(K, G) / (K + G) over the distinct minutes with a kill (K) and with a gather (G), rounded to 0.05;
    // the site leaves runs under 0.8 out of its per-hour figures (farming and gathering at once). Absent with neither
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] double? focus = null)
{
    /// <summary>The gear the runs are shared under: "gs:1400-1599" from the player's gear score (Options), "unknown" until it is set (Data.GearScore).</summary>
    public static string Gear { get; set; } = "unknown";

    public static string AppVersion => typeof(RunPayload).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "0.0.0";

    /// <summary>Most keys per odds map, as the site accepts them; the most-tried recipes and steps are kept.</summary>
    public const int MaxOddsKeys = 100;
    /// <summary>Most enemies per run, as the site accepts them; the ones that paid the most Kinah are kept.</summary>
    public const int MaxMobs = 60;

    /// <param name="region">The chosen server's region key ("na_e"); the settings' region when none was chosen.</param>
    /// <param name="server">The chosen server's label ("Siel (Elyos)"); empty files the run under the region alone.</param>
    /// <param name="instances">Expedition and event runs that finished while this run or chunk counted (InstanceLog.TakeUnshared).</param>
    public static RunPayload Build(SessionEngine s, Settings cfg, string kind, string activity, string region, string server, ItemDictionary? dict = null, IEnumerable<InstanceRun>? instances = null)
    {
        var zones = Shareable(s.ZoneSnapshot());
        var main = zones.Where(kv => kv.Key != SessionEngine.UnknownZone).OrderByDescending(kv => kv.Value.Seconds).Select(kv => kv.Key).FirstOrDefault();
        long? net = s.NetKinah is long n && s.Elapsed.TotalMinutes >= 5 ? RoundNet(n) : null;
        string Key(long id) => dict?.KeyOf(id) ?? id.ToString();
        // a drop the player flagged "Something looks wrong" never leaves this PC, in any list
        bool Off(long id) => KinahLedger.Session.ReportedDrops.Has(s.ItemName(id));
        int Flagged(SessionEngine.ZoneStats z) => z.Items.Where(x => Off(x.Key)).Sum(x => x.Value) + z.ItemsBound.Where(x => Off(x.Key)).Sum(x => x.Value);
        return new(
            AppVersion,
            region,
            ServerKeys.Build(region, server),
            activity,
            kind,
            main,
            Gear,
            // farming minutes, not wall minutes: time within a minute of a loot, kill or gather line. The site's per-hour
            // rates divide by this; drop rates per kill and per attempt don't depend on it. At least one minute once anything happened.
            Math.Max(s.Events > 0 ? 1 : 0, (int)Math.Round(s.Active.TotalMinutes)),
            s.Kills,
            s.Nodes,
            s.Kinah,
            s.KinahBound,
            Merge(s.Items, Key, Off),
            Merge(s.ItemsBound, Key, Off),
            s.PetSnapshot(),
            // each zone carries its own items, so the site files them under the zone they dropped in, never all under the main one
            zones.Where(kv => kv.Value.Active >= 30 || kv.Value.Kills > 0 || kv.Value.Pets > 0 || kv.Value.Items.Count > 0 || kv.Value.ItemsBound.Count > 0)
                 .ToDictionary(kv => kv.Key, kv => new ZoneRun((int)Math.Round(kv.Value.Active / 60), kv.Value.Kills, kv.Value.Nodes, kv.Value.Kinah, Math.Max(0, kv.Value.Drops - Flagged(kv.Value)), kv.Value.Pets, kv.Value.PetsByName, Merge(kv.Value.Items, Key, Off), Merge(kv.Value.ItemsBound, Key, Off))),
            InstallId.Get(),
            net,
            OddsPayload.From(s),
            MobRun.From(s, Key, Off),
            InstanceRunPayload.From(instances, Key, Off),
            s.Focus);
    }

    /// <summary>The net is rounded to the nearest 100 so the figure says nothing exact about the player's purse.</summary>
    public static long RoundNet(long n) => (long)Math.Round(n / 100.0, MidpointRounding.AwayFromZero) * 100;

    /// <summary>Counts keyed the way the site reads them; two synthetic IDs that carry the same name add up.</summary>
    static Dictionary<string, int> Merge(Dictionary<long, int> counts, Func<long, string> key, Func<long, bool>? off = null)
    {
        var d = new Dictionary<string, int>();
        foreach (var (id, n) in counts) { if (off?.Invoke(id) == true) continue; var k = key(id); d[k] = d.GetValueOrDefault(k) + n; }
        return d;
    }

    /// <summary>How an item is keyed in what is shared: its real id, or its name when the name wasn't in the item list (the site resolves either).</summary>
    public static string ItemKey(SessionEngine s, long id) => id > 0 ? id.ToString() : s.ItemName(id);

    static Dictionary<string, int> Keyed(SessionEngine s, Dictionary<long, int> counts)
    {
        var outp = new Dictionary<string, int>();
        foreach (var (id, c) in counts) { var k = ItemKey(s, id); outp[k] = outp.GetValueOrDefault(k) + c; }
        return outp;
    }

    /// <summary>
    /// What leaves this PC names only zones the tracker is sure of: a known name or one with a place word. A label it
    /// read off the minimap title but couldn't verify stays in the window here and is shared as "Unknown zone", so a
    /// stray caption can't become a spot on the site.
    /// </summary>
    public static Dictionary<string, SessionEngine.ZoneStats> Shareable(Dictionary<string, SessionEngine.ZoneStats> zones)
    {
        var outp = new Dictionary<string, SessionEngine.ZoneStats>();
        foreach (var (key, v) in zones)
        {
            var k = key == SessionEngine.UnknownZone || KinahLedger.Ocr.ZoneLocator.IsVerified(key) ? key : SessionEngine.UnknownZone;
            if (!outp.TryGetValue(k, out var z)) { outp[k] = z = new SessionEngine.ZoneStats(); }
            z.Seconds += v.Seconds; z.Active += v.Active; z.Kinah += v.Kinah; z.KinahDrops += v.KinahDrops; z.CurrencyLines += v.CurrencyLines; z.KillsSeen += v.KillsSeen;
            z.Defeated += v.Defeated; z.Xp += v.Xp; z.Pets += v.Pets; z.Drops += v.Drops; z.Nodes += v.Nodes;
            foreach (var (n, c) in v.PetsByName) z.PetsByName[n] = z.PetsByName.GetValueOrDefault(n) + c;
            foreach (var (id, c) in v.Items) z.Items[id] = z.Items.GetValueOrDefault(id) + c;
            foreach (var (id, c) in v.ItemsBound) z.ItemsBound[id] = z.ItemsBound.GetValueOrDefault(id) + c;
        }
        return outp;
    }
}

/// <summary>One zone's share of a run: how long, and what came out of it. This is what the "best spot per hour" tables are built from.</summary>
public sealed record ZoneRun(int minutes, int kills, int nodes, long kinah, int drops, int pets, Dictionary<string, int> pets_by_name, Dictionary<string, int> items, Dictionary<string, int> items_bound);

/// <summary>
/// Kinah by enemy, a best guess: what came within a few seconds of the target bar showing this enemy. Only names on the
/// enemy list (data/mobs.txt) are ever credited, and only zones on the zone list are sent. The site labels it a guess.
/// </summary>
public sealed record MobRun(string name, string zone, int kills, long kinah, Dictionary<string, int> drops, int crystals)
{
    public static List<MobRun>? From(SessionEngine s, Func<long, string> key, Func<long, bool>? off = null)
    {
        var list = s.MobSnapshot()
            .Where(m => m.Zone != SessionEngine.UnknownZone && (m.Kills > 0 || m.Kinah > 0))
            .Take(RunPayload.MaxMobs)
            .Select(m =>
            {
                var d = new Dictionary<string, int>();
                foreach (var (id, n) in m.Drops) { if (off?.Invoke(id) == true) continue; var k = key(id); d[k] = d.GetValueOrDefault(k) + n; }
                return new MobRun(m.Name, m.Zone, m.Kills, m.Kinah, d, m.Crystals);
            }).ToList();
        return list.Count > 0 ? list : null;
    }
}

/// <summary>
/// One trip into an expedition or event area, entry to the last loot: how long it took, the Kinah, the kills and the
/// tradeable items. Only areas on the instance list (data/instances.txt) are sent; nothing else about the run or the player.
/// The site shows these per run and per hour inside runs. An event is filed under its family ("Shugo Festival"), the
/// same on every send; <c>event</c> names the game itself ("Hidden Lugi") when it was read and <c>family</c> repeats the
/// family, both left out when empty (since 0.8.12; the site files by <c>zone</c> and ignores fields it doesn't know).
/// </summary>
public sealed record InstanceRunPayload(string zone, string kind, int seconds, long kinah, int kills, Dictionary<string, int> items,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? @event = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? family = null)
{
    /// <summary>Most runs per payload, and the longest run, as the site accepts them.</summary>
    public const int MaxRuns = 50;
    public const int MaxSeconds = 4 * 3600;

    public static List<InstanceRunPayload>? From(IEnumerable<InstanceRun>? runs, Func<long, string> key, Func<long, bool>? off = null)
    {
        if (runs == null) return null;
        var list = runs
            // a run whose Kinah wasn't read isn't sent: the site would average it in as the little the chat saw (since 0.8.23)
            .Where(r => r.LastLoot != null && KindOf(r) != null && r.Seconds >= 1 && r.Seconds <= MaxSeconds && !r.KinahUnknown)
            .OrderByDescending(r => r.Start).Take(MaxRuns)
            .Select(r =>
            {
                var d = new Dictionary<string, int>();
                // every reward of the run, bound ones too: the site tracks how often each comes, sellable or not (Steven, 2026-10-23)
                foreach (var (id, n) in r.Items.Concat(r.BoundItems)) { if (off?.Invoke(id) == true) continue; var k = key(id); d[k] = d.GetValueOrDefault(k) + n; }
                r.FileUnderFamily();
                return new InstanceRunPayload(r.Zone, KindOf(r)!, (int)Math.Round(r.Seconds), r.Kinah, r.Kills, d,
                    r.Event.Length > 0 && r.Event != r.Zone ? r.Event : null, r.Family.Length > 0 ? r.Family : null);
            }).ToList();
        return list.Count > 0 ? list : null;
    }

    /// <summary>
    /// A run's kind as sent: the instance list's for an area, "World Boss" for a boss's loot (its zone is the boss's name,
    /// on no area list; the site keeps it as its own loot table, never Kinah/hr or an event). Null: not sent.
    /// </summary>
    static string? KindOf(InstanceRun r) => r.Kind == InstanceLog.WorldBoss ? InstanceLog.WorldBoss : KinahLedger.Ocr.ZoneLocator.KindOf(r.Zone);
}

/// <summary>Successes and failures for one recipe or enchant step.</summary>
public sealed record OddsCount(int ok, int fail);

/// <summary>
/// Craft and enchant outcomes seen in chat: crafts by item, enchants by "item|from|to". Results the line didn't tie
/// to an item are counted in the window but not shared; they say nothing about any recipe.
/// </summary>
public sealed record OddsPayload(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Dictionary<string, OddsCount>? crafts,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Dictionary<string, OddsCount>? enchants)
{
    public static OddsPayload? From(SessionEngine s)
    {
        var (crafts, enchants) = s.OddsSnapshot();
        Dictionary<string, OddsCount>? Pick(Dictionary<string, SessionEngine.Odds> m, Func<string, bool> keep)
        {
            var d = m.Where(kv => keep(kv.Key) && kv.Value.Attempts > 0)
                     .OrderByDescending(kv => kv.Value.Attempts).Take(RunPayload.MaxOddsKeys)
                     .ToDictionary(kv => kv.Key, kv => new OddsCount(Math.Min(kv.Value.Ok, 10_000), Math.Min(kv.Value.Fail, 10_000)));
            return d.Count > 0 ? d : null;
        }
        var c = Pick(crafts, k => k != "?" && k.Length <= 64);
        var e = Pick(enchants, k => !k.StartsWith("?|") && k.Length <= 64);
        return c == null && e == null ? null : new OddsPayload(c, e);
    }
}

/// <summary>One Market listing as seen on screen: what, how many, the price per unit, when. Never who listed it.</summary>
public sealed record PriceAsk(string item, long unit_price, int qty, string seen_at);

/// <summary>One of the player's own sales, from the chat: what, how many, the price per unit, when, and how long it was listed when this PC saw the listing too. Never who bought it.</summary>
public sealed record PriceSale(string item, int qty, long unit_price, string sold_at, double? hours_listed);

/// <summary>One of the player's own listings that expired unsold.</summary>
public sealed record PriceExpired(string item, int qty, string expired_at);

/// <summary>
/// Exactly what leaves the PC while the Market is open: the server the player typed, the asks read since the last
/// batch, the app version and the install ID. No character name, no account, no seller names, no screenshots.
/// Since 0.7 the player's own sales and expired listings ride along (item, quantity, price, time). Posted to the /v1/prices route.
/// </summary>
public sealed record PriceBatch(
    string app, string server_key, string install, List<PriceAsk> asks,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] List<PriceSale>? sales = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] List<PriceExpired>? expired = null);

/// <summary>Region plus the server as typed, slugged: "na_e" and "Siel (Elyos)" give "na_e-siel-elyos". Same-named servers exist in every region, so both parts matter.</summary>
public static class ServerKeys
{
    public static string Build(string region, string server)
    {
        var slug = Regex.Replace((server ?? "").ToLowerInvariant(), @"[^a-z0-9]+", "-").Trim('-');
        return slug.Length == 0 ? region : $"{region}-{slug}";
    }
}

public static class Uploader
{
    public static Task<bool> SendAsync(RunPayload p, UploadSettings u) =>
        !u.Enabled || string.IsNullOrWhiteSpace(u.Endpoint) ? Task.FromResult(false) : PostAsync(u.Endpoint, p);

    /// <summary>Market asks have their own switch in the window; here only the route matters.</summary>
    public static Task<bool> SendPricesAsync(PriceBatch p, UploadSettings u) =>
        string.IsNullOrWhiteSpace(u.PricesUrl) ? Task.FromResult(false) : PostAsync(u.PricesUrl, p);

    /// <summary>Why the last send failed, in plain words, so the window can say more than "couldn't reach the site".</summary>
    public static string LastError { get; private set; } = "";

    static async Task<bool> PostAsync<T>(string url, T payload)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("KinahLedger/" + RunPayload.AppVersion);
        try
        {
            var r = await http.PostAsJsonAsync(url, payload);
            if (r.IsSuccessStatusCode) { LastError = ""; return true; }
            string body = "";
            try { body = (await r.Content.ReadAsStringAsync()).Trim(); } catch (Exception) { /* no body */ }
            LastError = $"the site answered {(int)r.StatusCode}" + (body.Length > 0 ? ": " + (body.Length > 140 ? body[..140] + "…" : body) : "");
            return false;
        }
        catch (HttpRequestException ex) { LastError = "no connection: " + ex.Message; return false; }
        catch (TaskCanceledException) { LastError = "the site didn't answer within 15 s"; return false; }
    }
}
