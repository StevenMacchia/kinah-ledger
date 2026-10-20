using System.Text;
using KinahLedger.Capture;

namespace KinahLedger.Protocol;

public readonly record struct RecordEntry(long UnixMs, byte Kind, byte[] Data)
{
    public const byte Marker = 255;
    public bool IsMarker => Kind == Marker;
    public bool FromServer => Kind == (byte)Direction.ServerToClient;
}

/// <summary>
/// Binary log of the reassembled stream plus typed notes, used to map the protocol offline.
/// Layout: magic "KLREC1\n", then records of: int64 unix ms | byte kind (0 client→server, 1 server→client, 255 note) | int32 length | bytes.
/// Recordings contain whatever the server sent, which can include character names, so they are shared only by choice.
/// </summary>
public sealed class FrameRecorder : IDisposable
{
    static readonly byte[] Magic = Encoding.ASCII.GetBytes("KLREC1\n");
    readonly BinaryWriter _w;
    readonly object _lock = new();

    public long Bytes { get; private set; }
    public long Records { get; private set; }

    public FrameRecorder(string path)
    {
        _w = new BinaryWriter(new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read, 1 << 16));
        _w.Write(Magic);
    }

    public void Write(Direction dir, byte[] data) => Put((byte)dir, data);
    public void Marker(string note) => Put(RecordEntry.Marker, Encoding.UTF8.GetBytes(note));

    void Put(byte kind, byte[] data)
    {
        lock (_lock)
        {
            _w.Write(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            _w.Write(kind);
            _w.Write(data.Length);
            _w.Write(data);
            Bytes += data.Length;
            Records++;
        }
    }

    public void Flush() { lock (_lock) _w.Flush(); }
    public void Dispose() { lock (_lock) _w.Dispose(); }

    public static IEnumerable<RecordEntry> Read(string path)
    {
        using var r = new BinaryReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
        var magic = r.ReadBytes(Magic.Length);
        if (!magic.AsSpan().SequenceEqual(Magic)) throw new InvalidDataException("not a .klrec file");
        while (r.BaseStream.Position < r.BaseStream.Length)
        {
            long ms = r.ReadInt64();
            byte kind = r.ReadByte();
            int len = r.ReadInt32();
            if (len < 0 || len > (64 << 20)) yield break;
            yield return new RecordEntry(ms, kind, r.ReadBytes(len));
        }
    }
}
