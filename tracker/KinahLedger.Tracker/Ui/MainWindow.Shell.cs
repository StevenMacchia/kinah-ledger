using System.Windows;
using System.Windows.Interop;
using KinahLedger.Platform;
using KinahLedger.Session;
using KinahLedger.Update;
using WF = System.Windows.Forms;

namespace KinahLedger.Ui;

/// <summary>Everything Options can change, as one value: what the window shows when it opens, and what Save hands back.</summary>
public sealed record OptionsChoice(
    string Region, string Server, bool Share, bool Hud, string Hotkey, string Skin, bool AutoUpdate,
    bool StartWithWindows, string OnGameStart, bool HideWhenGameCloses, bool CloseToTray, bool MinimizeToTray, string HudFields,
    bool ReportFrames = false, bool SendFrames = false, int GearScore = 0, bool HelpCounting = true, bool ShowAccuracy = false,
    bool HudLocked = true, double HudOpacity = 0.9, double HudScale = 1, string HudLook = HudLooks.Strip)
{
    /// <summary>Options › In game › Rare drops (MainWindow.RareWatch.cs): the watch list, one name a line, and the chime.</summary>
    public string RareWatch { get; init; } = "";
    public bool RareChime { get; init; } = true;
    public double RareVolume { get; init; } = 0.6;
    /// <summary>The That was wrong shortcut ("Ctrl+Shift+J"), "" when off (Ui/MainWindow.MarkWrong.cs).</summary>
    public string MarkWrongKey { get; init; } = "";
}

/// <summary>
/// The app around the live count: the tray, starting with Windows, reacting to the game starting and closing, the
/// start/stop shortcut, and the bridge to the Options window (which edits a copy and hands it back on Save).
/// </summary>
public partial class MainWindow
{
    bool _gameWasRunning, _gameBalloonShown;
    DateTime _gameGoneAt = DateTime.MaxValue;
    string _demoMarket = "";
    bool _hotkeySuspended;

    /* ---------------- starting hidden, the tray ---------------- */

    /// <summary>
    /// A start from Windows sign-in (--background): the window exists (so the shortcut works) but stays in the tray;
    /// it comes up when the game starts, or from the tray icon.
    /// </summary>
    public void StartInTray()
    {
        new WindowInteropHelper(this).EnsureHandle();   // SourceInitialized: dark title bar, shortcut
    }

    /// <summary>Hides to the tray, saying where it went the first time.</summary>
    void HideToTray()
    {
        Hide();
        if (_trayHintShown) return;
        _trayHintShown = true;
        _tray.ShowBalloonTip(4000, "Kinah Ledger is still running",
            "It's in the tray by the clock and opens when Aion 2 starts. Right-click the icon to quit.", WF.ToolTipIcon.Info);
    }

    /// <summary>Brings the window up without taking focus from the game that is starting.</summary>
    void ShowQuietly()
    {
        ShowActivated = false;
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        ShowActivated = true;
    }

    /// <summary>Another start of the exe asked for the window (the tracker was already running).</summary>
    public void ShowFromOtherStart() => Dispatcher.BeginInvoke(Restore);

    /// <summary>
    /// The game starting or closing, from the watcher's process check (every few seconds). Starting: the window opens
    /// (or a tray note says reading has begun). Closed for ten seconds: the window hides, if asked to and no run is on.
    /// </summary>
    void WatchGame()
    {
        bool running = _watcher.GameRunning;
        if (running && !_gameWasRunning) OnGameStarted();
        if (running) _gameGoneAt = DateTime.MaxValue;
        else if (_gameWasRunning) _gameGoneAt = DateTime.UtcNow;
        else if (_gameGoneAt != DateTime.MaxValue && DateTime.UtcNow - _gameGoneAt > TimeSpan.FromSeconds(10)) { _gameGoneAt = DateTime.MaxValue; OnGameClosed(); }
        _gameWasRunning = running;
    }

    void OnGameStarted()
    {
        if (IsVisible) return;
        if (_prefs.OnGameStart == "open") { ShowQuietly(); return; }
        if (_gameBalloonShown) return;
        _gameBalloonShown = true;
        _tray.ShowBalloonTip(4000, "Aion 2 is running", "Kinah Ledger is reading the chat from the tray. Double-click the icon to open it.", WF.ToolTipIcon.Info);
    }

    void OnGameClosed()
    {
        // the game is gone: the run so far is saved and shared like Stop, and counting starts fresh next time
        _ = StartFresh("Aion 2 closed");
        if (!_prefs.HideWhenGameCloses || Farming || !IsVisible || _options != null) return;
        HideToTray();
    }

    bool _startingFresh;

    /// <summary>
    /// Logging off and another character (Steven, 2026-10-23: "Close the run, start fresh"): a farming run in progress
    /// stops and is saved and shared as one run, as Stop does; the always-on count is shared and starts over, the crafting
    /// part too; counting goes on from here for whoever is in the world next.
    /// </summary>
    async Task StartFresh(string why)
    {
        if (_startingFresh || _demo) return;
        _startingFresh = true;
        try
        {
            CrashLog.Note($"starting fresh: {why}");
            bool had = Farming || (_watcher.Chat?.Engine.Events ?? 0) > 0;
            if (Farming) await StopTracking();
            await _watcher.ClearFarmingAsync();
            _watcher.Chat?.ResetCraftView();
            _lastRun = null;
            Redraw();
            if (had) Say($"{why}: the run so far is saved, and counting starts fresh.", "Faint");
        }
        catch (Exception ex) { CrashLog.Write("start fresh", ex); }
        finally { _startingFresh = false; }
    }

    /* ---------------- the start/stop shortcut ---------------- */

    /// <summary>The shortcut as the player sees it ("Ctrl+Shift+K"); empty when it is off.</summary>
    public string HotkeyText => Hotkey.Parse(_prefs.Hotkey)?.ToString() ?? "";

    /// <summary>Registers the shortcut from the prefs. A message when Windows refused it (another app has it), else null.</summary>
    string? ApplyHotkey()
    {
        var h = new WindowInteropHelper(this).Handle;
        if (h == IntPtr.Zero) return null;
        Native.UnregisterHotKey(h, HotkeyId);
        if (_hotkeySuspended) return null;
        // one saved before the shortcut rules (Ctrl+K, say) is off, and says why (Platform/Hotkey.cs Refusal)
        if (Hotkey.Explain(_prefs.Hotkey) is { } why)
        {
            _hotkeyProblem = ($"The shortcut {_prefs.Hotkey.Trim()} is off: {char.ToLowerInvariant(why[0])}{why[1..]} Pick another in Options › In game.", $"Shortcut {_prefs.Hotkey.Trim()} is off · pick another in Options");
            return _hotkeyProblem.Value.Long;
        }
        if (Hotkey.Parse(_prefs.Hotkey) is not { } key) { _hotkeyProblem = null; return null; }
        _hotkeyProblem = Native.RegisterHotKey(h, HotkeyId, key.NativeModifiers, key.VirtualKey) ? null
            : ($"{key} is already used by another app, so the shortcut is off. Pick another in Options › In game.", $"{key} is taken by another app · pick another in Options");
        return _hotkeyProblem?.Long;
    }

    /// <summary>The shortcut couldn't be set: the status line's sentence and the panel's short line; null when it works or is off.</summary>
    (string Long, string Short)? _hotkeyProblem;

    /// <summary>While Options records a new shortcut, the current one must not fire (it would start a run).</summary>
    public void SuspendHotkey(bool suspend)
    {
        _hotkeySuspended = suspend;
        ApplyHotkey();
        ApplyMarkWrongKey();   // the That was wrong key too: neither fires while a key is being recorded
    }

    /* ---------------- Options ---------------- */

    public void OpenOptions(OptionsWindow.Page? page = null)
    {
        if (_welcome) { Restore(); return; }   // the server comes first; the welcome card asks for it
        if (!IsVisible) Restore();
        if (_options != null) { if (page is { } p) _options.Go(p); _options.Activate(); return; }
        _options = new OptionsWindow(this, page ?? OptionsWindow.Page.General) { Owner = this };
        _options.Closed += (_, _) => { _options = null; SuspendHotkey(false); };
        _options.Show();
    }

    public UserPrefs Prefs => _prefs;

    /// <summary>
    /// The panel's opacity and size while Options is open: shown on the panel at once, remembered only on Save (Cancel
    /// shows the saved ones again), like every other setting there.
    /// </summary>
    public void PreviewHud(double opacity, double scale)
    {
        _hud?.SetOpacity(opacity);
        _hud?.SetScale(scale);
    }

    /// <summary>
    /// The rest of Options › In game the same way (2026-10-25 audit: the panel switch, its figures and its lock waited for
    /// Save with nothing to see): the edited choices shown on the panel at once, remembered only on Save; Cancel puts the
    /// saved ones back (<see cref="EndHudPreview"/>). Null while Options isn't previewing anything.
    /// </summary>
    OptionsChoice? _hudPreview;
    /// <summary>Reset position being tried: the panel sits at the default spot until Save keeps it or Cancel moves it back.</summary>
    bool _hudSpotPreview;
    /// <summary>The panel's switch as it shows now: the one being tried in Options, else the saved one.</summary>
    bool HudWanted => _hudPreview?.Hud ?? _prefs.Hud;

    /// <summary>Options › In game changed: the panel shows the edited switch, figures, lock, opacity and size now.</summary>
    public void PreviewHudLook(OptionsChoice c)
    {
        _hudPreview = c;
        if (!c.Hud) CloseHud();
        else if (_hud == null && !_demo && _watcher.Chat != null) EnsureHud();   // on: it shows over the game as it would after Save
        _hud?.SetFields(Ui.HudFields.Parse(c.HudFields));
        PreviewHud(c.HudOpacity, c.HudScale);
        UpdateHud();
    }

    /// <summary>Options › In game › Reset position, being tried: the panel moves to the default spot now; Save keeps it there.</summary>
    public void PreviewHudSpot()
    {
        _hudSpotPreview = true;
        PlaceHudDefault();
    }

    /// <summary>Options closed (after Save or Cancel): the panel back to the saved choices, which Save has just made the edited ones.</summary>
    public void EndHudPreview()
    {
        bool moved = _hudSpotPreview;
        _hudPreview = null; _hudSpotPreview = false;
        if (!_prefs.Hud) { CloseHud(); return; }
        if (_hud == null) return;   // the next tick opens it, run or no run
        _hud.SetFields(Ui.HudFields.Parse(_prefs.HudFields));
        PreviewHud(_prefs.HudOpacity, _hudPrefs.Scale);
        if (moved) { if (SavedHudSpot() is { } spot) { _hud.Left = spot.X; _hud.Top = spot.Y; } else PlaceHudDefault(); }
        UpdateHud();
    }

    /// <summary>For the options self-test: the panel's switch as it shows now (the one being tried, or the saved one).</summary>
    internal bool HudShownForTest => HudWanted;
    /// <summary>For the options self-test: the panel's figures as it shows them now.</summary>
    internal string HudFieldsShownForTest => _hudPreview?.HudFields ?? _prefs.HudFields;
    public Settings AppSettings => _settings;
    public bool CanStartWithWindows => _exe != null;

    /// <summary>What Options opens with: the saved choices, and the startup entry as Windows has it now.</summary>
    public OptionsChoice CurrentOptions() => new(
        RegionKey, Server?.Label ?? "", _prefs.Share, _prefs.Hud, HotkeyText, Skins.Get(_prefs.Skin).Key, _prefs.AutoUpdate,
        AutoStart.IsOn(), _prefs.OnGameStart == "tray" ? "tray" : "open", _prefs.HideWhenGameCloses, _prefs.CloseToTray, _prefs.MinimizeToTray, Ui.HudFields.Join(Ui.HudFields.Parse(_prefs.HudFields)),
        _prefs.ReportFrames, _prefs.SendReportFrames, _prefs.GearScore, _prefs.HelpImproveCounting, _prefs.ShowAccuracy,
        _hudPrefs.Locked, Math.Round(Math.Clamp(_prefs.HudOpacity, 0.3, 1), 2), Math.Round(Math.Clamp(_hudPrefs.Scale, HudPrefs.MinScale, HudPrefs.MaxScale), 2), HudLook)
        { RareWatch = WatchText(), RareChime = _hudPrefs.Chime, RareVolume = Math.Round(Math.Clamp(_hudPrefs.ChimeVolume, 0, 1), 2), MarkWrongKey = MarkWrongText };

    /// <summary>Save in Options: every change takes effect now and is remembered. A message for anything Windows refused, else null.</summary>
    public string? ApplyOptions(OptionsChoice c)
    {
        var problems = new List<string>();
        var was = CurrentOptions();

        if (c.Region != was.Region || c.Server != was.Server)
        {
            ApplyServer(c.Region, c.Server);
            if (Server is { } s && !Farming) Say($"Server set to {s.Label} · {s.Region.Name}.", "Good");
        }
        if (c.Share != was.Share) { _prefs.Share = c.Share; MarketSwitch(c.Share); }
        if (c.Hud != was.Hud)
        {
            _prefs.Hud = c.Hud;
            if (!c.Hud) CloseHud();   // off: the panel goes now; on: the next tick shows it, run or no run
        }
        if (c.HudFields != was.HudFields)
        {
            _prefs.HudFields = c.HudFields;
            _hud?.SetFields(Ui.HudFields.Parse(c.HudFields));
            UpdateHud();
        }
        if (c.HudLocked != was.HudLocked) { _hudPrefs.Locked = c.HudLocked; UpdateHud(); }
        if (c.HudLook != was.HudLook) ApplyHudLook(c.HudLook);   // the panel is made again in the new look
        _prefs.HudOpacity = c.HudOpacity; _hudPrefs.Scale = c.HudScale;
        PreviewHud(c.HudOpacity, c.HudScale);
        if (c.Skin != Skins.Get(_prefs.Skin).Key) { _prefs.Skin = c.Skin; }
        ApplySkin(_prefs.Skin);
        if (c.AutoUpdate != was.AutoUpdate) AutoUpdateChanged(c.AutoUpdate);
        if (c.Hotkey != was.Hotkey)
        {
            _prefs.Hotkey = c.Hotkey;
            ReleaseMarkWrongKey();   // two keys swapped: the start/stop one first, then That was wrong again below
            if (ApplyHotkey() is { } err) problems.Add(err);
        }
        if (c.MarkWrongKey != was.MarkWrongKey || c.Hotkey != was.Hotkey)
        {
            _hudPrefs.MarkWrongKey = c.MarkWrongKey;
            if (ApplyMarkWrongKey() is { } err) problems.Add(err);
        }
        _prefs.OnGameStart = c.OnGameStart;
        _prefs.HideWhenGameCloses = c.HideWhenGameCloses;
        _prefs.CloseToTray = c.CloseToTray;
        _prefs.MinimizeToTray = c.MinimizeToTray;
        _prefs.ReportFrames = ReportBuffer.KeepFrames = c.ReportFrames;
        if (c.GearScore != was.GearScore) SetGearScore(c.GearScore);
        _prefs.SendReportFrames = ReportBuffer.KeepUiFrames = c.SendFrames;
        _prefs.HelpImproveCounting = c.HelpCounting; ApplyCounting();   // with sharing too: off with either
        ApplyRareDrops(c, was);   // the watch list and the chime (Ui/MainWindow.RareWatch.cs)
        if (c.ShowAccuracy != was.ShowAccuracy) { _prefs.ShowAccuracy = c.ShowAccuracy; _accChipSig = _accSig = ""; FillAccuracyCard(); UpdateHud(); }
        if (c.StartWithWindows != was.StartWithWindows)
        {
            if (_exe == null) problems.Add("Starting with Windows needs the downloaded KinahLedger.exe.");
            else if (!AutoStart.Set(c.StartWithWindows, _exe, out var err)) problems.Add("Windows didn't accept the startup setting (" + err + ").");
        }
        SavePrefs();
        // the new shortcut and server show in the live lines straight away
        ServerRowState();
        return problems.Count > 0 ? string.Join(" ", problems) : null;
    }

    /* ---------------- sharing and data, for Options ---------------- */

    public string LinkCode()
    {
        try { return Upload.InstallId.LinkCode(); } catch (Exception) { return "unavailable"; }
    }

    /// <summary>Opens the site with this PC's link code; signed in, the site links it at once.</summary>
    public void LinkProfile()
    {
        var code = LinkCode();
        if (!code.StartsWith("KL-")) { Report("No link code yet; try again in a moment.", "Warn"); return; }
        Open($"{SiteUrl}/?link={Uri.EscapeDataString(code)}#profile");
        Report("Opened kinahledger.com in your browser. Sign in with Discord if it asks; this PC is then linked to your profile.", "Good");
    }

    public void ResetInstallId()
    {
        Upload.InstallId.Reset();
        Report("Install ID replaced. Runs from now on can't be tied to earlier ones; link the new code to keep them on your profile.", "Good");
    }

    /// <summary>Options › In game › Reset position: back where a first start puts it (PlaceHudDefault).</summary>
    public void ResetHudPosition()
    {
        // the look's own spot: the Ticker's is kept apart from the strip's and the Card's
        if (HudLook == HudLooks.Ticker) { _hudPrefs.TickerX = null; _hudPrefs.TickerY = null; if (!_demo) _hudPrefs.Save(); }
        else { _prefs.HudX = null; _prefs.HudY = null; if (!_demo) _prefs.Save(); }
        _hudSpotPreview = false;
        PlaceHudDefault();
    }

    public static string DataFolder => CrashLog.Dir;
}

/// <summary>The tray menu in the window's colours instead of the default light grey.</summary>
sealed class TrayMenuRenderer : WF.ToolStripProfessionalRenderer
{
    static readonly System.Drawing.Color Bg = System.Drawing.Color.FromArgb(17, 26, 40), Hover = System.Drawing.Color.FromArgb(30, 45, 69),
        Line = System.Drawing.Color.FromArgb(35, 47, 69), Ink = System.Drawing.Color.FromArgb(233, 239, 247);

    public TrayMenuRenderer() : base(new Colors()) { RoundedEdges = false; }

    protected override void OnRenderItemText(WF.ToolStripItemTextRenderEventArgs e) { e.TextColor = Ink; base.OnRenderItemText(e); }

    sealed class Colors : WF.ProfessionalColorTable
    {
        public override System.Drawing.Color ToolStripDropDownBackground => Bg;
        public override System.Drawing.Color MenuBorder => Line;
        public override System.Drawing.Color MenuItemBorder => Hover;
        public override System.Drawing.Color MenuItemSelected => Hover;
        public override System.Drawing.Color SeparatorDark => Line;
        public override System.Drawing.Color SeparatorLight => Bg;
        public override System.Drawing.Color ImageMarginGradientBegin => Bg;
        public override System.Drawing.Color ImageMarginGradientMiddle => Bg;
        public override System.Drawing.Color ImageMarginGradientEnd => Bg;
    }
}
