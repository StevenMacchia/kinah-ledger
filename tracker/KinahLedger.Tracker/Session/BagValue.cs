using KinahLedger.Data;

namespace KinahLedger.Session;

/// <summary>
/// What the character's bag (or a session's drops) is worth (Packets/BagBook.cs, Steven, 2026-11-13: "bag value"): the
/// tradable items at the lowest Market price the tracker knows (the site's server board, the Market's own reads, a price
/// typed in), and what that sells for after the Market's cut. Bound or unbound decides it, every time (Steven, 2026-11-14:
/// "that matters in every instance ever in this game"): a bound item can't be sold, so it has no value here, as everywhere
/// in the tracker; it is counted apart. The game's own messages say which: a bound item has its own item id ("Life Potion
/// (Bound)" is not "Life Potion"), and the item list says the id isn't tradable. Items with no price are counted, never
/// guessed. Items not on the item list (soul crystals, costumes, the game's own counters) are left out.
/// </summary>
public static class BagValue
{
    /// <summary>One kind of item held: how many, its price (null for a bound item, or none known), its value, whether bound, the change since the first snapshot.</summary>
    public sealed record Line(long Id, string Name, long Count, long? Price, long Value, bool Bound, long Change);

    /// <summary>
    /// The figures: the tradable items' worth at the price, what that sells for after the Market's cut, how many tradable
    /// kinds have no price, the bound kinds and items (no value), the lines (most valuable first, bound last), and the
    /// tradable worth's change since <paramref name="first"/> (null when there is none).
    /// </summary>
    public sealed record Worth(long Total, long Sellable, int Unpriced, int Kinds, List<Line> Lines, long? Change, int BoundKinds = 0, long BoundCount = 0);

    public static Worth Of(IReadOnlyDictionary<long, long> now, IReadOnlyDictionary<long, long>? first, ItemDictionary items, Func<string, long?> price, double keeps)
    {
        var lines = new List<Line>();
        long total = 0, change = 0, boundCount = 0; int unpriced = 0, boundKinds = 0;
        var ids = now.Keys.Union(first?.Keys ?? Enumerable.Empty<long>()).ToList();
        foreach (var id in ids)
        {
            if (items.Get(id) is not { } info) continue;
            long n = now.GetValueOrDefault(id), was = first?.GetValueOrDefault(id) ?? n;
            bool bound = IsBound(info);
            long? p = bound ? null : price(info.Name);   // bound: can't be sold, no value
            if (p is long pv) change += (n - was) * pv;
            if (n <= 0) continue;
            long value = p is long v ? v * n : 0;
            total += value;
            if (bound) { boundKinds++; boundCount += n; }
            else if (p == null) unpriced++;
            lines.Add(new Line(id, info.Name, n, p, value, bound, n - was));
        }
        lines = lines.OrderBy(l => l.Bound).ThenByDescending(l => l.Value).ThenByDescending(l => l.Count).ToList();
        return new Worth(total, (long)Math.Round(total * keeps), unpriced, lines.Count, lines, first == null ? null : change, boundKinds, boundCount);
    }

    /// <summary>
    /// A worn piece's enhancement level as the game's window says it ("+10"), "" when none is said: the level is confirmed for
    /// +1 to +12 against the game's window (2026-11-13); over +15 (the weapon's 18 there) the game shows a badge of its own.
    /// </summary>
    public static string LevelText(int level) => level is >= 1 and <= MaxShownLevel ? $"+{level}" : "";
    public const int MaxShownLevel = 15;

    /// <summary>Whether a worn piece has a picture: only an item the item list knows has one (never a made-up icon).</summary>
    public static bool HasIcon(ItemDictionary items, long itemId) => items.Get(itemId) != null;

    /// <summary>Bound: its own id says so (the item list's "(Bound)" name, or the id not tradable).</summary>
    public static bool IsBound(ItemInfo info) => !info.Tradable || info.Name.EndsWith("(Bound)", StringComparison.OrdinalIgnoreCase);
}
