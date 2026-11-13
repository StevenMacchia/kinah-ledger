namespace KinahLedger.Packets;

/// <summary>
/// The Kinah balance in the game server's messages (packet probe, 2026-11-13, kinah-bench/packets/findings.md): a message
/// that starts <c>56 01 01 00 ?? ?? ?? ?? 00 78 01 00 3f 33 70 37</c> (the four ?? bytes looked like the character's id)
/// and goes on with the balance. Its low 24 bits rose by exactly every "Acquired N Kina." the chat said, about a second
/// before the chat said it, and the whole figure was the balance the screen showed (30,131,180 at 16:45:43).
///
/// Two shapes of it came in:
/// <list type="bullet">
/// <item>a plain one, where the balance is a 32-bit number followed by <c>00 00 00 00 01 00 00 00</c>: the whole balance;</item>
/// <item>another, with more fields after it, where only the low 24 bits read as the balance (the byte after them is something
///   else). Those are counted as the change in the low 24 bits, which is exact for any change under 8.3M either way.</item>
/// </list>
/// Not mapped yet: what the second shape is (Kinah with a kill credit, it looked like) and whether bound Kinah has its own.
/// </summary>
public sealed class KinahBalance
{
    static readonly byte[] Head = { 0x56, 0x01, 0x01, 0x00 };
    static readonly byte[] Tail = { 0x00, 0x78, 0x01, 0x00, 0x3f, 0x33, 0x70, 0x37 };
    static readonly byte[] PlainAfter = { 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00 };
    /// <summary>Head, 4 bytes, tail, the balance's 4 bytes and the 8 that tell the plain shape.</summary>
    const int Need = 4 + 4 + 8 + 4 + 8;

    /// <summary>A balance message: the change since the last one (0 for the first), and the whole balance when the message has it or it is known.</summary>
    public sealed record Seen(DateTime At, long Change, long? Balance, bool Plain);

    readonly List<byte> _carry = new();
    uint? _lastLow;
    public long? Balance { get; private set; }
    public int Messages { get; private set; }
    public event Action<Seen>? Changed;

    /// <summary>Bytes of the server's stream, in order.</summary>
    public void Feed(byte[] data, DateTime at)
    {
        _carry.AddRange(data);
        var b = _carry.ToArray();
        int i = 0;
        for (; i + Need <= b.Length; i++)
        {
            if (b[i] != Head[0] || !b.AsSpan(i, 4).SequenceEqual(Head) || !b.AsSpan(i + 8, 8).SequenceEqual(Tail)) continue;
            int v = i + 16;
            uint low = (uint)(b[v] | (b[v + 1] << 8) | (b[v + 2] << 16));
            bool plain = b.AsSpan(v + 4, 8).SequenceEqual(PlainAfter);
            Take(at, low, plain ? low | ((uint)b[v + 3] << 24) : null);
            i += Need - 1;
        }
        // keep what wasn't looked at yet: the start of a message cut across two segments (under Need bytes)
        _carry.RemoveRange(0, Math.Min(i, b.Length));
    }

    /// <summary>Lost bytes: a message may have been cut, so the carry goes; the balance's low bits still give the right change after it.</summary>
    public void Gap() => _carry.Clear();

    void Take(DateTime at, uint low, uint? whole)
    {
        Messages++;
        long change = 0;
        if (_lastLow is uint last)
        {
            change = (low - last) & 0xFFFFFF;
            if (change >= 0x800000) change -= 0x1000000;   // spent
        }
        _lastLow = low;
        if (whole is uint w) Balance = w;
        else if (Balance is long known) Balance = known + change;
        Changed?.Invoke(new Seen(at, change, Balance, whole != null));
    }
}
