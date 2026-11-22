using System.Windows;
using System.Windows.Media;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

/// <summary>
/// Elyos's signature art, every piece drawn once and frozen (it costs nothing per frame; the moments only move, scale and
/// fade the elements that show it): the leader's great wings, the winged crest on the plates' top edge, the god-rays and
/// the drifting feathers of light of a new lead, the radiant burst of a kill, the beam of a fight's start. Each is made on first
/// use: the colours they take live in HudElyos.cs, whose static fields are set after this file's.
/// </summary>
sealed partial class HudElyos
{
    static Drawing? _wingL, _wingR, _crest, _godRays, _burst, _lightFeather;
    static LinearGradientBrush? _beam, _veil;

    /// <summary>A feather: a slim leaf from <paramref name="b"/> along <paramref name="d"/> (a unit vector), <paramref name="l"/> long,
    /// <paramref name="w"/> wide, its belly to the <paramref name="side"/> of its line.</summary>
    static Geometry FeatherAt(Point b, Vector d, double l, double w, double side = 1)
    {
        var n = new Vector(-d.Y, d.X) * side;
        var tip = b + d * l;
        var g = new StreamGeometry();
        using (var x = g.Open())
        {
            x.BeginFigure(b, true, true);
            x.QuadraticBezierTo(b + d * (l * .5) + n * w, tip, true, false);
            x.QuadraticBezierTo(b + d * (l * .55) - n * (w * .45), b, true, false);
        }
        g.Freeze();
        return g;
    }

    static Vector Dir(double deg) => new(Math.Cos(deg * Math.PI / 180), Math.Sin(deg * Math.PI / 180));

    static T Fz<T>(T f) where T : Freezable { f.Freeze(); return f; }

    /// <summary>Gold light from a point: ivory at its heart through gold leaf to deep gold at <paramref name="r"/> px (drawing space).</summary>
    static RadialGradientBrush GoldFrom(double r, double a = 1) => Fz(new RadialGradientBrush
    {
        MappingMode = BrushMappingMode.Absolute, Center = new Point(0, 0), GradientOrigin = new Point(0, 0), RadiusX = r, RadiusY = r,
        GradientStops =
        {
            new GradientStop(Mod.A(Color.FromRgb(0xFF, 0xFB, 0xEC), a), 0), new GradientStop(Mod.A(GoldHiC, a), .35),
            new GradientStop(Mod.A(GoldMidC, a), .72), new GradientStop(Mod.A(GoldLoC, a), 1),
        },
    });

    /// <summary>
    /// The leader's great wing (the left one; the right is its mirror), tall and swept up as the Elyos wear them, from its root
    /// at (0, 0): an arm rising and curving out to the wrist, eleven ivory primaries hanging from it (down and out at the root,
    /// up and out at the tip, longer towards the tip) edged in gold, two rows of coverts along the arm, a glow of ivory light
    /// round every feather with a touch of sky in it. Drawn once about 44 px wide and 124 px tall; the meter scales it to the
    /// room from its row to the plate's top (<see cref="WingBox"/>).
    /// </summary>
    static Drawing WingL => _wingL ??= MakeWing();
    static Drawing WingR => _wingR ??= Fz(new DrawingGroup { Children = { WingL }, Transform = Fz(new ScaleTransform(-1, 1)) });
    /// <summary>The wing's bounds as drawn (its root at (0, 0)).</summary>
    static Rect WingBox => WingL.Bounds;

    static Drawing MakeWing()
    {
        var g = new DrawingGroup();
        Point Arm(double t)
        {
            Point r = new(0, 0), c = new(-4, -48), w = new(-24, -96);
            double u = 1 - t;
            return new Point(u * u * r.X + 2 * u * t * c.X + t * t * w.X, u * u * r.Y + 2 * u * t * c.Y + t * t * w.Y);
        }
        var glowSky = Fz(new Pen(Frozen(Mod.A(Color.FromRgb(0xBF, 0xE3, 0xFF), .22)), 9) { LineJoin = PenLineJoin.Round });
        var glow = Fz(new Pen(Frozen(Mod.A(Color.FromRgb(0xFF, 0xF6, 0xD8), .35)), 5) { LineJoin = PenLineJoin.Round });
        var edge = Fz(new Pen(Frozen(Color.FromRgb(0xE8, 0xC4, 0x6A)), .9) { LineJoin = PenLineJoin.Round });
        var pearlEdge = Fz(new Pen(Frozen(Mod.A(Color.FromRgb(0xC9, 0xBE, 0xA6), .9)), .7) { LineJoin = PenLineJoin.Round });
        var pearl = Fz(new LinearGradientBrush(Color.FromRgb(0xFF, 0xFE, 0xF9), Color.FromRgb(0xE6, 0xDE, 0xCC), 90));
        var ivory = Fz(new RadialGradientBrush
        {
            MappingMode = BrushMappingMode.Absolute, Center = new Point(-8, -40), GradientOrigin = new Point(-8, -40), RadiusX = 90, RadiusY = 90,
            GradientStops = { new GradientStop(Color.FromRgb(0xFF, 0xFD, 0xF6), 0), new GradientStop(Color.FromRgb(0xF4, 0xF0, 0xE6), .5), new GradientStop(Color.FromRgb(0xE8, 0xD6, 0xA8), 1) },
        });
        var primaries = new List<Geometry>();
        for (int i = 0; i < 11; i++)
        {
            double k = i / 10.0, t = .08 + .92 * k;
            primaries.Add(FeatherAt(Arm(t), Dir(150 + 85 * k), 11 + 19 * k, 4.5 + 2 * k, -1));
        }
        foreach (var f in primaries) g.Children.Add(new GeometryDrawing(null, glowSky, f));
        foreach (var f in primaries) g.Children.Add(new GeometryDrawing(null, glow, f));
        for (int i = primaries.Count - 1; i >= 0; i--) g.Children.Add(new GeometryDrawing(ivory, edge, primaries[i]));
        // the secondaries: a middle tier of pearl feathers over the primaries' roots
        for (int i = 0; i < 9; i++)
        {
            double k = i / 8.0, t = .05 + .85 * k;
            g.Children.Add(new GeometryDrawing(pearl, pearlEdge, FeatherAt(Arm(t), Dir(140 + 85 * k), 10 + 11 * k, 4.4, -1)));
        }
        // the coverts: short pearl feathers along the arm
        for (int i = 0; i < 8; i++)
        {
            double k = i / 7.0, t = .03 + .9 * k;
            g.Children.Add(new GeometryDrawing(pearl, pearlEdge, FeatherAt(Arm(t), Dir(125 + 80 * k), 6 + 4 * k, 3.4, -1)));
        }
        var arm = new StreamGeometry();
        using (var x = arm.Open())
        {
            x.BeginFigure(Arm(0), false, false);
            for (int n = 1; n <= 16; n++) x.LineTo(Arm(n / 16.0), true, true);
        }
        arm.Freeze();
        g.Children.Add(new GeometryDrawing(null, Fz(new Pen(GoldFill, 1.8) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }), arm));
        g.Children.Add(new GeometryDrawing(GoldFill, Fz(new Pen(Frozen(Mod.A(AbyssC, .6)), .7)), Fz(new EllipseGeometry(new Point(0, 0), 3, 3))));
        return Fz(g);
    }

    /// <summary>
    /// The winged crest on a plate's top edge, centred at (0, 0) on the edge: a gold sun (a disc in a diamond) with three
    /// feathers each side lying along the edge and lifting at their tips, rising at most 7 px over it.
    /// </summary>
    static Drawing CrestWings => _crest ??= MakeCrest();

    static Drawing MakeCrest()
    {
        // a winged sun about 58 by 22: its disc and rays rise 9 px over the edge, its wings lie along it lifting at the tips
        var g = new DrawingGroup();
        var edge = Fz(new Pen(Frozen(Mod.A(AbyssC, .6)), .6));
        var glow = Fz(new Pen(Frozen(Mod.A(Color.FromRgb(0xFF, 0xF6, 0xD8), .3)), 3.5) { LineJoin = PenLineJoin.Round });
        var fill = GoldFrom(30);
        var feathers = new List<Geometry>();
        for (int side = -1; side <= 1; side += 2)
            for (int i = 0; i < 4; i++)
            {
                double deg = side < 0 ? 180 + 4 + i * 9 : -4 - i * 9;
                feathers.Add(FeatherAt(new Point(side * 5, 1.5 - i * .5), Dir(deg), 25 - i * 5, 3 - i * .35, side < 0 ? -1 : 1));
            }
        foreach (var f in feathers) g.Children.Add(new GeometryDrawing(null, glow, f));
        foreach (var f in feathers) g.Children.Add(new GeometryDrawing(fill, edge, f));
        // the sun: eight short rays, a disc of ivory light in a gold ring
        var ray = Fz(new Pen(GoldFill, 1.6) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round });
        for (int k = 0; k < 8; k++)
        {
            var v = Dir(-90 + k * 45);
            g.Children.Add(new GeometryDrawing(null, ray, Fz(new LineGeometry(new Point(0, -4) + v * 10, new Point(0, -4) + v * (k % 2 == 0 ? 15 : 13)))));
        }
        g.Children.Add(new GeometryDrawing(Fz(new RadialGradientBrush(Mod.A(Color.FromRgb(0xFF, 0xF6, 0xD8), .55), Mod.A(Color.FromRgb(0xFF, 0xF6, 0xD8), 0))), null, Fz(new EllipseGeometry(new Point(0, -4), 15, 15))));
        g.Children.Add(new GeometryDrawing(GoldFill, edge, Fz(new EllipseGeometry(new Point(0, -4), 8, 8))));
        g.Children.Add(new GeometryDrawing(Frozen(Color.FromRgb(0xFF, 0xFB, 0xEC)), null, Fz(new EllipseGeometry(new Point(0, -4), 4.4, 4.4))));
        return Fz(g);
    }

    /// <summary>God-rays: nine wedges of warm light fanning up from (0, 0), fading out by 58 px.</summary>
    static Drawing GodRays => _godRays ??= MakeRays(9, 200, 340, 58, 4.5, upOnly: true);

    /// <summary>A kill's burst: sixteen rays all round (0, 0), long and short in turn, gold-white fading by 120 px.</summary>
    static Drawing BurstRays => _burst ??= MakeRays(16, 0, 360, 120, 13, upOnly: false);

    static Drawing MakeRays(int n, double from, double to, double len, double half, bool upOnly)
    {
        var g = new DrawingGroup();
        var b = Fz(new RadialGradientBrush
        {
            MappingMode = BrushMappingMode.Absolute, Center = new Point(0, 0), GradientOrigin = new Point(0, 0), RadiusX = len, RadiusY = len,
            GradientStops = { new GradientStop(Mod.A(Colors.White, .9), 0), new GradientStop(Mod.A(WarmC, .6), .3), new GradientStop(Mod.A(GoldHiC, .25), .65), new GradientStop(Mod.A(GoldHiC, 0), 1) },
        });
        for (int i = 0; i < n; i++)
        {
            double deg = upOnly ? from + (to - from) * i / (n - 1) : from + (to - from) * i / n;
            double l = len * (i % 2 == 0 ? 1 : .68), h = half * (i % 2 == 0 ? 1 : .7);
            var d = Dir(deg); var nn = new Vector(-d.Y, d.X);
            var s = new StreamGeometry();
            using (var x = s.Open()) { x.BeginFigure(new Point(0, 0), true, true); x.LineTo((Point)(d * l + nn * h), true, false); x.LineTo((Point)(d * l - nn * h), true, false); }
            s.Freeze();
            g.Children.Add(new GeometryDrawing(b, null, s));
        }
        return Fz(g);
    }

    /// <summary>A feather of light that drifts off the wings: a small ivory-gold feather in a soft glow, centred on (0, 0).</summary>
    static Drawing LightFeather => _lightFeather ??= MakeLightFeather();

    static Drawing MakeLightFeather()
    {
        var f = FeatherAt(new Point(-5, 0), new Vector(1, 0), 10, 2.2);
        var g = new DrawingGroup();
        g.Children.Add(new GeometryDrawing(null, Fz(new Pen(Frozen(Mod.A(WarmC, .35)), 3.5) { LineJoin = PenLineJoin.Round }), f));
        g.Children.Add(new GeometryDrawing(Fz(new LinearGradientBrush(Color.FromRgb(0xFF, 0xFB, 0xEC), GoldHiC, 0)), null, f));
        return Fz(g);
    }

    /// <summary>The beam of a fight's start: a column of light, white at its heart, warm at its sides, gone at its edges.</summary>
    static LinearGradientBrush BeamBrush => _beam ??= Lin(new Point(0, 0), new Point(1, 0),
        (Mod.A(WarmC, 0), 0), (Mod.A(WarmC, .3), .4), (Colors.White, .49), (Colors.White, .51), (Mod.A(WarmC, .3), .6), (Mod.A(WarmC, 0), 1));
    static LinearGradientBrush? _beamHalo, _beamFade;
    /// <summary>The beam's fall-off down its length (a mask): soft at its top, fading out before the plate's foot, never a hard end.</summary>
    static LinearGradientBrush BeamFade => _beamFade ??= Lin(new Point(0, 0), new Point(0, 1),
        (Mod.A(Colors.Black, .2), 0), (Colors.Black, .12), (Mod.A(Colors.Black, .7), .55), (Mod.A(Colors.Black, 0), 1));
    /// <summary>The beam's halo: a wide soft column of sky light round it.</summary>
    static LinearGradientBrush BeamHalo => _beamHalo ??= Lin(new Point(0, 0), new Point(1, 0),
        (Mod.A(Color.FromRgb(0x7F, 0xC4, 0xFF), 0), 0), (Mod.A(Color.FromRgb(0x9F, 0xD4, 0xFF), .38), .5), (Mod.A(Color.FromRgb(0x7F, 0xC4, 0xFF), 0), 1));

    /// <summary>The gold the plate materialises from: molten gold, light at the top.</summary>
    static LinearGradientBrush VeilBrush => _veil ??= Lin(new Point(0, 0), new Point(.3, 1),
        (Color.FromRgb(0xFF, 0xF4, 0xCE), 0), (GoldHiC, .3), (GoldMidC, .7), (GoldLoC, 1));
}
