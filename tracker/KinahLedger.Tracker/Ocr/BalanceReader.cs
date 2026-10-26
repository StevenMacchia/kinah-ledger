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
    DateTime _wordsAt = DateTime.MinValue;

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
        var lines = await ReadNumbersAsync(raw, strip.Location);
        Reads++;
        _last = Counters(lines);
        LastBoxes = NumberBoxes(lines); LastBoxesAt = DateTime.UtcNow;
        // once a minute, what the recognizer saw on the strip and which numbers came of it: reads.log kept no strip reads
        // before 2026-10-26, so why the counter went unread all of 2026-10-25 could only be inferred
        if ((LastBoxesAt - _wordsAt).TotalSeconds >= 60)
        {
            _wordsAt = LastBoxesAt;
            var words = lines.SelectMany(l => l.Words ?? new[] { new OcrWordBox(l.Text, l.Box) }).Select(w => $"{w.Text}@{w.Box.Left - strip.Left}");
            CrashLog.Note($"balance strip ({(crop == null ? "fixed" : "anchored")} {strip.Width}x{strip.Height}): words {string.Join(" ", words)} -> numbers {string.Join(" ", _last.Select(c => $"{c.Value:N0}@{c.Left - strip.Left}"))}");
        }
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

    /// <summary>A few characters that aren't digits in front of a number: the counter's icon read as part of it.</summary>
    [GeneratedRegex(@"^[^0-9]{1,3}(?=[0-9][0-9,\.]*$)")]
    private static partial Regex IconInFront();

    /// <summary>A word with the icon in front of its number read as letters ("@29,281,753") is that number.</summary>
    static string WithoutIcon(string t) => NumberShape().IsMatch(t) ? t : IconInFront().Match(t) is { Success: true } m ? t[m.Length..] : t;

    static List<(string Text, Rectangle Box)> Merged(IEnumerable<OcrLineBox> lines)
    {
        var words = lines.SelectMany(l => l.Words ?? new[] { new OcrWordBox(l.Text, l.Box) })
            .Select(w => (Text: WithoutIcon(w.Text.Trim('(', ')', '[', ']', '|', ' ', ':', ';')), w.Box))
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
        // a lone digit is an icon read as one (the strip's bag of coins read "0" at its left, 2026-10-24 pictures), never Kinah worth checking
        return merged.Where(m => m.Text.Count(c => char.IsDigit(c) || c is 'O' or 'o' or 'I' or 'l') >= 2).ToList();
    }

    /// <summary>
    /// The strip's white digits alone, dark on light: a coloured pixel (max channel minus min channel over
    /// <paramref name="satMax"/>) is background, any other keeps its darkest channel, stretched from <paramref name="lo"/>
    /// to <paramref name="hi"/>. The counters' icons are gold and blue: gone, they can't be read into the numbers.
    /// </summary>
    public static unsafe Bitmap WhiteText(Bitmap src, int satMax = 60, int lo = 90, int hi = 230)
    {
        var dst = new Bitmap(src.Width, src.Height, PixelFormat.Format24bppRgb);
        var rect = new Rectangle(0, 0, src.Width, src.Height);
        var rs = src.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var ds = dst.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
        try
        {
            for (int y = 0; y < src.Height; y++)
            {
                byte* s = (byte*)rs.Scan0 + y * rs.Stride;
                byte* d = (byte*)ds.Scan0 + y * ds.Stride;
                for (int x = 0; x < src.Width; x++, s += 4, d += 3)
                {
                    int mx = Math.Max(s[0], Math.Max(s[1], s[2])), mn = Math.Min(s[0], Math.Min(s[1], s[2]));
                    int v = mx - mn > satMax ? 0 : (mn - lo) * 255 / (hi - lo);
                    v = v < 0 ? 0 : v > 255 ? 255 : v;
                    byte o = (byte)(255 - v);
                    d[0] = o; d[1] = o; d[2] = o;
                }
            }
        }
        finally { src.UnlockBits(rs); dst.UnlockBits(ds); }
        return dst;
    }

    /// <summary>
    /// The strip read one number at a time (2026-10-26). Read whole, the recognizer skipped the two Kinah figures on most
    /// reads (the 131029 pictures: "0 7,811 011.8254", the gold and blue icons glued to the digits or the long figures
    /// dropped), which is why the counter was never found on 2026-10-25. Here the icons are taken out
    /// (<see cref="WhiteText"/>), the white digits are cut into runs of columns with ink, a gap wider than a seventh of
    /// the strip's height ending a run, and each run is read on its own. A run that reads as nothing is left out; when no
    /// run gives a number, the whole strip is read as before.
    /// </summary>
    public static async Task<List<OcrLineBox>> ReadNumbersAsync(Bitmap raw, Point origin, int lo = 150)
    {
        using var wt = WhiteText(raw, 60, lo);
        // each run cut to its ink rows; one narrower than 0.4 of the strip's height is what's left of an icon's white
        // letter, not a counter (a counter under 100 isn't Kinah worth checking)
        var parts = InkRuns(wt).Where(r => r.Width >= wt.Height * 0.4).Select(r => Rows(wt, r)).Where(r => r.Height > 0).ToList();
        if (parts.Count == 0) return await ScreenReader.ReadBoxesAsync(raw, origin, 3, boost: true);
        // the runs stacked on a white sheet, one per line with a line's gap between, as a column of numbers: read alone,
        // a single number with nothing around it was skipped as often as it was read
        int band = parts.Max(p => p.Height), m = Math.Max(8, band), slot = band * 2;
        using var sheet = new Bitmap(parts.Max(p => p.Width) + 2 * m, 2 * m + slot * parts.Count, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(sheet))
        {
            g.Clear(Color.White);
            for (int i = 0; i < parts.Count; i++) g.DrawImage(wt, new Rectangle(m, m + i * slot, parts[i].Width, parts[i].Height), parts[i], GraphicsUnit.Pixel);
        }
        var found = new List<OcrLineBox>();
        foreach (var line in await ScreenReader.ReadBoxesAsync(sheet, Point.Empty, 3))
        {
            // each word back to the strip, by the line of the sheet it sits on
            var words = new List<OcrWordBox>();
            foreach (var w in line.Words ?? new[] { new OcrWordBox(line.Text, line.Box) })
            {
                int i = Math.Clamp((w.Box.Top + w.Box.Height / 2 - m) / slot, 0, parts.Count - 1);
                int dx = origin.X + parts[i].X - m, dy = origin.Y + parts[i].Y - (m + i * slot);
                words.Add(new OcrWordBox(w.Text, new Rectangle(w.Box.X + dx, w.Box.Y + dy, w.Box.Width, w.Box.Height)));
            }
            if (words.Count > 0) found.Add(new OcrLineBox(string.Join(" ", words.Select(x => x.Text)), words.Select(x => x.Box).Aggregate(Rectangle.Union), words));
        }
        if (Counters(found).Count > 0) return found;
        return await ScreenReader.ReadBoxesAsync(raw, origin, 3, boost: true);
    }

    /// <summary>The rows of <paramref name="run"/> with ink, padded a little: the run cut to its text.</summary>
    static unsafe Rectangle Rows(Bitmap wt, Rectangle run)
    {
        int top = -1, bottom = -1;
        var data = wt.LockBits(new Rectangle(0, 0, wt.Width, wt.Height), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
        try
        {
            for (int y = run.Top; y < run.Bottom; y++)
            {
                byte* row = (byte*)data.Scan0 + y * data.Stride;
                bool any = false;
                for (int x = run.Left; x < run.Right && !any; x++) any = row[x * 3] < 100;
                if (any) { if (top < 0) top = y; bottom = y; }
            }
        }
        finally { wt.UnlockBits(data); }
        if (top < 0) return Rectangle.Empty;
        int pad = Math.Max(2, (bottom - top) / 6);
        int t = Math.Max(run.Top, top - pad), b = Math.Min(run.Bottom, bottom + 1 + pad);
        return new Rectangle(run.X, t, run.Width, b - t);
    }

    /// <summary>The strip's runs of columns with dark (text) pixels in a <see cref="WhiteText"/> picture, padded, left to right, full height.</summary>
    public static unsafe List<Rectangle> InkRuns(Bitmap wt)
    {
        int w = wt.Width, h = wt.Height, gap = Math.Max(4, h / 7), pad = Math.Max(3, h / 12);
        var ink = new bool[w];
        var data = wt.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
        try
        {
            for (int x = 0; x < w; x++)
            {
                int n = 0;
                for (int y = 0; y < h && n < 2; y++) if (((byte*)data.Scan0 + y * data.Stride)[x * 3] < 100) n++;
                ink[x] = n >= 2;
            }
        }
        finally { wt.UnlockBits(data); }
        var runs = new List<Rectangle>();
        int start = -1, last = -1;
        for (int x = 0; x <= w; x++)
        {
            bool on = x < w && ink[x];
            if (on) { if (start < 0) start = x; last = x; continue; }
            if (start >= 0 && (x == w || x - last > gap))
            {
                if (last - start + 1 >= Math.Max(4, h / 12))
                {
                    int l = Math.Max(0, start - pad), r = Math.Min(w, last + 1 + pad);
                    runs.Add(new Rectangle(l, 0, r - l, h));
                }
                start = -1;
            }
        }
        return runs;
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
/// Works out which counter on the strip is the Kinah balance and keeps its last trustworthy value. The counter that
/// rose by exactly the Kina the chat counted, between two steady readings, twice, is Kinah. A value is trusted when it
/// moved by exactly the pickups since the last read, or when the strip read the same twice in a row; a misread digit is
/// neither, so it never becomes the balance. Pure logic, so the self-test can drive it.
///
/// Why steady readings (2026-10-26): the counter changes in the frame its chat line appears (kinah-bench timing.md) and
/// the chat count follows 0.8 to 1.9 s later (median 1.1 s over 2026-10-25's 1,395 Kina lines after 12:25: the line's
/// first read, then the vote), while the strip is read every 3 s. In a fight a line is in flight at most reads, so two
/// consecutive reads seldom differ by exactly the Kina counted between them. A counter read the same twice has held
/// still for a read's interval, long enough for the chat to have counted what raised it; two such readings bound a
/// window whose rise is compared with the Kina counted in it. A fall is Kinah spent (the Market's cut, a repair, a
/// teleport): never a match, never forced. Each window judged is one line in reads.log.
/// </summary>
public sealed class BalanceTracker
{
    /// <summary>How far, in screen pixels, a counter's left edge may move between reads (a growing number, a recognizer wobble).</summary>
    public int Tolerance { get; init; } = 40;
    /// <summary>Matches needed before a counter is taken to be Kinah.</summary>
    public int VotesToLock { get; init; } = 2;
    /// <summary>Two steady readings further apart than this aren't compared (the strip wasn't read steadily in between: a loading screen, a menu).</summary>
    public double MaxWindowSeconds { get; init; } = 120;
    /// <summary>Where each window's decision goes (reads.log; the self-test catches them).</summary>
    public Action<string>? Log { get; set; } = CrashLog.Note;

    readonly List<(int Left, int Votes)> _candidates = new();
    // per counter on the strip, its last steady reading: the value, the Kina counted then, and when
    sealed class Steady { public int Left; public long Value; public long Pickups; public DateTime At; }
    readonly List<Steady> _steady = new();
    List<CounterRead>? _prev;
    long _prevPickups;
    long? _lastRaw;

    /// <summary>Windows between steady readings whose rise was exactly the Kina counted in them (the votes), that didn't match, and that fell.</summary>
    public int WindowsAgreed { get; private set; }
    public int WindowsMismatched { get; private set; }
    public int WindowsFell { get; private set; }

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
            Windows(cur, pickupsTotal, samePixels, t);
            var best = _candidates.OrderByDescending(x => x.Votes).FirstOrDefault();
            // two counters that rise with every pickup alike (the strip's 2nd and 3rd figures, 2026-10-24: both +15,660 over a
            // farming run, so neither ever won and the counter check never compared once): the rightmost of the tied is taken,
            // the one that doesn't move when Kinah is spent (kinah-bench kinah-counter.md: only the 2nd fell 38,000 on a spend)
            var tied = _candidates.Where(x => x.Votes == best.Votes && x.Votes >= VotesToLock).ToList();
            if (tied.Count > 1 && best.Votes >= VotesToLock + 1) best = tied.OrderByDescending(x => x.Left).First();
            if (best.Votes >= VotesToLock && (_candidates.Count(x => x.Votes >= VotesToLock) == 1 || tied.Count > 1 && best.Votes >= VotesToLock + 1))
            {
                KinahLeft = best.Left;
                // this read is a steady one that just matched the pickups (the same value as the last read), so it counts
                _lastRaw = _prev == null ? null : Near(_prev, best.Left)?.Value;
                Note = "Kinah counter found";
                Log?.Invoke($"Kinah counter found: the number at x {best.Left}, its rise agreed with the chat's Kina in {best.Votes} windows between steady readings");
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

    /// <summary>
    /// While the Kinah counter isn't known: each counter read the same as last time (or the same pixels) is a steady
    /// reading. Against that counter's previous steady reading, a rise of exactly the Kina counted in between is a vote;
    /// any other rise, a fall or a window too long is not, and each is one line. The same steady value again moves the
    /// window's start up to now: Kina counted while the counter held still was a late count of an earlier rise (or a line
    /// the counter never showed), and belongs to no later window.
    /// </summary>
    void Windows(List<CounterRead> cur, long pickups, bool samePixels, DateTime t)
    {
        foreach (var c in cur)
        {
            var p = _prev == null ? null : Near(_prev, c.Left);
            if (!samePixels && (p == null || p.Value != c.Value)) continue;   // moving, or not read last time: not steady
            var s = _steady.FirstOrDefault(x => Math.Abs(x.Left - c.Left) <= Tolerance);
            if (s == null) { _steady.Add(new Steady { Left = c.Left, Value = c.Value, Pickups = pickups, At = t }); continue; }
            if (s.Value == c.Value) { s.Left = c.Left; s.Pickups = pickups; s.At = t; continue; }
            long rise = c.Value - s.Value, counted = pickups - s.Pickups;
            double secs = (t - s.At).TotalSeconds;
            string window = $"Kinah counter search, the number at x {c.Left}: between steady readings at {s.At.ToLocalTime():HH:mm:ss} and {t.ToLocalTime():HH:mm:ss}";
            if (secs > MaxWindowSeconds) Log?.Invoke($"{window} ({secs:0} s, too long to judge) it went from {s.Value:N0} to {c.Value:N0}: not compared");
            else if (rise < 0)
            {
                WindowsFell++;
                Log?.Invoke($"{window} it fell {-rise:N0} (Kinah spent: the Market's cut, a repair, a teleport) while the chat counted {counted:N0}: no match");
            }
            else if (counted > 0 && rise == counted)
            {
                WindowsAgreed++;
                int i = _candidates.FindIndex(x => Math.Abs(x.Left - c.Left) <= Tolerance);
                if (i < 0) _candidates.Add((c.Left, 1)); else _candidates[i] = (c.Left, _candidates[i].Votes + 1);
                Log?.Invoke($"{window} it rose {rise:N0} and the chat counted {counted:N0}: agrees ({_candidates.First(x => Math.Abs(x.Left - c.Left) <= Tolerance).Votes} of {VotesToLock})");
            }
            else
            {
                WindowsMismatched++;
                Log?.Invoke($"{window} it rose {rise:N0} and the chat counted {counted:N0}: no match");
            }
            s.Left = c.Left; s.Value = c.Value; s.Pickups = pickups; s.At = t;
        }
    }

    CounterRead? Near(List<CounterRead> list, int left) => list.Where(c => Math.Abs(c.Left - left) <= Tolerance).OrderBy(c => Math.Abs(c.Left - left)).FirstOrDefault();
}
