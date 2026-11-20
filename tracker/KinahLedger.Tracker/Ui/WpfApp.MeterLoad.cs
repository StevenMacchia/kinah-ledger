using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using KinahLedger.Packets;
using KinahLedger.Parser;

namespace KinahLedger.Ui;

public static partial class WpfApp
{
    /// <summary>
    /// meter-load [--hud mono] [--scale 1.2] [--seconds 30] [--warm 5] [--party 5] [--every 150] [--main hidden|shown|parser]
    /// [--still] [--expanded] [--no-heals] [--separate]: the CPU the live meter costs, per thread. A party boss pull in an
    /// Expedition is fed to a Parser at real time from a thread of its own (each member hitting every --every ms, the boss's HP
    /// after each hit, the healers' heals), the way the packet reader feeds it live; the meter is drawn inside the Kinah
    /// overlay in the look given (off every screen, not still: its motion runs as live), Always on, by the window's own redraw
    /// path (a change's redraw, the meter's clock). After --warm seconds, --seconds are measured: each thread's CPU (the
    /// window's, WPF's render thread, the feeder, the collector...), the memory allocated, the redraws and frames. For
    /// comparing builds (2026-11-18: Steven's "super laggy" meter); run it with tests\hidden-run.ps1.
    /// </summary>
    public static int MeterLoad(Settings settings, Dictionary<string, string> opts)
    {
        Ensure();
        string Opt(string k, string d) => opts.TryGetValue(k, out var v) && v != "true" ? v : d;
        string look = Opt("hud", "mono");   // (as keys: the harness also builds into releases from before the Modern theme)
        double scale = double.Parse(Opt("scale", "1.2"), System.Globalization.CultureInfo.InvariantCulture);
        double seconds = double.Parse(Opt("seconds", "30"), System.Globalization.CultureInfo.InvariantCulture);
        double warm = double.Parse(Opt("warm", "5"), System.Globalization.CultureInfo.InvariantCulture);
        int party = int.Parse(Opt("party", "5"));
        int every = int.Parse(Opt("every", "150"));
        string main = Opt("main", "hidden");
        bool still = opts.ContainsKey("still"), heals = !opts.ContainsKey("no-heals");

        var parser = new BossParser();
        parser.SetMode(ParserMode.Bosses, DateTime.UtcNow);
        var w = new MainWindow(settings, watch: false) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -12000, Top = -12000, ShowActivated = false };
        w.HudInViewForTest = true;
        var prefs = w.ParserPrefsForTest;
        prefs.AlwaysOn = true; prefs.HealBars = heals; prefs.Expanded = opts.ContainsKey("expanded"); prefs.ClickThrough = true;
        prefs.Look = Opt("meter", "line");
        w.Show();
        if (main == "hidden") w.Hide();
        w.ShowParserReplay(parser, ParserMode.Bosses);
        if (main != "parser") w.ShowPartForTest("Home");
        if (opts.ContainsKey("separate")) prefs.InKinah = false;
        w.ApplyMeterExpandedForTest();
        var hud = new HudWindow(null, look, still: still) { Left = -12000, Top = -12000, ShowActivated = false };
        hud.SetScale(scale);
        // the Kinah line as the window has it, shown (the game in front: the window says "hidden" while it isn't)
        var values = w.HudValues.Count > 0 ? w.HudValues.ToDictionary(kv => kv.Key, kv => kv.Value)
            : new Dictionary<string, string> { ["feedText"] = "Spiritstone", ["feedValue"] = "+7.5K", ["feedRate"] = "1.27M", ["feedUnit"] = "an hour" };
        values["show"] = "";
        hud.Update(values);
        hud.Show();
        w.AttachHudForTest(hud);
        MainWindow.MeterDrawStats.Clear();

        int frames = 0; TimeSpan lastFrame = TimeSpan.Zero;
        EventHandler onFrame = (_, ev) => { var at = ((System.Windows.Media.RenderingEventArgs)ev).RenderingTime; if (at != lastFrame) { lastFrame = at; frames++; } };
        System.Windows.Media.CompositionTarget.Rendering += onFrame;

        bool done = false; uint feederTid = 0; long fed = 0;
        var feeder = new Thread(() =>
        {
            feederTid = GetCurrentThreadId();
            try
            {
                var t = DateTime.UtcNow;
                parser.Take(new InstanceEntered(t, ContentKind.Expedition, "Fire Temple", 1));
                string[] classes = { "Chanter", "Gladiator", "Cleric", "Ranger", "Sorcerer", "Templar", "Assassin", "Spiritmaster" };
                parser.Take(new GroupChanged(t, Enumerable.Range(0, party).Select(i => new GroupMember(i + 1, "Member" + (i + 1), classes[i % classes.Length], 4_500 + i * 37, 5_600 + i * 5, i == 0)).ToList()));
                parser.Take(new BossEngaged(DateTime.UtcNow, "Silver Blade Rotan", 99));
                long max = 2_000_000_000, hp = max; var rnd = new Random(3); var sw = Stopwatch.StartNew();
                double span = (warm + seconds + 2) * 1000;
                for (int k = 0; sw.Elapsed.TotalMilliseconds < span; k++)
                {
                    while (sw.Elapsed.TotalMilliseconds < k * (double)every) Thread.Sleep(1);
                    var now = DateTime.UtcNow;
                    for (int m = 1; m <= party; m++)
                    {
                        long a = rnd.Next(9_000, 15_000);
                        if (rnd.Next(60) == 0) a *= 5;
                        hp -= a; fed++;
                        parser.Take(new DamageDealt(now, m, 99, a));
                        parser.Take(new HitDealt(now, m, m, 99, a, 1000 + m, k, 0, false, false, rnd.Next(4) == 0 ? 3 : 1, 0, 0, 0));
                        if (heals && (m == 1 || m == 3) && rnd.Next(3) == 0)
                        {
                            long h = rnd.Next(2_000, 6_000);
                            parser.Take(new HealDone(now, m, 1 + rnd.Next(party), h, h * 3 / 4, 2000 + m, false, false, false, false));
                        }
                    }
                    parser.Take(new BossHealth(now, 99, Math.Max(1, hp), max));
                }
            }
            catch (Exception ex) { Console.WriteLine("feed: " + ex.Message); }
            finally { done = true; }
        }) { IsBackground = true, Name = "meter-load feed", Priority = ThreadPriority.BelowNormal };
        uint uiTid = GetCurrentThreadId();
        feeder.Start();

        Dictionary<int, (double Ms, string Label)>? before = null;
        long alloc0 = 0, sw0 = 0, swt0 = 0; int g0 = 0, g1 = 0, g2 = 0, frames0 = 0, draws0 = 0, clock0 = 0;
        var clock = Stopwatch.StartNew();
        double measured = 0;
        var frame = new System.Windows.Threading.DispatcherFrame();
        var tick = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        tick.Tick += (_, _) =>
        {
            parser.Tick(DateTime.UtcNow);
            if (before == null && clock.Elapsed.TotalSeconds >= warm)
            {
                before = Threads(uiTid, feederTid);
                alloc0 = GC.GetTotalAllocatedBytes(); g0 = GC.CollectionCount(0); g1 = GC.CollectionCount(1); g2 = GC.CollectionCount(2);
                frames0 = frames; draws0 = MainWindow.ParserDraws; clock0 = MainWindow.MeterClockDraws; sw0 = HudWindow.ShareWatchTicks; swt0 = HudWindow.ShareWatchTime;
                MainWindow.MeterDrawStats.Clear();
                measured = clock.Elapsed.TotalSeconds;
                var lv = w.ParserLiveForTest;
                Console.WriteLine($"  at the start: meter in the Kinah overlay {!w.ParserOverlaySeparate}, visible {hud.IsVisible} at opacity {hud.Opacity:0.00}, section shown {hud.ParserShown}, look's own fight {hud.LookFightForTest}, live {lv?.Live} ({lv?.Roster.Count} in it, {lv?.Title})");
            }
            else if (before != null && clock.Elapsed.TotalSeconds >= warm + seconds)
            {
                tick.Stop();
                double span = clock.Elapsed.TotalSeconds - measured;
                var after = Threads(uiTid, feederTid);
                var rows = after.Select(kv => (Label: kv.Value.Label, Ms: kv.Value.Ms - (before.TryGetValue(kv.Key, out var b) ? b.Ms : 0)))
                    .GroupBy(r => r.Label).Select(g => (Label: g.Key, Cores: g.Sum(r => r.Ms) / 1000 / span)).OrderByDescending(r => r.Cores).ToList();
                Console.WriteLine($"meter-load: hud {look} at {scale:0.##}, meter {prefs.Look}, party {party}, a hit each {every} ms a member, heals {heals}, still {still}, main {main}; measured {span:0.0} s");
                Console.WriteLine($"  process {rows.Sum(r => r.Cores):0.000} cores");
                foreach (var r in rows.Where(r => r.Cores >= 0.002)) Console.WriteLine($"  {r.Cores,6:0.000}  {r.Label}");
                var (d50, d95, dmax, dn) = MainWindow.MeterDrawStats.Stats();
                Console.WriteLine($"  allocated {(GC.GetTotalAllocatedBytes() - alloc0) / span / 1e6:0.0} MB/s; collections gen0 {GC.CollectionCount(0) - g0}, gen1 {GC.CollectionCount(1) - g1}, gen2 {GC.CollectionCount(2) - g2}");
                Console.WriteLine($"  redraws {(MainWindow.ParserDraws - draws0) / span:0.0}/s, clock draws {(MainWindow.MeterClockDraws - clock0) / span:0.0}/s, meter draw median {d50:0.00} ms, 95th {d95:0.00} ms, worst {dmax:0.0} ms ({dn} draws); frames {(frames - frames0) / span:0.0}/s; fed {fed:N0} hits");
                Console.WriteLine($"  Share watch: {(HudWindow.ShareWatchTicks - sw0) / span:0.0} ticks/s, {TimeSpan.FromTicks(HudWindow.ShareWatchTime - swt0).TotalMilliseconds / span:0.000} ms/s on the window's thread");
                frame.Continue = false;
            }
        };
        tick.Start();
        System.Windows.Threading.Dispatcher.PushFrame(frame);
        System.Windows.Media.CompositionTarget.Rendering -= onFrame;
        hud.Close();
        w.Close();
        return 0;
    }

    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    [DllImport("kernel32.dll")] static extern IntPtr OpenThread(uint access, bool inherit, uint id);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
    [DllImport("ntdll.dll")] static extern int NtQueryInformationThread(IntPtr h, int cls, out IntPtr info, int len, IntPtr ret);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern int GetThreadDescription(IntPtr h, out IntPtr desc);
    [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr p);

    /// <summary>
    /// copy-load [--w 3840] [--h 2160] [--fps 60] [--seconds 10] [--passes 1.7] [--gap 40] [--lazy] [--ocr]: the game window's
    /// capture, per thread, on a texture of the given size delivered --fps times a second while a reader takes a pass's six
    /// crops (--gap ms apart) --passes times a second. Without --lazy, each frame is copied as it comes (a GPU surface into a
    /// SoftwareBitmap, CreateCopyFromSurfaceAsync), as Ocr/WindowCapture.cs did until 2026-11-18; with --lazy, through
    /// WindowCapture itself, which crops on the graphics card for a read (1.0.31; whole frames only if the card can't). --ocr instead recognizes a chat-sized picture back to back, as the
    /// reader's passes do, to tell its threads apart. Needs no screen: runs under tests\hidden-run.ps1. (Windows Graphics Capture
    /// itself delivers no frames on hidden-run's desktop, nor for a window off every screen, so the capture is stood in for.)
    /// </summary>
    public static unsafe int CopyLoad(Dictionary<string, string> opts)
    {
        string Opt(string k, string d) => opts.TryGetValue(k, out var v) && v != "true" ? v : d;
        int wpx = int.Parse(Opt("w", "3840")), hpx = int.Parse(Opt("h", "2160"));
        double fps = double.Parse(Opt("fps", "60"), System.Globalization.CultureInfo.InvariantCulture);
        double seconds = double.Parse(Opt("seconds", "10"), System.Globalization.CultureInfo.InvariantCulture);
        bool ocr = opts.ContainsKey("ocr"), lazy = opts.ContainsKey("lazy");
        double passes = double.Parse(Opt("passes", "1.7"), System.Globalization.CultureInfo.InvariantCulture);
        int gap = int.Parse(Opt("gap", "40"));
        long n = 0;
        Action work;
        Func<System.Drawing.Rectangle, System.Drawing.Bitmap?>? crop = null;
        if (ocr)
        {
            var pic = new System.Drawing.Bitmap(864, 479);
            using (var g = System.Drawing.Graphics.FromImage(pic))
            {
                g.Clear(System.Drawing.Color.FromArgb(20, 20, 24));
                for (int i = 0; i < 18; i++) g.DrawString($"[Party] Member{i}: You have acquired [Silver Blade Rotan's Box] x{i + 1}", new System.Drawing.Font("Segoe UI", 11), System.Drawing.Brushes.White, 4, 4 + i * 26);
            }
            // --threads N: N reads at once, as a fight's passes overlap (each takes a recognizer of its own, up to the reader's 3)
            int threads = Math.Clamp(int.Parse(Opt("threads", "1")), 1, 8);
            var pics = Enumerable.Range(0, threads).Select(_ => (System.Drawing.Bitmap)pic.Clone()).ToArray();   // GDI+ pictures are one thread's at a time
            work = () =>
            {
                if (threads == 1) { KinahLedger.Ocr.ScreenReader.ReadBitmapAsync(pic, boost: false, scale: 2).GetAwaiter().GetResult(); return; }
                Task.WaitAll(pics.Select(q => Task.Run(() => KinahLedger.Ocr.ScreenReader.ReadBitmapAsync(q, boost: false, scale: 2))).ToArray());
            };
        }
        else
        {
            int hr = D3D11CreateDevice(IntPtr.Zero, 1, IntPtr.Zero, 0x20, IntPtr.Zero, 0, 7, out var dev, out _, out var ctx);
            if (hr < 0) { Console.WriteLine($"no Direct3D device ({hr:X8})"); return 1; }
            var desc = stackalloc uint[11] { (uint)wpx, (uint)hpx, 1, 1, 87 /* B8G8R8A8_UNORM */, 1, 0, 0 /* DEFAULT */, 0x28 /* shader resource | render target */, 0, 0 };
            var create = ((delegate* unmanaged[Stdcall]<IntPtr, uint*, IntPtr, out IntPtr, int>*)(*(IntPtr*)dev))[5];
            Marshal.ThrowExceptionForHR(create(dev, desc, IntPtr.Zero, out var tex));
            var sid = new Guid("cafcb56c-6ac3-4889-bf47-9e23bbd260ec");
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(tex, ref sid, out var dxgiSurface));
            Marshal.ThrowExceptionForHR(CreateDirect3D11SurfaceFromDXGISurface(dxgiSurface, out var insp));
            var surface = WinRT.MarshalInterface<Windows.Graphics.DirectX.Direct3D11.IDirect3DSurface>.FromAbi(insp);
            if (lazy)
            {
                // the capture as it is now: each frame only kept (Ocr/WindowCapture.cs), copied when a read asks
                KinahLedger.Ocr.WindowCapture.ResetForTest();
                work = () => KinahLedger.Ocr.WindowCapture.OfferForTest(surface);
                crop = r => KinahLedger.Ocr.WindowCapture.CropForTest(r);
            }
            else
            {
                Windows.Graphics.Imaging.SoftwareBitmap? last = null;
                var gate = new object();
                // OnFrame's work up to 1.0.11, as it was: each frame copied, waited for, kept as the newest (the one before it freed)
                work = () =>
                {
                    var bmp = Windows.Graphics.Imaging.SoftwareBitmap.CreateCopyFromSurfaceAsync(surface, Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied).AsTask().GetAwaiter().GetResult();
                    lock (gate) { last?.Dispose(); last = bmp; }
                };
                crop = r => { lock (gate) return last == null ? null : KinahLedger.Ocr.WindowCapture.CropOfForTest(last, r); };
            }
        }
        work();   // warm
        uint me = GetCurrentThreadId();
        // the reader's passes (--passes a second): its crops one after another, as a pass takes them (the chat, the loot list,
        // the target, the balance, the grid, the scene), each --gap ms apart
        int crops = 0; bool stop = false; uint readerTid = 0; double cropMs = 0, cropMax = 0;
        var reader = new Thread(() =>
        {
            readerTid = GetCurrentThreadId();
            var rs = new[] { new System.Drawing.Rectangle(693, 1514, 864, 479), new System.Drawing.Rectangle(2900, 900, 600, 560), new System.Drawing.Rectangle(1500, 40, 840, 90),
                new System.Drawing.Rectangle(3300, 2080, 400, 40), new System.Drawing.Rectangle(2600, 600, 900, 900), new System.Drawing.Rectangle(0, 0, 640, 360) };
            var rsw = Stopwatch.StartNew();
            for (int k = 1; !stop; k++)
            {
                foreach (var r in rs) { long c0 = Stopwatch.GetTimestamp(); using var b = crop?.Invoke(r); double cm = Stopwatch.GetElapsedTime(c0).TotalMilliseconds; cropMs += cm; cropMax = Math.Max(cropMax, cm); if (b != null) Interlocked.Increment(ref crops); Thread.Sleep(gap); }
                while (!stop && rsw.Elapsed.TotalMilliseconds < k * 1000 / passes) Thread.Sleep(5);
            }
        }) { IsBackground = true, Name = "copy-load reader" };
        if (!ocr) reader.Start();
        var before = Threads(me, 0);
        long copies0 = KinahLedger.Ocr.WindowCapture.Copies;
        var sw = Stopwatch.StartNew();
        double busy = 0;
        while (sw.Elapsed.TotalSeconds < seconds)
        {
            var t0 = sw.Elapsed.TotalMilliseconds;
            work(); n++;
            busy += sw.Elapsed.TotalMilliseconds - t0;
            if (!ocr) { double due = n * 1000 / fps; while (sw.Elapsed.TotalMilliseconds < due) Thread.Sleep(1); }
        }
        double span = sw.Elapsed.TotalSeconds;
        var after = Threads(me, 0);
        stop = true;
        var rows = after.Select(kv => (Label: kv.Key == me ? "frame arrivals (OnFrame's thread)" : kv.Key == readerTid ? "the reader's crops" : kv.Value.Label, Ms: kv.Value.Ms - (before.TryGetValue(kv.Key, out var b) ? b.Ms : 0)))
            .GroupBy(r => r.Label).Select(g => (Label: g.Key, Cores: g.Sum(r => r.Ms) / 1000 / span)).OrderByDescending(r => r.Cores).ToList();
        Console.WriteLine(ocr ? $"copy-load --ocr: {n / span:0.0} recognitions a second, {busy / n:0.0} ms each, {KinahLedger.Ocr.ScreenReader.Engines} recognizers, private bytes {Process.GetCurrentProcess().PrivateMemorySize64 / 1e6:0} MB"
            : $"copy-load{(lazy ? " --lazy" : "")}: a {wpx}x{hpx} frame {n / span:0.0} times a second ({busy / n:0.0} ms each on arrival), {crops / span:0.0} crops a second ({passes:0.#} passes), " +
              (lazy ? $"{(KinahLedger.Ocr.WindowCapture.Copies - copies0) / span:0.0} whole frames copied a second, " +
                      $"{KinahLedger.Ocr.WindowCapture.CardBytes / 1024.0 / Math.Max(1, KinahLedger.Ocr.WindowCapture.CardReads):0} KB read back per crop on the card" : "every frame copied"));
        if (!ocr) Console.WriteLine($"  a crop takes {cropMs / Math.Max(1, crops):0.0} ms (longest {cropMax:0.0} ms); private bytes {Process.GetCurrentProcess().PrivateMemorySize64 / 1e6:0} MB");
        Console.WriteLine($"  process {rows.Sum(r => r.Cores):0.000} cores, working set {Process.GetCurrentProcess().WorkingSet64 / 1e6:0} MB");
        foreach (var r in rows.Where(r => r.Cores >= 0.002)) Console.WriteLine($"  {r.Cores,6:0.000}  {r.Label}");
        return 0;
    }

    /// <summary>
    /// event-age &lt;capture.pcapng&gt; [--port 13328]: how old each kind of the Parser's events is when the decoder hands it over,
    /// by the recording's own clock (the packet that brought it out, against the time the event says), and how old the time is
    /// that the Parser's change carries (what the window's capture-to-screen line measures from). At full speed, no window.
    /// </summary>
    public static int EventAge(string pcapng, int port)
    {
        if (NativeDecoder.Version < 4) { Console.WriteLine($"this build's decoder reads no fights (interface version {NativeDecoder.Version})"); return 1; }
        var p = new BossParser();
        p.SetMode(ParserMode.Bosses, DateTime.MinValue.AddYears(2000));
        var ages = new SortedDictionary<string, List<double>>();
        DateTime fed = DateTime.MinValue;
        void Age(string k, DateTime at) { if (!ages.TryGetValue(k, out var l)) ages[k] = l = new(); l.Add((fed - at).TotalMilliseconds); }
        // the window's redraws as live (Ui/MainWindow.Parser.cs): a change draws at once, or when the last draw is 60 ms old,
        // measured from the oldest time the changes in it carried; "event's own time" is what that line measured before 2026-11-18
        DateTime taking = DateTime.MinValue, drawAt = DateTime.MinValue;   // drawAt: when the pending redraw is drawn (the last one's, once drawn)
        DateTime? oldestNow = null, oldestOwn = null;
        var drawsNow = new List<double>(); var drawsOwn = new List<double>();
        void Draw()
        {
            if (oldestNow is { } n) drawsNow.Add(Math.Max(0, (drawAt - n).TotalMilliseconds));
            if (oldestOwn is { } o) drawsOwn.Add(Math.Max(0, (drawAt - o).TotalMilliseconds));
            oldestNow = oldestOwn = null;
        }
        p.Changed += at =>
        {
            if (at is not { } a) return;
            Age("~ the Parser's change", a);
            if (oldestNow != null && fed >= drawAt) Draw();   // the pending redraw has been drawn by now
            if (oldestNow == null) drawAt = fed >= drawAt.AddMilliseconds(60) ? fed : drawAt.AddMilliseconds(60);
            if (oldestNow == null || a < oldestNow) oldestNow = a;
            if (oldestOwn == null || taking < oldestOwn) oldestOwn = taking;
        };
        var segs = new List<(DateTime At, TcpSegment S)>();
        foreach (var (at, frame, link) in Pcapng.Read(pcapng))
            if (NpcapSource.IpOf(frame, link) is { } ip && TcpSegment.Parse(ip) is { } s && s.SrcPort == port) segs.Add((at, s));
        using var dec = NativeDecoder.TryOpen()!;
        foreach (var conn in segs.GroupBy(x => (x.S.Src.ToString(), x.S.DstPort)).OrderBy(g => g.First().At))
        {
            dec.Reset();
            var st = new TcpStream
            {
                OnData = (d, at) =>
                {
                    fed = at;
                    dec.Feed(d, at);
                    while (dec.Next() is not null) { }
                    while (dec.NextItem() is not null) { }
                    while (dec.NextFight() is { } f) { Age("FightEvent " + f.Kind, f.At); KinahLedger.Session.TrackerSession.RawFight(p, f); }
                    while (dec.NextBossEvent() is { } b) { Age(b.GetType().Name, b.At); taking = b.At; p.Take(b); }
                    while (dec.NextHit() is { } h) { Age(h is HealDone hd ? $"HealDone{(hd.Estimate ? " (estimate)" : "")}{(hd.Tick ? " (tick)" : "")}" : h.GetType().Name, h.At); taking = h.At; p.Take(h); }
                    while (dec.NextAreaEvent() is not null) { }
                },
                OnGap = () => { dec.Gap(); p.Take(new FeedGap(fed)); },
            };
            foreach (var (at, s) in conn) { st.Add(s.Seq, s.Payload, at, s.Syn); p.Tick(at); }
        }
        Console.WriteLine($"event-age {Path.GetFileName(Path.GetDirectoryName(Path.GetFullPath(pcapng)))}: age when handed over, ms (the packet that brought it out against the event's own time)");
        foreach (var (k, l) in ages)
        {
            l.Sort();
            double P(double q) => l[Math.Min(l.Count - 1, (int)(q * l.Count))];
            int over = l.Count(x => x >= 500);
            Console.WriteLine($"  {k,-34} {l.Count,8:N0}  median {P(0.5),6:0}  95th {P(0.95),6:0}  99th {P(0.99),6:0}  worst {l[^1],6:0}  over 500 ms {over * 100.0 / l.Count:0.0}%");
        }
        Draw();
        foreach (var (k, l) in new[] { ("redraws, the event's own time", drawsOwn), ("redraws, as measured now", drawsNow) })
        {
            if (l.Count == 0) continue;
            var a = l.ToArray(); Array.Sort(a);
            Console.WriteLine($"  {k,-34} {a.Length,8:N0}  median {ParserLatency.Pct(a, 0.5),6:0}  95th {ParserLatency.Pct(a, 0.95),6:0}  worst {a[^1],6:0}  (the decoder's and the batching's part; the window's own draw comes on top)");
        }
        return 0;
    }

    [DllImport("d3d11.dll", ExactSpelling = true)]
    static extern int D3D11CreateDevice(IntPtr adapter, int driverType, IntPtr software, uint flags, IntPtr featureLevels, uint levels, uint sdk, out IntPtr device, out int level, out IntPtr context);
    [DllImport("d3d11.dll", ExactSpelling = true)]
    static extern int CreateDirect3D11SurfaceFromDXGISurface(IntPtr dxgiSurface, out IntPtr graphicsSurface);

    /// <summary>This process's threads: CPU so far (ms) and a label (the window's, the feeder, else the module it started in and its name).</summary>
    static Dictionary<int, (double Ms, string Label)> Threads(uint ui, uint feeder)
    {
        var p = Process.GetCurrentProcess();
        var mods = p.Modules.Cast<ProcessModule>().Select(m => (m.ModuleName, Base: (long)m.BaseAddress, End: (long)m.BaseAddress + m.ModuleMemorySize)).ToList();
        var d = new Dictionary<int, (double, string)>();
        foreach (ProcessThread t in p.Threads)
        {
            string label;
            if (t.Id == ui) label = "UI thread (the window's)";
            else if (t.Id == feeder) label = "feeder (the packet reader's stand-in)";
            else
            {
                var h = OpenThread(0x0800 | 0x0040, false, (uint)t.Id);
                long addr = 0; string? name = null;
                if (h != IntPtr.Zero)
                {
                    if (NtQueryInformationThread(h, 9, out var a, IntPtr.Size, IntPtr.Zero) == 0) addr = a.ToInt64();
                    if (GetThreadDescription(h, out var s) >= 0 && s != IntPtr.Zero) { name = Marshal.PtrToStringUni(s); LocalFree(s); }
                    CloseHandle(h);
                }
                var mod = mods.FirstOrDefault(m => addr >= m.Base && addr < m.End).ModuleName ?? "?";
                label = mod.StartsWith("wpfgfx", StringComparison.OrdinalIgnoreCase) ? "WPF render thread (wpfgfx)" : name is { Length: > 0 } ? $"{mod} '{name}'" : mod;
                if (label.Contains("TP Worker")) label = ".NET thread pool";
            }
            try { d[t.Id] = (t.TotalProcessorTime.TotalMilliseconds, label); } catch (Exception) { }
        }
        return d;
    }
}

public sealed partial class HudWindow
{
    internal string LookFightForTest => _look is HudStackLook sl ? (sl.FightFrame != null ? "shown" : "none") + " (" + _look.GetType().Name + ")" : _look?.GetType().Name ?? "classic";
}

public partial class MainWindow
{
    internal ParserPrefs ParserPrefsForTest => _parserPrefs;
    /// <summary>meter-load: the Kinah overlay in view as if the game were in front (no game runs there).</summary>
    internal bool HudInViewForTest { get; set; }
    internal void ApplyMeterExpandedForTest() => ApplyMeterExpanded();
}
