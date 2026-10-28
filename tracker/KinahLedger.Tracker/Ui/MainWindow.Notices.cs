using WF = System.Windows.Forms;

namespace KinahLedger.Ui;

/// <summary>
/// The panel's one line under its figures (its "notice" value), shared by everything that has to reach a player who is
/// in the game and not looking at the window: a price alert, the start/stop shortcut not working, and an update that is
/// ready. The first that applies shows.
/// </summary>
public partial class MainWindow
{
    /// <summary>The update's own line ("Update ready · installs when you close the tracker"), set by Ui/MainWindow.Updates.cs (TellAboutUpdate); "" when none.</summary>
    string _updateNotice = "";
    /// <summary>How long the update's line stays on the panel once the panel is in view: the banner on the window keeps saying it.</summary>
    const int UpdateNoticeSeconds = 60;
    /// <summary>Until when the update's line shows; MaxValue until the panel is first in view after it was set.</summary>
    DateTime _updateNoticeUntil = DateTime.MaxValue;

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
    void HotkeyProblemAtStart(string problem, string title = "The start/stop shortcut is off")
    {
        Say(problem, "Warn");
        _hotkeyNoticeUntil = DateTime.MaxValue;
        if (_hotkeyBalloonShown || _demo) return;
        _hotkeyBalloonShown = true;
        try { _tray.ShowBalloonTip(8000, title, problem, WF.ToolTipIcon.Warning); } catch (Exception ex) { CrashLog.Write("shortcut notice", ex); }
    }

    /// <summary>How long a price alert stays on the panel and in the window's status line.</summary>
    const int PriceAlertSeconds = 8;
    string _priceNotice = "";
    DateTime _priceNoticeUntil = DateTime.MinValue;

    /// <summary>
    /// A price alert that fired (Ui/MainWindow.PriceAlerts.cs): on the panel for <see cref="PriceAlertSeconds"/> seconds,
    /// beside the tray notice that Focus Assist and Game Mode hold back while the game is in front, and in the window's
    /// status line for as long, the whole line on hover.
    /// </summary>
    void ShowPriceAlert(string line)
    {
        _priceNotice = line; _priceNoticeUntil = DateTime.UtcNow.AddSeconds(PriceAlertSeconds);
        Tell(line, "Good"); Status.ToolTip = line;
        if (_priceNoticeUntil > _statusHoldUntil) _statusHoldUntil = _priceNoticeUntil;
        UpdateHud();
    }

    /// <summary>How long the That was wrong key's answer stays on the panel, its gold line running down meanwhile.</summary>
    const double MarkNoticeSeconds = 3;
    string _markNotice = "";
    DateTime _markNoticeUntil = DateTime.MinValue;

    /// <summary>
    /// The That was wrong key's answer (Ui/MainWindow.MarkWrong.cs): "Saved the last minute · Report in tracker", or
    /// "Nothing counted in the last 2 minutes", for <see cref="MarkNoticeSeconds"/> seconds, ahead of every other notice.
    /// </summary>
    void ShowMarkNotice(string line)
    {
        _markNotice = line; _markNoticeUntil = DateTime.UtcNow.AddSeconds(MarkNoticeSeconds);
        UpdateHud();
    }

    /// <summary>The panel's "noticeClock" value while the That was wrong notice shows ("&lt;end ticks&gt;/3": the Card and the Ticker draw its gold line), else "".</summary>
    string HudNoticeClock() => DateTime.UtcNow < _markNoticeUntil ? $"{_markNoticeUntil.Ticks}/{MarkNoticeSeconds}" : "";

    /// <summary>The panel's notice now: the That was wrong key's answer (its 3 s), a price alert (its 8 s), a shortcut's problem (its first minute in view), else the update's line (its first minute in view).</summary>
    string HudNotice()
    {
        if (DateTime.UtcNow < _markNoticeUntil) return _markNotice;
        if (DateTime.UtcNow < _priceNoticeUntil) return _priceNotice;
        if ((_hotkeyProblem ?? _markKeyProblem) is { } hk && !_hotkeySuspended)
        {
            // the minute starts the first time the panel is in view (it is hidden while the game isn't in front)
            if (_hotkeyNoticeUntil == DateTime.MaxValue && _hudValues.GetValueOrDefault("show", "") != "hidden") _hotkeyNoticeUntil = DateTime.UtcNow.AddSeconds(HotkeyNoticeSeconds);
            if (DateTime.UtcNow < _hotkeyNoticeUntil) return hk.Short;
        }
        if (_updateNotice.Length == 0) return "";
        if (_updateNoticeUntil == DateTime.MaxValue && _hudValues.GetValueOrDefault("show", "") != "hidden") _updateNoticeUntil = DateTime.UtcNow.AddSeconds(UpdateNoticeSeconds);
        return DateTime.UtcNow < _updateNoticeUntil ? _updateNotice : "";
    }
}
