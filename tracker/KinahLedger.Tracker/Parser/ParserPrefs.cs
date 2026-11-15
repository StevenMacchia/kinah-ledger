using System.Text.Json;

namespace KinahLedger.Parser;

/// <summary>
/// The Parser's own settings (parser.json in the tracker's folder, for every character): the overlay on or off, where it
/// was dragged, click-through, and its shortcut. Kept apart from ui.json so the Parser never touches the window's settings.
/// </summary>
public sealed class ParserPrefs
{
    /// <summary>The fight overlay over the game (shown in boss fights only).</summary>
    public bool Overlay { get; set; } = true;
    /// <summary>Clicks go through the overlay to the game; off, it can be dragged.</summary>
    public bool ClickThrough { get; set; }
    /// <summary>Where the overlay was dropped, in screen pixels; null until it is dragged once.</summary>
    public int? Left { get; set; }
    public int? Top { get; set; }
    /// <summary>The overlay's shortcut ("Off", the default, for none; e.g. "Ctrl+Shift+P").</summary>
    public string Hotkey { get; set; } = DefaultHotkey;
    /// <summary>What the Parser keeps: "boss" (Bosses only, the default), "run" (Full run), "fight" (Every fight).</summary>
    public string Mode { get; set; } = "boss";
    /// <summary>Which records the list shows ("boss", "run", "fight"); the mode's own when empty.</summary>
    public string ListMode { get; set; } = "";
    /// <summary>
    /// Minimal by default (Steven, 2026-11-14): the page and the overlay show the boss, the clock and each member's DPS;
    /// these switches (Options › In game › Parser) add each member's class, Combat Power, item level and damage (Details),
    /// kills near you and the group's deaths (KillsNear), the list's kind and boss pickers (Filters), and the explanations
    /// on hover (Hovers). Group details is on by default since the parser polish (Steven, 2026-11-14).
    /// </summary>
    public bool Details { get; set; } = true;
    /// <summary>
    /// The one-time move of Group details to on (the parser polish) is done; see Load. False in a file written before it,
    /// so that player's Details moves to on once; from then on their switch is kept, on or off.
    /// </summary>
    public bool DetailsSettled { get; set; }
    /// <summary>The Parser in its own overlay window (draggable, its own place, click-through) instead of the Kinah overlay's section above the Kinah line (Steven, 2026-11-14: "expand by default but give option in settings to separate"). Off by default.</summary>
    public bool Separate { get; set; }
    /// <summary>DPS over each member's active time (first hit to last, pauses over 20 s left out) instead of over the fight's length (the default).</summary>
    public bool DpsActive { get; set; }
    /// <summary>Your DPS small on the Kinah overlay's line while fighting in the open world or the Abyss (on by default: Steven asked for it).</summary>
    public bool FarmDps { get; set; } = true;
    /// <summary>
    /// The meter's width at 100%, in DIPs (Steven, 2026-11-14: "maybe an option to make it wider?"): 330 by default, 300 to
    /// 640; set by dragging the meter's left or right edge, or Settings › In game › Parser › Width.
    /// </summary>
    public double Width { get; set; } = 330;
    public bool KillsNear { get; set; }
    public bool Filters { get; set; }
    public bool Hovers { get; set; }
    /// <summary>The overlay's meter expanded (Parser v2): everyone, with the damage (and crit and the DPS over time once kept); off, four rows. A click on the meter switches it.</summary>
    public bool Expanded { get; set; }

    /// <summary>Options saved the Parser's switches: the window reads them again.</summary>
    public static event Action? Saved;
    public static void RaiseSaved() => Saved?.Invoke();

    /// <summary>The last filters on the Parser page's list.</summary>
    public string KindFilter { get; set; } = "";
    public string BossFilter { get; set; } = "";

    // no system-wide shortcut by default (2026-11-14): one would take the keys from every other app on the PC; the player sets one in Options
    public const string DefaultHotkey = "Off";

    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    static string PathNow => System.IO.Path.Combine(CrashLog.Dir, "parser.json");

    public static ParserPrefs Load()
    {
        try
        {
            if (File.Exists(PathNow) && JsonSerializer.Deserialize<ParserPrefs>(File.ReadAllText(PathNow)) is { } saved)
            {
                // Group details on for everyone (Steven, 2026-11-14): a file from before has it off by the old default, so it
                // moves to on once, as HudPrefs moved the panel's look; DetailsSettled marks the move done, so a later off stays off
                if (!saved.DetailsSettled) { saved.Details = true; saved.DetailsSettled = true; saved.Save(); }
                return saved;
            }
        }
        catch (Exception) { /* a broken file: the defaults */ }
        return new() { DetailsSettled = true };
    }

    /// <summary>For parser-test: the one-time move read from a file's text (Load's rule, without the file).</summary>
    internal static ParserPrefs FromText(string json)
    {
        var p = JsonSerializer.Deserialize<ParserPrefs>(json) ?? new();
        if (!p.DetailsSettled) { p.Details = true; p.DetailsSettled = true; }
        return p;
    }

    public void Save()
    {
        try { Directory.CreateDirectory(CrashLog.Dir); File.WriteAllText(PathNow, JsonSerializer.Serialize(this, Json)); }
        catch (Exception ex) { CrashLog.Write("parser settings", ex); }
    }
}
