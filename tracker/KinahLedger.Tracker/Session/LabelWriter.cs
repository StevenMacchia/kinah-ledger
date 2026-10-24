using System.Drawing;
using System.Drawing.Imaging;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace KinahLedger.Session;

/// <summary>
/// One training pair, as data (no pixels): what a reader read, and the truth two witnesses settled on. Written as one
/// JSON line; the names are short because thousands of them go up gzipped. Never a player name, never a chat line that
/// isn't the player's own pickup, never the install's link code.
/// </summary>
public sealed record LabelPair(
    [property: JsonPropertyName("id")] string Id,
    // kina-line, loot-row, chat-item, card-slot or gather.
    [property: JsonPropertyName("kind")] string Kind,
    // The text as the reader read it (a timestamp in front dropped); for a card slot, the count as read.
    [property: JsonPropertyName("text")] string Text,
    // The icon fingerprint (a loot row's 64-bit hash in hex) or icon key (a card slot), if any.
    [property: JsonPropertyName("icon")] string? Icon,
    // The name decoder's read of the name crop, if it ran (filled when the batch is sent, off the counting path).
    [property: JsonPropertyName("dec")] string? Decoded,
    // The truth: the item's id (0 for Kina), its name and the quantity.
    [property: JsonPropertyName("item")] long Item,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("qty")] long Qty,
    // The witnesses that agreed, e.g. ["chat", "kinah counter"]; always two or more.
    [property: JsonPropertyName("wit")] string[] Witnesses,
    [property: JsonPropertyName("ver")] string Version,
    // The game window's size and the chat's text height in pixels (0 when not known).
    [property: JsonPropertyName("sw")] int ScreenW,
    [property: JsonPropertyName("sh")] int ScreenH,
    [property: JsonPropertyName("tpx")] int TextPx,
    [property: JsonPropertyName("t")] string At,
    // The crops kept with it ("name", "icon", "slot"), uploaded after the pair with its id.
    [property: JsonPropertyName("crops")] string[] Crops);

/// <summary>
/// "Help improve counting" (Options › Sharing, on by default, said once on first run with a one-click off): every count
/// a second witness confirmed becomes a training pair for the matcher, the name decoder and the icon index, kept in
/// %LocalAppData%\KinahLedger\labels\pending.jsonl (2 MB at most) with small crops of the item's name words, icon or
/// reward slot beside it (<see cref="Session.Crops"/>, 60 an hour at most), and sent in batches by Upload/LabelClient.cs.
/// Off: nothing is written, nothing is sent, and the folder is emptied. Whole frames are never kept here; they stay
/// under "Send report pictures", unchanged.
/// </summary>
public sealed class LabelWriter
{
    public static readonly string[] Kinds = ["kina-line", "loot-row", "chat-item", "card-slot", "gather"];
    public const long MaxFileBytes = 2 * 1024 * 1024;
    public const int CropsPerHour = 60;
    public const long MaxCropFolderBytes = 8 * 1024 * 1024;
    /// <summary>The same read of the same truth within this many seconds is one pair (a row stays up and is read again).</summary>
    public const double RepeatSeconds = 20;

    public static string DefaultDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KinahLedger", "labels");

    /// <summary>The live tracker's writer. Off until the window turns it on from the player's choice (tests and replays write nothing).</summary>
    public static LabelWriter Shared { get; set; } = new(DefaultDir);

    public string Dir { get; }
    public string PendingPath => Path.Combine(Dir, "pending.jsonl");
    public string SendingPath => Path.Combine(Dir, "sending.jsonl");
    public string CropDir => Path.Combine(Dir, "crops");

    public LabelWriter(string dir) { Dir = dir; }

    readonly object _lock = new();
    bool _enabled;
    /// <summary>The player's choice (and sharing on): only then is anything written or sent.</summary>
    public bool Enabled { get { lock (_lock) return _enabled; } }
    public int Written { get; private set; }
    public int Dropped { get; private set; }
    public int CropsKept { get; private set; }
    readonly Dictionary<string, DateTime> _recent = new();
    readonly Queue<DateTime> _cropTimes = new();

    /// <summary>Turns the writer on or off. Off empties the folder: the local file is not kept.</summary>
    public void SetEnabled(bool on)
    {
        lock (_lock)
        {
            _enabled = on;
            if (!on) Purge();
        }
    }

    /// <summary>Deletes everything the writer kept.</summary>
    public void Purge()
    {
        lock (_lock)
        {
            _recent.Clear();
            try { if (Directory.Exists(Dir)) Directory.Delete(Dir, true); } catch (Exception ex) { CrashLog.Write("labels purge", ex); }
        }
    }

    static readonly Regex Stamp = new(@"^\s*\[[\d:\s]+\]\s*", RegexOptions.Compiled);

    /// <summary>
    /// Text that may leave the PC: one line, at most 96 characters, no sender bracket (a "[Name]" is a player speaking),
    /// no "Name:" chat form. A chat line must also read as the player's own pickup ("Acquired ...", "... acquired.").
    /// Null when refused.
    /// </summary>
    public static string? SafeText(string kind, string raw)
    {
        var t = Stamp.Replace((raw ?? "").Replace('\r', ' ').Replace('\n', ' '), "").Trim();
        if (t.Length == 0 || t.Length > 96) return null;
        if (t.Contains('[') || t.Contains(']') || t.Contains('<') || t.Contains('>') || Regex.IsMatch(t, @"^\S{1,24}\s*:\s")) return null;
        if (kind is "kina-line" or "chat-item")
        {
            if (!Regex.IsMatch(t, @"acqu", RegexOptions.IgnoreCase)) return null;
            if (Ocr.ChatParser.SomeoneElses(t)) return null;   // another player's loot names them
        }
        return t;
    }

    /// <summary>
    /// Writes one pair when it may be: the writer is on, the kind is known, two different witnesses agreed, the text is
    /// safe and the truth is an item and a quantity. Crops are kept only through <see cref="Crops"/>'s rule and the hourly
    /// cap. Returns the pair written, or null (nothing written). The caller keeps ownership of the bitmaps.
    /// </summary>
    public LabelPair? Write(string kind, string text, string? icon, long item, string name, long qty, IEnumerable<string> witnesses,
        Size screen, int textPx, DateTime at, IReadOnlyList<(string Part, Bitmap Crop, CropKind Kind, Size Span)>? crops = null)
    {
        if (!Enabled) return null;
        if (Array.IndexOf(Kinds, kind) < 0 || qty <= 0 || item < 0 || string.IsNullOrWhiteSpace(name)) return null;
        var wit = witnesses.Select(w => w.Trim()).Where(w => w.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (wit.Length < 2) return null;
        var safe = SafeText(kind, text);
        if (safe == null) return null;
        var key = kind + "|" + safe.ToLowerInvariant() + "|" + item + "|" + qty;
        lock (_lock)
        {
            if (!_enabled) return null;
            if (_recent.TryGetValue(key, out var seen) && Math.Abs((at - seen).TotalSeconds) < RepeatSeconds) return null;
            _recent[key] = at;
            if (_recent.Count > 512) foreach (var k in _recent.Where(kv => Math.Abs((at - kv.Value).TotalSeconds) >= RepeatSeconds).Select(kv => kv.Key).ToList()) _recent.Remove(k);
            try
            {
                Directory.CreateDirectory(Dir);
                var fi = new FileInfo(PendingPath);
                if (fi.Exists && fi.Length > MaxFileBytes) { Dropped++; return null; }
                var id = Guid.NewGuid().ToString("N")[..16];
                var kept = crops == null ? new List<string>() : KeepCrops(id, crops, at);
                var pair = new LabelPair(id, kind, safe, string.IsNullOrEmpty(icon) ? null : icon, null, item, name.Trim(), qty, wit,
                    Upload.RunPayload.AppVersion, screen.Width, screen.Height, textPx, at.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"), kept.ToArray());
                File.AppendAllText(PendingPath, JsonSerializer.Serialize(pair) + "\n");
                Written++;
                return pair;
            }
            catch (Exception ex) { CrashLog.Write("label writer", ex); return null; }
        }
    }

    List<string> KeepCrops(string id, IReadOnlyList<(string Part, Bitmap Crop, CropKind Kind, Size Span)> crops, DateTime at)
    {
        var kept = new List<string>();
        while (_cropTimes.Count > 0 && (at - _cropTimes.Peek()).TotalHours >= 1) _cropTimes.Dequeue();
        foreach (var (part, bmp, kind, span) in crops)
        {
            if (_cropTimes.Count >= CropsPerHour) break;
            if (part is not ("name" or "icon" or "slot")) continue;
            if (!Crops.IsAllowedCut(bmp.Size, kind, span)) continue;
            Directory.CreateDirectory(CropDir);
            if (FolderBytes(CropDir) > MaxCropFolderBytes) break;
            bmp.Save(Path.Combine(CropDir, $"{id}.{part}.png"), ImageFormat.Png);
            _cropTimes.Enqueue(at);
            kept.Add(part);
            CropsKept++;
        }
        return kept;
    }

    static long FolderBytes(string dir)
    {
        try { return new DirectoryInfo(dir).EnumerateFiles().Sum(f => f.Length); } catch (Exception) { return 0; }
    }

    /// <summary>
    /// The next batch to send: what a failed send left (sending.jsonl) or up to <paramref name="max"/> pending lines, moved
    /// to sending.jsonl so pairs written meanwhile wait for the next one. Empty when there is nothing (or the writer is off).
    /// </summary>
    public List<LabelPair> TakeBatch(int max = 500)
    {
        lock (_lock)
        {
            if (!_enabled) return new();
            try
            {
                if (!File.Exists(SendingPath))
                {
                    if (!File.Exists(PendingPath)) return new();
                    var all = File.ReadAllLines(PendingPath).Where(l => l.Length > 0).ToList();
                    File.WriteAllLines(SendingPath, all.Take(max));
                    var rest = all.Skip(max).ToList();
                    if (rest.Count > 0) File.WriteAllLines(PendingPath, rest); else File.Delete(PendingPath);
                }
                var list = new List<LabelPair>();
                foreach (var l in File.ReadLines(SendingPath))
                    try { if (JsonSerializer.Deserialize<LabelPair>(l) is { } p) list.Add(p); } catch (JsonException) { /* a torn line is left out */ }
                return list;
            }
            catch (Exception ex) { CrashLog.Write("labels batch", ex); return new(); }
        }
    }

    /// <summary>The batch went up (or the site refused its shape for good): it is let go, crops not sent with it too.</summary>
    public void BatchDone(bool keepCrops)
    {
        lock (_lock)
        {
            try
            {
                if (!keepCrops && File.Exists(SendingPath))
                    foreach (var l in File.ReadLines(SendingPath))
                        try { if (JsonSerializer.Deserialize<LabelPair>(l) is { } p) DeleteCrops(p.Id); } catch (JsonException) { }
                File.Delete(SendingPath);
            }
            catch (Exception ex) { CrashLog.Write("labels batch done", ex); }
        }
    }

    /// <summary>A crop file of a pair, or null.</summary>
    public string? CropPath(string id, string part)
    {
        var p = Path.Combine(CropDir, $"{id}.{part}.png");
        return File.Exists(p) ? p : null;
    }

    public void DeleteCrops(string id)
    {
        foreach (var part in new[] { "name", "icon", "slot" })
            try { var p = Path.Combine(CropDir, $"{id}.{part}.png"); if (File.Exists(p)) File.Delete(p); } catch (Exception) { }
    }
}
