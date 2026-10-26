using WF = System.Windows.Forms;

namespace KinahLedger.Ui;

/// <summary>
/// The panel's one line under its figures (its "notice" value), shared by everything that has to reach a player who is
/// in the game and not looking at the window: the start/stop shortcut not working, and an update waiting for the player
/// to leave the game. The first that applies shows.
/// </summary>
public partial class MainWindow
{
    /// <summary>The update's own line ("Update ready · installs when you leave the game"), set by Ui/MainWindow.Updates.cs; "" when none.</summary>
    string _updateNotice = "";

    /// <summary>How long the shortcut's problem stays on the panel once the panel is in view.</summary>
    const int HotkeyNoticeSeconds = 60;
    /// <summary>Until when the shortcut's problem shows on the panel; MaxValue until the panel is first in view.</summary>
    DateTime _hotkeyNoticeUntil = DateTime.MaxValue;
    bool _hotkeyBalloonShown;

    /// <summary>
    /// The shortcut found unusable at start (another app has it, or it breaks the shortcut rules). The status line alone
    /// was all there was, and a start from Windows sign-in (--background) never shows the window: so a tray notice, once,
    /// and the panel's line for its first minute in view.
    /// </summary>
    void HotkeyProblemAtStart(string problem)
    {
        Say(problem, "Warn");
        _hotkeyNoticeUntil = DateTime.MaxValue;
        if (_hotkeyBalloonShown || _demo) return;
        _hotkeyBalloonShown = true;
        try { _tray.ShowBalloonTip(8000, "The start/stop shortcut is off", problem, WF.ToolTipIcon.Warning); } catch (Exception ex) { CrashLog.Write("shortcut notice", ex); }
    }

    /// <summary>The panel's notice now: the shortcut's problem (its first minute in view), else the update's line.</summary>
    string HudNotice()
    {
        if (_hotkeyProblem is { } hk && !_hotkeySuspended)
        {
            // the minute starts the first time the panel is in view (it is hidden while the game isn't in front)
            if (_hotkeyNoticeUntil == DateTime.MaxValue && _hudValues.GetValueOrDefault("show", "") != "hidden") _hotkeyNoticeUntil = DateTime.UtcNow.AddSeconds(HotkeyNoticeSeconds);
            if (DateTime.UtcNow < _hotkeyNoticeUntil) return hk.Short;
        }
        return _updateNotice;
    }
}
