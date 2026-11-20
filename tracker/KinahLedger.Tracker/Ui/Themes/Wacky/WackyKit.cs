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
        /// <summary>Where a part of the sprite (<paramref name="part"/>, in the sprite's own room) is on the effects' canvas now.</summary>
        public Rect Where(Rect part)
        {
            double w = El.DesiredSize.Width, h = El.DesiredSize.Height;
            var m = new Matrix();
            m.Translate(-w / 2, -h / 2);
            m.Append(El.RenderTransform.Value);
            m.Translate(w / 2 + Canvas.GetLeft(El), h / 2 + Canvas.GetTop(El));
            return Rect.Transform(part, m);
        }
        public void Shift(double dx, double dy) { _t.X += dx; _t.Y += dy; }
    }

    // ---- keeping the effects inside the window (Steven, 2026-11-16: "some animations get cut off"): the window over the
    // game is exactly the look's size, so anything drawn past its edge is cut there

    /// <summary>The window's size (the look's root, its clear margin included).</summary>
    protected Size WinSize { get { _root.UpdateLayout(); return new Size(_root.ActualWidth, _root.ActualHeight); } }

    /// <summary>A part of <paramref name="host"/> centred in it (a sprite's sticker or bubble inside its wider room).</summary>
    protected static Rect Centred(FrameworkElement host, FrameworkElement part)
    {
        host.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        part.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Size h = host.DesiredSize, p = part.DesiredSize;
        return new Rect((h.Width - p.Width) / 2, (h.Height - p.Height) / 2, p.Width, p.Height);
    }

    /// <summary>
    /// The sprite moved back inside the window when its part (<paramref name="part"/>, in the sprite's room) reaches past an
    /// edge at this point of its course: a slam from big or a peel that turns it slides along the edge instead of being cut.
    /// </summary>
    protected void Keep(Sprite s, Rect part, double pad = 3)
    {
        double w = _root.ActualWidth, h = _root.ActualHeight;
        if (w <= 0 || h <= 0 || s.El.Visibility != Visibility.Visible) return;
        var r = s.Where(part);
        double dx = r.Width > w - 2 * pad ? (w - r.Width) / 2 - r.Left : r.Left < pad ? pad - r.Left : r.Right > w - pad ? w - pad - r.Right : 0;
        double dy = r.Height > h - 2 * pad ? (h - r.Height) / 2 - r.Top : r.Top < pad ? pad - r.Top : r.Bottom > h - pad ? h - pad - r.Bottom : 0;
        if (dx != 0 || dy != 0) s.Shift(dx, dy);
    }

    /// <summary>The largest scale up to <paramref name="want"/> at which a part this size, turned up to <paramref name="turn"/> degrees, fits the window.</summary>
    protected double Fit(double want, Size part, double turn = 0, double pad = 3)
    {
        var win = WinSize;
        double a = Math.Abs(turn) * Math.PI / 180;
        double bw = part.Width * Math.Cos(a) + part.Height * Math.Sin(a), bh = part.Width * Math.Sin(a) + part.Height * Math.Cos(a);
        if (win.Width <= 0 || bw <= 0 || bh <= 0) return want;
        return Math.Max(1, Math.Min(want, Math.Min((win.Width - 2 * pad) / bw, (win.Height - 2 * pad) / bh)));
    }

    /// <summary><paramref name="c"/> moved so a part this size (at <paramref name="scale"/>, turned up to <paramref name="turn"/> degrees) centred on it fits the window.</summary>
    protected Point Inside(Point c, Size part, double scale = 1, double turn = 0, double pad = 3)
    {
        var win = WinSize;
        if (win.Width <= 0) return c;
        double a = Math.Abs(turn) * Math.PI / 180;
        double hw = (part.Width * Math.Cos(a) + part.Height * Math.Sin(a)) * scale / 2, hh = (part.Width * Math.Sin(a) + part.Height * Math.Cos(a)) * scale / 2;
        double x = hw * 2 > win.Width - 2 * pad ? win.Width / 2 : Math.Clamp(c.X, pad + hw, win.Width - pad - hw);
        double y = hh * 2 > win.Height - 2 * pad ? win.Height / 2 : Math.Clamp(c.Y, pad + hh, win.Height - pad - hh);
        return new Point(x, y);
    }

    /// <summary>How far a point is from each edge of the window.</summary>
    protected (double L, double T, double R, double B) Room(Point c) { var win = WinSize; return (c.X, c.Y, win.Width - c.X, win.Height - c.Y); }

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

    // ---- the pickup strip (Ui/HudPickStrip.cs): the best drop and the last three under the hour while farming

    /// <summary>How this look wears the pickup strip.</summary>
    protected abstract PickStrip.Look StripLook { get; }

    /// <summary>The line is drawn again when the strip's pickups change too, and with the dungeon run's values (Ui/HudRunBuddy.cs).</summary>
    protected override string LineSig(Figures f) => base.LineSig(f) + "|" + PickStrip.Sig(Values, StripLook) + "|" + RunBuddy.Sig(Values);

    // ---- the dungeon run (Ui/HudRunBuddy.cs; Steven, 2026-11-18, of the run-mode Buddy: "Yes, all looks")

    /// <summary>The dungeon run drawn in place of the hour, or null: out of a run, and while a fight's panel has the Kinah line put away.</summary>
    protected RunBuddy.State? RunOf() => KinahHidden ? null : RunBuddy.Of(Values);

    /// <summary>
    /// The strip in a run: always the best drop and the last three (Steven, 2026-11-19: on ALL buddies), on its own row under
    /// the run's lines, so it never gives way to them. <paramref name="room"/> is kept for the callers' symmetry with the lines.
    /// </summary>
    protected FrameworkElement? RunStrip(double room) => PickStrip.Of(Values, StripLook);

    /// <summary>An element's width laid out on its own (its render transform, a tilt or a skew, left out).</summary>
    protected static double W(FrameworkElement e)
    {
        e.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return e.DesiredSize.Width;
    }

    /// <summary>
    /// The width of a part kept between draws (the timer's tag), put in <paramref name="host"/> first when it is out of the
    /// tree: shown while out of it, it keeps the size it had hidden.
    /// </summary>
    protected static double KeptW(FrameworkElement e, Panel host)
    {
        if (VisualTreeHelper.GetParent(e) == null) host.Children.Add(e);
        return W(e);
    }

    /// <summary>The first of <paramref name="forms"/> that is no wider than <paramref name="room"/>, else the last: a part goes before a figure is cut.</summary>
    protected static T Fit<T>(double room, params Func<T>[] forms) where T : FrameworkElement
    {
        T? last = null;
        foreach (var make in forms)
        {
            last = make();
            if (W(last) <= room + .5) return last;
        }
        return last!;
    }

    /// <summary>"Cleared in {0}" split round its figure, wherever the language puts it: the words before and after.</summary>
    protected static (string Before, string After) ClearedWords()
    {
        var fmt = L.T("Cleared in {0}");
        int at = fmt.IndexOf("{0}", StringComparison.Ordinal);
        return at < 0 ? (fmt, "") : (fmt[..at].TrimEnd(), fmt[(at + 3)..].TrimStart());
    }

    /// <summary>"2 runs" / "1 run": today's runs here.</summary>
    protected static string RunsWord(RunBuddy.State s) => s.TodayRuns == "1" ? L.T("1 run") : L.F("{0} runs", s.TodayRuns);

    /// <summary>
    /// The strip for this line, or null: an hour shown (not the Abyss, the idle or waiting line, nor a dungeon's meter); it
    /// stays beside a fight's card, as the Kinah line does.
    /// </summary>
    protected FrameworkElement? StripFor(Figures f) =>
        !f.Abyss && f.Counted && f.Rate.Length > 0 && !KinahHidden ? PickStrip.Of(Values, StripLook) : null;

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

    /// <summary>The widest a meter row's DPS is written ("999.9K", "9.99M": Amounts.Meter), and its share ("99.9%", "100%").</summary>
    public static readonly string[] DpsForms = { "888.8K", "8.88M" };
    /// <summary>The share column's widest forms: a share, or the PvP panel's Kill and Assist (in the language in use).</summary>
    public static string[] ShareForms => new[] { "88.8%", "100%" }.Concat(Encounter.PvpWords).ToArray();

    /// <summary>
    /// The room a figure column needs (Steven, 2026-11-17: "a lot of numbers in all the meters you can't see, it's cut off!"): the
    /// widest of its forms in its face, size and weight, tabular digits, rounded up, plus <paramref name="gap"/> before it.
    /// </summary>
    public static double Room(FontFamily face, double size, FontWeight w, string[] forms, double gap)
    {
        double max = 0;
        foreach (var s in forms)
        {
            var t = new TextBlock { Text = s, FontFamily = face, FontSize = size, FontWeight = w };
            System.Windows.Documents.Typography.SetNumeralAlignment(t, FontNumeralAlignment.Tabular);
            TextOptions.SetTextFormattingMode(t, TextFormattingMode.Display);
            t.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            max = Math.Max(max, t.DesiredSize.Width);
        }
        return Math.Ceiling(max + 1) + gap;
    }

    /// <summary>
    /// A member's bar, 0 to 1, against the top member's figure (Steven, 2026-11-17: "prioritizes the bar"): the top row's bar
    /// runs the row's whole width, the others by their damage beside it; none before the first hit.
    /// </summary>
    public static double Bar(MeterMember m, MeterContext c)
    {
        if (m.CpOnly) return 0;
        return c.E.BarOf(m.F);   // against the top member's: the top bar full, the rest in proportion (2026-11-20)
    }

    /// <summary>
    /// The expanded row's detail: the damage, then the crit rate with its word as a label in the look's small label style (the
    /// "CP" before the Combat Power's), so the figures keep their size and the line its room ("127.81M · 38% crit").
    /// </summary>
    public static void Detail(TextBlock t, MeterMember m, MeterContext c, System.Windows.Documents.Run label)
    {
        t.Inlines.Clear();
        if (!c.Expanded || m.Damage.Length == 0) { t.Visibility = Visibility.Collapsed; return; }
        t.Inlines.Add(new System.Windows.Documents.Run(m.Damage + (m.Crit.Length > 0 ? " · " + m.Crit + " " : "")));
        if (m.Crit.Length > 0) { label.Text = L.T("crit"); t.Inlines.Add(label); }
        t.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// A row's DPS and share columns: sized for the widest DPS and share (<see cref="Room"/>), or before any damage for the
    /// Combat Power ("888,888") and its "CP" label in the language shown.
    /// </summary>
    public static void FigureCols(ColumnDefinition fig, ColumnDefinition share, bool cpOnly, FontFamily figFace, double figSize, FontWeight figW, FontFamily shareFace, double shareSize, FontWeight shareW)
    {
        fig.Width = new GridLength(Room(figFace, figSize, figW, cpOnly ? new[] { "888,888" } : DpsForms, 6));
        share.Width = new GridLength(Room(shareFace, shareSize, shareW, cpOnly ? new[] { L.T("CP") } : ShareForms, 5));
    }

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

/// <summary>
/// What the Wacky looks' skills cards share (Ui/Themes/MemberCard.cs; Steven, 2026-11-16: the pop-up on a meter row was
/// the Classic card in every theme): the ability table's columns, right-aligned tabular figures, the foot line in parts,
/// and the card's entrance (played only with effects on, settled in under 300 ms, nothing on the figures).
/// </summary>
static class WkCard
{
    /// <summary>A crit or back-attack column's width.</summary>
    public const double RateCol = 36;

    /// <summary>The card's width: <paramref name="w"/> plus the rate columns it carries.</summary>
    public static double Width(MemberCard c, double w) => w + (c.CritCol ? RateCol : 0) + (c.BackCol ? RateCol : 0);

    /// <summary>A row of the ability table: the name (and its bar) taking the room, then share, damage, crit and back.</summary>
    public static Grid Cols(MemberCard c, double share, double damage, double height)
    {
        var g = new Grid { Height = height };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(share) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(damage) });
        if (c.CritCol) g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(RateCol) });
        if (c.BackCol) g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(RateCol) });
        return g;
    }

    /// <summary>
    /// A name with its marks after it (YOU, Combat Power): the marks follow the name and keep their room, the name is cut
    /// first when the line is short (a left-aligned grid whose star column shrinks to the name).
    /// </summary>
    public static Grid NameLine(FrameworkElement name, params FrameworkElement[] after)
    {
        var g = new Grid { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.Children.Add(name);
        for (int i = 0; i < after.Length; i++)
        {
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(after[i], i + 1); g.Children.Add(after[i]);
        }
        return g;
    }

    /// <summary>
    /// A meter row's line made to fit its <paramref name="room"/> in Steven's order: its heal and TOP marks stacked first (no
    /// loss), then the Combat Power goes, then the share (its slot kept empty, so the DPS never leaves its column), then an
    /// expanded row's damage and crit, and only then is the name cut (2026-11-20 review: names were cut while their CP still
    /// showed). The line is measured at its natural width (its star columns then size to their content), margins included.
    /// </summary>
    /// <param name="share">Shows (true) or hides (false) the share; its column stays.</param>
    /// <param name="detail">An expanded row's damage and crit on the same line.</param>
    /// <param name="stack">Puts the heal and TOP marks one over the other (true) or side by side (false).</param>
    public static void Crowd(FrameworkElement line, double room, UIElement? cp, bool cpWanted, Action<bool> share, UIElement? detail = null, Action<bool>? stack = null)
    {
        stack?.Invoke(false);
        if (cp != null) cp.Visibility = cpWanted ? Visibility.Visible : Visibility.Collapsed;
        share(true);
        bool detailWanted = detail != null && detail.Visibility == Visibility.Visible;
        if (room > 0)
        {
            bool Fits()
            {
                // every part of the line measured again (a measure of the line alone keeps its children's last sizes)
                void Dirty(DependencyObject d) { if (d is UIElement u) u.InvalidateMeasure(); for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++) Dirty(VisualTreeHelper.GetChild(d, i)); }
                Dirty(line);
                line.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                return line.DesiredSize.Width <= room + .5;
            }
            var steps = new List<Action>();
            if (stack != null) steps.Add(() => stack(true));
            if (cp != null && cpWanted) steps.Add(() => cp.Visibility = Visibility.Collapsed);
            steps.Add(() => share(false));
            if (detailWanted) steps.Add(() => detail!.Visibility = Visibility.Collapsed);
            foreach (var step in steps) { if (Fits()) break; step(); }
        }
        line.InvalidateMeasure();
    }

    public static int CritAt => 3;
    public static int BackAt(MemberCard c) => c.CritCol ? 4 : 3;

    public static T Put<T>(Grid g, T el, int col) where T : UIElement { Grid.SetColumn(el, col); g.Children.Add(el); return el; }

    /// <summary>A figure in its column: right-aligned, tabular digits.</summary>
    public static TextBlock Num(string s, double size, Brush fg, FontFamily face, FontWeight w)
    {
        var t = new TextBlock { Text = s, FontSize = size, Foreground = fg, FontFamily = face, FontWeight = w, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        System.Windows.Documents.Typography.SetNumeralAlignment(t, FontNumeralAlignment.Tabular);
        return t;
    }

    /// <summary>An ability's name, its specialties after it in <paramref name="picks"/> ("Pummel  3·4"), cut with an ellipsis when long.</summary>
    public static TextBlock Ability(CardAbility a, double size, Brush fg, FontFamily face, FontWeight w, Brush picks, double picksSize)
    {
        var t = new TextBlock { FontSize = size, Foreground = fg, FontFamily = face, FontWeight = w, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        t.Inlines.Add(new System.Windows.Documents.Run(a.Name));
        if (a.Picks.Length > 0) t.Inlines.Add(new System.Windows.Documents.Run("  " + a.Picks) { FontSize = picksSize, Foreground = picks });
        return t;
    }

    /// <summary>The foot line in its parts, each in its tone: hits, biggest, healed (green), deaths (red), dots between.</summary>
    public static TextBlock Foot(MemberCard c, double size, FontFamily face, FontWeight w, Brush plain, Brush healed, Brush deaths, Brush dot)
    {
        var t = new TextBlock { FontSize = size, FontFamily = face, FontWeight = w, Foreground = plain, TextWrapping = TextWrapping.Wrap };
        System.Windows.Documents.Typography.SetNumeralAlignment(t, FontNumeralAlignment.Tabular);
        foreach (var (s, b) in new[] { (c.Hits, plain), (c.Biggest, plain), (c.Healed, healed), (c.DeathsText, deaths) })
        {
            if (s.Length == 0) continue;
            if (t.Inlines.Count > 0) t.Inlines.Add(new System.Windows.Documents.Run("  ·  ") { Foreground = dot });
            t.Inlines.Add(new System.Windows.Documents.Run(s) { Foreground = b });
        }
        return t;
    }

    /// <summary>
    /// The card's entrance: <paramref name="step"/> 0 to 1 over <paramref name="ms"/> once it is first shown, with effects
    /// on; otherwise (a render, Windows's animations off) it is drawn whole at once. It never moves a figure.
    /// </summary>
    public static void Enter(FrameworkElement card, double ms, Action<double> step)
    {
        step(1);
        if (Motion.Still || Motion.FrozenAt != null || !SystemParameters.ClientAreaAnimation) return;
        bool played = false;
        card.Loaded += (_, _) =>
        {
            if (played) return;
            played = true;
            step(0);
            Motion.Play(card, Math.Min(ms, 290), step, () => step(1));
        };
    }
}
