using System.Collections.Concurrent;
using System.Text;
using KinahLedger.Session;

namespace KinahLedger.Packets;

/// <summary>
/// Packet reading (Steven, 2026-11-13, after the AION 2 moderators allowed it): a copy of the game server's messages to this
/// PC, turned into Kinah, bound Kinah and Abyss Points changes by the release build's decoder (Packets/Decoder.cs; not part
/// of this source). Since 0.10.66 those three count from here (Session/TrackerSession.Packets.cs), the screen's reading of
/// them set aside while they do; packets.log says each change and, once a minute, how the screen's reading compared.
///
/// It only listens: the copy comes from Npcap or a raw socket (Packets/Sources.cs), filtered to the game's own server
/// connection, found from the game's process id in Windows' connection table. Nothing is sent, the game is never touched,
/// and no packet is kept: only the decoded changes go to the log (a PC that asked for a local recording keeps one, Packets/PacketLog.cs).
/// </summary>
public sealed class PacketReader : IDisposable
{
    /// <summary>Options › Game reading › Read the game's network traffic. On by default; with neither Npcap nor admin it just says why it is off.</summary>
    public bool Enabled { get; set; } = true;
    /// <summary>One line for Options: what packet reading is doing.</summary>
    public string State { get; private set; } = "Waiting for the game";
    /// <summary>True while a capture runs on the game's connection.</summary>
    public bool On => _testOn || _thread is { IsAlive: true };
    bool _testOn;
    /// <summary>For packet-test: reading as if a capture ran, the messages handed in by hand.</summary>
    internal void ReadingForTest() { _testOn = true; _packetAt = DateTime.UtcNow; }
    readonly Dictionary<CurrencyKind, long> _testAmounts = new();
    /// <summary>For packet-test: an amount as the decoder would report it (the change from the last one, or a baseline).</summary>
    internal void AmountForTest(CurrencyKind k, long amount, DateTime at)
    {
        _packetAt = DateTime.UtcNow;
        bool first = !_testAmounts.TryGetValue(k, out var was);
        _testAmounts[k] = amount;
        if (first || amount != was) Take(new CurrencyChange(at, k, first ? 0 : amount - was, amount, first));
    }
    internal void AgreeForTest() => Agree();
    public string Kind { get; private set; } = "";
    public long Packets => Interlocked.Read(ref _packets);
    /// <summary>Messages and bundles the decoder read, its resyncs and its currency messages, for the log.</summary>
    public (long Messages, long Bundles, int Resyncs, int CurrencyMessages) DecoderStats => _decoder?.Stats ?? default;
    /// <summary>Where the decoder comes from: the release build's native one; a test hands in its own.</summary>
    public static Func<IGameDecoder?> Decoders { get; set; } = NativeDecoder.TryOpen;

    IGameDecoder? _decoder;
    readonly ConcurrentQueue<CurrencyChange> _changes = new();
    TcpStream _down = new();
    TcpTable.Link? _link;
    IPacketSource? _source;
    Thread? _thread;
    volatile bool _stop;
    DateTime _failedAt = DateTime.MinValue, _minuteAt = DateTime.UtcNow, _packetAt = DateTime.MinValue;
    long _packets;
    readonly object _gate = new();
    // per currency: when its baseline came (the first amount after a connection), and when the packets stopped agreeing
    readonly Dictionary<CurrencyKind, DateTime> _baseAt = new();
    readonly Dictionary<CurrencyKind, string> _broken = new();
    // the screen's readings set aside, and the packets' own changes, for the agreement check and the minute's line
    readonly List<(CurrencyKind K, long Amount, DateTime At)> _screen = new();
    readonly List<(CurrencyKind K, long Delta, DateTime At)> _seen = new();
    readonly List<DateTime> _misses = new();

    /// <summary>A change the decoder found: kept for the agreement check, queued for counting, logged.</summary>
    void Take(CurrencyChange c)
    {
        lock (_gate)
        {
            if (c.First) _baseAt[c.What] = c.At;
            else _seen.Add((c.What, c.Delta, c.At));
        }
        _changes.Enqueue(c);
        Log($"{Local(c.At)}  {Name(c.What)} {(c.First ? "now" : Signed(c.Delta))} · {c.Amount:N0}");
    }

    /// <summary>The next change for the tracker to count (TrackerSession.DrainPackets), null when there is none.</summary>
    public CurrencyChange? Next() => _changes.TryDequeue(out var c) ? c : null;

    /// <summary>
    /// Whether the packets count <paramref name="k"/> at <paramref name="at"/>: reading now, its baseline in a while before
    /// (a line within 4 s after the baseline is the pickup that baseline came with: the chat's), and not broken.
    /// </summary>
    public bool Counting(CurrencyKind k, DateTime at)
    {
        if (!On || (DateTime.UtcNow - _packetAt).TotalSeconds > 15) return false;
        lock (_gate) return !_broken.ContainsKey(k) && _baseAt.TryGetValue(k, out var b) && at > b.AddSeconds(4);
    }

    /// <summary>Why the packets stopped counting <paramref name="k"/>, or null.</summary>
    public string? Broken(CurrencyKind k) { lock (_gate) return _broken.GetValueOrDefault(k); }

    /// <summary>A pickup the screen read (a chat line, the counter, the loot list) and the packets count instead: kept for the agreement check.</summary>
    public void ScreenSaw(CurrencyKind k, long amount, DateTime at) { lock (_gate) { _screen.Add((k, amount, at)); _screenAll.Add((k, at)); _screenAll.RemoveAll(s => (at - s.At).TotalMinutes > 2); } }
    // every screen reading set aside, kept two minutes (the agreement check takes its own copy off _screen)
    readonly List<(CurrencyKind K, DateTime At)> _screenAll = new();

    /// <summary>Whether the screen read a pickup of <paramref name="k"/> within <paramref name="seconds"/> of <paramref name="at"/> (and the packets count it instead).</summary>
    public bool ScreenNear(CurrencyKind k, DateTime at, double seconds) { lock (_gate) return _screenAll.Any(s => s.K == k && Math.Abs((s.At - at).TotalSeconds) <= seconds); }

    /// <summary>Every few seconds from the game watcher: follows the game's server connection, checks agreement, writes the minute's line.</summary>
    public void Tick(IReadOnlyCollection<uint> gamePids, SessionEngine? chat)
    {
        try { TickCore(gamePids); Agree(); Minute(); }
        catch (Exception ex) { CrashLog.Write("packet reading", ex); State = "Off: " + ex.Message; StopCapture(); }
    }

    void TickCore(IReadOnlyCollection<uint> gamePids)
    {
        if (!Enabled) { StopCapture(); State = "Off in Options"; return; }
        if (gamePids.Count == 0) { StopCapture(); State = "Waiting for the game"; return; }
        var game = TcpTable.GameServer(TcpTable.Of(gamePids));
        if (game == null) { StopCapture(); State = "Waiting for the game to connect to its server"; return; }
        // another server (an event, the Abyss, a relog): a new stream; the amounts carry on across it
        if (_link != null && game != _link) { Log($"{Local(DateTime.UtcNow)}  the game moved to {game.Remote}:{game.RemotePort}"); StopCapture(); }
        if (!On && (DateTime.UtcNow - _failedAt).TotalSeconds >= 60) StartCapture(game);
    }

    void StartCapture(TcpTable.Link game)
    {
        // this build reads no traffic without the decoder (a build from the public source): the screen counts, as before
        _decoder ??= Decoders();
        if (_decoder == null) { State = "Off: this build can't read the game's traffic (the downloaded tracker can)"; _failedAt = DateTime.UtcNow; return; }
        var src = PacketSources.Open(game, out var why);
        if (src == null)
        {
            _failedAt = DateTime.UtcNow;
            string was = State;
            State = PacketSources.NpcapInstalled ? "Off: " + why : "Off: install Npcap (npcap.com) to count from the game's traffic";
            if (State != was) Log($"{Local(DateTime.UtcNow)}  packet reading off: {why}");
            return;
        }
        _source = src; _link = game; Kind = src.Kind;
        // a new connection starts from a fresh amount: the chat counted while nothing was read, so the first message is a baseline
        var dec = _decoder;
        dec.Reset(); lock (_gate) _baseAt.Clear();
        _down = new TcpStream { OnData = (d, at) => { dec.Feed(d, at); while (dec.Next() is { } ch) Take(ch); }, OnGap = dec.Gap };
        _stop = false;
        _thread = new Thread(Run) { IsBackground = true, Name = "packet reading", Priority = ThreadPriority.BelowNormal };
        _thread.Start();
        State = $"On ({Kind}): counting Kinah, bound Kinah and Abyss Points from the game's server {game.Remote}:{game.RemotePort}";
        Log($"{Local(DateTime.UtcNow)}  reading {game.Remote}:{game.RemotePort} → {game.Local}:{game.LocalPort} with {Kind}");
    }

    void Run()
    {
        var src = _source!; var link = _link!;
        try
        {
            while (!_stop)
            {
                if (src.Next() is not { } p) continue;
                if (TcpSegment.Parse(p.Ip) is not { } s) continue;
                // the server's messages to this PC only (what this PC sends looked encrypted, and says nothing we need)
                if (s.SrcPort != link.RemotePort || s.DstPort != link.LocalPort || !s.Src.Equals(link.Remote)) continue;
                Interlocked.Increment(ref _packets);
                _packetAt = DateTime.UtcNow;
                _down.Add(s.Seq, s.Payload, p.At, s.Syn);
            }
        }
        catch (Exception ex) when (!_stop)
        {
            CrashLog.Write("packet reading", ex);
            State = "Off: " + ex.Message;
            _failedAt = DateTime.UtcNow;
        }
        finally { try { src.Dispose(); } catch (Exception) { } }
    }

    void StopCapture()
    {
        _stop = true;
        _thread?.Join(1000);
        _thread = null; _source = null; _link = null;
    }

    /// <summary>
    /// The agreement check: a pickup the screen read 15 s ago or more, of a currency the packets count, that the packets show
    /// no change of within 10 s either side, is a miss. Three misses of one currency in five minutes and the packets stop
    /// counting it (the message changed, a patch): the screen counts it again, and packets.log says so.
    /// </summary>
    void Agree()
    {
        var now = DateTime.UtcNow;
        lock (_gate)
        {
            foreach (var s in _screen.Where(s => (now - s.At).TotalSeconds >= 15).ToList())
            {
                _screen.Remove(s);
                if (_seen.Any(p => p.K == s.K && Math.Abs((p.At - s.At).TotalSeconds) <= 10)) continue;
                _misses.Add(now);
                _misses.RemoveAll(m => (now - m).TotalMinutes > 5);
                if (_misses.Count >= 3 && !_broken.ContainsKey(s.K))
                {
                    _broken[s.K] = $"the chat read {Name(s.K)} three times in five minutes that the game's messages didn't show";
                    Log($"{Local(now)}  {Name(s.K)}: {_broken[s.K]}; the screen counts it again");
                    CrashLog.Note($"packets: {_broken[s.K]}; the screen counts it again");
                }
            }
            _seen.RemoveAll(p => (now - p.At).TotalMinutes > 2);
        }
    }

    void Minute()
    {
        var now = DateTime.UtcNow;
        if ((now - _minuteAt).TotalSeconds < 60) return;
        var from = _minuteAt; _minuteAt = now;
        List<string> parts;
        lock (_gate)
        {
            parts = Enum.GetValues<CurrencyKind>().Select(k =>
            {
                long pk = _seen.Where(p => p.K == k && p.At >= from && p.Delta > 0).Sum(p => p.Delta);
                int n = _seen.Count(p => p.K == k && p.At >= from && p.Delta > 0);
                long sc = _screen.Where(s => s.K == k && s.At >= from).Sum(s => s.Amount);
                return pk == 0 && sc == 0 ? "" : $"{Name(k)} +{pk:N0} in {n} (the screen read +{sc:N0}{(pk == sc ? ": agree" : "")})";
            }).Where(s => s.Length > 0).ToList();
        }
        if (!On && parts.Count == 0) return;
        Log($"{Local(now)}  minute: {(parts.Count == 0 ? "nothing picked up" : string.Join("; ", parts))} · {Packets:N0} packets, {DecoderStats.Messages:N0} messages, {_down.Gaps} gaps, {DecoderStats.Resyncs} resyncs");
    }

    static string Name(CurrencyKind k) => k switch { CurrencyKind.Kinah => "Kinah", CurrencyKind.BoundKinah => "bound Kinah", _ => "Abyss Points" };

    /* ---------------- packets.log ---------------- */

    public static string LogPath => Path.Combine(CrashLog.Dir, "packets.log");
    const long LogMax = 2_000_000;
    static readonly object LogGate = new();

    internal static void Log(string line)
    {
        try
        {
            lock (LogGate)
            {
                var path = LogPath;
                var fi = new FileInfo(path);
                if (fi.Exists && fi.Length > LogMax) File.Move(path, Path.Combine(CrashLog.Dir, "packets.old.log"), true);
                File.AppendAllText(path, line + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch (Exception) { /* the log is for checking: never a reason to stop reading */ }
    }

    internal static string Local(DateTime utc) => utc.ToLocalTime().ToString("HH:mm:ss.f");
    static string Signed(long v) => v >= 0 ? $"+{v:N0}" : $"−{-v:N0}";

    public void Dispose() { StopCapture(); _decoder?.Dispose(); _decoder = null; }
}
