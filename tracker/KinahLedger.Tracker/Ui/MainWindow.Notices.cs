using WF = System.Windows.Forms;

namespace KinahLedger.Ui;

/// <summary>
/// The panel's one line under its figures (its "notice" value), shared by everything that has to reach a player who is
/// in the game and not looking at the window: a price alert and an update that is ready (a shortcut that
/// is off is never one: it is a quiet line in Options › Shortcuts). The first that applies shows.
/// </summary>
public partial class MainWindow
{
    /// <summary>The update's own line ("Update ready · installs when you close the tracker"), set by Ui/MainWindow.Updates.cs (TellAboutUpdate); "" when none.</summary>
    string _updateNotice = "";
    /// <summary>How long the update's line stays on the panel once the panel is in view: the banner on the window keeps saying it.</summary>
    const int UpdateNoticeSeconds = 60;
    /// <summary>Until when the update's line shows; MaxValue until the panel is first in view after it was set.</summary>
    DateTime _updateNoticeUntil = DateTime.MaxValue;

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

    /// <summary>The panel's notice now: the That was wrong key's answer (its 3 s), a price alert (its 8 s), else the update's line (its first minute in view).</summary>
    string HudNotice()
    {
        if (DateTime.UtcNow < _markNoticeUntil) return _markNotice;
        if (DateTime.UtcNow < _priceNoticeUntil) return _priceNotice;
        if (_updateNotice.Length == 0) return "";
        if (_updateNoticeUntil == DateTime.MaxValue && _hudValues.GetValueOrDefault("show", "") != "hidden") _updateNoticeUntil = DateTime.UtcNow.AddSeconds(UpdateNoticeSeconds);
        return DateTime.UtcNow < _updateNoticeUntil ? _updateNotice : "";
    }
}
