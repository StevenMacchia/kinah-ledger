using KinahLedger.Packets;

namespace KinahLedger.Session;

/// <summary>
/// Timed event timers (0.10.83): when the game's scheduled events start next, and the event the player is in, part by part.
///
/// The game doesn't tell a player who isn't at an event anything about it, so the countdowns are the known schedule
/// (<see cref="Schedule"/>, the same rules as the site's /events/ page: kinah-ledger-site data/events.json) counted on the game
/// server's own clock. Two things set that clock, kept apart:
/// <list type="bullet">
/// <item>its time zone, per region (<see cref="ClockOf"/>): the schedule's times are wall-clock times in the region's own zone,
/// with its daylight saving time (NA East: US Eastern; checked against the game's own messages and Steven's map, 2026-11-14).
/// Until 0.10.85 every region was counted on UTC+9 (MetaBot's reading), which put NA East's Artifact Siege at 07:00 instead of
/// 21:00 and its Spacetime Rift an hour early. Regions not checked yet keep UTC+9, and the card says so;</item>
/// <item>its gap to this PC's clock (about 15 s behind on 2026-11-13), taken from the game's traffic (<see cref="TakeClock"/>),
/// so the countdown can't be off by it. With packet reading off, this PC's clock is used, and the card says so.</item>
/// </list>
///
/// The event the player is in comes from the event's own server (<see cref="Take"/>): getting ready, under way, over, left.
/// </summary>
public sealed class EventTimers
{
    /// <summary>The game's clock is live while a sample is this recent (the decoder sends one a minute).</summary>
    public const double ClockLiveSeconds = 180;
    /// <summary>An event the player left, or that is over, still shows this long.</summary>
    public const double KeepLiveSeconds = 120;
    /// <summary>An event part no message came for in this long is taken as left (the reading stopped, the game closed).</summary>
    public const double StaleLiveMinutes = 15;
    /// <summary>
    /// The clock a region's schedule runs on: its time zone (daylight saving included), whether that was checked against the
    /// game, and what to say about it.
    /// </summary>
    public sealed record RegionClock(string Region, string Name, TimeZoneInfo Zone, bool Checked, string Note);

    /// <summary>MetaBot's reading for every region (not checked in the game): one clock at UTC+9, no daylight saving.</summary>
    static readonly TimeZoneInfo Utc9 = TimeZoneInfo.CreateCustomTimeZone("UTC+9", TimeSpan.FromHours(9), "UTC+9", "UTC+9");

    /// <summary>
    /// The clock of a region (Settings.Region: "na_e", "na_w", "eu", "latam", "asia"). NA East is US Eastern, checked
    /// 2026-11-14: the Abyss server's own next-siege time (Sat 21:00 ET), the chat's "A Spacetime Rift has appeared" at 11:00,
    /// 14:00 and 17:00 ET on 2026-11-13, and the field bosses on Steven's Lower Reshanta map (Watcher Kaira every 3 h from
    /// 01:00 ET). The other regions are not checked: UTC+9, as before (Europe is likely Central
    /// European time, but no reading tells it apart yet).
    /// </summary>
    public static RegionClock ClockOf(string? region) => region switch
    {
        "na_e" => new("na_e", "NA East", Zone("Eastern Standard Time", "America/New_York") ?? Utc9, true, "US Eastern time"),
        "na_w" => new("na_w", "NA West", Utc9, false, "UTC+9, not checked for NA West yet"),
        "eu" => new("eu", "Europe", Utc9, false, "UTC+9, not checked for Europe yet"),
        "latam" => new("latam", "Latin America", Utc9, false, "UTC+9, not checked for Latin America yet"),
        "asia" => new("asia", "Asia", Utc9, false, "UTC+9, not checked for Asia yet"),
        _ => new(region ?? "", "this region", Utc9, false, "UTC+9, not checked yet"),
    };

    static TimeZoneInfo? Zone(params string[] ids)
    {
        foreach (var id in ids)
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException) { }
        }
        return null;
    }

    /// <summary>
    /// A recurring event in the region's wall-clock time: every <paramref name="EveryMinutes"/> from <paramref name="AtMinutes"/>
    /// past midnight, or once a day at <paramref name="AtMinutes"/> on <paramref name="Weekdays"/> (0 Sunday). OnMinutes: how
    /// long it counts as on after its start. AtIn: a region whose first start of the day differs (NA East's Rift, 02:00).
    /// </summary>
    public sealed record Recurring(string Name, string Every, int EveryMinutes, int AtMinutes, int[]? Weekdays, int OnMinutes, string Where, IReadOnlyDictionary<string, int>? AtIn = null)
    {
        public int AtFor(string region) => AtIn != null && AtIn.TryGetValue(region, out var at) ? at : AtMinutes;
    }

    /// <summary>
    /// The schedule, as kinah-ledger-site data/events.json has it ("recurring"; keep the two in step). How long each counts as
    /// on: the Shugo Festival's rounds were over by :05 in the game's own messages (2026-11-13, 17:01 and 18:01); the
    /// Dimensional Invasion is a 3-minute lobby and up to 10 minutes of waves; the Artifact Siege a 10-minute warning, then the
    /// cores; the Spacetime Rift's portal is given 10 minutes. The Rift in NA East: every 3 hours from 02:00 Eastern (the chat's
    /// "A Spacetime Rift has appeared" at 11:00, 14:00, 17:00 ET on 2026-11-13; the same grid in summer time as aion2-data's
    /// 00:00 UTC anchor of 2026-10-07), not from 00:00.
    /// </summary>
    public static readonly Recurring[] Schedule =
    {
        new("Shugo Festival", "Every hour", 60, 0, null, 6, "Verteron and Altgard"),
        new("Dimensional Invasion", "Every hour at half past", 60, 30, null, 13, "Verteron and Altgard"),
        new("Spacetime Rift", "Every 3 hours", 180, 0, null, 10, "One of ten zones", new Dictionary<string, int> { ["na_e"] = 120 }),
        new("Artifact Siege", "Thu and Sat", 1440, 21 * 60, new[] { 4, 6 }, 20, "Chaotic Lower Reshanta"),   // twice a week since the Nov 4 patch
    };

    /// <summary>The event the player is in, as its server told it. Times are UTC on this PC's clock.</summary>
    public sealed record Live(string Family, string Name, AreaEvent.Stage Stage, DateTime? StartedPc, DateTime SeenAt, DateTime? OverAt, DateTime? LeftAt)
    {
        public string Label => Name.Length > 0 ? Name : Family.Length > 0 ? Family : "An event";
    }

    /// <summary>A scheduled event's row at a moment: on now (since when) or its next start (in how long). Times UTC, this PC's clock.</summary>
    public sealed record Row(Recurring Event, bool OnNow, DateTime StartPc, TimeSpan Left, TimeSpan Since, string When);

    readonly object _gate = new();
    TimeSpan _offset;            // this PC's clock minus the server's
    DateTime _clockAt = DateTime.MinValue;
    Live? _live;

    /// <summary>True once the game's clock was read; false while the countdown runs on this PC's clock.</summary>
    public bool ClockKnown { get { lock (_gate) return _clockAt != DateTime.MinValue; } }
    /// <summary>This PC's clock minus the game server's (zero until the traffic said).</summary>
    public TimeSpan Offset { get { lock (_gate) return _offset; } }

    /// <summary>The game's clock, sampled: <paramref name="serverNow"/> at <paramref name="at"/> (this PC's, UTC).</summary>
    public void TakeClock(DateTime at, DateTime serverNow)
    {
        lock (_gate) { _offset = at - serverNow; _clockAt = at; }
    }

    /// <summary>"Game clock, live" / "Game clock, read 2 h ago" / "This PC's clock (packet reading is off)".</summary>
    public string ClockLine(DateTime now)
    {
        lock (_gate)
        {
            if (_clockAt == DateTime.MinValue) return "On this PC's clock: the game's own clock comes with packet reading";
            double gap = _offset.TotalSeconds;
            string g = Math.Abs(gap) < 1 ? "" : $" ({Math.Abs(gap):0} s {(gap > 0 ? "behind" : "ahead of")} this PC's)";
            return (now - _clockAt).TotalSeconds <= ClockLiveSeconds ? $"On the game's clock{g}" : $"On the game's clock as last read{g}, {BossTimers.Ago(now - _clockAt)}";
        }
    }

    /// <summary>What the game's traffic said about events: the clock, and the event the player is in.</summary>
    public void Take(AreaEvent e)
    {
        lock (_gate)
        {
            if (e.ServerNow is { } sv) { _offset = e.At - sv; _clockAt = e.At; }
            switch (e.What)
            {
                case AreaEvent.Kind.Phase:
                    {
                        var started = e.PhaseStart is { } ps ? ps + _offset : (DateTime?)null;
                        // the same event: its next part (getting ready, then under way); a name known now is kept
                        if (_live is { LeftAt: null } l && (Same(l, e) || (l.Family.Length > 0 && l.Family == e.Family)))
                            _live = l with { Name = e.Name.Length > 0 ? e.Name : l.Name, Stage = e.Phase, StartedPc = started ?? l.StartedPc, SeenAt = e.At, OverAt = e.Phase == AreaEvent.Stage.Over ? e.At : l.OverAt };
                        else _live = new Live(e.Family, e.Name, e.Phase, started, e.At, e.Phase == AreaEvent.Stage.Over ? e.At : null, null);
                        break;
                    }
                case AreaEvent.Kind.Over:
                    if (_live is { } o) _live = o with { Stage = AreaEvent.Stage.Over, OverAt = o.OverAt ?? e.At, SeenAt = e.At };
                    else _live = new Live(e.Family, e.Name, AreaEvent.Stage.Over, null, e.At, e.At, null);
                    break;
                case AreaEvent.Kind.Closed:
                    // a part getting ready closing as the event begins is no leaving: only the event's own part, or a move
                    if (_live is { LeftAt: null } c && (e.ServerMoved || e.Phase != AreaEvent.Stage.Preparing || c.Stage == AreaEvent.Stage.Over))
                        _live = c with { LeftAt = e.At, SeenAt = e.At };
                    break;
            }
        }
    }

    static bool Same(Live l, AreaEvent e) => string.Equals(l.Family, e.Family, StringComparison.OrdinalIgnoreCase) && string.Equals(l.Name, e.Name, StringComparison.OrdinalIgnoreCase);

    /// <summary>The event the player is in (or just left), null when none.</summary>
    public Live? Current(DateTime now)
    {
        lock (_gate)
        {
            if (_live is not { } l) return null;
            if (l.LeftAt is { } left && (now - left).TotalSeconds > KeepLiveSeconds) return null;
            if (l.LeftAt == null && (now - l.SeenAt).TotalMinutes > StaleLiveMinutes) return null;
            return l;
        }
    }

    /// <summary>Every scheduled event at <paramref name="now"/> (UTC, this PC's clock) in <paramref name="region"/>: on now first, then the soonest.</summary>
    public List<Row> Rows(DateTime now, string? region)
    {
        TimeSpan off; lock (_gate) off = _offset;
        var server = now - off;
        var clock = ClockOf(region);
        return Schedule.Select(r => RowOf(r, server, off, clock)).OrderBy(r => r.OnNow ? 0 : 1).ThenBy(r => r.OnNow ? -r.Since : r.Left).ToList();
    }

    /// <summary>One event's row at <paramref name="serverUtc"/> (the game's clock) in a region, its times put back on this PC's by <paramref name="offset"/>.</summary>
    public static Row RowOf(Recurring r, DateTime serverUtc, TimeSpan offset, RegionClock clock)
    {
        var (last, next) = Around(r, serverUtc, clock);
        var since = serverUtc - last;
        if (since >= TimeSpan.Zero && since.TotalMinutes < r.OnMinutes)
            return new Row(r, true, last + offset, TimeSpan.Zero, since, $"started {BossTimers.Span(since)} ago");
        var left = next - serverUtc;
        // a day or more off (the Artifact Siege): days and hours, the seconds ticking there would only be noise
        string when = left.TotalHours >= 24 ? $"{(int)left.TotalDays}d {left.Hours}h {left.Minutes:00}m" : BossTimers.Countdown(left);
        return new Row(r, false, next + offset, left, TimeSpan.Zero, when);
    }

    /// <summary>
    /// The event's last start at or before <paramref name="serverUtc"/>, and its next start after it (UTC, the game's clock):
    /// its starts are wall-clock times in the region's zone, day by day, so a daylight saving change moves them with the region.
    /// A start that falls in the hour skipped in spring is taken an hour on; one in the hour repeated in autumn, the first time.
    /// </summary>
    public static (DateTime Last, DateTime Next) Around(Recurring r, DateTime serverUtc, RegionClock clock)
    {
        var utc = DateTime.SpecifyKind(serverUtc, DateTimeKind.Utc);
        var today = TimeZoneInfo.ConvertTimeFromUtc(utc, clock.Zone).Date;
        int at = r.AtFor(clock.Region);
        DateTime last = DateTime.MinValue, next = DateTime.MaxValue;
        int span = r.Weekdays == null ? 1 : 7;
        for (int d = -span; d <= span; d++)
        {
            var day = today.AddDays(d);
            if (r.Weekdays != null && !r.Weekdays.Contains((int)day.DayOfWeek)) continue;
            for (int m = at; m < 1440; m += r.EveryMinutes)
            {
                var s = ToUtc(day.AddMinutes(m), clock.Zone);
                if (s <= utc) { if (s > last) last = s; }
                else if (s < next) next = s;
            }
        }
        return (last, next);
    }

    static DateTime ToUtc(DateTime wall, TimeZoneInfo zone)
    {
        wall = DateTime.SpecifyKind(wall, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(wall)) wall = wall.AddHours(1);   // spring forward: the hour doesn't exist
        if (zone.IsAmbiguousTime(wall))                           // autumn: the hour comes twice; the first (daylight) time
        {
            var offs = zone.GetAmbiguousTimeOffsets(wall);
            var off = offs.Max();
            return DateTime.SpecifyKind(wall - off, DateTimeKind.Utc);
        }
        return TimeZoneInfo.ConvertTimeToUtc(wall, zone);
    }

    /// <summary>What the event the player is in says now: "Getting ready" / "Under way" / "Over: rewards next" / "Left".</summary>
    public static string StageText(Live l) => l.LeftAt != null ? "Left" : l.Stage switch
    {
        AreaEvent.Stage.Preparing => "Getting ready",
        AreaEvent.Stage.UnderWay => "Under way",
        AreaEvent.Stage.Over => "Over: rewards next",
        _ => "On",
    };

    /// <summary>For renders and tests: the clock and the event set by hand.</summary>
    internal void SetForTest(TimeSpan offset, bool clockKnown, Live? live)
    {
        lock (_gate) { _offset = offset; _clockAt = clockKnown ? DateTime.UtcNow : DateTime.MinValue; _live = live; }
    }
}
