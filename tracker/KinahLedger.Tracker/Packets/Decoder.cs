using System.Runtime.InteropServices;

namespace KinahLedger.Packets;

/// <summary>The currencies counted from the game's traffic.</summary>
public enum CurrencyKind { Kinah = 0, BoundKinah = 1, AbyssPoints = 2 }

/// <summary>A change of a currency: how much, the amount now, and whether it is the first seen on a connection (a baseline, no change known).</summary>
public sealed record CurrencyChange(DateTime At, CurrencyKind What, long Delta, long Amount, bool First);

/// <summary>
/// What turns the game server's stream into currency changes. The decoder is not part of this source: release builds carry
/// it as a native library (klnet.dll), and a build without it reads no traffic (the screen counts, as before).
/// </summary>
public interface IGameDecoder : IDisposable
{
    /// <summary>A new connection: the next amount of each currency is a baseline.</summary>
    void Reset();
    /// <summary>Bytes were lost: what follows doesn't continue what came before.</summary>
    void Gap();
    /// <summary>The server's stream, in order: one segment's bytes and when they came.</summary>
    void Feed(byte[] data, DateTime at);
    /// <summary>The next change found, or null.</summary>
    CurrencyChange? Next();
    (long Messages, long Bundles, int Resyncs, int CurrencyMessages) Stats { get; }
}

/// <summary>The release build's decoder, klnet.dll, through its C interface.</summary>
public sealed unsafe class NativeDecoder : IGameDecoder
{
    [StructLayout(LayoutKind.Sequential)] struct KlChange { public int Kind; public int First; public long Delta; public long Amount; public long AtUtcTicks; }
    [StructLayout(LayoutKind.Sequential)] struct KlStats { public long Messages; public long Bundles; public int Resyncs; public int CurrencyMessages; }

    [DllImport("klnet", EntryPoint = "kl_abi")] static extern int Abi();
    [DllImport("klnet", EntryPoint = "kl_open")] static extern nint Open();
    [DllImport("klnet", EntryPoint = "kl_close")] static extern void Close(nint h);
    [DllImport("klnet", EntryPoint = "kl_reset")] static extern void ResetN(nint h);
    [DllImport("klnet", EntryPoint = "kl_gap")] static extern void GapN(nint h);
    [DllImport("klnet", EntryPoint = "kl_feed")] static extern void FeedN(nint h, byte* data, int len, long atUtcTicks);
    [DllImport("klnet", EntryPoint = "kl_next")] static extern int NextN(nint h, KlChange* change);
    [DllImport("klnet", EntryPoint = "kl_stats")] static extern int StatsN(nint h, KlStats* stats);

    static bool? _have;
    /// <summary>True when this build carries the decoder (klnet.dll, its interface version 1).</summary>
    public static bool Available
    {
        get
        {
            if (_have is bool h) return h;
            try { _have = Abi() == 1; }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException) { _have = false; }
            return _have.Value;
        }
    }

    /// <summary>A decoder, or null when this build has none.</summary>
    public static IGameDecoder? TryOpen()
    {
        if (!Available) return null;
        var h = Open();
        return h == 0 ? null : new NativeDecoder(h);
    }

    nint _h;
    NativeDecoder(nint h) => _h = h;

    public void Reset() { if (_h != 0) ResetN(_h); }
    public void Gap() { if (_h != 0) GapN(_h); }
    public void Feed(byte[] data, DateTime at)
    {
        if (_h == 0 || data.Length == 0) return;
        fixed (byte* p = data) FeedN(_h, p, data.Length, at.ToUniversalTime().Ticks);
    }
    public CurrencyChange? Next()
    {
        if (_h == 0) return null;
        KlChange c;
        if (NextN(_h, &c) != 1 || c.Kind is < 0 or > 2) return null;
        return new CurrencyChange(new DateTime(c.AtUtcTicks, DateTimeKind.Utc), (CurrencyKind)c.Kind, c.Delta, c.Amount, c.First != 0);
    }
    public (long Messages, long Bundles, int Resyncs, int CurrencyMessages) Stats
    {
        get { KlStats s = default; if (_h != 0) StatsN(_h, &s); return (s.Messages, s.Bundles, s.Resyncs, s.CurrencyMessages); }
    }
    public void Dispose() { if (_h != 0) { Close(_h); _h = 0; } }
}
