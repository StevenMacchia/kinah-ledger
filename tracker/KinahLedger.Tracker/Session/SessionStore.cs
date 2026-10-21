using System.Text.Json;
using KinahLedger.Data;

namespace KinahLedger.Session;

/// <summary>Every finished run, kept per Windows user in %LOCALAPPDATA%\KinahLedger\sessions.jsonl, one line per run.</summary>
public static class SessionStore
{
    public static string PathFor() => Path.Combine(CrashLog.Dir, "sessions.jsonl");

    public sealed record Saved(string started, int minutes, string activity, string gear, string region, long kinah, long kinah_bound, int kills, int kinah_drops, int nodes, int rare_finds,
        Dictionary<string, int> drops, Dictionary<string, int> pets, Dictionary<string, Upload.ZoneRun> zones, bool shared,
        long? kinah_net = null, Upload.OddsPayload? odds = null, int sold = 0, long sold_kinah = 0);

    static Dictionary<string, int> ByName(Dictionary<long, int> m, ItemDictionary items)
    {
        var d = new Dictionary<string, int>();
        foreach (var (id, n) in m) { var k = items.NameOf(id); d[k] = d.GetValueOrDefault(k) + n; }
        return d;
    }

    public static void Append(SessionEngine e, ItemDictionary items, string activity, Settings s, bool shared)
    {
        try
        {
            var t = e.Totals();
            var drops = new Dictionary<string, int>();
            foreach (var r in e.ItemRows()) drops[r.Name] = r.Count + r.Bound;
            var zones = e.ZoneSnapshot().ToDictionary(kv => kv.Key, kv => new Upload.ZoneRun((int)Math.Round(kv.Value.Seconds / 60), kv.Value.Kills, kv.Value.Nodes, kv.Value.Kinah, kv.Value.Drops, kv.Value.Pets, kv.Value.PetsByName, ByName(kv.Value.Items, items), ByName(kv.Value.ItemsBound, items)));
            var rec = new Saved(e.Started.ToLocalTime().ToString("yyyy-MM-dd HH:mm"), (int)Math.Round(e.Active.TotalMinutes), activity, s.GearBracket, s.Region,
                t.Kinah, t.KinahBound, t.Kills, e.KinahDrops, t.Nodes, t.Bonus, drops, e.PetSnapshot(), zones, shared,
                e.NetKinah, Upload.OddsPayload.From(e), e.Sold, e.SoldKinah);
            File.AppendAllText(PathFor(), JsonSerializer.Serialize(rec) + "\n");
        }
        catch (Exception ex) { CrashLog.Write("session store", ex); }
    }

    public static int Count()
    {
        try { var p = PathFor(); return File.Exists(p) ? File.ReadLines(p).Count(l => l.Length > 2) : 0; }
        catch (Exception) { return 0; }
    }
}
