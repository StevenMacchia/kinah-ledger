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
    /// <summary>A listing's time left as the recognizer reads it: "2d 23h", "ld 2h" (a 1 read as l), "3h 5min". No item name holds one.</summary>
    [GeneratedRegex(@"(?<![A-Za-z0-9])[0-9lI|]{1,2}\s?d\s+[0-9lI|]{1,2}\s?h(?![A-Za-z0-9])|(?<![A-Za-z0-9])[0-9lI|]{1,2}\s?h\s+[0-9lI|]{1,2}\s?m(?:in)?(?![A-Za-z0-9])", RegexOptions.IgnoreCase)]
    private static partial Regex TimeLeftRx();

    public static bool HasTimeLeft(string text) => TimeLeftRx().IsMatch(text);

    /// <summary>The longest name kept: the site takes item names up to 64 characters, and the longest item is 55.</summary>
    public const int MaxNameLength = 64;

    /// <summary>
    /// Text that is a Market row's numbers rather than a name: longer than any item name, a time left in it, or more digits
    /// and separators than letters. Whole rows were read as names this way ("0 6576 0 6,576 2d 23h 0 6577 ...",
    /// "0 50,000 ld 2h", 34 of the 59 names in one player's prices.json, 2026-10-28).
    /// </summary>
    public static bool IsRowText(string name)
    {
        if (name.Length > MaxNameLength || HasTimeLeft(name)) return true;
        int letters = 0, digits = 0;
        foreach (var c in name) { if (char.IsLetter(c)) letters++; else if (char.IsDigit(c) || c is ',' or '.') digits++; }
        return digits > letters;
    }

    public MarketAsk? Parse(MarketReader.Row row) => row.Cells != null ? ParseCells(row.Cells) : Parse(row.Flat);

    /// <summary>A table row with its columns already apart.</summary>
    public MarketAsk? ParseCells(IReadOnlyDictionary<string, string> cells)
    {
        if (!cells.TryGetValue("name", out var nameText)) return null;
        nameText = KnownName(nameText);
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

    /* ---------------- an item's listings, shown under the main board's headers ---------------- */

    /// <summary>What <see cref="Listings"/> found: the asks, how many listing rows were on screen, and the item they were named for (null when none could be).</summary>
    public sealed record ListingRead(List<MarketAsk> Asks, int Rows, string? Item);

    /// <summary>
    /// An item's listings as the Market shows them when it is opened to buy (2026-10-28 reads, Steven's 2026-11-10 report):
    /// under the main board's headers, one row per listing with its unit price, its total and a time left ("16,799 537,568
    /// 2d 23h"), and no name of their own. <see cref="Parse(MarketReader.Row)"/> reads none of them, so a Market opened only
    /// to buy read nothing at all. Here they are named for the line above them, and only when that line is an item the
    /// dictionary knows; a name it doesn't know in between leaves the listings below unnamed (a header line doesn't).
    /// The stack is the total over the unit price, so a misread digit drops the row rather than guess.
    /// </summary>
    public ListingRead Listings(IReadOnlyList<MarketReader.Row> rows)
    {
        var asks = new List<MarketAsk>(); int listingRows = 0; string? item = null, named = null;
        foreach (var r in rows)
        {
            if (HasTimeLeft(r.Flat))
            {
                listingRows++;
                if (item == null || ListingNumbers(r.Flat) is not { } n) continue;
                if (Make(item, n.Qty, n.Unit) is { Known: true } a) { asks.Add(a); named = a.Item; }
                continue;
            }
            var text = r.Cells != null && r.Cells.TryGetValue("name", out var c) ? c : r.Flat;
            if (LevelOnly().IsMatch(text.Trim())) continue;            // the item's own "Item Level 10" line keeps it
            // only a line that reads as a name changes whose listings follow; a header line, a mark or a line of numbers doesn't
            if (HeaderOnly().IsMatch(text.Trim())) continue;            // a listing table's own headers, read as one line
            var name = CleanName(text);
            if (name == null) continue;
            item = _items.Knows(name) ? name : null;
        }
        return new ListingRead(asks, listingRows, named);
    }

    [GeneratedRegex(@"^(?:item\s*)?[l1]eve[l1]\s*\d{1,3}$", RegexOptions.IgnoreCase)]
    private static partial Regex LevelOnly();
    /// <summary>A line of nothing but column headers ("Unit Price Total Price Time Left"): no item's name.</summary>
    [GeneratedRegex(@"^(?:(?:unit|pr[il1]ces?|tota[l1]|quant[il1]ty|qty|sa[l1]es?|[l1]owest|[l1]ist|[l1]ist[il1]ngs?|items?|name|grade|time|[l1]eft|remaining|se[l1][l1]er|each|per|amount|count|stock|period|date|expires?|expiry|registered|/)\s*)+$", RegexOptions.IgnoreCase)]
    private static partial Regex HeaderOnly();

    /// <summary>
    /// A listing row's stack and unit price: the first two numbers (ten or more; a stray "0" or "01" from an icon is skipped)
    /// where the second is a whole number of the first, the time left taken out first. "16,799 537,568 2d 23h 32" is 32 at
    /// 16,799; "17,000 34,000 2d 22h" is 2 at 17,000; "16,799 16,799 2d 23h" is 1. Null when no pair agrees.
    /// </summary>
    public static (int Qty, long Unit)? ListingNumbers(string flat)
    {
        var nums = new List<long>();
        foreach (var tok in TimeLeftRx().Replace(flat, " ").Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!IsNum(tok)) continue;
            long v = ChatParser.Digits(NumTok().Match(tok).Groups["n"].Value);
            if (v >= 10) nums.Add(v);
        }
        for (int i = 0; i + 1 < nums.Count; i++)
        {
            long u = nums[i], t = nums[i + 1];
            if (u <= MaxPrice && t % u == 0 && t / u is >= 1 and <= MaxQty) return ((int)(t / u), u);
        }
        return null;
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

    /// <summary>
    /// A name cell that holds more than the name: the Main tab's rows have a checkbox and the item's icon, whose stack count
    /// reads as a number beside the name ("40 Fresh Raw Meat"). When the cell isn't an item as it is, a short token at either
    /// end is dropped, one at a time, for as long as that leaves an item the list knows; otherwise the cell is kept as read.
    /// </summary>
    string KnownName(string cell)
    {
        if (CleanName(cell) is { } whole && _items.Knows(whole)) return cell;
        var toks = cell.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        for (int drop = 1; drop <= 3 && toks.Count > 1; drop++)
        {
            if (toks[0].Length <= 4 && !toks[0].All(char.IsLetter)) toks.RemoveAt(0);
            else if (toks[^1].Length <= 4 && !toks[^1].All(char.IsLetter)) toks.RemoveAt(toks.Count - 1);
            else break;
            var t = string.Join(" ", toks);
            if (CleanName(t) is { } n && _items.Knows(n)) return t;
        }
        return cell;
    }

    static readonly char[] NameEdges = { '.', ':', ',', ';', '|', '-', '_', '[', ']', '(', ')', '"', '•', '·' };

    /// <summary>A name worth keeping: tags and stray icon characters removed, at least three letters, not a header or a button, not a row's numbers.</summary>
    public static string? CleanName(string raw)
    {
        var name = NameTags().Replace(raw, " ");
        name = Regex.Replace(name, @"\s+", " ").Trim().Trim(NameEdges);
        name = Regex.Replace(name, @"^[^A-Za-z0-9]{1,2}\s+", "");   // an item icon often reads as one or two symbols before the name
        if (name.Count(char.IsLetter) < 3) return null;
        if (MarketLocator.KindOf(name) != null || UiWords().IsMatch(name)) return null;
        if (IsRowText(name)) return null;   // a backstop: the reader keeps rows apart, but a misread one must never become an item
        return name;
    }
}
