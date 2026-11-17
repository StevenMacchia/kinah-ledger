using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using KinahLedger.Parser;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

sealed partial class HudHolo
{
    /// <summary>The fight's foot on the glass: TOTAL and the figure, the group's word and figure, the held average's cut chip.</summary>
    FrameworkElement Foot(Encounter e, DateTime now)
    {
        var (total, word, group, held) = FootOf(e, now);
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = new StackPanel { Orientation = Orientation.Horizontal };
        left.Children.Add(Label(L.T("TOTAL")));
        var tv = Tt(total, 15, White, Face, FontWeights.Bold); tv.Margin = new Thickness(6, 0, 0, 0); left.Children.Add(tv);
        g.Children.Add(left);
        var right = new StackPanel { Orientation = Orientation.Horizontal };
        right.Children.Add(Label(word.ToUpperInvariant()));
        var gv = Tt(group, 15, Pale, Face, FontWeights.Bold); gv.Margin = new Thickness(6, 0, 0, 0); right.Children.Add(gv);
        if (held)
        {
            var chip = Chip(L.T(Encounter.AvgBadge).ToUpperInvariant(), GreenC, filled: false);
            chip.Tag = "avg-badge"; chip.Margin = new Thickness(7, 0, 0, 0);
            right.Children.Add(chip);
        }
        Grid.SetColumn(right, 1); g.Children.Add(right);
        return AsFoot(g, e, now);
    }

    /// <summary>The line between the party and you pinned below it: "◇ 2 MORE" on a cyan hairline.</summary>
    static FrameworkElement GapLine(int left)
    {
        var g = new Grid { VerticalAlignment = VerticalAlignment.Center };
        g.Children.Add(Hairline(new Thickness(0)));
        var t = Tt(left > 0 ? "◇ " + L.F("{0} more", left).ToUpperInvariant() : "◇", 9.5, Lite, Small, FontWeights.SemiBold);
        g.Children.Add(new Border { Background = Frozen(GroundC), Padding = new Thickness(6, 0, 6, 0), HorizontalAlignment = HorizontalAlignment.Center, Child = t });
        return g;
    }

    /// <summary>A small cut-cornered chip (YOU, TOP DPS, AVG. DPS): filled with dark words, or an outline with its words in the colour.</summary>
    static Grid Chip(string s, Color c, bool filled)
    {
        var g = new Grid { VerticalAlignment = VerticalAlignment.Center };
        g.Children.Add(new TacSketch((dc, sz) =>
        {
            var r = new Rect(.5, .5, sz.Width - 1, sz.Height - 1);
            dc.DrawGeometry(filled ? Frozen(c) : Frozen(Tac.A(DeepC, .7)), Tac.P(Frozen(c), 1), Tac.Cut(r, 4));
        }));
        var t = Tt(s, 9, filled ? Frozen(DeepC) : Frozen(c), Small, FontWeights.Bold); t.Margin = new Thickness(6, 1, 6, 1);
        g.Children.Add(t);
        return g;
    }

    /// <summary>
    /// One member as a line of a holographic list: the rank in a hex in the class's colour (the podium's hex glows), the name
    /// in its class's colour and its marks, the DPS in white and the share in light cyan, and under it a glowing line bar on
    /// a dashed track, bright at its head. You: the row lit as cyan glass with a bright edge at its left and a YOU chip.
    /// </summary>
    sealed class HoloRow : IThemeRow
    {
        public const double H = 29, Pitch = 32;
        readonly Grid _root = new() { Height = H };
        readonly TacSketch _art;
        readonly TextBlock _rank = Tt("", 12, White, Face, FontWeights.Bold), _name = Tt("", 14.5, White, Face, FontWeights.SemiBold),
            _dps = Tt("", 16, White, Face, FontWeights.Bold), _pct = Tt("", 12, Lite, Face, FontWeights.SemiBold), _dmg = Tt("", 13.5, Pale, Face, FontWeights.SemiBold), _crit = Tt("", 12, Lite, Face, FontWeights.SemiBold);
        readonly StackPanel _nameLine = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(7, 2, 0, 0) };
        MeterMember? _m;
        double _bar;
        bool _expanded;

        public FrameworkElement Root => _root;

        public HoloRow()
        {
            foreach (var w in new[] { 30.0, double.NaN, 62, 50, 0, 0 })
                _root.ColumnDefinitions.Add(new ColumnDefinition { Width = double.IsNaN(w) ? new GridLength(1, GridUnitType.Star) : new GridLength(w) });
            _art = new TacSketch(Draw);
            Grid.SetColumnSpan(_art, 6);
            _root.Children.Add(_art);
            _rank.HorizontalAlignment = HorizontalAlignment.Center; _rank.VerticalAlignment = VerticalAlignment.Top; _rank.Margin = new Thickness(0, 5, 0, 0);
            Put(_rank, 0);
            _name.TextTrimming = TextTrimming.CharacterEllipsis;
            _nameLine.Children.Add(_name);
            Put(_nameLine, 1);
            foreach (var (t, col) in new[] { (_dps, 2), (_pct, 3), (_dmg, 4), (_crit, 5) })
            {
                t.HorizontalAlignment = HorizontalAlignment.Right; t.VerticalAlignment = VerticalAlignment.Top;
                t.Margin = new Thickness(0, t == _dps ? 1 : 4, 7, 0);
                Put(t, col);
            }
            _dps.Tag = "row-dps";
            _root.SizeChanged += (_, _) => Fit();
        }

        void Put(FrameworkElement el, int col) { Grid.SetColumn(el, col); _root.Children.Add(el); }

        public void Set(MeterMember m, MeterContext c, bool fresh)
        {
            _m = m;
            if (_expanded != c.Expanded)
            {
                _expanded = c.Expanded;
                _root.ColumnDefinitions[4].Width = new GridLength(c.Expanded ? 58 : 0);
                _root.ColumnDefinitions[5].Width = new GridLength(c.Expanded ? 42 : 0);
            }
            _rank.Text = m.Rank.ToString(CultureInfo.InvariantCulture);
            _name.Text = m.Name;
            _name.Foreground = Frozen(m.NameColor);
            while (_nameLine.Children.Count > 1) _nameLine.Children.RemoveAt(1);
            if (m.You) { var y = Chip(L.T("YOU"), CyanC, filled: true); y.Margin = new Thickness(7, 1, 0, 0); _nameLine.Children.Add(y); }
            if (m.Deaths > 0) { var d = Tt(m.Deaths > 1 ? $"✖{m.Deaths}" : "✖", 11, Coral, Face, FontWeights.Bold); d.Margin = new Thickness(6, 2, 0, 0); _nameLine.Children.Add(d); }
            if (m.Heal.Length > 0)
            {
                var h = Tt("+" + m.Heal, 12, Green, Face, FontWeights.SemiBold);
                h.Tag = "heal-figures"; h.Margin = new Thickness(6, 2, 0, 0);
                _nameLine.Children.Add(h);
            }
            if (m.TopDps) { var p = Chip(L.T("TOP DPS"), AmberC, filled: false); p.Tag = "top-dps"; p.Margin = new Thickness(6, 1, 0, 0); _nameLine.Children.Add(p); }
            if (m.TopHeals) { var p = Chip(L.T("TOP HEALS"), GreenC, filled: false); p.Tag = "top-heals"; p.Margin = new Thickness(6, 1, 0, 0); _nameLine.Children.Add(p); }
            _dps.Text = m.Figure;
            _pct.Text = m.ShareText;
            _dmg.Text = c.Expanded ? m.Damage : "";
            _crit.Text = c.Expanded ? m.Crit : "";
            Fit();
            SlideBar(_art, c, "bar", m.Share, v => { _bar = v; _art.InvalidateVisual(); });
            _art.InvalidateVisual();
        }

        void Fit()
        {
            double room = _root.ColumnDefinitions[1].ActualWidth;
            if (room <= 0) { _name.ClearValue(FrameworkElement.MaxWidthProperty); return; }
            double marks = 0;
            for (int i = 1; i < _nameLine.Children.Count; i++) { var el = (FrameworkElement)_nameLine.Children[i]; el.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity)); marks += el.DesiredSize.Width; }
            _name.MaxWidth = Math.Max(28, room - _nameLine.Margin.Left - marks - 6);
        }

        void Draw(DrawingContext dc, Size s)
        {
            if (_m is not { } m || s.Width < 40) return;
            double h = s.Height, x0 = _root.ColumnDefinitions[0].ActualWidth;
            var tone = m.ClassColor;
            // you: the row lit as glass, a bright edge at its left, its corners cut
            if (m.You)
            {
                var r = new Rect(.5, .5, s.Width - 1, h - 1);
                dc.DrawGeometry(Tac.Grad(false, (Tac.A(CyanC, .2), 0), (Tac.A(CyanC, .05), 1)), Tac.P(Frozen(Tac.A(CyanC, .45)), 1), Tac.Cut(r, 6));
                dc.DrawRectangle(Cyan, null, new Rect(0, 6, 2, h - 12));
            }
            // the rank's hex in the class's colour; the podium's filled faintly and ringed in light
            double cx = x0 / 2, cy = 13, rr = 10.5;
            var pts = Enumerable.Range(0, 6).Select(i => { double a = Math.PI / 3 * i - Math.PI / 2; return new Point(cx + rr * Math.Cos(a), cy + rr * Math.Sin(a)); }).ToArray();
            var hex = new StreamGeometry();
            using (var x = hex.Open()) { x.BeginFigure(pts[0], true, true); foreach (var p in pts.Skip(1)) x.LineTo(p, true, false); }
            hex.Freeze();
            bool podium = m.Rank <= 3 && !m.CpOnly;
            if (podium) dc.DrawGeometry(null, Tac.P(Frozen(Tac.A(tone, .35)), 4, round: true), hex);
            dc.DrawGeometry(Frozen(Tac.A(Tac.Mix(DeepC, tone, podium ? .38 : .18), .95)), Tac.P(Frozen(Tac.Mix(tone, Colors.White, .2)), 1.3, round: true), hex);
            // the line bar: a dashed track, a glowing fill in the class's colour, a bright head
            double bx = x0 + 7, bw = s.Width - bx - 7, by = h - 4.5;
            dc.DrawLine(Tac.P(Frozen(Tac.A(CyanC, .22)), 1, dash: new DashStyle(new double[] { 2, 2 }, 0)), new Point(bx, by), new Point(bx + bw, by));
            double fx = bw * Math.Clamp(_bar, 0, 1);
            if (fx > .5)
            {
                dc.DrawRectangle(Frozen(Tac.A(tone, .22)), null, new Rect(bx, by - 3, fx, 6));
                dc.DrawRectangle(Frozen(Tac.A(tone, .55)), null, new Rect(bx, by - 1.5, fx, 3));
                dc.DrawRectangle(Frozen(Tac.Mix(tone, Colors.White, .45)), null, new Rect(bx, by - .75, fx, 1.5));
                dc.DrawRectangle(Tac.Grad(false, (Tac.A(Colors.White, 0), 0), (Tac.A(Colors.White, .95), 1)), null, new Rect(bx + fx - 10, by - 1, 10, 2));
            }
        }
    }
}
