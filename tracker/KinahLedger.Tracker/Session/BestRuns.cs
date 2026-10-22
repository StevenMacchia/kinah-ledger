using System.Text.Json;
using KinahLedger.Data;

namespace KinahLedger.Session;

/// <summary>
/// The player's best hour in each zone, per server, kept on this PC in %LOCALAPPDATA%\KinahLedger\best.json, so the run
/// card can say "Your best here: 1.40M/hr". A stay counts once it has ten minutes of farming time in the zone: a finished
/// run (per zone), a zone left for another, or the window closing. The rate is the window's headline: Kinah plus the
/// tradeable loot at the lowest Market price, after the Market's cut, per hour of farming time. The first look also
/// reads the saved run history (sessions.jsonl) at today's prices, so the best is there from the first start of 0.7.6.
/// </summary>
public static class BestRuns
{
    public sealed record Best(long PerHr, int Minutes, string At);

    /// <summary>A stay shorter than this doesn't count: a few lucky minutes would set a best nobody can beat.</summary>
    public const int MinMinutes = 10;

    static readonly object Lock = new();
    static Dictionary<string, Best>? _best;
    /// <summary>Tests point this elsewhere so the player's own files are never touched.</summary>
    public static string? DirOverride { get; set; }

    static string Dir => DirOverride ?? CrashLog.Dir;
    static string Key(string server, string zone) => server.Trim() + "|" + zone.Trim();

    /// <summary>Kinah plus the tradeable items at the lowest Market price after the Market's cut; items with no price add nothing.</summary>
    public static long Value(long kinah, IEnumerable<(string Name, int Count)> tradeable)
    {
        double v = kinah;
        foreach (var (name, n) in tradeable)
            if (n > 0 && PriceBook.Price(name) is long p) v += p * (double)n * PriceBook.SellerKeeps;
        return (long)Math.Round(v);
    }

    /// <summary>What a count is worth now, as the window's session total.</summary>
    public static long Value(SessionEngine e) => Value(e.Totals().Kinah, e.ItemRows().Select(r => (r.Name, r.Count)));

    /// <summary>The best for a zone on a server, or null when there is none yet.</summary>
    public static Best? For(string server, string? zone)
    {
        if (string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(zone) || zone == SessionEngine.UnknownZone) return null;
        lock (Lock) return Load().TryGetValue(Key(server, zone), out var b) ? b : null;
    }

    /// <summary>A stay in a zone: kept when it ran ten minutes or more and beat the best there. True when it set a new best.</summary>
    public static bool Offer(string server, string? zone, long value, double seconds)
    {
        if (string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(zone) || zone == SessionEngine.UnknownZone) return false;
        if (seconds < MinMinutes * 60 || value <= 0) return false;
        long perHr = (long)Math.Round(value / (seconds / 3600));
        lock (Lock)
        {
            var all = Load(); var k = Key(server, zone);
            if (all.TryGetValue(k, out var b) && b.PerHr >= perHr) return false;
            all[k] = new Best(perHr, (int)Math.Round(seconds / 60), DateTime.Now.ToString("yyyy-MM-dd"));
            Save(all);
            return true;
        }
    }

    /// <summary>A stay as the engine counted it (the zone view when the zone changes or the window closes).</summary>
    public static bool Offer(string server, SessionEngine e) => Offer(server, e.Zone, Value(e), e.Active.TotalSeconds);

    /// <summary>A finished run, zone by zone.</summary>
    public static void OfferRun(string server, SessionEngine e, ItemDictionary items)
    {
        foreach (var (zone, z) in e.ZoneSnapshot())
            Offer(server, zone, Value(z.Kinah, z.Items.Select(kv => (items.NameOf(kv.Key), kv.Value))), z.Seconds);
    }

    static Dictionary<string, Best> Load()
    {
        if (_best != null) return _best;
        _best = new Dictionary<string, Best>(StringComparer.OrdinalIgnoreCase);
        var path = Path.Combine(Dir, "best.json");
        try
        {
            if (File.Exists(path))
            {
                var d = JsonSerializer.Deserialize<Dictionary<string, Best>>(File.ReadAllText(path));
                if (d != null) foreach (var (k, v) in d) _best[k] = v;
                return _best;
            }
            // first start with this file: the saved runs, at today's prices, under the server they were saved with
            Seed(_best);
            if (_best.Count > 0) Save(_best);
        }
        catch (Exception ex) { CrashLog.Write("best runs", ex); }
        return _best;
    }

    /// <summary>Each saved run's zones (ten minutes or more), valued at today's prices. Runs from before servers were saved are skipped.</summary>
    static void Seed(Dictionary<string, Best> into)
    {
        var path = Path.Combine(Dir, "sessions.jsonl");
        if (!File.Exists(path)) return;
        foreach (var line in File.ReadLines(path))
        {
            if (line.Length < 3) continue;
            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                var server = root.TryGetProperty("server", out var sv) && sv.ValueKind == JsonValueKind.String ? sv.GetString() ?? "" : "";
                if (server.Length == 0 || !root.TryGetProperty("zones", out var zones) || zones.ValueKind != JsonValueKind.Object) continue;
                var at = root.TryGetProperty("started", out var st) && st.ValueKind == JsonValueKind.String && st.GetString() is { Length: >= 10 } s ? s[..10] : "";
                foreach (var z in zones.EnumerateObject())
                {
                    int minutes = Int(z.Value, "minutes");
                    if (minutes < MinMinutes) continue;
                    var items = new List<(string, int)>();
                    if (z.Value.TryGetProperty("items", out var it) && it.ValueKind == JsonValueKind.Object)
                        foreach (var i in it.EnumerateObject()) if (i.Value.TryGetInt32(out var n)) items.Add((i.Name, n));
                    long perHr = (long)Math.Round(Value(Long(z.Value, "kinah"), items) / (minutes / 60.0));
                    var k = Key(server, z.Name);
                    if (perHr > 0 && (!into.TryGetValue(k, out var b) || b.PerHr < perHr)) into[k] = new Best(perHr, minutes, at);
                }
            }
            catch (Exception) { /* one bad line doesn't stop the rest */ }
        }
    }

    static int Int(JsonElement e, string p) => e.TryGetProperty(p, out var v) && v.TryGetInt32(out var n) ? n : 0;
    static long Long(JsonElement e, string p) => e.TryGetProperty(p, out var v) && v.TryGetInt64(out var n) ? n : 0;

    static void Save(Dictionary<string, Best> all)
    {
        try { File.WriteAllText(Path.Combine(Dir, "best.json"), JsonSerializer.Serialize(all)); }
        catch (Exception ex) { CrashLog.Write("best runs", ex); }
    }

    /// <summary>Forgets what was loaded, for tests that switch folders.</summary>
    public static void Reset() { lock (Lock) _best = null; }
}
