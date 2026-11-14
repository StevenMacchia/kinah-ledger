using System.IO;
using System.Windows;

namespace KinahLedger.Ui;

public static partial class WpfApp
{
    /// <summary>
    /// ui-shot --timers (or --bosses): the timers' window (Ui/MainWindow.Bosses.cs) over made-up readings: in an event with
    /// the map open, an event getting ready, an hour old, and none yet (timers-live.png, timers-getting-ready.png,
    /// timers-stale.png, timers-empty.png). Nothing is read, saved or sent.
    /// </summary>
    public static void ShotBosses(Settings settings, string dir, string? skin)
    {
        Ensure();
        Directory.CreateDirectory(dir);
        var w = new MainWindow(settings, watch: false) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -12000, Top = -12000, ShowActivated = false };
        w.Show();
        if (skin != null) Skins.Apply(skin);
        DoEvents();
        w.ShotBosses(dir, skin);
        w.Close();
    }
}
