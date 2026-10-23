using System.Text.Json;

namespace KinahLedger.Session;

/// <summary>
/// Price alerts (0.8.12): a watch list of items, each with a price and a side ("below 1,000" or "above 40,000") on one
/// server, set from a drop or recipe card. Checked whenever the prices change (the board every 15 minutes, and asks
/// read on the Market on this PC); an alert fires once when its price crosses the line and re-arms when the price goes
/// back. Kept on this PC in %LOCALAPPDATA%\KinahLedger\alerts.json; nothing is sent.
/// </summary>
public static class PriceAlerts
{
    public sealed class Alert
    {
        public string Server { get; set; } = "";
        public string Item { get; set; } = "";
        public bool Above { get; set; }
        public long Price { get; set; }
        /// <summary>False after it fired, until the price is back on the other side.</summary>
        public bool Armed { get; set; } = true;
        public DateTime? FiredAt { get; set; }

        public bool Met(long price) => Above ? price >= Price : price <= Price;
        public string Rule => $"{(Above ? "above" : "below")} {Price:N0}";
    }

    static readonly object Lock = new();
    static List<Alert>? _all;
    /// <summary>Tests point this elsewhere so the player's own file is never touched.</summary>
    public static string? DirOverride { get; set; }
    static string PathFor => Path.Combine(DirOverride ?? CrashLog.Dir, "alerts.json");

    public static List<Alert> For(string server) { lock (Lock) return Load().Where(a => Same(a.Server, server)).Select(Copy).ToList(); }

    public static Alert? Find(string server, string item) { lock (Lock) return Load().FirstOrDefault(a => Same(a.Server, server) && Same(a.Item, item)) is { } a ? Copy(a) : null; }

    /// <summary>Sets the one alert an item has on a server (a new price or side replaces it), armed.</summary>
    public static void Set(string server, string item, bool above, long price)
    {
        if (string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(item) || price <= 0) return;
        lock (Lock)
        {
            var all = Load();
            all.RemoveAll(a => Same(a.Server, server) && Same(a.Item, item));
            all.Add(new Alert { Server = server.Trim(), Item = item.Trim(), Above = above, Price = price });
            Save(all);
        }
    }

    public static bool Remove(string server, string item)
    {
        lock (Lock) { var all = Load(); int n = all.RemoveAll(a => Same(a.Server, server) && Same(a.Item, item)); if (n > 0) Save(all); return n > 0; }
    }

    /// <summary>
    /// The alerts on this server that fire at these prices: met and armed. Each fires once, then waits until its price
    /// is back on the other side of the line. An item with no price changes nothing.
    /// </summary>
    public static List<(Alert Alert, long Price)> Check(string server, Func<string, long?> priceOf, DateTime now)
    {
        var fired = new List<(Alert, long)>();
        lock (Lock)
        {
            bool changed = false;
            foreach (var a in Load().Where(a => Same(a.Server, server)))
            {
                if (priceOf(a.Item) is not long p || p <= 0) continue;
                if (a.Met(p))
                {
                    if (!a.Armed) continue;
                    a.Armed = false; a.FiredAt = now; changed = true;
                    fired.Add((Copy(a), p));
                }
                else if (!a.Armed) { a.Armed = true; changed = true; }
            }
            if (changed) Save(Load());
        }
        return fired;
    }

    /// <summary>Forgets what was loaded, for tests that switch folders.</summary>
    public static void Reset() { lock (Lock) _all = null; }

    static List<Alert> Load()
    {
        if (_all != null) return _all;
        _all = new();
        try { if (File.Exists(PathFor)) _all = JsonSerializer.Deserialize<List<Alert>>(File.ReadAllText(PathFor)) ?? new(); }
        catch (Exception ex) { CrashLog.Write("price alerts", ex); }
        return _all;
    }

    static void Save(List<Alert> all)
    {
        try { Directory.CreateDirectory(Path.GetDirectoryName(PathFor)!); File.WriteAllText(PathFor, JsonSerializer.Serialize(all)); }
        catch (Exception ex) { CrashLog.Write("price alerts", ex); }
    }

    static Alert Copy(Alert a) => new() { Server = a.Server, Item = a.Item, Above = a.Above, Price = a.Price, Armed = a.Armed, FiredAt = a.FiredAt };
    static bool Same(string a, string b) => string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
}
