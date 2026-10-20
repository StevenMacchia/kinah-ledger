using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using KinahLedger.Data;
using KinahLedger.Ocr;
using KinahLedger.Session;
using DRect = System.Drawing.Rectangle;
using WF = System.Windows.Forms;

namespace KinahLedger.Ui;

public sealed record DropRow(string Name, string Drops, string DropPct, string PerHour);

public partial class MainWindow : Window
{
    static readonly string[] Kinds = { "Open world", "Dungeon", "Expedition", "Raid", "Gathering", "Event", "Other" };
    const string RepoUrl = "https://github.com/StevenMacchia/kinah-ledger";
    const int HotkeyId = 1;

    readonly Settings _settings;
    readonly UserPrefs _prefs = UserPrefs.Load();
    readonly ItemDictionary _items;
    TrackerSession? _session;
    HudWindow? _hud;
    DRect? _region = RegionStore.Load();
    bool _quitting, _balloonShown, _demo;

    readonly WF.NotifyIcon _tray = new();
    readonly WF.ToolStripMenuItem _trayToggle = new("Start tracking");
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(500) };

    Brush B(string key) => (Brush)FindResource(key);

    public MainWindow(Settings settings)
    {
        InitializeComponent();
        _settings = settings;
        _items = ItemDictionary.LoadOrEmpty(settings.ItemsFile);
        Icon = Native.AppIconSource();

        Kind.ItemsSource = Kinds;
        Kind.SelectedItem = Kinds.Contains(_prefs.Activity) ? _prefs.Activity : "Open world";
        Place.Text = _prefs.Place;
        HudChk.IsChecked = _prefs.Hud;
        ShareChk.IsChecked = _prefs.Share;
        if (_prefs.WinX is int x && _prefs.WinY is int y && x > SystemParameters.VirtualScreenLeft - 50 && y > SystemParameters.VirtualScreenTop - 50
            && x < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 200 && y < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 200)
        { WindowStartupLocation = WindowStartupLocation.Manual; Left = x; Top = y; }

        PickBtn.Click += (_, _) => Pick();
        TestBtn.Click += async (_, _) => await TestRead();
        StartBtn.Click += async (_, _) => await Toggle();
        SiteLink.Click += (_, _) => Open(RepoUrl);
        SentLink.Click += (_, _) => WhatsSent();
        ResetLink.Click += (_, _) => { _region = null; UpdateStep2(); Preview.Source = null; PreviewHint.Visibility = Visibility.Visible; Say("Chat area cleared. Press Pick area to choose it again.", "Faint"); };
        LogsLink.Click += (_, _) => Open(CrashLog.Dir);

        BuildTray();
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
        SourceInitialized += OnSourceInitialized;
        StateChanged += OnStateChanged;
        Closing += OnClosingWindow;

        UpdateStep2();
        ShowTable(null);
        Say("Not tracking. Do steps 1 and 2, then press Start.", "Faint");
    }

    /* ---------------- actions ---------------- */

    string ActivityName() { var p = Place.Text.Trim(); var k = Kind.SelectedItem as string ?? "Open world"; return p.Length > 0 ? $"{k} · {p}" : k; }

    async Task Toggle() { if (_session == null) await StartTracking(); else await StopTracking(); }

    void Pick()
    {
        Hide();
        try { var r = RegionPickerWindow.Pick(); if (r != null) { _region = r; RegionStore.Save(r.Value); Preview.Source = null; PreviewHint.Visibility = Visibility.Visible; } }
        finally { Show(); Activate(); UpdateStep2(); }
        if (_region != null) Say("Chat area set. Press Test read to check it, then Start.", "Good");
    }

    async Task TestRead()
    {
        if (_region == null) { Pick(); if (_region == null) return; }
        TestBtn.IsEnabled = false; Say("Reading…", "Faint");
        try
        {
            var reader = new ScreenReader(_region.Value, _settings.Ocr.Scale);
            var parser = new ChatParser(_items);
            var lines = await Task.Run(() => reader.ReadAsync());
            SetPreview(reader.Thumb);
            var engine = new SessionEngine(_items);
            int hits = 0;
            foreach (var l in lines) foreach (var ev in parser.Parse(l, DateTime.UtcNow)) { engine.Apply(ev); hits++; }
            TableTitle.Text = "TEST READ · WHAT'S ON SCREEN NOW";
            ShowTable(engine, TimeSpan.FromHours(1));
            Summary.Text = $"{lines.Count} lines read · {hits} loot or kill lines";
            if (lines.Count == 0) Say("Nothing recognized. Use borderless or windowed mode and make sure the box covers the chat text.", "Warn");
            else if (hits == 0) Say($"{lines.Count} line(s) read but none look like loot. Loot something first, or try a bigger chat font or a tighter box.", "Warn");
            else Say($"Reads fine: {hits} loot or kill line(s) out of {lines.Count}. Ready to start.", "Good");
        }
        catch (Exception ex) { Say(ex.Message, "Bad"); }
        finally { TestBtn.IsEnabled = true; }
    }

    async Task StartTracking()
    {
        if (_region == null) { Pick(); if (_region == null) return; }
        SavePrefs();
        try
        {
            _settings.Upload.Enabled = ShareChk.IsChecked == true;
            _session = new TrackerSession(_region.Value, _settings, _items, ActivityName());
            _session.Start();
            if (HudChk.IsChecked == true) { _hud = new HudWindow { Owner = this }; _hud.Show(); }
            TableTitle.Text = "DROPS THIS SESSION";
            ShowTable(_session.Engine);
            SetTrackingUi(true);
            Say($"Tracking {ActivityName()}. Minimize this window; the panel and Ctrl+Shift+K keep working.", "Faint");
        }
        catch (Exception ex) { _session = null; Say(ex.Message, "Bad"); }
        await Task.CompletedTask;
    }

    async Task StopTracking()
    {
        var s = _session; if (s == null) return;
        StartBtn.IsEnabled = false; Say("Stopping…", "Faint");
        await s.StopAsync();
        CloseHud();
        // closing the panel hands focus to whatever is behind it, usually the game, which then covers this window
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Show(); Activate(); Topmost = true; Topmost = false;
        FillNumbers(s.Engine, s.Engine.Elapsed);
        string msg, tone = "Faint"; bool shared = false;
        if (!s.Eligible) msg = "Stopped. Under 5 minutes or no loot seen, so this run isn't shared.";
        else if (ShareChk.IsChecked != true) msg = "Stopped. Sharing is off, so this run stays on this PC.";
        else if (string.IsNullOrWhiteSpace(_settings.Upload.Endpoint)) msg = "Stopped. Sharing isn't live yet, so this run stays on this PC.";
        else if (await s.UploadAsync(_settings)) { msg = "Stopped. Run shared anonymously."; tone = "Good"; shared = true; }
        else { msg = "Stopped. Couldn't reach the site; run kept on this PC."; tone = "Warn"; }
        if (s.Engine.Events > 0) { SessionStore.Append(s.Engine, _items, s.Activity, _settings, shared); msg += $" Saved to your history ({SessionStore.Count()} runs)."; }
        _session = null;
        TableTitle.Text = "LAST RUN · " + s.Activity.ToUpperInvariant();
        SetTrackingUi(false);
        Say(msg, tone);
    }

    void SetTrackingUi(bool on)
    {
        StartBtn.Content = on ? "Stop" : "Start tracking";
        StartBtn.IsEnabled = true;
        StartBtn.Background = B(on ? "BtnStop" : "BtnPrimary");
        StartBtn.BorderBrush = on ? new SolidColorBrush(Color.FromRgb(0xD9, 0x5E, 0x5E)) : new SolidColorBrush(Color.FromRgb(0x4C, 0x9B, 0xEF));
        _trayToggle.Text = on ? "Stop tracking" : "Start tracking";
        PickBtn.IsEnabled = !on; TestBtn.IsEnabled = !on; Kind.IsEnabled = !on; Place.IsEnabled = !on; HudChk.IsEnabled = !on; ShareChk.IsEnabled = !on;
        _tray.Text = on ? "Kinah Ledger · tracking" : "Kinah Ledger";
    }

    void Tick()
    {
        var s = _session; if (s == null) return;
        FillNumbers(s.Engine, s.Engine.Elapsed);
        if (s.LastError.Length > 0) Say("Read problem: " + s.LastError, "Bad");
        else Say($"Tracking {s.Activity} · {s.Reads} reads" + (s.Unaligned > 0 ? $" · {s.Unaligned} skipped" : "") + (s.LastLine.Length > 0 ? " · last: " + s.LastLine : ""), "Faint");
        if (s.Reads % 2 == 0) SetPreview(s.Thumb);
        if (_hud != null)
        {
            var e = s.Engine; double h = e.Hours; var t = e.Totals(); long kinah = t.Kinah + t.KinahBound;
            _hud.Update($"{Fk(kinah / h)} Kinah/hr",
                $"{Fk(t.Kills / h)} kills/hr · {Fk(t.Drops / h)} drops/hr · {(t.Kills > 0 ? Fk(kinah / (double)t.Kills) : "—")} Kinah/kill\n{e.Elapsed:hh\\:mm\\:ss} · {Fk(kinah)} Kinah · {t.Drops} drops · {t.Kills} kills",
                s.LastLine);
        }
    }

    void FillNumbers(SessionEngine e, TimeSpan elapsed)
    {
        double h = Math.Max(elapsed.TotalHours, 1.0 / 3600);
        var t = e.Totals(); long kinah = t.Kinah + t.KinahBound;
        KinahHr.Text = Fk(kinah / h);
        KinahKill.Text = t.Kills > 0 ? Fk(kinah / (double)t.Kills) : "—";
        KillsHr.Text = Fk(t.Kills / h);
        DropsHr.Text = Fk(t.Drops / h);
        Summary.Text = $"{elapsed:hh\\:mm\\:ss} · {Fk(kinah)} Kinah · {t.Drops} drops · {t.Kills} kills";
        ShowTable(e, elapsed);
    }

    void ShowTable(SessionEngine? e, TimeSpan? elapsed = null)
    {
        var rows = new List<DropRow>();
        int kills = 0;
        if (e != null)
        {
            double h = Math.Max((elapsed ?? e.Elapsed).TotalHours, 1.0 / 3600); kills = e.Totals().Kills;
            foreach (var r in e.ItemRows())
            {
                int total = r.Count + r.Bound;
                rows.Add(new DropRow(r.Name, total.ToString("N0"), kills > 0 ? Pct(total / (double)kills) : "—", Rate(total / h)));
            }
        }
        Rows.ItemsSource = rows;
        TableEmpty.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        TableEmpty.Text = e == null ? "Drops show here as they're read, with the drop rate per kill and per hour." : "No drops yet.";
        TableNote.Visibility = rows.Count > 0 && kills == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    static string Pct(double p) => p >= 1 ? p.ToString("0.0") + "×" : p >= .1 ? (p * 100).ToString("0") + "%" : p >= .01 ? (p * 100).ToString("0.0") + "%" : (p * 100).ToString("0.00") + "%";
    static string Rate(double v) => v >= 100 ? v.ToString("0") : v >= 10 ? v.ToString("0.0") : v.ToString("0.00");
    static string Fk(double v) => Math.Abs(v) >= 1e6 ? $"{v / 1e6:0.00}M" : Math.Abs(v) >= 1e4 ? $"{v / 1e3:0}K" : Math.Abs(v) >= 1e3 ? $"{v / 1e3:0.0}K" : v >= 100 ? $"{v:0}" : v >= 10 ? $"{v:0.0}" : $"{v:0.00}";

    void SetPreview(System.Drawing.Bitmap? bmp)
    {
        if (bmp == null) return;
        using (bmp) Preview.Source = Native.ToSource(bmp);
        PreviewHint.Visibility = Visibility.Collapsed;
    }
    void CloseHud() { _hud?.Close(); _hud = null; }
    void Say(string text, string tone) { Status.Text = text; Status.Foreground = B(tone); }
    void Restore() { Show(); WindowState = WindowState.Normal; Activate(); }
    static void Open(string url) { try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch (Exception) { } }

    void WhatsSent() => MessageBox.Show(this,
        "When a run ends, these totals go to the public tables:\n\n" +
        "•  what you're farming and where, gear score bracket, region\n•  run length in minutes and kill count\n•  Kinah picked up, tradeable and bound\n•  drop counts per item\n•  app version and a random install ID used only to stop spam\n\n" +
        "Never your character name, server, account, class, level, chat text or screenshots.\nSharing isn't live yet; until the site can receive runs, everything stays on this PC.",
        "What's shared", MessageBoxButton.OK, MessageBoxImage.Information);

    /* ---------------- tray, hotkey, window state ---------------- */

    void BuildTray()
    {
        var menu = new WF.ContextMenuStrip { BackColor = System.Drawing.Color.FromArgb(17, 26, 40), ForeColor = System.Drawing.Color.FromArgb(233, 239, 247), ShowImageMargin = false, Font = new System.Drawing.Font("Segoe UI", 9.75f) };
        var open = new WF.ToolStripMenuItem("Open Kinah Ledger"); open.Click += (_, _) => Restore();
        _trayToggle.Click += async (_, _) => await Toggle();
        var quit = new WF.ToolStripMenuItem("Quit"); quit.Click += (_, _) => { _quitting = true; Close(); };
        menu.Items.AddRange(new WF.ToolStripItem[] { open, _trayToggle, new WF.ToolStripSeparator(), quit });
        _tray.Icon = Native.AppIcon(); _tray.Text = "Kinah Ledger"; _tray.ContextMenuStrip = menu; _tray.Visible = true;
        _tray.DoubleClick += (_, _) => Restore();
    }

    void OnSourceInitialized(object? sender, EventArgs e)
    {
        var h = new WindowInteropHelper(this).Handle;
        Native.DarkTitleBar(h);
        HwndSource.FromHwnd(h)?.AddHook(WndProc);
        Native.RegisterHotKey(h, HotkeyId, Native.MOD_CONTROL | Native.MOD_SHIFT, (uint)KeyInterop.VirtualKeyFromKey(Key.K));
    }

    IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Native.WM_HOTKEY && wParam.ToInt32() == HotkeyId) { _ = Toggle(); handled = true; }
        return IntPtr.Zero;
    }

    void OnStateChanged(object? sender, EventArgs e)
    {
        // only a deliberate minimize while tracking goes to the tray; otherwise the window behaves like any other
        if (WindowState != WindowState.Minimized || _session == null) return;
        Hide();
        if (!_balloonShown) { _balloonShown = true; _tray.ShowBalloonTip(4000, "Kinah Ledger is still tracking", "It's in the tray by the clock. Double-click the icon to bring the window back.", WF.ToolTipIcon.Info); }
    }

    void OnClosingWindow(object? sender, CancelEventArgs e)
    {
        SavePrefs();
        if (_session != null && !_quitting)
        {
            var r = MessageBox.Show(this, "You're still tracking. Stop and quit?\n\nYes stops the run and closes Kinah Ledger.\nNo keeps tracking and leaves this window open.", "Kinah Ledger", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r == MessageBoxResult.No) { e.Cancel = true; return; }
            e.Cancel = true; _quitting = true;
            _ = StopTracking().ContinueWith(_ => Dispatcher.Invoke(Close));
            return;
        }
        var h = new WindowInteropHelper(this).Handle;
        if (h != IntPtr.Zero) Native.UnregisterHotKey(h, HotkeyId);
        CloseHud();
        _tray.Visible = false; _tray.Dispose();
    }

    void SavePrefs()
    {
        if (_demo) return;   // a layout render must not overwrite the player's saved choices
        _prefs.Activity = Kind.SelectedItem as string ?? "Open world"; _prefs.Place = Place.Text.Trim();
        _prefs.Hud = HudChk.IsChecked == true; _prefs.Share = ShareChk.IsChecked == true;
        if (WindowState == WindowState.Normal && !double.IsNaN(Left)) { _prefs.WinX = (int)Left; _prefs.WinY = (int)Top; }
        _prefs.Save();
    }

    void UpdateStep2()
    {
        if (_region is DRect r) { Step2Status.Text = $"Set: {r.Width} × {r.Height} px at {r.X}, {r.Y}. Press Test read if the chat moved."; Step2Status.Foreground = B("Ink"); }
        else { Step2Status.Text = "Not set. Press Pick area and drag a box around the chat lines that show loot and Kinah."; Step2Status.Foreground = B("Gold"); }
    }

    public void ForceClose() { _quitting = true; Close(); }

    /// <summary>Runs start → wait → stop exactly as the buttons would, for catching crashes without a desktop session.</summary>
    public async Task<string> DebugCycle(DRect region, int ms)
    {
        _demo = true;
        _region = region; UpdateStep2();
        await StartTracking();
        if (_session == null) throw new InvalidOperationException("start failed: " + Status.Text);
        await Task.Delay(ms);
        var reads = _session.Reads;
        await StopTracking();
        return $"{reads} reads, status: {Status.Text}";
    }

    /// <summary>Fills the window with a sample open-world session so its layout can be reviewed without the game.</summary>
    public void ShowDemo()
    {
        _demo = true;
        _region = new DRect(24, 980, 812, 140); UpdateStep2();
        Kind.SelectedItem = "Open world"; Place.Text = "Lakrum elites";
        var e = new SessionEngine(_items); var t = DateTime.UtcNow;
        for (int i = 0; i < 161; i++) e.Apply(new Protocol.KinahChanged(t, 3690 + (i % 7) * 40, false));
        void Drop(string n, int c, bool b = false) { for (int i = 0; i < c; i++) e.Apply(new Protocol.ItemAcquired(t, _items.IdOf(n), 1, b)); }
        Drop("Thick hide", 41); Drop("Essence", 26); Drop("Lesser essence", 19); Drop("Manastone", 8); Drop("Aether shard", 7); Drop("Radiant Odyle", 5, true); Drop("Enchantment stone", 3); Drop("Greater manastone", 1);
        TableTitle.Text = "DROPS THIS SESSION";
        FillNumbers(e, TimeSpan.FromMinutes(42) + TimeSpan.FromSeconds(17));
        using (var b = new System.Drawing.Bitmap(166, 76))
        using (var g = System.Drawing.Graphics.FromImage(b))
        {
            g.Clear(System.Drawing.Color.FromArgb(5, 9, 16));
            using var f = new System.Drawing.Font("Consolas", 7f);
            string[] lines = { "You defeated Lakrum Scout.", "Acquired 3,690 Kina.", "Acquired Thick hide x2.", "You defeated Lakrum Scout.", "Acquired 3,690 Kina." };
            for (int i = 0; i < lines.Length; i++) g.DrawString(lines[i], f, i % 2 == 1 ? System.Drawing.Brushes.Khaki : System.Drawing.Brushes.Gray, 4, 4 + i * 13);
            SetPreview((System.Drawing.Bitmap)b.Clone());
        }
        SetTrackingUi(true);
        Say("Tracking Open world · Lakrum elites · 3,620 reads · last: Acquired Thick hide ×2.", "Faint");
    }
}
