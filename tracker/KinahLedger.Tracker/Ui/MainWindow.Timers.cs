using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using KinahLedger.Session;
using Shape = System.Windows.Shapes;

namespace KinahLedger.Ui;

/// <summary>
/// The timers' reach (0.10.85, Steven 2026-11-14: "I can not for the life of me find the tracker for the world boss spawns"):
/// a Timers button in the header, the timers overlay (Ui/TimersOverlay.cs) with its chime, and their settings
/// (Session/TimerBoard.cs, timers.json), set in Options › Timers (Ui/OptionsWindow.Timers.cs). One tick a second, whether or
/// not the Timers window is open, moves the overlay and rings the chime; it reads the same timers as the window.
///
/// For the redesign's Timers page (Ui/MainWindow.Overlays.cs on its branch), the pieces are callable from outside:
/// <see cref="TimerChoices"/> (the settings), <see cref="IsTimerWatched"/> / <see cref="SetTimerWatched"/> (a row's bell),
/// <see cref="SetTimerChime"/>, <see cref="TimersOverlayOn"/> / <see cref="SetTimersOverlay"/>, <see cref="OpenTimersPopOut"/>
/// and <see cref="TimerItemsNow"/>. The header button is one method, <see cref="AddTimersButton"/>.
/// </summary>
public partial class MainWindow
{
    TimerPrefs _timerPrefs = TimerPrefs.Defaults();
    readonly TimerChimes _timerChimes = new();
    TimersOverlay? _timersOverlay;
    DispatcherTimer? _timersTick;
    Button? _timersBtn;

    /// <summary>The timers for the overlay and the chime at <paramref name="now"/>.</summary>
    List<TimerBoard.Item> TimerItems(DateTime now) => TimerBoard.Build(now, EventClock, Bosses, _watcher.RegionKey);

    /// <summary>Once, as the window opens: the header button; for a watching window the settings, the overlay and the tick.</summary>
    void InitTimers(bool watch)
    {
        AddTimersButton();
        if (!watch) return;
        _timerPrefs = TimerPrefs.Load();
        _timersTick = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => TimersTick(DateTime.UtcNow), Dispatcher);
        _timersTick.Start();
        if (_timerPrefs.OverlayOn) Dispatcher.BeginInvoke(() => SetTimersOverlay(true, save: false), DispatcherPriority.ApplicationIdle);
        Closed += (_, _) => { _timersTick?.Stop(); _timersOverlay?.Close(); _timersOverlay = null; _bossWin?.Close(); };
    }

    /// <summary>The header's Timers button, left of the site's (until the redesign's Timers tab replaces it).</summary>
    void AddTimersButton()
    {
        if (HeadButtons == null || _timersBtn != null) return;
        var icon = new Shape.Path();
        icon.SetResourceReference(StyleProperty, "QIconStyle");
        icon.SetResourceReference(Shape.Path.DataProperty, "IcTimer");
        _timersBtn = new Button { Content = icon, Margin = new Thickness(0, 0, 6, 0), ToolTip = "Timers: when the events start and the world bosses spawn." };
        _timersBtn.SetResourceReference(StyleProperty, "RoundBtn");
        System.Windows.Automation.AutomationProperties.SetName(_timersBtn, "Timers");
        _timersBtn.Click += (_, _) => OpenTimersPopOut();
        HeadButtons.Children.Insert(0, _timersBtn);
    }

    /// <summary>A second's work: the overlay's rows, and the chime for any watched timer now close enough.</summary>
    void TimersTick(DateTime now)
    {
        if (_timersOverlay == null && !_timerPrefs.ChimeOn) return;
        List<TimerBoard.Item> items;
        try { items = TimerItems(now); } catch (Exception ex) { CrashLog.Write("timers", ex); return; }
        _timersOverlay?.Show(TimerBoard.ForOverlay(items, _timerPrefs, now), now);
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
    /// <summary>The full timers window, to keep open beside the game or on a second screen.</summary>
    internal void OpenTimersPopOut() => OpenBossWindow();

    /// <summary>The overlay on or off (and saved).</summary>
    internal void SetTimersOverlay(bool on, bool save = true)
    {
        _timerPrefs.OverlayOn = on;
        if (save) _timerPrefs.Save();
        if (on && _timersOverlay == null)
        {
            _timersOverlay = new TimersOverlay(_timerPrefs)
            {
                OpenSettings = () => OpenOptions(OptionsWindow.Page.Timers),
                HideOverlay = () => SetTimersOverlay(false),
                SetClickThroughPref = SetTimersClickThrough,
            };
            _timersOverlay.Closed += (_, _) => _timersOverlay = null;
            TimersTick(DateTime.UtcNow);   // it shows itself only while a watched timer is near
        }
        else if (!on && _timersOverlay != null) { _timersOverlay.Close(); _timersOverlay = null; }
    }

    internal void SetTimersClickThrough(bool on)
    {
        _timerPrefs.ClickThrough = on; _timerPrefs.Save();
        _timersOverlay?.SetClickThrough(on);
    }

    /// <summary>For the options self-test: the header button, and the overlay's window when it is on.</summary>
    internal Button? TimersButtonForTest => _timersBtn;
    internal TimersOverlay? TimersOverlayForTest => _timersOverlay;
    internal TimerPrefs TimerPrefsForTest { get => _timerPrefs; set => _timerPrefs = value; }
}
