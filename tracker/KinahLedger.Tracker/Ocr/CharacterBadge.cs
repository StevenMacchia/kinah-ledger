using System.Drawing;
using System.Drawing.Imaging;

namespace KinahLedger.Ocr;

/// <summary>
/// The character's badge: the class icon in the medallion between the health bar (red) and the mana bar (cyan) at the
/// bottom middle of the game (Steven, 2026-11-11: "the class icon is between the health and mana bars"). Each character
/// shows its class's icon there all the time it is in the world, so the icon tells the player's characters apart; the
/// level and combat power under it are left alone (the OCR reads them about half the time).
/// <para>Found by colour, not by a fixed place, so any window size and interface scale work: on some row of the bottom
/// band a red run ends left of the middle and a cyan run starts right of it, and the medallion fills the gap. The icon
/// sits above that row inside the medallion's dark diamond; its light pixels (silver on navy), cut to their own box and
/// laid on a 16 × 16 grid, are the fingerprint. Checked on Steven's run pictures of 2026-11-04 to 11 (5120 × 2160):
/// three characters, three icons (a shield, a harp, a knot), each the same pixels every time.</para>
/// </summary>
public static class CharacterBadge
{
    /// <summary>Where the medallion was found, in the picture's own pixels: the bars' row and the gap's two edges.</summary>
    public readonly record struct Place(int Row, int Left, int Right)
    {
        public int Gap => Right - Left;
        public int Mid => (Left + Right) / 2;
    }

    public const int Grid = 16;

    /// <summary>The part of the game window to capture: the bottom quarter, the middle 40%.</summary>
    public static Rectangle Band(Rectangle game) =>
        new(game.Left + (int)(game.Width * 0.30), game.Top + (int)(game.Height * 0.75), (int)(game.Width * 0.40), game.Height - (int)(game.Height * 0.75));

    static bool Red(uint c) { int r = (int)(c >> 16) & 255, g = (int)(c >> 8) & 255, b = (int)c & 255; return r > 140 && g < 95 && b < 115 && r - g > 70; }
    static bool Cyan(uint c) { int r = (int)(c >> 16) & 255, g = (int)(c >> 8) & 255, b = (int)c & 255; return b > 160 && g > 130 && r < 130 && b - r > 70; }

    /// <summary>A picture's pixels as 0xRRGGBB, row after row.</summary>
    public static uint[] Pixels(Bitmap bmp, out int w, out int h)
    {
        w = bmp.Width; h = bmp.Height;
        var px = new uint[w * h];
        using var copy = bmp.PixelFormat == PixelFormat.Format32bppArgb ? null : bmp.Clone(new Rectangle(0, 0, w, h), PixelFormat.Format32bppArgb);
        var src = copy ?? bmp;
        var data = src.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var row = new int[w];
            for (int y = 0; y < h; y++)
            {
                System.Runtime.InteropServices.Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, w);
                for (int x = 0; x < w; x++) px[y * w + x] = (uint)row[x] & 0xFFFFFF;
            }
        }
        finally { src.UnlockBits(data); }
        return px;
    }

    /// <summary>
    /// The medallion in a picture whose middle column is the game's middle (the band, or a whole game frame). Null when no
    /// row has both bars, as on a loading screen, the character screen or a window over the bottom of the game.
    /// <paramref name="gameHeight"/>: the game window's height, which the interface scales with; the gap is 7% of it at
    /// the default scale (150 px of 2160), so anything under 3.5% or over 14% is something else.
    /// </summary>
    public static Place? Find(uint[] px, int w, int h, int gameHeight)
    {
        int minGap = Math.Max(12, (int)(gameHeight * 0.035)), maxGap = (int)(gameHeight * 0.14);
        int mid = w / 2, reach = Math.Max(40, w / 5);
        var rows = new List<Place>();
        for (int y = h / 3; y < h - 2; y++)
        {
            int o = y * w;
            int left = -1;
            for (int x = mid; x > mid - reach && x > 8; x--)
                if (Red(px[o + x - 1]) && Red(px[o + x - 2]) && Red(px[o + x - 3]) && Red(px[o + x - 4])) { left = x; break; }
            if (left < 0) continue;
            int right = -1;
            for (int x = mid; x < mid + reach && x < w - 8; x++)
                if (Cyan(px[o + x]) && Cyan(px[o + x + 1]) && Cyan(px[o + x + 2]) && Cyan(px[o + x + 3])) { right = x; break; }
            if (right < 0) continue;
            // both bars long, not a red or cyan speck: most of the stretch beyond each edge, as long as the gap, is bar colour
            int gap = right - left, len = Math.Max(12, gap);
            if (gap < minGap || gap > maxGap) continue;
            if (left - len < 0 || right + len >= w) continue;
            int reds = 0, cyans = 0;
            for (int i = 1; i <= len; i++) { if (Red(px[o + left - i])) reds++; if (Cyan(px[o + right + i - 1])) cyans++; }
            if (reds < len * 0.6 || cyans < len * 0.6) continue;
            // the medallion sits about on the middle: the two edges about as far from it
            if (Math.Abs((left + right) / 2 - mid) > gap / 3 + 4) continue;
            rows.Add(new Place(y, left, right));
        }
        if (rows.Count < 2) return null;
        // the bars' rows: the narrowest gap's rows (the diamond widens below), their middle row
        int g = rows.Min(r => r.Gap);
        var near = rows.Where(r => r.Gap <= g + Math.Max(3, g / 12)).ToList();
        var pick = near[near.Count / 2];
        return new Place(pick.Row, near.Max(r => r.Left), near.Min(r => r.Right));
    }

    /// <summary>
    /// The icon's box: centred on the gap, from 0.40 of the gap above the bars' row to 0.28 below it (the level's digits
    /// start lower), 0.84 of the gap wide. Only its pixels inside the medallion's diamond count (<see cref="InDiamond"/>),
    /// so the world behind the medallion's corners never does.
    /// </summary>
    public static Rectangle IconBox(Place p, int w, int h)
    {
        int g = p.Gap;
        int iw = (int)(g * 0.84), top = p.Row - (int)(g * 0.40), bottom = p.Row + (int)(g * 0.28);
        return Rectangle.Intersect(new Rectangle(p.Mid - iw / 2, top, iw, bottom - top), new Rectangle(0, 0, w, h));
    }

    /// <summary>Inside the diamond around the icon: centred on the gap a little above the bars' row, 0.46 of the gap from its centre to each corner.</summary>
    public static bool InDiamond(Place p, int x, int y)
    {
        double g = p.Gap, cy = p.Row - g * 0.06, r = g * 0.46;
        return Math.Abs(x - p.Mid) + Math.Abs(y - cy) <= r;
    }

    static int Luma(uint c) => (int)((((c >> 16) & 255) * 299 + ((c >> 8) & 255) * 587 + (c & 255) * 114) / 1000);

    /// <summary>
    /// The icon's fingerprint: the mean brightness of each of 16 × 16 cells over <see cref="IconBox"/>, two hex digits a
    /// cell, the cells whose middle is outside the diamond left at 00. Compared by the shape of the brightness, not its
    /// level (<see cref="Alike"/>), so a reward screen dimming the game, or a glow over it, leaves it the same icon. Null
    /// when the box is too small or holds nothing light (no icon).
    /// </summary>
    public static string? Fingerprint(uint[] px, int w, int h, Place p)
    {
        var box = IconBox(p, w, h);
        if (box.Width < Grid || box.Height < Grid) return null;
        var sb = new System.Text.StringBuilder(Grid * Grid * 2);
        int bright = 0;
        for (int gy = 0; gy < Grid; gy++)
            for (int gx = 0; gx < Grid; gx++)
            {
                int ax = box.Left + gx * box.Width / Grid, bx = Math.Max(ax + 1, box.Left + (gx + 1) * box.Width / Grid);
                int ay = box.Top + gy * box.Height / Grid, by = Math.Max(ay + 1, box.Top + (gy + 1) * box.Height / Grid);
                int v = 0;
                if (InDiamond(p, (ax + bx) / 2, (ay + by) / 2))
                {
                    long sum = 0; int n = 0;
                    for (int y = ay; y < by; y++) for (int x = ax; x < bx; x++) { sum += Luma(px[y * w + x]); n++; }
                    v = Math.Clamp((int)(sum / Math.Max(1, n)), 1, 255);
                    if (v > 140) bright++;
                }
                sb.Append(v.ToString("x2"));
            }
        return bright >= 6 ? sb.ToString() : null;
    }

    /// <summary>
    /// How alike two fingerprints are: the correlation of their cells' brightness over the cells inside the diamond, from
    /// -1 to 1 (1: the same pattern of light and dark). On Steven's pictures the same icon scores 0.78 and up, two
    /// classes' icons 0.60 or less.
    /// </summary>
    public static double Alike(string? a, string? b)
    {
        if (a == null || b == null || a.Length != b.Length || a.Length != Grid * Grid * 2) return 0;
        var x = new List<double>(); var y = new List<double>();
        for (int i = 0; i < a.Length; i += 2)
        {
            int u = Convert.ToInt32(a.Substring(i, 2), 16), v = Convert.ToInt32(b.Substring(i, 2), 16);
            if (u == 0 || v == 0) continue;
            x.Add(u); y.Add(v);
        }
        if (x.Count < 20) return 0;
        double mx = x.Average(), my = y.Average(), sxy = 0, sxx = 0, syy = 0;
        for (int i = 0; i < x.Count; i++) { sxy += (x[i] - mx) * (y[i] - my); sxx += (x[i] - mx) * (x[i] - mx); syy += (y[i] - my) * (y[i] - my); }
        return sxx <= 0 || syy <= 0 ? 0 : sxy / Math.Sqrt(sxx * syy);
    }

    /// <summary>Fingerprints this alike are the same icon.</summary>
    public const double SameIcon = 0.72;

    /// <summary>The badge in a picture (a band or a whole frame), or null.</summary>
    public static (Place Place, string Print)? Read(Bitmap bmp, int gameHeight)
    {
        var px = Pixels(bmp, out int w, out int h);
        if (Find(px, w, h, gameHeight) is not { } p) return null;
        return Fingerprint(px, w, h, p) is { } f ? (p, f) : null;
    }
}
