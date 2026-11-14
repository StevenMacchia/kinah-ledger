namespace KinahLedger.Session;

/// <summary>
/// The Abyss's world bosses (0.10.85). They keep a fixed schedule, and the game draws their timers on the map from it, so
/// the timers here come from the same schedule (MetaBot's, as the site's /field-bosses page has it), counted on the game
/// server's clock.
///
/// The schedule's hours are the region's own wall clock. Steven's map of Chaotic Lower Reshanta on NA East (2026-11-14,
/// 06:38 Eastern) matched it to the second: Watcher Kaira every 3 hours from 01:00 (21min 20s left, so 07:00), the three
/// Executors Mon, Thu and Sat at 21:30 (14h 51min 20s), Guardian Lord Nahma, three spots, Sun and Fri at 21:00
/// (1d 14h 21min). NA East's clock is checked; the other regions' are taken as their own time zone until someone checks.
/// Middle Reshanta's bosses are on the same schedule as MetaBot gives it, not checked on the map yet.
/// </summary>
public static class AbyssBosses
{
    /// <summary>
    /// A boss on a schedule: every <paramref name="EveryMinutes"/> from <paramref name="AtMinutes"/> past midnight, or at
    /// <paramref name="AtMinutes"/> on <paramref name="Weekdays"/> (0 Sunday), region time. Spots: how many places it
    /// appears at once (Nahma: three). Checked: matched against the game's own map.
    /// </summary>
    public sealed record Boss(string Name, string Place, string Map, string Every, int EveryMinutes, int AtMinutes, int[]? Weekdays, int Spots, bool Checked);

    public const string Lower = "Lower Reshanta", Middle = "Middle Reshanta";
    static readonly int[] MonThuSat = { 1, 4, 6 }, SunFri = { 0, 5 };

    public static readonly Boss[] Schedule =
    {
        new("Watcher Kaira", "Black Fragments Area", Lower, "Every 3 hours", 180, 60, null, 1, true),
        new("Guardian Lord Nahma", "Reshanta's Root", Lower, "Sun and Fri", 1440, 21 * 60, SunFri, 3, true),
        new("Executor Tamasa", "Reshanta's Root", Lower, "Mon, Thu and Sat", 1440, 21 * 60 + 30, MonThuSat, 1, true),
        new("Executor Argo", "Siel's Heart", Lower, "Mon, Thu and Sat", 1440, 21 * 60 + 30, MonThuSat, 1, true),
        new("Executor Kaira", "Branding Island", Lower, "Mon, Thu and Sat", 1440, 21 * 60 + 30, MonThuSat, 1, true),
        new("Enraged Guardian Lord Nahma", "Steel Titan's Grave", Middle, "Sun and Fri", 1440, 21 * 60, SunFri, 1, false),
        new("Executioner Dramos", "Corroded Heart", Middle, "Mon, Thu and Sat", 1440, 21 * 60 + 30, MonThuSat, 1, false),
        new("Turncoat Ducal", "Eroded Fragment Area", Middle, "Mon, Thu and Sat", 1440, 21 * 60 + 30, MonThuSat, 1, false),
        new("Ravager Marakha", "Steel Titan's Grave", Middle, "Mon, Thu and Sat", 1440, 21 * 60 + 30, MonThuSat, 1, false),
    };

    /// <summary>A spawn counts as "Spawned" this long after its time: the map can't say when it is killed.</summary>
    public const int SpawnedMinutes = 30;

    /// <summary>The region's clock (Windows time zone ids), and whether it was checked against the game's map.</summary>
    public static (TimeZoneInfo Zone, bool Checked) ZoneFor(string? region)
    {
        string id = region switch
        {
            "na_w" => "Pacific Standard Time",
            "eu" => "W. Europe Standard Time",
            "latam" => "E. South America Standard Time",
            "asia" => "Korea Standard Time",
            _ => "Eastern Standard Time",
        };
        TimeZoneInfo zone;
        try { zone = TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException) { zone = TimeZoneInfo.CreateCustomTimeZone(id, TimeSpan.FromHours(-5), id, id); }
        return (zone, region is null or "" or "na_e");
    }

    /// <summary>A boss's row at a moment. SpawnPc: its spawn on this PC's clock (UTC); Spawned: its time passed under <see cref="SpawnedMinutes"/> ago.</summary>
    public sealed record Row(Boss Boss, bool Spawned, DateTime SpawnPc, TimeSpan Left, TimeSpan Since, string When);

    /// <summary>Every Abyss boss at <paramref name="nowPc"/> (UTC, this PC's clock), <paramref name="offset"/> this PC's clock minus the game's.</summary>
    public static List<Row> Rows(DateTime nowPc, TimeSpan offset, string? region)
    {
        var (zone, _) = ZoneFor(region);
        var server = nowPc - offset;
        return Schedule.Select(b => RowOf(b, server, offset, zone)).OrderBy(r => r.Spawned ? 0 : 1).ThenBy(r => r.Spawned ? -r.Since : r.Left).ToList();
    }

    public static Row RowOf(Boss b, DateTime serverUtc, TimeSpan offset, TimeZoneInfo zone)
    {
        var (last, next) = Around(b, serverUtc, zone);
        var since = serverUtc - last;
        if (since >= TimeSpan.Zero && since.TotalMinutes < SpawnedMinutes)
            return new Row(b, true, last + offset, TimeSpan.Zero, since, $"{BossTimers.Span(since)} ago");
        var left = next - serverUtc;
        string when = left.TotalHours >= 24 ? $"{(int)left.TotalDays}d {left.Hours}h {left.Minutes:00}m" : BossTimers.Countdown(left);
        return new Row(b, false, next + offset, left, TimeSpan.Zero, when);
    }

    /// <summary>The boss's last spawn at or before <paramref name="serverUtc"/> and its next after it (UTC, the game's clock), on <paramref name="zone"/>'s wall clock.</summary>
    public static (DateTime Last, DateTime Next) Around(Boss b, DateTime serverUtc, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(serverUtc, DateTimeKind.Utc), zone);
        DateTime last = DateTime.MinValue, next = DateTime.MaxValue;
        for (int d = -8; d <= 8; d++)
        {
            var day = local.Date.AddDays(d);
            if (b.Weekdays != null && !b.Weekdays.Contains((int)day.DayOfWeek)) continue;
            int step = b.Weekdays != null ? 1440 : b.EveryMinutes;
            for (int m = b.AtMinutes % step; m < 1440; m += step)
            {
                if (b.Weekdays != null && m != b.AtMinutes) continue;
                var s = day.AddMinutes(m);
                if (s <= local) { if (s > last) last = s; }
                else if (s < next) next = s;
            }
        }
        return (ToUtc(last, zone), ToUtc(next, zone));
    }

    static DateTime ToUtc(DateTime local, TimeZoneInfo zone)
    {
        if (local == DateTime.MinValue || local == DateTime.MaxValue) return DateTime.SpecifyKind(local, DateTimeKind.Utc);
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(local)) local = local.AddHours(1);   // the hour the clocks skip in spring
        return TimeZoneInfo.ConvertTimeToUtc(local, zone);
    }
}
