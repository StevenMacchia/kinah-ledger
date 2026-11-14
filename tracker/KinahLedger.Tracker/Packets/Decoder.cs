using System.Runtime.InteropServices;

namespace KinahLedger.Packets;

/// <summary>The currencies counted from the game's traffic.</summary>
public enum CurrencyKind { Kinah = 0, BoundKinah = 1, AbyssPoints = 2 }

/// <summary>A change of a currency: how much, the amount now, and whether it is the first seen on a connection (a baseline, no change known).</summary>
public sealed record CurrencyChange(DateTime At, CurrencyKind What, long Delta, long Amount, bool First);

/// <summary>What happened to a stack of an item in the inventory.</summary>
public enum ItemOp { NewStack = 0, Changed = 1, Gone = 2 }

/// <summary>
/// A change of one stack of an item: the game's item id (the item list's), how much the stack changed and what it holds now.
/// First: the stack's earlier amount isn't known (the first change seen of it on a connection), so neither is the change.
/// Batch: the game's update it came in; the changes of one update came together (a craft's materials, a stack moved),
/// BatchRows of them, and BatchFell when something in that update went down.
/// </summary>
public sealed record ItemChange(DateTime At, long ItemId, ItemOp Op, long StackId, long Delta, long Total, bool First, int Batch, int BatchRows, bool BatchFell);

/// <summary>
/// A field boss as the game's world map lists it (the decoder's interface version 5). Key: which boss, the same every time
/// (opaque). Name, Place, Region: as the decoder knows them ("" when it doesn't). Time: for a dead boss its next spawn, for a
/// live one when it spawned, on the game server's clock; ServerNow: the server's clock when the list came (null when not
/// known yet), so the time left is Time minus ServerNow, whatever this PC's clock says.
/// </summary>
public sealed record BossSighting(DateTime At, DateTime? ServerNow, string Key, string Name, string Place, string Region, BossSighting.Kind State, DateTime? Time, bool First)
{
    public enum Kind { Dead = 0, Alive = 1, NoTimer = 2 }
}

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
    /// <summary>True when this decoder reads item changes too (klnet.dll's interface version 2).</summary>
    bool ReadsItems => false;
    /// <summary>The next item change found, or null (always null for a decoder that reads no items).</summary>
    ItemChange? NextItem() => null;
    /// <summary>The next field boss from the world map's boss list, or null (always null for a decoder without it).</summary>
    BossSighting? NextBoss() => null;
    (long Messages, long Bundles, int Resyncs, int CurrencyMessages) Stats { get; }
}

/// <summary>The release build's decoder, klnet.dll, through its C interface.</summary>
public sealed unsafe class NativeDecoder : IGameDecoder
{
    [StructLayout(LayoutKind.Sequential)] struct KlChange { public int Kind; public int First; public long Delta; public long Amount; public long AtUtcTicks; }
    [StructLayout(LayoutKind.Sequential)] struct KlItem { public long AtUtcTicks; public uint ItemId; public int Op; public long StackId; public long Delta; public long Total; public int First; public int Batch; public int BatchRows; public int BatchFell; }
    [StructLayout(LayoutKind.Sequential)] struct KlBoss
    {
        public long AtUtcTicks; public long ServerUtcMs; public long TimeUtcMs; public uint MapId; public uint BossId; public int State; public int Flags;
        public float X, Y, Z; public int Reserved;
        public fixed byte Name[48]; public fixed byte Place[48]; public fixed byte Region[24];
    }
    [StructLayout(LayoutKind.Sequential)] struct KlStats { public long Messages; public long Bundles; public int Resyncs; public int CurrencyMessages; }

    [DllImport("klnet", EntryPoint = "kl_abi")] static extern int Abi();
    [DllImport("klnet", EntryPoint = "kl_open")] static extern nint Open();
    [DllImport("klnet", EntryPoint = "kl_close")] static extern void Close(nint h);
    [DllImport("klnet", EntryPoint = "kl_reset")] static extern void ResetN(nint h);
    [DllImport("klnet", EntryPoint = "kl_gap")] static extern void GapN(nint h);
    [DllImport("klnet", EntryPoint = "kl_feed")] static extern void FeedN(nint h, byte* data, int len, long atUtcTicks);
    [DllImport("klnet", EntryPoint = "kl_next")] static extern int NextN(nint h, KlChange* change);
    [DllImport("klnet", EntryPoint = "kl_stats")] static extern int StatsN(nint h, KlStats* stats);
    [DllImport("klnet", EntryPoint = "kl_next_item")] static extern int NextItemN(nint h, KlItem* item);
    [DllImport("klnet", EntryPoint = "kl_next_boss")] static extern int NextBossN(nint h, KlBoss* boss);

    static int? _abi;
    /// <summary>klnet.dll's interface version: 0 when this build has none, 1 for currencies only, 2 with item changes, 5 with the field boss list (later versions keep every earlier export).</summary>
    public static int Version
    {
        get
        {
            if (_abi is int v) return v;
            try { int a = Abi(); _abi = a >= 1 ? a : 0; }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException) { _abi = 0; }
            return _abi.Value;
        }
    }
    /// <summary>True when this build carries the decoder (klnet.dll, any interface version).</summary>
    public static bool Available => Version > 0;

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
    public bool ReadsItems => Version >= 2;
    public ItemChange? NextItem()
    {
        if (_h == 0 || Version < 2) return null;
        KlItem c;
        if (NextItemN(_h, &c) != 1 || c.Op is < 0 or > 2) return null;
        return new ItemChange(new DateTime(c.AtUtcTicks, DateTimeKind.Utc), c.ItemId, (ItemOp)c.Op, c.StackId, c.Delta, c.Total, c.First != 0, c.Batch, c.BatchRows, c.BatchFell != 0);
    }
    // a dll that says 5 or more but was built without the boss list (versions are numbered as branches merge): asked once
    static bool _noBosses;
    public BossSighting? NextBoss()
    {
        if (_h == 0 || Version < 5 || _noBosses) return null;
        KlBoss b;
        try { if (NextBossN(_h, &b) != 1 || b.State is < 0 or > 2) return null; }
        catch (EntryPointNotFoundException) { _noBosses = true; return null; }
        static string Str(byte* p, int n) { int len = 0; while (len < n && p[len] != 0) len++; return System.Text.Encoding.UTF8.GetString(p, len); }
        static DateTime? Ms(long ms) => ms > 0 && ms < 4_102_444_800_000 ? DateTime.UnixEpoch.AddMilliseconds(ms) : null;
        return new BossSighting(new DateTime(b.AtUtcTicks, DateTimeKind.Utc), Ms(b.ServerUtcMs), $"{b.MapId}:{b.BossId}",
            Str(b.Name, 48), Str(b.Place, 48), Str(b.Region, 24), (BossSighting.Kind)b.State, Ms(b.TimeUtcMs), (b.Flags & 1) != 0);
    }
    public (long Messages, long Bundles, int Resyncs, int CurrencyMessages) Stats
    {
        get { KlStats s = default; if (_h != 0) StatsN(_h, &s); return (s.Messages, s.Bundles, s.Resyncs, s.CurrencyMessages); }
    }
    public void Dispose() { if (_h != 0) { Close(_h); _h = 0; } }
}
