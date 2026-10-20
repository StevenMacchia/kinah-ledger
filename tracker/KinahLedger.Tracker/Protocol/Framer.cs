using KinahLedger.Capture;

namespace KinahLedger.Protocol;

/// <summary>One game message. Opcode is -1 when framing is still "raw".</summary>
public sealed record Frame(DateTime Time, Direction Dir, int Opcode, ReadOnlyMemory<byte> Body, ReadOnlyMemory<byte> Raw);

public interface IFramer
{
    IEnumerable<Frame> Push(Direction dir, byte[] data);
}

/// <summary>Passes each reassembled chunk through unchanged. Used until the message layout is mapped from recordings.</summary>
public sealed class RawChunkFramer : IFramer
{
    public IEnumerable<Frame> Push(Direction dir, byte[] data)
    {
        yield return new Frame(DateTime.UtcNow, dir, -1, data, data);
    }
}

/// <summary>Generic "length prefix, then opcode" framer, configured from settings.json once the layout is known.</summary>
public sealed class LengthPrefixFramer : IFramer
{
    readonly FramingSettings _s;
    readonly Dictionary<Direction, List<byte>> _buf = new()
    {
        [Direction.ClientToServer] = new List<byte>(),
        [Direction.ServerToClient] = new List<byte>()
    };

    public LengthPrefixFramer(FramingSettings s) { _s = s; }

    public IEnumerable<Frame> Push(Direction dir, byte[] data)
    {
        var b = _buf[dir];
        b.AddRange(data);
        var frames = new List<Frame>();
        int hdr = _s.HeaderOffset + _s.LengthBytes + _s.OpcodeBytes;
        while (b.Count >= hdr)
        {
            int len = ReadInt(b, _s.HeaderOffset, _s.LengthBytes, _s.LittleEndian);
            int total = _s.LengthIncludesHeader ? len : len + _s.HeaderOffset + _s.LengthBytes;
            if (total < hdr || total > (1 << 20))
            {
                // nonsense length: drop what we have and resync on the next chunk
                b.Clear();
                break;
            }
            if (b.Count < total) break;
            int op = ReadInt(b, _s.HeaderOffset + _s.LengthBytes, _s.OpcodeBytes, _s.LittleEndian);
            var raw = b.GetRange(0, total).ToArray();
            b.RemoveRange(0, total);
            frames.Add(new Frame(DateTime.UtcNow, dir, op, raw.AsMemory(hdr), raw));
        }
        return frames;
    }

    static int ReadInt(List<byte> b, int off, int n, bool le)
    {
        int v = 0;
        for (int i = 0; i < n; i++) v |= b[off + (le ? i : n - 1 - i)] << (8 * i);
        return v;
    }
}

public static class Framer
{
    public static IFramer FromSettings(FramingSettings s) =>
        s.Mode.Equals("length", StringComparison.OrdinalIgnoreCase) ? new LengthPrefixFramer(s) : new RawChunkFramer();
}
