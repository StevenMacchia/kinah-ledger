using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using KinahLedger.Parser;

namespace KinahLedger.Ui;

/// <summary>
/// Settings simplified and copy promoted (Steven, 2026-11-18: "Redundancy in settings in the parser + parser settings. Have all
/// settings simplified - defaulted to on and in the settings. No settings should be from the app outside settings. Make copy
/// and pasting EASY and promoted"). The options-test parts (<c>KL_OPTIONS_ONLY=settings</c>, and the head's copy checks under
/// <c>parsershare</c>) and the renders: ui-shot DIR --simplify draws the Parser page live, just after a kill, with a kept kill
/// open and empty; Settings › DPS meter with its folds shut and open; the header's Overlays menu. KL_LANG for ko and zh-TW.
/// </summary>
public static partial class WpfApp
{
    /// <summary>
    /// options-test: no switch, pick or list on the Parser page or the Timers page (the list's own boss filter aside), the
    /// overlay's right-click menu has no setting, each overlay's line in the Overlays menu says where it is set up, each of
    /// the meter's settings has one row in Settings, a new install's meter defaults (1.0.18) while a file from before keeps
    /// its choices, and Damage to adds reaches the meter on Save.
    /// </summary>
    static void SimplifyChecks(Settings settings, Action<bool, string> check)
    {
        static void Click(System.Windows.Controls.Primitives.ButtonBase b) => b.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        var w = new MainWindow(settings, watch: false) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -12000, Top = -12000, ShowActivated = false };
        try
        {
            w.Show(); w.ShowDemo("Open world", null); DoEvents();
            // the Parser page: actions only, in every state
            var found = new List<string>();
            foreach (var st in new[] { "live", "history", "detail", "empty", "inspect-full", "taken" })
            {
                w.ShowParserDemo(st); DoEvents(); w.UpdateLayout();
                foreach (var c in OptionsWindow.TreeOf(w.ParserPage).OfType<Control>())
                {
                    string name = System.Windows.Automation.AutomationProperties.GetName(c);
                    if (c is CheckBox || c is Slider || (c is ComboBox && FindTagged(w.ParserPage, "parser-head") is { } head && OptionsWindow.TreeOf(head).Contains(c))
                        || name.StartsWith("Parser meter:") || name.StartsWith("DPS over") || name.StartsWith("Damage counted") || name.StartsWith("Overlay look"))
                        found.Add($"{st}: {c.GetType().Name} {name}");
                }
            }
            check(found.Count == 0, "Parser page: no switch, meter pick, overlay-look link or DPS-basis segment anywhere on it, in any state" + (found.Count > 0 ? ": " + string.Join("; ", found.Distinct().Take(5)) : ""));
            HeldFightChecks(w, check);
            // the Timers page: no Overlay switch (the Overlays menu switches it, the gear opens Settings › Timers)
            w.ShowPartForTest("Timers"); DoEvents();
            var tSwitches = OptionsWindow.TreeOf(w.TimersPage).OfType<CheckBox>().Where(c => c.Style == w.TryFindResource("Switch") as Style).ToList();
            check(tSwitches.Count == 0, $"Timers page: no Overlay switch on it ({tSwitches.Count})");
            // the overlay's right-click menu: hide it, and a way to its settings; no click-through there
            var mo = new ParserOverlay(still: true);
            try
            {
                var items = mo.BuildMenu().Items.OfType<MenuItem>().Select(i => i.Header as string ?? "").ToList();
                check(items.Count == 2 && items[1] == L.T("DPS meter settings…") && !items.Any(i => i.Contains("clicks through", StringComparison.OrdinalIgnoreCase)),
                    $"DPS meter overlay: its right-click menu has Hide and DPS meter settings…, no click-through ({string.Join(" | ", items)})");
            }
            finally { mo.Close(); }
            // the Overlays menu: each overlay's line says where it is set up
            var entries = w.OverlayEntries().Where(e => e.On != null && e.Text.EndsWith(L.T("overlay"), StringComparison.Ordinal) || e.Text == L.F("{0} overlay", L.T("Parser"))).ToList();
            var unsaid = entries.Where(e => !e.Sub.Contains(L.T("Settings  ›").Replace("  ›", "").Trim())).Select(e => e.Text).ToList();
            check(entries.Count >= 2 && unsaid.Count == 0, $"Overlays menu: each overlay's line says where in Settings it is set up ({entries.Count}{(unsaid.Count > 0 ? "; not: " + string.Join(", ", unsaid) : "")})");

            // the meter's defaults for a new install (1.0.18): on, apart from the ones that are worse on or a choice between two
            var n = new ParserPrefs();
            bool onOk = n.AlwaysOn && n.Overlay && n.ClickThrough && n.Details && n.Deaths && n.Filters && n.Hovers && n.HealBars && n.Expanded && n.KillsNear;
            bool offOk = !n.DpsActive && !n.InKinah && !n.ShareHideNames && n.CritNow == "none" && n.Targets == "boss" && Platform.Hotkey.Parse(n.ShareHotkey) == null;
            // a file from before keeps its choices: a 1.0.17 file has every key written (off stays off), an older one without
            // the keys reads the old default (off), a choice of on stays on
            var v17 = ParserPrefs.FromText("{\"Overlay\":true,\"ClickThrough\":false,\"AlwaysOn\":false,\"Expanded\":false,\"KillsNear\":false,\"DetailsSettled\":true,\"HonestSettled\":true,\"GlassSettled\":true}");
            var older = ParserPrefs.FromText("{\"Overlay\":true,\"Details\":true,\"DetailsSettled\":true,\"HonestSettled\":true}");
            var chose = ParserPrefs.FromText("{\"ClickThrough\":true,\"AlwaysOn\":true,\"Expanded\":true,\"KillsNear\":true,\"DetailsSettled\":true,\"HonestSettled\":true}");
            bool kept = !v17.ClickThrough && !v17.AlwaysOn && !v17.Expanded && !v17.KillsNear && !older.ClickThrough && !older.AlwaysOn && !older.Expanded && !older.KillsNear
                && chose.ClickThrough && chose.AlwaysOn && chose.Expanded && chose.KillsNear;
            check(onOk && offOk && kept, $"DPS meter defaults: Always on, the overlay, click-through, group details, deaths, kills near you, filters, hovers, heal bars and the expanded meter on for a new install; DPS over active time, inside the Kinah overlay, hidden names, crit and adds off; a saved file keeps its own (on {onOk}, off {offOk}, kept {kept})");

            // Settings: each of the meter's settings one row, on the DPS meter page
            var o = new OptionsWindow(w, OptionsWindow.Page.Meter) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -12000, Top = -12000, ShowActivated = false };
            try
            {
                o.Show(); DoEvents();
                var rows = o.SearchForTestAll();
                var titles = new[] { "Expanded meter", "Heal bars", "Group details", "DPS over active time", "Damage to adds", "Deaths", "Kills near you", "List filters", "Crit and back-attack rates", "Explanations on hover", "Parser inside the Kinah overlay", "Hide other names", "Share picture shortcut", "Meter position", "Width" };
                var off = titles.Select(t => (t, rows.Where(r => r.Title == L.T(t)).ToList())).Where(x => x.Item2.Count != 1 || x.Item2[0].Page != OptionsWindow.Page.Meter).Select(x => $"{x.t} ×{x.Item2.Count}").ToList();
                check(off.Count == 0, "Settings: each of the meter's settings is one row, on the DPS meter page" + (off.Count > 0 ? ": " + string.Join(", ", off) : ""));
                // Damage to adds: waits for Save, then counts adds on the meter, and back
                bool boss0 = Encounter.BossOnly;
                var adds = o.ParserAddsForTest;
                adds.IsChecked = adds.IsChecked != true; Click(adds);
                bool waits = o.SaveBtn.IsEnabled && Encounter.BossOnly == boss0;
                Click(o.SaveBtn); DoEvents();
                bool reached = Encounter.BossOnly == !boss0;
                adds.IsChecked = adds.IsChecked != true; Click(adds); Click(o.SaveBtn); DoEvents();
                check(waits && reached && Encounter.BossOnly == boss0, $"Settings › DPS meter › Damage to adds waits for Save, then reaches the meter, and back (waited {waits}, reached {reached})");
            }
            finally { o.Close(); }
        }
        finally { w.Close(); }
    }

    /// <summary>
    /// options-test: the live view holds the last boss fight until the next one starts (Steven, 2026-11-18: the graphs stay up):
    /// its hero, tabs and result with one quiet Ready line over it and the list under it, no Back; Copy picture copies it; a
    /// pull clicked in the list opens with Back, and Back returns to the held fight, or to the live one once a fight is under
    /// way; a wipe is held as a kill is; with nothing kept, the Ready card as before.
    /// </summary>
    static void HeldFightChecks(MainWindow w, Action<bool, string> check)
    {
        static void Click(System.Windows.Controls.Primitives.ButtonBase b) => b.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        var got = new List<string>();
        var sink0 = MainWindow.ShareSinkForTest;
        MainWindow.ShareSinkForTest = (_, f) => got.Add(f);
        try
        {
            var bad = new List<string>();
            // just after a kill (and five minutes on: nothing changes with time while no new fight starts)
            w.ShowParserDemo("history"); DoEvents(); w.UpdateLayout();
            var bosses = w.ParserPastForTest.Where(e => e.ModeKind == ParserMode.Bosses && e.Roster.Any(f => !f.Other)).ToList();
            var held = bosses.Last();
            if (w.ParserHeldForTest != held.Id) bad.Add($"held {w.ParserHeldForTest ?? "none"}, not the newest pull {held.Id}");
            if (FindTagged(w.ParserPage, "parser-ready-line") == null) bad.Add("no Ready line");
            if (FindTagged(w.ParserPage, "parser-hero") == null || FindTagged(w.ParserPage, "parser-member") == null) bad.Add("no hero or group for the held fight");
            if (FindTagged(w.ParserPage, "parser-back") != null) bad.Add("a Back button on the held fight");
            if (w.ParserPageForTest().Rows < 5) bad.Add($"the list under it has {w.ParserPageForTest().Rows} rows");
            w.ParserTabForTest = "Timeline"; DoEvents();
            if (FindTagged(w.ParserPage, "parser-hero") == null) bad.Add("the Graphs tab drops the held fight");
            w.ParserTabForTest = "Group"; DoEvents();
            got.Clear();
            if (FindTagged(w.ParserPage, "parser-copy-now") is Button pic) Click(pic);
            if (got.Count != 1 || got[0] != ParserShare.FileName(held, ParserShare.Group)) bad.Add($"Copy picture gave {string.Join(",", got)}");
            // a pull clicked in the list opens with Back; Back returns to the held fight
            var row = OptionsWindow.TreeOf(w.ParserPage).OfType<Border>().Where(b => b.Tag as string == "parser-row").Skip(2).FirstOrDefault();
            row?.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonUpEvent });
            DoEvents();
            var opened = w.ParserOpenForTest;
            var back = FindTagged(w.ParserPage, "parser-back") as Button;
            if (row == null || opened == null || opened == held.Id || back == null || FindTagged(w.ParserPage, "parser-hero") == null) bad.Add($"a click on an older pull didn't open it with Back ({opened})");
            w.ParserTabForTest = "Timeline"; DoEvents();
            if (w.ParserOpenForTest != opened) bad.Add("the Graphs tab closed the opened pull");
            if (back != null) { Click(back); DoEvents(); }
            if (w.ParserOpenForTest != null || w.ParserHeldForTest != held.Id || FindTagged(w.ParserPage, "parser-ready-line") == null) bad.Add("Back didn't return to the held fight");
            // a wipe is held as a kill is
            w.HoldWipeForTest(); DoEvents();
            var wipe = w.ParserPastForTest.Last();
            if (wipe.Outcome != "wipe" || w.ParserHeldForTest != wipe.Id) bad.Add("a wipe isn't held");
            // a fight under way takes the live view; a pull opened from the list goes Back to it
            w.ShowParserDemo("live"); DoEvents();
            if (FindTagged(w.ParserPage, "parser-ready-line") != null || !w.ParserPageForTest().Live) bad.Add("a new fight doesn't take the live view");
            w.OpenFightForTest(w.ParserPastForTest.First(e => e.ModeKind == ParserMode.Bosses && e.Roster.Any(f => !f.Other)).Id); DoEvents();
            if (FindTagged(w.ParserPage, "parser-back") is Button b2) { Click(b2); DoEvents(); } else bad.Add("no Back on a pull opened during a fight");
            if (w.ParserOpenForTest != null || !w.ParserPageForTest().Live) bad.Add("Back didn't return to the fight under way");
            // nothing kept: the Ready card, as before
            w.ShowParserDemo("empty"); DoEvents();
            if (FindTagged(w.ParserPage, "parser-ready-line") != null || FindTagged(w.ParserPage, "parser-hero") != null) bad.Add("empty: something held");
            check(bad.Count == 0, "Parser: the live view holds the last boss fight (hero, tabs, result, a Ready line over it, the list under it) until the next one starts; Copy picture copies it; a pull clicked in the list opens with Back, back to the held or live fight; a wipe is held too" + (bad.Count > 0 ? ": " + string.Join("; ", bad) : ""));
        }
        finally { MainWindow.ShareSinkForTest = sink0; }
    }

    /// <summary>
    /// options-test (under parsershare): the head's big Copy picture and Copy text copy the fight the page shows (the tab's
    /// view; a member's abilities while one is open), greyed with nothing to copy; Ctrl+C and Ctrl+Shift+C do the same on the
    /// Parser page and nothing elsewhere; their tooltips name the keys; a kill puts Copy picture on the page and the status line.
    /// </summary>
    static void HeadCopyChecks(MainWindow w, Action<bool, string> check, List<(int W, int H, string File, BitmapSource Bmp)> got, Func<string?> text, Action<string?> setText)
    {
        static void Click(System.Windows.Controls.Primitives.ButtonBase b) => b.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        var bad = new List<string>();
        foreach (var (state, view, head) in new[] { ("live", ParserShare.Group, L.T("DPS")), ("detail", ParserShare.Group, L.T("DPS")), ("taken", ParserShare.Taken, L.T("Taken")), ("history", ParserShare.Group, L.T("DPS")) })
        {
            w.ShowParserDemo(state); DoEvents(); w.UpdateLayout();
            var pic = FindTagged(w.ParserPage, "parser-copy-now") as Button;
            var txt = FindTagged(w.ParserPage, "parser-copy-now-text") as Button;
            if (pic is not { IsEnabled: true } || txt is not { IsEnabled: true }) { bad.Add($"{state}: no Copy picture or Copy text"); continue; }
            if (pic.ActualHeight < 30 || pic.Style != w.TryFindResource("PrimaryButton") as Style) bad.Add($"{state}: Copy picture isn't the big gold one ({pic.ActualHeight:0})");
            if (!(pic.ToolTip as string ?? "").Contains(MainWindow.ParserCopyKey) || !(txt.ToolTip as string ?? "").Contains(MainWindow.ParserCopyTextKey)) bad.Add($"{state}: the tooltips don't say the keys");
            if (FindTagged(w.ParserPage, "parser-copy-hint") == null) bad.Add($"{state}: no key hint beside the buttons");
            got.Clear(); Click(pic);
            if (got.Count != 1 || !got[0].File.EndsWith($"-{view}.png", StringComparison.Ordinal)) bad.Add($"{state}: Copy picture gave {string.Join(",", got.Select(g => g.File))}, not the {view}");
            setText(null); Click(txt);
            var lines = (text() ?? "").Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length < 3 || !lines[1].Split('\t').Contains(head) || lines.Skip(2).Any(l => l.Split('\t').Length != lines[1].Split('\t').Length)) bad.Add($"{state}: Copy text gave {lines.Length} lines");
            // the keys
            got.Clear(); w.PressForTest(Key.C, ModifierKeys.Control);
            if (got.Count != 1 || !got[0].File.EndsWith($"-{view}.png", StringComparison.Ordinal)) bad.Add($"{state}: Ctrl+C gave {got.Count} pictures");
            setText(null); w.PressForTest(Key.C, ModifierKeys.Control | ModifierKeys.Shift);
            if (!(text() ?? "").Contains('\t')) bad.Add($"{state}: Ctrl+Shift+C copied no table");
        }
        // a member open: their abilities
        {
            w.ShowParserDemo("inspect-full"); DoEvents();
            got.Clear(); w.PressForTest(Key.C, ModifierKeys.Control);
            if (got.Count != 1 || !got[0].File.EndsWith("-abilities.png", StringComparison.Ordinal)) bad.Add($"inspect: Ctrl+C gave {string.Join(",", got.Select(g => g.File))}");
            setText(null); w.PressForTest(Key.C, ModifierKeys.Control | ModifierKeys.Shift);
            var lines = (text() ?? "").Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length < 3 || !lines[1].Split('\t').Contains(L.T("Ability"))) bad.Add($"inspect: Ctrl+Shift+C gave {lines.Length} lines");
        }
        // nothing yet: the buttons greyed, still there, and the keys copy nothing
        {
            w.ShowParserDemo("empty"); DoEvents();
            var pic = FindTagged(w.ParserPage, "parser-copy-now") as Button;
            var txt = FindTagged(w.ParserPage, "parser-copy-now-text") as Button;
            got.Clear(); w.PressForTest(Key.C, ModifierKeys.Control);
            if (pic == null || txt == null || pic.IsEnabled || txt.IsEnabled || got.Count != 0) bad.Add($"empty: the buttons {(pic == null ? "missing" : pic.IsEnabled ? "on" : "greyed")}, Ctrl+C gave {got.Count}");
        }
        // off the Parser page: Ctrl+C is not the Parser's
        {
            w.ShowPartForTest("Farming"); DoEvents();
            got.Clear();
            if (w.ParserCopyKeyForTest(text: false) || got.Count != 0) bad.Add("Farming: Ctrl+C copied the Parser's picture");
        }
        check(bad.Count == 0, $"Parser head: a big Copy picture and Copy text, their keys said beside them and in their tooltips, copy the view shown (a member's abilities when open); Ctrl+C and Ctrl+Shift+C do the same on the page only; greyed with nothing to copy" + (bad.Count > 0 ? ": " + string.Join("; ", bad) : ""));

        // a boss kill: Copy picture on the page's head and the status line, never a popup; a click copies the kill
        {
            w.ShowParserDemo("history"); DoEvents();
            w.KillPromptForTest(); DoEvents();
            var prompt = FindTagged(w.ParserPage, "parser-kill-prompt");
            var link = FindTagged(w.ParserPage, "parser-kill-copy");
            var kill = w.ParserPastForTest.Last(x => x.Outcome == "kill");
            got.Clear();
            link?.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonUpEvent });
            bool copied = got.Count == 1 && got[0].File == ParserShare.FileName(kill, ParserShare.Group);
            got.Clear();
            bool status = w.KillPromptShownForTest && w.CopyKillPictureForTest() && got.Count == 1;
            w.ShowParserDemo("history"); DoEvents();
            bool gone = FindTagged(w.ParserPage, "parser-kill-prompt") == null && !w.KillPromptShownForTest;
            check(prompt != null && link != null && copied && status && gone && w.ToastShowing is not null,
                $"Parser: after a kill, Copy picture on the page's head and the status line copies the kill (page {prompt != null}/{copied}, status line {status}), then goes ({gone})");
        }
    }

    public static int ShotSimplify(Settings settings, string dir)
    {
        Ensure();
        Directory.CreateDirectory(dir);
        var fit = new List<string>();
        var w = new MainWindow(settings, watch: false) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -12000, Top = -12000, ShowActivated = false };
        try
        {
            w.Show();
            w.ShowDemo("Open world", null); DoEvents();
            w.ParserPrefsFreshForTest();   // a new install's meter settings (1.0.18's defaults), on the page and in Settings
            // live; 5 minutes after a kill (held, no prompt); a new fight starting (the group, no damage yet); a wipe held; an older
            // pull opened from the list (Back); nothing kept; just killed last (its line holds the status line a while)
            foreach (var st in new[] { "live", "after5", "starting", "wipe", "detail", "empty", "killed" })
            {
                w.ShowParserDemo(st switch { "killed" or "after5" or "wipe" => "history", "starting" => "nodamage", _ => st });
                if (st == "killed") w.KillPromptForTest();
                if (st == "wipe") w.HoldWipeForTest();
                // the Graphs tab, as Steven keeps it, with the sample's per-second figures (a render's only)
                if (st is "after5" or "wipe" or "killed") { MainWindow.ParserDepthSampleForRender(true); w.ParserTabForTest = "Timeline"; }
                DoEvents();
                Save((FrameworkElement)w.Content, Path.Combine(dir, $"parser-{st}.png"), 800 - 16);
                if (st == "detail")
                {
                    // what Copy text puts on the clipboard, as a file to read (never the clipboard)
                    var sink0 = MainWindow.CopyTextSinkForTest;
                    MainWindow.CopyTextSinkForTest = t => File.WriteAllText(Path.Combine(dir, "parser-detail-copy.txt"), t);
                    try { w.CopyParserNowForTest(text: true); } finally { MainWindow.CopyTextSinkForTest = sink0; }
                }
                fit.Add($"parser-{st}: {w.LowerScrollForTest.ExtentHeight:0} of {w.LowerScrollForTest.ViewportHeight:0}");
            }
            Save(new Border { Child = w.BuildMenu(w.OverlayEntries()), Padding = new Thickness(4) }, Path.Combine(dir, "overlays-menu.png"), 320, Color.FromRgb(0x0A, 0x0B, 0x10));
            var o = new OptionsWindow(w, OptionsWindow.Page.Meter) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -12000, Top = -12000, ShowActivated = false };
            o.Show();
            IconCache.Drain(TimeSpan.FromSeconds(3));
            DoEvents();
            Save((FrameworkElement)o.Content, Path.Combine(dir, "options-meter.png"), 0);
            fit.Add($"options-meter: {o.Scroller.ExtentHeight:0} of {o.Scroller.ViewportHeight:0}{(o.Scroller.ExtentHeight > o.Scroller.ViewportHeight + 1 ? "  SCROLLS" : "")}");
            o.OpenFoldsForTest();
            DoEvents();
            double tall = o.Scroller.ExtentHeight + (o.ActualHeight - o.Scroller.ViewportHeight) + 2;
            o.MaxHeight = o.Height = Math.Max(o.ActualHeight, tall);
            DoEvents();
            Save((FrameworkElement)o.Content, Path.Combine(dir, "options-meter-open.png"), 0);
            o.Close();
        }
        catch (Exception ex) { Console.WriteLine(ex); return 1; }
        finally { w.Close(); }
        File.WriteAllLines(Path.Combine(dir, "fit.txt"), fit);
        Console.WriteLine($"wrote {Path.GetFullPath(dir)}");
        return 0;
    }
}
