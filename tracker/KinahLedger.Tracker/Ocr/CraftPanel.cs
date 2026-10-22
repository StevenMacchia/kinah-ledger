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
    public sealed record Sample(bool Visible, double Success, double Fail, bool Combo);

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

    /// <summary>How well a label's white pixels match its shape: intersection over union of the two grids, 0 to 1.</summary>
    static double LabelScore(Bitmap f, (int X0, int X1, int Y0, int Y1, string Mask) l)
    {
        int cx = f.Width / 2; double k = f.Height / 2160.0;
        int both = 0, any = 0;
        for (int j = 0; j < GridH; j++)
            for (int i = 0; i < GridW; i++)
            {
                int x = cx + (int)Math.Round((l.X0 - 2560 + (l.X1 - l.X0) * (i + 0.5) / GridW) * k);
                int y = (int)Math.Round((l.Y0 + (l.Y1 - l.Y0) * (j + 0.5) / GridH) * k);
                if (x < 0 || y < 0 || x >= f.Width || y >= f.Height) return 0;
                var c = f.GetPixel(x, y);
                bool white = Math.Min(c.R, Math.Min(c.G, c.B)) > 185, want = l.Mask[j * GridW + i] == '1';
                if (white || want) any++;
                if (white && want) both++;
            }
        return any == 0 ? 0 : both / (double)any;
    }

    /// <summary>Whether the crafting panel is on screen: both bar labels in place.</summary>
    public static bool IsUp(Bitmap f) => Labels.All(l => LabelScore(f, l) >= LabelMatch);

    public static Sample Measure(Bitmap f)
    {
        int h = f.Height, cx = f.Width / 2;
        int x0 = cx + (int)(BarLeft * h), x1 = cx + (int)(BarRight * h);
        if (x0 < 0 || x1 >= f.Width) return new Sample(false, 0, 0, false);
        int ys = (int)(SuccessY * h), yf = (int)(FailY * h);
        if (!IsUp(f)) return new Sample(false, 0, 0, false);
        double Fill(int y, Func<Color, bool> isFill)
        {
            // the rightmost filled pixel: the bar's label sits over its left end, so only the fill's reach counts
            for (int x = x1; x >= x0; x--) if (isFill(f.GetPixel(x, y))) return (x - x0 + 1) / (double)(x1 - x0 + 1);
            return 0;
        }
        // a combo roll tints the whole panel blue
        var bg = f.GetPixel(cx + (int)(PanelLeft * h) + 30, (int)(TextBottom * h) - 30);
        bool combo = bg.B > bg.R + 35 && bg.B > 70;
        return new Sample(true, Fill(ys, Blue), Fill(yf, Orange), combo);
    }

    /// <summary>The lines the last text read saw, top to bottom, for the reads log.</summary>
    public static IReadOnlyList<string> LastLines { get; private set; } = Array.Empty<string>();

    /// <summary>The panel's text: how far the queue is ("1 / 2") and the recipe, with "(Bound)" and the level kept as read.</summary>
    public static async Task<(int Done, int Of, string Recipe)?> ReadTextAsync(Bitmap f)
    {
        int h = f.Height, cx = f.Width / 2;
        var area = Rectangle.FromLTRB(cx + (int)(PanelLeft * h), (int)(TextTop * h), cx + (int)(PanelRight * h), (int)(TextBottom * h));
        area.Intersect(new Rectangle(0, 0, f.Width, f.Height));
        using var bmp = f.Clone(area, f.PixelFormat);
        var lines = await ScreenReader.ReadBoxesAsync(bmp, area.Location, 2);
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
