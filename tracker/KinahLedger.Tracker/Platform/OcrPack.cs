using System.Diagnostics;
using System.Windows;
using Windows.Media.Ocr;

namespace KinahLedger.Platform;

/// <summary>
/// Windows reads the screen with its own text recognition, which needs the English recognizer: part of the English
/// language features, missing on some PCs set up in another language. Without it nothing is read. At start the tracker
/// checks, and when it is missing offers to add it: one Windows prompt (it is a Windows feature, so it needs the
/// administrator's OK) and about a minute's download (0.8, Steven 2026-10-22).
/// </summary>
public static class OcrPack
{
    /// <summary>The Windows feature that carries the English recognizer.</summary>
    public const string Capability = "Language.OCR~~~en-US~0.0.1.0";

    /// <summary>True when Windows can recognize English text.</summary>
    public static bool HasEnglish()
    {
        try { return OcrEngine.AvailableRecognizerLanguages.Any(l => l.LanguageTag.StartsWith("en", StringComparison.OrdinalIgnoreCase)); }
        catch (Exception) { return true; }   // the check itself failing says nothing about the feature; reading will tell
    }

    /// <summary>
    /// Asks, then adds the feature with one elevated PowerShell call and waits for it. True when English is there
    /// afterwards. Declining Windows' prompt, or no network, leaves things as they were and says how to do it by hand.
    /// </summary>
    public static bool OfferInstall(Window? owner)
    {
        var ask = "Kinah Ledger reads the game's chat with Windows' built-in text recognition, and the English part of it isn't installed on this PC.\n\n" +
                  "Add it now? Windows asks for permission once, then downloads it (about a minute).";
        var yes = (owner != null ? MessageBox.Show(owner, ask, "Kinah Ledger", MessageBoxButton.YesNo, MessageBoxImage.Information)
                                 : MessageBox.Show(ask, "Kinah Ledger", MessageBoxButton.YesNo, MessageBoxImage.Information)) == MessageBoxResult.Yes;
        if (!yes) return false;
        try
        {
            var p = Process.Start(new ProcessStartInfo("powershell.exe",
                $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -Command \"Add-WindowsCapability -Online -Name '{Capability}' | Out-Null\"")
            { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden });
            p?.WaitForExit(10 * 60 * 1000);
        }
        catch (System.ComponentModel.Win32Exception) { /* the Windows prompt was declined */ }
        catch (Exception ex) { CrashLog.Write("ocr pack", ex); }
        if (HasEnglish()) return true;
        MessageBox.Show("English text recognition still isn't installed.\n\nTo add it by hand: Settings › Time & language › Language & region › Add a language › English (United States), " +
                        "with Optical character recognition ticked. Then start Kinah Ledger again.", "Kinah Ledger", MessageBoxButton.OK, MessageBoxImage.Warning);
        return false;
    }
}
