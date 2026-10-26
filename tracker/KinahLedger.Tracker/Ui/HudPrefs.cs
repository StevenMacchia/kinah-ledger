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
