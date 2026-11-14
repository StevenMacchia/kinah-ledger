using KinahLedger.Data;

namespace KinahLedger.Session;

/// <summary>
/// What the character's inventory is worth (Packets/BagBook.cs, Steven, 2026-11-13: "bag value"): every item at the lowest
/// Market price the tracker knows (the site's server board, the Market's own reads, a price typed in), a bound item at
/// its tradable twin's price. Sellable is the tradable items alone, after the Market's cut. Items with no price are
/// counted, never guessed. Items not on the item list (soul crystals, costumes, the game's own counters) are left out.
/// </summary>
public static class BagValue
{
    /// <summary>One kind of item held: how many, what they are worth, whether bound, and the change since the first snapshot.</summary>
    public sealed record Line(long Id, string Name, long Count, long? Price, long Value, bool Bound, long Change);

    /// <summary>
    /// The inventory's figures: its worth, the sellable part after the Market's cut, how many tradable kinds have no price,
    /// the lines (most valuable first), and the worth's change since <paramref name="first"/> (null when there is none).
    /// </summary>
    public sealed record Worth(long Total, long Sellable, int Unpriced, int Kinds, List<Line> Lines, long? Change);

    public static Worth Of(IReadOnlyDictionary<long, long> now, IReadOnlyDictionary<long, long>? first, ItemDictionary items, Func<string, long?> price, double keeps)
    {
        var lines = new List<Line>();
        long total = 0, sellable = 0, change = 0; int unpriced = 0;
        var ids = now.Keys.Union(first?.Keys ?? Enumerable.Empty<long>()).ToList();
        foreach (var id in ids)
        {
            if (items.Get(id) is not { } info) continue;
            long n = now.GetValueOrDefault(id), was = first?.GetValueOrDefault(id) ?? n;
            bool bound = !info.Tradable || info.Name.EndsWith("(Bound)", StringComparison.OrdinalIgnoreCase);
            long? p = price(bound ? Unbound(info.Name) : info.Name);
            if (p is long pv) change += (n - was) * pv;
            if (n <= 0) continue;
            long value = p is long v ? v * n : 0;
            total += value;
            if (!bound) { if (p == null) unpriced++; else sellable += (long)Math.Round(value * keeps); }
            lines.Add(new Line(id, info.Name, n, p, value, bound, n - was));
        }
        lines = lines.OrderByDescending(l => l.Value).ThenByDescending(l => l.Count).ToList();
        return new Worth(total, sellable, unpriced, lines.Count, lines, first == null ? null : change);
    }

    static string Unbound(string n) => System.Text.RegularExpressions.Regex.Replace(n, @"\s*\(bound\)\s*$", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();
}
