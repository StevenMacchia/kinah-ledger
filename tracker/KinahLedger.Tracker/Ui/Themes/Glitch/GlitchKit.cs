using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using KinahLedger.Parser;
using KinahLedger.Session;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

/// <summary>
/// The Glitch theme's shared parts (VHS, Corrupted, Terminal): a haunted tape's lo-fi, analogue, eerie and crisp family.
/// Monospace faces, scanlines, a streaky tape noise, chromatic splits fixed behind steady figures, and a panel with its
/// own effect layer (<see cref="GlitchPanel"/>). Every effect is cheap for the layered window: tiled brushes, plain
/// rectangles and duplicate text, no blur.
/// </summary>
static class GlitchKit
{
    /// <summary>VHS: a camcorder's OSD (Lucida Console). Corrupted: Consolas. Terminal: Cascadia Mono.</summary>
    public static readonly FontFamily VhsFace = HudThemes.Console;
    public static readonly FontFamily CodeFace = new("Consolas, Cascadia Mono, Lucida Console");
    public static readonly FontFamily CrtFace = HudThemes.Mono;

    public static SolidColorBrush Br(uint rgb, double a = 1) => B(rgb, a);

    /// <summary>A text in a look's face: tabular digits, never trimmed unless asked.</summary>
    public static TextBlock Txt(string s, double size, Brush fg, FontFamily face, FontWeight? w = null, bool trim = false)
    {
        var t = new TextBlock
        {
            Text = s, FontSize = size, Foreground = fg, FontFamily = face, FontWeight = w ?? FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = trim ? TextTrimming.CharacterEllipsis : TextTrimming.None,
        };
        System.Windows.Documents.Typography.SetNumeralAlignment(t, FontNumeralAlignment.Tabular);
        return t;
    }

    public static System.Windows.Documents.Run Rn(string s, Brush fg, double? size = null, FontWeight? w = null)
    {
        var r = new System.Windows.Documents.Run(s) { Foreground = fg };
        if (size is double z) r.FontSize = z;
        if (w is FontWeight ww) r.FontWeight = ww;
        return r;
    }

    /// <summary>An element's width as it would lay out on its own (a figure's, before it joins its row).</summary>
    public static double Wide(FrameworkElement e)
    {
        e.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return e.DesiredSize.Width;
    }

    /// <summary>
    /// The pickup strip (Ui/HudPickStrip.cs) when it fits the <paramref name="room"/> DIPs a look has left beside its figures
    /// (Steven, 2026-11-18: "too much empty space in some of the buddies"): the best drop and the last three, whole, or null
    /// when they don't fit there (the look then gives them a line of their own: <see cref="StripUnder"/>; Steven, 2026-11-19:
    /// "it need to show up on ALL buddies"). Null too before the first pickup, or with Settings › Buddy › Pickup icons off.
    /// </summary>
    public static FrameworkElement? FitStrip(IReadOnlyDictionary<string, string> v, PickStrip.Look look, double room) =>
        PickStrip.Width(look) <= room ? PickStrip.Of(v, look) : null;

    /// <summary>The strip in a figure row (a grid of three columns: the figures, the room, a block at the right) beside the figures, at the room's right.</summary>
    public static void StripBeside(Grid row, FrameworkElement strip)
    {
        strip.HorizontalAlignment = HorizontalAlignment.Right; strip.Margin = new Thickness(14, 0, 0, 0);
        Grid.SetColumn(strip, 1); row.Children.Add(strip);
    }

    /// <summary>
    /// The strip on a line of its own under a figure row's figures, at their left (the row gets a second row for it): where
    /// the figures leave no room beside them, so the panel never grows past the meter's width and the strip is never dropped.
    /// The row's caller clears its row definitions with its columns.
    /// </summary>
    public static void StripUnder(Grid row, FrameworkElement strip)
    {
        if (row.RowDefinitions.Count == 0) { row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); }
        strip.HorizontalAlignment = HorizontalAlignment.Left; strip.Margin = new Thickness(0, 6, 0, 1);
        Grid.SetRow(strip, 1); Grid.SetColumnSpan(strip, Math.Max(1, row.ColumnDefinitions.Count)); row.Children.Add(strip);
    }

    /// <summary>The strip beside a figure row's figures in <paramref name="room"/>, else under them; nothing before the first pickup or with the icons off.</summary>
    public static void StripInRow(Grid row, IReadOnlyDictionary<string, string> v, PickStrip.Look look, double room)
    {
        if (FitStrip(v, look, room) is { } beside) StripBeside(row, beside);
        else if (PickStrip.Of(v, look) is { } under) StripUnder(row, under);
    }

    /// <summary>
    /// A name in capitals, as a tape's OSD writes it. A word joiner (zero width) between an A and a P, so a name never reads
    /// as the Abyss Points' "AP" ("KASHAPA" stays a name: the overlay's words for the Abyss are its own).
    /// </summary>
    public static string Caps(string s) => s.ToUpper(CultureInfo.CurrentCulture).Replace("AP", "A⁠P");

    /// <summary>
    /// A figure with a chromatic split: two coloured copies fixed <paramref name="dx"/> px left and right BEHIND the steady
    /// figure (the split never moves; the figure is drawn once on top).
    /// </summary>
    public static Grid Split(string s, double size, FontFamily face, FontWeight w, Brush fg, Color left, Color right, double dx, double dy = 0)
    {
        var g = new Grid { VerticalAlignment = VerticalAlignment.Center };
        var a = Txt(s, size, Frozen(left), face, w); a.RenderTransform = new TranslateTransform(-dx, -dy);
        var b = Txt(s, size, Frozen(right), face, w); b.RenderTransform = new TranslateTransform(dx, dy);
        g.Children.Add(a); g.Children.Add(b); g.Children.Add(Txt(s, size, fg, face, w));
        return g;
    }

    /// <summary>A phosphor glow round a figure: faint copies a hair off on every side, behind it (no blur, cheap).</summary>
    public static Grid Phosphor(string s, double size, FontFamily face, FontWeight w, Brush fg, Color glow, double reach = 0.9, double alpha = 0.3)
    {
        var g = new Grid { VerticalAlignment = VerticalAlignment.Center };
        var near = Frozen(Color.FromArgb((byte)(255 * alpha), glow.R, glow.G, glow.B));
        var far = Frozen(Color.FromArgb((byte)(255 * alpha * 0.45), glow.R, glow.G, glow.B));
        foreach (var (x, y, br) in new[] { (reach, 0.0, near), (-reach, 0.0, near), (0.0, reach, near), (0.0, -reach, near), (reach * 1.8, 0.0, far), (-reach * 1.8, 0.0, far), (0.0, reach * 1.8, far), (0.0, -reach * 1.8, far) })
        {
            var c = Txt(s, size, br, face, w); c.RenderTransform = new TranslateTransform(x, y); g.Children.Add(c);
        }
        g.Children.Add(Txt(s, size, fg, face, w));
        return g;
    }

    // ---- textures

    /// <summary>Scanlines: a <paramref name="thick"/> px line of <paramref name="c"/> every <paramref name="period"/> px.</summary>
    public static Brush Lines(Color c, double period = 3, double thick = 1)
    {
        var g = new DrawingGroup();
        g.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, 4, period))));
        g.Children.Add(new GeometryDrawing(Frozen(c), null, new RectangleGeometry(new Rect(0, 0, 4, thick))));
        var b = new DrawingBrush(g) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 4, period), ViewportUnits = BrushMappingMode.Absolute, Stretch = Stretch.Fill };
        b.Freeze();
        return b;
    }

    /// <summary>Vertical stripes (a level meter's segments): a <paramref name="thick"/> px gap every <paramref name="period"/> px.</summary>
    public static Brush Segments(Color gap, double period = 4, double thick = 1)
    {
        var g = new DrawingGroup();
        g.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, period, 4))));
        g.Children.Add(new GeometryDrawing(Frozen(gap), null, new RectangleGeometry(new Rect(period - thick, 0, thick, 4))));
        var b = new DrawingBrush(g) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, period, 4), ViewportUnits = BrushMappingMode.Absolute, Stretch = Stretch.Fill };
        b.Freeze();
        return b;
    }

    /// <summary>A tape's noise: grey streaks held a few pixels along each row (VHS noise runs sideways), drawn once.</summary>
    public static readonly BitmapSource Noise = MakeNoise(160, 48, 7);
    static BitmapSource MakeNoise(int w, int h, int seed)
    {
        var r = new Random(seed);
        var px = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
        {
            double row = r.NextDouble(), v = r.NextDouble();
            for (int x = 0; x < w; x++)
            {
                if (r.NextDouble() < 0.3) v = r.NextDouble();
                double l = Math.Clamp(0.6 * v + 0.4 * row, 0, 1);
                l = l * l;   // mostly dark, a few bright streaks
                int i = (y * w + x) * 4;
                px[i] = px[i + 1] = px[i + 2] = (byte)(40 + 215 * l);
                px[i + 3] = (byte)(255 * Math.Clamp(0.25 + l * 1.1, 0, 1));
            }
        }
        var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, px, w * 4);
        bmp.Freeze();
        return bmp;
    }

    /// <summary>The noise as a brush, stretched sideways into streaks; not frozen, so an effect can slide it (<see cref="Shift"/>).</summary>
    public static ImageBrush NoiseBrush(double opacity = 1)
    {
        var b = new ImageBrush(Noise) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 320, 48), ViewportUnits = BrushMappingMode.Absolute, Stretch = Stretch.Fill, Opacity = opacity };
        RenderOptions.SetBitmapScalingMode(b, BitmapScalingMode.NearestNeighbor);
        return b;
    }

    /// <summary>The noise slid to a step of its course (a new grain each step, so the static seethes; the figures never move).</summary>
    public static void Shift(ImageBrush b, double p, int steps = 24)
    {
        int s = (int)(p * steps);
        b.Viewport = new Rect(-(s * 73 % 320), -(s * 29 % 48), 320, 48);
    }

    /// <summary>A soft top and bottom for a band (opaque in the middle).</summary>
    public static readonly Brush SoftEdges = MakeSoft();
    static Brush MakeSoft()
    {
        var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        b.GradientStops.Add(new GradientStop(Colors.Transparent, 0));
        b.GradientStops.Add(new GradientStop(Colors.Black, 0.35));
        b.GradientStops.Add(new GradientStop(Colors.Black, 0.65));
        b.GradientStops.Add(new GradientStop(Colors.Transparent, 1));
        b.Freeze();
        return b;
    }

    /// <summary>A vignette: clear in the middle, <paramref name="edge"/> of <paramref name="c"/> at the rim.</summary>
    public static Brush Vignette(Color c, double edge, double centreAlpha = 0)
    {
        var b = new RadialGradientBrush { Center = new Point(.5, .5), GradientOrigin = new Point(.5, .5), RadiusX = .75, RadiusY = .9 };
        b.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(255 * centreAlpha), c.R, c.G, c.B), 0));
        b.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(255 * centreAlpha), c.R, c.G, c.B), .55));
        b.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(255 * edge), c.R, c.G, c.B), 1));
        b.Freeze();
        return b;
    }

    /// <summary>A camcorder's corner marks: four L brackets <paramref name="arm"/> px long, <paramref name="inset"/> px in.</summary>
    public static Grid Corners(Brush stroke, double arm, double thick, double inset)
    {
        var g = new Grid { IsHitTestVisible = false };
        string a = arm.ToString(CultureInfo.InvariantCulture);
        foreach (var (h, v, data) in new[]
        {
            (HorizontalAlignment.Left, VerticalAlignment.Top, $"M0,{a} L0,0 L{a},0"),
            (HorizontalAlignment.Right, VerticalAlignment.Top, $"M0,0 L{a},0 L{a},{a}"),
            (HorizontalAlignment.Left, VerticalAlignment.Bottom, $"M0,0 L0,{a} L{a},{a}"),
            (HorizontalAlignment.Right, VerticalAlignment.Bottom, $"M0,{a} L{a},{a} L{a},0"),
        })
        {
            g.Children.Add(new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse(data), Stroke = stroke, StrokeThickness = thick, HorizontalAlignment = h, VerticalAlignment = v, Margin = new Thickness(inset),
                StrokeStartLineCap = PenLineCap.Square, StrokeEndLineCap = PenLineCap.Square, SnapsToDevicePixels = true,
            });
        }
        return g;
    }

    // ---- the run clock (the window's "time": "RUN 42:17" during a run)

    /// <summary>The clock's figures and its seconds' parity (a tiny idle blink follows it, rule 5: no timer of its own).</summary>
    public static (bool Run, string Clock, bool Odd) RunClock(IReadOnlyDictionary<string, string> v)
    {
        var (run, clock) = Time(v.TryGetValue("time", out var s) ? s : "");
        bool odd = clock.Length > 0 && char.IsDigit(clock[^1]) && (clock[^1] - '0') % 2 == 1;
        return (run, clock, odd);
    }

    /// <summary>"42:17" as a tape's counter: "0:42:17" with the hours always there, "00:42:17".</summary>
    public static string Timecode(string clock)
    {
        var parts = clock.Split(':');
        if (parts.Length == 0 || parts.Any(p => p.Length == 0 || !p.All(char.IsDigit))) return "00:00:00";
        var nums = parts.Select(p => int.Parse(p, CultureInfo.InvariantCulture)).ToList();
        while (nums.Count < 3) nums.Insert(0, 0);
        return $"{nums[^3]:00}:{nums[^2]:00}:{nums[^1]:00}";
    }

    // ---- the dungeon run (the "run*" values in RunBuddy's words, Ui/HudRunBuddy.cs): what every Glitch look draws it with

    /// <summary>The run's state while it stands in for the Kinah line: none with the line hidden for a fight or a rare drop held.</summary>
    public static RunBuddy.State? RunOf(IReadOnlyDictionary<string, string> v, bool kinahHidden) => !kinahHidden && Rare(v) == null ? RunBuddy.Of(v) : null;

    /// <summary>The count's forms, fullest first: "Run 3 today · 38 clears", "Run 3 today", "" (its parts go before a name is cut).</summary>
    public static string[] CountForms(RunBuddy.State s)
    {
        var parts = RunBuddy.Count(s).Split(" · ", StringSplitOptions.RemoveEmptyEntries);
        var forms = new List<string>();
        for (int n = parts.Length; n >= 1; n--) forms.Add(string.Join(" · ", parts.Take(n)));
        forms.Add("");
        return forms.ToArray();
    }

    /// <summary>"Cleared in {0}" round its clock, in the language's order: the words before it and after it.</summary>
    public static (string Before, string After) ClearedIn()
    {
        var fmt = L.T("Cleared in {0}");
        int at = fmt.IndexOf("{0}", StringComparison.Ordinal);
        return at < 0 ? (fmt + " ", "") : (fmt[..at], fmt[(at + 3)..]);
    }

    /// <summary>Ahead of your fastest run's pace, behind it, or even: its brush.</summary>
    public static Brush PaceTone(int sign, Brush ahead, Brush behind, Brush even) => sign < 0 ? ahead : sign > 0 ? behind : even;

    /// <summary>
    /// A figure row's form in <paramref name="cap"/> DIPs, with the pickup strip in the room it leaves: the first of the
    /// <paramref name="whole"/> forms that keep every figure (they drop words only, "Cleared in") with room for the strip, so
    /// the strip goes before a figure and no gap stands where it was; else the fullest form that fits, the strip if it still fits.
    /// </summary>
    public static (FrameworkElement L, FrameworkElement? R, FrameworkElement? Strip) FigForm(IReadOnlyDictionary<string, string> v, PickStrip.Look look, double cap,
        IReadOnlyList<(FrameworkElement L, FrameworkElement? R)> forms, int whole)
    {
        static double W((FrameworkElement L, FrameworkElement? R) f) => Wide(f.L) + (f.R != null ? Wide(f.R) : 0);
        for (int i = 0; i < Math.Min(whole, forms.Count); i++)
            if (W(forms[i]) <= cap + .5 && FitStrip(v, look, cap - W(forms[i]) - 14) is { } st) return (forms[i].L, forms[i].R, st);
        var f = forms.FirstOrDefault(x => W(x) <= cap + .5, forms[^1]);
        return (f.L, f.R, FitStrip(v, look, cap - W(f) - 14));
    }

    public static System.Windows.Documents.Run[] Join(params System.Windows.Documents.Run[][] parts) => parts.SelectMany(p => p).ToArray();

    /// <summary>
    /// <paramref name="t"/> as the first of <paramref name="forms"/> that fits <paramref name="room"/> (the last when none does):
    /// a part goes before a figure is cut. Returns the form's index.
    /// </summary>
    public static int FitRuns(TextBlock t, double room, params System.Windows.Documents.Run[][] forms)
    {
        for (int i = 0; i < forms.Length; i++)
        {
            t.Inlines.Clear();
            foreach (var r in forms[i]) t.Inlines.Add(Copy(r));
            if (i == forms.Length - 1 || Wide(t) <= room + .5) return i;
        }
        return -1;
    }

    /// <summary>A run's copy with only what it set itself (its size and face otherwise the line's).</summary>
    static System.Windows.Documents.Run Copy(System.Windows.Documents.Run r)
    {
        var c = new System.Windows.Documents.Run(r.Text);
        foreach (var p in new[] { System.Windows.Documents.TextElement.FontSizeProperty, System.Windows.Documents.TextElement.ForegroundProperty, System.Windows.Documents.TextElement.FontWeightProperty, System.Windows.Documents.TextElement.FontFamilyProperty })
            if (r.ReadLocalValue(p) is var x && x != DependencyProperty.UnsetValue) c.SetValue(p, x);
        return c;
    }

    // ---- a course's shapes

    /// <summary>A course cut into <paramref name="n"/> steps (a digital glitch moves in jumps, not glides).</summary>
    public static double Steps(double p, int n) => Math.Floor(Math.Clamp(p, 0, 0.9999) * n) / n;

    /// <summary>1 inside [a, b) of the course, fading in over <paramref name="fin"/> and out over <paramref name="fout"/>.</summary>
    public static double Gate(double p, double a, double b, double fin = 0, double fout = 0)
    {
        if (p < a || p >= b) return 0;
        double v = 1;
        if (fin > 0 && p < a + fin) v = Math.Min(v, (p - a) / fin);
        if (fout > 0 && p > b - fout) v = Math.Min(v, (b - p) / fout);
        return Math.Clamp(v, 0, 1);
    }

    /// <summary>A blink: on for the first half of each of <paramref name="n"/> beats over the course's first <paramref name="over"/>, then on.</summary>
    public static bool Blink(double p, int n, double over) => p >= over || (p / over * n) % 1 < 0.55;

    /// <summary>A stable 0 to 1 from a text (a name's channel, a pickup's torn slice).</summary>
    public static double Hash(string s)
    {
        uint h = 2166136261;
        foreach (char c in s) { h ^= c; h *= 16777619; }
        return (h % 10007) / 10007.0;
    }

    /// <summary>A fill's width as a share of its track's (the track's own SizeChanged keeps it after layout).</summary>
    public static void Share(Grid track, FrameworkElement fill, double share) =>
        fill.Width = Math.Max(0, track.ActualWidth * Math.Clamp(double.IsFinite(share) ? share : 0, 0, 1));

    // ---- a fight's foot, worked out as the Parser's (Ui/ParserFoot.cs): the total, and the group's DPS (yours alone)

    /// <param name="Held">The fight is held (over, or Always on at rest): the group figure is the whole fight's average.</param>
    public sealed record FootFigures(string Total, string Label, string Dps, bool Held);

    public static FootFigures FootOf(Encounter e, DateTime now)
    {
        var (word, value, held) = ParserFoot.Right(e, now);   // the group's DPS; alone, your best or peak (none: Label "")
        return new(Amounts.Meter(e.Credited), word, value, held);
    }

    /// <summary>A look's own foot carries the Parser foot's tag and its words for the tests and a screen reader.</summary>
    public static void TagFoot(FrameworkElement el, Encounter e, DateTime now)
    {
        el.Tag = "parser-foot";
        System.Windows.Automation.AutomationProperties.SetName(el, e.FootText(now));
    }

    /// <summary>
    /// A text's width on one line in a look's face (to size a column for the widest form its figure takes), as the rows lay it out:
    /// display mode snaps each glyph to the screen's whole pixels, so the width is the screen's own (measured ideal, "+12.3K/s"
    /// lost its "s" on a screen at 125%).
    /// </summary>
    public static double TextW(string s, FontFamily face, double size, FontWeight w)
    {
        var tf = new Typeface(face, FontStyles.Normal, w, FontStretches.Normal);
        return new FormattedText(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, tf, size, Brushes.Black, null, TextFormattingMode.Display, ScreenDip).WidthIncludingTrailingWhitespace;
    }
    static double ScreenDip => _screenDip ??= VisualTreeHelper.GetDpi(new DrawingVisual()).PixelsPerDip;
    static double? _screenDip;

    /// <summary>
    /// A member's bar: their DPS against the top member's, so the top one fills it and the rest read at a glance (Steven,
    /// 2026-11-17: "prioritizes the bar"); 0 before a hit or with no figure.
    /// </summary>
    public static double BarOf(MeterMember m, MeterContext c)
    {
        if (m.CpOnly || !double.IsFinite(m.FigureValue) || m.FigureValue <= 0) return 0;
        return c.E.BarOf(m.F);   // against the top member's: the top bar full, the rest in proportion (2026-11-20)
    }

    /// <summary>A row's figure column: right-aligned, tabular, never trimmed (a figure that changes never shifts).</summary>
    public static TextBlock FigCell(double size, Brush fg, FontFamily face, double minWidth, FontWeight? w = null)
    {
        var t = Txt("", size, fg, face, w);
        t.TextAlignment = TextAlignment.Right; t.HorizontalAlignment = HorizontalAlignment.Right; t.MinWidth = minWidth;
        return t;
    }

    /// <summary>A small tag: words on a block of colour ("TOP DPS").</summary>
    public static Border Tag(string text, Brush bg, Brush fg, FontFamily face, double size = 9, Thickness? margin = null) => new()
    {
        Background = bg, Padding = new Thickness(3, 0, 3, 0), Margin = margin ?? new Thickness(5, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
        Child = new TextBlock { Text = text, FontFamily = face, FontSize = size, FontWeight = FontWeights.Bold, Foreground = fg, VerticalAlignment = VerticalAlignment.Center },
    };

    /// <summary>A class's three-letter code, as a list's class column says it ("GLA", "CLR"); empty for none.</summary>
    public static string ClassCode(string? cls) => ClassArt.Key(cls) switch
    {
        "gladiator" => L.T("GLA"), "templar" => L.T("TMP"), "assassin" => L.T("ASN"), "ranger" => L.T("RNG"), "sorcerer" => L.T("SOR"),
        "spiritmaster" => L.T("SPM"), "cleric" => L.T("CLR"), "chanter" => L.T("CHN"), _ => "",
    };

    /// <summary>A timer's words split at its name: ("Shugo", "ENTER 2:41").</summary>
    public static (string Name, string Tail) TimerParts(TimerBoard.Item i, string text)
    {
        var name = i.Short ?? "";
        if (name.Length > 0 && text.StartsWith(name + " ", StringComparison.Ordinal)) return (name, text[(name.Length + 1)..]);
        return ("", text);
    }
}

/// <summary>
/// A Glitch panel: its ground layers, the body (rows of text), the layers over the body (scanlines), and an effect layer
/// on top that never takes a click and keeps every transient effect inside the panel (<see cref="Run"/>).
/// </summary>
sealed class GlitchPanel
{
    public readonly Grid Root = new();
    public readonly Grid Ground = new() { IsHitTestVisible = false };
    public readonly StackPanel Body = new();
    public readonly Grid Over = new() { IsHitTestVisible = false };
    public readonly Grid Fx = new() { IsHitTestVisible = false, ClipToBounds = true };
    /// <summary>The effects on the picture under the words (noise, static, tears): the figures are always drawn clean over them.</summary>
    public readonly Grid Under = new() { IsHitTestVisible = false, ClipToBounds = true };

    public GlitchPanel(Thickness pad)
    {
        Body.Margin = pad;
        Root.Children.Add(Ground); Root.Children.Add(Under); Root.Children.Add(Body); Root.Children.Add(Over); Root.Children.Add(Fx);
    }

    public double W => Root.ActualWidth > 0 ? Root.ActualWidth : 300;
    public double H => Root.ActualHeight > 0 ? Root.ActualHeight : 80;

    /// <summary>
    /// One transient effect: <paramref name="el"/> on the effect layer for <paramref name="ms"/>, stepped by
    /// <paramref name="step"/> (a held render frame calls it once at its point), then taken away.
    /// </summary>
    public void Run(UIElement el, double ms, Action<double> step, bool under = false)
    {
        var layer = under ? Under : Fx;
        layer.Children.Add(el);
        Motion.Play(el, ms, step, () => layer.Children.Remove(el));
    }

    /// <summary>A full-panel flash of <paramref name="c"/>: up to <paramref name="peak"/> by <paramref name="up"/> of the course, gone by <paramref name="gone"/>.</summary>
    public void Flash(Color c, double peak, double ms, double up = 0.06, double gone = 0.4)
    {
        var r = new Rectangle { Fill = HudKit.Frozen(c), Opacity = 0 };
        Run(r, ms, p => r.Opacity = p < up ? peak * p / up : p < gone ? peak * (1 - Motion.Out((p - up) / (gone - up))) : 0);
    }
}

/// <summary>
/// A meter row's level bar: a track, the fill a share of its width (kept on the track's size), echoes of the fill set a
/// few px off (the Corrupted ghosts), and an overlay over it all (segments, cells). The share slides (Motion.Slide).
/// </summary>
sealed class GlitchLevel
{
    public readonly Grid Root = new() { IsHitTestVisible = false };
    public readonly Border Fill = new() { HorizontalAlignment = HorizontalAlignment.Left };
    readonly List<Border> _echoes = new();
    double _share;

    public GlitchLevel(double height, Brush track, Brush? over = null)
    {
        Root.Height = height;
        Root.Children.Add(new Border { Background = track });
        Root.Children.Add(Fill);
        if (over != null) Root.Children.Add(new Rectangle { Fill = over });
        Root.SizeChanged += (_, _) => Apply();
    }

    /// <summary>A copy of the fill <paramref name="dx"/>, <paramref name="dy"/> px off, behind it.</summary>
    public Border Echo(double dx, double dy)
    {
        var b = new Border { HorizontalAlignment = HorizontalAlignment.Left, RenderTransform = new TranslateTransform(dx, dy) };
        Root.Children.Insert(1, b);
        _echoes.Add(b);
        return b;
    }

    public double Share
    {
        get => _share;
        set { _share = Math.Clamp(double.IsFinite(value) ? value : 0, 0, 1); Apply(); }
    }

    void Apply()
    {
        double w = Math.Max(0, Root.ActualWidth * _share);
        Fill.Width = w;
        foreach (var e in _echoes) e.Width = w;
    }

    /// <summary>The share shown: slid from the last one under <paramref name="key"/> (set at once in a render or a first draw).</summary>
    public void Slide(string key, double share) => Motion.Slide(Root, key, Math.Clamp(double.IsFinite(share) ? share : 0, 0, 1), v => Share = v);
}

/// <summary>
/// A meter row's name line: its parts left to right, packed from the left. The name is the part that gives way, but only
/// after the Combat Power beside it (<see cref="Yields"/>) has: the CP is put away when keeping it would cut the name shorter
/// than <see cref="NameMin"/> (the Parser's rows' rule, ParserRows.FitName), so a name never shrinks to a stub for it.
/// </summary>
sealed class GlitchNameLine : Panel
{
    public UIElement? Text, Yields;
    /// <summary>The least room the name keeps before the CP is put away (a short name: all of it).</summary>
    public double NameMin = 60;
    double _nameW;
    bool _drop = true;

    protected override Size MeasureOverride(Size a)
    {
        var inf = new Size(double.PositiveInfinity, a.Height);
        double others = 0, h = 0, drop = 0, nameW = 0;
        foreach (UIElement c in InternalChildren)
        {
            c.Measure(inf);
            h = Math.Max(h, c.DesiredSize.Height);
            if (c == Text) nameW = c.DesiredSize.Width;
            else if (c == Yields) drop = c.DesiredSize.Width;
            else others += c.DesiredSize.Width;
        }
        bool open = double.IsInfinity(a.Width);
        _drop = drop > 0 && (open || a.Width - others - drop >= Math.Min(nameW, NameMin));
        double room = open ? nameW : Math.Max(0, a.Width - others - (_drop ? drop : 0));
        _nameW = Math.Min(nameW, room);
        Text?.Measure(new Size(_nameW, a.Height));
        return new Size(others + _nameW + (_drop ? drop : 0), h);
    }

    protected override Size ArrangeOverride(Size f)
    {
        // a CP put away is not drawn at all (arranged 0 wide it still kept its words' width under a clip)
        if (Yields is UIElement y) y.Opacity = _drop ? 1 : 0;
        double x = 0;
        foreach (UIElement c in InternalChildren)
        {
            double w = c == Text ? _nameW : c == Yields ? (_drop ? c.DesiredSize.Width : 0) : c.DesiredSize.Width;
            double ch = Math.Min(f.Height, c.DesiredSize.Height);
            c.Arrange(new Rect(x, Math.Max(0, (f.Height - ch) / 2), w, ch));
            x += w;
        }
        return f;
    }
}
