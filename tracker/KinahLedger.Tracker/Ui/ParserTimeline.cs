using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using KinahLedger.Parser;
using static KinahLedger.Ui.PK;

namespace KinahLedger.Ui;

/// <summary>
/// The Parser page's Timeline tab (Parser/PullTimeline.cs holds the data): a kept boss pull lane by lane, as the best parsers
/// of other games draw a pull. One card, one chart drawn in one pass:
/// <list type="bullet">
/// <item>the time ruler; the boss's HP with its 75, 50 and 25% moments;</item>
/// <item>the group's damage a second with its spikes, and your biggest hits;</item>
/// <item>your abilities, biggest first: each a band of its damage a second and a mark for each cast as it landed, coloured by
/// ability; the first six shown, the rest one folded lane (Show all opens it);</item>
/// <item>the group: each member's damage a second and their deaths, a line across the chart at each.</item>
/// </list>
/// The wheel zooms round the pointer, a drag pans, a double-click (or Fit) shows the whole pull. The hover names the exact
/// event under the pointer: a cast's time, damage and hits; a hit; a spike; a death with its recap (the last hits taken, the
/// killing blow last); a phase; the HP or a lane's damage at that second. Under the chart, each death in one line, the recap
/// opened by a click. It fits the page: the lanes fold, nothing scrolls.
/// </summary>
static class ParserTimeline
{
    /// <summary>The lanes' colours: the abilities, biggest first (the folded lane is grey).</summary>
    static readonly Color[] Hues =
    {
        Color.FromRgb(0x5A, 0xB8, 0xFF), Color.FromRgb(0xF5, 0xB9, 0x42), Color.FromRgb(0x3D, 0xD6, 0x8C),
        Color.FromRgb(0xC6, 0x8C, 0xFF), Color.FromRgb(0xFF, 0x8A, 0x5B), Color.FromRgb(0xFF, 0x6F, 0xB1),
        Color.FromRgb(0x7F, 0xE0, 0xE0), Color.FromRgb(0xE8, 0xD0, 0x6A), Color.FromRgb(0xA8, 0xB8, 0xFF),
        Color.FromRgb(0xFF, 0xA8, 0x8C), Color.FromRgb(0x9C, 0xE0, 0x8C), Color.FromRgb(0xE0, 0x9C, 0xD8),
    };
    public static Color HueOf(int i) => Hues[i % Hues.Length];
    static Brush Ink => K("Ink"); static Brush Muted => K("Muted"); static Brush Faint => K("Faint"); static Brush Text2 => K("Text2"); static Brush Hair => K("CardHair");

    /// <summary>The window's picks while one pull shows: its zoom (seconds from and to), every ability or the first six, the death opened.</summary>
    static string _for = "";
    static double _from, _to;
    static bool _all;
    static int _deathOpen = -1;
    /// <summary>For the tests: the chart last built for the page.</summary>
    internal static Chart? Last;
    /// <summary>For the renders and tests: the death whose recap is open (-1 none), and every ability shown.</summary>
    internal static int DeathOpenForTest { get => _deathOpen; set => _deathOpen = value; }
    internal static bool AllForTest { get => _all; set => _all = value; }

    static void Hold(string id) { if (_for == id) return; _for = id; _from = 0; _to = 0; _all = false; _deathOpen = -1; }

    /// <summary>The tab's card for a pull (null when the pull has no timeline). <paramref name="share"/>: drawn for a picture (the whole pull, every lane folded, no buttons).</summary>
    public static Border? View(FightView v, CommandCtx? ctx, FrameworkElement? copy, double? width = null)
    {
        if (PullTimeline.Of(v.E) is not { } t) return null;
        bool share = ctx == null || ctx.Share;
        if (!share) Hold(v.E.Id);
        bool all = !share && _all;
        var body = new StackPanel { Tag = "parser-timeline" };

        // the head: the title and what it shows; on the right the zoom and the copy buttons
        var head = Cols(-1, 0);
        var title = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        title.Children.Add(T(L.T("Timeline"), 13, Ink, FontWeights.SemiBold));
        string about = share ? L.F("{0} · your casts, hits and the group's deaths", Clock(t.Length)) : L.T("Scroll to zoom, drag to move, double-click for the whole pull");
        title.Children.Add(M(T(about, 11, Faint, FontWeights.Medium), 10, 1, 0, 0));
        head.Children.Add(title);
        var chart = new Chart(t, all, ParserGraphs.Pal.Now()) { Margin = new Thickness(0, 8, 0, 0) };
        if (!share)
        {
            Last = chart;
            if (_to > _from) chart.SetSpan(_from, _to, notify: false);
            chart.SpanChanged = (a, b) => { _from = a; _to = b; };
        }
        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (!share)
        {
            right.Children.Add(ZoomButton("−", L.T("Zoom out"), "parser-timeline-out", () => chart.ZoomBy(1.6, null)));
            right.Children.Add(ZoomButton("+", L.T("Zoom in"), "parser-timeline-in", () => chart.ZoomBy(1 / 1.6, null)));
            right.Children.Add(ZoomButton(L.T("Fit"), L.T("The whole pull"), "parser-timeline-fit", () => chart.SetSpan(0, chart.Length, notify: true)));
        }
        if (copy != null) { copy.Margin = new Thickness(10, -4, 0, -4); copy.VerticalAlignment = VerticalAlignment.Center; right.Children.Add(copy); }
        head.Children.Add(Col(right, 1));
        body.Children.Add(head);
        body.Children.Add(chart);

        // under the chart, one row: the fold (Show all abilities) and each death a chip (when, who; a click opens its recap under
        // the row, one at a time; hovering one marks it on the chart), so the card fits the page whatever happened
        var foot = new WrapPanel { Margin = new Thickness(0, 6, 0, 0), Tag = "parser-timeline-foot" };
        if (!share && t.Folded is { } fold)
        {
            var b = Chip(all ? L.T("Fold the small abilities") : L.F("Show all {0} abilities", t.Abilities.Count), Text2, false, "parser-timeline-fold");
            AutomationProperties.SetName(b, all ? "Fold the abilities" : "Show all abilities");
            Click(b, () => { _all = !_all; ctx!.SetTab(ctx.Tab); });
            foot.Children.Add(b);
        }
        if (t.Deaths.Count > 0)
        {
            foot.Children.Add(M(T(t.Deaths.Count == 1 ? L.T("1 death") : L.F("{0} deaths", t.Deaths.Count), 11, Muted, FontWeights.SemiBold), foot.Children.Count > 0 ? 10 : 0, 3, 6, 0));
            for (int i = 0; i < t.Deaths.Count; i++)
            {
                var d = t.Deaths[i];
                bool open = !share && _deathOpen == i;
                var chip = Chip("", Text2, open, "parser-timeline-death");
                var words = (TextBlock)chip.Child;
                words.Inlines.Add(R("✕ ", 11, C(Coral), FontWeights.Bold));
                words.Inlines.Add(R(NameOf(d.F), 11, Ink, FontWeights.SemiBold));
                words.Inlines.Add(R("  " + Clock(d.At), 11, Text2));
                if (!share)
                {
                    int k = i;
                    chip.ToolTip = NameOf(d.F) + "  ·  " + DeathLine(d);
                    Click(chip, () => { _deathOpen = _deathOpen == k ? -1 : k; ctx!.SetTab(ctx.Tab); });
                    chip.MouseEnter += (_, _) => chart.Mark(d.At);
                    chip.MouseLeave += (_, _) => chart.Mark(null);
                    AutomationProperties.SetName(chip, $"{NameOf(d.F)} died at {Clock(d.At)}");
                }
                foot.Children.Add(chip);
            }
        }
        int bare = t.DeathsCounted - t.Deaths.Count;
        if (bare > 0) foot.Children.Add(M(T(bare == 1 ? L.T("1 death came with no recap, so it isn't on the timeline.") : L.F("{0} deaths came with no recap, so they aren't on the timeline.", bare), 11, Faint, FontWeights.Medium, trim: false), 8, 3, 0, 0));
        if (foot.Children.Count > 0) body.Children.Add(foot);
        // the death opened: its line and its recap
        if (!share && _deathOpen >= 0 && _deathOpen < t.Deaths.Count)
        {
            var d = t.Deaths[_deathOpen];
            var sp = new StackPanel { Tag = "parser-timeline-recap-card" };
            var head2 = T("", 11.5, Text2, FontWeights.Medium, trim: false);
            head2.Inlines.Add(R(NameOf(d.F) + "  ", 11.5, Ink, FontWeights.SemiBold));
            head2.Inlines.Add(R(DeathLine(d), 11.5, Text2));
            sp.Children.Add(head2);
            sp.Children.Add(Recap(d));
            body.Children.Add(new Border { Margin = new Thickness(0, 6, 0, 0), Padding = new Thickness(10, 6, 10, 6), CornerRadius = new CornerRadius(8), Background = C(Coral, 0.07), BorderBrush = C(Coral, 0.35), BorderThickness = new Thickness(1), Child = sp });
        }
        if (width is { } w) body.Width = w;
        return Glass(body, new Thickness(14, 12, 14, 12), 14);
    }

    static string NameOf(Fighter f) => ParserRows.NameOf(f);

    /// <summary>A death in a line: when, the boss's HP then, and what killed them.</summary>
    static string DeathLine(PullTimeline.Death d)
    {
        var parts = new List<string> { Clock(d.At) };
        if (d.BossHpLeft is { } hp) parts.Add(L.F("boss at {0}", Pct(hp, 0)));
        string killer = Who(d.Killer, d.KillerSkill);
        if (killer.Length > 0) parts.Add(L.F("killed by {0}", killer));
        else if (d.Last.LastOrDefault() is { } last && Who(last.Source, last.Skill) is { Length: > 0 } lw) parts.Add(L.F("last hit by {0}", lw));
        return string.Join("  ·  ", parts);
    }

    /// <summary>
    /// Who or what dealt a hit, in words, as the page's other views say it (FightView.Source): the attacker named (the boss, a
    /// member), else its skill's name when the skill list has it, else "". A boss's skill is never named by the list: its ids
    /// are the game's for monsters, and a player skill of the same number would name it wrong.
    /// </summary>
    static string Who(string source, int skill) => source.Length > 0 ? source : skill != 0 && Abilities.Resolve(skill) != null ? Abilities.Name(skill) : "";

    /// <summary>The hit of a recap that killed: the last hit taken of the killing skill (none named: no hit is called the killing blow).</summary>
    internal static int KillingBlow(PullTimeline.Death d) => d.KillerSkill == 0 ? -1 : d.Last.FindLastIndex(h => h.Skill == d.KillerSkill);

    static Border Chip(string text, Brush fg, bool on, string tag) => new()
    {
        CornerRadius = new CornerRadius(7), Padding = new Thickness(8, 1, 8, 2), Margin = new Thickness(0, 2, 6, 2), BorderThickness = new Thickness(1), Tag = tag,
        Background = on ? C(Coral, 0.14) : K("CardQuiet"), BorderBrush = on ? C(Coral, 0.6) : Hair,
        Child = T(text, 11, fg, FontWeights.SemiBold, trim: false),
    };

    static void Click(Border b, Action act)
    {
        b.Cursor = Cursors.Hand; b.Focusable = true;
        b.MouseLeftButtonUp += (_, _) => act();
        b.KeyDown += (_, k) => { if (k.Key is Key.Enter or Key.Space) { k.Handled = true; act(); } };
    }

    /// <summary>
    /// A death's recap: its last hits taken, oldest first, each with its time and amount, the killing blow marked; in two
    /// columns past three hits (down the left, then the right), so an open recap keeps the card on the page.
    /// </summary>
    static FrameworkElement Recap(PullTimeline.Death d)
    {
        var g = new Grid { Margin = new Thickness(0, 4, 0, 0), Tag = "parser-timeline-recap" };
        if (d.Last.Count == 0) { g.Children.Add(T(L.T("No hits taken were seen just before."), 11, Faint)); return g; }
        int cols = d.Last.Count > 3 ? 2 : 1, rows = (int)Math.Ceiling(d.Last.Count / (double)cols);
        for (int c = 0; c < cols; c++)
        {
            if (c > 0) g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        }
        for (int r = 0; r < rows; r++) g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        int kbAt = KillingBlow(d);
        for (int i = 0; i < d.Last.Count; i++)
        {
            var h = d.Last[i];
            bool kb = i == kbAt;
            int r = i % rows, c0 = i / rows * 4;
            g.Children.Add(Row(Col(M(T(ClockTenths(h.At), 11, Faint, tab: true), 0, 0, 10, 0), c0), r));
            string who = Who(h.Source, h.Skill);
            var name = T(who.Length > 0 ? who : L.T("Unnamed hit"), 11, kb ? Ink : Text2, kb ? FontWeights.SemiBold : FontWeights.Medium);
            if (kb) name.Inlines.Add(R("  " + L.T("killing blow"), 10, C(Coral), FontWeights.Bold));
            g.Children.Add(Row(Col(name, c0 + 1), r));
            g.Children.Add(Row(Col(M(T("−" + Int(h.Amount), 11, C(Coral), FontWeights.SemiBold, tab: true), 10, 0, 0, 0), c0 + 2), r));
        }
        return g;
    }

    /// <summary>A time with tenths ("1:23.4"): a death's hits come within a second of each other.</summary>
    public static string ClockTenths(double s)
    {
        s = Math.Max(0, s);
        int m = (int)(s / 60); double r = s - m * 60;
        return m.ToString(CultureInfo.InvariantCulture) + ":" + r.ToString("00.0", CultureInfo.InvariantCulture);
    }

    static Border ZoomButton(string text, string tip, string tag, Action act)
    {
        var b = new Border { CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 1, 8, 2), Margin = new Thickness(4, -3, 0, -3), MinWidth = 24, Cursor = Cursors.Hand, Focusable = true, Tag = tag,
            Background = K("CardQuiet"), BorderBrush = Hair, BorderThickness = new Thickness(1), ToolTip = tip, VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = text, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Text2, HorizontalAlignment = HorizontalAlignment.Center } };
        AutomationProperties.SetName(b, tip);
        b.MouseLeftButtonUp += (_, _) => act();
        b.KeyDown += (_, k) => { if (k.Key is Key.Enter or Key.Space) { k.Handled = true; act(); } };
        b.MouseEnter += (_, _) => b.Background = K("HoverTint");
        b.MouseLeave += (_, _) => b.Background = K("CardQuiet");
        return b;
    }

    // ================================================================ the chart

    /// <summary>One lane of the chart: its label, its kind, and where it is.</summary>
    internal sealed record TRow(string Kind, string Label, string Figure, Color Color, double Top, double Height, object? Data);

    /// <summary>
    /// The timeline itself, drawn in one pass (geometry and text, never an element a mark): the lanes' labels in a column on the
    /// left, the pull across the rest between the span's two times. The hover is a layer of its own.
    /// </summary>
    public sealed class Chart : FrameworkElement
    {
        readonly PullTimeline _t;
        readonly ParserGraphs.Pal _p;
        readonly DrawingVisual _hover = new();
        internal readonly List<TRow> Rows = new();
        public double Length => Math.Max(1, _t.N);
        double _from, _to;
        double? _mark;
        Point? _drag; double _dragFrom, _dragTo;
        public Action<double, double>? SpanChanged;
        const double Gutter = 168, RulerH = 18, Pad = 4;
        /// <summary>The narrowest span the zoom goes to (seconds).</summary>
        public const double MinSpan = 6;
        public double From => _from;
        public double To => _to;
        /// <summary>The pull the chart draws (for the renders and tests).</summary>
        public PullTimeline Data => _t;

        public Chart(PullTimeline t, bool all, ParserGraphs.Pal p)
        {
            _t = t; _p = p; _from = 0; _to = Length;
            AddVisualChild(_hover);
            ClipToBounds = true; SnapsToDevicePixels = true; Cursor = Cursors.Cross; Focusable = false;
            AutomationProperties.SetName(this, "Pull timeline");
            Lay(all);
            Height = Rows.Count > 0 ? Rows[^1].Top + Rows[^1].Height + Pad : 60;
            MouseWheel += (_, ev) => { ev.Handled = true; ZoomBy(ev.Delta > 0 ? 1 / 1.25 : 1.25, ev.GetPosition(this).X); };
            MouseLeftButtonDown += (_, ev) =>
            {
                if (ev.ClickCount == 2) { SetSpan(0, Length, notify: true); return; }
                _drag = ev.GetPosition(this); _dragFrom = _from; _dragTo = _to; CaptureMouse();
            };
            MouseLeftButtonUp += (_, _) => { _drag = null; ReleaseMouseCapture(); };
            MouseMove += (_, ev) =>
            {
                var at = ev.GetPosition(this);
                if (_drag is { } d0 && ev.LeftButton == MouseButtonState.Pressed && PlotW > 0)
                {
                    double dt = (at.X - d0.X) / PlotW * (_dragTo - _dragFrom);
                    double a = _dragFrom - dt, b = _dragTo - dt;
                    if (a < 0) { b -= a; a = 0; }
                    if (b > Length) { a -= b - Length; b = Length; }
                    SetSpan(Math.Max(0, a), b, notify: true);
                }
                Hover(at);
            };
            MouseLeave += (_, _) => { if (_drag == null) Hover(null); };
        }

        protected override int VisualChildrenCount => 1;
        protected override Visual GetVisualChild(int index) => _hover;

        double PlotW => Math.Max(0, RenderSize.Width - Gutter - 8);
        double X(double s) => Gutter + (s - _from) / Math.Max(0.001, _to - _from) * PlotW;
        double SecondAt(double x) => _from + (x - Gutter) / Math.Max(1, PlotW) * (_to - _from);

        /// <summary>The span shown, clamped to the pull (at least <see cref="MinSpan"/>).</summary>
        public void SetSpan(double from, double to, bool notify)
        {
            double len = Length, span = Math.Clamp(to - from, Math.Min(MinSpan, len), len);
            from = Math.Clamp(from, 0, len - span);
            _from = from; _to = from + span;
            InvalidateVisual();
            if (notify) SpanChanged?.Invoke(_from, _to);
        }

        /// <summary>Zooms by a factor round a point (the pointer's x; null: the middle).</summary>
        public void ZoomBy(double k, double? x)
        {
            double at = x is { } px && px >= Gutter ? SecondAt(px) : (_from + _to) / 2;
            double span = (_to - _from) * k, f = (at - _from) / Math.Max(0.001, _to - _from);
            SetSpan(at - f * span, at - f * span + span, notify: true);
        }

        /// <summary>A time marked across the chart (a death line hovered under it); null clears it.</summary>
        public void Mark(double? at) { _mark = at; InvalidateVisual(); }

        // ---- the lanes, top to bottom

        void Lay(bool all)
        {
            double y = RulerH + 2;
            void Add(string kind, string label, string fig, Color c, double h, object? data) { Rows.Add(new TRow(kind, label, fig, c, y, h, data)); y += h + 2; }
            if (_t.Hp != null) Add("hp", L.T("Boss HP"), _t.Hp![^1] > 0 ? Pct(_t.Hp[^1], 0) : "", Mint, 34, null);
            Add("group", L.T("Group damage"), Amounts.Short(_t.Group.Sum()), _p.InkC, 26, null);
            if (_t.BigHits.Count > 0) Add("hits", L.T("Your biggest hits"), Amounts.Short(_t.BigHits.Max(h => h.Amount)), Gold, 18, null);
            y += 4;
            var lanes = all || _t.Folded == null ? _t.Abilities : _t.Abilities.Take(PullTimeline.TopLanes).ToList();
            double laneH = lanes.Count > PullTimeline.TopLanes + 1 ? 13 : 18;   // every ability shown: thinner lanes, so the card still fits
            for (int i = 0; i < lanes.Count; i++) Add("ability", lanes[i].Name, Pct(lanes[i].Share, 1), HueOf(i), laneH, lanes[i]);
            if (!all && _t.Folded is { } f) Add("ability", L.F("{0} smaller abilities", f.Of!.Count), Pct(f.Share, 1), Color.FromRgb(0x8A, 0x93, 0xA6), 18, f);
            y += 4;
            foreach (var m in _t.Members) Add("member", NameOf(m.F), Amounts.Short(m.Damage), m.F.You ? Gold : ClassArt.ColorOf(m.F.Class), 14, m);
        }

        static readonly Dictionary<(Color, double, bool), Pen> Pens = new();
        static Pen P(Color c, double w, bool dash = false)
        {
            if (Pens.TryGetValue((c, w, dash), out var p)) return p;
            p = new Pen(new SolidColorBrush(c), w) { DashStyle = dash ? new DashStyle(new double[] { 2, 3 }, 0) : DashStyles.Solid };
            p.Freeze(); Pens[(c, w, dash)] = p; return p;
        }
        static Color A(Color c, double a) => Color.FromArgb((byte)Math.Round(255 * Math.Clamp(a, 0, 1)), c.R, c.G, c.B);
        /// <summary>A frozen brush a colour, kept: a long pull's bands draw thousands of bars on every zoom.</summary>
        static readonly Dictionary<Color, SolidColorBrush> Brushes0 = new();
        static SolidColorBrush B(Color c)
        {
            if (Brushes0.TryGetValue(c, out var b)) return b;
            if (Brushes0.Count > 2048) Brushes0.Clear();
            b = new SolidColorBrush(c); b.Freeze(); Brushes0[c] = b; return b;
        }

        FormattedText Ft(string s, double size, Brush b, bool bold = false) =>
            new(s, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, bold ? _p.Bold : _p.Semi, size, b, VisualTreeHelper.GetDpi(this).PixelsPerDip);

        /// <summary>The first and last whole seconds in the span.</summary>
        (int A, int B) Seconds() => ((int)Math.Max(0, Math.Floor(_from)), (int)Math.Min(_t.N - 1, Math.Ceiling(_to)));

        protected override void OnRender(DrawingContext dc)
        {
            var size = RenderSize;
            if (size.Width < Gutter + 40) return;
            double right = Gutter + PlotW;
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(size));   // the whole chart takes the pointer
            // the ruler: a time every step the span asks for
            double span = _to - _from;
            double step = new[] { 1, 2, 5, 10, 15, 30, 60, 120, 300 }.First(s => span / s <= Math.Max(3, PlotW / 70.0));
            for (double s = Math.Ceiling(_from / step) * step; s <= _to + 1e-6; s += step)
            {
                double x = Math.Round(X(s)) + 0.5;
                dc.DrawLine(new Pen(_p.Grid, 1), new Point(x, RulerH), new Point(x, size.Height - Pad));
                var ft = Ft(step < 1 ? ClockTenths(s) : Clock(s), 10, _p.Faint);
                double tx = Math.Clamp(x - ft.Width / 2, Gutter, right - ft.Width);
                dc.DrawText(ft, new Point(tx, 2));
            }
            var (a, b) = Seconds();
            double w1 = Math.Max(1, X(1) - X(0));
            foreach (var r in Rows)
            {
                // the label: name on the left, figure on the right of the gutter
                var name = Ft(r.Label, r.Kind == "member" ? 10.5 : 11, r.Kind == "member" ? _p.Muted : _p.Ink);
                var fig = Ft(r.Figure, 10, _p.Faint);
                double ly = r.Top + (r.Height - name.Height) / 2;
                if (r.Kind is "ability" or "member") dc.DrawRoundedRectangle(B(r.Color), null, new Rect(2, r.Top + r.Height / 2 - 4, 8, 8), 2, 2);
                name.MaxTextWidth = Gutter - 18 - fig.Width - 8; name.MaxLineCount = 1; name.Trimming = TextTrimming.CharacterEllipsis;
                dc.DrawText(name, new Point(r.Kind is "ability" or "member" ? 15 : 2, ly));
                dc.DrawText(fig, new Point(Gutter - 8 - fig.Width, r.Top + (r.Height - fig.Height) / 2));
                dc.DrawRectangle(B(A(_p.InkC, _p.Dark ? 0.025 : 0.035)), null, new Rect(Gutter, r.Top, PlotW, r.Height));
                dc.PushClip(new RectangleGeometry(new Rect(Gutter, r.Top, PlotW, r.Height)));
                switch (r.Kind)
                {
                    case "hp" when _t.Hp is { } hp:
                    {
                        // the HP left, a line over a soft fill, full at the start
                        var g = new StreamGeometry();
                        using (var c = g.Open())
                        {
                            double Y(double v) => r.Top + 2 + (1 - v) * (r.Height - 4);
                            c.BeginFigure(new Point(X(0), r.Top + r.Height), true, true);
                            c.LineTo(new Point(X(0), Y(1)), true, false);
                            for (int i = 0; i < hp.Length; i++) c.LineTo(new Point(X(i + 1), Y(hp[i])), true, false);
                            c.LineTo(new Point(X(hp.Length), r.Top + r.Height), true, false);
                        }
                        g.Freeze();
                        dc.DrawGeometry(B(A(Mint, 0.16)), P(A(Mint, 0.9), 1.4), g);
                        foreach (var ph in _t.Phases)
                        {
                            double x = Math.Round(X(ph.At)) + 0.5;
                            dc.DrawLine(P(A(Gold, 0.85), 1, dash: true), new Point(x, r.Top), new Point(x, r.Top + r.Height));
                            var t = Ft($"{Pct(ph.Mark, 0)} {Clock(ph.At)}", 9.5, B(A(Gold, 0.95)), bold: true);
                            dc.DrawRoundedRectangle(_p.Back, null, new Rect(x + 3, r.Top + 1, t.Width + 6, t.Height + 1), 3, 3);
                            dc.DrawText(t, new Point(x + 6, r.Top + 1.5));
                        }
                        break;
                    }
                    case "group":
                    {
                        long max = Math.Max(1, _t.Group.Max());
                        var fill = B(A(_p.InkC, 0.32));
                        for (int i = a; i <= b; i++)
                        {
                            if (_t.Group[i] <= 0) continue;
                            double h = (r.Height - 3) * _t.Group[i] / (double)max;
                            dc.DrawRectangle(fill, null, new Rect(X(i), r.Top + r.Height - h, Math.Max(1, w1 - (w1 > 3 ? 1 : 0)), h));
                        }
                        foreach (var sp in _t.Spikes)
                        {
                            double x = X(sp.Second + 0.5), h = (r.Height - 3) * sp.Damage / (double)max;
                            var tri = Geometry.Parse($"M{x - 4},{r.Top + r.Height - h - 1} L{x + 4},{r.Top + r.Height - h - 1} L{x},{r.Top + r.Height - h + 5} Z");
                            dc.DrawGeometry(B(Gold), null, tri);
                        }
                        break;
                    }
                    case "hits":
                    {
                        long max = Math.Max(1, _t.BigHits.Max(h => h.Amount));
                        foreach (var h in _t.BigHits.OrderBy(h => h.Amount))
                        {
                            double x = X(h.At), s = 3 + 3 * h.Amount / (double)max, cy = r.Top + r.Height / 2;
                            var d = new StreamGeometry();
                            using (var c = d.Open()) { c.BeginFigure(new Point(x, cy - s), true, true); c.LineTo(new Point(x + s, cy), true, false); c.LineTo(new Point(x, cy + s), true, false); c.LineTo(new Point(x - s, cy), true, false); }
                            d.Freeze();
                            dc.DrawGeometry(B(h.Crit == true ? Coral : Gold), P(_p.Under, 1), d);
                        }
                        break;
                    }
                    case "ability" when r.Data is PullTimeline.Lane lane:
                    {
                        // the band: the ability's damage a second, as height and depth; then each cast, a mark the lane's height
                        long max = Math.Max(1, lane.PerSecond.Max());
                        for (int i = a; i <= b; i++)
                        {
                            long v = lane.PerSecond[i];
                            if (v <= 0) continue;
                            double k = v / (double)max, h = 3 + (r.Height - 5) * k;
                            dc.DrawRectangle(B(A(r.Color, 0.20 + 0.35 * k)), null, new Rect(X(i), r.Top + r.Height - h, Math.Max(1, w1 - (w1 > 3 ? 1 : 0)), h));
                        }
                        if (lane.Casts != null)
                        {
                            var pen = P(A(r.Color, 1), w1 > 12 ? 2 : 1.5);
                            var edge = P(A(_p.Under, 0.9), w1 > 12 ? 4 : 3);
                            foreach (var c in lane.Casts)
                            {
                                if (c.At < _from - 1 || c.At > _to + 1) continue;
                                double x = Math.Round(X(c.At)) + 0.5;
                                dc.DrawLine(edge, new Point(x, r.Top + 1), new Point(x, r.Top + r.Height - 1));
                                dc.DrawLine(pen, new Point(x, r.Top + 1), new Point(x, r.Top + r.Height - 1));
                            }
                        }
                        break;
                    }
                    case "member" when r.Data is PullTimeline.MemberLane m:
                    {
                        long max = Math.Max(1, _t.Members.Max(x => x.PerSecond.DefaultIfEmpty(0).Max()));
                        for (int i = a; i <= b; i++)
                        {
                            long v = m.PerSecond[i];
                            if (v <= 0) continue;
                            double k = v / (double)max, h = 2 + (r.Height - 3) * Math.Sqrt(k);
                            dc.DrawRectangle(B(A(r.Color, 0.25 + 0.5 * k)), null, new Rect(X(i), r.Top + r.Height - h, Math.Max(1, w1 - (w1 > 3 ? 1 : 0)), h));
                        }
                        break;
                    }
                }
                dc.Pop();
            }
            // the deaths: a line across every lane, the cross on the member's own
            foreach (var d in _t.Deaths)
            {
                if (d.At < _from || d.At > _to) continue;
                double x = Math.Round(X(d.At)) + 0.5;
                dc.DrawLine(P(A(Coral, 0.45), 1, dash: true), new Point(x, RulerH), new Point(x, size.Height - Pad));
                if (Rows.FirstOrDefault(r => r.Data is PullTimeline.MemberLane ml && ml.F == d.F) is { } row)
                {
                    double cy = row.Top + row.Height / 2, s = 4;
                    dc.DrawEllipse(_p.Back, null, new Point(x, cy), s + 2, s + 2);
                    dc.DrawLine(P(Coral, 2), new Point(x - s, cy - s), new Point(x + s, cy + s));
                    dc.DrawLine(P(Coral, 2), new Point(x - s, cy + s), new Point(x + s, cy - s));
                }
            }
            if (_mark is { } mk && mk >= _from && mk <= _to)
            {
                double x = Math.Round(X(mk)) + 0.5;
                dc.DrawLine(P(Coral, 1.5), new Point(x, RulerH), new Point(x, size.Height - Pad));
            }
        }

        // ---- the hover: the exact event under the pointer, in a box

        /// <summary>What the hover names at a point: a title and its lines (a coloured dot, words, a figure).</summary>
        internal (string Title, List<(Color? Dot, string Words, string Figure)> Lines)? At(Point at)
        {
            if (at.X < Gutter || PlotW <= 0) return null;
            double s = SecondAt(at.X), tol = 6 / Math.Max(0.001, PlotW) * (_to - _from);
            int sec = Math.Clamp((int)Math.Floor(s), 0, _t.N - 1);
            var lines = new List<(Color?, string, string)>();
            // a death line is hovered anywhere across the chart
            var death = _t.Deaths.Where(d => Math.Abs(d.At - s) <= tol).OrderBy(d => Math.Abs(d.At - s)).FirstOrDefault();
            var row = Rows.FirstOrDefault(r => at.Y >= r.Top - 1 && at.Y <= r.Top + r.Height + 1);
            if (death != null && (row == null || row.Kind != "ability" || !NearCast(row, s, tol, out _)))
            {
                string title = L.F("{0} died at {1}", NameOf(death.F), ClockTenths(death.At)) + (death.BossHpLeft is { } bh ? "  ·  " + L.F("boss at {0}", Pct(bh, 0)) : "");
                foreach (var (h, i) in death.Last.Select((h, i) => (h, i)))
                {
                    string who = Who(h.Source, h.Skill);
                    lines.Add((Coral, ClockTenths(h.At) + "  " + (who.Length > 0 ? who : L.T("Unnamed hit")) + (i == KillingBlow(death) ? "  · " + L.T("killing blow") : ""), "−" + Int(h.Amount)));
                }
                if (death.Last.Count == 0) lines.Add((null, L.T("No hits taken were seen just before."), ""));
                return (title, lines);
            }
            if (row == null) return (Clock(sec), lines);
            switch (row.Kind)
            {
                case "hp" when _t.Hp is { } hp:
                {
                    var ph = _t.Phases.Where(p => Math.Abs(p.At - s) <= tol).OrderBy(p => Math.Abs(p.At - s)).FirstOrDefault();
                    if (ph != null) return (L.F("Boss at {0}", Pct(ph.Mark, 0)), new() { (Gold, L.F("crossed at {0}", ClockTenths(ph.At)), "") });
                    return (L.F("{0} · Boss HP", Clock(sec)), new() { (Mint, L.T("HP left at the end of the second"), Pct(hp[sec], 1)) });
                }
                case "group":
                {
                    var sp = _t.Spikes.FirstOrDefault(x => x.Second == sec);
                    lines.Add((_p.InkC, L.T("The group's damage that second"), Int(_t.Group[sec])));
                    foreach (var m in _t.Members.Where(m => m.PerSecond[sec] > 0).OrderByDescending(m => m.PerSecond[sec]).Take(4))
                        lines.Add((m.F.You ? Gold : ClassArt.ColorOf(m.F.Class), NameOf(m.F), Int(m.PerSecond[sec])));
                    return (sp != null ? L.F("{0} · a damage spike", Clock(sec)) : Clock(sec), lines);
                }
                case "hits":
                {
                    var h = _t.BigHits.Where(x => Math.Abs(x.At - s) <= tol * 1.5).OrderBy(x => Math.Abs(x.At - s)).FirstOrDefault();
                    if (h == null) return null;
                    int rank = _t.BigHits.OrderByDescending(x => x.Amount).ToList().IndexOf(h) + 1;
                    return (L.F("Your #{0} biggest hit", rank), new() { (h.Crit == true ? Coral : Gold, h.Name + (h.Crit == true ? "  · " + L.T("crit") : ""), Int(h.Amount)), (null, L.F("at {0}", ClockTenths(h.At)), "") });
                }
                case "ability" when row.Data is PullTimeline.Lane lane:
                {
                    if (NearCast(row, s, tol, out var c) && c != null)
                    {
                        int n = lane.Casts!.IndexOf(c) + 1;
                        lines.Add((row.Color, L.F("cast {0} of {1} at {2}", n, lane.Casts.Count, ClockTenths(c.At)), ""));
                        lines.Add((null, c.Hits == 1 ? L.T("1 hit") : L.F("{0} hits", c.Hits), Int(c.Damage)));
                        return (row.Label, lines);
                    }
                    lines.Add((row.Color, L.T("Damage that second"), Int(lane.PerSecond[sec])));
                    lines.Add((null, L.T("In the whole pull"), Int(lane.Damage) + "  ·  " + Pct(lane.Share, 1)));
                    if (lane.CastCount is { } cc) lines.Add((null, L.T("Casts"), cc.ToString("N0", CultureInfo.InvariantCulture)));
                    return (L.F("{0} · {1}", Clock(sec), row.Label), lines);
                }
                case "member" when row.Data is PullTimeline.MemberLane m:
                    lines.Add((row.Color, L.T("Damage that second"), Int(m.PerSecond[sec])));
                    lines.Add((null, L.T("In the whole pull"), Int(m.Damage)));
                    if (m.Deaths > 0) lines.Add((Coral, m.Deaths == 1 ? L.T("1 death") : L.F("{0} deaths", m.Deaths), ""));
                    return (L.F("{0} · {1}", Clock(sec), row.Label), lines);
            }
            return null;
        }

        bool NearCast(TRow row, double s, double tol, out CastMark? hit)
        {
            hit = null;
            if (row.Data is not PullTimeline.Lane { Casts: { } casts }) return false;
            hit = casts.Where(c => Math.Abs(c.At - s) <= tol).OrderBy(c => Math.Abs(c.At - s)).FirstOrDefault();
            return hit != null;
        }

        void Hover(Point? at)
        {
            using var dc = _hover.RenderOpen();
            if (at is not { } pt || At(pt) is not { } info) return;
            double x = Math.Round(Math.Clamp(pt.X, Gutter, Gutter + PlotW)) + 0.5;
            dc.DrawLine(P(A(_p.InkC, 0.35), 1), new Point(x, RulerH), new Point(x, RenderSize.Height - Pad));
            var title = Ft(info.Title, 11, _p.Ink, bold: true);
            var cells = info.Lines.Select(l => (l.Dot, W: Ft(l.Words, 11, _p.Ink), F: Ft(l.Figure, 11, _p.Ink, bold: true))).ToList();
            const double rowH = 16;
            double colW = cells.Select(c => (c.Dot != null ? 14 : 0) + c.W.Width + (c.F.Width > 0 ? 16 + c.F.Width : 0)).DefaultIfEmpty(0).Max();
            double w = Math.Max(title.Width, colW) + 20, h = 9 + title.Height + (cells.Count > 0 ? 4 + cells.Count * rowH : 0) + 5;
            double bx = x + 14 + w > RenderSize.Width ? x - 14 - w : x + 14;
            double by = Math.Clamp(pt.Y - h / 2, 2, Math.Max(2, RenderSize.Height - h - 2));
            dc.DrawRoundedRectangle(_p.Box, new Pen(_p.BoxEdge, 1), new Rect(bx, by, w, h), 7, 7);
            dc.DrawText(title, new Point(bx + 10, by + 6));
            for (int k = 0; k < cells.Count; k++)
            {
                var (dot, words, fig) = cells[k];
                double y = by + 8 + title.Height + 3 + k * rowH, nx = bx + 10;
                if (dot is { } dcol) { dc.DrawEllipse(B(dcol), null, new Point(nx + 4, y + rowH / 2), 3.5, 3.5); nx += 14; }
                dc.DrawText(words, new Point(nx, y + (rowH - words.Height) / 2));
                dc.DrawText(fig, new Point(bx + w - 10 - fig.Width, y + (rowH - fig.Height) / 2));
            }
        }

        /// <summary>For the tests and renders: the hover drawn at a point as the pointer would.</summary>
        internal void HoverForTest(Point at) => Hover(at);
        internal double XOf(double s) => X(s);
    }
}
