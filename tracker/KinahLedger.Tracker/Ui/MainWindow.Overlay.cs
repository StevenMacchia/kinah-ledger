using System.Windows;
using KinahLedger.Ocr;

namespace KinahLedger.Ui;

/// <summary>
/// The panel over the game behaving like a game overlay: what it knows about the game being in front, from the facts the
/// watcher already has (GameWatcher.GameRect, GameWindow.InFront). Nothing here reads the screen.
/// </summary>
public partial class MainWindow
{
    /// <summary>
    /// True while the game is the window the player is using: the watcher saw its window in the last few seconds (not
    /// closed, not minimized) and it has focus. Cheap while the game runs; false at once when it doesn't.
    /// </summary>
    bool GameInFront() => !_demo && _watcher.GameRect != null && GameWindow.InFront(_settings.ProcessHints);

    /// <summary>Tells the panel at once when another window comes to the front (Native.WatchForeground).</summary>
    IDisposable? _foregroundWatch;

    /// <summary>
    /// How the panel shows now, as its "show" value: "" over the game; "dim" (the paused look) while the chat reader is
    /// paused or the chat is covered; "hidden" when the game isn't in front, is minimized or has closed. The tracker's own
    /// windows count as the game, so the window and Options can be used with the panel in view.
    /// </summary>
    string HudPresence()
    {
        if (_guide != null) return "hidden";   // the placement guide draws it in its outline meanwhile
        if (_demo || HudMoving) return "";   // being moved from the tray: in view wherever the player is
        if (_watcher.GameRect == null) return "hidden";
        if (Native.ForegroundPid() != (uint)Environment.ProcessId && !GameWindow.InFront(_settings.ProcessHints)) return "hidden";
        var chat = _watcher.Chat;
        return chat == null || chat.Paused.Length > 0 ? "dim" : "";
    }

    /// <summary>Until when the tray's Move the panel keeps a locked panel unlocked and in view.</summary>
    DateTime _hudMoveUntil = DateTime.MinValue;
    const int HudMoveSeconds = 20;
    bool HudMoving => DateTime.UtcNow < _hudMoveUntil;
    /// <summary>The saved lock, for the options self-test.</summary>
    public bool HudLocked => _hudPrefs.Locked;
    /// <summary>The saved panel size, for the options self-test.</summary>
    public double HudScale => _hudPrefs.Scale;

    /// <summary>
    /// The tray's Move the panel: unlocked and in view for 20 seconds, whether the game is in front or not, then locked again
    /// where it was left (the window's tick puts the lock back).
    /// </summary>
    void MoveHudForAWhile()
    {
        if (!_prefs.Hud) { _tray.ShowBalloonTip(4000, "The panel is off", "Turn it on in Options › In game, then move it from here.", System.Windows.Forms.ToolTipIcon.Info); return; }
        EnsureHud();
        _hudMoveUntil = DateTime.UtcNow.AddSeconds(HudMoveSeconds);
        UpdateHud();
        Say($"Drag the panel where you want it. It locks again in {HudMoveSeconds} seconds.", "Faint");
    }

    /// <summary>A short line on the panel ("Run started"), shown for <see cref="HudFlashSeconds"/> through its "flash" value.</summary>
    string _hudFlash = "";
    DateTime _hudFlashUntil = DateTime.MinValue;
    const int HudFlashSeconds = 3;

    /// <summary>Confirms a start or stop on the panel, so the shortcut pressed in the game shows it took.</summary>
    void FlashHud(string text)
    {
        _hudFlash = text; _hudFlashUntil = DateTime.UtcNow.AddSeconds(HudFlashSeconds);
        UpdateHud();
    }

    /// <summary>
    /// The panel's run state: "run" (its dot red only during a run), the timer as "RUN 12:31" during a run and the
    /// session clock otherwise (FillNumbers), and the flash while it lasts (the window's tick takes it off).
    /// </summary>
    void HudRunValues()
    {
        _hudValues["run"] = RunOn ? "1" : "";   // off the moment the window's Stop is pressed, though Undo can still bring it back
        if (RunEngine is { } run) _hudValues["time"] = "RUN " + Clock(run.Active);
        else if (_hudValues.GetValueOrDefault("time", "").StartsWith("RUN ")) _hudValues["time"] = "";   // the next count puts the clock back
        _hudValues["flash"] = DateTime.UtcNow < _hudFlashUntil ? _hudFlash : "";
    }

    /// <summary>
    /// The panel's first place, and Reset position's: the top centre of the monitor the game is on (the watcher's
    /// GameRect), or of the main monitor while the game hasn't been seen.
    /// </summary>
    void PlaceHudDefault()
    {
        if (_hud == null) return;
        var area = _watcher.GameRect is { } g ? System.Windows.Forms.Screen.FromRectangle(g).WorkingArea
            : System.Windows.Forms.Screen.PrimaryScreen?.WorkingArea ?? new System.Drawing.Rectangle(0, 0, 1920, 1040);
        // the Ticker's toasts hang down over the target's name at the centre: it starts left of the target bar instead
        _hud.PlaceTopCentre(area, _hud.Look == HudLooks.Ticker ? HudLooks.TickerLeftOfCentre : null);
    }

    /// <summary>
    /// Where the player last dropped the panel, clamped into the screens on all four sides so some of it is always in
    /// view; null when it was never moved or the monitor it was on is gone (unplugged), so it goes back to the default.
    /// </summary>
    System.Windows.Point? SavedHudSpot()
    {
        if (HudSpotSaved() is not (double hx, double hy) || double.IsNaN(hx) || double.IsNaN(hy)) return null;
        const double keepW = 160, keepH = 40;   // the panel's size isn't known before it lays out: this much stays on screen
        double l = SystemParameters.VirtualScreenLeft, t = SystemParameters.VirtualScreenTop;
        double x = Math.Clamp(hx, l, Math.Max(l, l + SystemParameters.VirtualScreenWidth - keepW));
        double y = Math.Clamp(hy, t, Math.Max(t, t + SystemParameters.VirtualScreenHeight - keepH));
        // the screens' bounding box has gaps where monitors differ in size: the spot has to be on a monitor that is there
        double scale = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice.M11 ?? 1;
        var px = new System.Drawing.Point((int)Math.Round((x + keepW / 2) * scale), (int)Math.Round((y + keepH / 2) * scale));
        return System.Windows.Forms.Screen.AllScreens.Any(s => s.Bounds.Contains(px)) ? new System.Windows.Point(x, y) : null;
    }

    /// <summary>From the foreground hook: only the panel's presence, without working the figures out again.</summary>
    void HudFollowGame()
    {
        if (_hud == null) return;
        _hudValues["show"] = HudPresence();
        _hud.Update(_hudValues);
    }

    /* ---------------- Place the panel (Ui/PlacementGuide.cs) ---------------- */

    /// <summary>The placement guide while it is over the game.</summary>
    PlacementGuide? _guide;

    /// <summary>Screen pixels to a DIP for the panel's saved spot: the scale <see cref="SavedHudSpot"/> reads it back with.</summary>
    double HudSpotScale => PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice.M11 ?? 1;

    /// <summary>
    /// Place the panel… (the tray, Options › In game, and once after the first run's setup check): the guide over the
    /// game's part of its monitor, the panel drawn in its look to drag or snap. Null when it opened, else why not.
    /// </summary>
    public string? OpenPlacementGuide()
    {
        if (_guide != null) { _guide.Activate(); return null; }
        if (!HudWanted) return "The panel is off. Turn it on in Options › In game, then place it.";
        if (_watcher.GameRect is not { } game) return "Open the game first: the guide goes over it.";
        var view = System.Drawing.Rectangle.Intersect(game, System.Windows.Forms.Screen.FromRectangle(game).Bounds);
        if (view.Width < 320 || view.Height < 240) return "The game window is mostly off the screen. Move it back, then place the panel.";
        var guide = MakePlacementGuide(game, view);
        _guide = guide;
        guide.Closed += (_, _) => { if (_guide == guide) _guide = null; UpdateHud(); };
        guide.Open(this);
        UpdateHud();   // the panel steps aside while the guide is up (HudPresence)
        return null;
    }

    /// <summary>The guide for a game rect, drawn over <paramref name="view"/>, starting where the look's panel was left (the self-test and the renders pass their own).</summary>
    internal PlacementGuide MakePlacementGuide(System.Drawing.Rectangle game, System.Drawing.Rectangle view, string? look = null)
    {
        look = HudLooks.Parse(look ?? HudLook);
        double k = HudSpotScale;
        System.Drawing.Point? start = look == HudLook && SavedHudSpot() is { } s ? new System.Drawing.Point((int)Math.Round(s.X * k), (int)Math.Round(s.Y * k)) : null;
        return new PlacementGuide(look, HudFields.Parse(_prefs.HudFields), HudSample(), _hudPrefs.Scale, game, view, _watcher.Chat?.Region, start, px => KeepPlacedSpot(look, px));
    }

    /// <summary>Enter in the guide: the spot kept for its look, as a drag keeps it (the Ticker's apart), and the panel moved there.</summary>
    void KeepPlacedSpot(string look, System.Drawing.Point px)
    {
        double k = HudSpotScale, x = px.X / k, y = px.Y / k;
        if (look == HudLooks.Ticker) { _hudPrefs.TickerX = x; _hudPrefs.TickerY = y; if (!_demo) _hudPrefs.Save(); }
        else { _prefs.HudX = x; _prefs.HudY = y; if (!_demo) _prefs.Save(); }
        _hudSpotPreview = false;
        if (_hud != null && look == HudLook) { _hud.Left = x; _hud.Top = y; }
        Say("Panel placed. Place it again any time from the tray menu or Options › In game.", "Good");
    }

    /// <summary>The first run's setup check closed: the guide is owed, and opens once the game is in front (<see cref="PlacementGuideTick"/>).</summary>
    void PlacementGuideOwed()
    {
        if (_demo) return;
        _hudPrefs.PlaceGuideDue = true;
        _hudPrefs.Save();
    }

    /// <summary>From the window's tick: the owed guide opens once, when the game is in front with the panel over it.</summary>
    void PlacementGuideTick()
    {
        if (!_hudPrefs.PlaceGuideDue || _guide != null || _hud == null || !GameInFront()) return;
        _hudPrefs.PlaceGuideDue = false;
        _hudPrefs.Save();
        OpenPlacementGuide();
    }

    /// <summary>The guide is over the game, for the self-test.</summary>
    internal bool PlacementGuideOpen => _guide != null;
    /// <summary>The Ticker's saved spot, for the self-test.</summary>
    internal (double? X, double? Y) TickerSpotForTest => (_hudPrefs.TickerX, _hudPrefs.TickerY);
    /// <summary>Pixels to a DIP for the saved spots, for the self-test.</summary>
    internal double HudSpotScaleForTest => HudSpotScale;
}
