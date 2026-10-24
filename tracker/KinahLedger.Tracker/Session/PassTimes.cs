namespace KinahLedger.Session;

/// <summary>
/// Where a pass's time goes, region by region, for the once-a-minute audit line (2026-10-24: 670 to 1220 ms a pass on
/// Steven's 5120x2160 window against a 260 ms design target, and no way to tell which reader took it). Each reader's
/// time is added as it runs; the line gives each region's total over the minute divided by the passes, in ms. The loot
/// list is read beside the chat, so the regions add up to more than the pass; balance (the watcher's own 10 s timer) and
/// decoder (its own thread) are off the pass and shown at the same per-pass rate. Thread-safe.
/// </summary>
public sealed class PassTimes
{
    public static readonly string[] Regions = { "chat", "list", "target", "balance", "craft", "grid", "nameplates", "header", "anchors", "scene", "decoder" };

    readonly double[] _ms = new double[Regions.Length];
    readonly object _lock = new();

    public void Add(string region, double ms)
    {
        int i = Array.IndexOf(Regions, region);
        if (i < 0 || double.IsNaN(ms) || ms < 0) return;
        lock (_lock) _ms[i] += ms;
    }

    /// <summary>The total so far for one region, ms (tests).</summary>
    public double Total(string region) { int i = Array.IndexOf(Regions, region); lock (_lock) return i < 0 ? 0 : _ms[i]; }

    /// <summary>"chat 310 · list 180 · …": each region's ms per pass over <paramref name="passes"/> passes.</summary>
    public string Line(int passes)
    {
        lock (_lock) return string.Join(" · ", Regions.Select((r, i) => $"{r} {(passes > 0 ? _ms[i] / passes : 0):0}"));
    }

    public void Reset() { lock (_lock) Array.Clear(_ms); }

    /// <summary>Runs <paramref name="work"/> and adds its time to <paramref name="region"/>, also when it throws.</summary>
    public async Task Time(string region, Func<Task> work)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try { await work(); }
        finally { Add(region, sw.Elapsed.TotalMilliseconds); }
    }

    public async Task<T> Time<T>(string region, Func<Task<T>> work)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try { return await work(); }
        finally { Add(region, sw.Elapsed.TotalMilliseconds); }
    }

    public void Time(string region, Action work)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try { work(); }
        finally { Add(region, sw.Elapsed.TotalMilliseconds); }
    }

    /// <summary>The audit line's pass part: "57 passes, 1016 ms each (per pass: chat 310 · list 180 · … ms)".</summary>
    public static string AuditPart(int passes, double passMs, PassTimes times) =>
        $"{passes} passes, {(passes > 0 ? passMs / passes : 0):0} ms each (per pass: {times.Line(passes)} ms)";
}
