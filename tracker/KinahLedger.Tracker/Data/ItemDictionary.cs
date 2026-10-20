using System.Text.Json;

namespace KinahLedger.Data;

public sealed record ItemInfo(long Id, string Name, string Grade, bool Tradable, string Icon, string Category);

/// <summary>Item names by ID, loaded from data/tw-items.json (the crawler's output). Empty when the file is absent.</summary>
public sealed class ItemDictionary
{
    readonly Dictionary<long, ItemInfo> _byId = new();
    readonly Dictionary<string, long> _byName = new();

    public int Count => _byId.Count;

    /// <summary>Where the game's item icons live; the crawler's records carry full URLs under it, the gathering list only file names.</summary>
    public const string IconBase = "https://assets.playnccdn.com/static-aion2-gamedata/resources/";

    /// <summary>The icon URL for an item, or "" when none is known (an unlisted item, or a synthetic ID for a misread name).</summary>
    public string IconUrl(long id)
    {
        if (!_byId.TryGetValue(id, out var i) || i.Icon.Length == 0) return "";
        return i.Icon.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? i.Icon : IconBase + i.Icon;
    }
    public ItemInfo? Get(long id) => _byId.GetValueOrDefault(id);
    public string NameOf(long id) => _byId.TryGetValue(id, out var i) ? i.Name : $"item {id}";

    /// <summary>
    /// How an item is named in what's shared: its dictionary ID when it has one, else its name. A synthetic ID only means
    /// something on this PC, so it never leaves it; the site matches names against the same dictionary.
    /// </summary>
    public string KeyOf(long id) => id > 0 ? id.ToString() : NameOf(id);

    /// <summary>
    /// ID for a name read off the screen. Known names map to their real item ID; unknown ones get a
    /// stable negative ID so they still count, and the name is kept so it displays properly.
    /// </summary>
    public long IdOf(string name)
    {
        var key = Norm(name);
        if (_byName.TryGetValue(key, out var id)) return id;

        // a misread of a known name ("Fresh Frult") snaps to the closest real item, so one drop doesn't become two rows
        var close = Closest(key);
        if (close != null) { _byName[key] = close.Value; return close.Value; }

        long synthetic = -(long)(Fnv(key) & 0x7FFFFFFF) - 1;
        _byId.TryAdd(synthetic, new ItemInfo(synthetic, name.Trim(), "", !key.EndsWith("(bound)"), "", ""));
        _byName[key] = synthetic;
        return synthetic;
    }

    /// <summary>True when the name is a real item, exactly or by a close misread: a dictionary entry or a gathering material. Adds nothing.</summary>
    public bool Knows(string name)
    {
        var key = Norm(name);
        if (_byName.TryGetValue(key, out var id)) return id > 0 || (_byId.TryGetValue(id, out var info) && info.Category.Length > 0);
        return Closest(key) is { } close && (close > 0 || (_byId.TryGetValue(close, out var ci) && ci.Category.Length > 0));
    }

    long? Closest(string key)
    {
        if (key.Length < 4) return null;
        int budget = Math.Max(1, key.Length / 6);
        long? best = null; int bestD = budget + 1;
        foreach (var kv in _byName)
        {
            if (Math.Abs(kv.Key.Length - key.Length) > budget || kv.Key[0] != key[0]) continue;
            int d = Edit(key, kv.Key, bestD - 1);
            if (d < bestD) { bestD = d; best = kv.Value; if (d == 0) break; }
        }
        if (bestD <= budget) return best;
        // the first letter of a line fading in is the one most often misread ("mria acquired." for "Aria acquired."):
        // a name that differs from exactly one known name in its first letter only is that name
        long? only = null;
        foreach (var kv in _byName)
        {
            if (kv.Key.Length != key.Length || kv.Key[0] == key[0] || string.CompareOrdinal(kv.Key, 1, key, 1, key.Length - 1) != 0) continue;
            if (only != null && only != kv.Value) return null;
            only = kv.Value;
        }
        return only;
    }

    /// <summary>Levenshtein distance with a cutoff, so the scan over thousands of names stays cheap.</summary>
    static int Edit(string a, string b, int max)
    {
        if (max < 0) return int.MaxValue;
        var prev = new int[b.Length + 1]; var cur = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++) prev[j] = j;
        for (int i = 1; i <= a.Length; i++)
        {
            cur[0] = i; int rowMin = cur[0];
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
                rowMin = Math.Min(rowMin, cur[j]);
            }
            if (rowMin > max) return int.MaxValue;
            (prev, cur) = (cur, prev);
        }
        return prev[b.Length];
    }

    static string Norm(string s) => string.Join(' ', s.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    static uint Fnv(string s) { uint h = 2166136261; foreach (var c in s) { h ^= c; h *= 16777619; } return h; }

    public static ItemDictionary LoadOrEmpty(string path)
    {
        var d = new ItemDictionary();
        var p = Settings.Resolve(path);
        if (File.Exists(p)) d.LoadFile(p);
        else d.LoadEmbedded();
        // the gathering materials are always known, so misreads of them snap to the right name even without the full list
        foreach (var g in GatheringCatalog.All)
            if (!d._byName.ContainsKey(Norm(g.Name)))
            {
                long id = -(long)(Fnv("gather:" + Norm(g.Name)) & 0x7FFFFFFF) - 1;
                d._byId[id] = new ItemInfo(id, g.Name, g.Grade, true, "", "Gathering Material");
                d._byName[Norm(g.Name)] = id;
            }
        d.LoadGatheringIcons(p);
        return d;
    }

    /// <summary>Icon file names for the gathering materials: data/gathering-items.json beside the item list when present, else the copy built into the exe.</summary>
    void LoadGatheringIcons(string itemsPath)
    {
        try
        {
            var beside = Path.Combine(Path.GetDirectoryName(itemsPath) ?? "", "gathering-items.json");
            using var stream = File.Exists(beside) ? File.OpenRead(beside) : typeof(ItemDictionary).Assembly.GetManifestResourceStream("gathering-items.json");
            if (stream == null) return;
            using var doc = JsonDocument.Parse(stream);
            if (!doc.RootElement.TryGetProperty("items", out var arr)) return;
            foreach (var e in arr.EnumerateArray())
            {
                var name = Str(e, "name"); var icon = Str(e, "icon");
                if (name.Length == 0 || icon.Length == 0 || !_byName.TryGetValue(Norm(name), out var id)) continue;
                if (_byId.TryGetValue(id, out var info) && info.Icon.Length == 0) _byId[id] = info with { Icon = icon };
            }
        }
        catch (Exception) { /* icons are decoration: a bad file costs nothing */ }
    }

    void LoadFile(string p) { using var doc = JsonDocument.Parse(File.ReadAllText(p)); LoadJson(doc); }

    /// <summary>The item list built into the exe (data/tw-items.json at build time): the whitelist when no list sits beside the exe.</summary>
    void LoadEmbedded()
    {
        try
        {
            using var stream = typeof(ItemDictionary).Assembly.GetManifestResourceStream("tw-items.json");
            if (stream == null) return;
            using var doc = JsonDocument.Parse(stream);
            LoadJson(doc);
        }
        catch (Exception) { /* without a list nothing counts as a drop; the window's status says how many items are known */ }
    }

    void LoadJson(JsonDocument doc)
    {
        var d = this;
        if (!doc.RootElement.TryGetProperty("items", out var arr)) return;
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
    }

    static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : "";
}
