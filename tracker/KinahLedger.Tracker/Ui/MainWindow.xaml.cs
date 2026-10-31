using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Documents;
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
public sealed record DropRow(string Name, string Drops, string DropPct, string PerHour, bool Header = false, string Grade = "", IconSlot? Icon = null, string Initial = "", string Price = "", string Value = "", string Sub = "", string Tone = "", string Key = "", bool Child = false, string Report = "", bool Reported = false, string Tip = "", long Worth = 0, string PriceTone = "", string PriceTip = "", bool Mat = false)
{
    /// <summary>The row's report link: "Report" flags the drop so the site never gets it; "Undo" takes the flag off.</summary>
    public string ReportText => Reported ? "Undo" : "Report";
    /// <summary>The words before the link: the hint "Looks wrong?", or "Reported ·" on a flagged row.</summary>
    public string ReportHint => Reported ? "Reported ·" : "Looks wrong?";
    /// <summary>The line under the name, with a ⓘ when hovering the row tells more.</summary>
    public string SubShown => Tip.Length == 0 || Header ? Sub : Sub.Length > 0 ? Sub + "  ⓘ" : "ⓘ";
}

public partial class MainWindow : Window
{
    // dungeons and raids are out for now: a boss kill drops more lines at once than any chat window shows
    // Soul Crystals are still counted and shared (the site's Pets page needs them); the window shows them as a count only
    // open world or gathering is worked out from what the chat shows (0.7.6: the picker is gone); a run is saved and shared
    // under what it turned out to be (GameWatcher.DominantKind)
    /// <summary>Mixed: kills and gathers both in the last few minutes (SessionEngine.MixedAt), the tiles show both.</summary>
    enum Mode { Open, Gather, Mixed }
    public const string RepoUrl = "https://github.com/StevenMacchia/kinah-ledger";
    public const string SiteUrl = "https://kinahledger.com";
    public const string SupportUrl = "https://ko-fi.com/swoly";
    /// <summary>The Kinah Ledger Discord (never expires): #tracker-help, #bugs-and-feedback, #announcements (every release posts there).</summary>
    public const string DiscordUrl = "https://discord.gg/fjHMYAABca";
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
    /// <summary>The panel's lock and size (hud.json).</summary>
    readonly HudPrefs _hudPrefs;
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
        Upload.RunPayload.Gear = GearScore.Payload(_prefs.GearScore);   // runs are shared under the player's gear bracket, or "unknown"
        if (!_prefs.ManualMarket) _marketRegion = null;
        _settings.Upload.Enabled = _prefs.Share;
        // the site's item list, so drops are shared under real ids and show their icons; one conditional request per start
        if (watch && (settings.Ui.Icons || _settings.Upload.Enabled)) _ = DictionaryCache.RefreshAsync(_items);
        _watcher = new GameWatcher(settings, _items, _marketRegion, _prefs.ManualChat ? _region : null, _prefs.Server) { RegionKey = _prefs.Region, Server = _prefs.Server };
        // another character in the world: close the run and start fresh (MainWindow.Shell.cs)
        _watcher.CharacterChanged += why => Dispatcher.BeginInvoke(() => { _ = StartFresh(why.Contains("character screen") ? "Logged off" : "Another character"); });
        // the Market is read for this PC whatever the Share switch says; only sending follows it (MarketCapture.Sharing)
        _watcher.Market.Enabled = settings.Market.Enabled;
        Icon = Native.AppIconSource();
        // the panel over the game shows the newest feed line and the rate since 0.8.30: the old default set moves to it once
        if (_prefs.HudFields == HudFields.OldDefault) _prefs.HudFields = HudFields.Default;
        // the panel's lock and size: locked for a new install, left draggable for one that used the tracker before
        _hudPrefs = HudPrefs.Load(existingInstall: _prefs.Welcomed || _prefs.LastVersion.Length > 0, remember: watch);

        // off by default: the tables start over on a new zone. The site gets each zone's own numbers either way.
        // every start: the drops table open; Gathered, each gathering skill, Kinah by enemy and Soul Crystals folded into one line
        // each under it (Steven, 2026-10-24, the approved farming mock)
        _prefs.DropsOpen = true; _prefs.GatherOpen = _prefs.MobsOpen = _prefs.PetsOpen = false;
        // All zones, clearing the tables and the shortcut live in the "…" menu beside the run's buttons
        MoreBtn.Click += (_, _) => ShowMoreMenu();
        if (_prefs.WinX is int x && _prefs.WinY is int y && x > SystemParameters.VirtualScreenLeft - 50 && y > SystemParameters.VirtualScreenTop - 50
            && x < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 200 && y < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 200)
        { WindowStartupLocation = WindowStartupLocation.Manual; Left = x; Top = y; }

        if (!_prefs.ManualChat) _region = null;   // auto mode: the chat is found at start, not remembered
        StartBtn.Click += async (_, _) => await Toggle(fromWindow: true);
        // everything that can be set is in the Options window, so the ledger never moves or grows for it
        OptionsBtn.Click += (_, _) => OpenOptions();
        SiteBtn.Click += (_, _) => Open(SiteUrl);
        ServerChip.MouseLeftButtonUp += (_, _) => OpenOptions(OptionsWindow.Page.Server);
        FixLink.Click += (_, _) => OpenOptions(OptionsWindow.Page.Reading);
        Version.Text = "v" + Upload.RunPayload.AppVersion;
        // the server: everything shared is per server, so it is chosen first (welcome screen or Options) and remembered
        _watcher.Market.Region = RegionKey; _watcher.Market.Server = Server?.Label ?? "";

        InitShare();   // Share: the sheet with the picture to paste (Ui/MainWindow.Share.cs)
        // the miscount report keeps pictures only once the player said yes (Options › Sharing, or the question on its first use)
        ReportBuffer.KeepFrames = _prefs.ReportFrames;
        ReportBuffer.KeepUiFrames = _prefs.SendReportFrames;
        MiscountLink.Click += (_, _) => ReportMiscount();
        DiscordLink.Click += (_, _) => Open(DiscordUrl);
        // Compact and Full view: the header button switches, and the summary's link opens Full view
        // the view as it shows: on a screen too narrow for Full view, a click asks for it again and says why it can't
        ViewBtn.Click += (_, _) => SetView(!FullShown);
        // a click on ODDS switches "1 in 21" and "4.9%" (and doesn't fold the section under it)
        OddsAsPct = _prefs.OddsAsPercent; ShowOddsHeader();
        PctHeader.MouseLeftButtonUp += (_, ev) => { ev.Handled = true; OddsAsPct = _prefs.OddsAsPercent = !_prefs.OddsAsPercent; if (!_demo) _prefs.Save(); ShowOddsHeader(); Redraw(); };
        // "show all" / "show fewer" under the drops; the sections, the feed's hints and the chips' tables (Ui/MainWindow.Live.cs)
        DropsMoreLink.Click += (_, _) => { if (!_allRows.Remove("Drops")) _allRows.Add("Drops"); Redraw(); };
        InitLive();
        InitToday();
        InitLoot();   // LOOT's This zone / Today switch (Ui/MainWindow.Loot.cs)
        InitGoals(watch);   // the player's goals, from goals.json (Ui/MainWindow.Goals.cs); a render or a self-test has none
        // a click on a drop or gathered row opens its card: this run, the price, the site, the listed chances
        Rows.MouseLeftButtonUp += ItemRowClick; MatRows.MouseLeftButtonUp += ItemRowClick; GatherRows.MouseLeftButtonUp += ItemRowClick;
        // a click on a recipe opens its breakdown: materials, what a craft costs, what it makes (Ui/MainWindow.CraftCard.cs)
        CraftRows.MouseLeftButtonUp += CraftRowClick;
        InstSessionBtn.Click += (_, _) => { _eventsAll = false; ShowInstances(); };
        InstEarlierBtn.Click += (_, _) => { _eventsAll = true; ShowInstances(); };
        // a click on an enemy opens or closes its loot table
        MobRows.MouseLeftButtonUp += (_, ev) =>
        {
            if (RowOf((ev.OriginalSource as FrameworkElement)?.DataContext) is { Key.Length: > 0 } r)
            {
                if (!_openMobs.Remove(r.Key)) _openMobs.Add(r.Key);
                Redraw();
            }
        };
        // Farming, Crafting and Events are three parts of the window; crafting never shows in the farming tables. A click is a
        // page picked by hand: it stays until the activity moves on (Session/PageFollow.cs)
        FarmTab.Click += (_, _) => ShowPart("Farming");
        CraftTab.Click += (_, _) => ShowPart("Crafting");
        EventsTab.Click += (_, _) => ShowPart("Events");
        CraftCreditLink.Click += (_, _) => Open("https://metabot.gg/en/aion-2/crafting");
        MobCreditLink.Click += (_, _) => Open(MobImages.CreditUrl);
        // no "Are you sure?": they happen at once with an Undo bar, and become final when it goes (Ui/MainWindow.Undo.cs)
        CraftResetLink.Click += async (_, _) => await StartCraftOver();
        InstClearBtn.Click += async (_, _) => await ClearRuns();
        WireUndo();
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
        foreach (var inner in new[] { DropsScroll, CraftScroll })
            inner.PreviewMouseWheel += (_, ev) =>
            {
                ev.Handled = true;
                LowerScroll.RaiseEvent(new MouseWheelEventArgs(ev.MouseDevice, ev.Timestamp, ev.Delta) { RoutedEvent = MouseWheelEvent, Source = LowerScroll });
            };
        ShowPart(PageFollow.Farming, byHand: false);   // always Farming at the start, whatever page it closed on (0.8.55)
        SizeChanged += (_, _) => KeepOnScreen();
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(RefitIfNeeded);   // the screen's scaling changed, or a screen with another one
        ShowTable(null);
        ServerRowState();
        InitWelcome();
        InitCounting(watch);   // Help improve counting: the choice applied, the one-time notice
        if (!HasServer) Say("Choose your region and server first: prices and drops are kept per server.", "Warn");
        else Say("Reading the chat whenever the game is open. Start run times a stretch on top.", "Faint");
        // the data folder's clean-up: pictures and miscount reports past their time, on its own thread, then hourly (Housekeeping)
        if (watch) Housekeeping.RunIfDue(DateTime.Now);
        // miscount reports the site couldn't take before (no connection, the route not deployed yet) go now, when the backoff allows
        if (watch) { var up = _settings.Upload; _ = Task.Run(async () => { try { await Upload.ReportClient.FlushAsync(up); } catch (Exception ex) { CrashLog.Write("miscount report upload", ex); } }); }
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
        // three stats (the approved mock): the hour's Kinah is in the line under the big figure, so it isn't one of them
        (Tile1Label.Text, Tile2Label.Text, Tile3Label.Text, Tile4Label.Text) = m switch
        {
            Mode.Gather => ("attempts an hour", "items an hour", "items a try", ""),
            Mode.Mixed => ("", "Kinah a kill", "kills an hour", "attempts an hour"),
            _ => ("", "Kinah a kill", "kills an hour", "drops a kill"),
        };
        Tile1.Visibility = m == Mode.Gather ? Visibility.Visible : Visibility.Collapsed;
        Tile4.Visibility = m == Mode.Gather ? Visibility.Collapsed : Visibility.Visible;
        // the fifth tile: what the hour is worth if the tradeable items are sold on the Market (plus the Kinah, in open world)
        Tile5Label.Text = m == Mode.Gather ? "value/hr" : "total/hr";
        if (!Farming && _watcher.Chat == null && !_demo) ShowTable(null);
    }


    /// <summary>Redraws the table for the count on screen: the run, the run just stopped, or the live count.</summary>
    /// <summary>"Looks wrong? Report" on a drop or gathered row: flags the item so nothing about it is sent to the site; "Undo" takes the flag off.</summary>
    void ReportClick(object sender, RoutedEventArgs e)
    {
        if (RowOf((sender as FrameworkContentElement)?.DataContext) is not { } r || r.Report.Length == 0) return;
        ToggleReport(r);
    }

    /// <summary>Report, or its Undo, for a drop or gathered item: from its row's link, or a right-click on its LOOT slot.</summary>
    void ToggleReport(DropRow r)
    {
        if (r.Report.Length == 0) return;
        bool on = ReportedDrops.Toggle(r.Report);
        CrashLog.Note(on ? $"reported: {r.Report} (not sent to the site)" : $"report undone: {r.Report}");
        // a flagged drop also keeps the last minute of what was read, so the misread can be found and fixed
        string? saved = null;
        if (on && _watcher.Chat is { } c) { try { saved = c.WriteReport(r.Report); } catch (Exception ex) { CrashLog.Write("miscount report", ex); } }
        Say(on ? $"{r.Report} won't be sent to the site. Thanks for flagging it." + (saved != null ? " The last minute is saved in your reports folder." : "") : $"{r.Report} counts again.", "Faint");
        if (saved != null) Status.ToolTip = saved;
        if (saved != null) _ = SendReportPictures(saved, "Looks wrong? on " + r.Report);
        Redraw();
    }

    /// <summary>
    /// A saved report's loot list and crafting panel pictures go to the site when the player turned that on (Options ›
    /// Sharing, off by default) and sharing is on; never the chat. The status line says how it went.
    /// </summary>
    async Task SendReportPictures(string dir, string why)
    {
        if (!_prefs.SendReportFrames || !_prefs.Share) return;
        try
        {
            var (ok, note) = await Upload.AccuracyClient.SendFramesAsync(_settings.Upload, dir, why);
            CrashLog.Note($"miscount pictures: {(ok ? "sent" : "not sent")} ({note})");
            // a failed send is final, so the folder is marked: the clean-up keeps it for the player to attach by hand
            if (!ok && ReportBuffer.HasSendablePictures(dir)) ReportBuffer.MarkUnsent(dir, note); else ReportBuffer.ClearUnsent(dir);
            Say(ok ? $"Report saved and its pictures sent ({note}). Thanks: they become tests the tracker must pass." : $"Report saved on this PC; its pictures weren't sent ({note}).", ok ? "Good" : "Faint");
        }
        catch (Exception ex) { CrashLog.Write("miscount pictures", ex); }
    }

    /// <summary>
    /// "Report a miscount": the last minute of what the tracker read and counted (ReportBuffer), saved to a folder on this
    /// PC as before, and since 0.9.0 sent to the site's report table in the background (Upload/ReportClient.cs; Steven,
    /// 2026-10-30: reports should arrive by themselves, not wait for a zip on Discord). Other players' chat is taken out
    /// before it goes; no pictures go with it. The first time, while pictures are off, it asks whether to keep them (on
    /// this PC). The status line says "Report sent", or that it waits for the site, and nothing more.
    /// </summary>
    void ReportMiscount()
    {
        var c = _watcher.Chat;
        if (c == null) { Say("Nothing to report yet: the last minute is kept once the tracker is reading the game.", "Warn"); return; }
        bool asked = false;
        if (!_prefs.ReportFrames)
        {
            var answer = MessageBox.Show(this,
                "Sends this report to Kinah Ledger (no chat from other players).\n\n" +
                "Also keep the last minute of the chat and the loot list as pictures on this PC, for the next reports? " +
                "Pictures show exactly what the screen said, which is what finds a misread. They can show other players' names " +
                "and messages, so they are never sent with a report. Options › Sharing turns them off again.",
                "Kinah Ledger", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer == MessageBoxResult.Yes) { _prefs.ReportFrames = ReportBuffer.KeepFrames = true; SavePrefs(); asked = true; }
        }
        try
        {
            var dir = c.WriteReport("");
            Status.ToolTip = dir;
            _ = SendReportPictures(dir, "Report a miscount");
            Say("Sending the report…" + (asked ? " Pictures are kept on this PC from now on." : ""), "Faint");
            _ = UploadReport(dir, "Report a miscount", "");
        }
        catch (Exception ex) { CrashLog.Write("miscount report", ex); Say("Couldn't save the report: " + ex.Message, "Bad"); }
    }

    /// <summary>
    /// A saved report folder to the site (ReportClient), built and sent on a worker thread so neither the window nor the
    /// reader waits for it; then one short line in the status bar, no figures.
    /// </summary>
    async Task UploadReport(string dir, string why, string flagged)
    {
        var server = Upload.ServerKeys.Build(RegionKey, Server?.Label ?? "");
        var u = _settings.Upload;
        try
        {
            var o = await Task.Run(() => Upload.ReportClient.SendAsync(u, dir, why, flagged, server));
            Say(o switch
            {
                Upload.ReportClient.Outcome.Sent => "Report sent. Thanks: it helps fix the count.",
                Upload.ReportClient.Outcome.Queued => "Report saved; it will send when the site answers.",
                _ => "Report saved on this PC.",
            }, o == Upload.ReportClient.Outcome.Sent ? "Good" : "Faint");
        }
        catch (Exception ex) { CrashLog.Write("miscount report upload", ex); Say("Report saved on this PC.", "Faint"); }
    }

    void Redraw()
    {
        var e = Shown();
        if (_demo && _demoEngine != null) { FillNumbers(_demoEngine, _demoElapsed); FillLive(); FillAccuracyCard(); FillToday(); return; }   // a layout render keeps its sample
        if (e != null) FillNumbers(e, e.Active); else { ShowTable(null); _hasSummary = false; _lootHere = null; FillLoot(); }
        FillLive();
        FillAccuracyCard();
        FillToday();
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
                "guessed" => "Part of the name was hidden, so it was matched to the closest item. Hover the item's row and press Report if it's not right.",
                "loot list" => "Only the loot list beside your character showed it; the chat didn't.",
                _ => char.ToUpperInvariant(v[0]) + v[1..] + ".",
            });
        return string.Join(" ", parts);
    }

    /// <summary>
    /// The count the window shows: this zone since you arrived (or since the run started), unless All zones is on; then the
    /// run, or the live count. A run just stopped shows for a few seconds either way.
    /// </summary>
    SessionEngine? Shown()
    {
        if (_clearedView != null) return _clearedView;   // Clear the tables, while it can still be undone
        var chat = _watcher.Chat; var farm = RunEngine;
        if (farm == null && _lastRun != null && DateTime.UtcNow < _lastRunUntil) return _lastRun;
        if (!_prefs.AllZones && chat != null) return chat.ZoneView;
        // All zones: the run, or every zone since the tracker started (GameWatcher.AllView). It showed the always-on count
        // before 0.8.50, which is shared and started over every 15 minutes, so All zones emptied itself on its own
        return farm ?? (chat != null ? _watcher.AllView : null);
    }

    string ViewTitle() => RunEngine != null ? (_prefs.AllZones ? "this run" : "this run · this zone") : (_prefs.AllZones ? "every zone since the tracker started" : "this zone");

    void ResetTiles() { KinahHr.Text = "0"; KinahKill.Text = _mode != Mode.Gather ? "" : "0"; KillsHr.Text = "0"; DropsHr.Text = "0"; ValueHr.Text = ""; }

    /// <param name="fromWindow">True for the window's Start/Stop button; the shortcut and the tray pass false.</param>
    async Task Toggle(bool fromWindow = false)
    {
        if (_welcome) { Say("Choose your server first.", "Warn"); return; }
        // Start while a stop can still be undone: that stop becomes final (saved and shared), and a new run starts
        if (_stopPending) { await CommitPending(); await StartTracking(); return; }
        if (!Farming) await StartTracking();
        // the window's own Stop gets the Undo bar; the shortcut and the tray stop at once, as before
        else if (fromWindow) await StopFromWindow();
        else await StopTracking();
    }

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

    /// <summary>The step-by-step setup check (first run, and Options › Game reading › Check the setup).</summary>
    public CalibrationWindow OpenCalibration(Window owner)
    {
        var w = new CalibrationWindow(_settings, _items.Knows, () => _prefs.ManualChat ? _region : null, PickChat) { Owner = owner };
        w.Show();
        return w;
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
            FlashHud("Run started");   // the shortcut pressed in the game shows it took
        }
        catch (Exception ex)
        {
            StartBtn.IsEnabled = true;
            ShowFix(true);   // the fix is one click away, in Options › Game reading
            Say(ex.Message, "Warn");
        }
    }

    /// <param name="fromWindow">
    /// True for the window's own Stop button: the window comes forward to show the result. The shortcut, the tray, the game
    /// closing and a character change leave the windows where they are, so a stop mid-fight never pops the tracker over the game.
    /// </param>
    async Task StopTracking(bool fromWindow = false)
    {
        // a stop from anywhere else (the shortcut, the tray, the game closing, an update) makes the window's waiting one final
        if (_pendingUndo?.Kind == "stop") EndUndo();
        _stopPending = false;
        var e = _watcher.StopFarm(); if (e == null) return;
        // shared and saved under what the run turned out to be, not the label picked before it started
        var chat = _watcher.Chat; var kind = e.Events > 0 ? GameWatcher.DominantKind(e) : _watcher.FarmKind; var activity = TrackerSession.ActivityOf(e, kind);
        StartBtn.IsEnabled = false; Say("Stopping…", "Faint");
        long worth = BestRuns.Value(e);
        KeepShareRun(e);   // Share's Last run
        FlashHud(worth > 0 ? $"Run stopped · {Fk(worth)} this run" : "Run stopped");
        if (fromWindow)
        {
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Show(); Activate();
        }
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
        StartBtn.IsEnabled = on || HasServer;
        _trayToggle.Text = on ? "Stop run" : "Start run";
        _tray.Text = on ? "Kinah Ledger · farming" : "Kinah Ledger";
    }

    void Tick()
    {
        if (_demo || _moving) return;
        WatchGame();
        var chat = _watcher.Chat; var farm = RunEngine;
        var show = Shown();
        if (show != null && (farm != null || show != _lastRun))
        {
            // the tiles follow what the player is doing now (kills or gathers in the last few minutes), in a run or not:
            // a stretch of gathering shows attempts per hour, mob farming Kinah per hour
            // both at once (a node between mobs): one mixed set that holds until one of them has stopped for a few minutes
            var dom = show.MixedAt(DateTime.UtcNow) ? Mode.Mixed : show.RecentKind is { } rk ? (rk == "Gathering" ? Mode.Gather : Mode.Open) : show.Events > 0 ? ModeOf(show) : _mode;
            if (dom != _mode) { _mode = dom; ApplyModeLabels(); }
            if (show != _lastRun) _tableWhat = ViewTitle();
            FillNumbers(show, show.Active);
        }
        else if (chat == null) { TableTitle.Text = _watcher.GameRect == null ? "Reading starts once the game is open" : "Looking for the chat"; HeadRate.Text = " "; HeadRateUnit.Visibility = Visibility.Collapsed; }
        // the live dot: the game is being read right now
        LiveMark.Visibility = chat != null && chat.Paused.Length == 0 && chat.LastError.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (farm?.Paused == true && !TableTitle.Text.EndsWith(" · paused")) TableTitle.Text += " · paused";
        // the zone is the run card's title, as the site's "best place" card names its zone
        ZoneLbl.Text = chat?.Zone ?? (_watcher.GameRect == null ? "Waiting for the game" : "Zone not read yet");
        // with no zone on the list, what the minimap corner said, so a zone the list doesn't have yet can be named and added
        ZoneLbl.ToolTip = chat?.Zone == null && chat?.ZoneNote is { Length: > 0 } zn ? zn + "\n\nA zone has to be on the tracker's zone list to count. Send the name shown here to have it added." : null;
        if (DateTime.UtcNow < _statusHoldUntil) { /* a stopped run's result is still showing */ }
        else if (chat == null) Tell(_watcher.GameRect == null ? "Waiting for the game. Reading starts on its own once the game is open." : "Looking for the chat… " + _watcher.ChatNote, "Faint");
        else if (chat.LastError.Length > 0) Tell("Read problem: " + chat.LastError, "Bad");
        else if (chat.Paused.Length > 0) Tell($"Paused: {chat.Paused}. Only the game is read, so reading picks up when the chat is in view again · " + MarketShort(), "Warn");
        else
        {
            // one plain line; the reader's details (reads, Market, the Kinah counter, uploads) are on hover
            // the footer (the approved mock): "Tracking Western Cantas Valley · last: Spiritstone acquired"; read counts on hover
            var line = !chat.ChatFound ? "Reading the game: crafting, loot and zone. The chat is picked up as soon as it shows."
                : chat.LastLine.Length > 0 ? "Last: " + chat.LastLine : chat.Zone is { Length: > 0 } tz ? "Tracking " + tz : "Reading the chat";
            _lastRead = chat.ChatFound ? line : "";
            // the Farming page says nothing here while all is well (2026-10-28, the calm page): the last line read is in ⋯
            Tell(_part == "Farming" && chat.ChatFound ? "" : line, "Faint");
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
        // the panel over the game can't stop the rest of the refresh (the Crafting table, the pickups) if it fails to open
        if (HudWanted && chat != null) { try { EnsureHud(); } catch (Exception ex) { if (!_hudFailed) { _hudFailed = true; CrashLog.Write("panel over the game", ex); } } }
        PlacementGuideTick();   // once after the first run's setup check, when the game is in front (Ui/MainWindow.Overlay.cs)
        if (chat != null) FollowActivity(chat.Doing.Current.Kind, DateTime.UtcNow);   // Farming, Crafting or Events, as the player is doing
        if (_part == "Crafting") FillCraft(CraftShown);
        FillLive();
        FillAccuracyCard();
        FillToday();   // the Today strip over Farming and Events (Ui/MainWindow.Today.cs)
        AlertTick();
        ShareTick();   // the Share sheet's preview follows the count (Ui/MainWindow.Share.cs)
        GoalTick();   // a goal reached is said once, on the panel and in the status line (Ui/MainWindow.Goals.cs)
        PriceAlertTick();
        UpdateHud();
    }

    /// <summary>The figures the panel over the game can show, from the last FillNumbers, so the panel and the window always agree.</summary>
    readonly Dictionary<string, string> _hudValues = new();

    /// <summary>The panel over the game, with the figures the window worked out last.</summary>
    void UpdateHud()
    {
        if (_hud == null) return;
        _hudValues["show"] = HudPresence();
        _hudValues["unlocked"] = HudMoving ? "move" : (_hudPreview?.HudLocked ?? _hudPrefs.Locked) ? "" : "1";   // a lock being tried in Options shows at once
        HudRunValues();
        _hudValues["last"] = _demo ? "Spiritstone ×1" : _watcher.Chat?.RecentPickups(1).FirstOrDefault()?.What ?? "";
        HudFeed();
        _hudValues["notice"] = HudNotice();   // a price alert, the shortcut's problem or a waiting update (Ui/MainWindow.Notices.cs)
        _hudValues["noticeClock"] = HudNoticeClock();   // the That was wrong notice's gold line, while it counts down
        _hud.Update(_hudValues);
        _hud.SetAccuracy(HudAccuracy, HudAccuracyToLook);   // only with Show accuracy details on (MainWindow.AccuracyCard.cs)
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
    sealed record Tables(List<DropRow> Drops, List<DropRow> Gathered, long DropCount, long GatherCount, long Value, int Priced, int Unpriced, List<DropRow> Mobs, long Gross = 0, List<DropRow>? Pets = null, List<SkillBox>? Skills = null, int DropsUnpriced = 0);

    /// <summary>A gathering skill's own box under the drops: its attempts, how many failed or came plentiful, the pace and what its items are worth.</summary>
    sealed record SkillBox(string Name, int Attempts, int Failed, int Plentiful, double PerHour, long Value);

    /// <summary>A crafting material (the item list's "Craft Resource" or "Crafting Material"): listed under its own sub-line in the drops.</summary>
    static bool IsMaterial(string category) => category.Contains("Craft", StringComparison.OrdinalIgnoreCase) || category.Contains("Material", StringComparison.OrdinalIgnoreCase);

    // what the card's head says after the activity and the clock: "this run", "this zone", ...
    string _tableWhat = "counted while the game is open";

    /// <summary>
    /// Splits the session into mob drops (rated per kill) and gathered materials grouped by skill (each skill rated
    /// against its own attempts). Soul Crystals stay out of both: the summary line counts them.
    /// </summary>
    Tables Build(SessionEngine e, double hours, int kills)
    {
        var drops = new List<DropRow>(); long dc = 0, gc = 0, value = 0, gross = 0; int priced = 0, unpriced = 0, dropsUnpriced = 0;
        // the enemies fought in this view and their loot tables: odds count only kills of enemies that can drop the item
        var ms = e.MobSnapshot();
        _lastMobKinah = ms.Sum(m => (long)m.Kinah);
        var book = LootBook.Current;
        var tabled = ms.Where(m => book.Knows(m.Name)).ToList();
        var bySkill = new Dictionary<GatherFamily, List<(string name, int total)>>();
        var bySkillGrade = new Dictionary<string, string>();
        var worth = new Dictionary<string, string>();
        var worthN = new Dictionary<string, long>();
        var keptN = new Dictionary<string, long>();
        foreach (var r in e.ItemRows())
        {
            int total = r.Count + r.Bound;
            // a row's value is its tradeable copies at the lowest Market price, after the Market's cut, so the rows add up to
            // the Session's "Loot, after the Market's 12%" (2026-10-25: they were before it; the CI render's Thick hide read
            // 45,100, which is 39,688 after the cut). Worth stays before the cut: Top drops and the Discord card rank by it,
            // and the card says "at the lowest Market ask". Bound copies can't be sold. A missing price is a blank cell,
            // said once in the section line ("6 have no price yet")
            string rowValue = "";
            if (r.Count > 0)
            {
                var price = PriceBook.Price(r.Name);
                if (price != null) { long g0 = price.Value * r.Count, kept = (long)Math.Round(g0 * PriceBook.SellerKeeps); gross += g0; value += kept; priced++; rowValue = Short(kept); worthN[r.Name] = g0; keptN[r.Name] = kept; } else { unpriced++; if (GatheringCatalog.Find(r.Name) == null) dropsUnpriced++; }
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
                        odds = "";
                        tips.Add("None of the enemies you fought here lists this item. It may have been misread, or come from another fight. Press Report to keep it off the site.");
                    }
                }
                bool flagged = ReportedDrops.Has(r.Name);
                // copies the chat never confirmed (the loot list beside the character, or a guessed name): counted, and said on hover
                if (e.Unconfirmed(r.Id) is int unc && unc > 0) tips.Add($"{unc} of these came from the loot list beside your character or a partly hidden name, not a clear chat line.");
                if (flagged) { sub = "Reported · not sent to the site"; tone = "Warn"; }
                // no line under the name any more (the approved mock): bound copies and a report are said on hover
                if (r.Bound > 0) tips.Insert(0, r.Count == 0 ? "Bound: can't be sold, so it has no value here." : $"{r.Bound} of these are bound and have no value here.");
                if (flagged) tips.Insert(0, "Reported: not sent to the site.");
                drops.Add(new DropRow(r.Name, total.ToString("N0"), odds, Whole(total / hours), Grade: grade, Icon: Slot(r.Id), Initial: InitialOf(r.Name), Price: PriceCell(r.Name).Text, PriceTone: PriceCell(r.Name).Tone, PriceTip: PriceCell(r.Name).Tip, Value: rowValue, Sub: sub, Tone: tone, Report: r.Name, Reported: flagged,
                    Tip: string.Join("\n\n", tips), Worth: worthN.GetValueOrDefault(r.Name), Mat: IsMaterial(cat)));
            }
        }
        // most valuable first (the approved mock), then the most picked up
        static int Qty(DropRow d) => int.TryParse(d.Drops, System.Globalization.NumberStyles.AllowThousands, null, out var n) ? n : 0;
        drops = drops.OrderByDescending(d => d.Worth).ThenByDescending(Qty).ToList();
        // Soul Crystals come from kills too: their own section at the bottom, never mixed into the item rows or the drop count
        var petRows = new List<DropRow>();
        foreach (var (name, n) in e.PetSnapshot().OrderByDescending(x => x.Value))
            petRows.Add(new DropRow(name, n.ToString("N0"), "", Whole(n / hours), Initial: InitialOf(name), Sub: "Soul Crystal"));
        var gathered = new List<DropRow>(); var skills = new List<SkillBox>();
        var stats = e.SkillSnapshot();
        foreach (var skill in GatherSkills.Order)
        {
            if (!bySkill.TryGetValue(skill, out var items) && !stats.ContainsKey(skill)) continue;
            var st = stats.GetValueOrDefault(skill) ?? new SessionEngine.SkillStats();
            int ok = st.Nodes - st.Failed;
            // the skill, its attempts and the hour's pace; failed and plentiful gathers on hover
            // the skill gets its own box (attempts, failed, plentiful, the pace); its items list under Gathered with the rest
            // the skill's worth after the Market's cut, like the rows under it
            skills.Add(new SkillBox(GatherSkills.Label(skill), st.Nodes, st.Failed, ok > 0 ? st.Plentiful : 0, st.Nodes / hours, (items ?? new()).Sum(x => keptN.GetValueOrDefault(x.name))));
            foreach (var (name, total) in (items ?? new()).OrderByDescending(x => x.total))
            {
                bool rep = ReportedDrops.Has(name);
                var gtips = new List<string>();
                if (e.Unconfirmed(_items.IdOf(name)) is int gu && gu > 0) gtips.Add($"{gu} of these came from the loot list beside your character or a partly hidden name, not a clear chat line.");
                gathered.Add(new DropRow(name, total.ToString("N0"), st.Nodes > 0 ? Pct(total / (double)st.Nodes) : "", Whole(total / hours), Grade: bySkillGrade.GetValueOrDefault(name, ""), Icon: Slot(_items.IdOf(name)), Initial: InitialOf(name), Price: PriceCell(name).Text, PriceTone: PriceCell(name).Tone, PriceTip: PriceCell(name).Tip, Value: worth.GetValueOrDefault(name, ""),
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
            mobs.Add(new DropRow(m.Name, m.Kills.ToString("N0"), m.Kills > 0 ? Fk(m.Kinah / (double)m.Kills) : "", Fk(m.Kinah / hours), Initial: InitialOf(m.Name), Icon: IconCache.Get(MobImages.Url(m.Name)), Value: Short(m.Kinah),
                Sub: where + (open ? "hide drops" : "show drops"), Key: key,
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
        return new Tables(drops, gathered, dc, gc, value, priced, unpriced, mobs, gross, petRows, skills, dropsUnpriced);
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
        text.Text = all ? "Show fewer" : $"Show all {counted}";
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
        PctHeader.Text = "Odds";   // the numbers themselves show which way it reads
        PctHeader.ToolTip = (OddsAsPct ? "Drops per kill as a percentage" : "How many kills one drop takes")
            + ", counting only kills of enemies that can drop the item. Click to show it as " + (OddsAsPct ? "\"1 in x\"." : "a percentage.");
    }

    // the drops' Market value and how many items carried a price, from the last FillNumbers, for the panel over the game
    long _lastValue; int _lastPriced;
    /// <summary>The Kinah by enemy adds up to, from the last Build, for its folded box.</summary>
    long _lastMobKinah;

    /// <summary>A price or value that fits its column: in full under 100,000, then "680K", "1.68M" (Steven, 2026-10-22: "1,680,000" ran out of the column).</summary>
    static string Short(long v) => Math.Abs(v) < 100_000 ? v.ToString("N0")
        : Math.Abs(v) < 1_000_000 ? $"{v / 1e3:0.#}K"
        : Math.Abs(v) < 1_000_000_000 ? $"{v / 1e6:0.##}M" : $"{v / 1e9:0.##}B";

    /// <summary>The count on screen is too young for an hour's rate to mean much (FillNumbers); BestRuns.MinMinutes is separate.</summary>
    bool _rateWarming;
    static readonly TimeSpan RateWarmup = TimeSpan.FromMinutes(2);
    const int RateWarmupCounts = 3;
    /// <summary>The panel's rate while it warms up.</summary>
    const string WarmingUp = "warming up";

    void FillNumbers(SessionEngine e, TimeSpan elapsed)
    {
        double h = Math.Max(elapsed.TotalHours, 1.0 / 3600);
        // the first minute's hour swings wildly (one pickup 20 s in reads as millions an hour): until 2 minutes and 3
        // counts, whichever comes later, the window dims its figure and the panel says it is warming up
        _rateWarming = elapsed < RateWarmup || e.Events < RateWarmupCounts;
        var t = e.Totals(); long kinah = t.Kinah;   // farming Kinah only; bound Kinah is reward money
        var s = Build(e, h, t.Kills);
        string worth = s.Value > 0 ? $" · worth {Fk(s.Value)} ({Fk(s.Value / h)}/hr)" : "";
        // quest and payment rewards: bound Kinah, a payment line, or one line far above what the mobs pay. Never farming income.
        // said in ⋯ since 2026-10-28, not on the page: the player doesn't need it while counting goes right
        _leftOut = t.Rewards > 0 ? $"{t.Rewards} reward line{(t.Rewards == 1 ? "" : "s")}" + (e.KinahSkipped > 0 ? $" ({Fk(e.KinahSkipped)} Kinah)" : "") : "";
        string crystals = t.Pets > 0 ? $" · {t.Pets} soul crystal{(t.Pets == 1 ? "" : "s")}" : "";
        // the clock is farming time; a minute or more of readable time with no loot is idle, said in ⋯
        _idleNote = e.Idle >= TimeSpan.FromMinutes(1) ? $"{TodayLoot.Dur(e.Idle.TotalSeconds)} idle, not counted in any rate" : "";
        switch (_mode)
        {
            case Mode.Gather:
            {
                KinahHr.Text = Fk(t.Nodes / h);
                KinahKill.Text = Fk(s.GatherCount / h);
                KillsHr.Text = t.Nodes > 0 ? (s.GatherCount / (double)t.Nodes).ToString("0.00") : "";
                DropsHr.Text = Fk(t.Bonus / h);
                int ok = t.Nodes - t.Failed;
                string plent = ok > 0 ? $" · plentiful {Math.Round(100.0 * t.Plentiful / ok)}%" : "";
                string fails = t.Failed > 0 ? $" ({t.Failed} failed)" : "";
                _ = fails; _ = plent; _ = worth; _ = crystals;
                break;
            }
            case Mode.Mixed:
                // Kinah, the whole hour (Kinah plus the drops and the gathered items after the Market's cut), kills and attempts
                KinahHr.Text = Fk(kinah / h);
                KinahKill.Text = t.Kills > 0 ? Fk(kinah / (double)t.Kills) : "";
                KillsHr.Text = Fk(t.Kills / h);
                DropsHr.Text = Fk(t.Nodes / h);
                break;
            default:
                KinahHr.Text = Fk(kinah / h);
                KinahKill.Text = t.Kills > 0 ? Fk(kinah / (double)t.Kills) : "";
                KillsHr.Text = Fk(t.Kills / h);
                // drops a kill (the approved mock): 0.7, not 159 an hour
                DropsHr.Text = t.Kills <= 0 ? "" : s.DropCount / (double)t.Kills is var dk && dk >= 0.1 ? dk.ToString("0.0") : dk.ToString("0.00");
                _ = worth; _ = crystals;
                break;
        }
        // the hour's worth if the tradeable items are sold at the lowest Market price, after the Market's cut; open world adds the Kinah
        _lastValue = s.Value; _lastPriced = s.Priced;
        long worthHr = (long)((s.Value + (_mode == Mode.Gather ? 0 : kinah)) / h);
        ValueHr.Text = s.Priced > 0 || (_mode != Mode.Gather && kinah > 0) ? Fk(worthHr) : "";
        ValueTile.ToolTip = (s.Priced + s.Unpriced == 0 ? "No tradeable items yet."
                : s.Priced == 0 ? "No Market price yet for any of these items. Open the Market in the game once: the prices you see there value your drops, and the site's price board fills in the rest."
                : $"{s.Priced} of {s.Priced + s.Unpriced} tradeable items priced, at the lowest Market price, keeping {PriceBook.SellerKeeps:P0} after the Market's fee and tax. Bound items can't be sold and aren't counted."
                  + (s.Unpriced > 0 ? " Open the Market and look at the others to price them too." : ""))
            + (_mode == Mode.Gather ? "" : " Kinah per hour is included.");
        // the card's head: what and how long ("Open world · 42 min"), in the one way the page writes time (2026-10-28: it was
        // "farming 5:19", minutes and seconds, under a Today line whose "0:21" was hours and minutes); what it covers on hover
        var verb = _mode == Mode.Gather ? "gathering · " : "";
        TableTitle.Text = $"{ZoneLocator.KindOf(e.Zone) ?? "Open world"} · {verb}{TodayLoot.Dur(elapsed.TotalSeconds)}";
        TableTitle.ToolTip = _tableWhat.Length > 0 ? "Counting " + _tableWhat + "." : null;
        long totalNow = kinah + s.Value;
        // the live card's right side: the hour's worth, its line since the start, kills and Kinah a kill, best here, the health line
        FillRate(e, h, _mode == Mode.Gather ? 0 : kinah, s.Value, s.Priced, t.Kills, t.Nodes, t.Nodes > 0 ? s.GatherCount / (double)t.Nodes : 0);
        Summary.Visibility = Visibility.Collapsed;   // idle and skipped reward lines: in ⋯ (ShowMoreMenu)

        // LOOT's first side (Ui/MainWindow.Loot.cs): what this count picked up and its total, with how it adds up on hover.
        // It replaced the SESSION line (2026-10-28), which was this same count; only what happened: gathering doesn't say "0 kills"
        _hasSummary = e.Events > 0;
        ShareCardBtn.IsEnabled = true;   // the sheet says what each of its sides waits for
        var sums = new List<string>();
        if (kinah > 0 || _mode != Mode.Gather) sums.Add($"Kinah picked up: {kinah:N0}, from {t.Kills:N0} kill{(t.Kills == 1 ? "" : "s")}. Quest rewards and bound Kinah are left out.");
        if (s.Priced > 0) { sums.Add($"Loot at the lowest Market price: {s.Gross:N0}."); sums.Add($"Market fee and tax, {1 - PriceBook.SellerKeeps:P0}: −{s.Gross - s.Value:N0}."); }
        if (s.Unpriced > 0) sums.Add($"{s.Unpriced} item{(s.Unpriced == 1 ? " has" : "s have")} no price yet and add{(s.Unpriced == 1 ? "s" : "")} nothing.");
        sums.Add($"Total, after the Market's cut: {totalNow:N0}.");
        if (PlayersHereLine(_watcher.Chat?.ZoneView?.Zone ?? _demoZone ?? e.Zone) is { Length: > 0 } players) sums.Add(players);
        var byEnemy = e.MobSnapshot().Where(m => m.Kinah > 0).GroupBy(m => m.Name).Select(g => (Where: g.Key, Count: g.Sum(m => m.Kinah))).OrderByDescending(x => x.Count).ToList();
        _lootHere = new LootHere(s, kinah, t.Kills, elapsed, totalNow, string.Join("\n", sums), byEnemy);
        FillLoot();
        var top = s.Drops.Concat(s.Gathered).Where(r => !r.Header && r.Worth > 0).OrderByDescending(r => r.Worth).ToList();
        _topRows = top;

        // the player's best hour in this zone (the panel's "best"); this stay's own rate is the zone view's, whatever the window shows
        ShowBest(e);
        FillWhere(_watcher.Chat?.ZoneView?.Zone ?? _demoZone ?? e.Zone);

        // the panel over the game shows these same figures: its rate is the window's big number (gathering leaves Kinah out
        // of it, as FillRate does), and loot an hour shows only when the rate has Kinah in it too. 2026-10-25: gathering
        // showed "779K / hr" and "779K loot / hr" side by side, the same figure twice
        long rateTotal = (_mode == Mode.Gather ? 0 : kinah) + s.Value;
        bool rateHasKinah = _mode != Mode.Gather && kinah > 0;
        _hudValues["time"] = Clock(elapsed);
        _hudValues["warm"] = _rateWarming ? "1" : "";
        _hudValues["rate"] = rateTotal <= 0 ? "" : _rateWarming ? WarmingUp : Fk(rateTotal / h);
        _hudValues["kinah"] = _rateWarming ? "" : Fk(kinah / h);
        _hudValues["loot"] = s.Priced > 0 && rateHasKinah && !_rateWarming ? Fk(s.Value / h) : "";
        _hudValues["total"] = Fk(totalNow);
        // the Share picture's This session: this count's zone, time and total (Ui/MainWindow.Share.cs)
        SetShareSession(e, elapsed, totalNow);
        ShowTable(e, elapsed, s);
    }

    /// <summary>True once the count on screen has something in it: LOOT's first side shows it from then on.</summary>
    bool _hasSummary;

    /// <summary>
    /// What the Farming page stopped saying while all is well (2026-10-28), kept for the ⋯ menu: the reward lines left out
    /// of the count, the idle time, and the last line read ("Last: Spiritstone acquired").
    /// </summary>
    string _leftOut = "", _idleNote = "", _lastRead = "";

    /// <summary>Every priced drop and gathered item, most valuable first, from the last FillNumbers (Top drops shows three, the Discord card five).</summary>
    List<DropRow> _topRows = new();

    /// <summary>The run card's "Your best here" line and the panel's "best" figure.</summary>
    void ShowBest(SessionEngine shown)
    {
        var zoneView = _watcher.Chat?.ZoneView;
        var zone = zoneView?.Zone ?? shown.Zone;
        ShowFarmLine(zone);
        var best = _demo ? new BestRuns.Best(1_400_000, 48, "2026-10-19") : BestRuns.For(Server?.Label ?? "", zone);
        // this stay: the zone view when it is live, else what the window shows (a layout render)
        var stay = zoneView != null && !_demo ? zoneView : shown;
        double hrs = stay.ActiveHours;
        long rate = stay.Active.TotalMinutes >= 1 ? (long)(BestRuns.Value(stay) / hrs) : 0;
        bool counts = stay.Active.TotalMinutes >= BestRuns.MinMinutes;
        if (best == null)
        {
            BestLine.Visibility = Visibility.Collapsed;
            _hudValues["best"] = counts && rate > 0 ? "first" : "";
            JoinCompare();
            return;
        }
        BestLine.Visibility = Visibility.Visible;
        // one grey line under the hour (the approved mock): "Your best here 1.40M · players at your gear 1.46M"
        if (counts && rate > best.PerHr)
        {
            BestLine.Text = $"Best here yet · beats {Fk(best.PerHr)}";
            BestLine.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "Good");
            _hudValues["best"] = "new best";
        }
        else
        {
            BestLine.Text = $"Your best here {Fk(best.PerHr)}";
            BestLine.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "Faint");
            _hudValues["best"] = Fk(best.PerHr);
        }
        JoinCompare();
        BestLine.ToolTip = $"Your best hour in {zone} on {Server?.Label ?? "this server"}: {best.PerHr:N0} Kinah plus loot after tax, over {best.Minutes} minutes on {best.At}. A stay counts once it has {BestRuns.MinMinutes} minutes here; loot is valued at today's prices.";
    }

    /// <summary>The comparison line: a dot between your best and the players' figure when both show, the first word capitalised.</summary>
    void JoinCompare()
    {
        bool best = BestLine.Visibility == Visibility.Visible, farm = FarmLine.Visibility == Visibility.Visible;
        CompareSep.Visibility = best && farm ? Visibility.Visible : Visibility.Collapsed;
        if (farm && FarmLine.Text.Length > 0) FarmLine.Text = (best ? char.ToLowerInvariant(FarmLine.Text[0]) : char.ToUpperInvariant(FarmLine.Text[0])) + FarmLine.Text[1..];
        CompareLine.Visibility = best || farm ? Visibility.Visible : Visibility.Collapsed;
    }

    /* ---------------- crafting ---------------- */

    string _part = "Farming";
    /// <summary>The page following what the player is doing (Session/PageFollow.cs).</summary>
    readonly PageFollow _follow = new();

    /// <summary>
    /// Shows one part of the window: Farming (the run bar, the stats, the loot tables), Crafting (the crafting ledger) or
    /// Expeditions and events (each trip, never on the Farming page). <paramref name="byHand"/>: the player clicked it, so
    /// the page stays until the activity moves on. Since 0.8.55 the page isn't remembered: the window opens on Farming.
    /// </summary>
    void ShowPart(string part, bool byHand = true)
    {
        _part = part is "Crafting" or "Events" ? part : "Farming";
        bool craft = _part == "Crafting";
        ApplyView();
        FarmTab.Style = (Style)FindResource(_part == "Farming" ? "SegOn" : "SegOff");
        CraftTab.Style = (Style)FindResource(craft ? "SegOn" : "SegOff");
        EventsTab.Style = (Style)FindResource(_part == "Events" ? "SegOn" : "SegOff");
        if (_part == "Events") ShowInstances();
        if (byHand) _follow.Picked();
        if (craft && !_demo) FillCraft(CraftShown);
    }

    /// <summary>
    /// The page follows what the player is doing (Options › General › Switch pages to what I'm doing, on by default), from
    /// the window's tick with the session's activity: never on the first-run screen, and only once a change has held.
    /// </summary>
    void FollowActivity(Session.ActivityKind kind, DateTime now)
    {
        if (_follow.Step(kind, now, _prefs.FollowActivity && !_welcome) is { } page && page != _part) ShowPart(page, byHand: false);
    }

    /// <summary>For the options self-test: one tick of the page following an activity, and the page showing.</summary>
    internal void FollowForTest(Session.ActivityKind kind, DateTime now) => FollowActivity(kind, now);
    internal string PartForTest => _part;

    /// <summary>Compact or Full view, remembered; the window grows to the right, or to the left near the screen's edge.</summary>
    void SetView(bool full)
    {
        _prefs.FullView = full;
        if (!_demo) _prefs.Save();
        _fullAsked = full;   // a screen too narrow for it says so now, not only the first time
        ApplyView();
        _fullAsked = false;
    }
    bool _fullAsked;

    /// <summary>
    /// What shows where, for the part (Farming or Crafting), the view (Compact or Full) and the first-run screen.
    /// Compact (Steven, 0.7.6): one 662px column, the run card, a slim session total, then the tables at three rows each;
    /// no recent pickups. Full: double width, the run card, the summary with top drops and the pickups on the left, the
    /// tables at six rows each on the right. Crafting is its own table in one column, whatever the view.
    /// </summary>
    void ApplyView()
    {
        bool craft = _part == "Crafting" && !_welcome, events = _part == "Events" && !_welcome, farm = !craft && !events && !_welcome;
        // Full view scaled to the screen it is on, or Compact when even scaled it can't fit (Ui/MainWindow.Fit.cs)
        FitFull(farm && _prefs.FullView, _fullAsked);
        bool full = farm && FullShown;
        LeftCol.Visibility = craft || events ? Visibility.Collapsed : Visibility.Visible;
        SessionCard.Visibility = farm ? Visibility.Visible : Visibility.Collapsed;
        if (!farm) SummaryCard.Visibility = Visibility.Collapsed;   // LOOT shows itself on Farming (FillLoot)
        if (!farm) GoalsCard.Visibility = Visibility.Collapsed;     // GOALS too (FillGoals)
        // the tables only in Full view since 2026-10-28 (the calm Farming page, mock C): Compact is the live card and LOOT
        TablesCard.Visibility = full ? Visibility.Visible : Visibility.Collapsed;
        CraftCard.Visibility = craft ? Visibility.Visible : Visibility.Collapsed;
        EventsCard.Visibility = events ? Visibility.Visible : Visibility.Collapsed;
        LowerScroll.Visibility = full || craft || events ? Visibility.Visible : Visibility.Collapsed;
        // rows: 1 the left column in Compact, 2 (the one that fills, and scrolls) for the tables, and for both columns in Full
        System.Windows.Controls.Grid.SetRow(LeftCol, full ? 2 : 1);
        System.Windows.Controls.Grid.SetRow(LowerScroll, 2);
        System.Windows.Controls.Grid.SetColumn(LowerScroll, full ? 1 : 0);
        LowerScroll.Margin = full ? new Thickness(16, 0, -20, 0) : new Thickness(0, 0, -20, 0);
        // fixed column widths: an Auto column would take half the header's width from the span and leave a gap
        LeftColDef.Width = new GridLength(662);
        RightColDef.Width = new GridLength(full ? 16 + 662 : 0);
        // the feed and the rate with LOOT and Where to farm now under them and the footer closing that card, in both views;
        // Full view adds the drops and the chips on the right
        MoveTo(WhereSection, LeftCol, LeftCol.Children.IndexOf(SummaryCard) + 1);
        MoveTo(StatusBar, full ? LeftCol : LedgerCol, -1);
        System.Windows.Controls.Grid.SetRow(StatusBar, 3); System.Windows.Controls.Grid.SetColumnSpan(StatusBar, 2);
        HeadStack.Width = full ? 662 * 2 + 16 : 662;
        StatusBar.Width = 662;
        ViewFill.Opacity = full ? 1 : 0.25;
        ViewBtn.ToolTip = full ? "Compact view: one column" : _fullTooWide ? "Full view needs a wider screen than this one" : "Full view: twice as wide, with the drop tables beside the loot";
        ViewBtn.Visibility = craft || events || _welcome ? Visibility.Collapsed : Visibility.Visible;
        // Session and Where to farm now open in Full view, folded in Compact, until the player opens or folds them
        _secUser.Remove("Session"); _secUser.Remove("Where");
        ApplyPlayerView(craft, farm, full);
        FillLive();
        Redraw();
        JoinCards();
    }

    /// <summary>Puts an element into another panel (at <paramref name="index"/>, or the end), leaving it where it is when it is there already.</summary>
    static void MoveTo(FrameworkElement el, System.Windows.Controls.Panel to, int index)
    {
        if (ReferenceEquals(el.Parent, to)) return;
        (el.Parent as System.Windows.Controls.Panel)?.Children.Remove(el);
        if (index < 0 || index > to.Children.Count) to.Children.Add(el); else to.Children.Insert(index, el);
    }

    /// <summary>Rows per table before "Show all": three in Compact, six in Full view.</summary>
    int TableRows => FullShown ? 6 : 3;

    /// <summary>A layout render's sample count and clock, so switching views in a render redraws the same sample.</summary>
    SessionEngine? _demoEngine; TimeSpan _demoElapsed;

    /// <summary>
    /// The Crafting part (direction C): the last five crafts beside the session's profit, then This session, per recipe the
    /// tries, how many worked, what every try cost at Market prices (materials from the recipe book; a failed try uses them
    /// too), what came out at the lowest Market price after the Market's cut, and the profit. A material with no price yet
    /// makes the cost "at least" the priced part and the profit "at most", said once in the section line; materials ticked
    /// "I have these" cost nothing. A missing figure is a blank cell.
    /// </summary>
    void FillCraft(SessionEngine? e)
    {
        var rows = new List<DropRow>();
        long spent = 0, worth = 0; int noBook = 0, noPrice = 0, tries = 0, ok = 0, recipes = 0;
        var missing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in e?.CraftSnapshot() ?? new())
        {
            tries += r.Attempts; ok += r.Ok;
            long? cost = RecipeBook.CostPerTry(r.Name, out int unpricedMats, out int vendorMats, out int farmedMats, missing);
            // vendor materials with no NPC price are unpriced too: the cost is then the least a try costs
            int gaps = unpricedMats + vendorMats;
            long made = 0; bool madeKnown = r.MadeUnknown == 0;
            foreach (var (item, n) in r.Made)
            {
                if (PriceBook.Price(item) is long p) made += (long)Math.Round(p * n * PriceBook.SellerKeeps); else madeKnown = false;
            }
            if (cost != null) spent += cost.Value * r.Attempts; else noBook++;
            if (madeKnown) worth += made; else noPrice++;
            long? profit = cost != null && madeKnown ? made - cost.Value * r.Attempts : null;
            var spentCell = cost is not long cv ? "" : gaps == 0 ? Fk(cv * r.Attempts) : cv == 0 && farmedMats == 0 ? "" : "≥ " + Fk(cv * r.Attempts);
            var profitCell = profit is not long pv ? "" : gaps == 0 ? Signed(pv) : "≤ " + Signed(pv);
            var sellsCell = PriceCell(r.Name);
            var book = RecipeBook.ForOutput(r.Name);
            var tip = "Click for the materials, what a try costs and what it makes."
                + $"\n{r.Ok:N0} worked, {r.Fail:N0} failed."
                + (book?.SuccessRate is double sr ? $"\nListed success {sr:P0}." : "")
                + (r.Upgraded + r.NotUpgraded > 0 ? $"\nUpgrade rolls: {r.Upgraded} of {r.Upgraded + r.NotUpgraded} won." : "")
                + (r.MadeUnknown > 0 ? $"\n{r.MadeUnknown} success{(r.MadeUnknown == 1 ? "" : "es")} whose yield the panel didn't show." : "");
            rows.Add(new DropRow(r.Name, r.Attempts.ToString("N0"), r.Ok.ToString("N0"), spentCell,
                Initial: InitialOf(r.Name), Icon: Slot(_items.IdOf(r.Name)), Grade: _items.Get(_items.IdOf(r.Name))?.Grade ?? "", Key: "craft:" + r.Name, Tip: tip,
                Price: madeKnown && made > 0 ? Fk(made) : "", PriceTone: sellsCell.Tone, PriceTip: sellsCell.Tip, Value: profitCell, Tone: profit is long t ? (t >= 0 ? "Good" : "Bad") : ""));
            recipes++;
            rows.AddRange(UpgradeRows(r, book));
        }
        SyncRows(CraftRows, rows);
        _crafted = e?.CraftSnapshot() ?? new();
        FillBestCrafts();
        // the feed: the last five crafts, worked or failed, with what each made or cost
        DrawFeed(CraftFeedHost, CraftEmpty, FeedFor(FeedTab.Crafting), FeedTab.Crafting, CraftValue);
        FillHealth(CraftHealthText, CraftHealthMark);
        // with nothing crafted yet, the feed's one sentence and Best crafts: no empty table, nothing to start over
        var some = rows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        CraftHead.Visibility = CraftResetText.Visibility = some;
        long net = worth - spent;
        CraftProfit.Text = spent > 0 || worth > 0 ? Signed(net) : " ";
        _craftNet = spent > 0 || worth > 0 ? Signed(net) : "";
        CraftProfit.Foreground = B(spent > 0 || worth > 0 ? (net >= 0 ? "Ink" : "Bad") : "Ink");
        CraftProfitNote.Text = spent > 0 || worth > 0 ? (missing.Count + noBook > 0 ? "profit this session, at most" : "profit this session") : "profit shows with the first craft";
        CraftWhat.Inlines.Clear();
        void Fig(string text) { var run = new Run(text); run.SetResourceReference(TextElement.FontFamilyProperty, "MonoFont"); run.SetResourceReference(TextElement.ForegroundProperty, "Ink"); CraftWhat.Inlines.Add(run); }
        if (tries > 0)
        {
            Fig(tries.ToString("N0")); CraftWhat.Inlines.Add(new Run($" tr{(tries == 1 ? "y" : "ies")} · ")); Fig(ok.ToString("N0")); CraftWhat.Inlines.Add(new Run(" worked"));
            if (spent > 0) { CraftWhat.Inlines.Add(new LineBreak()); Fig(Fk(spent)); CraftWhat.Inlines.Add(new Run(" spent")); }
            if (worth > 0) { CraftWhat.Inlines.Add(new LineBreak()); Fig(Fk(worth)); CraftWhat.Inlines.Add(new Run(" sells for")); }
        }
        CraftWhat.Visibility = CraftWhat.Inlines.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        // This session's line: the recipes, the cut, and the gaps said once
        var gapNote = missing.Count > 0 ? $"{missing.Count} material{(missing.Count == 1 ? " has" : "s have")} no price yet" : "";
        var noBookNote = noBook > 0 ? $"{noBook} recipe{(noBook == 1 ? "" : "s")} not in the recipe book" : "";
        CraftSpentNote.Text = $"{recipes} recipe{(recipes == 1 ? "" : "s")} · after the Market's {1 - PriceBook.SellerKeeps:P0}";
        CraftSpent.Text = spent > 0 || worth > 0 ? Signed(net) : "";
        CraftSpent.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, net >= 0 ? "Gold" : "Faint");
        CraftMadeNote.Text = string.Join(" · ", new[] { gapNote, noBookNote, noPrice > 0 ? $"{noPrice} with no Market price for what came out" : "" }.Where(x => x.Length > 0));
        CraftMadeNote.Visibility = CraftMadeNote.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        CraftNote.Text = $"Lowest Market price on {Server?.Name ?? "your server"}";
        ApplySections();
    }

    /// <summary>
    /// A recipe's upgrades, as rows under it (Steven, 2026-10-26): each item a won upgrade roll made instead of the base
    /// ("Splendent Orichalcum Longsword" under Orichalcum Longsword), with how many, its icon, what they sell for and what
    /// they added over the base items they replaced. An upgrade has no tries of its own: those cells show "—". The recipe's
    /// row keeps every try, its cost, everything that came out and the profit; these rows only show where the value came from.
    /// </summary>
    List<DropRow> UpgradeRows(Session.CraftLedger.Recipe r, RecipeBook.Recipe? book)
    {
        var rows = new List<DropRow>();
        int baseYield = Math.Max(1, book?.OutputQty ?? 1);
        foreach (var (item, n) in r.Made.Where(m => m.Value > 0 && !string.Equals(m.Key, r.Name, StringComparison.OrdinalIgnoreCase))
                     .OrderByDescending(m => m.Value).ThenBy(m => m.Key, StringComparer.Ordinal))
        {
            var great = book?.GreatOutcomes.FirstOrDefault(g => string.Equals(g.Item, item, StringComparison.OrdinalIgnoreCase));
            int rolls = great is { Qty: > 0 } ? Math.Max(1, n / great.Qty) : n;
            long? sells = PriceBook.Price(item) is long up ? (long)Math.Round(up * n * PriceBook.SellerKeeps) : null;
            // what the same crafts would have sold for as the base item, after the Market's cut
            long? extra = sells is long sv && PriceBook.Price(r.Name) is long bp ? sv - (long)Math.Round(bp * (double)rolls * baseYield * PriceBook.SellerKeeps) : null;
            var cell = PriceCell(item);
            var tip = $"An upgrade of {r.Name}: {n:N0} made by {rolls:N0} won upgrade roll{(rolls == 1 ? "" : "s")}, counted in the recipe's row above."
                + (sells is long s1 ? $"\nSells for {Fk(s1)} after the Market's cut." : "\nNo Market price for it yet.")
                + (extra is long e1 ? $"\n{Signed(e1)} against {rolls * baseYield:N0} {r.Name} at its price." : "");
            rows.Add(new DropRow(item, "—", "—", "", Child: true, Sub: "  ×" + n.ToString("N0"),
                Initial: InitialOf(item), Icon: Slot(_items.IdOf(item)), Grade: _items.Get(_items.IdOf(item))?.Grade ?? "", Key: "craft:" + r.Name, Tip: tip,
                Price: sells is long s2 && s2 > 0 ? Fk(s2) : "", PriceTone: cell.Tone, PriceTip: cell.Tip,
                Value: extra is long e2 ? Signed(e2) : "", Tone: extra is long e3 ? (e3 >= 0 ? "Good" : "Bad") : ""));
        }
        return rows;
    }

    static string Signed(double v) => (v >= 0 ? "+" : "−") + Fk(Math.Abs(v));

    void ShowTable(SessionEngine? e, TimeSpan? elapsed = null, Tables? pre = null)
    {
        var drops = new List<DropRow>(); var gathered = new List<DropRow>(); var mobs = new List<DropRow>(); var pets = new List<DropRow>(); var skills = new List<SkillBox>();
        int kills = 0, nodes = 0;
        if (e != null)
        {
            double h = Math.Max((elapsed ?? e.Active).TotalHours, 1.0 / 3600);
            var t = e.Totals(); kills = t.Kills; nodes = t.Nodes;
            var s = pre ?? Build(e, h, kills);
            drops = s.Drops; gathered = s.Gathered; mobs = s.Mobs; pets = s.Pets ?? new(); skills = s.Skills ?? new();
        }
        else _lastMobKinah = 0;
        // this zone's view: an empty section says what came in other zones, so nothing looks lost after a zone change
        // (Steven, 2026-10-21: the Soul Crystals from Eternal Isle "not showing" in Crimson Isle)
        // (not while a clear can still be undone: the counts it hides aren't "other zones")
        var allE = !_prefs.AllZones && e != null && _clearedView == null && _watcher.Chat != null && _watcher.AllView is { } ce && !ReferenceEquals(ce, e) ? ce : null;
        var allRows = allE?.ItemRows() ?? new();
        int allPets = allE?.PetSnapshot().Values.Sum() ?? 0, allMobs = allE?.MobSnapshot().Count ?? 0;
        int allGather = allRows.Where(r => GatheringCatalog.Find(r.Name) != null).Sum(r => r.Count + r.Bound);
        int allDrops = allRows.Where(r => GatheringCatalog.Find(r.Name) == null).Sum(r => r.Count + r.Bound);
        // a section with nothing in it is hidden
        DropsSection.Visibility = drops.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
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

        // Drops from kills: one aligned table, the crafting materials under their own sub-line; the top rows of each (three in
        // Compact, six in Full) until "Show all". The section line says how many kinds, and how many have no price, once
        var plain = drops.Where(r => !r.Mat).ToList(); var mats = drops.Where(r => r.Mat).ToList();
        bool allDropsShown = _allRows.Contains("Drops");
        SyncRows(Rows, allDropsShown ? plain : plain.Take(TableRows).ToList());
        SyncRows(MatRows, allDropsShown ? mats : mats.Take(TableRows).ToList());
        MatHead.Visibility = mats.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        MatTitle.Text = $"CRAFTING MATERIALS · {mats.Count}";
        DropsMore.Visibility = plain.Count > TableRows || mats.Count > TableRows ? Visibility.Visible : Visibility.Collapsed;
        DropsMoreRun.Text = allDropsShown ? "Show fewer" : $"Show all {drops.Count}";
        int noPriceRows = pre?.DropsUnpriced ?? drops.Count(r => r.Value.Length == 0 && r.Worth == 0);
        DropsNote.Text = drops.Count == 0 ? "" : $"{drops.Count} kind{(drops.Count == 1 ? "" : "s")}" + (noPriceRows > 0 ? $" · {noPriceRows} {(noPriceRows == 1 ? "has" : "have")} no price yet" : "");
        TableEmpty.Visibility = drops.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        TableEmpty.Text = e == null ? "Drops show here as they're read, with the odds and how many come an hour." : "No drops yet.";

        // Also this session: a chip each for Gathered, the gathering skills, Soul Crystals and Kinah by enemy (Ui/MainWindow.Live.cs)
        var target = _watcher.Chat?.TargetNote ?? "";
        FillExtras(gathered, skills, pets, mobs, e?.Totals().Kinah ?? 0);
        ApplySections();
        ShowInstances();

        var notes = new List<string>();
        if (drops.Count > 0 && kills == 0) notes.Add("Odds appear once kills are counted. Kills come from Kinah or Soul Point pickups, one per mob.");
        if (_extraOpen == "Gather" && gathered.Count > 0 && nodes == 0) notes.Add("Per-try rates appear once a gather is counted: every ore, log, Odyle, herb or food line counts as one attempt, failed ones too.");
        bool noPrice = drops.Any(r => !r.Header && r.Value.Length == 0);
        if (_extraOpen == "Mob" && mobs.Count > 0 && target.Length > 0) notes.Add("Target bar: " + target + ".");
        if (_watcher.Chat?.Target is { Unlisted: { Length: > 0 } un } tg && (DateTime.UtcNow - tg.UnlistedAt).TotalSeconds < 30 && mobs.Count == 0) notes.Add($"The target bar shows \"{un}\", which isn't on the enemy list yet, so it isn't counted.");
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
    static string Odds(long drops, int kills) => drops <= 0 || kills <= 0 ? "" : drops >= kills ? $"{drops / (double)kills:0.0} a kill" : OddsAsPct ? Pct(drops / (double)kills) : $"1 in {Math.Round(kills / (double)drops):0}";

    /// <summary>True when Events shows every run kept on this PC, not only this session's.</summary>
    bool _eventsAll;
    bool _hudFailed;
    InstanceLog? _demoInst;
    internal static int GradeRank(string g) => g switch { "Epic" => 4, "Unique" => 3, "Legend" => 2, "Rare" => 1, _ => 0 };

    static string Ago(DateTime utc)
    {
        var d = DateTime.UtcNow - utc;
        return d.TotalMinutes < 1 ? "just now" : d.TotalHours < 1 ? $"{(int)d.TotalMinutes} min ago" : d.TotalDays < 1 ? $"{(int)d.TotalHours} h ago" : $"{(int)d.TotalDays} d ago";
    }

    /// <summary>"42:17" under an hour, "1:42:17" past it, as the site's timer shows it.</summary>
    static string Clock(TimeSpan t) => t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes}:{t.Seconds:00}";

    static string Pct(double p) => p >= 1 ? p.ToString("0.0") + "×" : p >= .1 ? (p * 100).ToString("0") + "%" : p >= .01 ? (p * 100).ToString("0.0") + "%" : (p * 100).ToString("0.00") + "%";
    static string Rate(double v) => v >= 100 ? v.ToString("0") : v >= 10 ? v.ToString("0.0") : v.ToString("0.00");
    /// <summary>A figure: "4.26M", "641K", "7.5K", "474", "7.5"; exactly nothing is "0" (the HUD said "0.00 Kinah / hr", the Events feed "paid 0.00").</summary>
    /// A whole number under 1,000 has no decimals ("47", "-500"; they read "47.0" and "-500.00"); a rate like 7.5 an hour keeps them.
    static string Fk(double v)
    {
        double a = Math.Abs(v);
        if (v == 0) return "0";
        if (a >= 1e6) return $"{v / 1e6:0.00}M";
        if (a >= 1e4) return $"{v / 1e3:0}K";
        if (a >= 1e3) return $"{v / 1e3:0.0}K";
        if (a >= 100 || v == Math.Round(v)) return $"{v:0}";
        return a >= 10 ? $"{v:0.0}" : $"{v:0.00}";
    }

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
        if (_hud != null || !HudWanted) return;
        // what Options is trying, while it is open (Ui/MainWindow.Shell.cs PreviewHudLook), else the saved choices
        _hud = new HudWindow(HudFields.Parse(_hudPreview?.HudFields ?? _prefs.HudFields), HudLook) { Owner = this };   // the look waits for Save
        _hud.SetOpacity(_hudPreview?.HudOpacity ?? _prefs.HudOpacity);
        _hud.SetScale(_hudPreview?.HudScale ?? _hudPrefs.Scale);
        // where the player left it, kept on a screen that is still there; else the top centre of the game's monitor
        if (!_hudSpotPreview && SavedHudSpot() is { } spot) { _hud.Left = spot.X; _hud.Top = spot.Y; } else PlaceHudDefault();
        _hud.Dragged += () => { if (_hud != null) RememberHudSpot(_hud.Left, _hud.Top); };   // the Ticker's spot is its own (never in a render)
        // it follows the game from the start: created while the game is behind another window, it waits out of sight
        UpdateHud();
        if (_hudValues.GetValueOrDefault("show", "") != "hidden") _hud.Show();
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
    /// <summary>A message to read (saved, set, fixed, split): it stays about 6 s before the live status takes the line back, the whole text on hover (it used to last one 500 ms tick).</summary>
    void Say(string text, string tone) { Tell(text, tone); Status.ToolTip = text; if (DateTime.UtcNow.AddSeconds(SayHoldSeconds) > _statusHoldUntil) _statusHoldUntil = DateTime.UtcNow.AddSeconds(SayHoldSeconds); }
    const double SayHoldSeconds = 6;
    /// <summary>The live status line, rewritten each tick: in the look's own colour, so a change of look repaints it.</summary>
    void Tell(string text, string tone) { Status.Text = text; Status.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, tone); Status.ToolTip = null; }
    void Restore() { Show(); WindowState = WindowState.Normal; Activate(); }
    public static void Open(string url) { if (OpenForTest != null) { OpenForTest(url); return; } try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch (Exception) { } }
    /// <summary>Set by the options self-test: takes the address instead of starting a browser.</summary>
    internal static Action<string>? OpenForTest;

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
        "Updates: at start and every 10 minutes the tracker asks GitHub which release is newest; only the app version goes with that request. A newer version is downloaded from that release and checked against the checksum in its notes before it's installed. update.check in settings.json turns the check off.\n\n" +
        "Reading checks: with each shared run, counts of how reading went (reads that didn't line up, names guessed or not found, pickups only the loot list saw, crafting panel counts), the item names the tracker couldn't read exactly (names only, never chat), and where the chat sits as fractions of the game window with the window's size go to kinahledger.com (https://kinahledger.com/v1/accuracy) with the app version and the install ID. At start and every six hours the tracker also reads small reading settings and the staged rollout of new versions from kinahledger.com/v1/config; only the app version goes with that request.\n\n" +
        "Help improve counting (on unless you turn it off in Options › Sharing, and only while sharing is on): when two of the tracker's own checks agree on a pickup (a chat line and the loot list, a Kina line and the Kinah counter, a gather and the gathering bar, an event card's icon and its grade), what it read and what it settled on go to kinahledger.com (https://kinahledger.com/v1/labels) with the app version, the install ID, the window size and the text height, plus small pictures of that item's name words, icon or reward slot (at most 60 an hour, https://kinahledger.com/v1/label-crops). Never the chat box, never a chat line that isn't your own pickup, never anyone's name, never a whole screenshot. The pictures are deleted after 90 days. Turned off, nothing is written or sent for it and what was waiting on this PC is deleted.\n\n" +
        "Report pictures stay on this PC unless you turn on Send report pictures.",
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
        // the Market is still read with sharing off: the player's own asks price their drops on this PC (2026-10-23);
        // only sending follows the switch (MarketCapture.Sharing)
        _watcher.Market.Enabled = _settings.Market.Enabled;
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
        var movePanel = new WF.ToolStripMenuItem("Move the panel"); movePanel.Click += (_, _) => MoveHudForAWhile();
        var placePanel = new WF.ToolStripMenuItem("Place the panel…"); placePanel.Click += (_, _) => { if (OpenPlacementGuide() is { } why) _tray.ShowBalloonTip(4000, "Place the panel", why, WF.ToolTipIcon.Info); };
        var updates = new WF.ToolStripMenuItem("Check for updates"); updates.Click += async (_, _) => { OpenOptions(OptionsWindow.Page.Updates); await CheckForUpdate(manual: true); };
        var site = new WF.ToolStripMenuItem("kinahledger.com"); site.Click += (_, _) => Open(SiteUrl);
        var support = new WF.ToolStripMenuItem("Support on Ko-fi"); support.Click += (_, _) => Open(SupportUrl);
        var quit = new WF.ToolStripMenuItem("Quit Kinah Ledger"); quit.Click += (_, _) => { _quitting = true; Close(); };
        menu.Items.AddRange(new WF.ToolStripItem[] { open, _trayToggle, movePanel, placePanel, new WF.ToolStripSeparator(), options, updates, site, support, new WF.ToolStripSeparator(), quit });
        _tray.Icon = Native.AppIcon(); _tray.Text = "Kinah Ledger"; _tray.ContextMenuStrip = menu; _tray.Visible = true;
        _tray.DoubleClick += (_, _) => Restore();
        _tray.BalloonTipClicked += (_, _) => Restore();
    }

    void OnSourceInitialized(object? sender, EventArgs e)
    {
        var h = new WindowInteropHelper(this).Handle;
        Native.DarkTitleBar(h, Skins.IsDark(_prefs.Skin));
        HwndSource.FromHwnd(h)?.AddHook(WndProc);
        RefitIfNeeded();   // Full view against the screen the window opened on, now that it has one
        if (ApplyHotkey() is { } err) HotkeyProblemAtStart(err);   // the status line, a tray notice once, and the panel (Ui/MainWindow.Notices.cs)
        if (ApplyMarkWrongKey() is { } markErr) HotkeyProblemAtStart(markErr, "The That was wrong shortcut is off");
        _foregroundWatch ??= Native.WatchForeground(HudFollowGame);   // the panel fades with an alt-tab, not a tick later
    }

    IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Native.WM_HOTKEY && wParam.ToInt32() == HotkeyId) { _ = Toggle(); handled = true; }
        else if (msg == Native.WM_HOTKEY && wParam.ToInt32() == Hotkey.MarkWrongId) { MarkLastLineWrong(); handled = true; }   // Ui/MainWindow.MarkWrong.cs
        else if (OnSizeMove(msg) && msg == WM_EXITSIZEMOVE) Dispatcher.BeginInvoke(RefitIfNeeded);   // dropped on another screen: Full view fitted to it
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
        // an action behind the Undo bar becomes final first (a stopped run saved, a removed run gone), then the window closes
        if (_pendingUndo != null && !_demo)
        {
            e.Cancel = true;
            _ = CommitPending().ContinueWith(_ => Dispatcher.Invoke(Close));
            return;
        }
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
        if (h != IntPtr.Zero) { Native.UnregisterHotKey(h, HotkeyId); Native.UnregisterHotKey(h, Hotkey.MarkWrongId); }
        _options?.Discard();
        _foregroundWatch?.Dispose(); _foregroundWatch = null;
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
        _hudPrefs.Save();
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
        LoadPlayerDemo(_items); _demoZone = "Western Cantas Valley";   // best crafts and where to farm, sample boards
        _demoDay = DemoDay();   // the Today strip's sample day: three zones of farming (Ui/MainWindow.Today.cs)
        _demoDays = DemoDays();   // the Week side's six days before it (Ui/MainWindow.Week.cs)
        LoadDemoFeed(kind);   // the live feed's sample lines and rate line (Ui/MainWindow.Live.cs)
        SetWelcome(false);   // a layout render of the main window, not of the first-run screen
        if (kind == "Events")
        {
            // sample runs (never the player's own, Ui/MainWindow.RunsCard.cs), for a layout render
            _demoInst = DemoRuns();
            _demoWeekRuns = DemoWeekRuns(_demoInst);   // and four days more for the strip's Week side
            ShowPart("Events", byHand: false); SetTrackingUi(false);
            Say("Reading the chat", "Faint");
            if (Environment.GetEnvironmentVariable("KL_SHOT_TODAY") == "1") OpenTodaySample();
            return;
        }
        if (kind == "CraftingEmpty")
        {
            // the Crafting part before anything is crafted: the one sentence and Best crafts, for a layout render
            _demoFeed.RemoveAll(l => l.Tab == Session.FeedTab.Crafting);
            ShowPart("Crafting", byHand: false);
            FillCraft(new SessionEngine(_items));
            SetTrackingUi(false);
            Say("Reading the chat", "Faint");
            return;
        }
        if (kind == "CraftingUpgrades")
        {
            // a recipe's upgrades as rows under it (Steven, 2026-10-26), for a layout render (example figures): 13 tries of
            // Orichalcum Longsword with two upgrade rolls won, and Expert's Orichalcum Longsword with one. The combo rolls
            // come named by the upgrade, as the blue panel names them, and still land on the recipe being crafted
            var ue = new SessionEngine(_items); var u0 = DateTime.UtcNow.AddMinutes(-20);
            PriceBook.SetBoard(new() { ["Orichalcum Longsword"] = 30_000, ["Splendent Orichalcum Longsword"] = 139_998, ["Orichalcum Ingot"] = 2_000, ["Refining Stone"] = 899, ["Odyle"] = 3_480,
                ["Expert's Orichalcum Longsword"] = 260_000, ["Expert's Splendent Orichalcum Longsword"] = 400_000, ["Expert's Refining Stone"] = 20_000, ["Fine Orichalcum Ore"] = 9_800, ["Fine Odyle"] = 3_098 });
            int us = 0;
            void Race(string recipe, bool ok, bool combo = false)
            {
                var at = u0.AddSeconds(us += 12);
                ue.CraftPanelSeen(recipe, at);
                ue.Apply(new CraftResult(at, recipe, ok, Combo: combo) { Via = SessionEngine.CraftPanelVia, Makes = combo ? null : RecipeBook.ForOutput(recipe)?.OutputQty ?? 1 });
            }
            const string sword = "Orichalcum Longsword", expert = "Expert's Orichalcum Longsword";
            foreach (var (ok, roll) in new (bool, string)[] { (true, ""), (true, ""), (false, ""), (true, "won"), (true, ""), (true, "lost"), (false, ""), (true, ""), (true, "won"),
                         (true, ""), (false, ""), (true, "lost"), (true, "") })
            {
                Race(sword, ok);
                if (roll.Length > 0) Race("Splendent Orichalcum Longsword", roll == "won", combo: true);
            }
            Race(expert, true); Race("Expert's Splendent Orichalcum Longsword", true, combo: true); Race(expert, false); Race(expert, true);
            ShowPart("Crafting", byHand: false);
            FillCraft(ue);
            SetTrackingUi(false);
            Say("Last: Expert's Orichalcum Longsword acquired", "Faint");
            return;
        }
        if (kind == "Crafting")
        {
            // the Crafting part with a morning's crafts from the crafting panel, priced, for a layout render (example figures)
            var ce = new SessionEngine(_items); var t0 = DateTime.UtcNow.AddMinutes(-30);
            PriceBook.SetBoard(new() { ["Sapphire Powder"] = 9_999, ["Sapphire Gemstone"] = 1_790, ["Diamond Powder"] = 4_150, ["Diamond Gemstone"] = 1_500,
                ["Orichalcum Greaves"] = 200_000, ["Orichalcum Ingot"] = 2_000, ["Strong Leather"] = 9_929, ["Refining Stone"] = 899, ["Odyle"] = 3_480 });
            int s = 0;
            void Craft(string recipe, bool ok)
            {
                var at = t0.AddSeconds(s += 90);
                ce.CraftPanelSeen(recipe, at);
                ce.Apply(new CraftResult(at, recipe, ok) { Via = SessionEngine.CraftPanelVia, Makes = RecipeBook.ForOutput(recipe)?.OutputQty ?? 1 });
            }
            Craft("Sapphire Powder", true); Craft("Sapphire Powder", true); Craft("Orichalcum Greaves", true); Craft("Orichalcum Greaves", false); Craft("Diamond Powder", true);
            ShowPart("Crafting", byHand: false);
            FillCraft(ce);
            SetTrackingUi(false);
            Say("Last: Diamond Powder acquired", "Faint");
            return;
        }
        ShowPart("Farming", byHand: false);   // a farming render, whichever part the player left open (options-test checks the views on it)
        var e = new SessionEngine(_items); var t = DateTime.UtcNow;
        // a price board for the layout render, as the site would send it, so the value tile shows a figure
        PriceBook.SetBoard(new() { ["Thick hide"] = 1_100, ["Spiritstone"] = 7_499, ["Manastone"] = 4_200, ["Orichalcum Ore"] = 1_950, ["Fine Orichalcum Ore"] = 9_800, ["Sapphire Gemstone"] = 12_000, ["Ruby Gemstone"] = 12_000, ["Odyle"] = 2_300, ["Kukuru"] = 900 });
        void Drop(string n, int c, bool b = false) { for (int i = 0; i < c; i++) e.Apply(new ItemAcquired(t, _items.IdOf(n), 1, b)); }
        void Kills(int n, int kinah) { for (int i = 0; i < n; i++) { e.Apply(new KinahChanged(t, kinah + (i % 7) * 40, false)); e.Apply(new CurrencyGained(t, "Oculazen Soul Points", 1)); } }
        void Pet(string n, int c) { for (int i = 0; i < c; i++) e.Apply(new PetCrystal(t, n + " Soul Crystal", 1)); }
        _mode = kind == "Gathering" ? Mode.Gather : kind == "Mixed" ? Mode.Mixed : Mode.Open;
        switch (_mode)
        {
            case Mode.Mixed:
                // a layout render of farming and gathering at once: mobs and nodes in the same stretch
                e.CreditZone("Western Cantas Valley", 42 * 60 + 17);
                e.SetTarget("Abyss Drakan Scout", t); Kills(61, 3690);
                Drop("Thick hide", 22); Drop("Spiritstone", 14);
                Drop("Orichalcum Ore", 38); Drop("Sapphire Gemstone", 4); Drop("Odyle", 12);
                for (int i = 0; i < 5; i++) e.Apply(new GatherFailed(t, "Essence Extraction"));
                break;
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
        _tableWhat = "this zone";   // no run on, as a player mostly has it (KL_SHOT_STATE=run draws one)
        if (_mode == Mode.Open) _extraOpen = "Mob";   // the mock opens Kinah by enemy in place
        _demoEngine = e; _demoElapsed = TimeSpan.FromMinutes(42) + TimeSpan.FromSeconds(17);
        LoadAccuracyDemo();
        FillNumbers(e, _demoElapsed);
        FillLive();
        FillAccuracyCard();
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
        SetTrackingUi(false);
        LiveMark.Visibility = Visibility.Visible;
        // all is well, so the footer says nothing on Farming (2026-10-28); the last line read is in ⋯
        _lastRead = _mode == Mode.Gather ? "Last: Sapphire Gemstone acquired" : "Last: Spiritstone acquired";
        Tell("", "Faint");
        if (market)
        {
            _demoMarket = "Market open · 318 asks read · 41 items · 318 shared · last read 12 s ago";
        }
        // KL_SHOT_UNDO=1: a render with the Undo bar up, as it shows just after Clear the tables (Ui/MainWindow.Undo.cs)
        if (Environment.GetEnvironmentVariable("KL_SHOT_UNDO") == "1") ShowUndoSample();
        // KL_SHOT_TODAY=1: the Today strip opened to its slots (0.8.50)
        FillToday();
        if (Environment.GetEnvironmentVariable("KL_SHOT_TODAY") == "1") OpenTodaySample();
    }
}
