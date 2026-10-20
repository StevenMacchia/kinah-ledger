using System.Text.Json;

namespace KinahLedger.Session;

/// <summary>
/// The player's own Market listings as the chat announced them, kept per Windows user in
/// %LOCALAPPDATA%\KinahLedger\listings.json so a sale days later can still say how long the item was listed.
/// Never shared: only the hours between listing and sale go out with the sale. At most 100 entries, none older than 14 days.
/// </summary>
public static class ListingBook
{
    public sealed record Entry(string item, int qty, DateTime at);

    static readonly object Lock = new();
    static string PathFor() => Path.Combine(CrashLog.Dir, "listings.json");
    /// <summary>Tests point this elsewhere so the player's own file is never touched.</summary>
    public static string? PathOverride { get; set; }

    public static void Add(string item, int qty, DateTime at)
    {
        lock (Lock)
        {
            var list = Load();
            list.Add(new Entry(item, qty, at));
            Save(list);
        }
    }

    /// <summary>Removes and returns the listing time of the oldest listing of this item, preferring one of the same quantity; null when none was seen on this PC.</summary>
    public static DateTime? Take(string item, int qty)
    {
        lock (Lock)
        {
            var list = Load();
            var same = list.Where(e => string.Equals(e.item, item, StringComparison.OrdinalIgnoreCase)).OrderBy(e => e.at).ToList();
            var hit = same.FirstOrDefault(e => e.qty == qty) ?? same.FirstOrDefault();
            if (hit == null) return null;
            list.Remove(hit);
            Save(list);
            return hit.at;
        }
    }

    static List<Entry> Load()
    {
        try
        {
            var p = PathOverride ?? PathFor();
            if (!File.Exists(p)) return new();
            var list = JsonSerializer.Deserialize<List<Entry>>(File.ReadAllText(p)) ?? new();
            var cutoff = DateTime.UtcNow.AddDays(-14);
            return list.Where(e => e.at >= cutoff).ToList();
        }
        catch (Exception) { return new(); }   // a damaged file only costs the time-to-sell figure
    }

    static void Save(List<Entry> list)
    {
        try { File.WriteAllText(PathOverride ?? PathFor(), JsonSerializer.Serialize(list.OrderBy(e => e.at).TakeLast(100).ToList())); }
        catch (Exception ex) { CrashLog.Write("listing book", ex); }
    }
}
