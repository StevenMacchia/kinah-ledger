using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace KinahLedger.Ui;

public static partial class WpfApp
{
    /// <summary>
    /// ui-shot DIR --parser graphs: the Parser page's Graphs tab in each state (MainWindow.GraphStates: ui-graphs-STATE.png, the
    /// window, and ui-graphs-STATE-page.png, the page whole), a hover on the six-member kill, and each graph's Copy picture
    /// (ui-graphs-picture-KIND.png). KL_LANG=ko or zh-TW draws them in that language.
    /// </summary>
    static void ShotGraphs(MainWindow w, string dir)
    {
        var got = new List<(BitmapSource Bmp, string File)>();
        MainWindow.ShareSinkForTest = (b, f) => got.Add((b, f));
        try
        {
            foreach (var st in MainWindow.GraphStates)
            {
                w.ShowGraphsDemo(st);
                DoEvents(); DoEvents();
                Save((FrameworkElement)w.Content, Path.Combine(dir, $"ui-graphs-{st}.png"), 800 - 16);
                SavePart(w.ParserPage, Path.Combine(dir, $"ui-graphs-{st}-page.png"), w.TryFindResource("Bg") as Brush);
                if (st == "six" && w.GraphsForTest is { } g)
                {
                    // a hover: every member's DPS at 1:10 on the DPS graph, the crosshair across the other two
                    g.ChartOf(ParserGraphs.Dps).ShowHover(70, true);
                    g.ChartOf(ParserGraphs.Hp).ShowHover(70, false); g.ChartOf(ParserGraphs.Heal).ShowHover(70, false);
                    DoEvents();
                    Save((FrameworkElement)w.Content, Path.Combine(dir, "ui-graphs-six-hover-dps.png"), 800 - 16);
                    g.ChartOf(ParserGraphs.Dps).ShowHover(null, false);
                    g.ChartOf(ParserGraphs.Hp).ShowHover(70, true); g.ChartOf(ParserGraphs.Heal).ShowHover(70, false);
                    DoEvents();
                    Save((FrameworkElement)w.Content, Path.Combine(dir, "ui-graphs-six-hover-hp.png"), 800 - 16);
                    g.ChartOf(ParserGraphs.Hp).ShowHover(null, false);
                    g.ChartOf(ParserGraphs.Heal).ShowHover(72, true);
                    DoEvents();
                    Save((FrameworkElement)w.Content, Path.Combine(dir, "ui-graphs-six-hover-heal.png"), 800 - 16);
                    g.ChartOf(ParserGraphs.Heal).ShowHover(null, false);
                }
            }
            // each graph's Copy picture (after the renders, so its toast is in none of them): the six-member kill's three, the live pull's DPS
            void Pictures(string st, params string[] kinds)
            {
                w.ShowGraphsDemo(st); DoEvents();
                foreach (var kind in kinds)
                {
                    got.Clear();
                    w.CopyGraphForTest(kind);
                    foreach (var (bmp, _) in got)
                    {
                        var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(bmp));
                        using var fs = File.Create(Path.Combine(dir, $"ui-graphs-picture-{st}-{kind}.png")); enc.Save(fs);
                    }
                }
            }
            Pictures("six", ParserGraphs.Kinds);
            Pictures("live", ParserGraphs.Dps, ParserGraphs.Hp);
            Pictures("solo", ParserGraphs.Hp);
        }
        finally { MainWindow.ShareSinkForTest = null; }
    }

    [DllImport("kernel32.dll")] static extern IntPtr GetCurrentThread();
    [DllImport("kernel32.dll")] static extern bool GetThreadTimes(IntPtr thread, out long created, out long exited, out long kernel, out long user);
    /// <summary>This thread's CPU time so far (kernel and user), in ms.</summary>
    static double UiThreadMs() { GetThreadTimes(GetCurrentThread(), out _, out _, out long k, out long u); return (k + u) / 10_000.0; }

    /// <summary>
    /// A live fight on the Parser page for <paramref name="seconds"/> of wall time: hits every 60 ms (the Parser's shortest
    /// frame, the most a fight redraws the page), each redrawing it as the window does. The process's CPU time (every thread:
    /// the window's, WPF's render thread), the window thread's own, the page's redraws and the graphs' own draws.
    /// </summary>
    static (double ProcMs, double UiMs, int Frames, int GraphRedraws, double GraphMs) LiveCpu(MainWindow w, string tab, double seconds)
    {
        w.ShowGraphsDemo("live");
        w.ParserTabForTest = tab;
        DoEvents();
        var proc = Process.GetCurrentProcess();
        proc.Refresh();
        double p0 = proc.TotalProcessorTime.TotalMilliseconds, u0 = UiThreadMs();
        int g0 = w.GraphsForTest?.Redraws ?? 0;
        double gm0 = ParserGraphs.DataMs + ParserGraphs.DrawMs;
        var clock = Stopwatch.StartNew();
        int frames = 0;
        var panel = w.GraphsForTest;
        while (clock.Elapsed.TotalSeconds < seconds)
        {
            double due = (frames + 1) * 60;
            w.GraphsLiveStepForTest(0.06);
            w.ParserDrawNowForTest();
            frames++;
            DoEvents();
            double wait = due - clock.Elapsed.TotalMilliseconds;
            if (wait > 0) Thread.Sleep((int)wait);
        }
        DoEvents();
        proc.Refresh();
        int redraws = tab == "Timeline" && w.GraphsForTest is { } g ? (ReferenceEquals(g, panel) ? g.Redraws - g0 : g.Redraws) : 0;
        return (proc.TotalProcessorTime.TotalMilliseconds - p0, UiThreadMs() - u0, frames, redraws, ParserGraphs.DataMs + ParserGraphs.DrawMs - gm0);
    }

    /// <summary>
    /// options-test (<c>KL_OPTIONS_ONLY=graphs</c> alone): the Parser page's Graphs tab. It is first and the default for a fight
    /// with seconds kept, absent for one without; each state draws the graphs its record has and only those (no empty chart);
    /// one line a member; the panel fits under the hero at the window's size, nothing cut; the hover reads every line; Copy
    /// picture gives each graph in the share frame; a live fight's graphs grow but are drawn at most every 250 ms; the CPU a
    /// live fight costs on the Graphs tab against the Group tab; every text in Korean and Chinese.
    /// </summary>
    static void GraphsChecks(Settings settings, Action<bool, string> check)
    {
        // the application stays up after this window closes: the languages part below draws in windows of its own
        var app = Ensure(); var shutdown = app.ShutdownMode;
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var w = new MainWindow(settings, watch: false) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -12000, Top = -12000, ShowActivated = false };
        check(w.ParserTabForTest == "Timeline", "graphs: the Graphs tab is a fight's view when the window opens");
        w.Show(); w.ShowDemo(); DoEvents();
        var got = new List<(BitmapSource Bmp, string File)>();
        try
        {
            // what each state draws: (DPS lines, the HP card's title, the heal card's title or null, deaths)
            var want = new Dictionary<string, (int Lines, string Hp, string? Heal, int Deaths)>
            {
                ["live"] = (6, "Boss HP and group DPS", "Healing and damage taken", 0),
                ["mid"] = (6, "Boss HP and group DPS", "Healing and damage taken", 1),
                ["killed"] = (4, "Boss HP and group DPS", "Healing", -1),
                ["wipe"] = (6, "Boss HP and group DPS", "Healing and damage taken", 6),
                ["solo"] = (1, "Boss HP", null, 0),
                ["six"] = (6, "Boss HP and group DPS", "Healing and damage taken", 1),
                ["noheal"] = (4, "Boss HP and group DPS", null, 0),
                ["real"] = (4, "Boss HP and group DPS", "Healing", -1),
            };
            foreach (var st in MainWindow.GraphStates)
            {
                w.ShowGraphsDemo(st); DoEvents(); DoEvents();
                var g = w.GraphsForTest;
                if (g?.Shown is not { } s || !g.IsVisible) { check(false, $"graphs, {st}: the Graphs view shows"); continue; }
                var (lines, hpTitle, healTitle, deaths) = want[st];
                bool hpCard = g.CardOf(ParserGraphs.Hp).IsVisible, healCard = g.CardOf(ParserGraphs.Heal).IsVisible;
                string hpT = ParserGraphs.Title(ParserGraphs.Hp, s), healT = ParserGraphs.Title(ParserGraphs.Heal, s);
                check(s.Members.Count == lines && s.Members.Count(m => m.You) == 1 && hpCard && hpT == hpTitle && healCard == (healTitle != null) && (healTitle == null || healT == healTitle)
                    && (deaths < 0 || s.Deaths.Count == deaths),
                    $"graphs, {st}: {s.Members.Count} DPS lines (yours one), \"{hpT}\"{(healCard ? $", \"{healT}\"" : ", no heal graph")}, {s.Deaths.Count} deaths marked, {s.N} s");
                // fits: the whole panel inside the window under the hero; nothing cut
                var bottom = w.GraphsBottomForTest;
                var cut = MeterShare.Trimmed(g);
                check(bottom <= w.ParserViewportForTest + 1 && cut.Count == 0 && g.ChartOf(ParserGraphs.Dps).ActualHeight >= 112,
                    $"graphs, {st}: the panel fits the window ({bottom:0} of {w.ParserViewportForTest:0} px, DPS chart {g.ChartOf(ParserGraphs.Dps).ActualHeight:0} px tall), nothing cut{(cut.Count > 0 ? ": " + string.Join(", ", cut) : "")}");
                var rows = g.ChartOf(ParserGraphs.Dps).HoverRows(s.N / 2);
                check(rows.Count(r => r.Value != "–") >= lines && s.Members.All(m => rows.Any(r => r.Name == m.Name)),
                    $"graphs, {st}: the hover at {PK.Clock(s.N / 2)} reads every member's DPS ({string.Join(", ", rows.Select(r => $"{r.Name} {r.Value}"))})");
            }
            // a record kept before the seconds were: no Graphs tab, the Group's table as before
            w.ShowGraphsDemo("noheal");
            var old = w.ParserShownForTest!;
            foreach (var f in old.Roster) { f.PerSecond = null; f.BossPerSecond = null; f.HealPerSecond = null; f.TakenPerSecond = null; }
            old.BossHpPerSecond = null;
            w.ParserTabForTest = "Timeline"; DoEvents();
            check(FindTagged(w.ParserPage, "parser-graphs") == null && FindTagged(w.ParserPage, "parser-member") != null,
                "graphs: a record without its seconds has no Graphs tab; the group's table shows");
            // Copy picture: each graph in the share frame, at the share pictures' width, named for the graph
            MainWindow.ShareSinkForTest = (b, f) => got.Add((b, f));
            w.ShowGraphsDemo("six"); DoEvents();
            foreach (var kind in ParserGraphs.Kinds)
                if (FindTagged(w.GraphsForTest!, "graph-copy-" + kind) is Button b) b.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            check(got.Count == 3 && got.All(x => x.Bmp.PixelWidth == ParserShare.PixelWidth && x.Bmp.PixelHeight > 500) && ParserGraphs.Kinds.All(k => got.Any(x => x.File.EndsWith("-graph-" + k + ".png", StringComparison.Ordinal))),
                $"graphs: each card's Copy picture copies that graph in the share frame ({string.Join(", ", got.Select(x => $"{x.File} {x.Bmp.PixelWidth}x{x.Bmp.PixelHeight}"))})");
            // live: the graphs grow a second at a time, drawn at most every 250 ms however often the page redraws
            w.ShowGraphsDemo("live"); DoEvents();
            var panel = w.GraphsForTest!;
            int n0 = panel.Shown!.N, r0 = panel.Redraws, frames = 0;
            var clock = Stopwatch.StartNew();
            while (clock.Elapsed.TotalSeconds < 3)
            {
                w.GraphsLiveStepForTest(0.25); w.ParserDrawNowForTest(); frames++; DoEvents(); Thread.Sleep(45);   // a quarter second of the fight a frame: it moves on while the wall clock does
            }
            DoEvents();
            double secs = clock.Elapsed.TotalSeconds;
            int redraws = panel.Redraws - r0;
            check(ReferenceEquals(w.GraphsForTest, panel) && panel.Shown!.N > n0 + 1 && redraws >= 2 && redraws <= secs * 1000 / ParserGraphs.LiveFrameMs + 1,
                $"graphs, live: the same view kept over {frames} page redraws in {secs:0.0} s; it grew from {n0} to {panel.Shown!.N} s, its graphs drawn {redraws} times (at most every {ParserGraphs.LiveFrameMs} ms)");
            // the CPU a live fight costs: the page on the Group tab (as 1.0.12 opened it) against the Graphs tab
            var before = LiveCpu(w, "Group", 10);
            var after = LiveCpu(w, "Timeline", 10);
            Console.WriteLine($"      CPU, 10 s of a live six-member fight, a page redraw every 60 ms:");
            Console.WriteLine($"      Group tab : {before.ProcMs:0} ms process ({before.ProcMs / 100:0.0}% of a core), {before.UiMs:0} ms window thread, {before.Frames} redraws ({before.UiMs / Math.Max(1, before.Frames):0.00} ms each)");
            Console.WriteLine($"      Graphs tab: {after.ProcMs:0} ms process ({after.ProcMs / 100:0.0}% of a core), {after.UiMs:0} ms window thread, {after.Frames} redraws ({after.UiMs / Math.Max(1, after.Frames):0.00} ms each), graphs drawn {after.GraphRedraws} times, {after.GraphMs:0} ms of it the graphs' own ({after.GraphMs / Math.Max(1, after.GraphRedraws):0.0} ms a draw)");
            check(after.UiMs <= before.UiMs * 1.35 + 150, $"graphs: a live fight on the Graphs tab costs the window thread about what the Group tab does ({after.UiMs:0} ms against {before.UiMs:0} ms in 10 s)");
        }
        finally { MainWindow.ShareSinkForTest = null; w.Close(); }

        // every text in Korean and Chinese: the graph states drawn in each, nothing English left, every L.T with an entry
        var dir = Path.Combine(Path.GetTempPath(), "KinahLedger-graphs-lang");
        var culture = CultureInfo.CurrentCulture; var ui = CultureInfo.CurrentUICulture;
        try
        {
            foreach (var code in new[] { "ko", "zh-TW" })
            {
                L.Init(code);
                LangCheck.Begin();
                Directory.CreateDirectory(Path.Combine(dir, code));
                int rc = ShotParser(settings, Path.Combine(dir, code), "graphs", null);
                var (onScreen, noEntry) = LangCheck.End();
                check(rc == 0 && onScreen.Count == 0 && noEntry.Count == 0,
                    $"graphs, {code}: drawn whole, no English left ({string.Join("; ", onScreen.Take(8))}), every text with an entry ({string.Join("; ", noEntry.Take(8))})");
            }
        }
        finally
        {
            L.Init("en");
            CultureInfo.CurrentCulture = culture; CultureInfo.CurrentUICulture = ui;
            app.ShutdownMode = shutdown;
            try { Directory.Delete(dir, true); } catch (Exception) { }
        }
    }
}
