using System.Drawing;
using System.Drawing.Imaging;

namespace KinahLedger.Ocr;

/// <summary>
/// The character's own name, read off the nameplate over its head (Steven, 2026-11-11: "character name is above head").
/// The camera keeps the character in the middle of the screen, and its plate is a short health bar (red over a cyan
/// mana line) with the name just above it, green, or teal in a group; a title ("Has Exceptional Skill") can sit a line
/// higher. Plain OCR of the middle of the screen read "Sw'oiy" and "[Swoleo]b" in a fight; here the plate is found by
/// its bar first, only the line right above it is kept, and only its green and teal pixels, drawn dark on white, go to
/// the OCR. Each reading is a vote (Characters.VoteName); a name counts once it has been read the same a few times.
/// </summary>
public static class CharacterName
{
    /// <summary>The part of the game window to capture: the middle fifth across, from 25% to 65% down.</summary>
    public static Rectangle Region(Rectangle game) =>
        new(game.Left + (int)(game.Width * 0.40), game.Top + (int)(game.Height * 0.25), (int)(game.Width * 0.20), (int)(game.Height * 0.40));

    static bool Red(uint c) { int r = (int)(c >> 16) & 255, g = (int)(c >> 8) & 255, b = (int)c & 255; return r > 170 && g < 110 && r - g > 90 && r - b > 50; }
    static bool Cyan(uint c) { int r = (int)(c >> 16) & 255, g = (int)(c >> 8) & 255, b = (int)c & 255; return b > 150 && g > 120 && r < 140 && b - r > 60; }
    /// <summary>The name's ink: green, or teal in a group; never orange damage, white or gold.</summary>
    public static bool Ink(uint c) { int r = (int)(c >> 16) & 255, g = (int)(c >> 8) & 255, b = (int)c & 255; return g > 110 && g - r > 45 && (g - b > 25 || (b > 120 && b - r > 45)); }

    /// <summary>Ink at a pixel or a neighbour: the name's thin strokes, green on grass, lose their edges to the colour test, and the OCR misses thin letters.</summary>
    static bool Thick(uint[] px, int w, int h, int x, int y)
    {
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int xx = x + dx, yy = y + dy;
                if (xx >= 0 && yy >= 0 && xx < w && yy < h && Ink(px[yy * w + xx])) return true;
            }
        return false;
    }

    /// <summary>The plate's bar nearest the middle column: its top row and its two ends. Null when there is none.</summary>
    public static (int Top, int Left, int Right)? FindBar(uint[] px, int w, int h, int gameWidth)
    {
        int minLen = Math.Max(20, (int)(gameWidth * 0.018)), maxLen = (int)(gameWidth * 0.07), mid = w / 2;
        (int Top, int Left, int Right)? best = null; int bestOff = int.MaxValue;
        for (int y = 0; y < h - 8; y++)
        {
            int o = y * w;
            for (int x = 0; x < w; x++)
            {
                if (!Red(px[o + x])) continue;
                int x0 = x;
                while (x < w && (Red(px[o + x]) || (x + 1 < w && Red(px[o + x + 1])))) x++;
                int len = x - x0;
                if (len < minLen || len > maxLen) continue;
                // the mana line: cyan under most of the bar within a few rows (a monster's bar has none)
                bool cyan = false;
                for (int dy = 1; dy <= 8 && !cyan && y + dy < h; dy++)
                {
                    int n = 0, oo = (y + dy) * w;
                    for (int xx = x0; xx < x; xx++) if (Cyan(px[oo + xx])) n++;
                    cyan = n >= len / 3;
                }
                if (!cyan) continue;
                int off = Math.Abs((x0 + x) / 2 - mid);
                if (off < bestOff && off <= gameWidth * 0.03) { bestOff = off; best = (y, x0, x); }
            }
        }
        return best;
    }

    /// <summary>
    /// The name's line over the bar as a picture for the OCR: the ink rows closest above the bar, cut to their width, ink
    /// dark on white, three times the size. Null when no line of ink sits there.
    /// </summary>
    public static Bitmap? NameStrip(uint[] px, int w, int h, (int Top, int Left, int Right) bar, int gameHeight)
    {
        int len = bar.Right - bar.Left, cx = (bar.Left + bar.Right) / 2;
        int x0 = Math.Max(0, cx - len * 3 / 2), x1 = Math.Min(w, cx + len * 3 / 2);
        int y1 = bar.Top - 1, y0 = Math.Max(0, bar.Top - (int)(gameHeight * 0.035));
        if (y1 - y0 < 6) return null;
        var rows = new int[y1 - y0];
        for (int y = y0; y < y1; y++) for (int x = x0; x < x1; x++) if (Ink(px[y * w + x])) rows[y - y0]++;
        // the lowest band of rows with ink, upward from the bar: the name (a title would be the band above)
        int need = Math.Max(2, (x1 - x0) / 120);
        int bottom = -1;
        for (int i = rows.Length - 1; i >= 0; i--) if (rows[i] >= need) { bottom = i; break; }
        if (bottom < 0) return null;
        int top = bottom, gap = 0;
        for (int i = bottom - 1; i >= 0; i--)
        {
            if (rows[i] >= need) { top = i; gap = 0; }
            else if (++gap > Math.Max(2, gameHeight / 700)) break;
        }
        // a title or a damage number just above can join the band: the name is the bottom 1.5% of the height at most
        top = Math.Max(top, bottom - (int)(gameHeight * 0.015));
        int lineH = bottom - top + 1;
        if (lineH < gameHeight * 0.005 || lineH > gameHeight * 0.03) return null;
        int ty = y0 + top - 2, by = y0 + bottom + 3;
        int lx = x1, rx = x0;
        for (int y = Math.Max(0, ty); y < Math.Min(h, by); y++) for (int x = x0; x < x1; x++) if (Ink(px[y * w + x])) { if (x < lx) lx = x; if (x > rx) rx = x; }
        if (rx <= lx) return null;
        lx = Math.Max(0, lx - 4); rx = Math.Min(w - 1, rx + 4); ty = Math.Max(0, ty); by = Math.Min(h, by);
        const int S = 3, Pad = 12;
        var bmp = new Bitmap((rx - lx + 1) * S + Pad * 2, (by - ty) * S + Pad * 2, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(bmp)) g.Clear(Color.White);
        var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
        try
        {
            var row = new byte[data.Stride];
            for (int yy = 0; yy < bmp.Height; yy++)
            {
                Array.Fill(row, (byte)255);
                int sy = ty + (yy - Pad) / S;
                if (yy >= Pad && sy < by)
                    for (int xx = Pad; xx < bmp.Width - Pad; xx++)
                    {
                        int sx = lx + (xx - Pad) / S;
                        if (sx <= rx && Thick(px, w, h, sx, sy)) { row[xx * 3] = 30; row[xx * 3 + 1] = 30; row[xx * 3 + 2] = 30; }
                    }
                System.Runtime.InteropServices.Marshal.Copy(row, 0, data.Scan0 + yy * data.Stride, data.Stride);
            }
        }
        finally { bmp.UnlockBits(data); }
        return bmp;
    }

    /// <summary>An OCR line as a character name: letters and digits only, 2 to 16 of them, at least two letters; null otherwise.</summary>
    public static string? Clean(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        // a name is one word; a line of several (a title, a guild tag) is not this line
        if (parts.Length != 1) return null;
        var s = new string(parts[0].Where(char.IsLetterOrDigit).ToArray());
        if (s.Length < 2 || s.Length > 16 || s.Count(char.IsLetter) < 2) return null;
        // the OCR adds or drops a stray mark at an edge more than inside a word
        return parts[0].Trim('\'', '"', '`', '.', ',', '[', ']', '(', ')', '|', '-', '_').All(char.IsLetterOrDigit) ? s : null;
    }

    /// <summary>The name in a picture of the middle of the game (the region, or a whole frame): found, cut, read. Null when there is none.</summary>
    public static async Task<string?> ReadAsync(Bitmap picture, int gameWidth, int gameHeight)
    {
        var px = CharacterBadge.Pixels(picture, out int w, out int h);
        if (FindBar(px, w, h, gameWidth) is not { } bar) return null;
        using var strip = NameStrip(px, w, h, bar, gameHeight);
        if (strip == null) return null;
        var lines = await ScreenReader.ReadBitmapAsync(strip, false, 1);
        return lines.Count == 1 ? Clean(lines[0]) : null;
    }
}
