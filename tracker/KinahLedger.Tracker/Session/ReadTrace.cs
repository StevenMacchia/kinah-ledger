using System.Text.Json;

namespace KinahLedger.Session;

/// <summary>
/// Every read as it came off the screen, chat and loot list, in trace.log beside reads.log: one JSON line each, so a
/// fight can be replayed through the parser later and a fix measured on what the screen really showed, not on a guess
/// (2026-10-22). Text only, nothing else: the same chat lines reads.log already holds. Kept to about 40 MB, then the
/// file starts over as trace.old.log.
/// </summary>
public static class ReadTrace
{
    static readonly object Lock = new();
    const long MaxBytes = 40L * 1024 * 1024;
    static string PathNow => Path.Combine(CrashLog.Dir, "trace.log");

    public static void Chat(DateTime at, IEnumerable<string> lines) => Write(at, "chat", lines);
    public static void List(DateTime at, IEnumerable<string> rows) => Write(at, "list", rows);
    /// <summary>
    /// What else the tracker read that decides where a pickup lands, as it changes (since 2026-10-23): "zone" (the minimap's
    /// zone, or Unknown zone), "reward" (a reward screen's title as it opens, "" as it closes), "header" (the event named under
    /// the minimap). Names from the game's lists only, never a player's.
    /// </summary>
    public static void Signal(DateTime at, string kind, string value) => Write(at, kind, new[] { value });

    static void Write(DateTime at, string kind, IEnumerable<string> lines)
    {
        try
        {
            var json = JsonSerializer.Serialize(new { t = at.ToString("O"), k = kind, l = lines.ToArray() });
            lock (Lock)
            {
                var p = PathNow;
                var fi = new FileInfo(p);
                if (fi.Exists && fi.Length > MaxBytes) { var old = Path.Combine(CrashLog.Dir, "trace.old.log"); File.Delete(old); File.Move(p, old); }
                File.AppendAllText(p, json + Environment.NewLine);
            }
        }
        catch (Exception) { /* the trace is for later analysis; a failed write never stops the reading */ }
    }
}
