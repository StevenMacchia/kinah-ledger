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
