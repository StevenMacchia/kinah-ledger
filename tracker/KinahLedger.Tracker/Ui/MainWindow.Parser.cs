using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using KinahLedger.Packets;
using KinahLedger.Parser;
using KinahLedger.Platform;
using KinahLedger.Session;

namespace KinahLedger.Ui;

/// <summary>
/// The Parser page (Steven, 2026-11-13: "It should have its own tab for parser", then "allow users to select just boss
/// only for parser or full run etc, give options"). Its mode says what is kept (Parser/BossParser.cs): Bosses only (each
/// boss pull in a Transcendence, an Expedition or a Sanctuary), Full run (each run of those, entering to leaving, every boss
/// a split) or Every fight (anywhere, the first hit or kill to a pause), each named on the page with what it counts. On
/// top, the record under way with its clock and the group (name, class, Combat Power, item level; damage and DPS only when
/// the game's messages give them, never made up) or, with none, where you are and what starts one. Under it, the records
/// kept on this PC for the character playing, by mode, kind and boss or instance; a click opens one. The overlay over the
/// game (Ui/ParserOverlay.cs) is switched here, with its shortcut, and has the mode on its own menu.
/// </summary>
public partial class MainWindow
{
    ParserPrefs _parserPrefs = new();
    /// <summary>The kept fight open in the detail view, or null for the list.</summary>
    string? _parserOpen;
    bool _parserAllRows;
    /// <summary>A layout render's sample (ParserDemo): the page shows it instead of this PC's fights.</summary>
    List<Encounter>? _parserDemoPast;
    Encounter? _parserDemoLive;
    (ContentKind Kind, string Instance) _parserDemoWhere;
    DateTime _parserDemoNow;
    ParserOverlay? _parserOverlay;
    /// <summary>The last fight ended, and when: the overlay shows its result for a few seconds after the kill.</summary>
    Encounter? _parserJustEnded;
    DateTime _parserEndedAt = DateTime.MinValue;
    string _parserLastLiveId = "";
    const double OverlayResultSeconds = 15;
    /// <summary>"Move overlay" is on: the separate overlay shows sample rows, never click-through, so it can be placed before any fight.</summary>
    bool _parserPlacing;
    (Encounter? Live, DateTime At) _parserPlaceSample;
    const int ParserHotkeyId = 3;
    const int ParserRowsAtRest = 10;

    /// <summary>Options › In game › Parser's switches (minimal by default): group details, kills and deaths, list filters, explanations on hover.</summary>
    bool PDetails => _parserPrefs.Details;
    bool PKills => _parserPrefs.KillsNear;
    bool PFilters => _parserPrefs.Filters;
    /// <summary>An explanation on hover, only when Options › In game › Parser › Explanations on hover is on.</summary>
    string? Tip(string text) => _parserPrefs.Hovers ? text : null;

    void InitParser(bool watch)
    {
        ParserPrefs.Saved += () => Dispatcher.BeginInvoke(() =>
        {
            var fresh = ParserPrefs.Load();
            _parserPrefs.Details = fresh.Details; _parserPrefs.KillsNear = fresh.KillsNear; _parserPrefs.Filters = fresh.Filters; _parserPrefs.Hovers = fresh.Hovers; _parserPrefs.Separate = fresh.Separate; _parserPrefs.DpsActive = fresh.DpsActive;
            Encounter.ActiveBasis = fresh.DpsActive;
            if (!fresh.Separate) _parserPlacing = false;
            ParserOverlayTick();
            DrawOverlayPills();   // the menu lists the Parser only while it is separate
            if (!_parserPrefs.Filters) { _parserPrefs.KindFilter = ""; _parserPrefs.BossFilter = ""; }
            RedrawParserPage();
        });
        _parserPrefs = watch ? ParserPrefs.Load() : new ParserPrefs();
        Encounter.ActiveBasis = _parserPrefs.DpsActive;
        if (watch) TrackerSession.ParserMode = ParserModeNow;   // a session made from now on starts in it
        ParserTab.Click += (_, _) => ShowPart("Parser");
        // five pills and the Timers countdown in the header's tab column: a little less room each, so Parser isn't cut off
        foreach (var tab in PartTabRow.Children.OfType<Button>()) tab.Padding = new Thickness(8, 0, 8, 0);
        // the header's Overlays menu (Ui/MainWindow.Overlays.cs): the Parser is a section of the Kinah overlay by default, and
        // an overlay of its own in the menu only once Settings › In game › Parser separates it
        RegisterOverlay("Parser", () => ParserOverlayOn, on => ParserOverlayOn = on, "Boss fights: DPS by member, the boss's HP, the pull timer.", visible: () => _parserPrefs.Separate || !HudWanted);
        if (!watch) return;
        SourceInitialized += (_, _) =>
        {
            var h = new WindowInteropHelper(this).Handle;
            HwndSource.FromHwnd(h)?.AddHook(ParserWndProc);
            RegisterParserHotkey(h);
        };
        Closed += (_, _) =>
        {
            var h = new WindowInteropHelper(this).Handle;
            if (h != IntPtr.Zero) Native.UnregisterHotKey(h, ParserHotkeyId);
            _parserOverlay?.Close();
        };
    }

    /// <summary>The overlay's shortcut (parser.json, Ctrl+Shift+P by default); one that's taken or not allowed is left off and logged.</summary>
    void RegisterParserHotkey(IntPtr h)
    {
        if (Hotkey.Parse(_parserPrefs.Hotkey) is not { } key) return;
        var got = key.Register(h, ParserHotkeyId);
        if (got is Hotkey.Outcome.Taken or Hotkey.Outcome.TakenByTracker)
            KeyTakenOnce($"parser: the overlay's shortcut {key}: {(got == Hotkey.Outcome.Taken ? "taken by another app" : "held by another Kinah Ledger")}, off");
    }

    IntPtr ParserWndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Native.WM_HOTKEY && wParam.ToInt32() == ParserHotkeyId) { SetParserOverlay(!_parserPrefs.Overlay); handled = true; }
        return IntPtr.Zero;
    }

    void SetParserOverlay(bool on)
    {
        _parserPrefs.Overlay = on;
        if (!_demo) _parserPrefs.Save();
        Say(on ? "Fight overlay on: it shows over the game during boss fights." : "Fight overlay off.", "Faint");
        ParserOverlayTick();
        DrawOverlayPills();
        RedrawParserPage();
    }

    /// <summary>
    /// The Parser overlay's switches for the window's header Overlays group and Options › Overlays (the redesign's shell):
    /// on/off, click-through and a reset of the spot it was dragged to, all kept in parser.json.
    /// </summary>
    internal bool ParserOverlayOn { get => _parserPrefs.Overlay; set { if (value != _parserPrefs.Overlay) SetParserOverlay(value); } }
    internal bool ParserOverlayClickThrough { get => _parserPrefs.ClickThrough; set { if (value != _parserPrefs.ClickThrough) SetParserClickThrough(value); } }
    internal void ResetParserOverlayPosition()
    {
        _parserPrefs.Left = null; _parserPrefs.Top = null;
        if (!_demo) _parserPrefs.Save();
        _parserOverlay?.Place(null, null, _watcher.GameRect);
    }

    void SetParserClickThrough(bool on)
    {
        _parserPrefs.ClickThrough = on;
        if (!_demo) _parserPrefs.Save();
        _parserOverlay?.SetClickThrough(on);
        RedrawParserPage();
    }

    /// <summary>What the Parser keeps now (parser.json).</summary>
    ParserMode ParserModeNow => BossParser.ModeOf(_parserPrefs.Mode);
    /// <summary>Which records the list shows: its own pick, else the mode's.</summary>
    ParserMode ParserListMode => _parserPrefs.ListMode.Length > 0 ? BossParser.ModeOf(_parserPrefs.ListMode) : ParserModeNow;

    /// <summary>The mode picked (the page or the overlay's menu): remembered, the session's parser switched, the list follows it.</summary>
    void SetParserMode(ParserMode mode)
    {
        if (mode == ParserModeNow && _parserPrefs.ListMode == BossParser.ModeKey(mode)) return;
        _parserPrefs.Mode = BossParser.ModeKey(mode);
        _parserPrefs.ListMode = BossParser.ModeKey(mode);
        _parserPrefs.KindFilter = ""; _parserPrefs.BossFilter = ""; _parserAllRows = false; _parserOpen = null;
        if (!_demo) _parserPrefs.Save();
        TrackerSession.ParserMode = mode;
        try { ParserFights?.SetMode(mode, DateTime.UtcNow); } catch (Exception ex) { CrashLog.Write("parser mode", ex); }
        if (_demo && _parserDemoState.Length > 0) { ShowParserDemo(mode switch { ParserMode.Run => "run", ParserMode.Fights => "fight", _ => "live" }); return; }
        Say($"Parser: {BossParser.Name(mode)}. {BossParser.Counts(mode)}", "Faint");
        ParserOverlayTick();
        RedrawParserPage();
    }

    static string Noun(ParserMode m, int n = 1) => m switch { ParserMode.Run => n == 1 ? "run" : "runs", ParserMode.Fights => n == 1 ? "fight" : "fights", _ => n == 1 ? "boss pull" : "boss pulls" };

    // ---- what the page shows ----

    BossParser? ParserFights => ParserSourceForTest ?? _watcher.Chat?.Fights;

    // ---- real time (Steven, 2026-11-14: "the parser needs to be SUPER low delay, it can not be laggy, it needs to register
    // in real time") ----
    // The Parser says each change as it happens, on the packet reader's thread (BossParser.Changed). The tab and the overlay
    // redraw on it at once, at most every ParserFrameMs (no waiting on the window's 1 s tick), and the time from the oldest
    // undrawn event's capture to the frame that shows it is kept (ParserLatencyStats; reads.log gets a line a minute).

    /// <summary>The shortest time between two redraws: hits batch into one frame instead of a redraw each.</summary>
    const double ParserFrameMs = 60;
    BossParser? _parserWatched;
    readonly object _parserDrawGate = new();
    DateTime? _parserOldest;
    bool _parserDrawQueued;
    long _parserLastDraw;
    System.Threading.Timer? _parserDrawTimer;
    DateTime _parserStatsAt = DateTime.UtcNow;
    /// <summary>Capture to screen, in ms, of the Parser's redraws.</summary>
    internal static ParserLatency ParserLatencyStats { get; } = new();
    /// <summary>The same since the last line in reads.log (the line a minute starts it again; the one above keeps everything).</summary>
    static readonly ParserLatency ParserLatencyMinute = new();
    /// <summary>A replay's Parser (parser-latency), in place of the session's; the overlay shows whatever is in front.</summary>
    internal BossParser? ParserSourceForTest { get; set; }
    internal bool ParserOverlayForceForTest { get; set; }

    /// <summary>Listens to the Parser of the session playing (a new session's when it changes).</summary>
    internal void WatchParser()
    {
        var f = ParserFights;
        if (ReferenceEquals(f, _parserWatched)) return;
        if (_parserWatched != null) _parserWatched.Changed -= OnParserChanged;
        _parserWatched = f;
        if (f != null) f.Changed += OnParserChanged;
    }

    /// <summary>A change, from any thread: one redraw is queued, now or when the last one is <see cref="ParserFrameMs"/> old.</summary>
    /// <summary>For parser-latency: changes the Parser said, frames drawn for them, of which on screen.</summary>
    internal static int ParserChangesSeen, ParserDraws, ParserDrawsShown;
    void OnParserChanged(DateTime? at)
    {
        Interlocked.Increment(ref ParserChangesSeen);
        lock (_parserDrawGate)
        {
            if (at is { } a && (_parserOldest == null || a < _parserOldest)) _parserOldest = a;
            if (_parserDrawQueued) return;
            _parserDrawQueued = true;
        }
        double since = System.Diagnostics.Stopwatch.GetElapsedTime(_parserLastDraw).TotalMilliseconds;
        if (_parserLastDraw == 0 || since >= ParserFrameMs) Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render, ParserDrawNow);
        else
        {
            _parserDrawTimer ??= new System.Threading.Timer(_ => Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render, ParserDrawNow));
            _parserDrawTimer.Change((int)Math.Ceiling(ParserFrameMs - since), System.Threading.Timeout.Infinite);
        }
    }

    /// <summary>The redraw: the tab when it shows, the overlay; the time to the frame that shows it kept.</summary>
    void ParserDrawNow()
    {
        DateTime? oldest;
        Interlocked.Increment(ref ParserDraws);
        lock (_parserDrawGate) { oldest = _parserOldest; _parserOldest = null; _parserDrawQueued = false; }
        _parserLastDraw = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            if (_part == "Parser") FillParserPage();
            ParserOverlayTick();
        }
        catch (Exception ex) { CrashLog.Write("parser page", ex); }
        bool shown = (_part == "Parser" && IsVisible) || _parserOverlay is { IsVisible: true } || _hud is { IsVisible: true, ParserShown: true };
        if (oldest is not { } o || !shown) return;
        Interlocked.Increment(ref ParserDrawsShown);
        EventHandler? h = null;
        h = (_, _) =>
        {
            System.Windows.Media.CompositionTarget.Rendering -= h;
            double ms = Math.Max(0, (DateTime.UtcNow - o).TotalMilliseconds);
            ParserLatencyStats.Add(ms); ParserLatencyMinute.Add(ms);
        };
        System.Windows.Media.CompositionTarget.Rendering += h;
    }

    /// <summary>A line a minute in reads.log: how fast the Parser drew.</summary>
    void ParserLatencyNote()
    {
        if ((DateTime.UtcNow - _parserStatsAt).TotalSeconds < 60) return;
        _parserStatsAt = DateTime.UtcNow;
        var (p50, p95, max, n) = ParserLatencyMinute.Stats();
        if (n == 0) return;
        CrashLog.Note($"parser: {n} redraws, capture to screen median {p50:0} ms, 95th percentile {p95:0} ms, worst {max:0} ms");
        ParserLatencyMinute.Clear();
    }
    DateTime ParserNow => _demo ? _parserDemoNow : DateTime.UtcNow;
    Encounter? ParserLive => _demo ? _parserDemoLive : ParserFights?.Live;
    (ContentKind Kind, string Instance) ParserWhere => _demo ? _parserDemoWhere : ParserFights?.Where ?? (ContentKind.Other, "");
    List<Encounter> ParserPast() => _demo ? _parserDemoPast ?? new() : TrackerSession.FightStore.All();
    /// <summary>The session's parser in the mode the page says (a session made before the pick, or after a restart of the reader).</summary>
    void ParserModeSync() { if (!_demo && ParserFights is { } f && f.Mode != ParserModeNow) f.SetMode(ParserModeNow, DateTime.UtcNow); }

    /// <summary>The window's tick: the page while it shows, and the overlay always.</summary>
    void ParserTick()
    {
        try
        {
            ParserModeSync();
            WatchParser();
            ParserLatencyNote();
            if (_part == "Parser") FillParserPage();
            ParserOverlayTick();
        }
        catch (Exception ex) { CrashLog.Write("parser page", ex); }
    }

    void RedrawParserPage() { _drawnSig.Remove("parserpage"); FillParserPage(); }

    /// <summary>The Parser has a window of its own over the game: Options › In game › Parser › Separate parser overlay, or no Kinah overlay to sit in.</summary>
    internal bool ParserOverlaySeparate => _parserPrefs.Separate || !HudWanted || _hud == null;

    /// <summary>
    /// The Parser over the game follows the record: on (its switch), one under way or one that ended in the last
    /// <see cref="OverlayResultSeconds"/>. By default it is the Kinah overlay's section above the Kinah line (the Kinah
    /// overlay shows itself while the game is in front); separated, its own window, while the game (or the tracker) is in front.
    /// </summary>
    void ParserOverlayTick()
    {
        if (_demo && !_parserPlacing) return;
        var now = DateTime.UtcNow;
        var live = ParserLive;
        // the fight that just ended: its result shows a little while
        if (live == null && _parserLastLiveId.Length > 0)
        {
            _parserJustEnded = ParserFights?.LastEnded is { } le && le.Id == _parserLastLiveId ? le : TrackerSession.FightStore.All().LastOrDefault(e => e.Id == _parserLastLiveId);
            _parserEndedAt = now;
            _parserLastLiveId = "";
        }
        if (live != null) { _parserLastLiveId = live.Id; _parserJustEnded = null; }
        var show = live ?? ((now - _parserEndedAt).TotalSeconds <= OverlayResultSeconds ? _parserJustEnded : null);
        if (!ParserOverlaySeparate)
        {
            // expanded by default: the section above the Kinah line; its own window stays away
            _parserOverlay?.ShowWanted(false);
            _hud!.ShowParser(_parserPrefs.Overlay ? show : null, now, ParserModeNow, PDetails);
            return;
        }
        _hud?.ShowParser(null, now, ParserModeNow, PDetails);
        if (_parserPlacing)
        {
            // placing: sample rows wherever the game is, held still and draggable until Done
            if (_parserPlaceSample.Live == null) _parserPlaceSample = (ParserDemo.Sample(now).Live, now);
            show = _parserPlaceSample.Live;
            now = _parserPlaceSample.At;
        }
        bool inFront = ParserOverlayForceForTest || (_watcher.GameRect != null && (Native.ForegroundPid() == (uint)Environment.ProcessId || GameInFront()));
        bool want = _parserPlacing ? show != null : _parserPrefs.Overlay && show != null && inFront;
        if (!want) { _parserOverlay?.ShowWanted(false); return; }
        if (_parserOverlay == null)
        {
            _parserOverlay = new ParserOverlay { HotkeyText = Hotkey.Parse(_parserPrefs.Hotkey)?.ToString() ?? "" };
            _parserOverlay.Dropped += (x, y) => { _parserPrefs.Left = x; _parserPrefs.Top = y; _parserPrefs.Save(); };
            _parserOverlay.ModePicked += SetParserMode;
            _parserOverlay.HideAsked += () => SetParserOverlay(false);
            _parserOverlay.ClickThroughAsked += () => SetParserClickThrough(true);
            _parserOverlay.SetClickThrough(_parserPrefs.ClickThrough);
            _parserOverlay.Mode = ParserModeNow;
            _parserOverlay.ShowDetails = PDetails;
            _parserOverlay.Draw(show!, now);
            _parserOverlay.ShowWanted(true);
            _parserOverlay.SetClickThrough(_parserPrefs.ClickThrough);
            _parserOverlay.Place(_parserPrefs.Left, _parserPrefs.Top, _watcher.GameRect);
            return;
        }
        _parserOverlay.Mode = ParserModeNow;
        _parserOverlay.ShowDetails = PDetails;
        _parserOverlay.Draw(show!, now);
        _parserOverlay.ShowWanted(true);
    }

    /// <summary>"Move overlay" on or off: on, the separate overlay shows sample rows and takes the pointer; off (Done), it goes back to its fights and its click-through.</summary>
    internal void SetParserPlacing(bool on)
    {
        _parserPlacing = on && _parserPrefs.Separate;
        _parserPlaceSample = (null, default);
        _parserOverlay?.SetClickThrough(!_parserPlacing && _parserPrefs.ClickThrough);
        if (!_parserPlacing) _parserOverlay?.ShowWanted(false);   // a fight under way shows it again on the next tick
        ParserOverlayTick();
        // where it was dropped last, else its default place over the game, else over the main screen (no game running yet)
        // a test's window (the options test) keeps it off every screen
        if (_parserPlacing && _parserOverlay != null && _demo) { _parserOverlay.Left = -12000; _parserOverlay.Top = -12000; }
        else if (_parserPlacing && _parserOverlay != null) { _parserOverlay.SetClickThrough(false); _parserOverlay.Place(_parserPrefs.Left, _parserPrefs.Top, _watcher.GameRect ?? System.Windows.Forms.Screen.PrimaryScreen?.Bounds); }
        RedrawParserPage();
    }

    internal bool ParserPlacingForTest => _parserPlacing;

    void FillParserPage()
    {
        bool show = _part == "Parser" && !_welcome;
        ParserPage.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (!show) { _drawnSig.Remove("parserpage"); return; }
        var now = ParserNow;
        var live = ParserLive;
        var past = ParserPast();
        var where = ParserWhere;
        var sig = $"{live?.Id}:{(int)(live?.Duration(now).TotalSeconds ?? 0)}:{live?.Roster.Sum(f => f.Damage)}:{live?.Roster.Count}:{live?.Kills.Count}:{live?.Partial}:{live?.BossHp}|{past.Count}:{past.LastOrDefault()?.Id}|{where}|{_parserOpen}|{_parserAllRows}|{_parserPrefs.KindFilter}|{_parserPrefs.BossFilter}|{_parserPrefs.Overlay}:{_parserPrefs.ClickThrough}:{_parserPrefs.Separate}:{_parserPlacing}:{Encounter.ActiveBasis}|{string.Join(",", _parserAbilitiesOpen)}|{Skins.Current}|{ParserStatus(where).Line}|{_parserPrefs.Mode}:{_parserPrefs.ListMode}|{live?.Splits.Count}:{live?.YourKills}:{live?.YourDeaths}:{live?.Boss}";
        if (!Changed("parserpage", sig)) return;

        ParserPage.Children.Clear();
        ParserPage.Children.Add(ParserHead(past));
        var open = _parserOpen != null ? past.FirstOrDefault(e => e.Id == _parserOpen) : null;
        if (open != null) { ParserPage.Children.Add(ParserDetail(open)); return; }
        _parserOpen = null;
        ParserPage.Children.Add(live != null ? ParserLiveCard(live, now) : ParserWaitingCard(where, past));
        ParserPage.Children.Add(ParserHistory(past, now));
    }

    /// <summary>The page's head: the title and the overlay's switches, then the mode (Bosses only, Full run, Every fight) and what it counts.</summary>
    FrameworkElement ParserHead(List<Encounter> past)
    {
        var box = new StackPanel { Margin = new Thickness(2, 0, 0, 10) };
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.Children.Add(PT("Parser", 20, FontWeights.SemiBold, "Ink", display: true));
        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        right.Children.Add(ParserSwitch("Overlay", _parserPrefs.Overlay, SetParserOverlay,
            $"The record under way over the game: the boss or the run, the clock and the group. {(_parserPrefs.Separate ? "Its own overlay (Settings › In game › Parser); right-click it for the mode." : "It opens above the Kinah overlay when a fight starts and folds away after.")}{(Hotkey.Parse(_parserPrefs.Hotkey) is { } k ? $" Shortcut: {k}." : "")}", "Fight overlay"));
        // click-through and the drag are the separate overlay's; above the Kinah line it follows the Kinah overlay's own
        if (_parserPrefs.Separate)
        {
            var move = new Button { Content = _parserPlacing ? "Done" : "Move overlay", Height = 26, FontSize = 12, Padding = new Thickness(12, 0, 12, 0), Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center,
                ToolTip = Tip("Shows the overlay with sample rows: drag it where you want it over the game, then press Done. It opens there for every fight.") };
            move.SetResourceReference(StyleProperty, "PillBtn");
            AutomationProperties.SetName(move, _parserPlacing ? "Done moving the overlay" : "Move overlay");
            move.Click += (_, _) => SetParserPlacing(!_parserPlacing);
            right.Children.Insert(0, move);
        }
        if (_parserPrefs.Separate)
            right.Children.Add(Margin(ParserSwitch("Click-through", _parserPrefs.ClickThrough, SetParserClickThrough,
                "On: clicks go through the overlay to the game. Off: drag the overlay where you want it; it stays there.", "Overlay click-through"), 14, 0, 0, 0));
        Grid.SetColumn(right, 1);
        g.Children.Add(right);
        box.Children.Add(g);
        // the mode: three choices, and one line on what the one picked counts
        var modes = new StackPanel { Orientation = Orientation.Horizontal };
        var seg = new Border { Padding = new Thickness(3), CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), Child = modes, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0) };
        seg.SetResourceReference(Border.BackgroundProperty, "SegGroup"); seg.SetResourceReference(Border.BorderBrushProperty, "CardLine");
        AutomationProperties.SetName(seg, "What the Parser keeps");
        foreach (var md in new[] { ParserMode.Bosses, ParserMode.Run, ParserMode.Fights })
        {
            var b = new Button { Content = BossParser.Name(md), Height = 26, FontSize = 12.5, Padding = new Thickness(12, 0, 12, 0), Margin = new Thickness(modes.Children.Count > 0 ? 2 : 0, 0, 0, 0), ToolTip = Tip(BossParser.Counts(md)) };
            b.SetResourceReference(StyleProperty, md == ParserModeNow ? "SegOn" : "SegOff");
            AutomationProperties.SetName(b, "Parser mode: " + BossParser.Name(md));
            var pick = md;
            b.Click += (_, _) => SetParserMode(pick);
            modes.Children.Add(b);
        }
        box.Children.Add(seg);
        var counts = PT(BossParser.Counts(ParserModeNow), 12, FontWeights.Medium, "Muted");
        counts.TextWrapping = TextWrapping.Wrap; counts.Margin = new Thickness(0, 6, 0, 0);
        box.Children.Add(counts);
        return box;
    }

    FrameworkElement ParserSwitch(string label, bool on, Action<bool> set, string tip, string name)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, ToolTip = Tip(tip) };
        var t = PT(label, 12, FontWeights.SemiBold, on ? "Ink" : "Muted");
        t.Margin = new Thickness(0, 0, 7, 0);
        var cb = new CheckBox { IsChecked = on, VerticalAlignment = VerticalAlignment.Center };
        cb.SetResourceReference(StyleProperty, "Switch");
        AutomationProperties.SetName(cb, name);
        cb.Click += (_, _) => set(cb.IsChecked == true);
        t.MouseLeftButtonUp += (_, _) => set(!on);
        t.Cursor = Cursors.Hand;
        sp.Children.Add(t); sp.Children.Add(cb);
        return sp;
    }

    // ---- the fight under way ----

    FrameworkElement ParserLiveCard(Encounter e, DateTime now)
    {
        var card = PCard(hero: true);
        var body = new StackPanel();
        body.Children.Add(FightHead(e, now, live: true));
        if (e.ModeKind == ParserMode.Run)
        {
            // the pull under way inside the run, with its boss's HP; then the splits so far
            if (e.Boss.Length > 0) body.Children.Add(Margin(PullNow(e, now), 0, 10, 0, 0));
            if (e.Splits.Count > 0) body.Children.Add(Margin(SplitsTable(e, live: true), 0, 10, 0, 0));
        }
        else if (e.BossHpLeft is { } left) body.Children.Add(Margin(BossHpBar(e, left), 0, 10, 0, 0));
        if (FightChips(e, now, live: true) is { } chips) body.Children.Add(Margin(chips, 0, 10, 0, 0));
        if (e.Roster.Count > 0 || e.ModeKind == ParserMode.Bosses) body.Children.Add(Margin(RosterTable(e, now, detail: false), 0, 10, 0, 0));
        card.Child = body;
        return card;
    }

    /// <summary>A run's pull under way: "Now" and the boss, its clock, its HP bar.</summary>
    FrameworkElement PullNow(Encounter e, DateTime now)
    {
        var box = new Border { CornerRadius = new CornerRadius(9), Padding = new Thickness(10, 7, 10, 8), BorderThickness = new Thickness(1) };
        box.SetResourceReference(Border.BackgroundProperty, "GoldTint"); box.SetResourceReference(Border.BorderBrushProperty, "GoldLine");
        var sp = new StackPanel();
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var l = new StackPanel { Orientation = Orientation.Horizontal };
        l.Children.Add(PT("Now", 11.5, FontWeights.SemiBold, "Gold"));
        l.Children.Add(Margin(PT(e.Boss, 13.5, FontWeights.Bold, "Ink", trim: true), 8, 0, 0, 0));
        g.Children.Add(l);
        var pull = e.Splits.LastOrDefault(x => x.Outcome == "");
        var clock = PT(pull != null ? BossParser.Clock(TimeSpan.FromSeconds(pull.Seconds)) : "", 13.5, FontWeights.Bold, "Gold");
        Grid.SetColumn(clock, 1); g.Children.Add(clock);
        sp.Children.Add(g);
        if (e.BossHpLeft is { } left) sp.Children.Add(Margin(BossHpBar(e, left), 0, 6, 0, 0));
        box.Child = sp;
        return box;
    }

    /// <summary>A run's boss pulls: the boss (final marked), when into the run, how long, how it ended, the group's DPS.</summary>
    FrameworkElement SplitsTable(Encounter e, bool live)
    {
        var host = new StackPanel();
        bool dps = e.Splits.Any(x => x.Dps > 0);
        Grid Row()
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(62) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(104) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(dps ? 66 : 0) });
            return g;
        }
        void Cell(Grid g, int col, FrameworkElement el) { Grid.SetColumn(el, col); g.Children.Add(el); }
        var head = Row(); head.Margin = new Thickness(0, 0, 0, 3);
        Cell(head, 1, PT("Boss", 11, FontWeights.SemiBold, "Faint"));
        Cell(head, 2, Right(PT("At", 11, FontWeights.SemiBold, "Faint")));
        Cell(head, 3, Right(PT("Time", 11, FontWeights.SemiBold, "Faint")));
        if (dps) Cell(head, 5, Right(PT("Group DPS", 11, FontWeights.SemiBold, "Faint")));
        host.Children.Add(head);
        int i = 0;
        foreach (var x in e.Splits)
        {
            var row = Row();
            var rule = new Border { BorderThickness = new Thickness(0, i == 0 ? 0 : 1, 0, 0), Padding = new Thickness(0, 4, 0, 4), Child = row };
            rule.SetResourceReference(Border.BorderBrushProperty, "CardHair");
            Cell(row, 0, PT((++i).ToString(CultureInfo.InvariantCulture), 11.5, FontWeights.SemiBold, "Faint"));
            var nm = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 8, 0) };
            nm.Children.Add(PT(x.Boss, 12.5, FontWeights.SemiBold, x.Outcome == "" ? "Gold" : "Ink", trim: true));
            if (x.Final) nm.Children.Add(Margin(PT("final", 11, FontWeights.Medium, "Faint"), 6, 1, 0, 0));
            if (x.Wave) nm.Children.Add(Margin(PT("waves", 11, FontWeights.Medium, "Faint"), 6, 1, 0, 0));
            if (x.Partial) nm.Children.Add(Margin(PT("partial", 11, FontWeights.SemiBold, "Warn"), 6, 1, 0, 0));
            Cell(row, 1, nm);
            Cell(row, 2, Right(PT(BossParser.Clock(x.Start - e.Start), 12.5, FontWeights.Medium, "Muted")));
            Cell(row, 3, Right(PT(BossParser.Clock(TimeSpan.FromSeconds(x.Seconds)), 12.5, FontWeights.SemiBold, "Ink")));
            var tone = x.Outcome switch { "kill" => ("Killed", "Mint", "MintTint"), "wipe" => ("Wiped", "Coral", "CoralTint"), "left" => ("Left", "Muted", "ChipBg"), _ => ("In progress", "Gold", "GoldTint") };
            var chip = PChip(tone.Item1, tone.Item2, tone.Item3, dot: x.Outcome == ""); chip.Margin = new Thickness(10, 0, 0, 0);
            Cell(row, 4, chip);
            if (dps) Cell(row, 5, Right(PT(x.Dps > 0 ? Amounts.Short(x.Dps) : "–", 12.5, FontWeights.SemiBold, "Text2")));
            host.Children.Add(rule);
        }
        return host;
    }

    /// <summary>The fight's head: the boss's art, its name, the instance and kind under it; the clock and the outcome on the right.</summary>
    FrameworkElement FightHead(Encounter e, DateTime now, bool live)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var artName = e.ModeKind == ParserMode.Run ? "dungeon" : e.ModeKind == ParserMode.Fights && e.Boss.Length == 0 ? (e.Kind == "Abyss" ? "rift" : "medal") : "boss";
        var art = new Border { Width = 44, Height = 44, CornerRadius = new CornerRadius(10), Margin = new Thickness(0, 0, 12, 0), Child = GameArt.Element(artName, 34) };
        art.SetResourceReference(Border.BackgroundProperty, "SegGroup");
        art.SetResourceReference(Border.BorderBrushProperty, live ? "GoldLine" : "CardHair");
        art.BorderThickness = new Thickness(1);
        g.Children.Add(art);
        var mid = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var name = PT(e.Title, live ? 22 : 19, FontWeights.SemiBold, "Ink", trim: true, display: true);
        AutomationProperties.SetName(name, $"{(e.ModeKind == ParserMode.Run ? "Run" : e.ModeKind == ParserMode.Fights ? "Fight" : "Boss")} {e.Title}");
        mid.Children.Add(name);
        var sub = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 0) };
        var place = BossBook.Strip(e.Instance);
        bool showPlace = place.Length > 0 && place != e.Title && place != e.Kind;
        // "Ferocious Horn Den · Conquest": the instance and the game's mode for it, then the kind
        var lead = showPlace ? (e.GameMode.Length > 0 ? $"{place} · {e.GameMode}" : place) : e.GameMode;
        if (lead.Length > 0) sub.Children.Add(PT(lead, 12, FontWeights.Medium, "Muted", trim: true));
        sub.Children.Add(PT((lead.Length > 0 ? "  ·  " : "") + KindLabel(e), 12, FontWeights.Medium, lead.Length > 0 ? "Faint" : "Muted"));
        if (e.ModeKind != ParserMode.Run && e.FinalBoss) sub.Children.Add(PT("  ·  final boss", 12, FontWeights.Medium, "Faint"));
        if (e.ModeKind == ParserMode.Run) sub.Children.Add(PT($"  ·  {e.Splits.Count(x => x.Outcome == "kill")} of {e.Splits.Select(x => x.Boss).Distinct().Count()} bosses down", 12, FontWeights.Medium, "Faint"));
        mid.Children.Add(sub);
        Grid.SetColumn(mid, 1); g.Children.Add(mid);
        var right = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(12, 0, 0, 0) };
        var clock = PT(BossParser.Clock(e.Duration(now)), live ? 30 : 24, FontWeights.Bold, live ? "Gold" : "Ink", display: true);
        clock.HorizontalAlignment = HorizontalAlignment.Right;
        AutomationProperties.SetName(clock, "Time " + BossParser.Clock(e.Duration(now)));
        right.Children.Add(clock);
        var chipRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 2, 0, 0) };
        chipRow.Children.Add(OutcomeChip(e));
        right.Children.Add(chipRow);
        Grid.SetColumn(right, 2); g.Children.Add(right);
        return g;
    }

    static string KindLabel(Encounter e)
    {
        // "Shattered Arkanis [Stage 3]" says its stage beside the kind
        int i = e.Instance.IndexOf(" [", StringComparison.Ordinal);
        var stage = i > 0 ? e.Instance[(i + 2)..].TrimEnd(']') : "";
        return stage.Length > 0 ? $"{e.Kind} {stage}" : e.Kind;
    }

    /// <summary>"Killed", "Cleared" or "3 kills" in mint, "Wiped" or "Died" in coral, "Left" or "Ended" quiet, "In progress" in gold with a dot.</summary>
    FrameworkElement OutcomeChip(Encounter e)
    {
        var (ink, tint) = e.Tone switch { "good" => ("Mint", "MintTint"), "bad" => ("Coral", "CoralTint"), "quiet" => ("Muted", "ChipBg"), _ => ("Gold", "GoldTint") };
        return PChip(e.OutcomeLabel, ink, tint, dot: e.Live);
    }

    /// <summary>The figures line: group DPS and boss damage when known, kills near you, deaths, and partial with why.</summary>
    FrameworkElement? FightChips(Encounter e, DateTime now, bool live)
    {
        var wp = new WrapPanel();
        void Add(FrameworkElement c) { c.Margin = new Thickness(0, 0, 6, 4); wp.Children.Add(c); }
        if (e.HasDamage)
        {
            var gd = PStat("Group DPS", Amounts.Short(e.GroupDps(now)), "Ink", e.ModeKind == ParserMode.Run ? "boss pulls" : "");
            if (e.ModeKind == ParserMode.Run) gd.ToolTip = Tip("Over the run's boss pulls: the damage counted is the bosses'.");
            Add(gd);
            if (PDetails && e.BossDamage > 0 && e.BossMaxHp <= 0) Add(PStat("Boss damage", Amounts.Short(e.BossDamage), "Ink"));
            if (PDetails && e.BossMaxHp > 0 && !live) Add(PStat("Boss HP", Amounts.Short(e.BossMaxHp), "Ink", e.Outcome == "kill" ? "" : e.BossHpLeft is { } l ? $"{l:P0} left" : ""));
        }
        if ((PKills || e.ModeKind == ParserMode.Fights) && e.YourKills > 0) Add(PStat("Your kills", e.YourKills.ToString("N0", CultureInfo.InvariantCulture), "Ink", e.PlayerKills > 0 ? $"{e.PlayerKills} player{(e.PlayerKills == 1 ? "" : "s")}" : ""));
        int adds = e.Kills.Count(k => !k.Boss && !k.Yours);
        bool fight = e.ModeKind == ParserMode.Fights;
        if (PKills && adds > 0) Add(PStat("Kills near you", adds.ToString("N0", CultureInfo.InvariantCulture), "Ink"));
        int deaths = Math.Max(e.Roster.Sum(f => f.Deaths), e.YourDeaths);
        if ((PKills || fight) && deaths > 0) Add(PStat("Deaths", deaths.ToString("N0", CultureInfo.InvariantCulture), "Coral"));
        if (PDetails && !e.HasDamage && e.Members.Any(f => f.CombatPower > 0)) Add(PStat("Group CP", AvgCp(e), "Ink", "average"));
        if (e.Partial) { var c = PChip("Partial", "Warn", "GoldTint"); c.ToolTip = Tip($"Some of this {Noun(e.ModeKind)} wasn't seen: {string.Join("; ", e.PartialWhy)}. It is still one {Noun(e.ModeKind)}; its damage and kills may be short."); Add(c); }
        return wp.Children.Count > 0 ? wp : null;
    }

    static string AvgCp(Encounter e)
    {
        var known = e.Roster.Where(f => f.CombatPower > 0).ToList();
        return known.Count == 0 ? "–" : ((int)Math.Round(known.Average(f => f.CombatPower))).ToString("N0", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The group (Steven, 2026-11-14: "thicker bars that act as a background for the character ... show the icon for what
    /// class they are in the game ... Color coordinate it"): each row on a meter bar in its class's colour (Ui/ClassArt.cs),
    /// as long as its share of the top row's damage (its Combat Power before any damage), the class's own emblem at its left,
    /// then name (you marked), class, Combat Power, item level; with damage, damage and DPS; in the detail view, deaths too.
    /// Unknown figures say "–", never 0.
    /// </summary>
    FrameworkElement RosterTable(Encounter e, DateTime now, bool detail)
    {
        var host = new StackPanel();
        if (e.Roster.Count == 0)
        {
            host.Children.Add(PT("The group wasn't seen.", 12, FontWeights.Medium, "Faint"));
            return host;
        }
        bool dmg = e.HasDamage;
        bool deaths = detail && PKills && e.Roster.Any(f => f.Deaths > 0);
        // before the group list comes (damage only), the rows are who hit the boss: no class, CP or item level to show
        bool gear = PDetails && e.Members.Any(f => f.CombatPower > 0 || f.ItemLevel > 0 || f.Class.Length > 0);
        var cols = new List<(double Width, string Head, bool Right)> { (34, "", false), (0, "Member", false), (gear ? 92 : 0.01, gear ? "Class" : "", false), (gear ? 64 : 0.01, gear ? "CP" : "", true), (gear ? 62 : 0.01, gear ? "Item lvl" : "", true) };
        // Steven, 2026-11-14: "should give DPS and % not just a %": each row says "48.2K DPS · 25.1%"; its damage on hover,
        // and as a column of its own in the detail view (and with Group details)
        bool dmgCol = dmg && (detail || PDetails);
        if (dmg) { cols.Add((dmgCol ? 76 : 0.01, dmgCol ? "Damage" : "", true)); cols.Add((150, "", true)); }
        if (deaths) cols.Add((50, "Deaths", true));
        Grid Row()
        {
            var g = new Grid();
            foreach (var c in cols) g.ColumnDefinitions.Add(new ColumnDefinition { Width = c.Width > 0 ? new GridLength(c.Width) : new GridLength(1, GridUnitType.Star) });
            return g;
        }
        void Cell(Grid g, int col, FrameworkElement el) { Grid.SetColumn(el, col); g.Children.Add(el); }

        var head = Row(); head.Margin = new Thickness(6, 0, 4, 4);
        for (int i = 0; i < cols.Count; i++)
        {
            if (cols[i].Head.Length == 0) continue;
            var t = PT(cols[i].Head, 11, FontWeights.SemiBold, "Faint");
            if (cols[i].Right) t.HorizontalAlignment = HorizontalAlignment.Right;
            Cell(head, i, t);
        }
        // minimal (no column but the name's): no head row, the rows say what their figures are
        if (cols.Skip(2).Any(c => c.Head.Length > 0)) host.Children.Add(head);
        // what the bars measure: damage when the game gives it, else Combat Power (the group's strength before a hit lands)
        double Measure(Fighter f) => dmg ? f.Damage : f.CombatPower;
        double top = Math.Max(1, e.Roster.Max(Measure));
        bool first = true;
        // Steven, 2026-11-14: "don't count [a member] until they have at least 1 [damage]": the live card shows those who
        // have; the detail lists everyone, those with none last and quiet
        var rowsShown = detail ? e.Ranked().ToList() : e.Shown().ToList();
        foreach (var f in rowsShown)
        {
            var row = Row();
            row.Margin = new Thickness(6, 0, 4, 0);
            var meter = ClassArt.MeterRow(row, f.Class, Measure(f) / top, other: f.Other);
            meter.Margin = new Thickness(0, first ? 0 : 3, 0, 0);
            var rule = new Border { Padding = new Thickness(0), Child = meter, MinHeight = 34 };
            row.MinHeight = 34;
            first = false;
            Cell(row, 0, f.Other ? OtherMark(24) : ClassArt.Icon(f.Class, 24, f.Name));
            var nameBox = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            var nameLine = new StackPanel { Orientation = Orientation.Horizontal };
            var nm = PT(f.Other ? "Other" : f.Name.Length > 0 ? f.Name : "Unnamed", 13, f.Other ? FontWeights.Medium : FontWeights.SemiBold, f.Other || f.Name.Length == 0 ? "Muted" : "Ink", trim: true);
            if (f.Other) nm.ToolTip = Tip("HP the boss lost with no hit to name its source, such as damage over time. Shown so the rows add up to the HP it lost.");
            else if (f.Name.Length == 0) nm.ToolTip = Tip("Someone not named yet: a pet, or a player not seen up close.");
            nameLine.Children.Add(nm);
            // you: a gold pill (the name stays in ink: gold would read as a class colour on the bars)
            if (f.You) { var you = PChip("you", "GoldInk", "Gold"); you.Margin = new Thickness(7, 0, 0, 0); you.Padding = new Thickness(6, 0, 6, 1); nameLine.Children.Add(you); }
            if (f.Late) nameLine.Children.Add(Margin(PT("joined late", 11, FontWeights.Medium, "Faint"), 6, 1, 0, 0));
            if (f.Gone) nameLine.Children.Add(Margin(PT("left", 11, FontWeights.Medium, "Faint"), 6, 1, 0, 0));
            nameBox.Children.Add(nameLine);
            Cell(row, 1, nameBox);
            if (dmg && f.Damage <= 0 && !f.Other) { meter.Opacity = 0.55; nameLine.Children.Add(Margin(PT("no damage", 11, FontWeights.Medium, "Faint"), 6, 1, 0, 0)); }
            // a member's damage by ability: a click opens it under the row
            string abilityKey = $"{e.Id}:{f.Id}";
            bool open = _parserAbilitiesOpen.Contains(abilityKey);
            if (dmg && f.Abilities.Count > 0)
            {
                nameLine.Children.Add(Margin(PT(open ? "▾" : "▸", 11, FontWeights.SemiBold, "Faint"), 6, 0, 0, 0));
                meter.Cursor = Cursors.Hand;
                meter.ToolTip = Tip("This member's damage by ability");
                meter.MouseLeftButtonUp += (_, ev) => { ev.Handled = true; if (!_parserAbilitiesOpen.Remove(abilityKey)) _parserAbilitiesOpen.Add(abilityKey); RedrawParserPage(); };
            }
            if (gear && !f.Other)
            {
                Cell(row, 2, PT(f.Class.Length > 0 ? ClassArt.Title(f.Class) : "–", 12, FontWeights.SemiBold, "Text2", trim: true));
                Cell(row, 3, Right(PT(f.CombatPower > 0 ? f.CombatPower.ToString("N0", CultureInfo.InvariantCulture) : "–", 13, FontWeights.SemiBold, "Ink")));
                Cell(row, 4, Right(PT(f.ItemLevel > 0 ? f.ItemLevel.ToString("N0", CultureInfo.InvariantCulture) : "–", 13, FontWeights.Medium, "Text2")));
            }
            int c = 5;
            if (dmg)
            {
                if (dmgCol) Cell(row, c, Right(PT(Amounts.Short(f.Damage), 13, FontWeights.Medium, "Text2")));
                c++;
                Cell(row, c++, DpsFigure(e, f, now));
            }
            if (deaths) Cell(row, c, Right(PT(f.Deaths > 0 ? f.Deaths.ToString(CultureInfo.InvariantCulture) : "–", 13, FontWeights.Medium, f.Deaths > 0 ? "Coral" : "Faint")));
            host.Children.Add(rule);
            if (open && f.Abilities.Count > 0) host.Children.Add(AbilityTable(f, e, now));
        }
        return host;
    }

    /// <summary>"48.2K DPS · 25.1%": a member's DPS (over the fight, or their active time) and share; their damage on hover.</summary>
    FrameworkElement DpsFigure(Encounter e, Fighter f, DateTime now)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Background = Brushes.Transparent };
        sp.Children.Add(PT(Amounts.Short(e.Dps(f, now)), 13, FontWeights.Bold, "Ink"));
        sp.Children.Add(Margin(PT("DPS", 10.5, FontWeights.SemiBold, "Faint"), 3, 1, 0, 0));
        sp.Children.Add(Margin(PT("·", 12, FontWeights.SemiBold, "Faint"), 6, 0, 6, 0));
        sp.Children.Add(PT(Encounter.Pct(e.Share(f)), 12.5, FontWeights.SemiBold, "Text2"));
        // the figure behind them, always (a figure, not an explanation)
        sp.ToolTip = $"{Amounts.Short(f.Damage)} damage in {BossParser.Clock(TimeSpan.FromSeconds(e.DpsSeconds(f, now)))}{(Encounter.ActiveBasis && !f.Other ? " active" : "")}";
        AutomationProperties.SetName(sp, $"{Amounts.Short(e.Dps(f, now))} DPS, {Encounter.Pct(e.Share(f))} of the damage");
        return sp;
    }

    /// <summary>Which members' abilities are open ("record id:member id").</summary>
    readonly HashSet<string> _parserAbilitiesOpen = new();

    /// <summary>
    /// A member's damage by ability (Steven, 2026-11-14: "click on and open or expand you abilities to see how much each is
    /// doing on average / total"): each ability's total, hits, average a hit and share of the member's damage on a bar in
    /// the class's colour; the first eight, then the rest folded.
    /// </summary>
    FrameworkElement AbilityTable(Fighter f, Encounter e, DateTime now)
    {
        var host = new StackPanel { Margin = new Thickness(40, 2, 4, 6) };
        // the member first: item level, Combat Power, server, their damage and the time it's over
        var who = new List<string>();
        if (f.ItemLevel > 0) who.Add($"Item level {f.ItemLevel.ToString("N0", CultureInfo.InvariantCulture)}");
        if (f.CombatPower > 0) who.Add($"Combat Power {f.CombatPower.ToString("N0", CultureInfo.InvariantCulture)}");
        if (f.Server.Length > 0) who.Add(f.Server);
        who.Add($"{Amounts.Short(f.Damage)} damage in {BossParser.Clock(TimeSpan.FromSeconds(e.DpsSeconds(f, now)))}{(Encounter.ActiveBasis ? " active" : "")}");
        host.Children.Add(Margin(PT(string.Join("  ·  ", who), 11.5, FontWeights.Medium, "Muted", trim: true), 6, 0, 4, 5));
        Grid Row()
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
            return g;
        }
        void Cell(Grid g, int col, FrameworkElement el) { Grid.SetColumn(el, col); g.Children.Add(el); }
        var head = Row(); head.Margin = new Thickness(6, 0, 4, 2);
        Cell(head, 0, PT("Ability", 10.5, FontWeights.SemiBold, "Faint"));
        Cell(head, 1, Right(PT("Damage", 10.5, FontWeights.SemiBold, "Faint")));
        Cell(head, 2, Right(PT("Hits", 10.5, FontWeights.SemiBold, "Faint")));
        Cell(head, 3, Right(PT("Average", 10.5, FontWeights.SemiBold, "Faint")));
        Cell(head, 4, Right(PT("Share", 10.5, FontWeights.SemiBold, "Faint")));
        host.Children.Add(head);
        var list = f.Abilities.OrderByDescending(a => a.Damage).ToList();
        string allKey = $"{e.Id}:{f.Id}:all";
        bool all = _parserAbilitiesOpen.Contains(allKey);
        long total = Math.Max(1, f.Damage);
        double top = Math.Max(1, list.Max(a => a.Damage));
        foreach (var a in all ? list : list.Take(8))
        {
            var g = Row(); g.Margin = new Thickness(6, 0, 4, 0); g.MinHeight = 22;
            Cell(g, 0, PT(Abilities.Name(a.Skill), 12, FontWeights.Medium, a.Skill == 0 ? "Muted" : "Text2", trim: true));
            Cell(g, 1, Right(PT(Amounts.Short(a.Damage), 12, FontWeights.SemiBold, "Ink")));
            Cell(g, 2, Right(PT(a.Hits.ToString("N0", CultureInfo.InvariantCulture), 12, FontWeights.Medium, "Text2")));
            Cell(g, 3, Right(PT(Amounts.Short(a.Average), 12, FontWeights.Medium, "Text2")));
            Cell(g, 4, Right(PT($"{a.Damage * 100.0 / total:0}%", 12, FontWeights.Medium, "Muted")));
            var meter = ClassArt.MeterRow(g, f.Class, a.Damage / top, radius: 4);
            meter.Margin = new Thickness(0, 2, 0, 0);
            meter.Opacity = 0.9;
            host.Children.Add(meter);
        }
        if (list.Count > 8)
            host.Children.Add(Margin(FoldLink(all ? "Fewer" : $"{list.Count - 8} more  ›", () => { if (!_parserAbilitiesOpen.Remove(allKey)) _parserAbilitiesOpen.Add(allKey); RedrawParserPage(); }), 6, 2, 0, 0));
        return host;
    }

    /// <summary>The boss's HP: a bar in the grade of what is left (coral under a third), the share and the figures beside it.</summary>
    FrameworkElement BossHpBar(Encounter e, double left)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var track = new Grid { Height = 8, VerticalAlignment = VerticalAlignment.Center };
        var back = new Border { CornerRadius = new CornerRadius(4) }; back.SetResourceReference(Border.BackgroundProperty, "LineStrong");
        var fill = new Border { CornerRadius = new CornerRadius(4), HorizontalAlignment = HorizontalAlignment.Left };
        fill.SetResourceReference(Border.BackgroundProperty, left < 0.34 ? "Coral" : "Gold");
        track.Children.Add(back); track.Children.Add(fill);
        track.SizeChanged += (_, _) => fill.Width = Math.Max(left > 0 ? 3 : 0, track.ActualWidth * left);
        AutomationProperties.SetName(track, $"Boss HP {left:P0}");
        g.Children.Add(track);
        var txt = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        txt.Children.Add(PT($"{left:P0}", 13, FontWeights.Bold, left < 0.34 ? "Coral" : "Ink"));
        txt.Children.Add(Margin(PT($"{Amounts.Short(e.BossHp)} / {Amounts.Short(e.BossMaxHp)} HP", 12, FontWeights.Medium, "Muted"), 7, 0, 0, 0));
        Grid.SetColumn(txt, 1); g.Children.Add(txt);
        return g;
    }

    /// <summary>The Other row's mark: a quiet tile with a dot pattern, not a class.</summary>
    internal static FrameworkElement OtherMark(double size)
    {
        var c = Color.FromRgb(0x86, 0x8D, 0xA1);
        return new Border
        {
            Width = size, Height = size, CornerRadius = new CornerRadius(size / 2), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left,
            Background = Frozen(Color.FromArgb(0x18, c.R, c.G, c.B)), BorderBrush = Frozen(Color.FromArgb(0x50, c.R, c.G, c.B)), BorderThickness = new Thickness(1),
            Child = new TextBlock { Text = "···", FontSize = size * 0.45, FontWeight = FontWeights.Bold, Foreground = Frozen(c), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, -size * 0.12, 0, 0) },
        };
    }

    FrameworkElement ShareBar(double share, bool you)
    {
        var track = new Grid { Height = 3, Margin = new Thickness(0, 4, 0, 0), MaxWidth = 220, HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
        var back = new Border { CornerRadius = new CornerRadius(1.5) }; back.SetResourceReference(Border.BackgroundProperty, "SegGroup");
        var fill = new Border { CornerRadius = new CornerRadius(1.5), HorizontalAlignment = HorizontalAlignment.Left };
        fill.SetResourceReference(Border.BackgroundProperty, you ? "Gold" : "Muted");
        track.Children.Add(back); track.Children.Add(fill);
        track.SizeChanged += (_, _) => fill.Width = Math.Max(2, track.ActualWidth * Math.Clamp(share, 0, 1));
        return track;
    }

    /// <summary>A class's mark: the game's emblem on a ring in the class's colour (Ui/ClassArt.cs).</summary>
    internal static FrameworkElement ClassMark(string cls, double size, string name = "") => ClassArt.Icon(cls, size, name);

    static SolidColorBrush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

    /// <summary>What the Parser is waiting for in its mode, and where you are.</summary>
    (string Head, string Line, string Tone) ParserStatus((ContentKind Kind, string Instance) where)
    {
        bool kept = where.Kind is ContentKind.Transcendence or ContentKind.Expedition or ContentKind.Sanctuary;
        if (!_demo && _watcher.Packets is { } pr && !pr.On)
            return ("The Parser is off", "It needs a setting turned on: Settings › Game reading.", "Warn");
        switch (ParserModeNow)
        {
            case ParserMode.Run:
                return kept ? ($"In {BossBook.Strip(where.Instance)}", "Full run counts from entering to leaving; this run started before the Parser could see it.", "Good")
                    : ("Waiting for a run", "Enter a Transcendence, an Expedition or a Sanctuary: the run counts from there to leaving, every boss a split.", "Faint");
            case ParserMode.Fights:
                var zone = _demo ? "" : ParserFights?.Zone ?? "";
                return ("Waiting for a fight", $"Your next kill or hit starts one{(kept ? $" in {BossBook.Strip(where.Instance)}" : zone.Length > 0 ? $" in {zone}" : "")}. It ends {BossParser.PauseSeconds:0} seconds after the last.", "Good");
            default:
                return kept ? ($"Ready in {BossBook.Strip(where.Instance)}", $"{Encounter.KindName(where.Kind)}. The next boss you pull starts a fight here; trash and adds never do.", "Good")
                    : ("Waiting for a boss", "Boss pulls are kept in Transcendence, Expeditions and Sanctuaries only. Enter one and pull a boss.", "Faint");
        }
    }

    FrameworkElement ParserWaitingCard((ContentKind Kind, string Instance) where, List<Encounter> past)
    {
        var card = PCard(hero: false);
        card.Padding = new Thickness(14, 12, 14, 12);
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var art = GameArt.Element(ParserModeNow == ParserMode.Fights ? "medal" : "dungeon", 30); art.Margin = new Thickness(0, 0, 12, 0); art.Opacity = 0.85;
        g.Children.Add(art);
        var (head, line, tone) = ParserStatus(where);
        var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var h = new StackPanel { Orientation = Orientation.Horizontal };
        var dot = new Border { Width = 7, Height = 7, CornerRadius = new CornerRadius(3.5), Margin = new Thickness(0, 1, 7, 0), VerticalAlignment = VerticalAlignment.Center };
        dot.SetResourceReference(Border.BackgroundProperty, tone);
        h.Children.Add(dot);
        h.Children.Add(PT(head, 14, FontWeights.SemiBold, "Ink", trim: true));
        sp.Children.Add(h);
        var l = PT(line, 12, FontWeights.Medium, "Text2"); l.TextWrapping = TextWrapping.Wrap; l.Margin = new Thickness(0, 3, 0, 0);
        sp.Children.Add(l);
        Grid.SetColumn(sp, 1); g.Children.Add(sp);
        // the last record of this mode, one line, so the card says something useful in between
        if (past.LastOrDefault(e => e.ModeKind == ParserModeNow) is { } last)
        {
            var lastBox = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
            var lt = PT("Last " + Noun(ParserModeNow).Replace("boss ", ""), 11, FontWeights.SemiBold, "Faint"); lt.HorizontalAlignment = HorizontalAlignment.Right;
            lastBox.Children.Add(lt);
            var lr = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 3, 0, 0) };
            lr.Children.Add(PT(last.Title, 12.5, FontWeights.SemiBold, "Ink", trim: true));
            lr.Children.Add(PT("  " + BossParser.Clock(last.Duration(last.End!.Value)) + "  ", 12.5, FontWeights.Medium, "Muted"));
            lr.Children.Add(OutcomeChip(last));
            lastBox.Children.Add(lr);
            lastBox.MaxWidth = 280;
            Grid.SetColumn(lastBox, 2); g.Children.Add(lastBox);
        }
        card.Child = g;
        return card;
    }

    // ---- the fights kept ----

    FrameworkElement ParserHistory(List<Encounter> all, DateTime now)
    {
        var card = new Border { Margin = new Thickness(0, 8, 0, 0) };
        card.SetResourceReference(StyleProperty, "QRefCard");
        var body = new StackPanel();
        var mode = ParserListMode;
        var past = all.Where(e => e.ModeKind == mode).ToList();
        // the head: which records (boss pulls, runs, fights, with their counts), the boss or instance pick
        var head = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var modes = new StackPanel { Orientation = Orientation.Horizontal };
        var mseg = new Border { Padding = new Thickness(3), CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), Child = modes, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center,
            ToolTip = Tip("Every record of this character, kept on this PC only: never uploaded. Each is kept in the mode it was counted in.") };
        mseg.SetResourceReference(Border.BackgroundProperty, "SegGroup"); mseg.SetResourceReference(Border.BorderBrushProperty, "CardLine");
        foreach (var (md, label) in new[] { (ParserMode.Bosses, "Boss pulls"), (ParserMode.Run, "Runs"), (ParserMode.Fights, "Fights") })
        {
            int n = all.Count(e => e.ModeKind == md);
            var b = new Button { Content = n > 0 ? $"{label} {n}" : label, Height = 24, FontSize = 12, Padding = new Thickness(9, 0, 9, 0), Margin = new Thickness(modes.Children.Count > 0 ? 2 : 0, 0, 0, 0) };
            b.SetResourceReference(StyleProperty, md == mode ? "SegOn" : "SegOff");
            AutomationProperties.SetName(b, $"Show the kept {label.ToLowerInvariant()}");
            var pick = md;
            b.Click += (_, _) => { _parserPrefs.ListMode = BossParser.ModeKey(pick); _parserPrefs.KindFilter = ""; _parserPrefs.BossFilter = ""; ParserFilterChanged(); };
            modes.Children.Add(b);
        }
        head.Children.Add(mseg);
        var inKind = past.Where(e => _parserPrefs.KindFilter.Length == 0 || e.Kind == _parserPrefs.KindFilter).ToList();
        // a boss (pulls) or an instance (runs) to look at alone; fights are told apart by their kind
        Func<Encounter, string>? keyOf = mode switch { ParserMode.Bosses => e => e.Boss, ParserMode.Run => e => e.Title, _ => null };
        string anyLabel = mode == ParserMode.Run ? "Any instance" : "Any boss";
        var bossBtn = new Button { Height = 30, FontSize = 12, Padding = new Thickness(10, 0, 10, 0), Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        bossBtn.Content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children =
            {
                PT(_parserPrefs.BossFilter.Length > 0 ? _parserPrefs.BossFilter : anyLabel, 12, FontWeights.SemiBold, "Ink", trim: true),
                new System.Windows.Shapes.Path { Data = Geometry.Parse("M0,0 L3.5,3.5 L7,0"), StrokeThickness = 1.4, Margin = new Thickness(7, 1, 0, 0), VerticalAlignment = VerticalAlignment.Center, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round },
            },
        };
        ((System.Windows.Shapes.Path)((StackPanel)bossBtn.Content).Children[1]).SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "Muted");
        ((TextBlock)((StackPanel)bossBtn.Content).Children[0]).MaxWidth = 150;
        AutomationProperties.SetName(bossBtn, mode == ParserMode.Run ? "Filter by instance" : "Filter by boss");
        bossBtn.ToolTip = Tip(mode == ParserMode.Run ? "Show one instance's runs" : "Show one boss's pulls");
        bossBtn.Click += (_, _) =>
        {
            if (keyOf == null) return;
            var entries = new List<MenuEntry> { new(anyLabel, () => { _parserPrefs.BossFilter = ""; ParserFilterChanged(); }, Sub: $"{inKind.Count} {Noun(mode, inKind.Count)}", On: _parserPrefs.BossFilter.Length == 0) };
            foreach (var g in inKind.GroupBy(keyOf, StringComparer.OrdinalIgnoreCase).OrderByDescending(g => g.Max(e => e.Start)))
            {
                var key = keyOf(g.First());
                int good = g.Count(e => e.Tone == "good");
                string sub = mode == ParserMode.Run ? $"{g.Count()} {Noun(mode, g.Count())}, {good} cleared" : $"{BossBook.Strip(g.First().Instance)} · {g.Count()} {Noun(mode, g.Count())}, {good} kill{(good == 1 ? "" : "s")}";
                entries.Add(new MenuEntry(key, () => { _parserPrefs.BossFilter = key; ParserFilterChanged(); }, Sub: sub, On: _parserPrefs.BossFilter == key));
            }
            ShowMenu(bossBtn, entries);
        };
        if (inKind.Count == 0 || keyOf == null || !PFilters) bossBtn.Visibility = Visibility.Collapsed;
        Grid.SetColumn(bossBtn, 1); head.Children.Add(bossBtn);
        body.Children.Add(head);
        // the kinds this mode's records are in (Transcendence, Expedition, Sanctuary; the open world, the Abyss for fights), when more than one
        var kindsHere = past.Select(e => e.Kind).Where(k => k.Length > 0).Distinct().OrderBy(k => k).ToList();
        if (kindsHere.Count > 1 && PFilters)
        {
            var kinds = new StackPanel { Orientation = Orientation.Horizontal };
            var seg = new Border { Padding = new Thickness(3), CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), Child = kinds, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 8) };
            seg.SetResourceReference(Border.BackgroundProperty, "SegGroup"); seg.SetResourceReference(Border.BorderBrushProperty, "CardLine");
            foreach (var key in kindsHere.Prepend(""))
            {
                int n = key.Length == 0 ? past.Count : past.Count(e => e.Kind == key);
                var b = new Button { Content = $"{(key.Length == 0 ? "All" : key)} {n}", Height = 24, FontSize = 12, Padding = new Thickness(9, 0, 9, 0), Margin = new Thickness(kinds.Children.Count > 0 ? 2 : 0, 0, 0, 0) };
                b.SetResourceReference(StyleProperty, _parserPrefs.KindFilter == key ? "SegOn" : "SegOff");
                var k = key;
                b.Click += (_, _) => { _parserPrefs.KindFilter = k; _parserPrefs.BossFilter = ""; ParserFilterChanged(); };
                kinds.Children.Add(b);
            }
            body.Children.Add(seg);
        }

        var shown = inKind.Where(e => _parserPrefs.BossFilter.Length == 0 || keyOf == null || keyOf(e).Equals(_parserPrefs.BossFilter, StringComparison.OrdinalIgnoreCase)).OrderByDescending(e => e.Start).ToList();
        if (shown.Count == 0)
        {
            var empty = PT(past.Count > 0 ? $"No {Noun(mode, 2)} of this kind yet." : mode switch
            {
                ParserMode.Run => "No runs kept yet. In Full run, each Transcendence, Expedition or Sanctuary run lands here, entering to leaving, with every boss as a split.",
                ParserMode.Fights => "No fights kept yet. In Every fight, each fight anywhere lands here, with your kills and deaths.",
                _ => "No boss pulls kept yet. Each boss you fight in a Transcendence, an Expedition or a Sanctuary lands here, with its group and timings.",
            } + " They stay on this PC.", 12.5, FontWeights.Normal, "Faint");
            empty.TextWrapping = TextWrapping.Wrap; empty.Margin = new Thickness(0, 4, 0, 6);
            body.Children.Add(empty);
        }
        else
        {
            // a boss or an instance picked: its record on one line
            if (_parserPrefs.BossFilter.Length > 0 && keyOf != null)
            {
                var good = shown.Where(e => e.Tone == "good").ToList();
                var best = good.Count > 0 ? good.Min(e => e.Duration(e.End!.Value)) : (TimeSpan?)null;
                var rec = PT(mode == ParserMode.Run
                    ? $"{shown.Count} {Noun(mode, shown.Count)} · {good.Count} cleared{(best is { } b ? " · best clear " + BossParser.Clock(b) : "")}"
                    : $"{shown.Count} {Noun(mode, shown.Count)} · {good.Count} kill{(good.Count == 1 ? "" : "s")}{(best is { } b2 ? " · best kill " + BossParser.Clock(b2) : "")}", 12, FontWeights.SemiBold, "Text2");
                rec.Margin = new Thickness(0, 0, 0, 6);
                body.Children.Add(rec);
            }
            int take = _parserAllRows ? shown.Count : Math.Min(shown.Count, ParserRowsAtRest);
            for (int i = 0; i < take; i++) body.Children.Add(HistoryRow(shown[i], i > 0, now));
            if (shown.Count > ParserRowsAtRest)
                body.Children.Add(FoldLink(_parserAllRows ? "Fewer" : $"{shown.Count - ParserRowsAtRest} more  ›", () => { _parserAllRows = !_parserAllRows; RedrawParserPage(); }));
        }
        card.Child = body;
        return card;
    }

    void ParserFilterChanged() { _parserAllRows = false; if (!_demo) _parserPrefs.Save(); RedrawParserPage(); }

    /// <summary>One kept fight: when, the boss with its instance, the outcome, how long, the group's DPS (or its average CP); a click opens it.</summary>
    FrameworkElement HistoryRow(Encounter e, bool rule, DateTime now)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(86) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(92) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(54) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(78) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        var local = e.Start.ToLocalTime();
        var today = (_demo ? _parserDemoNow : DateTime.UtcNow).ToLocalTime().Date;
        var when = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        when.Children.Add(PT(local.ToString("HH:mm", CultureInfo.InvariantCulture), 12.5, FontWeights.SemiBold, "Text2"));
        when.Children.Add(PT(local.Date == today ? "Today" : local.Date == today.AddDays(-1) ? "Yesterday" : local.ToString("MMM d", CultureInfo.InvariantCulture), 11, FontWeights.Medium, "Faint"));
        g.Children.Add(when);
        var mid = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        var nameLine = new StackPanel { Orientation = Orientation.Horizontal };
        nameLine.Children.Add(PT(e.Title, 13, FontWeights.SemiBold, "Ink", trim: true));
        if (e.Partial) nameLine.Children.Add(Margin(PT("partial", 11, FontWeights.SemiBold, "Warn"), 6, 1, 0, 0));
        // the group at a glance: each member's class emblem, small, in the same colours as the tab and the overlay
        var classes = e.Members.Where(f => ClassArt.Key(f.Class).Length > 0 && (!e.HasDamage || f.Damage > 0)).OrderByDescending(f => f.You).Take(8).ToList();
        if (classes.Count > 0)
        {
            var icons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            foreach (var f in classes) { var ic = ClassArt.Icon(f.Class, 15, f.Name); ic.Margin = new Thickness(0, 0, 2, 0); icons.Children.Add(ic); }
            nameLine.Children.Add(icons);
        }
        mid.Children.Add(nameLine);
        var place = BossBook.Strip(e.Instance);
        string sub = e.ModeKind switch
        {
            ParserMode.Run => $"{(e.GameMode.Length > 0 ? e.GameMode + " · " : "")}{KindLabel(e)} · {e.Splits.Count(x => x.Outcome == "kill")} of {e.Splits.Select(x => x.Boss).Distinct().Count()} bosses{(e.YourKills > 0 ? $" · {e.YourKills} kills" : "")}",
            ParserMode.Fights => $"{(place.Length > 0 && place != e.Kind && place != e.Title ? place + " · " : "")}{KindLabel(e)}{(e.YourDeaths > 0 && e.YourKills > 0 ? $" · {e.YourKills} kill{(e.YourKills == 1 ? "" : "s")}" : "")}",
            _ => $"{e.Place} · {KindLabel(e)}",
        };
        mid.Children.Add(PT(sub, 11.5, FontWeights.Medium, "Muted", trim: true));
        Grid.SetColumn(mid, 1); g.Children.Add(mid);
        var oc = OutcomeChip(e); oc.HorizontalAlignment = HorizontalAlignment.Left;
        Grid.SetColumn(oc, 2); g.Children.Add(oc);
        var dur = Right(PT(BossParser.Clock(e.Duration(e.End ?? now)), 13, FontWeights.SemiBold, "Ink"));
        Grid.SetColumn(dur, 3); g.Children.Add(dur);
        var fig = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
        if (e.HasDamage) { fig.Children.Add(Right(PT(Amounts.Short(e.GroupDps(e.End ?? now)), 13, FontWeights.SemiBold, "Text2"))); fig.Children.Add(Right(PT("group DPS", 10.5, FontWeights.Medium, "Faint"))); }
        else if (e.ModeKind != ParserMode.Bosses) { fig.Children.Add(Right(PT(e.YourKills.ToString("N0", CultureInfo.InvariantCulture), 13, FontWeights.SemiBold, "Text2"))); fig.Children.Add(Right(PT(e.PlayerKills > 0 ? $"kills · {e.PlayerKills} PvP" : "your kills", 10.5, FontWeights.Medium, "Faint"))); }
        else if (PDetails) { fig.Children.Add(Right(PT(AvgCp(e), 13, FontWeights.SemiBold, "Text2"))); fig.Children.Add(Right(PT($"avg CP · {e.Members.Count()} in group", 10.5, FontWeights.Medium, "Faint"))); }
        Grid.SetColumn(fig, 4); g.Children.Add(fig);
        var chev = new System.Windows.Shapes.Path { Data = Geometry.Parse("M0,0 L3.5,3.5 L0,7"), StrokeThickness = 1.4, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
        chev.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "Faint");
        Grid.SetColumn(chev, 5); g.Children.Add(chev);
        var b = new Border { Child = g, Padding = new Thickness(6, 6, 6, 6), Margin = new Thickness(-6, 0, -6, 0), CornerRadius = new CornerRadius(7), BorderThickness = new Thickness(0, rule ? 1 : 0, 0, 0), Background = Brushes.Transparent, Cursor = Cursors.Hand, Focusable = true };
        b.SetResourceReference(Border.BorderBrushProperty, "CardHair");
        AutomationProperties.SetName(b, $"{e.Title}, {e.OutcomeLabel}, {BossParser.Clock(e.Duration(e.End ?? now))}");
        b.Tag = "parser-row";
        b.ToolTip = e.ModeKind switch
        {
            ParserMode.Run => "Open this run: every boss as a split, the group, the timings, the kills",
            ParserMode.Fights => "Open this fight: your kills and deaths, the group, the timings",
            _ => "Open this boss pull: the group, the timings, the kills near you",
        };
        if (!_parserPrefs.Hovers) b.ToolTip = null;
        b.MouseEnter += (_, _) => b.SetResourceReference(Border.BackgroundProperty, "HoverTint");
        b.MouseLeave += (_, _) => b.Background = Brushes.Transparent;
        b.MouseLeftButtonUp += (_, _) => OpenFight(e.Id);
        b.KeyDown += (_, k) => { if (k.Key is Key.Enter or Key.Space) { k.Handled = true; OpenFight(e.Id); } };
        return b;
    }

    void OpenFight(string? id) { _parserOpen = id; RedrawParserPage(); LowerScroll.ScrollToTop(); }

    /// <summary>One kept fight: back to the list, its head, its figures, the timings, the group with deaths, the kills near you.</summary>
    FrameworkElement ParserDetail(Encounter e)
    {
        var end = e.End ?? e.LastAt;
        var box = new StackPanel();
        var back = FoldLink($"‹  All {Noun(e.ModeKind, 2)}", () => OpenFight(null));
        back.Margin = new Thickness(2, 0, 0, 8);
        box.Children.Add(back);
        var card = PCard(hero: false);
        var body = new StackPanel();
        body.Children.Add(FightHead(e, end, live: false));
        if (FightChips(e, end, live: false) is { } chips) body.Children.Add(Margin(chips, 0, 10, 0, 0));
        // the timings: pulled, ended, how long; the instance's copy said apart only when it matters
        var local = e.Start.ToLocalTime();
        var startWord = e.ModeKind switch { ParserMode.Run => "Entered", ParserMode.Fights => "Started", _ => "Pulled" };
        var endWord = e.ModeKind == ParserMode.Run ? "Left" : "Ended";
        var times = PT($"{startWord} {local.ToString("ddd MMM d, HH:mm:ss", CultureInfo.InvariantCulture)}   ·   {endWord} {end.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture)}   ·   {BossParser.Clock(e.Duration(end))}", 12, FontWeights.Medium, "Text2", trim: true);
        times.Margin = new Thickness(0, 8, 0, 0);
        body.Children.Add(times);
        if (e.Partial)
        {
            var why = PT($"Partial: {string.Join("; ", e.PartialWhy)}. Still one {Noun(e.ModeKind)}; its damage and kills may be short.", 12, FontWeights.Medium, "Warn");
            why.TextWrapping = TextWrapping.Wrap; why.Margin = new Thickness(0, 4, 0, 0);
            body.Children.Add(why);
        }
        if (e.ModeKind == ParserMode.Run && e.Splits.Count > 0)
        {
            body.Children.Add(Margin(PT("Bosses", 11, FontWeights.SemiBold, "Faint"), 0, 12, 0, 4));
            body.Children.Add(SplitsTable(e, live: false));
        }
        if (e.Roster.Count > 0 || e.ModeKind == ParserMode.Bosses) body.Children.Add(Margin(RosterTable(e, end, detail: true), 0, 12, 0, 0));
        void KillChips(string head, IEnumerable<KillNear> kills, string ink)
        {
            var groups = kills.GroupBy(k => k.Name, StringComparer.OrdinalIgnoreCase).OrderByDescending(g => g.Count()).ToList();
            if (groups.Count == 0) return;
            body.Children.Add(Margin(PT(head, 11, FontWeights.SemiBold, "Faint"), 0, 12, 0, 4));
            var wp = new WrapPanel();
            foreach (var g in groups) { var c = PChip(g.Count() > 1 ? $"{g.First().Name} ×{g.Count()}" : g.First().Name, ink, "ChipBg"); c.Margin = new Thickness(0, 0, 6, 4); wp.Children.Add(c); }
            body.Children.Add(wp);
        }
        if (PKills || e.ModeKind == ParserMode.Fights) KillChips("Your kills", e.Kills.Where(k => k.Yours), "Text2");
        if (PKills) KillChips("Kills near you", e.Kills.Where(k => !k.Boss && !k.Yours), "Text2");
        if (!e.HasDamage)
        {
            var nd = PT($"No damage was seen for this {Noun(e.ModeKind)}, so none is shown.", 11.5, FontWeights.Medium, "Faint");
            nd.Margin = new Thickness(0, 10, 0, 0);
            body.Children.Add(nd);
        }
        card.Child = body;
        box.Children.Add(card);
        return box;
    }

    // ---- small parts ----

    static TextBlock PT(string text, double size, FontWeight weight, string brush, bool trim = false, bool display = false)
    {
        var t = QT(text, size, weight, brush, trim);
        if (display) t.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
        return t;
    }

    static T Margin<T>(T el, double l, double t, double r, double b) where T : FrameworkElement { el.Margin = new Thickness(l, t, r, b); return el; }

    static TextBlock Right(TextBlock t) { t.HorizontalAlignment = HorizontalAlignment.Right; return t; }

    static Border PCard(bool hero)
    {
        var b = new Border();
        b.SetResourceReference(StyleProperty, "QCard");
        if (hero) b.SetResourceReference(Border.BackgroundProperty, "HeroBg");
        if (hero) b.Padding = new Thickness(16, 12, 16, 12);
        return b;
    }

    static Border PChip(string text, string ink, string tint, bool dot = false)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        if (dot)
        {
            var d = new Border { Width = 6, Height = 6, CornerRadius = new CornerRadius(3), Margin = new Thickness(0, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center };
            d.SetResourceReference(Border.BackgroundProperty, ink);
            sp.Children.Add(d);
        }
        sp.Children.Add(QT(text, 11.5, FontWeights.SemiBold, ink));
        var b = new Border { Child = sp, CornerRadius = new CornerRadius(7), Padding = new Thickness(8, 2, 8, 3), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
        b.SetResourceReference(Border.BackgroundProperty, tint);
        return b;
    }

    /// <summary>A figure with its label in front: "Group DPS 41.2K".</summary>
    static Border PStat(string label, string value, string ink, string after = "")
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        sp.Children.Add(QT(label, 11.5, FontWeights.Medium, "Muted"));
        sp.Children.Add(Margin(QT(value, 12.5, FontWeights.Bold, ink), 6, 0, 0, 0));
        if (after.Length > 0) sp.Children.Add(Margin(QT(after, 11.5, FontWeights.Medium, "Faint"), 5, 0, 0, 0));
        var b = new Border { Child = sp, CornerRadius = new CornerRadius(8), Padding = new Thickness(9, 3, 9, 4), BorderThickness = new Thickness(1) };
        b.SetResourceReference(Border.BackgroundProperty, "SegGroup");
        b.SetResourceReference(Border.BorderBrushProperty, "CardHair");
        return b;
    }

    // ---- renders and tests ----

    string _parserDemoState = "";

    /// <summary>
    /// A layout render's Parser page: Bosses only "live" (a pull under way), "history" (between pulls), "detail" (one kept
    /// pull open), "nodamage", "nogroup" (damage before any group list), "empty"; Full run "run" (a run under way),
    /// "run-detail"; Every fight "fight" (a fight under way), "fight-history".
    /// </summary>
    internal void ShowParserDemo(string state)
    {
        _demo = true;
        // "-all": the same state with every Parser option on (the PR's second render)
        bool everyOption = state.EndsWith("-all");
        if (everyOption) state = state[..^4];
        _parserPrefs.Details = _parserPrefs.KillsNear = _parserPrefs.Filters = _parserPrefs.Hovers = everyOption;
        _parserDemoState = state;
        _parserDemoNow = new DateTime(2026, 11, 13, 21, 30, 0, DateTimeKind.Utc);
        var mode = state.StartsWith("run") ? ParserMode.Run : state.StartsWith("fight") ? ParserMode.Fights : ParserMode.Bosses;
        bool live = state is "live" or "nodamage" or "nogroup" or "run" or "fight";
        // the kept records of every mode, so the list's switch has something in each; the one under way in the mode shown
        var all = new List<Encounter>();
        Encounter? now = null;
        if (state != "empty")
            foreach (var md in new[] { ParserMode.Bosses, ParserMode.Run, ParserMode.Fights })
            {
                var (past, l) = ParserDemo.Sample(_parserDemoNow, live: live && md == mode, damage: state != "nodamage", group: state != "nogroup", mode: md);
                all.AddRange(past);
                if (md == mode) now = l;
            }
        _parserDemoPast = all.OrderBy(e => e.Start).ToList(); _parserDemoLive = now;
        _parserDemoWhere = state == "empty" || state == "fight-history" ? (ContentKind.Other, "") : (ContentKind.Expedition, "Ferocious Horn Den");
        _parserPrefs.Mode = BossParser.ModeKey(mode); _parserPrefs.ListMode = "";
        _parserOpen = state switch
        {
            "detail" => all.LastOrDefault(e => e.ModeKind == ParserMode.Bosses && e.Kills.Any(k => !k.Boss) && e.Roster.Any(f => f.Deaths > 0))?.Id,
            "run-detail" => all.LastOrDefault(e => e.ModeKind == ParserMode.Run && e.Outcome == "cleared")?.Id,
            _ => null,
        };
        _parserPrefs.KindFilter = ""; _parserPrefs.BossFilter = "";
        // the detail render opens its top member's damage by ability
        _parserAbilitiesOpen.Clear();
        if (_parserOpen != null && all.FirstOrDefault(e => e.Id == _parserOpen) is { } opened && opened.Ranked().FirstOrDefault(f => f.Abilities.Count > 0) is { } topMember)
            _parserAbilitiesOpen.Add($"{opened.Id}:{topMember.Id}");
        ShowPart("Parser", byHand: false);
        RedrawParserPage();
    }

    /// <summary>parser-latency: the page and the overlay on a replay's Parser, drawn as live (not a demo: nothing held still).</summary>
    internal void ShowParserReplay(BossParser parser, ParserMode mode)
    {
        _demo = false;
        ParserSourceForTest = parser; ParserOverlayForceForTest = true;
        _parserPrefs.Mode = BossParser.ModeKey(mode); _parserPrefs.ListMode = ""; _parserPrefs.Overlay = true;
        ShowPart("Parser", byHand: false);
        WatchParser();
        RedrawParserPage();
    }

    internal void SetParserModeForTest(ParserMode m) => SetParserMode(m);
    internal ParserMode ParserModeForTest => ParserModeNow;
    internal Encounter? ParserLiveForTest => ParserLive;

    /// <summary>For the options test: what the page shows (a live fight, the rows in the list, the detail open).</summary>
    internal (bool Live, int Rows, bool Detail, int Roster) ParserPageForTest()
    {
        int rows = 0; bool live = false, detail = _parserOpen != null;
        foreach (var c in ParserPage.Children.OfType<FrameworkElement>()) Walk(c);
        void Walk(DependencyObject d)
        {
            if (d is Border b && b.Tag is "parser-row") rows++;
            if (d is TextBlock t && t.Text == "In progress") live = true;
            foreach (var ch in LogicalTreeHelper.GetChildren(d).OfType<DependencyObject>()) Walk(ch);
        }
        int roster = (ParserLive ?? (_parserOpen != null ? ParserPast().FirstOrDefault(e => e.Id == _parserOpen) : null))?.Members.Count() ?? 0;
        return (live, rows, detail, roster);
    }

    internal void OpenFightForTest(string? id) => OpenFight(id);
    internal List<Encounter> ParserPastForTest => ParserPast();
    internal ParserOverlay? ParserOverlayForTest => _parserOverlay;
    internal void SetParserOverlayForTest(bool on) => SetParserOverlay(on);
    internal void SetParserSeparateForTest(bool on) { _parserPrefs.Separate = on; if (!on) _parserPlacing = false; DrawOverlayPills(); RedrawParserPage(); }
}
