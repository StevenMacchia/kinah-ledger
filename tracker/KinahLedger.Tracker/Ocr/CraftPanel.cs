using System.Drawing;
using System.Text.RegularExpressions;

namespace KinahLedger.Ocr;

/// <summary>
/// The crafting panel at the bottom centre of the screen while crafts run (Steven, 2026-10-22: "you can queue like 100
/// crafts and never see the chat"): "Crafting in Progress 1 / 2", the recipe ("Life Serum (Bound) (Novice Lv. 1)"), and
/// two bars that race, Success (blue) and Fail (orange). The bar that fills first is that craft's outcome. A combo roll
/// runs the same race on a blue-tinted panel with an "x2" badge. Geometry measured on a 5120×2160 frame and kept in
/// screen heights from the centre, so it holds on any size. Measuring the bars is pixels only, cheap enough every pass;
/// the text is read only when asked.
/// </summary>
public static class CraftPanel
{
    /// <summary>One look at the panel: whether it is up, how full each bar is (0 to 1), and whether it is a combo roll.</summary>
    public sealed record Sample(bool Visible, double Success, double Fail, bool Combo)
    {
        /// <summary>The panel sits higher, above the skill bar: the gathering panel (never a craft).</summary>
        public bool Raised { get; init; }
    }

    // The gathering panel is the same widget, raised above the skill bar by 213 px on a 2160-high screen (measured
    // 2026-10-24 on the 2026-10-23 Inina frames at 1080: labels at 848 and 871 against the crafting panel's 955 and 978).
    // Until then its bars were never read, so a gather the chat missed had no second witness (Steven's missed Aria)
    const double GatherShift = 213 / 2160.0;

    // measured 2026-10-22 on 5120×2160: bars from x 2227 to 2998 (centre 2560), Success at y 1910, Fail at y 1956
    const double BarLeft = (2227 - 2560) / 2160.0, BarRight = (2998 - 2560) / 2160.0;
    const double SuccessY = 1910 / 2160.0, FailY = 1956 / 2160.0;
    const double TextTop = 1725 / 2160.0, TextBottom = 1870 / 2160.0, PanelLeft = (2086 - 2560) / 2160.0, PanelRight = (3030 - 2560) / 2160.0;

    static bool Blue(Color c) => c.B > 150 && c.B > c.R + 60 && c.G > 90;
    static bool Orange(Color c) => c.R > 190 && c.G is > 95 and < 175 && c.B < 90;

    // The panel is see-through, so its colours are no test that it is up (the empty track takes the world's colour, and
    // the crafting window's dark frame passes any darkness test). The white "Success" and "Fail" labels at the bars'
    // left ends are: their shapes, sampled to a 48×10 grid, measured 2026-10-22. On 54 crafting frames every panel frame
    // matched both at 0.88 or more; every other frame (the crafting window, the world) at 0.17 or less.
    const int GridW = 48, GridH = 10;
    const double LabelMatch = 0.6;
    static readonly (int X0, int X1, int Y0, int Y1, string Mask)[] Labels =
    {
        (2227, 2347, 1896, 1924,
            "000000000000000000000000000000000000000000000000" +
            "000000000000000000000000000000000000000000000000" +
            "000111110000000000000000000000000000000000000000" +
            "000100000010001000011000011000011000011000010000" +
            "000110000010001001100001100100110110110101100000" +
            "000011100010001001000001100000100010110001100000" +
            "000000010010001001000001100001111110001100011000" +
            "000000010010001001100001100000100000000110001100" +
            "000111100001101000111100011100011110011100111000" +
            "000000000000000000000000000000000000000000000000"),
        (2227, 2287, 1942, 1970,
            "000000000000000000000000000000000000000000000000" +
            "000000111111111110000000000000001110001100000000" +
            "000000111000000000000000000000000000001100000000" +
            "000000111000000000011111110000001100001110000000" +
            "000000111111111100000000011100001110001110000000" +
            "000000111111111100001111111100001110001100000000" +
            "000000111000000001110000011100001100001100000000" +
            "000000110000000000111000111100001100001110000000" +
            "000000110000000000000110001100001100000001100000" +
            "000000000000000000000000000000000000000000000000"),
    };

    /// <summary>
    /// A picture of part of the game: <paramref name="bmp"/> shows <paramref name="area"/>, both in screen pixels, of the
    /// game window <paramref name="game"/>. Positions are worked out on the game and looked up in the picture.
    /// </summary>
    readonly record struct View(Bitmap Bmp, Rectangle Area, Rectangle Game, int Dy = 0, int Dx = 0, Pixels? Px = null)
    {
        public int H => Game.Height;
        public int Cx => Game.Width / 2;
        /// <summary>The pixel at a point of the game window, or null outside the picture.</summary>
        public Color? At(int gx, int gy)
        {
            int x = Game.X + gx + Dx - Area.X, y = Game.Y + gy + Dy - Area.Y;
            if (Px is { } p) return x < 0 || y < 0 || x >= p.W || y >= p.H ? null : p.At(x, y);
            return x < 0 || y < 0 || x >= Bmp.Width || y >= Bmp.Height ? null : Bmp.GetPixel(x, y);
        }

        /// <summary>The pixel's darkest channel (what the labels' white test reads), or -1 outside the picture.</summary>
        public int Min3(int gx, int gy)
        {
            int x = Game.X + gx + Dx - Area.X, y = Game.Y + gy + Dy - Area.Y;
            if (Px is { } p) return x < 0 || y < 0 || x >= p.W || y >= p.H ? -1 : p.Min3(x, y);
            if (x < 0 || y < 0 || x >= Bmp.Width || y >= Bmp.Height) return -1;
            var c = Bmp.GetPixel(x, y);
            return Math.Min(c.R, Math.Min(c.G, c.B));
        }
    }

    /// <summary>
    /// A picture's own pixels, locked once for a whole look (ocr.craft_pixels, 2026-10-26): what GetPixel would give for
    /// each pixel, without a GDI+ call per pixel. The look for the raised gathering panel tries 81 places on a 2160-high
    /// screen, about 780,000 pixels, which cost 100 to 160 ms a pass one GetPixel at a time. Only the formats whose bytes
    /// are the colour as GetPixel gives it; any other picture (or one locked elsewhere) is read with GetPixel, as before.
    /// </summary>
    sealed unsafe class Pixels : IDisposable
    {
        readonly Bitmap _bmp;
        readonly System.Drawing.Imaging.BitmapData _data;
        readonly byte* _scan;
        readonly int _stride, _bpp;
        readonly bool _alpha;
        /// <summary>The picture's size, kept: Bitmap.Width and Height are a GDI+ call each.</summary>
        public readonly int W, H;

        Pixels(Bitmap bmp, System.Drawing.Imaging.BitmapData data, int bpp, bool alpha)
        {
            _bmp = bmp; _data = data; _bpp = bpp; _alpha = alpha;
            _scan = (byte*)data.Scan0; _stride = data.Stride; W = data.Width; H = data.Height;
        }

        public static Pixels? Lock(Bitmap bmp)
        {
            if (!KinahLedger.Update.RemoteConfig.Current.CraftPixels) return null;
            var f = bmp.PixelFormat;
            int bpp = f is System.Drawing.Imaging.PixelFormat.Format32bppArgb or System.Drawing.Imaging.PixelFormat.Format32bppRgb ? 4
                : f == System.Drawing.Imaging.PixelFormat.Format24bppRgb ? 3 : 0;
            if (bpp == 0) return null;
            try { return new Pixels(bmp, bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), System.Drawing.Imaging.ImageLockMode.ReadOnly, f), bpp, f == System.Drawing.Imaging.PixelFormat.Format32bppArgb); }
            catch (Exception) { return null; }
        }

        /// <summary>The pixel at (x, y), inside the picture (the caller checks).</summary>
        public Color At(int x, int y)
        {
            byte* p = _scan + (long)y * _stride + x * _bpp;
            return Color.FromArgb(_alpha ? p[3] : 255, p[2], p[1], p[0]);
        }

        /// <summary>The darkest of the pixel's three channels at (x, y), inside the picture (the caller checks).</summary>
        public int Min3(int x, int y)
        {
            byte* p = _scan + (long)y * _stride + x * _bpp;
            int m = p[0] < p[1] ? p[0] : p[1];
            return m < p[2] ? m : p[2];
        }

        public void Dispose() => _bmp.UnlockBits(_data);
    }

    /// <summary>The part of the game window the panel's text, labels and bars sit in, in screen pixels: all a read needs.</summary>
    public static Rectangle Region(Rectangle game)
    {
        int h = game.Height, cx = game.X + game.Width / 2;
        // from the raised (gathering) panel's text to the crafting panel's bars
        return Rectangle.FromLTRB(cx + (int)(PanelLeft * h), game.Y + (int)((TextTop - GatherShift) * h), cx + (int)(PanelRight * h) + 2, game.Y + (int)(1975 / 2160.0 * h));
    }

    /// <summary>The two labels' measured cells, for drawing test panels (ReplayBank.Seed).</summary>
    internal static IReadOnlyList<(int X0, int X1, int Y0, int Y1, string Mask)> LabelShapes => Labels;

    /// <summary>How well a label's white pixels match its shape: intersection over union of the two grids, 0 to 1.</summary>
    static double LabelScore(View v, (int X0, int X1, int Y0, int Y1, string Mask) l)
    {
        double k = v.H / 2160.0;
        int both = 0, any = 0;
        for (int j = 0; j < GridH; j++)
            for (int i = 0; i < GridW; i++)
            {
                int x = v.Cx + (int)Math.Round((l.X0 - 2560 + (l.X1 - l.X0) * (i + 0.5) / GridW) * k);
                int y = (int)Math.Round((l.Y0 + (l.Y1 - l.Y0) * (j + 0.5) / GridH) * k);
                int m = v.Min3(x, y);
                if (m < 0) return 0;
                bool white = m > 185, want = l.Mask[j * GridW + i] == '1';
                if (white || want) any++;
                if (white && want) both++;
            }
        return any == 0 ? 0 : both / (double)any;
    }

    /// <summary>
    /// The same match, with each grid cell white when any pixel in its area is (not only its centre): the raised gathering
    /// panel's labels draw a little thinner and shifted against the crafting panel's shapes (the 2026-10-23 Inina frames:
    /// 0.49 and 0.56 this way, 0 on frames without the panel, where centre sampling gave 0.32).
    /// </summary>
    static double AreaScore(View v, (int X0, int X1, int Y0, int Y1, string Mask) l)
    {
        double k = v.H / 2160.0;
        int both = 0, any = 0;
        for (int j = 0; j < GridH; j++)
            for (int i = 0; i < GridW; i++)
            {
                int xa = v.Cx + (int)Math.Floor((l.X0 - 2560 + (l.X1 - l.X0) * i / (double)GridW) * k), xb = v.Cx + (int)Math.Ceiling((l.X0 - 2560 + (l.X1 - l.X0) * (i + 1) / (double)GridW) * k);
                int ya = (int)Math.Floor((l.Y0 + (l.Y1 - l.Y0) * j / (double)GridH) * k), yb = (int)Math.Ceiling((l.Y0 + (l.Y1 - l.Y0) * (j + 1) / (double)GridH) * k);
                bool white = false;
                for (int y = ya; y < yb && !white; y++)
                    for (int x = xa; x < xb; x++)
                    {
                        int m = v.Min3(x, y);
                        if (m < 0) return 0;
                        if (m > 185) { white = true; break; }
                    }
                bool want = l.Mask[j * GridW + i] == '1';
                if (white || want) any++;
                if (white && want) both++;
            }
        return any == 0 ? 0 : both / (double)any;
    }

    /// <summary>The raised panel's labels: both at least this (AreaScore).</summary>
    const double RaisedMatch = 0.4;

    /// <summary>Whether the crafting panel is on screen in a picture of the whole game window: both bar labels in place.</summary>
    public static bool IsUp(Bitmap f) { using var px = Pixels.Lock(f); var v = Whole(f, px); return Labels.All(l => LabelScore(v, l) >= LabelMatch); }

    static View Whole(Bitmap f, Pixels? px) => new(f, new Rectangle(0, 0, f.Width, f.Height), new Rectangle(0, 0, f.Width, f.Height), Px: px);

    /// <summary>The panel in a picture of the whole game window.</summary>
    public static Sample Measure(Bitmap f) { using var px = Pixels.Lock(f); return Measure(Whole(f, px)); }

    /// <summary>The panel in a picture of <see cref="Region"/> (or any area holding it) of the game window <paramref name="game"/>.</summary>
    public static Sample Measure(Bitmap bmp, Rectangle area, Rectangle game) { using var px = Pixels.Lock(bmp); return Measure(new View(bmp, area, game, Px: px)); }

    static Sample Measure(View v)
    {
        var s = MeasureAt(v);
        if (s.Visible) return s;
        // the raised (gathering) panel: its labels found within a couple of pixels of where they should be
        int dy0 = -(int)Math.Round(GatherShift * v.H), r = Math.Max(2, (int)Math.Round(2 * v.H / 1080.0));
        View? best = null; double bestScore = 0;
        for (int dy = dy0 - r; dy <= dy0 + r; dy++)
            for (int dx = -r; dx <= r; dx++)
            {
                var w = v with { Dy = dy, Dx = dx };
                double sc = Labels.Min(l => AreaScore(w, l));
                if (sc > bestScore) { bestScore = sc; best = w; }
            }
        if (best is not { } bv || bestScore < RaisedMatch) return s;
        var raised = MeasureAt(bv, labelsChecked: true);
        return raised.Visible ? raised with { Raised = true } : s;
    }

    static Sample MeasureAt(View v, bool labelsChecked = false)
    {
        int h = v.H, cx = v.Cx;
        int x0 = cx + (int)(BarLeft * h), x1 = cx + (int)(BarRight * h);
        int ys = (int)(SuccessY * h), yf = (int)(FailY * h);
        if (v.At(x0, ys) == null || v.At(x1, yf) == null) return new Sample(false, 0, 0, false);
        if (!labelsChecked && !Labels.All(l => LabelScore(v, l) >= LabelMatch)) return new Sample(false, 0, 0, false);
        double Fill(int y, Func<Color, bool> isFill)
        {
            // the rightmost filled pixel: the bar's label sits over its left end, so only the fill's reach counts
            for (int x = x1; x >= x0; x--) if (v.At(x, y) is { } c && isFill(c)) return (x - x0 + 1) / (double)(x1 - x0 + 1);
            return 0;
        }
        // a combo roll tints the whole panel blue
        bool combo = v.At(cx + (int)(PanelLeft * h) + 30, (int)(TextBottom * h) - 30) is { } bg && bg.B > bg.R + 35 && bg.B > 70;
        return new Sample(true, Fill(ys, Blue), Fill(yf, Orange), combo);
    }

    /// <summary>The lines the last text read saw, top to bottom, for the reads log.</summary>
    public static IReadOnlyList<string> LastLines { get; private set; } = Array.Empty<string>();

    /// <summary>The panel's text: how far the queue is ("1 / 2") and the recipe, with "(Bound)" and the level kept as read.</summary>
    public static Task<(int Done, int Of, string Recipe)?> ReadTextAsync(Bitmap f) => ReadTextAsync(f, new Rectangle(0, 0, f.Width, f.Height), new Rectangle(0, 0, f.Width, f.Height));

    /// <summary>The same, from a picture of <paramref name="area"/> of the game window <paramref name="game"/> (screen pixels).</summary>
    public static async Task<(int Done, int Of, string Recipe)?> ReadTextAsync(Bitmap f, Rectangle area, Rectangle game)
    {
        int h = game.Height, cx = game.X + game.Width / 2;
        var text = Rectangle.FromLTRB(cx + (int)(PanelLeft * h) - area.X, game.Y + (int)(TextTop * h) - area.Y, cx + (int)(PanelRight * h) - area.X, game.Y + (int)(TextBottom * h) - area.Y);
        text.Intersect(new Rectangle(0, 0, f.Width, f.Height));
        if (text.Width < 8 || text.Height < 8) return null;
        using var bmp = f.Clone(text, f.PixelFormat);
        var lines = await ScreenReader.ReadBoxesAsync(bmp, text.Location, 2);
        LastLines = lines.OrderBy(l => l.Box.Top).Select(l => l.Text).ToList();
        return Parse(LastLines);
    }

    /// <summary>
    /// The panel's title line, read clean ("Crafting in Progress 1 / 2") or garbled: "Crafting in Br.egress*40",
    /// "Crafting in Rr.egteß+a•", "C åfting in", "Cråfting in Progresö1:'/..4" (Steven's crafting feed, 2026-10-25 17:08,
    /// where each of those was filed as a recipe of its own). A title is never the recipe; a recipe line carries the
    /// mastery level, which the title never does.
    /// </summary>
    public static bool IsTitleLine(string line) =>
        LevelOf(line) == 0
        && (line.Contains("Progress", StringComparison.OrdinalIgnoreCase)
            || Session.TrackerSession.IsCraftTitle(line)
            || Regex.IsMatch(line, @"(?:^|\W)[cg]?\s?r?[aåäà4]ft(?:ing|in|ng)\s+[il1]n\b", RegexOptions.IgnoreCase));

    /// <summary>The panel's lines top down, split into the queue ("1 / 2", read from the title) and the recipe (the first line that is neither the title nor the proficiency line).</summary>
    public static (int Done, int Of, string Recipe)? Parse(IEnumerable<string> topDown)
    {
        int done = 0, of = 0; string recipe = "";
        foreach (var text in topDown)
        {
            if (IsTitleLine(text))
            {
                // "Crafting in Progress 1 / 2"; the reader sometimes drops the spaces, reads the 1 as I or l, or litters the slash
                var m = Regex.Match(text.Replace('I', '1').Replace('l', '1').Replace('|', '/'), @"(\d+)\D{0,4}/\D{0,4}(\d+)\D{0,2}$");
                if (m.Success) { done = int.Parse(m.Groups[1].Value); of = int.Parse(m.Groups[2].Value); }
                continue;
            }
            if (recipe.Length == 0 && text.Trim().Length >= 3 && !text.Contains("Proficiency", StringComparison.OrdinalIgnoreCase)) recipe = text.Trim();
        }
        return of > 0 || recipe.Length > 0 ? (done, of, recipe) : null;
    }

    /// <summary>The recipe's item name without the level the panel adds: "Life Serum (Bound) (Novice Lv. 1)" → "Life Serum (Bound)".</summary>
    /// <summary>The recipe's mastery level in the panel's title ("Orichalcum Ingot (Novice Lv. 25)" → 25), 0 when it isn't read.</summary>
    public static int LevelOf(string recipe) => Regex.Match(recipe, @"\bLv\.?\s*(\d{1,3})", RegexOptions.IgnoreCase) is { Success: true } m ? int.Parse(m.Groups[1].Value) : 0;

    /// The panel's close button can come with the title as a lone "X" after the level (2026-10-25 07:22:39, a great success:
    /// "Fine Durable Balaur Horn (Professional Lv. 30) X"), which kept the level on and filed three crafts under a recipe
    /// that doesn't exist; that X, or a short run of symbols, after the level goes with it.
    public static string ItemOf(string recipe) => Regex.Replace(recipe, @"\s*\((?:novice|apprentice|journeyman|expert|master|professional|artisan|grand ?master)[^)]*\)(?:\s*(?:[XxＸ×✕✖]|[^\w\s]{1,3}))?\s*$", "", RegexOptions.IgnoreCase).Trim();
}

/// <summary>
/// Turns panel samples into crafts: each race ends when a bar is full (success when the blue one, failure when the
/// orange one), counted once; the next race starts when both bars are low again. A race on a combo panel is the combo
/// roll for the craft before it. Samples need to come often (a race lasts about five seconds; twice a second is plenty).
/// </summary>
public sealed class CraftPanelTracker
{
    public sealed record Outcome(DateTime At, bool Ok, bool Combo);

    public const double Full = 0.97, Low = 0.15;
    bool _done = true;

    public Outcome? Feed(DateTime t, CraftPanel.Sample s)
    {
        if (!s.Visible) return null;
        if (_done)
        {
            // a new race: both bars back near empty
            if (s.Success < Low && s.Fail < Low) _done = false;
            return null;
        }
        if (s.Success >= Full || s.Fail >= Full)
        {
            _done = true;
            return new Outcome(t, s.Success >= Full && s.Success >= s.Fail, s.Combo);
        }
        return null;
    }
}
