using System.Windows;
using System.Windows.Media;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

/// <summary>
/// Telestrator's two layers: the analysis board (charcoal glass with a tactics grid ruled into it, a hairline rim and four
/// corner brackets, as an analyst's replay frame) and the caster's pen over it (bright marker strokes with a hand's wobble:
/// a loose circle, a quick underline, an arrow, a scribbled plus). Every stroke is a polyline built from a seed, so the
/// same mark comes out the same on every draw (no jitter), and can be drawn on part way (a stroke being drawn live).
/// </summary>
sealed partial class HudTelestrator
{
    static readonly Color BoardC = Color.FromRgb(0x17, 0x1A, 0x20), BoardDeepC = Color.FromRgb(0x0B, 0x0D, 0x11),
        PenC = Color.FromRgb(0xFF, 0xE1, 0x3B), PinkC = Color.FromRgb(0xFF, 0x47, 0xB0), PenMintC = Color.FromRgb(0x6B, 0xFF, 0xB4);

    /// <summary>The tactics board's ruling: a fine grid every 12 px and a firmer one every 48 px.</summary>
    static readonly DrawingBrush GridMinor = MakeGrid(12, .03), GridMajor = MakeGrid(48, .045);

    static DrawingBrush MakeGrid(double n, double a)
    {
        var lines = new GeometryGroup();
        lines.Children.Add(new RectangleGeometry(new Rect(0, 0, n, 1)));
        lines.Children.Add(new RectangleGeometry(new Rect(0, 0, 1, n)));
        var d = new GeometryDrawing(Mod.Br(Colors.White, a), null, lines);
        var b = new DrawingBrush(d)
        {
            TileMode = TileMode.Tile, Viewport = new Rect(0, 0, n, n), ViewportUnits = BrushMappingMode.Absolute,
            Viewbox = new Rect(0, 0, n, n), ViewboxUnits = BrushMappingMode.Absolute, Stretch = Stretch.Fill,
        };
        b.Freeze();
        return b;
    }

    /// <summary>
    /// The analysis board: a soft drop under it (outside its shape, inside the window's room), charcoal glass darker at its
    /// foot, the tactics grid, a faint light along its top, the hairline rim and the replay frame's corner brackets.
    /// </summary>
    static void Board(DrawingContext dc, Size s, double radius, double opacity, bool shadow = true, bool grid = true, double brackets = .4)
    {
        if (s.Width < 4 || s.Height < 4) return;
        // drawn once per size and setting, then replayed (a board is redrawn only when its size changes)
        var key = (Math.Round(s.Width, 1), Math.Round(s.Height, 1), radius, Math.Round(opacity, 3), shadow, grid, brackets);
        if (!Boards.TryGetValue(key, out var art))
        {
            if (Boards.Count > 48) Boards.Clear();
            var dg = new DrawingGroup();
            using (var bc = dg.Open()) BoardArt(bc, s, radius, opacity, shadow, grid, brackets);
            dg.Freeze();
            Boards[key] = art = dg;
        }
        dc.DrawDrawing(art);
    }

    static readonly Dictionary<(double, double, double, double, bool, bool, double), DrawingGroup> Boards = new();

    static void BoardArt(DrawingContext dc, Size s, double radius, double opacity, bool shadow, bool grid, double brackets)
    {
        var r = new Rect(.5, .5, s.Width - 1, s.Height - 1);
        var shape = Mod.Pill(r, radius);
        if (shadow)
            for (int k = 1; k <= 5; k++)
            {
                var o = new Rect(r.X - k * .9, r.Y - k * .4 + 2, r.Width + k * 1.8, r.Height + k * 1.2 + 1);
                var ring = new CombinedGeometry(GeometryCombineMode.Exclude, Mod.Pill(o, radius + k), shape);
                ring.Freeze();
                dc.DrawGeometry(Mod.Br(Colors.Black, .075 - k * .012), null, ring);
            }
        double a = Math.Clamp(.62 + .3 * opacity, .7, .94);
        dc.DrawGeometry(Mod.Grad(true, (Mod.A(BoardC, a), 0), (Mod.A(BoardDeepC, Math.Min(.96, a + .05)), 1)), null, shape);
        if (grid)
        {
            dc.PushClip(shape);
            dc.DrawRectangle(GridMinor, null, new Rect(0, 0, s.Width, s.Height));
            dc.DrawRectangle(GridMajor, null, new Rect(0, 0, s.Width, s.Height));
            dc.Pop();
        }
        if (s.Width > 2 * radius + 4)
            dc.DrawRectangle(Mod.Grad(false, (Mod.A(Colors.White, 0), 0), (Mod.A(Colors.White, .14), .5), (Mod.A(Colors.White, 0), 1)), null, new Rect(radius, 1, s.Width - 2 * radius, 1));
        dc.DrawGeometry(null, Mod.P(Mod.Br(Colors.White, .13), 1), shape);
        if (brackets > 0 && s.Height >= 20) Brackets(dc, r, brackets);
    }

    /// <summary>The replay frame's four corner brackets, 7 px each, just inside the rim.</summary>
    static void Brackets(DrawingContext dc, Rect r, double a)
    {
        const double L = 7, I = 3;
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            void Corner(Point o, double dx, double dy)
            {
                c.BeginFigure(new Point(o.X + dx * L, o.Y), false, false);
                c.LineTo(o, true, false);
                c.LineTo(new Point(o.X, o.Y + dy * L), true, false);
            }
            Corner(new Point(r.Left + I, r.Top + I), 1, 1); Corner(new Point(r.Right - I, r.Top + I), -1, 1);
            Corner(new Point(r.Left + I, r.Bottom - I), 1, -1); Corner(new Point(r.Right - I, r.Bottom - I), -1, -1);
        }
        g.Freeze();
        dc.DrawGeometry(null, Mod.P(Mod.Br(Colors.White, a), 1.2), g);
    }

    /// <summary>A pen of round caps and joins.</summary>
    static Pen PenOf(Color c, double w)
    {
        var key = (c, Math.Round(w, 2));
        if (Pens.TryGetValue(key, out var p)) return p;
        if (Pens.Count > 256) Pens.Clear();
        p = new Pen(Frozen(c), w) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        p.Freeze();
        return Pens[key] = p;
    }

    static readonly Dictionary<(Color, double), Pen> Pens = new();

    /// <summary>A pen line's geometry, made once per key (a mark that sits still is never rebuilt).</summary>
    static Geometry? Cached(string key, Func<Geometry?> make)
    {
        if (Shapes.TryGetValue(key, out var g)) return g;
        if (Shapes.Count > 256) Shapes.Clear();
        return Shapes[key] = make();
    }

    static readonly Dictionary<string, Geometry?> Shapes = new();

    /// <summary>A marker stroke: a dark edge under it, so it reads on a lit bar and over snow alike, then the ink.</summary>
    static void Ink(DrawingContext dc, Geometry? g, Color c, double w = 2.2, double a = 1)
    {
        if (g == null || a <= .01) return;
        dc.DrawGeometry(null, PenOf(Mod.A(Colors.Black, .5 * a), w + 2.2), g);
        dc.DrawGeometry(null, PenOf(Mod.A(c, a), w), g);
    }

    /// <summary>A pen's line through <paramref name="pts"/>, its first <paramref name="drawn"/> of its length (a stroke being drawn on).</summary>
    static Geometry? Poly(IReadOnlyList<Point> pts, double drawn = 1)
    {
        if (pts.Count < 2 || drawn <= .005) return null;
        double total = 0;
        for (int i = 1; i < pts.Count; i++) total += (pts[i] - pts[i - 1]).Length;
        double want = total * Math.Clamp(drawn, 0, 1), run = 0;
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(pts[0], false, false);
            for (int i = 1; i < pts.Count; i++)
            {
                double d = (pts[i] - pts[i - 1]).Length;
                if (run + d >= want) { double k = d > 0 ? (want - run) / d : 0; c.LineTo(pts[i - 1] + (pts[i] - pts[i - 1]) * k, true, true); break; }
                c.LineTo(pts[i], true, true);
                run += d;
            }
        }
        g.Freeze();
        return g;
    }

    /// <summary>
    /// A loose hand-drawn circle inside <paramref name="r"/> (never past it): it starts up at the left, runs clockwise a little
    /// past full (<paramref name="sweep"/> turns) and ends just outside where it began, its radius wandering as a hand's does.
    /// </summary>
    static List<Point> LoopPts(Rect r, int seed, double sweep = 1.15)
    {
        var rnd = new Random(seed);
        double p1 = rnd.NextDouble() * 6.283, p2 = rnd.NextDouble() * 6.283, tilt = (rnd.NextDouble() - .5) * .07, a0 = -2.45 + (rnd.NextDouble() - .5) * .4;
        var c = new Point(r.X + r.Width / 2, r.Y + r.Height / 2);
        const double Max = 1.17;
        int n = Math.Max(40, (int)((r.Width + r.Height) * 1.1 * sweep));
        var pts = new List<Point>(n + 1);
        double ct = Math.Cos(tilt), st = Math.Sin(tilt);
        for (int i = 0; i <= n; i++)
        {
            double k = (double)i / n, a = a0 + 2 * Math.PI * sweep * k;
            double f = (1 + .05 * Math.Sin(2 * a + p1) + .03 * Math.Sin(3 * a + p2)) * (.94 + .12 * k) / Max;
            double x = Math.Cos(a) * r.Width / 2 * f, y = Math.Sin(a) * r.Height / 2 * f;
            pts.Add(new Point(c.X + x * ct - y * st, c.Y + x * st + y * ct));
        }
        return pts;
    }

    /// <summary>A quick pen line from a to b, bowed <paramref name="bow"/> px to its left at the middle, with a hand's small waver.</summary>
    static List<Point> LinePts(Point a, Point b, double bow, int seed, double waver = .45)
    {
        var rnd = new Random(seed);
        double p = rnd.NextDouble() * 6.283;
        var d = b - a;
        double len = d.Length;
        var nrm = len > 0 ? new Vector(d.Y / len, -d.X / len) : new Vector(0, 0);
        int n = Math.Max(8, (int)(len / 2.5));
        var pts = new List<Point>(n + 1);
        for (int i = 0; i <= n; i++)
        {
            double k = (double)i / n;
            pts.Add(a + d * k + nrm * (bow * 4 * k * (1 - k) + waver * Math.Sin(k * 8.5 + p) * Math.Sin(Math.PI * k)));
        }
        return pts;
    }

    /// <summary>An arrow's head at the end of <paramref name="pts"/>: two quick strokes back from its tip.</summary>
    static Geometry? Head(IReadOnlyList<Point> pts, double size)
    {
        if (pts.Count < 3) return null;
        var tip = pts[^1];
        var dir = tip - pts[Math.Max(0, pts.Count - 4)];
        if (dir.Length < .01) return null;
        dir.Normalize();
        static Vector Rot(Vector v, double deg) { double t = deg * Math.PI / 180; return new Vector(v.X * Math.Cos(t) - v.Y * Math.Sin(t), v.X * Math.Sin(t) + v.Y * Math.Cos(t)); }
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(tip + Rot(-dir, 32) * size, false, false);
            c.LineTo(tip, true, true);
            c.LineTo(tip + Rot(-dir, -30) * size * .9, true, true);
        }
        g.Freeze();
        return g;
    }

    /// <summary>A pen arrow: its line drawn on to <paramref name="drawn"/>, the head once the line has reached its end.</summary>
    static void Arrow(DrawingContext dc, IReadOnlyList<Point> pts, Color c, double drawn, double a = 1, double w = 2, double head = 5)
    {
        Ink(dc, Poly(pts, Math.Min(1, drawn / .82)), c, w, a);
        if (drawn > .82) Ink(dc, Head(pts, head * Math.Clamp((drawn - .82) / .18, 0, 1) + .01), c, w, a);
    }

    /// <summary>A hand-drawn plus (a healer's note): two quick strokes in a <paramref name="size"/> px square at <paramref name="o"/>.</summary>
    static void Plus(DrawingContext dc, Point o, double size, Color c, int seed, double w = 1.7)
    {
        double s = size;
        Ink(dc, Poly(LinePts(new Point(o.X + .5, o.Y + s * .54), new Point(o.X + s - .5, o.Y + s * .46), .5, seed, .25)), c, w);
        Ink(dc, Poly(LinePts(new Point(o.X + s * .47, o.Y + .5), new Point(o.X + s * .53, o.Y + s - .5), -.4, seed + 3, .25)), c, w);
    }

    /// <summary>A member's seed for their marks (the same marks each draw).</summary>
    static int SeedOf(long id) => unchecked((int)(id ^ (id >> 32)) * 31 + 7);
}
