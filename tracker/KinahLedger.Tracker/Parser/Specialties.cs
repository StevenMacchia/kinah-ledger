using System.Text.Json;

namespace KinahLedger.Parser;

/// <summary>One specialization of a skill: its number (1 to 5), what it does, the skill level it needs (0: not said), its icon (MetaBot's, "" when none).</summary>
public sealed record Specialty(int N, string Text, int Level, string Icon);

/// <summary>
/// What each skill's specializations do (Steven, 2026-11-16: the Parser's "Specialties 4·5" tags "clickable in the app so u
/// can see which specialties are grabbed - use the names of the specialties if they're available"). The game names them only
/// "Specialization 1" to "5" and the hits carry only their numbers (<see cref="Abilities.PicksOf"/>), so their effects come
/// from MetaBot's skill pages: data/specialties.json, filled by tools/specialty-fill.js (200 skills; passives and stances
/// have none). Keyed by the skill id the list names (<see cref="Abilities.Resolve"/>).
/// </summary>
public static class Specialties
{
    static readonly object Gate = new();
    static Dictionary<int, List<Specialty>>? _map;

    /// <summary>A skill's specializations, 1 to 5 in order; empty when the data has none for it.</summary>
    public static IReadOnlyList<Specialty> Of(int skill)
    {
        var map = Map;
        int s = skill >= 100_000_000 ? Abilities.SkillOfEffect(skill) : skill < 0 ? Abilities.BaseOf(skill) : skill;
        if (map.TryGetValue(s, out var l)) return l;
        if (Abilities.Resolve(s) is { } id && map.TryGetValue(id, out var r)) return r;
        if (s is >= 10_000_000 and < 20_000_000 && map.TryGetValue(s - s % 10_000, out var b)) return b;
        return Array.Empty<Specialty>();
    }

    /// <summary>How many skills have specializations (for the tests).</summary>
    public static int Count => Map.Count;

    static Dictionary<int, List<Specialty>> Map
    {
        get
        {
            lock (Gate)
            {
                if (_map != null) return _map;
                var map = new Dictionary<int, List<Specialty>>();
                try
                {
                    using var st = typeof(Specialties).Assembly.GetManifestResourceStream("specialties.json");
                    if (st != null)
                    {
                        using var doc = JsonDocument.Parse(st);
                        foreach (var p in doc.RootElement.GetProperty("skills").EnumerateObject())
                        {
                            if (!int.TryParse(p.Name, out var id) || !p.Value.TryGetProperty("specs", out var specs)) continue;
                            var list = new List<Specialty>();
                            foreach (var x in specs.EnumerateArray())
                            {
                                int n = x.TryGetProperty("n", out var nn) ? nn.GetInt32() : 0;
                                // the five a player picks from (a page's combination rows, n 6 and up, are no specialization of their own)
                                if (n is < 1 or > 5) continue;
                                string text = x.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
                                int level = x.TryGetProperty("level", out var lv) && lv.ValueKind == JsonValueKind.Number ? lv.GetInt32() : 0;
                                string icon = x.TryGetProperty("icon", out var ic) ? ic.GetString() ?? "" : "";
                                if (text.Length > 0) list.Add(new Specialty(n, text, level, icon));
                            }
                            if (list.Count > 0) map[id] = list.OrderBy(z => z.N).ToList();
                        }
                    }
                }
                catch (Exception ex) { CrashLog.Write("specialties", ex); }
                return _map = map;
            }
        }
    }
}
