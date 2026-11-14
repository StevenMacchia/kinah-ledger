using System.Runtime.InteropServices;

namespace KinahLedger.Packets;

/// <summary>The currencies counted from the game's traffic.</summary>
/// <summary>The currencies counted from the game's traffic. Rank Points (0.10.76) come with the player's fights (interface version 3), not from <see cref="IGameDecoder.Next"/>.</summary>
public enum CurrencyKind { Kinah = 0, BoundKinah = 1, AbyssPoints = 2, RankPoints = 3 }

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

/// <summary>How a gathering attempt ended: it worked, its upgrade roll won (the better grade of the material), or it failed.</summary>
public enum GatherEnd { Worked = 0, Upgrade = 1, Failed = 2 }

/// <summary>
/// One gathering attempt of the player's, from the game's messages: the node (its id on this connection; a new node has a
/// new id), its kind when its appearance was seen (0 when not), how the attempt ended, what it gave and how many (0 on a
/// fail; the item is then what the node gives), and the attempt's number on that node (1 to 3, 0 when not seen).
/// </summary>
public sealed record GatherResult(DateTime At, long NodeId, long NodeKind, GatherEnd Outcome, long ItemId, int Count, int Attempt);

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
/// A timed event as the game's event server tells it (the decoder's interface version 6), or a sample of the game server's
/// clock. Clock: only ServerNow says anything (the server's clock at At; it comes on any server, a minute apart). Phase: a
/// part of the event opened (or was open when the player arrived), with PhaseStart its start on the server's clock when
/// known. Over: the event is over (its rewards come next). Closed: the part closed, the player left it; ServerMoved when the
/// tracker saw the game leave the event's server without being told. Family and Name: the event as the tracker's list names it
/// ("Shugo Festival", "Defend Shugo Merchants"), "" when not known. Code: the game's own name for the part, for the log when
/// the event isn't known yet.
/// </summary>
public sealed record AreaEvent(DateTime At, DateTime? ServerNow, AreaEvent.Kind What, AreaEvent.Stage Phase, DateTime? PhaseStart, string Family, string Name, string Code, uint Id, bool ServerMoved = false)
{
    public enum Kind { Clock = 0, Phase = 1, Over = 2, Closed = 3 }
    /// <summary>Where the event stands: getting ready (the lobby's countdown), under way, over.</summary>
    public enum Stage { Unknown = 0, Preparing = 1, UnderWay = 2, Over = 3 }
    /// <summary>The event's name to show: the game, else its family, else "".</summary>
    public string Label => Name.Length > 0 ? Name : Family;
}

/// <summary>
/// One record of the inventory's snapshot (the game sends the whole inventory at the start of each connection to its
/// server): Begin (Count: the stacks the game says it holds), Stack (an item's stack: its id on this connection, the amount,
/// the container; Currency when it is Kinah, bound Kinah or Abyss Points; Level a piece of gear's enhancement level, 0 for
/// anything else), End (Count: the stacks read).
/// </summary>
public sealed record BagRecord(DateTime At, int Kind, int Count, long ItemId, long StackId, long Amount, int Container, int Slot, CurrencyKind? Currency, int Level = 0)
{
    public const int Begin = 0, Stack = 1, End = 2;
}

/// <summary>
/// The decoder's boss parser row (klnet.dll version 4) before it becomes a <see cref="BossEvent"/>. Kind: 1 instance entered,
/// 2 left, 3 boss seen, 4 pull started, 5 pull ended, 6 group member, 7 member left, 8 damage, 9 boss HP, 10 a death, 11 the instance's mode.
/// </summary>
internal sealed record BossRow(DateTime At, int Kind, int Content, string Zone, int Pull, string Boss, long BossEntity, int Outcome,
    long Entity, string Name, string Class, bool Self, long CombatPower, long ItemLevel, long Amount, long Max, int Skill = 0);

/// <summary>Turns the decoder's boss rows into the Parser's events: keeps the roster (GroupChanged is the whole list) and the instance serial.</summary>
internal sealed class BossMapper
{
    readonly Dictionary<long, GroupMember> _group = new();
    long _instance;

    public void Reset() => _group.Clear();

    public BossEvent? Map(BossRow r)
    {
        switch (r.Kind)
        {
            case 1:
                _group.Clear();
                return new InstanceEntered(r.At, r.Content is >= 1 and <= 3 ? (ContentKind)r.Content : ContentKind.Other, r.Zone, ++_instance);
            case 2: _group.Clear(); return new InstanceLeft(r.At);
            case 4: return new BossEngaged(r.At, r.Boss, r.BossEntity);
            case 5: return new BossEnded(r.At, r.BossEntity, r.Outcome switch { 1 => BossOutcome.Kill, 2 => BossOutcome.Wipe, _ => BossOutcome.Left });
            case 6:
                _group[r.Entity] = new GroupMember(r.Entity, r.Name, r.Class, (int)Math.Clamp(r.CombatPower, 0, int.MaxValue), (int)Math.Clamp(r.ItemLevel, 0, int.MaxValue), r.Self, r.Boss);   // a member row's boss field is its server
                return new GroupChanged(r.At, _group.Values.ToList());
            case 7: return _group.Remove(r.Entity) ? new GroupChanged(r.At, _group.Values.ToList()) : null;
            case 11: return r.Boss.Length > 0 ? new InstanceMode(r.At, r.Boss) : null;   // the mode rides in the boss field
            case 8: return new DamageDealt(r.At, r.Entity, r.BossEntity, r.Amount, r.Name, r.Self, r.Skill, (int)Math.Clamp(r.ItemLevel, 0, int.MaxValue));   // ItemLevel carries the hits for a damage row   // source 0: HP lost with no hit (damage over time, lost traffic)
            case 9: return new BossHealth(r.At, r.BossEntity, r.Amount, r.Max);
            case 10: return new UnitDied(r.At, r.Entity, r.Boss.Length > 0 ? r.Boss : r.Name, r.Amount == 1);
            default: return null;   // a boss seen (its HP comes with the pull), or a kind this build doesn't know
        }
    }
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
    /// <summary>The next timed event's part or clock sample, or null (always null for a decoder without them).</summary>
    AreaEvent? NextAreaEvent() => null;
    /// <summary>True when this decoder reads the player's kills and deaths too (klnet.dll's interface version 3).</summary>
    bool ReadsFights => false;
    /// <summary>The next fight event found, or null (always null for a decoder that reads no fights).</summary>
    FightEvent? NextFight() => null;
    /// <summary>True when this decoder reads boss fights too (klnet.dll's interface version 4).</summary>
    bool ReadsBosses => false;
    /// <summary>The next boss parser event, or null (always null for a decoder that reads no boss fights).</summary>
    BossEvent? NextBossEvent() => null;
    /// <summary>True when this decoder reads the inventory's snapshot too (klnet.dll's interface version 8).</summary>
    bool ReadsBag => false;
    /// <summary>The next record of an inventory snapshot, or null (always null for a decoder that reads none).</summary>
    BagRecord? NextBag() => null;
    /// <summary>True when this decoder reads the player's gathering too (klnet.dll's interface version 7).</summary>
    bool ReadsGathers => false;
    /// <summary>The next gathering attempt found, or null (always null for a decoder that reads none).</summary>
    GatherResult? NextGather() => null;
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
        public long AtUtcTicks; public int Kind, Content, Encounter, Outcome, Self, Skill;
        public long BossEntity, Entity, CombatPower, ItemLevel, Amount, Max;
        public fixed byte Zone[64]; public fixed byte Boss[64]; public fixed byte Name[32]; public fixed byte Class[16];
    }
    [StructLayout(LayoutKind.Sequential)] struct KlBoss
    {
        public long AtUtcTicks; public long ServerUtcMs; public long TimeUtcMs; public uint MapId; public uint BossId; public int State; public int Flags;
        public float X, Y, Z; public int Reserved;
        public fixed byte Name[48]; public fixed byte Place[48]; public fixed byte Region[24];
    }
    [StructLayout(LayoutKind.Sequential)] struct KlAreaEvent
    {
        public long AtUtcTicks; public long ServerUtcMs; public long StartUtcMs; public uint Id; public int Kind; public int Stage; public int State; public int Flags; public int Reserved;
        public fixed byte Tag[48]; public fixed byte Family[32]; public fixed byte Name[48];
    }
    [StructLayout(LayoutKind.Sequential)] struct KlEvent { public long AtUtcTicks; public int Kind; public int Flags; public uint OwnId; public uint VictimId; public uint KillerId; public uint MonsterId; public long Delta; public long Total; public fixed byte VictimName[32]; public fixed byte KillerName[32]; }
    [StructLayout(LayoutKind.Sequential)] struct KlBagRow { public long AtUtcTicks; public int Kind; public int Count; public uint ItemId; public int Container; public int Slot; public int Gear; public int Currency; public int Reserved; public long StackId; public long Amount; }
    [StructLayout(LayoutKind.Sequential)] struct KlGather { public long AtUtcTicks; public uint NodeId; public uint TemplateId; public uint ItemId; public int Result; public int Count; public int Attempt; public int Flags; public float X, Y, Z; }
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
    [DllImport("klnet", EntryPoint = "kl_next_event")] static extern int NextEventN(nint h, KlEvent* ev);
    [DllImport("klnet", EntryPoint = "kl_next_fight")] static extern int NextBossEventN(nint h, KlFight* fight);
    [DllImport("klnet", EntryPoint = "kl_next_area_event")] static extern int NextAreaN(nint h, KlAreaEvent* ev);
    [DllImport("klnet", EntryPoint = "kl_next_bag")] static extern int NextBagN(nint h, KlBagRow* row);
    [DllImport("klnet", EntryPoint = "kl_next_gather")] static extern int NextGatherN(nint h, KlGather* g);

    static int? _abi;
    /// <summary>klnet.dll's interface version: 0 when this build has none, 1 for currencies only, 2 with item changes, 5 with the field boss list; later versions only add exports.</summary>
    public static int Version
    {
        get
        {
            if (_abi is int v) return v;
            // a later version only adds exports (every one of 1 and 2 stays as it is): it reads what this build knows
            try { int a = Abi(); _abi = a >= 1 && a < 100 ? a : 0; }
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

    public void Reset() { if (_h != 0) ResetN(_h); _bosses.Reset(); }
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
    readonly BossMapper _bosses = new();
    // a dll of version 4 or later without kl_next_fight (one built from a branch that took a later number first): no boss
    // fights, found once, and packet reading goes on
    static bool _noBossExport;
    public bool ReadsBosses => Version >= 4 && !_noBossExport;
    public BossEvent? NextBossEvent()
    {
        if (_h == 0 || !ReadsBosses) return null;
        KlFight f;
        while (true)
        {
            int got;
            try { got = NextBossEventN(_h, &f); }
            catch (EntryPointNotFoundException) { _noBossExport = true; return null; }
            if (got != 1) break;
            var row = new BossRow(new DateTime(f.AtUtcTicks, DateTimeKind.Utc), f.Kind, f.Content, Text(f.Zone, 64), f.Encounter, Text(f.Boss, 64), f.BossEntity,
                f.Outcome, f.Entity, Text(f.Name, 32), Text(f.Class, 16), f.Self != 0, f.CombatPower, f.ItemLevel, f.Amount, f.Max, f.Skill);
            if (_bosses.Map(row) is { } e) return e;
        }
        return null;
    }
    /// <summary>A NUL-padded UTF-8 field.</summary>
    internal static string Text(byte* p, int size)
    {
        int n = 0; while (n < size && p[n] != 0) n++;
        return n == 0 ? "" : System.Text.Encoding.UTF8.GetString(p, n);
    }
    /// <summary>The interface's boss event record is 256 bytes (a check for the tests).</summary>
    internal static int BossRecordSize => sizeof(KlFight);
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
    // as for the bosses: a dll that says 6 or more but was built without the events, asked once
    static bool _noAreas;
    public AreaEvent? NextAreaEvent()
    {
        if (_h == 0 || Version < 6 || _noAreas) return null;
        KlAreaEvent e;
        try { if (NextAreaN(_h, &e) != 1 || e.Kind is < 0 or > 3 || e.Stage is < 0 or > 3) return null; }
        catch (EntryPointNotFoundException) { _noAreas = true; return null; }
        static string Str(byte* p, int n) { int len = 0; while (len < n && p[len] != 0) len++; return System.Text.Encoding.UTF8.GetString(p, len); }
        static DateTime? Ms(long ms) => ms > 0 && ms < 4_102_444_800_000 ? DateTime.UnixEpoch.AddMilliseconds(ms) : null;
        return new AreaEvent(new DateTime(e.AtUtcTicks, DateTimeKind.Utc), Ms(e.ServerUtcMs), (AreaEvent.Kind)e.Kind, (AreaEvent.Stage)e.Stage, Ms(e.StartUtcMs),
            Str(e.Family, 32), Str(e.Name, 48), Str(e.Tag, 48), e.Id);
    }
    public bool ReadsFights => Version >= 3;
    public FightEvent? NextFight()
    {
        if (_h == 0 || Version < 3) return null;
        KlEvent e;
        if (NextEventN(_h, &e) != 1 || e.Kind is < 1 or > 2) return null;
        static string Text(byte* b) { int n = 0; while (n < 32 && b[n] != 0) n++; return System.Text.Encoding.UTF8.GetString(b, n); }
        return new FightEvent(new DateTime(e.AtUtcTicks, DateTimeKind.Utc), (FightKind)e.Kind, (FightFlags)e.Flags, e.VictimId, e.KillerId, e.MonsterId,
            e.Delta, e.Total, Text(e.VictimName), Text(e.KillerName));
    }
    // a dll that says 8 or more but was built without the inventory export (the interface numbers are shared between
    // branches): asked once, then never again, and the inventory stays unknown
    static bool _noBag;
    public bool ReadsBag => Version >= 8 && !_noBag;
    public BagRecord? NextBag()
    {
        if (_h == 0 || !ReadsBag) return null;
        KlBagRow r;
        try { if (NextBagN(_h, &r) != 1 || r.Kind is < 0 or > 2) return null; }
        catch (EntryPointNotFoundException) { _noBag = true; return null; }
        // the gear's enhancement level (confirmed against the game's window on 2026-11-13 for +1 to +12)
        return new BagRecord(new DateTime(r.AtUtcTicks, DateTimeKind.Utc), r.Kind, r.Count, r.ItemId, r.StackId, r.Amount, r.Container, r.Slot, r.Currency is >= 0 and <= 2 ? (CurrencyKind)r.Currency : null, r.Gear is > 0 and < 100 ? r.Gear : 0);
    }
    // a dll that says 7 or more but was built without the gathering export (the interface numbers are shared between
    // branches): asked once, then never again, and gathering stays the screen's
    static bool _noGathers;
    public bool ReadsGathers => Version >= 7 && !_noGathers;
    public GatherResult? NextGather()
    {
        if (_h == 0 || !ReadsGathers) return null;
        KlGather g;
        try { if (NextGatherN(_h, &g) != 1 || g.Result is < 0 or > 2 || g.Count is < 0 or > 1000) return null; }
        catch (EntryPointNotFoundException) { _noGathers = true; return null; }
        return new GatherResult(new DateTime(g.AtUtcTicks, DateTimeKind.Utc), g.NodeId, (g.Flags & 1) != 0 ? g.TemplateId : 0, (GatherEnd)g.Result, g.ItemId, g.Count, (g.Flags & 2) != 0 ? g.Attempt : 0);
    }
    public (long Messages, long Bundles, int Resyncs, int CurrencyMessages) Stats
    {
        get { KlStats s = default; if (_h != 0) StatsN(_h, &s); return (s.Messages, s.Bundles, s.Resyncs, s.CurrencyMessages); }
    }
    public void Dispose() { if (_h != 0) { Close(_h); _h = 0; } }
}
