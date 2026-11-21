using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

/// <summary>
/// Asmodian's forge: the parts every surface of the look is made of. Black-iron plates with notched corners (a deep cut at
/// the top-left and bottom-right, a nick at the others), brushed grain in the metal, a crimson edge that glows a little past
/// the plate and a smoulder rising from its foot, a thorn off each deep cut. The blood-red horned crown, the iron chips, and
/// the embers: a few sparks drawn straight into one sketch (no elements, no effects), so a burst costs a few ellipses a frame.
/// </summary>
sealed partial class HudAsmodian
{
    // the palette: black iron, crimson, ember; ash for what is over or paused (expression-bodied: a static field in another
    // part of the class may read them while the statics are still being set, in any order)
    static Color IronC => Color.FromRgb(0x0B, 0x07, 0x08);
    static Color Iron2C => Color.FromRgb(0x17, 0x0C, 0x0E);
    static Color RimC => Color.FromRgb(0x4A, 0x20, 0x26);
    static Color CrimC => Color.FromRgb(0xC2, 0x1F, 0x2E);
    static Color HotC => Color.FromRgb(0xFF, 0x4A, 0x3D);
    static Color EmberC => Color.FromRgb(0xFF, 0x8A, 0x3D);
    static Color PaleC => Color.FromRgb(0xFF, 0xD0, 0x8A);
    static Color BloodC => Color.FromRgb(0x7A, 0x0C, 0x16);
    static Color AshC => Color.FromRgb(0x9A, 0x86, 0x89);
    static Color BoneC => Color.FromRgb(0xF6, 0xEE, 0xEE);

    /// <summary>The room round a plate for its glow and thorns (drawn inside the plate's own element: nothing spills).</summary>
    const double G = 5;

    /// <summary>The look's heavy face: Bahnschrift, condensed for titles.</summary>
    static readonly FontFamily Bahn = HudThemes.Bahn;

    /// <summary>A title in the heavy face: Bahnschrift SemiBold Condensed.</summary>
    static TextBlock Head(string s, double size, Brush fg, FontWeight? w = null)
    {
        var t = Mod.Text(s, size, fg, w ?? FontWeights.SemiBold, Bahn);
        t.FontStretch = FontStretches.Condensed;
        return t;
    }

    /// <summary>A big figure in the heavy face at its normal width (tabular digits).</summary>
    static TextBlock Big(string s, double size, Brush fg, FontWeight? w = null) => Mod.Text(s, size, fg, w ?? FontWeights.SemiBold, Bahn);

    /// <summary>A plate's outline: the top-left and bottom-right corners cut <paramref name="big"/> deep, the other two <paramref name="small"/>.</summary>
    static Geometry Plate(Rect r, double big, double small)
    {
        double b = Math.Max(0, Math.Min(big, Math.Min(r.Width, r.Height) / 2)), s = Math.Max(0, Math.Min(small, Math.Min(r.Width, r.Height) / 2));
        var g = new StreamGeometry();
        using (var x = g.Open())
        {
            x.BeginFigure(new Point(r.Left + b, r.Top), true, true);
            x.LineTo(new Point(r.Right - s, r.Top), true, false);
            x.LineTo(new Point(r.Right, r.Top + s), true, false);
            x.LineTo(new Point(r.Right, r.Bottom - b), true, false);
            x.LineTo(new Point(r.Right - b, r.Bottom), true, false);
            x.LineTo(new Point(r.Left + s, r.Bottom), true, false);
            x.LineTo(new Point(r.Left, r.Bottom - s), true, false);
            x.LineTo(new Point(r.Left, r.Top + b), true, false);
        }
        g.Freeze();
        return g;
    }

    /// <summary>The grain in the iron: faint horizontal streaks, light and dark, made once.</summary>
    static readonly ImageBrush Grain = MakeGrain();

    static ImageBrush MakeGrain()
    {
        const int w = 160, h = 48;
        var px = new byte[w * h * 4];
        var rnd = new Random(666);
        for (int y = 0; y < h; y++)
        {
            // each line a streak of its own tone, broken along its length
            double line = rnd.NextDouble();
            for (int x = 0; x < w; x++)
            {
                double v = line * .7 + rnd.NextDouble() * .3;
                bool light = v > .62;
                byte a = (byte)(light ? (v - .62) * 34 : (.62 - v) * 30);
                byte c = light ? (byte)255 : (byte)0;
                int i = (y * w + x) * 4;
                px[i] = px[i + 1] = px[i + 2] = (byte)(c * a / 255); px[i + 3] = a;
            }
        }
        var bmp = System.Windows.Media.Imaging.BitmapSource.Create(w, h, 96, 96, PixelFormats.Pbgra32, null, px, w * 4);
        bmp.Freeze();
        var b = new ImageBrush(bmp) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, w, h), ViewportUnits = BrushMappingMode.Absolute, Stretch = Stretch.None };
        b.Freeze();
        return b;
    }

    /// <summary>
    /// A black-iron plate filling <paramref name="s"/> less <see cref="G"/> round it: the edge's glow outside it (in the G room),
    /// the iron (darker at its foot), the grain, a smoulder rising from its foot (<paramref name="heat"/> 0..1 more), a light
    /// along its top, the crimson edge (<paramref name="edge"/>: another colour for a rare drop, ash while paused) and a thorn
    /// off each deep cut. <paramref name="sheen"/> 0..1: a band of ember light crossing it (a pickup).
    /// </summary>
    static void Iron(DrawingContext dc, Size s, double opacity, double heat = 0, Color? edge = null, double big = 9, double small = 3, bool thorns = true, double sheen = -1, double g = G)
    {
        if (s.Width < 2 * g + 6 || s.Height < 2 * g + 6) return;
        var r = new Rect(g, g, s.Width - 2 * g, s.Height - 2 * g);
        var shape = Plate(r, big, small);
        var ec = edge ?? CrimC;
        // the glow: rings of the edge's colour outside the plate (their inner half lies under the iron)
        for (int k = 3; k >= 1; k--)
            dc.DrawGeometry(null, Mod.P(Mod.Br(ec, (k == 1 ? .24 : k == 2 ? .11 : .05) * (1 + 1.4 * heat)), 2 * k * Math.Min(1, g / 5)), shape);
        double a = Math.Clamp(.66 + .3 * opacity, .72, .95);
        dc.DrawGeometry(Mod.Grad(true, (Mod.A(Iron2C, a), 0), (Mod.A(IronC, Math.Min(.97, a + .04)), 1)), null, shape);
        dc.PushClip(shape);
        dc.PushOpacity(.9);
        dc.DrawRectangle(Grain, null, r);
        dc.Pop();
        // the smoulder: crimson light rising from the foot, and a breath of it at the left where the edge is hottest
        double sm = .15 + .3 * heat;
        dc.DrawRectangle(Mod.Grad(true, (Mod.A(ec, 0), 0), (Mod.A(ec, 0), .42), (Mod.A(ec, sm), 1)), null, r);
        dc.DrawRectangle(Mod.Grad(false, (Mod.A(ec, .08 + .12 * heat), 0), (Mod.A(ec, 0), .35)), null, r);
        if (sheen is > 0 and < 1)
        {
            double x = r.Left - 70 + (r.Width + 140) * sheen;
            var band = new StreamGeometry();
            using (var q = band.Open()) { q.BeginFigure(new Point(x, r.Top), true, true); q.LineTo(new Point(x + 40, r.Top), true, false); q.LineTo(new Point(x + 14, r.Bottom), true, false); q.LineTo(new Point(x - 26, r.Bottom), true, false); }
            band.Freeze();
            dc.DrawGeometry(Mod.Grad(false, (Mod.A(EmberC, 0), 0), (Mod.A(PaleC, .2), .55), (Mod.A(EmberC, 0), 1)), null, band);
        }
        // the light along the top: the iron's sheen, warm at the left
        dc.DrawRectangle(Mod.Grad(false, (Mod.A(HotC, .55), 0), (Mod.A(Colors.White, .1), .3), (Mod.A(Colors.White, .03), 1)), null, new Rect(r.Left + big, r.Top + 1, Math.Max(0, r.Width - big - small), 1));
        dc.Pop();
        // the edge: hot at the top-left, cooling to the bottom-right
        var ink = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
        ink.GradientStops.Add(new GradientStop(Mod.Mix(ec, HotC, edge == null ? .7 : .2), 0));
        ink.GradientStops.Add(new GradientStop(Mod.A(ec, .9), .4));
        ink.GradientStops.Add(new GradientStop(Mod.A(ec, .6), 1));
        ink.Freeze();
        dc.DrawGeometry(null, Mod.P(ink, 1.1), shape);
        if (thorns && g >= 3) Thorns(dc, r, big, ec);
    }

    /// <summary>A thorn off each deep cut, pointing out from the plate (inside the G room), and a rivet in each nicked corner.</summary>
    static void Thorns(DrawingContext dc, Rect r, double big, Color ec)
    {
        double b = Math.Min(big, Math.Min(r.Width, r.Height) / 2);
        if (b < 4) return;
        var fill = Mod.Grad(true, (Mod.Mix(ec, PaleC, .25), 0), (Mod.Mix(ec, BloodC, .3), 1));
        void Thorn(Point m, double dx, double dy)
        {
            // a narrow spike: its base along the cut, its tip out along the cut's normal
            const double half = 2.7, len = 6.4;
            var g = new StreamGeometry();
            using (var x = g.Open())
            {
                x.BeginFigure(new Point(m.X - dy * half, m.Y + dx * half), true, true);
                x.LineTo(new Point(m.X + dx * len, m.Y + dy * len), true, false);
                x.LineTo(new Point(m.X + dy * half, m.Y - dx * half), true, false);
            }
            g.Freeze();
            dc.DrawGeometry(fill, null, g);
        }
        const double n = .7071;
        Thorn(new Point(r.Left + b / 2, r.Top + b / 2), -n, -n);
        Thorn(new Point(r.Right - b / 2, r.Bottom - b / 2), n, n);
    }

    /// <summary>A small iron chip (a mark, a timer, the outcome): a notched plate, its edge, its words. <paramref name="filled"/>: a crimson ingot (YOU).</summary>
    static Grid Chip(string s, Brush ink, Color edge, bool filled = false, double size = 8.5, FontFamily? face = null, Thickness? pad = null, double cut = 3)
    {
        var t = Mod.Text(s, size, ink, FontWeights.Bold, face);
        t.Margin = pad ?? new Thickness(5, 1, 5, 1.5);
        var g = new Grid { VerticalAlignment = VerticalAlignment.Center };
        g.Children.Add(new TacSketch((dc, sz) =>
        {
            if (sz.Width < 4 || sz.Height < 4) return;
            var shape = Plate(new Rect(.5, .5, sz.Width - 1, sz.Height - 1), cut, 0);
            if (filled) dc.DrawGeometry(Mod.Grad(true, (HotC, 0), (CrimC, .55), (BloodC, 1)), Mod.P(Mod.Br(PaleC, .55), 1), shape);
            else dc.DrawGeometry(Mod.Grad(true, (Mod.A(Iron2C, .95), 0), (Mod.A(IronC, .95), 1)), Mod.P(Mod.Br(edge, .85), 1), shape);
        }));
        g.Children.Add(t);
        return g;
    }

    /// <summary>A rule across a plate: crimson fading out at both ends, a small diamond at its middle.</summary>
    static FrameworkElement Rule(Thickness m, bool diamond = true) => new TacSketch((dc, s) =>
    {
        double y = Math.Round(s.Height / 2) + .5;
        dc.DrawRectangle(Mod.Grad(false, (Mod.A(CrimC, 0), 0), (Mod.A(CrimC, .7), .5), (Mod.A(CrimC, 0), 1)), null, new Rect(0, y - .5, s.Width, 1));
        if (!diamond) return;
        double cx = Math.Round(s.Width / 2);
        var d = new StreamGeometry();
        using (var x = d.Open()) { x.BeginFigure(new Point(cx, y - 3.5), true, true); x.LineTo(new Point(cx + 4, y), true, false); x.LineTo(new Point(cx, y + 3.5), true, false); x.LineTo(new Point(cx - 4, y), true, false); }
        d.Freeze();
        dc.DrawGeometry(Mod.Br(HotC), Mod.P(Mod.Br(IronC), 1), d);
    }) { Height = 7, Margin = m };

    // ================================================================ the crown

    /// <summary>The horned crown's outline in a 16 x 13 box: two horns curling out, three spikes, a band.</summary>
    static readonly Geometry CrownShape = MakeCrown();

    static Geometry MakeCrown()
    {
        var g = Geometry.Parse("M 1.6,13 L 1.6,9.2 C 1.2,7 0.2,4.6 0,1.2 C 1.8,3.4 3.4,5 4.6,6.6 L 8,0 L 11.4,6.6 C 12.6,5 14.2,3.4 16,1.2 C 15.8,4.6 14.8,7 14.4,9.2 L 14.4,13 Z");
        g.Freeze();
        return g;
    }

    static readonly Brush CrownFill = Mod.Grad(true, (Color.FromRgb(0xFF, 0x6A, 0x52), 0), (Color.FromRgb(0xD0, 0x1E, 0x2C), .45), (Color.FromRgb(0x6A, 0x08, 0x12), 1));
    static readonly Pen CrownPen = Mod.P(Mod.Br(Color.FromRgb(0x24, 0x04, 0x08)), .9);

    /// <summary>The crown drawn at <paramref name="at"/> (its box's top-left), <paramref name="k"/> times its size, with its gem and band light.</summary>
    static void Crown(DrawingContext dc, Point at, double k = 1)
    {
        dc.PushTransform(new MatrixTransform(k, 0, 0, k, at.X, at.Y));
        // a blood glow behind it
        dc.DrawEllipse(BloodGlow, null, new Point(8, 8), 11, 9);
        dc.DrawGeometry(CrownFill, CrownPen, CrownShape);
        dc.DrawRectangle(Mod.Br(Color.FromRgb(0x3A, 0x05, 0x0B), .9), null, new Rect(1.6, 9.6, 12.8, 1));
        dc.DrawRectangle(Mod.Br(PaleC, .55), null, new Rect(2.2, 9.2, 11.6, .6));
        dc.DrawEllipse(Mod.Br(PaleC), null, new Point(8, 11.3), 1.2, 1.2);
        dc.Pop();
    }

    static readonly Brush BloodGlow = MakeRadial(Color.FromArgb(0x70, 0xC2, 0x1F, 0x2E), Color.FromArgb(0x00, 0xC2, 0x1F, 0x2E));

    static RadialGradientBrush MakeRadial(Color c0, Color c1, Color? mid = null)
    {
        var b = new RadialGradientBrush();
        b.GradientStops.Add(new GradientStop(c0, 0));
        if (mid is { } m) b.GradientStops.Add(new GradientStop(m, .45));
        b.GradientStops.Add(new GradientStop(c1, 1));
        b.Freeze();
        return b;
    }

    // ================================================================ the embers

    static readonly Brush SparkPale = MakeRadial(Color.FromRgb(0xFF, 0xF2, 0xCC), Color.FromArgb(0, 0xFF, 0x4A, 0x3D), Color.FromArgb(0xD0, 0xFF, 0xB2, 0x5A));
    static readonly Brush SparkHot = MakeRadial(Color.FromRgb(0xFF, 0xB0, 0x70), Color.FromArgb(0, 0xC2, 0x1F, 0x2E), Color.FromArgb(0xC0, 0xFF, 0x4A, 0x3D));

    /// <summary>A number from 0 to 1 fixed for a seed (the sparks' courses: the same in every render).</summary>
    static double Hash(int seed, int salt = 0) { uint h = (uint)(seed * 374761393 + salt * 668265263); h = (h ^ (h >> 13)) * 1274126177; return (h ^ (h >> 16)) % 1000 / 1000.0; }

    /// <summary>
    /// A burst of sparks as small elements on a canvas (<see cref="Start"/> lays them once; <see cref="Step"/> moves them by
    /// transform and opacity only, a frozen brush each): each is born a little after the burst starts, rises from its point
    /// on a sway, shrinks, turns from pale gold to crimson and fades. <see cref="Clear"/> takes them away.
    /// </summary>
    sealed class Sparks
    {
        const double D = 12;
        readonly Canvas _layer;
        readonly List<(System.Windows.Shapes.Ellipse El, TranslateTransform T, ScaleTransform S, int Seed, double Rise)> _live = new();
        public Sparks(Canvas layer) => _layer = layer;

        public void Start(IEnumerable<(Point At, int Seed, double Rise)> pts)
        {
            Clear();
            foreach (var (at, seed, rise) in pts.Take(9))
            {
                var t = new TranslateTransform(); var sc = new ScaleTransform(1, 1);
                var el = new System.Windows.Shapes.Ellipse { Width = D, Height = D, Fill = SparkPale, Opacity = 0, IsHitTestVisible = false, RenderTransformOrigin = new Point(.5, .5) };
                el.RenderTransform = new TransformGroup { Children = { sc, t } };
                Canvas.SetLeft(el, at.X - D / 2); Canvas.SetTop(el, at.Y - D / 2);
                _layer.Children.Add(el);
                _live.Add((el, t, sc, seed, rise));
            }
        }

        public void Step(double t)
        {
            foreach (var (el, tr, sc, seed, rise) in _live)
            {
                double born = Hash(seed, 1) * .28, l = (t - born) / .62;
                if (l <= 0 || l >= 1) { el.Opacity = 0; continue; }
                double h2 = Hash(seed, 2), h3 = Hash(seed, 3);
                tr.X = (h2 - .5) * 10 * l + 2.4 * Math.Sin(h3 * 6.28 + l * 7);
                tr.Y = -rise * (.7 + .5 * h3) * Motion.Out(l);
                sc.ScaleX = sc.ScaleY = (2.7 + 2 * h2) * (1 - .45 * l) * 1.7 * 2 / D;
                el.Opacity = l < .12 ? l / .12 : l > .5 ? 1 - (l - .5) / .5 : 1;
                var fill = l < .45 ? SparkPale : SparkHot;
                if (!ReferenceEquals(el.Fill, fill)) el.Fill = fill;
            }
        }

        public void Clear()
        {
            foreach (var s in _live) _layer.Children.Remove(s.El);
            _live.Clear();
        }
    }

    /// <summary>
    /// A plate's heat, drawn once as its own layer over the plate (its opacity animated, never the plate redrawn): the edge's
    /// glow in hot crimson past the plate, a hot smoulder from its foot and a hot edge line.
    /// </summary>
    static void HotEdge(DrawingContext dc, Size s, double big = 9, double small = 3, Color? edge = null, double g = G)
    {
        if (s.Width < 2 * g + 6 || s.Height < 2 * g + 6) return;
        var r = new Rect(g, g, s.Width - 2 * g, s.Height - 2 * g);
        var shape = Plate(r, big, small);
        var ec = edge ?? HotC;
        for (int k = 3; k >= 1; k--) dc.DrawGeometry(null, Mod.P(Mod.Br(ec, k == 1 ? .34 : k == 2 ? .16 : .07), 2 * k * Math.Min(1, g / 5)), shape);
        dc.PushClip(shape);
        dc.DrawRectangle(Mod.Grad(true, (Mod.A(ec, 0), 0), (Mod.A(ec, 0), .45), (Mod.A(ec, .16), 1)), null, r);
        dc.DrawRectangle(Mod.Grad(false, (Mod.A(ec, .1), 0), (Mod.A(ec, 0), .3)), null, r);
        dc.Pop();
        dc.DrawGeometry(null, Mod.P(Mod.Br(Mod.Mix(ec, PaleC, .35)), 1.2), shape);
    }

    /// <summary>A heat layer for a plate (collapsed and clear until a moment lights it).</summary>
    static TacSketch HeatLayer(double big = 9, double small = 3, Color? edge = null, double g = G) =>
        new((dc, s) => HotEdge(dc, s, big, small, edge, g)) { Opacity = 0, Visibility = Visibility.Collapsed };

    /// <summary>A heat layer's strength for a moment (shown while above nothing).</summary>
    static void Heat(UIElement layer, double k)
    {
        layer.Opacity = Math.Clamp(k, 0, 1);
        var v = k > .001 ? Visibility.Visible : Visibility.Collapsed;
        if (layer.Visibility != v) layer.Visibility = v;
    }

    /// <summary>The ember band a pickup sends across a plate: a slanted band of light, drawn once, moved by its transform.</summary>
    static System.Windows.Shapes.Path SheenBand()
    {
        var g = Geometry.Parse("M 26,0 L 66,0 L 40,60 L 0,60 Z"); g.Freeze();
        return new System.Windows.Shapes.Path { Data = g, Fill = SheenFill, IsHitTestVisible = false, Opacity = 0, RenderTransform = new TranslateTransform() };
    }

    static readonly Brush SheenFill = MakeSheen();
    static Brush MakeSheen()
    {
        var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        b.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0xFF, 0x8A, 0x3D), 0));
        b.GradientStops.Add(new GradientStop(Color.FromArgb(0x40, 0xFF, 0xD0, 0x8A), .55));
        b.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0xFF, 0x8A, 0x3D), 1));
        b.Freeze();
        return b;
    }
}
