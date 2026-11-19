using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using KinahLedger.Parser;

namespace KinahLedger.Ui;

public static partial class WpfApp
{
    /// <summary>The window sizes the Parser layout renders go through: a name, the zoom, the window's width and height (DIP, its frame included).</summary>
    internal static readonly (string Name, double Zoom, double W, double H)[] ParserLayoutSizes =
    {
        ("700x560", 1, 700, 560), ("700x800", 1, 700, 800), ("1000x700", 1, 1000, 700), ("zoom80", 0.8, 700, 560), ("zoom130", 1.3, 700, 560),
    };

    /// <summary>The Parser layout renders' states: a fight under way, a kill just made, a wipe held, a kill held five minutes on, an older pull opened, nothing kept.</summary>
    internal static readonly string[] ParserLayoutStates = { "live", "killed", "wipe", "held", "opened", "empty" };

    /// <summary>
    /// ui-shot DIR --parser layout (2026-11-18, Steven on 1.0.18's page: "like bro this is awful"): the Parser page in each of
    /// <see cref="ParserLayoutStates"/> at each of <see cref="ParserLayoutSizes"/> (layout-STATE-SIZE.png, the window; and
    /// layout-STATE-700x560-page.png, the page whole), with layout.txt saying for each the page's room, where the graph sits in
    /// it and how tall it is. A window that can be dragged to a size (the window-resize branch's DragEdgeForTest) is dragged to
    /// it; else it is set to it. KL_LANG=ko or zh-TW draws them in that language.
    /// </summary>
    static void ShotParserLayout(MainWindow w, string dir)
    {
        if (Environment.GetEnvironmentVariable("KL_SCREEN_H") == null) Environment.SetEnvironmentVariable("KL_SCREEN_H", "1040");
        var drag = typeof(MainWindow).GetMethod("DragEdgeForTest", BindingFlags.Instance | BindingFlags.NonPublic);
        var reset = typeof(MainWindow).GetMethod("ResetSizeForTest", BindingFlags.Instance | BindingFlags.NonPublic);
        var zoomTo = typeof(MainWindow).GetMethod("SetWindowScaleForTest", BindingFlags.Instance | BindingFlags.NonPublic);
        void Settle() { DoEvents(); DoEvents(); w.UpdateLayout(); DoEvents(); }
        void SizeTo(double width, double height, double zoom)
        {
            reset?.Invoke(w, null); Settle();
            if (drag != null)
            {
                Action mid = Settle;
                drag.Invoke(w, new object?[] { 6, 0.0, height - w.ActualHeight, mid }); Settle();
                drag.Invoke(w, new object?[] { 2, width - w.ActualWidth, 0.0, mid }); Settle();
                if (Math.Abs(zoom - 1) > 0.001) { zoomTo?.Invoke(w, new object[] { zoom }); Settle(); }
            }
            else { w.SizeToContent = SizeToContent.Manual; w.Width = width; w.Height = height; Settle(); }
        }
        void Show(string state)
        {
            switch (state)
            {
                case "live": w.ShowGraphsDemo("live70"); break;
                case "killed": w.ShowGraphsDemo("six"); w.HoldFightForTest("graphs-six", inInstance: true); w.KillPromptForTest(); break;
                case "wipe": w.ShowGraphsDemo("wipe"); w.HoldFightForTest("graphs-wipe", inInstance: true); break;
                case "held": w.ShowGraphsDemo("six"); w.HoldFightForTest("graphs-six", inInstance: false); break;
                case "opened": w.ShowGraphsDemo("killed"); break;
                case "empty": w.ShowParserDemo("empty"); break;
            }
        }
        var report = new List<string>();
        foreach (var (size, zoom, width, height) in ParserLayoutSizes)
            foreach (var st in ParserLayoutStates)
            {
                w.ResetGraphsForTest();
                Show(st); Settle();
                SizeTo(width, height, zoom);
                Show(st); Settle();
                w.LowerScroll.ScrollToTop(); Settle();
                var file = $"layout-{st}-{size}.png";
                Save((FrameworkElement)w.Content, Path.Combine(dir, file), 784);
                if (size == "700x560") SavePart(w.ParserPage, Path.Combine(dir, $"layout-{st}-{size}-page.png"), w.TryFindResource("Bg") as Brush);
                var g = w.GraphsForTest;
                double dps = g is { IsLoaded: true } && g.IsDescendantOf(w.LowerScroll) ? g.ChartOf(ParserGraphs.Dps).ActualHeight : 0;
                double top = g is { IsLoaded: true } && g.IsDescendantOf(w.LowerScroll) ? g.TranslatePoint(new Point(0, 0), w.LowerScroll).Y : -1;
                report.Add($"{file,-34} window {w.ActualWidth:0}x{w.ActualHeight:0} page room {w.ParserViewportForTest:0} graphs top {top:0} foot {(w.GraphsBottomForTest == double.MaxValue ? -1 : w.GraphsBottomForTest):0} DPS chart {dps:0} ({(w.ActualHeight > 0 ? dps / w.ActualHeight : 0):P0} of the window) page scrolls {w.ParserScrollableForTest:0}");
            }
        reset?.Invoke(w, null);
        File.WriteAllLines(Path.Combine(dir, "layout.txt"), report);
        foreach (var l in report) Console.WriteLine(l);
    }
}
