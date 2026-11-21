using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using KinahLedger.Parser;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

sealed partial class HudFinals
{
    /// <summary>The fight's foot: "TOTAL 2.03M" at the left, "GROUP DPS 12.7K" at the right (gold caps, white figures), the AVG chip on a held average.</summary>
    FrameworkElement Foot(Encounter e, DateTime now)
    {
        var (total, word, group, held) = FootOf(e, now);
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        TextBlock Pair(string w, string fig)
        {
            var t = T("", 13.5, White, FontWeights.SemiBold);
            t.Inlines.Add(new System.Windows.Documents.Run(Caps(w) + "  ") { FontSize = 11, FontStretch = FontStretches.Condensed, Foreground = AuSoft });
            t.Inlines.Add(new System.Windows.Documents.Run(fig));
            return t;
        }
        var tp = Pair(L.T("Total"), total);
        EnergyMark.After(tp, 12, Soft);   // Odyle Energy (Ui/EnergyMark.cs)
        g.Children.Add(tp);
        var right = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 0, 0, 0) };
        if (word.Length > 0) right.Children.Add(Pair(word, group));
        if (held)
        {
            var avg = Chip(L.T("AVG"), size: 10); avg.Tag = "avg-badge"; avg.ToolTip = L.T(Encounter.AvgBadge); avg.Margin = new Thickness(6, 0, 0, 0);
            right.Children.Add(avg);
        }
        Grid.SetColumn(right, 1); g.Children.Add(right);
        return AsFoot(g, e, now);
    }

    /// <summary>The line between the party and you pinned below it: "2 MORE" in quiet gold caps between two short gold rules.</summary>
    static FrameworkElement GapLine(int left)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        FrameworkElement Rule() => new Border { Width = 26, Height = 1, Background = Mod.Br(AuC, .4), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 6, 0) };
        sp.Children.Add(Rule());
        sp.Children.Add(Label(left > 0 ? L.F("{0} more", left) : "·", 10, AuSoft));
        sp.Children.Add(Rule());
        return sp;
    }

    /// <summary>
    /// The rows' shared fit: a figure column holds or goes for every row at once (a column that jumps from row to row can't be
    /// read down). Level 0 shows every column; 1 lets the share go; 2, on the expanded meter, the crit rate too.
    /// </summary>
    sealed class FinalsFit
    {
        readonly Dictionary<FinalsRow, int> _need = new();
        public int Level { get; private set; }

        public void Report(FinalsRow row, int need)
        {
            _need[row] = need;
            foreach (var gone in _need.Keys.Where(r => r.Root.Parent == null && r != row).ToList()) _need.Remove(gone);
            int level = _need.Values.DefaultIfEmpty(0).Max();
            if (level == Level) return;
            Level = level;
            foreach (var r in _need.Keys.Where(r => r != row).ToList()) r.Layout();
        }
    }

    /// <summary>
    /// One member on the stage: a graphite tile; their rank in a small black seed box (the leader's rimmed in gold with the
    /// trophy cup standing in it); the bar, as long as their part of the top, graphite lit from inside in their class's colour
    /// (a glow along its top and foot, deepening to a bright leading edge); the leader's bar brushed gold. Over it in white
    /// with a tight shade: the class's art in a ring (gold for the leader), the name, the Combat Power, the marks (★ DPS,
    /// ✚ Heal, YOU in gold, deaths), a healer's healing a second in mint, the DPS big and the share. Crowding (Steven,
    /// 2026-11-17): the CP goes first, then the marks shorten to their glyphs, then the name trims (never below ten letters),
    /// then the share column goes for every row; a figure is never cut.
    /// </summary>
    sealed class FinalsRow : IThemeRow
    {
        public const double H = 26, Pitch = 29;
        const double PadX = 4, RankW = 20, Gap1 = 6, IconW = 18, Gap2 = 6, ShareW = 36, DmgW = 56, CritW = 36, PadR = 8;
        /// <summary>Where the bar starts: after the seed box.</summary>
        const double BarX = PadX + RankW + 3;
        readonly FinalsFit _fit;
        readonly Grid _root = new() { Height = H };
        readonly TacSketch _art;
        /// <summary>The glint across the row: a band moved by its transform (a sweep, a climb), never a redraw.</summary>
        readonly Band _shine = new(AuPaleC, .55, 26, -28) { Margin = new Thickness(1) };
        readonly Grid _words = new();
        readonly TextBlock _rank = T("", 13, White, FontWeights.SemiBold, cond: true), _name = T("", 14, White, FontWeights.SemiBold), _cp = T("", 10.5, Mod.Br(Colors.White, .7), FontWeights.Normal),
            _heal = T("", 11.5, Mint2, FontWeights.SemiBold), _dps = T("", 17.5, White, FontWeights.SemiBold), _share = T("", 11, Mod.Br(Colors.White, .8), FontWeights.Normal),
            _dmg = T("", 12, Soft, FontWeights.Normal), _crit = T("", 11, Quiet, FontWeights.Normal);
        readonly StackPanel _nameLine = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, ClipToBounds = false };
        readonly Border _icon = new() { Width = IconW, Height = IconW, CornerRadius = new CornerRadius(IconW / 2), BorderThickness = new Thickness(1.2), VerticalAlignment = VerticalAlignment.Center, Background = Mod.Br(Black0, .6) };
        readonly Trophy _trophy = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 3, 0, 0), Visibility = Visibility.Collapsed };
        readonly List<FrameworkElement> _keep = new();
        readonly List<(FrameworkElement Full, FrameworkElement Short)> _give = new();
        MeterMember? _m;
        string _iconCls = "\0";
        double _bar;
        bool _expanded, _lead;
        int _lastRank;

        public FrameworkElement Root => _root;
        /// <summary>This row is the leader's (rank 1 with damage done): the gold bar and the trophy.</summary>
        public bool Leads => _lead;

        public FinalsRow(FinalsFit fit)
        {
            _fit = fit;
            foreach (var w in new[] { PadX, RankW, Gap1, IconW, Gap2, double.NaN, 0, 0, ShareW, 0, 0, PadR })
                _words.ColumnDefinitions.Add(new ColumnDefinition { Width = double.IsNaN(w) ? new GridLength(1, GridUnitType.Star) : new GridLength(w) });
            _words.ColumnDefinitions[6].Width = GridLength.Auto; _words.ColumnDefinitions[7].Width = GridLength.Auto;
            _art = new TacSketch(Draw);
            _root.Children.Add(_art); _root.Children.Add(_shine); _root.Children.Add(_words);
            _rank.HorizontalAlignment = HorizontalAlignment.Center;
            Put(_rank, 1); Put(_trophy, 1); Put(_icon, 3);
            _name.TextTrimming = TextTrimming.CharacterEllipsis;
            _nameLine.Children.Add(_name);
            Put(_nameLine, 5);
            _heal.Margin = new Thickness(6, 0, 0, 0); _heal.Tag = "heal-figures"; Put(_heal, 6);
            _dps.Margin = new Thickness(6, 0, 0, 0); _dps.HorizontalAlignment = HorizontalAlignment.Right; _dps.Tag = "row-dps"; Put(_dps, 7);
            _share.Tag = "row-share"; _cp.Tag = "cp";
            foreach (var (t, col) in new[] { (_share, 8), (_dmg, 9), (_crit, 10) }) { t.HorizontalAlignment = HorizontalAlignment.Right; t.Margin = new Thickness(0, 1, 0, 0); Put(t, col); }
            _cp.Margin = new Thickness(6, 1, 0, 0);
            _root.SizeChanged += (_, _) => Fit();
        }

        void Put(FrameworkElement el, int col) { Grid.SetColumn(el, col); _words.Children.Add(el); }

        public void Set(MeterMember m, MeterContext c, bool fresh)
        {
            _m = m;
            if (_expanded != c.Expanded)
            {
                _expanded = c.Expanded;
                _words.ColumnDefinitions[9].Width = new GridLength(c.Expanded ? DmgW : 0);
            }
            bool lead = m.Rank == 1 && !m.CpOnly && !m.Pvp;
            _lead = lead;
            // the tight shade only where the words sit on gold (the graphite bars are dark enough without it)
            if (lead != (_words.Effect != null)) _words.Effect = lead ? Shade() : null;
            _rank.Text = m.Rank.ToString(CultureInfo.InvariantCulture);
            _rank.Visibility = lead ? Visibility.Collapsed : Visibility.Visible;
            _trophy.Visibility = lead ? Visibility.Visible : Visibility.Collapsed;
            _icon.BorderBrush = lead ? GoldMetal : Mod.Br(Colors.White, .3);
            if (_iconCls != m.Class)
            {
                _iconCls = m.Class;
                var ic = ThemeText.Icon(m.Class, IconW - 4, m.Name);
                ic.HorizontalAlignment = HorizontalAlignment.Center; ic.VerticalAlignment = VerticalAlignment.Center;
                _icon.Child = ic;
            }
            _name.Text = m.Name;
            while (_nameLine.Children.Count > 1) _nameLine.Children.RemoveAt(1);
            _keep.Clear(); _give.Clear();
            _cp.Text = m.Cp;
            if (m.Cp.Length > 0) _nameLine.Children.Add(_cp);
            FrameworkElement Add(FrameworkElement el) { el.Margin = new Thickness(5, 0, 0, 0); _nameLine.Children.Add(el); return el; }
            if (m.TopDps) _give.Add((Add(Mark("★ " + L.T("DPS"), false)), Add(Mark("★", false, L.T("Top DPS")))));
            if (m.TopHeals) _give.Add((Add(Mark("✚ " + L.T("Heal"), true)), Add(Mark("✚", true, L.T("Top heals")))));
            if (m.You) { var y = Chip(L.T("YOU"), filled: true); y.Tag = "you"; _keep.Add(Add(y)); }
            if (m.Deaths > 0) { var d = T("✕" + m.Deaths.ToString(CultureInfo.InvariantCulture), 11.5, Coral, FontWeights.SemiBold); d.Tag = "deaths"; d.ToolTip = L.F(m.Deaths == 1 ? "{0} death" : "{0} deaths", m.Deaths); _keep.Add(Add(d)); }
            _heal.Text = m.Heal.Length > 0 ? "+" + m.Heal : "";
            _heal.Visibility = m.Heal.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            _dps.Text = m.Figure;
            _dps.FontSize = m.CpOnly ? 14.5 : 17.5;
            _dps.Foreground = lead ? AuPale : White;
            _share.Text = m.ShareText;
            _share.Foreground = lead ? AuPale : Mod.Br(Colors.White, .8);
            _dmg.Text = c.Expanded ? m.Damage : "";
            _crit.Text = c.Expanded ? m.Crit : "";
            Fit();
            SlideBar(_art, c, "bar", m.CpOnly ? 0 : c.E.BarOf(m.F), v => { _bar = v; _art.InvalidateVisual(); });
            _art.InvalidateVisual();
            // a row that climbed: a glint runs along it as it glides up into its place
            if (!fresh && c.Moves && _lastRank > 0 && m.Rank < _lastRank && !lead)
                Motion.Play(_shine, 650, t => Glint(Seg(t, .05, 1)), () => Glint(-1));
            _lastRank = m.Rank;
        }

        /// <summary>The trophy rising into the seed box (the lead just taken, or the fight just begun).</summary>
        public void Crown(double ms) { if (_lead) _trophy.Rise(ms); }

        /// <summary>The gold glint's place along the row (0 to 1; outside: none).</summary>
        public void Glint(double at) => _shine.At(at);

        /// <summary>A mark: ★ DPS as a black chip rimmed in gold, ✚ Heal rimmed in mint.</summary>
        static Border Mark(string s, bool heal, string? tip = null)
        {
            var m = heal ? Chip(Caps(s), Frozen(Mod.Mix(MintC, Colors.White, .35)), MintC) : Chip(Caps(s));
            m.Tag = heal ? "top-heals" : "top-dps";
            m.ToolTip = tip;
            if (s.Length == 1) m.Padding = new Thickness(3, 0, 3, 1);
            return m;
        }

        /// <summary>The name line's room with every column shown (the row's width less every other column).</summary>
        double Room()
        {
            double w = _root.ActualWidth;
            if (w <= 0) return double.NaN;
            double other = PadX + RankW + Gap1 + IconW + Gap2 + Mod.W(_dps) + _dps.Margin.Left + (_heal.Visibility == Visibility.Visible ? Mod.W(_heal) + _heal.Margin.Left : 0)
                + (_expanded ? DmgW + CritW : 0) + PadR;
            return w - other - ShareW;
        }

        /// <summary>The room a fit level gives back: the share's column, then (expanded) the crit rate's.</summary>
        double Freed(int level) => (level >= 1 ? ShareW : 0) + (level >= 2 && _expanded ? CritW : 0);

        /// <summary>The narrowest the name goes: its first ten letters and the ellipsis (a shorter name, whole).</summary>
        double NameMin()
        {
            var s = _name.Text;
            if (s.Length <= 10) return Mod.W(_name);
            var t = T(s[..10] + "…", _name.FontSize, White, _name.FontWeight);
            return Mod.W(t) + 1;
        }

        void Fit()
        {
            double room = Room();
            if (double.IsNaN(room)) return;
            _name.ClearValue(FrameworkElement.MaxWidthProperty);
            double least = Math.Min(Mod.W(_name), NameMin()) + _keep.Sum(k => Mod.W(k) + k.Margin.Left) + _give.Sum(g => Mod.W(g.Short) + g.Short.Margin.Left);
            int need = 0;
            while (need < 2 && least > room + Freed(need) + .5) need++;
            _fit.Report(this, need);
            Layout();
        }

        /// <summary>The row laid out at the meter's fit level: its columns, the CP, the marks' forms and the name's width.</summary>
        internal void Layout()
        {
            double room = Room();
            if (double.IsNaN(room)) return;
            int level = _fit.Level;
            _words.ColumnDefinitions[8].Width = new GridLength(level >= 1 ? 0 : ShareW);
            _share.Visibility = level >= 1 ? Visibility.Collapsed : Visibility.Visible;
            _words.ColumnDefinitions[10].Width = new GridLength(_expanded && level < 2 ? CritW : 0);
            _crit.Visibility = level >= 2 ? Visibility.Collapsed : Visibility.Visible;
            room += Freed(level);
            _name.ClearValue(FrameworkElement.MaxWidthProperty);
            double nw = Mod.W(_name), keep = _keep.Sum(k => Mod.W(k) + k.Margin.Left);
            double cpw = _cp.Text.Length > 0 ? Mod.W(_cp) + _cp.Margin.Left : 0;
            double full = _give.Sum(g => Mod.W(g.Full) + g.Full.Margin.Left), shorts = _give.Sum(g => Mod.W(g.Short) + g.Short.Margin.Left);
            // the CP goes first (and stays gone once a column has gone), then the marks shorten, then the name trims
            bool cp = level == 0 && cpw > 0 && nw + cpw + keep + full <= room;
            bool longMarks = nw + keep + full <= room;
            _cp.Visibility = cp ? Visibility.Visible : Visibility.Collapsed;
            foreach (var (f, sh) in _give) { f.Visibility = longMarks ? Visibility.Visible : Visibility.Collapsed; sh.Visibility = longMarks ? Visibility.Collapsed : Visibility.Visible; }
            double marks = keep + (longMarks ? full : shorts) + (cp ? cpw : 0);
            _name.MaxWidth = Math.Max(Math.Min(nw, NameMin()), room - marks);
        }

        void Draw(DrawingContext dc, Size s)
        {
            if (_m is not { } m || s.Width < 40) return;
            double h = s.Height;
            var r = new Rect(.5, .5, s.Width - 1, h - 1);
            var tile = Mod.Pill(r, 3);
            var tone = m.ClassColor;
            // the graphite tile, a breath lighter at its top
            dc.DrawGeometry(Mod.Grad(true, (Color.FromRgb(0x22, 0x22, 0x26), 0), (Color.FromRgb(0x17, 0x17, 0x1A), 1)), null, tile);
            // the bar: from after the seed box, as long as the member's part of the top
            var lane = new Rect(BarX, r.Y + 1, Math.Max(0, r.Right - 1 - BarX), r.Height - 2);
            double fx = lane.Width * Math.Clamp(_bar, 0, 1);
            if (fx > 1)
            {
                var fill = new Rect(lane.X, lane.Y, fx, lane.Height);
                var shape = Mod.Pill(fill, 2);
                dc.PushClip(shape);
                if (_lead)
                {
                    // brushed gold: a deep bronze at the ends and a lit band where the lathe caught the light, bevelled top and foot
                    var gold = new LinearGradientBrush { MappingMode = BrushMappingMode.Absolute, StartPoint = new Point(fill.X, 0), EndPoint = new Point(fill.Right, 0) };
                    foreach (var (c, at) in new[] { (Color.FromRgb(0x4A, 0x36, 0x12), 0.0), (Color.FromRgb(0x86, 0x63, 0x22), .22), (Color.FromRgb(0xB5, 0x8C, 0x3A), .5), (Color.FromRgb(0x92, 0x6C, 0x28), .7), (Color.FromRgb(0x5E, 0x45, 0x18), 1.0) })
                        gold.GradientStops.Add(new GradientStop(c, at));
                    gold.Freeze();
                    dc.DrawRectangle(gold, null, fill);
                    dc.PushOpacity(.7); dc.DrawRectangle(Brushed, null, fill); dc.Pop();
                    dc.DrawRectangle(Mod.Grad(true, (Mod.A(Colors.White, .2), 0), (Mod.A(Colors.White, 0), .42), (Mod.A(Colors.Black, 0), .6), (Mod.A(Colors.Black, .3), 1)), null, fill);
                    dc.DrawRectangle(Mod.Br(AuPaleC, .75), null, new Rect(fill.X, fill.Y, fx, 1));
                }
                else
                {
                    // graphite lit from inside in the class's colour: deepening toward the leading edge, a glow along top and foot
                    dc.DrawRectangle(Mod.Grad(false, (Color.FromRgb(0x2A, 0x2A, 0x30), 0), (Color.FromRgb(0x2F, 0x2F, 0x36), 1)), null, fill);
                    dc.DrawRectangle(Mod.Grad(false, (Mod.A(tone, .12), 0), (Mod.A(tone, .34), .75), (Mod.A(tone, .5), 1)), null, fill);
                    dc.DrawRectangle(Mod.Grad(true, (Mod.A(tone, .55), 0), (Mod.A(tone, 0), .3), (Mod.A(tone, 0), .7), (Mod.A(tone, .45), 1)), null, fill);
                    double ex = fill.Right;
                    // the leading edge: a soft glow and a slim line, quiet enough that a figure over it still reads
                    dc.DrawRectangle(Mod.Grad(false, (Mod.A(tone, 0), 0), (Mod.A(Mod.Mix(tone, Colors.White, .2), .38), 1)), null, new Rect(Math.Max(fill.X, ex - 14), fill.Y, Math.Min(14, fx), fill.Height));
                    dc.DrawRectangle(Mod.Br(Mod.Mix(tone, Colors.White, .2), .7), null, new Rect(Math.Max(fill.X, ex - 1.5), fill.Y, Math.Min(1.5, fx), fill.Height));
                }
                dc.Pop();
            }
            // a healer's share of the group's healing as a mint line along the foot (Settings' heal bars)
            if (ParserRows.HealBarsPref && m.HealShare > 0)
                dc.DrawRectangle(Mod.Br(MintC, .9), null, new Rect(lane.X, lane.Bottom - 2.5, Math.Max(3, lane.Width * Math.Clamp(m.HealShare, 0, 1)), 2.5));
            // the seed box: black, the leader's rimmed in brushed gold
            var seed = new Rect(PadX + .5, r.Y + 3, RankW - 1, r.Height - 6);
            dc.DrawGeometry(Mod.Br(Black0, .92), _lead ? Mod.P(GoldMetal, 1.3) : Mod.P(Mod.Br(AuC, .3), 1), Mod.Pill(seed, 2));
            // the tile's edge: yours in gold, the rest a faint line
            dc.DrawGeometry(null, Mod.P(m.You ? Mod.Br(AuLightC, .8) : Mod.Br(Colors.White, .07), m.You ? 1.2 : 1), tile);
        }
    }
}
