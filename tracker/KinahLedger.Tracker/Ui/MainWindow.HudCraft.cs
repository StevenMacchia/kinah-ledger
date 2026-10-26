using KinahLedger.Data;
using KinahLedger.Session;

namespace KinahLedger.Ui;

/// <summary>
/// The panel's crafting line (the mock's feature 2, approved 2026-10-26): while the crafting window or panel is open (the
/// tracker's activity is Crafting), the Card and the Ticker show crafting instead of farming. Every figure comes from the
/// craft ledger, summed as the window's Crafting part sums it (FillCraft): the tries, how many worked and failed, the
/// profit after the Market's cut, what the recipe sells for each, and the last crafts with what each made or cost (the
/// feed's CraftValue). The Strip keeps its own crafting line (HudFeedValues).
/// </summary>
public partial class MainWindow
{
    /// <summary>How many crafts the looks get ("craft0" newest to "craft3"): the Card shows three as tiles, the Ticker four toasts.</summary>
    const int HudCrafts = 4;
    static readonly string[] CraftHeadKeys = { "craft", "craftTime", "craftRecipe", "craftRecipeId", "craftIcon", "craftTries", "craftOk", "craftFail", "craftProfit", "craftSells" };
    static readonly string[] CraftLineKeys = { "", "Id", "Item", "Ok", "Worth", "Icon", "At" };

    /// <summary>Time with the crafting window open this session, added up tick by tick; the tick it was last added at; the tries then.</summary>
    TimeSpan _craftTime;
    DateTime _craftTick;
    int _craftTriesSeen;

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
        foreach (var (k, v) in HudCraftValuesFor(e, crafts, _craftTime)) _hudValues[k] = v;
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
            long? cost = RecipeBook.CostPerTry(r.Name, out _, out _, out _, missing);
            long made = 0; bool madeKnown = r.MadeUnknown == 0;
            foreach (var (item, n) in r.Made)
            {
                if (PriceBook.Price(item) is long p) made += (long)Math.Round(p * n * PriceBook.SellerKeeps); else madeKnown = false;
            }
            if (cost != null) spent += cost.Value * r.Attempts; else noBook++;
            if (madeKnown) worth += made;
        }
        return new CraftSums(tries, ok, spent, worth, missing.Count + noBook > 0);
    }

    /// <summary>
    /// The crafting values for a ledger and its newest craft lines: on, the time crafting ("18:05"), the newest craft's recipe
    /// (name, item id, icon), tries, worked, failed, the session's profit ("+1.12M", "≤ +1.12M" with a price missing), what
    /// one of the recipe sells for after the Market's cut ("176K"), and the last crafts newest first with what each made or cost.
    /// </summary>
    internal Dictionary<string, string> HudCraftValuesFor(SessionEngine? e, IReadOnlyList<FeedLine> lines, TimeSpan time)
    {
        var d = HudCraftBlank();
        var s = SumCrafts(e);
        var crafts = lines.Where(l => l.Kind == "craft").OrderByDescending(l => l.At).Take(HudCrafts).ToList();
        var recipe = crafts.FirstOrDefault()?.Name ?? "";
        if (recipe.Length < 2) recipe = "";
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
        for (int i = 0; i < crafts.Count; i++)
        {
            var l = crafts[i];
            var c = "craft" + i;
            long id = l.Name.Length >= 2 ? _items.IdOf(l.Name) : 0;
            d[c] = l.Name.Length >= 2 ? l.Name : "A craft";
            d[c + "Id"] = $"{l.At.Ticks}:{l.Name}:{l.Ok}";
            d[c + "Item"] = id != 0 ? id.ToString() : "";
            d[c + "Ok"] = l.Ok ? "1" : "";
            d[c + "Worth"] = CraftValue(l).Text;
            d[c + "Icon"] = id != 0 ? _items.IconUrl(id) : "";
            d[c + "At"] = l.At.Ticks.ToString();
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
            lines.Add(new FeedLine(at, FeedTab.Crafting, "craft", recipe, 1, Ok: outcome[i], Via: SessionEngine.CraftPanelVia, Where: "Crafting"));
        }
        return (e, lines, new TimeSpan(0, 18, 5));
    }

    /// <summary>The Card and the Ticker while crafting, from the sample session, for layout renders (the Crafting render, with its prices).</summary>
    internal List<(string File, string Look, Dictionary<string, string> Values)> HudCraftLookStates()
    {
        var (e, lines, time) = HudCraftSample();
        var v = HudSample();
        foreach (var (k, x) in HudCraftValuesFor(e, lines, time)) v[k] = x;
        return new() { ("ui-hud-card-crafting.png", HudLooks.Card, v), ("ui-hud-ticker-crafting.png", HudLooks.Ticker, v) };
    }

    /// <summary>For the self-test: the window's K/M figures ("176K") and the feed's signed ones ("+137K", "−39K").</summary>
    internal static string FkForTest(double v) => Fk(v);
    internal static string FeedKForTest(double v) => FeedK(v);

    /// <summary>For the self-test: the sample session's values, and the profit the window's Crafting part shows for the same ledger.</summary>
    internal (Dictionary<string, string> Values, CraftSums Sums, string WindowProfit) HudCraftCheck()
    {
        var (e, lines, time) = HudCraftSample();
        var v = HudCraftValuesFor(e, lines, time);
        FillCraft(e);
        return (v, SumCrafts(e), _craftNet);
    }
}
