using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DRect = System.Drawing.Rectangle;

namespace KinahLedger.Ui;

/// <summary>Starts the WPF side without an App.xaml, so Program.Main stays the single entry point.</summary>
public static partial class WpfApp
{
    /// <param name="player">True for the window the player uses (and the debug cycles on the real game): their chosen look from
    /// ui.json. False for renders and self-tests, which start from the default look and never read ui.json.</param>
    static Application Ensure(bool player = false)
    {
        if (Application.Current != null) return Application.Current;
        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/KinahLedger;component/Ui/Theme.xaml", UriKind.Relative) });
        Skins.Apply(player ? KinahLedger.Session.UserPrefs.Load().Skin : Skins.Default);   // the look, over the base theme
        return app;
    }

    /// <summary>The real app lifecycle (window shown, message loop running) with an automatic start → wait → stop, to catch anything that kills the process.</summary>
    public static int Live(Settings settings, DRect? region, int ms)
    {
        var app = Ensure(player: true);
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
        var app = Ensure(player: true);
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
        Ensure(player: true);
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
        Ensure(player: true);
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
        // Badge's state lines each start with a sign (2026-11-08 audit: they differed by text colour alone): the stop's flash
        // the stop square, a waiting notice the gold "i", a start the run dot
        static IEnumerable<DependencyObject> BadgeTree(DependencyObject d) { yield return d; foreach (var c in LogicalTreeHelper.GetChildren(d).OfType<DependencyObject>()) foreach (var x in BadgeTree(c)) yield return x; }
        bool BadgeSign(Dictionary<string, string> v, System.Windows.Media.ImageSource sign) =>
            BadgeTree(HudWindow.Preview(HudLooks.Strip, null, v)).OfType<System.Windows.Controls.Image>().Any(i => i.Source == sign && i.Parent is FrameworkElement row && row.Visibility == Visibility.Visible);
        Check(BadgeSign(new() { ["flash"] = "Run stopped · 1.27M this run", ["run"] = "" }, HudKit.StopSign)
              && BadgeSign(new() { ["notice"] = "Update ready · installs when you leave the game" }, HudKit.NoticeSign)
              && BadgeSign(new() { ["flash"] = "Run started", ["run"] = "1" }, HudKit.StartSign),
            "Badge: a stop, a notice and a start each show their own sign");
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
        // the recap comes alive (2026-11-11): its opening held at points of its course, as KL_MOTION_AT holds a render
        {
            var frozenWas = Motion.FrozenAt; bool stillWas = Motion.Still;
            static IEnumerable<DependencyObject> Tree(DependencyObject d) => new[] { d }.Concat(LogicalTreeHelper.GetChildren(d).OfType<DependencyObject>().SelectMany(Tree));
            static List<string> Texts(DependencyObject d) => Tree(d).OfType<System.Windows.Controls.TextBlock>().Select(t => t.Text).ToList();
            static List<FrameworkElement> Tiles(DependencyObject d) => Tree(d).OfType<System.Windows.Controls.Grid>().Where(g => g.ToolTip is string).Cast<FrameworkElement>().ToList();
            System.Windows.Controls.Border Held(string state, double? at)
            {
                Motion.FrozenAt = at;
                var (_, card, open) = w.RecapSamples().First(x => x.State == state);
                open();
                card.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                return card;
            }
            var early = Held("run", 0.1); var settled = Held("run", 1);
            var et = Texts(early); var ends = Texts(settled);
            Check(early.Opacity is > 0 and < 1 && !et.Contains("686K") && !et.Contains("955K") && Tiles(early).All(x => x.Opacity == 0)
                  && ends.Contains("686K") && ends.Contains("47:12") && ends.Contains("955K") && ends.Contains("+18%") && ends.Contains(" vs your average here")
                  && settled.Opacity == 1 && Tiles(settled).Count == 5 && Tiles(settled).All(x => x.Opacity == 1 && x.RenderTransform is not System.Windows.Media.ScaleTransform),
                $"recap: a tenth in, the card is arriving and its figures and tiles wait their turn; at the end it reads 686K, 47:12, 955K, +18% ({early.Opacity:0.00}; {string.Join(" ", ends.Take(12))})");
            Check(early.DesiredSize == settled.DesiredSize, $"recap: the card keeps its size while it plays ({early.DesiredSize} and {settled.DesiredSize})");
            // the best drop (a Legend) a tenth into its course: grown in part way and glowing in the dark cards' Legend blue
            var glowing = Held("run", (MainWindow.RecapBestDropAt + 0.1 * Motion.GlowMs) / (MainWindow.RecapBestDropAt + Motion.GlowMs));
            var glow = Tree(glowing).OfType<UIElement>().Select(e => e.Effect).OfType<System.Windows.Media.Effects.DropShadowEffect>().FirstOrDefault(d => d.BlurRadius == 18);
            Check(glow is { Opacity: > 0.3 } && glow.Color == System.Windows.Media.Color.FromRgb(0x5A, 0xA6, 0xFF), $"recap: the best drop glows once in its grade's colour as it lands ({glow?.Color} {glow?.Opacity:0.00})");
            var best = Texts(Held("new-best", 1)); var thin = Texts(Held("no-loot", 1)); var longName = Texts(Held("long-name", 1));
            Check(best.Contains("★ NEW BEST HERE") && best.Any(x => x.Contains("it was 1.05M")) && !best.Contains(" vs your average here") && !ends.Contains("★ NEW BEST HERE"),
                "recap: a new best has the gold tag and its figures, and no line against the average; a run as usual has no tag");
            Check(!thin.Contains("DROPS") && !thin.Any(x => x.Contains("average")) && thin.Count(x => x == "148K") == 1 && thin.Contains("all Kinah") && !thin.Contains("Kinah") && !thin.Contains("Loot"),
                $"recap: a run with no loot says its Kinah once, as the total (all Kinah), with no DROPS and, on thin history, no line against the average ({string.Join(" ", thin)})");
            Check(longName.Any(x => x.EndsWith("The Forgotten Sanctuary of the Silvermoon")) && longName.Contains("\u22127%") && longName.Contains("+4"),
                $"recap: a long place name is whole, a run under the average reads \u22127%, and drops past the strip are counted ({string.Join(" ", longName.Where(x => x.Contains('%') || x.StartsWith('+')))})");
            Motion.Still = true;
            var calm = Held("run", null);
            Check(!Motion.On && calm.Opacity == 1 && Texts(calm).Contains("686K") && Tiles(calm).All(x => x.Opacity == 1), "recap: with motion off the card shows settled at once");
            // a card with its own way out: closed at once, its drawing kept until the leave is done, then gone
            if (InCard.Layer is { } layer)
            {
                Action? leaving = null;
                int drawn = layer.Children.Count;
                var ic = new InCard { Child = new System.Windows.Controls.Border { Width = 10, Height = 10 }, Leave = remove => { leaving = remove; return remove; } };
                ic.IsOpen = true; ic.IsOpen = false;
                bool kept = !ic.IsOpen && InCard.Current == null && layer.Children.Count == drawn + 1;
                leaving?.Invoke();
                bool goneAfter = layer.Children.Count == drawn;
                // opened again while still leaving: the old drawing goes at once, and only the new one is drawn
                ic.IsOpen = true; ic.IsOpen = false; ic.IsOpen = true;
                bool once = layer.Children.Count == drawn + 1;
                ic.IsOpen = false;
                bool leavingNow = layer.Children.Count == drawn + 1;
                var second = new InCard { Child = new System.Windows.Controls.Border { Width = 10, Height = 10 } };
                second.IsOpen = true;
                bool notOverlapping = layer.Children.Count == drawn + 1;
                second.IsOpen = false;
                Check(kept && goneAfter && once && leavingNow && notOverlapping && layer.Children.Count == drawn,
                    "recap: a card that leaves is closed at once, its drawing goes when the leave is done, and opening it or another card ends the leave at once");
            }
            // the leave's own timer: a leave whose course never ticks (the clock held) still takes the card away, once
            Motion.Still = false; Motion.FrozenAt = null;
            if (Motion.On)
            {
                int removals = 0;
                var leaver = new System.Windows.Controls.Border();
                Motion.Leave(leaver, () => removals++);
                Motion.Halt(leaver);   // as if the course never ran: only the timer is left
                var until = DateTime.UtcNow.AddMilliseconds(Motion.LeaveMs * 3);
                while (DateTime.UtcNow < until) { DoEvents(); Thread.Sleep(10); }
                int byTimer = removals;
                var cut = new System.Windows.Controls.Border();
                var ended = Motion.Leave(cut, () => removals++);
                ended(); ended();
                bool settledBack = cut.Opacity == 1 && !Motion.Playing(cut);
                Check(byTimer == 1 && removals == 2 && settledBack, $"recap: a leave that never finishes is taken away by its timer, once; ended at once it removes once and the card is put back settled ({byTimer} {removals} {settledBack})");
            }
            Motion.Still = stillWas; Motion.FrozenAt = frozenWas;
        }

        // Options › Game reading › Explain each count (Steven, 2026-11-04: "an option you can turn on"): off by default, the
        // line card's WHY IT WAS COUNTED only while it is on, kept on Save, off again
        o = Open();
        o.Go(OptionsWindow.Page.Reading);
        Check(o.PageReading.Visibility == Visibility.Visible && o.ExplainChk.IsChecked == false && !p.ExplainCounts, "Explain each count: on Game reading, off by default");
        Check(o.InvDetailsChk.IsChecked == false && !p.InventoryDetails, "Inventory details: off by default (the window shows the worth and the items)");
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

        // Options › Game reading › Read the game's network traffic (2026-11-13, Packets/PacketReader.cs): on by default, saved
        // on Save; a test window never reads (the watcher's loop doesn't run), so its reader stays off whatever the switch says
        o = Open();
        o.Go(OptionsWindow.Page.Reading);
        Check(o.PacketChk.IsChecked == true && p.PacketReading && o.PacketStateLbl.Text.StartsWith("Now: "), $"packet reading: on Game reading, on by default, \"{o.PacketStateLbl.Text}\"");
        Flip(o.PacketChk); Click(o.SaveBtn); DoEvents();
        Check(!p.PacketReading && !w.PacketsForTest.Enabled, "...off and saved: the reader is off");
        o = Open(); Flip(o.PacketChk); Click(o.SaveBtn); DoEvents();
        Check(p.PacketReading && !w.PacketsForTest.On, "...on again, and a test window still reads nothing");

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
        // the pages by key (0.10.26; redesign 2026-11-14: as the pills sit, Farm, Craft, Runs, Timers)
        w.PressForTest(Key.D2, ModifierKeys.Control); Check(w.PartForTest == "Crafting", "Ctrl+2 shows Crafting");
        w.PressForTest(Key.D3, ModifierKeys.Control); Check(w.PartForTest == "Events", "Ctrl+3 shows Runs");
        w.PressForTest(Key.D4, ModifierKeys.Control); Check(w.PartForTest == "Timers" && w.TimersPage.Visibility == Visibility.Visible && w.TimersPage.Children.Count > 0, "Ctrl+4 shows the Timers page, drawn");
        w.PressForTest(Key.NumPad1, ModifierKeys.Control); Check(w.PartForTest == "Farming", "Ctrl+1 (the keypad's too) shows Farming");
        // no page names where its figures come from (Steven, 2026-11-14: only Options may); every page, the timers live and empty
        foreach (var timers in new[] { "live", "empty" })
        {
            w.TimersSampleForShot(timers);
            var said = new List<string>();
            foreach (var page in new[] { "Farming", "Events", "Crafting", "Timers", "Quests" })
            {
                w.ShowPartForTest(page); DoEvents();
                said.AddRange(w.SourceWordsShown().Select(h => $"{page}: {h}"));
            }
            said.AddRange(w.SourceWordsShown(w.BuildBossCard(DateTime.UtcNow)).Select(h => $"timers window: {h}"));
            Check(said.Count == 0, $"no page or hover outside Options names the reading's source (timers {timers}){(said.Count > 0 ? ": " + string.Join(" | ", said.Take(6)) : "")}");
        }
        w.TimersSampleForShot("off");
        w.ShowPartForTest("Farming"); DoEvents();

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
        // UX pass (2026-11-13): the open bag card takes the strip's place, not a second copy of the same slots under it
        Check(!w.BagStripForTest().Strip, "...and stands in for the bag strip while it is open");
        w.ToggleChipForTest("bag"); DoEvents();
        Check(w.SummaryCard.Visibility != Visibility.Visible, "...and closes it again");
        w.ShowFull(); DoEvents();
        Check(w.QuestRight.Visibility == Visibility.Visible && w.SummaryCard.Visibility == Visibility.Visible && w.PickupStrip.Visibility == Visibility.Visible && w.TopDropsBox.Visibility == Visibility.Visible && w.Rows.Items.Count > 3 && w.HeadStack.Width > compactLayout * 1.8,
            $"Full view: the bag in the second column, the zone board open, {compactW:0} -> {w.ActualWidth:0} px wide");
        w.ShowCompact(); DoEvents();
        Check(Math.Abs(w.HeadStack.Width - compactLayout) < 1, "back to Compact: the width it had");

        // the Farming page, calm (2026-10-28, Steven: "THIS IS A LOT OF INFO ON ONE WINDOW"): one period at a time in LOOT, one
        // way to write time, no zone chip under a minute, nothing said while all is well, no section with nothing in it
        w.ShowDemo(); w.ShowCompact(); DoEvents();   // the sample again, after the Options round above changed the server and said things
        var here = w.LootForTest();
        // the overall count across zones is the default since 0.10.43 (Steven, 2026-11-11), so LOOT's heading is All zones
        // UX pass (2026-11-13): each figure one home. The hero's line says the Kinah, the loot and the kills; LOOT's first side
        // no longer repeats them as a Kinah slot, a total (the two added up) and the hero's "42 min", so its line is the kinds alone
        // one screen (0.10.70; Steven, 2026-11-13: "i dont like the 'this zone' toggle"): LOOT's first side is This session
        Check(here.Here == "This session" && here.Slots >= 8 && here.Note.EndsWith(" kinds") && !here.Note.Contains("min") && here.Total.Trim().Length == 0,
            $"LOOT, {here.Here}: {here.Slots} slots, \"{here.Note}\", no total (the hero's line has the Kinah and the loot)");
        var heroLine = w.HeroStatsForTest();
        Check(heroLine.Count == 3 && heroLine[0].EndsWith(" Kinah") && heroLine[1].EndsWith(" loot") && heroLine[2].EndsWith(" kills") && heroLine.All(p => "KMB0123456789".Contains(p.Split(' ')[0][^1])),
            "the hero's line: " + string.Join(" · ", heroLine));
        // the place's share beside each figure, and the place's own hour beside the session's (0.10.70): the sample farmed
        // Eastern Cantas Valley first, then Western, where it is now
        var heroHere = w.HeroHereForTest(); var heroTop = w.HeroForTest();
        Check(heroHere.Count == 3 && heroHere.All(x => x.EndsWith(" here") && x.Length > 6) && heroTop.Label.StartsWith("an hour · ") && heroTop.Label.Split('\n')[0].EndsWith(" here")
            && w.HeroStatTipForTest("Kinah").Contains("in Western Cantas Valley."),
            $"one screen: the place's share beside each figure ({string.Join(" / ", heroHere)}), its hour beside the session's (\"{heroTop.Label.Split('\n')[0]}\")");
        // how the total adds up moved with it: from the bag's total to the loot figure's hover
        Check(w.HeroStatTipForTest("loot") is var tt && tt.Contains("Kinah picked up") && tt.Contains("Total, " + Data.PriceBook.Fee), "the hero's loot figure says how Kinah and loot add up on hover");
        var bagStrip = w.BagStripForTest();
        Check(bagStrip.Strip && bagStrip.Slots is > 0 and <= 11 && bagStrip.Spark && bagStrip.SparkPoints == 30,
            $"the bag strip is one row of items, no Kinah slot ({bagStrip.Slots} slots); the hero's trend draws the last 30 of the sample's minutes ({bagStrip.SparkPoints} points)");
        // Full view's columns end level (Steven's no-empty-space rule; the left one used to stop halfway beside the second)
        w.ShowFull(); DoEvents();
        var bal = w.FullBalanceForTest();
        Check(bal.Left > 0 && bal.Right > 0 && Math.Abs(bal.Left - bal.Right) <= 0.2 * Math.Max(bal.Left, bal.Right),
            $"Full view: the columns end within a fifth of each other, {bal.Left:0} and {bal.Right:0} px; Just dropped {(bal.FeedRight ? "right" : "left")}, Where to farm {(bal.WhereRight ? "right" : "left")}");
        w.ShowCompact(); DoEvents();
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
        // worth first by name, so the check holds whatever order the window starts in
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
        // Week wrapped and Day wrapped (Ui/MainWindow.Wrapped.cs): the button at the foot of LOOT's Week and Today sides opens the
        // sheet (off the screens in a render), whose farming is that side's own total and whose total adds the runs
        {
            var wf = w.WrappedFactsForTest(Session.Wrapped.Week);
            bool opened = w.WrapButtonForTest(false) == "Week wrapped" && w.ClickWrapButtonForTest(false); DoEvents();
            var sh = w.WrappedSheetForTest();
            Check(opened && w.FootTotal.Text == Amounts.Short(wf.Farmed) && sh.Total == Amounts.Short(wf.Total) && (wf.FromRuns > 0 || sh.Total == w.FootTotal.Text)
                && sh.Stats.Count == 3 && sh.Rows.Count >= 2 && sh.Thin.Length == 0 && sh.Buttons.Contains("Copy picture") && sh.Buttons.Contains("Save picture"),
                $"Week wrapped: LOOT's Week side opens it, {sh.Total} made as the Week side's {w.FootTotal.Text}, tiles {string.Join(" / ", sh.Stats)}, {string.Join(" / ", sh.Rows)}");
            w.CloseWrapped();
            w.PickLootForTest(true); DoEvents();
            var df = w.WrappedFactsForTest(Session.Wrapped.Day);
            opened = w.WrapButtonForTest(false) == "Day wrapped" && w.ClickWrapButtonForTest(false); DoEvents();
            var dsh = w.WrappedSheetForTest();
            Check(opened && w.FootTotal.Text == Amounts.Short(df.Farmed) && dsh.Total == Amounts.Short(df.Total) && dsh.Stats.Count == 3,
                $"Day wrapped: LOOT's Today side opens it, {dsh.Total} made as the Today side's {w.FootTotal.Text}, tiles {string.Join(" / ", dsh.Stats)}");
            w.CloseWrapped();
            w.PickLootWeekForTest(); DoEvents();
        }
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
        Check(w.LootForTest().Note == here.Note, "back to This session");
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

        // the Abyss (Steven, 2026-11-13: "in abyss zones the primary should be abyss points per hour and a secondary ticket
        // for kinah"): the sample's 118 kills paid 24,740 Abyss Points in 42 min 17 s, so the hero is their hour and Kinah and
        // loot an hour sit on the gold ticket under it; out of the Abyss the hero is the Kinah hour again and there is no ticket
        w.ShowDemo("Abyss"); DoEvents();
        var abyssHero = w.HeroForTest();
        string apHour = Amounts.Short(24_740 / ((42 * 60 + 17) / 3600.0));
        Check(abyssHero.Abyss && abyssHero.Hero == apHour && abyssHero.Label.EndsWith("Abyss Points") && abyssHero.Ticket.Contains(" Kinah and loot an hour") && !abyssHero.Ticket.StartsWith(apHour),
            $"the Abyss: the hero is {abyssHero.Hero} (expected {apHour}) \"{abyssHero.Label.Replace('\n', ' ')}\", the ticket \"{abyssHero.Ticket}\"");
        var abyssLine = w.HeroStatsForTest();
        Check(abyssLine.Count == 6 && abyssLine[0] == "24,740 AP" && abyssLine[2].EndsWith(" Kinah"), "...the hero's line leads with the points picked up: " + string.Join(" · ", abyssLine));
        // Rank Points (0.10.76): beside the Abyss Points, their hour on a ticket beside the Kinah one, deaths' cost on the hovers
        string rankTip = w.HeroStatTipForTest("Rank"), rankTicket = w.RankTicketForTest();
        Check(abyssLine[1] == "2,104 Rank" && rankTicket == "2,986 Rank an hour" && rankTip.Contains("2,104 Rank Points earned") && rankTip.Contains("2,986 an hour") && rankTip.Contains("64 lost on 2 deaths (1 to players): 2,040 kept"),
            $"...Rank Points: {abyssLine.ElementAtOrDefault(1)}, the ticket \"{rankTicket}\", on hover \"{rankTip.Replace('\n', ' ')}\"");
        // the game's messages told the kills apart (0.10.72): PvP and mobs in place of kills, players, monsters and deaths on hover
        string pvpTip = w.HeroStatTipForTest("PvP");
        Check(abyssLine[4] == "9 PvP" && abyssLine[5] == "109 mobs" && pvpTip.Contains("9 players and 109 monsters") && pvpTip.Contains("You died 2 times, 1 of them to players"),
            $"...kills told apart: {abyssLine.ElementAtOrDefault(4)} · {abyssLine.ElementAtOrDefault(5)}, on hover \"{pvpTip}\"");
        w.ShowDemo(); DoEvents();
        var worldHero = w.HeroForTest();
        Check(!worldHero.Abyss && worldHero.Ticket == "" && w.RankTicketForTest() == "" && worldHero.Label.Contains("Kinah and loot"), $"...out of the Abyss: the Kinah hour {worldHero.Hero}, no ticket");

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
              && w.MoreEntries().Any(e => e.Text == MainWindow.ShareText && e.Key == MainWindow.CopyPictureKey), "each button's tooltip says its key, and ⋯ › Clear everything and Share a picture… say Ctrl+N and Ctrl+Shift+C");

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
        Check(System.Windows.Controls.ToolTipService.GetShowOnDisabled(w.StartBtn), "Start run says why while it is off");

        // Share (0.9.3, ShareA): the Farming card's button says Share and opens the sheet inside the card (never a window or a
        // menu) on This session, the character name off by default; typed but off it is nowhere, on it is in the head line
        {
            var keep = w.SharePrefsForTest;
            w.SharePrefsForTest = (false, "");
            Check(w.MoreEntries().FirstOrDefault(e => e.Text == MainWindow.ShareText) is { Do: not null }, "Farming's ⋯ › Share a picture… opens the sheet");
            w.ToggleShareForTest("Farming"); DoEvents();
            var sh = w.ShareForTest();
            Check(sh.Open && sh.Host == "ShareHost" && sh.Scope == KinahLedger.Session.SharePicture.Today && !sh.Switch, $"Share opens inside the farming card on Today, name off ({sh.Host} {sh.Scope})");
            Check(new[] { "Copy picture", "Save picture", "Copy as text", "Close Share", "Today", "This session", "Last run", "This week" }.All(sh.Buttons.Contains),
                "...with the switch, Copy picture, Save picture, Copy as text and ✕: " + string.Join(", ", sh.Buttons));
            Check(w.SharePicForTest(KinahLedger.Session.SharePicture.Session) is { } sp && sp.Made != "0" && sp.Sub.Contains("kill") && sp.Site == "kinahledger.com",
                "This session's picture has the count's total and kills");
            Check(w.SharePicForTest(KinahLedger.Session.SharePicture.Today) is { } td && td.Kicker.StartsWith("TODAY") && td.Rows.Count > 0 && td.Rows[0].Item && td.Split != null && td.Stats.Count == 3,
                "Today's picture: the day's total split into loot and Kinah, three figures, the top drops");
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

        // the header (UX pass 2026-11-13): the same round buttons on every page (Timers since 0.10.85), each with a tooltip in words; the character
        // chip shows the character counted for with its class icon; the switcher lists every character, the one counted for
        // first and marked, each with its icon, when it was played and its week, Rename on every row and Count for on the others
        {
            static IEnumerable<DependencyObject> Tree(DependencyObject d) => new[] { d }.Concat(LogicalTreeHelper.GetChildren(d).OfType<DependencyObject>().SelectMany(Tree));
            var seen = new List<string>();
            foreach (var page in new[] { "Farming", "Crafting", "Events", "Timers", "Quests" })
            {
                w.ShowPartForTest(page); DoEvents();
                var shown = w.HeadButtons.Children.OfType<System.Windows.Controls.Button>().Where(b => b.Visibility == Visibility.Visible).ToList();
                seen.Add(page + ": " + string.Join(", ", shown.Select(b => System.Windows.Automation.AutomationProperties.GetName(b))));
                Check(shown.Count == 2 && shown.All(b => b.ToolTip is string t && t.Length > 20) && System.Windows.Automation.AutomationProperties.GetName(shown[0]) == "Overlays" && w.SiteText.IsVisible,
                    $"the header's round buttons on {page}: Overlays and Settings, each with a tooltip; the site is a footer link ({seen[^1]})");
            }
            // the Overlays button's menu: a switch per overlay, Kinah then Timers; a switch flips its overlay and the dot follows
            var ovs = w.OverlaysForTest();
            var entries = w.OverlayEntries();
            Check(string.Join(",", ovs.Select(p => p.Name)) == "Kinah,Timers" && entries.Count == 2 && entries.All(e => e.On != null && e.Sub.Length > 0),
                $"the header's Overlays menu: a switch each for Kinah, then Timers, with a line saying where each is set up ({string.Join(", ", ovs.Select(p => $"{p.Name} {(p.On ? "on" : "off")}"))})");
            bool kinahWas = ovs[0].On;
            entries[0].Do!(); DoEvents();
            bool flipped = w.OverlaysForTest()[0].On != kinahWas;
            bool dotOff = !w.OverlaysForTest().Any(o => o.On) ? !w.OverlaysDotForTest : w.OverlaysDotForTest;
            w.OverlayEntries()[0].Do!(); DoEvents();
            Check(flipped && dotOff && w.OverlaysForTest()[0].On == kinahWas && w.OverlaysDotForTest == w.OverlaysForTest().Any(o => o.On),
                $"...its Kinah switch turns the panel {(kinahWas ? "off" : "on")} and back; the button's green dot shows while any overlay is on ({flipped} {dotOff})");
            // an overlay that isn't one of its own just now (the Parser inside the Kinah overlay) is left out of the menu
            bool sep = false;
            w.RegisterOverlay("Test", () => true, _ => { }, "a test overlay", visible: () => sep);
            bool hiddenOut = !w.OverlayEntries().Any(e => e.Text == "Test overlay");
            sep = true;
            bool shownIn = w.OverlayEntries().Any(e => e.Text == "Test overlay");
            w.UnregisterOverlayForTest("Test");
            Check(hiddenOut && shownIn, $"...an overlay registered with a visible switch is in the menu only while it is one of its own ({hiddenOut} {shownIn})");
            Check(seen.Select(s => s[(s.IndexOf(':') + 1)..]).Distinct().Count() == 1, "...the same on every page");
        // the timers (0.10.85, Ui/MainWindow.Timers.cs): a Timers button in the header opens the window; the overlay never
        // takes focus, passes clicks through when asked, and Customize's switches land in the settings
        {
            var tw = w;
            tw.TimerPrefsForTest = Session.TimerPrefs.Defaults();
            tw.TimersSampleForShot("live");
            Click(tw.TimersTab); DoEvents();
            var btn = tw.TimersPopOutForTest;
            bool onPage = tw.PartForTest == "Timers" && btn != null && btn.IsVisible;
            if (btn != null) Click(btn);
            DoEvents();
            var open = Application.Current.Windows.OfType<BossWindow>().FirstOrDefault(x => x.IsVisible);
            bool popOut = open != null && open.Owner == null && open.ShowInTaskbar && open.ResizeMode == ResizeMode.CanResizeWithGrip;
            Check(onPage && open != null && popOut, $"timers: the Timers tab shows the page ({onPage}); its Pop out opens the Timers window ({open != null}), a pop-out of its own: no owner, in the taskbar, resizable ({popOut})");
            open?.Close();
            // the page's bells: the watched timers by default, a click on a bell watches a timer, again stops it
            var bells = tw.TimerBellsForTest();
            var gartua = Session.TimerBoard.BossKey("Eternal Gartua");
            var unwatched = bells.FirstOrDefault(b => !b.On && b.Key.StartsWith("boss:")).Key;
            bool defaults = bells.Any(b => b.Key == gartua && b.On) && bells.Any(b => b.Key == Session.TimerBoard.EventKey("Shugo Festival") && b.On);
            bool folded = unwatched == null;   // the other bosses fold under their map's line until opened
            Check(defaults && folded, $"...the page: Gartua and Shugo belled by default ({defaults}), the other bosses folded away ({folded}; {bells.Count} bells)");
            tw.ClickTimerBellForTest(gartua); DoEvents();
            bool off = !tw.TimerPrefsForTest.Watched(gartua);
            tw.OpenTimerGroupForTest("Verteron"); DoEvents();   // unwatched, Gartua went into its map's folded list
            tw.ClickTimerBellForTest(gartua); DoEvents();
            Check(off && tw.TimerPrefsForTest.Watched(gartua), $"...a bell's click stops watching Gartua ({off}); it moves into Verteron's list, where its bell watches it again");
            tw.TimersSampleForShot("off");
            tw.ShowPartForTest("Farming"); DoEvents();
            tw.SetTimersOverlay(true, save: false); DoEvents();
            Check(tw.OverlaysForTest().Any(p => p.Name == "Timers" && p.On) && tw.OverlaysDotForTest, "...the header's Overlays menu shows Timers on, and the button its green dot");
            var ov = tw.TimersOverlayForTest;
            long ex0 = ov?.ExStyleForTest ?? 0;                 // click-through by default
            ov?.InteractiveForTest(true); DoEvents();
            long exCtrl = ov?.ExStyleForTest ?? 0;              // Ctrl held: it takes the mouse
            ov?.InteractiveForTest(false);
            tw.SetTimersClickThrough(false); DoEvents();
            long ex1 = ov?.ExStyleForTest ?? 0;                 // click-through off
            tw.SetTimersClickThrough(true); DoEvents();
            bool noFocus = (ex0 & Native.ExNoActivate) != 0 && (ex0 & Native.ExToolWindow) != 0 && (ex0 & Native.ExTransparent) != 0
                && (exCtrl & Native.ExTransparent) == 0 && (exCtrl & Native.ExNoActivate) != 0 && (ex1 & Native.ExTransparent) == 0;
            Check(ov != null && ov.Topmost && !ov.ShowActivated && ov.Owner == null && noFocus,
                $"...the overlay: its own window (no owner), on top, never takes focus, out of Alt+Tab; click-through by default, clickable while Ctrl is held or with click-through off ({noFocus})");
            ov?.Show(new List<Session.TimerBoard.Item>(), DateTime.UtcNow);
            bool hidden = ov != null && !ov.IsVisible;
            ov?.Show(new List<Session.TimerBoard.Item> { new("boss:Heretic Layla", "Verteron", "Heretic Layla", "Rock Tomb Hill", Session.TimerBoard.Kind.Waiting, DateTime.UtcNow.AddMinutes(3), "3m 00s", "at 07:00") }, DateTime.UtcNow);
            bool shown = ov != null && ov.IsVisible;
            ov?.Show(new List<Session.TimerBoard.Item>(), DateTime.UtcNow);
            Check(hidden && shown && ov != null && !ov.IsVisible, $"...hidden with nothing near, shown with a timer 3 min away, hidden again ({hidden} {shown})");
            tw.SetTimersOverlay(false, save: false); DoEvents();
            Check(tw.TimersOverlayForTest == null && !tw.TimerPrefsForTest.OverlayOn, "...and off again");
            // Options › Timers: the overlay's switch and each timer's picks, saved as they change
            var to = new OptionsWindow(tw, OptionsWindow.Page.Timers) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -12000, Top = -12000, ShowActivated = false };
            to.Show(); DoEvents();
            var kairaKey = Session.TimerBoard.AbyssKey("Watcher Kaira");
            bool listed = to.TimerBoxes.ContainsKey(kairaKey) && to.TimerBoxes.ContainsKey(Session.TimerBoard.EventKey("Shugo Festival")) && to.TimerBoxes.Count >= Session.EventTimers.Schedule.Length + Session.AbyssBosses.Schedule.Length;
            bool byDefault = to.TimerBoxes.TryGetValue(kairaKey, out var kb) && kb.Overlay.IsChecked == true && kb.Chime.IsChecked == true
                && to.TimerBoxes.TryGetValue(Session.TimerBoard.EventKey("Shugo Festival"), out var sb) && sb.Overlay.IsChecked == true;
            if (kb.Chime != null) { Flip(kb.Chime); Flip(kb.Overlay); }
            bool picked = byDefault && !tw.TimerPrefsForTest.Chimes(kairaKey) && !tw.TimerPrefsForTest.Watched(kairaKey) && kb.Chime?.IsEnabled == false;
            if (to.TimersOverlayChk != null) Flip(to.TimersOverlayChk);
            DoEvents();
            bool overlayOn = tw.TimersOverlayForTest != null && tw.TimerPrefsForTest.OverlayOn;
            if (to.TimersOverlayChk != null) Flip(to.TimersOverlayChk);
            DoEvents();
            Check(listed && picked && overlayOn && tw.TimersOverlayForTest == null && to.Scroller.VerticalScrollBarVisibility == System.Windows.Controls.ScrollBarVisibility.Auto,
                $"Options › Timers lists the timers ({to.TimerBoxes.Count}), Watcher Kaira and Shugo watched by default; its Chime and Watch boxes change the picks ({picked}), the overlay's switch turns it on and off ({overlayOn}); the page scrolls");
            // placement (Steven, 2026-11-14: "let the user customize where the timer goes even if one is not up"): Move overlay
            // shows the strip now, outlined and clickable, with the next watched timers or a sample; Done keeps the spot on a
            // screen and it hides again with nothing due; with the overlay off it closes after; Reset forgets the spot
            {
                var pp = Session.TimerPrefs.Defaults();
                foreach (var i in tw.TimerItemsNow()) pp.SetWatched(i.Key, false);   // nothing watched: nothing due, so only placement shows it
                pp.OverlayOn = false;
                tw.TimerPrefsForTest = pp;
                bool buttons = tw.TimersMoveForTest != null && to.TimersMoveBtn != null && to.TimersResetBtn != null;
                tw.StartTimersPlacement(); DoEvents();
                var po = tw.TimersOverlayForTest;
                long ex = po?.ExStyleForTest ?? 0;
                bool placed = po != null && po.Placing && po.IsVisible && (ex & Native.ExTransparent) == 0 && (ex & Native.ExNoActivate) != 0 && po.RowsForTest == 2;
                if (po != null) { po.Left = SystemParameters.VirtualScreenLeft - 60000; po.Top = 300; po.FinishPlacing(); }
                DoEvents();
                bool kept = pp.X is double px && px >= SystemParameters.VirtualScreenLeft && px < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth && pp.Y is double;
                bool closedAfter = tw.TimersOverlayForTest == null && !pp.OverlayOn;
                pp.OverlayOn = true; tw.SetTimersOverlay(true, save: false); DoEvents();
                tw.StartTimersPlacement(); DoEvents();
                var po2 = tw.TimersOverlayForTest;
                po2?.FinishPlacing(); DoEvents();
                bool hiddenAfter = po2 != null && !po2.IsVisible && !po2.Placing && ((po2.ExStyleForTest & Native.ExTransparent) != 0);
                tw.ResetTimersOverlayPosition();
                bool reset = pp.X == null && pp.Y == null;
                tw.SetTimersOverlay(false, save: false); DoEvents();
                Check(buttons && placed && kept && closedAfter && hiddenAfter && reset,
                    $"Move overlay (the Timers page and Settings): the strip shows at once, outlined and clickable, with a sample when nothing is due ({placed}); Done keeps the spot on a screen ({kept}), closes it again with the overlay off ({closedAfter}), hides it with the overlay on, clicks through again ({hiddenAfter}); Reset forgets the spot ({reset})");
            }
            to.Close();
        }
            w.ShowPartForTest("Farming"); DoEvents();
            Check(w.MoreEntries().Any(e => e.Text == MainWindow.FullViewText && e.On != null), "Compact/Full is a switch in Farming's ⋯ menu");
            var whoChip = w.CharChipForTest();
            Check(whoChip.Shown && whoChip.Name == MainWindow.DemoCharacterName && whoChip.Picture, $"the header's chip shows the character counted for, with its class icon ({whoChip.Name}, icon drawn: {whoChip.Picture})");
            var rows = w.SwitcherForTest();
            Check(rows.Count == 3 && rows.Count(r => r.Counting) == 1 && rows[0].Counting && rows[0].Name == MainWindow.DemoCharacterName && rows[0].Status == "Counting now",
                "the switcher lists the three characters, the one counted for first and marked: " + string.Join(" | ", rows.Select(r => $"{r.Name} ({r.Status})")));
            Check(rows.All(r => r.Picture) && rows.Skip(1).All(r => r.Status.StartsWith("Played ")) && rows.All(r => r.Week.StartsWith("Last 7 days: ")),
                "...each with its class icon, when it was played and its week: " + string.Join(" | ", rows.Select(r => r.Week)));
            var card = w.SwitcherCardForTest();
            var texts = Tree(card).OfType<System.Windows.Controls.TextBlock>().Select(t => t.Text).ToList();
            var named = Tree(card).OfType<FrameworkElement>().Select(e => System.Windows.Automation.AutomationProperties.GetName(e)).Where(n => n.Length > 0).ToList();
            Check(texts.Count(t => t == "Counting now") == 1 && rows.All(r => texts.Contains(r.Name)), "the card draws every name and one Counting now");
            Check(named.Count(n => n.StartsWith("Rename ")) == 3 && named.Count(n => n.StartsWith("Count for ")) == 2 && named.Contains("Count as a new character"),
                "...Rename on every row, Count for on the two others, and Count as a new character");

            // another character's week is read from its own folder and nothing there is written (Session/CharacterLooks.cs)
            var alt = TestTemp.Path("KinahLedger-options-test-character-" + Environment.ProcessId);
            if (Directory.Exists(alt)) Directory.Delete(alt, true);
            Directory.CreateDirectory(alt);
            var today = DateTime.Today;
            File.WriteAllText(Path.Combine(alt, "days.json"), System.Text.Json.JsonSerializer.Serialize(new Session.DayFile
            {
                Since = Session.DayStore.DayOf(today.AddDays(-10)),
                Days = { new() { Day = Session.DayStore.DayOf(today.AddDays(-1)), Made = 800_000 }, new() { Day = Session.DayStore.DayOf(today.AddDays(-9)), Made = 5_000_000 } },
            }));
            File.WriteAllText(Path.Combine(alt, "best.json"), """{"Siel (Elyos)|Western Cantas Valley":{"PerHr":1200000,"Minutes":40,"At":"2026-11-10"},"Zikel (Asmodian)|Ara Village":{"PerHr":9000000,"Minutes":20,"At":"2026-11-09"}}""");
            var altFiles = Directory.GetFiles(alt).Select(f => (f, File.GetLastWriteTimeUtc(f))).ToList();
            var look = Session.CharacterLooks.Read(alt, "Siel (Elyos)", w.ItemsForTest);
            Check(look.Farmed == 800_000 && look.Best?.PerHr == 1_200_000 && look.BestZone == "Western Cantas Valley" && look.Runs == 0,
                $"an alt's week from its folder: the last 7 days only, its best on this server ({Amounts.Short(look.Farmed)}, best {look.Best?.PerHr} in {look.BestZone})");
            Check(Directory.GetFiles(alt).Select(f => (f, File.GetLastWriteTimeUtc(f))).SequenceEqual(altFiles), "...and nothing in that folder written");
            Directory.Delete(alt, true);
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
            Check(!more.Any(e => e.Text == "All zones") && more.Any(e => e.Text == "Clear everything" && e.Do != null) && more.Any(e => e.Text == "Set a goal…" && e.Do == null && e.Sub.Length > 0),
                "the ⋯ menu: no All zones switch (one screen), Clear everything, Set a goal off (with why) once three are set: " + string.Join(" | ", more.Select(e => e.Text)));
            Check(w.BuildMenu(more) is { } menuCard && Tree(menuCard).OfType<System.Windows.Controls.ContextMenu>().Count() == 0 && Tree(menuCard).OfType<System.Windows.Controls.MenuItem>().Count() == 0,
                "the ⋯ menu is drawn in the card kit, no Windows menu items");
            w.ShowGoalsSample(2); DoEvents();
            Check(w.MoreEntries().First(e => e.Text == "Set a goal…").Do != null, "with room for a goal, ⋯ › Set a goal… opens the sheet");
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
            // Options > General > Quests off: the rows go (Quests has no tab: Next up's head opens it); on again brings them back
            var og = Open(); og.Go(OptionsWindow.Page.General); Flip(og.QuestsChk); Click(og.SaveBtn); DoEvents();
            Check(w.QuestsForTest().Rows == 0, "Options > General > Quests off: no quest rows on Next up");
            og = Open(); og.Go(OptionsWindow.Page.General); Flip(og.QuestsChk); Click(og.SaveBtn); DoEvents();
            Check(w.QuestsForTest().Rows > 0, "...and on again");
            // the Crafting page's Next up carries the two materials' quests while the farming count is on
            w.ShowDemo("Crafting"); DoEvents();
            Check(w.CraftNextForTest().Split(',').Count(x => x == "quest") == 2, $"Crafting's Next up: the two materials' quest rows: {w.CraftNextForTest()}");
            w.ShowDemo(); DoEvents();
        }

        // the Parser (2026-11-13, Ui/MainWindow.Parser.cs, Ui/WpfApp.Parser.cs)
        ParserChecks(w, Check);

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
            Check(w.WrapButtonForTest(true) == "Week wrapped", $"...its head's recap button names the week: \"{w.WrapButtonForTest(true)}\"");
            w.PickRunsWeekForTest(false); DoEvents();
            Check(w.TodayTitle.Text == "TODAY" && !MainWindow.InlineText(w.TodayNote).Contains(" to "), $"...and back to Today: \"{MainWindow.InlineText(w.TodayNote)}\"");
            // Day wrapped from the strip's head: the day's runs as the strip sums them, beside the day's farming
            {
                var df = w.WrappedFactsForTest(Session.Wrapped.Day);
                bool opened = w.WrapButtonForTest(true) == "Day wrapped" && w.ClickWrapButtonForTest(true); DoEvents();
                var sh = w.WrappedSheetForTest();
                Check(opened && w.TodaySum.Text == Amounts.Short(df.FromRuns) && sh.Total == Amounts.Short(df.Total) && (df.Farmed == 0 || sh.Split.Contains(w.TodaySum.Text + " from ")),
                    $"Day wrapped from the TODAY strip: its runs {w.TodaySum.Text} as the strip's sum, {sh.Total} made, \"{sh.Split}\"");
                w.CloseWrapped();
            }
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
            bool Lit(System.Windows.Controls.Button tab) => ReferenceEquals(tab.Style, w.FindResource("NavOn"));
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

        // the window under the mouse (2026-11-11: Ui/Motion.Glide.cs, Ui/Motion.Feel.cs, Ui/MainWindow.PageTurn.cs), each motion
        // held at a point of its course: a page tab's light slides from the old tab and ends exactly on the new one at every
        // page and window size without the tabs measuring differently; a switch's light the same, its inks swapping halfway;
        // the page cross-fades and rises; a card lifts, a tile's light passes, a button sinks and springs back; with motion
        // off a hover and a press show at once and nothing slides, turns or shines
        {
            var frozenWas = Motion.FrozenAt; bool stillWas = Motion.Still;
            static Rect Near(Rect r) => new(Math.Round(r.X, 3), Math.Round(r.Y, 3), Math.Round(r.Width, 3), Math.Round(r.Height, 3));
            var tabs = new[] { w.FarmTab, w.CraftTab, w.EventsTab, w.TimersTab }.Where(t => t.Visibility == Visibility.Visible).ToList();
            var tabRow = (System.Windows.Controls.Panel)System.Windows.Media.VisualTreeHelper.GetParent(w.FarmTab);
            // where a tab's face draws its light, as the screen has it: the tab's box in its row, inside its one-pixel edge
            Rect Face(System.Windows.Controls.Button b)
            {
                var r = b.TransformToVisual(tabRow).TransformBounds(new Rect(b.RenderSize));
                var face = (System.Windows.Controls.Border)b.Template.FindName("b", b);
                double dpi = System.Windows.Media.VisualTreeHelper.GetDpi(face).DpiScaleX, edge = Math.Round(face.BorderThickness.Left * dpi) / dpi;
                r.Inflate(-edge, -edge);
                return r;
            }
            foreach (var scale in new[] { 0.8, 1.0, 1.5 })
            {
                Motion.FrozenAt = 1; Click(w.FarmTab); w.PreviewWindowScale(scale); DoEvents();
                var size = new Size(w.PartTabs.ActualWidth, w.PartTabs.ActualHeight);
                var off = new List<string>();
                var from = w.FarmTab;
                foreach (var tab in tabs.Skip(1).Append(w.FarmTab))
                {
                    Motion.FrozenAt = 0; Click(tab); DoEvents();
                    var start = Motion.LightNow(tabRow);
                    if (start is not { } s0 || Near(s0) != Near(Face(from))) off.Add($"{tab.Name} starts at {start} not {Face(from)}");
                    Motion.FrozenAt = 1 - 1e-9; Click(from); DoEvents(); Click(tab); DoEvents();
                    var end = Motion.LightNow(tabRow);
                    if (end is not { } e1 || Near(e1) != Near(Face(tab))) off.Add($"{tab.Name} ends at {end} not {Face(tab)}");
                    if (new Size(w.PartTabs.ActualWidth, w.PartTabs.ActualHeight) != size) off.Add($"{tab.Name} measured {w.PartTabs.ActualWidth}x{w.PartTabs.ActualHeight}");
                    Motion.FrozenAt = 1; Click(from); DoEvents(); Click(tab); DoEvents();
                    if (Motion.LightNow(tabRow) != null || tabRow.Background != null || tab.ReadLocalValue(System.Windows.Controls.Control.BackgroundProperty) != DependencyProperty.UnsetValue || !Motion.GetLit(tab))
                        off.Add($"{tab.Name} not handed its own light at the end");
                    from = tab;
                }
                Check(off.Count == 0, $"the page tabs' light at {scale:0.0}: starts on the old tab, ends exactly on the new one and hands it its own light, the tabs {size.Width:0.#}x{size.Height:0.#} throughout{(off.Count > 0 ? ": " + string.Join("; ", off) : "")}");
            }
            w.PreviewWindowScale(1); DoEvents();

            // an in-page switch (the Today strip's Today / Week, on Runs): the light a tenth in, the inks not yet swapped
            Motion.FrozenAt = 1; Click(w.EventsTab); w.PickRunsWeekForTest(false); DoEvents();
            var segRow = (System.Windows.Controls.Panel)System.Windows.Media.VisualTreeHelper.GetParent(w.TodayDayBtn);
            var segOnInk = w.TodayDayBtn.Foreground; var segOffInk = w.TodayWeekBtn.Foreground;
            Motion.FrozenAt = 0.1; Click(w.TodayWeekBtn); DoEvents();
            var seg = Motion.LightNow(segRow);
            var dayBox = Motion.LightBox(w.TodayDayBtn)!.Value.Box; var weekBox = Motion.LightBox(w.TodayWeekBtn)!.Value.Box;
            bool segMid = seg is { } sm && sm.X > dayBox.X && sm.X < weekBox.X && w.TodayWeekBtn.Foreground == segOffInk && w.TodayDayBtn.Foreground == segOnInk;
            Motion.FrozenAt = 0.5; Click(w.TodayDayBtn); DoEvents(); Click(w.TodayWeekBtn); DoEvents();
            bool segSwapped = w.TodayWeekBtn.Foreground == segOnInk && w.TodayDayBtn.Foreground == segOffInk;
            Motion.FrozenAt = 1; Click(w.TodayDayBtn); DoEvents();
            Check(segMid && segSwapped && Motion.LightNow(segRow) == null && w.TodayDayBtn.ReadLocalValue(System.Windows.Controls.Control.ForegroundProperty) == DependencyProperty.UnsetValue,
                $"a switch's light slides too (a tenth in at x {seg?.X:0.#}, between {dayBox.X:0.#} and {weekBox.X:0.#}, inks not swapped: {segMid}); halfway the inks have swapped ({segSwapped}); at its end the styles' own inks");

            // the page turn, a fifth in: the page left is a picture over the window fading out, the new page rising and fading in
            Motion.FrozenAt = 1; Click(w.FarmTab); DoEvents();
            Motion.FrozenAt = 0.2; Click(w.CraftTab); DoEvents();
            var cover = w.LedgerCol.Children.OfType<System.Windows.Controls.Canvas>().SelectMany(c => c.Children.OfType<System.Windows.Controls.Image>()).FirstOrDefault();
            double rise = (w.LowerScroll.RenderTransform as System.Windows.Media.TranslateTransform)?.Y ?? -1, shown = w.LowerScroll.Opacity;
            Motion.FrozenAt = 1; Click(w.FarmTab); DoEvents();
            bool turnedBack = !w.LedgerCol.Children.OfType<System.Windows.Controls.Canvas>().Any() && w.LeftCol.RenderTransform == System.Windows.Media.Transform.Identity && w.LeftCol.Opacity == 1 && w.LowerScroll.Opacity == 1;
            Check(cover is { Opacity: > 0.2 and < 0.4 } && rise > 3.5 && rise < 4.5 && shown > 0.4 && shown < 0.6 && turnedBack,
                $"a page turn a fifth in: the page left fading ({cover?.Opacity:0.00}), the new one {rise:0.0} px low at {shown:0.00}; at its end nothing left over ({turnedBack})");

            // hover and press: a card lifts and its edge brightens, a tile's light passes, a button sinks and springs back
            var card = new System.Windows.Controls.Border { Width = 80, Height = 40, BorderThickness = new Thickness(1), Cursor = System.Windows.Input.Cursors.Hand };
            Motion.SetFeel(card, Feel.Lift);
            var inert = new System.Windows.Controls.Border { Width = 80, Height = 40, BorderThickness = new Thickness(1) };
            Motion.SetFeel(inert, Feel.Lift);
            Motion.FrozenAt = 0.5; Motion.HoverFeel(card, true); Motion.HoverFeel(inert, true);
            double lifted = Motion.TouchOf(card).Lift;
            bool edged = card.BorderBrush == Application.Current.FindResource("CardLineHover");
            Motion.FrozenAt = 1; Motion.HoverFeel(card, false);
            Check(lifted < -0.8 && lifted > -1 && edged && Motion.TouchOf(inert).Lift == 0 && Motion.TouchOf(card).Lift == 0 && card.RenderTransform == System.Windows.Media.Transform.Identity && card.ReadLocalValue(System.Windows.Controls.Border.BorderBrushProperty) == DependencyProperty.UnsetValue,
                $"a card under the mouse lifts ({lifted:0.00} px halfway) and its edge brightens; one that opens nothing stays still; left, it settles and its edge is the style's again");
            var tile = MainWindow.Under(w).FirstOrDefault(e => Motion.GetFeel(e) == Feel.Tile && e.IsVisible && e.ActualWidth > 0);
            bool shining = false, gone = false;
            if (tile != null)
            {
                Motion.FrozenAt = 0.5; Motion.HoverFeel(tile, true);
                shining = Motion.TouchOf(tile).Shining && InCard.Layer!.Children.OfType<System.Windows.Shapes.Rectangle>().Any(r => System.Windows.Controls.Panel.GetZIndex(r) < 0);
                Motion.FrozenAt = 1; Motion.HoverFeel(tile, false); Motion.HoverFeel(tile, true); Motion.HoverFeel(tile, false);
                gone = !Motion.TouchOf(tile).Shining && !InCard.Layer!.Children.OfType<System.Windows.Shapes.Rectangle>().Any();
            }
            Check(tile != null && shining && gone, $"an item tile under the mouse has a light pass across it, under the hover cards; at its end the light is gone ({tile?.GetType().Name}: {shining} {gone})");
            var btn = new System.Windows.Controls.Button();
            Motion.FrozenAt = 0.5; Motion.SetPressed(btn, true);
            double sunk = Motion.TouchOf(btn).Scale;
            Motion.SetPressed(btn, false);
            double sprung = Motion.TouchOf(btn).Scale;
            Motion.FrozenAt = 1; Motion.SetPressed(btn, true); Motion.SetPressed(btn, false);
            Check(sunk < 0.98 && sunk > 0.97 && sprung > 1 && sprung < 1.01 && btn.RenderTransform == System.Windows.Media.Transform.Identity,
                $"a button held sinks ({sunk:0.000} halfway), let go it springs past 1 ({sprung:0.0000} halfway) and settles with no transform left");

            var corner = new System.Windows.Controls.Button { RenderTransformOrigin = new Point(0, 1) };
            Motion.SetPressed(corner, true); Motion.SetPressed(corner, false);
            Check(corner.RenderTransformOrigin == new Point(0, 1), $"a press gives a button its own transform origin back ({corner.RenderTransformOrigin})");
            // a row washing as its goal is reached while the pointer is on it: the wash takes the transform and gives it back
            // at its end, and the row lifts again on the next hover
            var washed = new System.Windows.Controls.Border { Cursor = System.Windows.Input.Cursors.Hand, Background = System.Windows.Media.Brushes.Transparent };
            Motion.SetFeel(washed, Feel.Lift);
            Motion.FrozenAt = 0.5; Motion.HoverFeel(washed, true);
            Motion.Wash(washed, "Gold");
            Motion.HoverFeel(washed, false);
            bool washKept = washed.RenderTransform is System.Windows.Media.ScaleTransform;
            Motion.FrozenAt = 1; Motion.Wash(washed, "Gold");
            bool washGave = washed.RenderTransform == System.Windows.Media.Transform.Identity;
            Motion.HoverFeel(washed, true);
            double relifted = Motion.TouchOf(washed).Lift;
            Motion.HoverFeel(washed, false);
            Check(washKept && washGave && relifted == -1 && washed.RenderTransform == System.Windows.Media.Transform.Identity,
                $"a row washing under the pointer: leaving keeps the wash's transform ({washKept}), the wash gives it back ({washGave}), the row lifts again ({relifted})");

            // the page following what the player does (not a pick by hand) moves the tab's light without a slide
            var settle = DateTime.UtcNow.AddMilliseconds(700);
            while (DateTime.UtcNow < settle) { DoEvents(); Thread.Sleep(10); }
            Motion.FrozenAt = 0.5; w.ShowPartForTest("Crafting"); DoEvents();
            bool followSlid = Motion.LightNow(tabRow) != null;
            Motion.FrozenAt = 1; Click(w.FarmTab); DoEvents();
            Check(!followSlid && Motion.LightNow(tabRow) == null, $"the page following what the player does moves the tab's light at once, no slide ({followSlid})");

            // motion off (a render, or Windows's animations off): a hover and a press show at once, nothing slides, turns or shines
            Motion.Still = true; Motion.FrozenAt = null;
            Motion.HoverFeel(card, true);
            double offLift = Motion.TouchOf(card).Lift;
            bool offShine = false;
            if (tile != null) { Motion.HoverFeel(tile, true); offShine = Motion.TouchOf(tile).Shining; }
            Motion.SetPressed(btn, true);
            double offPress = Motion.TouchOf(btn).Scale;
            Motion.SetPressed(btn, false); Motion.HoverFeel(card, false); if (tile != null) Motion.HoverFeel(tile, false);
            Click(w.CraftTab); DoEvents();
            bool offTurn = Motion.LightNow(tabRow) == null && tabRow.Background == null && !w.LedgerCol.Children.OfType<System.Windows.Controls.Canvas>().Any();
            Click(w.FarmTab); DoEvents();
            Check(offLift == -1 && !offShine && offPress == 0.97 && Motion.TouchOf(btn).Scale == 1 && Motion.TouchOf(card).Lift == 0 && offTurn,
                $"motion off: a hover lifts at once ({offLift}), a press sinks at once ({offPress}), no light passes ({offShine}), a tab's light and the page don't slide ({offTurn})");

            // played for real, not held: a hover's clock runs to its end though it is in no window
            Motion.Still = false;
            if (Motion.On)
            {
                var live = new System.Windows.Controls.Border { Cursor = System.Windows.Input.Cursors.Hand };
                Motion.SetFeel(live, Feel.Lift);
                Motion.HoverFeel(live, true);
                var until = DateTime.UtcNow.AddMilliseconds(Motion.HoverMs * 4);
                while (DateTime.UtcNow < until && Motion.TouchOf(live).Lift != -1) { DoEvents(); Thread.Sleep(10); }
                double up = Motion.TouchOf(live).Lift;
                Motion.HoverFeel(live, false);
                until = DateTime.UtcNow.AddMilliseconds(Motion.HoverMs * 4);
                while (DateTime.UtcNow < until && live.RenderTransform != System.Windows.Media.Transform.Identity) { DoEvents(); Thread.Sleep(10); }
                Check(up == -1 && live.RenderTransform == System.Windows.Media.Transform.Identity, $"a hover played in real time lifts to -1 ({up}) and settles back with no transform left");
            }
            else Console.WriteLine("skip  a hover played in real time: Windows's animations are off on this PC");
            Motion.Still = stillWas; Motion.FrozenAt = frozenWas;
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

        // a new best's moment in the window (Ui/MainWindow.Bests.cs): held a third in, the best tag growing in and the ring
        // swelling; with motion off, no tag at all (the ring's ★ says it); the hour back under the best takes a tag away
        {
            var frozenWas = Motion.FrozenAt;
            Motion.FrozenAt = 0.3;
            var held = w.BestMomentForTest();
            Motion.FrozenAt = null;
            var still = w.BestMomentForTest();
            Motion.FrozenAt = frozenWas;
            Check(held.Tag && held.Opacity > 0.3 && held.Opacity < 1 && held.RingScale > 1.05 && held.GoneUnder && !still.Tag,
                $"motion: a new best grows its tag in and pops the ring once ({held.Tag} {held.Opacity:0.00} ring {held.RingScale:0.000}); still, no tag ({still.Tag}); back under, the tag goes ({held.GoneUnder})");
        }

        // a rare drop is a moment (2026-11-11, Ui/MainWindow.RareMoment.cs): the drops of one tick are one moment, the best grade with
        // "+2 more", and none plays in a demo; held halfway the window's says what dropped on a layer that takes no room; at its end it is gone
        {
            var (lead, more, played) = w.RareTickForTest("Thick Balaur Horn ×1", "Noble Dragon Lord Greatsword ×1", "Wisdom Greatsword ×1");
            Check(lead == "Noble Dragon Lord Greatsword" && more == "2" && !played, $"motion: three rare drops in one tick are one moment, the Epic's with +2 more, and none plays in a demo ({lead}, +{more}, {played})");
            var frozenWas = Motion.FrozenAt;
            Motion.FrozenAt = 0.5;
            var words = w.RareMomentForTest("Noble Dragon Lord Greatsword", 2);
            Check(words?.StartsWith("RARE DROP · EPIC +2 more | ") == true && w.RareMomentLayerIsFlat, $"motion: the window's moment held halfway, over the page and taking no room in it ({words})");
            Motion.FrozenAt = 1;
            Check(w.RareMomentForTest("Noble Dragon Lord Greatsword", 0) == null, "motion: at its end the window's moment is gone, the page as it was");
            Motion.FrozenAt = frozenWas;
        }
        w.ForceClose();
        // motion (Ui/Motion.cs, 2026-11-11), each effect held at a point of its course (KL_MOTION_AT's way) so the frame can be read
        {
            var frozenWas = Motion.FrozenAt;
            string N0(double v) => Math.Round(v).ToString("N0");
            var fig = new System.Windows.Controls.TextBlock(); fig.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "Ink");
            var ink = (System.Windows.Media.SolidColorBrush)Application.Current.FindResource("Ink");
            Motion.FrozenAt = 0.5;
            Motion.Number(fig, "test|fig", 1000, N0);
            string first = fig.Text;
            Motion.Number(fig, "test|fig", 2000, N0);
            var mid = (fig.Foreground as System.Windows.Media.SolidColorBrush)?.Color;
            Check(first == "1,000" && fig.Text == "2,000" && mid != ink.Color && mid?.G > ink.Color.G - 40, $"motion: a figure's first value shows still, a rise rolls there and tints green ({first} then {fig.Text}, {mid})");
            Motion.FrozenAt = 0.2;
            Motion.Number(fig, "test|fig", 3000, N0);
            Check(fig.Text == "2,784", $"motion: a roll a fifth of the way in, eased (2,000 to 3,000 reads {fig.Text})");
            Motion.FrozenAt = 1;
            Motion.Number(fig, "test|fig", 4000, N0);
            Check(fig.Text == "4,000" && fig.Foreground == ink && fig.ReadLocalValue(System.Windows.Controls.TextBlock.ForegroundProperty).GetType().Name.Contains("Resource"),
                $"motion: at its end the figure reads its value in its own theme colour again ({fig.Text}, {fig.ReadLocalValue(System.Windows.Controls.TextBlock.ForegroundProperty).GetType().Name})");
            Motion.FrozenAt = 0.5;
            var kills = new System.Windows.Controls.TextBlock(); kills.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "Ink");
            Motion.Number(kills, "test|kills", 5, N0, -1); Motion.Number(kills, "test|kills", 6, N0, -1);
            Motion.Number(fig, "test|fig", 5000, N0, roll: false);
            string clock = fig.Text;
            Motion.Blank(fig, "test|fig", " "); Motion.Number(fig, "test|fig", 100, N0);
            Check(kills.Foreground == ink && clock == "5,000" && fig.Text == "100", $"motion: no tint for a count of kills, no roll for a figure the clock moved, none after a blank ({kills.Foreground == ink} {clock} {fig.Text})");

            FrameworkElement El() => new System.Windows.Controls.Border { Width = 40, Height = 40 };
            static bool Moved(FrameworkElement e) => e.RenderTransform is System.Windows.Media.ScaleTransform;
            Motion.Begin("test|host"); var a0 = El(); Motion.Slot(a0, "test|host", "A", 1); Motion.End("test|host");
            Motion.Begin("test|host"); var a1 = El(); var b1 = El();
            Motion.Slot(a1, "test|host", "A", 1); Motion.Slot(b1, "test|host", "B", 1, "Legend"); Motion.End("test|host");
            Check(!Moved(a0) && !Moved(a1) && Moved(b1) && b1.Effect is System.Windows.Media.Effects.DropShadowEffect { Opacity: > 0.3 },
                $"motion: a host's first draw lands nothing; then a new Legend slot grows in and glows, the one already there stays still ({Moved(a0)} {Moved(a1)} {Moved(b1)} {b1.Effect?.GetType().Name} {(b1.Effect as System.Windows.Media.Effects.DropShadowEffect)?.Opacity:0.00})");
            Motion.FrozenAt = 0.1;
            Motion.Begin("test|host"); var a2 = El(); var b2 = El();
            Motion.Slot(a2, "test|host", "A", 2); Motion.Slot(b2, "test|host", "B", 1, "Legend"); Motion.End("test|host");
            double sx = (a2.RenderTransform as System.Windows.Media.ScaleTransform)?.ScaleX ?? 1;
            Check(sx > 1.04 && sx < 1.08 && !Moved(b2) && a2.Effect == null && a2.Opacity == 1, $"motion: more of a slot swells and settles, without a glow (scale {sx:0.000} a tenth in)");
            Motion.Begin("test|host"); var news = new[] { "A", "B", "C", "D", "E", "F" }.Select(k => { var e = El(); Motion.Slot(e, "test|host", k, 2); return e; }).ToList(); Motion.End("test|host");
            Motion.Begin("test|host"); var gone = new[] { "A", "X" }.Select(k => { var e = El(); Motion.Slot(e, "test|host", k, 2); return e; }).ToList(); Motion.End("test|host");
            Check(!news.Any(Moved) && !gone.Any(Moved), "motion: four new slots at once, or a slot gone, is another view: nothing lands");

            Motion.FrozenAt = 0.5;
            var chev = new System.Windows.Shapes.Path();
            Motion.Turn(chev, 0, 90);
            double angle = (chev.RenderTransform as System.Windows.Media.RotateTransform)?.Angle ?? -1;
            bool folded = false; Motion.Close(new System.Windows.Controls.Border(), () => folded = true);
            Check(angle > 70 && angle < 90 && folded, $"motion: a chevron halfway turned reads {angle:0}°, eased; a fold of something not shown hides it at once");

            // 0.10.44: bars slide, a goal reached washes its row once, the chart draws in
            double shown = -1; var bar = new System.Windows.Controls.Border();
            Motion.Slide(bar, "test|bar", 0.2, s => shown = s);
            double firstBar = shown;
            Motion.Slide(bar, "test|bar", 0.6, s => shown = s);
            Check(firstBar == 0.2 && shown > 0.5 && shown < 0.6, $"motion: a bar's first share shows still, a new one slides there, eased ({firstBar:0.00} then {shown:0.000} halfway)");
            Motion.Slide(bar, "test|bar", 0.603, s => shown = s);
            var bar2 = new System.Windows.Controls.Border(); double shown2 = -1;
            Motion.Slide(bar2, "test|bar", 0.9, s => shown2 = s);
            Check(shown2 > 0.6 && shown2 < 0.9, $"motion: a bar drawn again under its key slides from the share last shown ({shown2:0.000})");
            Motion.ForgetShare("test|bar"); var bar3 = new System.Windows.Controls.Border(); double shown3 = -1;
            Motion.Slide(bar3, "test|bar", 0.3, s => shown3 = s);
            Check(shown3 == 0.3, $"motion: a bar that went away comes back without a slide ({shown3:0.000})");

            System.Windows.Controls.Border Row() => new() { Background = System.Windows.Media.Brushes.Transparent };
            var r0 = Row(); Motion.Reached(r0, "test|goal", true);
            var r1 = Row(); Motion.Reached(r1, "test|goal2", false);
            var r2 = Row(); Motion.Reached(r2, "test|goal2", true);
            var r3 = Row(); Motion.Reached(r3, "test|goal2", true);
            static bool Washed(System.Windows.Controls.Border b) => b.Background is System.Windows.Media.SolidColorBrush { Color.A: > 0x10 } && b.RenderTransform is System.Windows.Media.ScaleTransform;
            Check(!Washed(r0) && !Washed(r1) && Washed(r2) && !Washed(r3), $"motion: a goal washes once, on the draw where it is reached; one first seen done stays still ({Washed(r0)} {Washed(r1)} {Washed(r2)} {Washed(r3)})");

            var chart = new System.Windows.Controls.Canvas { Width = 600, Height = 90 };
            Motion.Reveal(chart, 600, 90);
            double clipW = (chart.Clip as System.Windows.Media.RectangleGeometry)?.Rect.Width ?? -1;
            Check(clipW > 400 && clipW < 632, $"motion: the chart halfway drawn in shows {clipW:0} px of its width, eased");
            // the rate chart, live (Ui/RateChart.cs): a new minute runs the line out from where it ended and pops the head dot; a
            // pickup eases the newest minute, the clock's nudge is set at once; past an hour the oldest minute folds away; the mouse reads a minute
            {
                const double CW = 628, CH = 96;
                var t0 = new DateTime(2026, 11, 11, 20, 0, 0, DateTimeKind.Utc);
                Session.RateLine Rates(int n, double last = 0, int from = 0)
                {
                    var l = new Session.RateLine();
                    for (int i = from; i < from + n; i++) l.Add(t0.AddMinutes(i), i == from + n - 1 && last > 0 ? last : 500_000 + 10_000 * i);
                    return l;
                }
                (RateChart Chart, System.Windows.Shapes.Polyline Line, System.Windows.Shapes.Path Fill, System.Windows.Controls.Canvas Box) Fresh()
                {
                    var cv = new System.Windows.Controls.Canvas { Width = CW, Height = CH };
                    var fill = new System.Windows.Shapes.Path(); var line = new System.Windows.Shapes.Polyline(); var dot = new System.Windows.Shapes.Ellipse { Width = 10, Height = 10 };
                    var bestLine = new System.Windows.Shapes.Line(); var bestLbl = new System.Windows.Controls.TextBlock();
                    foreach (UIElement el in new UIElement[] { fill, line, dot, bestLine, bestLbl }) cv.Children.Add(el);
                    return (new RateChart(cv, fill, line, dot, bestLine, bestLbl), line, fill, cv);
                }
                static bool At(Point p, (double X, double Y) q) => Math.Abs(p.X - q.X) < 0.01 && Math.Abs(p.Y - q.Y) < 0.01;
                bool Settled(System.Windows.Shapes.Polyline line, Session.RateLine l) { var t = l.Points(CW, CH); return line.Points.Count == t.Count && t.Select((q, i) => At(line.Points[i], q)).All(x => x); }
                var three = Rates(3); var four = Rates(4, 505_000);
                var lastWas = three.Points(CW, CH)[^1];

                Motion.FrozenAt = 0;
                var land0 = Fresh(); land0.Chart.Draw(three, 600_000, live: true); land0.Chart.Draw(four, 600_000, live: true);
                Check(land0.Line.Points.Count == 4 && At(land0.Line.Points[3], lastWas) && At(land0.Line.Points[2], lastWas) && land0.Chart.Head.Ring,
                    $"motion: chart: a new minute starts where the line ended, its segment not yet out ({land0.Line.Points[^1]}, was {lastWas})");
                Motion.FrozenAt = 0.3;
                var land3 = Fresh(); land3.Chart.Draw(three, 600_000, live: true); land3.Chart.Draw(four, 600_000, live: true);
                double y3 = land3.Line.Points[^1].Y, yTo = four.Points(CW, CH)[^1].Y;
                Check(Math.Abs(land3.Chart.Head.Scale - 1.6) < 0.01 && land3.Chart.Head.Ring && y3 > Math.Min(lastWas.Y, yTo) + 1 && y3 < Math.Max(lastWas.Y, yTo) - 1,
                    $"motion: chart: three tenths in the head dot is at its swell ({land3.Chart.Head.Scale:0.00}), its ring out, the line on its way ({lastWas.Y:0.0} → {y3:0.0} → {yTo:0.0})");
                Motion.FrozenAt = 1;
                var land1 = Fresh(); land1.Chart.Draw(three, 600_000, live: true); land1.Chart.Draw(four, 600_000, live: true);
                Check(Settled(land1.Line, four) && land1.Chart.Head == (1.0, false), $"motion: chart: at its end the line's last point is the newest minute's ({land1.Line.Points[^1]}), the dot at rest");
                var gold = ((System.Windows.Media.SolidColorBrush)Application.Current.FindResource("Gold")).Color;
                var stop = (land1.Fill.Fill as System.Windows.Media.LinearGradientBrush)?.GradientStops[0].Color;
                land1.Box.Resources["Bg"] = System.Windows.Media.Brushes.White; land1.Box.Resources["Gold"] = System.Windows.Media.Brushes.Red;
                var switched = (land1.Fill.Fill as System.Windows.Media.LinearGradientBrush)?.GradientStops[0].Color;
                Check(stop is { } sc && sc.R == gold.R && sc.G == gold.G && sc.B == gold.B && sc.A > 0 && switched == System.Windows.Media.Color.FromArgb(0x4C, 0xFF, 0, 0),
                    $"motion: chart: the fill under the line is the look's gold ({stop}, gold {gold}), repainted at once in a new look's, softer on a light one ({switched})");
                land1.Chart.HoverAt(2);
                string readout = t0.AddMinutes(2).ToLocalTime().ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture) + " · " + Amounts.Short(520_000) + " /h";
                Check(land1.Chart.Crosshair.Shown && land1.Chart.Readout == readout && Math.Abs(land1.Chart.Crosshair.X - land1.Line.Points[2].X) < 0.01,
                    $"motion: chart: the mouse on the third minute reads its time and rate (\"{land1.Chart.Readout}\") at its point ({land1.Chart.Crosshair.X:0.0})");

                Motion.FrozenAt = 0.5;
                var cn = Fresh(); cn.Chart.Draw(four, 600_000, live: true);
                var nudged = Rates(4, 505_000 * 1.0001); cn.Chart.Draw(nudged, 600_000, live: true);
                bool nudgeSet = Settled(cn.Line, nudged);
                var picked = Rates(4, 515_000); cn.Chart.Draw(picked, 600_000, live: true);
                double yWas = nudged.Points(CW, CH)[^1].Y, yPick = picked.Points(CW, CH)[^1].Y, yHalf = cn.Line.Points[^1].Y;
                Check(nudgeSet && yHalf < yWas - 1 && yHalf > yPick + 1 && !cn.Chart.Head.Ring,
                    $"motion: chart: the clock's nudge is set at once ({nudgeSet}); a pickup eases the newest minute, halfway {yHalf:0.0} between {yWas:0.0} and {yPick:0.0}, without a pop");
                var cr = Fresh(); var hour = Rates(60); cr.Chart.Draw(hour, 0, live: true);
                var rolled = Rates(60, from: 1); cr.Chart.Draw(rolled, 0, live: true);
                Check(cr.Line.Points.Count == 61 && cr.Line.Points[0].X == 0 && cr.Line.Points[1].X > 0 && cr.Line.Points[1].X < CW / 59,
                    $"motion: chart: past an hour the oldest minute folds onto the first while the line slides left ({cr.Line.Points.Count} points, the second at {cr.Line.Points[1].X:0.0})");
                var cc = Fresh(); cc.Chart.Draw(three, 600_000, live: false); cc.Chart.Draw(four, 600_000, live: false);
                int closedPts = cc.Line.Points.Count; cc.Chart.Open();
                var co = Fresh(); co.Chart.Draw(three, 600_000, live: false); co.Chart.Draw(four, 600_000, live: true);
                Check(closedPts == 0 && Settled(cc.Line, four) && Settled(co.Line, four) && !co.Chart.Head.Ring, $"motion: chart: closed it draws nothing ({closedPts} points); opened, or the first draw after, the line is set as it is");
                bool stillWas = Motion.Still;
                Motion.FrozenAt = null; Motion.Still = true;
                var cs = Fresh(); cs.Chart.Draw(three, 600_000, live: true); cs.Chart.Draw(four, 600_000, live: true);
                cs.Chart.HoverAt(1);
                Check(Settled(cs.Line, four) && cs.Chart.Head == (1.0, false) && cs.Chart.Crosshair.Shown, "motion: chart: with motion off a new minute is drawn settled at once, the crosshair up at once");
                Motion.Still = stillWas;
            }
            // a new best here and today's milestones (Session/Milestones.cs, Ui/MainWindow.Bests.cs): said only on a crossing seen live, once
            {
                var ms = new Session.Milestones();
                const string d1 = "2026-11-11", d2 = "2026-11-12";
                Check(Session.Milestones.MarkAt(999_999) == -1 && Session.Milestones.MarkAt(1_000_000) == 0 && Session.Milestones.MarkAt(200_000_000) == Session.Milestones.DayMarks.Length - 1,
                    "motion: today's Kinah marks start at 1M and the last holds past 100M");
                var marks = new[] { ms.Day(d1, "c1", 1_200_000, false), ms.Day(d1, "c1", 1_300_000, false), ms.Day(d1, "c1", 2_600_000, false), ms.Day(d1, "c1", 2_700_000, false) };
                Check(marks[0] == null && marks[1] == null && marks[2] == 2_500_000 && marks[3] == null,
                    $"motion: today's Kinah already past 1M on the first look (the tracker starting) says nothing; passing 2.5M says it once ({string.Join(", ", marks.Select(x => x?.ToString() ?? "-"))})");
                var again = new[] { ms.Day(d1, "c2", 3_000_000, false), ms.Day(d1, "c1", 2_800_000, false), ms.Day(d1, "c1", 2_400_000, false), ms.Day(d1, "c1", 2_600_000, false) };
                Check(again.All(x => x == null), "motion: another character's first look says nothing, and a mark passed once isn't said again that day (Kinah taken off and picked up again)");
                var loaded = ms.Day(d1, "c1", 6_000_000, true); var jump = ms.Day(d1, "c1", 26_000_000, false);
                var next = new[] { ms.Day(d2, "c1", 900_000, false), ms.Day(d2, "c1", 1_000_000, false) };
                Check(loaded == null && jump == 25_000_000 && next[0] == null && next[1] == 1_000_000,
                    $"motion: a day loaded again is noted, not said; several marks at once say the highest; a new day starts over ({loaded} {jump} {next[0]} {next[1]})");
                // midnight: the clock's new day can be read a moment before the reader starts its count, with yesterday's total
                const string d3 = "2026-11-13";
                var midnight = new[] { ms.Day(d3, "c1", 6_000_000, false), ms.Day(d3, "c1", 0, true), ms.Day(d3, "c1", 1_000_000, false) };
                Check(midnight[0] == null && midnight[1] == null && midnight[2] == 1_000_000,
                    $"motion: yesterday's 6M seen under the new day, then the new day's own count from 0: its 1M is still said ({string.Join(", ", midnight.Select(x => x?.ToString() ?? "-"))})");
                bool firstLook = ms.Best(d1, "c1|here|1", true, true, false);
                var crossing = new[] { ms.Best(d1, "c1|here|2", true, false, false), ms.Best(d1, "c1|here|2", true, true, true), ms.Best(d1, "c1|here|2", true, false, false),
                    ms.Best(d1, "c1|here|2", true, true, false), ms.Best(d1, "c1|here|2", true, true, false), ms.Best(d1, "c1|here|2", true, false, false), ms.Best(d1, "c1|here|2", true, true, false) };
                Check(!firstLook && crossing.Count(x => x) == 1 && crossing[3],
                    $"motion: a best already passed on a count's first look says nothing; passing it by a pickup says it once (not when prices moved it, not again after a dip) ({string.Join("", crossing.Select(x => x ? "1" : "0"))})");
                bool warm = ms.Best(d1, "c1|here|3", false, false, false) | ms.Best(d1, "c1|here|3", true, true, false);
                bool newDay = ms.Best(d2, "c1|here|2", true, false, false) | ms.Best(d2, "c1|here|2", true, true, false);
                Check(!warm && newDay, "motion: an hour past its best when it stops warming up says nothing; on a new day the same count can say a best again");
                // the hour against a 1,050,000 best over one hour: 1M Kinah and 10 Spiritstone, sold at 5,000 then 7,000 after a price refresh
                {
                    const double keeps = 0.88, best = 1_050_000;
                    var piecesWas = new Dictionary<long, int> { [1] = 10 };
                    double hourWas = 1_000_000 + 10 * 5_000 * keeps;
                    // the refresh and an ordinary pickup (500 Kinah, one Spiritstone) in the same tick: the prices did it
                    double picked = Session.Milestones.Picked(1_000_000, 1_000_500, piecesWas, new[] { (1L, "Spiritstone", 11) }, _ => 7_000 * keeps);
                    double hourNow = 1_000_500 + 11 * 7_000 * keeps;
                    bool refresh = ms.Best(d1, "c1|here|4", true, hourWas >= best, false) | ms.Best(d1, "c1|here|4", true, hourNow >= best, hourNow - picked >= best);
                    // no refresh, two Spiritstone and 500 Kinah: play did it
                    double picked2 = Session.Milestones.Picked(1_000_000, 1_000_500, piecesWas, new[] { (1L, "Spiritstone", 12) }, _ => 5_000 * keeps);
                    double hourNow2 = 1_000_500 + 12 * 5_000 * keeps;
                    bool play = ms.Best(d1, "c1|here|5", true, hourWas >= best, false) | ms.Best(d1, "c1|here|5", true, hourNow2 >= best, hourNow2 - picked2 >= best);
                    Check(!refresh && play && Math.Abs(picked - (500 + 7_000 * keeps)) < 1e-6,
                        $"motion: a price refresh and a pickup in one tick don't say a best the prices made ({hourNow:N0} without {picked:N0} picked); two pieces that pass it do ({hourNow2:N0})");
                }
                var lit = new System.Windows.Shapes.Rectangle { Visibility = Visibility.Collapsed };
                var figure = new System.Windows.Controls.TextBlock { Text = "1.27M" };
                Motion.FrozenAt = 0.5;
                Motion.LightSweep(lit, figure, "Gold");
                double bandX = ((lit.Fill as System.Windows.Media.LinearGradientBrush)?.RelativeTransform as System.Windows.Media.TranslateTransform)?.X ?? double.NaN;
                bool lightHalf = lit.Visibility == Visibility.Visible && lit.OpacityMask is System.Windows.Media.VisualBrush && bandX > 0.5 && bandX < 0.6;
                Motion.FrozenAt = 1;
                Motion.LightSweep(lit, figure, "Gold");
                Check(lightHalf && lit.Visibility == Visibility.Collapsed && lit.Fill == null && lit.OpacityMask == null,
                    $"motion: the light halfway has crossed most of the figure, eased (band at {bandX:0.000}), masked by its glyphs; at its end it is gone");
            }
            // the overlay's Ticker (0.10.49): its figures read back as numbers only when the overlay writes them the same way,
            // and the hour rolls on a pickup (held halfway: between the old and the new figure)
            Check(HudTicker.Num("1.27M") == 1_270_000 && HudTicker.Num("894K") == 894_000 && HudTicker.Num("12,345") == 12_345 && HudTicker.Num("warming up") == null && HudTicker.Num("1.2345M") == null,
                "motion: the overlay's figures read back as numbers (1.27M, 894K, 12,345), and nothing else does");
            {
                var sNow = DateTime.UtcNow;
                IEnumerable<DateTime> Ago(int n, double s) => Enumerable.Repeat(sNow.AddSeconds(-s), n);
                double s12 = MainWindow.Streak(Ago(12, 2), sNow), s6 = MainWindow.Streak(Ago(6, 10), sNow), sAging = MainWindow.Streak(Ago(12, 32.5), sNow), sGone = MainWindow.Streak(Ago(30, 46), sNow), sNone = MainWindow.Streak(Array.Empty<DateTime>(), sNow);
                Check(s12 == 1 && s6 == 0.5 && Math.Abs(sAging - 0.5) < 1e-9 && sGone == 0 && sNone == 0 && MainWindow.Streak(Ago(40, 1), sNow) == 1,
                    $"streak: twelve pickups in the last 20 s fill the bar, six half; aging past 20 s they count less and less, gone at 45 s ({s12} {s6} {sAging:0.00} {sGone} {sNone})");
            }
            {
                var tk = new HudTicker();
                var v1 = new Dictionary<string, string> { ["run"] = "1", ["time"] = "RUN 42:17", ["rate"] = "1M", ["total"] = "800K" };
                // a pickup just made (Ui/HudFreshPicks.cs): only that rolls the figures
                var v2 = new Dictionary<string, string>(v1) { ["rate"] = "1.27M", ["total"] = "894K", ["pick0"] = "Thick hide", ["pick0Id"] = "fresh", ["pick0At"] = DateTime.UtcNow.Ticks.ToString() };
                // held a fifth in: the hour's roll (its tint takes twice as long, so the figure moves in the first half) and the session's
                Motion.FrozenAt = 0.2;
                tk.Update(v1); tk.Update(v2);
                static IEnumerable<System.Windows.Controls.TextBlock> Texts(object o) =>
                    (o is System.Windows.Controls.TextBlock t ? new[] { t } : Enumerable.Empty<System.Windows.Controls.TextBlock>())
                    .Concat(o is DependencyObject d ? LogicalTreeHelper.GetChildren(d).Cast<object>().SelectMany(Texts) : Enumerable.Empty<System.Windows.Controls.TextBlock>());
                // rolled, not flipped (Steven, 2026-11-12, of 0.10.57's flip: "blinks/flashes instead of rolling"): a fifth in, the hour
                // tinted (its tint takes twice as long, so the figure moves in the first half) and the session part way, eased
                var tkRate = Texts(tk.Root).FirstOrDefault(t => t.Text.EndsWith("M")); var tkTotal = Texts(tk.Root).FirstOrDefault(t => t.Text.EndsWith("K"));
                Check(tkRate?.Text == "1.21M" && tkTotal?.Text == "845.9K" && (tkRate.Foreground as System.Windows.Media.SolidColorBrush)?.Color != HudKit.Rgb(HudKit.GoldC),
                    $"motion: the overlay's hour and session roll on a pickup, held a fifth in, eased, the hour tinted ({tkRate?.Text} {tkTotal?.Text})");
                // the hot streak bar (Ui/HudStreak.cs) slides to its new share on Pop, and takes no room: the look measures the same with it
                var tkBare = new HudTicker(); tkBare.Update(v1); tkBare.Update(v2);
                var sv1 = new Dictionary<string, string>(v1) { ["streak"] = "0.25" }; var sv2 = new Dictionary<string, string>(v2) { ["streak"] = "0.75", ["pick0Id"] = "fresh2" };
                var tkBar = new HudTicker(); tkBar.Update(sv1); tkBar.Update(sv2);
                static Size Measured(FrameworkElement r) { r.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity)); return r.DesiredSize; }
                var tkSh = tkBar.StreakForTest;
                Check(tkSh.Shown is > 0.4 and < 0.6 && tkSh.Root.Visibility == Visibility.Visible && tkSh.Root.Height == StreakBar.Thick && Measured(tkBar.Root) == Measured(tkBare.Root) && tkBare.StreakForTest.Root.Visibility == Visibility.Collapsed,
                    $"motion: Pop's hot streak bar fills from 25% to 75%, held a fifth in, eased ({tkSh.Shown:0.000}); it adds nothing to the look's size ({Measured(tkBar.Root)} and {Measured(tkBare.Root)} without one)");
                // Buddy (the Card look, 0.10.52): its figures roll the same way, and its ring sweeps from 60% of your best to 90%
                var bd = new HudCard();
                var bv1 = new Dictionary<string, string>(v1) { ["chase"] = "0.6" };
                var bv2 = new Dictionary<string, string>(v2) { ["chase"] = "0.9" };
                bd.Update(bv1); bd.Update(bv2);
                var bdShown = Texts(bd.Root).SelectMany(t => t.Inlines.Count > 0 ? t.Inlines.OfType<System.Windows.Documents.Run>().Select(r => r.Text) : new[] { t.Text }).ToList();
                // Buddy's figures are runs (the first inline of their lines): a run's own start takes text position 0, so its characters count from 1
                var (bdRate, bdTotal, _) = bd.FiguresForTest;
                Check(bdRate?.Text == "1.21M" && bdTotal?.Text == "845.9K" && bdShown.Contains("75"),
                    $"motion: Buddy's hour and session roll and its ring sweeps (60 to 90), held a fifth in ({string.Join(" | ", bdShown.Where(t => t.Length is > 0 and < 24))})");
                var bdBar = new HudCard(); bdBar.Update(sv1); bdBar.Update(sv2);
                var bdBare = new HudCard(); bdBare.Update(v1); bdBare.Update(v2);
                var bdSh = bdBar.FiguresForTest.Streak;
                Check(bdSh.Shown is > 0.4 and < 0.6 && bdSh.Root.Visibility == Visibility.Visible && Measured(bdBar.Root) == Measured(bdBare.Root),
                    $"motion: Buddy's hot streak bar under its figures fills from 25% to 75% ({bdSh.Shown:0.000}) and adds nothing to the card's size ({Measured(bdBar.Root)}, {Measured(bdBare.Root)})");
                // Badge (the Strip look, 0.10.55) moves like them: on a pickup its hour, feed rate and session roll and its ring sweeps
                // (60 to 90), the Legend pickup's worth pops and the live dot lights blue, the edge lights gold, and the changed notice
                // cross-fades (a fifth in, the old one is still fading out); a first draw and a still drawing move nothing
                var bgNow = DateTime.UtcNow;
                var bgV1 = new Dictionary<string, string>(v1)
                {
                    ["chase"] = "0.6", ["toBest"] = "560K to your best here", ["feedText"] = "Thick hide", ["feedValue"] = "+1.1K", ["feedRate"] = "1M", ["feedUnit"] = "an hour",
                    ["pick0"] = "Thick hide", ["pick0Id"] = "a", ["pick0At"] = bgNow.AddSeconds(-4).Ticks.ToString(), ["notice"] = "Update ready",
                };
                var bgV2 = new Dictionary<string, string>(bgV1)
                {
                    ["rate"] = "1.27M", ["total"] = "894K", ["chase"] = "0.9", ["toBest"] = "130K to your best here", ["feedText"] = "Shade Gloves", ["feedValue"] = "+28K", ["feedRate"] = "1.27M",
                    ["pick0"] = "Shade Gloves", ["pick0Id"] = "b", ["pick0Grade"] = "Legend", ["pick0At"] = bgNow.Ticks.ToString(), ["notice"] = "Price alert: Shade Gloves under 30K",
                };
                var bgGoneV = new Dictionary<string, string>(bgV2) { ["notice"] = "" };
                string[] bgFields = { "rate", "total", "feed" };
                static string BgWords(System.Windows.Controls.TextBlock t) => string.Concat(t.Inlines.OfType<System.Windows.Documents.Run>().Select(r => r.Text));
                List<System.Windows.Controls.TextBlock> BgTexts(HudWindow h) => BadgeTree((DependencyObject)h.Content).OfType<System.Windows.Controls.TextBlock>().ToList();
                static System.Windows.Controls.Border BgFrame(HudWindow h) => (System.Windows.Controls.Border)((System.Windows.Controls.Grid)h.OwnContent!).Children[0];
                var badge = new HudWindow(bgFields);
                badge.Update(bgV1); badge.Update(bgV2);
                var bgShown = BgTexts(badge).Select(BgWords).ToList();
                var bgWorth = BgTexts(badge).FirstOrDefault(t => BgWords(t) == "+28K");
                var bgEdge = (BgFrame(badge).BorderBrush as System.Windows.Media.SolidColorBrush)?.Color;
                var bgDot = BadgeTree((DependencyObject)badge.Content).OfType<System.Windows.Shapes.Ellipse>().FirstOrDefault(e => e.Width == 6);
                var bgOld = BgTexts(badge).FirstOrDefault(t => BgWords(t) == "Update ready");
                var bgRolls = BgTexts(badge).Where(t => t.Text is "1.21M" or "845.9K").ToList();
                Check(bgRolls.Count(t => t.Text == "1.21M") == 2 && bgRolls.Any(t => t.Text == "845.9K") && bgShown.Contains("75")
                      && bgWorth?.RenderTransform is System.Windows.Media.ScaleTransform { ScaleX: > 1.03 } && bgEdge?.R > 0x50
                      && (bgDot?.Fill as System.Windows.Media.SolidColorBrush)?.Color == HudKit.GradeColor("Legend")
                      && bgOld?.Parent is FrameworkElement { Opacity: > 0 and < 0.5 } && !bgShown.Contains(bgV2["notice"]),
                    $"motion: Badge's hour, feed rate and session roll, its ring sweeps, a Legend pickup pops in blue, the edge lights softly and the notice cross-fades, a fifth in ({string.Join(" | ", bgRolls.Select(t => t.Text))}; edge {bgEdge}, dot {(bgDot?.Fill as System.Windows.Media.SolidColorBrush)?.Color})");
                var bgBar = new HudWindow(bgFields); bgBar.Update(new Dictionary<string, string>(bgV1) { ["streak"] = "0.25" }); bgBar.Update(new Dictionary<string, string>(bgV2) { ["streak"] = "0.75" });
                var bgSh = bgBar.StreakForTest;
                var bgBarSize = Measured((FrameworkElement)bgBar.Content);
                bgBar.Update(new Dictionary<string, string>(bgV2) { ["streak"] = "0", ["pick0Id"] = "c", ["pick0At"] = DateTime.UtcNow.Ticks.ToString() });
                Motion.FrozenAt = 1;
                bgBar.Update(new Dictionary<string, string>(bgV2) { ["streak"] = "0", ["pick0Id"] = "d", ["pick0At"] = DateTime.UtcNow.Ticks.ToString() });
                bool bgDrained = bgSh.Shown == 0 && bgSh.Root.Visibility == Visibility.Collapsed;
                Motion.FrozenAt = 0.2;
                var bgBare = new HudWindow(bgFields); bgBare.Update(bgV1); bgBare.Update(bgV2);
                var bgBareSize = Measured((FrameworkElement)bgBare.Content);
                Check(bgDrained && bgSh.Root.Height == StreakBar.Thick && Math.Abs(bgBarSize.Height - bgBareSize.Height) < 0.01,
                    $"motion: Badge's hot streak bar drains to nothing and hides at 0 ({bgSh.Shown:0.000}, {bgSh.Root.Visibility}); it adds no height ({bgBarSize.Height:0.0} and {bgBareSize.Height:0.0} without)");
                bgBar.Close(); bgBare.Close();
                var bgFresh = new HudWindow(bgFields); bgFresh.Update(bgV2);
                var bgStill = new HudWindow(bgFields, still: true); bgStill.Update(bgV1); bgStill.Update(bgV2);
                bool BgCalm(HudWindow h) => BgTexts(h).Select(BgWords).Count(t => t == "1.27M") == 2 && BgTexts(h).Any(t => BgWords(t) == "90") && BgTexts(h).Any(t => BgWords(t) == bgV2["notice"])
                    && BgTexts(h).All(t => t.RenderTransform is not System.Windows.Media.ScaleTransform && t.Parent is FrameworkElement { Opacity: 1 } && !Motion.Playing(t));
                Check(BgCalm(bgFresh) && BgCalm(bgStill), $"motion: Badge's first draw and a still drawing of it show their figures, ring and notice as they are ({BgCalm(bgFresh)} {BgCalm(bgStill)})");
                // a rare drop: the frame beats in its colour (down between beats a fifth in), the spark pops, the line comes in
                var bgBlue = HudKit.GradeColor("Legend");
                bgFresh.Alert("Rare drop: Shade Gloves ×1", bgBlue);
                var bgBeat = (BgFrame(bgFresh).BorderBrush as System.Windows.Media.SolidColorBrush)?.Color;
                var bgSpark = BadgeTree((DependencyObject)bgFresh.Content).OfType<System.Windows.Controls.Image>().FirstOrDefault(i => i.RenderTransform is System.Windows.Media.ScaleTransform);
                var bgAlert = BgTexts(bgFresh).FirstOrDefault(t => BgWords(t).StartsWith("Rare drop"));
                Check(bgBeat?.A < 0xA0 && bgBeat?.B == bgBlue.B && bgSpark?.RenderTransform is System.Windows.Media.ScaleTransform { ScaleX: > 1.001 and < 1.15 } && bgAlert?.Parent is FrameworkElement { Opacity: > 0.3 and < 0.8 },
                    $"motion: Badge's rare drop beats its frame in the drop's colour, pops its spark and fades its line in ({bgBeat}, spark {bgSpark != null}, line {(bgAlert?.Parent as FrameworkElement)?.Opacity:0.00})");
                // the sparks (Motion.Sparks, 2026-11-12): a dozen in the drop's colour flying out from the sign, part way out and
                // fading a fifth in, none past a quarter of the sign beyond its edge; in its row, taking no room
                static (int Dots, double Far, bool Fading) SparksOf(System.Windows.Controls.Canvas? c)
                {
                    if (c == null) return (0, 0, false);
                    var dots = c.Children.OfType<System.Windows.Shapes.Ellipse>().ToList();
                    double far = dots.Max(e => e.RenderTransform is System.Windows.Media.TranslateTransform t ? Math.Sqrt(t.X * t.X + t.Y * t.Y) + e.Width / 2 : 0);
                    return (dots.Count, far, dots.All(e => e.Opacity is > 0.2 and < 1) && dots.All(e => e.Fill is System.Windows.Media.SolidColorBrush));
                }
                var bgBurst = BadgeTree((DependencyObject)bgFresh.Content).OfType<System.Windows.Controls.Canvas>().FirstOrDefault(c => c.Children.Count == Motion.SparkCount);
                var (bgDots, bgFar, bgFading) = SparksOf(bgBurst);
                Check(bgDots == Motion.SparkCount && bgFading && bgFar > 4 && bgFar <= 14 / 2.0 + 3.5 + 0.01 && bgBurst?.Margin.Right == -bgBurst?.Width,
                    $"motion: Badge's rare drop bursts {bgDots} sparks from its sign, fading, out to {bgFar:0.0} px from its centre (at most 10.5)");
                // a notice that goes fades out before it hides: halfway it still reads, faint; at its end it is gone
                Motion.FrozenAt = 0.5;
                var bgGoing = new HudWindow(bgFields); bgGoing.Update(bgV2); bgGoing.Update(bgGoneV);
                var bgGoingNote = BgTexts(bgGoing).FirstOrDefault(t => BgWords(t) == bgV2["notice"]);
                Motion.FrozenAt = 1;
                var bgGone = new HudWindow(bgFields); bgGone.Update(bgV2); bgGone.Update(bgGoneV);
                bool bgHidden = !BgTexts(bgGone).Any(t => BgWords(t) == bgV2["notice"] && t.Visibility == Visibility.Visible);
                Check(bgGoingNote is { Visibility: Visibility.Visible, Parent: FrameworkElement { Opacity: > 0 and < 0.5 } } && bgHidden,
                    $"motion: Badge's notice fades out before it hides ({(bgGoingNote?.Parent as FrameworkElement)?.Opacity:0.00}, then hidden {bgHidden})");
                // no pickup, nothing moves: a price refresh revaluing the session (the same newest pickup, the figures up) and another
                // character's session coming in (the pickups cleared, then its own from minutes ago) show their figures as they are,
                // with no roll, no lit edge and no sweep, on Badge, Pop and Buddy alike
                Motion.FrozenAt = 0.2;
                var bgLine = HudKit.Rgb(0x232736);
                var bgPrice = new Dictionary<string, string>(bgV2) { ["rate"] = "1.4M", ["total"] = "990K", ["feedRate"] = "1.4M", ["chase"] = "0.95" };
                var bgCleared = new Dictionary<string, string>(bgV2) { ["rate"] = "", ["total"] = "0", ["feedRate"] = "", ["chase"] = "", ["toBest"] = "", ["pick0"] = "", ["pick0Id"] = "", ["pick0At"] = "" };
                var bgOther = new Dictionary<string, string>(bgV2) { ["rate"] = "1.9M", ["total"] = "2.1M", ["feedRate"] = "1.9M", ["chase"] = "0.8", ["pick0Id"] = "other", ["pick0At"] = bgNow.AddMinutes(-5).Ticks.ToString() };
                var bgQuiet = new HudWindow(bgFields);
                bgQuiet.Update(bgV2);
                bgQuiet.Update(bgPrice);
                var quietPrice = BgTexts(bgQuiet).Select(BgWords).ToList();
                bool priceStill = quietPrice.Count(t => t == "1.4M") == 2 && quietPrice.Contains("990K") && quietPrice.Contains("95") && (BgFrame(bgQuiet).BorderBrush as System.Windows.Media.SolidColorBrush)?.Color == bgLine;
                bgQuiet.Update(bgCleared); bgQuiet.Update(bgOther);
                var quietOther = BgTexts(bgQuiet).Select(BgWords).ToList();
                bool otherStill = quietOther.Count(t => t == "1.9M") == 2 && quietOther.Contains("2.1M") && quietOther.Contains("80") && (BgFrame(bgQuiet).BorderBrush as System.Windows.Media.SolidColorBrush)?.Color == bgLine;
                var tkQuiet = new HudTicker(); tkQuiet.Update(v2); tkQuiet.Update(new Dictionary<string, string>(v2) { ["rate"] = "1.4M", ["total"] = "990K" });
                var tkQuietShown = Texts(tkQuiet.Root).Select(t => t.Text).ToList();
                var bdQuiet = new HudCard(); bdQuiet.Update(new Dictionary<string, string>(v2) { ["chase"] = "0.6" }); bdQuiet.Update(new Dictionary<string, string>(v2) { ["rate"] = "1.4M", ["total"] = "990K", ["chase"] = "0.9" });
                var bdQuietShown = Texts(bdQuiet.Root).SelectMany(t => t.Inlines.Count > 0 ? t.Inlines.OfType<System.Windows.Documents.Run>().Select(r => r.Text) : new[] { t.Text }).ToList();
                bool popStill = tkQuietShown.Contains("1.4M") && tkQuietShown.Contains("990K") && Texts(tkQuiet.Root).All(t => !Motion.Playing(t)),
                    buddyStill = bdQuietShown.Contains("1.4M") && bdQuietShown.Contains("990K") && bdQuietShown.Contains("90") && bdQuiet.FiguresForTest is { Rate: { } qr, Total: { } qt } && qr.Text == "1.4M" && qt.Text == "990K";
                Check(priceStill && otherStill && popStill && buddyStill,
                    $"motion: with no pickup just made (a price refresh, another session) nothing rolls, lights or sweeps (Badge price {priceStill}, other session {otherStill}; Pop {popStill}; Buddy {buddyStill})");
                foreach (var h in new[] { badge, bgFresh, bgStill, bgGoing, bgGone, bgQuiet }) h.Close();
                // a rare drop's card makes its entrance (2026-11-11, Ui/HudRare.Motion.cs), held a fifth in: the tile growing in, the
                // name's light on it, the price rolling up; not on a look's first draw; "+2 more" follows the drops that join it
                static double TileScale((FrameworkElement Tile, string Label, string Price, bool Lit)? p) => (p?.Tile.RenderTransform as System.Windows.Media.ScaleTransform)?.ScaleX ?? 1;
                var drop = new Dictionary<string, string>(v2) { ["rare"] = "Shade Gloves", ["rareId"] = "1", ["rareCount"] = "×1", ["rareGrade"] = "Legend", ["rareColor"] = "#5AA6FF", ["rarePrice"] = "28,000", ["rareWhere"] = "on Siel" };
                var heldFirst = new HudCard(); heldFirst.Update(drop);
                var h0 = HudRare.PartsForTest(heldFirst.RareDropForTest);
                var card = new HudCard(); card.Update(v2); card.Update(drop);
                var cp = HudRare.PartsForTest(card.RareDropForTest);
                Check(h0 is { Lit: false, Price: "28,000" } && TileScale(h0) == 1 && cp is { Lit: true } && TileScale(cp) is > 0.6 and < 1 && HudTicker.Num(cp.Value.Price) is > 0 and < 28_000,
                    $"motion: Buddy's rare drop makes its entrance, a fifth in (tile at {TileScale(cp):0.00}, price {cp?.Price}); one held on the look's first draw stands still");
                // its sparks: under the tile, part way out and fading, none past the ring's reach (a quarter of the 40 px tile beyond its edge)
                var cardBurst = HudRare.SparksForTest(card.RareDropForTest);
                var (cdDots, cdFar, cdFading) = SparksOf(cardBurst);
                bool underTile = cardBurst?.Parent is System.Windows.Controls.Grid cg && cp is { } cpv && cg.Children.IndexOf(cardBurst) < cg.Children.IndexOf(cpv.Tile);
                Check(cdDots == Motion.SparkCount && cdFading && cdFar > 10 && cdFar <= 40 / 2.0 + 10 + 0.01 && underTile && HudRare.SparksForTest(heldFirst.RareDropForTest) == null,
                    $"motion: Buddy's rare drop bursts {cdDots} sparks in its colour from under the tile, out to {cdFar:0.0} px from its centre (at most 30), fading; none on the look's first draw");
                card.Update(new Dictionary<string, string>(drop) { ["craft"] = "1" }); card.Update(drop);
                Check(HudRare.PartsForTest(card.RareDropForTest) is { } back && TileScale(back) == 1, "motion: a drop hidden while crafting and shown again in its hold makes no second entrance");
                var tick = new HudTicker(); tick.Update(v2); tick.Update(drop);
                var tp = HudRare.PartsForTest(tick.RareToastForTest);
                Check(tp is { Lit: true } && TileScale(tp) < 1 && SparksOf(HudRare.SparksForTest(tick.RareToastForTest)).Dots == Motion.SparkCount, $"motion: Pop's rare toast makes the same entrance, sparks and all (tile at {TileScale(tp):0.00})");
                Motion.FrozenAt = 1;
                var ended = new HudCard(); ended.Update(v2); ended.Update(drop);
                var ep = HudRare.PartsForTest(ended.RareDropForTest);
                Check(ep is { Lit: false, Price: "28,000" } && TileScale(ep) == 1 && HudRare.SparksForTest(ended.RareDropForTest) == null, "motion: at its end the rare drop reads as the card draws it still, its sparks gone");
                var joined = new Dictionary<string, string>(drop) { ["rareMore"] = "2" };
                var made = ended.RareDropForTest;
                ended.Update(joined); tick.Update(joined);
                Check(ended.RareDropForTest == made && HudRare.PartsForTest(made)?.Label == "RARE DROP · LEGEND  +2 more" && HudRare.PartsForTest(tick.RareToastForTest)?.Label == "RARE DROP · LEGEND  +2 more",
                    $"motion: drops joining the one held say so on Buddy and Pop, the card left as it is ({HudRare.PartsForTest(made)?.Label})");
            }
            // 2026-11-11 (Ui/Motion.Sequence.cs): a staged opening, each effect in its turn on one course, held at a point of it
            var seen = new List<string>();
            var cues = new Motion.Cues().Add(0, 100, p => seen.Add($"a{p:0.##}")).Add(100, 100, p => seen.Add($"b{p:0.##}"));
            Motion.FrozenAt = 0.25;
            Motion.Sequence(new System.Windows.Controls.Border(), cues);
            Check(string.Join(" ", seen) == "a0 b0 a0.5", $"motion: a sequence sets every effect to its start, then each moves only in its turn ({string.Join(" ", seen)})");
            var own = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Gold);
            var cu = new System.Windows.Controls.TextBlock { Foreground = own };
            var count = Motion.CountUp(cu, 1000, N0, System.Windows.Media.Colors.Lime);
            count(0.5); string half = cu.Text; bool tinted = !ReferenceEquals(cu.Foreground, own);
            count(1);
            Check(half == "875" && tinted && cu.Text == "1,000" && ReferenceEquals(cu.Foreground, own), $"motion: a figure counts up from nothing, eased and tinted, and ends in its own brush ({half} then {cu.Text})");
            var seqSlot = El();
            var landing = Motion.Landing(seqSlot, "Legend", System.Windows.Media.Colors.Red);
            landing(0); double waiting = seqSlot.Opacity;
            landing(0.1);
            double ls = (seqSlot.RenderTransform as System.Windows.Media.ScaleTransform)?.ScaleX ?? 1;
            Check(waiting == 0 && ls is > 0.9 and < 1 && seqSlot.Effect is System.Windows.Media.Effects.DropShadowEffect { Color: var lc } && lc == System.Windows.Media.Colors.Red,
                $"motion: a slot in a sequence waits unseen, then lands and glows in the colour it was given (scale {ls:0.000})");
            bool left = false; _ = Motion.Leave(new System.Windows.Controls.Border(), () => left = true);
            var tag = new System.Windows.Controls.Border(); var glint = Motion.Glint(tag, System.Windows.Media.Colors.Gold, System.Windows.Media.Colors.White); glint(0.5);
            Check(left && (tag.Background as System.Windows.Media.LinearGradientBrush)?.RelativeTransform is System.Windows.Media.TranslateTransform { X: 0 },
                "motion: a card leaving in a held render goes at once; a glint halfway is in the middle of its box");
            // the recap's line against your average here (Session/RunStanding.cs): the hour over earlier stays, only with three of them
            var stays = new List<(long, double)> { (300_000, 1800), (500_000, 3600), (90_000, 300) };
            var thin = KinahLedger.Session.RunStanding.HourAverage(stays);
            stays.Add((400_000, 1800));
            var avg = KinahLedger.Session.RunStanding.HourAverage(stays);
            Check(thin == null && avg == 600_000, $"recap: the average hour here weighs each stay by its time, leaves out one under ten minutes, and waits for three ({thin?.ToString() ?? "none"} then {avg})");
            Check(KinahLedger.Session.RunStanding.Line(KinahLedger.Session.RunStanding.Share(708_000, 600_000)) == ("+18%", " vs your average here", 1) && KinahLedger.Session.RunStanding.Line(-0.07) == ("\u22127%", " vs your average here", -1)
                  && KinahLedger.Session.RunStanding.Line(0.02) == ("", "About your average here", 0) && KinahLedger.Session.RunStanding.Share(5, 0) == 0,
                "recap: +18% over the average, \u22127% under it, within 3% about your average, and no average is no share");
            Motion.Still = true; Motion.FrozenAt = null;
            var still = new System.Windows.Controls.TextBlock();
            Motion.Number(still, "test|still", 1, N0); Motion.Number(still, "test|still", 2, N0);
            Check(!Motion.On && still.Text == "2" && !Motion.Playing(still), "motion: none in a render (Still): a figure just shows its value");
            // Week wrapped's entrance (Ui/Motion.Wrapped.cs on Motion.Sequence): one course over the sheet, each part at its own time. Held a fifth in,
            // the total is counting, the first part is in and the last still to come; halfway, the drop lands glowing; at the end
            // every part is settled and the glow gone; with no motion the sheet shows settled at once
            {
                var sheet = new System.Windows.Controls.Border();
                var total = new System.Windows.Controls.TextBlock();
                System.Windows.Controls.Border head = new(), last = new(), drop = new();
                Motion.Cues Cues() => new Motion.Cues().Add(0, 1000, Motion.CountUp(total, 1_000_000, N0)).Add(0, 300, Motion.Arrive(head, 0, 12))
                    .Add(1000, 800, Motion.GlowLand(drop, "Gold")).Add(2000, 300, Motion.Stamp(last));
                Motion.FrozenAt = 0.2; Motion.Sequence(sheet, Cues());
                string counting = total.Text; double headIn = head.Opacity, lastIn = last.Opacity;
                Motion.FrozenAt = 0.5; Motion.Sequence(sheet, Cues());
                double glow = (drop.Effect as System.Windows.Media.Effects.DropShadowEffect)?.Opacity ?? -1;
                Motion.FrozenAt = 1; Motion.Sequence(sheet, Cues());
                bool settled = total.Text == "1,000,000" && last.Opacity == 1 && drop.Effect == null && drop.RenderTransform is System.Windows.Media.ScaleTransform { ScaleX: 1 };
                Motion.FrozenAt = null; Motion.Sequence(sheet, Cues());
                bool still2 = total.Text == "1,000,000" && head.Opacity == 1 && last.Opacity == 1 && !Motion.Playing(sheet);
                Check(counting != "0" && counting != "1,000,000" && headIn == 1 && lastIn == 0 && glow > 0.3 && settled && still2,
                    $"motion: Week wrapped's sequence held a fifth in counts ({counting}), first in, last to come ({headIn} {lastIn}); the drop glows halfway ({glow:0.00}); settled at the end and at once with none ({settled} {still2})");
            }
            Motion.FrozenAt = frozenWas;
        }
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

    /// <summary>
    /// ui-shot --recap: the run-end recap card in each of its sample states (ui-recap-<state>.png), on the window's page
    /// colour with room round it for its rise, held at KL_MOTION_AT's point of its opening when that is set (else settled).
    /// </summary>
    public static void ShotRecap(Settings settings, string dir, string? skin)
    {
        Ensure();
        Directory.CreateDirectory(dir);
        var w = new MainWindow(settings, watch: false) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -12000, Top = -12000, ShowActivated = false };
        w.Show();
        if (skin != null) Skins.Apply(skin);
        w.ShowDemo("Open world");
        var samples = w.RecapSamples();
        IconCache.Drain(TimeSpan.FromSeconds(8));   // the drops' icons, when this PC is online
        DoEvents();
        foreach (var (state, card, open) in samples)
        {
            var page = new System.Windows.Controls.Border { Child = card, Padding = new Thickness(16) };
            page.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "Bg");
            page.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity)); page.Arrange(new Rect(page.DesiredSize)); page.UpdateLayout();
            open();
            DoEvents();
            Save(page, Path.Combine(dir, "ui-recap-" + state + (skin != null ? "-" + skin : "") + ".png"), 0);
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
        if (demo && Motion.FrozenAt != null) { w.MotionFrameForRender(); DoEvents(); }   // KL_MOTION_AT: the effects held at that point
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
            // KL_SWITCHER=1 (UX pass 2026-11-13): the same window with the header's character switcher open, "<page>-<state>-switcher.png"
            if (demo && Environment.GetEnvironmentVariable("KL_SWITCHER") == "1") w.SaveWithSwitcher(Path.Combine(dir, Path.GetFileNameWithoutExtension(file) + "-switcher.png"));
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
        var name = welcome ? "ui-welcome" : !demo ? (mine && kind == "Events" ? "ui-main-events-mine" : "ui-main") : kind switch { "Gathering" => "ui-main-gathering", "Abyss" => "ui-main-abyss", "Pets" => "ui-main-pets", "Market" => "ui-main-market", "Crafting" => "ui-main-crafting", "CraftingEmpty" => "ui-main-crafting-empty", "CraftingUpgrades" => "ui-main-crafting-upgrades", "Events" => "ui-main-events", "Quests" => "ui-main-quests", _ => "ui-main-tracking" };
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
        if (demo && kind == "Open world") w.ShotCharacterSwitcher(dir);   // the header's character switcher (Ui/MainWindow.Characters.cs)
        if (demo && Environment.GetEnvironmentVariable("KL_SWITCHER") == "1") w.SaveWithSwitcher(Path.Combine(dir, name + "-switcher.png"));
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
            if (Motion.FrozenAt != null) { h.MotionFrameForRender(hudState); DoEvents(); }   // KL_MOTION_AT: Badge's motion held at that point
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
            if (Motion.FrozenAt != null) { h.MotionFrameForRender(values); DoEvents(); }
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
            if (Motion.FrozenAt != null) { h.MotionFrameForRender(v); DoEvents(); }   // KL_MOTION_AT: the overlay's motion held at that point
            // over the mock's dark page (#0B0C0F), so the toasts' see-through washes read as they do over the game
            Save((FrameworkElement)h.Content, Path.Combine(dir, file), 0, Color.FromRgb(0x0B, 0x0C, 0x0F));
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
            ("ui-hud-stopped.png", HudFields.Parse(null), new() { ["run"] = "", ["flash"] = "Run stopped · 1.27M this run" }),
            // past your best here: the ring full in mint with its star, the chase line mint
            ("ui-hud-best.png", HudFields.Parse(null), new() { ["chase"] = "1.000", ["toBest"] = "New best here" }),
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
            if (Motion.FrozenAt != null) { h.MotionFrameForRender(v); DoEvents(); }
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
