using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace KinahLedger.Ui;

/// <summary>
/// "Report a miscount" when the tracker isn't reading the game yet (Steven, 2026-11-18: a friend's tracker showed nothing,
/// and the report said "Nothing to report yet", so nobody could see why). Instead of refusing, it sends a diagnostics
/// report: the tracker's own state (version, Windows, administrator or not, Npcap, the game reading line, the game's
/// process and window, fullscreen, the screens and their scaling, the overlay settings) and the tail of reads.log, which
/// ReportClient filters as it does every report (other players' chat never leaves the PC). No pictures.
/// </summary>
public partial class MainWindow
{
    [DllImport("shell32.dll")] static extern int SHQueryUserNotificationState(out int state);

    /// <summary>The Windows notification state: 3 is a Direct3D game in exclusive fullscreen (QUNS_RUNNING_D3D_FULL_SCREEN).</summary>
    static string FullscreenState()
    {
        try
        {
            if (SHQueryUserNotificationState(out int s) != 0) return "unknown";
            return s switch { 1 => "not present", 2 => "busy (a fullscreen app)", 3 => "Direct3D exclusive fullscreen", 4 => "presentation mode", 5 => "accepts notifications", 6 => "quiet time", 7 => "a Store app", _ => s.ToString() };
        }
        catch (Exception) { return "unknown"; }
    }

    /// <summary>The diagnostics lines: the tracker's own words, no player names.</summary>
    List<string> DiagnosticsLines()
    {
        var d = new List<string>
        {
            "Diagnostics (the tracker is not reading the game yet)",
            $"version {Upload.RunPayload.AppVersion}",
            $"windows {Environment.OSVersion.VersionString}",
            $"administrator {KinahLedger.Packets.PacketSources.IsAdmin()}",
            $"npcap installed {KinahLedger.Packets.PacketSources.NpcapInstalled}",
            $"game reading on {_prefs.PacketReading} · now: {_watcher.Packets.State}",
            $"game process seen {Ocr.GameWindow.ProcessSeen} · pids {Ocr.GameWindow.Pids.Count} · running {_watcher.GameRunning}",
            $"game window {(_watcher.GameRect is { } r ? $"{r.X},{r.Y} {r.Width}x{r.Height}" : "not found")} · handle {(Ocr.GameWindow.Handle != IntPtr.Zero)}",
            $"game in front {Ocr.GameWindow.InFront(_settings.ProcessHints)} · fullscreen state {FullscreenState()} · larger than its screen {MeterBlocks.LargerThanItsScreen(_watcher.GameRect)} · fullscreen card {_fullscreen}",
            $"window capture active {Ocr.WindowCapture.Active}",
            $"chat {( _watcher.Chat != null ? "found" : "not found")} · note: {_watcher.ChatNote} · manual chat {_prefs.ManualChat}",
            $"last error: {_watcher.LastError}",
            $"overlay: kinah {_prefs.Hud} · look {HudLook} · locked {_hudPrefs.Locked} · parser overlay {_parserPrefs.Overlay} · always on {_parserPrefs.AlwaysOn} · in kinah {_parserPrefs.InKinah}",
            $"meter window: made {_parserOverlay != null} · shown {_parserOverlay?.IsVisible == true} · at {(_parserOverlay is { } po ? $"{po.Left:0},{po.Top:0} {po.ActualWidth:0}x{po.ActualHeight:0}" : "-")} · saved spot {_parserPrefs.Left?.ToString("0") ?? "-"},{_parserPrefs.Top?.ToString("0") ?? "-"} · click-through {_parserPrefs.ClickThrough} · a fight on it {_meterOnOverlay != null}",
            $"kinah overlay window: made {_hud != null} · shown {_hud?.IsVisible == true} · at {(_hud is { } hw ? $"{hw.Left:0},{hw.Top:0} {hw.ActualWidth:0}x{hw.ActualHeight:0}" : "-")}",
        };
        try
        {
            foreach (var s in System.Windows.Forms.Screen.AllScreens)
                d.Add($"screen {s.DeviceName} {s.Bounds.Width}x{s.Bounds.Height} at {s.Bounds.X},{s.Bounds.Y}{(s.Primary ? " primary" : "")}");
        }
        catch (Exception) { }
        try { var dpi = VisualTreeHelper.GetDpi(this); d.Add($"window scaling {dpi.DpiScaleX * 100:0}%"); } catch (Exception) { }
        // the game reading's own log: only the tracker's state lines (where it reads from, Off, recording, the minute counts)
        try
        {
            var pl = Path.Combine(CrashLog.Dir, "packets.log");
            if (File.Exists(pl))
            {
                using var fs = new FileStream(pl, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var sr = new StreamReader(fs, Encoding.UTF8);
                var keep = sr.ReadToEnd().Split('\n').Select(l => l.TrimEnd('\r'))
                    .Where(l => l.Contains("  reading ") || l.Contains("Off") || l.Contains("recording") || l.Contains("  minute: ") || l.Contains("the game moved"))
                    .TakeLast(40).ToList();
                if (keep.Count > 0) { d.Add("game reading log:"); d.AddRange(keep.Select(l => "  " + l)); }
            }
        }
        catch (Exception) { }
        return d;
    }

    /// <summary>A diagnostics report folder under reports/ (summary.txt, the reads.log tail), sent like any report. True when it went.</summary>
    bool ReportDiagnostics()
    {
        try
        {
            var dir = Path.Combine(CrashLog.Dir, "reports", "diag-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            Directory.CreateDirectory(dir);
            File.WriteAllLines(Path.Combine(dir, "summary.txt"), DiagnosticsLines());
            var reads = Path.Combine(CrashLog.Dir, "reads.log");
            if (File.Exists(reads))
            {
                using var fs = new FileStream(reads, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var sr = new StreamReader(fs, Encoding.UTF8);
                File.WriteAllLines(Path.Combine(dir, "reads-log.txt"), sr.ReadToEnd().Split('\n').Select(l => l.TrimEnd('\r')).TakeLast(400));
            }
            if (_watcher.GameRect is { } g) File.WriteAllText(Path.Combine(dir, "game.json"), $"{{\"w\":{g.Width},\"h\":{g.Height}}}");
            Status.ToolTip = dir;
            Say(L.T("The tracker isn't reading the game yet. Sending a diagnostics report so we can see why…"), "Faint");
            _ = UploadReport(dir, "Diagnostics: not reading the game", "");
            return true;
        }
        catch (Exception ex) { CrashLog.Write("diagnostics report", ex); Say(L.F("Couldn't save the report: {0}", ex.Message), "Bad"); return false; }
    }

    /// <summary>
    /// Every report carries the same lines at the end of its summary (2026-11-18: a friend's meter didn't show though the
    /// tracker read the game, and the report couldn't say why): the overlay windows, their places and switches.
    /// </summary>
    void AppendDiagnostics(string dir)
    {
        try
        {
            var p = Path.Combine(dir, "summary.txt");
            File.AppendAllLines(p, new[] { "" }.Concat(DiagnosticsLines().Skip(1)));
        }
        catch (Exception ex) { CrashLog.Write("report diagnostics", ex); }
    }

    /// <summary>For the options self-test: the diagnostics lines.</summary>
    internal List<string> DiagnosticsForTest() => DiagnosticsLines();
}
