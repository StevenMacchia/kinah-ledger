using System.Text.Json;

namespace KinahLedger.Data;

/// <summary>
/// The faction of each piece of gear whose icon the other faction's twin shares ("Forgotten Helm" Elyos, "Shade Helm"
/// Asmodian, one picture): data/gear-factions.json, built into the exe by tools/data/gear-factions.js from the item's own
/// name ("Elyos Chanter's Breastplate") or from where it drops (Elyos maps only, or Asmodian maps only, MetaBot.GG).
/// Checked when it was built (2026-10-25): in all 237 pairs the Elyos item has the lower id, and the 64 times the chat named
/// one of these pieces for Steven's Elyos character on 2026-10-24, it was the Elyos one each time.
/// </summary>
public static class GearFactions
{
    static readonly Dictionary<string, string> ByName = Load();

    static Dictionary<string, string> Load()
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var s = DataFiles.Open("gear-factions.json");
            if (s == null) return d;
            using var doc = JsonDocument.Parse(s);
            foreach (var (prop, faction) in new[] { ("elyos", "Elyos"), ("asmodian", "Asmodian") })
                if (doc.RootElement.TryGetProperty(prop, out var arr) && arr.ValueKind == JsonValueKind.Array)
                    foreach (var n in arr.EnumerateArray())
                        if (n.GetString() is { Length: > 0 } name) d[name] = faction;
        }
        catch (Exception) { /* no list: twins stay unknown gear unless the chat names them */ }
        return d;
    }

    /// <summary>"Elyos", "Asmodian", or null for a name the list doesn't hold.</summary>
    public static string? Of(string name) => ByName.GetValueOrDefault(name.Trim());
    public static int Count => ByName.Count;

    /// <summary>The faction in a server's label ("Siel (Elyos)" is Elyos), or null when it names none.</summary>
    public static string? OfServer(string? server)
    {
        var s = (server ?? "").Trim();
        if (s.EndsWith("(Elyos)", StringComparison.OrdinalIgnoreCase)) return "Elyos";
        if (s.EndsWith("(Asmodian)", StringComparison.OrdinalIgnoreCase)) return "Asmodian";
        return null;
    }

    /// <summary>
    /// Of a slot's candidate names (one icon, one grade), the one of <paramref name="faction"/>: only when every name has a
    /// faction on the list, exactly one is the player's, and the rest are the other faction's. Null otherwise (a name of no
    /// known faction, two of the player's, no faction given): the slot stays as it was.
    /// </summary>
    public static string? Pick(IReadOnlyCollection<string> names, string? faction)
    {
        if (faction == null || names.Count < 2) return null;
        var f = names.Select(n => (Name: n, F: Of(n))).ToList();
        if (f.Any(x => x.F == null)) return null;
        var mine = f.Where(x => x.F == faction).ToList();
        return mine.Count == 1 ? mine[0].Name : null;
    }
}
