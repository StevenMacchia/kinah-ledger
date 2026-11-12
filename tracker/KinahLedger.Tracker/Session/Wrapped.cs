using System.Globalization;
using KinahLedger.Data;

namespace KinahLedger.Session;

/// <summary>
/// An expedition or event on a recap sheet: its name as the run list writes it, its place as Compare's Places name it, its
/// kind, the local day it started, what it made (Kinah plus rewards, as the Events cards count a run), how long it took, and
/// whether it counts (a run left out of the averages adds nothing).
/// </summary>
public sealed record WrappedRun(string Name, string Place, string Kind, DateTime Day, long Value, double Seconds, bool Counted);

/// <summary>An item a recap sheet can call its biggest drop: its name and grade, how many, what they are worth after the Market's cut, where and on which day.</summary>
public sealed record WrappedDrop(string Name, string Grade, int Count, long Worth, string Where, DateTime Day);

/// <summary>Crafting on a recap sheet: the profit as the Crafting page sums it this session, the crafts, and whether a missing price makes it "at most".</summary>
public sealed record WrappedCraft(long Net, int Tries, bool AtMost);

/// <summary>
/// One period of a recap sheet: its days oldest first (from <see cref="DayHistory.Days"/>), the runs started in it, and the
/// runs' made as the view the sheet opens from sums it: Kinah plus rewards after the Market's cut (the TODAY strip's sum
/// for a day; the Events cards' made for a week, so no place or run on the sheet ever out-makes its total).
/// </summary>
public sealed record WrappedPeriod(IReadOnlyList<HistoryDay> Days, IReadOnlyList<WrappedRun> Runs, long RunsMade);

/// <summary>The sheet's top place: the farming zone that made most, or with no farming the expedition or event that did.</summary>
public sealed record WrappedPlace(string Name, long Made, double Seconds, int Runs, bool IsRun);

/// <summary>
/// What a recap sheet says, before it is written in words: the period, what was made farming and in runs, the time and the
/// rates, the days played of the period's days, the top place, the best run (or with none the best day), the biggest drop,
/// crafting, and a week against the week before (null for a day, whose evening isn't played yet, and when the week before
/// had no play or history didn't cover all of it).
/// </summary>
public sealed record WrappedFacts(string Scope, DateTime From, DateTime To, long Farmed, double FarmSeconds, int Kills, int Attempts,
    long FromRuns, int Runs, int Counted, double RunSeconds, int DaysPlayed, int Days, WrappedPlace? Top, WrappedRun? BestRun, HistoryDay? BestDay,
    WrappedDrop? Drop, WrappedCraft? Craft, long? Before, bool Thin)
{
    public long Total => Farmed + FromRuns;
    /// <summary>Made an hour farming, as the Week side writes it: farming's made over its time, once there is a minute of it.</summary>
    public double? PerHour => FarmSeconds >= 60 && Farmed > 0 ? Farmed / (FarmSeconds / 3600) : null;
    /// <summary>A counted run's average, as the Events strip's Week side writes it.</summary>
    public double? PerRun => Counted > 0 && FromRuns > 0 ? FromRuns / (double)Counted : null;
    /// <summary>The change against the period before, as a share of it (0.18 is 18% more); null with nothing to set it against.</summary>
    public double? Change => Before is long b && b > 0 ? (Total - b) / (double)b : null;
}

/// <summary>One of the sheet's figures in a tile: the figure, and the word under it.</summary>
public sealed record WrappedStat(string Fig, string Label);

/// <summary>One line of the sheet's highlights: its kind (zone, place, run, day, drop, craft), the label over it, the name, the figure on the right and the line under it.</summary>
public sealed record WrappedRow(string Kind, string Label, string Name, string Fig, string Sub);

/// <summary>
/// The sheet's words, as drawn: the title and the period, the total and what it is, the split between farming and runs,
/// the tiles, the highlights, a week's change against the week before (its tone 1 up, -1 down, 0 about the same) and, for a
/// thin sheet, what will appear and when.
/// </summary>
public sealed record WrappedText(string Title, string Range, string Total, string TotalLabel, string Split, IReadOnlyList<WrappedStat> Stats,
    IReadOnlyList<WrappedRow> Rows, string Change, string ChangeLabel, int Tone, string ThinHead, string ThinBody, string ThinSoFar);

/// <summary>
/// Week wrapped and Day wrapped (2026-11-11, Steven: the tracker "cool and flashy"): a recap sheet of the week or the day
/// for the character counted for, made to be shared. These are its figures and words, apart from the drawing
/// (Ui/MainWindow.Wrapped.cs) so the self-test can check them. The days come from <see cref="DayHistory.Days"/> and the
/// runs from the run log (each on the day it started), through the same sums the views use: farming as the Week side and
/// LOOT's Today side make it, runs as the TODAY strip sums them (Kinah plus rewards). With too little to wrap (a week of one day, a day of a few minutes) the sheet says what will appear and
/// when instead of a page of zeros. Display only, on this PC: nothing here is sent anywhere.
/// </summary>
public static class Wrapped
{
    public const string Week = "week", Day = "day";

    /// <summary>A week wraps once it has play on this many days: a week of one day is that day's sheet.</summary>
    public const int MinDays = 2;
    /// <summary>A day wraps after this much farming, or its first run: a few minutes would make a sheet of nothing much.</summary>
    public const double MinFarmSeconds = 600;
    /// <summary>A change under this share of the period before reads "about the same".</summary>
    public const double Same = 0.005;

    /// <summary>A day with play: farming kept for it, or a run started on it (runs are kept from before farming history began).</summary>
    static bool Played(HistoryDay d) => (!d.BeforeHistory && d.Farm.Any) || d.Runs.Runs > 0;

    /// <summary>
    /// The runs started on the local days <paramref name="first"/> to <paramref name="last"/> (in <paramref name="tz"/>, this
    /// PC's by default): a run belongs to the day it started, as on the Week side, so one past midnight is in one period only.
    /// </summary>
    public static List<InstanceRun> RunsIn(IEnumerable<InstanceRun> runs, DateTime first, DateTime last, TimeZoneInfo? tz = null) =>
        runs.Where(r => DayHistory.LocalStart(r, tz).Date is var d && d >= first.Date && d <= last.Date).ToList();

    /// <summary>
    /// The sheet's figures for <paramref name="now"/>; a week is set against <paramref name="before"/>, the week before it
    /// (a day is not: today so far against the whole of yesterday would read as a drop every morning).
    /// <paramref name="runDrops"/> are the period's run rewards with their worth; <paramref name="gradeOf"/> names a farmed
    /// item's grade; <paramref name="craft"/> is this session's crafting, or null with none.
    /// </summary>
    public static WrappedFacts Of(string scope, WrappedPeriod now, WrappedPeriod? before, IEnumerable<WrappedDrop> runDrops, Func<string, string> gradeOf, WrappedCraft? craft)
    {
        var days = now.Days;
        var kept = days.Where(d => !d.BeforeHistory).ToList();
        var t = DayHistory.Totals(kept);
        var counted = now.Runs.Where(r => r.Counted).ToList();
        int played = days.Count(Played);

        // the top place: the zone that made most this period (Compare's zones: ten minutes or more there), else the run place that did
        WrappedPlace? top = ZoneCompare.Zones(days).Where(z => z.Made > 0).OrderByDescending(z => z.Made).ThenByDescending(z => z.Active)
            .Select(z => new WrappedPlace(z.Zone, z.Made, z.Active, 0, false)).FirstOrDefault();
        top ??= counted.GroupBy(r => r.Place, StringComparer.OrdinalIgnoreCase)
            .Select(g => new WrappedPlace(g.First().Place, g.Sum(r => r.Value), g.Sum(r => r.Seconds), g.Count(), true))
            .Where(p => p.Made > 0).OrderByDescending(p => p.Made).ThenByDescending(p => p.Runs).FirstOrDefault();

        var best = counted.Where(r => r.Value > 0).OrderByDescending(r => r.Value).ThenBy(r => r.Day).FirstOrDefault();
        // with no run to name, a week of two days or more names its best day of farming
        var bestDay = best == null && scope == Week && played >= MinDays
            ? kept.Where(d => d.Farm.Made > 0).OrderByDescending(d => d.Farm.Made).ThenBy(d => d.Date).FirstOrDefault() : null;

        // the biggest drop: each zone's best drop of each day (days.json keeps one a zone), and each counted run's rewards
        var drops = kept.SelectMany(d => d.Farm.Zones.Where(z => z.Best.Length > 0 && z.BestWorth > 0)
                .Select(z => new WrappedDrop(z.Best, gradeOf(z.Best), z.BestCount, z.BestWorth, z.Zone, d.Date)))
            .Concat(runDrops.Where(x => x.Worth > 0));
        var drop = drops.OrderByDescending(x => x.Worth).ThenByDescending(x => SharePicture.GradeRank(x.Grade)).ThenBy(x => x.Name, StringComparer.Ordinal).FirstOrDefault();

        long? was = null;
        if (scope == Week && before != null && before.Days.Count > 0 && before.Days.All(d => !d.BeforeHistory) && (before.Days.Any(Played) || before.Runs.Count > 0))
        {
            long b = DayHistory.Totals(before.Days).Made + before.RunsMade;
            if (b > 0) was = b;
        }
        var f = new WrappedFacts(scope, days[0].Date, days[^1].Date, t.Made, t.Active, t.Kills, kept.Sum(d => d.Farm.Attempts),
            now.RunsMade, now.Runs.Count, counted.Count, counted.Sum(r => r.Seconds), played, days.Count, top, best, bestDay, drop,
            craft is { Tries: > 0 } ? craft : null, was, false);
        bool thin = f.Total <= 0 || (scope == Week ? played < MinDays : f.FarmSeconds < MinFarmSeconds && f.Runs == 0);
        return f with { Thin = thin };
    }

    /// <summary>"Thu 5 to Wed 11 Nov", "Fri 30 Oct to Thu 5 Nov"; a day, "Wed 11 Nov".</summary>
    public static string Range(DateTime from, DateTime to)
    {
        var c = CultureInfo.InvariantCulture;
        if (from.Date == to.Date) return to.ToString("ddd d MMM", c);
        return from.Month == to.Month ? $"{from.ToString("ddd d", c)} to {to.ToString("ddd d MMM", c)}" : $"{from.ToString("ddd d MMM", c)} to {to.ToString("ddd d MMM", c)}";
    }

    static string Plural(int n, string one, string many) => $"{n.ToString("N0", CultureInfo.InvariantCulture)} {(n == 1 ? one : many)}";

    /// <summary>
    /// The sheet's words. <paramref name="fk"/> writes a figure the way the window does ("224K", "1.20M"). A figure the
    /// period has none of gets no tile and no line, so the sheet never shows a zero or a dash; and none says the total again
    /// (one run's average, a place of one run, a line that made the whole total keeps its name and loses its figure).
    /// </summary>
    public static WrappedText Text(WrappedFacts f, Func<double, string> fk)
    {
        bool week = f.Scope == Week;
        var c = CultureInfo.InvariantCulture;
        // the split only when both made something: alone, it would say the total again
        bool both = f.Farmed > 0 && f.FromRuns > 0;
        // a line's figure, unless it is the total or its side of the split again (the line keeps its name and loses its figure)
        string Fig(long v, long? side = null) => v == f.Total || (both && v == side) ? "" : fk(v);
        var split = both ? $"{fk(f.Farmed)} farming · {fk(f.FromRuns)} from {Plural(f.Runs, "run", "runs")}" : "";

        var stats = new List<WrappedStat>();
        if (f.FarmSeconds >= 60) stats.Add(new(TodayLoot.Dur(f.FarmSeconds), f.Attempts > f.Kills ? "gathering" : "farming"));
        else if (f.RunSeconds >= 60) stats.Add(new(TodayLoot.Dur(f.RunSeconds), "in runs"));
        if (f.PerHour is double h) stats.Add(new(fk(h), "an hour farming"));
        else if (f.PerRun is double pr && f.Counted > 1) stats.Add(new(fk(pr), "a run"));
        if (week) stats.Add(new($"{f.DaysPlayed} of {f.Days}", "days played"));
        else if (f.Kills > 0) stats.Add(new(f.Kills.ToString("N0", c), f.Kills == 1 ? "kill" : "kills"));
        else if (f.Attempts > 0) stats.Add(new(f.Attempts.ToString("N0", c), f.Attempts == 1 ? "gathering attempt" : "gathering attempts"));
        else if (f.Runs > 0) stats.Add(new(f.Runs.ToString("N0", c), f.Runs == 1 ? "run" : "runs"));

        var rows = new List<WrappedRow>();
        // a place of one run is that run, named below as the best
        if (f.Top is { } top && !(top.IsRun && top.Runs == 1))
            rows.Add(top.IsRun
                ? new("place", "TOP PLACE", top.Name, Fig(top.Made, f.FromRuns), Plural(top.Runs, "run", "runs") + (top.Seconds >= 60 ? " · " + TodayLoot.Dur(top.Seconds) : ""))
                : new("zone", "TOP ZONE", top.Name, Fig(top.Made, f.Farmed), TodayLoot.Dur(top.Seconds) + (top.Seconds >= 60 ? $" · {fk(top.Made / (top.Seconds / 3600))} an hour" : "")));
        if (f.BestRun is { } r)
        {
            var when = week ? " · " + r.Day.ToString("ddd d", c) : "";
            rows.Add(new("run", f.Counted >= 2 ? "BEST RUN" : "THE RUN", r.Name, Fig(r.Value, f.FromRuns), (r.Kind.Length > 0 ? r.Kind : "Run") + when + (r.Seconds >= 60 ? " · " + TodayLoot.Dur(r.Seconds) : "")));
        }
        else if (f.BestDay is { } d)
            rows.Add(new("day", "BEST DAY", d.Date.ToString("dddd d MMM", c), Fig(d.Farm.Made, f.Farmed), TodayLoot.Dur(d.Farm.Active) + " farming"));
        if (f.Drop is { } x)
            rows.Add(new("drop", "BIGGEST DROP", x.Name, Fig(x.Worth), $"×{x.Count.ToString("N0", c)} · {x.Where}" + (week ? " · " + x.Day.ToString("ddd d", c) : "")));
        if (f.Craft is { } cr)
            rows.Add(new("craft", "CRAFTING", $"{Plural(cr.Tries, "craft", "crafts")} this session", (cr.AtMost ? "≤ " : "") + (cr.Net >= 0 ? "+" : "−") + fk(Math.Abs(cr.Net)),
                "profit, " + PriceBook.Cut + (cr.AtMost ? ", at most (a price is missing)" : "")));

        string change = "", changeLabel = ""; int tone = 0;
        if (f.Change is double ch && f.Before is long b)
        {
            changeLabel = "vs last week, " + fk(b);
            if (Math.Abs(ch) < Same) change = "about the same";
            // twice the period before or more reads as a multiple ("▲ 7×"), not a four-figure share
            else if (ch >= 1) { tone = 1; change = $"▲ {(1 + ch).ToString("0.#", c)}×"; }
            else { tone = ch > 0 ? 1 : -1; change = $"{(ch > 0 ? "▲" : "▼")} {Math.Abs(ch * 100).ToString(Math.Abs(ch) < 0.095 ? "0.#" : "N0", c)}%"; }
        }

        // a thin sheet: what will appear and when, and what there is so far
        string thinHead = "", thinBody = "", soFar = "";
        if (f.Thin)
        {
            thinHead = week ? "Your week wraps up after two days of play." : "Your day wraps up after 10 minutes of farming or your first run.";
            thinBody = $"Then this sheet shows what you made, your time and Kinah an hour, your top zone, best run and biggest drop{(week ? ", and how the week did against the last one" : "")}, ready to share.";
            var bits = new List<string>();
            if (f.FarmSeconds >= 60) bits.Add(TodayLoot.Dur(f.FarmSeconds) + " farming");
            if (f.Runs > 0) bits.Add(Plural(f.Runs, "run", "runs"));
            if (f.Total > 0) bits.Add(fk(f.Total) + " made");
            soFar = week
                ? f.DaysPlayed == 0 ? "Nothing counted on this character yet this week." : $"So far: {Plural(f.DaysPlayed, "day", "days")} of play{(bits.Count > 0 ? ", " + string.Join(", ", bits) : "")}. Play on another day this week and it fills in."
                : bits.Count == 0 ? "Nothing counted yet today." : "So far today: " + string.Join(", ", bits) + ".";
        }
        return new WrappedText(week ? "WEEK WRAPPED" : "DAY WRAPPED", Range(f.From, f.To), fk(f.Total), week ? "made this week" : "made today", split, stats, rows,
            change, changeLabel, tone, thinHead, thinBody, soFar);
    }

    /// <summary>Where Save picture puts a sheet: Pictures\Kinah Ledger\kinah-ledger-2026-11-11-2241-week-wrapped.png.</summary>
    public static string FileName(string scope, DateTime local) => SharePicture.FileName(scope + "-wrapped", local);
}
