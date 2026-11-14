using System.Text.Json;
using System.Text.Json.Serialization;
using KinahLedger.Packets;

namespace KinahLedger.Session;

/// <summary>
/// World boss timers (field bosses), from the game's own boss list. The game sends it about once a second while the world
/// map is open, so a timer is as fresh as the last time the player opened the map, and counts down here in between.
///
/// Each boss's time is on the game server's clock, which runs some seconds off this PC's (about 15 s behind on 2026-11-13).
/// So the time left is taken against the server's clock when the list came, and the spawn is kept as a moment on this PC's
/// clock (<see cref="Boss.SpawnPc"/>). The countdown then needs only this PC's clock and can't be off by the gap.
///
/// The last reading is saved (bosses.json beside the tracker's other files), so a restart still shows the timers with how
/// old they are. A boss seen alive stays "up when last seen", since nobody can know it is still up until the map is opened
/// again. A boss whose spawn time has passed is "due" until a list says it is up.
/// </summary>
public sealed class BossTimers
{
    /// <summary>The list is still coming (the map is open) when the last one is this recent.</summary>
    public const double LiveSeconds = 75;
    /// <summary>Readings older than this are dropped, on load and when the list is drawn.</summary>
    public static readonly TimeSpan KeepFor = TimeSpan.FromDays(3);

    public enum Kind { Waiting, Up, NoTimer }

    /// <summary>One boss as last read. SpawnPc: for Waiting the next spawn, for Up when it spawned; UTC on this PC's clock.</summary>
    public sealed record Boss(string Key, string Name, string Place, string Region, Kind State, DateTime SpawnPc, DateTime SeenAt, bool ClockGuessed);

    /// <summary>What a row says now. Left: to the spawn (Waiting); negative when it is past (due).</summary>
    public sealed record Row(Boss Boss, string Status, string When, TimeSpan Left, bool UpNow, bool Due, bool Stale);

    readonly object _gate = new();
    readonly Dictionary<string, Boss> _bosses = new();
    readonly string? _path;
    DateTime _savedAt = DateTime.MinValue;
    bool _dirty;

    /// <summary>Fired (on the reader's thread) when a boss's state or spawn changed, not on a refresh of the same reading.</summary>
    public event Action<Boss>? Changed;

    /// <summary>Timers saved to <paramref name="path"/> (null: kept in memory only, for tests and renders).</summary>
    public BossTimers(string? path = null) { _path = path; Load(); }

    /// <summary>bosses.json in the tracker's data folder (%LOCALAPPDATA%\KinahLedger).</summary>
    public static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KinahLedger", "bosses.json");

    /// <summary>When the boss list last came; null when it never did.</summary>
    public DateTime? LastSeen { get { lock (_gate) return _bosses.Count == 0 ? null : _bosses.Values.Max(b => b.SeenAt); } }

    public int Count { get { lock (_gate) return _bosses.Count; } }

    /// <summary>A boss as the decoder read it from the list; true when its state or spawn changed (not a refresh of the same reading).</summary>
    public bool Take(BossSighting s)
    {
        // the time left on the server's clock, put on this PC's: the list came at s.At (this PC), when the server said ServerNow
        TimeSpan offset = s.ServerNow is { } sv ? s.At - sv : TimeSpan.Zero;
        var state = s.State switch { BossSighting.Kind.Alive => Kind.Up, BossSighting.Kind.Dead => Kind.Waiting, _ => Kind.NoTimer };
        var spawn = s.Time is { } t ? t + offset : DateTime.MinValue;
        string name = s.Name.Length > 0 ? s.Name : (s.Region.Length > 0 ? $"A {s.Region} boss" : "A field boss");
        var b = new Boss(s.Key, name, s.Place, s.Region, state, spawn, s.At, s.ServerNow == null);
        Boss? changed = null;
        lock (_gate)
        {
            bool known = _bosses.TryGetValue(s.Key, out var was);
            // the same reading again (only the clock's gap moves a few ms between lists): keep the first spawn, move the sighting
            if (known && was!.State == b.State && Math.Abs((was.SpawnPc - b.SpawnPc).TotalSeconds) < 2) b = b with { SpawnPc = was.SpawnPc };
            else changed = b;
            _bosses[s.Key] = b;
            _dirty = true;
        }
        if (changed != null) Changed?.Invoke(changed);
        SaveSoon(s.At, changed != null);
        return changed != null;
    }

    /// <summary>Every boss as it stands at <paramref name="now"/> (UTC): up first, then due, then the soonest spawn.</summary>
    public List<Row> Rows(DateTime now)
    {
        List<Boss> all;
        lock (_gate) all = _bosses.Values.Where(b => now - b.SeenAt < KeepFor).ToList();
        var rows = all.Select(b => RowOf(b, now)).ToList();
        return rows.OrderBy(r => r.UpNow ? 0 : r.Boss.State == Kind.Up ? 1 : r.Due ? 2 : 3)
            .ThenBy(r => r.Boss.State == Kind.Waiting ? r.Left : TimeSpan.Zero)
            .ThenBy(r => r.Boss.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>What one boss's row says at <paramref name="now"/>.</summary>
    public static Row RowOf(Boss b, DateTime now)
    {
        bool live = (now - b.SeenAt).TotalSeconds <= LiveSeconds;
        switch (b.State)
        {
            case Kind.Up:
                {
                    var since = now - b.SpawnPc;
                    return live
                        ? new Row(b, "Up now", $"up {Span(since)}", TimeSpan.Zero, true, false, false)
                        : new Row(b, "Up when last seen", $"seen {Ago(now - b.SeenAt)}", TimeSpan.Zero, false, false, true);
                }
            case Kind.Waiting:
                {
                    var left = b.SpawnPc - now;
                    if (left > TimeSpan.Zero) return new Row(b, "Spawns in", Countdown(left), left, false, false, !live);
                    return new Row(b, "Due", $"{Span(-left)} ago", left, false, true, true);
                }
            default:
                return new Row(b, "No timer", "", TimeSpan.Zero, false, false, !live);
        }
    }

    /// <summary>The live countdown: "1h 12m 05s", "12m 05s", "45s".</summary>
    public static string Countdown(TimeSpan t)
    {
        long s = (long)Math.Ceiling(t.TotalSeconds);
        if (s <= 0) return "0s";
        long h = s / 3600, m = s % 3600 / 60, sec = s % 60;
        return h > 0 ? $"{h}h {m:00}m {sec:00}s" : m > 0 ? $"{m}m {sec:00}s" : $"{sec}s";
    }

    /// <summary>A rough length: "12 min", "2 h 5 min", "40 s".</summary>
    public static string Span(TimeSpan t)
    {
        if (t < TimeSpan.Zero) t = TimeSpan.Zero;
        if (t.TotalSeconds < 60) return $"{(int)t.TotalSeconds} s";
        if (t.TotalMinutes < 60) return $"{(int)t.TotalMinutes} min";
        if (t.TotalHours < 48) return t.Minutes == 0 ? $"{(int)t.TotalHours} h" : $"{(int)t.TotalHours} h {t.Minutes} min";
        return $"{(int)t.TotalDays} days";
    }

    public static string Ago(TimeSpan t) => t.TotalSeconds < 5 ? "just now" : Span(t) + " ago";

    /// <summary>One line on how fresh the reading is: "Live from the world map", "Read 2 h 5 min ago: open the world map to refresh".</summary>
    public string Freshness(DateTime now)
    {
        if (LastSeen is not { } at) return "No reading yet";
        var age = now - at;
        return age.TotalSeconds <= LiveSeconds ? "Live from the world map" : $"Read {Ago(age)}: open the world map to refresh";
    }

    // ---- saved copy ----
    sealed record Saved(int Version, DateTime SavedAt, List<Boss> Bosses);
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    void SaveSoon(DateTime at, bool changed)
    {
        if (_path == null) return;
        // the map open: the same list once a second, saved once a minute; a change at once, but not 24 times in the first list's
        // burst (the next list, a second later, saves the rest; the app's close saves what is left)
        double since = (at - _savedAt).TotalSeconds;
        if (since >= 0 && since < (changed ? 2 : 60)) return;
        Save(at);
    }

    /// <summary>Writes the timers now (also when the app closes).</summary>
    public void Save(DateTime? at = null)
    {
        if (_path == null) return;
        try
        {
            Saved snap;
            lock (_gate) { if (!_dirty) return; snap = new Saved(1, at ?? DateTime.UtcNow, _bosses.Values.ToList()); _dirty = false; }
            _savedAt = snap.SavedAt;
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(snap, Json));
            File.Move(tmp, _path, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { CrashLog.Note("boss timers: not saved: " + ex.Message); }
    }

    void Load()
    {
        if (_path == null || !File.Exists(_path)) return;
        try
        {
            var s = JsonSerializer.Deserialize<Saved>(File.ReadAllText(_path), Json);
            if (s?.Bosses == null) return;
            var now = DateTime.UtcNow;
            lock (_gate)
                foreach (var b in s.Bosses)
                    if (b.Key is { Length: > 0 } && b.Name != null && now - b.SeenAt < KeepFor)
                        _bosses[b.Key] = b with { SeenAt = DateTime.SpecifyKind(b.SeenAt, DateTimeKind.Utc), SpawnPc = DateTime.SpecifyKind(b.SpawnPc, DateTimeKind.Utc) };
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or NotSupportedException) { CrashLog.Note("boss timers: saved copy not read: " + ex.Message); }
    }

    /// <summary>For renders and tests: the timers set by hand.</summary>
    internal void SetForTest(IEnumerable<Boss> bosses) { lock (_gate) { _bosses.Clear(); foreach (var b in bosses) _bosses[b.Key] = b; _dirty = true; } }
}
