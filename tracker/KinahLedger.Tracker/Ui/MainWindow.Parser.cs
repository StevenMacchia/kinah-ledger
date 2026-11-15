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
    const int ParserHotkeyId = 3;
    const int ParserRowsAtRest = 5;   // the kept fights folded at five (Parser v2: one screen, as the main page's lists)

    /// <summary>Options › In game › Parser's switches (minimal by default): group details, kills and deaths, list filters, explanations on hover.</summary>
    bool PDetails => _parserPrefs.Details;
    bool PKills => _parserPrefs.KillsNear;
    /// <summary>The group's deaths on boss pulls and runs (on by default since the honest pass).</summary>
    bool PDeaths => _parserPrefs.Deaths;
    bool PFilters => _parserPrefs.Filters;
    /// <summary>An explanation on hover, only when Options › In game › Parser › Explanations on hover is on.</summary>
    string? Tip(string text) => _parserPrefs.Hovers ? text : null;

    void InitParser(bool watch)
    {
        ParserPrefs.Saved += () => Dispatcher.BeginInvoke(() =>
        {
            var fresh = ParserPrefs.Load();
            _parserPrefs.Details = fresh.Details; _parserPrefs.KillsNear = fresh.KillsNear; _parserPrefs.Deaths = fresh.Deaths; _parserPrefs.Targets = fresh.Targets; _parserPrefs.Filters = fresh.Filters; _parserPrefs.Hovers = fresh.Hovers; _parserPrefs.Separate = fresh.Separate; _parserPrefs.Look = fresh.Look; _parserPrefs.DpsActive = fresh.DpsActive; _parserPrefs.FarmDps = fresh.FarmDps;
            Encounter.ActiveBasis = fresh.DpsActive; Encounter.BossOnly = fresh.Targets != "all"; Encounter.CritKey = Encounter.CritKeyOf(fresh.CritNow); Encounter.BackKey = Encounter.BackKeyOf(fresh.BackNow);
            _parserPrefs.Width = fresh.Width; _parserPrefs.Expanded = fresh.Expanded; ApplyMeterExpanded();
            ParserOverlayTick();
            DrawOverlayPills();   // the menu lists the Parser only while it is separate
            if (!_parserPrefs.Filters) { _parserPrefs.KindFilter = ""; _parserPrefs.BossFilter = ""; }
            RedrawParserPage();
        });
        _parserPrefs = watch ? ParserPrefs.Load() : new ParserPrefs();
        ApplyMeterExpanded();
        // a new best on a boss (the overlay's PB moment) is measured against your kills of it kept on this PC
        ParserRows.BestOf = e => FightView.Records(e, ParserPast()).Best;
        if (watch) StartFarmTicker();
        Encounter.ActiveBasis = _parserPrefs.DpsActive; Encounter.BossOnly = _parserPrefs.Targets != "all"; Encounter.CritKey = Encounter.CritKeyOf(_parserPrefs.CritNow); Encounter.BackKey = Encounter.BackKeyOf(_parserPrefs.BackNow);   // crit and back attacks: none until a release picks the kind
        if (watch) TrackerSession.ParserMode = ParserModeNow;   // a session made from now on starts in it
        ParserTab.Click += (_, _) => { _parserFromRuns = false; ShowPart("Parser"); };
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

    /// <summary>
    /// The group card's two switches, one click from the page (the honest pass): DPS over the fight or over each member's
    /// active time, and member figures as boss damage or all targets. Kept in parser.json; the overlay follows.
    /// </summary>
    void SetParserBasis(bool? active = null, bool? bossOnly = null)
    {
        if (active is { } a) { _parserPrefs.DpsActive = a; Encounter.ActiveBasis = a; }
        if (bossOnly is { } b) { _parserPrefs.Targets = b ? "boss" : "all"; Encounter.BossOnly = b; }
        if (!_demo) _parserPrefs.Save();
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
    /// <summary>For parser-latency: the Kinah line's farming figure, each time its text changed, and the time from the game event to that frame.</summary>
    internal static int FarmTextChanges;
    internal static ParserLatency FarmLatencyStats { get; } = new();
    /// <summary>For parser-latency: the replay's own Kinah overlay (hidden, off-screen), so the line's figure is drawn as live.</summary>
    internal void AttachHudForTest(HudWindow h) => _hud = h;
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
        string farmWas = _hud?.FarmDpsShown ?? "";
        try
        {
            if (_part == "Parser") FillParserPage();
            ParserOverlayTick();
        }
        catch (Exception ex) { CrashLog.Write("parser page", ex); }
        FarmDrawn(farmWas, oldest);
        bool shown = (_part == "Parser" && IsVisible) || _parserOverlay is { IsVisible: true } || _hud is { IsVisible: true, ParserShown: true } || _hud is { IsVisible: true, FarmDpsShown.Length: > 0 };
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

    /// <summary>The Kinah line's farming figure after a draw: counted when its text changed, timed from the event that brought it (if any) to the frame.</summary>
    void FarmDrawn(string was, DateTime? from)
    {
        var now = _hud?.FarmDpsShown ?? "";
        if (now == was) return;
        Interlocked.Increment(ref FarmTextChanges);
        if (from is not { } o) return;
        EventHandler? h = null;
        h = (_, _) => { System.Windows.Media.CompositionTarget.Rendering -= h; FarmLatencyStats.Add(Math.Max(0, (DateTime.UtcNow - o).TotalMilliseconds)); };
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
    Encounter? ParserLive => _demo ? _parserDemoLive : _parserPrefs.AlwaysOn ? ParserFights?.AlwaysLive : ParserFights?.Live;
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
        if (_demo) return;
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
        // Steven, 2026-11-14: "turn into the parser when you're in a dungeon - it should not disappear after a fight and persist
        // throughout the dungeon - it should reset each fight": in a Transcendence, an Expedition or a Sanctuary the meter stays,
        // the last fight's result until the next one starts afresh, the dungeon's ready card before the first
        if (show == null && DungeonMeter(now) is { } held) show = held;
        // Steven, 2026-11-14: big "only inside of expeditions and other runs": an open-world fight never opens the section or
        // the separate overlay; it is one small figure on the Kinah line instead (your DPS, Options › In game › Parser)
        if (show is { OpenWorld: true }) show = null;
        // Steven, 2026-11-15: "Always on": the meter is always up, anywhere, and starts from 0 by the parser's rules
        if (_parserPrefs.AlwaysOn) show = AlwaysMeter(now);
        // the small DPS on the Kinah line is the meter's figure: not twice
        _hud?.SetFarmDps(_parserPrefs.AlwaysOn ? "" : FarmDpsText(now));
        if (!ParserOverlaySeparate)
        {
            // expanded by default: the section above the Kinah line; its own window stays away
            _parserOverlay?.ShowWanted(false);
            _hud!.ShowParser(_parserPrefs.Overlay ? show : null, now, ParserModeNow, PDetails);
            // Steven, 2026-11-14: "the parser should REPLACE the kinah tracker and that should be hidden during expeditions":
            // in a dungeon the meter is the overlay, the Kinah line away until the player leaves
            _hud.SetKinahHidden(_parserPrefs.Overlay && show != null && InDungeon);
            return;
        }
        // two windows (the default since 0.10.100, Steven, 2026-11-15: "have them just be 2 separate windows pls i miss the old
        // one"): the Kinah overlay stays as it is, the Parser in its own window, in the open world as in a dungeon
        _hud?.SetKinahHidden(false);
        _hud?.ShowParser(null, now, ParserModeNow, PDetails);
        _parserOwnWanted = _parserPrefs.Overlay && show != null;
        bool inFront = ParserOverlayForceForTest || (_watcher.GameRect != null && (Native.ForegroundPid() == (uint)Environment.ProcessId || GameInFront()));
        bool want = _parserOwnWanted && inFront;
        if (!want) { _parserOverlay?.ShowWanted(false); return; }
        if (_parserOverlay == null)
        {
            _parserOverlay = new ParserOverlay { HotkeyText = _parserPrefs.Hotkey };   // "Off" (the default) says no shortcut
            _parserOverlay.Dropped += (x, y) => { _parserPrefs.Left = x; _parserPrefs.Top = y; _parserPrefs.Save(); };
            _parserOverlay.ModePicked += SetParserMode;
            _parserOverlay.HideAsked += () => SetParserOverlay(false);
            _parserOverlay.ClickThroughAsked += () => SetParserClickThrough(true);
            _parserOverlay.Clicked += ToggleMeterExpanded;
            _parserOverlay.SetClickThrough(_parserPrefs.ClickThrough);
            _parserOverlay.SetOpacity(_hudPreview?.HudOpacity ?? _prefs.HudOpacity);
            var (radius, pad) = HudWindow.FrameShape(HudFields.Parse(_prefs.HudFields));
            _parserOverlay.SetFrame(radius, pad);
            _parserOverlay.Look = _parserPrefs.Look;
            _parserOverlay.Mode = ParserModeNow;
            _parserOverlay.ShowDetails = PDetails;
            _parserOverlay.Draw(show!, now);
            _parserOverlay.ShowWanted(true);
            _parserOverlay.SetClickThrough(_parserPrefs.ClickThrough);
            _parserOverlay.Place(_parserPrefs.Left, _parserPrefs.Top, _watcher.GameRect);
            return;
        }
        _parserOverlay.Look = _parserPrefs.Look;   // Settings › In game › Parser › Overlay look, at once
        _parserOverlay.Mode = ParserModeNow;
        _parserOverlay.ShowDetails = PDetails;
        _parserOverlay.Draw(show!, now);
        _parserOverlay.ShowWanted(true);
    }


    /// <summary>
    /// The meter to hold while in a dungeon with no fight under way: the last fight that ended in this dungeon, else its ready
    /// card; null outside one.
    /// </summary>
    Encounter? DungeonMeter(DateTime now)
    {
        var (kind, instance) = ParserWhere;
        if (kind is not (ContentKind.Transcendence or ContentKind.Expedition or ContentKind.Sanctuary)) return null;
        if (_parserJustEnded is { OpenWorld: false } last && BossBook.Strip(last.Instance).Equals(BossBook.Strip(instance), StringComparison.OrdinalIgnoreCase)) return last;
        if (_parserReady == null || !_parserReady.Instance.Equals(instance, StringComparison.Ordinal)) _parserReady = Encounter.Ready(kind, instance, "", now);
        return _parserReady;
    }
    Encounter? _parserReady;

    /// <summary>
    /// The Always on meter: the pull or the tally under way, else what ended last while it is from where the player is (a
    /// dungeon's own, or the open world's), else the place's ready card. Never blank, and never another place's figures.
    /// </summary>
    Encounter? AlwaysMeter(DateTime now)
    {
        if (ParserFights is not { } pf) return null;
        if (pf.AlwaysLive is { } live) return live;
        var held = pf.AlwaysEnded;
        var (kind, instance) = ParserWhere;
        bool dungeon = kind is ContentKind.Transcendence or ContentKind.Expedition or ContentKind.Sanctuary;
        static bool Kept(Encounter e) => Encounter.KindOf(e.Kind) is ContentKind.Transcendence or ContentKind.Expedition or ContentKind.Sanctuary;
        if (dungeon)
        {
            if (held != null && Kept(held) && BossBook.Strip(held.Instance).Equals(BossBook.Strip(instance), StringComparison.OrdinalIgnoreCase)) return held;
            if (_parserReady == null || _parserReady.ModeKind != ParserMode.Bosses || !_parserReady.Instance.Equals(instance, StringComparison.Ordinal)) _parserReady = Encounter.Ready(kind, instance, "", now);
            return _parserReady;
        }
        if (held != null && !Kept(held)) return held;
        var place = pf.Zone.Trim().Length > 0 ? pf.Zone.Trim() : "Open world";
        if (_parserReady == null || _parserReady.ModeKind != ParserMode.Fights || _parserReady.Instance != place) _parserReady = Encounter.ReadyAnywhere(place, now);
        return _parserReady;
    }

    /// <summary>Always on or Only boss fights (the Parser page's head): remembered, the meter follows at once.</summary>
    void SetParserAlwaysOn(bool on)
    {
        if (_parserPrefs.AlwaysOn == on) return;
        _parserPrefs.AlwaysOn = on;
        if (!_demo) _parserPrefs.Save();
        Say(on ? "Parser meter: always on" : "Parser meter: boss fights only", "Faint");
        ParserOverlayTick();
        RedrawParserPage();
    }

    /// <summary>For the options test: the meter's pick on the page, and the meter it would draw now.</summary>
    internal void SetParserAlwaysOnForTest(bool on) => SetParserAlwaysOn(on);
    internal bool ParserAlwaysOnForTest => _parserPrefs.AlwaysOn;
    internal Encounter? AlwaysMeterForTest(DateTime now) => AlwaysMeter(now);

    /// <summary>Settings › In game › Parser › Width moved: the meter at that width now (Save keeps it).</summary>
    public void PreviewFightWidth(double w)
    {
        HudStackLook.FightWidth = w;
        _hud?.ApplyFightWidth();
        _parserOverlay?.ApplyFightWidth();
        if (_parserOverlay != null) ParserOverlayTick();
    }

    /// <summary>In a Transcendence, an Expedition or a Sanctuary now (the meter replaces the Kinah line there).</summary>
    bool InDungeon => ParserWhere.Kind is ContentKind.Transcendence or ContentKind.Expedition or ContentKind.Sanctuary;

    /// <summary>Your live DPS in the open-world fight under way ("13.2K"), or "" (none, no hit of yours yet, or turned off in Options).</summary>
    string FarmDpsText(DateTime now)
    {
        if (!_parserPrefs.FarmDps || ParserFights?.FarmDpsNow(now) is not { } dps || dps <= 0) return "";
        return FarmFigure(dps);
    }

    /// <summary>
    /// The readout's one format, so its width holds while it rolls (tabular digits do the rest): under 1,000 as is, then
    /// thousands with one decimal ("13.2K"), millions with two ("1.25M").
    /// </summary>
    internal static string FarmFigure(double v) => v < 1_000 ? Math.Round(v).ToString("0", CultureInfo.InvariantCulture)
        : v < 999_950 ? (v / 1_000).ToString("0.0", CultureInfo.InvariantCulture) + "K" : (v / 1_000_000).ToString("0.00", CultureInfo.InvariantCulture) + "M";

    System.Windows.Threading.DispatcherTimer? _farmTicker;
    /// <summary>The tab's own live readout of your farming DPS (the open-world fight's card), refreshed with the Kinah line's.</summary>
    TextBlock? _parserFarmNow;

    /// <summary>
    /// The live readout's clock (Steven, 2026-11-14: "rolling all the time"): ten times a second while you farm, the Kinah
    /// line's figure (and the tab's) is worked out again, so it slides between hits and eases off when you stop; each hit
    /// also redraws it at once (the Parser's change, ParserDrawNow). Idle otherwise: one cheap check a tick.
    /// </summary>
    void StartFarmTicker()
    {
        if (_farmTicker != null) return;
        _farmTicker = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(100) };
        _farmTicker.Tick += (_, _) => FarmTick();
        _farmTicker.Start();
    }

    void FarmTick()
    {
        try
        {
            bool live = ParserFights?.FarmLive == true;
            if (!live && (_hud?.FarmDpsShown ?? "").Length == 0 && _parserFarmNow == null) return;
            var text = FarmDpsText(DateTime.UtcNow);
            if (_hud != null)
            {
                var was = _hud.FarmDpsShown;
                _hud.SetFarmDps(text);
                if (_hud.FarmDpsShown != was) Interlocked.Increment(ref FarmTextChanges);
            }
            if (_parserFarmNow != null)
            {
                if (!_parserFarmNow.IsLoaded && _parserFarmNow.Parent == null) _parserFarmNow = null;
                else _parserFarmNow.Text = text.Length > 0 ? text : "–";
            }
        }
        catch (Exception ex) { CrashLog.Write("parser farming readout", ex); }
    }

    void FillParserPage()
    {
        bool show = _part == "Parser" && !_welcome;
        ParserPage.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (!show) { _drawnSig.Remove("parserpage"); return; }
        var now = ParserNow;
        var live = ParserLive;
        var past = ParserPast();
        var where = ParserWhere;
        var sig = $"{live?.Id}:{(int)(live?.Duration(now).TotalSeconds ?? 0)}:{live?.Roster.Sum(f => f.Damage)}:{live?.Roster.Count}:{live?.Kills.Count}:{live?.Partial}:{live?.BossHp}|{past.Count}:{past.LastOrDefault()?.Id}|{where}|{_parserOpen}|{_parserAllRows}|{_parserPrefs.KindFilter}|{_parserPrefs.BossFilter}|{_parserPrefs.Overlay}:{_parserPrefs.ClickThrough}:{_parserPrefs.Separate}:{Encounter.ActiveBasis}:{Encounter.BossOnly}:{live?.Uncredited}|{_parserTab}:{_parserInspect}:{_parserSort}:{_parserPrefs.Details}:{_parserPrefs.KillsNear}|{Skins.Current}|{ParserStatus(where).Line}|{_parserPrefs.Mode}:{_parserPrefs.ListMode}:{_parserPrefs.AlwaysOn}|{live?.Splits.Count}:{live?.YourKills}:{live?.YourDeaths}:{live?.Boss}";
        if (!Changed("parserpage", sig)) return;

        ParserPage.Children.Clear(); _parserFarmNow = null;
        ParserPage.Children.Add(ParserHead(past));
        var open = _parserOpen != null ? past.FirstOrDefault(e => e.Id == _parserOpen) : null;
        // a member opened (Parser v2): their inspect view in place of the fight's
        if (InspectBlock(open ?? live, now, past) is { } inspect) { ParserPage.Children.Add(inspect); return; }
        if (open != null) { ParserPage.Children.Add(FightBlock(open, now, past, live: false)); return; }
        _parserOpen = null;
        ParserPage.Children.Add(live != null ? FightBlock(live, now, past, live: true) : ParserWaitingCard(where, past));
        ParserPage.Children.Add(ParserHistory(past, now));
    }

    /// <summary>
    /// The page's head (the parser polish, 2026-11-14: one line, as the main page has no title of its own): what the Parser
    /// keeps (Bosses only, Full run, Every fight) on the left; the overlay's look and its switches on the right; under them
    /// one quiet line on what the mode counts.
    /// </summary>
    FrameworkElement ParserHead(List<Encounter> past)
    {
        var box = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var modes = new StackPanel { Orientation = Orientation.Horizontal };
        var seg = new Border { Padding = new Thickness(3), CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), Child = modes, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
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
        // the meter (Steven, 2026-11-15: "a toggle in the parser table directly for 'Always on' or 'Only boss fights'")
        var meters = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var (always, label, tip) in new[]
        {
            (false, "Only boss fights", "The meter shows in boss fights and in Transcendence, Expeditions and Sanctuaries; outside them your DPS is one small figure on the Kinah line."),
            (true, "Always on", $"The meter is always up, the open world and the Abyss too. It starts from 0 when a boss is pulled, when you enter a dungeon, and outside dungeons after {BossParser.AlwaysIdleSeconds / 60:0} minutes without fighting."),
        })
        {
            var b = new Button { Content = label, Height = 26, FontSize = 12.5, Padding = new Thickness(12, 0, 12, 0), Margin = new Thickness(meters.Children.Count > 0 ? 2 : 0, 0, 0, 0), ToolTip = Tip(tip) };
            b.SetResourceReference(StyleProperty, always == _parserPrefs.AlwaysOn ? "SegOn" : "SegOff");
            AutomationProperties.SetName(b, "Parser meter: " + label);
            var pick = always;
            b.Click += (_, _) => SetParserAlwaysOn(pick);
            meters.Children.Add(b);
        }
        var mseg = new Border { Padding = new Thickness(3), CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), Child = meters, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
        mseg.SetResourceReference(Border.BackgroundProperty, "SegGroup"); mseg.SetResourceReference(Border.BorderBrushProperty, "CardLine");
        AutomationProperties.SetName(mseg, "When the Parser's meter shows");
        var left = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Children = { seg, mseg } };
        g.Children.Add(left);
        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        // the overlay's look, one click from here (Steven, 2026-11-14: "idk how to change the style"): its name, a click opens
        // Settings › In game at the Parser's look row: its own window's look (Standard, Auto, Capsule, Meter, Float), or
        // inside the Kinah overlay that overlay's look
        // (the look's link sits under it, beside what the mode counts: the head's row holds the two picks and the switch)
        TextBlock? lookLink = null;
        {
            string lookName = _parserPrefs.Separate ? ParserLookName(_parserPrefs.Look) : HudLooks.All.First(l => l.Key == HudLook).Name;
            var look = new { Name = lookName };
            lookLink = QLinkify(PT($"Overlay look: {look.Name}  ›", 12, FontWeights.SemiBold, "Link"));
            lookLink.ToolTip = Tip(_parserPrefs.Separate ? "How the Parser's own window looks over the game: pick another in Settings › In game › Parser." : "How the Parser draws over the game (the Kinah overlay's look): pick another in Settings › In game.");
            lookLink.Margin = new Thickness(16, 7, 0, 0);
            lookLink.VerticalAlignment = VerticalAlignment.Top;
            AutomationProperties.SetName(lookLink, $"Overlay look: {look.Name}. Change it");
            lookLink.MouseLeftButtonUp += (_, _) => OpenParserLook();
        }
        right.Children.Add(ParserSwitch("Overlay", _parserPrefs.Overlay, SetParserOverlay,
            $"The record under way over the game: the boss or the run, the clock and the group. {(_parserPrefs.Separate ? "Its own overlay (Settings › In game › Parser); right-click it for the mode." : "It opens above the Kinah overlay when a fight starts and folds away after.")}{(Hotkey.Parse(_parserPrefs.Hotkey) is { } k ? $" Shortcut: {k}." : "")}", "Fight overlay"));
        // click-through and the drag are the separate overlay's; above the Kinah line it follows the Kinah overlay's own
        if (_parserPrefs.Separate)
            right.Children.Add(Margin(ParserSwitch("Click-through", _parserPrefs.ClickThrough, SetParserClickThrough,
                "Off (the default): drag the overlay anywhere to move it; it stays there. On: clicks go through it to the game, and a handle on its top edge moves it.", "Overlay click-through"), 14, 0, 0, 0));
        Grid.SetColumn(right, 1);
        g.Children.Add(right);
        box.Children.Add(g);
        // Always on: what is kept, then the meter's rule (Bosses only's own sentence says the open world never counts: it is still never kept)
        var kept = !_parserPrefs.AlwaysOn ? BossParser.Counts(ParserModeNow)
            : ParserModeNow == ParserMode.Bosses ? "Keeps each boss pull in Transcendence, Expeditions and Sanctuaries." : BossParser.Counts(ParserModeNow);
        var counts = PT(kept + (_parserPrefs.AlwaysOn ? $" The meter is always up, the open world too: from 0 at each boss pull, on entering a dungeon, and after {BossParser.AlwaysIdleSeconds / 60:0} minutes without fighting." : ""), 12, FontWeights.Medium, "Muted");
        counts.TextWrapping = TextWrapping.Wrap; counts.Margin = new Thickness(2, 7, 0, 0);
        var under = new Grid();
        under.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        under.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        under.Children.Add(counts);
        if (lookLink != null) { Grid.SetColumn(lookLink, 1); under.Children.Add(lookLink); }
        box.Children.Add(under);
        return box;
    }

    /// <summary>Settings › In game, scrolled to the Parser's look row.</summary>
    void OpenParserLook()
    {
        OpenOptions(OptionsWindow.Page.InGame);
        _options?.ShowParserLook();
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
        if (e.BossHpLeft is { } left) { sp.Children.Add(Margin(BossHpBar(e, left), 0, 7, 0, 0)); sp.Children.Add(Margin(BossHpWords(e, left), 0, 5, 0, 0)); }
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

    static string AvgCp(Encounter e)
    {
        var known = e.Roster.Where(f => f.CombatPower > 0).ToList();
        return known.Count == 0 ? "–" : ((int)Math.Round(known.Average(f => f.CombatPower))).ToString("N0", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The group card's switches: Boss / All targets (only when the record has damage to adds to tell apart), then Fight /
    /// Active (DPS over the fight's length or over each member's active time). Small segments, the page's own.
    /// </summary>
    FrameworkElement ParserBasisSwitches(Encounter e)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        FrameworkElement Seg(string name, params (string Label, bool On, Action Pick, string Tip)[] opts)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            var seg = new Border { Padding = new Thickness(2), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Child = sp, VerticalAlignment = VerticalAlignment.Center };
            seg.SetResourceReference(Border.BackgroundProperty, "SegGroup"); seg.SetResourceReference(Border.BorderBrushProperty, "CardLine");
            AutomationProperties.SetName(seg, name);
            foreach (var (label, on, pick, tip) in opts)
            {
                var b = new Button { Content = label, Height = 22, FontSize = 11.5, Padding = new Thickness(9, 0, 9, 0), Margin = new Thickness(sp.Children.Count > 0 ? 2 : 0, 0, 0, 0), ToolTip = Tip(tip) };
                b.SetResourceReference(StyleProperty, on ? "SegOn" : "SegOff");
                AutomationProperties.SetName(b, $"{name}: {label}");
                b.Click += (_, _) => { if (!on) pick(); };
                sp.Children.Add(b);
            }
            return seg;
        }
        if (e.HasAdds)
            row.Children.Add(Seg("Damage counted",
                ("Boss", Encounter.BossOnly, () => SetParserBasis(bossOnly: true), "Each member's damage to the boss only: DPS, shares and the bars."),
                ("All targets", !Encounter.BossOnly, () => SetParserBasis(bossOnly: false), "Each member's damage to everything, adds included.")));
        if (row.Children.Count > 0) row.Children.Add(new Border { Width = 6 });
        row.Children.Add(Seg("DPS over",
            ("Fight", !Encounter.ActiveBasis, () => SetParserBasis(active: false), "Each member's DPS over the whole fight's length."),
            ("Active", Encounter.ActiveBasis, () => SetParserBasis(active: true), $"Each member's DPS over the time they were dealing damage: their first hit to their last, pauses over {Fighter.PauseSeconds:0} seconds left out.")));
        return row;
    }

    /// <summary>
    /// The group card's coverage line (the honest pass: no "Other" row, no damage left out unsaid): how much of the HP the
    /// boss lost is credited to a member, and that DPS and shares count that; the uncredited share, and why when known, on
    /// hover (a figure, so it shows whatever the hover switch says).
    /// </summary>
    FrameworkElement? CoverageLine(Encounter e)
    {
        if (e.Coverage is not { } cov) return null;
        bool run = e.ModeKind == ParserMode.Run;
        string whose = run ? "the bosses' HP" : "the boss's HP";
        string pct = cov >= 1 ? "100%" : Encounter.Pct(cov) == "100%" ? ">99.9%" : Encounter.Pct(cov);
        var t = PT($"{pct} of {whose} credited  ·  DPS and shares count credited damage", 11.5, FontWeights.Medium, cov < 0.95 ? "Warn" : "Faint", trim: true);
        var tip = new List<string>();
        if (e.UncreditedBoss > 0)
            tip.Add($"{Amounts.Short(e.UncreditedBoss)} of the {Amounts.Short(e.BossDamage)} HP {(run ? "the bosses" : "the boss")} lost ({Encounter.Pct(1 - cov)}) came with no hit naming who dealt it, so it is in no member's figures.");
        else tip.Add($"Every point of HP {(run ? "the bosses" : "the boss")} lost is in a member's figures.");
        if (e.Uncredited > e.UncreditedBoss) tip.Add($"Another {Amounts.Short(e.Uncredited - e.UncreditedBoss)} to other targets had no hit to name its source either.");
        var why = e.UncreditedWhy.Where(kv => kv.Key != 0 && kv.Value > 0).GroupBy(kv => Encounter.WhyWords(kv.Key))
            .Select(g => (Words: g.Key, Amount: g.Sum(kv => kv.Value))).OrderByDescending(x => x.Amount).Select(x => $"{x.Words} ({Amounts.Short(x.Amount)})").ToList();
        if (why.Count > 0) tip.Add("Why: " + string.Join("; ", why) + ".");
        t.ToolTip = string.Join(" ", tip);
        AutomationProperties.SetName(t, $"{pct} of {whose} credited");
        return t;
    }

    static TextBlock Tab(TextBlock t) { System.Windows.Documents.Typography.SetNumeralAlignment(t, FontNumeralAlignment.Tabular); return t; }



    /// <summary>The boss's HP as the window's own bar (QBar: the track, the fill with its gloss and glow), gold, coral under a third; it slides as the boss loses HP.</summary>
    FrameworkElement BossHpBar(Encounter e, double left)
    {
        var bar = QBar(left, left < 0.34 ? "Coral" : "Gold", 8, key: $"parser|hp|{e.Id}");
        AutomationProperties.SetName(bar, $"Boss HP {Math.Round(left * 100):0}%");
        return bar;
    }

    /// <summary>"47% · 1.82M left of 3.85M HP" (Steven, 2026-11-14: the HP left beside the boss's %), tabular digits.</summary>
    static TextBlock BossHpWords(Encounter e, double left)
    {
        var t = new TextBlock { FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center };
        System.Windows.Documents.Typography.SetNumeralAlignment(t, FontNumeralAlignment.Tabular);
        var pct = new System.Windows.Documents.Run($"{Math.Round(left * 100):0}%") { FontWeight = FontWeights.Bold, FontSize = 13.5 };
        pct.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, left < 0.34 ? "Coral" : "Ink");
        var rest = new System.Windows.Documents.Run($" · {Amounts.Short(Math.Max(0, e.BossHp))} left") { FontWeight = FontWeights.SemiBold };
        rest.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "Text2");
        var of = new System.Windows.Documents.Run($" of {Amounts.Short(e.BossMaxHp)} HP") { FontWeight = FontWeights.Medium };
        of.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "Muted");
        t.Inlines.Add(pct); t.Inlines.Add(rest); t.Inlines.Add(of);
        AutomationProperties.SetName(t, $"Boss HP {Math.Round(left * 100):0}%, {Amounts.Short(Math.Max(0, e.BossHp))} left of {Amounts.Short(e.BossMaxHp)}");
        return t;
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
        if (e.HasDamage) { fig.Children.Add(Right(PT(e.GroupDpsText(e.End ?? now), 13, FontWeights.SemiBold, "Text2"))); fig.Children.Add(Right(PT("group DPS", 10.5, FontWeights.Medium, "Faint"))); }
        else if (e.ModeKind != ParserMode.Bosses) { fig.Children.Add(Right(PT(e.YourKills.ToString("N0", CultureInfo.InvariantCulture), 13, FontWeights.SemiBold, "Text2"))); fig.Children.Add(Right(PT(e.PlayerKills > 0 ? $"kills · {e.PlayerKills} PvP" : "your kills", 10.5, FontWeights.Medium, "Faint"))); }
        else if (PDetails) { fig.Children.Add(Right(PT(AvgCp(e), 13, FontWeights.SemiBold, "Text2"))); fig.Children.Add(Right(PT("average CP", 10.5, FontWeights.Medium, "Faint"))); }
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

    void OpenFight(string? id) { _parserOpen = id; if (id == null) _parserFromRuns = false; _parserInspect = null; RedrawParserPage(); LowerScroll.ScrollToTop(); }

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
        // the defaults (Group details on since the parser polish); "-all": every Parser option on (the PR's second render);
        // "-min": every option off
        bool everyOption = state.EndsWith("-all"), none = state.EndsWith("-min");
        if (everyOption || none) state = state[..^4];
        // the defaults since the honest pass: deaths, filters and hovers on; kills near you off
        _parserPrefs.KillsNear = everyOption;
        _parserPrefs.Deaths = _parserPrefs.Filters = _parserPrefs.Hovers = !none;
        _parserPrefs.Details = !none;
        _parserPrefs.DpsActive = Encounter.ActiveBasis = false; _parserPrefs.Targets = "boss"; Encounter.BossOnly = true;
        // crit is off as shipped; a render can show it on with KL_CRIT=result3 or mark08 (a render's switch only, never the app's)
        Encounter.CritKey = Encounter.CritKeyOf(Environment.GetEnvironmentVariable("KL_CRIT") ?? _parserPrefs.CritNow);
        Encounter.BackKey = Encounter.BackKeyOf(Environment.GetEnvironmentVariable("KL_BACK") ?? _parserPrefs.BackNow);
        _parserDemoState = state;
        _parserDemoNow = new DateTime(2026, 11, 13, 21, 30, 0, DateTimeKind.Utc);
        var mode = state.StartsWith("run") ? ParserMode.Run : state.StartsWith("fight") ? ParserMode.Fights : ParserMode.Bosses;
        bool live = state is "live" or "nodamage" or "nogroup" or "run" or "fight" or "inspect" or "compare" or "timeline" or "taken" or "full" or "inspect-full";
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
        if (now is { ModeKind: ParserMode.Bosses, HasDamage: true } && mode == ParserMode.Bosses) all.AddRange(ParserDemoEarlier(now, _parserDemoNow));
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
        // Parser v2's views: the tab, the member opened, and for the renders of what the record will carry (the timeline, damage
        // taken, crits), a sample of those figures (ParserViewSample: render states only, the app never sets it)
        _parserTab = state switch { "compare" => "Compare", "timeline" => "Timeline", "taken" => "Taken", _ => "Group" };
        _parserSort = "Damage";
        var shownNow = _parserOpen != null ? all.FirstOrDefault(e => e.Id == _parserOpen) : now;
        _parserInspect = state is "inspect" or "inspect-full" ? shownNow?.Ranked().FirstOrDefault(f => f.You && f.Abilities.Count > 0)?.Id : null;
        ParserViewSample = state is "timeline" or "taken" or "full" or "inspect-full" ? ParserDemoDepth : null;
        ShowPart("Parser", byHand: false);
        RedrawParserPage();
    }

    /// <summary>parser-latency: the page and the overlay on a replay's Parser, drawn as live (not a demo: nothing held still).</summary>
    internal void ShowParserReplay(BossParser parser, ParserMode mode)
    {
        _demo = false;
        ParserSourceForTest = parser; ParserOverlayForceForTest = true;
        _parserPrefs.InKinah = true;   // the meter in the off-screen Kinah overlay, as it has been timed: its own window would show on the screen
        StartFarmTicker();
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
            if (d is FrameworkElement h && Equals(h.Tag, "parser-hero") && _parserOpen == null && ParserLive is { Live: true }) live = true;
            foreach (var ch in LogicalTreeHelper.GetChildren(d).OfType<DependencyObject>()) Walk(ch);
        }
        int roster = (ParserLive ?? (_parserOpen != null ? ParserPast().FirstOrDefault(e => e.Id == _parserOpen) : null))?.Members.Count() ?? 0;
        return (live, rows, detail, roster);
    }

    internal void OpenFightForTest(string? id) => OpenFight(id);
    internal List<Encounter> ParserPastForTest => ParserPast();
    internal string? ParserOpenForTest => _parserOpen;
    internal ParserOverlay? ParserOverlayForTest => _parserOverlay;
    internal void SetParserOverlayForTest(bool on) => SetParserOverlay(on);
    internal void SetParserSeparateForTest(bool on) { _parserPrefs.Separate = on; DrawOverlayPills(); RedrawParserPage(); }

    /// <summary>A Parser window look's name: "Standard" for its own panel, else the fight look's (Auto, Capsule, Meter, Float).</summary>
    internal static string ParserLookName(string? key)
    {
        var k = ParserPrefs.LookOf(key);
        return k == ParserPrefs.StandardLook ? "Standard" : HudLooks.All.First(l => l.Key == k).Name;
    }

    /// <summary>The Parser's own window is wanted (the game in front aside): set by the overlay's tick when it has its own window.</summary>
    bool _parserOwnWanted;

    /// <summary>
    /// For the options test: one overlay tick on <paramref name="parser"/> with the meter Always on, the Parser in its own
    /// window or inside the Kinah overlay, against an off-screen Kinah overlay; nothing shown on any screen (the game is not
    /// in front, so the Parser's own window is only wanted). What the Kinah overlay did, and whether the own window was wanted.
    /// </summary>
    internal (bool KinahHidden, bool ParserInKinah, bool OwnWanted) TwoWindowsForTest(HudWindow hud, BossParser parser, bool inKinah)
    {
        var (hud0, demo0, src0, force0, prefs0) = (_hud, _demo, ParserSourceForTest, ParserOverlayForceForTest, (_parserPrefs.AlwaysOn, _parserPrefs.Overlay, _parserPrefs.InKinah));
        try
        {
            _hud = hud; _demo = false; ParserSourceForTest = parser; ParserOverlayForceForTest = false;
            _parserPrefs.AlwaysOn = true; _parserPrefs.Overlay = true; _parserPrefs.InKinah = inKinah;
            _parserOwnWanted = false;
            var hudPreview0 = _hudPreview; bool hudOn0 = _prefs.Hud;
            _hudPreview = null; _prefs.Hud = true;   // the Kinah overlay wanted, as the player has it (a test never saves _prefs)
            try { ParserOverlayTick(); } finally { _hudPreview = hudPreview0; _prefs.Hud = hudOn0; }
            return (hud.KinahHidden, hud.ParserShown, _parserOwnWanted);
        }
        finally
        {
            hud.SetKinahHidden(false); hud.ShowParser(null, DateTime.UtcNow, ParserModeNow, PDetails);
            (_hud, _demo, ParserSourceForTest, ParserOverlayForceForTest) = (hud0, demo0, src0, force0);
            (_parserPrefs.AlwaysOn, _parserPrefs.Overlay, _parserPrefs.InKinah) = prefs0;
            _parserOverlay?.ShowWanted(false);
        }
    }
}
