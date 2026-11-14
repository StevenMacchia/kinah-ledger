using System.Collections.Concurrent;
using System.Text;
using KinahLedger.Session;

namespace KinahLedger.Packets;

/// <summary>
/// Packet reading (Steven, 2026-11-13, after the AION 2 moderators allowed it): a copy of the game server's messages to this
/// PC, turned into Kinah, bound Kinah and Abyss Points changes by the release build's decoder (Packets/Decoder.cs; not part
/// of this source). Since 0.10.66 those three count from here (Session/TrackerSession.Packets.cs), the screen's reading of
/// them set aside while they do; packets.log says each change and, once a minute, how the screen's reading compared.
///
/// It only listens: the copy comes from Npcap or a raw socket (Packets/Sources.cs), filtered to the game's own server
/// connection, found from the game's process id in Windows' connection table. Nothing is sent, the game is never touched,
/// and no packet is kept: only the decoded changes go to the log (a PC that asked for a local recording keeps one, Packets/PacketLog.cs).
/// </summary>
public sealed class PacketReader : IDisposable
{
    /// <summary>Options › Game reading › Read the game's network traffic. On by default; with neither Npcap nor admin it just says why it is off.</summary>
    public bool Enabled { get; set; } = true;
    /// <summary>One line for Options: what packet reading is doing.</summary>
    public string State { get; private set; } = "Waiting for the game";
    /// <summary>True while a capture runs on the game's connection.</summary>
    public bool On => _testOn || _thread is { IsAlive: true };
    bool _testOn;
    /// <summary>For packet-test: reading as if a capture ran, the messages handed in by hand.</summary>
    internal void ReadingForTest() { _testOn = true; _testItems = true; _packetAt = DateTime.UtcNow; }
    bool _testItems;
    /// <summary>For packet-test: item changes as the decoder would report them (cut into updates by their batch).</summary>
    internal void ItemsForTest(params ItemChange[] changes) { _packetAt = DateTime.UtcNow; TakeItems(changes); }
    internal void AgreeItemsForTest(DateTime now) { lock (_gate) AgreeItems(now); }
    readonly Dictionary<CurrencyKind, long> _testAmounts = new();
    /// <summary>For packet-test: an amount as the decoder would report it (the change from the last one, or a baseline).</summary>
    internal void AmountForTest(CurrencyKind k, long amount, DateTime at)
    {
        _packetAt = DateTime.UtcNow;
        bool first = !_testAmounts.TryGetValue(k, out var was);
        _testAmounts[k] = amount;
        if (first || amount != was) Take(new CurrencyChange(at, k, first ? 0 : amount - was, amount, first));
    }
    internal void AgreeForTest() => Agree();
    public string Kind { get; private set; } = "";
    public long Packets => Interlocked.Read(ref _packets);
    /// <summary>Messages and bundles the decoder read, its resyncs and its currency messages, for the log.</summary>
    public (long Messages, long Bundles, int Resyncs, int CurrencyMessages) DecoderStats => _decoder?.Stats ?? default;
    /// <summary>
    /// World boss timers from the game's world map (the decoder's version 5, Session/BossTimers.cs): filled while the map is
    /// open, saved, counted down in between. Read from any thread.
    /// </summary>
    public BossTimers Bosses { get; } = new(BossTimers.DefaultPath);
    /// <summary>
    /// Timed event timers (the decoder's version 6, Session/EventTimers.cs): the game's clock for the schedule's countdowns,
    /// and the event the player is in, from the event's own server. Read from any thread.
    /// </summary>
    public EventTimers Timers { get; } = new();
    /// <summary>Where the decoder comes from: the release build's native one; a test hands in its own.</summary>
    public static Func<IGameDecoder?> Decoders { get; set; } = NativeDecoder.TryOpen;

    IGameDecoder? _decoder;
    readonly ConcurrentQueue<CurrencyChange> _changes = new();
    readonly ConcurrentQueue<ItemBatch> _items = new();
    readonly ConcurrentQueue<FightEvent> _fights = new();
    readonly ConcurrentQueue<AreaEvent> _areas = new();
    readonly ConcurrentQueue<object> _uncovered = new();
    TcpStream _down = new();
    TcpTable.Link? _link;
    IPacketSource? _source;
    Thread? _thread;
    volatile bool _stop;
    DateTime _failedAt = DateTime.MinValue, _minuteAt = DateTime.UtcNow, _packetAt = DateTime.MinValue;
    long _packets;
    readonly object _gate = new();
    // per currency: when its baseline came (the first amount after a connection), and when the packets stopped agreeing
    readonly Dictionary<CurrencyKind, DateTime> _baseAt = new();
    readonly Dictionary<CurrencyKind, string> _broken = new();
    // the screen's readings set aside, and the packets' own changes, for the agreement check and the minute's line
    readonly List<(CurrencyKind K, long Amount, DateTime At)> _screen = new();
    readonly List<(CurrencyKind K, long Delta, DateTime At)> _seen = new();
    readonly List<DateTime> _misses = new();
    // items and crafts: the item changes seen (a rise, or a stack's first sight), the screen's item pickups and crafting
    // races set aside for the agreement check, the misses, and why either stopped
    readonly List<(long Id, long Delta, bool First, DateTime At)> _itemSeen = new();
    readonly List<(long Id, int Count, DateTime At, bool Late, object? Tag)> _screenItems = new();
    readonly List<(long Id, DateTime At)> _screenItemsAll = new();
    readonly List<DateTime> _craftSeen = new(), _screenCrafts = new(), _itemMisses = new(), _craftMisses = new();
    string? _itemsBroken, _craftsBroken;
    DateTime _readingSince = DateTime.MaxValue;
    /// <summary>What packets.log calls an item: the session's item list.</summary>
    public Func<long, string>? NameOf { get; set; }
    /// <summary>For the agreement check: whether crafting was under way at a time (a chat item line then is the craft's item).</summary>
    public Func<DateTime, bool>? CraftingAt { get; set; }

    /// <summary>A change the decoder found: kept for the agreement check, queued for counting, logged.</summary>
    void Take(CurrencyChange c)
    {
        lock (_gate)
        {
            if (c.First) _baseAt[c.What] = c.At;
            else _seen.Add((c.What, c.Delta, c.At));
        }
        _changes.Enqueue(c); Arrived();
        Log($"{Local(c.At)}  {Name(c.What)} {(c.First ? "now" : Signed(c.Delta))} · {c.Amount:N0}");
    }

    /// <summary>A field boss from the world map's list: into the timers, and a line in packets.log when its state or spawn changed.</summary>
    void TakeBoss(BossSighting b)
    {
        if (!Bosses.Take(b)) return;
        string name = b.Name.Length > 0 ? b.Name : "a boss not named yet";
        string what = b.State switch
        {
            BossSighting.Kind.Alive => "up" + (b.Time is { } t1 ? $" since {Local(t1 + (b.ServerNow is { } s1 ? b.At - s1 : TimeSpan.Zero))}" : ""),
            BossSighting.Kind.Dead => b.Time is { } t2 && b.ServerNow is { } s2 ? $"spawns in {BossTimers.Countdown(t2 - s2)}" : "waiting",
            _ => "no timer",
        };
        Log($"{Local(b.At)}  world boss: {name}{(b.Place.Length > 0 ? $" ({b.Place})" : "")} {what}");
    }

    /// <summary>The next change for the tracker to count (TrackerSession.DrainPackets), null when there is none.</summary>
    public CurrencyChange? Next() => _changes.TryDequeue(out var c) ? c : null;

    // set when a change or an update is queued: the session's wait between screen passes ends on it, so a pickup's amount is
    // counted as it comes, not at the next pass (Steven, 2026-11-13: "if we're using packets these numbers should be instantaneous")
    readonly SemaphoreSlim _arrived = new(0, 1);
    void Arrived() { if (_arrived.CurrentCount == 0) try { _arrived.Release(); } catch (SemaphoreFullException) { } }
    /// <summary>Waits up to <paramref name="ms"/> for a change or an update to be queued; true when one was.</summary>
    public async Task<bool> WaitArrivedAsync(int ms, CancellationToken ct)
    {
        try { return await _arrived.WaitAsync(ms, ct); }
        catch (OperationCanceledException) { return false; }
    }
    /// <summary>True when changes or updates wait to be drained.</summary>
    public bool Pending => !_changes.IsEmpty || !_items.IsEmpty || !_fights.IsEmpty || !_areas.IsEmpty;

    /// <summary>Item changes the decoder found, cut into the game's updates: kept for the agreement check, queued, logged.</summary>
    void TakeItems(IReadOnlyList<ItemChange> rows)
    {
        foreach (var b in ItemBatch.Cut(rows))
        {
            lock (_gate) foreach (var r in b.Rows) _itemSeen.Add((r.ItemId, r.Delta, r.First, r.At));
            _items.Enqueue(b); Arrived();
            Log($"{Local(b.At)}  {string.Join(", ", b.Rows.Select(r => $"{ItemName(r.ItemId)} {(r.First ? "now" : Signed(r.Delta))} · {r.Total:N0}"))}{(b.Rows.Count > 1 ? $" (one update{(b.Fell ? ", something went down" : "")})" : "")}");
        }
    }
    string ItemName(long id) { try { return NameOf?.Invoke(id) ?? $"item {id}"; } catch (Exception) { return $"item {id}"; } }

    /// <summary>A kill the player is in, a death or a Rank Points change: queued for the tracker, logged.</summary>
    void TakeFight(FightEvent f)
    {
        _fights.Enqueue(f); Arrived();
        if (_fights.Count > 4096) _fights.TryDequeue(out _);
        Log($"{Local(f.At)}  " + (f.Kind == FightKind.RankPoints ? $"Rank Points {(f.Delta == 0 ? "now" : Signed(f.Delta))} · {f.Total:N0}"
            : f.Flags.HasFlag(FightFlags.VictimMe) ? $"died{(f.KillerName.Length > 0 ? " to " + f.KillerName : "")}"
            : $"{(f.Flags.HasFlag(FightFlags.Mine) ? "killed" : "a kill naming no killer:")} {(f.Flags.HasFlag(FightFlags.VictimPlayer) ? "the player " + f.VictimName : f.Flags.HasFlag(FightFlags.VictimMonster) ? $"monster {f.MonsterId}" : "something")}"));
    }

    /// <summary>
    /// A timed event's part from its server, or a sample of the game's clock: into the timers; a part is queued for the
    /// session (an event run starts and ends from it, InstanceLog.GameSaid) and logged.
    /// </summary>
    void TakeArea(AreaEvent a)
    {
        Timers.Take(a);
        if (a.What == AreaEvent.Kind.Clock) return;
        _areas.Enqueue(a); Arrived();
        if (_areas.Count > 256) _areas.TryDequeue(out _);
        string name = a.Label.Length > 0 ? a.Label : $"an event the tracker doesn't know yet ({a.Code})";
        string what = a.What switch
        {
            AreaEvent.Kind.Phase => (a.Phase == AreaEvent.Stage.Preparing ? "getting ready" : a.Phase == AreaEvent.Stage.Over ? "over" : "under way")
                + (a.PhaseStart is { } ps && a.ServerNow is { } sv ? $" since {Local(ps + (a.At - sv))}" : ""),
            AreaEvent.Kind.Over => "over",
            _ => a.ServerMoved ? "left (the game moved to another server)" : "left",
        };
        Log($"{Local(a.At)}  event: {name} {what}");
    }

    /// <summary>The next part of a timed event for the tracker (TrackerSession.DrainPackets), null when there is none.</summary>
    public AreaEvent? NextArea() => _areas.TryDequeue(out var a) ? a : null;
    internal void AreaForTest(AreaEvent a) => TakeArea(a);

    /// <summary>The next fight event for the tracker (TrackerSession.DrainPackets), null when there is none.</summary>
    public FightEvent? NextFight() => _fights.TryDequeue(out var f) ? f : null;
    /// <summary>True while the reader gets the player's kills and deaths from the game (a decoder of interface version 3).</summary>
    public bool ReadsFights => On && (_testFights || _decoder?.ReadsFights == true);
    bool _testFights;
    internal void FightsForTest(FightEvent f) { _testFights = true; if (f.Kind == FightKind.RankPoints) Take(new CurrencyChange(f.At, CurrencyKind.RankPoints, f.Delta, f.Total, f.Delta == 0)); _fights.Enqueue(f); }

    /// <summary>The next inventory update for the tracker (TrackerSession.DrainPackets), null when there is none.</summary>
    public ItemBatch? NextItems() => _items.TryDequeue(out var b) ? b : null;

    /// <summary>A screen pickup the agreement check found the traffic didn't count (never showed it, or showed only a stack's first sight): the screen's to count after all.</summary>
    public object? NextUncovered() => _uncovered.TryDequeue(out var o) ? o : null;

    /// <summary>True while the decoder reads item changes on a live connection (the release build's klnet.dll, version 2).</summary>
    bool ItemsOn => On && (DateTime.UtcNow - _packetAt).TotalSeconds <= 15 && (_testItems || _decoder?.ReadsItems == true);
    DateTime ReadingSince => _testItems ? DateTime.MinValue : _readingSince;

    /// <summary>Whether the packets count item pickups at <paramref name="at"/>: reading items since a while before, and not stopped.</summary>
    public bool CountingItems(DateTime at) { if (!ItemsOn) return false; lock (_gate) return _itemsBroken == null && at > ReadingSince.AddSeconds(4); }

    /// <summary>Whether the packets count crafts at <paramref name="at"/>: what each craft made, and the tries that made nothing.</summary>
    public bool CountingCrafts(DateTime at) { if (!ItemsOn) return false; lock (_gate) return _craftsBroken == null && at > ReadingSince.AddSeconds(4); }

    /// <summary>Why the packets stopped counting items, or crafts; null while they count.</summary>
    public string? ItemsBroken { get { lock (_gate) return _itemsBroken; } }
    public string? CraftsBroken { get { lock (_gate) return _craftsBroken; } }

    /// <summary>An item pickup the screen read and the packets count instead, for the agreement check. Late: a chat line, counted from the screen if the traffic didn't count it (<see cref="NextUncovered"/>).</summary>
    public void ScreenSawItem(long id, int count, DateTime at, bool late, object? tag)
    {
        lock (_gate)
        {
            _screenItems.Add((id, count, at, late, tag));
            _screenItemsAll.Add((id, at)); _screenItemsAll.RemoveAll(s => (at - s.At).TotalMinutes > 2);
            if (tag != null) { _chatItemsAll.Add((id, at)); _chatItemsAll.RemoveAll(s => (at - s.At).TotalMinutes > 2); }
        }
    }
    readonly List<(long Id, DateTime At)> _chatItemsAll = new();
    /// <summary>Whether the chat (not the loot list) said a pickup of the item within <paramref name="seconds"/> of <paramref name="at"/>: its say on the pickup is in.</summary>
    public bool ChatNearItem(long id, DateTime at, double seconds) { lock (_gate) return _chatItemsAll.Any(s => s.Id == id && Math.Abs((s.At - at).TotalSeconds) <= seconds); }

    /// <summary>Whether the screen read a pickup of the item within <paramref name="seconds"/> of <paramref name="at"/>.</summary>
    public bool ScreenNearItem(long id, DateTime at, double seconds) { lock (_gate) return _screenItemsAll.Any(s => s.Id == id && Math.Abs((s.At - at).TotalSeconds) <= seconds); }

    /// <summary>The traffic showed a craft's materials or its item at <paramref name="at"/>.</summary>
    public void CraftSeen(DateTime at) { lock (_gate) { _craftSeen.Add(at); _craftSeen.RemoveAll(c => (at - c).TotalMinutes > 2); } }

    // when the traffic lost bytes (TcpStream gave up on a hole), the last two minutes
    readonly List<DateTime> _gapsAt = new();
    DateTime _dataAt = DateTime.MinValue;
    /// <summary>Whether the traffic lost bytes between <paramref name="from"/> and <paramref name="to"/>.</summary>
    public bool GapBetween(DateTime from, DateTime to) { lock (_gate) return _gapsAt.Any(g => g >= from.AddSeconds(-1) && g <= to.AddSeconds(1)); }
    /// <summary>For packet-test: the traffic lost bytes at <paramref name="at"/>.</summary>
    internal void GapForTest(DateTime at) { lock (_gate) _gapsAt.Add(at); }

    /// <summary>A race the crafting panel finished while the packets count crafts, for the agreement check.</summary>
    public void ScreenSawCraft(DateTime at) { lock (_gate) _screenCrafts.Add(at); }

    /// <summary>
    /// Whether the packets count <paramref name="k"/> at <paramref name="at"/>: reading now, its baseline in a while before
    /// (a line within 4 s after the baseline is the pickup that baseline came with: the chat's), and not broken.
    /// </summary>
    public bool Counting(CurrencyKind k, DateTime at)
    {
        if (!On || (DateTime.UtcNow - _packetAt).TotalSeconds > 15) return false;
        lock (_gate) return !_broken.ContainsKey(k) && _baseAt.TryGetValue(k, out var b) && at > b.AddSeconds(4);
    }

    /// <summary>Why the packets stopped counting <paramref name="k"/>, or null.</summary>
    public string? Broken(CurrencyKind k) { lock (_gate) return _broken.GetValueOrDefault(k); }

    /// <summary>A pickup the screen read (a chat line, the counter, the loot list) and the packets count instead: kept for the agreement check.</summary>
    public void ScreenSaw(CurrencyKind k, long amount, DateTime at) { lock (_gate) { _screen.Add((k, amount, at)); _screenAll.Add((k, at)); _screenAll.RemoveAll(s => (at - s.At).TotalMinutes > 2); } }
    // every screen reading set aside, kept two minutes (the agreement check takes its own copy off _screen)
    readonly List<(CurrencyKind K, DateTime At)> _screenAll = new();

    /// <summary>Whether the screen read a pickup of <paramref name="k"/> within <paramref name="seconds"/> of <paramref name="at"/> (and the packets count it instead).</summary>
    public bool ScreenNear(CurrencyKind k, DateTime at, double seconds) { lock (_gate) return _screenAll.Any(s => s.K == k && Math.Abs((s.At - at).TotalSeconds) <= seconds); }

    /// <summary>Every few seconds from the game watcher: follows the game's server connection, checks agreement, writes the minute's line.</summary>
    public void Tick(IReadOnlyCollection<uint> gamePids, SessionEngine? chat)
    {
        try { TickCore(gamePids); Agree(); Minute(); }
        catch (Exception ex) { CrashLog.Write("packet reading", ex); State = "Off: " + ex.Message; StopCapture(); }
    }

    void TickCore(IReadOnlyCollection<uint> gamePids)
    {
        if (!Enabled) { StopCapture(); State = "Off in Options"; return; }
        if (gamePids.Count == 0) { StopCapture(); State = "Waiting for the game"; return; }
        var game = TcpTable.GameServer(TcpTable.Of(gamePids));
        if (game == null) { StopCapture(); State = "Waiting for the game to connect to its server"; return; }
        // another server (an event, the Abyss, a relog): a new stream; the amounts carry on across it
        if (_link != null && game != _link)
        {
            var now = DateTime.UtcNow;
            Log($"{Local(now)}  the game moved to {game.Remote}:{game.RemotePort}");
            StopCapture();
            // off the event's server without its "closed": the event is left all the same
            if (Timers.Current(now) is { LeftAt: null } live) TakeArea(new AreaEvent(now, null, AreaEvent.Kind.Closed, live.Stage, null, live.Family, live.Name, "", 0, ServerMoved: true));
        }
        if (!On && (DateTime.UtcNow - _failedAt).TotalSeconds >= 60) StartCapture(game);
    }

    void StartCapture(TcpTable.Link game)
    {
        // this build reads no traffic without the decoder (a build from the public source): the screen counts, as before
        _decoder ??= Decoders();
        if (_decoder == null) { State = "Off: this build can't read the game's traffic (the downloaded tracker can)"; _failedAt = DateTime.UtcNow; return; }
        var src = PacketSources.Open(game, out var why);
        if (src == null)
        {
            _failedAt = DateTime.UtcNow;
            string was = State;
            State = PacketSources.NpcapInstalled ? "Off: " + why : "Off: install Npcap (npcap.com) to count from the game's traffic";
            if (State != was) Log($"{Local(DateTime.UtcNow)}  packet reading off: {why}");
            return;
        }
        _source = src; _link = game; Kind = src.Kind;
        // a new connection starts from a fresh amount: the chat counted while nothing was read, so the first message is a baseline
        var dec = _decoder;
        dec.Reset(); lock (_gate) { _baseAt.Clear(); _readingSince = DateTime.UtcNow; }
        var rows = new List<ItemChange>();
        _down = new TcpStream
        {
            OnData = (d, at) =>
            {
                _dataAt = at;
                dec.Feed(d, at);
                while (dec.Next() is { } ch) Take(ch);
                rows.Clear();
                while (dec.NextItem() is { } it) rows.Add(it);
                if (rows.Count > 0) TakeItems(rows);
                while (dec.NextBoss() is { } boss) TakeBoss(boss);
                while (dec.NextAreaEvent() is { } area) TakeArea(area);
                while (dec.NextFight() is { } f)
                {
                    // Rank Points are a currency too (0.10.76): counted, checked against the chat and logged like Abyss Points
                    if (f.Kind == FightKind.RankPoints) Take(new CurrencyChange(f.At, CurrencyKind.RankPoints, f.Delta, f.Total, f.Delta == 0));
                    TakeFight(f);
                }
            },
            OnGap = () =>
            {
                dec.Gap();
                lock (_gate) { var at = _dataAt == DateTime.MinValue ? DateTime.UtcNow : _dataAt; _gapsAt.Add(at); _gapsAt.RemoveAll(g => (at - g).TotalMinutes > 2); }
            },
        };
        _stop = false;
        _thread = new Thread(Run) { IsBackground = true, Name = "packet reading", Priority = ThreadPriority.BelowNormal };
        _thread.Start();
        State = $"On ({Kind}): counting Kinah, bound Kinah, Abyss Points{(dec.ReadsItems ? ", item pickups and crafts" : "")}{(dec.ReadsFights ? ", your kills and deaths" : "")} from the game's server {game.Remote}:{game.RemotePort}";
        Log($"{Local(DateTime.UtcNow)}  reading {game.Remote}:{game.RemotePort} → {game.Local}:{game.LocalPort} with {Kind}");
    }

    void Run()
    {
        var src = _source!; var link = _link!;
        try
        {
            while (!_stop)
            {
                if (src.Next() is not { } p) continue;
                if (TcpSegment.Parse(p.Ip) is not { } s) continue;
                // the server's messages to this PC only (what this PC sends looked encrypted, and says nothing we need)
                if (s.SrcPort != link.RemotePort || s.DstPort != link.LocalPort || !s.Src.Equals(link.Remote)) continue;
                Interlocked.Increment(ref _packets);
                _packetAt = DateTime.UtcNow;
                _down.Add(s.Seq, s.Payload, p.At, s.Syn);
            }
        }
        catch (Exception ex) when (!_stop)
        {
            CrashLog.Write("packet reading", ex);
            State = "Off: " + ex.Message;
            _failedAt = DateTime.UtcNow;
        }
        finally { try { src.Dispose(); } catch (Exception) { } }
    }

    void StopCapture()
    {
        _stop = true;
        _thread?.Join(1000);
        _thread = null; _source = null; _link = null;
    }

    /// <summary>
    /// The agreement check: a pickup the screen read 15 s ago or more, of a currency the packets count, that the packets show
    /// no change of within 10 s either side, is a miss. Three misses of one currency in five minutes and the packets stop
    /// counting it (the message changed, a patch): the screen counts it again, and packets.log says so.
    /// </summary>
    void Agree()
    {
        var now = DateTime.UtcNow;
        lock (_gate)
        {
            foreach (var s in _screen.Where(s => (now - s.At).TotalSeconds >= 15).ToList())
            {
                _screen.Remove(s);
                if (_seen.Any(p => p.K == s.K && Math.Abs((p.At - s.At).TotalSeconds) <= 10)) continue;
                _misses.Add(now);
                _misses.RemoveAll(m => (now - m).TotalMinutes > 5);
                if (_misses.Count >= 3 && !_broken.ContainsKey(s.K))
                {
                    _broken[s.K] = $"the chat read {Name(s.K)} three times in five minutes that the game's messages didn't show";
                    Log($"{Local(now)}  {Name(s.K)}: {_broken[s.K]}; the screen counts it again");
                    CrashLog.Note($"packets: {_broken[s.K]}; the screen counts it again");
                }
            }
            _seen.RemoveAll(p => (now - p.At).TotalMinutes > 2);
            AgreeItems(now);
            _itemSeen.RemoveAll(p => (now - p.At).TotalMinutes > 2);
            _risesTaken.RemoveWhere(r => (now - r.At).TotalMinutes > 2);
        }
    }

    /// <summary>How near a screen pickup an item rise covers it: the game's update lands about 1.5 s before the chat line (up to 4.5 s), never much after.</summary>
    const double ItemBefore = 8, ItemAfter = 0.5;
    readonly HashSet<(long Id, DateTime At)> _risesTaken = new();

    /// <summary>
    /// How long before a chat line a rise of its item that already covered a line still covers this one (the same line read
    /// again: a window over the chat, then the chat shown once more), and how long a rise the tracker didn't count (the
    /// Market open, a reward screen, a base) covers the item's lines: a window over the chat can hide a bought item's line
    /// for minutes, and when it shows it is no pickup (Steven, 2026-11-13 18:35: two Wrathful Ego bought, counted as drops
    /// 23 s later).
    /// </summary>
    public const double RereadSeconds = 60, ExcludedSeconds = 300;
    readonly List<(long Id, DateTime At, string Why)> _itemExcluded = new();

    /// <summary>A rise of an item the tracker didn't count as a pickup (<paramref name="why"/>: with the Market open, ...): it covers that item's chat lines for <see cref="ExcludedSeconds"/>.</summary>
    public void ItemExcluded(long id, DateTime at, string why)
    {
        lock (_gate) { _itemExcluded.Add((id, at, why)); _itemExcluded.RemoveAll(x => (at - x.At).TotalSeconds > ExcludedSeconds * 2); }
    }

    /// <summary>For packet-test: what the agreement check decided for each screen pickup it judged, oldest first ("covered", "covered: ...", "first sight", "not shown").</summary>
    public List<string> Verdicts { get; } = new();

    /// <summary>
    /// The agreement check for items and crafts (under _gate). A screen pickup 15 s old or more is covered by a rise of that
    /// item the traffic showed around it (left out: the traffic counted it); during a craft it is the craft's item (left
    /// out); a first sight of that item's stack around it is the pickup the traffic couldn't count, and the screen counts
    /// it after all; another item's rise around it covers it too (a line that misread the name); nothing at all is a miss,
    /// and the screen counts it after all too. Three misses in five minutes and
    /// items go back to the screen. A crafting race the traffic showed nothing of within 12 s is a miss for crafts.
    /// </summary>
    void AgreeItems(DateTime now)
    {
        foreach (var s in _screenItems.Where(s => (now - s.At).TotalSeconds >= 15).ToList())
        {
            _screenItems.Remove(s);
            bool crafting = false;
            try { crafting = CraftingAt?.Invoke(s.At) == true; } catch (Exception) { }
            if (crafting) continue;
            bool Near((long Id, long Delta, bool First, DateTime At) p) => (p.At - s.At).TotalSeconds is >= -ItemBefore and <= ItemAfter;
            // the item's own rise (each covers one line: the latest before it not taken yet); its stack's first sight (the
            // screen's to count, no miss); another item's rise (the line misread its name)
            var own = _itemSeen.Where(p => Near(p) && p.Id == s.Id && !p.First && p.Delta > 0 && !_risesTaken.Contains((p.Id, p.At))).OrderByDescending(p => p.At).ToList();
            if (own.Count > 0) { _risesTaken.Add((own[0].Id, own[0].At)); Verdicts.Add("covered"); continue; }
            // a rise the tracker didn't count (bought, a reward, in a base) before the line: the line is that item's, read
            // now, never a pickup, however late the chat showed it
            if (_itemExcluded.LastOrDefault(x => x.Id == s.Id && (s.At - x.At).TotalSeconds is >= -ItemAfter and <= ExcludedSeconds) is { Why: not null } ex)
            {
                Verdicts.Add($"covered: {ex.Why}");
                Log($"{Local(now)}  the screen read {ItemName(s.Id)} x{s.Count} at {Local(s.At)}: the game's update of it at {Local(ex.At)} wasn't a pickup ({ex.Why}): not counted");
                continue;
            }
            // a rise of the item that already covered a line, a little before this one, and no new rise: the same line read
            // again (the chat covered by a window, then shown once more), not a second pickup
            if (_itemSeen.Any(p => p.Id == s.Id && !p.First && p.Delta > 0 && _risesTaken.Contains((p.Id, p.At)) && (s.At - p.At).TotalSeconds is >= 0 and <= RereadSeconds))
            {
                Verdicts.Add("covered: read again");
                Log($"{Local(now)}  the screen read {ItemName(s.Id)} x{s.Count} at {Local(s.At)} again: its update was taken by an earlier line; not counted");
                continue;
            }
            if (_itemSeen.Any(p => Near(p) && p.Id == s.Id && p.First)) { Verdicts.Add("first sight"); if (s.Late && s.Tag != null) _uncovered.Enqueue(s.Tag); continue; }
            if (_itemSeen.Any(p => Near(p) && !p.First && p.Delta > 0)) { Verdicts.Add("covered: another item"); continue; }
            Verdicts.Add("not shown");
            if (s.Late && s.Tag != null) _uncovered.Enqueue(s.Tag);
            if (!s.Late) continue;   // the loot list alone: no miss, nothing counted
            _itemMisses.Add(now); _itemMisses.RemoveAll(m => (now - m).TotalMinutes > 5);
            Log($"{Local(now)}  the screen read {ItemName(s.Id)} x{s.Count} at {Local(s.At)} that the game's messages didn't show: counted from the screen");
            if (_itemMisses.Count >= 3 && _itemsBroken == null)
            {
                _itemsBroken = "the chat read three item pickups in five minutes that the game's messages didn't show";
                Log($"{Local(now)}  items: {_itemsBroken}; the screen counts them again");
                CrashLog.Note($"packets: {_itemsBroken}; the screen counts them again");
            }
        }
        foreach (var c in _screenCrafts.Where(c => (now - c).TotalSeconds >= 15).ToList())
        {
            _screenCrafts.Remove(c);
            if (_craftSeen.Any(p => Math.Abs((p - c).TotalSeconds) <= 12)) continue;
            _craftMisses.Add(now); _craftMisses.RemoveAll(m => (now - m).TotalMinutes > 5);
            if (_craftMisses.Count >= 3 && _craftsBroken == null)
            {
                _craftsBroken = "the crafting panel finished three crafts in five minutes that the game's messages didn't show";
                Log($"{Local(now)}  crafts: {_craftsBroken}; the panel counts them again");
                CrashLog.Note($"packets: {_craftsBroken}; the panel counts them again");
            }
        }
    }

    void Minute()
    {
        var now = DateTime.UtcNow;
        if ((now - _minuteAt).TotalSeconds < 60) return;
        var from = _minuteAt; _minuteAt = now;
        List<string> parts;
        lock (_gate)
        {
            parts = Enum.GetValues<CurrencyKind>().Select(k =>
            {
                long pk = _seen.Where(p => p.K == k && p.At >= from && p.Delta > 0).Sum(p => p.Delta);
                int n = _seen.Count(p => p.K == k && p.At >= from && p.Delta > 0);
                long sc = _screen.Where(s => s.K == k && s.At >= from).Sum(s => s.Amount);
                return pk == 0 && sc == 0 ? "" : $"{Name(k)} +{pk:N0} in {n} (the screen read +{sc:N0}{(pk == sc ? ": agree" : "")})";
            }).Where(s => s.Length > 0).ToList();
            var rises = _itemSeen.Where(p => p.At >= from && !p.First && p.Delta > 0).ToList();
            if (rises.Count > 0) parts.Add($"items +{rises.Sum(p => p.Delta):N0} in {rises.Count} rises");
        }
        if (!On && parts.Count == 0) return;
        Log($"{Local(now)}  minute: {(parts.Count == 0 ? "nothing picked up" : string.Join("; ", parts))} · {Packets:N0} packets, {DecoderStats.Messages:N0} messages, {_down.Gaps} gaps, {DecoderStats.Resyncs} resyncs");
    }

    static string Name(CurrencyKind k) => k switch { CurrencyKind.Kinah => "Kinah", CurrencyKind.BoundKinah => "bound Kinah", CurrencyKind.RankPoints => "Rank Points", _ => "Abyss Points" };

    /* ---------------- packets.log ---------------- */

    public static string LogPath => Path.Combine(CrashLog.Dir, "packets.log");
    const long LogMax = 2_000_000;
    static readonly object LogGate = new();

    internal static void Log(string line)
    {
        try
        {
            lock (LogGate)
            {
                var path = LogPath;
                var fi = new FileInfo(path);
                if (fi.Exists && fi.Length > LogMax) File.Move(path, Path.Combine(CrashLog.Dir, "packets.old.log"), true);
                File.AppendAllText(path, line + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch (Exception) { /* the log is for checking: never a reason to stop reading */ }
    }

    internal static string Local(DateTime utc) => utc.ToLocalTime().ToString("HH:mm:ss.f");
    static string Signed(long v) => v >= 0 ? $"+{v:N0}" : $"−{-v:N0}";

    public void Dispose() { StopCapture(); _decoder?.Dispose(); _decoder = null; Bosses.Save(); }
}
