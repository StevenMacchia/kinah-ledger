using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Path = System.Windows.Shapes.Path;
using KinahLedger.Parser;
using KinahLedger.Session;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

/// <summary>
/// What the Wacky theme's three looks share (Sticker, Neon, Goo; Steven, 2026-11-16: loud, chunky and playful, "movie
/// production level"): the look's root with a clear margin round the panel for the moments to spill into (reported as
/// <see cref="Inset"/>), the effects' canvas over it, and sprites: an effect's element with its own scale, rotation and
/// shift, played once through Motion.Play (so a render's held frame shows it at that point) and taken away at its end.
/// The figures themselves never move: every effect goes round them.
/// </summary>
abstract class WackyLook : HudThemeLook
{
    protected readonly Grid _root = new() { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
    /// <summary>The look's own parts, top to bottom, inside the clear margin.</summary>
    protected readonly StackPanel _body = new() { HorizontalAlignment = HorizontalAlignment.Left };
    /// <summary>The moments' effects, over everything, never hit.</summary>
    protected readonly Canvas _fx = new() { IsHitTestVisible = false };
    readonly Thickness _inset;
    readonly Dictionary<string, FrameworkElement> _slots = new();
    /// <summary>The panel opacity last set (0.3 to 1).</summary>
    protected double Op { get; private set; } = 0.94;

    protected WackyLook(Thickness inset, FontFamily rowsFace) : base(false, rowsFace)
    {
        _inset = inset;
        _body.Margin = inset;
        _root.Children.Add(_body);
        _root.Children.Add(_fx);
    }

    public sealed override FrameworkElement Root => _root;
    public sealed override Thickness Inset => _inset;

    public sealed override void SetOpacity(double opacity) { Op = Math.Clamp(opacity, 0.3, 1); Ground(Op); }
    /// <summary>The panels' ground at this opacity (<see cref="Op"/>).</summary>
    protected abstract void Ground(double opacity);

    // ---- sprites

    /// <summary>An effect's element with its own transforms (about its centre), placed on the effects' canvas.</summary>
    protected sealed class Sprite
    {
        public readonly FrameworkElement El;
        readonly ScaleTransform _s = new();
        readonly SkewTransform _k = new();
        readonly RotateTransform _r = new();
        readonly TranslateTransform _t = new();
        public Sprite(FrameworkElement el)
        {
            El = el;
            el.IsHitTestVisible = false;
            el.RenderTransformOrigin = new Point(.5, .5);
            el.RenderTransform = new TransformGroup { Children = { _s, _k, _r, _t } };
        }
        public Sprite Set(double sx, double sy, double rot, double dx, double dy, double op, double skew = 0)
        {
            _s.ScaleX = sx; _s.ScaleY = sy; _k.AngleX = skew; _r.Angle = rot; _t.X = dx; _t.Y = dy; El.Opacity = Math.Clamp(op, 0, 1);
            El.Visibility = op <= 0.001 ? Visibility.Hidden : Visibility.Visible;
            return this;
        }
    }

    /// <summary>
    /// <paramref name="el"/> on the effects' canvas, centred on <paramref name="centre"/>, in place of the one this
    /// <paramref name="slot"/> held (a moment told again starts over).
    /// </summary>
    protected Sprite Spawn(FrameworkElement el, Point centre, string slot)
    {
        Drop(slot);
        el.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var sz = el.DesiredSize;
        Canvas.SetLeft(el, Math.Round(centre.X - sz.Width / 2));
        Canvas.SetTop(el, Math.Round(centre.Y - sz.Height / 2));
        _fx.Children.Add(el);
        _slots[slot] = el;
        return new Sprite(el).Set(1, 1, 0, 0, 0, 0);
    }

    /// <summary>Plays a sprite's course once (held: at the render's point), and takes it away at its end.</summary>
    protected void Play(Sprite s, string slot, double ms, Action<double> step) =>
        Motion.Play(s.El, ms, step, () => { _fx.Children.Remove(s.El); if (_slots.TryGetValue(slot, out var cur) && ReferenceEquals(cur, s.El)) _slots.Remove(slot); });

    protected void Drop(string slot)
    {
        if (!_slots.TryGetValue(slot, out var old)) return;
        Motion.Halt(old);
        _fx.Children.Remove(old);
        _slots.Remove(slot);
    }

    /// <summary>A point of <paramref name="el"/> (its width and height times fx and fy) on the effects' canvas.</summary>
    protected Point At(FrameworkElement? el, double fx, double fy, Point? fallback = null)
    {
        var none = fallback ?? new Point(_inset.Left + 60, _inset.Top + 20);
        if (el == null) return none;
        try
        {
            _root.UpdateLayout();
            if (!el.IsVisible || el.ActualWidth <= 0) return none;
            return el.TranslatePoint(new Point(el.ActualWidth * fx, el.ActualHeight * fy), _fx);
        }
        catch (InvalidOperationException) { return none; }
    }

    // ---- the timers, shared: where the next one goes (the line, or inside the fight in a dungeon)

    protected IReadOnlyList<TimerBoard.Item> Due { get; private set; } = Array.Empty<TimerBoard.Item>();
    protected DateTime DueNow { get; private set; }

    protected sealed override void DrawTimers(IReadOnlyList<TimerBoard.Item> due, DateTime now)
    {
        Due = due; DueNow = now;
        DrawTag();
    }
    /// <summary>The next timer's tag drawn and put where it belongs now.</summary>
    protected abstract void DrawTag();

    /// <summary>"Shugo" and "ENTER 2:41" apart, and the tone.</summary>
    protected static (string Name, string Clock, TimerBadge.Tone Tone) TagWords(TimerBoard.Item i, DateTime now)
    {
        var (text, tone) = TimerWords(i, now);
        var name = i.Short ?? "";
        var clock = name.Length > 0 && text.StartsWith(name) ? text[name.Length..].TrimStart() : text;
        if (name.Length == 0 || clock == text) name = "";
        return (name, clock, tone);
    }

    // ---- small shared helpers

    protected static double Seg(double p, double a, double b) => b <= a ? (p >= b ? 1 : 0) : Math.Clamp((p - a) / (b - a), 0, 1);
    protected static double Lerp(double a, double b, double t) => a + (b - a) * t;
    /// <summary>A damped wobble: <paramref name="cycles"/> swings over the course, dying away.</summary>
    protected static double Wob(double t, double cycles, double decay) => Math.Sin(t * cycles * 2 * Math.PI) * Math.Exp(-decay * t);
    protected static double In(double p) { p = Math.Clamp(p, 0, 1); return p * p * p; }
    protected static double InOut(double p) { p = Math.Clamp(p, 0, 1); return p < .5 ? 4 * p * p * p : 1 - Math.Pow(-2 * p + 2, 3) / 2; }

    /// <summary>A pickup's words on its effect: "+7.5K", else its count and name.</summary>
    protected static string PickWords(Pick p) => p.Worth.StartsWith("+") ? p.Worth : p.Kinah ? p.Name : (p.Count.Length > 0 ? p.Count + " " : "") + p.Name;

    /// <summary>The words for a fight's end on a stamp: kill, wipe, cleared, over.</summary>
    protected static bool Won(Encounter e) => e.Outcome is "kill" or "cleared";
}

/// <summary>The Wacky theme's drawing parts: outlined words as shapes, bursts, arcs, gradients.</summary>
static class Wk
{
    public static readonly FontFamily Heavy = HudThemes.Heavy, Impact = HudThemes.Impact, Bahn = HudThemes.Bahn;
    public static readonly FontFamily Segoe = new("Segoe UI");

    public static SolidColorBrush S(uint rgb, double a = 1) => B(rgb, a);

    public static LinearGradientBrush Lin(double x0, double y0, double x1, double y1, params (uint C, double A, double At)[] stops)
    {
        var b = new LinearGradientBrush { StartPoint = new Point(x0, y0), EndPoint = new Point(x1, y1) };
        foreach (var (c, a, at) in stops) b.GradientStops.Add(new GradientStop(Rgb(c, a), at));
        b.Freeze();
        return b;
    }

    public static RadialGradientBrush Rad(double cx, double cy, double r, params (uint C, double A, double At)[] stops)
    {
        var b = new RadialGradientBrush { Center = new Point(cx, cy), GradientOrigin = new Point(cx, cy), RadiusX = r, RadiusY = r };
        foreach (var (c, a, at) in stops) b.GradientStops.Add(new GradientStop(Rgb(c, a), at));
        b.Freeze();
        return b;
    }

    public static uint Hex(Color c) => (uint)(c.R << 16 | c.G << 8 | c.B);

    /// <summary>A colour made vivid: its hue kept, its saturation and brightness lifted (a class's soft colour as candy, tape or neon).</summary>
    public static uint Vivid(uint c, double sat = .78, double val = .97)
    {
        double r = (c >> 16 & 0xFF) / 255.0, g = (c >> 8 & 0xFF) / 255.0, b = (c & 0xFF) / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
        double h = d == 0 ? 0 : max == r ? ((g - b) / d % 6) : max == g ? (b - r) / d + 2 : (r - g) / d + 4;
        h *= 60; if (h < 0) h += 360;
        double s = Math.Max(max == 0 ? 0 : d / max, d == 0 ? 0 : sat), v = Math.Max(max, val);
        double cc = v * s, x = cc * (1 - Math.Abs(h / 60 % 2 - 1)), m = v - cc;
        (double R, double G, double B) = h < 60 ? (cc, x, 0.0) : h < 120 ? (x, cc, 0.0) : h < 180 ? (0.0, cc, x) : h < 240 ? (0.0, x, cc) : h < 300 ? (x, 0.0, cc) : (cc, 0.0, x);
        return (uint)Math.Round((R + m) * 255) << 16 | (uint)Math.Round((G + m) * 255) << 8 | (uint)Math.Round((B + m) * 255);
    }

    /// <summary>A colour lighter (t &gt; 0, toward white) or darker (t &lt; 0, toward black).</summary>
    public static uint Shade(uint c, double t)
    {
        double r = c >> 16 & 0xFF, g = c >> 8 & 0xFF, b = c & 0xFF;
        double to = t > 0 ? 255 : 0, k = Math.Abs(t);
        return (uint)Math.Round(r + (to - r) * k) << 16 | (uint)Math.Round(g + (to - g) * k) << 8 | (uint)Math.Round(b + (to - b) * k);
    }

    /// <summary>Words as a shape (for the moments' stickers and stamps, never a figure the tests read).</summary>
    public static Geometry TextGeo(string s, FontFamily face, FontWeight w, double size, double skew = 0)
    {
        var tf = new Typeface(face, FontStyles.Normal, w, FontStretches.Normal);
        var ft = new FormattedText(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, tf, size, Brushes.Black, 1.0);
        var geo = ft.BuildGeometry(new Point(0, 0));
        if (skew != 0) geo.Transform = new SkewTransform(skew, 0, 0, ft.Baseline);
        return geo;
    }

    /// <summary>
    /// Words outlined (the stroke under the fill, so the letters keep their weight), with an optional hard shadow
    /// <paramref name="shadowAt"/> px down and right: a sticker's or a cabinet's lettering.
    /// </summary>
    public static Grid Outlined(string s, FontFamily face, FontWeight w, double size, Brush fill, Brush? stroke, double t, double skew = 0, Brush? shadow = null, double shadowAt = 0, Brush? glow = null, double glowT = 0)
    {
        var geo = TextGeo(s, face, w, size, skew);
        var b = geo.Bounds;
        if (b.IsEmpty) b = new Rect(0, 0, 1, 1);
        double pad = Math.Max(t, glowT) + 1;
        var g = new Grid { Width = Math.Ceiling(b.Width + 2 * pad + shadowAt), Height = Math.Ceiling(b.Height + 2 * pad + shadowAt) };
        Geometry At(double dx) { var gg = new GeometryGroup { Children = { geo }, Transform = new TranslateTransform(-b.X + pad + dx, -b.Y + pad + dx) }; gg.Freeze(); return gg; }
        var at0 = At(0);
        if (glow != null && glowT > 0) g.Children.Add(new Path { Data = at0, Stroke = glow, StrokeThickness = glowT * 2, StrokeLineJoin = PenLineJoin.Round, Fill = glow });
        if (shadow != null && shadowAt > 0) g.Children.Add(new Path { Data = At(shadowAt), Fill = shadow, Stroke = shadow, StrokeThickness = t * 2, StrokeLineJoin = PenLineJoin.Round });
        if (stroke != null && t > 0) g.Children.Add(new Path { Data = at0, Fill = stroke, Stroke = stroke, StrokeThickness = t * 2, StrokeLineJoin = PenLineJoin.Round });
        g.Children.Add(new Path { Data = at0, Fill = fill });
        return g;
    }

    /// <summary>A comic burst: <paramref name="n"/> spikes round an oval <paramref name="rx"/> by <paramref name="ry"/>, the dips at <paramref name="inner"/> of the way out.</summary>
    public static Geometry Star(double rx, double ry, int n, double inner, int seed)
    {
        var rnd = new Random(seed);
        var sg = new StreamGeometry();
        using (var c = sg.Open())
        {
            for (int i = 0; i < n * 2; i++)
            {
                double a = Math.PI * i / n - Math.PI / 2 + (rnd.NextDouble() - .5) * (Math.PI / n) * .5;
                double k = i % 2 == 0 ? 1 - rnd.NextDouble() * .14 : inner + (rnd.NextDouble() - .5) * .08;
                var pt = new Point(rx + Math.Cos(a) * rx * k, ry + Math.Sin(a) * ry * k);
                if (i == 0) c.BeginFigure(pt, true, true); else c.LineTo(pt, true, true);
            }
        }
        sg.Freeze();
        return sg;
    }

    /// <summary>
    /// A burst sticker: the spiky shape in <paramref name="fill"/> with a black outline and a hard offset shadow, and
    /// <paramref name="content"/> at its middle.
    /// </summary>
    public static Grid Burst(double rx, double ry, int n, double inner, int seed, Brush fill, Brush line, double lineT, double shadowAt, UIElement? content)
    {
        var geo = Star(rx, ry, n, inner, seed);
        double pad = lineT + 1;
        var g = new Grid { Width = 2 * rx + 2 * pad + shadowAt, Height = 2 * ry + 2 * pad + shadowAt };
        var at = new TranslateTransform(pad, pad);
        if (shadowAt > 0) g.Children.Add(new Path { Data = geo, Fill = line, Stroke = line, StrokeThickness = lineT, StrokeLineJoin = PenLineJoin.Round, RenderTransform = new TranslateTransform(pad + shadowAt, pad + shadowAt) });
        g.Children.Add(new Path { Data = geo, Fill = fill, Stroke = line, StrokeThickness = lineT, StrokeLineJoin = PenLineJoin.Round, RenderTransform = at });
        if (content is FrameworkElement fe)
        {
            fe.HorizontalAlignment = HorizontalAlignment.Center; fe.VerticalAlignment = VerticalAlignment.Center;
            fe.Margin = new Thickness(0, 0, shadowAt, shadowAt);
            g.Children.Add(fe);
        }
        return g;
    }

    /// <summary>An arc from the top, clockwise, <paramref name="share"/> of a circle of <paramref name="size"/> with the stroke inside it.</summary>
    public static Shape Arc(double size, double stroke, double share, Brush brush, PenLineCap cap = PenLineCap.Flat, double inset = 0)
    {
        double s = Math.Clamp(share, 0, 1);
        double c = size / 2, r = (size - stroke) / 2 - inset;
        if (s >= .999) return new Ellipse { Width = 2 * r + stroke, Height = 2 * r + stroke, Stroke = brush, StrokeThickness = stroke, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        double a = 2 * Math.PI * Math.Max(s, .004);
        var fig = new PathFigure { StartPoint = new Point(c, c - r) };
        fig.Segments.Add(new ArcSegment(new Point(c + r * Math.Sin(a), c - r * Math.Cos(a)), new Size(r, r), 0, a > Math.PI, SweepDirection.Clockwise, true));
        var geo = new PathGeometry(); geo.Figures.Add(fig); geo.Freeze();
        return new Path { Data = geo, Stroke = brush, StrokeThickness = stroke, StrokeStartLineCap = cap, StrokeEndLineCap = cap, Width = size, Height = size };
    }

    /// <summary>A text block in a face (not a figure: words, labels).</summary>
    public static TextBlock Words(string s, double size, Brush fg, FontFamily face, FontWeight w) => new()
    {
        Text = s, FontSize = size, Foreground = fg, FontFamily = face, FontWeight = w, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.None,
    };

    /// <summary>A tilted part keeps smooth letters (Display mode snaps them to the pixel grid, which a turn breaks).</summary>
    public static T Tilt<T>(T el, double angle) where T : FrameworkElement
    {
        el.RenderTransformOrigin = new Point(.5, .5);
        el.RenderTransform = new RotateTransform(angle);
        TextOptions.SetTextFormattingMode(el, TextFormattingMode.Ideal);
        return el;
    }

    /// <summary>
    /// A meter bar's fill: <paramref name="fill"/>'s width is its share of <paramref name="host"/>'s, kept through a resize;
    /// with effects on it slides to a new share (Motion.Slide); off, it is set where it ends.
    /// </summary>
    public sealed class BarFill
    {
        readonly FrameworkElement _host, _fill;
        readonly double _min;
        double _v;
        public BarFill(FrameworkElement host, FrameworkElement fill, double min)
        {
            _host = host; _fill = fill; _min = min;
            host.SizeChanged += (_, _) => Apply(_v);
        }
        public void Apply(double v)
        {
            _v = Math.Clamp(double.IsFinite(v) ? v : 0, 0, 1);
            double w = _host.ActualWidth;
            if (w <= 0) return;
            _fill.Width = _v <= 0 ? 0 : Math.Max(_min, w * _v);
        }
        /// <summary>The bar at <paramref name="share"/>; true when it moved from another share.</summary>
        public bool Set(double share, MeterContext c, string tag, bool fresh)
        {
            bool moved = Math.Abs(share - _v) > .005;
            if (!c.Moves || fresh) { Apply(share); return false; }
            Motion.Slide(_fill, c.Key + "|" + tag, share, Apply);
            return moved;
        }
    }

    /// <summary>A set of small pieces (droplets, confetti, sparks) drawn on one canvas, each moved by the course.</summary>
    public sealed class Bits
    {
        public readonly Canvas Host;
        readonly List<(FrameworkElement El, TranslateTransform T, RotateTransform R, ScaleTransform S)> _all = new();
        public Bits(double w, double h) { Host = new Canvas { Width = w, Height = h, IsHitTestVisible = false }; }
        public void Add(FrameworkElement el)
        {
            var t = new TranslateTransform(); var r = new RotateTransform(); var s = new ScaleTransform();
            el.RenderTransformOrigin = new Point(.5, .5);
            el.RenderTransform = new TransformGroup { Children = { s, r, t } };
            el.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(el, Host.Width / 2 - el.DesiredSize.Width / 2); Canvas.SetTop(el, Host.Height / 2 - el.DesiredSize.Height / 2);
            Host.Children.Add(el);
            _all.Add((el, t, r, s));
        }
        public int Count => _all.Count;
        public void Set(int i, double dx, double dy, double rot, double scale, double op)
        {
            var (el, t, r, s) = _all[i];
            t.X = dx; t.Y = dy; r.Angle = rot; s.ScaleX = s.ScaleY = scale; el.Opacity = Math.Clamp(op, 0, 1);
        }
    }
}
