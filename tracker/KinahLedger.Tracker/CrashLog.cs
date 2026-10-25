namespace KinahLedger;

/// <summary>Writes unexpected errors and read traces to %LOCALAPPDATA%\KinahLedger so problems can be diagnosed after the fact.</summary>
public static class CrashLog
{
    /// <summary>Where the logs go instead of the player's own folder: parse-test points it at a scratch folder so its made-up lines never land in reads.log.</summary>
    public static string? DirOverride { get; set; }

    public static string Dir
    {
        get
        {
            var d = DirOverride ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KinahLedger");
            Directory.CreateDirectory(d);
            return d;
        }
    }

    public static string CrashPath => Path.Combine(Dir, "crash.log");
    public static string ReadsPath => Path.Combine(Dir, "reads.log");
    /// <summary>What reads.log became when it was turned over (see <see cref="ReadsMaxBytes"/>); the one before it is replaced.</summary>
    public static string ReadsOldPath => Path.Combine(Dir, "reads.old.log");

    public static void Write(string where, Exception ex)
    {
        try { File.AppendAllText(CrashPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {where}\n{ex}\n\n"); } catch (Exception) { /* nothing left to do */ }
    }

    // ---- reads.log: turned over, never wiped (2026-10-25) ----
    // At 2 MB it used to be emptied, and the evening's decision lines for all 22 runs went with it (250 "crafting panel
    // up/gone" lines and 98 "Show acquired designs only" lines in 36 minutes had filled it). It now becomes reads.old.log,
    // as trace.log becomes trace.old.log, so there are always two files of history; and a line that repeats is written
    // once, then counted (see Burst).

    /// <summary>The size at which reads.log becomes reads.old.log and a new one starts (so up to twice this is kept).</summary>
    public static long ReadsMaxBytes { get; set; } = 5_000_000;
    /// <summary>How long an identical line is held to its first writing (<see cref="NoteBurst"/>) or its first few (<see cref="Note"/>).</summary>
    public static double BurstSeconds { get; set; } = 60;
    /// <summary>How many times an identical <see cref="Note"/> is written in a <see cref="BurstSeconds"/> window before the rest are only counted.</summary>
    public const int FloodFree = 5;

    /// <summary>The time lines are stamped with and bursts are measured by; tests move it.</summary>
    internal static Func<DateTime> Wall { get; set; } = () => DateTime.Now;

    static readonly object ReadsLock = new();

    /// <summary>One identical line in its window: when it was first written, how many were written, how many were held back and when the last was.</summary>
    sealed class Burst { public DateTime FirstAt, LastHeldAt; public int Written, Held; }
    static readonly Dictionary<string, Burst> Bursts = new();
    static string _statPath = "";
    static long _len;
    static DateTime _scannedAt = DateTime.MinValue;

    /// <summary>Lines that tell what the tracker decided or where it was; however often they repeat, none is held back (replays read the zone and reward lines).</summary>
    static bool Exempt(string text) =>
        text.StartsWith("audit:", StringComparison.Ordinal) || text.StartsWith("zone:", StringComparison.Ordinal) ||
        text.StartsWith("reward screen", StringComparison.Ordinal) || text.StartsWith("event header:", StringComparison.Ordinal) ||
        text.StartsWith("activity:", StringComparison.Ordinal) || text.StartsWith("anchor", StringComparison.Ordinal) ||
        text.StartsWith("reads.log turned over", StringComparison.Ordinal);

    /// <summary>One line per new chat line read, marked ok when it parsed. Rotated at <see cref="ReadsMaxBytes"/> so it can't grow without bound.</summary>
    /// <param name="afterFade">The line was counted because the chat had faded and came back with it, not by lining up with the previous read.</param>
    public static void Read(string line, bool parsed, bool afterFade = false)
    {
        try
        {
            lock (ReadsLock)
            {
                if (Clock == null) { Prepare(); FlushHeld(Wall(), false); }
                Append($"{Stamp()}  {(parsed ? "ok" : "--")}  {line}{(afterFade ? "    (after a fade)" : "")}");
            }
        }
        catch (Exception) { /* logging must never break tracking */ }
    }

    /// <summary>For replay: the time each line of reads.log is stamped with (the replayed read's, in local time), and no trimming.</summary>
    public static Func<DateTime>? Clock { get; set; }

    static string Stamp() => $"{Clock?.Invoke() ?? Wall():HH:mm:ss.f}";

    /// <summary>
    /// A note about the reading itself (not a chat line) in reads.log, marked ?? so it stands apart from the lines. An
    /// identical note more than <see cref="FloodFree"/> times in a minute is held back and counted (see <see cref="NoteBurst"/>),
    /// except the lines in <see cref="Exempt"/>.
    /// </summary>
    public static void Note(string text) => NoteCore(text, Exempt(text) ? int.MaxValue : FloodFree);

    /// <summary>
    /// A note that flaps: written the first time, and an identical one in the next <see cref="BurstSeconds"/> is only
    /// counted. When the minute is over the count is written as one line, "(×6 more, last at 07:20:08)". For the
    /// "crafting panel up" / "gone" pair and the like, where a state change repeats every few seconds.
    /// </summary>
    public static void NoteBurst(string text) => NoteCore(text, 1);

    static void NoteCore(string text, int free)
    {
        try
        {
            lock (ReadsLock)
            {
                if (Clock == null)
                {
                    Prepare();
                    var now = Wall();
                    FlushHeld(now, false);
                    if (free != int.MaxValue && !Admit(text, free, now)) return;
                }
                Append($"{Stamp()}  ??  {text}");
            }
        }
        catch (Exception) { /* logging must never break tracking */ }
    }

    /// <summary>Writes the counts still held (the end of a run, a test): one line per note that was repeated.</summary>
    public static void FlushHeld()
    {
        try { lock (ReadsLock) { if (Clock == null) FlushHeld(Wall(), true); } }
        catch (Exception) { /* logging must never break tracking */ }
    }

    /// <summary>For tests: forget every burst, as if the tracker had just started.</summary>
    internal static void ResetBursts() { lock (ReadsLock) { Bursts.Clear(); _statPath = ""; _len = 0; _scannedAt = DateTime.MinValue; } }

    /// <summary>Whether this line is written: the first <paramref name="free"/> of a window are; the rest are counted.</summary>
    static bool Admit(string text, int free, DateTime now)
    {
        if (!Bursts.TryGetValue(text, out var b) || (now - b.FirstAt).TotalSeconds >= BurstSeconds)
        {
            // a window that ended with lines held was written out by FlushHeld just before this
            if (Bursts.Count > 500) foreach (var k in Bursts.Where(kv => kv.Value.Held == 0 && (now - kv.Value.FirstAt).TotalSeconds >= BurstSeconds).Select(kv => kv.Key).ToList()) Bursts.Remove(k);
            Bursts[text] = new Burst { FirstAt = now, Written = 1 };
            return true;
        }
        if (b.Written < free) { b.Written++; return true; }
        b.Held++; b.LastHeldAt = now;
        return false;
    }

    /// <summary>Writes a line for each burst whose window is over and which held lines back (<paramref name="force"/>: all of them), and forgets the finished ones.</summary>
    static void FlushHeld(DateTime now, bool force)
    {
        if (!force && (now - _scannedAt).TotalSeconds < 1) return;
        _scannedAt = now;
        List<(string Text, Burst B)>? due = null;
        foreach (var kv in Bursts)
        {
            bool over = (now - kv.Value.FirstAt).TotalSeconds >= BurstSeconds;
            if (kv.Value.Held > 0 && (over || force)) (due ??= new()).Add((kv.Key, kv.Value));
        }
        if (due == null) return;
        foreach (var (text, b) in due.OrderBy(d => d.B.LastHeldAt))
        {
            Append($"{Stamp()}  ??  {text}  (×{b.Held} more, last at {b.LastHeldAt:HH:mm:ss})");
            b.Held = 0;
            if ((now - b.FirstAt).TotalSeconds >= BurstSeconds) Bursts.Remove(text);
        }
    }

    /// <summary>
    /// Before a write (lock held): notices a reads.log that someone emptied or removed (the bursts it counted are history
    /// then), and turns the file over when it is past <see cref="ReadsMaxBytes"/>.
    /// </summary>
    static void Prepare()
    {
        var p = ReadsPath;
        var fi = new FileInfo(p);
        long len = fi.Exists ? fi.Length : 0;
        if (!string.Equals(p, _statPath, StringComparison.OrdinalIgnoreCase) || len < _len) Bursts.Clear();
        _statPath = p; _len = len;
        if (len <= ReadsMaxBytes) return;
        try
        {
            File.Move(p, ReadsOldPath, overwrite: true);
            _len = 0;
            Append($"{Stamp()}  ??  reads.log turned over at {len / 1_000_000.0:0.0} MB: the lines before this are in reads.old.log");
        }
        catch (Exception)
        {
            // the old file is held open by something: keep writing, and only if the file runs on to four times its size, start over
            if (len > ReadsMaxBytes * 4) { try { File.WriteAllText(p, ""); _len = 0; } catch (Exception) { } }
        }
    }

    static void Append(string line)
    {
        File.AppendAllText(ReadsPath, line + "\n");
        _len += System.Text.Encoding.UTF8.GetByteCount(line) + 1;
    }

    /// <summary>
    /// The last <paramref name="lines"/> lines of the reads log, oldest first: from reads.log, and from the end of
    /// reads.old.log when reads.log has just been turned over and holds fewer (a miscount report wants the minutes before).
    /// </summary>
    public static List<string> ReadsTail(int lines)
    {
        var tail = new List<string>();
        try
        {
            lock (ReadsLock)
            {
                if (File.Exists(ReadsPath)) tail = File.ReadLines(ReadsPath).TakeLast(lines).ToList();
                if (tail.Count < lines && File.Exists(ReadsOldPath)) tail = File.ReadLines(ReadsOldPath).TakeLast(lines - tail.Count).Concat(tail).ToList();
            }
        }
        catch (Exception) { /* the log is being written: the caller goes without it */ }
        return tail;
    }
}
