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

    /// <summary>The health bars in the frame's middle: thin, flat red boxes.</summary>
    public static List<Rectangle> FindBars(Bitmap f)
    {
        int h = f.Height, w = f.Width;
        var area = new Rectangle((int)(w * 0.12), (int)(h * 0.04), (int)(w * 0.76), (int)(h * 0.78));
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
    public static async Task<List<Plate>> ReadAsync(Bitmap frame)
    {
        int h = frame.Height;
        var plates = new List<Plate>();
        foreach (var bar in FindBars(frame))
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
