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

    public TrackerSession(Rectangle? manualRegion, Settings s, ItemDictionary items, string kind, int? intervalMs = null)
    {
        _settings = s;
        _items = items;
        _parser = new ChatParser(items);
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
            if (zone != null && ZoneView.Zone != null && zone != ZoneView.Zone) { var fresh = new SessionEngine(_items); fresh.SetZone(zone); ZoneView = fresh; }
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
                else { await ReadOnce(); await TargetDueAsync(); await LootListAsync(); }
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
            if (spent < _interval) { try { await Task.Delay(_interval - spent, ct); } catch (TaskCanceledException) { } }
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
        bool reward = Target.RewardOpen(DateTime.UtcNow);
        var now = DateTime.UtcNow;
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
        bool Apply(IEnumerable<GameEvent> events)
        {
            bool any = false;
            foreach (var parsed in events)
            {
                // the loot list beside the character already added this pickup (this chat line came late): it counts once
                if (_cross.Chat(parsed, now)) { any = true; continue; }
                // with the crafting window open, an item a recipe makes is a craft, never a reward or a drop
                bool made = crafting && parsed is ItemAcquired ia && RecipeBook.ForOutput(_items.NameOf(ia.ItemId)) != null;
                var ev = reward && !made ? _parser.AsReward(parsed) : parsed;
                Engine.Apply(ev); Secondary?.Apply(ev); ZoneView.Apply(ev); Instances.Loot(ev); any = true;
                if (ev is CraftResult or ItemAcquired) CraftView.Apply(ev);
                if (ev is KinahChanged { Bound: false } kc) Interlocked.Add(ref _kinahPicked, kc.Delta);
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
            foreach (var (_, events) in _parser.ParseBatch(new List<string> { l }, now, wrap: false)) any |= Apply(events);
            CrashLog.Read(l + "    (read again: it was garbled while fading in)", any);
            if (any) { LastLine = l; LootLine?.Invoke(l); }
        }
        LastError = "";
    }

    DateTime _targetAt = DateTime.MinValue;

    /// <summary>The chat and the loot list beside the character, checked against each other: see <see cref="LootCrossCheck"/>.</summary>
    readonly LootCrossCheck _cross;
    public LootCrossCheck CrossCheck => _cross;

    /// <summary>
    /// One read of the loot list beside the character, every pass while the game is in view. Its rows wait for the chat;
    /// a row the chat never matched is counted from the list, through the same rules as a chat line (reward screens too).
    /// </summary>
    async Task LootListAsync()
    {
        if (GameRect is not { } g) return;
        // every pass: a row is up about three seconds, and during fast kills a pass can take over a second
        var region = LootPopup.Region(g);
        try
        {
            var cover = GameWindow.Covered(region, _settings.ProcessHints);
            if (cover != null) return;   // something over it (this window, a browser): nothing read, nothing added
            var lines = await ScreenReader.ReadBoxesAsync(region, 3, boost: true);
            var now = DateTime.UtcNow;
            var add = _cross.Popup(LootPopup.Rows(lines.Select(l => (l.Box.Top, l.Text))), now);
            if (add.Count == 0) return;
            bool reward = Target.RewardOpen(now);
            foreach (var e in new[] { Engine, Secondary, ZoneView, CraftView }) e?.SetCraftWindow(Target.CraftAt);
            Engine.SetTarget(Target.Name, Target.NameAt); Secondary?.SetTarget(Target.Name, Target.NameAt); ZoneView.SetTarget(Target.Name, Target.NameAt);
            bool crafting = (now - Target.CraftAt).TotalSeconds <= TargetReader.CraftGraceSeconds;
            foreach (var parsed in add)
            {
                bool made = crafting && parsed is ItemAcquired ia && RecipeBook.ForOutput(_items.NameOf(ia.ItemId)) != null;
                var ev = reward && !made ? _parser.AsReward(parsed) : parsed;
                Engine.Apply(ev); Secondary?.Apply(ev); ZoneView.Apply(ev); Instances.Loot(ev);
                if (ev is ItemAcquired) CraftView.Apply(ev);
                if (ev is KinahChanged { Bound: false } kc) Interlocked.Add(ref _kinahPicked, kc.Delta);
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
    /// <summary>The window's close button hides it to the tray; Quit in the tray menu ends the tracker.</summary>
    public bool CloseToTray { get; set; }
    /// <summary>Minimizing always hides to the tray, not only during a run.</summary>
    public bool MinimizeToTray { get; set; }
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
