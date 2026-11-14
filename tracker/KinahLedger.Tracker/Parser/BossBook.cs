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

    /// <summary>"Shattered Arkanis [Stage 3]" is Shattered Arkanis on the list.</summary>
    public static string Strip(string instance)
    {
        var t = instance.Trim();
        int i = t.IndexOf(" [", StringComparison.Ordinal);
        return i > 0 ? t[..i].Trim() : t;
    }
}
