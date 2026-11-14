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

/// <summary>Which boss content a fight is in. The boss parser runs only in these.</summary>
public enum ContentKind { None = 0, Transcendence = 1, Expedition = 2, Sanctuary = 3 }

/// <summary>What a boss parser event is.</summary>
public enum FightKind { ZoneEnter = 1, ZoneLeave = 2, BossSeen = 3, EncounterStart = 4, EncounterEnd = 5, Member = 6, MemberLeft = 7, Damage = 8, BossHp = 9 }

/// <summary>How an encounter ended.</summary>
public enum FightOutcome { None = 0, Killed = 1, Wipe = 2, Left = 3 }

/// <summary>
/// One boss parser event (klnet.dll's interface version 3). Events come only inside a Transcendence, Expedition or Sanctuary
/// instance, between its ZoneEnter and ZoneLeave. Every EncounterStart gets exactly one EncounterEnd (Left when the zone or
/// the connection ends first), and an encounter's events carry its Encounter serial (a new one per boss pull).
/// <list type="bullet">
/// <item>BossSeen, BossHp: Amount is the boss's HP now, Max its max HP. EncounterStart: Max is the max HP.</item>
/// <item>Damage: one share of an HP fall of the boss, to Entity (Name when known; Self when it is the player). Amount is this
/// share, Max that attacker's total in the encounter. Entity 0 is "other": a fall no hit came with (damage over time, or
/// lost traffic). An encounter's Damage adds up to the HP it lost.</item>
/// <item>EncounterEnd: Outcome, and Amount the HP the boss lost in it.</item>
/// <item>Member, MemberLeft: a group member (Entity, Name, Class, CombatPower, ItemLevel); a later Member for the same Entity
/// replaces the earlier one. Not sent yet (the group isn't read yet): a fight shows without it.</item>
/// </list>
/// </summary>
public sealed record FightEvent(
    DateTime At, FightKind Kind,
    ContentKind Content, string Zone,
    int Encounter,
    string Boss, long BossEntity,
    FightOutcome Outcome,
    long Entity, string Name, string Class, bool Self,
    long CombatPower, long ItemLevel,
    long Amount, long Max);

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
    /// <summary>True when this decoder reads boss fights too (klnet.dll's interface version 3).</summary>
    bool ReadsFights => false;
    /// <summary>The next boss parser event, or null (always null for a decoder that reads no fights).</summary>
    FightEvent? NextFight() => null;
    (long Messages, long Bundles, int Resyncs, int CurrencyMessages) Stats { get; }
}

/// <summary>The release build's decoder, klnet.dll, through its C interface.</summary>
public sealed unsafe class NativeDecoder : IGameDecoder
{
    [StructLayout(LayoutKind.Sequential)] struct KlChange { public int Kind; public int First; public long Delta; public long Amount; public long AtUtcTicks; }
    [StructLayout(LayoutKind.Sequential)] struct KlItem { public long AtUtcTicks; public uint ItemId; public int Op; public long StackId; public long Delta; public long Total; public int First; public int Batch; public int BatchRows; public int BatchFell; }
    [StructLayout(LayoutKind.Sequential)]
    struct KlFight
    {
        public long AtUtcTicks; public int Kind, Content, Encounter, Outcome, Self, Reserved;
        public long BossEntity, Entity, CombatPower, ItemLevel, Amount, Max;
        public fixed byte Zone[64]; public fixed byte Boss[64]; public fixed byte Name[32]; public fixed byte Class[16];
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
    [DllImport("klnet", EntryPoint = "kl_next_fight")] static extern int NextFightN(nint h, KlFight* fight);

    static int? _abi;
    /// <summary>klnet.dll's interface version: 0 when this build has none, 1 for currencies only, 2 with item changes, 3 with boss fights.</summary>
    public static int Version
    {
        get
        {
            if (_abi is int v) return v;
            try { int a = Abi(); _abi = a is >= 1 and <= 3 ? a : 0; }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException) { _abi = 0; }
            return _abi.Value;
        }
    }
    /// <summary>True when this build carries the decoder (klnet.dll, interface version 1 to 3).</summary>
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
    public bool ReadsFights => Version >= 3;
    public FightEvent? NextFight()
    {
        if (_h == 0 || Version < 3) return null;
        KlFight f;
        if (NextFightN(_h, &f) != 1 || f.Kind is < 1 or > 9) return null;
        return new FightEvent(new DateTime(f.AtUtcTicks, DateTimeKind.Utc), (FightKind)f.Kind,
            f.Content is >= 0 and <= 3 ? (ContentKind)f.Content : ContentKind.None, Text(f.Zone, 64), f.Encounter, Text(f.Boss, 64), f.BossEntity,
            f.Outcome is >= 0 and <= 3 ? (FightOutcome)f.Outcome : FightOutcome.None, f.Entity, Text(f.Name, 32), Text(f.Class, 16), f.Self != 0,
            f.CombatPower, f.ItemLevel, f.Amount, f.Max);
    }
    /// <summary>A NUL-padded UTF-8 field.</summary>
    internal static string Text(byte* p, int size)
    {
        int n = 0; while (n < size && p[n] != 0) n++;
        return n == 0 ? "" : System.Text.Encoding.UTF8.GetString(p, n);
    }
    /// <summary>The interface's event record is 256 bytes (a check for the tests).</summary>
    internal static int FightSize => sizeof(KlFight);
    public (long Messages, long Bundles, int Resyncs, int CurrencyMessages) Stats
    {
        get { KlStats s = default; if (_h != 0) StatsN(_h, &s); return (s.Messages, s.Bundles, s.Resyncs, s.CurrencyMessages); }
    }
    public void Dispose() { if (_h != 0) { Close(_h); _h = 0; } }
}
