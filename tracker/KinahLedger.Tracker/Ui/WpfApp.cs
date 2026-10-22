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
        w.ShowDemo();
        var startSkin = Skins.Current;
        // a known start: the window and tray choices off (0.7.6 turns two of them on for new installs)
        w.Prefs.CloseToTray = w.Prefs.MinimizeToTray = w.Prefs.HideWhenGameCloses = false;
        OptionsWindow Open() { var o = new OptionsWindow(w, OptionsWindow.Page.General) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -12000, Top = -12000, ShowActivated = false }; o.Show(); DoEvents(); return o; }

        var o = Open();
        Check(!o.SaveBtn.IsEnabled, "Save is off until something changes");
        Flip(o.HideOnCloseChk);
        Check(o.SaveBtn.IsEnabled, "a change turns Save on");
        Flip(o.HideOnCloseChk);
        Check(!o.SaveBtn.IsEnabled, "changing it back turns Save off again");

        Flip(o.CloseToTrayChk); Flip(o.MinToTrayChk); Flip(o.HideOnCloseChk);
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
        Check(!w.Prefs.CloseToTray && w.Prefs.Share, "nothing applies before Save");
        Click(o.SaveBtn);
        DoEvents();
        Check(!o.IsVisible, "Save closes the window");
        var p = w.Prefs;
        Check(p.CloseToTray && p.MinimizeToTray && p.HideWhenGameCloses, "window and tray choices saved");
        Check(p.OnGameStart == "tray", "when Aion 2 starts: stay in the tray");
        Check(!p.Share && !w.AppSettings.Upload.Enabled, "sharing off reaches the uploader");
        Check(w.RegionKey == eu.Key && w.Server?.Label == euServer.Label, "server saved: " + w.Server?.Label);
        Check(p.Hotkey == "" && w.HotkeyText == "", "shortcut turned off");
        Check(p.Skin == other && Skins.Current == other, "theme saved");

        o = Open();
        Check(o.CloseToTrayChk.IsChecked == true && o.GameStartBox.SelectedIndex == 1 && o.ServerPick.SelectedItem is Data.GameServer gs && gs.Label == euServer.Label, "Options opens with the saved choices");
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

        o = Open();
        Click(o.HotkeyResetBtn);
        Click(o.SaveBtn);
        DoEvents();
        Check(p.Hotkey == Platform.Hotkey.Default, "Default puts Ctrl+Shift+K back");

        // Compact and Full view (0.7.6): the tables and pickups only in Full, which doubles the width; Crafting is one column either way
        w.ShowCompact(); DoEvents();
        double compactW = w.ActualWidth;
        Check(w.TablesCard.Visibility == Visibility.Visible && w.SummaryCard.Visibility == Visibility.Visible && w.PickupStrip.Visibility != Visibility.Visible && w.TopDropsBox.Visibility != Visibility.Visible && w.Rows.Items.Count <= 3,
            "Compact: run card, slim total, tables at three rows, no pickups");
        w.ShowFull(); DoEvents();
        Check(w.TablesCard.Visibility == Visibility.Visible && w.PickupStrip.Visibility == Visibility.Visible && w.TopDropsBox.Visibility == Visibility.Visible && w.Rows.Items.Count > 3 && w.ActualWidth > compactW * 1.8,
            $"Full view: pickups, top drops, more rows, {compactW:0} -> {w.ActualWidth:0} px wide");
        w.ShowCompact(); DoEvents();
        Check(Math.Abs(w.ActualWidth - compactW) < 2, "back to Compact: the width it had");

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
            o.Show();
            DoEvents();
            Save((FrameworkElement)o.Content, Path.Combine(dir, $"options-{page.ToString().ToLowerInvariant()}{(skin != null ? "-" + skin : "")}.png"), 0);
            o.Close();
        }
        w.Close();
    }

    public static void Shot(Settings settings, string dir, bool demo, string kind = "Open world", bool compact = false, bool welcome = false, string? skin = null, bool full = false)
    {
        Ensure();
        Directory.CreateDirectory(dir);
        var w = new MainWindow(settings, watch: false) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -12000, Top = -12000, ShowActivated = false };
        w.Show();
        if (skin != null) Skins.Apply(skin);
        // KL_SCREEN_H=1040: render as a screen of that height would show it (the window never grows past the work area)
        if (double.TryParse(Environment.GetEnvironmentVariable("KL_SCREEN_H"), out var screenH) && screenH > 300) w.MaxHeight = screenH;
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
        var name = welcome ? "ui-welcome" : !demo ? "ui-main" : kind switch { "Gathering" => "ui-main-gathering", "Pets" => "ui-main-pets", "Market" => "ui-main-market", "Crafting" => "ui-main-crafting", _ => "ui-main-tracking" };
        Save((FrameworkElement)w.Content, Path.Combine(dir, name + (compact ? "-compact" : full ? "-full" : "") + (skin != null ? "-" + skin : "") + ".png"), (compact ? 640 : 800) - 16);
        // KL_CARD_ZONE=<zone>: also draw the card a click on the first drop opens, with the live site's data for that zone
        if (demo && Environment.GetEnvironmentVariable("KL_CARD_ZONE") is { Length: > 0 } cardZone)
        {
            using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            Task.Run(() => KinahLedger.Data.SiteData.RefreshAsync(http, "https://kinahledger.com", "na_e-siel-elyos")).Wait();
            w.ShotItemCard(Path.Combine(dir, "ui-card.png"), cardZone);
        }
        // the Discord card for the same count, and the panel over the game with the window's own figures (every field, then the default set)
        if (demo && kind != "Crafting") w.SaveShareCard(Path.Combine(dir, "ui-card-discord.png"));
        var values = w.HudValues.ToDictionary(kv => kv.Key, kv => kv.Value);
        w.Close();

        foreach (var (file, fields) in new[] { ("ui-hud.png", HudFields.Parse(null)), ("ui-hud-all.png", HudFields.All.Select(f => f.Key).ToList()), ("ui-hud-alert.png", HudFields.Parse(null)) })
        {
            var h = new HudWindow(fields) { Left = -12000, Top = -12000, ShowActivated = false };
            h.Show();
            h.Update(values);
            if (file == "ui-hud-alert.png") h.Alert("Rare drop: Shade Gloves ×1", System.Windows.Media.Color.FromRgb(0x5A, 0xA6, 0xFF));
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

    static void Save(FrameworkElement root, string path, double fallbackWidth)
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
        rtb.Render(root);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = File.Create(path);
        enc.Save(fs);
    }
}
