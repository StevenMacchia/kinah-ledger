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
    /// <summary>
    /// The meter over the game (Steven, 2026-11-15: "a toggle in the parser table directly for 'Always on' or 'Only boss
    /// fights'"): off (Only boss fights, the default) it shows in boss fights and dungeons; on (Always on) it is always up,
    /// starting from 0 when a boss is pulled, when you enter a dungeon, and outside dungeons after two minutes without
    /// fighting (Parser/BossParser.cs, AlwaysLive). What is kept stays the mode's either way.
    /// </summary>
    public bool AlwaysOn { get; set; }
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
    /// <summary>Kills near you on boss pulls and runs (off by default: minimal).</summary>
    public bool KillsNear { get; set; }
    /// <summary>
    /// The group's deaths on boss pulls and runs, the list filters and the explanations on hover: on by default since the
    /// parser's honest pass (the spec's serious defaults); a player from before moves to them once (<see cref="HonestSettled"/>).
    /// </summary>
    public bool Deaths { get; set; } = true;
    public bool Filters { get; set; } = true;
    public bool Hovers { get; set; } = true;
    /// <summary>
    /// The one-time move to the honest pass's defaults is done. A file from before: deaths come on (they were Kills and
    /// deaths' half, on for a player who had that on), and filters and hovers come on unless the player had turned any of
    /// the three extras on, a choice of their own that is kept as it is.
    /// </summary>
    public bool HonestSettled { get; set; }
    /// <summary>Member figures: "boss" (damage to the boss only, the default) or "all" (every target, adds too). The group card's switch.</summary>
    public string Targets { get; set; } = "boss";
    /// <summary>The overlay's meter expanded (Parser v2): everyone, with the damage (and crit and the DPS over time once kept); off, four rows. A click on the meter switches it.</summary>
    public bool Expanded { get; set; }
    /// <summary>
    /// Which hit kind the Parser shows as crit ("none", "result3" or "mark08", the two candidates the hits carry), and as a
    /// back attack ("none" or "side1"). No switch in Settings. Null, as every player has it, means the release's default
    /// (<see cref="CritDefault"/>, <see cref="BackDefault"/>): null is never written, so a release that changes a default
    /// reaches every player, saved settings or not. A value in the file is a choice and stays.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] public string? CritSource { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] public string? BackSource { get; set; }
    /// <summary>The defaults a release ships: the morning's flip is one of these lines.</summary>
    public const string CritDefault = "none", BackDefault = "none";
    [System.Text.Json.Serialization.JsonIgnore] public string CritNow => CritSource ?? CritDefault;
    [System.Text.Json.Serialization.JsonIgnore] public string BackNow => BackSource ?? BackDefault;

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
            if (File.Exists(PathNow) && Read(File.ReadAllText(PathNow)) is { } saved)
            {
                // Group details on for everyone (Steven, 2026-11-14): a file from before has it off by the old default, so it
                // moves to on once, as HudPrefs moved the panel's look; DetailsSettled marks the move done, so a later off stays off
                // the honest pass's move is made on every read until the player saves a Parser setting (the same each time,
                // from the file as written), so reading never writes: a self-test reading the player's file leaves it alone
                bool detailsMove = !saved.DetailsSettled;
                Settle(saved);
                if (detailsMove) saved.Save();
                return saved;
            }
        }
        catch (Exception) { /* a broken file: the defaults */ }
        return new() { DetailsSettled = true, HonestSettled = true };
    }

    /// <summary>The one-time moves of a file from before (Group details on; the honest pass's defaults); true when one was made.</summary>
    internal static bool Settle(ParserPrefs p)
    {
        bool moved = false;
        if (!p.DetailsSettled) { p.Details = true; p.DetailsSettled = true; moved = true; }
        if (!p.HonestSettled)
        {
            // in a file from before, Deaths, Filters and Hovers read as this build's defaults (on) when missing, so their
            // values there say nothing: what the player chose is in the old switches (off by default) as written
            bool chose = p.KillsNear || p.OldFilters == true || p.OldHovers == true;
            p.Deaths = true;
            if (!chose) { p.Filters = true; p.Hovers = true; }
            else { p.Filters = p.OldFilters == true; p.Hovers = p.OldHovers == true; }
            p.HonestSettled = true; moved = true;
        }
        return moved;
    }

    // what a file from before the honest pass said for Filters and Hovers, as written (null: not in it); the properties
    // themselves read the new default when a file lacks them, so Read takes these from the file's text
    [System.Text.Json.Serialization.JsonIgnore] bool? OldFilters { get; set; }
    [System.Text.Json.Serialization.JsonIgnore] bool? OldHovers { get; set; }

    static ParserPrefs Read(string json)
    {
        var p = JsonSerializer.Deserialize<ParserPrefs>(json) ?? new();
        if (!p.HonestSettled)
        {
            using var doc = JsonDocument.Parse(json);
            bool? B(string name) => doc.RootElement.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : null;
            p.OldFilters = B("Filters"); p.OldHovers = B("Hovers");
        }
        return p;
    }

    /// <summary>For parser-test: the one-time moves read from a file's text (Load's rule, without the file).</summary>
    internal static ParserPrefs FromText(string json)
    {
        var p = Read(json);
        Settle(p);
        return p;
    }

    public void Save()
    {
        try { Directory.CreateDirectory(CrashLog.Dir); File.WriteAllText(PathNow, JsonSerializer.Serialize(this, Json)); }
        catch (Exception ex) { CrashLog.Write("parser settings", ex); }
    }
}
