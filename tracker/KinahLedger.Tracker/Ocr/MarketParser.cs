using System.Text.RegularExpressions;
using KinahLedger.Data;

namespace KinahLedger.Ocr;

/// <summary>One listing as read: the item, how many in the stack and the price per unit. Nothing about who listed it.</summary>
public sealed record MarketAsk(long ItemId, string Item, int Qty, long UnitPrice, bool Known)
{
    /// <summary>The same ask seen again (same item, stack and price) within the repeat window counts once.</summary>
    public string Key => $"{Item.ToLowerInvariant()}|{Qty}|{UnitPrice}";
    public override string ToString() => $"{Item} ×{Qty} @ {UnitPrice:N0}";
}

/// <summary>
/// Turns a Market row into an ask. With columns (the auto-detected table) each cell is read on its own. A hand-chosen
/// area gives one line per row, "Spiritstone x10 7,520 75,200", which is split by its numbers from the right:
/// quantity, unit price, total. The total has to equal quantity × unit price, so a misread digit drops the row
/// instead of sharing a wrong price. Digits and thousands separators are read the way chat Kinah amounts are.
/// </summary>
public sealed partial class MarketParser
{
    readonly ItemDictionary _items;
    public MarketParser(ItemDictionary items) { _items = items; }

    public const long MaxPrice = 9_999_999_999, MaxQty = 9_999;

    [GeneratedRegex(@"^(?<x>[×xX\*])?(?<n>[0-9OoIl][0-9OoIl,\.]*)$")]
    private static partial Regex NumTok();
    /// <summary>Words that ride along with prices and say nothing: "7,499 Kina", "each".</summary>
    [GeneratedRegex(@"^(?:kinah?|k|ea|each|per|unit|units|pcs?)\.?$", RegexOptions.IgnoreCase)]
    private static partial Regex Filler();
    /// <summary>Level tags and bind marks that follow a name in the list.</summary>
    [GeneratedRegex(@"\s*(?:\(bound\)|\[?\blv\.?\s*\d{1,3}\]?|\bitem\s+[l1]eve[l1]\s*\d{1,3}|\b[l1]eve[l1]\s+\d{1,3})\s*", RegexOptions.IgnoreCase)]
    private static partial Regex NameTags();
    [GeneratedRegex(@"^(?:buy|sell|search|my\s+listings?|listings?|page|register|registration|sort|filter|all|category|categories|cancel|collect|settle(?:ment)?|settlement\s+details?|main|list|grade|item|exchange|market|enter\s+a\s+search.*|current\s+tax\s+rate.*|sales?\s*list|lowest\s*sale\s*price)$", RegexOptions.IgnoreCase)]
    private static partial Regex UiWords();

    public MarketAsk? Parse(MarketReader.Row row) => row.Cells != null ? ParseCells(row.Cells) : Parse(row.Flat);

    /// <summary>A table row with its columns already apart.</summary>
    public MarketAsk? ParseCells(IReadOnlyDictionary<string, string> cells)
    {
        if (!cells.TryGetValue("name", out var nameText)) return null;
        // the main board: one row per item with its lowest sale price; the price is per unit, so the stack is one
        if (cells.TryGetValue("lowest", out var lowText))
        {
            long low = Number(lowText);
            return low > 0 ? Make(nameText, 1, low) : null;
        }
        if (!cells.TryGetValue("unit", out var unitText)) return null;
        long unit = Number(unitText); if (unit <= 0) return null;
        long qty = cells.TryGetValue("qty", out var q) ? Number(q) : 0;
        long total = cells.TryGetValue("total", out var t) ? Number(t) : 0;
        if (qty > 0 && total > 0 && qty * unit != total) return null;      // one of the three was misread; which one can't be known
        if (qty <= 0 && total > 0) { if (total % unit != 0) return null; qty = total / unit; }
        if (qty <= 0) return null;
        return Make(nameText, qty, unit);
    }

    /// <summary>One line per row, numbers on the right. A seller name or a time left after the numbers is dropped, never kept.</summary>
    public MarketAsk? Parse(string line)
    {
        var toks = line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        int end = toks.Length - 1;
        while (end >= 0 && !IsNum(toks[end])) end--;
        if (end < 0) return null;
        // walk in from the right collecting numbers, stepping over filler words, stopping at the name
        var nums = new List<(int idx, long v, bool marked, bool sep)>();
        for (int i = end; i >= 0; i--)
        {
            if (IsNum(toks[i]))
            {
                var m = NumTok().Match(toks[i]);
                nums.Insert(0, (i, ChatParser.Digits(m.Groups["n"].Value), m.Groups["x"].Success, m.Groups["n"].Value.IndexOfAny(Separators) >= 0));
            }
            else if (!Filler().IsMatch(toks[i])) break;
        }
        // three numbers: quantity, unit price, total, which must agree. A number ahead of them ("Lv. 10") stays in the name.
        if (nums.Count >= 3)
        {
            var (q, u, t) = (nums[^3], nums[^2], nums[^1]);
            if (q.v is >= 1 and <= MaxQty && u.v >= 1 && q.v * u.v == t.v) return Make(NameBefore(toks, q.idx), q.v, u.v);
        }
        if (nums.Count >= 2)
        {
            var (a, b) = (nums[^2], nums[^1]);
            // "x10 7,520": a marked stack; "1 1,950": a small bare count; "7,499 74,990": unit price then total, the stack is the ratio
            if (a.marked || (!a.sep && a.v <= 999 && b.v >= a.v)) return a.v >= 1 && b.v >= 1 ? Make(NameBefore(toks, a.idx), a.v, b.v) : null;
            if (a.v >= 1 && b.v % a.v == 0 && b.v / a.v is >= 1 and <= MaxQty) return Make(NameBefore(toks, a.idx), b.v / a.v, a.v);
        }
        return null;   // a lone number is a price without a stack, or a chat line; never guessed
    }

    static readonly char[] Separators = { ',', '.' };

    static bool IsNum(string tok)
    {
        var m = NumTok().Match(tok);
        return m.Success && (m.Groups["x"].Success || m.Groups["n"].Value.Any(char.IsDigit));
    }

    /// <summary>The first number in a cell, read with the chat's digit corrections; 0 when there is none.</summary>
    static long Number(string cell)
    {
        foreach (var tok in cell.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            if (IsNum(tok)) return ChatParser.Digits(NumTok().Match(tok).Groups["n"].Value);
        return 0;
    }

    static string NameBefore(string[] toks, int firstNumber)
    {
        int n = firstNumber;
        while (n > 0 && Filler().IsMatch(toks[n - 1])) n--;
        return string.Join(" ", toks.Take(n));
    }

    MarketAsk? Make(string rawName, long qty, long unit)
    {
        if (qty < 1 || qty > MaxQty || unit < 1 || unit > MaxPrice) return null;
        var name = CleanName(rawName);
        if (name == null) return null;
        bool known = _items.Knows(name);
        long id = _items.IdOf(name);
        return new MarketAsk(id, _items.NameOf(id), (int)qty, unit, known);
    }

    static readonly char[] NameEdges = { '.', ':', ',', ';', '|', '-', '_', '[', ']', '(', ')', '"', '•', '·' };

    /// <summary>A name worth keeping: tags and stray icon characters removed, at least three letters, not a header or a button.</summary>
    public static string? CleanName(string raw)
    {
        var name = NameTags().Replace(raw, " ");
        name = Regex.Replace(name, @"\s+", " ").Trim().Trim(NameEdges);
        name = Regex.Replace(name, @"^[^A-Za-z0-9]{1,2}\s+", "");   // an item icon often reads as one or two symbols before the name
        if (name.Count(char.IsLetter) < 3) return null;
        if (MarketLocator.KindOf(name) != null || UiWords().IsMatch(name)) return null;
        return name;
    }
}
