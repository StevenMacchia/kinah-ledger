using System.Text.RegularExpressions;
using KinahLedger.Data;
using KinahLedger.Ocr;

namespace KinahLedger.Session;

/// <summary>
/// Lines in the loot list's band that are not loot list rows, dropped before they become rows (LootRows) (2026-10-24).
/// The band sits beside the character, so the world's own text lands in it: on Steven's 5120x2160 screen, in 35 minutes,
/// 157 such lines were refused only because they hadn't settled ("Vangi", "Vang", "Vangt": the nameplate "Vanguard of
/// Atreia" standing next to the character; "Kraka Footpath hmpsite": the campsite's name; "Withered Fairy", a nameplate),
/// and many more settled and reached the cross-check, each an OCR read and a row object that a real row in the same slot
/// could be merged with. Four rules, each enough on its own:
///
///   (a) the text is, by the fuzzy rules below, a nameplate or target name seen in the last <see cref="NameSeconds"/>
///       (this pass or the one before), the current zone's name or one seen in the last <see cref="ZoneSeconds"/>, a
///       zone's name with more after it ("Kraka Footpath Campsite" in Kraka Footpath), or any zone on the list;
///   (b) nothing is left of it where the icon sits: the whole place an icon could be (LootRows.MeasureAll: 3.2 to 0.15
///       line heights left of the text) is flat, spread at most <see cref="FlatSpread"/> and under
///       <see cref="FlatShare"/> of the text's own contrast (a faint row's icon fades with its text, so the rule scales);
///       the icon hash being 1 (LootRows.Measure's "flat square") is the same verdict on a smaller square;
///   (c) it isn't on the list's dark strip: its background (the darker 30% of its box) is at least
///       <see cref="OffStripGap"/> above the strip's and <see cref="OffStripRatio"/> times it, the strip's being what
///       sure rows in the same read sit on (Kinah, "(Bound)" or a count of 2 or more), else the same over the last
///       <see cref="StripSeconds"/>, else over <see cref="OffStripAbsolute"/>. Only for text at least
///       <see cref="OffStripMinContrast"/> bright: a row fading in fades with its strip, so a faint line is never judged
///       by its background;
///   (e) its text starts far right of the rows' own text column: more than <see cref="FarRightPitches"/> slot pitches
///       right of where sure rows' text starts (the median over the last ones: Kinah, "(Bound)", an item's own name with
///       its icon). On Steven's 5120x2160 screen rows' text starts at x about 1920 and the title, the green line of the
///       player's own nameplate at screen centre, at about 2437 (7 pitches of 73 px right; kinah-bench compare-131029.md),
///       over the world, with nothing icon-like to its left. Only when both are measured (the trace keeps "x" since this
///       version);
///   (d) it is static text (2026-10-24 13:10, the player's title "Vanguard of Atreia" read as "Vangi", "Vangt", "Vqng(",
///       "Yang(" for minutes, its icon place over the world so neither (b) nor (c) holds on every read, and each garbled
///       read a new one-read row that went unsure): text with no icon of its own (no hash, a flat one, or a spread under
///       <see cref="VouchSpread"/>) read at the same y, within half a slot, <see cref="StaticReads"/> times or more over
///       more than <see cref="StaticSeconds"/>, whatever its text, makes that y a static band; every later line there is
///       dropped at once and logged once a minute ("static text at y ..."). A real row never qualifies: it is up 2.5 s and
///       moves up when a new one comes. Two guards: a line that was a slot or more lower in the read before, with the
///       same text, has moved up into the band and is a row; and where real rows have sat lately (Kina, a soul, "(Bound)",
///       a row with its icon and an item's own name: the list's slots) only a line with no icon of its own is dropped.
///
/// Thresholds (b) and (c), from Steven's trace.log of 2026-10-24 12:47 to 13:25 local (1,209 reads of the title, 131 of
/// real Kina, Sapphire Gemstone, Orichalcum Ore and soul rows): real item rows' spread is 0.112 or more (Kina 0.197 or
/// more) and at least 0.23 of their contrast; the title's median is 0.028, so (b) at 0.05 and 0.15 of the contrast takes
/// half the title's reads and no real row (margins 2.2x and 1.5x). Real rows' background with their text at 0.35
/// or more is 0.29 at most; (c) takes only what is 0.22 above the strip (about 0.34 on a 0.12 strip). (d) takes the rest.
///
/// Never dropped, whatever the rules say: Kinah rows, soul rows, shop rows, anything with "(Bound)" (or "(B..." read
/// badly) or opening with "&lt;", a row with a count of 2 or more, a row whose name is an item's own name, and a line the
/// picture vouches for (an icon beside it, at least <see cref="VouchSpread"/>, on a background within
/// <see cref="VouchGap"/> of the strip's): a soul row cut at the left reads as the enemy's name, which rule (a) would
/// otherwise take for its nameplate. A line with nothing measured (a trace without the picture's measures, a test) is
/// judged by rule (a) only.
/// </summary>
public sealed class LootNotRow
{
    public const string Plate = "a nameplate", Zone = "a zone", Off = "not on the list", Static = "static text";
    public const double NameSeconds = 10, ZoneSeconds = 120, StripSeconds = 30, LogEverySeconds = 60;
    public const double FlatSpread = 0.05, FlatShare = 0.15;
    public const double OffStripGap = 0.22, OffStripRatio = 1.8, OffStripAbsolute = 0.6, OffStripMinContrast = 0.35;
    /// <summary>A line with this much going on where its icon sits, over a background within this of the strip's, is a row whatever its text.</summary>
    public const double VouchSpread = 0.08, VouchGap = 0.08;
    /// <summary>(d): this many reads with no icon at one y, over more than this long, make it a static band; it stays one while such reads keep coming within <see cref="StaticHoldSeconds"/>.</summary>
    public const int StaticReads = 3;
    public const double StaticSeconds = 6, StaticHoldSeconds = 30, StaticForgetSeconds = 60;
    /// <summary>A y where a real row sat this recently is one of the list's slots.</summary>
    public const double RowSlotSeconds = 120;
    /// <summary>(e): text starting more than this many slot pitches right of the rows' text column is not a row; the column is known after this many sure rows.</summary>
    public const double FarRightPitches = 5.5;
    public const int ColumnRows = 3;
    /// <summary>The list's slot pitch before any read has shown it, in pixels (73 at 5120x2160).</summary>
    public const int DefaultPitch = 60;

    readonly ItemDictionary _items;
    readonly Dictionary<string, (string Name, string Kind, DateTime At)> _seen = new(StringComparer.Ordinal);
    readonly List<(double Bg, DateTime At)> _strip = new();
    readonly Dictionary<string, DateTime> _logged = new(StringComparer.Ordinal);
    readonly Dictionary<string, string?> _zoneListCache = new(StringComparer.Ordinal);

    /// <summary>A y band where text with no icon of its own keeps being read (rule d).</summary>
    sealed class Band
    {
        public double Y;
        public readonly List<DateTime> Reads = new();
        public DateTime Last, Logged = DateTime.MinValue;
        public bool IsStatic(DateTime now) => Reads.Count >= StaticReads && (Reads[^1] - Reads[0]).TotalSeconds > StaticSeconds
            && (now - Last).TotalSeconds <= StaticHoldSeconds && (Last - now).TotalSeconds < 5;
    }
    readonly List<Band> _bands = new();
    readonly List<(double Y, DateTime At)> _rowSlots = new();
    readonly List<int> _pitches = new();
    readonly List<(int Left, DateTime At)> _lefts = new();
    List<(int Top, string Text)> _prev = new();
    DateTime _prevAt = DateTime.MinValue;

    /// <summary>Lines dropped, every read; and the drops logged and counted (once per text per minute, as list_notrow).</summary>
    public int Dropped { get; private set; }
    public int Logged { get; private set; }
    /// <summary>Of the lines dropped, those dropped as static text (rule d).</summary>
    public int StaticDropped { get; private set; }

    /// <summary>
    /// The list's slot pitch: the smallest gap between two real rows (Kinah, "(Bound)", an item's own name with its icon) in
    /// one read over the last ones, never the gap from the title or a nameplate to a row (223 px from the title to the
    /// bottom slot on 2026-10-24); <see cref="DefaultPitch"/> before two rows have shown together. Half of it is "the same y".
    /// </summary>
    public int Pitch => _pitches.Count == 0 ? DefaultPitch : _pitches.Min();
    double Tol => Math.Max(8, Pitch / 2.0);

    /// <summary>Where sure rows' text starts (the median of the last ones), or null before <see cref="ColumnRows"/>.</summary>
    public int? Column
    {
        get
        {
            if (_lefts.Count < ColumnRows) return null;
            var s = _lefts.Select(x => x.Left).OrderBy(x => x).ToList();
            return s[s.Count / 2];
        }
    }

    public LootNotRow(ItemDictionary items) { _items = items; }

    /// <summary>A name on screen now: a nameplate or the target bar's (<see cref="Plate"/>), or a zone's (<see cref="Zone"/>).</summary>
    public void See(string? name, DateTime at, string kind)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        var key = Norm(name);
        if (Letters(key) < 4) return;
        _seen[kind + "|" + key] = (name.Trim(), kind, at);
    }

    /// <summary>The lines that may be rows, in order; the others are dropped (logged once per text per minute).</summary>
    public List<LootLine> Filter(List<LootLine> lines, DateTime now)
    {
        if (lines.Count == 0) return lines;
        foreach (var k in _seen.Where(kv => (now - kv.Value.At).TotalSeconds > (kv.Value.Kind == Zone ? ZoneSeconds : NameSeconds) || (kv.Value.At - now).TotalSeconds > 5).Select(kv => kv.Key).ToList()) _seen.Remove(k);
        _strip.RemoveAll(s => (now - s.At).TotalSeconds > StripSeconds || (s.At - now).TotalSeconds > 5);
        var parsed = lines.Select(l => (Line: l, Row: LootPopup.Rows(new[] { (l.Top, l.Text) }).FirstOrDefault())).ToList();
        // the strip under this read's sure rows
        var sure = parsed.Where(p => p.Row != null && Sure(p.Line, p.Row) && p.Line.Background >= 0 && p.Line.Contrast >= 0.3).Select(p => p.Line.Background).ToList();
        foreach (var b in sure) { _strip.Add((b, now)); if (_strip.Count > 20) _strip.RemoveAt(0); }
        double? stripNow = sure.Count > 0 ? Median(sure) : null;
        double? stripRecent = _strip.Count >= 2 ? Median(_strip.Select(s => s.Bg).ToList()) : null;
        // rule (d)'s memory: the slot pitch, where real rows sit, and where text with no icon of its own keeps being read
        // (this read counts before it is judged: the third read over six seconds is dropped at once)
        var exempt = parsed.Select(p => p.Row != null && Exempt(p.Line, p.Row)).ToList();
        LearnPitch(parsed.Where((p, i) => exempt[i] && (Sure(p.Line, p.Row!) || LootCrossCheck.SoulOf(p.Row!.Name) != null || !NoIcon(p.Line))).Select(p => p.Line.Top).ToList());
        double tol = Tol;
        _bands.RemoveAll(b => (now - b.Last).TotalSeconds > StaticForgetSeconds || (b.Last - now).TotalSeconds > 5);
        _rowSlots.RemoveAll(r => (now - r.At).TotalSeconds > RowSlotSeconds || (r.At - now).TotalSeconds > 5);
        _lefts.RemoveAll(x => (now - x.At).TotalSeconds > 600 || (x.At - now).TotalSeconds > 5);
        for (int i = 0; i < parsed.Count; i++)
        {
            var (line, row) = parsed[i];
            if (row == null || line.Contrast < 0) continue;
            if (exempt[i])
            {
                // a real row: Kina, a soul, "(Bound)", a count of 2 or more, or an item's own name with an icon beside it
                if (LootCrossCheck.SoulOf(row.Name) != null || Sure(line, row) || !NoIcon(line)) { _rowSlots.Add((line.Top, now)); if (_rowSlots.Count > 60) _rowSlots.RemoveAt(0); }
                // where rows' text starts: a sure row or an item's own name with its icon (a soul's marquee slice starts anywhere)
                if (line.Left >= 0 && LootCrossCheck.SoulOf(row.Name) == null && (Sure(line, row) || !NoIcon(line))) { _lefts.Add((line.Left, now)); if (_lefts.Count > 30) _lefts.RemoveAt(0); }
            }
            // text that has no icon of its own, or names no item: what the title's reads are, whatever the world behind them
            else if (NoIcon(line) || !Knows(row.Name)) Evidence(line.Top, now, tol);
        }
        var keep = new List<LootLine>(lines.Count);
        for (int i = 0; i < parsed.Count; i++)
        {
            var (line, row) = parsed[i];
            string detail = "";
            var why = row == null || exempt[i] ? null : Why(line, row, stripNow ?? stripRecent, out detail);
            // (d) after the others, which keep their own verdicts and log lines: static text they let through
            if (why == null && row != null && !exempt[i] && StaticBand(line, now, tol) is { } band)
            {
                Dropped++; StaticDropped++;
                if ((now - band.Logged).TotalSeconds < LogEverySeconds && (band.Logged - now).TotalSeconds < 5) continue;
                band.Logged = now;
                Logged++;
                Accuracy.Add("list_notrow");
                CrashLog.Note($"loot list: static text at y {band.Y:0}: ignored (\"{line.Text.Trim()}\"; text with no icon of its own read there {band.Reads.Count} times over {(band.Reads[^1] - band.Reads[0]).TotalSeconds:0.0} s, and a real row is up 2.5 s and moves up)");
                continue;
            }
            if (why == null) { keep.Add(line); continue; }
            Dropped++;
            var text = line.Text.Trim();
            var key = Norm(text);
            if (_logged.TryGetValue(key, out var at) && (now - at).TotalSeconds < LogEverySeconds && (at - now).TotalSeconds < 5) continue;
            _logged[key] = now;
            if (_logged.Count > 300) foreach (var old in _logged.Where(kv => (now - kv.Value).TotalSeconds > LogEverySeconds).Select(kv => kv.Key).ToList()) _logged.Remove(old);
            Logged++;
            Accuracy.Add("list_notrow");
            CrashLog.Note($"loot list: \"{text}\" is {why}: ignored ({detail})");
        }
        _prev = parsed.Where(p => p.Row != null).Select(p => (p.Line.Top, p.Line.Text)).ToList();
        _prevAt = now;
        return keep;
    }

    /// <summary>
    /// Whether a line is already known not to be a row, before this read is filtered: static text (rule d) or a nameplate
    /// or target name on screen (rule a's names). The gathering panel's parse leaves such lines out (2026-10-24: the
    /// player's title read as the panel's first line, "Vangt | Sapphire Gemstone | Success"). A panel's own label never is.
    /// </summary>
    public bool Ignorable(LootLine line, DateTime now)
    {
        if (LootPopup.PanelMark(line.Text)) return false;
        if (LootPopup.Rows(new[] { (line.Top, line.Text) }).FirstOrDefault() is not { } row || Exempt(line, row)) return false;
        if (StaticBand(line, now, Tol) != null) return true;
        foreach (var (name, kind, at) in _seen.Values)
            if (kind == Plate && (now - at).TotalSeconds <= NameSeconds && (at - now).TotalSeconds <= 5 && Like(row.Name, name, prefix: true, startsWith: false)) return true;
        return false;
    }

    /// <summary>Whether the item list knows the name (its fuzzy match). Cached per text.</summary>
    bool Knows(string name)
    {
        var key = Norm(name);
        if (_knowsCache.TryGetValue(key, out var k)) return k;
        bool was = Accuracy.Muted;
        Accuracy.Muted = true;   // asking the item list isn't a misread
        try { k = _items.Knows(name); }
        finally { Accuracy.Muted = was; }
        if (_knowsCache.Count > 2000) _knowsCache.Clear();
        return _knowsCache[key] = k;
    }
    readonly Dictionary<string, bool> _knowsCache = new(StringComparer.Ordinal);

    /// <summary>No icon of its own beside it: no hash, a flat one, or a spread where the icon sits too low to vouch for one.</summary>
    static bool NoIcon(LootLine line) => line.Icon <= 1 || line.Spread < VouchSpread;

    /// <summary>One read of text with no icon of its own at this y: the band it falls in (within half a slot) takes it, or a new one starts.</summary>
    void Evidence(int y, DateTime now, double tol)
    {
        var b = _bands.Where(x => Math.Abs(x.Y - y) <= tol).OrderBy(x => Math.Abs(x.Y - y)).FirstOrDefault();
        if (b == null) { b = new Band { Y = y }; _bands.Add(b); if (_bands.Count > 12) _bands.RemoveAt(0); }
        b.Reads.RemoveAll(t => (now - t).TotalSeconds > StaticForgetSeconds || (t - now).TotalSeconds > 5);
        b.Reads.Add(now);
        if (b.Reads.Count > 40) b.Reads.RemoveAt(0);
        b.Y += (y - b.Y) / Math.Min(b.Reads.Count, 10);
        b.Last = now;
    }

    /// <summary>
    /// The static band a measured line sits in, or null: none there, the line moved up into it from a slot lower in the read
    /// before (a row), or the band is one of the list's slots and the line has an icon of its own.
    /// </summary>
    Band? StaticBand(LootLine line, DateTime now, double tol)
    {
        if (line.Contrast < 0) return null;
        var b = _bands.Where(x => Math.Abs(x.Y - line.Top) <= tol && x.IsStatic(now)).OrderBy(x => Math.Abs(x.Y - line.Top)).FirstOrDefault();
        if (b == null) return null;
        if (!NoIcon(line) && _rowSlots.Any(r => Math.Abs(r.Y - line.Top) <= tol)) return null;
        if ((now - _prevAt).TotalSeconds <= 3 && _prevAt <= now && _prev.Any(p => p.Top > line.Top + tol && SameText(p.Text, line.Text))) return null;
        return b;
    }

    /// <summary>The smallest gap of 20 px or more between real rows in one read: a slot's pitch, or a few (kept for the last 60 reads with two rows or more).</summary>
    void LearnPitch(List<int> rowTops)
    {
        if (rowTops.Count < 2) return;
        var tops = rowTops.OrderBy(t => t).ToList();
        int best = int.MaxValue;
        for (int i = 1; i < tops.Count; i++) { int d = tops[i] - tops[i - 1]; if (d >= 20 && d < best) best = d; }
        if (best == int.MaxValue) return;
        _pitches.Add(best);
        if (_pitches.Count > 60) _pitches.RemoveAt(0);
    }

    static bool SameText(string a, string b)
    {
        string x = Norm(a), y = Norm(b);
        if (x == y) return true;
        if (Letters(x) >= 4 && Letters(y) >= 4 && (x.Contains(y) || y.Contains(x))) return true;
        return Like(a, b, prefix: false, startsWith: false);
    }

    /// <summary>A row nothing here may drop: Kinah, a soul, a shop's, "(Bound)", "&lt;...&gt;", a count of 2 or more, an item's own name.</summary>
    bool Exempt(LootLine line, LootPopup.Row row)
    {
        if (Sure(line, row) || LootCrossCheck.SoulOf(row.Name) != null || LootCrossCheck.IsShopWord(row.Name)) return true;
        if (line.Text.TrimStart().StartsWith('<')) return true;
        // "(Bound)" read badly ("Drakan Guard Dog (Boc": an enemy's soul row cut at the left, the enemy's name in it)
        if (Regex.IsMatch(line.Text, @"\(\s*[bB8]|ound\)")) return true;
        bool was = Accuracy.Muted;
        Accuracy.Muted = true;   // asking the item list isn't a misread
        try { return _items.IsExact(row.Name); }
        finally { Accuracy.Muted = was; }
    }

    /// <summary>A row sure to be a loot list row, whose background is the strip's: Kinah, "(Bound)", a count of 2 or more.</summary>
    static bool Sure(LootLine line, LootPopup.Row row) =>
        row.IsKinah || Regex.IsMatch(row.Name, @"^\W*kin[ae]h?\W*(\(bound\))?\W*$", RegexOptions.IgnoreCase)
        || line.Text.Contains("bound", StringComparison.OrdinalIgnoreCase) || (!row.NoCount && row.Count >= 2);

    /// <summary>Why a line is not a row (<see cref="Plate"/>, <see cref="Zone"/>, <see cref="Off"/>), or null when it may be one.</summary>
    internal string? Why(LootLine line, LootPopup.Row row, double? strip, out string detail)
    {
        detail = "";
        if (Exempt(line, row)) return null;
        // (e) far right of the rows' text column: the player's own nameplate at screen centre, over the world (whose
        // texture can look like an icon to the vouch below)
        if (line.Left >= 0 && Column is int col && line.Left - col > FarRightPitches * Pitch)
        { detail = $"its text starts at x {line.Left}, {line.Left - col} px right of the rows' text ({(line.Left - col) / (double)Pitch:0.0} slot pitches)"; return Off; }
        // the picture vouches for it: an icon beside it and the strip's own background behind it. A row may carry a name
        // that is also on screen (an enemy's soul row cut at the left is the enemy's name), so no name drops it then
        if (line.Spread >= VouchSpread && line.Background >= 0 && strip is double vs && line.Background <= vs + VouchGap) return null;
        var text = row.Name;
        // (a) a name on screen: a nameplate or the target bar's (cut short by the band's edge too), a zone's
        foreach (var (name, kind, _) in _seen.Values.OrderBy(v => v.Kind == Plate ? 0 : 1))
            if (Like(text, name, prefix: kind == Plate, startsWith: kind == Zone)) { detail = name; return kind; }
        if (ZoneOnList(text) is { } z) { detail = z; return Zone; }
        // (b) no icon: the whole place one could be is flat
        if (line.Spread >= 0 && line.Contrast > 0 && line.Spread <= FlatSpread && line.Spread < FlatShare * line.Contrast)
        { detail = $"no icon beside it: spread {line.Spread:0.000}, text {line.Contrast:0.00}"; return Off; }
        // (c) not on the list's dark strip: bright text over a background far lighter than the strip's
        if (line.Background >= 0 && line.Contrast >= OffStripMinContrast)
        {
            bool off = strip is double s ? line.Background >= s + OffStripGap && line.Background >= s * OffStripRatio : line.Background >= OffStripAbsolute;
            if (off) { detail = strip is double s2 ? $"off the list's dark strip: background {line.Background:0.00} against {s2:0.00}" : $"off the list's dark strip: background {line.Background:0.00}"; return Off; }
        }
        return null;
    }

    /// <summary>Any zone on the list, read whole (a campsite's banner of a place nearby). Cached per text.</summary>
    string? ZoneOnList(string text)
    {
        var key = Norm(text);
        if (_zoneListCache.TryGetValue(key, out var hit)) return hit;
        string? found = null;
        if (Letters(key) >= 8)
            foreach (var z in ZoneLocator.Known)
                if (Like(text, z, prefix: false, startsWith: false)) { found = z; break; }
        if (_zoneListCache.Count > 2000) _zoneListCache.Clear();
        _zoneListCache[key] = found;
        return found;
    }

    /// <summary>
    /// The fuzzy rules: the whole text within a misread of the name (NameMatch's OCR-aware distance, a quarter of the
    /// letters, at least two); with <paramref name="prefix"/>, the text (4 letters or more) within a misread per four of the
    /// name's start (a nameplate cut by the band's edge: "Vangt" for "Vanguard of Atreia"); with <paramref name="startsWith"/>,
    /// the name (8 letters or more) within a misread per six of the text's start ("Kraka Footpath Campsite" in Kraka Footpath).
    /// </summary>
    internal static bool Like(string text, string name, bool prefix, bool startsWith)
    {
        string a = Norm(text), b = Norm(name);
        if (Letters(a) < 4 || Letters(b) < 4) return false;
        int whole = Math.Max(2, Math.Min(a.Length, b.Length) / 4) * NameMatch.Unit;
        if (NameMatch.Distance(a, b, whole) <= whole) return true;
        if (prefix && b.Length >= 8 && a.Length < b.Length)
        {
            int budget = Math.Max(1, a.Length / 4) * NameMatch.Unit;
            for (int n = Math.Max(4, a.Length - 1); n <= Math.Min(b.Length, a.Length + 1); n++)
                if (NameMatch.Distance(a, b[..n], budget) <= budget) return true;
        }
        if (startsWith && Letters(b) >= 8 && a.Length > b.Length)
        {
            int budget = Math.Max(1, b.Length / 6) * NameMatch.Unit;
            if (NameMatch.Distance(a[..b.Length], b, budget) <= budget) return true;
        }
        return false;
    }

    static string Norm(string s) => Regex.Replace(Regex.Replace(s.ToLowerInvariant(), @"[^a-z0-9]+", " "), @"\s+", " ").Trim();
    static int Letters(string s) => s.Count(char.IsLetterOrDigit);
    static double Median(List<double> v) { var s = v.OrderBy(x => x).ToList(); return s[s.Count / 2]; }
}
