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
        EnergyMark.After(left, 11.5, PhosDim, Face);   // Odyle Energy (Ui/EnergyMark.cs)
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
        EnergyMark.DropIfWide(left, g, FightWidth - 40 - 12);   // a run's long "GROUP DPS (BOSS PULLS)" keeps its figure whole
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
    /// One member as a bar of the target list (Steven, 2026-11-17: "better looks that doesn't crowd everything and prioritizes
    /// the bar"): the rank in brackets ("[01]", amber for the lead), then the bar, the row's height and the rest of its width:
    /// a vector frame with a tick each tenth, filled in the class's colour to the member's share and ending in a bright tick. On
    /// it: the class's diamond and the callsign with its marks (a kill's star, deaths), Combat Power small under it (and a
    /// healer's healing a second), the DPS big at the right and the share small after it. You: amber corner brackets lock round
    /// the row. Short of room a plate goes to its glyph, then YOU, then the CP (the skills card says them all): never a figure cut.
    /// </summary>
    sealed class TargetRow : IThemeRow
    {
        public const double H = 33, Pitch = 36;
        readonly Grid _root = new() { Height = H };
        readonly TacSketch _art;
        readonly TextBlock _rank = Tt("", 12, PhosDim, Face, FontWeights.Bold), _name = Tt("", 13, Phos, Face, FontWeights.Bold),
            _dps = Tt("", 14.5, Pale, Face, FontWeights.Bold), _pct = Tt("", 11, PhosDim, Face, FontWeights.SemiBold), _dmg = Tt("", 12.5, Pale, Face, FontWeights.SemiBold), _crit = Tt("", 11, PhosDim, Face, FontWeights.SemiBold);
        /// <summary>The bar's two lines of words: the callsign and its marks; under it, Combat Power and a healer's healing.</summary>
        readonly StackPanel _top = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(TextIn, 2, 0, 0) };
        readonly StackPanel _under = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(TextIn, 0, 0, 3) };
        /// <summary>Combat Power under the callsign: a small dim "CP" tight on the figure, quiet beside the DPS.</summary>
        readonly StackPanel _cp = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        readonly TextBlock _cpFig = Tt("", 10.5, Frozen(Tac.A(PaleC, .78)), Face, FontWeights.SemiBold);
        readonly TextBlock _none = new();
        readonly List<FrameworkElement> _keep = new(), _keepUnder = new();
        readonly List<(FrameworkElement, FrameworkElement?)> _give = new(), _giveUnder = new();
        readonly double _dmgW, _critW, _pctW;
        MeterMember? _m;
        double _bar;
        bool _expanded;
        /// <summary>Where the words start on the bar: past the class's diamond.</summary>
        const double TextIn = 17;

        public FrameworkElement Root => _root;

        public TargetRow()
        {
            foreach (var t in new[] { _dps, _pct, _dmg, _crit }) { t.Margin = new Thickness(0, 0, t == _pct ? 6 : 7, 0); t.HorizontalAlignment = HorizontalAlignment.Right; t.VerticalAlignment = VerticalAlignment.Center; }
            // the figure columns sized for the widest real figure (a 54 px column cut the 1 off "199.9K"); the bar gets the rest
            _dmgW = Tac.FigCol(_dmg, "888.88M"); _critW = Tac.FigCol(_crit, "88%");
            foreach (var w in new[] { 32.0, double.NaN, Tac.FigCol(_dps, "888.8K"), (_pctW = Tac.FigCol(_pct, "88.8%", "100%", L.T("CP"))), 0, 0 })
                _root.ColumnDefinitions.Add(new ColumnDefinition { Width = double.IsNaN(w) ? new GridLength(1, GridUnitType.Star) : new GridLength(w) });
            _art = new TacSketch(Draw);
            Grid.SetColumnSpan(_art, 6);
            _root.Children.Add(_art);
            _rank.HorizontalAlignment = HorizontalAlignment.Center;
            Put(_rank, 0);
            _name.TextTrimming = TextTrimming.CharacterEllipsis;
            _top.Children.Add(_name);
            Put(_top, 1); Put(_under, 1);
            Grid.SetColumnSpan(_top, 2); Grid.SetColumnSpan(_under, 2);   // as far as the DPS leaves free (Fit)
            Put(_dps, 1); Grid.SetColumnSpan(_dps, 2);   // over its column and the bar's: a wider figure ("167,548" before a hit) takes room from the words, never a digit
            Put(_pct, 3); Put(_dmg, 4); Put(_crit, 5);
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
                _root.ColumnDefinitions[4].Width = new GridLength(c.Expanded ? _dmgW : 0);
                _root.ColumnDefinitions[5].Width = new GridLength(c.Expanded ? _critW : 0);
            }
            bool lead = m.Rank == 1 && !m.CpOnly;
            _rank.Text = m.Rank.ToString("00", CultureInfo.InvariantCulture);
            _rank.Foreground = lead || m.You ? Amber : PhosDim;
            _name.Text = m.Name;
            // the callsign in a light tint of its class: it reads on its own class's fill
            _name.Foreground = Frozen(Tac.Mix(m.NameColor, Colors.White, .35));
            while (_top.Children.Count > 1) _top.Children.RemoveAt(1);
            _under.Children.Clear();
            _keep.Clear(); _give.Clear(); _keepUnder.Clear(); _giveUnder.Clear();
            FrameworkElement Add(FrameworkElement el) { _top.Children.Add(el); return el; }
            FrameworkElement Under(FrameworkElement el) { if (_under.Children.Count > 0) el.Margin = new Thickness(7, el.Margin.Top, 0, 0); else el.Margin = new Thickness(0, el.Margin.Top, 0, 0); _under.Children.Add(el); return el; }
            var mint = Frozen(MintC);
            // the callsign's line: a kill's plates (to their glyph when short of room), deaths, YOU (it goes: the brackets say it)
            if (m.TopDps) _give.Add((Add(Plate(L.T("TOP DPS"), Amber, "top-dps")), Add(Plate("★", Amber, "top-dps", L.T("Top DPS")))));
            if (m.TopHeals) _give.Add((Add(Plate(L.T("TOP HEALS"), mint, "top-heals")), Add(Plate("✚", mint, "top-heals", L.T("Top heals")))));
            if (m.Deaths > 0) _keep.Add(Add(Mark(m.Deaths > 1 ? $"✖{m.Deaths}" : "✖", Red, Red, solid: false)));
            if (m.You) _give.Add((Add(Mark(L.T("YOU"), Amber, Dark, solid: true)), null));
            // under it, quiet: Combat Power once there's damage (before it, the figure itself is the CP), a healer's healing
            _cp.Children[0].Visibility = m.Pvp ? Visibility.Collapsed : Visibility.Visible;   // the PvP panel's line is who killed, not CP
            if (m.Cp.Length > 0) { _cpFig.Text = m.Cp; _giveUnder.Add((Under(_cp), null)); }
            if (m.Heal.Length > 0) _keepUnder.Add(Under(HealFig(m.Heal)));
            // nothing under it (before the first hit): the callsign in the bar's middle
            bool two = _under.Children.Count > 0;
            _top.VerticalAlignment = two ? VerticalAlignment.Top : VerticalAlignment.Center;
            _top.Margin = new Thickness(TextIn, two ? 2 : 0, 0, 0);
            _dps.Text = m.Figure;
            _dps.Foreground = m.You ? Amber : Pale;
            _pct.Text = m.ShareText;
            // the PvP panel's Kill or Assist: its column as wide as the longer word, on its own rows only
            _root.ColumnDefinitions[3].Width = new GridLength(m.Pvp ? Math.Max(_pctW, Tac.FigCol(_pct, Encounter.PvpWords)) : _pctW);
            _dmg.Text = c.Expanded ? m.Damage : "";
            _crit.Text = c.Expanded ? m.Crit : "";
            Fit();
            SlideBar(_art, c, "bar", Tac.BarOf(m, c), v => { _bar = v; _art.InvalidateVisual(); });
            _art.InvalidateVisual();
        }

        void Fit()
        {
            var cols = _root.ColumnDefinitions;
            double room = cols[1].ActualWidth;
            if (room <= 0) { Tac.FitName(_name, double.PositiveInfinity, _keep, _give); return; }
            // the words run on into the DPS's column as far as its figure leaves free
            _dps.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double free = Math.Min(cols[2].ActualWidth, cols[2].ActualWidth - _dps.DesiredSize.Width) - 6;
            double w = room + free - TextIn - 4, floor = Tac.NameFloor(_name);
            Tac.FitName(_name, w, _keep, _give, floor, floor);
            Tac.FitName(_none, w, _keepUnder, _giveUnder, 0, 0);
        }

        static readonly Color MintC = Color.FromRgb(0x5E, 0xF0, 0xA0);

        /// <summary>A healer's healing a second ("+1.1K/s").</summary>
        static TextBlock HealFig(string heal)
        {
            var h = Tt("+" + heal, 10.5, Frozen(MintC), Face, FontWeights.Bold);
            h.Tag = "heal-figures";
            return h;
        }

        /// <summary>A kill's plate (TOP DPS, TOP HEALS) as an outlined tag, or its glyph (★, ✚) when the line is short of room.</summary>
        static Border Plate(string s, Brush tone, string tag, string? tip = null) { var b = Mark(s, tone, tone, solid: false); b.Tag = tag; b.ToolTip = tip; return b; }

        /// <summary>A tag on the callsign's line: solid (YOU) or an outline (a death, a kill's plates).</summary>
        static Border Mark(string s, Brush line, Brush fg, bool solid) => new()
        {
            Background = solid ? line : Frozen(Tac.A(InkC0, .7)), BorderBrush = line, BorderThickness = new Thickness(1), Padding = new Thickness(4, 0, 4, 0), Margin = new Thickness(6, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center, Child = Tt(s, 9.5, fg, Face, FontWeights.Bold),
        };

        void Draw(DrawingContext dc, Size s)
        {
            if (_m is not { } m || s.Width < 40) return;
            var cols = _root.ColumnDefinitions;
            double x0 = cols[0].ActualWidth, h = s.Height;
            var tone = m.ClassColor;
            var light = Tac.Mix(tone, Colors.White, .3);
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
            // the rank's brackets, on the bar's middle
            double rx = x0 / 2, ry = h / 2;
            var rb = Tac.P(m.Rank == 1 || m.You ? Amber : PhosFaint, 1.2);
            dc.DrawLine(rb, new Point(rx - 13, ry - 7), new Point(rx - 13, ry + 7)); dc.DrawLine(rb, new Point(rx - 13, ry - 7), new Point(rx - 10, ry - 7)); dc.DrawLine(rb, new Point(rx - 13, ry + 7), new Point(rx - 10, ry + 7));
            dc.DrawLine(rb, new Point(rx + 13, ry - 7), new Point(rx + 13, ry + 7)); dc.DrawLine(rb, new Point(rx + 13, ry - 7), new Point(rx + 10, ry - 7)); dc.DrawLine(rb, new Point(rx + 13, ry + 7), new Point(rx + 10, ry + 7));
            // the bar: a vector frame from the rank to the DPS's end (the share and the expanded figures small after it), a tick
            // each tenth along its foot, filled in the class's colour to the member's part of the top, a bright line along the
            // fill's top and a light tick at its end
            double bx = x0, by = m.You ? 3 : 1.5, bw = cols[1].ActualWidth + cols[2].ActualWidth - 1, bh = h - 2 * by;
            dc.DrawRectangle(Frozen(Tac.A(InkC0, .45)), Tac.P(PhosFaint, 1), new Rect(bx + .5, by + .5, bw - 1, bh - 1));
            double fx = bw * Math.Clamp(_bar, 0, 1);
            if (fx > .5)
            {
                dc.DrawRectangle(Tac.Grad(false, (Tac.A(tone, .52), 0), (Tac.A(tone, .34), 1)), null, new Rect(bx, by, fx, bh));
                dc.DrawRectangle(Frozen(light), null, new Rect(bx, by, fx, 1.5));
                // its end: a bright tick at the top and the foot (a line the bar's height ran through the figures)
                var end = Frozen(Tac.Mix(tone, Colors.White, .7));
                dc.DrawRectangle(end, null, new Rect(bx + fx - 1.5, by, 1.5, 5)); dc.DrawRectangle(end, null, new Rect(bx + fx - 1.5, by + bh - 5, 1.5, 5));
            }
            for (int i = 1; i < 10; i++) { double tx = Math.Round(bx + bw * i / 10); dc.DrawRectangle(i == 5 ? PhosDim : PhosFaint, null, new Rect(tx, by + bh - (i == 5 ? 4 : 3), 1, i == 5 ? 4 : 3)); }
            // the class's diamond at the callsign's line
            double dy = _under.Children.Count > 0 ? by + 9.5 : h / 2;
            var dia = new StreamGeometry();
            using (var x = dia.Open()) { x.BeginFigure(new Point(bx + 8.5, dy - 4.5), true, true); x.LineTo(new Point(bx + 13, dy), true, false); x.LineTo(new Point(bx + 8.5, dy + 4.5), true, false); x.LineTo(new Point(bx + 4, dy), true, false); }
            dia.Freeze();
            dc.DrawGeometry(Frozen(light), Tac.P(Frozen(Tac.A(Colors.White, .55)), .8), dia);
        }
    }
}
