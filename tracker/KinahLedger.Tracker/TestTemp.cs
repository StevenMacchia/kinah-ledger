namespace KinahLedger;

/// <summary>
/// Scratch folders for the offline test modes (parse-test, market-test, accuracy-test, replay-bank). Agents and CI used to
/// share fixed folders under %TEMP% (KinahLedger-test-logs, KinahLedger-parse-test, ...), so two runs at once failed at
/// random (2026-10-24). Each run of a test mode now has its own folder, %TEMP%\KinahLedger-tests\&lt;mode&gt;-&lt;pid&gt;-&lt;time&gt;,
/// removed when the run passes and named on the error stream when it doesn't, so its logs can be read.
/// </summary>
public static class TestTemp
{
    static readonly string[] Modes = { "parse-test", "market-test", "accuracy-test", "replay-bank" };

    /// <summary>This run's folder, or null outside a test mode (then <see cref="Path"/> is under %TEMP% as before).</summary>
    public static string? Base { get; private set; }

    public static bool IsTestMode(string cmd) => Modes.Contains(cmd);

    /// <summary>Makes this run's folder: the mode, the process id and the time, so no two runs share one.</summary>
    public static string Begin(string mode)
    {
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "KinahLedger-tests", $"{mode}-{Environment.ProcessId}-{DateTime.Now:yyyyMMdd-HHmmss-fff}");
        Directory.CreateDirectory(dir);
        return Base = dir;
    }

    /// <summary>A path for a test's scratch file or folder: inside this run's folder in a test mode, else under %TEMP%.</summary>
    public static string Path(string name) => System.IO.Path.Combine(Base ?? System.IO.Path.GetTempPath(), name);

    /// <summary>The run is over: its folder goes when it passed; when it failed the folder stays and its path is printed.</summary>
    public static void End(bool passed)
    {
        if (Base is not { } dir) return;
        Base = null;
        if (!passed) { Console.Error.WriteLine($"test files kept in {dir}"); return; }
        // replay-bank's KL_BANK_KEEP keeps each case's logs for reading: the folder stays then too
        if (Environment.GetEnvironmentVariable("KL_BANK_KEEP") is { Length: > 0 }) { Console.WriteLine($"test files kept in {dir}"); return; }
        // a picture a test loaded may still hold its file until the collector frees it: freed, and tried again
        for (int i = 0; i < 3 && Directory.Exists(dir); i++)
        {
            try { Directory.Delete(dir, true); }
            catch (Exception) { GC.Collect(); GC.WaitForPendingFinalizers(); Thread.Sleep(200); }
        }
    }
}
