using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using KinahLedger.Parser;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

sealed partial class HudFighter
{
    /// <summary>
    /// The fight's foot as the jet's readout: TOTAL and the fight's figure, a dotted leader, the group's word and its figure,
    /// and the held fight's AVG. DPS tag in amber.
    /// </summary>
    FrameworkElement Foot(Encounter e, DateTime now)
    {
        var (total, word, group, held) = FootOf(e, now);
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = new StackPanel { Orientation = Orientation.Horizontal };
        left.Children.Add(Tt(L.T("TOTAL"), 10.5, PhosDim, Face, FontWeights.Bold));
        var tv = Tt(total, 14, Pale, Face, FontWeights.Bold); tv.Margin = new Thickness(6, 0, 0, 0); left.Children.Add(tv);
        g.Children.Add(left);
        var lead = new TacSketch((dc, s) =>
        {
            for (double x = 6; x < s.Width - 6; x += 4) dc.DrawRectangle(PhosFaint, null, new Rect(x, s.Height / 2, 1.2, 1.2));
        });
        Grid.SetColumn(lead, 1); g.Children.Add(lead);
        var right = new StackPanel { Orientation = Orientation.Horizontal };
        right.Children.Add(Tt(word.ToUpperInvariant(), 10.5, PhosDim, Face, FontWeights.Bold));
        var gv = Tt(group, 14, Phos, Face, FontWeights.Bold); gv.Margin = new Thickness(6, 0, 0, 0); right.Children.Add(gv);
        if (held)
        {
            var badge = new Border { Tag = "avg-badge", BorderBrush = Amber, BorderThickness = new Thickness(1), Padding = new Thickness(4, 0, 4, 0), Margin = new Thickness(7, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
                Child = Tt(L.T(Encounter.AvgBadge).ToUpperInvariant(), 9.5, Amber, Face, FontWeights.Bold) };
            right.Children.Add(badge);
        }
        Grid.SetColumn(right, 2); g.Children.Add(right);
        return AsFoot(g, e, now);
    }

    /// <summary>The line between the party and you pinned below it: "▼ 2 MORE" between dashes.</summary>
    static FrameworkElement GapLine(int left)
    {
        var g = new Grid { VerticalAlignment = VerticalAlignment.Center };
        g.Children.Add(new TacSketch((dc, s) => dc.DrawLine(Tac.P(PhosFaint, 1, dash: new DashStyle(new double[] { 2, 3 }, 0)), new Point(0, s.Height / 2), new Point(s.Width, s.Height / 2))));
        var t = Tt(left > 0 ? "▼ " + L.F("{0} more", left).ToUpperInvariant() : "▼", 10, PhosDim, Face, FontWeights.Bold);
        g.Children.Add(new Border { Background = Frozen(InkC0), Padding = new Thickness(6, 0, 6, 0), HorizontalAlignment = HorizontalAlignment.Center, Child = t });
        return g;
    }

    /// <summary>
    /// One member as a line of the target list: the rank in brackets ("[01]", amber for the lead), a diamond in the class's
    /// colour, the callsign (the name in its class's colour) and its marks, the DPS and share at the right, and under it a
    /// thin vector bar with a tick each tenth, its fill in the class's colour ending in a bright tick. You: amber corner
    /// brackets lock round the row and the YOU tag is solid amber.
    /// </summary>
    sealed class TargetRow : IThemeRow
    {
        public const double H = 27, Pitch = 30;
        readonly Grid _root = new() { Height = H };
        readonly TacSketch _art;
        readonly TextBlock _rank = Tt("", 12, PhosDim, Face, FontWeights.Bold), _name = Tt("", 13, Phos, Face, FontWeights.Bold),
            _dps = Tt("", 14.5, Pale, Face, FontWeights.Bold), _pct = Tt("", 11, PhosDim, Face, FontWeights.SemiBold), _dmg = Tt("", 12.5, Pale, Face, FontWeights.SemiBold), _crit = Tt("", 11, PhosDim, Face, FontWeights.SemiBold);
        readonly StackPanel _nameLine = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(16, 2, 0, 0) };
        MeterMember? _m;
        double _bar;
        bool _expanded;

        public FrameworkElement Root => _root;

        public TargetRow()
        {
            foreach (var w in new[] { 34.0, double.NaN, 62, 50, 0, 0 })
                _root.ColumnDefinitions.Add(new ColumnDefinition { Width = double.IsNaN(w) ? new GridLength(1, GridUnitType.Star) : new GridLength(w) });
            _art = new TacSketch(Draw);
            Grid.SetColumnSpan(_art, 6);
            _root.Children.Add(_art);
            _rank.HorizontalAlignment = HorizontalAlignment.Center; _rank.VerticalAlignment = VerticalAlignment.Top; _rank.Margin = new Thickness(0, 3, 0, 0);
            Put(_rank, 0);
            _name.TextTrimming = TextTrimming.CharacterEllipsis;
            _nameLine.Children.Add(_name);
            Put(_nameLine, 1);
            foreach (var (t, col) in new[] { (_dps, 2), (_pct, 3), (_dmg, 4), (_crit, 5) })
            {
                t.HorizontalAlignment = HorizontalAlignment.Right; t.VerticalAlignment = VerticalAlignment.Top;
                t.Margin = new Thickness(0, t == _dps ? 1 : 4, 6, 0);
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
            bool lead = m.Rank == 1 && !m.CpOnly;
            _rank.Text = m.Rank.ToString("00", CultureInfo.InvariantCulture);
            _rank.Foreground = lead || m.You ? Amber : PhosDim;
            _name.Text = m.Name;
            _name.Foreground = Frozen(m.NameColor);
            while (_nameLine.Children.Count > 1) _nameLine.Children.RemoveAt(1);
            if (m.You) _nameLine.Children.Add(Mark(L.T("YOU"), Amber, Dark, solid: true));
            if (m.Deaths > 0) _nameLine.Children.Add(Mark(m.Deaths > 1 ? $"✖{m.Deaths}" : "✖", Red, Red, solid: false));
            if (m.Heal.Length > 0)
            {
                var h = Tt("+" + m.Heal, 11, Frozen(Color.FromRgb(0x5E, 0xF0, 0xA0)), Face, FontWeights.Bold);
                h.Tag = "heal-figures"; h.Margin = new Thickness(6, 1, 0, 0);
                _nameLine.Children.Add(h);
            }
            if (m.TopDps) { var p = Mark(L.T("TOP DPS"), Amber, Amber, solid: false); p.Tag = "top-dps"; _nameLine.Children.Add(p); }
            if (m.TopHeals) { var p = Mark(L.T("TOP HEALS"), Frozen(Color.FromRgb(0x5E, 0xF0, 0xA0)), Frozen(Color.FromRgb(0x5E, 0xF0, 0xA0)), solid: false); p.Tag = "top-heals"; _nameLine.Children.Add(p); }
            _dps.Text = m.Figure;
            _dps.Foreground = m.You ? Amber : Pale;
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

        /// <summary>A tag in the name's line: solid (YOU) or an outline (a death, a kill's plates).</summary>
        static Border Mark(string s, Brush line, Brush fg, bool solid) => new()
        {
            Background = solid ? line : Frozen(Tac.A(InkC0, .6)), BorderBrush = line, BorderThickness = new Thickness(1), Padding = new Thickness(4, 0, 4, 0), Margin = new Thickness(6, 1, 0, 0),
            VerticalAlignment = VerticalAlignment.Center, Child = Tt(s, 9.5, fg, Face, FontWeights.Bold),
        };

        void Draw(DrawingContext dc, Size s)
        {
            if (_m is not { } m || s.Width < 40) return;
            var cols = _root.ColumnDefinitions;
            double x0 = cols[0].ActualWidth, h = s.Height;
            var tone = m.ClassColor;
            var light = Tac.Mix(tone, Colors.White, .25);
            // you: a faint amber wash and corner brackets locked round the row
            if (m.You)
            {
                dc.DrawRectangle(Tac.Grad(false, (Tac.A(AmberC, .14), 0), (Tac.A(AmberC, .03), 1)), null, new Rect(0, 0, s.Width, h));
                var b = new StreamGeometry();
                using (var x = b.Open())
                {
                    void C(Point a, Point c0, Point d) { x.BeginFigure(a, false, false); x.LineTo(c0, true, false); x.LineTo(d, true, false); }
                    double r = s.Width - .75, bt = h - .75, k = 6;
                    C(new Point(.75, k), new Point(.75, .75), new Point(k, .75)); C(new Point(r - k, .75), new Point(r, .75), new Point(r, k));
                    C(new Point(r, bt - k), new Point(r, bt), new Point(r - k, bt)); C(new Point(k, bt), new Point(.75, bt), new Point(.75, bt - k));
                }
                b.Freeze();
                dc.DrawGeometry(null, Tac.P(Amber, 1.5), b);
            }
            // the rank's brackets
            double rx = cols[0].ActualWidth / 2, ry = 3 + 8;
            var rb = Tac.P(m.Rank == 1 || m.You ? Amber : PhosFaint, 1.2);
            dc.DrawLine(rb, new Point(rx - 13, ry - 7), new Point(rx - 13, ry + 7)); dc.DrawLine(rb, new Point(rx - 13, ry - 7), new Point(rx - 10, ry - 7)); dc.DrawLine(rb, new Point(rx - 13, ry + 7), new Point(rx - 10, ry + 7));
            dc.DrawLine(rb, new Point(rx + 13, ry - 7), new Point(rx + 13, ry + 7)); dc.DrawLine(rb, new Point(rx + 13, ry - 7), new Point(rx + 10, ry - 7)); dc.DrawLine(rb, new Point(rx + 13, ry + 7), new Point(rx + 10, ry + 7));
            // the class's diamond before the callsign
            double dy = 3 + 8;
            var dia = new StreamGeometry();
            using (var x = dia.Open()) { x.BeginFigure(new Point(x0 + 6, dy - 5), true, true); x.LineTo(new Point(x0 + 11, dy), true, false); x.LineTo(new Point(x0 + 6, dy + 5), true, false); x.LineTo(new Point(x0 + 1, dy), true, false); }
            dia.Freeze();
            dc.DrawGeometry(Frozen(tone), Tac.P(Frozen(Tac.A(Colors.White, .4)), .8), dia);
            // the vector bar: a baseline with a tick each tenth, the fill a fine line in the class's colour, a bright tick at its end
            double bx = x0 + 1, bw = s.Width - bx - 4, by = h - 4;
            dc.DrawRectangle(PhosFaint, null, new Rect(bx, by, bw, 1));
            for (int i = 0; i <= 10; i++) { double tx = Math.Round(bx + bw * i / 10); dc.DrawRectangle(i % 5 == 0 ? PhosDim : PhosFaint, null, new Rect(tx, by - (i % 5 == 0 ? 3 : 2), 1, i % 5 == 0 ? 4 : 3)); }
            double fx = bw * Math.Clamp(_bar, 0, 1);
            if (fx > .5)
            {
                dc.DrawRectangle(Frozen(Tac.A(tone, .35)), null, new Rect(bx, by - 2, fx, 4));
                dc.DrawRectangle(Frozen(light), null, new Rect(bx, by - 1, fx, 2));
                dc.DrawRectangle(Frozen(Colors.White), null, new Rect(bx + fx - 1, by - 4, 1.6, 7));
            }
        }
    }
}
