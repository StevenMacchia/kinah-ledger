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
    Task? _loop;
    DateTime _zoneAt = DateTime.MinValue;
    string? _lastZone;
    DateTime _zoneSince;

    public SessionEngine Engine { get; }
    public string Kind { get; }
    public Rectangle? Region { get; private set; }
    public bool ManualRegion { get; }
    public string? Zone { get; private set; }
    /// <summary>Seconds spent per detected zone, so the run can be attributed to where most of it happened.</summary>
    public Dictionary<string, double> ZoneSeconds { get; } = new();
    public long Reads { get; private set; }
    public int Unaligned => _tracker.Unaligned;
    public string LastError { get; private set; } = "";
    public string LastLine { get; private set; } = "";
    public string DetectNote { get; private set; } = "";
    public bool Running => _loop != null && !_loop.IsCompleted;
    public bool Eligible => Engine.Elapsed.TotalMinutes >= 5 && Engine.Events > 0;
    public Bitmap? Thumb => _reader?.Thumb;
    public Rectangle? GameRect { get; private set; }

    public static readonly Color ChatColor = Color.FromArgb(76, 195, 245), ZoneColor = Color.FromArgb(241, 208, 140);

    /// <summary>The whole game with the chat and map areas outlined; falls back to the chat crop when the game window isn't known.</summary>
    public Bitmap? Frame(int maxWidth)
    {
        var game = GameRect ?? GameWindow.Find(_settings.ProcessHints);
        if (game == null || Region == null) return Thumb;
        GameRect = game;
        try { return ScreenReader.Frame(game.Value, maxWidth, (Region.Value, ChatColor), (ZoneLocator.Strip(game.Value), ZoneColor)); }
        catch (Exception) { return Thumb; }
    }

    /// <summary>The kind plus the zone most of the run happened in, e.g. "Open world · Western Cantas Valley".</summary>
    public string Activity
    {
        get
        {
            var z = ZoneSeconds.Count > 0 ? ZoneSeconds.MaxBy(kv => kv.Value).Key : Zone;
            return z != null ? $"{Kind} · {z}" : Kind;
        }
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
            var (region, note) = await ChatLocator.DetectAsync(game.Value);
            DetectNote = note;
            if (region == null) throw new InvalidOperationException("Couldn't see the chat. Make sure the game UI is showing with the chat open at the bottom-left (not hidden, no full-screen panel), then try again, or choose the chat yourself.");
            Region = region;
        }
        _reader = new ScreenReader(Region.Value, _settings.Ocr.Scale);
        if (game != null) await ReadZone(game.Value);
        _loop ??= Task.Run(() => Loop(_cts.Token));
    }

    async Task ReadZone(Rectangle game)
    {
        try
        {
            var (zone, _) = await ZoneLocator.DetectAsync(game);
            var now = DateTime.UtcNow;
            if (_lastZone != null) ZoneSeconds[_lastZone] = ZoneSeconds.GetValueOrDefault(_lastZone) + (now - _zoneSince).TotalSeconds;
            if (zone != null) Zone = zone;
            _lastZone = zone ?? _lastZone; _zoneSince = now;
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
        if (_lastZone != null) ZoneSeconds[_lastZone] = ZoneSeconds.GetValueOrDefault(_lastZone) + (DateTime.UtcNow - _zoneSince).TotalSeconds;
        Engine.Finish();
    }

    public async Task StopAsync()
    {
        _cts.Cancel();
        if (_loop != null) { try { await _loop; } catch (Exception) { /* loop ended */ } }
        Engine.Finish();
    }

    public Task<bool> UploadAsync(Settings s) =>
        Eligible ? Uploader.SendAsync(RunPayload.Build(Engine, s, Activity), s.Upload) : Task.FromResult(false);
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
