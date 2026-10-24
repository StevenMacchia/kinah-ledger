using System.Drawing;
using System.Drawing.Imaging;
using System.Text.RegularExpressions;
using KinahLedger.Data;
using KinahLedger.Ocr;

namespace KinahLedger.Session;

/// <summary>
/// The loot list's rows as objects, not as text (brief item 2, 2026-10-24; reworked from 10 fps footage the same day).
/// What the footage of Steven's 5120x2160 screen showed, and this follows:
///
///   - Rows stack from the bottom. A new row takes the bottom slot and pushes every older row up one slot (about 73 px
///     native) within 0.2 to 0.3 s; up to five rows show at once. A row that expires leaves the rows below it where they
///     are: nothing ever moves down. So a row's y is not who it is: a row is known by its place in the stack (matched to
///     this read's lines in order from the bottom, allowing the upward shift a new row below it causes), when it was
///     first seen, its icon's hash and its name's family.
///   - A real row is up 2.5 s at full contrast (about three reads), fades in within a frame, slides in from the right
///     (its end cut off meanwhile), fades out in one frame, and never goes sooner. An object read fewer than
///     <see cref="MinRowReads"/> times, with no icon, between a read without it and the next read without it under <see cref="RowSeconds"/> apart, is not a row (by time, not reads: a pass can take over a second); one on screen
///     over <see cref="MaxRowSeconds"/> is not one either (the player's title "Vanguard of Atreia" sits in the band for
///     minutes, the "F Pick up" prompt for seconds). Both are logged once and never counted. Behind a full-screen window
///     a row's timer stops: rows the list showed before it was covered (<see cref="Reset"/>) are the same rows when they
///     show again within <see cref="ParkedSeconds"/>.
///   - A long name scrolls like a marquee for the row's whole life, so each read is another slice of it ("l: Starturtle
///     (Bound) x1", "turtle (Bound) x1"). A slice is never "the final text": slices of one name stand for the longest read
///     of it, and with an icon beside it a slice that is part of exactly one item's name names that item.
///
/// An item row's text is taken once it has settled: its contrast (how far the text stands out, measured on the crop) has
/// stopped rising after the fade-in, or it has been read three times, or (with an icon beside it) a read names exactly one
/// item. Reads fainter than <see cref="VoteFloor"/> of the row's brightest (fading out, half covered) don't count, and the
/// row's text is the majority of its last three counted reads, the latest winning a tie: a change in place that lasts two
/// reads is the row's final text ("Orichalcum Ore" becoming "Fine Orichalcum Ore"), one stray read isn't. A row that goes
/// before it settles is unsure: logged, never shown to the cross-check, never counted. With no contrast (an old trace, a
/// test) every read counts and a row is settled at once, as before.
///
/// Kinah rows pass through as read (two reads agreeing is their rule in LootCrossCheck); they are tracked only so that the
/// stack's shifts are seen. A soul row passes as "Soul: Kind (Bound)" on every read, its marquee slices included, so one
/// row is one soul. A shop's row passes through untouched.
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
    /// <summary>A real row's life at full contrast (the footage: 2.4 s, then one faded frame).</summary>
    public const double RowSeconds = 2.5;
    /// <summary>An object read fewer times than this with no icon, up under <see cref="RowSeconds"/> by the reads around it: not a row. (MinRowSeconds: the old bound, kept for reference.)</summary>
    public const int MinRowReads = 2;
    public const double MinRowSeconds = 1.2;
    /// <summary>On screen longer than this (time behind a full-screen window aside): not a row.</summary>
    public const double MaxRowSeconds = 6;
    /// <summary>The most slots a row can move up between two reads (up to five rows show at once).</summary>
    public const int MaxShift = 4;
    /// <summary>A row the list showed before it was covered, showing again within this long: the same row.</summary>
    public const double ParkedSeconds = 12;
    /// <summary>A slice of a long name names an item only with this many letters or more.</summary>
    public const int SliceLetters = 6;
    /// <summary>"Not a row" lines are logged once per text in this long.</summary>
    public const double NoteEverySeconds = 60;

    enum Kind { Item, Kinah, Soul }

    /// <summary>One read of an item row: its text, its item's name (lower case) or the text, its contrast, whether the text names no item as it stands (a slice, a misread, junk), whether it is part of exactly one item's name (with an icon beside it), and that item's own name.</summary>
    readonly record struct Read(string Text, string Canon, double C, bool Slice, bool Unique, string? Name);

    sealed class Obj
    {
        public int Id, Order, Top, Misses, Seen;
        public Kind Kind;
        public DateTime First, Last;
        public DateTime Before = DateTime.MinValue;   // the read before it first showed (it wasn't there)
        public DateTime? MissAt;                      // the first read it was missing from since it was last seen
        public bool Iconed;                           // an icon beside it on some read
        public double Hidden;           // seconds it was covered (a full-screen window), which don't count to its time up
        public ulong Icon;
        public double Peak = -1;
        public bool Settled, Measured, Shown, Dropped;
        public string? Text, Canon, Name;   // the settled text standing for the row, its item (lower case), the item's own name
        public string Soul = "";        // a soul row's enemy kind
        public long Amount;             // a Kinah row's amount
        public readonly List<Read> Reads = new();
        public string LastCanon => Reads.Count > 0 ? Reads[^1].Canon : "";
        public string Key => Canon ?? LastCanon;
        public double Visible(DateTime now) => (now - First).TotalSeconds - Hidden;
    }

    /// <summary>One line of this read, parsed: Kinah, soul (with its kind) or item (with what <see cref="Look"/> made of it).</summary>
    sealed record Cur(LootLine Line, LootPopup.Row Row, Kind Kind, string Soul, string Canon, bool Slice, bool Unique, string? Name);

    readonly ItemDictionary _items;
    readonly List<Obj> _live = new();
    readonly List<(Obj Row, DateTime Gone)> _gone = new();
    readonly List<Obj> _parked = new();
    readonly List<double> _peaks = new();
    readonly Dictionary<string, string?> _sliceCache = new(StringComparer.Ordinal);
    readonly Dictionary<string, bool> _knownCache = new(StringComparer.Ordinal);
    readonly Dictionary<string, DateTime> _noted = new(StringComparer.Ordinal);
    int _next = 1, _order = 1, _gap = 30, _near = 18;
    DateTime _now, _lastFeed = DateTime.MinValue;

    /// <summary>Rows that went before their text settled: logged, never counted.</summary>
    public int Unsure { get; private set; }
    /// <summary>Rows whose settled text changed in place (one item, the final text).</summary>
    public int Changed { get; private set; }
    /// <summary>Objects read once and gone (not rows: a real row is up 2.5 s), and objects up over <see cref="MaxRowSeconds"/> (not rows either).</summary>
    public int ShortLived { get; private set; }
    public int LongLived { get; private set; }
    /// <summary>Rows the list showed before it was covered and showed again after: the same rows.</summary>
    public int Resumed { get; private set; }
    /// <summary>Rows that moved up the stack (a new row came in below them) and were kept as the same rows.</summary>
    public int Shifted { get; private set; }

    public LootRows(ItemDictionary items) { _items = items; }

    /// <summary>
    /// Set by the session: whether a gathering bar race finished on Success after a row of this item (its name, lower
    /// case) first showed. Then a row back in its slot is the next gather, not the same row flickering (2026-10-24).
    /// </summary>
    public Func<string, DateTime, bool>? NewGather { get; set; }

    /// <summary>
    /// Set by the session: a row this already showed the cross-check turned out not to be a row (read once and gone, or up
    /// too long): its object and why. The cross-check stops waiting for it.
    /// </summary>
    public Action<int, string>? NotARow { get; set; }

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

    /// <summary>
    /// The list was covered (a window that isn't loot, the crafting panel, a scene the list isn't read in): the rows up
    /// are set aside, not judged. One that shows again within <see cref="ParkedSeconds"/> is the same row (behind a
    /// full-screen window a row's timer stops: 2026-10-24 footage, Kina x384 back 6 s after it came).
    /// </summary>
    public void Reset()
    {
        foreach (var o in _live) if (!_parked.Contains(o)) _parked.Add(o);
        if (_parked.Count > 20) _parked.RemoveRange(0, _parked.Count - 20);
        _live.Clear(); _gone.Clear();
    }

    /// <summary>
    /// One read of the list: the rows to show the cross-check, each item row with its object (Obj, First) and its settled
    /// text; rows not settled yet are held back. Kinah and shop rows come as read; soul rows as "Soul: Kind (Bound)".
    /// </summary>
    public List<LootPopup.Row> Feed(List<LootLine> lines, DateTime now)
    {
        _now = now;
        // rows gone since the last read leave first, so nothing new is taken for one of them
        Expire(now);
        var outRows = new List<(int Top, LootPopup.Row Row)>();
        var cur = new List<Cur>();
        foreach (var l in lines.OrderBy(l => l.Top))
        {
            var row = LootPopup.Rows(new[] { (l.Top, l.Text) }).FirstOrDefault();
            if (row == null) continue;
            // a shop's word: the cross-check sets the whole read aside
            if (!row.IsKinah && LootCrossCheck.IsShopWord(row.Name)) { outRows.Add((l.Top, row)); continue; }
            bool kina = row.IsKinah || KinaWord(row.Name);
            var soul = kina ? null : LootCrossCheck.SoulOf(row.Name);
            if (kina) cur.Add(new Cur(l, row, Kind.Kinah, "", "", false, false, null));
            else if (soul != null) cur.Add(new Cur(l, row, Kind.Soul, soul, "", false, false, null));
            else { var (canon, slice, unique, name) = Look(row.Name, l.Icon); cur.Add(new Cur(l, row, Kind.Item, "", canon, slice, unique, name)); }
        }
        // the usual gap between rows, for what "the same slot" and "a slot up" mean
        if (cur.Count >= 2)
        {
            var gaps = cur.Zip(cur.Skip(1), (a, b) => b.Line.Top - a.Line.Top).Where(d => d > 4).OrderBy(d => d).ToList();
            if (gaps.Count > 0) _gap = gaps[gaps.Count / 2];
        }
        _near = Math.Max(6, (int)(_gap * 0.6));

        var match = Align(cur);
        var taken = new HashSet<Obj>();
        // top to bottom: of two new rows the upper one came first
        for (int i = 0; i < cur.Count; i++)
        {
            var c = cur[i];
            var o = match[i];
            if (o == null)
            {
                o = (c.Kind == Kind.Item ? Back(c) : null) ?? Unpark(c, cur.Count - 1 - i, now);
                if (o == null) { o = new Obj { Id = _next++, Order = _order++, First = now, Kind = c.Kind, Before = _lastFeed }; _live.Add(o); }
            }
            else if (c.Line.Contrast >= 0 && o.Top - c.Line.Top > _near) Shifted++;
            taken.Add(o);
            Take(o, c, now);
            if (Emit(o, c, now) is { } shown) outRows.Add((c.Line.Top, shown));
        }
        // rows not in this read: gone after two reads and a second and a half; unsure if their text never settled
        foreach (var o in _live.Where(x => !taken.Contains(x))) { if (o.Misses++ == 0) o.MissAt = now; }
        Expire(now);
        _lastFeed = now;
        return outRows.OrderBy(r => r.Top).Select(r => r.Row).ToList();
    }

    /// <summary>
    /// This read's lines matched to the rows up, as a stack: both in order from the bottom, a row matching a line at its
    /// own slot or up to <see cref="MaxShift"/> slots higher (a new row came in below it), never lower. Each pairing costs
    /// what makes it less likely (a shift, a shift with no line below to cause it, text that is only the same family); a
    /// row not in the read and a line no row matches cost too, a new line above a matched one most
    /// (new rows come in at the bottom). The cheapest whole alignment wins. Small: at most a handful of each.
    /// </summary>
    Obj?[] Align(List<Cur> cur)
    {
        var objs = _live.OrderByDescending(o => o.Top).ThenByDescending(o => o.Order).ToList();
        int n = objs.Count, m = cur.Count;
        var result = new Obj?[m];
        if (n == 0 || m == 0) return result;
        // line j counted from the bottom is cur[m - 1 - j]; lines below it: j
        const double MissRow = 1.0, NewBottom = 0.8, NewAbove = 2.5;
        var cost = new double?[n, m];
        for (int i = 0; i < n; i++) for (int j = 0; j < m; j++) cost[i, j] = Cost(objs[i], cur[m - 1 - j], j);
        var f = new double[n + 1, m + 1, 2];
        var pick = new byte[n + 1, m + 1, 2];
        for (int i = n; i >= 0; i--)
            for (int j = m; j >= 0; j--)
                for (int a = 0; a < 2; a++)
                {
                    if (i == n && j == m) { f[i, j, a] = 0; continue; }
                    double best = double.MaxValue; byte how = 0;
                    if (i < n && j < m && cost[i, j] is double c && c + f[i + 1, j + 1, 1] < best) { best = c + f[i + 1, j + 1, 1]; how = 3; }
                    if (i < n && MissRow + f[i + 1, j, a] < best) { best = MissRow + f[i + 1, j, a]; how = 1; }
                    if (j < m && (a == 1 ? NewAbove : NewBottom) + f[i, j + 1, a] < best) { best = (a == 1 ? NewAbove : NewBottom) + f[i, j + 1, a]; how = 2; }
                    f[i, j, a] = best; pick[i, j, a] = how;
                }
        for (int i = 0, j = 0, a = 0; i < n || j < m;)
        {
            switch (pick[i, j, a])
            {
                case 3: result[m - 1 - j] = objs[i]; i++; j++; a = 1; break;
                case 1: i++; break;
                default: j++; break;
            }
        }
        return result;
    }

    /// <summary>What pairing the row <paramref name="o"/> with the line <paramref name="c"/> costs, or null when it can't be that row.</summary>
    double? Cost(Obj o, Cur c, int below)
    {
        int d = o.Top - c.Line.Top;   // above zero: the line is higher than the row was
        int slots = (int)Math.Round(d / (double)_gap);
        if (c.Line.Contrast < 0 && !o.Measured)
        {
            // a trace without the picture's measures: its rows were placed by their order in the read, so a row may seem to
            // move either way; as before, a slot and a half either way
            if (Math.Abs(d) > (int)(_gap * 1.5)) return null;
            slots = Math.Abs(slots);
        }
        else
        {
            if (d < -_near) return null;   // rows never move down
            if (slots < 0) slots = 0;
            if (slots > MaxShift || Math.Abs(d - slots * _gap) > _near) return null;
        }
        bool sameIcon = o.Icon > 1 && c.Line.Icon > 1 && Hamming(o.Icon, c.Line.Icon) <= 8;
        // two icons that far apart are two rows, except the row's own item read again in its own slot within its life: a
        // fading or half-covered read's hash moves ("Sapphire Gemstone x1" at y 1197, then "•Sapphire Gemstohé x1" at 1194
        // 1.3 s later, hashes 33 bits apart: split into two one-read objects and both refused, 2026-10-24 13:11:33)
        if (o.Icon > 1 && c.Line.Icon > 1 && Hamming(o.Icon, c.Line.Icon) >= 20 && !SameRowAgain(o, c, slots)) return null;
        // far fainter than the row was, in another slot: a new row fading in there, never the row moved
        if (slots >= 1 && Fresh(o, c.Line)) return null;
        if (Text(o, c, slots, sameIcon) is not double t) return null;
        return t + 0.4 * slots + (slots > below ? 0.8 : 0);
    }

    /// <summary>The row's own item (an item's name, or a misread of it) read again in the same slot while the row is still up.</summary>
    bool SameRowAgain(Obj o, Cur c, int slots) =>
        slots == 0 && o.Kind == Kind.Item && c.Kind == Kind.Item && o.Visible(_now) < RowSeconds && Known(o.Key) && (o.Key == c.Canon || Family(o.Key, c.Canon));

    /// <summary>How well the line's text fits the row's (0 the same; null: another row).</summary>
    double? Text(Obj o, Cur c, int slots, bool sameIcon)
    {
        switch (o.Kind, c.Kind)
        {
            case (Kind.Kinah, Kind.Kinah):
                if (!c.Row.IsKinah || o.Amount == 0) return 0.5;   // "Kina x1": the amount not read
                if (o.Amount == c.Row.Amount) return 0;
                return OneDigitApart(o.Amount, c.Row.Amount) ? 0.5 : null;
            case (Kind.Soul, Kind.Soul):
            {
                string a = o.Soul.ToLowerInvariant(), b = c.Soul.ToLowerInvariant();
                if (a.Length == 0 || b.Length == 0 || a == b) return 0;
                return a.Contains(b) || b.Contains(a) ? 0.3 : null;
            }
            case (Kind.Soul, Kind.Item):
                return o.Soul.Length >= 3 && SliceOf(c.Canon, SoulText(o.Soul)) ? 0.4 : null;
            case (Kind.Item, Kind.Soul):
                return c.Soul.Length >= 3 && o.Reads.Count > 0 && o.Reads.All(r => SliceOf(r.Canon, SoulText(c.Soul))) ? 0.4 : null;
            case (Kind.Item, Kind.Item):
            {
                var k = o.Key;
                if (k == c.Canon) return 0;
                if (sameIcon) return 0.3;
                if (Family(k, c.Canon)) return 0.5;
                if (slots == 0 && !o.Settled) return 0.7;   // still fading in: its text isn't sure yet
                // text that names no item, in the same place, starting alike ("Vang(", "Vangi", "Vangt": the player's title)
                if (slots == 0 && !Known(k) && !Known(c.Canon) && SamePrefix(k, c.Canon)) return 0.6;
                return null;
            }
        }
        return null;
    }

    /// <summary>One line taken as the row's read.</summary>
    void Take(Obj o, Cur c, DateTime now)
    {
        o.Top = c.Line.Top; o.Last = now; o.Misses = 0; o.Seen++; o.MissAt = null;
        if (c.Line.Spread >= LootNotRow.VouchSpread) o.Iconed = true;
        if (c.Line.Icon != 0) o.Icon = c.Line.Icon;
        switch (c.Kind)
        {
            case Kind.Kinah:
                if (c.Row.IsKinah) o.Amount = c.Row.Amount;
                break;
            case Kind.Soul:
                if (o.Kind == Kind.Item) o.Kind = Kind.Soul;   // its slices before were a soul's name scrolling
                if (c.Soul.Length > o.Soul.Length) o.Soul = c.Soul;
                break;
            default:
                if (o.Kind != Kind.Item) break;   // a soul row's slice: the soul it is stands
                if (o.Dropped) break;   // not a row: its reads are taken so they make no new row, and nothing more
                o.Reads.Add(new Read(c.Line.Text, c.Canon, c.Line.Contrast, c.Slice, c.Unique, c.Name));
                if (o.Reads.Count > 12) o.Reads.RemoveAt(0);
                Judge(o);
                break;
        }
    }

    /// <summary>What the cross-check is shown for the row in this read, or null (not settled, or not a row).</summary>
    LootPopup.Row? Emit(Obj o, Cur c, DateTime now)
    {
        if (o.Dropped) return null;
        if (o.Kind != Kind.Kinah && o.Visible(now) > MaxRowSeconds)
        {
            o.Dropped = true; LongLived++;
            Accuracy.Add("list_notrow");
            NoteNotRow(c.Line.Text, $"loot list: \"{c.Line.Text.Trim()}\" at y {o.Top} has been up {o.Visible(now):0.0} s: a real row is up {RowSeconds:0.0} s, so it isn't one (the player's title, a prompt); ignored", now);
            if (o.Shown) NotARow?.Invoke(o.Id, $"up over {MaxRowSeconds:0} s");
            return null;
        }
        switch (o.Kind)
        {
            case Kind.Kinah: return c.Row;   // as read: two reads agreeing is the cross-check's rule for Kinah
            case Kind.Soul:
                return o.Soul.Length >= 3 ? new LootPopup.Row(c.Row.Text, $"Soul: {o.Soul} (Bound)", 0, 1, false)
                     : c.Kind == Kind.Soul ? c.Row : null;
        }
        if (!o.Settled || o.Text == null || LootPopup.Rows(new[] { (c.Line.Top, o.Text) }).FirstOrDefault() is not { } shown) return null;
        // the winning read is a slice of one item's name: the row is that item, its count from the read
        if (o.Name != null && !Known(shown.Name.Trim().ToLowerInvariant())) shown = shown with { Name = o.Name };
        o.Shown = true;
        return shown with { Obj = o.Id, First = o.First };
    }

    void Expire(DateTime now)
    {
        foreach (var o in _live.ToList())
        {
            if (!((o.Misses >= 2 && (now - o.Last).TotalSeconds >= GoneSeconds) || (now - o.Last).TotalSeconds > LostSeconds)) continue;
            _live.Remove(o);
            if (o.Kind != Kind.Item || o.Dropped) continue;
            // read once and gone: a real row is up 2.5 s, about three reads (only said when the picture was measured)
            // by time, not by reads (a pass can take over a second, so a real row can be read once): only when the read before
            // it showed and the first read it was missing from are closer than a real row's life can it not have been one;
            // and never a row with an icon beside it
            bool brief = o.Measured && o.Seen < MinRowReads && !o.Iconed && o.MissAt is DateTime miss && o.Before != DateTime.MinValue
                && (miss - o.Before).TotalSeconds - o.Hidden < RowSeconds;
            if (!o.Settled)
            {
                Unsure++;
                Accuracy.Add("list_unsure");
                var typical = Typical;
                CrashLog.Note($"loot list: \"{o.Reads[^1].Text}\" at y {o.Top} went before its text settled ({o.Reads.Count} read(s), contrast {o.Peak:0.00}{(typical is double t ? $" of a usual {t:0.00}" : "")}): unsure, not counted");
            }
            else if (brief)
            {
                ShortLived++;
                Accuracy.Add("list_notrow");
                NoteNotRow(o.Reads[^1].Text, $"loot list: \"{o.Reads[^1].Text.Trim()}\" at y {o.Top} was read {o.Seen} time(s) and went: a real row is up {RowSeconds:0.0} s, so it isn't one; ignored", now);
                if (o.Shown) NotARow?.Invoke(o.Id, "read once and gone");
            }
            else _gone.Add((o, o.Last));
        }
        _gone.RemoveAll(g => (now - g.Gone).TotalSeconds > BackSeconds);
        _parked.RemoveAll(p => (now - p.Last).TotalSeconds > ParkedSeconds || (p.Last - now).TotalSeconds > 5);
    }

    void NoteNotRow(string text, string note, DateTime now)
    {
        var key = Regex.Replace(text.ToLowerInvariant(), @"[^a-z0-9]+", "");
        if (_noted.TryGetValue(key, out var at) && (now - at).TotalSeconds < NoteEverySeconds && (at - now).TotalSeconds < 5) return;
        _noted[key] = now;
        if (_noted.Count > 200) foreach (var old in _noted.Where(kv => (now - kv.Value).TotalSeconds > NoteEverySeconds).Select(kv => kv.Key).ToList()) _noted.Remove(old);
        CrashLog.Note(note);
    }

    /// <summary>A row that went a moment ago, back in its slot with the same item and not fading in afresh: the same row (it flickered out of a read or two).</summary>
    Obj? Back(Cur c)
    {
        var g = _gone.Where(x => Math.Abs(x.Row.Top - c.Line.Top) <= _near && x.Row.Key == c.Canon && !Fresh(x.Row, c.Line))
            .OrderByDescending(x => x.Gone).FirstOrDefault();
        if (g.Row == null) return null;
        // gathering: a gathering bar race finished on Success after the gone row showed, so this is the next gather's row
        if (NewGather?.Invoke(c.Canon, g.Row.First) == true)
        {
            CrashLog.Note($"gathering: \"{c.Line.Text}\" at y {c.Line.Top} is back where a row went a moment ago, and a gathering bar race finished since: a new gather, not the same row");
            return null;
        }
        _gone.Remove(g);
        _live.Add(g.Row);
        return g.Row;
    }

    /// <summary>A row the list showed before it was covered, showing again: the same row, its time covered not counted.</summary>
    Obj? Unpark(Cur c, int below, DateTime now)
    {
        Obj? best = null; double bestCost = double.MaxValue;
        foreach (var p in _parked)
        {
            if ((now - p.Last).TotalSeconds > ParkedSeconds || p.Last > now) continue;
            if (Cost(p, c, below) is double v && v < bestCost) { bestCost = v; best = p; }
        }
        if (best == null) return null;
        double gap = (now - best.Last).TotalSeconds;
        best.Hidden += gap;
        _parked.Remove(best);
        _live.Add(best);
        Resumed++;
        CrashLog.Note($"loot list: \"{c.Line.Text.Trim()}\" at y {c.Line.Top} is the row the list showed before it was covered {gap:0.0} s ago: the same row, not a new pickup");
        return best;
    }

    /// <summary>A read much fainter than the row's settled brightest: a new row fading in (only said when both were measured).</summary>
    static bool Fresh(Obj o, LootLine line) => o.Settled && o.Peak > 0 && line.Contrast >= 0 && line.Contrast < FreshFloor * o.Peak;

    /// <summary>
    /// Settles the row when its contrast stops rising (or, with an icon, when a read names exactly one item), and sets its
    /// text to the vote of its last counted reads. A slice of a longer read of the same row stands for that read.
    /// </summary>
    void Judge(Obj o)
    {
        var (_, _, c, _, _, _) = o.Reads[^1];
        if (c >= 0) { o.Measured = true; if (c > o.Peak) o.Peak = c; }
        if (!o.Settled)
        {
            int n = o.Reads.Count;
            bool byContrast = false;
            if (!o.Measured) o.Settled = true;   // nothing measured: as before, the first read stands
            else if (n >= 2 && o.Reads[n - 2].C >= 0 && c <= o.Reads[n - 2].C * SettleRise) byContrast = true;
            else if (n >= 3) byContrast = true;
            else if (n == 1 && Typical is double t && c >= OneReadFloor * t) byContrast = true;
            if (byContrast) { o.Settled = true; _peaks.Add(o.Peak); if (_peaks.Count > 30) _peaks.RemoveAt(0); }
            // the icon beside it and a read that is part of exactly one item's name: which item it is is settled
            else if (o.Icon > 1 && o.Reads.Any(r => r.Unique)) o.Settled = true;
        }
        if (!o.Settled) return;
        var counted = o.Reads.Where(r => r.C < 0 || r.C >= VoteFloor * o.Peak).ToList();
        if (counted.Count == 0) return;
        var last = counted.Skip(Math.Max(0, counted.Count - 3)).ToList();
        // a marquee's slice ("turtle (bound)") stands for the longest read of the row it is part of ("l: starturtle (bound)")
        string KeyOf(Read r) => !r.Slice || r.Unique ? r.Canon
            : o.Reads.Where(y => y.Canon.Length > r.Canon.Length && SliceOf(r.Canon, y.Canon)).OrderByDescending(y => y.Canon.Length).Select(y => y.Canon).FirstOrDefault() ?? r.Canon;
        var keyed = last.Select(r => (R: r, K: KeyOf(r))).ToList();
        int best = keyed.GroupBy(x => x.K).Max(g => g.Count());
        // the most votes; of those, the one read last
        var win = keyed.Last(x => keyed.Count(y => y.K == x.K) == best);
        int at = o.Reads.FindLastIndex(r => r.Canon == win.K);
        var read = at >= 0 ? o.Reads[at] : win.R;
        // a change between two texts that name no item (the player's title read "Vangi", then "Vangt") is no change of item
        if (o.Canon != null && o.Canon != win.K && (Known(o.Canon) || Known(win.K)))
        {
            Changed++;
            CrashLog.Note($"loot list: the row at y {o.Top} now reads \"{read.Text}\" (was \"{o.Text}\"): one item, the final text");
        }
        o.Text = read.Text; o.Canon = win.K; o.Name = read.Name;
    }

    /// <summary>
    /// What a row's name is: the item list's name for it (lower case), or, when it names no item as it stands, with an icon
    /// beside it the one item whose name it is part of (a marquee's slice), else the text itself.
    /// </summary>
    (string Canon, bool Slice, bool Unique, string? Name) Look(string name, ulong icon)
    {
        bool was = Accuracy.Muted;
        Accuracy.Muted = true;   // looking a row up again is no new misread
        try
        {
            if (_items.Knows(name))
            {
                var own = _items.NameOf(_items.IdOf(name));
                bool unique = icon > 1 && SliceItem(name) is { } u && Base(u) == Base(own);
                return (own.ToLowerInvariant(), false, unique, own);
            }
            if (icon > 1 && SliceItem(name) is { } item) return (item.ToLowerInvariant(), true, true, item);
            return (name.Trim().ToLowerInvariant(), true, false, null);
        }
        finally { Accuracy.Muted = was; }
    }

    /// <summary>The one item (as the list names it) whose name holds this text, or null when none or several do. Cached per text.</summary>
    string? SliceItem(string name)
    {
        var core = ItemDictionary.Norm(name);
        if (core.Count(char.IsLetter) < SliceLetters) return null;
        if (_sliceCache.TryGetValue(core, out var hit)) return hit;
        var holders = _items.Fragment(name, 40).Where(x => ItemDictionary.Norm(x.Name).Contains(core, StringComparison.Ordinal)).ToList();
        string? found = null;
        if (holders.Count > 0 && holders.Count <= 40 && holders.Select(x => Base(x.Name)).Distinct().Count() == 1)
        {
            bool bound = name.Contains("bound", StringComparison.OrdinalIgnoreCase);
            found = holders.OrderBy(x => x.Name.Contains("(bound)", StringComparison.OrdinalIgnoreCase) == bound ? 0 : 1).ThenBy(x => x.Name.Length).First().Name;
        }
        if (_sliceCache.Count > 2000) _sliceCache.Clear();
        _sliceCache[core] = found;
        return found;
    }

    /// <summary>Whether a row's name (lower case) is an item's. Cached.</summary>
    bool Known(string canon)
    {
        if (_knownCache.TryGetValue(canon, out var k)) return k;
        bool was = Accuracy.Muted;
        Accuracy.Muted = true;
        try { k = _items.Knows(canon); }
        finally { Accuracy.Muted = was; }
        if (_knownCache.Count > 2000) _knownCache.Clear();
        return _knownCache[canon] = k;
    }

    static string Base(string n) => Regex.Replace(n, @"\s*\(bound\)\s*$", "", RegexOptions.IgnoreCase).Trim().ToLowerInvariant();
    static string SoulText(string kind) => $"soul: {kind.ToLowerInvariant()} (bound)";
    static bool KinaWord(string name) => Regex.IsMatch(name, @"^\W*kin[ae]h?\W*(\(bound\))?\W*$", RegexOptions.IgnoreCase);
    static string Letters(string s) => Regex.Replace(Regex.Replace(s.ToLowerInvariant(), @"[^a-z0-9():]+", " "), @"\s+", " ").Trim();

    /// <summary>The text (four letters or more) is part of the name: a marquee's slice of it.</summary>
    static bool SliceOf(string text, string name)
    {
        var a = Letters(text);
        return a.Count(char.IsLetter) >= 4 && Letters(name).Contains(a, StringComparison.Ordinal);
    }

    /// <summary>Two texts whose first three letters are the same, marks before them aside.</summary>
    static bool SamePrefix(string a, string b)
    {
        static string P(string s) { var l = new string(s.Where(char.IsLetter).Take(3).ToArray()).ToLowerInvariant(); return l.Length == 3 ? l : ""; }
        var x = P(a);
        return x.Length == 3 && x == P(b);
    }

    /// <summary>Two Kinah amounts of three digits or more one digit apart: a misread digit.</summary>
    static bool OneDigitApart(long a, long b)
    {
        string x = a.ToString(), y = b.ToString();
        if (x.Length != y.Length || x.Length < 3) return false;
        int diff = 0; for (int i = 0; i < x.Length; i++) if (x[i] != y[i]) diff++;
        return diff == 1;
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
        var m = MeasureAll(raw, origin, box);
        return (m.Contrast, m.Icon);
    }

    /// <summary>
    /// <see cref="Measure"/>, plus what tells a row from text that isn't one (LootNotRow, 2026-10-24): the line's
    /// background (the darker 30% of its box's pixels, which is what is behind the letters: the list's dark strip under a
    /// row, the world under a nameplate) and the spread of the place left of it where an icon would be. The icon's exact
    /// place isn't measured on real frames yet (PopupIcons tries sizes 1.3 to 2.4 line heights at gaps of 0.15 to 0.8),
    /// so the spread covers all of them at once: the standard deviation of a 12x8 grid of cell means over the box from
    /// 3.2 line heights to 0.15 left of the text, 2.4 tall. Any icon anywhere in it makes it large; it is about 0 only
    /// when the whole place is flat. -1 when less than 1.2 line heights of that place are inside the picture.
    /// </summary>
    public static (double Contrast, ulong Icon, double Background, double Spread) MeasureAll(Bitmap raw, Point origin, Rectangle box)
    {
        var r = new Rectangle(box.X - origin.X, box.Y - origin.Y, box.Width, box.Height);
        r.Intersect(new Rectangle(0, 0, raw.Width, raw.Height));
        if (r.Width < 4 || r.Height < 4) return (-1, 0, -1, -1);
        int h = r.Height, side = (int)(h * 1.6), gap = (int)(h * 0.3);
        var icon = new Rectangle(r.Left - gap - side, r.Top + h / 2 - side / 2, side, side);
        int sh = (int)(h * 2.4);
        var place = new Rectangle(r.Left - (int)(h * 3.2), r.Top + h / 2 - sh / 2, (int)(h * 3.2) - (int)(h * 0.15), sh);
        place.Intersect(new Rectangle(0, 0, raw.Width, raw.Height));
        bool placeOk = place.Width >= h * 1.2 && place.Height >= sh - 1;
        var whole = Rectangle.Union(r, icon);
        if (placeOk) whole = Rectangle.Union(whole, place);
        whole.Intersect(new Rectangle(0, 0, raw.Width, raw.Height));
        var data = raw.LockBits(whole, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            // Scan0 points into the frame's own memory and Stride is the frame's, not the rectangle's: copy one row's
            // width at a time (copying Stride x Height from Scan0 read past the end of the frame on a bottom row and
            // crashed the tracker with an access violation on 2026-10-24)
            int stride = whole.Width * 4;
            var buf = PixelRows.Copy(data, whole.Width, whole.Height);
            int Luma(int x, int y) { int i = (y - whole.Top) * stride + (x - whole.Left) * 4; return (buf[i + 2] * 299 + buf[i + 1] * 587 + buf[i] * 114) / 1000; }
            var hist = new int[256]; int count = 0;
            int step = Math.Max(1, Math.Min(r.Width, r.Height) / 24);
            for (int y = r.Top; y < r.Bottom; y += step)
                for (int x = r.Left; x < r.Right; x += step) { hist[Luma(x, y)]++; count++; }
            int Pct(double p) { int target = (int)(count * p), acc = 0; for (int v = 0; v < 256; v++) { acc += hist[v]; if (acc > target) return v; } return 255; }
            double contrast = Math.Max(0, Pct(0.9) - Pct(0.5)) / 255.0;
            double background = Pct(0.3) / 255.0;
            double spread = -1;
            if (placeOk)
            {
                var means = new double[96];
                for (int cy = 0; cy < 8; cy++)
                    for (int cx = 0; cx < 12; cx++)
                    {
                        int x0 = place.Left + cx * place.Width / 12, y0 = place.Top + cy * place.Height / 8;
                        int x1 = Math.Max(x0 + 1, place.Left + (cx + 1) * place.Width / 12), y1 = Math.Max(y0 + 1, place.Top + (cy + 1) * place.Height / 8);
                        int sx = Math.Max(1, (x1 - x0) / 4), sy = Math.Max(1, (y1 - y0) / 4);
                        double s = 0; int k = 0;
                        for (int y = y0; y < y1; y += sy) for (int x = x0; x < x1; x += sx) { s += Luma(x, y); k++; }
                        means[cy * 12 + cx] = s / Math.Max(1, k);
                    }
                double avg = means.Average();
                spread = Math.Sqrt(means.Sum(v => (v - avg) * (v - avg)) / means.Length) / 255.0;
            }
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
            return (contrast, hash, background, spread);
        }
        finally { raw.UnlockBits(data); }
    }
}
