using System.Text.Json;
using System.Text.Json.Serialization;

namespace KinahLedger;

/// <summary>settings.json beside the exe. Every field has a working default so the file is optional.</summary>
public sealed class Settings
{
    /// <summary>Substrings matched against process names (case-insensitive) to find the game window. Only its position is used; the process itself is never touched.</summary>
    public string[] ProcessHints { get; set; } = ["aion"];
    public string Region { get; set; } = "na_e";
    public string GearBracket { get; set; } = "1400-1599";
    public string ItemsFile { get; set; } = "data/tw-items.json";
    public UploadSettings Upload { get; set; } = new();
    public OcrSettings Ocr { get; set; } = new();
    public MarketSettings Market { get; set; } = new();
    public UiSettings Ui { get; set; } = new();
    public UpdateSettings Update { get; set; } = new();

    public static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true
    };

    public static Settings Load()
    {
        var p = Path.Combine(AppContext.BaseDirectory, "settings.json");
        if (!File.Exists(p)) return new Settings();
        return JsonSerializer.Deserialize<Settings>(File.ReadAllText(p), JsonOpts) ?? new Settings();
    }

    public static string Resolve(string path) => Path.IsPathRooted(path) ? path : Path.Combine(AppContext.BaseDirectory, path);
}

/// <summary>Screen reading: the chat's loot lines are read off the screen with Windows' built-in text recognition.</summary>
public sealed class OcrSettings
{
    /// <summary>Chat text is small; reading it at 2x is far more accurate. With AutoScale, the least it is enlarged by.</summary>
    public int Scale { get; set; } = 2;
    /// <summary>Enlarge the chat by how tall its text is on this screen (TextScale), aiming at TargetTextPx, never below Scale.</summary>
    public bool AutoScale { get; set; } = true;
    /// <summary>How tall the chat's words should be after enlarging, in pixels.</summary>
    public int TargetTextPx { get; set; } = 36;
    /// <summary>Judge a garbled item name by the colour it was printed in (its grade), learned from names read exactly.</summary>
    public bool NameColours { get; set; } = true;
    /// <summary>Name a loot list row whose text is cut off or garbled by its icon, among the items its letters could be.</summary>
    public bool LootIcons { get; set; } = true;
    /// <summary>
    /// Count a loot list row the chat never named, after a few seconds (the old "the chat read missed it" rule). Off since
    /// 2026-10-23 (Steven: about a quarter of gathers right): redraws, the in-game upgrade animation and a cancelled
    /// gather's panel turned into pickups. Rows still confirm chat lines, and the crafting panel's flow is unchanged.
    /// </summary>
    public bool LootListAdds { get; set; }
    public int IntervalMs { get; set; } = 700;
    /// <summary>What you're farming, for the shared totals. Override per run with --activity.</summary>
    public string Activity { get; set; } = "unknown";
    public bool Hud { get; set; } = true;
    /// <summary>How often the zone label above the minimap is read.</summary>
    public int ZoneEverySec { get; set; } = 10;
    /// <summary>Read the game window's own pixels (Windows Graphics Capture), so a window on top is never read. Off: copy the screen.</summary>
    public bool WindowCapture { get; set; } = true;
}

/// <summary>The window.</summary>
public sealed class UiSettings
{
    /// <summary>
    /// Item icons: each is fetched once from NCSOFT's image server by file name as its item shows up, the way a browser does
    /// on their site, and kept under %LocalAppData%\KinahLedger\icons. Nothing is bundled. Off (with sharing and update.check off too) keeps the tracker fully offline;
    /// rows show a lettered tile instead.
    /// </summary>
    public bool Icons { get; set; } = true;
}

/// <summary>Updates: the tracker asks the site for the newest version at start and every few hours.</summary>
public sealed class UpdateSettings
{
    /// <summary>Off: the tracker never asks. On, the window's switch chooses between installing a new version on its own and asking first.</summary>
    public bool Check { get; set; } = true;
    /// <summary>The version file the site publishes (version, download address, size, SHA-256).</summary>
    public string Manifest { get; set; } = Update.Updater.DefaultManifest;
}

/// <summary>Market price reading: while the Market window is on screen its listings are read and shared as asks.</summary>
public sealed class MarketSettings
{
    /// <summary>Master switch; the window has its own per-player switch on top of it.</summary>
    public bool Enabled { get; set; } = true;
    /// <summary>How often the open Market is read. Never faster than once a second.</summary>
    public int IntervalMs { get; set; } = 1000;
    /// <summary>How often the screen is searched for the Market while it isn't open.</summary>
    public int DetectEverySec { get; set; } = 3;
    /// <summary>The same ask (item, stack, price) seen again inside this window isn't shared twice.</summary>
    public int RepeatMinutes { get; set; } = 15;
    /// <summary>Queued asks are sent this often, or sooner when a batch fills.</summary>
    public int FlushSec { get; set; } = 10;
    public int BatchSize { get; set; } = 100;
    /// <summary>Reads in a row with no listing before the Market counts as closed and is looked for again.</summary>
    public int LostAfterReads { get; set; } = 5;
}

public sealed class UploadSettings
{
    /// <summary>On by default; the window's "what's sent?" link says exactly what leaves the PC.</summary>
    public bool Enabled { get; set; } = true;
    /// <summary>Where finished runs go (…/v1/runs): the site's ingest route. Blank it in settings.json and nothing is sent; an unreachable site keeps the run on this PC.</summary>
    public string Endpoint { get; set; } = "https://kinahledger.com/v1/runs";
    /// <summary>Override for where Market asks go (…/v1/prices). Empty: derived from Endpoint when that ends in /v1/runs, so the default is https://kinahledger.com/v1/prices; otherwise nothing is sent.</summary>
    public string PricesEndpoint { get; set; } = "";

    [JsonIgnore]
    public string PricesUrl
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(PricesEndpoint)) return PricesEndpoint.Trim();
            var e = Endpoint.Trim().TrimEnd('/');
            return e.EndsWith("/v1/runs", StringComparison.OrdinalIgnoreCase) ? e[..^4] + "prices" : "";
        }
    }
}
