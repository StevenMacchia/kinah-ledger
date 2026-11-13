using System.Net;

namespace KinahLedger.Packets;

/// <summary>
/// packet-test: the packet reader's parts on made-up bytes (the balance message in both shapes and cut across segments,
/// segments out of order, twice and lost, frames from a card, a capture file), and packet-replay: the reader over a saved
/// capture (kinah-bench/packets/capture-*/capture.pcapng), printing every balance message it finds.
/// </summary>
public static class PacketTests
{
    static int _fails;
    static void Check(bool ok, string what) { Console.WriteLine((ok ? "ok    " : "FAIL  ") + what); if (!ok) _fails++; }

    /// <summary>A balance message as the game sent it on 2026-11-13: plain, or the long shape with another field after the low 24 bits.</summary>
    internal static byte[] Message(uint balance, bool plain, byte other = 0x22)
    {
        var m = new List<byte> { 0x56, 0x01, 0x01, 0x00, 0xae, 0xae, 0x0d, 0x00, 0x00, 0x78, 0x01, 0x00, 0x3f, 0x33, 0x70, 0x37,
                                 (byte)balance, (byte)(balance >> 8), (byte)(balance >> 16) };
        if (plain) m.AddRange(new byte[] { (byte)(balance >> 24), 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0 });
        else m.AddRange(new byte[] { other, 0x00, 0x11, 0x00, 0x05, 0x00, 0x06, 0x02, 0x00, 0x20, 0x0b, 0x00 });
        return m.ToArray();
    }

    public static int Run()
    {
        _fails = 0;
        var t = new DateTime(2026, 11, 13, 21, 44, 43, DateTimeKind.Utc);

        // the balance: the long shape before any plain one gives changes only; a plain one gives the whole figure
        {
            var k = new KinahBalance(); var seen = new List<KinahBalance.Seen>();
            k.Changed += seen.Add;
            var noise = new byte[] { 0x11, 0x1d, 0x37, 0xb5, 0x37, 0x2f, 0x03, 0x56, 0x01 };
            k.Feed(noise.Concat(Message(30_124_466, false)).ToArray(), t);
            k.Feed(Message(30_124_883, true), t.AddSeconds(1));
            k.Feed(Message(30_125_300, true).Concat(noise).ToArray(), t.AddSeconds(2));
            Check(seen.Count == 3 && seen[0].Change == 0 && seen[0].Balance == null && seen[1].Change == 417 && seen[1].Balance == 30_124_883 && seen[2].Change == 417 && seen[2].Balance == 30_125_300,
                $"the balance message: the long shape gives a change, a plain one the balance: {string.Join(", ", seen.Select(s => $"{s.Change:+0;-0;0}={s.Balance}"))}");
            // cut across two segments, anywhere in it
            var m = Message(30_125_364, false, 0x9b);
            k.Feed(m[..7], t.AddSeconds(3)); k.Feed(m[7..], t.AddSeconds(3));
            Check(seen.Count == 4 && seen[3].Change == 64 && seen[3].Balance == 30_125_364, $"...cut across two segments: +{seen[^1].Change}, balance {seen[^1].Balance:N0}");
            // spent, and the low 24 bits rolling over
            k.Feed(Message(30_120_364, false), t.AddSeconds(4));
            Check(seen[^1].Change == -5_000 && seen[^1].Balance == 30_120_364, $"...spent: {seen[^1].Change:N0}");
            var r = new KinahBalance(); var rs = new List<KinahBalance.Seen>(); r.Changed += rs.Add;
            r.Feed(Message(0x01FFFFF0, true), t); r.Feed(Message(0x02000010, false), t);
            Check(rs[^1].Change == 32 && rs[^1].Balance == 0x02000010, $"...the low 24 bits rolling over: +{rs[^1].Change}, balance {rs[^1].Balance:N0}");
            // a gap: a cut message is dropped, and the next one's change still covers what was missed
            k.Feed(Message(30_121_000, true)[..10], t.AddSeconds(5)); k.Gap(); k.Feed(Message(30_121_500, true), t.AddSeconds(6));
            Check(seen[^1].Change == 1_136 && seen[^1].Balance == 30_121_500, $"...after lost bytes: the next message carries the change over the gap (+{seen[^1].Change:N0})");
        }

        // the stream: in order, out of order, twice, overlapping, and a hole given up on
        {
            var s = new TcpStream(); var got = new List<byte>(); int gaps = 0;
            s.OnData = (d, _) => got.AddRange(d); s.OnGap = () => gaps++;
            byte[] B(int from, int n) => Enumerable.Range(from, n).Select(i => (byte)i).ToArray();
            s.Add(1000, B(0, 10), t);
            s.Add(1020, B(20, 10), t);          // early
            s.Add(1010, B(10, 10), t);          // fills the hole
            s.Add(1010, B(10, 10), t);          // again
            s.Add(1025, B(25, 10), t);          // overlaps the end
            Check(got.SequenceEqual(B(0, 35)) && gaps == 0, $"the stream puts segments in order once each: {got.Count} bytes");
            s.Add(1045, B(45, 5), t.AddSeconds(1));   // 1035..1045 never comes
            s.Add(1050, B(50, 5), t.AddSeconds(4));
            Check(gaps == 1 && got.Count == 45 && got[^1] == 54, $"...and gives up on a hole after {TcpStream.GapSeconds:0} s: {gaps} gap, {got.Count} bytes");
            var w = new TcpStream(); var wg = new List<byte>(); w.OnData = (d, _) => wg.AddRange(d);
            w.Add(0xFFFFFFFA, B(0, 6), t); w.Add(0, B(6, 4), t);
            Check(wg.SequenceEqual(B(0, 10)), "...across the sequence number rolling over");
        }

        // a segment and a frame: Ethernet with a VLAN tag, the TCP header's own length
        var server = IPAddress.Parse("193.202.112.2"); var me = IPAddress.Parse("192.168.1.4");
        {
            var ip = Ip(server, 13328, me, 57735, 0x01020304, Message(30_125_300, true));
            var eth = new byte[12].Concat(new byte[] { 0x81, 0x00, 0x00, 0x05, 0x08, 0x00 }).Concat(ip).ToArray();
            var back = NpcapSource.IpOf(eth, 1);
            var seg = back == null ? null : TcpSegment.Parse(back);
            Check(seg is { SrcPort: 13328, DstPort: 57735, Seq: 0x01020304 } sg && sg.Src.Equals(server) && sg.Payload.Length == Message(1, true).Length,
                $"a frame from the card: {seg?.Src}:{seg?.SrcPort} → {seg?.Dst}:{seg?.DstPort}, {seg?.Payload.Length} bytes");
        }

        // the whole way, from a capture file: two balance messages, the second split over two segments, a packet twice
        {
            var dir = TestTemp.Path("KinahLedger-packet-test"); Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, "capture.pcapng");
            var a = Message(30_125_300, true); var b = Message(30_125_364, false, 0x9b);
            var pk = new List<(DateTime, byte[])>
            {
                (t, Ip(server, 13328, me, 57735, 5000, a)),
                (t.AddSeconds(1), Ip(server, 13328, me, 57735, 5000 + (uint)a.Length, b[..9])),
                (t.AddSeconds(1), Ip(server, 13328, me, 57735, 5000 + (uint)a.Length, b[..9])),
                (t.AddSeconds(1), Ip(server, 13328, me, 57735, 5000 + (uint)a.Length + 9, b[9..])),
                (t.AddSeconds(1), Ip(me, 57735, server, 13328, 9000, Message(1, true))),   // this PC to the server: not read
            };
            Pcapng.Write(file, pk.Select(p => (p.Item1, new byte[12].Concat(new byte[] { 0x08, 0x00 }).Concat(p.Item2).ToArray())));
            var seen = Replay(file, 13328, quiet: true);
            Check(seen.Count == 2 && seen[0].Balance == 30_125_300 && seen[1].Change == 64 && seen[1].Balance == 30_125_364,
                $"a capture file, start to end: {string.Join(", ", seen.Select(s => $"{s.Change:+0;-0;0} → {s.Balance:N0}"))}");
            try { Directory.Delete(dir, true); } catch (Exception) { }
        }

        Check(TcpTable.GameServer(new[] { new TcpTable.Link(me, 52139, IPAddress.Parse("34.149.33.84"), 443, 1), new TcpTable.Link(me, 57735, server, 13328, 1), new TcpTable.Link(IPAddress.Loopback, 57066, IPAddress.Loopback, 57065, 1) }) is { RemotePort: 13328 },
            "the game's server connection: not the web (443) and not this PC");
        Console.WriteLine(_fails == 0 ? "packet self-test passed" : $"packet self-test: {_fails} failed");
        return _fails == 0 ? 0 : 1;
    }

    /// <summary>
    /// packet-live: the reader against the game running now, for <paramref name="seconds"/>: which connection, which source
    /// (Npcap or a raw socket), how many packets, and each balance message as it comes. Logs to the test folder, never the player's.
    /// </summary>
    public static int Live(IEnumerable<string> processHints, int seconds)
    {
        Ocr.GameWindow.Find(processHints);
        var pids = Ocr.GameWindow.Pids;
        Console.WriteLine($"game processes: {(pids.Count == 0 ? "none" : string.Join(", ", pids))} · Npcap {(PacketSources.NpcapInstalled ? "installed" : "not installed")} · administrator {(PacketSources.IsAdmin() ? "yes" : "no")}");
        var link = TcpTable.GameServer(TcpTable.Of(pids));
        if (link == null) { Console.WriteLine("no game server connection: is a character logged in?"); return 1; }
        Console.WriteLine($"game server: {link.Remote}:{link.RemotePort} → {link.Local}:{link.LocalPort}");
        using var src = PacketSources.Open(link, out var why);
        if (src == null) { Console.WriteLine("no packet source: " + why); return 1; }
        Console.WriteLine($"reading with {src.Kind} for {seconds} s; pick up some Kinah to see the balance");
        var k = new KinahBalance();
        k.Changed += s => Console.WriteLine($"  {s.At.ToLocalTime():HH:mm:ss.ff}  Kinah {(s.Change >= 0 ? "+" : "−")}{Math.Abs(s.Change):N0}  {(s.Balance is long b ? $"balance {b:N0}" : "(balance not seen whole yet)")}");
        var st = new TcpStream { OnData = k.Feed, OnGap = k.Gap };
        long packets = 0;
        var end = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < end)
        {
            if (src.Next() is not { } p || TcpSegment.Parse(p.Ip) is not { } s) continue;
            if (s.SrcPort != link.RemotePort || s.DstPort != link.LocalPort || !s.Src.Equals(link.Remote)) continue;
            packets++;
            st.Add(s.Seq, s.Payload, p.At, s.Syn);
        }
        Console.WriteLine($"{packets:N0} packets, {st.Bytes:N0} bytes, {st.Gaps} gaps, {k.Messages} balance messages");
        return packets > 0 ? 0 : 1;
    }

    /// <summary>An IPv4 TCP packet carrying <paramref name="payload"/>.</summary>
    internal static byte[] Ip(IPAddress src, int sport, IPAddress dst, int dport, uint seq, byte[] payload)
    {
        int total = 40 + payload.Length;
        var p = new byte[total];
        p[0] = 0x45; p[2] = (byte)(total >> 8); p[3] = (byte)total; p[8] = 64; p[9] = 6;
        src.GetAddressBytes().CopyTo(p, 12); dst.GetAddressBytes().CopyTo(p, 16);
        p[20] = (byte)(sport >> 8); p[21] = (byte)sport; p[22] = (byte)(dport >> 8); p[23] = (byte)dport;
        p[24] = (byte)(seq >> 24); p[25] = (byte)(seq >> 16); p[26] = (byte)(seq >> 8); p[27] = (byte)seq;
        p[32] = 5 << 4; p[33] = 0x18;
        payload.CopyTo(p, 40);
        return p;
    }

    /// <summary>
    /// The reader over a capture file: the server's side of the connection on <paramref name="serverPort"/> (the busiest one
    /// when there are several), every balance message it finds. Prints them unless quiet.
    /// </summary>
    public static List<KinahBalance.Seen> Replay(string pcapng, int serverPort, bool quiet = false)
    {
        var segs = new List<(DateTime At, TcpSegment S)>();
        foreach (var (at, frame, link) in Pcapng.Read(pcapng))
            if (NpcapSource.IpOf(frame, link) is { } ip && TcpSegment.Parse(ip) is { } s && s.SrcPort == serverPort) segs.Add((at, s));
        var busiest = segs.GroupBy(x => (x.S.Src.ToString(), x.S.DstPort)).OrderByDescending(g => g.Sum(x => x.S.Payload.Length)).FirstOrDefault();
        var seen = new List<KinahBalance.Seen>();
        if (busiest == null) { if (!quiet) Console.WriteLine($"no packets from port {serverPort}"); return seen; }
        var k = new KinahBalance(); k.Changed += seen.Add;
        var st = new TcpStream { OnData = k.Feed, OnGap = k.Gap };
        foreach (var (at, s) in busiest) st.Add(s.Seq, s.Payload, at, s.Syn);
        if (!quiet)
        {
            foreach (var s in seen)
                Console.WriteLine($"{s.At.ToLocalTime():HH:mm:ss.ff}  {(s.Change >= 0 ? "+" : "−")}{Math.Abs(s.Change),7:N0}  {(s.Balance is long b ? b.ToString("N0") : "(balance not seen whole yet)"),14}{(s.Plain ? "" : "  long shape")}");
            Console.WriteLine($"{seen.Count} balance messages from {busiest.Key.Item1}:{serverPort}, {st.Bytes:N0} bytes, {st.Gaps} gaps; Kinah gained {seen.Where(s => s.Change > 0).Sum(s => s.Change):N0}, spent {-seen.Where(s => s.Change < 0).Sum(s => s.Change):N0}");
        }
        return seen;
    }
}

/// <summary>Just enough pcapng to read pktmon's and Wireshark's captures and to write a test one (Ethernet, microseconds).</summary>
public static class Pcapng
{
    public static IEnumerable<(DateTime At, byte[] Frame, int Link)> Read(string path)
    {
        var d = File.ReadAllBytes(path);
        int pos = 0;
        var ifaces = new List<(int Link, double Res)>();
        while (pos + 12 <= d.Length)
        {
            uint type = BitConverter.ToUInt32(d, pos), len = BitConverter.ToUInt32(d, pos + 4);
            if (len < 12 || pos + len > d.Length) yield break;
            int body = pos + 8;
            if (type == 0x0A0D0D0A) ifaces.Clear();
            else if (type == 1)
            {
                int link = BitConverter.ToUInt16(d, body); double res = 1e-6;
                for (int o = body + 8; o + 4 <= pos + len - 4;)
                {
                    int code = BitConverter.ToUInt16(d, o), ol = BitConverter.ToUInt16(d, o + 2);
                    if (code == 0) break;
                    if (code == 9 && ol >= 1) { byte v = d[o + 4]; res = (v & 0x80) != 0 ? Math.Pow(2, -(v & 0x7F)) : Math.Pow(10, -v); }
                    o += 4 + ((ol + 3) & ~3);
                }
                ifaces.Add((link, res));
            }
            else if (type == 6 && ifaces.Count > 0)
            {
                int iid = (int)BitConverter.ToUInt32(d, body);
                ulong ts = ((ulong)BitConverter.ToUInt32(d, body + 4) << 32) | BitConverter.ToUInt32(d, body + 8);
                int cap = (int)BitConverter.ToUInt32(d, body + 12);
                var (link, res) = ifaces[iid < ifaces.Count ? iid : 0];
                var at = DateTime.UnixEpoch.AddTicks((long)(ts * res * TimeSpan.TicksPerSecond));
                yield return (at, d.AsSpan(body + 20, Math.Min(cap, (int)len - 32)).ToArray(), link);
            }
            pos += (int)len;
        }
    }

    public static void Write(string path, IEnumerable<(DateTime At, byte[] Frame)> frames)
    {
        using var w = new BinaryWriter(File.Create(path));
        void Block(uint type, byte[] body)
        {
            int pad = (4 - body.Length % 4) % 4, len = 12 + body.Length + pad;
            w.Write(type); w.Write(len); w.Write(body); w.Write(new byte[pad]); w.Write(len);
        }
        Block(0x0A0D0D0A, BitConverter.GetBytes(0x1A2B3C4Du).Concat(BitConverter.GetBytes((ushort)1)).Concat(BitConverter.GetBytes((ushort)0)).Concat(BitConverter.GetBytes(-1L)).ToArray());
        Block(1, BitConverter.GetBytes((ushort)1).Concat(BitConverter.GetBytes((ushort)0)).Concat(BitConverter.GetBytes(0u)).ToArray());
        foreach (var (at, f) in frames)
        {
            ulong us = (ulong)((at - DateTime.UnixEpoch).Ticks / 10);
            Block(6, BitConverter.GetBytes(0u).Concat(BitConverter.GetBytes((uint)(us >> 32))).Concat(BitConverter.GetBytes((uint)us))
                .Concat(BitConverter.GetBytes((uint)f.Length)).Concat(BitConverter.GetBytes((uint)f.Length)).Concat(f).ToArray());
        }
    }
}
