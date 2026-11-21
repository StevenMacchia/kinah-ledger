using System.Windows;
using System.Windows.Media;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

/// <summary>
/// Asmodian's war-gear silhouette (round 2, Steven: "okay at best ... my bar is the HIGHEST"): the plates no longer sit as
/// rectangles. Demon horns break out of their top corners and curl back in, a crest of thorns rises from the top edge,
/// fangs hang from the foot and thorns jut from the sides; behind the fight's title a blood moon glows with a crescent
/// sigil in rune rings. All of it is drawn once with its plate (the plate's sketch draws again only on a resize), so it
/// costs nothing per frame. The room round each plate (<see cref="FightRoom"/> and friends) holds the art inside the
/// window: nothing spills.
/// </summary>
sealed partial class HudAsmodian
{
    /// <summary>The room round the war plates: the top for the horns and the crest, the sides for the horns' sweep, the foot for the fangs.</summary>
    static readonly Thickness FightRoom = new(13, 35, 13, 12), LineRoom = new(12, 22, 12, 9), CardRoom = new(13, 31, 13, 11);

    static Point Bez(Point a, Point b, Point c, Point d, double t)
    {
        double u = 1 - t;
        return new Point(u * u * u * a.X + 3 * u * u * t * b.X + 3 * u * t * t * c.X + t * t * t * d.X, u * u * u * a.Y + 3 * u * u * t * b.Y + 3 * u * t * t * c.Y + t * t * t * d.Y);
    }

    static readonly Brush HornFill = Mod.Grad(true, (Color.FromRgb(0x3A, 0x0A, 0x0A), 0), (Color.FromRgb(0x1A, 0x06, 0x06), 1));
    static readonly Pen HornGlow = Bevel(Color.FromArgb(0x80, 0xE0, 0x1C, 0x22), 7), HornOutline = Bevel(Colors.Black, 4.2);

    /// <summary>A pen whose joins are bevelled: a sharp tip's miter would reach far past the art (and the window).</summary>
    static Pen Bevel(Color c, double w) { var p = new Pen(Frozen(c), w) { LineJoin = PenLineJoin.Bevel }; p.Freeze(); return p; }
    static readonly Pen HornRim = Bevel(Color.FromRgb(0xFF, 0x3B, 0x2F), 2);
    static readonly Pen HornEdge = Mod.P(Frozen(Color.FromArgb(0xE0, 0xFF, 0xC0, 0x90)), 1);
    static readonly Pen HornRidge = Mod.P(Frozen(Color.FromArgb(0xE0, 0x05, 0x02, 0x03)), 1.4), HornRidgeLit = Mod.P(Frozen(Color.FromArgb(0xA0, 0xFF, 0x4A, 0x3D)), .8);
    static readonly Brush TipGlow = MakeRadial(Color.FromArgb(0xC0, 0xFF, 0x9A, 0x5A), Color.FromArgb(0, 0xFF, 0x4A, 0x3D));
    static readonly Pen SpikeOutline = Bevel(Colors.Black, 2.6), SpikeRim = Bevel(Color.FromRgb(0xFF, 0x3B, 0x2F), 1.1);
    static readonly Brush SpikeTip = MakeRadial(Color.FromRgb(0xFF, 0xF0, 0xD0), Color.FromArgb(0, 0xFF, 0x6A, 0x4A));
    static readonly Brush SpikeFill = Mod.Grad(true, (Color.FromRgb(0x3A, 0x0A, 0x0A), 0), (Color.FromRgb(0x1A, 0x06, 0x06), 1));

    /// <summary>
    /// A demon horn in the box <paramref name="box"/> (its base along the box's foot): it sweeps out and up from its base and
    /// curls its tip back in; ridged, rimmed in crimson, its tip smouldering. <paramref name="right"/>: the mirror (the right corner's).
    /// </summary>
    static void Horn(DrawingContext dc, Rect box, bool right)
    {
        Point P(double x, double y) => new(box.Left + (right ? 1 - x : x) * box.Width, box.Top + y * box.Height);
        Point o0 = P(.36, 1), o1 = P(.3, .4), o2 = P(.12, .1), tip = P(.03, .05);
        Point i1 = P(.36, .34), i2 = P(.68, .48), i3 = P(.95, 1);
        var g = new StreamGeometry();
        using (var x = g.Open())
        {
            x.BeginFigure(o0, true, true);
            x.BezierTo(o1, o2, tip, true, true);
            x.BezierTo(i1, i2, i3, true, true);
        }
        g.Freeze();
        // the glow outside it, a black outline (it holds over snow), the iron, the hot rim
        dc.DrawGeometry(null, HornGlow, g); dc.DrawGeometry(null, HornOutline, g);
        dc.DrawGeometry(HornFill, HornRim, g);
        // a hot light along the outer sweep
        var lit = new StreamGeometry();
        using (var x = lit.Open()) { x.BeginFigure(Bez(o0, o1, o2, tip, .12), false, false); x.BezierTo(o1 + new Vector(right ? -1 : 1, 0), o2 + new Vector(right ? -1 : 1, .6), tip, true, true); }
        lit.Freeze();
        dc.DrawGeometry(null, HornEdge, lit);
        // the ridges: rings across the horn, dark with a lit edge
        foreach (var t in new[] { .3, .5, .68, .82 })
        {
            var a = Bez(o0, o1, o2, tip, t); var b = Bez(tip, i1, i2, i3, 1 - t);
            dc.DrawLine(HornRidge, a, b);
            dc.DrawLine(HornRidgeLit, a + new Vector(0, 1.1), b + new Vector(0, 1.1));
        }
        dc.DrawEllipse(TipGlow, null, tip, 3.5, 3.5);
    }

    /// <summary>A row of <paramref name="n"/> thorns along a plate's edge from <paramref name="a"/> to <paramref name="b"/>, pointing along <paramref name="out"/>; their lengths from <paramref name="lens"/>.</summary>
    static void Spikes(DrawingContext dc, Point a, Point b, Vector @out, params double[] lens)
    {
        int n = lens.Length;
        var along = b - a; double step = along.Length / n; along.Normalize();
        for (int i = 0; i < n; i++)
        {
            var c = a + along * (step * (i + .5));
            double half = Math.Min(step * .42, 2.2 + lens[i] * .22);
            var g = new StreamGeometry();
            using (var x = g.Open())
            {
                x.BeginFigure(c - along * half - @out * 1.5, true, true);
                x.LineTo(c + @out * lens[i], true, false);
                x.LineTo(c + along * half - @out * 1.5, true, false);
            }
            g.Freeze();
            dc.DrawGeometry(null, SpikeOutline, g);
            dc.DrawGeometry(SpikeFill, SpikeRim, g);
            if (lens[i] >= 6) dc.DrawEllipse(SpikeTip, null, c + @out * (lens[i] - 1.2), 2, 2);
        }
    }

    static readonly Brush MoonGlow = MakeRadial(Color.FromArgb(0xD0, 0xF0, 0x2A, 0x34), Color.FromArgb(0, 0x8E, 0x0F, 0x1C), Color.FromArgb(0x70, 0xC2, 0x1F, 0x2E));
    static readonly Pen RuneRing = Mod.P(Frozen(Color.FromArgb(0xD8, 0xFF, 0x46, 0x40)), 2), RuneRing2 = Mod.P(Frozen(Color.FromArgb(0x80, 0xC2, 0x1F, 0x2E)), .9), RuneTick = Mod.P(Frozen(Color.FromArgb(0xA0, 0xFF, 0x6A, 0x4A)), 1.2);
    static readonly Brush CrescentFill = Mod.Grad(true, (Color.FromArgb(0xC0, 0xFF, 0x6A, 0x50), 0), (Color.FromArgb(0x70, 0xA8, 0x14, 0x22), 1));

    /// <summary>
    /// The blood moon behind the fight's title: a crimson glow, two rune rings with notched runes between them, and at its
    /// heart a horned crescent (the shadow faction's mark, as this look draws it). Low enough that the title reads over it.
    /// </summary>
    static void BloodMoon(DrawingContext dc, Point c, double r)
    {
        dc.DrawEllipse(MoonGlow, null, c, r * 1.35, r * 1.35);
        dc.DrawEllipse(null, RuneRing, c, r * .84, r * .84);
        dc.DrawEllipse(null, RuneRing2, c, r * .66, r * .66);
        for (int k = 0; k < 16; k++)
        {
            double a = Math.PI * 2 * k / 16, l = k % 4 == 0 ? .16 : k % 2 == 0 ? .1 : .06;
            var d = new Vector(Math.Sin(a), -Math.Cos(a));
            dc.DrawLine(RuneTick, c + d * r * .68, c + d * r * (.68 + l));
        }
        var crescent = new CombinedGeometry(GeometryCombineMode.Exclude, new EllipseGeometry(c, r * .44, r * .44), new EllipseGeometry(c + new Vector(r * .2, -r * .14), r * .38, r * .38));
        crescent.Freeze();
        dc.DrawGeometry(CrescentFill, null, crescent);
        // two horns rising off the crescent's tips
        Spikes(dc, c + new Vector(-r * .34, -r * .3), c + new Vector(-r * .14, -r * .42), new Vector(-.45, -.9), r * .32);
        Spikes(dc, c + new Vector(r * .1, r * .36), c + new Vector(r * .34, r * .26), new Vector(.5, .86), r * .22);
    }

    /// <summary>
    /// A war plate: the horns behind it at its top corners (their bases under the iron), the iron plate (<see cref="Iron"/>)
    /// in <paramref name="room"/>, a crest of thorns rising from its top edge's middle, fangs hanging from its foot, thorns
    /// off its sides, and (<paramref name="moon"/>) the blood moon inside it at that point. Drawn once per size.
    /// </summary>
    static void WarPlate(DrawingContext dc, Size s, double opacity, Thickness room, double big, double horn, Point? moon = null, double moonR = 26, Color? edge = null, bool crest = true, bool fangs = true)
    {
        var r = new Rect(room.Left, room.Top, s.Width - room.Left - room.Right, s.Height - room.Top - room.Bottom);
        if (r.Width < 40 || r.Height < 12) return;
        // the horns: their box's foot a few px under the plate's top, their outer sweep into the side room
        // the box's foot a few px under the plate's top (the base hidden by the iron); its outer edge past the corner, in the side room
        // (4 px clear of the room's edges: the tip's glow and the rim stay inside)
        double hh = Math.Min(horn, room.Top), hw = hh * .95;
        Horn(dc, new Rect(r.Left - room.Left + 4, r.Top + 4 - hh, hw, hh), false);
        Horn(dc, new Rect(r.Right + room.Right - 4 - hw, r.Top + 4 - hh, hw, hh), true);
        Iron(dc, s, opacity, 0, edge, big, 3, true, room: room);
        if (moon is { } m)
        {
            dc.PushClip(Plate(r, big, 3));
            BloodMoon(dc, new Point(r.Left + m.X, r.Top + m.Y), moonR);
            dc.Pop();
        }
        double cx = r.Left + r.Width / 2;
        if (crest)
        {
            // the crest: thorns rising from the top edge, tallest in the middle
            // seven thorns, uneven, the middle one tallest (26 px at most)
            double top = Math.Min(room.Top - 2.5, 26);
            Spikes(dc, new Point(cx - 46, r.Top + 1), new Point(cx + 46, r.Top + 1), new Vector(0, -1), top * .46, top * .62, top * .8, top, top * .74, top * .55, top * .4);
        }
        // fangs from the foot, thorns off the sides
        // fangs: one at each bottom corner, one in the middle
        double foot = Math.Min(room.Bottom - 2.5, 10);
        if (fangs)
        {
            Spikes(dc, new Point(cx - 6, r.Bottom - 1), new Point(cx + 6, r.Bottom - 1), new Vector(0, 1), foot);
            Spikes(dc, new Point(r.Left + 14, r.Bottom - 1), new Point(r.Left + 24, r.Bottom - 1), new Vector(-.25, 1), foot * .9);
            Spikes(dc, new Point(r.Right - 30, r.Bottom - 1), new Point(r.Right - 20, r.Bottom - 1), new Vector(.25, 1), foot * .9);
        }
        double side = Math.Min(room.Left - 1.5, 7);
        if (r.Height > 30)
        {
            Spikes(dc, new Point(r.Left + 1, r.Top + r.Height * .45), new Point(r.Left + 1, r.Top + r.Height * .75), new Vector(-1, 0), side, side * .65);
            Spikes(dc, new Point(r.Right - 1, r.Top + r.Height * .45), new Point(r.Right - 1, r.Top + r.Height * .75), new Vector(1, 0), side * .65, side);
        }
        else Spikes(dc, new Point(r.Right - 1, r.Top + r.Height * .3), new Point(r.Right - 1, r.Top + r.Height * .7), new Vector(1, 0), side * .8);
    }
}
