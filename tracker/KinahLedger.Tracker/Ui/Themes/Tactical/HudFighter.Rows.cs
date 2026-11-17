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
        if (word.Length > 0)
        {
            right.Children.Add(Tt(word.ToUpperInvariant(), 10.5, PhosDim, Face, FontWeights.Bold));
            var gv = Tt(group, 14, Phos, Face, FontWeights.Bold); gv.Margin = new Thickness(6, 0, 0, 0); right.Children.Add(gv);
        }
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
        readonly StackPanel _nameLine = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(15, 2, 0, 0) };
        /// <summary>
        /// Combat Power after the callsign: a small dim "CP" tight on the figure, a step under the name's weight. Always this
        /// compact form (the mono face is wide: every row of the list says it the same way, and the callsign keeps its room).
        /// </summary>
        readonly StackPanel _cp = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(5, 1, 0, 0) };
        readonly TextBlock _cpFig = Tt("", 10.5, Frozen(Tac.A(PaleC, .74)), Face, FontWeights.SemiBold);
        readonly List<FrameworkElement> _keep = new();
        readonly List<(FrameworkElement, FrameworkElement?)> _give = new();
        MeterMember? _m;
        double _bar;
        bool _expanded;

        public FrameworkElement Root => _root;

        public TargetRow()
        {
            // the rank, the callsign's line, the DPS, the share (as narrow as their widest figures: the callsign gets the room)
            foreach (var w in new[] { 30.0, double.NaN, 54, 45, 0, 0 })
                _root.ColumnDefinitions.Add(new ColumnDefinition { Width = double.IsNaN(w) ? new GridLength(1, GridUnitType.Star) : new GridLength(w) });
            _art = new TacSketch(Draw);
            Grid.SetColumnSpan(_art, 6);
            _root.Children.Add(_art);
            _rank.HorizontalAlignment = HorizontalAlignment.Center; _rank.VerticalAlignment = VerticalAlignment.Top; _rank.Margin = new Thickness(0, 3, 0, 0);
            Put(_rank, 0);
            _name.TextTrimming = TextTrimming.CharacterEllipsis;
            _nameLine.Children.Add(_name);
            Put(_nameLine, 1);
            Grid.SetColumnSpan(_nameLine, 2);   // the name may run on into the DPS's column, as far as its figure leaves free (Fit)
            foreach (var (t, col) in new[] { (_dps, 2), (_pct, 3), (_dmg, 4), (_crit, 5) })
            {
                t.HorizontalAlignment = HorizontalAlignment.Right; t.VerticalAlignment = VerticalAlignment.Top;
                t.Margin = new Thickness(0, t == _dps ? 1 : 4, t == _pct ? 4 : 6, 0);
                Put(t, col);
            }
            _dps.Tag = "row-dps";
            var cpWord = Tt(L.T("CP"), 7.5, PhosDim, Face, FontWeights.Bold); cpWord.Margin = new Thickness(0, 1, 2, 0);
            _cp.Children.Add(cpWord); _cp.Children.Add(_cpFig);
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
            _keep.Clear(); _give.Clear();
            FrameworkElement Add(FrameworkElement el) { _nameLine.Children.Add(el); return el; }
            // Combat Power beside the callsign once there's damage (before it, the figure itself is the CP). Short of room, in
            // turn: TOP DPS goes to its star, TOP HEALS folds into the heal figure (framed in mint), the YOU tag goes (the amber
            // frame says it); only then is the name cut
            FrameworkElement? you = null;
            if (m.Cp.Length > 0) { _cpFig.Text = m.Cp; _keep.Add(Add(_cp)); }
            if (m.You) you = Add(Mark(L.T("YOU"), Amber, Dark, solid: true));
            if (m.Deaths > 0) _keep.Add(Add(Mark(m.Deaths > 1 ? $"✖{m.Deaths}" : "✖", Red, Red, solid: false)));
            var mint = Frozen(MintC);
            if (m.Heal.Length > 0 && m.TopHeals)
            {
                var full = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Children = { HealFig(m.Heal), Plate(L.T("TOP HEALS"), mint, "top-heals") } };
                var framed = new StackPanel { Orientation = Orientation.Horizontal, Children = { Tt("✚", 9, mint, Face, FontWeights.Bold), HealFig(m.Heal, 3, plus: false) } };
                var shrt = new Border { BorderBrush = mint, BorderThickness = new Thickness(1), Background = Frozen(Tac.A(InkC0, .6)), Padding = new Thickness(2, 0, 3, 0), Margin = new Thickness(5, 1, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center, Child = framed, Tag = "top-heals", ToolTip = L.T("Top heals") };
                _give.Add((Add(full), Add(shrt)));
            }
            else if (m.Heal.Length > 0) _keep.Add(Add(HealFig(m.Heal)));
            else if (m.TopHeals) _give.Add((Add(Plate(L.T("TOP HEALS"), mint, "top-heals")), Add(Plate("✚", mint, "top-heals", L.T("Top heals")))));
            if (m.TopDps) _give.Insert(0, (Add(Plate(L.T("TOP DPS"), Amber, "top-dps")), Add(Plate("★", Amber, "top-dps", L.T("Top DPS")))));
            if (you != null) _give.Add((you, null));
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
            if (room <= 0) { Tac.FitName(_name, double.PositiveInfinity, _keep, _give); return; }
            Tac.FitName(_name, room + Tac.FreeLeft(_dps, _root.ColumnDefinitions[2].ActualWidth) - _nameLine.Margin.Left - 6, _keep, _give);
        }

        static readonly Color MintC = Color.FromRgb(0x5E, 0xF0, 0xA0);

        /// <summary>A healer's healing a second after the name ("+1.1K/s").</summary>
        static TextBlock HealFig(string heal, double left = 5, bool plus = true)
        {
            var h = Tt((plus ? "+" : "") + heal, 10.5, Frozen(MintC), Face, FontWeights.Bold);
            h.Tag = "heal-figures"; h.Margin = new Thickness(left, 1, 0, 0);
            return h;
        }

        /// <summary>A kill's plate (TOP DPS, TOP HEALS) as an outlined tag, or its glyph (★, ✚) when the line is short of room.</summary>
        static Border Plate(string s, Brush tone, string tag, string? tip = null) { var b = Mark(s, tone, tone, solid: false); b.Tag = tag; b.ToolTip = tip; return b; }

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
            using (var x = dia.Open()) { x.BeginFigure(new Point(x0 + 7, dy - 4.5), true, true); x.LineTo(new Point(x0 + 11.5, dy), true, false); x.LineTo(new Point(x0 + 7, dy + 4.5), true, false); x.LineTo(new Point(x0 + 2.5, dy), true, false); }
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
