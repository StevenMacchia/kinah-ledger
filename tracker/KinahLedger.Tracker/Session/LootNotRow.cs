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
/// could be merged with. Three rules, each enough on its own:
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
///       by its background.
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
    public const string Plate = "a nameplate", Zone = "a zone", Off = "not on the list";
    public const double NameSeconds = 10, ZoneSeconds = 120, StripSeconds = 30, LogEverySeconds = 60;
    public const double FlatSpread = 0.012, FlatShare = 0.1;
    public const double OffStripGap = 0.18, OffStripRatio = 1.8, OffStripAbsolute = 0.6, OffStripMinContrast = 0.35;
    /// <summary>A line with this much going on where its icon sits, over a background within this of the strip's, is a row whatever its text.</summary>
    public const double VouchSpread = 0.03, VouchGap = 0.08;

    readonly ItemDictionary _items;
    readonly Dictionary<string, (string Name, string Kind, DateTime At)> _seen = new(StringComparer.Ordinal);
    readonly List<(double Bg, DateTime At)> _strip = new();
    readonly Dictionary<string, DateTime> _logged = new(StringComparer.Ordinal);
    readonly Dictionary<string, string?> _zoneListCache = new(StringComparer.Ordinal);

    /// <summary>Lines dropped, every read; and the drops logged and counted (once per text per minute, as list_notrow).</summary>
    public int Dropped { get; private set; }
    public int Logged { get; private set; }

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
        var keep = new List<LootLine>(lines.Count);
        foreach (var (line, row) in parsed)
        {
            string detail = "";
            var why = row == null ? null : Why(line, row, stripNow ?? stripRecent, out detail);
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
        return keep;
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
