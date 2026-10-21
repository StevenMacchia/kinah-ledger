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
        var zs = e.ZoneSnapshot().Where(kv => kv.Key != SessionEngine.UnknownZone).ToList();
        var z = zs.Count > 0 ? zs.MaxBy(kv => kv.Value.Seconds).Key : e.Zone;
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

    string? _pendingZone; DateTime _zoneNoteAt = DateTime.MinValue;

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
            if (zone != null && zone != Engine.Zone) CrashLog.Note($"zone: {zone}");
            else if (zone == null && Engine.Zone == null && (DateTime.UtcNow - _zoneNoteAt).TotalSeconds >= 60) { _zoneNoteAt = DateTime.UtcNow; CrashLog.Note($"zone read: {note}"); }
            Engine.SetZone(zone);
            Secondary?.SetZone(zone);
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
                else { await ReadOnce(); await TargetDueAsync(); }
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
        if (_tracker.Unaligned > unaligned) CrashLog.Note($"couldn't line this read up with the last one, so nothing in it was counted ({lines.Count} lines; last: {lines[^1]})");
        // the enemy on the target bar, as last read, goes with this read's events (Kinah by enemy, a best guess)
        if (fresh.Count > 0) { Engine.SetTarget(Target.Name, Target.NameAt); Secondary?.SetTarget(Target.Name, Target.NameAt); ZoneView.SetTarget(Target.Name, Target.NameAt); }
        // parsed as one batch: a payment line claims the Kinah lines just before it in the same read
        foreach (var (l, events) in _parser.ParseBatch(fresh, DateTime.UtcNow))
        {
            bool any = false;
            foreach (var ev in events)
            {
                Engine.Apply(ev); Secondary?.Apply(ev); ZoneView.Apply(ev); any = true;
                if (ev is CraftResult or ItemAcquired) CraftView.Apply(ev);
                if (ev is KinahChanged { Bound: false } kc) Interlocked.Add(ref _kinahPicked, kc.Delta);
                try { Parsed?.Invoke(ev); } catch (Exception ex) { CrashLog.Write("parsed event", ex); }
            }
            CrashLog.Read(l, any, afterFade);
            if (any) { LastLine = l; LootLine?.Invoke(l); }
        }
        LastError = "";
    }

    DateTime _targetAt = DateTime.MinValue;

    /// <summary>
    /// The target bar every 2 seconds while the chat is read. Unlike the minimap, the panel isn't hidden for it: a read every
    /// 2 seconds would make it flicker. It is read through instead, and the window asks to move it only when nothing is read.
    /// </summary>
    async Task TargetDueAsync()
    {
        if ((DateTime.UtcNow - _targetAt).TotalSeconds < 2) return;
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
        _cts.Cancel();
        if (_loop != null) { try { await _loop; } catch (Exception) { /* loop ended */ } }
        Engine.Finish();
    }

    /// <summary>The player's region key and server label ("na_e", "Siel (Elyos)"), sent with the run so the site can keep drops per server.</summary>
    public string RegionKey { get; set; } = "";
    public string Server { get; set; } = "";

    public Task<bool> UploadAsync(Settings s) => UploadAsync(s, Engine, Kind, Activity);

    /// <summary>Shares one engine's totals (a farming run, or a chunk of the always-on count) under the given kind. Under five minutes or empty: nothing is sent.</summary>
    public Task<bool> UploadAsync(Settings s, SessionEngine e, string kind, string activity) =>
        e.Elapsed.TotalMinutes >= 5 && e.Events > 0 ? Uploader.SendAsync(RunPayload.Build(e, s, kind, activity, RegionKey.Length > 0 ? RegionKey : s.Region, Server, _items), s.Upload) : Task.FromResult(false);
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
    /// <summary>True once the chat and zone have been found: the window opens small, with the setup steps behind Settings.</summary>
    public bool Compact { get; set; }
    /// <summary>The Drops and Gathered sections both show at once; each folds to its header on a click, and stays folded.</summary>
    public bool DropsOpen { get; set; } = true;
    public bool GatherOpen { get; set; } = true;
    public bool MobsOpen { get; set; } = true;
    /// <summary>Off (the default): the tables start over when the zone changes. On: they keep counting across zones. What is shared is split by zone either way.</summary>
    public bool AllZones { get; set; }
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
