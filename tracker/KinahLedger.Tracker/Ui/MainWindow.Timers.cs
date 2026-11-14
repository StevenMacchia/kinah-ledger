using System.Windows;
using System.Windows.Controls;
using Shape = System.Windows.Shapes;
using System.Windows.Threading;
using KinahLedger.Session;

namespace KinahLedger.Ui;

/// <summary>
/// The timers' reach (0.10.85, Steven 2026-11-14: "I can not for the life of me find the tracker for the world boss spawns"):
/// a Timers button in the header beside the site and Settings, the timers overlay (Ui/TimersOverlay.cs) with its chime, and
/// their settings (Session/TimerBoard.cs, timers.json), set in Options › Timers (Ui/OptionsWindow.Timers.cs). One tick a second, whether or not the Timers window is open, moves the
/// overlay and rings the chime; it reads the same timers as the window.
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
        Closed += (_, _) => { _timersTick?.Stop(); _timersOverlay?.Close(); _timersOverlay = null; };
    }

    /// <summary>The header's Timers button, left of the site's (added here so the header's markup stays the redesign's).</summary>
    void AddTimersButton()
    {
        if (HeadButtons == null || _timersBtn != null) return;
        var icon = new Shape.Path();
        icon.SetResourceReference(StyleProperty, "QIconStyle");
        icon.SetResourceReference(Shape.Path.DataProperty, "IcTimer");
        _timersBtn = new Button { Content = icon, Margin = new Thickness(0, 0, 6, 0), ToolTip = "Timers: the game's events and the world bosses' spawns, with an overlay over the game and a chime before the ones you pick." };
        _timersBtn.SetResourceReference(StyleProperty, "RoundBtn");
        System.Windows.Automation.AutomationProperties.SetName(_timersBtn, "Timers");
        _timersBtn.Click += (_, _) => OpenBossWindow();
        HeadButtons.Children.Insert(0, _timersBtn);
    }

    /// <summary>A second's work: the overlay's rows, and the chime for any timer now close enough.</summary>
    void TimersTick(DateTime now)
    {
        if (_timersOverlay == null && !(_timerPrefs.ChimeOn && _timerPrefs.Chime.Count > 0)) return;
        List<TimerBoard.Item> items;
        try { items = TimerItems(now); } catch (Exception ex) { CrashLog.Write("timers", ex); return; }
        _timersOverlay?.Show(TimerBoard.ForOverlay(items, _timerPrefs, now), now);
        var ring = _timerChimes.Due(now, items, _timerPrefs);
        if (ring.Count == 0) return;
        PlayChime(_timerPrefs.Volume);
        CrashLog.Note("timers: chime for " + string.Join(", ", ring.Select(i => $"{i.Name} ({i.Big})")));
    }

    /// <summary>The overlay on or off (and saved), as Customize or the menu sets it.</summary>
    internal void SetTimersOverlay(bool on, bool save = true)
    {
        _timerPrefs.OverlayOn = on;
        if (save) _timerPrefs.Save();
        if (on && _timersOverlay == null)
        {
            _timersOverlay = new TimersOverlay(_timerPrefs);
            _timersOverlay.Closed += (_, _) => _timersOverlay = null;
            TimersTick(DateTime.UtcNow);   // it shows itself only while a timer is near
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
    /// <summary>The settings Options › Timers changes (they save as they change).</summary>
    internal TimerPrefs TimerChoices => _timerPrefs;
    /// <summary>Every timer now, for Options › Timers' list.</summary>
    internal List<TimerBoard.Item> TimerItemsNow() => TimerItems(DateTime.UtcNow);
}
