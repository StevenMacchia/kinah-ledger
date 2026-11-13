using System.Net;

namespace KinahLedger.Packets;

/// <summary>An IPv4 TCP segment's parts the reader needs.</summary>
public readonly record struct TcpSegment(IPAddress Src, int SrcPort, IPAddress Dst, int DstPort, uint Seq, bool Syn, bool Fin, bool Rst, byte[] Payload)
{
    public static TcpSegment? Parse(ReadOnlySpan<byte> ip)
    {
        if (ip.Length < 20 || ip[0] >> 4 != 4 || ip[9] != 6) return null;
        int ihl = (ip[0] & 0x0F) * 4, total = (ip[2] << 8) | ip[3];
        int end = total > 0 && total <= ip.Length ? total : ip.Length;
        if (end < ihl + 20) return null;
        var t = ip[ihl..end];
        int doff = (t[12] >> 4) * 4;
        if (doff < 20 || doff > t.Length) return null;
        byte flags = t[13];
        return new TcpSegment(new IPAddress(ip.Slice(12, 4)), (t[0] << 8) | t[1], new IPAddress(ip.Slice(16, 4)), (t[2] << 8) | t[3],
            (uint)((t[4] << 24) | (t[5] << 16) | (t[6] << 8) | t[7]), (flags & 0x02) != 0, (flags & 0x01) != 0, (flags & 0x04) != 0, t[doff..].ToArray());
    }
}

/// <summary>
/// One direction of a TCP connection put back in order: segments arrive out of order, twice, or not at all, and the bytes
/// come out once each, in order. A segment missing for <see cref="GapSeconds"/> is given up on and the stream goes on
/// after it (a gap: the decoder resyncs on its next marker). Joins a connection already running at its first segment.
/// </summary>
public sealed class TcpStream
{
    public const double GapSeconds = 2;
    const int MaxPending = 512;

    uint? _next;
    readonly Dictionary<uint, (byte[] Data, DateTime At)> _pending = new();
    /// <summary>Bytes in order, with the time of the segment they came in.</summary>
    public Action<byte[], DateTime>? OnData;
    /// <summary>Some bytes were lost: what follows doesn't continue what came before.</summary>
    public Action? OnGap;
    public int Gaps { get; private set; }
    public long Bytes { get; private set; }

    public void Add(uint seq, byte[] data, DateTime at, bool syn = false)
    {
        if (syn) { _next = seq + 1; _pending.Clear(); return; }
        if (data.Length == 0) return;
        if (_next is not uint next) { _next = seq; next = seq; }
        int d = unchecked((int)(seq - next));
        if (d < 0)
        {
            if (d + data.Length <= 0) return;   // already had all of it
            data = data[(-d)..];
            seq = next;
            d = 0;
        }
        if (d == 0) { Deliver(data, at); Drain(); }
        else if (!_pending.ContainsKey(seq)) _pending[seq] = (data, at);
        // a hole nobody is going to fill: skip to what came after it
        while (_pending.Count > 0 && (_pending.Count > MaxPending || (at - _pending.Values.Min(p => p.At)).TotalSeconds > GapSeconds))
        {
            uint first = _pending.Keys.OrderBy(k => unchecked((int)(k - _next!.Value))).First();
            _next = first;
            Gaps++;
            OnGap?.Invoke();
            Drain();
        }
    }

    void Deliver(byte[] data, DateTime at)
    {
        _next = unchecked(_next!.Value + (uint)data.Length);
        Bytes += data.Length;
        OnData?.Invoke(data, at);
    }

    void Drain()
    {
        bool moved = true;
        while (moved && _pending.Count > 0)
        {
            moved = false;
            foreach (var (seq, (data, at)) in _pending.ToList())
            {
                int d = unchecked((int)(seq - _next!.Value));
                if (d > 0) continue;
                _pending.Remove(seq);
                if (d + data.Length <= 0) { moved = true; continue; }
                Deliver(data[(-d)..], at);
                moved = true;
            }
        }
    }
}
