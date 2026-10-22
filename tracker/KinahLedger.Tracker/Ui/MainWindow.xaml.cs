using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using KinahLedger.Data;
using KinahLedger.Ocr;
using KinahLedger.Platform;
using KinahLedger.Session;
using DRect = System.Drawing.Rectangle;
using WF = System.Windows.Forms;

namespace KinahLedger.Ui;

/// <summary>One table row. Icon is the item's icon slot (filled as the file arrives) and Initial the letter shown until then; both empty on a header row.</summary>
/// <param name="Tip">How the row was read or worked out, shown on hover only; the line under the name gets a ⓘ when there is one.</param>
/// <param name="Worth">The row's tradeable copies at the lowest Market price, before the fee: what Top drops and the Discord card rank by.</param>
public sealed record DropRow(string Name, string Drops, string DropPct, string PerHour, bool Header = false, string Grade = "", IconSlot? Icon = null, string Initial = "", string Price = "", string Value = "", string Sub = "", string Tone = "", string Key = "", bool Child = false, string Report = "", bool Reported = false, string Tip = "", long Worth = 0)
{
    /// <summary>The row's report link: "Looks wrong?" flags the drop so the site never gets it; "Undo" takes the flag off.</summary>
    public string ReportText => Reported ? "Undo" : "Looks wrong?";
    /// <summary>The line under the name, with a ⓘ when hovering the row tells more.</summary>
    public string SubShown => Tip.Length == 0 || Header ? Sub : Sub.Length > 0 ? Sub + "  ⓘ" : "ⓘ";
}

public partial class MainWindow : Window
{
    // dungeons and raids are out for now: a boss kill drops more lines at once than any chat window shows
    // Soul Crystals are still counted and shared (the site's Pets page needs them); the window shows them as a count only
    // open world or gathering is worked out from what the chat shows (0.7.6: the picker is gone); a run is saved and shared
    // under what it turned out to be (GameWatcher.DominantKind)
    enum Mode { Open, Gather }
    public const string RepoUrl = "https://github.com/StevenMacchia/kinah-ledger";
    public const string SiteUrl = "https://kinahledger.com";
    const int HotkeyId = 1;

    readonly Settings _settings;
    readonly UserPrefs _prefs = UserPrefs.Load();
    readonly ItemDictionary _items;
    /// <summary>Runs from the moment the window opens: finds the game and reads the Market whenever it is open. Start tracking is only for farming runs.</summary>
    readonly GameWatcher _watcher;
    /// <summary>The farming run just stopped, shown for a while before the live count takes the window back.</summary>
    SessionEngine? _lastRun; DateTime _lastRunUntil;
    bool Farming => _watcher.Farming;
    static Mode ModeOf(SessionEngine e) => GameWatcher.DominantKind(e) == "Gathering" ? Mode.Gather : Mode.Open;
    HudWindow? _hud;
    DRect? _region = RegionStore.Load();
    DRect? _marketRegion = RegionStore.Load(RegionStore.MarketFile);
    bool _quitting, _balloonShown, _trayHintShown, _demo, _watcherStopped;
    /// <summary>The Options window while it is open.</summary>
    OptionsWindow? _options;
    /// <summary>The last frame of the game with the chat, zone and Market outlined, for the Options window's preview.</summary>
    public System.Windows.Media.Imaging.BitmapSource? PreviewSource { get; private set; }

    readonly WF.NotifyIcon _tray = new();
    readonly WF.ToolStripMenuItem _trayToggle = new("Start farming");
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    bool _framing; int _ticks;

    // a timer tick after the window closed (the off-screen renders) finds no theme: grey rather than a crash
    Brush B(string key) => TryFindResource(key) as Brush ?? Brushes.Gray;

    /// <param name="watch">False for layout renders, so no screen is read while the window exists off-screen.</param>
    public MainWindow(Settings settings, bool watch = true)
    {
        InitializeComponent();
        _settings = settings;
        _items = ItemDictionary.LoadOrEmpty(settings.ItemsFile);
        IconCache.Enabled = settings.Ui.Icons;
        if (!_prefs.ManualMarket) _marketRegion = null;
        _settings.Upload.Enabled = _prefs.Share;
        // the site's item list, so drops are shared under real ids and show their icons; one conditional request per start
        if (watch && (settings.Ui.Icons || _settings.Upload.Enabled)) _ = DictionaryCache.RefreshAsync(_items);
        _watcher = new GameWatcher(settings, _items, _marketRegion, _prefs.ManualChat ? _region : null, _prefs.Server) { RegionKey = _prefs.Region, Server = _prefs.Server };
        _watcher.Market.Enabled = settings.Market.Enabled && _prefs.Share;   // prices ride on the one Share switch
        Icon = Native.AppIconSource();

        // off by default: the tables start over on a new zone. The site gets each zone's own numbers either way.
        // every start: Drops, Gathered and Kinah by enemy open, Soul Crystals folded (Steven: Gathered is never hidden at start)
        _prefs.DropsOpen = _prefs.GatherOpen = _prefs.MobsOpen = true; _prefs.PetsOpen = false;
        AllZonesChk.IsChecked = _prefs.AllZones;
        AllZonesChk.Click += (_, _) => { _prefs.AllZones = AllZonesChk.IsChecked == true; if (!_demo) _prefs.Save(); Redraw(); Tick(); };
        if (_prefs.WinX is int x && _prefs.WinY is int y && x > SystemParameters.VirtualScreenLeft - 50 && y > SystemParameters.VirtualScreenTop - 50
            && x < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 200 && y < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 200)
        { WindowStartupLocation = WindowStartupLocation.Manual; Left = x; Top = y; }

        if (!_prefs.ManualChat) _region = null;   // auto mode: the chat is found at start, not remembered
        StartBtn.Click += async (_, _) => await Toggle();
        // everything that can be set is in the Options window, so the ledger never moves or grows for it
        OptionsBtn.Click += (_, _) => OpenOptions();
        SiteBtn.Click += (_, _) => Open(SiteUrl);
        ServerChip.MouseLeftButtonUp += (_, _) => OpenOptions(OptionsWindow.Page.Server);
        FixLink.Click += (_, _) => OpenOptions(OptionsWindow.Page.Reading);
        Version.Text = "v" + Upload.RunPayload.AppVersion;
        // the server: everything shared is per server, so it is chosen first (welcome screen or Options) and remembered
        _watcher.Market.Region = RegionKey; _watcher.Market.Server = Server?.Label ?? "";

        ShareCardBtn.Click += (_, _) => CopyShareCard();
        // Compact and Full view: the header button switches, and the summary's link opens Full view
        ViewBtn.Click += (_, _) => SetView(!_prefs.FullView);
        FullLink.Click += (_, _) => SetView(true);
        // a click on ODDS switches "1 in 21" and "4.9%" (and doesn't fold the section under it)
        OddsAsPct = _prefs.OddsAsPercent; ShowOddsHeader();
        PctHeader.MouseLeftButtonUp += (_, ev) => { ev.Handled = true; OddsAsPct = _prefs.OddsAsPercent = !_prefs.OddsAsPercent; if (!_demo) _prefs.Save(); ShowOddsHeader(); Redraw(); };
        DropsHead.MouseLeftButtonUp += (_, _) => { if (Rows.Items.Count == 0) return; _prefs.DropsOpen = !_prefs.DropsOpen; if (!_demo) _prefs.Save(); Redraw(); };
        GatherHead.MouseLeftButtonUp += (_, _) => { if (GatherRows.Items.Count == 0) return; _prefs.GatherOpen = !_prefs.GatherOpen; if (!_demo) _prefs.Save(); Redraw(); };
        MobHead.MouseLeftButtonUp += (_, _) => { if (MobRows.Items.Count == 0) return; _prefs.MobsOpen = !_prefs.MobsOpen; if (!_demo) _prefs.Save(); Redraw(); };
        PetHead.MouseLeftButtonUp += (_, _) => { if (PetRows.Items.Count == 0) return; _prefs.PetsOpen = !_prefs.PetsOpen; if (!_demo) _prefs.Save(); Redraw(); };
        // "show all" / "show fewer" under each section
        foreach (var (name, link) in new[] { ("Drops", DropsMoreLink), ("Gather", GatherMoreLink), ("Mob", MobMoreLink), ("Pet", PetMoreLink) })
            link.Click += (_, _) => { if (!_allRows.Remove(name)) _allRows.Add(name); Redraw(); };
        // a click on a drop or gathered row opens its card: this run, the price, the site, the listed chances
        Rows.MouseLeftButtonUp += ItemRowClick; GatherRows.MouseLeftButtonUp += ItemRowClick;
        // a click on an enemy opens or closes its loot table
        MobRows.MouseLeftButtonUp += (_, ev) =>
        {
            if ((ev.OriginalSource as FrameworkElement)?.DataContext is DropRow { Key.Length: > 0 } r)
            {
                if (!_openMobs.Remove(r.Key)) _openMobs.Add(r.Key);
                Redraw();
            }
        };
        // Farming and Crafting are two parts of the window; crafting never shows in the farming tables
        FarmTab.Click += (_, _) => ShowPart("Farming");
        CraftTab.Click += (_, _) => ShowPart("Crafting");
        EventsTab.Click += (_, _) => ShowPart("Events");
        CraftCreditLink.Click += (_, _) => Open("https://metabot.gg/en/aion-2/crafting");
        MobCreditLink.Click += (_, _) => Open(MobImages.CreditUrl);
        CraftResetLink.Click += (_, _) =>
        {
            if (MessageBox.Show(this, "Start crafting over? The recipes, tries and profit shown here are cleared.", "Kinah Ledger", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            _watcher.Chat?.ResetCraftView(); FillCraft(_watcher.Chat?.CraftView);
        };
        InstClearLink.Click += (_, _) =>
        {
            if (MessageBox.Show(this, "Clear the expedition and event runs shown here? Runs not sent to the site yet are still sent.", "Kinah Ledger", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            _watcher.Chat?.Instances.ClearShown(); ShowInstances();
        };
        FarmClearLink.Click += async (_, _) =>
        {
            if (Farming) { Say("A farming run is on: Stop ends it and starts the tables over.", "Warn"); return; }
            if (MessageBox.Show(this, "Clear the farming tables and start counting again? What was counted is shared with the site first, as usual.", "Kinah Ledger", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            await _watcher.ClearFarmingAsync();
            _lastRun = null;
            Redraw();
            Say("Farming tables cleared. Counting starts again from here.", "Faint");
        };
        _mode = Mode.Open;
        ApplyModeLabels(); ResetTiles();
        BuildTray();
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
        SourceInitialized += OnSourceInitialized;
        StateChanged += OnStateChanged;
        Closing += OnClosingWindow;

        // the tables grow with what's farmed and the window with them, never scrolling while it fits; 0.7.6 (Steven): the
        // window is never taller than the screen, so past that, one scroll area below the summary holds the pickups and
        // every table (they no longer scroll on their own). A wheel over a table scrolls the whole area.
        MaxHeight = SystemParameters.WorkArea.Height;
        foreach (var inner in new[] { DropsScroll, GatherScroll, MobScroll, PetScroll, CraftScroll })
            inner.PreviewMouseWheel += (_, ev) =>
            {
                ev.Handled = true;
                LowerScroll.RaiseEvent(new MouseWheelEventArgs(ev.MouseDevice, ev.Timestamp, ev.Delta) { RoutedEvent = MouseWheelEvent, Source = LowerScroll });
            };
        ShowPart(_prefs.Part, remember: false);
        SizeChanged += (_, _) => KeepOnScreen();
        ShowTable(null);
        ServerRowState();
        InitWelcome();
        if (!HasServer) Say("Choose your region and server first: prices and drops are kept per server.", "Warn");
        else Say("Reading the chat whenever the game is open. Start run times a stretch on top.", "Faint");
        if (watch) _watcher.Start();
    }

    /// <summary>
    /// Once the chat and the zone have been found, steps 1 and 2 go behind the Settings button and the window
    /// shrinks to the mode picker, the tiles and the table. Settings brings the setup card back at any time.
    /// </summary>


    /// <summary>The main window is the live count only; everything that needs setting is in Options. Kept for layout renders.</summary>
    public void ShowCompact() => SetView(false);
    /// <summary>Full view, for layout renders.</summary>
    public void ShowFull() => SetView(true);






    /* ---------------- actions ---------------- */

    /// <summary>The label a run starts under; it is saved and shared under what it turned out to be.</summary>
    string ActivityName() => _mode == Mode.Gather ? "Gathering" : "Open world";

    /// <summary>
    /// Open world rates drops per kill. Gathering has no Kinah and no kills: the unit is the node. Which one is read from
    /// the chat (kills or gathers in the last few minutes), so the stats follow what the player is doing.
    /// Soul Crystals never enter either table; the summary line counts them.
    /// </summary>
    Mode _mode;

    void ApplyModeLabels()
    {
        var m = _mode;
        (Tile1Label.Text, Tile2Label.Text, Tile3Label.Text, Tile4Label.Text) = m switch
        {
            Mode.Gather => ("attempts/hr", "items/hr", "per try", "rare/hr"),
            _ => ("Kinah/hr", "Kinah/kill", "kills/hr", "drops/hr"),
        };
        // the fifth tile: what the hour is worth if the tradeable items are sold on the Market (plus the Kinah, in open world)
        Tile5Label.Text = m == Mode.Gather ? "value/hr" : "total/hr";
        if (!Farming && _watcher.Chat == null && !_demo) ShowTable(null);
    }


    /// <summary>Redraws the table for the count on screen: the run, the run just stopped, or the live count.</summary>
    /// <summary>"Looks wrong?" on a drop or gathered row: flags the item so nothing about it is sent to the site; "Undo" takes the flag off.</summary>
    void ReportClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkContentElement)?.DataContext is not DropRow r || r.Report.Length == 0) return;
        bool on = ReportedDrops.Toggle(r.Report);
        CrashLog.Note(on ? $"reported: {r.Report} (not sent to the site)" : $"report undone: {r.Report}");
        Say(on ? $"{r.Report} won't be sent to the site. Thanks for flagging it." : $"{r.Report} counts again.", "Faint");
        Redraw();
    }

    void Redraw()
    {
        var e = Shown();
        if (_demo && _demoEngine != null) { FillNumbers(_demoEngine, _demoElapsed); FillPickups(); return; }   // a layout render keeps its sample
        if (e != null) FillNumbers(e, e.Active); else { ShowTable(null); _hasSummary = false; SummaryCard.Visibility = Visibility.Collapsed; }
        FillPickups();
    }

    /// <summary>One line of the recent pickups strip; Tip says, in plain words, how it was read (on hover).</summary>
    public sealed record PickupRow(string Time, string What, string Tip);

    /// <summary>The reader's "chat · recovered" and the like, as a sentence a player can use.</summary>
    static string ViaTip(string via)
    {
        var parts = new List<string>();
        foreach (var v in via.Split('·', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            parts.Add(v switch
            {
                "chat" => "Read in the chat.",
                "recovered" => "Read again after the line scrolled away or came out blurred.",
                "guessed" => "Part of the name was hidden, so it was matched to the closest item. Hover the item's row and press Looks wrong? if it's not right.",
                "loot list" => "Only the loot list beside your character showed it; the chat didn't.",
                _ => char.ToUpperInvariant(v[0]) + v[1..] + ".",
            });
        return string.Join(" ", parts);
    }

    /// <summary>The recent pickups strip: the last three, newest first, on the Farming part while something has been counted.</summary>
    void FillPickups()
    {
        var list = _demo
            ? new List<PickupRow>
              {
                  new("17:51:12", "Spiritstone ×1", ViaTip("chat · recovered")), new("17:51:11", "1,192 Kinah", ViaTip("chat · recovered")),
                  new("17:51:09", "431 Kinah", ViaTip("chat")),
              }
            : (_watcher.Chat?.RecentPickups(3) ?? new()).Select(p => new PickupRow(p.At.ToLocalTime().ToString("HH:mm:ss"), p.What, ViaTip(p.Via))).ToList();
        PickupRows.ItemsSource = list;
        PickupStrip.Visibility = list.Count > 0 && _part == "Farming" && !_welcome && _prefs.FullView ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// The count the window shows: this zone since you arrived (or since the run started), unless All zones is on; then the
    /// run, or the live count. A run just stopped shows for a few seconds either way.
    /// </summary>
    SessionEngine? Shown()
    {
        var chat = _watcher.Chat; var farm = _watcher.Farm;
        if (farm == null && _lastRun != null && DateTime.UtcNow < _lastRunUntil) return _lastRun;
        if (!_prefs.AllZones && chat != null) return chat.ZoneView;
        return farm ?? chat?.Engine;
    }

    string ViewTitle() => _watcher.Farm != null ? (_prefs.AllZones ? "this run" : "this run · this zone") : (_prefs.AllZones ? "counted while the game is open" : "this zone");

    void ResetTiles() { KinahHr.Text = "0"; KinahKill.Text = _mode == Mode.Open ? "—" : "0"; KillsHr.Text = "0"; DropsHr.Text = "0"; ValueHr.Text = "—"; }

    async Task Toggle() { if (_welcome) { Say("Choose your server first.", "Warn"); return; } if (!Farming) await StartTracking(); else await StopTracking(); }

    /// <summary>Says something in the status line and on the Options window's reading page when it is open.</summary>
    void Report(string text, string tone)
    {
        Say(text, tone);
        _options?.ShowNote(text, tone);
    }

    /// <summary>Drag a box around the chat by hand. The Options window hides itself around this.</summary>
    public void PickChat()
    {
        bool was = IsVisible;
        Hide();
        try { var r = RegionPickerWindow.Pick(); if (r != null) { _region = r; _prefs.ManualChat = true; RegionStore.Save(r.Value); _watcher.ManualChat = r; _ = _watcher.ResetChatAsync(); SetPreview(null); } }
        finally { if (was) { Show(); Activate(); } }
        if (_region != null && _prefs.ManualChat) Report("Chat area chosen by hand. The zone is still read from the minimap if the game window is found. Press Test read, then Start.", "Good");
    }

    /// <summary>Back to finding the chat on its own.</summary>
    public void ResetChat()
    {
        _region = null; _prefs.ManualChat = false; RegionStore.Clear(); _watcher.ManualChat = null; _ = _watcher.ResetChatAsync();
        SetPreview(null);
        Report("Back to automatic: the chat is found when you start.", "Faint");
    }

    /// <summary>Finds the game, the chat and the zone right now and shows what it found, without starting a run.</summary>
    public async Task DetectNow()
    {
        Report("Looking for the game window, the chat and the zone…", "Faint");
        try
        {
            var game = GameWindow.Find(_settings.ProcessHints);
            if (game == null) { Report("The game window wasn't found. Start the game in borderless or windowed mode, or choose the chat yourself.", "Warn"); return; }
            if (_hud != null) { _hud.Hide(); }   // never read our own panel
            if (GameWindow.Covered(ChatLocator.Corner(game.Value), _settings.ProcessHints) is { } cover)
            {
                Report($"The chat corner of the game is out of view: {cover} is over it. Move it off the bottom-left of the game, then Detect again.", "Warn");
                return;
            }
            var (region, note) = await Task.Run(() => ChatLocator.DetectAsync(game.Value));
            var (zone, _) = await Task.Run(() => ZoneLocator.DetectAsync(game.Value));
            ZoneLbl.Text = "Zone: " + (zone ?? "not read yet");
            var g = game.Value;
            var marks = region != null ? new[] { (region.Value, TrackerSession.ChatColor), (ZoneLocator.Strip(g), TrackerSession.ZoneColor) } : new[] { (ZoneLocator.Strip(g), TrackerSession.ZoneColor) };
            SetPreview(await Task.Run(() => ScreenReader.Frame(g, 480, marks)));
            if (region == null)
            {
                Report("Couldn't see the chat (" + note + "). Open the chat so its input box shows (press Enter once, or pin the chat window in the game's chat settings), then Detect again, or choose the chat yourself.", "Warn");
                return;
            }
            _region = region; _prefs.ManualChat = false; RegionStore.Clear();
            ShowFix(false);
            Report($"Found the chat ({note}){(zone != null ? " and the zone" : "")}. Press Start whenever you're ready.", "Good");
        }
        catch (Exception ex) { Report(ex.Message, "Bad"); }
    }

    public async Task TestRead()
    {
        if (_region == null) { await DetectNow(); if (_region == null) return; }
        Report("Reading…", "Faint");
        try
        {
            if (GameWindow.Covered(_region.Value, _settings.ProcessHints) is { } cover)
            {
                Report($"The chat is out of view: {cover}{(cover.StartsWith("the game") ? "" : " is over it")}. Only the game is read; bring it into view, then Test read again.", "Warn");
                return;
            }
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
            if (lines.Count == 0) Report("Nothing recognized. Use borderless or windowed mode and make sure the box covers the chat text.", "Warn");
            else if (hits == 0) Report($"{lines.Count} line(s) read but none look like loot. Loot something first, or try a bigger chat font or a tighter box.", "Warn");
            else Report($"Reads fine: {hits} loot or kill line(s) out of {lines.Count}. Ready to start.", "Good");
        }
        catch (Exception ex) { Report(ex.Message, "Bad"); }
    }

    async Task StartTracking()
    {
        SavePrefs();
        if (Server is not { } server) { Say("Choose your region and server first: prices and drops are kept per server.", "Warn"); ServerRowState(); return; }
        StartBtn.IsEnabled = false;
        Say(_watcher.Chat == null ? "Finding the chat and the zone…" : "Starting…", "Faint");
        try
        {
            _settings.Upload.Enabled = _prefs.Share;
            var chat = await _watcher.EnsureChatAsync();
            _region = chat.Region;
            var farm = _watcher.StartFarm(ActivityName());
            _lastRun = null;
            ZoneLbl.Text = chat.Zone ?? "Zone not read yet";
            EnsureHud();
            _tableWhat = ViewTitle();
            Redraw();
            SetTrackingUi(true);
            ShowFix(false);
            Say($"Run started · {chat.Zone ?? "zone not read yet"}. Minimize this window; the panel and {(HotkeyText.Length > 0 ? HotkeyText : "the tray")} keep working.", "Faint");
        }
        catch (Exception ex)
        {
            StartBtn.IsEnabled = true;
            ShowFix(true);   // the fix is one click away, in Options › Game reading
            Say(ex.Message, "Warn");
        }
    }

    async Task StopTracking()
    {
        var e = _watcher.StopFarm(); if (e == null) return;
        // shared and saved under what the run turned out to be, not the label picked before it started
        var chat = _watcher.Chat; var kind = e.Events > 0 ? GameWatcher.DominantKind(e) : _watcher.FarmKind; var activity = TrackerSession.ActivityOf(e, kind);
        StartBtn.IsEnabled = false; Say("Stopping…", "Faint");
        // closing the panel hands focus to whatever is behind it, usually the game, which then covers this window
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Show(); Activate(); Topmost = true; Topmost = false;
        // the window goes straight back to the live count (the player's call: Stop must not look like counting stopped);
        // the run's result stays on the status line for a few seconds and in the history
        _lastRun = null; _lastRunUntil = DateTime.MinValue;
        var t = e.Totals();
        string ran = $"{e.Elapsed:hh\\:mm\\:ss}" + (t.Kinah > 0 ? $" · {Fk(t.Kinah)} Kinah" : "") + (t.Kills > 0 ? $" · {t.Kills} kills" : "") + (t.Nodes > 0 ? $" · {t.Nodes} attempts" : "") + (t.Drops > 0 ? $" · {t.Drops} items" : "");
        string msg, tone = "Faint"; bool shared = false; bool eligible = e.Elapsed.TotalMinutes >= 5 && e.Events > 0;
        if (!eligible) msg = $"Run stopped ({ran}). Under 5 minutes or no loot seen, so it isn't shared.";
        else if (!_prefs.Share) msg = $"Run stopped ({ran}). Sharing is off, so it stays on this PC.";
        else if (string.IsNullOrWhiteSpace(_settings.Upload.Endpoint)) msg = $"Run stopped ({ran}). No sharing address in settings.json, so it stays on this PC.";
        else if (chat != null && await chat.UploadAsync(_settings, e, kind, activity)) { msg = $"Run stopped ({ran}) and shared anonymously."; tone = "Good"; shared = true; }
        else { msg = $"Run stopped ({ran}). Not shared: " + (Upload.Uploader.LastError.Length > 0 ? Upload.Uploader.LastError : "couldn't reach the site") + ". Kept on this PC."; tone = "Warn"; }
        if (e.Events > 0)
        {
            SessionStore.Append(e, _items, activity, _settings, shared, Server?.Label ?? "");
            try { BestRuns.OfferRun(Server?.Label ?? "", e, _items); } catch (Exception ex) { CrashLog.Write("best runs", ex); }
            msg += $" Saved to your history ({SessionStore.Count()} runs). Still counting.";
        }
        SetTrackingUi(false);
        Say(msg, tone);
        _statusHoldUntil = DateTime.UtcNow.AddSeconds(15);
        Tick();
    }

    /// <summary>A message the live status line mustn't overwrite yet (a stopped run's result).</summary>
    DateTime _statusHoldUntil;

    void SetTrackingUi(bool on)
    {
        StartBtn.Content = on ? "Stop run" : "Start run";
        RecDot.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        StartBtn.IsEnabled = on || HasServer;
        _trayToggle.Text = on ? "Stop run" : "Start run";
        _tray.Text = on ? "Kinah Ledger · farming" : "Kinah Ledger";
    }

    void Tick()
    {
        if (_demo) return;
        WatchGame();
        var chat = _watcher.Chat; var farm = _watcher.Farm;
        var show = Shown();
        if (show != null && (farm != null || show != _lastRun))
        {
            // the tiles follow what the player is doing now (kills or gathers in the last few minutes), in a run or not:
            // a stretch of gathering shows attempts per hour, mob farming Kinah per hour
            var dom = show.RecentKind is { } rk ? (rk == "Gathering" ? Mode.Gather : Mode.Open) : show.Events > 0 ? ModeOf(show) : _mode;
            if (dom != _mode) { _mode = dom; ApplyModeLabels(); }
            if (show != _lastRun) _tableWhat = ViewTitle();
            FillNumbers(show, show.Active);
        }
        else if (chat == null) { TableTitle.Text = _watcher.GameRect == null ? "Not reading yet" : "Looking for the chat"; HeadRate.Text = ""; HeadRateLbl.Visibility = Visibility.Collapsed; }
        string verb = _mode == Mode.Gather ? "Gathering" : "Farming";
        if (farm != null) FarmTime.Text = $"{verb} for " + farm.Elapsed.ToString(@"hh\:mm\:ss") + (farm.Paused ? " · paused while the game is out of view" : "");
        else if (chat != null) FarmTime.Text = "Counting since " + _watcher.ChatSince.ToLocalTime().ToString("HH:mm");
        else FarmTime.Text = "Reading starts on its own once the game is open";
        // the zone is the run card's title, as the site's "best place" card names its zone
        ZoneLbl.Text = chat?.Zone ?? (_watcher.GameRect == null ? "Waiting for the game" : "Zone not read yet");
        // with no zone on the list, what the minimap corner said, so a zone the list doesn't have yet can be named and added
        ZoneLbl.ToolTip = chat?.Zone == null && chat?.ZoneNote is { Length: > 0 } zn ? zn + "\n\nA zone has to be on the tracker's zone list to count. Send the name shown here to have it added." : null;
        if (DateTime.UtcNow < _statusHoldUntil) { /* a stopped run's result is still showing */ }
        else if (chat == null) Say(_watcher.GameRect == null ? "Waiting for the game. Reading starts on its own once the game is open." : "Looking for the chat… " + _watcher.ChatNote, "Faint");
        else if (chat.LastError.Length > 0) Say("Read problem: " + chat.LastError, "Bad");
        else if (chat.Paused.Length > 0) Say($"Paused: {chat.Paused}. Only the game is read, so reading picks up when the chat is in view again · " + MarketShort(), "Warn");
        else
        {
            // one plain line; the reader's details (reads, Market, the Kinah counter, uploads) are on hover
            Say((farm != null ? "Run on · reading the chat" : "Reading the chat") + (chat.LastLine.Length > 0 ? " · last: " + chat.LastLine : ""), "Faint");
            Status.ToolTip = $"{chat.Reads:N0} chat reads so far.\n{MarketShort()}.\n{BalanceShort()}." + (_watcher.Flushed > 0 ? $"\n{_watcher.Flushed} batch{(_watcher.Flushed == 1 ? "" : "es")} shared with the site." : "");
        }
        if (chat != null && _options is { WantsPreview: true } && ++_ticks % 4 == 0 && !_framing)
        {
            // the whole game with the chat, map and (when open) Market outlined, refreshed every couple of seconds off the UI thread
            _framing = true;
            var marks = MarketMarks();
            _ = Task.Run(() => chat.Frame(480, marks)).ContinueWith(t => Dispatcher.Invoke(() => { _framing = false; if (t.Status == TaskStatus.RanToCompletion) SetPreview(t.Result); }));
        }
        // the panel over the game shows whenever its switch is on and the chat is being read: the run while one is on, the
        // always-on count otherwise. It was a farming-run thing before 0.7.1, which left the overlay missing most of the time.
        if (_prefs.Hud && chat != null) EnsureHud();
        if (_part == "Crafting") FillCraft(chat?.CraftView);
        FillPickups();
        UpdateHud();
    }

    /// <summary>The figures the panel over the game can show, from the last FillNumbers, so the panel and the window always agree.</summary>
    readonly Dictionary<string, string> _hudValues = new();

    /// <summary>The panel over the game, with the figures the window worked out last.</summary>
    void UpdateHud()
    {
        if (_hud == null) return;
        _hudValues["last"] = _demo ? "Spiritstone ×1" : _watcher.Chat?.RecentPickups(1).FirstOrDefault()?.What ?? "";
        _hud.Update(_hudValues);
    }

    /// <summary>The panel's figures, for layout renders of the panel.</summary>
    public IReadOnlyDictionary<string, string> HudValues => _hudValues;

    /// <summary>The Kinah balance counter in a few words for the status line: still looking for it, or the last good read.</summary>
    string BalanceShort()
    {
        var b = _watcher.Balance.Tracker;
        return b.Balance is long v ? $"Kinah {v:N0}" : b.KinahLeft != null ? "Kinah counter found" : "Kinah counter: pick up Kina to find it";
    }

    /// <summary>The Market in a few words for the status line.</summary>
    string MarketShort()
    {
        var m = _watcher.Market;
        if (!m.Enabled) return "Market: off with sharing";
        if (m.Detected) return $"Market: open · {m.AsksSeen:N0} asks";
        return m.AsksSeen > 0 ? $"Market: {m.AsksSeen:N0} asks · {m.Shared:N0} shared" : "Market: watching";
    }

    /// <param name="Value">What the tradeable items would sell for on the Market, after its cut; bound items can't be sold.</param>
    /// <param name="Priced">Tradeable items with a price; <paramref name="Unpriced"/> those without, left out of the value.</param>
    /// <param name="Gross">What the priced tradeable items sell for before the Market's fee and tax.</param>
    sealed record Tables(List<DropRow> Drops, List<DropRow> Gathered, long DropCount, long GatherCount, long Value, int Priced, int Unpriced, List<DropRow> Mobs, long Gross = 0, List<DropRow>? Pets = null);

    // what the card's head says after the activity and the clock: "this run", "this zone", ...
    string _tableWhat = "counted while the game is open";

    /// <summary>
    /// Splits the session into mob drops (rated per kill) and gathered materials grouped by skill (each skill rated
    /// against its own attempts). Soul Crystals stay out of both: the summary line counts them.
    /// </summary>
    Tables Build(SessionEngine e, double hours, int kills)
    {
        var drops = new List<DropRow>(); long dc = 0, gc = 0, value = 0, gross = 0; int priced = 0, unpriced = 0;
        // the enemies fought in this view and their loot tables: odds count only kills of enemies that can drop the item
        var ms = e.MobSnapshot();
        var book = LootBook.Current;
        var tabled = ms.Where(m => book.Knows(m.Name)).ToList();
        var bySkill = new Dictionary<GatherFamily, List<(string name, int total)>>();
        var bySkillGrade = new Dictionary<string, string>();
        var worth = new Dictionary<string, string>();
        var worthN = new Dictionary<string, long>();
        foreach (var r in e.ItemRows())
        {
            int total = r.Count + r.Bound;
            // a row's value is its tradeable copies at the lowest Market price, before the fee; bound copies can't be sold
            string rowValue = r.Count > 0 ? "—" : "";
            if (r.Count > 0)
            {
                var price = PriceBook.Price(r.Name);
                if (price != null) { long g0 = price.Value * r.Count; gross += g0; value += (long)Math.Round(g0 * PriceBook.SellerKeeps); priced++; rowValue = g0.ToString("N0"); worthN[r.Name] = g0; } else unpriced++;
            }
            worth[r.Name] = rowValue;
            var g = GatheringCatalog.Find(r.Name);
            var grade = g?.Grade ?? _items.Get(r.Id)?.Grade ?? "";
            if (g != null) { gc += total; if (!bySkill.TryGetValue(g.Skill, out var list)) bySkill[g.Skill] = list = new(); list.Add((r.Name, total)); bySkillGrade[r.Name] = grade; }
            else
            {
                dc += total;
                // under the name, what kind of item it is, as the site shows it ("Drop" when the item list has no category)
                var cat = _items.Get(r.Id)?.Category is { Length: > 0 } c ? c : "Drop";
                // under the name only what it is; where it came from and how it was read are on hover (0.7.6)
                var sub = r.Count == 0 ? cat + " · bound" : r.Bound > 0 ? $"{cat} · {r.Bound} bound" : cat;
                var tips = new List<string>();
                var odds = Odds(total, kills); var tone = "";
                if (tabled.Count > 0)
                {
                    // the kills of every enemy fought that can drop the item, pooled (LootBook.Pool): leather doesn't come
                    // from a rock, so rocks don't count; two enemies that both drop Fresh Raw Meat both do
                    var (eligible, from, listed) = book.Pool(ms.Select(m => (m.Name, m.Kills, m.Drops.ContainsKey(r.Id))), r.Name, r.Id);
                    if (from.Count > 0 && eligible > 0)
                    {
                        odds = Odds(total, eligible);
                        var names = string.Join(", ", from.Take(3)) + (from.Count > 3 ? " and others" : "");
                        tips.Add($"Drops from {names}. The odds count only their {eligible:N0} kill{(eligible == 1 ? "" : "s")}." + (listed is double lc ? $" The game lists it at {OneIn(lc)}." : ""));
                    }
                    else if (tabled.Count == ms.Count && book.SourcesOf(r.Name).Count > 0)
                    {
                        // every enemy fought has a table, none lists it, and some other enemy's does: a misread, or a drop
                        // credited to the wrong fight. An item on no table at all is one the tables don't cover: no warning
                        odds = "—";
                        tips.Add("None of the enemies you fought here lists this item. It may have been misread, or come from another fight. Press Looks wrong? to keep it off the site.");
                    }
                }
                bool flagged = ReportedDrops.Has(r.Name);
                // copies the chat never confirmed (the loot list beside the character, or a guessed name): counted, and said on hover
                if (e.Unconfirmed(r.Id) is int unc && unc > 0) tips.Add($"{unc} of these came from the loot list beside your character or a partly hidden name, not a clear chat line.");
                if (PriceTip(r.Name) is { Length: > 0 } pt) tips.Add(pt);
                if (flagged) { sub = "Reported · not sent to the site"; tone = "Warn"; }
                drops.Add(new DropRow(r.Name, total.ToString("N0"), odds, Rate(total / hours), Grade: grade, Icon: Slot(r.Id), Initial: InitialOf(r.Name), Price: PriceText(r.Name), Value: rowValue, Sub: sub, Tone: tone, Report: r.Name, Reported: flagged,
                    Tip: string.Join("\n\n", tips), Worth: worthN.GetValueOrDefault(r.Name)));
            }
        }
        // Soul Crystals come from kills too: their own section at the bottom, never mixed into the item rows or the drop count
        var petRows = new List<DropRow>();
        foreach (var (name, n) in e.PetSnapshot().OrderByDescending(x => x.Value))
            petRows.Add(new DropRow(name, n.ToString("N0"), "", Rate(n / hours), Initial: InitialOf(name), Sub: "Soul Crystal"));
        var gathered = new List<DropRow>();
        var stats = e.SkillSnapshot();
        foreach (var skill in GatherSkills.Order)
        {
            if (!bySkill.TryGetValue(skill, out var items) && !stats.ContainsKey(skill)) continue;
            var st = stats.GetValueOrDefault(skill) ?? new SessionEngine.SkillStats();
            int ok = st.Nodes - st.Failed;
            // the skill, its attempts and the hour's pace; failed and plentiful gathers on hover
            var head = $"{GatherSkills.Label(skill).ToUpperInvariant()}  ·  {st.Nodes} attempt{(st.Nodes == 1 ? "" : "s")}"
                     + (st.Nodes > 0 ? $"  ·  {Rate(st.Nodes / hours)}/hr" : "");
            var headTip = (st.Failed > 0 ? $"{st.Failed} of the attempts failed. " : "") + (ok > 0 && st.Plentiful > 0 ? $"{Math.Round(100.0 * st.Plentiful / ok)}% of the successful ones were plentiful (extra yield). " : "")
                        + "Every gather line counts as one attempt, failed ones too.";
            gathered.Add(new DropRow(head, "", "", "", Header: true, Tip: headTip));
            foreach (var (name, total) in (items ?? new()).OrderByDescending(x => x.total))
            {
                bool rep = ReportedDrops.Has(name);
                var gtips = new List<string>();
                if (e.Unconfirmed(_items.IdOf(name)) is int gu && gu > 0) gtips.Add($"{gu} of these came from the loot list beside your character or a partly hidden name, not a clear chat line.");
                if (PriceTip(name) is { Length: > 0 } gpt) gtips.Add(gpt);
                gathered.Add(new DropRow(name, total.ToString("N0"), st.Nodes > 0 ? Pct(total / (double)st.Nodes) : "—", Rate(total / hours), Grade: bySkillGrade.GetValueOrDefault(name, ""), Icon: Slot(_items.IdOf(name)), Initial: InitialOf(name), Price: PriceText(name), Value: worth.GetValueOrDefault(name, ""),
                    Sub: rep ? "Reported · not sent to the site" : "Gathering material", Tone: rep ? "Warn" : "", Report: name, Reported: rep, Tip: string.Join("\n\n", gtips), Worth: worthN.GetValueOrDefault(name)));
            }
        }
        // Kinah by enemy, a best guess: most Kinah first. The zone goes under the name when the view spans more than one.
        var mobs = new List<DropRow>();
        bool zonesToo = ms.Select(m => m.Zone).Distinct().Count() > 1;
        foreach (var m in ms)
        {
            var key = m.Zone + "|" + m.Name;
            bool open = _openMobs.Contains(key), known = book.Knows(m.Name);
            var where = zonesToo && m.Zone is { Length: > 0 } mz ? mz + " · " : "";
            // every enemy opens: its loot table when one is known, and always what it dropped for you
            mobs.Add(new DropRow(m.Name, m.Kills.ToString("N0"), m.Kills > 0 ? Fk(m.Kinah / (double)m.Kills) : "—", Fk(m.Kinah / hours), Initial: InitialOf(m.Name), Icon: IconCache.Get(MobImages.Url(m.Name)), Value: m.Kinah.ToString("N0"),
                Sub: where + (open ? "▾ Hide drops" : "▸ Show drops"), Key: key,
                Tip: "A best guess: the Kinah that landed just after the target bar named this enemy. Click to " + (open ? "hide" : "show") + " its drops."));
            if (!open) continue;
            // what you got against the listed chance, what hasn't dropped yet last; then drops the table doesn't list
            var table = known ? book.TableOf(m.Name).ToList() : new();
            bool OnTable(long id) => table.Any(en => (en.Id is long tid && tid == id) || string.Equals(_items.NameOf(id), en.Item, StringComparison.OrdinalIgnoreCase));
            var rows = table.Select(en =>
            {
                int got = m.Drops.Where(d => (en.Id is long id && id == d.Key) || string.Equals(_items.NameOf(d.Key), en.Item, StringComparison.OrdinalIgnoreCase)).Sum(d => d.Value);
                return (en, got);
            }).OrderByDescending(x => x.got > 0).ThenByDescending(x => x.en.Chance ?? 0).ToList();
            foreach (var (en, got) in rows)
            {
                long iid = en.Id ?? _items.IdOf(en.Item);
                mobs.Add(new DropRow(en.Item, got > 0 ? got.ToString("N0") : "", got > 0 ? Odds(got, m.Kills) : "not yet", "", Grade: _items.Get(iid)?.Grade ?? "", Icon: Slot(iid), Initial: InitialOf(en.Item),
                    Sub: en.Chance is double c && c > 0 ? $"listed {OneIn(c)}" : "listed", Child: true));
            }
            foreach (var (id, got) in m.Drops.Where(d => !OnTable(d.Key)).OrderByDescending(d => d.Value))
            {
                var name = _items.NameOf(id);
                mobs.Add(new DropRow(name, got.ToString("N0"), Odds(got, m.Kills), "", Grade: _items.Get(id)?.Grade ?? "", Icon: Slot(id), Initial: InitialOf(name),
                    Sub: known ? "not on its listed table" : "dropped for you", Child: true));
            }
            if (m.Crystals > 0)
                mobs.Add(new DropRow("Soul Crystals", m.Crystals.ToString("N0"), Odds(m.Crystals, m.Kills), "", Initial: "S", Sub: "from this enemy", Child: true));
            if (!known && m.Drops.Count == 0 && m.Crystals == 0)
                mobs.Add(new DropRow("No item drops yet", "", "", "", Sub: "only Kinah so far · its loot table isn't in the list yet", Child: true));
        }
        return new Tables(drops, gathered, dc, gc, value, priced, unpriced, mobs, gross, petRows);
    }

    /// <summary>Sections showing every row, by name ("Drops", "Gather", "Mob", "Pet"); the others show their top rows.</summary>
    readonly HashSet<string> _allRows = new();

    /// <summary>
    /// A section's rows as shown: the first <paramref name="top"/> rows that count (not group headers, not an open loot
    /// table's rows), with the headers and loot-table rows that belong to them, unless "show all" is on. The link under
    /// the section says how many more there are, or offers to show fewer.
    /// </summary>
    List<DropRow> TopRows(string section, List<DropRow> rows, int top, System.Windows.Controls.TextBlock link, System.Windows.Documents.Run text)
    {
        int counted = rows.Count(r => !r.Header && !r.Child);
        bool all = _allRows.Contains(section);
        link.Visibility = counted > top ? Visibility.Visible : Visibility.Collapsed;
        text.Text = all ? "Show fewer" : $"Show all {counted}  ·  {counted - top} more";
        if (all || counted <= top) return rows;
        var shown = new List<DropRow>(); int n = 0;
        for (int i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            if (!r.Header && !r.Child) { if (n == top) break; n++; }
            shown.Add(r);
        }
        // a group header left with nothing under it is dropped
        while (shown.Count > 0 && shown[^1].Header) shown.RemoveAt(shown.Count - 1);
        return shown;
    }

    /// <summary>Enemy rows whose loot table is open, by "zone|name"; a click on the row opens or closes it.</summary>
    readonly HashSet<string> _openMobs = new();

    /// <summary>A listed chance as the Odds column reads: 0.2 → "1 in 5", or "20%" with the column switched to percentages.</summary>
    static string OneIn(double chance) => chance >= 1 ? "every kill" : OddsAsPct ? Pct(chance) : $"1 in {Math.Max(1, Math.Round(1 / chance)):N0}";

    /// <summary>True when the Odds column reads as percentages; the header switches it (UserPrefs.OddsAsPercent).</summary>
    static bool OddsAsPct;

    void ShowOddsHeader()
    {
        PctHeader.Text = "ODDS ⇄";   // the column is narrow; the numbers themselves show which way it reads
        PctHeader.ToolTip = (OddsAsPct ? "Drops per kill as a percentage" : "How many kills one drop takes")
            + ", counting only kills of enemies that can drop the item. Click to show it as " + (OddsAsPct ? "\"1 in x\"." : "a percentage.");
    }

    // the drops' Market value and how many items carried a price, from the last FillNumbers, for the panel over the game
    long _lastValue; int _lastPriced;

    /// <summary>The lowest Market price for an item on this server, as the Price column shows it; "≈" when it is the region's price, a dash until one is known.</summary>
    static string PriceText(string name) => PriceBook.Price(name) is long p ? (PriceBook.SourceOf(name) == PriceBook.From.Region ? "≈" : "") + p.ToString("N0") : "—";

    /// <summary>A line for the row's hover when the price is not the server's own: the region's middle price stands in.</summary>
    string PriceTip(string name) => PriceBook.SourceOf(name) == PriceBook.From.Region
        ? $"≈ Site price: nobody has listed it on {Server?.Label ?? "your server"} yet, so this is the middle price on the other {Server?.Faction ?? ""} servers in {Server?.Region.Name ?? "your region"}."
        : "";

    void FillNumbers(SessionEngine e, TimeSpan elapsed)
    {
        double h = Math.Max(elapsed.TotalHours, 1.0 / 3600);
        var t = e.Totals(); long kinah = t.Kinah;   // farming Kinah only; bound Kinah is reward money
        var s = Build(e, h, t.Kills);
        string worth = s.Value > 0 ? $" · worth {Fk(s.Value)} ({Fk(s.Value / h)}/hr)" : "";
        // quest and payment rewards: bound Kinah, a payment line, or one line far above what the mobs pay. Never farming income.
        string skipped = t.Rewards > 0 ? $" · {t.Rewards} reward line{(t.Rewards == 1 ? "" : "s")} skipped" + (e.KinahSkipped > 0 ? $" ({Fk(e.KinahSkipped)} Kinah)" : "") : "";
        string crystals = t.Pets > 0 ? $" · {t.Pets} soul crystal{(t.Pets == 1 ? "" : "s")}" : "";
        // the clock is farming time; a minute or more of readable time with no loot is shown as idle beside it
        string idle = e.Idle >= TimeSpan.FromMinutes(1) ? $"{Clock(e.Idle)} idle (not counted in any rate)" : "";
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
                _ = fails; _ = plent; _ = worth; _ = crystals;
                break;
            }
            default:
                KinahHr.Text = Fk(kinah / h);
                KinahKill.Text = t.Kills > 0 ? Fk(kinah / (double)t.Kills) : "—";
                KillsHr.Text = Fk(t.Kills / h);
                DropsHr.Text = Fk(s.DropCount / h);
                _ = worth; _ = crystals;
                break;
        }
        // the hour's worth if the tradeable items are sold at the lowest Market price, after the Market's cut; open world adds the Kinah
        _lastValue = s.Value; _lastPriced = s.Priced;
        long worthHr = (long)((s.Value + (_mode == Mode.Gather ? 0 : kinah)) / h);
        ValueHr.Text = s.Priced > 0 || (_mode != Mode.Gather && kinah > 0) ? Fk(worthHr) : "—";
        ValueTile.ToolTip = (s.Priced + s.Unpriced == 0 ? "No tradeable items yet."
                : s.Priced == 0 ? "No Market price yet for any of these items. Open the Market in the game once: the prices you see there value your drops, and the site's price board fills in the rest."
                : $"{s.Priced} of {s.Priced + s.Unpriced} tradeable items priced, at the lowest Market price, keeping {PriceBook.SellerKeeps:P0} after the Market's fee and tax. Bound items can't be sold and aren't counted."
                  + (s.Unpriced > 0 ? " Open the Market and look at the others to price them too." : ""))
            + (_mode == Mode.Gather ? "" : " Kinah per hour is included.");
        // the card's head: what and how long, then the hour's total (Kinah plus the loot after the Market's cut)
        TableTitle.Text = $"{(ZoneLocator.KindOf(e.Zone) ?? (_mode == Mode.Gather ? "Gathering" : "Open world"))} · {Clock(elapsed)}";
        TableWhat.Text = _tableWhat;
        TableWhatChip.Visibility = _tableWhat.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        long totalNow = kinah + s.Value;
        HeadRate.Text = totalNow > 0 ? $"{Fk(totalNow / h)}/hr" : "";
        HeadRateLbl.Visibility = totalNow > 0 ? Visibility.Visible : Visibility.Collapsed;
        Summary.Text = string.Join(" · ", new[] { idle, skipped.TrimStart(' ', '·') }.Where(x => x.Length > 0));
        Summary.Visibility = Summary.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        // the summary card: the session total, how it adds up in one line (the whole sum on hover), the three best drops
        // only what happened: a gathering stretch doesn't say "0 kills · 0 drops"
        FootTotalNote.Text = string.Join(" · ", new[]
        {
            t.Kills > 0 ? $"{t.Kills} kill{(t.Kills == 1 ? "" : "s")}" : "", s.DropCount > 0 ? $"{s.DropCount} drop{(s.DropCount == 1 ? "" : "s")}" : "",
            s.GatherCount > 0 ? $"{s.GatherCount} gathered" : "", t.Pets > 0 ? $"{t.Pets} Soul Crystal{(t.Pets == 1 ? "" : "s")}" : "",
        }.Where(x => x.Length > 0));
        _hasSummary = e.Events > 0;
        SummaryCard.Visibility = _hasSummary && _part == "Farming" && !_welcome ? Visibility.Visible : Visibility.Collapsed;
        ShareCardBtn.IsEnabled = totalNow > 0;
        FootTotal.Text = totalNow.ToString("N0");
        SumBreakdown.Text = (kinah > 0 || _mode != Mode.Gather ? $"{Fk(kinah)} Kinah" : "") + (kinah > 0 || _mode != Mode.Gather ? "  +  " : "")
            + (s.Priced > 0 ? $"{Fk(s.Value)} loot after the Market's {1 - PriceBook.SellerKeeps:P0}" : "loot not priced yet")
            + (s.Unpriced > 0 ? $"  ·  {s.Unpriced} item{(s.Unpriced == 1 ? "" : "s")} without a price" : "");
        SumBreakdown.ToolTip = $"Kinah picked up: {kinah:N0}, from {t.Kills:N0} kill{(t.Kills == 1 ? "" : "s")}. Quest rewards and bound Kinah are left out.\n"
            + (s.Priced > 0 ? $"Loot at the lowest Market price: {s.Gross:N0}.\nMarket fee and tax, {1 - PriceBook.SellerKeeps:P0}: −{s.Gross - s.Value:N0}.\n" : "")
            + (s.Unpriced > 0 ? $"{s.Unpriced} item{(s.Unpriced == 1 ? " has" : "s have")} no price anywhere yet and add nothing.\n" : "")
            + $"Session total: {totalNow:N0}.";
        var top = s.Drops.Concat(s.Gathered).Where(r => !r.Header && r.Worth > 0).OrderByDescending(r => r.Worth).ToList();
        _topRows = top;
        TopDrops.ItemsSource = top.Take(3).Select(r => r with { Value = Fk(r.Worth) }).ToList();
        TopDropsBox.Visibility = top.Count > 0 && TopDropsShown ? Visibility.Visible : Visibility.Collapsed;

        // the player's best hour in this zone; this stay's own rate is the zone view's, whatever the window shows
        ShowBest(e);

        var extra = Extras(e, h);
        ExtraLine.Text = extra; ExtraLine.ToolTip = extra.Length > 0 ? "From the Kinah counter at the top-left of the game and your Market mail: what your balance actually changed by, spending included." : null;
        ExtraLine.Visibility = extra.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        // the panel over the game shows these same figures
        _hudValues["time"] = Clock(elapsed);
        _hudValues["rate"] = totalNow > 0 ? Fk(totalNow / h) : "—";
        _hudValues["kinah"] = Fk(kinah / h);
        _hudValues["loot"] = s.Priced > 0 ? Fk(s.Value / h) : "—";
        _hudValues["total"] = Fk(totalNow);
        _shareCard = new CardNumbers(e.Zone, ZoneLocator.KindOf(e.Zone) ?? (_mode == Mode.Gather ? "Gathering" : "Open world"), elapsed, totalNow, totalNow > 0 ? totalNow / h : 0,
            _mode == Mode.Gather ? new[] { (KinahHr.Text, Tile1Label.Text), (KinahKill.Text, Tile2Label.Text), (KillsHr.Text, Tile3Label.Text) }
                                 : new[] { (KinahHr.Text, Tile1Label.Text), (KillsHr.Text, Tile3Label.Text), (DropsHr.Text, Tile4Label.Text) });
        ShowTable(e, elapsed, s);
    }

    /// <summary>True once the count on screen has something in it: the summary card shows from then on.</summary>
    bool _hasSummary;

    /// <summary>Every priced drop and gathered item, most valuable first, from the last FillNumbers (Top drops shows three, the Discord card five).</summary>
    List<DropRow> _topRows = new();

    /// <summary>The run card's "Your best here" line and the panel's "best" figure.</summary>
    void ShowBest(SessionEngine shown)
    {
        var zoneView = _watcher.Chat?.ZoneView;
        var zone = zoneView?.Zone ?? shown.Zone;
        var best = _demo ? new BestRuns.Best(1_400_000, 48, "2026-10-19") : BestRuns.For(Server?.Label ?? "", zone);
        // this stay: the zone view when it is live, else what the window shows (a layout render)
        var stay = zoneView != null && !_demo ? zoneView : shown;
        double hrs = stay.ActiveHours;
        long rate = stay.Active.TotalMinutes >= 1 ? (long)(BestRuns.Value(stay) / hrs) : 0;
        bool counts = stay.Active.TotalMinutes >= BestRuns.MinMinutes;
        if (best == null)
        {
            BestLine.Visibility = Visibility.Collapsed;
            _hudValues["best"] = counts && rate > 0 ? "first" : "—";
            return;
        }
        BestLine.Visibility = Visibility.Visible;
        if (counts && rate > best.PerHr)
        {
            BestLine.Text = $"Best here yet · beats {Fk(best.PerHr)}/hr";
            BestLine.Foreground = B("Good");
            _hudValues["best"] = "new best";
        }
        else
        {
            BestLine.Text = $"Your best here: {Fk(best.PerHr)}/hr";
            BestLine.Foreground = B("Muted");
            _hudValues["best"] = Fk(best.PerHr);
        }
        BestLine.ToolTip = $"Your best hour in {zone} on {Server?.Label ?? "this server"}: {best.PerHr:N0} Kinah plus loot after tax, over {best.Minutes} minutes on {best.At}. A stay counts once it has {BestRuns.MinMinutes} minutes here; loot is valued at today's prices.";
    }

    /// <summary>
    /// What 0.7 adds to the summary line, each part only once there is something to say: net Kinah from the balance
    /// counter, craft and enchant results, and the player's own Market sales.
    /// </summary>
    static string Extras(SessionEngine e, double h)
    {
        var parts = new List<string>();
        if (e.NetKinah is long net) parts.Add($"Kinah balance {(net >= 0 ? "up" : "down")} {Fk(Math.Abs(net))} ({(net >= 0 ? "" : "−")}{Fk(Math.Abs(net) / h)}/hr)");
        var o = e.OddsTotals();   // crafts have their own part of the window now, never this line
        if (o.Enchants > 0) parts.Add($"{o.EnchantsOk} of {o.Enchants} enchants worked");
        if (e.Sold > 0) parts.Add($"sold {e.Sold} listing{(e.Sold == 1 ? "" : "s")} for {Fk(e.SoldKinah)}");
        if (e.Expired > 0) parts.Add($"{e.Expired} listing{(e.Expired == 1 ? "" : "s")} expired");
        return string.Join(" · ", parts);
    }

    /* ---------------- crafting ---------------- */

    string _part = "Farming";

    /// <summary>Shows one part of the window: Farming (the run bar, the stats, the loot tables), Crafting (the crafting ledger) or Expeditions and events (each trip, never on the Farming page).</summary>
    void ShowPart(string part, bool remember = true)
    {
        _part = part is "Crafting" or "Events" ? part : "Farming";
        bool craft = _part == "Crafting";
        ApplyView();
        FarmTab.Style = (Style)FindResource(_part == "Farming" ? "SegOn" : "SegOff");
        CraftTab.Style = (Style)FindResource(craft ? "SegOn" : "SegOff");
        EventsTab.Style = (Style)FindResource(_part == "Events" ? "SegOn" : "SegOff");
        if (_part == "Events") ShowInstances();
        if (remember && !_demo) { _prefs.Part = _part; _prefs.Save(); }
        if (craft && !_demo) FillCraft(_watcher.Chat?.CraftView);
    }

    /// <summary>Compact or Full view, remembered; the window grows to the right, or to the left near the screen's edge.</summary>
    void SetView(bool full)
    {
        _prefs.FullView = full;
        if (!_demo) _prefs.Save();
        ApplyView();
    }

    /// <summary>
    /// What shows where, for the part (Farming or Crafting), the view (Compact or Full) and the first-run screen.
    /// Compact (Steven, 0.7.6): one 662px column, the run card, a slim session total, then the tables at three rows each;
    /// no recent pickups. Full: double width, the run card, the summary with top drops and the pickups on the left, the
    /// tables at six rows each on the right. Crafting is its own table in one column, whatever the view.
    /// </summary>
    void ApplyView()
    {
        bool craft = _part == "Crafting" && !_welcome, events = _part == "Events" && !_welcome, farm = !craft && !events && !_welcome, full = farm && _prefs.FullView;
        LeftCol.Visibility = craft || events ? Visibility.Collapsed : Visibility.Visible;
        SessionCard.Visibility = Tiles.Visibility = farm ? Visibility.Visible : Visibility.Collapsed;
        SummaryCard.Visibility = farm && _hasSummary ? Visibility.Visible : Visibility.Collapsed;
        TablesCard.Visibility = farm ? Visibility.Visible : Visibility.Collapsed;
        CraftCard.Visibility = craft ? Visibility.Visible : Visibility.Collapsed;
        EventsCard.Visibility = events ? Visibility.Visible : Visibility.Collapsed;
        LowerScroll.Visibility = farm || craft || events ? Visibility.Visible : Visibility.Collapsed;
        // rows: 1 the left column in Compact, 2 (the one that fills, and scrolls) for the tables, and for both columns in Full
        System.Windows.Controls.Grid.SetRow(LeftCol, full ? 2 : 1);
        System.Windows.Controls.Grid.SetRow(LowerScroll, 2);
        System.Windows.Controls.Grid.SetColumn(LowerScroll, full ? 1 : 0);
        LowerScroll.Margin = full ? new Thickness(16, 0, -20, 0) : new Thickness(0, 0, -20, 0);
        // fixed column widths: an Auto column would take half the header's width from the span and leave a gap
        LeftColDef.Width = new GridLength(662);
        RightColDef.Width = new GridLength(full ? 16 + 662 : 0);
        // the summary: slim in Compact (the total and how it adds up), with the top drops in Full
        TopDropsShown = full;
        TopDropsBox.Visibility = full && _topRows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        FootTotal.FontSize = full ? 26 : 20;
        SummaryCard.Padding = full ? new Thickness(20, 14, 20, 16) : new Thickness(20, 10, 20, 10);
        FullLinkText.Visibility = Visibility.Collapsed;
        HeadStack.Width = StatusRow.Width = full ? 662 * 2 + 16 : 662;
        ViewFill.Opacity = full ? 1 : 0.25;
        ViewBtn.ToolTip = full ? "Compact view: one column, three rows per table" : "Full view: twice as wide, with recent pickups and more rows per table";
        ViewBtn.Visibility = craft || _welcome ? Visibility.Collapsed : Visibility.Visible;
        FillPickups();
        Redraw();
    }

    /// <summary>True in Full view: the summary shows the top drops (Compact keeps it to the total).</summary>
    bool TopDropsShown;

    /// <summary>Rows per table before "Show all": three in Compact, six in Full view.</summary>
    int TableRows => _prefs.FullView ? 6 : 3;

    /// <summary>A layout render's sample count and clock, so switching views in a render redraws the same sample.</summary>
    SessionEngine? _demoEngine; TimeSpan _demoElapsed;

    /// <summary>
    /// The crafting table: per recipe, the tries, how often it worked, what a try costs at Market prices (materials from
    /// the recipe book; a failed try uses them too), what came out and what it sells for after the Market's cut, and the
    /// profit or loss: the outcome's value minus every try's cost. A dash wherever a price or the recipe isn't known.
    /// </summary>
    void FillCraft(SessionEngine? e)
    {
        var rows = new List<DropRow>();
        long spent = 0, worth = 0; int noCost = 0, noPrice = 0, tries = 0, ok = 0;
        foreach (var r in e?.CraftSnapshot() ?? new())
        {
            tries += r.Attempts; ok += r.Ok;
            long? cost = RecipeBook.CostPerTry(r.Name, out int unpricedMats, out int vendorMats);
            bool costKnown = cost != null && unpricedMats == 0;
            long made = 0; bool madeKnown = true;
            foreach (var (item, n) in r.Made)
            {
                if (PriceBook.Price(item) is long p) made += (long)Math.Round(p * n * PriceBook.SellerKeeps); else madeKnown = false;
            }
            if (costKnown) spent += cost!.Value * r.Attempts; else noCost++;
            if (madeKnown) worth += made; else noPrice++;
            var book = RecipeBook.ForOutput(r.Name);
            var outcome = r.Made.Count == 0 ? "nothing came out yet" : "made " + string.Join(", ", r.Made.OrderByDescending(m => m.Value).Select(m => (m.Key.Equals(r.Name, StringComparison.OrdinalIgnoreCase) ? "" : m.Key + " ") + "×" + m.Value));
            var listed = book?.SuccessRate is double sr ? $" · listed {sr:P0}" : "";
            long? profit = costKnown && madeKnown ? made - cost!.Value * r.Attempts : null;
            var sells = PriceBook.Price(r.Name) is long sp ? Fk(sp * PriceBook.SellerKeeps) : "—";
            rows.Add(new DropRow(r.Name, r.Attempts.ToString("N0"), r.Attempts > 0 ? $"{100.0 * r.Ok / r.Attempts:0}%" : "—", costKnown ? Fk(cost!.Value) : "—",
                Initial: InitialOf(r.Name), Icon: Slot(_items.IdOf(r.Name)), Grade: _items.Get(_items.IdOf(r.Name))?.Grade ?? "",
                Price: sells, Value: profit is long pf ? Signed(pf) : "—", Tone: profit is long t ? (t >= 0 ? "Good" : "Bad") : "",
                Sub: outcome + listed + (book == null ? " · not in the recipe book" : unpricedMats > 0 ? $" · {unpricedMats} material{(unpricedMats == 1 ? "" : "s")} not priced" : "") + (vendorMats > 0 ? $" · plus {vendorMats} vendor item{(vendorMats == 1 ? "" : "s")}" : "")));
        }
        CraftRows.ItemsSource = rows;
        CraftEmpty.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        CraftWhat.Text = tries > 0 ? $"{tries} tr{(tries == 1 ? "y" : "ies")} · {ok} worked ({100.0 * ok / tries:0}%)" : "since the tracker started";
        CraftSpent.Text = spent > 0 ? "−" + Fk(spent) : "—";
        CraftSpentNote.Text = noCost > 0 ? $"{noCost} recipe{(noCost == 1 ? "" : "s")} with no cost yet, left out" : "";
        CraftMade.Text = worth > 0 ? Fk(worth) : "—";
        CraftMadeNote.Text = noPrice > 0 ? $"{noPrice} recipe{(noPrice == 1 ? "" : "s")} with no Market price yet, left out" : "after the Market's fee and tax";
        long net = worth - spent;
        CraftProfit.Text = spent > 0 || worth > 0 ? Signed(net) : "—";
        CraftProfit.Foreground = B(spent > 0 || worth > 0 ? (net >= 0 ? "Good" : "Bad") : "Ink");
        CraftProfitNote.Text = noCost + noPrice > 0 ? "only recipes with every price known" : "";
    }

    static string Signed(double v) => (v >= 0 ? "+" : "−") + Fk(Math.Abs(v));

    void ShowTable(SessionEngine? e, TimeSpan? elapsed = null, Tables? pre = null)
    {
        var drops = new List<DropRow>(); var gathered = new List<DropRow>(); var mobs = new List<DropRow>(); var pets = new List<DropRow>();
        int kills = 0, nodes = 0;
        if (e != null)
        {
            double h = Math.Max((elapsed ?? e.Active).TotalHours, 1.0 / 3600);
            var t = e.Totals(); kills = t.Kills; nodes = t.Nodes;
            var s = pre ?? Build(e, h, kills);
            drops = s.Drops; gathered = s.Gathered; mobs = s.Mobs; pets = s.Pets ?? new();
        }
        // this zone's view: an empty section says what came in other zones, so nothing looks lost after a zone change
        // (Steven, 2026-10-21: the Soul Crystals from Eternal Isle "not showing" in Crimson Isle)
        var allE = !_prefs.AllZones && e != null && _watcher.Chat?.Engine is { } ce && !ReferenceEquals(ce, e) ? ce : null;
        var allRows = allE?.ItemRows() ?? new();
        int allPets = allE?.PetSnapshot().Values.Sum() ?? 0, allMobs = allE?.MobSnapshot().Count ?? 0;
        int allGather = allRows.Where(r => GatheringCatalog.Find(r.Name) != null).Sum(r => r.Count + r.Bound);
        int allDrops = allRows.Where(r => GatheringCatalog.Find(r.Name) == null).Sum(r => r.Count + r.Bound);
        string None(int elsewhere, string unit) => elsewhere > 0 ? $"  ·  NONE HERE · {elsewhere:N0}{(unit.Length > 0 ? " " + unit : "")} IN OTHER ZONES" : "  ·  NONE YET";
        // summary first, tables after (Steven, 0.7.6): a section with nothing in it is hidden (or, when other zones have
        // some, shows that on its folded header), and it opens on its own the moment its first row lands. A click folds it.
        if (Opened("Drops", drops.Count)) _prefs.DropsOpen = true;
        if (Opened("Gather", gathered.Count)) _prefs.GatherOpen = true;
        if (Opened("Mob", mobs.Count)) _prefs.MobsOpen = true;
        if (Opened("Pet", pets.Count)) _prefs.PetsOpen = true;
        DropsSection.Visibility = drops.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        GatherSection.Visibility = gathered.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        MobSection.Visibility = mobs.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        PetSection.Visibility = pets.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        // what came in other zones, for the sections empty here: one line instead of a header each (Steven, 0.7.6)
        var elsewhere = new List<string>();
        if (drops.Count == 0 && allDrops > 0) elsewhere.Add($"{allDrops:N0} drop{(allDrops == 1 ? "" : "s")}");
        if (gathered.Count == 0 && allGather > 0) elsewhere.Add($"{allGather:N0} gathered");
        if (mobs.Count == 0 && allMobs > 0) elsewhere.Add($"{allMobs:N0} enem{(allMobs == 1 ? "y" : "ies")}");
        if (pets.Count == 0 && allPets > 0) elsewhere.Add($"{allPets:N0} Soul Crystal{(allPets == 1 ? "" : "s")}");
        bool nothingHere = drops.Count + gathered.Count + mobs.Count + pets.Count == 0;
        OtherZones.Text = elsewhere.Count == 0 ? "" : (nothingHere ? "Nothing here yet" : "Other zones") + " · " + string.Join(", ", elsewhere) + (nothingHere ? " in other zones" : "");
        OtherZones.Visibility = elsewhere.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        bool none = nothingHere && elsewhere.Count == 0;
        TablesEmpty.Visibility = none && !(_watcher.Chat?.Instances.Snapshot().Count > 0) ? Visibility.Visible : Visibility.Collapsed;
        bool dOpen = _prefs.DropsOpen && drops.Count > 0, gOpen = _prefs.GatherOpen && gathered.Count > 0,
             mOpen = _prefs.MobsOpen && mobs.Count > 0, pOpen = _prefs.PetsOpen && pets.Count > 0;
        // the top three rows of each section until "show all" (0.7.6, so a full session rarely scrolls); an enemy's open loot table always shows
        Rows.ItemsSource = TopRows("Drops", drops, TableRows, DropsMore, DropsMoreRun);
        GatherRows.ItemsSource = TopRows("Gather", gathered, TableRows, GatherMore, GatherMoreRun);
        MobRows.ItemsSource = TopRows("Mob", mobs, TableRows, MobMore, MobMoreRun);
        PetRows.ItemsSource = TopRows("Pet", pets, TableRows, PetMore, PetMoreRun);
        PetEmpty.Visibility = pets.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        int pc = pets.Sum(r => int.TryParse(r.Drops, System.Globalization.NumberStyles.AllowThousands, null, out var n) ? n : 0);
        PetTitle.Text = pc > 0 ? $"SOUL CRYSTALS FROM KILLS  ·  {pc}" : "SOUL CRYSTALS FROM KILLS" + None(allPets, "");
        PetChev.Text = pOpen ? "▾" : "▸";
        PetBody.Visibility = pOpen ? Visibility.Visible : Visibility.Collapsed;
        if (!dOpen) DropsMore.Visibility = Visibility.Collapsed;
        if (!gOpen) GatherMore.Visibility = Visibility.Collapsed;
        if (!mOpen) MobMore.Visibility = Visibility.Collapsed;
        if (!pOpen) PetMore.Visibility = Visibility.Collapsed;
        MobEmpty.Visibility = mobs.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ShowInstances();
        MobCredit.Visibility = mobs.Count > 0 && mOpen ? Visibility.Visible : Visibility.Collapsed;
        var target = _watcher.Chat?.TargetNote ?? "";
        MobEmpty.Text = target.Length > 0 ? "Target bar: " + target + "."
            : "A best guess: the Kinah that lands just after the target bar names an enemy. With several enemies on you, the bar shows only one.";
        int mc = mobs.Count(r => !r.Child);
        MobTitle.Text = mc > 0 ? $"KINAH BY ENEMY  ·  {mc}" : "KINAH BY ENEMY" + None(allMobs, "ENEMIES");
        MobChev.Text = mOpen ? "▾" : "▸";
        MobBody.Visibility = mOpen ? Visibility.Visible : Visibility.Collapsed;
        TableEmpty.Visibility = drops.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        GatherEmpty.Visibility = gathered.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        TableEmpty.Text = e == null ? "Drops show here as they're read, with the drop rate per kill and per hour." : "No drops yet.";
        GatherEmpty.Text = e == null ? "Gathered materials show here, with how often each comes per attempt and per hour." : "Nothing gathered yet.";
        // one table at a time, picked by the tabs; the tab labels carry the counts
        int gc = gathered.Count(r => !r.Header);
        int ic = drops.Count(r => !r.Header);   // item rows; the Soul Crystals have their own section
        DropsTitle.Text = ic > 0 ? $"DROPS FROM KILLS  ·  {ic}" : "DROPS FROM KILLS" + None(allDrops, "");
        GatherTitle.Text = gc > 0 ? $"GATHERED  ·  {gc}" : gathered.Count > 0 ? "GATHERED" : "GATHERED" + None(allGather, "");
        DropsChev.Text = dOpen ? "▾" : "▸"; GatherChev.Text = gOpen ? "▾" : "▸";
        DropsBody.Visibility = dOpen ? Visibility.Visible : Visibility.Collapsed;
        GatherBody.Visibility = gOpen ? Visibility.Visible : Visibility.Collapsed;
        var notes = new List<string>();
        if (dOpen && drops.Count > 0 && kills == 0) notes.Add("Drop % appears once kills are counted. Kills come from Kinah or Soul Point pickups, one per mob.");
        if (gOpen && gathered.Count > 0 && nodes == 0) notes.Add("Per-attempt rates appear once a gather is counted: every ore, log, Odyle, herb or food line counts as one attempt, failed ones too.");
        bool noPrice = (dOpen && drops.Any(r => !r.Header && r.Price == "—")) || (gOpen && gathered.Any(r => !r.Header && r.Price == "—"));
        if (mOpen && mobs.Count > 0 && target.Length > 0) notes.Add("Target bar: " + target + ".");
        if (_prefs.MobsOpen && _watcher.Chat?.Target is { Unlisted: { Length: > 0 } un } tg && (DateTime.UtcNow - tg.UnlistedAt).TotalSeconds < 30 && mobs.Count == 0) notes.Add($"The target bar shows \"{un}\", which isn't on the enemy list yet, so it isn't counted.");
        if (noPrice && !PriceBook.Any) notes.Add("No Market prices for this server yet. Open the Market in the game once with the tracker running, and the site's price board fills in the rest.");
        TableNote.Text = string.Join(" ", notes);
        TableNote.Visibility = notes.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Rows per section at the last redraw, so a section that just got its first row can open itself.</summary>
    readonly Dictionary<string, int> _rowsBefore = new();

    /// <summary>True when a section went from no rows to some since the last redraw.</summary>
    bool Opened(string section, int rows)
    {
        int before = _rowsBefore.GetValueOrDefault(section);
        _rowsBefore[section] = rows;
        return before == 0 && rows > 0;
    }

    /// <summary>The icon slot for an item: cached or fetched by the cache, a lettered tile until then.</summary>
    IconSlot Slot(long id) => IconCache.Get(_items.IconUrl(id));
    static string InitialOf(string name) { foreach (var c in name) if (char.IsLetter(c)) return char.ToUpperInvariant(c).ToString(); return "·"; }

    /// <summary>A drop's odds as the site's item page says them: "1 in 6 kills" (here "1 in 6", or "17%" with the column switched), or "2.3 a kill" for more than one per kill.</summary>
    static string Odds(long drops, int kills) => drops <= 0 || kills <= 0 ? "—" : drops >= kills ? $"{drops / (double)kills:0.0} a kill" : OddsAsPct ? Pct(drops / (double)kills) : $"1 in {Math.Round(kills / (double)drops):0}";

    /// <summary>
    /// Expeditions and events, one row per place: how many runs, the average time from entering to the last loot, the value
    /// per hour inside the runs and per run (Kinah plus loot at the lowest Market price after the Market's cut).
    /// </summary>
    void ShowInstances()
    {
        var log = _watcher.Chat?.Instances;
        var runs = log?.Snapshot() ?? new List<InstanceRun>();
        var rows = new List<DropRow>();
        foreach (var g in runs.GroupBy(r => r.Zone).OrderByDescending(g => g.Max(r => r.LastLoot)))
        {
            long value = 0; double secs = 0; int n = 0;
            foreach (var r in g) { value += RunValue(r); secs += r.Seconds; n++; }
            var kind = g.Last().Kind;
            var last = g.Max(r => r.LastLoot) ?? DateTime.UtcNow;
            var sub = log?.Open == g.Key ? $"{kind} · in progress" : $"{kind} · last run {Ago(last)}";
            rows.Add(new DropRow(g.Key, n.ToString("N0"), Clock(TimeSpan.FromSeconds(secs / n)), Fk(value / Math.Max(secs, 10) * 3600),
                Initial: InitialOf(g.Key), Value: (value / n).ToString("N0"), Sub: sub));
        }
        InstRows.ItemsSource = rows;
        InstWhat.Text = rows.Count > 0 ? $"{runs.Count} run{(runs.Count == 1 ? "" : "s")} kept on this PC" + (log?.Open is { } open ? $" · in {open} now" : "") : "";
        InstEmpty.Visibility = rows.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>A run's worth: its Kinah plus its tradeable loot at the lowest Market price, after the Market's cut.</summary>
    long RunValue(InstanceRun r)
    {
        double v = r.Kinah;
        foreach (var (id, count) in r.Items)
            if (PriceBook.Price(_items.NameOf(id)) is long p) v += p * count * PriceBook.SellerKeeps;
        return (long)Math.Round(v);
    }

    static string Ago(DateTime utc)
    {
        var d = DateTime.UtcNow - utc;
        return d.TotalMinutes < 1 ? "just now" : d.TotalHours < 1 ? $"{(int)d.TotalMinutes} min ago" : d.TotalDays < 1 ? $"{(int)d.TotalHours} h ago" : $"{(int)d.TotalDays} d ago";
    }

    /// <summary>"42:17" under an hour, "1:42:17" past it, as the site's timer shows it.</summary>
    static string Clock(TimeSpan t) => t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes}:{t.Seconds:00}";

    static string Pct(double p) => p >= 1 ? p.ToString("0.0") + "×" : p >= .1 ? (p * 100).ToString("0") + "%" : p >= .01 ? (p * 100).ToString("0.0") + "%" : (p * 100).ToString("0.00") + "%";
    static string Rate(double v) => v >= 100 ? v.ToString("0") : v >= 10 ? v.ToString("0.0") : v.ToString("0.00");
    static string Fk(double v) => Math.Abs(v) >= 1e6 ? $"{v / 1e6:0.00}M" : Math.Abs(v) >= 1e4 ? $"{v / 1e3:0}K" : Math.Abs(v) >= 1e3 ? $"{v / 1e3:0.0}K" : v >= 100 ? $"{v:0}" : v >= 10 ? $"{v:0.0}" : $"{v:0.00}";

    /// <summary>A new frame for the Options window's preview; null clears it (the chat area changed).</summary>
    void SetPreview(System.Drawing.Bitmap? bmp)
    {
        if (bmp == null) { PreviewSource = null; _options?.RefreshPreview(); return; }
        using (bmp) PreviewSource = Native.ToSource(bmp);
        _options?.RefreshPreview();
    }

    /// <summary>The status row's link to Options › Game reading, shown while a reading problem stands.</summary>
    void ShowFix(bool on) => FixText.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
    void CloseHud() { _hud?.Close(); _hud = null; }

    /// <summary>The draggable panel over the game, where it was last left; created once and kept until its switch goes off.</summary>
    void EnsureHud()
    {
        if (_hud != null || !_prefs.Hud) return;
        _hud = new HudWindow(HudFields.Parse(_prefs.HudFields)) { Owner = this, Opacity = Math.Clamp(_prefs.HudOpacity, 0.3, 1) };
        if (_prefs.HudX is double hx && _prefs.HudY is double hy && hx > SystemParameters.VirtualScreenLeft - 50 && hy > SystemParameters.VirtualScreenTop - 50) { _hud.Left = hx; _hud.Top = hy; }
        _hud.LocationChanged += (_, _) => { if (_hud != null && !_demo) { _prefs.HudX = _hud.Left; _prefs.HudY = _hud.Top; } };
        _hud.Show();
    }

    /// <summary>When the window grows past the bottom of the screen it's on, it moves up instead (main screen only; elsewhere it's left where the player put it).</summary>
    void KeepOnScreen()
    {
        var wa = SystemParameters.WorkArea;
        if (WindowState != WindowState.Normal || double.IsNaN(Top) || Left < wa.Left || Left >= wa.Right) return;
        if (Top + ActualHeight > wa.Bottom) Top = Math.Max(wa.Top, wa.Bottom - ActualHeight);
        // Full view doubles the width: near the right edge the window moves left to stay on the screen
        if (Left + ActualWidth > wa.Right) Left = Math.Max(wa.Left, wa.Right - ActualWidth);
    }
    void Say(string text, string tone) { Status.Text = text; Status.Foreground = B(tone); Status.ToolTip = null; }
    void Restore() { Show(); WindowState = WindowState.Normal; Activate(); }
    public static void Open(string url) { try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch (Exception) { } }

    /// <summary>The privacy statement in the code signing policy, in this repository.</summary>
    public const string PrivacyUrl = "https://github.com/StevenMacchia/kinah-ledger/blob/main/CODE_SIGNING_POLICY.md#privacy";

    public void WhatsSent(Window? owner = null) => MessageBox.Show(owner ?? this,
        "When a run ends, these totals go to the public tables:\n\n" +
        "•  your server (region and world, as chosen above), what you're farming and where, gear score bracket\n•  run length in minutes and kill count\n•  Kinah picked up, tradeable and bound\n•  drop counts per item, Soul Crystal counts per zone, minutes per zone\n•  app version and a random install ID used only to stop spam\n\n" +
        "Never your character name, account, class, level, chat text or screenshots.\n" +
        "If you paste the profile link code into your profile on the site, the runs from this PC show up there too; nobody else can see them unless you make the profile public.\n\n" +
        "The totals go to kinahledger.com (https://kinahledger.com/v1/runs) once, when you press Stop. If the site can't be reached, the run simply stays on this PC: it's still saved to your history here and isn't sent later.\n\n" +
        "Market prices: while the Market is open, the listings on your screen (item, quantity and unit price, never seller names) go to kinahledger.com (https://kinahledger.com/v1/prices) for your server's price board.\n\n" +
        "Item icons: each is fetched once from NCSOFT's own image server by file name, the way a browser does on their site, and kept in a small cache on this PC. Nothing about you goes with it. ui.icons in settings.json turns this off.\n\n" +
        "Updates: at start and every six hours the tracker asks GitHub which release is newest; only the app version goes with that request. A newer version is downloaded from that release and checked against the checksum in its notes before it's installed. update.check in settings.json turns the check off.",
        "What's shared", MessageBoxButton.OK, MessageBoxImage.Information);

    /* ---------------- server (everything shared is per server) ---------------- */

    public string RegionKey => Servers.Region(_prefs.Region)?.Key ?? Servers.Region(_settings.Region)?.Key ?? Servers.Regions[0].Key;
    public GameServer? Server => Servers.Find(RegionKey, _prefs.Server);
    bool HasServer => Server != null;


    /// <summary>A chosen server goes to the readers at once and into the prefs; Start waits until there is one.</summary>
    void ApplyServer(string regionKey, string label)
    {
        _prefs.Region = regionKey; _prefs.Server = label;
        var s = Server;
        _watcher.Market.Region = RegionKey; _watcher.Market.Server = s?.Label ?? "";
        _watcher.RegionKey = RegionKey; _watcher.Server = s?.Label ?? "";
        if (_watcher.Chat is { } ch) { ch.RegionKey = RegionKey; ch.Server = s?.Label ?? ""; }
        if (!_demo) _prefs.Save();
        ServerRowState();
    }

    /// <summary>The server under the name in the header; until one is chosen it says so in orange and Start stays off (a run or a price with no server would skew the site).</summary>
    void ServerRowState()
    {
        bool ok = HasServer;
        ServerChip.Text = Server is { } s ? $"{s.Label} · {s.Region.Name}" : "No server chosen · pick one in Options";
        ServerChip.Foreground = B(ok ? "Muted" : "Warn");
        if (!Farming) StartBtn.IsEnabled = ok;
    }

    /* ---------------- market prices (always on while the window is open; not part of a run) ---------------- */

    void MarketSwitch(bool on)
    {
        _settings.Upload.Enabled = on;
        _watcher.Market.Enabled = on && _settings.Market.Enabled;
    }

    /// <summary>Drag a box around the Market's rows by hand. The Options window hides itself around this.</summary>
    public void PickMarket()
    {
        bool was = IsVisible;
        Hide();
        try
        {
            var r = RegionPickerWindow.Pick("Drag a box around the Market's listings: the rows with item, quantity and unit price.");
            if (r != null) { _marketRegion = r; _prefs.ManualMarket = true; RegionStore.Save(r.Value, RegionStore.MarketFile); _watcher.Market.UseRegion(r); }
        }
        finally { if (was) { Show(); Activate(); } }
        if (_prefs.ManualMarket && _marketRegion != null) Report("Market area chosen by hand and in use now. Each row is read as one line and split by its numbers.", "Good");
    }

    /// <summary>
    /// Share Market prices: reads what the Market shows right now and sends it at once. The site lists a player's prices as
    /// the server's current ones, ahead of scraped data, for two hours or until someone shares newer ones.
    /// </summary>
    public async void ShareMarketNow()
    {
        if (!_prefs.Share) { Say("Sharing is off. Turn on Share runs and Market prices first.", "Warn"); return; }
        if (!HasServer) { Say("Choose your server first, so the prices land on the right board.", "Warn"); return; }
        var game = GameWindow.Find(_settings.ProcessHints);
        if (game == null) { Say("The game isn't open. Open the Market in the game, then press Share.", "Warn"); return; }
        Say("Reading the Market on your screen…", "Faint");
        bool hidden = false;
        try
        {
            // this window may sit over the Market: step aside for the read, like choosing an area does
            if (GameWindow.Covered(game.Value, _settings.ProcessHints) == "Kinah Ledger's own window") { Hide(); hidden = true; await Task.Delay(250); }
            var (asks, items, sent, note) = await _watcher.Market.ShareNowAsync(game.Value, _settings.Ocr.Scale);
            if (hidden) { Show(); Activate(); hidden = false; }
            if (asks == 0) Say(note, "Warn");
            else if (!sent) Say($"Read {asks} prices for {items} item{(items == 1 ? "" : "s")}, but {note}. They go out with the next batch.", "Warn");
            else Say($"Shared {asks} prices for {items} item{(items == 1 ? "" : "s")}. They're {Server?.Label}'s current prices on the site now.", "Good");
        }
        catch (Exception ex) { CrashLog.Write("share market", ex); Say("Couldn't read the Market: " + ex.Message, "Bad"); }
        finally { if (hidden) { Show(); Activate(); } }
    }

    /// <summary>Back to finding the Market by its title and column headers.</summary>
    public void ResetMarket()
    {
        _marketRegion = null; _prefs.ManualMarket = false; RegionStore.Clear(RegionStore.MarketFile); _watcher.Market.UseRegion(null);
        Report("Back to automatic: the Market window is found by its title and column headers.", "Faint");
    }

    public bool MarketManual => _prefs.ManualMarket && _marketRegion != null;
    public bool ChatManual => _prefs.ManualChat && _region != null;

    /// <summary>What the Market reader is doing right now, run or no run.</summary>
    public string MarketLine()
    {
        var m = _watcher.Market;
        if (_demoMarket.Length > 0) return _demoMarket;
        if (!m.Enabled) return "Sharing is off, so the Market isn't read either. Turn sharing back on (Options › Sharing) and it resumes.";
        if (_watcher.GameRect == null) return "Waiting for the game. While the game and this window are open, the Market is read whenever you open it; Start tracking is only for farming runs.";
        return m.Status + (m.Manual && _marketRegion is DRect r ? $" · chosen area {r.Width} x {r.Height} px" : "");
    }

    /// <summary>The Market table's outline for the preview, when it is being read.</summary>
    (DRect, System.Drawing.Color)[] MarketMarks()
    {
        var m = _watcher.Market;
        return m.Enabled && m.Layout is { } l && (m.Detected || m.Manual) ? new[] { (l.Outline, MarketCapture.MarkColor) } : Array.Empty<(DRect, System.Drawing.Color)>();
    }

    public void WhatsRead(Window? owner = null) => MessageBox.Show(owner ?? this,
        "Whenever this window and the game are open (not only during a run), the tracker watches for the Market window. While the Market is open, the listings on your screen are read about once a second, the same way the chat is:\n\n" +
        "•  item name, stack size and unit price of each ask\n•  your server, as chosen above, so prices land on the right board\n•  app version and the random install ID used only to stop spam\n\n" +
        "Never seller names, your character, chat text or screenshots. The same ask isn't sent twice within 15 minutes.\n" +
        "Nothing is clicked or typed in the game. It follows the Share switch in Options › Sharing: with sharing off, the Market isn't read at all.\n\n" +
        "Asks go to kinahledger.com (https://kinahledger.com/v1/prices) in small batches as they are read. If the site can't be reached, the batch is tried again a few seconds later; whatever is still unsent when this window closes is dropped. Prices are never stored on this PC.",
        "What's read from the Market", MessageBoxButton.OK, MessageBoxImage.Information);

    /* ---------------- tray, hotkey, window state ---------------- */

    void BuildTray()
    {
        var menu = new WF.ContextMenuStrip { BackColor = System.Drawing.Color.FromArgb(17, 26, 40), ForeColor = System.Drawing.Color.FromArgb(233, 239, 247), ShowImageMargin = false, Font = new System.Drawing.Font("Segoe UI", 9.75f) };
        menu.Renderer = new TrayMenuRenderer();
        var open = new WF.ToolStripMenuItem("Open Kinah Ledger") { Font = new System.Drawing.Font("Segoe UI", 9.75f, System.Drawing.FontStyle.Bold) };
        open.Click += (_, _) => Restore();
        _trayToggle.Click += async (_, _) => await Toggle();
        var options = new WF.ToolStripMenuItem("Options…"); options.Click += (_, _) => OpenOptions();
        var updates = new WF.ToolStripMenuItem("Check for updates"); updates.Click += async (_, _) => { OpenOptions(OptionsWindow.Page.Updates); await CheckForUpdate(manual: true); };
        var site = new WF.ToolStripMenuItem("kinahledger.com"); site.Click += (_, _) => Open(SiteUrl);
        var quit = new WF.ToolStripMenuItem("Quit Kinah Ledger"); quit.Click += (_, _) => { _quitting = true; Close(); };
        menu.Items.AddRange(new WF.ToolStripItem[] { open, _trayToggle, new WF.ToolStripSeparator(), options, updates, site, new WF.ToolStripSeparator(), quit });
        _tray.Icon = Native.AppIcon(); _tray.Text = "Kinah Ledger"; _tray.ContextMenuStrip = menu; _tray.Visible = true;
        _tray.DoubleClick += (_, _) => Restore();
        _tray.BalloonTipClicked += (_, _) => Restore();
    }

    void OnSourceInitialized(object? sender, EventArgs e)
    {
        var h = new WindowInteropHelper(this).Handle;
        Native.DarkTitleBar(h, Skins.IsDark(_prefs.Skin));
        HwndSource.FromHwnd(h)?.AddHook(WndProc);
        if (ApplyHotkey() is { } err) Say(err, "Warn");
    }

    IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Native.WM_HOTKEY && wParam.ToInt32() == HotkeyId) { _ = Toggle(); handled = true; }
        return IntPtr.Zero;
    }

    void OnStateChanged(object? sender, EventArgs e)
    {
        // a minimize goes to the tray while farming, or always when Options › General says so
        if (WindowState != WindowState.Minimized || !(Farming || _prefs.MinimizeToTray)) return;
        Hide();
        if (Farming && !_balloonShown) { _balloonShown = true; _tray.ShowBalloonTip(4000, "Kinah Ledger is still counting", "It's in the tray by the clock. Double-click the icon to bring the window back.", WF.ToolTipIcon.Info); }
    }

    void OnClosingWindow(object? sender, CancelEventArgs e)
    {
        SavePrefs();
        // Options › General: the close button keeps the tracker in the tray, so it can open with the game; Quit is in the tray menu
        if (_prefs.CloseToTray && !_quitting && !_demo) { e.Cancel = true; HideToTray(); return; }
        if (Farming && !_quitting)
        {
            var r = MessageBox.Show(this, "You're still farming. Stop the run and quit?\n\nYes stops the run and closes Kinah Ledger.\nNo keeps it running and leaves this window open.", "Kinah Ledger", MessageBoxButton.YesNo, MessageBoxImage.Question);
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
        _options?.Discard();
        CloseHud();
        _tray.Visible = false; _tray.Dispose();
        InstallUpdateOnClose();   // last: a downloaded update takes the exe's place now that nothing else needs it
    }

    void SavePrefs()
    {
        if (_demo) return;   // a layout render must not overwrite the player's saved choices
        _prefs.Activity = ActivityName();
        if (_prefs.ManualChat && _region is DRect mr) RegionStore.Save(mr);
        if (_prefs.ManualMarket && _marketRegion is DRect mm) RegionStore.Save(mm, RegionStore.MarketFile);
        if (WindowState == WindowState.Normal && !double.IsNaN(Left)) { _prefs.WinX = (int)Left; _prefs.WinY = (int)Top; }
        _prefs.Save();
    }

    /// <summary>Where the chat is read from, for Options › Game reading.</summary>
    public string ChatAreaText() =>
        _prefs.ManualChat && _region is DRect m ? $"Chosen by hand: {m.Width} × {m.Height} px at {m.X}, {m.Y}. Back to automatic undoes it."
        : _region is DRect r ? $"Found automatically: {r.Width} × {r.Height} px at {r.X}, {r.Y}. The zone is read from the minimap while you play."
        : "Found automatically when the game is open: the chat above the input box, and the zone from the minimap. If it can't find the chat, choose it yourself.";

    public void ForceClose() { _quitting = true; Close(); }

    /// <summary>Windows is signing out or shutting down: close for real, whatever the close button is set to do.</summary>
    public void PrepareForExit() => _quitting = true;

    /// <summary>Runs start → wait → stop exactly as the buttons would, for catching crashes without a desktop session.</summary>
    public async Task<string> DebugCycle(DRect? region, int ms)
    {
        _demo = true;
        _region = region; _prefs.ManualChat = region != null; _watcher.ManualChat = region;
        if (!HasServer) { ApplyServer("na_e", "Siel (Elyos)"); }
        await StartTracking();
        if (!Farming) throw new InvalidOperationException("start failed: " + Status.Text);
        var chat = _watcher.Chat!; var found = chat.Region; var zone = chat.Zone;
        await Task.Delay(ms);
        var reads = chat.Reads;
        await StopTracking();
        return $"{reads} reads, chat {found}, zone {zone ?? "none"}, status: {Status.Text}";
    }

    /// <summary>Fills the window with a sample session (open world, gathering, pets, or open world with the Market open) so its layout can be reviewed without the game.</summary>
    public void ShowDemo(string kind = "Open world")
    {
        _demo = true;
        bool market = kind == "Market";
        _region = new DRect(96, 1664, 1338, 384); _prefs.ManualChat = false;
        ZoneLbl.Text = "Western Cantas Valley";
        ApplyServer("na_e", "Siel (Elyos)");
        SetWelcome(false);   // a layout render of the main window, not of the first-run screen
        if (kind == "Events") { ShowPart("Events", remember: false); SetTrackingUi(false); return; }
        if (kind == "Crafting")
        {
            // the Crafting part with a morning's crafts, priced, for a layout render
            var ce = new SessionEngine(_items); var t0 = DateTime.UtcNow.AddMinutes(-30);
            PriceBook.SetBoard(new() { ["Expert's Splendent Orichalcum Longsword"] = 420_000, ["Artisan's Refining Stone"] = 21_000, ["Pure Orichalcum Ore"] = 5_200, ["Pure Odyle"] = 6_300,
                ["Artisan's Orichalcum Longsword"] = 690_000, ["Artisan's Splendent Orichalcum Longsword"] = 1_450_000, ["Fresh Fruit"] = 900, ["Artisan's Fruit Salad"] = 5_100 });
            int s = 0;
            void Craft(string recipe, bool ok, string? made = null) { ce.Apply(new CraftResult(t0.AddSeconds(s += 30), ok ? recipe : "", ok)); if (ok) ce.Apply(new ItemAcquired(t0.AddSeconds(s + 1), _items.IdOf(made ?? recipe), 1, false)); }
            Craft("Artisan's Orichalcum Longsword", true); Craft("Artisan's Orichalcum Longsword", false); Craft("Artisan's Orichalcum Longsword", true, "Artisan's Splendent Orichalcum Longsword");
            Craft("Artisan's Orichalcum Longsword", true); Craft("Artisan's Orichalcum Longsword", false);
            for (int i = 0; i < 12; i++) Craft("Artisan's Fruit Salad", i % 4 != 3);
            ShowPart("Crafting", remember: false);
            FillCraft(ce);
            SetTrackingUi(false);
            Say("Reading · 3,620 reads · last: Artisan's Fruit Salad acquired.", "Faint");
            return;
        }
        var e = new SessionEngine(_items); var t = DateTime.UtcNow;
        // a price board for the layout render, as the site would send it, so the value tile shows a figure
        PriceBook.SetBoard(new() { ["Thick hide"] = 1_100, ["Spiritstone"] = 7_499, ["Manastone"] = 4_200, ["Orichalcum Ore"] = 1_950, ["Fine Orichalcum Ore"] = 9_800, ["Sapphire Gemstone"] = 12_000, ["Ruby Gemstone"] = 12_000, ["Odyle"] = 2_300, ["Kukuru"] = 900 });
        void Drop(string n, int c, bool b = false) { for (int i = 0; i < c; i++) e.Apply(new ItemAcquired(t, _items.IdOf(n), 1, b)); }
        void Kills(int n, int kinah) { for (int i = 0; i < n; i++) { e.Apply(new KinahChanged(t, kinah + (i % 7) * 40, false)); e.Apply(new CurrencyGained(t, "Oculazen Soul Points", 1)); } }
        void Pet(string n, int c) { for (int i = 0; i < c; i++) e.Apply(new PetCrystal(t, n + " Soul Crystal", 1)); }
        _mode = kind == "Gathering" ? Mode.Gather : Mode.Open;
        switch (_mode)
        {
            case Mode.Gather:
                e.CreditZone("Western Cantas Valley", 42 * 60 + 17);
                Drop("Orichalcum Ore", 82); for (int i = 0; i < 14; i++) e.Apply(new ItemAcquired(t, _items.IdOf("Orichalcum Ore"), 2, false));
                Drop("Fine Orichalcum Ore", 14); Drop("Pure Orichalcum Ore", 2); Drop("Sapphire Gemstone", 9); Drop("Ruby Gemstone", 6); Drop("Diamond Gemstone", 4); Drop("Fine Sapphire Gemstone", 1);
                Drop("Kukuru", 23); Drop("Conide", 11); Drop("Odyle", 31); Drop("Fine Odyle", 3);
                for (int i = 0; i < 11; i++) e.Apply(new GatherFailed(t, "Essence Extraction"));
                break;
            default:
                e.CreditZone("Western Cantas Valley", 42 * 60 + 17);
                // Kinah by enemy: the target bar named these while their Kinah landed
                e.SetTarget("Abyss Drakan Scout", t); Kills(94, 3690);
                e.SetTarget("Abyss Drakan Striker", t); Kills(52, 4120);
                e.SetTarget("Abyss Drakan Striker", t.AddMinutes(-5)); Kills(15, 3500);   // the bar went quiet: nobody's
                Drop("Thick hide", 41); Drop("Spiritstone", 26); Drop("Fresh Fruit", 19); Drop("Manastone", 8); Drop("Aether shard", 7); Drop("Enchantment stone", 3); Drop("Greater manastone", 1);
                Drop("Orichalcum Ore", 6); Drop("Sapphire Gemstone", 1);
                // on both Drakans' tables: their odds pool the kills of the two
                Drop("Soft Balaur Meat", 3); Drop("Refining Stone", 4);
                Pet("Natura", 3);
                // 0.7: the balance counter, a few crafts and a sale, so the extra line shows in layout renders
                e.SetBalance(1_204_300); e.SetBalance(1_784_900);
                e.Apply(new CraftResult(t, "Orichalcum Ingot", true)); e.Apply(new CraftResult(t, "Orichalcum Ingot", true)); e.Apply(new CraftResult(t, "Orichalcum Ingot", false));
                e.Apply(new MarketSold(t, "Spiritstone", 10, 75_200));
                break;
        }
        ApplyModeLabels();
        _tableWhat = "this run";
        FarmTime.Text = (_mode == Mode.Gather ? "Gathering" : "Farming") + " for 00:42:17";
        _demoEngine = e; _demoElapsed = TimeSpan.FromMinutes(42) + TimeSpan.FromSeconds(17);
        FillNumbers(e, _demoElapsed);
        FillPickups();
        _hudValues["last"] = "Spiritstone ×1";
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
        Say(_mode == Mode.Gather ? "Tracking Gathering · Western Cantas Valley · 3,620 reads · last: Sapphire Gemstone acquired." : "Tracking Open world · Western Cantas Valley · 3,620 reads · last: Spiritstone acquired.", "Faint");
        if (market)
        {
            _demoMarket = "Market open · 318 asks read · 41 items · 318 shared · last read 12 s ago";
        }
    }
}
