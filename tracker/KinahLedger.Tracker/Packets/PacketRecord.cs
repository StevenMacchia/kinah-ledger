using System.Net;
using System.Text;
using System.Text.Json;

namespace KinahLedger.Packets;

/// <summary>
/// packet-record: a copy of the traffic between AION 2 and its game server for a few minutes, with Npcap (no administrator
/// needed), and the tracker's chat lines from the same minutes, for working out what the game says when. It only listens:
/// nothing is sent to the game, and nothing in the game or its memory is touched. Only the game server's ports are
/// recorded (not 443, so no web, Discord or launcher traffic), and the files stay on this PC. Nothing here reads what the
/// traffic says: the file is the raw frames as the card handed them.
///
/// The folder is the one kinah-bench/packets/capture.ps1 made with Windows' pktmon (which needs an admin terminal):
/// capture-&lt;yyyy-MM-dd-HHmmss&gt;\ with capture.pcapng, chat.log (reads.log's lines for those minutes) and info.json.
/// </summary>
public static class PacketRecord
{
    public static int Run(IEnumerable<string> processHints, Dictionary<string, string> opts)
    {
        double minutes = 10;
        if (opts.TryGetValue("seconds", out var secs) && double.TryParse(secs, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var s)) minutes = s / 60;
        else if (opts.TryGetValue("minutes", out var mins) && double.TryParse(mins, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var m)) minutes = m;
        if (minutes <= 0) { Console.WriteLine("--minutes must be more than 0"); return 2; }
        bool selfTest = opts.ContainsKey("self-test");
        var root = opts.TryGetValue("out", out var o) ? Path.GetFullPath(o) : selfTest ? TestTemp.Path("KinahLedger-packet-record") : DefaultOut();
        return selfTest ? SelfTest(root) : Record(processHints, minutes, root);
    }

    /// <summary>kinah-bench\packets beside the repo when this build runs from it (or from under it), else Documents\KinahLedger captures.</summary>
    static string DefaultOut()
    {
        foreach (var start in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
            for (var d = new DirectoryInfo(start); d != null; d = d.Parent)
            {
                var bench = Path.Combine(d.FullName, "kinah-bench", "packets");
                if (Directory.Exists(bench)) return bench;
            }
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "KinahLedger captures");
    }

    /// <summary>The game's server links: not the web (443, 80) and not this PC, as capture.ps1 and the reader pick them.</summary>
    internal static List<TcpTable.Link> GameLinks(IReadOnlyCollection<uint> pids) =>
        TcpTable.Of(pids).Where(l => l.RemotePort is not (443 or 80) && !IPAddress.IsLoopback(l.Remote) && !l.Remote.Equals(IPAddress.Any)).ToList();

    internal static string FilterFor(IEnumerable<int> ports) => string.Join(" or ", ports.Select(p => $"tcp port {p}"));

    static int Record(IEnumerable<string> processHints, double minutes, string root)
    {
        if (!PacketSources.NpcapInstalled) { Say("Npcap isn't installed. Get the free edition from npcap.com, then run this again.", ConsoleColor.Yellow); return 1; }
        Ocr.GameWindow.Find(processHints);
        var pids = Ocr.GameWindow.Pids;
        if (pids.Count == 0) { Say("AION 2 isn't running. Open it, log in to a character, then run this again.", ConsoleColor.Yellow); return 1; }
        var links = GameLinks(pids);
        var game = TcpTable.GameServer(links);
        if (game == null) { Say("AION 2 has no game-server connection yet. Log in to a character, then run this again.", ConsoleColor.Yellow); return 1; }
        var ports = links.Select(l => l.RemotePort).Distinct().OrderBy(p => p).ToList();
        var seen = links.Select(Key).Distinct().ToList();

        NpcapSource src;
        try { src = new NpcapSource(game.Local, FilterFor(ports)); }
        catch (Exception ex) { Say("Couldn't open the capture: " + ex.Message, ConsoleColor.Red); return 1; }

        var (dir, start) = NewFolder(root);
        var pcap = Path.Combine(dir, "capture.pcapng");
        bool moved = false, stop = false;
        ConsoleCancelEventHandler onCancel = (_, e) => { e.Cancel = true; stop = true; };
        Console.CancelKeyPress += onCancel;
        string? failed = null;
        var w = new PcapngWriter(pcap, src.LinkType, NpcapSource.SnapLen);
        try
        {
            Console.WriteLine();
            Say($"Recording the game server ({string.Join(", ", seen)}) for {Span(TimeSpan.FromMinutes(minutes))}.", ConsoleColor.Green);
            Console.WriteLine("Play normally: kill mobs, pick up Kinah and loot, gather a node or two. Keep the chat visible so the tracker reads it too.");
            Console.WriteLine("Press Ctrl+C to stop early (the capture is still saved).");
            var end = start.AddMinutes(minutes);
            var nextLook = DateTime.Now; var nextLine = DateTime.Now;
            bool live = !Console.IsOutputRedirected;
            while (!stop && DateTime.Now < end)
            {
                if (src.NextFrame() is { } f) w.Add(f.At, f.Frame, f.Length);
                var now = DateTime.Now;
                if (now >= nextLook)
                {
                    nextLook = now.AddSeconds(5);
                    foreach (var l in GameLinks(pids))
                    {
                        if (!seen.Contains(Key(l))) seen.Add(Key(l));
                        if (!ports.Contains(l.RemotePort))
                        {
                            // the game moved to another port: the filter takes it in from here (pktmon's couldn't)
                            ports.Add(l.RemotePort); ports.Sort(); moved = true;
                            try { src.Filter(FilterFor(ports)); } catch (Exception ex) { Console.WriteLine($"\ncouldn't add port {l.RemotePort}: {ex.Message}"); }
                        }
                    }
                }
                if (now >= nextLine)
                {
                    var status = $"{Clock(end - now)} left, {w.Frames:N0} packets, {w.Bytes / 1e6:0.0} MB";
                    if (live) { Console.Write("\r  " + status.PadRight(60)); nextLine = now.AddSeconds(1); }
                    else { Console.WriteLine("  " + status); nextLine = now.AddSeconds(30); }
                }
            }
            if (live) Console.WriteLine();
        }
        catch (Exception ex) { failed = ex.Message; }
        finally
        {
            Console.CancelKeyPress -= onCancel;
            w.Dispose();
            src.Dispose();
        }
        if (failed != null) Say("The capture stopped: " + failed + " (what it had is saved)", ConsoleColor.Yellow);
        return Finish(dir, start, DateTime.Now, ports, seen, moved, w.Frames);
    }

    /// <summary>
    /// packet-record --self-test: the same folder from made-up frames (no game, no Npcap), so the writer and the folder can be
    /// checked against analyze.py and packet-replay anywhere.
    /// </summary>
    static int SelfTest(string root)
    {
        var (dir, start) = NewFolder(root);
        var server = IPAddress.Parse("193.202.112.2"); var me = IPAddress.Parse("192.168.1.4");
        var rnd = new Random(13328);
        uint down = 5000, up = 9000;
        using (var w = new PcapngWriter(Path.Combine(dir, "capture.pcapng"), 1, NpcapSource.SnapLen))
        {
            var at = start.ToUniversalTime();
            for (int i = 0; i < 200; i++)
            {
                var pay = new byte[rnd.Next(20, 1400)]; rnd.NextBytes(pay);
                bool fromServer = i % 4 != 3;
                var ip = fromServer ? PacketTests.Ip(server, 13328, me, 57735, down, pay) : PacketTests.Ip(me, 57735, server, 13328, up, pay);
                if (fromServer) down += (uint)pay.Length; else up += (uint)pay.Length;
                w.Add(at, new byte[12].Concat(new byte[] { 0x08, 0x00 }).Concat(ip).ToArray());
                at = at.AddMilliseconds(rnd.Next(5, 150));
            }
        }
        int rc = Finish(dir, start, DateTime.Now, new List<int> { 13328 }, new List<string> { $"{server}:13328" }, false, 200);
        var back = Pcapng.Read(Path.Combine(dir, "capture.pcapng")).ToList();
        bool ok = back.Count == 200 && back.All(b => b.Link == 1) && File.Exists(Path.Combine(dir, "info.json")) && File.Exists(Path.Combine(dir, "chat.log"));
        Console.WriteLine(ok ? $"packet-record self-test passed: {back.Count} frames read back" : $"packet-record self-test FAILED: {back.Count} frames read back");
        return ok ? rc : 1;
    }

    static (string Dir, DateTime Start) NewFolder(string root)
    {
        var start = DateTime.Now;
        var dir = Path.Combine(root, "capture-" + start.ToString("yyyy-MM-dd-HHmmss"));
        Directory.CreateDirectory(dir);
        return (dir, start);
    }

    /// <summary>chat.log and info.json beside the capture, and what was saved.</summary>
    static int Finish(string dir, DateTime start, DateTime stop, List<int> ports, List<string> links, bool moved, long frames)
    {
        var lines = ChatLines(start.AddSeconds(-30), stop.AddSeconds(30));
        File.WriteAllLines(Path.Combine(dir, "chat.log"), lines, new UTF8Encoding(true));

        using (var fs = File.Create(Path.Combine(dir, "info.json")))
        using (var j = new Utf8JsonWriter(fs, new JsonWriterOptions { Indented = true }))
        {
            j.WriteStartObject();
            j.WriteString("startUtc", start.ToUniversalTime().ToString("o"));
            j.WriteString("stopUtc", stop.ToUniversalTime().ToString("o"));
            j.WriteString("localDate", start.ToString("yyyy-MM-dd"));
            j.WriteNumber("utcOffsetMinutes", (int)TimeZoneInfo.Local.GetUtcOffset(start).TotalMinutes);
            j.WriteStartArray("ports"); foreach (var p in ports) j.WriteNumberValue(p); j.WriteEndArray();
            j.WriteStartArray("links"); foreach (var l in links) j.WriteStringValue(l); j.WriteEndArray();
            j.WriteBoolean("serverMoved", moved);
            j.WriteNumber("chatLines", lines.Count);
            j.WriteString("recorder", "KinahLedger packet-record " + Upload.RunPayload.AppVersion);
            j.WriteEndObject();
        }

        var size = new FileInfo(Path.Combine(dir, "capture.pcapng")).Length;
        Console.WriteLine();
        Say("Done: " + dir, ConsoleColor.Green);
        Console.WriteLine($"capture.pcapng {size / 1048576.0:0.0} MB ({frames:N0} packets), chat.log {lines.Count} lines" +
                          (moved ? " (the game moved to another server port during the capture: recorded too, and noted)" : ""));
        if (frames == 0) Say("No packets were recorded: was a character logged in and playing?", ConsoleColor.Yellow);
        Console.WriteLine("Tell Claude the capture is done.");
        return 0;
    }

    /// <summary>
    /// The player's reads.log lines (and reads.old.log's, when it turned over) stamped between the two times of day. Read from
    /// the player's own folder, never written: packet-record's own notes go to a temp folder like the other test commands.
    /// </summary>
    static List<string> ChatLines(DateTime from, DateTime to)
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KinahLedger");
        string a = from.ToString("HH:mm:ss"), b = to.ToString("HH:mm:ss");
        bool wraps = string.CompareOrdinal(a, b) > 0;   // across midnight
        bool In(string t) => wraps ? string.CompareOrdinal(t, a) >= 0 || string.CompareOrdinal(t, b) <= 0
                                   : string.CompareOrdinal(t, a) >= 0 && string.CompareOrdinal(t, b) <= 0;
        var list = new List<string>();
        foreach (var name in new[] { "reads.old.log", "reads.log" })
        {
            var p = Path.Combine(folder, name);
            if (!File.Exists(p)) continue;
            try
            {
                using var fs = new FileStream(p, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var r = new StreamReader(fs, Encoding.UTF8);
                for (string? l; (l = r.ReadLine()) != null;)
                    if (l.Length >= 8 && l[2] == ':' && l[5] == ':' && In(l[..8])) list.Add(l);
            }
            catch (IOException) { }
        }
        return list;
    }

    static string Key(TcpTable.Link l) => $"{l.Remote}:{l.RemotePort}";
    static string Clock(TimeSpan t) => t < TimeSpan.Zero ? "00:00" : $"{(int)t.TotalMinutes:00}:{t.Seconds:00}";
    static string Span(TimeSpan t) => t.TotalMinutes >= 1 && t.Seconds == 0 ? $"{t.TotalMinutes:0} minute{(t.TotalMinutes == 1 ? "" : "s")}" : $"{t.TotalSeconds:0} seconds";

    static void Say(string text, ConsoleColor color)
    {
        var keep = Console.ForegroundColor;
        try { Console.ForegroundColor = color; Console.WriteLine(text); }
        finally { Console.ForegroundColor = keep; }
    }
}
