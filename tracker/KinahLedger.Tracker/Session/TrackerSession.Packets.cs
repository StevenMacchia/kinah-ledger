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
///
/// Since 0.10.68, item pickups and crafts too, when the build's decoder reads the inventory (Steven, 2026-11-13: loot in the
/// Abyss "was not being properly tracked before at all"). An item going up counts as a pickup through the same gate as a
/// currency (a kill, a gather or the screen's own line for it nearby, or an open run; not in a base, with the Market or a
/// reward screen open, nor a bound item no line named, which is a quest's or a box's), while the chat's and the loot list's
/// item pickups are logged and left out. A chat line the traffic didn't count (it never showed it, or only showed a
/// stack's first sight) is counted from the chat after the agreement check, so nothing is lost. While crafting, updates
/// are crafts (<see cref="PacketCrafts"/>), never pickups, and the crafting panel's races are logged and left out.
/// </summary>
public sealed partial class TrackerSession
{
    /// <summary>The watcher's packet reader; null when there is none (a replay, a test).</summary>
    public PacketReader? Packets { get; init; }

    const long PacketPickupMax = 1_000_000;

    /// <summary>Changes waiting for the lines around them (a kill's lines come a second or two after its Kinah).</summary>
    readonly List<CurrencyChange> _packetHeld = new();
    const double PacketHoldSeconds = 3, PacketNearSeconds = 10;

    /// <summary>
    /// A packet amount counted (or a craft or an item): the window redraws now rather than at its next half-second tick
    /// (Steven, 2026-11-13: "if we're using packets these numbers should be instantaneous"). Raised on the reading loop.
    /// </summary>
    public event Action? PacketCounted;
    bool _packetCountedNow;
    /// <summary>When the last pickup the traffic counted came (its message's time).</summary>
    DateTime _packetPickupAt = DateTime.MinValue;
    /// <summary>How long after its message came each of the last packet counts landed (seconds), for the log's minute line and packet-test.</summary>
    internal readonly List<double> PacketDelays = new();
    void NotePacketDelay(DateTime at)
    {
        double d = Math.Max(0, (DateTime.UtcNow - at).TotalSeconds);
        PacketDelays.Add(d); if (PacketDelays.Count > 200) PacketDelays.RemoveAt(0);
        _packetCountedNow = true;
    }
    static string After(DateTime at) => $"{Math.Max(0, (DateTime.UtcNow - at).TotalSeconds):0.00} s after it came";

    /// <summary>
    /// The packet reader's changes, counted in order as they come. Each is decided at once when what decides it is in: in a
    /// base, with the Market open or over a million at once it is a sale, the mail or a reward (logged, not counted); with a
    /// kill or a gather in the last <see cref="PacketNearSeconds"/>, the screen's own reading of it, or a run open it is a
    /// pickup and counts now. Only a rise with none of those waits, up to <see cref="PacketHoldSeconds"/>, for a kill's
    /// lines to come in (they lag the game's message by a second or two); then it counts, or is left out as before. Until
    /// 0.10.70 every change waited the full <see cref="PacketHoldSeconds"/>, so the window showed a pickup 3 to 4 s late.
    /// </summary>
    void DrainPackets()
    {
        if (Packets is not { } p) return;
        _packetCountedNow = false;
        while (p.Next() is { } next) _packetHeld.Add(next);
        var now = DateTime.UtcNow;
        // the player's kills and deaths first: a kill is the "kill nearby" its own Kinah and drops are checked against
        DrainFights(p, now);
        DrainItems(p, now);
        while (_packetHeld.Count > 0)
        {
            var c = _packetHeld[0];
            if (c.First || c.Delta <= 0) { _packetHeld.RemoveAt(0); continue; }   // a baseline, or spending: nothing picked up
            string what = c.What switch { CurrencyKind.Kinah => "Kinah", CurrencyKind.BoundKinah => "bound Kinah", _ => "Abyss Points" };
            bool nearKill = (_lastKillAt - c.At).TotalSeconds >= -PacketNearSeconds || (_lastGatherAt - c.At).TotalSeconds >= -PacketNearSeconds
                // a pickup the traffic counted in the last few seconds: still farming (in the Abyss the kill's lines are few and
                // late, so on Steven's 2026-11-13 log this decided 38 of the 51 pickups that would otherwise have waited)
                || (c.At - _packetPickupAt).TotalSeconds is >= 0 and <= PacketNearSeconds;
            bool screenSaw = p.ScreenNear(c.What, c.At, PacketNearSeconds);
            bool waited = (now - c.At).TotalSeconds >= PacketHoldSeconds;
            string? why = InBase ? $"in a base ({Zone})"
                : MarketOpen?.Invoke(c.At) == true ? "with the Market open"
                : c.Delta > PacketPickupMax ? "at once"
                : nearKill || screenSaw || Instances.Current != null ? null
                : waited ? "with no kill, gather or pickup line around it"
                : "";
            if (why == "") break;   // its lines may still come: it waits, and the ones after it with it, so they count in order
            _packetHeld.RemoveAt(0);
            if (why != null)
            {
                CrashLog.Note($"packets: {what} +{c.Delta:N0} {why}: a sale, the mail or a reward, not a pickup; not counted");
                continue;
            }
            GameEvent ev = c.What == CurrencyKind.AbyssPoints
                ? new CurrencyGained(c.At, "Abyss Points", c.Delta) { Via = "packets" }
                : new KinahChanged(c.At, c.Delta, c.What == CurrencyKind.BoundKinah) { Via = "packets" };
            CrashLog.Note($"packets: {what} +{c.Delta:N0} (now {c.Amount:N0}): counted from the game's message, {After(c.At)}");
            CommitWith(ev, "counter");
            NotePacketDelay(c.At);
            if (c.At > _packetPickupAt) _packetPickupAt = c.At;
        }
        if (_packetCountedNow) try { PacketCounted?.Invoke(); } catch (Exception ex) { CrashLog.Write("packet counted", ex); }
    }

    /// <summary>
    /// The reading loop's wait between screen passes, cut short whenever the packet reader queues something: what came is
    /// drained at once, and the wait goes on to its end (TrackerSession.Loop).
    /// </summary>
    async Task WaitWithPacketsAsync(int ms, CancellationToken ct)
    {
        if (Packets is not { } p) { try { await Task.Delay(ms, ct); } catch (TaskCanceledException) { } return; }
        var until = DateTime.UtcNow.AddMilliseconds(ms);
        while (!ct.IsCancellationRequested)
        {
            // what is held for its lines is looked at again every quarter second, so it counts as soon as they are in
            int left = (int)(until - DateTime.UtcNow).TotalMilliseconds;
            if (left <= 0) break;
            await p.WaitArrivedAsync(_packetHeld.Count > 0 || _itemHeld.Count > 0 ? Math.Min(left, 250) : left, ct);
            DrainPackets();
        }
    }

    /// <summary>Between the screen pass's readers: the packets drained when there is something to drain, so a pass of half a second doesn't hold a count.</summary>
    void PacketsDue() { if (Packets is { } p && (p.Pending || _packetHeld.Count > 0 || _itemHeld.Count > 0)) DrainPackets(); }

    /// <summary>
    /// True when <paramref name="ev"/> is Kinah, bound Kinah or Abyss Points from the screen (a chat line, the Kinah counter,
    /// the loot list) while the packets count that currency: it is logged, offered to the reader's agreement check, and not
    /// counted. A reward table's coin is the run's own reading and stays.
    /// </summary>
    bool PacketsHave(GameEvent ev)
    {
        if (ev is RewardReceived rr) { _rewardLines.Add((ev.Time, Unbound(rr.Name))); if (_rewardLines.Count > 40) _rewardLines.RemoveAt(0); }
        if (Packets is not { } p || ev.Via == "packets" || ev.Via.Contains("reward table")) return false;
        // an item pickup from the screen while the packets count items: the agreement check decides whether the chat's line counts after all
        if (ev is ItemAcquired { Count: > 0 } ia && p.CountingItems(ev.Time) && !_screenLate.Contains(ev))
        {
            bool chat = !ev.Via.Contains("loot list");
            p.ScreenSawItem(ia.ItemId, ia.Count, ev.Time, chat, chat ? ev : null);
            CrashLog.Note($"{Describe(ev)} ({(ev.Via.Length > 0 ? ev.Via : "chat")}): counted from the game's messages instead{(chat ? " (the chat's, if they didn't show it)" : "")}");
            return true;
        }
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

    // ---- items and crafts (0.10.68) ----
    readonly List<ItemBatch> _itemHeld = new();
    PacketCrafts? _packetCrafts;
    /// <summary>Screen pickups the traffic didn't count, handed back by the agreement check: Commit counts them.</summary>
    readonly HashSet<GameEvent> _screenLate = new(ReferenceEqualityComparer.Instance);
    readonly List<(DateTime At, string Name)> _rewardLines = new();
    const double CraftEndSeconds = 20;

    /// <summary>Crafting under way at <paramref name="at"/>: the crafting panel up lately, or the crafting window open.</summary>
    bool CraftingAt(DateTime at) => CraftView.CraftPanelActive(at) || TargetReader.CraftOpen(Target.CraftAt, at);

    /// <summary>The recipe the crafting panel names now, "" when it wasn't read.</summary>
    string PanelRecipeNow => _queueName.Length >= 2 && (_queueSure || _queueGuessed) ? _queueName : _panelRecipe;

    void DrainItems(PacketReader p, DateTime now)
    {
        p.NameOf ??= id => _items.NameOf(id);
        p.CraftingAt ??= CraftingAt;
        while (p.NextItems() is { } b) _itemHeld.Add(b);
        // an update is taken once the chat had its say on each item that went up (a pickup line, or a box's or a reward's
        // contents), else once PacketHoldSeconds old as before: the chat tells a drop from a box's contents, and lags the
        // game's update by a second or two, so an item counts as fast as its line, not a fixed 3 s later
        while (_itemHeld.Count > 0 && ((now - _itemHeld[0].At).TotalSeconds >= PacketHoldSeconds || ChatHadItsSay(p, _itemHeld[0])))
        {
            var b = _itemHeld[0]; _itemHeld.RemoveAt(0);
            TakeItems(p, b);
        }
        // the crafting is over: its tries with nothing made are fails
        if (_packetCrafts is { Open: true } pc && (now - pc.LastAt).TotalSeconds > CraftEndSeconds && !CraftingAt(now.AddSeconds(-PacketHoldSeconds)))
            foreach (var o in pc.End(pc.LastAt)) CountPacketCraft(o);
        // the screen's pickups the traffic didn't count: the screen's after all
        while (p.NextUncovered() is GameEvent late)
        {
            _screenLate.Add(late);
            CrashLog.Note($"{Describe(late)} ({(late.Via.Length > 0 ? late.Via : "chat")}): the game's messages didn't count it, so the chat does");
            Commit(late);
            _screenLate.Remove(late);
        }
    }

    /// <summary>
    /// Whether every item that went up in <paramref name="b"/> has had the chat's say: a chat pickup line for it, or the chat
    /// calling it a reward or a box's contents, near the update. An update with nothing going up, or something going down, or
    /// while crafting, has nothing to wait for.
    /// </summary>
    bool ChatHadItsSay(PacketReader p, ItemBatch b)
    {
        if (b.Fell || CraftingAt(b.At)) return true;
        foreach (var (id, n, unknown) in b.Net())
        {
            if (unknown || n <= 0 || _items.Get(id) == null) continue;
            string un = Unbound(_items.NameOf(id));
            if (p.ChatNearItem(id, b.At, PacketNearSeconds)) continue;
            if (_rewardLines.Any(r => Math.Abs((r.At - b.At).TotalSeconds) <= PacketNearSeconds && string.Equals(r.Name, un, StringComparison.OrdinalIgnoreCase))) continue;
            return false;
        }
        return true;
    }

    /// <summary>One inventory update, once the lines around it are in: a craft's while crafting, else each item that went up a pickup if the gate lets it.</summary>
    void TakeItems(PacketReader p, ItemBatch b)
    {
        string Said() => string.Join(", ", b.Net().Select(n => $"{_items.NameOf(n.ItemId)} {(n.Unknown ? "(first seen)" : n.Net >= 0 ? $"+{n.Net:N0}" : $"−{-n.Net:N0}")}"));
        if (CraftingAt(b.At))
        {
            p.CraftSeen(b.At);
            if (!p.CountingCrafts(b.At)) { CrashLog.Note($"packets: {Said()} while crafting: the crafting panel counts the crafts"); return; }
            _packetCrafts ??= new PacketCrafts(id => _items.NameOf(id), id => _items.Get(id) != null) { GapBetween = p.GapBetween };
            foreach (var o in _packetCrafts.Take(b, PanelRecipeNow)) CountPacketCraft(o);
            return;
        }
        if (!p.CountingItems(b.At)) return;
        // something went down with it: a craft's materials, a box opened, a stack moved or something used, never a pickup
        if (b.Fell) { CrashLog.Note($"packets: {Said()} in one update with something going down: not a pickup"); return; }
        foreach (var (id, n, unknown) in b.Net())
        {
            string name = _items.NameOf(id);
            if (unknown) { CrashLog.Note($"packets: {name} first seen (its earlier amount not known): its pickup is the chat's"); continue; }
            if (n <= 0) continue;
            if (_items.Get(id) == null) { CrashLog.Note($"packets: {name} +{n:N0} is not on the item list: the chat's"); continue; }
            bool bound = name.EndsWith("(Bound)", StringComparison.OrdinalIgnoreCase);
            bool screenSaw = p.ScreenNearItem(id, b.At, PacketNearSeconds);
            bool near = (_lastKillAt - b.At).TotalSeconds >= -PacketNearSeconds || (_lastGatherAt - b.At).TotalSeconds >= -PacketNearSeconds
                || (_gatherRaces.Count > 0 && (_gatherRaces[^1].At - b.At).TotalSeconds >= -PacketNearSeconds);
            string un = Unbound(name);
            string? why = InBase && !name.Contains("Odyle", StringComparison.OrdinalIgnoreCase) ? $"in a base ({Zone})"
                : MarketOpen?.Invoke(b.At) == true ? "with the Market open"
                : Target.RewardOpen(b.At) ? "with a reward screen open (the reward table reads it)"
                : _rewardLines.Any(r => Math.Abs((r.At - b.At).TotalSeconds) <= PacketNearSeconds && string.Equals(r.Name, un, StringComparison.OrdinalIgnoreCase)) ? "with the chat calling it a reward"
                : bound && !screenSaw && Instances.Current == null ? "bound, with no pickup line for it (a quest's or a box's reward)"
                : !near && !screenSaw && Instances.Current == null ? "with no kill, gather or pickup line around it" : null;
            if (why != null) { CrashLog.Note($"packets: {name} +{n:N0} {why}: not a pickup; not counted"); continue; }
            var ev = new ItemAcquired(b.At, id, (int)Math.Min(n, 99999), bound) { Via = "packets" };
            CrashLog.Note($"packets: {name} +{n:N0}: counted from the game's message, {After(b.At)}");
            CommitWith(ev, "counter");
            NotePacketDelay(b.At);
            if (b.At > _packetPickupAt) _packetPickupAt = b.At;
        }
    }

    /// <summary>A craft the traffic told, counted as the panel's race would be (its queue's tallies, the Crafting page, the feed).</summary>
    void CountPacketCraft(PacketCrafts.Outcome o)
    {
        string recipe = o.Recipe.Length >= 2 ? o.Recipe : PanelRecipeNow;
        if (o.Lost) { CrashLog.Note($"packets: a try of {(recipe.Length > 0 ? recipe : "an unread recipe")}: {o.Why}; not counted"); return; }
        CrashLog.Note($"packets: craft {(o.Combo ? "combo won" : o.Ok ? "worked" : "failed")} for {(recipe.Length > 0 ? recipe : "an unread recipe")}: {o.Why}");
        PanelRace(recipe, o.Ok, o.Combo, o.At, _panelLevel, _queueOpen ? _queueId : 0, makes: o.Makes, why: "the game's messages: " + o.Why, fromPackets: true);
        _packetCountedNow = true;
    }

    /// <summary>True when the packets count crafts now: a race of the crafting panel is logged, kept for the agreement check and left out.</summary>
    bool PacketsHaveCraft(DateTime at, string what)
    {
        if (Packets is not { } p || !p.CountingCrafts(at)) return false;
        p.ScreenSawCraft(at);
        CrashLog.Note($"crafting panel: {what}: counted from the game's messages instead");
        return true;
    }

    // ---- kills and deaths (0.10.72) ----
    readonly FightTally _fightTally = new();
    /// <summary>Rank Points the player gained since the tracker started, from the game's messages.</summary>
    public long PacketRankPoints => _fightTally.RankPoints;

    /// <summary>
    /// The player's kills and deaths from the game's messages (Steven, 2026-11-13: "count abyss pvp kills vs. mob kills"):
    /// each one counted as <see cref="FightCounted"/>, a player or a monster, so the Abyss can say "3 players · 48 monsters".
    /// The chat's kill count stays: the kills shown are the larger of the two (SessionEngine.Kills).
    /// </summary>
    void DrainFights(PacketReader p, DateTime now)
    {
        while (p.NextFight() is { } f)
        {
            _fightTally.Add(f);
            // the player's own kill is its Kinah's and drops' evidence as it comes (0.10.73), not once the tally decides it
            // 3 s later: the first pickup of a fight counts at once too
            if (f.Kind == FightKind.Kill && f.Flags.HasFlag(FightFlags.Mine) && !f.Flags.HasFlag(FightFlags.VictimMe) && f.At > _lastKillAt) _lastKillAt = f.At;
        }
        foreach (var o in _fightTally.Take(now))
        {
            if (!o.Death && (o.At - _lastKillAt).TotalSeconds > 0) _lastKillAt = o.At;
            string what = o.Death ? $"died{(o.Name.Length > 0 ? " to " + o.Name : "")}"
                : $"killed {(o.Player ? "the player " + (o.Name.Length > 0 ? o.Name : "(unnamed)") : "a monster")}{(o.Credited ? " (the game named no killer; your Rank Points rose with it)" : "")}";
            CrashLog.Note($"packets: {what}");
            CommitWith(new FightCounted(o.At, o.Player, o.Death, o.Name) { Via = "packets" }, "counter");
        }
    }

    /// <summary>For packet-test: a pickup through Commit, as a chat line or the counter would come, and a pass's drain.</summary>
    internal void CommitForTest(GameEvent ev) => Commit(ev);
    internal void DrainPacketsForTest() => DrainPackets();
    /// <summary>For packet-test: the reading loop's wait between passes, run for <paramref name="ms"/>.</summary>
    internal void WaitForTest(int ms) => WaitWithPacketsAsync(ms, CancellationToken.None).GetAwaiter().GetResult();
}