using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace KinahLedger.Ui;

/// <summary>
/// What keeps the DPS meter off, Npcap missing first (Ui/MainWindow.MeterBlock.cs, 2026-11-18): the options-test part
/// (<c>KL_OPTIONS_ONLY=npcap</c> alone) and the renders (<c>ui-shot DIR --npcap</c>, with KL_LANG=ko or zh-TW for the others).
/// </summary>
public static partial class WpfApp
{
    static readonly MeterBlock[] MeterBlockCases = { MeterBlock.None, MeterBlock.Missing, MeterBlock.AdminsOnly, MeterBlock.NoServer, MeterBlock.Off, MeterBlock.Restart };

    /// <summary>
    /// options-test: the card shows only while Npcap is missing (and the tracker isn't administrator), its Get Npcap opens
    /// npcap.com's download, the Overlays menu and the two Settings pages carry the one line, and a simulated install takes
    /// all of it away and has the reader try again at once. Then the other cases, each only from its own facts.
    /// </summary>
    static void NpcapChecks(Settings settings, Action<bool, string> check)
    {
        var inst0 = MeterBlocks.NpcapInstalled; var admin0 = MeterBlocks.IsAdmin; var only0 = MeterBlocks.AdminOnly; var forced0 = MeterBlocks.Forced; var open0 = MainWindow.OpenForTest;
        var fsProbe0 = MeterBlocks.FullscreenProbe; var fsForced0 = MeterBlocks.ForcedFullscreen;
        bool installed = false, admin = false, adminOnly = false;
        bool? fullscreen = false;
        MeterBlocks.FullscreenProbe = _ => fullscreen; MeterBlocks.ForcedFullscreen = null;
        string? opened = null;
        MeterBlocks.NpcapInstalled = () => installed; MeterBlocks.IsAdmin = () => admin; MeterBlocks.AdminOnly = () => adminOnly; MeterBlocks.Forced = null;
        MainWindow.OpenForTest = u => opened = u;
        var w = new MainWindow(settings, watch: false) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -12000, Top = -12000, ShowActivated = false };
        OptionsWindow? o = null;
        try
        {
            w.Show();
            w.ShowDemo("Open world", null); DoEvents();
            w.ShowParserDemo("empty"); DoEvents();
            check(w.MeterBlockShown == MeterBlock.None && w.MeterCardForTest() == null, "npcap: a render or test window shows no card (it reads nothing)");

            // Npcap missing, not administrator: the card, its button, Check again
            w.MeterBlockLiveForTest = true;
            w.CheckMeterBlock(); DoEvents();
            var card = w.MeterCardForTest();
            check(w.MeterBlockShown == MeterBlock.Missing && card is { } c && c.Title == L.T("The DPS meter needs Npcap") && c.Buttons.Count == 1 && c.Buttons[0].Text == L.T("Get Npcap") && c.CheckAgain,
                $"npcap: missing and not administrator: the Parser page's card, \"{card?.Title}\", with Get Npcap and Check again");
            check(card?.Buttons.FirstOrDefault().Tag == "https://npcap.com/#download", $"npcap: Get Npcap's address is npcap.com's download ({card?.Buttons.FirstOrDefault().Tag})");
            w.ClickMeterButtonForTest(MeterBlocks.NpcapUrl); DoEvents();
            check(opened == "https://npcap.com/#download", $"npcap: Get Npcap opens npcap.com's download in the browser ({opened})");
            var words = w.SourceWordsShown(w.ParserPage);
            check(words.Count == 0, "npcap: the card names Npcap and no other word for how the game is read" + (words.Count > 0 ? ": " + words[0] : ""));
            // the Overlays menu: one line, after the Parser and Applicants switches, acting on its link
            var entries = w.OverlayEntries();
            int line = entries.FindIndex(e => e.Link.Length > 0);
            int applicants = entries.FindIndex(e => e.Text == L.F("{0} overlay", L.T("Applicants")));
            check(line >= 0 && entries.Count(e => e.Link.Length > 0) == 1 && entries[line].Text == L.T("Needs Npcap") && entries[line].Link == L.T("Get it") && line > applicants,
                $"npcap: the Overlays menu has one line, \"{(line >= 0 ? entries[line].Text + " · " + entries[line].Link : "none")}\", under the Applicants switch");
            opened = null;
            if (line >= 0) entries[line].Do!();
            check(opened == MeterBlocks.NpcapUrl, $"npcap: the menu's Get it opens the download ({opened})");
            // Settings › DPS meter and › Applicants: the same line
            o = new OptionsWindow(w, OptionsWindow.Page.Meter) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -12000, Top = -12000, ShowActivated = false };
            o.Show(); DoEvents();
            o.Go(OptionsWindow.Page.Applicants); DoEvents();
            var meterLine = o.MeterBlockLineForTest(OptionsWindow.Page.Meter);
            var appLine = o.MeterBlockLineForTest(OptionsWindow.Page.Applicants);
            string want = L.T("Needs Npcap") + " · " + L.T("Get it");
            check(meterLine is { } ml && ml.Text == want && ml.Url == MeterBlocks.NpcapUrl && appLine is { } al && al.Text == want && al.Url == MeterBlocks.NpcapUrl,
                $"npcap: Settings › DPS meter and › Applicants say \"{want}\" with the download link ({meterLine?.Text} / {appLine?.Text})");

            // installed (simulated): everything goes, and the reader tries again at once (no restart)
            int tries = w.MeterRetriesForTest;
            installed = true;
            w.CheckMeterBlock(); o.MeterBlockChanged(); DoEvents();
            check(w.MeterBlockShown == MeterBlock.None && w.MeterCardForTest() == null && !w.OverlayEntries().Any(e => e.Link.Length > 0)
                && o.MeterBlockLineForTest(OptionsWindow.Page.Meter) == null && o.MeterBlockLineForTest(OptionsWindow.Page.Applicants) == null,
                "npcap: once installed the card, the menu line and both Settings lines go by themselves");
            check(w.MeterRetriesForTest == tries + 1, $"npcap: once installed the reader tries again at once instead of after its minute's wait ({w.MeterRetriesForTest - tries} retries)");
            check(w.MeterCardForTest() == null && !w.ParserPage.Children.OfType<FrameworkElement>().Any(e => e.Tag as string == MainWindow.MeterBlockTag),
                "npcap: the Parser page is as before once installed");

            // administrator without Npcap: the raw socket reads, nothing shows
            installed = false; admin = true;
            w.CheckMeterBlock(); DoEvents();
            check(w.MeterBlockShown == MeterBlock.None && w.MeterCardForTest() == null, "npcap: run as administrator without Npcap: nothing shows (Windows' own reading works then)");

            // Npcap for administrators only, not administrator: its own card and line
            admin = false; installed = true; adminOnly = true;
            w.CheckMeterBlock(); DoEvents();
            var ac = w.MeterCardForTest();
            var aline = w.OverlayEntries().FirstOrDefault(e => e.Link.Length > 0);
            check(w.MeterBlockShown == MeterBlock.AdminsOnly && ac?.Title == L.T("Npcap is set to admins only") && ac.Value.Buttons.Any(b => b.Tag == MeterBlocks.NpcapUrl) && aline?.Text == L.T("Npcap is set to admins only"),
                $"npcap: installed for administrators only: \"{ac?.Title}\" with Get Npcap, and the menu says so ({aline?.Text})");
            check(!(ac?.Buttons.Any(b => b.Tag == "runas") ?? false) || MeterBlocks.CanElevate(), "npcap: Restart as administrator only for an administrator under UAC's limited token (same account, same settings)");
            adminOnly = false; tries = w.MeterRetriesForTest;
            w.CheckMeterBlock(); DoEvents();
            check(w.MeterBlockShown == MeterBlock.None && w.MeterRetriesForTest == tries + 1, "npcap: set open to everyone: the card goes and the reader tries again");

            // the switch off: its own card, with the way to Settings
            w.Prefs.PacketReading = false;
            w.CheckMeterBlock(); DoEvents();
            var oc = w.MeterCardForTest();
            check(w.MeterBlockShown == MeterBlock.Off && oc?.Title == L.T("The DPS meter is off") && oc.Value.Buttons.Count == 1 && oc.Value.Buttons[0].Text == L.T("Open Settings")
                && w.OverlayEntries().FirstOrDefault(e => e.Link.Length > 0)?.Text == L.T("Turned off in Settings › Game reading"),
                $"npcap: Read the game's network traffic off: \"{oc?.Title}\" with Open Settings, and the menu line");
            w.Prefs.PacketReading = true;
            w.CheckMeterBlock(); DoEvents();
            check(w.MeterBlockShown == MeterBlock.None, "npcap: switched back on: nothing shows");

            // the cases only their own facts tell
            var two = TimeSpan.FromMinutes(2); var three = MeterBlocks.NoServerAfter;
            const string waiting = "Waiting for the game to connect to its server";
            {
                var ps = System.Windows.Forms.Screen.PrimaryScreen!.Bounds;
                check(MeterBlocks.LargerThanItsScreen(new System.Drawing.Rectangle(ps.X, ps.Y, ps.Width * 2, ps.Height * 3 / 2))
                    && !MeterBlocks.LargerThanItsScreen(ps) && !MeterBlocks.LargerThanItsScreen(new System.Drawing.Rectangle(ps.X + 100, ps.Y + 100, ps.Width / 2, ps.Height / 2))
                    && !MeterBlocks.LargerThanItsScreen(null),
                    "fullscreen: a game window larger than its monitor (5120x2160 on 2560x1440) is exclusive fullscreen; one that fits, or none, is not");
            }
            check(MeterBlocks.Decide(true, false, true, false, false, waiting, two, false) == MeterBlock.None
                && MeterBlocks.Decide(true, false, true, false, false, waiting, three, false) == MeterBlock.NoServer
                && MeterBlocks.Decide(true, false, true, false, false, "Waiting for the game", null, false) == MeterBlock.None
                && MeterBlocks.Decide(true, true, true, false, false, "On (Npcap): counting", three, false) == MeterBlock.None,
                "npcap: the ping booster line only after 3 minutes of the game running with no server connection to follow, never while reading");
            check(MeterBlocks.Decide(true, false, true, false, false, "Off: Npcap: no network card has the address 10.0.0.2; the tracker isn't running as administrator", null, true) == MeterBlock.Restart
                && MeterBlocks.Decide(true, false, true, false, false, "Off: Npcap: no network card has the address 10.0.0.2; the tracker isn't running as administrator", null, false) == MeterBlock.None,
                "npcap: Restart only when Npcap came in during this run and opening it failed since; the same error otherwise shows nothing");
            check(MeterBlocks.Decide(false, false, false, false, false, "Off in Options", null, false) == MeterBlock.Off
                && MeterBlocks.Decide(true, false, false, true, false, "Waiting for the game", null, false) == MeterBlock.None
                && MeterBlocks.Decide(true, false, true, true, true, "Waiting for the game", null, false) == MeterBlock.None,
                "npcap: switched off says so first; an administrator sees nothing for Npcap missing or admins-only");

            // AION 2 in exclusive fullscreen: the window's card on every page and the menu's first line, kept while the game
            // is behind (nothing can be told then), gone once it is seen in front out of exclusive fullscreen
            fullscreen = true;
            w.CheckMeterBlock(); DoEvents();
            var fsEntries = w.OverlayEntries();
            bool fsShown = w.FullscreenShownForTest && fsEntries.Count > 0 && fsEntries[0].Warn && fsEntries[0].Link.Length == 0
                && fsEntries[0].Text == L.T("AION 2 is in exclusive fullscreen, so the overlays can't show over it. In the game's graphics settings, set Display mode to Borderless.");
            fullscreen = null;   // the game behind another window
            w.CheckMeterBlock(); DoEvents();
            bool fsKept = w.FullscreenShownForTest;
            fullscreen = false;
            w.CheckMeterBlock(); DoEvents();
            check(fsShown && fsKept && !w.FullscreenShownForTest && w.OverlayEntries().FirstOrDefault()?.Warn != true,
                $"fullscreen: the window's card and the Overlays menu's first line while AION 2 is in exclusive fullscreen ({fsShown}), kept while it is behind ({fsKept}), gone once it is out");

            // first run: the look step carries the card while Npcap is missing, and Done is still there
            installed = false;
            w.CheckMeterBlock(); DoEvents();
            w.ShowWelcomeDemo(); DoEvents();
            w.WelcomeServerDoneForTest(); DoEvents();
            bool welcomeMissing = w.WelcomeMeterCardsForTest == 1 && w.WelcomeLookForTest.StepShown;
            fullscreen = true;
            w.CheckMeterBlock(); DoEvents();
            bool welcomeBoth = w.WelcomeMeterCardsForTest == 2 && !w.FullscreenShownForTest;
            installed = true; fullscreen = false;
            w.CheckMeterBlock(); DoEvents();
            check(welcomeMissing && welcomeBoth && !w.WelcomeMeterShownForTest,
                $"npcap: first run's look step shows the card while Npcap is missing ({welcomeMissing}), the fullscreen card beside it in its place ({welcomeBoth}), and drops them once fixed");
        }
        catch (Exception ex) { check(false, "npcap: " + ex); }
        finally
        {
            MeterBlocks.NpcapInstalled = inst0; MeterBlocks.IsAdmin = admin0; MeterBlocks.AdminOnly = only0; MeterBlocks.Forced = forced0; MainWindow.OpenForTest = open0;
            MeterBlocks.FullscreenProbe = fsProbe0; MeterBlocks.ForcedFullscreen = fsForced0;
            o?.Close();
            w.Close();
        }
    }

    /// <summary>
    /// ui-shot DIR --npcap: every case on the Parser page (a new player's empty page, and one with fights kept), the Overlays
    /// menu, Settings › DPS meter and › Applicants (fit.txt says whether a page scrolls), and first run's look step, in the
    /// language of KL_LANG. "none" is the page as it is with Npcap in.
    /// </summary>
    public static int ShotNpcap(Settings settings, string dir)
    {
        Ensure();
        Directory.CreateDirectory(dir);
        var forced0 = MeterBlocks.Forced; var fsForced0 = MeterBlocks.ForcedFullscreen;
        var fit = new List<string>();
        var w = new MainWindow(settings, watch: false) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -12000, Top = -12000, ShowActivated = false };
        try
        {
            w.Show();
            w.ShowDemo("Open world", null); DoEvents();
            var bg = Color.FromRgb(0x0A, 0x0B, 0x10);
            MeterBlocks.ForcedFullscreen = false;
            foreach (var b in MeterBlockCases)
            {
                string k = b.ToString().ToLowerInvariant();
                MeterBlocks.Forced = b;
                foreach (var st in new[] { "empty", "history" })
                {
                    w.ShowParserDemo(st);
                    w.CheckMeterBlock(); DoEvents();
                    Save((FrameworkElement)w.Content, Path.Combine(dir, $"parser-{st}-{k}.png"), 800 - 16);
                    fit.Add($"parser-{st}-{k}: {w.LowerScrollForTest.ExtentHeight:0} of {w.LowerScrollForTest.ViewportHeight:0}{(w.LowerScrollForTest.ExtentHeight > w.LowerScrollForTest.ViewportHeight + 1 ? "  SCROLLS" : "")}");
                }
                Save(new Border { Child = w.BuildMenu(w.OverlayEntries()), Padding = new Thickness(4) }, Path.Combine(dir, $"overlays-menu-{k}.png"), 320, bg);
                if (b != MeterBlock.Restart)
                    foreach (var page in new[] { OptionsWindow.Page.Meter, OptionsWindow.Page.Applicants })
                    {
                        var o = new OptionsWindow(w, page) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -12000, Top = -12000, ShowActivated = false };
                        o.Show();
                        IconCache.Drain(TimeSpan.FromSeconds(3));
                        DoEvents();
                        string name = $"options-{page.ToString().ToLowerInvariant()}-{k}";
                        Save((FrameworkElement)o.Content, Path.Combine(dir, name + ".png"), 0);
                        fit.Add($"{name}: {o.Scroller.ExtentHeight:0} of {o.Scroller.ViewportHeight:0}{(o.Scroller.ExtentHeight > o.Scroller.ViewportHeight + 1 ? "  SCROLLS" : "")}");
                        o.Close();
                    }
            }
            // AION 2 in exclusive fullscreen: the window's card (Farming and the Parser page with the Npcap card too), the menu
            MeterBlocks.ForcedFullscreen = true;
            foreach (var b in new[] { MeterBlock.None, MeterBlock.Missing })
            {
                MeterBlocks.Forced = b;
                string k = b.ToString().ToLowerInvariant();
                w.ShowDemo("Open world", null); w.CheckMeterBlock(); DoEvents();
                Save((FrameworkElement)w.Content, Path.Combine(dir, $"fullscreen-farming-{k}.png"), 800 - 16);
                w.ShowParserDemo("empty"); w.CheckMeterBlock(); DoEvents();
                Save((FrameworkElement)w.Content, Path.Combine(dir, $"fullscreen-parser-{k}.png"), 800 - 16);
                fit.Add($"fullscreen-parser-{k}: {w.LowerScrollForTest.ExtentHeight:0} of {w.LowerScrollForTest.ViewportHeight:0}{(w.LowerScrollForTest.ExtentHeight > w.LowerScrollForTest.ViewportHeight + 1 ? "  SCROLLS" : "")}");
                Save(new Border { Child = w.BuildMenu(w.OverlayEntries()), Padding = new Thickness(4) }, Path.Combine(dir, $"overlays-menu-fullscreen-{k}.png"), 320, bg);
            }
            // first run's look step, last (it takes the window over): with the cards, and without
            foreach (var (b, fs) in new[] { (MeterBlock.Missing, false), (MeterBlock.AdminsOnly, false), (MeterBlock.Missing, true), (MeterBlock.None, false) })
            {
                MeterBlocks.Forced = b; MeterBlocks.ForcedFullscreen = fs;
                w.CheckMeterBlock();
                w.ShowWelcomeDemo(); DoEvents();
                w.WelcomeServerDoneForTest(); DoEvents();
                Save((FrameworkElement)w.Content, Path.Combine(dir, $"welcome-look-{b.ToString().ToLowerInvariant()}{(fs ? "-fullscreen" : "")}.png"), 800 - 16);
            }
        }
        catch (Exception ex) { Console.WriteLine(ex); return 1; }
        finally { MeterBlocks.Forced = forced0; MeterBlocks.ForcedFullscreen = fsForced0; w.Close(); }
        File.WriteAllLines(Path.Combine(dir, "fit.txt"), fit);
        Console.WriteLine($"wrote {Path.GetFullPath(dir)}");
        return 0;
    }
}
