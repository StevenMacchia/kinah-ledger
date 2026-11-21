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
    static string FullTooWideNote => L.T("Full view needs a wider screen than this one, so the window shows Compact. It comes back on a wider screen.");

    /// <summary>
    /// The work area's width, in this window's units, of the screen the window is on (the taskbar left out). A layout
    /// render has no screen of its own: as wide as it needs, unless KL_SCREEN_W names one to render the narrow case.
    /// </summary>
    double WorkWidth()
    {
        if (_demo) return double.TryParse(Environment.GetEnvironmentVariable("KL_SCREEN_W"), out var sw) && sw > 300 ? sw : double.PositiveInfinity;
        var h = new WindowInteropHelper(this).Handle;
        if (ScreenArea.Test is { } ts) return ts.Width;
        if (h == IntPtr.Zero) return SystemParameters.WorkArea.Width;
        double dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        return WF.Screen.FromHandle(h).WorkingArea.Width / (dpi > 0 ? dpi : 1);
    }

    /// <summary>The work area's height, in this window's units, of the screen the window is on. A layout render has none: as tall as it needs (KL_SCREEN_H renders a screen of that height).</summary>
    double WorkHeight()
    {
        if (_demo) return double.TryParse(Environment.GetEnvironmentVariable("KL_SCREEN_H"), out var sh) && sh > 300 ? sh : double.PositiveInfinity;
        var h = new WindowInteropHelper(this).Handle;
        if (ScreenArea.Test is { } ts) return ts.Height;
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
    /// The scale the window is drawn at for a chosen size: the size, unless the screen is too narrow for it, then as big as
    /// fits. Since 1.0.18 the screen's height doesn't take the size down: the window stops at the screen and the page scrolls
    /// in its area under the header (before, the size followed the page's height, so a page taller than the screen showed at
    /// 100% and the next one at 125%). Only a screen too short for the header and a few rows of a page holds it back.
    /// </summary>
    double DrawScale(bool full, double wanted)
    {
        double byWidth = WorkWidth() / ViewNeed(full);
        double byHeight = double.PositiveInfinity;
        if (!double.IsPositiveInfinity(WorkHeight()))
            byHeight = Math.Max(1, (WorkHeight() - FrameHeight()) / MinRoom);
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

    /// <summary>The chosen window size changed (Options, or a key): the window drawn at it now, without a relayout of the pages; the column and the room held to the screen at it.</summary>
    void RefitScale()
    {
        double next = DrawScale(FullOnScreen, WindowScaleWanted);
        if (Math.Abs(next - _fitScale) > 0.001) SetDrawScale(next);
        ApplyColumn();
        ApplyMaxHeight();
    }

    // ---- the window's size by dragging (1.0.18, Steven, 2026-11-18: "when you drag to resize it fucks up the size completely
    // on everything on the app"). 0.10.132 made a drag zoom the whole window (80% to 150%, the aspect locked), so a drag made
    // every text bigger and, on the pages that keep the Farming page's height, pushed the page past its scroll area. Now a
    // drag gives the pages room and the text stays its size; the zoom is Window size alone (Options, Ctrl+= and Ctrl+-).
    //  - Top or bottom: the most room the pages get under the header (WindowRoom). A page shorter than it keeps its own
    //    height (no empty strip under it), a longer one scrolls in its area, and the Parser's graphs grow into it. The edge
    //    stops where the page has nothing more to show.
    //  - A side: the one-column pages stretch, from the designed 662 to MaxColumnWidth (WindowWidth): cards, bars, tables
    //    and graphs fill it, the Runs cards and the bag's slots with them. Full view's two columns keep their width.
    //  - Both are kept for the next start, apart from the zoom; Reset size (Options, Ctrl+0) puts all three back.
    // No maximize: the pages have no layout for a whole screen.

    /// <summary>The designed width of a column (every page is laid out for it), and the widest a one-column page stretches to.</summary>
    public const double DesignWidth = 662, MaxColumnWidth = 1000;
    /// <summary>The least room a drag leaves the pages under the header: the header, the status line and a few rows of a page.</summary>
    public const double MinRoom = 360;

    /// <summary>The one-column pages' width now, in design units: the player's, held to the screen at the zoom drawn.</summary>
    double _column = DesignWidth;
    /// <summary>The width the player gave the window (the designed one until a side is dragged).</summary>
    double ColumnWanted => _prefs.WindowWidth > 0 ? _prefs.WindowWidth : DesignWidth;

    /// <summary>The frame around the column (the window's edges and title bar, the padding), in DIP: what a drag doesn't give the pages.</summary>
    double FrameWidth() => ChromeWidth() + Root.Padding.Left + Root.Padding.Right + Root.BorderThickness.Left + Root.BorderThickness.Right;
    double FrameHeight() => ChromeHeight() + Root.Padding.Top + Root.Padding.Bottom + Root.BorderThickness.Top + Root.BorderThickness.Bottom;

    /// <summary>A column of <paramref name="want"/>: never under the designed width, never past the widest, never wider than the screen holds at the zoom drawn.</summary>
    double ColumnFit(double want)
    {
        double max = MaxColumnWidth, ww = WorkWidth();
        if (!double.IsPositiveInfinity(ww)) max = Math.Min(max, (ww - FrameWidth()) / _fitScale);
        return Math.Round(Math.Clamp(want, DesignWidth, Math.Max(DesignWidth, max)));
    }

    /// <summary>The ledger's width as it shows: Full view's two columns, or the one column.</summary>
    double LedgerWidth() => FullOnScreen ? DesignWidth * 2 + 16 : _column;

    /// <summary>
    /// The column's width on every part sized to it: the header, the left column, the pages' scroll area, the status line, the
    /// toast, the Undo bar and the rate chart. Full view and the first-run screen keep the designed width.
    /// </summary>
    void ApplyColumn(double? want = null)
    {
        bool full = FullOnScreen;
        double col = full || _welcome ? DesignWidth : ColumnFit(want ?? ColumnWanted);
        bool changed = Math.Abs(col - _column) > 0.5;
        _column = col;
        LeftColDef.Width = new GridLength(col);
        RightColDef.Width = new GridLength(full ? 16 + DesignWidth : 0);
        HeadStack.Width = full ? DesignWidth * 2 + 16 : col;
        LeftCol.Width = col;
        StatusBar.Width = col;
        if (LowerScroll.Content is FrameworkElement lower) lower.Width = col;
        Toast.Width = col;
        UndoBar.Width = col - 40;
        RateCanvas.Width = col - 34; BestLinePath.X2 = col - 34;
        if (changed) ColumnChanged();
    }

    bool _columnRedrawQueued;
    /// <summary>The parts drawn for a width (the Runs cards, the bag's slots) drawn again for the new one, once for a burst of changes (a drag).</summary>
    void ColumnChanged()
    {
        if (_columnRedrawQueued || !IsLoaded) return;
        _columnRedrawQueued = true;
        Dispatcher.BeginInvoke(() =>
        {
            _columnRedrawQueued = false;
            if (_welcome) return;
            if (_part == "Events") ShowInstances();
            else if (_part == "Farming") FillBagStrip();
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    /// <summary>The room the screen leaves the pages at the zoom drawn, in design units (none for a render without KL_SCREEN_H).</summary>
    double ScreenRoom()
    {
        double screen = _demo ? WorkHeight() : Math.Min(_screenMax, WorkHeight());
        return double.IsPositiveInfinity(screen) ? double.PositiveInfinity : Math.Max(MinRoom, (screen - FrameHeight()) / _fitScale);
    }

    /// <summary>
    /// The most room the page gets under the header, in design units: the room the player dragged to; else on Quests, Timers
    /// and Parser the Farming page's height (they keep it, 0.10.28); else the page's own. The screen caps each.
    /// </summary>
    double RoomCap()
    {
        double room = double.PositiveInfinity;
        if (!_welcome && _prefs.WindowRoom > 0) room = Math.Max(MinRoom, _prefs.WindowRoom);
        else if (_part is "Quests" or "Timers" or "Parser" && !_welcome && _farmHeight > 0) room = _farmHeight;
        return Math.Min(room, ScreenRoom());
    }

    /// <summary>
    /// The window's height caps: the window never taller than the screen; the ledger never taller than <see cref="RoomCap"/>,
    /// in its own units, so a zoom draws the same room bigger (1.0.18: the cap was the window's height in pixels, the Farming
    /// page's at the zoom it was left at, and a bigger zoom pushed the page past it). Lifted while an edge is dragged.
    /// </summary>
    void ApplyMaxHeight()
    {
        MaxHeight = _screenMax;
        double cap = _sizing ? double.PositiveInfinity : RoomCap();
        if (FitHost.MaxHeight.Equals(cap)) return;
        FitHost.MaxHeight = cap;
        Dispatcher.BeginInvoke(FitGraphs, System.Windows.Threading.DispatcherPriority.Loaded);   // the Parser's graphs take the new room
    }

    /// <summary>
    /// The height the Parser's graphs fit (Ui/MainWindow.ParserGraphs.cs): the page area the window can give at its cap, not
    /// only the one it has now (the window sizes to its content, so the graphs would never grow into a taller room); during a
    /// drag, the area the window has.
    /// </summary>
    double GraphsRoom()
    {
        double cap = FitHost.MaxHeight;
        if (_sizing || double.IsPositiveInfinity(cap) || LedgerCol.ActualHeight <= 0 || LowerScroll.ActualHeight <= 0) return LowerScroll.ViewportHeight;
        double rest = LedgerCol.ActualHeight - LowerScroll.ActualHeight;   // the header over the page's area and the status line under it
        return Math.Min(LowerScroll.MaxHeight, cap - rest);
    }

    /// <summary>The Parser's Graphs view on the page: it takes any room it is given.</summary>
    bool GraphsShown => _graphs is { IsLoaded: true } g && g.IsVisible && g.IsDescendantOf(LowerScroll);

    /// <summary>The most room the page shown can use, in design units: the Graphs view any (up to the screen); another page, the room it has and what it scrolls past.</summary>
    double TallestRoom()
    {
        double screen = ScreenRoom();
        if (double.IsPositiveInfinity(screen)) screen = 4000;
        if (_part == "Parser" && GraphsShown) return screen;
        double extra = 0;
        foreach (var sv in new[] { LeftScroll, LowerScroll })
            if (sv.IsVisible) extra = Math.Max(extra, sv.ExtentHeight - sv.ViewportHeight);
        return Math.Min(screen, FitHost.ActualHeight + extra);
    }

    internal const int WM_SIZING = 0x0214;
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    struct WinRect { public int L, T, R, B; }
    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int index);
    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int index, IntPtr value);

    bool _sizing, _sizedV, _sizedW;
    double _sizeFrameW, _sizeFrameH, _sizeTallest, _sizingRoom;

    /// <summary>The frame keeps its sizing border, without the maximize button (and a double-click on the title bar does nothing).</summary>
    void NoMaximize(IntPtr hwnd)
    {
        const int GWL_STYLE = -16; const long WS_MAXIMIZEBOX = 0x00010000;
        long style = GetWindowLongPtr(hwnd, GWL_STYLE).ToInt64();
        if ((style & WS_MAXIMIZEBOX) != 0) SetWindowLongPtr(hwnd, GWL_STYLE, new IntPtr(style & ~WS_MAXIMIZEBOX));
    }

    /// <summary>
    /// An edge being dragged (WM_SIZING): a side sets the column's width (the one-column pages stretch; Full view holds), the
    /// top or the bottom the pages' room, between <see cref="MinRoom"/> and the most the page can use; the rectangle Windows
    /// proposes is set to that size, so the window never shows a gap or a cut while the edge moves. The zoom stays as it is.
    /// </summary>
    internal void OnSizing(IntPtr edgeParam, IntPtr rectParam)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        double dx = dpi.DpiScaleX > 0 ? dpi.DpiScaleX : 1, dy = dpi.DpiScaleY > 0 ? dpi.DpiScaleY : 1;
        if (!_sizing)
        {
            if (FitHost.ActualWidth <= 0 || FitHost.ActualHeight <= 0) return;
            _sizing = true; _sizedV = _sizedW = false;
            _sizeFrameW = Math.Max(0, ActualWidth - FitHost.ActualWidth * _fitScale);
            _sizeFrameH = Math.Max(0, ActualHeight - FitHost.ActualHeight * _fitScale);
            _sizeTallest = TallestRoom();
            _sizingRoom = FitHost.ActualHeight;
            FitHost.MaxHeight = double.PositiveInfinity;   // the drag sets the room; the drop keeps it
        }
        var r = System.Runtime.InteropServices.Marshal.PtrToStructure<WinRect>(rectParam);
        int edge = edgeParam.ToInt32();   // 1 left, 2 right, 3 top, 4 top-left, 5 top-right, 6 bottom, 7 bottom-left, 8 bottom-right
        bool side = edge is 1 or 2 or 4 or 5 or 7 or 8, vertical = edge is 3 or 4 or 5 or 6 or 7 or 8;
        double w = (r.R - r.L) / dx, h = (r.B - r.T) / dy;
        // the width is the player's at once: a page drawn again mid-drag (a tick, the Runs cards for the new width) keeps it
        if (side && !FullOnScreen && !_welcome) { ApplyColumn((w - _sizeFrameW) / _fitScale); _prefs.WindowWidth = _column; _sizedW = true; }
        if (vertical) { _sizingRoom = Math.Clamp((h - _sizeFrameH) / _fitScale, MinRoom, Math.Max(MinRoom, _sizeTallest)); _sizedV = true; }
        int nw = (int)Math.Round((_sizeFrameW + LedgerWidth() * _fitScale) * dx), nh = (int)Math.Round((_sizeFrameH + _sizingRoom * _fitScale) * dy);
        if (edge is 1 or 4 or 7) r.L = r.R - nw; else r.R = r.L + nw;
        if (edge is 3 or 4 or 5) r.T = r.B - nh; else r.B = r.T + nh;
        System.Runtime.InteropServices.Marshal.StructureToPtr(r, rectParam, false);
    }

    /// <summary>The drag let go: the room and the width it reached are kept (the zoom isn't touched), and the status line says how to undo it.</summary>
    void EndSizing()
    {
        if (!_sizing) return;
        _sizing = false;
        // an edge let go where the page has nothing more to show means "as tall as the page": no room kept, so a page that grows later shows whole
        if (_sizedV) _prefs.WindowRoom = _sizingRoom >= _sizeTallest - 1 && !(_part == "Parser" && GraphsShown) ? 0 : Math.Round(_sizingRoom);
        if ((_sizedV || _sizedW) && !_demo) _prefs.Save();
        ApplyMaxHeight();
        if (_sizedV || _sizedW) Say(L.F("Size kept · {0} resets it", WindowResetKey), "Faint");
    }

    /// <summary>
    /// For renders and options-test: an edge dragged by (<paramref name="dx"/>, <paramref name="dy"/>) DIP as Windows runs it:
    /// the drag starts, WM_SIZING proposes the rectangle, the window takes the one handed back (as Windows would), then
    /// <paramref name="mid"/> (a render's layout pass, a look during the drag) and the drop. Edge: 1 left, 2 right, 3 top, 6 bottom, 8 bottom-right.
    /// Returns the rectangle handed back, in DIP.
    /// </summary>
    internal (double W, double H) DragEdgeForTest(int edge, double dx, double dy, Action? mid = null)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        double sx = dpi.DpiScaleX > 0 ? dpi.DpiScaleX : 1, sy = dpi.DpiScaleY > 0 ? dpi.DpiScaleY : 1;
        int W0 = (int)Math.Round(ActualWidth * sx), H0 = (int)Math.Round(ActualHeight * sy);
        var r = new WinRect { L = 0, T = 0, R = W0, B = H0 };
        if (edge is 1 or 4 or 7) r.L -= (int)Math.Round(dx * sx); else if (edge is 2 or 5 or 8) r.R += (int)Math.Round(dx * sx);
        if (edge is 3 or 4 or 5) r.T -= (int)Math.Round(dy * sy); else if (edge is 6 or 7 or 8) r.B += (int)Math.Round(dy * sy);
        var p = System.Runtime.InteropServices.Marshal.AllocHGlobal(16);
        try
        {
            OnSizeMove(WM_ENTERSIZEMOVE);
            System.Runtime.InteropServices.Marshal.StructureToPtr(r, p, false);
            OnSizing(new IntPtr(edge), p);
            var got = System.Runtime.InteropServices.Marshal.PtrToStructure<WinRect>(p);
            Width = (got.R - got.L) / sx; Height = (got.B - got.T) / sy;
            mid?.Invoke();
            OnSizeMove(WM_EXITSIZEMOVE);
            EndSizing();
            return ((got.R - got.L) / sx, (got.B - got.T) / sy);
        }
        finally { System.Runtime.InteropServices.Marshal.FreeHGlobal(p); }
    }

    /// <summary>
    /// For renders and options-test: how the page sits in the window now, in the window's own pixels (DIP). Header: the header's
    /// foot; PageTop: the top of the page's area under it (its scroll area); Offset: that area's scroll; Bottom: the column's foot
    /// (the status line) against the frame's inner foot; GapBelow and GapRight: empty room between the column and the frame.
    /// </summary>
    internal (double Header, double PageTop, double Offset, double Bottom, double Inner, double GapBelow, double GapRight, double Scrollable) LayoutForTest()
    {
        UpdateLayout();
        double Y(FrameworkElement e, double y) => e.TranslatePoint(new Point(0, y), Root).Y;
        double header = Y(HeadStack, HeadStack.ActualHeight);
        System.Windows.Controls.ScrollViewer area = LowerScroll.IsVisible ? LowerScroll : LeftScroll;
        double top = Y(area, 0);
        double inner = Root.ActualHeight - Root.Padding.Bottom - Root.BorderThickness.Bottom;
        double colFoot = Y(LedgerCol, LedgerCol.ActualHeight);
        double right = LedgerCol.TranslatePoint(new Point(LedgerCol.ActualWidth, 0), Root).X;
        double innerW = Root.ActualWidth - Root.Padding.Right - Root.BorderThickness.Right;
        return (header, top, area.VerticalOffset, colFoot, inner, Math.Max(0, inner - colFoot), Math.Max(0, innerW - right), area.ScrollableHeight);
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

    /// <summary>
    /// Reset size (1.0.18, Options › General beside Window size, and Ctrl+0): 100%, the pages' own height and the designed
    /// width, kept at once. For a player whose size came from a drag that zoomed (0.10.132 to 1.0.17) or who lost track of it.
    /// </summary>
    public void ResetWindowSize()
    {
        _windowScalePreview = 0;
        _prefs.WindowScale = 1; _prefs.WindowRoom = 0; _prefs.WindowWidth = 0;
        if (!_demo) _prefs.Save();
        RefitScale();
        _options?.WindowSizeWasReset();   // Options open: its slider at 100%, with nothing left to save
        Say(L.T("Window size back to 100%, as tall as each page and its designed width"), "Faint");
    }

    /// <summary>Ctrl+= and Ctrl+- (0.10.11): one step bigger or smaller, kept; Ctrl+0 resets the size (1.0.18: the room and the width too). The status line says the size, and when the screen holds it back.</summary>
    void StepWindowScale(int steps)
    {
        if (steps == 0) { ResetWindowSize(); return; }
        double next = Math.Round(Math.Clamp(WindowScaleWanted + steps * WindowScaleStep, MinWindowScale, MaxWindowScale), 2);
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
        var line = L.F("Window size {0:P0}", wanted);
        if (wanted > _fitScale + 0.005) line += L.F(" · {0:P0} fits this screen", _fitScale);
        else if (Math.Abs(wanted - MaxWindowScale) < 0.001) line += L.T(" · the biggest");
        else if (Math.Abs(wanted - MinWindowScale) < 0.001) line += L.T(" · the smallest");
        return line + L.F(" · {0} and {1}, {2} for 100%", WindowBiggerKey, WindowSmallerKey, WindowResetKey);
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
    /// another screen or the scaling changes. Moved up if its bottom would now sit below the screen, and back in from a side
    /// a wider column would push it past (1.0.18).
    /// </summary>
    void FitMaxHeight()
    {
        if (_demo) return;
        if (ScreenArea.Test is { } ts) { _screenMax = ts.Height; ApplyMaxHeight(); return; }
        var h = new WindowInteropHelper(this).Handle;
        if (h == IntPtr.Zero) return;
        double dpi = VisualTreeHelper.GetDpi(this).DpiScaleY, dpiX = VisualTreeHelper.GetDpi(this).DpiScaleX;
        if (dpi <= 0) dpi = 1;
        if (dpiX <= 0) dpiX = 1;
        var wa = WF.Screen.FromHandle(h).WorkingArea;
        if (wa.Height <= 0) return;
        _screenMax = wa.Height / dpi; ApplyMaxHeight();
        if (WindowState != WindowState.Normal) return;
        if (ActualHeight > 0 && Top + ActualHeight > wa.Bottom / dpi) Top = Math.Max(wa.Top / dpi, wa.Bottom / dpi - ActualHeight);
        if (ActualWidth > 0 && Left + ActualWidth > wa.Right / dpiX) Left = Math.Max(wa.Left / dpiX, wa.Right / dpiX - ActualWidth);
    }

    /// <summary>For the options self-test and layout review: the scale the window is drawn at, and whether Full view fell back to Compact.</summary>
    internal (double Scale, bool Compact) FitState => (_fitScale, _fullTooWide);
    /// <summary>For the options self-test: the window size as it shows now (the one being tried, or the saved one).</summary>
    internal double WindowScaleShownForTest => WindowScaleWanted;
    /// <summary>For the options self-test: the room and the width kept (0: the page's own, the designed), the column as it shows, the ledger's room cap.</summary>
    internal (double Room, double Width, double Column, double Cap) SizeForTest => (_prefs.WindowRoom, _prefs.WindowWidth, _column, FitHost.MaxHeight);
    /// <summary>For renders and options-test: the window back to its first size (100%, the page's own height, the designed width, Compact).</summary>
    internal void ResetSizeForTest()
    {
        _windowScalePreview = 0;
        _prefs.WindowScale = 1; _prefs.FullView = false; _prefs.WindowRoom = 0; _prefs.WindowWidth = 0;
        SizeToContent = SizeToContent.WidthAndHeight;
        if (_part == "Farming") ApplyView(); else RefitScale();
    }
    /// <summary>For renders and options-test: the window size picked (as Options' Save keeps it).</summary>
    internal void SetWindowScaleForTest(double scale) { _prefs.WindowScale = scale; RefitScale(); }
    /// <summary>For the options self-test: Ctrl+= (1), Ctrl+- (-1) or Ctrl+0 (0).</summary>
    internal void StepWindowScaleForTest(int steps) => StepWindowScale(steps);
}
