using System.Net.Http;
using System.Text.Json;

namespace KinahLedger.Data;

/// <summary>
/// What kinahledger.com lists, read like a browser reads it (nothing is sent): the community's drop rates per zone for
/// the player's server (/zones/{server}.json, last 7 days) and the game's listed drop chances by enemy and zone
/// (/data/listed-drops.json, from MetaBot.GG). Shown on the card a click on a drop opens. Empty until the first fetch,
/// and stays empty with sharing off, so the tracker keeps offline then.
/// </summary>
public static class SiteData
{
    public sealed record Rate(double? PerKill, double? PerNode, double PerHour, int Runs);
    public sealed record ZoneEntry(string Zone, string Activity, int Runs, bool Published, Dictionary<string, Rate> Drops);
    public sealed record Source(string Monster, double Chance, string Zone, string Map, int Level);
    public sealed record Listed(string Name, string Url, List<Source> Sources);

    static readonly object Lock = new();
    static List<ZoneEntry> _zones = new();
    static Dictionary<string, Listed> _listed = new(StringComparer.OrdinalIgnoreCase);
    static DateTime _listedAt = DateTime.MinValue;

    public static int MinRuns { get; private set; } = 5;
    public static string ServerKey { get; private set; } = "";
    public static string SiteUrl { get; private set; } = "https://kinahledger.com";
    public static DateTime? ZonesAt { get; private set; }

    /// <summary>Fetches the zones snapshot for the server, and the listed drops once every six hours.</summary>
    public static async Task RefreshAsync(HttpClient http, string origin, string serverKey)
    {
        SiteUrl = origin; ServerKey = serverKey;
        try
        {
            var r = await http.GetAsync($"{origin}/zones/{serverKey}.json");
            if (r.IsSuccessStatusCode) { var z = ParseZones(await r.Content.ReadAsStringAsync(), out var min); lock (Lock) { _zones = z; MinRuns = min; ZonesAt = DateTime.UtcNow; } }
        }
        catch (Exception ex) { CrashLog.Note("site zones: " + ex.Message); }
        if ((DateTime.UtcNow - _listedAt).TotalHours < 6) return;
        try
        {
            var r = await http.GetAsync($"{origin}/data/listed-drops.json");
            if (r.IsSuccessStatusCode) { var l = ParseListed(await r.Content.ReadAsStringAsync()); lock (Lock) { _listed = l; _listedAt = DateTime.UtcNow; } }
        }
        catch (Exception ex) { CrashLog.Note("site listed drops: " + ex.Message); }
    }

    public static List<ZoneEntry> ParseZones(string json, out int minRuns)
    {
        var outp = new List<ZoneEntry>(); minRuns = 5;
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.TryGetProperty("publish_rule", out var pr) && pr.TryGetProperty("min_runs", out var mr) && mr.TryGetInt32(out var m)) minRuns = m;
        if (!root.TryGetProperty("windows", out var w) || !w.TryGetProperty("7d", out var week) || week.ValueKind != JsonValueKind.Array) return outp;
        foreach (var z in week.EnumerateArray())
        {
            var drops = new Dictionary<string, Rate>(StringComparer.OrdinalIgnoreCase);
            if (z.TryGetProperty("drops", out var d) && d.ValueKind == JsonValueKind.Object)
                foreach (var p in d.EnumerateObject())
                    drops[p.Name] = new Rate(Num(p.Value, "per_kill"), Num(p.Value, "per_node"), Num(p.Value, "per_hour") ?? 0, (int)(Num(p.Value, "n_runs") ?? 0));
            bool published = z.TryGetProperty("rates", out var rt) && rt.ValueKind == JsonValueKind.Object;
            outp.Add(new ZoneEntry(Str(z, "zone"), Str(z, "activity"), (int)(Num(z, "n_runs") ?? 0), published, drops));
        }
        return outp;
    }

    public static Dictionary<string, Listed> ParseListed(string json)
    {
        var outp = new Dictionary<string, Listed>(StringComparer.OrdinalIgnoreCase);
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Object) return outp;
        foreach (var it in items.EnumerateObject())
        {
            var name = Str(it.Value, "name");
            if (name.Length == 0) continue;
            var src = new List<Source>();
            if (it.Value.TryGetProperty("sources", out var ss) && ss.ValueKind == JsonValueKind.Array)
                foreach (var s in ss.EnumerateArray())
                    if (Num(s, "chance") is double c && c > 0) src.Add(new Source(Str(s, "monster"), c, Str(s, "zone"), Str(s, "map"), (int)(Num(s, "level") ?? 0)));
            var l = new Listed(name, Str(it.Value, "url"), src);
            outp[name] = l; outp[it.Name] = l;
        }
        return outp;
    }

    /// <summary>The community's 7-day figures for this zone (the open-world bucket first), or null when the site has no runs there.</summary>
    public static ZoneEntry? Zone(string? zone)
    {
        if (zone == null) return null;
        lock (Lock)
            return _zones.Where(z => string.Equals(z.Zone, zone, StringComparison.OrdinalIgnoreCase))
                         .OrderByDescending(z => z.Activity == "open-world").ThenByDescending(z => z.Runs).FirstOrDefault();
    }

    /// <summary>The site's listed drop chances for an item, by id or name.</summary>
    public static Listed? ListedFor(long id, string name)
    {
        lock (Lock)
            return (id > 0 && _listed.TryGetValue(id.ToString(), out var a)) ? a : _listed.TryGetValue(name, out var b) ? b : null;
    }

    /// <summary>For tests: load both files from text.</summary>
    public static void Load(string zonesJson, string listedJson)
    {
        var z = ParseZones(zonesJson, out var min); var l = ParseListed(listedJson);
        lock (Lock) { _zones = z; MinRuns = min; _listed = l; ZonesAt = DateTime.UtcNow; }
    }

    static double? Num(JsonElement e, string p) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;
    static string Str(JsonElement e, string p) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
}
