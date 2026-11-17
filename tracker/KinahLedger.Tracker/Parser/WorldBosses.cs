using KinahLedger.Session;

namespace KinahLedger.Parser;

/// <summary>
/// The world bosses the Parser keeps pulls of wherever they are fought (Steven, 2026-11-17: "add boss names / bars etc for
/// nightmare dungeons and world bosses etc and add them to the parser logs"). The names are the timers': the field bosses the
/// game's own boss list named (Session/BossTimers.cs, as fresh as the last time the world map was open, kept three days) and
/// the Abyss's scheduled bosses (Session/AbyssBosses.cs). A pull is a world boss's when its name is one of them, in full or as
/// the timers shorten it ("Kashapa" is Soul Ruler Kashapa); the record then carries the timers' full name and the boss's
/// place as its instance. A boss the timers don't know stays the live-only meter's, as before.
/// </summary>
public sealed class WorldBossBook
{
    /// <summary>A world boss: its full name and where it is (the place the timers give it).</summary>
    public sealed record Boss(string Name, string Zone);

    readonly Func<IEnumerable<(string Name, string Place)>>? _timers;
    readonly bool _abyss;

    /// <summary>The bosses of <paramref name="timers"/> (read at each pull, so a list read since counts), with the Abyss's when <paramref name="abyss"/>.</summary>
    public WorldBossBook(Func<IEnumerable<(string Name, string Place)>>? timers, bool abyss = true) { _timers = timers; _abyss = abyss; }

    /// <summary>No world boss known: every boss outside a dungeon is the live-only meter's (tests, renders).</summary>
    public static WorldBossBook None { get; } = new(null, abyss: false);

    /// <summary>A fixed list (tests).</summary>
    public static WorldBossBook Of(params (string Name, string Place)[] bosses) => new(() => bosses, abyss: false);

    /// <summary>Every boss known now, one per name.</summary>
    public List<Boss> All()
    {
        var all = new List<Boss>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string name, string place)
        {
            var n = Clean(name);
            if (n.Length > 0 && seen.Add(n)) all.Add(new Boss(n, Clean(place)));
        }
        try { if (_timers != null) foreach (var (name, place) in _timers()) Add(name, place); }
        catch (Exception ex) { CrashLog.Note("parser: the world boss list wasn't read: " + ex.Message); }
        if (_abyss) foreach (var b in AbyssBosses.Schedule) Add(b.Name, b.Place);
        return all;
    }

    /// <summary>
    /// The world boss a pull's name is: the same name (case and spacing aside), the timers' short name for it, or a name with a
    /// known full name in it; null when none is (or a short name two bosses would share).
    /// </summary>
    public Boss? Find(string? name)
    {
        var n = Clean(name);
        if (n.Length == 0) return null;
        var all = All();
        if (all.Count == 0) return null;
        if (all.FirstOrDefault(b => b.Name.Equals(n, StringComparison.OrdinalIgnoreCase)) is { } full) return full;
        var shorts = TimerBoard.ShortNames(all.Select(b => b.Name));
        var byShort = all.Where(b => shorts.TryGetValue(b.Name, out var s) && s.Equals(n, StringComparison.OrdinalIgnoreCase)).ToList();
        if (byShort.Count == 1) return byShort[0];
        var inside = all.Where(b => b.Name.Length >= 4 && Words(n).Contains(" " + b.Name.ToLowerInvariant() + " ")).ToList();
        return inside.Count == 1 ? inside[0] : null;
    }

    /// <summary>The only world boss the timers place in <paramref name="zone"/> (a pull the game didn't name there), else null.</summary>
    public Boss? OnlyIn(string? zone)
    {
        var z = Clean(zone);
        if (z.Length == 0) return null;
        var here = All().Where(b => b.Zone.Equals(z, StringComparison.OrdinalIgnoreCase)).ToList();
        return here.Count == 1 ? here[0] : null;
    }

    static string Clean(string? s) => string.Join(' ', (s ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    static string Words(string s) => " " + s.ToLowerInvariant() + " ";
}
