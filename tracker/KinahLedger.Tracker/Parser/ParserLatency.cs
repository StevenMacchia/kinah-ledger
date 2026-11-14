using System.Diagnostics;
using KinahLedger.Packets;

namespace KinahLedger.Parser;

/// <summary>
/// How long the Parser takes from a hit's capture to its bar and figure on screen (Steven, 2026-11-14: "the parser needs to
/// be SUPER low delay, it can not be laggy, it needs to register in real time"). The window records, at each frame it
/// draws, the time since the oldest event it hadn't drawn yet was captured; this keeps the last few thousand and gives the
/// median and the 95th percentile. The target is under 100 ms in the tab and the overlay.
/// </summary>
public sealed class ParserLatency
{
    readonly object _gate = new();
    readonly double[] _ms = new double[4096];
    int _n, _next;

    public void Add(double ms) { lock (_gate) { _ms[_next] = ms; _next = (_next + 1) % _ms.Length; if (_n < _ms.Length) _n++; } }
    public int Count { get { lock (_gate) return _n; } }
    public void Clear() { lock (_gate) { _n = 0; _next = 0; } }

    /// <summary>The median, the 95th percentile and the worst of what is kept, in ms (0 when nothing is).</summary>
    public (double P50, double P95, double Max, int N) Stats()
    {
        double[] a;
        lock (_gate) { a = new double[_n]; Array.Copy(_ms, a, _n); }
        if (a.Length == 0) return (0, 0, 0, 0);
        Array.Sort(a);
        return (Pct(a, 0.5), Pct(a, 0.95), a[^1], a.Length);
    }

    public static double Pct(double[] sorted, double p) => sorted.Length == 0 ? 0 : sorted[Math.Clamp((int)Math.Ceiling(p * sorted.Length) - 1, 0, sorted.Length - 1)];

    /// <summary>
    /// The processing path alone, for packet-test: a heavy pull's worth of events (8 members hitting every 0.1 s for five
    /// minutes, the boss's HP after each fall) handed to the Parser one by one as the packet reader hands them, each timed
    /// from the hand-over to the Parser having it and having said so; then what the window reads per frame (the record
    /// under way, copied). The per-event 99th percentile and the per-frame read must each stay under <paramref name="limitMs"/>.
    /// </summary>
    public static (bool Ok, string Line) ProcessingPath(double limitMs = 50)
    {
        var p = new BossParser(BossBook.Parse("Shattered Arkanis|Expedition|Ruthilis of Pain|final"));
        DateTime? said = null;
        p.Changed += at => said = at;
        var t = DateTime.UtcNow.AddMinutes(-10);
        p.Take(new InstanceEntered(t, ContentKind.Expedition, "Shattered Arkanis", 1));
        p.Take(new GroupChanged(t, Enumerable.Range(1, 8).Select(i => new GroupMember(i, "Member" + i, i % 2 == 0 ? "Cleric" : "Gladiator", 4_000 + i, 5_600 + i, i == 1)).ToList()));
        p.Take(new BossEngaged(t, "Ruthilis of Pain", 99));
        var per = new List<double>(30_000);
        var sw = new Stopwatch();
        long hp = 100_000_000;
        for (int i = 0; i < 3_000; i++)
        {
            var at = t.AddMilliseconds(100 * i);
            for (int m = 1; m <= 8; m++)
            {
                sw.Restart();
                p.Take(new DamageDealt(at, m, 99, 1_000));
                sw.Stop();
                if (said != at) return (false, "the Parser didn't say it had the hit");
                per.Add(sw.Elapsed.TotalMilliseconds);
            }
            hp -= 8_000;
            sw.Restart(); p.Take(new BossHealth(at, 99, hp, 100_000_000)); sw.Stop();
            per.Add(sw.Elapsed.TotalMilliseconds);
        }
        // what a frame reads: the record under way, copied, a few times
        var frames = new List<double>();
        for (int i = 0; i < 50; i++) { sw.Restart(); _ = p.Live; sw.Stop(); frames.Add(sw.Elapsed.TotalMilliseconds); }
        var a = per.ToArray(); Array.Sort(a);
        var f = frames.ToArray(); Array.Sort(f);
        double p99 = Pct(a, 0.99), frame = Pct(f, 0.95);
        bool ok = p99 <= limitMs && frame <= limitMs && p.Live?.Roster.Sum(x => x.Damage) == 3_000L * 8 * 1_000;
        return (ok, $"the Parser takes a hit as it comes: {a.Length:N0} events, median {Pct(a, 0.5) * 1000:0} µs, 99th percentile {p99 * 1000:0} µs, worst {a[^1]:0.0} ms; a frame's read {frame:0.00} ms (limit {limitMs:0} ms each)");
    }
}
