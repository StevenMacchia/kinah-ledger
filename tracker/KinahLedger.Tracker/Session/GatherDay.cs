using System.Text.Json;
using System.Text.Json.Serialization;

namespace KinahLedger.Session;

/// <summary>
/// gathers.json beside today.json: how many times this PC has gathered on each server today, against the game's daily
/// limit (Aion 2 patch notes, 2026-10-28: 1,000 gathers a server, reset daily at 12:00 AM PDT / 8:00 AM CET). The game's day
/// rolls at midnight Pacific time, not on this PC's calendar, so this keeps its own day key (DayStore's is local).
/// Every finished gathering race counts, a failed one too: the game counts attempts, and nothing in the notes says a
/// failed gather is free. Never sent anywhere.
/// </summary>
public static class GatherDay
{
    public const int Limit = 1000;
    /// <summary>The count at which the status line warns once.</summary>
    public const int WarnAt = 900;

    public sealed class State
    {
        [JsonPropertyName("day")] public string Day { get; set; } = "";
        [JsonPropertyName("servers")] public Dictionary<string, int> Servers { get; set; } = new();
        /// <summary>The warnings already said today, per server: 900 and 1000.</summary>
        [JsonPropertyName("said")] public Dictionary<string, List<int>> Said { get; set; } = new();
    }

    static readonly object Lock = new();
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    // the file read once per path and day, then kept: the window asks for the count on every redraw
    static State? _kept; static string? _keptPath;

    public static string PathFor() => Path.Combine(CrashLog.Dir, "gathers.json");

    static TimeZoneInfo Pacific()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time"); }
        catch { return TimeZoneInfo.CreateCustomTimeZone("UTC-8", TimeSpan.FromHours(-8), "UTC-8", "UTC-8"); }
    }

    static DateTime PacificOf(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Pacific());

    /// <summary>"2026-11-04": the date in Pacific time of <paramref name="utc"/>, the game's day.</summary>
    public static string DayKeyOf(DateTime utc) => PacificOf(utc).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>The next Pacific midnight after <paramref name="nowUtc"/>, as a local time for display.</summary>
    public static DateTime ResetsAt(DateTime nowUtc)
    {
        var tz = Pacific();
        var next = PacificOf(nowUtc).Date.AddDays(1);
        var utc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(next, DateTimeKind.Unspecified), tz);
        return utc.ToLocalTime();
    }

    static State Load(string? path, DateTime nowUtc)
    {
        var key = DayKeyOf(nowUtc);
        var p = path ?? PathFor();
        if (_kept != null && _keptPath == p && _kept.Day == key) return _kept;
        State fresh = new() { Day = key };
        try
        {
            if (File.Exists(p) && JsonSerializer.Deserialize<State>(File.ReadAllText(p)) is { } s && s.Day == key)
            {
                s.Servers ??= new(); s.Said ??= new();
                fresh = s;
            }
        }
        catch (Exception ex) { CrashLog.Write("gathers load", ex); }
        _kept = fresh; _keptPath = p;
        return fresh;
    }

    static void Save(State s, string? path)
    {
        try
        {
            var p = path ?? PathFor();
            var tmp = p + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(s, Json));
            File.Move(tmp, p, overwrite: true);
        }
        catch (Exception ex) { CrashLog.Write("gathers save", ex); }
    }

    /// <summary>Today's (Pacific) gathers on <paramref name="server"/> ("" when unknown).</summary>
    public static int Count(string server, DateTime? nowUtc = null, string? path = null)
    {
        lock (Lock) return Load(path, nowUtc ?? DateTime.UtcNow).Servers.GetValueOrDefault(server ?? "");
    }

    /// <summary>Counts one finished gather on <paramref name="server"/>; returns the new count.</summary>
    public static int Add(string server, DateTime nowUtc, string? path = null)
    {
        lock (Lock)
        {
            var s = Load(path, nowUtc);
            server ??= "";
            int n = s.Servers.GetValueOrDefault(server) + 1;
            s.Servers[server] = n;
            Save(s, path);
            return n;
        }
    }

    /// <summary>
    /// The warning to say now for <paramref name="server"/>, once a game day: at 900 and at the limit. Null when there is
    /// none or it was said already. Marks it said.
    /// </summary>
    public static string? Warning(string server, DateTime nowUtc, string? path = null)
    {
        lock (Lock)
        {
            var s = Load(path, nowUtc);
            server ??= "";
            int n = s.Servers.GetValueOrDefault(server);
            int level = n >= Limit ? Limit : n >= WarnAt ? WarnAt : 0;
            if (level == 0) return null;
            if (!s.Said.TryGetValue(server, out var said)) s.Said[server] = said = new();
            if (said.Contains(level)) return null;
            said.Add(level);
            Save(s, path);
            return level == Limit
                ? "Daily gathering limit reached (1,000 a server): gathers fail until 00:00 Pacific"
                : "900 of 1,000 gathers today: 100 left before the game's daily limit";
        }
    }

    /// <summary>"N of 1,000 gathers today · resets 00:00 Pacific (HH:mm here)", or "" while N is 0.</summary>
    public static string Line(string server, DateTime nowUtc, string? path = null)
    {
        int n = Count(server, nowUtc, path);
        if (n <= 0) return "";
        return $"{n:N0} of {Limit:N0} gathers today · resets 00:00 Pacific ({ResetsAt(nowUtc):HH:mm} here)";
    }
}
