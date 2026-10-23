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

    public static void Write(string where, Exception ex)
    {
        try { File.AppendAllText(CrashPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {where}\n{ex}\n\n"); } catch (Exception) { /* nothing left to do */ }
    }

    static readonly object ReadsLock = new();
    /// <summary>One line per new chat line read, marked ok when it parsed. Capped so it can't grow without bound.</summary>
    /// <param name="afterFade">The line was counted because the chat had faded and came back with it, not by lining up with the previous read.</param>
    public static void Read(string line, bool parsed, bool afterFade = false)
    {
        try
        {
            lock (ReadsLock)
            {
                var p = ReadsPath;
                if (Clock == null && File.Exists(p) && new FileInfo(p).Length > 2_000_000) File.WriteAllText(p, "");
                File.AppendAllText(p, $"{Clock?.Invoke() ?? DateTime.Now:HH:mm:ss.f}  {(parsed ? "ok" : "--")}  {line}{(afterFade ? "    (after a fade)" : "")}\n");
            }
        }
        catch (Exception) { /* logging must never break tracking */ }
    }

    /// <summary>For replay: the time each line of reads.log is stamped with (the replayed read's, in local time), and no trimming.</summary>
    public static Func<DateTime>? Clock { get; set; }

    /// <summary>A note about the reading itself (not a chat line) in reads.log, marked ?? so it stands apart from the lines.</summary>
    public static void Note(string text)
    {
        try { lock (ReadsLock) File.AppendAllText(ReadsPath, $"{Clock?.Invoke() ?? DateTime.Now:HH:mm:ss.f}  ??  {text}\n"); }
        catch (Exception) { /* logging must never break tracking */ }
    }
}
