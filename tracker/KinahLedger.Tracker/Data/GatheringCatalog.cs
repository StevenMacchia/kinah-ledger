namespace KinahLedger.Data;

public enum GatherFamily { Ore, Log, Odyle, Gemstone, Herb, Food }

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
    public static readonly GatherFamily[] Order = { GatherFamily.Ore, GatherFamily.Log, GatherFamily.Odyle, GatherFamily.Herb, GatherFamily.Food };

    public static string Label(GatherFamily f) => f switch
    {
        GatherFamily.Ore => "Mining · ore and gemstones",
        GatherFamily.Log => "Logging · logs",
        GatherFamily.Odyle => "Essence extraction · Odyle",
        GatherFamily.Herb => "Herbs",
        GatherFamily.Food => "Cooking ingredients",
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

    static string Norm(string s)
    {
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\s*\(bound\)\s*", " ", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return string.Join(' ', s.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
