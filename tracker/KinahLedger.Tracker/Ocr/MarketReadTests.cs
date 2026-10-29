using System.Drawing;
using KinahLedger.Data;

namespace KinahLedger.Ocr;

/// <summary>
/// Run by market-test: whole Market rows must never become an item's name. The names below are the ones a player's
/// prices.json held on 2026-10-28 (34 of its 59): the main board's fold had glued a page of an item's listings into one
/// name, and short misreads with a time left ("0 50,000 ld 2h") got through as items. Every one must be refused, every
/// item the dictionary knows must still pass, and drawn Market rows (words placed where the recognizer puts them) must
/// come out one listing per row.
/// </summary>
static class MarketReadTests
{
    static readonly string[] Garbled =
    {
        "0 6576 0 6,576 2d 23h 0 6577 0 6,577 2d 22h 0 6577 0 6,577 2d 23h 0 6577 0 6,577 2d 23h 0 6577 0 6,577 2d 23h 0 6577 0 6,577 2d 23h",
        "71,999 71 , 999 .2d 23h a 95,555 95,555 2d 23h 95,900 0 95,900 2d 23h 95,900 0 95,900 2d 23h 95,990 0 95,990 2d 22h 95,990 0 95,990 2d 22h",
        "0 71,999 71,999 2d 23h a 95,555 95,555 2d 23h 95,900 0 95,900 2d 23h 95,900 0 95,900 2d 23h 95,990 0 95,990 2d 22h 95,990 0 95,990 2d 22h",
        "95,555 95,555 2d 23h a 95,900 95,900 2d 23h 95,900 95,900 2d 23h 95,990 95,990 2d 22h 95,990 95,990 2d 22h",
        "95,555 95,555 2d 23h a 95,900 0 95,900 2d 23h 95,900 0 95,900 2d 23h 95,990 0 95,990 2d 22h 95,990 0 95,990 2d 22h 95,998 0 95,998 2d 22h",
        "0 71,999 a 71,999 2d 23h a 95,555 95,555 2d 23h 95,900 0 95,900 2d 23h 95,900 95,900 2d 23h 95,990 0 95,990 2d 22h",
        "57,497 57,497 2d 23h a 82,900 82,900 2d 23h 82,900 82,900 2d 23h 82,900 0 82,900 2d 23h 82,990 82,990 2d 23h 82,990 0 82,990 2d 23h",
        "82,900 82,900 2d 23h a 82,900 82,900 2d 23h 82,900 82,900 2d 23h 82,990 0 82,990 2d 23h 82,990 82,990 2d 23h 83,000 0 83,000 2d 21h",
        "16,799 16,799 2d 23h a 16,799 537,568 2d 23h 16,991 67,964 2d 23h 16,991 0 169,910 2d 23h 17,000 34,000 2d 22h",
        "70,000 70,000 2d 23h a 70,000 70,000 2d 23h 75,000 0 75,000 2d 23h 79,999 79,999 2d 23h 79,999 79,999 2d 23h 94,222 0 94,222 2d 23h",
        "58,884 0 294,420 2d 23h a 60,000 2d 23h 66,666 0 399,996 2d 21h 66,666 0 333,330 2d 21h 66,666 0 599,994 2d 21h 66,666 0 599,994 2d 22h",
        "67,885 67,885 2d 23h a 89,990 89,990 2d 23h 90,000 0 90,000 2d 23h 90,000 0 90,000 2d 23h 94,444 0 94,444 2d 15h 94,444 0 94,444 2d 19h",
        "67,879 0 339,395 2d 23h a 67,885 67,885 2d 23h 67,885 0 814,620 2d 23h 69,999 2d 23h 70,000 0140,000 2d 23h 70,000 2d 23h",
        "2d 23h a 120,000 120,000 2d 22h",
        "2d 23h 2d 23h 2d 23h 2d 23h 2d 23h 2d 23h",
        "ld 23h",
        "46,000 2d 22h a 46,000 184,000 2d 23h 0 47,897 0 239,485 2d 22h 0 47,898 01 2d 21h 0 47,900 2d 21h 47,984 0 527,824 2d 21h",
        "72,222 0 288,888 2d 23h a 73,000 584,000 2d 23h 74,997 2d 23h 74,997 0 749,970 2d 23h 74,998 2d 22h 74,999 2d 22h",
        "2d 22h a 120,000 120,000 2d 21h",
        "item 39 722,210 2d 20h 722,210 2d 20h",
        "722,210 2d 20h 722,210 2d 21h",
        "64,997 64,997 2d 23h a 68,887 68,887 2d 23h 68,887 68,887 2d 23h 68,887 68,887 2d 23h 68,887 01 2d 23h 70,000 2d 23h",
        "2d 23h a 01700 2d 23h 2d 22h 2d 23h 2d 23h 01749 2d 21h",
        "01700 2d 23h a 1740 2d 22h 2d 23h 2d 23h 2d 21h 2d 20h",
        "1740 2d 23h a 1740 2d 23h 1749 2d 21h 1850 2d 20h 2d 20h",
        "2d 21h a 01850 2d 20h 01850 2d 20h 01870 2d 19h",
        "0 50,000 ld 2h",
        "50,000 ld 2h @ -v",
        "130,000 3h 5min",
        "item 35 2d 15h 01 2d 23h",
        "item 35 2d 23h 01",
        "016,999 016,999 2d 23h 16,999 16,999 2d 23h 016,999 0 16,999 2d 23h 016,999 0 16,999 2d 23h 016,999 016,999 2d 23h 016,999 016,999 2d 23h",
        "16,999 16,999 2d 23h 16,999 16,999 2d 23h 16,999 16,999 2d 23h 16,999 16,999 2d 23h 16,999 16,999 2d 23h 16,999 16,999 2d 23h",
        "3089 0 407,748 2d 23h 03090 2d 23h 3280 01 2d 23h 03690 2d 23h 3698 0 73,960 2d 23h 03698 0314,330 2d 23h",
    };

    public static bool Run(MarketParser parser, ItemDictionary items)
    {
        bool ok = true;
        void Check(string what, string got, string want) { bool pass = got == want; ok &= pass; Console.WriteLine($"  {what,-62} -> {got}  {(pass ? "ok" : "MISMATCH, expected " + want)}"); }

        // the backstop: each garbled name is refused as a name, as a board cell and at the front of a chosen area's line
        Console.WriteLine("\nMarket rows read as a name (a player's prices.json, 2026-10-28):");
        foreach (var g in Garbled)
        {
            var asName = MarketParser.CleanName(g);
            var asCell = parser.ParseCells(new Dictionary<string, string> { ["name"] = g, ["lowest"] = "1" });
            var asLine = parser.Parse(g + " x1 7,499 7,499");
            var got = asName == null && asCell == null && asLine == null ? "(ignored)" : $"kept: {asName ?? asCell?.Item ?? asLine?.Item}";
            Check(g.Length > 58 ? g[..55] + "..." : g, got, "(ignored)");
        }

        // and nothing real is refused: every item name the dictionary knows still reads as a name
        var refused = items.AllItems().Select(i => i.Name).Where(n => n.Length > 0 && MarketParser.IsRowText(n)).Distinct().ToList();
        Check($"{items.AllItems().Count} known item names, none taken for a row's numbers", refused.Count == 0 ? "none refused" : string.Join(" | ", refused.Take(5)), "none refused");
        foreach (var (name, want) in new[] { ("Enchantment Stone 15", "Enchantment Stone 15"), ("Radiant Odyle Lv. 10", "Radiant Odyle"), ("Artwork: Destruction Archons High Priest Hystan (Bound)", "Artwork: Destruction Archons High Priest Hystan") })
            Check($"\"{name}\" stays a name", MarketParser.CleanName(name) ?? "(ignored)", want);
        Check("a name over 64 characters is refused", MarketParser.CleanName(new string('a', 40) + " " + new string('b', 30)) ?? "(ignored)", "(ignored)");

        // the source: the main board's fold. Headers as the main board shows them, then rows drawn word by word
        Console.WriteLine("\nMarket reader, the main board's rows (drawn):");
        var game = new Rectangle(0, 0, 1920, 1080);
        var (board, _) = MarketLocator.Detect(game, new List<OcrLineBox> { Line(200, 20, ("Grade", 300, 60), ("Item", 420, 40), ("Sales", 900, 50), ("List", 955, 35), ("Lowest", 1150, 60), ("Sale", 1215, 40), ("Price", 1260, 50)) });
        Check("the main board's headers are found", board?.Kinds is { } bk ? string.Join(", ", bk) : "(none)", "grade, name, listings, lowest");
        if (board == null) return false;
        string Asks(MarketLayout layout, List<OcrLineBox> lines) { var a = MarketReader.Rows(layout, lines).Select(r => parser.Parse(r)).Where(x => x != null).ToList(); return a.Count == 0 ? "(none)" : string.Join(" / ", a); }
        string Names(MarketLayout layout, List<OcrLineBox> lines) => string.Join(" / ", MarketReader.Rows(layout, lines).Select(r => r.Cells != null && r.Cells.TryGetValue("name", out var n) ? n : ""));

        // two items: one with its numbers on the name line, one with them on a line of their own between name and level
        var items2 = new List<OcrLineBox>
        {
            Line(240, 20, ("Spiritstone", 450, 90), ("12", 940, 20), ("7,499", 1200, 50)),
            Line(264, 16, ("Item", 450, 30), ("Level", 484, 36), ("10", 524, 16)),
            Line(300, 20, ("Raw", 450, 30), ("Leather", 484, 60)),
            Line(318, 18, ("3", 945, 10), ("14,949", 1200, 60)),
            Line(340, 16, ("Item", 450, 30), ("Level", 484, 36), ("20", 524, 16)),
        };
        Check("a name, its numbers and its level line make one item", Asks(board, items2), "Spiritstone ×1 @ 7,499 / Raw Leather ×1 @ 14,949");

        // an item's listings showing under the board's headers: prices, totals and times in the name column, the stack in
        // the lowest-price column, and a mark between the first two rows. The old fold made one row of all of it, named
        // "16,799 16,799 2d 23h a 16,799 537,568 2d 23h 16,991 67,964 2d 23h 16,991 0 169,910 2d 23h 17,000 34,000 2d 22h"
        var listings = new List<OcrLineBox>
        {
            Line(400, 20, ("16,799", 430, 60), ("16,799", 560, 60), ("2d", 700, 20), ("23h", 724, 30), ("1", 1200, 10)),
            Line(424, 10, ("a", 640, 10)),
            Line(440, 20, ("16,799", 430, 60), ("537,568", 560, 70), ("2d", 700, 20), ("23h", 724, 30), ("32", 1200, 20)),
            Line(480, 20, ("16,991", 430, 60), ("67,964", 560, 60), ("2d", 700, 20), ("23h", 724, 30), ("4", 1200, 10)),
            Line(520, 20, ("16,991", 430, 60), ("0", 540, 10), ("169,910", 560, 70), ("2d", 700, 20), ("23h", 724, 30), ("10", 1200, 20)),
            Line(560, 20, ("17,000", 430, 60), ("34,000", 560, 60), ("2d", 700, 20), ("22h", 724, 30), ("2", 1200, 10)),
        };
        Check("listings under the board's headers stay one per row", Names(board, listings), "16,799 16,799 2d 23h / 16,799 537,568 2d 23h / 16,991 67,964 2d 23h / 16,991 0 169,910 2d 23h / 17,000 34,000 2d 22h");
        Check("and none of them is read as an ask", Asks(board, listings), "(none)");

        // a board item with no price on its line, right above a listing: the listing's stack is not the item's price
        var above = new List<OcrLineBox> { Line(360, 20, ("Fine", 450, 40), ("Odyle", 494, 50)) };
        above.AddRange(listings);
        Check("a listing never gives the item above it a price", Asks(board, above), "(none)");

        // the fold reaches one line: numbers two lines down belong to nothing above them
        var far = new List<OcrLineBox> { Line(240, 20, ("Spiritstone", 450, 90)), Line(300, 20, ("12", 940, 20), ("7,499", 1200, 50)) };
        Check("numbers a few lines down are not an item's", Asks(board, far), "(none)");

        // the listing table, with a time left past the total: the row still reads, the time is never part of a number
        Console.WriteLine("\nMarket reader, a listing table with a time left (drawn):");
        var (listing, _) = MarketLocator.Detect(game, new List<OcrLineBox> { Line(200, 20, ("Item", 420, 40), ("Quantity", 900, 80), ("Unit", 1050, 40), ("Price", 1094, 50), ("Total", 1250, 50), ("Price", 1304, 50)) });
        Check("the listing table's headers are found", listing?.Kinds is { } lk ? string.Join(", ", lk) : "(none)", "name, qty, unit, total");
        if (listing == null) return false;
        var rows = new List<OcrLineBox>
        {
            Line(240, 20, ("Spiritstone", 450, 90), ("10", 930, 20), ("7,520", 1080, 50), ("75,200", 1270, 60), ("2d", 1380, 20), ("23h", 1404, 30)),
            Line(280, 20, ("Raw", 450, 30), ("Leather", 484, 60), ("20", 930, 20), ("14,949", 1080, 60), ("298,980", 1270, 70), ("3h", 1380, 20), ("5min", 1404, 40)),
        };
        Check("rows with a time left read as asks", Asks(listing, rows), "Spiritstone ×10 @ 7,520 / Raw Leather ×20 @ 14,949");
        return ok;
    }

    /// <summary>One line as the recognizer returns it: words at x with a width, on a band from y of the given height.</summary>
    static OcrLineBox Line(int y, int h, params (string text, int x, int w)[] words)
    {
        var boxes = words.Select(w => new OcrWordBox(w.text, new Rectangle(w.x, y, w.w, h))).ToList();
        var box = boxes.Skip(1).Aggregate(boxes[0].Box, (acc, b) => Rectangle.Union(acc, b.Box));
        return new OcrLineBox(string.Join(" ", words.Select(w => w.text)), box, boxes);
    }
}
