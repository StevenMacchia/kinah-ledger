using System.Globalization;
using System.Text.RegularExpressions;

namespace KinahLedger.Session;

/// <summary>One thing the tracker read besides the chat and the loot list, for replay: a zone, a reward screen, an event header.</summary>
public sealed record ReplaySignal(DateTime At, string Kind, string Value);

/// <summary>
/// The zone and reward screen reads a replay needs to put each pickup where the live tracker would have (2026-10-23).
/// Traces written since then carry them as "zone", "reward" and "header" lines; older ones only have chat and loot list
/// reads, so for those the same facts are taken from reads.log's notes ("zone: Hidden Lugi (Event)", "reward screen open
/// (Trade Shop)"). reads.log keeps the time of day only: its days are told apart where the clock runs backwards, and the
/// last day is the day the file was last written (or <c>--reads-date</c>).
/// </summary>
public static class ReplaySignals
{
    static readonly Regex Line = new(@"^(\d{2}):(\d{2}):(\d{2})(?:\.(\d))?\s+\S+\s+(.*)$", RegexOptions.Compiled);
    static readonly Regex ZoneNote = new(@"^zone: (?!read:)(?!"")(.+?)(?: \((?:Expedition|Event|Arena|Legion|Ignored)\))?$", RegexOptions.Compiled);
    static readonly Regex Unlisted = new(@"^zone: "".+"" is on no list", RegexOptions.Compiled);
    static readonly Regex Reward = new(@"^reward screen open \((.+?)\):", RegexOptions.Compiled);
    static readonly Regex Header = new(@"^event header: (.+?)(?: \(|$)", RegexOptions.Compiled);

    /// <summary>
    /// The signals in reads.log, in order, with local times turned into UTC. <paramref name="olderPath"/> is reads.old.log, the
    /// file reads.log was turned over from (2026-10-25): read first, so a trace from before the turn-over still finds its zones.
    /// </summary>
    public static List<ReplaySignal> FromReadsLog(string path, DateTime? lastDay = null, string? olderPath = null)
    {
        var raw = new List<(TimeSpan T, string Kind, string Value)>();
        var days = new List<int>();
        int day = 0; TimeSpan prev = TimeSpan.Zero; bool any = false;
        var lines = olderPath != null && File.Exists(olderPath) ? File.ReadLines(olderPath).Concat(File.ReadLines(path)) : File.ReadLines(path);
        foreach (var l in lines)
        {
            var m = Line.Match(l);
            if (!m.Success) continue;
            var t = new TimeSpan(0, int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value), m.Groups[4].Success ? int.Parse(m.Groups[4].Value) * 100 : 0);
            // the clock running back by more than two hours is a new day (a read a little out of order isn't)
            if (any && t < prev && (prev - t).TotalHours > 2) day++;
            prev = t; any = true;
            var text = m.Groups[5].Value.Trim();
            if (Unlisted.IsMatch(text)) { raw.Add((t, "zone", SessionEngine.UnknownZone)); days.Add(day); }
            else if (ZoneNote.Match(text) is { Success: true } z) { raw.Add((t, "zone", z.Groups[1].Value.Trim())); days.Add(day); }
            else if (Reward.Match(text) is { Success: true } r) { raw.Add((t, "reward", r.Groups[1].Value.Trim())); days.Add(day); }
            else if (Header.Match(text) is { Success: true } h) { raw.Add((t, "header", h.Groups[1].Value.Trim())); days.Add(day); }
        }
        var last = (lastDay ?? File.GetLastWriteTime(path)).Date;
        var list = new List<ReplaySignal>();
        for (int i = 0; i < raw.Count; i++)
        {
            var local = last.AddDays(days[i] - day).Add(raw[i].T);
            list.Add(new ReplaySignal(DateTime.SpecifyKind(local, DateTimeKind.Local).ToUniversalTime(), raw[i].Kind, raw[i].Value));
        }
        return list;
    }

    /// <summary>A date given as yyyy-MM-dd, or null.</summary>
    public static DateTime? Date(string? s) => s != null && DateTime.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
}
