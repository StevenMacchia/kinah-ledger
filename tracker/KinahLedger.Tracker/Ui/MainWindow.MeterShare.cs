using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using KinahLedger.Parser;
using KinahLedger.Platform;

namespace KinahLedger.Ui;

/// <summary>
/// The meter's share picture (Ui/MeterShare.cs draws it), from three places: Share under the Parser's own overlay with the
/// pointer over it (while it takes clicks), Share's ▾ › Meter on the Parser page for the fight shown (a kept one too; its
/// other pictures are Ui/MainWindow.ParserShare.cs's), and a shortcut of
/// its own (Settings › In game › Parser › Share picture shortcut, Off by default) that copies the fight on the meter, or the
/// last one, while clicks go through. Each copies the picture and saves it in Pictures\Kinah Ledger, and says so.
/// </summary>
public partial class MainWindow
{
    const int ShareHotkeyId = 4;
    /// <summary>The fight the overlay shows now (ParserOverlayTick), for its Share and the shortcut.</summary>
    Encounter? _meterOnOverlay;

    /// <summary>The look the meter is drawn in over the game: the Parser window's own, or inside the Kinah overlay that overlay's fight look.</summary>
    MeterShare.Look MeterShareLook()
    {
        var (radius, pad) = HudWindow.FrameShape(HudFields.Parse(_prefs.HudFields));
        var key = _parserPrefs.Separate ? _parserPrefs.Look : ParserPrefs.LookOf(HudLook);
        return new MeterShare.Look(key, ParserModeNow, PDetails, radius, pad);
    }

    /// <summary>A record with something to share: members on the meter, not a dungeon's ready card.</summary>
    static bool Shareable(Encounter? e) => e is { Waiting: false } && e.Roster.Any(f => !f.Other);

    /// <summary>The shortcut's fight: the one on the meter now, else the last one that ended (this session's, else the newest kept).</summary>
    Encounter? MeterShareFight()
    {
        if (Shareable(_meterOnOverlay)) return _meterOnOverlay;
        if (Shareable(ParserLive)) return ParserLive;
        if (Shareable(_parserJustEnded)) return _parserJustEnded;
        if (Shareable(ParserFights?.LastEnded)) return ParserFights!.LastEnded;
        return ParserPast().LastOrDefault(Shareable);
    }

    /// <summary>
    /// The picture of <paramref name="e"/> copied and saved; said in the window's toast and, from the overlay, under the meter.
    /// True when it went to the clipboard.
    /// </summary>
    bool ShareMeter(Encounter? e, bool fromOverlay = false)
    {
        if (!Shareable(e))
        {
            var why = L.T("No fight to share yet. Pull a boss and the picture fills in.");
            Say(why, "Warn");
            if (fromOverlay) { _parserOverlay?.Flash(why, good: false); _hud?.FlashShare(why, good: false); }
            return false;
        }
        try
        {
            var now = e!.Live ? (_demo ? ParserNow : DateTime.UtcNow) : e.End ?? e.LastAt;
            var bmp = MeterShare.Render(e, now, MeterShareLook(), _parserPrefs.ShareHideNames);
            if (ShareSinkForTest is { } sink) sink(bmp, MeterShare.FileName(e));   // a test: never the clipboard or Pictures
            else
            {
                CopyPng(bmp);
                try { _meterShareSaved = SavePicture(bmp, MeterShare.FileName(e)); }
                catch (Exception ex) { CrashLog.Write("meter share save", ex); }
            }
            Say(L.T("Picture copied. Paste it in Discord or anywhere with Ctrl+V."), "Good");
            _parserOverlay?.Flash(L.T("Picture copied"), good: true);
            _hud?.FlashShare(L.T("Picture copied"), good: true);
            return true;
        }
        catch (Exception ex)
        {
            CrashLog.Write("meter share", ex);
            Say(L.F("Couldn't copy the picture: {0}", ex.Message), "Bad");
            _parserOverlay?.Flash(L.F("Couldn't copy the picture: {0}", ex.Message), good: false);
            return false;
        }
    }
    string _meterShareSaved = "";

    /// <summary>The overlay's Share and the page's: wired once the overlay is made.</summary>
    void WireOverlayShare(ParserOverlay o) => o.ShareAsked += () => ShareMeter(_meterOnOverlay, fromOverlay: true);

    // ---- the shortcut (Settings › In game › Parser › Share picture shortcut; Off by default) ----

    void RegisterShareHotkey(IntPtr h)
    {
        if (h == IntPtr.Zero) return;
        Native.UnregisterHotKey(h, ShareHotkeyId);
        if (Hotkey.Parse(_parserPrefs.ShareHotkey) is not { } key) return;
        var got = key.Register(h, ShareHotkeyId);
        if (got is Hotkey.Outcome.Taken or Hotkey.Outcome.TakenByTracker)
            KeyTakenOnce($"parser: the share picture's shortcut {key}: {(got == Hotkey.Outcome.Taken ? "taken by another app" : "held by another Kinah Ledger")}, off");
    }

    IntPtr ShareWndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Native.WM_HOTKEY && wParam.ToInt32() == ShareHotkeyId) { ShareMeter(MeterShareFight(), fromOverlay: true); handled = true; }
        return IntPtr.Zero;
    }

    void InitMeterShare(bool watch)
    {
        if (!watch) return;
        SourceInitialized += (_, _) =>
        {
            var h = new WindowInteropHelper(this).Handle;
            HwndSource.FromHwnd(h)?.AddHook(ShareWndProc);
            RegisterShareHotkey(h);
        };
        Closed += (_, _) => { var h = new WindowInteropHelper(this).Handle; if (h != IntPtr.Zero) Native.UnregisterHotKey(h, ShareHotkeyId); };
        // a new key saved in Settings: registered at once
        ParserPrefs.Saved += () => Dispatcher.BeginInvoke(() =>
        {
            var fresh = ParserPrefs.Load();
            _parserPrefs.ShareHideNames = fresh.ShareHideNames;
            if (fresh.ShareHotkey == _parserPrefs.ShareHotkey) return;
            _parserPrefs.ShareHotkey = fresh.ShareHotkey;
            RegisterShareHotkey(new WindowInteropHelper(this).Handle);
        });
    }

    // ---- for the options test and renders ----

    /// <summary>Set by the options test: the picture and its file name go here instead of the clipboard and Pictures.</summary>
    internal static Action<System.Windows.Media.Imaging.BitmapSource, string>? ShareSinkForTest;
    internal bool ShareMeterForTest(Encounter? e) => ShareMeter(e);
    internal bool ShareMeterShortcutForTest() => ShareMeter(MeterShareFight(), fromOverlay: true);
    internal void SetMeterOnOverlayForTest(Encounter? e) => _meterOnOverlay = e;
    internal string MeterShareSavedForTest => _meterShareSaved;
    internal bool ShareHideNamesForTest { get => _parserPrefs.ShareHideNames; set => _parserPrefs.ShareHideNames = value; }
}
