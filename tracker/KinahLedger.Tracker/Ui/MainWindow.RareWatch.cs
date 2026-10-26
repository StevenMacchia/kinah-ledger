using System.Windows.Media;
using KinahLedger.Data;

namespace KinahLedger.Ui;

/// <summary>
/// Rare drops, upgraded (2026-10-26, kinah-bench/mock-hud-next f1). The watch list (watch.txt, the same list the drop
/// card's "Alert me when it drops" adds to) is edited in Options › In game › Rare drops and kept on Save; the chime's
/// switch and volume are kept in hud.json (HudPrefs). A drop hands the Card and the Ticker its item, price and whether
/// it is watched as "rare…" values next to the ones HoldRareOnHud sets.
/// </summary>
public partial class MainWindow
{
    /// <summary>The watch list as Options edits it: one name a line, A to Z.</summary>
    internal static string WatchText() { lock (Watched) return string.Join("\n", Watched.OrderBy(x => x, StringComparer.OrdinalIgnoreCase)); }

    /// <summary>The names in an edited watch list, each once.</summary>
    internal static List<string> WatchNames(string text) =>
        text.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>Options' Save: the watch list (only when it was edited, so a card's Watch meanwhile is kept) and the chime.</summary>
    void ApplyRareDrops(OptionsChoice c, OptionsChoice was)
    {
        if (c.RareWatch != was.RareWatch) SetWatched(WatchNames(c.RareWatch));
        _hudPrefs.Chime = c.RareChime;
        _hudPrefs.ChimeVolume = Math.Clamp(c.RareVolume, 0, 1);
    }

    /// <summary>The whole watch list at once; written to watch.txt, except in a layout render or the self-test.</summary>
    void SetWatched(IEnumerable<string> names)
    {
        lock (Watched)
        {
            Watched.Clear();
            foreach (var n in names) Watched.Add(n);
            if (_demo) return;   // never the player's list
            try { System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(WatchPath)!); System.IO.File.WriteAllLines(WatchPath, Watched.OrderBy(x => x)); } catch (Exception) { }
        }
    }

    /// <summary>
    /// A drop's price as the window's rows say it (PriceBook, MainWindow.PriceCell): "28K", "≈28K" when it is the region's
    /// middle, and where ("on Siel", "each on Siel" for more than one). Blank for no sure price, which the looks say as
    /// "no price yet".
    /// </summary>
    (string Price, string Where) RarePrice(string name, string count)
    {
        if (PriceBook.QuoteOf(name) is not { Sure: true } q || q.Price <= 0) return ("", "");
        var server = Server?.Name ?? "";
        var each = count is "" or "×1" ? "" : "each ";
        return ((q.From == PriceBook.From.Region ? "≈" : "") + Fk(q.Price), (each + (server.Length > 0 ? "on " + server : "")).Trim());
    }

    /// <summary>The drop's own values on top of HoldRareOnHud's: the item, its price, whether it is watched and whether the chime played.</summary>
    void HoldRareExtras(long id, string price, string where, bool watched, bool chime)
    {
        _hudRare["rareItem"] = id.ToString();
        _hudRare["rarePrice"] = price;
        _hudRare["rareWhere"] = where;
        _hudRare["rareWatched"] = watched ? "1" : "";
        _hudRare["rareChime"] = chime ? "1" : "";
    }

    /* ---------------- the chime ---------------- */

    static MediaPlayer? _chimePlayer;
    static string ChimeWav => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media", "Windows Notify System Generic.wav");

    /// <summary>
    /// The chime at a volume, 0 to 1 (a drop, a price alert, Options' Play). Windows' notify sound through a MediaPlayer,
    /// which has a volume; the plain sound player (full volume) if that can't play, the system's asterisk with no file.
    /// </summary>
    public void PlayChime(double volume)
    {
        if (volume <= 0) return;
        try
        {
            if (!System.IO.File.Exists(ChimeWav)) { System.Media.SystemSounds.Asterisk.Play(); return; }
            if (_chimePlayer == null) { _chimePlayer = new MediaPlayer(); _chimePlayer.MediaFailed += (_, _) => PlainChime(); }
            _chimePlayer.Open(new Uri(ChimeWav));
            _chimePlayer.Volume = Math.Clamp(volume, 0, 1);
            _chimePlayer.Play();
        }
        catch (Exception) { PlainChime(); }
    }

    static void PlainChime()
    {
        try { new System.Media.SoundPlayer(ChimeWav).Play(); } catch (Exception) { }
    }

    /* ---------------- for Options ---------------- */

    /// <summary>A watched item's grade and icon for its tile in Options (Find: looks the name up, adds nothing).</summary>
    internal (string Grade, string Icon) WatchItem(string name) =>
        _items.Find(name) is long id ? (_items.Get(id)?.Grade ?? "", _items.IconUrl(id)) : ("", "");

    /// <summary>The item list's names holding the text, for Options' "Add item…" box.</summary>
    internal List<ItemInfo> ItemsLike(string text, int max) => _items.Like(text, max);

    /// <summary>For the options self-test: the chime as saved, and the drop the looks hold now.</summary>
    internal bool HudChime => _hudPrefs.Chime;
    internal double HudChimeVolume => _hudPrefs.ChimeVolume;
    internal IReadOnlyDictionary<string, string> RareHeldForTest => _hudRare;

    /// <summary>For the Options render: a sample watch list (a Legend and two Rares), in memory only.</summary>
    internal void SampleWatchList()
    {
        if (_demo) SetWatched(new[] { "Thick Balaur Horn", "Fine Diamond Gemstone", "Fine Orichalcum Ore" });
    }

    /// <summary>
    /// The Card and the Ticker with the mock's watched drop (Shade Gloves, Legend, 28K on Siel, the chime on), for layout
    /// renders: ui-hud-card-rare-watched.png and ui-hud-ticker-rare-watched.png.
    /// </summary>
    internal List<(string File, string Look, Dictionary<string, string> Values)> HudRareStates()
    {
        var id = _items.IdOf("Shade Gloves");
        var v = new Dictionary<string, string>(HudSample())
        {
            ["rare"] = "Shade Gloves", ["rareId"] = "sample-watched", ["rareCount"] = "×1", ["rareGrade"] = "Legend",
            ["rareIcon"] = _items.IconUrl(id), ["rareColor"] = "#5AA6FF", ["rareItem"] = id.ToString(),
            ["rarePrice"] = "28K", ["rareWhere"] = "on Siel", ["rareWatched"] = "1", ["rareChime"] = "1",
        };
        return new() { ("ui-hud-card-rare-watched.png", HudLooks.Card, v), ("ui-hud-ticker-rare-watched.png", HudLooks.Ticker, v) };
    }
}
