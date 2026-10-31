using System.Globalization;

namespace KinahLedger.Session;

/// <summary>One item on the Share picture: its name, grade, how many, and its worth at the lowest Market price (0 with none).</summary>
public sealed record ShareDrop(string Name, string Grade, int Count, long Worth);

/// <summary>
/// What a Share picture is drawn from, for one of its three sides (This session, Last run, This week): the day, the server,
/// the title (the zone, the run's name, or the week), what it was and for how long, how many kills (or attempts, or runs),
/// what it made after the Market's cut, and the items picked up. <paramref name="Note"/> stands in the drops' place when
/// there are no items to show (the week, which keeps no items).
/// </summary>
public sealed record ShareFacts(string Scope, DateTime Day, string Server, string Title, string What, double Seconds, int Count, string CountWord,
    long Made, IReadOnlyList<ShareDrop> Drops, string Note = "");

/// <summary>The picture's words, as drawn: the head line, the title and the line under it, the two figures, the icons and the best drop.</summary>
public sealed record SharePic(string Head, string Title, string Sub, string Rate, string RateLabel, string Made, string MadeLabel,
    IReadOnlyList<ShareDrop> Icons, string Best, string Site);

/// <summary>
/// Share (0.9.3, ShareA of the canvas, Steven's pick 2026-10-31): a 1200×630 picture of a session, the last run or the week,
/// to paste in Discord or anywhere. This is its text, apart from the drawing (Ui/MainWindow.Share.cs), so the self-test
/// can check it: the character name only when the player turned it on, the figures for each side, the best drop. No
/// accuracy figures and no "to look at" counts, ever. Nothing is uploaded.
/// </summary>
public static class SharePicture
{
    public const string Session = "session", Run = "run", Week = "week";
    public const string Site = "kinahledger.com";
    public const string RateLabel = "Kinah an hour", MadeLabel = "made, after the Market's cut";
    /// <summary>The one line under the sheet's buttons.</summary>
    public const string Note = "Paste it in Discord or anywhere. Nothing is uploaded.";
    public const int Width = 1200, Height = 630;

    /// <summary>The game's grades, best last: what "best drop" is picked by first.</summary>
    public static int GradeRank(string grade) => grade switch { "Epic" => 4, "Unique" => 3, "Legend" => 2, "Rare" => 1, _ => 0 };

    /// <summary>The best drop: the highest grade, then the most worth a piece, then the name.</summary>
    public static ShareDrop? BestDrop(IEnumerable<ShareDrop> drops) => drops.Where(d => d.Count > 0)
        .OrderByDescending(d => GradeRank(d.Grade)).ThenByDescending(d => d.Worth / (double)d.Count).ThenBy(d => d.Name, StringComparer.Ordinal).FirstOrDefault();

    /// <summary>The five icons: the most worth first, and the best drop always among them (in the fifth place when it wasn't).</summary>
    public static List<ShareDrop> TopFive(IEnumerable<ShareDrop> drops)
    {
        var list = drops.Where(d => d.Count > 0).OrderByDescending(d => d.Worth).ThenByDescending(d => GradeRank(d.Grade)).ThenBy(d => d.Name, StringComparer.Ordinal).ToList();
        var top = list.Take(5).ToList();
        if (BestDrop(list) is { } best && !top.Contains(best)) { if (top.Count == 5) top[4] = best; else top.Add(best); }
        return top;
    }

    /// <summary>"1 h 12 min", "42 min", "under a minute": the one way the window writes time.</summary>
    public static string Dur(double secs) => TodayLoot.Dur(secs);

    /// <summary>
    /// The picture's words. <paramref name="showName"/> off (the default): no character name anywhere, whatever is typed.
    /// <paramref name="fk"/> writes a figure the way the window does ("224K", "1.20M").
    /// </summary>
    public static SharePic Compose(ShareFacts f, bool showName, string? name, Func<double, string> fk)
    {
        var who = showName && !string.IsNullOrWhiteSpace(name) ? name.Trim() : "";
        var head = string.Join(" · ", new[] { f.Day.ToString("ddd d MMM", CultureInfo.InvariantCulture), who, f.Server }.Where(s => s.Length > 0));
        var time = Dur(f.Seconds);
        // "1 h 12 min of farming · 722 kills"; a run says what it was first: "Expedition · 7 min · 18 kills"
        var sub = f.What.StartsWith("of ", StringComparison.Ordinal) ? $"{time} {f.What}" : f.What.Length > 0 ? $"{f.What} · {time}" : time;
        if (f.Count > 0 && f.CountWord.Length > 0) sub += $" · {f.Count.ToString("N0", CultureInfo.InvariantCulture)} {f.CountWord}{(f.Count == 1 ? "" : "s")}";
        // an hour's rate needs a minute to mean anything
        var rate = f.Seconds >= 60 && f.Made > 0 ? fk(f.Made / (f.Seconds / 3600)) : "–";
        var icons = f.Note.Length > 0 ? new List<ShareDrop>() : TopFive(f.Drops);
        var best = f.Note.Length > 0 ? f.Note : BestDrop(f.Drops) is { } b ? "best drop: " + b.Name : "";
        return new SharePic(head, f.Title, sub, rate, RateLabel, fk(f.Made), MadeLabel, icons, best, Site);
    }

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
        if (p.Best.Length > 0) lines.Add(p.Best);
        lines.Add(p.Site);
        return string.Join("\n", lines);
    }

    /// <summary>The week's line in the drops' place: "best day: Thu 30, 1.20M · most made in Northern Tolbas Forest".</summary>
    public static string WeekNote(DateTime? bestDay, long bestMade, string? topZone, Func<double, string> fk)
    {
        var bits = new List<string>();
        if (bestDay is { } d && bestMade > 0) bits.Add($"best day: {d.ToString("ddd d", CultureInfo.InvariantCulture)}, {fk(bestMade)}");
        if (!string.IsNullOrWhiteSpace(topZone)) bits.Add("most made in " + topZone);
        return string.Join(" · ", bits);
    }

    /// <summary>Where Save picture puts it: Pictures\Kinah Ledger\kinah-ledger-2026-10-31-2241-session.png.</summary>
    public static string FileName(string scope, DateTime local) => $"kinah-ledger-{local:yyyy-MM-dd-HHmm}-{scope}.png";
}
