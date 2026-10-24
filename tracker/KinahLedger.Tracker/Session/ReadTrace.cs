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
    /// A loot list read with each row's top ("y") and, when measured, its text's contrast ("c"), so a replay tracks the
    /// rows by their slots and settles their text the way the live read did (LootRows); old readers ignore both. Since
    /// 2026-10-24 also, when measured, each row's background ("b") and the spread where its icon sits ("s", -1 where that
    /// place was outside the picture), so a replay tells rows from nameplates and world text the same way (LootNotRow).
    /// And each row's icon hash ("h", hex, "0" where it fell outside the picture), so a replay knows a row by its icon too.
    /// </summary>
    public static void List(DateTime at, IReadOnlyList<string> rows, IReadOnlyList<int> tops, IReadOnlyList<double>? contrast,
        IReadOnlyList<double>? background = null, IReadOnlyList<double>? spread = null, IReadOnlyList<string>? icons = null)
    {
        try
        {
            var o = new Dictionary<string, object> { ["t"] = at.ToString("O"), ["k"] = "list", ["l"] = rows, ["y"] = tops };
            if (contrast != null) o["c"] = contrast;
            if (background != null) o["b"] = background;
            if (spread != null) o["s"] = spread;
            if (icons != null) o["h"] = icons;
            Append(JsonSerializer.Serialize(o));
        }
        catch (Exception) { /* the trace is for later analysis */ }
    }
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
            Append(JsonSerializer.Serialize(new { t = at.ToString("O"), k = kind, l = lines.ToArray() }));
        }
        catch (Exception) { /* the trace is for later analysis; a failed write never stops the reading */ }
    }

    static void Append(string json)
    {
        try
        {
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
