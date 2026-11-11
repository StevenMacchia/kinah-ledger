using System.Globalization;
using System.Text.Json;

namespace KinahLedger.Session;

/// <summary>
/// The quest engine (phase 1 of the quest system, 2026-11-08; the design is the side-session note quest-score-design.md).
/// A quest is a goal the tracker sets from what the player is doing, worth points when reached. The engine is pure: the
/// window hands it <see cref="QuestInputs"/> (the counts the tracker already keeps: this session's, today's and the week's
/// Kinah, the crafting materials picked up, the runs and best runs by place, gathering attempts, and the all-time tally it
/// folds day by day) and reads back the live quests in the order the surface shows them, each one's tree, the ones just
/// completed and the score. Nothing here touches WPF, the chat or the network. What is kept (the completed quests, the
/// pins, the all-time tally and the points) lives in quests.json beside goals.json; the completed quests go up with the
/// next upload batch (Upload/RunPayload.cs) once sent, never twice.
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

/// <summary>How long a quest counts over: this count (the session), today from midnight on this PC, this week, or all time (the tally this PC has kept).</summary>
public static class Horizon
{
    public const string Session = "session", Today = "today", Week = "week", AllTime = "alltime";
    public static readonly string[] All = { Session, Today, Week, AllTime };
    /// <summary>A quest over a longer horizon is worth more for the same figure (Steven, 2026-11-08: far fewer points overall, so the multipliers stay small).</summary>
    public static double Mult(string h) => h switch { AllTime => 8, Week => 4, Today => 2, _ => 1 };
    public static string Word(string h) => h switch { AllTime => "all time", Week => "this week", Today => "today", _ => "this session" };
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
/// is and how rare. <paramref name="LastFarmed"/> is the kinds last picked up, newest first (the feed): the material rows
/// follow it. <paramref name="AllTime"/> is the tally quests.json keeps (<see cref="Quests.AllTimeCounts"/>), null when the
/// window has none yet.
/// </summary>
public sealed record QuestInputs(QuestCounts Session, QuestCounts Today, QuestCounts Week, IReadOnlyList<QuestMaterial> Materials, IReadOnlyList<string> LastFarmed,
    IReadOnlyList<BestHere> BestRuns, DateTime SessionStartUtc, DateTime NowUtc, QuestCounts? AllTime = null)
{
    public QuestCounts Of(string horizon) => horizon switch { Horizon.AllTime => AllTime ?? QuestCounts.None, Horizon.Week => Week, Horizon.Today => Today, _ => Session };
}

/// <summary>
/// One quest, live or passed: its kind, horizon, subject (an item or a place; "" for Kinah and gathering), target, tier,
/// the points it is worth, and how far it is in the counts given.
/// </summary>
public sealed record Quest(string Kind, string Horizon, string Subject, long Target, int Tier, int Points, long Have, double? SecondsToGo)
{
    /// <summary>"material:session:Spiritstone:100": one per chain step; the scope (<see cref="Quests.ScopeOf"/>) says which session, day or week.</summary>
    public string Key => Quests.KeyOf(Kind, Horizon, Subject, Target);
    /// <summary>"material:Spiritstone": the chain this step is on, whatever the horizon (what a pin names).</summary>
    public string Chain => Quests.ChainKey(Kind, Subject);
    public bool Done => Target > 0 && Have >= Target;
    public long ToGo => Math.Max(0, Target - Have);
    public double Share => Target <= 0 ? 0 : Math.Clamp(Have / (double)Target, 0, 1);
}

/// <summary>A quest's tree: the quest, its chain upward (the next steps it unlocks, nearest first) and its branches (the same thing over the longer horizons).</summary>
public sealed record QuestTree(Quest Node, IReadOnlyList<Quest> Up, IReadOnlyList<Quest> Branches);

/// <summary>
/// A quest completed: what it was, when, the count behind it, the points credited (after the hour's cap) and the scope it
/// was done in (the session's start, the day, the week), so each is done once per scope; and whether the upload has
/// carried it to the site.
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
    /// <summary>"s:2026-11-08T14:02:11.250Z" for a session quest (the count's start, to the millisecond), "d:2026-11-08" for today's, "w:2026-W45" for the week's, "a" for all time.</summary>
    public string Scope { get; set; } = "";
    /// <summary>Carried to the site in an upload batch (phase 2); an all-time quest is the site's own and is never sent.</summary>
    public bool Sent { get; set; }
    /// <summary>"2026-11-08": a gold or Epic session step credited outside the hour's cap, on that local day ("" for every other); a later session that day climbs the same item under the cap (<see cref="Quests.FreeToday"/>).</summary>
    public string FreeDay { get; set; } = "";
}

/// <summary>The all-time tally this PC keeps (quests.json): the days folded so far, and today's counts as last seen, so the total is the sum of both.</summary>
public sealed class AllTimeTally
{
    /// <summary>The day <see cref="DayKinah"/> and the other Day figures belong to ("2026-11-08"); a new day folds them into the totals first.</summary>
    public string Day { get; set; } = "";
    public long Kinah { get; set; }
    public Dictionary<string, long> Items { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, int> Runs { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public long Gathers { get; set; }
    public long DayKinah { get; set; }
    public Dictionary<string, long> DayItems { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, int> DayRuns { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public long DayGathers { get; set; }
}

/// <summary>quests.json: the completed quests (the last <see cref="Quests.KeepDays"/> days), the pins, the all-time tally and the all-time points.</summary>
public sealed class QuestLog
{
    public int Version { get; set; } = 2;
    public long AllTimePoints { get; set; }
    public List<QuestDone> Done { get; set; } = new();
    /// <summary>Chains the player pinned to Next up ("material:Spiritstone", "gather:", "runs:Urugugu Canyon"): a pin takes a material row.</summary>
    public List<string> Pins { get; set; } = new();
    public AllTimeTally AllTime { get; set; } = new();
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
    // all time (Steven, 2026-11-08: "all time tracking quests that get really high for materials gathered etc")
    public static readonly long[] StackAllTime = { 1_000, 2_000, 5_000, 10_000, 25_000, 50_000, 100_000 };
    public static readonly long[] KinahAllTime = { 100_000_000, 250_000_000, 500_000_000, 1_000_000_000, 2_500_000_000, 5_000_000_000 };
    public static readonly long[] RunsAllTime = { 100, 250, 500, 1_000 };
    public static readonly long[] GatherAllTime = { 1_000, 2_500, 5_000, 10_000 };

    // a rarer material needs fewer and pays more (Steven, 2026-11-08: "more rare items should require less to complete but
    // give more points -- 5 being the max per session for gold but rewarding like 50"). Common climbs the stack chains above;
    // each rarer grade has its own, shorter and lower, over the session, today and all time. Only gold was anchored by
    // Steven (1, 2, 3, 5 this session, 5 worth about 50); the green, blue and violet ladders are a first cut to tune.
    public const int TierCommon = 1, TierRare = 2, TierLegend = 3, TierUnique = 4, TierEpic = 5;
    static readonly long[][] SessionByTier = { StackChain, new long[] { 3, 5, 10, 20, 30, 50, 100 }, new long[] { 1, 2, 3, 5, 10, 20 }, new long[] { 1, 2, 3, 5 }, new long[] { 1, 2, 3 } };
    static readonly long[][] TodayByTier = { StackChain, new long[] { 5, 10, 20, 50, 100, 200 }, new long[] { 2, 5, 10, 20, 50 }, new long[] { 1, 3, 5, 10 }, new long[] { 1, 2, 3, 5 } };
    static readonly long[][] AllTimeByTier = { StackAllTime, new long[] { 100, 250, 500, 1_000, 2_500, 5_000 }, new long[] { 25, 50, 100, 250, 500 }, new long[] { 10, 25, 50, 100 }, new long[] { 5, 10, 25, 50 } };
    /// <summary>A rarer grade's points by step, before the horizon (common keeps <see cref="StepPts"/> × the kind): gold's 1, 2, 3, 5 are 10, 20, 30, 50, so a gold step always beats the same count of a lower grade.</summary>
    static readonly int[][] PtsByTier = { Array.Empty<int>(), new[] { 2, 3, 4, 6, 8, 10, 15 }, new[] { 4, 6, 8, 12, 18, 25 }, new[] { 10, 20, 30, 50 }, new[] { 20, 35, 50 } };

    /// <summary>The session's ladder stops at its last step for gold and above (5 gold is the session's most); the others, and every today and all-time ladder, go on past their end.</summary>
    public static bool Capped(string kind, string horizon, int tier) => kind == QuestKind.Material && horizon == Horizon.Session && tier >= TierUnique;

    /// <summary>The next step of a chain above <paramref name="have"/> for this kind, horizon and tier; 0 when a capped ladder is finished (no quest left on it).</summary>
    public static long Next(string kind, string horizon, int tier, long have)
    {
        var chain = ChainOf(kind, horizon, tier);
        return Capped(kind, horizon, tier) && have >= chain[^1] ? 0 : NextStep(chain, have);
    }

    /// <summary>How many of a material the session needs before it gets a quest: one of a Legend or rarer (one drop is the event), else <see cref="MaterialMin"/>.</summary>
    public static int MinFor(int tier) => tier >= TierLegend ? 1 : MaterialMin;

    /// <summary>Gold and above earn outside the hour's cap: a rare drop is luck, not grinding. Legend stays under it (blue is farmed in volume and its steps are small).</summary>
    public static bool CapFree(string kind, int tier) => kind == QuestKind.Material && tier >= TierUnique;

    /// <summary>
    /// Whether this session's step of a gold or Epic item is credited outside the cap: only in the first session that day to
    /// climb that item's ladder (Steven, 2026-11-08: Wrathful Ego is gold and drops by the hundreds, so every restart paid
    /// 1, 2, 3, 5 again for 110 points). A later session that day still has the quests; their points go under the cap.
    /// </summary>
    public static bool FreeToday(QuestLog log, Quest q, string scope, DateTime nowLocal)
    {
        if (q.Horizon != Horizon.Session || !CapFree(q.Kind, q.Tier)) return false;
        var day = DayKey(nowLocal);
        return !log.Done.Any(d => d.FreeDay == day && d.Scope != scope && d.Kind == q.Kind && string.Equals(d.Subject, q.Subject, StringComparison.OrdinalIgnoreCase));
    }

    static string DayKey(DateTime local) => local.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>How many steps of a chain a tree shows above the live one.</summary>
    public const int UpSteps = 3;
    /// <summary>Points an hour of play can earn from the session's quests at most, so grinding one trivial quest never outscores real farming; today's and the week's quests are few and coarse and are not capped.</summary>
    public const int CapPerHour = 60;
    /// <summary>Completed quests older than this leave quests.json (the all-time total keeps their points).</summary>
    public const int KeepDays = 60;
    /// <summary>A material needs this many picked up in the session before it gets a quest (a stray drop is not something you are farming).</summary>
    public const int MaterialMin = 3;
    /// <summary>How many materials have a live quest at once: the row at rest and four more on the Quests page, so the list is never a wall (a demo session had 13).</summary>
    public const int MaterialMax = 5;
    /// <summary>How many of them also have a today quest (the one at rest and the next).</summary>
    public const int MaterialToday = 2;
    /// <summary>Next up shows this many bars and no more (Steven, 2026-11-08, after "maximum of 3": "have 2 quests on the main page"): Kinah this session, then the material last farmed (or a pin).</summary>
    public const int NextUpRows = 2;
    /// <summary>Nothing before this much play: the first two minutes have no pace.</summary>
    public const int MinSeconds = 120;

    public static string KeyOf(string kind, string horizon, string subject, long target) => $"{kind}:{horizon}:{subject}:{target}";
    public static string ChainKey(string kind, string subject) => $"{kind}:{subject}";

    /// <summary>The chain a kind climbs over a horizon; a material's by its tier (<see cref="SessionByTier"/>).</summary>
    public static long[] ChainOf(string kind, string horizon, int tier = TierCommon) => kind switch
    {
        QuestKind.Kinah => horizon switch { Horizon.AllTime => KinahAllTime, Horizon.Week => KinahWeek, Horizon.Today => KinahToday, _ => KinahSession },
        QuestKind.Runs => horizon == Horizon.AllTime ? RunsAllTime : RunsChain,
        QuestKind.Gather => horizon == Horizon.AllTime ? GatherAllTime : GatherChain,
        QuestKind.BestRun => Array.Empty<long>(),
        _ => (horizon switch { Horizon.AllTime => AllTimeByTier, Horizon.Session => SessionByTier, _ => TodayByTier })[Math.Clamp(tier, TierCommon, TierEpic) - 1],
    };

    /// <summary>The next step of a chain above <paramref name="have"/>; past the chain's end, a step of its own size (25 runs, round thousands, the last Kinah step's size again).</summary>
    public static long NextStep(long[] chain, long have)
    {
        foreach (var m in chain) if (m > have) return m;
        if (chain.Length == 0) return 0;
        long last = chain[^1];
        long step = last < 100 ? 25 : last < 100_000 ? (have < 10_000 ? 1_000 : 10_000) : last >= 1_000_000_000 ? 1_000_000_000 : last >= 100_000_000 ? 100_000_000 : last >= 10_000_000 ? 10_000_000 : 1_000_000;
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

    /// <summary>The tier a grade is: Common 1, Rare (green) 2, Legend (blue) 3, Unique (gold) 4, Epic and above 5.</summary>
    public static int GradeTier(string grade) => grade switch
    {
        "Epic" or "Mythic" or "Ultimate" => TierEpic,
        "Unique" or "Heroic" => TierUnique,
        "Legend" or "Legendary" => TierLegend,
        "Rare" => TierRare,
        _ => TierCommon,
    };

    /// <summary>
    /// A material's tier: its grade leads (<see cref="GradeTier"/>); the drop odds the app has ("1 in 54" is
    /// <paramref name="oneIn"/> 54) lift a Common or Rare one step when rarer than 1 in 50, never more, and never into gold,
    /// whose points are the grade's own (0.10.30; before, odds alone could make a Common item the top tier).
    /// </summary>
    public static int TierOf(double? oneIn, string grade)
    {
        int t = GradeTier(grade);
        return t <= TierRare && oneIn is double o && o > 50 ? t + 1 : t;
    }

    /// <summary>
    /// The points a chain's step is worth before the kind, the tier and the horizon: 1, 1, 2, 3, 5, 6, 8, 10, 15, 25, 40, then
    /// five more a step. Small on purpose (Steven, 2026-11-08: "way less point totals overall"): a session is tens of points,
    /// a heavy week about a thousand.
    /// </summary>
    static readonly int[] StepPts = { 1, 1, 2, 3, 5, 6, 8, 10, 15, 25, 40 };
    /// <summary>Kinah is what farming is for, so its steps are worth twice a run's and three times a material's (Steven: "not enough for kinah farming").</summary>
    static double KindScale(string kind) => kind switch { QuestKind.Kinah => 2, QuestKind.Material => 0.6, QuestKind.Runs => 1, QuestKind.Gather => 0.5, _ => 1 };
    static double TierMult(int tier) => tier switch { >= 3 => 2, 2 => 1.5, _ => 1 };
    /// <summary>Beating your best run at a place, flat.</summary>
    public const int BestRunPoints = 10;

    /// <summary>The step's points × the kind × the tier × the horizon, whole and at least one.</summary>
    public static int PointsFor(string kind, string horizon, long target, int tier)
    {
        if (kind == QuestKind.BestRun) return BestRunPoints;
        int step = StepIndex(ChainOf(kind, horizon, tier), target);
        if (kind == QuestKind.Material && tier > TierCommon)
        {
            var t = PtsByTier[Math.Min(tier, TierEpic) - 1];
            double p = step < t.Length ? t[step] : t[^1] + 5 * (step - t.Length + 1);
            return Math.Max(1, (int)Math.Round(p * Horizon.Mult(horizon)));
        }
        double basePts = step < StepPts.Length ? StepPts[step] : StepPts[^1] + 5 * (step - StepPts.Length + 1);
        return Math.Max(1, (int)Math.Round(basePts * KindScale(kind) * TierMult(tier) * Horizon.Mult(horizon)));
    }

    /* ---------------- the live quests ---------------- */

    /// <summary>
    /// The quests on the list, in the order the surface shows them: Kinah this session first; then the pinned chains' live
    /// steps and the crafting materials in the order they were last farmed (the rarer of two variants of a kind wins the
    /// row); then the rest by how soon the pace reaches them: the other materials, gathering, the runs at each place
    /// today, beating the best run there. Next up shows the first <see cref="NextUpRows"/>; the Quests page shows them all.
    /// The longer horizons (Kinah today and this week, a material today, the runs this week, all time) are the trees'
    /// branches and the Quests page's Daily and All time; they complete and credit all the same (<see cref="Advance"/>).
    /// Each is the next unreached step of its chain, so one step per chain is live at a time. The session's quests wait
    /// for <see cref="MinSeconds"/> of play; the runs' do not.
    /// </summary>
    public static List<Quest> Live(QuestInputs x, IReadOnlyList<string>? pins = null)
    {
        var top = new List<Quest>();
        var rest = new List<Quest>();
        bool session = x.Session.Seconds >= MinSeconds;
        if (session) top.Add(Make(QuestKind.Kinah, Horizon.Session, "", NextStep(KinahSession, x.Session.Kinah), 1, x.Session.Kinah, x.Session.Seconds));
        // the pins first: a pinned chain's live step takes a material row
        var pinned = new List<Quest>();
        foreach (var pin in pins ?? Array.Empty<string>())
        {
            var q = PinnedLive(pin, x);
            if (q != null && pinned.All(p => p.Key != q.Key)) pinned.Add(q);
        }
        var picked = PickMaterials(x);
        // a gold ladder done (5 this session) has no row left: the next material takes it
        var matRows = session ? picked.Select(m => (m, t: Next(QuestKind.Material, Horizon.Session, m.Tier, x.Session.Item(m.Name)))).Where(p => p.t > 0)
            .Select(p => Make(QuestKind.Material, Horizon.Session, p.m.Name, p.t, p.m.Tier, x.Session.Item(p.m.Name), x.Session.Seconds)).ToList() : new();
        var rows = new List<Quest>();
        rows.AddRange(pinned);
        foreach (var m in matRows) if (rows.All(r => r.Chain != m.Chain)) rows.Add(m);
        top.AddRange(rows.Take(NextUpRows - 1));
        rest.AddRange(rows.Skip(NextUpRows - 1));
        foreach (var (place, n) in x.Today.Runs.OrderByDescending(kv => kv.Value))
            if (n > 0 && rows.All(r => r.Chain != ChainKey(QuestKind.Runs, place))) rest.Add(Make(QuestKind.Runs, Horizon.Today, place, NextStep(RunsChain, n), 1, n, x.Today.Seconds));
        foreach (var b in x.BestRuns.Where(b => b.Best > 0 && b.Latest <= b.Best))
            rest.Add(BestQuest(b));
        if (session && x.Session.Gathers > 0 && rows.All(r => r.Kind != QuestKind.Gather)) rest.Add(Make(QuestKind.Gather, Horizon.Session, "", NextStep(GatherChain, x.Session.Gathers), 1, x.Session.Gathers, x.Session.Seconds));
        top.AddRange(rest.OrderBy(q => q.SecondsToGo ?? double.MaxValue).ThenBy(q => q.Points));
        return top.GroupBy(q => q.Key).Select(g => g.First()).ToList();
    }

    /// <summary>A pinned chain's live step from the counts: "material:Spiritstone" (this session, once it has a count), "gather:" (this session), "runs:Place" (today). Null when the pin names nothing counted.</summary>
    public static Quest? PinnedLive(string pin, QuestInputs x)
    {
        int cut = pin.IndexOf(':');
        if (cut < 0) return null;
        var kind = pin[..cut]; var subject = pin[(cut + 1)..];
        switch (kind)
        {
            case QuestKind.Material:
            {
                var m = x.Materials.FirstOrDefault(m => string.Equals(m.Name, subject, StringComparison.OrdinalIgnoreCase));
                long have = x.Session.Item(subject);
                if (x.Session.Seconds < MinSeconds) return null;
                int tier = m?.Tier ?? TierCommon;
                long next = Next(QuestKind.Material, Horizon.Session, tier, have);
                return next <= 0 ? null : Make(QuestKind.Material, Horizon.Session, m?.Name ?? subject, next, tier, have, x.Session.Seconds);
            }
            case QuestKind.Gather:
                return x.Session.Seconds < MinSeconds ? null : Make(QuestKind.Gather, Horizon.Session, "", NextStep(GatherChain, x.Session.Gathers), 1, x.Session.Gathers, x.Session.Seconds);
            case QuestKind.Runs:
            {
                int n = x.Today.RunsAt(subject);
                return Make(QuestKind.Runs, Horizon.Today, subject, NextStep(RunsChain, n), 1, n, x.Today.Seconds);
            }
            default: return null;
        }
    }

    /// <summary>The words a variant of a kind carries ("Radiant Orichalcum Ore", "Fine Odyle"): stripped, two variants share a base name.</summary>
    static readonly string[] VariantWords = { "Radiant", "Fine", "Pure", "Greater", "Lesser", "Superior", "Refined", "Major", "Minor", "Ancient", "Brilliant", "Shining", "Dark", "Rough" };

    /// <summary>"Orichalcum Ore" for "Radiant Orichalcum Ore": the kind without its variant word.</summary>
    public static string BaseName(string name)
    {
        foreach (var w in VariantWords)
            if (name.StartsWith(w + " ", StringComparison.OrdinalIgnoreCase) && name.Length > w.Length + 1) return name[(w.Length + 1)..];
        return name;
    }

    /// <summary>
    /// The materials with a quest (at most <see cref="MaterialMax"/>), in the rows' order: gold and rarer first, then the kinds last farmed (the
    /// feed's order, newest first), then by the stack's worth. Rarity counts (Steven, 2026-11-08: "Radiant vs regular"): of
    /// two variants of one kind, the higher tier takes the row and the plain one steps back, whichever was farmed last.
    /// </summary>
    public static List<QuestMaterial> PickMaterials(QuestInputs x)
    {
        var mats = x.Materials.Where(m => x.Session.Item(m.Name) >= MinFor(m.Tier)).ToList();
        int Recency(QuestMaterial m) { for (int i = 0; i < x.LastFarmed.Count; i++) if (string.Equals(x.LastFarmed[i], m.Name, StringComparison.OrdinalIgnoreCase)) return i; return int.MaxValue; }
        // gold and rarer first: only a listed material earns, and a rare drop is the one worth showing (0.10.30)
        var ordered = mats.OrderByDescending(m => m.Tier >= TierUnique).ThenBy(Recency).ThenByDescending(m => m.Worth * x.Session.Item(m.Name)).ThenByDescending(m => x.Session.Item(m.Name)).ThenBy(m => m.Name, StringComparer.Ordinal).ToList();
        var picked = new List<QuestMaterial>();
        foreach (var m in ordered)
        {
            var twin = picked.FirstOrDefault(p => string.Equals(BaseName(p.Name), BaseName(m.Name), StringComparison.OrdinalIgnoreCase));
            if (twin != null)
            {
                // the rarer variant keeps the row; the plain one goes behind it
                if (m.Tier > twin.Tier) { picked[picked.IndexOf(twin)] = m; picked.Add(twin); }
                else picked.Add(m);
                continue;
            }
            picked.Add(m);
        }
        return picked.Take(MaterialMax).ToList();
    }

    static Quest BestQuest(BestHere b) => new(QuestKind.BestRun, Horizon.Today, b.Place, b.Best + 1, 2, BestRunPoints, b.Latest, null);

    /// <summary>A quest at a step of its chain, with its progress and the time to go at the horizon's pace (none before a minute, none with nothing counted).</summary>
    static Quest Make(string kind, string horizon, string subject, long target, int tier, long have, double seconds)
    {
        double? eta = have > 0 && seconds >= 60 && target > have ? (target - have) / (have / seconds) : null;
        return new Quest(kind, horizon, subject, target, tier, PointsFor(kind, horizon, target, tier), have, eta);
    }

    /// <summary>
    /// A quest's tree: itself, its chain upward (the next <see cref="UpSteps"/> steps, what finishing it unlocks) and its
    /// branches (the same thing over the longer horizons, as far as they are counted: a session quest branches to today
    /// and, for Kinah and runs, the week; every kind to all time once the tally has it).
    /// </summary>
    public static QuestTree Tree(Quest q, QuestInputs x)
    {
        var up = new List<Quest>();
        var chain = ChainOf(q.Kind, q.Horizon, q.Tier);
        for (long t = q.Target, i = 0; i < UpSteps && chain.Length > 0; i++)
        {
            t = Next(q.Kind, q.Horizon, q.Tier, t);
            if (t <= 0) break;   // a capped ladder ends: nothing above gold's 5 this session
            up.Add(new Quest(q.Kind, q.Horizon, q.Subject, t, q.Tier, PointsFor(q.Kind, q.Horizon, t, q.Tier), q.Have, null));
        }
        var branches = new List<Quest>();
        if (q.Kind != QuestKind.BestRun)
            foreach (var h in Horizon.All)
            {
                if (Horizon.Rank(h) <= Horizon.Rank(q.Horizon)) continue;
                if (q.Kind == QuestKind.Runs && h == Horizon.Session) continue;
                if (q.Kind is QuestKind.Gather or QuestKind.Material && h == Horizon.Week) continue;
                if (h == Horizon.AllTime && x.AllTime == null) continue;
                var hc = x.Of(h);
                long have = q.Kind switch { QuestKind.Kinah => hc.Kinah, QuestKind.Material => hc.Item(q.Subject), QuestKind.Gather => hc.Gathers, _ => hc.RunsAt(q.Subject) };
                branches.Add(Make(q.Kind, h, q.Subject, Next(q.Kind, h, q.Tier, have), q.Tier, have, hc.Seconds));
            }
        return new QuestTree(q, up, branches);
    }

    /// <summary>Today's repeatable quests (the Quests page's Daily): Kinah today, the shown materials today, the runs at each place today, gathering today; each the next step of its chain.</summary>
    public static List<Quest> Daily(QuestInputs x, IReadOnlyList<string>? pins = null)
    {
        var list = new List<Quest>();
        list.Add(Make(QuestKind.Kinah, Horizon.Today, "", NextStep(KinahToday, x.Today.Kinah), 1, x.Today.Kinah, x.Today.Seconds));
        foreach (var m in ShownMaterials(x, pins)) list.Add(Make(QuestKind.Material, Horizon.Today, m.Name, Next(QuestKind.Material, Horizon.Today, m.Tier, x.Today.Item(m.Name)), m.Tier, x.Today.Item(m.Name), x.Today.Seconds));
        foreach (var (place, n) in x.Today.Runs.OrderByDescending(kv => kv.Value)) list.Add(Make(QuestKind.Runs, Horizon.Today, place, NextStep(RunsChain, n), 1, n, x.Today.Seconds));
        if (x.Today.Gathers > 0) list.Add(Make(QuestKind.Gather, Horizon.Today, "", NextStep(GatherChain, x.Today.Gathers), 1, x.Today.Gathers, x.Today.Seconds));
        return list;
    }

    /// <summary>The all-time tracking quests (the Quests page's All time): Kinah, every material the tally has a thousand or more of (else the top five by count), runs by place, gathering; each the next step of its long chain.</summary>
    public static List<Quest> AllTimeQuests(QuestInputs x, IReadOnlyList<QuestMaterial> known)
    {
        var list = new List<Quest>();
        var a = x.AllTime;
        if (a == null) return list;
        list.Add(Make(QuestKind.Kinah, Horizon.AllTime, "", NextStep(KinahAllTime, a.Kinah), 1, a.Kinah, 0));
        var items = a.Items.OrderByDescending(kv => kv.Value).ToList();
        var shown = items.Where(kv => kv.Value >= 1000).ToList();
        if (shown.Count < 5) shown = items.Take(5).ToList();
        foreach (var (name, n) in shown)
        {
            int tier = known.FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase))?.Tier ?? 1;
            list.Add(Make(QuestKind.Material, Horizon.AllTime, name, Next(QuestKind.Material, Horizon.AllTime, tier, n), tier, n, 0));
        }
        foreach (var (place, n) in a.Runs.OrderByDescending(kv => kv.Value).Take(5)) list.Add(Make(QuestKind.Runs, Horizon.AllTime, place, NextStep(RunsAllTime, n), 1, n, 0));
        if (a.Gathers > 0) list.Add(Make(QuestKind.Gather, Horizon.AllTime, "", NextStep(GatherAllTime, a.Gathers), 1, a.Gathers, 0));
        return list;
    }

    /// <summary>The materials whose rows Next up shows (the pins' first, then the last farmed): the ones with a today quest too.</summary>
    static List<QuestMaterial> ShownMaterials(QuestInputs x, IReadOnlyList<string>? pins)
    {
        var shown = new List<QuestMaterial>();
        foreach (var pin in pins ?? Array.Empty<string>())
            if (pin.StartsWith(QuestKind.Material + ":", StringComparison.Ordinal) && x.Materials.FirstOrDefault(m => string.Equals(m.Name, pin[(QuestKind.Material.Length + 1)..], StringComparison.OrdinalIgnoreCase)) is { } pm && !shown.Contains(pm)) shown.Add(pm);
        foreach (var m in PickMaterials(x)) if (shown.Count < MaterialToday && !shown.Contains(m)) shown.Add(m);
        return shown.Take(MaterialToday).ToList();
    }

    /* ---------------- completion and the score ---------------- */

    /// <summary>The scope a quest is done once in: the session (its start), the day, the ISO week, or ever.</summary>
    public static string ScopeOf(string horizon, DateTime sessionStartUtc, DateTime nowLocal) => horizon switch
    {
        Horizon.AllTime => "a",
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
        foreach (var q in Reached(x, log.Pins))
        {
            var scope = ScopeOf(q.Horizon, x.SessionStartUtc, nowLocal);
            if (log.Done.Any(d => d.Key == q.Key && d.Scope == scope)) continue;
            bool free = FreeToday(log, q, scope, nowLocal);
            int credit = q.Horizon == Horizon.Session && !free ? Math.Clamp(Math.Min(q.Points, CapLeft(log, x)), 0, q.Points) : q.Points;
            var d = new QuestDone { Key = q.Key, Kind = q.Kind, Horizon = q.Horizon, Subject = q.Subject, Target = q.Target, Tier = q.Tier, Points = credit, Worth = q.Points, Have = q.Have, DoneAtUtc = x.NowUtc, Scope = scope, Sent = q.Horizon == Horizon.AllTime, FreeDay = free ? DayKey(nowLocal) : "" };
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
        long earned = log.Done.Where(d => d.Horizon == Horizon.Session && d.DoneAtUtc >= x.SessionStartUtc && d.FreeDay.Length == 0).Sum(d => (long)d.Points);
        return (int)Math.Clamp(hours * (long)CapPerHour - earned, 0, int.MaxValue);
    }

    /// <summary>Every step of every chain the counts have passed (the steps below the live one), so a count that jumped credits each once; the session's steps only after <see cref="MinSeconds"/>.</summary>
    static IEnumerable<Quest> Reached(QuestInputs x, IReadOnlyList<string>? pins)
    {
        IEnumerable<Quest> Steps(string kind, string horizon, string subject, int tier, long have)
        {
            if (horizon == Horizon.Session && x.Session.Seconds < MinSeconds) yield break;
            if (horizon == Horizon.AllTime && x.AllTime == null) yield break;
            for (long t = Next(kind, horizon, tier, 0); t > 0 && t <= have; t = Next(kind, horizon, tier, t))
                yield return new Quest(kind, horizon, subject, t, tier, PointsFor(kind, horizon, t, tier), have, null);
        }
        foreach (var h in Horizon.All) foreach (var q in Steps(QuestKind.Kinah, h, "", 1, x.Of(h).Kinah)) yield return q;
        // the materials with a live quest (the picked ones and the pins), so points come only from what the list showed; today's for the rows shown
        var picked = PickMaterials(x);
        var shown = ShownMaterials(x, pins);
        var withQuest = picked.Concat(shown).GroupBy(m => m.Name, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();
        foreach (var m in withQuest)
            foreach (var h in shown.Contains(m) ? new[] { Horizon.Session, Horizon.Today } : new[] { Horizon.Session })
                foreach (var q in Steps(QuestKind.Material, h, m.Name, m.Tier, x.Of(h).Item(m.Name))) yield return q;
        if (x.AllTime != null)
            foreach (var (name, n) in x.AllTime.Items)
            {
                int tier = x.Materials.FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase))?.Tier ?? 1;
                foreach (var q in Steps(QuestKind.Material, Horizon.AllTime, name, tier, n)) yield return q;
            }
        foreach (var h in new[] { Horizon.Today, Horizon.Week, Horizon.AllTime }) foreach (var (place, n) in x.Of(h).Runs) foreach (var q in Steps(QuestKind.Runs, h, place, 1, n)) yield return q;
        foreach (var h in new[] { Horizon.Session, Horizon.Today, Horizon.AllTime }) foreach (var q in Steps(QuestKind.Gather, h, "", 1, x.Of(h).Gathers)) yield return q;
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

    /// <summary>The completed quests of this session, newest first.</summary>
    public static List<QuestDone> DoneThisSession(QuestLog log, DateTime sessionStartUtc) => log.Done.Where(d => d.DoneAtUtc >= sessionStartUtc).OrderByDescending(d => d.DoneAtUtc).ToList();

    /// <summary>The completed quests by local day, newest day first, each day newest first (the Quests page's Done).</summary>
    public static List<(DateTime Day, List<QuestDone> Done)> DoneByDay(QuestLog log, DateTime nowLocal)
    {
        var offset = nowLocal - nowLocal.ToUniversalTime();
        return log.Done.GroupBy(d => (d.DoneAtUtc + offset).Date).OrderByDescending(g => g.Key)
            .Select(g => (g.Key, g.OrderByDescending(d => d.DoneAtUtc).ToList())).ToList();
    }

    /// <summary>How many days running a today quest of this kind and subject was done, counting today if it was, else from yesterday back; 0 with none.</summary>
    public static int Streak(QuestLog log, string kind, string subject, DateTime todayLocal)
    {
        bool DoneOn(DateTime day) => log.Done.Any(d => d.Horizon == Horizon.Today && d.Kind == kind && string.Equals(d.Subject, subject, StringComparison.OrdinalIgnoreCase) && d.Scope == "d:" + day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        var day = todayLocal.Date;
        if (!DoneOn(day)) day = day.AddDays(-1);
        int n = 0;
        while (DoneOn(day) && n < KeepDays) { n++; day = day.AddDays(-1); }
        return n;
    }

    /// <summary>The completed quests the upload has not carried yet (all-time quests are the site's own and never go).</summary>
    public static List<QuestDone> Unsent(QuestLog log) => log.Done.Where(d => !d.Sent && d.Horizon != Horizon.AllTime).OrderBy(d => d.DoneAtUtc).ToList();

    /// <summary>Marks these as carried (a batch the site took).</summary>
    public static void MarkSent(QuestLog log, IEnumerable<string> keysAndScopes)
    {
        var set = keysAndScopes.ToHashSet(StringComparer.Ordinal);
        foreach (var d in log.Done) if (set.Contains(d.Key + "|" + d.Scope)) d.Sent = true;
    }

    static void Prune(QuestLog log, DateTime nowUtc) => log.Done.RemoveAll(d => d.DoneAtUtc < nowUtc.AddDays(-KeepDays));

    /* ---------------- the all-time tally ---------------- */

    /// <summary>
    /// Folds today's counts into the tally: a new day moves the day figures last seen into the totals first, then the day
    /// figures are today's. The total is the sum of both (<see cref="AllTimeCounts"/>). Days the tracker did not run are
    /// not in it; the site keeps the lifetime totals from the uploads.
    /// </summary>
    public static bool FoldToday(QuestLog log, QuestCounts today, string dayKey)
    {
        var a = log.AllTime ??= new AllTimeTally();
        bool changed = false;
        if (a.Day != dayKey)
        {
            if (a.Day.Length > 0)
            {
                a.Kinah += a.DayKinah; a.Gathers += a.DayGathers;
                foreach (var (k, v) in a.DayItems) a.Items[k] = a.Items.GetValueOrDefault(k) + v;
                foreach (var (k, v) in a.DayRuns) a.Runs[k] = a.Runs.GetValueOrDefault(k) + v;
            }
            a.Day = dayKey; a.DayKinah = 0; a.DayGathers = 0; a.DayItems = new(StringComparer.OrdinalIgnoreCase); a.DayRuns = new(StringComparer.OrdinalIgnoreCase);
            changed = true;
        }
        if (a.DayKinah != today.Kinah || a.DayGathers != today.Gathers || a.DayItems.Count != today.Items.Count || a.DayRuns.Count != today.Runs.Count
            || today.Items.Any(kv => a.DayItems.GetValueOrDefault(kv.Key) != kv.Value) || today.Runs.Any(kv => a.DayRuns.GetValueOrDefault(kv.Key) != kv.Value))
        {
            a.DayKinah = today.Kinah; a.DayGathers = today.Gathers;
            a.DayItems = new Dictionary<string, long>(today.Items, StringComparer.OrdinalIgnoreCase);
            a.DayRuns = new Dictionary<string, int>(today.Runs, StringComparer.OrdinalIgnoreCase);
            changed = true;
        }
        return changed;
    }

    /// <summary>The tally as counts: the folded days plus today's figures.</summary>
    public static QuestCounts AllTimeCounts(QuestLog log)
    {
        var a = log.AllTime ??= new AllTimeTally();
        var items = new Dictionary<string, long>(a.Items, StringComparer.OrdinalIgnoreCase);
        foreach (var (k, v) in a.DayItems) items[k] = items.GetValueOrDefault(k) + v;
        var runs = new Dictionary<string, int>(a.Runs, StringComparer.OrdinalIgnoreCase);
        foreach (var (k, v) in a.DayRuns) runs[k] = runs.GetValueOrDefault(k) + v;
        return new QuestCounts(a.Kinah + a.DayKinah, 0, items, runs, (int)Math.Min(int.MaxValue, a.Gathers + a.DayGathers));
    }

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

    /// <summary>The tier in the game's grade words: common, rare (green), legend (blue), unique (gold), epic.</summary>
    public static string TierWord(int tier) => tier switch { >= TierEpic => "epic", TierUnique => "unique", TierLegend => "legend", TierRare => "rare", _ => "common" };

    static string N(long n) => n.ToString("N0", CultureInfo.InvariantCulture);

    /* ---------------- quests.json ---------------- */

    public static string PathFor() => Path.Combine(Characters.DataDir, "quests.json");

    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    /// <summary>The log as quests.json holds it; a fresh one with no file or a broken one.</summary>
    public static QuestLog Load(string? path = null)
    {
        try
        {
            var p = path ?? PathFor();
            if (!File.Exists(p)) return new QuestLog();
            var log = JsonSerializer.Deserialize<QuestLog>(File.ReadAllText(p), Json) ?? new QuestLog();
            log.Done ??= new();
            log.Pins ??= new();
            log.AllTime ??= new AllTimeTally();
            log.AllTime.Items ??= new(StringComparer.OrdinalIgnoreCase); log.AllTime.Runs ??= new(StringComparer.OrdinalIgnoreCase);
            log.AllTime.DayItems ??= new(StringComparer.OrdinalIgnoreCase); log.AllTime.DayRuns ??= new(StringComparer.OrdinalIgnoreCase);
            log.Done.RemoveAll(d => d == null || string.IsNullOrEmpty(d.Key) || string.IsNullOrEmpty(d.Scope));
            foreach (var d in log.Done) { d.Kind ??= ""; d.Horizon ??= ""; d.Subject ??= ""; d.FreeDay ??= ""; }
            // a version 1 log (0.10.25) was worth about ten times today's points (Steven, 2026-11-08: "way too many"); its history keeps its figures, the score starts over
            if (log.Version < 2) { log.AllTimePoints = 0; foreach (var d in log.Done) { d.Points = 0; d.Sent = true; } log.Version = 2; }
            return log;
        }
        catch (Exception ex)
        {
            CrashLog.Write("quests load", ex);
            // the file is kept as quests.bad.json for a look, so the next save does not write an empty log over the history
            try { var p = path ?? PathFor(); if (File.Exists(p)) File.Move(p, Path.ChangeExtension(p, ".bad.json"), overwrite: true); } catch (Exception) { }
            return new QuestLog();
        }
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
