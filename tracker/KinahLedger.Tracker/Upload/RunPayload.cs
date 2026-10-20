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
    string gear,
    int minutes,
    int kills,
    long kinah,
    long kinah_bound,
    Dictionary<string, int> items,
    Dictionary<string, int> items_bound,
    string install)
{
    public static RunPayload Build(SessionEngine s, Settings cfg, string activity) => new(
        "0.1.0",
        cfg.Region,
        activity,
        cfg.GearBracket,
        (int)Math.Round(s.Elapsed.TotalMinutes),
        s.Kills,
        s.Kinah,
        s.KinahBound,
        s.Items.ToDictionary(k => k.Key.ToString(), v => v.Value),
        s.ItemsBound.ToDictionary(k => k.Key.ToString(), v => v.Value),
        InstallId.Get());
}

public static class Uploader
{
    public static async Task<bool> SendAsync(RunPayload p, UploadSettings u)
    {
        if (!u.Enabled || string.IsNullOrWhiteSpace(u.Endpoint)) return false;
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("KinahLedger/0.1");
        try
        {
            var r = await http.PostAsJsonAsync(u.Endpoint, p);
            return r.IsSuccessStatusCode;
        }
        catch (HttpRequestException) { return false; }
        catch (TaskCanceledException) { return false; }
    }
}
