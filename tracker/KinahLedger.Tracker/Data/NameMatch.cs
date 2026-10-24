namespace KinahLedger.Data;

/// <summary>
/// The items a name could be right now, in the order they are tried (brief item 1, 2026-10-24): the loot tables of the
/// enemies on the target bar, recently on it and around the character; the gathering materials while gathering; the open
/// recipe's outputs; this run's items. <see cref="Closed"/> says the sets are authoritative for the moment (fighting
/// enemies whose loot tables are known, in the open world): a misread whose only close item lies outside all of them and
/// more than <see cref="NameMatch.TightCost"/> away is then refused, never counted.
/// </summary>
public sealed class NameContext
{
    public List<(string Source, IReadOnlyCollection<string> Names)> Sets { get; } = new();
    public bool Closed { get; init; }

    public NameContext Add(string source, IEnumerable<string> names)
    {
        var l = names.Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (l.Count > 0) Sets.Add((source, l));
        return this;
    }

    public bool Empty => Sets.Count == 0 && !Closed;
}

/// <summary>What a name read off the screen was matched to, where from (a <see cref="NameMatch"/> source), at what weighted cost.</summary>
/// <param name="FullId">The item the whole list alone would have given (what <see cref="ItemDictionary.IdOf"/> snaps to), for the log.</param>
public sealed record NameMatchResult(long? Id, string Source, int Cost, long? FullId = null);

/// <summary>
/// Edit distance that knows how Windows' text recognition misreads the game's font (brief item 1): "l", "I" and "1" are
/// near the same, so are "O" and "0", "rn" and "m", "cl" and "d", "vv" and "w", "5" and "S". Costs are in tenths of a
/// plain edit (<see cref="Unit"/>), so a weighted distance is never more than ten times the plain one and every name the
/// old budget allowed is still allowed. Names are compared as <see cref="ItemDictionary"/> normalises them (lower case,
/// accents off), so the table is in lower case.
/// </summary>
public static class NameMatch
{
    /// <summary>One plain edit (a letter added, dropped or changed).</summary>
    public const int Unit = 10;
    /// <summary>The most a name outside every nearby table may be off and still count when the context is closed: one plain edit, or three of the usual slips.</summary>
    public const int TightCost = Unit;

    // where a name came from, as Accuracy.Name logs it
    public const string Exact = "exact", Alias = "alias", TargetTable = "target-table", ZoneGather = "zone-gather", Recipe = "recipe",
        Run = "run", Full = "full", Refused = "refused", None = "none";

    /// <summary>A source that is one of the context's sets (not the whole list, not exact).</summary>
    public static bool IsContext(string source) => source is TargetTable or ZoneGather or Recipe or Run;

    // one letter for another, both ways; anything else costs a plain edit
    static readonly (char A, char B, int Cost)[] Pairs =
    {
        // the slips seen most (l/I/1/|, O/0, 5/S, 8/B), as cheap as a third of an edit
        ('i', 'l', 3), ('l', '1', 3), ('i', '1', 3), ('o', '0', 3), ('s', '5', 3), ('b', '8', 3),
        // the next most common, half an edit: Q for O, 9 and q for g, 2 for z, e/c/o, u/v, n/h ("Fihe"), m/n ("Orichalcun"), t/f, j/i, 6/b
        ('o', 'q', 5), ('0', 'q', 5), ('g', '9', 5), ('g', 'q', 5), ('z', '2', 5), ('e', 'c', 5), ('c', 'o', 5), ('a', 'o', 5),
        ('u', 'v', 5), ('n', 'h', 5), ('m', 'n', 5), ('t', 'f', 5), ('i', 'j', 5), ('b', '6', 5), ('y', 'v', 5),
    };

    // two letters read as one (or one as two): "rn" for "m" ("Orichalcurn"), "cl" for "d", "vv" for "w"
    static readonly (string Two, char One, int Cost)[] Multi =
    {
        ("rn", 'm', 3), ("cl", 'd', 3), ("vv", 'w', 3), ("ri", 'n', 5), ("li", 'h', 5), ("nn", 'm', 5), ("ii", 'u', 5), ("in", 'm', 5),
    };

    static readonly int[,] Sub = BuildSub();
    static readonly Dictionary<int, int> MultiCost = Multi.ToDictionary(m => Key3(m.Two[0], m.Two[1], m.One), m => m.Cost);
    static readonly object LearnLock = new();

    static int[,] BuildSub()
    {
        var t = new int[128, 128];
        for (int a = 0; a < 128; a++) for (int b = 0; b < 128; b++) t[a, b] = a == b ? 0 : Unit;
        foreach (var (a, b, c) in Pairs) { t[a, b] = c; t[b, a] = c; }
        return t;
    }

    static int Key3(char a, char b, char c) => (a << 16) | (b << 8) | c;

    /// <summary>The cost of reading <paramref name="b"/> where the game printed <paramref name="a"/>.</summary>
    public static int SubCost(char a, char b) => a == b ? 0 : a < 128 && b < 128 ? Sub[a, b] : Unit;

    /// <summary>A space or a mark the recognizer adds or drops costs half an edit ("Orichalcum0re", "Raw Leather.").</summary>
    static int InsDel(char c) => c is ' ' or '.' or ',' or '\'' or '-' ? Unit / 2 : Unit;

    /// <summary>
    /// A misread the crowd settled (data/aliases.json) that differs from its item in one letter teaches that pair: it costs
    /// half an edit from then on, if it cost more. Mined at load, so the table follows what trackers really see.
    /// </summary>
    public static bool Learn(string read, string name)
    {
        if (read.Length != name.Length || read.Length < 3) return false;
        int at = -1;
        for (int i = 0; i < read.Length; i++)
            if (read[i] != name[i]) { if (at >= 0) return false; at = i; }
        if (at < 0) return false;
        char a = name[at], b = read[at];
        if (a >= 128 || b >= 128 || char.IsWhiteSpace(a) || char.IsWhiteSpace(b)) return false;
        lock (LearnLock)
        {
            if (Sub[a, b] <= Unit / 2) return false;
            Sub[a, b] = Unit / 2; Sub[b, a] = Unit / 2;
        }
        return true;
    }

    /// <summary>
    /// Weighted edit distance between two normalised names, in tenths of an edit, or int.MaxValue once it is sure to pass
    /// <paramref name="max"/>. Besides adding, dropping and changing a letter, two letters may stand for one ("rn" ~ "m").
    /// </summary>
    public static int Distance(string a, string b, int max)
    {
        if (max < 0) return int.MaxValue;
        int n = a.Length, m = b.Length;
        // each letter of length difference costs at least what two-for-one does
        if (Math.Abs(n - m) * 3 > max) return int.MaxValue;
        var r0 = new int[m + 1]; var r1 = new int[m + 1]; var r2 = new int[m + 1];
        r1[0] = 0;
        for (int j = 1; j <= m; j++) r1[j] = r1[j - 1] + InsDel(b[j - 1]);
        int prevMin = 0;
        for (int i = 1; i <= n; i++)
        {
            char ai = a[i - 1];
            r2[0] = r1[0] + InsDel(ai);
            int rowMin = r2[0];
            for (int j = 1; j <= m; j++)
            {
                char bj = b[j - 1];
                int v = Math.Min(r1[j - 1] + SubCost(ai, bj), Math.Min(r1[j] + InsDel(ai), r2[j - 1] + InsDel(bj)));
                // two letters of a for one of b, and one of a for two of b
                if (i >= 2 && MultiCost.TryGetValue(Key3(a[i - 2], ai, bj), out var c1)) v = Math.Min(v, r0[j - 1] + c1);
                if (j >= 2 && MultiCost.TryGetValue(Key3(b[j - 2], bj, ai), out var c2)) v = Math.Min(v, r1[j - 2] + c2);
                r2[j] = v;
                if (v < rowMin) rowMin = v;
            }
            // a later cell can reach back two rows (two-for-one), so both rows must be past the budget
            if (rowMin > max && prevMin > max) return int.MaxValue;
            prevMin = rowMin;
            (r0, r1, r2) = (r1, r2, r0);
        }
        return r1[m] <= max ? r1[m] : int.MaxValue;
    }

    /// <summary>
    /// Two first letters that are one of the cheap slips ("l" for "I", "0" for "O"), or the start of two letters read for
    /// one ("vv" for "w"): a misread can start with either.
    /// </summary>
    public static bool CloseFirst(char a, char b) =>
        a == b || SubCost(a, b) <= 3 || Multi.Any(m => m.Cost <= 3 && ((m.Two[0] == a && m.One == b) || (m.Two[0] == b && m.One == a)));
}
