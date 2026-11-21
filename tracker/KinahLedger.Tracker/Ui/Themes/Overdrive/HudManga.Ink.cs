using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

/// <summary>
/// Manga's ink: the page's palette (ink black, paper white, one hot red; the class colours on the bars are the only other
/// colours), its faces, and the drawn parts every piece of the look is made of. Each part is built once as a frozen Drawing
/// (an ink panel with a brushed edge, screentone, splatter, focus lines, a speed-line burst, an explosion balloon, a
/// sparkle, a hand-lettered sound effect) and shown by an <see cref="InkArt"/>, so a moment only moves and fades it.
/// </summary>
sealed partial class HudManga
{
    static readonly Color InkC = Color.FromRgb(0x0A, 0x0A, 0x0D), PaperC = Color.FromRgb(0xFB, 0xF9, 0xF2), RedC = Color.FromRgb(0xE8, 0x1C, 0x2B),
        GreyC = Color.FromRgb(0x6A, 0x6A, 0x72);
    static readonly SolidColorBrush InkB = Frozen(InkC), PaperB = Frozen(PaperC), RedB = Frozen(RedC), White = Frozen(Colors.White),
        OnInkSoft = Frozen(Mod.A(Colors.White, .88)), OnInkQuiet = Frozen(Mod.A(Colors.White, .62)),
        OnPaperSoft = Frozen(Mod.A(InkC, .76)), OnPaperQuiet = Frozen(Mod.A(InkC, .58));

    /// <summary>The figures' face: Impact, crisp condensed digits (the clock, the DPS, the hour).</summary>
    static readonly FontFamily FigFace = HudThemes.Impact;
    /// <summary>The names' and titles' face: Segoe UI Black, the heavy lettering of a manga's captions.</summary>
    static readonly FontFamily NameFace = HudThemes.Heavy;

    /// <summary>A text in the figures' face, tabular.</summary>
    static TextBlock Fig(string s, double size, Brush fg) => Mod.Text(s, size, fg, FontWeights.Normal, FigFace);

    // ================================================================ the pseudo-random hand

    /// <summary>A steady wobble, -1 to 1, for the <paramref name="i"/>th point of the part <paramref name="seed"/> (the same every draw).</summary>
    static double J(int seed, int i)
    {
        double v = Math.Sin(seed * 12.9898 + i * 78.233) * 43758.5453;
        return (v - Math.Floor(v)) * 2 - 1;
    }

    /// <summary>
    /// A closed ink shape through <paramref name="pts"/>: each edge cut into ~<paramref name="step"/> px pieces, each piece
    /// nudged off the line by up to <paramref name="rough"/> px, so the edge reads as a brush's, not a ruler's.
    /// </summary>
    static Geometry Brushed(IReadOnlyList<Point> pts, int seed, double rough, double step = 11)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            int k = 0;
            for (int e = 0; e < pts.Count; e++)
            {
                Point a = pts[e], b = pts[(e + 1) % pts.Count];
                if (e == 0) c.BeginFigure(a, true, true);
                double len = (b - a).Length;
                int n = Math.Max(1, (int)Math.Round(len / step));
                var nrm = new Vector(-(b.Y - a.Y), b.X - a.X); if (len > 0) nrm /= len;
                for (int i = 1; i <= n; i++)
                {
                    var p = a + (b - a) * (i / (double)n);
                    if (i < n) p += nrm * (rough * J(seed, k++));
                    c.LineTo(p, true, true);
                }
            }
        }
        g.Freeze();
        return g;
    }

    static Point P(double x, double y) => new(x, y);

    // ================================================================ the parts

    /// <summary>Screentone: a dot grid at 45 degrees whose dots swell toward <paramref name="corner"/> and vanish by <paramref name="reach"/> px, clipped to <paramref name="clip"/>.</summary>
    static void Tone(DrawingContext dc, Geometry clip, Point corner, double reach, double pitch = 4.2, double maxR = 1.75, Brush? ink = null)
    {
        var b = clip.Bounds;
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            int row = 0;
            for (double y = b.Top - pitch; y <= b.Bottom + pitch; y += pitch * .5, row++)
                for (double x = b.Left - pitch + (row % 2) * pitch * .5; x <= b.Right + pitch; x += pitch)
                {
                    double d = (new Point(x, y) - corner).Length, k = 1 - d / reach;
                    if (k <= 0) continue;
                    double r = maxR * Math.Pow(k, .8);
                    if (r < .42) continue;
                    c.BeginFigure(new Point(x + r, y), true, true);
                    c.ArcTo(new Point(x - r, y), new Size(r, r), 0, false, SweepDirection.Clockwise, true, false);
                    c.ArcTo(new Point(x + r, y), new Size(r, r), 0, false, SweepDirection.Clockwise, true, false);
                }
        }
        g.Freeze();
        dc.PushClip(clip);
        dc.DrawGeometry(ink ?? InkB, null, g);
        dc.Pop();
    }

    /// <summary>An ink splatter: a blot and its flung drops round <paramref name="at"/>, flung toward <paramref name="toward"/>.</summary>
    static void Splat(DrawingContext dc, Point at, Vector toward, int seed, double size, Brush ink)
    {
        toward.Normalize();
        dc.DrawGeometry(ink, null, Brushed(Ring(at, size, size * .78, 9, seed), seed, size * .16, size * .7));
        for (int i = 0; i < 5; i++)
        {
            double along = size * (1.3 + 1.4 * (J(seed, 40 + i) + 1) / 2), side = size * .8 * J(seed, 60 + i);
            var p = at + toward * along + new Vector(-toward.Y, toward.X) * side;
            double r = size * (.12 + .14 * (J(seed, 80 + i) + 1) / 2) * (1 - i * .08);
            dc.DrawEllipse(ink, null, p, r, r);
        }
    }

    /// <summary>Points round an ellipse (for a blot, a bubble, a balloon), <paramref name="n"/> of them.</summary>
    static List<Point> Ring(Point c, double rx, double ry, int n, int seed, double wob = 0)
    {
        var pts = new List<Point>();
        for (int i = 0; i < n; i++)
        {
            double a = 2 * Math.PI * i / n, k = 1 + wob * J(seed, i);
            pts.Add(new Point(c.X + rx * k * Math.Cos(a), c.Y + ry * k * Math.Sin(a)));
        }
        return pts;
    }

    /// <summary>
    /// Speed lines bursting out of <paramref name="c"/>: <paramref name="n"/> thin wedges between the radii, each its own
    /// length and weight (the concentration lines of a manga's big moment); <paramref name="sx"/>, <paramref name="sy"/> stretch it.
    /// </summary>
    static Geometry Burst(Point c, double rIn, double rOut, int n, int seed, double weight = 1.4, double sx = 1, double sy = 1)
    {
        var g = new StreamGeometry();
        using (var x = g.Open())
            for (int i = 0; i < n; i++)
            {
                double a = 2 * Math.PI * (i + .45 * J(seed, i)) / n;
                double r0 = rIn * (1 + .3 * J(seed, 200 + i)), r1 = rOut * (.8 + .2 * J(seed, 400 + i));
                double w = weight * (.55 + .45 * (J(seed, 600 + i) + 1) / 2);
                var dir = new Vector(sx * Math.Cos(a), sy * Math.Sin(a));
                var nrm = new Vector(-dir.Y, dir.X); nrm.Normalize();
                Point Q(double r) => new(c.X + dir.X * r, c.Y + dir.Y * r);
                var tip = Q(r0); var end = Q(r1);
                x.BeginFigure(tip, true, true);
                x.LineTo(end + nrm * w, true, false);
                x.LineTo(end - nrm * w, true, false);
            }
        g.Freeze();
        return g;
    }

    /// <summary>A speed line: a wedge from a point at <paramref name="from"/> to an end <paramref name="w"/> px thick at <paramref name="to"/>.</summary>
    static Geometry Streak(Point from, Point to, double w)
    {
        var d = to - from; if (d.Length < .01) d = new Vector(1, 0);
        d.Normalize();
        var n = new Vector(-d.Y, d.X) * (w / 2);
        var g = new StreamGeometry();
        using (var c = g.Open()) { c.BeginFigure(from, true, true); c.LineTo(to + n, true, false); c.LineTo(to - n, true, false); }
        g.Freeze();
        return g;
    }

    /// <summary>An explosion balloon: <paramref name="n"/> spikes round <paramref name="c"/>, each tip its own length.</summary>
    static Geometry Balloon(Point c, double rOut, double rIn, int n, int seed, double sy = 1)
    {
        var pts = new List<Point>();
        for (int i = 0; i < n * 2; i++)
        {
            double a = Math.PI * i / n - Math.PI / 2, r = i % 2 == 0 ? rOut * (.82 + .18 * J(seed, i)) : rIn * (.92 + .08 * J(seed, i));
            pts.Add(new Point(c.X + r * Math.Cos(a), c.Y + sy * r * Math.Sin(a)));
        }
        var g = new StreamGeometry();
        using (var x = g.Open()) { x.BeginFigure(pts[0], true, true); x.PolyLineTo(pts.Skip(1).ToList(), true, true); }
        g.Freeze();
        return g;
    }

    /// <summary>A shoujo sparkle: a four-pointed star of <paramref name="r"/> round <paramref name="c"/>.</summary>
    static Geometry Sparkle(Point c, double r)
    {
        double k = r * .16;
        var g = new StreamGeometry();
        using (var x = g.Open())
        {
            x.BeginFigure(P(c.X, c.Y - r), true, true);
            x.QuadraticBezierTo(P(c.X + k, c.Y - k), P(c.X + r, c.Y), true, true);
            x.QuadraticBezierTo(P(c.X + k, c.Y + k), P(c.X, c.Y + r), true, true);
            x.QuadraticBezierTo(P(c.X - k, c.Y + k), P(c.X - r, c.Y), true, true);
            x.QuadraticBezierTo(P(c.X - k, c.Y - k), P(c.X, c.Y - r), true, true);
        }
        g.Freeze();
        return g;
    }

    static readonly Dictionary<string, Geometry> Letters = new();

    /// <summary>A sound effect's words as outlines in Impact, slanted like hand lettering (cached per word and size).</summary>
    static Geometry Word(string s, double size, double slant = -12)
    {
        var key = s + "|" + size + "|" + slant;
        if (Letters.TryGetValue(key, out var g)) return g;
        var ft = new FormattedText(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(FigFace, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal), size, Brushes.Black, 1.25);
        var geo = ft.BuildGeometry(new Point(0, 0));
        var tg = new TransformGroup();
        tg.Children.Add(new SkewTransform(slant, 0, 0, ft.Height / 2));
        tg.Children.Add(new ScaleTransform(1, 1.08));
        var pg = PathGeometry.CreateFromGeometry(geo);
        pg.Transform = tg;
        var flat = PathGeometry.CreateFromGeometry(pg);
        flat.Freeze();
        if (Letters.Count > 64) Letters.Clear();
        Letters[key] = flat;
        return flat;
    }

    /// <summary>
    /// A hand-lettered sound effect: the word in <paramref name="fill"/> inside a thick <paramref name="edge"/> outline, a
    /// hard <paramref name="shadow"/> stamped behind it (none: null), tilted <paramref name="angle"/> degrees; placed at its
    /// own origin with its bounds' size in <paramref name="size"/>.
    /// </summary>
    static Drawing Lettered(string s, double em, Color fill, Color edge, Color? shadow, double edgeW, double angle, out Size size)
    {
        var geo = Word(s, em);
        var dg = new DrawingGroup();
        using (var dc = dg.Open())
        {
            if (shadow is { } sh)
            {
                dc.PushTransform(new TranslateTransform(em * .08, em * .08));
                dc.DrawGeometry(Frozen(sh), Pn(Frozen(sh), edgeW), geo);
                dc.Pop();
            }
            dc.DrawGeometry(null, Pn(Frozen(edge), edgeW), geo);
            dc.DrawGeometry(Frozen(fill), null, geo);
        }
        dg.Transform = new RotateTransform(angle);
        var b = dg.Bounds;
        var outer = new DrawingGroup();
        outer.Children.Add(dg);
        outer.Transform = new TranslateTransform(-b.Left, -b.Top);
        outer.Freeze();
        size = new Size(Math.Ceiling(b.Width), Math.Ceiling(b.Height));
        return outer;
    }

    /// <summary>A frozen drawing made by <paramref name="paint"/>.</summary>
    static DrawingGroup Drawn(Action<DrawingContext> paint)
    {
        var dg = new DrawingGroup();
        using (var dc = dg.Open()) paint(dc);
        dg.Freeze();
        return dg;
    }

    static Pen Pn(Brush b, double w, bool round = true)
    {
        var p = new Pen(b, w) { LineJoin = round ? PenLineJoin.Round : PenLineJoin.Miter, StartLineCap = round ? PenLineCap.Round : PenLineCap.Flat, EndLineCap = round ? PenLineCap.Round : PenLineCap.Flat };
        p.Freeze();
        return p;
    }

    /// <summary>A narration box: a black ink caption with a white brushed hairline inside, white words (the notice, the run's head, a timer).</summary>
    static Border Caption(FrameworkElement child, Thickness pad, bool red = false) => new()
    {
        Child = child, Padding = pad, Background = red ? RedB : InkB, BorderBrush = red ? InkB : Frozen(Mod.A(Colors.White, .85)), BorderThickness = new Thickness(1.2),
        VerticalAlignment = VerticalAlignment.Center, SnapsToDevicePixels = true,
    };
}

/// <summary>
/// A frozen drawing shown as it is: rendered once when it is set, then only moved, turned, scaled or faded by its
/// transforms and opacity (the look's moments never redraw it). Its size is its own Width and Height.
/// </summary>
sealed class InkArt : FrameworkElement
{
    Drawing? _art;
    public InkArt(Drawing? art = null, double w = double.NaN, double h = double.NaN) { _art = art; Width = w; Height = h; IsHitTestVisible = false; }
    public Drawing? Art { get => _art; set { if (ReferenceEquals(_art, value)) return; _art = value; InvalidateVisual(); } }
    protected override void OnRender(DrawingContext dc) { if (_art != null) dc.DrawDrawing(_art); }
}
