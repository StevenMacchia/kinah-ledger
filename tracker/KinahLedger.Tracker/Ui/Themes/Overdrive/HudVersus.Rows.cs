using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using KinahLedger.Parser;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

sealed partial class HudVersus
{
    /// <summary>A name of up to this many characters always shows whole (the crowding rule's floor).</summary>
    const int NameKeep = 10;

    /// <summary>A text's width in a block's own face, size and weight (a figure column sized for its widest form).</summary>
    static double TextW(TextBlock like, string s)
    {
        var probe = new TextBlock { Text = s, FontFamily = like.FontFamily, FontSize = like.FontSize, FontWeight = like.FontWeight, FontStyle = like.FontStyle, FontStretch = like.FontStretch, Padding = like.Padding };
        TextOptions.SetTextFormattingMode(probe, TextFormattingMode.Display);
        System.Windows.Documents.Typography.SetNumeralAlignment(probe, FontNumeralAlignment.Tabular);
        probe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return Math.Ceiling(probe.DesiredSize.Width);
    }

    /// <summary>A figure column's width: the widest of <paramref name="forms"/>, the block's margins and a pixel of air (two for Impact's lean).</summary>
    static double FigCol(TextBlock fig, params string[] forms) => forms.Max(f => TextW(fig, f)) + fig.Margin.Left + fig.Margin.Right + 2;

    /// <summary>The fight's foot: a thin slanted plate, "Total 622.4M" at the left, the group's word and figure at the right, the AVG badge on a held average.</summary>
    FrameworkElement Foot(Encounter e, DateTime now)
    {
        var (total, word, group, held) = FootOf(e, now);
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        TextBlock Pair(string w, string fig)
        {
            var t = T("", 10.5, Soft, FontWeights.SemiBold);
            t.Inlines.Add(new System.Windows.Documents.Run(w + " "));
            t.Inlines.Add(new System.Windows.Documents.Run(fig) { FontWeight = FontWeights.Bold, Foreground = PaleY, FontSize = 12 });
            return t;
        }
        var tp = Pair(L.T("Total"), total);
        EnergyMark.After(tp, 11, Frozen(Mod.A(Colors.White, .72)));   // Odyle Energy (Ui/EnergyMark.cs)
        g.Children.Add(tp);
        var right = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 0, 0, 0) };
        if (word.Length > 0) right.Children.Add(Pair(word, group));
        if (held)
        {
            var avg = Badge(L.T("AVG"), SteelC, size: 8); avg.Tag = "avg-badge"; avg.ToolTip = L.T(Encounter.AvgBadge); avg.Margin = new Thickness(6, 0, 0, 0);
            right.Children.Add(avg);
        }
        Grid.SetColumn(right, 1); g.Children.Add(right);
        Lit(g);
        var plate = Plated(g, Lean, null, new Thickness(12, 3, 14, 4));
        plate.HorizontalAlignment = HorizontalAlignment.Stretch;
        return AsFoot(plate, e, now);
    }

    /// <summary>The line between the party and you pinned below it: "2 MORE", small, leaning.</summary>
    static FrameworkElement GapLine(int left)
    {
        var t = T(left > 0 ? L.F("{0} more", left).ToUpperInvariant() : "·", 9, Frozen(Mod.A(SteelC, .9)), FontWeights.Bold, italic: true);
        t.HorizontalAlignment = HorizontalAlignment.Center;
        return Lit(t);
    }

    /// <summary>
    /// One member as a fighter's life gauge (the Versus row): at the left the rank in an angled numeral block (the leader's a
    /// combo counter's "1" on a hit spark), the class's art framed like a character-select portrait chip, then the huge slanted
    /// gauge: a dark track with a double outline (black, then chrome; yours P1 cyan, the leader's pale gold), filled to the
    /// member's part of the top in their class colour with a glossy band and a hard white cap (the leader's a hot red-to-yellow
    /// gradient). On the gauge in shaded white: the name in bold italic, the Combat Power small, the marks (★ DPS, a healer's
    /// ✚, deaths, YOU), a healer's healing a second in mint. At the right a black score plate holds the DPS big in Impact and
    /// the share. Short of room the CP goes first, then the share; a name keeps ten characters; a figure is never cut (Mod.Fit).
    /// </summary>
    sealed class VersusRow : IThemeRow, IThemeBarSpan
    {
        public const double H = 28, Pitch = 31;
        const double RankW = 25, ChipW = 25, Air = 4, PlatePad = 5, PadR = 10;
        readonly HudVersus _look;
        readonly Grid _root = new() { Height = H };
        readonly TacSketch _art, _crown;
        readonly ScaleTransform _crownScale = new(1, 1);
        readonly Grid _words = new();
        readonly TextBlock _rank = Big("", 17, Steel), _name = T("", 13, White, FontWeights.Bold, NameFace, italic: true), _cp = T("", 10, Frozen(Mod.A(Colors.White, .92)), FontWeights.SemiBold),
            _heal = T("", 10.5, Mint, FontWeights.Bold), _dps = Big("", 18, White), _share = T("", 10, Frozen(Mod.A(Colors.White, .86)), FontWeights.SemiBold),
            _dmg = T("", 10.5, Soft, FontWeights.SemiBold), _crit = T("", 10, Soft, FontWeights.SemiBold);
        readonly StackPanel _line = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        readonly Border _iconHost = new() { Width = 18, Height = 18, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        readonly List<FrameworkElement> _marks = new();
        readonly double _dpsW, _shareW, _dmgW, _critW;
        string _cls = "\0";
        MeterMember? _m;
        double _bar;
        int _was;
        bool _expanded, _shareOn = true;

        public FrameworkElement Root => _root;

        public VersusRow(HudVersus look)
        {
            _look = look;
            _dps.Tag = "row-dps"; _share.Tag = "row-share"; _heal.Tag = "heal-figures"; _cp.Tag = "cp";
            _dps.HorizontalAlignment = _share.HorizontalAlignment = _dmg.HorizontalAlignment = _crit.HorizontalAlignment = HorizontalAlignment.Right;
            _dps.Margin = new Thickness(0, -1, 1, 0); _share.Margin = new Thickness(0, 3, 0, 0); _dmg.Margin = new Thickness(0, 1, 0, 0); _crit.Margin = new Thickness(0, 1, 0, 0);
            _heal.Margin = new Thickness(4, 0, 3, 0); _heal.Padding = new Thickness(3, 0, 3, 1); _heal.Background = Frozen(Mod.A(InkC, .74));
            _cp.Padding = new Thickness(3, 0, 3, 0); _cp.Background = Frozen(Mod.A(InkC, .5));
            // the columns: rank, the portrait chip, air, the name's line (the rest), heal, the plate's lead-in, DPS, share; expanded, damage and crit; the plate's end
            _dpsW = FigCol(_dps, "888.8K"); _shareW = FigCol(_share, "88.8%", "100%", L.T("CP")) + 3;
            _dmgW = FigCol(_dmg, "888.88M") + 7; _critW = FigCol(_crit, "88%") + 7;
            foreach (var w in new[] { RankW, ChipW, Air + 2, double.NaN, 0, PlatePad + Lean * H, _dpsW, _shareW, 0, 0, PadR })
                _words.ColumnDefinitions.Add(new ColumnDefinition { Width = double.IsNaN(w) ? new GridLength(1, GridUnitType.Star) : w == 0 ? GridLength.Auto : new GridLength(w) });
            _words.ColumnDefinitions[8].Width = new GridLength(0); _words.ColumnDefinitions[9].Width = new GridLength(0);
            _art = new TacSketch(Draw);
            _crown = new TacSketch(DrawCrown) { RenderTransform = _crownScale };
            _root.Children.Add(_art);
            _root.Children.Add(_crown);
            _rank.HorizontalAlignment = HorizontalAlignment.Center; _rank.Margin = new Thickness(1, 0, 0, 0);
            Put(_rank, 0); Put(_iconHost, 1); Put(_line, 3); Put(_heal, 4); Put(_dps, 6); Put(_share, 7); Put(_dmg, 8); Put(_crit, 9);
            _name.TextTrimming = TextTrimming.CharacterEllipsis;
            _cp.Margin = new Thickness(5, 1, 0, 0);
            _line.Children.Add(_name); _line.Children.Add(_cp);
            _root.Children.Add(Lit(_words));
            _root.SizeChanged += (_, _) => { Fit(); _shine = null; };
        }

        void Put(FrameworkElement el, int col) { Grid.SetColumn(el, col); _words.Children.Add(el); }

        /// <summary>Deaths on a small dark pill in a bright coral, so they read on any class colour over a bright scene.</summary>
        static readonly SolidColorBrush DeathInk = Frozen(Color.FromRgb(0xFF, 0x8C, 0x8C)), DeathPill = Frozen(Mod.A(InkC, .8));

        /// <summary>The TOP DPS badge's words (its long or its short form), null without one.</summary>
        TextBlock? _top;

        bool Leads => _m is { Rank: 1, CpOnly: false, Pvp: false };

        /// <summary>Where the DPS bar is drawn (the PB ghost's track): in the row's art, the gauge from the portrait chip to the row's end, the rect a full bar fills.</summary>
        public (FrameworkElement On, Rect Track)? BarTrack => _art.ActualWidth <= 0 ? null : (_art, new Rect(RankW + ChipW + 1, 1, Math.Max(0, _art.ActualWidth - 1 - (RankW + ChipW + 1)), _art.ActualHeight - 2));

        public void Set(MeterMember m, MeterContext c, bool fresh)
        {
            // a rank won with effects playing: speed lines streak off the row as it glides up
            if (!fresh && c.Moves && _was > 0 && m.Rank < _was) Climb();
            _was = m.Rank;
            _m = m;
            if (_expanded != c.Expanded)
            {
                _expanded = c.Expanded;
                _words.ColumnDefinitions[8].Width = new GridLength(c.Expanded ? _dmgW : 0);
                _words.ColumnDefinitions[9].Width = new GridLength(c.Expanded ? _critW : 0);
            }
            _rank.Text = m.Rank.ToString(CultureInfo.InvariantCulture);
            _rank.Foreground = Leads ? White : m.You ? Cyan : Steel;
            _rank.FontSize = Leads ? 18 : 17;
            _rank.Effect = null;
            if (m.Class != _cls)
            {
                _cls = m.Class;
                _iconHost.Child = ThemeText.Icon(m.Class, 18, m.Name);
            }
            _name.Text = m.Name;
            _cp.Text = m.Cp;
            // the marks after the name: ★ DPS, a healer's ✚, deaths, YOU (kept whole: the name and the CP give way first)
            foreach (var mk in _marks) _line.Children.Remove(mk);
            _marks.Clear();
            void Add(FrameworkElement el) { el.Margin = new Thickness(4, 0, 0, 0); _line.Children.Add(el); _marks.Add(el); }
            _top = null;
            if (m.TopDps) { var t = Badge("★ " + L.T("DPS"), YellowC); t.Tag = "top-dps"; t.ToolTip = L.T("Top DPS"); Add(t); _top = (TextBlock)t.Child; }
            if (m.TopHeals) { var t = Badge("✚", MintC); t.Tag = "top-heals"; t.ToolTip = L.T("Top heals"); Add(t); }
            if (m.Deaths > 0) { var d = T($"✕{m.Deaths}", 10.5, DeathInk, FontWeights.Bold); d.Background = DeathPill; d.Padding = new Thickness(2, 0, 2, 0); d.Tag = "deaths"; d.ToolTip = L.F(m.Deaths == 1 ? "{0} death" : "{0} deaths", m.Deaths); Add(d); }
            if (m.You) { var y = Badge(L.T("YOU"), CyanC); y.Tag = "you"; Add(y); }
            _heal.Text = m.Heal.Length > 0 ? "+" + m.Heal : "";
            _heal.Visibility = m.Heal.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            _dps.Text = m.Figure;
            _dps.FontSize = m.CpOnly ? 15 : 18;
            _dps.Foreground = Leads ? PaleY : White;
            // a wider figure (Combat Power before the first hit, "167,548") widens its column: never a digit cut
            _words.ColumnDefinitions[6].Width = new GridLength(Math.Max(_dpsW, TextW(_dps, m.Figure) + 3));
            _share.Text = m.ShareText;
            _dmg.Text = c.Expanded ? m.Damage : "";
            _crit.Text = c.Expanded ? m.Crit : "";
            Fit();
            SlideBar(_art, c, "bar", m.CpOnly ? 0 : BarOf(m, c), v => { _bar = v; _art.InvalidateVisual(); });
            _art.InvalidateVisual(); _crown.InvalidateVisual();
            if (Leads) _look.Lead(this, m, c);
        }

        static double BarOf(MeterMember m, MeterContext c) => c.E.BarOf(m.F);   // against the top member's: the top bar full, the rest in proportion

        /// <summary>The crowding rule (Mod.Fit): the CP goes first, then the share; the name keeps at least ten characters.</summary>
        void Fit()
        {
            double total = _root.ActualWidth;
            if (total <= 0) return;
            var cols = _words.ColumnDefinitions;
            double heal = _heal.Visibility == Visibility.Visible ? Mod.W(_heal) + _heal.Margin.Left + _heal.Margin.Right : 0;
            double room = total - cols[0].Width.Value - cols[1].Width.Value - cols[2].Width.Value - heal - cols[5].Width.Value - cols[6].Width.Value
                - cols[8].Width.Value - cols[9].Width.Value - cols[10].Width.Value - 4;
            double min = _name.Text.Length <= NameKeep ? TextW(_name, _name.Text) : TextW(_name, _name.Text[..NameKeep] + "…") + 1;
            // short of room even with the CP and the share gone, the TOP DPS badge shortens to its star (the badges are kept)
            if (_top != null)
            {
                _top.Text = "★ " + L.T("DPS");
                if (min + _marks.Sum(Mod.W) > room + .5) _top.Text = "★";
            }
            var (cp, share) = Mod.Fit(_name, room, _cp.Text.Length > 0 ? _cp : null, _marks, _shareW, min);
            _cp.Visibility = cp ? Visibility.Visible : Visibility.Collapsed;
            if (share != _shareOn || cols[7].Width.Value != (share ? _shareW : 0))
            {
                _shareOn = share;
                cols[7].Width = new GridLength(share ? _shareW : 0);
            }
            _share.Visibility = share ? Visibility.Visible : Visibility.Collapsed;
            _art.InvalidateVisual();
        }

        // ---- the parts' places (px from the row's left)

        double PlateW()
        {
            var cols = _words.ColumnDefinitions;
            return cols[5].Width.Value + cols[6].Width.Value + cols[7].Width.Value + cols[8].Width.Value + cols[9].Width.Value + cols[10].Width.Value;
        }

        /// <summary>The gauge's span: after the portrait chip to just short of the score plate.</summary>
        (double L, double R) Span(double w) => (RankW + ChipW + 1, w - 1);

        /// <summary>The score plate over the gauge's end: smoked glass, the fill still glowing through it.</summary>
        static readonly Brush PlateGlass = Mod.Grad(true, (Mod.A(Ink2C, .6), 0), (Mod.A(InkC, .72), 1));

        static readonly Brush RankBlock = Mod.Grad(true, (Mod.A(Ink2C, .96), 0), (Mod.A(InkC, .96), 1));

        void Draw(DrawingContext dc, Size s)
        {
            if (_m is not { } m || s.Width < 60) return;
            double h = s.Height;
            // the rank's numeral block
            if (!Leads)
            {
                var rb = Slab(new Rect(.5, 2, RankW - 1, h - 4));
                dc.DrawGeometry(RankBlock, EdgeOut, rb);
                dc.DrawGeometry(null, m.You ? EdgeYou : SteelThin, rb);
            }
            // the portrait chip: the class colour behind the art, a double frame
            var chip = Slab(new Rect(RankW, 1, ChipW, h - 2));
            dc.DrawGeometry(Mod.Grad(true, (Mod.Mix(m.ClassColor, Colors.White, .15), 0), (Mod.Mix(m.ClassColor, Colors.Black, .55), 1)), EdgeOut, chip);
            dc.DrawGeometry(null, Leads ? EdgeLead : EdgeIn, chip);
            // the life gauge
            var (l, r) = Span(s.Width);
            if (r - l > 20)
            {
                var gr = new Rect(l, 1, r - l, h - 2);
                Gauge(dc, gr, m.CpOnly ? 0 : _bar, Leads ? LeadFill : FillOf(m.ClassColor), m.You ? EdgeYou : Leads ? EdgeLead : null);
                // a healer's share of the group's healing as a mint line along the gauge's foot (Settings' heal bars)
                if (ParserRows.HealBarsPref && m.HealShare > 0)
                {
                    dc.PushClip(Slab(gr));
                    dc.DrawRectangle(Frozen(Mod.A(MintC, .95)), null, new Rect(l, h - 4.5, Math.Max(3, (r - l) * Math.Clamp(m.HealShare, 0, 1)), 2.5));
                    dc.Pop();
                }
            }
            // the score plate
            double px = s.Width - PlateW();
            var plate = Slab(new Rect(px, 3, s.Width - px - 3, h - 6));
            dc.DrawGeometry(PlateGlass, EdgeOut, plate);
            dc.DrawGeometry(null, Leads ? EdgeLead : SteelThin, plate);
            if (Leads)
            {
                dc.PushClip(plate);
                dc.DrawRectangle(HotStripe, null, new Rect(px, 2, s.Width - px, 2.5));
                dc.Pop();
            }
        }

        // ---- the leader's flare: a hit spark behind the rank (drawn once; the landing scales it)

        const double FlareX = RankW / 2 + 1, FlareY = H / 2;
        static readonly Geometry FlareSpikes = Burst(new Point(FlareX, FlareY), 8, 13.5, 11, .15), FlareCore = Burst(new Point(FlareX, FlareY), 7, 10, 9, .4);
        static readonly Brush FlareFill = Mod.Grad(true, (PaleYC, 0), (YellowC, .55), (OrangeC, 1));
        static readonly Pen FlareRim = Mod.P(Frozen(RedC), 1.6), FlareOut = Mod.P(Frozen(Mod.A(Colors.Black, .85)), 3.2);

        void DrawCrown(DrawingContext dc, Size s)
        {
            if (!Leads) return;
            dc.DrawGeometry(null, FlareOut, FlareSpikes);
            dc.DrawGeometry(FlareFill, FlareRim, FlareSpikes);
            dc.DrawGeometry(Frozen(BloodC), Mod.P(Frozen(Mod.A(Colors.Black, .6)), 1), FlareCore);
        }

        // ---- the moments: small parts drawn once, only moved (transforms) and faded (opacity) while they play

        Canvas? _fx;
        TacSketch? _spark, _flash, _trail;
        System.Windows.Shapes.Ellipse? _ring;
        Canvas? _shineHost;
        System.Windows.Shapes.Rectangle? _shine;
        readonly TranslateTransform _shineAt = new(), _trailAt = new();
        readonly ScaleTransform _sparkScale = new(1, 1), _ringScale = new(1, 1);
        static readonly Brush ShineFill = Mod.Grad(false, (Mod.A(Colors.White, 0), 0), (Mod.A(Colors.White, .55), .45), (Mod.A(Colors.White, .95), .55), (Mod.A(Colors.White, 0), 1));

        void Fx()
        {
            if (_fx != null) return;
            _fx = new Canvas { IsHitTestVisible = false };
            // the hit spark that bursts behind the leader's numeral, its ring, a white flash over the gauge
            _spark = new TacSketch((dc, _) =>
            {
                var g = Burst(new Point(20, 15), 5, 20, 12, .1);
                dc.DrawGeometry(Frozen(Mod.A(PaleYC, .95)), Mod.P(Frozen(Mod.A(OrangeC, .9)), 1.2), g);
            }, 40, 30) { Opacity = 0, RenderTransform = _sparkScale, RenderTransformOrigin = new Point(.5, .5) };
            Canvas.SetLeft(_spark, FlareX - 20); Canvas.SetTop(_spark, 0);
            _ring = new System.Windows.Shapes.Ellipse { Width = 34, Height = 26, Stroke = Frozen(YellowC), StrokeThickness = 2.2, Opacity = 0, RenderTransform = _ringScale, RenderTransformOrigin = new Point(.5, .5), IsHitTestVisible = false };
            Canvas.SetLeft(_ring, FlareX - 17); Canvas.SetTop(_ring, FlareY - 13);
            _flash = new TacSketch((dc, s) => { var (l, r) = Span(s.Width); if (r - l > 10) dc.DrawGeometry(Frozen(Mod.A(Colors.White, .9)), null, Slab(new Rect(l, 1, r - l, H - 2))); }) { Opacity = 0, Width = 0, Height = H };
            _trail = new TacSketch((dc, s) =>
            {
                var b = Mod.Grad(false, (Mod.A(YellowC, 0), 0), (Mod.A(YellowC, .85), 1));
                foreach (var (y, len) in new[] { (5.0, 56.0), (12, 80), (19, 64), (25, 40) }) dc.DrawRectangle(b, null, new Rect(-len, y, len, 1.6));
            }, 0, H) { Opacity = 0, RenderTransform = _trailAt };
            Canvas.SetLeft(_trail, RankW + ChipW + 60);
            foreach (var el in new UIElement[] { _flash, _ring, _spark, _trail }) _fx.Children.Add(el);
            _root.Children.Insert(_root.Children.IndexOf(_crown), _fx);
        }

        /// <summary>The light that sweeps along the gauge (a white band leaning as the gauge does, clipped to it), drawn once per size.</summary>
        void ShineFx()
        {
            if (_shine != null && _shineHost != null) return;
            double w = _root.ActualWidth;
            if (w < 60) return;
            var (l, r) = Span(w);
            if (_shineHost == null) { _shineHost = new Canvas { IsHitTestVisible = false }; _root.Children.Insert(1, _shineHost); }
            _shineHost.Clip = Slab(new Rect(l, 1, r - l, H - 2));
            _shineHost.Children.Clear();
            _shine = new System.Windows.Shapes.Rectangle { Width = 46, Height = H, Fill = ShineFill, Opacity = 0, RenderTransform = new TransformGroup { Children = { new SkewTransform(-Math.Atan(Lean) * 180 / Math.PI, 0, 23, H / 2), _shineAt } } };
            _shineHost.Children.Add(_shine);
        }

        /// <summary>The bar shimmer at this row's turn of a sweep down the meter (<paramref name="t"/> 0 to 1, -1: none).</summary>
        internal void Shimmer(double t)
        {
            if (_m is not { } m) return;
            int at = Math.Max(0, Math.Min(m.Rank, 6) - 1);
            double u = t is > 0 and < 1 && !m.CpOnly ? Seg(t, .06 * at, .62 + .06 * at) : 0;
            if (u is <= 0 or >= 1) { if (_shine != null) _shine.Opacity = 0; return; }
            ShineFx();
            if (_shine == null) return;
            var (l, r) = Span(_root.ActualWidth);
            double end = l + (r - l) * Math.Clamp(_bar, 0, 1);
            _shineAt.X = l - 50 + (end - l + 60) * Out(u);
            _shine.Opacity = Math.Sin(Math.PI * u);
        }

        /// <summary>
        /// The crown lands: the leader's hit spark slams down onto the rank from big (scale 1.8 to 1, a bounce), a burst of
        /// sparks explodes behind it with a ring of impact, and a white flash frame hits the gauge.
        /// </summary>
        internal void Land()
        {
            Fx();
            _crown.RenderTransformOrigin = new Point(FlareX / Math.Max(1, _root.ActualWidth), .5);
            _flash!.Width = _root.ActualWidth;
            Motion.Play(_crown, 900, t =>
            {
                double k = Seg(t, 0, .28);
                _crownScale.ScaleX = _crownScale.ScaleY = SlamScale(k, 1.8);
                _crown.Opacity = Seg(t, 0, .1);
                double b = Seg(t, .2, .75);
                _spark!.Opacity = t < .2 ? 0 : 1 - b;
                _sparkScale.ScaleX = _sparkScale.ScaleY = .5 + .5 * Out(b);
                double rr = Seg(t, .22, .9);
                _ring!.Opacity = t < .22 ? 0 : .9 * (1 - rr);
                _ringScale.ScaleX = _ringScale.ScaleY = .3 + .8 * Out(rr);
                _flash.Opacity = .55 * Flash(t, .24, .3);
            }, () => { _crownScale.ScaleX = _crownScale.ScaleY = 1; _crown.Opacity = 1; _spark!.Opacity = 0; _ring!.Opacity = 0; _flash.Opacity = 0; });
        }

        /// <summary>A rank won: yellow speed lines streak off the row's back as it glides up.</summary>
        void Climb()
        {
            Fx();
            Motion.Play(_trail!, 520, t =>
            {
                _trailAt.X = -16 * Out(t);
                _trail!.Opacity = .9 * (1 - t);
            }, () => _trail!.Opacity = 0);
        }
    }
}
