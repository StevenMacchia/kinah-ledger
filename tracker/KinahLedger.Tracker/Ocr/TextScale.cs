namespace KinahLedger.Ocr;

/// <summary>
/// How much to enlarge the chat before it is read, from how tall its text is on this screen (2026-10-23). The same chat
/// is about 12 px tall on a 1080p screen and 25 px on a 4K one, and a fixed 2x left the small one under the size Windows'
/// reader handles best. The text's height comes from the reader's own word boxes, so nothing is measured twice: the
/// median of the last words of three letters or more, in screen pixels. The factor aims the text at
/// <see cref="TargetPx"/>, never below the configured scale (a setup that reads well today reads the same), never above
/// <see cref="Ceiling"/>, and moves only when the wanted factor differs by more than a tenth, so reads don't keep
/// changing size (the vote across reads compares them). Pure arithmetic: tested in parse-test.
/// With <see cref="ToTarget"/> (2026-10-24) the floor holds only for text under <see cref="SmallPx"/>; other text goes up
/// to the next quarter step that reaches the target, never under <see cref="Least"/>, and text at or above the target
/// never past <see cref="BigCap"/>. The target bar reads this way (Least 2). For the chat it is ocr.chat_scale_to_target,
/// OFF in code: 29 px chat on 5120x2160 would read at 1.25x instead of 2x, but drawn chats read worse there (bench --sizing).
/// </summary>
public sealed class TextScale
{
    public const double Ceiling = 4;
    /// <summary>Text shorter than this, in screen pixels, keeps the configured floor (a 1080p chat at 12 px still reads at 3x).</summary>
    public const double SmallPx = 18;
    /// <summary>The most text already at or above the target is enlarged by, and the least any text is (at 1x the bench's 32 px chat lost a count).</summary>
    public const double BigCap = 1.25;
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
    /// <summary>
    /// True: the floor holds only under <see cref="SmallPx"/> (ocr.chat_scale_to_target). Unset or false: the floor holds
    /// for every text size, as before 2026-10-24.
    /// </summary>
    public Func<bool>? ToTarget { get; init; }
    /// <summary>With <see cref="ToTarget"/>, the least factor for text of <see cref="SmallPx"/> or more (the target bar uses 2x).</summary>
    public double Least { get; init; } = BigCap;

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
            double want = Want(Median(_heights));
            if (Math.Abs(want - Factor) / Factor <= 0.1) return false;
            Factor = want;
            return true;
        }
    }

    /// <summary>The factor for text <paramref name="px"/> tall on screen, in quarter steps (tiny drifts in the measure never move it).</summary>
    public double Want(double px)
    {
        double want = TargetPx / px;
        if (ToTarget?.Invoke() == true && px >= SmallPx)
        {
            // up to the next quarter step (a hair of slack, so 24.01 px doesn't jump a step), so the text reaches the target:
            // the bench's 32 px chat read at 1x lost an "x3" that 1.25x kept, so 1.25x is also the least
            want = Math.Ceiling(want * 4 - 0.05) / 4;
            return Math.Clamp(want, Least, px >= TargetPx ? Math.Max(Least, BigCap) : Math.Max(Least, Ceiling));
        }
        want = Math.Clamp(want, Floor, Ceiling);
        return Math.Round(want * 4) / 4;
    }

    static double Median(IEnumerable<double> xs)
    {
        var s = xs.OrderBy(x => x).ToList();
        return s.Count % 2 == 1 ? s[s.Count / 2] : (s[s.Count / 2 - 1] + s[s.Count / 2]) / 2;
    }
}
