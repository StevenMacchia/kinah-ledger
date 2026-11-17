using System.Globalization;
using KinahLedger.Upload;

namespace KinahLedger.Session;

/// <summary>
/// Every character's completed quests the site has not had yet, not only the one being played (Steven, 2026-11-17: the
/// alts' quests waited until that character was played again, 117 for Swoles and 134 for SwolyDaBooly). The window keeps
/// the current character's log in memory and sends it as before; the others are read from their own folders
/// (characters\cN\quests.json), sent under their own character, and marked sent in their own file.
/// </summary>
public static class QuestBacklog
{
    /// <summary>A character's quests.json, by its slot ("c2").</summary>
    public static string PathOf(string id) => Path.Combine(CrashLog.Dir, "characters", id, "quests.json");

    /// <summary>The oldest of a log's unsent quests, as a batch carries them (at most <paramref name="max"/>).</summary>
    public static List<QuestDonePayload> Payloads(QuestLog log, int max) =>
        Quests.Unsent(log).Take(max).Select(d => new QuestDonePayload(d.Key, d.Kind, d.Horizon, d.Subject, d.Target, d.Tier, d.Points, d.Worth, d.Have,
            d.DoneAtUtc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture), d.Scope)).ToList();

    /// <summary>
    /// The character, other than the one counted for now, whose oldest unsent quest is the oldest of all, with up to
    /// <paramref name="max"/> of its quests; null when none waits. The oldest goes first because the site keeps a quest on
    /// its own day only while it arrives within 30 days (kinah-ledger-site PR 237).
    /// </summary>
    public static (Character Who, List<QuestDonePayload> Quests)? Next(IReadOnlyList<Character> all, string? currentId, Func<Character, QuestLog> load, int max)
    {
        Character? best = null; QuestLog? bestLog = null; var bestAt = DateTime.MaxValue;
        foreach (var c in all)
        {
            if (c.Id.Length == 0 || c.Id == currentId) continue;
            var log = load(c);
            var first = log.Done.Where(d => !d.Sent && d.Horizon != Horizon.AllTime).Select(d => (DateTime?)d.DoneAtUtc).Min();
            if (first is { } at && at < bestAt) { best = c; bestLog = log; bestAt = at; }
        }
        return best == null ? null : (best, Payloads(bestLog!, max));
    }

    /// <summary>Every character's unsent quests but the current one's, by slot (for the log line and the self-tests).</summary>
    public static Dictionary<string, int> Waiting(IReadOnlyList<Character> all, string? currentId, Func<Character, QuestLog> load) =>
        all.Where(c => c.Id.Length > 0 && c.Id != currentId).ToDictionary(c => c.Id, c => Quests.Unsent(load(c)).Count);

    /// <summary>A character's log read from its own folder (a fresh one with no file).</summary>
    public static QuestLog Load(Character c) => Quests.Load(PathOf(c.Id));

    /// <summary>Marks a batch's quests sent in a character's own file (one not played now); how many it marked.</summary>
    public static int MarkSent(string id, IEnumerable<string> keysAndScopes)
    {
        var p = PathOf(id);
        if (!File.Exists(p)) return 0;
        var log = Quests.Load(p);
        int before = log.Done.Count(d => d.Sent);
        Quests.MarkSent(log, keysAndScopes);
        int marked = log.Done.Count(d => d.Sent) - before;
        if (marked > 0) Quests.Save(log, p);
        return marked;
    }
}
