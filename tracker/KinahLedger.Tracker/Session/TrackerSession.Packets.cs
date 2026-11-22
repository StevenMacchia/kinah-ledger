using KinahLedger.Data;
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
/// currency (a kill, a gather, a monster's soul, a pickup the traffic counted or the screen's own line for it nearby, or an
/// open run; not in a base, with the Market or a reward screen open, nor a bound item no line or kill stands beside, or
/// that came as a box opened or an achievement's reward, which is a quest's or a box's), while the chat's and the loot list's
/// item pickups are logged and left out. A chat line the traffic didn't count (it never showed it, or only showed a
/// stack's first sight) is counted from the chat after the agreement check, so nothing is lost. While crafting, updates
/// are crafts (<see cref="PacketCrafts"/>), never pickups, and the crafting panel's races are logged and left out.
/// </summary>
public sealed partial class TrackerSession
{
    /// <summary>The watcher's packet reader; null when there is none (a replay, a test).</summary>
    public PacketReader? Packets
    {
        get => _packets;
        // the reader names items and maps the game's ids as this session's list does, from its first update on
        init { _packets = value; if (value != null) { value.NameOf ??= id => _items.NameOf(id); value.FromGame ??= _items.FromGame; } }
    }
    readonly PacketReader? _packets;

    const long PacketPickupMax = 1_000_000;

    /// <summary>Changes waiting for the lines around them (a kill's lines come a second or two after its Kinah).</summary>
    readonly List<CurrencyChange> _packetHeld = new();
    const double PacketHoldSeconds = 3, PacketNearSeconds = 10;

    /// <summary>When the player's Rank Points last rose (the game's messages), the last 40: each is a kill of theirs.</summary>
    readonly List<DateTime> _rankRises = new();
    /// <summary>How far from a Rank Points rise a currency change is the same kill's pay (one update; the parts can come a few tenths apart).</summary>
    const double KillPaySeconds = 1;
    /// <summary>The most Abyss Points a kill pays with no Rank Points beside it (a kill's are tens to a few hundred).</summary>
    const long KillAbyssPointsMax = 1_000;
    /// <summary>A change that came with a rise of the player's Rank Points: a kill's pay, whether or not its kill was told yet.</summary>
    bool KillPay(DateTime at) => _rankRises.Any(r => Math.Abs((r - at).TotalSeconds) <= KillPaySeconds);

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
        double d = Math.Max(0, (PacketClock.Now - at).TotalSeconds);
        PacketDelays.Add(d); if (PacketDelays.Count > 200) PacketDelays.RemoveAt(0);
        _packetCountedNow = true;
    }
    static string After(DateTime at) => $"{Math.Max(0, (PacketClock.Now - at).TotalSeconds):0.00} s after it came";

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
        while (p.Next() is { } next)
        {
            _packetHeld.Add(next);
            if (next.What == CurrencyKind.RankPoints && !next.First && next.Delta > 0) { _rankRises.Add(next.At); if (_rankRises.Count > 40) _rankRises.RemoveAt(0); }
        }
        var now = PacketClock.Now;
        // the player's kills and deaths first: a kill is the "kill nearby" its own Kinah and drops are checked against
        DrainFights(p, now);
        // the player's gathering next: a gather is the "gather nearby" its item's rise is checked against
        DrainGathers(p, now);
        // the event the player is in, from its own server: an event run starts and ends from it (InstanceLog.GameSaid)
        // (and a dungeon's map event names the instance for the Parser when the game entered it with no name)
        while (p.NextArea() is { } area)
        {
            Instances.GameSaid(area, Zone);
            // its end too: a dungeon's boss room closing and opening again after its last boss is a new run in the same copy
            if (area.What is AreaEvent.Kind.Phase or AreaEvent.Kind.Over or AreaEvent.Kind.Closed && area.Code.Length > 0)
                Fights.MapEvent(area.Code, area.At, closed: area.What == AreaEvent.Kind.Closed, over: area.What == AreaEvent.Kind.Over);
        }
        // the Parser saw the room open again after the last boss: the run log's run closes there and the next opens
        while (_runsAgain.TryDequeue(out var again)) Instances.RunAgain(again.At, again.Instance);
        DrainItems(p, now);
        while (_packetHeld.Count > 0)
        {
            var c = _packetHeld[0];
            // Rank Points going down: a death's cost in the Abyss (the game takes them at the death, 32 each on 2026-11-13)
            if (c.What == CurrencyKind.RankPoints && !c.First && c.Delta < 0)
            {
                _packetHeld.RemoveAt(0);
                CrashLog.Note($"packets: Rank Points {c.Delta:N0} (now {c.Amount:N0}): lost");
                CommitWith(new RankPointsLost(c.At, -c.Delta) { Via = "packets" }, "counter");
                continue;
            }
            if (c.First || c.Delta <= 0) { _packetHeld.RemoveAt(0); continue; }   // a baseline, or spending: nothing picked up
            string what = c.What switch { CurrencyKind.Kinah => "Kinah", CurrencyKind.BoundKinah => "bound Kinah", CurrencyKind.RankPoints => "Rank Points", _ => "Abyss Points" };
            bool nearKill = (_lastKillAt - c.At).TotalSeconds >= -PacketNearSeconds || (_lastGatherAt - c.At).TotalSeconds >= -PacketNearSeconds
                // a pickup the traffic counted in the last few seconds: still farming (in the Abyss the kill's lines are few and
                // late, so on Steven's 2026-11-13 log this decided 38 of the 51 pickups that would otherwise have waited)
                || (c.At - _packetPickupAt).TotalSeconds is >= 0 and <= PacketNearSeconds
                // a kill's pay: Rank Points rise only for a kill, and the game pays a kill's bound Kinah, Rank and Abyss
                // Points in one update. Its kill can be told 4 to 7 s later (2026-11-20 10:15:42: three Abyss kills' pay
                // waited its 3 s, no kill was in yet, and 184 bound Kinah, 105 Rank and 52 Abyss Points went uncounted)
                || KillPay(c.At);
            bool screenSaw = p.ScreenNear(c.What, c.At, PacketNearSeconds);
            bool waited = (now - c.At).TotalSeconds >= PacketHoldSeconds;
            string? why = InBase ? $"in a base ({Zone})"
                : MarketOpen?.Invoke(c.At) == true ? "with the Market open"
                : c.Delta > PacketPickupMax ? "at once"
                // more bound Kinah or Abyss Points than a kill pays, with no Rank Points beside it: a quest's turn-in or a reward's,
                // whatever was killed around it (2026-11-20 11:10 to 11:17, Western Altamia: four quests' 50,000 to 80,000 bound
                // Kinah and 2,500 Abyss Points each, counted as pickups because kills were going on; 13:43:14, 1,260 Abyss Points
                // a second before the "Rewards Collected" screen)
                : Instances.Current == null && !KillPay(c.At) && (c.What == CurrencyKind.BoundKinah && c.Delta > SessionEngine.AbyssKillKinaMax
                    || c.What == CurrencyKind.AbyssPoints && c.Delta > KillAbyssPointsMax) ? "more than a kill pays, with no Rank Points beside it (a quest's or a reward's)"
                : nearKill || screenSaw || Instances.Current != null ? null
                : waited ? "with no kill, gather or pickup line around it"
                : "";
            if (why == "") break;   // its lines may still come: it waits, and the ones after it with it, so they count in order
            _packetHeld.RemoveAt(0);
            // inside an event, the game's own count of its Kinah and Abyss Points: the run's rewards (InstanceLog.GameCurrency)
            if (c.What is CurrencyKind.Kinah or CurrencyKind.BoundKinah or CurrencyKind.AbyssPoints)
                Instances.GameCurrency(c.At, c.Delta, c.What == CurrencyKind.AbyssPoints ? _items.IdOf("Abyss Points") : 0);
            if (why != null)
            {
                CrashLog.Note($"packets: {what} +{c.Delta:N0} {why}: a sale, the mail or a reward, not a pickup; not counted");
                continue;
            }
            GameEvent ev = c.What == CurrencyKind.AbyssPoints
                ? new CurrencyGained(c.At, "Abyss Points", c.Delta) { Via = "packets" }
                : c.What == CurrencyKind.RankPoints ? new CurrencyGained(c.At, "Rank Points", c.Delta) { Via = "packets" }
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

    // ---- the game's data first, the screen its backup (1.0.20) ----
    // A player's 5120x2160 window on 2026-11-18: 560 to 920 ms a pass, about 80 passes a minute back to back, the loot list
    // alone 450 ms of it, while the packets already counted every pickup. While they count items and Kinah, the loot list
    // (whose rows are only logged and left out then) isn't read, and the rest of the pass runs at a slow pace with a cap on
    // its share of a core: the chat stays the agreement check's witness and the reward and box lines' say, the target bar
    // says the enemy, a reward screen and the crafting window, the crafting panel the recipe and the gathering bar. When
    // the packets are off, missing or stop counting, and inside an expedition or event or with a reward screen open (their
    // rewards are the screen's reading), the screen reads everything at the usual pace again, from the next pass.

    /// <summary>While the screen is the backup: at least this long from the start of one pass to the start of the next.</summary>
    public const int BackupPassMs = 2500;
    /// <summary>While the screen is the backup: a pass's share of one core at most (the wait after it is at least (1 − share) / share of its time).</summary>
    public const double BackupCoreShare = 0.25;
    /// <summary>How much later a slow read can date a chat line (a line a busy chat scrolled past waits for reads that show it: UnalignedWatchSeconds).</summary>
    public const double BackupLagSeconds = UnalignedWatchSeconds;
    bool _screenBackup;
    DateTime _backupLagUntil = DateTime.MinValue;

    /// <summary>Why the screen reads everything now; null while the packets count items and Kinah and the screen is their backup.</summary>
    internal string? FullReadingWhy(DateTime now)
    {
        if (Update.RemoteConfig.Current.Off("packets_first")) return "switched off in the remote settings";
        if (Packets is not { } p) return "there is no game reader";
        if (!p.Enabled) return "the game reader is off";
        if (!p.On) return "the game reader isn't running";
        if (p.ItemsBroken is { } ib) return ib;
        if (p.Broken(CurrencyKind.Kinah) is { } kb) return kb;
        if (!p.CountingItems(now) || !p.Counting(CurrencyKind.Kinah, now)) return "the game reader isn't counting items and Kinah yet";
        // an expedition's or event's rewards and a reward screen's are the screen's reading (the reward table, their lines)
        if (Instances.Current != null || ZoneLocator.KindOf(Zone) != null) return "inside an expedition or event";
        if (Target.RewardOpen(now)) return "a reward screen is open";
        return null;
    }

    /// <summary>True while the screen is the packets' backup (<see cref="FullReadingWhy"/>); one log line at each switch.</summary>
    internal bool ScreenBackup(DateTime now)
    {
        var why = FullReadingWhy(now);
        bool backup = why == null;
        // the slow reads date a busy chat's line later: the agreement check allows for it, so it isn't a miss (PacketReader)
        if (backup) _backupLagUntil = now.AddSeconds(BackupLagSeconds + 15);   // and until the lines read meanwhile are judged
        if (Packets is { } p) p.ScreenLagSeconds = backup || now < _backupLagUntil ? BackupLagSeconds : 0;
        if (backup != _screenBackup)
        {
            _screenBackup = backup;
            CrashLog.Note(backup ? "screen reading: backup only (the game reader counts)" : $"screen reading: full ({why})");
        }
        return backup;
    }

    /// <summary>True while the screen is the backup, as the last pass found it.</summary>
    public bool ScreenIsBackup => _screenBackup;

    /// <summary>
    /// The wait after a pass that took <paramref name="spent"/> ms, the usual pace being <paramref name="every"/> ms: as
    /// usual while the screen reads everything; as the backup, never under <see cref="BackupPassMs"/> start to start, never
    /// back to back, and long enough that the pass is at most <see cref="BackupCoreShare"/> of the time.
    /// </summary>
    internal static int PassWait(int spent, int every, bool backup)
    {
        int wait = every - spent;
        if (!backup) return wait;
        int share = (int)Math.Ceiling(Math.Max(0, spent) * (1 - BackupCoreShare) / BackupCoreShare);
        return Math.Max(Math.Max(wait, BackupPassMs - spent), Math.Max(share, 250));
    }

    /// <summary>
    /// True when <paramref name="ev"/> is Kinah, bound Kinah or Abyss Points from the screen (a chat line, the Kinah counter,
    /// the loot list) while the packets count that currency: it is logged, offered to the reader's agreement check, and not
    /// counted. A reward table's coin is the run's own reading and stays.
    /// </summary>
    bool PacketsHave(GameEvent ev)
    {
        if (ev is RewardReceived rr) { _rewardLines.Add((ev.Time, Unbound(rr.Name))); if (_rewardLines.Count > 40) _rewardLines.RemoveAt(0); }
        if (Packets is not { } p || ev.Via == "packets" || ev.Via.Contains("reward table")) return false;
        // a run's reward no list knows, counted by its name: the game's data can't name it, so it is the chat's, and never a
        // pickup the data "didn't show" (2026-11-20 12:23: three such lines stopped the data counting items for three hours)
        if (ev is ItemAcquired { Via: "unlisted" }) return false;
        // an item pickup from the screen while the packets count items: the agreement check decides whether the chat's line counts after all
        if (ev is ItemAcquired { Count: > 0 } ia && p.CountingItems(ev.Time) && !_screenLate.Contains(ev))
        {
            // a first sight the session counted from a gather's report: the gather's chat lines say how many really came. Two
            // procs on one gather can print two lines for the item ("Acquired Orichalcum Ore x2." and "Orichalcum Ore
            // acquired.", Steven 2026-11-15: "if plentiful and bountiful harvest proc at same time it only logs 1 of the
            // resources"), so the lines add up; the chat's repeat guard has already left out a line read twice. A line read
            // after the item went up again is the next pickup's, even before that rise is drained (2026-11-15 12:13:24.2: the
            // next gather's "Acquired Fine Orichalcum Ore x2." was read 0.3 s after its rise, ahead of it)
            int pi = _provisional.FindIndex(x => x.Id == ia.ItemId && (ev.Time - x.At).TotalSeconds is >= -1 and <= ProvisionalSeconds && !p.RoseAfter(x.Id, x.At, ev.Time));
            if (pi >= 0 && !ev.Via.Contains("loot list"))
            {
                var pv = _provisional[pi];
                int lines = pv.Lines + ia.Count;
                _provisional[pi] = pv with { Lines = lines, Counted = Math.Max(pv.Counted, lines) };
                if (lines > pv.Counted)
                {
                    CrashLog.Note($"{Describe(ev)} (chat): its stack's first sight was counted as {pv.Counted} from the gather's report; its lines say {lines}{(pv.Lines > 0 ? $" ({pv.Lines} before this one)" : "")}: {lines - pv.Counted} more");
                    CommitWith(new ItemAcquired(pv.At, pv.Id, lines - pv.Counted, ia.Bound) { Via = "packets" }, "counter");
                }
            }
            // the traffic showed the stack only for the first time and counted nothing: this line is that pickup, now
            else if (pi < 0 && !ev.Via.Contains("loot list") && p.FirstSightNear(ia.ItemId, ev.Time))
            {
                CrashLog.Note($"{Describe(ev)} (chat): the game's message showed its stack only for the first time, so the chat's line counts it now");
                return false;
            }
            bool chat = !ev.Via.Contains("loot list");
            p.ScreenSawItem(ia.ItemId, ia.Count, ev.Time, chat, chat ? ev : null);
            CrashLog.Note($"{Describe(ev)} ({(ev.Via.Length > 0 ? ev.Via : "chat")}): counted from the game's messages instead{(chat ? " (the chat's, if they didn't show it)" : "")}");
            return true;
        }
        CurrencyKind? kind = ev switch
        {
            KinahChanged { Delta: > 0 } k => k.Bound ? CurrencyKind.BoundKinah : CurrencyKind.Kinah,
            CurrencyGained { Amount: > 0 } cg when SessionEngine.IsAbyssPoints(cg.Name) => CurrencyKind.AbyssPoints,
            CurrencyGained { Amount: > 0 } cg when SessionEngine.IsRankPoints(cg.Name) => CurrencyKind.RankPoints,
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
        p.FromGame ??= _items.FromGame;
        p.CraftingAt ??= CraftingAt;
        // what the game's data counted when an event's rewards came: its Kinah, and Abyss Points (the picture's count stays without them)
        Instances.GameCounts ??= at => (p.Counting(CurrencyKind.Kinah, at) && p.Counting(CurrencyKind.BoundKinah, at),
            p.Counting(CurrencyKind.AbyssPoints, at) ? Array.Empty<long>() : new[] { _items.IdOf("Abyss Points") });
        while (p.NextItems() is { } b) { SoulKill(b); _itemHeld.Add(b); }
        // an update is taken once the chat had its say on each item that went up (a pickup line, or a box's or a reward's
        // contents), else once PacketHoldSeconds old as before: the chat tells a drop from a box's contents, and lags the
        // game's update by a second or two, so an item counts as fast as its line, not a fixed 3 s later
        while (_itemHeld.Count > 0 && ((now - _itemHeld[0].At).TotalSeconds >= PacketHoldSeconds || ChatHadItsSay(p, _itemHeld[0]) || DropNow(_itemHeld[0], now)))
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
    /// A monster's soul going up (the loot list's "Soul: Swarm (Bound)" row): the player looted a kill. It is a kill's
    /// evidence for the drops around it as it comes, before the item waits for its lines. In the Abyss the game names the
    /// player as the killer of only some kills (2026-11-16 16:59: a soul every second or two, no kill of the player's named
    /// for minutes), so without this an Earth Dagger dropped among them had "no kill around it".
    /// </summary>
    void SoulKill(ItemBatch b)
    {
        if (b.At > _lastKillAt && b.Net().Any(x => ItemDictionary.IsMonsterSoul(x.ItemId) && (x.Unknown || x.Net > 0))) _lastKillAt = b.At;
    }

    /// <summary>
    /// Whether <paramref name="n"/> of <paramref name="id"/> that came at <paramref name="at"/> went again in the next
    /// update, with nothing else coming in it: the game used it at once (Stigma Shard points, a stack of Soul Crystals
    /// registered as it lands), so it was never loot. An item that goes into something else is not this; gear dismantled
    /// into Enhance Stones has its own rule (<see cref="DismantledAtOnce"/>, <see cref="TakeBackDismantled"/>).
    /// </summary>
    bool UsedAtOnce(long id, long n, DateTime at) =>
        _itemHeld.Any(h => (h.At - at).TotalSeconds is >= 0 and <= 1.5 && h.Net() is var hn
            && hn.Any(x => x.ItemId == id && !x.Unknown && x.Net == -n) && !hn.Any(x => x.Unknown || x.Net > 0));

    /// <summary>How old an update is before <see cref="DropNow"/> takes it: long enough for the next update (the item used or dismantled at once, 0.1 s later on 2026-11-21 17:56:00) to be in.</summary>
    const double DropSettleSeconds = 0.5;

    /// <summary>
    /// A kill's drop while the screen is the backup: one unbound item went up, nothing went down, and a kill came in the last
    /// <see cref="PacketNearSeconds"/>. It counts now. The screen reads the chat only every <see cref="BackupPassMs"/> then,
    /// so waiting for the chat's line took every drop to the full <see cref="PacketHoldSeconds"/> (2026-11-21: all eight of
    /// the day's monster drops 3.01 to 3.24 s late). An update of several items (an event's rewards, a box's contents) and
    /// a bound item (a quest's turn-in) still wait for the chat's say; the chat's line that comes later is this rise's to
    /// the agreement check, as when the line comes after the count.
    /// </summary>
    bool DropNow(ItemBatch b, DateTime now)
    {
        if (!_screenBackup || b.Fell || (now - b.At).TotalSeconds < DropSettleSeconds) return false;
        if ((_lastKillAt - b.At).TotalSeconds < -PacketNearSeconds) return false;
        if (Instances.Current != null || InRun || CraftingAt(b.At)) return false;
        var up = b.Net().Where(x => !ItemDictionary.IsMonsterSoul(x.ItemId) && (x.Unknown || x.Net > 0)).ToList();
        return up.Count == 1 && !up[0].Unknown && _items.Get(up[0].ItemId) != null
            && !_items.NameOf(up[0].ItemId).EndsWith("(Bound)", StringComparison.OrdinalIgnoreCase);
    }

    // ---- gear dismantled at once ----
    /// <summary>How soon after its pickup a piece of gear dismantled into Enhance Stones is taken out of the count.</summary>
    const double GearDismantleSeconds = 60;
    /// <summary>Gear counted as farming's pickups lately, for a dismantle right after (<see cref="TakeBackDismantled"/>).</summary>
    readonly List<ItemAcquired> _gearCounted = new();

    bool IsEnhanceStone(long id) => string.Equals(_items.NameOf(id), "Enhance Stone", StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether a held update within <see cref="GearDismantleSeconds"/> after <paramref name="at"/> takes <paramref name="id"/> away with Enhance Stones coming in.</summary>
    bool DismantledAtOnce(long id, DateTime at) =>
        _itemHeld.Any(h => (h.At - at).TotalSeconds is >= 0 and <= GearDismantleSeconds && h.Net() is var hn
            && hn.Any(x => x.ItemId == id && !x.Unknown && x.Net < 0) && hn.Any(x => !x.Unknown && x.Net > 0 && IsEnhanceStone(x.ItemId)));

    /// <summary>
    /// Gear dismantled right after it dropped (2026-11-21 17:56:00: "Forgotten Gloves −1, Enhance Stone +18" 0.1 s after the
    /// gloves came; 12:51:37, Riverside Gloves and Enhance Stone +23): it never reaches the Market, so it is no loot worth
    /// counting. Each piece that went with Enhance Stones coming in, counted as a pickup in the last
    /// <see cref="GearDismantleSeconds"/>, is taken back out of every count, as "Not a drop" would. The Enhance Stones are
    /// not a pickup (they came with something going down) and have no price.
    /// </summary>
    void TakeBackDismantled(ItemBatch b)
    {
        _gearCounted.RemoveAll(g => (b.At - g.Time).TotalSeconds > GearDismantleSeconds * 2);
        if (_gearCounted.Count == 0 || !b.Net().Any(x => !x.Unknown && x.Net > 0 && IsEnhanceStone(x.ItemId))) return;
        foreach (var (id, n, unknown) in b.Net())
        {
            if (unknown || n >= 0 || !_items.IsGear(id)) continue;
            long left = -n;
            while (left > 0)
            {
                int i = _gearCounted.FindLastIndex(g => g.ItemId == id && g.Count <= left && (b.At - g.Time).TotalSeconds is >= 0 and <= GearDismantleSeconds);
                if (i < 0) break;
                var g = _gearCounted[i]; _gearCounted.RemoveAt(i); left -= g.Count;
                var line = Live.Find(l => l.Kind == "item" && l.ItemId == g.ItemId && l.At == g.Time && l.Amount == g.Count);
                var t = NotADrop(g, line?.Where ?? "", line);
                CrashLog.Note($"packets: {_items.NameOf(id)} ×{g.Count} (counted {(b.At - g.Time).TotalSeconds:0.0} s before) dismantled into Enhance Stones: no loot, {(t.Any ? "taken back out of the count" : "no count held it")}");
                _packetCountedNow = true;
            }
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
        // a gather's yield: the gather's report is its evidence, and the chat has nothing to add (2026-11-14: a Fine
        // Orichalcum Ore waited 1.7 s for its line)
        if (GatherOf(b) != null) return true;
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

    /// <summary>
    /// Inside an event run, an inventory update is the run's by the game's own item data (2026-11-15, Steven: "all shugo
    /// festival runs right now are requiring review - it shouldn't.... fix it based on packets"): each item that went up,
    /// bound or not by its id (a bound item has its own), with an id the item list lacks or a stack first seen kept as not
    /// known. This comes before the pickup gate, which leaves an update out with a reward screen open or the chat calling it
    /// a reward (both are what an event's reward is), and before the agreement check stops counting farming's pickups: the
    /// run's rewards are its own reading (InstanceLog.GameLoot). An update with something going down in it is not taken (a
    /// box opened, a stack moved): its rises may be a box's contents.
    /// </summary>
    void GameRewards(PacketReader p, ItemBatch b)
    {
        if (Instances.CurrentKind != "Event" || !p.ReadsItemsAt(b.At)) return;
        if (b.Fell) { CrashLog.Note($"packets: an update with something going down inside {Instances.Open}: not the run's rewards"); return; }
        var rises = new List<(long Id, long Count, bool Bound, bool Listed, bool FirstSeen, string Name)>();
        foreach (var (gid, n, unknown) in b.Net())
        {
            if (!unknown && n <= 0) continue;
            long id = _items.FromGame(gid);
            string name = _items.NameOf(id);
            rises.Add((id, unknown ? 0 : n, name.EndsWith("(Bound)", StringComparison.OrdinalIgnoreCase), _items.Get(id) != null, unknown, name));
        }
        // the run's: their chat lines, however late (the reward screen over the chat, a long list scrolling by), are those
        // rewards to the agreement check, not pickups it missed (2026-11-15 11:05:29: seven of them handed items back to the screen)
        if (Instances.GameLoot(b.At, rises))
            foreach (var r in rises) if (r.Listed && !r.FirstSeen) p.RunTook(r.Id, r.Name, r.Count, b.At);
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
        GameRewards(p, b);
        if (!p.CountingItems(b.At)) return;
        // something went down with it: a craft's materials, a box opened, a stack moved or something used, never a pickup
        // (a monster's soul taken into the Soul Codex, a moment after it came, is said once a minute: one with every kill)
        if (b.Fell)
        {
            if (b.Rows.All(r => ItemDictionary.IsMonsterSoul(r.ItemId))) CrashLog.NoteBurst("packets: a monster's soul taken into the Soul Codex");
            else { CrashLog.Note($"packets: {Said()} in one update with something going down: not a pickup"); TakeBackDismantled(b); }
            return;
        }
        var gather = GatherOf(b);
        // the screen's line for any item of this one update: the update is a pickup the player saw, all of it (2026-11-20
        // 11:34:49, Gartua's drop: Refining Stone and Artisan's Refining Stone came in one update; the plain one was judged
        // first, before the Artisan's count made the update "near", and was left out as "no kill or pickup line around it")
        bool updateSeen = b.Net().Any(x => !x.Unknown && x.Net > 0 && p.ScreenNearItem(x.ItemId, b.At, PacketNearSeconds));
        foreach (var (id, n0, unknown) in b.Net())
        {
            string name = _items.NameOf(id);
            long n = n0;
            bool find = IsRareFind(name);
            // a monster's soul (the loot list's "Soul: Swarm (Bound)"): a kill's (SoulKill), registered in the Soul Codex the
            // moment it lands, so no loot (2026-11-17: 690 of these were "not on the item list" in two days)
            if (ItemDictionary.IsMonsterSoul(id)) { CrashLog.NoteBurst($"packets: a monster's soul ({name}) {(unknown ? "first seen" : $"+{n:N0}")}: a kill's, taken into the Soul Codex at once; not loot"); continue; }
            if (_items.Get(id) == null) { if (unknown || n > 0) CrashLog.Note($"packets: {name} {(unknown ? "first seen" : $"+{n:N0}")} is not on the item list: the chat's"); continue; }
            if (unknown)
            {
                // a stack seen for the first time (the reading began mid-connection, after an update): no change known. A
                // gather's yield counts all the same, with the gather's own count (one for a rare find); the chat's line
                // tops it up if it says more (CountTopUp)
                if (gather is not { } g0) { CrashLog.Note($"packets: {name} first seen (its earlier amount not known): its pickup is the chat's"); continue; }
                n = g0.ItemId == id ? Math.Max(1, g0.Count) : 1;
            }
            if (n <= 0) continue;
            // the item's next rise is the next pickup's: the lines after it are that one's, never a top-up of the first sight
            // (a first-seen ore counted as one, the next gather's "Acquired Orichalcum Ore x2." 5 s later topped it up to two)
            if (!unknown) _provisional.RemoveAll(x => x.Id == id && x.At < b.At);
            bool bound = name.EndsWith("(Bound)", StringComparison.OrdinalIgnoreCase);
            if (gather != null && !InBase)
            {
                // a gather's yield: the gather is its evidence, bound or not, a rare find or not, and it counts now
                var gev = new ItemAcquired(b.At, id, (int)Math.Min(n, 99999), bound) { Via = "packets" };
                CrashLog.Note($"packets: {name} +{n:N0}{(unknown ? " (its stack first seen: the gather's own count)" : "")}{(find ? ", a rare find" : "")}: gathered, counted from the game's message, {After(b.At)}");
                CommitWith(gev, "counter");
                if (unknown) { p.CountedFirstSight(id, n, b.At); _provisional.Add(new Provisional(id, b.At, (int)n, 0)); _provisional.RemoveAll(x => (b.At - x.At).TotalMinutes > 1); }
                NotePacketDelay(b.At);
                if (b.At > _packetPickupAt) _packetPickupAt = b.At;
                continue;
            }
            // the chat's or the loot list's line for it, or its bound twin's (the chat reads "(Bound)" off a name it guessed)
            bool screenSaw = p.ScreenNearItem(id, b.At, PacketNearSeconds);
            bool near = (_lastKillAt - b.At).TotalSeconds >= -PacketNearSeconds || (_lastGatherAt - b.At).TotalSeconds >= -PacketNearSeconds
                || (_gatherRaces.Count > 0 && (_gatherRaces[^1].At - b.At).TotalSeconds >= -PacketNearSeconds)
                // a pickup the traffic counted around it (a kill's Kinah, Rank or Abyss Points): still farming, as for the
                // currencies. In the Abyss the game names the player's own kill for only some kills: on 2026-11-16 from 16:37
                // to 17:00 three Earth gear pieces, a mace and seven bound drops came among Kinah counted every second, and
                // none counted
                || (_packetPickupAt - b.At).TotalSeconds >= -PacketNearSeconds
                || updateSeen;
            string un = Unbound(name);
            bool outside = Instances.Current == null && !InRun;
            // a bound item comes from a quest or a box when one was just turned in or opened (BoundRewardWhy); otherwise, with
            // a kill around it, it dropped (Steven, 2026-11-17: "tracker also missed a blue refining stone ... and raw
            // leather", both bound, both among the Abyss Swarm's kills)
            string? rewardWhy = bound && outside && !screenSaw ? BoundRewardWhy(b.At) : null;
            string? why = InBase && !name.Contains("Odyle", StringComparison.OrdinalIgnoreCase) ? $"in a base ({Zone})"
                : MarketOpen?.Invoke(b.At) == true ? "with the Market open"
                : Target.RewardOpen(b.At) ? "with a reward screen open (the reward table reads it)"
                : _rewardLines.Any(r => Math.Abs((r.At - b.At).TotalSeconds) <= PacketNearSeconds && string.Equals(r.Name, un, StringComparison.OrdinalIgnoreCase)) ? "with the chat calling it a reward"
                : outside && b.At <= _achieveUntil ? "with an achievement's reward"
                : UsedAtOnce(id, n, b.At) ? "used at once (it went again in the next update)"
                : outside && _items.IsGear(id) && DismantledAtOnce(id, b.At) ? "dismantled at once (it went into Enhance Stones in a later update)"
                : outside && !IsGatherMaterial(id) && PickupCheck.TooMany((int)Math.Min(n, int.MaxValue)) is { } tooMany ? tooMany
                : rewardWhy != null ? $"bound, with no pickup line for it, {rewardWhy} (a quest's or a box's reward)"
                : bound && !screenSaw && !near && Instances.Current == null ? "bound, with no pickup line or kill for it (a quest's or a box's reward)"
                : !near && !screenSaw && Instances.Current == null ? "with no kill, gather or pickup line around it" : null;
            // ...and the chat's line for it, however late it shows (a window over the chat), is covered by this rise: never a pickup
            if (why != null) { CrashLog.Note($"packets: {name} +{n:N0} {why}: not a pickup; not counted"); p.ItemExcluded(id, b.At, why); continue; }
            var ev = new ItemAcquired(b.At, id, (int)Math.Min(n, 99999), bound) { Via = "packets" };
            CrashLog.Note($"packets: {name} +{n:N0}: counted from the game's message, {After(b.At)}");
            CommitWith(ev, "counter");
            if (outside && _items.IsGear(id)) { _gearCounted.Add(ev); if (_gearCounted.Count > 40) _gearCounted.RemoveAt(0); }
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
        PanelRace(recipe, o.Ok, o.Combo, o.At, _panelLevel, _queueOpen ? _queueId : 0, makes: o.Makes, why: o.Why, fromPackets: true);
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
            // an assist is a death at someone else's hand: no drop of the player's comes with it
            if (!o.Death && !o.Assist && (o.At - _lastKillAt).TotalSeconds > 0) _lastKillAt = o.At;
            string what = o.Death ? $"died{(o.Name.Length > 0 ? " to " + o.Name : "")}"
                : o.Assist ? $"an assist on the player {(o.Name.Length > 0 ? o.Name : "(unnamed)")}: killed by {(o.Killer.Length > 0 ? o.Killer : "someone else")}, your last hit {o.HitBefore:0.0} s before"
                : $"killed {(o.Player ? "the player " + (o.Name.Length > 0 ? o.Name : "(unnamed)") : "a monster")}{(o.Credited ? " (the game named no killer; your Rank Points rose with it)" : "")}";
            CrashLog.Note($"packets: {what}");
            CommitWith(new FightCounted(o.At, o.Player, o.Death, o.Name) { Via = "packets", Assist = o.Assist }, "counter");
            FightToParser(o);   // the Parser's runs and fights (TrackerSession.Parser.cs)
        }
    }

    // ---- gathering (0.10.80) ----
    long _lastGatherNode = -1;
    /// <summary>The gathers that gave something lately (when, what, how many the game's report said), for their items' updates.</summary>
    readonly List<(DateTime At, long ItemId, int Count)> _gathered = new();
    /// <summary>How near a gather's report its items' update comes: the same moment (13:10:48.3, 2026-11-14), or the next message.</summary>
    const double GatherItemSeconds = 1.5;

    /// <summary>The gather an inventory update is the yield of: one that gave something within <see cref="GatherItemSeconds"/>, or none.</summary>
    (DateTime At, long ItemId, int Count)? GatherOf(ItemBatch b)
    {
        var g = _gathered.Where(x => Math.Abs((x.At - b.At).TotalSeconds) <= GatherItemSeconds).OrderBy(x => Math.Abs((x.At - b.At).TotalSeconds)).ToList();
        if (g.Count == 0) return null;
        // its own item in the update first, else the nearest
        return g.FirstOrDefault(x => b.Rows.Any(r => r.ItemId == x.ItemId)) is { At.Ticks: > 0 } own ? own : g[0];
    }

    /// <summary>Gathering's rare finds (Poko's Skin Bundle): from any node, one at a time, counted even unpriced.</summary>
    bool IsRareFind(string name) => GatheringCatalog.Find(name) is { Family: GatherFamily.Find };

    /// <summary>
    /// Counts a provisional pickup's top-up: a stack seen for the first time was counted at the gather's report (its own
    /// count, or one for a rare find), and the chat's lines, a moment later, say more came (the upgrade roll's two; two
    /// procs' lines on one gather, added up). Counted: what the session has counted for it; Lines: what its lines said so far.
    /// The item's next rise ends it: the lines after that are the next pickup's.
    /// </summary>
    readonly List<Provisional> _provisional = new();
    readonly record struct Provisional(long Id, DateTime At, int Counted, int Lines);
    /// <summary>How long after a first sight a chat line for its item can still be that gather's (the chat lags the game by a second or two).</summary>
    const double ProvisionalSeconds = 8;

    /// <summary>
    /// The player's gathering from the game's messages (Steven, 2026-11-13: "exact gathering results"): every attempt, how it
    /// ended (worked, the upgrade roll won, failed), its node, what it gave and how many. Each is one attempt under its
    /// node's skill (named from what it gave, GatheringCatalog), a node when it is the first attempt on it. Meanwhile the
    /// screen's attempts (the bar's races, results whose race went unseen, failure lines) are set aside for the agreement
    /// check (Tally): one the traffic didn't show is counted from the screen after all, and three of them in five minutes
    /// hand gathering back to the screen. The items gathered still count from the inventory's updates, as every pickup does.
    /// </summary>
    void DrainGathers(PacketReader p, DateTime now)
    {
        while (p.NextGather() is { } g)
        {
            string item = _items.NameOf(g.ItemId);
            var node = GatheringCatalog.NodeOf(item);
            var family = node?.Family ?? GatherFamily.Unknown;
            string name = node?.Name ?? UnknownNode;
            bool newNode = g.Attempt == 1 || (g.Attempt == 0 && g.NodeId != _lastGatherNode);
            _lastGatherNode = g.NodeId;
            if (g.Outcome != GatherEnd.Failed) { _gathered.Add((g.At, g.ItemId, g.Count)); _gathered.RemoveAll(x => (g.At - x.At).TotalMinutes > 1); }
            // its item's rise in the inventory, in the same moment, is a gather's: it counts at once
            if (g.At > _lastGatherAt) _lastGatherAt = g.At;
            string how = g.Outcome switch { GatherEnd.Upgrade => "the upgrade roll won", GatherEnd.Failed => "failed", _ => "worked" };
            string what = g.Outcome == GatherEnd.Failed ? $"a {name} node" : $"{item} x{g.Count}";
            if (!p.CountingGathers(g.At)) { CrashLog.Note($"packets: gathering {how}, {what}: the screen counts gathering now ({p.GathersBroken ?? "the reading just began"})"); continue; }
            lock (_tallyLock) Named(family, name, "result", g.At);
            if (GatherInRun) { CrashLog.Note($"packets: gathering {how}, {what}: inside the run, not farming's gathering"); continue; }
            foreach (var e in FarmEngines()) { e.AttemptsFromTally = true; e.GatherAttempt(g.At, family, name, g.Outcome != GatherEnd.Failed, g.Outcome == GatherEnd.Upgrade, newNode); }
            CrashLog.Note($"packets: gathering {how}, {what}{(g.Attempt > 0 ? $", try {g.Attempt} on the node" : "")}{(newNode ? " (a new node)" : "")}: counted from the game's message, {After(g.At)}");
            NotePacketDelay(g.At);
        }
        // the screen's attempts the traffic never showed: the screen's after all
        while (p.NextUncoveredGather() is Attempt a)
        {
            a.Held = false;
            CrashLog.Note($"gather attempt at {a.At.ToLocalTime():HH:mm:ss.f}: {a.Node}, {(a.Ok ? "success" : "failed")}: the game's messages didn't show it, so the screen's count does");
            if (GatherInRun) continue;
            foreach (var e in FarmEngines()) { e.AttemptsFromTally = true; e.GatherAttempt(a.At, a.Family, a.Node, a.Ok); }
        }
    }

    /// <summary>For packet-test: a pickup through Commit, as a chat line or the counter would come, and a pass's drain.</summary>
    internal void CommitForTest(GameEvent ev) => Commit(ev);
    internal void DrainPacketsForTest() => DrainPackets();
    /// <summary>For packet-test: the reading loop's wait between passes, run for <paramref name="ms"/>.</summary>
    internal void WaitForTest(int ms) => WaitWithPacketsAsync(ms, CancellationToken.None).GetAwaiter().GetResult();
}