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
        EnergyMark.After(left, 12, LiteDim, Face);   // Odyle Energy (Ui/EnergyMark.cs)
        g.Children.Add(left);
        var right = new StackPanel { Orientation = Orientation.Horizontal };
        if (word.Length > 0)
        {
            right.Children.Add(Label(word.ToUpperInvariant()));
            var gv = Tt(group, 15, Pale, Face, FontWeights.Bold); gv.Margin = new Thickness(6, 0, 0, 0); right.Children.Add(gv);
        }
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
    /// The meter's Combat Power, one call for the whole table (art review, 2026-11-20: a CP on some rows and not others reads
    /// ragged): when any row shown has no room for its CP (its marks at their glyphs), every row drops it.
    /// </summary>
    sealed class CpTable
    {
        readonly Dictionary<HoloRow, bool> _short = new();
        public bool Drop { get; private set; }

        public void Report(HoloRow row, bool shortOfRoom)
        {
            _short[row] = shortOfRoom;
            foreach (var gone in _short.Keys.Where(r => !r.Shown).ToList()) _short.Remove(gone);
            bool drop = _short.Values.Any(v => v);
            if (drop == Drop) return;
            Drop = drop;
            foreach (var r in _short.Keys.ToList()) if (r != row) r.Refit();
        }
    }

    /// <summary>
    /// One member as a bar of glass (Steven, 2026-11-17: "better looks that doesn't crowd everything and prioritizes the bar"):
    /// the rank in a hex in the class's colour (the podium's hex glows), then the bar, a cut-cornered pane the row's height from
    /// the hex to the DPS's end, lit in the class's colour to the member's part of the top (its end soft, never a hard edge
    /// through a letter, and short of the figure's room so it never runs through a digit), a dashed track along its foot. On it,
    /// one line (2026-11-20, compact): the name, Combat Power quiet beside it (on every row or none, <see cref="CpTable"/>), a
    /// healer's healing a second (the kill's top healer's in the green TOP HEALS chip when short of room), the marks; the DPS big
    /// at its right end; the share small after it; expanded, the damage and crit after the share. You: the row lit as cyan glass
    /// with a bright edge at its left. Short of room a chip goes to its glyph, then YOU (the skills card says them all), and only
    /// then does the name trim (never a 10-letter name): never a figure cut.
    /// </summary>
    sealed class HoloRow : IThemeRow, IThemeBarSpan
    {
        /// <summary>The row's height and pitch: one line; expanded, two (the damage and crit leave the words too little room for one).</summary>
        public const double H = 28, Pitch = 31, H2 = 34, Pitch2 = 37;
        readonly Grid _root = new() { Height = H };
        readonly TacSketch _art;
        readonly CpTable _table;
        readonly TextBlock _rank = Tt("", 11.5, White, Face, FontWeights.Bold), _name = Tt("", 14.5, White, Face, FontWeights.SemiBold),
            _dps = Tt("", 16, White, Face, FontWeights.Bold), _pct = Tt("", 12, Lite, Face, FontWeights.SemiBold), _dmg = Tt("", 13, Pale, Face, FontWeights.SemiBold), _crit = Tt("", 12, Lite, Face, FontWeights.SemiBold);
        /// <summary>The bar's line of words: the name, Combat Power, a healer's healing, the marks.</summary>
        readonly StackPanel _top = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(TextIn, 0, 0, 0) };
        /// <summary>Combat Power beside the name: "CP" in the small mono, the figure light cyan, quiet beside the DPS.</summary>
        readonly StackPanel _cp = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(7, 1, 0, 0) };
        readonly TextBlock _cpFig = Tt("", 11.5, Frozen(Tac.A(LiteC, .9)), Face, FontWeights.SemiBold);
        readonly StackPanel _under = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(TextIn, 0, 0, 2) };
        readonly TextBlock _none = new();
        readonly List<FrameworkElement> _keep = new(), _keepUnder = new();
        readonly List<(FrameworkElement, FrameworkElement?)> _give = new(), _giveUnder = new();
        readonly double _dmgW, _critW, _pctW;
        MeterMember? _m;
        double _bar;
        bool _expanded, _hasCp, _two;
        /// <summary>Where the words start on the pane.</summary>
        const double TextIn = 10;

        public FrameworkElement Root => _root;

        /// <summary>The light's full run: the pane from the hex (2 px past the rank column) to 8 px short of the figure's room (<see cref="FigRoom"/>), inset 3.5 px top and foot for you (2 for the rest).</summary>
        public (FrameworkElement On, Rect Track)? BarTrack
        {
            get
            {
                var cols = _root.ColumnDefinitions;
                if (_art.ActualWidth <= 0 || _m is not { } m) return null;
                double by = m.You ? 3.5 : 2, bw = cols[1].ActualWidth + cols[2].ActualWidth - 2;
                return (_art, new Rect(cols[0].ActualWidth + 2, by, Math.Max(0, bw - FigRoom()), _art.ActualHeight - 2 * by));
            }
        }

        /// <summary>The room kept clear of the bar's end for the DPS figure (its column, or a wider figure) and 8 px more.</summary>
        double FigRoom()
        {
            _dps.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return Math.Max(_root.ColumnDefinitions[2].ActualWidth, _dps.DesiredSize.Width + _dps.Margin.Right) + 8;
        }

        public bool Shown => _root.Parent != null;

        public HoloRow(CpTable table)
        {
            _table = table;
            foreach (var t in new[] { _dps, _pct, _dmg, _crit }) { t.Margin = new Thickness(0, 0, t == _dps ? 9 : t == _pct ? 7 : 3, 0); t.HorizontalAlignment = HorizontalAlignment.Right; t.VerticalAlignment = VerticalAlignment.Center; }
            // the figure columns sized for the widest real figure; the bar gets the rest
            _dmgW = Tac.FigCol(_dmg, "888.88M"); _critW = Tac.FigCol(_crit, "88%");
            foreach (var w in new[] { 30.0, double.NaN, Tac.FigCol(_dps, "888.8K"), (_pctW = Tac.FigCol(_pct, "88.8%", "100%", L.T("CP"))), 0, 0 })
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
            var cpWord = Tt(L.T("CP"), 8.5, LiteDim, Small, FontWeights.Bold); cpWord.Margin = new Thickness(0, 1, 3, 0);
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
            _rank.Text = m.Rank.ToString(CultureInfo.InvariantCulture);
            _name.Text = m.Name;
            // the name in a light tint of its class: it reads on its own class's light
            _name.Foreground = Frozen(Tac.Mix(m.NameColor, Colors.White, .4));
            _two = c.Expanded;
            _root.Height = _two ? H2 : H;
            while (_top.Children.Count > 1) _top.Children.RemoveAt(1);
            _under.Children.Clear();
            _keep.Clear(); _give.Clear(); _keepUnder.Clear(); _giveUnder.Clear();
            FrameworkElement Add(FrameworkElement el) { _top.Children.Add(el); return el; }
            FrameworkElement Under(FrameworkElement el) { el.Margin = new Thickness(_under.Children.Count > 0 ? 8 : 0, 0, 0, 0); _under.Children.Add(el); return el; }
            FrameworkElement Top(string s, Color c, string tag, string? tip = null) { var p = Chip(s, c, filled: false); p.Tag = tag; p.ToolTip = tip; p.Margin = new Thickness(6, 0, 0, 0); return p; }
            // quiet beside the name: Combat Power once there's damage (before it, the figure itself is the CP), a healer's healing
            _hasCp = m.Cp.Length > 0;
            _cp.Children[0].Visibility = m.Pvp ? Visibility.Collapsed : Visibility.Visible;   // the PvP panel's line is who killed, not CP
            if (_hasCp && _two) { _cpFig.Text = m.Cp; _giveUnder.Add((Under(_cp), null)); }
            else if (_hasCp) { _cpFig.Text = m.Cp; _cp.Margin = new Thickness(7, 1, 0, 0); Add(_cp); }
            // the kill's top healer: the figure and TOP HEALS, short of room the figure in the green chip (it says TOP HEALS)
            (FrameworkElement, FrameworkElement?)? topHeal = null;
            if (m.Heal.Length > 0 && _two) _keepUnder.Add(Under(HealFig(m.Heal)));
            else if (m.Heal.Length > 0 && m.TopHeals)
            {
                var full = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(7, 0, 0, 0) };
                full.Children.Add(HealFig(m.Heal)); full.Children.Add(Top(L.T("TOP HEALS"), GreenC, "top-heals"));
                var chip = HealChip(m.Heal); chip.ToolTip = L.T("Top heals"); chip.Margin = new Thickness(6, 0, 0, 0);
                topHeal = (Add(full), Add(chip));
            }
            else if (m.Heal.Length > 0) { var h = HealFig(m.Heal); h.Margin = new Thickness(7, 0, 0, 0); _keep.Add(Add(h)); }
            // the marks: deaths, a kill's chips (to their glyph when short of room), YOU (it goes: the lit glass says it)
            if (m.Deaths > 0) { var d = Tt(m.Deaths > 1 ? $"✖{m.Deaths}" : "✖", 11, Coral, Face, FontWeights.Bold); d.Margin = new Thickness(5, 0, 0, 0); _keep.Add(Add(d)); }
            if (m.TopDps) _give.Add((Add(Top(L.T("TOP DPS"), AmberC, "top-dps")), Add(Top("★", AmberC, "top-dps", L.T("Top DPS")))));
            if (topHeal is { } th) _give.Add(th);
            else if (m.TopHeals) _give.Add((Add(Top(L.T("TOP HEALS"), GreenC, "top-heals")), Add(Top("✚", GreenC, "top-heals", L.T("Top heals")))));
            if (m.You) { var y = Chip(L.T("YOU"), CyanC, filled: true); y.Margin = new Thickness(7, 0, 0, 0); _give.Add((Add(y), null)); }
            bool under = _under.Children.Count > 0;
            _under.Visibility = under ? Visibility.Visible : Visibility.Collapsed;
            _top.VerticalAlignment = under ? VerticalAlignment.Top : VerticalAlignment.Center;
            _top.Margin = new Thickness(TextIn, under ? 1 : 0, 0, 0);
            _dps.Text = m.Figure;
            _pct.Text = m.ShareText;
            // the PvP panel's Kill or Assist: its column as wide as the longer word, on its own rows only
            _root.ColumnDefinitions[3].Width = new GridLength(m.Pvp ? Math.Max(_pctW, Tac.FigCol(_pct, Encounter.PvpWords)) : _pctW);
            _dmg.Text = c.Expanded ? m.Damage : "";
            _crit.Text = c.Expanded ? m.Crit : "";
            Fit();
            SlideBar(_art, c, "bar", Tac.BarOf(m, c), v => { _bar = v; _art.InvalidateVisual(); });
            _art.InvalidateVisual();
        }

        /// <summary>Fitted again (the table's call on the CP changed).</summary>
        public void Refit() => Fit();

        void Fit()
        {
            var cols = _root.ColumnDefinitions;
            double room = cols[1].ActualWidth;
            if (room <= 0) { _cp.Visibility = _hasCp && (_two || !_table.Drop) ? Visibility.Visible : Visibility.Collapsed; Tac.FitName(_name, double.PositiveInfinity, _keep, _give); return; }
            // the words run on into the DPS's column as far as its figure leaves free
            _dps.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double free = Math.Min(cols[2].ActualWidth, cols[2].ActualWidth - _dps.DesiredSize.Width) - 5;
            double w = room + free - TextIn - 2, floor = Tac.NameFloor(_name);
            // the CP: does this row hold it whole, the name whole and its marks at their glyphs? The table decides for every row
            // (a row with no CP says so too, so a row that once was short no longer holds the table's CP off)
            // (two lines: the CP has its own line, never short of room)
            if (_two) { _table.Report(this, false); Tac.FitName(_name, w, _keep, _give, floor); Tac.FitName(_none, w, _keepUnder, _giveUnder, 0, 0); return; }
            double need = _hasCp ? Wd(_name) + Wd(_cp) + _keep.Sum(Wd) + _give.Sum(g => g.Item2 is { } s ? Wd(s) : 0) : 0;
            _table.Report(this, _hasCp && need > w);
            bool cp = _hasCp && !_table.Drop;
            _cp.Visibility = cp ? Visibility.Visible : Visibility.Collapsed;
            var keep = cp ? _keep.Append(_cp).ToList() : _keep;
            // the marks give way before the name trims (to its floor at the least: a 10-letter name whole)
            Tac.FitName(_name, w, keep, _give, floor);
        }

        static double Wd(FrameworkElement e)
        {
            var v = e.Visibility; e.Visibility = Visibility.Visible;
            e.ClearValue(FrameworkElement.MaxWidthProperty);
            e.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double w = e.DesiredSize.Width; e.Visibility = v;
            return w;
        }

        /// <summary>A healer's healing a second ("+1.1K/s").</summary>
        static TextBlock HealFig(string heal)
        {
            var h = Tt("+" + heal, 12, Green, Face, FontWeights.SemiBold);
            h.Tag = "heal-figures";
            return h;
        }

        /// <summary>The top healer's healing a second in the green cut-cornered chip (TOP HEALS's own outline): the chip is the mark.</summary>
        static Grid HealChip(string heal)
        {
            var g = Chip("", GreenC, filled: false);
            g.Tag = "top-heals";
            g.Children.RemoveAt(1);
            var h = HealFig(heal); h.Margin = new Thickness(4, 0, 4, 0);
            g.Children.Add(h);
            return g;
        }

        void Draw(DrawingContext dc, Size s)
        {
            if (_m is not { } m || s.Width < 40) return;
            var cols = _root.ColumnDefinitions;
            double h = s.Height, x0 = cols[0].ActualWidth;
            var tone = m.ClassColor;
            // you: the row lit as glass, a bright edge at its left, its corners cut
            if (m.You)
            {
                var r = new Rect(.5, .5, s.Width - 1, h - 1);
                dc.DrawGeometry(Tac.Grad(false, (Tac.A(CyanC, .2), 0), (Tac.A(CyanC, .05), 1)), Tac.P(Frozen(Tac.A(CyanC, .45)), 1), Tac.Cut(r, 6));
                dc.DrawRectangle(Cyan, null, new Rect(0, 6, 2, h - 12));
            }
            // the rank's hex in the class's colour, on the bar's middle; the podium's filled faintly and ringed in light
            double cx = x0 / 2 + 1, cy = h / 2, rr = 10;
            var pts = Enumerable.Range(0, 6).Select(i => { double a = Math.PI / 3 * i - Math.PI / 2; return new Point(cx + rr * Math.Cos(a), cy + rr * Math.Sin(a)); }).ToArray();
            var hex = new StreamGeometry();
            using (var x = hex.Open()) { x.BeginFigure(pts[0], true, true); foreach (var p in pts.Skip(1)) x.LineTo(p, true, false); }
            hex.Freeze();
            bool podium = m.Rank <= 3 && !m.CpOnly;
            if (podium) dc.DrawGeometry(null, Tac.P(Frozen(Tac.A(tone, .35)), 4, round: true), hex);
            dc.DrawGeometry(Frozen(Tac.A(Tac.Mix(DeepC, tone, podium ? .38 : .18), .95)), Tac.P(Frozen(Tac.Mix(tone, Colors.White, .2)), 1.3, round: true), hex);
            // the bar: a cut-cornered pane from the hex to the DPS's end, lit in the class's colour to the member's part of the top,
            // a bright head, a dashed track along its foot
            double bx = x0 + 2, by = m.You ? 3.5 : 2, bw = cols[1].ActualWidth + cols[2].ActualWidth - 2, bh = h - 2 * by;
            var pane = Tac.Cut(new Rect(bx + .5, by + .5, bw - 1, bh - 1), 5);
            dc.DrawGeometry(Frozen(Tac.A(DeepC, .55)), Tac.P(Frozen(Tac.A(CyanC, .26)), 1), pane);
            // the light runs to 8 px short of the figure's room (the DPS column, or a wider figure: "167,548" before a hit), the
            // same for every row, so a full bar ends clear of the digits; its last 6 px fade out, never a hard edge through a letter
            double figRoom = FigRoom();
            double fx = Math.Max(0, bw - figRoom) * Math.Clamp(_bar, 0, 1);
            if (fx > .5)
            {
                const double Soft = 6;
                dc.PushClip(pane);
                var mask = new LinearGradientBrush { MappingMode = BrushMappingMode.Absolute, StartPoint = new Point(bx + fx - Soft, 0), EndPoint = new Point(bx + fx, 0),
                    GradientStops = { new GradientStop(Colors.Black, 0), new GradientStop(Colors.Transparent, 1) } };
                mask.Freeze();
                dc.PushOpacityMask(mask);
                dc.DrawRectangle(Tac.Grad(false, (Tac.A(tone, .50), 0), (Tac.A(tone, .30), 1)), null, new Rect(bx, by, fx, bh));
                dc.DrawRectangle(Frozen(Tac.A(Tac.Mix(tone, Colors.White, .45), .9)), null, new Rect(bx, by, fx, 1.5));
                dc.DrawRectangle(Tac.Grad(false, (Tac.A(Colors.White, 0), 0), (Tac.A(Colors.White, .16), 1)), null, new Rect(Math.Max(bx, bx + fx - 26), by, Math.Min(26, fx), bh));
                dc.Pop();
                // the head: a short tick at the light's end, on the foot (clear of the words)
                var end = Frozen(Tac.Mix(tone, Colors.White, .6));
                dc.DrawRectangle(end, null, new Rect(bx + fx - 1.5, by + bh - 3, 1.5, 3));
                dc.Pop();
            }
            dc.DrawLine(Tac.P(Frozen(Tac.A(CyanC, .3)), 1, dash: new DashStyle(new double[] { 2, 2 }, 0)), new Point(bx + 6, by + bh - 2.5), new Point(bx + bw - 6, by + bh - 2.5));
        }
    }
}
