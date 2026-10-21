using System.Drawing;
using System.Text.RegularExpressions;

namespace KinahLedger.Ocr;

/// <summary>
/// The loot list the game shows just left of the character for a few seconds after each pickup: one row per pickup,
/// an icon then "Kina x404" or "Raw Leather x1" (long names scroll sideways, so their start can be cut off). It is a
/// second witness for the chat: a pickup the chat read missed during fast kills still shows here.
/// </summary>
public static partial class LootPopup
{
    /// <summary>
    /// Where the list sits: left of the screen's centre and a little below it. The game's interface scales with the
    /// window's height, so the band is measured in heights from the centre, which holds on wide and ultrawide screens.
    /// </summary>
    public static Rectangle Region(Rectangle game)
    {
        double h = game.Height, cx = game.Left + game.Width / 2.0, cy = game.Top + h / 2.0;
        int left = (int)(cx - 0.42 * h), right = (int)(cx - 0.02 * h);
        int top = (int)(cy - 0.06 * h), bottom = (int)(cy + 0.30 * h);
        return Rectangle.FromLTRB(Math.Max(game.Left, left), Math.Max(game.Top, top), Math.Min(game.Right, right), Math.Min(game.Bottom, bottom));
    }

    /// <summary>One row of the list: Kinah (Amount) or an item (Name, Count), as read; Key compares rows across reads.</summary>
    public sealed record Row(string Text, string Name, long Amount, int Count, bool IsKinah)
    {
        /// <summary>The count wasn't read; the row stands for one of the item.</summary>
        public bool NoCount { get; init; }
        public string Key => IsKinah ? "kina" + Amount : Regex.Replace(Name.ToLowerInvariant(), @"[^a-z0-9]", "") + "x" + Count;
    }

    [GeneratedRegex(@"^(?<name>.+?)\s*[xX×\*]\s*(?<n>[0-9OoIl][0-9OoIl,\.]*)\s*$")]
    private static partial Regex RowShape();

    [GeneratedRegex(@"^k[i1l]n(?:a|ah)$", RegexOptions.IgnoreCase)]
    private static partial Regex KinaWord();

    /// <summary>The rows in one read of the region, top to bottom. Anything that isn't "name x count" is left out.</summary>
    public static List<Row> Rows(IEnumerable<(int Top, string Text)> lines)
    {
        var rows = new List<Row>();
        foreach (var (_, raw) in lines.OrderBy(l => l.Top))
        {
            // marks the recognizer adds at either end of a row ("*Kina x212", "kina x483?")
            var t = Regex.Replace(raw, @"\s+", " ").Trim();
            t = Regex.Replace(t, @"^[^A-Za-z0-9(]+|[^A-Za-z0-9)]+$", "");
            var m = RowShape().Match(t);
            if (!m.Success)
            {
                // a row whose count scrolled away or wasn't read: an item name alone counts as one, if the item list knows it
                if (t.Length >= 4 && !t.Contains(':') && Regex.IsMatch(t, @"^[A-Z][A-Za-z'\- ()]+$")) rows.Add(new Row(t, t, 0, 1, false) { NoCount = true });
                continue;
            }
            var name = m.Groups["name"].Value.Trim().Trim('.', ',', ':', ';', '|', '-');
            long n = ChatParser.Digits(m.Groups["n"].Value);
            if (n <= 0 || name.Length < 2) continue;
            bool kina = KinaWord().IsMatch(name);
            rows.Add(new Row(t, name, kina ? n : 0, kina ? 1 : (int)Math.Clamp(n, 1, 9999), kina));
        }
        return rows;
    }
}

/// <summary>
/// Which rows of the loot list are new in a read. Rows leave from the top as they expire and arrive at the bottom, so
/// the rows of the last read that are still there are the top of this one; what follows them is new. After the list
/// was empty, everything in it is new: it only ever shows the last few seconds of pickups.
/// </summary>
public sealed class LootPopupTracker
{
    List<LootPopup.Row> _prev = new();

    public List<LootPopup.Row> NewRows(List<LootPopup.Row> cur)
    {
        if (cur.Count == 0) { _prev = cur; return new List<LootPopup.Row>(); }
        if (_prev.Count == 0) { _prev = cur; return new List<LootPopup.Row>(cur); }
        // the longest run at the end of the last read that is also the start of this one
        for (int k = Math.Min(_prev.Count, cur.Count); k >= 1; k--)
        {
            bool same = true;
            for (int i = 0; i < k && same; i++) same = Same(_prev[_prev.Count - k + i], cur[i]);
            if (!same) continue;
            var fresh = cur.Skip(k).ToList();
            _prev = cur;
            return fresh;
        }
        // nothing in common: the old rows all expired and these all arrived since
        _prev = cur;
        return new List<LootPopup.Row>(cur);
    }

    /// <summary>The same row read twice: the same Kinah amount or item and count, allowing a letter of misread in a name that scrolls.</summary>
    static bool Same(LootPopup.Row a, LootPopup.Row b)
    {
        if (a.IsKinah != b.IsKinah) return false;
        if (a.IsKinah) return a.Amount == b.Amount;
        if (a.Count != b.Count) return false;
        string x = a.Key, y = b.Key;
        // a long name scrolls sideways, so one read may show its start and the next its end: either contains most of the other
        return x == y || x.Contains(y) || y.Contains(x) || (x.Length >= 6 && y.Length >= 6 && (x[^6..] == y[^6..]));
    }
}
