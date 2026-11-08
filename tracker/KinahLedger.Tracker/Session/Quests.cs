using System.Globalization;
using System.Text.Json;

namespace KinahLedger.Session;

/// <summary>
/// The quest engine (phase 1 of the quest system, 2026-11-08; the design is the side-session note quest-score-design.md).
/// A quest is a goal the tracker sets from what the player is doing, worth points when reached. The engine is pure: the
/// window hands it <see cref="QuestInputs"/> (the counts the tracker already keeps: this session's, today's and the week's
/// Kinah, the crafting materials picked up, the runs and best runs by place, gathering attempts) and reads back the live
/// quests in the order the surface shows them, each one's tree, the ones just completed and the score. Nothing here touches
/// WPF, the chat or the network. What is kept (the completed quests and their points) lives in quests.json beside
/// goals.json; nothing is uploaded in this phase (phase 2 sends the completed quests in the upload batch).
/// </summary>
public static class QuestKind
{
    /// <summary>Loose Kinah picked up farming (as LOOT's Today side counts it): the top row, always.</summary>
    public const string Kinah = "kinah";
    /// <summary>A crafting material by amount (never gear, never a currency): the two rows under the Kinah.</summary>
    public const string Material = "material";
    /// <summary>Runs at a place (expeditions and events alike).</summary>
    public const string Runs = "runs";
    /// <summary>Beat your best run at a place.</summary>
    public const string BestRun = "best";
    /// <summary>Gathering attempts.</summary>
    public const string Gather = "gather";
    public static readonly string[] All = { Kinah, Material, Runs, BestRun, Gather };
}

/// <summary>How long a quest counts over: this count (the session), today from midnight on this PC, or this week.</summary>
public static class Horizon
{
    public const string Session = "session", Today = "today", Week = "week";
    public static readonly string[] All = { Session, Today, Week };
    /// <summary>A weekly quest is worth more than a session one for the same figure.</summary>
    public static double Mult(string h) => h switch { Week => 3, Today => 1.5, _ => 1 };
    public static string Word(string h) => h switch { Week => "this week", Today => "today", _ => "this session" };
    public static int Rank(string h) => Array.IndexOf(All, h);
}

/// <summary>
/// What one horizon has counted: the figures quests are measured against. Items are by the name the item list writes,
/// crafting materials only (the window filters gear and currencies out before they get here); the week has no item counts
/// (the day history keeps none), so its dictionary is empty and material quests stop at today. Runs are by place name.
/// </summary>
/// <param name="Kinah">Loose Kinah from farming.</param>
/// <param name="Seconds">Time farming in the horizon (the active time), what every pace divides by.</param>
/// <param name="Items">Crafting materials picked up, by name.</param>
/// <param name="Runs">Runs finished, by place.</param>
/// <param name="Gathers">Gathering attempts.</param>
public sealed record QuestCounts(long Kinah, double Seconds, IReadOnlyDictionary<string, long> Items, IReadOnlyDictionary<string, int> Runs, int Gathers)
{
    public static readonly QuestCounts None = new(0, 0, new Dictionary<string, long>(), new Dictionary<string, int>(), 0);
    public long Item(string name) => Items.TryGetValue(name, out var n) ? n : Items.FirstOrDefault(kv => string.Equals(kv.Key, name, StringComparison.OrdinalIgnoreCase)).Value;
    public int RunsAt(string place) => Runs.TryGetValue(place, out var n) ? n : 0;
    /// <summary>Whether the horizon counts items at all (the week does not).</summary>
    public bool HasItems => Items.Count > 0;
}

/// <summary>A place's best run (its worth: Kinah and loot at Market prices) from before today, and the best run today has had there so far.</summary>
public sealed record BestHere(string Place, long Best, long Latest);

/// <summary>A crafting material the window knows: its name as the item list writes it, its tier from its rarity (<see cref="Quests.TierOf"/>), and what a piece sells for (0 unpriced).</summary>
public sealed record QuestMaterial(string Name, int Tier, long Worth);

/// <summary>
/// Everything the engine reads at one look. <paramref name="Materials"/> is the window's word on what a crafting material
/// is and how rare. <paramref name="LastFarmed"/> is the kinds last picked up, newest first (the feed): the first material
/// row is what you are farming now.
/// </summary>
public sealed record QuestInputs(QuestCounts Session, QuestCounts Today, QuestCounts Week, IReadOnlyList<QuestMaterial> Materials, IReadOnlyList<string> LastFarmed,
    IReadOnlyList<BestHere> BestRuns, DateTime SessionStartUtc, DateTime NowUtc)
{
    public QuestCounts Of(string horizon) => horizon switch { Horizon.Week => Week, Horizon.Today => Today, _ => Session };
}

/// <summary>
/// One quest, live or passed: its kind, horizon, subject (an item or a place; "" for Kinah and gathering), target, tier,
/// the points it is worth, and how far it is in the counts given.
/// </summary>
public sealed record Quest(string Kind, string Horizon, string Subject, long Target, int Tier, int Points, long Have, double? SecondsToGo)
{
    /// <summary>"material:session:Spiritstone:100": one per chain step; the scope (<see cref="Quests.ScopeOf"/>) says which session, day or week.</summary>
    public string Key => Quests.KeyOf(Kind, Horizon, Subject, Target);
    public bool Done => Target > 0 && Have >= Target;
    public long ToGo => Math.Max(0, Target - Have);
    public double Share => Target <= 0 ? 0 : Math.Clamp(Have / (double)Target, 0, 1);
}

/// <summary>A quest's tree: the quest, its chain upward (the next steps it unlocks, nearest first) and its branches (the same thing over the longer horizons).</summary>
public sealed record QuestTree(Quest Node, IReadOnlyList<Quest> Up, IReadOnlyList<Quest> Branches);

/// <summary>
/// A quest completed: what it was, when, the count behind it, the points credited (after the hour's cap) and the scope it
/// was done in (the session's start, the day, the week), so each is done once per scope.
/// </summary>
public sealed class QuestDone
{
    public string Key { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Horizon { get; set; } = "";
    public string Subject { get; set; } = "";
    public long Target { get; set; }
    public int Tier { get; set; }
    /// <summary>The points credited, after the cap (<see cref="Quests.CapPerHour"/>); what the quest was worth is <see cref="Worth"/>.</summary>
    public int Points { get; set; }
    public int Worth { get; set; }
    public long Have { get; set; }
    public DateTime DoneAtUtc { get; set; }
    /// <summary>"s:2026-11-08T14:02:11.250Z" for a session quest (the count's start, to the millisecond), "d:2026-11-08" for today's, "w:2026-W45" for the week's.</summary>
    public string Scope { get; set; } = "";
}

/// <summary>quests.json: the completed quests (the last <see cref="Quests.KeepDays"/> days) and the all-time points.</summary>
public sealed class QuestLog
{
    public int Version { get; set; } = 1;
    public long AllTimePoints { get; set; }
    public List<QuestDone> Done { get; set; } = new();
}

/// <summary>The score: the points this session, today, this week, and all time.</summary>
public sealed record QuestScore(long Session, long Today, long Week, long AllTime);

public static class Quests
{
    /* ---------------- the chains ---------------- */

    /// <summary>The stack chain every count climbs: 5 → 10 → 20 → 30 → 50 → 100 → 150 → 200 → 300 → 500 → 1,000, then thousands.</summary>
    public static readonly long[] StackChain = { 5, 10, 20, 30, 50, 100, 150, 200, 300, 500, 1000 };
    /// <summary>Kinah this session: the round figures from 100K up.</summary>
    public static readonly long[] KinahSession = { 100_000, 250_000, 500_000, 1_000_000, 2_000_000, 3_000_000, 5_000_000, 7_500_000, 10_000_000, 15_000_000, 20_000_000 };
    public static readonly long[] KinahToday = { 500_000, 1_000_000, 2_000_000, 3_000_000, 5_000_000, 10_000_000, 15_000_000, 20_000_000, 30_000_000, 50_000_000 };
    public static readonly long[] KinahWeek = { 5_000_000, 10_000_000, 25_000_000, 50_000_000, 100_000_000, 150_000_000, 200_000_000, 300_000_000, 500_000_000 };
    public static readonly long[] RunsChain = { 1, 3, 5, 8, 10, 15, 20, 30, 50 };
    public static readonly long[] GatherChain = { 10, 25, 50, 100, 200, 300, 500, 1000 };

    /// <summary>How many steps of a chain a tree shows above the live one.</summary>
    public const int UpSteps = 3;
    /// <summary>Points an hour of play can earn from the session's quests at most, so grinding one trivial quest never outscores real farming; today's and the week's quests are few and coarse and are not capped.</summary>
    public const int CapPerHour = 400;
    /// <summary>Completed quests older than this leave quests.json (the all-time total keeps their points).</summary>
    public const int KeepDays = 60;
    /// <summary>A material needs this many picked up in the session before it gets a quest (a stray drop is not something you are farming).</summary>
    public const int MaterialMin = 3;
    /// <summary>How many materials have a live quest at once: the two rows at rest and three more behind the chevron, so the list is never a wall (a demo session had 13).</summary>
    public const int MaterialMax = 5;
    /// <summary>How many of them also have a today quest (the two shown at rest).</summary>
    public const int MaterialToday = 2;
    /// <summary>Nothing before this much play: the first two minutes have no pace.</summary>
    public const int MinSeconds = 120;

    public static string KeyOf(string kind, string horizon, string subject, long target) => $"{kind}:{horizon}:{subject}:{target}";

    /// <summary>The chain a kind climbs over a horizon.</summary>
    public static long[] ChainOf(string kind, string horizon) => kind switch
    {
        QuestKind.Kinah => horizon switch { Horizon.Week => KinahWeek, Horizon.Today => KinahToday, _ => KinahSession },
        QuestKind.Runs => RunsChain,
        QuestKind.Gather => GatherChain,
        QuestKind.BestRun => Array.Empty<long>(),
        _ => StackChain,
    };

    /// <summary>The next step of a chain above <paramref name="have"/>; past the chain's end, the last step's size again for a chain that ends under 1,000 (runs go 50, 100, 150), round thousands for the other counts, the last step's size again for Kinah.</summary>
    public static long NextStep(long[] chain, long have)
    {
        foreach (var m in chain) if (m > have) return m;
        if (chain.Length == 0) return 0;
        long last = chain[^1];
        long step = last < 1_000 ? last : last < 100_000 ? (have < 10_000 ? 1_000 : 10_000) : last >= 100_000_000 ? 100_000_000 : last >= 10_000_000 ? 10_000_000 : 1_000_000;
        return (have / step + 1) * step;
    }

    /// <summary>Which step of its chain a target is (0 the first); past the chain, counts on from its end.</summary>
    public static int StepIndex(long[] chain, long target)
    {
        if (chain.Length == 0) return 0;
        int i = Array.IndexOf(chain, target);
        if (i >= 0) return i;
        long last = chain[^1], step = Math.Max(1, NextStep(chain, last) - last);
        return target <= last ? 0 : chain.Length - 1 + (int)Math.Min(20, (target - last) / step);
    }

    /* ---------------- tiers and points ---------------- */

    /// <summary>
    /// A material's tier from its rarity: the drop odds the app has ("1 in 54" is <paramref name="oneIn"/> 54) and its grade.
    /// Tier 1 drops better than 1 in 10, tier 2 up to 1 in 50, tier 3 rarer than that, or Legend and better whatever the odds.
    /// With no odds, the grade alone: Common 1, Rare 2.
    /// </summary>
    public static int TierOf(double? oneIn, string grade)
    {
        if (grade is "Legend" or "Legendary" or "Unique" or "Epic" or "Mythic" or "Ultimate" or "Heroic") return 3;
        if (oneIn is double o && o > 0) return o < 10 ? 1 : o <= 50 ? 2 : 3;
        return grade is "" or "Common" or "Normal" ? 1 : 2;
    }

    static int BaseOf(string kind) => kind switch { QuestKind.Kinah => 10, QuestKind.Material => 8, QuestKind.Runs => 12, QuestKind.BestRun => 40, QuestKind.Gather => 6, _ => 5 };
    static double TierMult(int tier) => tier switch { >= 3 => 2.5, 2 => 1.6, _ => 1 };

    /// <summary>Base points by kind × the step (the chain's first step 1, each step up one more) × the tier × the horizon, whole.</summary>
    public static int PointsFor(string kind, string horizon, long target, int tier)
    {
        if (kind == QuestKind.BestRun) return (int)Math.Round(BaseOf(kind) * TierMult(tier) * Horizon.Mult(horizon));
        int step = StepIndex(ChainOf(kind, horizon), target);
        return (int)Math.Round(BaseOf(kind) * (1 + step) * TierMult(tier) * Horizon.Mult(horizon));
    }

    /* ---------------- the live quests ---------------- */

    /// <summary>
    /// The quests on the list, in the order the surface shows them: Kinah this session first, then the two crafting
    /// materials (the kind last picked up, then the most valuable other stack), then the rest by how soon the pace reaches
    /// them: the other materials, gathering, the runs at each place today, beating the best run there. The longer horizons
    /// (Kinah today and this week, a material today, the runs this week) are the trees' branches (Steven: "only ever show
    /// the Next up but you can expand them and they tree out"); they complete and credit all the same (<see cref="Advance"/>).
    /// Each is the next unreached step of its chain, so one step per chain is live at a time (the default put to Steven on
    /// 2026-11-08). The session's quests wait for <see cref="MinSeconds"/> of play; the runs' do not.
    /// </summary>
    public static List<Quest> Live(QuestInputs x)
    {
        var top = new List<Quest>();
        var rest = new List<Quest>();
        // the session's quests wait for two minutes of play (no pace before); today's and the week's are there as soon as something is counted
        bool session = x.Session.Seconds >= MinSeconds;
        // Kinah this session: the top row (today's and the week's are its branches)
        if (session) top.Add(Make(QuestKind.Kinah, Horizon.Session, "", NextStep(KinahSession, x.Session.Kinah), 1, x.Session.Kinah, x.Session.Seconds));
        // the two materials under the Kinah (Steven, 2026-11-08): what you are farming now, then the most valuable other stack
        var picked = PickMaterials(x);
        var matRows = session ? picked.Select(m => Make(QuestKind.Material, Horizon.Session, m.Name, NextStep(StackChain, x.Session.Item(m.Name)), m.Tier, x.Session.Item(m.Name), x.Session.Seconds)).ToList() : new();
        top.AddRange(matRows.Take(2));
        rest.AddRange(matRows.Skip(2));
        // runs at a place today (this week's are the branch); beat your best run here
        foreach (var (place, n) in x.Today.Runs.OrderByDescending(kv => kv.Value))
            if (n > 0) rest.Add(Make(QuestKind.Runs, Horizon.Today, place, NextStep(RunsChain, n), 1, n, x.Today.Seconds));
        foreach (var b in x.BestRuns.Where(b => b.Best > 0 && b.Latest <= b.Best))
            rest.Add(BestQuest(b));
        // gathering attempts this session (today's is the branch)
        if (session && x.Session.Gathers > 0) rest.Add(Make(QuestKind.Gather, Horizon.Session, "", NextStep(GatherChain, x.Session.Gathers), 1, x.Session.Gathers, x.Session.Seconds));
        top.AddRange(rest.OrderBy(q => q.SecondsToGo ?? double.MaxValue).ThenBy(q => q.Points));
        return top.GroupBy(q => q.Key).Select(g => g.First()).ToList();
    }

    /// <summary>The materials with a quest (at most <see cref="MaterialMax"/>), in the rows' order: the kind last picked up, then by the stack's worth, then by count.</summary>
    public static List<QuestMaterial> PickMaterials(QuestInputs x)
    {
        var mats = x.Materials.Where(m => x.Session.Item(m.Name) >= MaterialMin).ToList();
        var picked = new List<QuestMaterial>();
        var lastName = x.LastFarmed.FirstOrDefault(n => mats.Any(m => string.Equals(m.Name, n, StringComparison.OrdinalIgnoreCase)));
        if (lastName != null) picked.Add(mats.First(m => string.Equals(m.Name, lastName, StringComparison.OrdinalIgnoreCase)));
        foreach (var m in mats.OrderByDescending(m => m.Worth * x.Session.Item(m.Name)).ThenByDescending(m => x.Session.Item(m.Name)).ThenBy(m => m.Name, StringComparer.Ordinal))
            if (!picked.Contains(m)) picked.Add(m);
        return picked.Take(MaterialMax).ToList();
    }

    static Quest BestQuest(BestHere b) => new(QuestKind.BestRun, Horizon.Today, b.Place, b.Best + 1, 2, PointsFor(QuestKind.BestRun, Horizon.Today, b.Best + 1, 2), b.Latest, null);

    /// <summary>A quest at a step of its chain, with its progress and the time to go at the horizon's pace (none before a minute, none with nothing counted).</summary>
    static Quest Make(string kind, string horizon, string subject, long target, int tier, long have, double seconds)
    {
        double? eta = have > 0 && seconds >= 60 && target > have ? (target - have) / (have / seconds) : null;
        return new Quest(kind, horizon, subject, target, tier, PointsFor(kind, horizon, target, tier), have, eta);
    }

    /// <summary>
    /// A quest's tree: itself, its chain upward (the next <see cref="UpSteps"/> steps, what finishing it unlocks) and its
    /// branches (the same thing over the longer horizons, as far as they are counted: a session quest branches to today
    /// and, for Kinah and runs, the week).
    /// </summary>
    public static QuestTree Tree(Quest q, QuestInputs x)
    {
        var up = new List<Quest>();
        var chain = ChainOf(q.Kind, q.Horizon);
        var c = x.Of(q.Horizon);
        for (long t = q.Target, i = 0; i < UpSteps && chain.Length > 0; i++)
        {
            t = NextStep(chain, t);
            up.Add(new Quest(q.Kind, q.Horizon, q.Subject, t, q.Tier, PointsFor(q.Kind, q.Horizon, t, q.Tier), q.Have, null));
        }
        var branches = new List<Quest>();
        if (q.Kind != QuestKind.BestRun)
            foreach (var h in Horizon.All)
            {
                if (Horizon.Rank(h) <= Horizon.Rank(q.Horizon)) continue;
                if (q.Kind == QuestKind.Runs && h == Horizon.Session) continue;
                if (q.Kind is QuestKind.Gather or QuestKind.Material && h == Horizon.Week) continue;
                var hc = x.Of(h);
                long have = q.Kind switch { QuestKind.Kinah => hc.Kinah, QuestKind.Material => hc.Item(q.Subject), QuestKind.Gather => hc.Gathers, _ => hc.RunsAt(q.Subject) };
                branches.Add(Make(q.Kind, h, q.Subject, NextStep(ChainOf(q.Kind, h), have), q.Tier, have, hc.Seconds));
            }
        return new QuestTree(q, up, branches);
    }

    /* ---------------- completion and the score ---------------- */

    /// <summary>The scope a quest is done once in: the session (its start), the day, or the ISO week.</summary>
    public static string ScopeOf(string horizon, DateTime sessionStartUtc, DateTime nowLocal) => horizon switch
    {
        Horizon.Week => "w:" + ISOWeek.GetYear(nowLocal).ToString(CultureInfo.InvariantCulture) + "-W" + ISOWeek.GetWeekOfYear(nowLocal).ToString("00", CultureInfo.InvariantCulture),
        Horizon.Today => "d:" + nowLocal.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        _ => "s:" + sessionStartUtc.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture),
    };

    /// <summary>
    /// One look at the counts: every chain step the counts have passed and not yet in the log for its scope is added to it,
    /// with its points (a session quest's after the cap: <see cref="CapPerHour"/> × the hours played this session, less what
    /// the session's quests have earned), and returned, so the window can say it. The caller saves the log. A count that jumped (4 to 60 at once)
    /// credits every step passed, each once. Before <see cref="MinSeconds"/> nothing is credited (no quest was live).
    /// </summary>
    public static List<QuestDone> Advance(QuestLog log, QuestInputs x, DateTime nowLocal)
    {
        var done = new List<QuestDone>();
        foreach (var q in Reached(x))
        {
            var scope = ScopeOf(q.Horizon, x.SessionStartUtc, nowLocal);
            if (log.Done.Any(d => d.Key == q.Key && d.Scope == scope)) continue;
            int credit = q.Horizon == Horizon.Session ? Math.Clamp(Math.Min(q.Points, CapLeft(log, x)), 0, q.Points) : q.Points;
            var d = new QuestDone { Key = q.Key, Kind = q.Kind, Horizon = q.Horizon, Subject = q.Subject, Target = q.Target, Tier = q.Tier, Points = credit, Worth = q.Points, Have = q.Have, DoneAtUtc = x.NowUtc, Scope = scope };
            log.Done.Add(d); log.AllTimePoints += credit;
            done.Add(d);
        }
        Prune(log, x.NowUtc);
        return done;
    }

    /// <summary>What the session's quests can still earn under the cap: the hours played (rounded up) × the cap, less what they have earned.</summary>
    public static int CapLeft(QuestLog log, QuestInputs x)
    {
        int hours = Math.Max(1, (int)Math.Ceiling(x.Session.Seconds / 3600.0));
        long earned = log.Done.Where(d => d.Horizon == Horizon.Session && d.DoneAtUtc >= x.SessionStartUtc).Sum(d => (long)d.Points);
        return (int)Math.Clamp(hours * (long)CapPerHour - earned, 0, int.MaxValue);
    }

    /// <summary>Every step of every chain the counts have passed (the steps below the live one), so a count that jumped credits each once; the session's steps only after <see cref="MinSeconds"/>.</summary>
    static IEnumerable<Quest> Reached(QuestInputs x)
    {
        IEnumerable<Quest> Steps(string kind, string horizon, string subject, int tier, long have)
        {
            if (horizon == Horizon.Session && x.Session.Seconds < MinSeconds) yield break;
            var chain = ChainOf(kind, horizon);
            for (long t = NextStep(chain, 0); t > 0 && t <= have; t = NextStep(chain, t))
                yield return new Quest(kind, horizon, subject, t, tier, PointsFor(kind, horizon, t, tier), have, null);
        }
        foreach (var h in Horizon.All) foreach (var q in Steps(QuestKind.Kinah, h, "", 1, x.Of(h).Kinah)) yield return q;
        // the materials with a live quest (the picked ones), so points come only from what the list showed
        var picked = PickMaterials(x);
        for (int i = 0; i < picked.Count; i++)
            foreach (var h in i < MaterialToday ? new[] { Horizon.Session, Horizon.Today } : new[] { Horizon.Session })
                foreach (var q in Steps(QuestKind.Material, h, picked[i].Name, picked[i].Tier, x.Of(h).Item(picked[i].Name))) yield return q;
        foreach (var h in new[] { Horizon.Today, Horizon.Week }) foreach (var (place, n) in x.Of(h).Runs) foreach (var q in Steps(QuestKind.Runs, h, place, 1, n)) yield return q;
        foreach (var h in new[] { Horizon.Session, Horizon.Today }) foreach (var q in Steps(QuestKind.Gather, h, "", 1, x.Of(h).Gathers)) yield return q;
        foreach (var b in x.BestRuns.Where(b => b.Best > 0 && b.Latest > b.Best)) yield return BestQuest(b);
    }

    /// <summary>The score from the log: the points this session (since its start), today, this week (from Monday), all time.</summary>
    public static QuestScore Score(QuestLog log, DateTime sessionStartUtc, DateTime nowLocal)
    {
        var day = nowLocal.Date; var offset = nowLocal - nowLocal.ToUniversalTime();
        var weekStart = day.AddDays(-(((int)day.DayOfWeek + 6) % 7));
        long s = 0, t = 0, w = 0;
        foreach (var d in log.Done)
        {
            var local = (d.DoneAtUtc + offset).Date;
            if (d.DoneAtUtc >= sessionStartUtc) s += d.Points;
            if (local == day) t += d.Points;
            if (local >= weekStart && local <= day) w += d.Points;
        }
        return new QuestScore(s, t, w, log.AllTimePoints);
    }

    /// <summary>The completed quests of this session, newest first (the Done fold).</summary>
    public static List<QuestDone> DoneThisSession(QuestLog log, DateTime sessionStartUtc) => log.Done.Where(d => d.DoneAtUtc >= sessionStartUtc).OrderByDescending(d => d.DoneAtUtc).ToList();

    static void Prune(QuestLog log, DateTime nowUtc) => log.Done.RemoveAll(d => d.DoneAtUtc < nowUtc.AddDays(-KeepDays));

    /* ---------------- words ---------------- */

    /// <summary>The quest's title: "1M this session", "100 Spiritstone", "5 Urugugu Canyon runs", "Beat your best run · Urugugu Canyon", "50 gathers".</summary>
    public static string Title(Quest q, Func<double, string> fk) => q.Kind switch
    {
        QuestKind.Kinah => $"{fk(q.Target)} {Horizon.Word(q.Horizon)}",
        QuestKind.Material => $"{N(q.Target)} {q.Subject}",
        QuestKind.Runs => $"{N(q.Target)} {q.Subject} {(q.Target == 1 ? "run" : "runs")}",
        QuestKind.BestRun => $"Beat your best run · {q.Subject}",
        QuestKind.Gather => $"{N(q.Target)} gathers",
        _ => q.Key,
    };

    /// <summary>The title of a completed quest, from its record.</summary>
    public static string Title(QuestDone d, Func<double, string> fk) => Title(AsQuest(d), fk);

    public static Quest AsQuest(QuestDone d) => new(d.Kind, d.Horizon, d.Subject, d.Target, d.Tier, d.Worth, d.Have, null);

    /// <summary>An amount in the quest's own words: Kinah short ("1.2M"), else a count.</summary>
    public static string Amount(Quest q, long n, Func<double, string> fk) => q.Kind is QuestKind.Kinah or QuestKind.BestRun ? fk(n) : N(n);

    /// <summary>"12 pts", "+12 pts".</summary>
    public static string Pts(long n, bool plus = false) => (plus && n > 0 ? "+" : "") + n.ToString("N0", CultureInfo.InvariantCulture) + " pts";

    public static string TierWord(int tier) => tier switch { >= 3 => "rare", 2 => "uncommon", _ => "common" };

    static string N(long n) => n.ToString("N0", CultureInfo.InvariantCulture);

    /* ---------------- quests.json ---------------- */

    public static string PathFor() => Path.Combine(CrashLog.Dir, "quests.json");

    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    /// <summary>The log as quests.json holds it; a fresh one with no file, or with a broken one (which is first copied aside as quests.json.bad, so nothing is lost silently).</summary>
    public static QuestLog Load(string? path = null)
    {
        try
        {
            var p = path ?? PathFor();
            if (!File.Exists(p)) return new QuestLog();
            var log = JsonSerializer.Deserialize<QuestLog>(File.ReadAllText(p), Json);
            if (log == null) { KeepBad(p); return new QuestLog(); }
            log.Done ??= new();
            log.Done.RemoveAll(d => d == null || string.IsNullOrEmpty(d.Key) || string.IsNullOrEmpty(d.Scope));
            foreach (var d in log.Done) { d.Kind ??= ""; d.Horizon ??= ""; d.Subject ??= ""; }
            return log;
        }
        catch (Exception ex)
        {
            CrashLog.Write("quests load", ex);
            KeepBad(path ?? PathFor());
            return new QuestLog();
        }
    }

    /// <summary>Copies a file that cannot be read as a log to quests.json.bad (over an older one); if even that fails, nothing more is done.</summary>
    static void KeepBad(string p)
    {
        try { File.Copy(p, p + ".bad", overwrite: true); } catch (Exception) { }
    }

    /// <summary>Writes the log (to a temporary file first, so a crash mid-write never leaves half a file).</summary>
    public static void Save(QuestLog log, string? path = null)
    {
        try
        {
            var p = path ?? PathFor();
            Directory.CreateDirectory(Path.GetDirectoryName(p)!);
            var tmp = p + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(log, Json));
            File.Move(tmp, p, overwrite: true);
        }
        catch (Exception ex) { CrashLog.Write("quests save", ex); }
    }
}
