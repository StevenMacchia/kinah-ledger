using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using KinahLedger.Parser;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

sealed partial class HudMagma
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

    /// <summary>A member's bar, 0 to 1, against the fight's top.</summary>
    static double BarOf(MeterMember m, MeterContext c) => c.E.BarOf(m.F);   // against the top member's: the top bar full, the rest in proportion (2026-11-20)

    /// <summary>A member's seed for their row's crust (the plates stay put from draw to draw).</summary>
    static int SeedOf(long id) => (int)(Math.Abs(id) % 9973) + 1;

    /// <summary>The fight's foot after its lava seam: "Total 622.4M" at the left, the group's word and figure at the right, the AVG mark on a held average.</summary>
    FrameworkElement Foot(Encounter e, DateTime now)
    {
        var (total, word, group, held) = FootOf(e, now);
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        TextBlock Pair(string w, string fig)
        {
            var t = new TextBlock { FontFamily = Mod.Face, FontSize = 11, FontWeight = FontWeights.Medium, Foreground = Quiet, VerticalAlignment = VerticalAlignment.Center };
            System.Windows.Documents.Typography.SetNumeralAlignment(t, FontNumeralAlignment.Tabular);
            t.Inlines.Add(new System.Windows.Documents.Run(w + " "));
            t.Inlines.Add(new System.Windows.Documents.Run(fig) { FontWeight = FontWeights.Bold, Foreground = White, FontSize = 12 });
            return t;
        }
        var tp = Pair(L.T("Total"), total);
        EnergyMark.After(tp, 11, Quiet);   // Odyle Energy (Ui/EnergyMark.cs)
        g.Children.Add(tp);
        var right = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 0, 0, 0) };
        if (word.Length > 0) right.Children.Add(Pair(word, group));
        if (held)
        {
            var avg = Chip(L.T("AVG"), Soft, LavaHotC, size: 8.5); avg.Tag = "avg-badge"; avg.ToolTip = L.T(Encounter.AvgBadge); avg.Margin = new Thickness(6, 0, 0, 0);
            right.Children.Add(avg);
        }
        Grid.SetColumn(right, 1); g.Children.Add(right);
        return AsFoot(Shaded(g), e, now);
    }

    /// <summary>The line between the party and you pinned below it: "2 MORE" between two lava seams.</summary>
    static FrameworkElement GapLine(int left)
    {
        var g = new Grid { VerticalAlignment = VerticalAlignment.Center };
        g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); g.ColumnDefinitions.Add(new ColumnDefinition());
        var t = Tt(left > 0 ? L.F("{0} more", left).ToUpperInvariant() : "·", 9, Soft, FontWeights.SemiBold);
        t.Margin = new Thickness(8, 0, 8, 0);
        Grid.SetColumn(t, 1); g.Children.Add(Shaded(t));
        var l = Seam(new Thickness(0), .7); l.VerticalAlignment = VerticalAlignment.Center;
        var r = Seam(new Thickness(0), .7); r.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(r, 2); g.Children.Add(l); g.Children.Add(r);
        return g;
    }

    /// <summary>
    /// One member as a channel cut in the basalt with their molten flow in it (<see cref="Flow"/>): the flow's length their part
    /// of the top, cooled crust floating on its root, a yellow-white front, the leader's running hottest. The class's colour
    /// stays as a stripe down the channel's left edge and the ring round the class art. On it: the rank (the top row wears the
    /// molten crown over its lava pool instead), the art, the name in white, Combat Power small, the marks (★ DPS, a healer's
    /// ✚, deaths, YOU white-hot), a healer's healing a second in mint, the DPS big over a warm underglow and the share small.
    /// You: the channel's rim glows orange. Short of room the CP goes first, then the share; a name keeps at least ten
    /// characters; a figure is never cut (Mod.Fit).
    /// Its moments: the crown rises out of the pool when this row takes the lead (white-hot, cooling to orange); a row that
    /// climbs a rank flares its rim and runs a molten streak along its foot; the heat pulse and haze roll along its flow.
    /// </summary>
    sealed class MagmaRow : IThemeRow
    {
        public const double H = 26, Pitch = 29;
        const double PadL = 7, PadR = 7, RankW = 19, IconW = 23, BandW = 48;
        readonly HudMagma _look;
        readonly Grid _root = new() { Height = H };
        readonly TacSketch _art;
        readonly Grid _words = new() { Margin = new Thickness(PadL, 0, PadR, 0) };
        readonly TextBlock _rank = Tt("", 12, Frozen(Color.FromRgb(0xFF, 0xE6, 0xCC)), FontWeights.Bold), _name = Tt("", 12.5, White, FontWeights.Bold), _cp = Tt("", 9.5, Frozen(Color.FromArgb(0xE6, 0xF2, 0xD8, 0xC4)), FontWeights.SemiBold),
            _heal = Tt("", 10.5, Mint2, FontWeights.Bold), _dps = Tt("", 16, White, FontWeights.Bold), _share = Tt("", 10, Soft, FontWeights.SemiBold),
            _dmg = Tt("", 11, Soft, FontWeights.SemiBold), _crit = Tt("", 10.5, Soft, FontWeights.SemiBold);
        readonly StackPanel _line = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        readonly Grid _rankHost = new() { Width = RankW, Height = H };
        readonly Border _iconHost = new() { Width = 18, Height = 18, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
        readonly List<FrameworkElement> _marks = new();
        readonly double _dpsW, _shareW, _dmgW, _critW;

        // the moments' layers, each drawn once and moved by its transform and opacity alone (collapsed while still)
        readonly Canvas _pulseFx = new() { IsHitTestVisible = false, ClipToBounds = true, Visibility = Visibility.Collapsed };
        readonly TacSketch _band, _haze;
        readonly TranslateTransform _bandX = new(), _hazeY = new();
        readonly TacSketch _flare = new(DrawFlare, 34, H) { Visibility = Visibility.Collapsed, HorizontalAlignment = HorizontalAlignment.Left };
        readonly TranslateTransform _flareX = new();
        readonly Grid _climbFx = new() { IsHitTestVisible = false, Visibility = Visibility.Collapsed };
        readonly ScaleTransform _streakS = new(0, 1);
        readonly TacSketch _climbRim;
        // the crown: its pool, the crown (cool, and a white-hot copy over it), the pool's flare, two drops and a flash
        readonly Grid _crownHost = new() { Visibility = Visibility.Collapsed, IsHitTestVisible = false };
        readonly Grid _crownMove = new();
        readonly TranslateTransform _crownY = new();
        readonly TacSketch _crownHot, _poolFlare, _flash;
        readonly System.Windows.Shapes.Ellipse[] _drops = new System.Windows.Shapes.Ellipse[2];
        readonly TranslateTransform[] _dropT = { new(), new() };

        string _cls = "\0";
        MeterMember? _m;
        double _bar;
        int _lastRank, _seed = 1;
        bool _expanded, _shareOn = true, _crown;

        public FrameworkElement Root => _root;

        public MagmaRow(HudMagma look)
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
            _root.Children.Add(_art);

            // the heat pulse's layer over the flow: the bright band rolling along it, the haze's ripples drifting up
            _band = new TacSketch((dc, s) => dc.DrawRectangle(Mod.Grad(false, (Mod.A(LavaYelC, 0), 0), (Mod.A(LavaYelC, .55), .55), (Mod.A(WhiteHotC, .9), .86), (Mod.A(LavaHotC, 0), 1)), null, new Rect(0, 0, s.Width, s.Height)), BandW, H)
                { RenderTransform = _bandX };
            _haze = new TacSketch(DrawHaze, double.NaN, H) { RenderTransform = _hazeY };
            _pulseFx.Children.Add(_band); _pulseFx.Children.Add(_haze);
            _root.Children.Add(_pulseFx);
            _flare.RenderTransform = _flareX;
            _root.Children.Add(_flare);
            // a climb: a molten streak along the foot growing out from the left, the rim flaring
            var streak = new TacSketch((dc, s) => dc.DrawRectangle(Mod.Grad(false, (Mod.A(LavaHotC, 0), 0), (Mod.A(LavaYelC, .95), 1)), null, new Rect(1, s.Height - 2.5, s.Width - 2, 2)))
                { RenderTransform = _streakS };
            _climbRim = new TacSketch((dc, s) => dc.DrawGeometry(null, Mod.P(Mod.Br(LavaYelC, .9), 1.5), Mod.Pill(new Rect(.5, .5, s.Width - 1, s.Height - 1), 3.5)));
            _climbFx.Children.Add(streak); _climbFx.Children.Add(_climbRim);
            _root.Children.Add(_climbFx);

            // the rank's cell: the number, or the molten crown over its pool
            var pool = new TacSketch((dc, s) => Pool(dc, new Point(s.Width / 2, s.Height / 2 + 6.5), 0));
            _poolFlare = new TacSketch((dc, s) => Pool(dc, new Point(s.Width / 2, s.Height / 2 + 6.5), 1)) { Opacity = 0 };
            var window = new Grid { ClipToBounds = true, Height = H / 2 + 8.5, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(-3, 0, -3, 0) };
            var cool = new TacSketch((dc, s) => Crown(dc, new Point(s.Width / 2 - 8, H / 2 - 6), 0));
            _crownHot = new TacSketch((dc, s) => Crown(dc, new Point(s.Width / 2 - 8, H / 2 - 6), 1)) { Opacity = 0 };
            _crownMove.Children.Add(cool); _crownMove.Children.Add(_crownHot);
            _crownMove.RenderTransform = _crownY;
            window.Children.Add(_crownMove);
            _flash = new TacSketch((dc, s) =>
            {
                var g = new RadialGradientBrush(Mod.A(WhiteHotC, .6), Mod.A(LavaHotC, 0)); g.Freeze();
                dc.DrawEllipse(g, null, new Point(s.Width / 2, s.Height / 2), 10, 10);
            }) { Opacity = 0 };
            _crownHost.Children.Add(pool); _crownHost.Children.Add(_poolFlare); _crownHost.Children.Add(window); _crownHost.Children.Add(_flash);
            for (int i = 0; i < 2; i++)
            {
                _drops[i] = new System.Windows.Shapes.Ellipse { Width = 2.4, Height = 2.4, Fill = i == 0 ? LavaYel : LavaHot, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Opacity = 0, RenderTransform = _dropT[i], Margin = new Thickness(0, 13, 0, 0) };
                _crownHost.Children.Add(_drops[i]);
            }
            _rank.HorizontalAlignment = HorizontalAlignment.Center;
            _rankHost.Children.Add(_rank); _rankHost.Children.Add(_crownHost);
            Put(_rankHost, 0); Put(_iconHost, 1); Put(_line, 2); Put(_heal, 3); Put(_dps, 4); Put(_share, 5); Put(_dmg, 6); Put(_crit, 7);
            _name.TextTrimming = TextTrimming.CharacterEllipsis;
            _cp.Margin = new Thickness(5, 2, 0, 0);
            _line.Children.Add(_name); _line.Children.Add(_cp);
            _words.Effect = Shade();
            _root.Children.Add(_words);
            _root.SizeChanged += (_, _) => { Fit(); if (_pendingP >= 0) SetPulse(_pendingP); };
        }

        void Put(FrameworkElement el, int col) { Grid.SetColumn(el, col); _words.Children.Add(el); }

        public void Set(MeterMember m, MeterContext c, bool fresh)
        {
            _m = m;
            _seed = SeedOf(m.Id);
            if (_expanded != c.Expanded)
            {
                _expanded = c.Expanded;
                _words.ColumnDefinitions[6].Width = new GridLength(c.Expanded ? _dmgW : 0);
                _words.ColumnDefinitions[7].Width = new GridLength(c.Expanded ? _critW : 0);
            }
            _rank.Text = m.Rank.ToString(CultureInfo.InvariantCulture);
            // the crown on the top row once there is damage (by Combat Power before it, the rank stays a number)
            _crown = m.Rank == 1 && !m.CpOnly && !m.Pvp;
            _crownHost.Visibility = _crown ? Visibility.Visible : Visibility.Collapsed;
            _rank.Visibility = _crown ? Visibility.Collapsed : Visibility.Visible;
            if (!fresh && c.Moves && _lastRank > 0)
            {
                if (_crown && _lastRank > 1) { Rise(); _look.Lead(); }
                else if (m.Rank < _lastRank) Climb();
            }
            _lastRank = m.Rank;
            if (m.Class != _cls)
            {
                _cls = m.Class;
                var icon = ThemeText.Icon(m.Class, 18, m.Name);
                if (icon is Border b) { b.BorderBrush = Frozen(Mod.Mix(m.ClassColor, Colors.White, .2)); b.BorderThickness = new Thickness(1.3); }
                _iconHost.Child = icon;
            }
            _name.Text = m.Name;
            _cp.Text = m.Cp;
            // the marks after the name: ★ DPS, a healer's ✚, deaths, YOU (kept whole: the name and the CP give way first)
            foreach (var mk in _marks) _line.Children.Remove(mk);
            _marks.Clear();
            void Add(FrameworkElement el) { el.Margin = new Thickness(5, 0, 0, 0); _line.Children.Add(el); _marks.Add(el); }
            if (m.TopDps) { var t = Chip("★ " + L.T("DPS"), LavaYel, LavaHotC); t.Tag = "top-dps"; t.ToolTip = L.T("Top DPS"); Add(t); }
            if (m.TopHeals) { var t = Chip("✚", MarkMint, MintC2); t.Tag = "top-heals"; t.ToolTip = L.T("Top heals"); Add(t); }
            if (m.Deaths > 0) { var d = Tt($"✕{m.Deaths}", 10.5, Coral, FontWeights.Bold); d.Tag = "deaths"; Add(d); }
            if (m.You) { var y = Chip(L.T("YOU"), White, LavaHotC, filled: true); y.Tag = "you"; Add(y); }
            _heal.Text = m.Heal.Length > 0 ? "+" + m.Heal : "";
            _heal.Visibility = m.Heal.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            _dps.Text = m.Figure;
            _dps.FontSize = m.CpOnly ? 13.5 : 16;
            // a wider figure (Combat Power before the first hit, "167,548") widens its column: never a digit cut
            _words.ColumnDefinitions[4].Width = new GridLength(Math.Max(_dpsW, TextW(_dps, m.Figure) + 4));
            _share.Text = m.ShareText;
            _dmg.Text = c.Expanded ? m.Damage : "";
            _crit.Text = c.Expanded ? m.Crit : "";
            Fit();
            SlideBar(_art, c, "bar", m.CpOnly ? 0 : BarOf(m, c), v => { _bar = v; _art.InvalidateVisual(); });
            _art.InvalidateVisual();
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
            _cp.Visibility = cp ? Visibility.Visible : Visibility.Collapsed;
            if (share != _shareOn || cols[5].Width.Value != (share ? _shareW : 0))
            {
                _shareOn = share;
                cols[5].Width = new GridLength(share ? _shareW : 0);
            }
            _share.Visibility = share ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>The flow's room in the row (its rect) and its front's x now.</summary>
        (Rect Flow, double Front) FlowNow(double w)
        {
            // the flow pours up to the share's column: the share (and, expanded, the damage and crit) sit on the stone past it
            var cols = _words.ColumnDefinitions;
            double wall = PadR + cols[5].Width.Value + cols[6].Width.Value + cols[7].Width.Value;
            var flow = new Rect(3.5, 2, Math.Max(20, w - wall - 3.5 + 3), H - 4);   // its lip in the air before the share, clear of the DPS figure
            return (flow, flow.X + flow.Width * Math.Clamp(_bar, 0, 1));
        }

        bool _pulsing;
        double _pendingP = -1;

        /// <summary>
        /// The heat pulse rolling along this row's flow, 0..1 (-1: done); the look's heat pulse drives it. Its layers were drawn
        /// once: each frame only moves the band along, lifts the haze and fades the front's flare.
        /// </summary>
        public void SetPulse(double p)
        {
            if (p >= 0 && p < 1 && _art.ActualWidth <= 0) { _pendingP = p; return; }   // a held frame before the first layout: once laid out
            _pendingP = -1;
            if (p < 0 || p >= 1 || _m is null || _bar <= .01)
            {
                if (_pulsing) { _pulsing = false; _pulseFx.Visibility = Visibility.Collapsed; _flare.Visibility = Visibility.Collapsed; }
                return;
            }
            var (flow, front) = FlowNow(_art.ActualWidth);
            if (!_pulsing)
            {
                // the layer clipped to the flow as it stands when the pulse starts
                _pulsing = true;
                _pulseFx.Clip = Mod.Pill(new Rect(flow.X, flow.Y, front - flow.X, flow.Height), 2.5);
                _haze.Width = Math.Max(1, front - flow.X); Canvas.SetLeft(_haze, flow.X + (front - flow.X) * .3);
                _pulseFx.Visibility = Visibility.Visible; _flare.Visibility = Visibility.Visible;
            }
            double k = Seg(p, 0, .85), len = front - flow.X;
            _bandX.X = flow.X - BandW + (len + BandW) * Out(k);
            _band.Opacity = Math.Sin(Math.PI * Math.Min(1, k * 1.15 + .04));
            _hazeY.Y = -5 * p;
            _haze.Opacity = Math.Sin(Math.PI * p);
            _flareX.X = front - 17;
            _flare.Opacity = p > .62 ? Math.Sin(Math.PI * Seg(p, .62, 1)) : 0;
        }

        /// <summary>
        /// The crown rises out of its lava pool: it surfaces white-hot, the pool flaring and throwing two drops, then it cools
        /// to bright orange as it settles. Every part was drawn once; the course moves and fades them.
        /// </summary>
        public void Rise()
        {
            if (!_crown) return;
            foreach (var d in _drops) d.Opacity = 0;
            Motion.Play(_crownHost, 1100, t =>
            {
                double up = Out(Seg(t, .05, .5));
                _crownY.Y = 13 * (1 - up);
                _crownHot.Opacity = t < .45 ? 1 : 1 - Seg(t, .45, 1);
                _poolFlare.Opacity = t < .5 ? Math.Min(1, Math.Sin(Math.PI * Seg(t, 0, .5)) + .3) * (1 - Seg(t, .4, .5)) : 0;
                _flash.Opacity = t is > .3 and < .75 ? 1 - Seg(t, .3, .75) : 0;
                double k = Seg(t, .12, .7);
                for (int i = 0; i < 2; i++)
                {
                    _drops[i].Opacity = t is > .12 and < .7 ? 1 - .6 * k : 0;
                    _dropT[i].X = (i == 0 ? -1 : 1) * (3 + 4 * k);
                    _dropT[i].Y = -10 * Math.Sin(Math.PI * k);
                }
            }, () =>
            {
                _crownY.Y = 0; _crownHot.Opacity = 0; _poolFlare.Opacity = 0; _flash.Opacity = 0;
                foreach (var d in _drops) d.Opacity = 0;
            });
        }

        /// <summary>The row climbed a rank: its rim flares and a molten streak runs along its foot (with the glide, not against it).</summary>
        void Climb()
        {
            _climbFx.Visibility = Visibility.Visible;
            Motion.Play(_climbFx, 650, t =>
            {
                _streakS.ScaleX = Out(Seg(t, 0, .6));
                _climbFx.Opacity = 1 - t;
            }, () => { _climbFx.Visibility = Visibility.Collapsed; _streakS.ScaleX = 0; _climbFx.Opacity = 1; });
        }

        static double Seg(double p, double a, double b) => Math.Clamp((p - a) / (b - a), 0, 1);
        static double Out(double p) => Motion.Out(p);

        /// <summary>The front's flare as the pulse reaches it: a hot glow, drawn once.</summary>
        static void DrawFlare(DrawingContext dc, Size s)
        {
            var g = new RadialGradientBrush(Mod.A(LavaYelC, .85), Mod.A(LavaC, 0)); g.Freeze();
            dc.DrawEllipse(g, null, new Point(s.Width / 2, s.Height / 2), s.Width / 2, s.Height * .5);
            var c = new RadialGradientBrush(Mod.A(WhiteHotC, .9), Mod.A(LavaYelC, 0)); c.Freeze();
            dc.DrawEllipse(c, null, new Point(s.Width / 2, s.Height / 2), 4, s.Height * .32);
        }

        /// <summary>The heat haze's two ripples across the row (drawn once; the pulse lifts and fades them).</summary>
        static void DrawHaze(DrawingContext dc, Size s)
        {
            if (s.Width < 30) return;
            for (int k = 0; k < 2; k++)
            {
                double y = s.Height * (.62 - .3 * k), amp = 1.1, ph = k * 2.1;
                var g = new StreamGeometry();
                using (var c = g.Open())
                {
                    c.BeginFigure(new Point(0, y + amp * Math.Sin(ph)), false, false);
                    for (double x = 3; x <= s.Width; x += 3) c.LineTo(new Point(x, y + amp * Math.Sin(ph + x * .35)), true, true);
                }
                g.Freeze();
                dc.DrawGeometry(null, Mod.P(Mod.Grad(false, (Mod.A(WhiteHotC, 0), 0), (Mod.A(WhiteHotC, .34), .7), (Mod.A(LavaYelC, .1), 1)), 1.1), g);
            }
        }

        void Draw(DrawingContext dc, Size s)
        {
            if (_m is not { } m || s.Width < 30) return;
            var r = new Rect(.5, .5, s.Width - 1, s.Height - 1);
            var channel = Mod.Pill(r, 3.5);
            // the channel cut in the stone: near black, a shadow under its top lip
            dc.DrawGeometry(Frozen(Mod.A(TrenchC, .94)), null, channel);
            dc.PushClip(channel);
            dc.DrawRectangle(Mod.Grad(true, (Mod.A(Colors.Black, .55), 0), (Mod.A(Colors.Black, 0), 1)), null, new Rect(r.X, r.Y, r.Width, 5));
            var (flow, front) = FlowNow(s.Width);
            double fx = front - flow.X;
            double heat = _crown ? 1 : .3 + .45 * Math.Clamp(_bar, 0, 1);
            // the warm underglow behind the DPS figure (on the stone past a short flow)
            if (_dps.ActualWidth > 0 && _dps.IsDescendantOf(_root))
            {
                var p = _dps.TranslatePoint(new Point(_dps.ActualWidth / 2, _dps.ActualHeight / 2), _root);
                var ug = new RadialGradientBrush(Mod.A(LavaC, .32), Mod.A(LavaC, 0)); ug.Freeze();
                dc.DrawEllipse(ug, null, new Point(p.X, p.Y + 4), _dps.ActualWidth * .7 + 4, r.Height * .55);
            }
            Flow(dc, flow, fx, heat, _seed);
            // a healer's share of the group's healing as a mint line along the channel's foot (Settings' heal bars)
            if (ParserRows.HealBarsPref && m.HealShare > 0)
                dc.DrawRectangle(Mod.Br(MintC2, .9), null, new Rect(r.X, r.Bottom - 2, Math.Max(3, r.Width * Math.Clamp(m.HealShare, 0, 1)), 2));
            // the class's stripe down the channel's left edge
            dc.DrawRectangle(Mod.Grad(true, (Mod.Mix(m.ClassColor, Colors.White, .25), 0), (m.ClassColor, .5), (Mod.Mix(m.ClassColor, Colors.Black, .35), 1)), null, new Rect(r.X, r.Y, 2.5, r.Height));
            dc.Pop();
            // the rim: a warm bevel on its lower lip; yours glowing orange
            if (m.You) dc.DrawGeometry(null, Mod.P(Mod.Br(LavaHotC, .95), 1.2), channel);
            else dc.DrawGeometry(null, Mod.P(Mod.Grad(true, (Mod.A(RimC, .9), 0), (Mod.A(BevelC, .75), 1)), 1), channel);
        }
    }
}
