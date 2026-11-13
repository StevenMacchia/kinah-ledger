using System.Text;
using KinahLedger.Session;

namespace KinahLedger.Packets;

/// <summary>
/// Packet reading, alongside the screen (Steven, 2026-11-13, after the AION 2 moderators allowed it): a copy of the game
/// server's messages to this PC, read for what they say. This first step changes no count. It watches the Kinah balance
/// in the game's messages and, once a minute, sets the Kinah it saw against what the chat reading counted, in
/// packets.log, so the two can be compared over real play before packets count anything.
///
/// It only listens: the copy comes from Npcap or a raw socket (Packets/Sources.cs), filtered to the game's own server
/// connection, found from the game's process id in Windows' connection table. Nothing is sent, the game is never touched,
/// and no packet is kept: only the decoded balance lines go to the log.
/// </summary>
public sealed class PacketReader : IDisposable
{
    /// <summary>Options › Game reading › Read the game's network traffic. On by default; with neither Npcap nor admin it just says why it is off.</summary>
    public bool Enabled { get; set; } = true;
    /// <summary>One line for Options and the ⋯ menu: what packet reading is doing.</summary>
    public string State { get; private set; } = "Waiting for the game";
    /// <summary>True while packets are coming in from the game's server.</summary>
    public bool On => _thread is { IsAlive: true };
    public string Kind { get; private set; } = "";
    public long? Balance => _kinah.Balance;
    public int Messages => _kinah.Messages;
    public long Packets => Interlocked.Read(ref _packets);

    readonly KinahBalance _kinah = new();
    TcpStream _down = new();
    TcpTable.Link? _link;
    IPacketSource? _source;
    Thread? _thread;
    volatile bool _stop;
    DateTime _failedAt = DateTime.MinValue, _minuteAt = DateTime.UtcNow;
    long _packets;
    // the minute's sums: Kinah the balance gained and spent, in how many changes
    long _gained, _spent; int _changes;
    SessionEngine? _chatOf; long _chatKinahAt;
    readonly object _gate = new();

    public PacketReader()
    {
        _kinah.Changed += s =>
        {
            lock (_gate) { if (s.Change > 0) { _gained += s.Change; _changes++; } else if (s.Change < 0) { _spent -= s.Change; _changes++; } }
            Log($"{Local(s.At)}  Kinah {(s.Change == 0 && !s.Plain ? "seen" : Signed(s.Change))}{(s.Balance is long b ? $" · balance {b:N0}" : " · balance not seen whole yet")}{(s.Plain ? "" : " (the long shape)")}");
        };
    }

    /// <summary>Every few seconds from the game watcher: follows the game's server connection and writes the minute's line.</summary>
    public void Tick(IReadOnlyCollection<uint> gamePids, SessionEngine? chat)
    {
        try { TickCore(gamePids, chat); }
        catch (Exception ex) { CrashLog.Write("packet reading", ex); State = "Off: " + ex.Message; StopCapture(); }
    }

    void TickCore(IReadOnlyCollection<uint> gamePids, SessionEngine? chat)
    {
        if (!Enabled) { StopCapture(); State = "Off in Options"; return; }
        if (gamePids.Count == 0) { StopCapture(); State = "Waiting for the game"; return; }
        var game = TcpTable.GameServer(TcpTable.Of(gamePids));
        if (game == null) { StopCapture(); State = "Waiting for the game to connect to its server"; return; }
        // a new server (a channel change, a relog): a new stream, the balance's low bits carry on across it
        if (_link != null && game != _link) { Log($"{Local(DateTime.UtcNow)}  the game moved to {game.Remote}:{game.RemotePort}"); StopCapture(); }
        if (!On && (DateTime.UtcNow - _failedAt).TotalSeconds >= 60) StartCapture(game);
        Minute(chat);
    }

    void StartCapture(TcpTable.Link game)
    {
        var src = PacketSources.Open(game, out var why);
        if (src == null)
        {
            _failedAt = DateTime.UtcNow;
            string was = State;
            State = PacketSources.NpcapInstalled ? "Off: " + why : "Off: install Npcap (npcap.com) to read the game's traffic";
            if (State != was) Log($"{Local(DateTime.UtcNow)}  packet reading off: {why}");
            return;
        }
        _source = src; _link = game; Kind = src.Kind;
        _down = new TcpStream { OnData = _kinah.Feed, OnGap = _kinah.Gap };
        _stop = false;
        _thread = new Thread(Run) { IsBackground = true, Name = "packet reading", Priority = ThreadPriority.BelowNormal };
        _thread.Start();
        State = $"On ({Kind}): reading the game's server {game.Remote}:{game.RemotePort}";
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
    /// Once a minute while reading: the Kinah the balance gained against the Kinah the chat counted in the same minute
    /// (the always-on count's loose Kinah). Spending shows as spent, never against the chat.
    /// </summary>
    void Minute(SessionEngine? chat)
    {
        var now = DateTime.UtcNow;
        if ((now - _minuteAt).TotalSeconds < 60) return;
        _minuteAt = now;
        long gained, spent; int changes;
        lock (_gate) { gained = _gained; spent = _spent; changes = _changes; _gained = _spent = 0; _changes = 0; }
        long? chatGain = null;
        if (chat != null)
        {
            long k = chat.Kinah;
            if (ReferenceEquals(chat, _chatOf)) chatGain = k - _chatKinahAt;
            _chatOf = chat; _chatKinahAt = k;
        }
        if (!On && changes == 0) return;
        string vs = chatGain is not long cg ? "the chat count just started"
            : cg == gained ? $"the chat counted +{cg:N0}: agree"
            : $"the chat counted {Signed(cg)}: {(gained > cg ? "packets saw" : "the chat counted")} {Math.Abs(gained - cg):N0} more";
        Log($"{Local(now)}  minute: packets saw Kinah +{gained:N0} in {changes} change{(changes == 1 ? "" : "s")}{(spent > 0 ? $", spent {spent:N0}" : "")}; {vs}"
            + $" · {Packets:N0} packets, {_down.Gaps} gaps");
    }

    /* ---------------- packets.log ---------------- */

    public static string LogPath => Path.Combine(CrashLog.Dir, "packets.log");
    const long LogMax = 2_000_000;
    static readonly object LogGate = new();

    static void Log(string line)
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

    static string Local(DateTime utc) => utc.ToLocalTime().ToString("HH:mm:ss.f");
    static string Signed(long v) => v >= 0 ? $"+{v:N0}" : $"−{-v:N0}";

    public void Dispose() => StopCapture();
}
