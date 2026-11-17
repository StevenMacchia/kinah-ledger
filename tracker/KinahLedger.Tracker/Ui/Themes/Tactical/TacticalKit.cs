using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using KinahLedger.Parser;
using KinahLedger.Session;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

/// <summary>
/// What the Tactical theme's three looks share (Fighter, Scorebug, Holo): hard edges, dense figures, one accent each, and
/// motion that frames the figures (brackets, plates, scan lines) and never moves them. The parts: a drawn element
/// (<see cref="TacSketch"/>) for every line, ring and plate, so a moment changes a field and redraws instead of building
/// shapes; a text with a tight dark outline (<see cref="TacHalo"/>) for text straight over the game; the shapes (a clipped
/// glass, a slanted block); and the small sums the three looks make the same way.
/// </summary>
abstract class TacticalLook : HudThemeLook
{
    protected TacticalLook(bool bare, FontFamily face) : base(bare, face) { }

    /// <summary>A course's part from <paramref name="a"/> to <paramref name="b"/>, 0 before it and 1 after.</summary>
    protected static double Seg(double p, double a, double b) => Math.Clamp((p - a) / (b - a), 0, 1);

    /// <summary>Whether the line has an hour to show (else the waiting line).</summary>
    protected static bool HasHour(Figures f) => f.Counted && f.Rate.Length > 0;

    /// <summary>The ring of your best is shown: an hour counted, not warming, a best here, out of the Abyss.</summary>
    protected static bool HasRing(Figures f) => HasHour(f) && f.Share >= 0 && !f.Warm && !f.Abyss;

    /// <summary>The share of your best as a whole percent ("91").</summary>
    protected static string Pct(double share) => Math.Round(Math.Clamp(share, 0, 9.99) * 100).ToString("0", CultureInfo.InvariantCulture);

    /// <summary>
    /// The way to your best as a short figure: "−130K" from "130K to your best here", "NEW" once beaten; empty with no best.
    /// </summary>
    protected static string Gap(Figures f)
    {
        if (f.ToBest.Length == 0) return "";
        if (f.Beat) return L.T("NEW");
        const string tail = " to your best here";
        return f.ToBest.EndsWith(tail, StringComparison.Ordinal) ? "−" + f.ToBest[..^tail.Length] : f.ToBest;
    }

    /// <summary>The timer's tone, said once for the three looks.</summary>
    protected static TimerBadge.Tone ToneOf(TimerBoard.Item i, DateTime now) => TimerBadge.Of(i, now).Tone;

    /// <summary>
    /// The meter's foot as the Parser's (ParserFoot.Line) says it, for a look that draws its own: the fight's total, the
    /// group's figure and its word ("Group DPS", "Your DPS" alone on the meter, "(boss pulls)" in a run), and whether it is
    /// the held fight's average (the AVG. DPS badge).
    /// </summary>
    protected static (string Total, string GroupWord, string Group, bool Held) FootOf(Encounter e, DateTime now)
    {
        bool held = e.HeldAverage;
        bool pulls = e.ModeKind == ParserMode.Run && (e.BossView || !e.TargetsSplit);
        bool solo = ParserFoot.SoloYou(e);
        string word = L.T(solo ? (held ? "You" : pulls ? "Your DPS (boss pulls)" : "Your DPS") : held ? "Group" : pulls ? "Group DPS (boss pulls)" : "Group DPS");
        return (Amounts.Meter(e.Credited), word, e.GroupDpsText(held ? e.End ?? now : now), held);
    }

    /// <summary>A look's own foot marked as the meter's foot (its words read aloud as the Parser's are).</summary>
    protected static T AsFoot<T>(T foot, Encounter e, DateTime now) where T : FrameworkElement
    {
        foot.Tag = "parser-foot";
        System.Windows.Automation.AutomationProperties.SetName(foot, e.FootText(now));
        return foot;
    }

    /// <summary>A bar's share shown through <paramref name="apply"/>: slid there while effects play, else set (Motion.Slide's rules).</summary>
    protected static void SlideBar(UIElement el, MeterContext c, string part, double share, Action<double> apply)
    {
        if (c.Moves) Motion.Slide(el, c.Key + "|" + part, share, apply);
        else apply(share);
    }

    /// <summary>A text in a face, tabular digits, never cut.</summary>
    protected static TextBlock Tt(string s, double size, Brush fg, FontFamily face, FontWeight weight, FontStretch? stretch = null) => Tac.Text(s, size, fg, face, weight, stretch);
}

static class Tac
{
    public static SolidColorBrush C(uint rgb, double a = 1) => B(rgb, a);

    public static TextBlock Text(string s, double size, Brush fg, FontFamily face, FontWeight weight, FontStretch? stretch = null)
    {
        var t = new TextBlock
        {
            Text = s, FontSize = size, Foreground = fg, FontFamily = face, FontWeight = weight, FontStretch = stretch ?? FontStretches.Normal,
            VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.None,
        };
        System.Windows.Documents.Typography.SetNumeralAlignment(t, FontNumeralAlignment.Tabular);
        return t;
    }

    /// <summary>A rectangle with its corners cut at 45° (<paramref name="cut"/> long): top-left and bottom-right, or all four.</summary>
    public static Geometry Cut(Rect r, double cut, bool all = false)
    {
        double c = Math.Min(cut, Math.Min(r.Width, r.Height) / 2);
        var g = new StreamGeometry();
        using (var x = g.Open())
        {
            x.BeginFigure(new Point(r.Left + c, r.Top), true, true);
            if (all) { x.LineTo(new Point(r.Right - c, r.Top), true, false); x.LineTo(new Point(r.Right, r.Top + c), true, false); }
            else x.LineTo(new Point(r.Right, r.Top), true, false);
            x.LineTo(new Point(r.Right, r.Bottom - c), true, false);
            x.LineTo(new Point(r.Right - c, r.Bottom), true, false);
            if (all) { x.LineTo(new Point(r.Left + c, r.Bottom), true, false); x.LineTo(new Point(r.Left, r.Bottom - c), true, false); }
            else x.LineTo(new Point(r.Left, r.Bottom), true, false);
            x.LineTo(new Point(r.Left, r.Top + c), true, false);
        }
        g.Freeze();
        return g;
    }

    /// <summary>A block slanted as a broadcast's (its top <paramref name="lean"/> right of its foot), inside <paramref name="r"/>.</summary>
    public static Geometry Lean(Rect r, double lean)
    {
        var g = new StreamGeometry();
        using (var x = g.Open())
        {
            x.BeginFigure(new Point(r.Left + lean, r.Top), true, true);
            x.LineTo(new Point(r.Right, r.Top), true, false);
            x.LineTo(new Point(r.Right - lean, r.Bottom), true, false);
            x.LineTo(new Point(r.Left, r.Bottom), true, false);
        }
        g.Freeze();
        return g;
    }

    /// <summary>The corners of a rectangle cut at 45° on all four corners, clockwise from the top-left cut's top end.</summary>
    public static Point[] CutPoints(Rect r, double c, bool all = false)
    {
        c = Math.Min(c, Math.Min(r.Width, r.Height) / 2);
        return all
            ? new[] { new Point(r.Left + c, r.Top), new Point(r.Right - c, r.Top), new Point(r.Right, r.Top + c), new Point(r.Right, r.Bottom - c), new Point(r.Right - c, r.Bottom), new Point(r.Left + c, r.Bottom), new Point(r.Left, r.Bottom - c), new Point(r.Left, r.Top + c) }
            : new[] { new Point(r.Left + c, r.Top), new Point(r.Right, r.Top), new Point(r.Right, r.Bottom - c), new Point(r.Right - c, r.Bottom), new Point(r.Left, r.Bottom), new Point(r.Left, r.Top + c) };
    }

    /// <summary>
    /// A closed outline drawn part of the way round (0..1 of its length, from its first point): an outline tracing itself
    /// in. <paramref name="head"/> gets the point the line has reached.
    /// </summary>
    public static Geometry Trace(IReadOnlyList<Point> pts, double f, out Point head, double from = 0)
    {
        double total = 0;
        for (int i = 0; i < pts.Count; i++) total += (pts[(i + 1) % pts.Count] - pts[i]).Length;
        double start = total * Math.Clamp(from, 0, 1), end = total * Math.Clamp(f, 0, 1), at = 0;
        var g = new StreamGeometry();
        head = pts[0];
        using (var x = g.Open())
        {
            bool begun = false;
            for (int i = 0; i < pts.Count && at < end; i++)
            {
                var a = pts[i]; var b = pts[(i + 1) % pts.Count];
                double len = (b - a).Length;
                if (len <= 0) continue;
                if (at + len > start)
                {
                    if (!begun) { x.BeginFigure(a + (b - a) * (Math.Max(0, start - at) / len), false, from <= 0 && f >= 1); begun = true; }
                    head = end >= at + len ? b : a + (b - a) * ((end - at) / len);
                    x.LineTo(head, true, true);
                }
                at += len;
            }
        }
        g.Freeze();
        return g;
    }

    /// <summary>A pen, frozen.</summary>
    public static Pen P(Brush b, double w, bool round = false, DashStyle? dash = null)
    {
        var p = new Pen(b, w) { LineJoin = round ? PenLineJoin.Round : PenLineJoin.Miter, StartLineCap = round ? PenLineCap.Round : PenLineCap.Flat, EndLineCap = round ? PenLineCap.Round : PenLineCap.Flat };
        if (dash != null) p.DashStyle = dash;
        p.Freeze();
        return p;
    }

    /// <summary>A gradient across (left to right) or down, frozen.</summary>
    public static LinearGradientBrush Grad(bool down, params (Color C, double At)[] stops)
    {
        var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = down ? new Point(0, 1) : new Point(1, 0) };
        foreach (var (c, at) in stops) b.GradientStops.Add(new GradientStop(c, at));
        b.Freeze();
        return b;
    }

    public static Color Mix(Color a, Color b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return Color.FromArgb((byte)(a.A + (b.A - a.A) * t), (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
    }

    public static Color A(Color c, double a) => Color.FromArgb((byte)Math.Round(255 * Math.Clamp(a, 0, 1)), c.R, c.G, c.B);

    /// <summary>A canvas of no size, for an effect that hangs off a part without moving the layout (it never takes a click).</summary>
    public static Canvas Anchor(HorizontalAlignment h = HorizontalAlignment.Left, VerticalAlignment v = VerticalAlignment.Top) =>
        new() { Width = 0, Height = 0, HorizontalAlignment = h, VerticalAlignment = v, IsHitTestVisible = false };
}

/// <summary>
/// A drawn part: whatever <see cref="Draw"/> paints in its size. Its own size is set (Width, Height) or given by its parent.
/// A moment changes the look's fields and calls <see cref="UIElement.InvalidateVisual"/>: nothing is built while it plays.
/// </summary>
sealed class TacSketch : FrameworkElement
{
    public Action<DrawingContext, Size>? Draw;
    public TacSketch(Action<DrawingContext, Size> draw, double w = double.NaN, double h = double.NaN)
    {
        Draw = draw; Width = w; Height = h;
        IsHitTestVisible = false;
        SnapsToDevicePixels = true;
    }
    protected override void OnRender(DrawingContext dc) => Draw?.Invoke(dc, RenderSize);
}

/// <summary>
/// A text straight over the game with a tight dark outline round every glyph (a jet HUD's lines over snow and sky): the
/// glyphs' outline drawn under the text, a hard ring and a softer one, so it reads on any scene without a panel. The text
/// is the child (a TextBlock, so it reads as the look's text); set it through <see cref="Text"/> so the outline follows.
/// </summary>
sealed class TacHalo : Decorator
{
    readonly Pen _hard, _soft;
    public TextBlock Block { get; }

    public TacHalo(TextBlock t, Color ink, double width = 3.2)
    {
        Block = t;
        Child = t;
        // ideal glyph positions, as the outline's geometry has them (display mode drifts from it along a long line)
        TextOptions.SetTextFormattingMode(t, TextFormattingMode.Ideal);
        _hard = Tac.P(new SolidColorBrush(ink), width, round: true);
        _soft = Tac.P(new SolidColorBrush(Tac.A(ink, .45)), width + 1.6, round: true);
        VerticalAlignment = VerticalAlignment.Center;
    }

    public string Text { get => Block.Text; set { if (Block.Text == value) return; Block.Text = value; InvalidateVisual(); } }
    public Brush Fill { set { Block.Foreground = value; } }

    protected override void OnRender(DrawingContext dc)
    {
        var t = Block;
        if (t.Text.Length == 0 || t.Visibility != Visibility.Visible) return;
        var ft = new FormattedText(t.Text, CultureInfo.CurrentUICulture, t.FlowDirection, new Typeface(t.FontFamily, t.FontStyle, t.FontWeight, t.FontStretch),
            t.FontSize, Brushes.Black, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        Point o;
        try { o = t.TranslatePoint(new Point(t.Padding.Left, t.Padding.Top), this); } catch (InvalidOperationException) { o = new Point(); }
        var geo = ft.BuildGeometry(o);
        dc.DrawGeometry(null, _soft, geo);
        dc.DrawGeometry(null, _hard, geo);
    }
}
