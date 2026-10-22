using KinahLedger.Data;

namespace KinahLedger.Session;

/// <summary>
/// Crafting, kept apart from farming: what was crafted, how often it worked, and what came out. Nothing here is a
/// drop, a kill, farming time or a zone's numbers. The chat prints "Successfully crafted X" and then "X acquired.";
/// that item line is the craft's output. A long name wraps in the chat ("Successfully crafted Artisan's Orichalcum" /
/// "Longsword."), so the output is matched by how its name continues the crafted name, not by an exact match.
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
        public int Attempts => Ok + Fail;
        public Recipe Copy() => new() { Name = Name, Ok = Ok, Fail = Fail, Upgraded = Upgraded, NotUpgraded = NotUpgraded, Made = new Dictionary<string, int>(Made, StringComparer.OrdinalIgnoreCase) };
    }

    // ---- the crafting panel (Steven, 2026-10-22: crafts are read from what the game shows for them, never the chat) ----
    // While crafts run, the panel at the bottom of the screen races a Success bar against a Fail bar; each race is one
    // craft, and a race on the blue combo panel is the upgrade roll for the craft before it. The panel is the count: while
    // it was seen in the last PanelSeconds, every item the loot list shows is what the crafts made, under the panel's recipe.

    /// <summary>How long after the panel was last seen the loot list's rows are still the crafts' output (a queue's last item lands after the panel closes).</summary>
    public const double PanelSeconds = 15;
    string _panelRecipe = ""; DateTime _panelAt = DateTime.MinValue;

    /// <summary>The panel is on screen, crafting this recipe (the item it makes, "" when its text wasn't read).</summary>
    public void PanelSeen(string recipe, DateTime t)
    {
        if (recipe.Length >= 2) _panelRecipe = recipe;
        if (t > _panelAt) _panelAt = t;
    }

    public bool PanelActive(DateTime t) => _panelAt != DateTime.MinValue && (t - _panelAt).TotalSeconds <= PanelSeconds;

    /// <summary>One race on the panel: a craft that worked or failed, or the combo roll after one.</summary>
    public void OnPanel(CraftResult cr)
    {
        PanelSeen(cr.Item, cr.Time);
        var r = Get(_panelRecipe.Length >= 2 ? _panelRecipe : "?");
        if (cr.Combo) { if (cr.Ok) r.Upgraded++; else r.NotUpgraded++; }
        else if (cr.Ok) r.Ok++;
        else r.Fail++;
        _last = r.Name; _lastAt = cr.Time;
    }

    /// <summary>
    /// An item picked up while the panel is up or just gone: what the crafts made, recorded under the panel's recipe
    /// (the try itself was counted from the panel). Returns false when the panel hasn't been seen lately.
    /// </summary>
    public bool TakePanelOutput(string item, int count, DateTime t)
    {
        if (!PanelActive(t)) return false;
        var r = Get(_panelRecipe.Length >= 2 ? _panelRecipe : "?");
        r.Made[item] = r.Made.GetValueOrDefault(item) + count;
        return true;
    }

    /// <summary>How long after "Successfully crafted" its item line can come.</summary>
    public const double OutputSeconds = 10;

    readonly Dictionary<string, Recipe> _recipes = new(StringComparer.OrdinalIgnoreCase);
    string? _pending; DateTime _pendingAt;        // a craft that worked and whose item line hasn't come yet
    string _last = ""; DateTime _lastAt;          // the last recipe, for "Crafting failed." lines that don't name it

    Recipe Get(string name) { if (!_recipes.TryGetValue(name, out var r)) _recipes[name] = r = new Recipe { Name = name }; return r; }

    /// <summary>The full recipe name for a name the chat may have cut short: a recipe already seen, else the recipe book, else as read.</summary>
    string Resolve(string read)
    {
        if (read.Length < 2) return "?";
        if (_recipes.ContainsKey(read)) return _recipes[read].Name;
        var seen = _recipes.Keys.Where(k => k.StartsWith(read, StringComparison.OrdinalIgnoreCase)).ToList();
        if (seen.Count == 1) return seen[0];
        return RecipeBook.Complete(read) ?? read;
    }

    public void OnResult(CraftResult cr)
    {
        Settle(cr.Time);
        var name = cr.Item.Length >= 2 ? cr.Item : (cr.Time - _lastAt).TotalMinutes <= 2 ? _last : "";
        if (cr.Ok) { _pending = name.Length >= 2 ? name : "?"; _pendingAt = cr.Time; }
        else Get(Resolve(name)).Fail++;
        if (name.Length >= 2) { _last = Resolve(name); _lastAt = cr.Time; }
    }

    /// <summary>True when this item line is the output of the craft just made; it is then recorded here and nowhere else.</summary>
    public bool TakeOutput(string item, int count, DateTime t)
    {
        if (_pending == null) return false;
        if ((t - _pendingAt).TotalSeconds > OutputSeconds) { Settle(t); return false; }
        if (_pending != "?" && !Continues(_pending, item)) return false;
        // the recipe is the item when the item line finishes the crafted name; a better outcome (a great success) keeps the recipe read
        var recipe = _pending == "?" || item.StartsWith(_pending, StringComparison.OrdinalIgnoreCase) ? item : Resolve(_pending);
        var r = Get(recipe);
        r.Ok++;
        r.Made[item] = r.Made.GetValueOrDefault(item) + count;
        _last = recipe; _lastAt = t; _pending = null;
        return true;
    }

    /// <summary>
    /// An item a recipe makes, picked up while the crafting window was open, with no "Successfully crafted" line read
    /// (the window hides most of the chat): it is a craft that worked. Returns false when no recipe makes the item.
    /// </summary>
    public bool TakeUnannounced(string item, int count)
    {
        if (RecipeBook.ForOutput(item) == null) return false;
        var r = Get(item);
        r.Ok++;
        r.Made[item] = r.Made.GetValueOrDefault(item) + count;
        _last = item; _lastAt = DateTime.UtcNow;
        return true;
    }

    /// <summary>A craft whose item line never came in time still worked: it counts under the name read.</summary>
    void Settle(DateTime t)
    {
        if (_pending == null || (t - _pendingAt).TotalSeconds <= OutputSeconds) return;
        Get(Resolve(_pending)).Ok++;
        _pending = null;
    }

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

    /// <summary>Copies of every recipe, a craft still waiting for its item line counted as a success, most tries first.</summary>
    public List<Recipe> Snapshot()
    {
        var copy = _recipes.Values.Select(r => r.Copy()).ToDictionary(r => r.Name, StringComparer.OrdinalIgnoreCase);
        if (_pending != null)
        {
            var n = Resolve(_pending);
            if (!copy.TryGetValue(n, out var r)) copy[n] = r = new Recipe { Name = n };
            r.Ok++;
        }
        return copy.Values.OrderByDescending(r => r.Attempts).ThenBy(r => r.Name).ToList();
    }
}
