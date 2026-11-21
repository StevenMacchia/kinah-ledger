using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using KinahLedger.Parser;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

sealed partial class HudRift
{
    /// <summary>The meter's foot: "Total 622.4M" at the left, "Group DPS 999K" (or your best or peak) at the right, the AVG mark after it.</summary>
    FrameworkElement Foot(Encounter e, DateTime now)
    {
        var (total, word, group, held) = FootOf(e, now);
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        TextBlock Pair(string w, string fig)
        {
            var t = Tt("", 11.5, Soft, FontWeights.Medium);
            t.Inlines.Add(new System.Windows.Documents.Run(w + " "));
            t.Inlines.Add(new System.Windows.Documents.Run(fig) { FontWeight = FontWeights.Bold, Foreground = White, FontSize = 12 });
            return t;
        }
        var tp = Pair(L.T("Total"), total);
        EnergyMark.After(tp, 11, Soft);   // Odyle Energy (Ui/EnergyMark.cs)
        g.Children.Add(tp);
        var right = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 0, 0, 0) };
        if (word.Length > 0) right.Children.Add(Pair(word, group));
        if (held)
        {
            var avg = Mark(L.T("AVG"), size: 8.5); avg.Tag = "avg-badge"; avg.ToolTip = L.T(Encounter.AvgBadge); avg.Margin = new Thickness(6, 0, 0, 0);
            right.Children.Add(avg);
        }
        Grid.SetColumn(right, 1); g.Children.Add(right);
        return AsFoot(g, e, now);
    }

    /// <summary>The line between the party and you pinned below it: "2 MORE", small and quiet, between two faint seams.</summary>
    static FrameworkElement GapLine(int left)
    {
        var t = Tt(left > 0 ? L.F("{0} more", left).ToUpperInvariant() : "···", 9, Quiet, FontWeights.SemiBold);
        t.HorizontalAlignment = HorizontalAlignment.Center;
        return t;
    }

    /// <summary>
    /// The rows' shared fit: a figure column holds or goes for every row at once (a column that jumps from row to row can't
    /// be read down). Level 0 shows every column; 1 lets the share go; 2, on the expanded meter, the crit rate too.
    /// </summary>
    internal sealed class RiftFit
    {
        readonly Dictionary<RiftRow, int> _need = new();
        public int Level { get; private set; }

        public void Report(RiftRow row, int need)
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
    /// One member as a bar out of the Abyss: a void-dark ground with a faint violet rim (yours in starlight), the bar rising from
    /// the Abyss's violet into the member's class colour with the stars showing through it, and at its leading edge a fissure
    /// of rift light (the leader's the brightest). On it (the bars kept dark enough that no shade effect is needed): the rank, the class's emblem in
    /// a lilac ring (the leader's crowned with three crystal shards), the name in white with the Combat Power after it and the
    /// marks (★ DPS, ✚ Heal, YOU, deaths), a healer's healing a second in mint, the DPS big in starlight and the share.
    /// Crowding (Steven, 2026-11-17): the CP goes first, then the marks shorten to their glyphs, then the name trims (never
    /// below ten letters), then the share column goes for every row; a figure is never cut.
    /// </summary>
    internal sealed class RiftRow : IThemeRow
    {
        public const double H = 26, Pitch = 29;
        const double PadX = 8, RankW = 14, Gap1 = 4, IconW = 20, Gap2 = 6, ShareW = 34, DmgW = 54, CritW = 36;
        readonly HudRift _look;
        readonly RiftFit _fit;
        readonly Grid _root = new() { Height = H };
        readonly Grid _words = new();
        readonly TacSketch _art, _crown;
        readonly TextBlock _rank = Tt("", 12, Frozen(Mod.A(LilacC, .95)), FontWeights.Bold), _name = Tt("", 13.5, Frozen(Colors.White), FontWeights.SemiBold),
            _cp = Tt("", 10.5, Frozen(Mod.A(Color.FromRgb(0xE8, 0xE0, 0xFF), .8)), FontWeights.Medium),
            _heal = Tt("", 10.5, Mint2, FontWeights.Bold), _dps = Tt("", 17, White, FontWeights.Bold), _share = Tt("", 10.5, Frozen(Mod.A(Color.FromRgb(0xF2, 0xEE, 0xFF), .95)), FontWeights.SemiBold),
            _dmg = Tt("", 12, Soft, FontWeights.SemiBold), _crit = Tt("", 11, Quiet, FontWeights.SemiBold);
        readonly StackPanel _nameLine = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, ClipToBounds = false };
        readonly Border _icon = new() { Width = IconW, Height = IconW, CornerRadius = new CornerRadius(IconW / 2), BorderThickness = new Thickness(1.5), BorderBrush = Frozen(Mod.A(LilacC, .55)), Background = Frozen(Mod.A(VoidC, .55)), VerticalAlignment = VerticalAlignment.Center };
        readonly List<FrameworkElement> _keep = new();
        readonly List<(FrameworkElement Full, FrameworkElement Short)> _give = new();
        MeterMember? _m;
        string _iconCls = "\0";
        double _bar;
        int _was;
        static readonly SolidColorBrush RankInk = Frozen(Mod.A(LilacC, .95)), LeadRim = Frozen(Mod.A(LightC, .9)), Rim = Frozen(Mod.A(LilacC, .55));
        // the moments' layer, made on the first moment: all of it drawn once, only moved and faded while a moment plays
        readonly TranslateTransform _crownAt = new();
        Canvas? _fx;
        System.Windows.Shapes.Rectangle? _band;
        Color? _bandC;
        TacSketch? _flare, _streak, _flash;
        System.Windows.Shapes.Ellipse? _ring;
        readonly TranslateTransform _bandAt = new(), _flareAt = new(), _streakAt = new();
        readonly ScaleTransform _ringScale = new(1, 1, 17, 6);
        bool _expanded;

        public FrameworkElement Root => _root;

        public RiftRow(HudRift look, RiftFit fit)
        {
            _look = look; _fit = fit;
            foreach (var w in new[] { PadX, RankW, Gap1, IconW, Gap2, double.NaN, 0, 0, ShareW, 0, 0, PadX })
                _words.ColumnDefinitions.Add(new ColumnDefinition { Width = double.IsNaN(w) ? new GridLength(1, GridUnitType.Star) : new GridLength(w) });
            _words.ColumnDefinitions[6].Width = GridLength.Auto; _words.ColumnDefinitions[7].Width = GridLength.Auto;
            _art = new TacSketch(Draw);
            _crown = new TacSketch(DrawCrown);
            _root.Children.Add(_art);
            _root.Children.Add(_words);
            _root.Children.Add(_crown);
            _crown.RenderTransform = _crownAt;
            _rank.HorizontalAlignment = HorizontalAlignment.Center;
            Put(_rank, 1); Put(_icon, 3);
            _name.TextTrimming = TextTrimming.CharacterEllipsis;
            _nameLine.Children.Add(_name);
            Put(_nameLine, 5);
            _heal.Margin = new Thickness(5, 0, 0, 0); _heal.Tag = "heal-figures"; Put(_heal, 6);
            _dps.Margin = new Thickness(5, 0, 0, 0); _dps.HorizontalAlignment = HorizontalAlignment.Right; _dps.Tag = "row-dps"; Put(_dps, 7);
            _share.Tag = "row-share"; _cp.Tag = "cp";
            foreach (var (t, col) in new[] { (_share, 8), (_dmg, 9), (_crit, 10) }) { t.HorizontalAlignment = HorizontalAlignment.Right; Put(t, col); }
            _cp.Margin = new Thickness(5, 1, 0, 0);
            _root.SizeChanged += (_, _) => Fit();
        }

        void Put(FrameworkElement el, int col) { Grid.SetColumn(el, col); _words.Children.Add(el); }

        bool Leads => _m is { Rank: 1, CpOnly: false, Pvp: false };

        public void Set(MeterMember m, MeterContext c, bool fresh)
        {
            // a rank won with effects playing: the fissure flares and leaves a streak of rift light below it as the row glides up
            if (!fresh && c.Moves && _was > 0 && m.Rank < _was) Climb();
            _was = m.Rank;
            _m = m;
            if (_expanded != c.Expanded)
            {
                _expanded = c.Expanded;
                _words.ColumnDefinitions[9].Width = new GridLength(c.Expanded ? DmgW : 0);
            }
            _rank.Text = m.Rank.ToString(CultureInfo.InvariantCulture);
            _rank.Foreground = Leads ? White : RankInk;
            if (_iconCls != m.Class)
            {
                _iconCls = m.Class;
                var ic = ThemeText.Icon(m.Class, IconW - 3, m.Name);
                ic.HorizontalAlignment = HorizontalAlignment.Center;
                _icon.Child = ic;
            }
            _icon.BorderBrush = Leads ? LeadRim : Rim;
            _name.Text = m.Name;
            while (_nameLine.Children.Count > 1) _nameLine.Children.RemoveAt(1);
            _keep.Clear(); _give.Clear();
            _cp.Text = m.Cp;
            if (m.Cp.Length > 0) _nameLine.Children.Add(_cp);
            FrameworkElement Add(FrameworkElement el) { el.Margin = new Thickness(4, 0, 0, 0); _nameLine.Children.Add(el); return el; }
            if (m.TopDps) _give.Add((Add(TopMark("★ " + L.T("DPS"), false)), Add(TopMark("★", false, L.T("Top DPS")))));
            if (m.TopHeals) _give.Add((Add(TopMark("✚ " + L.T("Heal"), true)), Add(TopMark("✚", true, L.T("Top heals")))));
            if (m.You) { var y = Mark(L.T("YOU"), filled: true, size: 9); y.Tag = "you"; _keep.Add(Add(y)); }
            if (m.Deaths > 0) { var d = Tt("✕" + m.Deaths.ToString(CultureInfo.InvariantCulture), 10.5, Coral, FontWeights.Bold); d.Tag = "deaths"; d.ToolTip = L.F(m.Deaths == 1 ? "{0} death" : "{0} deaths", m.Deaths); _keep.Add(Add(d)); }
            _heal.Text = m.Heal.Length > 0 ? "+" + m.Heal : "";
            _heal.Visibility = m.Heal.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            _dps.Text = m.Figure;
            _dps.FontSize = m.CpOnly ? 14 : 17;
            _share.Text = m.ShareText;
            _dmg.Text = c.Expanded ? m.Damage : "";
            _crit.Text = c.Expanded ? m.Crit : "";
            Fit();
            // filled against the top member's (the top bar full, the rest in proportion, 2026-11-20: Encounter.BarOf)
            SlideBar(_art, c, "bar", m.CpOnly ? 0 : c.E.BarOf(m.F), v => { _bar = v; _art.InvalidateVisual(); });
            _art.InvalidateVisual(); _crown.InvalidateVisual();
            if (Leads) _look.Lead(this, m, c);
        }

        /// <summary>A top mark as a violet glass chip: ★ DPS lilac-rimmed, ✚ Heal mint-rimmed.</summary>
        static Border TopMark(string s, bool heal, string? tip = null)
        {
            var m = Mark(s, heal ? Mint2 : White, heal ? MintC2 : null, size: 9);
            m.Tag = heal ? "top-heals" : "top-dps";
            m.ToolTip = tip;
            if (s.Length == 1) m.Padding = new Thickness(3, 0, 3, 1);
            return m;
        }

        // ---- the moments: small parts drawn once, only moved (transforms) and faded (opacity) while they play

        const double CrownX = PadX + RankW + Gap1 + IconW / 2, CrownY = (H - IconW) / 2 + 5;

        /// <summary>The bar's leading edge, px from the row's left.</summary>
        double Edge() => Math.Max(2, (_root.ActualWidth - 1) * Math.Clamp(_bar, 0, 1));

        void Fx()
        {
            if (_fx != null) return;
            _fx = new Canvas { IsHitTestVisible = false };
            // the pulse's band of light, sweeping up the bar (in a layer clipped to the row, under the words)
            var clip = new Canvas { IsHitTestVisible = false, ClipToBounds = true };
            _band = new System.Windows.Shapes.Rectangle { Width = 60, Height = H, Opacity = 0, RenderTransform = _bandAt };
            clip.Children.Add(_band);
            // the fissure's flare (breaking out above and below the bar), the streak a climb leaves, the crown's flash and ring
            _flare = new TacSketch((dc, _) => Fissure(dc, 0, .5, H - 1, LightC, 2.2, 3)) { Opacity = 0, RenderTransform = _flareAt };
            _streak = new TacSketch((dc, _) => dc.DrawRectangle(Mod.Grad(true, (Mod.A(CyanC, .75), 0), (Mod.A(VioletC, 0), 1)), null, new Rect(-1.2, H, 2.4, 18))) { Opacity = 0, RenderTransform = _streakAt };
            _flash = new TacSketch((dc, _) => Fissure(dc, CrownX, CrownY - 14, 24, VioletC, .9, 6)) { Opacity = 0 };
            _ring = new System.Windows.Shapes.Ellipse { Width = 34, Height = 12, Stroke = Mod.Br(CyanC, .9), StrokeThickness = 1.3, Opacity = 0, RenderTransform = _ringScale, IsHitTestVisible = false };
            Canvas.SetLeft(_ring, CrownX - 17); Canvas.SetTop(_ring, CrownY - 7);
            foreach (var el in new UIElement[] { _flare, _streak, _flash, _ring }) _fx.Children.Add(el);
            _root.Children.Insert(1, clip);
            _root.Children.Add(_fx);
        }

        /// <summary>The seams' pulse (0 to 1, -1: none) at this row, in rank order: a band of light sweeping up the bar into its fissure, which flares.</summary>
        internal void Seam(double t, Color? c)
        {
            if (_m is not { } m) return;
            int at = Math.Max(0, Math.Min(m.Rank, 6) - 1);
            double seam = t is > 0 and < 1 && !m.CpOnly ? Seg(t, .2 + .1 * at, .62 + .1 * at) : 0;
            if (seam is <= 0 or >= 1) { if (_band != null) { _band.Opacity = 0; _flare!.Opacity = 0; } return; }
            Fx();
            var col = c ?? LightC;
            if (_bandC != col)
            {
                _bandC = col;
                _band!.Fill = Mod.Grad(false, (Mod.A(VioletC, 0), 0), (Mod.A(Mod.Mix(VioletC, col, .5), .35), .55), (Mod.A(col, .85), .86), (Mod.A(Colors.White, .9), .9), (Mod.A(col, 0), 1));
            }
            double fx = Edge();
            _bandAt.X = -60 + (fx + 4) * Out(Math.Min(1, seam / .7));
            _band!.Opacity = Math.Min(1, 1.2 * Math.Sin(Math.PI * Math.Min(1, seam / .8)));
            _flareAt.X = fx;
            _flare!.Opacity = seam > .55 ? Math.Sin(Math.PI * (seam - .55) / .45) : 0;
        }

        /// <summary>The crown lands: it drops onto its place over the emblem, a fissure of rift light flashes down through it and a ring of light spreads from its feet.</summary>
        internal void Land()
        {
            Fx();
            Motion.Play(_crown, 1000, t =>
            {
                _crownAt.Y = -7 * (1 - Back(Seg(t, 0, .4), 2.4));
                _crown.Opacity = Seg(t, 0, .18);
                _flash!.Opacity = t < .3 ? 0 : Math.Sin(Math.PI * Seg(t, .3, .8));
                double r = Seg(t, .3, .95);
                _ringScale.ScaleX = _ringScale.ScaleY = .25 + .9 * r;
                _ring!.Opacity = t < .3 ? 0 : .85 * (1 - r);
            }, () => { _crownAt.Y = 0; _crown.Opacity = 1; _flash!.Opacity = 0; _ring!.Opacity = 0; });
        }

        /// <summary>A rank won: the fissure flares and leaves a streak of rift light below it as the row glides up.</summary>
        void Climb()
        {
            Fx();
            Motion.Play(_streak!, 620, t =>
            {
                _streakAt.X = _flareAt.X = Edge();
                _streak!.Opacity = 1 - t;
                _flare!.Opacity = .8 * (1 - t);
            }, () => { _streak!.Opacity = 0; _flare!.Opacity = 0; });
        }

        /// <summary>The name line's room with every column shown (the row's width less every other column).</summary>
        double Room()
        {
            double w = _root.ActualWidth;
            if (w <= 0) return double.NaN;
            double other = PadX + RankW + Gap1 + IconW + Gap2 + Mod.W(_dps) + _dps.Margin.Left + (_heal.Visibility == Visibility.Visible ? Mod.W(_heal) + _heal.Margin.Left : 0)
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
            double least = Math.Min(Mod.W(_name), NameMin()) + _keep.Sum(e => Mod.W(e) + 4) + _give.Sum(g => Mod.W(g.Short) + 4);
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
            _words.ColumnDefinitions[8].Width = new GridLength(level >= 1 ? 0 : ShareW);
            _share.Visibility = level >= 1 ? Visibility.Collapsed : Visibility.Visible;
            _words.ColumnDefinitions[10].Width = new GridLength(_expanded && level < 2 ? CritW : 0);
            _crit.Visibility = level >= 2 ? Visibility.Collapsed : Visibility.Visible;
            room += Freed(level);
            _name.ClearValue(FrameworkElement.MaxWidthProperty);
            double nw = Mod.W(_name), keep = _keep.Sum(e => Mod.W(e) + 4);
            double cpw = _cp.Text.Length > 0 ? Mod.W(_cp) + _cp.Margin.Left : 0;
            double full = _give.Sum(g => Mod.W(g.Full) + 4), shorts = _give.Sum(g => Mod.W(g.Short) + 4);
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
            var pill = Mod.Pill(r, 5);
            var tone = m.ClassColor;
            // the ground: the void, a breath of violet at its top
            dc.DrawGeometry(Mod.Grad(true, (Mod.A(DuskC, .78), 0), (Mod.A(VoidC, .82), 1)), null, pill);
            double fx = r.Width * Math.Clamp(_bar, 0, 1);
            if (fx > 1)
            {
                dc.PushClip(pill);
                var fill = new Rect(r.X, r.Y, fx, r.Height);
                // the body: the Abyss's violet at its root rising into the class's colour, the stars showing through
                dc.DrawRectangle(Mod.Grad(false, (Mod.A(Mod.Mix(DeepVioletC, tone, .14), .94), 0), (Mod.A(Mod.Mix(Mod.Mix(tone, VioletC, .3), VoidC, .3), .82), .55), (Mod.A(Mod.Mix(tone, DeepVioletC, .32), .94), 1)), null, fill);
                dc.PushOpacity(.5);
                dc.DrawRectangle(Stars, null, fill);
                dc.Pop();
                // depth: lighter along its top half, a hairline of light on its top
                dc.DrawRectangle(Mod.Grad(true, (Mod.A(Colors.White, .13), 0), (Mod.A(Colors.White, 0), .55)), null, fill);
                dc.DrawRectangle(Mod.Br(LilacC, .3), null, new Rect(r.X, r.Y, fx, 1));
                dc.Pop();
            }
            // a healer's share of the group's healing as a mint line along the bar's foot (Settings' heal bars)
            if (ParserRows.HealBarsPref && m.HealShare > 0)
            {
                dc.PushClip(pill);
                dc.DrawRectangle(Mod.Br(MintC2, .9), null, new Rect(r.X, r.Bottom - 2.5, Math.Max(3, r.Width * Math.Clamp(m.HealShare, 0, 1)), 2.5));
                dc.Pop();
            }
            dc.DrawGeometry(null, Mod.P(m.You ? Mod.Br(StarC, .6) : Mod.Br(LilacC, .14), m.You ? 1.2 : 1), pill);
            // the fissure at the leading edge: the leader's the brightest
            if (fx > 1) Fissure(dc, Math.Max(r.X + 1.5, r.X + fx - .5), r.Y, r.Height, tone, Leads ? 1.4 : .62, (int)(m.Id & 7));
        }

        /// <summary>The crown over the leader's emblem: three crystal shards floating over it (drawn once; a landing moves it).</summary>
        void DrawCrown(DrawingContext dc, Size s)
        {
            if (Leads) Crown(dc, CrownX, CrownY, 1.45, 0);
        }
    }
}
