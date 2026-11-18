using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using KinahLedger.Parser;
using static KinahLedger.Ui.PK;

namespace KinahLedger.Ui;

/// <summary>
/// The Parser page's Graphs. Steven, 2026-11-18: "have one graph for the entire fight from start to finish it doesnt move just
/// the line does for every person and make it detailed", and "it's just like the post game one just in real time and very
/// detailed".
/// <list type="bullet">
/// <item>The fight: one detailed graph, start to finish. Every member's DPS a line in their class's colour (yours gold and
/// thicker), each outlined so six read apart on the dark; the boss's HP on the right-hand scale with the thirds and the moment
/// it crossed each; deaths marked on the member's line; the kill or the wipe at the end. Each line's name and DPS so far
/// stand at its right end, in a column of their own, never over each other.</item>
/// <item>Under it, smaller, healing and damage taken a second when the record has them.</item>
/// </list>
/// The frame stands still: a live fight's time runs from 0 to the length this boss usually takes (your best kill of it, else
/// your last pull, else 3:00), longer only in big steps when the fight runs past it; the DPS scale starts at the best this
/// boss has seen and steps up only when a line passes it. Only the lines grow. A kept fight is the same graph, its time exactly
/// start to finish. Lines are a 2 s average, so bursts and drops show. The hover reads every line and the boss's HP at a
/// second, a crosshair across both graphs. Each graph is drawn in one pass (<see cref="DrawingContext"/>: geometry and text,
/// never an element a point) and kept frozen; a live fight's are drawn again at most every <see cref="LiveFrameMs"/>, and only
/// for whole seconds. Each has its own Copy picture (the share frame, Ui/MeterShare.cs).
/// </summary>
static class ParserGraphs
{
    public const string Dps = "dps", Heal = "heal";
    public static readonly string[] Kinds = { Dps, Heal };
    /// <summary>The lines' smoothing: a Gaussian of this sigma (seconds), about a 2 s window, so bursts and drops show.</summary>
    public const double Sigma = 0.85;
    /// <summary>The window the hover names.</summary>
    public const int AverageSeconds = 2;
    /// <summary>A live fight's time scale before anything is known of the boss.</summary>
    public const double DefaultSeconds = 180;
    /// <summary>A live fight's graphs are drawn again at most this often (the data moves a second at a time anyway).</summary>
    public const int LiveFrameMs = 250;
    /// <summary>For the options test: the charts drawn since the start (each a fresh frozen drawing).</summary>
    internal static int Draws;
    /// <summary>For the options test's CPU measure: the time spent working the graphs out and drawing them, in ms.</summary>
    internal static double DataMs, DrawMs;

    // ================================================================ the data

    public sealed class Line
    {
        public long Id; public string Name = ""; public Color Color; public bool You;
        public double[] Raw = Array.Empty<double>(); public double[] S = Array.Empty<double>();
        /// <summary>Its figure for the fight so far (the label at its end): damage or healing a second over the seconds drawn.</summary>
        public double Avg;
    }

    /// <summary>What the graphs are drawn from, worked out once a draw from the fight's view.</summary>
    public sealed class Set
    {
        /// <summary>The seconds of data: a live fight's whole seconds (the one under way is still filling), a kept one's all.</summary>
        public int N;
        public bool Live;
        public string Outcome = "";
        /// <summary>The frame: the time axis's length (seconds) and the two value scales' tops. Fixed while a fight runs (see the class).</summary>
        public double Extent, YMax, HealMax;
        /// <summary>The right-hand column for the lines' names and figures, wide enough for the widest (DIPs).</summary>
        public double Gutter = 100;
        public List<Line> Members = new();
        public double[]? HpLeft;
        /// <summary>Live: the boss's HP now (the ring's figure; the line stops at the last whole second).</summary>
        public double? HpNow;
        public List<(double At, string Name, long Id)> Deaths = new();
        public List<Line> Healers = new();
        public double[]? Taken;
        public double TakenAvg;
        public List<Line> Takers = new();
        /// <summary>The fight's time as the hero says it (a kill's label matches its clock).</summary>
        public string Clock = "";
        public string Sig = "";
        public bool Has(string kind) => N >= 2 && kind switch { Dps => Members.Count > 0, Heal => Healers.Count > 0 || Taken != null, _ => false };
    }

    /// <summary>Your line's gold: the window's on a dark look, a deeper one on Daylight so it keeps its contrast (as GoldInk).</summary>
    static Color YouColor => Dark ? Gold : Color.FromRgb(0xD0, 0x86, 0x00);

    /// <summary>A Gaussian rolling average (sigma <paramref name="sigma"/> seconds, out to three sigma, weighed again at the ends).</summary>
    public static double[] Gauss(IReadOnlyList<double> s, double sigma)
    {
        int r = Math.Max(1, (int)Math.Ceiling(3 * sigma));
        var w = new double[r + 1];
        for (int j = 0; j <= r; j++) w[j] = Math.Exp(-0.5 * j * j / (sigma * sigma));
        var o = new double[s.Count];
        for (int i = 0; i < s.Count; i++)
        {
            double sum = 0, wt = 0;
            for (int j = Math.Max(0, i - r); j <= Math.Min(s.Count - 1, i + r); j++) { double x = w[Math.Abs(i - j)]; sum += s[j] * x; wt += x; }
            o[i] = wt > 0 ? sum / wt : 0;
        }
        return o;
    }

    static double[] Pad(IReadOnlyList<long>? l, int n) { var r = new double[n]; if (l != null) for (int i = 0; i < n && i < l.Count; i++) r[i] = l[i]; return r; }

    /// <summary>The value scales' steps: 1, 1.5, 2, 2.5, 3, 4, 5, 6, 8 of each power of ten. A scale only ever moves up one of these.</summary>
    static readonly double[] Ladder = Enumerable.Range(2, 6).SelectMany(p => new[] { 1, 1.5, 2, 2.5, 3, 4, 5, 6, 8 }.Select(m => m * Math.Pow(10, p))).ToArray();
    public static double Step(double v) => Ladder.FirstOrDefault(x => x >= v, Ladder[^1]);

    /// <summary>
    /// A live fight's time scale: the length this boss usually takes (your best kill of it, else your last pull of it, else
    /// <see cref="DefaultSeconds"/>) with a little room, in half minutes; half as long again each time the fight runs past it.
    /// A kept fight's: exactly its length.
    /// </summary>
    public static double ExtentOf(FightView v, int n, bool live)
    {
        double end = Math.Max(1, n - 1);
        if (!live) return end;
        static double Up30(double x) => Math.Ceiling(x / 30) * 30;
        double ext = Up30(Math.Max(60, (v.Best?.Seconds ?? v.Last?.Seconds ?? DefaultSeconds) * 1.1));
        while (end > ext) ext = Up30(ext * 1.5);
        return ext;
    }

    static readonly Dictionary<string, double> _pastPeak = new();
    /// <summary>The best a member's line reached in your best and last pulls of this boss (the DPS scale starts there; 0: none).</summary>
    static double PastPeak(FightView v)
    {
        var refs = new[] { v.Best, v.Last }.Where(r => r != null).Select(r => r!.E).ToList();
        if (refs.Count == 0) return 0;
        string key = string.Join("|", refs.Select(e => e.Id));
        if (_pastPeak.TryGetValue(key, out var peak)) return peak;
        peak = 0;
        foreach (var e in refs)
            foreach (var f in e.Members)
                if ((e.BossView ? f.BossPerSecond : f.PerSecond) is { Count: > 1 } ps) peak = Math.Max(peak, Gauss(ps.Select(x => (double)x).ToList(), Sigma).Max());
        if (_pastPeak.Count > 200) _pastPeak.Clear();
        return _pastPeak[key] = peak;
    }

    static readonly Typeface MeasureFace = new(PK.Face, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
    static double Wide(string s, double size) => new FormattedText(s, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, MeasureFace, size, Brushes.Black, 1).WidthIncludingTrailingWhitespace;

    /// <summary>A figure as the end labels write it: whole up to 99,999, then "123K".</summary>
    public static string Fig(double v) => v < 99_999.5 ? Int(v) : Amounts.Short(v);

    public static Set Of(FightView v, bool live)
    {
        var e = v.E;
        var s = new Set { Live = live, Outcome = live ? "" : e.Outcome };
        var dealers = v.Members.Where(m => m.PerSecond is { Count: > 0 }).ToList();
        int n = live ? (int)Math.Floor(e.Duration(v.Now).TotalSeconds)
            : Math.Max(dealers.Select(m => m.PerSecond!.Count).DefaultIfEmpty(0).Max(), (int)Math.Ceiling(e.Duration(v.Now).TotalSeconds - 0.001));
        s.N = Math.Max(0, n);
        if (s.N < 2) { s.Sig = "none"; return s; }
        n = s.N;
        s.Clock = BossParser.Clock(e.Duration(v.Now));
        if (live) s.HpNow = e.BossHpLeft;
        Line Of(MemberView m, IReadOnlyList<long>? l)
        {
            var raw = Pad(l, n);
            return new Line { Id = m.F.Id, Name = ParserRows.NameOf(m.F), Color = m.You ? YouColor : ClassArt.ColorOf(m.F.Class), You = m.You, Raw = raw, S = Gauss(raw, Sigma), Avg = raw.Sum() / Math.Max(1, n) };
        }
        foreach (var m in dealers.OrderBy(m => m.Rank)) s.Members.Add(Of(m, m.PerSecond));
        if (e.BossHpPerSecond is { Count: > 1 } hps)
        {
            var hp = new double[n];
            for (int i = 0; i < n; i++) hp[i] = Math.Clamp(i < hps.Count ? hps[i] : hps[^1], 0, 1);
            if (!live && e.Outcome == "kill") hp[^1] = 0;
            s.HpLeft = hp;
        }
        foreach (var m in v.Members) foreach (var d in m.DeathLog) s.Deaths.Add((d.At, ParserRows.NameOf(m.F), m.F.Id));
        // healers: who healed another member (a damage dealer's own self-heals are no healer's line)
        foreach (var m in v.Members.Where(m => m.F.HealPerSecond is { Count: > 0 } h && h.Sum() > 0 && m.F.HealingOthers > 0).OrderByDescending(m => m.F.HealingEffective))
            s.Healers.Add(Of(m, m.F.HealPerSecond));
        var takers = v.Members.Where(m => m.F.TakenPerSecond is { Count: > 0 } t && t.Sum() > 0).ToList();
        if (takers.Count > 0)
        {
            foreach (var m in takers) s.Takers.Add(Of(m, m.F.TakenPerSecond));
            var sum = new double[n];
            foreach (var t in s.Takers) for (int i = 0; i < n; i++) sum[i] += t.Raw[i];
            s.Taken = Gauss(sum, Sigma);
            s.TakenAvg = sum.Sum() / n;
        }
        // the frame: the time from what this boss usually takes, the DPS scale from the best seen on it, each moving only in steps
        s.Extent = ExtentOf(v, n, live);
        double peak = s.Members.Count > 0 ? s.Members.Max(m => m.S.Max()) : 0, past = PastPeak(v);
        s.YMax = Step(Math.Max(past > 0 ? past * 1.05 : 5_000, peak * 1.04));
        s.HealMax = Step(Math.Max(2_000, Math.Max(s.Healers.Select(h => h.S.Max()).DefaultIfEmpty(0).Max(), s.Taken?.Max() ?? 0) * 1.04));
        // the names' column: the widest name with a figure as wide as it can get
        double fig = Wide("88,888", 10.5) + 8;
        var names = s.Members.Select(m => m.Name).Concat(s.Healers.Select(h => h.Name)).Append(L.T("Boss HP")).Append(s.Taken != null ? L.T("Damage taken") : "");
        s.Gutter = Math.Clamp(names.Max(x => Wide(x, 10.5)) + fig + 22, 96, 170);
        s.Sig = string.Join("|", n, s.Members.Sum(m => m.Raw.Sum()), s.Members.Count, s.HpLeft?[^1] ?? -1, s.HpNow ?? -1, s.Healers.Sum(h => h.Raw.Sum()), s.Taken?.Sum() ?? -1, s.Deaths.Count, s.Outcome, s.Extent, s.YMax);
        return s;
    }

    // ================================================================ the colours a chart is drawn in (the window's look when built)

    public sealed class Pal
    {
        public Brush Ink = null!, Muted = null!, Faint = null!, Grid = null!, GridMajor = null!, Box = null!, BoxEdge = null!, Back = null!;
        public Color InkC, Under;
        public bool Dark;
        public Typeface Semi = null!, Bold = null!;
        public static Pal Now()
        {
            bool dark = PK.Dark;
            var ink = K("Ink");
            return new Pal
            {
                Dark = dark,
                Ink = ink, Muted = K("Muted"), Faint = K("Faint"),
                InkC = (ink as SolidColorBrush)?.Color ?? (dark ? White : Black),
                Grid = C(dark ? White : Black, dark ? 0.055 : 0.07),
                GridMajor = C(dark ? White : Black, dark ? 0.10 : 0.12),
                Box = C(dark ? Color.FromRgb(0x10, 0x13, 0x1B) : Color.FromRgb(0xFF, 0xFF, 0xFF), 0.97),
                BoxEdge = C(dark ? White : Black, dark ? 0.16 : 0.14),
                Back = C(dark ? Color.FromRgb(0x0C, 0x0E, 0x14) : White, dark ? 0.7 : 0.78),
                // the outline under each line: the card's own dark (or white on Daylight), so crossing lines stay apart
                Under = dark ? Color.FromRgb(0x0A, 0x0C, 0x12) : Colors.White,
                Semi = new Typeface(PK.Face, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
                Bold = new Typeface(PK.Face, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
            };
        }
    }

    // ================================================================ one chart

    /// <summary>
    /// One graph, drawn in one pass and kept frozen: WPF renders a chart again whenever the page around it is laid out anew (a
    /// live fight's page is, on every hit), and that only replays the drawing. The hover is a layer of its own over it.
    /// </summary>
    public sealed class Chart : FrameworkElement
    {
        public readonly string Kind;
        readonly Pal _p;
        readonly DrawingVisual _hover = new();
        Set? _set;
        Rect _plot;
        int? _hoverAt; bool _hoverBox;
        DrawingGroup? _cache;
        (Set? Set, Size Size, double Dpi) _cacheFor;
        /// <summary>The healing's graph under the fight's on the page: the fight's graph writes the times for both.</summary>
        public bool SharedTime;
        /// <summary>The panel's crosshair: a second hovered here (null: none), so the other chart marks it too.</summary>
        public Action<int?>? Hovered;

        public Chart(string kind, Pal p)
        {
            Kind = kind; _p = p;
            AddVisualChild(_hover);
            ClipToBounds = true; Cursor = Cursors.Cross; SnapsToDevicePixels = true;
            AutomationProperties.SetName(this, kind == Dps ? "DPS and boss HP graph" : "Healing and damage taken graph");
            MouseMove += (_, ev) => { var at = SecondAt(ev.GetPosition(this).X); Hovered?.Invoke(at); ShowHover(at, true); };
            MouseLeave += (_, _) => { Hovered?.Invoke(null); ShowHover(null, false); };
        }

        protected override int VisualChildrenCount => 1;
        protected override Visual GetVisualChild(int index) => _hover;

        public Set? Data => _set;
        public void SetData(Set s) { _set = s; InvalidateVisual(); }

        int? SecondAt(double x)
        {
            if (_set is not { N: >= 2 } s || _plot.Width <= 0 || x > _plot.Right + 2) return null;
            return (int)Math.Clamp(Math.Round((x - _plot.Left) / _plot.Width * s.Extent), 0, s.N - 1);
        }

        double X(double t) => _plot.Left + t / _set!.Extent * _plot.Width;
        double Y(double v) => _plot.Bottom - Math.Clamp(v / Top, 0, 1.02) * _plot.Height;
        double YHp(double hp) => _plot.Bottom - Math.Clamp(hp, 0, 1) * _plot.Height;
        double Top => Kind == Dps ? _set!.YMax : _set!.HealMax;

        FormattedText Ft(string s, double size, Brush b, bool bold = false) =>
            new(s, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, bold ? _p.Bold : _p.Semi, size, b, VisualTreeHelper.GetDpi(this).PixelsPerDip);

        static Pen P(Brush b, double w, bool dash = false, double on = 2, double off = 3)
        {
            var p = new Pen(b, w) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            if (dash) p.DashStyle = new DashStyle(new[] { on, off }, 0);
            p.Freeze();
            return p;
        }

        protected override void OnRender(DrawingContext dc)
        {
            var size = RenderSize;
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(size));   // the whole chart takes the mouse
            if (_set is not { } s || size.Width < 60 || size.Height < 40) return;
            double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            if (_cache == null || !ReferenceEquals(_cacheFor.Set, s) || _cacheFor.Size != size || _cacheFor.Dpi != dpi)
            {
                Draws++;
                long t0 = Stopwatch.GetTimestamp();
                var g = new DrawingGroup();
                using (var gc = g.Open()) Paint(gc, s, size);
                g.Freeze();
                (_cache, _cacheFor) = (g, (s, size, dpi));
                DrawMs += Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
            }
            dc.DrawDrawing(_cache);
            if (_hoverAt != null) ShowHover(_hoverAt, _hoverBox);
        }

        void Paint(DrawingContext dc, Set s, Size size)
        {
            if (!s.Has(Kind))
            {
                var wait = Ft(L.T("The graphs fill in after the first seconds of the fight."), 11.5, _p.Faint);
                dc.DrawText(wait, new Point((size.Width - wait.Width) / 2, (size.Height - wait.Height) / 2));
                return;
            }
            // the healing's graph sits under the fight's on the same time scale: its times are the fight's, not written again
            _plot = new Rect(new Point(46, 8), new Point(Math.Max(120, size.Width - s.Gutter), size.Height - (Kind == Dps || !SharedTime ? 20 : 6)));
            Frame(dc, s);
            if (Kind == Dps) DrawFight(dc, s); else DrawHeal(dc, s);
        }

        // ---- the frame: time across (a light line every 15 or 30 s), the value scale up the left, quarters

        void Frame(DrawingContext dc, Set s)
        {
            var minor = P(_p.Grid, 1); var major = P(_p.GridMajor, 1);
            // the value scale: a line at each quarter, labelled where there's room
            bool quarters = _plot.Height >= 110;
            for (int k = 0; k <= 4; k++)
            {
                if (!quarters && k % 2 == 1) continue;
                double v = Top * k / 4, y = Math.Round(Y(v)) + 0.5;
                dc.DrawLine(k == 0 ? major : minor, new Point(_plot.Left, y), new Point(_plot.Right, y));
                var t = Ft(Amounts.Short(v), 10, _p.Faint);
                dc.DrawText(t, new Point(_plot.Left - 6 - t.Width, y - t.Height / 2));
            }
            // time: a light line every 15 s (30 s, a minute on a long frame), labelled where the labels don't crowd
            double step = s.Extent <= 150 ? 15 : s.Extent <= 330 ? 30 : s.Extent <= 660 ? 60 : 120;
            double perLabel = Math.Ceiling(46 / (step / s.Extent * _plot.Width));
            int idx = 0;
            for (double t = 0; t <= s.Extent + 0.01; t += step, idx++)
            {
                double x = Math.Round(X(t)) + 0.5;
                if (t > 0) dc.DrawLine(t % 60 == 0 ? major : minor, new Point(x, _plot.Top), new Point(x, _plot.Bottom));
                if (idx % perLabel != 0 || (Kind == Heal && SharedTime)) continue;
                var ft = Ft(PK.Clock(t), 10, _p.Faint);
                dc.DrawText(ft, new Point(Math.Clamp(x - ft.Width / 2, 0, _plot.Right - ft.Width / 2), _plot.Bottom + 4));
            }
            dc.DrawLine(major, new Point(_plot.Right + 0.5, _plot.Top), new Point(_plot.Right + 0.5, _plot.Bottom));
        }

        // ---- the series

        /// <summary>A smooth line through one value a second (Catmull-Rom as Béziers; straight once the points are close).</summary>
        StreamGeometry Curve(IReadOnlyList<double> s, Func<double, double> y)
        {
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                var pts = new Point[s.Count];
                for (int i = 0; i < s.Count; i++) pts[i] = new Point(X(i), y(s[i]));
                ctx.BeginFigure(pts[0], false, false);
                if (_plot.Width / Math.Max(1, _set!.Extent) < 3) ctx.PolyLineTo(pts.Skip(1).ToList(), true, true);
                else
                    for (int i = 0; i < pts.Length - 1; i++)
                    {
                        Point p0 = pts[Math.Max(0, i - 1)], p1 = pts[i], p2 = pts[i + 1], p3 = pts[Math.Min(pts.Length - 1, i + 2)];
                        var c1 = new Point(p1.X + (p2.X - p0.X) / 6, Math.Clamp(p1.Y + (p2.Y - p0.Y) / 6, _plot.Top - 4, _plot.Bottom));
                        var c2 = new Point(p2.X - (p3.X - p1.X) / 6, Math.Clamp(p2.Y - (p3.Y - p1.Y) / 6, _plot.Top - 4, _plot.Bottom));
                        ctx.BezierTo(c1, c2, p2, true, true);
                    }
            }
            g.Freeze();
            return g;
        }

        /// <summary>The area under a series, down to the floor.</summary>
        StreamGeometry Area(IReadOnlyList<double> top, Func<double, double> y)
        {
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(new Point(X(0), _plot.Bottom), true, true);
                var pts = new List<Point>(top.Count + 1);
                for (int i = 0; i < top.Count; i++) pts.Add(new Point(X(i), y(top[i])));
                pts.Add(new Point(X(top.Count - 1), _plot.Bottom));
                ctx.PolyLineTo(pts, false, true);
            }
            g.Freeze();
            return g;
        }

        /// <summary>A member's line: an outline of the card's dark under it so lines that cross stay apart; yours glows gold.</summary>
        void Series(DrawingContext dc, Line l, double thick, Func<double, double> y)
        {
            var g = Curve(l.S, y);
            if (l.You) dc.DrawGeometry(null, P(C(l.Color, 0.20), thick + 7), g);
            dc.DrawGeometry(null, P(C(_p.Under, _p.Dark ? 0.75 : 0.9), thick + 2.6), g);
            dc.DrawGeometry(null, P(C(l.Color, l.You ? 1 : 0.95), thick), g);
            if (_set!.Live) { var head = new Point(X(l.S.Length - 1), y(l.S[^1])); dc.DrawEllipse(C(_p.Under), null, head, l.You ? 4.6 : 3.8, l.You ? 4.6 : 3.8); dc.DrawEllipse(C(l.Color), null, head, l.You ? 3.2 : 2.5, l.You ? 3.2 : 2.5); }
        }

        void DrawFight(DrawingContext dc, Set s)
        {
            // the boss's HP behind the lines: its scale on the right (inside the frame), the thirds, a soft area under it
            if (s.HpLeft is { } hp)
            {
                var guide = P(C(_p.InkC, _p.Dark ? 0.22 : 0.28), 1, dash: true, on: 5, off: 4);
                // the thirds: a dashed guide across, named at the left (the HP line starts at the top there, the right end is the lines')
                foreach (var th in new[] { 2 / 3.0, 1 / 3.0 })
                {
                    double y = Math.Round(YHp(th)) + 0.5;
                    dc.DrawLine(guide, new Point(_plot.Left, y), new Point(_plot.Right, y));
                }
                var fill = new LinearGradientBrush(C(_p.InkC, _p.Dark ? 0.09 : 0.07).Color, C(_p.InkC, 0).Color, 90); fill.Freeze();
                dc.DrawGeometry(fill, null, Area(hp, YHp));
            }
            foreach (var l in s.Members.Where(m => !m.You).Reverse()) Series(dc, l, 1.7, Y);
            foreach (var l in s.Members.Where(m => m.You)) Series(dc, l, 2.7, Y);
            if (s.HpLeft is { } h)
            {
                // the HP line over the members' (it is the fight's spine): straight, second to second
                var hpG = new StreamGeometry();
                using (var ctx = hpG.Open())
                {
                    ctx.BeginFigure(new Point(X(0), YHp(h[0])), false, false);
                    ctx.PolyLineTo(Enumerable.Range(1, h.Length - 1).Select(i => new Point(X(i), YHp(h[i]))).ToList(), true, true);
                }
                hpG.Freeze();
                dc.DrawGeometry(null, P(C(_p.Under, 0.8), 4.2), hpG);
                dc.DrawGeometry(null, P(C(_p.InkC, 0.9), 2), hpG);
                foreach (var th in new[] { 2 / 3.0, 1 / 3.0 })
                {
                    int i = Array.FindIndex(h, x => x <= th);
                    if (i <= 0 || h[0] <= th) continue;
                    var c = new Point(X(i), YHp(th));
                    dc.DrawEllipse(C(_p.Under), P(C(_p.InkC), 1.6), c, 3.8, 3.8);
                    var t = Ft(PK.Clock(i), 10, _p.Muted, bold: true);
                    Label(dc, t, new Point(Math.Clamp(c.X - t.Width / 2, _plot.Left, _plot.Right - t.Width), Math.Max(_plot.Top, c.Y - 6 - t.Height)));
                }
            }
            // the thirds' names over the lines, at the left
            if (s.HpLeft != null)
                foreach (var (th, word) in new[] { (2 / 3.0, "⅔"), (1 / 3.0, "⅓") })
                {
                    var t = Ft(L.F("{0} HP", word), 10, _p.Muted, bold: true);
                    Label(dc, t, new Point(_plot.Left + 5, Math.Round(YHp(th)) - t.Height - 1));
                }
            Deaths(dc, s);
            // the end: the kill (mint) or the wipe and the HP it left (coral)
            if (s.Outcome is "kill" or "wipe")
            {
                bool kill = s.Outcome == "kill";
                double xe = X(s.N - 1);
                var tone = C(kill ? (_p.Dark ? Mint : Color.FromRgb(0x12, 0x96, 0x58)) : Coral);
                dc.DrawLine(P(tone, 1.6), new Point(xe, _plot.Top), new Point(xe, _plot.Bottom));
                var t = Ft(kill ? L.F("Killed at {0}", s.Clock) : L.F("Wiped · {0} HP left", Pct(s.HpLeft?[^1] ?? 0, 0)), 10.5, tone, bold: true);
                Label(dc, t, new Point(xe - 6 - t.Width, _plot.Top + 14));
                if (s.HpLeft is { } h2) dc.DrawEllipse(tone, null, new Point(xe, YHp(h2[^1])), 4, 4);
            }
            else if (s.Live && s.HpLeft is { } h3)
            {
                var head = new Point(X(s.N - 1), YHp(h3[^1]));
                dc.DrawEllipse(C(_p.InkC, 0.18), null, head, 7, 7);
                dc.DrawEllipse(C(_p.InkC), null, head, 3.4, 3.4);
            }
            // the names and figures at the lines' ends, the boss's HP with them
            var ends = s.Members.Select(m => (Y: Y(m.S[^1]), Head: new Point(X(m.S.Length - 1), Y(m.S[^1])), Name: m.Name, Value: Fig(m.Avg), Color: m.Color, Bold: m.You)).ToList();
            if (s.HpLeft is { } h4) ends.Add((YHp(h4[^1]), new Point(X(s.N - 1), YHp(h4[^1])), L.T("Boss HP"), Pct(s.HpNow ?? h4[^1], 0), _p.InkC, true));
            Ends(dc, s, ends);
        }

        /// <summary>A death: a coral mark on the member's line at its second; one label for deaths close together.</summary>
        void Deaths(DrawingContext dc, Set s)
        {
            var coral = C(Coral); var ring = P(C(_p.Under, 0.9), 1.5); var cross = P(Brushes.White, 1.4);
            var marks = new List<(Point C, string Name)>();
            foreach (var (at, name, id) in s.Deaths.Where(d => d.At <= s.N).OrderBy(d => d.At))
            {
                var l = s.Members.FirstOrDefault(m => m.Id == id);
                int i = (int)Math.Clamp(Math.Round(at), 0, s.N - 1);
                var c = new Point(X(Math.Min(at, s.N - 1)), l != null ? Y(l.S[i]) : _plot.Bottom - 6);
                dc.DrawEllipse(coral, ring, c, 5.5, 5.5);
                dc.DrawLine(cross, new Point(c.X - 2.2, c.Y - 2.2), new Point(c.X + 2.2, c.Y + 2.2));
                dc.DrawLine(cross, new Point(c.X - 2.2, c.Y + 2.2), new Point(c.X + 2.2, c.Y - 2.2));
                marks.Add((c, name));
            }
            for (int a = 0; a < marks.Count;)
            {
                int b = a;
                while (b + 1 < marks.Count && marks[b + 1].C.X - marks[b].C.X < 70) b++;
                var group = marks.Skip(a).Take(b - a + 1).ToList();
                var t = Ft(group.Count == 1 ? L.F("{0} died", group[0].Name) : L.F("{0} deaths", group.Count), 10.5, coral, bold: true);
                double x0 = group[0].C.X, x1 = group[^1].C.X, y = group.Min(g => g.C.Y);
                double tx = x1 + 9 + t.Width <= _plot.Right ? x1 + 9 : x0 - 9 - t.Width;
                Label(dc, t, new Point(Math.Max(_plot.Left, tx), Math.Clamp(y - t.Height / 2, _plot.Top, _plot.Bottom - t.Height)));
                a = b + 1;
            }
        }

        /// <summary>
        /// The end labels: each line's name and figure in the column right of the frame, at its line's height, pushed apart so none
        /// sits on another; a hairline from a line's head when the label had to move (or, live, from the head across the time
        /// still to come).
        /// </summary>
        void Ends(DrawingContext dc, Set s, List<(double Y, Point Head, string Name, string Value, Color Color, bool Bold)> ends)
        {
            if (ends.Count == 0) return;
            const double gap = 14;
            var order = ends.OrderBy(x => x.Y).ToList();
            var ys = new double[order.Count];
            double top = _plot.Top + 6, bottom = _plot.Bottom - 4;
            for (int i = 0; i < order.Count; i++) ys[i] = Math.Max(order[i].Y, i == 0 ? top : ys[i - 1] + gap);
            if (ys[^1] > bottom) { ys[^1] = bottom; for (int i = order.Count - 2; i >= 0; i--) ys[i] = Math.Min(ys[i], ys[i + 1] - gap); }
            double lx = _plot.Right + 8;
            for (int i = 0; i < order.Count; i++)
            {
                var (y0, head, name, value, color, bold) = order[i];
                double y = ys[i];
                bool moved = Math.Abs(y - y0) > 1.5 || head.X < _plot.Right - 2;
                if (moved) dc.DrawLine(P(C(color, 0.35), 1, dash: head.X < _plot.Right - 2, on: 1, off: 3), new Point(head.X + 4, y0), new Point(lx - 3, y));
                dc.DrawRoundedRectangle(C(color), null, new Rect(lx, y - 1.5, 7, 3), 1.5, 1.5);
                var v = Ft(value, 10.5, bold ? (color == _p.InkC ? _p.Ink : GoldInk) : _p.Ink, bold: true);
                double vx = _plot.Right + s.Gutter - 4 - v.Width;
                var n = Ft(name, 10.5, C(color, 0.95), bold: bold);
                n.MaxTextWidth = Math.Max(10, vx - (lx + 11) - 6); n.Trimming = TextTrimming.CharacterEllipsis; n.MaxLineCount = 1;
                dc.DrawText(n, new Point(lx + 11, y - n.Height / 2));
                dc.DrawText(v, new Point(vx, y - v.Height / 2));
            }
        }

        void DrawHeal(DrawingContext dc, Set s)
        {
            if (s.Taken is { } taken)
            {
                dc.DrawGeometry(C(Coral, 0.15), null, Area(taken, Y));
                dc.DrawGeometry(null, P(C(Coral, 0.8), 1.3), Curve(taken, Y));
            }
            foreach (var l in s.Healers.Where(h => !h.You).Reverse()) Series(dc, l, 1.8, Y);
            foreach (var l in s.Healers.Where(h => h.You)) Series(dc, l, 2.4, Y);
            var ends = s.Healers.Select(h => (Y: Y(h.S[^1]), Head: new Point(X(h.S.Length - 1), Y(h.S[^1])), Name: h.Name, Value: Fig(h.Avg), Color: h.Color, Bold: h.You)).ToList();
            if (s.Taken is { } t) ends.Add((Y(t[^1]), new Point(X(t.Length - 1), Y(t[^1])), L.T("Damage taken"), Fig(s.TakenAvg), Coral, false));
            Ends(dc, s, ends);
        }

        /// <summary>Words on the chart over a soft backing, so the lines under them never cross them.</summary>
        void Label(DrawingContext dc, FormattedText t, Point at)
        {
            dc.DrawRoundedRectangle(_p.Back, null, new Rect(at.X - 3, at.Y - 0.5, t.Width + 6, t.Height + 1), 4, 4);
            dc.DrawText(t, at);
        }

        // ---- the hover: a crosshair at the second, and on the chart under the mouse every line's value then

        public void ShowHover(int? at, bool box)
        {
            _hoverAt = at; _hoverBox = box;
            using var dc = _hover.RenderOpen();
            if (at is not int i || _set is not { } s || !s.Has(Kind) || _plot.Width <= 0) return;
            double x = Math.Round(X(i)) + 0.5;
            dc.DrawLine(P(C(_p.InkC, 0.4), 1), new Point(x, _plot.Top), new Point(x, _plot.Bottom));
            if (!box) return;
            var rows = HoverRows(i);
            var title = Ft(L.F(Kind == Heal ? "{0} · HP a second, {1} s average" : "{0} · {1} s average", PK.Clock(i), AverageSeconds), 10.5, _p.Muted, bold: true);
            var cells = rows.Select(r => (r.Dot, Name: Ft(r.Name, 11.5, _p.Ink), Value: Ft(r.Value, 11.5, r.Tone ?? _p.Ink, bold: true))).ToList();
            // as many rows as the chart is tall, then a column more beside them (the box never leaves the chart)
            const double rowH = 16;
            int fit = Math.Max(1, (int)((RenderSize.Height - 4 - (title.Height + 15)) / rowH));
            int cols = Math.Max(1, (int)Math.Ceiling(cells.Count / (double)fit)), per = (int)Math.Ceiling(cells.Count / (double)cols);
            double colW = cells.Select(c => (c.Dot != null ? 14 : 0) + c.Name.Width + 14 + c.Value.Width).DefaultIfEmpty(0).Max();
            double w = Math.Max(title.Width, cols * colW + (cols - 1) * 16) + 20;
            double h = 9 + title.Height + 4 + per * rowH + 4;
            double bx = x + 12 + w > _plot.Right ? x - 12 - w : x + 12;
            if (bx < 0) bx = Math.Max(0, RenderSize.Width - w);
            double by = Math.Clamp((IsMouseOver ? Mouse.GetPosition(this).Y : _plot.Top + h / 2) - h / 2, 2, Math.Max(2, RenderSize.Height - h - 2));
            dc.DrawRoundedRectangle(_p.Box, P(_p.BoxEdge, 1), new Rect(bx, by, w, h), 7, 7);
            dc.DrawText(title, new Point(bx + 10, by + 6));
            for (int k = 0; k < cells.Count; k++)
            {
                var (dot, name, value) = cells[k];
                double cx = bx + 10 + k / per * (colW + 16), y = by + 8 + title.Height + 3 + k % per * rowH;
                double nx = cx;
                if (dot != null) { dc.DrawEllipse(dot, null, new Point(nx + 4, y + rowH / 2), 3.5, 3.5); nx += 14; }
                dc.DrawText(name, new Point(nx, y + (rowH - name.Height) / 2));
                dc.DrawText(value, new Point(cx + colW - value.Width, y + (rowH - value.Height) / 2));
            }
        }

        /// <summary>The hover's rows at a second: a coloured dot, a name, the value (and its own tone).</summary>
        public List<(Brush? Dot, string Name, string Value, Brush? Tone)> HoverRows(int i)
        {
            var rows = new List<(Brush?, string, string, Brush?)>();
            if (_set is not { } s || i >= s.N) return rows;
            if (Kind == Dps)
            {
                foreach (var l in s.Members.OrderByDescending(m => m.S[i])) rows.Add((C(l.Color), l.Name, Int(l.S[i]), l.You ? GoldInk : null));
                if (s.HpLeft != null) rows.Add((C(_p.InkC), L.T("Boss HP"), Pct(s.HpLeft[i], 0), null));
                foreach (var d in s.Deaths.Where(d => Math.Abs(d.At - i) <= 1)) rows.Add((C(Coral), L.F("{0} died", d.Name), PK.Clock(d.At), C(Coral)));
            }
            else
            {
                foreach (var l in s.Healers.OrderByDescending(h => h.S[i])) rows.Add((C(l.Color), l.Name, Int(l.S[i]), l.You ? GoldInk : null));
                if (s.Taken is { } t)
                {
                    rows.Add((C(Coral), L.T("Damage taken"), Int(t[i]), C(Coral)));
                    foreach (var l in s.Takers.OrderByDescending(x => x.S[i]).Take(3).Where(x => x.S[i] > 0)) rows.Add((null, "  " + l.Name, Int(l.S[i]), _p.Muted));
                }
            }
            return rows;
        }
    }

    // ================================================================ the heads

    /// <summary>A graph's title as its data says it (only the parts it has).</summary>
    public static string Title(string kind, Set s) => kind switch
    {
        Dps => s.HpLeft != null ? L.T("DPS and boss HP") : L.T("DPS over the fight"),
        _ => s.Healers.Count > 0 && s.Taken != null ? L.T("Healing and damage taken") : s.Healers.Count > 0 ? L.T("Healing") : L.T("Damage taken"),
    };

    /// <summary>A graph's head: the title, a quiet line beside it, and (on the page) its Copy picture.</summary>
    static Grid Head(TextBlock title, FrameworkElement? sub, FrameworkElement? copy)
    {
        var g = Cols(0, -1, 0);
        g.Children.Add(title);
        if (sub != null) g.Children.Add(Col(M(sub, 10, 0, 10, 0), 1));
        if (copy != null) g.Children.Add(Col(copy, 2));
        return g;
    }

    static TextBlock TitleText(string text, double size = 13) => T(text, size, K("Ink"), FontWeights.SemiBold, trim: false);

    /// <summary>The quiet words beside the title: how the lines are averaged (and, live, how far in).</summary>
    static TextBlock Sub(string text) => T(text, 11, K("Faint"), FontWeights.Medium, trim: false);

    /// <summary>A graph's Copy picture: a real button in its head (a picture of this graph in the share frame).</summary>
    static Button CopyButton(string kind, Action<string> copy)
    {
        var icon = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M4,1.5 L10.5,1.5 L10.5,8 M1.5,4 L8,4 L8,10.5 L1.5,10.5 Z"), StrokeThickness = 1.3, Width = 12, Height = 12,
            StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, VerticalAlignment = VerticalAlignment.Center,
        };
        icon.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "Ink");
        var text = new TextBlock { Text = L.T("Copy picture"), Margin = new Thickness(5, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var b = new Button
        {
            Content = new StackPanel { Orientation = Orientation.Horizontal, Children = { icon, text } }, Height = 22, FontSize = 11, Padding = new Thickness(7, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center, Tag = "graph-copy-" + kind, Cursor = Cursors.Hand,
            ToolTip = L.T("A picture of this graph to paste in Discord or anywhere. Nothing is uploaded."),
        };
        AutomationProperties.SetName(b, "Copy a picture of this graph");
        b.Click += (_, _) => copy(kind);
        return b;
    }

    // ================================================================ the page's view: one card, kept across the page's redraws

    /// <summary>
    /// The Graphs tab's view of one fight, one card: the fight's graph across the top (always), the healing and damage taken
    /// under a hairline, smaller, when the record has them. Kept by the window while the same fight shows, so the page's many
    /// redraws a second only lay it out again; its charts are drawn when their data moves, a live fight's at most every
    /// <see cref="LiveFrameMs"/>.
    /// </summary>
    public sealed class GraphsPanel : Border
    {
        public readonly string Key;
        readonly Pal _p = Pal.Now();
        readonly Dictionary<string, Chart> _charts = new();
        readonly Dictionary<string, FrameworkElement> _parts = new();
        readonly Dictionary<string, TextBlock> _titles = new();
        readonly TextBlock _sub = Sub("");
        readonly Border _rule;
        (FightView V, bool Live)? _want;
        long _lastDraw;
        DispatcherTimer? _timer;
        Set? _set;
        double _fightH = 200, _healH = 80, _room = double.NaN;
        /// <summary>For the options test: the times this panel worked its graphs out again.</summary>
        public int Redraws;

        public GraphsPanel(string key, Action<string> copy)
        {
            Key = key; Tag = "parser-graphs";
            var body = new StackPanel();
            foreach (var kind in Kinds)
            {
                var chart = new Chart(kind, _p) { Height = kind == Dps ? _fightH : _healH, SharedTime = kind == Heal };
                chart.Hovered = at => { foreach (var c in _charts.Values) if (c != chart) c.ShowHover(at, false); };
                _charts[kind] = chart;
                var title = TitleText("", kind == Dps ? 13 : 12.5); _titles[kind] = title;
                var part = new StackPanel { Tag = "graph-card-" + kind };
                part.Children.Add(M(Head(title, kind == Dps ? _sub : null, CopyButton(kind, copy)), 0, 0, 0, 4));
                part.Children.Add(chart);
                _parts[kind] = part;
                body.Children.Add(part);
                if (kind == Dps)
                {
                    _rule = new Border { Height = 1, Margin = new Thickness(0, 8, 0, 7), Background = K("CardHair") };
                    body.Children.Add(_rule);
                }
            }
            _rule ??= new Border();
            Child = Glass(body, new Thickness(12, 9, 12, 8), 14);
        }

        public Chart ChartOf(string kind) => _charts[kind];
        /// <summary>A graph's part of the card (its head and chart), collapsed when the fight has nothing for it.</summary>
        public FrameworkElement CardOf(string kind) => _parts[kind];
        public Set? Shown => _set;

        /// <summary>The fight now: drawn at once when kept (or the first time), else at most every <see cref="LiveFrameMs"/>.</summary>
        public void Show(FightView v, bool live)
        {
            _want = (v, live);
            double since = Stopwatch.GetElapsedTime(_lastDraw).TotalMilliseconds;
            if (!live || _lastDraw == 0 || since >= LiveFrameMs) { DrawWanted(); return; }
            if (_timer == null)
            {
                _timer = new DispatcherTimer(DispatcherPriority.Background);
                _timer.Tick += (_, _) => DrawWanted();
            }
            if (!_timer.IsEnabled) { _timer.Interval = TimeSpan.FromMilliseconds(Math.Max(1, LiveFrameMs - since)); _timer.Start(); }
        }

        void DrawWanted()
        {
            _timer?.Stop();
            if (_want is not var (v, live)) return;
            _want = null;
            _lastDraw = Stopwatch.GetTimestamp();
            long t0 = Stopwatch.GetTimestamp();
            var s = Of(v, live);
            DataMs += Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
            if (_set != null && s.Sig == _set.Sig && s.Live == _set.Live) return;   // nothing moved (a hit's redraw inside the same second)
            Redraws++;
            _set = s;
            foreach (var kind in Kinds)
            {
                bool has = kind == Dps || s.Has(kind);
                _parts[kind].Visibility = has ? Visibility.Visible : Visibility.Collapsed;
                if (!has) continue;
                var title = Title(kind, s);
                if (_titles[kind].Text != title) _titles[kind].Text = title;
                _charts[kind].SetData(s);
            }
            var sub = L.F("{0} s average", AverageSeconds);
            if (_sub.Text != sub) _sub.Text = sub;
            _rule.Visibility = s.Has(Heal) ? Visibility.Visible : Visibility.Collapsed;
            Fit(_room);
        }

        /// <summary>
        /// The charts' heights for the room the window leaves under the hero (Steven: pages fit without scrolling): the fight's
        /// graph takes most, the healing the rest, each between a floor that still reads and a ceiling that doesn't sprawl.
        /// </summary>
        public void Fit(double room)
        {
            _room = room;
            bool heal = _rule.Visibility == Visibility.Visible;
            double fight = 220, small = 80;
            if (!double.IsNaN(room))
            {
                double chrome = 19 + 26 + (heal ? 16 + 26 : 0);   // the card's padding and edge, the heads, the hairline's room
                double avail = room - chrome;
                if (heal) { fight = Math.Clamp(avail * 0.7, 140, 360); small = Math.Clamp(avail - fight, 64, 150); }
                else fight = Math.Clamp(avail, 140, 420);
            }
            if (Math.Abs(fight - _fightH) > 1) { _fightH = fight; _charts[Dps].Height = fight; }
            if (Math.Abs(small - _healH) > 1) { _healH = small; _charts[Heal].Height = small; }
        }

        public void Stop() => _timer?.Stop();
    }

    // ================================================================ a graph as a picture

    /// <summary>One graph's card at a share picture's width: its head without the button, the chart taller than the page's.</summary>
    public static FrameworkElement Face(FightView v, string kind, double width)
    {
        var p = Pal.Now();
        var s = Of(v, v.E.Live);
        if (!s.Has(kind)) kind = Dps;
        var chart = new Chart(kind, p) { Height = kind == Dps ? 320 : 170 };
        chart.SetData(s);
        var body = new StackPanel();
        var sub = s.Live ? L.F("{0} in", PK.Clock(v.E.Duration(v.Now).TotalSeconds)) + "  ·  " + L.F("{0} s average", AverageSeconds) : L.F("{0} s average", AverageSeconds);
        body.Children.Add(M(Head(TitleText(Title(kind, s)), Sub(sub), null), 0, 0, 0, 8));
        body.Children.Add(chart);
        var card = Glass(body, new Thickness(14, 12, 14, 10), 14);
        card.Width = width;
        card.Tag = "share-graph-" + kind;
        return card;
    }

    /// <summary>The window's look dark for the moment the picture is drawn (as the Parser's share pictures are); put back at once.</summary>
    static T InDark<T>(Func<T> draw)
    {
        var was = Skins.Current;
        bool swap = was is not ("night" or "abyss");
        if (swap) Skins.Apply("night");
        try { return draw(); }
        finally { if (swap) Skins.Apply(was); }
    }

    /// <summary>The PNG of one graph in the share frame (Ui/MeterShare.cs), at twice its size, in the dark look.</summary>
    public static BitmapSource Picture(Encounter e, DateTime now, FightView v, string kind) =>
        InDark(() => MeterShare.Framed(e, now, () => Face(v, kind, ParserShare.ContentWidth), "parser-graph-" + kind, b => MeterShare.Snap(b, "parser graph picture")));

    /// <summary>"kinah-ledger-2026-11-18-2130-fight-Kromede-graph-dps.png".</summary>
    public static string FileName(Encounter e, string kind) => MeterShare.FileName(e)[..^4] + "-graph-" + kind + ".png";
}
