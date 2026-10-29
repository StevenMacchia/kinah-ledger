using System.Drawing;
using System.Text.RegularExpressions;

namespace KinahLedger.Ocr;

/// <summary>
/// Where the Market's listing table is and how its columns run, in screen pixels. Found from the column headers
/// ("Item", "Quantity", "Unit Price", "Total Price"), or a rectangle the player dragged. A dragged area has no
/// columns, so each row is read as one line and split by its numbers instead.
/// </summary>
public sealed class MarketLayout
{
    /// <summary>The rows under the header, where listings are read from.</summary>
    public Rectangle Table { get; init; }
    /// <summary>Title to table bottom: what the preview outlines.</summary>
    public Rectangle Outline { get; init; }
    /// <summary>Column kinds left to right (name, qty, unit, total); null for a hand-chosen area.</summary>
    public string[]? Kinds { get; init; }
    /// <summary>The x positions where one column ends and the next starts; one fewer than Kinds.</summary>
    public int[]? Bounds { get; init; }
    public bool Manual => Kinds == null;

    public static MarketLayout FromRegion(Rectangle r) => new() { Table = r, Outline = r };

    /// <summary>Which column a screen x falls in, by kind; null without columns.</summary>
    public string? ColumnAt(int x)
    {
        if (Kinds == null || Bounds == null) return null;
        int i = 0;
        while (i < Bounds.Length && x >= Bounds[i]) i++;
        return Kinds[i];
    }

    public override string ToString() => Manual
        ? $"chosen area {Table.Width} × {Table.Height} at {Table.X}, {Table.Y}"
        : $"table {Table.Width} × {Table.Height} at {Table.X}, {Table.Y}, columns {string.Join(" | ", Kinds!)}";
}

/// <summary>
/// Finds the Market window inside the game frame the way the chat locator finds the chat: by reading text with its
/// position. The column headers of the listing table give both the table's place and where each column runs; the
/// "Market" title above them confirms it. Only the screen is read; nothing is clicked and nothing is sent to the game.
/// </summary>
public static class MarketLocator
{
    /// <summary>Reads a screen rectangle; swapped for a screenshot crop by the market-test command.</summary>
    public delegate Task<List<OcrLineBox>> Reader(Rectangle region, int scale, bool boost);
    public static readonly Reader ScreenRead = (r, s, b) => ScreenReader.ReadBoxesAsync(r, s, b);

    static readonly Regex TitleRx = new(@"^(?:server\s+|cross[\s-]*server\s+)?market(?:place)?(?:\s*[|·:\-]?\s*exchange)?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    // header words as the recognizer sees them, with its usual slips (1 for l, 7 for t) allowed.
    // Two table shapes: the listing table (Item, Quantity, Unit Price, Total Price) and the Market's main board
    // (Grade, Item, Sales List, Lowest Sale Price), which is what the Exchange tab shows while browsing.
    static readonly (string kind, Regex rx)[] Headers =
    {
        ("name",     new(@"^(?:item|items|item\s*name|name)$", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("qty",      new(@"^(?:quantity|quant[il1]ty|qty|q[t7]y|amount|count|stock)$", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("lowest",   new(@"^(?:[l1]owest\s*sa[l1]e\s*pr[il1]ce|[l1]owest\s*pr[il1]ce|[l1]owest\s*sa[l1]e|[l1]owest)$", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("listings", new(@"^(?:sa[l1]es\s*[l1][il1]s[t7]|sa[l1]es|[l1]ist[il1]ngs?)$", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("grade",    new(@"^grade$", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("unit",     new(@"^(?:unit\s*pr[il1]ce|pr[il1]ce\s*(?:per|/|each)(?:\s*(?:unit|each|one))?|unit|pr[il1]ce|pr[il1]ce\s*each)$", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("total",    new(@"^(?:tota[l1]\s*pr[il1]ce|tota[l1]|sum|tota[l1]\s*amount)$", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
    };

    /// <summary>The middle of the game frame, where panels open; the outer few percent hold the HUD.</summary>
    public static Rectangle Area(Rectangle game) => new(game.Left + game.Width * 4 / 100, game.Top + game.Height * 3 / 100, game.Width * 92 / 100, game.Height * 94 / 100);

    /// <summary>Small screens are read at 2x like the chat; a 4K or wider frame has big enough text already and would pass the recognizer's size limit at 2x.</summary>
    public static int ScaleFor(Rectangle r) => r.Width <= 2600 && r.Height <= 1500 ? 2 : 1;

    public static async Task<(MarketLayout? layout, string note)> DetectAsync(Rectangle game, Reader? read = null)
    {
        var area = Area(game);
        var lines = await (read ?? ScreenRead)(area, ScaleFor(area), false);
        return Detect(game, lines);
    }

    public static string? KindOf(string text)
    {
        foreach (var (kind, rx) in Headers) if (rx.IsMatch(text)) return kind;
        return null;
    }

    static readonly char[] Edges = { '.', ':', ',', '|', '-', '_', '[', ']', '(', ')', '•', '·' };
    public static string Clean(string s) => Regex.Replace(s, @"\s+", " ").Trim().Trim(Edges);

    /// <summary>Works out the layout from lines already read, so a screenshot can be tested without the game.</summary>
    public static (MarketLayout? layout, string note) Detect(Rectangle game, List<OcrLineBox> lines)
    {
        var hits = new List<(string kind, Rectangle box)>();
        foreach (var line in lines)
        {
            var words = line.Words is { Count: > 0 } w ? w : new[] { new OcrWordBox(line.Text, line.Box) };
            var mine = new List<(string kind, Rectangle box)>(); int used = 0;
            for (int i = 0; i < words.Count; i++)
            {
                // three-word headers first ("Lowest Sale Price"), then two-word ("Unit Price", "Sales List"), then single words
                if (i + 2 < words.Count && words[i + 1].Box.Left - words[i].Box.Right < words[i].Box.Height * 2 && words[i + 2].Box.Left - words[i + 1].Box.Right < words[i].Box.Height * 2)
                {
                    var three = KindOf(Clean(words[i].Text + " " + words[i + 1].Text + " " + words[i + 2].Text));
                    if (three != null) { mine.Add((three, Rectangle.Union(Rectangle.Union(words[i].Box, words[i + 1].Box), words[i + 2].Box))); used += 3; i += 2; continue; }
                }
                if (i + 1 < words.Count && words[i + 1].Box.Left - words[i].Box.Right < words[i].Box.Height * 2)
                {
                    var pair = KindOf(Clean(words[i].Text + " " + words[i + 1].Text));
                    if (pair != null) { mine.Add((pair, Rectangle.Union(words[i].Box, words[i + 1].Box))); used += 2; i++; continue; }
                }
                var one = KindOf(Clean(words[i].Text));
                if (one != null) { mine.Add((one, words[i].Box)); used++; }
            }
            // a chat line that happens to say "price" is mostly other words; a header line is nothing but headers
            if (mine.Count > 0 && used * 10 >= words.Count * 6) hits.AddRange(mine);
        }
        if (hits.Count == 0) return (null, $"no Market column headers in view ({lines.Count} lines read)");

        // the header row: the headers sharing one baseline with the most kinds; it needs the unit price and a quantity or a total
        List<(string kind, Rectangle box)>? row = null;
        foreach (var h in hits)
        {
            int mid = h.box.Top + h.box.Height / 2;
            var same = hits.Where(o => o.box.Top <= mid && o.box.Bottom >= mid)
                           .GroupBy(o => o.kind).Select(g => g.OrderBy(o => o.box.Left).First())
                           .OrderBy(o => o.box.Left).ToList();
            bool listing = same.Any(o => o.kind == "unit") && same.Any(o => o.kind is "qty" or "total");
            bool board = same.Any(o => o.kind == "lowest") && same.Any(o => o.kind == "name");
            if ((listing || board) && (row == null || same.Count > row.Count)) row = same;
        }
        if (row == null) return (null, $"saw {string.Join(", ", hits.Select(h => h.kind).Distinct())} but not a whole header row");

        int hh = Math.Max(8, row.Max(o => o.box.Height));
        int numericLeft = row.Where(o => o.kind is not ("name" or "grade")).Min(o => o.box.Left);
        var kinds = new List<string>(); var bounds = new List<int>();
        if (row[0].kind is not ("name" or "grade")) { kinds.Add("name"); bounds.Add(numericLeft - hh / 2); }
        for (int i = 0; i < row.Count; i++)
        {
            kinds.Add(row[i].kind);
            if (i + 1 >= row.Count) continue;
            var a = row[i].box; var b = row[i + 1].box;
            // the name column runs up to the first number column; number columns split halfway between their headers
            bounds.Add(row[i].kind == "name" ? b.Left - hh / 2 : (a.Left + a.Width / 2 + b.Left + b.Width / 2) / 2);
        }
        int rowTop = row.Min(o => o.box.Top), rowBottom = row.Max(o => o.box.Bottom);
        int left = row[0].kind is "name" or "grade" ? Math.Max(game.Left, row[0].box.Left - hh) : Math.Max(game.Left, numericLeft - game.Width * 22 / 100);
        int right = Math.Min(game.Right, row[^1].box.Right + Math.Max(2 * hh, game.Width * 2 / 100));
        int top = rowBottom + 2;
        int bottom = Math.Min(game.Bottom - game.Height * 4 / 100, top + game.Height * 60 / 100);
        if (bottom - top < hh * 2 || right - left < hh * 6) return (null, "the header row sits too low or too close to the edge to hold a table");
        var table = Rectangle.FromLTRB(left, top, right, bottom);

        // the title sits above the headers; it confirms the window but isn't required, since a scrolled list can push it out of the read area
        var title = lines.FirstOrDefault(l => TitleRx.IsMatch(Clean(l.Text)) && l.Box.Bottom <= rowTop && rowTop - l.Box.Bottom < game.Height * 25 / 100 && l.Box.Right > left && l.Box.Left < right);
        int outlineTop = (title != null ? title.Box.Top : rowTop) - hh / 2;
        var layout = new MarketLayout { Table = table, Outline = Rectangle.FromLTRB(left, Math.Max(game.Top, outlineTop), right, bottom), Kinds = kinds.ToArray(), Bounds = bounds.ToArray() };
        return (layout, $"{(title != null ? "Market title and " : "")}headers {string.Join(", ", row.Select(o => o.kind))} found; {layout}");
    }
}

/// <summary>Reads the rows of a located table: words grouped by row, then by column when the layout has columns.</summary>
public static class MarketReader
{
    /// <summary>One row as read: the whole line, and the text per column when the columns are known.</summary>
    public sealed record Row(string Flat, IReadOnlyDictionary<string, string>? Cells, Rectangle Box);

    public static async Task<List<Row>> ReadRowsAsync(MarketLayout layout, int scale, MarketLocator.Reader? read = null)
    {
        var lines = await (read ?? MarketLocator.ScreenRead)(layout.Table, Math.Min(scale, MarketLocator.ScaleFor(layout.Table)), true);
        return Rows(layout, lines);
    }

    public static List<Row> Rows(MarketLayout layout, List<OcrLineBox> lines)
    {
        var words = new List<OcrWordBox>();
        foreach (var l in lines) words.AddRange(l.Words is { Count: > 0 } w ? w : new[] { new OcrWordBox(l.Text, l.Box) });
        words.Sort((a, b) => a.Box.Top != b.Box.Top ? a.Box.Top.CompareTo(b.Box.Top) : a.Box.Left.CompareTo(b.Box.Left));
        // a word joins the row whose vertical span holds its centre; otherwise it starts a row
        var groups = new List<List<OcrWordBox>>();
        foreach (var w in words)
        {
            int mid = w.Box.Top + w.Box.Height / 2;
            var home = groups.LastOrDefault(g => mid >= g.Min(x => x.Box.Top) && mid <= g.Max(x => x.Box.Bottom));
            if (home != null) home.Add(w); else groups.Add(new List<OcrWordBox> { w });
        }
        var rows = new List<Row>();
        foreach (var g in groups)
        {
            g.Sort((a, b) => a.Box.Left.CompareTo(b.Box.Left));
            var box = g.Skip(1).Aggregate(g[0].Box, (acc, x) => Rectangle.Union(acc, x.Box));
            Dictionary<string, string>? cells = null;
            if (!layout.Manual)
            {
                cells = new Dictionary<string, string>();
                foreach (var w in g)
                {
                    var k = layout.ColumnAt(w.Box.Left + w.Box.Width / 2)!;
                    cells[k] = cells.TryGetValue(k, out var s) ? s + " " + w.Text : w.Text;
                }
            }
            rows.Add(new Row(string.Join(" ", g.Select(x => x.Text)), cells, box));
        }
        // The main board shows each item on two lines, the name and "Item Level N"; the numbers sit between them and may
        // land on either, or on a line of their own. An item takes its level line and one line of numbers, each within a
        // line and a half of it. Unbounded, the fold ran on down an item's listings showing under the board's headers and
        // glued a page of them into one name ("16,799 16,799 2d 23h a 16,799 537,568 2d 23h ...", 2026-10-28). The board
        // has no time-left column, so a line with a time left is never folded either way: it stays a row of its own and
        // reads as nothing, and after a few such reads the Market is looked for again.
        if (layout.Kinds != null && Array.IndexOf(layout.Kinds, "lowest") >= 0)
        {
            var merged = new List<Row>();
            int open = -1, lineHeight = 0; bool hasLevel = false, hasNumbers = false;   // the item that can still take a line
            foreach (var r in rows)
            {
                if (MarketParser.HasTimeLeft(r.Flat)) { merged.Add(r); open = -1; continue; }
                var name = CellOf(r, "name");
                bool level = LevelLine.IsMatch(name), nameless = !level && name.Count(char.IsLetter) < 3;
                bool numbers = r.Cells!.Any(kv => kv.Key != "name" && kv.Value.Any(char.IsDigit));
                if (nameless && !numbers) continue;   // a stray mark or icon between lines: no name and no price
                bool near = open >= 0 && r.Box.Top - merged[open].Box.Bottom < lineHeight * 1.5;
                if (near && level && !hasLevel) { merged[open] = Fold(merged[open], r); hasLevel = true; continue; }
                if (near && nameless && !hasNumbers && !CellOf(merged[open], "lowest").Any(char.IsDigit)) { merged[open] = Fold(merged[open], r); hasNumbers = true; continue; }
                merged.Add(r);
                if (level) open = -1;                 // a level line on its own ends the item above it
                else if (!nameless) { open = merged.Count - 1; lineHeight = r.Box.Height; hasLevel = hasNumbers = false; }
            }
            rows = merged;
        }
        return rows;
    }

    static string CellOf(Row r, string kind) => r.Cells != null && r.Cells.TryGetValue(kind, out var v) ? v.Trim() : "";

    /// <summary>A board item's level line or numbers line added to it; the line's own name cell ("Item Level 10", a stray mark) stays out of the name.</summary>
    static Row Fold(Row item, Row line)
    {
        var cells = new Dictionary<string, string>(item.Cells!);
        foreach (var (k, v) in line.Cells!) if (k != "name") cells[k] = cells.TryGetValue(k, out var s) && s.Length > 0 ? s + " " + v : v;
        return new Row(item.Flat + " " + line.Flat, cells, Rectangle.Union(item.Box, line.Box));
    }

    static readonly Regex LevelLine = new(@"^(?:item\s*)?[l1]eve[l1]\s*\d{1,3}$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
}
