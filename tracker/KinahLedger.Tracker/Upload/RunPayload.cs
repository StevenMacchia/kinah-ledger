using System.Net.Http.Json;
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
}

/// <summary>
/// Exactly what leaves the PC when a run is shared. No character name, server, account, class, level,
/// chat text or screenshots: only totals the public tables need.
/// </summary>
public sealed record RunPayload(
    string app,
    string region,
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

    public static RunPayload Build(SessionEngine s, Settings cfg, string kind, string activity)
    {
        var zones = s.ZoneSnapshot();
        var main = zones.Where(kv => kv.Key != SessionEngine.UnknownZone).OrderByDescending(kv => kv.Value.Seconds).Select(kv => kv.Key).FirstOrDefault();
        return new(
            AppVersion,
            cfg.Region,
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

public static class Uploader
{
    public static async Task<bool> SendAsync(RunPayload p, UploadSettings u)
    {
        if (!u.Enabled || string.IsNullOrWhiteSpace(u.Endpoint)) return false;
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("KinahLedger/" + RunPayload.AppVersion);
        try
        {
            var r = await http.PostAsJsonAsync(u.Endpoint, p);
            return r.IsSuccessStatusCode;
        }
        catch (HttpRequestException) { return false; }
        catch (TaskCanceledException) { return false; }
    }
}
