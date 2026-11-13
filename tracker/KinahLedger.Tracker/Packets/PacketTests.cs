using System.Net;
using KinahLedger.Data;
using KinahLedger.Session;

namespace KinahLedger.Packets;

/// <summary>
/// packet-test: the packet reader's own parts (segments out of order, twice and lost, frames from a card, a capture file,
/// the game's connection) and the counting: a change from the game's traffic counts where a chat line would, the chat's line
/// for it doesn't, a baseline's own pickup is the chat's, a sale counts nothing, and three misses hand a currency back to
/// the screen. The decoder itself is tested where it lives. packet-replay and packet-live run the decoder when the build has it.
/// </summary>
public static class PacketTests
{
    static int _fails;
    static void Check(bool ok, string what) { Console.WriteLine((ok ? "ok    " : "FAIL  ") + what); if (!ok) _fails++; }

    public static int Run(Settings settings)
    {
        _fails = 0;
        var t = new DateTime(2026, 11, 13, 21, 44, 43, DateTimeKind.Utc);

        // the stream: in order, out of order, twice, overlapping, a hole given up on, the sequence rolling over
        {
            var s = new TcpStream(); var got = new List<byte>(); int gaps = 0;
            s.OnData = (d, _) => got.AddRange(d); s.OnGap = () => gaps++;
            byte[] B(int from, int n) => Enumerable.Range(from, n).Select(i => (byte)i).ToArray();
            s.Add(1000, B(0, 10), t); s.Add(1020, B(20, 10), t); s.Add(1010, B(10, 10), t); s.Add(1010, B(10, 10), t); s.Add(1025, B(25, 10), t);
            Check(got.SequenceEqual(B(0, 35)) && gaps == 0, $"the stream puts segments in order once each: {got.Count} bytes");
            s.Add(1045, B(45, 5), t.AddSeconds(1)); s.Add(1050, B(50, 5), t.AddSeconds(4));
            Check(gaps == 1 && got.Count == 45 && got[^1] == 54, $"...and gives up on a hole after {TcpStream.GapSeconds:0} s: {gaps} gap, {got.Count} bytes");
            var w = new TcpStream(); var wg = new List<byte>(); w.OnData = (d, _) => wg.AddRange(d);
            w.Add(0xFFFFFFFA, B(0, 6), t); w.Add(0, B(6, 4), t);
            Check(wg.SequenceEqual(B(0, 10)), "...across the sequence number rolling over");
        }

        // a frame from the card: Ethernet with a VLAN tag, the TCP header's own length
        var server = IPAddress.Parse("193.202.112.2"); var me = IPAddress.Parse("192.168.1.4");
        var payload = Enumerable.Range(0, 32).Select(i => (byte)(i * 7)).ToArray();
        {
            var ip = Ip(server, 13328, me, 57735, 0x01020304, payload);
            var eth = new byte[12].Concat(new byte[] { 0x81, 0x00, 0x00, 0x05, 0x08, 0x00 }).Concat(ip).ToArray();
            var seg = NpcapSource.IpOf(eth, 1) is { } back ? TcpSegment.Parse(back) : null;
            Check(seg is { SrcPort: 13328, DstPort: 57735, Seq: 0x01020304 } sg && sg.Src.Equals(server) && sg.Payload.SequenceEqual(payload),
                $"a frame from the card: {seg?.Src}:{seg?.SrcPort} → {seg?.Dst}:{seg?.DstPort}, {seg?.Payload.Length} bytes");
        }

        // a capture file read back: the server's side of the connection put together, this PC's own packets left out
        {
            var dir = TestTemp.Path("KinahLedger-packet-test"); Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, "capture.pcapng");
            var pk = new List<(DateTime, byte[])>
            {
                (t, Ip(server, 13328, me, 57735, 5000, payload[..20])),
                (t.AddSeconds(1), Ip(server, 13328, me, 57735, 5020, payload[20..])),
                (t.AddSeconds(1), Ip(server, 13328, me, 57735, 5020, payload[20..])),
                (t.AddSeconds(1), Ip(me, 57735, server, 13328, 9000, payload)),
            };
            Pcapng.Write(file, pk.Select(p => (p.Item1, new byte[12].Concat(new byte[] { 0x08, 0x00 }).Concat(p.Item2).ToArray())));
            var down = new List<byte>(); var st = new TcpStream { OnData = (d, _) => down.AddRange(d) };
            int frames = 0;
            foreach (var (at, frame, link) in Pcapng.Read(file))
            {
                frames++;
                if (NpcapSource.IpOf(frame, link) is { } ip && TcpSegment.Parse(ip) is { SrcPort: 13328 } s) st.Add(s.Seq, s.Payload, at, s.Syn);
            }
            Check(frames == 4 && down.SequenceEqual(payload), $"a capture file read back: {frames} frames, the server's {down.Count} bytes once each and in order");
            try { Directory.Delete(dir, true); } catch (Exception) { }
        }

        // the counting: a change counts where a chat line would, the chat's own line for it doesn't, the baseline's own
        // pickup is the chat's, a sale counts nothing, and three misses hand a currency back to the screen
        {
            var items = ItemDictionary.LoadOrEmpty(settings.ItemsFile);
            var reader = new PacketReader(); reader.ReadingForTest();
            var s = new TrackerSession(null, settings, items, "Test") { Packets = reader };
            var t0 = DateTime.UtcNow.AddMinutes(-3);
            s.Engine.SetZone("Western Cantas Valley");
            reader.AmountForTest(CurrencyKind.Kinah, 30_000_000, t0);
            s.CommitForTest(new KinahChanged(t0.AddSeconds(1.5), 300, false) { Via = "chat" });   // the baseline's own pickup: the chat's
            reader.AmountForTest(CurrencyKind.Kinah, 30_000_417, t0.AddSeconds(10));
            s.CommitForTest(new KinahChanged(t0.AddSeconds(11.2), 417, false) { Via = "chat" });   // the same pickup read off the chat
            s.CommitForTest(new KinahChanged(t0.AddSeconds(11.5), 5_193, false) { Via = "chat" });   // a misread
            reader.AmountForTest(CurrencyKind.Kinah, 30_000_936, t0.AddSeconds(12));   // the 519 the chat misread
            reader.AmountForTest(CurrencyKind.Kinah, 30_001_094, t0.AddSeconds(14));   // +158 the chat never read...
            s.CommitForTest(new CurrencyGained(t0.AddSeconds(15), "Fera Soul Points", 1) { Via = "chat" });   // ...though it read the kill
            s.DrainPacketsForTest();
            Check(s.Engine.Kinah == 300 + 417 + 519 + 158, $"Kinah counted from the game's traffic: {s.Engine.Kinah:N0} (300 from the chat before the baseline, then 417 + 519 + 158; the chat's 417 and its misread 5,193 left out)");
            // bound Kinah and Abyss Points in the Abyss
            s.Engine.SetZone("Chaotic Lower Reshanta");
            reader.AmountForTest(CurrencyKind.BoundKinah, 2_946_591, t0.AddSeconds(20)); reader.AmountForTest(CurrencyKind.AbyssPoints, 31_422, t0.AddSeconds(20));
            reader.AmountForTest(CurrencyKind.BoundKinah, 2_946_869, t0.AddSeconds(30)); reader.AmountForTest(CurrencyKind.AbyssPoints, 31_434, t0.AddSeconds(30));
            s.CommitForTest(new CurrencyGained(t0.AddSeconds(31), "Abyss Points", 7) { Via = "chat" });   // "Acquired M 7 Abyss Point(s)"
            s.CommitForTest(new KinahChanged(t0.AddSeconds(31), 278, true) { Via = "chat" });
            s.DrainPacketsForTest();
            Check(s.Engine.KinahBound == 278 && s.Engine.AbyssPoints == 12, $"...bound Kinah +{s.Engine.KinahBound} and Abyss Points +{s.Engine.AbyssPoints} from the game's traffic, the chat's misread 7 left out");
            // a sale (an NPC in town, nothing killed around it) and a rise in a base are no pickups
            s.Engine.SetZone("Western Cantas Valley");
            reader.AmountForTest(CurrencyKind.Kinah, 30_001_594, t0.AddSeconds(50)); s.DrainPacketsForTest();
            s.Engine.SetZone("Test Base");
            reader.AmountForTest(CurrencyKind.Kinah, 30_002_094, t0.AddSeconds(52)); s.CommitForTest(new CurrencyGained(t0.AddSeconds(52), "Fera Soul Points", 1) { Via = "chat" }); s.DrainPacketsForTest();
            Check(s.Engine.Kinah == 1_394, $"...a rise with nothing killed around it (a sale) and one in a base count nothing: {s.Engine.Kinah:N0}");
            // three chat pickups the traffic never showed: Kinah goes back to the screen
            s.Engine.SetZone("Western Cantas Valley");
            for (int i = 0; i < 3; i++) s.CommitForTest(new KinahChanged(t0.AddSeconds(70 + i * 20), 400 + i, false) { Via = "chat" });
            reader.AgreeForTest();
            bool broken = reader.Broken(CurrencyKind.Kinah) != null;
            s.CommitForTest(new KinahChanged(t0.AddSeconds(135), 500, false) { Via = "chat" });
            Check(broken && s.Engine.Kinah == 1_894 && reader.Broken(CurrencyKind.AbyssPoints) == null, $"...three chat pickups the game's traffic never showed: Kinah is the screen's again ({s.Engine.Kinah:N0}), Abyss Points stay the traffic's");
        }

        // a build without the decoder reads no traffic and says so
        {
            var keep = PacketReader.Decoders;
            PacketReader.Decoders = () => null;
            var r = new PacketReader();
            r.Tick(new uint[] { 1 }, null);   // no such game connection: it waits, it doesn't fail
            PacketReader.Decoders = keep;
            Check(!r.On && r.State.Length > 0, $"no game connection: \"{r.State}\"");
        }
        Console.WriteLine($"decoder in this build: {(NativeDecoder.Available ? "yes (klnet.dll)" : "no (a build from the public source: the screen counts)")}");

        Check(TcpTable.GameServer(new[] { new TcpTable.Link(me, 52139, IPAddress.Parse("34.149.33.84"), 443, 1), new TcpTable.Link(me, 57735, server, 13328, 1), new TcpTable.Link(IPAddress.Loopback, 57066, IPAddress.Loopback, 57065, 1) }) is { RemotePort: 13328 },
            "the game's server connection: not the web (443) and not this PC");
        Console.WriteLine(_fails == 0 ? "packet self-test passed" : $"packet self-test: {_fails} failed");
        return _fails == 0 ? 0 : 1;
    }

    /// <summary>packet-live: the reader against the game running now, for <paramref name="seconds"/>, each currency change as it comes.</summary>
    public static int Live(IEnumerable<string> processHints, int seconds)
    {
        Ocr.GameWindow.Find(processHints);
        var pids = Ocr.GameWindow.Pids;
        Console.WriteLine($"game processes: {(pids.Count == 0 ? "none" : string.Join(", ", pids))} · Npcap {(PacketSources.NpcapInstalled ? "installed" : "not installed")} · administrator {(PacketSources.IsAdmin() ? "yes" : "no")} · decoder {(NativeDecoder.Available ? "yes" : "no")}");
        using var dec = NativeDecoder.TryOpen();
        if (dec == null) { Console.WriteLine("this build has no decoder (klnet.dll)"); return 1; }
        var link = TcpTable.GameServer(TcpTable.Of(pids));
        if (link == null) { Console.WriteLine("no game server connection: is a character logged in?"); return 1; }
        Console.WriteLine($"game server: {link.Remote}:{link.RemotePort} → {link.Local}:{link.LocalPort}");
        using var src = PacketSources.Open(link, out var why);
        if (src == null) { Console.WriteLine("no packet source: " + why); return 1; }
        Console.WriteLine($"reading with {src.Kind} for {seconds} s");
        var st = new TcpStream { OnData = (d, at) => { dec.Feed(d, at); while (dec.Next() is { } c) Print(c); }, OnGap = dec.Gap };
        long packets = 0;
        var end = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < end)
        {
            if (src.Next() is not { } p || TcpSegment.Parse(p.Ip) is not { } s) continue;
            if (s.SrcPort != link.RemotePort || s.DstPort != link.LocalPort || !s.Src.Equals(link.Remote)) continue;
            packets++;
            st.Add(s.Seq, s.Payload, p.At, s.Syn);
        }
        var stats = dec.Stats;
        Console.WriteLine($"{packets:N0} packets, {st.Bytes:N0} bytes, {stats.Messages:N0} messages, {st.Gaps} gaps, {stats.Resyncs} resyncs, {stats.CurrencyMessages} currency messages");
        return packets > 0 ? 0 : 1;
    }

    static void Print(CurrencyChange c) =>
        Console.WriteLine($"  {c.At.ToLocalTime():HH:mm:ss.ff}  {c.What,-12} {(c.First ? "now" : (c.Delta >= 0 ? "+" : "−") + Math.Abs(c.Delta).ToString("N0")),8}  {c.Amount:N0}");

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
    /// The decoder over a capture file: every connection from <paramref name="serverPort"/> in turn (an event or the Abyss is
    /// another server), each its own decoder, every currency change, printed. Needs the decoder in the build.
    /// </summary>
    public static int Replay(string pcapng, int serverPort)
    {
        if (!NativeDecoder.Available) { Console.WriteLine("this build has no decoder (klnet.dll)"); return 1; }
        var segs = new List<(DateTime At, TcpSegment S)>();
        foreach (var (at, frame, link) in Pcapng.Read(pcapng))
            if (NpcapSource.IpOf(frame, link) is { } ip && TcpSegment.Parse(ip) is { } s && s.SrcPort == serverPort) segs.Add((at, s));
        var seen = new List<CurrencyChange>();
        foreach (var conn in segs.GroupBy(x => (x.S.Src.ToString(), x.S.DstPort)).OrderBy(g => g.First().At))
        {
            using var dec = NativeDecoder.TryOpen()!;
            var st = new TcpStream { OnData = (d, at) => { dec.Feed(d, at); while (dec.Next() is { } c) seen.Add(c); }, OnGap = dec.Gap };
            foreach (var (at, s) in conn) st.Add(s.Seq, s.Payload, at, s.Syn);
            var stats = dec.Stats;
            Console.WriteLine($"{conn.Key.Item1}:{serverPort} → :{conn.Key.DstPort}: {st.Bytes:N0} bytes, {stats.Messages:N0} messages, {st.Gaps} gaps, {stats.Resyncs} resyncs, {stats.CurrencyMessages} currency messages");
        }
        foreach (var c in seen) Print(c);
        foreach (var k in Enum.GetValues<CurrencyKind>())
            Console.WriteLine($"{k}: gained {seen.Where(c => c.What == k && c.Delta > 0).Sum(c => c.Delta):N0} in {seen.Count(c => c.What == k && c.Delta > 0)} changes");
        return 0;
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
