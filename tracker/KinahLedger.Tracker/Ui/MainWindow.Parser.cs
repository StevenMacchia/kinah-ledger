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
/// The Parser page (Steven, 2026-11-13: "It should have its own tab for parser"). It keeps boss pulls (Parser/BossParser.cs):
/// each boss pull in a Transcendence, an Expedition, a Sanctuary or Nightmare, and each world boss. The Full run and Every
/// fight modes of 2026-11-13 are gone (Steven, 2026-11-17: "get rid of full run and every fight"); records an older version
/// kept in them stay on the PC, out of the list, and still open. On
/// top, the record under way with its clock and the group (name, class, Combat Power, item level; damage and DPS only when
/// the game's messages give them, never made up) or, with none, where you are and what starts one. Under it, the records
/// kept on this PC for the character playing, by mode, kind and boss or instance; a click opens one. The overlay over the
/// game (Ui/ParserOverlay.cs) is switched here, with its shortcut.
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
    /// <summary>The parser's late healing when the held result was last read (Parser/BossParser.cs LateHeals).</summary>
    long _parserLateHeals;
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
            _parserPrefs.Details = fresh.Details; _parserPrefs.KillsNear = fresh.KillsNear; _parserPrefs.Deaths = fresh.Deaths; _parserPrefs.Targets = fresh.Targets; _parserPrefs.Filters = fresh.Filters; _parserPrefs.Hovers = fresh.Hovers; _parserPrefs.Separate = fresh.Separate; _parserPrefs.Look = fresh.Look; _parserPrefs.DpsActive = fresh.DpsActive;
            Encounter.ActiveBasis = fresh.DpsActive; Encounter.BossOnly = fresh.Targets != "all"; Encounter.CritKey = Encounter.CritKeyOf(fresh.CritNow); Encounter.BackKey = Encounter.BackKeyOf(fresh.BackNow);
            _parserPrefs.Width = fresh.Width; _parserPrefs.WidthPicked = fresh.WidthPicked; _parserPrefs.Expanded = fresh.Expanded; _parserPrefs.HealBars = fresh.HealBars; _parserPrefs.PvpPanel = fresh.PvpPanel; _parserPrefs.OdyleEnergy = fresh.OdyleEnergy; _parserPrefs.Ghost = fresh.Ghost; _parserPrefs.GhostGroup = fresh.GhostGroup; ApplyMeterExpanded();
            ParserOverlayTick();
            DrawOverlayPills();   // the menu lists the Parser only while it is separate
            if (!_parserPrefs.Filters) { _parserPrefs.KindFilter = ""; _parserPrefs.BossFilter = ""; }
            RedrawParserPage();
        });
        // your row named as your character when nothing else names you (alone in the open world): the character in the world,
        // read when a row is drawn (never written into the record, so a switch to an alt never leaves the old name behind)
        if (watch) ParserRows.YourName = () => Session.Characters.Current?.Name ?? "";
        // KL_PARSER_CLICKTHROUGH=1: a render's meter with click-through on, as Steven plays (2026-11-15)
        // the options test and renders keep the switches 1.0.18 turned on for new installs as they were (off), so their checks of
        // each still hold; ParserPrefs' own defaults are parser-test's (Parser/ParserTests.cs)
        _parserPrefs = watch ? ParserPrefs.Load() : new ParserPrefs { ClickThrough = Environment.GetEnvironmentVariable("KL_PARSER_CLICKTHROUGH") == "1", AlwaysOn = false, Expanded = false, KillsNear = false };
        ApplyMeterExpanded();
        // a new best on a boss (the overlay's PB moment) is measured against your kills of it kept on this PC
        // your record on this fight (this boss in this instance and mode), kept in records.json: the overlay's record moments and
        // its pace measure against it, never against the same boss elsewhere (Steven, 2026-11-16: "ONLY pop up on a per fight basis")
        ParserRows.BestOf = e => _demo ? FightView.Records(e, ParserPast()).Best : RecordRef(TrackerSession.FightStore.Records.Of(e), e);
        // the PB ghost (Parser/PbGhost.cs): your best kill of this fight second by second, and the best before this pull
        PbGhost.Source = e => _demo ? DemoGhost(e) : (TrackerSession.FightStore.Records.GhostFor(e, () => TrackerSession.FightStore.All()), RecordRef(TrackerSession.FightStore.Records.Of(e), e)?.Dps ?? 0);
        if (watch) StartFarmTicker();
        Encounter.ActiveBasis = _parserPrefs.DpsActive; Encounter.BossOnly = _parserPrefs.Targets != "all"; Encounter.CritKey = Encounter.CritKeyOf(_parserPrefs.CritNow); Encounter.BackKey = Encounter.BackKeyOf(_parserPrefs.BackNow);   // crit and back attacks: none until a release picks the kind
        if (watch) TrackerSession.ParserMode = ParserModeNow;   // a session made from now on starts in it
        ParserTab.Click += (_, _) => { _parserFromRuns = false; ShowPart("Parser"); };
        // five pills and the Timers countdown in the header's tab column: a little less room each, so Parser isn't cut off
        foreach (var tab in PartTabRow.Children.OfType<Button>()) tab.Padding = new Thickness(8, 0, 8, 0);
        // the header's Overlays menu (Ui/MainWindow.Overlays.cs): the Parser is a section of the Kinah overlay by default, and
        // an overlay of its own in the menu only once Settings › In game › Parser separates it
        RegisterOverlay("Parser", () => ParserOverlayOn, on => ParserOverlayOn = on, L.T("DPS by member, the boss's HP, the pull timer. Set up in Settings › DPS meter."), visible: () => _parserPrefs.Separate || !HudWanted);
        InitKillPrompt();   // a kill's Copy picture on the status line (Ui/MainWindow.ParserShare.cs)
        InitMeterShare(watch);   // the meter's share picture and its shortcut (Ui/MainWindow.MeterShare.cs)
        InitParserBack();   // Backspace, Alt+← and the mouse's back button on a pull open (Ui/MainWindow.ParserShare.cs)
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
        Say(on ? L.T("Fight overlay on: it shows over the game during boss fights.") : L.T("Fight overlay off."), "Faint");
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
    /// <summary>The meter always up (Options › DPS meter › When it shows, as the Parser page's Always on).</summary>
    internal bool ParserAlwaysOn { get => _parserPrefs.AlwaysOn; set => SetParserAlwaysOn(value); }
    internal void ResetParserOverlayPosition()
    {
        _parserPrefs.Left = null; _parserPrefs.Top = null; _parserPrefs.Bottom = null;
        if (!_demo) _parserPrefs.Save();
        _parserOverlay?.Place(null, null, null, _watcher.GameRect);
    }

    void SetParserClickThrough(bool on)
    {
        _parserPrefs.ClickThrough = on;
        if (!_demo) _parserPrefs.Save();
        _parserOverlay?.SetClickThrough(on);
        RedrawParserPage();
    }

    /// <summary>
    /// What the Parser keeps: boss pulls, always (Steven, 2026-11-17: "get rid of full run and every fight"). The parser's
    /// other modes stay inside Parser/BossParser.cs, out of reach: nothing picks them, and parser.json's old "run" or "fight"
    /// reads as "boss" (ParserPrefs.Settle).
    /// </summary>
    ParserMode ParserModeNow => ParserMode.Bosses;
    /// <summary>Which records the list shows: boss pulls (an old run or fight kept on this PC is left out of it).</summary>
    ParserMode ParserListMode => ParserMode.Bosses;

    static string Noun(ParserMode m, int n = 1) => L.T(m switch { ParserMode.Run => n == 1 ? "run" : "runs", ParserMode.Fights => n == 1 ? "fight" : "fights", _ => n == 1 ? "boss pull" : "boss pulls" });

    /// <summary>What the Parser counts, in the language in use (BossParser.Counts is the English).</summary>
    static string ModeCounts(ParserMode m) => L.T(BossParser.Counts(m));

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
    /// <summary>For parser-latency: the window's own time on each meter draw (a hit's redraw, the clock's), in ms.</summary>
    internal static ParserLatency MeterDrawStats { get; } = new();
    /// <summary>The same since the last line in reads.log (the line a minute starts it again; the one above keeps everything).</summary>
    static readonly ParserLatency ParserLatencyMinute = new();
    /// <summary>A replay's Parser (parser-latency), in place of the session's; the overlay shows whatever is in front.</summary>
    internal BossParser? ParserSourceForTest { get; set; }
    internal bool ParserOverlayForceForTest { get; set; }

    // ---- the PvP panel (Parser/PvpKills.cs) ----

    /// <summary>For the tests and renders: the PvP kills and whether the player is in PvP, in place of the session's.</summary>
    internal (PvpKills Kills, bool InPvp)? PvpSourceForTest { get; set; }
    PvpKills? PvpSource => PvpSourceForTest?.Kills ?? _watcher.Chat?.Pvp;
    internal bool PvpPanelForTest { get => _parserPrefs.PvpPanel; set => _parserPrefs.PvpPanel = value; }
    bool InPvpNow => PvpSourceForTest?.InPvp ?? _watcher.Chat?.InPvp ?? false;
    Encounter? _pvpBoard;
    int _pvpBoardVersion = -1;

    /// <summary>
    /// The PvP panel when the meter shows it: in the Abyss or an arena with the switch on (Settings › DPS meter › More), unless a
    /// boss fight is what the meter holds (an Abyss boss's pull, a run). Before the first kill only where the meter would show
    /// anyway (Always on): the open world's fights never opened it.
    /// </summary>
    internal Encounter? PvpBoard(Encounter? show, DateTime now)
    {
        if (!_parserPrefs.PvpPanel || !InPvpNow || PvpSource is not { } pvp) return null;
        if (show is { OpenWorld: false, Waiting: false } && (show.Boss.Length > 0 || show.ModeKind == ParserMode.Run)) return null;
        int v = pvp.Version;
        if (v == 0 && show == null) return null;
        if (_pvpBoard == null || v != _pvpBoardVersion) { _pvpBoard = pvp.Board(now); _pvpBoardVersion = v; }
        return _pvpBoard;
    }

    PvpKills? _pvpWatched;

    /// <summary>Listens to the Parser of the session playing (a new session's when it changes).</summary>
    internal void WatchParser()
    {
        var f = ParserFights;
        // a PvP kill redraws the meter as a hit does
        if (!ReferenceEquals(PvpSource, _pvpWatched))
        {
            if (_pvpWatched != null) _pvpWatched.Changed -= OnPvpChanged;
            _pvpWatched = PvpSource;
            if (_pvpWatched != null) _pvpWatched.Changed += OnPvpChanged;
        }
        if (ReferenceEquals(f, _parserWatched)) return;
        if (_parserWatched != null) _parserWatched.Changed -= OnParserChanged;
        _parserWatched = f;
        if (f != null) f.Changed += OnParserChanged;
    }

    /// <summary>A change, from any thread: one redraw is queued, now or when the last one is <see cref="ParserFrameMs"/> old.</summary>
    /// <summary>For parser-latency: changes the Parser said, frames drawn for them, of which on screen.</summary>
    internal static int ParserChangesSeen, ParserDraws, ParserDrawsShown;
    /// <summary>For parser-latency: the replay's own Kinah overlay (hidden, off-screen), so the meter in it is drawn as live.</summary>
    internal void AttachHudForTest(HudWindow h) => _hud = h;
    void OnPvpChanged() => OnParserChanged(null);
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
        long drawStart = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            if (_part == "Parser") FillParserPage();
            ParserOverlayTick();
        }
        catch (Exception ex) { CrashLog.Write("parser page", ex); }
        MeterDrawStats.Add(System.Diagnostics.Stopwatch.GetElapsedTime(drawStart).TotalMilliseconds);
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
    Encounter? ParserLive => _demo ? _parserDemoLive : _parserPrefs.AlwaysOn ? ParserFights?.AlwaysLive : ParserFights?.Live;
    (ContentKind Kind, string Instance) ParserWhere => _demo ? _parserDemoWhere : ParserFights?.Where ?? (ContentKind.Other, "");
    /// <summary>A fight's record as the meter measures against it: the best before this fight (DPS and healing a second).</summary>
    internal static PullRef? RecordRef(FightRecord? r, Encounter e)
    {
        if (r == null) return null;
        double dps = r.DpsBefore(e.Id), hps = r.HpsBefore(e.Id);
        return dps > 0 || hps > 0 ? new PullRef { E = e, Dps = dps, Hps = hps, When = r.BestDpsFight == e.Id ? DateTime.MinValue : r.BestDpsAt } : null;
    }

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
            KillPromptTick();   // a boss just killed: Copy picture on the page and the status line (Ui/MainWindow.ParserShare.cs)
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
        // a heal of the pull's last moment can come after its kill: the held result takes it (the pull's own record, re-read)
        if (live == null && _parserJustEnded != null && ParserFights is { } pf && pf.LateHeals != _parserLateHeals)
        {
            _parserLateHeals = pf.LateHeals;
            if (pf.EndedNow(_parserJustEnded.Id) is { } fresh) _parserJustEnded = fresh;
        }
        var show = live ?? ((now - _parserEndedAt).TotalSeconds <= OverlayResultSeconds ? _parserJustEnded : null);
        // Steven, 2026-11-14: "turn into the parser when you're in a dungeon - it should not disappear after a fight and persist
        // throughout the dungeon - it should reset each fight": in a Transcendence, an Expedition or a Sanctuary the meter stays,
        // the last fight's result until the next one starts afresh, the dungeon's ready card before the first
        if (show == null && DungeonMeter(now) is { } held) show = held;
        // Steven, 2026-11-14: big "only inside of expeditions and other runs": an open-world fight never opens the section or
        // the separate overlay (the Parser page shows your DPS in it; the Kinah line's small figure went on 2026-11-15)
        if (show is { OpenWorld: true }) show = null;
        // Steven, 2026-11-15: "Always on": the meter is always up, anywhere, and starts from 0 by the parser's rules
        if (_parserPrefs.AlwaysOn) show = AlwaysMeter(now);
        // Steven, 2026-11-20: "instead of having the DPS meter in PVP ... a panel which shows your last 3 kills": in the Abyss
        // and the arenas the meter is the PvP panel (a boss pull there keeps the DPS meter)
        if (PvpBoard(show, now) is { } board) show = board;
        MeterPopOutTick(show, now);   // the meter's own window, apart from the overlay (Ui/MainWindow.PopOuts.cs)
        // the meter's clock redraws this one between hits (MeterClockTick)
        _meterShown = _parserPrefs.Overlay ? show : null;
        _meterOnOverlay = _meterShown;
        _meterInKinah = !ParserOverlaySeparate;
        StartMeterClock();
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
        // shown while the game is open, a tab out included (Steven, 2026-11-17); minimized or closed takes it away
        bool inFront = ParserOverlayForceForTest || _watcher.GameRect != null;
        bool want = _parserOwnWanted && inFront;
        if (!want) { _parserOverlay?.ShowWanted(false); return; }
        if (_parserOverlay == null)
        {
            _parserOverlay = new ParserOverlay { HotkeyText = _parserPrefs.Hotkey };   // "Off" (the default) says no shortcut
            _parserOverlay.Dropped += (x, y, b) => { _parserPrefs.Left = x; _parserPrefs.Top = y; _parserPrefs.Bottom = b; _parserPrefs.Save(); };
            _parserOverlay.HideAsked += () => SetParserOverlay(false);
            _parserOverlay.SettingsAsked += () => { OpenOptions(OptionsWindow.Page.Meter); Activate(); };
            WireOverlayShare(_parserOverlay);
            _parserOverlay.Clicked += ToggleMeterExpanded;
            // an edge dragged on the Parser's own window: its width kept from then on (Steven, 2026-11-15: "let the user drag size/width from the overlay itself")
            _parserOverlay.FightWidthSet += w => { _parserPrefs.Width = w - MeterExtraWidth; _parserPrefs.WidthPicked = true; if (!_demo) _parserPrefs.Save(); _hud?.ApplyFightWidth(); };
            _parserOverlay.SetClickThrough(_parserPrefs.ClickThrough);
            _parserOverlay.SetOpacity(_hudPreview?.HudOpacity ?? _prefs.HudOpacity);
            _parserOverlay.SetScale(_hudPreview?.HudScale ?? _hudPrefs.Scale);   // the Kinah overlay's size: Buddy's width on screen
            var (radius, pad) = HudWindow.FrameShape(HudFields.Parse(_prefs.HudFields));
            _parserOverlay.SetFrame(radius, pad);
            _parserOverlay.Look = _parserPrefs.Look;
            _parserOverlay.Mode = ParserModeNow;
            _parserOverlay.ShowDetails = PDetails;
            _parserOverlay.ShareShown = Shareable(show);
            _parserOverlay.Draw(show!, now);
            _parserOverlay.ShowWanted(true);
            _parserOverlay.SetClickThrough(_parserPrefs.ClickThrough);
            _parserOverlay.Place(_parserPrefs.Left, _parserPrefs.Top, _parserPrefs.Bottom, _watcher.GameRect);
            return;
        }
        _parserOverlay.Look = _parserPrefs.Look;   // Settings › In game › Parser › Overlay look, at once
        _parserOverlay.Mode = ParserModeNow;
        _parserOverlay.ShowDetails = PDetails;
        _parserOverlay.ShareShown = Shareable(show);
        _parserOverlay.Draw(show!, now);
        _parserOverlay.ShowWanted(true);
    }


    // ---- the meter's clock (Steven, 2026-11-15: "it should be refreshing the numbers every like 0.01 seconds and moving it
    // depending on if youre attacking or not") ----
    // A hit redraws the meter at once (OnParserChanged). Between hits the DPS falls as time passes, so the meter is drawn
    // again 20 times a second from the copy last fetched: no new copy of the record, only the clock moves. Only while a live
    // record is on a visible meter.

    /// <summary>How often the live meter is drawn between hits.</summary>
    const int MeterClockMs = 50;
    System.Windows.Threading.DispatcherTimer? _meterClock;
    Encounter? _meterShown;
    bool _meterInKinah;
    /// <summary>For the options test: the meter's clock ticks and the live meters it drew.</summary>
    internal static int MeterClockDraws;

    void StartMeterClock()
    {
        if (_meterClock != null || _demo) return;
        _meterClock = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(MeterClockMs) };
        _meterClock.Tick += (_, _) => MeterClockTick(DateTime.UtcNow);
        _meterClock.Start();
    }

    internal void MeterClockTick(DateTime now)
    {
        MeterPopOutClock(now);   // the meter's own window (Ui/MainWindow.PopOuts.cs)
        if (_demo || _meterShown is not { Live: true, End: null } e) return;
        long drawStart = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            if (_meterInKinah) { if (_hud is { IsVisible: true } h) { h.ShowParser(e, now, ParserModeNow, PDetails); MeterClockDraws++; } }
            else if (_parserOverlay is { IsVisible: true } o) { o.Draw(e, now); MeterClockDraws++; }
        }
        catch (Exception ex) { CrashLog.Write("parser meter clock", ex); _meterShown = null; }
        MeterDrawStats.Add(System.Diagnostics.Stopwatch.GetElapsedTime(drawStart).TotalMilliseconds);
    }

    /// <summary>For the options test: the meter's clock on a given record, as if the overlay showed it.</summary>
    internal void SetMeterShownForTest(Encounter? e, bool inKinah) { _meterShown = e; _meterInKinah = inKinah; }

    /// <summary>
    /// The meter to hold while in a dungeon with no fight under way: the last fight that ended in this dungeon, else its ready
    /// card; null outside one.
    /// </summary>
    Encounter? DungeonMeter(DateTime now)
    {
        var (kind, instance) = ParserWhere;
        if (!Encounter.Dungeon(kind)) return null;
        if (_parserJustEnded is { OpenWorld: false } last && BossBook.Strip(last.Instance).Equals(BossBook.Strip(instance), StringComparison.OrdinalIgnoreCase)) return last;
        if (_parserReady == null || !_parserReady.Instance.Equals(instance, StringComparison.Ordinal)) _parserReady = Encounter.Ready(kind, instance, "", now);
        return WithParty(_parserReady);
    }
    Encounter? _parserReady;

    /// <summary>A ready card with the party in it as it stands (Combat Power and class from the party list), so the meter shows who is there before a hit.</summary>
    Encounter WithParty(Encounter ready)
    {
        var group = ParserFights?.Group ?? Array.Empty<GroupMember>();
        var sig = string.Join("|", group.Select(m => $"{m.Id}:{m.Name}:{m.CombatPower}:{m.Class}"));
        if (ready.Roster.Count == group.Count && _readyPartySig == sig) return ready;
        _readyPartySig = sig;
        ready.Roster.Clear();
        foreach (var m in group) ready.Roster.Add(Fighter.From(m));
        return ready;
    }
    string _readyPartySig = "";

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
        bool dungeon = Encounter.Dungeon(kind);
        static bool Kept(Encounter e) => Encounter.Dungeon(Encounter.KindOf(e.Kind));
        if (dungeon)
        {
            if (held != null && Kept(held) && BossBook.Strip(held.Instance).Equals(BossBook.Strip(instance), StringComparison.OrdinalIgnoreCase)) return held;
            if (_parserReady == null || _parserReady.ModeKind != ParserMode.Bosses || !_parserReady.Instance.Equals(instance, StringComparison.Ordinal)) _parserReady = Encounter.Ready(kind, instance, "", now);
            return WithParty(_parserReady);
        }
        if (held != null && !Kept(held)) return held;
        var place = pf.Zone.Trim().Length > 0 ? pf.Zone.Trim() : "Open world";
        if (_parserReady == null || _parserReady.ModeKind != ParserMode.Fights || _parserReady.Instance != place) _parserReady = Encounter.ReadyAnywhere(place, now);
        return WithParty(_parserReady);
    }

    /// <summary>Always on or Only boss fights (the Parser page's head): remembered, the meter follows at once.</summary>
    void SetParserAlwaysOn(bool on)
    {
        if (_parserPrefs.AlwaysOn == on) return;
        _parserPrefs.AlwaysOn = on;
        if (!_demo) _parserPrefs.Save();
        Say(on ? L.T("Parser meter: always on") : L.T("Parser meter: boss fights only"), "Faint");
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
        HudStackLook.FightWidth = w + MeterExtraWidth;   // the slider's is the compact width: the expanded meter keeps its extra
        _hud?.ApplyFightWidth();
        _parserOverlay?.ApplyFightWidth();
        if (_parserOverlay != null) ParserOverlayTick();
    }

    /// <summary>In a Transcendence, an Expedition or a Sanctuary now (the meter replaces the Kinah line there).</summary>
    bool InDungeon => Encounter.Dungeon(ParserWhere.Kind);

    /// <summary>Your live DPS in the open-world fight under way ("13.2K"), or "" (none, or no hit of yours yet): the Parser page's readout.</summary>
    string FarmDpsText(DateTime now)
    {
        if (ParserFights?.FarmDpsNow(now) is not { } dps || dps <= 0) return "";
        return FarmFigure(dps);
    }

    /// <summary>
    /// The readout's one format, so its width holds while it rolls (tabular digits do the rest): under 1,000 as is, then
    /// thousands with one decimal ("13.2K"), millions with two ("1.25M").
    /// </summary>
    internal static string FarmFigure(double v) => v < 1_000 ? Math.Round(v).ToString("0", CultureInfo.InvariantCulture)
        : v < 999_950 ? (v / 1_000).ToString("0.0", CultureInfo.InvariantCulture) + "K" : (v / 1_000_000).ToString("0.00", CultureInfo.InvariantCulture) + "M";

    System.Windows.Threading.DispatcherTimer? _farmTicker;
    /// <summary>The tab's own live readout of your farming DPS (the open-world fight's card).</summary>
    TextBlock? _parserFarmNow;

    /// <summary>
    /// The live readout's clock (Steven, 2026-11-14: "rolling all the time"): ten times a second while you farm, the Parser
    /// page's figure is worked out again, so it slides between hits and eases off when you stop; each hit also redraws it at
    /// once (the Parser's change, ParserDrawNow). Idle otherwise: one cheap check a tick.
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
            if (_parserFarmNow == null) return;
            var text = FarmDpsText(DateTime.UtcNow);
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
        sig += $"|{_meterBlock}|{SelfCheckDue}";
        if (!Changed("parserpage", sig)) return;

        ParserPage.Children.Clear(); _parserFarmNow = null;
        // what keeps the meter off, first (Npcap missing, Ui/MainWindow.MeterBlock.cs): the waiting card then has nothing to add
        var block = MeterBlockCard(_meterBlock);
        _meterCardForTest = block;
        if (block != null) ParserPage.Children.Add(block);
        else if (SelfCheckDue) ParserPage.Children.Add(SelfCheckLine());
        var open = _parserOpen != null ? past.FirstOrDefault(e => e.Id == _parserOpen) : null;
        // the live view holds the last boss fight until the next one starts (Steven, 2026-11-18: the graphs should stay up),
        // its graphs, strip and result in place of the Ready card; an open-world tally (Always on) never takes its place
        var held = ParserHeldFight(past);
        var pageLive = PageLive(live, held);
        // a member opened (Parser v2): their inspect view in place of the fight's
        var inspect = InspectBlock(open ?? pageLive ?? held, now, past);
        if (inspect == null && open == null) _parserOpen = null;
        // over a held fight, the Ready line ("Ready in Ferocious Horn Den") sits in the head, beside the copy buttons
        bool ready = inspect == null && open == null && pageLive == null && held != null && block == null;
        ParserPage.Children.Add(ParserHead(past, ready ? ParserReadyLine(where, live, now) : null));
        if (inspect != null) { ParserPage.Children.Add(inspect); return; }
        if (open != null) { ParserPage.Children.Add(FightBlock(open, now, past, live: false)); return; }
        if (pageLive != null) ParserPage.Children.Add(FightBlock(pageLive, now, past, live: true));
        else if (held != null) ParserPage.Children.Add(FightBlock(held, now, past, live: false, held: true));
        else if (block == null) ParserPage.Children.Add(ParserWaitingCard(where, past));
        ParserPage.Children.Add(ParserHistory(past, now));
    }

    /// <summary>The fight the live view holds between boss fights: the newest boss pull kept (this session's or from before a restart) with a group in it.</summary>
    Encounter? ParserHeldFight(List<Encounter> past) => past.LastOrDefault(e => e.ModeKind == ParserModeNow && e.Roster.Any(f => !f.Other));

    /// <summary>The fight under way the page shows: a boss fight always; an open-world tally only while there is no boss fight to hold.</summary>
    static Encounter? PageLive(Encounter? live, Encounter? held) => live != null && (!live.OpenWorld || held == null) ? live : null;

    /// <summary>The fight the page shows now: one opened, else the one under way, else the one held.</summary>
    Encounter? ParserShownFight(List<Encounter> past)
    {
        if (_parserOpen != null && past.FirstOrDefault(e => e.Id == _parserOpen) is { } open) return open;
        var held = ParserHeldFight(past);
        return PageLive(ParserLive, held) ?? held;
    }

    /// <summary>
    /// Over a held fight, in the head beside the copy buttons: the Ready card in a few words ("Ready in Ferocious Horn Den",
    /// all of it on hover; "the next boss starts a new fight" after it where there's room), and while you fight in the open
    /// world, your DPS there rolling at its end (FarmTick).
    /// </summary>
    FrameworkElement ParserReadyLine((ContentKind Kind, string Instance) where, Encounter? live, DateTime now)
    {
        var (head, line, tone) = ParserStatus(where);
        var g = new Grid { Tag = "parser-ready-line", ToolTip = Tip(line), VerticalAlignment = VerticalAlignment.Center };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var dot = new Border { Width = 7, Height = 7, CornerRadius = new CornerRadius(3.5), Margin = new Thickness(0, 1, 7, 0), VerticalAlignment = VerticalAlignment.Center };
        dot.SetResourceReference(Border.BackgroundProperty, tone);
        g.Children.Add(dot);
        var text = PT(head, 12, FontWeights.SemiBold, "Text2", trim: true);
        text.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(text, 1); g.Children.Add(text);
        if (live is { OpenWorld: true, You.Damage: > 0 } f)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            sp.Children.Add(PT("  ·  " + L.T("Your DPS now") + "  ", 12, FontWeights.Medium, "Faint"));
            _parserFarmNow = PT(FarmDpsText(now) is { Length: > 0 } t0 ? t0 : FarmFigure(f.FarmDps()), 12, FontWeights.Bold, "Ink");
            System.Windows.Documents.Typography.SetNumeralAlignment(_parserFarmNow, FontNumeralAlignment.Tabular);
            _parserFarmNow.MinWidth = 40;
            sp.Children.Add(_parserFarmNow);
            Grid.SetColumn(sp, 2); g.Children.Add(sp);
        }
        return g;
    }

    /// <summary>
    /// The page's head, one line (Steven, 2026-11-18: "No settings should be from the app outside settings. Make copy and
    /// pasting EASY and promoted"; then, on 1.0.18's crowded head, "like bro this is awful"). Left, a big Copy picture, Copy
    /// text and the ▾ for the fight shown (Ui/MainWindow.ParserShare.cs); beside them a kill just made with its own Copy
    /// picture, or over a held fight the Ready line, or the keys in one quiet line (and on the buttons' hovers), each where
    /// there's room; right, Pop out and a Settings link to Settings › DPS meter, where the meter's switches (when it shows, the
    /// overlay, click-through, its look) all live now.
    /// </summary>
    FrameworkElement ParserHead(List<Encounter> past, FrameworkElement? ready = null)
    {
        var g = new Grid { Margin = new Thickness(0, 0, 0, 8), Tag = "parser-head" };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.Children.Add(ParserCopyBar(past));
        var middle = ParserHeadLine(CopyFightNow(past) != null, ready);
        Grid.SetColumn(middle, 1); g.Children.Add(middle);
        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
        var pop = (Button)MeterPopOutButton();   // the meter in its own window (Ui/MainWindow.PopOuts.cs): an action, not a setting
        pop.Height = 32; pop.Margin = new Thickness(0, 0, 12, 0);
        right.Children.Add(pop);
        var set = QLinkify(PT(L.T("Settings  ›"), 12.5, FontWeights.SemiBold, "Link"));
        set.VerticalAlignment = VerticalAlignment.Center;
        set.ToolTip = L.T("The meter's settings: when it shows, the overlay, click-through, its look and width, what it shows.");
        set.Tag = "parser-settings";
        AutomationProperties.SetName(set, "DPS meter settings");
        set.MouseLeftButtonUp += (_, _) => OpenOptions(OptionsWindow.Page.Meter);
        right.Children.Add(set);
        Grid.SetColumn(right, 2);
        g.Children.Add(right);
        return g;
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
        l.Children.Add(PT(L.T("Now"), 11.5, FontWeights.SemiBold, "Gold"));
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
        Cell(head, 1, PT(L.T("Boss"), 11, FontWeights.SemiBold, "Faint"));
        Cell(head, 2, Right(PT(L.T("At"), 11, FontWeights.SemiBold, "Faint")));
        Cell(head, 3, Right(PT(L.T("Time"), 11, FontWeights.SemiBold, "Faint")));
        if (dps) Cell(head, 5, Right(PT(L.T("Group DPS"), 11, FontWeights.SemiBold, "Faint")));
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
            if (x.Final) nm.Children.Add(Margin(PT(L.T("final"), 11, FontWeights.Medium, "Faint"), 6, 1, 0, 0));
            if (x.Wave) nm.Children.Add(Margin(PT(L.T("waves"), 11, FontWeights.Medium, "Faint"), 6, 1, 0, 0));
            if (x.Partial) nm.Children.Add(Margin(PT(L.T("partial"), 11, FontWeights.SemiBold, "Warn"), 6, 1, 0, 0));
            Cell(row, 1, nm);
            Cell(row, 2, Right(PT(BossParser.Clock(x.Start - e.Start), 12.5, FontWeights.Medium, "Muted")));
            Cell(row, 3, Right(PT(BossParser.Clock(TimeSpan.FromSeconds(x.Seconds)), 12.5, FontWeights.SemiBold, "Ink")));
            var tone = x.Outcome switch { "kill" => (L.T("Killed"), "Mint", "MintTint"), "wipe" => (L.T("Wiped"), "Coral", "CoralTint"), "left" => (L.T("Left"), "Muted", "ChipBg"), _ => (L.T("In progress"), "Gold", "GoldTint") };
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
        return stage.Length > 0 ? $"{PK.KindWord(e.Kind)} {stage}" : PK.KindWord(e.Kind);
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
        var t = PT(L.F(run ? "{0} of the bosses' HP credited  ·  DPS and shares count credited damage" : "{0} of the boss's HP credited  ·  DPS and shares count credited damage", pct), 11.5, FontWeights.Medium, cov < 0.95 ? "Warn" : "Faint", trim: true);
        var tip = new List<string>();
        if (e.UncreditedBoss > 0)
            tip.Add(L.F(run ? "{0} of the {1} HP the bosses lost ({2}) came with no hit naming who dealt it, so it is in no member's figures." : "{0} of the {1} HP the boss lost ({2}) came with no hit naming who dealt it, so it is in no member's figures.", Amounts.Short(e.UncreditedBoss), Amounts.Short(e.BossDamage), Encounter.Pct(1 - cov)));
        else tip.Add(L.T(run ? "Every point of HP the bosses lost is in a member's figures." : "Every point of HP the boss lost is in a member's figures."));
        if (e.Uncredited > e.UncreditedBoss) tip.Add(L.F("Another {0} to other targets had no hit to name its source either.", Amounts.Short(e.Uncredited - e.UncreditedBoss)));
        var why = e.UncreditedWhy.Where(kv => kv.Key != 0 && kv.Value > 0).GroupBy(kv => Encounter.WhyWords(kv.Key))
            .Select(g => (Words: g.Key, Amount: g.Sum(kv => kv.Value))).OrderByDescending(x => x.Amount).Select(x => $"{x.Words} ({Amounts.Short(x.Amount)})").ToList();
        if (why.Count > 0) tip.Add(L.F("Why: {0}.", string.Join("; ", why)));
        t.ToolTip = string.Join(" ", tip);
        AutomationProperties.SetName(t, $"{pct} of {whose} credited");
        return t;
    }

    static TextBlock Tab(TextBlock t) { System.Windows.Documents.Typography.SetNumeralAlignment(t, FontNumeralAlignment.Tabular); return t; }



    /// <summary>The boss's HP as the window's own bar (QBar: the track, the fill with its gloss and glow), gold, coral under a third; it slides as the boss loses HP.</summary>
    FrameworkElement BossHpBar(Encounter e, double left)
    {
        var bar = QBar(left, left < 0.34 ? "Coral" : "Gold", 8, key: $"parser|hp|{e.Id}");
        bar.Children.Add(HudKit.HpTicks());   // a third and two thirds
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
        var rest = new System.Windows.Documents.Run(L.F(" · {0} left", Amounts.Short(Math.Max(0, e.BossHp)))) { FontWeight = FontWeights.SemiBold };
        rest.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "Text2");
        var of = new System.Windows.Documents.Run(L.F(" of {0} HP", Amounts.Short(e.BossMaxHp))) { FontWeight = FontWeights.Medium };
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
        bool kept = Encounter.Dungeon(where.Kind);
        // off for a reason of its own ("Off: ..."); before the game runs or connects, the page just waits as it would
        if (!_demo && _watcher.Packets is { } pr && !pr.On && pr.State.StartsWith("Off", StringComparison.Ordinal))
            return (L.T("The Parser is off"), L.T("It needs a setting turned on: Settings › Game reading."), "Warn");
        return kept ? (L.F("Ready in {0}", BossBook.Strip(where.Instance)), L.F("{0}. The next boss you pull starts a fight here; trash and adds never do.", PK.KindWord(Encounter.KindName(where.Kind))), "Good")
            : (L.T("Waiting for a boss"), L.T("Boss pulls are kept in Transcendence, Expeditions, Sanctuaries, Nightmare and Ascension Trials, and for world bosses. Enter one and pull a boss, or fight a world boss."), "Faint");
    }

    FrameworkElement ParserWaitingCard((ContentKind Kind, string Instance) where, List<Encounter> past)
    {
        var card = PCard(hero: false);
        card.Padding = new Thickness(14, 12, 14, 12);
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var art = GameArt.Element("dungeon", 30); art.Margin = new Thickness(0, 0, 12, 0); art.Opacity = 0.85;
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
            var lt = PT(L.T("Last pull"), 11, FontWeights.SemiBold, "Faint"); lt.HorizontalAlignment = HorizontalAlignment.Right;
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
        // boss pulls only: a run or a fight kept by an older version (Full run, Every fight) stays in fights.json, out of the list
        var past = all.Where(e => e.ModeKind == mode).ToList();
        // the head: the boss pulls kept and their count, the boss pick
        var head = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Tag = "parser-list-head",
            ToolTip = Tip(L.T("Every boss pull of this character, kept on this PC. Nothing about your fights leaves your PC unless you turn on Show my boss bests on my kinahledger.com profile (Options › Sharing): then only your own best kill on each boss goes to your profile, never your group's numbers.")) };
        title.Inlines.Add(new System.Windows.Documents.Run(L.T("Boss pulls")) { FontSize = 13, FontWeight = FontWeights.SemiBold });
        if (past.Count > 0) title.Inlines.Add(new System.Windows.Documents.Run("  " + past.Count.ToString("N0", CultureInfo.InvariantCulture)) { FontSize = 12.5, FontWeight = FontWeights.SemiBold });
        title.SetResourceReference(TextBlock.ForegroundProperty, "Ink");
        if (title.Inlines.Count > 1) ((System.Windows.Documents.Run)title.Inlines.LastInline).SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "Faint");
        AutomationProperties.SetName(title, "The kept boss pulls");
        head.Children.Add(title);
        var inKind = past.Where(e => _parserPrefs.KindFilter.Length == 0 || e.Kind == _parserPrefs.KindFilter).ToList();
        // a boss to look at alone
        Func<Encounter, string>? keyOf = e => e.Boss;
        string anyLabel = L.T("Any boss");
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
        AutomationProperties.SetName(bossBtn, "Filter by boss");
        bossBtn.ToolTip = Tip(L.T("Show one boss's pulls"));
        bossBtn.Click += (_, _) =>
        {
            if (keyOf == null) return;
            var entries = new List<MenuEntry> { new(anyLabel, () => { _parserPrefs.BossFilter = ""; ParserFilterChanged(); }, Sub: $"{inKind.Count} {Noun(mode, inKind.Count)}", On: _parserPrefs.BossFilter.Length == 0) };
            foreach (var g in inKind.GroupBy(keyOf, StringComparer.OrdinalIgnoreCase).OrderByDescending(g => g.Max(e => e.Start)))
            {
                var key = keyOf(g.First());
                int good = g.Count(e => e.Tone == "good");
                string sub = L.F(good == 1 ? "{0} · {1} {2}, {3} kill" : "{0} · {1} {2}, {3} kills", BossBook.Strip(g.First().Instance), g.Count(), Noun(mode, g.Count()), good);
                entries.Add(new MenuEntry(key, () => { _parserPrefs.BossFilter = key; ParserFilterChanged(); }, Sub: sub, On: _parserPrefs.BossFilter == key));
            }
            ShowMenu(bossBtn, entries);
        };
        if (inKind.Count == 0 || keyOf == null || !PFilters) bossBtn.Visibility = Visibility.Collapsed;
        Grid.SetColumn(bossBtn, 1); head.Children.Add(bossBtn);
        body.Children.Add(head);
        // the kinds the pulls are in (Transcendence, Expedition, Sanctuary, Nightmare, world bosses), when more than one
        var kindsHere = past.Select(e => e.Kind).Where(k => k.Length > 0).Distinct().OrderBy(k => k).ToList();
        if (kindsHere.Count > 1 && PFilters)
        {
            var kinds = new StackPanel { Orientation = Orientation.Horizontal };
            var seg = new Border { Padding = new Thickness(3), CornerRadius = Skins.Corner(10), BorderThickness = new Thickness(1), Child = kinds, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 8) };
            seg.SetResourceReference(Border.BackgroundProperty, "SegGroup"); seg.SetResourceReference(Border.BorderBrushProperty, "CardLine");
            foreach (var key in kindsHere.Prepend(""))
            {
                int n = key.Length == 0 ? past.Count : past.Count(e => e.Kind == key);
                var b = new Button { Content = $"{(key.Length == 0 ? L.T("All") : PK.KindWord(key))} {n}", Height = 24, FontSize = 12, Padding = new Thickness(9, 0, 9, 0), Margin = new Thickness(kinds.Children.Count > 0 ? 2 : 0, 0, 0, 0) };
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
            var empty = PT((past.Count > 0 ? L.F("No {0} of this kind yet.", Noun(mode, 2))
                : L.T("No boss pulls kept yet. Each boss you fight in a Transcendence, an Expedition, a Sanctuary, a Nightmare instance or an Ascension Trial, and each world boss, lands here, with its group and timings.")) + L.T(" They stay on this PC."), 12.5, FontWeights.Normal, "Faint");
            empty.TextWrapping = TextWrapping.Wrap; empty.Margin = new Thickness(0, 4, 0, 6);
            body.Children.Add(empty);
        }
        else
        {
            // a boss picked: its record on one line
            if (_parserPrefs.BossFilter.Length > 0 && keyOf != null)
            {
                var good = shown.Where(e => e.Tone == "good").ToList();
                var best = good.Count > 0 ? good.Min(e => e.Duration(e.End!.Value)) : (TimeSpan?)null;
                var rec = PT(L.F(good.Count == 1 ? "{0} {1} · {2} kill" : "{0} {1} · {2} kills", shown.Count, Noun(mode, shown.Count), good.Count) + (best is { } b2 ? L.F(" · best kill {0}", BossParser.Clock(b2)) : ""), 12, FontWeights.SemiBold, "Text2");
                rec.Margin = new Thickness(0, 0, 0, 6);
                body.Children.Add(rec);
            }
            int take = _parserAllRows ? shown.Count : Math.Min(shown.Count, ParserRowsAtRest);
            for (int i = 0; i < take; i++) body.Children.Add(HistoryRow(shown[i], i > 0, now));
            if (shown.Count > ParserRowsAtRest)
                body.Children.Add(FoldLink(_parserAllRows ? L.T("Fewer") : L.F("{0} more  ›", shown.Count - ParserRowsAtRest), () => { _parserAllRows = !_parserAllRows; RedrawParserPage(); }));
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
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(PullShareCol) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        var local = e.Start.ToLocalTime();
        var today = (_demo ? _parserDemoNow : DateTime.UtcNow).ToLocalTime().Date;
        var when = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        when.Children.Add(PT(local.ToString("HH:mm", CultureInfo.InvariantCulture), 12.5, FontWeights.SemiBold, "Text2"));
        when.Children.Add(PT(local.Date == today ? L.T("Today") : local.Date == today.AddDays(-1) ? L.T("Yesterday") : local.ToString("MMM d", CultureInfo.InvariantCulture), 11, FontWeights.Medium, "Faint"));
        g.Children.Add(when);
        var mid = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        var nameLine = new StackPanel { Orientation = Orientation.Horizontal };
        nameLine.Children.Add(PT(e.Title, 13, FontWeights.SemiBold, "Ink", trim: true));
        if (e.Partial) nameLine.Children.Add(Margin(PT(L.T("partial"), 11, FontWeights.SemiBold, "Warn"), 6, 1, 0, 0));
        // the group at a glance: each member's class emblem, small, in the same colours as the tab and the overlay
        var classes = e.Members.Where(f => ClassArt.Key(f.Class).Length > 0 && (!e.HasDamage || f.Damage > 0)).OrderByDescending(f => f.You).Take(8).ToList();
        if (classes.Count > 0)
        {
            var icons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            foreach (var f in classes) { var ic = ClassArt.Icon(f.Class, 15, f.Name); ic.Margin = new Thickness(0, 0, 2, 0); icons.Children.Add(ic); }
            nameLine.Children.Add(icons);
        }
        mid.Children.Add(nameLine);
        // a pull: its place and kind ("Nightmare" in Nightmare once, not twice)
        string sub = e.Place.Length == 0 || e.Place.Equals(e.Kind, StringComparison.OrdinalIgnoreCase) ? KindLabel(e) : $"{e.Place} · {KindLabel(e)}";
        mid.Children.Add(PT(sub, 11.5, FontWeights.Medium, "Muted", trim: true));
        Grid.SetColumn(mid, 1); g.Children.Add(mid);
        var oc = OutcomeChip(e); oc.HorizontalAlignment = HorizontalAlignment.Left;
        Grid.SetColumn(oc, 2); g.Children.Add(oc);
        var dur = Right(PT(BossParser.Clock(e.Duration(e.End ?? now)), 13, FontWeights.SemiBold, "Ink"));
        Grid.SetColumn(dur, 3); g.Children.Add(dur);
        var fig = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
        if (e.HasDamage) { fig.Children.Add(Right(PT(e.GroupDpsText(e.End ?? now), 13, FontWeights.SemiBold, "Text2"))); fig.Children.Add(Right(PT(L.T("group DPS"), 10.5, FontWeights.Medium, "Faint"))); }
        else if (PDetails) { fig.Children.Add(Right(PT(AvgCp(e), 13, FontWeights.SemiBold, "Text2"))); fig.Children.Add(Right(PT(L.T("average CP"), 10.5, FontWeights.Medium, "Faint"))); }
        Grid.SetColumn(fig, 4); g.Children.Add(fig);
        var chev = new System.Windows.Shapes.Path { Data = Geometry.Parse("M0,0 L3.5,3.5 L0,7"), StrokeThickness = 1.4, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
        chev.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "Faint");
        Grid.SetColumn(chev, 6); g.Children.Add(chev);
        var b = new Border { Child = g, Padding = new Thickness(6, 6, 6, 6), Margin = new Thickness(-6, 0, -6, 0), CornerRadius = new CornerRadius(7), BorderThickness = new Thickness(0, rule ? 1 : 0, 0, 0), Background = Brushes.Transparent, Cursor = Cursors.Hand, Focusable = true };
        b.SetResourceReference(Border.BorderBrushProperty, "CardHair");
        AutomationProperties.SetName(b, $"{e.Title}, {e.OutcomeLabel}, {BossParser.Clock(e.Duration(e.End ?? now))}");
        b.Tag = "parser-row";
        b.ToolTip = L.T("Open this boss pull: the group, the timings, the kills near you");
        if (!_parserPrefs.Hovers) b.ToolTip = null;
        // Share this pull: a small picture mark on a finished boss pull (Ui/MainWindow.PullShare.cs), brighter while the row is pointed at
        var share = PullShareButton(e);
        if (share != null) { Grid.SetColumn(share, 5); g.Children.Add(share); }
        b.MouseEnter += (_, _) => { b.SetResourceReference(Border.BackgroundProperty, "HoverTint"); if (share != null) share.Opacity = 1; };
        b.MouseLeave += (_, _) => { b.Background = Brushes.Transparent; if (share != null) share.Opacity = PullShareRest; };
        b.MouseLeftButtonUp += (_, _) => OpenFight(e.Id);
        b.KeyDown += (_, k) => { if (k.Key is Key.Enter or Key.Space) { k.Handled = true; OpenFight(e.Id); } };
        return b;
    }

    void OpenFight(string? id) { _parserOpen = id; if (id == null) _parserFromRuns = false; _parserInspect = null; RedrawParserPage(); LowerScroll.ScrollToTop(); }

    // ---- small parts ----

    static TextBlock PT(string text, double size, FontWeight weight, string brush, bool trim = false, bool display = false)
    {
        var t = QT(text, size, weight, brush, trim);
        if (display) { t.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont"); t.SetResourceReference(EffectProperty, "DisplayFx"); }
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
    /// A layout render's Parser page: "live" (a pull under way), "history" (between pulls), "detail" (one kept pull open),
    /// "nodamage", "nogroup" (damage before any group list), "empty". The sample keeps a few runs and fights of the old Full
    /// run and Every fight beside the pulls, as an older version left them: the list leaves them out.
    /// </summary>
    internal void ShowParserDemo(string state)
    {
        _demo = true;
        // the defaults (Group details on since the parser polish); "-all": every Parser option on (the PR's second render);
        // "-min": every option off
        bool everyOption = state.EndsWith("-all"), none = state.EndsWith("-min");
        if (everyOption || none) state = state[..^4];
        // the defaults since the honest pass: deaths, filters and hovers on; kills near you off
        _parserPrefs.KillsNear = !none;   // on for a new install since 1.0.18
        _killPrompt = null; KillCopyText.Visibility = Visibility.Collapsed;   // a state of its own: no kill just made
        _parserPrefs.Deaths = _parserPrefs.Filters = _parserPrefs.Hovers = !none;
        _parserPrefs.Details = !none;
        _parserPrefs.DpsActive = Encounter.ActiveBasis = false; _parserPrefs.Targets = "boss"; Encounter.BossOnly = true;
        // crit is off as shipped; a render can show it on with KL_CRIT=result3 or mark08 (a render's switch only, never the app's)
        Encounter.CritKey = Encounter.CritKeyOf(Environment.GetEnvironmentVariable("KL_CRIT") ?? _parserPrefs.CritNow);
        Encounter.BackKey = Encounter.BackKeyOf(Environment.GetEnvironmentVariable("KL_BACK") ?? _parserPrefs.BackNow);
        _parserDemoState = state;
        _parserDemoNow = new DateTime(2026, 11, 13, 21, 30, 0, DateTimeKind.Utc);
        // "fight": an Every fight record under way on the Graphs tab, which a fight that isn't a boss pull doesn't have (its group's table instead)
        var mode = state == "fight" ? ParserMode.Fights : ParserMode.Bosses;
        bool live = state is "fight" or "live" or "nodamage" or "nogroup" or "inspect" or "compare" or "timeline" or "taken" or "full" or "inspect-full";
        // the kept records of every mode an older version kept (the list shows the boss pulls only); the pull under way
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
        // the compare's render: casts and hit kinds on every pull, so its Casts and Crit columns can be seen (render states only)
        if (state == "compare") foreach (var x in all.Append(now)) if (x != null) ParserDemoCasts(x);
        // the Timeline tab's render: the kept pulls with your lanes (render states only); KL_TIMELINE_PULLS=records.jsonl (from
        // parser-replay --save) renders real pulls instead, KL_TIMELINE_PICK=n the n-th of them (else the one with most deaths)
        string? trackOpen = null;
        if (state == "track")
        {
            if (Environment.GetEnvironmentVariable("KL_TIMELINE_PULLS") is { Length: > 0 } tp && File.Exists(tp))
            {
                var real = File.ReadAllLines(tp).Where(l => l.Trim().StartsWith('{')).Select(l => System.Text.Json.JsonSerializer.Deserialize<Encounter>(l)!).Where(PullTimeline.Has).ToList();
                if (real.Count > 0)
                {
                    all = real; now = null;
                    int pick = int.TryParse(Environment.GetEnvironmentVariable("KL_TIMELINE_PICK"), out var pk) ? Math.Clamp(pk, 0, real.Count - 1) : -1;
                    trackOpen = (pick >= 0 ? real[pick] : real.OrderByDescending(e => e.Members.Sum(f => f.Deaths)).ThenByDescending(e => e.Duration(e.End ?? e.LastAt)).First()).Id;
                }
            }
            if (trackOpen == null)
            {
                foreach (var x in all) if (x.ModeKind == ParserMode.Bosses && !x.Live) { ParserDemoCasts(x); ParserDemoTimeline(x); }
                trackOpen = all.LastOrDefault(e => PullTimeline.Has(e) && e.Roster.Any(f => f.Deaths > 0))?.Id ?? all.LastOrDefault(PullTimeline.Has)?.Id;
            }
        }
        _parserDemoPast = all.OrderBy(e => e.Start).ToList(); _parserDemoLive = now;
        _parserDemoWhere = state == "empty" ? (ContentKind.Other, "") : (ContentKind.Expedition, "Ferocious Horn Den");
        _parserPrefs.Mode = BossParser.ModeKey(mode); _parserPrefs.ListMode = "";
        _parserOpen = state switch
        {
            "detail" => all.LastOrDefault(e => e.ModeKind == ParserMode.Bosses && e.Kills.Any(k => !k.Boss) && e.Roster.Any(f => f.Deaths > 0))?.Id,
            "track" => trackOpen,
            _ => null,
        };
        _parserPrefs.KindFilter = ""; _parserPrefs.BossFilter = "";
        // Parser v2's views: the tab, the member opened, and for the renders of what the record will carry (the timeline, damage
        // taken, crits), a sample of those figures (ParserViewSample: render states only, the app never sets it)
        _parserTab = state switch { "compare" => "Compare", "timeline" or "fight" => "Timeline", "track" => "Track", "taken" => "Taken", _ => "Group" };
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

    /// <summary>
    /// For the options test: the meter's held result on <paramref name="parser"/> (Bosses, inside an off-screen Kinah overlay,
    /// nothing on any screen): one tick while the pull is under way, <paramref name="end"/> ends it, a tick, then
    /// <paramref name="late"/> brings a heal of its last moment and a tick again. What the held result said for
    /// <paramref name="member"/>'s healing after the end and after the late heal, and whether it is still that pull.
    /// </summary>
    internal (long AtEnd, long AfterLate, bool SamePull) HeldHealForTest(HudWindow hud, BossParser parser, long member, Action end, Action late)
    {
        var (hud0, demo0, src0, force0, prefs0) = (_hud, _demo, ParserSourceForTest, ParserOverlayForceForTest, (_parserPrefs.AlwaysOn, _parserPrefs.Overlay, _parserPrefs.InKinah, _parserPrefs.Separate));
        try
        {
            _hud = hud; _demo = false; ParserSourceForTest = parser; ParserOverlayForceForTest = false;
            _parserPrefs.AlwaysOn = false; _parserPrefs.Overlay = true; _parserPrefs.InKinah = true; _parserPrefs.Separate = false;
            _parserLastLiveId = ""; _parserJustEnded = null; _parserLateHeals = parser.LateHeals;
            var hudPreview0 = _hudPreview; bool hudOn0 = _prefs.Hud;
            _hudPreview = null; _prefs.Hud = true;
            try
            {
                ParserOverlayTick();
                string id = _parserLastLiveId;
                end(); ParserOverlayTick();
                long atEnd = _parserJustEnded?.Members.FirstOrDefault(f => f.Id == member)?.Healing ?? -1;
                late(); ParserOverlayTick();
                long after = _parserJustEnded?.Members.FirstOrDefault(f => f.Id == member)?.Healing ?? -1;
                return (atEnd, after, id.Length > 0 && _parserJustEnded?.Id == id);
            }
            finally { _hudPreview = hudPreview0; _prefs.Hud = hudOn0; }
        }
        finally
        {
            hud.SetKinahHidden(false); hud.ShowParser(null, DateTime.UtcNow, ParserModeNow, PDetails);
            (_hud, _demo, ParserSourceForTest, ParserOverlayForceForTest) = (hud0, demo0, src0, force0);
            (_parserPrefs.AlwaysOn, _parserPrefs.Overlay, _parserPrefs.InKinah, _parserPrefs.Separate) = prefs0;
            _parserLastLiveId = ""; _parserJustEnded = null;
            _parserOverlay?.ShowWanted(false);
        }
    }

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
    /// <summary>For a render and the options test: the sample's newest wipe as the last pull (the one the live view holds).</summary>
    internal void HoldWipeForTest()
    {
        if (_parserDemoPast?.LastOrDefault(e => e.ModeKind == ParserMode.Bosses && e.Outcome == "wipe") is not { } wipe) return;
        _parserDemoPast.Remove(wipe); _parserDemoPast.Add(wipe);
        RedrawParserPage();
    }
    /// <summary>For a render: the demo's fight <paramref name="id"/> as the last pull, held in the live view (no fight under way), in the instance or out of it.</summary>
    internal void HoldFightForTest(string id, bool inInstance)
    {
        if (_parserDemoPast?.FirstOrDefault(e => e.Id == id) is not { } e) return;
        _parserDemoPast.Remove(e); _parserDemoPast.Add(e);
        _parserDemoLive = null; _parserOpen = null;
        _parserDemoWhere = inInstance ? (ContentKind.Expedition, "Ferocious Horn Den") : (ContentKind.Other, "");
        RedrawParserPage();
    }
    /// <summary>For the options test: the fight the live view holds now (null: none, or one under way shown).</summary>
    internal string? ParserHeldForTest => _parserOpen == null && ParserHeldFight(ParserPast()) is { } h && PageLive(ParserLive, h) == null ? h.Id : null;
    internal List<Encounter> ParserPastForTest => ParserPast();
    internal string? ParserOpenForTest => _parserOpen;
    internal ParserOverlay? ParserOverlayForTest => _parserOverlay;
    internal void SetParserOverlayForTest(bool on) => SetParserOverlay(on);
    internal void SetParserSeparateForTest(bool on) { _parserPrefs.Separate = on; DrawOverlayPills(); RedrawParserPage(); }

    /// <summary>A Parser window look's name: "Standard" for its own panel, else the fight look's (Auto, Capsule, Meter, Float).</summary>
    internal static string ParserLookName(string? key)
    {
        var k = ParserPrefs.LookOf(key);
        return k switch { ParserPrefs.StandardLook => "Forge", ParserPrefs.PodiumLook => "Podium", ParserPrefs.ReactorLook => "Reactor", _ => HudLooks.All.First(l => l.Key == k).Name };
    }

    /// <summary>A Parser panel look's line in Settings (English, through L.T); null for a fight look (its own Hint).</summary>
    internal static string? PanelLookHint(string? key) => key switch
    {
        ParserPrefs.StandardLook => "The Parser's own panel: every member one thick lit bar, the words on it.",
        ParserPrefs.PodiumLook => "The leader on a lit card; everyone else a name over a long glowing rail.",
        ParserPrefs.ReactorLook => "An instrument panel: tracks lit cell by cell, the figures in inset readouts.",
        _ => null,
    };

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
