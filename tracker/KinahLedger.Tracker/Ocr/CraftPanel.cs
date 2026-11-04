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
    /// <summary>One look at the panel: whether it is up, how full each bar is (0 to 1), and whether its colour says a combo roll.</summary>
    public sealed record Sample(bool Visible, double Success, double Fail, bool Combo)
    {
        /// <summary>The panel sits higher, above the skill bar: the gathering panel (never a craft).</summary>
        public bool Raised { get; init; }
        /// <summary>
        /// The panel's own colour beside its text (<see cref="TintPatches"/>, averaged), for telling the blue upgrade-roll
        /// tint from what the queue's first craft showed (CraftPanelTracker); null when not measured.
        /// </summary>
        public (double R, double G, double B)? Panel { get; init; }
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
        // a combo roll tints the whole panel blue. Until 0.9.12 one pixel, 30 raw pixels in from the panel's corner whatever
        // the screen's size, said so; over sunlit grass the see-through panel read R111 G114 B85 there, and three upgrade
        // rolls in a queue of ten Orichalcum Longswords went in as plain crafts (2026-11-03 13:23). Now the patches beside
        // the text are averaged; the tracker compares them to the panel's colour on the queue's first craft and votes over
        // the race. It is the third sign of an upgrade roll, after the queue's count and the panel's title (TrackerSession).
        var panel = PanelColour(v);
        bool combo = panel is { } pc && pc.B - pc.R > 25 && pc.G - pc.R > 25 && pc.B > 70;
        return new Sample(true, Fill(ys, Blue), Fill(yf, Orange), combo) { Panel = panel };
    }

    /// <summary>
    /// Where the panel shows its own colour, clear of text, icon and bars (2160-high pixels, x from the centre): four down
    /// its left margin and three in the gap between the recipe's line and the Success bar. Measured 2026-11-03 on Steven's
    /// frames: about (88, 96, 54) over grass on a plain craft, (88, 138, 126) on the blue upgrade roll.
    /// </summary>
    internal static readonly (int Dx, int Y)[] TintPatches = { (-460, 1830), (-460, 1880), (-460, 1930), (-460, 1960), (-260, 1880), (40, 1880), (340, 1880) };

    /// <summary>The average colour of <see cref="TintPatches"/>, each a small square scaled with the screen; null when any falls outside the picture.</summary>
    static (double R, double G, double B)? PanelColour(View v)
    {
        double k = v.H / 2160.0;
        int r = Math.Max(1, (int)Math.Round(3 * k));
        long sr = 0, sg = 0, sb = 0; int n = 0;
        foreach (var (dx, y) in TintPatches)
        {
            int px = v.Cx + (int)Math.Round(dx * k), py = (int)Math.Round(y * k);
            for (int j = -r; j <= r; j += r)
                for (int i = -r; i <= r; i += r)
                {
                    if (v.At(px + i, py + j) is not { } c) return null;
                    sr += c.R; sg += c.G; sb += c.B; n++;
                }
        }
        return n == 0 ? null : (sr / (double)n, sg / (double)n, sb / (double)n);
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

    /// <summary>
    /// The panel's recipe line, read clean or not: the item, then its mastery tier and level in brackets ("Wind Serum
    /// (Novice Lv. 10)", read on 2026-10-25 as "Wind Serum (Novice Lv- 10)", "(Novice Lv. IO)", "Wihd Serum (Novite Lv. 10)").
    /// No other panel with the Success and Fail bars (a node's gathering bar, an Odyle cube) shows one, so it is a witness of
    /// the crafting window at work when the title above it comes out garbled.
    /// </summary>
    public static bool IsRecipeLine(string line) =>
        Regex.IsMatch(line, @"\([^()]{0,24}" + Lv + @"[^\w\s]{0,2}\s*[0-9IlOo|]{1,3}\b", RegexOptions.IgnoreCase)
        || Regex.IsMatch(line, @"\b(?:novice|apprentice|journeyman|expert|master|professional|artisan)\s+" + Lv + @"\b", RegexOptions.IgnoreCase);

    /// <summary>
    /// "Lv" as the reader gives it: "Lv", "Ly", and since 2026-10-27 "I-v", "L-v", "1v", "|v" (Steven's Mana Potions read as
    /// "Mana Potion (Novice I-v-40)1", each misread filed as a recipe of its own). Symbols may follow it before the digits
    /// ("Lv.€40").
    /// </summary>
    const string Lv = @"(?<![A-Za-z])[LI1|][\-\.]?[vy]";

    /// <summary>What can follow the level's bracket on the panel's title and goes with it: the close button's X, or a short run of stray marks ("1 '", "l", "~").</summary>
    const string AfterLevel = @"(?:\s*(?:[XxＸ×✕✖]|[^()\s]{1,3})){0,2}\s*$";

    /// <summary>
    /// The panel's lines top down, split into the queue ("1 / 2", read from the title) and the recipe: the line with the
    /// mastery level (<see cref="IsRecipeLine"/>), else the first line that is neither the title, the proficiency line nor
    /// a queue count. A title garbled past <see cref="IsTitleLine"/> ("qaf€ihgfiwprcgté$s 1 m/:ål" above "Wind Serum (Novice
    /// Lv- 10)", 2026-10-25 20:25:14) was the first such line and filed crafts under a recipe that doesn't exist.
    /// </summary>
    public static (int Done, int Of, string Recipe)? Parse(IEnumerable<string> topDown)
    {
        int done = 0, of = 0; string recipe = "", leveled = "";
        foreach (var text in topDown)
        {
            if (IsTitleLine(text))
            {
                // "Crafting in Progress 1 / 2"; the reader sometimes drops the spaces, reads the 1 as I or l, a 0 as O ("1 / 1O" would
                // be a queue of 1, and every race after the first an upgrade roll), or litters the slash
                var m = Regex.Match(text.Replace('I', '1').Replace('l', '1').Replace('O', '0').Replace('|', '/'), @"(\d+)\D{0,4}/\D{0,4}(\d+)\D{0,2}$");
                if (m.Success) { done = int.Parse(m.Groups[1].Value); of = int.Parse(m.Groups[2].Value); }
                continue;
            }
            if (leveled.Length == 0 && IsRecipeLine(text)) leveled = text.Trim();
            // a recipe's name has no slash; the title's queue count ("4 /z:iT,", "13 / Tl") has one
            if (recipe.Length == 0 && text.Trim().Length >= 3 && !text.Contains('/') && !text.Contains("Proficiency", StringComparison.OrdinalIgnoreCase)) recipe = text.Trim();
        }
        if (leveled.Length > 0) recipe = leveled;
        return of > 0 || recipe.Length > 0 ? (done, of, recipe) : null;
    }

    /// <summary>The recipe's mastery level in the panel's title ("Orichalcum Ingot (Novice Lv. 25)" → 25, "Lv- 10" as read too), 0 when it isn't read.</summary>
    public static int LevelOf(string recipe) => Regex.Match(recipe, Lv + @"[^\w\s]{0,2}\s*(\d{1,3})", RegexOptions.IgnoreCase) is { Success: true } m ? int.Parse(m.Groups[1].Value) : 0;

    /// <summary>The recipe's item name without the level the panel adds: "Life Serum (Bound) (Novice Lv. 1)" → "Life Serum (Bound)".</summary>
    /// The panel's close button can come with the title as a lone "X" after the level (2026-10-25 07:22:39, a great success:
    /// "Fine Durable Balaur Horn (Professional Lv. 30) X"), which kept the level on and filed three crafts under a recipe
    /// that doesn't exist; that X, or a short run of symbols, after the level goes with it. A tier read wrong ("Wihd Serum
    /// (Novite Lv. 10)", 2026-10-25 20:26:22) goes too: any bracket holding a level at the end.
    public static string ItemOf(string recipe) => ItemOf(recipe, out _);

    /// <summary>
    /// The same, with <paramref name="cut"/> set when the panel cut the title short. A long title ends in an ellipsis, inside
    /// the level's bracket or before it, and the reader often drops what follows (2026-10-26, 0.8.40: "Splendent Orichalcum
    /// Longsword (Novice Lv...", "Expert's Orichalcum Longsword (Novice Lv. 1", "(Novice Lv. 1..", "Superior Wind Serum
    /// (Professional Lv."), each filed as a recipe of its own, with no price, no profit and no icon. The ellipsis goes, and so
    /// does a bracket that never closes when it is the level's ("(Novice Lv. 1", "(Novice L", "(Nov", a lone "("), leaving
    /// the longest clean part of the name. A name's own bracket cut short ("Life Serum (Bou…") is not the level's and stays
    /// as read, for <see cref="Settle"/> to finish against the recipe book. What is left may itself be cut ("Splendent
    /// Orichalcum Long…"): <see cref="Settle"/> settles it.
    /// </summary>
    public static string ItemOf(string recipe, out bool cut)
    {
        cut = false;
        var s = Regex.Replace(recipe, @"\s*\((?:novice|apprentice|journeyman|expert|master|professional|artisan|grand ?master)[^)]*\)" + AfterLevel, "", RegexOptions.IgnoreCase).Trim();
        // a whole bracket holding a level (its tier read wrong); one that never closes is the cut form below
        s = Regex.Replace(s, @"\s*\([^()]{0,24}" + Lv + @"[^\w\s]{0,2}\s*[0-9IlOo|]{1,3}\s*\)" + AfterLevel, "", RegexOptions.IgnoreCase).Trim();
        // the ellipsis ("…", read as "..." or ".."), with the close button's X after it
        var t = Regex.Replace(s, @"\s*(?:…|‥|\.{2,})(?:\s*[XxＸ×✕✖])?\s*$", "").Trim();
        if (t != s) cut = true;
        // a bracket that never closes
        if (Regex.Match(t, @"\s*\(([^()]*)$") is { Success: true } open)
        {
            cut = true;
            if (IsLevelStart(open.Groups[1].Value)) t = t[..open.Index].Trim();
        }
        return t;
    }

    static readonly string[] Tiers = { "novice", "apprentice", "journeyman", "expert", "master", "professional", "artisan", "grandmaster", "grand master" };

    /// <summary>
    /// What a level bracket can be up to where the panel cut it: nothing yet, part of a tier's name ("Nov", "Novice"), the
    /// tier and part of "Lv" ("Novice L"), or anything holding "Lv" ("Novice Lv. 1", "Novite Lv"). "(Bound" is a name's.
    /// </summary>
    static bool IsLevelStart(string inside)
    {
        var x = Regex.Replace(inside, @"\s+", " ").Trim();
        if (x.Length == 0 || Regex.IsMatch(x, Lv + @"(?:\b|[^\w\s])", RegexOptions.IgnoreCase)) return true;
        foreach (var tier in Tiers)
        {
            if (tier.StartsWith(x, StringComparison.OrdinalIgnoreCase)) return true;
            // the tier whole and anything after it: "Novice L", and "Novice I-VEE." (2026-11-03 13:23:57, the blue panel's
            // "Splendent Orichalcum Longsword (Novice Lv..." read with its tail garbled, kept as a recipe of its own)
            if (x.StartsWith(tier + " ", StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    /// <summary>
    /// The recipe a panel title names, with its level and a note for the reads log. A title the panel cut short
    /// (<see cref="ItemOf(string, out bool)"/>) is settled against the recipe book: the recipe it names whole, else the one
    /// recipe whose output starts with what was read. On the blue combo roll the panel names the upgrade the recipe being
    /// crafted rolls for ("Splendent Orichalcum Longsword (Novice Lv..." while crafting Orichalcum Longsword, 2026-10-26
    /// 10:12): that is still the recipe being crafted. Between several recipes, the one being crafted (<paramref
    /// name="crafting"/>, the panel's recipe so far) wins, else the one the loot list or the chat showed made lately
    /// (<paramref name="madeLately"/>). Otherwise the name stays as read: a craft is counted whatever its name, never dropped.
    /// A level read up to the cut ("Lv. 1" of what may be "Lv. 15") is the recipe's level that begins with those digits.
    /// </summary>
    public static (string Recipe, int Level, string Note) Settle(string read, string crafting, IEnumerable<string>? madeLately = null)
    {
        var item = ItemOf(read, out bool cut);
        int level = LevelOf(read);
        if (!cut || item.Length < 2)
        {
            // a whole name the reader got wrong ("tvQewa Potion" for Mana Potion, 2026-10-27) is the recipe it stands for
            if (item.Length >= 3 && KinahLedger.Data.RecipeBook.ForOutput(item) == null && Nearest(item, level, crafting, madeLately) is { } near)
                return (near, level, $"read wrong, settled as {near}");
            return (item, level, "");
        }
        var cmp = StringComparer.OrdinalIgnoreCase;
        string settled = item, note;
        var named = KinahLedger.Data.RecipeBook.StartingWith(item);
        if (crafting.Length >= 2 && !cmp.Equals(item, crafting) && KinahLedger.Data.RecipeBook.ForOutput(crafting) is { } cur
            && cur.GreatOutcomes.Any(g => !cmp.Equals(g.Item, crafting) && g.Item.StartsWith(item, StringComparison.OrdinalIgnoreCase)))
        {
            settled = crafting; note = $"cut by the panel, the upgrade {crafting} rolls for: {crafting}";
        }
        else if (named.Count == 1) { settled = named[0]; note = $"recipe cut by the panel, settled as {settled}"; }
        else if (named.Count > 1 && crafting.Length >= 2 && named.Contains(crafting, cmp)) { settled = crafting; note = $"recipe cut by the panel, {named.Count} candidates: the one being crafted, {crafting}"; }
        else if (named.Count > 1 && (madeLately ?? Enumerable.Empty<string>()).Where(m => named.Contains(m, cmp)).Distinct(cmp).ToList() is { Count: 1 } seen)
        {
            settled = named.First(n => cmp.Equals(n, seen[0])); note = $"recipe cut by the panel, {named.Count} candidates: {settled}, which the loot list or the chat showed made";
        }
        else note = $"recipe cut by the panel, {named.Count} candidates: kept as read";
        return (settled, LevelAtCut(settled, level), note);
    }

    /// <summary>
    /// Whether a recipe line read names the upgrade the recipe being crafted rolls for ("Splendent Orichalcum Longsword
    /// (Novice Lv..." while crafting Orichalcum Longsword): the panel shows it, in green, for the upgrade roll after a craft
    /// and for nothing else. The name whole, the start of it where the panel cut it, or within a letter in ten of it; never
    /// the recipe itself, a cut of it, or another recipe the book has ("Expert's Orichalcum Longsword").
    /// </summary>
    public static bool NamesUpgrade(string read, string crafting)
    {
        if (crafting.Length < 2 || KinahLedger.Data.RecipeBook.ForOutput(crafting) is not { } book) return false;
        var item = ItemOf(read, out bool cut);
        if (item.Length < 3) return false;
        var cmp = StringComparison.OrdinalIgnoreCase;
        if (string.Equals(item, crafting, cmp) || crafting.StartsWith(item, cmp)) return false;
        foreach (var g in book.GreatOutcomes)
        {
            if (string.Equals(g.Item, crafting, cmp)) continue;
            if (string.Equals(g.Item, item, cmp)) return true;
            if (KinahLedger.Data.RecipeBook.ForOutput(item) != null) continue;
            if (cut && item.Length >= 6 && g.Item.StartsWith(item, cmp)) return true;
            var (a, b) = (Norm(item), Norm(g.Item));
            if (KinahLedger.Data.NameMatch.Distance(a, b, b.Length) != int.MaxValue) return true;
        }
        return false;
    }

    /// <summary>
    /// Whether a title read names its recipe for certain (<paramref name="settled"/>, what <see cref="Settle"/> made of
    /// <paramref name="read"/>): the recipe book has the name as read whole, or it is the one recipe a name the panel cut
    /// short begins. A name settled from a near miss, or from what was being crafted, is a guess; a name the book doesn't
    /// have is never sure on one read (two reads that agree are, TrackerSession's queue).
    /// </summary>
    public static bool Sure(string read, string settled)
    {
        if (settled.Length < 2 || KinahLedger.Data.RecipeBook.ForOutput(settled) == null) return false;
        var item = ItemOf(read, out bool cut);
        if (!cut) return string.Equals(item, settled, StringComparison.OrdinalIgnoreCase);
        var named = KinahLedger.Data.RecipeBook.StartingWith(item);
        return named.Count == 1 && string.Equals(named[0], settled, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The recipe a name read wrong stands for (2026-10-27: Steven's Mana Potion queue showed four rows, "Mana Potion",
    /// "Mana Potion (Novice I-v-40)1", "Mana Potion (Novice I-v-40)1 '" and "tvQewa Potion (Novice Lv.€40)l"). First the
    /// recipe being crafted and the items the loot list showed made lately, when the level read fits theirs and the name
    /// shares a word with it (with a level read) or is within four letters in ten of it; then, with a level read, the one
    /// recipe at that level in the book within three letters in ten, clearly closer than any other. Null when none is: the
    /// name stays as read, a craft is never dropped.
    /// </summary>
    internal static string? Nearest(string item, int level, string crafting, IEnumerable<string>? madeLately = null)
    {
        var read = Norm(item);
        if (read.Length < 3) return null;
        var cmp = StringComparer.OrdinalIgnoreCase;
        bool Fits(string c) { if (level <= 0) return true; var ms = KinahLedger.Data.RecipeBook.MasteriesOf(c); return ms.Count == 0 || ms.Contains(level); }
        foreach (var c in new[] { crafting }.Concat(madeLately ?? Enumerable.Empty<string>()).Where(c => c.Length >= 2).Distinct(cmp))
        {
            if (KinahLedger.Data.RecipeBook.ForOutput(c) is not { } book || !Fits(c)) continue;
            var n = Norm(c);
            if ((level > 0 && SharesWord(read, n)) || KinahLedger.Data.NameMatch.Distance(read, n, n.Length * 4) != int.MaxValue) return book.Output;
        }
        if (level <= 0) return null;
        lock (NearCache)
        {
            var key = $"{level}|{read}";
            if (NearCache.TryGetValue(key, out var hit)) return hit;
            string? best = null; int bestD = int.MaxValue, second = int.MaxValue;
            foreach (var c in KinahLedger.Data.RecipeBook.Outputs)
            {
                if (!KinahLedger.Data.RecipeBook.MasteriesOf(c).Contains(level)) continue;
                var n = Norm(c);
                int d = KinahLedger.Data.NameMatch.Distance(read, n, n.Length * 3);
                if (d == int.MaxValue) continue;
                if (d < bestD) { second = bestD; bestD = d; best = c; } else if (d < second) second = d;
            }
            var found = best != null && (second == int.MaxValue || second - bestD >= KinahLedger.Data.NameMatch.Unit) ? best : null;
            if (NearCache.Count > 200) NearCache.Clear();
            NearCache[key] = found;
            return found;
        }
    }

    static readonly Dictionary<string, string?> NearCache = new(StringComparer.Ordinal);

    static string Norm(string s) => Regex.Replace(s.ToLowerInvariant(), @"\s+", " ").Trim();

    /// <summary>Whether two names share a word of four letters or more ("potion").</summary>
    static bool SharesWord(string a, string b)
    {
        static IEnumerable<string> Words(string s) => s.Split(new[] { ' ', '-' }, StringSplitOptions.RemoveEmptyEntries).Select(w => w.Trim('.', ',', '\'', '’')).Where(w => w.Length >= 4);
        return Words(a).Intersect(Words(b)).Any();
    }

    /// <summary>A level read up to a cut: kept when the item's recipes have it and no longer level begins with it, else the one level that does, else none.</summary>
    static int LevelAtCut(string item, int level)
    {
        if (level <= 0) return 0;
        var ms = KinahLedger.Data.RecipeBook.MasteriesOf(item);
        if (ms.Count == 0) return level;
        var longer = ms.Where(m => m.ToString().StartsWith(level.ToString(), StringComparison.Ordinal)).ToList();
        return longer.Count == 1 ? longer[0] : 0;
    }
}

/// <summary>
/// Turns panel samples into crafts: each race ends when a bar is full (success when the blue one, failure when the
/// orange one), counted once; the next race starts when both bars are low again. Whether the race ran on the blue
/// upgrade-roll panel is a vote over its samples (<see cref="Outcome.Combo"/>): each sample's colour against the panel's
/// own on the queue's first craft (<see cref="NewQueue"/>), or the sample's own test before that is known. Samples need to
/// come often (a race lasts five to fifteen seconds; twice a second is plenty).
/// </summary>
public sealed class CraftPanelTracker
{
    /// <param name="Combo">Half or more of the race's samples showed the upgrade roll's blue tint.</param>
    public sealed record Outcome(DateTime At, bool Ok, bool Combo);

    public const double Full = 0.97, Low = 0.15;

    /// <summary>
    /// The shortest race the crafting panel runs, from both bars empty to one full. Measured 2026-11-03 on Steven's logs:
    /// race to race, the quickest real ones were 3.1 s (an upgrade roll, 2026-11-02 19:22:55.9 to 59.0) and 3.6 s (a
    /// craft), most 5 to 15 s; the bar that flickered full as a 1/1 queue of Orichalcum Ingots opened (2026-11-03
    /// 13:23:08.0 empty, 09.8 full, the panel up at 07.6) took 1.8 to 2.2 s, and was counted as a second craft.
    /// </summary>
    public const double MinRaceSeconds = 2.5;

    /// <summary>A race quicker than this is no race (<see cref="MinRaceSeconds"/> for the crafting panel; 0, the default, keeps every race).</summary>
    public double MinSeconds { get; init; }

    /// <summary>A race the last <see cref="Feed"/> dropped as too quick, for the reads log; null when it dropped none.</summary>
    public string? Dropped { get; private set; }

    /// <summary>A colour this far from the queue's first craft, in blue and in green over red, is the upgrade roll's tint (about 70 and 40 on 2026-11-03).</summary>
    public const double TintBlue = 30, TintGreen = 20;

    /// <summary>Two samples this close on both bars are the bars stopped: the race is over and can be scored.</summary>
    public const double Settled = 0.02;
    /// <summary>A race is close, and waits for the bars to stop, when the losing bar is this far along too; a clear one is scored the moment a bar is full, as before 0.9.17.</summary>
    public const double CloseBar = 0.8;

    bool _done = true;
    DateTime _start;
    int _samples, _tinted;
    double _sr, _sg, _sb; int _sn;
    (double R, double G, double B)? _base;
    // a bar full, the race waiting for the bars to stop: the loser stops just short of full while the winner is still
    // climbing (2026-11-04 09:41:54, Steven's Artisan's Steamed Cypri read "fail" at success 83%, fail 98%, and worked; an
    // Inina Pasta the same at 88%). The highest each bar got decides once both have stopped, or the panel has gone.
    bool _pending; double _peakS, _peakF, _lastS, _lastF;

    /// <summary>A new queue: the panel's own colour is taken again from its first craft (the world behind the see-through panel has changed).</summary>
    public void NewQueue() => _base = null;

    public Outcome? Feed(DateTime t, CraftPanel.Sample s)
    {
        Dropped = null;
        // the panel gone with a race waiting: scored as it stood
        if (!s.Visible) return _pending ? Finish(t) : null;
        if (_done)
        {
            // a new race: both bars back near empty
            if (s.Success < Low && s.Fail < Low) Start(t, s);
            return null;
        }
        // the next race started under a race waiting: that one is scored as it stood, this sample is the new one's first
        if (_pending && s.Success < Low && s.Fail < Low)
        {
            var o = Finish(t);
            Start(t, s);
            return o;
        }
        Look(s);
        _peakS = Math.Max(_peakS, s.Success); _peakF = Math.Max(_peakF, s.Fail);
        if (!_pending)
        {
            if (s.Success < Full && s.Fail < Full) { _lastS = s.Success; _lastF = s.Fail; return null; }
            double took = (t - _start).TotalSeconds;
            if (MinSeconds > 0 && took < MinSeconds)
            {
                _done = true;
                bool quick = s.Success >= Full && s.Success >= s.Fail;
                Dropped = $"{(quick ? "success" : "fail")} bar full {took:0.0} s after both were empty, quicker than any real race ({MinSeconds:0.0} s): not a craft";
                return null;
            }
            _lastS = s.Success; _lastF = s.Fail;
            // a clear race is over now; a close one (the loser four fifths along or more) waits for the bars to stop
            if (Math.Min(s.Success, s.Fail) < CloseBar) return Finish(t);
            _pending = true;
            return null;
        }
        bool still = Math.Abs(s.Success - _lastS) <= Settled && Math.Abs(s.Fail - _lastF) <= Settled;
        _lastS = s.Success; _lastF = s.Fail;
        return still ? Finish(t) : null;
    }

    /// <summary>The race waiting to be scored, scored now (the crafting panel went): null when none waits.</summary>
    public Outcome? Flush(DateTime t) => _pending ? Finish(t) : null;

    void Start(DateTime t, CraftPanel.Sample s)
    {
        _done = false; _pending = false; _start = t;
        _samples = _tinted = _sn = 0; _sr = _sg = _sb = 0; _peakS = _peakF = _lastS = _lastF = 0;
        Look(s);
    }

    /// <summary>The race's outcome: success when the success bar got full and the fail bar no higher; the tint vote; the queue's first plain craft gives the panel's own colour for the rest of it.</summary>
    Outcome Finish(DateTime t)
    {
        _done = true; _pending = false;
        bool ok = _peakS >= Full && _peakS >= _peakF;
        bool combo = _tinted > 0 && _tinted * 2 >= _samples;
        if (_base == null && !combo && _sn > 0) _base = (_sr / _sn, _sg / _sn, _sb / _sn);
        return new Outcome(t, ok, combo);
    }

    void Look(CraftPanel.Sample s)
    {
        _samples++;
        if (Tinted(s)) _tinted++;
        if (s.Panel is { } p) { _sr += p.R; _sg += p.G; _sb += p.B; _sn++; }
    }

    bool Tinted(CraftPanel.Sample s) =>
        _base is { } b && s.Panel is { } p
            ? (p.B - b.B) - (p.R - b.R) >= TintBlue && (p.G - b.G) - (p.R - b.R) >= TintGreen
            : s.Combo;
}
