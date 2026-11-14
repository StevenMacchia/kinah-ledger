using System.IO;
using System.Windows;

namespace KinahLedger.Ui;

public static partial class WpfApp
{
    /// <summary>
    /// ui-shot --proposal: the redesign proposal's pages (Ui/MainWindow.Proposal.cs) over the demo, each as proposal-&lt;page&gt;.png,
    /// plus proposal-farm-rail.png (the pages in a left rail) and proposal-timers-overlay.png. Nothing is read, saved or sent.
    /// </summary>
    public static void ShotProposal(Settings settings, string dir, string? skin)
    {
        Ensure();
        Directory.CreateDirectory(dir);
        Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;   // one window per page
        Environment.SetEnvironmentVariable("KL_DEMO_BAG", "known");
        foreach (var (page, kind, rail) in new[]
        {
            ("Farm", "Open world", false), ("Runs", "Events", false), ("Craft", "Crafting", false),
            ("Character", "Open world", false), ("Timers", "Open world", false), ("Overlays", "Open world", false), ("Quests", "Quests", false), ("Farm", "Open world", true),
        })
        { try {
            var w = new MainWindow(settings, watch: false) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -12000, Top = -12000, ShowActivated = false };
            w.Show();
            Skins.Apply(skin ?? Skins.Default);
            w.MaxHeight = 4000;
            w.ShowDemo(kind);
            IconCache.Drain(TimeSpan.FromSeconds(8));
            DoEvents();
            w.ProposalPage(page, rail);
            DoEvents();
            var name = "proposal-" + page.ToLowerInvariant() + (rail ? "-rail" : "") + (skin != null ? "-" + skin : "");
            Save((FrameworkElement)w.Content, Path.Combine(dir, name + ".png"), 800);
            if (page == "Timers" && !rail)
            {
                var o = w.ProposalTimerOverlay();
                Save(o, Path.Combine(dir, "proposal-timers-overlay.png"), 300);
            }
            w.Close();
        } catch (Exception ex) { Console.WriteLine(page + ": " + ex); } }
    }
}
