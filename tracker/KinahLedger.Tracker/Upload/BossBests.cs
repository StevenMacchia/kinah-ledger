using System.Text.Json;
using System.Text.Json.Serialization;
using KinahLedger.Parser;

namespace KinahLedger.Upload;

/// <summary>
/// One boss on the player's kinahledger.com profile (Steven, 2026-11-15: "updates based on the parser ... to the user
/// profile"): the player's OWN best kill of it in one instance and game mode, and their latest pull of it, for "last vs
/// best". Nothing about anyone else in the group: no names, classes, damage or shares, only how many were in it. Sent only
/// with Options › Sharing › "Show my boss bests on my kinahledger.com profile" on (<see cref="BossBests.Enabled"/>, off by
/// default), riding the run uploads (<see cref="RunPayload.boss_bests"/>); the site keeps it only for a tracker linked to a
/// profile and shows it only on that profile's own signed-in page, never publicly and never ranked.
/// </summary>
/// <param name="dps">Your damage over the fight's length, whole: to the boss alone when the record kept that apart (<paramref name="basis"/> "boss"), else to every target ("all", records kept before the honest pass).</param>
/// <param name="at">When the best kill started, UTC.</param>
/// <param name="group">How many were in the group, you included.</param>
/// <param name="kills">Your kills of this boss kept on this PC.</param>
public sealed record BossBestPayload(
    string boss, string instance, string mode, string kind, long dps, string basis, int seconds, string at,
    [property: JsonPropertyName("class"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? cls = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? cp = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? group = null,
    int kills = 1,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? last_dps = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? last_seconds = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? last_at = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? last_outcome = null);

/// <summary>
/// The boss bests that go to the profile, and when. Off by default; the window sets <see cref="Enabled"/> from the
/// player's choice. Each character's list goes when it changed since the site last took it (or an hour after the site said
/// the tracker isn't linked to a profile yet), so an upload carries it rarely and small. Turned off, the next upload carries
/// an empty list once (<see cref="ClearPending"/>), which tells the site to forget what it kept.
/// </summary>
public static class BossBests
{
    /// <summary>Most bosses an upload carries (the site takes 60): the ones fought most recently.</summary>
    public const int MaxSent = 40;
    /// <summary>A list the site kept nowhere (no profile linked yet) is offered again after this long.</summary>
    public static readonly TimeSpan RetryUnlinked = TimeSpan.FromHours(1);
    /// <summary>The kinds a boss pull is kept for (Encounter.Kind).</summary>
    static readonly HashSet<string> Kinds = new(StringComparer.OrdinalIgnoreCase) { "Transcendence", "Expedition", "Sanctuary" };

    /// <summary>Options › Sharing › Show my boss bests on my kinahledger.com profile. Off by default.</summary>
    public static bool Enabled { get; set; }
    /// <summary>The switch went off and the site hasn't been told yet: the next upload carries an empty list.</summary>
    public static bool ClearPending { get; set; }
    /// <summary>The site took the empty list: the window clears its saved <see cref="ClearPending"/>.</summary>
    public static Action? Cleared { get; set; }
    /// <summary>The kept fights of the character playing (the Parser's store); a test sets its own.</summary>
    public static Func<IEnumerable<Encounter>> Fights { get; set; } = () => KinahLedger.Session.TrackerSession.FightStore.All();

    static readonly object Gate = new();
    /// <summary>Per character slot: the list the site last answered for, whether it kept it, when.</summary>
    static readonly Dictionary<string, (string Sig, bool Linked, DateTime At)> Answered = new();

    /// <summary>Forget what was sent (a test, a new install id).</summary>
    public static void Reset() { lock (Gate) Answered.Clear(); }

    /// <summary>
    /// What the next upload carries for this character slot: an empty list to forget (switched off), the list when it changed
    /// since the site took it, or null (nothing to send: off, no kills kept, or unchanged).
    /// </summary>
    public static List<BossBestPayload>? Pending(string charId, DateTime nowUtc)
    {
        if (!Enabled) return ClearPending ? new List<BossBestPayload>() : null;
        List<BossBestPayload> list;
        try { list = From(Fights()); }
        catch (Exception ex) { CrashLog.Write("boss bests for the profile", ex); return null; }
        if (list.Count == 0) return null;
        var sig = Signature(list);
        lock (Gate)
        {
            if (Answered.TryGetValue(charId, out var a) && a.Sig == sig && (a.Linked || nowUtc - a.At < RetryUnlinked)) return null;
        }
        return list;
    }

    /// <summary>How the site answered an upload that carried boss bests (its "boss_bests": {stored, dropped, linked, cleared}).</summary>
    public static void Took(RunPayload p, SendResult r, DateTime nowUtc)
    {
        if (p.boss_bests == null || !r.Ok) return;
        bool linked = false, cleared = false;
        try
        {
            using var doc = JsonDocument.Parse(r.Body.Length > 0 ? r.Body : "{}");
            if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("boss_bests", out var b) && b.ValueKind == JsonValueKind.Object)
            {
                linked = b.TryGetProperty("linked", out var l) && l.ValueKind == JsonValueKind.True;
                cleared = b.TryGetProperty("cleared", out var c) && c.ValueKind == JsonValueKind.True;
            }
        }
        catch (JsonException) { /* a site from before: kept nowhere, tried again in an hour */ }
        if (p.boss_bests.Count == 0)
        {
            // an empty list is "forget mine": done once the site said so, or when nothing was ever kept (not linked)
            if (cleared || !linked) { ClearPending = false; Cleared?.Invoke(); }
            return;
        }
        lock (Gate) Answered[p.character?.id ?? ""] = (Signature(p.boss_bests), linked, nowUtc);
    }

    static string Signature(List<BossBestPayload> list) => JsonSerializer.Serialize(list);

    /// <summary>
    /// The player's own best kill per instance, game mode and boss, with their latest pull of it, from the kept fights. Only
    /// boss pulls of a Transcendence, an Expedition or a Sanctuary that ended, with damage known, the player's own damage in
    /// them, nothing missed (not Partial); the best is a kill. Newest first, at most <see cref="MaxSent"/>.
    /// </summary>
    public static List<BossBestPayload> From(IEnumerable<Encounter> fights)
    {
        var pulls = fights.Where(e => e.ModeKind == ParserMode.Bosses && !e.Wave && !e.OpenWorld && e.End != null && e.HasDamage && !e.Partial
                                      && e.Boss.Length > 0 && Kinds.Contains(e.Kind) && e.You is { Damage: > 0 })
                          .ToList();
        var outp = new List<(DateTime Latest, BossBestPayload P)>();
        foreach (var g in pulls.GroupBy(e => (BossBook.Strip(e.Instance).ToLowerInvariant(), e.GameMode.Trim().ToLowerInvariant(), e.Boss.Trim().ToLowerInvariant())))
        {
            var kills = g.Where(e => e.Outcome == "kill").ToList();
            if (kills.Count == 0) continue;
            var best = kills.OrderByDescending(Dps).ThenBy(e => e.Start).First();
            var last = g.OrderByDescending(e => e.Start).First();
            var you = best.You!;
            var place = BossBook.Strip(best.Instance);
            outp.Add((last.Start, new BossBestPayload(
                Cut(best.Boss.Trim(), 64), Cut(place.Length > 0 ? place : best.Instance.Trim(), 64), Cut(best.GameMode.Trim(), 32), Kind(best.Kind),
                (long)Math.Round(Dps(best)), best.TargetsSplit ? "boss" : "all", Seconds(best), Utc(best.Start),
                you.Class.Trim().Length > 0 ? you.Class.Trim() : null, you.CombatPower > 0 ? you.CombatPower : null,
                best.Members.Count() is var n and > 0 ? n : null, kills.Count,
                (long)Math.Round(Dps(last)), Seconds(last), Utc(last.Start), last.Outcome is "kill" or "wipe" ? last.Outcome : "left")));
        }
        return outp.OrderByDescending(x => x.Latest).Take(MaxSent).Select(x => x.P).ToList();
    }

    /// <summary>The player's own damage in a pull over its length: to the boss alone when the record kept that apart, whatever the meter's own switches show.</summary>
    public static double Dps(Encounter e)
    {
        var you = e.You;
        if (you == null) return 0;
        long damage = e.TargetsSplit ? you.BossDamage : you.Damage;
        return damage / Math.Max(e.Duration(e.End ?? e.LastAt).TotalSeconds, Encounter.MinDpsSeconds);
    }

    static int Seconds(Encounter e) => Math.Max(1, (int)Math.Round(e.Duration(e.End ?? e.LastAt).TotalSeconds));
    static string Kind(string k) => Kinds.FirstOrDefault(x => string.Equals(x, k.Trim(), StringComparison.OrdinalIgnoreCase)) ?? k;
    static string Cut(string s, int n) => s.Length > n ? s[..n] : s;
    static string Utc(DateTime t) => (t.Kind == DateTimeKind.Local ? t.ToUniversalTime() : DateTime.SpecifyKind(t, DateTimeKind.Utc)).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");
}
