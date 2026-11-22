using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

/// <summary>
/// Versus's art: the shapes a fighting game's HUD is cut from (slanted slabs, chamfered boxes, hit-spark bursts), its
/// inks, and the slam words its moments throw. Every brush and pen is made once and frozen; a shape is a small frozen
/// geometry; a moment only moves and fades what was drawn.
/// </summary>
sealed partial class HudVersus
{
    // ---- the inks: a fighting game's hot yellow, orange and red over a deep navy-black, chrome outlines, a P1 cyan
    static readonly Color InkC = Color.FromRgb(0x07, 0x08, 0x12), Ink2C = Color.FromRgb(0x16, 0x1A, 0x30), SteelC = Color.FromRgb(0xD8, 0xE0, 0xF0),
        YellowC = Color.FromRgb(0xFF, 0xD8, 0x1F), PaleYC = Color.FromRgb(0xFF, 0xF3, 0x9A), OrangeC = Color.FromRgb(0xFF, 0x8A, 0x0A),
        RedC = Color.FromRgb(0xFF, 0x2E, 0x22), BloodC = Color.FromRgb(0xB0, 0x06, 0x1A), CyanC = Color.FromRgb(0x2E, 0xE0, 0xFF),
        BlueC = Color.FromRgb(0x1F, 0x6B, 0xFF), MintC = Color.FromRgb(0x5D, 0xF2, 0xA6), CoralC = Color.FromRgb(0xFF, 0x70, 0x70), GreyC = Color.FromRgb(0x8A, 0x90, 0xA0);
    static readonly SolidColorBrush White = Frozen(Colors.White), Soft = Frozen(Mod.A(Colors.White, .86)), Quiet = Frozen(Mod.A(Colors.White, .64)),
        Yellow = Frozen(YellowC), PaleY = Frozen(PaleYC), Mint = Frozen(Color.FromRgb(0x8C, 0xFF, 0xC6)), Coral = Frozen(CoralC), Cyan = Frozen(CyanC),
        InkB = Frozen(InkC), Steel = Frozen(SteelC);

    /// <summary>The faces: Impact for the big figures and the slams, Segoe UI bold italic for names, Bahnschrift for the small print.</summary>
    static readonly FontFamily Punch = HudThemes.Impact, NameFace = new("Segoe UI"), Print = HudThemes.Bahn;

    /// <summary>The slant of everything (px across per px down): a fighting game's bars lean forward.</summary>
    const double Lean = .22;

    // ---- pens and brushes made once
    static readonly Pen EdgeOut = Mod.P(Frozen(Mod.A(Colors.Black, .92)), 3), EdgeIn = Mod.P(Frozen(Mod.A(SteelC, .78)), 1.1),
        EdgeYou = Mod.P(Frozen(CyanC), 1.6), EdgeLead = Mod.P(Frozen(PaleYC), 1.4), TickPen = Mod.P(Frozen(Mod.A(Colors.Black, .3)), 1),
        CapPen = Mod.P(Frozen(Mod.A(Colors.White, .95)), 2), SteelThin = Mod.P(Frozen(Mod.A(SteelC, .55)), 1);
    static readonly Brush Track = Mod.Grad(true, (Mod.A(Ink2C, .92), 0), (Mod.A(InkC, .92), 1)),
        Gloss = Mod.Grad(true, (Mod.A(Colors.White, .40), 0), (Mod.A(Colors.White, .14), .42), (Mod.A(Colors.White, 0), .46), (Mod.A(Colors.Black, 0), .7), (Mod.A(Colors.Black, .24), 1)),
        LeadFill = Mod.Grad(false, (BloodC, 0), (RedC, .38), (OrangeC, .74), (YellowC, .96), (PaleYC, 1)),
        Plate = Mod.Grad(true, (Mod.A(Ink2C, .94), 0), (Mod.A(InkC, .94), 1)),
        HotStripe = Mod.Grad(true, (YellowC, 0), (OrangeC, .55), (RedC, 1)),
        SlamFill = Mod.Grad(true, (PaleYC, 0), (YellowC, .4), (OrangeC, .68), (RedC, .86), (BloodC, 1)),
        DefeatFill = Mod.Grad(true, (Color.FromRgb(0xE8, 0xEE, 0xFA), 0), (Color.FromRgb(0x9A, 0xA6, 0xC0), .5), (Color.FromRgb(0x4A, 0x52, 0x6A), 1)),
        SuperFill = Mod.Grad(true, (Color.FromRgb(0xC8, 0xF8, 0xFF), 0), (CyanC, .45), (BlueC, 1)),
        GoldFill = Mod.Grad(true, (PaleYC, 0), (YellowC, .45), (OrangeC, 1)),
        CyanStripe = Mod.Grad(true, (Color.FromRgb(0xC8, 0xF8, 0xFF), 0), (CyanC, .5), (BlueC, 1)),
        GreyStripe = Mod.Grad(true, (SteelC, 0), (GreyC, 1));
    static readonly Dictionary<Color, Brush> Fills = new();

    /// <summary>A member's gauge fill in their class colour: lighter at the top, deeper at the foot.</summary>
    static Brush FillOf(Color c)
    {
        if (Fills.TryGetValue(c, out var b)) return b;
        b = Mod.Grad(true, (Mod.Mix(c, Colors.White, .3), 0), (c, .5), (Mod.Mix(c, Colors.Black, .4), 1));
        Fills[c] = b;
        return b;
    }

    // ---- shapes

    /// <summary>A slab leaning forward inside <paramref name="r"/>: its top edge shifted right by <paramref name="k"/> times its height.</summary>
    static Geometry Slab(Rect r, double k = Lean)
    {
        double s = Math.Max(0, Math.Min(k * r.Height, r.Width / 2));
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(r.Left + s, r.Top), true, true);
            c.LineTo(new Point(r.Right, r.Top), true, false);
            c.LineTo(new Point(r.Right - s, r.Bottom), true, false);
            c.LineTo(new Point(r.Left, r.Bottom), true, false);
        }
        g.Freeze();
        return g;
    }

    /// <summary>A box with its top-left and bottom-right corners cut (<paramref name="cut"/> px): a round timer's, a card's.</summary>
    static Geometry Chop(Rect r, double cut)
    {
        double c = Math.Min(cut, Math.Min(r.Width, r.Height) / 2);
        var g = new StreamGeometry();
        using (var x = g.Open())
        {
            x.BeginFigure(new Point(r.Left + c, r.Top), true, true);
            x.LineTo(new Point(r.Right, r.Top), true, false);
            x.LineTo(new Point(r.Right, r.Bottom - c), true, false);
            x.LineTo(new Point(r.Right - c, r.Bottom), true, false);
            x.LineTo(new Point(r.Left, r.Bottom), true, false);
            x.LineTo(new Point(r.Left, r.Top + c), true, false);
        }
        g.Freeze();
        return g;
    }

    /// <summary>A hit spark: a star of <paramref name="n"/> uneven spikes between <paramref name="r0"/> and <paramref name="r1"/>.</summary>
    static Geometry Burst(Point at, double r0, double r1, int n, double turn = 0)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            for (int i = 0; i < 2 * n; i++)
            {
                double a = turn + Math.PI * i / n;
                double r = i % 2 == 0 ? r1 * (i % 4 == 0 ? 1 : .74) : r0;
                var p = new Point(at.X + r * Math.Sin(a), at.Y - r * Math.Cos(a));
                if (i == 0) c.BeginFigure(p, true, true); else c.LineTo(p, true, false);
            }
        }
        g.Freeze();
        return g;
    }

    /// <summary>A slanted stroke across a slab's height at <paramref name="x"/> (its foot), leaning as the slab does.</summary>
    static void LeanLine(DrawingContext dc, Pen p, double x, double top, double bottom, double k = Lean) =>
        dc.DrawLine(p, new Point(x + k * (bottom - top), top), new Point(x, bottom));

    /// <summary>
    /// A fighting game's gauge in <paramref name="r"/>: a dark track, filled to <paramref name="part"/> with <paramref name="fill"/>,
    /// a glossy band over its top, a hard white cap at its end, thin ticks at the tenths and a double outline (black, then
    /// chrome; <paramref name="rim"/> in its place for yours or the leader's).
    /// </summary>
    static void Gauge(DrawingContext dc, Rect r, double part, Brush fill, Pen? rim = null, bool ticks = true, double k = Lean)
    {
        if (r.Width < 8 || r.Height < 4) return;
        var shape = Slab(r, k);
        dc.DrawGeometry(Track, null, shape);
        double fw = r.Width * Math.Clamp(part, 0, 1);
        if (fw > .5)
        {
            dc.PushClip(shape);
            var f = Slab(new Rect(r.Left - 1, r.Top, fw + 1 + k * r.Height, r.Height), k);
            dc.DrawGeometry(fill, null, f);
            dc.DrawGeometry(Gloss, null, f);
            if (part < .995) LeanLine(dc, CapPen, r.Left + fw, r.Top, r.Bottom, k);
            dc.Pop();
        }
        if (ticks)
            for (int i = 1; i < 10; i++) LeanLine(dc, TickPen, r.Left + r.Width * i / 10, r.Top + 1, r.Bottom - 1, k);
        dc.DrawGeometry(null, EdgeOut, shape);
        dc.DrawGeometry(null, rim ?? EdgeIn, shape);
    }

    /// <summary>A dark slab behind words (a plate): navy-black, a chrome hairline, an optional hot stripe on its leading edge.</summary>
    internal static void PlateArt(DrawingContext dc, Size s, double k, Brush? stripe = null, Color? tint = null, double bloom = 1)
    {
        if (s.Width < 6 || s.Height < 4) return;
        var r = new Rect(1.5, 1.5, Math.Max(4, (s.Width - 3) * Math.Clamp(bloom, 0, 1)), s.Height - 3);
        var shape = Slab(r, k);
        dc.DrawGeometry(Plate, null, shape);
        if (tint is { } t) dc.DrawGeometry(Mod.Grad(false, (Mod.A(t, .62), 0), (Mod.A(t, .26), .55), (Mod.A(t, .1), 1)), null, shape);
        if (stripe != null)
        {
            dc.PushClip(shape);
            dc.DrawGeometry(stripe, null, Slab(new Rect(r.Left - 2, r.Top, 7 + k * r.Height, r.Height), k));
            dc.Pop();
        }
        dc.DrawGeometry(null, EdgeOut, shape);
        dc.DrawGeometry(null, SteelThin, shape);
    }

    /// <summary>A part that paints a plate behind its child, leaning <paramref name="k"/>: the timer chips, the notice, the foot.</summary>
    static Grid Plated(FrameworkElement child, double k = Lean, Brush? stripe = null, Thickness? pad = null)
    {
        var g = new Grid { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
        g.Children.Add(new TacSketch((dc, s) => PlateArt(dc, s, k, stripe)));
        child.Margin = pad ?? new Thickness(10, 3, 12, 4);
        g.Children.Add(child);
        return g;
    }

    // ---- words

    /// <summary>A text in one of the look's faces (tabular digits, never cut).</summary>
    static TextBlock T(string s, double size, Brush fg, FontWeight weight, FontFamily? face = null, bool italic = false)
    {
        var t = Mod.Text(s, size, fg, weight, face ?? Print);
        if (italic) t.FontStyle = FontStyles.Italic;
        return t;
    }

    /// <summary>A big figure in Impact (its one weight: Impact made bold only smears), leaning.</summary>
    static TextBlock Big(string s, double size, Brush fg) => T(s, size, fg, FontWeights.Normal, Punch, italic: true);

    /// <summary>The tight dark shade under words on the HUD's plates and gauges.</summary>
    static DropShadowEffect Shade(double blur = 2.5) => new() { Color = Colors.Black, Direction = 300, ShadowDepth = 1.2, BlurRadius = blur, Opacity = 1 };

    static T0 Lit<T0>(T0 el) where T0 : FrameworkElement { el.Effect = Shade(); return el; }

    /// <summary>A badge leaning as the bars do: TOP DPS in hot yellow, the heal mark in mint, YOU in P1 cyan, AVG in chrome.</summary>
    static Border Badge(string s, Color bg, Brush? fg = null, double size = 8.5)
    {
        var t = T(s, size, fg ?? InkB, FontWeights.Bold, italic: true);
        return new Border
        {
            Child = t, Padding = new Thickness(4, 0, 4, 1), VerticalAlignment = VerticalAlignment.Center, Background = Frozen(bg),
            BorderBrush = Frozen(Mod.A(Colors.Black, .85)), BorderThickness = new Thickness(1),
            RenderTransform = new SkewTransform(-12, 0), RenderTransformOrigin = new Point(.5, .5),
        };
    }

    /// <summary>
    /// Words as a shape for a slam (FIGHT!, K.O., DEFEAT, NEW RECORD, SPECIAL!): Impact for Latin, a heavy Korean or Chinese
    /// face for theirs; leaning <paramref name="skew"/> degrees, its box's top-left at 0,0. Never a figure.
    /// </summary>
    static Geometry Words(string s, double size, double skew = -12)
    {
        bool latin = s.All(ch => ch < 0x2000);
        var tf = latin ? new Typeface(Punch, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal)
            : new Typeface(new FontFamily("Malgun Gothic, Microsoft JhengHei, Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
        var ft = new FormattedText(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, tf, latin ? size : size * .8, Brushes.Black, 1.0);
        var geo = ft.BuildGeometry(new Point(0, 0));
        var b = geo.Bounds;
        if (b.IsEmpty) return Geometry.Empty;
        var tg = new TransformGroup();
        tg.Children.Add(new TranslateTransform(-b.Left, -b.Top));
        tg.Children.Add(new SkewTransform(skew, 0, 0, b.Height));
        var pg = PathGeometry.CreateFromGeometry(geo);
        pg.Transform = tg;
        var flat = pg.GetFlattenedPathGeometry();
        var nb = flat.Bounds;
        flat.Transform = new TranslateTransform(-nb.Left, -nb.Top);
        var done = flat.GetFlattenedPathGeometry();
        done.Freeze();
        return done;
    }

    /// <summary>
    /// A slam word as a part drawn once (its size the word's and its outline's): a hard drop shadow, a thick outline under a
    /// hot fill, a thin light line inside it. A moment scales and fades it; nothing is drawn again.
    /// </summary>
    static TacSketch Slam(string s, double size, Brush fill, Color? edge = null, double stroke = 6, double skew = -12)
    {
        var geo = Words(s, size, skew);
        var b = geo.Bounds;
        if (b.IsEmpty) b = new Rect(0, 0, 1, 1);
        double pad = stroke / 2 + 2;
        var outline = Mod.P(Frozen(edge ?? Colors.Black), stroke, round: true);
        var inner = Mod.P(Frozen(Mod.A(Colors.White, .5)), .8);
        var shade = Frozen(Mod.A(Colors.Black, .55));
        return new TacSketch((dc, _) =>
        {
            dc.PushTransform(new TranslateTransform(pad + 3, pad + 3));
            dc.DrawGeometry(shade, outline, geo);
            dc.Pop();
            dc.PushTransform(new TranslateTransform(pad, pad));
            dc.DrawGeometry(null, outline, geo);
            dc.DrawGeometry(fill, inner, geo);
            dc.Pop();
        }, b.Width + 2 * pad + 3, b.Height + 2 * pad + 3)
        { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, RenderTransformOrigin = new Point(.5, .5), Opacity = 0 };
    }

    /// <summary>The slam's size for a room: as big as <paramref name="want"/>, its width within <paramref name="roomW"/> and height within <paramref name="roomH"/>.</summary>
    static double SlamSize(string s, double want, double roomW, double roomH = double.PositiveInfinity)
    {
        var b = Words(s, want).Bounds;
        if (b.IsEmpty) return want;
        double k = Math.Min(1, Math.Min((roomW - 16) / (b.Width + 8), (roomH - 10) / (b.Height + 8)));
        return Math.Max(10, want * k);
    }

    /// <summary>Speed streaks across a part (drawn once; a moment slides and fades them): the lines a fighting game throws behind a hit.</summary>
    static TacSketch Streaks(Color c, int n = 14, int seed = 3) => new((dc, s) =>
    {
        if (s.Width < 4 || s.Height < 4) return;
        var rnd = new Random(seed);
        var brush = Mod.Grad(false, (Mod.A(c, 0), 0), (Mod.A(c, .8), .72), (Mod.A(Colors.White, .95), 1));
        for (int i = 0; i < n; i++)
        {
            double y = s.Height * (i + .5) / n + (rnd.NextDouble() - .5) * s.Height / n, len = s.Width * (.22 + .4 * rnd.NextDouble()), x = rnd.NextDouble() * (s.Width - len);
            double th = 1 + 2 * rnd.NextDouble();
            dc.DrawRectangle(brush, null, new Rect(x, y - th / 2, len, th));
        }
    })
    { IsHitTestVisible = false, Opacity = 0 };

    /// <summary>A ring of impact (an outline that spreads and fades), flattened as a shockwave on the floor.</summary>
    static System.Windows.Shapes.Ellipse Ring(double d, Color c, double t = 4) => new()
    {
        Width = d, Height = d * .5, Stroke = Frozen(c), StrokeThickness = t, Opacity = 0, IsHitTestVisible = false,
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        RenderTransformOrigin = new Point(.5, .5), RenderTransform = new ScaleTransform(.2, .2),
    };

    /// <summary>A hit spark drawn once (a moment scales, turns and fades it): yellow spikes round a white core, a red rim.</summary>
    static TacSketch Spark(double r, Color c, int n = 9)
    {
        double d = 2 * r + 4;
        var spikes = Burst(new Point(d / 2, d / 2), r * .36, r, n);
        var core = Burst(new Point(d / 2, d / 2), r * .2, r * .52, 7, .3);
        var fill = Frozen(c);
        var rim = Mod.P(Frozen(Mod.A(RedC, .9)), 1.5);
        return new TacSketch((dc, _) => { dc.DrawGeometry(fill, rim, spikes); dc.DrawGeometry(White, null, core); }, d, d)
        { RenderTransformOrigin = new Point(.5, .5), Opacity = 0, IsHitTestVisible = false, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    }

    /// <summary>A slam's scale on its course: from <paramref name="from"/> down to 1 with a small bounce.</summary>
    static double SlamScale(double p, double from) => p >= 1 ? 1 : 1 + (from - 1) * (1 - Back(p, 2.2));

    /// <summary>A flash frame's strength: up at once, gone over <paramref name="len"/> of the course after <paramref name="at"/>.</summary>
    static double Flash(double t, double at, double len) => t < at ? 0 : Math.Max(0, 1 - (t - at) / len);
}
