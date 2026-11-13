using System.Net;
using System.Text.Json;

namespace KinahLedger.Packets;

/// <summary>
/// A local recording of the game server's traffic while the tracker runs, for diagnosis (Steven, 2026-11-13: "can't we
/// just have the traffic constantly running so that I don't have to run powershell commands"). Off for everyone; turned on
/// by the file packets\record-on in the tracker's folder (packet-log on) or KL_PACKET_LOG=1.
///
/// The same capture as packet-record: Npcap on the game's server ports (any server, so an event or Abyss server is in it),
/// raw frames as the card handed them, nothing read out of them. One pcapng file per local hour in
/// %LOCALAPPDATA%\KinahLedger\packets (yyyyMMdd-HH.pcapng; -2, -3 after a restart within the hour), flushed every couple
/// of seconds so it can be read while it is written, with a .json beside it (its UTC start and end, frames, bytes, the
/// server links). Files older than 48 hours go, and the oldest go while the folder is over 2 GB. Nothing is uploaded.
/// </summary>
public sealed class PacketLog : IDisposable
{
    /// <summary>Only a watching window records (MainWindow); a layout render or a test never does.</summary>
    public bool Allowed { get; set; }
    public static string Dir => Path.Combine(CrashLog.Dir, "packets");
    public static string FlagPath => Path.Combine(Dir, "record-on");
    /// <summary>Whether this PC asked for the recording: the flag file, or KL_PACKET_LOG=1.</summary>
    public static bool Wanted => Environment.GetEnvironmentVariable("KL_PACKET_LOG") is "1" or "true" || File.Exists(FlagPath);

    public static TimeSpan KeepFor { get; set; } = TimeSpan.FromHours(48);
    public static long KeepBytes { get; set; } = 2L << 30;

    /// <summary>The file being written now, or null.</summary>
    public string? Current => _file;

    Thread? _thread;
    volatile bool _stop;
    volatile string? _newFilter;
    IPAddress? _local;
    List<int> _ports = new();
    readonly List<string> _links = new();
    readonly object _gate = new();
    string? _file;
    DateTime _failedAt = DateTime.MinValue, _prunedAt = DateTime.MinValue;

    /// <summary>The watcher's look every few seconds: start, widen or stop the recording.</summary>
    public void Tick(IReadOnlyCollection<uint> gamePids)
    {
        try
        {
            if (!Allowed || !Wanted || gamePids.Count == 0) { Stop(); return; }
            var links = PacketRecord.GameLinks(gamePids);
            var game = TcpTable.GameServer(links);
            if (game == null) { Stop(); return; }
            lock (_gate) foreach (var l in links) { var k = $"{l.Remote}:{l.RemotePort}"; if (!_links.Contains(k)) _links.Add(k); }
            if (_thread is { IsAlive: true })
            {
                if (!game.Local.Equals(_local)) { Stop(); return; }   // another network card: open it on the next look
                var add = links.Select(l => l.RemotePort).Where(p => !_ports.Contains(p)).Distinct().ToList();
                if (add.Count > 0) { _ports = _ports.Concat(add).OrderBy(p => p).ToList(); _newFilter = PacketRecord.FilterFor(_ports); }
                return;
            }
            if ((DateTime.UtcNow - _failedAt).TotalSeconds < 60 || !PacketSources.NpcapInstalled) return;
            _ports = links.Select(l => l.RemotePort).Distinct().OrderBy(p => p).ToList();
            _local = game.Local;
            var src = new NpcapSource(game.Local, PacketRecord.FilterFor(_ports));
            _stop = false; _newFilter = null;
            _thread = new Thread(() => Run(src)) { IsBackground = true, Name = "packet log", Priority = ThreadPriority.BelowNormal };
            _thread.Start();
        }
        catch (Exception ex) { CrashLog.Write("packet log", ex); _failedAt = DateTime.UtcNow; Stop(); }
    }

    void Run(NpcapSource src)
    {
        PcapngWriter? w = null;
        DateTime hour = default, started = default, indexAt = default, last = default;
        try
        {
            Directory.CreateDirectory(Dir);
            Prune(null);
            while (!_stop)
            {
                if (_newFilter is { } f) { _newFilter = null; src.Filter(f); }
                var frame = src.NextFrame();
                var now = DateTime.Now;
                var thisHour = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0);
                if (w == null || thisHour != hour)
                {
                    if (w != null) { Index(w, started, last); w.Dispose(); w = null; }
                    hour = thisHour;
                    _file = NewName(thisHour);
                    w = new PcapngWriter(_file, src.LinkType, NpcapSource.SnapLen);
                    started = DateTime.UtcNow; last = started; indexAt = default;
                    PacketReader.Log($"{PacketReader.Local(started)}  recording the game's traffic to {Path.GetFileName(_file)}");
                    Prune(_file);
                }
                if (frame is { } fr) { if (w.Frames == 0 && fr.At < started) started = fr.At; w.Add(fr.At, fr.Frame, fr.Length); last = fr.At; }
                if ((DateTime.UtcNow - indexAt).TotalSeconds >= 30) { Index(w, started, last); indexAt = DateTime.UtcNow; }
                if ((DateTime.UtcNow - _prunedAt).TotalMinutes >= 10) Prune(_file);
            }
        }
        catch (Exception ex) when (!_stop)
        {
            CrashLog.Write("packet log", ex);
            _failedAt = DateTime.UtcNow;
        }
        finally
        {
            if (w != null) { try { Index(w, started, last); w.Dispose(); } catch (Exception) { } }
            try { src.Dispose(); } catch (Exception) { }
            _file = null;
        }
    }

    /// <summary>yyyyMMdd-HH.pcapng for the local hour, or -2, -3... when the tracker started again within it.</summary>
    static string NewName(DateTime hour)
    {
        var stem = Path.Combine(Dir, hour.ToString("yyyyMMdd-HH"));
        var p = stem + ".pcapng";
        for (int i = 2; File.Exists(p); i++) p = $"{stem}-{i}.pcapng";
        return p;
    }

    /// <summary>The .json beside a file: when it starts and ends (UTC), how much it holds, and the server links seen.</summary>
    void Index(PcapngWriter w, DateTime startUtc, DateTime lastUtc)
    {
        if (_file == null) return;
        string[] links; int[] ports;
        lock (_gate) { links = _links.ToArray(); ports = _ports.ToArray(); }
        var tmp = Path.ChangeExtension(_file, ".json.tmp");
        using (var fs = File.Create(tmp))
        using (var j = new Utf8JsonWriter(fs, new JsonWriterOptions { Indented = true }))
        {
            j.WriteStartObject();
            j.WriteString("file", Path.GetFileName(_file));
            j.WriteString("startUtc", startUtc.ToString("o"));
            j.WriteString("lastFrameUtc", lastUtc.ToString("o"));
            j.WriteString("writtenUtc", DateTime.UtcNow.ToString("o"));
            j.WriteNumber("utcOffsetMinutes", (int)TimeZoneInfo.Local.GetUtcOffset(DateTime.Now).TotalMinutes);
            j.WriteNumber("frames", w.Frames);
            j.WriteNumber("bytes", w.Bytes);
            j.WriteStartArray("ports"); foreach (var p in ports) j.WriteNumberValue(p); j.WriteEndArray();
            j.WriteStartArray("links"); foreach (var l in links) j.WriteStringValue(l); j.WriteEndArray();
            j.WriteString("recorder", "KinahLedger " + Upload.RunPayload.AppVersion);
            j.WriteEndObject();
        }
        File.Move(tmp, Path.ChangeExtension(_file, ".json"), true);
    }

    /// <summary>Files past <see cref="KeepFor"/> go, then the oldest while the folder is over <see cref="KeepBytes"/>; never the one being written.</summary>
    internal void Prune(string? current)
    {
        _prunedAt = DateTime.UtcNow;
        try
        {
            if (!Directory.Exists(Dir)) return;
            var files = new DirectoryInfo(Dir).GetFiles("*.pcapng")
                .Where(f => current == null || !string.Equals(f.FullName, Path.GetFullPath(current), StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f.LastWriteTimeUtc).ToList();
            long total = files.Sum(f => f.Length) + (current != null && File.Exists(current) ? new FileInfo(current).Length : 0);
            foreach (var f in files)
            {
                bool old = DateTime.UtcNow - f.LastWriteTimeUtc > KeepFor;
                if (!old && total <= KeepBytes) continue;
                try
                {
                    total -= f.Length;
                    f.Delete();
                    var side = Path.ChangeExtension(f.FullName, ".json");
                    if (File.Exists(side)) File.Delete(side);
                }
                catch (IOException) { }   // being read right now: the next pass takes it
            }
        }
        catch (Exception ex) { CrashLog.Write("packet log prune", ex); }
    }

    void Stop()
    {
        _stop = true;
        var t = _thread; _thread = null;
        if (t != null && t != Thread.CurrentThread) t.Join(2000);
        lock (_gate) _links.Clear();
    }

    public void Dispose() => Stop();

    /// <summary>
    /// packet-log test: the recorder as the running tracker drives it, against the game running now, into a temp folder (the
    /// player's own packets folder untouched): a file read back while it is still written, then whole, and its .json.
    /// </summary>
    static int LiveTest(IEnumerable<string> processHints, int seconds)
    {
        CrashLog.DirOverride = TestTemp.Path("KinahLedger-packet-log-test");
        if (Directory.Exists(Dir)) Directory.Delete(Dir, true);
        Directory.CreateDirectory(Dir); File.WriteAllText(FlagPath, "test");
        using var log = new PacketLog { Allowed = true };
        var end = DateTime.UtcNow.AddSeconds(seconds); var half = DateTime.UtcNow.AddSeconds(seconds / 2.0);
        int midFrames = -1; string? file = null;
        while (DateTime.UtcNow < end)
        {
            Ocr.GameWindow.Find(processHints);
            log.Tick(Ocr.GameWindow.Pids);
            file ??= log.Current;
            if (midFrames < 0 && DateTime.UtcNow >= half && log.Current is { } cur) { midFrames = Pcapng.Read(cur).Count(); Console.WriteLine($"while writing: {Path.GetFileName(cur)} reads {midFrames:N0} frames"); }
            Thread.Sleep(5000);
        }
        log.Dispose();
        if (file == null) { Console.WriteLine("nothing recorded: is AION 2 running with a character logged in, and Npcap installed?"); return 1; }
        var frames = Pcapng.Read(file).ToList();
        var side = Path.ChangeExtension(file, ".json");
        Console.WriteLine($"{file}: {frames.Count:N0} frames, {new FileInfo(file).Length / 1e6:0.00} MB, first {frames.FirstOrDefault().At:HH:mm:ss.fff}Z, last {frames.LastOrDefault().At:HH:mm:ss.fff}Z");
        if (File.Exists(side)) Console.WriteLine(File.ReadAllText(side));
        bool ok = frames.Count > 0 && midFrames > 0 && frames.Count >= midFrames && File.Exists(side);
        Console.WriteLine(ok ? "packet-log test passed" : "packet-log test FAILED");
        return ok ? 0 : 1;
    }

    /// <summary>packet-log on|off|status: the flag file the running tracker looks for every few seconds.</summary>
    public static int Command(string? what, IEnumerable<string> processHints, int seconds)
    {
        switch (what?.ToLowerInvariant())
        {
            case "test": return LiveTest(processHints, seconds);
            case "on":
                Directory.CreateDirectory(Dir);
                File.WriteAllText(FlagPath, "The tracker records the game server's traffic here while this file exists (packet-log off removes it).\r\n");
                Console.WriteLine($"on: the running tracker starts recording to {Dir} within a few seconds of seeing the game's server");
                return 0;
            case "off":
                if (File.Exists(FlagPath)) File.Delete(FlagPath);
                Console.WriteLine("off" + (Environment.GetEnvironmentVariable("KL_PACKET_LOG") is "1" or "true" ? " (but KL_PACKET_LOG is set, which keeps it on)" : "") + "; the files already written stay until they are 48 hours old");
                return 0;
            case null or "status":
                Console.WriteLine($"{(Wanted ? "on" : "off")}: {Dir}");
                if (Directory.Exists(Dir))
                    foreach (var f in new DirectoryInfo(Dir).GetFiles("*.pcapng").OrderBy(f => f.Name))
                        Console.WriteLine($"  {f.Name,-22} {f.Length / 1048576.0,8:0.0} MB  last written {f.LastWriteTime:yyyy-MM-dd HH:mm:ss}");
                return 0;
            default:
                Console.WriteLine("packet-log on|off|status (test [--seconds 40]: the recorder against the game running now, into a temp folder)");
                return 2;
        }
    }
}
