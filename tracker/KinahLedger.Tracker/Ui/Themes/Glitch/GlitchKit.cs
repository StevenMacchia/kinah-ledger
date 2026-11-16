using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
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

    public GlitchPanel(Thickness pad)
    {
        Body.Margin = pad;
        Root.Children.Add(Ground); Root.Children.Add(Body); Root.Children.Add(Over); Root.Children.Add(Fx);
    }

    public double W => Root.ActualWidth > 0 ? Root.ActualWidth : 300;
    public double H => Root.ActualHeight > 0 ? Root.ActualHeight : 80;

    /// <summary>
    /// One transient effect: <paramref name="el"/> on the effect layer for <paramref name="ms"/>, stepped by
    /// <paramref name="step"/> (a held render frame calls it once at its point), then taken away.
    /// </summary>
    public void Run(UIElement el, double ms, Action<double> step)
    {
        Fx.Children.Add(el);
        Motion.Play(el, ms, step, () => Fx.Children.Remove(el));
    }

    /// <summary>A full-panel flash of <paramref name="c"/>: up to <paramref name="peak"/> by <paramref name="up"/> of the course, gone by <paramref name="gone"/>.</summary>
    public void Flash(Color c, double peak, double ms, double up = 0.06, double gone = 0.4)
    {
        var r = new Rectangle { Fill = HudKit.Frozen(c), Opacity = 0 };
        Run(r, ms, p => r.Opacity = p < up ? peak * p / up : p < gone ? peak * (1 - Motion.Out((p - up) / (gone - up))) : 0);
    }
}
