using System.Drawing;
using System.Drawing.Imaging;
using System.Text.Json;

namespace KinahLedger.Session;

/// <summary>
/// "Report a miscount" (2026-10-23): the last minute of what the tracker read and counted, kept in memory so that the
/// moment a count looks wrong it can be saved as it was, and a fix measured on it later. Always kept: every chat and
/// loot list read as text (the same lines trace.log holds) and every pickup as counted. Kept only when the player said
/// yes (<see cref="KeepFrames"/>): the pictures those reads came from, since the chat can show other players' names and
/// messages. A picture is kept only when it changed since the last one of its kind, and the loot list (the world shows
/// behind it) at most once a second, within <see cref="MaxPictureBytes"/>.
///
/// Nothing leaves the PC. <see cref="Write"/> makes a folder under reports/ beside the logs: the pictures with an
/// index, trace.jsonl in trace.log's own form (so `replay` runs it as it is), what was counted, the tracker's state and
/// the end of reads.log. `report-replay` reads the pictures again with the current reader.
/// </summary>
public sealed class ReportBuffer
{
    public const double KeepSeconds = 60;
    public const long MaxPictureBytes = 80L * 1024 * 1024;

    /// <summary>Keep the pictures too (UserPrefs.ReportFrames; off until the player says yes).</summary>
    public static bool KeepFrames { get; set; }

    sealed record Shot(DateTime At, string Kind, Point Origin, byte[] Png);
    readonly List<Shot> _shots = new();
    readonly List<(DateTime At, string Kind, string[] Lines)> _reads = new();
    readonly List<(DateTime At, string What, string Via)> _counted = new();
    readonly Dictionary<string, (ulong Hash, DateTime At)> _last = new();
    readonly object _lock = new();
    long _bytes;

    /// <summary>
    /// One capture as it was read: kept as a PNG when pictures are on and it changed since the last of its kind (and,
    /// with <paramref name="minGapSeconds"/>, no sooner than that after it). <paramref name="origin"/> is where its top-left sat on screen.
    /// </summary>
    public void Picture(string kind, Bitmap bmp, Point origin, DateTime at, double minGapSeconds = 0)
    {
        if (!KeepFrames) return;
        ulong h = Hash(bmp);
        lock (_lock)
        {
            if (_last.TryGetValue(kind, out var l) && (l.Hash == h || (at - l.At).TotalSeconds < minGapSeconds)) return;
            _last[kind] = (h, at);
        }
        byte[] png;
        using (var ms = new MemoryStream()) { bmp.Save(ms, ImageFormat.Png); png = ms.ToArray(); }
        lock (_lock) { _shots.Add(new Shot(at, kind, origin, png)); _bytes += png.Length; Trim(at); }
    }

    /// <summary>One read's lines, as trace.log keeps them ("chat", or "list" with rows as "463 Kina" and "Name x2").</summary>
    public void Lines(string kind, IEnumerable<string> lines, DateTime at)
    {
        lock (_lock) { _reads.Add((at, kind, lines.ToArray())); Trim(at); }
    }

    /// <summary>A pickup as counted: what, and how it was read (GameEvent.Via).</summary>
    public void Counted(DateTime at, string what, string via)
    {
        lock (_lock) { _counted.Add((at, what, via)); Trim(at); }
    }

    /// <summary>How much is held now: pictures, reads, pickups.</summary>
    public (int Pictures, int Reads, int Counted) Held { get { lock (_lock) return (_shots.Count, _reads.Count, _counted.Count); } }

    void Trim(DateTime now)
    {
        var cut = now.AddSeconds(-KeepSeconds);
        while (_shots.Count > 0 && (_shots[0].At < cut || _bytes > MaxPictureBytes)) { _bytes -= _shots[0].Png.Length; _shots.RemoveAt(0); }
        _reads.RemoveAll(r => r.At < cut);
        _counted.RemoveAll(c => c.At < cut);
    }

    /// <summary>
    /// Saves what is held to a new folder under <paramref name="root"/>/reports and returns it. <paramref name="summary"/>
    /// is the tracker's state in words; <paramref name="logTail"/> the end of reads.log.
    /// </summary>
    public string Write(string root, string summary, IEnumerable<string> logTail, DateTime now)
    {
        List<Shot> shots; List<(DateTime At, string Kind, string[] Lines)> reads; List<(DateTime At, string What, string Via)> counted;
        lock (_lock) { Trim(now); shots = _shots.ToList(); reads = _reads.ToList(); counted = _counted.ToList(); }
        var dir = Path.Combine(root, "reports", now.ToLocalTime().ToString("yyyyMMdd-HHmmss"));
        for (int i = 2; Directory.Exists(dir); i++) dir = Path.Combine(root, "reports", now.ToLocalTime().ToString("yyyyMMdd-HHmmss") + "-" + i);
        Directory.CreateDirectory(dir);
        if (shots.Count > 0)
        {
            var frames = Path.Combine(dir, "frames");
            Directory.CreateDirectory(frames);
            var index = new List<string>();
            foreach (var s in shots)
            {
                var file = $"{s.Kind}-{s.At.ToLocalTime():HHmmss-fff}.png";
                for (int i = 2; File.Exists(Path.Combine(frames, file)); i++) file = $"{s.Kind}-{s.At.ToLocalTime():HHmmss-fff}-{i}.png";
                File.WriteAllBytes(Path.Combine(frames, file), s.Png);
                index.Add(JsonSerializer.Serialize(new { t = s.At.ToString("O"), k = s.Kind, f = "frames/" + file, x = s.Origin.X, y = s.Origin.Y }));
            }
            File.WriteAllLines(Path.Combine(dir, "frames.jsonl"), index);
        }
        File.WriteAllLines(Path.Combine(dir, "trace.jsonl"), reads.Select(r => JsonSerializer.Serialize(new { t = r.At.ToString("O"), k = r.Kind, l = r.Lines })));
        File.WriteAllLines(Path.Combine(dir, "counted.jsonl"), counted.Select(c => JsonSerializer.Serialize(new { t = c.At.ToString("O"), what = c.What, via = c.Via })));
        File.WriteAllText(Path.Combine(dir, "summary.txt"), summary);
        File.WriteAllLines(Path.Combine(dir, "reads-log.txt"), logTail);
        File.WriteAllText(Path.Combine(dir, "README.txt"),
            "Kinah Ledger miscount report: the last minute before it was saved, on this PC only.\r\n\r\n" +
            "summary.txt     the tracker's state (version, chat area, text size, checks so far)\r\n" +
            "counted.jsonl   every pickup counted, and how it was read\r\n" +
            "trace.jsonl     every chat and loot list read as text; `KinahLedger replay trace.jsonl` counts it again\r\n" +
            "reads-log.txt   the end of reads.log: what the tracker noted while reading\r\n" +
            (shots.Count > 0 ? "frames/         the pictures those reads came from (frames.jsonl says when and where);\r\n" +
                               "                `KinahLedger report-replay <this folder>` reads them again with the current reader\r\n" : "") +
            "\r\nThe pictures and the chat lines can show other players' names and messages. Nothing here is sent anywhere;\r\n" +
            "zip the folder yourself to attach it to a bug report, and look through it first.\r\n");
        return dir;
    }

    /// <summary>A quick fingerprint of a picture (every third pixel of every second row), to keep only the ones that changed.</summary>
    static unsafe ulong Hash(Bitmap bmp)
    {
        var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            ulong h = 14695981039346656037UL;
            for (int y = 0; y < bmp.Height; y += 2)
            {
                uint* row = (uint*)((byte*)data.Scan0 + y * data.Stride);
                for (int x = 0; x < bmp.Width; x += 3) { h ^= row[x]; h *= 1099511628211UL; }
            }
            return h;
        }
        finally { bmp.UnlockBits(data); }
    }
}
