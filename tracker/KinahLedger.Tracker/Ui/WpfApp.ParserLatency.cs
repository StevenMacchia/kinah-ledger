using System.Diagnostics;
using System.IO;
using System.Windows;
using KinahLedger.Packets;
using KinahLedger.Parser;
using KinahLedger.Session;

namespace KinahLedger.Ui;

public static partial class WpfApp
{
    /// <summary>
    /// parser-latency &lt;capture.pcapng&gt;... [--mode boss|run|fight] [--minutes N]: the Parser's capture-to-screen time over
    /// recordings played back at their own speed. Each recording's server stream goes through this build's decoder on a
    /// thread of its own, as the packet reader runs live: as fast as it can until its first fight event (so the decoder is
    /// in step), then at the recording's pace, each piece stamped with the moment it is fed (its "capture"). The events go
    /// to a Parser exactly as live (BossSink's path, the kill events' own); the window shows the Parser page and the overlay
    /// over it, and redraws as it does live. Prints how many events came and the median, 95th percentile and worst of
    /// capture to screen. Needs a decoder of interface version 4 or later beside the exe.
    /// </summary>
    public static int ParserLatencyReplay(Settings settings, List<string> files, string mode, double minutes, int port)
    {
        if (NativeDecoder.Version < 4) { Console.WriteLine($"this build's decoder reads no fights for the Parser (interface version {NativeDecoder.Version}); put a version 4 klnet.dll beside the exe"); return 1; }
        Ensure();
        var parser = new BossParser();
        var kept = new List<Encounter>();
        parser.Ended = kept.Add;
        parser.SetMode(BossParser.ModeOf(mode), DateTime.UtcNow);
        var w = new MainWindow(settings, watch: false) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -12000, Top = -12000, ShowActivated = false };
        w.Show();
        w.ShowParserReplay(parser, BossParser.ModeOf(mode));
        MainWindow.ParserLatencyStats.Clear();
        int bossEvents = 0, killEvents = 0, gaps = 0;
        double fedSeconds = 0;
        bool done = false;
        var feeder = new Thread(() =>
        {
            try
            {
                // --synthetic: a heavy boss pull at real time (8 members hitting every 0.1 s, the boss's HP after each fall), as the decoder sends a pull
                if (files.Count == 1 && files[0] == "synthetic")
                {
                    var t = DateTime.UtcNow;
                    parser.Take(new InstanceEntered(t, ContentKind.Transcendence, "Shattered Arkanis [Stage 3]", 1));
                    parser.Take(new GroupChanged(t, new[] { "Gladiator", "Cleric", "Templar", "Sorcerer", "Ranger", "Assassin", "Chanter", "Spiritmaster" }
                        .Select((c, i) => new GroupMember(i + 1, "Member" + (i + 1), c, 4_500 + i * 37, 5_600 + i * 5, i == 0)).ToList()));
                    parser.Take(new BossEngaged(DateTime.UtcNow, "Ruthilis of Pain", 99));
                    long hp = 400_000_000; var rnd = new Random(3); var sw2 = Stopwatch.StartNew();
                    double span = (minutes > 0 ? minutes : 2) * 60_000;
                    for (int k = 0; sw2.Elapsed.TotalMilliseconds < span; k++)
                    {
                        while (sw2.Elapsed.TotalMilliseconds < k * 100.0) Thread.Sleep(1);
                        var now = DateTime.UtcNow;
                        for (int m = 1; m <= 8; m++) { long a2 = rnd.Next(9_000, 15_000); hp -= a2; bossEvents++; parser.Take(new DamageDealt(now, m, 99, a2)); }
                        bossEvents++; parser.Take(new BossHealth(now, 99, Math.Max(1, hp), 400_000_000));
                        fedSeconds = sw2.Elapsed.TotalSeconds;
                    }
                    return;
                }
                foreach (var file in files)
                {
                    var segs = new List<(DateTime At, TcpSegment S)>();
                    foreach (var (at, frame, link) in Pcapng.Read(file))
                        if (NpcapSource.IpOf(frame, link) is { } ip && TcpSegment.Parse(ip) is { } s && s.SrcPort == port) segs.Add((at, s));
                    foreach (var conn in segs.GroupBy(x => (x.S.Src.ToString(), x.S.DstPort)).OrderBy(g => g.First().At))
                    {
                        using var dec = NativeDecoder.TryOpen()!;
                        bool live = false; DateTime start = default; var sw = new Stopwatch();
                        var st = new TcpStream
                        {
                            OnData = (d, _) =>
                            {
                                var now = DateTime.UtcNow;
                                dec.Feed(d, now);
                                while (dec.Next() is not null) { }
                                while (dec.NextItem() is not null) { }
                                while (dec.NextFight() is { } f) { killEvents++; live = true; TrackerSession.RawFight(parser, f); }
                                while (dec.NextBossEvent() is { } b) { bossEvents++; live = true; parser.Take(b); }
                            },
                            OnGap = () => { gaps++; dec.Gap(); parser.Take(new FeedGap(DateTime.UtcNow)); },
                        };
                        foreach (var (at, s) in conn)
                        {
                            if (live)
                            {
                                if (!sw.IsRunning) { sw.Start(); start = at; }
                                double due = (at - start).TotalMilliseconds;
                                if (minutes > 0 && due > minutes * 60_000) break;
                                // the recording's own pace: sleep to within 2 ms of the piece's time, then wait out the rest
                                while (sw.Elapsed.TotalMilliseconds < due - 2) Thread.Sleep(1);
                                while (sw.Elapsed.TotalMilliseconds < due) Thread.SpinWait(50);
                                fedSeconds = sw.Elapsed.TotalSeconds;
                            }
                            st.Add(s.Seq, s.Payload, DateTime.UtcNow, s.Syn);
                        }
                        Console.WriteLine($"{Path.GetFileName(Path.GetDirectoryName(Path.GetFullPath(file)))}: {st.Bytes:N0} bytes, played {sw.Elapsed.TotalSeconds:0} s at its own pace");
                    }
                }
            }
            catch (Exception ex) { Console.WriteLine("replay: " + ex.Message); }
            finally { done = true; }
        }) { IsBackground = true, Name = "packet reading (replay)", Priority = ThreadPriority.BelowNormal };
        feeder.Start();
        // the window's own loop runs meanwhile; the session's pass part (the stale check, a fight's end) each second, as live
        var frame = new System.Windows.Threading.DispatcherFrame();
        var tick = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        tick.Tick += (_, _) => { parser.Tick(DateTime.UtcNow); if (done) { tick.Stop(); frame.Continue = false; } };
        tick.Start();
        System.Windows.Threading.Dispatcher.PushFrame(frame);
        DoEvents();
        var (p50, p95, max, n) = MainWindow.ParserLatencyStats.Stats();
        Console.WriteLine($"events: {bossEvents:N0} boss events, {killEvents:N0} kill events, {gaps} gaps, over {fedSeconds:0} s at the recordings' pace; {BossParser.Name(parser.Mode)} kept {kept.Count} records");
        Console.WriteLine($"capture to screen (tab and overlay, {n:N0} redraws): median {p50:0} ms, 95th percentile {p95:0} ms, worst {max:0} ms (target under 100 ms)");
        Console.WriteLine($"the Parser said {MainWindow.ParserChangesSeen:N0} changes; the window drew {MainWindow.ParserDraws:N0} frames for them ({MainWindow.ParserDrawsShown:N0} on screen)");
        w.Close();
        return n > 0 && p95 < 100 ? 0 : 1;
    }
}
