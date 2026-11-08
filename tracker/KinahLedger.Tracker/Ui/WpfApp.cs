using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DRect = System.Drawing.Rectangle;

namespace KinahLedger.Ui;

/// <summary>Starts the WPF side without an App.xaml, so Program.Main stays the single entry point.</summary>
public static class WpfApp
{
    static Application Ensure()
    {
        if (Application.Current != null) return Application.Current;
        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/KinahLedger;component/Ui/Theme.xaml", UriKind.Relative) });
        Skins.Apply(KinahLedger.Session.UserPrefs.Load().Skin);   // the chosen look, over the base theme
        return app;
    }

    /// <summary>The real app lifecycle (window shown, message loop running) with an automatic start → wait → stop, to catch anything that kills the process.</summary>
    public static int Live(Settings settings, DRect? region, int ms)
    {
        var app = Ensure();
        app.DispatcherUnhandledException += (_, e) => { Console.WriteLine("UI exception: " + e.Exception); CrashLog.Write("ui", e.Exception); e.Handled = true; };
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Console.WriteLine("process exception: " + e.ExceptionObject);
        var w = new MainWindow(settings);
        string result = "window closed before the cycle finished";
        w.Loaded += async (_, _) =>
        {
            try
            {
                result = await w.DebugCycle(region, ms);
                await Task.Delay(1500);                // stay alive a moment after Stop: this is where the app vanished for the player
                result += " · app still alive after stop";
            }
            catch (Exception ex) { result = "cycle FAILED: " + ex; }
            w.ForceClose();
        };
        app.Exit += (_, e) => Console.WriteLine($"app exit code {e.ApplicationExitCode}");
        app.Run(w);
        Console.WriteLine(result);
        return result.Contains("still alive") ? 0 : 1;
    }

    /// <summary>
    /// The window, as a double-click opens it. One tracker per Windows session: a second start shows the running one's
    /// window and quits. <paramref name="background"/> (Windows sign-in) starts in the tray; <paramref name="afterUpdate"/>
    /// waits for the version it replaces to finish closing.
    /// </summary>
    public static int Run(Settings settings, bool background = false, bool afterUpdate = false)
    {
        using var instance = Platform.SingleInstance.TryAcquire(afterUpdate ? TimeSpan.FromSeconds(30) : TimeSpan.Zero);
        if (instance == null)
        {
            if (!background) Platform.SingleInstance.ShowExisting();
            return 0;
        }
        if (Update.Updater.ExePath is { } exe) Platform.AutoStart.Repair(exe);   // the exe moved or was renamed since startup was turned on
        var app = Ensure();
        // an error in a button handler must not take the whole app down: log it, tell the player, carry on
        bool dialogUp = false; var dialogAt = DateTime.MinValue;
        app.DispatcherUnhandledException += (_, e) =>
        {
            CrashLog.Write("ui", e.Exception);
            e.Handled = true;
            // one dialog at a time and not a second within a minute of the last (a failure that repeats still lands in the log)
            if (dialogUp || DateTime.UtcNow - dialogAt < TimeSpan.FromMinutes(1)) return;
            dialogUp = true;
            try
            {
                var open = MessageBox.Show("Something went wrong, but Kinah Ledger is still running and carried on.\n\nThe details were saved to a log. Do you want to open the log folder? (If it keeps happening, the log helps to fix it.)", "Kinah Ledger", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
                if (open == MessageBoxResult.Yes) MainWindow.Open(CrashLog.Dir);
            }
            finally { dialogUp = false; dialogAt = DateTime.UtcNow; }
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) => CrashLog.Write("process", e.ExceptionObject as Exception ?? new Exception(e.ExceptionObject?.ToString()));
        TaskScheduler.UnobservedTaskException += (_, e) => { CrashLog.Write("task", e.Exception); e.SetObserved(); };
        // nothing reads without Windows' English text recognition: offer to add it before anything else
        if (!background && !Platform.OcrPack.HasEnglish()) Platform.OcrPack.OfferInstall(null);
        var w = new MainWindow(settings);
        w.StartUpdates();
        instance.OnShowRequested(w.ShowFromOtherStart);
        app.SessionEnding += (_, _) => w.PrepareForExit();   // sign-out or shutdown: never held up by "keep running in the tray"
        if (!background) return app.Run(w);
        w.StartInTray();
        app.MainWindow = w;
        return app.Run();
    }

    /// <summary>Starts and stops a session in-process (no clicks needed) and reports any exception, for finding crashes.</summary>
    public static int Cycle(Settings settings, DRect? region, int ms)
    {
        Ensure();
        var w = new MainWindow(settings) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -12000, Top = -12000, ShowActivated = false };
        w.Show();
        DoEvents();
        var task = w.DebugCycle(region, ms);
        var deadline = DateTime.UtcNow.AddMilliseconds(ms + 20000);
        while (!task.IsCompleted && DateTime.UtcNow < deadline) { DoEvents(); Thread.Sleep(20); }
        if (!task.IsCompleted) { Console.WriteLine("cycle did not finish in time"); return 2; }
        if (task.IsFaulted) { Console.WriteLine("cycle FAILED:\n" + task.Exception?.GetBaseException()); return 1; }
        Console.WriteLine("cycle ok: " + task.Result);
        w.Close();
        return 0;
    }

    public static DRect? PickRegion()
    {
        Ensure();
        return RegionPickerWindow.Pick();
    }

    /// <summary>Renders the window (and the panel) off-screen to PNG files so the layout can be reviewed without a desktop session.</summary>
    /// <summary>
    /// Drives the Options window the way a player would (switches, lists, theme chips, Save, Cancel) over a sample
    /// session and checks what reaches the main window. Nothing is saved to disk: the sample session never writes prefs.
    /// </summary>
    public static int OptionsSelfTest(Settings settings)
    {
        Ensure();
        int fails = 0;
        void Check(bool ok, string what) { Console.WriteLine((ok ? "ok    " : "FAIL  ") + what); if (!ok) fails++; }
        static void Click(System.Windows.Controls.Primitives.ButtonBase b) => b.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        static void Flip(System.Windows.Controls.CheckBox c) { c.IsChecked = c.IsChecked != true; Click(c); }

        var w = new MainWindow(settings, watch: false) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -12000, Top = -12000, ShowActivated = false };
        w.Show();
        Check(w.PartForTest == "Farming", "the window opens on Farming (0.8.55)");
        w.ShowDemo();
        var startSkin = Skins.Current;
        // a known start: the window and tray choices off (0.7.6 turns two of them on for new installs)
        w.Prefs.CloseToTray = w.Prefs.MinimizeToTray = w.Prefs.HideWhenGameCloses = false;
        OptionsWindow Open() { var o = new OptionsWindow(w, OptionsWindow.Page.General) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -12000, Top = -12000, ShowActivated = false }; o.Show(); DoEvents(); return o; }

        var o = Open();
        Check(!o.SaveBtn.IsEnabled, "Save is off until something changes");
        Check(o.FollowChk.IsChecked == true && w.Prefs.FollowActivity, "Switch pages to what I'm doing: on by default");
        Flip(o.HideOnCloseChk);
        Check(o.SaveBtn.IsEnabled, "a change turns Save on");
        Flip(o.HideOnCloseChk);
        Check(!o.SaveBtn.IsEnabled, "changing it back turns Save off again");

        Flip(o.CloseToTrayChk); Flip(o.MinToTrayChk); Flip(o.HideOnCloseChk); Flip(o.FollowChk);
        o.GameStartBox.SelectedIndex = 1;
        Flip(o.ShareChk);
        var eu = Data.Servers.Regions.First(r => r.Key != w.RegionKey);
        o.RegionPick.SelectedItem = eu;
        var euServer = Data.Servers.In(eu.Key)[0];
        o.ServerPick.SelectedItem = euServer;
        Click(o.HotkeyOffBtn);
        var other = Skins.All.First(s => s.Key != startSkin).Key;
        Click((System.Windows.Controls.Button)o.SkinHost.Children[Array.FindIndex(Skins.All, s => s.Key == other)]);
        Check(Skins.Current == other, "a theme chip previews the theme at once");
        o.GearBox.Text = "1450";
        Check(!w.Prefs.CloseToTray && w.Prefs.Share && w.Prefs.GearScore != 1450 && w.Prefs.FollowActivity, "nothing applies before Save");
        Click(o.SaveBtn);
        DoEvents();
        Check(!o.IsVisible, "Save closes the window");
        var p = w.Prefs;
        Check(p.CloseToTray && p.MinimizeToTray && p.HideWhenGameCloses, "window and tray choices saved");
        Check(!p.FollowActivity, "Switch pages to what I'm doing: turned off and saved");
        Check(p.OnGameStart == "tray", "when Aion 2 starts: stay in the tray");
        Check(!p.Share && !w.AppSettings.Upload.Enabled, "sharing off reaches the uploader");
        Check(w.RegionKey == eu.Key && w.Server?.Label == euServer.Label, "server saved: " + w.Server?.Label);
        Check(p.Hotkey == "" && w.HotkeyText == "", "shortcut turned off");
        Check(p.Skin == other && Skins.Current == other, "theme saved");
        Check(p.GearScore == 1450 && Upload.RunPayload.Gear == "gs:1400-1599", "gear score saved and shared as its bracket: " + Upload.RunPayload.Gear);

        o = Open();
        Check(o.CloseToTrayChk.IsChecked == true && o.GameStartBox.SelectedIndex == 1 && o.ServerPick.SelectedItem is Data.GameServer gs && gs.Label == euServer.Label, "Options opens with the saved choices");
        Check(o.FollowChk.IsChecked == false, "...Switch pages to what I'm doing among them, off");
        Flip(o.HudChk);
        var third = Skins.All.First(s => s.Key != other).Key;
        Click((System.Windows.Controls.Button)o.SkinHost.Children[Array.FindIndex(Skins.All, s => s.Key == third)]);
        Check(Skins.Current == third, "second theme previewed");
        bool hudBefore = p.Hud;
        Click(o.CancelBtn);
        DoEvents();
        Check(!o.IsVisible, "Cancel closes the window");
        Check(p.Hud == hudBefore, "Cancel keeps the panel setting as it was");
        Check(Skins.Current == other, "Cancel puts the saved theme back");

        // the panel's figures: a chip adds one, Save hands it to the panel, the order stays the panel's
        o = Open();
        o.Go(OptionsWindow.Page.InGame);
        var before = HudFields.Parse(p.HudFields);
        var add = HudFields.All.First(f => !before.Contains(f.Key)).Key;
        Click((System.Windows.Controls.Button)o.HudFieldHost.Children[Array.FindIndex(HudFields.All, f => f.Key == add)]);
        Check(o.SaveBtn.IsEnabled && HudFields.Parse(p.HudFields).SequenceEqual(before), "a panel figure waits for Save");
        Click(o.SaveBtn);
        DoEvents();
        Check(HudFields.Parse(p.HudFields).Contains(add) && HudFields.Parse(p.HudFields).Count == before.Count + 1, "panel figure saved: " + p.HudFields);

        // the panel's lock (click-through over the game) follows Save like the other switches
        o = Open();
        o.Go(OptionsWindow.Page.InGame);
        bool lockedBefore = w.HudLocked;
        Check(o.HudLockChk.IsChecked == lockedBefore, "Lock the panel in place opens as saved: " + lockedBefore);
        Flip(o.HudLockChk);
        Check(o.SaveBtn.IsEnabled && w.HudLocked == lockedBefore, "the panel lock waits for Save");
        Click(o.SaveBtn);
        DoEvents();
        Check(w.HudLocked == !lockedBefore, "panel lock saved");

        // the panel's look (2026-10-25): the Strip until another is picked; the three drawn in Options; the Strip's figures grey out for the others
        o = Open();
        o.Go(OptionsWindow.Page.InGame);
        Check(w.HudLook == HudLooks.Strip && o.HudFieldHost.IsEnabled, "panel look: the Strip, with its figures to pick");
        Check(o.HudLookHost.Children.Count == 3 && o.HudLookPreview.Children.Count == 3, $"the three looks drawn in Options ({o.HudLookPreview.Children.Count})");
        Click((System.Windows.Controls.Button)o.HudLookHost.Children[Array.FindIndex(HudLooks.All, l => l.Key == HudLooks.Ticker)]);
        Check(o.SaveBtn.IsEnabled && w.HudLook == HudLooks.Strip && !o.HudFieldHost.IsEnabled && o.HudFieldsNote.Visibility == Visibility.Visible, "a panel look waits for Save; the Strip's figures grey out");
        Click(o.SaveBtn);
        DoEvents();
        Check(w.HudLook == HudLooks.Ticker, "panel look saved: " + w.HudLook);
        o = Open();
        o.Go(OptionsWindow.Page.InGame);
        Click((System.Windows.Controls.Button)o.HudLookHost.Children[0]);
        Click(o.SaveBtn);
        DoEvents();
        Check(w.HudLook == HudLooks.Strip, "back to the Strip");
        // the looks' pickups come from the feed: newest first, with the count, worth, grade and icon
        var picks = w.HudPickValuesNow();
        string Pk(int i, string k = "") => picks.GetValueOrDefault("pick" + i + k, "");
        Check(Enumerable.Range(0, HudLooks.Picks).Any(i => Pk(i) == "Spiritstone" && Pk(i, "Count") == "×1" && Pk(i, "Worth").StartsWith("+"))
              && Enumerable.Range(0, HudLooks.Picks).Any(i => Pk(i, "Kind") == "kinah" && Pk(i).EndsWith(" Kinah") && Pk(i, "Icon").EndsWith("Icon_gold.png"))
              && long.Parse(Pk(0, "At")) >= long.Parse(Pk(1, "At")),
            $"panel looks: the feed's pickups, newest first ({Pk(0)}, {Pk(1)}, {Pk(2)}, {Pk(3)})");

        // Options › In game › Rare drops (2026-10-26): the watch list and the chime wait for Save, and a watched Common item alerts
        OptionsWindow.SelfTestRareDrops(w, Open, Check);

        // Options › General › Show a recap when a run ends (2026-11-04): on by default, kept on Save, back on again
        o = Open();
        Check(o.RecapChk.IsChecked == true && p.RunRecap, "Show a recap when a run ends: on by default");
        Flip(o.RecapChk);
        Check(o.SaveBtn.IsEnabled && p.RunRecap, "the recap switch waits for Save");
        Click(o.SaveBtn);
        DoEvents();
        Check(!p.RunRecap, "recap turned off and saved");
        o = Open();
        Check(o.RecapChk.IsChecked == false, "Options opens with the recap off");
        Flip(o.RecapChk);
        Click(o.SaveBtn);
        DoEvents();
        Check(p.RunRecap, "recap back on");
        Check(w.SampleRecapCard() is System.Windows.Controls.Border, "the recap card builds");

        // Options › Game reading › Explain each count (Steven, 2026-11-04: "an option you can turn on"): off by default, the
        // line card's WHY IT WAS COUNTED only while it is on, kept on Save, off again
        o = Open();
        o.Go(OptionsWindow.Page.Reading);
        Check(o.PageReading.Visibility == Visibility.Visible && o.ExplainChk.IsChecked == false && !p.ExplainCounts, "Explain each count: on Game reading, off by default");
        Check(!w.SampleLineReportExplains(), "a line's card has no WHY IT WAS COUNTED with it off");
        Flip(o.ExplainChk);
        Check(o.SaveBtn.IsEnabled && !p.ExplainCounts, "the explain switch waits for Save");
        Click(o.SaveBtn);
        DoEvents();
        Check(p.ExplainCounts && w.SampleLineReportExplains(), "explain turned on and saved; the line's card says why");
        o = Open();
        Check(o.ExplainChk.IsChecked == true, "Options opens with explain on");
        Flip(o.ExplainChk);
        Click(o.SaveBtn);
        DoEvents();
        Check(!p.ExplainCounts && !w.SampleLineReportExplains(), "explain back off");

        // Options › Shortcuts & tips (2026-11-04): the three groups, the tips, the start/stop key as it is now
        OptionsWindow.SelfTestTips(w, Open, Check);

        // the panel's opacity and size: shown on the panel as the sliders move, kept on Save, put back by Cancel
        o = Open();
        o.Go(OptionsWindow.Page.InGame);
        double opacityBefore = p.HudOpacity, scaleBefore = w.HudScale;
        o.HudOpacity.Value = 0.5; o.HudScale.Value = 1.5;
        Check(o.SaveBtn.IsEnabled && p.HudOpacity == opacityBefore && w.HudScale == scaleBefore, "panel opacity and size wait for Save");
        Click(o.CancelBtn);
        DoEvents();
        Check(p.HudOpacity == opacityBefore && w.HudScale == scaleBefore, "Cancel keeps the panel's opacity and size");
        o = Open();
        o.Go(OptionsWindow.Page.InGame);
        Check(Math.Abs(o.HudOpacity.Value - opacityBefore) < 0.01 && Math.Abs(o.HudScale.Value - scaleBefore) < 0.01, "Options opens with the saved opacity and size");
        o.HudOpacity.Value = 0.5; o.HudScale.Value = 1.5;
        Click(o.SaveBtn);
        DoEvents();
        Check(Math.Abs(p.HudOpacity - 0.5) < 0.001 && Math.Abs(w.HudScale - 1.5) < 0.001, $"panel opacity and size saved: {p.HudOpacity:P0}, {w.HudScale:P0}");

        // Window size (0.10.11): shown on the window as the slider moves, kept on Save, put back by Cancel; the keys step it
        o = Open();
        Check(Math.Abs(o.WindowScale.Value - 1) < 0.001 && w.FitState.Scale == 1, "window size: 100% to start, drawn at full size");
        o.WindowScale.Value = 1.25;
        Check(o.SaveBtn.IsEnabled && Math.Abs(w.WindowScaleShownForTest - 1.25) < 0.001 && Math.Abs(w.FitState.Scale - 1.25) < 0.001 && p.WindowScale == 1, $"window size tried at once, saved later: drawn at {w.FitState.Scale:P0}");
        Click(o.CancelBtn);
        DoEvents();
        Check(p.WindowScale == 1 && w.FitState.Scale == 1, "Cancel draws the window at the saved size again");
        o = Open();
        o.WindowScale.Value = 1.25;
        Click(o.SaveBtn);
        DoEvents();
        Check(Math.Abs(p.WindowScale - 1.25) < 0.001 && Math.Abs(w.FitState.Scale - 1.25) < 0.001, $"window size saved: {p.WindowScale:P0}, drawn at {w.FitState.Scale:P0}");
        w.StepWindowScaleForTest(1);
        Check(Math.Abs(p.WindowScale - 1.35) < 0.001 && Math.Abs(w.FitState.Scale - 1.35) < 0.001, "Ctrl+= is a step bigger, kept");
        w.StepWindowScaleForTest(0);
        Check(p.WindowScale == 1 && w.FitState.Scale == 1, "Ctrl+0 is back to 100%");
        for (int i = 0; i < 9; i++) w.StepWindowScaleForTest(1);
        Check(Math.Abs(p.WindowScale - MainWindow.MaxWindowScale) < 0.001, $"the size stops at {MainWindow.MaxWindowScale:P0}");
        for (int i = 0; i < 12; i++) w.StepWindowScaleForTest(-1);
        Check(Math.Abs(p.WindowScale - MainWindow.MinWindowScale) < 0.001, $"and at {MainWindow.MinWindowScale:P0}");
        w.StepWindowScaleForTest(0);
        // the screen caps it: a 1,000 DIP screen holds Compact at about 140%, never less than 100%
        Environment.SetEnvironmentVariable("KL_SCREEN_W", "1000");
        o = Open();
        o.WindowScale.Value = 1.5;
        Check(w.FitState.Scale < 1.5 && w.FitState.Scale >= 1.3, $"a narrow screen caps the size: {w.FitState.Scale:P0} of 150%");
        Click(o.CancelBtn);
        DoEvents();
        Environment.SetEnvironmentVariable("KL_SCREEN_W", null);
        Check(w.FitState.Scale == 1, "and back to 100% on Cancel");

        // Keep above other windows (0.10.26): off to start, a switch in Options that waits for Save, and the same one in the ⋯ menu
        Check(!w.Topmost && !p.AlwaysOnTop, "keep above other windows: off to start");
        o = Open();
        Flip(o.TopmostChk);
        Check(o.SaveBtn.IsEnabled && !w.Topmost, "the switch waits for Save");
        Click(o.SaveBtn);
        DoEvents();
        Check(w.Topmost && p.AlwaysOnTop, "saved: the window is on top");
        var onTop = w.MoreEntries().First(e => e.Text == MainWindow.AlwaysOnTopText);
        Check(onTop.On == true, "the ⋯ menu shows the switch on");
        onTop.Do!();
        Check(!w.Topmost && !p.AlwaysOnTop && w.MoreEntries().First(e => e.Text == MainWindow.AlwaysOnTopText).On == false, "the ⋯ menu's switch turns it off at once");
        o = Open();
        Check(o.TopmostChk.IsChecked == false, "Options opens with it off again");
        Click(o.CancelBtn);
        DoEvents();
        // the pages by key (0.10.26): Ctrl+2 Crafting, Ctrl+3 Events, Ctrl+1 Farming
        w.PressForTest(Key.D2, ModifierKeys.Control); Check(w.PartForTest == "Crafting", "Ctrl+2 shows Crafting");
        w.PressForTest(Key.D3, ModifierKeys.Control); Check(w.PartForTest == "Events", "Ctrl+3 shows Events");
        w.PressForTest(Key.NumPad1, ModifierKeys.Control); Check(w.PartForTest == "Farming", "Ctrl+1 (the keypad's too) shows Farming");

        // Save as CSV (0.10.11): the ⋯ menu's entry, today's loot as the file holds it, and the runs the Events page shows
        Check(w.MoreEntries().Any(e => e.Text == MainWindow.SaveLootText), "the ⋯ menu has Save today's loot as CSV…");
        var lootCsv = w.LootCsvForTest() ?? "";
        var lootLines = lootCsv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Check(lootLines.Length > 3 && lootLines[0].TrimEnd('\r') == Session.CsvExport.LootHeader && lootLines[1].StartsWith("kinah,Kinah,"), $"today's loot as CSV: {lootLines.Length} lines, the Kinah first");
        Check(lootLines.Any(l => l.StartsWith("drop,Spiritstone,")) && lootLines.Any(l => l.StartsWith("gathered,")), "...with the sample day's drops and gathered items");
        var csvDir = TestTemp.Path("KinahLedger-options-test-csv"); Directory.CreateDirectory(csvDir);
        string? askedName = null;
        MainWindow.SaveCsvForTest = name => { askedName = name; return Path.Combine(csvDir, name); };
        w.ShowDemo("Events");
        DoEvents();
        w.SaveRunsCsvForTest();
        var runsPath = Path.Combine(csvDir, Session.CsvExport.FileName("runs", DateTime.Now));
        Check(askedName == Path.GetFileName(runsPath) && File.Exists(runsPath), $"the runs saved as {askedName}");
        var runsLines = File.Exists(runsPath) ? File.ReadAllLines(runsPath) : Array.Empty<string>();
        Check(runsLines.Length == w.ShownRunsForTest().Count + 1 && runsLines[0].TrimStart('\uFEFF') == Session.CsvExport.RunsHeader, $"...one line a run: {runsLines.Length - 1} runs, after the header");
        Check(runsLines.Skip(1).All(l => l.Split(',')[1] is "Expedition" or "Event"), "...each an expedition or an event");
        MainWindow.SaveCsvForTest = null;
        // the week (0.10.26): a row a day, seven days
        var weekLines = w.WeekCsvForTest().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Check(weekLines.Length == 8 && weekLines[0].TrimEnd('\r') == Session.CsvExport.WeekHeader, $"the week as CSV: {weekLines.Length - 1} days after the header");
        Check(w.MoreEntries().Any(e => e.Text == MainWindow.SaveWeekText), "the ⋯ menu has Save this week as CSV…");
        // crafts (0.10.26): This session's recipes, priced as the table is
        w.ShowDemo("Crafting");
        DoEvents();
        var craftLines = (w.CraftsCsvForTest() ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Check(craftLines.Length == 4 && craftLines[0].TrimEnd('\r') == Session.CsvExport.CraftsHeader, $"the crafts as CSV: {Math.Max(0, craftLines.Length - 1)} recipes after the header");
        Check(craftLines.Skip(1).Any(l => l.StartsWith("Orichalcum Greaves,2,1,1,")) && craftLines.Skip(1).All(l => l.Split(',')[7] is "yes" or "at least" or "no"), "...the sample's recipes with their cost known or not");
        Check(w.CraftResetTextForTest.Visibility == Visibility.Visible, "Save as CSV shows beside Start over once something was crafted");
        w.ShowDemo();
        DoEvents();

        // the panel's switch and figures are tried on the panel at once too (2026-10-25), and Cancel puts them back
        o = Open();
        o.Go(OptionsWindow.Page.InGame);
        bool hudSaved = p.Hud; var fieldsSaved = p.HudFields;
        Flip(o.HudChk);
        var onNow = HudFields.Parse(fieldsSaved);
        int chip = Array.FindIndex(HudFields.All, f => !onNow.Contains(f.Key));
        if (chip < 0) chip = Array.FindIndex(HudFields.All, f => onNow.Contains(f.Key));
        Click((System.Windows.Controls.Button)o.HudFieldHost.Children[chip]);
        Check(w.HudShownForTest == !hudSaved && w.HudFieldsShownForTest != fieldsSaved && p.Hud == hudSaved && p.HudFields == fieldsSaved,
            "the panel switch and a figure show at once, and wait for Save to be kept");
        Click(o.CancelBtn);
        DoEvents();
        Check(w.HudShownForTest == hudSaved && w.HudFieldsShownForTest == fieldsSaved, "Cancel puts the panel's switch and figures back");

        o = Open();
        Click(o.HotkeyResetBtn);
        Click(o.SaveBtn);
        DoEvents();
        Check(p.Hotkey == Platform.Hotkey.Default, "Default puts Ctrl+Shift+K back");
        // shortcut rules (2026-10-25): nothing Windows or typing needs is taken from every app on the PC
        {
            static bool Ok(System.Windows.Input.ModifierKeys m, System.Windows.Input.Key k) => Platform.Hotkey.IsUsable(m, k);
            const System.Windows.Input.ModifierKeys C = System.Windows.Input.ModifierKeys.Control, A = System.Windows.Input.ModifierKeys.Alt, S = System.Windows.Input.ModifierKeys.Shift, W = System.Windows.Input.ModifierKeys.Windows;
            Check(!Ok(C, System.Windows.Input.Key.C) && !Ok(C, System.Windows.Input.Key.V) && !Ok(C, System.Windows.Input.Key.Z) && !Ok(C, System.Windows.Input.Key.A) && !Ok(A, System.Windows.Input.Key.F)
                && !Ok(C | A, System.Windows.Input.Key.K) && !Ok(S, System.Windows.Input.Key.K) && !Ok(C, System.Windows.Input.Key.D1),
                "a letter or number needs Shift with Ctrl or Alt: Ctrl+C, Ctrl+V, Ctrl+Z, Ctrl+A, Alt+F, Ctrl+Alt+K, Shift+K, Ctrl+1 refused");
            Check(!Ok(A, System.Windows.Input.Key.Tab) && !Ok(A, System.Windows.Input.Key.Escape) && !Ok(C, System.Windows.Input.Key.Escape) && !Ok(A, System.Windows.Input.Key.Space)
                && !Ok(A, System.Windows.Input.Key.F4) && !Ok(W | S, System.Windows.Input.Key.S) && !Ok(S, System.Windows.Input.Key.Insert) && !Ok(C, System.Windows.Input.Key.Left),
                "system keys refused: Alt+Tab, Alt+Esc, Ctrl+Esc, Alt+Space, Alt+F4, Win+Shift+S, Shift+Insert, Ctrl+Left");
            Check(Ok(C | S, System.Windows.Input.Key.K) && Ok(A | S, System.Windows.Input.Key.R) && Ok(A, System.Windows.Input.Key.F9) && Ok(S, System.Windows.Input.Key.F8) && Ok(C | A, System.Windows.Input.Key.F2),
                "allowed: Ctrl+Shift+K, Alt+Shift+R, Alt+F9, Shift+F8, Ctrl+Alt+F2");
            Check(Platform.Hotkey.Parse("Ctrl+K") == null && Platform.Hotkey.Explain("Ctrl+K") is { Length: > 0 } && Platform.Hotkey.Explain("Ctrl+Shift+K") == null && Platform.Hotkey.Parse("Ctrl+Shift+K") != null,
                "a saved Ctrl+K is off with a reason; Ctrl+Shift+K still parses");
        }

        // Compact and Full view (0.7.6): the tables and pickups only in Full, which doubles the width; Crafting is one column either way
        w.ShowCompact(); DoEvents();
        // the layout's own width, not the window's: a small screen (the CI runner's) squeezes the window but not the layout
        double compactW = w.ActualWidth, compactLayout = w.HeadStack.Width;
        // Compact since 2026-10-28 (the calm Farming page, mock C): the live card and LOOT's slots, no tables; Full view adds them
        // Compact in the Quest look (2026-11-07): the hero, the chips, Next up and Just dropped; the bag opens from the loot chip, no tables
        Check(w.TablesCard.Visibility != Visibility.Visible && w.SummaryCard.Visibility != Visibility.Visible && w.TopDropsBox.Visibility != Visibility.Visible && w.Rows.Items.Count <= 3,
            "Compact: the hero and the chips, the bag behind its chip, no tables, Where next folded");
        w.ToggleChipForTest("bag"); DoEvents();
        Check(w.SummaryCard.Visibility == Visibility.Visible && w.PickupStrip.Visibility == Visibility.Visible && w.LootForTest().Slots >= 8, "...the loot chip opens the bag with its slots (two rows of four tiles)");
        w.ToggleChipForTest("bag"); DoEvents();
        Check(w.SummaryCard.Visibility != Visibility.Visible, "...and closes it again");
        w.ShowFull(); DoEvents();
        Check(w.QuestRight.Visibility == Visibility.Visible && w.SummaryCard.Visibility == Visibility.Visible && w.PickupStrip.Visibility == Visibility.Visible && w.TopDropsBox.Visibility == Visibility.Visible && w.Rows.Items.Count > 3 && w.HeadStack.Width > compactLayout * 1.8,
            $"Full view: the bag and the zone board in the second column, open, {compactW:0} -> {w.ActualWidth:0} px wide");
        w.ShowCompact(); DoEvents();
        Check(Math.Abs(w.HeadStack.Width - compactLayout) < 1, "back to Compact: the width it had");

        // the Farming page, calm (2026-10-28, Steven: "THIS IS A LOT OF INFO ON ONE WINDOW"): one period at a time in LOOT, one
        // way to write time, no zone chip under a minute, nothing said while all is well, no section with nothing in it
        w.ShowDemo(); w.ShowCompact(); DoEvents();   // the sample again, after the Options round above changed the server and said things
        var here = w.LootForTest();
        Check(here.Here == "This zone" && here.Slots >= 8 && here.Note.Contains("kinds · 42 min") && "KMB".Contains(here.Total[^1]) && !here.Total.Contains(','),
            $"LOOT, This zone: {here.Slots} slots, \"{here.Note}\", {here.Total}");
        Check(w.FootTotal.ToolTip is string tt && tt.Contains("Kinah picked up") && tt.Contains("Total, " + Data.PriceBook.Fee), "LOOT's total says how it adds up on hover");
        Check(w.TableTitle.Text == "Open world · 42 min" && !w.TableTitle.Text.Contains(':'), $"the card says how long in minutes: \"{w.TableTitle.Text}\"");
        Check(w.TodayStrip.Visibility != Visibility.Visible, "no Today strip over the Farming card: Today is LOOT's other side");
        Check(w.HealthLine.Visibility != Visibility.Visible && w.Summary.Visibility != Visibility.Visible && w.FeedHint.Visibility != Visibility.Visible && w.Status.Text.Length == 0,
            "nothing said while all is well: no Reading the chat, no skipped lines, no feed hint, no Last: line");
        Check(w.MoreInfo().Any(i => i.Text == "Reading the chat") && w.MoreInfo().Any(i => i.Text.StartsWith("Last: ")), "...they are in ⋯: " + string.Join(" | ", w.MoreInfo().Select(i => i.Text)));
        w.PickLootForTest(true); DoEvents();
        var day = w.LootForTest(); var chips = w.LootChipsForTest();
        Check(day.Note.Contains("h") && day.Note.Contains("farming") && !day.Note.Contains(':') && day.Slots >= 8 && chips.Count == 4 && chips[0] == "All zones",
            $"LOOT, Today: \"{day.Note}\" {day.Total}, {day.Slots} slots, chips {string.Join(" / ", chips)}");
        Check(!chips.Any(c => c.Contains("Drana")) && chips.Skip(1).All(c => c.EndsWith(" min") || c.EndsWith(" h")), "no chip for a zone under a minute; each says its minutes");
        // Sort  worth · amount · quality over the slots (Steven, 2026-11-05): by amount the counts fall along the slots, by
        // quality the grades do; the slots are the same ones, and worth puts them back as they were
        // worth first by name: the test runs with this PC's ui.json, where the player may have picked another order
        w.SetLootSortForTest(Session.LootOrder.Worth); DoEvents();
        var byWorth = w.LootOrderForTest.ToList();
        w.SetLootSortForTest(Session.LootOrder.Amount); DoEvents();
        var byAmount = w.LootOrderForTest.ToList();
        w.SetLootSortForTest(Session.LootOrder.Quality); DoEvents();
        var byQuality = w.LootOrderForTest.ToList();
        w.SetLootSortForTest(Session.LootOrder.Worth); DoEvents();
        // the drops and the gathered items are two groups (FROM KILLS, GATHERED), each in order on its own
        static bool Falls(List<(string Name, long Count, string Grade, bool Gathered)> l, Func<(string Name, long Count, string Grade, bool Gathered), long> f)
            => l.GroupBy(x => x.Gathered).All(g => g.Zip(g.Skip(1)).All(p => f(p.First) >= f(p.Second)));
        Check(byWorth.Count >= 10 && w.LootForTest().Slots == day.Slots && w.LootOrderForTest.SequenceEqual(byWorth)
            && byAmount.Count == byWorth.Count && Falls(byAmount, x => x.Count) && !byAmount.SequenceEqual(byWorth)
            && byQuality.Count == byWorth.Count && Falls(byQuality, x => Session.LootOrder.GradeRank(x.Grade)) && byQuality.Any(x => x.Grade.Length > 0),
            $"LOOT, Today sorted: by amount {string.Join(" ", byAmount.Where(x => !x.Gathered).Select(x => x.Count))} then gathered {string.Join(" ", byAmount.Where(x => x.Gathered).Select(x => x.Count))}, by quality {string.Join(" ", byQuality.Where(x => !x.Gathered).Select(x => x.Grade.Length > 0 ? x.Grade : "-"))}, by worth as before");
        // History (0.9, Ui/MainWindow.Week.cs): LOOT's Week side draws the last seven days with today picked, today's bar the
        // Today side's total; a click on a past bar picks that day, its line and its zones
        w.PickLootWeekForTest(); DoEvents();
        var wk = w.WeekForTest();
        Check(wk.Bars == 7 && wk.Picked == "Today" && wk.Line.StartsWith("TODAY") && wk.TodayFig == day.Total && w.FootTotalNote.Text.Contains(" to ") && ReferenceEquals(w.LootWeekBtn.Style, w.FindResource("SegOn")),
            $"LOOT, Week: {wk.Bars} bars, {wk.Picked} picked, \"{wk.Line}\", today's bar {wk.TodayFig} as Today's {day.Total}, \"{w.FootTotalNote.Text}\"");
        w.PickWeekDayForTest(2); DoEvents();
        var wk2 = w.WeekForTest();
        Check(wk2.Bars == 7 && wk2.Picked.Length > 0 && wk2.Picked != "Today" && wk2.Line.StartsWith(wk2.Picked.ToUpperInvariant()) && wk2.Chips.Count >= 2 && !wk2.Line.Contains('%'),
            $"...a click on a past bar picks it: {wk2.Picked}, \"{wk2.Line}\", chips {string.Join(" / ", wk2.Chips)}");
        // Compare (0.9.3, Ui/MainWindow.Compare.cs): off by default, so Week has no Days | Zones and the card no usual line;
        // on, Zones ranks the week's zones (none under ten minutes), the zone here picked; a second tick shows the pair and a verdict
        var c0 = w.CompareForTest();
        Check(!w.Prefs.CompareZones && !c0.Switch && c0.Rows.Count == 0 && c0.Usual == "", "Compare zones and runs: off by default, no Days | Zones on Week, no usual line");
        w.SetCompareForTest(true); w.PickLootWeekForTest(); DoEvents();
        Check(w.CompareForTest().Switch && w.WeekForTest().Bars == 7, "...on: Days | Zones over the same seven bars");
        w.PickZonesForTest(true); DoEvents();
        var c1 = w.CompareForTest();
        Check(c1.Rows.Count >= 3 && !c1.Rows.Contains("Drana Cultivation Area") && c1.Picked.SequenceEqual(new[] { "Western Cantas Valley" }) && !c1.Vs,
            $"...Zones: {string.Join(" / ", c1.Rows)}, the zone here picked ({string.Join(",", c1.Picked)}), no zone under 10 minutes");
        w.PickZoneRowForTest(c1.Rows[0] == "Western Cantas Valley" ? 1 : 0); DoEvents();
        var c2 = w.CompareForTest();
        Check(c2.Picked.Count == 2 && c2.Vs && c2.Verdict.Length > 0 && !c2.Verdict.Contains('%'), $"...a second tick: side by side, \"{c2.Verdict}\"");
        Check(c2.Usual.StartsWith("Your usual here") && (c2.Usual.Contains(" than usual") || c2.Usual.Contains("about your usual")) && !c2.Usual.Contains("today"), $"...the Farming card's usual here, set against the hour above: \"{c2.Usual}\"");
        // Where to farm now's rows (0.9.11): a hand on each, the zone here tinted, your best hour beside its figure; with Compare
        // on, a click turns LOOT to Week › Zones with that zone picked; one not farmed this week leaves the zone here picked and says so
        var wh = w.WhereForTest();
        Check(wh.Rows.Count >= 3 && wh.Hand && wh.Here == "Western Cantas Valley" && wh.Best.Count == 1 && wh.Best[0].StartsWith("your best ") && wh.Tip.Contains("Click to compare"),
            $"Where to farm now: rows {string.Join(" / ", wh.Rows)}, here \"{wh.Here}\", best {string.Join(",", wh.Best)}, hand {wh.Hand}");
        w.PickZonesForTest(false); DoEvents();   // LOOT back on Days, so the click is what turns it to Zones
        w.ClickWhereRowForTest("Western Cantas Valley"); DoEvents();
        var c3 = w.CompareForTest();
        Check(c3.Switch && c3.Rows.Count >= 3 && c3.Picked.SequenceEqual(new[] { "Western Cantas Valley" }) && ReferenceEquals(w.LootWeekBtn.Style, w.FindResource("SegOn")),
            $"...a click on a row opens Compare on it: picked {string.Join(",", c3.Picked)}");
        w.ClickWhereRowForTest(wh.Rows.First(r => !c3.Rows.Contains(r))); DoEvents();
        Check(w.CompareForTest().Picked.SequenceEqual(new[] { "Western Cantas Valley" }) && w.Status.Text.Contains("isn't in Compare yet"), $"...a zone not farmed this week: the zone here stays picked, \"{w.Status.Text}\"");
        // a row's site link: the zone's page through the site's /farming/zone/{zone} redirect, in the site's slug rule
        Check(MainWindow.Slug("Steel Titan's Grave") == "steel-titans-grave" && MainWindow.Slug("  Black Fragments Area - Abandoned Island’s ") == "black-fragments-area-abandoned-islands"
            && MainWindow.ZonePageUrl("Western Cantas Valley") == "https://kinahledger.com/farming/zone/western-cantas-valley",
            $"a zone's site address: {MainWindow.Slug("Steel Titan's Grave")}, {MainWindow.ZonePageUrl("Western Cantas Valley")}");
        w.PickZonesForTest(false); w.SetCompareForTest(false); DoEvents();
        Check(!w.CompareForTest().Switch && w.CompareForTest().Usual == "", "...off again: Week as before, no usual line");
        w.PickLootForTest(false); DoEvents();
        Check(w.LootForTest().Note == here.Note, "back to This zone");
        // 0.9.0 (Steven, 2026-10-30): no "1 to look at" pill, though the sample has a refused line; Report a miscount stays
        Check(w.AccuracyChip.Visibility == Visibility.Collapsed && ReferenceEquals(w.AccuracyChip.Parent, w.StatusRow) && w.MiscountText.Visibility == Visibility.Visible,
            "Accuracy: no pill in the footer for the sample's refused line, Report a miscount shown");
        Check(w.HudValues.GetValueOrDefault("loot", "").Length > 0, "panel: loot an hour beside a rate with Kinah in it");
        // rows update in place (2026-10-25): a redraw keeps the row controls and only hands them new words, so a tick with a
        // farming session on screen no longer rebuilds the tables (which is what made clicks and drags wait)
        var rowItems = w.Rows.ItemsSource as System.Collections.ObjectModel.ObservableCollection<RowItem>;
        // the table itself is not drawn since the Quest look (2026-11-07: the bag's tiles and the odds badges show its figures), so no container to ask for
        var item0 = rowItems is { Count: > 1 } ? rowItems[0] : null;
        Check(rowItems != null && item0 != null, $"the drops table holds row items ({rowItems?.Count} rows)");
        w.ShowFull(); DoEvents();
        Check(rowItems != null && ReferenceEquals(rowItems[0], item0) && w.Rows.Items.Count > 3,
            "Full view adds rows under the ones already there, keeping the first");
        var was = item0!.Row;
        MainWindow.SyncRows(w.Rows, new[] { was with { Drops = "999" }, rowItems![1].Row });
        DoEvents();
        Check(ReferenceEquals(rowItems[0], item0) && item0.Drops == "999" && w.Rows.Items.Count == 2,
            "new words land on the same row item; extra rows come off the end");
        Check(!item0.Set(was with { Drops = "999" }), "the same words again change nothing");
        w.ShowCompact(); DoEvents();
        Check(ReferenceEquals(rowItems[0], item0) && item0.Drops == was.Drops && w.Rows.Items.Count <= 3,
            "a redraw puts the count's words back on the same item");
        // an icon whose download failed asks again after RetrySeconds, not at the next start (2026-10-25: Expert's gear rows lettered all session)
        var badUrl = "http://127.0.0.1:9/no-such-icon-" + Guid.NewGuid().ToString("N") + ".png";
        int fetchesBefore = IconCache.Fetches;
        var slot = IconCache.Get(badUrl);
        IconCache.Drain(TimeSpan.FromSeconds(15)); DoEvents();
        Check(ReferenceEquals(IconCache.Get(badUrl), slot) && IconCache.Fetches == fetchesBefore + 1 && slot.Image == null && slot.FailedAt != default && !slot.Busy,
            "a failed icon download is remembered and not asked again at once");
        slot.FailedAt = DateTime.UtcNow.AddSeconds(-IconCache.RetrySeconds - 1);
        IconCache.Get(badUrl); IconCache.Drain(TimeSpan.FromSeconds(15)); DoEvents();
        Check(IconCache.Fetches == fetchesBefore + 2 && slot.Image == null, "...and asked again once the retry time has passed");

        // a drag pins the window's size until the drop, so the tick's layout can't stall the drag
        var sizing = w.SizeToContent;
        Check(w.OnSizeMove(MainWindow.WM_ENTERSIZEMOVE) && w.SizeToContent == SizeToContent.Manual, "a drag starting pins the size");
        Check(w.OnSizeMove(MainWindow.WM_EXITSIZEMOVE) && w.SizeToContent == sizing && sizing == SizeToContent.WidthAndHeight, "the drop sizes to content again");

        w.ShowDemo("Gathering"); DoEvents();
        Check(w.HudValues.GetValueOrDefault("loot", "") == "" && w.HudValues.GetValueOrDefault("rate", "") == w.HeadRate.Text,
            $"panel, gathering: the hour once ({w.HudValues.GetValueOrDefault("rate", "")}, the window's {w.HeadRate.Text}), no second loot figure");
        Check(w.HudValues.GetValueOrDefault("warm", "") == "" && w.HeadRate.Opacity == 1, "a 42-minute count shows its rate, not warming up");

        // Undo instead of "Are you sure?" (2026-10-25): Clear the tables happens at once with a bar, and Undo puts them back
        w.ShowDemo(); DoEvents();
        _ = w.ClearTablesForTest(); DoEvents();
        Check(w.UndoShowing == "Everything cleared" && w.TablesClearedForUndo, "Clear everything: cleared at once, with an Undo bar");
        w.PressUndo(); DoEvents();
        Check(w.UndoShowing == null && !w.TablesClearedForUndo, "Undo puts the tables back and the bar goes");
        // 0.9.10: a "done" shows in the toast over the page, not the faint status line; the window's keys and their tooltips
        Check(w.ToastShowing == "The farming tables are back as they were." && w.Status.Text != w.ToastShowing, $"a done is said in the toast: \"{w.ToastShowing}\"");
        var keys = w.InputBindings.OfType<System.Windows.Input.KeyBinding>().Select(k => (k.Modifiers, k.Key)).ToList();
        Check(keys.Contains((System.Windows.Input.ModifierKeys.Control, System.Windows.Input.Key.OemComma)) && keys.Contains((System.Windows.Input.ModifierKeys.Control, System.Windows.Input.Key.N))
              && keys.Contains((System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Shift, System.Windows.Input.Key.C)), "Ctrl+, Ctrl+N and Ctrl+Shift+C are bound in the window");
        Check(w.OptionsBtn.ToolTip is string ot && ot.Contains(MainWindow.OptionsKey) && w.MoreEntries().Any(e => e.Text == MainWindow.ClearAllText && e.Key == MainWindow.NewSessionKey)
              && w.ShareCardBtn.ToolTip is string st && st.Contains(MainWindow.CopyPictureKey), "each button's tooltip says its key, and ⋯ › Clear everything says Ctrl+N");

        // 0.9.13: Start run is the fresh start. New session left the card (it is Clear everything in ⋯ and the tray); Start run
        // with counts on the tables clears them at once with an Undo bar, and Undo brings them back and cancels the run
        {
            static IEnumerable<DependencyObject> Kids(DependencyObject d) => new[] { d }.Concat(LogicalTreeHelper.GetChildren(d).OfType<DependencyObject>().SelectMany(Kids));
            Check(!Kids(w).OfType<System.Windows.Controls.Button>().Any(b => b.Content as string == "New session" || System.Windows.Automation.AutomationProperties.GetName(b) == "New session"),
                "the live card has no New session button");
            w.ShowDemo(); DoEvents();
            int counted = w.ShownEventsForTest;
            Check(w.StartBtn.ToolTip is string sTip && sTip.Contains("clears the counts"), "Start run's tooltip says it clears the counts when there are some");
            _ = w.StartFromZeroForTest(); DoEvents();
            Check(counted > 0 && w.UndoShowing == "Run started from zero" && w.ShownEventsForTest == 0 && w.StartBtn.Content as string == "Stop run",
                $"Start run with counts: the tables start at zero, with an Undo bar ({counted} → {w.ShownEventsForTest}, \"{w.UndoShowing}\")");
            w.PressUndo(); DoEvents();
            Check(w.UndoShowing == null && w.ShownEventsForTest == counted && w.StartBtn.Content as string == "Start run",
                $"Undo brings the counts back ({w.ShownEventsForTest}) and the run is not started");
        }
        Check(System.Windows.Controls.ToolTipService.GetShowOnDisabled(w.StartBtn) && System.Windows.Controls.ToolTipService.GetShowOnDisabled(w.ShareCardBtn), "Start run and Share say why while they are off");

        // Share (0.9.3, ShareA): the Farming card's button says Share and opens the sheet inside the card (never a window or a
        // menu) on This session, the character name off by default; typed but off it is nowhere, on it is in the head line
        {
            var keep = w.SharePrefsForTest;
            w.SharePrefsForTest = (false, "");
            Check(System.Windows.Automation.AutomationProperties.GetName(w.ShareCardBtn) == "Share" && w.ShareCardBtn.IsEnabled, "the header's round Share button is named Share");
            w.ToggleShareForTest("Farming"); DoEvents();
            var sh = w.ShareForTest();
            Check(sh.Open && sh.Host == "ShareHost" && sh.Scope == KinahLedger.Session.SharePicture.Session && !sh.Switch, $"Share opens inside the farming card on This session, name off ({sh.Host} {sh.Scope})");
            Check(new[] { "Copy picture", "Save picture", "Copy as text", "Close Share", "This session", "Last run", "This week" }.All(sh.Buttons.Contains),
                "...with the switch, Copy picture, Save picture, Copy as text and ✕: " + string.Join(", ", sh.Buttons));
            Check(w.SharePicForTest(KinahLedger.Session.SharePicture.Session) is { } sp && sp.Made != "0" && sp.Sub.Contains("kill") && sp.Site == "kinahledger.com",
                "This session's picture has the count's total and kills");
            w.SharePrefsForTest = (false, "Aerin"); DoEvents();
            Check(!w.ShareForTest().Head.Contains("Aerin") && w.SharePicForTest(KinahLedger.Session.SharePicture.Session) is { } off && !KinahLedger.Session.SharePicture.Text(off).Contains("Aerin"),
                "a name typed but the switch off: on neither the picture nor the text");
            w.SharePrefsForTest = (true, "Aerin"); DoEvents();
            Check(w.ShareForTest().Head.Contains("Aerin") && w.ShareForTest().Switch, "the switch on: the name in the picture's head line");
            w.PickShareScopeForTest(KinahLedger.Session.SharePicture.Week); DoEvents();
            Check(w.ShareForTest().Scope == KinahLedger.Session.SharePicture.Week, "This week picked");
            w.ToggleShareForTest("Farming"); DoEvents();
            Check(!w.ShareForTest().Open && w.ShareHost.Visibility != Visibility.Visible, "Share again closes the sheet");
            w.SharePrefsForTest = keep;
        }

        // GOALS (0.9, Goals B of kinah-bench/mock-goals-history): no box with no goal; sample goals as slots between the
        // actions and LOOT; the "+" slot while fewer than three; a goal reached said once; the box gone with the last goal
        {
            // no goal: the slim "Set a goal for today" row in the box's place (2026-10-30), between the actions and LOOT
            // Next up in the Quest look (2026-11-07): with no goal the sample's milestones fill the box, so the slim row waits for an empty count
            var none = w.GoalsForTest();
            Check(none.Shown && none.Slots == 0 && !none.Slim && none.Placed && !none.Add, "Next up: no goal, the quests fill the card, Set a goal lives on the Quests page, the slim row away");
            Check(w.GoalsSlim.Focusable && System.Windows.Automation.AutomationProperties.GetName(w.GoalsSlim) == "Set a goal for today",
                "the slim GOALS row: keyboard focusable, named Set a goal for today");
            w.ShowGoalsSample(3); DoEvents();
            var three = w.GoalsForTest();
            Check(three.Shown && !three.Slim && three.Slots == 2 && !three.Add && three.Placed && three.Note.StartsWith("today · 1 of 3 done") && three.Note.Contains(" more"),
                $"Next up: three goals, two rows under the chips (two bars at most), the third behind the head, no Set a goal row, \"{three.Note}\"");
            w.ShowGoalsSample(2); DoEvents();
            var two = w.GoalsForTest();
            Check(two.Shown && two.Slots == 2 && !two.Add && two.Note.StartsWith("today · 0 of 2 done"), $"Next up: two goal rows fill the card, the quests wait behind the head (Set a goal on the Quests page), \"{two.Note}\"");
            Check(w.LootForTest().Slots >= 8, "the bag still fills under Next up");
            // reached between two looks: said once on the status line (and the panel), never again that day
            w.SetGoalsForTest(new[] { new Session.Goal { Kind = Session.Goal.KindRuns, Which = Session.Goal.Expeditions, Target = 3 } });
            var said = w.GoalTickForTest(); var again = w.GoalTickForTest();
            Check(said.Count == 1 && said[0] == "Goal reached · 3 expeditions" && again.Count == 0 && w.Status.Text == said[0],
                $"a goal reached is said once: \"{string.Join(" | ", said)}\", then {again.Count}");
            Check(w.GoalCard(new Session.Goal { Kind = Session.Goal.KindKinah, Target = 2_000_000 }) is { } gc && gc.Child is System.Windows.Controls.StackPanel goalBody && goalBody.Children.Count >= 6,
                "a goal's hover card: band, bar, TODAY, Have, To go, pace");
            // no right-click anywhere (2026-10-31): a goal's click opens the sheet with Remove goal, the ⋯ menu is the card's own
            w.ShowGoalsSample(3); DoEvents();
            var rightClick = w.RightClickMenusForTest();
            Check(rightClick.Count == 0, "no right-click menus in the window: " + string.Join(", ", rightClick));
            static IEnumerable<DependencyObject> Tree(DependencyObject d) => new[] { d }.Concat(LogicalTreeHelper.GetChildren(d).OfType<DependencyObject>().SelectMany(Tree));
            var change = w.GoalChangeSheetForTest(new Session.Goal { Kind = Session.Goal.KindKinah, Target = 2_000_000 });
            Check(Tree(change).OfType<System.Windows.Controls.Button>().Any(b => b.Content as string == "Remove goal") && Tree(change).OfType<System.Windows.Controls.TextBlock>().Any(t => t.Text == "Change this goal"),
                "a goal's click: the sheet says Change this goal and has Remove goal");
            var more = w.MoreEntries();
            Check(more.Any(e => e.Text == "All zones" && e.On != null) && more.Any(e => e.Text == "Clear everything" && e.Do != null) && more.Any(e => e.Text == "Set a goal…" && e.Do == null && e.Sub.Length > 0),
                "the ⋯ menu: All zones a switch, Clear everything, Set a goal off (with why) once three are set: " + string.Join(" | ", more.Select(e => e.Text)));
            Check(w.BuildMenu(more) is { } menuCard && Tree(menuCard).OfType<System.Windows.Controls.ContextMenu>().Count() == 0 && Tree(menuCard).OfType<System.Windows.Controls.MenuItem>().Count() == 0,
                "the ⋯ menu is drawn in the card kit, no Windows menu items");
            w.ShowGoalsSample(2); DoEvents();
            Check(w.MoreEntries().First().Do != null, "with room for a goal, ⋯ › Set a goal… opens the sheet");
            w.ShowPartForTest("Crafting"); DoEvents();
            Check(w.GoalsForTest() is { Shown: false, Slim: false }, "GOALS (and the slim row) only on the Farming page");
            w.ShowPartForTest("Farming"); DoEvents();
            w.SetGoalsForTest(null); DoEvents();
            Check(w.GoalsForTest() is { Shown: true, Slim: false, Slots: 0 }, "the last goal removed: Next up keeps the pace's milestones, no goal rows (Quest)");
        }

        // Quests (phase 1, 2026-11-08; the Quests page and three bars later that day, two bars in 0.10.28, Ui/MainWindow.QuestsPage.cs):
        // Next up holds two bars at most (Kinah this session, then the crafting material farmed last, or a pin); a click opens a quest's tree; the first look
        // credits the steps passed without a word; the rest, Daily, All time, Your goals and Done are the Quests page; a pin takes a
        // Next up row; Options > General > Quests off takes it all away; the score on the head and in Options > Sharing
        {
            var q = w.QuestsForTest();
            Check(q.Rows >= 1 && q.AtRest <= 2 && q.Kinds.StartsWith("kinah,material") && q.Tree == 0, $"Next up: {q.Rows} quest rows, {q.AtRest} on the card (two at most), kinds {q.Kinds}");
            Check(w.QuestsNowForTest.Count >= 2 && w.QuestsNowForTest.Take(2).All(x => x.Horizon == Session.Horizon.Session), "the two on the card are this session's");
            w.ToggleQuestTreeForTest(1); DoEvents();
            var t = w.QuestsForTest();
            Check(t.Tree >= 3 && t.AtRest <= 2, $"a click on the second row opens its tree: {t.Tree} tree rows, still {t.AtRest} on the card");
            w.ToggleQuestTreeForTest(1); DoEvents();
            Check(w.QuestsForTest().Tree == 0, "a second click folds the tree");
            w.QuestTickForTest(); DoEvents();
            var again = w.QuestTickForTest(); DoEvents();
            var d = w.QuestsForTest();
            Check(again.Count == 0 && d.Note.Contains("pts this session") && d.AtRest <= 2, $"the first look credited the steps passed before it quietly; a look with nothing new says nothing ({again.Count} said); the head says the score: \"{d.Note}\"; done: {w.QuestsDoneForTest()}");
            double farmHeight = w.ActualHeight;
            w.ShowPartForTest("Quests"); DoEvents();
            var pg = w.QuestsPageForTest();
            Check(!pg.Off && pg.Active >= 3 && pg.Daily >= 1 && pg.AllTime >= 1 && pg.Done > 0, $"the Quests page: {pg.Active} active, {pg.Daily} daily, {pg.AllTime} all time, {pg.Done} done");
            // 0.10.28 (Steven): the page scrolls inside the window instead of growing it down the screen
            Check(farmHeight >= 500 && w.MaxHeight <= farmHeight && w.ActualHeight <= farmHeight + 1 && w.LowerScroll.ScrollableHeight > 0,
                $"the Quests page keeps the Farming page's height and scrolls inside it: window {w.ActualHeight:0} (Farming was {farmHeight:0}), {w.LowerScroll.ScrollableHeight:0} to scroll");
            var pinChain = w.QuestsNowForTest[3].Chain;
            w.TogglePinForTest(3); DoEvents();
            var pinned = w.QuestsPageForTest();
            w.ShowPartForTest("Farming"); DoEvents();
            Check(pinned.Pins == 1 && w.QuestsNowForTest[1].Chain == pinChain && w.QuestsForTest().AtRest <= 2, $"a pin takes a Next up row: {pinChain} is row 2 ({w.QuestsForTest().AtRest} on the card)");
            w.TogglePinForTest(1); DoEvents();
            Check(w.QuestsPageForTest().Pins == 0, "...and unpins");
            Check(w.QuestScoreLine().Contains("this session") && w.QuestScoreLine().Contains("this week"), "the score line: this session, today, this week");
            var so = Open(); so.Go(OptionsWindow.Page.Sharing); DoEvents();
            Check(so.ScoreText.Text == w.QuestScoreLine() && so.ScoreText.Text.Contains("pts"), $"Options > Sharing: Your score under the profile link card: \"{so.ScoreText.Text}\"");
            so.Close();
            Check(w.QuestCard(w.QuestsNowForTest[1]) is { } qc && qc.Child is System.Windows.Controls.StackPanel qb && qb.Children.Count >= 8, "a quest's hover card: band, bar, the horizon, Have, To go, pace, POINTS, Worth, Tier, unlocks");
            // Options > General > Quests off: the tab and the rows go; on again brings them back
            var og = Open(); og.Go(OptionsWindow.Page.General); Flip(og.QuestsChk); Click(og.SaveBtn); DoEvents();
            Check(w.QuestsTab.Visibility != Visibility.Visible && w.QuestsForTest().Rows == 0, "Options > General > Quests off: no tab, no quest rows on Next up");
            og = Open(); og.Go(OptionsWindow.Page.General); Flip(og.QuestsChk); Click(og.SaveBtn); DoEvents();
            Check(w.QuestsTab.Visibility == Visibility.Visible && w.QuestsForTest().Rows > 0, "...and on again");
            // the Crafting page's Next up carries the two materials' quests while the farming count is on
            w.ShowDemo("Crafting"); DoEvents();
            Check(w.CraftNextForTest().Split(',').Count(x => x == "quest") == 2, $"Crafting's Next up: the two materials' quest rows: {w.CraftNextForTest()}");
            w.ShowDemo(); DoEvents();
        }

        // What's new (0.9.12, Ui/MainWindow.WhatsNew.cs): the notes' bullets as bold lead and rest; once per version, never on
        // a fresh install; Got it stores the version; Farming only; six bullets, then "and N more"; the link to the release
        {
            const string sample = "Kinah Ledger tracker 9.9.9: a sample.\r\n\r\n- **A zone row is a button.** Hover a row and it lights up.\r\n"
                + "- **Each row's tooltip says what a click does.**\r\n* **Links keep their text.** See [the site](https://kinahledger.com) and `code`.\r\n"
                + "- A bullet with no bold lead.\r\n\r\nInstalled copies update by themselves in the background.\r\n";
            var notes = Platform.WhatsNew.Parse(sample);
            Check(notes.Count == 4 && notes[0] == new Platform.WhatsNew.Note("A zone row is a button.", "Hover a row and it lights up.")
                && notes[1] == new Platform.WhatsNew.Note("Each row's tooltip says what a click does.", "")
                && notes[2].Rest == "See the site and code." && notes[3] == new Platform.WhatsNew.Note("", "A bullet with no bold lead."),
                "What's new: bullets split into lead and rest, no summary or closing line: " + string.Join(" | ", notes.Select(n => $"[{n.Lead}] {n.Rest}")));
            Check(Platform.WhatsNew.Parse("Only a summary.\n\nInstalled copies update by themselves in the background.").Count == 0 && Platform.WhatsNew.Parse("").Count == 0,
                "What's new: notes with no bullets give none");
            Check(Platform.WhatsNew.Built().Count > 0, $"What's new: this build carries its notes (release-notes/v{Upload.RunPayload.AppVersion}.md)");
            var cur = Upload.RunPayload.AppVersion; var keepSeen = w.Prefs.WhatsNewSeen; var keepFull = w.Prefs.FullView;
            w.ShowPartForTest("Farming"); DoEvents();
            var fresh = w.WhatsNewStartForTest("", "", sample);
            Check(!fresh.Shown && w.Prefs.WhatsNewSeen == cur, $"What's new: a fresh install shows nothing and counts {cur} as seen ({w.Prefs.WhatsNewSeen})");
            var same = w.WhatsNewStartForTest(cur, cur, sample);
            Check(!same.Shown, "What's new: not shown when the stored version is this one");
            var older = w.WhatsNewStartForTest("", "0.9.11", sample);
            Check(older.Shown, "What's new: shown after an update from a version before the card (nothing stored, an older one ran)");
            var shown = w.WhatsNewStartForTest("0.9.11", cur, sample);
            Check(shown.Shown && shown.Title == $"WHAT'S NEW IN {cur}" && shown.Lines == 4 && shown.More == "" && w.WhatsNewCard.Parent == w.LeftCol && w.LeftCol.Children.IndexOf(w.WhatsNewCard) == 0,
                $"What's new: shown when the stored version differs, at the top of the left column: \"{shown.Title}\", {shown.Lines} bullets");
            string? opened = null; MainWindow.OpenForTest = u => opened = u;
            w.WhatsNewAllLink.RaiseEvent(new RoutedEventArgs(System.Windows.Documents.Hyperlink.ClickEvent));
            MainWindow.OpenForTest = null;
            Check(opened == $"https://github.com/StevenMacchia/kinah-ledger/releases/tag/v{cur}", $"What's new: All notes opens the release ({opened})");
            w.ShowPartForTest("Crafting"); DoEvents();
            Check(!w.WhatsNewForTest().Shown, "What's new: not on the Crafting page");
            w.ShowPartForTest("Farming"); w.ShowFull(); DoEvents();
            Check(w.WhatsNewForTest().Shown, "What's new: back on Farming, in Full view too");
            if (!keepFull) w.ShowCompact();
            DoEvents();
            Click(w.WhatsNewGotIt); DoEvents();

            Check(!w.WhatsNewForTest().Shown && w.Prefs.WhatsNewSeen == cur, $"What's new: Got it puts it away and stores {cur} ({w.Prefs.WhatsNewSeen})");
            var many = string.Join("\n", Enumerable.Range(1, 8).Select(i => $"- **Bullet {i}.** rest {i}."));
            var eight = w.WhatsNewStartForTest("0.0.1", cur, many);
            Check(eight.Shown && eight.Lines == MainWindow.WhatsNewMax && eight.More == "and 2 more in the notes", $"What's new: {eight.Lines} bullets, then \"{eight.More}\"");
            Click(w.WhatsNewGotIt); DoEvents();
            Check(!w.WhatsNewStartForTest("0.0.1", cur, "Summary only.").Shown, "What's new: a notes file with no bullets shows nothing");
            w.Prefs.WhatsNewSeen = keepSeen;
        }

        // the panel's crafting line (2026-10-26): the Card's and the Ticker's craft values from a sample ledger that adds up,

        // summed as the Crafting part sums it, at the Crafting render's prices
        w.ShowDemo("Crafting"); DoEvents();
        {
            var (cv, sums, windowNet) = w.HudCraftCheck();
            string C(string k) => cv.GetValueOrDefault(k, "");
            long cost = Data.RecipeBook.CostPerTry("Orichalcum Greaves", out _) ?? -1;
            long sells = Data.PriceBook.Price("Orichalcum Greaves") is long gp ? (long)Math.Round(gp * Data.PriceBook.SellerKeeps) : -1;
            Check(C("craft") == "1" && C("craftTime") == "18:05" && C("craftRecipe") == "Orichalcum Greaves" && C("craftIcon").Length > 0
                  && C("craftTries") == "12" && C("craftOk") == "9" && C("craftFail") == "3",
                $"panel crafting: on, {C("craftTime")}, {C("craftRecipe")}, {C("craftTries")} crafts, {C("craftOk")} worked, {C("craftFail")} failed");
            Check(cost > 0 && sells > 0 && Math.Abs(sums.Net - (9 * sells - 12 * cost)) <= 9,
                $"panel crafting adds up: 9 × {sells:N0} − 12 × {cost:N0} = {sums.Net:N0}");
            Check(windowNet.Length > 0 && C("craftProfit").Replace("≤ ", "") == windowNet && C("craftSells") == MainWindow.FkForTest(sells),
                $"panel crafting: profit {C("craftProfit")} as the Crafting part's {windowNet}, sells for {C("craftSells")} each");
            Check(C("craft0Ok") == "1" && C("craft0Worth") == MainWindow.FeedKForTest(sells - cost) && C("craft1Ok") == "" && C("craft1Worth") == MainWindow.FeedKForTest(-cost)
                  && C("craft3").Length > 0 && long.Parse(C("craft0At")) > long.Parse(C("craft1At")),
                $"panel crafting: the last crafts newest first, {C("craft0Worth")} worked, {C("craft1Worth")} failed");
            Check(!cv.Keys.Any(k => k.Contains("ccura", StringComparison.OrdinalIgnoreCase)) && !cv.Values.Any(x => x.Contains('%')), "panel crafting: no accuracy figure");
            // the 10th craft's won combo roll is a line of its own after it (2026-11-04: a Cypri combo made a second dish and
            // the panel only said "worked"), worded by what the book says a win makes; the tiles stay crafts
            var up = Data.RecipeBook.ForOutput("Orichalcum Greaves")?.Upgrade?.Item;
            var comboWord = up != null ? "combo: " + up : Data.RecipeBook.ForOutput("Orichalcum Greaves")?.DoublesOnCombo == true ? "combo ×2" : "combo won";
            Check(C("craft2Combo") == comboWord && C("craft2Ok") == "1" && C("craft2Id").EndsWith(":combo") && C("craft0Combo") == "" && C("craft1Combo") == "" && C("craft3Combo") == "",
                $"panel crafting: a won combo roll is its own line, \"{C("craft2Combo")}\" (expected \"{comboWord}\")");
            var cpicks = CraftKit.Crafts(cv);
            Check(cpicks.Count == 4 && CraftKit.IsCombo(cpicks[2]) && CraftKit.Worked(cpicks[2]) && cpicks[2].Note == comboWord && !CraftKit.IsCombo(cpicks[0]) && cpicks.Count(CraftKit.IsCombo) == 1,
                $"panel crafting: the looks read the roll as worked, noted \"{cpicks.ElementAtOrDefault(2)?.Note}\"");
            // the queue under way (2026-11-04): its line under the recipe on the Card, "10 of 12 · ..." from the sample queue
            var qtext = MainWindow.HudCraftSampleQueue().Text;
            var cardVals = w.HudCraftLookStates().First(s => s.Look == HudLooks.Card).Values;
            var qcard = new HudCard { Still = true };
            qcard.Update(cardVals);
            static IEnumerable<System.Windows.Controls.TextBlock> Texts(DependencyObject o) =>
                (o is System.Windows.Controls.TextBlock tb ? new[] { tb } : Enumerable.Empty<System.Windows.Controls.TextBlock>()).Concat(LogicalTreeHelper.GetChildren(o).OfType<DependencyObject>().SelectMany(Texts));
            var shown = Texts(qcard.Root).FirstOrDefault(t => t.Text == qtext);
            Check(C("craftQueue") == "1" && C("craftQueueText") == qtext && shown is { Visibility: Visibility.Visible },
                $"panel crafting: the Card shows the queue under way, \"{C("craftQueueText")}\"{(shown == null ? " (not drawn)" : shown.Visibility != Visibility.Visible ? " (hidden)" : "")}");
        }

        // the Ticker's top drop (2026-10-26): the most valuable one of an item wins over a stack of cheap ones; blank with nothing priced
        {
            var rows = new List<DropRow>
            {
                new("Spiritstone", "40", "", "", Worth: 300_000),
                new("Shade Gloves", "1", "", "", Grade: "Legend", Worth: 28_000),
                new("Thick hide", "3", "", "", Worth: 0),
            };
            var tv = w.HudTopValues(rows);
            Check(tv["top"] == "Shade Gloves" && tv["topCount"] == "×1" && tv["topWorth"] == "+24,640" && tv["topGrade"] == "Legend",
                $"top drop: {tv["top"]} {tv["topCount"]} {tv["topWorth"]} ({tv["topGrade"]})");
            Check(w.HudTopValues(new List<DropRow> { new("Thick hide", "3", "", "") })["top"] == "", "top drop: none before anything priced");
            // the panel's accuracy line ("23 to look at" read as an error): none with Show accuracy details off, the default
            Check(!p.ShowAccuracy && w.HudAccuracy == null, "the panel shows no accuracy line with Show accuracy details off");
        }

        // That was wrong (2026-10-26): a second shortcut, off by default, recorded like the first, kept only on Save
        {
            const System.Windows.Input.ModifierKeys C = System.Windows.Input.ModifierKeys.Control, S = System.Windows.Input.ModifierKeys.Shift;
            o = Open();
            o.Go(OptionsWindow.Page.InGame);
            Check(w.MarkWrongText == "" && o.MarkKeyBox.Text == "Off" && !o.MarkKeyOffBtn.IsEnabled, "That was wrong: off until a key is picked");
            Check(o.RecordForTest(true, C | S, System.Windows.Input.Key.K) is { } same && same.Contains("start/stop") && !o.SaveBtn.IsEnabled, "That was wrong can't take the start/stop shortcut's keys");
            Check(o.RecordForTest(true, C, System.Windows.Input.Key.J) is { } rule && rule.Contains("Shift") && !o.SaveBtn.IsEnabled, "...and keeps the shortcut rules (Ctrl+J refused)");
            Check(o.RecordForTest(true, C | S, System.Windows.Input.Key.J) == null && o.MarkKeyBox.Text == "Ctrl+Shift+J" && o.SaveBtn.IsEnabled && w.MarkWrongText == "", "a recorded key waits for Save");
            Click(o.CancelBtn);
            DoEvents();
            Check(!o.IsVisible && w.MarkWrongText == "", "Cancel leaves That was wrong off");
            o = Open();
            o.Go(OptionsWindow.Page.InGame);
            o.RecordForTest(true, C | S, System.Windows.Input.Key.J);
            Click(o.SaveBtn);
            DoEvents();
            Check(w.MarkWrongText == "Ctrl+Shift+J", "That was wrong saved: " + w.MarkWrongText);
            if (o.IsVisible) { Console.WriteLine("      (Options stayed open: " + o.FooterNote.Text + ")"); Click(o.CancelBtn); DoEvents(); }
            o = Open();
            o.Go(OptionsWindow.Page.InGame);
            Check(o.MarkKeyBox.Text == "Ctrl+Shift+J" && o.RecordForTest(false, C | S, System.Windows.Input.Key.J) is { } back && back.Contains("That was wrong"), "Options opens with it; start/stop can't take its keys");
            Click(o.MarkKeyOffBtn);
            Click(o.SaveBtn);
            DoEvents();
            Check(w.MarkWrongText == "" && p.Hotkey == Platform.Hotkey.Default, "That was wrong turned off again, start/stop untouched");

            // the key pressed over the sample feed: the newest line marked with its report, the panel's 3 s notice, Undo
            w.ShowDemo(); DoEvents();
            Check(w.PressMarkWrongForTest(false) == "Saved the last minute · Report in tracker" && w.MarkStateForTest is (1, _, "mark"), "the key: the newest line marked, the panel says the minute is saved");
            Check(w.PressMarkWrongForTest(false) == "Already marked · Report in tracker" && w.MarkStateForTest.Marked == 1, "pressed again: the same line isn't filed twice");
            w.PressUndo(); DoEvents();
            Check(w.MarkStateForTest is (0, false, null), "Undo takes the mark back");
            w.ShowMarkedSample(); DoEvents();
            Check(w.MarkStateForTest is (1, true, "mark") && w.UndoShowing == null && w.FeedHint.Visibility != Visibility.Visible, "a Farming line: marked, with Undo under the feed in place of the hint");
            w.PressUndo(); DoEvents();
            Check(w.MarkStateForTest is (0, false, null) && w.FeedHint.Visibility != Visibility.Visible, "...and Undo there takes it back (the hint itself stays in ⋯)");
            Check(w.PressMarkWrongForTest(true) == "Nothing counted in the last 2 minutes" && w.MarkStateForTest is (0, false, null), "nothing counted in the last 2 minutes: the panel says so and nothing is filed");
        }

        // Place the panel… (Ui/PlacementGuide.cs): every snap spot inside the game on a 1080p and a 5120×2160 screen, in each
        // look at its saved size; Enter keeps the spot for its look (the Ticker's apart from the Card's), Esc keeps nothing
        foreach (var game in new[] { new DRect(0, 0, 1920, 1080), new DRect(0, 0, 5120, 2160) })
        {
            var outside = new List<string>();
            foreach (var look in new[] { HudLooks.Strip, HudLooks.Card, HudLooks.Ticker })
            {
                var g = w.MakePlacementGuide(game, game, look);
                var spots = new HashSet<System.Drawing.Point>();
                foreach (var s in HudSpots.All.Select(x => x.Key))
                {
                    g.Snap(s);
                    var r = g.PanelPx;
                    spots.Add(r.Location);
                    if (!game.Contains(r) || g.Chosen != s || r.Width < 60 || r.Height < 20) outside.Add($"{look} {s} {r}");
                }
                if (spots.Count != HudSpots.All.Length) outside.Add($"{look}: {spots.Count} different spots");
                g.Close();
            }
            Check(outside.Count == 0, $"snap spots inside the {game.Width}×{game.Height} game in every look" + (outside.Count > 0 ? ": " + string.Join("; ", outside) : ""));
        }
        {
            var game = new DRect(0, 0, 1920, 1080);
            double k = w.HudSpotScaleForTest;
            bool At(double? v, int px) => v is { } d && Math.Abs(d * k - px) < 1;
            var card = (p.HudX, p.HudY);
            var g = w.MakePlacementGuide(game, game, HudLooks.Ticker);
            g.Snap(HudSpots.Minimap);
            var want = g.PanelPx.Location;
            g.Press(System.Windows.Input.Key.Enter);
            var ticker = w.TickerSpotForTest;
            Check(At(ticker.X, want.X) && At(ticker.Y, want.Y) && p.HudX == card.HudX && p.HudY == card.HudY,
                $"placement guide: Enter keeps the Ticker under the minimap in its own spot ({ticker.X:0}, {ticker.Y:0}); the Card's is left alone");
            g = w.MakePlacementGuide(game, game, HudLooks.Card);
            g.Snap(HudSpots.Target);
            want = g.PanelPx.Location;
            g.Press(System.Windows.Input.Key.Enter);
            Check(At(p.HudX, want.X) && At(p.HudY, want.Y) && w.TickerSpotForTest == ticker,
                $"placement guide: Enter keeps the Card left of the target bar in the Card's spot ({p.HudX:0}, {p.HudY:0}); the Ticker's is left alone");
            var kept = (p.HudX, p.HudY, w.TickerSpotForTest);
            g = w.MakePlacementGuide(game, game, HudLooks.Card);
            g.Snap(HudSpots.Top);
            g.Press(System.Windows.Input.Key.Escape);
            Check((p.HudX, p.HudY, w.TickerSpotForTest) == kept, "placement guide: Esc keeps nothing");
            o = Open();
            o.Go(OptionsWindow.Page.InGame);
            Click(o.HudPlaceBtn);
            Check(!w.PlacementGuideOpen && o.IsVisible, "Options › In game › Place the panel… with no game: a note, no guide");
            o.Close();
        }

        // History on the Events page (0.9): the Today strip's Week side draws seven days of runs with today picked, and a
        // click on yesterday's bar shows its runs by name (the sample's five Urugugu Canyon runs)
        {
            w.ShowDemo("Events"); DoEvents();
            bool stripOpen = w.TodayOpenForTest;
            // the page's runs one at a time (Steven, 2026-11-05: "tell you what you got on every single run ... a menu at the
            // top where you could drop down and choose to sort by it rather than it be so gigantic"): ALL RUNS over one place's
            // card, its band a dropdown of the places (2026-11-06: the others folded under it were "way too long, should be a
            // dropdown menu to choose what you want to view and then change it"); Best first falls by what each made; a click
            // on a run picks its place
            // (2026-11-06, "1 box ... similar to Today's tab"): one box as the page opens, its loot in slots; every run is a second box from ⋯
            var one = w.RunListForTest(); int boxes0 = w.InstHost.Children.Count;
            w.ShowRunListForTest(true); DoEvents();
            var rl = w.RunListForTest();
            w.SetRunsSortForTest(Session.RunsOrder.Best); DoEvents();
            var best = w.RunListForTest();
            w.SetRunsSortForTest(Session.RunsOrder.Newest); DoEvents();
            int cards = w.InstHost.Children.Count;
            Check(boxes0 == 1 && one.Runs == 0 && one.Open.Count == 1 && one.Places.Count >= 2 && one.Places[0] == one.Open[0] && one.Slots >= 3 && rl.Runs >= 5 && rl.Pick == "Newest first" && cards == 2 && best.Pick == "Best first" && best.Values.Zip(best.Values.Skip(1)).All(p => p.First >= p.Second) && !best.Values.SequenceEqual(rl.Values),
                $"Events: {boxes0} box ({string.Join(",", one.Open)} of {one.Places.Count} places, {one.Slots} slots); with every run: {rl.Runs} runs, \"{rl.Pick}\", {cards} boxes; best first {string.Join(" ", best.Values.Take(4).Select(v => v.ToString()))}");
            var otherPlace = one.Places.First(p => p != one.Open[0]);
            w.PickPlaceForTest(otherPlace); DoEvents();
            var rl2 = w.RunListForTest();
            Check(rl2.Open.Count == 1 && rl2.Open[0] == otherPlace && rl2.Places.SequenceEqual(one.Places) && rl2.Slots >= 1,
                $"...a place picked from the band's dropdown takes the box ({string.Join(",", rl2.Open)}, {rl2.Slots} slots), the same {rl2.Places.Count} places offered");
            w.ClickRunForTest(rl.Runs - 1); DoEvents();
            var rl4 = w.RunListForTest();
            Check(w.FixOpenForTest != null && rl4.Open.Count == 1 && w.FixOpenForTest.Contains(rl4.Open[0]), $"...a click on the last run picks its place with the run's sheet: open {string.Join(",", rl4.Open)}, sheet {w.FixOpenForTest?.Split('|').Last()}");
            w.ShowRunListForTest(false); w.PickPlaceForTest(one.Open[0]); DoEvents();
            Check(w.InstHost.Children.Count == 1 && w.InstEarlierBtn.Content as string == "All time", $"...every run hidden again: {w.InstHost.Children.Count} box; the scope reads \"{w.InstEarlierBtn.Content}\"");
            // a click's card opens inside the window and never past its edges, so never over the game (Steven, 2026-11-06); one
            // taller than the window fits by scrolling; a press outside it closes it
            static bool Within((Rect Card, Size Layer)? s) => s is { } v && v.Card.Width > 0 && v.Card.Height > 0 && v.Card.Left >= 0 && v.Card.Top >= 0 && v.Card.Right <= v.Layer.Width + 0.5 && v.Card.Bottom <= v.Layer.Height + 0.5;
            static string Where((Rect Card, Size Layer)? s) => s is { } v ? $"{v.Card.Left:0},{v.Card.Top:0} {v.Card.Width:0}x{v.Card.Height:0} in {v.Layer.Width:0}x{v.Layer.Height:0}" : "none";
            w.OpenMenuForTest(w.EventsMoreBtn, 4); DoEvents();
            var shortMenu = InCard.Shown();
            w.OpenMenuForTest(w.EventsMoreBtn, 40); DoEvents();
            var tallMenu = InCard.Shown();
            InCard.Outside(w.Root); DoEvents();
            // a hover tip too (2026-11-07): drawn on the layer, inside the window, for the button at the window's right edge
            HoverTip.Show(w.EventsMoreBtn); DoEvents();
            var tip = HoverTip.Shown();
            bool tipOwned = ReferenceEquals(HoverTip.Owner, w.EventsMoreBtn);
            HoverTip.Hide(); DoEvents();
            // and an item's card, built the first time it shows
            static IEnumerable<FrameworkElement> Tree(DependencyObject d)
            {
                for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(d); i++)
                {
                    var c = System.Windows.Media.VisualTreeHelper.GetChild(d, i);
                    if (c is FrameworkElement f) yield return f;
                    foreach (var g in Tree(c)) yield return g;
                }
            }
            var cardSlot = Tree(w.Root).FirstOrDefault(f => f.ToolTip is LazyTip && f.IsVisible);
            if (cardSlot != null) HoverTip.Show(cardSlot);
            DoEvents();
            var slotTip = HoverTip.Shown();
            HoverTip.Hide(); DoEvents();
            Check(Within(tip) && tipOwned && HoverTip.Shown() == null && cardSlot != null && Within(slotTip) && slotTip!.Value.Card.Height > 60,
                $"hover tips show inside the window: ⋯'s tip {Where(tip)}, an item's card {Where(slotTip)}; gone when hidden");
            Check(Within(shortMenu) && Within(tallMenu) && InCard.Current == null,
                $"cards open inside the window: a menu {Where(shortMenu)}, one of 40 rows {Where(tallMenu)}; a press outside closes it ({(InCard.Current == null ? "closed" : "still open")})");
            w.SetRunsSortForTest(Session.RunsOrder.Newest); DoEvents();
            w.PickRunsWeekForTest(true); DoEvents();
            var rw = w.WeekForTest(runs: true);
            Check(w.TodayStrip.Visibility == Visibility.Visible && rw.Bars == 7 && rw.Picked == "Today" && rw.Line.StartsWith("TODAY") && w.TodayTitle.Text == "WEEK" && MainWindow.InlineText(w.TodayNote).Contains(" to "),
                $"Events, Week: {rw.Bars} bars, {rw.Picked} picked, \"{rw.Line}\", head \"{w.TodayTitle.Text} {MainWindow.InlineText(w.TodayNote)} {w.TodaySum.Text}\"");
            w.PickWeekDayForTest(5, runs: true); DoEvents();
            var rw2 = w.WeekForTest(runs: true);
            Check(rw2.Picked != "Today" && rw2.Line.StartsWith(rw2.Picked.ToUpperInvariant()) && rw2.Chips.Any(c => c.StartsWith("Urugugu Canyon ×")),
                $"...yesterday picked: {rw2.Picked}, \"{rw2.Line}\", chips {string.Join(" / ", rw2.Chips)}");
            w.PickRunsWeekForTest(false); DoEvents();
            Check(w.TodayTitle.Text == "TODAY" && !MainWindow.InlineText(w.TodayNote).Contains(" to "), $"...and back to Today: \"{MainWindow.InlineText(w.TodayNote)}\"");
            w.TodayOpenForTest = stripOpen; DoEvents();
        }

        // the page follows what the player is doing (0.8.55, Session/PageFollow.cs), through the window's own code: a change
        // that holds 5 s switches the page and lights its tab, a tab clicked by hand stays until the activity moves on, and
        // switched off nothing moves. parse-test has the rules in full; this is the window's side of them.
        {
            w.ShowDemo(); DoEvents();
            p.FollowActivity = true;
            var ft = DateTime.UtcNow;
            string Follow(Session.ActivityKind kind, double secs)
            {
                for (double x = 0; x < secs; x += 0.5) { w.FollowForTest(kind, ft); ft = ft.AddSeconds(0.5); }
                DoEvents();
                return w.PartForTest;
            }
            bool Lit(System.Windows.Controls.Button tab) => ReferenceEquals(tab.Style, w.FindResource("SegOn"));
            Check(Follow(Session.ActivityKind.Farming, 10) == "Farming", "the page: farming leaves it on Farming");
            Check(Follow(Session.ActivityKind.Crafting, 3) == "Farming" && Follow(Session.ActivityKind.Farming, 5) == "Farming", "...3 s of crafting doesn't move it");
            Check(Follow(Session.ActivityKind.Crafting, 6) == "Crafting" && Lit(w.CraftTab) && !Lit(w.FarmTab), "...crafting that holds shows Crafting, its tab lit");
            Check(Follow(Session.ActivityKind.Expedition, 6) == "Events" && Lit(w.EventsTab), "...an expedition shows Events");
            Click(w.FarmTab);
            Check(w.PartForTest == "Farming" && Follow(Session.ActivityKind.Expedition, 30) == "Farming", "Farming clicked in the expedition stays through it");
            Check(Follow(Session.ActivityKind.Crafting, 6) == "Crafting", "...until crafting starts");
            p.FollowActivity = false;
            Check(Follow(Session.ActivityKind.Farming, 10) == "Crafting" && Follow(Session.ActivityKind.Expedition, 10) == "Crafting", "switched off: farming and an expedition leave Crafting showing");
            p.FollowActivity = true;
            w.ShowDemo(); DoEvents();
        }

        // Options › Sharing › Share my week opens the profile with the share card (site PR 28), no browser started here
        {
            var opened = new List<string>();
            MainWindow.OpenForTest = opened.Add;
            try
            {
                o = Open();
                o.Go(OptionsWindow.Page.Sharing);
                Check((string)o.ShareWeekBtn.Content == "Share my week" && o.ShareWeekBtn.IsEnabled, "Options › Sharing has a Share my week button");
                Click(o.ShareWeekBtn);
                Check(opened.Count == 1 && opened[0] == "https://kinahledger.com/#profile?share=1", "...it opens " + string.Join(", ", opened));
                Check(o.IsVisible && !o.SaveBtn.IsEnabled, "...and leaves Options open with nothing to save");
                o.Close();
            }
            finally { MainWindow.OpenForTest = null; }
        }

        w.ForceClose();
        Console.WriteLine(fails == 0 ? "options self-test passed" : $"options self-test: {fails} failed");
        return fails == 0 ? 0 : 1;
    }

    /// <summary>Renders each page of the Options window over a sample session, for layout review.</summary>
    /// <summary>The setup check, drawn off-screen against the live game: one image per step, a few seconds apart.</summary>
    public static void ShotCalibration(Settings settings, string dir)
    {
        Ensure();
        Directory.CreateDirectory(dir);
        var w = new CalibrationWindow(settings, Data.ItemDictionary.LoadOrEmpty(settings.ItemsFile).Knows, () => null, () => { }) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -12000, Top = -12000, ShowActivated = false };
        w.Show();
        for (int step = 0; step < 4; step++)
        {
            w.GoTo(step);
            var until = DateTime.UtcNow.AddSeconds(step < 3 ? 4 : 1);
            while (DateTime.UtcNow < until) { DoEvents(); Thread.Sleep(50); }
            Save((FrameworkElement)w.Content, Path.Combine(dir, $"ui-calibration-{step + 1}.png"), 1300);
        }
        w.Close();
    }

    public static void ShotOptions(Settings settings, string dir, string? skin = null)
    {
        Ensure();
        Directory.CreateDirectory(dir);
        if (skin != null) Skins.Apply(skin);
        var w = new MainWindow(settings, watch: false) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -12000, Top = -12000, ShowActivated = false };
        w.Show();
        w.ShowDemo("Market");
        if (skin != null) w.Prefs.Skin = skin;   // what Options opens with (a render; nothing is saved)
        foreach (var page in Enum.GetValues<OptionsWindow.Page>())
        {
            var o = new OptionsWindow(w, page) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -12000, Top = -12000, ShowActivated = false };
            // KL_OPTIONS_H=1400: render the page that tall, to see rows below the fold
            if (double.TryParse(Environment.GetEnvironmentVariable("KL_OPTIONS_H"), out var oh) && oh > 300) { o.MaxHeight = oh; o.Height = oh; }
            o.Show();
            DoEvents();
            Save((FrameworkElement)o.Content, Path.Combine(dir, $"options-{page.ToString().ToLowerInvariant()}{(skin != null ? "-" + skin : "")}.png"), 0);
            o.Close();
        }
        // In game at the panel's look: the three looks drawn at their real size, the chosen one ringed
        {
            var o = new OptionsWindow(w, OptionsWindow.Page.InGame) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -12000, Top = -12000, ShowActivated = false };
            o.Show();
            IconCache.Drain(TimeSpan.FromSeconds(8));
            DoEvents();
            var at = o.HudLookHost.TransformToAncestor(o.Scroller).Transform(new Point(0, 0));
            o.Scroller.ScrollToVerticalOffset(Math.Max(0, o.Scroller.VerticalOffset + at.Y - 70));
            DoEvents();
            Save((FrameworkElement)o.Content, Path.Combine(dir, $"options-ingame-look{(skin != null ? "-" + skin : "")}.png"), 0);
            o.Close();
        }
        // In game once more, scrolled to the end: the panel's rows below the fold (opacity, size and position)
        {
            var o = new OptionsWindow(w, OptionsWindow.Page.InGame) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -12000, Top = -12000, ShowActivated = false };
            o.Show();
            DoEvents();
            o.Scroller.ScrollToEnd();
            DoEvents();
            Save((FrameworkElement)o.Content, Path.Combine(dir, $"options-ingame-lower{(skin != null ? "-" + skin : "")}.png"), 0);
            o.Close();
        }
        // General scrolled to the end: the Window group and the theme below the fold (0.8.55 put Switch pages above them)
        {
            var o = new OptionsWindow(w, OptionsWindow.Page.General) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -12000, Top = -12000, ShowActivated = false };
            o.Show();
            DoEvents();
            o.Scroller.ScrollToEnd();
            DoEvents();
            Save((FrameworkElement)o.Content, Path.Combine(dir, $"options-general-lower{(skin != null ? "-" + skin : "")}.png"), 0);
            o.Close();
        }
        // In game at Rare drops (2026-10-26): a sample watch list, the chime and its volume
        OptionsWindow.ShotRareDrops(w, Path.Combine(dir, $"options-ingame-rare{(skin != null ? "-" + skin : "")}.png"), (e, file) => Save(e, file, 0));
        // Place the panel… (In game): the guide over a dark stand-in for the game, as the approved mock draws it
        if (skin == null) ShotPlacementGuide(w, dir);
        w.Close();
    }

    /// <summary>
    /// The placement guide as the approved mock draws it (f4-placement.png): a 1600×900 part of a 5120×2160 game, so the
    /// minimap, loot list and chat box are markers on its edges, with the Card at the top centre and the Ticker left of the
    /// target bar; and the Card's guide over a whole 1920×1080 game, every part outlined in place. No game runs in CI, so a
    /// dark stand-in sits under the veil.
    /// </summary>
    static void ShotPlacementGuide(MainWindow w, string dir)
    {
        var wide = new DRect(0, 0, 5120, 2160);
        var crop = new DRect(1760, 0, 1600, 900);
        var hd = new DRect(0, 0, 1920, 1080);
        foreach (var url in w.HudSample().Where(kv => kv.Key.EndsWith("Icon")).Select(kv => kv.Value).Distinct()) IconCache.Get(url);
        IconCache.Drain(TimeSpan.FromSeconds(10));
        DoEvents();
        foreach (var (file, look, game, view) in new[]
        {
            ("ui-placement-guide-card.png", HudLooks.Card, wide, crop),
            ("ui-placement-guide-ticker.png", HudLooks.Ticker, wide, crop),
            ("ui-placement-guide-whole.png", HudLooks.Card, hd, hd),
        })
        {
            var g = w.MakePlacementGuide(game, view, look);
            var stand = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0.35, 1) };
            stand.GradientStops.Add(new GradientStop(Color.FromRgb(0x3A, 0x26, 0x28), 0));
            stand.GradientStops.Add(new GradientStop(Color.FromRgb(0x22, 0x1C, 0x24), 0.45));
            stand.GradientStops.Add(new GradientStop(Color.FromRgb(0x12, 0x14, 0x1C), 1));
            stand.Freeze();
            var scene = new System.Windows.Controls.Grid { Width = view.Width, Height = view.Height, Background = stand };
            scene.Children.Add(g.ShotFace());
            scene.Measure(new Size(view.Width, view.Height));
            scene.Arrange(new Rect(0, 0, view.Width, view.Height));
            scene.UpdateLayout();
            DoEvents();
            Save(scene, Path.Combine(dir, file), view.Width);
            scene.Children.Clear();
            g.Close();
        }
    }

    /// <param name="state">A review sweep's state (tools/ui-review.md): the render is named "<page>-<state>.png", and on the Events page its figures check goes beside it.</param>
    /// <returns>What the figures check found wrong (a state render of the Events page); empty otherwise.</returns>
    public static List<string> Shot(Settings settings, string dir, bool demo, string kind = "Open world", bool compact = false, bool welcome = false, string? skin = null, bool full = false, bool tall = false, bool mine = false, UiState? state = null)
    {
        var problems = new List<string>();
        Ensure();
        Directory.CreateDirectory(dir);
        var w = new MainWindow(settings, watch: false) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -12000, Top = -12000, ShowActivated = false };
        w.Show();
        if (skin != null) Skins.Apply(skin);
        // a state's own look (the daylight and abyss states, 0.10.22); a state without one renders in Night, so a sweep reads the same on every PC
        else if (state != null) Skins.Apply(state.Skin ?? Skins.Default);
        // KL_SCREEN_H=1040: render as a screen of that height would show it (the window never grows past the work area)
        if (double.TryParse(Environment.GetEnvironmentVariable("KL_SCREEN_H"), out var screenH) && screenH > 300) w.MaxHeight = screenH;
        // --tall: the whole window, however tall, so a render shows every block of a view (named "-tall")
        if (tall) w.MaxHeight = 4000;
        // KL_WINDOW_SCALE=1.25 (0.10.11): the window at that size, as Options › General › Window size would draw it
        if (double.TryParse(Environment.GetEnvironmentVariable("KL_WINDOW_SCALE"), out var winScale) && winScale > 0) w.PreviewWindowScale(winScale);
        // a state's own window size (scale-80, scale-150) and view (compact, full)
        if (state?.Scale is { } stScale) w.PreviewWindowScale(stScale);
        if (demo) w.ShowDemo(kind, state);
        // --events --mine (0.10.10): the page as this PC's own runs fill it, from a copy of the run log; nothing is tracked, saved or sent
        else if (mine && kind == "Events") w.ShowMineRuns();
        if (compact || state?.View == "compact") w.ShowCompact();
        if (full || state?.View == "full") w.ShowFull();
        if (welcome) w.ShowWelcomeDemo(skin ?? "night");
        // KL_SHOT_STATE=run|paused|stopped|empty|onboard (2026-10-28; onboard 2026-11-04): the Farming page's other states, each its own "-<state>" render
        // and KL_SHOT_STATE=week|week-new (0.9): LOOT's Week side; with --events, week is the Events strip's Week side
        var shotState = demo && kind is "Open world" or "Events" ? Environment.GetEnvironmentVariable("KL_SHOT_STATE") ?? "" : "";
        // and KL_SHOT_STATE=share-session|share-run|share-week|share-picture (0.9.3): the Share sheet open; with --events, share-run
        // and Compare (0.9.3, Ui/MainWindow.Compare.cs): week-zones and compare-on on Farming, week-places and run-vs-average on Events
        if (kind == "Events" && shotState is not ("week" or "share-run" or "week-places" or "run-vs-average")) shotState = "";
        if (shotState == "share-run" && kind == "Events") w.OpenShareSample(shotState, events: true);
        else if (shotState.Length > 0 && kind == "Events") w.ShowEventsStateSample(shotState);
        else if (shotState.Length > 0) w.ShowStateSample(shotState);
        IconCache.Drain(TimeSpan.FromSeconds(8));   // the demo rows' icons, when this PC is online
        DoEvents();
        if (Environment.GetEnvironmentVariable("KL_DEBUG_LAYOUT") == "1")
        {
            void Dump(DependencyObject d, int depth)
            {
                if (depth > 7) return;
                for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++)
                {
                    var c = VisualTreeHelper.GetChild(d, i);
                    if (c is FrameworkElement fe && fe.ActualWidth > 600) Console.WriteLine($"{new string(' ', depth * 2)}{fe.GetType().Name} {fe.Name} {fe.ActualWidth:0} desired {fe.DesiredSize.Width:0} vis {fe.Visibility}");
                    Dump(c, depth + 1);
                }
            }
            Console.WriteLine($"window {w.ActualWidth:0}"); Dump(w, 0);
        }
        // a state render (tools/ui-review.md): the page and the state in the file name, the figures check beside an Events render, nothing else
        if (state != null)
        {
            var file = Path.Combine(dir, UiState.PageName(kind) + "-" + state.Name + (skin != null ? "-" + skin : "") + ".png");
            Save((FrameworkElement)w.Content, file, (compact ? 640 : 800) - 16);
            if (kind == "Events")
            {
                var fig = w.EventsFigures(state);
                fig = new Dictionary<string, object?> { ["page"] = "events", ["state"] = state.Name, ["note"] = state.Note, ["file"] = Path.GetFileName(file) }.Concat(fig).ToDictionary(kv => kv.Key, kv => kv.Value);
                File.WriteAllText(Path.ChangeExtension(file, ".figures.json"), System.Text.Json.JsonSerializer.Serialize(fig, new System.Text.Json.JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
                var check = (Dictionary<string, object?>)fig["check"]!;
                foreach (var key in new[] { "missing", "trimmed", "pastWindowEdge" })
                    foreach (var p in (List<string>)check[key]!) problems.Add($"{key}: {p}");
            }
            w.Close();
            return problems;
        }
        var name = welcome ? "ui-welcome" : !demo ? (mine && kind == "Events" ? "ui-main-events-mine" : "ui-main") : kind switch { "Gathering" => "ui-main-gathering", "Pets" => "ui-main-pets", "Market" => "ui-main-market", "Crafting" => "ui-main-crafting", "CraftingEmpty" => "ui-main-crafting-empty", "CraftingUpgrades" => "ui-main-crafting-upgrades", "Events" => "ui-main-events", "Quests" => "ui-main-quests", _ => "ui-main-tracking" };
        Save((FrameworkElement)w.Content, Path.Combine(dir, name + (shotState.Length > 0 ? "-" + shotState : "") + (compact ? "-compact" : full ? "-full" : "") + (tall ? "-tall" : "") + (skin != null ? "-" + skin : "") + ".png"), (compact ? 640 : 800) - 16);
        // GOALS (0.9): a goal's hover card and the Set a goal sheet beside the goals render (ui-goal-card.png, ui-goal-card-kinah.png,
        // ui-goal-sheet.png, ui-goal-sheet-item.png)
        if (shotState == "goals" && skin == null && !full) w.ShotGoalCards(dir);
        // Share (0.9.3): the 1200×630 pictures themselves (ui-share-picture-session.png, -run, -week, and -named with a sample name on)
        if (shotState == "share-picture" && skin == null && !full) w.ShotSharePictures(dir);
        // the whole sheet on its own too (ui-share-sheet-*.png): the window stops at the runner's screen height, under the preview
        if (shotState.StartsWith("share-") && shotState != "share-picture")
            w.ShotShareSheet(Path.Combine(dir, "ui-share-sheet-" + shotState[6..] + (kind == "Events" ? "-events" : "") + (compact ? "-compact" : full ? "-full" : "") + (skin != null ? "-" + skin : "") + ".png"), w.TryFindResource(kind == "Events" ? "Bg" : "Card") as Brush);
        // the Week side whole (0.9): the window stops at the runner's screen height, so the LOOT box (ui-loot-week*.png) or the
        // Events strip (ui-runs-week*.png) is drawn on its own too, with the picked day and its chips under the bars
        if (shotState.StartsWith("week"))
            SavePart(kind == "Events" ? w.TodayStrip : w.SummaryCard, Path.Combine(dir, (kind == "Events" ? "ui-runs-" : "ui-loot-") + shotState + (compact ? "-compact" : full ? "-full" : "") + (skin != null ? "-" + skin : "") + ".png"), w.TryFindResource("Bg") as Brush);
        // Compare (0.9.3): the state's list and card whole, off the window (ui-cmp-zones*.png, ui-cmp-places*.png, ui-cmp-run*.png)
        if (shotState is "week-zones" or "week-places" or "run-vs-average")
            w.ShotCompareParts(dir, shotState, (compact ? "-compact" : full ? "-full" : "") + (skin != null ? "-" + skin : ""));
        // a state render is the window only: the cards and the panel come from the plain farming renders
        if (shotState.Length > 0) { w.Close(); return problems; }
        // the Events part's card on its own: folded, and with Materials open
        if (demo && kind == "Events") w.ShotRunsCards(dir);
        // KL_CARD_ZONE=<zone>: also draw the card a click on the first drop opens, with the live site's data for that zone
        if (demo && Environment.GetEnvironmentVariable("KL_CARD_ZONE") is { Length: > 0 } cardZone)
        {
            using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            Task.Run(() => KinahLedger.Data.SiteData.RefreshAsync(http, "https://kinahledger.com", "na_e-siel-elyos")).Wait();
            w.ShotItemCard(Path.Combine(dir, "ui-card.png"), cardZone);
        }
        // the Discord card for the same count, and the panel over the game with the window's own figures (every field, then the default set)
        if (demo && kind is not ("Crafting" or "CraftingEmpty" or "CraftingUpgrades" or "Events" or "Quests")) w.SaveShareCard(Path.Combine(dir, "ui-card-discord.png"));
        if (demo && kind == "Crafting") w.ShotCraftCard(Path.Combine(dir, "ui-craft-card.png"));
        if (demo) w.ShotPlayerCards(dir, kind);
        // a tapped feed line's report sheet (direction C), for a farming render
        if (demo && kind == "Open world") w.ShotLineReport(Path.Combine(dir, "ui-line-report.png"));
        // the run-end recap (2026-11-04, Ui/MainWindow.Recap.cs), for a sample run
        if (demo && kind == "Open world") w.ShotRecapCard(Path.Combine(dir, "ui-recap.png"));
        // the Today strip's hover cards (0.8.50): a farming drop's and the Kinah's
        if (demo && kind == "Open world") w.ShotTodayCards(dir);
        // and on the Events page, a run reward's (0.8.51: each page's strip shows its own day)
        if (demo && kind == "Events") w.ShotTodayRunCard(dir);
        // the Accuracy card on its own (the card the Farming page's chip opens), with its things to look at
        if (demo && kind is not ("Crafting" or "CraftingEmpty" or "CraftingUpgrades" or "Events" or "Market" or "Quests")) w.ShotAccuracyCard(Path.Combine(dir, "ui-accuracy-card.png"));
        var values = w.HudValues.ToDictionary(kv => kv.Key, kv => kv.Value);
        var hudAcc = w.HudAccuracy; bool hudToLook = w.HudAccuracyToLook;
        // the panel's three states (direction C): the newest feed line and the rate, farming and crafting, and a flagged run
        // (the farming and flagged states from a farming render, the crafting one from the Crafting render: each with its own sample prices)
        var states = !demo ? new() : w.HudStates().Where(x => kind == "Crafting" ? x.File == "ui-hud-crafting.png" : kind == "Open world" && x.File != "ui-hud-crafting.png").ToList();
        // the Card and the Ticker in the mock's three states, from the window's sample figures
        var lookStates = demo && kind == "Open world" ? w.HudLookStates() : demo && kind == "Crafting" ? w.HudCraftLookStates() : new();
        if (demo && kind == "Open world") lookStates.AddRange(w.MarkedLookStates());   // That was wrong's notice: ui-hud-card-marked.png, ui-hud-ticker-marked.png
        if (states.Count > 0) foreach (var (k, v) in states[0].Values) values[k] = v;
        // That was wrong (2026-10-26): the window just after the key, the newest Farming line marked and Undo under the feed
        // (last, once the panel's figures are taken; not over the Undo bar's render, which the mark would make final)
        if (demo && kind == "Open world" && Environment.GetEnvironmentVariable("KL_SHOT_UNDO") != "1")
        {
            w.ShowMarkedSample();
            DoEvents();
            Save((FrameworkElement)w.Content, Path.Combine(dir, "ui-main-marked" + (compact ? "-compact" : full ? "-full" : "") + (tall ? "-tall" : "") + (skin != null ? "-" + skin : "") + ".png"), (compact ? 640 : 800) - 16);
        }
        w.Close();

        foreach (var (file, hudState) in states)
        {
            var h = new HudWindow(new[] { "feed" }) { Left = -12000, Top = -12000, ShowActivated = false };
            h.Show();
            h.Update(hudState);
            DoEvents();
            Save((FrameworkElement)h.Content, Path.Combine(dir, file), 0);
            h.Close();
        }

        foreach (var (file, fields) in new[] { ("ui-hud.png", HudFields.Parse(null)), ("ui-hud-all.png", HudFields.All.Select(f => f.Key).ToList()), ("ui-hud-alert.png", HudFields.Parse(null)), ("ui-hud-accuracy.png", HudFields.Parse(null)) })
        {
            var h = new HudWindow(fields) { Left = -12000, Top = -12000, ShowActivated = false };
            h.Show();
            h.Update(values);
            if (file == "ui-hud-accuracy.png") h.SetAccuracy(hudAcc ?? "✓ Nothing looked wrong", hudAcc != null && hudToLook);
            if (file == "ui-hud-alert.png") h.Alert("Rare drop: Shade Gloves ×1 · sells for 28K on Siel", System.Windows.Media.Color.FromRgb(0x5A, 0xA6, 0xFF));
            DoEvents();
            Save((FrameworkElement)h.Content, Path.Combine(dir, file), 0);
            h.Close();
        }

        // the panel's looks (2026-10-25): ui-hud-card-normal/notice/rare.png and ui-hud-ticker-*.png, with their icons
        foreach (var url in lookStates.SelectMany(st => st.Values.Where(kv => kv.Key.EndsWith("Icon")).Select(kv => kv.Value)).Distinct()) IconCache.Get(url);
        if (lookStates.Count > 0) { IconCache.Drain(TimeSpan.FromSeconds(10)); DoEvents(); }
        foreach (var (file, look, v) in lookStates)
        {
            var h = new HudWindow(null, look, still: true) { Left = -12000, Top = -12000, ShowActivated = false };
            h.Show();
            h.Update(v);
            DoEvents();
            // over the mock's dark page (#090B15), so the toasts' see-through washes read as they do over the game
            Save((FrameworkElement)h.Content, Path.Combine(dir, file), 0, Color.FromRgb(0x09, 0x0B, 0x15));
            h.Close();
        }

        // the overlay's own states (tracker/overlay-behaviour), each from the same figures with the keys MainWindow sets
        if (!demo || kind != "Open world") return problems;
        foreach (var (file, fields, extra) in new (string, List<string>, Dictionary<string, string>)[]
        {
            ("ui-hud-notice.png", HudFields.Parse(null), new() { ["notice"] = "Update ready · installs when you leave the game" }),
            // being moved from the tray: the dashed outline and the "drag me" hint
            ("ui-hud-unlocked.png", HudFields.Parse(null), new() { ["unlocked"] = "move" }),
            // a run on: the red dot, the run's clock, and the shortcut's confirmation; then the stop's line on the default panel
            ("ui-hud-run.png", HudFields.Parse("time,rate,total"), new() { ["run"] = "1", ["time"] = "RUN 42:17", ["flash"] = "Run started" }),
            ("ui-hud-stopped.png", HudFields.Parse(null), new() { ["flash"] = "Run stopped · 1.27M this run" }),
            // Options at its far ends: 30% background (the figures stay solid) at 150%
            ("ui-hud-scaled.png", HudFields.Parse("time,rate,total"), new()),
            // the first two minutes: the hour says it is warming up instead of a wild figure, on the feed and on the classic set
            ("ui-hud-warming.png", HudFields.Parse(null), new() { ["warm"] = "1", ["rate"] = "warming up", ["feedRate"] = "warming up", ["feedUnit"] = "" }),
            ("ui-hud-warming-classic.png", HudFields.Parse("time,rate,total"), new() { ["warm"] = "1", ["rate"] = "warming up", ["time"] = "0:48", ["total"] = "21.4K" }),
        })
        {
            var h = new HudWindow(fields) { Left = -12000, Top = -12000, ShowActivated = false };
            h.Show();
            var v = new Dictionary<string, string>(values);
            foreach (var (k, x) in extra) v[k] = x;
            h.Update(v);
            if (file == "ui-hud-scaled.png") { h.SetOpacity(0.3); h.SetScale(1.5); }
            DoEvents();
            Save((FrameworkElement)h.Content, Path.Combine(dir, file), 0);
            h.Close();
        }
        return problems;
    }

    /// <summary>
    /// The review sweep (tools/ui-review.md): every state under tracker/tests/ui/states rendered on the Events page
    /// ("events"), or on every page ("all"; a state with runs of its own is an Events state). Each render is whole (tall),
    /// named "<page>-<state>.png", with the figures check beside an Events render. A state that fails to render is reported
    /// and the sweep goes on; the exit code says whether every render came out and every figures check passed. The Market
    /// prices come from each state, never from this PC's own price files.
    /// </summary>
    public static int Sweep(Settings settings, string dir, string which, string statesDir, string? skin)
    {
        var files = Directory.GetFiles(statesDir, "*.json").OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
        if (files.Count == 0) { Console.WriteLine($"no states in {statesDir}"); return 2; }
        var pages = which == "all" ? new[] { "Open world", "Gathering", "Market", "Crafting", "CraftingEmpty", "CraftingUpgrades", "Events", "Quests" } : new[] { "Events" };
        Directory.CreateDirectory(dir);
        Ensure();
        Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;   // one window per state: the first one's close must not end the application
        KinahLedger.Data.PriceBook.PathOverride = Path.Combine(Path.GetTempPath(), "KinahLedger-state-runs", "prices.json");
        KinahLedger.Data.PriceBook.Reset();
        var report = new List<string>();
        int renders = 0, failed = 0, wrong = 0;
        foreach (var f in files)
        {
            UiState st;
            try { st = UiState.Load(f); }
            catch (Exception ex) { failed++; report.Add($"{Path.GetFileName(f)}: FAILED to read: {ex.Message}"); continue; }
            foreach (var kind in pages)
            {
                if (st.EventsOnly && kind != "Events") continue;
                var name = UiState.PageName(kind) + "-" + st.Name + (skin != null ? "-" + skin : "");
                try
                {
                    var problems = Shot(settings, dir, demo: true, kind, skin: skin, tall: true, state: st);
                    renders++;
                    if (problems.Count == 0) report.Add($"{name}.png  ok");
                    else { wrong++; report.Add($"{name}.png  FIGURES: " + string.Join(" | ", problems)); }
                }
                catch (Exception ex) { failed++; report.Add($"{name}.png  FAILED: {ex.GetType().Name}: {ex.Message}"); }
            }
        }
        report.Add($"{renders} renders written to {Path.GetFullPath(dir)}, {failed} failed, {wrong} with a figure wrong or missing");
        File.WriteAllLines(Path.Combine(dir, "sweep.txt"), report);
        foreach (var l in report) Console.WriteLine(l);
        return failed == 0 && wrong == 0 ? 0 : 1;
    }

    /// <summary>Lets pending layout and render work run, since no message loop is running in this mode.</summary>
    static void DoEvents()
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ContextIdle, new Action(() => frame.Continue = false));
        System.Windows.Threading.Dispatcher.PushFrame(frame);
    }

    /// <summary>
    /// One part of the window drawn whole on its own, over the page's colour with a margin, through a brush of its own visual
    /// (so its place in the window doesn't shift it): a part the window cuts off at the screen's height still shows in full.
    /// </summary>
    static void SavePart(FrameworkElement el, string path, Brush? page)
    {
        double w = el.ActualWidth, h = el.ActualHeight;
        if (w < 1 || h < 1) return;
        const int pad = 12;
        int wpx = (int)Math.Ceiling(w) + 2 * pad, hpx = (int)Math.Ceiling(h) + 2 * pad;
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            if (page != null) dc.DrawRectangle(page, null, new Rect(0, 0, wpx, hpx));
            dc.DrawRectangle(new VisualBrush(el) { ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect(0, 0, w, h), Stretch = Stretch.Fill }, null, new Rect(pad, pad, w, h));
        }
        var rtb = new RenderTargetBitmap(wpx, hpx, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = File.Create(path);
        enc.Save(fs);
    }

    /// <param name="backdrop">Painted under the drawing (the panel looks' renders); transparent when null.</param>
    static void Save(FrameworkElement root, string path, double fallbackWidth, Color? backdrop = null)
    {
        if (root.ActualWidth < 1 || root.ActualHeight < 1)
        {
            root.Measure(new Size(fallbackWidth > 0 ? fallbackWidth : double.PositiveInfinity, double.PositiveInfinity));
            root.Arrange(new Rect(root.DesiredSize));
            root.UpdateLayout();
        }
        int wpx = (int)Math.Ceiling(root.ActualWidth), hpx = (int)Math.Ceiling(root.ActualHeight);
        if (wpx < 1 || hpx < 1) throw new InvalidOperationException("the window did not lay out");
        var rtb = new RenderTargetBitmap(wpx, hpx, 96, 96, PixelFormats.Pbgra32);
        if (backdrop is { } bg)
        {
            var under = new DrawingVisual();
            using (var dc = under.RenderOpen()) dc.DrawRectangle(new SolidColorBrush(bg), null, new Rect(0, 0, wpx, hpx));
            rtb.Render(under);
        }
        rtb.Render(root);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = File.Create(path);
        enc.Save(fs);
    }
}
