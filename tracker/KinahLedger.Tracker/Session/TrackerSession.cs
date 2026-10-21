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

    public TrackerSession(Rectangle? manualRegion, Settings s, ItemDictionary items, string kind, int? intervalMs = null)
    {
        _settings = s;
        _items = items;
        _parser = new ChatParser(items);
        Engine = new SessionEngine(items);
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
        }
        _reader = new ScreenReader(Region.Value, _settings.Ocr.Scale);
        if (game != null && GameWindow.Covered(ZoneLocator.Strip(game.Value), _settings.ProcessHints) == null) await ReadZone(game.Value);
        _loop ??= Task.Run(() => Loop(_cts.Token));
    }

    string? _pendingZone;

    async Task ReadZone(Rectangle game)
    {
        try
        {
            var (zone, _) = await ZoneLocator.DetectAsync(game);
            if (zone != null && !ZoneLocator.IsVerified(zone))
            {
                // an unfamiliar label becomes the zone only when two reads in a row agree; a stray read (a button, a tooltip) keeps the last zone
                if (!string.Equals(_pendingZone, zone, StringComparison.Ordinal)) { _pendingZone = zone; zone = null; }
            }
            else _pendingZone = null;
            Engine.SetZone(zone);
            Secondary?.SetZone(zone);
        }
        catch (Exception ex) { CrashLog.Write("zone", ex); }
        _zoneAt = DateTime.UtcNow;
    }

    int _emptyReads;

    async Task Loop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var t0 = DateTime.UtcNow;
            try
            {
                // only the game is read: while it is minimized or another window sits over the chat, nothing is read and the clocks stop
                if (GameWindow.Covered(Region!.Value, _settings.ProcessHints) is { } cover)
                {
                    Engine.Pause(); Secondary?.Pause();
                    Paused = cover.StartsWith("the game") ? cover : cover + " covers the chat";
                    LastError = "";
                }
                else await ReadOnce();
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
        Engine.Resume(); Secondary?.Resume();
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
                if (again != null && again != Region) { Region = again; _reader = new ScreenReader(again.Value, _settings.Ocr.Scale); DetectNote = "re-found: " + note; }
            }
        }
        foreach (var l in _tracker.NewLines(lines))
        {
            bool any = false;
            foreach (var ev in _parser.Parse(l, DateTime.UtcNow)) { Engine.Apply(ev); Secondary?.Apply(ev); any = true; }
            CrashLog.Read(l, any);
            if (any) { LastLine = l; LootLine?.Invoke(l); }
        }
        LastError = "";
        if ((DateTime.UtcNow - _zoneAt).TotalSeconds >= _settings.Ocr.ZoneEverySec)
        {
            var game = GameWindow.Find(_settings.ProcessHints);
            GameRect = game ?? GameRect;
            // the minimap has its own check: a window over it alone keeps the last zone and lets the chat be read
            if (game != null && GameWindow.Covered(ZoneLocator.Strip(game.Value), _settings.ProcessHints) == null) await ReadZone(game.Value);
            else _zoneAt = DateTime.UtcNow;
        }
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
        e.Elapsed.TotalMinutes >= 5 && e.Events > 0 ? Uploader.SendAsync(RunPayload.Build(e, s, kind, activity, RegionKey.Length > 0 ? RegionKey : s.Region, Server), s.Upload) : Task.FromResult(false);
}

/// <summary>What the window remembers between runs, kept per user.</summary>
public sealed class UserPrefs
{
    public string Activity { get; set; } = "Open world";
    public string Place { get; set; } = "";
    public bool Hud { get; set; } = true;
    public bool Share { get; set; } = true;
    /// <summary>True once the player chose the chat by hand; auto-detection then stays off until they switch back.</summary>
    public bool ManualChat { get; set; }
    /// <summary>True once the chat and zone have been found: the window opens small, with the setup steps behind Settings.</summary>
    public bool Compact { get; set; }
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
