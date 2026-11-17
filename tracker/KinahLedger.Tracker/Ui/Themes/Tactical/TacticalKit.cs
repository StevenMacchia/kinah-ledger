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
        // the figure in "130K to your best here" (or its translation: the figure is not always first)
        var m = System.Text.RegularExpressions.Regex.Match(f.ToBest, @"\d[\d.,]*[KMBkmb]?");
        return m.Success ? "−" + m.Value : f.ToBest;
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
        var (word, value, held) = ParserFoot.Right(e, now);   // the group's DPS; alone, your best or peak (none: GroupWord "")
        return (Amounts.Meter(e.Credited), word, value, held);
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

    // ---- the members' skills cards (IThemeCard): what the three looks' cards share

    public static double Clamp01(double x) => Math.Clamp(x, 0, 1);

    /// <summary>The room a right-aligned figure leaves free at the left of its column (<paramref name="col"/> wide), less 6 px of air.</summary>
    public static double FreeLeft(TextBlock fig, double col)
    {
        if (col <= 0) return 0;
        fig.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return Math.Max(0, col - fig.DesiredSize.Width - fig.Margin.Right - 6);
    }

    /// <summary>
    /// A meter row's name line fitted to its room (Steven, 2026-11-16: Combat Power beside every name): the name whole when it
    /// fits; else the parts give way in <paramref name="give"/>'s order, each to its short form (a TOP DPS plate to its star,
    /// "CP 4.8K" to its compact form, the YOU tag to none: the row's own frame says it), and only then is the name cut (to
    /// <paramref name="nameMin"/> at the least: the marks are never cut at the line's end). <paramref name="keep"/>: the parts
    /// that never give way (the CP in a look that always shows it compact, a death, the heal figure).
    /// </summary>
    /// <param name="nameFloor">The name gives way first, down to this (its marks keep their full form while it can); infinite: the marks first.</param>
    public static void FitName(TextBlock name, double room, IReadOnlyList<FrameworkElement> keep, IReadOnlyList<(FrameworkElement Full, FrameworkElement? Short)> give, double nameMin = 22, double nameFloor = double.PositiveInfinity)
    {
        var inf = new Size(double.PositiveInfinity, double.PositiveInfinity);
        name.ClearValue(FrameworkElement.MaxWidthProperty);
        name.Measure(inf);
        double nw = name.DesiredSize.Width, fixedW = keep.Sum(e => W(e)), least = Math.Min(nw, nameFloor);
        var full = give.Select(p => W(p.Full)).ToList();
        var shrt = give.Select(p => p.Short is { } s ? W(s) : 0).ToList();
        int level = give.Count;
        for (int i = 0; i <= give.Count; i++)
        {
            double m = fixedW;
            for (int j = 0; j < give.Count; j++) m += j < i ? shrt[j] : full[j];
            if (least + m <= room) { level = i; break; }
        }
        double marks = fixedW;
        for (int j = 0; j < give.Count; j++)
        {
            bool cut = j < level;
            give[j].Full.Visibility = cut ? Visibility.Collapsed : Visibility.Visible;
            if (give[j].Short is { } s) s.Visibility = cut ? Visibility.Visible : Visibility.Collapsed;
            marks += cut ? shrt[j] : full[j];
        }
        name.MaxWidth = Math.Max(Math.Min(nameMin, nw), room - marks);
    }

    /// <summary>A part's width laid out on its own (shown for the measure, then as it was).</summary>
    static double W(FrameworkElement e)
    {
        var v = e.Visibility; e.Visibility = Visibility.Visible;
        e.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double w = e.DesiredSize.Width; e.Visibility = v;
        return w;
    }

    /// <summary>A name of up to this many characters always shows whole; a longer one keeps at least <see cref="NameKeepLong"/> and its "…".</summary>
    public const int NameKeep = 10, NameKeepLong = 6;

    /// <summary>
    /// The least room a name keeps: whole up to <see cref="NameKeep"/> characters, else its first <see cref="NameKeepLong"/> and
    /// "…" (a long name gives way before a mark goes to a second line).
    /// </summary>
    public static double NameFloor(TextBlock name)
    {
        name.ClearValue(FrameworkElement.MaxWidthProperty);
        name.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double nw = name.DesiredSize.Width;
        if (name.Text.Length <= NameKeep) return nw;
        return Math.Min(nw, TextW(name, name.Text[..NameKeepLong] + "…") + 1);
    }

    /// <summary>A text's width in a block's own face, size and weight (a figure column sized for its widest form).</summary>
    public static double TextW(TextBlock like, string s)
    {
        var probe = new TextBlock { Text = s, FontFamily = like.FontFamily, FontSize = like.FontSize, FontWeight = like.FontWeight, FontStretch = like.FontStretch, FontStyle = like.FontStyle, Padding = like.Padding };
        TextOptions.SetTextFormattingMode(probe, TextOptions.GetTextFormattingMode(like));
        System.Windows.Documents.Typography.SetNumeralAlignment(probe, System.Windows.Documents.Typography.GetNumeralAlignment(like));
        probe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return Math.Ceiling(probe.DesiredSize.Width);
    }

    /// <summary>A figure column's width: the widest of <paramref name="forms"/> in the block's face, its margins and a pixel of air.</summary>
    public static double FigCol(TextBlock fig, params string[] forms) => forms.Max(f => TextW(fig, f)) + fig.Margin.Left + fig.Margin.Right + 1;

    /// <summary>
    /// A meter row's name and its marks on one line, or two when they can't share one (Steven, 2026-11-17: "a lot of numbers in
    /// all the meters you can't see, it's cut off!"): the name keeps <see cref="NameFloor"/> on its line (a 10-character name
    /// whole), the marks after it in <paramref name="order"/> move to <paramref name="sub"/> from the last until the line fits
    /// (a part and its short form move together), and each line's marks then take their full form where they fit (FitName).
    /// Parts in <paramref name="keep"/> or <paramref name="give"/> that aren't in <paramref name="order"/> (a CP at the plate's
    /// end) count against the first line. Returns true when the second line is in use.
    /// </summary>
    public static bool FitLines(TextBlock name, double room, double subRoom, Panel line, Panel sub, IReadOnlyList<FrameworkElement> order,
        IReadOnlyList<FrameworkElement> keep, IReadOnlyList<(FrameworkElement Full, FrameworkElement? Short)> give)
    {
        // the marks in groups: a part with its short form
        var groups = new List<List<FrameworkElement>>();
        foreach (var e in order)
        {
            var pair = give.FirstOrDefault(p => ReferenceEquals(p.Short, e));
            if (pair.Full != null && groups.Count > 0 && groups[^1].Contains(pair.Full)) groups[^1].Add(e);
            else groups.Add(new List<FrameworkElement> { e });
        }
        double floor = NameFloor(name);
        int n = groups.Count, k = n;
        for (int i = 0; i <= n; i++)
        {
            var down = groups.Skip(n - i).SelectMany(g => g).ToHashSet();
            double least = keep.Where(e => !down.Contains(e)).Sum(W) + give.Where(p => !down.Contains(p.Full)).Sum(p => p.Short is { } s ? W(s) : 0);
            if (floor + least <= room) { k = i; break; }
        }
        var under = groups.Skip(n - k).SelectMany(g => g).ToList();
        var lower = under.ToHashSet();
        // the children where they go (only when they move: a part re-added is laid out again)
        var top = new List<UIElement> { name };
        top.AddRange(order.Where(e => !lower.Contains(e)));
        if (!line.Children.Cast<UIElement>().SequenceEqual(top) || !sub.Children.Cast<UIElement>().SequenceEqual(under))
        {
            line.Children.Clear(); sub.Children.Clear();
            foreach (var e in top) line.Children.Add(e);
            foreach (var e in under) sub.Children.Add(e);
        }
        FitName(name, room, keep.Where(e => !lower.Contains(e)).ToList(), give.Where(p => !lower.Contains(p.Full)).ToList(), floor, floor);
        // the second line's marks: full where they fit, else short in the give's order
        var subGive = give.Where(p => lower.Contains(p.Full)).ToList();
        if (subGive.Count > 0)
        {
            var dummy = new TextBlock();
            FitName(dummy, subRoom, keep.Where(lower.Contains).ToList(), subGive, 0, 0);
        }
        return k > 0;
    }

    /// <summary>A rate column's width on a card (crit, back attacks).</summary>
    public const double CardRate = 36;

    /// <summary>
    /// A card's entrance: <paramref name="step"/> run from 0 to 1 over <paramref name="ms"/> (under 300) once the card shows; set
    /// at 1 at once, and left there in a render, a test, a held frame or with Windows's animations off (the card is complete).
    /// </summary>
    public static void CardIn(FrameworkElement card, double ms, Action<double> step)
    {
        step(1);
        if (Motion.Still || !Motion.On || Motion.FrozenAt != null) return;
        card.Loaded += (_, _) => { step(0); Motion.Play(card, ms, step, () => step(1)); };
    }

    /// <summary>An ability table's columns: the name (the rest of the width), its share, its damage, then crit and back when the card has them.</summary>
    public static Grid CardGrid(MemberCard c, double share, double damage, double height = double.NaN)
    {
        var g = new Grid { Height = height };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(share) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(damage) });
        if (c.CritCol) g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(CardRate) });
        if (c.BackCol) g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(CardRate) });
        return g;
    }

    /// <summary>A cell of an ability table (column 1 share, 2 damage, 3 crit, 4 back: back moves to 3 without crit).</summary>
    public static T Cell<T>(Grid g, T el, int col, MemberCard c) where T : UIElement
    {
        if (col == 4 && !c.CritCol) col = 3;
        Grid.SetColumn(el, col);
        g.Children.Add(el);
        return el;
    }

    /// <summary>An ability's name and, after it, the specialties its hits named in <paramref name="picks"/>'s colour ("Pummel  3·4").</summary>
    public static TextBlock AbilityName(CardAbility a, double size, Brush fg, FontFamily face, FontWeight w, Brush picks, double picksSize, FontStretch? stretch = null)
    {
        var t = Text("", size, fg, face, w, stretch);
        t.TextTrimming = TextTrimming.CharacterEllipsis;
        t.Inlines.Add(new System.Windows.Documents.Run(a.Name));
        if (a.Picks.Length > 0) t.Inlines.Add(new System.Windows.Documents.Run("  " + a.Picks) { FontSize = picksSize, FontWeight = FontWeights.Bold, Foreground = picks });
        return t;
    }

    /// <summary>An ability row's hover line: its whole name (a long one is cut on the card) and its hits.</summary>
    public static string AbilityTip(CardAbility a) => a.Tip.Length > 0 ? a.Name + "\n" + a.Tip : a.Name;

    /// <summary>The rates line as labelled figures: (word, figure) for the crit rate, the crits' share of the damage and the share from behind.</summary>
    public static List<(string Word, string Fig)> Rates(MemberCard c)
    {
        var l = new List<(string, string)>();
        if (c.Crit.Length > 0) l.Add((L.T("crit"), c.Crit));
        if (c.CritShare.Length > 0) l.Add((L.T("from crits"), c.CritShare));
        if (c.Back.Length > 0) l.Add((L.T("from behind"), c.Back));
        return l;
    }

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
