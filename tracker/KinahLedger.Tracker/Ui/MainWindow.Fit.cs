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
    /// Window size (0.10.11, Options › General and Ctrl+= / Ctrl+-): the window drawn bigger or smaller than its 662 DIP
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

    // ---- resizing by dragging (0.10.132, Steven, 2026-11-16: "allow users to resize the main kinah ledger app ... by dragging") ----
    // An edge or a corner dragged sets the window's size the way Options and Ctrl+= do: the whole window drawn bigger or
    // smaller (its pages are laid out to fit with no scrolling), live as the edge moves, kept on the drop. The window's
    // rectangle is held to the scaled content, so it never shows a gap or a cut. No maximize: it has no layout for that.

    internal const int WM_SIZING = 0x0214;
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    struct WinRect { public int L, T, R, B; }
    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int index);
    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int index, IntPtr value);

    bool _sizing;
    double _sizeBaseW, _sizeBaseH, _sizeChromeW, _sizeChromeH, _sizingScale;

    /// <summary>The frame keeps its sizing border, without the maximize button (and a double-click on the title bar does nothing).</summary>
    void NoMaximize(IntPtr hwnd)
    {
        const int GWL_STYLE = -16; const long WS_MAXIMIZEBOX = 0x00010000;
        long style = GetWindowLongPtr(hwnd, GWL_STYLE).ToInt64();
        if ((style & WS_MAXIMIZEBOX) != 0) SetWindowLongPtr(hwnd, GWL_STYLE, new IntPtr(style & ~WS_MAXIMIZEBOX));
    }

    /// <summary>
    /// An edge being dragged (WM_SIZING): the window's size from where the edge is (its width for a side or a corner, its
    /// height for the top or the bottom), 80% to 150%, drawn at once; the rectangle Windows proposes is set to that size.
    /// </summary>
    internal void OnSizing(IntPtr edgeParam, IntPtr rectParam)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        double dx = dpi.DpiScaleX > 0 ? dpi.DpiScaleX : 1, dy = dpi.DpiScaleY > 0 ? dpi.DpiScaleY : 1;
        if (!_sizing)
        {
            if (FitHost.ActualWidth <= 0 || FitHost.ActualHeight <= 0) return;
            _sizing = true;
            _sizeBaseW = FitHost.ActualWidth; _sizeBaseH = FitHost.ActualHeight;
            _sizeChromeW = Math.Max(0, ActualWidth - _sizeBaseW * _fitScale); _sizeChromeH = Math.Max(0, ActualHeight - _sizeBaseH * _fitScale);
            _sizingScale = _fitScale;
        }
        var r = System.Runtime.InteropServices.Marshal.PtrToStructure<WinRect>(rectParam);
        int edge = edgeParam.ToInt32();   // 1 left, 2 right, 3 top, 4 top-left, 5 top-right, 6 bottom, 7 bottom-left, 8 bottom-right
        bool side = edge is 1 or 2 or 4 or 5 or 7 or 8;
        double w = (r.R - r.L) / dx, h = (r.B - r.T) / dy;
        double s = side ? (w - _sizeChromeW) / _sizeBaseW : (h - _sizeChromeH) / _sizeBaseH;
        s = Math.Round(Math.Clamp(s, MinWindowScale, MaxWindowScale), 3);
        int nw = (int)Math.Round((_sizeChromeW + _sizeBaseW * s) * dx), nh = (int)Math.Round((_sizeChromeH + _sizeBaseH * s) * dy);
        if (edge is 1 or 4 or 7) r.L = r.R - nw; else r.R = r.L + nw;
        if (edge is 3 or 4 or 5) r.T = r.B - nh; else r.B = r.T + nh;
        System.Runtime.InteropServices.Marshal.StructureToPtr(r, rectParam, false);
        _sizingScale = s;
        if (Math.Abs(s - _fitScale) > 0.001) SetDrawScale(s);
    }

    /// <summary>The drag let go: the size it reached is the window's size from now on (as if chosen in Options), and the status line says it.</summary>
    void EndSizing()
    {
        if (!_sizing) return;
        _sizing = false;
        double kept = Math.Round(Math.Clamp(_sizingScale, MinWindowScale, MaxWindowScale), 2);
        if (_options != null) _options.SetWindowScale(kept);
        else if (Math.Abs(kept - _prefs.WindowScale) > 0.001) { _prefs.WindowScale = kept; SavePrefs(); }
        RefitScale();
        Say(WindowScaleLine(kept), "Faint");
    }

    /// <summary>
    /// For options-test: drags the right edge to <paramref name="factor"/> times the window's width, then to
    /// <paramref name="far"/> times, and drops it; the scale before, after the first pull, the rectangle's width ratio, after
    /// the far pull, and the size kept. The saved size is put back after.
    /// </summary>
    internal (double Before, double Wider, double WidthRatio, double Capped, double Kept) DragResizeForTest(double factor, double far)
    {
        double saved = _prefs.WindowScale, before = _fitScale;
        var dpi = VisualTreeHelper.GetDpi(this);
        int W0 = (int)Math.Round(ActualWidth * dpi.DpiScaleX), H0 = (int)Math.Round(ActualHeight * dpi.DpiScaleY);
        var p = System.Runtime.InteropServices.Marshal.AllocHGlobal(16);
        try
        {
            OnSizeMove(WM_ENTERSIZEMOVE);
            System.Runtime.InteropServices.Marshal.StructureToPtr(new WinRect { L = 0, T = 0, R = (int)(W0 * factor), B = H0 }, p, false);
            OnSizing(new IntPtr(2), p);
            double wider = _fitScale;
            var got = System.Runtime.InteropServices.Marshal.PtrToStructure<WinRect>(p);
            double ratio = (got.R - got.L) / (double)W0;
            System.Runtime.InteropServices.Marshal.StructureToPtr(new WinRect { L = 0, T = 0, R = (int)(W0 * far), B = H0 }, p, false);
            OnSizing(new IntPtr(2), p);
            double capped = _fitScale;
            OnSizeMove(WM_EXITSIZEMOVE);
            EndSizing();
            double kept = _prefs.WindowScale;
            return (before, wider, ratio, capped, kept);
        }
        finally
        {
            System.Runtime.InteropServices.Marshal.FreeHGlobal(p);
            _prefs.WindowScale = saved; RefitScale();
        }
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

    /// <summary>Ctrl+= and Ctrl+- (0.10.11): one step bigger or smaller, kept; Ctrl+0 is back to 100%. The status line says the size, and when the screen holds it back.</summary>
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
        _screenMax = wa.Height / dpi; ApplyMaxHeight();
        if (WindowState == WindowState.Normal && ActualHeight > 0 && Top + ActualHeight > wa.Bottom / dpi) Top = Math.Max(wa.Top / dpi, wa.Bottom / dpi - ActualHeight);
    }

    /// <summary>For the options self-test and layout review: the scale the window is drawn at, and whether Full view fell back to Compact.</summary>
    internal (double Scale, bool Compact) FitState => (_fitScale, _fullTooWide);
    /// <summary>For the options self-test: the window size as it shows now (the one being tried, or the saved one).</summary>
    internal double WindowScaleShownForTest => WindowScaleWanted;
    /// <summary>For the options self-test: Ctrl+= (1), Ctrl+- (-1) or Ctrl+0 (0).</summary>
    internal void StepWindowScaleForTest(int steps) => StepWindowScale(steps);
}
