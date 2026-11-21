using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

/// <summary>
/// Arcade's drawn parts: a 5x7 LED dot font for its words (the rank words, HI-SCORE, 1UP, the bursts), the pixel crown, the
/// player-one cursor, the CRT screen (a black ground, static scanlines, a Pac-Man-blue double frame with pixel-stepped corners
/// and a legend set into its top edge) and the LED bars (discrete cells, lit to the share). All painted in OnRender, cheap:
/// no effects, geometry cached per word and size. A word the font can't spell (Korean, Chinese) falls back to a bold text in
/// the same colour, so every language reads.
/// </summary>
sealed partial class HudArcade
{
    /// <summary>The look's face: Bahnschrift (a scoreboard's DIN), bold for figures; Korean and Chinese fall back to the system's.</summary>
    static readonly FontFamily Face = HudThemes.Bahn;

    // the classic name-entry colours: 1ST gold, 2ND silver-cyan, 3RD orange, 4TH pink, 5TH green, then round again in new hues
    static readonly Color[] RankTones =
    {
        Color.FromRgb(0xFF, 0xD2, 0x3F), Color.FromRgb(0x8C, 0xEB, 0xFF), Color.FromRgb(0xFF, 0x9A, 0x3C), Color.FromRgb(0xFF, 0x66, 0xCC), Color.FromRgb(0x72, 0xFF, 0x7E),
        Color.FromRgb(0xB8, 0x96, 0xFF), Color.FromRgb(0xFF, 0x70, 0x70), Color.FromRgb(0x7F, 0xB0, 0xFF), Color.FromRgb(0xFF, 0xEC, 0x8A), Color.FromRgb(0x5C, 0xF2, 0xD2),
    };

    /// <summary>A rank's colour on the table.</summary>
    static Color RankC(int rank) => RankTones[(Math.Max(1, rank) - 1) % RankTones.Length];

    /// <summary>"1ST", "2ND" ... in English; the rank and the language's own word elsewhere ("1위").</summary>
    static string RankWord(int rank) => rank.ToString(System.Globalization.CultureInfo.InvariantCulture)
        + ((rank % 100) is 11 or 12 or 13 ? L.T("TH") : (rank % 10) switch { 1 => L.T("ST"), 2 => L.T("ND"), 3 => L.T("RD"), _ => L.T("TH") });

    // ================================================================ the LED font

    static readonly Dictionary<char, string[]> Glyphs = MakeGlyphs();

    static Dictionary<char, string[]> MakeGlyphs()
    {
        var g = new Dictionary<char, string[]>();
        void A(char c, params string[] rows) => g[c] = rows;
        A('A', ".###.", "#...#", "#...#", "#####", "#...#", "#...#", "#...#");
        A('B', "####.", "#...#", "#...#", "####.", "#...#", "#...#", "####.");
        A('C', ".###.", "#...#", "#....", "#....", "#....", "#...#", ".###.");
        A('D', "####.", "#...#", "#...#", "#...#", "#...#", "#...#", "####.");
        A('E', "#####", "#....", "#....", "####.", "#....", "#....", "#####");
        A('F', "#####", "#....", "#....", "####.", "#....", "#....", "#....");
        A('G', ".###.", "#...#", "#....", "#.###", "#...#", "#...#", ".####");
        A('H', "#...#", "#...#", "#...#", "#####", "#...#", "#...#", "#...#");
        A('I', "###", ".#.", ".#.", ".#.", ".#.", ".#.", "###");
        A('J', "..###", "...#.", "...#.", "...#.", "...#.", "#..#.", ".##..");
        A('K', "#...#", "#..#.", "#.#..", "##...", "#.#..", "#..#.", "#...#");
        A('L', "#....", "#....", "#....", "#....", "#....", "#....", "#####");
        A('M', "#...#", "##.##", "#.#.#", "#.#.#", "#...#", "#...#", "#...#");
        A('N', "#...#", "#...#", "##..#", "#.#.#", "#..##", "#...#", "#...#");
        A('O', ".###.", "#...#", "#...#", "#...#", "#...#", "#...#", ".###.");
        A('P', "####.", "#...#", "#...#", "####.", "#....", "#....", "#....");
        A('Q', ".###.", "#...#", "#...#", "#...#", "#.#.#", "#..#.", ".##.#");
        A('R', "####.", "#...#", "#...#", "####.", "#.#..", "#..#.", "#...#");
        A('S', ".####", "#....", "#....", ".###.", "....#", "....#", "####.");
        A('T', "#####", "..#..", "..#..", "..#..", "..#..", "..#..", "..#..");
        A('U', "#...#", "#...#", "#...#", "#...#", "#...#", "#...#", ".###.");
        A('V', "#...#", "#...#", "#...#", "#...#", "#...#", ".#.#.", "..#..");
        A('W', "#...#", "#...#", "#...#", "#.#.#", "#.#.#", "#.#.#", ".#.#.");
        A('X', "#...#", "#...#", ".#.#.", "..#..", ".#.#.", "#...#", "#...#");
        A('Y', "#...#", "#...#", ".#.#.", "..#..", "..#..", "..#..", "..#..");
        A('Z', "#####", "....#", "...#.", "..#..", ".#...", "#....", "#####");
        A('0', ".###.", "#...#", "#..##", "#.#.#", "##..#", "#...#", ".###.");
        A('1', ".#.", "##.", ".#.", ".#.", ".#.", ".#.", "###");
        A('2', ".###.", "#...#", "....#", "...#.", "..#..", ".#...", "#####");
        A('3', "#####", "...#.", "..#..", "...#.", "....#", "#...#", ".###.");
        A('4', "...#.", "..##.", ".#.#.", "#..#.", "#####", "...#.", "...#.");
        A('5', "#####", "#....", "####.", "....#", "....#", "#...#", ".###.");
        A('6', "..##.", ".#...", "#....", "####.", "#...#", "#...#", ".###.");
        A('7', "#####", "....#", "...#.", "..#..", ".#...", ".#...", ".#...");
        A('8', ".###.", "#...#", "#...#", ".###.", "#...#", "#...#", ".###.");
        A('9', ".###.", "#...#", "#...#", ".####", "....#", "...#.", ".##..");
        A('!', "#", "#", "#", "#", "#", ".", "#");
        A('?', ".###.", "#...#", "....#", "...#.", "..#..", ".....", "..#..");
        A('-', "....", "....", "....", "####", "....", "....", "....");
        A('+', ".....", "..#..", "..#..", "#####", "..#..", "..#..", ".....");
        A('.', ".", ".", ".", ".", ".", ".", "#");
        A(',', "..", "..", "..", "..", "..", ".#", "#.");
        A(':', ".", ".", "#", ".", ".", "#", ".");
        A('\'', "#", "#", ".", ".", ".", ".", ".");
        A('/', "....#", "....#", "...#.", "..#..", ".#...", "#....", "#....");
        A('%', "##..#", "##..#", "...#.", "..#..", ".#...", "#..##", "#..##");
        A('×', ".....", "#...#", ".#.#.", "..#..", ".#.#.", "#...#", ".....");
        A(' ', "..", "..", "..", "..", "..", "..", "..");
        return g;
    }

    /// <summary>Whether the LED font spells every letter of <paramref name="s"/> (else a text stands in).</summary>
    static bool Spells(string s) => s.Length > 0 && s.All(ch => Glyphs.ContainsKey(char.ToUpperInvariant(ch)));

    /// <summary>
    /// A word in the LED font: dots <see cref="Px"/> wide, a dark shade a dot under them and a glow in the ink round them,
    /// sized to its dots. Drawn once (its geometry cached per word and size); a moment moves it by its transform.
    /// </summary>
    sealed class LedWord : FrameworkElement
    {
        readonly string _text;
        readonly double _px;
        public Brush Ink;
        public Color GlowC;
        public double GlowA;
        readonly Geometry _dots;
        readonly double[] _ends;

        public LedWord(string text, double px, Brush ink, Color glow, double glowA = .3)
        {
            _text = text.ToUpperInvariant(); _px = px; Ink = ink; GlowC = glow; GlowA = glowA;
            IsHitTestVisible = false; SnapsToDevicePixels = true;
            (_dots, _ends) = Build(_text, px);
            VerticalAlignment = VerticalAlignment.Center;
            System.Windows.Automation.AutomationProperties.SetName(this, text);
        }

        public string Text => _text;
        public double DotW => _ends.Length == 0 ? 0 : _ends[^1];

        static readonly Dictionary<(string, double), (Geometry, double[])> Cache = new();

        static (Geometry, double[]) Build(string s, double px)
        {
            if (Cache.TryGetValue((s, px), out var hit)) return hit;
            var sg = new StreamGeometry();
            var ends = new double[s.Length];
            double x = 0, dot = px >= 1.9 ? px * .84 : px;   // big words read as LED dots, small ones as solid pixels
            using (var ctx = sg.Open())
            {
                for (int i = 0; i < s.Length; i++)
                {
                    var rows = Glyphs.TryGetValue(s[i], out var g) ? g : Glyphs[' '];
                    for (int r = 0; r < 7; r++)
                        for (int c = 0; c < rows[r].Length; c++)
                        {
                            if (rows[r][c] != '#') continue;
                            double l = x + c * px, t = r * px;
                            ctx.BeginFigure(new Point(l, t), true, true);
                            ctx.LineTo(new Point(l + dot, t), false, false);
                            ctx.LineTo(new Point(l + dot, t + dot), false, false);
                            ctx.LineTo(new Point(l, t + dot), false, false);
                        }
                    x += rows[0].Length * px;
                    ends[i] = x;
                    x += px;
                }
            }
            sg.Freeze();
            return Cache[(s, px)] = (sg, ends);
        }

        protected override Size MeasureOverride(Size available) => new(Math.Ceiling(DotW + _px * .7), Math.Ceiling(7 * _px + _px * .7));

        /// <summary>Where letter <paramref name="i"/> ends, in px from the word's left (a typing wipe steps by these).</summary>
        public double LetterEnd(int i) => i < 0 ? 0 : _ends[Math.Min(i, _ends.Length - 1)] + _px * .5;
        public int Letters => _ends.Length;

        protected override void OnRender(DrawingContext dc)
        {
            if (_ends.Length > 0) Paint(dc);
        }

        Pen? _glow;
        TranslateTransform? _down;
        Pen GlowPen() { var p = new Pen(Ink(GlowC, GlowA), _px * 1.5) { LineJoin = PenLineJoin.Round }; p.Freeze(); return p; }

        void Paint(DrawingContext dc)
        {
            // the glow: the dots stroked wide in the ink's colour, faint; then the shade a dot down; then the dots
            if (GlowA > 0) dc.DrawGeometry(null, _glow ??= GlowPen(), _dots);
            dc.PushTransform(_down ??= new TranslateTransform(0, _px * .7));
            dc.DrawGeometry(ShadeB, null, _dots);
            dc.Pop();
            dc.DrawGeometry(Ink, null, _dots);
        }
    }

    static readonly SolidColorBrush ShadeB = Frozen(Color.FromArgb(0xE0, 0, 0, 0));

    /// <summary>
    /// A word for the screen: the LED font when it spells it, else a bold text of about the same height in the same colour
    /// (Korean and Chinese), so no English is left in their renders and nothing goes unread.
    /// </summary>
    static FrameworkElement Word(string s, double px, Color ink, double glowA = .3, Brush? fill = null, double textSize = 0)
    {
        if (Spells(s)) return new LedWord(s, px, fill ?? Frozen(ink), ink, glowA);
        var t = Tt(s, textSize > 0 ? textSize : Math.Round(7 * px * 1.22 * 2) / 2, fill ?? Frozen(ink), FontWeights.Bold);
        return t;
    }

    // ================================================================ the crown and the cursor

    // the crown in pixels: Y bright gold, G gold, D its shade, R a ruby, B a sapphire
    static readonly string[] CrownArt =
    {
        "Y....Y....Y",
        "GG..GYG..GG",
        "GGG.GGG.GGG",
        "GGGGGGGGGGG",
        "GRGGGBGGGRG",
        "GGGGGGGGGGG",
        "DDDDDDDDDDD",
    };

    static readonly Dictionary<char, SolidColorBrush> CrownInk = new()
    {
        ['Y'] = Frozen(Color.FromRgb(0xFF, 0xF4, 0xB0)), ['G'] = Frozen(Color.FromRgb(0xFF, 0xC8, 0x2E)), ['D'] = Frozen(Color.FromRgb(0xC2, 0x7A, 0x12)),
        ['R'] = Frozen(Color.FromRgb(0xFF, 0x3B, 0x5C)), ['B'] = Frozen(Color.FromRgb(0x4D, 0xC8, 0xFF)),
    };

    /// <summary>The pixel crown, drawn once (a black outline a pixel round it, so it reads on any ground) and reused.</summary>
    static readonly Drawing Crown = MakeCrown(CrownPx);

    static Drawing MakeCrown(double px)
    {
        var g = new DrawingGroup();
        using (var dc = g.Open())
        {
            int w = CrownArt[0].Length, h = CrownArt.Length;
            for (int r = 0; r < h; r++)
                for (int c = 0; c < w; c++)
                    if (CrownArt[r][c] != '.') dc.DrawRectangle(ShadeB, null, new Rect((c - .6) * px, (r - .6) * px, px * 2.2, px * 2.2));
            for (int r = 0; r < h; r++)
                for (int c = 0; c < w; c++)
                    if (CrownInk.TryGetValue(CrownArt[r][c], out var b)) dc.DrawRectangle(b, null, new Rect(c * px, r * px, px + .05, px + .05));
        }
        g.Freeze();
        return g;
    }

    const double CrownPx = 1.1;
    static double CrownW => CrownArt[0].Length * CrownPx;
    static double CrownH => CrownArt.Length * CrownPx;

    /// <summary>The player-one cursor: a pixel arrow pointing at your row (an arcade menu's ▶).</summary>
    static void DrawCursor(DrawingContext dc, double x, double cy, double px, Brush ink)
    {
        int[] cols = { 1, 2, 3, 4, 3, 2, 1 };
        double top = cy - 3.5 * px;
        for (int r = 0; r < 7; r++) dc.DrawRectangle(ShadeB, null, new Rect(x - px * .5, top + r * px + px * .5, cols[r] * px + px, px));
        for (int r = 0; r < 7; r++) dc.DrawRectangle(ink, null, new Rect(x, top + r * px, cols[r] * px, px));
    }

    // ================================================================ the CRT screen

    static readonly Color ScreenC = Color.FromRgb(0x05, 0x06, 0x0B), MazeC = Color.FromRgb(0x2B, 0x47, 0xFF), MazeHiC = Color.FromRgb(0x6E, 0x86, 0xFF);

    /// <summary>The static scanlines: a 1 px dark line every 3 px, tiled (drawn once, never moving).</summary>
    static readonly DrawingBrush Scan = MakeScan();

    static DrawingBrush MakeScan()
    {
        var g = new DrawingGroup();
        g.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, 4, 3))));
        g.Children.Add(new GeometryDrawing(Frozen(Color.FromArgb(0x5A, 0, 0, 0)), null, new RectangleGeometry(new Rect(0, 2, 4, 1))));
        var b = new DrawingBrush(g) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 4, 3), ViewportUnits = BrushMappingMode.Absolute, Stretch = Stretch.None };
        b.Freeze();
        return b;
    }

    /// <summary>A frozen brush per colour, made once (the drawing never allocates a brush per frame).</summary>
    static SolidColorBrush Ink(Color c, double a = 1)
    {
        var k = Mod.A(c, a * c.A / 255.0);
        uint key = ((uint)k.A << 24) | ((uint)k.R << 16) | ((uint)k.G << 8) | k.B;
        if (!Inks.TryGetValue(key, out var b)) Inks[key] = b = Frozen(k);
        return b;
    }
    static readonly Dictionary<uint, SolidColorBrush> Inks = new();

    /// <summary>The screens' ground at an opacity (a tube's black, a touch lighter at its middle), made once per opacity.</summary>
    static Brush ScreenFill(double ground)
    {
        int key = (int)Math.Round(ground * 100);
        if (Fills.TryGetValue(key, out var f)) return f;
        var fill = new RadialGradientBrush { Center = new Point(.5, .42), GradientOrigin = new Point(.5, .42), RadiusX = .75, RadiusY = .9 };
        fill.GradientStops.Add(new GradientStop(Mod.A(Color.FromRgb(0x0D, 0x10, 0x1E), key / 100.0), 0));
        fill.GradientStops.Add(new GradientStop(Mod.A(ScreenC, key / 100.0), 1));
        fill.Freeze();
        return Fills[key] = fill;
    }
    static readonly Dictionary<int, Brush> Fills = new();

    /// <summary>
    /// The screen in <paramref name="r"/>: the black ground, the scanlines (a frozen tile), and the frame: a 2 px outer line and
    /// a 1 px inner one in <paramref name="frame"/>, the corners stepped in pixels; the top edge broken from
    /// <paramref name="gap0"/> to <paramref name="gap1"/> for a legend set into it. Drawn on a change only; a moment's flare is
    /// <see cref="DrawFlare"/> on a layer of its own whose opacity moves.
    /// </summary>
    static void DrawScreen(DrawingContext dc, Rect r, Color frame, double ground, double gap0 = -1, double gap1 = -1)
    {
        if (r.Width < 12 || r.Height < 12) return;
        var fill = ScreenFill(ground);
        // the ground inside the stepped corners
        var inner = new Rect(r.X + 1, r.Y + 1, r.Width - 2, r.Height - 2);
        dc.DrawRectangle(fill, null, new Rect(inner.X + 2, inner.Y, inner.Width - 4, inner.Height));
        dc.DrawRectangle(fill, null, new Rect(inner.X, inner.Y + 2, 2, inner.Height - 4));
        dc.DrawRectangle(fill, null, new Rect(inner.Right - 2, inner.Y + 2, 2, inner.Height - 4));
        dc.DrawRectangle(Scan, null, new Rect(inner.X + 2, inner.Y + 2, inner.Width - 4, inner.Height - 4));
        Frame(dc, r, 2, 3, Ink(frame), gap0, gap1);
        Frame(dc, new Rect(r.X + 4, r.Y + 4, r.Width - 8, r.Height - 8), 1, 2, Ink(Mod.Mix(frame, Colors.White, .25), .7), gap0, gap1);
    }

    /// <summary>A screen's frame lit white-hot with a faint wash inside: drawn once on its own layer, the moment moves its opacity.</summary>
    static void DrawFlare(DrawingContext dc, Rect r, Color c, double gap0 = -1, double gap1 = -1)
    {
        if (r.Width < 12 || r.Height < 12) return;
        var hot = Mod.Mix(c, Colors.White, .6);
        dc.DrawRectangle(Ink(c, .1), null, new Rect(r.X + 5, r.Y + 5, r.Width - 10, r.Height - 10));
        Frame(dc, r, 2, 3, Ink(hot), gap0, gap1);
        Frame(dc, new Rect(r.X + 4, r.Y + 4, r.Width - 8, r.Height - 8), 1, 2, Ink(hot, .8), gap0, gap1);
    }

    /// <summary>A pixel frame: lines <paramref name="t"/> thick, each corner cut by a <paramref name="step"/> px step; the top broken for a legend.</summary>
    static void Frame(DrawingContext dc, Rect r, double t, double step, Brush b, double gap0, double gap1)
    {
        double l = r.Left, tp = r.Top, rt = r.Right, bt = r.Bottom;
        if (gap0 >= 0 && gap1 > gap0)
        {
            dc.DrawRectangle(b, null, new Rect(l + step, tp, Math.Max(0, gap0 - (l + step)), t));
            dc.DrawRectangle(b, null, new Rect(gap1, tp, Math.Max(0, rt - step - gap1), t));
        }
        else dc.DrawRectangle(b, null, new Rect(l + step, tp, r.Width - 2 * step, t));
        dc.DrawRectangle(b, null, new Rect(l + step, bt - t, r.Width - 2 * step, t));
        dc.DrawRectangle(b, null, new Rect(l, tp + step, t, r.Height - 2 * step));
        dc.DrawRectangle(b, null, new Rect(rt - t, tp + step, t, r.Height - 2 * step));
        // the stepped corners: one pixel block each, set in between the lines
        double s = Math.Min(step, t + 1);
        dc.DrawRectangle(b, null, new Rect(l + step - s, tp + step - s, s, s));
        dc.DrawRectangle(b, null, new Rect(rt - step, tp + step - s, s, s));
        dc.DrawRectangle(b, null, new Rect(l + step - s, bt - step, s, s));
        dc.DrawRectangle(b, null, new Rect(rt - step, bt - step, s, s));
    }

    // ================================================================ the LED bars

    /// <summary>
    /// A row of LED cells as one frozen tile (a cell <paramref name="cell"/> wide, <paramref name="gap"/> of air, a light line
    /// along its top when <paramref name="top"/>), so a whole bar is one rectangle filled with it, not a rectangle per cell.
    /// </summary>
    static DrawingBrush CellTile(Color c, double cell, double gap, double h, bool top)
    {
        var key = (c, cell, gap, h, top);
        if (Tiles.TryGetValue(key, out var t)) return t;
        var g = new DrawingGroup();
        g.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, cell + gap, h))));
        g.Children.Add(new GeometryDrawing(Ink(c), null, new RectangleGeometry(new Rect(0, 0, cell, h))));
        if (top) g.Children.Add(new GeometryDrawing(Ink(Colors.White, .42), null, new RectangleGeometry(new Rect(0, 0, cell, 1))));
        var b = new DrawingBrush(g)
        {
            TileMode = TileMode.Tile, Stretch = Stretch.None, Viewport = new Rect(0, 0, cell + gap, h), ViewportUnits = BrushMappingMode.Absolute,
            Viewbox = new Rect(0, 0, cell + gap, h), ViewboxUnits = BrushMappingMode.Absolute,
        };
        b.Freeze();
        return Tiles[key] = b;
    }
    static readonly Dictionary<(Color, double, double, double, bool), DrawingBrush> Tiles = new();

    /// <summary>How many whole cells fit in <paramref name="w"/>, and the bar's drawn width (the last cell flush, no trailing air).</summary>
    static (int N, double W) Cells(double w, double cell, double gap)
    {
        int n = Math.Max(1, (int)Math.Floor((w + gap) / (cell + gap)));
        return (n, n * (cell + gap) - gap);
    }

    /// <summary>
    /// An LED bar: <paramref name="share"/> of its cells lit in <paramref name="tone"/> (the cell at the edge lit in part), each
    /// lit cell with a light line along its top and a faint bloom round the lit run, the cell at the head hot (nearly white);
    /// the unlit cells dim. Five rectangles whatever its length: the cells are tiles.
    /// </summary>
    static void DrawLed(DrawingContext dc, Rect r, double share, Color tone, double cell = 5, double gap = 2, double dimA = .1)
    {
        if (r.Width < 4 || r.Height < 2) return;
        double pitch = cell + gap, h = r.Height;
        var (n, w) = Cells(r.Width, cell, gap);
        double fill = Math.Clamp(share, 0, 1) * n;
        int whole = (int)Math.Floor(fill);
        double part = fill - whole;
        dc.PushTransform(new TranslateTransform(r.X, r.Y));
        if (fill > .05) dc.DrawRectangle(Ink(tone, .2), null, new Rect(-1.5, -1.5, Math.Min(w, fill * pitch - gap) + 3, h + 3));
        dc.DrawRectangle(CellTile(Mod.A(Colors.White, dimA), cell, gap, h, false), null, new Rect(0, 0, w, h));
        if (whole > 0) dc.DrawRectangle(CellTile(Mod.Mix(tone, Colors.White, .08), cell, gap, h, true), null, new Rect(0, 0, whole * pitch - gap, h));
        var hot = Mod.Mix(tone, Colors.White, .55);
        if (whole > 0 && part < .5) dc.DrawRectangle(Ink(hot), null, new Rect((whole - 1) * pitch, 0, cell, h));
        if (whole < n && part > .02) dc.DrawRectangle(Ink(hot, Math.Round(part * 20) / 20), null, new Rect(whole * pitch, 0, cell, h));
        dc.Pop();
    }

    /// <summary>
    /// A chase's light: four cells, the front one brightest with a halo, fading behind it. Drawn once; the chase moves it along
    /// a bar a whole cell at a time (a transform, as an LED steps).
    /// </summary>
    static void DrawChaseLight(DrawingContext dc, Color c, double cell, double gap, double h)
    {
        double pitch = cell + gap;
        dc.DrawRectangle(Ink(c, .35), null, new Rect(3 * pitch - 2, 0, cell + 4, h + 4));
        for (int i = 0; i < 4; i++) dc.DrawRectangle(Ink(c, Math.Round(.95 * (1 - (3 - i) / 3.5) * 20) / 20), null, new Rect(i * pitch, 2, cell, h));
    }

    /// <summary>
    /// The pickup strip's tiles in pixel frames: each rounded slot squared off, the image clip squared, a black pixel notch at
    /// each corner of the frame (the strip itself is PickStrip's: what it shows, its star and its light, unchanged).
    /// </summary>
    static FrameworkElement Pixelate(FrameworkElement strip)
    {
        void Walk(DependencyObject o)
        {
            if (o is Border b && b.CornerRadius.TopLeft > 0) b.CornerRadius = new CornerRadius(0);
            if (o is Image img && img.Clip is RectangleGeometry rg) img.Clip = new RectangleGeometry(rg.Rect);
            if (o is Grid g && (Equals(g.Tag, "pick-tile") || Equals(g.Tag, "pick-best")))
                g.Children.Add(new TacSketch((dc, s) =>
                {
                    // the corners notched a pixel in black, as a sprite's frame is
                    var k = ShadeB;
                    foreach (var (x, y) in new[] { (0.0, 0.0), (s.Width - 2, 0.0), (0.0, s.Height - 2), (s.Width - 2, s.Height - 2) }) dc.DrawRectangle(k, null, new Rect(x, y, 2, 2));
                }));
            foreach (var child in LogicalTreeHelper.GetChildren(o)) if (child is DependencyObject d) Walk(d);
        }
        Walk(strip);
        return strip;
    }
}
