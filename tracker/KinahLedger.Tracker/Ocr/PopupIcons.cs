using System.Drawing;
using KinahLedger.Data;

namespace KinahLedger.Ocr;

/// <summary>
/// The icon at the start of each row of the loot list beside the character, as a second reading of the row's name
/// (2026-10-23). A long name scrolls sideways, so a read often shows only its middle or end ("itstone x1"), and a row
/// read once during fast kills can come out garbled; the icon beside it doesn't move. A row whose text names an item
/// exactly is left as it is. Otherwise the items its letters could be (ItemDictionary.Fragment and Near) are compared by
/// icon, the way the reward table's slots are (IconIndex), and the row is named only when one icon is clearly the art
/// beside it: closer than <see cref="SureDistance"/> and ahead of the next by <see cref="MinMargin"/>. Items that share
/// an icon can't be told apart by it, so they give no answer. The icon's exact place beside the text isn't measured
/// yet, so a few sizes and gaps are tried and the closest kept; `popup-test --image` prints them for a real picture.
/// </summary>
public static class PopupIcons
{
    /// <summary>The reward table's own sure distance (TrackerSession.GridSureDistance).</summary>
    public const double SureDistance = 70;
    public const double MinMargin = 12;
    /// <summary>More items than this fit the letters: the read says too little for an icon to settle.</summary>
    public const int MaxCandidates = 40;

    public sealed record Verdict(long Id, string Name, double Distance, double Next);

    /// <summary>What the icon said about one row: the row's text to use, the candidates' distances, and the verdict if any.</summary>
    public sealed record Look(string Text, List<(string Name, double Distance)> Scores, Verdict? Verdict, string Note);

    /// <summary>The squares the icon may sit in, just left of a row's text and centred on it, inside <paramref name="bounds"/>.</summary>
    public static IEnumerable<Rectangle> Crops(Rectangle text, Rectangle bounds)
    {
        int h = text.Height;
        if (h < 6) yield break;
        foreach (var k in new[] { 1.3, 1.6, 2.0, 2.4 })
            foreach (var gap in new[] { 0.15, 0.4, 0.8 })
            {
                int s = (int)Math.Round(h * k), right = text.Left - (int)Math.Round(h * gap);
                var r = new Rectangle(right - s, text.Top + h / 2 - s / 2, s, s);
                if (s >= 8 && bounds.Contains(r)) yield return r;
            }
    }

    /// <summary>
    /// The verdict over candidates scored by icon (each candidate's best distance over the crops): the closest icon, when it
    /// is sure and clearly ahead of the next icon. The items behind that icon must be one item: a tradable and a bound
    /// copy share their art ("Spiritstone", "Spiritstone (Bound)"), and are one drop, so the row's own "(Bound)" picks
    /// between them; two different items with one icon give no answer. Pure: tested in parse-test.
    /// </summary>
    public static Verdict? Decide(IReadOnlyList<(long Id, string Name, string IconKey, double Distance)> scored, bool bound = false)
    {
        var byIcon = scored.GroupBy(x => x.IconKey).Select(g => (Items: g.Select(x => (x.Id, x.Name)).Distinct().ToList(), D: g.Min(x => x.Distance))).OrderBy(x => x.D).ToList();
        if (byIcon.Count == 0) return null;
        var best = byIcon[0];
        double next = byIcon.Count > 1 ? byIcon[1].D : double.MaxValue;
        if (best.D > SureDistance || next - best.D < MinMargin) return null;
        static string Base(string n) => System.Text.RegularExpressions.Regex.Replace(n, @"\s*\(bound\)\s*$", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim().ToLowerInvariant();
        if (best.Items.Select(i => Base(i.Name)).Distinct().Count() != 1) return null;
        var pick = best.Items.OrderBy(i => i.Name.Contains("(bound)", StringComparison.OrdinalIgnoreCase) == bound ? 0 : 1).ThenBy(i => i.Name.Length).First();
        return new Verdict(pick.Id, pick.Name, best.D, next);
    }

    /// <summary>
    /// One row of the list read at <paramref name="line"/> (screen coordinates), with <paramref name="raw"/> the capture
    /// of the list's area whose top-left sits at <paramref name="origin"/> on screen. Returns the text to count the row by:
    /// the row as read, or "Name xN" when the icon named it.
    /// </summary>
    public static Look Name(ItemDictionary items, Bitmap raw, Point origin, OcrLineBox line)
    {
        var none = new List<(string, double)>();
        var rows = LootPopup.Rows(new[] { (line.Box.Top, line.Text) });
        if (rows.Count != 1) return new Look(line.Text, none, null, "");
        var row = rows[0];
        if (row.IsKinah || Session.LootCrossCheck.SoulOf(row.Name) != null || items.IsExact(row.Name)) return new Look(line.Text, none, null, "");
        bool known = items.Knows(row.Name);
        var cands = known ? items.Near(row.Name, 2) : items.Fragment(row.Name, MaxCandidates);
        if (cands.Count == 0 || cands.Count > MaxCandidates) return new Look(line.Text, none, null, cands.Count == 0 ? "no item fits the letters" : "too many items fit the letters");
        var withIcons = cands.Select(c => (c, Key: IconIndex.KeyOf(items.IconUrl(c.Id)))).Where(x => x.Key.Length > 0 && IconIndex.Of(x.Key).Count > 0).ToList();
        if (withIcons.Count == 0) return new Look(line.Text, none, null, "no icon known for the items that fit");
        var text = new Rectangle(line.Box.X - origin.X, line.Box.Y - origin.Y, line.Box.Width, line.Box.Height);
        var crops = Crops(text, new Rectangle(0, 0, raw.Width, raw.Height)).ToList();
        if (crops.Count == 0) return new Look(line.Text, none, null, "no room for an icon left of the text");
        var cells = crops.Select(r => (Cell: IconIndex.Cell(raw, r), Bg: RewardGrid.Background(raw, r))).ToList();
        var scored = withIcons.Select(x => (x.c.Id, x.c.Name, x.Key, Distance: IconIndex.Of(x.Key).Min(e => cells.Min(cl => IconIndex.Distance(cl.Cell, cl.Bg, e))))).ToList();
        var scores = scored.OrderBy(x => x.Distance).Select(x => (x.Name, x.Distance)).ToList();
        var v = Decide(scored, row.Name.Contains("bound", StringComparison.OrdinalIgnoreCase));
        if (v == null) return new Look(line.Text, scores, null, "no icon is clearly the one beside it");
        long current = known ? items.IdOf(row.Name) : 0;
        if (v.Id == current) return new Look(line.Text, scores, v, "the icon agrees");
        return new Look($"{v.Name} x{row.Count}", scores, v, known ? $"the icon says {v.Name}, not {items.NameOf(current)}" : $"the icon names it {v.Name}");
    }
}
