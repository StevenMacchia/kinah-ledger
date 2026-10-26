using System.Text.Json;

namespace KinahLedger.Ui;

/// <summary>
/// The panel's overlay choices (Options › In game): locked in place, and its size. Kept in hud.json beside ui.json, so
/// the counting side's UserPrefs is left as it is.
/// </summary>
public sealed class HudPrefs
{
    /// <summary>Clicks go through the panel to the game and it never takes focus. On for new installs; an install from before keeps it draggable.</summary>
    public bool Locked { get; set; } = true;
    /// <summary>The panel's size, 0.7 to 2 (70 to 200%).</summary>
    public double Scale { get; set; } = 1;
    /// <summary>The panel's look (HudLooks: "strip", "card", "ticker"). The strip for everyone until they pick another.</summary>
    public string Look { get; set; } = HudLooks.Strip;
    /// <summary>
    /// Where the player left the Ticker, kept apart from the strip's and the Card's spot (UserPrefs.HudX/HudY): its toasts
    /// hang down, so its place is left of the target bar, not over it. Null until it is dragged.
    /// </summary>
    public double? TickerX { get; set; }
    public double? TickerY { get; set; }
    /// <summary>Options › In game › Rare drops: the chime on a rare drop or a watched item (price alerts chime either way).</summary>
    public bool Chime { get; set; } = true;
    /// <summary>The chime's volume, 0 to 1.</summary>
    public double ChimeVolume { get; set; } = 0.6;
    /// <summary>
    /// The "That was wrong" shortcut, as "Ctrl+Shift+J"; empty (the default) is off. Kept here rather than in UserPrefs,
    /// which belongs to the counting side.
    /// </summary>
    public string MarkWrongKey { get; set; } = "";
    /// <summary>The placement guide is owed: set when the first run's setup check closes, cleared once it has opened over the game.</summary>
    public bool PlaceGuideDue { get; set; }

    public const double MinScale = 0.7, MaxScale = 2;

    static string PathFor() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KinahLedger", "hud.json");

    /// <param name="existingInstall">The tracker ran here before this file existed: the panel stays draggable, as it was.</param>
    /// <param name="remember">Writes the first choice down at once, so a later start makes the same one (never for layout renders).</param>
    public static HudPrefs Load(bool existingInstall, bool remember)
    {
        try
        {
            var p = PathFor();
            if (File.Exists(p) && JsonSerializer.Deserialize<HudPrefs>(File.ReadAllText(p)) is { } saved) return saved;
        }
        catch (Exception) { /* corrupt: decide again */ }
        var fresh = new HudPrefs { Locked = !existingInstall };
        if (remember) fresh.Save();
        return fresh;
    }

    public void Save()
    {
        try { Directory.CreateDirectory(Path.GetDirectoryName(PathFor())!); File.WriteAllText(PathFor(), JsonSerializer.Serialize(this)); }
        catch (Exception) { /* read-only profile */ }
    }
}

/// <summary>
/// The placement guide's snap spots (Ui/PlacementGuide.cs), and the outlines of the game's own parts it draws, all in screen
/// pixels from the game window's rect and the readers' own regions (TargetReader, EventHeader, LootPopup, ChatLocator), so
/// they follow the game on any screen. Each spot is where the panel's window goes: its top-left and its size.
/// </summary>
public static class HudSpots
{
    public const string Top = "top", Minimap = "minimap", Target = "target";

    public sealed record Spot(string Key, string Name);

    /// <summary>The guide's chooser, in its order: the first start's spot, the right edge under the minimap, left of the target bar.</summary>
    public static readonly Spot[] All = { new(Top, "Top centre"), new(Minimap, "Under minimap"), new(Target, "Left of target") };

    /// <summary>
    /// The game's target bar (its name and health bar), inside the band the target reader reads (TargetReader.Strip). The
    /// game draws its interface to the window's height, so the bar is measured in heights: 0.36 across, from 0.03 to
    /// 0.088 down (Steven's 5120×2160 frame of 2026-10-23, where the mock outlines it).
    /// </summary>
    public static System.Drawing.Rectangle TargetBar(System.Drawing.Rectangle game)
    {
        var strip = KinahLedger.Ocr.TargetReader.Strip(game);
        int h = game.Height, w = Math.Min(strip.Width, (int)(h * 0.36));
        int top = Math.Min(strip.Bottom - 1, strip.Top + (int)(h * 0.03)), bottom = Math.Min(strip.Bottom, strip.Top + (int)(h * 0.088));
        return new(game.Left + (game.Width - w) / 2, top, w, Math.Max(1, bottom - top));
    }

    /// <summary>The minimap and its title: the right edge above the event header, which the game draws just under the map (EventHeader.Region).</summary>
    public static System.Drawing.Rectangle MinimapBox(System.Drawing.Rectangle game)
    {
        var header = KinahLedger.Ocr.EventHeader.Region(game);
        return new(header.Left, game.Top, header.Width, Math.Max(1, header.Top - game.Top));
    }

    /// <summary>
    /// The chat box where the game puts it by default (the reader's own area is used once it has found the chat): the
    /// bottom-left corner, as wide as the reader's shortest chat (ChatLocator.MinChatWidth) and a fifth of the height.
    /// </summary>
    public static System.Drawing.Rectangle ChatBox(System.Drawing.Rectangle game)
    {
        int h = game.Height;
        return new(game.Left + (int)(h * 0.04), game.Bottom - (int)(h * 0.26), Math.Min(game.Width / 2, KinahLedger.Ocr.ChatLocator.MinChatWidth(game)), (int)(h * 0.20));
    }

    /// <summary>
    /// Where the panel's window goes for a snap spot, in pixels: the panel is <paramref name="panel"/> big and
    /// <paramref name="dpi"/> pixels to a DIP, and it keeps 12 DIPs from the edges, as the first start's top centre does
    /// (HudWindow.PlaceTopCentre). Always inside the game window.
    /// </summary>
    public static System.Drawing.Rectangle At(string key, System.Drawing.Rectangle game, System.Drawing.Size panel, double dpi = 1)
    {
        int m = (int)Math.Round(12 * dpi), w = panel.Width, h = panel.Height;
        int x, y;
        switch (key)
        {
            case Minimap: { var map = MinimapBox(game); x = map.Right - m - w; y = map.Bottom + m; break; }
            case Target: { var bar = TargetBar(game); x = bar.Left - m - w; y = game.Top + m; break; }
            default: x = game.Left + (game.Width - w) / 2; y = game.Top + m; break;
        }
        x = Math.Clamp(x, game.Left, Math.Max(game.Left, game.Right - w));
        y = Math.Clamp(y, game.Top, Math.Max(game.Top, game.Bottom - h));
        return new(x, y, w, h);
    }
}
