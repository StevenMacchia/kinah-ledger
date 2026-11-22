using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

sealed partial class HudScorebug
{
    // ================================================================ the broadcast kit (Arena and Studio)

    static readonly Color RedDeep = Color.FromRgb(0x9E, 0x0F, 0x20), Mint = Color.FromRgb(0x8D, 0xF5, 0xBF), Coral = Color.FromRgb(0xFF, 0x6B, 0x78),
        Amber = Color.FromRgb(0xFF, 0xC2, 0x3D), Quiet = Color.FromRgb(0x8B, 0x92, 0xA3), BoardLine = Color.FromRgb(0x2A, 0x2F, 0x3B);

    /// <summary>A text in a colour, the look's condensed face (or <paramref name="st"/>), tabular digits.</summary>
    static TextBlock C(string s, double size, Color c, FontWeight w, FontStretch? st = null) => Tt(s, size, Frozen(c), Face, w, st ?? Tight);
    static readonly FontStretch Semi = FontStretches.SemiCondensed;
    static string Up(string s) => s.ToUpper(CultureInfo.CurrentCulture);

    /// <summary>A soft shadow (or a glow in <paramref name="c"/> with no depth).</summary>
    static DropShadowEffect Glow(double blur, double depth, double opacity, Color? c = null)
    {
        var d = new DropShadowEffect { BlurRadius = blur, ShadowDepth = depth, Direction = 270, Opacity = opacity, Color = c ?? Colors.Black };
        d.Freeze();
        return d;
    }

    /// <summary>A block whose left edge leans by <paramref name="l"/> and right edge by <paramref name="r"/> (its top further right).</summary>
    static Geometry Slant(Rect r, double l, double rr)
    {
        var g = new StreamGeometry();
        using (var x = g.Open())
        {
            x.BeginFigure(new Point(r.Left + l, r.Top), true, true);
            x.LineTo(new Point(r.Right, r.Top), true, false);
            x.LineTo(new Point(r.Right - rr, r.Bottom), true, false);
            x.LineTo(new Point(r.Left, r.Bottom), true, false);
        }
        g.Freeze();
        return g;
    }

    /// <summary>A lit panel: its colour from <paramref name="top"/> to <paramref name="bottom"/>, a sheen over its upper half, a bright top line and a dark foot.</summary>
    static void Gloss(DrawingContext dc, Geometry g, Color top, Color bottom, double sheen = .2, double edge = .45)
    {
        var r = g.Bounds;
        dc.DrawGeometry(Tac.Grad(true, (top, 0), (bottom, 1)), null, g);
        dc.PushClip(g);
        if (sheen > 0) dc.DrawRectangle(Tac.Grad(true, (Tac.A(Colors.White, sheen), 0), (Tac.A(Colors.White, sheen * .25), .5), (Tac.A(Colors.White, 0), .52)), null, r);
        if (edge > 0) dc.DrawRectangle(Frozen(Tac.A(Colors.White, edge)), null, new Rect(r.Left, r.Top, r.Width, 1));
        dc.DrawRectangle(Frozen(Tac.A(Colors.Black, .45)), null, new Rect(r.Left, r.Bottom - 1, r.Width, 1));
        dc.Pop();
    }

    /// <summary>A cell: <paramref name="draw"/> as its ground, <paramref name="child"/> on it.</summary>
    static Grid GCell(Action<DrawingContext, Size> draw, FrameworkElement? child, double h, Thickness pad)
    {
        var g = new Grid { Height = h };
        g.Children.Add(new TacSketch(draw));
        if (child != null) { child.Margin = pad; g.Children.Add(child); }
        return g;
    }

    /// <summary>A slanted glossy cell in one colour.</summary>
    static Grid GBlock(Color c, FrameworkElement? child, double h, Thickness pad, double l = 7, double r = 7, double sheen = .2) =>
        GCell((dc, s) => Gloss(dc, Slant(new Rect(s), l, r), Tac.Mix(c, Colors.White, .12), Tac.Mix(c, Colors.Black, .22), sheen), child, h, pad);

    /// <summary>A rank's colour: gold, silver, bronze for the podium, slate below it, red for you.</summary>
    static Color RankColor(MeterMember m) => m.You ? RedC : m.CpOnly ? SlateC : m.Rank switch { 1 => GoldTab, 2 => SilverTab, 3 => BronzeTab, _ => SlateC };

    /// <summary>A row of LED segments in <paramref name="rect"/>, the first <paramref name="lit"/> of them lit in <paramref name="c"/>, the last lit one white-hot.</summary>
    static void Leds(DrawingContext dc, Rect rect, double lit, Color c, double seg = 4, double gap = 1.6)
    {
        if (rect.Width < seg) return;
        int n = Math.Max(1, (int)Math.Floor((rect.Width + gap) / (seg + gap)));
        double step = (rect.Width + gap) / n, sw = step - gap;
        int on = (int)Math.Round(n * Math.Clamp(lit, 0, 1));
        if (lit > 0 && on == 0) on = 1;
        var offB = Frozen(Tac.A(c, .13));
        var onB = Tac.Grad(true, (Tac.Mix(c, Colors.White, .45), 0), (c, .45), (Tac.Mix(c, Colors.Black, .25), 1));
        var head = Tac.Grad(true, (Tac.Mix(c, Colors.White, .8), 0), (Tac.Mix(c, Colors.White, .4), 1));
        for (int i = 0; i < n; i++)
            dc.DrawRoundedRectangle(i < on ? (i == on - 1 ? head : onB) : offB, null, new Rect(rect.X + i * step, rect.Y, sw, rect.Height), 1, 1);
    }

    /// <summary>
    /// A line of parts fitted to <paramref name="host"/>'s room: while too wide, each part in <paramref name="give"/> takes its
    /// short form (or goes, with none) in turn; with <paramref name="name"/>, the name trims first (never a 10-letter name).
    /// </summary>
    static void FitLine(FrameworkElement host, TextBlock? name, IReadOnlyList<FrameworkElement> keep, IReadOnlyList<(FrameworkElement Full, FrameworkElement? Short)> give)
    {
        double room = host.ActualWidth;
        if (room <= 0) return;
        if (name != null) { Tac.FitName(name, room, keep, give, nameMin: 22, nameFloor: Tac.NameFloor(name)); return; }
        var inf = new Size(double.PositiveInfinity, double.PositiveInfinity);
        double Wd(FrameworkElement e) { var v = e.Visibility; e.Visibility = Visibility.Visible; e.Measure(inf); double w = e.DesiredSize.Width; e.Visibility = v; return w; }
        double fixedW = keep.Sum(Wd);
        for (int level = 0; level <= give.Count; level++)
        {
            double w = fixedW;
            for (int j = 0; j < give.Count; j++) w += j < level ? (give[j].Short is { } s ? Wd(s) : 0) : Wd(give[j].Full);
            if (w <= room || level == give.Count)
            {
                for (int j = 0; j < give.Count; j++)
                {
                    give[j].Full.Visibility = j < level ? Visibility.Collapsed : Visibility.Visible;
                    if (give[j].Short is { } s) s.Visibility = j < level ? Visibility.Visible : Visibility.Collapsed;
                }
                return;
            }
        }
    }

    /// <summary>
    /// A leaderboard row of either package: its ground, its words, and the two moments a row plays (a kill's glint crossing it,
    /// <see cref="Sweep"/>; its entrance wiping in behind a slanted edge, <see cref="Enter"/>, the words on once it is in).
    /// </summary>
    abstract class BoardRow : IThemeRow, IThemeBarSpan
    {
        protected readonly Grid RootG = new();
        /// <summary>The row's words (faded in after its entrance's wipe, so no figure is ever shown cut).</summary>
        protected readonly Grid Words = new();
        readonly TacSketch _fx;
        protected MeterMember? M;
        protected double Bar;
        protected Action? Refit;
        /// <summary>A light crossing the row (a kill's glint over the winner), 0..1; -1 none.</summary>
        public double Sweep = -1;
        /// <summary>The row's entrance, 0..1.</summary>
        public double Enter = 1;
        /// <summary>The row's words once its entrance is in, 0..1 (they come on after the wipe, never cut by it).</summary>
        public double WordsIn = 1;

        public FrameworkElement Root => RootG;

        /// <summary>Where the row's bar is drawn (each package says; none by default).</summary>
        public virtual (FrameworkElement On, Rect Track)? BarTrack => null;

        protected BoardRow(double h)
        {
            RootG.Height = h;
            _fx = new TacSketch(DrawFx);
            Panel.SetZIndex(_fx, 10);
            RootG.Children.Add(Words);
            RootG.Children.Add(_fx);
            RootG.SizeChanged += (_, _) => Refit?.Invoke();
        }

        public abstract void Set(MeterMember m, MeterContext c, bool fresh);

        /// <summary>The moments drawn as far as <see cref="Sweep"/> and <see cref="Enter"/> say.</summary>
        public void Apply()
        {
            if (EnterWhole)
            {
                // the row as one piece: its plate, bar and words fade in together, settling down a few pixels (no wipe: no plate
                // shown bare, no figure cut by a slanted edge)
                double k = Math.Clamp(Enter, 0, 1);
                RootG.ClearValue(UIElement.ClipProperty);
                RootG.Opacity = k;
                _drop.Y = -6 * (1 - k);
                foreach (UIElement ch in RootG.Children) if (ch != _fx && ch.RenderTransform != _drop) ch.RenderTransform = _drop;
                Words.Opacity = 1;
            }
            else
            {
                if (Enter >= 1) RootG.ClearValue(UIElement.ClipProperty); else RootG.Clip = Wipe(-10, (Math.Max(RootG.ActualWidth, 300) + 20) * Enter, RootG.Height);
                Words.Opacity = Enter >= 1 ? WordsIn : 0;
            }
            _fx.InvalidateVisual();
        }

        /// <summary>The row enters as one piece (Studio) rather than wiping in behind a slanted edge, its words after (Arena).</summary>
        protected virtual bool EnterWhole => false;
        readonly TranslateTransform _drop = new();

        void DrawFx(DrawingContext dc, Size s)
        {
            if (Sweep is not (> 0 and < 1)) return;
            double x = -60 + (s.Width + 120) * Sweep;
            dc.DrawGeometry(Tac.Grad(false, (Color.FromArgb(0, 0xFF, 0xFF, 0xFF), 0), (Color.FromArgb(0x9A, 0xFF, 0xF4, 0xC8), .5), (Color.FromArgb(0, 0xFF, 0xFF, 0xFF), 1)), null, Tac.Lean(new Rect(x - 30, 0, 60, s.Height), 10));
        }
    }

    /// <summary>
    /// Arena's row, the stadium's video board (Steven picked it, 2026-11-17): a slanted position chip (gold, silver, bronze,
    /// then dark; yours red), the class's glowing stripe and the name in capitals over its quiet line (Combat Power, deaths,
    /// the TOP marks), the bar as LED segments in the class's colour glowing on the black board (a healer's healing as a green
    /// line of segments under it, the heal figure at its end), the DPS big and the share small under it; with Details, the
    /// damage and crit in a column of their own. Short of room the marks go to their glyphs, then the CP; never a figure cut.
    /// </summary>
    sealed class ArenaRow : BoardRow
    {
        public const double H = 34, Pitch = 37;
        const double NameW = 108;
        readonly Border _ground = new();
        readonly Grid _rank;
        readonly TacSketch _rankArt;
        readonly TextBlock _rankT = C("", 17, Colors.White, FontWeights.Bold);
        readonly Border _stripe = new() { Width = 3, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 4) };
        readonly TextBlock _name = C("", 14, Colors.White, FontWeights.Bold, Semi);
        readonly StackPanel _sub = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, -1, 0, 0) };
        readonly Border _subHost = new();
        readonly Grid _barCol = new() { Margin = new Thickness(0, 0, 8, 0) };
        readonly TacSketch _leds;
        readonly Border _healBox = new() { Background = Frozen(Tac.A(Colors.Black, .72)), Padding = new Thickness(4, 0, 4, 0), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom };
        readonly TextBlock _heal = C("", 10.5, Mint, FontWeights.Bold, Semi);
        readonly TextBlock _dps = C("", 20, Colors.White, FontWeights.Bold), _pct = C("", 10, Quiet, FontWeights.SemiBold, Semi),
            _dmg = C("", 13.5, Colors.White, FontWeights.Bold), _crit = C("", 10, Quiet, FontWeights.SemiBold, Semi);
        readonly StackPanel _more;
        readonly double _moreW;
        Color _rc;
        bool _healer, _expanded;
        double _healBar;

        public ArenaRow() : base(H)
        {
            foreach (var w in new[] { 30.0, -1, double.NaN, Tac.FigCol(_dps, "888.8K", "888,888") + 6, 0 })
                Words.ColumnDefinitions.Add(new ColumnDefinition { Width = double.IsNaN(w) ? new GridLength(1, GridUnitType.Star) : w < 0 ? new GridLength(.45, GridUnitType.Star) : new GridLength(w) });
            // the name's column grows with a wider meter (a long name whole at 640), never under the room a 10-letter name needs
            Words.ColumnDefinitions[1].MinWidth = NameW; Words.ColumnDefinitions[1].MaxWidth = 172;
            _moreW = Math.Max(Tac.FigCol(_dmg, "888.88M"), Tac.FigCol(_crit, "88%")) + 10;
            RootG.Children.Insert(0, _ground);
            // the position chip
            _rankArt = new TacSketch((dc, s) => Gloss(dc, Slant(new Rect(s), 0, 5), Tac.Mix(_rc, Colors.White, .12), Tac.Mix(_rc, Colors.Black, .22), .3));
            _rank = new Grid { Width = 26, Height = 24, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
            _rank.Children.Add(_rankArt);
            _rankT.HorizontalAlignment = HorizontalAlignment.Center; _rankT.Margin = new Thickness(-2, 0, 0, 1);
            _rank.Children.Add(_rankT);
            Words.Children.Add(_rank);
            // the stripe and the name over its quiet line
            var nameCol = new Grid { Margin = new Thickness(0, 0, 6, 0) };
            nameCol.Children.Add(_stripe);
            var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
            _name.TextTrimming = TextTrimming.CharacterEllipsis;
            stack.Children.Add(_name);
            _subHost.Child = _sub;
            stack.Children.Add(_subHost);
            nameCol.Children.Add(stack);
            Grid.SetColumn(nameCol, 1); Words.Children.Add(nameCol);
            // the LED bar (a healer's line under it) runs on under the figures to the row's end (2026-11-21: with the meter
            // expanded the leader's bar stopped at about half the row), a shade under the figures so every digit reads; the heal
            // figure at the end of the bar's own column
            _leds = new TacSketch(DrawLeds);
            _barCol.Children.Add(_leds);
            _barCol.Margin = new Thickness(0, 0, 3, 0);
            Grid.SetColumn(_barCol, 2); Grid.SetColumnSpan(_barCol, 3); Words.Children.Add(_barCol);
            var shade = new Border { IsHitTestVisible = false, Margin = new Thickness(-14, 2, 0, 2),
                Background = Tac.Grad(false, (Tac.A(InkC0, 0), 0), (Tac.A(InkC0, .4), .25), (Tac.A(InkC0, .48), 1)) };
            Grid.SetColumn(shade, 3); Grid.SetColumnSpan(shade, 2); Words.Children.Add(shade);
            _heal.Effect = Glow(4, 0, 1); _heal.Tag = "heal-figures";
            _healBox.Child = _heal; _healBox.Margin = new Thickness(0, 0, 8, 0);
            Grid.SetColumn(_healBox, 2); Words.Children.Add(_healBox);
            // the DPS over the share
            _dps.Tag = "row-dps"; _dps.HorizontalAlignment = HorizontalAlignment.Right;
            _dps.LineHeight = 20; _dps.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
            _pct.HorizontalAlignment = HorizontalAlignment.Right;
            var fig = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0), Children = { _dps, _pct } };
            Grid.SetColumn(fig, 3); Words.Children.Add(fig);
            // Details: the damage over the crit
            _dmg.HorizontalAlignment = HorizontalAlignment.Right; _dmg.LineHeight = 16; _dmg.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
            _crit.HorizontalAlignment = HorizontalAlignment.Right;
            _more = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0), Children = { _dmg, _crit } };
            Grid.SetColumn(_more, 4); Words.Children.Add(_more);
            Refit = () => FitLine(_subHost, null, _keep, _give);
        }

        readonly List<FrameworkElement> _keep = new();
        readonly List<(FrameworkElement, FrameworkElement?)> _give = new();

        /// <summary>The LED line: DrawLeds' rect (the art's whole width; 16 px tall and centred, or 13 px from y 5 with a healer's line under it). A bar of v lights round(v * segments) of them, so it ends on a segment.</summary>
        public override (FrameworkElement On, Rect Track)? BarTrack
        {
            get
            {
                if (_leds.ActualWidth <= 0) return null;
                double bh = _healer ? 13 : 16, y = _healer ? 5 : Math.Round((_leds.ActualHeight - bh) / 2);
                return (_leds, new Rect(0, y, _leds.ActualWidth, bh));
            }
        }

        public override void Set(MeterMember m, MeterContext c, bool fresh)
        {
            M = m;
            if (_expanded != c.Expanded) { _expanded = c.Expanded; Words.ColumnDefinitions[4].Width = new GridLength(c.Expanded ? _moreW : 0); }
            var col = m.ClassColor;
            _ground.Background = m.You ? Tac.Grad(false, (Tac.A(RedC, .32), 0), (Tac.A(RedC, .08), 1)) : Frozen(Tac.A(Colors.White, .025));
            _ground.BorderBrush = Frozen(Tac.A(RedC, .8)); _ground.BorderThickness = new Thickness(0, m.You ? 1 : 0, 0, m.You ? 1 : 0);
            _rc = RankColor(m); if (_rc == SlateC) _rc = Color.FromRgb(0x23, 0x27, 0x2F);
            _rankArt.InvalidateVisual();
            _rankT.Text = m.Rank.ToString(CultureInfo.InvariantCulture);
            _rankT.Foreground = Frozen(RankColor(m) == SlateC || m.You ? Colors.White : InkC0);
            _stripe.Background = Tac.Grad(true, (Tac.Mix(col, Colors.White, .3), 0), (col, 1));
            _stripe.Effect = Glow(6, 0, .8, col);
            _name.Text = Up(m.Name);
            // the quiet line: Combat Power, deaths, the TOP marks (to their glyphs short of room)
            _sub.Children.Clear(); _keep.Clear(); _give.Clear();
            TextBlock Small(string s, Color cl, FontWeight fw, string? tag = null) { var t = C(s, 10, cl, fw, Semi); t.Margin = new Thickness(0, 0, 6, 0); t.Tag = tag; _sub.Children.Add(t); return t; }
            TextBlock? cp = m.Cp.Length > 0 ? Small(m.Cp, Quiet, FontWeights.SemiBold) : null;
            if (m.Deaths > 0) _keep.Add(Small(m.Deaths > 1 ? "✕" + m.Deaths : "✕", Coral, FontWeights.Bold));
            if (m.TopDps) { var f = Small("★ " + Up(L.T("TOP DPS")), Amber, FontWeights.Bold, "top-dps"); var s = Small("★", Amber, FontWeights.Bold, "top-dps"); s.ToolTip = L.T("Top DPS"); _give.Add((f, s)); }
            if (m.TopHeals) { var f = Small("✚ " + Up(L.T("TOP HEALS")), Mint, FontWeights.Bold, "top-heals"); var s = Small("✚", Mint, FontWeights.Bold, "top-heals"); s.ToolTip = L.T("Top heals"); _give.Add((f, s)); }
            if (cp != null) _give.Add((cp, null));
            _subHost.Visibility = _sub.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            // the bar
            _healer = m.Heal.Length > 0;
            double healAll = c.E.Members.Sum(f => (double)f.HealingEffective), healTop = c.E.Members.Select(f => (double)f.HealingEffective).DefaultIfEmpty(0).Max();
            _healBar = _healer && healTop > 0 && healAll > 0 ? Math.Clamp(m.HealShare / (healTop / healAll), 0, 1) : 0;
            _heal.Text = _healer ? "+" + m.Heal : "";
            _healBox.Visibility = _healer ? Visibility.Visible : Visibility.Collapsed;
            _leds.Effect = Glow(9, 0, .7, col);
            _dps.Text = m.Figure; _dps.FontSize = m.CpOnly ? 16 : 20;
            _dps.Foreground = Frozen(m.You ? Color.FromRgb(0xFF, 0xE3, 0xA3) : Colors.White);
            _dps.Effect = Glow(8, 0, .5, m.You ? Amber : Colors.White);
            _pct.Text = m.ShareText;
            _dmg.Text = c.Expanded ? m.Damage : ""; _crit.Text = c.Expanded ? m.Crit : "";
            Refit?.Invoke();
            SlideBar(_leds, c, "bar", Tac.BarOf(m, c), v => { Bar = v; _leds.InvalidateVisual(); });
            _leds.InvalidateVisual();
        }

        void DrawLeds(DrawingContext dc, Size s)
        {
            if (M is not { } m || s.Width < 8) return;
            double bh = _healer ? 13 : 16, y = _healer ? 5 : Math.Round((s.Height - bh) / 2);
            Leds(dc, new Rect(0, y, s.Width, bh), Bar, m.ClassColor, 4, 1.6);
            if (_healer) Leds(dc, new Rect(0, y + bh + 3, s.Width, 4), _healBar, GreenC, 4, 1.6);
        }
    }
}
