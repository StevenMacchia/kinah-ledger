using System.Drawing;
using System.Drawing.Imaging;
using System.Text.RegularExpressions;

namespace KinahLedger.Ocr;

/// <summary>A number read off the currency strip and where it starts on screen.</summary>
public sealed record CounterRead(long Value, int Left);

/// <summary>
/// Reads the currency counters at the top-left of the game (four numbers side by side) every few seconds, only when
/// the strip's pixels changed. Which counter is Kinah isn't assumed: <see cref="BalanceTracker"/> works it out from the
/// chat, as the one that grows by exactly the Kina the chat says was picked up.
/// </summary>
public sealed partial class BalanceReader
{
    /// <summary>The top-left corner that holds the currency counters. The HUD scales with the window's height, so the width follows the height too.</summary>
    public static Rectangle Strip(Rectangle game) =>
        new(game.Left, game.Top, (int)Math.Min(game.Width * 0.35, game.Height * 0.6), Math.Max(24, (int)(game.Height * 0.045)));

    [GeneratedRegex(@"^[0-9OoIl][0-9OoIl,\.]*$")]
    private static partial Regex NumberShape();

    ulong _hash;
    List<CounterRead> _last = new();

    public long Reads { get; private set; }
    /// <summary>Where the numbers were on screen in the last read (an anchor for the strip, Ocr.Anchors), and when.</summary>
    public IReadOnlyList<Rectangle> LastBoxes { get; private set; } = Array.Empty<Rectangle>();
    public DateTime LastBoxesAt { get; private set; } = DateTime.MinValue;
    public long Skipped { get; private set; }
    public BalanceTracker Tracker { get; } = new();

    /// <summary>One read: capture the strip, skip recognition when it looks the same as last time, and hand the counters to the tracker.</summary>
    /// <param name="crop">The strip around its numbers once they held still (Anchors.BalanceCrop); null: the fixed strip.</param>
    public async Task TickAsync(Rectangle game, long pickupsTotal, Rectangle? crop = null)
    {
        var strip = crop ?? Strip(game);
        using var raw = ScreenReader.Capture(strip);
        var h = Hash(raw);
        if (h == _hash && Reads > 0) { Skipped++; LastBoxesAt = DateTime.UtcNow; Tracker.Observe(_last, pickupsTotal, samePixels: true); return; }
        _hash = h;
        var lines = await ScreenReader.ReadBoxesAsync(raw, strip.Location, 3, boost: true);
        Reads++;
        _last = Counters(lines);
        LastBoxes = NumberBoxes(lines); LastBoxesAt = DateTime.UtcNow;
        Tracker.Observe(_last, pickupsTotal, samePixels: false);
    }

    /// <summary>
    /// The numbers on the strip, left to right. The recognizer sometimes splits "23,501" into "23," and "501", so a
    /// piece that ends or starts with a separator, or a three-digit piece right after another number, is joined back.
    /// Icons and anything else that isn't digits are dropped.
    /// </summary>
    public static List<CounterRead> Counters(IEnumerable<OcrLineBox> lines) =>
        Merged(lines).Select(m => new CounterRead(ChatParser.Digits(m.Text), m.Box.Left)).Where(c => c.Value >= 0 && c.Value <= 1_000_000_000_000).ToList();

    /// <summary>The numbers' boxes on screen, left to right, joined the way <see cref="Counters"/> joins them.</summary>
    public static List<Rectangle> NumberBoxes(IEnumerable<OcrLineBox> lines) => Merged(lines).Select(m => m.Box).ToList();

    static List<(string Text, Rectangle Box)> Merged(IEnumerable<OcrLineBox> lines)
    {
        var words = lines.SelectMany(l => l.Words ?? new[] { new OcrWordBox(l.Text, l.Box) })
            .Select(w => (Text: w.Text.Trim('(', ')', '[', ']', '|', ' ', ':', ';'), w.Box))
            .Where(w => w.Text.Length > 0 && NumberShape().IsMatch(w.Text) && w.Text.Any(char.IsDigit))
            .OrderBy(w => w.Box.Left).ToList();
        var merged = new List<(string Text, Rectangle Box)>();
        foreach (var w in words)
        {
            if (merged.Count > 0)
            {
                var p = merged[^1];
                int gap = w.Box.Left - p.Box.Right;
                bool joins = gap <= Math.Max(4, p.Box.Height * 0.6) &&
                             (p.Text.EndsWith(',') || p.Text.EndsWith('.') || w.Text.StartsWith(',') || w.Text.StartsWith('.') || (w.Text.Length == 3 && w.Text.All(char.IsDigit)));
                if (joins) { merged[^1] = (p.Text + w.Text, Rectangle.Union(p.Box, w.Box)); continue; }
            }
            merged.Add(w);
        }
        return merged;
    }

    /// <summary>A cheap fingerprint of the strip (every fourth pixel), so an unchanged strip isn't recognized again.</summary>
    static unsafe ulong Hash(Bitmap bmp)
    {
        var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            ulong h = 14695981039346656037;
            for (int y = 0; y < bmp.Height; y += 2)
            {
                uint* row = (uint*)((byte*)data.Scan0 + y * data.Stride);
                for (int x = 0; x < bmp.Width; x += 4) { h ^= row[x] & 0xF0F0F0; h *= 1099511628211; }
            }
            return h;
        }
        finally { bmp.UnlockBits(data); }
    }
}

/// <summary>
/// Works out which counter on the strip is the Kinah balance and keeps its last trustworthy value. Between two reads
/// the chat says how much Kina was picked up; the counter that grew by exactly that much, twice, is Kinah. A value
/// is trusted when it moved by exactly the pickups, or when the strip read the same twice in a row; a misread digit
/// is neither, so it never becomes the balance. Pure logic, so the self-test can drive it.
/// </summary>
public sealed class BalanceTracker
{
    /// <summary>How far, in screen pixels, a counter's left edge may move between reads (a growing number, a recognizer wobble).</summary>
    public int Tolerance { get; init; } = 40;
    /// <summary>Matches needed before a counter is taken to be Kinah.</summary>
    public int VotesToLock { get; init; } = 2;

    readonly List<(int Left, int Votes)> _candidates = new();
    List<CounterRead>? _prev;
    long _prevPickups;
    long? _lastRaw;

    /// <summary>Where the Kinah counter starts on screen once found; null while it's still being worked out.</summary>
    public int? KinahLeft { get; private set; }
    /// <summary>The last trusted balance; null until the counter is found and read cleanly.</summary>
    public long? Balance { get; private set; }
    public DateTime? BalanceAt { get; private set; }
    public string Note { get; private set; } = "looking for the Kinah counter: pick up some Kina";
    /// <summary>The Kinah counter showed on the strip in the last read (false while it isn't found yet, or the interface is gone).</summary>
    public bool CounterSeen { get; private set; }

    public void Observe(List<CounterRead> cur, long pickupsTotal, bool samePixels, DateTime? now = null)
    {
        var t = now ?? DateTime.UtcNow;
        long picked = _prev == null ? 0 : pickupsTotal - _prevPickups;
        if (KinahLeft == null)
        {
            if (_prev != null && picked > 0)
                foreach (var c in cur)
                {
                    var p = Near(_prev, c.Left);
                    if (p == null || c.Value - p.Value != picked) continue;
                    int i = _candidates.FindIndex(x => Math.Abs(x.Left - c.Left) <= Tolerance);
                    if (i < 0) _candidates.Add((c.Left, 1)); else _candidates[i] = (c.Left, _candidates[i].Votes + 1);
                }
            var best = _candidates.OrderByDescending(x => x.Votes).FirstOrDefault();
            // two counters that rise with every pickup alike (the strip's 2nd and 3rd figures, 2026-10-24: both +15,660 over a
            // farming run, so neither ever won and the counter check never compared once): the rightmost of the tied is taken,
            // the one that doesn't move when Kinah is spent (kinah-bench kinah-counter.md: only the 2nd fell 38,000 on a spend)
            var tied = _candidates.Where(x => x.Votes == best.Votes && x.Votes >= VotesToLock).ToList();
            if (tied.Count > 1 && best.Votes >= VotesToLock + 1) best = tied.OrderByDescending(x => x.Left).First();
            if (best.Votes >= VotesToLock && (_candidates.Count(x => x.Votes >= VotesToLock) == 1 || tied.Count > 1 && best.Votes >= VotesToLock + 1))
            {
                KinahLeft = best.Left;
                _lastRaw = _prev == null ? null : Near(_prev, best.Left)?.Value;   // this read just matched the pickups, so it counts
                Note = "Kinah counter found";
            }
            else if (_candidates.Count > 0) Note = "Kinah counter: one match so far, waiting for another pickup";
        }
        if (KinahLeft is int left && Near(cur, left) is { } k)
        {
            KinahLeft = k.Left;   // follow the left edge as the number grows a digit
            // the same pixels, or a move of exactly the Kina picked up since the last read (none: the same value twice)
            // ...or the same value read twice running: the Kina witness (Session/KinaWitness.cs) needs readings while the count
            // lags the counter (a Kina line waits for it), and a misread digit is seldom read the same way twice
            bool trusted = samePixels || (_lastRaw is long r && _prev != null && picked >= 0 && k.Value - r == picked) || (_lastRaw is long r2 && k.Value == r2);
            if (trusted) { Balance = k.Value; BalanceAt = t; Note = "Kinah balance read"; }
            _lastRaw = k.Value;
        }
        CounterSeen = KinahLeft is int seenLeft && Near(cur, seenLeft) != null;
        _prev = cur; _prevPickups = pickupsTotal;
    }

    CounterRead? Near(List<CounterRead> list, int left) => list.Where(c => Math.Abs(c.Left - left) <= Tolerance).OrderBy(c => Math.Abs(c.Left - left)).FirstOrDefault();
}
