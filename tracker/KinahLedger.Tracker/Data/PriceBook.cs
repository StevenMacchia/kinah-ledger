using System.Text.Json;

namespace KinahLedger.Data;

/// <summary>
/// Market prices by item name, so gathered materials and drops can be valued in Kinah.
/// Reads prices.json from %LOCALAPPDATA%\KinahLedger or data\ beside the exe:
/// { "updated": "2026-10-19", "prices": { "Orichalcum Ore": 1950, "Ruby Gemstone": 12000 } }
/// Empty until a price source is wired up; nothing is valued with made-up numbers.
/// </summary>
public static class PriceBook
{
    static Dictionary<string, long>? _prices;
    public static string Updated { get; private set; } = "";
    public static bool Any => Load().Count > 0;

    public static long? Price(string name) => Load().TryGetValue(Norm(name), out var p) ? p : null;

    static Dictionary<string, long> Load()
    {
        if (_prices != null) return _prices;
        _prices = new Dictionary<string, long>();
        foreach (var path in new[] { Path.Combine(CrashLog.Dir, "prices.json"), Settings.Resolve("data/prices.json") })
        {
            try
            {
                if (!File.Exists(path)) continue;
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                if (doc.RootElement.TryGetProperty("updated", out var u) && u.ValueKind == JsonValueKind.String) Updated = u.GetString() ?? "";
                if (!doc.RootElement.TryGetProperty("prices", out var p)) continue;
                foreach (var kv in p.EnumerateObject()) if (kv.Value.TryGetInt64(out var v)) _prices[Norm(kv.Name)] = v;
                break;
            }
            catch (Exception ex) { CrashLog.Write("prices", ex); }
        }
        return _prices;
    }

    static string Norm(string s) => string.Join(' ', s.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
}
