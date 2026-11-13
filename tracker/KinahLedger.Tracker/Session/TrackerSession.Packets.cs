using KinahLedger.Ocr;
using KinahLedger.Packets;

namespace KinahLedger.Session;

/// <summary>
/// Kinah, bound Kinah and Abyss Points counted from the game's own messages (Steven, 2026-11-13: "Yes, packets count"),
/// while the packet reader (Packets/PacketReader.cs) has them. The amounts are the game's: a change of the balance it sends
/// with every pickup, so a chat line read wrong ("5,193" for 519), read garbled or never read at all can't miscount.
///
/// A packet change goes through Commit like a chat line, so it lands where a chat line would have: a run, farming, the
/// feed, the enemy it was credited to. While packets count a currency, that currency's chat lines, the Kinah counter's
/// rises and the loot list's rows are logged and left out, so nothing counts twice. The screen counts again when:
/// <list type="bullet">
/// <item>packet reading is off or stopped (no Npcap, a stop, the game closed);</item>
/// <item>a currency has no baseline yet (the first message after a connection only says the amount, not the change: that
///   pickup is the chat's);</item>
/// <item>the packets stopped agreeing (<see cref="PacketReader.Broken"/>): the chat said three pickups of a currency in five
///   minutes that the game's messages never showed, as after a patch that changed the message.</item>
/// </list>
/// A rise in a base, with the Market open, or of over a million at once is not a pickup (a sale, the mail, a quest) and
/// counts nothing, as the Kinah counter's own rises never did there.
/// </summary>
public sealed partial class TrackerSession
{
    /// <summary>The watcher's packet reader; null when there is none (a replay, a test).</summary>
    public PacketReader? Packets { get; init; }

    const long PacketPickupMax = 1_000_000;

    /// <summary>Changes held until the lines around them are in (a kill's lines come a second or two after its Kinah).</summary>
    readonly List<CurrencyChange> _packetHeld = new();
    const double PacketHoldSeconds = 3, PacketNearSeconds = 10;

    /// <summary>
    /// The packet reader's changes, counted in order once <see cref="PacketHoldSeconds"/> old: a rise is a pickup when a kill
    /// or a gather was seen within <see cref="PacketNearSeconds"/>, the screen read a pickup of it then, or a run is open.
    /// Otherwise it is a sale, the mail or a quest's pay (an NPC in town, the Market): logged, not counted.
    /// </summary>
    void DrainPackets()
    {
        if (Packets is not { } p) return;
        while (p.Next() is { } next) _packetHeld.Add(next);
        var now = DateTime.UtcNow;
        while (_packetHeld.Count > 0 && (now - _packetHeld[0].At).TotalSeconds >= PacketHoldSeconds)
        {
            var c = _packetHeld[0]; _packetHeld.RemoveAt(0);
            if (c.First || c.Delta <= 0) continue;   // a baseline, or spending: nothing picked up
            string what = c.What switch { CurrencyKind.Kinah => "Kinah", CurrencyKind.BoundKinah => "bound Kinah", _ => "Abyss Points" };
            bool nearKill = (_lastKillAt - c.At).TotalSeconds >= -PacketNearSeconds || (_lastGatherAt - c.At).TotalSeconds >= -PacketNearSeconds;
            bool screenSaw = p.ScreenNear(c.What, c.At, PacketNearSeconds);
            string? why = InBase ? $"in a base ({Zone})"
                : MarketOpen?.Invoke(c.At) == true ? "with the Market open"
                : c.Delta > PacketPickupMax ? "at once"
                : !nearKill && !screenSaw && Instances.Current == null ? "with no kill, gather or pickup line around it" : null;
            if (why != null)
            {
                CrashLog.Note($"packets: {what} +{c.Delta:N0} {why}: a sale, the mail or a reward, not a pickup; not counted");
                continue;
            }
            GameEvent ev = c.What == CurrencyKind.AbyssPoints
                ? new CurrencyGained(c.At, "Abyss Points", c.Delta) { Via = "packets" }
                : new KinahChanged(c.At, c.Delta, c.What == CurrencyKind.BoundKinah) { Via = "packets" };
            CrashLog.Note($"packets: {what} +{c.Delta:N0} (now {c.Amount:N0}): counted from the game's message");
            CommitWith(ev, "counter");
        }
    }

    /// <summary>
    /// True when <paramref name="ev"/> is Kinah, bound Kinah or Abyss Points from the screen (a chat line, the Kinah counter,
    /// the loot list) while the packets count that currency: it is logged, offered to the reader's agreement check, and not
    /// counted. A reward table's coin is the run's own reading and stays.
    /// </summary>
    bool PacketsHave(GameEvent ev)
    {
        if (Packets is not { } p || ev.Via == "packets" || ev.Via.Contains("reward table")) return false;
        CurrencyKind? kind = ev switch
        {
            KinahChanged { Delta: > 0 } k => k.Bound ? CurrencyKind.BoundKinah : CurrencyKind.Kinah,
            CurrencyGained { Amount: > 0 } cg when SessionEngine.IsAbyssPoints(cg.Name) => CurrencyKind.AbyssPoints,
            _ => null,
        };
        if (kind is not { } k2 || !p.Counting(k2, ev.Time)) return false;
        long amount = ev is KinahChanged kc ? kc.Delta : ((CurrencyGained)ev).Amount;
        p.ScreenSaw(k2, amount, ev.Time);
        CrashLog.Note($"{Describe(ev)} ({(ev.Via.Length > 0 ? ev.Via : "chat")}): counted from the game's messages instead");
        return true;
    }

    /// <summary>For packet-test: a pickup through Commit, as a chat line or the counter would come, and a pass's drain.</summary>
    internal void CommitForTest(GameEvent ev) => Commit(ev);
    internal void DrainPacketsForTest() => DrainPackets();
}