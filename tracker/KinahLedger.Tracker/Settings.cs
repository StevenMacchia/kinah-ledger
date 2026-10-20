using System.Text.Json;

namespace KinahLedger;

/// <summary>settings.json beside the exe. Every field has a working default so the file is optional.</summary>
public sealed class Settings
{
    /// <summary>Substrings matched against process names (case-insensitive) to find the game client.</summary>
    public string[] ProcessHints { get; set; } = ["aion"];
    /// <summary>Remote ports to ignore: the client also keeps HTTPS connections open for the launcher and web services.</summary>
    public int[] IgnorePorts { get; set; } = [443, 80];
    public string Region { get; set; } = "na_e";
    public string GearBracket { get; set; } = "1400-1599";
    public string ItemsFile { get; set; } = "data/tw-items.json";
    public string OpcodesFile { get; set; } = "opcodes.json";
    public string ZonesFile { get; set; } = "zones.json";
    public FramingSettings Framing { get; set; } = new();
    public UploadSettings Upload { get; set; } = new();
    public OcrSettings Ocr { get; set; } = new();

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

/// <summary>How the server's byte stream splits into messages. "raw" until the real layout is known from recordings.</summary>
public sealed class FramingSettings
{
    public string Mode { get; set; } = "raw";          // raw | length
    public int HeaderOffset { get; set; } = 0;         // bytes before the length field
    public int LengthBytes { get; set; } = 2;
    public bool LittleEndian { get; set; } = true;
    public bool LengthIncludesHeader { get; set; } = true;
    public int OpcodeBytes { get; set; } = 2;          // opcode follows the length field
}

/// <summary>Screen-reading mode: reads the chat's loot lines off the screen with Windows' built-in text recognition.</summary>
public sealed class OcrSettings
{
    /// <summary>Chat text is small; reading it at 2x is far more accurate.</summary>
    public int Scale { get; set; } = 2;
    public int IntervalMs { get; set; } = 700;
    /// <summary>What you're farming, for the shared totals. Override per run with --activity.</summary>
    public string Activity { get; set; } = "unknown";
    public bool Hud { get; set; } = true;
}

public sealed class UploadSettings
{
    /// <summary>On by default; the first-run notice in `run` says exactly what is sent.</summary>
    public bool Enabled { get; set; } = true;
    /// <summary>Empty until the site's ingest endpoint exists, so nothing is sent yet.</summary>
    public string Endpoint { get; set; } = "";
}
