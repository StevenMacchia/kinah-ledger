using System.Net.Http.Json;
using System.Text.RegularExpressions;
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
/// chat text or screenshots.
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
    string install)
{
    public static string AppVersion => typeof(RunPayload).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "0.0.0";

    /// <param name="region">The chosen server's region key ("na_e"); the settings' region when none was chosen.</param>
    /// <param name="server">The chosen server's label ("Siel (Elyos)"); empty files the run under the region alone.</param>
    public static RunPayload Build(SessionEngine s, Settings cfg, string kind, string activity, string region, string server)
    {
        var zones = s.ZoneSnapshot();
        var main = zones.Where(kv => kv.Key != SessionEngine.UnknownZone).OrderByDescending(kv => kv.Value.Seconds).Select(kv => kv.Key).FirstOrDefault();
        return new(
            AppVersion,
            region,
            ServerKeys.Build(region, server),
            activity,
            kind,
            main,
            cfg.GearBracket,
            (int)Math.Round(s.Elapsed.TotalMinutes),
            s.Kills,
            s.Nodes,
            s.Kinah,
            s.KinahBound,
            s.Items.ToDictionary(k => k.Key.ToString(), v => v.Value),
            s.ItemsBound.ToDictionary(k => k.Key.ToString(), v => v.Value),
            s.PetSnapshot(),
            zones.Where(kv => kv.Value.Seconds >= 30 || kv.Value.Kills > 0 || kv.Value.Pets > 0)
                 .ToDictionary(kv => kv.Key, kv => new ZoneRun((int)Math.Round(kv.Value.Seconds / 60), kv.Value.Kills, kv.Value.Nodes, kv.Value.Kinah, kv.Value.Drops, kv.Value.Pets, kv.Value.PetsByName)),
            InstallId.Get());
    }
}

/// <summary>One zone's share of a run: how long, and what came out of it. This is what the "best spot per hour" tables are built from.</summary>
public sealed record ZoneRun(int minutes, int kills, int nodes, long kinah, int drops, int pets, Dictionary<string, int> pets_by_name);

/// <summary>One Market listing as seen on screen: what, how many, the price per unit, when. Never who listed it.</summary>
public sealed record PriceAsk(string item, long unit_price, int qty, string seen_at);

/// <summary>
/// Exactly what leaves the PC while the Market is open: the server the player typed, the asks read since the last
/// batch, the app version and the install ID. No character name, no account, no seller names, no screenshots.
/// Posted to the /v1/prices route.
/// </summary>
public sealed record PriceBatch(string app, string server_key, string install, List<PriceAsk> asks);

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

    static async Task<bool> PostAsync<T>(string url, T payload)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("KinahLedger/" + RunPayload.AppVersion);
        try
        {
            var r = await http.PostAsJsonAsync(url, payload);
            return r.IsSuccessStatusCode;
        }
        catch (HttpRequestException) { return false; }
        catch (TaskCanceledException) { return false; }
    }
}
