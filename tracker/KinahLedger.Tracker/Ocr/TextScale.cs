namespace KinahLedger.Ocr;

/// <summary>
/// How much to enlarge the chat before it is read, from how tall its text is on this screen (2026-10-23). The same chat
/// is about 12 px tall on a 1080p screen and 25 px on a 4K one, and a fixed 2x left the small one under the size Windows'
/// reader handles best. The text's height comes from the reader's own word boxes, so nothing is measured twice: the
/// median of the last words of three letters or more, in screen pixels. The factor aims the text at
/// <see cref="TargetPx"/>, never below the configured scale (a setup that reads well today reads the same), never above
/// <see cref="Ceiling"/>, and moves only when the wanted factor differs by more than a tenth, so reads don't keep
/// changing size (the vote across reads compares them). Pure arithmetic: tested in parse-test.
/// </summary>
public sealed class TextScale
{
    public const double Ceiling = 4;
    const int Keep = 60;

    readonly Queue<double> _heights = new();
    readonly object _lock = new();

    public TextScale(double floor, double targetPx)
    {
        Floor = Math.Clamp(floor, 1, Ceiling);
        TargetPx = targetPx;
        Factor = Floor;
    }

    /// <summary>The configured scale (ocr.scale): the factor never goes below it.</summary>
    public double Floor { get; }
    /// <summary>How tall, in pixels after enlarging, the text should be (ocr.targetTextPx).</summary>
    public double TargetPx { get; }
    /// <summary>The factor reads use now.</summary>
    public double Factor { get; private set; }

    /// <summary>The chat text's height on screen (the median word), or null before any word was read.</summary>
    public double? TextPx { get { lock (_lock) return _heights.Count == 0 ? null : Median(_heights); } }

    /// <summary>
    /// The heights, in screen pixels, of the words one read found. True when the factor changed (the caller notes it in
    /// the log). Fewer than five words so far: no change yet.
    /// </summary>
    public bool Observe(IEnumerable<double> heights)
    {
        lock (_lock)
        {
            foreach (var h in heights)
            {
                if (h < 4 || h > 200) continue;
                _heights.Enqueue(h);
                if (_heights.Count > Keep) _heights.Dequeue();
            }
            if (_heights.Count < 5 || TargetPx <= 0) return false;
            double want = Math.Clamp(TargetPx / Median(_heights), Floor, Ceiling);
            // quarter steps, so tiny drifts in the measure never move it
            want = Math.Round(want * 4) / 4;
            if (Math.Abs(want - Factor) / Factor <= 0.1) return false;
            Factor = Math.Max(Floor, want);
            return true;
        }
    }

    static double Median(IEnumerable<double> xs)
    {
        var s = xs.OrderBy(x => x).ToList();
        return s.Count % 2 == 1 ? s[s.Count / 2] : (s[s.Count / 2 - 1] + s[s.Count / 2]) / 2;
    }
}
