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
    /// <see cref="Reveal"/> letters shown (a name entry's typing). Sized to its dots.
    /// </summary>
    sealed class LedWord : FrameworkElement
    {
        readonly string _text;
        readonly double _px;
        public Brush Ink;
        public Color GlowC;
        public double GlowA;
        /// <summary>How many letters show (a letter partly in flickers in at half strength); all by default.</summary>
        public double Reveal = double.MaxValue;
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

        protected override void OnRender(DrawingContext dc)
        {
            if (_ends.Length == 0) return;
            int whole = (int)Math.Floor(Math.Min(Reveal, _ends.Length));
            double part = Math.Min(Reveal, _ends.Length) - whole;
            if (whole >= _ends.Length) { Paint(dc); return; }
            // the letters still to come are clipped off; the next one flickers in at half strength
            double full = whole == 0 ? 0 : _ends[whole - 1] + _px * .5, h = 7 * _px + 8;
            if (whole > 0) { dc.PushClip(new RectangleGeometry(new Rect(-4, -4, full + 4, h))); Paint(dc); dc.Pop(); }
            if (part > .35)
            {
                double next = _ends[whole] + _px * .5;
                dc.PushClip(new RectangleGeometry(new Rect(full, -4, next - full, h)));
                dc.PushOpacity(.55); Paint(dc); dc.Pop();
                dc.Pop();
            }
        }

        void Paint(DrawingContext dc)
        {
            // the glow: the dots stroked wide in the ink's colour, faint; then the shade a dot down; then the dots
            if (GlowA > 0) dc.DrawGeometry(null, new Pen(Frozen(Mod.A(GlowC, GlowA)), _px * 1.5) { LineJoin = PenLineJoin.Round }, _dots);
            dc.PushTransform(new TranslateTransform(0, _px * .7));
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
        t.Effect = Shade();
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

    /// <summary>The pixel crown, <paramref name="px"/> a pixel, with a black outline a pixel round it so it reads on any ground.</summary>
    static void DrawCrown(DrawingContext dc, double x, double y, double px)
    {
        int w = CrownArt[0].Length, h = CrownArt.Length;
        // the outline: each lit pixel's square grown by a pixel, in black
        for (int r = 0; r < h; r++)
            for (int c = 0; c < w; c++)
                if (CrownArt[r][c] != '.') dc.DrawRectangle(ShadeB, null, new Rect(x + (c - .6) * px, y + (r - .6) * px, px * 2.2, px * 2.2));
        for (int r = 0; r < h; r++)
            for (int c = 0; c < w; c++)
                if (CrownInk.TryGetValue(CrownArt[r][c], out var b)) dc.DrawRectangle(b, null, new Rect(x + c * px, y + r * px, px + .05, px + .05));
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

    /// <summary>
    /// The screen in <paramref name="r"/>: the black ground (a touch lighter at its middle, as a tube is), the scanlines, and
    /// the frame: a 2 px outer line and a 1 px inner one in <paramref name="frame"/>, the corners stepped in pixels; the top edge
    /// broken from <paramref name="gap0"/> to <paramref name="gap1"/> for a legend set into it. <paramref name="lit"/> brightens the frame (a moment).
    /// </summary>
    static void DrawScreen(DrawingContext dc, Rect r, Color frame, double ground, double gap0 = -1, double gap1 = -1, double lit = 0)
    {
        if (r.Width < 12 || r.Height < 12) return;
        var fill = new RadialGradientBrush { Center = new Point(.5, .42), GradientOrigin = new Point(.5, .42), RadiusX = .75, RadiusY = .9 };
        fill.GradientStops.Add(new GradientStop(Mod.A(Color.FromRgb(0x0D, 0x10, 0x1E), ground), 0));
        fill.GradientStops.Add(new GradientStop(Mod.A(ScreenC, ground), 1));
        fill.Freeze();
        // the ground inside the stepped corners
        var inner = new Rect(r.X + 1, r.Y + 1, r.Width - 2, r.Height - 2);
        dc.DrawRectangle(fill, null, new Rect(inner.X + 2, inner.Y, inner.Width - 4, inner.Height));
        dc.DrawRectangle(fill, null, new Rect(inner.X, inner.Y + 2, 2, inner.Height - 4));
        dc.DrawRectangle(fill, null, new Rect(inner.Right - 2, inner.Y + 2, 2, inner.Height - 4));
        dc.DrawRectangle(Scan, null, new Rect(inner.X + 2, inner.Y + 2, inner.Width - 4, inner.Height - 4));
        var c = lit > 0 ? Mod.Mix(frame, Colors.White, .55 * Math.Clamp(lit, 0, 1)) : frame;
        var outer = Frozen(c);
        var hair = Frozen(Mod.A(Mod.Mix(c, Colors.White, .25), .7));
        Frame(dc, r, 2, 3, outer, gap0, gap1);
        Frame(dc, new Rect(r.X + 4, r.Y + 4, r.Width - 8, r.Height - 8), 1, 2, hair, gap0, gap1);
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

    /// <summary>The cells of an LED bar <paramref name="w"/> wide: each cell's width and the pitch from one to the next.</summary>
    static (int N, double Pitch) Cells(double w, double cell, double gap)
    {
        int n = Math.Max(1, (int)Math.Floor((w + gap) / (cell + gap)));
        return (n, (w + gap) / n);
    }

    /// <summary>
    /// An LED bar: <paramref name="share"/> of its cells lit in <paramref name="tone"/> (the cell at the edge lit in part), each
    /// lit cell with a light line along its top and a faint bloom round the lit run, the cell at the head hot (nearly white);
    /// the unlit cells dim.
    /// </summary>
    static void DrawLed(DrawingContext dc, Rect r, double share, Color tone, double cell = 5, double gap = 2, double dimA = .1)
    {
        if (r.Width < 4 || r.Height < 2) return;
        var (n, pitch) = Cells(r.Width, cell, gap);
        double cw = pitch - gap, fill = Math.Clamp(share, 0, 1) * n;
        int whole = (int)Math.Floor(fill);
        double part = fill - whole;
        var dim = Frozen(Mod.A(Colors.White, dimA));
        var lit = Frozen(Mod.Mix(tone, Colors.White, .08));
        var top = Frozen(Mod.A(Colors.White, .42));
        // the bloom round the lit run
        if (fill > .05) dc.DrawRectangle(Frozen(Mod.A(tone, .2)), null, new Rect(r.X - 1.5, r.Y - 1.5, Math.Min(r.Width, fill * pitch - gap) + 3, r.Height + 3));
        for (int i = 0; i < n; i++)
        {
            var cr = new Rect(r.X + i * pitch, r.Y, cw, r.Height);
            if (i < whole)
            {
                bool head = i == whole - 1 && part < .5;
                dc.DrawRectangle(head ? Frozen(Mod.Mix(tone, Colors.White, .55)) : lit, null, cr);
                dc.DrawRectangle(top, null, new Rect(cr.X, cr.Y, cr.Width, 1));
            }
            else if (i == whole && part > .02)
            {
                dc.DrawRectangle(dim, null, cr);
                dc.DrawRectangle(Frozen(Mod.A(Mod.Mix(tone, Colors.White, .55), part)), null, cr);
            }
            else dc.DrawRectangle(dim, null, cr);
        }
    }

    /// <summary>
    /// The chase over an LED bar's cells (<paramref name="t"/> 0 to 1): a light three cells long running left to right, stepping
    /// cell by cell as an LED does, the cell at its front brightest; it runs the bar's whole length, lit cells and dim.
    /// </summary>
    static void DrawChase(DrawingContext dc, Rect r, double t, Color c, double cell = 5, double gap = 2)
    {
        if (t < 0 || t > 1 || r.Width < 4) return;
        var (n, pitch) = Cells(r.Width, cell, gap);
        double cw = pitch - gap;
        int c0 = (int)Math.Floor(-3 + (n + 6) * t);
        for (int i = Math.Max(0, c0 - 3); i <= Math.Min(n - 1, c0); i++)
        {
            double k = 1 - (c0 - i) / 3.5;
            if (k <= 0) continue;
            if (i == c0) dc.DrawRectangle(Frozen(Mod.A(c, .35)), null, new Rect(r.X + i * pitch - 2, r.Y - 2, cw + 4, r.Height + 4));
            dc.DrawRectangle(Frozen(Mod.A(c, .95 * k)), null, new Rect(r.X + i * pitch, r.Y, cw, r.Height));
        }
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
