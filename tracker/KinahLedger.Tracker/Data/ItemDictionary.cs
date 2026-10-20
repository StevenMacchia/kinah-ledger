using System.Text.Json;

namespace KinahLedger.Data;

public sealed record ItemInfo(long Id, string Name, string Grade, bool Tradable, string Icon, string Category);

/// <summary>Item names by ID, loaded from data/tw-items.json (the crawler's output). Empty when the file is absent.</summary>
public sealed class ItemDictionary
{
    readonly Dictionary<long, ItemInfo> _byId = new();
    readonly Dictionary<string, long> _byName = new();

    public int Count => _byId.Count;
    public ItemInfo? Get(long id) => _byId.GetValueOrDefault(id);
    public string NameOf(long id) => _byId.TryGetValue(id, out var i) ? i.Name : $"item {id}";

    /// <summary>
    /// ID for a name read off the screen. Known names map to their real item ID; unknown ones get a
    /// stable negative ID so they still count, and the name is kept so it displays properly.
    /// </summary>
    public long IdOf(string name)
    {
        var key = Norm(name);
        if (_byName.TryGetValue(key, out var id)) return id;
        long synthetic = -(long)(Fnv(key) & 0x7FFFFFFF) - 1;
        _byId.TryAdd(synthetic, new ItemInfo(synthetic, name.Trim(), "", !key.EndsWith("(bound)"), "", ""));
        _byName[key] = synthetic;
        return synthetic;
    }

    static string Norm(string s) => string.Join(' ', s.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    static uint Fnv(string s) { uint h = 2166136261; foreach (var c in s) { h ^= c; h *= 16777619; } return h; }

    public static ItemDictionary LoadOrEmpty(string path)
    {
        var d = new ItemDictionary();
        var p = Settings.Resolve(path);
        if (!File.Exists(p)) return d;
        using var doc = JsonDocument.Parse(File.ReadAllText(p));
        if (!doc.RootElement.TryGetProperty("items", out var arr)) return d;
        foreach (var e in arr.EnumerateArray())
        {
            if (!e.TryGetProperty("id", out var idEl) || !idEl.TryGetInt64(out var id)) continue;
            var info = new ItemInfo(
                id,
                Str(e, "name"),
                Str(e, "grade"),
                e.TryGetProperty("tradable", out var t) && t.ValueKind == JsonValueKind.True,
                Str(e, "icon"),
                Str(e, "categoryName"));
            d._byId[id] = info;
            // first ID wins per name, so a later duplicate doesn't flip the mapping
            d._byName.TryAdd(Norm(info.Name), id);
        }
        return d;
    }

    static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : "";
}

/// <summary>zones.json: zone or instance IDs mapped to activity slugs the site understands.</summary>
public sealed class ZoneMap
{
    readonly Dictionary<long, string> _z = new();

    public static ZoneMap Load(string path)
    {
        var m = new ZoneMap();
        var p = Settings.Resolve(path);
        if (!File.Exists(p)) return m;
        using var doc = JsonDocument.Parse(File.ReadAllText(p), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        if (!doc.RootElement.TryGetProperty("zones", out var z)) return m;
        foreach (var kv in z.EnumerateObject())
            if (long.TryParse(kv.Name, out var id) && kv.Value.ValueKind == JsonValueKind.String) m._z[id] = kv.Value.GetString()!;
        return m;
    }

    public string Slug(long? id) => id == null ? "unknown" : _z.TryGetValue(id.Value, out var s) ? s : $"zone_{id}";
    public string Name(long? id) => id == null ? "No zone yet" : _z.TryGetValue(id.Value, out var s) ? s.Replace('_', ' ') : $"zone {id}";
}
