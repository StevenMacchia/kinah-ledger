using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace KinahLedger.Ui;

public static partial class WpfApp
{
    /// <summary>
    /// options-test: the settings revamp (Steven, 2026-11-17: "a complete setting revamp ... the overlays are the most
    /// important thing that people want"). The left bar starts with the three overlays; every page fits the window with its
    /// folds shut; every row on every page is found by the search and reached by a jump (its fold opened, the row on
    /// screen); every switch, list and slider sits in a titled row; the DPS meter's header switches wait for Save; no page
    /// but Game reading names where the figures come from; Server and About still open their pages.
    /// </summary>
    static void SettingsRevampChecks(MainWindow w, Func<OptionsWindow> open, Action<bool, string> check)
    {
        static void Click(ButtonBase b) => b.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        static void Flip(CheckBox c) { c.IsChecked = c.IsChecked != true; Click(c); }
        var o = open();

        // the left bar: Buddy, DPS meter, Timers, then the rest
        var nav = OptionsWindow.NavPages.Select(p => p.Page).ToList();
        check(nav.Take(3).SequenceEqual(new[] { OptionsWindow.Page.InGame, OptionsWindow.Page.Meter, OptionsWindow.Page.Timers }) && nav.Distinct().Count() == nav.Count
              && Enum.GetValues<OptionsWindow.Page>().All(p => nav.Contains(OptionsWindow.Shown(p))),
            "settings: the left bar opens on the overlays (Buddy, DPS meter, Timers), and every page is on it");
        o.Go(OptionsWindow.Page.Server);
        bool server = o.PageShownForTest == OptionsWindow.Page.Sharing && o.PageSharing.Visibility == Visibility.Visible && o.RegionPick.IsVisible;
        o.Go(OptionsWindow.Page.About);
        bool about = o.PageShownForTest == OptionsWindow.Page.Updates && o.PageAbout.IsVisible;
        check(server && about, "settings: Server opens Account & sharing with the server first; About opens Updates & about");

        // every page fits the window at its default size with its folds shut (Steven, 2026-11-14: "CONCISE dont make people have to scroll")
        var tall = new List<string>();
        foreach (var (page, _) in OptionsWindow.NavPages)
        {
            o.Go(page); DoEvents();
            if (o.FoldsForTest(page).Any(f => !f.Shut)) tall.Add($"{page}: a fold opens by itself");
            if (o.Scroller.ExtentHeight > o.Scroller.ViewportHeight + 1) tall.Add($"{page}: {o.Scroller.ExtentHeight:0} of {o.Scroller.ViewportHeight:0}");
        }
        check(tall.Count == 0 && Math.Abs(o.Height - 720) < 1, "settings: every page fits the window without scrolling, its folds shut" + (tall.Count > 0 ? ": " + string.Join("; ", tall) : ""));

        // every row: found by the search under its own title, and reached by a jump
        o.SearchForTest("");
        var rows = o.SearchForTestAll();
        var lost = new List<string>();
        foreach (var r in rows)
        {
            var hits = o.SearchForTest(r.Title);
            if (!hits.Contains(r.Title)) { lost.Add($"{r.Title}: not found"); continue; }
            var hit = o.SearchHitsForTest.First(h => h.Title == r.Title && h.Page == r.Page);
            var shown = o.JumpForTest(hit); DoEvents();
            if (o.PageShownForTest != r.Page || !o.IsShownForTest(shown)) lost.Add($"{r.Title}: not shown after the jump");
            o.OpenFoldsForTest(false);
        }
        o.SearchForTest("");
        check(rows.Count >= 70 && lost.Count == 0 && o.NavShownForTest,
            $"settings: every row ({rows.Count}) is found by its title and shown by a jump, its fold opened" + (lost.Count > 0 ? ": " + string.Join("; ", lost.Take(6)) : ""));
        // the search: by a hint's words too, a title first; nothing found says so; cleared, the left bar is back
        var tray = o.SearchForTest("tray");
        var recipe = o.SearchForTest("recipe row");
        var none = o.SearchForTest("zzqx");
        bool noneSaid = !o.NavShownForTest && o.SearchHitsForTest.Count == 0;
        o.SearchForTest("");
        check(tray.FirstOrDefault() == "Minimize to the tray" && tray.Contains("Start with Windows") && recipe.Any(t => t.StartsWith("Click a recipe row")) && none.Count == 0 && noneSaid && o.NavShownForTest,
            $"settings: the search finds by title and hint, a title first ({string.Join(", ", tray.Take(3))}), a tip too, nothing for nonsense, and clears back to the left bar");

        // every switch, list and slider on a page sits in a row with a title (none left over after the move)
        var stray = new List<string>();
        foreach (var (page, _) in OptionsWindow.NavPages)
        {
            o.Go(page); o.OpenFoldsForTest(); DoEvents();
            var panel = o.PageShownPanelForTest;
            var titled = new HashSet<DependencyObject>(o.SearchForTestAll().Where(r => r.Page == page).SelectMany(r => OptionsWindow.TreeOf(r.Row)));
            foreach (var c in OptionsWindow.TreeOf(panel).OfType<Control>().Where(c => c is CheckBox or ComboBox or Slider && c.IsVisible))
                if (!titled.Contains(c) && !o.InHeaderForTest(c)) stray.Add($"{page}: {c.GetType().Name} {c.Name}{System.Windows.Automation.AutomationProperties.GetName(c)}");
        }
        o.OpenFoldsForTest(false);
        check(stray.Count == 0, "settings: every switch, list and slider sits in a titled row or a page's header" + (stray.Count > 0 ? ": " + string.Join("; ", stray.Take(6)) : ""));

        // no page but Game reading names where the figures come from (Steven, 2026-11-14), folds open
        var said = new List<string>();
        foreach (var (page, _) in OptionsWindow.NavPages.Where(p => p.Page != OptionsWindow.Page.Reading))
        {
            o.Go(page); o.OpenFoldsForTest(); DoEvents();
            said.AddRange(w.SourceWordsShown(o.PageShownPanelForTest).Select(h => $"{page}: {h}"));
        }
        o.OpenFoldsForTest(false);
        check(said.Count == 0, "settings: only Game reading says where the figures come from" + (said.Count > 0 ? ": " + string.Join(" | ", said.Take(4)) : ""));

        // the DPS meter's header switches (on/off, click-through, when it shows) wait for Save, then reach the meter
        o.Go(OptionsWindow.Page.Meter);
        var (show, through, when) = o.ParserOverlaySwitchesForTest;
        bool on0 = w.ParserOverlayOn, ct0 = w.ParserOverlayClickThrough, al0 = w.ParserAlwaysOn;
        check(show.IsChecked == on0 && through.IsChecked == ct0 && when.SelectedIndex == (al0 ? 1 : 0) && !o.SaveBtn.IsEnabled, "DPS meter: its header opens as the meter is (shown, click-through, when it shows)");
        Flip(through); when.SelectedIndex = al0 ? 0 : 1;
        bool waits = o.SaveBtn.IsEnabled && w.ParserOverlayClickThrough == ct0 && w.ParserAlwaysOn == al0;
        Click(o.SaveBtn); DoEvents();
        bool saved = w.ParserOverlayClickThrough == !ct0 && w.ParserAlwaysOn == !al0 && o.IsVisible && !o.SaveBtn.IsEnabled;
        Flip(through); when.SelectedIndex = al0 ? 1 : 0;
        Click(o.SaveBtn); DoEvents();
        check(waits && saved && w.ParserOverlayClickThrough == ct0 && w.ParserAlwaysOn == al0, $"DPS meter: click-through and When it shows wait for Save, then reach the meter, and back (waited {waits}, saved {saved})");

        // Buddy's stage: the look in use, another under the mouse, back on leaving
        o.Go(OptionsWindow.Page.InGame);
        string inUse = HudLooks.Parse(w.HudLook);
        string other = HudThemes.Looks(HudThemes.Of(inUse)).Select(l => l.Key).First(k => k != inUse);
        var s0 = o.HudStageForTest;
        o.HoverHudLookForTest(other); var s1 = o.HudStageForTest;
        o.HoverHudLookForTest(null); var s2 = o.HudStageForTest;
        check(s0 == inUse && s1 == other && s2 == inUse && !o.SaveBtn.IsEnabled, $"Buddy: the stage draws the look in use ({s0}), the one under the mouse ({s1}), then the one in use again ({s2}); hovering changes nothing");
        o.Close(); DoEvents();
    }
}
