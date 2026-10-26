using System.Text.Json;

namespace KinahLedger.Data;

/// <summary>
/// Crafting recipes: what each makes, from which materials, for how much Kinah. data/recipes.json, built into the exe
/// when present. The chat says what was crafted and whether it worked, never what it used; the cost of a try comes
/// from here, priced at the lowest Market price of each material.
/// </summary>
public static class RecipeBook
{
    public sealed record Material(string Item, int Qty);
    public sealed record Great(string Item, int Qty, double? Chance);
    public sealed record Recipe(string Name, string Skill, string Output, int OutputQty, List<Material> Materials, long? Kinah, double? SuccessRate, List<Great> GreatOutcomes)
    {
        /// <summary>'E' (Elyos) or 'A' (Asmodian): the two factions craft the same items from different materials.</summary>
        public char Faction { get; init; }
        /// <summary>The chance a combo (great success) that rolled then succeeds; with the combo chance, how often a success turns into the upgrade.</summary>
        public double? ComboSuccess { get; init; }
        public int Mastery { get; init; }
    }

    /// <summary>The player's faction ('E' or 'A', from the chosen server), so a recipe's materials are the ones they craft with.</summary>
    public static char PreferFaction { get; set; }

    public static readonly IReadOnlyList<Recipe> All = Load();
    public static string Credit { get; private set; } = "";
    static readonly Dictionary<string, List<Recipe>> ByOutput = All.GroupBy(r => r.Output, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

    static List<Recipe> Load()
    {
        var list = new List<Recipe>();
        try
        {
            using var s = KinahLedger.Data.DataFiles.Open("recipes.json");
            if (s == null) return list;
            using var doc = JsonDocument.Parse(s);
            var root = doc.RootElement;
            if (root.TryGetProperty("credit", out var c) && c.ValueKind == JsonValueKind.String) Credit = c.GetString() ?? "";
            if (!root.TryGetProperty("recipes", out var arr) || arr.ValueKind != JsonValueKind.Array) return list;
            foreach (var r in arr.EnumerateArray())
            {
                string Str(string k) => r.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
                int Int(JsonElement e, string k, int def) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : def;
                double? Dbl(JsonElement e, string k) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;
                var output = Str("output");
                if (output.Length < 2) continue;
                var mats = new List<Material>();
                if (r.TryGetProperty("materials", out var m) && m.ValueKind == JsonValueKind.Array)
                    foreach (var x in m.EnumerateArray())
                        if (x.TryGetProperty("item", out var it) && it.ValueKind == JsonValueKind.String) mats.Add(new Material(it.GetString()!, Math.Max(1, Int(x, "qty", 1))));
                var great = new List<Great>();
                if (r.TryGetProperty("great", out var g) && g.ValueKind == JsonValueKind.Array)
                    foreach (var x in g.EnumerateArray())
                        if (x.TryGetProperty("item", out var it) && it.ValueKind == JsonValueKind.String) great.Add(new Great(it.GetString()!, Math.Max(1, Int(x, "qty", 1)), Dbl(x, "chance")));
                long? kinah = r.TryGetProperty("kinah", out var k) && k.ValueKind == JsonValueKind.Number && k.TryGetInt64(out var kv) ? kv : null;
                // recipe_id: the second digit is the faction (221044001 Asmodian, 211044001 Elyos)
                var rid = r.TryGetProperty("recipe_id", out var ri) && ri.ValueKind == JsonValueKind.Number ? ri.GetRawText() : "";
                char faction = rid.Length > 1 ? (rid[1] == '1' ? 'E' : rid[1] == '2' ? 'A' : '\0') : '\0';
                list.Add(new Recipe(Str("name").Length > 0 ? Str("name") : output, Str("skill"), output, Math.Max(1, Int(r, "output_qty", 1)), mats, kinah, Dbl(r, "success_rate"), great)
                    { Faction = faction, ComboSuccess = Dbl(r, "combo_success_rate"), Mastery = Int(r, "mastery", 0) });
            }
        }
        catch (Exception) { /* no recipes: costs show as unknown */ }
        return list;
    }

    /// <summary>The recipe that makes this item, or null when the book doesn't have it.</summary>
    public static Recipe? ForOutput(string item) => For(item, 0);

    /// <summary>
    /// How many one success makes, told by the recipe's mastery level as the crafting panel's title shows it ("Orichalcum
    /// Ingot (Novice Lv. 25)" is the ×10 recipe, Lv. 1 the ×1: Steven, 2026-10-23, "theres a 1 craft one and a 10 craft
    /// recipe"). With no level read, an item whose recipes all make the same count is that count. Null when the book
    /// doesn't know the item or its recipes make different counts and the level doesn't say which: never assumed.
    /// </summary>
    public static int? YieldFor(string item, int level)
    {
        if (!ByOutput.TryGetValue(item.Trim(), out var all) || all.Count == 0) return null;
        var at = level > 0 ? all.Where(r => r.Mastery == level).ToList() : new List<Recipe>();
        var pool = at.Count > 0 ? at : all;
        var yields = pool.Select(r => r.OutputQty).Distinct().ToList();
        return yields.Count == 1 ? yields[0] : null;
    }

    /// <summary>
    /// The recipe for an item in the player's faction; with <paramref name="makes"/> above 0, the one that makes that many
    /// (Sapphire Powder: ×3 at mastery 20, ×30 at 45). Falls back to any recipe for the item.
    /// </summary>
    public static Recipe? For(string item, int makes)
    {
        if (!ByOutput.TryGetValue(item.Trim(), out var all) || all.Count == 0) return null;
        IEnumerable<Recipe> mine = all.Where(r => PreferFaction == '\0' || r.Faction == '\0' || r.Faction == PreferFaction);
        if (!mine.Any()) mine = all;
        if (makes > 0 && mine.FirstOrDefault(r => r.OutputQty == makes) is { } m) return m;
        return mine.First();
    }

    /// <summary>The one recipe whose output starts with a name the chat cut short ("Artisan's Orichalcum"), or null when none or several do.</summary>
    public static string? Complete(string cut)
    {
        var c = cut.Trim();
        if (c.Length < 3) return null;
        if (ByOutput.TryGetValue(c, out var exact)) return exact[0].Output;
        string? found = null;
        foreach (var k in ByOutput.Keys)
        {
            if (!k.StartsWith(c, StringComparison.OrdinalIgnoreCase)) continue;
            if (found != null) return null;
            found = k;
        }
        return found;
    }

    /// <summary>
    /// The recipe outputs a name the crafting panel cut short could be (CraftPanel.Settle): the one it names whole, else
    /// every output that starts with it, in order. None for fewer than three letters.
    /// </summary>
    public static List<string> StartingWith(string cut)
    {
        var c = cut.Trim();
        if (c.Length < 3) return new List<string>();
        if (ByOutput.TryGetValue(c, out var exact)) return new List<string> { exact[0].Output };
        return ByOutput.Keys.Where(k => k.StartsWith(c, StringComparison.OrdinalIgnoreCase)).OrderBy(k => k, StringComparer.Ordinal).ToList();
    }

    /// <summary>The mastery levels the item's recipes are at (the level the crafting panel shows), none when the book doesn't know it.</summary>
    public static List<int> MasteriesOf(string item) =>
        ByOutput.TryGetValue(item.Trim(), out var all) ? all.Select(r => r.Mastery).Where(m => m > 0).Distinct().OrderBy(m => m).ToList() : new List<int>();

    /// <summary>
    /// What one try costs at the lowest Market prices: the materials plus the Kinah fee. Null when the book doesn't
    /// have the recipe; <paramref name="unpriced"/> counts materials with no price yet, left out, so the cost is then the
    /// least a try costs. Bound materials ("Salt (Bound)") are bought from a vendor and can't be on the Market: they take
    /// the NPC price (typed, else shipped), and those with none are counted in <paramref name="vendor"/> and left out.
    /// Materials the player ticked "I have these" for cost nothing in cash and are counted in <paramref name="farmed"/>.
    /// </summary>
    public static long? CostPerTry(string recipeOutput, out int unpriced) => CostPerTry(recipeOutput, out unpriced, out _, out _);

    public static long? CostPerTry(string recipeOutput, out int unpriced, out int vendor) => CostPerTry(recipeOutput, out unpriced, out vendor, out _);

    public static long? CostPerTry(string recipeOutput, out int unpriced, out int vendor, out int farmed, ICollection<string>? missing = null)
    {
        unpriced = 0; vendor = 0; farmed = 0;
        var r = ForOutput(recipeOutput);
        if (r == null) return null;
        long cost = r.Kinah ?? 0;
        foreach (var m in r.Materials)
        {
            if (PriceBook.Farmed(m.Item)) farmed++;
            else if (m.Item.Contains("(Bound)", StringComparison.OrdinalIgnoreCase)) { if (PriceBook.VendorPrice(m.Item) is long vp) cost += vp * m.Qty; else { vendor++; missing?.Add(m.Item); } }
            else if (PriceBook.Price(m.Item) is long p) cost += p * m.Qty;
            else { unpriced++; missing?.Add(m.Item); }
        }
        return cost;
    }
}
