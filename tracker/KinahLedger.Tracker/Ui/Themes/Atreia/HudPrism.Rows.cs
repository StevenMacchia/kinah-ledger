using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using KinahLedger.Parser;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

sealed partial class HudPrism
{
    /// <summary>A name of up to this many characters always shows whole (the crowding rule's floor).</summary>
    const int NameKeep = 10;

    /// <summary>A text's width in a block's own face, size and weight (a figure column sized for its widest form).</summary>
    static double TextW(TextBlock like, string s)
    {
        var probe = new TextBlock { Text = s, FontFamily = like.FontFamily, FontSize = like.FontSize, FontWeight = like.FontWeight, FontStretch = like.FontStretch, Padding = like.Padding };
        TextOptions.SetTextFormattingMode(probe, TextFormattingMode.Display);
        System.Windows.Documents.Typography.SetNumeralAlignment(probe, FontNumeralAlignment.Tabular);
        probe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return Math.Ceiling(probe.DesiredSize.Width);
    }

    /// <summary>A figure column's width: the widest of <paramref name="forms"/>, the block's margins and a pixel of air.</summary>
    static double FigCol(TextBlock fig, params string[] forms) => forms.Max(f => TextW(fig, f)) + fig.Margin.Left + fig.Margin.Right + 1;

    /// <summary>A member's bar, 0 to 1, against the fight's top (by Combat Power before any damage).</summary>
    static double BarOf(MeterMember m, MeterContext c) => c.E.BarOf(m.F);   // against the top member's: the top bar full, the rest in proportion (2026-11-20)

    /// <summary>The fight's foot under its hairline: "Total 622.4M" at the left, the group's word and figure at the right, the AVG mark on a held average.</summary>
    FrameworkElement Foot(Encounter e, DateTime now)
    {
        var (total, word, group, held) = FootOf(e, now);
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        TextBlock Pair(string w, string fig)
        {
            var t = new TextBlock { FontFamily = Mod.Face, FontSize = 11, FontWeight = FontWeights.Medium, Foreground = Frozen(Mod.A(Colors.White, .8)), VerticalAlignment = VerticalAlignment.Center };
            System.Windows.Documents.Typography.SetNumeralAlignment(t, FontNumeralAlignment.Tabular);
            t.Inlines.Add(new System.Windows.Documents.Run(w + " "));
            t.Inlines.Add(new System.Windows.Documents.Run(fig) { FontWeight = FontWeights.Bold, Foreground = White, FontSize = 12 });
            return t;
        }
        var tp = Pair(L.T("Total"), total);
        EnergyMark.After(tp, 11, Frozen(Mod.A(Colors.White, .72)));   // Odyle Energy (Ui/EnergyMark.cs)
        g.Children.Add(tp);
        var right = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 0, 0) };
        if (word.Length > 0) right.Children.Add(Pair(word, group));
        if (held)
        {
            var avg = Mark(L.T("AVG"), size: 8.5); avg.Tag = "avg-badge"; avg.ToolTip = L.T(Encounter.AvgBadge); avg.Margin = new Thickness(5, 0, 0, 0);
            right.Children.Add(avg);
        }
        Grid.SetColumn(right, 1); g.Children.Add(right);
        return AsFoot(g, e, now);
    }

    /// <summary>The line between the party and you pinned below it: "2 MORE", small and quiet between two spectrum-tipped hairlines.</summary>
    static FrameworkElement GapLine(int left)
    {
        var t = Tt(left > 0 ? L.F("{0} more", left).ToUpperInvariant() : "·", 9, Frozen(Mod.A(Colors.White, .8)), FontWeights.SemiBold);
        t.HorizontalAlignment = HorizontalAlignment.Center;
        t.Effect = Shade();
        var g = new Grid { VerticalAlignment = VerticalAlignment.Center };
        g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); g.ColumnDefinitions.Add(new ColumnDefinition());
        FrameworkElement Rule(bool toRight) => new TacSketch((dc, s) =>
            dc.DrawRectangle(Mod.Grad(false, (Mod.A(Colors.White, toRight ? 0 : .3), 0), (Mod.A(Colors.White, toRight ? .3 : 0), 1)), null, new Rect(0, 0, s.Width, 1)))
            { Height = 1, Margin = new Thickness(toRight ? 20 : 8, 0, toRight ? 8 : 20, 0), VerticalAlignment = VerticalAlignment.Center };
        var l = Rule(true); var r = Rule(false);
        Grid.SetColumn(t, 1); Grid.SetColumn(r, 2);
        g.Children.Add(l); g.Children.Add(t); g.Children.Add(r);
        return g;
    }

    /// <summary>
    /// One member as a clear glass bar on the crystal, the light the hero. A faint glass channel the row's length; the bar
    /// clear white glass with a breath of the class's colour, a light along its top, a class-coloured notch at the row's left.
    /// At the bar's leading edge a white-hot line where the light leaves the glass, and out of it the light splits: a beam of
    /// spectrum fanning from a point at the edge into six bands, red over violet, the full height of the row. The leader's
    /// sprays across all the row's free space to its end; the others' fan out short. The figures sit on a plate of dark glass
    /// at the row's right, so the light passes under them and every digit reads. Rank 1 wears the crown: a prism twice the
    /// rank's height standing on the bar's top edge, white light entering it from the panel's edge and leaving as a rainbow
    /// along the row. You: a brighter rim. Short of room the CP goes first, then the share; a name keeps at least ten
    /// characters; a figure is never cut (Mod.Fit).
    /// </summary>
    sealed class PrismRow : IThemeRow, IThemeBarSpan
    {
        public const double H = 28, Pitch = 31;
        const double PadL = 5, RankW = 19, IconW = 24, PadR = 7;
        /// <summary>The top bar's length: this much of the row's free room (left of the figures' plate), the rest the leader's beam's.</summary>
        const double BarMax = .86;
        readonly HudPrism _look;
        readonly Grid _root = new() { Height = H };
        readonly TacSketch _art;
        readonly Grid _words = new() { Margin = new Thickness(PadL, 0, PadR, 0) };
        readonly TextBlock _rank = Tt("", 11.5, White, FontWeights.Bold), _name = Tt("", 13, White, FontWeights.Bold), _cp = Tt("", 9.5, Frozen(Mod.A(Colors.White, .7)), FontWeights.SemiBold),
            _heal = Tt("", 11, Mint2, FontWeights.Bold), _dps = Tt("", 16.5, White, FontWeights.Bold), _share = Tt("", 10.5, Frozen(Mod.A(Colors.White, .86)), FontWeights.SemiBold),
            _dmg = Tt("", 11, Soft, FontWeights.SemiBold), _crit = Tt("", 10.5, Soft, FontWeights.SemiBold);
        readonly StackPanel _line = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        readonly Border _iconHost = new() { Width = 17, Height = 17, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
        readonly List<FrameworkElement> _marks = new();
        /// <summary>The top healer's mark in its two forms: "✚ Heal", and the bare cross when the long one would crowd the name or the CP.</summary>
        (Border Full, Border Short)? _healMark;
        // the courses' own clocks (the bar's slide runs on the art, the glide on the root)
        readonly Border _crownClock = new() { Width = 0, Height = 0 }, _glintClock = new() { Width = 0, Height = 0 };
        // the sheen: a band drawn once (Band), slid along the row by its transform, the row's own bounds its clip
        readonly Grid _fx = new() { ClipToBounds = true, IsHitTestVisible = false };
        readonly Rectangle _band = new() { Width = 46, Fill = Band, HorizontalAlignment = HorizontalAlignment.Left, Visibility = Visibility.Collapsed, IsHitTestVisible = false };
        readonly TranslateTransform _bandX = new();
        // the crown, made the first time the row leads: the prism and its resting ray (drawn on a resize only), the long ray the
        // flash shoots (drawn once, scaled along the row) and the white bloom as the prism lands (opacity only)
        TacSketch? _crownArt, _rayArt;
        Ellipse? _bloom;
        Rectangle? _flare;
        readonly ScaleTransform _flareScale = new(1, 1);
        readonly RotateTransform _flareTurn = new();
        readonly ScaleTransform _pop = new(1, 1), _rayScale = new(.001, 1);
        readonly double _dpsW, _shareW, _dmgW, _critW;
        string _cls = "\0";
        MeterMember? _m;
        double _bar;
        int _lastRank;
        bool _expanded, _shareOn = true;

        public FrameworkElement Root => _root;

        public PrismRow(HudPrism look)
        {
            _look = look;
            _dps.Tag = "row-dps"; _share.Tag = "row-share"; _heal.Tag = "heal-figures"; _cp.Tag = "cp";
            _dps.HorizontalAlignment = _share.HorizontalAlignment = _dmg.HorizontalAlignment = _crit.HorizontalAlignment = HorizontalAlignment.Right;
            _share.Margin = new Thickness(0, 2, 0, 0); _dmg.Margin = new Thickness(0, 1, 0, 0); _crit.Margin = new Thickness(0, 1, 0, 0);
            _heal.Margin = new Thickness(6, 1, 0, 0);
            // the columns: rank, the class art, the name's line (the rest), heal, the DPS, the share; expanded, the damage and the crit rate
            _dpsW = FigCol(_dps, "888.8K") + 4; _shareW = FigCol(_share, "88.8%", "100%", L.T("CP")) + 6;
            _dmgW = FigCol(_dmg, "888.88M") + 8; _critW = FigCol(_crit, "88%") + 8;
            foreach (var w in new[] { RankW, IconW, double.NaN, 0, _dpsW, _shareW, 0, 0 })
                _words.ColumnDefinitions.Add(new ColumnDefinition { Width = double.IsNaN(w) ? new GridLength(1, GridUnitType.Star) : w == 0 ? GridLength.Auto : new GridLength(w) });
            _words.ColumnDefinitions[6].Width = new GridLength(0); _words.ColumnDefinitions[7].Width = new GridLength(0);
            _art = new TacSketch(Draw);
            _root.Children.Add(_crownClock); _root.Children.Add(_glintClock);
            _band.RenderTransform = _bandX;
            _fx.Children.Add(_band);
            _rank.HorizontalAlignment = HorizontalAlignment.Center;
            _iconHost.Margin = new Thickness(2, 0, 0, 0);
            Put(_rank, 0); Put(_iconHost, 1); Put(_line, 2); Put(_heal, 3); Put(_dps, 4); Put(_share, 5); Put(_dmg, 6); Put(_crit, 7);
            _name.TextTrimming = TextTrimming.CharacterEllipsis;
            _cp.Margin = new Thickness(5, 2, 0, 0);
            _line.Children.Add(_name); _line.Children.Add(_cp);
            // the words straight on the crystal's dark glass (no shade effect: the rows update every tick and stay cheap)
            _root.Children.Add(_words);
            // the bar after the words in the grid (arranged after them, so it reads where their figures sit) but drawn under
            // them, the sheen's band over the bar
            Panel.SetZIndex(_art, -1); Panel.SetZIndex(_fx, -1);
            _root.Children.Add(_art); _root.Children.Add(_fx);
            _root.SizeChanged += (_, _) => Fit();
        }

        void Put(FrameworkElement el, int col) { Grid.SetColumn(el, col); _words.Children.Add(el); }

        bool Crowned => _m is { Rank: 1, Pinned: false, CpOnly: false };

        public void Set(MeterMember m, MeterContext c, bool fresh)
        {
            _m = m;
            if (_expanded != c.Expanded)
            {
                _expanded = c.Expanded;
                _words.ColumnDefinitions[6].Width = new GridLength(c.Expanded ? _dmgW : 0);
                _words.ColumnDefinitions[7].Width = new GridLength(c.Expanded ? _critW : 0);
            }
            _rank.Text = m.Rank.ToString(CultureInfo.InvariantCulture);
            bool crowned = Crowned;
            if (m.Class != _cls)
            {
                _cls = m.Class;
                var icon = ThemeText.Icon(m.Class, 17, m.Name);
                if (icon is Border b) { b.BorderBrush = Frozen(Mod.A(Colors.White, .8)); b.BorderThickness = new Thickness(1); }
                _iconHost.Child = icon;
            }
            _name.Text = m.Name;
            _cp.Text = m.Cp;
            // the marks after the name: ★ DPS, a healer's ✚, deaths, YOU (kept whole: the name and the CP give way first)
            foreach (var mk in _marks) _line.Children.Remove(mk);
            if (_healMark is { } old) { _line.Children.Remove(old.Full); _line.Children.Remove(old.Short); _healMark = null; }
            _marks.Clear();
            void Add(FrameworkElement el) { el.Margin = new Thickness(5, 0, 0, 0); _line.Children.Add(el); _marks.Add(el); }
            if (m.TopDps) { var t = Mark("★ " + L.T("DPS")); t.Tag = "top-dps"; t.ToolTip = L.T("Top DPS"); Add(t); }
            if (m.TopHeals)
            {
                var full = Mark("✚ " + L.T("Heal"), MarkMint, MarkMintC); full.Tag = "top-heals"; full.ToolTip = L.T("Top heals");
                var bare = Mark("✚", MarkMint, MarkMintC); bare.Tag = "top-heals"; bare.ToolTip = L.T("Top heals"); bare.Visibility = Visibility.Collapsed;
                Add(full); bare.Margin = full.Margin; _line.Children.Insert(_line.Children.IndexOf(full) + 1, bare);
                _healMark = (full, bare);
            }
            if (m.Deaths > 0) { var d = Tt($"✕{m.Deaths}", 10, Coral, FontWeights.Bold); d.Tag = "deaths"; Add(d); }
            if (m.You) { var y = Mark(L.T("YOU"), filled: true); y.Tag = "you"; Add(y); }
            _heal.Text = m.Heal.Length > 0 ? "+" + m.Heal : "";
            _heal.Visibility = m.Heal.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            _dps.Text = m.Figure;
            // a wider figure (Combat Power before the first hit, "167,548") widens its column: never a digit cut
            _words.ColumnDefinitions[4].Width = new GridLength(Math.Max(_dpsW, TextW(_dps, m.Figure) + 4));
            _share.Text = m.ShareText;
            _dmg.Text = c.Expanded ? m.Damage : "";
            _crit.Text = c.Expanded ? m.Crit : "";
            Fit();
            SlideBar(_art, c, "bar", BarOf(m, c), v => { _bar = v; _art.InvalidateVisual(); });
            // a rank won: the lead takes the crown (and the look sweeps the bars); any other climb catches a glint
            if (!fresh && c.Moves && _lastRank > 0 && m.Rank < _lastRank)
            {
                if (crowned) { Crown(flare: true); _look.NewLeader(); }
                else Glint();
            }
            _lastRank = m.Rank;
            ShowCrown(crowned);
            _art.InvalidateVisual();
        }

        /// <summary>The sheen's place along this bar (0 to 1; outside it none), set by the look's sweep: the band's transform and opacity only.</summary>
        internal void SweepAt(double p)
        {
            bool on = p is > 0 and < 1;
            if (on != (_band.Visibility == Visibility.Visible))
            {
                _band.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
                // the sheen runs on the bars only: clipped at the figures' plate (set as it starts: a clip, no layout)
                if (on) _fx.Clip = new RectangleGeometry(new Rect(0, 0, Math.Max(0, PlateX(_root.ActualWidth)), H));
            }
            if (!on) return;
            _bandX.X = -48 + (_root.ActualWidth + 96) * p;
            _band.Opacity = .5 + .5 * Math.Sin(Math.PI * p);
        }

        /// <summary>A row that climbed: the sheen runs once along its bar alone.</summary>
        void Glint() => Motion.Play(_glintClock, 650, t => SweepAt(t), () => SweepAt(-1));

        /// <summary>
        /// The crown flashes in: the prism pops in with a white bloom and its ray shoots the row's length, then settles to its
        /// rest. <paramref name="flare"/>: a new leader, a rainbow flare bursting round the prism as it lands. <paramref name="leadMs"/>
        /// holds it back that long (the fight's opening light reaches the prism first).
        /// </summary>
        internal void Crown(bool flare = false, double leadMs = 0)
        {
            if (!Crowned) return;
            ShowCrown(true);
            if (_rayArt is not { } ray || _bloom is not { } bloom || _flare is not { } fl) return;
            ray.Visibility = Visibility.Visible;
            if (flare) fl.Visibility = Visibility.Visible;
            double total = 1100 + leadMs;
            Motion.Play(_crownClock, total, t =>
            {
                double p = Math.Max(0, (t * total - leadMs) / 1100);
                double flash = p <= 0 ? 0 : p < .12 ? Seg(p, 0, .12) : 1 - Seg(p, .12, .55);
                _pop.ScaleX = _pop.ScaleY = p <= 0 ? .01 : Math.Max(.01, Back(Seg(p, 0, .3), 2.6));
                bloom.Opacity = flash;
                double r = p < .35 ? Out(Seg(p, .05, .35)) : 1 - Out(Seg(p, .45, 1));
                _rayScale.ScaleX = Math.Max(.001, r);
                ray.Opacity = Math.Min(1, r * 1.5);
                if (flare)
                {
                    double f = Seg(p, .02, .75);
                    _flareScale.ScaleX = _flareScale.ScaleY = .25 + 1.05 * Out(f);
                    _flareTurn.Angle = 28 * f;
                    fl.Opacity = f < .2 ? Seg(f, 0, .2) : 1 - Seg(f, .35, 1);
                }
            }, () => { _pop.ScaleX = _pop.ScaleY = 1; bloom.Opacity = 0; ray.Visibility = Visibility.Collapsed; fl.Visibility = Visibility.Collapsed; });
        }

        /// <summary>Where the white light enters the crown's prism (its left face), in <paramref name="to"/>'s place.</summary>
        internal Point PrismIn(Visual to) => _root.TranslatePoint(new Point(CrownX - CrownHalf * .5, CrownTop + CrownTall * .5), (UIElement)to);

        /// <summary>The bar's leading edge (its middle), in <paramref name="to"/>'s place, and the member's colour.</summary>
        internal Point EdgeAt(Visual to) => _root.TranslatePoint(new Point(EdgeX(_root.ActualWidth), H / 2), (UIElement)to);
        internal Color Tone => _m?.ClassColor ?? Colors.White;

        /// <summary>The figures' plate's left edge: the DPS, the share and (expanded) the damage and crit columns, and the air round them.</summary>
        double PlateX(double w)
        {
            var c = _words.ColumnDefinitions;
            return Math.Max(w * .4, w - PadR - c[4].ActualWidth - c[5].ActualWidth - c[6].ActualWidth - c[7].ActualWidth - 6);
        }

        /// <summary>The bar's leading edge: its part of the free room's top length.</summary>
        /// <summary>Where a full bar would fill, for the PB ghost (IThemeBarSpan): from the channel's left to the top bar's length.</summary>
        public (FrameworkElement On, Rect Track)? BarTrack => _art.ActualWidth <= 0 ? null
            : (_art, new Rect(1, 0, Math.Max(0, PlateX(_art.ActualWidth) - 1) * BarMax, _art.ActualHeight));

        double EdgeX(double w) => 1 + Math.Max(0, PlateX(w) - 1) * BarMax * Math.Clamp(_bar, 0, 1);

        /// <summary>The crown's parts shown on the leader's row (made the first time), hidden on the others.</summary>
        void ShowCrown(bool on)
        {
            if (on && _crownArt == null)
            {
                _crownArt = new TacSketch((dc, s) => DrawCrown(dc, s.Width, s.Height)) { RenderTransform = _pop };
                _rayArt = new TacSketch((dc, s) => DrawRay(dc, s.Width - RayX - 4, 1)) { RenderTransform = _rayScale, Visibility = Visibility.Collapsed, Opacity = 0 };
                _rayScale.CenterX = RayX;
                _pop.CenterX = CrownX; _pop.CenterY = CrownTop + CrownTall;
                _bloom = new Ellipse
                {
                    Width = 34, Height = 34, Fill = Bloom, Opacity = 0, IsHitTestVisible = false, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(CrownX - 17, CrownTop + CrownTall * .6 - 17, 0, 0),
                };
                // the new leader's flare: a burst of rainbow spikes round a white core, drawn once, scaled, turned and faded
                _flare = new Rectangle
                {
                    Width = 64, Height = 64, Fill = Flare, Opacity = 0, Visibility = Visibility.Collapsed, IsHitTestVisible = false, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(CrownX - 32, CrownTop + CrownTall * .55 - 32, 0, 0),
                    RenderTransform = new TransformGroup { Children = { _flareScale, _flareTurn } }, RenderTransformOrigin = new Point(.5, .5),
                };
                foreach (var el in new FrameworkElement[] { _rayArt, _crownArt, _bloom }) { Panel.SetZIndex(el, -1); _root.Children.Add(el); }
                Panel.SetZIndex(_flare, 1); _root.Children.Add(_flare);
            }
            if (_crownArt == null) return;
            var v = on ? Visibility.Visible : Visibility.Collapsed;
            if (_crownArt.Visibility != v) _crownArt.Visibility = _bloom!.Visibility = v;
            if (!on) { Motion.Halt(_crownClock); _rayArt!.Visibility = Visibility.Collapsed; _flare!.Visibility = Visibility.Collapsed; _pop.ScaleX = _pop.ScaleY = 1; }
        }

        /// <summary>The crowding rule (Mod.Fit): the CP goes first, then the share; the name keeps at least ten characters.</summary>
        void Fit()
        {
            double total = _root.ActualWidth;
            if (total <= 0) return;
            var cols = _words.ColumnDefinitions;
            double heal = _heal.Visibility == Visibility.Visible ? Mod.W(_heal) + _heal.Margin.Left : 0;
            double room = total - PadL - PadR - cols[0].Width.Value - cols[1].Width.Value - heal - cols[4].Width.Value - cols[6].Width.Value - cols[7].Width.Value - 2;
            double min = _name.Text.Length <= NameKeep ? TextW(_name, _name.Text) : TextW(_name, _name.Text[..NameKeep] + "…") + 1;
            var (cp, share) = Mod.Fit(_name, room, _cp.Text.Length > 0 ? _cp : null, _marks, _shareW, min);
            if (_healMark is { } hm)
            {
                // the long heal mark only where nothing else gives way for it; else the bare cross in its place
                bool crowded = (!cp && _cp.Text.Length > 0) || !share || _name.MaxWidth < Mod.W(_name) - .5;
                int at = _marks.IndexOf(hm.Full) is var i0 and >= 0 ? i0 : _marks.IndexOf(hm.Short);
                if (crowded && at >= 0)
                {
                    _marks[at] = hm.Short;
                    (cp, share) = Mod.Fit(_name, room, _cp.Text.Length > 0 ? _cp : null, _marks, _shareW, min);
                }
                else if (at >= 0) _marks[at] = hm.Full;
                bool full = _marks.Contains(hm.Full);
                hm.Full.Visibility = full ? Visibility.Visible : Visibility.Collapsed;
                hm.Short.Visibility = full ? Visibility.Collapsed : Visibility.Visible;
            }
            _cp.Visibility = cp ? Visibility.Visible : Visibility.Collapsed;
            if (share != _shareOn || cols[5].Width.Value != (share ? _shareW : 0))
            {
                _shareOn = share;
                cols[5].Width = new GridLength(share ? _shareW : 0);
            }
            _share.Visibility = share ? Visibility.Visible : Visibility.Collapsed;
        }

        void Draw(DrawingContext dc, Size s)
        {
            if (_m is not { } m || s.Width < 40) return;
            double w = s.Width, h = s.Height;
            var r = new Rect(.5, .5, w - 1, h - 1);
            var shape = Octa(r, 3.5);
            var tone = m.ClassColor;
            double plate = PlateX(w), ex = EdgeX(w), fx = ex - r.X;
            // the channel: faint glass, a touch lighter at its top
            dc.DrawGeometry(Mod.Grad(true, (Mod.A(Colors.White, .07), 0), (Mod.A(Colors.White, .025), 1)), null, shape);
            dc.PushClip(shape);
            if (fx > 1)
            {
                var fill = new Rect(r.X, r.Y, fx, r.Height);
                // clear glass: white, a breath of the class's colour deepening to the edge, lighter along its top half
                dc.DrawRectangle(Mod.Grad(true, (Mod.A(Colors.White, .2), 0), (Mod.A(Colors.White, .1), 1)), null, fill);
                dc.DrawRectangle(Mod.Grad(false, (Mod.A(tone, .14), 0), (Mod.A(Mod.Mix(tone, Colors.White, .25), .36), 1)), null, fill);
                dc.DrawRectangle(Mod.Grad(true, (Mod.A(Colors.White, .12), 0), (Mod.A(Colors.White, 0), .5)), null, fill);
                dc.DrawRectangle(Mod.Br(Colors.White, .55), null, new Rect(r.X, r.Y, fx, 1));
                dc.DrawRectangle(Mod.Br(Colors.Black, .25), null, new Rect(r.X, r.Bottom - 1, fx, 1));
                // the light split at the leading edge (in the fight only: before the first hit the bars are Combat Power, quiet):
                // the leader's beam sprays to the row's end, the others' fan short of the figures
                if (fx > 4 && !m.CpOnly)
                {
                    bool lead = Crowned;
                    Beam(dc, ex, lead ? plate - 1 : Math.Min(ex + 34, plate - 1), r.Top + 1, r.Bottom - 1, lead ? 1 : .8, fade: !lead);
                    dc.DrawRectangle(Mod.Grad(false, (Mod.A(Colors.White, 0), 0), (Mod.A(Colors.White, .26), 1)), null, new Rect(Math.Max(r.X, ex - 7), r.Y, Math.Min(7, fx), r.Height));
                    // the edge's white line at its top and foot only: the words crossing it keep their middle
                    dc.DrawRectangle(Mod.Br(Colors.White, .95), null, new Rect(ex - 1.3, r.Y, 1.3, r.Height * .27));
                    dc.DrawRectangle(Mod.Br(Colors.White, .95), null, new Rect(ex - 1.3, r.Bottom - r.Height * .27, 1.3, r.Height * .27));
                }
            }
            // the figures' plate: dark glass the light passes under, so every digit reads
            var pr = new Rect(plate, r.Y + 1.5, Math.Max(0, r.Right - 1.5 - plate), r.Height - 3);
            if (pr.Width > 4)
            {
                var pg = Octa(pr, 3);
                dc.DrawGeometry(Mod.Grad(false, (Mod.A(DeepC, .6), 0), (Mod.A(DeepC, .74), .25), (Mod.A(DeepC, .74), 1)), null, pg);
                dc.DrawLine(Mod.P(Mod.Br(Colors.White, .14), 1), new Point(pr.X + 3, pr.Y + .5), new Point(pr.Right - 3, pr.Y + .5));
                dc.DrawLine(Mod.P(Mod.Br(Colors.White, .2), 1), new Point(pr.X + .5, pr.Y + 3), new Point(pr.X + .5, pr.Bottom - 3));
            }
            // a healer's share of the group's healing as a mint line along the bar's foot (Settings' heal bars)
            if (ParserRows.HealBarsPref && m.HealShare > 0)
                dc.DrawRectangle(Mod.Br(MintC2, .9), null, new Rect(r.X, r.Bottom - 2.5, Math.Max(3, (plate - r.X) * Math.Clamp(m.HealShare, 0, 1)), 2.5));
            // the class's notch at the row's left
            dc.DrawRectangle(Mod.Grad(true, (Mod.Mix(tone, Colors.White, .2), 0), (tone, 1)), null, new Rect(r.X, r.Y + 3, 3, r.Height - 6));
            dc.Pop();
            // the rim: a lit top edge; yours brighter all round
            if (m.You) dc.DrawGeometry(null, Mod.P(Mod.Br(Colors.White, .66), 1.2), shape);
            else dc.DrawGeometry(null, Mod.P(Mod.Grad(true, (Mod.A(Colors.White, .24), 0), (Mod.A(Colors.White, .05), 1)), 1), shape);
        }

        /// <summary>
        /// The light leaving a bar: a beam from a point at <paramref name="x0"/> (the row's middle) fanning out to the row's full
        /// height by <paramref name="x1"/>, in six bands red over violet, a white-hot core at its start. <paramref name="fade"/>:
        /// gone by its end (a short fan), else still bright there (the leader's spray).
        /// </summary>
        static void Beam(DrawingContext dc, double x0, double x1, double top, double bot, double a, bool fade)
        {
            if (x1 - x0 < 3) return;
            double c = (top + bot) / 2, hh = bot - top;
            for (int k = 0; k < Spec.Length; k++)
            {
                double y0a = c - 1.5 + k * .5, y0b = y0a + .5, y1a = top + hh * k / Spec.Length, y1b = top + hh * (k + 1) / Spec.Length;
                var b = new LinearGradientBrush { MappingMode = BrushMappingMode.Absolute, StartPoint = new Point(x0, 0), EndPoint = new Point(x1, 0) };
                b.GradientStops.Add(new GradientStop(Mod.A(Spec[k], .95 * a), 0));
                b.GradientStops.Add(new GradientStop(Mod.A(Spec[k], (fade ? .4 : .85) * a), .45));
                b.GradientStops.Add(new GradientStop(Mod.A(Spec[k], fade ? 0 : .6 * a), 1));
                b.Freeze();
                dc.DrawGeometry(b, null, Poly(new Point(x0, y0a), new Point(x1, y1a), new Point(x1, y1b), new Point(x0, y0b)));
            }
            var core = new LinearGradientBrush { MappingMode = BrushMappingMode.Absolute, StartPoint = new Point(x0, 0), EndPoint = new Point(Math.Min(x1, x0 + 22), 0) };
            core.GradientStops.Add(new GradientStop(Mod.A(Colors.White, .5 * a), 0)); core.GradientStops.Add(new GradientStop(Mod.A(Colors.White, 0), 1)); core.Freeze();
            dc.DrawGeometry(core, null, Poly(new Point(x0, c - 2.5), new Point(Math.Min(x1, x0 + 22), c - .5), new Point(Math.Min(x1, x0 + 22), c + .5), new Point(x0, c + 2.5)));
        }

        /// <summary>The crown's place: the prism (twice the rank's height) standing on the bar's top edge over the rank, its side's depth, where its ray leaves it.</summary>
        const double CrownX = PadL + RankW / 2 + 1, CrownTop = -16, CrownTall = 18, CrownHalf = 11, CrownDepth = 5.5;
        static readonly double RayX = CrownX + CrownHalf * .5 + CrownDepth * .7;

        /// <summary>
        /// The crown: a triangular prism standing on the bar's top edge over the rank (its front face bright glass, its side
        /// face in shade, its edges white), a thin beam of white light entering its left face from the panel's edge and its
        /// resting rainbow leaving the right face along the top of the bar. It rises into the room above the row, never past the
        /// meter's panel. Drawn when the row's size changes only.
        /// </summary>
        static void DrawCrown(DrawingContext dc, double w, double h)
        {
            double cx = CrownX, top = CrownTop;
            var apex = new Point(cx, top); var bl = new Point(cx - CrownHalf, top + CrownTall); var br = new Point(cx + CrownHalf, top + CrownTall);
            // the white light coming in from the panel's edge to the left face
            var inP = new Point(cx - CrownHalf * .5, top + CrownTall * .5);
            var wb = new LinearGradientBrush { MappingMode = BrushMappingMode.Absolute, StartPoint = new Point(-12, 0), EndPoint = inP };
            wb.GradientStops.Add(new GradientStop(Mod.A(Colors.White, 0), 0)); wb.GradientStops.Add(new GradientStop(Mod.A(Colors.White, .95), 1)); wb.Freeze();
            dc.DrawLine(new Pen(wb, 1.6), new Point(-12, inP.Y + 2.5), inP);
            DrawRay(dc, Math.Min(96, w - RayX - 4), .9);
            var side = Poly(apex, new Point(apex.X + CrownDepth, apex.Y + 1.6), new Point(br.X + CrownDepth, br.Y - .8), br);
            dc.DrawGeometry(Mod.Grad(true, (Color.FromRgb(0x8C, 0x96, 0xAC), 0), (Color.FromRgb(0x3C, 0x42, 0x50), 1)), null, side);
            var front = Poly(apex, br, bl);
            dc.DrawGeometry(Mod.Grad(true, (Mod.A(Colors.White, .96), 0), (Mod.A(Color.FromRgb(0xB4, 0xC0, 0xD6), .92), 1)), null, front);
            // the light inside the glass: white in at the left face, split towards the right
            dc.DrawLine(Mod.P(Mod.Br(Colors.White, .9), 1.2), inP, new Point(cx + CrownHalf * .2, top + CrownTall * .58));
            for (int k = 0; k < Spec.Length; k++)
                dc.DrawLine(new Pen(Frozen(Mod.A(Spec[k], .85)), 1), new Point(cx + CrownHalf * .2, top + CrownTall * .58), new Point(cx + CrownHalf * .5 + .5, top + CrownTall * (.44 + k * .045)));
            dc.DrawGeometry(Mod.Br(Colors.White, .35), null, Poly(apex, new Point(cx, top + CrownTall), bl));
            dc.DrawGeometry(null, Mod.P(Mod.Br(DeepC, .9), 1), Poly(apex, new Point(apex.X + CrownDepth, apex.Y + 1.6), new Point(br.X + CrownDepth, br.Y - .8), bl));
            dc.DrawLine(Mod.P(Mod.Br(Colors.White, 1), 1), apex, bl);
        }

        /// <summary>The prism's rainbow: six bands out of its right face, fanning down onto the bar's top over <paramref name="len"/> px, fading at their ends.</summary>
        static void DrawRay(DrawingContext dc, double len, double a)
        {
            if (len < 4) return;
            double oy = CrownTop + CrownTall * .5;
            for (int i = 0; i < Spec.Length; i++)
            {
                var b = new LinearGradientBrush { MappingMode = BrushMappingMode.Absolute, StartPoint = new Point(RayX, 0), EndPoint = new Point(RayX + len, 0) };
                b.GradientStops.Add(new GradientStop(Mod.A(Spec[i], a), 0)); b.GradientStops.Add(new GradientStop(Mod.A(Spec[i], a * .5), .45)); b.GradientStops.Add(new GradientStop(Mod.A(Spec[i], 0), 1)); b.Freeze();
                double y0 = oy - 1.5 + i * .6, y1 = -3 + i * 1.6;
                dc.DrawGeometry(b, null, Poly(new Point(RayX, y0), new Point(RayX + len, y1), new Point(RayX + len, y1 + 1.6), new Point(RayX, y0 + .6)));
            }
        }
    }

    /// <summary>The sheen's band, drawn once: a slanted band of spectrum, faded at its sides, with a white (or mint) core.</summary>
    static readonly DrawingBrush Band = MakeBand(Colors.White), BandMint = MakeBand(MintC2);

    static DrawingBrush MakeBand(Color core)
    {
        const double w = 46, h = 26;
        var g = new DrawingGroup();
        using (var dc = g.Open())
        {
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));
            dc.DrawGeometry(SpecGrad(new Point(0, 0), new Point(w - 4, 0), .55, fade: true), null, Poly(new Point(12, 0), new Point(w - 4, 0), new Point(w - 14, h), new Point(0, h)));
            dc.DrawLine(Mod.P(Mod.Br(core, .65), 1.3), new Point(w - 18, 0), new Point(w - 28, h));
        }
        g.Freeze();
        var b = new DrawingBrush(g) { Stretch = Stretch.Fill };
        b.Freeze();
        return b;
    }

    /// <summary>The new leader's flare, drawn once: sixteen spikes of spectrum round a white core.</summary>
    static readonly DrawingBrush Flare = MakeFlare();

    static DrawingBrush MakeFlare()
    {
        const double c = 32;
        var g = new DrawingGroup();
        using (var dc = g.Open())
        {
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, 2 * c, 2 * c));
            for (int k = 0; k < 16; k++)
            {
                double a = k * Math.PI / 8, len = k % 2 == 0 ? c - 1 : c * .62;
                var col = Spec[k % Spec.Length];
                var tip = new Point(c + len * Math.Cos(a), c + len * Math.Sin(a));
                var b = new LinearGradientBrush { MappingMode = BrushMappingMode.Absolute, StartPoint = new Point(c, c), EndPoint = tip };
                b.GradientStops.Add(new GradientStop(Mod.A(Colors.White, .95), 0)); b.GradientStops.Add(new GradientStop(Mod.A(col, .9), .35)); b.GradientStops.Add(new GradientStop(Mod.A(col, 0), 1)); b.Freeze();
                dc.DrawGeometry(b, null, Poly(new Point(c + 4 * Math.Cos(a - Math.PI / 2), c + 4 * Math.Sin(a - Math.PI / 2)), tip, new Point(c + 4 * Math.Cos(a + Math.PI / 2), c + 4 * Math.Sin(a + Math.PI / 2))));
            }
            var core = new RadialGradientBrush(Mod.A(Colors.White, 1), Mod.A(Colors.White, 0)); core.Freeze();
            dc.DrawEllipse(core, null, new Point(c, c), 10, 10);
        }
        g.Freeze();
        var brush = new DrawingBrush(g) { Stretch = Stretch.Fill };
        brush.Freeze();
        return brush;
    }

    /// <summary>The white bloom as the crown lands, made once.</summary>
    static readonly Brush Bloom = MakeBloom();
    static Brush MakeBloom() { var b = new RadialGradientBrush(Mod.A(Colors.White, .9), Mod.A(Colors.White, 0)); b.Freeze(); return b; }
}
