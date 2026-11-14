using System.IO;
using System.Windows;
using System.Windows.Media;

namespace KinahLedger.Ui;

public static partial class WpfApp
{
    /// <summary>
    /// ui-shot --timers-page: the Timers page in the window over the timers' sample readings (live: in an event with the map
    /// open, the default view, and the header's Overlays menu; live-open: every map's list opened; empty: nothing read yet), as timers-page-&lt;state&gt;[-&lt;skin&gt;].png. Nothing is read, saved or sent.
    /// </summary>
    public static void ShotTimersPage(Settings settings, string dir, string? skin)
    {
        Ensure();
        Directory.CreateDirectory(dir);
        Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;   // one window per state
        foreach (var state in new[] { "live", "live-open", "empty" })
        {
            var w = new MainWindow(settings, watch: false) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -12000, Top = -12000, ShowActivated = false };
            w.Show();
            Skins.Apply(skin ?? Skins.Default);
            w.MaxHeight = 4000;
            w.ShowDemo();
            DoEvents();
            w.TimersSampleForShot(state == "live-open" ? "live" : state);
            w.ShowPartForTest("Timers");
            if (state == "live-open") foreach (var g in new[] { "Verteron", "Altgard", "Abyss", "Lower Reshanta", "World bosses" }) w.OpenTimerGroupForTest(g);   // every list open
            DoEvents();
            Save((FrameworkElement)w.Content, Path.Combine(dir, $"timers-page-{state}{(skin != null ? "-" + skin : "")}.png"), 800);
            // the header's Overlays menu (its card, as the button opens it)
            if (state == "live") Save(new System.Windows.Controls.Border { Child = w.BuildMenu(w.OverlayEntries()), Padding = new Thickness(4) }, Path.Combine(dir, $"overlays-menu{(skin != null ? "-" + skin : "")}.png"), 320, Skins.Current == "daylight" ? Colors.White : Color.FromRgb(0x0A, 0x0B, 0x10));
            w.Close();
        }
    }
}
