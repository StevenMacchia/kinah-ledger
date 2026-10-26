using System.Drawing;
using KinahLedger.Data;

namespace KinahLedger.Ocr;

/// <summary>
/// The icon at the start of each row of the loot list beside the character, as a second reading of the row's name
/// (2026-10-23). A long name scrolls sideways, so a read often shows only its middle or end ("itstone x1"), and a row
/// read once during fast kills can come out garbled; the icon beside it doesn't move. A row whose text names an item
/// exactly is left as it is. Otherwise the items its letters could be (ItemDictionary.Fragment and Near) are compared by
/// icon, the way the reward table's slots are (IconIndex), and the icon has a verdict only when one icon is clearly the art
/// beside it: closer than <see cref="SureDistance"/> and ahead of the next by <see cref="MinMargin"/>. Items that share
/// an icon can't be told apart by it, so they give no answer. The icon's exact place beside the text isn't measured
/// yet, so a few sizes and gaps are tried and the closest kept; `popup-test --image` prints them for a real picture.
///
/// The text and the icon are two witnesses, and the icon is the noisier one: the list draws its art small, so a sure
/// verdict sits at 60 to 70 where the reward table's are at 20 to 40. Since 2026-10-25 (<see cref="Settle"/>) the text
/// wins when it names a known item closely and clearly ("Accursed or" is Accursed Orb, whatever the icon says: 0.8.39
/// renamed it Accursed Bow about 105 times that day); the icon settles only what the text leaves open (a slice that is
/// part of several names, letters as near one item as another); and an icon that names an item the text is far from
/// leaves the row unsure, never counted on the icon alone.
/// </summary>
public static class PopupIcons
{
    /// <summary>The reward table's own sure distance (TrackerSession.GridSureDistance).</summary>
    public const double SureDistance = 70;
    public const double MinMargin = 12;
    /// <summary>More items than this fit the letters: the read says too little for an icon to settle.</summary>
    public const int MaxCandidates = 40;
    /// <summary>A text cost beyond this (eight plain edits) is no reading of that item at all.</summary>
    const int CostCap = 8 * NameMatch.Unit;

    public sealed record Verdict(long Id, string Name, double Distance, double Next);

    /// <summary>
    /// What the icon said about one row: the row's text to use, the candidates' distances, the verdict if any, and why.
    /// <see cref="By"/> says what decided a row the icon had a verdict on: "text" (the text won over an icon that named
    /// another item), "agree" (the text and the icon named the same item; the row is renamed to it), "icon" (the icon
    /// settled what the text left open), "unsure" (the icon named an item the text is far from: the row isn't counted from
    /// this read), or "" (nothing changed: no verdict, or the text already is the icon's item).
    /// <see cref="Trace"/> is the icon's scores as trace.log keeps them ("id:distance,…", closest first), so a replay
    /// settles the row again with this version's rules.
    /// </summary>
    public sealed record Look(string Text, List<(string Name, double Distance)> Scores, Verdict? Verdict, string Note)
    {
        public string By { get; init; } = "";
        public bool Unsure => By == "unsure";
        public string Trace { get; init; } = "";
    }

    /// <summary>A row the icon may look at: the row as parsed, whether its text is a known item's misread, and the items its letters could be.</summary>
    public sealed record Prep(LootPopup.Row Row, bool Known, List<ItemDictionary.NameCandidate> Cands);

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
        if (best.Items.Select(i => Base(i.Name)).Distinct().Count() != 1) return null;
        var pick = best.Items.OrderBy(i => i.Name.Contains("(bound)", StringComparison.OrdinalIgnoreCase) == bound ? 0 : 1).ThenBy(i => i.Name.Length).First();
        return new Verdict(pick.Id, pick.Name, best.D, next);
    }

    static string Base(string n) => System.Text.RegularExpressions.Regex.Replace(n, @"\s*\(bound\)\s*$", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim().ToLowerInvariant();

    /// <summary>
    /// The row a line reads as and the items its letters could be, or null with why there is nothing for the icon to
    /// look at (a Kinah or soul row, an exact name, no item or too many items fit). Text only: the live read and a replay
    /// of trace.log both start here.
    /// </summary>
    public static (Prep? Prep, string Note) Prepare(ItemDictionary items, string text)
    {
        var rows = LootPopup.Rows(new[] { (0, text) });
        if (rows.Count != 1) return (null, "");
        var row = rows[0];
        if (row.IsKinah || Session.LootCrossCheck.SoulOf(row.Name) != null || items.IsExact(row.Name)) return (null, "");
        bool known = items.Knows(row.Name);
        var cands = known ? items.Near(row.Name, 2) : items.Fragment(row.Name, MaxCandidates);
        if (cands.Count == 0 || cands.Count > MaxCandidates) return (null, cands.Count == 0 ? "no item fits the letters" : "too many items fit the letters");
        return (new Prep(row, known, cands), "");
    }

    /// <summary>
    /// How far a row's text is from an item's name, on NameMatch's scale (tenths of a plain edit, OCR slips cheaper): the
    /// read against the whole name, or against the piece of it the list can show. A read inside the name (a marquee's
    /// slice, "itstone" in "Spiritstone") costs nothing; a row whose count wasn't read (<paramref name="cut"/>: its end
    /// hasn't slid in yet) is matched against the name's start too ("Accursed on" is one slip from "Accursed Or", the
    /// start of "Accursed Orb", and two edits from "Accursed Bow"). Past eight edits: int.MaxValue.
    /// </summary>
    public static int TextCost(string read, string name, bool cut)
    {
        var k = ItemDictionary.Norm(read);
        var n = ItemDictionary.Norm(name);
        if (k.Length == 0 || n.Length == 0) return int.MaxValue;
        if (n.Contains(k, StringComparison.Ordinal)) return 0;
        int best = NameMatch.Distance(k, n, CostCap);
        if (cut)
            for (int len = k.Length - 1; len <= k.Length + 1; len++)
                if (len >= 4 && len < n.Length) best = Math.Min(best, NameMatch.Distance(k, n[..len], CostCap));
        return best;
    }

    /// <summary>A text cost in plain edits, for the log ("1", "0.5", "far").</summary>
    static string Edits(int cost) => cost >= int.MaxValue ? "far" : (cost / (double)NameMatch.Unit).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// The text against the icon, for one row (2026-10-25). Each item the letters could be (and each the icon scored) is
    /// costed by <see cref="TextCost"/>, a tradable and a bound copy as one item. The text names an item closely when its
    /// best is within the budget the item list's own snapping allows (a sixth of the letters, at least one edit: what
    /// ItemDictionary.IdOf takes as that item) and clearly when the next item is a full edit further (the margin
    /// ItemDictionary.Match asks of a context set over the whole list). Then:
    ///   - the text names an item closely and clearly: the text wins. An icon that names another item is logged and set
    ///     aside; the row is named by the text's item (By "text"). An icon that agrees names it as before (By "agree").
    ///   - otherwise the icon settles it when its item is among the text's near ties (less than a full edit behind the
    ///     text's best: every item a slice is part of), or when the text reads as no item at all (past the item list's
    ///     widest slice search, a quarter of the letters): the row is named by the icon (By "icon"), as since 0.8.26.
    ///   - an icon that names an item the text puts a full edit or more behind its best: the two witnesses disagree and
    ///     neither is sure, so the row is unsure (By "unsure"): its text is left as read and this read doesn't vote.
    /// With no sure icon there is nothing to settle and the text stands as read, as before.
    /// </summary>
    public static Look Settle(ItemDictionary items, string text, Prep prep, IReadOnlyList<(long Id, string Name, string IconKey, double Distance)> scored)
    {
        var row = prep.Row;
        bool bound = row.Name.Contains("bound", StringComparison.OrdinalIgnoreCase);
        var scores = scored.OrderBy(x => x.Distance).Select(x => (x.Name, x.Distance)).ToList();
        string trace = string.Join(",", scored.OrderBy(x => x.Distance).Select(x => $"{x.Id}:{x.Distance.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)}"));
        var v = Decide(scored, bound);
        if (v == null) return new Look(text, scores, null, "no icon is clearly the one beside it") { Trace = trace };

        // the text's own reading: each item (bound and tradable copies as one) at its best cost, closest first
        var named = prep.Cands.Select(c => (c.Id, c.Name)).Concat(scored.Select(s => (s.Id, s.Name))).DistinctBy(x => x.Id)
            .Select(x => (x.Id, x.Name, Cost: TextCost(row.Name, x.Name, row.NoCount)))
            .GroupBy(x => Base(x.Name))
            .Select(g => (Base: g.Key, Cost: g.Min(x => x.Cost), Pick: g.OrderBy(x => x.Name.Contains("(bound)", StringComparison.OrdinalIgnoreCase) == bound ? 0 : 1).ThenBy(x => x.Name.Length).First()))
            .OrderBy(x => x.Cost).ThenBy(x => x.Base, StringComparer.Ordinal).ToList();
        var first = named[0];
        int second = named.Count > 1 ? named[1].Cost : int.MaxValue;
        int letters = ItemDictionary.Norm(row.Name).Length;
        // close: what the item list's own snapping takes as that item; an opinion at all: the widest its slice search goes
        int budget = Math.Max(1, letters / 6) * NameMatch.Unit, wide = Math.Max(2, letters / 4) * NameMatch.Unit;
        bool clear = first.Cost <= budget && (second == int.MaxValue || second - first.Cost >= NameMatch.Unit);
        int at = named.FindIndex(x => x.Base == Base(v.Name));
        int iconCost = at >= 0 ? named[at].Cost : int.MaxValue;
        string icon = $"icon {v.Distance:0}, next {(v.Next < 1e6 ? v.Next.ToString("0") : "none")}";
        string next = named.Count > 1 ? $", next {named[1].Pick.Name} {Edits(second)}" : "";

        // named by the icon's item: as read when the text already is that item (the icon agrees), else renamed
        Look Named(string by, string why)
        {
            if (prep.Known && v!.Id == items.IdOf(row.Name)) return new Look(text, scores, v, "the icon agrees") { Trace = trace };
            return new Look($"{v!.Name} x{row.Count}", scores, v, why) { By = by, Trace = trace };
        }
        if (clear)
        {
            if (Base(v.Name) == first.Base) return Named("agree", $"the text and the icon both say {v.Name} ({Edits(first.Cost)} edits{next}; {icon})");
            return new Look($"{first.Pick.Name} x{row.Count}", scores, v,
                $"the text says {first.Pick.Name} ({Edits(first.Cost)} edits{next}), the icon {v.Name} ({icon}): the text wins") { By = "text", Trace = trace };
        }
        // the text reads as no item at all (past its widest search), or as the icon's item about as well as its best
        if (first.Cost > wide || (iconCost < int.MaxValue && iconCost - first.Cost < NameMatch.Unit))
        {
            var ties = named.Where(x => x.Cost - first.Cost < NameMatch.Unit).Select(x => x.Pick.Name).ToList();
            string what = first.Cost > wide ? $"the text reads as no item ({Edits(first.Cost)} edits from {first.Pick.Name})"
                : ties.Count > 1 ? $"the text fits {string.Join(", ", ties.Take(3))}{(ties.Count > 3 ? $" and {ties.Count - 3} more" : "")} as well ({Edits(first.Cost)} edits)"
                : $"the text is nearest {first.Pick.Name}, not surely ({Edits(first.Cost)} edits{next})";
            return Named("icon", $"{what}, the icon says {v.Name} ({icon}): the icon settles it");
        }
        return new Look(text, scores, v,
            $"the text says {first.Pick.Name} ({Edits(first.Cost)} edits{next}), the icon {v.Name} ({Edits(iconCost)} edits from the text; {icon}): unsure, not counted from this read") { By = "unsure", Trace = trace };
    }

    /// <summary>
    /// A row as trace.log kept it (since 2026-10-25): the text as read before the icon looked, and the icon's scores
    /// (<see cref="Look.Trace"/>), settled again with this version's rules (the replay bank). Scores naming no item on the
    /// list are dropped; with none left, the text stands.
    /// </summary>
    public static Look Replay(ItemDictionary items, string text, string trace)
    {
        var none = new List<(string, double)>();
        var (prep, note) = Prepare(items, text);
        if (prep == null) return new Look(text, none, null, note);
        var scored = new List<(long Id, string Name, string IconKey, double Distance)>();
        foreach (var part in trace.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var kv = part.Split(':');
            if (kv.Length != 2 || !long.TryParse(kv[0], out var id) || !double.TryParse(kv[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d)) continue;
            if (items.Get(id) is not { } info) continue;
            scored.Add((id, info.Name, IconIndex.KeyOf(items.IconUrl(id)), d));
        }
        if (scored.Count == 0) return new Look(text, none, null, "no icon scores");
        return Settle(items, text, prep, scored);
    }

    /// <summary>
    /// One row of the list read at <paramref name="line"/> (screen coordinates), with <paramref name="raw"/> the capture
    /// of the list's area whose top-left sits at <paramref name="origin"/> on screen. Returns the text to count the row by:
    /// the row as read, or "Name xN" when the text or the icon named it (<see cref="Settle"/>).
    /// </summary>
    public static Look Name(ItemDictionary items, Bitmap raw, Point origin, OcrLineBox line)
    {
        var none = new List<(string, double)>();
        var (prep, note) = Prepare(items, line.Text);
        if (prep == null) return new Look(line.Text, none, null, note);
        var withIcons = prep.Cands.Select(c => (c, Key: IconIndex.KeyOf(items.IconUrl(c.Id)))).Where(x => x.Key.Length > 0 && IconIndex.Of(x.Key).Count > 0).ToList();
        if (withIcons.Count == 0) return new Look(line.Text, none, null, "no icon known for the items that fit");
        var text = new Rectangle(line.Box.X - origin.X, line.Box.Y - origin.Y, line.Box.Width, line.Box.Height);
        var crops = Crops(text, new Rectangle(0, 0, raw.Width, raw.Height)).ToList();
        if (crops.Count == 0) return new Look(line.Text, none, null, "no room for an icon left of the text");
        var cells = crops.Select(r => (Cell: IconIndex.Cell(raw, r), Bg: RewardGrid.Background(raw, r))).ToList();
        var scored = withIcons.Select(x => (x.c.Id, x.c.Name, x.Key, Distance: IconIndex.Of(x.Key).Min(e => cells.Min(cl => IconIndex.Distance(cl.Cell, cl.Bg, e))))).ToList();
        return Settle(items, line.Text, prep, scored);
    }
}
