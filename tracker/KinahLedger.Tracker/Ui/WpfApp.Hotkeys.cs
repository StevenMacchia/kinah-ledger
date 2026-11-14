using System.Windows;
using KinahLedger.Platform;

namespace KinahLedger.Ui;

public static partial class WpfApp
{
    /// <summary>
    /// options-test: shortcuts never reach the player's screen from a test or a second copy (Steven, 2026-11-14: test runs
    /// took his tracker's shortcuts and both sides popped "taken by another app" notices). Only the tracker holding the
    /// one-per-session claim registers shortcuts or shows a tray icon; a taken shortcut is a quiet line in Options ›
    /// Shortcuts and nothing else; one held by another Kinah Ledger is not mentioned at all.
    /// </summary>
    static void HotkeyChecks(MainWindow w, Action<bool, string> check)
    {
        // 1. this run is a test: no claim, so no shortcut registered and no tray icon (so no tray notice) from its windows
        check(!SingleInstance.Held && !Hotkey.Allowed && Hotkey.RegisteredCount == 0 && !w.TrayShownForTest,
            $"shortcuts: a test run registers none ({Hotkey.RegisteredCount}) and shows no tray icon, so it can't take the running tracker's keys or put notices on screen");
        // 3. a copy without the claim (a second tracker, a dev build beside the installed one) skips them, even where Windows would take them
        var oldNative = Hotkey.RegisterNative; var oldOther = Hotkey.OtherTracker;
        try
        {
            Hotkey.RegisterNative = (_, _, _, _) => true;
            var key = Hotkey.Parse("Ctrl+Shift+K")!.Value;
            check(key.Register(new IntPtr(1), 99) == Hotkey.Outcome.Skipped && Hotkey.RegisteredCount == 0,
                "shortcuts: a tracker without the one-per-session claim (a second copy) skips registering them");
            // 2. the tracker itself, its key taken by another app: one quiet line under it in Options › Shortcuts, nothing on the panel or in the status line
            Hotkey.AllowForTest = true;
            Hotkey.RegisterNative = (_, _, _, _) => false;
            Hotkey.OtherTracker = () => false;
            string status = w.StatusTextForTest;
            w.ApplyHotkeyForTest();
            var note = w.HotkeyNote;
            var opt = new OptionsWindow(w, OptionsWindow.Page.Tips) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -12000, Top = -12000, ShowActivated = false };
            opt.Show(); DoEvents();
            var hints = opt.KeyHintsForTest;
            opt.Close();
            check(note != null && note.Contains("another app") && hints.StartStop == note && w.HudNoticeForTest.Length == 0 && w.StatusTextForTest == status,
                $"shortcuts: one taken by another app is a quiet line under it in Options › Shortcuts (\"{hints.StartStop}\"), not a notice on the panel or the status line");
            // ...and taken by another Kinah Ledger (most likely the holder): nothing at all
            Hotkey.OtherTracker = () => true;
            w.ApplyHotkeyForTest();
            check(w.HotkeyNote == null && w.HudNoticeForTest.Length == 0, "shortcuts: one held by another Kinah Ledger is not mentioned anywhere");
        }
        finally
        {
            Hotkey.AllowForTest = false; Hotkey.RegisterNative = oldNative; Hotkey.OtherTracker = oldOther;
            w.ApplyHotkeyForTest();   // back to none (no claim)
        }
    }
}
