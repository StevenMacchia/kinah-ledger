using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using KinahLedger.Parser;
using Path = System.Windows.Shapes.Path;

namespace KinahLedger.Ui;

/// <summary>
/// Parser v2's shared pieces (the page and the overlay): glass with a lit top edge, your row's gold frame, rank chips in the
/// class's colour (angled on the overlay) that flip on a swap, sparklines, bars, and the charts' frame.
/// </summary>
static class PK
{
    // ---- tokens (the window's skin, resolved when drawn; the overlay passes its own dark ones)
    public static Brush K(string key) => Application.Current.TryFindResource(key) as Brush ?? Brushes.Magenta;
    public static Color KC(string key) => (Application.Current.TryFindResource(key) as SolidColorBrush)?.Color ?? Colors.Magenta;
    public static bool Dark => Skins.IsDark(Skins.Current);
    public static SolidColorBrush C(Color c, double a = 1) { var b = new SolidColorBrush(Color.FromArgb((byte)Math.Round(255 * Math.Clamp(a, 0, 1)), c.R, c.G, c.B)); b.Freeze(); return b; }
    public static readonly Color Gold = Color.FromRgb(0xF5, 0xB9, 0x42), Mint = Color.FromRgb(0x3D, 0xD6, 0x8C), Coral = Color.FromRgb(0xF2, 0x6D, 0x6D), White = Colors.White, Black = Colors.Black;
    public static readonly FontFamily Face = HudKit.Text;
    /// <summary>Gold as text and marks on the page: the window's gold on a dark look, a deeper one on Daylight so it keeps its contrast.</summary>
    public static Brush GoldInk => Dark ? C(Gold) : C(Color.FromRgb(0xA8, 0x6A, 0x00));

    public static TextBlock T(string s, double size, Brush b, FontWeight? w = null, bool tab = false, bool trim = true)
    {
        var t = new TextBlock { Text = s, FontSize = size, Foreground = b, FontWeight = w ?? FontWeights.Medium, VerticalAlignment = VerticalAlignment.Center };
        if (trim) t.TextTrimming = TextTrimming.CharacterEllipsis;
        if (tab) Typography.SetNumeralAlignment(t, FontNumeralAlignment.Tabular);
        return t;
    }
    public static Run R(string s, double size, Brush b, FontWeight? w = null) => new(s) { FontSize = size, Foreground = b, FontWeight = w ?? FontWeights.Medium };
    public static string Int(double v) => Math.Round(v).ToString("N0", CultureInfo.InvariantCulture);
    public static string Pct(double v, int d = 1) => (v * 100).ToString(d == 0 ? "0" : "0.0", CultureInfo.InvariantCulture) + "%";
    public static string Signed(double v) => (v >= 0 ? "+" : "−") + Pct(Math.Abs(v));

    /// <summary>A record's kind in the language in use: the open world and the Abyss are words; the instance kinds (Transcendence, Expedition, Sanctuary) are the game's names and stay.</summary>
    public static string KindWord(string kind) => kind is "Open world" or "Abyss" ? L.T(kind) : kind;

    /// <summary>The sub line under a record's title (as <see cref="Encounter.Where"/>), its kind and place in the language in use.</summary>
    public static string WhereOf(Encounter e)
    {
        var bits = new List<string>();
        var i = BossBook.Strip(e.Instance);
        if (i.Length > 0 && i != e.Title && i != e.Kind) bits.Add(KindWord(i));
        if (e.GameMode.Length > 0) bits.Add(e.GameMode);
        if (e.Kind.Length > 0) bits.Add(KindWord(e.Kind));
        return string.Join(" · ", bits);
    }
    public static string Clock(double s) => BossParser.Clock(TimeSpan.FromSeconds(s));
    public static T M<T>(T el, double l, double t, double r, double b) where T : FrameworkElement { el.Margin = new Thickness(l, t, r, b); return el; }
    public static T Col<T>(T el, int c, int span = 1) where T : UIElement { Grid.SetColumn(el, c); if (span > 1) Grid.SetColumnSpan(el, span); return el; }
    public static T Row<T>(T el, int r) where T : UIElement { Grid.SetRow(el, r); return el; }
    public static Grid Cols(params double[] widths)
    {
        var g = new Grid();
        foreach (var w in widths) g.ColumnDefinitions.Add(new ColumnDefinition { Width = w < 0 ? new GridLength(-w, GridUnitType.Star) : w == 0 ? GridLength.Auto : new GridLength(w) });
        return g;
    }

    // ---- surfaces

    static ImageBrush? _noise;
    /// <summary>A fine grain over the glass (a 96 px tile of soft noise), so a large dark surface doesn't band.</summary>
    public static ImageBrush Noise
    {
        get
        {
            if (_noise != null) return _noise;
            const int s = 96;
            var px = new byte[s * s * 4];
            var r = new Random(11);
            for (int i = 0; i < s * s; i++) { byte v = (byte)r.Next(256); px[i * 4] = v; px[i * 4 + 1] = v; px[i * 4 + 2] = v; px[i * 4 + 3] = (byte)r.Next(0, 18); }
            var bmp = BitmapSource.Create(s, s, 96, 96, PixelFormats.Bgra32, null, px, s * 4);
            bmp.Freeze();
            _noise = new ImageBrush(bmp) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, s, s), ViewportUnits = BrushMappingMode.Absolute, Stretch = Stretch.None };
            _noise.Freeze();
            return _noise;
        }
    }

    /// <summary>
    /// Real glass on the page: a fill lighter at the top, a lit top edge (a hairline of light, brightest in the middle), an
    /// inner shadow at the foot, the grain, a soft drop shadow under it. <paramref name="glow"/>: a colour's faint light from the top left (the hero).
    /// </summary>
    public static Border Glass(UIElement content, Thickness pad, double radius = 14, Color? glow = null, Brush? edge = null)
    {
        bool dark = Dark;
        var fill = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        fill.GradientStops.Add(new GradientStop(dark ? Color.FromRgb(0x18, 0x1B, 0x25) : KC("Card"), 0));
        fill.GradientStops.Add(new GradientStop(dark ? Color.FromRgb(0x10, 0x12, 0x18) : KC("Card"), 1));
        var line = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        line.GradientStops.Add(new GradientStop(dark ? Color.FromArgb(0x40, 255, 255, 255) : KC("Line"), 0));
        line.GradientStops.Add(new GradientStop(dark ? Color.FromArgb(0x16, 255, 255, 255) : KC("Line"), 0.35));
        line.GradientStops.Add(new GradientStop(dark ? Color.FromArgb(0x0E, 255, 255, 255) : KC("Line"), 1));
        var outer = new Border { CornerRadius = new CornerRadius(radius), BorderThickness = new Thickness(1), BorderBrush = edge ?? line, Background = fill,
            Effect = new DropShadowEffect { Color = Black, BlurRadius = 18, ShadowDepth = 3, Direction = 270, Opacity = dark ? 0.32 : 0.08 } };
        var g = new Grid();
        if (glow is { } gc)
        {
            var rg = new RadialGradientBrush { Center = new Point(0.12, 0), GradientOrigin = new Point(0.12, 0), RadiusX = 0.75, RadiusY = 1.1 };
            rg.GradientStops.Add(new GradientStop(Color.FromArgb(dark ? (byte)0x26 : (byte)0x18, gc.R, gc.G, gc.B), 0));
            rg.GradientStops.Add(new GradientStop(Color.FromArgb(0, gc.R, gc.G, gc.B), 1));
            g.Children.Add(new Border { CornerRadius = new CornerRadius(radius - 1), Background = rg });
        }
        // the lit top edge
        var hl = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        hl.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 0));
        hl.GradientStops.Add(new GradientStop(Color.FromArgb(dark ? (byte)0x70 : (byte)0x00, 255, 255, 255), 0.5));
        hl.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 1));
        g.Children.Add(new Border { Height = 1, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(radius, 0, radius, 0), Background = hl });
        // the inner shadow at the foot
        var sh = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        sh.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 0.7));
        sh.GradientStops.Add(new GradientStop(Color.FromArgb(dark ? (byte)0x24 : (byte)0x04, 0, 0, 0), 1));
        g.Children.Add(new Border { CornerRadius = new CornerRadius(radius - 1), Background = sh, IsHitTestVisible = false });
        g.Children.Add(new Border { Padding = pad, Child = content });
        outer.Child = g;
        return outer;
    }

    /// <summary>Your row's frame: a gold lit edge, a gold wash fading right, a soft gold glow. Unmistakable at any rank.</summary>
    public static Border YouFrame(UIElement content, double radius, Thickness pad, double glow = 1)
    {
        var edge = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        edge.GradientStops.Add(new GradientStop(Color.FromArgb(0xF0, Gold.R, Gold.G, Gold.B), 0));
        edge.GradientStops.Add(new GradientStop(Color.FromArgb(0x70, Gold.R, Gold.G, Gold.B), 0.6));
        edge.GradientStops.Add(new GradientStop(Color.FromArgb(0x50, Gold.R, Gold.G, Gold.B), 1));
        var wash = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        wash.GradientStops.Add(new GradientStop(Color.FromArgb(0x1A, Gold.R, Gold.G, Gold.B), 0));   // light: the name reads over it
        wash.GradientStops.Add(new GradientStop(Color.FromArgb(0x08, Gold.R, Gold.G, Gold.B), 1));
        return new Border { CornerRadius = new CornerRadius(radius), BorderThickness = new Thickness(1), BorderBrush = edge, Background = wash, Padding = pad, Child = content,
            Effect = glow > 0 ? new DropShadowEffect { Color = Gold, BlurRadius = 16, ShadowDepth = 0, Opacity = 0.42 * glow } : null };
    }

    /// <summary>
    /// A rank chip in the member's class colour. On a swap it flips like a card over <paramref name="flip"/> (0 to 1): the old
    /// number folds away, the new one unfolds. <paramref name="skew"/>: the angled chip of the Arena direction.
    /// </summary>
    public static FrameworkElement RankChip(int rank, string cls, double size, int? from = null, double flip = 1, bool you = false, bool skew = false)
    {
        var c = you ? Gold : ClassArt.ColorOf(cls);
        bool turning = from is int f0 && f0 != rank && flip < 1;
        int shown = turning && flip < 0.5 ? from!.Value : rank;
        double sy = turning ? Math.Abs(Math.Cos(Math.PI * flip)) : 1;
        var b = new Border
        {
            Width = size * (skew ? 1.25 : 1), Height = size, CornerRadius = new CornerRadius(skew ? 3 : size * 0.3), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left,
            Background = C(c, you ? 0.95 : Dark ? 0.2 : 0.3), BorderBrush = C(c, you ? 1 : 0.75), BorderThickness = new Thickness(1),
            Child = new TextBlock { Text = shown.ToString(), FontSize = size * 0.58, FontWeight = FontWeights.Bold, Foreground = you ? C(Color.FromRgb(0x1D, 0x13, 0x00)) : C(ClassArt.NameColor(cls, Dark)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        var tg = new TransformGroup();
        if (skew) tg.Children.Add(new SkewTransform(-14, 0));
        if (sy < 0.999) tg.Children.Add(new ScaleTransform(1, Math.Max(0.02, sy)));
        if (tg.Children.Count > 0) { b.RenderTransform = tg; b.RenderTransformOrigin = new Point(0.5, 0.5); }
        if (you) b.Effect = new DropShadowEffect { Color = Gold, BlurRadius = 10, ShadowDepth = 0, Opacity = 0.6 };
        return b;
    }

    /// <summary>
    /// A figure whose changed digits roll (an odometer): at <paramref name="t"/> each digit that differs between the two
    /// writes is partway up, the old leaving the top, the new coming from below. Tabular, so nothing shifts sideways.
    /// </summary>
    public static FrameworkElement Roll(string from, string to, double t, double size, Brush fg, FontWeight w)
    {
        if (t >= 1 || from == to) return T(to, size, fg, w, tab: true, trim: false);
        int len = Math.Max(from.Length, to.Length);
        from = from.PadLeft(len); to = to.PadLeft(len);
        double e = 1 - Math.Pow(1 - Math.Clamp(t, 0, 1), 3);
        var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        double h = Math.Round(size * 1.3);
        for (int i = 0; i < len; i++)
        {
            if (from[i] == to[i]) { sp.Children.Add(T(to[i].ToString(), size, fg, w, tab: true, trim: false)); continue; }
            var cell = new Grid { Height = h, ClipToBounds = true };
            var a = T(from[i].ToString(), size, fg, w, tab: true, trim: false); a.RenderTransform = new TranslateTransform(0, -e * h); a.Opacity = 1 - e;
            var b = T(to[i].ToString(), size, fg, w, tab: true, trim: false); b.RenderTransform = new TranslateTransform(0, (1 - e) * h); b.Opacity = e;
            cell.Children.Add(a); cell.Children.Add(b);
            sp.Children.Add(cell);
        }
        return sp;
    }

    /// <summary>A sparkline: the series as a line with a soft area under it.</summary>
    public static FrameworkElement Spark(double[] s, double w, double h, Color c, double max = 0, double thick = 1.4, bool glow = false, double reveal = 1)
    {
        var g = new Grid { Width = w, Height = h, ClipToBounds = true };
        if (s.Length < 2) return g;
        if (max <= 0) max = s.Max() * 1.06;
        // the line over its own range (from 60% of its low), so a member's swings show at a sparkline's height
        double lo = s.Min() * 0.6;
        int n = Math.Max(2, (int)Math.Round(s.Length * Math.Clamp(reveal, 0, 1)));
        var pts = new PointCollection();
        for (int i = 0; i < n; i++) pts.Add(new Point(i * w / (s.Length - 1), h - 1 - (s[i] - lo) / Math.Max(1, max - lo) * (h - 2)));
        var area = new PathFigure { StartPoint = new Point(0, h), IsClosed = true };
        foreach (var p in pts) area.Segments.Add(new LineSegment(p, false));
        area.Segments.Add(new LineSegment(new Point(pts[^1].X, h), false));
        var fill = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        fill.GradientStops.Add(new GradientStop(Color.FromArgb(0x50, c.R, c.G, c.B), 0)); fill.GradientStops.Add(new GradientStop(Color.FromArgb(0, c.R, c.G, c.B), 1));
        g.Children.Add(new Path { Data = new PathGeometry(new[] { area }), Fill = fill });
        g.Children.Add(new Polyline { Points = pts, Stroke = C(c), StrokeThickness = thick, StrokeLineJoin = PenLineJoin.Round, Effect = glow ? new DropShadowEffect { Color = c, BlurRadius = 6, ShadowDepth = 0, Opacity = 0.7 } : null });
        return g;
    }

    /// <summary>A bar whose share fills in a colour with a glow (the window's progress bars).</summary>
    public static Grid Bar(double share, Color c, double h, Brush track, bool glow = true)
    {
        var g = new Grid { Height = h };
        g.Children.Add(new Border { CornerRadius = new CornerRadius(h / 2), Background = track });
        var fill = new Border { CornerRadius = new CornerRadius(h / 2), HorizontalAlignment = HorizontalAlignment.Left, Background = C(c), Effect = glow ? new DropShadowEffect { Color = c, BlurRadius = 8, ShadowDepth = 0, Opacity = 0.55 } : null };
        fill.Child = new Border { CornerRadius = new CornerRadius(h / 2), Margin = new Thickness(1, 1, 1, 0), Background = new LinearGradientBrush(Color.FromArgb(0x55, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90) };
        g.Children.Add(fill);
        g.SizeChanged += (_, _) => fill.Width = share <= 0.001 ? 0 : Math.Max(h, g.ActualWidth * Math.Clamp(share, 0, 1));
        return g;
    }

    /// <summary>The boss's HP bar: gold, coral under a third, mint once it's down.</summary>
    public static Grid HpBar(double left, double h, Brush track, bool live) => Bar(live ? left : 1, live ? (left < 0.34 ? Coral : Gold) : Mint, h, track);

    /// <summary>A short line of what a figure is (the small caps-free label over a tile's figure).</summary>
    public static StackPanel Stat(string label, FrameworkElement value, string sub, Brush labelB, Brush subB)
    {
        var sp = new StackPanel();
        sp.Children.Add(T(label, 11.5, labelB, FontWeights.SemiBold));
        sp.Children.Add(M(value, 0, 1, 0, 0));
        if (sub.Length > 0) sp.Children.Add(T(sub, 11.5, subB));
        return sp;
    }

    // ---- the charts

    public sealed class Chart
    {
        public double W, H, L = 44, Rt = 52, Tp = 28, Bt = 22;
        public double Secs, YMax;
        public double X(double t) => L + t / Secs * (W - L - Rt);
        public double Y(double v) => Tp + (1 - v / YMax) * (H - Tp - Bt);
        public double YHp(double hp) => Tp + (1 - hp) * (H - Tp - Bt);
    }

    public static double Nice(double v) { double p = Math.Pow(10, Math.Floor(Math.Log10(Math.Max(1, v)))); foreach (var m in new[] { 1, 1.25, 1.5, 2, 2.5, 3, 4, 5, 6, 8, 10 }) if (m * p >= v) return m * p; return 10 * p; }

    /// <summary>The axes and the grid for a chart over <paramref name="secs"/>; the boss's HP behind it when the record carries it (one value a second).</summary>
    public static (Canvas Canvas, Chart Ch) Frame(double secs, double w, double h, double yMax, Brush grid, Brush label, IReadOnlyList<double>? hp = null)
    {
        var ch = new Chart { W = w, H = h, Secs = Math.Max(1, secs), YMax = Nice(yMax), Rt = hp != null ? 52 : 12 };
        var cv = new Canvas { Width = w, Height = h, ClipToBounds = true };
        for (int k = 0; k <= 2; k++)
        {
            double v = ch.YMax * k / 2, y = ch.Y(v);
            cv.Children.Add(new Line { X1 = ch.L, X2 = w - ch.Rt, Y1 = y, Y2 = y, Stroke = grid, StrokeThickness = 1, StrokeDashArray = k == 0 ? null : new DoubleCollection { 2, 4 } });
            var lt = T(Amounts.Short(v), 10.5, label, FontWeights.SemiBold, tab: true); Canvas.SetLeft(lt, 4); Canvas.SetTop(lt, y - 8); cv.Children.Add(lt);
        }
        double step = secs > 400 ? 120 : secs > 200 ? 60 : 30;
        for (double t = 0; t <= secs + 0.1; t += step)
        {
            var lt = T(Clock(t), 10.5, label, FontWeights.SemiBold, tab: true); Canvas.SetLeft(lt, ch.X(t) - 12); Canvas.SetTop(lt, h - ch.Bt + 5); cv.Children.Add(lt);
        }
        if (hp is { Count: > 1 })
        {
            var fig = new PathFigure { StartPoint = new Point(ch.X(0), ch.YHp(hp[0])) };
            for (int i = 1; i < hp.Count; i++) fig.Segments.Add(new LineSegment(new Point(ch.X(i), ch.YHp(hp[i])), true));
            cv.Children.Add(new Path { Data = new PathGeometry(new[] { fig }), Stroke = C(Dark ? White : Black, 0.22), StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 3, 2 } });
            foreach (var (v, s) in new[] { (1.0, "100%"), (0.5, "50%") })
            { var lt = T(s, 10.5, label, FontWeights.SemiBold); Canvas.SetLeft(lt, w - ch.Rt + 6); Canvas.SetTop(lt, ch.YHp(v) - 8); cv.Children.Add(lt); }
            var hl = T(L.T("boss HP"), 10, label, FontWeights.SemiBold); Canvas.SetLeft(hl, w - ch.Rt + 2); Canvas.SetTop(hl, ch.YHp(0) - 8); cv.Children.Add(hl);
        }
        return (cv, ch);
    }

    /// <summary>A series on a chart, one value a second (yours gold, thicker, glowing).</summary>
    public static void Line(Canvas cv, Chart ch, IReadOnlyList<double> s, Color c, double thick, bool glow, DoubleCollection? dash = null, double opacity = 1)
    {
        var pts = new PointCollection();
        for (int i = 0; i < s.Count; i++) pts.Add(new Point(ch.X(i), ch.Y(s[i])));
        cv.Children.Add(new Polyline { Points = pts, Stroke = C(c, opacity), StrokeThickness = thick, StrokeLineJoin = PenLineJoin.Round, StrokeDashArray = dash,
            Effect = glow ? new DropShadowEffect { Color = c, BlurRadius = 9, ShadowDepth = 0, Opacity = 0.75 } : null });
    }

    /// <summary>A death on the chart: a coral mark at its second, with the member's name.</summary>
    public static void DeathMark(Canvas cv, Chart ch, double at, double v, string name)
    {
        double x = ch.X(at), y = ch.Y(v);
        var dot = new Border { Width = 14, Height = 14, CornerRadius = new CornerRadius(7), Background = C(Coral), BorderBrush = C(Black, 0.6), BorderThickness = new Thickness(1.5),
            Child = new TextBlock { Text = "✕", FontSize = 8.5, FontWeight = FontWeights.Bold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, -1, 0, 0) },
            Effect = new DropShadowEffect { Color = Coral, BlurRadius = 10, ShadowDepth = 0, Opacity = 0.8 } };
        Canvas.SetLeft(dot, x - 7); Canvas.SetTop(dot, y - 7); cv.Children.Add(dot);
        var lt = T(L.F("{0} died", name), 10.5, C(Coral), FontWeights.SemiBold); Canvas.SetLeft(lt, x + 9); Canvas.SetTop(lt, y - 9); cv.Children.Add(lt);
    }

    /// <summary>A rolling average over a window of <paramref name="k"/> seconds each side, so a per-second line reads calmly.</summary>
    public static double[] Smooth(IReadOnlyList<double> s, int k = 2)
    {
        var r = new double[s.Count];
        for (int i = 0; i < s.Count; i++) { int a = Math.Max(0, i - k), b = Math.Min(s.Count - 1, i + k); double sum = 0; for (int j = a; j <= b; j++) sum += s[j]; r[i] = sum / (b - a + 1); }
        return r;
    }
}
