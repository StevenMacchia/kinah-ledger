namespace KinahLedger.Data;

/// <summary>Unknown: a gathering bar race whose node nothing named (no panel line, result, node on the target bar or recent node); its attempt still counts.</summary>
public enum GatherFamily { Ore, Log, Odyle, Gemstone, Herb, Food, Unknown }

/// <summary>One gatherable resource. Bonus marks what a node gives on top of its base material: gemstones and the higher tiers.</summary>
public sealed record GatherItem(string Name, string Grade, GatherFamily Family, bool Bonus)
{
    /// <summary>A line for a primary resource means one node was gathered; gemstones ride along with ore and don't count as nodes.</summary>
    public bool IsNode => Family != GatherFamily.Gemstone;
    /// <summary>The skill whose nodes this comes from. Gemstones come out of ore nodes.</summary>
    public GatherFamily Skill => Family == GatherFamily.Gemstone ? GatherFamily.Ore : Family;
}

public static class GatherSkills
{
    public static readonly GatherFamily[] Order = { GatherFamily.Ore, GatherFamily.Log, GatherFamily.Odyle, GatherFamily.Herb, GatherFamily.Food, GatherFamily.Unknown };

    public static string Label(GatherFamily f) => f switch
    {
        GatherFamily.Ore => "Mining · ore and gemstones",
        GatherFamily.Log => "Logging · logs",
        GatherFamily.Odyle => "Essence extraction · Odyle",
        GatherFamily.Herb => "Herbs",
        GatherFamily.Food => "Cooking ingredients",
        GatherFamily.Unknown => "Gathering · node not named",
        _ => f.ToString()
    };

    /// <summary>Which skill a failure line belongs to, from the words the game uses ("Essence Extraction failed").</summary>
    public static GatherFamily? FromSkillText(string skill)
    {
        var s = skill.ToLowerInvariant();
        if (s.Contains("essence") || s.Contains("extract") || s.Contains("odyle")) return GatherFamily.Odyle;
        if (s.Contains("min") || s.Contains("ore")) return GatherFamily.Ore;
        if (s.Contains("log") || s.Contains("wood") || s.Contains("lumber")) return GatherFamily.Log;
        if (s.Contains("herb") || s.Contains("alchem") || s.Contains("harvest")) return GatherFamily.Herb;
        if (s.Contains("cook") || s.Contains("food") || s.Contains("collect") || s.Contains("forag")) return GatherFamily.Food;
        return null;
    }
}

/// <summary>
/// The 46 gathering materials in Aion 2 global, from NCSoft's item dictionary (category "Gathering Material", 2026-10-19).
/// Built in so Gathering mode works before any item list is downloaded.
/// </summary>
public static class GatheringCatalog
{
    // declared before Spec: static fields initialize in textual order
    // tier names as the item dictionary spells them: Common, Rare, Legend, Unique, Epic
    static readonly (string, string)[] Metal = { ("", "Common"), ("Fine ", "Rare"), ("Pure ", "Legend"), ("Radiant ", "Unique") };

    static readonly (string[] names, GatherFamily family, (string prefix, string grade)[] tiers)[] Spec =
    {
        (new[] { "Orichalcum Ore" }, GatherFamily.Ore, Metal),
        (new[] { "Asvata Log", "Yggdrasil Log" }, GatherFamily.Log, Metal),
        (new[] { "Odyle" }, GatherFamily.Odyle, Metal),
        (new[] { "Diamond Gemstone", "Ruby Gemstone", "Sapphire Gemstone" }, GatherFamily.Gemstone, Metal),
        (new[] { "Aria", "Azpha", "Calendula", "Targena" }, GatherFamily.Herb, new[] { ("", "Common"), ("Fresh ", "Rare") }),
        (new[] { "Conide", "Coriolus", "Cypri", "Inina", "Kukuru", "Mela", "Morfa", "Ohkra", "Pujery", "Raydam" }, GatherFamily.Food, new[] { ("", "Common") })
    };

    static readonly Dictionary<string, GatherItem> ByName = Build();

    static Dictionary<string, GatherItem> Build()
    {
        var d = new Dictionary<string, GatherItem>();
        foreach (var (names, family, tiers) in Spec)
            foreach (var n in names)
                foreach (var (prefix, grade) in tiers)
                {
                    var name = prefix + n;
                    d[Norm(name)] = new GatherItem(name, grade, family, family == GatherFamily.Gemstone || grade != "Common");
                }
        return d;
    }

    public static IEnumerable<GatherItem> All => ByName.Values;

    public static GatherItem? Find(string name) => ByName.GetValueOrDefault(Norm(name));

    // ---- nodes: what the target bar names while a node is targeted (2026-10-24) ----
    // The bar reads a node's name in pieces ("Orichalcum", "Ore"; "Kukuru", "Vege", "Vegetable", "Ftable"), so a node is
    // matched word by word against the materials' names and the few words nodes go by, with a misread letter allowed and
    // a word cut short at either end; nothing under three letters.
    // The words a node goes by besides its material's name, and what each stands for (material null: the skill only)
    static readonly (string Word, string Node, string? Material, GatherFamily Family)[] NodeWords =
    {
        ("ore", "Orichalcum Ore", "Orichalcum Ore", GatherFamily.Ore),
        ("vein", "Orichalcum Ore", "Orichalcum Ore", GatherFamily.Ore),
        ("gem", "Gemstone", null, GatherFamily.Ore),
        ("gemstone", "Gemstone", null, GatherFamily.Ore),
        ("log", "Log", null, GatherFamily.Log),
        // the vegetable node gives Kukuru (Steven's trace, 2026-10-24 15:59: the bar read "Kukuru", then "Vege…" pieces)
        ("vegetable", "Kukuru", "Kukuru", GatherFamily.Food),
    };

    /// <summary>A node: its name (the material it gives, or its skill's word), the material when known, and the skill.</summary>
    public sealed record GatherNode(string Name, string? Material, GatherFamily Family);

    /// <summary>The node a material comes from, named after its base tier ("Fine Orichalcum Ore" → Orichalcum Ore); gemstones ride along with ore.</summary>
    public static GatherNode? NodeOf(string material)
    {
        if (Find(material) is not { } g) return null;
        var b = BaseName(g.Name);
        // a gemstone comes out of an ore node or a gem node: "Gemstone", in mining
        return g.IsNode ? new GatherNode(b, b, g.Skill) : new GatherNode(GemNode, b, g.Skill);
    }

    /// <summary>The node name gemstones are filed under (they ride along with ore, or come from a gem node).</summary>
    public const string GemNode = "Gemstone";

    /// <summary>A material's name without its tier ("Fresh Aria" → "Aria", "Fine Orichalcum Ore" → "Orichalcum Ore").</summary>
    public static string BaseName(string name) =>
        System.Text.RegularExpressions.Regex.Replace(name.Trim(), @"^(?:fine|pure|radiant|fresh)\s+", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    /// <summary>
    /// The gathering node a piece of target bar text names, or null. <paramref name="otherWord"/> says a word belongs to
    /// something else (an enemy's name): text holding one isn't a node ("Aullaeu Forest Odyle Spider" is an enemy).
    /// </summary>
    public static GatherNode? MatchNode(string text, Func<string, bool>? otherWord = null)
    {
        var words = System.Text.RegularExpressions.Regex.Split(text.ToLowerInvariant(), "[^a-z]+").Where(w => w.Length >= 3).ToList();
        GatherNode? found = null;
        foreach (var w in words)
        {
            if (MatchWord(w) is { } m) { found ??= m; continue; }
            if (w.Length >= 4 && otherWord != null && otherWord(w)) return null;
        }
        return found;
    }

    static readonly List<(string Key, GatherNode Node)> NodeKeys = BuildNodeKeys();

    static List<(string, GatherNode)> BuildNodeKeys()
    {
        var keys = new List<(string, GatherNode)>();
        foreach (var g in ByName.Values.Where(x => x.IsNode).OrderBy(x => x.Name, StringComparer.Ordinal))
        {
            var b = BaseName(g.Name);
            var first = b.Split(' ')[0].ToLowerInvariant();
            if (!keys.Any(k => k.Item1 == first)) keys.Add((first, new GatherNode(b, b, g.Skill)));
        }
        foreach (var (word, node, material, family) in NodeWords) keys.Add((word, new GatherNode(node, material, family)));
        return keys;
    }

    static GatherNode? MatchWord(string w)
    {
        foreach (var (k, n) in NodeKeys) if (w == k) return n;
        foreach (var (k, n) in NodeKeys)
        {
            // a misread letter per six ("Orjchalcum", "Qdyle"), on names of five letters or more
            if (k.Length >= 5 && Math.Abs(k.Length - w.Length) <= 1 && Edit(k, w) <= Math.Max(1, k.Length / 6)) return n;
            // cut short at the end: four letters or more as read ("Vege", "Orich"; one misread from six), or three with
            // one misread and the first letter right ("Veq")
            if (w.Length >= 4 && w.Length < k.Length && Edit(k[..w.Length], w) <= (w.Length >= 6 ? 1 : 0)) return n;
            if (w.Length == 3 && k.Length >= 6 && w[0] == k[0] && Edit(k[..3], w) <= 1) return n;
            // cut short at the start ("Ftable" for Vegetable): five letters or more, one misread
            if (w.Length >= 5 && w.Length < k.Length && Edit(k[^w.Length..], w) <= 1) return n;
        }
        return null;
    }

    static int Edit(string a, string b)
    {
        var prev = new int[b.Length + 1]; var cur = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++) prev[j] = j;
        for (int i = 1; i <= a.Length; i++)
        {
            cur[0] = i;
            for (int j = 1; j <= b.Length; j++) cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            (prev, cur) = (cur, prev);
        }
        return prev[b.Length];
    }

    static string Norm(string s)
    {
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\s*\(bound\)\s*", " ", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return string.Join(' ', s.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
