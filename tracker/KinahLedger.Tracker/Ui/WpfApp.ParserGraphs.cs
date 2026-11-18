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
                    // a hover: every member's DPS and the boss's HP at 1:10 on the fight's graph, the crosshair on the healing's
                    g.ChartOf(ParserGraphs.Dps).ShowHover(70, true); g.ChartOf(ParserGraphs.Heal).ShowHover(70, false);
                    DoEvents();
                    Save((FrameworkElement)w.Content, Path.Combine(dir, "ui-graphs-six-hover-dps.png"), 800 - 16);
                    g.ChartOf(ParserGraphs.Dps).ShowHover(72, false);
                    g.ChartOf(ParserGraphs.Heal).ShowHover(72, true);
                    DoEvents();
                    Save((FrameworkElement)w.Content, Path.Combine(dir, "ui-graphs-six-hover-heal.png"), 800 - 16);
                    g.ChartOf(ParserGraphs.Heal).ShowHover(null, false); g.ChartOf(ParserGraphs.Dps).ShowHover(null, false);
                }
            }
            // the pen held (KL_MOTION_AT does it for every state): a kept fight drawing in at 40%, a live fight's heads mid-glide
            var at0 = Motion.FrozenAt;
            try
            {
                foreach (var (st, at, file) in new[] { ("six", 0.4, "ui-graphs-six-drawin-40.png"), ("wipe", 0.7, "ui-graphs-wipe-drawin-70.png"), ("live70", 0.5, "ui-graphs-live70-glide-50.png") })
                {
                    Motion.FrozenAt = at; w.ResetGraphsForTest(); w.ShowGraphsDemo(st); DoEvents(); DoEvents();
                    Save((FrameworkElement)w.Content, Path.Combine(dir, file), 800 - 16);
                }
            }
            finally { Motion.FrozenAt = at0; w.ResetGraphsForTest(); }
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
            Pictures("live120", ParserGraphs.Dps);
            Pictures("solo", ParserGraphs.Dps);
        }
        finally { MainWindow.ShareSinkForTest = null; }
    }

    [DllImport("kernel32.dll")] static extern IntPtr GetCurrentThread();
    [DllImport("kernel32.dll")] static extern bool GetThreadTimes(IntPtr thread, out long created, out long exited, out long kernel, out long user);
    /// <summary>This thread's CPU time so far (kernel and user), in ms.</summary>
    static double UiThreadMs() { GetThreadTimes(GetCurrentThread(), out _, out _, out long k, out long u); return (k + u) / 10_000.0; }

    /// <summary>Runs the window's dispatcher (input, layout, rendering and the frame tick, as the app would idle) until <paramref name="dueMs"/> on <paramref name="clock"/>.</summary>
    static void PumpUntil(Stopwatch clock, double dueMs)
    {
        double left = dueMs - clock.Elapsed.TotalMilliseconds;
        if (left <= 1) { DoEvents(); return; }
        var frame = new System.Windows.Threading.DispatcherFrame();
        var timer = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Send) { Interval = TimeSpan.FromMilliseconds(left) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        System.Windows.Threading.Dispatcher.PushFrame(frame);
    }

    /// <summary>
    /// A live fight on the Parser page for <paramref name="seconds"/> of wall time: a hit every <paramref name="hitMs"/> (60 ms:
    /// the Parser's shortest frame, the most a fight redraws the page), each redrawing it as the window does, the dispatcher
    /// running between them as the app idles. <paramref name="pen"/>: the graphs' pen plays (draw-in, gliding heads). The CPU
    /// time of the window thread, of WPF's render thread (the process's busiest other thread) and of the whole process; the
    /// page's redraws, the graphs' own draws and the pen's frames.
    /// </summary>
    static (double ProcMs, double UiMs, double RenderMs, int Frames, int GraphRedraws, double GraphMs, int PenFrames) LiveCpu(MainWindow w, string tab, double seconds, bool pen, double hitMs = 60)
    {
        ParserGraphs.Chart.ForceMotionForTest = pen;
        try
        {
            w.ResetGraphsForTest();
            w.ShowGraphsDemo("live24");
            w.ParserTabForTest = tab;
            DoEvents();
            var proc = Process.GetCurrentProcess();
            uint ui = GetCurrentThreadId();
            Dictionary<int, double> Threads() { proc.Refresh(); var d = new Dictionary<int, double>(); foreach (ProcessThread th in proc.Threads) { try { d[th.Id] = th.TotalProcessorTime.TotalMilliseconds; } catch (Exception) { } } return d; }
            var t0 = Threads();
            double p0 = proc.TotalProcessorTime.TotalMilliseconds, u0 = UiThreadMs();
            double gm0 = ParserGraphs.DataMs + ParserGraphs.DrawMs;
            int pf0 = ParserGraphs.Chart.Frames;
            var clock = Stopwatch.StartNew();
            int frames = 0;
            var panel = w.GraphsForTest;
            int g0 = panel?.Redraws ?? 0;
            while (clock.Elapsed.TotalSeconds < seconds)
            {
                w.GraphsLiveStepForTest(hitMs / 1000);
                w.ParserDrawNowForTest();
                frames++;
                PumpUntil(clock, frames * hitMs);
            }
            DoEvents();
            var t1 = Threads();
            proc.Refresh();
            double render = t1.Where(kv => kv.Key != ui && t0.ContainsKey(kv.Key)).Select(kv => kv.Value - t0[kv.Key]).DefaultIfEmpty(0).Max();
            int redraws = tab == "Timeline" && w.GraphsForTest is { } g ? (ReferenceEquals(g, panel) ? g.Redraws - g0 : g.Redraws) : 0;
            return (proc.TotalProcessorTime.TotalMilliseconds - p0, UiThreadMs() - u0, render, frames, redraws, ParserGraphs.DataMs + ParserGraphs.DrawMs - gm0, ParserGraphs.Chart.Frames - pf0);
        }
        finally { ParserGraphs.Chart.ForceMotionForTest = false; }
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
            // what each state draws: (DPS lines, the heal graph's title or null, deaths, the frame's length: live a fixed 3:00,
            // one step longer past it, a kept fight exactly its length (0))
            var want = new Dictionary<string, (int Lines, string? Heal, int Deaths, double Extent)>
            {
                ["live24"] = (6, "Healing and damage taken", 0, 180),
                ["live70"] = (6, "Healing and damage taken", 1, 180),
                ["live120"] = (6, "Healing and damage taken", 1, 180),
                ["over"] = (6, "Healing and damage taken", 1, 270),
                ["killed"] = (4, "Healing", -1, 0),
                ["wipe"] = (6, "Healing and damage taken", 6, 0),
                ["solo"] = (1, null, 0, 0),
                ["six"] = (6, "Healing and damage taken", 1, 0),
                ["noheal"] = (4, null, 0, 0),
                ["real"] = (4, "Healing", -1, 180),
            };
            var liveScales = new List<double>();
            foreach (var st in MainWindow.GraphStates)
            {
                w.ShowGraphsDemo(st); DoEvents(); DoEvents();
                var g = w.GraphsForTest;
                if (g?.Shown is not { } s || !g.IsVisible) { check(false, $"graphs, {st}: the Graphs view shows"); continue; }
                var (lines, healTitle, deaths, extent) = want[st];
                bool healCard = g.CardOf(ParserGraphs.Heal).IsVisible;
                string fightT = ParserGraphs.Title(ParserGraphs.Dps, s), healT = ParserGraphs.Title(ParserGraphs.Heal, s);
                bool frame = extent > 0 ? s.Extent == extent : Math.Abs(s.Extent - (s.N - 1)) < 0.01;
                if (st.StartsWith("live", StringComparison.Ordinal)) liveScales.Add(s.YMax);
                check(s.Members.Count == lines && s.Members.Count(m => m.You) == 1 && s.HpLeft != null && fightT == "DPS and boss HP" && healCard == (healTitle != null) && (healTitle == null || healT == healTitle)
                    && (deaths < 0 || s.Deaths.Count == deaths) && frame,
                    $"graphs, {st}: one graph, \"{fightT}\", {s.Members.Count} DPS lines (yours one) and the boss's HP{(healCard ? $"; \"{healT}\" under it" : "; no heal graph")}; {s.Deaths.Count} deaths marked; {s.N} s of data in a frame of {PK.Clock(s.Extent)}{(s.Live ? " (live, fixed)" : " (kept: start to finish)")}, DPS scale {s.YMax:N0}");
                // fits: the whole panel inside the window under the hero; nothing cut
                var bottom = w.GraphsBottomForTest;
                var cut = MeterShare.Trimmed(g);
                check(bottom <= w.ParserViewportForTest + 1 && cut.Count == 0 && g.ChartOf(ParserGraphs.Dps).ActualHeight >= 140,
                    $"graphs, {st}: the panel fits the window ({bottom:0} of {w.ParserViewportForTest:0} px, the fight's graph {g.ChartOf(ParserGraphs.Dps).ActualHeight:0} px tall), nothing cut{(cut.Count > 0 ? ": " + string.Join(", ", cut) : "")}");
                var rows = g.ChartOf(ParserGraphs.Dps).HoverRows(s.N / 2);
                check(rows.Count(r => r.Value != "–") >= lines + 1 && s.Members.All(m => rows.Any(r => r.Name == m.Name)) && rows.Any(r => r.Name == L.T("Boss HP")),
                    $"graphs, {st}: the hover at {PK.Clock(s.N / 2)} reads every member's DPS and the boss's HP ({string.Join(", ", rows.Select(r => $"{r.Name} {r.Value}"))})");
            }
            check(liveScales.Count == 3 && liveScales.Distinct().Count() == 1,
                $"graphs: a live fight's frame stands still: 0:24, 1:10 and 2:00 in, the same 3:00 and the same DPS scale ({string.Join(", ", liveScales.Select(x => x.ToString("N0")))})");            // a record kept before the seconds were: no Graphs tab, the Group's table as before
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
            check(got.Count == 2 && got.All(x => x.Bmp.PixelWidth == ParserShare.PixelWidth && x.Bmp.PixelHeight > 500) && ParserGraphs.Kinds.All(k => got.Any(x => x.File.EndsWith("-graph-" + k + ".png", StringComparison.Ordinal))),
                $"graphs: each card's Copy picture copies that graph in the share frame ({string.Join(", ", got.Select(x => $"{x.File} {x.Bmp.PixelWidth}x{x.Bmp.PixelHeight}"))})");
            // live: the graphs grow a second at a time, drawn at most every 250 ms however often the page redraws
            w.ShowGraphsDemo("live24"); DoEvents();
            var panel = w.GraphsForTest!;
            int n0 = panel.Shown!.N, r0 = panel.Redraws, frames = 0;
            double ext0 = panel.Shown!.Extent, y0 = panel.Shown!.YMax;
            var clock = Stopwatch.StartNew();
            while (clock.Elapsed.TotalSeconds < 3)
            {
                w.GraphsLiveStepForTest(0.25); w.ParserDrawNowForTest(); frames++; DoEvents(); Thread.Sleep(45);   // a quarter second of the fight a frame: it moves on while the wall clock does
            }
            DoEvents();
            double secs = clock.Elapsed.TotalSeconds;
            int redraws = panel.Redraws - r0;
            check(ReferenceEquals(w.GraphsForTest, panel) && panel.Shown!.N > n0 + 1 && redraws >= 2 && redraws <= secs * 1000 / ParserGraphs.LiveFrameMs + 1
                && panel.Shown!.Extent == ext0 && panel.Shown!.YMax == y0,
                $"graphs, live: the same view kept over {frames} page redraws in {secs:0.0} s; its lines grew from {n0} to {panel.Shown!.N} s in a frame that stood still ({PK.Clock(panel.Shown!.Extent)}, {panel.Shown!.YMax:N0}), its graphs drawn {redraws} times (at most every {ParserGraphs.LiveFrameMs} ms)");
            // the pen: a kept fight's first look draws in (once: a redraw of it doesn't), a live fight's heads glide to a newer
            // second, and with the window still (renders, Windows's animations off) nothing plays
            ParserGraphs.Chart.ForceMotionForTest = true;
            try
            {
                w.ResetGraphsForTest(); w.ShowGraphsDemo("six");
                var fight = w.GraphsForTest!.ChartOf(ParserGraphs.Dps);
                bool began = fight.Revealing;
                var wait = Stopwatch.StartNew();
                while (fight.Revealing && wait.ElapsedMilliseconds < 3000) PumpUntil(wait, wait.ElapsedMilliseconds + 20);
                double took = wait.ElapsedMilliseconds;
                w.ShowGraphsDemo("six"); DoEvents();
                bool again = w.GraphsForTest!.ChartOf(ParserGraphs.Dps).Revealing;
                w.ResetGraphsForTest(); w.ShowGraphsDemo("live24"); DoEvents();
                var live = w.GraphsForTest!; var lf = live.ChartOf(ParserGraphs.Dps);
                Thread.Sleep(ParserGraphs.LiveFrameMs + 30);
                w.GraphsLiveStepForTest(1.0); w.ParserDrawNowForTest();
                bool glides = lf.Gliding;
                wait.Restart();
                while (lf.Gliding && wait.ElapsedMilliseconds < 3000) PumpUntil(wait, wait.ElapsedMilliseconds + 20);
                double glideTook = wait.ElapsedMilliseconds;
                check(began && !fight.Revealing && took >= ParserGraphs.Chart.DrawInMs * 0.8 && took < 2500 && !again && glides && glideTook >= ParserGraphs.Chart.GlideMs * 0.8 && glideTook < 2500,
                    $"graphs: the pen draws a kept fight in on its first look ({took:0} ms), not on a redraw of it; a live line's head glides to its newest second ({glideTook:0} ms)");
            }
            finally { ParserGraphs.Chart.ForceMotionForTest = false; }
            w.ResetGraphsForTest(); w.ShowGraphsDemo("six");
            check(!w.GraphsForTest!.ChartOf(ParserGraphs.Dps).Revealing, "graphs: with the window still (a render, Windows's animations off) the graph shows drawn, no pen");
            // the CPU a live fight costs (10 s each, six members): the page on the Group tab (as 1.0.12 opened it), the Graphs
            // tab with the pen still, and with the pen drawing; then the pen alone, a quiet page redrawn once a second
            var group = LiveCpu(w, "Group", 10, pen: false);
            var still = LiveCpu(w, "Timeline", 10, pen: false);
            var drawn = LiveCpu(w, "Timeline", 10, pen: true);
            var quiet0 = LiveCpu(w, "Timeline", 10, pen: false, hitMs: 1000);
            var quiet1 = LiveCpu(w, "Timeline", 10, pen: true, hitMs: 1000);
            string Row(string what, (double ProcMs, double UiMs, double RenderMs, int Frames, int GraphRedraws, double GraphMs, int PenFrames) r) =>
                $"      {what,-34}: window thread {r.UiMs / 100:0.0}% of a core, render thread {r.RenderMs / 100:0.0}%, process {r.ProcMs / 100:0.0}%; {r.Frames} page redraws, graphs drawn {r.GraphRedraws}x ({r.GraphMs:0} ms), pen frames {r.PenFrames}";
            Console.WriteLine("      CPU, 10 s of a live six-member fight:");
            Console.WriteLine(Row("Group tab, a hit every 60 ms", group));
            Console.WriteLine(Row("Graphs, pen still, hit every 60 ms", still));
            Console.WriteLine(Row("Graphs, pen drawing, hit every 60 ms", drawn));
            Console.WriteLine(Row("Graphs, pen still, page once a second", quiet0));
            Console.WriteLine(Row("Graphs, pen drawing, page once a s", quiet1));
            double penCost = (quiet1.UiMs + quiet1.RenderMs - quiet0.UiMs - quiet0.RenderMs) / 100;
            Console.WriteLine($"      the pen's own cost (window + render thread, quiet page): {penCost:0.0}% of a core at {1000 / ParserGraphs.Chart.FrameMs:0} frames a second");
            check(still.UiMs <= group.UiMs * 1.35 + 150 && drawn.PenFrames > 100 && penCost <= 5,
                $"graphs: a live fight on the Graphs tab costs the window thread about what the Group tab does ({still.UiMs:0} ms against {group.UiMs:0} ms in 10 s); the pen drew {drawn.PenFrames} frames for {penCost:0.0}% of a core");
        }        finally { MainWindow.ShareSinkForTest = null; w.Close(); }

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
