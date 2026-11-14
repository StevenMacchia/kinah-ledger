using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using KinahLedger.Session;
using Shape = System.Windows.Shapes;

namespace KinahLedger.Ui;

/// <summary>
/// The timers' reach (0.10.85, Steven 2026-11-14: "I can not for the life of me find the tracker for the world boss spawns"):
/// the timers overlay (Ui/TimersOverlay.cs) with its chime, and their settings
/// (Session/TimerBoard.cs, timers.json), set in Options › Timers (Ui/OptionsWindow.Timers.cs). One tick a second, whether or
/// not the Timers window is open, moves the overlay and rings the chime; it reads the same timers as the window.
///
/// For the redesign's Timers page (Ui/MainWindow.Overlays.cs on its branch), the pieces are callable from outside:
/// <see cref="TimerChoices"/> (the settings), <see cref="IsTimerWatched"/> / <see cref="SetTimerWatched"/> (a row's bell),
/// <see cref="SetTimerChime"/>, <see cref="TimersOverlayOn"/> / <see cref="SetTimersOverlay"/>, <see cref="OpenTimersPopOut"/>
/// and <see cref="TimerItemsNow"/>. The Timers page is Ui/MainWindow.TimersPage.cs.
/// </summary>
public partial class MainWindow
{
    TimerPrefs _timerPrefs = TimerPrefs.Defaults();
    readonly TimerChimes _timerChimes = new();
    TimersOverlay? _timersOverlay;
    DispatcherTimer? _timersTick;

    /// <summary>The timers for the overlay and the chime at <paramref name="now"/>.</summary>
    List<TimerBoard.Item> TimerItems(DateTime now) => TimerBoard.Build(now, EventClock, Bosses, EventRegion);

    /// <summary>Once, as the window opens: the header button; for a watching window the settings, the overlay and the tick.</summary>
    void InitTimers(bool watch)
    {
        // the redesign's Timers tab replaced the header button (AddTimersButton); the overlay joins the header's Overlays group
        RegisterOverlay("Timers", () => TimersOverlayOn, on => SetTimersOverlay(on), "Events and world bosses you watch, over the game: hidden until one is near, with a chime.");
        if (!watch) return;
        _timerPrefs = TimerPrefs.Load();
        _timersTick = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => TimersTick(DateTime.UtcNow), Dispatcher);
        _timersTick.Start();
        DrawOverlayPills();
        if (_timerPrefs.OverlayOn) Dispatcher.BeginInvoke(() => SetTimersOverlay(true, save: false), DispatcherPriority.ApplicationIdle);
        Closed += (_, _) => { _timersTick?.Stop(); _timersOverlay?.Close(); _timersOverlay = null; _bossWin?.Close(); };
    }

    /// <summary>Where the timer line goes now: on top of the Kinah overlay, in its own strip, or nowhere.</summary>
    string TimersHost => TimerPrefs.Host(_timerPrefs.OverlayOn, _timerPrefs.Separate, _hud != null);

    /// <summary>
    /// A second's work: the timer line where it goes (on top of the Kinah overlay by default; its own strip when separated or
    /// with the Kinah overlay off), and the chime for any watched timer now close enough. The first time the strip is needed
    /// because the Kinah overlay is off, it opens to be placed.
    /// </summary>
    void TimersTick(DateTime now)
    {
        if (!_timerPrefs.OverlayOn && !_timerPrefs.ChimeOn) { _hud?.ShowTimers(Array.Empty<TimerBoard.Item>(), now); return; }
        List<TimerBoard.Item> items;
        try { items = TimerItems(now); } catch (Exception ex) { CrashLog.Write("timers", ex); return; }
        var near = TimerBoard.ForOverlay(items, _timerPrefs, now);
        string host = TimersHost;
        _hud?.ShowTimers(host == "kinah" ? near : Array.Empty<TimerBoard.Item>(), now);
        if (host == "strip")
        {
            if (_timersOverlay == null) OpenTimersStrip();
            if (!_timerPrefs.Separate && !_timerPrefs.PlacedOnce && near.Count > 0 && _timersOverlay is { Placing: false } first)
            {
                // the line needs a home of its own (the Kinah overlay is off): the strip opens once to be placed
                _timerPrefs.PlacedOnce = true; _timerPrefs.Save();
                first.StartPlacing(PlacementSample(now), now);
            }
            _timersOverlay?.Show(near, now);
        }
        else if (_timersOverlay is { Placing: false }) { _timersOverlay.Close(); _timersOverlay = null; }
        var ring = _timerChimes.Due(now, items, _timerPrefs);
        if (ring.Count == 0) return;
        PlayChime(_timerPrefs.Volume);
        CrashLog.Note("timers: chime for " + string.Join(", ", ring.Select(i => $"{i.Name} ({i.Big})")));
    }

    /* ---------------- callable from the Timers page and Options ---------------- */

    /// <summary>The timers' settings (they save as they change).</summary>
    internal TimerPrefs TimerChoices => _timerPrefs;
    /// <summary>Every timer now.</summary>
    internal List<TimerBoard.Item> TimerItemsNow() => TimerItems(DateTime.UtcNow);
    internal bool IsTimerWatched(string key) => _timerPrefs.Watched(key);
    /// <summary>A timer on or off the overlay and its chime (a row's bell), saved.</summary>
    internal void SetTimerWatched(string key, bool on) { _timerPrefs.SetWatched(key, on); _timerPrefs.Save(); TimersTick(DateTime.UtcNow); }
    /// <summary>A watched timer's chime on or off, saved.</summary>
    internal void SetTimerChime(string key, bool on) { _timerPrefs.SetChimes(key, on); _timerPrefs.Save(); }
    internal bool TimersOverlayOn => _timerPrefs.OverlayOn;
    /// <summary>The timers are in their own strip (Settings › Timers › Separate timer overlay): the Overlays menu lists them then.</summary>
    internal bool TimersSeparate => _timerPrefs.Separate;
    /// <summary>The Kinah overlay is up (the timer line's home unless separated).</summary>
    internal bool KinahOverlayOn => _hud != null;
    /// <summary>For the options self-test: where the timer line goes now.</summary>
    internal string TimersHostForTest => TimersHost;
    /// <summary>The full timers window, to keep open beside the game or on a second screen.</summary>
    internal void OpenTimersPopOut() => OpenBossWindow();

    /// <summary>The timers overlay on or off (and saved): the line on the Kinah overlay, or the separate strip.</summary>
    internal void SetTimersOverlay(bool on, bool save = true)
    {
        _timerPrefs.OverlayOn = on;
        if (save) _timerPrefs.Save();
        if (!on && _timersOverlay != null) { _timersOverlay.Close(); _timersOverlay = null; }
        TimersTick(DateTime.UtcNow);   // the line shows itself only while a watched timer is near
        DrawOverlayPills();
    }

    /// <summary>Settings › Timers › Separate timer overlay: the timers in their own strip, or back on top of the Kinah overlay.</summary>
    internal void SetTimersSeparate(bool on)
    {
        _timerPrefs.Separate = on; _timerPrefs.Save();
        if (!on && _timersOverlay is { Placing: false }) { _timersOverlay.Close(); _timersOverlay = null; }
        TimersTick(DateTime.UtcNow);
        DrawOverlayPills();
        if (_part == "Timers") FillTimersPage();
    }

    /// <summary>The strip's window, made when the timers need it (separated, the Kinah overlay off, or to place it).</summary>
    void OpenTimersStrip()
    {
        if (_timersOverlay != null) return;
        _timersOverlay = new TimersOverlay(_timerPrefs)
        {
            OpenSettings = () => OpenOptions(OptionsWindow.Page.Timers),
            HideOverlay = () => SetTimersOverlay(false),
            SetClickThroughPref = SetTimersClickThrough,
        };
        _timersOverlay.Closed += (_, _) => _timersOverlay = null;
    }

    /// <summary>
    /// Move overlay (Steven, 2026-11-14): the strip shown now to be dragged, even with nothing due and with the overlay
    /// off; Done or Esc keeps the spot. With the overlay off it closes again after.
    /// </summary>
    internal void StartTimersPlacement()
    {
        OpenTimersStrip();
        var o = _timersOverlay!;
        // after Done the strip stays only where it is the timers' home (separated, or the Kinah overlay off)
        o.PlacementDone = () => { if (TimersHost != "strip" && _timersOverlay != null) { _timersOverlay.Close(); _timersOverlay = null; } };
        var now = DateTime.UtcNow;
        o.StartPlacing(PlacementSample(now), now);
    }

    /// <summary>Reset position: the overlay back to its default spot (top right of the main screen), saved.</summary>
    internal void ResetTimersOverlayPosition()
    {
        _timerPrefs.X = null; _timerPrefs.Y = null; _timerPrefs.Save();
        _timersOverlay?.ResetSpot();
    }

    /// <summary>What stands in while placing with nothing due: the next watched timers, else "Shugo 4:52 · Kashapa 12:10".</summary>
    List<TimerBoard.Item> PlacementSample(DateTime now)
    {
        List<TimerBoard.Item> next;
        try
        {
            next = TimerItems(now).Where(i => i.State == TimerBoard.Kind.Waiting && i.AtPc is { } at && at > now && _timerPrefs.Watched(i.Key))
                .OrderBy(i => i.AtPc).Take(Math.Max(1, _timerPrefs.Count)).ToList();
        }
        catch (Exception) { next = new(); }
        if (next.Count > 0) return next;
        TimerBoard.Item Fake(string name, string shortName, double seconds) =>
            new(TimerBoard.EventKey(name), "Events", name, "", TimerBoard.Kind.Waiting, now.AddSeconds(seconds), "", "") { Short = shortName };
        return new() { Fake("Shugo Festival", "Shugo", 292), Fake("Soul Ruler Kashapa", "Kashapa", 730) };
    }

    internal void SetTimersClickThrough(bool on)
    {
        _timerPrefs.ClickThrough = on; _timerPrefs.Save();
        _timersOverlay?.SetClickThrough(on);
    }

    /// <summary>For the options self-test: the overlay's window when it is on.</summary>
    internal TimersOverlay? TimersOverlayForTest => _timersOverlay;
    internal TimerPrefs TimerPrefsForTest { get => _timerPrefs; set => _timerPrefs = value; }
}
