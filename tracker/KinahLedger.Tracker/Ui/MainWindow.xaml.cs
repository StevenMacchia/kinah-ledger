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

public sealed record DropRow(string Name, string Drops, string DropPct, string PerHour, bool Header = false, string Grade = "");

public partial class MainWindow : Window
{
    // dungeons and raids are out for now: a boss kill drops more lines at once than any chat window shows
    static readonly string[] Kinds = { "Open world", "Gathering", "Pets" };
    enum Mode { Open, Gather, Pets }
    const string RepoUrl = "https://github.com/StevenMacchia/kinah-ledger";
    const int HotkeyId = 1;

    readonly Settings _settings;
    readonly UserPrefs _prefs = UserPrefs.Load();
    readonly ItemDictionary _items;
    /// <summary>Runs from the moment the window opens: finds the game and reads the Market whenever it is open. Start tracking is only for farming runs.</summary>
    readonly GameWatcher _watcher;
    TrackerSession? _session;
    HudWindow? _hud;
    DRect? _region = RegionStore.Load();
    DRect? _marketRegion = RegionStore.Load(RegionStore.MarketFile);
    bool _quitting, _balloonShown, _demo, _compact, _watcherStopped;

    readonly WF.NotifyIcon _tray = new();
    readonly WF.ToolStripMenuItem _trayToggle = new("Start tracking");
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    bool _framing; int _ticks;

    Brush B(string key) => (Brush)FindResource(key);

    /// <param name="watch">False for layout renders, so no screen is read while the window exists off-screen.</param>
    public MainWindow(Settings settings, bool watch = true)
    {
        InitializeComponent();
        _settings = settings;
        _items = ItemDictionary.LoadOrEmpty(settings.ItemsFile);
        if (!_prefs.ManualMarket) _marketRegion = null;
        _watcher = new GameWatcher(settings, _items, _marketRegion, _prefs.Server);
        _watcher.Market.Enabled = settings.Market.Enabled && _prefs.ShareMarket;
        Icon = Native.AppIconSource();

        Kind.ItemsSource = Kinds;
        Kind.SelectedItem = Kinds.Contains(_prefs.Activity) ? _prefs.Activity : "Open world";
        HudChk.IsChecked = _prefs.Hud;
        ShareChk.IsChecked = _prefs.Share;
        if (_prefs.WinX is int x && _prefs.WinY is int y && x > SystemParameters.VirtualScreenLeft - 50 && y > SystemParameters.VirtualScreenTop - 50
            && x < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 200 && y < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 200)
        { WindowStartupLocation = WindowStartupLocation.Manual; Left = x; Top = y; }

        PickBtn.Click += (_, _) => Pick();
        DetectBtn.Click += async (_, _) => await DetectNow();
        TestBtn.Click += async (_, _) => await TestRead();
        if (!_prefs.ManualChat) _region = null;   // auto mode: the chat is found at start, not remembered
        StartBtn.Click += async (_, _) => await Toggle();
        SiteLink.Click += (_, _) => Open(RepoUrl);
        SentLink.Click += (_, _) => WhatsSent();
        Version.Text = "v" + Upload.RunPayload.AppVersion;
        // Market prices: its own switch and server, both remembered and live on the watcher; a chosen area replaces the title-and-headers search at once
        MarketChk.IsChecked = _prefs.ShareMarket;
        ServerBox.Text = _prefs.Server;
        MarketChk.Checked += (_, _) => MarketSwitch(true);
        MarketChk.Unchecked += (_, _) => MarketSwitch(false);
        ServerBox.TextChanged += (_, _) => _watcher.Market.Server = ServerBox.Text.Trim();
        MarketPickBtn.Click += (_, _) => PickMarket();
        MarketResetLink.Click += (_, _) => { _marketRegion = null; _prefs.ManualMarket = false; RegionStore.Clear(RegionStore.MarketFile); _watcher.Market.UseRegion(null); UpdateMarketUi(); Say("Back to automatic: the Market window is found by its title and column headers.", "Faint"); };
        MarketSentLink.Click += (_, _) => WhatsRead();
        try { LinkCodeRun.Text = Upload.InstallId.LinkCode(); } catch (Exception) { LinkCodeRun.Text = "unavailable"; }
        CopyCodeLink.Click += (_, _) => { try { Clipboard.SetText(LinkCodeRun.Text); Say("Link code copied. Paste it into your profile on the site to see your runs there.", "Good"); } catch (Exception) { Say("Couldn't reach the clipboard; the code is " + LinkCodeRun.Text, "Warn"); } };
        ResetLink.Click += (_, _) => { _region = null; _prefs.ManualChat = false; RegionStore.Clear(); UpdateStep2(); Preview.Source = null; PreviewHint.Visibility = Visibility.Visible; Say("Back to automatic: the chat is found when you start.", "Faint"); };
        LogsLink.Click += (_, _) => Open(CrashLog.Dir);

        Kind.SelectionChanged += (_, _) => { if (_session == null) { ApplyModeLabels(); ResetTiles(); } };
        ApplyModeLabels(); ResetTiles();
        BuildTray();
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
        SourceInitialized += OnSourceInitialized;
        StateChanged += OnStateChanged;
        Closing += OnClosingWindow;

        UpdateStep2();
        UpdateMarketUi();
        ShowTable(null);
        SettingsBtn.Click += (_, _) => SetCompact(!_compact);
        SetCompact(_prefs.Compact, remember: false);
        if (watch) _watcher.Start();
    }

    /// <summary>
    /// Once the chat and the zone have been found, steps 1 and 2 go behind the Settings button and the window
    /// shrinks to the mode picker, the tiles and the table. Settings brings the setup card back at any time.
    /// </summary>
    void SetCompact(bool on, bool remember = true)
    {
        _compact = on;
        var v = on ? Visibility.Collapsed : Visibility.Visible;
        SetupCard.Visibility = v; Tagline.Visibility = v; Badge3.Visibility = v;
        Step3Title.Margin = on ? new Thickness(0) : new Thickness(12, 0, 0, 0);
        KindGrid.Margin = on ? new Thickness(0, 12, 0, 0) : new Thickness(36, 12, 0, 0);
        OptsRow.Margin = on ? new Thickness(0, 14, 0, 0) : new Thickness(36, 14, 0, 0);
        LinkRow.Margin = on ? new Thickness(0, 8, 0, 0) : new Thickness(36, 8, 0, 0);
        // the Market card stays in the compact layout (its status matters while tracking), just without the step badge and indent
        Badge4.Visibility = v;
        Step4Title.Margin = on ? new Thickness(0) : new Thickness(12, 0, 0, 0);
        foreach (var fe in new FrameworkElement[] { MarketRow, MarketRow2, MarketStatus }) fe.Margin = new Thickness(on ? 0 : 36, fe.Margin.Top, 0, 0);
        Kind.Width = on ? 168 : 200;
        KindHint.Visibility = v;                 // the zone name gets the room instead
        StartBtn.Width = on ? 160 : 196;
        SettingsBtn.Content = on ? "Settings" : "Hide setup";
        SettingsBtn.ToolTip = on ? "Show the setup steps: chat area, zone and the preview." : "Hide the setup steps. The chat and the zone are still found when you press Start.";
        Width = on ? 640 : 800;
        if (_session == null && !_demo) Say(on ? "Not tracking. Press Start; the chat and the zone are found on their own. Setup is under Settings." : "Not tracking. Do steps 1 and 2, then press Start.", "Faint");
        if (remember) { _prefs.Compact = on; SavePrefs(); }
    }

    /// <summary>The compact layout for a layout render, without touching saved preferences.</summary>
    public void ShowCompact() => SetCompact(true, remember: false);

    /* ---------------- actions ---------------- */

    string ActivityName() => Kind.SelectedItem as string ?? "Open world";

    /// <summary>
    /// Open world rates drops per kill. Gathering has no Kinah and no kills: the unit is the node.
    /// Pets counts Soul Crystals by zone, per hour only, so the best spot shows; they never enter the drop tables.
    /// </summary>
    Mode _mode;
    Mode SelectedMode => (Kind.SelectedItem as string) switch { "Gathering" => Mode.Gather, "Pets" => Mode.Pets, _ => Mode.Open };

    void ApplyModeLabels()
    {
        var m = _session != null ? _mode : SelectedMode;
        (Tile1Label.Text, Tile2Label.Text, Tile3Label.Text, Tile4Label.Text, KindHint.Text) = m switch
        {
            Mode.Gather => ("NODES / HR", "ITEMS / HR", "ITEMS / NODE", "RARE FINDS / HR", "Ore, logs, Odyle, herbs and food. Each gather is a node; each skill has its own per-node rate."),
            Mode.Pets => ("SOUL CRYSTALS / HR", "SOUL CRYSTALS", "KILLS / HR", "KINAH / HR", "Pet Soul Crystals from mobs, counted by zone so the best spot per hour shows. Drops and per hour only, no drop %."),
            _ => ("KINAH / HR", "KINAH / KILL", "KILLS / HR", "DROPS / HR", "Mobs that drop Kinah. Each Kinah pickup is a kill. Souls and Soul Crystals never count as drops."),
        };
        // the mode's own table comes first; the others appear only when they have rows
        TableStack.Children.Remove(DropsSection); TableStack.Children.Remove(GatherSection); TableStack.Children.Remove(PetSection);
        var order = m switch { Mode.Gather => new[] { GatherSection, DropsSection, PetSection }, Mode.Pets => new[] { PetSection, DropsSection, GatherSection }, _ => new[] { DropsSection, PetSection, GatherSection } };
        for (int i = 0; i < order.Length; i++) { order[i].Margin = new Thickness(0, i == 0 ? 0 : 12, 0, 0); TableStack.Children.Insert(1 + i, order[i]); }
        if (_session == null) ShowTable(null);
    }

    void ResetTiles() { KinahHr.Text = "0"; KinahKill.Text = SelectedMode == Mode.Open ? "—" : "0"; KillsHr.Text = "0"; DropsHr.Text = "0"; }

    async Task Toggle() { if (_session == null) await StartTracking(); else await StopTracking(); }

    void Pick()
    {
        Hide();
        try { var r = RegionPickerWindow.Pick(); if (r != null) { _region = r; _prefs.ManualChat = true; RegionStore.Save(r.Value); Preview.Source = null; PreviewHint.Visibility = Visibility.Visible; } }
        finally { Show(); Activate(); UpdateStep2(); }
        if (_region != null) Say("Chat area chosen by hand. The zone is still read from the minimap if the game window is found. Press Test read, then Start.", "Good");
    }

    /// <summary>Finds the game, the chat and the zone right now and shows what it found, without starting a run.</summary>
    async Task DetectNow()
    {
        DetectBtn.IsEnabled = false; Say("Looking for the game window, the chat and the zone…", "Faint");
        try
        {
            var game = GameWindow.Find(_settings.ProcessHints);
            if (game == null) { if (_compact) SetCompact(false, remember: false); Say("The game window wasn't found. Start the game in borderless or windowed mode, or choose the chat yourself.", "Warn"); return; }
            if (_hud != null) { _hud.Hide(); }   // never read our own panel
            var (region, note) = await Task.Run(() => ChatLocator.DetectAsync(game.Value));
            var (zone, _) = await Task.Run(() => ZoneLocator.DetectAsync(game.Value));
            ZoneLbl.Text = "Zone: " + (zone ?? "not read yet");
            var g = game.Value;
            var marks = region != null ? new[] { (region.Value, TrackerSession.ChatColor), (ZoneLocator.Strip(g), TrackerSession.ZoneColor) } : new[] { (ZoneLocator.Strip(g), TrackerSession.ZoneColor) };
            SetPreview(await Task.Run(() => ScreenReader.Frame(g, 480, marks)));
            if (region == null)
            {
                if (_compact) SetCompact(false, remember: false);   // the buttons to fix it are in the setup card
                Say("Couldn't see the chat (" + note + "). Open the chat so its input box shows (press Enter once, or pin the chat window in the game's chat settings), then Detect again, or choose the chat yourself.", "Warn");
                return;
            }
            _region = region; _prefs.ManualChat = false; RegionStore.Clear();
            UpdateStep2();
            // steps 1 and 2 are done: from now on the window opens small, with the setup behind Settings
            bool first = !_compact; if (first) SetCompact(true);
            Say($"Found the chat ({note}){(zone != null ? " and the zone" : "")}. " + (first ? "Setup is now under Settings. " : "") + "Press Start whenever you're ready.", "Good");
        }
        catch (Exception ex) { Say(ex.Message, "Bad"); }
        finally { DetectBtn.IsEnabled = true; }
    }

    async Task TestRead()
    {
        if (_region == null) { await DetectNow(); if (_region == null) return; }
        TestBtn.IsEnabled = false; Say("Reading…", "Faint");
        _mode = SelectedMode;
        try
        {
            var reader = new ScreenReader(_region.Value, _settings.Ocr.Scale);
            var parser = new ChatParser(_items);
            var lines = await Task.Run(() => reader.ReadAsync());
            var game = GameWindow.Find(_settings.ProcessHints);
            SetPreview(game is { } gw ? await Task.Run(() => ScreenReader.Frame(gw, 480, (_region.Value, TrackerSession.ChatColor), (ZoneLocator.Strip(gw), TrackerSession.ZoneColor))) : reader.Thumb);
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
        SavePrefs();
        StartBtn.IsEnabled = false;
        Say(_region == null ? "Finding the chat and the zone…" : "Starting…", "Faint");
        try
        {
            _settings.Upload.Enabled = ShareChk.IsChecked == true;
            _mode = SelectedMode;
            ApplyModeLabels();
            var session = new TrackerSession(_prefs.ManualChat ? _region : null, _settings, _items, ActivityName());
            await session.StartAsync();
            _session = session;
            _region = session.Region;
            UpdateStep2();
            ZoneLbl.Text = "Zone: " + (session.Zone ?? "not read yet");
            if (HudChk.IsChecked == true)
            {
                _hud = new HudWindow { Owner = this };
                if (_prefs.HudX is double hx && _prefs.HudY is double hy && hx > SystemParameters.VirtualScreenLeft - 50 && hy > SystemParameters.VirtualScreenTop - 50) { _hud.Left = hx; _hud.Top = hy; }
                _hud.LocationChanged += (_, _) => { if (_hud != null && !_demo) { _prefs.HudX = _hud.Left; _prefs.HudY = _hud.Top; } };
                _hud.Show();
            }
            TableTitle.Text = "THIS SESSION";
            ShowTable(session.Engine);
            SetTrackingUi(true);
            if (!_compact) SetCompact(true);   // chat and zone found: the setup card has done its job
            Say($"Tracking {session.Activity}. Minimize this window; the panel and Ctrl+Shift+K keep working.", "Faint");
        }
        catch (Exception ex)
        {
            _session = null; StartBtn.IsEnabled = true;
            if (_compact) SetCompact(false, remember: false);   // show the setup card so the fix is one click away
            Say(ex.Message, "Warn");
        }
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
        else if (string.IsNullOrWhiteSpace(_settings.Upload.Endpoint)) msg = "Stopped. No sharing address in settings.json, so this run stays on this PC.";
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
        PickBtn.IsEnabled = !on; TestBtn.IsEnabled = !on; Kind.IsEnabled = !on; HudChk.IsEnabled = !on; ShareChk.IsEnabled = !on;
        // the Market card is not part of a run: its switch, server and area stay live either way
        _tray.Text = on ? "Kinah Ledger · tracking" : "Kinah Ledger";
    }

    void Tick()
    {
        if (!_demo) MarketStatus.Text = MarketLine();   // the Market reader runs with or without a run
        var s = _session; if (s == null) return;
        FillNumbers(s.Engine, s.Engine.Elapsed);
        ZoneLbl.Text = "Zone: " + (s.Zone ?? "not read yet");
        if (s.LastError.Length > 0) Say("Read problem: " + s.LastError, "Bad");
        else Say($"Tracking {s.Activity} · {s.Reads} reads" + (s.Unaligned > 0 ? $" · {s.Unaligned} skipped" : "") + (s.LastLine.Length > 0 ? " · last: " + s.LastLine : ""), "Faint");
        if (++_ticks % 4 == 0 && !_framing)
        {
            // the whole game with the chat, map and (when open) Market outlined, refreshed every couple of seconds off the UI thread
            _framing = true;
            var marks = MarketMarks();
            _ = Task.Run(() => s.Frame(480, marks)).ContinueWith(t => Dispatcher.Invoke(() => { _framing = false; if (t.Status == TaskStatus.RanToCompletion) SetPreview(t.Result); }));
        }
        if (_hud != null)
        {
            var e = s.Engine; double h = e.Hours; var t = e.Totals(); long kinah = t.Kinah;
            var foot = (s.Zone != null ? s.Zone + " · " : "") + s.LastLine;
            switch (_mode)
            {
                case Mode.Gather:
                    _hud.Update($"{Fk(t.Drops / h)} items/hr",
                        $"{Fk(t.Nodes / h)} nodes/hr · {(t.Nodes > 0 ? (t.Drops / (double)t.Nodes).ToString("0.00") : "—")} items/node · {Fk(t.Bonus / h)} rare/hr\n{e.Elapsed:hh\\:mm\\:ss} · {t.Drops} items · {t.Nodes} nodes{(t.Failed > 0 ? $" ({t.Failed} failed)" : "")} · {t.Bonus} rare finds",
                        foot);
                    break;
                case Mode.Pets:
                    var (best, rate) = BestZone(e.ZoneSnapshot(), h);
                    _hud.Update($"{Fk(t.Pets / h)} crystals/hr",
                        $"{Fk(t.Kills / h)} kills/hr · {Fk(kinah / h)} Kinah/hr · {t.Pets} crystals\n{e.Elapsed:hh\\:mm\\:ss} · {t.Kills} kills · {Fk(kinah)} Kinah" + (best != null ? $" · best: {best} {Rate(rate)}/hr" : ""),
                        foot);
                    break;
                default:
                    _hud.Update($"{Fk(kinah / h)} Kinah/hr",
                        $"{Fk(t.Kills / h)} kills/hr · {Fk(t.Drops / h)} drops/hr · {(t.Kills > 0 ? Fk(kinah / (double)t.Kills) : "—")} Kinah/kill\n{e.Elapsed:hh\\:mm\\:ss} · {Fk(kinah)} Kinah · {t.Drops} drops · {t.Kills} kills" + (t.Pets > 0 ? $" · {t.Pets} crystals" : ""),
                        foot);
                    break;
            }
        }
    }

    sealed record Tables(List<DropRow> Drops, List<DropRow> Gathered, List<DropRow> Pets, long DropCount, long GatherCount, long Value, string? BestZone, double BestRate);

    static string Mins(double seconds) { int m = (int)Math.Round(seconds / 60); return m >= 60 ? $"{m / 60} h {m % 60:00} min" : $"{m} min"; }
    /// <summary>Hours credited to a zone; a zone seen for under half a minute is rated against the whole run instead.</summary>
    static double ZoneHours(SessionEngine.ZoneStats z, double sessionHours) => z.Seconds >= 30 ? z.Seconds / 3600 : sessionHours;
    /// <summary>The zone with the most Soul Crystals per hour, among zones with at least a minute of play.</summary>
    static (string? zone, double rate) BestZone(Dictionary<string, SessionEngine.ZoneStats> zones, double hours)
    {
        string? best = null; double bestRate = 0;
        foreach (var (zone, z) in zones)
        {
            if (z.Pets == 0 || z.Seconds < 60 || zone == SessionEngine.UnknownZone) continue;
            double r = z.Pets / ZoneHours(z, hours);
            if (r > bestRate) { best = zone; bestRate = r; }
        }
        return (best, bestRate);
    }

    /// <summary>
    /// Splits the session into mob drops (rated per kill), gathered materials grouped by skill (each skill rated
    /// against its own nodes), and pet Soul Crystals grouped by zone (rated per hour in that zone, never per kill).
    /// </summary>
    Tables Build(SessionEngine e, double hours, int kills)
    {
        var drops = new List<DropRow>(); long dc = 0, gc = 0, value = 0;
        var bySkill = new Dictionary<GatherFamily, List<(string name, int total)>>();
        var bySkillGrade = new Dictionary<string, string>();
        foreach (var r in e.ItemRows())
        {
            int total = r.Count + r.Bound;
            var price = PriceBook.Price(r.Name); if (price != null) value += price.Value * total;
            var g = GatheringCatalog.Find(r.Name);
            var grade = g?.Grade ?? _items.Get(r.Id)?.Grade ?? "";
            if (g != null) { gc += total; if (!bySkill.TryGetValue(g.Skill, out var list)) bySkill[g.Skill] = list = new(); list.Add((r.Name, total)); bySkillGrade[r.Name] = grade; }
            else { dc += total; drops.Add(new DropRow(r.Name, total.ToString("N0"), kills > 0 ? Pct(total / (double)kills) : "—", Rate(total / hours), Grade: grade)); }
        }
        var gathered = new List<DropRow>();
        var stats = e.SkillSnapshot();
        foreach (var skill in GatherSkills.Order)
        {
            if (!bySkill.TryGetValue(skill, out var items) && !stats.ContainsKey(skill)) continue;
            var st = stats.GetValueOrDefault(skill) ?? new SessionEngine.SkillStats();
            int ok = st.Nodes - st.Failed;
            var head = $"{GatherSkills.Label(skill).ToUpperInvariant()}  ·  {st.Nodes} node{(st.Nodes == 1 ? "" : "s")}"
                     + (st.Failed > 0 ? $" ({st.Failed} failed)" : "")
                     + (ok > 0 ? $"  ·  plentiful {Math.Round(100.0 * st.Plentiful / ok)}%" : "")
                     + (st.Nodes > 0 ? $"  ·  {Rate(st.Nodes / hours)} nodes/hr" : "");
            gathered.Add(new DropRow(head, "", "", "", Header: true));
            foreach (var (name, total) in (items ?? new()).OrderByDescending(x => x.total))
                gathered.Add(new DropRow(name, total.ToString("N0"), st.Nodes > 0 ? Pct(total / (double)st.Nodes) : "—", Rate(total / hours), Grade: bySkillGrade.GetValueOrDefault(name, "")));
        }
        // pets: one header per zone, best rate first, each crystal rated against the time spent in that zone
        var pets = new List<DropRow>();
        var zones = e.ZoneSnapshot();
        foreach (var (zone, z) in zones.Where(kv => kv.Value.Pets > 0).OrderByDescending(kv => kv.Value.Pets / ZoneHours(kv.Value, hours)))
        {
            double zh = ZoneHours(z, hours);
            var when = z.Seconds >= 30 ? Mins(z.Seconds) : "zone time not read";
            pets.Add(new DropRow($"{zone.ToUpperInvariant()}  ·  {when}  ·  {z.Kills} kill{(z.Kills == 1 ? "" : "s")}  ·  {Rate(z.Pets / zh)} crystals/hr", "", "", "", Header: true));
            foreach (var (name, n) in z.PetsByName.OrderByDescending(x => x.Value))
                pets.Add(new DropRow(name, n.ToString("N0"), "", Rate(n / zh)));
        }
        var (best, bestRate) = BestZone(zones, hours);
        return new Tables(drops, gathered, pets, dc, gc, value, best, bestRate);
    }

    void FillNumbers(SessionEngine e, TimeSpan elapsed)
    {
        double h = Math.Max(elapsed.TotalHours, 1.0 / 3600);
        var t = e.Totals(); long kinah = t.Kinah;   // farming Kinah only; bound Kinah is reward money
        var s = Build(e, h, t.Kills);
        string worth = s.Value > 0 ? $" · worth {Fk(s.Value)} ({Fk(s.Value / h)}/hr)" : "";
        string skipped = t.Rewards > 0 ? $" · {t.Rewards} reward line{(t.Rewards == 1 ? "" : "s")} skipped" : "";
        string crystals = t.Pets > 0 ? $" · {t.Pets} soul crystal{(t.Pets == 1 ? "" : "s")}" : "";
        switch (_mode)
        {
            case Mode.Gather:
            {
                KinahHr.Text = Fk(t.Nodes / h);
                KinahKill.Text = Fk(s.GatherCount / h);
                KillsHr.Text = t.Nodes > 0 ? (s.GatherCount / (double)t.Nodes).ToString("0.00") : "—";
                DropsHr.Text = Fk(t.Bonus / h);
                int ok = t.Nodes - t.Failed;
                string plent = ok > 0 ? $" · plentiful {Math.Round(100.0 * t.Plentiful / ok)}%" : "";
                string fails = t.Failed > 0 ? $" ({t.Failed} failed)" : "";
                Summary.Text = $"{elapsed:hh\\:mm\\:ss} · {s.GatherCount} gathered · {t.Nodes} nodes{fails}{plent} · {t.Bonus} rare finds{worth}" + (kinah > 0 ? $" · {Fk(kinah)} Kinah" : "") + crystals + skipped;
                break;
            }
            case Mode.Pets:
                KinahHr.Text = Fk(t.Pets / h);
                KinahKill.Text = t.Pets.ToString("N0");
                KillsHr.Text = Fk(t.Kills / h);
                DropsHr.Text = Fk(kinah / h);
                Summary.Text = $"{elapsed:hh\\:mm\\:ss} · {t.Pets} soul crystals · {t.Kills} kills · {Fk(kinah)} Kinah"
                             + (s.BestZone != null ? $" · best zone: {s.BestZone} ({Rate(s.BestRate)}/hr)" : "")
                             + (s.DropCount > 0 ? $" · {s.DropCount} drops" : "") + skipped;
                break;
            default:
                KinahHr.Text = Fk(kinah / h);
                KinahKill.Text = t.Kills > 0 ? Fk(kinah / (double)t.Kills) : "—";
                KillsHr.Text = Fk(t.Kills / h);
                DropsHr.Text = Fk(s.DropCount / h);
                Summary.Text = $"{elapsed:hh\\:mm\\:ss} · {Fk(kinah)} Kinah · {s.DropCount} drops · {t.Kills} kills{worth}" + crystals + (s.GatherCount > 0 ? $" · {s.GatherCount} gathered" : "") + skipped;
                break;
        }
        ShowTable(e, elapsed, s);
    }

    void ShowTable(SessionEngine? e, TimeSpan? elapsed = null, Tables? pre = null)
    {
        var drops = new List<DropRow>(); var gathered = new List<DropRow>(); var pets = new List<DropRow>();
        int kills = 0, nodes = 0;
        var m = e == null ? SelectedMode : _mode;
        if (e != null)
        {
            double h = Math.Max((elapsed ?? e.Elapsed).TotalHours, 1.0 / 3600);
            var t = e.Totals(); kills = t.Kills; nodes = t.Nodes;
            var s = pre ?? Build(e, h, kills);
            drops = s.Drops; gathered = s.Gathered; pets = s.Pets;
        }
        Rows.ItemsSource = drops; GatherRows.ItemsSource = gathered; PetRows.ItemsSource = pets;
        TableEmpty.Visibility = drops.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        GatherEmpty.Visibility = gathered.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        PetEmpty.Visibility = pets.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        TableEmpty.Text = e == null ? "Drops show here as they're read, with the drop rate per kill and per hour." : "No drops yet.";
        GatherEmpty.Text = e == null ? "Gathered materials show here, with how often each comes per node and per hour." : "Nothing gathered yet.";
        PetEmpty.Text = e == null ? "Soul Crystals show here by zone, with drops and per hour, so the best pet-farming spot stands out." : "No Soul Crystals yet.";
        // the mode's own table always shows; the others only when they have something
        DropsSection.Visibility = m == Mode.Open || (e != null && drops.Count > 0) ? Visibility.Visible : Visibility.Collapsed;
        GatherSection.Visibility = m == Mode.Gather || (e != null && gathered.Count > 0) ? Visibility.Visible : Visibility.Collapsed;
        PetSection.Visibility = m == Mode.Pets || (e != null && pets.Count > 0) ? Visibility.Visible : Visibility.Collapsed;
        TableNote.Text = m switch
        {
            Mode.Gather => "Per-node rates appear once a node is counted: any ore, log, Odyle, herb or food line counts as one node.",
            Mode.Pets => "Zone rates appear once the zone has been read from the minimap for a minute.",
            _ => "Drop % appears once kills are counted. Kills come from Kinah or Soul Point pickups, one per mob.",
        };
        TableNote.Visibility = m switch
        {
            Mode.Gather => gathered.Count > 0 && nodes == 0,
            Mode.Pets => pets.Count > 0 && pre?.BestZone == null,
            _ => drops.Count > 0 && kills == 0,
        } ? Visibility.Visible : Visibility.Collapsed;
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
        "•  what you're farming and where, gear score bracket, region\n•  run length in minutes and kill count\n•  Kinah picked up, tradeable and bound\n•  drop counts per item, Soul Crystal counts per zone, minutes per zone\n•  app version and a random install ID used only to stop spam\n\n" +
        "Never your character name, server, account, class, level, chat text or screenshots.\n" +
        "If you paste the profile link code into your profile on the site, the runs from this PC show up there too; nobody else can see them unless you make the profile public.\n\n" +
        "Sharing isn't live yet: until the site can receive runs (upload.endpoint in settings.json), everything stays on this PC. Once it is, the totals go once, when you press Stop; if the site can't be reached, the run stays here, saved to your history, and isn't sent later.",
        "What's shared", MessageBoxButton.OK, MessageBoxImage.Information);

    /* ---------------- market prices (always on while the window is open; not part of a run) ---------------- */

    void MarketSwitch(bool on)
    {
        _prefs.ShareMarket = on;
        _watcher.Market.Enabled = on;
        UpdateMarketUi();
    }

    void PickMarket()
    {
        Hide();
        try
        {
            var r = RegionPickerWindow.Pick("Drag a box around the Market's listings: the rows with item, quantity and unit price.");
            if (r != null) { _marketRegion = r; _prefs.ManualMarket = true; RegionStore.Save(r.Value, RegionStore.MarketFile); _watcher.Market.UseRegion(r); }
        }
        finally { Show(); Activate(); UpdateMarketUi(); }
        if (_prefs.ManualMarket && _marketRegion != null) Say("Market area chosen by hand and in use now. Each row is read as one line and split by its numbers.", "Good");
    }

    /// <summary>The card's buttons and its line, right away; the line is then refreshed every half second from the watcher.</summary>
    void UpdateMarketUi()
    {
        bool manual = _prefs.ManualMarket && _marketRegion != null;
        MarketResetText.Visibility = manual ? Visibility.Visible : Visibility.Collapsed;
        MarketPickBtn.Content = manual ? "Choose again" : "Choose the Market";
        MarketStatus.Text = MarketLine();
    }

    /// <summary>What the Market reader is doing right now, run or no run.</summary>
    string MarketLine()
    {
        var m = _watcher.Market;
        if (!m.Enabled) return "Off. The Market is never read; everything else keeps working.";
        if (_watcher.GameRect == null) return "Waiting for the game. While the game and this window are open, the Market is read whenever you open it; Start tracking is only for farming runs.";
        return m.Status + (m.Manual && _marketRegion is DRect r ? $" · chosen area {r.Width} x {r.Height} px" : "");
    }

    /// <summary>The Market table's outline for the preview, when it is being read.</summary>
    (DRect, System.Drawing.Color)[] MarketMarks()
    {
        var m = _watcher.Market;
        return m.Enabled && m.Layout is { } l && (m.Detected || m.Manual) ? new[] { (l.Outline, MarketCapture.MarkColor) } : Array.Empty<(DRect, System.Drawing.Color)>();
    }

    void WhatsRead() => MessageBox.Show(this,
        "Whenever this window and the game are open (not only during a run), the tracker watches for the Market window. While the Market is open, the listings on your screen are read about once a second, the same way the chat is:\n\n" +
        "•  item name, stack size and unit price of each ask\n•  your server, as typed here, so prices land on the right board\n•  app version and the random install ID used only to stop spam\n\n" +
        "Never seller names, your character, chat text or screenshots. The same ask isn't sent twice within 15 minutes.\n" +
        "Nothing is clicked or typed in the game. Turn this off any time; the tracker still works.\n\n" +
        "Sharing isn't live yet: until the site can receive prices, what's read stays on this PC and is never stored. Once it is, asks go out in small batches as they are read; if the site can't be reached, the batch is tried again a few seconds later, and whatever is still unsent when this window closes is dropped.",
        "What's read from the Market", MessageBoxButton.OK, MessageBoxImage.Information);

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
        if (_watcher.Running && !_watcherStopped)
        {
            // let the Market reader send what it still holds (a few seconds at most), then close for real
            e.Cancel = true; _watcherStopped = true;
            _ = _watcher.StopAsync().ContinueWith(_ => Dispatcher.Invoke(Close));
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
        _prefs.Activity = Kind.SelectedItem as string ?? "Open world";
        _prefs.Hud = HudChk.IsChecked == true; _prefs.Share = ShareChk.IsChecked == true;
        _prefs.ShareMarket = MarketChk.IsChecked == true; _prefs.Server = ServerBox.Text.Trim();
        if (_prefs.ManualChat && _region is DRect mr) RegionStore.Save(mr);
        if (_prefs.ManualMarket && _marketRegion is DRect mm) RegionStore.Save(mm, RegionStore.MarketFile);
        if (WindowState == WindowState.Normal && !double.IsNaN(Left)) { _prefs.WinX = (int)Left; _prefs.WinY = (int)Top; }
        _prefs.Save();
    }

    void UpdateStep2()
    {
        if (_prefs.ManualChat && _region is DRect m) { Step2Status.Text = $"Chosen by hand: {m.Width} × {m.Height} px at {m.X}, {m.Y}. Reset chat area to go back to automatic."; Step2Status.Foreground = B("Ink"); }
        else if (_region is DRect r) { Step2Status.Text = $"Found automatically: {r.Width} × {r.Height} px at {r.X}, {r.Y}. The zone is read from the minimap while you play."; Step2Status.Foreground = B("Ink"); }
        else { Step2Status.Text = "Found automatically when you press Start: the chat above the input box, and the zone from the minimap. If it can't find the chat, choose it yourself."; Step2Status.Foreground = B("Muted"); }
    }

    public void ForceClose() { _quitting = true; Close(); }

    /// <summary>Runs start → wait → stop exactly as the buttons would, for catching crashes without a desktop session.</summary>
    public async Task<string> DebugCycle(DRect? region, int ms)
    {
        _demo = true;
        _region = region; _prefs.ManualChat = region != null; UpdateStep2();
        await StartTracking();
        if (_session == null) throw new InvalidOperationException("start failed: " + Status.Text);
        var found = _session.Region; var zone = _session.Zone;
        await Task.Delay(ms);
        var reads = _session.Reads;
        await StopTracking();
        return $"{reads} reads, chat {found}, zone {zone ?? "none"}, status: {Status.Text}";
    }

    /// <summary>Fills the window with a sample session (open world, gathering, pets, or open world with the Market open) so its layout can be reviewed without the game.</summary>
    public void ShowDemo(string kind = "Open world")
    {
        _demo = true;
        bool market = kind == "Market";
        _region = new DRect(96, 1664, 1338, 384); _prefs.ManualChat = false; UpdateStep2();
        ZoneLbl.Text = "Zone: Western Cantas Valley";
        var e = new SessionEngine(_items); var t = DateTime.UtcNow;
        void Drop(string n, int c, bool b = false) { for (int i = 0; i < c; i++) e.Apply(new ItemAcquired(t, _items.IdOf(n), 1, b)); }
        void Kills(int n, int kinah) { for (int i = 0; i < n; i++) { e.Apply(new KinahChanged(t, kinah + (i % 7) * 40, false)); e.Apply(new CurrencyGained(t, "Oculazen Soul Points", 1)); } }
        void Pet(string n, int c) { for (int i = 0; i < c; i++) e.Apply(new PetCrystal(t, n + " Soul Crystal", 1)); }
        Kind.SelectedItem = Kinds.Contains(kind) ? kind : "Open world"; _mode = SelectedMode;
        switch (_mode)
        {
            case Mode.Gather:
                e.CreditZone("Western Cantas Valley", 42 * 60 + 17);
                Drop("Orichalcum Ore", 82); for (int i = 0; i < 14; i++) e.Apply(new ItemAcquired(t, _items.IdOf("Orichalcum Ore"), 2, false));
                Drop("Fine Orichalcum Ore", 14); Drop("Pure Orichalcum Ore", 2); Drop("Sapphire Gemstone", 9); Drop("Ruby Gemstone", 6); Drop("Diamond Gemstone", 4); Drop("Fine Sapphire Gemstone", 1);
                Drop("Kukuru", 23); Drop("Conide", 11); Drop("Odyle", 31); Drop("Fine Odyle", 3);
                for (int i = 0; i < 11; i++) e.Apply(new GatherFailed(t, "Essence Extraction"));
                break;
            case Mode.Pets:
                e.CreditZone("Western Cantas Valley", 25 * 60); Kills(96, 3690); Pet("Natura", 9); Pet("Fera", 4); Drop("Spiritstone", 12);
                e.CreditZone("Rock Tomb Hill", 17 * 60 + 17); Kills(71, 2900); Pet("Natura", 3); Pet("Fera", 6); Pet("Oculazen", 1); Drop("Thick hide", 9);
                break;
            default:
                e.CreditZone("Western Cantas Valley", 42 * 60 + 17);
                Kills(161, 3690);
                Drop("Thick hide", 41); Drop("Spiritstone", 26); Drop("Fresh Fruit", 19); Drop("Manastone", 8); Drop("Aether shard", 7); Drop("Enchantment stone", 3); Drop("Greater manastone", 1);
                Drop("Orichalcum Ore", 6); Drop("Sapphire Gemstone", 1);
                Pet("Natura", 3);
                break;
        }
        ApplyModeLabels();
        TableTitle.Text = "THIS SESSION";
        FillNumbers(e, TimeSpan.FromMinutes(42) + TimeSpan.FromSeconds(17));
        using (var b = new System.Drawing.Bitmap(480, 203))
        using (var g = System.Drawing.Graphics.FromImage(b))
        {
            // a stand-in for the game frame: dark field, chat box bottom-left, map strip top-right
            g.Clear(System.Drawing.Color.FromArgb(18, 30, 24));
            using var ground = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(34, 52, 40));
            g.FillRectangle(ground, 0, 120, 480, 83);
            using var chatPen = new System.Drawing.Pen(TrackerSession.ChatColor, 2); using var zonePen = new System.Drawing.Pen(TrackerSession.ZoneColor, 2);
            g.DrawRectangle(chatPen, 10, 148, 62, 44); g.DrawRectangle(zonePen, 364, 0, 116, 28);
            using var f = new System.Drawing.Font("Consolas", 5f);
            string[] lines = { "Orichalcum Ore acquired.", "Acquired 436 Kina.", "Kukuru acquired." };
            for (int i = 0; i < lines.Length; i++) g.DrawString(lines[i], f, System.Drawing.Brushes.Khaki, 12, 152 + i * 9);
            if (market)
            {
                // the Market panel in the middle of the frame, its table outlined like the live preview does
                using var panel = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(14, 20, 32));
                using var marketPen = new System.Drawing.Pen(MarketCapture.MarkColor, 2);
                g.FillRectangle(panel, 120, 30, 240, 120);
                g.DrawString("Market", f, System.Drawing.Brushes.White, 126, 34);
                g.DrawString("Item              Qty   Unit price    Total", f, System.Drawing.Brushes.Gray, 126, 46);
                string[] rows = { "Spiritstone        x1       7,499     7,499", "Spiritstone       x10       7,520    75,200", "Spiritstone        x5       7,600    38,000", "Raw Leather       x20      14,949   298,980" };
                for (int i = 0; i < rows.Length; i++) g.DrawString(rows[i], f, System.Drawing.Brushes.Gainsboro, 126, 58 + i * 9);
                g.DrawRectangle(marketPen, 122, 32, 236, 116);
            }
            SetPreview((System.Drawing.Bitmap)b.Clone());
        }
        SetTrackingUi(true);
        Say(_mode switch { Mode.Gather => "Tracking Gathering · Western Cantas Valley · 3,620 reads · last: Sapphire Gemstone acquired.", Mode.Pets => "Tracking Pets · Rock Tomb Hill · 3,620 reads · last: Obtained 1 Fera Soul Crystals.", _ => "Tracking Open world · Western Cantas Valley · 3,620 reads · last: Spiritstone acquired." }, "Faint");
        if (market)
        {
            ServerBox.Text = "Siel (Elyos)"; MarketChk.IsChecked = true;
            MarketStatus.Text = "Market open · 318 asks read · 41 items · 318 shared · last read 12 s ago";
        }
    }
}
