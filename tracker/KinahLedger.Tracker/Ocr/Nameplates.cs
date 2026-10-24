using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace KinahLedger.Ocr;

/// <summary>
/// The enemies fighting the player, from the game's own picture (Steven, 2026-10-22: "look at the char on screen to see
/// which healthbars around it are being fought"). An enemy in combat shows its gold name with a red health bar under it;
/// one standing by shows its name alone. So: find the thin red bars around the character, read the name just above each,
/// and keep the names on the enemy list (the player's own bar is the same red, but "Swoly" is no enemy). Measured on a
/// 4096x1728 frame: the bar is (241, 54, 82), about 0.7% of the height tall, centred under the name, the name 1 to 2.5%
/// of the height above it. Everything is in fractions of the frame's height, so any resolution works.
/// </summary>
public static class Nameplates
{
    public sealed record Plate(string Enemy, Rectangle Bar, string Read);

    static bool BarRed(byte r, byte g, byte b) => r > 195 && g >= 25 && g <= 95 && b >= 50 && b <= 115 && r - g > 140 && r - b > 110;

    /// <summary>The middle of the frame, where the plates around the character are looked for.</summary>
    public static Rectangle Area(int w, int h) => new((int)(w * 0.12), (int)(h * 0.04), (int)(w * 0.76), (int)(h * 0.78));

    /// <summary>
    /// The health bars in the frame's middle. <paramref name="half"/> (ocr.plates_half, on in code): the fast search;
    /// otherwise the old full 24-bit scan, which the fast one must match bar for bar (parse-test, bench --sizing).
    /// </summary>
    public static List<Rectangle> FindBars(Bitmap f, bool half) => half ? FindBarsHalf(f) : FindBars(f);

    /// <summary>
    /// The fast search (2026-10-24; a 5120x2160 frame was converted to 24 bits, a second full copy, and every pixel of its
    /// middle scanned once a second in fights). The capture's own 32-bit pixels are read in place; every second pixel of
    /// every second row finds where bars may be (a bar is at least 0.4% of the height tall and as wide, so 8 px at 2160:
    /// four sampled rows and columns at least); each such place, grown by a margin, is then scanned at full size the old
    /// way, so the bars kept are the full scan's own rectangles.
    /// </summary>
    public static unsafe List<Rectangle> FindBarsHalf(Bitmap f)
    {
        int h = f.Height, w = f.Width;
        var area = Area(w, h);
        var data = f.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            IntPtr scan0 = data.Scan0; int stride = data.Stride;
            // the coarse look: runs on even rows, every second pixel, at a little under half the old shortest run
            int minRun = Math.Max(6, (int)(h * 0.004));
            int coarseRun = Math.Max(2, minRun / 2 - 1);
            var runs = new List<(int Y, int X0, int X1)>();
            int x0 = area.Left + (area.Left & 1), y0 = area.Top + (area.Top & 1);
            for (int y = y0; y < area.Bottom; y += 2)
            {
                byte* row = (byte*)scan0 + (long)y * stride;
                int start = -1, n = 0;
                for (int x = x0; x < area.Right; x += 2)
                {
                    byte* p = row + x * 4;
                    if (BarRed(p[2], p[1], p[0])) { if (start < 0) { start = x; n = 0; } n++; }
                    else if (start >= 0) { if (n >= coarseRun) runs.Add((y, start, x - 2)); start = -1; }
                }
                if (start >= 0 && n >= coarseRun) runs.Add((y, start, area.Right - 1));
            }
            var spots = Stack(runs, 2, 6, (l, r, top, last) => Rectangle.FromLTRB(l, top, r + 1, last + 1));
            // each place again at full size, the old way, in a window with room around it. A window is used only when no red
            // run of the old scan's length crosses its sides and none lies on its top or bottom row (where it isn't the
            // area's edge): then nothing outside it can join what is inside, and its bars are exactly the full scan's.
            // Otherwise it grows, and after three tries the area is scanned whole.
            int mx = 16, my = 6 + (int)(h * 0.012);
            bool Red(int x, int y) { byte* p = (byte*)scan0 + (long)y * stride + x * 4; return BarRed(p[2], p[1], p[0]); }
            var bars = new List<Rectangle>();
            long scanned = 0, all = (long)area.Width * area.Height;
            foreach (var s in spots)
            {
                var win = Rectangle.Intersect(Rectangle.FromLTRB(s.Left - mx, s.Top - my, s.Right + mx, s.Bottom + my), area);
                if (bars.Any(b => win.IntersectsWith(b)) && bars.Any(b => b.Contains(s))) continue;   // already measured whole
                for (int tries = 0; ; tries++)
                {
                    scanned += (long)win.Width * win.Height;
                    // red everywhere (a sunset, a red-lit cave): the windows would cover the area anyway, so scan it whole
                    if (tries == 3 || scanned > all) return Scan(area, area, h, Red, out _).OrderBy(b => b.Y).ThenBy(b => b.X).ToList();
                    var found = Scan(win, area, h, Red, out bool open);
                    if (!open) { foreach (var b in found) if (!bars.Contains(b)) bars.Add(b); break; }
                    win = Rectangle.Intersect(Rectangle.FromLTRB(win.Left - 64, win.Top - my, win.Right + 64, win.Bottom + my), area);
                }
            }
            return bars.OrderBy(b => b.Y).ThenBy(b => b.X).ToList();
        }
        finally { f.UnlockBits(data); }
    }

    /// <summary>
    /// The old scan's runs and bars inside <paramref name="win"/>, at full size. <paramref name="open"/>: a red run of the
    /// old scan's length crosses one of its sides, or lies on its top or bottom row, where that side isn't the area's.
    /// </summary>
    static List<Rectangle> Scan(Rectangle win, Rectangle area, int h, Func<int, int, bool> red, out bool open)
    {
        var runs = new List<(int Y, int X0, int X1)>();
        int minRun = Math.Max(6, (int)(h * 0.004));
        open = false;
        for (int y = win.Top; y < win.Bottom; y++)
        {
            int start = -1;
            for (int x = win.Left; x < win.Right; x++)
            {
                bool r = red(x, y);
                if (r && start < 0) start = x;
                else if (!r && start >= 0) { if (x - start >= minRun) runs.Add((y, start, x - 1)); start = -1; }
            }
            if (start >= 0 && win.Right - start >= minRun) runs.Add((y, start, win.Right - 1));
            if (open) continue;
            // a run through a side: its length in the whole area
            if (win.Left > area.Left && red(win.Left, y) && red(win.Left - 1, y) && RunAt(win.Left, y) >= minRun) open = true;
            if (win.Right < area.Right && red(win.Right - 1, y) && red(win.Right, y) && RunAt(win.Right - 1, y) >= minRun) open = true;
        }
        if (runs.Any(r => (r.Y == win.Top && win.Top > area.Top) || (r.Y == win.Bottom - 1 && win.Bottom < area.Bottom))) open = true;
        return Stack(runs, 1, 3, (x0, x1, top, last) =>
        {
            int bh = last - top + 1, bw = x1 - x0 + 1;
            return bh >= h * 0.004 && bh <= h * 0.012 && bw >= bh ? new Rectangle(x0, top, bw, bh) : (Rectangle?)null;
        });
        int RunAt(int x, int y)
        {
            int l = x, r = x;
            while (l - 1 >= area.Left && red(l - 1, y)) l--;
            while (r + 1 < area.Right && red(r + 1, y)) r++;
            return r - l + 1;
        }
    }

    /// <summary>
    /// Rows of runs stacked on each other (each within <paramref name="gap"/> rows of the last, starting within
    /// <paramref name="slack"/> px of it) are one box; <paramref name="keep"/> says which boxes count.
    /// </summary>
    static List<Rectangle> Stack(List<(int Y, int X0, int X1)> runs, int gap, int slack, Func<int, int, int, int, Rectangle?> keep)
    {
        var boxes = new List<Rectangle>();
        var open = new List<(int X0, int X1, int Top, int Last)>();
        foreach (var r in runs.OrderBy(r => r.Y).ThenBy(r => r.X0))
        {
            int k = open.FindIndex(o => o.Last >= r.Y - gap && Math.Abs(o.X0 - r.X0) <= slack);
            if (k >= 0) { var o = open[k]; open[k] = (Math.Min(o.X0, r.X0), Math.Max(o.X1, r.X1), o.Top, r.Y); }
            else open.Add((r.X0, r.X1, r.Y, r.Y));
            for (int j = open.Count - 1; j >= 0; j--) if (open[j].Last < r.Y - gap) { if (keep(open[j].X0, open[j].X1, open[j].Top, open[j].Last) is { } b) boxes.Add(b); open.RemoveAt(j); }
        }
        foreach (var o in open) if (keep(o.X0, o.X1, o.Top, o.Last) is { } b) boxes.Add(b);
        return boxes;
    }

    /// <summary>The health bars in the frame's middle: thin, flat red boxes (the full 24-bit scan, as before 2026-10-24).</summary>
    public static List<Rectangle> FindBars(Bitmap f)
    {
        int h = f.Height, w = f.Width;
        var area = Area(w, h);
        var data = f.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
        var runs = new List<(int Y, int X0, int X1)>();
        try
        {
            int stride = data.Stride; var row = new byte[stride];
            int minRun = Math.Max(6, (int)(h * 0.004));
            for (int y = area.Top; y < area.Bottom; y++)
            {
                Marshal.Copy(data.Scan0 + y * stride, row, 0, stride);
                int start = -1;
                for (int x = area.Left; x < area.Right; x++)
                {
                    int i = x * 3; bool red = BarRed(row[i + 2], row[i + 1], row[i]);
                    if (red && start < 0) start = x;
                    else if (!red && start >= 0) { if (x - start >= minRun) runs.Add((y, start, x - 1)); start = -1; }
                }
                if (start >= 0 && area.Right - start >= minRun) runs.Add((y, start, area.Right - 1));
            }
        }
        finally { f.UnlockBits(data); }
        // rows of runs stacked on each other, starting at the same place, are one bar
        var bars = new List<Rectangle>();
        var open = new List<(int X0, int X1, int Top, int Last)>();
        foreach (var r in runs.OrderBy(r => r.Y).ThenBy(r => r.X0))
        {
            int k = open.FindIndex(o => o.Last >= r.Y - 1 && Math.Abs(o.X0 - r.X0) <= 3);
            if (k >= 0) { var o = open[k]; open[k] = (Math.Min(o.X0, r.X0), Math.Max(o.X1, r.X1), o.Top, r.Y); }
            else open.Add((r.X0, r.X1, r.Y, r.Y));
            // close what can't grow any more
            for (int j = open.Count - 1; j >= 0; j--) if (open[j].Last < r.Y - 1) { Close(open[j]); open.RemoveAt(j); }
        }
        foreach (var o in open) Close(o);
        void Close((int X0, int X1, int Top, int Last) o)
        {
            int bh = o.Last - o.Top + 1, bw = o.X1 - o.X0 + 1;
            if (bh >= h * 0.004 && bh <= h * 0.012 && bw >= bh) bars.Add(new Rectangle(o.X0, o.Top, bw, bh));
        }
        return bars;
    }

    /// <summary>The enemies with a health bar in the frame: the name read above each bar, matched to the enemy list.</summary>
    public static Task<List<Plate>> ReadAsync(Bitmap frame) => ReadAsync(frame, KinahLedger.Update.RemoteConfig.Current.PlatesHalf);

    /// <summary>The same, with the bar search chosen (<see cref="FindBars(Bitmap, bool)"/>); each name is read from the frame at full size.</summary>
    public static async Task<List<Plate>> ReadAsync(Bitmap frame, bool halfScan)
    {
        int h = frame.Height;
        var plates = new List<Plate>();
        foreach (var bar in FindBars(frame, halfScan))
        {
            int cx = bar.Left + bar.Width / 2, half = (int)(h * 0.16);
            var above = Rectangle.Intersect(new Rectangle(cx - half, bar.Top - (int)(h * 0.03), half * 2, (int)(h * 0.03)), new Rectangle(0, 0, frame.Width, h));
            if (above.Width < 10 || above.Height < 6) continue;
            using var cut = frame.Clone(above, frame.PixelFormat);
            var lines = await ScreenReader.ReadBoxesAsync(cut, above.Location, 2, boost: true);
            // the line nearest the bar is its name; a title line may sit above it
            foreach (var l in lines.OrderByDescending(l => l.Box.Bottom))
            {
                var name = TargetReader.Match(l.Text.Trim());
                if (name == null) continue;
                plates.Add(new Plate(name, bar, l.Text.Trim()));
                break;
            }
        }
        return plates;
    }
}
