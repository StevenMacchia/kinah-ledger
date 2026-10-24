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
    readonly record struct View(Bitmap Bmp, Rectangle Area, Rectangle Game, int Dy = 0)
    {
        public int H => Game.Height;
        public int Cx => Game.Width / 2;
        /// <summary>The pixel at a point of the game window, or null outside the picture.</summary>
        public Color? At(int gx, int gy)
        {
            int x = Game.X + gx - Area.X, y = Game.Y + gy + Dy - Area.Y;
            return x < 0 || y < 0 || x >= Bmp.Width || y >= Bmp.Height ? null : Bmp.GetPixel(x, y);
        }
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
                if (v.At(x, y) is not { } c) return 0;
                bool white = Math.Min(c.R, Math.Min(c.G, c.B)) > 185, want = l.Mask[j * GridW + i] == '1';
                if (white || want) any++;
                if (white && want) both++;
            }
        return any == 0 ? 0 : both / (double)any;
    }

    /// <summary>Whether the crafting panel is on screen in a picture of the whole game window: both bar labels in place.</summary>
    public static bool IsUp(Bitmap f) { var v = Whole(f); return Labels.All(l => LabelScore(v, l) >= LabelMatch); }

    static View Whole(Bitmap f) => new(f, new Rectangle(0, 0, f.Width, f.Height), new Rectangle(0, 0, f.Width, f.Height));

    /// <summary>The panel in a picture of the whole game window.</summary>
    public static Sample Measure(Bitmap f) => Measure(Whole(f));

    /// <summary>The panel in a picture of <see cref="Region"/> (or any area holding it) of the game window <paramref name="game"/>.</summary>
    public static Sample Measure(Bitmap bmp, Rectangle area, Rectangle game) => Measure(new View(bmp, area, game));

    static Sample Measure(View v)
    {
        var s = MeasureAt(v);
        if (s.Visible) return s;
        var raised = MeasureAt(v with { Dy = -(int)Math.Round(GatherShift * v.H) });
        return raised.Visible ? raised with { Raised = true } : s;
    }

    static Sample MeasureAt(View v)
    {
        int h = v.H, cx = v.Cx;
        int x0 = cx + (int)(BarLeft * h), x1 = cx + (int)(BarRight * h);
        int ys = (int)(SuccessY * h), yf = (int)(FailY * h);
        if (v.At(x0, ys) == null || v.At(x1, yf) == null) return new Sample(false, 0, 0, false);
        if (!Labels.All(l => LabelScore(v, l) >= LabelMatch)) return new Sample(false, 0, 0, false);
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
        int done = 0, of = 0; string recipe = "";
        foreach (var l in lines.OrderBy(l => l.Box.Top))
        {
            if (l.Text.Contains("Progress", StringComparison.OrdinalIgnoreCase))
            {
                // "Crafting in Progress 1 / 2"; the reader sometimes drops the spaces or reads the 1 as I or l
                var m = Regex.Match(l.Text.Replace('I', '1').Replace('l', '1').Replace('|', '/'), @"(\d+)\s*/\s*(\d+)\s*$");
                if (m.Success) { done = int.Parse(m.Groups[1].Value); of = int.Parse(m.Groups[2].Value); }
                continue;
            }
            if (recipe.Length == 0 && l.Text.Trim().Length >= 3 && !l.Text.Contains("Proficiency", StringComparison.OrdinalIgnoreCase)) recipe = l.Text.Trim();
        }
        return of > 0 || recipe.Length > 0 ? (done, of, recipe) : null;
    }

    /// <summary>The recipe's item name without the level the panel adds: "Life Serum (Bound) (Novice Lv. 1)" → "Life Serum (Bound)".</summary>
    /// <summary>The recipe's mastery level in the panel's title ("Orichalcum Ingot (Novice Lv. 25)" → 25), 0 when it isn't read.</summary>
    public static int LevelOf(string recipe) => Regex.Match(recipe, @"\bLv\.?\s*(\d{1,3})", RegexOptions.IgnoreCase) is { Success: true } m ? int.Parse(m.Groups[1].Value) : 0;

    public static string ItemOf(string recipe) => Regex.Replace(recipe, @"\s*\((?:novice|apprentice|journeyman|expert|master|professional|artisan|grand ?master)[^)]*\)\s*$", "", RegexOptions.IgnoreCase).Trim();
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
