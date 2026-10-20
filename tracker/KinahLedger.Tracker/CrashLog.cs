namespace KinahLedger;

/// <summary>Writes unexpected errors and read traces to %LOCALAPPDATA%\KinahLedger so problems can be diagnosed after the fact.</summary>
public static class CrashLog
{
    public static string Dir
    {
        get
        {
            var d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KinahLedger");
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
    public static void Read(string line, bool parsed)
    {
        try
        {
            lock (ReadsLock)
            {
                var p = ReadsPath;
                if (File.Exists(p) && new FileInfo(p).Length > 2_000_000) File.WriteAllText(p, "");
                File.AppendAllText(p, $"{DateTime.Now:HH:mm:ss}  {(parsed ? "ok" : "--")}  {line}\n");
            }
        }
        catch (Exception) { /* logging must never break tracking */ }
    }
}
