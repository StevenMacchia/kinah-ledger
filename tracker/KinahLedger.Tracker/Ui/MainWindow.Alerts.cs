using System.Windows.Media;

namespace KinahLedger.Ui;

/// <summary>
/// Rare drop alerts (0.8, Steven 2026-10-22): a Legend (blue), Unique (gold) or Epic (purple) item, or any item the
/// player watches, plays a chime and lights the in-game panel in the item's grade colour for a few seconds, and says so
/// in the window's status line. Items are watched from the drop card ("Watch"); the list is kept on this PC.
/// </summary>
public partial class MainWindow
{
    static string WatchPath => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KinahLedger", "watch.txt");
    static readonly HashSet<string> Watched = LoadWatched();
    DateTime _alertSeen = DateTime.UtcNow;

    static HashSet<string> LoadWatched()
    {
        try { return System.IO.File.Exists(WatchPath) ? System.IO.File.ReadAllLines(WatchPath).Select(l => l.Trim()).Where(l => l.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase) : new(StringComparer.OrdinalIgnoreCase); }
        catch (Exception) { return new(StringComparer.OrdinalIgnoreCase); }
    }

    public static bool IsWatched(string item) { lock (Watched) return Watched.Contains(item); }

    /// <summary>Watch or stop watching an item; returns whether it is watched now.</summary>
    public static bool ToggleWatch(string item)
    {
        lock (Watched)
        {
            bool on = !Watched.Remove(item);
            if (on) Watched.Add(item);
            try { System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(WatchPath)!); System.IO.File.WriteAllLines(WatchPath, Watched.OrderBy(x => x)); } catch (Exception) { }
            return on;
        }
    }

    /// <summary>Called from the window's tick: alerts on pickups counted since the last tick.</summary>
    void AlertTick()
    {
        var chat = _watcher.Chat;
        if (chat == null) return;
        var fresh = chat.RecentPickups(12).Where(p => p.At > _alertSeen).OrderBy(p => p.At).ToList();
        if (fresh.Count == 0) return;
        _alertSeen = fresh[^1].At;
        foreach (var p in fresh) AlertFor(p.What, p.At, p.Where);
    }

    /// <summary>
    /// Whether a pickup that landed here chimes (0.8.62): a drop out in the world does; a run's reward never does (an
    /// event's cards, an expedition's table, an arena, a legion area, a world boss's loot, a reward screen). Steven,
    /// 2026-10-29: "it gives a windows ding sound every time something is logged from an event and i hate it": each Epic
    /// on an event's cards chimed. The panel and the status line still show the item.
    /// </summary>
    internal static bool ChimesFor(string where) => !KinahLedger.Session.Activity.IsRunLabel(where);

    /// <summary>A chime at most this often: drops that land together chime once.</summary>
    public const double ChimeGapSeconds = 10;
    DateTime _chimedAt = DateTime.MinValue;

    /// <summary>
    /// One pickup ("Shade Gloves ×1"): a Legend, Unique or Epic item, or any item on the watch list whatever its grade,
    /// chimes (when Options › In game › Rare drops says so), lights the panel and says so in the status line. The Card
    /// and the Ticker get the item, its grade, count, price and whether it is watched (MainWindow.RareWatch.cs); the
    /// strip its one line with the price. Returns whether it alerted (the options self-test).
    /// </summary>
    internal bool AlertFor(string what, DateTime at, string where = "")
    {
        int x = what.LastIndexOf(" ×", StringComparison.Ordinal);
        if (x <= 0) return false;                                   // Kinah, not an item
        var name = what[..x];
        var count = what[(x + 1)..];
        var id = _items.IdOf(name);
        var grade = _items.Get(id)?.Grade ?? "";
        bool rare = grade is "Legend" or "Unique" or "Epic";
        bool watched = IsWatched(name);
        if (!rare && !watched) return false;
        var color = grade switch
        {
            "Legend" => Color.FromRgb(0x5A, 0xA6, 0xFF), "Unique" => Color.FromRgb(0xF3, 0xD3, 0x5E), "Epic" => Color.FromRgb(0xB4, 0x8B, 0xFF),
            "Rare" => Color.FromRgb(0x62, 0xD9, 0x8B), _ => Color.FromRgb(0x5E, 0xD8, 0xCB),
        };
        bool chime = _hudPrefs.Chime && ChimesFor(where);
        if (chime && !_demo && (DateTime.UtcNow - _chimedAt).TotalSeconds >= ChimeGapSeconds) { _chimedAt = DateTime.UtcNow; Chime(); }
        var (price, where) = RarePrice(name, count);
        var priceText = price.Length > 0 ? ("sells for " + price + " " + where).TrimEnd() : "no price yet";
        _hud?.Alert((rare ? "Rare drop: " : "Watched drop: ") + what + " · " + priceText, color);
        HoldRareOnHud(name, count, grade, color, at);   // the Card's and the Ticker's rare drop
        HoldRareExtras(id, price, where, watched, chime);
        Say((rare ? $"Rare drop ({grade}): " : "Watched drop: ") + what + " · " + priceText, "Good");
        return true;
    }

    /// <summary>A price alert, or a drop with the chime on: at the volume chosen in Options › In game › Rare drops.</summary>
    void Chime() => PlayChime(_hudPrefs.ChimeVolume);
}
