using System.Text.Json;
using KinahLedger.Data;

namespace KinahLedger.Session;

/// <summary>Every finished run, kept per Windows user in %LOCALAPPDATA%\KinahLedger\sessions.jsonl, one line per run.</summary>
public static class SessionStore
{
    public static string PathFor() => Path.Combine(CrashLog.Dir, "sessions.jsonl");

    public sealed record Saved(string started, int minutes, string activity, string gear, string region, long kinah, long kinah_bound, int kills, int kinah_drops, Dictionary<string, int> drops, bool shared);

    public static void Append(SessionEngine e, ItemDictionary items, string activity, Settings s, bool shared)
    {
        try
        {
            var t = e.Totals();
            var drops = new Dictionary<string, int>();
            foreach (var r in e.ItemRows()) drops[r.Name] = r.Count + r.Bound;
            var rec = new Saved(e.Started.ToLocalTime().ToString("yyyy-MM-dd HH:mm"), (int)Math.Round(e.Elapsed.TotalMinutes), activity, s.GearBracket, s.Region,
                t.Kinah, t.KinahBound, t.Kills, e.KinahDrops, drops, shared);
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
