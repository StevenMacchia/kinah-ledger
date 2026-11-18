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
/// The Parser page's Graphs (Steven, 2026-11-18: "THE PARSER SHOULD DISPLAY DETAILED GRAPHICS OF THE FIGHT EITHER AS ITS GOING
/// LIKE THE LINE CHARTS OR W.E WHILE THE FIGHTS GOING ON or AT LEAST AFTER EACH FIGHT"): a fight's default view, live while it
/// runs and kept with every pull.
/// <list type="bullet">
/// <item>DPS over the fight: one smooth line a member in their class's colour, yours gold and thicker, deaths marked.</item>
/// <item>Boss HP and group DPS: the members' damage stacked (the group's DPS is its top edge, each band a member's part), the
/// boss's HP over it on its own axis with the thirds marked where it crossed them, and the kill or the wipe.</item>
/// <item>Healing and damage taken: each healer's effective healing a second over the group's damage taken a second.</item>
/// </list>
/// Each figure comes from the record (store v2's seconds); a graph with nothing behind it is not drawn. A hover shows every
/// line's value at that second, a crosshair across all three. Each chart is drawn in one pass (<see cref="DrawingContext"/>:
/// geometry and text, never an element a point); a live fight's are drawn again at most every <see cref="LiveFrameMs"/>, and
/// only for whole seconds. Each card has its own Copy picture (the share frame, Ui/MeterShare.cs).
/// </summary>
static class ParserGraphs
{
    public const string Dps = "dps", Hp = "hp", Heal = "heal";
    public static readonly string[] Kinds = { Dps, Hp, Heal };
    /// <summary>
    /// The smoothing the lines are drawn with (<see cref="Gauss"/>'s sigma, in seconds): 2, about a 5 s window, for most fights;
    /// 3 (7 s) past four minutes and 4 (9 s) past six, so a long fight's line still reads. The hover says the window (2k+1 s).
    /// </summary>
    public static int SmoothOf(int seconds) => Math.Clamp((int)Math.Round(seconds / 90.0), 2, 4);
    /// <summary>A live fight's graphs are drawn again at most this often (the data moves a second at a time anyway).</summary>
    public const int LiveFrameMs = 250;
    /// <summary>For the options test: the charts drawn since the start (each base layer's pass).</summary>
    internal static int Draws;
    /// <summary>For the options test's CPU measure: the time spent working the graphs out and drawing them, in ms.</summary>
    internal static double DataMs, DrawMs;

    // ================================================================ the data

    public sealed class Line { public long Id; public string Name = ""; public Color Color; public bool You; public double[] Raw = Array.Empty<double>(); public double[] S = Array.Empty<double>(); }

    /// <summary>What the three graphs are drawn from, worked out once a draw from the fight's view.</summary>
    public sealed class Set
    {
        /// <summary>The seconds drawn: a live fight's whole seconds (the one under way is still filling), a kept one's all.</summary>
        public int N;
        public bool Live;
        public string Outcome = "";
        public List<Line> Members = new();
        public double[]? HpLeft;
        public List<(double At, string Name, long Id)> Deaths = new();
        public List<Line> Healers = new();
        public double[]? Taken;
        public List<Line> Takers = new();
        public string Sig = "";
        /// <summary>The smoothing (seconds each side) and, live, the boss's HP now (the ring's figure; the line stops at the last whole second).</summary>
        public int K = 2;
        /// <summary>The fight's time as the hero says it (a kill's label matches its clock).</summary>
        public string Clock = "";
        public double? HpNow;
        public bool HasStack => Members.Count > 1;
        public bool Has(string kind) => N >= 2 && kind switch { Dps => Members.Count > 0, Hp => HpLeft != null || HasStack, Heal => Healers.Count > 0 || Taken != null, _ => false };
    }

    /// <summary>Your line's gold: the window's on a dark look, a deeper one on Daylight so it keeps its contrast (as GoldInk).</summary>
    static Color YouColor => Dark ? Gold : Color.FromRgb(0xD0, 0x86, 0x00);

    /// <summary>
    /// A Gaussian rolling average (sigma <paramref name="k"/> seconds, out to three sigma, weighed again at the ends): a skill's
    /// steady rhythm (a hit every 3 s) smooths flat instead of drawing as a comb, as a plain window of 5 or 7 s would.
    /// </summary>
    public static double[] Gauss(IReadOnlyList<double> s, int k)
    {
        int r = 3 * k;
        var w = new double[r + 1];
        for (int j = 0; j <= r; j++) w[j] = Math.Exp(-0.5 * j * j / (double)(k * k));
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
        s.K = SmoothOf(n);
        s.Clock = BossParser.Clock(e.Duration(v.Now));
        if (live) s.HpNow = e.BossHpLeft;
        Line Of(MemberView m, IReadOnlyList<long>? l) { var raw = Pad(l, n); return new Line { Id = m.F.Id, Name = ParserRows.NameOf(m.F), Color = m.You ? YouColor : ClassArt.ColorOf(m.F.Class), You = m.You, Raw = raw, S = Gauss(raw, s.K) }; }
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
            s.Taken = Gauss(sum, s.K);
        }
        s.Sig = string.Join("|", n, s.Members.Sum(m => m.Raw.Sum()), s.Members.Count, s.HpLeft?[^1] ?? -1, s.Healers.Sum(h => h.Raw.Sum()), s.Taken?.Sum() ?? -1, s.Deaths.Count, s.Outcome);
        return s;
    }

    // ================================================================ the colours a chart is drawn in (the window's look when built)

    public sealed class Pal
    {
        public Brush Ink = null!, Muted = null!, Faint = null!, Grid = null!, Box = null!, BoxEdge = null!, Back = null!;
        public Color InkC;
        public Typeface Semi = null!, Bold = null!;
        public static Pal Now()
        {
            bool dark = Dark;
            var ink = K("Ink");
            return new Pal
            {
                Ink = ink, Muted = K("Muted"), Faint = K("Faint"),
                InkC = (ink as SolidColorBrush)?.Color ?? (dark ? White : Black),
                Grid = C(dark ? White : Black, dark ? 0.08 : 0.09),
                Box = C(dark ? Color.FromRgb(0x10, 0x13, 0x1B) : Color.FromRgb(0xFF, 0xFF, 0xFF), 0.97),
                BoxEdge = C(dark ? White : Black, dark ? 0.16 : 0.14),
                Back = C(dark ? Color.FromRgb(0x0C, 0x0E, 0x14) : White, dark ? 0.7 : 0.78),
                Semi = new Typeface(PK.Face, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
                Bold = new Typeface(PK.Face, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
            };
        }
    }

    // ================================================================ one chart

    /// <summary>
    /// One graph, drawn in one pass: the axes, the series as frozen geometry, the labels as formatted text. The hover is a
    /// layer of its own over it, so a mouse move never draws the series again.
    /// </summary>
    public sealed class Chart : FrameworkElement
    {
        public readonly string Kind;
        readonly Pal _p;
        readonly DrawingVisual _hover = new();
        Set? _set;
        Rect _plot;
        double _yMax, _xSpan;
        int? _hoverAt; bool _hoverBox;
        /// <summary>The panel's crosshair: a second hovered here (null: none), so the other charts mark it too.</summary>
        public Action<int?>? Hovered;

        public Chart(string kind, Pal p)
        {
            Kind = kind; _p = p;
            AddVisualChild(_hover);
            ClipToBounds = true; Cursor = Cursors.Cross; SnapsToDevicePixels = true;
            AutomationProperties.SetName(this, kind switch { Dps => "DPS over the fight graph", Hp => "Boss HP and group DPS graph", _ => "Healing and damage taken graph" });
            MouseMove += (_, ev) => { var at = SecondAt(ev.GetPosition(this).X); Hovered?.Invoke(at); ShowHover(at, true); };
            MouseLeave += (_, _) => { Hovered?.Invoke(null); ShowHover(null, false); };
        }

        protected override int VisualChildrenCount => 1;
        protected override Visual GetVisualChild(int index) => _hover;

        public Set? Data => _set;
        public void SetData(Set s) { _set = s; InvalidateVisual(); }

        int? SecondAt(double x)
        {
            if (_set is not { N: >= 2 } s || _plot.Width <= 0) return null;
            return (int)Math.Clamp(Math.Round((x - _plot.Left) / _plot.Width * _xSpan), 0, s.N - 1);
        }

        double X(double t) => _plot.Left + t / _xSpan * _plot.Width;
        double Y(double v) => _plot.Bottom - Math.Clamp(v / _yMax, 0, 1.04) * _plot.Height;
        double YHp(double hp) => _plot.Bottom - Math.Clamp(hp, 0, 1) * _plot.Height;

        FormattedText Ft(string s, double size, Brush b, bool bold = false) =>
            new(s, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, bold ? _p.Bold : _p.Semi, size, b, VisualTreeHelper.GetDpi(this).PixelsPerDip);

        static Pen P(Brush b, double w, bool dash = false)
        {
            var p = new Pen(b, w) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            if (dash) p.DashStyle = new DashStyle(new double[] { 2, 3 }, 0);
            p.Freeze();
            return p;
        }

        /// <summary>The chart as last drawn (frozen), and what for: WPF renders a chart again whenever the page around it is laid out anew (a live fight's page is, on every hit), and that only replays this.</summary>
        DrawingGroup? _cache;
        (Set? Set, Size Size, double Dpi) _cacheFor;

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
            _xSpan = Math.Max(1, s.N - 1);
            bool hpAxis = Kind == Hp && s.HpLeft != null && s.HasStack;
            _plot = new Rect(new Point(42, 8), new Point(size.Width - (hpAxis ? 40 : 12), size.Height - 20));
            switch (Kind)
            {
                case Dps: DrawDps(dc, s); break;
                case Hp: DrawHp(dc, s); break;
                default: DrawHeal(dc, s); break;
            }
        }

        // ---- the frame: the value grid on the left, the time along the foot

        void Grid(DrawingContext dc, double max, Func<double, string>? label = null)
        {
            _yMax = Nice(Math.Max(1, max * 1.08));
            var thin = P(_p.Grid, 1, dash: true);
            for (int k = 0; k <= 2; k++)
            {
                double v = _yMax * k / 2, y = Math.Round(Y(v)) + 0.5;
                dc.DrawLine(k == 0 ? P(_p.Grid, 1) : thin, new Point(_plot.Left, y), new Point(_plot.Right, y));
                var t = Ft(label?.Invoke(v) ?? Amounts.Short(v), 10, _p.Faint);
                dc.DrawText(t, new Point(_plot.Left - 6 - t.Width, y - t.Height / 2));
            }
            TimeAxis(dc);
        }

        void TimeAxis(DrawingContext dc)
        {
            double perPx = _xSpan / Math.Max(1, _plot.Width);
            double step = new[] { 5.0, 10, 15, 20, 30, 60, 90, 120, 180, 300, 600 }.FirstOrDefault(st => st / perPx >= 64, 900);
            for (double t = 0; t <= _xSpan + 0.01; t += step)
            {
                var ft = Ft(Clock(t), 10, _p.Faint);
                double x = Math.Clamp(X(t) - ft.Width / 2, 0, RenderSize.Width - ft.Width);
                dc.DrawText(ft, new Point(x, _plot.Bottom + 4));
            }
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
                if (_plot.Width / Math.Max(1, s.Count) < 3) ctx.PolyLineTo(pts.Skip(1).ToList(), true, true);
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

        /// <summary>The band between two series (a stacked part, or a series and the floor).</summary>
        StreamGeometry Band(IReadOnlyList<double> top, IReadOnlyList<double>? under, Func<double, double> y)
        {
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(new Point(X(0), y(top[0])), true, true);
                var pts = new List<Point>(top.Count * 2);
                for (int i = 1; i < top.Count; i++) pts.Add(new Point(X(i), y(top[i])));
                for (int i = top.Count - 1; i >= 0; i--) pts.Add(new Point(X(i), under == null ? _plot.Bottom : y(under[i])));
                ctx.PolyLineTo(pts, false, true);
            }
            g.Freeze();
            return g;
        }

        void Series(DrawingContext dc, Line l, double thick)
        {
            var g = Curve(l.S, Y);
            if (l.You) dc.DrawGeometry(null, P(C(l.Color, 0.22), thick + 5), g);   // yours glows (a wide soft pen under it, no effect)
            dc.DrawGeometry(null, P(C(l.Color, l.You ? 1 : 0.92), thick), g);
            if (_set!.Live) dc.DrawEllipse(C(l.Color), null, new Point(X(l.S.Length - 1), Y(l.S[^1])), l.You ? 3.4 : 2.6, l.You ? 3.4 : 2.6);   // the line's head while it grows
        }

        void DrawDps(DrawingContext dc, Set s)
        {
            Grid(dc, s.Members.Max(m => m.S.Max()));
            foreach (var l in s.Members.Where(m => !m.You).Reverse()) Series(dc, l, 1.7);
            foreach (var l in s.Members.Where(m => m.You)) Series(dc, l, 2.8);
            // a death: a coral mark on the member's line at its second; one label for deaths close together (the name when
            // alone, "4 deaths" for a wipe's last seconds: the hover names them)
            var coral = C(Coral); var ring = P(C(Dark ? Black : White, 0.85), 1.5); var cross = P(Brushes.White, 1.4);
            var marks = new List<(Point C, string Name)>();
            foreach (var (at, name, id) in s.Deaths.OrderBy(d => d.At))
            {
                var l = s.Members.FirstOrDefault(m => m.Id == id);
                int i = (int)Math.Clamp(Math.Round(at), 0, s.N - 1);
                var c = new Point(X(Math.Min(at, _xSpan)), l != null ? Y(l.S[i]) : _plot.Bottom - 6);
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
                Label(dc, t, new Point(Math.Max(_plot.Left, tx), Math.Clamp(y - t.Height / 2, _plot.Top - 4, _plot.Bottom - t.Height)));
                a = b + 1;
            }
        }

        /// <summary>Words on the chart over a soft backing, so the lines under them never cross them.</summary>
        void Label(DrawingContext dc, FormattedText t, Point at)
        {
            dc.DrawRoundedRectangle(_p.Back, null, new Rect(at.X - 3, at.Y - 0.5, t.Width + 6, t.Height + 1), 4, 4);
            dc.DrawText(t, at);
        }

        void DrawHp(DrawingContext dc, Set s)
        {
            Func<double, double> hpY = YHp;
            if (s.HasStack)
            {
                // the members' damage stacked: the group's DPS is the top edge, each band a member's part (the top dealer at the foot)
                var cum = new List<double[]>();
                var run = new double[s.N];
                foreach (var m in s.Members) { run = run.Zip(m.S, (a, b) => a + b).ToArray(); cum.Add(run); }
                Grid(dc, cum[^1].Max());
                for (int k = cum.Count - 1; k >= 0; k--)
                {
                    var l = s.Members[k];
                    dc.DrawGeometry(C(l.Color, l.You ? 0.62 : 0.42), null, Band(cum[k], k > 0 ? cum[k - 1] : null, Y));
                }
                dc.DrawGeometry(null, P(C(_p.InkC, 0.55), 1.2), Curve(cum[^1], Y));
            }
            else
            {
                _yMax = 1;
                var thin = P(_p.Grid, 1, dash: true);
                foreach (var v in new[] { 0.0, 0.5, 1.0 })
                {
                    double y = Math.Round(YHp(v)) + 0.5;
                    dc.DrawLine(v == 0 ? P(_p.Grid, 1) : thin, new Point(_plot.Left, y), new Point(_plot.Right, y));
                    var t = Ft(Pct(v, 0), 10, _p.Faint);
                    dc.DrawText(t, new Point(_plot.Left - 6 - t.Width, y - t.Height / 2));
                }
                TimeAxis(dc);
            }
            if (s.HpLeft is not { } hp) return;
            // the thirds (Steven, 2026-11-16: "little tick markers on every 1/3 health of every boss"): a guide at each, and the
            // moment the boss crossed it
            var guide = P(C(_p.InkC, 0.22), 1, dash: true);
            foreach (var (th, word) in new[] { (2 / 3.0, "⅔"), (1 / 3.0, "⅓") })
            {
                double y = Math.Round(YHp(th)) + 0.5;
                dc.DrawLine(guide, new Point(_plot.Left, y), new Point(_plot.Right, y));
                if (s.HasStack) { var t = Ft(word, 10.5, _p.Muted, bold: true); dc.DrawText(t, new Point(_plot.Right + 6, y - t.Height / 2)); }
            }
            if (s.HasStack)
                foreach (var (v, word) in new[] { (1.0, "100%"), (0.0, "0%") }) { var t = Ft(word, 10, _p.Faint); dc.DrawText(t, new Point(_plot.Right + 6, YHp(v) - t.Height / 2)); }
            if (!s.HasStack)
            {
                // a solo fight: the HP's area lit under its line
                var fill = new LinearGradientBrush(C(Gold, 0.30).Color, C(Gold, 0.02).Color, 90); fill.Freeze();
                dc.DrawGeometry(fill, null, Band(hp, null, hpY));
            }
            var hpPen = P(C(s.HasStack ? _p.InkC : Gold), 2.2);
            var hpG = new StreamGeometry();
            using (var ctx = hpG.Open())
            {
                ctx.BeginFigure(new Point(X(0), YHp(hp[0])), false, false);
                ctx.PolyLineTo(Enumerable.Range(1, hp.Length - 1).Select(i => new Point(X(i), YHp(hp[i]))).ToList(), true, true);
            }
            hpG.Freeze();
            dc.DrawGeometry(null, hpPen, hpG);
            foreach (var th in new[] { 2 / 3.0, 1 / 3.0 })
            {
                int i = Array.FindIndex(hp, x => x <= th);
                if (i <= 0 || hp[0] <= th) continue;
                var c = new Point(X(i), YHp(th));
                dc.DrawEllipse(K("Card") is SolidColorBrush cb ? cb : Brushes.Black, P(C(_p.InkC), 1.6), c, 3.6, 3.6);
                var t = Ft(Clock(i), 10, _p.Muted, bold: true);
                double tx = c.X - t.Width / 2;
                Label(dc, t, new Point(Math.Clamp(tx, _plot.Left, _plot.Right - t.Width), Math.Max(_plot.Top - 6, c.Y - 6 - t.Height)));
            }
            // the end: the kill (mint) or the wipe and the HP it left (coral); live, the HP now at the line's head
            double xe = X(s.N - 1);
            if (s.Outcome is "kill" or "wipe")
            {
                bool kill = s.Outcome == "kill";
                var tone = C(kill ? (Dark ? Mint : Color.FromRgb(0x12, 0x96, 0x58)) : Coral);
                dc.DrawLine(P(tone, 1.6), new Point(xe, _plot.Top), new Point(xe, _plot.Bottom));
                var t = Ft(kill ? L.F("Killed at {0}", s.Clock) : L.F("Wiped · {0} HP left", Pct(hp[^1], 0)), 10.5, tone, bold: true);
                Label(dc, t, new Point(xe - 6 - t.Width, _plot.Top));
                dc.DrawEllipse(tone, null, new Point(xe, YHp(hp[^1])), 4, 4);
            }
            else if (s.Live)
            {
                var head = new Point(xe, YHp(hp[^1]));
                dc.DrawEllipse(C(Gold, 0.25), null, head, 7, 7);
                dc.DrawEllipse(C(Gold), null, head, 3.6, 3.6);
                var t = Ft(Pct(s.HpNow ?? hp[^1], 0), 11, GoldInk, bold: true);
                Label(dc, t, new Point(Math.Max(_plot.Left, xe - 8 - t.Width), Math.Max(_plot.Top, head.Y - t.Height - 4)));
            }
        }

        void DrawHeal(DrawingContext dc, Set s)
        {
            double max = Math.Max(s.Healers.Select(h => h.S.Max()).DefaultIfEmpty(0).Max(), s.Taken?.Max() ?? 0);
            Grid(dc, max * (Legended ? 1.22 : 1));   // the legend's room at the top
            if (s.Taken is { } taken)
            {
                dc.DrawGeometry(C(Coral, 0.16), null, Band(taken, null, Y));
                dc.DrawGeometry(null, P(C(Coral, 0.8), 1.4), Curve(taken, Y));
            }
            foreach (var l in s.Healers.Where(h => !h.You).Reverse()) Series(dc, l, 2);
            foreach (var l in s.Healers.Where(h => h.You)) Series(dc, l, 2.6);
            if (!Legended) return;
            // who each line is, along the top (the card's head has no room beside its title and Copy picture)
            double x = _plot.Left + 6;
            var keys = s.Healers.Select(h => (h.Color, h.Name, Area: false)).ToList();
            if (s.Taken != null) keys.Add((Coral, L.T("damage taken"), true));
            foreach (var (c, name, area) in keys)
            {
                var t = Ft(name, 10.5, _p.Muted, bold: true);
                if (x + 16 + t.Width > _plot.Right) break;
                if (area) dc.DrawRoundedRectangle(C(c, 0.35), P(C(c), 1), new Rect(x, _plot.Top + 2, 10, 7), 1.5, 1.5);
                else dc.DrawRoundedRectangle(C(c), null, new Rect(x, _plot.Top + 4, 11, 3), 1.5, 1.5);
                dc.DrawText(t, new Point(x + 15, _plot.Top + 5.5 - t.Height / 2));
                x += 15 + t.Width + 12;
            }
        }

        /// <summary>The heal graph draws its own legend along its top (on the page; a picture has it in the head).</summary>
        public bool Legended;

        // ---- the hover: a crosshair at the second, and on the chart under the mouse every line's value then

        public void ShowHover(int? at, bool box)
        {
            _hoverAt = at; _hoverBox = box;
            using var dc = _hover.RenderOpen();
            if (at is not int i || _set is not { } s || !s.Has(Kind) || _plot.Width <= 0) return;
            double x = Math.Round(X(i)) + 0.5;
            dc.DrawLine(P(C(_p.InkC, 0.35), 1), new Point(x, _plot.Top), new Point(x, _plot.Bottom));
            if (!box) return;
            var rows = HoverRows(i);
            var title = Ft(L.F(Kind == Heal ? "{0} · HP a second, {1} s average" : "{0} · {1} s average", Clock(i), 2 * s.K + 1), 10.5, _p.Muted, bold: true);
            var cells = rows.Select(r => (r.Dot, Name: Ft(r.Name, 11.5, _p.Ink), Value: Ft(r.Value, 11.5, r.Tone ?? _p.Ink, bold: true))).ToList();
            // as many rows as the chart is tall, then a column more beside them (the box never leaves the chart)
            const double rowH = 16;
            int fit = Math.Max(1, (int)((RenderSize.Height - 4 - (title.Height + 15)) / rowH));
            int cols = Math.Max(1, (int)Math.Ceiling(cells.Count / (double)fit)), per = (int)Math.Ceiling(cells.Count / (double)cols);
            double colW = cells.Select(c => (c.Dot != null ? 14 : 0) + c.Name.Width + 14 + c.Value.Width).DefaultIfEmpty(0).Max();
            double w = Math.Max(title.Width, cols * colW + (cols - 1) * 16) + 20;
            double h = 9 + title.Height + 4 + per * rowH + 4;
            double bx = x + 12 + w > RenderSize.Width ? x - 12 - w : x + 12;
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
            if (_set is not { } s) return rows;
            switch (Kind)
            {
                case Dps:
                    foreach (var l in s.Members.OrderByDescending(m => m.S[i])) rows.Add((C(l.Color), l.Name, Int(l.S[i]), l.You ? GoldInk : null));
                    foreach (var d in s.Deaths.Where(d => Math.Abs(d.At - i) <= 1)) rows.Add((C(Coral), L.F("{0} died", d.Name), Clock(d.At), C(Coral)));
                    break;
                case Hp:
                    if (s.HpLeft != null) rows.Add((C(s.HasStack ? _p.InkC : Gold), L.T("Boss HP"), Pct(s.HpLeft[i], 0), null));
                    if (s.HasStack)
                    {
                        double sum = s.Members.Sum(m => m.S[i]);
                        rows.Add((null, L.T("Group DPS"), Int(sum), null));
                        foreach (var l in s.Members.OrderByDescending(m => m.S[i])) rows.Add((C(l.Color), l.Name, sum > 0 ? Pct(l.S[i] / sum, 0) : "–", l.You ? GoldInk : _p.Muted));
                    }
                    break;
                default:
                    foreach (var l in s.Healers.OrderByDescending(h => h.S[i])) rows.Add((C(l.Color), l.Name, Int(l.S[i]), l.You ? GoldInk : null));
                    if (s.Taken is { } t)
                    {
                        rows.Add((C(Coral), L.T("Damage taken"), Int(t[i]), C(Coral)));
                        foreach (var l in s.Takers.OrderByDescending(x => x.S[i]).Take(3).Where(x => x.S[i] > 0)) rows.Add((null, "  " + l.Name, Int(l.S[i]), _p.Muted));
                    }
                    break;
            }
            return rows;
        }
    }

    // ================================================================ the cards

    /// <summary>A graph's title as its data says it (only the parts it has). <paramref name="page"/>: the page's short DPS title (its legend shares the line).</summary>
    public static string Title(string kind, Set s, bool page = false) => kind switch
    {
        Dps => page ? "DPS" : L.T("DPS over the fight"),
        Hp => s.HpLeft != null && s.HasStack ? L.T("Boss HP and group DPS") : s.HpLeft != null ? L.T("Boss HP") : L.T("Group DPS"),
        _ => s.Healers.Count > 0 && s.Taken != null ? L.T("Healing and damage taken") : s.Healers.Count > 0 ? L.T("Healing") : L.T("Damage taken"),
    };

    static Border Swatch(Color c, bool area = false) => new()
    {
        Width = area ? 10 : 11, Height = area ? 8 : 3, CornerRadius = new CornerRadius(area ? 2 : 1.5), Background = C(c, area ? 0.5 : 1),
        BorderBrush = area ? C(c) : null, BorderThickness = new Thickness(area ? 1 : 0), Margin = new Thickness(9, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>The legend: who each line is (the DPS graph's members; the heal graph's healers and the damage taken).</summary>
    static FrameworkElement Legend(string kind, Set s)
    {
        var wp = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        void Add(Color c, string name, bool lit, bool area = false)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 1, 0, 1) };
            sp.Children.Add(Swatch(c, area));
            sp.Children.Add(T(name, 10.5, lit ? GoldInk : K("Muted"), FontWeights.SemiBold, trim: false));
            wp.Children.Add(sp);
        }
        if (kind == Dps) foreach (var l in s.Members) Add(l.Color, l.Name, l.You);
        if (kind == Heal)
        {
            foreach (var l in s.Healers) Add(l.Color, l.Name, l.You);
            if (s.Taken != null) Add(Coral, L.T("damage taken"), false, area: true);
        }
        return wp;
    }

    /// <summary>A graph's head: the title, the legend, and (on the page) its Copy picture.</summary>
    static Grid Head(TextBlock title, FrameworkElement? legend, FrameworkElement? copy)
    {
        var g = Cols(0, -1, 0);
        g.Children.Add(title);
        if (legend != null) g.Children.Add(Col(M(legend, 10, 0, copy != null ? 10 : 0, 0), 1));
        if (copy != null) g.Children.Add(Col(copy, 2));
        return g;
    }

    static TextBlock TitleText(string text, double size = 13) => T(text, size, K("Ink"), FontWeights.SemiBold, trim: false);

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
    /// The Graphs tab's view of one fight, one card: the DPS graph across the top (always shown), the boss's HP and the healing
    /// side by side under a hairline (each only when the record has it; one alone takes the whole row). Kept by the window while
    /// the same fight shows, so the page's many redraws a second only lay it out again; its charts are drawn when their data
    /// moves, a live fight's at most every <see cref="LiveFrameMs"/>.
    /// </summary>
    public sealed class GraphsPanel : Border
    {
        public readonly string Key;
        readonly Pal _p = Pal.Now();
        readonly Dictionary<string, Chart> _charts = new();
        readonly Dictionary<string, FrameworkElement> _parts = new();
        readonly Dictionary<string, TextBlock> _titles = new();
        readonly ContentControl _legend = new() { Focusable = false, IsTabStop = false };
        string _legendSig = "";
        readonly Grid _row = Cols(-1, 21, -1);
        readonly Border _rule;
        (FightView V, bool Live)? _want;
        long _lastDraw;
        DispatcherTimer? _timer;
        Set? _set;
        double _dpsH = 168, _smallH = 104, _room = double.NaN;
        /// <summary>For the options test: the times this panel worked its graphs out again.</summary>
        public int Redraws;

        public GraphsPanel(string key, Action<string> copy)
        {
            Key = key; Tag = "parser-graphs";
            var body = new StackPanel();
            foreach (var kind in Kinds)
            {
                var chart = new Chart(kind, _p) { Height = kind == Dps ? _dpsH : _smallH, Legended = kind == Heal };
                chart.Hovered = at => { foreach (var c in _charts.Values) if (c != chart) c.ShowHover(at, false); };
                _charts[kind] = chart;
                var title = TitleText("", kind == Dps ? 13 : 12.5); _titles[kind] = title;
                var part = new StackPanel { Tag = "graph-card-" + kind };
                part.Children.Add(M(Head(title, kind == Dps ? _legend : null, CopyButton(kind, copy)), 0, 0, 0, 4));
                part.Children.Add(chart);
                _parts[kind] = part;
            }
            body.Children.Add(_parts[Dps]);
            // the row under it: a hairline across, the two graphs either side of a hairline down
            _rule = new Border { Height = 1, Margin = new Thickness(0, 9, 0, 8), Background = K("CardHair") };
            body.Children.Add(_rule);
            _row.Children.Add(_parts[Hp]);
            _row.Children.Add(Col(new Border { Width = 1, Background = K("CardHair"), HorizontalAlignment = HorizontalAlignment.Center, Tag = "graph-split" }, 1));
            _row.Children.Add(Col(_parts[Heal], 2));
            body.Children.Add(_row);
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
                var title = Title(kind, s, page: true);
                if (_titles[kind].Text != title) _titles[kind].Text = title;
                _charts[kind].SetData(s);
            }
            // the legend again only when who is in it changes
            string sig = string.Join(",", s.Members.Select(m => m.Id + m.Name));
            if (sig != _legendSig) { _legendSig = sig; _legend.Content = Legend(Dps, s); }
            // the second row: both side by side, one alone across it, none: gone
            bool hp = s.Has(Hp), heal = s.Has(Heal);
            _row.Visibility = _rule.Visibility = hp || heal ? Visibility.Visible : Visibility.Collapsed;
            _row.Children[1].Visibility = hp && heal ? Visibility.Visible : Visibility.Collapsed;
            Grid.SetColumn(_parts[Heal], hp ? 2 : 0);
            Grid.SetColumnSpan(_parts[Hp], heal ? 1 : 3);
            Grid.SetColumnSpan(_parts[Heal], hp ? 1 : 3);
            Fit(_room);
        }

        /// <summary>
        /// The charts' heights for the room the window leaves under the hero (Steven: pages fit without scrolling): the DPS chart
        /// takes most, the row under it the rest, each between a floor that still reads and a ceiling that doesn't sprawl.
        /// </summary>
        public void Fit(double room)
        {
            _room = room;
            bool row = _row.Visibility == Visibility.Visible;
            double dps = 168, small = 104;
            if (!double.IsNaN(room))
            {
                double chrome = 19 + 26 + (row ? 18 + 26 : 0);   // the card's padding and edge, the heads, the hairline's room
                double avail = room - chrome;
                if (row) { dps = Math.Clamp(avail * 0.58, 112, 270); small = Math.Clamp(avail - dps, 74, 180); }
                else dps = Math.Clamp(avail, 112, 340);
            }
            if (Math.Abs(dps - _dpsH) > 1) { _dpsH = dps; _charts[Dps].Height = dps; }
            if (Math.Abs(small - _smallH) > 1) { _smallH = small; _charts[Hp].Height = small; _charts[Heal].Height = small; }
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
        var chart = new Chart(kind, p) { Height = kind == Dps ? 250 : 190 };
        chart.SetData(s);
        var body = new StackPanel();
        var sub = s.Live ? L.F("{0} in", Clock(v.E.Duration(v.Now).TotalSeconds)) : "";
        var title = TitleText(Title(kind, s));
        body.Children.Add(M(Head(title, Legend(kind, s), sub.Length > 0 ? T(sub, 11.5, K("Muted"), FontWeights.SemiBold) : null), 0, 0, 0, 8));
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
