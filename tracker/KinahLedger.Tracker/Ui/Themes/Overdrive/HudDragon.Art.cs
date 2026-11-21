using System.Windows;
using System.Windows.Media;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

/// <summary>
/// Dragon's art, all vector and procedural, each piece drawn once and frozen: the Balaur's horned head (a profile facing
/// right, its slit eye and the fire in its jaws), its scaled body laid along a spine (an obsidian band with two rows of
/// scales rimmed in a bronze-gold metallic sheen, dorsal spikes on the outside, an emerald glow along its belly, a spade at
/// the tail's tip), the dragon-fire bar (a class-coloured stream with a hot core and a flame front of three tongues), the
/// rank's scale crest, the ouroboros ring and the flame burst a breath throws. A coil for a size is one frozen
/// DrawingGroup, kept in a small cache, so a frame costs one draw of it.
/// </summary>
sealed partial class HudDragon
{
    // the palette: obsidian scales, a bronze-gold sheen, molten gold, an emerald under-glow, ivory horns
    static readonly Color ObsidianC = Color.FromRgb(0x0C, 0x0A, 0x10), ObsidianHiC = Color.FromRgb(0x2C, 0x24, 0x36), ScaleC = Color.FromRgb(0x1C, 0x17, 0x23),
        BronzeC = Color.FromRgb(0xB8, 0x93, 0x5A), DragonGoldC = Color.FromRgb(0xFF, 0xC4, 0x45), HotC = Color.FromRgb(0xFF, 0xEC, 0xB0), EmberC = Color.FromRgb(0xFF, 0x86, 0x2A),
        EmeraldC = Color.FromRgb(0x34, 0xF0, 0xAE), IvoryC = Color.FromRgb(0xF0, 0xE4, 0xC4), AshC = Color.FromRgb(0xC9, 0xC2, 0xCF);
    static readonly SolidColorBrush GoldInk = Frozen(Color.FromRgb(0xFF, 0xD7, 0x7A)), EmeraldInk = Frozen(Color.FromRgb(0x7D, 0xFF, 0xCF)), IvoryInk = Frozen(IvoryC);

    /// <summary>Serif for the fight's title and the big words (every Windows has these).</summary>
    static readonly FontFamily Serif = new("Palatino Linotype, Cambria, Georgia");

    static T Fz<T>(T f) where T : Freezable { f.Freeze(); return f; }
    static RadialGradientBrush Glow(Color c, double a) => Fz(new RadialGradientBrush(Mod.A(c, a), Mod.A(c, 0)));

    // ================================================================ the head

    /// <summary>The head's box in its own units (it faces right); the eye's and the jaws' points in it.</summary>
    const double HeadW = 56, HeadH = 40;
    static readonly Point EyeAt = new(34, 15.4), MouthAt = new(52, 26.4);

    /// <summary>The eye's lid in the head's units (the meter blinks it over the eye).</summary>
    internal const string LidPath = "M 29.2,13.6 C 32.4,12.8 36.4,14.2 39,16.9 C 35.8,17.6 31.8,16.7 29.2,13.6 Z";

    static Geometry G(string data) { var g = Geometry.Parse(data); g.Freeze(); return g; }

    /// <summary>The Balaur's head, drawn once (its own units; place it with a transform).</summary>
    static readonly DrawingGroup HeadArt = MakeHead();

    static DrawingGroup MakeHead()
    {
        var g = new DrawingGroup();
        using (var dc = g.Open())
        {
            var skin = Mod.Grad(true, (Color.FromRgb(0x46, 0x3C, 0x52), 0), (Color.FromRgb(0x1E, 0x19, 0x26), .45), (ObsidianC, 1));
            var rim = Mod.P(Mod.Grad(false, (Color.FromRgb(0x7A, 0x5E, 0x36), 0), (Color.FromRgb(0xF4, 0xD6, 0x92), .55), (Color.FromRgb(0x8A, 0x6A, 0x3C), 1)), .75);
            var horn = Mod.Grad(false, (Color.FromRgb(0x6E, 0x62, 0x4C), 0), (IvoryC, .75), (Color.FromRgb(0xFF, 0xF8, 0xE6), 1));
            var hornRim = Mod.P(Frozen(Color.FromArgb(0xCC, 0x1A, 0x14, 0x10)), .55);
            var ink = Frozen(Color.FromRgb(0x08, 0x06, 0x0A));
            // the roar's light: the fire in the open jaws glowing out past them
            dc.DrawEllipse(Fz(new RadialGradientBrush(Mod.A(DragonGoldC, .7), Mod.A(EmberC, 0))), null, new Point(47, 26.5), 14, 9);
            // the great horns: two swept back and rising, behind the skull
            dc.DrawGeometry(horn, hornRim, G("M 25,10.5 C 20,4.2 12,0.9 0.6,0.5 C 7.5,2.6 13.5,6.4 17.2,13 Z"));
            dc.DrawGeometry(horn, hornRim, G("M 18.5,14.2 C 13.5,9.6 7.6,7.4 0.4,7.8 C 5.6,9.4 9.6,12.2 12.6,17 Z"));
            // the spines down the back of the skull and the neck
            dc.DrawGeometry(skin, rim, G("M 12.6,17 L 4.4,15.8 L 11.4,20.4 L 3.2,21.6 L 11.2,24 Z"));
            // the cheek blades, swept back over the neck's join
            dc.DrawGeometry(skin, rim, G("M 14,23 L 1.6,25.4 L 10.6,28 L 2.6,33.6 L 13.2,31 L 8.6,38.6 L 19.6,32.6 L 24,30 Z"));
            // the lower jaw, thrown wide open in the roar
            dc.DrawGeometry(skin, rim, G("M 23.5,24.6 L 34,28.4 L 46,33 C 49,34 51.2,34.6 52.6,35.6 L 50.4,37.2 C 44,38.4 36,37.8 28,35.2 C 22,33.4 17,31 14,28 C 17.4,27.6 20.6,26.4 23.5,24.6 Z"));
            // inside the jaws: the throat's dark and the fire coming up it
            dc.DrawGeometry(Mod.Grad(false, (Color.FromRgb(0x3A, 0x08, 0x06), 0), (Color.FromRgb(0x9A, 0x1E, 0x0C), .55), (EmberC, 1)), null, G("M 24,24.2 L 54,21.6 L 52,34.4 L 46,32.6 L 34,28.2 Z"));
            dc.DrawGeometry(Mod.Grad(false, (Mod.A(EmberC, 0), 0), (DragonGoldC, .5), (HotC, 1)), null, G("M 30,25.8 C 38,24.6 46,24.4 55.6,23.6 C 53,26.4 54.6,28.4 55.8,30.4 C 48,29.6 40,28.6 30,25.8 Z"));
            // the upper fangs (two long ones) and the lower teeth, set before the jaws' rims so their roots sit inside
            dc.DrawGeometry(Frozen(IvoryC), hornRim, G("M 36,22.8 L 37.2,26 L 38.4,22.7 Z M 40.6,22.4 L 42.2,27.6 L 43.6,22.2 Z M 45.6,22 L 46.6,25.2 L 47.6,21.9 Z M 49.6,21.7 L 51.2,27.4 L 52.6,21.4 Z"));
            dc.DrawGeometry(Frozen(IvoryC), hornRim, G("M 35.4,29.4 L 36.8,26.4 L 38,30 Z M 40.4,31.2 L 41.8,27.6 L 43.2,31.8 Z M 45.6,33.2 L 47,29.4 L 48.4,33.8 Z M 49.8,34.6 L 50.8,31.8 L 51.8,35 Z"));
            // the skull and upper jaw: a deep wedge, the snout's horn at its tip
            dc.DrawGeometry(skin, rim, G("M 11,20 C 11,14.4 15,10.6 21,9.4 L 28,8.6 C 32,8.8 36,10.6 39.6,12.6 L 48,15.4 L 50.6,11.4 L 51.6,15.8 C 54,16.6 55.6,17.8 56,19.8 L 55.4,22 L 45,22.4 L 36,23 L 24,24.6 C 19,26.4 14.4,26.4 11.4,24.4 Z"));
            // the brow horn and the ridge of spikes over it
            dc.DrawGeometry(horn, hornRim, G("M 26.4,9.6 C 26.6,5.6 28.6,2.6 32.4,0.8 C 31,3.6 30.8,6.6 31.6,9.6 Z"));
            dc.DrawGeometry(horn, hornRim, G("M 33.4,10.4 C 34.2,7.6 35.8,5.8 38.4,4.8 C 37.4,6.8 37.2,8.8 37.8,11.4 Z"));
            // scales on the skull and the snout
            var scale = Mod.P(Frozen(Mod.A(Color.FromRgb(0xD8, 0xB4, 0x72), .55)), .55);
            foreach (var d in new[] { "M 13.6,20.4 L 15.8,17.8 L 18,20.4", "M 17.8,16.6 L 20,14 L 22.2,16.6", "M 18.6,23 L 20.8,20.4 L 23,23", "M 22.8,19.6 L 25,17 L 27.2,19.6", "M 41,18 L 43,15.8 L 45,18.2", "M 45.4,19.4 L 47.2,17.4 L 49,19.6" })
                dc.DrawGeometry(null, scale, G(d));
            // the scowl: a heavy brow plate driven down toward the snout over the eye
            dc.DrawGeometry(Mod.Grad(true, (Color.FromRgb(0x52, 0x46, 0x5E), 0), (ObsidianC, 1)), rim, G("M 26,12.4 C 30,10.2 35,10.4 41.4,15.6 L 39.8,16.2 C 35.6,13.8 31,13 27,14.4 Z"));
            dc.DrawGeometry(null, Mod.P(ink, 1.2, true), G("M 50.4,17.8 L 53.6,19.2"));
            // the snarl's wrinkles along the upper lip
            dc.DrawGeometry(null, Mod.P(Frozen(Mod.A(Color.FromRgb(0x0A, 0x08, 0x0C), .8)), .7, true), G("M 39,19.4 Q 41.6,20.8 44,19.6 M 44.6,20.4 Q 47,21.4 49.4,20.2"));
            // the emerald under-glow along the jaw
            dc.DrawGeometry(null, Mod.P(Frozen(Mod.A(EmeraldC, .3)), 2.6, true), G("M 22,33.4 C 30,36.6 40,38 50.4,37.2"));
            dc.DrawGeometry(null, Mod.P(Frozen(Mod.A(EmeraldC, .9)), .8, true), G("M 22,33.4 C 30,36.6 40,38 50.4,37.2"));
            // the eye: narrowed under the brow, molten, its slit a blade
            dc.DrawEllipse(Glow(EmberC, .95), null, EyeAt, 9, 6);
            dc.DrawEllipse(Glow(DragonGoldC, .9), null, EyeAt, 5.5, 3.6);
            dc.DrawGeometry(Fz(new RadialGradientBrush(HotC, EmberC) { GradientOrigin = new Point(.4, .4) }), Mod.P(ink, .6), G("M 29.6,14.2 C 32.6,13.6 36.4,14.8 38.6,16.8 C 35.6,17.4 32,16.8 29.6,14.2 Z"));
            dc.PushTransform(new RotateTransform(-8, 34.2, 15.6));
            dc.DrawEllipse(Frozen(Color.FromRgb(0x08, 0x04, 0x02)), null, new Point(34.2, 15.6), .55, 1.9);
            dc.Pop();
            dc.DrawEllipse(Frozen(Mod.A(Colors.White, .9)), null, new Point(32.4, 14.8), .5, .4);
        }
        g.Freeze();
        return g;
    }

    /// <summary>The head at (<paramref name="x"/>, <paramref name="y"/>), <paramref name="k"/> times its own size.</summary>
    static void Head(DrawingContext dc, double x, double y, double k)
    {
        dc.PushTransform(new MatrixTransform(k, 0, 0, k, x, y));
        dc.DrawDrawing(HeadArt);
        dc.Pop();
    }

    // ================================================================ the body along a spine

    /// <summary>A spine flattened to a polyline, read by its length: a point and the way it runs there.</summary>
    sealed class Spine
    {
        readonly List<Point> _p = new();
        readonly List<double> _l = new();
        public double Length { get; private set; }

        public Spine(Geometry g)
        {
            var flat = g.GetFlattenedPathGeometry(.2, ToleranceType.Absolute);
            foreach (var f in flat.Figures)
            {
                Add(f.StartPoint);
                foreach (var seg in f.Segments)
                    if (seg is PolyLineSegment pl) foreach (var p in pl.Points) Add(p);
                    else if (seg is LineSegment ls) Add(ls.Point);
            }
        }

        void Add(Point p)
        {
            if (_p.Count > 0) { double d = (p - _p[^1]).Length; if (d < 1e-6) return; Length += d; }
            _p.Add(p); _l.Add(Length);
        }

        public (Point P, Vector T) At(double d)
        {
            d = Math.Clamp(d, 0, Length);
            int lo = 0, hi = _l.Count - 1;
            while (hi - lo > 1) { int mid = (lo + hi) / 2; if (_l[mid] <= d) lo = mid; else hi = mid; }
            var a = _p[lo]; var b = _p[hi];
            double seg = _l[hi] - _l[lo];
            var t = b - a; if (t.Length > 1e-9) t.Normalize(); else t = new Vector(1, 0);
            return (seg > 1e-9 ? a + (b - a) * ((d - _l[lo]) / seg) : a, t);
        }
    }

    /// <summary>The metallic sheen over a drawing of <paramref name="w"/> x <paramref name="h"/>: bands of bronze and pale gold crossing it.</summary>
    static LinearGradientBrush Sheen(double w, double h, double a = 1)
    {
        var b = new LinearGradientBrush { MappingMode = BrushMappingMode.Absolute, StartPoint = new Point(0, 0), EndPoint = new Point(Math.Max(60, w * .5), Math.Max(40, h * .5)), SpreadMethod = GradientSpreadMethod.Reflect };
        foreach (var (c, at) in new[] { (Color.FromRgb(0x5E, 0x48, 0x2A), 0.0), (Color.FromRgb(0xF6, 0xDA, 0x98), .22), (Color.FromRgb(0x6A, 0x52, 0x30), .45), (Color.FromRgb(0xE2, 0xBC, 0x72), .7), (Color.FromRgb(0x4C, 0x3A, 0x22), 1.0) })
            b.GradientStops.Add(new GradientStop(Mod.A(c, a), at));
        b.Freeze();
        return b;
    }

    /// <summary>
    /// The scaled body along <paramref name="sp"/>, <paramref name="thick"/> px across at each point (0 the start, 1 the tail's
    /// tip). Its outside is on the left of the way it runs (the coil runs counter-clockwise round the meter): the dorsal
    /// spikes there, the emerald under-glow along the inside.
    /// </summary>
    static void Body(DrawingContext dc, Spine sp, Func<double, double> thick, LinearGradientBrush sheen, bool spikes = true, bool spade = true, double glow = 1, double spikeMax = 8, double spikeStep = 15, double plateStep = 7.4, double twoRowsAt = 10)
    {
        double L = sp.Length;
        if (L < 4) return;
        int n = Math.Max(8, (int)Math.Ceiling(L / 1.5));
        var outer = new List<Point>(n + 1); var inner = new List<Point>(n + 1); var belly = new List<Point>(n + 1);
        for (int i = 0; i <= n; i++)
        {
            double d = L * i / n;
            var (p, t) = sp.At(d);
            var nv = new Vector(-t.Y, t.X);
            double w = thick(d / L) / 2;
            outer.Add(p + nv * w); inner.Add(p - nv * w);
            belly.Add(p - nv * Math.Max(0, w - .7));
        }
        var outline = new StreamGeometry();
        using (var c = outline.Open())
        {
            c.BeginFigure(outer[0], true, true);
            c.PolyLineTo(outer.Skip(1).ToList(), true, true);
            inner.Reverse();
            c.PolyLineTo(inner, true, true);
        }
        outline.Freeze();
        dc.DrawGeometry(Frozen(ObsidianC), Mod.P(Frozen(Color.FromArgb(0xE6, 0x05, 0x04, 0x07)), 1.1), outline);
        var rimLine = new List<Point>(n + 1);
        for (int i = 0; i <= n; i++) { var (p, t) = sp.At(L * i / n); rimLine.Add(p + new Vector(-t.Y, t.X) * Math.Max(0, thick((double)i / n) / 2 - .6)); }

        // a rounded end to the tail where it has no spade
        if (!spade) { var (pe, _) = sp.At(L); double we = thick(1) / 2; dc.DrawEllipse(Frozen(ObsidianC), Mod.P(sheen, .7), pe, we, we); }
        // the spade at the tail's tip
        if (spade)
        {
            var (p, t) = sp.At(L);
            var nv = new Vector(-t.Y, t.X);
            var sg = new StreamGeometry();
            using (var c = sg.Open())
            {
                c.BeginFigure(p - t * 2, true, true);
                c.BezierTo(p + nv * 3 - t * 1, p + nv * 7.5 + t * 1, p + nv * 2.5 + t * 4, true, true);
                c.LineTo(p + t * 11, true, true);
                c.LineTo(p - nv * 2.5 + t * 4, true, true);
                c.BezierTo(p - nv * 7.5 + t * 1, p - nv * 3 - t * 1, p - t * 2, true, true);
            }
            sg.Freeze();
            dc.DrawGeometry(Frozen(ScaleC), Mod.P(sheen, .8), sg);
        }

        // two rows of plates, staggered, their rounded free edges toward the tail: each a dark plate with a lit bronze rim
        var plates = new StreamGeometry(); var rims = new StreamGeometry();
        using (var c = plates.Open())
        using (var rc = rims.Open())
        {
            double step = plateStep;
            for (double d = 2; d < L - 3; d += step)
            {
                double w = thick(d / L) / 2;
                if (w < 1.6) continue;
                int rows = w * 2 >= twoRowsAt ? 2 : 1;
                for (int r = 0; r < rows; r++)
                {
                    double dd = d + (r == 1 ? step / 2 : 0);
                    var (p, t) = sp.At(dd);
                    var nv = new Vector(-t.Y, t.X);
                    double off = rows == 2 ? (r == 0 ? w * .44 : -w * .44) : 0, hw = rows == 2 ? w * .5 : w * .74, len = Math.Min(step * .9, hw * 1.7);
                    var ctr = p + nv * off;
                    Point a = ctr + nv * hw, b = ctr - nv * hw;
                    c.BeginFigure(a, true, true);
                    c.BezierTo(a + t * len, b + t * len, b, true, true);
                    rc.BeginFigure(a + t * len * .25, false, false);
                    rc.BezierTo(a + t * len, b + t * len, b + t * len * .25, true, true);
                }
            }
        }
        plates.Freeze(); rims.Freeze();
        dc.DrawGeometry(ScaleFill, null, plates);
        dc.DrawGeometry(null, Mod.P(Frozen(Color.FromArgb(0xE0, 0x05, 0x04, 0x07)), 1.6), rims);
        dc.DrawGeometry(null, Mod.P(sheen, .8), rims);

        // the bronze edge along the outside, as light catching the coil's back
        dc.DrawGeometry(null, Mod.P(sheen, .9, true), Poly(rimLine));

        // the dorsal spikes, raked toward the tail
        if (spikes)
        {
            var sg = new StreamGeometry();
            int k = 0;
            using (var c = sg.Open())
                for (double d = 10; d < L - 12; d += spikeStep * (.7 + .6 * ((k * 0.6180339887) % 1)), k++)
                {
                    double w = thick(d / L) / 2;
                    if (w < 2.6) continue;
                    var (p, t) = sp.At(d);
                    var nv = new Vector(-t.Y, t.X);
                    double len = Math.Min(spikeMax, w * .9) * (k % 2 == 0 ? 1 : .55), bw = Math.Min(k % 2 == 0 ? 4.2 : 3, w * .6);
                    c.BeginFigure(p + nv * (w * .6) - t * bw, true, true);
                    c.QuadraticBezierTo(p + nv * (w + len * .4) - t * bw * .2, p + nv * (w + len) + t * bw * 1.1, true, true);
                    c.QuadraticBezierTo(p + nv * (w + len * .2) + t * bw * .6, p + nv * (w * .6) + t * bw, true, true);
                }
            sg.Freeze();
            dc.DrawGeometry(Mod.Grad(true, (IvoryC, 0), (Color.FromRgb(0x9C, 0x80, 0x52), .45), (Color.FromRgb(0x2A, 0x22, 0x30), 1)), Mod.P(Frozen(Color.FromArgb(0xDD, 0x06, 0x05, 0x08)), .6), sg);
        }

        // the emerald under-glow along the belly (the inside, toward the meter)
        if (glow > 0)
        {
            var bg = Poly(belly);
            dc.DrawGeometry(null, Mod.P(Frozen(Mod.A(EmeraldC, .2 * glow)), 4, true), bg);
            dc.DrawGeometry(null, Mod.P(Frozen(Mod.A(EmeraldC, .85 * glow)), 1, true), bg);
        }
    }

    /// <summary>The scales' own colour: obsidian with a violet lift, lit from above.</summary>
    static readonly LinearGradientBrush ScaleFill = Mod.Grad(true, (Color.FromRgb(0x3E, 0x33, 0x4C), 0), (Color.FromRgb(0x24, 0x1D, 0x2E), 1));

    static StreamGeometry Poly(List<Point> pts)
    {
        var g = new StreamGeometry();
        using (var c = g.Open()) { c.BeginFigure(pts[0], false, false); c.PolyLineTo(pts.Skip(1).ToList(), true, true); }
        g.Freeze();
        return g;
    }

    // ================================================================ the meter's coil

    /// <summary>The meter's frame: the room inside the coil, where the words go.</summary>
    static readonly Thickness FightPad = new(27, 25, 22, 15);

    /// <summary>Where the head sits on the meter, and its size; the words beside it start at <see cref="TitleX"/>.</summary>
    const double FightHeadX = 0, FightHeadY = 0, FightHeadK = 1.5, TitleX = 88;

    /// <summary>The ground's top: the head and the tail's curl rise over it.</summary>
    const double GroundTop = 18;

    static readonly Dictionary<string, DrawingGroup> CoilCache = new();

    static DrawingGroup Cached(string key, Action<DrawingContext> draw)
    {
        if (CoilCache.TryGetValue(key, out var g)) return g;
        if (CoilCache.Count > 40) CoilCache.Clear();
        g = new DrawingGroup();
        using (var dc = g.Open()) draw(dc);
        g.Freeze();
        CoilCache[key] = g;
        return g;
    }

    /// <summary>
    /// The meter held by the dragon (<paramref name="w"/> x <paramref name="h"/>): the obsidian ground, its great head rising
    /// over the top-left corner, the neck thick down the left side, the body thinning round the bottom and up the right, the
    /// tail curling up over the top-right corner and back on itself.
    /// </summary>
    static DrawingGroup FightCoil(double w, double h, double ground) => Cached(FormattableString.Invariant($"fight|{w:0}|{h:0}|{ground:0.00}"), dc =>
    {
        // the ground inside the coil, a bronze rim along its open top
        var gr = new Rect(13, GroundTop, w - 22, h - GroundTop - 9);
        dc.DrawRoundedRectangle(Mod.Grad(true, (Mod.A(Color.FromRgb(0x17, 0x12, 0x1D), ground), 0), (Mod.A(Color.FromRgb(0x0B, 0x09, 0x0E), ground), .55), (Mod.A(Color.FromRgb(0x07, 0x0A, 0x09), ground), 1)), null, gr, 12, 12);
        dc.DrawLine(Mod.P(Mod.Grad(false, (Mod.A(BronzeC, .9), 0), (Mod.A(Color.FromRgb(0xF2, 0xD4, 0x92), .7), .5), (Mod.A(BronzeC, 0), 1)), 1), new Point(62, GroundTop + .5), new Point(w - 46, GroundTop + .5));
        var (sp, thick) = FightSpine(w, h);
        Body(dc, sp, thick, Sheen(w, h), spikeMax: 9, spikeStep: 52);
        Head(dc, FightHeadX, FightHeadY, FightHeadK);
    });

    /// <summary>The meter's spine and its thickness: the neck from under the head down the left, the bottom, up the right, the tail's curl over the top-right corner.</summary>
    static (Spine Sp, Func<double, double> Thick) FightSpine(double w, double h)
    {
        double x0 = 13, x1 = w - 11, y1 = h - 8, r = 13;
        var spine = G(FormattableString.Invariant(
            $"M 26,49 Q {x0},50 {x0},64 L {x0},{y1 - r} Q {x0},{y1} {x0 + r},{y1} L {x1 - r},{y1} Q {x1},{y1} {x1},{y1 - r} L {x1},48 C {x1},30 {x1 - 4},13 {x1 - 18},9 C {x1 - 32},6 {x1 - 41},15 {x1 - 34},23 C {x1 - 29},28 {x1 - 21},25 {x1 - 23},18"));
        var sp = new Spine(spine);
        double L = sp.Length;
        double left = (20 + (y1 - r - 64)) / L, bottom = left + (22 + (x1 - x0 - 2 * r)) / L, right = bottom + (22 + (y1 - r - 48)) / L;
        double Thick(double s) =>
            s < .03 ? 13 + 3 * s / .03
            : s < left ? 16
            : s < bottom ? 16 - 6 * (s - left) / Math.Max(.01, bottom - left)
            : s < right ? 11 - 4 * (s - bottom) / Math.Max(.01, right - bottom)
            : Math.Max(2.4, 7 - 4.6 * Math.Pow((s - right) / Math.Max(.01, 1 - right), 1.3));
        return (sp, Thick);
    }

    /// <summary>
    /// The dragon ablaze (a moment flashes it): its whole coil and head lit in <paramref name="c"/>, a wide soft light along the
    /// spine and a hot line down its middle, drawn once per size and colour.
    /// </summary>
    static DrawingGroup FightBlaze(double w, double h, Color c) => Cached(FormattableString.Invariant($"blaze|{w:0}|{h:0}|{c}"), dc =>
    {
        var (sp, _) = FightSpine(w, h);
        int n = Math.Max(8, (int)(sp.Length / 3));
        var pts = new List<Point>(n + 1);
        for (int i = 0; i <= n; i++) pts.Add(sp.At(sp.Length * i / n).P);
        var line = Poly(pts);
        dc.DrawGeometry(null, Mod.P(Frozen(Mod.A(c, .16)), 24, true), line);
        dc.DrawGeometry(null, Mod.P(Frozen(Mod.A(c, .3)), 18, true), line);
        dc.DrawGeometry(null, Mod.P(Frozen(Mod.A(Mod.Mix(c, Colors.White, .5), .6)), 4, true), line);
        var head = new Point(FightHeadX + 30 * FightHeadK, FightHeadY + 20 * FightHeadK);
        dc.DrawEllipse(Glow(c, .75), null, new Point(head.X + 4, head.Y), 50, 40);
    });

    // ================================================================ the Buddy's coil

    /// <summary>
    /// The Buddy held by a smaller dragon (<paramref name="w"/> x <paramref name="h"/>): an obsidian plate, a scaled body along
    /// its top from the left to the right end, down round it and back along the foot to its spade. With
    /// <paramref name="head"/> its head at the top-left (no ring there); with the ring, the ring is the dragon.
    /// </summary>
    static DrawingGroup LineCoil(double w, double h, double ground, bool head, bool dim) => Cached(FormattableString.Invariant($"line|{w:0}|{h:0}|{ground:0.00}|{head}|{dim}"), dc =>
    {
        var gr = new Rect(3, 3, w - 7, h - 6);
        var tone = dim ? Color.FromRgb(0x12, 0x12, 0x15) : Color.FromRgb(0x14, 0x10, 0x1A);
        dc.DrawRoundedRectangle(Mod.Grad(false, (Mod.A(tone, ground), 0), (Mod.A(Color.FromRgb(0x0A, 0x08, 0x0D), ground * .96), .6), (Mod.A(Color.FromRgb(0x0A, 0x08, 0x0D), ground * .9), 1)), null, gr, 10, 10);
        double x0 = head ? 34 : 22, x1 = w - 10, y0 = 8.5, y1 = h - 10.5, r = Math.Min(11, (h - 19) / 2);
        double tip = Math.Max(x0 + 30, w * .56);
        var spine = G(FormattableString.Invariant(
            $"M {x0},{y0} L {x1 - r},{y0} Q {x1},{y0} {x1},{y0 + r} L {x1},{y1 - r} Q {x1},{y1} {x1 - r},{y1} L {tip},{y1}"));
        var sp = new Spine(spine);
        double L = sp.Length, turn = (x1 - r - x0 + (h - 13)) / L;
        double Thick(double s) => s < .04 ? 6.5 + 3.5 * s / .04 : s < turn * .6 ? 10 : Math.Max(4.5, 10 - 5.5 * Math.Pow((s - turn * .6) / Math.Max(.01, 1 - turn * .6), 1.1));
        Body(dc, sp, Thick, Sheen(w, h, dim ? .55 : 1), spikes: true, spade: false, glow: dim ? .35 : .8, spikeMax: 3, spikeStep: 58, plateStep: 9.5, twoRowsAt: 99);
        if (head) Head(dc, -1, -2, .78);
    });

    // ================================================================ the ouroboros ring

    /// <summary>The ouroboros's body, drawn once per size: its spine, a circle from the neck under the head round counter-clockwise to the tail's tip in the jaws.</summary>
    static (Spine Sp, Func<double, double> Thick, DrawingGroup Art) OuroborosBody(double size)
    {
        double c = size / 2, R = c - 7;
        Point At(double deg) { double a = deg * Math.PI / 180; return new Point(c + R * Math.Sin(a), c - R * Math.Cos(a)); }
        // counter-clockwise from 340 degrees (behind the head) round to 28 degrees (the jaws)
        var fig = new PathFigure { StartPoint = At(340) };
        fig.Segments.Add(new ArcSegment(At(160), new Size(R, R), 0, false, SweepDirection.Counterclockwise, true));
        fig.Segments.Add(new ArcSegment(At(28), new Size(R, R), 0, false, SweepDirection.Counterclockwise, true));
        var pg = new PathGeometry(); pg.Figures.Add(fig); pg.Freeze();
        var sp = new Spine(pg);
        double Thick(double s) => s < .7 ? 8.4 : Math.Max(2.4, 8.4 - 6 * (s - .7) / .3);
        var key = FormattableString.Invariant($"ouro|{size:0}");
        var art = Cached(key, dc =>
        {
            dc.DrawEllipse(Frozen(Mod.A(ObsidianC, .9)), null, new Point(c, c), R - 3.6, R - 3.6);
            Body(dc, sp, Thick, Sheen(size, size), spikes: true, spade: false, glow: .9, spikeMax: 2.4, spikeStep: 14);
        });
        return (sp, Thick, art);
    }

    /// <summary>The ouroboros's head at the top, facing round the ring, its jaws closing on the tail (drawn over the molten fill).</summary>
    static void OuroborosHead(DrawingContext dc, double size)
    {
        double c = size / 2, R = c - 7, a = 340 * Math.PI / 180, k = .74;
        var neck = new Point(c + R * Math.Sin(a), c - R * Math.Cos(a));
        dc.PushTransform(new MatrixTransform(k, 0, 0, k, neck.X - 12 * k, neck.Y - 27 * k));
        dc.DrawDrawing(HeadArt);
        dc.Pop();
    }

    /// <summary>
    /// The run's ring as a dragon biting its tail: its scaled body round the circle, the part of your best molten gold from the
    /// jaws round clockwise (all of it once beaten), the head at the top closing on the tail.
    /// </summary>
    static void Ouroboros(DrawingContext dc, double size, double share, Color fill)
    {
        var (sp, thick, art) = OuroborosBody(size);
        dc.DrawDrawing(art);
        share = Math.Clamp(share, 0, 1);
        if (share > .005)
        {
            // the molten part: from the tail's tip (the jaws) back along the body, clockwise round the ring
            double L = sp.Length, from = L * (1 - share);
            int n = Math.Max(2, (int)((L - from) / 1.5));
            var pts = new List<Point>(n + 1);
            for (int i = 0; i <= n; i++) pts.Add(sp.At(L - (L - from) * i / n).P);
            var g = Poly(pts);
            dc.DrawGeometry(null, Mod.P(Frozen(Mod.A(fill, .28)), 12, true), g);
            dc.DrawGeometry(null, Mod.P(Frozen(Mod.A(fill, .62)), 7.5, true), g);
            dc.DrawGeometry(null, Mod.P(Frozen(Mod.A(Mod.Mix(fill, Colors.White, .6), .85)), 1.4, true), g);
        }
        OuroborosHead(dc, size);
    }

    // ================================================================ dragon fire

    /// <summary>
    /// A bar as dragon fire in <paramref name="r"/> reaching <paramref name="fx"/>: a class-coloured stream darkening toward
    /// its source, a hot core running along it, and a flame front of three tongues at its end (the leader's rimmed in gold).
    /// </summary>
    static void Fire(DrawingContext dc, Rect r, double fx, Color cls, bool lead, long seed, double heat = 1)
    {
        double len = fx - r.Left;
        if (len < 1) return;
        double h = r.Height, top = r.Top, bot = r.Bottom, mid = top + h / 2;
        double nl = Math.Min(h * 1.15, len);
        var deep = Mod.Mix(cls, ObsidianC, .66);
        var body = new LinearGradientBrush { MappingMode = BrushMappingMode.Absolute, StartPoint = new Point(r.Left, 0), EndPoint = new Point(fx, 0) };
        body.GradientStops.Add(new GradientStop(Mod.A(deep, .92), 0));
        body.GradientStops.Add(new GradientStop(Mod.A(Mod.Mix(cls, ObsidianC, .5), .94), .55));
        body.GradientStops.Add(new GradientStop(Mod.A(Mod.Mix(cls, ObsidianC, .22), .95), .85));
        body.GradientStops.Add(new GradientStop(Mod.A(Mod.Mix(cls, DragonGoldC, .22), .96), 1));
        body.Freeze();
        // the stream up to the front
        if (len > nl) dc.DrawRectangle(body, null, new Rect(r.Left, top, len - nl + .6, h));
        // the front: three pointed tongues, the middle one the longest (a little different per member), a hot flame inside it
        double j = ((seed * 2654435761L) & 0xFF) / 255.0;
        var front = Tongues(fx - nl, fx, top, bot, j);
        // the heat round the front, then the front and its rim (not across its back)
        dc.DrawEllipse(Glow(lead ? DragonGoldC : Mod.Mix(cls, DragonGoldC, .5), (lead ? .5 : .32) * heat), null, new Point(fx - nl * .25, mid), nl * .9, h * .62);
        dc.DrawGeometry(body, null, front);
        dc.DrawGeometry(null, lead ? Mod.P(Frozen(Mod.A(DragonGoldC, .95)), 1) : Mod.P(Frozen(Mod.A(Mod.Mix(cls, DragonGoldC, .55), .7)), .75), Tongues(fx - nl, fx, top, bot, j, false));
        double cl = Math.Min(nl * .8, len * .8);
        var core = Tongues(fx - 1.5 - cl, fx - 1.5, top + h * .2, bot - h * .2, 1 - j);
        var hot = new LinearGradientBrush { MappingMode = BrushMappingMode.Absolute, StartPoint = new Point(fx - 1.5 - cl, 0), EndPoint = new Point(fx, 0) };
        hot.GradientStops.Add(new GradientStop(Mod.A(DragonGoldC, 0), 0));
        hot.GradientStops.Add(new GradientStop(Mod.A(DragonGoldC, .55 * heat), .45));
        hot.GradientStops.Add(new GradientStop(Mod.A(HotC, (lead ? .95 : .8) * heat), 1));
        hot.Freeze();
        dc.DrawGeometry(hot, null, core);
    }

    /// <summary>A flame's front from <paramref name="x0"/> to <paramref name="fx"/>: three pointed tongues licking forward, the middle the longest.</summary>
    static StreamGeometry Tongues(double x0, double fx, double top, double bot, double j, bool closed = true)
    {
        double nl = fx - x0, h = bot - top, mid = top + h / 2;
        double ta = fx - nl * (.2 + .12 * j), tc = fx - nl * (.3 - .1 * j);
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(x0, top), closed, closed);
            c.BezierTo(new Point(x0 + nl * .3, top), new Point(ta - nl * .25, top - h * .02), new Point(ta, top + h * .06), true, true);
            c.QuadraticBezierTo(new Point(fx - nl * .5, top + h * .2), new Point(fx - nl * .62, top + h * .34), true, true);
            c.QuadraticBezierTo(new Point(fx - nl * .2, top + h * .34), new Point(fx, mid + h * .02), true, true);
            c.QuadraticBezierTo(new Point(fx - nl * .2, bot - h * .32), new Point(fx - nl * .62, bot - h * .3), true, true);
            c.QuadraticBezierTo(new Point(fx - nl * .5, bot - h * .18), new Point(tc, bot - h * .05), true, true);
            c.BezierTo(new Point(tc - nl * .25, bot + h * .02), new Point(x0 + nl * .3, bot), new Point(x0, bot), true, true);
        }
        g.Freeze();
        return g;
    }

    /// <summary>The rank's crest: a dragon scale (a shield with a pointed foot), molten gold for the leader, obsidian rimmed in bronze for the rest.</summary>
    static void Crest(DrawingContext dc, Point c, double s, bool lead)
    {
        var g = new StreamGeometry();
        using (var x = g.Open())
        {
            x.BeginFigure(new Point(c.X - s * .5, c.Y - s * .44), true, true);
            x.QuadraticBezierTo(new Point(c.X, c.Y - s * .62), new Point(c.X + s * .5, c.Y - s * .44), true, true);
            x.BezierTo(new Point(c.X + s * .52, c.Y + s * .08), new Point(c.X + s * .26, c.Y + s * .36), new Point(c.X, c.Y + s * .56), true, true);
            x.BezierTo(new Point(c.X - s * .26, c.Y + s * .36), new Point(c.X - s * .52, c.Y + s * .08), new Point(c.X - s * .5, c.Y - s * .44), true, true);
        }
        g.Freeze();
        if (lead)
        {
            dc.DrawEllipse(Glow(DragonGoldC, .55), null, c, s * .95, s * .95);
            dc.DrawGeometry(Mod.Grad(true, (HotC, 0), (DragonGoldC, .45), (Color.FromRgb(0xB0, 0x62, 0x10), 1)), Mod.P(Frozen(Color.FromRgb(0x3A, 0x1E, 0x04)), .8), g);
        }
        else dc.DrawGeometry(Mod.Grad(true, (ObsidianHiC, 0), (ObsidianC, 1)), Mod.P(Frozen(Mod.A(BronzeC, .8)), .8), g);
    }

    /// <summary>The flame a breath throws along a row: a cone of fire from its left, drawn once (a moment scales and fades it).</summary>
    static readonly DrawingBrush Burst = MakeBurst(DragonGoldC, EmberC);

    static DrawingBrush MakeBurst(Color hot, Color ember)
    {
        var g = new DrawingGroup();
        using (var dc = g.Open())
        {
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, 120, 30));
            var fill = Mod.Grad(false, (Mod.A(hot, .8), 0), (Mod.A(ember, .7), .35), (Mod.A(ember, .45), .7), (Mod.A(ember, 0), 1));
            dc.DrawGeometry(fill, null, G("M 0,11 C 30,4 60,1 120,6 C 96,10 104,13 118,15 C 100,18 96,21 120,25 C 60,29 30,26 0,19 Z"));
            dc.DrawGeometry(Mod.Grad(false, (Mod.A(HotC, .6), 0), (Mod.A(HotC, .35), .5), (Mod.A(HotC, 0), 1)), null, G("M 0,13.5 C 30,10 50,11 80,14.5 C 50,18 30,19 0,16.5 Z"));
        }
        g.Freeze();
        return Fz(new DrawingBrush(g) { Stretch = Stretch.Fill });
    }

    /// <summary>The burst's ash-and-coral twin, for a wipe.</summary>
    static readonly DrawingBrush BurstAsh = MakeBurst(Color.FromRgb(0xFF, 0x9A, 0x8A), Color.FromRgb(0xB0, 0x3A, 0x3A));

    /// <summary>A light that runs along a bar (the fire racing down it): a band of hot gold, drawn once.</summary>
    static readonly LinearGradientBrush RaceBrush = Mod.Grad(false, (Mod.A(HotC, 0), 0), (Mod.A(DragonGoldC, .35), .45), (Mod.A(Colors.White, .85), .78), (Mod.A(HotC, 0), 1));

    /// <summary>A shockwave: a soft, thick ring of light, transparent inside (a moment scales and fades it).</summary>
    static RadialGradientBrush Shock(Color c)
    {
        var b = new RadialGradientBrush();
        b.GradientStops.Add(new GradientStop(Mod.A(c, 0), 0));
        b.GradientStops.Add(new GradientStop(Mod.A(c, 0), .62));
        b.GradientStops.Add(new GradientStop(Mod.A(c, .55), .86));
        b.GradientStops.Add(new GradientStop(Mod.A(Mod.Mix(c, Colors.White, .5), .9), .94));
        b.GradientStops.Add(new GradientStop(Mod.A(c, 0), 1));
        b.Freeze();
        return b;
    }

    static readonly RadialGradientBrush ShockHot = Shock(HotC), ShockGold = Shock(DragonGoldC);
    static readonly RadialGradientBrush MoteHot = Glow(HotC, 1), MoteEmber = Glow(EmberC, 1), MoteAsh = Glow(AshC, .85);

    /// <summary>A glint (four points), as on a coin of the hoard: drawn once.</summary>
    static readonly DrawingBrush Glint = MakeGlint();

    static DrawingBrush MakeGlint()
    {
        var g = new DrawingGroup();
        using (var dc = g.Open())
        {
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, 40, 40));
            dc.DrawEllipse(Glow(DragonGoldC, .7), null, new Point(20, 20), 14, 14);
            dc.DrawGeometry(Frozen(Color.FromRgb(0xFF, 0xF6, 0xD8)), null, G("M 20,0 Q 21.4,18.6 40,20 Q 21.4,21.4 20,40 Q 18.6,21.4 0,20 Q 18.6,18.6 20,0 Z"));
            dc.DrawEllipse(Brushes.White, null, new Point(20, 20), 2.4, 2.4);
        }
        g.Freeze();
        return Fz(new DrawingBrush(g) { Stretch = Stretch.Fill });
    }
}
