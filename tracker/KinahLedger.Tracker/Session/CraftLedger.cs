using KinahLedger.Data;

namespace KinahLedger.Session;

/// <summary>
/// Crafting, kept apart from farming: what was crafted, how often it worked, and what came out, all from the crafting
/// panel's Success and Fail bars. Nothing here is a drop, a kill, farming time or a zone's numbers.
/// Not thread-safe on its own: <see cref="SessionEngine"/> calls it under its lock.
/// </summary>
public sealed class CraftLedger
{
    /// <summary>One recipe: tries that worked and failed, and every item that came out, by name.</summary>
    public sealed class Recipe
    {
        public string Name = "";
        public int Ok, Fail;
        /// <summary>Combo rolls after a craft, as the crafting panel shows them: upgraded, and not.</summary>
        public int Upgraded, NotUpgraded;
        public Dictionary<string, int> Made = new(StringComparer.OrdinalIgnoreCase);
        /// <summary>Successes whose output count isn't known: the recipe has variants (×1 and ×10) and the panel didn't say which.</summary>
        public int MadeUnknown;
        public int Attempts => Ok + Fail;
        public Recipe Copy() => new() { Name = Name, Ok = Ok, Fail = Fail, Upgraded = Upgraded, NotUpgraded = NotUpgraded, MadeUnknown = MadeUnknown, Made = new Dictionary<string, int>(Made, StringComparer.OrdinalIgnoreCase) };
    }

    // ---- the crafting panel: the one way in (Steven, 2026-10-23: "if u dont see the crafting bars why the fuck are u
    // adding it to a craft window") ----
    // While crafts run, the panel at the bottom of the screen races a Success bar against a Fail bar; each race is one
    // craft, and a race on the blue combo panel is the upgrade roll for the craft before it. A recipe here exists only
    // because a race finished; what it made is the recipe's yield per success. Nothing the loot list or the chat shows
    // is ever a craft, a try or an output (0.8.22 counted 31 Orichalcum Ingots from 2 crafts, and a Cloak never made).

    /// <summary>How long after the panel was last seen a pickup of its recipe's item is still the crafts' output (a queue's last item lands after the panel closes).</summary>
    public const double PanelSeconds = 15;
    string _panelRecipe = ""; DateTime _panelAt = DateTime.MinValue;

    /// <summary>The panel is on screen, crafting this recipe (the item it makes, "" when its text wasn't read).</summary>
    public void PanelSeen(string recipe, DateTime t)
    {
        if (recipe.Length >= 2) _panelRecipe = recipe;
        if (t > _panelAt) _panelAt = t;
    }

    public bool PanelActive(DateTime t) => _panelAt != DateTime.MinValue && (t - _panelAt).TotalSeconds <= PanelSeconds;

    /// <summary>One race on the panel: a craft that worked or failed, or the combo roll after one. The only way into crafting.</summary>
    public void OnPanel(CraftResult cr)
    {
        PanelSeen(cr.Item, cr.Time);
        var name = _panelRecipe.Length >= 2 ? _panelRecipe : "?";
        var r = Get(name);
        if (cr.Combo) { if (cr.Ok) r.Upgraded++; else r.NotUpgraded++; }
        else if (cr.Ok)
        {
            r.Ok++;
            // what it made: the recipe's yield as the panel's level tells it, never assumed (made ? when it can't tell)
            if (name != "?" && cr.Makes is int y && y > 0) r.Made[name] = r.Made.GetValueOrDefault(name) + y;
            else r.MadeUnknown++;
        }
        else r.Fail++;
    }

    /// <summary>
    /// A pickup of the item the panel is crafting, while the panel is up or just gone: the craft's own output, already
    /// counted from the panel's race, so it is neither a drop nor a second craft. Anything else is what it is.
    /// </summary>
    public bool IsPanelOutput(string item, DateTime t) =>
        PanelActive(t) && _panelRecipe.Length >= 2 && item.Length >= 2
        && (item.StartsWith(_panelRecipe, StringComparison.OrdinalIgnoreCase) || _panelRecipe.StartsWith(item, StringComparison.OrdinalIgnoreCase));

    readonly Dictionary<string, Recipe> _recipes = new(StringComparer.OrdinalIgnoreCase);

    Recipe Get(string name) { if (!_recipes.TryGetValue(name, out var r)) _recipes[name] = r = new Recipe { Name = name }; return r; }

    /// <summary>
    /// Whether an item name is the crafted one: it continues the name read ("Artisan's Orichalcum" → "Artisan's
    /// Orichalcum Longsword"), the other way round, or shares two of its words (a better outcome of the same recipe).
    /// </summary>
    public static bool Continues(string crafted, string item)
    {
        if (item.StartsWith(crafted, StringComparison.OrdinalIgnoreCase) || crafted.StartsWith(item, StringComparison.OrdinalIgnoreCase)) return true;
        static HashSet<string> Words(string s) => s.ToLowerInvariant().Split(new[] { ' ', '-' }, StringSplitOptions.RemoveEmptyEntries).Select(w => w.Trim('.', ',', '\'', '’')).Where(w => w.Length >= 3).ToHashSet();
        var a = Words(crafted); var b = Words(item);
        return a.Count >= 2 && a.Intersect(b).Count() >= 2;
    }

    /// <summary>Copies of every recipe, most tries first.</summary>
    public List<Recipe> Snapshot() => _recipes.Values.Select(r => r.Copy()).OrderByDescending(r => r.Attempts).ThenBy(r => r.Name).ToList();
}
