using KinahLedger.Data;
using KinahLedger.Session;

namespace KinahLedger.Ui;

/// <summary>
/// The panel's crafting line (the mock's feature 2, approved 2026-10-26): while the crafting window or panel is open (the
/// tracker's activity is Crafting), the Card and the Ticker show crafting instead of farming. Every figure comes from the
/// craft ledger, summed as the window's Crafting part sums it (FillCraft): the tries, how many worked and failed, the
/// profit after the Market's cut, what a craft of the queue's recipe is worth (profit a craft and an hour, combos won), and the last crafts with what each made or cost (the
/// feed's CraftValue). The Strip keeps its own crafting line (HudFeedValues).
/// </summary>
public partial class MainWindow
{
    /// <summary>How many crafts the looks get ("craft0" newest to "craft3"): the Card shows three as tiles, the Ticker four toasts.</summary>
    const int HudCrafts = 4;
    static readonly string[] CraftHeadKeys = { "craft", "craftTime", "craftRecipe", "craftRecipeId", "craftIcon", "craftTries", "craftOk", "craftFail", "craftProfit", "craftSells", "craftPer", "craftPerHr", "craftCombos", "craftCombosKind", "craftQueue", "craftQueueText", "craftQueueDone", "craftQueueOf" };
    static readonly string[] CraftLineKeys = { "", "Id", "Item", "Ok", "Worth", "Icon", "At", "Combo" };

    /// <summary>Time with the crafting window open this session, added up tick by tick; the tick it was last added at; the tries then.</summary>
    TimeSpan _craftTime;
    DateTime _craftTick;
    int _craftTriesSeen;

    /// <summary>Crafts an hour this session, from every craft over the time the crafting window was open (HudCraftValues); null before a minute of it.</summary>
    internal double? CraftsPerHour(SessionEngine? e = null) => CraftsPerHourOf((e ?? CraftShown)?.CraftSnapshot().Sum(r => r.Attempts) ?? 0, _craftTime);

    static double? CraftsPerHourOf(int crafts, TimeSpan time) => crafts > 0 && time.TotalMinutes >= 1 ? crafts / time.TotalHours : null;

    /// <summary>
    /// The recipe the queue is crafting, by name: the newest craft line's, unless that line is the upgrade a combo won (its
    /// name would flip the panel to "Splendent ..." for a moment), then the recipe it upgrades. A line the ledger doesn't know is taken as it is.
    /// </summary>
    static string QueueRecipe(IReadOnlyList<CraftLedger.Recipe> ledger, IReadOnlyList<FeedLine> newestFirst)
    {
        foreach (var l in newestFirst)
        {
            if (l.Name.Length < 2) continue;
            if (ledger.FirstOrDefault(r => string.Equals(r.Name, l.Name, StringComparison.OrdinalIgnoreCase)) is { } own) return own.Name;
            if (ledger.FirstOrDefault(r => string.Equals(RecipeBook.ForOutput(r.Name)?.Upgrade?.Item, l.Name, StringComparison.OrdinalIgnoreCase)) is { } owner) return owner.Name;
        }
        var first = newestFirst.FirstOrDefault()?.Name ?? "";
        return first.Length < 2 ? "" : first;
    }

    /// <summary>The looks' crafting values, from the window's tick (HudFeed): all blank while the player isn't crafting.</summary>
    void HudCraftValues()
    {
        var chat = _watcher.Chat;
        var now = DateTime.UtcNow;
        // on with the crafting window or panel (the activity), and from the first race on even if the activity lags a read:
        // a craft line under CraftLedger.PanelSeconds old turns the looks to crafting on the tick it lands
        var crafts = chat == null || _demo ? new List<FeedLine>() : FeedFor(FeedTab.Crafting);
        bool fresh = crafts.Any(l => l.Kind == "craft" && (now - l.At).TotalSeconds is >= -5 and <= CraftLedger.PanelSeconds);
        bool on = !_demo && chat != null && (chat.Doing.Current.Kind == ActivityKind.Crafting || fresh);
        // the time crafting: each tick while the window is open adds its stretch (at most 2 s, so a stalled tick adds nothing odd)
        if (on && _craftTick != default) _craftTime += TimeSpan.FromSeconds(Math.Clamp((now - _craftTick).TotalSeconds, 0, 2));
        _craftTick = on ? now : default;
        if (!on) { foreach (var k in HudCraftBlank().Keys) _hudValues[k] = ""; return; }
        var e = CraftShown;
        int tries = e?.CraftSnapshot().Sum(r => r.Attempts) ?? 0;
        // crafting started over (the part's Start over): the clock starts over with it
        if (tries < _craftTriesSeen) _craftTime = TimeSpan.Zero;
        _craftTriesSeen = tries;
        foreach (var (k, v) in HudCraftValuesFor(e, crafts, _craftTime, chat?.CraftQueue)) _hudValues[k] = v;
    }

    static Dictionary<string, string> HudCraftBlank()
    {
        var d = CraftHeadKeys.ToDictionary(k => k, _ => "");
        for (int i = 0; i < HudCrafts; i++) foreach (var k in CraftLineKeys) d["craft" + i + k] = "";
        return d;
    }

    /// <summary>The craft ledger's sums, as FillCraft makes them: tries, worked, spent at Market prices, what came out after the cut.</summary>
    internal sealed record CraftSums(int Tries, int Ok, long Spent, long Worth, bool AtMost)
    {
        public int Fail => Tries - Ok;
        public long Net => Worth - Spent;
        public bool Any => Spent > 0 || Worth > 0;
    }

    /// <summary>
    /// The same sums as the Crafting part's (FillCraft): each recipe's cost per try (RecipeBook.CostPerTry) times its tries,
    /// what it made at the lowest Market price after the Market's cut; a material with no price, or a recipe the book
    /// doesn't have, makes the profit "at most". A recipe whose output has no price adds its cost and nothing for its output.
    /// </summary>
    internal static CraftSums SumCrafts(SessionEngine? e)
    {
        long spent = 0, worth = 0; int noBook = 0, tries = 0, ok = 0;
        var missing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in e?.CraftSnapshot() ?? new())
        {
            tries += r.Attempts; ok += r.Ok;
            long? cost = RecipeBook.CostOfTries(r.Name, r.Tries, out _, out _, out _, missing);
            long made = 0; bool madeKnown = r.MadeUnknown == 0;
            foreach (var (item, n) in r.Made)
            {
                if (PriceBook.Price(item) is long p) made += (long)Math.Round(p * n * PriceBook.SellerKeeps); else madeKnown = false;
            }
            if (cost != null) spent += cost.Value; else noBook++;
            if (madeKnown) worth += made;
        }
        return new CraftSums(tries, ok, spent, worth, missing.Count + noBook > 0);
    }

    /// <summary>
    /// The crafting values for a ledger and its newest craft lines: on, the time crafting ("18:05"), the newest craft's recipe
    /// (name, item id, icon), tries, worked, failed, the session's profit ("+1.12M", "≤ +1.12M" with a price missing), what
    /// one of the recipe sells for after the Market's cut ("176K"), and the last crafts newest first with what each made or cost.
    /// </summary>
    internal Dictionary<string, string> HudCraftValuesFor(SessionEngine? e, IReadOnlyList<FeedLine> lines, TimeSpan time, TrackerSession.CraftQueueView? queue = null)
    {
        var d = HudCraftBlank();
        // the queue under way, as its count and what was counted say ("3 of 10 · 2 worked · 1 unsure"), and its final
        // figures for CraftLedger.PanelSeconds after it ends (Steven, 2026-11-04: a miss should show while it happens)
        if (queue is { } q && (q.Open || q.EndedAt is DateTime end && (DateTime.UtcNow - end).TotalSeconds <= CraftLedger.PanelSeconds))
        {
            d["craftQueue"] = "1";
            d["craftQueueText"] = q.Text;
            d["craftQueueDone"] = q.Done.ToString();
            d["craftQueueOf"] = q.Of > 0 ? q.Of.ToString() : "";
        }
        var s = SumCrafts(e);
        // the tiles are crafts; a combo roll is the line after the craft it credits, never a craft of its own. A won roll
        // is still a line of its own here, "combo ×2" or "combo: Splendent ...", so the panel says what the craft before
        // it turned into (Steven, 2026-11-04: a Cypri combo made a second dish and the panel only said "worked"); a lost
        // roll is in the Combos count and nowhere else
        var crafts = lines.Where(l => l.Kind == "craft" && (!l.Combo || l.Ok)).OrderByDescending(l => l.At).Take(HudCrafts).ToList();
        var ledger = e?.CraftSnapshot() ?? new();
        var recipe = QueueRecipe(ledger, crafts);
        long recipeId = recipe.Length > 0 ? _items.IdOf(recipe) : 0;
        d["craft"] = "1";
        d["craftTime"] = time.TotalHours >= 1 ? $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}" : $"{time.Minutes:00}:{time.Seconds:00}";
        d["craftRecipe"] = recipe;
        d["craftRecipeId"] = recipeId != 0 ? recipeId.ToString() : "";
        d["craftIcon"] = recipeId != 0 ? _items.IconUrl(recipeId) : "";
        d["craftTries"] = s.Tries.ToString("N0");
        d["craftOk"] = s.Ok.ToString("N0");
        d["craftFail"] = s.Fail.ToString("N0");
        d["craftProfit"] = s.Any ? (s.AtMost ? "≤ " : "") + Signed(s.Net) : "";
        d["craftSells"] = recipe.Length > 0 && PriceBook.Price(recipe) is long p ? Fk(Math.Round(p * PriceBook.SellerKeeps)) : "";
        // what a craft of this recipe is worth, from the same math as the recipe card (CraftMath.Judge): profit a craft, an hour, combos won of rolled
        var mine = ledger.FirstOrDefault(r => string.Equals(r.Name, recipe, StringComparison.OrdinalIgnoreCase));
        int makes = mine?.Tries.Where(kv => kv.Key > 0).OrderByDescending(kv => kv.Value).Select(kv => kv.Key).FirstOrDefault() ?? 0;
        if (recipe.Length > 0 && RecipeBook.For(recipe, makes) is { } book
            && RecipeBook.CostPerTry(recipe, makes, out int noPrice, out int noVendor, out _) is long cost && noPrice + noVendor == 0)
        {
            var (rate, kind) = CraftMath.SuccessUsed(book.SuccessRate, mine?.Attempts ?? 0, mine?.Ok ?? 0);
            if (CraftMath.Judge(book, PriceBook.Price, cost, rate, PriceBook.SellerKeeps) is { } v)
            {
                var upTo = kind == CraftMath.RateKind.Unknown ? "≤ " : "";
                d["craftPer"] = upTo + Signed(v.Profit);
                if (mine is { Attempts: >= 3 } && CraftsPerHourOf(ledger.Sum(r => r.Attempts), time) is double cph) d["craftPerHr"] = upTo + Signed(v.Profit * cph);
            }
        }
        if (mine is { } m && m.Upgraded + m.NotUpgraded > 0)
        {
            d["craftCombos"] = $"{m.Upgraded}/{m.Upgraded + m.NotUpgraded}";
            // what a won roll makes, for the line's label: "Combos ×2 1/3", "Upgrades 1/3", or just "Combos" when the book can't say
            d["craftCombosKind"] = CraftLedger.ComboOf(m.Name) switch { RecipeBook.ComboMakes.Double => "×2", RecipeBook.ComboMakes.Upgrade => "upgrade", _ => "" };
        }
        for (int i = 0; i < crafts.Count; i++)
        {
            var l = crafts[i];
            var c = "craft" + i;
            // a won roll's line carries the recipe, not the upgrade the blue panel named, so its words and worth are the recipe's
            if (l.Combo && QueueRecipe(ledger, new[] { l }) is { Length: >= 2 } rn && rn != l.Name) l = l with { Name = rn };
            long id = l.Name.Length >= 2 ? _items.IdOf(l.Name) : 0;
            d[c] = l.Name.Length >= 2 ? l.Name : "A craft";
            d[c + "Id"] = $"{l.At.Ticks}:{l.Name}:{l.Ok}{(l.Combo ? ":combo" : "")}";
            d[c + "Item"] = id != 0 ? id.ToString() : "";
            d[c + "Ok"] = l.Ok ? "1" : "";
            d[c + "Worth"] = CraftValue(l).Text;
            d[c + "Icon"] = id != 0 ? _items.IconUrl(id) : "";
            d[c + "At"] = l.At.Ticks.ToString();
            d[c + "Combo"] = l.Combo ? ComboWon(l.Name) : "";
        }
        return d;
    }

    /// <summary>
    /// A sample crafting session that adds up, for the looks' renders and the self-test (never the player's): twelve
    /// Orichalcum Greaves, nine worked and three failed, the newest worked and the one before it failed, at the Crafting
    /// render's prices (ShowDemo("Crafting")), 18:05 at the crafting window. Its ledger is also what the window's Crafting
    /// part is filled from, so the panel and the window can be compared.
    /// </summary>
    internal (SessionEngine Engine, List<FeedLine> Lines, TimeSpan Time) HudCraftSample()
    {
        const string recipe = "Orichalcum Greaves";
        var e = new SessionEngine(_items);
        var lines = new List<FeedLine>();
        var t0 = DateTime.UtcNow.AddMinutes(-18);
        // oldest first: the three fails fall at the 4th, 8th and 11th tries
        bool[] outcome = { true, true, true, false, true, true, true, false, true, true, false, true };
        for (int i = 0; i < outcome.Length; i++)
        {
            var at = t0.AddSeconds(90 * i);
            e.CraftPanelSeen(recipe, at);
            e.Apply(new CraftResult(at, recipe, outcome[i]) { Via = SessionEngine.CraftPanelVia, Makes = RecipeBook.ForOutput(recipe)?.OutputQty ?? 1 });
            lines.Add(new FeedLine(at, FeedTab.Crafting, "craft", recipe, 1, Ok: outcome[i], Via: SessionEngine.CraftPanelVia, Where: "Crafting", Makes: RecipeBook.ForOutput(recipe)?.OutputQty ?? 0));
            // the 10th craft's combo roll, won, as a feed line only (the ledger's sums stay the plain twelve the checks add up)
            if (i == 9) lines.Add(new FeedLine(at.AddSeconds(8), FeedTab.Crafting, "craft", recipe, 1, Ok: true, Via: SessionEngine.CraftPanelVia, Where: "Crafting", Combo: true));
        }
        return (e, lines, new TimeSpan(0, 18, 5));
    }

    /// <summary>The sample session's queue under way, for the renders and the self-test: the 10th of 12, nine worked, one combo, one unsure.</summary>
    internal static TrackerSession.CraftQueueView HudCraftSampleQueue() =>
        new(true, "Orichalcum Greaves", 12, 10, 9, 8, 1, 1, DateTime.UtcNow.AddMinutes(-18), null);

    /// <summary>The Card and the Ticker while crafting, from the sample session, for layout renders (the Crafting render, with its prices).</summary>
    internal List<(string File, string Look, Dictionary<string, string> Values)> HudCraftLookStates()
    {
        var (e, lines, time) = HudCraftSample();
        var v = HudSample();
        foreach (var (k, x) in HudCraftValuesFor(e, lines, time, HudCraftSampleQueue())) v[k] = x;
        return new() { ("ui-hud-card-crafting.png", HudLooks.Card, v), ("ui-hud-ticker-crafting.png", HudLooks.Ticker, v) };
    }

    /// <summary>For the self-test: the window's K/M figures ("176K") and the feed's signed ones ("+137K", "−39K").</summary>
    internal static string FkForTest(double v) => Fk(v);
    internal static string FeedKForTest(double v) => FeedK(v);

    /// <summary>For the self-test: the sample session's values, and the profit the window's Crafting part shows for the same ledger.</summary>
    internal (Dictionary<string, string> Values, CraftSums Sums, string WindowProfit) HudCraftCheck()
    {
        var (e, lines, time) = HudCraftSample();
        var v = HudCraftValuesFor(e, lines, time, HudCraftSampleQueue());
        FillCraft(e);
        return (v, SumCrafts(e), _craftNet);
    }
}
