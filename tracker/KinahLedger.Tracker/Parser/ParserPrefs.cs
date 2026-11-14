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
    /// <summary>The overlay's shortcut ("Ctrl+Shift+P"; "Off" for none).</summary>
    public string Hotkey { get; set; } = DefaultHotkey;
    /// <summary>What the Parser keeps: "boss" (Bosses only, the default), "run" (Full run), "fight" (Every fight).</summary>
    public string Mode { get; set; } = "boss";
    /// <summary>Which records the list shows ("boss", "run", "fight"); the mode's own when empty.</summary>
    public string ListMode { get; set; } = "";
    /// <summary>
    /// Minimal by default (Steven, 2026-11-14): the page and the overlay show the boss, the clock and each member's DPS;
    /// these switches (Options › In game › Parser) add each member's class, Combat Power, item level and damage (Details),
    /// kills near you and the group's deaths (KillsNear), the list's kind and boss pickers (Filters), and the explanations
    /// on hover (Hovers).
    /// </summary>
    public bool Details { get; set; }
    /// <summary>The Parser in its own overlay window (draggable, its own place, click-through) instead of the Kinah overlay's section above the Kinah line (Steven, 2026-11-14: "expand by default but give option in settings to separate"). Off by default.</summary>
    public bool Separate { get; set; }
    /// <summary>DPS over each member's active time (first hit to last, pauses over 20 s left out) instead of over the fight's length (the default).</summary>
    public bool DpsActive { get; set; }
    public bool KillsNear { get; set; }
    public bool Filters { get; set; }
    public bool Hovers { get; set; }

    /// <summary>Options saved the Parser's switches: the window reads them again.</summary>
    public static event Action? Saved;
    public static void RaiseSaved() => Saved?.Invoke();

    /// <summary>The last filters on the Parser page's list.</summary>
    public string KindFilter { get; set; } = "";
    public string BossFilter { get; set; } = "";

    public const string DefaultHotkey = "Ctrl+Shift+P";

    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    static string PathNow => System.IO.Path.Combine(CrashLog.Dir, "parser.json");

    public static ParserPrefs Load()
    {
        try { if (File.Exists(PathNow)) return JsonSerializer.Deserialize<ParserPrefs>(File.ReadAllText(PathNow)) ?? new(); }
        catch (Exception) { /* a broken file: the defaults */ }
        return new();
    }

    public void Save()
    {
        try { Directory.CreateDirectory(CrashLog.Dir); File.WriteAllText(PathNow, JsonSerializer.Serialize(this, Json)); }
        catch (Exception ex) { CrashLog.Write("parser settings", ex); }
    }
}
