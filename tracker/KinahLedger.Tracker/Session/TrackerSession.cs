using System.Drawing;
using System.Text.Json;
using KinahLedger.Data;
using KinahLedger.Ocr;
using KinahLedger.Upload;

namespace KinahLedger.Session;

/// <summary>One screen-reading run: find the chat (or use a chosen area), read, parse, count, track the zone, and at the end optionally share.</summary>
public sealed class TrackerSession
{
    readonly Settings _settings;
    readonly ItemDictionary _items;
    readonly ChatParser _parser;
    readonly LineTracker _tracker = new();
    readonly CancellationTokenSource _cts = new();
    readonly int _interval;
    ScreenReader? _reader;
    Task? _loop, _marketLoop;
    DateTime _zoneAt = DateTime.MinValue;

    public SessionEngine Engine { get; }
    /// <summary>The Market reader that runs beside the chat loop; its switch and server can change while the run is on.</summary>
    public MarketCapture Market { get; }
    public string Kind { get; }
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
    public bool Running => _loop != null && !_loop.IsCompleted;
    public bool Eligible => Engine.Elapsed.TotalMinutes >= 5 && Engine.Events > 0;
    public Bitmap? Thumb => _reader?.Thumb;
    public Rectangle? GameRect { get; private set; }

    public static readonly Color ChatColor = Color.FromArgb(76, 195, 245), ZoneColor = Color.FromArgb(241, 208, 140);

    /// <summary>The whole game with the chat, map and (when open) Market areas outlined; falls back to the chat crop when the game window isn't known.</summary>
    public Bitmap? Frame(int maxWidth)
    {
        var game = GameRect ?? GameWindow.Find(_settings.ProcessHints);
        if (game == null || Region == null) return Thumb;
        GameRect = game;
        var marks = new List<(Rectangle, Color)> { (Region.Value, ChatColor), (ZoneLocator.Strip(game.Value), ZoneColor) };
        if (Market.Enabled && Market.Layout is { } m && (Market.Detected || Market.Manual)) marks.Add((m.Outline, MarketCapture.MarkColor));
        try { return ScreenReader.Frame(game.Value, maxWidth, marks.ToArray()); }
        catch (Exception) { return Thumb; }
    }

    /// <summary>The kind plus the zone most of the run happened in, e.g. "Open world · Western Cantas Valley".</summary>
    public string Activity
    {
        get
        {
            var zs = ZoneSeconds;
            var z = zs.Count > 0 ? zs.MaxBy(kv => kv.Value).Key : Zone;
            return z != null ? $"{Kind} · {z}" : Kind;
        }
    }

    public event Action<string>? LootLine;
    public event Action? Updated;

    public TrackerSession(Rectangle? manualRegion, Settings s, ItemDictionary items, string kind, int? intervalMs = null, Rectangle? marketRegion = null, string server = "")
    {
        _settings = s;
        _items = items;
        _parser = new ChatParser(items);
        Engine = new SessionEngine(items);
        Market = new MarketCapture(s, items, marketRegion, server);
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
            var (region, note) = await ChatLocator.DetectAsync(game.Value);
            DetectNote = note;
            if (region == null) throw new InvalidOperationException("Couldn't see the chat. Open the chat so its input box shows (press Enter once, or pin the chat window in the game's chat settings), then Detect again, or choose the chat yourself.");
            Region = region;
        }
        _reader = new ScreenReader(Region.Value, _settings.Ocr.Scale);
        if (game != null) await ReadZone(game.Value);
        _loop ??= Task.Run(() => Loop(_cts.Token));
        if (_settings.Market.Enabled) _marketLoop ??= Task.Run(() => MarketLoop(_cts.Token));
    }

    /// <summary>
    /// The Market reader's own loop, about once a second, so a slow read of the big Market table never delays the chat.
    /// It looks for the Market while it isn't open, reads it while it is, and sends queued asks every few seconds.
    /// </summary>
    async Task MarketLoop(CancellationToken ct)
    {
        int interval = Math.Clamp(_settings.Market.IntervalMs, 1000, 10000);
        while (!ct.IsCancellationRequested)
        {
            var t0 = DateTime.UtcNow;
            try
            {
                if (Market.Enabled)
                {
                    var game = GameRect ?? GameWindow.Find(_settings.ProcessHints);
                    if (game != null) await Market.TickAsync(game.Value, _settings.Ocr.Scale);
                }
                if (Market.FlushDue) await Market.FlushAsync();
            }
            catch (Exception ex) { Market.LastError = ex.Message; CrashLog.Write("market", ex); }
            int spent = (int)(DateTime.UtcNow - t0).TotalMilliseconds;
            if (spent < interval) { try { await Task.Delay(interval - spent, ct); } catch (TaskCanceledException) { } }
        }
        // what was read in the last seconds still goes out when the run stops
        try { while (Market.Pending > 0 && await Market.FlushAsync()) { } }
        catch (Exception ex) { CrashLog.Write("market flush", ex); }
    }

    async Task ReadZone(Rectangle game)
    {
        try
        {
            var (zone, _) = await ZoneLocator.DetectAsync(game);
            Engine.SetZone(zone);
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
                var lines = await _reader!.ReadAsync();
                Reads++;
                // the chat moved or the UI was toggled: after a stretch of empty reads, find it again (auto mode only)
                _emptyReads = lines.Count == 0 ? _emptyReads + 1 : 0;
                if (!ManualRegion && _emptyReads >= 20)
                {
                    _emptyReads = 0;
                    var g = GameWindow.Find(_settings.ProcessHints);
                    if (g != null)
                    {
                        var (again, note) = await ChatLocator.DetectAsync(g.Value);
                        if (again != null && again != Region) { Region = again; _reader = new ScreenReader(again.Value, _settings.Ocr.Scale); DetectNote = "re-found: " + note; }
                    }
                }
                foreach (var l in _tracker.NewLines(lines))
                {
                    bool any = false;
                    foreach (var ev in _parser.Parse(l, DateTime.UtcNow)) { Engine.Apply(ev); any = true; }
                    CrashLog.Read(l, any);
                    if (any) { LastLine = l; LootLine?.Invoke(l); }
                }
                LastError = "";
                if ((DateTime.UtcNow - _zoneAt).TotalSeconds >= _settings.Ocr.ZoneEverySec)
                {
                    var game = GameWindow.Find(_settings.ProcessHints);
                    GameRect = game ?? GameRect;
                    if (game != null) await ReadZone(game.Value); else _zoneAt = DateTime.UtcNow;
                }
            }
            catch (Exception ex) { LastError = ex.Message; CrashLog.Write("read loop", ex); }
            Updated?.Invoke();

            int spent = (int)(DateTime.UtcNow - t0).TotalMilliseconds;
            if (spent < _interval) { try { await Task.Delay(_interval - spent, ct); } catch (TaskCanceledException) { } }
        }
        Engine.Finish();
    }

    public async Task StopAsync()
    {
        _cts.Cancel();
        if (_loop != null) { try { await _loop; } catch (Exception) { /* loop ended */ } }
        if (_marketLoop != null) { try { await _marketLoop; } catch (Exception) { /* loop ended */ } }
        Engine.Finish();
    }

    public Task<bool> UploadAsync(Settings s) =>
        Eligible ? Uploader.SendAsync(RunPayload.Build(Engine, s, Kind, Activity), s.Upload) : Task.FromResult(false);
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
    /// <summary>The server as typed ("Siel (Elyos)"), sent with prices so they land on the right board. Nothing is sent without it.</summary>
    public string Server { get; set; } = "";
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
