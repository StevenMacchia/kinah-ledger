using System.Text;
using KinahLedger.Capture;
using KinahLedger.Data;
using KinahLedger.Ocr;
using KinahLedger.Protocol;
using KinahLedger.Session;
using KinahLedger.Ui;
using KinahLedger.Upload;

namespace KinahLedger;

internal static class Program
{
    [System.Runtime.InteropServices.DllImport("kernel32.dll")] static extern bool AttachConsole(int pid);
    [System.Runtime.InteropServices.DllImport("kernel32.dll")] static extern bool AllocConsole();
    [System.Runtime.InteropServices.DllImport("kernel32.dll")] static extern IntPtr GetStdHandle(int which);

    [STAThread]
    static int Main(string[] args)
    {
        var settings = Settings.Load();
        if (args.Length == 0)
        {
            // double-clicked: open the window
            return WpfApp.Run(settings);
        }
        // command line: write to the terminal that started us; if there's none and no pipe either, open one
        if (!AttachConsole(-1) && GetStdHandle(-11) == IntPtr.Zero) AllocConsole();
        var cmd = args[0].ToLowerInvariant();
        var (opts, pos) = ParseArgs(args.Skip(1));
        if (opts.TryGetValue("process", out var hints))
            settings.ProcessHints = hints.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        try
        {
            switch (cmd)
            {
                case "pick": return Pick();
                case "ui-shot": return UiShot(settings, opts, pos);
                case "ui-cycle": return WpfApp.Cycle(settings, RegionFor(opts), int.TryParse(opts.GetValueOrDefault("ms"), out var ms) ? ms : 4000);
                case "ui-live": return WpfApp.Live(settings, RegionFor(opts), int.TryParse(opts.GetValueOrDefault("ms"), out var lms) ? lms : 4000);
                case "parse-test": return ParseTest(settings);
                case "ocr-test": return OcrTest(settings, opts);
                case "watch": return Watch(settings, opts);
                case "devices":
                    foreach (var d in PacketSource.Devices()) Console.WriteLine(d);
                    return 0;
                case "connections": return Connections(settings);
                case "record": return Record(settings, opts);
                case "run": return Run(settings, opts);
                case "dump": return Dump(opts, pos);
                case "reset-id":
                    InstallId.Reset();
                    Console.WriteLine("Install ID removed. A new one is created on the next upload.");
                    return 0;
                case "help": Help(); return 0;
                default:
                    Console.Error.WriteLine($"unknown command '{cmd}'");
                    Help();
                    return 1;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("error: " + ex.Message);
            return 2;
        }
    }

    static void Help() => Console.WriteLine(
@"Kinah Ledger tracker 0.2

Screen reading (works today, no setup beyond picking the chat area):
  pick                          drag a box around the chat's loot lines; remembered for next time
  ocr-test [--save]             read the box once and show what was recognized (--save keeps a PNG)
  watch [--activity name]       live tracker with an on-screen panel; Ctrl+C to stop
        [--region x,y,w,h] [--interval 700] [--no-hud]

Packet capture (needs Npcap; decodes nothing until opcodes.json is mapped):
  devices                       list capture adapters (needs Npcap)
  connections                   show the game's live server connections
  record [--out file.klrec]     capture the game's traffic to a file; type a note + Enter to mark what you just did
         [--seconds N]          unattended: capture for N seconds with no notes
  run                           live tracker; decodes only what opcodes.json maps
  dump <file.klrec> [--around 4] [--bytes 48]
                                print the traffic around each note in a recording
         [--first N]            print the first N chunks instead
  reset-id                      replace the random install ID used for uploads

options: --process aion[,other]   --device <adapter name>");

    static (Dictionary<string, string>, List<string>) ParseArgs(IEnumerable<string> args)
    {
        var o = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var p = new List<string>();
        string? key = null;
        foreach (var s in args)
        {
            if (s.StartsWith("--")) { if (key != null) o[key] = "true"; key = s[2..]; }
            else if (key != null) { o[key] = s; key = null; }
            else p.Add(s);
        }
        if (key != null) o[key] = "true";
        return (o, p);
    }

    static int Connections(Settings s)
    {
        var c = GameConnectionFinder.Find(s.ProcessHints);
        if (c.Count == 0)
        {
            Console.WriteLine($"No established connections from a process matching [{string.Join(", ", s.ProcessHints)}].");
            Console.WriteLine("Is the game running? If its process has another name, pass --process <name> (see Task Manager > Details).");
            return 1;
        }
        foreach (var x in c)
            Console.WriteLine($"{x.Process,-20} pid {x.Pid,-7} {x.Local,-22} -> {x.Remote,-22} {(s.IgnorePorts.Contains(x.Remote.Port) ? "(ignored: web/launcher port)" : "game server")}");
        return 0;
    }

    static List<GameConnection> FindGame(Settings s) =>
        GameConnectionFinder.Find(s.ProcessHints).Where(x => !s.IgnorePorts.Contains(x.Remote.Port)).ToList();

    static List<GameConnection> WaitForGame(Settings s)
    {
        var c = FindGame(s);
        if (c.Count == 0) Console.WriteLine($"Waiting for a process matching [{string.Join(", ", s.ProcessHints)}] with a game server connection (ports {string.Join(",", s.IgnorePorts)} ignored)...");
        while (c.Count == 0) { Thread.Sleep(2000); c = FindGame(s); }
        Console.WriteLine($"Following {c.Count} connection(s) of {c[0].Process}: {string.Join(", ", c.Select(x => x.Remote.ToString()))}");
        return c;
    }

    static int Record(Settings s, Dictionary<string, string> o)
    {
        var conns = WaitForGame(s);
        using var src = new PacketSource(conns, o.GetValueOrDefault("device"));
        var path = o.GetValueOrDefault("out") ?? $"capture-{DateTime.Now:yyyyMMdd-HHmmss}.klrec";
        using var rec = new FrameRecorder(path);
        var asm = new TcpReassembler();
        src.Segment += asm.Push;
        asm.Data += rec.Write;
        src.Start();

        Console.WriteLine($"Recording to {path} via {src.DeviceName}.");
        var stop = false;
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop = true; };
        using var ticker = new System.Threading.Timer(_ =>
        {
            try { Console.Title = $"Kinah Ledger  recording  {rec.Records} chunks  {rec.Bytes / 1024} KB  lost {asm.Lost} B"; } catch { /* no console title */ }
        }, null, 1000, 1000);

        if (o.TryGetValue("seconds", out var secStr) && int.TryParse(secStr, out var seconds))
        {
            // unattended capture: no notes, just run for a fixed time
            Console.WriteLine($"Capturing for {seconds} seconds...");
            for (int i = 0; i < seconds && !stop; i++) Thread.Sleep(1000);
        }
        else
        {
            Console.WriteLine("Type what you just did and press Enter to add a note, e.g. 'looted 2 iron ore' or 'picked up 1,200 kinah'. Ctrl+C to stop.");
            while (!stop)
            {
                var line = Console.ReadLine();
                if (line == null) break;
                if (line.Trim().Length > 0) { rec.Marker(line.Trim()); Console.WriteLine("  noted"); }
            }
        }
        src.Stop();
        rec.Flush();
        Console.WriteLine($"Saved {path}: {rec.Records} chunks, {rec.Bytes / 1024} KB, {asm.Lost} bytes lost to gaps.");
        return 0;
    }

    static int Run(Settings s, Dictionary<string, string> o)
    {
        var items = ItemDictionary.LoadOrEmpty(s.ItemsFile);
        var table = OpcodeTable.Load(s.OpcodesFile);
        var zones = ZoneMap.Load(s.ZonesFile);
        Console.WriteLine($"Items known: {items.Count}. Opcodes mapped: {table.Opcodes.Count}.");
        if (table.IsEmpty) Console.WriteLine("opcodes.json is empty, so nothing is decoded yet. Use `record` to help map the protocol.");
        if (s.Upload.Enabled && !string.IsNullOrWhiteSpace(s.Upload.Endpoint))
            Console.WriteLine($"When you stop, this run's totals go anonymously to {s.Upload.Endpoint}: region, activity, gear bracket, minutes, kills, kinah, item counts and a random install ID. Set upload.enabled to false in settings.json to keep runs on this PC.");

        var conns = WaitForGame(s);
        using var src = new PacketSource(conns, o.GetValueOrDefault("device"));
        var asm = new TcpReassembler();
        var framer = Framer.FromSettings(s.Framing);
        var decoder = new TableDecoder(table);
        var engine = new SessionEngine(items);
        long frames = 0;

        src.Segment += asm.Push;
        asm.Data += (dir, bytes) =>
        {
            foreach (var f in framer.Push(dir, bytes))
            {
                frames++;
                if (decoder.TryDecode(f, out var ev) && ev != null) engine.Apply(ev);
            }
        };
        src.Start();

        var view = new ConsoleView(engine, () => zones.Name(engine.InstanceId ?? engine.ZoneId), () => $"frames {frames}  lost {asm.Lost} B");
        var stop = false;
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop = true; };
        while (!stop) { view.Render(); Thread.Sleep(1000); }

        src.Stop();
        engine.Finish();
        view.Render();

        if (engine.Elapsed.TotalMinutes < 5 || engine.Events == 0)
        {
            Console.WriteLine("Run too short or empty; nothing uploaded.");
            return 0;
        }
        var payload = RunPayload.Build(engine, s, zones.Slug(engine.InstanceId ?? engine.ZoneId));
        var sent = Uploader.SendAsync(payload, s.Upload).GetAwaiter().GetResult();
        Console.WriteLine(sent ? "Run shared anonymously." : "Run kept on this PC.");
        return 0;
    }

    /* ---------------- screen reading ---------------- */

    static int Pick()
    {
        Console.WriteLine("Drag a box around the chat lines that show loot and Kinah. Esc cancels.");
        var r = WpfApp.PickRegion();
        if (r == null) { Console.WriteLine("Cancelled."); return 1; }
        RegionStore.Save(r.Value);
        Console.WriteLine($"Saved chat region {r.Value.X},{r.Value.Y},{r.Value.Width},{r.Value.Height}. Next: `ocr-test` to check it reads, then `watch`.");
        return 0;
    }

    static System.Drawing.Rectangle RegionFor(Dictionary<string, string> o) =>
        RegionStore.Parse(o.GetValueOrDefault("region")) ?? RegionStore.Load()
        ?? throw new InvalidOperationException("no chat region yet: run `pick` first, or pass --region x,y,w,h");

    static int OcrTest(Settings s, Dictionary<string, string> o)
    {
        var items = ItemDictionary.LoadOrEmpty(s.ItemsFile);
        var reader = new ScreenReader(RegionFor(o), s.Ocr.Scale);
        var parser = new ChatParser(items);
        string? png = o.ContainsKey("save") ? $"ocr-test-{DateTime.Now:HHmmss}.png" : null;
        var lines = Task.Run(() => reader.ReadAsync(png)).GetAwaiter().GetResult();
        Console.WriteLine($"Region {reader.Region}: {lines.Count} line(s) recognized" + (png != null ? $", capture saved to {png}" : ""));
        foreach (var l in lines)
        {
            var evs = parser.Parse(l, DateTime.UtcNow).ToList();
            Console.WriteLine($"  {l}");
            foreach (var ev in evs) Console.WriteLine($"      -> {Describe(ev, items)}");
        }
        if (lines.Count == 0) Console.WriteLine("Nothing recognized. Make sure the game is in borderless or windowed mode and the box covers the chat text.");
        return 0;
    }

    /// <summary>Renders the window (and the panel) off-screen to PNG files, so the layout can be reviewed without a desktop session.</summary>
    static int UiShot(Settings s, Dictionary<string, string> o, List<string> pos)
    {
        var dir = pos.Count > 0 ? pos[0] : ".";
        WpfApp.Shot(s, dir, o.ContainsKey("demo"));
        Console.WriteLine($"wrote {Path.GetFullPath(dir)}");
        return 0;
    }

    /// <summary>Feeds typical chat lines (including recognition slips) through the parser and the new-line tracker.</summary>
    static int ParseTest(Settings s)
    {
        var items = ItemDictionary.LoadOrEmpty(s.ItemsFile);
        var parser = new ChatParser(items);
        string[] lines =
        {
            "[21:14] Acquired 9,000 Kina (Bound).",
            "Acquired 2,790 Kina.",
            "Acquired 1,24O Kina.",
            "Acquired Drakan hide ×2.",
            "Acquired Drakan hide x2",
            "Acquired Aether shard *3.",
            "Acquired Greater manastone.",
            "Acquired Radiant Odyle (Bound) ×4.",
            "You defeated Flame Sprite.",
            "Flame Sprite dealt 1,204 damage to you.",
            "Guild chat: anyone selling Odyle?"
        };
        Console.WriteLine("Parser:");
        foreach (var l in lines)
        {
            var evs = parser.Parse(l, DateTime.UtcNow).Select(e => Describe(e, items)).ToList();
            Console.WriteLine($"  {l,-44} -> {(evs.Count == 0 ? "(ignored)" : string.Join("; ", evs))}");
        }

        Console.WriteLine("\nNew-line tracker (what each read counts as new):");
        var t = new LineTracker();
        var frames = new[]
        {
            new List<string> { "a", "b", "c" },
            new List<string> { "b", "c", "d" },
            new List<string> { "b", "c", "d" },
            new List<string> { "c", "d", "d" },
            new List<string> { "x", "y", "z" },
            new List<string> { "y", "z", "w" }
        };
        string[] expect = { "", "d", "", "d", "", "w" };
        bool ok = true;
        for (int i = 0; i < frames.Length; i++)
        {
            var got = string.Join(",", t.NewLines(frames[i]));
            ok &= got == expect[i];
            Console.WriteLine($"  read {i + 1} [{string.Join(",", frames[i])}] -> new: [{got}]  expected [{expect[i]}] {(got == expect[i] ? "ok" : "MISMATCH")}");
        }
        Console.WriteLine(ok ? "\nAll tracker cases passed." : "\nTracker mismatch, see above.");
        return ok ? 0 : 1;
    }

    static string Describe(GameEvent ev, ItemDictionary items) => ev switch
    {
        KinahChanged k => $"Kinah {(k.Delta >= 0 ? "+" : "")}{k.Delta:N0}{(k.Bound ? " (bound)" : "")}",
        ItemAcquired a => $"{items.NameOf(a.ItemId)} ×{a.Count}{(a.Bound ? " (bound)" : "")}",
        MobKilled => "kill",
        ZoneChanged z => $"zone {z.ZoneId}",
        _ => ev.GetType().Name
    };

    static int Watch(Settings s, Dictionary<string, string> o)
    {
        var items = ItemDictionary.LoadOrEmpty(s.ItemsFile);
        var activity = o.GetValueOrDefault("activity") ?? s.Ocr.Activity;
        int? interval = int.TryParse(o.GetValueOrDefault("interval"), out var iv) ? iv : null;
        var session = new TrackerSession(RegionFor(o), s, items, activity, interval);
        var engine = session.Engine;

        Console.WriteLine($"Watching {RegionFor(o)} every {interval ?? s.Ocr.IntervalMs} ms. Activity: {activity}. Items known: {items.Count}. (The on-screen panel is only in the window app.)");
        if (s.Upload.Enabled && !string.IsNullOrWhiteSpace(s.Upload.Endpoint))
            Console.WriteLine($"When you stop, this run's totals go anonymously to {s.Upload.Endpoint}: region, activity, gear bracket, minutes, kills, kinah, item counts and a random install ID.");
        Console.WriteLine("Ctrl+C to stop.");

        var stop = false;
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop = true; };
        var view = new ConsoleView(engine, () => activity, () => $"reads {session.Reads}  skipped {session.Unaligned}" + (session.LastError.Length > 0 ? "  " + session.LastError : ""));
        session.Start();
        while (!stop) { view.Render(); Thread.Sleep(1000); }
        session.StopAsync().GetAwaiter().GetResult();
        view.Render();

        if (!session.Eligible) { Console.WriteLine("Run too short or empty; nothing uploaded."); return 0; }
        var sent = session.UploadAsync(s).GetAwaiter().GetResult();
        Console.WriteLine(sent ? "Run shared anonymously." : "Run kept on this PC.");
        return 0;
    }

    static string Fk(double v) => Math.Abs(v) >= 1e6 ? $"{v / 1e6:0.00}M" : Math.Abs(v) >= 1e3 ? $"{v / 1e3:0}K" : $"{v:0}";

    /* ---------------- recordings ---------------- */

    static int Dump(Dictionary<string, string> o, List<string> pos)
    {
        if (pos.Count == 0) throw new ArgumentException("usage: dump <file.klrec> [--around 4] [--bytes 48]");
        int around = int.Parse(o.GetValueOrDefault("around") ?? "4");
        int nb = int.Parse(o.GetValueOrDefault("bytes") ?? "48");
        var recs = FrameRecorder.Read(pos[0]).ToList();
        long fromServer = recs.Where(r => r.FromServer).Sum(r => (long)r.Data.Length);
        Console.WriteLine($"{recs.Count} records, {recs.Count(r => r.IsMarker)} notes, {fromServer / 1024} KB from server, {recs.Where(r => !r.FromServer && !r.IsMarker).Sum(r => (long)r.Data.Length) / 1024} KB from client");
        if (o.TryGetValue("first", out var firstStr) && int.TryParse(firstStr, out var first))
        {
            // raw look at the start of the stream, for working out the message framing
            foreach (var r in recs.Where(r => !r.IsMarker).Take(first))
                Console.WriteLine($"  {Ts(r.UnixMs)}  {(r.FromServer ? "S>C" : "C>S")} {r.Data.Length,6} B  {Hex(r.Data, nb)}");
            return 0;
        }
        for (int i = 0; i < recs.Count; i++)
        {
            if (!recs[i].IsMarker) continue;
            Console.WriteLine($"\n== {Ts(recs[i].UnixMs)}  NOTE: {Encoding.UTF8.GetString(recs[i].Data)}");
            for (int j = Math.Max(0, i - around); j < Math.Min(recs.Count, i + around + 1); j++)
            {
                if (recs[j].IsMarker) continue;
                Console.WriteLine($"  {(j < i ? "-" : "+")}{Math.Abs(j - i)}  {Ts(recs[j].UnixMs)}  {(recs[j].FromServer ? "S>C" : "C>S")} {recs[j].Data.Length,6} B  {Hex(recs[j].Data, nb)}");
            }
        }
        return 0;
    }

    static string Ts(long ms) => DateTimeOffset.FromUnixTimeMilliseconds(ms).LocalDateTime.ToString("HH:mm:ss.fff");
    static string Hex(byte[] b, int n) => string.Join(' ', b.Take(n).Select(x => x.ToString("x2"))) + (b.Length > n ? " …" : "");
}
