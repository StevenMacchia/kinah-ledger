namespace KinahLedger.Session;

/// <summary>One farming zone over the week (the Week side's seven days): what it made, the time there, its kills, on how many days, its best drop.</summary>
public sealed record ZoneWeek(string Zone, long Made, double Active, int Kills, int Days, string BestDrop)
{
    /// <summary>Made an hour: Kinah plus loot after the Market's cut, over the time farming there.</summary>
    public double PerHour => Active > 0 ? Made / (Active / 3600) : 0;
    public double KillsHour => Active > 0 ? Kills / (Active / 3600) : 0;
    public double PerKill => Kills > 0 ? Made / (double)Kills : 0;
}

/// <summary>One expedition or event over the week: its runs, the counted ones' worth and time, the best run, the best drop.</summary>
public sealed record PlaceWeek(string Place, string Kind, int Runs, int Counted, long Value, double Seconds, long BestRun, string BestDrop)
{
    public double PerRun => Counted > 0 ? Value / (double)Counted : 0;
    public double PerHour => Seconds > 0 ? Value / (Seconds / 3600) : 0;
    public double MinutesARun => Counted > 0 ? Seconds / Counted / 60 : 0;
}

/// <summary>Your usual here (B): the hour over earlier days in this zone, today's hour here, and the best earlier day's hour (null with no day of ten minutes there).</summary>
public sealed record UsualHere(long Usual, long Today, long? Best, double TodayActive, double EarlierActive)
{
    public long Diff => Today - Usual;
}

/// <summary>A closed run against the earlier counted runs of its place (B): their average, this run, their best, and its rank among all of them.</summary>
public sealed record RunVsPlace(long Average, long This, long Best, int Rank, int Of)
{
    public long Diff => This - Average;
}

/// <summary>
/// Compare (0.9.3, kinah-bench Compare A and B, Steven 2026-10-31: "do A and B but make it so its more optional only for
/// people who REALLY care, base app should be light"). Options › General › Compare zones and runs, off by default: with it
/// on, the Week side of LOOT gets Days | Zones and the Events strip's Week side Days | Places (each zone or place ranked,
/// two picked side by side, one plain verdict line), the Farming card says your usual and best here, and a closed run's
/// card says how it stands against your earlier runs there. The figures come from days.json and the run log on this PC
/// (<see cref="DayHistory"/>): nothing here is sent anywhere, and no accuracy figure is shown. Apart from the window so
/// the self-test can check the ranking, the floors and the thresholds.
/// </summary>
public static class ZoneCompare
{
    /// <summary>A zone with less than this in the week is left out of Zones, so a pass through doesn't make noise (10 minutes).</summary>
    public const double ZoneFloorSeconds = 600;
    /// <summary>Your usual here shows after this much time in the zone today (10 minutes)...</summary>
    public const double UsualTodaySeconds = 600;
    /// <summary>...and this much there on earlier days (30 minutes).</summary>
    public const double UsualEarlierSeconds = 1800;
    /// <summary>An earlier day counts for "your best here" with this much time there (10 minutes).</summary>
    public const double BestDaySeconds = 600;
    /// <summary>A run is set against its place's average once the place has this many earlier counted runs.</summary>
    public const int RunEarlierMin = 3;
    /// <summary>Rows picked to compare, at most.</summary>
    public const int MaxPicks = 2;
    /// <summary>Two figures within this share of each other read as "about the same".</summary>
    public const double Close = 0.1;

    /// <summary>
    /// The week's zones, best made an hour first: every kept day's zones (days before history began add nothing), the
    /// unread zone and zones with under <see cref="ZoneFloorSeconds"/> left out. The best drop is the one worth most over
    /// the days that kept it ("" when none did).
    /// </summary>
    public static List<ZoneWeek> Zones(IEnumerable<HistoryDay> days)
    {
        var acc = new Dictionary<string, (long Made, double Active, int Kills, int Days, Dictionary<string, (long Worth, int Count)> Best)>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in days)
        {
            if (d.BeforeHistory) continue;
            foreach (var z in d.Farm.Zones)
            {
                if (z.Zone.Length == 0 || z.Zone == SessionEngine.UnknownZone) continue;
                (long Made, double Active, int Kills, int Days, Dictionary<string, (long Worth, int Count)> Best) a =
                    acc.TryGetValue(z.Zone, out var was) ? was : (0, 0, 0, 0, new Dictionary<string, (long Worth, int Count)>());
                a.Made += z.Made; a.Active += z.Active; a.Kills += z.Kills; if (z.Active > 0) a.Days++;
                if (z.Best.Length > 0) { var b = a.Best.GetValueOrDefault(z.Best); a.Best[z.Best] = (b.Item1 + z.BestWorth, b.Item2 + z.BestCount); }
                acc[z.Zone] = a;
            }
        }
        return acc.Where(kv => kv.Value.Active >= ZoneFloorSeconds)
            .Select(kv =>
            {
                var best = kv.Value.Best.OrderByDescending(b => b.Value.Worth).ThenByDescending(b => b.Value.Count).Select(b => $"{b.Key} ×{b.Value.Count:N0}").FirstOrDefault() ?? "";
                return new ZoneWeek(kv.Key, kv.Value.Made, kv.Value.Active, kv.Value.Kills, kv.Value.Days, best);
            })
            .OrderByDescending(z => z.PerHour).ThenByDescending(z => z.Active).ThenBy(z => z.Zone, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>A run's place as the week's Places name it: an event's own name, else where it was run (as <see cref="DayHistory.RunsOf"/>).</summary>
    public static string PlaceOf(InstanceRun r) => r.Event.Length > 0 ? r.Event : r.Zone;

    /// <summary>
    /// The week's places, best a run first: each place's runs, the counted ones' worth (<paramref name="value"/>, as the
    /// Events cards count it) and time, the best run, and its best drop (<paramref name="bestDrop"/>). A place with no
    /// counted run is left out: it has no figure to rank by.
    /// </summary>
    public static List<PlaceWeek> Places(IEnumerable<InstanceRun> runs, Func<InstanceRun, long> value, Func<IEnumerable<InstanceRun>, string>? bestDrop = null)
    {
        return runs.GroupBy(PlaceOf, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var counted = g.Where(r => !r.OutOfAverages).ToList();
                var vals = counted.Select(value).ToList();
                return new PlaceWeek(g.First().Event.Length > 0 ? g.First().Event : g.First().Zone, g.OrderByDescending(r => r.Start).First().Kind, g.Count(), counted.Count,
                    vals.Sum(), counted.Sum(r => r.Seconds), vals.DefaultIfEmpty(0).Max(), bestDrop?.Invoke(counted) ?? "");
            })
            .Where(p => p.Counted > 0)
            .OrderByDescending(p => p.PerRun).ThenByDescending(p => p.Counted).ThenBy(p => p.Place, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// The rows picked to compare after a click on <paramref name="key"/>'s tick: a picked one comes off; a new one goes
    /// on, the oldest pick making room when two are already picked.
    /// </summary>
    public static List<string> Toggle(List<string> picks, string key)
    {
        var l = picks.ToList();
        if (l.RemoveAll(p => string.Equals(p, key, StringComparison.OrdinalIgnoreCase)) > 0) return l;
        l.Add(key);
        while (l.Count > MaxPicks) l.RemoveAt(0);
        return l;
    }

    /// <summary>
    /// The picks as drawn: the ones still listed, at most two. Before the player has ticked anything (<paramref name="touched"/>
    /// false), the row here now is picked for them.
    /// </summary>
    public static List<string> Picks(List<string> picks, bool touched, IEnumerable<string> listed, string? here)
    {
        var names = listed.ToList();
        if (!touched) return here != null && names.Contains(here, StringComparer.OrdinalIgnoreCase) ? new() { names.First(n => string.Equals(n, here, StringComparison.OrdinalIgnoreCase)) } : new();
        return picks.Where(p => names.Contains(p, StringComparer.OrdinalIgnoreCase)).TakeLast(MaxPicks).ToList();
    }

    /// <summary>
    /// Your usual here, or null until it means something: <see cref="UsualTodaySeconds"/> in the zone today and
    /// <see cref="UsualEarlierSeconds"/> there on the earlier days (<paramref name="earlier"/>, today not among them).
    /// </summary>
    public static UsualHere? Usual(string zone, IEnumerable<FarmDay> earlier, long todayMade, double todayActive)
    {
        if (string.IsNullOrEmpty(zone) || zone == SessionEngine.UnknownZone || todayActive < UsualTodaySeconds) return null;
        long made = 0; double active = 0; double? best = null;
        foreach (var d in earlier)
            foreach (var z in d.Zones.Where(x => string.Equals(x.Zone, zone, StringComparison.OrdinalIgnoreCase)))
            {
                made += z.Made; active += z.Active;
                if (z.Active >= BestDaySeconds) { double r = z.Made / (z.Active / 3600); if (best == null || r > best) best = r; }
            }
        if (active < UsualEarlierSeconds) return null;
        return new UsualHere((long)Math.Round(made / (active / 3600)), (long)Math.Round(todayMade / (todayActive / 3600)), best is { } b ? (long)Math.Round(b) : null, todayActive, active);
    }

    /// <summary>
    /// A closed run against the earlier counted runs of its place, or null with fewer than <see cref="RunEarlierMin"/> of
    /// them. Rank 1 is the best of all of them, this one included.
    /// </summary>
    public static RunVsPlace? VsPlace(long thisValue, IEnumerable<long> earlierValues)
    {
        var e = earlierValues.ToList();
        if (e.Count < RunEarlierMin) return null;
        int rank = 1 + e.Count(v => v > thisValue);
        return new RunVsPlace((long)Math.Round(e.Average()), thisValue, e.Max(), rank, e.Count + 1);
    }

    /// <summary>"1st", "2nd", "3rd", "4th", "11th", "22nd".</summary>
    public static string Nth(int n)
    {
        int t = n % 100;
        string s = t is >= 11 and <= 13 ? "th" : (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" };
        return n.ToString("N0") + s;
    }

    /// <summary>How today stands against your usual: "35K more today", "12K less today", or "about the same today".</summary>
    public static (string Text, int Sign) TodayWords(UsualHere u, Func<double, string> fk)
    {
        if (u.Usual <= 0 || Math.Abs(u.Diff) < Math.Max(1_000, u.Usual * 0.03)) return ("about the same today", 0);
        return u.Diff > 0 ? ($"{fk(u.Diff)} more today", 1) : ($"{fk(-u.Diff)} less today", -1);
    }

    /// <summary>The verdict under two zones: (lead, bold part, rest), "Kraka Footpath made" "123K more an hour" "this week."</summary>
    public static (string Lead, string Bold, string Tail) ZoneVerdict(ZoneWeek a, ZoneWeek b, Func<double, string> fk)
    {
        var (hi, lo) = a.PerHour >= b.PerHour ? (a, b) : (b, a);
        if (hi.PerHour <= 0) return ("Neither made anything yet this week.", "", "");
        if (hi.PerHour - lo.PerHour < hi.PerHour * Close) return ($"Close: {hi.Zone} and {lo.Zone} made ", "about the same an hour", " this week.");
        var why = hi.KillsHour > lo.KillsHour * (1 + Close) ? ", mostly from more kills." : hi.PerKill > lo.PerKill * (1 + Close) ? ", from more a kill." : " this week.";
        return ($"{hi.Zone} made ", $"{fk(hi.PerHour - lo.PerHour)} more an hour", why);
    }

    /// <summary>The verdict under two places: more a run, and whether the quicker one makes up for it an hour.</summary>
    public static (string Lead, string Bold, string Tail) PlaceVerdict(PlaceWeek a, PlaceWeek b, Func<double, string> fk)
    {
        var (hi, lo) = a.PerRun >= b.PerRun ? (a, b) : (b, a);
        if (hi.PerRun <= 0) return ("Neither paid anything yet this week.", "", "");
        bool closeRun = hi.PerRun - lo.PerRun < hi.PerRun * Close;
        if (closeRun) return ($"Close: {hi.Place} and {lo.Place} pay ", "about the same a run", lo.PerHour > hi.PerHour * (1 + Close) ? $", and {lo.Place} is quicker, so it makes more an hour." : ".");
        string rest;
        if (lo.PerHour <= 0 || hi.PerHour <= 0 || hi.MinutesARun <= lo.MinutesARun) rest = ".";
        else if (lo.PerHour > hi.PerHour * (1 + Close)) rest = $", but {lo.Place} is quicker, so it makes more an hour.";
        else if (lo.PerHour >= hi.PerHour * (1 - Close)) rest = $", but {lo.Place} is quicker, so an hour comes out about even.";
        else rest = ".";
        return ($"{hi.Place} pays ", $"{fk(hi.PerRun - lo.PerRun)} more a run", rest);
    }
}
