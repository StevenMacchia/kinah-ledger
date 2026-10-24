using KinahLedger.Session;

namespace KinahLedger.Ui;

/// <summary>
/// What the Accuracy card, the panel's line and the Discord card say, from the session's <see cref="AccuracyScore.Summary"/>.
/// No WPF here, so parse-test checks the words. Steven's copy rules: plain words, the title is the name, one secondary
/// line of 14 words or fewer, nothing that reads like a log.
/// </summary>
public sealed record AccuracyCardView(
    string Line, string Secondary, string Status, string Tone, string? Hud, string? Discord, string Detail, IReadOnlyList<AccuracyCardView.Row> Rows)
{
    public const string Title = "Accuracy";

    /// <summary>One thing to check as the list shows it: the time, what, and why in one plain sentence.</summary>
    public sealed record Row(int Id, string Time, string Text, string Why, AccuracyScore.Check Check);

    public bool HasRows => Rows.Count > 0;

    public static AccuracyCardView From(AccuracyScore.Summary s)
    {
        var rows = s.ToCheck.OrderByDescending(c => c.At)
            .Select(c => new Row(c.Id, c.At.ToLocalTime().ToString("HH:mm:ss"), c.Text, AccuracyScore.Plain(c.Why), c)).ToList();
        int n = rows.Count;
        string line = s.Enough ? $"Verified by the game's own numbers: {s.Percent}%" : "Not enough counted yet";
        string secondary = n > 0
            ? (rows.All(r => r.Check.Pickup) ? $"{n} pickup{(n == 1 ? "" : "s")} unsure · tap to see" : $"{n} to check · tap to see")
            : s.Enough && s.SeenOnce > 0 && s.State != AccuracyScore.State.Verified ? $"Nothing to check · {s.SeenOnce} seen only once" : "Nothing to check";
        string tone = s.State switch
        {
            AccuracyScore.State.Verified => "good",
            AccuracyScore.State.MostlyVerified => "gold",
            AccuracyScore.State.CheckThese => "warn",
            _ => "faint",
        };
        string? hud = n > 0 ? $"? {n} to check" : s.Enough ? $"✓ {s.Percent}% verified" : null;
        string? discord = s.Enough ? $"✓ {s.Percent}% verified by the game's own numbers" : null;
        return new AccuracyCardView(line, secondary, s.Word, tone, hud, discord, Details(s), rows);
    }

    /// <summary>The figures behind the share, for the card's hover: a few short lines, each a number and what it counts.</summary>
    static string Details(AccuracyScore.Summary s)
    {
        var l = new List<string>();
        if (s.Judged > 0) l.Add($"{s.Verified:N0} of {s.Judged:N0} pickups and reward slots backed by a second source in the game.");
        if (s.KinaCounted + s.KinaRefused > 0)
            l.Add(s.CounterRead ? $"Kinah counter: confirmed {s.KinaConfirmed:N0} Kinah pickups, refused {s.KinaRefused:N0}." : "Kinah counter: not read yet, so Kinah lines count as read.");
        if (s.Counter is { } r) l.Add($"Kinah counter against the count: {r.Words}.");
        if (s.RowsConfirmed + s.RowsNoChat > 0) l.Add($"Loot list: {s.RowsConfirmed:N0} rows matched the chat, {s.RowsNoChat:N0} had no chat line.");
        if (s.RunsFull + s.RunsPartly + s.RunsNotRead > 0) l.Add($"Reward pictures: {s.RunsFull} read in full, {s.RunsPartly} in part ({s.SlotsUnread} slots), {s.RunsNotRead} not read.");
        if (s.MedianDelaySeconds is double d) l.Add($"A pickup counts about {d:0.0} s after it shows.");
        if (s.RowsUnsure + s.NotRows > 0) l.Add($"Not counted, not misses: {s.RowsUnsure:N0} loot list rows too faint to read, {s.NotRows:N0} lines that weren't loot.");
        return string.Join("\n", l);
    }

    /// <summary>
    /// What "Report" on a row opens the miscount report with: the line that heads the report (time, what, why), the moment
    /// whose kept pictures and reads go in (the handle; none for a run, whose pictures are its reward-shots), and whether
    /// its loot list and panel pictures are sent (Options › Sharing: Send report pictures, with sharing on).
    /// </summary>
    public sealed record MiscountPrefill(string Why, DateTime? Focus, string Handle, bool SendPictures, IReadOnlyList<string> Pictures);

    public static MiscountPrefill Prefill(AccuracyScore.Check c, bool sendPictures)
    {
        DateTime? focus = c.Handle.StartsWith("run:") ? null
            : DateTime.TryParse(c.Handle, null, System.Globalization.DateTimeStyles.RoundtripKind, out var t) ? t.ToUniversalTime() : c.At.ToUniversalTime();
        var why = $"{c.At.ToLocalTime():HH:mm:ss} {c.Text}: {c.Reason}" + (c.Pictures.Count > 0 ? $" (pictures: {string.Join(", ", c.Pictures.Take(8))})" : "");
        return new MiscountPrefill(why, focus, c.Handle, sendPictures, c.Pictures);
    }
}
