using KinahLedger.Packets;

namespace KinahLedger.Parser;

/// <summary>
/// The bosses of each instance the parser keeps fights for (data/bosses.txt, "Instance|Kind|Boss|final"): the whitelist
/// that keeps trash, elites and named adds out of the Parser page. An instance on the list keeps fights with its listed
/// bosses only; an instance not on the list (new content) keeps fights with what the game itself marks a boss, so a new
/// dungeon works before the list knows it.
/// </summary>
public sealed class BossBook
{
    /// <summary>A boss of an instance, in route order; Wave: a wave stage (a split in Full run, never a boss pull).</summary>
    public sealed record Boss(string Instance, ContentKind Kind, string Name, bool Final, bool Wave = false);

    readonly Dictionary<string, List<Boss>> _byInstance = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, ContentKind> _kindOf = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The list built into the exe (or its kept, newer copy).</summary>
    public static BossBook Shared => _shared ??= Load();
    static BossBook? _shared;

    static BossBook Load()
    {
        try
        {
            using var s = Data.DataFiles.Open("bosses.txt");
            if (s == null) return new BossBook();
            using var r = new StreamReader(s);
            return Parse(r.ReadToEnd());
        }
        catch (Exception ex) { CrashLog.Write("boss list", ex); return new BossBook(); }
    }

    public static BossBook Parse(string text)
    {
        var b = new BossBook();
        foreach (var raw in text.Split('\n'))
        {
            var t = raw.Trim();
            if (t.Length == 0 || t.StartsWith('#')) continue;
            var p = t.Split('|');
            if (p.Length < 2) continue;
            var inst = p[0].Trim();
            var kind = Encounter.KindOf(p[1]);
            if (inst.Length == 0 || kind == ContentKind.Other) continue;
            b._kindOf.TryAdd(inst, kind);
            // "Instance|Kind||Code": the game's own code name for the instance (its map events', CODE_Mode_..._MapEvent)
            if ((p.Length < 3 || p[2].Trim().Length == 0) && p.Length >= 4 && p[3].Trim() is { Length: > 0 } code) b._byCode.TryAdd(code, inst);
            if (p.Length < 3 || p[2].Trim().Length == 0) continue;   // an instance with its kind and no bosses yet
            var list = b._byInstance.TryGetValue(inst, out var l) ? l : b._byInstance[inst] = new();
            var mark = p.Length >= 4 ? p[3].Trim() : "";
            list.Add(new Boss(inst, kind, p[2].Trim(), mark.Equals("final", StringComparison.OrdinalIgnoreCase), mark.Equals("wave", StringComparison.OrdinalIgnoreCase)));
        }
        return b;
    }

    /// <summary>The kind the list files an instance under, or Other when it isn't on the list.</summary>
    public ContentKind KindOf(string instance) => _kindOf.GetValueOrDefault(Strip(instance), ContentKind.Other);

    /// <summary>True when the list names this instance's bosses.</summary>
    public bool Knows(string instance) => _byInstance.ContainsKey(Strip(instance));

    public IReadOnlyList<Boss> BossesOf(string instance) => _byInstance.TryGetValue(Strip(instance), out var l) ? l : Array.Empty<Boss>();

    /// <summary>The listed boss of this name in this instance, or null.</summary>
    public Boss? Find(string instance, string name)
    {
        var n = name.Trim();
        return BossesOf(instance).FirstOrDefault(b => b.Name.Equals(n, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Every boss name on the list, any instance (a boss met in an instance whose name wasn't heard).</summary>
    public bool AnyNamed(string name) => _byInstance.Values.Any(l => l.Any(b => b.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase)));

    public int Count => _byInstance.Values.Sum(l => l.Count);

    readonly Dictionary<string, string> _byCode = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The words the game's map events put after an instance's code for its mode (FierceHornCave_Easy_Mapevent).</summary>
    static readonly HashSet<string> ModeWords = new(StringComparer.OrdinalIgnoreCase) { "Easy", "Normal", "Hard", "Hell" };

    /// <summary>
    /// True for a word that is a mode of an instance (Easy, Normal, Hard, Hell, and the game's names Conquest and Exploration),
    /// never an instance's name: a place read as one is no place.
    /// </summary>
    public static bool IsModeWord(string word) => ModeWords.Contains(word.Trim()) || word.Trim().Equals("Conquest", StringComparison.OrdinalIgnoreCase) || word.Trim().Equals("Exploration", StringComparison.OrdinalIgnoreCase);

    /// <summary>The mode word of a map event, as the game names it: Normal is Conquest (Steven, 2026-11-14); another as it is.</summary>
    public static string ModeName(string word) => word.Equals("Normal", StringComparison.OrdinalIgnoreCase) ? "Conquest" : word;

    /// <summary>
    /// The instance a map event's code is of, by the list's codes (the longest that starts it: "Fire_Temple" has an underscore
    /// of its own), and its mode when the next word is one (FierceHornCave_Easy_BossRoom_Mapevent: Ferocious Horn Den, Easy).
    /// The mode is never the name. Null for a code of no listed instance (an open-world event).
    /// </summary>
    public (string Instance, string Mode)? FromCode(string code)
    {
        var c = code.Trim();
        string? stem = null;
        foreach (var k in _byCode.Keys)
            if (c.Length > k.Length && c[k.Length] == '_' && c.StartsWith(k, StringComparison.OrdinalIgnoreCase) && (stem == null || k.Length > stem.Length)) stem = k;
        if (stem == null) return null;
        var next = c[(stem.Length + 1)..].Split('_')[0];
        return (_byCode[stem], ModeWords.Contains(next) ? ModeName(next) : "");
    }

    /// <summary>"Shattered Arkanis [Stage 3]" is Shattered Arkanis on the list.</summary>
    public static string Strip(string instance)
    {
        var t = instance.Trim();
        int i = t.IndexOf(" [", StringComparison.Ordinal);
        return i > 0 ? t[..i].Trim() : t;
    }
}
