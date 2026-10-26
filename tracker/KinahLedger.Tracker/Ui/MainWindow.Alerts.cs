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
        foreach (var p in fresh)
        {
            int x = p.What.LastIndexOf(" ×", StringComparison.Ordinal);
            if (x <= 0) continue;                                   // Kinah, not an item
            var name = p.What[..x];
            var grade = _items.Get(_items.IdOf(name))?.Grade ?? "";
            bool rare = grade is "Legend" or "Unique" or "Epic";
            if (!rare && !IsWatched(name)) continue;
            var color = grade switch
            {
                "Legend" => Color.FromRgb(0x5A, 0xA6, 0xFF), "Unique" => Color.FromRgb(0xF3, 0xD3, 0x5E), "Epic" => Color.FromRgb(0xB4, 0x8B, 0xFF),
                _ => Color.FromRgb(0x5E, 0xD8, 0xCB),
            };
            Chime();
            _hud?.Alert((rare ? "Rare drop: " : "Watched drop: ") + p.What, color);
            HoldRareOnHud(name, p.What[(x + 1)..], rare ? grade : "", color, p.At);   // the Card's and the Ticker's rare drop
            Say((rare ? $"Rare drop ({grade}): " : "Watched drop: ") + p.What, "Good");
        }
    }

    static void Chime()
    {
        try
        {
            var wav = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media", "Windows Notify System Generic.wav");
            if (System.IO.File.Exists(wav)) new System.Media.SoundPlayer(wav).Play();
            else System.Media.SystemSounds.Asterisk.Play();
        }
        catch (Exception) { }
    }
}
