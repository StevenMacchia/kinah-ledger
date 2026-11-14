using System.Text.Json;

namespace KinahLedger.Parser;

/// <summary>
/// Each ability's name by the game's skill id (data/skills.json, from MetaBot's skill pages: 279 skills), for a member's
/// damage by ability (Steven, 2026-11-14: "a lot of them allow you to click on and open or expand you abilities to see how
/// much each is doing on average / total"). A summon's hits come under its owner with the summon's own skill.
/// </summary>
public static class Abilities
{
    static Dictionary<int, (string Name, string Cls)>? _map;

    static Dictionary<int, (string Name, string Cls)> Map
    {
        get
        {
            if (_map != null) return _map;
            var map = new Dictionary<int, (string, string)>();
            try
            {
                using var s = typeof(Abilities).Assembly.GetManifestResourceStream("skills.json");
                if (s != null)
                {
                    using var doc = JsonDocument.Parse(s);
                    foreach (var p in doc.RootElement.GetProperty("skills").EnumerateObject())
                        if (int.TryParse(p.Name, out var id))
                            map[id] = (p.Value.GetProperty("name").GetString() ?? "", p.Value.TryGetProperty("cls", out var c) ? c.GetString() ?? "" : "");
                }
            }
            catch (Exception ex) { CrashLog.Write("abilities", ex); }
            return _map = map;
        }
    }

    /// <summary>"Judgment"; "Damage over time and more" for 0; "Ability 12060000" for one MetaBot doesn't list.</summary>
    public static string Name(int skill) => skill == 0 ? "Damage over time and more" : Map.TryGetValue(skill, out var s) && s.Name.Length > 0 ? s.Name : $"Ability {skill}";

    public static int Count => Map.Count;
}
