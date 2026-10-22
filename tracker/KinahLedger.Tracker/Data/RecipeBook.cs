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
    public sealed record Recipe(string Name, string Skill, string Output, int OutputQty, List<Material> Materials, long? Kinah, double? SuccessRate, List<Great> GreatOutcomes);

    public static readonly IReadOnlyList<Recipe> All = Load();
    public static string Credit { get; private set; } = "";
    static readonly Dictionary<string, Recipe> ByOutput = All.GroupBy(r => r.Output, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

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
                list.Add(new Recipe(Str("name").Length > 0 ? Str("name") : output, Str("skill"), output, Math.Max(1, Int(r, "output_qty", 1)), mats, kinah, Dbl(r, "success_rate"), great));
            }
        }
        catch (Exception) { /* no recipes: costs show as unknown */ }
        return list;
    }

    /// <summary>The recipe that makes this item, or null when the book doesn't have it.</summary>
    public static Recipe? ForOutput(string item) => ByOutput.GetValueOrDefault(item.Trim());

    /// <summary>The one recipe whose output starts with a name the chat cut short ("Artisan's Orichalcum"), or null when none or several do.</summary>
    public static string? Complete(string cut)
    {
        var c = cut.Trim();
        if (c.Length < 3) return null;
        if (ByOutput.ContainsKey(c)) return ByOutput[c].Output;
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
    /// What one try costs at the lowest Market prices: the materials plus the Kinah fee. Null when the book doesn't
    /// have the recipe; <paramref name="unpriced"/> counts materials with no price yet. Bound materials ("Salt (Bound)")
    /// are bought from a vendor and can't be on the Market: they are counted in <paramref name="vendor"/> and left out.
    /// </summary>
    public static long? CostPerTry(string recipeOutput, out int unpriced) => CostPerTry(recipeOutput, out unpriced, out _);

    public static long? CostPerTry(string recipeOutput, out int unpriced, out int vendor)
    {
        unpriced = 0; vendor = 0;
        var r = ForOutput(recipeOutput);
        if (r == null) return null;
        long cost = r.Kinah ?? 0;
        foreach (var m in r.Materials)
        {
            if (m.Item.Contains("(Bound)", StringComparison.OrdinalIgnoreCase)) vendor++;
            else if (PriceBook.Price(m.Item) is long p) cost += p * m.Qty;
            else unpriced++;
        }
        return cost;
    }
}
