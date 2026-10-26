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
}
