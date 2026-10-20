namespace KinahLedger.Capture;

/// <summary>
/// Turns captured TCP segments back into each direction's ordered byte stream.
/// Handles retransmits and out-of-order arrival; if a hole never fills it skips ahead and counts the loss.
/// </summary>
public sealed class TcpReassembler
{
    sealed class SeqComparer : IComparer<uint> { public int Compare(uint a, uint b) => unchecked((int)(a - b)); }

    sealed class Flow
    {
        public bool Init;
        public uint Next;
        public readonly SortedDictionary<uint, byte[]> Pending = new(new SeqComparer());
        public int PendingBytes;
    }

    const int MaxPendingBytes = 4 * 1024 * 1024;
    readonly Dictionary<string, Flow> _flows = new();

    public event Action<Direction, byte[]>? Data;
    public long Lost { get; private set; }

    public void Push(TcpSegment s)
    {
        if (!_flows.TryGetValue(s.FlowKey, out var f)) { f = new Flow(); _flows[s.FlowKey] = f; }
        if (!f.Init) { f.Init = true; f.Next = s.Seq; }

        int diff = unchecked((int)(s.Seq - f.Next));
        if (diff < 0)
        {
            // retransmission or overlap: only the part past what we already emitted is new
            if (-diff >= s.Payload.Length) return;
            Emit(f, s.Dir, s.Payload.AsSpan(-diff).ToArray());
        }
        else if (diff == 0)
        {
            Emit(f, s.Dir, s.Payload);
        }
        else
        {
            f.Pending[s.Seq] = s.Payload;
            f.PendingBytes += s.Payload.Length;
            if (f.PendingBytes > MaxPendingBytes) SkipGap(f);
        }
        Drain(f, s.Dir);
    }

    void Emit(Flow f, Direction d, byte[] bytes)
    {
        f.Next = unchecked(f.Next + (uint)bytes.Length);
        Data?.Invoke(d, bytes);
    }

    void Drain(Flow f, Direction d)
    {
        while (f.Pending.Count > 0)
        {
            var first = f.Pending.First();
            int diff = unchecked((int)(first.Key - f.Next));
            if (diff > 0) break;
            f.Pending.Remove(first.Key);
            f.PendingBytes -= first.Value.Length;
            if (-diff >= first.Value.Length) continue;
            Emit(f, d, diff == 0 ? first.Value : first.Value.AsSpan(-diff).ToArray());
        }
    }

    void SkipGap(Flow f)
    {
        // too much buffered behind a hole: give up on the missing bytes and resume at the earliest pending segment
        var first = f.Pending.First();
        Lost += unchecked((int)(first.Key - f.Next));
        f.Next = first.Key;
    }
}
