using System.Drawing;
using System.Text.Json;
using KinahLedger.Data;
using KinahLedger.Ocr;
using KinahLedger.Upload;

namespace KinahLedger.Session;

/// <summary>
/// One farming run: find the chat (or use a chosen area), read, parse, count, track the zone, and at the end optionally share.
/// The Market reader is not part of a run; it lives in <see cref="GameWatcher"/> and runs whenever the window is open.
/// </summary>
public sealed class TrackerSession
{
    readonly Settings _settings;
    readonly ItemDictionary _items;
    readonly ChatParser _parser;
    readonly LineTracker _tracker = new();
    readonly CancellationTokenSource _cts = new();
    readonly int _interval;
    ScreenReader? _reader;
    Task? _loop;
    DateTime _zoneAt = DateTime.MinValue;

    public SessionEngine Engine { get; private set; }
    /// <summary>
    /// What the window shows unless the player keeps the tables across zones: the same events, started over whenever the
    /// zone changes (and when a run starts). Only shown, never shared; what is shared comes from the other counts, split by zone.
    /// </summary>
    public SessionEngine ZoneView { get; private set; }
    /// <summary>
    /// The Crafting part of the window: the same events, never rotated and never reset by a zone change, so the crafting
    /// ledger runs for as long as the tracker does (or until the player starts it over). Only shown, never shared.
    /// </summary>
    public SessionEngine CraftView { get; private set; }
    /// <summary>Runs in expeditions and events, entry to last loot, kept across restarts.</summary>
    public InstanceLog Instances { get; } = new();
    public void ResetCraftView() => CraftView = new SessionEngine(_items);
    /// <summary>Starts the zone view over in the zone the player is in now.</summary>
    public void ResetZoneView() { var z = new SessionEngine(_items); z.SetZone(Engine.Zone); ZoneView = z; }

    /// <summary>The zone view's stay, offered as the zone's best hour on this server (kept only past ten minutes and a better rate).</summary>
    public void OfferBest() { try { BestRuns.Offer(Server, ZoneView); } catch (Exception ex) { CrashLog.Write("best runs", ex); } }
    /// <summary>The target bar at the top-centre: which enemy is being fought, for Kinah by enemy (a best guess).</summary>
    public TargetReader Target { get; } = new();
    /// <summary>Why the target bar can't be read right now ("the panel sits over it"); empty while it is read.</summary>
    public string TargetNote { get; private set; } = "";
    public string Kind { get; set; }
    /// <summary>A second engine fed the same events: the farming run that sits on top of the always-on reader.</summary>
    public SessionEngine? Secondary { get; set; }
    public Rectangle? Region { get; private set; }
    public bool ManualRegion { get; }
    public string? Zone => Engine.Zone;
    /// <summary>Seconds spent per detected zone, so the run can be attributed to where most of it happened.</summary>
    public Dictionary<string, double> ZoneSeconds => Engine.ZoneSnapshot().Where(kv => kv.Key != SessionEngine.UnknownZone).ToDictionary(kv => kv.Key, kv => kv.Value.Seconds);
    public long Reads { get; private set; }
    public int Unaligned => _tracker.Unaligned;
    public int Empty => _tracker.Empty;
    public string LastError { get; private set; } = "";
    public string LastLine { get; private set; } = "";
    public string DetectNote { get; private set; } = "";
    /// <summary>What the last zone read saw when it found no zone on the list ("top-right text isn't a zone on the list: …"); empty once one is read.</summary>
    public string ZoneNote { get; private set; } = "";
    /// <summary>Why nothing is being read right now ("chrome covers the chat"); empty while reading.</summary>
    public string Paused { get; private set; } = "";
    public bool Running => _loop != null && !_loop.IsCompleted;
    public bool Eligible => Engine.Elapsed.TotalMinutes >= 5 && Engine.Events > 0;
    public Bitmap? Thumb => _reader?.Thumb;
    public Rectangle? GameRect { get; private set; }

    public static readonly Color ChatColor = Color.FromArgb(76, 195, 245), ZoneColor = Color.FromArgb(241, 208, 140);

    /// <summary>The whole game with the chat and map areas outlined, plus any extra marks (the Market table); falls back to the chat crop when the game window isn't known.</summary>
    public Bitmap? Frame(int maxWidth, params (Rectangle box, Color color)[] extra)
    {
        var game = GameRect ?? GameWindow.Find(_settings.ProcessHints);
        if (game == null || Region == null) return Thumb;
        GameRect = game;
        var marks = new List<(Rectangle, Color)> { (Region.Value, ChatColor), (ZoneLocator.Strip(game.Value), ZoneColor) };
        marks.AddRange(extra);
        try { return ScreenReader.Frame(game.Value, maxWidth, marks.ToArray()); }
        catch (Exception) { return Thumb; }
    }

    /// <summary>The kind plus the zone most of the run happened in, e.g. "Open world · Western Cantas Valley".</summary>
    public string Activity => ActivityOf(Engine, Kind);

    public static string ActivityOf(SessionEngine e, string kind)
    {
        // the label is shared with the run, so only a zone the tracker is sure of goes into it
        var zs = e.ZoneSnapshot().Where(kv => kv.Key != SessionEngine.UnknownZone && ZoneLocator.IsVerified(kv.Key)).ToList();
        var z = zs.Count > 0 ? zs.MaxBy(kv => kv.Value.Seconds).Key : (e.Zone is { } cur && ZoneLocator.IsVerified(cur) ? cur : null);
        return z != null ? $"{kind} · {z}" : kind;
    }

    /// <summary>Starts a fresh engine for the always-on reader and hands back the finished one, with the zone carried over.</summary>
    public SessionEngine Rotate()
    {
        var old = Engine;
        var fresh = new SessionEngine(_items);
        fresh.SetZone(old.Zone);
        Engine = fresh;
        old.Finish();
        return old;
    }

    public event Action<string>? LootLine;
    public event Action? Updated;
    /// <summary>Every event read from the chat, as it is counted: the Market reader takes the player's own sales from here.</summary>
    public event Action<GameEvent>? Parsed;

    long _kinahPicked;
    /// <summary>All tradeable Kina picked up since the reader started, across chunks: what the balance counter is checked against.</summary>
    public long KinahPicked => Interlocked.Read(ref _kinahPicked);
    long _kinahBound;
    /// <summary>The pace of reading while fighting, and how long after the last loot it lasts (measured 2026-10-22: a chat and loot list pass takes about 260 ms).</summary>
    public const int FightIntervalMs = 300;
    public const double FightSeconds = 10;
    DateTime _lastActivity = DateTime.MinValue;

    // ---- the balance counter as a check on the count ----
    readonly BalanceCheck _balanceCheck = new();
    public BalanceCheck BalanceCheck => _balanceCheck;
    /// <summary>
    /// The game's Kinah counter, read every few seconds, against the Kinah counted. A rise the count lacks, of exactly an
    /// amount the loot list showed in one read only, is that pickup: two sources agree, and it is counted. Any other gap
    /// is written to reads.log and left alone, since a sale, mail or purchase moves the counter too.
    /// </summary>
    public void CheckBalance(long balance, DateTime at)
    {
        long gap = _balanceCheck.Observe(balance, KinahPicked + Interlocked.Read(ref _kinahBound), at);
        if (gap == 0) return;
        if (gap > 0 && gap <= BalanceCheck.MaxPickupGap && _cross.TakeSingleKinah(gap, at))
        {
            var ev = new KinahChanged(at, gap, false) { Via = "loot list · balance" };
            Engine.Apply(ev); Secondary?.Apply(ev); ZoneView.Apply(ev); Instances.Loot(ev); Feed(ev);
            Interlocked.Add(ref _kinahPicked, gap);
            _balanceCheck.Observe(balance, KinahPicked + Interlocked.Read(ref _kinahBound), at);   // the count matches the counter again
            CrashLog.Note($"the Kinah counter rose {gap:N0} more than counted, and the loot list showed {gap:N0} Kina once: counted from the two");
            BalanceAdded++;
            try { Parsed?.Invoke(ev); } catch (Exception ex) { CrashLog.Write("parsed event", ex); }
            return;
        }
        CrashLog.Note(gap > 0
            ? $"the Kinah counter rose {gap:N0} more than counted (a missed pickup, or a sale or mail the chat didn't show); not counted"
            : $"the Kinah counter rose {-gap:N0} less than counted (Kinah spent, or a pickup counted twice)");
    }
    public int BalanceAdded { get; private set; }

    public TrackerSession(Rectangle? manualRegion, Settings s, ItemDictionary items, string kind, int? intervalMs = null)
    {
        _settings = s;
        _items = items;
        _parser = new ChatParser(items);
        // a partly hidden name is guessed among this run's items and the current enemy's loot table first
        _parser.Likely = () => Engine.ItemRows().Select(r => r.Name).Concat(Target.Name is { } tn ? LootBook.Current.TableOf(tn).Select(e => e.Item) : Enumerable.Empty<string>()).ToList();
        Engine = new SessionEngine(items);
        ZoneView = new SessionEngine(items);
        _cross = new LootCrossCheck(items);
        CraftView = new SessionEngine(items);
        Kind = kind;
        Region = manualRegion;
        ManualRegion = manualRegion != null;
        _interval = Math.Clamp(intervalMs ?? s.Ocr.IntervalMs, 250, 5000);
    }

    /// <summary>Locates the chat when no area was chosen, reads the zone once, and starts the loop. Throws with a plain message when the chat can't be found.</summary>
    public async Task StartAsync()
    {
        var game = GameWindow.Find(_settings.ProcessHints);
        GameRect = game;
        if (Region == null)
        {
            if (game == null) throw new InvalidOperationException("The game window wasn't found. Start the game (borderless or windowed), or choose the chat yourself.");
            if (GameWindow.Covered(ChatLocator.Corner(game.Value), _settings.ProcessHints) is { } cover)
                throw new InvalidOperationException($"The chat corner of the game is out of view ({cover} is over it). Bring the game to the front, or move that window off the bottom-left of the game.");
            var (region, note) = await ChatLocator.DetectAsync(game.Value);
            DetectNote = note;
            if (region == null) throw new InvalidOperationException("Couldn't see the chat. Open the chat so its input box shows (press Enter once, or pin the chat window in the game's chat settings), then Detect again, or choose the chat yourself.");
            Region = region;
            _anchor = game;
        }
        _reader = new ScreenReader(Region.Value, _settings.Ocr.Scale);
        if (game != null && GameWindow.Covered(ZoneLocator.Strip(game.Value), _settings.ProcessHints) == null) await ReadZone(game.Value);
        _loop ??= Task.Run(() => Loop(_cts.Token));
    }

    string? _pendingZone; string? _unlistedTitle;

    /// <summary>Two reads of the same title, allowing the recognizer a letter or two.</summary>
    static bool Close(string a, string b)
    {
        a = a.ToLowerInvariant(); b = b.ToLowerInvariant();
        if (a == b) return true;
        int max = Math.Max(1, Math.Min(a.Length, b.Length) / 6);
        if (Math.Abs(a.Length - b.Length) > max) return false;
        var prev = new int[b.Length + 1]; var cur = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++) prev[j] = j;
        for (int i = 1; i <= a.Length; i++)
        {
            cur[0] = i;
            for (int j = 1; j <= b.Length; j++) cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            (prev, cur) = (cur, prev);
        }
        return prev[b.Length] <= max;
    } DateTime _zoneNoteAt = DateTime.MinValue;

    async Task ReadZone(Rectangle game)
    {
        try
        {
            var (zone, note) = await ZoneLocator.DetectAsync(game);
            ZoneNote = zone == null ? note : "";
            if (zone != null && !ZoneLocator.IsVerified(zone))
            {
                // an unfamiliar label becomes the zone only when two reads in a row agree; a stray read (a button, a tooltip) keeps the last zone
                if (!string.Equals(_pendingZone, zone, StringComparison.Ordinal)) { _pendingZone = zone; zone = null; }
            }
            else _pendingZone = null;
            // the minimap names a place on no list, twice in a row (an expedition's inner area, an event area): from now on
            // the count goes under Unknown zone, never under the last zone that was read (Steven, 2026-10-21: refining
            // stones from an expedition landed on Eastern Cantas Valley)
            if (zone == null && ZoneLocator.LastUnlisted is { } u)
            {
                if (_unlistedTitle != null && Close(_unlistedTitle, u) && Engine.Zone != SessionEngine.UnknownZone)
                {
                    zone = SessionEngine.UnknownZone;
                    CrashLog.Note($"zone: \"{u}\" is on no list, so this counts under Unknown zone, not under {Engine.Zone ?? "the last zone"}");
                }
                _unlistedTitle = u;
            }
            else if (zone != null) _unlistedTitle = null;
            if (zone != null && zone != Engine.Zone && zone != SessionEngine.UnknownZone) CrashLog.Note($"zone: {zone}" + (ZoneLocator.KindOf(zone) is { } kind ? $" ({kind})" : ""));
            else if (zone == null && Engine.Zone == null && (DateTime.UtcNow - _zoneNoteAt).TotalSeconds >= 60) { _zoneNoteAt = DateTime.UtcNow; CrashLog.Note($"zone read: {note}"); }
            Engine.SetZone(zone);
            Secondary?.SetZone(zone);
            Instances.Zone(zone, DateTime.UtcNow);
            // a new zone starts the zone view over: its tables are this zone's alone
            if (zone != null && ZoneView.Zone != null && zone != ZoneView.Zone) { OfferBest(); var fresh = new SessionEngine(_items); fresh.SetZone(zone); ZoneView = fresh; }
            else ZoneView.SetZone(zone);
        }
        catch (Exception ex) { CrashLog.Write("zone", ex); }
        _zoneAt = DateTime.UtcNow;
    }

    int _emptyReads;

    // the game window as it was when the chat area was found (auto mode): the area is kept relative to it, so the game
    // moving (a tab out and back, a drag) moves the area with it, and the game changing size (fullscreen to windowed,
    // a resolution change) finds the chat again at once instead of after twenty empty reads
    Rectangle? _anchor;
    DateTime _refindAt = DateTime.MinValue;

    /// <summary>Keeps the chat area on the game: shifted when the game moved, found again when the game changed size.</summary>
    async Task FollowGameAsync()
    {
        if (ManualRegion || _anchor is not { } a) return;
        var game = GameWindow.Find(_settings.ProcessHints);
        if (game is not { } g) return;   // minimized or gone: the cover check pauses, and the area waits where it was
        GameRect = g;
        if (g == a) return;
        if (g.Size == a.Size)
        {
            // same window, new place: the chat sits where it did inside it
            var moved = Region!.Value; moved.Offset(g.X - a.X, g.Y - a.Y);
            Region = moved; _reader = new ScreenReader(moved, _settings.Ocr.Scale); _anchor = g;
            DetectNote = "the chat area moved with the game";
            return;
        }
        if ((DateTime.UtcNow - _refindAt).TotalSeconds < 5 || GameWindow.Covered(ChatLocator.Corner(g), _settings.ProcessHints) != null) return;
        _refindAt = DateTime.UtcNow;
        var (again, note) = await ChatLocator.DetectAsync(g);
        if (again == null) return;   // the chat may be faded: the empty-read rule keeps trying
        Region = again; _reader = new ScreenReader(again.Value, _settings.Ocr.Scale); _anchor = g;
        _tracker.Rebase();
        DetectNote = "re-found after the game changed size: " + note;
    }

    async Task Loop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var t0 = DateTime.UtcNow;
            try
            {
                await FollowGameAsync();
                // only the game is read: while it is minimized or another window sits over the chat, nothing is read and the clocks stop
                if (GameWindow.Covered(Region!.Value, _settings.ProcessHints) is { } cover)
                {
                    Engine.Pause(); Secondary?.Pause(); ZoneView.Pause();
                    Paused = cover.StartsWith("the game") ? cover : cover + " covers the chat";
                    LastError = "";
                }
                else
                {
                    // the loot list is read beside the chat, not after it: a pass takes the slower of the two
                    var p0 = DateTime.UtcNow;
                    var list = LootReadAsync();
                    await ReadOnce();
                    _chatMs += (DateTime.UtcNow - p0).TotalMilliseconds;
                    await TargetDueAsync();
                    LootProcess(await list);
                    RewardShot();
                    _passes++; _passMs += (DateTime.UtcNow - p0).TotalMilliseconds;
                    Audit();
                }
                await ZoneDueAsync();
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // the screen can't be copied: the session is locked, a secure prompt is up, or a display is switching. Wait, like a cover.
                Engine.Pause(); Secondary?.Pause(); ZoneView.Pause();
                Paused = "the screen can't be read right now (locked, or a system prompt is up)";
                LastError = "";
            }
            catch (Exception ex) { LastError = ex.Message; CrashLog.Write("read loop", ex); }
            Updated?.Invoke();

            int spent = (int)(DateTime.UtcNow - t0).TotalMilliseconds;
            // during a fight (loot, a kill or a loot list row in the last FightSeconds) about three reads a second, so a line
            // or a row up for a moment is read more than once; idle, the usual pace, since back-to-back reading costs a CPU core
            int every = (DateTime.UtcNow - _lastActivity).TotalSeconds <= FightSeconds ? Math.Min(_interval, FightIntervalMs) : _interval;
            if (spent < every) { try { await Task.Delay(every - spent, ct); } catch (TaskCanceledException) { } }
        }
        Engine.Finish();
    }

    /// <summary>One read of the chat (the game is in view there): count the new lines, re-find a lost chat, re-read the zone when due.</summary>
    async Task ReadOnce()
    {
        // back in view: lines that arrived while it was covered weren't seen, so the first read is history and the clocks restart
        if (Paused.Length > 0) { Paused = ""; _tracker.Rebase(); }
        Engine.Resume(); Secondary?.Resume(); ZoneView.Resume();
        var lines = await _reader!.ReadAsync();
        if (lines.Count > 0) { ReadTrace.Chat(DateTime.UtcNow, lines); QuestCounter(lines); }
        Reads++;
        // the chat moved or the UI was toggled: after a stretch of empty reads, find it again (auto mode only)
        _emptyReads = lines.Count == 0 ? _emptyReads + 1 : 0;
        if (!ManualRegion && _emptyReads >= 20)
        {
            _emptyReads = 0;
            var g = GameWindow.Find(_settings.ProcessHints);
            if (g != null && GameWindow.Covered(ChatLocator.Corner(g.Value), _settings.ProcessHints) == null)
            {
                var (again, note) = await ChatLocator.DetectAsync(g.Value);
                if (again != null && again != Region) { Region = again; _reader = new ScreenReader(again.Value, _settings.Ocr.Scale); _anchor = g; DetectNote = "re-found: " + note; }
            }
        }
        ProcessChat(lines, DateTime.UtcNow);
    }

    /// <summary>One read of the chat, counted: lined up with the last, parsed, checked against the loot list and applied. Also what replay feeds a recorded fight through.</summary>
    internal void ProcessChat(List<string> lines, DateTime now)
    {
        int unaligned = _tracker.Unaligned;
        var fresh = _tracker.NewLines(lines);
        bool afterFade = _tracker.LastAfterFade;
        // a read that couldn't be lined up with the last one counts nothing; the log says so, so a missed line can be traced
        if (_tracker.LastFromAnchor) CrashLog.Note($"the last read was a stray line, so this one was lined up with the last full read of the chat ({fresh.Count} new)");
        if (_tracker.Unaligned > unaligned)
        {
            CrashLog.Note($"couldn't line this read up with the last one, so nothing in it was counted ({lines.Count} lines; last: {lines[^1]})");
            // both reads in full, so a lost burst of kills can be traced line by line
            CrashLog.Note("   before: " + string.Join("  |  ", _prevRead));
            CrashLog.Note("   now:    " + string.Join("  |  ", lines));
        }
        _prevRead = lines;
        // the enemy on the target bar, as last read, goes with this read's events (Kinah by enemy, a best guess)
        if (fresh.Count > 0) { Engine.SetTarget(Target.Name, Target.NameAt); Secondary?.SetTarget(Target.Name, Target.NameAt); ZoneView.SetTarget(Target.Name, Target.NameAt); }
        // parsed as one batch: a payment line claims the Kinah lines just before it in the same read
        // a Daeva Pass, shop or reward window open in the band: what lands now is a reward, not farming
        bool reward = Target.RewardOpen(now);
        // the crafting window: its skill tabs in the band, or its own "Show acquired designs" box over the chat
        if (lines.Any(l => l.Contains("acquired design", StringComparison.OrdinalIgnoreCase))) Target.MarkCraft(now);
        foreach (var e in new[] { Engine, Secondary, ZoneView, CraftView }) e?.SetCraftWindow(Target.CraftAt);
        bool crafting = (now - Target.CraftAt).TotalSeconds <= TargetReader.CraftGraceSeconds;
        // loot lines that were read garbled while they faded in, now read cleanly at the same place: one more parse each
        var reread = _reread.Take(lines, fresh.Count, now);
        // more loot than the chat can show at once (an expedition's rewards, a box opened): the read shares no line with
        // the last one, so nothing lines up. Its loot lines unseen in the last ten minutes are new, and are counted
        if (_tracker.Unaligned > unaligned)
        {
            var burst = Reread.Burst(_lastLines, lines, _recent, now);
            if (burst.Count > 0) { CrashLog.Note($"a burst of {burst.Count} loot line(s) arrived faster than the chat could show; counted from the read anyway"); reread.AddRange(burst); }
            else
            {
                // it shares lines with the last read but no run of them lined up: what follows the last shared line is new
                var after = Reread.AfterOverlap(_lastLines, lines);
                if (after.Count > 0) { CrashLog.Note($"counted {after.Count} loot line(s) below the last line this read shares with the last one: {string.Join(" | ", after)}"); reread.AddRange(after); }
            }
        }
        Reread.Remember(_recent, lines, now);
        _lastLines = lines;
        bool Apply(IEnumerable<GameEvent> events, string via = "chat")
        {
            bool any = false;
            foreach (var p0 in events)
            {
                var parsed = p0 with { Via = p0.Via.Length > 0 ? via + " · " + p0.Via : via };
                // the loot list beside the character already added this pickup (this chat line came late): it counts once
                if (_cross.Chat(parsed, now)) { any = true; continue; }
                // with the crafting window open, an item a recipe makes is a craft, never a reward or a drop
                bool made = crafting && parsed is ItemAcquired ia && RecipeBook.ForOutput(_items.NameOf(ia.ItemId)) != null;
                var ev = reward && !made ? _parser.AsReward(parsed) : parsed;
                Engine.Apply(ev); Secondary?.Apply(ev); ZoneView.Apply(ev); Instances.Loot(ev); any = true; _lastActivity = DateTime.UtcNow;
                Feed(ev);
                if (ev is CraftResult or ItemAcquired) CraftView.Apply(ev);
                if (ev is KinahChanged { Bound: false } kc) Interlocked.Add(ref _kinahPicked, kc.Delta);
                else if (ev is KinahChanged { Bound: true, Delta: > 0 } kb) Interlocked.Add(ref _kinahBound, kb.Delta);
                try { Parsed?.Invoke(ev); } catch (Exception ex) { CrashLog.Write("parsed event", ex); }
            }
            return any;
        }
        int at = 0;
        foreach (var (l, events) in _parser.ParseBatch(fresh, now))
        {
            bool any = Apply(events);
            CrashLog.Read(l, any, afterFade);
            if (any) { LastLine = l; LootLine?.Invoke(l); }
            // a loot-shaped line that matched nothing was probably still fading in: look at its place again next read
            else if (Reread.LooksLikeLoot(l)) _reread.Remember(l, fresh.Count - 1 - at, now);
            at++;
        }
        foreach (var l in reread)
        {
            bool any = false;
            foreach (var (_, events) in _parser.ParseBatch(new List<string> { l }, now, wrap: false)) any |= Apply(events, "chat · recovered");
            CrashLog.Read(l + "    (read again: it was garbled while fading in)", any);
            if (any) { LastLine = l; LootLine?.Invoke(l); }
        }
        LastError = "";
    }

    DateTime _targetAt = DateTime.MinValue;

    // ---- the audit: how the count holds up, once a minute in reads.log ----
    // The quest tracker's "Defeat … (n/20)" lines count kills of quest enemies exactly, so where every enemy fought is a
    // quest enemy, the kills counted should rise as fast as the counter. Pass times say how often the screen is read.
    int _passes; double _passMs, _chatMs; DateTime _auditAt = DateTime.UtcNow; int _auditKills = -1, _questSteps, _auditAdded, _auditDups;
    readonly Dictionary<int, int> _questMax = new();
    /// <summary>Steps of the quest tracker's kill counters seen since the start: kills of quest enemies, counted by the game.</summary>
    public int QuestStepsTotal { get; private set; }
    internal void QuestCounter(IReadOnlyList<string> lines)
    {
        var best = new Dictionary<int, int>();
        for (int i = 0; i < lines.Count; i++)
        {
            var m = System.Text.RegularExpressions.Regex.Match(lines[i], @"\((\d{1,3})\s*/\s*(\d{1,3})\)\s*$");
            if (!m.Success) continue;
            // the counter line, or its wrapped half, follows a "Defeat …" line
            bool quest = lines[i].Contains("Defeat", StringComparison.OrdinalIgnoreCase) || (i > 0 && lines[i - 1].Contains("Defeat", StringComparison.OrdinalIgnoreCase));
            if (!quest) continue;
            int n = int.Parse(m.Groups[1].Value), of = int.Parse(m.Groups[2].Value);
            if (n > of || of < 2) continue;
            best[of] = Math.Max(best.GetValueOrDefault(of), n);
        }
        foreach (var (of, n) in best)
        {
            if (_questMax.TryGetValue(of, out var was) && n > was && n - was <= 5) { _questSteps += n - was; QuestStepsTotal += n - was; }
            if (!_questMax.ContainsKey(of) || n > was || n < was - 3) _questMax[of] = n;
        }
    }
    void Audit()
    {
        var now = DateTime.UtcNow;
        if (_auditKills < 0) { _auditKills = Engine.Totals().Kills; _auditAdded = _cross.Added; _auditDups = _cross.Duplicates; }
        if ((now - _auditAt).TotalSeconds < 60) return;
        int kills = Engine.Totals().Kills;
        if (_passes > 0 && (kills > _auditKills || _questSteps > 0))
            CrashLog.Note($"audit: last {(now - _auditAt).TotalSeconds:0} s · kills counted +{kills - _auditKills} · quest counter +{_questSteps} · from the loot list +{_cross.Added - _auditAdded} · chat repeats skipped {_cross.Duplicates - _auditDups} · Kinah counter agreed {_balanceCheck.Agreed}×, over {_balanceCheck.Over:N0}, under {_balanceCheck.Under:N0} so far · {_passes} passes, {_passMs / _passes:0} ms each (chat {_chatMs / _passes:0} ms)");
        _auditAt = now; _auditKills = kills; _questSteps = 0; _passes = 0; _passMs = 0; _chatMs = 0; _auditAdded = _cross.Added; _auditDups = _cross.Duplicates;
    }

    /// <summary>One pickup as counted, for the recent pickups strip: when, what, and how it was read.</summary>
    public sealed record Pickup(DateTime At, string What, string Via, bool Unconfirmed);
    readonly List<Pickup> _pickups = new();
    /// <summary>The last pickups counted, newest first: items and Kinah only, rewards and bound Kinah left out.</summary>
    public List<Pickup> RecentPickups(int n) { lock (_pickups) return _pickups.TakeLast(n).Reverse().ToList(); }
    void Feed(GameEvent ev)
    {
        string? what = ev switch
        {
            ItemAcquired a => $"{_items.NameOf(a.ItemId)} ×{a.Count}" + (a.Bound ? " (bound)" : ""),
            KinahChanged { Bound: false, Delta: > 0 } k => $"{k.Delta:N0} Kinah",
            _ => null,
        };
        if (what == null) return;
        lock (_pickups) { _pickups.Add(new Pickup(ev.Time, what, ev.Via, ev.Unconfirmed)); if (_pickups.Count > 50) _pickups.RemoveRange(0, _pickups.Count - 50); }
    }

    /// <summary>The chat and the loot list beside the character, checked against each other: see <see cref="LootCrossCheck"/>.</summary>
    readonly LootCrossCheck _cross;
    public LootCrossCheck CrossCheck => _cross;

    /// <summary>
    /// One read of the loot list beside the character, every pass while the game is in view. Its rows wait for the chat;
    /// a row the chat never matched is counted from the list, through the same rules as a chat line (reward screens too).
    /// </summary>
    /// <summary>One read of the loot list beside the character, started before the chat read so the two run side by side. Null when it can't be read.</summary>
    async Task<List<OcrLineBox>?> LootReadAsync()
    {
        if (GameRect is not { } g) return null;
        var region = LootPopup.Region(g);
        try
        {
            if (GameWindow.Covered(region, _settings.ProcessHints) != null) return null;   // something over it: nothing read, nothing added
            return await ScreenReader.ReadBoxesAsync(region, 3, boost: true);
        }
        catch (Exception ex) { CrashLog.Write("loot list", ex); return null; }
    }

    void LootProcess(List<OcrLineBox>? lines)
    {
        if (lines != null) ProcessList(lines.Select(l => (l.Box.Top, l.Text)).ToList(), DateTime.UtcNow);
    }

    /// <summary>One read of the loot list, checked against the chat. Also what replay feeds a recorded fight through.</summary>
    internal void ProcessList(List<(int Top, string Text)> lines, DateTime now)
    {
        try
        {
            // a shop or reward screen covers the place the loot list shows: nothing there is loot
            if (Target.RewardOpen(now)) return;
            var rows = LootPopup.Rows(lines);
            if (rows.Count > 0) _lastActivity = DateTime.UtcNow;
            if (rows.Count > 0) ReadTrace.List(now, rows.Select(r => r.IsKinah ? $"{r.Amount} Kina" : $"{r.Name} x{r.Count}"));
            var add = _cross.Popup(rows, now);
            if (add.Count == 0) return;
            bool reward = Target.RewardOpen(now);
            foreach (var e in new[] { Engine, Secondary, ZoneView, CraftView }) e?.SetCraftWindow(Target.CraftAt);
            Engine.SetTarget(Target.Name, Target.NameAt); Secondary?.SetTarget(Target.Name, Target.NameAt); ZoneView.SetTarget(Target.Name, Target.NameAt);
            bool crafting = (now - Target.CraftAt).TotalSeconds <= TargetReader.CraftGraceSeconds;
            foreach (var p0 in add)
            {
                var parsed = p0 with { Via = "loot list" };
                bool made = crafting && parsed is ItemAcquired ia && RecipeBook.ForOutput(_items.NameOf(ia.ItemId)) != null;
                var ev = reward && !made ? _parser.AsReward(parsed) : parsed;
                Engine.Apply(ev); Secondary?.Apply(ev); ZoneView.Apply(ev); Instances.Loot(ev);
                Feed(ev);
                if (ev is ItemAcquired) CraftView.Apply(ev);
                if (ev is KinahChanged { Bound: false } kc) Interlocked.Add(ref _kinahPicked, kc.Delta);
                else if (ev is KinahChanged { Bound: true, Delta: > 0 } kb) Interlocked.Add(ref _kinahBound, kb.Delta);
                try { Parsed?.Invoke(ev); } catch (Exception ex) { CrashLog.Write("parsed event", ex); }
            }
        }
        catch (Exception ex) { CrashLog.Write("loot list", ex); }
    }
    /// <summary>The previous read as it came, for the log when a read can't be lined up with it.</summary>
    List<string> _prevRead = new();
    readonly Reread _reread = new();
    // every line of the last ten minutes of reads, for telling a burst's new lines from history
    readonly Dictionary<string, DateTime> _recent = new();
    List<string> _lastLines = new();

    /// <summary>
    /// The target bar every 2 seconds while the chat is read. Unlike the minimap, the panel isn't hidden for it: a read every
    /// 2 seconds would make it flicker. It is read through instead, and the window asks to move it only when nothing is read.
    /// </summary>
    // ---- reward screens, kept as pictures ----
    // Events and expeditions pay out on their own screens (cards picked at the end of an event, the big reward table at
    // the end of an expedition), not in the chat (Steven, 2026-10-22). To learn to read them, a picture of the game window
    // is kept each time one opens: a few seconds apart while it stays open, on this PC only, in reward-shots beside the
    // logs, at most RewardShotsPerDay a day. Inside an event or expedition a picture every 15 s too, since a card pick may
    // show no title to recognise. Nothing else on screen is captured, nothing is sent anywhere, and it only happens when the
    // reward-shots folder exists (made by hand on a PC that helps build the reader; never by the app).
    public const int RewardShotsPerDay = 200;
    DateTime _rewardShotAt = DateTime.MinValue; int _rewardShotsOpen;
    void RewardShot()
    {
        try
        {
            var now = DateTime.UtcNow;
            var dir = Path.Combine(CrashLog.Dir, "reward-shots");
            if (GameRect is not { } g || !Directory.Exists(dir)) return;
            bool reward = Target.RewardOpen(now), inside = ZoneLocator.KindOf(Zone) is "Event" or "Expedition";
            // fights too, every 30 s: the nameplates and health bars around the character, to learn which enemies are engaged
            bool fighting = (now - _lastActivity).TotalSeconds <= FightSeconds;
            if (!reward) _rewardShotsOpen = 0;
            bool due = reward ? _rewardShotsOpen < 6 && (now - _rewardShotAt).TotalSeconds >= 2
                     : inside ? (now - _rewardShotAt).TotalSeconds >= 15
                     : fighting && (now - _rewardShotAt).TotalSeconds >= 30;
            if (!due) return;
            var today = DateTime.Now.ToString("yyyyMMdd");
            if (Directory.GetFiles(dir, today + "-*.jpg").Length >= RewardShotsPerDay) return;
            if (GameWindow.Covered(g, _settings.ProcessHints) is { } cover && !cover.StartsWith("Kinah Ledger")) return;   // only the game, never a window over it
            _rewardShotAt = now; if (reward) _rewardShotsOpen++;
            var title = !reward ? (inside ? "in-run" : "fight") : new string(Target.RewardTitle.Where(c => char.IsLetterOrDigit(c) || c == ' ').ToArray()).Trim().Replace(' ', '-');
            var zoneName = new string((Zone ?? "").Where(c => char.IsLetterOrDigit(c) || c == ' ').ToArray()).Trim().Replace(' ', '-');
            using var bmp = ScreenReader.Capture(g);
            var jpg = System.Drawing.Imaging.ImageCodecInfo.GetImageEncoders().First(e => e.FormatID == System.Drawing.Imaging.ImageFormat.Jpeg.Guid);
            using var ps = new System.Drawing.Imaging.EncoderParameters(1);
            ps.Param[0] = new System.Drawing.Imaging.EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 85L);
            var file = Path.Combine(dir, $"{DateTime.Now:yyyyMMdd-HHmmss}-{zoneName}-{title}.jpg");
            bmp.Save(file, jpg, ps);
            if (_rewardShotsOpen == 1) CrashLog.Note($"reward screen kept as a picture: {Path.GetFileName(file)}");
        }
        catch (Exception ex) { CrashLog.Write("reward picture", ex); }
    }

    async Task TargetDueAsync()
    {
        if ((DateTime.UtcNow - _targetAt).TotalSeconds < 1) return;   // every second: a fast kill is over in two or three
        _targetAt = DateTime.UtcNow;
        if (GameRect is not { } g) return;
        try
        {
            var cover = GameWindow.Covered(TargetReader.Strip(g), _settings.ProcessHints);
            bool own = cover != null && cover.StartsWith("Kinah Ledger");
            // the panel (or this window) over part of the band is read through: its own labels are never on the enemy list.
            // Only when no enemy has been read for a minute does the window ask to drag it aside.
            if (cover == null || own)
            {
                await Target.TickAsync(g);
                TargetNote = own && (Target.Name == null || (DateTime.UtcNow - Target.NameAt).TotalSeconds > 60) ? "the panel sits over the target bar; drag it aside if enemies don't show" : "";
            }
            else TargetNote = cover!.StartsWith("the game") ? "" : cover + " covers the target bar";
        }
        catch (Exception ex) { CrashLog.Write("target bar", ex); }
    }

    /// <summary>
    /// The zone on its own clock, whether or not the chat can be read: every ZoneEverySec while a zone is known, every
    /// 30 seconds while none is ("not read yet"), as long as nothing covers the minimap corner.
    /// </summary>
    async Task ZoneDueAsync()
    {
        int every = Engine.Zone == null ? 5 : _settings.Ocr.ZoneEverySec;   // no zone yet (a menu, a loading screen): try again every 5 s, as long as it takes
        if ((DateTime.UtcNow - _zoneAt).TotalSeconds < every) return;
        var game = GameWindow.Find(_settings.ProcessHints);
        GameRect = game ?? GameRect;
        if (game is not { } g) { _zoneAt = DateTime.UtcNow; return; }
        // the panel over the game steps aside for the read when it sits on the minimap corner; the minimap then has its own
        // cover check: another window over it alone keeps the last zone and lets the chat be read
        await Ui.HudWindow.HiddenIfOver(ZoneLocator.Strip(g), async () =>
        {
            if (GameWindow.Covered(ZoneLocator.Strip(g), _settings.ProcessHints) == null) await ReadZone(g);
            else _zoneAt = DateTime.UtcNow;
        });
    }

    public async Task StopAsync()
    {
        OfferBest();   // the window closing ends the stay in this zone too
        Instances.Close();
        _cts.Cancel();
        if (_loop != null) { try { await _loop; } catch (Exception) { /* loop ended */ } }
        Engine.Finish();
    }

    /// <summary>The player's region key and server label ("na_e", "Siel (Elyos)"), sent with the run so the site can keep drops per server.</summary>
    public string RegionKey { get; set; } = "";
    public string Server { get; set; } = "";

    public Task<bool> UploadAsync(Settings s) => UploadAsync(s, Engine, Kind, Activity);

    /// <summary>
    /// Shares one engine's totals (a farming run, or a chunk of the always-on count) under the given kind, with the
    /// expedition and event runs that finished meanwhile. Under five minutes or empty: nothing is sent. A send that
    /// doesn't reach the site puts the runs back for the next one; with sharing off they are let go, never sent later.
    /// </summary>
    public async Task<bool> UploadAsync(Settings s, SessionEngine e, string kind, string activity)
    {
        if (e.Elapsed.TotalMinutes < 5 || e.Events == 0) return false;
        var runs = Instances.TakeUnshared(e.Started);
        bool ok = await Uploader.SendAsync(RunPayload.Build(e, s, kind, activity, RegionKey.Length > 0 ? RegionKey : s.Region, Server, _items, runs), s.Upload);
        if (!ok && s.Upload.Enabled && !string.IsNullOrWhiteSpace(s.Upload.Endpoint)) Instances.Restore(runs);
        return ok;
    }
}

/// <summary>What the window remembers between runs, kept per user.</summary>
public sealed class UserPrefs
{
    public string Activity { get; set; } = "Open world";
    public string Place { get; set; } = "";
    public bool Hud { get; set; } = true;
    /// <summary>How solid the panel over the game is, 0.3 to 1.</summary>
    public double HudOpacity { get; set; } = 0.9;
    public bool Share { get; set; } = true;
    /// <summary>True once the player chose the chat by hand; auto-detection then stays off until they switch back.</summary>
    public bool ManualChat { get; set; }
    /// <summary>No longer used: the window is always the live count, with setup in Options. Kept so older ui.json files load.</summary>
    public bool Compact { get; set; }
    /// <summary>The Drops and Gathered sections both show at once; each folds to its header on a click, and stays folded.</summary>
    public bool DropsOpen { get; set; } = true;
    public bool GatherOpen { get; set; } = true;
    public bool MobsOpen { get; set; } = true;
    /// <summary>Soul Crystals sit folded at the bottom until opened.</summary>
    public bool PetsOpen { get; set; }
    /// <summary>Off (the default): the tables start over when the zone changes. On: they keep counting across zones. What is shared is split by zone either way.</summary>
    public bool AllZones { get; set; }
    /// <summary>The Odds column as a percentage ("4.9%") instead of "1 in 21"; a click on its header switches.</summary>
    public bool OddsAsPercent { get; set; }
    /// <summary>Which part of the window shows: "Farming" (the run, the stats, the tables) or "Crafting" (recipes, cost, success, profit).</summary>
    public string Part { get; set; } = "Farming";
    /// <summary>Market prices: read and share the listings while the Market is open. On by default; off keeps everything else working.</summary>
    public bool ShareMarket { get; set; } = true;
    /// <summary>True once the player chose the Market area by hand; the title-and-headers search then stays off until they switch back.</summary>
    public bool ManualMarket { get; set; }
    /// <summary>The chosen server's label ("Siel (Elyos)"), sent with prices and runs so they land on the right board. Nothing is shared without it.</summary>
    public string Server { get; set; } = "";
    /// <summary>The chosen server's region key ("na_e"); empty means settings.json's region.</summary>
    public string Region { get; set; } = "";
    /// <summary>True once the first-run screen (region, faction, server, look) has been completed.</summary>
    public bool Welcomed { get; set; }
    /// <summary>The look: night, abyss or daylight.</summary>
    public string Skin { get; set; } = "night";
    /// <summary>Install a newer version on its own (downloaded in the background, swapped in when the window closes). Off: the window asks first.</summary>
    public bool AutoUpdate { get; set; } = true;
    /// <summary>The version that last opened the window, so the first start after an update can say so.</summary>
    public string LastVersion { get; set; } = "";
    /// <summary>When the game starts while the tracker waits in the tray: "open" shows the window, "tray" stays in the tray. Reading starts either way.</summary>
    public string OnGameStart { get; set; } = "open";
    /// <summary>Hide to the tray once the game has been closed for a few seconds (never during a run).</summary>
    public bool HideWhenGameCloses { get; set; }
    /// <summary>The window's close button hides it to the tray; Quit in the tray menu ends the tracker. On for new installs since 0.7.6, so a habit close doesn't stop the count.</summary>
    public bool CloseToTray { get; set; } = true;
    /// <summary>Minimizing always hides to the tray, not only during a run. On for new installs since 0.7.6.</summary>
    public bool MinimizeToTray { get; set; } = true;
    /// <summary>What the panel over the game shows, in this order: time, rate, kinah, loot, total, best, last (HudFields.All). The player picks in Options › In game.</summary>
    public string HudFields { get; set; } = Ui.HudFields.Default;
    /// <summary>Full view: the window doubles in width with every table beside the summary. Off (Compact, the default since 0.7.6): the run card and the summary only.</summary>
    public bool FullView { get; set; }
    /// <summary>The start/stop shortcut, as "Ctrl+Shift+K"; empty is off.</summary>
    public string Hotkey { get; set; } = Platform.Hotkey.Default;
    public int? WinX { get; set; }
    public int? WinY { get; set; }
    /// <summary>Where the over-game panel was last dragged to.</summary>
    public double? HudX { get; set; }
    public double? HudY { get; set; }

    static string PathFor()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KinahLedger");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "ui.json");
    }

    public static UserPrefs Load()
    {
        try { var p = PathFor(); if (File.Exists(p)) return JsonSerializer.Deserialize<UserPrefs>(File.ReadAllText(p)) ?? new UserPrefs(); }
        catch (Exception) { /* corrupt prefs: start fresh */ }
        return new UserPrefs();
    }

    public void Save() { try { File.WriteAllText(PathFor(), JsonSerializer.Serialize(this)); } catch (Exception) { /* read-only profile */ } }
}
