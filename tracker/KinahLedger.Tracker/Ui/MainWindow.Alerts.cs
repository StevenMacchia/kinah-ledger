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
    /// <summary>The watch list: watch.txt, read when the player's window opens (<see cref="UseWatchList"/>).</summary>
    static readonly HashSet<string> Watched = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>
    /// A layout render's or a self-test's process (a window made with watch: false): the list starts empty and watch.txt is
    /// neither read nor written, as with ui.json and hud.json, so a render or a test reads the same on every PC.
    /// </summary>
    static bool _watchLoaded, _watchDetached;
    DateTime _alertSeen = DateTime.UtcNow;

    /// <summary>From the window's constructor: the player's window reads watch.txt once; a render's or a test's empties the list and keeps it off the file.</summary>
    static void UseWatchList(bool player)
    {
        lock (Watched)
        {
            if (!player) { _watchDetached = true; Watched.Clear(); return; }
            if (_watchLoaded || _watchDetached) return;
            _watchLoaded = true;
            try { if (System.IO.File.Exists(WatchPath)) foreach (var l in System.IO.File.ReadAllLines(WatchPath)) if (l.Trim() is { Length: > 0 } n) Watched.Add(n); }
            catch (Exception) { /* unreadable: start empty */ }
        }
    }

    /// <summary>Writes the list to watch.txt (the caller holds the lock), never from a render's or a test's process.</summary>
    static void SaveWatched()
    {
        if (_watchDetached) return;
        try { System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(WatchPath)!); System.IO.File.WriteAllLines(WatchPath, Watched.OrderBy(x => x)); } catch (Exception) { }
    }

    public static bool IsWatched(string item) { lock (Watched) return Watched.Contains(item); }

    /// <summary>Watch or stop watching an item; returns whether it is watched now.</summary>
    public static bool ToggleWatch(string item)
    {
        lock (Watched)
        {
            bool on = !Watched.Remove(item);
            if (on) Watched.Add(item);
            SaveWatched();
            return on;
        }
    }

    /// <summary>Called from the window's tick: alerts on pickups counted since the last tick.</summary>
    void AlertTick()
    {
        ForgetRare(held: false);   // nothing from outside a tick waits for this one's moment (Ui/MainWindow.RareMoment.cs)
        var chat = _watcher.Chat;
        if (chat == null) return;
        var fresh = chat.RecentPickups(12).Where(p => p.At > _alertSeen).OrderBy(p => p.At).ToList();
        if (fresh.Count == 0) return;
        _alertSeen = fresh[^1].At;
        foreach (var p in fresh) AlertFor(p.What, p.At, p.Where);
        RareTickDone();   // the tick's drops are one moment, the best with "+2 more" (Ui/MainWindow.RareMoment.cs)
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
    internal bool AlertFor(string what, DateTime at, string landed = "")
    {
        int x = what.LastIndexOf(" ×", StringComparison.Ordinal);
        if (x <= 0) return false;                                   // Kinah, not an item
        var name = what[..x];
        var count = what[(x + 1)..];
        var id = _items.IdOf(name);
        var grade = _items.Get(id)?.Grade ?? "";
        // a coveted find from any node (Poko's Skin Bundle) has no price, but is a rare drop all the same
        bool rare = grade is "Legend" or "Unique" or "Epic" || KinahLedger.Data.GatheringCatalog.Find(name) is { AnyNode: true };
        bool watched = IsWatched(name);
        if (!rare && !watched) return false;
        var color = grade switch
        {
            "Legend" => Color.FromRgb(0x5A, 0xA6, 0xFF), "Unique" => Color.FromRgb(0xF3, 0xD3, 0x5E), "Epic" => Color.FromRgb(0xB4, 0x8B, 0xFF),
            "Rare" => Color.FromRgb(0x62, 0xD9, 0x8B), "Special" => Color.FromRgb(0x3F, 0xD3, 0xC6), _ => Color.FromRgb(0x3D, 0xD6, 0x8C),
        };
        bool chime = _hudPrefs.Chime && ChimesFor(landed);
        if (chime && !_demo && (DateTime.UtcNow - _chimedAt).TotalSeconds >= ChimeGapSeconds) { _chimedAt = DateTime.UtcNow; Chime(); }
        var (price, where) = RarePrice(name, count);
        var priceText = price.Length > 0 ? ("sells for " + price + " " + where).TrimEnd() : "no price yet";
        _hud?.Alert((rare ? "Rare drop: " : "Watched drop: ") + what + " · " + priceText, color);
        HoldRareOnHud(name, count, grade, color, at);   // the Card's and the Ticker's rare drop
        HoldRareExtras(id, price, where, watched, chime);
        NoteRare(new RareFind(id, name, count, grade, color, at, price, where, watched, chime));
        Say((rare ? $"Rare drop ({grade}): " : "Watched drop: ") + what + " · " + priceText, "Good");
        return true;
    }

    /// <summary>A drop with the chime on: at the volume chosen in Options › In game › Rare drops.</summary>
    void Chime() => PlayChime(_hudPrefs.ChimeVolume);

    /// <summary>
    /// A price alert's knock ("alert", the same note twice), at the same volume: its own sound so a price crossing and a
    /// drop don't sound alike (Steven, 2026-11-04).
    /// </summary>
    void AlertSound() => PlaySound("alert", _hudPrefs.ChimeVolume);
}
