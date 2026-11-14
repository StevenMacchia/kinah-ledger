using System.Drawing;

namespace KinahLedger.Ocr;

/// <summary>
/// The party list in the game's top-left corner (Steven, 2026-11-14: "brother... it's literally an icon in the top left"):
/// one row per member, each with the class's icon in a diamond at its left (the level under it), the name, then a red
/// health bar over a cyan mana bar. This finds the rows by their mana bars, cuts each class icon out, and names the class
/// by the icon's colour and shape: each class's glyph has its own colour in the game (Chanter orange, Cleric gold,
/// Templar blue, Gladiator light blue, Ranger teal, Sorcerer purple, Spiritmaster magenta, Assassin green), and its shape
/// is matched as the character badge is (<see cref="CharacterBadge"/>: brightness on a grid, compared by correlation). A
/// member who is away shows a grey icon and no mana bar: their row is found from the rows' spacing, and their class from
/// the shape alone.
/// <para>Built on 911 icons from Steven's 492 reward pictures (5120 × 2160, 2026-11-07 to 14): all eight classes, every
/// online icon named right; sizes and offsets are fractions of the game window's height, as the interface scales with it.</para>
/// </summary>
public static class PartyFrame
{
    /// <summary>One member's row: where its mana bar is, the class (a key of Ui.ClassArt, "" when not matched), how sure, whether online, and where the name is.</summary>
    public sealed record Member(int Row, string Class, double Score, bool Online, Rectangle NameBox, Rectangle IconBox);

    /// <summary>The part of the game window to capture: the left edge, from a tenth of the height down to seven tenths.</summary>
    public static Rectangle Region(Rectangle game) =>
        new(game.Left, game.Top + (int)(game.Height * 0.10), Math.Min(game.Width, (int)(game.Height * 0.24)), (int)(game.Height * 0.60));

    /// <summary>Each class's icon as the game draws it in the party list: its glyph's hue, the glyph's colour, and its brightness on a 12 × 12 grid (hex, 00 outside the diamond).</summary>
    public sealed record Ref(string Class, int Hue, int Rgb, string Grid);

    public static readonly Ref[] Refs =
    {
        new("chanter", 32, 0xCC995C, "000000000000000000000000000000000062400000000000000000003f31734600000000000000002f80ad3100000000000000a647aa536392000000000025ac38902e7a8695000000351fa123a54c6b2e97130000113b7f536a982324901b0000008764771d50491d6d00000000005310945d5712000000000000381b7c8a354f000000000000001573742900000000"),
        new("cleric", 47, 0xCCB972, "00000000000000000000000000000000005d3f0000000000000000003834a24400000000000000006744a931000000000000002cc4b9bab63700000000005f32712b9ea16d69000000361686402599a9661b140000111ebf3d1f8bae3a9a130000000fb13a198dae67590000000000714e158b99ba0000000000000da713847138000000000000000c89ad2400000000"),
        new("templar", 223, 0x718DD5, "0000000000000000000000000000000000623b0000000000000000003d728c580000000000000000897f368e00000000000000893072632d23000000000069942b7092292b68000000336f931b7c23767c5712000011305a27857c1a896a12000000105a5217761582550000000000628d112e2d720000000000000d723f0f8847000000000000001b5b746b00000000"),
        new("gladiator", 192, 0x7AC5D9, "000000000000000000000000000000000060400000000000000000003c337f4300000000000000004e2f993f00000000000000428a29b8592800000000001e5e9e24bb2ca21f00000030187b222fbc2769191500001011851d47af1d173012000000b78a5f27976b14a60000000000ae266d7c1ba10000000000002eac0d8d5c6f0000000000000012147a7d00000000"),
        new("ranger", 156, 0x60AB8B, "0000000000000000000000000000000000603f000000000000000000393553460000000000000000343a9233000000000000003b862d883d57000000000024862a2e7e2e4c73000000342b842728792b1f78120000116e421f1f6e221a7023000000382f22176c195d110000000000104a126c42140000000000000e0f5e561e0f000000000000000e375d0d00000000"),
        new("sorcerer", 263, 0xA27DD8, "000000000000000000000000000000000069350000000000000000004c367e4200000000000000002e3c513c000000000000004e746ba02c6100000000001a7d297d2f75272f0000002f2a45a02147704038140000114f1e72222d921a3412000000773c138e5c1f1d5e000000000058901b6d6e700000000000006818556b1133000000000000007b4b475700000000"),
        new("spiritmaster", 299, 0xB766B7, "0000000000000000000000000000000000673a0000000000000000003f394a3e00000000000000005b2e77430000000000000045355f56362c000000000044212c5a8d2c29520000002e1f4e3e2455676021130000236e6e7d357471476e380000004c12514d325c1847000000000019125c4512100000000000003f142b7c0e59000000000000002441643d00000000"),
        new("assassin", 111, 0x89CE79, "000000000000000000000000000000000065400000000000000000003f3b3c450000000000000000353c3d3900000000000000723247379a2c000000000020658d5232b6aa1b000000305bbf347142609e9c140000111361c276b33d9c1413000000131236bdbbb91c13000000000056b151a3b2ab000000000000b7690f0f29b3000000000000000d0e0f0e00000000"),
    };

    const int G = 12;

    static bool Cyan(uint c) { int r = (int)(c >> 16) & 255, g = (int)(c >> 8) & 255, b = (int)c & 255; return b > 160 && g > 130 && r < 130 && b - r > 70; }

    /// <summary>The members in a capture of <see cref="Region"/> (or any picture whose left edge is the game's), top to bottom; empty when there is no party list.</summary>
    public static List<Member> Read(Bitmap bmp, int gameHeight)
    {
        var px = CharacterBadge.Pixels(bmp, out int w, out int h);
        return Read(px, w, h, gameHeight);
    }

    public static List<Member> Read(uint[] px, int w, int h, int gameHeight)
    {
        double gh = gameHeight;
        int x0 = (int)(gh * 0.028), x1 = Math.Min(w - 1, (int)(gh * 0.215));
        int minRun = Math.Max(6, (int)(gh * 0.02));
        // rows with a cyan run: the mana bars (a member low on mana has a short one; its left edge lines up with the others)
        var hits = new List<(int Y, int Left)>();
        for (int y = 0; y < h; y++)
        {
            int o = y * w, run = 0, start = 0, best = 0, bestStart = 0;
            for (int x = x0; x < x1; x++)
            {
                if (Cyan(px[o + x])) { if (run == 0) start = x; run++; if (run > best) { best = run; bestStart = start; } }
                else run = 0;
            }
            if (best >= minRun) hits.Add((y, bestStart));
        }
        // runs of rows are bars: a bar is a few rows thick (0.25% to 0.8% of the height)
        var bars = new List<(int Y, int Left)>();
        int i = 0;
        while (i < hits.Count)
        {
            int j = i;
            while (j + 1 < hits.Count && hits[j + 1].Y - hits[j].Y <= 2) j++;
            int thick = hits[j].Y - hits[i].Y + 1;
            if (thick >= gh * 0.0025 && thick <= gh * 0.008) bars.Add((hits[(i + j) / 2].Y, hits.Skip(i).Take(j - i + 1).Min(x => x.Left)));
            i = j + 1;
        }
        if (bars.Count == 0) return new();
        // the bars that line up (the same left edge as most), a row apart: the pitch is about 4.4% of the height
        int left = bars.GroupBy(b => b.Left / Math.Max(2, (int)(gh * 0.003))).OrderByDescending(g => g.Count()).First().Min(b => b.Left);
        var rows = bars.Where(b => Math.Abs(b.Left - left) <= gh * 0.004).Select(b => b.Y).OrderBy(y => y).ToList();
        double pitch = gh * 0.0444;
        rows = rows.Where((y, k) => k == 0 || y - rows[k - 1] >= pitch * 0.6).ToList();
        if (rows.Count == 0) return new();
        // a row a pitch or more apart from the next is a member with no mana bar (away): their row is filled in
        var all = new List<(int Y, bool Online)>();
        for (int k = 0; k < rows.Count; k++)
        {
            all.Add((rows[k], true));
            if (k + 1 < rows.Count)
            {
                int n = (int)Math.Round((rows[k + 1] - rows[k]) / pitch);
                for (int m = 1; m < n && n <= 8; m++) all.Add((rows[k] + (int)Math.Round(m * (rows[k + 1] - rows[k]) / (double)n), false));
            }
        }
        if (all.Count > 8) return new();   // eight is a full force: more is something else
        var res = new List<Member>();
        foreach (var (y, online) in all)
        {
            double cx = left - gh * 0.0167, cy = y - gh * 0.0106, half = gh * 0.0167;
            var icon = Rectangle.FromLTRB((int)(cx - half), (int)(cy - half), (int)(cx + half), (int)(cy + half));
            var name = Rectangle.FromLTRB(left + (int)(gh * 0.026), y - (int)(gh * 0.027), Math.Min(w, left + (int)(gh * 0.15)), y - (int)(gh * 0.009));
            if (icon.Left < 0 || icon.Top < 0 || icon.Right >= w || icon.Bottom >= h) continue;
            var (hue, share, grid) = Features(px, w, cx, cy, half);
            var (cls, score) = Match(hue, share, grid);
            res.Add(new Member(y, cls, score, online, name, icon));
        }
        return res;
    }

    /// <summary>The icon's glyph hue (null when colourless: an away member's grey icon), the share of glyph pixels, and its brightness grid (0 outside the diamond).</summary>
    public static (double? Hue, double Share, int[] Grid) Features(uint[] px, int w, double cx, double cy, double half)
    {
        var grid = new int[G * G];
        double sx = 0, sy = 0; int glyph = 0, n = 0;
        for (int gy = 0; gy < G; gy++)
            for (int gx = 0; gx < G; gx++)
            {
                double x = cx - half + (gx + 0.5) * 2 * half / G, yy = cy - half + (gy + 0.5) * 2 * half * 0.85 / G;
                if (Math.Abs(x - cx) + Math.Abs(yy - cy) > half * 0.92) continue;
                double s = 0; int k = 0;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        uint c = px[((int)yy + dy) * w + (int)x + dx];
                        int r = (int)(c >> 16) & 255, g = (int)(c >> 8) & 255, b = (int)c & 255;
                        s += 0.299 * r + 0.587 * g + 0.114 * b; k++; n++;
                        int max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
                        double v = max / 255.0, sat = max == 0 ? 0 : (max - min) / (double)max;
                        if (sat > 0.35 && v > 0.45)
                        {
                            glyph++;
                            double hue = Hue(r, g, b);
                            sx += Math.Cos(hue * Math.PI / 180); sy += Math.Sin(hue * Math.PI / 180);
                        }
                    }
                grid[gy * G + gx] = Math.Max(1, (int)(s / k));
            }
        double? h = glyph == 0 ? null : (Math.Atan2(sy, sx) * 180 / Math.PI + 360) % 360;
        return (h, n == 0 ? 0 : glyph / (double)n, grid);
    }

    static double Hue(int r, int g, int b)
    {
        int max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        if (max == min) return 0;
        double d = max - min, h = max == r ? (g - b) / d % 6 : max == g ? (b - r) / d + 2 : (r - g) / d + 4;
        return (h * 60 + 360) % 360;
    }

    static int[] Parse(string hex) => Enumerable.Range(0, hex.Length / 2).Select(i => Convert.ToInt32(hex.Substring(i * 2, 2), 16)).ToArray();
    static readonly Dictionary<string, int[]> RefGrids = Refs.ToDictionary(r => r.Class, r => Parse(r.Grid));

    /// <summary>Correlation of two grids over the cells both have (-1 to 1).</summary>
    public static double Correlation(int[] a, int[] b)
    {
        double mx = 0, my = 0; int n = 0;
        for (int i = 0; i < a.Length && i < b.Length; i++) if (a[i] != 0 && b[i] != 0) { mx += a[i]; my += b[i]; n++; }
        if (n < 20) return 0;
        mx /= n; my /= n;
        double sxy = 0, sxx = 0, syy = 0;
        for (int i = 0; i < a.Length && i < b.Length; i++)
            if (a[i] != 0 && b[i] != 0) { sxy += (a[i] - mx) * (b[i] - my); sxx += (a[i] - mx) * (a[i] - mx); syy += (b[i] - my) * (b[i] - my); }
        return sxx <= 0 || syy <= 0 ? 0 : sxy / Math.Sqrt(sxx * syy);
    }

    /// <summary>
    /// The class an icon is: of the classes whose glyph colour is within 30° of the icon's (any, for a grey icon), the one
    /// whose shape correlates best; kept at 0.55 and up for a coloured icon, 0.75 for a grey one. ("", score) when none is.
    /// </summary>
    public static (string Class, double Score) Match(double? hue, double share, int[] grid)
    {
        bool colourful = hue is not null && share >= 0.08;
        string best = ""; double score = double.MinValue;
        foreach (var r in Refs)
        {
            if (colourful && HueGap(hue!.Value, r.Hue) > 30) continue;
            double c = Correlation(grid, RefGrids[r.Class]);
            if (c > score) { score = c; best = r.Class; }
        }
        if (best.Length == 0) return ("", 0);
        return score >= (colourful ? 0.55 : 0.75) ? (best, score) : ("", score);
    }

    static double HueGap(double a, double b) { double d = Math.Abs(a - b) % 360; return Math.Min(d, 360 - d); }

    /// <summary>
    /// A name as the party list prints it, tidied for matching: the signal bars' dots before it and a guild's tag after it
    /// ("SwolyDaBooly", "OHaa" from ".ııl OHaa[ISR]").
    /// </summary>
    public static string CleanName(string read)
    {
        var t = read.Trim();
        int b = t.IndexOf('[');
        if (b > 0) t = t[..b];
        t = new string(t.SkipWhile(c => !char.IsLetterOrDigit(c)).ToArray()).Trim();
        int sp = t.LastIndexOf(' ');
        if (sp > 0 && t[..sp].All(c => !char.IsLetter(c) || c is 'l' or 'I' or 'i' or 'ı')) t = t[(sp + 1)..];   // what's left of the signal bars
        return t;
    }
}
