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
    bool HudLocked = true, double HudOpacity = 0.9, double HudScale = 1);

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
        if (_hotkeySuspended || Hotkey.Parse(_prefs.Hotkey) is not { } key) return null;
        return Native.RegisterHotKey(h, HotkeyId, key.NativeModifiers, key.VirtualKey)
            ? null
            : $"{key} is already used by another app, so the shortcut is off. Pick another in Options › In game.";
    }

    /// <summary>While Options records a new shortcut, the current one must not fire (it would start a run).</summary>
    public void SuspendHotkey(bool suspend)
    {
        _hotkeySuspended = suspend;
        ApplyHotkey();
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
    public Settings AppSettings => _settings;
    public bool CanStartWithWindows => _exe != null;

    /// <summary>What Options opens with: the saved choices, and the startup entry as Windows has it now.</summary>
    public OptionsChoice CurrentOptions() => new(
        RegionKey, Server?.Label ?? "", _prefs.Share, _prefs.Hud, HotkeyText, Skins.Get(_prefs.Skin).Key, _prefs.AutoUpdate,
        AutoStart.IsOn(), _prefs.OnGameStart == "tray" ? "tray" : "open", _prefs.HideWhenGameCloses, _prefs.CloseToTray, _prefs.MinimizeToTray, Ui.HudFields.Join(Ui.HudFields.Parse(_prefs.HudFields)),
        _prefs.ReportFrames, _prefs.SendReportFrames, _prefs.GearScore, _prefs.HelpImproveCounting, _prefs.ShowAccuracy,
        _hudPrefs.Locked, Math.Round(Math.Clamp(_prefs.HudOpacity, 0.3, 1), 2), Math.Round(Math.Clamp(_hudPrefs.Scale, HudPrefs.MinScale, HudPrefs.MaxScale), 2));

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
        _prefs.HudOpacity = c.HudOpacity; _hudPrefs.Scale = c.HudScale;
        PreviewHud(c.HudOpacity, c.HudScale);
        if (c.Skin != Skins.Get(_prefs.Skin).Key) { _prefs.Skin = c.Skin; }
        ApplySkin(_prefs.Skin);
        if (c.AutoUpdate != was.AutoUpdate) AutoUpdateChanged(c.AutoUpdate);
        if (c.Hotkey != was.Hotkey)
        {
            _prefs.Hotkey = c.Hotkey;
            if (ApplyHotkey() is { } err) problems.Add(err);
        }
        _prefs.OnGameStart = c.OnGameStart;
        _prefs.HideWhenGameCloses = c.HideWhenGameCloses;
        _prefs.CloseToTray = c.CloseToTray;
        _prefs.MinimizeToTray = c.MinimizeToTray;
        _prefs.ReportFrames = ReportBuffer.KeepFrames = c.ReportFrames;
        if (c.GearScore != was.GearScore) SetGearScore(c.GearScore);
        _prefs.SendReportFrames = ReportBuffer.KeepUiFrames = c.SendFrames;
        _prefs.HelpImproveCounting = c.HelpCounting; ApplyCounting();   // with sharing too: off with either
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
        _prefs.HudX = null; _prefs.HudY = null;
        if (!_demo) _prefs.Save();
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
