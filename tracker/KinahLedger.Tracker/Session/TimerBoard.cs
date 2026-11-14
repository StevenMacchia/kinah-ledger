using System.Text.Json;

namespace KinahLedger.Session;

/// <summary>
/// Every timer the tracker knows as one list (0.10.85), for the timers overlay and its chime: the game's scheduled events
/// (<see cref="EventTimers"/>), the world bosses the world map listed (<see cref="BossTimers"/>) and the Abyss's bosses on
/// their schedule (<see cref="AbyssBosses"/>). Each item has a key that stays the same (what the player picked to show or
/// chime for), the moment it is about (its next start or spawn, on this PC's clock) and what its row says now.
/// </summary>
public static class TimerBoard
{
    public enum Kind { On, Waiting, Due, WasUp }

    /// <summary>
    /// One timer. AtPc: the next start or spawn (UTC, this PC's clock), null when it is on now or has no time; SincePc: when
    /// it started or spawned, for one that is on, up or due.
    /// </summary>
    public sealed record Item(string Key, string Group, string Name, string Sub, Kind State, DateTime? AtPc, string Big, string Small, DateTime? SincePc = null);

    public static string EventKey(string name) => "event:" + name;
    public static string BossKey(string name) => "boss:" + name;
    public static string AbyssKey(string name) => "abyss:" + name;

    /// <summary>Every timer at <paramref name="now"/> (UTC, this PC's clock): on or up first, then the soonest.</summary>
    public static List<Item> Build(DateTime now, EventTimers events, BossTimers bosses, string? region)
    {
        var list = new List<Item>();
        foreach (var r in events.Rows(now))
            list.Add(new Item(EventKey(r.Event.Name), "Events", r.Event.Name, r.Event.Every, r.OnNow ? Kind.On : Kind.Waiting,
                r.OnNow ? null : r.StartPc, r.OnNow ? "On now" : r.When, r.OnNow ? r.When : $"at {r.StartPc.ToLocalTime():HH:mm}", r.OnNow ? r.StartPc : null));
        foreach (var r in bosses.Rows(now))
        {
            if (r.Boss.State == BossTimers.Kind.NoTimer) continue;
            var kind = r.UpNow ? Kind.On : r.Due ? Kind.Due : r.Boss.State == BossTimers.Kind.Up ? Kind.WasUp : Kind.Waiting;
            string big = kind switch { Kind.On => "Up now", Kind.Due => "Due", Kind.WasUp => "Was up", _ => r.When };
            list.Add(new Item(BossKey(r.Boss.Name), r.Boss.Region.Length > 0 ? r.Boss.Region : "World bosses", r.Boss.Name, r.Boss.Place, kind,
                kind == Kind.Waiting ? r.Boss.SpawnPc : null, big, kind == Kind.Waiting ? $"at {r.Boss.SpawnPc.ToLocalTime():HH:mm}" : r.When,
                kind == Kind.Waiting ? null : r.Boss.SpawnPc));
        }
        foreach (var r in AbyssBosses.Rows(now, events.Offset, region))
            list.Add(new Item(AbyssKey(r.Boss.Name), r.Boss.Map, r.Boss.Spots > 1 ? $"{r.Boss.Name} ×{r.Boss.Spots}" : r.Boss.Name, r.Boss.Place,
                r.Spawned ? Kind.Due : Kind.Waiting, r.Spawned ? null : r.SpawnPc, r.Spawned ? "Spawned" : r.When,
                r.Spawned ? r.When : r.Left.TotalHours >= 20 ? $"{r.SpawnPc.ToLocalTime():ddd HH:mm}" : $"at {r.SpawnPc.ToLocalTime():HH:mm}",
                r.Spawned ? r.SpawnPc : null));
        return list.OrderBy(i => i.State switch { Kind.On => 0, Kind.Due => 1, Kind.Waiting => 2, _ => 3 })
            .ThenBy(i => i.AtPc ?? DateTime.MinValue).ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// What the overlay shows at <paramref name="now"/> (Steven, 2026-11-14: "ONLY appear within 5 min of that event ...
    /// otherwise invisible"): the timers the player keeps on it that start or spawn within <see cref="TimerPrefs.ShowMinutes"/>,
    /// or started or spawned less than that ago; at most <see cref="TimerPrefs.Count"/>. None: the overlay hides.
    /// </summary>
    public static List<Item> ForOverlay(IEnumerable<Item> items, TimerPrefs prefs, DateTime now)
    {
        var window = TimeSpan.FromMinutes(prefs.ShowMinutes);
        bool Near(Item i) => i.State switch
        {
            Kind.Waiting => i.AtPc is { } at && at > now && at - now <= window,
            Kind.On or Kind.Due => i.SincePc is { } since && now - since <= window,
            _ => false,
        };
        return items.Where(i => prefs.Shows(i.Key) && Near(i)).Take(Math.Max(1, prefs.Count)).ToList();
    }
}

/// <summary>
/// The chime before a timer (0.10.85): once per start or spawn, when it is <see cref="TimerPrefs.ChimeMinutes"/> away or less,
/// for the timers the player turned the chime on for. A timer first seen already inside the window chimes too, unless it
/// is under 20 seconds away (that chime would come as it starts, too late to help).
/// </summary>
public sealed class TimerChimes
{
    readonly HashSet<string> _rung = new();

    /// <summary>The items to chime for at <paramref name="now"/>; each start or spawn only once.</summary>
    public List<TimerBoard.Item> Due(DateTime now, IEnumerable<TimerBoard.Item> items, TimerPrefs prefs)
    {
        var due = new List<TimerBoard.Item>();
        if (!prefs.ChimeOn || prefs.Volume <= 0) return due;
        foreach (var i in items)
        {
            if (i.AtPc is not { } at || i.State != TimerBoard.Kind.Waiting || !prefs.Chimes(i.Key)) continue;
            var left = at - now;
            if (left <= TimeSpan.FromSeconds(20) || left > TimeSpan.FromMinutes(prefs.ChimeMinutes)) continue;
            // the same start read again a second later (a boss's spawn moves by the clock's gap): one chime per minute of it
            string id = $"{i.Key}@{at:yyyyMMddHHmm}";
            if (_rung.Add(id)) due.Add(i);
        }
        if (_rung.Count > 500) _rung.Clear();
        return due;
    }
}

/// <summary>
/// The timers overlay's and chime's settings (0.10.85), in timers.json beside the tracker's other files: whether the overlay
/// is on, where it is, click-through, how many timers it shows, the timers hidden from it, the timers with a chime, how many
/// minutes before, and its volume. Everything shows on the overlay until the player hides it; no timer chimes until the
/// player turns its bell on.
/// </summary>
public sealed class TimerPrefs
{
    public bool OverlayOn { get; set; }
    public bool ClickThrough { get; set; }
    public double? X { get; set; }
    public double? Y { get; set; }
    public int Count { get; set; } = 5;
    public bool ChimeOn { get; set; } = true;
    public int ChimeMinutes { get; set; } = 5;
    /// <summary>The overlay shows a timer from this many minutes before it starts or spawns until this many after.</summary>
    public int ShowMinutes { get; set; } = 5;
    public double Volume { get; set; } = 0.5;
    public HashSet<string> Hidden { get; set; } = new();
    public HashSet<string> Chime { get; set; } = new();

    public bool Shows(string key) => !Hidden.Contains(key);
    public bool Chimes(string key) => Chime.Contains(key);
    public void SetShows(string key, bool on) { if (on) Hidden.Remove(key); else Hidden.Add(key); }
    public void SetChimes(string key, bool on) { if (on) Chime.Add(key); else Chime.Remove(key); }

    public static readonly int[] CountChoices = { 3, 5, 8, 12 };
    public static readonly int[] MinuteChoices = { 1, 2, 5, 10, 15 };

    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    [System.Text.Json.Serialization.JsonIgnore] public string? Path { get; private set; }

    public static string DefaultPath => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KinahLedger", "timers.json");

    /// <summary>The settings in <paramref name="path"/> (the defaults when there are none, or they can't be read).</summary>
    public static TimerPrefs Load(string? path = null)
    {
        path ??= DefaultPath;
        TimerPrefs p = new();
        try { if (File.Exists(path)) p = JsonSerializer.Deserialize<TimerPrefs>(File.ReadAllText(path), Json) ?? new(); }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { CrashLog.Note("timers.json not read: " + ex.Message); }
        p.Path = path;
        p.Count = CountChoices.Contains(p.Count) ? p.Count : 5;
        p.ChimeMinutes = Math.Clamp(p.ChimeMinutes, 1, 60);
        p.ShowMinutes = Math.Clamp(p.ShowMinutes, 1, 60);
        p.Volume = Math.Clamp(p.Volume, 0, 1);
        p.Hidden ??= new(); p.Chime ??= new();
        return p;
    }

    /// <summary>Settings that are never saved (renders, self-tests).</summary>
    public static TimerPrefs Defaults() => new();

    public void Save()
    {
        if (Path == null) return;
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            var tmp = Path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, Json));
            File.Move(tmp, Path, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { CrashLog.Note("timers.json not saved: " + ex.Message); }
    }
}
