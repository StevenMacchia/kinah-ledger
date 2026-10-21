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
/// itself) and craft and enchant outcome counts.
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
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] OddsPayload? odds = null)
{
    public static string AppVersion => typeof(RunPayload).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "0.0.0";

    /// <summary>Most keys per odds map, as the site accepts them; the most-tried recipes and steps are kept.</summary>
    public const int MaxOddsKeys = 100;

    /// <param name="region">The chosen server's region key ("na_e"); the settings' region when none was chosen.</param>
    /// <param name="server">The chosen server's label ("Siel (Elyos)"); empty files the run under the region alone.</param>
    public static RunPayload Build(SessionEngine s, Settings cfg, string kind, string activity, string region, string server, ItemDictionary? dict = null)
    {
        var zones = s.ZoneSnapshot();
        var main = zones.Where(kv => kv.Key != SessionEngine.UnknownZone).OrderByDescending(kv => kv.Value.Seconds).Select(kv => kv.Key).FirstOrDefault();
        long? net = s.NetKinah is long n && s.Elapsed.TotalMinutes >= 5 ? RoundNet(n) : null;
        string Key(long id) => dict?.KeyOf(id) ?? id.ToString();
        return new(
            AppVersion,
            region,
            ServerKeys.Build(region, server),
            activity,
            kind,
            main,
            cfg.GearBracket,
            // farming minutes, not wall minutes: time within a minute of a loot, kill or gather line. The site's per-hour
            // rates divide by this; drop rates per kill and per attempt don't depend on it. At least one minute once anything happened.
            Math.Max(s.Events > 0 ? 1 : 0, (int)Math.Round(s.Active.TotalMinutes)),
            s.Kills,
            s.Nodes,
            s.Kinah,
            s.KinahBound,
            Merge(s.Items, Key),
            Merge(s.ItemsBound, Key),
            s.PetSnapshot(),
            zones.Where(kv => kv.Value.Active >= 30 || kv.Value.Kills > 0 || kv.Value.Pets > 0)
                 .ToDictionary(kv => kv.Key, kv => new ZoneRun((int)Math.Round(kv.Value.Active / 60), kv.Value.Kills, kv.Value.Nodes, kv.Value.Kinah, kv.Value.Drops, kv.Value.Pets, kv.Value.PetsByName)),
            InstallId.Get(),
            net,
            OddsPayload.From(s));
    }

    /// <summary>The net is rounded to the nearest 100 so the figure says nothing exact about the player's purse.</summary>
    public static long RoundNet(long n) => (long)Math.Round(n / 100.0, MidpointRounding.AwayFromZero) * 100;

    /// <summary>Counts keyed the way the site reads them; two synthetic IDs that carry the same name add up.</summary>
    static Dictionary<string, int> Merge(Dictionary<long, int> counts, Func<long, string> key)
    {
        var d = new Dictionary<string, int>();
        foreach (var (id, n) in counts) { var k = key(id); d[k] = d.GetValueOrDefault(k) + n; }
        return d;
    }
}

/// <summary>One zone's share of a run: how long, and what came out of it. This is what the "best spot per hour" tables are built from.</summary>
public sealed record ZoneRun(int minutes, int kills, int nodes, long kinah, int drops, int pets, Dictionary<string, int> pets_by_name);

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
