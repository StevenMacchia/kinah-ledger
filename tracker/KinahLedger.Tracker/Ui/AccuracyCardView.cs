using KinahLedger.Session;

namespace KinahLedger.Ui;

/// <summary>
/// What the Accuracy chip, the Accuracy card, the panel's line and the Discord card say, from the session's
/// <see cref="AccuracyScore.Summary"/>. No WPF here, so parse-test checks the words. Steven's copy rules: plain words, the
/// title is the name, one secondary line of 14 words or fewer, nothing that reads like a log. Since 2026-10-25 the measure
/// is "of what a second source could check, how much matched" (AccuracyScore rule 3): a pickup one source saw is "not
/// checked", never bad, and a percentage shows only when something was contradicted (Steven: "VERIFIED % BEING LOW COULD
/// CHURN USERS FAST").
/// </summary>
/// <param name="Chip">The Farming page's one chip: "✓ Nothing looked wrong", "2 to look at", "✓ All looked at".</param>
/// <param name="Line">The card's first line: "Checked by the game: all 412 matched", "… 410 of 412 matched (99%)".</param>
/// <param name="Counts">The card's four counts, in order: checked, matched, to look at, not checked.</param>
public sealed record AccuracyCardView(
    string Chip, string Tone, string Line, string? Hud, string? Discord, string Detail,
    IReadOnlyList<(string Label, int Count, string Sub)> Counts, IReadOnlyList<AccuracyCardView.Row> Rows)
{
    public const string Title = "Accuracy";

    /// <summary>One thing to look at as the list shows it: the time, what, and why in one plain sentence.</summary>
    public sealed record Row(int Id, string Time, string Text, string Why, AccuracyScore.Check Check);

    public bool HasRows => Rows.Count > 0;

    /// <summary>
    /// What a player sees. 0.8.34 (Steven 2026-10-25: a low "accuracy %" "makes the tool look useless and obsolete") cut it
    /// to a lone "2 to look at" chip; 0.9.0 (Steven 2026-10-30: "i would like to get rid of the '15 to look at' etc. just
    /// 'Report a miscount' should automatically send the logs") cuts that too: with details off there is no chip, no line,
    /// no counts, nothing on the panel or the Discord card. The things to check are still found, logged (the audit line,
    /// reads.log) and pin their pictures for a report; only the player-facing words go. "Report a miscount" in the footer
    /// is the one way in. With <paramref name="details"/> (Options › Reading › Show accuracy details, off by default, for
    /// checking the tracker) the full card as before: the line, the four counts, the list, the panel's tick and Discord line.
    /// </summary>
    public static AccuracyCardView From(AccuracyScore.Summary s, bool details)
    {
        var full = From(s);
        if (details) return full;
        // the rows stay on the (unseen) view so the panel's colour and the tests can still tell a check is open
        return new AccuracyCardView("", "good", "", null, null, "", Array.Empty<(string, int, string)>(), full.Rows);
    }

    public static AccuracyCardView From(AccuracyScore.Summary s)
    {
        var rows = s.ToCheck.OrderByDescending(c => c.At)
            .Select(c => new Row(c.Id, c.At.ToLocalTime().ToString("HH:mm:ss"), c.Text, AccuracyScore.Plain(c.Why), c)).ToList();
        int n = rows.Count;
        string chip = n > 0 ? $"{n} to look at" : s.Contradicted > 0 ? "✓ All looked at" : "✓ Nothing looked wrong";
        // nothing a second source could check yet: calm when the list is empty too, plain when it isn't (a count assumed 1)
        string line = s.Checked == 0 ? (n > 0 ? "Nothing a second source could check yet" : s.NotChecked > 0 ? "Nothing looked wrong" : "Nothing counted yet")
            : s.Contradicted == 0 ? (s.Checked == 1 ? "Checked by the game: 1 matched" : $"Checked by the game: all {s.Checked:N0} matched")
            : $"Checked by the game: {s.Matched:N0} of {s.Checked:N0} matched" + (s.ShowsPercent ? $" ({s.MatchPercent}%)" : "");
        var counts = new List<(string, int, string)>
        {
            ("Checked", s.Checked, "a second source could check it"),
            ("Matched", s.Matched, ""),
            ("To look at", n, ""),
            ("Not checked", s.NotChecked, "one source saw it, nothing said it was wrong"),
        };
        // the panel over the game: things to look at, or a calm tick once enough was checked; nothing before that
        string? hud = n > 0 || s.EnoughChecked ? chip : null;
        string? discord = !s.EnoughChecked ? null
            : s.Contradicted == 0 ? $"✓ Checked by the game: all {s.Checked:N0} matched"
            : $"Checked by the game: {s.Matched:N0} of {s.Checked:N0} matched ({s.MatchPercent}%)";
        return new AccuracyCardView(chip, n > 0 ? "warn" : "good", line, hud, discord, Details(s), counts, rows);
    }

    /// <summary>The figures behind the counts, for the card's hover: a few short lines, each a number and what it counts.</summary>
    static string Details(AccuracyScore.Summary s)
    {
        var l = new List<string>();
        if (s.Checked + s.NotChecked > 0) l.Add($"{s.Matched:N0} of {s.Checked:N0} pickups a second source could check matched. {s.NotChecked:N0} only one source saw.");
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
