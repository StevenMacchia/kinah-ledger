using System.IO;
using System.Windows;

namespace KinahLedger.Ui;

public static partial class WpfApp
{
    /// <summary>
    /// ui-shot --proposal: the redesign proposal's pages (Ui/MainWindow.Proposal.cs) over the demo, each as proposal-&lt;page&gt;.png,
    /// plus proposal-timers-overlay.png and proposal-overlays-menu.png. Nothing is read, saved or sent.
    /// </summary>
    public static void ShotProposal(Settings settings, string dir, string? skin)
    {
        Ensure();
        Directory.CreateDirectory(dir);
        Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;   // one window per page
        Environment.SetEnvironmentVariable("KL_DEMO_BAG", "known");
        foreach (var (page, kind) in new[]
        {
            ("Farm", "Open world"), ("Runs", "Events"), ("Timers", "Open world"),
        })
        { try {
            var w = new MainWindow(settings, watch: false) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -12000, Top = -12000, ShowActivated = false };
            w.Show();
            Skins.Apply(skin ?? Skins.Default);
            w.MaxHeight = 4000;
            w.ShowDemo(kind);
            IconCache.Drain(TimeSpan.FromSeconds(8));
            DoEvents();
            w.ProposalPage(page);
            DoEvents();
            var name = "proposal-" + page.ToLowerInvariant() + (skin != null ? "-" + skin : "");
            Save((FrameworkElement)w.Content, Path.Combine(dir, name + ".png"), 800);
            if (page == "Timers")
            {
                var o = w.ProposalTimerOverlay();
                Save(o, Path.Combine(dir, "proposal-timers-overlay.png"), 300);
                var m = w.ProposalOverlaysMenu();
                Save(m, Path.Combine(dir, "proposal-overlays-menu" + (skin != null ? "-" + skin : "") + ".png"), 300);
            }
            w.Close();
        } catch (Exception ex) { Console.WriteLine(page + ": " + ex); } }
    }
}
