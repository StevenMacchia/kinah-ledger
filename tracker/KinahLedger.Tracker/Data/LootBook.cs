using System.Text.Json;

namespace KinahLedger.Data;

/// <summary>
/// Each enemy's loot table: what it can drop and the listed chance per kill. data/loot-tables.json, from MetaBot.GG's
/// monster pages, built into the exe when present. It turns "drops per kill" into "drops per kill of an enemy that can
/// drop it" (Raw Leather never comes from a rock Kalgolem, so Kalgolem kills don't dilute its odds), and it keeps the
/// best guess of Kinah by enemy honest: a drop never goes to an enemy whose table doesn't have it.
/// </summary>
public sealed class LootBook
{
    public const string CreditUrl = "https://metabot.gg/en/aion-2/monsters";

    public sealed record Entry(string Item, long? Id, double? Chance);

    readonly Dictionary<string, List<Entry>> _tables = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, List<string>> _sourcesByItem = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The tables built into the exe; empty when the file wasn't there at build time.</summary>
    public static LootBook Current { get; set; } = LoadEmbedded();

    public int Count => _tables.Count;

    static LootBook LoadEmbedded()
    {
        try
        {
            using var s = typeof(LootBook).Assembly.GetManifestResourceStream("loot-tables.json");
            if (s == null) return new LootBook();
            using var r = new StreamReader(s);
            return FromJson(r.ReadToEnd());
        }
        catch (Exception) { return new LootBook(); }
    }

    static string Norm(string s) => string.Join(' ', s.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));

    /// <summary>Reads {"tables": {"enemy": {"drops": [{"item", "id", "chance"}]}}}.</summary>
    public static LootBook FromJson(string json)
    {
        var b = new LootBook();
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("tables", out var tables) || tables.ValueKind != JsonValueKind.Object) return b;
        foreach (var t in tables.EnumerateObject())
        {
            if (!t.Value.TryGetProperty("drops", out var drops) || drops.ValueKind != JsonValueKind.Array) continue;
            var list = new List<Entry>();
            foreach (var d in drops.EnumerateArray())
            {
                var item = d.TryGetProperty("item", out var i) && i.ValueKind == JsonValueKind.String ? i.GetString() ?? "" : "";
                if (item.Length < 2) continue;
                long? id = d.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.Number && idEl.TryGetInt64(out var v) && v > 0 ? v : null;
                double? chance = d.TryGetProperty("chance", out var c) && c.ValueKind == JsonValueKind.Number ? Math.Clamp(c.GetDouble(), 0, 1) : null;
                list.Add(new Entry(item.Trim(), id, chance));
                var key = Norm(item);
                if (!b._sourcesByItem.TryGetValue(key, out var src)) b._sourcesByItem[key] = src = new List<string>();
                if (!src.Contains(t.Name, StringComparer.OrdinalIgnoreCase)) src.Add(t.Name);
            }
            // an empty table says nothing (a page that lists no drops, or one not read yet): unknown, never "drops nothing"
            if (list.Count > 0) b._tables[t.Name] = list;
        }
        return b;
    }

    /// <summary>True when the book has this enemy's table (so "not on its table" means something).</summary>
    public bool Knows(string enemy) => _tables.ContainsKey(enemy);

    public IReadOnlyList<Entry> TableOf(string enemy) => _tables.TryGetValue(enemy, out var t) ? t : Array.Empty<Entry>();

    /// <summary>The listed entry for an item on an enemy's table, matched by item id when both have one, else by name; null when it isn't there.</summary>
    public Entry? Find(string enemy, string itemName, long itemId = 0)
    {
        if (!_tables.TryGetValue(enemy, out var t)) return null;
        var key = Norm(itemName);
        foreach (var e in t) if (itemId > 0 && e.Id == itemId) return e;
        foreach (var e in t) if (Norm(e.Item) == key) return e;
        return null;
    }

    /// <summary>Whether an enemy can drop the item. Null when the book has no table for it (unknown, so not a no).</summary>
    public bool? CanDrop(string enemy, string itemName, long itemId = 0) => Knows(enemy) ? Find(enemy, itemName, itemId) != null : null;

    /// <summary>Every enemy whose table lists the item.</summary>
    public IReadOnlyList<string> SourcesOf(string itemName) => _sourcesByItem.TryGetValue(Norm(itemName), out var s) ? s : Array.Empty<string>();
}
