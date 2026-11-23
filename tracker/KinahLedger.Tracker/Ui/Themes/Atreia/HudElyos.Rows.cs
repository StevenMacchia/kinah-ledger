using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using KinahLedger.Parser;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

sealed partial class HudElyos
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

    /// <summary>A member's bar, 0 to 1, against the fight's top (the top bar full, the rest in proportion).</summary>
    static double BarOf(MeterMember m, MeterContext c) => m.CpOnly ? 0 : c.E.BarOf(m.F);

    /// <summary>
    /// A rank in Roman numerals (I to XX), else in figures. The letters are set apart by hair spaces, as numerals cut in
    /// stone are; that also keeps "III" or "VIII" from reading as an English word to the language check (LangCheck).
    /// </summary>
    static string Roman(int n)
    {
        if (n < 1 || n > 20) return n.ToString(CultureInfo.InvariantCulture);
        string[] ones = { "", "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX" };
        return string.Join(" ", ((n >= 10 ? "X" : "") + (n >= 20 ? "X" : "") + ones[n % 10]).ToCharArray());
    }

    /// <summary>The fight's foot after the gold rule: "Total 2.03M" at the left, the group's word and figure at the right, the AVG mark on a held average.</summary>
    FrameworkElement Foot(Encounter e, DateTime now)
    {
        var (total, word, group, held) = FootOf(e, now);
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        TextBlock Pair(string w, string fig)
        {
            var t = new TextBlock { FontFamily = Serif, FontSize = 13.5, FontWeight = FontWeights.Bold, Foreground = GoldHi, VerticalAlignment = VerticalAlignment.Center };
            System.Windows.Documents.Typography.SetCapitals(t, FontCapitals.SmallCaps);
            System.Windows.Documents.Typography.SetNumeralAlignment(t, FontNumeralAlignment.Tabular);
            t.Inlines.Add(new System.Windows.Documents.Run(w + " "));
            t.Inlines.Add(new System.Windows.Documents.Run(fig) { FontFamily = Fig, FontWeight = FontWeights.SemiBold, Foreground = Ivory, FontSize = 13 });
            return t;
        }
        var tp = Pair(L.T("Total"), total);
        EnergyMark.After(tp, 11, Frozen(Mod.A(IvoryC, .72)));   // Odyle Energy (Ui/EnergyMark.cs)
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

    /// <summary>The line between the party and you pinned below it: "2 more" in the serif's gold capitals between two fine gold lines.</summary>
    static FrameworkElement GapLine(int left)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        FrameworkElement Line(bool toLeft) => new Border
        {
            Width = 22, Height = 1, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 1, 6, 0),
            Background = toLeft ? Lin(new Point(1, 0), new Point(0, 0), (Mod.A(GoldHiC, .6), 0), (Mod.A(GoldHiC, 0), 1)) : Lin(new Point(0, 0), new Point(1, 0), (Mod.A(GoldHiC, .6), 0), (Mod.A(GoldHiC, 0), 1)),
        };
        sp.Children.Add(Line(true));
        sp.Children.Add(Caps(left > 0 ? L.F("{0} more", left) : "◆", 10, Frozen(Mod.A(GoldHiC, .85))));
        sp.Children.Add(Line(false));
        return sp;
    }

    /// <summary>
    /// One member on the Elyos plate: a dark slot the width of the row, the member's bar in their class's colour washed with
    /// warm light from above, a gold edge along its top and an ivory leading edge; on it the rank in gold Roman numerals (the
    /// leader's between wings of light), the class's art in a gold ring, the name in ivory with Combat Power small after it and
    /// the marks (★ DPS, a healer's ✚, deaths, YOU), a healer's healing a second in mint, the DPS big and the share in gold at
    /// the right. You: a gold edge at the row's left and a gold hairline round it. A healer's share of the healing a mint line
    /// along the foot (Settings' heal bars). Short of room the CP goes first, then the share; a name keeps at least ten
    /// characters; a figure is never cut (Mod.Fit).
    /// </summary>
    sealed class ElyosRow : IThemeRow, IThemeBarSpan
    {
        public const double H = 26, Pitch = 29;
        const double PadL = 2, RankW = 25, IconCol = 25, PadR = 7;
        readonly Grid _root = new() { Height = H };
        readonly TacSketch _art, _wingArt;
        // the moments' parts, moved only by their transforms and opacity: the glint's band, the wings' scale, the flash
        readonly Canvas _glintHost = new() { IsHitTestVisible = false, ClipToBounds = false };
        readonly System.Windows.Shapes.Rectangle _glintBand = new() { Width = 42, Height = H, Fill = GlintBrush, Opacity = 0, IsHitTestVisible = false };
        readonly TranslateTransform _glintShift = new();
        readonly ScaleTransform _wingScale = new(1, 1, PadL + RankW / 2, H / 2);
        readonly System.Windows.Shapes.Ellipse _flash = new()
        {
            Width = 28, Height = 24, Fill = FlashBrush, Opacity = 0, IsHitTestVisible = false, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(PadL + RankW / 2 - 14, H / 2 - 12, 0, 0),
        };
        // the leader's great wings, out past the plate's edges at this row, their god-rays and the feathers of light they shed
        readonly TacSketch _rays;
        readonly ScaleTransform _raysScale = new(1, 1, PadL + RankW / 2, H / 2);
        readonly RotateTransform _raysTurn = new(0, PadL + RankW / 2, H / 2);
        readonly Canvas _fxCanvas = new() { IsHitTestVisible = false };
        bool _leader;
        Brush? _body;
        Color _bodyFor;
        readonly Grid _words = new() { Margin = new Thickness(PadL, 0, PadR, 0) };
        readonly TextBlock _rank = Tt("", 12.5, GoldHi, FontWeights.Bold, Serif), _name = Tt("", 13, Ivory, FontWeights.Bold), _cp = Tt("", 9.5, Frozen(Color.FromRgb(0xE6, 0xE9, 0xF2)), FontWeights.SemiBold),
            _heal = F("", 11.5, Mint2), _dps = F("", 17, Ivory), _share = F("", 11, Frozen(Color.FromRgb(0xF2, 0xDC, 0xA2)), FontWeights.Medium),
            _dmg = F("", 11.5, Soft, FontWeights.Medium), _crit = F("", 11, Soft, FontWeights.Medium);
        readonly StackPanel _line = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        readonly Border _iconHost = new() { Width = 19, Height = 19, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
        readonly List<FrameworkElement> _marks = new();
        readonly double _dpsW, _shareW, _dmgW, _critW;
        string _cls = "\0";
        MeterMember? _m;
        double _bar;
        Border? _healMark;
        int _lastRank;


        bool _expanded, _shareOn = true;

        public FrameworkElement Root => _root;

        /// <summary>Where the DPS bar is drawn (the PB ghost's track, IThemeBarSpan on main): a full bar fills <see cref="_art"/> edge to edge.</summary>
        public (FrameworkElement On, Rect Track)? BarTrack => _art.ActualWidth <= 0 ? null : (_art, new Rect(0, 0, _art.ActualWidth, _art.ActualHeight));

        public ElyosRow()
        {
            _dps.Tag = "row-dps"; _share.Tag = "row-share"; _heal.Tag = "heal-figures"; _cp.Tag = "cp";
            _dps.HorizontalAlignment = _share.HorizontalAlignment = _dmg.HorizontalAlignment = _crit.HorizontalAlignment = HorizontalAlignment.Right;
            _share.Margin = new Thickness(0, 2, 0, 0); _dmg.Margin = new Thickness(0, 1, 0, 0); _crit.Margin = new Thickness(0, 1, 0, 0);
            _heal.Margin = new Thickness(6, 1, 0, 0);
            // the columns: rank, the class art, the name's line (the rest), heal, the DPS, the share; expanded, the damage and the crit rate
            _dpsW = FigCol(_dps, "888.8K") + 4; _shareW = FigCol(_share, "88.8%", "100%", L.T("CP")) + 6;
            _dmgW = FigCol(_dmg, "888.88M") + 8; _critW = FigCol(_crit, "88%") + 8;
            foreach (var w in new[] { RankW, IconCol, double.NaN, 0, _dpsW, _shareW, 0, 0 })
                _words.ColumnDefinitions.Add(new ColumnDefinition { Width = double.IsNaN(w) ? new GridLength(1, GridUnitType.Star) : w == 0 ? GridLength.Auto : new GridLength(w) });
            _words.ColumnDefinitions[6].Width = new GridLength(0); _words.ColumnDefinitions[7].Width = new GridLength(0);
            _art = new TacSketch(Draw);
            _wingArt = new TacSketch(DrawWings) { RenderTransform = _wingScale };
            _glintBand.RenderTransform = new TransformGroup { Children = { new SkewTransform(-24, 0, 0, H / 2), _glintShift } };
            _glintHost.Children.Add(_glintBand);
            _rays = new TacSketch((dc, s) => { dc.PushTransform(RaysAt); dc.DrawDrawing(GodRays); dc.Pop(); }) { Opacity = 0, RenderTransform = new TransformGroup { Children = { _raysScale, _raysTurn } } };
            _root.Children.Add(_art); _root.Children.Add(_glintHost); _root.Children.Add(_rays); _root.Children.Add(_flash); _root.Children.Add(_wingArt);
            _rank.HorizontalAlignment = HorizontalAlignment.Center; _rank.Margin = new Thickness(0, 0, 0, 1);
            Put(_rank, 0); Put(_iconHost, 1); Put(_line, 2); Put(_heal, 3); Put(_dps, 4); Put(_share, 5); Put(_dmg, 6); Put(_crit, 7);
            _name.TextTrimming = TextTrimming.CharacterEllipsis;
            _cp.Margin = new Thickness(5, 2, 0, 0);
            _line.Children.Add(_name); _line.Children.Add(_cp);
            // one tight shade under all the row's words, so they read on any class colour
            _words.Effect = new DropShadowEffect { Color = Color.FromRgb(0x02, 0x05, 0x10), Direction = 270, ShadowDepth = .6, BlurRadius = 3.5, Opacity = 1 };
            _root.Children.Add(_words);
            _root.Children.Add(_fxCanvas);
            _root.SizeChanged += (_, e) =>
            {
                Fit();
            };
        }

        void Put(FrameworkElement el, int col) { Grid.SetColumn(el, col); _words.Children.Add(el); }

        public void Set(MeterMember m, MeterContext c, bool fresh)
        {
            bool climbed = !fresh && c.Moves && _lastRank > 0 && m.Rank < _lastRank && m.Rank > 1;
            _lastRank = m.Rank;
            _m = m;
            if (_expanded != c.Expanded)
            {
                _expanded = c.Expanded;
                _words.ColumnDefinitions[6].Width = new GridLength(c.Expanded ? _dmgW : 0);
                _words.ColumnDefinitions[7].Width = new GridLength(c.Expanded ? _critW : 0);
            }
            // the rank in Roman numerals, a smaller size for a long one so it keeps its column
            var rk = m.Died ? MeterMember.DiedMark : Roman(m.Rank);
            if (_rank.Text != rk)
            {
                _rank.Text = rk;
                _rank.FontSize = 12.5;
                while (_rank.FontSize > 8 && TextW(_rank, rk) > RankW - 3) _rank.FontSize -= .5;
            }
            _rank.Foreground = m.Rank == 1 && !m.CpOnly ? Frozen(Color.FromRgb(0xFF, 0xF4, 0xD0)) : GoldHi;
            if (m.Class != _cls)
            {
                _cls = m.Class;
                var icon = ThemeText.Icon(m.Class, 19, m.Name);
                if (icon is Border b) { b.BorderBrush = Leaf; b.BorderThickness = new Thickness(1.2); }
                _iconHost.Child = icon;
            }
            _name.Text = m.Name;
            _cp.Text = m.Cp;
            // the marks after the name: ★ DPS, a healer's ✚, deaths, YOU (kept whole: the name and the CP give way first)
            foreach (var mk in _marks) _line.Children.Remove(mk);
            _marks.Clear();
            void Add(FrameworkElement el) { el.Margin = new Thickness(5, 0, 0, 0); _line.Children.Add(el); _marks.Add(el); }
            if (m.TopDps) { var t = Mark("★ " + L.T("DPS")); t.Tag = "top-dps"; t.ToolTip = L.T("Top DPS"); Add(t); }
            _healMark = null;
            if (m.TopHeals) { var t = Mark("✚ " + L.T("Heal"), MintC2); t.Tag = "top-heals"; t.ToolTip = L.T("Top heals"); Add(t); _healMark = t; }
            if (m.Deaths > 0) { var d = F($"✕{m.Deaths}", 11, Coral, FontWeights.Bold); d.Tag = "deaths"; d.ToolTip = L.F(m.Deaths == 1 ? "{0} death" : "{0} deaths", m.Deaths); Add(d); }
            if (m.You) { var y = Mark(L.T("YOU"), filled: true); y.Tag = "you"; Add(y); }
            _heal.Text = m.Heal.Length > 0 ? "+" + m.Heal : "";
            _heal.Visibility = m.Heal.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            _dps.Text = m.Figure;
            _dps.FontSize = m.CpOnly ? 14.5 : 17;
            // a wider figure (Combat Power before the first hit, "167,548") widens its column: never a digit cut
            _words.ColumnDefinitions[4].Width = new GridLength(Math.Max(_dpsW, TextW(_dps, m.Figure) + 4));
            _share.Text = m.ShareText;
            _dmg.Text = c.Expanded ? m.Damage : "";
            _crit.Text = c.Expanded ? m.Crit : "";
            Fit();
            SlideBar(_art, c, "bar", BarOf(m, c), v => { _bar = v; _art.InvalidateVisual(); });
            _art.InvalidateVisual();
            bool leader = m is { Rank: 1, CpOnly: false, Pvp: false };
            if (leader != _leader) { _leader = leader; _wingArt.InvalidateVisual(); }
            if (climbed) Glint(0);
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
            // the heal mark in full ("✚ Heal") unless the name would be cut for it: then its glyph alone
            if (_healMark is { Child: TextBlock ht }) ht.Text = "✚ " + L.T("Heal");
            var (cp, share) = Mod.Fit(_name, room, _cp.Text.Length > 0 ? _cp : null, _marks, _shareW, min);
            if (_healMark is { Child: TextBlock hs } && _name.MaxWidth < TextW(_name, _name.Text) - .5)
            {
                hs.Text = "✚";
                (cp, share) = Mod.Fit(_name, room, _cp.Text.Length > 0 ? _cp : null, _marks, _shareW, min);
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
            if (_m is not { } m || s.Width < 30) return;
            var r = new Rect(0, 0, s.Width, s.Height);
            var tone = m.ClassColor;
            // the slot: darker than the plate, a royal lift for you
            dc.DrawRectangle(m.You ? YouSlot : Slot, null, r);
            double fx = Math.Round(s.Width * Math.Clamp(_bar, 0, 1), 1);
            if (fx > .5)
            {
                var bar = new Rect(0, 0, fx, s.Height);
                // the body: the class's colour, lit warm at the top and deepening at the foot (its brush made once per class)
                if (_body == null || _bodyFor != tone)
                {
                    // a pale class colour (gold, sand) deepened, so white names hold on it and it never reads as the frame's gold
                    _bodyFor = tone;
                    double lum = (.2126 * tone.R + .7152 * tone.G + .0722 * tone.B) / 255;
                    var t2 = lum > .55 ? Mod.Mix(tone, AbyssC, Math.Min(.4, (lum - .55) * 1.4 + .12)) : tone;
                    _body = Mod.Grad(true, (Mod.A(Mod.Mix(t2, WarmC, lum > .55 ? .12 : .3), .92), 0), (Mod.A(t2, .88), .45), (Mod.A(Mod.Mix(t2, AbyssC, .4), .92), 1));
                }
                dc.DrawRectangle(_body, null, bar);
                // a shade at its left, under the rank and the name
                dc.DrawRectangle(ShadeL, null, new Rect(0, 0, Math.Min(fx, 150), s.Height));
                // a shade at its right, under the figures, so they read wherever a bright bar ends
                double sx = Math.Max(0, s.Width - 140);
                if (fx > sx) { dc.PushClip(new RectangleGeometry(bar)); dc.DrawRectangle(ShadeR, null, new Rect(sx, 0, s.Width - sx, s.Height)); dc.Pop(); }
                // the warm light over its top half
                dc.DrawRectangle(WarmTop, null, bar);
                // the leading edge: a soft glow in the warm light, a fine ivory line fading out downwards (never a hard
                // stroke through a figure that sits over it), and a bead of light where it meets the gold top edge
                double gw = Math.Min(14, fx);
                dc.DrawRectangle(EdgeGlow, null, new Rect(fx - gw, 0, gw, s.Height));
                dc.DrawRectangle(EdgeFade, null, new Rect(Math.Max(0, fx - 1), 0, Math.Min(1, fx), s.Height * .6));
                dc.DrawRectangle(Bead, null, new Rect(Math.Max(0, fx - 2.5), 0, Math.Min(2.5, fx), 2));
                // the gold edge along its top
                dc.DrawRectangle(GoldTop, null, new Rect(0, 0, fx, 1.2));
            }
            // a healer's share of the group's healing as a mint line along the foot (Settings' heal bars)
            if (ParserRows.HealBarsPref && m.HealShare > 0)
                dc.DrawRectangle(HealLine, null, new Rect(0, s.Height - 2, Math.Max(3, s.Width * Math.Clamp(m.HealShare, 0, 1)), 2));
            // you: a gold edge at the row's left, a gold hairline round it
            if (m.You)
            {
                dc.DrawRectangle(null, YouRim, new Rect(.5, .5, s.Width - 1, s.Height - 1));
                dc.DrawRectangle(GoldFill, null, new Rect(0, 0, 3, s.Height));
            }
        }

        /// <summary>
        /// The leader's wings of light (rank I, once there is damage): the frozen pair (<see cref="Wings"/>) at the numeral. They
        /// unfurl by their scale alone (<see cref="Crown"/>).
        /// </summary>
        void DrawWings(DrawingContext dc, Size s)
        {
            if (!_leader) return;
            dc.PushTransform(WingsAt);
            dc.DrawDrawing(Wings);
            dc.Pop();
        }

        static readonly Transform RaysAt = Fz(new TranslateTransform(PadL + RankW / 2, H / 2));

        // the row's brushes, made once
        static readonly Brush Slot = Frozen(Mod.A(AbyssC, .6)), YouSlot = Mod.Grad(false, (Mod.A(RoyalC, .95), 0), (Mod.A(NightC, .8), 1)),
            ShadeL = Lin(new Point(0, 0), new Point(1, 0), (Mod.A(AbyssC, .5), 0), (Mod.A(AbyssC, 0), 1)),
            ShadeR = Lin(new Point(0, 0), new Point(1, 0), (Mod.A(AbyssC, 0), 0), (Mod.A(AbyssC, .36), .6), (Mod.A(AbyssC, .42), 1)),
            WarmTop = Mod.Grad(true, (Mod.A(WarmC, .2), 0), (Mod.A(WarmC, 0), .5)), EdgeGlow = Mod.Grad(false, (Mod.A(WarmC, 0), 0), (Mod.A(WarmC, .3), 1)),
            Bead = Frozen(Color.FromRgb(0xFF, 0xF8, 0xE0)), GoldTop = Lin(new Point(0, 0), new Point(1, 0), (GoldMidC, 0), (GoldHiC, .5), (Color.FromRgb(0xFF, 0xF2, 0xCC), 1)),
            HealLine = Frozen(Mod.A(MintC2, .95)),
            GlintBrush = Lin(new Point(0, 0), new Point(1, 0), (Mod.A(WarmC, 0), 0), (Mod.A(WarmC, .5), .38), (Mod.A(Colors.White, .78), .58), (Mod.A(Colors.White, 0), 1)),
            FlashBrush = MakeFlash();
        static readonly Pen YouRim = Frz(new Pen(Frozen(Mod.A(GoldHiC, .6)), 1));
        static readonly Transform WingsAt = MakeAt();
        static Transform MakeAt() { var t = new TranslateTransform(PadL + RankW / 2, H / 2); t.Freeze(); return t; }
        static Brush MakeFlash()
        {
            var b = new RadialGradientBrush(Mod.A(Colors.White, .85), Mod.A(WarmC, 0));
            b.GradientStops.Add(new GradientStop(Mod.A(GoldHiC, .5), .45));
            b.Freeze();
            return b;
        }

        /// <summary>
        /// The wings, drawn once: three feathers each side of the numeral's centre (0, 0), sweeping up and out in gold with a dark
        /// line round each so they read on a bright bar.
        /// </summary>
        static readonly Drawing Wings = MakeWings();
        static Drawing MakeWings()
        {
            var g = new DrawingGroup();
            var edge = Frz(new Pen(Frozen(Mod.A(AbyssC, .75)), .7));
            double[] ang = { 14, 38, 62 }, len = { 9.5, 8.5, 7 }, wid = { 2.2, 2, 1.7 };
            for (int side = -1; side <= 1; side += 2)
                for (int i = 0; i < 3; i++)
                {
                    double a = ang[i] * Math.PI / 180, l = len[i];
                    var b = new Point(side * 5.2, 7.5);
                    var d = new Vector(side * Math.Sin(a), -Math.Cos(a));
                    var n = new Vector(-d.Y, d.X) * side;   // towards the wing's outside and down
                    var tip = b + d * l;
                    var feather = new StreamGeometry();
                    using (var x = feather.Open())
                    {
                        x.BeginFigure(b, true, true);
                        x.QuadraticBezierTo(b + d * (l * .55) - n * wid[i], tip, true, false);
                        x.QuadraticBezierTo(b + d * (l * .45) + n * (wid[i] * .6), b, true, false);
                    }
                    feather.Freeze();
                    g.Children.Add(new GeometryDrawing(GoldFill, edge, feather));
                }
            g.Freeze();
            return g;
        }

        static readonly LinearGradientBrush EdgeFade = Lin(new Point(0, 0), new Point(0, 1), (Mod.A(IvoryC, .9), 0), (Mod.A(IvoryC, 0), 1));

        /// <summary>
        /// A sunlit glint along this row's bar, after <paramref name="delay"/> ms (a run down the meter): a slanted band of
        /// gold-white light (one frozen brush) crossing the bar, moved by its transform and faded by its opacity, clipped once
        /// to the bar as it stood when the glint began.
        /// </summary>
        public void Glint(double delay)
        {
            const double Ms = 620;
            double fx = _root.ActualWidth * Math.Clamp(_bar, 0, 1);
            if (fx < 6) return;
            var clip = new RectangleGeometry(new Rect(0, 0, fx, H)); clip.Freeze();
            _glintHost.Clip = clip;
            double total = delay + Ms;
            Motion.Play(_glintBand, total, p =>
            {
                double l = Math.Clamp((p * total - delay) / Ms, 0, 1);
                _glintShift.X = -50 + (fx + 70) * Out(l);
                _glintBand.Opacity = l is > 0 and < 1 ? Math.Sin(Math.PI * l) : 0;
            }, () => _glintBand.Opacity = 0);
        }

        /// <summary>
        /// The wings unfurl (this row just took the lead): the great wings spread out from behind the plate's edges past the
        /// row's height with a small overshoot, the numeral's wings with them, god-rays fan up from the numeral and swing
        /// across, and six feathers of light drift off the wings, turning as they fall. One course; transforms and opacity only.
        /// </summary>
        public void Crown()
        {
            _fxCanvas.Children.Clear();
            double w = _root.ActualWidth;
            var drift = new List<(UIElement El, TranslateTransform At, RotateTransform Turn, Point From, Vector By, double Spin, double Start)>();
            if (w > 30)
                for (int k = 0; k < 6; k++)
                {
                    int side = k % 2 == 0 ? -1 : 1, j = k / 2;
                    double x0 = side < 0 ? -Pad - 14 - j * 5 : w + Pad + 14 + j * 5, y0 = H / 2 - 16 + j * 9;
                    var el = new TacSketch((dc, _) => dc.DrawDrawing(LightFeather)) { Opacity = 0 };
                    var at = new TranslateTransform(x0, y0); var turn = new RotateTransform(side * (20 + 25 * j));
                    el.RenderTransform = new TransformGroup { Children = { turn, at } };
                    _fxCanvas.Children.Add(el);
                    drift.Add((el, at, turn, new Point(x0, y0), new Vector(side * (5 + 2 * j), 16 + 5 * j), side * (60 + 30 * j), .12 + .07 * j));
                }
            Motion.Play(_wingArt, 1150, p =>
            {
                double o = Math.Clamp(p / .18, 0, 1);
                double s2 = Back(Seg(p, .05, .55), 1.6);
                _wingScale.ScaleX = .08 + .92 * s2; _wingScale.ScaleY = .5 + .5 * Math.Min(1.08, s2);
                _wingArt.Opacity = o;
                _flash.Opacity = Math.Sin(Math.PI * Seg(p, .05, .6));
                double r = Seg(p, 0, .9);
                _rays.Opacity = .95 * Math.Sin(Math.PI * r);
                _raysScale.ScaleX = _raysScale.ScaleY = .45 + .55 * Out(Seg(p, 0, .45));
                _raysTurn.Angle = -9 + 18 * Out(r);
                foreach (var (el, at, turn, from, by, spin, start) in drift)
                {
                    double q = Seg(p, start, 1);
                    at.X = from.X + by.X * Out(q); at.Y = from.Y + by.Y * q;
                    turn.Angle = spin * q;
                    el.Opacity = Math.Sin(Math.PI * q);
                }
            }, () =>
            {
                _wingScale.ScaleX = _wingScale.ScaleY = 1; _wingArt.Opacity = 1; _flash.Opacity = 0; _rays.Opacity = 0;
                _fxCanvas.Children.Clear();
            });
        }
    }
}
