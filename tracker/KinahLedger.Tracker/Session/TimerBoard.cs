using System.Text.Json;

namespace KinahLedger.Session;

/// <summary>
/// Every timer the tracker knows as one list (0.10.85), for the timers overlay and its chime: the game's scheduled events
/// (<see cref="EventTimers"/>), the world bosses the world map listed (<see cref="BossTimers"/>) and the Abyss's bosses on
/// their schedule (<see cref="AbyssBosses"/>). Each item has a key that stays the same (what the player picked to watch or
/// mute), a short name for the overlay ("Shugo", "Kusan"), the moment it is about (its next start or spawn, on this PC's
/// clock) and what its row says now.
/// </summary>
public static class TimerBoard
{
    public enum Kind { On, Waiting, Due, WasUp }

    /// <summary>
    /// One timer. AtPc: the next start or spawn (UTC, this PC's clock), null when it is on now or has no time; SincePc: when
    /// it started or spawned, for one that is on, up or due. Short: the overlay's name.
    /// </summary>
    public sealed record Item(string Key, string Group, string Name, string Sub, Kind State, DateTime? AtPc, string Big, string Small, DateTime? SincePc = null)
    {
        public string Short { get; init; } = Name;
    }

    public static string EventKey(string name) => "event:" + name;
    public static string BossKey(string name) => "boss:" + name;
    public static string AbyssKey(string name) => "abyss:" + name;

    /// <summary>Every timer at <paramref name="now"/> (UTC, this PC's clock): on or up first, then the soonest.</summary>
    public static List<Item> Build(DateTime now, EventTimers events, BossTimers bosses, string? region)
    {
        var list = new List<Item>();
        foreach (var r in events.Rows(now, region))
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
        var shorts = ShortNames(list.Select(i => i.Name).Distinct());
        return list.Select(i => i with { Short = shorts.TryGetValue(i.Name, out var s) ? s : i.Name })
            .OrderBy(i => i.State switch { Kind.On => 0, Kind.Due => 1, Kind.Waiting => 2, _ => 3 })
            .ThenBy(i => i.AtPc ?? DateTime.MinValue).ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    static readonly Dictionary<string, string> EventShort = new()
    {
        ["Shugo Festival"] = "Shugo", ["Dimensional Invasion"] = "Invasion", ["Spacetime Rift"] = "Rift", ["Artifact Siege"] = "Siege",
        // the names players use where the last word isn't it
        ["Bloodfang Pnyn"] = "Bloodfang", ["Enraged Guardian Lord Nahma"] = "Enraged Nahma",
    };

    /// <summary>
    /// The overlay's names: an event's one word ("Shugo"), a boss's own name without its title ("Kusan the Mad Gladiator" is
    /// "Kusan", "Soul Ruler Kashapa" "Kashapa", "Guardian Lord Nahma ×3" "Nahma ×3"); two bosses that would share one keep
    /// a word of their title ("Watcher Kaira", "Executor Kaira").
    /// </summary>
    public static Dictionary<string, string> ShortNames(IEnumerable<string> names)
    {
        var all = names.ToList();
        static string Own(string n)
        {
            string spots = "";
            int x = n.IndexOf(" ×", StringComparison.Ordinal);
            if (x > 0) { spots = n[x..]; n = n[..x]; }
            int of = n.IndexOf(" of the ", StringComparison.Ordinal), the = n.IndexOf(" the ", StringComparison.Ordinal);
            if (of > 0) n = n[..of];
            else if (the > 0) n = n[..the];
            var words = n.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return (words.Length == 0 ? n : words[^1]) + spots;
        }
        var first = all.ToDictionary(n => n, n => EventShort.TryGetValue(n, out var e) ? e : Own(n));
        var clash = first.GroupBy(kv => kv.Value).Where(g => g.Count() > 1).SelectMany(g => g.Select(kv => kv.Key)).ToHashSet();
        foreach (var n in clash)
        {
            var words = n.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            first[n] = words.Length >= 2 ? words[^2] + " " + first[n] : n;
        }
        return first;
    }

    /// <summary>
    /// What the overlay shows at <paramref name="now"/> (Steven, 2026-11-14: "5 min til shugo, dimensional invasion,
    /// spacetime rift and the bosses ... minimal"): the watched timers that start or spawn within
    /// <see cref="TimerPrefs.WindowMinutes"/>, soonest first, at most <see cref="TimerPrefs.Count"/>. A boss that is up, or an
    /// event under way, is gone from it. None: the overlay hides.
    /// </summary>
    public static List<Item> ForOverlay(IEnumerable<Item> items, TimerPrefs prefs, DateTime now)
    {
        var window = TimeSpan.FromMinutes(prefs.WindowMinutes);
        return items.Where(i => i.State == Kind.Waiting && i.AtPc is { } at && at > now && at - now <= window && prefs.Watched(i.Key))
            .OrderBy(i => i.AtPc).Take(Math.Max(1, prefs.Count)).ToList();
    }

    /// <summary>The overlay's countdown: "4:52", "12:10", "1:05:20".</summary>
    public static string Clock(TimeSpan left)
    {
        long s = Math.Max(0, (long)Math.Ceiling(left.TotalSeconds));
        return s >= 3600 ? $"{s / 3600}:{s % 3600 / 60:00}:{s % 60:00}" : $"{s / 60}:{s % 60:00}";
    }
}

/// <summary>
/// The chime before a timer (0.10.85): once per start or spawn, when it is <see cref="TimerPrefs.ChimeMinutes"/> away or less,
/// for the watched timers whose chime isn't muted. A timer first seen already inside that time chimes too, unless it is under
/// 20 seconds away (that chime would come as it starts, too late to help).
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
/// The timers overlay's settings (0.10.85), in their own file, timers.json, beside the tracker's other files.
/// <list type="bullet">
/// <item>The overlay: on (on by default), click-through (on by default), where it is, how close a timer must be to show
///   (5 min) and how many at once (4).</item>
/// <item>The chime: on, how long before (5 min), how loud.</item>
/// <item>Which timers: watched by default are the four events, Soul Ruler Kashapa, Eternal Gartua, High Commander Lagta and
///   every Abyss boss (Steven, 2026-11-14); every other boss is off until the player turns it on. A watched timer chimes
///   unless muted.</item>
/// <item>The Timers window: where it is and its size, and whether its rows show their second line (off: minimal).</item>
/// </list>
/// </summary>
public sealed class TimerPrefs
{
    public bool OverlayOn { get; set; } = true;
    public bool ClickThrough { get; set; } = true;
    public double? X { get; set; }
    public double? Y { get; set; }
    public int Count { get; set; } = 4;
    public int WindowMinutes { get; set; } = 5;
    public bool ChimeOn { get; set; } = true;
    public int ChimeMinutes { get; set; } = 5;
    public double Volume { get; set; } = 0.5;
    /// <summary>Timers the player turned on that aren't watched by default.</summary>
    public HashSet<string> Watch { get; set; } = new();
    /// <summary>Timers watched by default that the player turned off.</summary>
    public HashSet<string> Unwatch { get; set; } = new();
    /// <summary>Watched timers whose chime the player turned off.</summary>
    public HashSet<string> Mute { get; set; } = new();
    /// <summary>
    /// The timers in their own strip (Ui/TimersOverlay.cs), apart from the Kinah overlay. Off by default (Steven, 2026-11-14:
    /// "have the tracker kinah [overlay] pop out at the top tiny"): the timer line sits on top of the Kinah overlay.
    /// </summary>
    public bool Separate { get; set; }
    /// <summary>The strip was shown once to be placed (the first time it was needed with the Kinah overlay off).</summary>
    public bool PlacedOnce { get; set; }

    /// <summary>Where the timer line goes: "kinah" (on top of the Kinah overlay), "strip" (its own) or "" (nowhere).</summary>
    public static string Host(bool overlayOn, bool separate, bool kinahOn) => !overlayOn ? "" : separate || !kinahOn ? "strip" : "kinah";

    /// <summary>The Timers window's rows show their second line (place, how often, the start time). Off by default.</summary>
    public bool Details { get; set; }
    public double? WinX { get; set; }
    public double? WinY { get; set; }
    public double? WinW { get; set; }
    public double? WinH { get; set; }

    /// <summary>The overlay's minutes until a timer counts as close: its row turns gold.</summary>
    public const int GoldMinutes = 5;

    /// <summary>Watched unless the player says otherwise: the events, Kashapa, Gartua, Lagta and the Abyss's bosses.</summary>
    public static bool WatchedByDefault(string key) =>
        key.StartsWith("event:", StringComparison.Ordinal) || key.StartsWith("abyss:", StringComparison.Ordinal)
        || (key.StartsWith("boss:", StringComparison.Ordinal) && (key.Contains("Kashapa") || key.Contains("Gartua") || key.Contains("Lagta")));

    public bool Watched(string key) => Watch.Contains(key) || (WatchedByDefault(key) && !Unwatch.Contains(key));
    public bool Chimes(string key) => Watched(key) && !Mute.Contains(key);

    public void SetWatched(string key, bool on)
    {
        if (on) { Unwatch.Remove(key); if (!WatchedByDefault(key)) Watch.Add(key); }
        else { Watch.Remove(key); if (WatchedByDefault(key)) Unwatch.Add(key); }
    }
    public void SetChimes(string key, bool on) { if (on) Mute.Remove(key); else Mute.Add(key); }

    public static readonly int[] CountChoices = { 2, 3, 4, 6, 8 };
    public static readonly int[] MinuteChoices = { 1, 2, 5, 10, 15, 30 };

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
        p.Count = Math.Clamp(p.Count, 1, 12);
        p.WindowMinutes = Math.Clamp(p.WindowMinutes, 1, 120);
        p.ChimeMinutes = Math.Clamp(p.ChimeMinutes, 1, 60);
        p.Volume = Math.Clamp(p.Volume, 0, 1);
        p.Watch ??= new(); p.Unwatch ??= new(); p.Mute ??= new();
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
