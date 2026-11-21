using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

/// <summary>
/// Tide's drawn parts: the deep-sea glass (abyssal teal fading down, the light from the surface, a net of caustic light near
/// its top), the water that fills a bar (a gently waving surface, the leading edge a curling crest with white foam), the pearl
/// in its shell that marks the leader, rings of ripples and rising bubbles. All of it frozen brushes and small geometry made
/// per draw; the caustic net is one bitmap tile made once and drawn through a brush, so a draw stays cheap.
/// </summary>
sealed partial class HudTide
{
    // the sea's palette: abyss at the bottom, deep teal at the top, the sea tint the class colours lean to, aqua light, foam
    static readonly Color AbyssC = Color.FromRgb(0x04, 0x16, 0x1C), DeepSeaC = Color.FromRgb(0x08, 0x2A, 0x33), SeaC = Color.FromRgb(0x0F, 0x4E, 0x5C),
        SeaTintC = Color.FromRgb(0x1A, 0xA9, 0xB8), AquaC = Color.FromRgb(0x6F, 0xE6, 0xEA), FoamC = Color.FromRgb(0xF0, 0xFF, 0xFF), CausticC = Color.FromRgb(0xA4, 0xF8, 0xF2),
        PearlC = Color.FromRgb(0xF7, 0xF2, 0xEC), PearlEdgeC = Color.FromRgb(0xC9, 0xC2, 0xD6), ShellC = Color.FromRgb(0xF6, 0xCF, 0xAE), ShellDeepC = Color.FromRgb(0xC4, 0x7E, 0x63),
        DarkC = Color.FromRgb(0x02, 0x10, 0x15);

    // ================================================================ the caustic net

    const int CauW = 240, CauH = 64;

    /// <summary>The caustic net: the bright, wavering lines light draws on a pool floor, fading out downward; tiles across.</summary>
    static readonly ImageBrush Caustic = MakeCaustic();

    /// <summary>
    /// The net as cell walls: the edges between wavy cells (the distance to a point's nearest centre against its second), the
    /// centres wrapped across the tile's width so it repeats seamlessly; the walls bright and thin, a breath of light inside
    /// each cell, all of it fading out over the tile's height.
    /// </summary>
    static ImageBrush MakeCaustic()
    {
        var rnd = new Random(2611);
        var pts = new List<(double X, double Y)>();
        for (int i = 0; i < 34; i++) pts.Add((rnd.NextDouble() * CauW, -14 + rnd.NextDouble() * (CauH + 28)));
        var px = new byte[CauW * CauH * 4];
        for (int y = 0; y < CauH; y++)
            for (int x = 0; x < CauW; x++)
            {
                // the walls waver: the point is moved by waves that repeat across the tile
                double wx = x + 5.5 * Math.Sin(2 * Math.PI * y / 27.0 + .7) + 3 * Math.Sin(2 * Math.PI * x * 5 / CauW + y / 9.0);
                double wy = y + 4.5 * Math.Sin(2 * Math.PI * x * 4 / CauW + 1.3) + 2 * Math.Sin(2 * Math.PI * y / 13.0 + x / 11.0);
                double d1 = double.MaxValue, d2 = double.MaxValue;
                foreach (var (cx, cy) in pts)
                {
                    double dx = wx - cx; dx -= CauW * Math.Round(dx / CauW);
                    double dy = wy - cy, d = Math.Sqrt(dx * dx + dy * dy);
                    if (d < d1) { d2 = d1; d1 = d; } else if (d < d2) d2 = d;
                }
                double edge = d2 - d1;
                double i0 = Math.Pow(Math.Max(0, 1 - edge / 2.6), 2.4) * (.55 + .45 * Math.Sin(x * .09 + y * .21)) + .1 * Math.Max(0, 1 - edge / 12);
                double fade = Math.Pow(1 - y / (double)(CauH - 1), 1.7);
                double a = Math.Clamp(i0 * fade, 0, 1) * .92;
                int o = (y * CauW + x) * 4;
                byte A = (byte)Math.Round(255 * a);
                px[o] = (byte)(CausticC.B * A / 255); px[o + 1] = (byte)(CausticC.G * A / 255); px[o + 2] = (byte)(CausticC.R * A / 255); px[o + 3] = A;
            }
        var bmp = BitmapSource.Create(CauW, CauH, 96, 96, PixelFormats.Pbgra32, null, px, CauW * 4);
        bmp.Freeze();
        var b = new ImageBrush(bmp) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, CauW, CauH), ViewportUnits = BrushMappingMode.Absolute, Stretch = Stretch.None };
        b.Freeze();
        return b;
    }

    /// <summary>The caustic net over <paramref name="r"/> (its top the tile's bright top), slid <paramref name="shift"/> px along, at <paramref name="alpha"/>.</summary>
    static void Caustics(DrawingContext dc, Rect r, double alpha, double shift = 0)
    {
        if (alpha <= .005 || r.Width < 1 || r.Height < 1) return;
        dc.PushClip(new RectangleGeometry(r));
        dc.PushOpacity(Math.Clamp(alpha, 0, 1));
        double sx = r.X + shift % CauW;
        dc.PushTransform(new TranslateTransform(sx, r.Y));
        dc.DrawRectangle(Caustic, null, new Rect(r.X - sx - CauW, 0, r.Width + 2 * CauW, Math.Min(r.Height, CauH)));
        dc.Pop(); dc.Pop(); dc.Pop();
    }

    // ================================================================ the deep-sea glass

    /// <summary>The room the sea's surface takes over a panel's top (its swell): a panel's words start this much lower.</summary>
    internal const double SurfaceH = 5;

    /// <summary>The surface's line along a panel's top: a long slow swell with a shorter one on it, between about 2 and 7 px down.</summary>
    static double TopY(double x) => 4.4 + 1.6 * Math.Sin(2 * Math.PI * x / 74 + .6) + .8 * Math.Sin(2 * Math.PI * x / 31 + 1.7);

    /// <summary>The breaking wave's height over a panel's box at full size (its spray goes a little higher).</summary>
    internal const double CrestH = 24;

    /// <summary>The room a panel with a wave of size <paramref name="k"/> needs over its box for the wave and its spray.</summary>
    internal static double CrestRoom(double k) => CrestH * k + 9;

    /// <summary>The breaking wave's span along a panel's top (at its right end).</summary>
    static double CrestW(double w, double k) => Math.Clamp(w * .42, 104, 165) * k;

    /// <summary>
    /// A panel's silhouette. <paramref name="surface"/>: its top is the sea's surface seen from below, a slow swell that rises
    /// at the right into a breaking wave standing up out of the panel (<paramref name="k"/> its size): the back of the wave up
    /// to the lip, the lip pitching forward and hooking under, the face falling to the trough, the broken water running on
    /// to the corner. Else a rounded box.
    /// </summary>
    internal static Geometry PanelShape(Size s, double radius, bool surface, double k = 1)
    {
        var r = new Rect(.5, .5, Math.Max(0, s.Width - 1), Math.Max(0, s.Height - 1));
        if (!surface || s.Width < 120 || s.Height < 30) return Mod.Pill(r, radius);
        double w = r.Right, h = r.Bottom, wc = CrestW(w, k), hr = CrestH * k, x0 = w - wc;
        double X(double u) => x0 + wc * u;
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(r.X, h - radius), true, true);
            c.LineTo(new Point(r.X, TopY(0) + radius), true, true);
            c.QuadraticBezierTo(new Point(r.X, TopY(0)), new Point(radius, TopY(radius)), true, true);
            for (double x = radius + 4; x < x0; x += 4) c.LineTo(new Point(x, TopY(x)), true, true);
            double y0 = TopY(x0);
            c.LineTo(new Point(x0, y0), true, true);
            c.BezierTo(new Point(X(.22), y0 - .1 * hr), new Point(X(.36), -hr), new Point(X(.56), -hr), true, true);       // the back, up to the lip
            c.BezierTo(new Point(X(.78), -hr), new Point(X(.95), -.74 * hr), new Point(X(.92), -.22 * hr), true, true);   // the lip pitching far forward
            c.BezierTo(new Point(X(.9), .05 * hr), new Point(X(.92), 6), new Point(X(.96), 7.5), true, true);              // the falling face
            c.BezierTo(new Point(X(.98), 8), new Point(w - 1, 8), new Point(w, radius + 9), true, true);                   // broken water to the corner
            c.LineTo(new Point(w, h - radius), true, true);
            c.ArcTo(new Point(w - radius, h), new Size(radius, radius), 0, false, SweepDirection.Clockwise, true, true);
            c.LineTo(new Point(r.X + radius, h), true, true);
            c.ArcTo(new Point(r.X, h - radius), new Size(radius, radius), 0, false, SweepDirection.Clockwise, true, true);
        }
        g.Freeze();
        return g;
    }

    /// <summary>
    /// The breaking wave's water and foam (inside and over the shape): lit translucent teal in the wave, the dark barrel under
    /// the lip with a curl of light inside it, white foam along the swell and heaped on the lip, spray thrown up off it.
    /// </summary>
    static void SurfaceFoam(DrawingContext dc, Size s, double radius, Geometry shape, double k = 1, double alpha = 1)
    {
        double w = s.Width - .5, wc = CrestW(w, k), hr = CrestH * k, x0 = w - wc;
        double X(double u) => x0 + wc * u;
        // the wave's water, lit from the surface: bright teal in the wave, fading down into the glass
        dc.PushClip(shape);
        var lit = new RadialGradientBrush { Center = new Point(.5, .5), GradientOrigin = new Point(.55, .35), RadiusX = .5, RadiusY = .5 };
        lit.GradientStops.Add(new GradientStop(Mod.A(Mod.Mix(SeaTintC, FoamC, .25), .8 * alpha), 0));
        lit.GradientStops.Add(new GradientStop(Mod.A(SeaTintC, .45 * alpha), .55));
        lit.GradientStops.Add(new GradientStop(Mod.A(SeaTintC, 0), 1));
        lit.Freeze();
        dc.DrawEllipse(lit, null, new Point(X(.6), -.3 * hr), wc * .66, hr * 1.25);
        // the barrel: dark under the lip, a curl of light inside it
        var barrel = new RadialGradientBrush { GradientOrigin = new Point(.6, .4) };
        barrel.GradientStops.Add(new GradientStop(Mod.A(DarkC, .85 * alpha), 0));
        barrel.GradientStops.Add(new GradientStop(Mod.A(DeepSeaC, .55 * alpha), .7));
        barrel.GradientStops.Add(new GradientStop(Mod.A(DeepSeaC, 0), 1));
        barrel.Freeze();
        var tube = new Point(X(.81), -.38 * hr);
        dc.DrawEllipse(barrel, null, tube, wc * .12, hr * .32);
        var eye = new RadialGradientBrush();
        eye.GradientStops.Add(new GradientStop(Mod.A(AquaC, .4 * alpha), 0));
        eye.GradientStops.Add(new GradientStop(Mod.A(AquaC, 0), 1));
        eye.Freeze();
        dc.DrawEllipse(eye, null, new Point(tube.X - wc * .07, tube.Y + hr * .06), wc * .045, hr * .16);   // the light at the tube's far end
        var curl = new StreamGeometry();
        using (var c = curl.Open())
        {
            // the lip's curl as a spiral of white water: forward over the tube, down its mouth, back under and up into it
            c.BeginFigure(new Point(X(.66), -.98 * hr), false, false);
            c.BezierTo(new Point(X(.86), -1.02 * hr), new Point(X(.96), -.72 * hr), new Point(X(.93), -.36 * hr), true, true);
            c.BezierTo(new Point(X(.9), -.06 * hr), new Point(X(.74), -.04 * hr), new Point(X(.71), -.3 * hr), true, true);
            c.BezierTo(new Point(X(.69), -.5 * hr), new Point(X(.8), -.6 * hr), new Point(X(.84), -.44 * hr), true, true);
        }
        curl.Freeze();
        dc.DrawGeometry(null, Mod.P(Mod.Br(AquaC, .45 * alpha), 6 * Math.Max(.7, k), round: true), curl);
        dc.DrawGeometry(null, Mod.P(Mod.Br(FoamC, .95 * alpha), 2.6 * Math.Max(.7, k), round: true), curl);
        dc.Pop();
        // the foam line along the swell and up the back of the wave to its lip
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(radius, TopY(radius) + .6), false, false);
            for (double x = radius + 4; x < x0; x += 4) c.LineTo(new Point(x, TopY(x) + .6), true, true);
            double y0 = TopY(x0);
            c.LineTo(new Point(x0, y0 + .6), true, true);
            c.BezierTo(new Point(X(.22), y0 - .1 * hr + .6), new Point(X(.36), -hr + .7), new Point(X(.56), -hr + .7), true, true);
            c.BezierTo(new Point(X(.77), -hr + .7), new Point(X(.94), -.74 * hr), new Point(X(.91), -.24 * hr), true, true);
        }
        g.Freeze();
        var line = new Pen(new LinearGradientBrush(new GradientStopCollection
        {
            new(Mod.A(FoamC, .22 * alpha), 0), new(Mod.A(FoamC, .4 * alpha), .5), new(Mod.A(FoamC, 1 * alpha), .8), new(Mod.A(FoamC, .9 * alpha), 1),
        }, 0), 1.6 + .6 * k) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        line.Freeze();
        var soft = new Pen(new LinearGradientBrush(new GradientStopCollection
        {
            new(Mod.A(FoamC, 0), 0), new(Mod.A(FoamC, .08 * alpha), .55), new(Mod.A(FoamC, .45 * alpha), .82), new(Mod.A(FoamC, .35 * alpha), 1),
        }, 0), 5 + 3 * k) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        soft.Freeze();
        dc.DrawGeometry(null, soft, g);
        dc.DrawGeometry(null, line, g);
        // foam heaped on the lip, and the spray off it
        var blob = new RadialGradientBrush();
        blob.GradientStops.Add(new GradientStop(Mod.A(Colors.White, .98 * alpha), 0));
        blob.GradientStops.Add(new GradientStop(Mod.A(FoamC, .85 * alpha), .6));
        blob.GradientStops.Add(new GradientStop(Mod.A(AquaC, 0), 1));
        blob.Freeze();
        var cap = new Pen(Mod.Br(FoamC, .92 * alpha), 4.5 * Math.Max(.6, k)) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        cap.Freeze();
        var lipLine = new StreamGeometry();
        using (var c = lipLine.Open())
        {
            c.BeginFigure(new Point(X(.46), -.95 * hr + 1.2), false, false);
            c.BezierTo(new Point(X(.6), -hr + 1.4), new Point(X(.8), -hr + 1.6), new Point(X(.88), -.78 * hr + 1), true, true);
            c.BezierTo(new Point(X(.93), -.6 * hr), new Point(X(.93), -.4 * hr), new Point(X(.91), -.26 * hr), true, true);
        }
        lipLine.Freeze();
        dc.DrawGeometry(null, cap, lipLine);
        foreach (var (u, v, rr) in new[] { (.48, -.97, 2.6), (.56, -1.02, 3.4), (.64, -1.03, 3.8), (.72, -1.0, 4.0), (.8, -.93, 3.8), (.87, -.8, 3.4), (.91, -.62, 3.0), (.92, -.42, 2.6), (.4, -.88, 2.0), (.9, -.28, 2.0) })
            dc.DrawEllipse(blob, null, new Point(X(u), v * hr + 1), rr * Math.Max(.6, k), rr * Math.Max(.6, k) * .85);
        var drop = Mod.Br(FoamC, .9 * alpha);
        for (int i = 0; i < 24; i++)
        {
            // the spray: a fan of drops thrown forward and up off the lip, smaller and fainter as they fly
            double f = (i * .618034 + .11) % 1, g2 = (i * .381966 + .37) % 1;
            double u = .55 + .45 * f, v = -1.04 - .3 * g2 + .55 * Math.Pow(f, 2.2), rr = (1.3 - .7 * g2) * Math.Max(.7, k);
            if (u > .99) continue;
            dc.DrawEllipse(Mod.Br(FoamC, (.95 - .5 * g2) * alpha), null, new Point(X(u), Math.Max(-hr - 7.5, v * hr)), rr, rr);
        }
    }

    /// <summary>Shafts of light falling slantwise from the surface into the deep, faint and fading as they go down.</summary>
    static void LightShafts(DrawingContext dc, Size s, double alpha = 1)
    {
        double h = Math.Min(s.Height, 160);
        var b = Mod.Grad(true, (Mod.A(AquaC, .09 * alpha), 0), (Mod.A(AquaC, .035 * alpha), .55), (Mod.A(AquaC, 0), 1));
        foreach (var (at, wTop, wBot) in new[] { (.14, 16.0, 30.0), (.41, 10.0, 22.0), (.64, 22.0, 40.0) })
        {
            double x = s.Width * at;
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(new Point(x, 0), true, true);
                c.LineTo(new Point(x + wTop, 0), true, false);
                c.LineTo(new Point(x + wTop + h * .42 + (wBot - wTop) / 2, h), true, false);
                c.LineTo(new Point(x + h * .42 - (wBot - wTop) / 2, h), true, false);
            }
            g.Freeze();
            dc.DrawGeometry(b, null, g);
        }
    }

    /// <summary>A strand of kelp from its root at (<paramref name="x"/>, <paramref name="y"/>): a tapering ribbon swaying up <paramref name="h"/> px.</summary>
    static void Kelp(DrawingContext dc, double x, double y, double h, double sway, double wid, Brush fill, Pen edge)
    {
        var g = new StreamGeometry();
        Point Spine(double t) => new(x + sway * Math.Sin(t * 2.6 + .4) * t, y - h * t);
        using (var c = g.Open())
        {
            const int n = 14;
            for (int i = 0; i <= n; i++)
            {
                double t = i / (double)n, wd = wid * (1 - t * .8) / 2;
                var p = Spine(t);
                if (i == 0) c.BeginFigure(new Point(p.X - wd, p.Y), true, true); else c.LineTo(new Point(p.X - wd, p.Y), true, true);
            }
            for (int i = n; i >= 0; i--)
            {
                double t = i / (double)n, wd = wid * (1 - t * .8) / 2;
                var p = Spine(t);
                c.LineTo(new Point(p.X + wd, p.Y), true, true);
            }
        }
        g.Freeze();
        dc.DrawGeometry(fill, edge, g);
    }

    /// <summary>
    /// The seabed along a panel's foot: low sand dunes with an aqua rim of light, a few rocks, kelp swaying up at the left
    /// and a coral fan at the right; dark, so the words over it stay white on dark. Drawn with the glass, once.
    /// </summary>
    static void Seabed(DrawingContext dc, Size s)
    {
        double w = s.Width, b = s.Height;
        // kelp behind everything, rising from the left corner
        var kelp = Mod.Grad(true, (Mod.A(Color.FromRgb(0x2A, 0x8A, 0x78), .34), 0), (Mod.A(Color.FromRgb(0x10, 0x4A, 0x48), .6), 1));
        var kelpEdge = Mod.P(Mod.Br(AquaC, .16), .7);
        // the garden sits in the middle of the foot, between its words (the total at the left, the group's figure at the right)
        double m = w * .38;
        Kelp(dc, m - 26, b, 26, 4, 5, kelp, kelpEdge);
        Kelp(dc, m - 18, b, 19, -3, 4, kelp, kelpEdge);
        Kelp(dc, m + 30, b, 22, -3, 4, kelp, kelpEdge);
        // the coral fan: a few branching strokes in a muted coral
        var coral = Mod.P(Mod.Br(Color.FromRgb(0xE0, 0x7A, 0x84), .4), 1.6, round: true);
        double fx = m + 8, fy = b - 4;
        foreach (var (a, len) in new[] { (-2.2, 13.0), (-1.9, 16.0), (-1.57, 17.0), (-1.25, 15.0), (-.95, 12.0) })
        {
            var tip = new Point(fx + len * Math.Cos(a), fy + len * Math.Sin(a));
            dc.DrawLine(coral, new Point(fx, fy), tip);
            dc.DrawLine(coral, new Point(fx + len * .55 * Math.Cos(a), fy + len * .55 * Math.Sin(a)), new Point(tip.X + 5 * Math.Cos(a - .7), tip.Y + 5 * Math.Sin(a - .7) + 3));
        }
        // the sand: low dunes across the foot, a rim of light on their crests
        var sand = new StreamGeometry();
        using (var c = sand.Open())
        {
            c.BeginFigure(new Point(0, b), true, true);
            for (double x = 0; x <= w; x += 6) c.LineTo(new Point(x, b - 5 - 2.2 * Math.Sin(2 * Math.PI * x / 90 + .8) - 1.2 * Math.Sin(2 * Math.PI * x / 37)), true, true);
            c.LineTo(new Point(w, b), true, true);
        }
        sand.Freeze();
        dc.DrawGeometry(Mod.Grad(true, (Mod.A(Mod.Mix(SeaC, AbyssC, .35), .95), 0), (Mod.A(AbyssC, .98), 1)), Mod.P(Mod.Br(AquaC, .2), .8), sand);
        // across the whole foot, low under the words: pebbles, sprigs of weed and a starfish
        var pebble = Mod.Br(Mod.Mix(SeaC, AbyssC, .1), .95);
        foreach (var at in new[] { .05, .12, .21, .3, .62, .7, .79, .86, .95 })
        {
            double x = w * at, rr = 1.6 + 1.2 * ((at * 7.3) % 1);
            dc.DrawEllipse(pebble, Mod.P(Mod.Br(AquaC, .2), .6), new Point(x, b - 3.5), rr * 1.4, rr);
        }
        var sprig = Mod.P(Mod.Br(Color.FromRgb(0x3A, 0xA8, 0x8C), .5), 1.2, round: true);
        foreach (var at in new[] { .09, .27, .66, .83 })
        {
            double x = w * at;
            dc.DrawLine(sprig, new Point(x, b - 4), new Point(x - 2, b - 11));
            dc.DrawLine(sprig, new Point(x + 1.5, b - 4), new Point(x + 3, b - 9));
        }
        var star = new StreamGeometry();
        using (var c = star.Open())
        {
            double sx = w * .9, sy = b - 5.5;
            for (int i = 0; i < 10; i++)
            {
                double a = -Math.PI / 2 + i * Math.PI / 5, rr = i % 2 == 0 ? 5.2 : 2.2;
                var p = new Point(sx + rr * Math.Cos(a), sy + rr * Math.Sin(a) * .8);
                if (i == 0) c.BeginFigure(p, true, true); else c.LineTo(p, true, true);
            }
        }
        star.Freeze();
        dc.DrawGeometry(Mod.Br(Color.FromRgb(0xE8, 0x86, 0x6A), .75), Mod.P(Mod.Br(DarkC, .6), .6), star);
        // rocks on the sand
        var rock = Mod.Grad(true, (Mod.A(Mod.Mix(SeaC, AbyssC, .2), .95), 0), (Mod.A(DarkC, .95), 1));
        var rim = Mod.P(Mod.Br(AquaC, .22), .8);
        dc.DrawGeometry(rock, rim, Mod.Pill(new Rect(m - 12, b - 11, 18, 10), 5));
        dc.DrawGeometry(rock, rim, Mod.Pill(new Rect(m + 1, b - 8, 11, 7), 3.5));
        dc.DrawGeometry(rock, rim, Mod.Pill(new Rect(m + 36, b - 9, 14, 8), 4));
    }

    /// <summary>
    /// The sea glass: a soft drop under it (outside its shape only), abyssal teal from its top fading deeper to its foot, the
    /// light from the surface falling in at the top (a glow and slanting shafts), the caustic net near the top
    /// (<paramref name="cau"/> its strength; <paramref name="shift"/> slides it), an aqua rim brighter at the top.
    /// <paramref name="surface"/>: its top is the sea's surface, a swell breaking into a wave standing up out of it at the
    /// right, with foam and spray (else a line of surface light under a rounded top); <paramref name="seabed"/>: the seabed
    /// along its foot. <paramref name="tint"/>: a breath of another colour in the water (a rare drop's grade). Drawn when its
    /// size or the opacity setting changes, never per frame.
    /// </summary>
    internal static void SeaPanel(DrawingContext dc, Size s, double radius, double opacity, double cau = 1, double shift = 0, bool shadow = true, Color? tint = null, double tintK = .3, bool surface = false, bool seabed = false, double crest = 1)
    {
        if (s.Width < 4 || s.Height < 4) return;
        var shape = PanelShape(s, radius, surface, crest);
        surface = surface && shape is StreamGeometry;
        if (shadow)
        {
            // the drop: rings outside the shape, deeper below it, kept inside the window's room
            for (int k = 1; k <= 6; k++)
            {
                var o = new Rect(.5 - k * 1.0, .5 - k * .5 + 2.5 + (surface ? 10 : 0), s.Width - 1 + k * 2.0, s.Height - 1 + k * 1.5 + 1.5 - (surface ? 10 : 0));
                var ring = new CombinedGeometry(GeometryCombineMode.Exclude, Mod.Pill(o, radius + k), shape);
                ring.Freeze();
                dc.DrawGeometry(Mod.Br(DarkC, .07 - k * .009), null, ring);
            }
        }
        double a = Math.Clamp(.58 + .32 * opacity, .62, .94);
        var top = tint is { } tc ? Mod.Mix(DeepSeaC, tc, tintK) : DeepSeaC;
        var mid = Mod.Mix(top, AbyssC, .55);
        dc.DrawGeometry(Mod.Grad(true, (Mod.A(top, a), 0), (Mod.A(mid, Math.Min(.96, a + .03)), .5), (Mod.A(AbyssC, Math.Min(.97, a + .06)), 1)), null, shape);
        dc.PushClip(shape);
        var glow = new RadialGradientBrush { Center = new Point(.5, 0), GradientOrigin = new Point(.5, 0), RadiusX = .62, RadiusY = Math.Min(1.6, 70 / s.Height) };
        glow.GradientStops.Add(new GradientStop(Mod.A(tint is { } g0 ? Mod.Mix(AquaC, g0, .5) : AquaC, .16), 0));
        glow.GradientStops.Add(new GradientStop(Mod.A(AquaC, 0), 1));
        glow.Freeze();
        dc.DrawRectangle(glow, null, new Rect(0, -CrestRoom(crest), s.Width, s.Height + CrestRoom(crest)));
        if (surface) LightShafts(dc, s);
        Caustics(dc, new Rect(0, -12, s.Width, Math.Min(CauH, s.Height + 12)), .5 * cau, shift);
        if (seabed && s.Height > 70) Seabed(dc, s);
        if (!surface)
            dc.DrawRectangle(Mod.Grad(false, (Mod.A(AquaC, 0), 0), (Mod.A(AquaC, .45), .22), (Mod.A(FoamC, .6), .5), (Mod.A(AquaC, .45), .78), (Mod.A(AquaC, 0), 1)), null,
                new Rect(radius * .6, 1.4, Math.Max(0, s.Width - radius * 1.2), 1));
        dc.Pop();
        dc.DrawGeometry(null, Mod.P(Mod.Grad(true, (Mod.A(AquaC, .42), 0), (Mod.A(AquaC, .16), .3), (Mod.A(AquaC, .1), 1)), 1), shape);
        if (surface) SurfaceFoam(dc, s, radius, shape, crest);
    }

    // ================================================================ the water in a bar

    /// <summary>The surface's height at <paramref name="x"/>: a gentle wave, lifted where a swell passes.</summary>
    static double Surface(double x, double top, double phase, double swellX, double swellA) =>
        top + 1.2 + .6 * Math.Sin(2 * Math.PI * x / 31 + phase) - (swellA > 0 ? swellA * Math.Exp(-Math.Pow((x - swellX) / 15, 2)) : 0);

    /// <summary>
    /// The water's shape from the left to <paramref name="fx"/> over a bar <paramref name="h"/> tall from <paramref name="top"/>:
    /// a gently waving surface (lifted by a passing swell centred at <paramref name="swellX"/>, <paramref name="swellA"/> px),
    /// the leading edge a crest that curls forward over a hollow face. The crest shrinks on a short bar.
    /// </summary>
    static Geometry Water(double x0, double fx, double top, double h, double phase = 0, double swellX = -999, double swellA = 0)
    {
        double k = Math.Clamp((fx - x0) / 26, .3, 1), cw = 15 * k;
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(x0 - 1, top + h), true, true);
            double xs = Math.Max(x0, fx - cw);
            for (double x = x0 - 1; x < xs; x += 4) c.LineTo(new Point(x, Surface(x, top, phase, swellX, swellA)), true, true);
            double ys = Surface(xs, top, phase, swellX, swellA);
            c.LineTo(new Point(xs, ys), true, true);
            // the crest: up and over, its lip pitching forward
            c.BezierTo(new Point(fx - cw * .5, ys - 2.2 * k), new Point(fx + 2.4 * k, top - .5), new Point(fx + 1.6 * k, top + h * .3), true, true);
            // the curl back under the lip, into the hollow
            c.BezierTo(new Point(fx + .9 * k, top + h * .46), new Point(fx - 3.4 * k, top + h * .48), new Point(fx - 4.8 * k, top + h * .6), true, true);
            // the face down to the trough
            c.BezierTo(new Point(fx - 5.8 * k, top + h * .74), new Point(fx - 2 * k, top + h * .9), new Point(fx - .8 * k, top + h), true, true);
        }
        g.Freeze();
        return g;
    }

    /// <summary>A brush or pen in a colour at a strength, made once and kept (a draw reuses them, frozen).</summary>
    static readonly Dictionary<(Color, int), Brush> FillCache = new();
    static readonly Dictionary<int, (Brush Glow, Pen Soft, Pen Line, Brush Under, Brush Fleck, Brush Fleck2, Brush Fleck3)> FoamCache = new();

    /// <summary>The water's fill for a tint at a strength: deep at the left, lit toward the crest (across the water's own box).</summary>
    static Brush WaterFill(Color tint, double alpha)
    {
        var key = (tint, (int)Math.Round(alpha * 100));
        if (FillCache.TryGetValue(key, out var b)) return b;
        var fill = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        fill.GradientStops.Add(new GradientStop(Mod.A(Mod.Mix(tint, AbyssC, .64), .94 * alpha), 0));
        fill.GradientStops.Add(new GradientStop(Mod.A(Mod.Mix(tint, AbyssC, .38), .94 * alpha), .6));
        fill.GradientStops.Add(new GradientStop(Mod.A(Mod.Mix(tint, AbyssC, .24), .95 * alpha), 1));
        fill.Freeze();
        if (FillCache.Count > 256) FillCache.Clear();
        FillCache[key] = fill;
        return fill;
    }

    /// <summary>The foam's glow, pens and flecks (and the light under the surface) at a strength, made once.</summary>
    static (Brush Glow, Pen Soft, Pen Line, Brush Under, Brush Fleck, Brush Fleck2, Brush Fleck3) FoamKit(double boost, double alpha)
    {
        int key = (int)Math.Round(boost * 10) * 1000 + (int)Math.Round(alpha * 100);
        if (FoamCache.TryGetValue(key, out var kit)) return kit;
        var glow = new RadialGradientBrush();
        glow.GradientStops.Add(new GradientStop(Mod.A(FoamC, Math.Min(1, (.3 + .4 * boost) * alpha)), 0));
        glow.GradientStops.Add(new GradientStop(Mod.A(AquaC, .1 * alpha), .55));
        glow.GradientStops.Add(new GradientStop(Mod.A(AquaC, 0), 1));
        glow.Freeze();
        // across the foam line's own box, left to right: clear, then whiter to the lip
        var line = new Pen(new LinearGradientBrush(new GradientStopCollection
        {
            new(Mod.A(FoamC, 0), 0), new(Mod.A(FoamC, .5 * alpha), .45), new(Mod.A(FoamC, .95 * alpha), .85), new(Mod.A(FoamC, .7 * alpha), 1),
        }, 0), 1.7 + .5 * boost) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        line.Freeze();
        var soft = new Pen(new LinearGradientBrush(new GradientStopCollection
        {
            new(Mod.A(FoamC, 0), 0), new(Mod.A(FoamC, .16 * alpha), .5), new(Mod.A(FoamC, (.34 + .2 * boost) * alpha), .9), new(Mod.A(FoamC, .2 * alpha), 1),
        }, 0), 4.2 + boost) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        soft.Freeze();
        var under = Mod.Grad(true, (Mod.A(FoamC, .3 * alpha), 0), (Mod.A(FoamC, .1 * alpha), .22), (Mod.A(FoamC, 0), .45), (Mod.A(AbyssC, .34 * alpha), 1));
        kit = (glow, soft, line, under, Mod.Br(FoamC, .85 * alpha), Mod.Br(FoamC, .6 * alpha), Mod.Br(FoamC, .55 * alpha));
        FoamCache[key] = kit;
        return kit;
    }

    /// <summary>The foam along the crest: a line of white rising over the lip (bright at the lip) on a softer wide foam, flecks round it, a soft glow at the lip.</summary>
    static void Foam(DrawingContext dc, double x0, double fx, double top, double h, double phase = 0, double boost = 0, double alpha = 1)
    {
        var kit = FoamKit(boost, alpha);
        double k = Math.Clamp((fx - x0) / 26, .3, 1), cw = 15 * k;
        double xs = Math.Max(x0, fx - cw), xf = Math.Max(x0, xs - 9 * k);
        var tip = new Point(fx + 1.6 * k, top + h * .3);
        double gr = 6.5 + 4 * boost;
        dc.DrawEllipse(kit.Glow, null, new Point(tip.X - 2, tip.Y - 1), gr, gr * .85);
        var g = new StreamGeometry();
        double ys = Surface(xs, top, phase, -999, 0);
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(xf, Surface(xf, top, phase, -999, 0) + .3), false, false);
            c.LineTo(new Point(xs, ys), true, true);
            c.BezierTo(new Point(fx - cw * .5, ys - 2.2 * k), new Point(fx + 2.4 * k, top - .5), tip, true, true);
            c.BezierTo(new Point(fx + 1 * k, top + h * .42), new Point(fx - 1.6 * k, top + h * .46), new Point(fx - 3 * k, top + h * .5), true, true);
        }
        g.Freeze();
        dc.DrawGeometry(null, kit.Soft, g);
        dc.DrawGeometry(null, kit.Line, g);
        dc.DrawEllipse(kit.Fleck, null, new Point(fx - 5 * k, top + h * .05), 1, 1);
        dc.DrawEllipse(kit.Fleck2, null, new Point(fx - 8 * k, Surface(fx - 8 * k, top, phase, -999, 0) - .9), .7, .7);
        dc.DrawEllipse(kit.Fleck3, null, new Point(fx + 2.4 * k, top + h * .44), .7, .7);
    }

    /// <summary>A class colour leaned to the sea: the tint a bar's water takes.</summary>
    static Color SeaTint(Color c) => Mod.Mix(c, SeaTintC, .36);

    /// <summary>
    /// A bar of water over <paramref name="r"/> (its track already drawn): the water to <paramref name="fx"/> px in the sea tint
    /// of <paramref name="c"/>, deep at the left and lit toward the crest, the light under its surface, the foam on its crest;
    /// <paramref name="caustic"/>: the caustic net over the water (the leader's). Brushes and pens come from a cache; the
    /// draw makes only the water's shape. Returns the shape (a moment's light is clipped to it).
    /// </summary>
    static Geometry? WaterBar(DrawingContext dc, Rect r, double fx, Color c, double caustic = 0, double boost = 0, double phase = 0, double alpha = 1, double foam = -1)
    {
        if (fx < 1.5) return null;
        double x1 = r.X + fx;
        var water = Water(r.X, x1, r.Y, r.Height, phase);
        dc.DrawGeometry(WaterFill(SeaTint(c), alpha), null, water);
        dc.PushClip(water);
        // the light under the surface, and the deep at the foot
        dc.DrawRectangle(FoamKit(0, alpha).Under, null, new Rect(r.X - 2, r.Y, fx + 6, r.Height));
        if (caustic > 0) Caustics(dc, new Rect(r.X, r.Y, fx + 4, r.Height), caustic * alpha, 37);
        dc.Pop();
        Foam(dc, r.X, x1, r.Y, r.Height, phase, boost, foam < 0 ? alpha : foam);
        return water;
    }

    // ================================================================ the pearl, ripples and bubbles

    /// <summary>
    /// The leader's mark: an open clam holding a pearl, centred on (<paramref name="cx"/>, <paramref name="cy"/>), about 27 px
    /// wide and 20 tall: the upper shell a scalloped fan standing open behind, the pearl (iridescent, a white glint) sitting
    /// in the lower shell's bowl, which covers its foot. <paramref name="shine"/> a glow round the pearl.
    /// </summary>
    static void Pearl(DrawingContext dc, double cx, double cy, double scale = 1, double open = 1, double rise = 0, double alpha = 1, double shine = 0)
    {
        if (alpha <= .01) return;
        dc.PushOpacity(Math.Clamp(alpha, 0, 1));
        double s = scale, R = 12.5 * s * (.35 + .65 * Math.Clamp(open, 0, 1));
        var hinge = new Point(cx, cy + 8 * s);
        var edge = Mod.P(Mod.Br(DarkC, .7), .9);
        // the upper shell: six scallops round an arc above the hinge, ribs fanning from it
        const int lobes = 6;
        double a0 = Math.PI * 1.08, a1 = Math.PI * 1.92;
        Point P(double a, double rr) => new(hinge.X + rr * Math.Cos(a), hinge.Y + rr * Math.Sin(a) * .9);
        var fan = new StreamGeometry();
        using (var c = fan.Open())
        {
            c.BeginFigure(hinge, true, true);
            c.LineTo(P(a0, R), true, true);
            for (int i = 0; i < lobes; i++)
            {
                double la = a0 + (a1 - a0) * i / lobes, lb = a0 + (a1 - a0) * (i + 1) / lobes;
                c.QuadraticBezierTo(P((la + lb) / 2, R * 1.13), P(lb, R), true, true);
            }
        }
        fan.Freeze();
        var shell = new RadialGradientBrush { Center = new Point(.5, 1), GradientOrigin = new Point(.5, 1), RadiusX = .8, RadiusY = 1.1 };
        shell.GradientStops.Add(new GradientStop(Mod.Mix(ShellC, Colors.White, .45), 0));
        shell.GradientStops.Add(new GradientStop(ShellC, .5));
        shell.GradientStops.Add(new GradientStop(ShellDeepC, 1));
        shell.Freeze();
        dc.DrawGeometry(shell, edge, fan);
        var rib = Mod.P(Mod.Br(ShellDeepC, .8), .7);
        for (int i = 1; i < lobes; i++) dc.DrawLine(rib, new Point(hinge.X, hinge.Y - 1), P(a0 + (a1 - a0) * i / lobes, R * .97));
        // the pearl: iridescent (white at its glint, a blush of rose and a sheen of aqua at its rim)
        double pr = 5.4 * s;
        var pc = new Point(cx, cy + 1.6 * s + rise);
        if (shine > 0)
        {
            var halo = new RadialGradientBrush();
            halo.GradientStops.Add(new GradientStop(Mod.A(FoamC, .7 * shine), 0));
            halo.GradientStops.Add(new GradientStop(Mod.A(AquaC, 0), 1));
            halo.Freeze();
            dc.DrawEllipse(halo, null, pc, pr * 3, pr * 3);
        }
        var pearl = new RadialGradientBrush { GradientOrigin = new Point(.32, .28), Center = new Point(.42, .4), RadiusX = .64, RadiusY = .64 };
        pearl.GradientStops.Add(new GradientStop(Colors.White, 0));
        pearl.GradientStops.Add(new GradientStop(PearlC, .35));
        pearl.GradientStops.Add(new GradientStop(Color.FromRgb(0xF6, 0xC8, 0xDE), .66));
        pearl.GradientStops.Add(new GradientStop(Color.FromRgb(0x8E, 0xDE, 0xE6), 1));
        pearl.Freeze();
        dc.DrawEllipse(pearl, Mod.P(Mod.Br(DarkC, .55), .8), pc, pr, pr);
        dc.DrawEllipse(Mod.Br(Colors.White, 1), null, new Point(pc.X - pr * .36, pc.Y - pr * .38), pr * .24, pr * .2);
        // the lower shell: a bowl in front of the pearl's foot, its lip lightly scalloped, ribbed
        double bw = 11.5 * s, top = cy + 4.2 * s, bot = cy + 10.5 * s;
        var bowl = new StreamGeometry();
        using (var c = bowl.Open())
        {
            c.BeginFigure(new Point(cx - bw, top), true, true);
            c.BezierTo(new Point(cx - bw, bot - 1 * s), new Point(cx - bw * .45, bot), new Point(cx, bot), true, true);
            c.BezierTo(new Point(cx + bw * .45, bot), new Point(cx + bw, bot - 1 * s), new Point(cx + bw, top), true, true);
            for (int i = 5; i > 0; i--)
            {
                double xa = cx - bw + 2 * bw * i / 5, xb = cx - bw + 2 * bw * (i - 1) / 5;
                c.QuadraticBezierTo(new Point((xa + xb) / 2, top - 1.4 * s), new Point(xb, top), true, true);
            }
        }
        bowl.Freeze();
        var bowlB = Mod.Grad(true, (Mod.Mix(ShellC, Colors.White, .3), 0), (ShellC, .45), (ShellDeepC, 1));
        dc.DrawGeometry(bowlB, edge, bowl);
        for (int i = 1; i < 5; i++)
        {
            double x = cx - bw + 2 * bw * i / 5;
            dc.DrawLine(rib, new Point(x, top + .8 * s), new Point(cx + (x - cx) * .55, bot - .8 * s));
        }
        dc.Pop();
    }

    /// <summary>The leader's pearl in its shell, drawn once (centred on 0,0) and reused by every draw.</summary>
    static readonly Drawing PearlArt = MakePearlArt();
    internal const double PearlScale = .8;

    static Drawing MakePearlArt()
    {
        var g = new DrawingGroup();
        using (var dc = g.Open()) Pearl(dc, 0, 0, PearlScale);
        g.Freeze();
        return g;
    }

    /// <summary>A soft round glow in a colour (the pearl's shine, a rare drop's bloom), drawn once.</summary>
    static Brush GlowBrush(Color c, double a)
    {
        var b = new RadialGradientBrush();
        b.GradientStops.Add(new GradientStop(Mod.A(c, a), 0));
        b.GradientStops.Add(new GradientStop(Mod.A(c, a * .35), .5));
        b.GradientStops.Add(new GradientStop(Mod.A(c, 0), 1));
        b.Freeze();
        return b;
    }

    // ================================================================ moving parts (only their transforms and opacity change)

    /// <summary>
    /// Rings of ripples spreading flat from a point: each ring an ellipse drawn once at its full reach, grown from a dot by a
    /// scale and faded by its opacity (a ring starts fine and widens to its line), each a little after the one before.
    /// </summary>
    internal sealed class RippleSet
    {
        public readonly Canvas Layer = new() { IsHitTestVisible = false, Visibility = Visibility.Collapsed };
        readonly (System.Windows.Shapes.Ellipse El, ScaleTransform S)[] _rings;

        public RippleSet(double reach, double flat, Color c, int rings = 3, double alpha = .8, double th = 1.3)
        {
            _rings = new (System.Windows.Shapes.Ellipse, ScaleTransform)[rings];
            for (int k = 0; k < rings; k++)
            {
                var s = new ScaleTransform(.1, .1);
                var e = new System.Windows.Shapes.Ellipse
                {
                    Width = 2 * reach, Height = 2 * reach * flat, Stroke = Frozen(Mod.A(c, alpha * (1 - .2 * k))), StrokeThickness = th - .2 * k,
                    RenderTransformOrigin = new Point(.5, .5), RenderTransform = s, IsHitTestVisible = false,
                };
                Canvas.SetLeft(e, -reach); Canvas.SetTop(e, -reach * flat);
                Layer.Children.Add(e);
                _rings[k] = (e, s);
            }
        }

        /// <summary>Its centre in the canvas it sits in (set once, at the moment's start).</summary>
        public void At(double x, double y) { Canvas.SetLeft(Layer, x); Canvas.SetTop(Layer, y); }

        /// <summary>The rings at their course's point, 0 to 1 (outside it: hidden).</summary>
        public void Step(double p)
        {
            bool on = p is > 0 and < 1;
            Layer.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            if (!on) return;
            for (int k = 0; k < _rings.Length; k++)
            {
                double q = Seg(p, k * .16, k * .16 + .62);
                var (e, s) = _rings[k];
                e.Opacity = q is > 0 and < 1 ? 1 - q : 0;
                s.ScaleX = s.ScaleY = .1 + .9 * Out(q);
            }
        }
    }

    /// <summary>A bubble: a rim of light, a faint aqua fill and a glint, drawn once into a brush every bubble wears.</summary>
    static readonly DrawingBrush BubbleBrush = MakeBubble();

    static DrawingBrush MakeBubble()
    {
        var g = new DrawingGroup();
        using (var dc = g.Open())
        {
            dc.DrawEllipse(Mod.Br(AquaC, .18), Mod.P(Mod.Br(FoamC, .85), 1.4), new Point(10, 10), 8.6, 8.6);
            dc.DrawEllipse(Mod.Br(Colors.White, .9), null, new Point(6.8, 6.6), 2.4, 2.2);
        }
        g.Freeze();
        var b = new DrawingBrush(g) { Stretch = Stretch.Uniform, Viewbox = new Rect(0, 0, 20, 20), ViewboxUnits = BrushMappingMode.Absolute };
        b.Freeze();
        return b;
    }

    /// <summary>
    /// Bubbles rising (at most nine): small rectangles wearing <see cref="BubbleBrush"/>, each placed once at the moment's start
    /// (by its number, so a held frame draws them the same each time), then only moved and faded: each starts low at its own
    /// moment, rises with a small wobble and fades out.
    /// </summary>
    internal sealed class BubbleSet
    {
        public readonly Canvas Layer = new() { IsHitTestVisible = false, Visibility = Visibility.Collapsed };
        sealed class B { public required System.Windows.Shapes.Rectangle El; public required TranslateTransform T; public double X, Y, Start, R; }
        readonly B[] _b;
        double _rise = 46;

        public BubbleSet(int n)
        {
            _b = new B[Math.Min(n, 9)];
            for (int i = 0; i < _b.Length; i++)
            {
                double r = 1.8 + 2.1 * ((i * .381966 + .4) % 1);
                var t = new TranslateTransform();
                var el = new System.Windows.Shapes.Rectangle { Width = 2 * r, Height = 2 * r, Fill = BubbleBrush, RenderTransform = t, IsHitTestVisible = false };
                Layer.Children.Add(el);
                _b[i] = new B { El = el, T = t, R = r, Start = (i % 5) * .07 + (i / 5) * .05 };
            }
        }

        /// <summary>Where they start, spread over the foot of <paramref name="area"/>, and how far they rise.</summary>
        public void Place(Rect area, double rise)
        {
            _rise = rise;
            for (int i = 0; i < _b.Length; i++)
            {
                double f = (i * .618034 + .13) % 1;
                _b[i].X = area.X + area.Width * (.08 + .84 * f) - _b[i].R;
                _b[i].Y = area.Bottom - 4 - ((i * 7) % 18) - _b[i].R;
            }
        }

        public void Step(double p)
        {
            bool on = p is > 0 and < 1;
            Layer.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            if (!on) return;
            for (int i = 0; i < _b.Length; i++)
            {
                var b = _b[i];
                double q = Seg(p, b.Start, b.Start + .62);
                if (q is <= 0 or >= 1) { b.El.Opacity = 0; continue; }
                b.T.X = b.X + 2.2 * Math.Sin(q * Math.PI * 3 + i);
                b.T.Y = b.Y - _rise * (1 - Math.Pow(1 - q, 1.6));
                b.El.Opacity = q < .15 ? q / .15 : 1 - Math.Pow(Seg(q, .5, 1), 1.4);
            }
        }
    }

    // ================================================================ the big moments' art (drawn once, then only moved)

    /// <summary>The wave that sweeps the meter: this wide, its face at the right.</summary>
    internal const double BigWaveW = 96;

    /// <summary>
    /// A wall of water as tall as <paramref name="h"/>: clear at its back, deepening aqua toward its face, the face a bright
    /// curving line of foam from the crest down, the crest's lip pitching forward at the top over a hollow, streaks of foam
    /// running down the face and spray off the lip. Drawn once per height; the sweep only moves it.
    /// </summary>
    static void DrawBigWave(DrawingContext dc, double h)
    {
        double w = BigWaveW, face = w - 16;
        double F(double y) => face - 5 * Math.Sin(Math.PI * Math.Clamp(y / h, 0, 1)) + 3 * (y / h);   // the face bows back at its middle
        var body = new StreamGeometry();
        using (var c = body.Open())
        {
            c.BeginFigure(new Point(0, h), true, true);
            c.LineTo(new Point(0, 14), true, false);
            c.BezierTo(new Point(w * .4, 12), new Point(face - 10, 2), new Point(face + 6, 2.5), true, true);       // up the back to the crest
            c.BezierTo(new Point(face + 14, 3), new Point(face + 15, 12), new Point(face + 8, 13), true, true);     // the lip pitching forward and down
            c.BezierTo(new Point(face + 3, 13.5), new Point(face + 2, 10), new Point(F(16), 16), true, true);        // the hollow
            for (double y = 20; y <= h; y += 6) c.LineTo(new Point(F(y), y), true, true);
        }
        body.Freeze();
        dc.DrawGeometry(Mod.Grad(false, (Mod.A(SeaTintC, 0), 0), (Mod.A(SeaTintC, .3), .3), (Mod.A(SeaTintC, .7), .55), (Mod.A(Mod.Mix(AquaC, FoamC, .3), .85), .85), (Mod.A(FoamC, .92), 1)), null, body);
        // the face: a line of foam from the lip down, brightest at the top
        var faceLine = new StreamGeometry();
        using (var c = faceLine.Open())
        {
            c.BeginFigure(new Point(face + 6, 2.5), false, false);
            c.BezierTo(new Point(face + 14, 3), new Point(face + 15, 12), new Point(face + 8, 13), true, true);
            c.BezierTo(new Point(face + 3, 13.5), new Point(face + 2, 10), new Point(F(16), 16), true, true);
            for (double y = 20; y <= h; y += 6) c.LineTo(new Point(F(y), y), true, true);
        }
        faceLine.Freeze();
        var pen = new Pen(Mod.Grad(true, (Mod.A(FoamC, 1), 0), (Mod.A(FoamC, .85), .3), (Mod.A(FoamC, .5), 1)), 2.8) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round };
        pen.Freeze();
        var soft = new Pen(Mod.Grad(true, (Mod.A(FoamC, .6), 0), (Mod.A(FoamC, .32), .3), (Mod.A(FoamC, .16), 1)), 10) { LineJoin = PenLineJoin.Round };
        soft.Freeze();
        dc.DrawGeometry(null, soft, faceLine);
        dc.DrawGeometry(null, pen, faceLine);
        // streaks of foam running down the face, and spray off the lip
        var streak = Mod.P(Mod.Br(FoamC, .6), 1.2);
        for (int i = 0; i < 6; i++)
        {
            double y = 22 + (h - 30) * ((i * .618 + .2) % 1), len = 8 + 10 * ((i * .38 + .5) % 1);
            dc.DrawLine(streak, new Point(F(y) - 4 - (i % 3) * 3, y), new Point(F(y + len) - 4 - (i % 3) * 3, y + len));
        }
        (double X, double Y, double R)[] spray = { (face + 17, 4, 1.3), (face + 13, -1, 1), (face + 20, 9, .9), (face + 9, 0, .8), (face + 16, -2.5, .7), (face + 21, 2, .7) };
        foreach (var (x, y, r) in spray) dc.DrawEllipse(Mod.Br(FoamC, .85), null, new Point(x, Math.Max(r, y + 2)), r, r);
        // the head of foam heaped on the crest
        var blob = new RadialGradientBrush();
        blob.GradientStops.Add(new GradientStop(Mod.A(Colors.White, .98), 0));
        blob.GradientStops.Add(new GradientStop(Mod.A(FoamC, .8), .6));
        blob.GradientStops.Add(new GradientStop(Mod.A(AquaC, 0), 1));
        blob.Freeze();
        foreach (var (x, y, r) in new[] { (face - 6, 5.0, 5.0), (face + 2, 4.0, 5.5), (face + 9, 6.5, 4.5), (face + 12, 11.0, 3.5), (face - 14, 7.0, 3.5), (face + 4, 16.0, 3.0) })
            dc.DrawEllipse(blob, null, new Point(x, y), r, r * .85);
    }

    /// <summary>A drop of water: a small bright bead with a glint, drawn once into a brush every drop wears.</summary>
    static readonly DrawingBrush DropBrush = MakeDrop();

    static DrawingBrush MakeDrop()
    {
        var g = new DrawingGroup();
        using (var dc = g.Open())
        {
            var body = new RadialGradientBrush { GradientOrigin = new Point(.35, .35) };
            body.GradientStops.Add(new GradientStop(Mod.A(FoamC, 1), 0));
            body.GradientStops.Add(new GradientStop(Mod.A(AquaC, .85), .6));
            body.GradientStops.Add(new GradientStop(Mod.A(SeaTintC, .5), 1));
            body.Freeze();
            dc.DrawEllipse(body, null, new Point(10, 10), 9, 9);
        }
        g.Freeze();
        var b = new DrawingBrush(g) { Stretch = Stretch.Uniform, Viewbox = new Rect(0, 0, 20, 20), ViewboxUnits = BrushMappingMode.Absolute };
        b.Freeze();
        return b;
    }

    /// <summary>
    /// A splash: a crown of drops thrown up and out from a point that fall back as they fade (at most twelve), each placed by its
    /// number so a held frame draws them the same each time; only their translation and opacity move.
    /// </summary>
    internal sealed class SplashSet
    {
        public readonly Canvas Layer = new() { IsHitTestVisible = false, Visibility = Visibility.Collapsed };
        readonly (System.Windows.Shapes.Rectangle El, TranslateTransform T, double Vx, double Vy, double R)[] _d;
        double _x, _y;

        public SplashSet(int n = 12, double reach = 1)
        {
            _d = new (System.Windows.Shapes.Rectangle, TranslateTransform, double, double, double)[Math.Min(n, 12)];
            for (int i = 0; i < _d.Length; i++)
            {
                double f = (i + .5) / _d.Length, ang = Math.PI * (1.08 + .84 * f) + .12 * Math.Sin(i * 2.3);   // a fan opening upward
                double v = (26 + 16 * ((i * .618 + .3) % 1)) * reach, r = 1.7 + 1.6 * ((i * .38 + .2) % 1);
                var t = new TranslateTransform();
                var el = new System.Windows.Shapes.Rectangle { Width = 2 * r, Height = 2 * r, Fill = DropBrush, RenderTransform = t, IsHitTestVisible = false };
                Layer.Children.Add(el);
                _d[i] = (el, t, 1.25 * v * Math.Cos(ang), .7 * v * Math.Sin(ang), r);   // wider than tall: it stays inside the glass
            }
        }

        /// <summary>Where the splash bursts from (set once at the moment's start).</summary>
        public void At(double x, double y) { _x = x; _y = y; }

        public void Step(double p)
        {
            bool on = p is > 0 and < 1;
            Layer.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            if (!on) return;
            double q = Out(p);
            foreach (var (el, t, vx, vy, r) in _d)
            {
                t.X = _x + vx * q - r;
                t.Y = _y + vy * q + 24 * p * p - r;   // thrown up, falling back
                el.Opacity = p < .1 ? p / .1 : 1 - Math.Pow(Seg(p, .45, 1), 1.3);
            }
        }
    }

    /// <summary>A whirlpool seen from above at a slant: three arms of light spiralling in to a bright eye, drawn once.</summary>
    static readonly DrawingBrush WhirlBrush = MakeWhirl();

    static DrawingBrush MakeWhirl()
    {
        var g = new DrawingGroup();
        using (var dc = g.Open())
        {
            var eye = new RadialGradientBrush();
            eye.GradientStops.Add(new GradientStop(Mod.A(FoamC, .55), 0));
            eye.GradientStops.Add(new GradientStop(Mod.A(AquaC, .12), .35));
            eye.GradientStops.Add(new GradientStop(Mod.A(AquaC, 0), 1));
            eye.Freeze();
            dc.DrawEllipse(eye, null, new Point(50, 50), 50, 50);
            var deep = new RadialGradientBrush();
            deep.GradientStops.Add(new GradientStop(Mod.A(DarkC, .85), 0));
            deep.GradientStops.Add(new GradientStop(Mod.A(DarkC, .4), .6));
            deep.GradientStops.Add(new GradientStop(Mod.A(DarkC, 0), 1));
            deep.Freeze();
            dc.DrawEllipse(deep, null, new Point(50, 50), 16, 16);
            for (int arm = 0; arm < 4; arm++)
                for (int k = 0; k < 36; k++)
                {
                    double t0 = k / 36.0, t1 = (k + 1) / 36.0;
                    Point P(double t) { double ang = arm * Math.PI / 2 + t * 2.4 * Math.PI, r = 7 + 42 * Math.Pow(t, 1.1); return new(50 + r * Math.Cos(ang), 50 + r * Math.Sin(ang)); }
                    double fade = t0 < .12 ? t0 / .12 : 1 - Math.Pow(t0, 1.4);
                    var pen = new Pen(Mod.Br(t0 < .45 ? FoamC : AquaC, .95 * fade), 4.2 - 2.6 * t0) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                    pen.Freeze();
                    dc.DrawLine(pen, P(t0), P(t1));
                }
        }
        g.Freeze();
        var b = new DrawingBrush(g) { Stretch = Stretch.Uniform, Viewbox = new Rect(0, 0, 100, 100), ViewboxUnits = BrushMappingMode.Absolute };
        b.Freeze();
        return b;
    }

    // ================================================================ small parts

    /// <summary>The tight dark shade under words over the water (one effect for a group of words).</summary>
    static DropShadowEffect Shade() => new() { Color = DarkC, Direction = 270, ShadowDepth = 1, BlurRadius = 3, Opacity = .95 };

    /// <summary>The faint aqua halo round a big white figure (the hour, the clock, the card's DPS): one effect on a few texts.</summary>
    static DropShadowEffect Halo(double blur = 9, double a = .7) => new() { Color = AquaC, ShadowDepth = 0, BlurRadius = blur, Opacity = a };

    /// <summary>Words with the tight shade under them.</summary>
    static T Lit<T>(T el) where T : FrameworkElement
    {
        el.Effect = Shade();
        return el;
    }

    /// <summary>A small sea-glass chip with an aqua hairline (★ DPS, the heal mark, AVG, RARE DROP); <paramref name="filled"/>: foam white with abyss words (YOU).</summary>
    static System.Windows.Controls.Border Mark(string s, Brush? fg = null, Color? edge = null, bool filled = false, double size = 8.5)
    {
        var t = Tt(s, size, filled ? Frozen(DarkC) : fg ?? White, FontWeights.Bold);
        return new System.Windows.Controls.Border
        {
            Child = t, CornerRadius = new CornerRadius(4), Padding = new Thickness(4, 0, 4, 1), VerticalAlignment = VerticalAlignment.Center,
            Background = filled ? Frozen(FoamC) : Frozen(Mod.A(Mod.Mix(AbyssC, edge ?? AquaC, .16), .86)), BorderThickness = new Thickness(1),
            BorderBrush = filled ? Frozen(FoamC) : Frozen(Mod.A(edge ?? AquaC, edge == null ? .5 : .7)),
        };
    }

    /// <summary>The sea line across the glass (the foot's, the card's): a long low swell of aqua light, fading at its ends.</summary>
    static FrameworkElement SeaLine(Thickness m, double a = 1) => new TacSketch((dc, s) =>
    {
        if (s.Width < 8) return;
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(0, 2), false, false);
            for (double x = 2; x <= s.Width; x += 2) c.LineTo(new Point(x, 2 + 1.1 * Math.Sin(2 * Math.PI * x / 46)), true, true);
        }
        g.Freeze();
        var pen = new Pen(Mod.Grad(false, (Mod.A(AquaC, .05 * a), 0), (Mod.A(AquaC, .4 * a), .5), (Mod.A(AquaC, .05 * a), 1)), 1);
        pen.Freeze();
        dc.DrawGeometry(null, pen, g);
    }) { Height = 4, Margin = m };

    /// <summary>The pickup strip's plain tiles rimmed in sea glass on a deep-water ground (a grade's own colour kept); the best drop's star stays gold.</summary>
    static T SeaRims<T>(T strip) where T : FrameworkElement
    {
        var rim = Frozen(Mod.A(AquaC, .6));
        var ground = Frozen(Mod.A(Mod.Mix(AbyssC, SeaC, .6), .9));
        void Walk(DependencyObject d)
        {
            if (d is System.Windows.Controls.Border { CornerRadius.TopLeft: 5 } box && ReferenceEquals(box.BorderBrush, SlotFrame))
            {
                box.BorderBrush = rim; box.Background = ground;
                return;
            }
            if (d is System.Windows.Controls.Panel p) foreach (var c in p.Children.OfType<DependencyObject>()) Walk(c);
            else if (d is System.Windows.Controls.Decorator dec && dec.Child != null) Walk(dec.Child);
        }
        Walk(strip);
        return strip;
    }
}
