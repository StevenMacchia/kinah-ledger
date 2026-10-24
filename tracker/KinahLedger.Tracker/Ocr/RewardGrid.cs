using System.Drawing;
using System.Drawing.Imaging;
using System.Net.Http;
using System.Runtime.InteropServices;
using KinahLedger.Data;

namespace KinahLedger.Ocr;

/// <summary>
/// A small fingerprint of every item icon the game uses (the icons from NCSoft's own game-data server, the same art the
/// game draws): 16×16 RGBA each, so a reward grid's slot can be matched to an icon. Built once with "icon-index" into
/// data/icon-index.bin, built into the exe. An icon is shared by up to 20 items; the slot's grade and the chat narrow it.
/// </summary>
public static class IconIndex
{
    public const int N = 16;
    public sealed record Entry(string Key, byte[] Rgba);

    static List<Entry>? _all;
    public static List<Entry> All => _all ??= Load();

    /// <summary>The file name of an icon URL, its key ("Icon_Equip_AR_L_0001_T04_Gloves").</summary>
    public static string KeyOf(string url) => Path.GetFileNameWithoutExtension(Uri.TryCreate(url, UriKind.Absolute, out var u) ? u.AbsolutePath : url);

    static List<Entry> Load()
    {
        var list = new List<Entry>();
        try
        {
            using var s = typeof(IconIndex).Assembly.GetManifestResourceStream("icon-index.bin");
            if (s == null) return list;
            using var r = new BinaryReader(s);
            int count = r.ReadInt32();
            for (int i = 0; i < count; i++) list.Add(new Entry(r.ReadString(), r.ReadBytes(N * N * 4)));
        }
        catch (Exception) { /* without the index the grid's items can't be named */ }
        return list;
    }

    /// <summary>A picture's area as a 16×16 grid of RGB, the way an icon is compared (Distance).</summary>
    public static double[] Cell(Bitmap f, Rectangle area)
    {
        var cell = new double[N * N * 3];
        using var small = new Bitmap(N, N, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(small)) { g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic; g.DrawImage(f, new Rectangle(0, 0, N, N), area, GraphicsUnit.Pixel); }
        for (int y = 0; y < N; y++) for (int x = 0; x < N; x++) { var c = small.GetPixel(x, y); int i = (y * N + x) * 3; cell[i] = c.R; cell[i + 1] = c.G; cell[i + 2] = c.B; }
        return cell;
    }

    /// <summary>
    /// How far a picture's grid (Cell) is from an icon laid on the backing colour <paramref name="bg"/>: the mean difference
    /// per channel, the icon's own pixels counting most. Under about 70 is the same art (RewardGrid's sure distance).
    /// </summary>
    public static double Distance(double[] cell, Color bg, Entry e) => Distance(cell, bg, e, null);

    /// <summary>The same, leaving out the grid cells <paramref name="skip"/> marks (a badge the game draws over the art).</summary>
    public static double Distance(double[] cell, Color bg, Entry e, bool[]? skip)
    {
        double sum = 0, wsum = 0;
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                // the lower corners carry the count and the lock: they say nothing about the icon
                if (y >= N - 4 && (x < 4 || x >= N - 6)) continue;
                if (skip != null && skip[y * N + x]) continue;
                int i = (y * N + x), j = i * 4, k = i * 3;
                double a = e.Rgba[j + 3] / 255.0;
                double r = e.Rgba[j] * a + bg.R * (1 - a), g = e.Rgba[j + 1] * a + bg.G * (1 - a), bl = e.Rgba[j + 2] * a + bg.B * (1 - a);
                double w = 0.35 + a;   // the icon's own pixels count most
                sum += w * (Math.Abs(r - cell[k]) + Math.Abs(g - cell[k + 1]) + Math.Abs(bl - cell[k + 2]));
                wsum += w;
            }
        return sum / Math.Max(wsum, 1);
    }

    static Dictionary<string, List<Entry>>? _byKey;
    /// <summary>The fingerprints of one icon key (an icon can have two: the file and its art as drawn in a slot).</summary>
    public static IReadOnlyList<Entry> Of(string key) => (_byKey ??= All.GroupBy(e => e.Key).ToDictionary(g => g.Key, g => g.ToList())).TryGetValue(key, out var l) ? l : Array.Empty<Entry>();

    /// <summary>A 16×16 RGBA fingerprint of an icon image.</summary>
    public static byte[] Fingerprint(Bitmap icon)
    {
        using var small = new Bitmap(N, N, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(small))
        {
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.Clear(Color.Transparent);
            g.DrawImage(icon, new Rectangle(0, 0, N, N));
        }
        var px = new byte[N * N * 4];
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                var c = small.GetPixel(x, y);
                int i = (y * N + x) * 4;
                px[i] = c.R; px[i + 1] = c.G; px[i + 2] = c.B; px[i + 3] = c.A;
            }
        return px;
    }

    /// <summary>An icon file as a bitmap: PNG directly, WebP (some MetaBot icons) through Windows' own image decoder.</summary>
    static async Task<Bitmap> OpenAsync(string file)
    {
        if (!file.EndsWith(".webp", StringComparison.OrdinalIgnoreCase)) return new Bitmap(file);
        var sf = await Windows.Storage.StorageFile.GetFileFromPathAsync(Path.GetFullPath(file));
        using var stream = await sf.OpenReadAsync();
        var dec = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(stream);
        using var soft = await dec.GetSoftwareBitmapAsync(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, Windows.Graphics.Imaging.BitmapAlphaMode.Straight);
        var px = new byte[4 * soft.PixelWidth * soft.PixelHeight];
        soft.CopyToBuffer(System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions.AsBuffer(px));
        var bmp = new Bitmap(soft.PixelWidth, soft.PixelHeight, PixelFormat.Format32bppArgb);
        var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        Marshal.Copy(px, 0, data.Scan0, px.Length);
        bmp.UnlockBits(data);
        return bmp;
    }

    /// <summary>
    /// A fingerprint from a reward slot as the game draws it (a saved picture), under the icon key of the item it shows:
    /// some art in a slot differs from the icon file (the Kina coin), so a real slot is the surer reference. The slot's
    /// backing is part of it, so it is compared as is (fully opaque), the way Match lays an icon on the slot's colour.
    /// </summary>
    public static Entry FromSlot(string key, Bitmap picture, Rectangle slot)
    {
        int m = (int)Math.Round(slot.Width * 0.04);
        using var crop = picture.Clone(Rectangle.Inflate(slot, -m, -m), PixelFormat.Format32bppArgb);
        return new Entry(key, Fingerprint(crop));
    }

    /// <summary>Downloads every distinct icon of the item list (cached in <paramref name="cacheDir"/>) and writes the index.</summary>
    public static async Task<int> BuildAsync(IEnumerable<string> urls, string cacheDir, string outFile, IEnumerable<Entry>? slots = null)
    {
        Directory.CreateDirectory(cacheDir);
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("KinahLedger-icon-index (+https://kinahledger.com)");
        var distinct = urls.Where(u => u.StartsWith("http")).Distinct().ToList();
        var entries = new System.Collections.Concurrent.ConcurrentBag<Entry>();
        using var gate = new SemaphoreSlim(6);
        int done = 0;
        await Task.WhenAll(distinct.Select(async url =>
        {
            await gate.WaitAsync();
            try
            {
                var key = KeyOf(url);
                var ext = Path.GetExtension(new Uri(url).AbsolutePath); if (ext.Length == 0) ext = ".png";
                var file = Path.Combine(cacheDir, key + ext);
                if (!File.Exists(file)) await File.WriteAllBytesAsync(file, await http.GetByteArrayAsync(url));
                using var bmp = await OpenAsync(file);
                entries.Add(new Entry(key, Fingerprint(bmp)));
            }
            catch (Exception ex) { Console.Error.WriteLine($"skipped {url}: {ex.Message}"); }
            finally { gate.Release(); if (Interlocked.Increment(ref done) % 200 == 0) Console.WriteLine($"  {done} of {distinct.Count}"); }
        }));
        foreach (var e in slots ?? Enumerable.Empty<Entry>()) entries.Add(e);
        using var w = new BinaryWriter(File.Create(outFile));
        var sorted = entries.OrderBy(e => e.Key, StringComparer.Ordinal).ToList();
        w.Write(sorted.Count);
        foreach (var e in sorted) { w.Write(e.Key); w.Write(e.Rgba); }
        return sorted.Count;
    }
}

/// <summary>
/// The reward table at the end of an expedition ("Obtained Items", "Reward collected."): a grid of square slots, eight to
/// a row, centred on the screen, each with the item's icon on its grade's colour and its count in the lower right
/// (Steven, 2026-10-22: the chat shows only the last few lines of it). Each slot is matched to an icon by its picture.
/// </summary>
public static class RewardGrid
{
    /// <summary>One slot as read: where, its grade by colour, its count as read, and the closest icons, best first.</summary>
    public sealed record Slot(Rectangle Box, string Grade, string CountText, long Count, List<(string Key, double Distance)> Icons);

    /// <summary>
    /// Several reads of one reward table, slot by slot: the number of slots most reads saw; for each slot, the icon most
    /// reads put first (at its closest), the count most reads agree on, and the grade most reads saw. A single read's slip
    /// (an empty count, a glare on one icon) is outvoted (2026-10-23 Urugugu: five slots, every count read empty).
    /// </summary>
    /// <summary>The last two reads agree on the slots, and every slot of both is a sure match: the table has stopped moving.</summary>
    public static bool Settled(List<List<Slot>> reads, double sure)
    {
        if (reads.Count < 2) return false;
        var a = reads[^2]; var b = reads[^1];
        if (a.Count == 0 || a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
        {
            if (a[i].Icons.Count == 0 || b[i].Icons.Count == 0) return false;
            if (a[i].Icons[0].Key != b[i].Icons[0].Key || a[i].Icons[0].Distance > sure || b[i].Icons[0].Distance > sure) return false;
            if (a[i].Count != b[i].Count) return false;
        }
        return true;
    }

    /// <summary>The same, where only sure reads of a slot (closest icon within <paramref name="sure"/>) vote on it: early reads of a table still fading in can't outvote one clean read.</summary>
    public static List<Slot> Vote(List<List<Slot>> reads, double sure)
    {
        var seen = reads.Where(r => r.Count > 0).ToList();
        if (seen.Count == 0) return new List<Slot>();
        // the slot count of the reads with the most sure slots
        // slots appear as the table slides in (14, then 27): the most slots two reads agree on, else the last read's
        int n = seen.GroupBy(r => r.Count).Where(g => g.Count() >= 2).Select(g => g.Key).DefaultIfEmpty(seen[^1].Count).Max();
        var same = seen.Where(r => r.Count == n).ToList();
        var res = new List<Slot>();
        for (int i = 0; i < n; i++)
        {
            var col = same.Select(r => r[i]).ToList();
            var sureOnes = col.Where(s => s.Icons.Count > 0 && s.Icons[0].Distance <= sure).ToList();
            res.Add(Vote(new List<List<Slot>> { }.Concat((sureOnes.Count > 0 ? sureOnes : col).Select(s => new List<Slot> { s })).ToList())[0]);
        }
        return res;
    }

    public static List<Slot> Vote(List<List<Slot>> reads)
    {
        var seen = reads.Where(r => r.Count > 0).ToList();
        if (seen.Count == 0) return new List<Slot>();
        int n = seen.GroupBy(r => r.Count).OrderByDescending(g => g.Count()).ThenByDescending(g => g.Key).First().Key;
        var same = seen.Where(r => r.Count == n).ToList();
        var res = new List<Slot>();
        for (int i = 0; i < n; i++)
        {
            var col = same.Select(r => r[i]).ToList();
            var byIcon = col.Where(s => s.Icons.Count > 0).GroupBy(s => s.Icons[0].Key)
                            .OrderByDescending(g => g.Count()).ThenBy(g => g.Min(s => s.Icons[0].Distance)).FirstOrDefault();
            var pick = byIcon == null ? col[0] : byIcon.OrderBy(s => s.Icons[0].Distance).First();
            var counts = col.Where(s => s.Count > 0).GroupBy(s => s.Count).OrderByDescending(g => g.Count()).FirstOrDefault();
            long count = counts?.Key ?? pick.Count;
            string text = counts?.First().CountText ?? pick.CountText;
            string grade = col.GroupBy(s => s.Grade).OrderByDescending(g => g.Count()).First().Key;
            res.Add(pick with { Count = count, CountText = text, Grade = grade });
        }
        return res;
    }

    // geometry measured on a 5120×2160 frame (2026-10-22 15:22): a slot is 135 px with a 148 px pitch, i.e. in screen heights
    public const double SlotH = 135.0 / 2160, PitchH = 148.0 / 2160;

    /// <summary>
    /// Reads the grid in a game frame. <paramref name="top"/> is the y (in the frame) just under "Reward collected.";
    /// the grid is centred on the frame. Returns the slots found, row by row.
    /// </summary>
    public static async Task<List<Slot>> ReadAsync(Bitmap frame, int top)
    {
        int h = frame.Height, s = (int)Math.Round(SlotH * h), p = (int)Math.Round(PitchH * h);
        int x0 = frame.Width / 2 - 4 * p + (p - s) / 2;
        // the first row's top edge, found where it is: the header's line gives it within a few pixels, and 7 px low put the
        // corners in the gap (2026-10-23 Urugugu: the gold slot read Common, its "4" cut off, the row ended at slot 7)
        top = RefineTop(frame, top, x0, s, p);
        var slots = new List<Slot>();
        for (int row = 0; row < 6; row++)
        {
            int y = top + row * p;
            if (y + s > h) break;
            int found = 0;
            for (int col = 0; col < 8; col++)
            {
                var box = new Rectangle(x0 + col * p, y, s, s);
                // the slots fill each row from the left: the first gap ends the row, and a row with none ends the grid
                if ((!IsSlot(frame, box) && TopEdge(frame, box) < EdgeMin) || Flat(frame, box) || Contrast(frame, box) < ContrastMin) break;
                found++;
                var bg = Background(frame, box);
                var grade = GradeOf(bg);
                // the count, white in the lower right; a long one ("600.0K") reaches left past the middle
                var corner = new Rectangle(box.Left + box.Width / 8, box.Top + box.Height * 3 / 5, box.Width * 7 / 8 - 2, box.Height * 2 / 5 - 2);
                // no white figures there: the game shows no count for one of an item
                string num = "";
                bool figures = false, glyphFigure = false;
                if (HasFigures(frame, corner))
                {
                    figures = true;
                    // the reader skips a lone figure ("4", "6") but reads it after a word: "Qty 4" (2026-10-23 Urugugu: 4, 2, 6, 3
                    // and 5 read as 1, 1, 7, 11 and 1 without it)
                    using (var cq = WhiteText(frame, corner, prefix: true))
                        foreach (var scale in new[] { 2, 3, 1 })
                        {
                            var q = string.Join(" ", (await ScreenReader.ReadBoxesAsync(cq, corner.Location, scale)).Select(l => l.Text.Trim())).Trim();
                            var after = System.Text.RegularExpressions.Regex.Match(q, @"q\W?t\W?y\W*(?<n>[0-9OoIl|SB.,]+\s?[KkMm]?)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                            if (after.Success && System.Text.RegularExpressions.Regex.IsMatch(after.Groups["n"].Value, @"\d")) { num = after.Groups["n"].Value; break; }
                        }
                    if (!System.Text.RegularExpressions.Regex.IsMatch(num, @"\d"))
                        using (var cb = WhiteText(frame, corner))
                            foreach (var scale in new[] { 2, 3, 4, 1 })
                            {
                                num = string.Join(" ", (await ScreenReader.ReadBoxesAsync(cb, corner.Location, scale)).Select(l => l.Text.Trim())).Trim();
                                if (System.Text.RegularExpressions.Regex.IsMatch(num, @"\d")) break;
                            }
                }
                // the reader skips a lone digit: then its shape, in the tight lower-right corner where a count ends
                if (!System.Text.RegularExpressions.Regex.IsMatch(num, @"\d"))
                {
                    var tight = new Rectangle(box.Left + box.Width * 11 / 20, box.Top + box.Height * 13 / 20, box.Width * 2 / 5, box.Height * 3 / 10);
                    num = Glyphs.Read(frame, tight, out glyphFigure);
                }
                // figures the reader can't tell are a count not read (0), never one assumed (2026-10-23: four counts read as 1)
                long count = figures && glyphFigure && !System.Text.RegularExpressions.Regex.IsMatch(num, @"\d") ? 0 : ParseCount(num);
                slots.Add(new Slot(box, grade, num, count, Match(frame, box, bg)));
            }
            if (found == 0 || found < 8 && row > 0 && slots.Count > 0 && found < 8) { if (found == 0) break; }
            if (found < 8) break;   // a short row is the last one
        }
        return slots;
    }

    /// <summary>
    /// The count's white figures alone, black on white and padded: the slot's art behind them confuses the reader, and a
    /// lone digit is read far more often this way.
    /// </summary>
    static Bitmap WhiteText(Bitmap f, Rectangle area, bool prefix = false)
    {
        const int pad = 24;
        // with a word drawn before the figures, in black like them, a little shorter than the slot's corner
        int lead = prefix ? area.Height * 2 : 0;
        var outBmp = new Bitmap(area.Width + 2 * pad + lead, area.Height + 2 * pad, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(outBmp))
        {
            g.Clear(Color.White);
            if (prefix)
            {
                // the figures sit low in the corner; the word on their line, about their height
                float em = Math.Max(10, area.Height * 0.42f);
                using var font = new Font("Arial", em, FontStyle.Bold, GraphicsUnit.Pixel);
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                g.DrawString("Qty", font, Brushes.Black, pad / 2f, pad + area.Height - em * 1.35f);
            }
        }
        int shift = lead;
        for (int y = 0; y < area.Height; y++)
            for (int x = 0; x < area.Width; x++)
            {
                var c = f.GetPixel(area.Left + x, area.Top + y);
                if (Math.Min(c.R, Math.Min(c.G, c.B)) > 185) outBmp.SetPixel(x + pad + shift, y + pad, Color.Black);
            }
        return outBmp;
    }

    /// <summary>White figures in the corner: enough near-white pixels to be a count.</summary>
    static bool HasFigures(Bitmap f, Rectangle area)
    {
        int n = 0;
        for (int y = 0; y < area.Height; y += 2) for (int x = 0; x < area.Width; x += 2) { var c = f.GetPixel(area.Left + x, area.Top + y); if (Math.Min(c.R, Math.Min(c.G, c.B)) > 185) n++; }
        return n >= 12;
    }

    /// <summary>"4" → 4, "4.0K" → 4,000, "600.0K" → 600,000, "1.2M" → 1,200,000; no number → 1. Letters that look like digits count as them.</summary>
    public static long ParseCount(string t)
    {
        var fixd = new string(t.Select(ch => ch switch { 'O' or 'o' or 'Q' or 'D' => '0', 'l' or 'I' or '|' or 'i' => '1', 'S' or 's' => '5', 'B' => '8', 'F' or 'G' or 'b' => '6', 'A' => '4', 'Z' or 'z' => '2', _ => ch }).ToArray());
        var m = System.Text.RegularExpressions.Regex.Match(fixd.Replace(',', '.').Replace(" ", ""), @"(\d+(?:\.\d+)?)([KkMm]?)");
        if (!m.Success) return 1;
        double v = double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        return (long)Math.Round(m.Groups[2].Value.ToUpperInvariant() switch { "K" => v * 1e3, "M" => v * 1e6, _ => v });
    }

    /// <summary>
    /// A slot is there when its two top corners are one colour and its two bottom corners another (the backing is shaded,
    /// dark at the top and lighter at the bottom), not the world behind.
    /// </summary>
    static bool IsSlot(Bitmap f, Rectangle b)
    {
        int d = Math.Max(3, b.Width / 20);
        static int Diff(Color x, Color y) => Math.Abs(x.R - y.R) + Math.Abs(x.G - y.G) + Math.Abs(x.B - y.B);
        var tl = f.GetPixel(b.Left + d, b.Top + d); var tr = f.GetPixel(b.Right - d, b.Top + d);
        var bl = f.GetPixel(b.Left + d, b.Bottom - d); var br = f.GetPixel(b.Right - d, b.Bottom - d);
        return Diff(tl, tr) < 45 && Diff(bl, br) < 160;
    }

    /// <summary>
    /// A box with no art in it: nearly one colour all over (the black past a picture's edge, plain floor). A slot always holds
    /// an icon, so a flat box ends the grid (the Urugugu replay read 48 slots, 19 of them black, without this).
    /// </summary>
    internal static bool Flat(Bitmap f, Rectangle b)
    {
        double sum = 0, sum2 = 0; int n = 0;
        for (int y = b.Top + b.Height / 5; y < b.Bottom - b.Height / 5; y += 4)
            for (int x = b.Left + b.Width / 5; x < b.Right - b.Width / 5; x += 4)
            {
                if (x < 0 || y < 0 || x >= f.Width || y >= f.Height) continue;
                var c = f.GetPixel(x, y); double v = (c.R + c.G + c.B) / 3.0;
                sum += v; sum2 += v * v; n++;
            }
        if (n == 0) return true;
        double mean = sum / n, sd = Math.Sqrt(Math.Max(0, sum2 / n - mean * mean));
        return sd < 6;
    }

    /// <summary>A slot's inside differs from the gaps around it at least this much (real slots 83 and up, floor 62 at most, 2026-10-23).</summary>
    public const double ContrastMin = 72;

    /// <summary>
    /// How far the box's inside is from the world in the gaps beside and above it (mean colour, the larger of the two). Past
    /// the last slot the box is the world itself and differs little (the Urugugu replay read 3 boxes of floor as slots).
    /// </summary>
    internal static double Contrast(Bitmap f, Rectangle b)
    {
        (double R, double G, double B) Mean(int x1, int y1, int x2, int y2)
        {
            double r = 0, g = 0, bl = 0; int n = 0;
            for (int y = Math.Max(0, y1); y < Math.Min(f.Height, y2); y += 3)
                for (int x = Math.Max(0, x1); x < Math.Min(f.Width, x2); x += 3) { var c = f.GetPixel(x, y); r += c.R; g += c.G; bl += c.B; n++; }
            return n == 0 ? (-1, -1, -1) : (r / n, g / n, bl / n);
        }
        var inside = Mean(b.Left + 8, b.Top + 8, b.Right - 8, b.Bottom - 8);
        double D((double R, double G, double B) o) => o.R < 0 ? 0 : Math.Abs(inside.R - o.R) + Math.Abs(inside.G - o.G) + Math.Abs(inside.B - o.B);
        // all four sides: the world shows in every gap round a slot, and a slot stands out from at least one (the gold ore of
        // 2026-10-23 differed little from its left gap, 26, and plenty from the others); floor is floor on every side
        return new[]
        {
            D(Mean(b.Left - 10, b.Top, b.Left - 3, b.Bottom)), D(Mean(b.Right + 3, b.Top, b.Right + 10, b.Bottom)),
            D(Mean(b.Left, b.Top - 10, b.Right, b.Top - 3)), D(Mean(b.Left, b.Bottom + 3, b.Right, b.Bottom + 10)),
        }.Max();
    }

    /// <summary>A slot's top edge stands out at least this much (mean colour difference across it).</summary>
    public const double EdgeMin = 30;

    /// <summary>
    /// How sharply the colour changes across a box's top edge: the line just inside against the line just outside, over the
    /// middle of its width. A slot's backing starts there whatever its art (the art can reach a corner: 2026-10-23, the
    /// pendant and the orb, 50 and 59 across the top corners); the world behind has no edge at that line.
    /// </summary>
    internal static double TopEdge(Bitmap f, Rectangle b)
    {
        int yIn = b.Top + 2, yOut = b.Top - 3;
        if (yOut < 0 || yIn >= f.Height) return 0;
        double sum = 0; int n = 0;
        for (int x = b.Left + b.Width / 6; x < b.Right - b.Width / 6; x += 3)
        {
            var a = f.GetPixel(x, yIn); var o = f.GetPixel(x, yOut);
            sum += Math.Abs(a.R - o.R) + Math.Abs(a.G - o.G) + Math.Abs(a.B - o.B); n++;
        }
        return n == 0 ? 0 : sum / n;
    }

    /// <summary>The first row's top: from 15 px above the estimate to 3 px below it, the line where the eight boxes' top edges are sharpest.</summary>
    internal static int RefineTop(Bitmap f, int top, int x0, int s, int p)
    {
        int best = top; double bestScore = -1;
        // the header puts the grid a few pixels low, never high (Urugugu 335 for 327, Vakron 337 for 331): searching below it
        // found the bright tops of the art inside the slots instead (Vakron, y 345), so the search reaches up 15 and down 3
        for (int y = top - 15; y <= top + 3; y++)
        {
            if (y - 5 < 0 || y + s >= f.Height) continue;
            double score = 0;
            for (int col = 0; col < 8; col++) score += TopEdge(f, new Rectangle(x0 + col * p, y, s, s));
            if (score > bestScore) { bestScore = score; best = y; }
        }
        return best;
    }

    /// <summary>The backing's colour: the four corners averaged.</summary>
    internal static Color Background(Bitmap f, Rectangle b)
    {
        int d = Math.Max(3, b.Width / 20);
        var cs = new[] { f.GetPixel(b.Left + d, b.Top + d), f.GetPixel(b.Right - d, b.Top + d), f.GetPixel(b.Left + d, b.Bottom - d), f.GetPixel(b.Right - d, b.Bottom - d) };
        return Color.FromArgb((int)cs.Average(c => c.R), (int)cs.Average(c => c.G), (int)cs.Average(c => c.B));
    }

    /// <summary>The slot's grade by its backing colour, in the game's colours.</summary>
    public static string GradeOf(Color c)
    {
        int r = c.R, g = c.G, b = c.B;
        if (r > 120 && g > 80 && b < 70 && r > b + 60) return "Unique";        // gold
        if (r > 80 && b > 90 && g < Math.Min(r, b) - 15) return "Epic";      // violet
        if (g > r + 20 && g > b + 10) return "Rare";                         // green
        if (b > r + 25 && g > r + 10 && Math.Abs(g - b) < 40 && g > 90) return "Special";   // teal
        if (b > r + 25 && b > g) return "Legend";                            // blue
        return "Common";                                                      // dark grey
    }

    /// <summary>
    /// The red square with a white diamond the game draws in the middle of gear another class wears (2026-10-23 Urugugu: 13
    /// of 29 slots carry it, and every one of them missed its icon at 70 to 100). Its cells, and the ring around them, are
    /// left out of the comparison; null when the slot has no badge.
    /// </summary>
    /// <summary>Leave the class badge out of the comparison (off only to score the reader as it was).</summary>
    public static bool MaskBadges { get; set; } = true;

    internal static bool[]? BadgeMask(double[] cell)
    {
        const int n = 16;
        bool Red(int x, int y) { int k = (y * n + x) * 3; double r = cell[k], g = cell[k + 1], b = cell[k + 2]; return r > 140 && r > g * 1.9 && r > b * 1.9; }
        int minX = n, minY = n, maxX = -1, maxY = -1, count = 0;
        for (int y = 3; y <= 12; y++)
            for (int x = 3; x <= 12; x++)
                if (Red(x, y)) { count++; minX = Math.Min(minX, x); minY = Math.Min(minY, y); maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y); }
        // a badge is a compact red block (about 3x3 to 4x4 cells here), not red art spread over the icon
        if (count < 3 || maxX - minX > 5 || maxY - minY > 5) return null;
        var skip = new bool[n * n];
        for (int y = Math.Max(0, minY - 1); y <= Math.Min(n - 1, maxY + 1); y++)
            for (int x = Math.Max(0, minX - 1); x <= Math.Min(n - 1, maxX + 1); x++)
                skip[y * n + x] = true;
        return skip;
    }

    /// <summary>The icons closest to the slot's picture: each icon laid on the slot's own colour, compared where it is drawn.</summary>
    static List<(string, double)> Match(Bitmap f, Rectangle b, Color bg)
    {
        // the icon fills the slot but for a thin margin; a 16×16 grid over that area
        int m = (int)Math.Round(b.Width * 0.04);
        var cell = IconIndex.Cell(f, Rectangle.Inflate(b, -m, -m));
        var skip = MaskBadges ? BadgeMask(cell) : null;
        var res = new List<(string, double)>();
        foreach (var e in IconIndex.All) res.Add((e.Key, IconIndex.Distance(cell, bg, e, skip)));
        // the best per icon: an icon with two fingerprints (the game's file and its art as drawn in a slot) is one answer
        return res.GroupBy(x => x.Item1).Select(g => g.OrderBy(x => x.Item2).First()).OrderBy(x => x.Item2).Take(5).ToList();
    }
}

/// <summary>
/// Reads a reward slot's count by the shapes of its white figures: each figure is cut out and compared with 0 to 9, K
/// and M drawn in a few bold sans fonts like the game's. Windows' reader skips a lone digit ("4", "6") most of the time.
/// </summary>
public static class Glyphs
{
    const int W = 12, H = 18;
    const string Chars = "0123456789KM";
    static List<(char C, bool[] Px)>? _templates;
    static List<(char, bool[])> Templates => _templates ??= Build();

    // The game's own figures, cut from the 2026-10-23 Urugugu table (12 x 18, row by row): its font is thin and narrow,
    // unlike any Windows font, and a lone "4" or "6" read as 1 or 7 against those.
    static readonly (char C, string Px)[] GameFigures =
    {
        ('4', "000000011100000000011100000000111100000000111100000000111100000001111100000011011100000011011100000111011100001110011100001100011100011100011100111111111111111111111111000000011100000000011100000000011100000000011100"),
        ('4', "000000001110000000001110000000011110000000111110000000111110000001111110000011101110000011001110000111001110001110001110001100001110011100001110111111111111111111111111000000001110000000001110000000001110000000001110"),
        ('6', "000000011100000000111000000001111000000001110000000011100000000111100000000111000000001111111000011111111110011111011111011100000111111000000011111000000011111000000011111000000011011100000111011111011111001111111110"),
        ('5', "011111111110011111111110011100000000011100000000011100000000011100000000011100000000011111100000011111111000010000111100000000001110000000000110000000000111000000000111000000000110010000001110111100011100011111111000"),
        ('1', "000000000111000000111111000011111111001111111111111111011111001100011111000000011111000000011111000000011111000000011111000000011111000000011111000000011111000000011111000000011111000000011111000000011111000000011111"),
    };

    static List<(char, bool[])> Build()
    {
        var list = new List<(char, bool[])>();
        foreach (var (c, px) in GameFigures) list.Add((c, px.Select(ch => ch == '1').ToArray()));
        foreach (var fam in new[] { "Arial", "Segoe UI", "Tahoma", "Verdana", "Bahnschrift" })
        {
            try
            {
                using var font = new Font(fam, 40, FontStyle.Bold, GraphicsUnit.Pixel);
                foreach (var ch in Chars)
                {
                    using var bmp = new Bitmap(60, 60);
                    using (var g = Graphics.FromImage(bmp)) { g.Clear(Color.Black); g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias; g.DrawString(ch.ToString(), font, Brushes.White, 4, 2); }
                    var mask = new bool[60, 60];
                    for (int y = 0; y < 60; y++) for (int x = 0; x < 60; x++) mask[x, y] = bmp.GetPixel(x, y).R > 128;
                    if (Bounds(mask, 0, 59) is { } r) list.Add((ch, Normalise(mask, r)));
                }
            }
            catch (Exception) { /* a font missing on this PC: the others still serve */ }
        }
        return list;
    }

    static Rectangle? Bounds(bool[,] m, int x0, int x1)
    {
        int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
        for (int x = x0; x <= x1; x++) for (int y = 0; y < m.GetLength(1); y++) if (m[x, y]) { minX = Math.Min(minX, x); maxX = Math.Max(maxX, x); minY = Math.Min(minY, y); maxY = Math.Max(maxY, y); }
        return maxX < 0 ? null : Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1);
    }

    static bool[] Normalise(bool[,] m, Rectangle r)
    {
        var px = new bool[W * H];
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                int sx = r.Left + x * r.Width / W, sy = r.Top + y * r.Height / H;
                px[y * W + x] = m[Math.Min(sx, m.GetLength(0) - 1), Math.Min(sy, m.GetLength(1) - 1)];
            }
        return px;
    }

    static bool DarkEdge(Bitmap f, Rectangle area, Rectangle b)
    {
        double sum = 0; int n = 0;
        for (int y = b.Top; y < b.Bottom; y += 2)
            foreach (var x in new[] { b.Left - 2, b.Right + 1 })
            {
                int fx = area.Left + x, fy = area.Top + y;
                if (fx < 0 || fy < 0 || fx >= f.Width || fy >= f.Height) continue;
                var c = f.GetPixel(fx, fy); sum += (c.R + c.G + c.B) / 3.0; n++;
            }
        return n > 0 && sum / n < 120;
    }

    /// <summary>The count in the area as text ("4", "600.0K"); empty when no figure can be told.</summary>
    public static string Read(Bitmap f, Rectangle area) => Read(f, area, out _);

    /// <summary>The same, saying whether a count's figure is there at all (white, edged dark, on the count's line, at the right).</summary>
    public static string Read(Bitmap f, Rectangle area, out bool figure)
    {
        figure = false;
        var m = new bool[area.Width, area.Height];
        for (int y = 0; y < area.Height; y++) for (int x = 0; x < area.Width; x++) { var c = f.GetPixel(area.Left + x, area.Top + y); m[x, y] = Math.Min(c.R, Math.Min(c.G, c.B)) > 150; }   // the game's figures are thin: most of a stroke is 120 to 185, so 200 kept only a 4's upright (read 1) and broke a 3 in two (11)
        // only the lowest band of rows with white in it: the count's line. Art above it (a blade, a feather) shared its
        // columns and was cut out with the figure (2026-10-23: the 3 came out 35 rows tall, the 2 27)
        {
            int yb = -1;
            for (int y = area.Height - 1; y >= 0 && yb < 0; y--) for (int x = 0; x < area.Width; x++) if (m[x, y]) { yb = y; break; }
            if (yb < 0) return "";
            // a figure is about half the corner tall (19 of 40 rows): keep that much above the lowest white row
            int ya = Math.Max(0, yb - area.Height / 2 - 1);
            for (int y = 0; y < area.Height; y++) if (y < ya || y > yb) for (int x = 0; x < area.Width; x++) m[x, y] = false;
        }
        // figures: runs of columns with white in them, read from the right, since the count is right-aligned
        var cols = Enumerable.Range(0, area.Width).Select(x => Enumerable.Range(0, area.Height).Count(y => m[x, y])).ToArray();
        var runs = new List<(int A, int B)>();
        for (int x = 0; x < area.Width; x++)
        {
            if (cols[x] == 0) continue;
            int a = x; while (x + 1 < area.Width && cols[x + 1] > 0) x++;
            runs.Add((a, x));
        }
        if (runs.Count == 0) return "";
        var boxes = runs.Select(r => (r, b: Bounds(m, r.A, r.B)!.Value)).ToList();
        int tall = boxes.Max(x => x.b.Height);
        if (tall < 8) return "";
        // only what stands on the count's line: as tall as the tallest figure, or a dot at its foot; art bleeding in is dropped
        int foot = boxes.Where(x => x.b.Height >= tall * 0.7).Max(x => x.b.Bottom);
        var sb = new System.Text.StringBuilder();
        foreach (var (r, b) in boxes)
        {
            if (Math.Abs(b.Bottom - foot) > tall / 4) continue;
            if (b.Height < tall * 0.35 && b.Width < tall * 0.5) { sb.Append('.'); continue; }
            if (b.Height < tall * 0.7) continue;
            // the count is white with a dark edge; a bright spot in the art has none
            if (!DarkEdge(f, area, b)) return "";
            var px = Normalise(m, b);
            char best = '?'; int bestD = int.MaxValue;
            foreach (var (c, t) in Templates)
            {
                int d = 0; for (int i = 0; i < px.Length; i++) if (px[i] != t[i]) d++;
                if (d < bestD) { bestD = d; best = c; }
            }
            // a narrow figure is a 1 whatever the template says: scaled to the box, a 1's bar fills it
            if (b.Width < b.Height * 0.35) best = '1';
            sb.Append(best);
        }
        // a count is right-aligned: its last figure ends close to the slot's right edge
        var lastFig = boxes.Where(x => Math.Abs(x.b.Bottom - foot) <= tall / 4 && x.b.Height >= tall * 0.7).Select(x => x.b.Right).DefaultIfEmpty(0).Max();
        if (lastFig < area.Width * 0.72) return "";
        figure = true;   // white, edged dark, on the line, at the right: a count is there, read or not
        var text = sb.ToString().Trim('.');
        return System.Text.RegularExpressions.Regex.IsMatch(text, @"^\d+(\.\d+)?[KM]?$") ? text : "";
    }
}
