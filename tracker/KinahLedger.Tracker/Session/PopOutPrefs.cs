using System.Text.Json;

namespace KinahLedger.Session;

/// <summary>
/// The pop-out windows (Steven, 2026-11-18: "Yes, build pop-outs"): the DPS meter and the Applicants list, each in a desktop
/// window of its own for a second screen, for a game in exclusive fullscreen where nothing can draw over it. Per window:
/// whether it was open (it opens again at start), stay on top, and where it sat and how wide it was. popouts.json beside the
/// other settings; saved as they change.
/// </summary>
public sealed class PopOutPrefs
{
    /// <summary>One window's choices.</summary>
    public sealed class Spot
    {
        /// <summary>Open when the tracker last closed: opened again at start.</summary>
        public bool Open { get; set; }
        /// <summary>Kept above other windows (off by default: a desktop window like any other).</summary>
        public bool OnTop { get; set; }
        public double? X { get; set; }
        public double? Y { get; set; }
        /// <summary>Its width; the height follows what it shows.</summary>
        public double? W { get; set; }
    }

    public Spot Meter { get; set; } = new();
    public Spot Applicants { get; set; } = new();

    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    [System.Text.Json.Serialization.JsonIgnore] public string? Path { get; private set; }

    public static string DefaultPath => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KinahLedger", "popouts.json");

    public static PopOutPrefs Load(string? path = null)
    {
        path ??= DefaultPath;
        PopOutPrefs p = new();
        try { if (File.Exists(path)) p = JsonSerializer.Deserialize<PopOutPrefs>(File.ReadAllText(path), Json) ?? new(); }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { CrashLog.Note("popouts.json not read: " + ex.Message); }
        p.Meter ??= new(); p.Applicants ??= new();
        p.Path = path;
        return p;
    }

    /// <summary>Settings that are never saved (renders, self-tests).</summary>
    public static PopOutPrefs Defaults() => new();

    public void Save()
    {
        if (Path == null) return;
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            var tmp = Path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, Json));
            File.Move(tmp, Path, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { CrashLog.Note("popouts.json not saved: " + ex.Message); }
    }
}
