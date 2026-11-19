using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace KinahLedger.Ui;

public static partial class WpfApp
{
    /// <summary>The sizes the resize renders go through: the window size (zoom) picked, then each drag: an edge (1 left, 2 right, 3 top, 6 bottom) moved by (dx, dy) DIP.</summary>
    internal static readonly (string Name, double Zoom, (int Edge, double Dx, double Dy)[] Drags)[] ResizeCases =
    {
        ("default", 1, new (int, double, double)[0]),
        ("taller", 1, new[] { (6, 0.0, 320.0) }),
        ("much-taller", 1, new[] { (6, 0.0, 700.0) }),
        ("shorter", 1, new[] { (6, 0.0, -300.0) }),
        ("top-shorter", 1, new[] { (3, 0.0, -200.0) }),
        ("wider", 1, new[] { (2, 240.0, 0.0) }),
        ("widest", 1, new[] { (2, 900.0, 0.0) }),
        ("narrower", 1, new[] { (2, 340.0, 0.0), (1, -200.0, 0.0) }),
        ("narrowest", 1, new[] { (1, -300.0, 0.0) }),
        ("zoom80", 0.8, new (int, double, double)[0]),
        ("zoom100", 1, new (int, double, double)[0]),
        ("zoom130", 1.3, new (int, double, double)[0]),
        ("zoom80-taller", 0.8, new[] { (6, 0.0, 300.0) }),
        ("zoom130-shorter", 1.3, new[] { (6, 0.0, -300.0) }),
        ("zoom130-wider", 1.3, new[] { (2, 240.0, 0.0) }),
    };

    /// <summary>
    /// ui-shot DIR --resize (2026-11-18, Steven: "when you drag to resize it fucks up the size completely"): the main window on
    /// Farm, Parser (the Graphs tab) and Runs at each of <see cref="ResizeCases"/>, as resize-PAGE-CASE.png, with resize.txt
    /// saying for each where the page sits under the header, its scroll, and any gap or cut. A screen of KL_SCREEN_H (1040 when
    /// not set) caps the window as a real one would.
    /// </summary>
    public static int ShotResize(Settings settings, string dir)
    {
        Ensure();
        Directory.CreateDirectory(dir);
        if (Environment.GetEnvironmentVariable("KL_SCREEN_H") == null) Environment.SetEnvironmentVariable("KL_SCREEN_H", "1040");
        var w = new MainWindow(settings, watch: false) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -12000, Top = -12000, ShowActivated = false };
        var report = new List<string>();
        int bad = 0;
        try
        {
            w.Show();
            Skins.Apply(Skins.Default);
            w.ShowDemo("Open world"); DoEvents();
            var pages = new (string Name, Action Show)[]
            {
                ("farm", () => w.ShowPartForTest("Farming")),
                ("parser", () => w.ShowGraphsDemo("six")),
                ("runs", () => w.ShowDemo("Events")),
            };
            foreach (var (page, show) in pages)
                foreach (var (name, zoom, drags) in ResizeCases)
                {
                    w.ResetSizeForTest();
                    show(); DoEvents(); DoEvents();
                    if (Math.Abs(zoom - 1) > 0.001) { w.SetWindowScaleForTest(zoom); DoEvents(); DoEvents(); }
                    foreach (var (edge, dx, dy) in drags) { w.DragEdgeForTest(edge, dx, dy, DoEvents); DoEvents(); DoEvents(); }
                    var file = $"resize-{page}-{name}.png";
                    Save((FrameworkElement)w.Content, Path.Combine(dir, file), 784);
                    var l = w.LayoutForTest();
                    var problems = ResizeProblems(l);
                    if (problems.Count > 0) bad++;
                    var size = w.SizeForTest;
                    report.Add($"{file,-36} window {w.ActualWidth:0}x{w.ActualHeight:0} zoom {w.FitState.Scale:0.00} column {size.Column:0} room {size.Room:0} header foot {l.Header:0} page top {l.PageTop:0} scroll {l.Offset:0}/{l.Scrollable:0} foot {l.Bottom:0} of {l.Inner:0}"
                        + (problems.Count > 0 ? "  PROBLEM: " + string.Join("; ", problems) : ""));
                }
            w.ResetSizeForTest();
        }
        catch (Exception ex) { Console.WriteLine(ex); return 1; }
        finally { w.Close(); }
        File.WriteAllLines(Path.Combine(dir, "resize.txt"), report);
        foreach (var l in report) Console.WriteLine(l);
        Console.WriteLine($"wrote {Path.GetFullPath(dir)}: {report.Count} renders, {bad} with a problem");
        return bad == 0 ? 0 : 1;
    }

    /// <summary>What is wrong with a page's place in the window: under the header, scrolled off its top, cut at the foot, a gap.</summary>
    internal static List<string> ResizeProblems((double Header, double PageTop, double Offset, double Bottom, double Inner, double GapBelow, double GapRight, double Scrollable) l)
    {
        var p = new List<string>();
        if (l.PageTop < l.Header - 0.5) p.Add($"the page starts {l.Header - l.PageTop:0} px under the header");
        if (l.Offset > 0.5) p.Add($"the page is scrolled {l.Offset:0} px off its top");
        if (l.Bottom > l.Inner + 1) p.Add($"the foot is cut by {l.Bottom - l.Inner:0} px");
        if (l.GapBelow > 2) p.Add($"{l.GapBelow:0} px empty under the page");
        if (l.GapRight > 2) p.Add($"{l.GapRight:0} px empty right of the page");
        return p;
    }

    /// <summary>
    /// options-test (<c>KL_OPTIONS_ONLY=resize</c> alone), 1.0.18 (Steven, 2026-11-18: a drag "fucks up the size completely on
    /// everything"): a drag gives the pages room and width and never zooms; the top or bottom edge sets the room (a page longer
    /// than it scrolls, the Parser's graphs grow into it, the edge stops where the page has nothing more), a side the column's
    /// width (designed 662 to <see cref="MainWindow.MaxColumnWidth"/>; Full view holds); both kept; the zoom picked stays as
    /// it was and draws the same room bigger; nothing under the header, cut or left empty at any of it; a page switch starts
    /// at the top; Reset size and Ctrl+0 put 100%, the room and the width back.
    /// </summary>
    static void ResizeChecks(MainWindow w, Action<bool, string> check)
    {
        var screen = Environment.GetEnvironmentVariable("KL_SCREEN_H");
        Environment.SetEnvironmentVariable("KL_SCREEN_H", "1040");
        void Settle() { DoEvents(); DoEvents(); }
        string Problems() => string.Join("; ", ResizeProblems(w.LayoutForTest()));
        try
        {
            w.ShowDemo("Open world"); w.ResetSizeForTest(); w.ShowPartForTest("Farming"); Settle();
            double h0 = w.ActualHeight, w0 = w.ActualWidth;
            check(Problems() == "" && w.FitState.Scale == 1 && w.SizeForTest.Column == MainWindow.DesignWidth,
                $"resize, Farm: at its first size the page sits under the header, nothing cut or empty ({w0:0}x{h0:0}) {Problems()}");

            // the bottom edge up: shorter, the text the same size, the page scrolls in its area under the header; kept
            w.DragEdgeForTest(6, 0, -250, Settle); Settle();
            var lay = w.LayoutForTest();
            check(Math.Abs(w.ActualHeight - (h0 - 250)) < 3 && w.FitState.Scale == 1 && lay.Scrollable > 200 && Problems() == "" && w.SizeForTest.Room > 0,
                $"resize, Farm: the bottom edge 250 px up makes the window {h0 - w.ActualHeight:0} px shorter, never smaller text (zoom {w.FitState.Scale:P0}); the page scrolls {lay.Scrollable:0} px in its area, room {w.SizeForTest.Room:0} kept {Problems()}");
            // and down past the page: it stops where the page has nothing more to show (no empty strip under it)
            w.DragEdgeForTest(6, 0, 600, Settle); Settle();
            check(w.ActualHeight <= h0 + 2 && w.ActualHeight >= h0 - 2 && Problems() == "",
                $"resize, Farm: the bottom edge pulled 600 px down stops at the page's own height ({w.ActualHeight:0}, the page {h0:0}), nothing empty {Problems()}");

            // a side: the one column stretches, the text the same size, kept; it stops at the widest and never goes under the designed width
            w.ResetSizeForTest(); Settle();
            w.DragEdgeForTest(2, 240, 0, Settle); Settle();
            var s = w.SizeForTest;
            check(Math.Abs(s.Column - (MainWindow.DesignWidth + 240)) < 2 && Math.Abs(s.Width - s.Column) < 0.5 && Math.Abs(w.ActualWidth - (w0 + 240)) < 3 && w.FitState.Scale == 1 && Problems() == "",
                $"resize, Farm: the right edge 240 px out stretches the column to {s.Column:0} (kept {s.Width:0}), the window {w.ActualWidth - w0:0} px wider, zoom {w.FitState.Scale:P0} {Problems()}");
            w.DragEdgeForTest(2, 2000, 0, Settle); Settle();
            double widest = w.SizeForTest.Column;
            w.DragEdgeForTest(1, -2000, 0, Settle); Settle();
            check(widest == MainWindow.MaxColumnWidth && w.SizeForTest.Column == MainWindow.DesignWidth && Problems() == "",
                $"resize, Farm: a side stops at {widest:0} wide and at the designed {w.SizeForTest.Column:0} {Problems()}");

            // Full view holds its two columns: a side does nothing there
            w.ShowFull(); Settle();
            double fullW = w.ActualWidth;
            w.DragEdgeForTest(2, 300, 0, Settle); Settle();
            check(Math.Abs(w.ActualWidth - fullW) < 2 && Problems() == "", $"resize, Full view: a side keeps its two columns ({fullW:0} to {w.ActualWidth:0}) {Problems()}");
            w.ShowCompact(); Settle();

            // Parser: the graphs take the room; taller, they grow; nothing under the header at any zoom
            w.ResetSizeForTest(); Settle();
            w.ShowGraphsDemo("six"); Settle();
            w.DragEdgeForTest(6, 0, -300, Settle); Settle();   // shorter first, so the screen leaves room to grow
            var g = w.GraphsForTest;
            double dps0 = g?.ChartOf(ParserGraphs.Dps).ActualHeight ?? 0, ph0 = w.ActualHeight;
            w.DragEdgeForTest(6, 0, 220, Settle); Settle();
            double dps1 = g?.ChartOf(ParserGraphs.Dps).ActualHeight ?? 0;
            check(w.ActualHeight > ph0 + 150 && dps1 > dps0 + 40 && w.GraphsBottomForTest <= w.ParserViewportForTest + 1 && w.FitState.Scale == 1 && Problems() == "",
                $"resize, Parser: the bottom edge 220 px down, the window {w.ActualHeight - ph0:0} px taller and the DPS graph {dps0:0} to {dps1:0} px, the graphs fit, zoom {w.FitState.Scale:P0} {Problems()}");
            w.DragEdgeForTest(2, 200, 0, Settle); Settle();
            check(w.SizeForTest.Column >= MainWindow.DesignWidth + 198 && g!.ActualWidth > MainWindow.DesignWidth + 150 && Problems() == "",
                $"resize, Parser: a side stretches the page and its graphs ({g.ActualWidth:0} px wide) {Problems()}");
            foreach (var zoom in new[] { 0.8, 1.3, 1.5, 1.0 })
            {
                w.SetWindowScaleForTest(zoom); Settle();
                // at 150% a 1,040 px screen leaves the graphs less than their smallest: then the page scrolls in its area
                double dpsNow = g!.ChartOf(ParserGraphs.Dps).ActualHeight;
                bool fits = w.GraphsBottomForTest <= w.ParserViewportForTest + 1;
                check(Math.Abs(w.FitState.Scale - zoom) < 0.001 && Problems() == "" && (fits || dpsNow <= 113) && w.ActualHeight <= 1040.5,
                    $"resize, Parser at {zoom:P0}: the room drawn {w.FitState.Scale:P0}, the window {w.ActualWidth:0}x{w.ActualHeight:0} within the screen, the page under the header, its graphs {(fits ? "fitting" : $"at their smallest ({dpsNow:0} px) and the page scrolling")} {Problems()}");
            }

            // a page another one left scrolled starts at its top (the page area is one scroll the pages share)
            w.ResetSizeForTest(); w.ShowDemo("Events"); Settle();
            w.DragEdgeForTest(6, 0, -300, Settle); Settle();
            w.LowerScroll.ScrollToVerticalOffset(250); Settle();
            double runsOffset = w.LowerScroll.VerticalOffset;
            w.ShowGraphsDemo("six"); Settle();
            check(runsOffset > 100 && w.LowerScroll.VerticalOffset == 0 && Problems() == "",
                $"resize: Runs scrolled {runsOffset:0} px, then the Parser page starts at its top ({w.LowerScroll.VerticalOffset:0}) {Problems()}");

            // Runs: its cards follow the column
            w.ResetSizeForTest(); w.ShowDemo("Events"); Settle();
            w.DragEdgeForTest(2, 200, 0, Settle); Settle();
            var host = w.InstHost.Children.OfType<FrameworkElement>().FirstOrDefault(e => e.IsVisible);
            check(host != null && host.ActualWidth >= w.SizeForTest.Column - 4 && Problems() == "",
                $"resize, Runs: the place card {host?.ActualWidth:0} px in a {w.SizeForTest.Column:0} px column {Problems()}");

            // Ctrl+0 resets: 100%, the page's own height, the designed width
            w.SetWindowScaleForTest(1.3); w.DragEdgeForTest(6, 0, -200, Settle); Settle();
            w.StepWindowScaleForTest(0); Settle();
            s = w.SizeForTest;
            check(w.FitState.Scale == 1 && s.Room == 0 && s.Width == 0 && s.Column == MainWindow.DesignWidth && Problems() == "",
                $"resize: Ctrl+0 puts the size back: {w.FitState.Scale:P0}, room {s.Room:0}, width {s.Width:0}, column {s.Column:0}");

            // Options › General › Reset size: at once, the slider at 100%, nothing left to save
            w.ShowPartForTest("Farming"); w.DragEdgeForTest(6, 0, -200, Settle); Settle();
            var o = new OptionsWindow(w, OptionsWindow.Page.General) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -12000, Top = -12000, ShowActivated = false };
            o.Show(); DoEvents();
            o.WindowScale.Value = 1.2; DoEvents();
            o.WindowSizeResetBtn.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent)); DoEvents();
            s = w.SizeForTest;
            check(Math.Abs(o.WindowScale.Value - 1) < 0.001 && !o.SaveBtn.IsEnabled && w.FitState.Scale == 1 && s.Room == 0 && s.Width == 0,
                $"resize: Options' Reset size puts 100% back at once (slider {o.WindowScale.Value:P0}, Save {(o.SaveBtn.IsEnabled ? "on" : "off")}), room {s.Room:0}, width {s.Width:0}");
            o.Discard(); DoEvents();
        }
        finally
        {
            Environment.SetEnvironmentVariable("KL_SCREEN_H", screen);
            w.ResetSizeForTest(); w.ShowPartForTest("Farming"); DoEvents();
        }
    }
}
