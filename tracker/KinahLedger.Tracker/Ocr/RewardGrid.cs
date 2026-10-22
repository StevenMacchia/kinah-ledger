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

    /// <summary>Downloads every distinct icon of the item list (cached in <paramref name="cacheDir"/>) and writes the index.</summary>
    public static async Task<int> BuildAsync(IEnumerable<string> urls, string cacheDir, string outFile)
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
                if (!IsSlot(frame, box)) break;
                found++;
                var bg = Background(frame, box);
                var grade = GradeOf(bg);
                // the count, white in the lower right; a long one ("600.0K") reaches left past the middle
                var corner = new Rectangle(box.Left + box.Width / 8, box.Top + box.Height * 3 / 5, box.Width * 7 / 8 - 2, box.Height * 2 / 5 - 2);
                // no white figures there: the game shows no count for one of an item
                string num = "";
                if (HasFigures(frame, corner))
                    using (var cb = WhiteText(frame, corner))
                        foreach (var scale in new[] { 2, 3, 4, 1 })
                        {
                            num = string.Join(" ", (await ScreenReader.ReadBoxesAsync(cb, corner.Location, scale)).Select(l => l.Text.Trim())).Trim();
                            if (System.Text.RegularExpressions.Regex.IsMatch(num, @"\d")) break;
                        }
                // the reader skips a lone digit: then its shape, in the tight lower-right corner where a count ends
                if (!System.Text.RegularExpressions.Regex.IsMatch(num, @"\d"))
                {
                    var tight = new Rectangle(box.Left + box.Width * 11 / 20, box.Top + box.Height * 13 / 20, box.Width * 2 / 5, box.Height * 3 / 10);
                    num = Glyphs.Read(frame, tight);
                }
                slots.Add(new Slot(box, grade, num, ParseCount(num), Match(frame, box, bg)));
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
    static Bitmap WhiteText(Bitmap f, Rectangle area)
    {
        const int pad = 24;
        var outBmp = new Bitmap(area.Width + 2 * pad, area.Height + 2 * pad, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(outBmp)) g.Clear(Color.White);
        for (int y = 0; y < area.Height; y++)
            for (int x = 0; x < area.Width; x++)
            {
                var c = f.GetPixel(area.Left + x, area.Top + y);
                if (Math.Min(c.R, Math.Min(c.G, c.B)) > 185) outBmp.SetPixel(x + pad, y + pad, Color.Black);
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

    /// <summary>The backing's colour: the four corners averaged.</summary>
    static Color Background(Bitmap f, Rectangle b)
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

    /// <summary>The icons closest to the slot's picture: each icon laid on the slot's own colour, compared where it is drawn.</summary>
    static List<(string, double)> Match(Bitmap f, Rectangle b, Color bg)
    {
        // the icon fills the slot but for a thin margin; a 16×16 grid over that area
        int m = (int)Math.Round(b.Width * 0.04);
        var area = Rectangle.Inflate(b, -m, -m);
        var cell = new double[IconIndex.N * IconIndex.N * 3];
        using (var small = new Bitmap(IconIndex.N, IconIndex.N, PixelFormat.Format32bppArgb))
        {
            using (var g = Graphics.FromImage(small)) { g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic; g.DrawImage(f, new Rectangle(0, 0, IconIndex.N, IconIndex.N), area, GraphicsUnit.Pixel); }
            for (int y = 0; y < IconIndex.N; y++) for (int x = 0; x < IconIndex.N; x++) { var c = small.GetPixel(x, y); int i = (y * IconIndex.N + x) * 3; cell[i] = c.R; cell[i + 1] = c.G; cell[i + 2] = c.B; }
        }
        var res = new List<(string, double)>();
        foreach (var e in IconIndex.All)
        {
            double sum = 0, wsum = 0;
            for (int y = 0; y < IconIndex.N; y++)
                for (int x = 0; x < IconIndex.N; x++)
                {
                    // the lower corners carry the count and the lock: they say nothing about the icon
                    if (y >= IconIndex.N - 4 && (x < 4 || x >= IconIndex.N - 6)) continue;
                    int i = (y * IconIndex.N + x), j = i * 4, k = i * 3;
                    double a = e.Rgba[j + 3] / 255.0;
                    double r = e.Rgba[j] * a + bg.R * (1 - a), g = e.Rgba[j + 1] * a + bg.G * (1 - a), bl = e.Rgba[j + 2] * a + bg.B * (1 - a);
                    double w = 0.35 + a;   // the icon's own pixels count most
                    sum += w * (Math.Abs(r - cell[k]) + Math.Abs(g - cell[k + 1]) + Math.Abs(bl - cell[k + 2]));
                    wsum += w;
                }
            res.Add((e.Key, sum / Math.Max(wsum, 1)));
        }
        return res.OrderBy(x => x.Item2).Take(5).ToList();
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

    static List<(char, bool[])> Build()
    {
        var list = new List<(char, bool[])>();
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
    public static string Read(Bitmap f, Rectangle area)
    {
        var m = new bool[area.Width, area.Height];
        for (int y = 0; y < area.Height; y++) for (int x = 0; x < area.Width; x++) { var c = f.GetPixel(area.Left + x, area.Top + y); m[x, y] = Math.Min(c.R, Math.Min(c.G, c.B)) > 200; }
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
        var text = sb.ToString().Trim('.');
        return System.Text.RegularExpressions.Regex.IsMatch(text, @"^\d+(\.\d+)?[KM]?$") ? text : "";
    }
}
