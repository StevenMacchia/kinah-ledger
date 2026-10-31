using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using KinahLedger.Data;

namespace KinahLedger.Session;

/// <summary>One farming zone of a kept day: what it made (Kinah plus loot after the Market's cut), how long, the kills.</summary>
public sealed class FarmDayZone
{
    public string Zone { get; set; } = "";
    public long Made { get; set; }
    public double Active { get; set; }
    public int Kills { get; set; }
    /// <summary>The zone's best drop that day, the sellable item worth most there, its count and worth (Compare, 0.9.3); empty on days kept before 0.9.3.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public string Best { get; set; } = "";
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public int BestCount { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public long BestWorth { get; set; }
}

/// <summary>
/// One local day's farming as days.json keeps it: the same figures LOOT's Today side shows for the whole day (made is
/// Kinah plus drops and gathering at the lowest Market price, after the Market's cut, as priced when the day was saved).
/// </summary>
public sealed class FarmDay
{
    /// <summary>The local calendar day, "2026-10-27" (<see cref="DayStore.DayOf"/>).</summary>
    public string Day { get; set; } = "";
    public long Made { get; set; }
    public long Kinah { get; set; }
    /// <summary>Seconds of farming: within a minute of a loot, kill or gather line.</summary>
    public double Active { get; set; }
    public int Kills { get; set; }
    /// <summary>Gathering attempts, so a day of gathering says so.</summary>
    public int Attempts { get; set; }
    /// <summary>The day's zones with something in them, most made first.</summary>
    public List<FarmDayZone> Zones { get; set; } = new();

    [JsonIgnore] public bool Any => Made > 0 || Kinah > 0 || Kills > 0 || Attempts > 0;
}

/// <summary>days.json: the kept days and the day history began (the first day 0.9 or later ran here).</summary>
public sealed class DayFile
{
    public int Version { get; set; } = 1;
    /// <summary>The first day history was kept, "2026-10-29"; a day before it shows "–", not nothing.</summary>
    public string Since { get; set; } = "";
    public List<FarmDay> Days { get; set; } = new();

    public FarmDay? Get(string day) => Days.FirstOrDefault(d => d.Day == day);
}

/// <summary>Runs of one kind in a day ("Expedition", "Transcendence", "Event", "Arena", "Legion", "World Boss", or "" for any other).</summary>
public sealed record RunKind(string Kind, int Count);

/// <summary>Runs under one name in a day: an event's own name, else the place ("Urugugu Canyon ×7").</summary>
public sealed record RunName(string Name, int Count, long Kinah);

/// <summary>
/// One local day's expeditions and events, grouped by the local day each run started: every run, the counted ones (as the
/// Events page counts them: a run left out of the averages adds no Kinah), their Kinah, by kind and by name.
/// </summary>
public sealed record RunsDay(int Runs, int Counted, long Kinah, List<RunKind> Kinds, List<RunName> Names)
{
    public static RunsDay None => new(0, 0, 0, new(), new());
}

/// <summary>One day of the Week side: its date, whether it is today, whether farming history had begun, its farming and its runs.</summary>
public sealed record HistoryDay(DateTime Date, string Key, bool IsToday, bool BeforeHistory, FarmDay Farm, RunsDay Runs);

/// <summary>
/// The day history (0.9, History A of kinah-bench/mock-goals-history, Steven 2026-10-29): days.json beside today.json keeps one
/// entry per local day of farming (made, Kinah, active time, kills, and each zone's), updated whenever today.json is saved
/// (DayStore.Save), finalized at the midnight rollover, seeded from today.json the first time, the last
/// <see cref="KeepDays"/> days kept. Runs need no store of their own: they come from the run log (instances.jsonl), grouped
/// by the local day each started. <see cref="Days"/> is the one way in: the last N days ending today, today live from the day
/// engine. Display only, on this PC: nothing here is sent anywhere. Independent of the window, so weekly goals can read it.
/// </summary>
public static class DayHistory
{
    /// <summary>Days kept in days.json, today included.</summary>
    public const int KeepDays = 60;

    public static string PathFor() => Path.Combine(CrashLog.Dir, "days.json");

    /// <summary>days.json beside a today.json at <paramref name="todayPath"/> (a test's scratch folder).</summary>
    public static string PathBeside(string todayPath) => Path.Combine(Path.GetDirectoryName(Path.GetFullPath(todayPath))!, "days.json");

    static readonly object _gate = new();
    static readonly JsonSerializerOptions Json = new();
    // the file as last read or written, so the window's tick doesn't read the disk twice a second
    static DayFile? _cache; static string _cachePath = ""; static DateTime _cacheStamp;

    /// <summary>"2026-10-27" as a date; null when it isn't one.</summary>
    public static DateTime? ParseDay(string? day) =>
        DateTime.TryParseExact(day, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d.Date : null;

    /// <summary>The file at <paramref name="path"/> (days.json by default); an empty one when there is none or it can't be read.</summary>
    public static DayFile Load(string? path = null)
    {
        var p = path ?? PathFor();
        lock (_gate) return LoadLocked(p);
    }

    static DayFile LoadLocked(string p)
    {
        try
        {
            if (!File.Exists(p)) return new DayFile();
            return JsonSerializer.Deserialize<DayFile>(File.ReadAllText(p), Json) ?? new DayFile();
        }
        catch (Exception ex)
        {
            CrashLog.Write("days load", ex);
            // kept aside once, so a broken file is never silently lost to the next save
            try { var bad = p + ".bad"; if (!File.Exists(bad)) File.Copy(p, bad); } catch (Exception) { /* the history starts over either way */ }
            return new DayFile();
        }
    }

    /// <summary>
    /// The file as it stands, read again only when it changed on disk: what the window reads on every tick. A copy is never
    /// handed out to change; <see cref="Record"/> and <see cref="Seed"/> are the writers.
    /// </summary>
    public static DayFile Current(string? path = null)
    {
        var p = path ?? PathFor();
        lock (_gate)
        {
            DateTime stamp;
            try { stamp = File.Exists(p) ? File.GetLastWriteTimeUtc(p) : DateTime.MinValue; } catch (Exception) { stamp = DateTime.MinValue; }
            if (_cache != null && _cachePath == p && _cacheStamp == stamp) return _cache;
            _cache = LoadLocked(p); _cachePath = p; _cacheStamp = stamp;
            return _cache;
        }
    }

    /// <summary>Writes the file (a temporary file first, so a crash mid-write never leaves half a file), the days older than <see cref="KeepDays"/> dropped.</summary>
    public static void Save(DayFile f, string? path = null, DateTime? todayLocal = null)
    {
        var p = path ?? PathFor();
        lock (_gate) SaveLocked(f, p, todayLocal);
    }

    static void SaveLocked(DayFile f, string p, DateTime? todayLocal)
    {
        Prune(f, (todayLocal ?? DateTime.Now).Date);
        var tmp = p + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(f, Json));
        File.Move(tmp, p, overwrite: true);
        _cache = null;
    }

    /// <summary>Drops the days before the last <see cref="KeepDays"/> ending <paramref name="today"/>, and puts the rest in order.</summary>
    public static void Prune(DayFile f, DateTime today)
    {
        var first = DayStore.DayOf(today.Date.AddDays(-(KeepDays - 1)));
        f.Days.RemoveAll(d => d.Day.Length != 10 || string.CompareOrdinal(d.Day, first) < 0);
        f.Days.Sort((a, b) => string.CompareOrdinal(a.Day, b.Day));
    }

    /// <summary>A day's farming from the figures LOOT's Today side works out (<see cref="TodayLoot.Farm"/>): the same made, the same zones.</summary>
    public static FarmDay FromFarm(string day, TodayFarm f) => new()
    {
        Day = day, Made = f.Made, Kinah = f.Kinah, Active = f.Active, Kills = f.Kills, Attempts = f.Attempts,
        Zones = f.Zones.Select(z => new FarmDayZone { Zone = z.Zone, Made = z.Made, Active = z.Active, Kills = z.Kills, Best = z.Best, BestCount = z.BestCount, BestWorth = z.BestWorth }).ToList(),
    };

    /// <summary>A day's farming from what today.json carries: a fresh engine seeded with it, read the way LOOT's Today side reads the live one.</summary>
    public static FarmDay FromCarry(SessionEngine.Carry c, ItemDictionary items)
    {
        var e = new SessionEngine(items);
        e.Seed(c);
        return FromFarm(c.Day, TodayLoot.Farm(e, items));
    }

    /// <summary>
    /// The day's entry written into days.json, in place of the one it had: called whenever today.json is saved (a minute
    /// apart, at close, and for the old day at midnight, which is its final figure). History begins with the first day written.
    /// </summary>
    public static void Record(SessionEngine.Carry c, ItemDictionary items, string? path = null, DateTime? todayLocal = null)
    {
        if (ParseDay(c.Day) == null) return;
        var day = FromCarry(c, items);
        var p = path ?? PathFor();
        lock (_gate)
        {
            var f = LoadLocked(p);
            if (f.Since.Length == 0 || string.CompareOrdinal(day.Day, f.Since) < 0) f.Since = day.Day;
            f.Days.RemoveAll(d => d.Day == day.Day);
            f.Days.Add(day);
            SaveLocked(f, p, todayLocal);
        }
    }

    /// <summary>
    /// When the tracker starts reading: days.json begins here the first time (history starts today), and a today.json from a
    /// day the file doesn't have yet (the tracker closed before midnight, or the first run after an update) becomes that
    /// day's entry, history starting with it. Run before the day's first save, which writes today.json over.
    /// </summary>
    public static void Seed(ItemDictionary items, string? todayPath = null, string? path = null, DateTime? todayLocal = null)
    {
        var tp = todayPath ?? DayStore.PathFor();
        var p = path ?? (todayPath != null ? PathBeside(todayPath) : PathFor());
        var today = (todayLocal ?? DateTime.Now).Date;
        SessionEngine.Carry? c = null;
        try { if (File.Exists(tp)) c = JsonSerializer.Deserialize<SessionEngine.Carry>(File.ReadAllText(tp), SessionEngine.CarryJson); }
        catch (Exception ex) { CrashLog.Write("days seed", ex); }
        lock (_gate)
        {
            var f = LoadLocked(p);
            bool changed = !File.Exists(p);
            if (c != null && ParseDay(c.Day) is { } cd && cd <= today && f.Get(c.Day) == null)
            {
                f.Days.Add(FromCarry(c, items));
                if (f.Since.Length == 0 || string.CompareOrdinal(c.Day, f.Since) < 0) f.Since = c.Day;
                changed = true;
            }
            if (f.Since.Length == 0) { f.Since = DayStore.DayOf(today); changed = true; }
            if (changed) SaveLocked(f, p, today);
        }
    }

    /// <summary>The run's start as a local day in <paramref name="tz"/> (this PC's by default); a run's times are kept in UTC.</summary>
    public static DateTime LocalStart(InstanceRun r, TimeZoneInfo? tz = null)
    {
        var utc = r.Start.Kind == DateTimeKind.Local ? r.Start.ToUniversalTime() : DateTime.SpecifyKind(r.Start, DateTimeKind.Utc);
        return TimeZoneInfo.ConvertTimeFromUtc(utc, tz ?? TimeZoneInfo.Local);
    }

    /// <summary>The kinds in the order the day's line says them.</summary>
    public static readonly string[] KindOrder = { "Expedition", InstanceLog.Transcendence, "Event", "Arena", "Legion", InstanceLog.WorldBoss };

    /// <summary>A run's kind as one of <see cref="KindOrder"/> whatever its case, or "" for any other.</summary>
    public static string KindOf(string kind) => KindOrder.FirstOrDefault(k => string.Equals(k, kind?.Trim(), StringComparison.OrdinalIgnoreCase)) ?? "";

    /// <summary>One day's runs: how many, the counted ones' Kinah, by kind and by name (most first).</summary>
    public static RunsDay RunsOf(IEnumerable<InstanceRun> runs)
    {
        var list = runs.ToList();
        if (list.Count == 0) return RunsDay.None;
        var counted = list.Where(r => !r.OutOfAverages).ToList();
        var kinds = list.GroupBy(r => KindOf(r.Kind)).Select(g => new RunKind(g.Key, g.Count()))
            .OrderBy(k => k.Kind.Length == 0 ? KindOrder.Length : Array.IndexOf(KindOrder, k.Kind)).ToList();
        var names = list.GroupBy(r => r.Event.Length > 0 ? r.Event : r.Zone, StringComparer.OrdinalIgnoreCase)
            .Select(g => new RunName(g.First().Event.Length > 0 ? g.First().Event : g.First().Zone, g.Count(), g.Where(r => !r.OutOfAverages).Sum(r => r.Kinah)))
            .OrderByDescending(n => n.Count).ThenByDescending(n => n.Kinah).ThenBy(n => n.Name, StringComparer.OrdinalIgnoreCase).ToList();
        return new RunsDay(list.Count, counted.Count, counted.Sum(r => r.Kinah), kinds, names);
    }

    /// <summary>The runs by the local day each started ("2026-10-27"), in <paramref name="tz"/> (this PC's by default).</summary>
    public static Dictionary<string, RunsDay> RunsByDay(IEnumerable<InstanceRun> runs, TimeZoneInfo? tz = null) =>
        runs.GroupBy(r => DayStore.DayOf(LocalStart(r, tz))).ToDictionary(g => g.Key, g => RunsOf(g));

    /// <summary>
    /// The last <paramref name="n"/> local days ending today, oldest first. Today's farming is live from
    /// <paramref name="today"/> (the day engine, read as LOOT's Today side reads it, so today's figure is that side's total);
    /// other days come from days.json (<paramref name="file"/>, or the file on disk), nothing for a day it lacks, and
    /// <see cref="HistoryDay.BeforeHistory"/> for a day before history began. Runs come from <paramref name="runs"/> (the
    /// run log), grouped by the local day each started; none when it is null.
    /// </summary>
    /// <param name="nowLocal">Now on this PC (a test's own day); <paramref name="tz"/> is the time zone runs are grouped in.</param>
    public static List<HistoryDay> Days(int n, SessionEngine? today, ItemDictionary items, IEnumerable<InstanceRun>? runs = null, DayFile? file = null,
        DateTime? nowLocal = null, TimeZoneInfo? tz = null)
    {
        var todayDate = (nowLocal ?? (tz != null ? TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz) : DateTime.Now)).Date;
        file ??= Current();
        // no history kept yet: it begins today
        var since = ParseDay(file.Since) ?? todayDate;
        var byDay = runs == null ? new Dictionary<string, RunsDay>() : RunsByDay(runs, tz);
        var list = new List<HistoryDay>(n);
        for (int i = n - 1; i >= 0; i--)
        {
            var date = todayDate.AddDays(-i);
            var key = DayStore.DayOf(date);
            bool isToday = i == 0;
            var kept = file.Get(key);
            var farm = isToday && today != null ? FromFarm(key, TodayLoot.Farm(today, items)) : kept ?? new FarmDay { Day = key };
            list.Add(new HistoryDay(date, key, isToday, !isToday && kept == null && date < since, farm, byDay.GetValueOrDefault(key) ?? RunsDay.None));
        }
        return list;
    }

    /// <summary>The days' totals: farming made, its time and kills; the runs, the counted ones and their Kinah.</summary>
    public static (long Made, double Active, int Kills, int Runs, int Counted, long RunKinah) Totals(IEnumerable<HistoryDay> days)
    {
        long made = 0, kinah = 0; double active = 0; int kills = 0, runs = 0, counted = 0;
        foreach (var d in days) { made += d.Farm.Made; active += d.Farm.Active; kills += d.Farm.Kills; runs += d.Runs.Runs; counted += d.Runs.Counted; kinah += d.Runs.Kinah; }
        return (made, active, kills, runs, counted, kinah);
    }

    /// <summary>The kinds of a day's runs in words: "11 expeditions · 3 events · 2 world bosses".</summary>
    public static string KindsLine(RunsDay d) => string.Join(" · ", d.Kinds.Select(k => $"{k.Count:N0} {KindWord(k.Kind, k.Count)}"));

    static string KindWord(string kind, int n) => kind switch
    {
        "Expedition" => n == 1 ? "expedition" : "expeditions",
        InstanceLog.Transcendence => n == 1 ? "Transcendence run" : "Transcendence runs",
        "Event" => n == 1 ? "event" : "events",
        "Arena" => n == 1 ? "arena" : "arenas",
        "Legion" => n == 1 ? "legion run" : "legion runs",
        InstanceLog.WorldBoss => n == 1 ? "world boss" : "world bosses",
        _ => n == 1 ? "run" : "runs",
    };
}
