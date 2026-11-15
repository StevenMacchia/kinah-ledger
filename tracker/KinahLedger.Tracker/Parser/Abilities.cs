using System.Text.Json;

namespace KinahLedger.Parser;

/// <summary>
/// Each ability's name by the game's skill id (data/skills.json, filled by tools/skill-fill.js: MetaBot's 279 class skills
/// plus the chain skills, the spirits' own attacks and the Theostones from the global client's English skill table, each
/// entry with its source), for a member's damage by ability (Steven, 2026-11-14: "a lot of them allow you to click on and
/// open or expand you abilities to see how much each is doing on average / total"). A summon's hits come under its owner
/// with the summon's own skill.
/// Steven, 2026-11-14: "wtf is 'Ability 16050000'… you need to get them fucking right": the view never shows a number. An
/// id the list doesn't name is "&lt;Class&gt; skill" when the class is known (from the id's family or the member), else
/// "Unknown skill", and it is noted once in reads.log ("ability unnamed: …") so the next fill finds it.
/// </summary>
public static class Abilities
{
    static readonly object Gate = new();
    static Dictionary<int, (string Name, string Cls)>? _map;
    static Dictionary<int, int>? _canon;
    static readonly HashSet<int> Noted = new();

    static Dictionary<int, (string Name, string Cls)> Map
    {
        get
        {
            lock (Gate)
            {
                if (_map != null) return _map;
                var map = new Dictionary<int, (string Name, string Cls)>();
                try
                {
                    using var s = typeof(Abilities).Assembly.GetManifestResourceStream("skills.json");
                    if (s != null)
                    {
                        using var doc = JsonDocument.Parse(s);
                        foreach (var p in doc.RootElement.GetProperty("skills").EnumerateObject())
                            if (int.TryParse(p.Name, out var id) && p.Value.TryGetProperty("name", out var n) && n.GetString() is { Length: > 0 } name)
                                map[id] = (name, p.Value.TryGetProperty("cls", out var c) ? c.GetString() ?? "" : "");
                    }
                }
                catch (Exception ex) { CrashLog.Write("abilities", ex); }
                // one row per ability: the ids the game gives one skill under one name (a spirit's attack at each of its
                // ranks: 100014 and 100018 are both Fire Spirit: Basic Attack) share the first of them
                var canon = new Dictionary<int, int>();
                foreach (var g in map.GroupBy(kv => (kv.Value.Name, kv.Value.Cls)))
                {
                    int first = g.Min(kv => kv.Key);
                    foreach (var kv in g) canon[kv.Key] = first;
                }
                _canon = canon;
                return _map = map;
            }
        }
    }

    /// <summary>
    /// The id the list names this skill under: the id itself, else, for a class skill (11xxxxxx to 19xxxxxx), its base without
    /// the last four digits (a rank or variant: 12060003 is Punishing Strike's); null when the list names neither.
    /// </summary>
    public static int? Resolve(int skill)
    {
        if (skill < 0) return BaseOf(skill) is > 0 and var b ? Resolve(b) : null;   // damage over time: its skill's
        if (skill == 0) return null;
        var map = Map;
        if (map.ContainsKey(skill)) return skill;
        if (skill is >= 10_000_000 and < 20_000_000 && skill % 10_000 != 0 && map.ContainsKey(skill - skill % 10_000)) return skill - skill % 10_000;
        return null;
    }

    /// <summary>The one id every id of the same ability shares (by name and class), for one row per ability; an unnamed id is its own.</summary>
    public static int Canon(int skill)
    {
        // damage over time: one row per skill, whichever of its effects ticked (-(the skill's own id x 100))
        if (skill < 0) return BaseOf(skill) is > 0 and var b && (long)Canon(b) * 100 is var dc and <= int.MaxValue ? (int)-dc : skill;
        if (Resolve(skill) is not { } id) return skill;
        _ = Map;
        return _canon!.TryGetValue(id, out var c) ? c : id;
    }

    /// <summary>
    /// Damage over time comes as a negative skill: minus its effect's id, the skill's id x 100 plus the effect's number
    /// (-1206000003 is Punishing Strike's, 12060000). The skill's id; a skill as it is.
    /// </summary>
    public static int BaseOf(int skill) => skill < 0 ? (int)(-(long)skill / 100) : skill;

    /// <summary>A class by its skills' family, the id's first two digits (11 Gladiator ... 18 Chanter, 19 Brawler); "" for anything else (damage over time: its skill's).</summary>
    public static string ClassOfFamily(int skill) => skill < 0 ? ClassOfFamily(BaseOf(skill)) : skill is >= 10_000_000 and < 20_000_000 ? (skill / 1_000_000) switch
    {
        11 => "Gladiator", 12 => "Templar", 13 => "Assassin", 14 => "Ranger", 15 => "Sorcerer", 16 => "Spiritmaster",
        17 => "Cleric", 18 => "Chanter", 19 => "Brawler", _ => "",
    } : "";

    /// <summary>
    /// "Judgment"; "Damage over time" for 0 (records from before damage over time came by skill); for an id the
    /// list doesn't name, "Spiritmaster skill" (the class from the id's family, else <paramref name="casterClass"/>, the
    /// member's) or "Unknown skill", noted once in reads.log. Damage over time (a negative skill): its skill's name with
    /// " (over time)", "Punishing Strike (over time)", "Templar skill (over time)", "Unknown skill (over time)".
    /// </summary>
    public static string Name(int skill, string? casterClass = null)
    {
        if (skill < 0) return (BaseOf(skill) is > 0 and var b ? Name(b, casterClass) : Title(casterClass) is { Length: > 0 } c ? c + " skill" : "Unknown skill") + " (over time)";
        if (skill == 0) return "Damage over time";
        if (Resolve(skill) is { } id) return Map[id].Name;
        var cls = ClassOfFamily(skill);
        if (cls.Length == 0) cls = Title(casterClass);
        bool note;
        lock (Gate) note = Noted.Add(skill);
        if (note) CrashLog.Note($"ability unnamed: {skill} ({(cls.Length > 0 ? cls : "no class")}); tools/skill-fill.js lists it");
        return cls.Length > 0 ? cls + " skill" : "Unknown skill";
    }

    /// <summary>The ability's class ("templar"), from the list or the id's family; "" when neither says.</summary>
    public static string ClassOf(int skill) => skill < 0 ? ClassOf(BaseOf(skill)) : Resolve(skill) is { } id && Map[id].Cls.Length > 0 ? Map[id].Cls : ClassOfFamily(skill).ToLowerInvariant();

    /// <summary>A member's abilities with the ids of one ability as one row (<see cref="Canon"/>), the biggest first.</summary>
    public static List<AbilityShare> Merged(IEnumerable<AbilityShare> abilities) => abilities
        .GroupBy(a => Canon(a.Skill))
        .Select(g => new AbilityShare { Skill = g.Key, Damage = g.Sum(a => a.Damage), Hits = g.Sum(a => a.Hits) })
        .OrderByDescending(a => a.Damage).ToList();

    static string Title(string? cls)
    {
        var k = (cls ?? "").Replace(" ", "").Replace("-", "").Trim().ToLowerInvariant();
        return k switch
        {
            "gladiator" or "templar" or "assassin" or "ranger" or "sorcerer" or "spiritmaster" or "cleric" or "chanter" or "brawler" => char.ToUpperInvariant(k[0]) + k[1..],
            "elementalist" => "Spiritmaster",
            _ => "",
        };
    }

    /// <summary>How many skill ids the list names.</summary>
    public static int Count => Map.Count;
}
