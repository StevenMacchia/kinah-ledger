using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using WF = System.Windows.Forms;

namespace KinahLedger.Ui;

/// <summary>
/// Full view on a screen narrower than it (2026-10-25 audit): Full is two 662 DIP columns, about 1,400 DIP with the
/// window's frame, and a 1080p screen at 150% Windows scaling is 1,280 DIP wide, so the window used to slide left and cut
/// the right column off. Now the two columns are scaled down together to fit the screen the window is on, never below
/// <see cref="MinFitScale"/>; on a screen narrower than that the window shows Compact and says why, once.
/// </summary>
public partial class MainWindow
{
    /// <summary>The smallest Full view is drawn at: below this its 12.5 px figures would be under 10 px, so Compact instead.</summary>
    const double MinFitScale = 0.75;
    /// <summary>The scale Full view is drawn at now (1 at full size, and in Compact).</summary>
    double _fitScale = 1;
    /// <summary>True while Full view is chosen but the screen is too narrow even scaled, so Compact shows.</summary>
    bool _fullTooWide;
    /// <summary>The Compact note was shown this session; it shows again only when the player asks for Full view.</summary>
    bool _fullNoteShown;
    /// <summary>Full view as it shows: chosen, and the screen wide enough for it (scaled or not).</summary>
    bool FullShown => _prefs.FullView && !_fullTooWide;

    /// <summary>The note Compact shows with when Full view doesn't fit.</summary>
    const string FullTooWideNote = "Full view needs a wider screen than this one, so the window shows Compact. It comes back on a wider screen.";

    /// <summary>
    /// The work area's width, in this window's units, of the screen the window is on (the taskbar left out). A layout
    /// render has no screen of its own: as wide as it needs, unless KL_SCREEN_W names one to render the narrow case.
    /// </summary>
    double WorkWidth()
    {
        if (_demo) return double.TryParse(Environment.GetEnvironmentVariable("KL_SCREEN_W"), out var sw) && sw > 300 ? sw : double.PositiveInfinity;
        var h = new WindowInteropHelper(this).Handle;
        if (h == IntPtr.Zero) return SystemParameters.WorkArea.Width;
        double dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        return WF.Screen.FromHandle(h).WorkingArea.Width / (dpi > 0 ? dpi : 1);
    }

    /// <summary>What Full view needs to be drawn at to fit the screen: 1 when it fits as it is, less when it has to shrink.</summary>
    double FullScale()
    {
        // the frame around the content (borders, and the window's edge): measured once the window is up, else Windows 11's
        double chrome = ActualWidth > 0 && Root.ActualWidth > 0 ? Math.Max(0, ActualWidth - Root.ActualWidth) : 16;
        double need = 662 * 2 + 16 + Root.Padding.Left + Root.Padding.Right + chrome;
        return Math.Min(1, WorkWidth() / need);
    }

    /// <summary>
    /// Works out how Full view fits (called from ApplyView before anything is placed): the scale it is drawn at, or Compact
    /// when even <see cref="MinFitScale"/> is too wide. <paramref name="asked"/>: the player just chose Full view, so the
    /// note shows now rather than only the first time.
    /// </summary>
    void FitFull(bool wantFull, bool asked = false)
    {
        double scale = wantFull ? FullScale() : 1;
        bool tooWide = wantFull && scale < MinFitScale;
        // said once the window is up (the constructor's own status line would cover it), and again whenever asked for
        bool up = new WindowInteropHelper(this).Handle != IntPtr.Zero;
        if (tooWide && (asked || (up && !_fullNoteShown))) { Say(FullTooWideNote, "Warn"); _fullNoteShown = true; }
        _fullTooWide = tooWide;
        _fitScale = wantFull && !tooWide && scale < 0.995 ? Math.Round(scale, 3) : 1;
        FitHost.LayoutTransform = _fitScale < 1 ? new ScaleTransform(_fitScale, _fitScale) : Transform.Identity;
        // text drawn smaller stays crisp with ideal layout; at full size the window keeps its pixel-snapped text
        TextOptions.SetTextFormattingMode(FitHost, _fitScale < 1 ? TextFormattingMode.Ideal : TextFormattingMode.Display);
    }

    /// <summary>The window moved to another screen, or the screen's scaling changed: Full view fitted again when that changes anything.</summary>
    void RefitIfNeeded()
    {
        if (!_prefs.FullView || _part != "Farming" || _welcome) return;
        double scale = FullScale();
        bool tooWide = scale < MinFitScale;
        double fit = !tooWide && scale < 0.995 ? Math.Round(scale, 3) : 1;
        if (tooWide != _fullTooWide || Math.Abs(fit - _fitScale) > 0.01) ApplyView();
        else if (tooWide && !_fullNoteShown) { Say(FullTooWideNote, "Warn"); _fullNoteShown = true; }   // the first fit ran before the window was up
    }

    /// <summary>For the options self-test and layout review: the scale Full view is drawn at, and whether it fell back to Compact.</summary>
    internal (double Scale, bool Compact) FitState => (_fitScale, _fullTooWide);
}
