using System.Drawing;
using System.Drawing.Imaging;
using KinahLedger.Data;
using KinahLedger.Ocr;

namespace KinahLedger.Session;

/// <summary>
/// The loot list's rows as objects, not as text (brief item 2, 2026-10-24). A row is known by where it sits (its y slot),
/// when it first showed and, when the picture gave one, the hash of its icon; its text is what it is read as, and it may
/// change. Each read is matched to the rows already up: the same text a slot or so away (the list moving up as a row
/// expires), then anything in the same slot that is the same item's family ("Orichalcum Ore" becoming "Fine Orichalcum
/// Ore", a misread fixed), the same icon, or a row still fading in. What is left is a new row.
///
/// A row's text is taken only once it has settled: its contrast (how far the text stands out, measured on the crop) has
/// stopped rising after the fade-in, or it has been read three times. Reads fainter than <see cref="VoteFloor"/> of the
/// row's brightest (fading out, half covered) don't count, and the row's text is the majority of its last three counted
/// reads, the latest winning a tie: a change in place that lasts two reads is the row's final text, one stray read isn't.
/// A row that goes before it settles is unsure: logged, never shown to the cross-check, never counted. With no contrast
/// (an old trace, a test) every read counts and a row is settled at once, as before.
///
/// Kinah and soul rows, and a shop's rows, pass through untouched: they have their own rules in LootCrossCheck.
/// </summary>
public sealed class LootRows
{
    /// <summary>Settled when a read is no more than this much above the one before (the fade-in has stopped).</summary>
    public const double SettleRise = 1.10;
    /// <summary>A read fainter than this share of the row's brightest is fading out or covered: it doesn't vote.</summary>
    public const double VoteFloor = 0.6;
    /// <summary>A row read once is settled only at this share of the usual settled contrast.</summary>
    public const double OneReadFloor = 0.9;
    /// <summary>Below this share of a row's brightest, a read a slot away is a new row fading in, not the row moved.</summary>
    public const double FreshFloor = 0.5;
    /// <summary>A row unseen for two reads and this long has gone; one unseen this long has gone whatever the reads.</summary>
    public const double GoneSeconds = 1.5, LostSeconds = 4;
    /// <summary>A row that went and shows again in its slot within this long, not fading in afresh, is the same row back.</summary>
    public const double BackSeconds = 4;

    sealed class Obj
    {
        public int Id, Top, Misses;
        public DateTime First, Last;
        public ulong Icon;
        public double Peak = -1;
        public bool Settled, Measured;
        public string? Text, Canon;   // the settled text standing for the row, and its item's name
        public readonly List<(string Text, string Canon, double C)> Reads = new();
        public string LastCanon => Reads.Count > 0 ? Reads[^1].Canon : "";
    }

    readonly ItemDictionary _items;
    readonly List<Obj> _live = new();
    readonly List<(Obj Row, DateTime Gone)> _gone = new();
    readonly List<double> _peaks = new();
    int _next = 1, _gap = 30;

    /// <summary>Rows that went before their text settled: logged, never counted.</summary>
    public int Unsure { get; private set; }
    /// <summary>Rows whose settled text changed in place (one item, the final text).</summary>
    public int Changed { get; private set; }

    public LootRows(ItemDictionary items) { _items = items; }

    /// <summary>
    /// Set by the session: whether a gathering bar race finished on Success after a row of this item (its name, lower
    /// case) first showed. Then a row back in its slot is the next gather, not the same row flickering (2026-10-24).
    /// </summary>
    public Func<string, DateTime, bool>? NewGather { get; set; }

    /// <summary>The usual contrast of a settled row on this screen (the median of the last ones), or null before three.</summary>
    public double? Typical
    {
        get
        {
            if (_peaks.Count < 3) return null;
            var s = _peaks.OrderBy(x => x).ToList();
            return s[s.Count / 2];
        }
    }

    /// <summary>Forgets every row without judging it (the list was covered by a window that isn't loot).</summary>
    public void Reset() { _live.Clear(); _gone.Clear(); }

    /// <summary>
    /// One read of the list: the rows to show the cross-check, each item row with its object (Obj, First) and its settled
    /// text; rows not settled yet are held back. Kinah, soul and shop rows come as read.
    /// </summary>
    public List<LootPopup.Row> Feed(List<LootLine> lines, DateTime now)
    {
        // rows gone since the last read leave first, so nothing new is taken for one of them
        Expire(now);
        var outRows = new List<(int Top, LootPopup.Row Row)>();
        var items = new List<(LootLine Line, LootPopup.Row Row, string Canon)>();
        foreach (var l in lines.OrderBy(l => l.Top))
        {
            var row = LootPopup.Rows(new[] { (l.Top, l.Text) }).FirstOrDefault();
            if (row == null) continue;
            if (row.IsKinah || LootCrossCheck.SoulOf(row.Name) != null || LootCrossCheck.IsShopWord(row.Name)
                || System.Text.RegularExpressions.Regex.IsMatch(row.Name, @"^\W*kin[ae]h?\W*(\(bound\))?\W*$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            { outRows.Add((l.Top, row)); continue; }
            items.Add((l, row, Canon(row.Name)));
        }
        // the usual gap between rows, for what "the same slot" and "a slot away" mean
        if (items.Count >= 2)
        {
            var gaps = items.Zip(items.Skip(1), (a, b) => b.Line.Top - a.Line.Top).Where(d => d > 4).OrderBy(d => d).ToList();
            if (gaps.Count > 0) _gap = gaps[gaps.Count / 2];
        }
        int near = Math.Max(6, (int)(_gap * 0.6)), shift = (int)(_gap * 1.5);

        var match = new Obj?[items.Count];
        var taken = new HashSet<Obj>();
        // the same text, the nearest within a slot and a half: the row, perhaps moved up. A read far fainter than the row
        // was, in another slot, is a new row fading in there (two identical drops), never the row moved
        var pairs = new List<(int I, Obj O, int D)>();
        for (int i = 0; i < items.Count; i++)
            foreach (var o in _live)
            {
                int d = Math.Abs(o.Top - items[i].Line.Top);
                if (d > shift || (o.Canon ?? o.LastCanon) != items[i].Canon) continue;
                if (d > near && Fresh(o, items[i].Line)) continue;
                pairs.Add((i, o, d));
            }
        foreach (var (i, o, _) in pairs.OrderBy(p => p.D))
            if (match[i] == null && !taken.Contains(o)) { match[i] = o; taken.Add(o); }
        // then the same slot: a change in place (the same item's family, the same icon, or a row still fading in)
        for (int i = 0; i < items.Count; i++)
        {
            if (match[i] != null) continue;
            var (line, _, canon) = items[i];
            var o = _live.Where(x => !taken.Contains(x) && Math.Abs(x.Top - line.Top) <= near).OrderBy(x => Math.Abs(x.Top - line.Top)).FirstOrDefault();
            if (o == null) continue;
            bool sameIcon = o.Icon != 0 && line.Icon != 0 && Hamming(o.Icon, line.Icon) <= 8;
            bool otherIcon = o.Icon != 0 && line.Icon != 0 && Hamming(o.Icon, line.Icon) >= 20;
            if (!otherIcon && (!o.Settled || sameIcon || Family(o.Canon ?? o.LastCanon, canon))) { match[i] = o; taken.Add(o); }
        }
        for (int i = 0; i < items.Count; i++)
        {
            var (line, row, canon) = items[i];
            var o = match[i] ?? Back(line, canon) ?? new Obj { Id = _next++, First = now };
            if (match[i] == null) { if (!_live.Contains(o)) _live.Add(o); taken.Add(o); }
            o.Top = line.Top; o.Last = now; o.Misses = 0;
            if (line.Icon != 0) o.Icon = line.Icon;
            o.Reads.Add((line.Text, canon, line.Contrast));
            if (o.Reads.Count > 12) o.Reads.RemoveAt(0);
            Judge(o);
            if (o.Settled && o.Text != null && LootPopup.Rows(new[] { (line.Top, o.Text) }).FirstOrDefault() is { } shown)
                outRows.Add((line.Top, shown with { Obj = o.Id, First = o.First }));
        }
        // rows not in this read: gone after two reads and a second and a half; unsure if their text never settled
        foreach (var o in _live.Where(x => !taken.Contains(x))) o.Misses++;
        Expire(now);
        return outRows.OrderBy(r => r.Top).Select(r => r.Row).ToList();
    }

    void Expire(DateTime now)
    {
        foreach (var o in _live.ToList())
        {
            if (!((o.Misses >= 2 && (now - o.Last).TotalSeconds >= GoneSeconds) || (now - o.Last).TotalSeconds > LostSeconds)) continue;
            _live.Remove(o);
            if (!o.Settled)
            {
                Unsure++;
                Accuracy.Add("list_unsure");
                var typical = Typical;
                CrashLog.Note($"loot list: \"{o.Reads[^1].Text}\" at y {o.Top} went before its text settled ({o.Reads.Count} read(s), contrast {o.Peak:0.00}{(typical is double t ? $" of a usual {t:0.00}" : "")}): unsure, not counted");
            }
            else _gone.Add((o, o.Last));
        }
        _gone.RemoveAll(g => (now - g.Gone).TotalSeconds > BackSeconds);
    }

    /// <summary>A row that went a moment ago, back in its slot with the same item and not fading in afresh: the same row (it flickered out of a read or two).</summary>
    Obj? Back(LootLine line, string canon)
    {
        int near = Math.Max(6, (int)(_gap * 0.6));
        var g = _gone.Where(x => Math.Abs(x.Row.Top - line.Top) <= near && (x.Row.Canon ?? x.Row.LastCanon) == canon && !Fresh(x.Row, line))
            .OrderByDescending(x => x.Gone).FirstOrDefault();
        if (g.Row == null) return null;
        // gathering: a gathering bar race finished on Success after the gone row showed, so this is the next gather's row
        if (NewGather?.Invoke(canon, g.Row.First) == true)
        {
            CrashLog.Note($"gathering: \"{line.Text}\" at y {line.Top} is back where a row went a moment ago, and a gathering bar race finished since: a new gather, not the same row");
            return null;
        }
        _gone.Remove(g);
        _live.Add(g.Row);
        return g.Row;
    }

    /// <summary>A read much fainter than the row's settled brightest: a new row fading in (only said when both were measured).</summary>
    static bool Fresh(Obj o, LootLine line) => o.Settled && o.Peak > 0 && line.Contrast >= 0 && line.Contrast < FreshFloor * o.Peak;

    /// <summary>Settles the row when its contrast stops rising, and sets its text to the vote of its last counted reads.</summary>
    void Judge(Obj o)
    {
        var (_, _, c) = o.Reads[^1];
        if (c >= 0) { o.Measured = true; if (c > o.Peak) o.Peak = c; }
        if (!o.Settled)
        {
            int n = o.Reads.Count;
            if (!o.Measured) o.Settled = true;   // nothing measured: as before, the first read stands
            else if (n >= 2 && o.Reads[n - 2].C >= 0 && c <= o.Reads[n - 2].C * SettleRise) o.Settled = true;
            else if (n >= 3) o.Settled = true;
            else if (n == 1 && Typical is double t && c >= OneReadFloor * t) o.Settled = true;
            if (o.Settled && o.Measured) { _peaks.Add(o.Peak); if (_peaks.Count > 30) _peaks.RemoveAt(0); }
        }
        if (!o.Settled) return;
        var counted = o.Reads.Where(r => r.C < 0 || r.C >= VoteFloor * o.Peak).ToList();
        if (counted.Count == 0) return;
        var last = counted.Skip(Math.Max(0, counted.Count - 3)).ToList();
        int best = last.GroupBy(r => r.Canon).Max(g => g.Count());
        // the most votes; of those, the one read last
        var win = last.Last(r => last.Count(x => x.Canon == r.Canon) == best);
        if (o.Canon != null && o.Canon != win.Canon)
        {
            Changed++;
            CrashLog.Note($"loot list: the row at y {o.Top} now reads \"{win.Text}\" (was \"{o.Text}\"): one item, the final text");
        }
        o.Text = win.Text; o.Canon = win.Canon;
    }

    /// <summary>The item list's name for a row's text (case aside), or the text itself when it names no item.</summary>
    string Canon(string name)
    {
        bool was = Accuracy.Muted;
        Accuracy.Muted = true;   // looking a row up again is no new misread
        try { return (_items.Knows(name) ? _items.NameOf(_items.IdOf(name)) : name.Trim()).ToLowerInvariant(); }
        finally { Accuracy.Muted = was; }
    }

    /// <summary>One item's family: one name holds the other ("fine orichalcum ore" holds "orichalcum ore"), or they are a misread apart.</summary>
    static bool Family(string a, string b)
    {
        if (a.Length == 0 || b.Length == 0) return false;
        if (a == b || a.Contains(b) || b.Contains(a)) return true;
        int budget = Math.Max(2, Math.Min(a.Length, b.Length) / 4) * NameMatch.Unit;
        return NameMatch.Distance(a, b, budget) <= budget;
    }

    static int Hamming(ulong a, ulong b) => System.Numerics.BitOperations.PopCount(a ^ b);

    /// <summary>
    /// How settled a line looks and its icon, from the loot list's picture: the contrast is the gap between the brightest
    /// tenth of the line's pixels and their median (0 to 1), which rises while the row fades in; the icon is an 8x8 average
    /// hash of the square left of the text (where PopupIcons looks for it), 0 when it falls outside the picture.
    /// </summary>
    public static (double Contrast, ulong Icon) Measure(Bitmap raw, Point origin, Rectangle box)
    {
        var r = new Rectangle(box.X - origin.X, box.Y - origin.Y, box.Width, box.Height);
        r.Intersect(new Rectangle(0, 0, raw.Width, raw.Height));
        if (r.Width < 4 || r.Height < 4) return (-1, 0);
        int h = r.Height, side = (int)(h * 1.6), gap = (int)(h * 0.3);
        var icon = new Rectangle(r.Left - gap - side, r.Top + h / 2 - side / 2, side, side);
        var whole = Rectangle.Union(r, icon);
        whole.Intersect(new Rectangle(0, 0, raw.Width, raw.Height));
        var data = raw.LockBits(whole, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            int stride = data.Stride;
            var buf = new byte[stride * whole.Height];
            System.Runtime.InteropServices.Marshal.Copy(data.Scan0, buf, 0, buf.Length);
            int Luma(int x, int y) { int i = (y - whole.Top) * stride + (x - whole.Left) * 4; return (buf[i + 2] * 299 + buf[i + 1] * 587 + buf[i] * 114) / 1000; }
            var hist = new int[256]; int count = 0;
            int step = Math.Max(1, Math.Min(r.Width, r.Height) / 24);
            for (int y = r.Top; y < r.Bottom; y += step)
                for (int x = r.Left; x < r.Right; x += step) { hist[Luma(x, y)]++; count++; }
            int Pct(double p) { int target = (int)(count * p), acc = 0; for (int v = 0; v < 256; v++) { acc += hist[v]; if (acc > target) return v; } return 255; }
            double contrast = Math.Max(0, Pct(0.9) - Pct(0.5)) / 255.0;
            ulong hash = 0;
            if (whole.Contains(icon))
            {
                var cells = new double[64];
                for (int cy = 0; cy < 8; cy++)
                    for (int cx = 0; cx < 8; cx++)
                    {
                        int x0 = icon.Left + cx * side / 8, y0 = icon.Top + cy * side / 8, x1 = icon.Left + (cx + 1) * side / 8, y1 = icon.Top + (cy + 1) * side / 8;
                        double s = 0; int k = 0;
                        for (int y = y0; y < Math.Max(y1, y0 + 1); y++) for (int x = x0; x < Math.Max(x1, x0 + 1); x++) { s += Luma(x, y); k++; }
                        cells[cy * 8 + cx] = s / Math.Max(1, k);
                    }
                double mean = cells.Average();
                for (int i = 0; i < 64; i++) if (cells[i] > mean) hash |= 1UL << i;
                if (hash == 0) hash = 1;   // a flat square still says "measured"
            }
            return (contrast, hash);
        }
        finally { raw.UnlockBits(data); }
    }
}
