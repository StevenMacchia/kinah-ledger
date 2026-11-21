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

    /// <summary>The score face: narrow solid LED digits (four dots wide) for the figures, the meter's and the Buddy's.</summary>
    static readonly Dictionary<char, string[]> FigGlyphs = new()
    {
        ['0'] = new[] { ".##.", "#..#", "#..#", "#..#", "#..#", "#..#", ".##." },
        ['1'] = new[] { ".#.", "##.", ".#.", ".#.", ".#.", ".#.", "###" },
        ['2'] = new[] { ".##.", "#..#", "...#", "..#.", ".#..", "#...", "####" },
        ['3'] = new[] { "###.", "...#", "...#", ".##.", "...#", "...#", "###." },
        ['4'] = new[] { "..#.", ".##.", "#.#.", "#.#.", "####", "..#.", "..#." },
        ['5'] = new[] { "####", "#...", "###.", "...#", "...#", "#..#", ".##." },
        ['6'] = new[] { ".##.", "#...", "#...", "###.", "#..#", "#..#", ".##." },
        ['7'] = new[] { "####", "...#", "..#.", "..#.", ".#..", ".#..", ".#.." },
        ['8'] = new[] { ".##.", "#..#", "#..#", ".##.", "#..#", "#..#", ".##." },
        ['9'] = new[] { ".##.", "#..#", "#..#", ".###", "...#", "...#", ".##." },
        ['K'] = new[] { "#..#", "#.#.", "##..", "##..", "#.#.", "#..#", "#..#" },
        ['B'] = new[] { "###.", "#..#", "#..#", "###.", "#..#", "#..#", "###." },
        ['–'] = new[] { "...", "...", "...", "###", "...", "...", "..." },
    };

    /// <summary>Whether the LED font spells every letter of <paramref name="s"/> (else a text stands in).</summary>
    static bool Spells(string s, bool fig = false) => s.Length > 0 && s.All(ch => (fig && FigGlyphs.ContainsKey(char.ToUpperInvariant(ch))) || Glyphs.ContainsKey(char.ToUpperInvariant(ch)));

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
        readonly Geometry _dots, _edge, _halo;
        readonly double[] _ends;

        public LedWord(string text, double px, Brush ink, Color glow, double glowA = .3, bool fig = false)
        {
            _text = text.ToUpperInvariant(); _px = px; Ink = ink; GlowC = glow; GlowA = glowA;
            IsHitTestVisible = false; SnapsToDevicePixels = true;
            (_dots, _edge, _halo, _ends) = Build(_text, px, fig);
            VerticalAlignment = VerticalAlignment.Center;
            System.Windows.Automation.AutomationProperties.SetName(this, text);
        }

        public string Text => _text;
        public double DotW => _ends.Length == 0 ? 0 : _ends[^1];

        static readonly Dictionary<(string, double, bool), (Geometry, Geometry, Geometry, double[])> Cache = new();

        /// <summary>The width of <paramref name="s"/> in the face at <paramref name="px"/> (a figure column sized for its widest form).</summary>
        public static double WidthOf(string s, double px, bool fig) { var e = Build(s.ToUpperInvariant(), px, fig).Item4; return e.Length == 0 ? 0 : e[^1] + px * .7; }

        /// <summary>
        /// The word's dots, once per word and size: the dots themselves (a figure's overlap a touch, so no seam shows between
        /// them), its dark edge (each dot grown by about half a dot) and its halo (grown by a dot), each one frozen geometry.
        /// </summary>
        static (Geometry, Geometry, Geometry, double[]) Build(string s, double px, bool fig)
        {
            if (Cache.TryGetValue((s, px, fig), out var hit)) return hit;
            var ends = new double[s.Length];
            var cells = new List<(double X, double Y)>();
            double x = 0;
            for (int i = 0; i < s.Length; i++)
            {
                var rows = fig && FigGlyphs.TryGetValue(s[i], out var fg) ? fg : Glyphs.TryGetValue(s[i], out var g) ? g : Glyphs[' '];
                for (int r = 0; r < 7; r++)
                    for (int c = 0; c < rows[r].Length; c++)
                        if (rows[r][c] == '#') cells.Add((x + c * px, r * px));
                x += rows[0].Length * px;
                ends[i] = x;
                x += px;
            }
            // figures solid (the dots overlap a hair); big words as LED dots with a hairline between; small words solid pixels
            double dot = fig ? px + .4 : px < 1.9 ? px + .15 : px * .9;
            Geometry Squares(double grow)
            {
                var sg = new StreamGeometry { FillRule = FillRule.Nonzero };
                using (var ctx = sg.Open())
                    foreach (var (l, t) in cells)
                    {
                        ctx.BeginFigure(new Point(l - grow, t - grow), true, true);
                        ctx.LineTo(new Point(l + dot + grow, t - grow), false, false);
                        ctx.LineTo(new Point(l + dot + grow, t + dot + grow), false, false);
                        ctx.LineTo(new Point(l - grow, t + dot + grow), false, false);
                    }
                sg.Freeze();
                return sg;
            }
            var built = (Squares(0), Squares(Math.Max(.8, px * .45)), Squares(px * 1.1), ends);
            return Cache[(s, px, fig)] = built;
        }

        protected override Size MeasureOverride(Size available) => new(Math.Ceiling(DotW + _px * .7), Math.Ceiling(7 * _px + _px * .7));

        /// <summary>Where letter <paramref name="i"/> ends, in px from the word's left (a typing wipe steps by these).</summary>
        public double LetterEnd(int i) => i < 0 ? 0 : _ends[Math.Min(i, _ends.Length - 1)] + _px * .5;
        public int Letters => _ends.Length;

        protected override void OnRender(DrawingContext dc)
        {
            if (_ends.Length == 0) return;
            // the phosphor bloom (the halo in the ink's colour, faint), a dark edge round the letters (they hold over a bright
            // scene), then the dots
            if (GlowA > 0) dc.DrawGeometry(Ink(GlowC, GlowA * .5), null, _halo);
            dc.DrawGeometry(ShadeB, null, _edge);
            dc.DrawGeometry(Ink, null, _dots);
        }
    }

    static readonly SolidColorBrush ShadeB = Frozen(Color.FromArgb(0xE0, 0, 0, 0));

    /// <summary>A figure in the score face (LED digits, solid, a dark edge, a phosphor bloom); a text where it can't spell it.</summary>
    static FrameworkElement Fig(string s, double px, Color ink, double glowA = .4, Brush? fill = null, double textSize = 0)
    {
        if (Spells(s, true)) return new LedWord(s, px, fill ?? Ink(ink), ink, glowA, fig: true);
        return Tt(s, textSize > 0 ? textSize : Math.Round(7 * px * 1.25 * 2) / 2, fill ?? Ink(ink), FontWeights.Bold);
    }

    /// <summary>A class's colour pushed vivid for the LED cells (the class colours are pastel; lit LEDs are saturated).</summary>
    static Color Vivid(Color c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0, max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        if (max - min < .04) return c;
        double k = 1.55, l = (max + min) / 2;
        byte P(double v) => (byte)Math.Round(255 * Math.Clamp(l + (v - l) * k, 0, 1));
        return Color.FromRgb(P(r), P(g), P(b));
    }

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

    const double CrownPx = 1.6;
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
        g.Children.Add(new GeometryDrawing(Frozen(Color.FromArgb(0x10, 0x60, 0x80, 0xFF)), null, new RectangleGeometry(new Rect(0, 0, 4, 1))));
        g.Children.Add(new GeometryDrawing(Frozen(Color.FromArgb(0x50, 0, 0, 0)), null, new RectangleGeometry(new Rect(0, 1, 4, 1))));
        var b = new DrawingBrush(g) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 4, 2), ViewportUnits = BrushMappingMode.Absolute, Viewbox = new Rect(0, 0, 4, 2), ViewboxUnits = BrushMappingMode.Absolute, Stretch = Stretch.None };
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

    static readonly Color BezelC = Color.FromRgb(0x04, 0x05, 0x0A), CabinetC = Color.FromRgb(0x3A, 0x14, 0x66), TMoldC = Color.FromRgb(0xFF, 0x8A, 0x2B);

    /// <summary>The tube's curvature: its corners and edges darker than its middle (a frozen radial, drawn once).</summary>
    static readonly Brush Vignette = MakeVignette();
    static Brush MakeVignette()
    {
        var b = new RadialGradientBrush { Center = new Point(.5, .5), GradientOrigin = new Point(.5, .5), RadiusX = .72, RadiusY = .85 };
        b.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), .55));
        b.GradientStops.Add(new GradientStop(Color.FromArgb(0x70, 0, 0, 0), 1));
        b.Freeze();
        return b;
    }

    /// <summary>The glass: a faint sheen over the tube's upper part, as light lies on a curved screen.</summary>
    static readonly Brush Sheen = MakeSheen();
    static Brush MakeSheen()
    {
        var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(.25, 1) };
        b.GradientStops.Add(new GradientStop(Color.FromArgb(0x16, 0xFF, 0xFF, 0xFF), 0));
        b.GradientStops.Add(new GradientStop(Color.FromArgb(0x05, 0xFF, 0xFF, 0xFF), .35));
        b.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0xFF, 0xFF, 0xFF), .5));
        b.Freeze();
        return b;
    }

    /// <summary>The bezel's inset: where the tube starts inside the screen's rectangle.</summary>
    const double Bezel = 6;

    /// <summary>The marquee's height over the meter's screen (its lit sign).</summary>
    const double MarqueeH = 22;

    /// <summary>The marquee's lit glass: red to orange to yellow and back, a frozen gradient.</summary>
    static readonly Brush MarqueeFill = MakeMarquee();
    static Brush MakeMarquee()
    {
        var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        b.GradientStops.Add(new GradientStop(Color.FromRgb(0xD8, 0x22, 0x36), 0));
        b.GradientStops.Add(new GradientStop(Color.FromRgb(0xFF, 0x7A, 0x1A), .1));
        b.GradientStops.Add(new GradientStop(Color.FromRgb(0xFF, 0x9E, 0x22), .5));
        b.GradientStops.Add(new GradientStop(Color.FromRgb(0xFF, 0x7A, 0x1A), .9));
        b.GradientStops.Add(new GradientStop(Color.FromRgb(0xD8, 0x22, 0x36), 1));
        b.Freeze();
        return b;
    }
    static readonly Brush MarqueeGloss = Mod.Grad(true, (Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF), 0), (Color.FromArgb(0x10, 0xFF, 0xFF, 0xFF), .5), (Color.FromArgb(0x30, 0, 0, 0), 1));

    /// <summary>The marquee's place in a screen <paramref name="r"/>.</summary>
    static Rect MarqueeRect(Rect r) => new(r.X + Bezel, r.Y + Bezel, r.Width - 2 * Bezel, MarqueeH);

    /// <summary>A row of bulbs along the marquee's top and bottom edges: every other one when <paramref name="phase"/> is 0 or 1, all at -1.</summary>
    static void DrawBulbs(DrawingContext dc, Rect m, Brush b, int phase = -1, double size = 3)
    {
        int i = 0;
        for (double x = m.X + 3; x + size <= m.Right - 2; x += 6, i++)
        {
            if (phase >= 0 && i % 2 != phase) continue;
            dc.DrawRectangle(b, null, new Rect(x, m.Y + 1, size, size));
            dc.DrawRectangle(b, null, new Rect(x, m.Bottom - 1 - size, size, size));
        }
    }

    /// <summary>
    /// A cabinet screen in <paramref name="r"/>, drawn once per size: a faint phosphor glow round it, a thin line in
    /// <paramref name="frame"/> and the 6 px black bezel inside it with a bevel of light, its corners stepped; with
    /// <paramref name="marquee"/>, the lit marquee across the bezel's top (red to yellow glass, a row of bulbs on each edge);
    /// then the tube with rounded corners: its black, scanlines every 2 px, the curvature's vignette and the glass's glare.
    /// </summary>
    static void DrawScreen(DrawingContext dc, Rect r, Color frame, double ground, bool marquee = false)
    {
        if (r.Width < 20 || r.Height < 20) return;
        for (int i = 1; i <= 3; i++) dc.DrawRectangle(null, PenOf(Ink(frame, .22 / i), 1), new Rect(r.X - i + .5, r.Y - i + .5, r.Width + 2 * i - 1, r.Height + 2 * i - 1));
        Stepped(dc, r, 4, Ink(BezelC, Math.Max(.9, ground)));
        // the cabinet's side: 3 px from its purple to black, and the T-molding along it, a 1 px line of the marquee's orange
        Frame(dc, r, 3, 4, Ink(frame), -1, -1);
        Frame(dc, new Rect(r.X + 1, r.Y + 1, r.Width - 2, r.Height - 2), 1, 4, Ink(TMoldC, .85), -1, -1);
        dc.DrawRectangle(Ink(Colors.White, .14), null, new Rect(r.X + 4, r.Y + 2, r.Width - 8, 1));
        dc.DrawRectangle(Ink(Colors.Black, .6), null, new Rect(r.X + 4, r.Bottom - 3, r.Width - 8, 1));
        var tr = r;
        if (marquee)
        {
            var m = MarqueeRect(r);
            dc.DrawRectangle(MarqueeFill, null, m);
            dc.DrawRectangle(MarqueeGloss, null, m);
            DrawBulbs(dc, m, Ink(Color.FromRgb(0x5A, 0x12, 0x08), .55));
            DrawBulbs(dc, m, Ink(Color.FromRgb(0xFF, 0xF4, 0xC8), .9), 0);
            dc.DrawRectangle(Ink(Colors.Black, .8), null, new Rect(m.X, m.Bottom, m.Width, 1));
            tr = new Rect(r.X, m.Bottom - Bezel + 2, r.Width, r.Bottom - (m.Bottom - Bezel + 2));
        }
        var tube = Tube(tr);
        dc.DrawGeometry(ScreenFill(ground), PenOf(Ink(frame, .45), 1), tube);
        dc.PushClip(tube);
        dc.DrawRectangle(Scan, null, tube.Bounds);
        dc.DrawRectangle(Vignette, null, tube.Bounds);
        dc.DrawRectangle(Sheen, null, tube.Bounds);
        dc.Pop();
    }

    /// <summary>The tube inside a screen: its glass with rounded corners, inset by the bezel.</summary>
    static Geometry Tube(Rect r)
    {
        var g = new RectangleGeometry(new Rect(r.X + Bezel, r.Y + Bezel, Math.Max(1, r.Width - 2 * Bezel), Math.Max(1, r.Height - 2 * Bezel)), 9, 9);
        g.Freeze();
        return g;
    }

    /// <summary>A rectangle with its corners stepped <paramref name="step"/> px, filled (the bezel).</summary>
    static void Stepped(DrawingContext dc, Rect r, double step, Brush b)
    {
        dc.DrawRectangle(b, null, new Rect(r.X + step, r.Y, r.Width - 2 * step, r.Height));
        dc.DrawRectangle(b, null, new Rect(r.X, r.Y + step, step, r.Height - 2 * step));
        dc.DrawRectangle(b, null, new Rect(r.Right - step, r.Y + step, step, r.Height - 2 * step));
        dc.DrawRectangle(b, null, new Rect(r.X + step / 2, r.Y + step / 2, step / 2, step / 2));
        dc.DrawRectangle(b, null, new Rect(r.Right - step, r.Y + step / 2, step / 2, step / 2));
        dc.DrawRectangle(b, null, new Rect(r.X + step / 2, r.Bottom - step, step / 2, step / 2));
        dc.DrawRectangle(b, null, new Rect(r.Right - step, r.Bottom - step, step / 2, step / 2));
    }

    static readonly Dictionary<(Brush, double), Pen> Pens = new();
    static Pen PenOf(Brush b, double w)
    {
        if (Pens.TryGetValue((b, w), out var p)) return p;
        p = new Pen(b, w); p.Freeze();
        return Pens[(b, w)] = p;
    }

    /// <summary>A screen's frame lit white-hot with its phosphor glow up and a wash on the glass: drawn once on its own layer.</summary>
    static void DrawFlare(DrawingContext dc, Rect r, Color c, bool marquee = false)
    {
        if (r.Width < 20 || r.Height < 20) return;
        var hot = Mod.Mix(c, Colors.White, .6);
        for (int i = 1; i <= 3; i++) dc.DrawRectangle(null, PenOf(Ink(c, .5 / i), 1.5), new Rect(r.X - i + .5, r.Y - i + .5, r.Width + 2 * i - 1, r.Height + 2 * i - 1));
        Frame(dc, r, 2, 4, Ink(hot), -1, -1);
        var tr = marquee ? new Rect(r.X, MarqueeRect(r).Bottom - Bezel + 2, r.Width, r.Bottom - (MarqueeRect(r).Bottom - Bezel + 2)) : r;
        dc.DrawGeometry(Ink(c, .08), PenOf(Ink(hot, .9), 1.5), Tube(tr));
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
