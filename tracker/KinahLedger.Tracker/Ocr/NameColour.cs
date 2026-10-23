using System.Drawing;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace KinahLedger.Ocr;

/// <summary>
/// The colour a word was drawn in, from the capture as the game drew it (before Boost turns it grey). The chat prints an
/// item's name in its grade's colour, so the colour narrows which item a garbled name can be (2026-10-23). Pure
/// arithmetic over the pixels, so it is tested in parse-test.
/// </summary>
public static class TextColour
{
    /// <summary>
    /// The text colour inside <paramref name="box"/> of a 32-bit BGRA picture: the average of its brightest pixels (the
    /// letters' cores; their edges blend into the chat's backing). Null when the box holds too few bright pixels to say.
    /// </summary>
    public static (Color Colour, int Pixels)? Of(ReadOnlySpan<byte> bgra, int stride, int width, int height, Rectangle box)
    {
        var r = Rectangle.Intersect(box, new Rectangle(0, 0, width, height));
        if (r.Width < 2 || r.Height < 2) return null;
        var hist = new int[256];
        for (int y = r.Top; y < r.Bottom; y++)
            for (int x = r.Left; x < r.Right; x++)
            {
                int i = y * stride + x * 4;
                hist[Math.Max(bgra[i], Math.Max(bgra[i + 1], bgra[i + 2]))]++;
            }
        // the brightest tenth of the box, and never darker than mid-grey: letters are bright on the chat's dark backing
        int total = r.Width * r.Height, want = Math.Max(6, total / 10), seen = 0, cut = 256;
        while (cut > 0 && seen < want) seen += hist[--cut];
        cut = Math.Max(cut, 110);
        long sr = 0, sg = 0, sb = 0; int n = 0;
        for (int y = r.Top; y < r.Bottom; y++)
            for (int x = r.Left; x < r.Right; x++)
            {
                int i = y * stride + x * 4;
                if (Math.Max(bgra[i], Math.Max(bgra[i + 1], bgra[i + 2])) < cut) continue;
                sb += bgra[i]; sg += bgra[i + 1]; sr += bgra[i + 2]; n++;
            }
        if (n < 6) return null;
        return (Color.FromArgb((int)(sr / n), (int)(sg / n), (int)(sb / n)), n);
    }

    /// <summary>A word that is part of an item's name: not the verb, a count, the Kina word or a mark.</summary>
    public static bool NameWord(string word) =>
        !Regex.IsMatch(word.Trim(), @"^(?:acqu\w*\W*|obtain\w*\W*|k[i1l]nah?\W*|[x×*]?\d[\d,.]*\W*|\(bound\)\W*|\W+)$", RegexOptions.IgnoreCase);

    /// <summary>A colour with its brightness taken out: each channel over the brightest, so a dim and a bright gold match.</summary>
    public static (double R, double G, double B) Chroma(Color c)
    {
        double m = Math.Max(1.0, Math.Max(c.R, Math.Max(c.G, c.B)));
        return (c.R / m, c.G / m, c.B / m);
    }

    public static double Distance((double R, double G, double B) a, (double R, double G, double B) b) =>
        Math.Sqrt((a.R - b.R) * (a.R - b.R) + (a.G - b.G) * (a.G - b.G) + (a.B - b.B) * (a.B - b.B));
}

/// <summary>
/// The colour each item grade is printed in, learned on this PC from names read exactly ("Raw Leather acquired." with
/// Raw Leather a Common item says what Common looks like here). Nothing is assumed about the game's palette, so a
/// different screen, gamma or HDR setting just teaches other colours. A grade counts once <see cref="MinSamples"/> names
/// have shown it. Kept in grade-colours.json beside the logs, so a restart doesn't start over.
/// </summary>
public sealed class GradeColours
{
    public const int MinSamples = 6;
    /// <summary>How near a colour must be to a grade's to be that grade (in <see cref="TextColour.Chroma"/> units), at the least.</summary>
    public const double MinRadius = 0.12;
    /// <summary>How much nearer another grade must be before the colour rules a grade out.</summary>
    public const double Margin = 0.10;

    sealed class Stat { public double R { get; set; } public double G { get; set; } public double B { get; set; } public int N { get; set; } public double Spread { get; set; } }
    readonly Dictionary<string, Stat> _grades = new(StringComparer.OrdinalIgnoreCase);
    readonly object _lock = new();

    /// <summary>Names read exactly since the book was loaded; the caller saves now and then.</summary>
    public int Observed { get; private set; }

    /// <summary>A name read exactly, of an item of this grade, was printed in this colour.</summary>
    public void Observe(string grade, Color c)
    {
        if (grade.Length == 0) return;
        var x = TextColour.Chroma(c);
        lock (_lock)
        {
            if (!_grades.TryGetValue(grade, out var s)) _grades[grade] = s = new Stat { R = x.R, G = x.G, B = x.B };
            // a running mean that keeps adapting: after 200 names each new one still moves it a little
            double w = 1.0 / Math.Min(s.N + 1, 200);
            double d = TextColour.Distance(x, (s.R, s.G, s.B));
            s.R += (x.R - s.R) * w; s.G += (x.G - s.G) * w; s.B += (x.B - s.B) * w;
            s.Spread += (d - s.Spread) * w;
            s.N++;
            Observed++;
        }
    }

    /// <summary>The grades learned well enough to judge by, with their colours.</summary>
    public List<(string Grade, Color Colour, int Seen)> Learned()
    {
        lock (_lock)
            return _grades.Where(kv => kv.Value.N >= MinSamples)
                .Select(kv => (kv.Key, Color.FromArgb((int)(kv.Value.R * 255), (int)(kv.Value.G * 255), (int)(kv.Value.B * 255)), kv.Value.N)).ToList();
    }

    /// <summary>
    /// Whether a name printed in <paramref name="c"/> can be an item of <paramref name="grade"/>: true when the colour is
    /// that grade's, false only when it is clearly far from it and clearly another learned grade's, null when the book
    /// can't say (the grade not learned yet, or the colour between two).
    /// </summary>
    public bool? Agrees(string grade, Color c)
    {
        if (grade.Length == 0) return null;
        var x = TextColour.Chroma(c);
        lock (_lock)
        {
            if (!_grades.TryGetValue(grade, out var own) || own.N < MinSamples) return null;
            double dOwn = TextColour.Distance(x, (own.R, own.G, own.B)), rOwn = Math.Max(MinRadius, 2.5 * own.Spread);
            double dOther = double.MaxValue, rOther = MinRadius;
            foreach (var (g, s) in _grades)
            {
                if (s.N < MinSamples || string.Equals(g, grade, StringComparison.OrdinalIgnoreCase)) continue;
                double d = TextColour.Distance(x, (s.R, s.G, s.B));
                if (d < dOther) { dOther = d; rOther = Math.Max(MinRadius, 2.5 * s.Spread); }
            }
            if (dOwn <= rOwn && dOwn <= dOther) return true;
            if (dOwn > rOwn && dOther <= rOther && dOther + Margin < dOwn) return false;
            return null;
        }
    }

    public string ToJson()
    {
        lock (_lock) return JsonSerializer.Serialize(_grades);
    }

    public static GradeColours FromJson(string json)
    {
        var g = new GradeColours();
        var d = JsonSerializer.Deserialize<Dictionary<string, Stat>>(json);
        if (d != null) foreach (var (k, v) in d) if (v.N > 0) g._grades[k] = v;
        return g;
    }

    /// <summary>The book kept on this PC, or an empty one (no file yet, or a torn one).</summary>
    public static GradeColours Load(string path)
    {
        try { return File.Exists(path) ? FromJson(File.ReadAllText(path)) : new GradeColours(); }
        catch (Exception) { return new GradeColours(); }
    }

    public void Save(string path)
    {
        try { File.WriteAllText(path, ToJson()); }
        catch (Exception) { /* the book is relearned from the next names; a failed save costs nothing else */ }
    }
}
