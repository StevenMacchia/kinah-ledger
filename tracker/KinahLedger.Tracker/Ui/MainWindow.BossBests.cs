using KinahLedger.Upload;

namespace KinahLedger.Ui;

/// <summary>
/// Options › Sharing › Show my boss bests on my kinahledger.com profile (2026-11-15, Upload/BossBests.cs). Off by default.
/// On, your own best kill on each boss and your last pull of it ride the run uploads to your signed-in profile; your group's
/// numbers stay on this PC. Off again, the next upload tells the site to forget them.
/// </summary>
public partial class MainWindow
{
    /// <summary>Hands the saved choice to the uploader (once, at start).</summary>
    void InitBossBests()
    {
        BossBests.Enabled = _prefs.ShareBossBests && !_demo;
        BossBests.ClearPending = _prefs.BossBestsClearPending && !_demo;
        BossBests.Cleared = () => Dispatcher.BeginInvoke(() => { _prefs.BossBestsClearPending = false; SavePrefs(); });
    }

    /// <summary>The switch, from Save in Options: off asks the site to forget what it kept.</summary>
    void SetBossBests(bool on)
    {
        _prefs.ShareBossBests = on;
        _prefs.BossBestsClearPending = !on;
        BossBests.Reset();   // on again: the whole list goes with the next upload
        BossBests.Enabled = on && !_demo;
        BossBests.ClearPending = !on && !_demo;
    }
}
