using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using KinahLedger.Parser;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

sealed partial class HudHype
{
    /// <summary>A name of up to this many characters always shows whole (the crowding rule's floor).</summary>
    const int NameKeep = 10;

    /// <summary>A text's width in a block's own face, size and weight (a figure column sized for its widest form).</summary>
    static double TextW(TextBlock like, string s)
    {
        var probe = new TextBlock { Text = s, FontFamily = like.FontFamily, FontSize = like.FontSize, FontWeight = like.FontWeight, FontStretch = like.FontStretch, FontStyle = like.FontStyle, Padding = like.Padding };
        TextOptions.SetTextFormattingMode(probe, TextFormattingMode.Display);
        System.Windows.Documents.Typography.SetNumeralAlignment(probe, FontNumeralAlignment.Tabular);
        probe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return Math.Ceiling(probe.DesiredSize.Width);
    }

    /// <summary>A figure column's width: the widest of <paramref name="forms"/>, the block's margins and a pixel of air.</summary>
    static double FigCol(TextBlock fig, params string[] forms) => forms.Max(f => TextW(fig, f)) + fig.Margin.Left + fig.Margin.Right + 1;

    /// <summary>A member's bar, 0 to 1, against the fight's top (by Combat Power before any damage).</summary>
    static double BarOf(MeterMember m, MeterContext c) => c.E.BarOf(m.F);   // against the top member's: the top bar full, the rest in proportion (2026-11-20)

    /// <summary>The fight's foot: "TOTAL 622.4M" at the left, the group's word and figure at the right, the AVG mark on a held average.</summary>
    FrameworkElement Foot(Encounter e, DateTime now)
    {
        var (total, word, group, held) = FootOf(e, now);
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        TextBlock Pair(string w, string fig)
        {
            var t = Ht("", 12, Frozen(Mod.A(LimeInkC, .95)), FontWeights.Bold);
            t.Inlines.Add(new System.Windows.Documents.Run(w.ToUpperInvariant() + " "));
            t.Inlines.Add(new System.Windows.Documents.Run(fig) { FontWeight = FontWeights.Bold, Foreground = White, FontSize = 13.5 });
            return t;
        }
        var tp = Pair(L.T("Total"), total);
        EnergyMark.After(tp, 11.5, Frozen(Mod.A(Colors.White, .72)));   // Odyle Energy (Ui/EnergyMark.cs)
        g.Children.Add(tp);
        var right = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 0, 0, 0) };
        if (word.Length > 0) right.Children.Add(Pair(word, group));
        if (held)
        {
            var avg = Mark(L.T("AVG"), size: 9.5); avg.Tag = "avg-badge"; avg.ToolTip = L.T(Encounter.AvgBadge); avg.Margin = new Thickness(6, 0, 0, 0);
            right.Children.Add(avg);
        }
        Grid.SetColumn(right, 1); g.Children.Add(right);
        return AsFoot(Shaded(g), e, now);
    }

    /// <summary>The line between the party and you pinned below it: "2 MORE" between two lime dashes.</summary>
    static FrameworkElement GapLine(int left)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        FrameworkElement Dash() => new TacSketch((dc, s) => dc.DrawGeometry(Frozen(Mod.A(LimeC, .7)), null, Para(new Rect(0, 0, s.Width, s.Height), 2)), 18, 2.5) { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 6, 0) };
        sp.Children.Add(Dash());
        sp.Children.Add(Ht(left > 0 ? L.F("{0} more", left).ToUpperInvariant() : "·", 11, Frozen(Mod.A(Colors.White, .86)), FontWeights.SemiBold));
        sp.Children.Add(Dash());
        return Shaded(sp);
    }

    /// <summary>
    /// The rows' shared fit: the share column holds or goes for every row at once, so the figure plates line up down the
    /// meter. Each row says whether it needs the share gone (its CP gone and its name at ten letters still don't fit).
    /// </summary>
    sealed class HypeFit
    {
        readonly Dictionary<HypeRow, bool> _need = new();
        public bool Off { get; private set; }

        public void Report(HypeRow row, bool need)
        {
            _need[row] = need;
            foreach (var gone in _need.Keys.Where(r => r.Root.Parent == null && r != row).ToList()) _need.Remove(gone);
            bool off = _need.Values.Any(x => x);
            if (off == Off) return;
            Off = off;
            foreach (var r in _need.Keys.Where(r => r != row).ToList()) r.Layout();
        }
    }

    /// <summary>Who leads the fight (rank 1 with damage): a change of leader slams the bolt badge in.</summary>
    sealed class HypeLead { public string Fight = ""; public long Id; }

    /// <summary>
    /// One member as an angled bar (the Hype package): a steel track leaning right; on it, from after the rank badge, the bar
    /// to the member's part of the top in their class's colour, dark at its left and full at its leading edge, a line of light
    /// along its top and a chrome cap at its end. The leader's bar is electric lime with a hard lime glow round it, and their
    /// badge is lime with a magenta lightning bolt; everyone else's badge is steel with a chrome edge. The DPS (italic, big)
    /// and the share sit on a black plate at the row's end behind a chrome edge, so a figure never sits on a bright bar. You:
    /// a magenta stripe at the row's left. The name in white with CP small after it, then the marks (★ DPS, ✚, deaths, YOU),
    /// a healer's healing a second in green before the plate. Short of room the CP goes first, then the share for every row;
    /// a name keeps at least ten characters; a figure is never cut.
    /// </summary>
    sealed class HypeRow : IThemeRow, IThemeBarSpan
    {
        public const double H = 26, Pitch = 29;
        const double PadL = 5, PadR = 7, BadgeW = 31, IconW = 24, Slant = 5;
        readonly HypeFit _fit;
        readonly HypeLead _lead;
        readonly HudHype _look;
        readonly Grid _root = new() { Height = H };
        readonly TacSketch _art, _badgeArt;
        /// <summary>The burst's glint under the words (clipped to the bar) and its flash and shockwave over them: built on the first burst, moved by transforms.</summary>
        readonly Canvas _glintHost = new() { IsHitTestVisible = false, Visibility = Visibility.Collapsed }, _fx = new() { IsHitTestVisible = false, Visibility = Visibility.Collapsed };
        readonly TranslateTransform _glintAt = new();
        System.Windows.Shapes.Path? _flash;
        (System.Windows.Shapes.Ellipse El, ScaleTransform Sc)[]? _rings;
        readonly Grid _words = new() { Margin = new Thickness(PadL, 0, PadR, 0) };
        readonly Grid _badge = new() { Width = 26, Height = 19, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        readonly ScaleTransform _badgeScale = new(1, 1);
        readonly RotateTransform _badgeTurn = new(0);
        readonly TextBlock _rank = Ht("", 13, White, FontWeights.Bold), _name = Ht("", 15, White, FontWeights.Bold), _cp = Ht("", 11.5, Frozen(Mod.A(Colors.White, .92)), FontWeights.SemiBold),
            _heal = Ht("", 11.5, Heal, FontWeights.Bold), _dps = Ht("", 18.5, White, FontWeights.Bold), _share = Ht("", 11.5, Frozen(Mod.A(Colors.White, .97)), FontWeights.Bold),
            _dmg = Ht("", 11.5, Soft, FontWeights.SemiBold), _crit = Ht("", 11, Soft, FontWeights.SemiBold);
        readonly StackPanel _line = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        readonly Border _iconHost = new() { Width = 18, Height = 18, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
        readonly List<FrameworkElement> _marks = new();
        /// <summary>The marks that shorten to their glyph when crowded (★ DPS, ✚ HEAL): full and brief forms, one shown.</summary>
        readonly List<(FrameworkElement Full, FrameworkElement Brief)> _give = new();
        readonly Grid _healChip, _cpHost;
        readonly double _dpsW, _shareW, _dmgW, _critW;
        string _cls = "\0";
        MeterMember? _m;
        double _bar;
        bool _expanded, _leader;

        public FrameworkElement Root => _root;

        /// <summary>Where a FULL bar is drawn (main's PB ghost, IThemeBarSpan): in the bar's art, from after the badge to the figure plate.</summary>
        public (FrameworkElement On, Rect Track)? BarTrack => _art.ActualWidth < 40 ? null
            : (_art, new Rect(PadL + BadgeW - 3, 1.5, BarEnd(_art.ActualWidth) - (PadL + BadgeW - 3), Math.Max(0, _art.ActualHeight - 3)));

        public HypeRow(HypeFit fit, HypeLead lead, HudHype look)
        {
            _fit = fit; _lead = lead; _look = look;
            _dps.Tag = "row-dps"; _share.Tag = "row-share"; _heal.Tag = "heal-figures"; _cp.Tag = "cp";
            _dps.HorizontalAlignment = _share.HorizontalAlignment = _dmg.HorizontalAlignment = _crit.HorizontalAlignment = HorizontalAlignment.Right;
            _share.Margin = new Thickness(0, 3, 0, 0); _dmg.Margin = new Thickness(0, 1, 0, 0); _crit.Margin = new Thickness(0, 1, 0, 0);
            // a healer's healing on a small black tag: it reads on any bar's bright end
            _healChip = Chip(_heal, Frozen(Mod.A(InkC, .8)), null, new Thickness(4, 0, 5, 1), 3);
            _healChip.Margin = new Thickness(5, 0, 2, 0);
            // the columns: the badge, the class art, the name's line (the rest), heal, the DPS, the share; expanded, the damage and the crit rate
            _dpsW = FigCol(_dps, "888.8K") + 2; _shareW = FigCol(_share, "88.8%", "100%", L.T("CP")) + 6;
            _dmgW = FigCol(_dmg, "888.88M") + 8; _critW = FigCol(_crit, "88%") + 8;
            foreach (var w in new[] { BadgeW, IconW, double.NaN, 0, _dpsW, _shareW, 0, 0 })
                _words.ColumnDefinitions.Add(new ColumnDefinition { Width = double.IsNaN(w) ? new GridLength(1, GridUnitType.Star) : w == 0 ? GridLength.Auto : new GridLength(w) });
            _words.ColumnDefinitions[6].Width = new GridLength(0); _words.ColumnDefinitions[7].Width = new GridLength(0);
            _art = new TacSketch(Draw);
            var glint = new Canvas { RenderTransform = _glintAt };
            glint.Children.Add(new System.Windows.Shapes.Path { Data = Para(new Rect(0, 0, 36, H), 9), Fill = GlintLead });
            glint.Children.Add(new System.Windows.Shapes.Path { Data = Para(new Rect(36, 0, 7, H), 9), Fill = GlintCore });
            _glintHost.Children.Add(glint);
            _root.Children.Add(_art);
            // the badge: a slanted tag, the rank in it; the leader's lime with the bolt
            _badgeArt = new TacSketch(DrawBadge);
            _badge.Children.Add(_badgeArt);
            _rank.HorizontalAlignment = HorizontalAlignment.Center; _rank.Margin = new Thickness(3, -1, 0, 0);
            _badge.Children.Add(_rank);
            _badge.RenderTransformOrigin = new Point(.5, .5);
            _badge.RenderTransform = new TransformGroup { Children = { _badgeScale, _badgeTurn } };
            Put(_badge, 0); Put(_iconHost, 1); Put(_line, 2); Put(_healChip, 3); Put(_dps, 4); Put(_share, 5); Put(_dmg, 6); Put(_crit, 7);
            _name.TextTrimming = TextTrimming.CharacterEllipsis;
            // the CP on a small dark tag: it reads wherever the bar's end or cap falls under it
            _cpHost = Chip(_cp, Frozen(Mod.A(InkC, .74)), null, new Thickness(3, 0, 4, 1), 3);
            _cpHost.Margin = new Thickness(5, 0, 0, 0);
            _line.Children.Add(_name); _line.Children.Add(_cpHost);
            _root.Children.Add(_glintHost);
            _root.Children.Add(_words);
            _root.Children.Add(_fx);
            _root.SizeChanged += (_, _) => Fit();
        }

        void Put(FrameworkElement el, int col) { Grid.SetColumn(el, col); _words.Children.Add(el); }

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
            bool leader = m.Rank == 1 && !m.CpOnly;
            if (leader != _leader) { _leader = leader; _rank.Foreground = leader ? Lime : White; _badgeArt.InvalidateVisual(); }
            if (m.Class != _cls)
            {
                _cls = m.Class; _tick = null;
                var icon = ThemeText.Icon(m.Class, 18, m.Name);
                if (icon is Border b) { b.BorderBrush = Chrome; b.BorderThickness = new Thickness(1.2); }
                _iconHost.Child = icon;
            }
            _name.Text = m.Name;
            _cp.Text = m.Cp;
            // the marks after the name: ★ DPS, a healer's ✚, deaths, YOU (kept whole: the name and the CP give way first)
            foreach (var mk in _marks) _line.Children.Remove(mk);
            _marks.Clear();
            void Add(FrameworkElement el) { el.Margin = new Thickness(5, 0, 0, 0); _line.Children.Add(el); _marks.Add(el); }
            _give.Clear();
            void Give(FrameworkElement full, FrameworkElement brief) { Add(full); Add(brief); _give.Add((full, brief)); }
            if (m.TopDps)
            {
                var t = Mark("★ " + L.T("DPS")); t.Tag = "top-dps"; t.ToolTip = L.T("Top DPS");
                var b = Mark("★"); b.Tag = "top-dps"; b.ToolTip = L.T("Top DPS");
                Give(t, b);
            }
            if (m.TopHeals)
            {
                var t = Mark(("✚ " + L.T("Heal")).ToUpperInvariant(), Heal, HealC); t.Tag = "top-heals"; t.ToolTip = L.T("Top heals");
                var b = Mark("✚", Heal, HealC); b.Tag = "top-heals"; b.ToolTip = L.T("Top heals");
                Give(t, b);
            }
            if (m.Deaths > 0) { var d = Ht($"✕{m.Deaths}", 11, MagInk, FontWeights.Bold); d.Tag = "deaths"; d.ToolTip = L.F(m.Deaths == 1 ? "{0} death" : "{0} deaths", m.Deaths); Add(d); }
            if (m.You) { var y = Mark(L.T("YOU"), filled: true); y.Tag = "you"; Add(y); }
            _heal.Text = m.Heal.Length > 0 ? "+" + m.Heal : "";
            _healChip.Visibility = m.Heal.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            _dps.Text = m.Figure;
            _dps.FontSize = m.CpOnly ? 15 : 18.5;
            // a wider figure (Combat Power before the first hit, "167,548") widens its column: never a digit cut
            _words.ColumnDefinitions[4].Width = new GridLength(Math.Max(_dpsW, TextW(_dps, m.Figure) + 2));
            _share.Text = m.ShareText;
            _dmg.Text = c.Expanded ? m.Damage : "";
            _crit.Text = c.Expanded ? m.Crit : "";
            Fit();
            SlideBar(_art, c, "bar", BarOf(m, c), v => { _bar = v; _art.InvalidateVisual(); });
            _art.InvalidateVisual();
            // a new leader (seen while effects play): the bolt badge slams onto them and the bars shine
            if (leader)
            {
                bool changed = _lead.Fight == c.E.Id && _lead.Id != 0 && _lead.Id != m.Id;
                _lead.Fight = c.E.Id; _lead.Id = m.Id;
                if (changed && c.Moves) _look.Burst(crown: true);
            }
        }

        /// <summary>The name line's room with the share's column counted in (the row's width less every other column).</summary>
        double Room()
        {
            double total = _root.ActualWidth;
            if (total <= 0) return double.NaN;
            var cols = _words.ColumnDefinitions;
            double heal = _healChip.Visibility == Visibility.Visible ? Mod.W(_healChip) : 0;
            return total - PadL - PadR - BadgeW - IconW - heal - cols[4].Width.Value - cols[6].Width.Value - cols[7].Width.Value - 2;
        }

        /// <summary>The narrowest the name goes: its first ten letters and the ellipsis (a shorter name, whole).</summary>
        double NameMin() => _name.Text.Length <= NameKeep ? TextW(_name, _name.Text) : TextW(_name, _name.Text[..NameKeep] + "…") + 1;

        /// <summary>The crowding rule: the CP goes first, then the share (for every row); the name keeps at least ten characters.</summary>
        void Fit()
        {
            double room = Room();
            if (double.IsNaN(room)) return;
            _name.ClearValue(FrameworkElement.MaxWidthProperty);
            double least = Math.Min(Mod.W(_name), NameMin()) + Kept() + _give.Sum(g => Mod.W(g.Brief));
            _fit.Report(this, least + _shareW > room + .5);
            Layout();
        }

        /// <summary>The row laid out at the meter's fit: the share's column, the CP and the name's width.</summary>
        internal void Layout()
        {
            double room = Room();
            if (double.IsNaN(room)) return;
            bool share = !_fit.Off;
            var cols = _words.ColumnDefinitions;
            if (cols[5].Width.Value != (share ? _shareW : 0)) cols[5].Width = new GridLength(share ? _shareW : 0);
            _share.Visibility = share ? Visibility.Visible : Visibility.Collapsed;
            _name.ClearValue(FrameworkElement.MaxWidthProperty);
            double nw = Mod.W(_name), cpw = _cp.Text.Length > 0 ? Mod.W(_cpHost) : 0, kept = Kept();
            double full = _give.Sum(g => Mod.W(g.Full)), brief = _give.Sum(g => Mod.W(g.Brief));
            double room0 = room - (share ? _shareW : 0) - kept;
            // the CP goes first, then the marks shorten to their glyphs, then the name trims (the share went above, for every row)
            bool cp = cpw > 0 && nw + cpw + full <= room0 + .5;
            bool longMarks = nw + full <= room0 + .5;
            foreach (var (f, b) in _give) { f.Visibility = longMarks ? Visibility.Visible : Visibility.Collapsed; b.Visibility = longMarks ? Visibility.Collapsed : Visibility.Visible; }
            double avail = room0 - (longMarks ? full : brief);
            _cpHost.Visibility = cp ? Visibility.Visible : Visibility.Collapsed;
            _name.MaxWidth = Math.Max(Math.Min(NameMin(), nw), avail - (cp ? cpw : 0));
            _art.InvalidateVisual();
        }

        /// <summary>The width of the marks that never shorten (deaths, YOU).</summary>
        double Kept() => _marks.Where(mk => !_give.Any(g => g.Full == mk || g.Brief == mk)).Sum(Mod.W);

        /// <summary>The figure plate's left edge in the row: the DPS, the share, the damage and the crit rate's columns and air.</summary>
        double PlateX(double width)
        {
            var cols = _words.ColumnDefinitions;
            return width - PadR - cols[4].Width.Value - cols[5].Width.Value - cols[6].Width.Value - cols[7].Width.Value - 7;
        }

        void Draw(DrawingContext dc, Size s)
        {
            if (_m is not { } m || s.Width < 40) return;
            double h = s.Height, px = PlateX(s.Width), x0 = PadL + BadgeW - 3;
            // the steel track the whole row long
            dc.DrawGeometry(Frozen(Mod.A(TrackC, .92)), null, Para(new Rect(0, 0, s.Width, h), Slant));
            dc.DrawRectangle(Frozen(Mod.A(Colors.White, .08)), null, new Rect(Slant, 0, s.Width - Slant, 1));
            // the bar: to the member's part of the top, from after the badge to the row's end, running on under the figures'
            // plate (Steven, 2026-11-21: expanded, the leader's bar stopped at two thirds of the row)
            double full = BarEnd(s.Width) - x0, bw = full * Math.Clamp(_bar, 0, 1);
            if (bw > 1.5)
            {
                var r = new Rect(x0, 1.5, bw, h - 3);
                var g = Para(r, Slant - .4);
                if (_leader)
                {
                    // the leader: electric lime with a hard glow round it (two outlines, no blur)
                    dc.DrawGeometry(null, Mod.P(Frozen(Mod.A(LimeC, .22)), 5), g);
                    dc.DrawGeometry(LeaderFill, null, g);
                    dc.DrawGeometry(null, Mod.P(Frozen(Mod.A(LimeC, .95)), 1.1), g);
                }
                else dc.DrawGeometry(m.You ? YouFill : m.CpOnly ? GunFillDim : GunFill, null, g);
                dc.PushClip(g);
                dc.DrawRectangle(Frozen(Mod.A(Colors.White, _leader ? .4 : .3)), null, new Rect(x0, r.Top, bw, 1));
                // the member's class: a tick of its colour at the bar's left
                dc.DrawGeometry(_tick ??= Frozen(m.ClassColor), null, Para(new Rect(x0 - 1, r.Top, 7 + Slant, r.Height), Slant));
                dc.Pop();
            }
            // a healer's share of the group's healing as a green line along the track's foot (Settings' heal bars)
            if (ParserRows.HealBarsPref && m.HealShare > 0)
                dc.DrawGeometry(Frozen(Mod.A(HealC, .95)), null, Para(new Rect(x0, h - 2.5, Math.Max(4, (px - x0) * Math.Clamp(m.HealShare, 0, 1)), 2.5), 1));
            // the figures' black plate, its chrome edge: smoked glass the bar runs on under, dark enough that every digit reads
            dc.DrawGeometry(PlateShade(px, s.Width), null, Para(new Rect(px, 0, s.Width - px, h), Slant));
            dc.DrawLine(Mod.P(Chrome, 1.6), new Point(px + Slant, 0), new Point(px, h));
            if (_leader) dc.DrawGeometry(Frozen(LimeC), null, Para(new Rect(px + Slant + 2, 0, s.Width - px - Slant - 2, 2), 0));
            // the bar's light and its chrome cap over the plate, so a full bar reads to the row's end (Steven, 2026-11-22: "why is
            // dmg bar so short here": at 100% the lime seemed to stop at the plate's edge, the plate too dark over it)
            if (bw > 1.5 && x0 + bw > px)
            {
                var g = Para(new Rect(x0, 1.5, bw, h - 3), Slant - .4);
                dc.PushClip(Para(new Rect(px, 0, s.Width - px, h), Slant));
                dc.PushClip(g);
                dc.DrawRectangle(Frozen(Mod.A(_leader ? LimeC : Colors.White, _leader ? .55 : .3)), null, new Rect(x0, 1.5, bw, 1.2));
                dc.DrawRectangle(Frozen(Mod.A(_leader ? LimeC : Colors.White, _leader ? .35 : .18)), null, new Rect(x0, h - 3, bw, 1.5));
                dc.Pop(); dc.Pop();
                if (_leader) dc.DrawGeometry(null, Mod.P(Frozen(Mod.A(LimeC, .7)), 1), g);
            }
            if (bw > 8) dc.DrawGeometry(Chrome, Mod.P(Frozen(Mod.A(Colors.Black, .6)), .8), Para(new Rect(x0 + bw - 5.5, 1.5, 5.5, h - 3), Slant - .4));
            // you: a magenta stripe at the row's left
            if (m.You) dc.DrawGeometry(Frozen(MagC), null, Para(new Rect(0, 0, 3 + Slant, h), Slant));
        }

        /// <summary>Where a full bar ends: at the row's right end, under the figures' plate.</summary>
        static double BarEnd(double w) => w - 2;

        /// <summary>The figures' plate as smoked glass: lighter at its chrome edge (the bar glows through), darker under the figures.</summary>
        static Brush PlateShade(double px, double w) => PlateShadeB;   // across the plate's own box: one frozen brush for every row

        static readonly LinearGradientBrush PlateShadeB = Mod.Grad(false, (Mod.A(InkC, .3), 0), (Mod.A(InkC, .42), .25), (Mod.A(InkC, .5), 1));

        /// <summary>The badge: a slanted tag with the rank in it; the leader's in lime with a magenta lightning bolt at its corner.</summary>
        void DrawBadge(DrawingContext dc, Size s)
        {
            var tag = Para(new Rect(.5, .5, s.Width - 1, s.Height - 1), 4);
            if (_leader)
            {
                // a solid lime bolt half again the row's height behind the badge, cutting past the row and the plate's edge
                dc.DrawGeometry(Frozen(LimeC), Mod.P(Black, 1.6), LeaderBolt);
                dc.DrawGeometry(Black, Mod.P(Frozen(LimeC), 1.4), tag);
            }
            else dc.DrawGeometry(Frozen(SteelC), Mod.P(Chrome, 1), tag);
        }

        static readonly LinearGradientBrush LeaderFill = Mod.Grad(false, (LimeDeepC, 0), (Mod.Mix(LimeDeepC, LimeC, .42), .62), (LimeC, 1));
        /// <summary>The bars' two colours read from across the room: lime for the leader (above), magenta for you, gunmetal chrome for the rest.</summary>
        static readonly LinearGradientBrush YouFill = Mod.Grad(false, (Color.FromRgb(0x3A, 0x06, 0x30), 0), (Color.FromRgb(0x9E, 0x12, 0x84), .7), (MagC, 1)),
            GunFill = Mod.Grad(false, (Color.FromRgb(0x24, 0x28, 0x2F), 0), (Color.FromRgb(0x4C, 0x53, 0x5E), .72), (Color.FromRgb(0x9A, 0xA3, 0xAE), 1)),
            GunFillDim = Mod.Grad(false, (Mod.A(Color.FromRgb(0x24, 0x28, 0x2F), .6), 0), (Mod.A(Color.FromRgb(0x4C, 0x53, 0x5E), .6), .72), (Mod.A(Color.FromRgb(0x9A, 0xA3, 0xAE), .6), 1));
        SolidColorBrush? _tick;
        static readonly Geometry LeaderBolt = Bolt(new Rect(-13, -11, 24, 40));
        static readonly LinearGradientBrush GlintLead = Mod.Grad(false, (Mod.A(Colors.White, 0), 0), (Mod.A(Colors.White, .45), 1));
        static readonly SolidColorBrush GlintCore = Frozen(Mod.A(Colors.White, .6));

        /// <summary>The bar's shape as drawn now (the glint's clip), or null with no bar to speak of.</summary>
        Geometry? BarShape()
        {
            double w = _root.ActualWidth, h = _root.ActualHeight;
            if (w < 40 || h < 4) return null;
            double x0 = PadL + BadgeW - 3, bw = (BarEnd(w) - x0) * Math.Clamp(_bar, 0, 1);
            return bw > 6 ? Para(new Rect(x0, 1.5, bw, h - 3), Slant - .4) : null;
        }

        /// <summary>The flash and the two shockwave rings, built the first time this row is crowned.</summary>
        void BuildCrownFx()
        {
            if (_flash != null) return;
            _flash = new System.Windows.Shapes.Path { Fill = Frozen(Mod.A(Colors.White, .8)), Opacity = 0 };
            _fx.Children.Add(_flash);
            var rings = new List<(System.Windows.Shapes.Ellipse, ScaleTransform)>();
            foreach (var (c, w, rx, ry) in new[] { (LimeC, 2.6, 26.0, 19.0), (MagC, 1.8, 19.0, 14.0) })
            {
                var sc = new ScaleTransform(.3, .3, rx, ry);
                var el = new System.Windows.Shapes.Ellipse { Width = 2 * rx, Height = 2 * ry, Stroke = Frozen(c), StrokeThickness = w, RenderTransform = sc, Opacity = 0 };
                Canvas.SetLeft(el, PadL + 13 - rx); Canvas.SetTop(el, H / 2 - ry);
                _fx.Children.Add(el);
                rings.Add((el, sc));
            }
            _rings = rings.ToArray();
        }

        /// <summary>
        /// The burst on this row, <paramref name="delay"/> ms after the top's: a chrome glint racing along the bar (a canvas
        /// clipped to the bar, moved by a transform); with <paramref name="crown"/>, the bolt badge slams in (from 2.4x, squashing
        /// on impact, a twist), a lime and a magenta shockwave ring go out round it, and the row takes one white frame. Only
        /// transforms and opacities change while it plays.
        /// </summary>
        public void Shine(double delay, bool crown)
        {
            const double GlintMs = 560, RingMs = 620, ImpactMs = 228;
            double total = Math.Max(delay + GlintMs, crown ? ImpactMs + RingMs + 40 : 0);
            if (crown) BuildCrownFx();
            bool placed = false;
            double reach = 0;
            Motion.Play(_fx, total, t =>
            {
                if (!placed)
                {
                    // once, when the row has its size: the glint's clip to the bar, the flash's shape
                    if (_root.ActualWidth < 40) return;
                    placed = true;
                    var bar = BarShape();
                    _glintHost.Clip = bar;
                    _glintHost.Visibility = bar != null ? Visibility.Visible : Visibility.Collapsed;
                    reach = bar?.Bounds.Right ?? 0;
                    if (_flash != null) _flash.Data = Para(new Rect(0, 0, _root.ActualWidth, _root.ActualHeight), Slant);
                    _fx.Visibility = crown ? Visibility.Visible : Visibility.Collapsed;
                }
                double at = t * total, g = (at - delay) / GlintMs;
                if (g is > 0 and < 1) { _glintAt.X = -50 + (reach + 60) * g; _glintHost.Opacity = Math.Sin(Math.PI * g); }
                else _glintHost.Opacity = 0;
                if (crown && _flash != null && _rings != null)
                {
                    // on impact (the badge lands 230 ms in): one white frame on the row, the shockwaves go out
                    _flash.Opacity = at is >= ImpactMs and < ImpactMs + 50 ? 1 : 0;
                    for (int i = 0; i < _rings.Length; i++)
                    {
                        double rg = Math.Clamp((at - ImpactMs - i * 80) / RingMs, 0, 1);
                        var (el, sc) = _rings[i];
                        sc.ScaleX = sc.ScaleY = .3 + .7 * Out(rg);
                        el.Opacity = rg is > 0 and < 1 ? .95 * (1 - rg) : 0;
                    }
                }
            }, () => { _glintHost.Visibility = Visibility.Collapsed; _fx.Visibility = Visibility.Collapsed; });
            if (!crown) return;
            Motion.Play(_badge, 760, t =>
            {
                // the slam: it falls in from 2.4x, faster and faster, squashes to 0.78 on impact, then springs back to its size
                double s;
                if (t < .3) { double q = t / .3; s = 1.9 - 1.12 * q * q; }
                else { double q = Seg(t, .3, 1); s = 1 - .22 * Math.Pow(1 - q, 2) * Math.Cos(q * Math.PI * 2.5); }
                _badgeScale.ScaleX = _badgeScale.ScaleY = s;
                _badgeTurn.Angle = -16 * (1 - Seg(t, 0, .3));
            }, () => { _badgeScale.ScaleX = _badgeScale.ScaleY = 1; _badgeTurn.Angle = 0; });
        }
    }
}
