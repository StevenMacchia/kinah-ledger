using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using KinahLedger.Parser;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

sealed partial class HudModernGlass
{
    /// <summary>The meter's foot: "Total 622.4M" at the left, "Group DPS 999K" (or your best or peak) at the right, the AVG mark after it.</summary>
    FrameworkElement Foot(Encounter e, DateTime now)
    {
        var (total, word, group, held) = FootOf(e, now);
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = Tt("", 12, Soft, FontWeights.Medium);
        left.Inlines.Add(new System.Windows.Documents.Run(L.T("Total") + " "));
        left.Inlines.Add(new System.Windows.Documents.Run(total) { FontWeight = FontWeights.Bold, Foreground = White });
        g.Children.Add(left);
        var right = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 0, 0, 0) };
        if (word.Length > 0)
        {
            var rt = Tt("", 12, Soft, FontWeights.Medium);
            rt.Inlines.Add(new System.Windows.Documents.Run(word + " "));
            rt.Inlines.Add(new System.Windows.Documents.Run(group) { FontWeight = FontWeights.Bold, Foreground = White });
            right.Children.Add(rt);
        }
        if (held)
        {
            var chip = Chip(L.T(Encounter.AvgBadge), Mod.Br(Colors.White, .18), White, 9);
            chip.Tag = "avg-badge"; chip.Margin = new Thickness(6, 0, 0, 0);
            right.Children.Add(chip);
        }
        Grid.SetColumn(right, 1); g.Children.Add(right);
        return AsFoot(g, e, now);
    }

    /// <summary>The line between the party and you pinned below it: "2 MORE", quiet and centred.</summary>
    static FrameworkElement GapLine(int left)
    {
        var t = Tt(left > 0 ? L.F("{0} more", left).ToUpperInvariant() : "···", 10, Faint, FontWeights.SemiBold);
        t.HorizontalAlignment = HorizontalAlignment.Center;
        System.Windows.Documents.Typography.SetCapitals(t, FontCapitals.Normal);
        return t;
    }

    /// <summary>
    /// The rows' shared fit: a figure column holds or goes for every row at once (a column that jumps from row to row can't
    /// be read down). Level 0 shows every column; 1 lets the share go; 2, on the expanded meter, the crit rate too. Each row
    /// says the level it needs, and all are laid out again when the meter's level changes.
    /// </summary>
    sealed class GlassFit
    {
        readonly Dictionary<GlassRow, int> _need = new();
        public int Level { get; private set; }

        public void Report(GlassRow row, int need)
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
    /// One member as a lit glass pill: a faint glass ground with a hairline rim (yours brighter), filled to their share in their
    /// class's colour, deepening towards a bright leading edge with a line of light along its top. On it: the rank, the class's
    /// emblem in a light ring, the name in white with the Combat Power after it and the marks (★ DPS, ✚ Heal, YOU, deaths), a
    /// healer's healing a second in green, the DPS big in white and the share. Crowding (Steven, 2026-11-17): the CP goes
    /// first, then the marks shorten to their glyphs, then the name trims (never below ten letters), then the share column
    /// goes for every row; a figure is never cut.
    /// </summary>
    sealed class GlassRow : IThemeRow
    {
        public const double H = 32, Pitch = 38;
        const double PadX = 10, RankW = 14, Gap1 = 4, IconW = 20, Gap2 = 7, ShareW = 37, DmgW = 54, CritW = 36;
        readonly GlassFit _fit;
        readonly Grid _root = new() { Height = H };
        readonly TacSketch _art;
        readonly TextBlock _rank = Tt("", 12.5, White, FontWeights.Bold), _name = Tt("", 13.5, White, FontWeights.SemiBold), _cp = Tt("", 10.5, Mod.Br(Colors.White, .58), FontWeights.Medium),
            _heal = Tt("", 10.5, Green, FontWeights.Bold), _dps = Tt("", 17, White, FontWeights.Bold), _share = Tt("", 10.5, Mod.Br(Colors.White, .75), FontWeights.SemiBold),
            _dmg = Tt("", 12, Soft, FontWeights.SemiBold), _crit = Tt("", 11, Dim, FontWeights.SemiBold);
        readonly StackPanel _nameLine = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, ClipToBounds = false };
        readonly Border _icon = new() { Width = IconW, Height = IconW, CornerRadius = new CornerRadius(IconW / 2), BorderThickness = new Thickness(1.5), BorderBrush = Mod.Br(Colors.White, .35), VerticalAlignment = VerticalAlignment.Center };
        readonly List<FrameworkElement> _keep = new();
        readonly List<(FrameworkElement Full, FrameworkElement Short)> _give = new();
        MeterMember? _m;
        string _iconCls = "\0";
        double _bar;
        bool _expanded;

        public FrameworkElement Root => _root;

        public GlassRow(GlassFit fit)
        {
            _fit = fit;
            foreach (var w in new[] { PadX, RankW, Gap1, IconW, Gap2, double.NaN, 0, 0, ShareW, 0, 0, PadX })
                _root.ColumnDefinitions.Add(new ColumnDefinition { Width = double.IsNaN(w) ? new GridLength(1, GridUnitType.Star) : new GridLength(w) });
            _root.ColumnDefinitions[6].Width = GridLength.Auto; _root.ColumnDefinitions[7].Width = GridLength.Auto;
            _art = new TacSketch(Draw);
            Grid.SetColumnSpan(_art, 12);
            _root.Children.Add(_art);
            _rank.HorizontalAlignment = HorizontalAlignment.Center;
            Put(_rank, 1); Put(_icon, 3);
            _name.TextTrimming = TextTrimming.CharacterEllipsis;
            _nameLine.Children.Add(_name);
            Put(_nameLine, 5);
            _heal.Margin = new Thickness(6, 0, 0, 0); _heal.Tag = "heal-figures"; Put(_heal, 6);
            _dps.Margin = new Thickness(6, 0, 0, 0); _dps.HorizontalAlignment = HorizontalAlignment.Right; _dps.Tag = "row-dps"; Put(_dps, 7);
            foreach (var (t, col) in new[] { (_share, 8), (_dmg, 9), (_crit, 10) }) { t.HorizontalAlignment = HorizontalAlignment.Right; Put(t, col); }
            _cp.Margin = new Thickness(6, 1, 0, 0);
            _root.SizeChanged += (_, _) => Fit();
        }

        void Put(FrameworkElement el, int col) { Grid.SetColumn(el, col); _root.Children.Add(el); }

        public void Set(MeterMember m, MeterContext c, bool fresh)
        {
            _m = m;
            if (_expanded != c.Expanded)
            {
                _expanded = c.Expanded;
                _root.ColumnDefinitions[9].Width = new GridLength(c.Expanded ? DmgW : 0);
            }
            _rank.Text = m.Rank.ToString(CultureInfo.InvariantCulture);
            if (_iconCls != m.Class)
            {
                _iconCls = m.Class;
                var ic = ThemeText.Icon(m.Class, IconW - 3, m.Name);
                ic.HorizontalAlignment = HorizontalAlignment.Center;
                _icon.Child = ic;
            }
            _name.Text = m.Name;
            while (_nameLine.Children.Count > 1) _nameLine.Children.RemoveAt(1);
            _keep.Clear(); _give.Clear();
            _cp.Text = m.Cp;
            if (m.Cp.Length > 0) _nameLine.Children.Add(_cp);
            FrameworkElement Add(FrameworkElement el) { el.Margin = new Thickness(6, 0, 0, 0); _nameLine.Children.Add(el); return el; }
            if (m.TopDps) _give.Add((Add(Mark("★ " + L.T("DPS"), false)), Add(Mark("★", false, L.T("Top DPS")))));
            if (m.TopHeals) _give.Add((Add(Mark("✚ " + L.T("Heal"), true)), Add(Mark("✚", true, L.T("Top heals")))));
            if (m.You) { var y = Chip(L.T("You"), White, Frozen(YouInkC), 9.5); y.Tag = "you"; _keep.Add(Add(y)); }
            if (m.Deaths > 0) { var d = Tt("✕" + m.Deaths.ToString(CultureInfo.InvariantCulture), 10.5, Red, FontWeights.Bold); d.ToolTip = L.F(m.Deaths == 1 ? "{0} death" : "{0} deaths", m.Deaths); _keep.Add(Add(d)); }
            _heal.Text = m.Heal.Length > 0 ? "+" + m.Heal : "";
            _heal.Visibility = m.Heal.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            _dps.Text = m.Figure;
            _dps.FontSize = m.CpOnly ? 14 : 17;
            _share.Text = m.ShareText;
            _dmg.Text = c.Expanded ? m.Damage : "";
            _crit.Text = c.Expanded ? m.Crit : "";
            Fit();
            // filled to the share against the top member's (the mock-up's pills: the leader's is full)
            double top = m.CpOnly ? 0 : c.E.Shown(c.Now).Where(f => !f.Other).Select(f => c.E.Share(f)).DefaultIfEmpty(0).Max();
            SlideBar(_art, c, "bar", top > 0 ? Math.Clamp(m.Share / top, 0, 1) : 0, v => { _bar = v; _art.InvalidateVisual(); });
            _art.InvalidateVisual();
        }

        /// <summary>A mark as a small glass chip: ★ DPS in white glass, ✚ Heal in green glass.</summary>
        static Border Mark(string s, bool heal, string? tip = null)
        {
            var m = Chip(s, heal ? Mod.Br(GreenC, .25) : Mod.Br(Colors.White, .18), heal ? Frozen(HealInkC) : White, 9.5, caps: s.Length > 1);
            m.Tag = heal ? "top-heals" : "top-dps";
            m.ToolTip = tip;
            if (s.Length == 1) m.Padding = new Thickness(4, 1, 4, 1);
            return m;
        }

        /// <summary>The name line's room with every column shown (the row's width less every other column).</summary>
        double Room()
        {
            double w = _root.ActualWidth;
            if (w <= 0) return double.NaN;
            double other = PadX + RankW + Gap1 + IconW + Gap2 + Mod.W(_dps) + (_heal.Visibility == Visibility.Visible ? Mod.W(_heal) : 0)
                + (_expanded ? DmgW + CritW : 0) + PadX;
            return w - other - ShareW;
        }

        /// <summary>The room a fit level gives back: the share's column, then (expanded) the crit rate's.</summary>
        double Freed(int level) => (level >= 1 ? ShareW : 0) + (level >= 2 && _expanded ? CritW : 0);

        /// <summary>The narrowest the name goes: its first ten letters and the ellipsis (a shorter name, whole).</summary>
        double NameMin()
        {
            var s = _name.Text;
            if (s.Length <= 10) return Mod.W(_name);
            var t = Tt(s[..10] + "…", _name.FontSize, White, _name.FontWeight);
            return Mod.W(t) + 1;
        }

        void Fit()
        {
            double room = Room();
            if (double.IsNaN(room)) return;
            _name.ClearValue(FrameworkElement.MaxWidthProperty);
            double least = Math.Min(Mod.W(_name), NameMin()) + _keep.Sum(Mod.W) + _give.Sum(g => Mod.W(g.Short));
            // a column goes (for every row) only when the CP gone, the marks short and the name at its ten letters still don't fit
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
            _root.ColumnDefinitions[8].Width = new GridLength(level >= 1 ? 0 : ShareW);
            _share.Visibility = level >= 1 ? Visibility.Collapsed : Visibility.Visible;
            _root.ColumnDefinitions[10].Width = new GridLength(_expanded && level < 2 ? CritW : 0);
            _crit.Visibility = level >= 2 ? Visibility.Collapsed : Visibility.Visible;
            room += Freed(level);
            _name.ClearValue(FrameworkElement.MaxWidthProperty);
            double nw = Mod.W(_name), keep = _keep.Sum(Mod.W);
            double cpw = _cp.Text.Length > 0 ? Mod.W(_cp) : 0;
            double full = _give.Sum(g => Mod.W(g.Full)), shorts = _give.Sum(g => Mod.W(g.Short));
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
            var pill = Mod.Pill(r, 11);
            var tone = m.ClassColor;
            // the glass ground: faint white, a touch lighter at the top
            dc.DrawGeometry(Mod.Grad(true, (Mod.A(Colors.White, .085), 0), (Mod.A(Colors.White, .045), 1)), null, pill);
            double fx = r.Width * Math.Clamp(_bar, 0, 1);
            if (fx > 1)
            {
                dc.PushClip(pill);
                var fill = new Rect(r.X, r.Y, fx, r.Height);
                dc.DrawRectangle(Mod.Grad(false, (Mod.A(tone, .24), 0), (Mod.A(tone, .62), 1)), null, fill);
                // depth: lighter along its top half, a line of light on its top edge
                dc.DrawRectangle(Mod.Grad(true, (Mod.A(Colors.White, .14), 0), (Mod.A(Colors.White, 0), .55)), null, fill);
                dc.DrawRectangle(Mod.Br(Colors.White, .28), null, new Rect(r.X, r.Y, fx, 1));
                // the bright leading edge with a soft glow behind it
                double ex = r.X + fx;
                dc.DrawRectangle(Mod.Grad(false, (Mod.A(tone, 0), 0), (Mod.A(Mod.Mix(tone, Colors.White, .2), .45), 1)), null, new Rect(Math.Max(r.X, ex - 10), r.Y, Math.Min(10, fx), r.Height));
                dc.DrawRectangle(Frozen(Mod.Mix(tone, Colors.White, .25)), null, new Rect(Math.Max(r.X, ex - 2), r.Y, Math.Min(2, fx), r.Height));
                dc.Pop();
            }
            // a healer's share of the group's healing as a green line along the pill's foot (Settings' heal bars)
            if (ParserRows.HealBarsPref && m.HealShare > 0)
            {
                dc.PushClip(pill);
                dc.DrawRectangle(Mod.Br(GreenC, .9), null, new Rect(r.X, r.Bottom - 2.5, Math.Max(3, r.Width * Math.Clamp(m.HealShare, 0, 1)), 2.5));
                dc.Pop();
            }
            dc.DrawGeometry(null, Mod.P(Mod.Br(Colors.White, m.You ? .55 : .1), m.You ? 1.2 : 1), pill);
        }
    }
}
