using System.Drawing;
using System.Text.Json;
using KinahLedger.Data;
using KinahLedger.Ocr;
using KinahLedger.Upload;

namespace KinahLedger.Session;

/// <summary>One screen-reading run: read, parse, count, and at the end optionally share. Used by both the window and `watch`.</summary>
public sealed class TrackerSession
{
    readonly ScreenReader _reader;
    readonly ChatParser _parser;
    readonly LineTracker _tracker = new();
    readonly CancellationTokenSource _cts = new();
    readonly int _interval;
    Task? _loop;

    public SessionEngine Engine { get; }
    public string Activity { get; set; }
    public long Reads { get; private set; }
    public int Unaligned => _tracker.Unaligned;
    public string LastError { get; private set; } = "";
    public string LastLine { get; private set; } = "";
    public bool Running => _loop != null && !_loop.IsCompleted;
    public Bitmap? Thumb => _reader.Thumb;
    public bool Eligible => Engine.Elapsed.TotalMinutes >= 5 && Engine.Events > 0;

    public event Action<string>? LootLine;
    public event Action? Updated;

    public TrackerSession(Rectangle region, Settings s, ItemDictionary items, string activity, int? intervalMs = null)
    {
        _reader = new ScreenReader(region, s.Ocr.Scale);
        _parser = new ChatParser(items);
        Engine = new SessionEngine(items);
        Activity = activity;
        _interval = Math.Clamp(intervalMs ?? s.Ocr.IntervalMs, 250, 5000);
    }

    public void Start() => _loop ??= Task.Run(() => Loop(_cts.Token));

    async Task Loop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var t0 = DateTime.UtcNow;
            try
            {
                var lines = await _reader.ReadAsync();
                Reads++;
                foreach (var l in _tracker.NewLines(lines))
                {
                    bool any = false;
                    foreach (var ev in _parser.Parse(l, DateTime.UtcNow)) { Engine.Apply(ev); any = true; }
                    if (any) { LastLine = l; LootLine?.Invoke(l); }
                }
                LastError = "";
            }
            catch (Exception ex) { LastError = ex.Message; }
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
    public int? WinX { get; set; }
    public int? WinY { get; set; }

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
