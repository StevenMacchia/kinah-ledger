using System.IO;
using System.Windows;
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
        app.DispatcherUnhandledException += (_, e) =>
        {
            CrashLog.Write("ui", e.Exception);
            e.Handled = true;
            MessageBox.Show($"Something went wrong, but the tracker is still running.\n\n{e.Exception.Message}\n\nDetails were saved to:\n{CrashLog.CrashPath}", "Kinah Ledger", MessageBoxButton.OK, MessageBoxImage.Warning);
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
        Check(w.TablesCard.Visibility == Visibility.Visible && w.SummaryCard.Visibility == Visibility.Visible && w.PickupStrip.Visibility != Visibility.Visible && w.TopDropsBox.Visibility != Visibility.Visible && w.Rows.Items.Count <= 3,
            "Compact: run card, slim total, tables at three rows, no pickups");
        w.ShowFull(); DoEvents();
        Check(w.TablesCard.Visibility == Visibility.Visible && w.PickupStrip.Visibility == Visibility.Visible && w.TopDropsBox.Visibility == Visibility.Visible && w.Rows.Items.Count > 3 && w.HeadStack.Width > compactLayout * 1.8,
            $"Full view: pickups, top drops, more rows, {compactW:0} -> {w.ActualWidth:0} px wide");
        w.ShowCompact(); DoEvents();
        Check(Math.Abs(w.HeadStack.Width - compactLayout) < 1, "back to Compact: the width it had");

        // the Farming page, calmer (2026-10-25): the session total labelled and in K/M, best here said once (under the
        // hour), accuracy as one chip, and the panel's loot an hour only beside a rate with Kinah in it
        Check(w.FootTotalNote.Text == "so far, after the Market's cut" && !w.FootTotal.Text.Contains(',') && w.FootTotal.Text.Length > 0 && "KMB".Contains(w.FootTotal.Text[^1]),
            $"Session total labelled, in K/M: {w.FootTotalNote.Text} {w.FootTotal.Text}");
        Check(!w.SessionRows.Children.OfType<System.Windows.Controls.Border>().Any(b => b.Child is System.Windows.Controls.Grid g && g.Children.OfType<System.Windows.Controls.TextBlock>().Any(t => t.Text.Contains("best here"))),
            "Session rows: your best here only under the hour");
        Check(w.AccuracyChip.Visibility == Visibility.Visible && w.AccuracyChip.Child is System.Windows.Controls.TextBlock { Text: "1 to look at" }, "Accuracy: one chip, the sample's refused line to look at");
        Check(w.HudValues.GetValueOrDefault("loot", "").Length > 0, "panel: loot an hour beside a rate with Kinah in it");
        // rows update in place (2026-10-25): a redraw keeps the row controls and only hands them new words, so a tick with a
        // farming session on screen no longer rebuilds the tables (which is what made clicks and drags wait)
        var rowItems = w.Rows.ItemsSource as System.Collections.ObjectModel.ObservableCollection<RowItem>;
        var box0 = w.Rows.ItemContainerGenerator.ContainerFromIndex(0);
        var item0 = rowItems is { Count: > 1 } ? rowItems[0] : null;
        Check(rowItems != null && box0 != null && item0 != null, $"the drops table holds row items with controls ({rowItems?.Count} rows)");
        w.ShowFull(); DoEvents();
        Check(ReferenceEquals(w.Rows.ItemContainerGenerator.ContainerFromIndex(0), box0) && rowItems != null && ReferenceEquals(rowItems[0], item0) && w.Rows.Items.Count > 3,
            "Full view adds rows under the ones already there, keeping their controls");
        var was = item0!.Row;
        MainWindow.SyncRows(w.Rows, new[] { was with { Drops = "999" }, rowItems![1].Row });
        DoEvents();
        Check(ReferenceEquals(w.Rows.ItemContainerGenerator.ContainerFromIndex(0), box0) && item0.Drops == "999" && w.Rows.Items.Count == 2,
            "new words land on the same row control; extra rows come off the end");
        Check(!item0.Set(was with { Drops = "999" }), "the same words again change nothing");
        w.ShowCompact(); DoEvents();
        Check(ReferenceEquals(w.Rows.ItemContainerGenerator.ContainerFromIndex(0), box0) && item0.Drops == was.Drops && w.Rows.Items.Count <= 3,
            "a redraw puts the count's words back on the same control");
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
        Check(w.UndoShowing == "Farming tables cleared" && w.TablesClearedForUndo, "Clear the tables: cleared at once, with an Undo bar");
        w.PressUndo(); DoEvents();
        Check(w.UndoShowing == null && !w.TablesClearedForUndo, "Undo puts the tables back and the bar goes");

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
            Check(tv["top"] == "Shade Gloves" && tv["topCount"] == "×1" && tv["topWorth"] == "+28K" && tv["topGrade"] == "Legend",
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
            Check(w.MarkStateForTest is (0, false, null) && w.FeedHint.Visibility == Visibility.Visible, "...and Undo there takes it back, the hint returns");
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

    public static void Shot(Settings settings, string dir, bool demo, string kind = "Open world", bool compact = false, bool welcome = false, string? skin = null, bool full = false, bool tall = false)
    {
        Ensure();
        Directory.CreateDirectory(dir);
        var w = new MainWindow(settings, watch: false) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -12000, Top = -12000, ShowActivated = false };
        w.Show();
        if (skin != null) Skins.Apply(skin);
        // KL_SCREEN_H=1040: render as a screen of that height would show it (the window never grows past the work area)
        if (double.TryParse(Environment.GetEnvironmentVariable("KL_SCREEN_H"), out var screenH) && screenH > 300) w.MaxHeight = screenH;
        // --tall: the whole window, however tall, so a render shows every block of a view (named "-tall")
        if (tall) w.MaxHeight = 4000;
        if (demo) w.ShowDemo(kind);
        if (compact) w.ShowCompact();
        if (full) w.ShowFull();
        if (welcome) w.ShowWelcomeDemo(skin ?? "night");
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
        var name = welcome ? "ui-welcome" : !demo ? "ui-main" : kind switch { "Gathering" => "ui-main-gathering", "Pets" => "ui-main-pets", "Market" => "ui-main-market", "Crafting" => "ui-main-crafting", "CraftingEmpty" => "ui-main-crafting-empty", "CraftingUpgrades" => "ui-main-crafting-upgrades", "Events" => "ui-main-events", _ => "ui-main-tracking" };
        Save((FrameworkElement)w.Content, Path.Combine(dir, name + (compact ? "-compact" : full ? "-full" : "") + (tall ? "-tall" : "") + (skin != null ? "-" + skin : "") + ".png"), (compact ? 640 : 800) - 16);
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
        if (demo && kind is not ("Crafting" or "CraftingEmpty" or "CraftingUpgrades" or "Events")) w.SaveShareCard(Path.Combine(dir, "ui-card-discord.png"));
        if (demo && kind == "Crafting") w.ShotCraftCard(Path.Combine(dir, "ui-craft-card.png"));
        if (demo) w.ShotPlayerCards(dir, kind);
        // a tapped feed line's report sheet (direction C), for a farming render
        if (demo && kind == "Open world") w.ShotLineReport(Path.Combine(dir, "ui-line-report.png"));
        // the Today strip's hover cards (0.8.50): a farming drop's and the Kinah's
        if (demo && kind == "Open world") w.ShotTodayCards(dir);
        // and on the Events page, a run reward's (0.8.51: each page's strip shows its own day)
        if (demo && kind == "Events") w.ShotTodayRunCard(dir);
        // the Accuracy card on its own (the card the Farming page's chip opens), with its things to look at
        if (demo && kind is not ("Crafting" or "CraftingEmpty" or "CraftingUpgrades" or "Events" or "Market")) w.ShotAccuracyCard(Path.Combine(dir, "ui-accuracy-card.png"));
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

        foreach (var (file, state) in states)
        {
            var h = new HudWindow(new[] { "feed" }) { Left = -12000, Top = -12000, ShowActivated = false };
            h.Show();
            h.Update(state);
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
        if (!demo || kind != "Open world") return;
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
    }

    /// <summary>Lets pending layout and render work run, since no message loop is running in this mode.</summary>
    static void DoEvents()
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ContextIdle, new Action(() => frame.Continue = false));
        System.Windows.Threading.Dispatcher.PushFrame(frame);
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
