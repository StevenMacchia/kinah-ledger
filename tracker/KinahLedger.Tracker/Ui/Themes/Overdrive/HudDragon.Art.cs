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

    /// <summary>
    /// The head's box in its own units (it faces right, roaring): 116 x 68, drawn 1:1 on the meter. The eye's and the jaws'
    /// points in it.
    /// </summary>
    const double HeadW = 116, HeadH = 68;
    static readonly Point EyeAt = new(63.2, 26.4), MouthAt = new(103, 41);

    /// <summary>The eye's lid in the head's units (the meter blinks it over the eye), and the line it closes from.</summary>
    internal const string LidPath = "M 53,23.8 L 62,23.2 L 73.2,26.8 C 66.4,30 58.6,29.4 53,23.8 Z";
    const double LidTop = 23.4;

    static Geometry G(string data) { var g = Geometry.Parse(data); g.Freeze(); return g; }

    // the head's own heat: a crimson under the ember, the throat's dark red (before the head: static fields start in order)
    static readonly Color CrimsonC = Color.FromRgb(0xF0, 0x22, 0x12), BloodC = Color.FromRgb(0x5A, 0x06, 0x06);

    /// <summary>The Balaur's head, drawn once (its own units; place it with a transform): the meter's in full, and a bolder, plainer one for the small sizes.</summary>
    static readonly DrawingGroup HeadArt = MakeHead(false), HeadSmallArt = MakeHead(true);

    /// <summary>A tooth from <paramref name="p"/> on a jaw's edge (running <paramref name="u"/>), toward <paramref name="dir"/>: a curved blade, its root inside the jaw.</summary>
    static void Tooth(StreamGeometryContext c, Point p, Vector u, Vector dir, double len, double w, double bend = .18)
    {
        u.Normalize(); dir.Normalize();
        Point a = p - u * w - dir * 1.4, b = p + u * w - dir * 1.4, tip = p + dir * len - u * (len * bend);
        c.BeginFigure(a, true, true);
        c.QuadraticBezierTo(p - u * (w * .35) + dir * (len * .6), tip, true, true);
        c.QuadraticBezierTo(p + u * (w * .55) + dir * (len * .4), b, true, true);
    }

    static DrawingGroup MakeHead(bool small)
    {
        var g = new DrawingGroup();
        using (var dc = g.Open())
        {
            // obsidian with blood in it; the lit planes warmer; a dark edge, a bronze rim (bold on the small head, where it carries the shape)
            var skin = Mod.Grad(true, (Color.FromRgb(0x30, 0x1E, 0x22), 0), (Color.FromRgb(0x17, 0x0D, 0x10), .5), (Color.FromRgb(0x08, 0x04, 0x05), 1));
            var plate = Mod.Grad(true, (Color.FromRgb(0x52, 0x36, 0x34), 0), (Color.FromRgb(0x22, 0x14, 0x16), .55), (Color.FromRgb(0x0C, 0x06, 0x07), 1));
            var edge = Mod.P(Frozen(Color.FromArgb(0xF0, 0x04, 0x02, 0x03)), small ? 2.2 : .9);
            var lit = Mod.P(Frozen(Mod.A(Color.FromRgb(0xFF, 0xD8, 0x96), small ? .9 : .8)), small ? 2 : .8, true);
            var bronze = Mod.P(Frozen(Mod.A(Color.FromRgb(0xC8, 0x8E, 0x4C), .55)), .6, true);
            // bone: pale at the tips, darkening into blood at the roots (the tips point left: the gradient runs tip to root)
            var horn = Mod.Grad(false, (Color.FromRgb(0xFF, 0xF4, 0xDC), 0), (Color.FromRgb(0xD6, 0xC2, 0x9C), .38), (Color.FromRgb(0x7A, 0x56, 0x40), .78), (Color.FromRgb(0x2E, 0x12, 0x10), 1));
            var hornFar = Mod.Grad(false, (Color.FromRgb(0xB0, 0x9C, 0x80), 0), (Color.FromRgb(0x6A, 0x52, 0x40), .5), (Color.FromRgb(0x1E, 0x0C, 0x0C), 1));
            var hornUp = Mod.Grad(true, (Color.FromRgb(0xFF, 0xF4, 0xDC), 0), (Color.FromRgb(0xC8, 0xAE, 0x86), .45), (Color.FromRgb(0x2E, 0x12, 0x10), 1));
            var hornEdge = Mod.P(Frozen(Color.FromArgb(0xE6, 0x0C, 0x05, 0x04)), small ? 1.6 : .7);
            var bone = Mod.Grad(true, (Color.FromRgb(0xFF, 0xFA, 0xEA), 0), (Color.FromRgb(0xE6, 0xD4, 0xB0), .6), (Color.FromRgb(0xA8, 0x86, 0x5C), 1));

            // the roar's light: the furnace in the open jaws thrown out past them
            dc.DrawEllipse(Fz(new RadialGradientBrush(Mod.A(DragonGoldC, .8), Mod.A(CrimsonC, 0)) { GradientStops = { new GradientStop(Mod.A(EmberC, .5), .5) } }), null, new Point(96, 41), small ? 28 : 24, small ? 22 : 18);

            // a warm halo round the head's mass, so its dark reads over the obsidian ground
            var halo = Mod.P(Frozen(Mod.A(Color.FromRgb(0xE8, 0xA0, 0x58), small ? .3 : .26)), small ? 4.4 : 2.6, true);
            dc.DrawGeometry(null, halo, G("M 28,24 L 40,15 L 56,11 L 68,14 C 80,15 92,15 102,13 L 109,14.6 L 113.4,18.6 L 111.6,24 L 106.6,26.8 M 100,55.6 L 105.4,53 L 104,58.8 L 96,62.2 L 76,62.8 L 58,58.8 L 42,52 M 44,45 L 34,44 L 27,36 L 28,24"));

            // the horns, swept back off the skull: a far one in shadow, the great near one rising to the corner
            dc.DrawGeometry(hornFar, hornEdge, G("M 44,16 C 32,10 18,8.6 1.4,11.4 C 14,14.4 24,19 32,26.4 Z"));
            dc.DrawGeometry(horn, hornEdge, G("M 55,12 C 43,3.4 24,0.4 3.4,0.8 C 18,3.8 28,8.6 34,15.6 C 37,19 39,21.4 40.4,24.6 Z"));
            if (!small)
                foreach (var d in new[] { "M 13.6,2.4 Q 15.6,5.4 18.6,7.2", "M 23.6,3.4 Q 25,7.6 28.4,10.4", "M 33.4,6 Q 34.6,10.4 38,13.6", "M 12,11.8 Q 13.6,14.6 16.6,15.6", "M 22,12.4 Q 23.6,16 26.4,18" })
                    dc.DrawGeometry(null, Mod.P(Frozen(Mod.A(Color.FromRgb(0x3A, 0x22, 0x18), .75)), .7, true), G(d));
            // the frill: long blades fanned back off the cheek, over the neck
            dc.DrawGeometry(plate, small ? edge : Mod.P(Frozen(Color.FromArgb(0xF0, 0x04, 0x02, 0x03)), .8), small
                ? G("M 33,29 L 4,26 L 29,37 Z M 32,41 L 10,54 L 40,48 Z")
                : G("M 33,29 L 1.6,25.4 L 29,36.6 Z M 30,36 L 4,42.4 L 30.4,43.4 Z M 33,42.4 L 12.4,56 L 39,48 Z M 39,47.6 L 27.4,62 L 46.6,52.4 Z"));
            if (!small) dc.DrawGeometry(null, bronze, G("M 33,29 L 1.6,25.4 M 30,36 L 4,42.4 M 33,42.4 L 12.4,56 M 39,47.6 L 27.4,62"));

            // inside the jaws: the throat's dark and the fire coming up it, bursting out past the fangs
            dc.DrawGeometry(Mod.Grad(false, (Color.FromRgb(0x1A, 0x02, 0x02), 0), (BloodC, .28), (Color.FromRgb(0xB4, 0x1E, 0x0A), .7), (EmberC, 1)), null, G("M 48,41.6 L 108,25.6 L 104,57 L 54,44.6 Z"));
            dc.DrawGeometry(Mod.Grad(false, (Mod.A(CrimsonC, 0), 0), (Mod.A(EmberC, .85), .3), (DragonGoldC, .66), (HotC, 1)), null,
                G("M 60,41 C 74,36 92,31 108.4,29.4 C 104.4,33.4 107.4,36 114.6,37 C 106.6,40 106.6,43 112.6,47 C 104.6,48 105,50.4 108.6,54.4 C 92,51 76,46.6 60,43.4 Z"));
            dc.DrawGeometry(Mod.Grad(false, (Mod.A(HotC, 0), 0), (Mod.A(HotC, .85), .5), (Mod.A(Colors.White, .95), 1)), null,
                G("M 72,41.6 C 84,39 94,37.6 106,37.4 C 102.4,40 103.4,42.4 108,44.4 C 96,45 84,44 72,42.8 Z"));

            // the teeth: long curved fangs and a jagged row between, set before the jaws so their roots sit inside
            var teeth = new StreamGeometry();
            using (var c = teeth.Open())
            {
                // upper, hanging from the lip line (106.6,26.8) to (55,40.4)
                var lu = new Vector(-51.6, 13.6); var down = new Vector(-.1, 1);
                Point Lip(double t) => new(106.6 - 51.6 * t, 26.8 + 13.6 * t);
                var up = small ? new[] { (.05, 16.0, 3.8), (.42, 12.5, 3.4) } : new[] { (.04, 14.4, 2.8), (.16, 5.0, 1.7), (.27, 7.6, 2.0), (.40, 11.8, 2.5), (.53, 5.4, 1.7), (.66, 6.8, 1.8), (.80, 4.4, 1.5) };
                foreach (var (t, len, w) in up) Tooth(c, Lip(t), lu, down, len, w);
                // lower, rising from the gum line (56,44) to (100,55.6)
                var gu = new Vector(44, 11.6); var rise = new Vector(.12, -1);
                Point Gum(double t) => new(56 + 44 * t, 44 + 11.6 * t);
                var lo = small ? new[] { (.9, 13.0, 3.6) } : new[] { (.92, 11.6, 2.6), (.78, 5.0, 1.7), (.62, 7.8, 2.0), (.46, 4.4, 1.6), (.3, 5.4, 1.7), (.15, 3.6, 1.5) };
                foreach (var (t, len, w) in lo) Tooth(c, Gum(t), gu, rise, len, w, -.14);
            }
            teeth.Freeze();
            dc.DrawGeometry(bone, Mod.P(Frozen(Color.FromArgb(0xE0, 0x1A, 0x08, 0x04)), small ? 1.4 : .55), teeth);

            // the lower jaw, thrown wide open, hooked at the chin, spurs raked back under it
            dc.DrawGeometry(plate, small ? edge : Mod.P(Frozen(Color.FromArgb(0xF0, 0x04, 0x02, 0x03)), .7), G("M 64,59.6 L 55,65.4 L 70.6,61.4 Z M 83,62 L 75,67 L 89.4,61.8 Z"));
            dc.DrawGeometry(skin, edge, G("M 36,43 L 54,43.4 L 76,49.6 L 100,55.6 L 105.4,53 L 104,58.8 L 96,62.2 L 76,62.8 L 58,58.8 L 42,52 Z"));
            if (!small) dc.DrawGeometry(Frozen(Mod.A(Colors.Black, .35)), null, G("M 58,53 L 99,58.4 L 96,62.2 L 76,62.8 L 58,58.8 Z"));
            // the fire's light along the gums and the lip
            dc.DrawGeometry(null, Mod.P(Frozen(Mod.A(EmberC, .9)), small ? 1.6 : .8, true), G("M 55,44.4 L 76,50.4 L 100,56.4"));

            // the skull and upper jaw: a long angular wedge, the crest high behind the brow, the snout hooked over the fangs
            dc.DrawGeometry(skin, edge, G("M 28,24 L 40,15 L 56,11 L 68,14 C 80,15 92,15 102,13 L 109,14.6 L 113.4,18.6 L 111.6,24 L 106.6,26.8 L 90,30 L 72,35 L 55,40.4 L 44,45 L 34,44 L 27,36 Z"));
            if (!small)
            {
                // the lit planes: the snout's top, the skull's crown, the cheek; a shadow under the brow
                var warm = Frozen(Mod.A(Color.FromRgb(0x9A, 0x66, 0x52), .38));
                dc.DrawGeometry(warm, null, G("M 68,14 C 80,15 92,15 102,13 L 109,14.6 L 104,18.6 L 88,20 L 74,21.4 Z M 40,15 L 56,11 L 58,17.4 L 44,21.6 Z"));
                dc.DrawGeometry(Frozen(Mod.A(Color.FromRgb(0x7A, 0x4C, 0x40), .26)), null, G("M 29.6,29 L 46,24.6 L 53,31 L 36,36 Z"));
                // the cheek's ridges: two blades raked back along the jaw's hinge
                dc.DrawGeometry(plate, Mod.P(Frozen(Color.FromArgb(0xF0, 0x04, 0x02, 0x03)), .7), G("M 52,33.4 L 30.4,34.6 L 46,37.6 Z M 50,39.4 L 31.4,42.6 L 45.4,43.4 Z"));
                dc.DrawGeometry(null, bronze, G("M 52,33.4 L 30.4,34.6 M 50,39.4 L 31.4,42.6"));
                // seams between the plates, glowing like a forge's cracks
                var seams = G("M 79,15.4 L 81,20.6 M 92.6,15 L 94,19.8 M 45,21.6 L 49,28 M 68,57.4 L 71,62.4 M 86,59.6 L 88,62.4 M 38,34.6 L 43,33.4");
                dc.DrawGeometry(null, Mod.P(Frozen(Mod.A(CrimsonC, .4)), 2.2, true), seams);
                dc.DrawGeometry(null, Mod.P(Frozen(Color.FromRgb(0x05, 0x02, 0x03)), .9, true), seams);
                dc.DrawGeometry(null, Mod.P(Frozen(Mod.A(EmberC, .95)), .4, true), seams);
                // light catching the top edges
                dc.DrawGeometry(null, lit, G("M 41,15.4 L 56,11.6 L 67,14.6 C 80,15.6 92,15.6 102,13.6 L 108.6,15.2 L 112.6,18.8"));
                dc.DrawGeometry(null, bronze, G("M 28.6,24.4 L 40,15.6 M 113,19 L 111.2,23.8 L 106.6,26.6"));
            }
            if (small) dc.DrawGeometry(null, lit, G("M 40,15.4 L 56,11.6 L 67,14.6 C 80,15.6 92,15.6 102,13.6 L 108.6,15.2"));
            dc.DrawGeometry(null, Mod.P(Frozen(Mod.A(EmberC, .9)), small ? 1.6 : .8, true), G("M 106.4,27.4 L 90,30.6 L 72,35.6 L 56,40.8"));

            // the horns on the snout, raked forward
            dc.DrawGeometry(hornUp, hornEdge, G("M 96.6,14.2 C 99,9.6 101,6.6 104.8,3.4 C 104.6,7.2 104.2,10.6 103.6,13.6 Z"));
            if (!small) dc.DrawGeometry(hornUp, hornEdge, G("M 86.6,14.8 L 89.4,9.4 L 92,14.6 Z"));

            // the scowl: a brow blade jutting forward over a deep socket, its spikes raked back
            dc.DrawGeometry(hornUp, hornEdge, G("M 51,12.4 C 49.4,7.8 47,4.4 43,1.4 C 50,2.8 55.4,6.4 58.4,11.4 Z"));
            if (!small) dc.DrawGeometry(hornUp, hornEdge, G("M 61.4,13.2 C 61,9.8 60,7.2 58,4.6 C 62.6,6 65.6,9.2 66.8,13.8 Z"));
            dc.DrawEllipse(Frozen(Mod.A(Colors.Black, .7)), null, new Point(63, 26.6), small ? 13 : 11.6, small ? 6 : 4.6);
            dc.DrawGeometry(plate, edge, G("M 44,17.6 L 58,13.4 L 82,19.4 L 77,22.2 L 70,23.4 L 61,22.8 L 51,24.6 Z"));
            dc.DrawGeometry(null, lit, G("M 45,17.6 L 58,13.8 L 81,19.4"));

            // the nostril, smouldering
            dc.DrawGeometry(Frozen(Color.FromRgb(0x05, 0x02, 0x02)), Mod.P(Frozen(Mod.A(EmberC, .85)), small ? 1.2 : .5), G("M 102.6,16.4 L 109,17.2 L 106,19.6 Z"));

            // the eye: a slanted slit deep under the brow, blood-red with a white-hot slit, a heat glow round it
            dc.DrawEllipse(Fz(new RadialGradientBrush(Mod.A(CrimsonC, .95), Mod.A(CrimsonC, 0)) { GradientStops = { new GradientStop(Mod.A(CrimsonC, .55), .45) } }), null, EyeAt, small ? 24 : 15.5, small ? 15 : 9.4);
            dc.DrawEllipse(Glow(EmberC, .85), null, EyeAt, small ? 10 : 6.4, small ? 6 : 3.6);
            dc.DrawGeometry(Fz(new RadialGradientBrush(new GradientStopCollection { new GradientStop(Color.FromRgb(0xFF, 0xD0, 0x60), 0), new GradientStop(Color.FromRgb(0xFF, 0x4A, 0x18), .3), new GradientStop(CrimsonC, .62), new GradientStop(BloodC, 1) }) { GradientOrigin = new Point(.55, .5), Center = new Point(.55, .5) }),
                Mod.P(Frozen(Color.FromRgb(0x08, 0x02, 0x02)), small ? 1.4 : .6), G("M 53.6,24.4 L 62,23.8 L 72.6,27 C 66,29.6 58.8,29 53.6,24.4 Z"));
            dc.PushTransform(new RotateTransform(-10, EyeAt.X, EyeAt.Y));
            dc.DrawEllipse(Glow(Color.FromRgb(0xFF, 0xF0, 0xB0), 1), null, EyeAt, small ? 2.8 : 2, small ? 4.6 : 3.4);
            dc.DrawEllipse(Frozen(Color.FromRgb(0xFF, 0xFC, 0xE8)), null, EyeAt, small ? 1.1 : .55, small ? 3 : 2.3);
            dc.Pop();

            if (!small)
            {
                // smoke curling off the nostril, embers flung out of the jaws
                var smoke = Mod.Grad(true, (Mod.A(Color.FromRgb(0x9A, 0x8C, 0x96), 0), 0), (Mod.A(Color.FromRgb(0x9A, 0x8C, 0x96), .55), .55), (Mod.A(Color.FromRgb(0x8A, 0x52, 0x40), .7), 1));
                dc.DrawGeometry(null, Mod.P(smoke, 1.7, true), G("M 107,15.6 C 111,12.6 107.4,9.4 111,6.4 C 114,4 112.6,2 115.6,0.6"));
                dc.DrawGeometry(null, Mod.P(smoke, 1.1, true), G("M 109.6,17.4 C 113,15.6 112.6,12.6 115.6,10.6"));
                foreach (var (x, y, r) in new[] { (112.4, 32.6, 1.4), (115, 42.6, 1.1), (110.6, 52.6, 1.2), (115.2, 26, .8), (107, 60.4, .8) })
                {
                    dc.DrawEllipse(Glow(EmberC, .9), null, new Point(x, y), r * 2.6, r * 2.6);
                    dc.DrawEllipse(Frozen(HotC), null, new Point(x, y), r * .55, r * .55);
                }
            }
        }
        g.Freeze();
        return g;
    }

    /// <summary>The head at (<paramref name="x"/>, <paramref name="y"/>), <paramref name="k"/> times its own size (the plainer one under half size).</summary>
    static void Head(DrawingContext dc, double x, double y, double k)
    {
        dc.PushTransform(new MatrixTransform(k, 0, 0, k, x, y));
        dc.DrawDrawing(k < .6 ? HeadSmallArt : HeadArt);
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

        // the dorsal spikes: hooked bone blades raked toward the tail, a big one and a small one in turn, dark at the root
        if (spikes)
        {
            var sg = new StreamGeometry(); var roots = new StreamGeometry();
            int k = 0;
            using (var c = sg.Open())
            using (var rc = roots.Open())
                for (double d = 10; d < L - 12; d += spikeStep * (.75 + .5 * ((k * 0.6180339887) % 1)), k++)
                {
                    double w = thick(d / L) / 2;
                    if (w < 2.6) continue;
                    var (p, t) = sp.At(d);
                    var nv = new Vector(-t.Y, t.X);
                    bool big = k % 2 == 0;
                    double len = Math.Min(Math.Min(spikeMax, w * 1.15), 14 - w) * (big ? 1 : .58), bw = Math.Min(big ? 4.4 : 3, w * .62);
                    Point b0 = p + nv * (w * .5) - t * bw, tip = p + nv * (w + len) + t * bw * 1.7, b1 = p + nv * (w * .5) + t * bw * .9;
                    c.BeginFigure(b0, true, true);
                    c.QuadraticBezierTo(p + nv * (w + len * .55) - t * bw * .35, tip, true, true);
                    c.QuadraticBezierTo(p + nv * (w + len * .32) + t * bw * .75, b1, true, true);
                    rc.BeginFigure(b0, true, true);
                    rc.LineTo(p + nv * (w + len * .3) - t * bw * .25, true, true);
                    rc.LineTo(p + nv * (w + len * .18) + t * bw * .7, true, true);
                    rc.LineTo(b1, true, true);
                }
            sg.Freeze(); roots.Freeze();
            dc.DrawGeometry(Frozen(Color.FromRgb(0xE4, 0xD2, 0xAC)), Mod.P(Frozen(Color.FromArgb(0xE6, 0x06, 0x04, 0x05)), .6), sg);
            dc.DrawGeometry(Frozen(Color.FromRgb(0x3E, 0x1E, 0x1C)), null, roots);
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
    static readonly LinearGradientBrush ScaleFill = Mod.Grad(true, (Color.FromRgb(0x40, 0x2C, 0x34), 0), (Color.FromRgb(0x22, 0x16, 0x1C), 1));

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
    const double FightHeadX = 0, FightHeadY = 0, FightHeadK = 1, TitleX = 118;

    /// <summary>The rows start under the head's jaws: the words over them (title, timers, HP) keep at least this much room.</summary>
    const double HeadFloor = 68;

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
        dc.DrawLine(Mod.P(Mod.Grad(false, (Mod.A(BronzeC, .9), 0), (Mod.A(Color.FromRgb(0xF2, 0xD4, 0x92), .7), .5), (Mod.A(BronzeC, 0), 1)), 1), new Point(TitleX - 8, GroundTop + .5), new Point(w - 46, GroundTop + .5));
        var (sp, thick) = FightSpine(w, h);
        Body(dc, sp, thick, Sheen(w, h), spikeMax: 9, spikeStep: 24);
        // its forefoot gripping the frame down the left side, the talons curled in over the ground's edge (short of the ranks)
        Claw(dc, new Point(13, Math.Max(HeadFloor + 26, h * .56)), false);
        Head(dc, FightHeadX, FightHeadY, FightHeadK);
    });

    /// <summary>The meter's spine and its thickness: the neck from under the head down the left, the bottom, up the right, the tail's curl over the top-right corner.</summary>
    static (Spine Sp, Func<double, double> Thick) FightSpine(double w, double h)
    {
        double x0 = 13, x1 = w - 11, y1 = h - 8, r = 13;
        var spine = G(FormattableString.Invariant(
            $"M 36,46 Q {x0},49 {x0},70 L {x0},{y1 - r} Q {x0},{y1} {x0 + r},{y1} L {x1 - r},{y1} Q {x1},{y1} {x1},{y1 - r} L {x1},48 C {x1},30 {x1 - 4},13 {x1 - 18},9 C {x1 - 32},6 {x1 - 41},15 {x1 - 34},23 C {x1 - 29},28 {x1 - 21},25 {x1 - 23},18"));
        var sp = new Spine(spine);
        double L = sp.Length;
        double left = (30 + (y1 - r - 70)) / L, bottom = left + (22 + (x1 - x0 - 2 * r)) / L, right = bottom + (22 + (y1 - r - 48)) / L;
        double Thick(double s) =>
            s < .06 ? 21 - 5 * s / .06
            : s < left ? 16
            : s < bottom ? 16 - 6 * (s - left) / Math.Max(.01, bottom - left)
            : s < right ? 11 - 4 * (s - bottom) / Math.Max(.01, right - bottom)
            : Math.Max(2.4, 7 - 4.6 * Math.Pow((s - right) / Math.Max(.01, 1 - right), 1.3));
        return (sp, Thick);
    }

    /// <summary>A forefoot on the coil at <paramref name="at"/>, three hooked talons curled in over the ground's edge (to the left when <paramref name="mirror"/>).</summary>
    static void Claw(DrawingContext dc, Point at, bool mirror)
    {
        const double k = .8;
        dc.PushTransform(new MatrixTransform(mirror ? -k : k, 0, 0, k, at.X, at.Y));
        dc.DrawGeometry(ScaleFill, Mod.P(Mod.Grad(false, (Color.FromRgb(0x7A, 0x5A, 0x34), 0), (Color.FromRgb(0xF2, 0xD0, 0x8A), .6), (Color.FromRgb(0x8A, 0x5E, 0x30), 1)), .9),
            G("M -6,-8 C 1,-10.4 9,-6.4 10.4,-0.6 C 11,4.6 6.4,9.4 0,9.4 C -5.6,8.6 -8,3 -6,-8 Z"));
        dc.DrawGeometry(null, Mod.P(Frozen(Mod.A(Color.FromRgb(0xD8, 0xB4, 0x72), .6)), .6, true), G("M -2,-4 L 1,-6.4 L 4,-4 M 0,2 L 3,-0.4 L 6,2"));
        dc.DrawGeometry(Mod.Grad(false, (Color.FromRgb(0x3E, 0x1E, 0x1C), 0), (Color.FromRgb(0xD8, 0xC6, 0xA0), .55), (Color.FromRgb(0xFF, 0xF6, 0xE0), 1)), Mod.P(Frozen(Color.FromArgb(0xE6, 0x08, 0x04, 0x04)), .6),
            G("M 7,-6.4 C 12.4,-6.8 16.2,-3.6 17,1.8 C 14.6,-1.2 12.2,-2.2 8.4,-2 Z M 8.8,-1 C 13.8,-0.2 16.8,3.4 16.8,8.8 C 14.8,5.8 12.4,4.4 8.4,3.8 Z M 6,4.4 C 10.2,5.8 12.6,9.4 12,14 C 10.4,11.2 8.4,9.8 4.6,8.8 Z"));
        dc.Pop();
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
        var head = new Point(FightHeadX + 60 * FightHeadK, FightHeadY + 38 * FightHeadK);
        dc.DrawEllipse(Glow(c, .75), null, head, 66, 50);
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
        if (head) Head(dc, 0, -1, .46);
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
        double c = size / 2, R = c - 7, a = 340 * Math.PI / 180, k = .42;
        var neck = new Point(c + R * Math.Sin(a), c - R * Math.Cos(a));
        Head(dc, neck.X - 30 * k, neck.Y - 46 * k, k);
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
