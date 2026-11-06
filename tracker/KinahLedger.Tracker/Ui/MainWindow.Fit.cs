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
    /// <summary>
    /// Window size (0.10.9, Options › General and Ctrl+= / Ctrl+-): the window drawn bigger or smaller than its 662 DIP
    /// columns, 80% to 150% in steps of 10%, for a large or high-resolution screen. The screen still caps it: the
    /// window never grows past the width or the height of the screen it is on.
    /// </summary>
    public const double MinWindowScale = 0.8, MaxWindowScale = 1.5, WindowScaleStep = 0.1;
    /// <summary>The size Options is trying while it is open (shown at once, kept on Save), or 0 for the saved one.</summary>
    double _windowScalePreview;
    /// <summary>The window size as it shows now: the one being tried in Options, else the saved one.</summary>
    double WindowScaleWanted => Math.Clamp(_windowScalePreview > 0 ? _windowScalePreview : _prefs.WindowScale, MinWindowScale, MaxWindowScale);
    /// <summary>The scale the window is drawn at now: the chosen size, capped by the screen (1 at full size).</summary>
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

    /// <summary>The work area's height, in this window's units, of the screen the window is on. A layout render has none: as tall as it needs (KL_SCREEN_H renders a screen of that height).</summary>
    double WorkHeight()
    {
        if (_demo) return double.TryParse(Environment.GetEnvironmentVariable("KL_SCREEN_H"), out var sh) && sh > 300 ? sh : double.PositiveInfinity;
        var h = new WindowInteropHelper(this).Handle;
        if (h == IntPtr.Zero) return SystemParameters.WorkArea.Height;
        double dpi = VisualTreeHelper.GetDpi(this).DpiScaleY;
        return WF.Screen.FromHandle(h).WorkingArea.Height / (dpi > 0 ? dpi : 1);
    }

    /// <summary>The frame around the content (borders, and the window's edge): measured once the window is up, else Windows 11's.</summary>
    double ChromeWidth() => ActualWidth > 0 && Root.ActualWidth > 0 ? Math.Max(0, ActualWidth - Root.ActualWidth) : 16;
    double ChromeHeight() => ActualHeight > 0 && Root.ActualHeight > 0 ? Math.Max(0, ActualHeight - Root.ActualHeight) : 40;

    /// <summary>What Full view needs to be drawn at to fit the screen's width: 1 when it fits as it is, less when it has to shrink.</summary>
    double FullScale() => Math.Min(1, WorkWidth() / ViewNeed(full: true));

    /// <summary>The width a view takes at full size, with the window's padding and frame.</summary>
    double ViewNeed(bool full) => (full ? 662 * 2 + 16 : 662) + Root.Padding.Left + Root.Padding.Right + ChromeWidth();

    /// <summary>
    /// The scale the window is drawn at for a chosen size: the size, unless the screen is too narrow or too short for
    /// it, then as big as fits. The height cap never goes below 1: a short screen clips the window at full size as it
    /// always did, it only stops a bigger size from making that worse.
    /// </summary>
    double DrawScale(bool full, double wanted)
    {
        double byWidth = WorkWidth() / ViewNeed(full);
        double byHeight = double.PositiveInfinity;
        double content = LedgerCol.ActualHeight;   // in the window's own units, before the transform
        if (content > 0 && !double.IsPositiveInfinity(WorkHeight()))
            byHeight = Math.Max(1, WorkHeight() / (content + Root.Padding.Top + Root.Padding.Bottom + ChromeHeight()));
        double s = Math.Min(wanted, Math.Min(byWidth, byHeight));
        return Math.Abs(s - 1) < 0.005 ? 1 : Math.Round(s, 3);
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
        SetDrawScale(DrawScale(wantFull && !tooWide, WindowScaleWanted));
    }

    /// <summary>Draws the window at <paramref name="scale"/>: the one piece under the frame, scaled together.</summary>
    void SetDrawScale(double scale)
    {
        _fitScale = scale;
        FitHost.LayoutTransform = _fitScale != 1 ? new ScaleTransform(_fitScale, _fitScale) : Transform.Identity;
        // text drawn at another size stays crisp with ideal layout; at full size the window keeps its pixel-snapped text
        TextOptions.SetTextFormattingMode(FitHost, _fitScale != 1 ? TextFormattingMode.Ideal : TextFormattingMode.Display);
    }

    /// <summary>True while Full view is on screen: chosen, on Farming, and the screen wide enough for it.</summary>
    bool FullOnScreen => FullShown && _part == "Farming" && !_welcome;

    /// <summary>The chosen window size changed (Options, or a key): the window drawn at it now, without a relayout of the pages.</summary>
    void RefitScale()
    {
        double next = DrawScale(FullOnScreen, WindowScaleWanted);
        if (Math.Abs(next - _fitScale) > 0.001) SetDrawScale(next);
    }

    /// <summary>Options › General › Window size, being tried: the window shows it now; Save keeps it, Cancel puts the saved one back.</summary>
    public void PreviewWindowScale(double scale)
    {
        _windowScalePreview = Math.Clamp(scale, MinWindowScale, MaxWindowScale);
        RefitScale();
    }

    /// <summary>Options closed (after Save or Cancel): the window back to the saved size, which Save has just made the edited one.</summary>
    public void EndWindowScalePreview()
    {
        _windowScalePreview = 0;
        RefitScale();
    }

    /// <summary>Ctrl+= and Ctrl+- (0.10.9): one step bigger or smaller, kept; Ctrl+0 is back to 100%. The status line says the size, and when the screen holds it back.</summary>
    void StepWindowScale(int steps)
    {
        double next = steps == 0 ? 1 : Math.Round(Math.Clamp(WindowScaleWanted + steps * WindowScaleStep, MinWindowScale, MaxWindowScale), 2);
        if (_options != null) { _options.SetWindowScale(next); return; }   // Options open: the slider there moves, and Save keeps it
        if (Math.Abs(next - _prefs.WindowScale) > 0.001)
        {
            _prefs.WindowScale = next;
            SavePrefs();
            RefitScale();
        }
        Say(WindowScaleLine(next), "Faint");
    }

    /// <summary>"Window size 120%", and "110% fits this screen" when the screen caps it.</summary>
    string WindowScaleLine(double wanted)
    {
        var line = $"Window size {wanted:P0}";
        if (wanted > _fitScale + 0.005) line += $" · {_fitScale:P0} fits this screen";
        else if (Math.Abs(wanted - MaxWindowScale) < 0.001) line += " · the biggest";
        else if (Math.Abs(wanted - MinWindowScale) < 0.001) line += " · the smallest";
        return line + $" · {WindowBiggerKey} and {WindowSmallerKey}, {WindowResetKey} for 100%";
    }

    /// <summary>The window moved to another screen, or the screen's scaling changed: fitted again when that changes anything.</summary>
    void RefitIfNeeded()
    {
        FitMaxHeight();   // whatever the view: a taller window than the new screen could not be resized back
        if (!_prefs.FullView || _part != "Farming" || _welcome) { RefitScale(); return; }
        double scale = FullScale();
        bool tooWide = scale < MinFitScale;
        double fit = DrawScale(!tooWide, WindowScaleWanted);
        if (tooWide != _fullTooWide || Math.Abs(fit - _fitScale) > 0.01) ApplyView();
        else if (tooWide && !_fullNoteShown) { Say(FullTooWideNote, "Warn"); _fullNoteShown = true; }   // the first fit ran before the window was up
    }

    /// <summary>
    /// The window is never taller than the working area of the screen it is on: set at start, and again when it moves to
    /// another screen or the scaling changes. Moved up if its bottom would now sit below the screen.
    /// </summary>
    void FitMaxHeight()
    {
        if (_demo) return;
        var h = new WindowInteropHelper(this).Handle;
        if (h == IntPtr.Zero) return;
        double dpi = VisualTreeHelper.GetDpi(this).DpiScaleY;
        if (dpi <= 0) dpi = 1;
        var wa = WF.Screen.FromHandle(h).WorkingArea;
        if (wa.Height <= 0) return;
        MaxHeight = wa.Height / dpi;
        if (WindowState == WindowState.Normal && ActualHeight > 0 && Top + ActualHeight > wa.Bottom / dpi) Top = Math.Max(wa.Top / dpi, wa.Bottom / dpi - ActualHeight);
    }

    /// <summary>For the options self-test and layout review: the scale the window is drawn at, and whether Full view fell back to Compact.</summary>
    internal (double Scale, bool Compact) FitState => (_fitScale, _fullTooWide);
    /// <summary>For the options self-test: the window size as it shows now (the one being tried, or the saved one).</summary>
    internal double WindowScaleShownForTest => WindowScaleWanted;
    /// <summary>For the options self-test: Ctrl+= (1), Ctrl+- (-1) or Ctrl+0 (0).</summary>
    internal void StepWindowScaleForTest(int steps) => StepWindowScale(steps);
}
