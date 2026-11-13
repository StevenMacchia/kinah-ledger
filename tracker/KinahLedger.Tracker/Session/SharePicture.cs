using KinahLedger.Data;
using System.Globalization;

namespace KinahLedger.Session;

/// <summary>One item on the Share picture: its name, grade, how many, and its worth at the lowest Market price (0 with none); <see cref="Bound"/> of the copies can't be sold.</summary>
public sealed record ShareDrop(string Name, string Grade, int Count, long Worth)
{
    public int Bound { get; init; }
}

/// <summary>A farming zone of the day on the Today picture: what it made and how long.</summary>
public sealed record ShareZone(string Name, long Made, double Seconds);

/// <summary>A day of the week on the This week picture: what it made.</summary>
public sealed record ShareDay(DateTime Date, long Made, bool IsToday);

/// <summary>
/// What a Share picture is drawn from, for one of its sides (This session, Today, Last run, This week): the day, the server,
/// the title (the zone, the run's name, or the week), what it was and for how long, how many kills (or attempts, or runs),
/// what it made after the Market's cut, and the items picked up. <paramref name="Note"/> stands in the drops' place when
/// there are no items to show (the week, which keeps no items). <see cref="Kinah"/> is the loose Kinah in the total (-1
/// when not known), <see cref="Zones"/> the day's zones (Today), <see cref="Days"/> the week's days.
/// </summary>
public sealed record ShareFacts(string Scope, DateTime Day, string Server, string Title, string What, double Seconds, int Count, string CountWord,
    long Made, IReadOnlyList<ShareDrop> Drops, string Note = "")
{
    public long Kinah { get; init; } = -1;
    public IReadOnlyList<ShareZone> Zones { get; init; } = Array.Empty<ShareZone>();
    public IReadOnlyList<ShareDay> Days { get; init; } = Array.Empty<ShareDay>();
}

/// <summary>A small figure under the total: "1.27M" over "Kinah an hour".</summary>
public sealed record ShareStat(string Fig, string Label);

/// <summary>The total's two parts: loose Kinah and the loot's worth, and the loot's share of the total (0..1).</summary>
public sealed record ShareSplit(string Kinah, string Loot, double LootShare);

/// <summary>
/// A line of the picture's panel: a drop (its icon, its name in its grade, "×30", its worth) or a day of the week. <see cref="Bar"/>
/// is its worth against the panel's biggest (0..1); <see cref="Best"/> marks the best drop or the best day; <see cref="Dim"/>
/// a line worth nothing on the picture (no price yet, bound, a day not farmed).
/// </summary>
public sealed record ShareRow(string Name, string Grade, string Count, string Fig, double Bar, bool Item, bool Best, bool Dim);

/// <summary>The picture's words, as drawn: the head line, the title and the line under it, the two figures, the icons and the best drop.</summary>
public sealed record SharePic(string Head, string Title, string Sub, string Rate, string RateLabel, string Made, string MadeLabel,
    IReadOnlyList<ShareDrop> Icons, string Best, string Site)
{
    /// <summary>"TODAY · FARMING", "LAST RUN · EXPEDITION": over the title.</summary>
    public string Kicker { get; init; } = "";
    /// <summary>The rate, the time and the kills (or attempts), under the total.</summary>
    public IReadOnlyList<ShareStat> Stats { get; init; } = Array.Empty<ShareStat>();
    /// <summary>Loose Kinah against the loot's worth, when the side knows its Kinah.</summary>
    public ShareSplit? Split { get; init; }
    /// <summary>The panel: its heading ("TOP DROPS", "BY DAY"), what it says on the right ("top 5 of 12 kinds"), its lines.</summary>
    public string RowsHead { get; init; } = "";
    public string RowsSide { get; init; } = "";
    public IReadOnlyList<ShareRow> Rows { get; init; } = Array.Empty<ShareRow>();
    /// <summary>Today's zones, most made first ("Western Cantas Valley 632K · Altamia 210K"), or nothing with one zone.</summary>
    public string Zones { get; init; } = "";
    /// <summary>The foot line when there are no zones to list: the best drop with its count and grade, or the week's best day and zone.</summary>
    public string BestLine { get; init; } = "";
}

/// <summary>
/// Share (0.9.3, ShareA of the canvas, Steven's pick 2026-10-31; richer in 0.10.67, Steven 2026-11-13: "the shareable daily
/// farming picture needs to be way better and more informative"): a 1200×630 picture of a session, today's farming, the
/// last run or the week, to paste in Discord or anywhere. On it: the total after the Market's cut, split into loose Kinah
/// and loot; the rate, the time and the kills; a panel of the top drops (icon, count, worth, a bar against the biggest) or,
/// for the week, a bar a day; and today's zones or the best drop at the foot. This is its text, apart from the drawing
/// (Ui/MainWindow.Share.cs), so the self-test can check it: the character name only when the player turned it on, the
/// figures for each side, the best drop. No accuracy figures and no "to look at" counts, ever. Nothing is uploaded.
/// </summary>
public static class SharePicture
{
    public const string Session = "session", Today = "today", Run = "run", Week = "week";
    public const string Site = "kinahledger.com";
    public const string RateLabel = "Kinah an hour";
    /// <summary>"made, after the Market's cut", or "before" it while Options shows amounts before the fee.</summary>
    public static string MadeLabel => "made, " + PriceBook.Cut;
    /// <summary>The one line under the sheet's buttons.</summary>
    public const string Note = "Paste it in Discord or anywhere. Nothing is uploaded.";
    public const int Width = 1200, Height = 630;
    /// <summary>The panel's drops: as many as fit beside the figures.</summary>
    public const int TopDrops = 5;

    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>The game's grades, best last: what "best drop" is picked by first.</summary>
    public static int GradeRank(string grade) => grade switch { "Epic" => 4, "Unique" => 3, "Legend" => 2, "Rare" => 1, _ => 0 };

    /// <summary>The best drop: the highest grade, then the most worth a piece, then the name.</summary>
    public static ShareDrop? BestDrop(IEnumerable<ShareDrop> drops) => drops.Where(d => d.Count > 0)
        .OrderByDescending(d => GradeRank(d.Grade)).ThenByDescending(d => d.Worth / (double)d.Count).ThenBy(d => d.Name, StringComparer.Ordinal).FirstOrDefault();

    /// <summary>The five icons: the most worth first, and the best drop always among them (in the fifth place when it wasn't).</summary>
    public static List<ShareDrop> TopFive(IEnumerable<ShareDrop> drops)
    {
        var list = drops.Where(d => d.Count > 0).OrderByDescending(d => d.Worth).ThenByDescending(d => GradeRank(d.Grade)).ThenBy(d => d.Name, StringComparer.Ordinal).ToList();
        var top = list.Take(TopDrops).ToList();
        if (BestDrop(list) is { } best && !top.Contains(best)) { if (top.Count == TopDrops) top[TopDrops - 1] = best; else top.Add(best); }
        return top;
    }

    /// <summary>"1 h 12 min", "42 min", "under a minute": the one way the window writes time.</summary>
    public static string Dur(double secs) => TodayLoot.Dur(secs);

    static string N(long n) => n.ToString("N0", Inv);
    static string Plural(long n, string word) => $"{N(n)} {word}{(n == 1 ? "" : "s")}";

    /// <summary>
    /// The picture's words. <paramref name="showName"/> off (the default): no character name anywhere, whatever is typed.
    /// <paramref name="fk"/> writes a figure the way the window does ("224K", "1.20M").
    /// </summary>
    public static SharePic Compose(ShareFacts f, bool showName, string? name, Func<double, string> fk)
    {
        var who = showName && !string.IsNullOrWhiteSpace(name) ? name.Trim() : "";
        var head = string.Join(" · ", new[] { f.Day.ToString("ddd d MMM", Inv), who, f.Server }.Where(s => s.Length > 0));
        var time = Dur(f.Seconds);
        // "1 h 12 min of farming · 722 kills"; a run says what it was first: "Expedition · 7 min · 18 kills"
        var sub = f.What.StartsWith("of ", StringComparison.Ordinal) ? $"{time} {f.What}" : f.What.Length > 0 ? $"{f.What} · {time}" : time;
        if (f.Count > 0 && f.CountWord.Length > 0) sub += $" · {Plural(f.Count, f.CountWord)}";
        // an hour's rate needs a minute to mean anything
        var rate = f.Seconds >= 60 && f.Made > 0 ? fk(f.Made / (f.Seconds / 3600)) : "–";
        var icons = f.Note.Length > 0 ? new List<ShareDrop>() : TopFive(f.Drops);
        var bestDrop = f.Note.Length > 0 ? null : BestDrop(f.Drops);
        var best = f.Note.Length > 0 ? f.Note : bestDrop is { } b ? "best drop: " + b.Name : "";

        // over the title: the side and what it was ("TODAY · FARMING", "LAST RUN · EXPEDITION")
        bool gather = f.CountWord == "attempt";
        var activity = f.Scope == Run ? (f.What.EndsWith(" run", StringComparison.Ordinal) ? f.What[..^4] : f.What) : gather ? "Gathering" : "Farming";
        var side = f.Scope switch { Today => "Today", Run => "Last run", Week => "Last 7 days", _ => "This session" };
        var kicker = activity.Length > 0 ? $"{side} · {activity}" : side;

        // under the total: the rate, the time, the kills
        var stats = new List<ShareStat>
        {
            new(rate, RateLabel),
            new(Short(f.Seconds), f.Scope == Run ? "run time" : gather ? "gathering" : "farming"),
        };
        if (f.Count > 0 && f.CountWord.Length > 0) stats.Add(new ShareStat(N(f.Count), f.CountWord + (f.Count == 1 ? "" : "s")));

        ShareSplit? split = null;
        if (f.Kinah >= 0 && f.Made > 0)
        {
            long loot = Math.Max(0, f.Made - f.Kinah);
            split = new ShareSplit(fk(Math.Min(f.Kinah, f.Made)), fk(loot), loot / (double)f.Made);
        }

        // the panel: the week's days, else the top drops
        List<ShareRow> rows; string rowsHead, rowsSide;
        if (f.Days.Count > 0)
        {
            long max = f.Days.Max(d => d.Made);
            var top = f.Days.Where(d => d.Made > 0).OrderByDescending(d => d.Made).FirstOrDefault();
            rows = f.Days.Select(d => new ShareRow(d.IsToday ? "Today" : d.Date.ToString("ddd d", Inv), "", "", d.Made > 0 ? fk(d.Made) : "–",
                max > 0 ? Math.Max(0, d.Made) / (double)max : 0, false, d == top, d.Made <= 0)).ToList();
            rowsHead = "By day";
            int farmed = f.Days.Count(d => d.Made > 0);
            rowsSide = $"{farmed} of {f.Days.Count} days farmed";
        }
        else
        {
            var all = f.Drops.Where(d => d.Count > 0).ToList();
            var top = f.Note.Length > 0 ? new List<ShareDrop>() : TopFive(all);
            long max = top.Count > 0 ? top.Max(d => d.Worth) : 0;
            rows = top.Select(d => new ShareRow(d.Name, d.Grade, "×" + N(d.Count),
                d.Worth > 0 ? fk(d.Worth) : d.Bound >= d.Count ? "bound" : "no price",
                max > 0 ? Math.Max(0, d.Worth) / (double)max : 0, true, top.Count > 1 && d == bestDrop, d.Worth <= 0)).ToList();
            rowsHead = "Top drops";
            rowsSide = all.Count > top.Count ? $"top {top.Count} of {all.Count} kinds" : all.Count > 0 ? Plural(all.Count, "kind") : "";
        }

        // the foot: today's zones, else the best drop (or the week's best day and zone)
        var zones = f.Zones.Where(z => z.Made > 0).OrderByDescending(z => z.Made).ToList();
        var zoneLine = zones.Count >= 2
            ? string.Join(" · ", zones.Take(2).Select(z => $"{z.Name} {fk(z.Made)}")) + (zones.Count > 2 ? $" · +{zones.Count - 2} more" : "")
            : "";
        var bestLine = f.Note.Length > 0 ? Cap(f.Note)
            : bestDrop is { } bd ? $"Best drop: {bd.Name} ×{N(bd.Count)}" + (GradeRank(bd.Grade) > 0 ? $" · {bd.Grade}" : "")
            : "";

        return new SharePic(head, f.Title, sub, rate, RateLabel, fk(f.Made), MadeLabel, icons, best, Site)
        {
            Kicker = kicker.ToUpperInvariant(), Stats = stats, Split = split,
            RowsHead = rowsHead.ToUpperInvariant(), RowsSide = rowsSide, Rows = rows, Zones = zoneLine, BestLine = bestLine,
        };
    }

    /// <summary>A time small enough for the picture's figures: "42 min", "2h 41m", "3h", "<1 min".</summary>
    public static string Short(double secs)
    {
        int m = (int)(Math.Max(0, secs) / 60);
        return m < 1 ? "<1 min" : m < 60 ? $"{m} min" : m % 60 == 0 ? $"{m / 60}h" : $"{m / 60}h {m % 60}m";
    }

    static string Cap(string s) => s.Length > 0 ? char.ToUpperInvariant(s[0]) + s[1..] : s;

    /// <summary>Copy as text: the same words as the picture, a line each, for a channel that takes no pictures.</summary>
    public static string Text(SharePic p)
    {
        var lines = new List<string>
        {
            $"Kinah Ledger · {p.Head}",
            p.Title,
            p.Sub,
            $"{p.Rate} {p.RateLabel} · {p.Made} {p.MadeLabel}",
        };
        if (p.Split is { } s) lines.Add($"loot {s.Loot} · Kinah {s.Kinah}");
        if (p.Rows.Count > 0)
            lines.Add((p.Rows[0].Item ? "top drops: " : "by day: ") + string.Join(" · ", p.Rows.Select(r => r.Item ? $"{r.Name} {r.Count} {r.Fig}" : $"{r.Name} {r.Fig}")));
        if (p.Zones.Length > 0) lines.Add("zones: " + p.Zones);
        if (p.Best.Length > 0) lines.Add(p.Best);
        lines.Add(p.Site);
        return string.Join("\n", lines);
    }

    /// <summary>The week's line in the drops' place: "best day: Thu 30, 1.20M · most made in Northern Tolbas Forest".</summary>
    public static string WeekNote(DateTime? bestDay, long bestMade, string? topZone, Func<double, string> fk)
    {
        var bits = new List<string>();
        if (bestDay is { } d && bestMade > 0) bits.Add($"best day: {d.ToString("ddd d", Inv)}, {fk(bestMade)}");
        if (!string.IsNullOrWhiteSpace(topZone)) bits.Add("most made in " + topZone);
        return string.Join(" · ", bits);
    }

    /// <summary>Where Save picture puts it: Pictures\Kinah Ledger\kinah-ledger-2026-10-31-2241-session.png.</summary>
    public static string FileName(string scope, DateTime local) => $"kinah-ledger-{local:yyyy-MM-dd-HHmm}-{scope}.png";
}
