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

    /// <summary>From the foreground hook: only the panel's presence, without working the figures out again.</summary>
    void HudFollowGame()
    {
        if (_hud == null) return;
        _hudValues["show"] = HudPresence();
        _hud.Update(_hudValues);
    }
}
