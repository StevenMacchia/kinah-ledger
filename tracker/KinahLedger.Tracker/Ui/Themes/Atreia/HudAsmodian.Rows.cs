using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using KinahLedger.Parser;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

sealed partial class HudAsmodian
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

    /// <summary>A forged bar's shape: <paramref name="fx"/> long from the row's left, its end cut as a blade (slanted back at its foot).</summary>
    static Geometry Blade(Rect r, double fx)
    {
        double slant = Math.Min(6, fx);
        var g = new StreamGeometry();
        using (var x = g.Open())
        {
            x.BeginFigure(new Point(r.Left, r.Top), true, true);
            x.LineTo(new Point(r.Left + fx, r.Top), true, false);
            x.LineTo(new Point(r.Left + fx - slant, r.Bottom), true, false);
            x.LineTo(new Point(r.Left, r.Bottom), true, false);
        }
        g.Freeze();
        return g;
    }

    /// <summary>
    /// A forged bar in <paramref name="tone"/> (a member's, an ability's): the class's colour tempered into the iron, deep at
    /// its root and smouldering towards its end, lit along its top and shadowed at its foot, its blade end burning from
    /// crimson to ember with a pale hot line down the cut.
    /// </summary>
    static void Forged(DrawingContext dc, Rect r, double fx, Color tone, double burn = 1, double quiet = double.PositiveInfinity, double temper = 1)
    {
        if (fx < 1) return;
        // an end under the figures (from quiet on) smoulders low, with no hot line through the digits
        bool under = r.Left + fx > quiet;
        if (under) burn *= .4;
        var blade = Blade(r, fx);
        dc.DrawGeometry(Mod.Grad(false, (Mod.A(Mod.Mix(tone, IronC, .62 * temper), .97), 0), (Mod.A(Mod.Mix(tone, IronC, .32 * temper), .97), 1)), null, blade);
        dc.DrawGeometry(Mod.Grad(true, (Mod.A(Colors.White, .13), 0), (Mod.A(Colors.White, 0), .45), (Mod.A(Colors.Black, .34), 1)), null, blade);
        dc.PushClip(blade);
        double ew = Math.Min(22, fx);
        dc.DrawRectangle(Mod.Grad(false, (Mod.A(CrimC, 0), 0), (Mod.A(CrimC, .42 * burn), .5), (Mod.A(HotC, .8 * burn), 1)), null, new Rect(r.Left + fx - ew, r.Top, ew, r.Height));
        dc.DrawRectangle(Mod.Br(Mod.Mix(tone, Colors.White, .45), .5), null, new Rect(r.Left, r.Top, fx, 1));
        dc.Pop();
        double slant = Math.Min(6, fx);
        if (!under) dc.DrawLine(Mod.P(Mod.Grad(true, (PaleC, 0), (HotC, 1)), 1.5), new Point(r.Left + fx - .5, r.Top), new Point(r.Left + fx - slant - .5, r.Bottom));
    }

    /// <summary>
    /// The readout's well: the iron darkened under a row's figures from <paramref name="x"/> to its right end (a soft step in),
    /// so a bar's burning end never draws its hot line through a digit; the bar still shows through.
    /// </summary>
    static void Well(DrawingContext dc, Rect r, double x, double a = .62)
    {
        if (x >= r.Right) return;
        dc.DrawRectangle(Mod.Grad(false, (Mod.A(IronC, 0), 0), (Mod.A(IronC, a), 1)), null, new Rect(x - 18, r.Top, 18, r.Height));
        dc.DrawRectangle(Mod.Br(IronC, a), null, new Rect(x, r.Top, r.Right - x, r.Height));
    }

    /// <summary>The fight's foot after its rule: "Total 622.4M" at the left, the group's word and figure at the right, the AVG mark on a held average.</summary>
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
            var avg = Chip(L.T("AVG"), Soft, CrimC, size: 8.5); avg.Tag = "avg-badge"; avg.ToolTip = L.T(Encounter.AvgBadge); avg.Margin = new Thickness(6, 0, 0, 0);
            right.Children.Add(avg);
        }
        Grid.SetColumn(right, 1); g.Children.Add(right);
        return AsFoot(g, e, now);
    }

    /// <summary>The line between the party and you pinned below it: "2 MORE" between two crimson rules.</summary>
    static FrameworkElement GapLine(int left)
    {
        var g = new Grid { VerticalAlignment = VerticalAlignment.Center };
        g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); g.ColumnDefinitions.Add(new ColumnDefinition());
        var t = Tt(left > 0 ? L.F("{0} more", left).ToUpperInvariant() : "◆", left > 0 ? 9 : 6, Quiet, FontWeights.SemiBold);
        t.Margin = new Thickness(8, 0, 8, 0);
        Grid.SetColumn(t, 1); g.Children.Add(t);
        var l = new Border { Height = 1, Background = Mod.Grad(false, (Mod.A(CrimC, 0), 0), (Mod.A(CrimC, .6), 1)), VerticalAlignment = VerticalAlignment.Center };
        var r = new Border { Height = 1, Background = Mod.Grad(false, (Mod.A(CrimC, .6), 0), (Mod.A(CrimC, 0), 1)), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(r, 2); g.Children.Add(l); g.Children.Add(r);
        return g;
    }

    /// <summary>
    /// One member as an iron slab with their bar forged in it (<see cref="Forged"/>): the bar's length their part of the top,
    /// its blade end burning. On it: the rank in the heavy face (the top row wears the blood-red horned crown instead), the
    /// class's art in a ring of its colour, the name in white, Combat Power small, the marks (★ DPS, a healer's ✚, deaths,
    /// YOU as a crimson ingot), a healer's healing a second in mint, the DPS big and the share small at the right. You: a hot
    /// crimson spine at the slab's left and a crimson rim. Short of room the CP goes first, then the share; a name keeps at
    /// least ten characters; a figure is never cut (Mod.Fit).
    /// Its moments: the crown slams down when this row takes the lead (and a crimson flash bursts round it); a row that
    /// climbs a rank flashes its rim and leaves an ember streak along its foot; the heat glint runs along its bar.
    /// </summary>
    sealed class IronRow : IThemeRow
    {
        public const double H = 26, Pitch = 29;
        const double Pad = 5, RankW = 20, IconW = 23;
        readonly HudAsmodian _look;
        readonly Grid _root = new() { Height = H };
        readonly TacSketch _art, _crownArt;
        readonly Grid _words = new() { Margin = new Thickness(Pad, 0, Pad + 3, 0) };
        readonly TextBlock _rank = Big("", 13.5, Soft, FontWeights.Bold), _name = Tt("", 12.5, White, FontWeights.Bold), _cp = Tt("", 9.5, Quiet, FontWeights.SemiBold),
            _heal = Tt("", 10.5, Mint2, FontWeights.Bold), _dps = Big("", 17, White, FontWeights.SemiBold), _share = Tt("", 10, Soft, FontWeights.SemiBold),
            _dmg = Tt("", 11, Soft, FontWeights.SemiBold), _crit = Tt("", 10.5, Soft, FontWeights.SemiBold);
        readonly StackPanel _line = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        readonly Grid _rankHost = new() { Width = RankW, Height = H };
        readonly Grid _crown = new() { Width = 18, Height = 15, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 1), Visibility = Visibility.Collapsed };
        readonly ScaleTransform _crownScale = new(1, 1);
        readonly Border _iconHost = new() { Width = 18, Height = 18, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
        readonly List<FrameworkElement> _marks = new();
        readonly double _dpsW, _shareW, _dmgW, _critW;
        string _cls = "\0", _markSig = "\0";
        MeterMember? _m;
        double _bar;
        int _lastRank;
        bool _expanded, _shareOn = true;
        /// <summary>The moments' layer over the slab, under the words: its pieces are made when a moment starts and go when it ends.</summary>
        readonly Canvas _fxLayer = new() { IsHitTestVisible = false };
        System.Windows.Shapes.Rectangle? _glint;
        TacSketch? _flash;

        public FrameworkElement Root => _root;

        public IronRow(HudAsmodian look)
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
            _root.Children.Add(_art); _root.Children.Add(_fxLayer);
            // the rank's cell: the number, or the crown
            _crownArt = new TacSketch((dc, s) => Crown(dc, new Point(0, 0), 1.12), 18, 15);
            _crown.Children.Add(_crownArt);
            _crown.RenderTransformOrigin = new Point(.5, .65); _crown.RenderTransform = _crownScale;
            _rank.HorizontalAlignment = HorizontalAlignment.Center;
            _rankHost.Children.Add(_rank); _rankHost.Children.Add(_crown);
            Put(_rankHost, 0); Put(_iconHost, 1); Put(_line, 2); Put(_heal, 3); Put(_dps, 4); Put(_share, 5); Put(_dmg, 6); Put(_crit, 7);
            _name.TextTrimming = TextTrimming.CharacterEllipsis;
            _cp.Margin = new Thickness(5, 2, 0, 0);
            _line.Children.Add(_name); _line.Children.Add(_cp);
            _root.Children.Add(_words);
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
            // the crown on the top row once there is damage (by Combat Power before it, the rank stays a number)
            bool crown = m.Rank == 1 && !m.CpOnly && !m.Pvp;
            var cv = crown ? Visibility.Visible : Visibility.Collapsed;
            if (_crown.Visibility != cv) { _crown.Visibility = cv; _rank.Visibility = crown ? Visibility.Collapsed : Visibility.Visible; }
            if (!fresh && c.Moves && _lastRank > 0)
            {
                if (crown && _lastRank > 1) { Slam(); _look.Lead(); }
                else if (m.Rank < _lastRank) Climb();
            }
            _lastRank = m.Rank;
            if (m.Class != _cls)
            {
                _cls = m.Class;
                var icon = ThemeText.Icon(m.Class, 18, m.Name);
                if (icon is Border b) { b.BorderBrush = Frozen(Mod.Mix(m.ClassColor, Colors.White, .25)); b.BorderThickness = new Thickness(1.2); }
                _iconHost.Child = icon;
            }
            _name.Text = m.Name;
            _cp.Text = m.Cp;
            // the marks after the name: ★ DPS, a healer's ✚, deaths, YOU (kept whole: the name and the CP give way first); made
            // again only when they change, not on every draw
            var sig = $"{m.TopDps}|{m.TopHeals}|{m.Deaths}|{m.You}";
            if (sig != _markSig)
            {
                _markSig = sig;
                foreach (var mk in _marks) _line.Children.Remove(mk);
                _marks.Clear();
                void Add(FrameworkElement el) { el.Margin = new Thickness(5, 0, 0, 0); _line.Children.Add(el); _marks.Add(el); }
                if (m.TopDps) { var t = Chip("★ " + L.T("DPS"), Gold2, GoldC2, size: 8.5); t.Tag = "top-dps"; t.ToolTip = L.T("Top DPS"); Add(t); }
                if (m.TopHeals) { var t = Chip("✚", MarkMint, MintC2, size: 8.5); t.Tag = "top-heals"; t.ToolTip = L.T("Top heals"); Add(t); }
                if (m.Deaths > 0) { var d = Tt($"✕{m.Deaths}", 10.5, Coral, FontWeights.Bold); d.Tag = "deaths"; Add(d); }
                if (m.You) { var y = Chip(L.T("YOU"), White, CrimC, filled: true, size: 8.5); y.Tag = "you"; Add(y); }
            }
            _heal.Text = m.Heal.Length > 0 ? "+" + m.Heal : "";
            _heal.Visibility = m.Heal.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            _dps.Text = m.Figure;
            _dps.FontSize = m.CpOnly ? 14 : 17;
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
            double room = total - _words.Margin.Left - _words.Margin.Right - cols[0].Width.Value - cols[1].Width.Value - heal - cols[4].Width.Value - cols[6].Width.Value - cols[7].Width.Value - 2;
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

        /// <summary>The row's width now (the meter's rows' width before its first layout, as in a render's held frame).</summary>
        double W => _root.ActualWidth > 0 ? _root.ActualWidth : _look._rowsW;

        /// <summary>
        /// The heat glint along the bar, 0..1 (-1: none): a band of ember light (one frozen brush) running along the bar to its
        /// burning end, moved by its transform inside the bar's shape (clipped once when it starts).
        /// </summary>
        public void SetGlint(double g)
        {
            if (g is <= 0 or >= 1)
            {
                if (_glint != null) { _fxLayer.Children.Remove(_glint); _glint = null; _fxLayer.Clip = null; }
                return;
            }
            var r = new Rect(.5, .5, Math.Max(1, W - 1), H - 1);
            double fx = r.Width * Math.Clamp(_bar, 0, 1);
            if (fx < 8) return;
            if (_glint == null)
            {
                _glint = new System.Windows.Shapes.Rectangle { Width = 26, Height = H, Fill = GlintFill, IsHitTestVisible = false, RenderTransform = new TranslateTransform() };
                _fxLayer.Clip = Blade(r, fx);
                _fxLayer.Children.Add(_glint);
            }
            ((TranslateTransform)_glint.RenderTransform).X = r.Left - 26 + (fx + 26) * Out(g);
            _glint.Opacity = Math.Sin(Math.PI * g);
        }

        /// <summary>The slab's flash (a slam, a climb): its outline in pale fire and a hot wash, drawn once, its opacity moved.</summary>
        TacSketch Flash()
        {
            if (_flash != null) return _flash;
            _flash = new TacSketch((dc, s) =>
            {
                var slab = Plate(new Rect(.5, .5, s.Width - 1, s.Height - 1), 6, 0);
                dc.DrawGeometry(Mod.Br(HotC, .3), Mod.P(Mod.Br(PaleC), 1.6), slab);
            }) { Width = W, Height = H, Opacity = 0 };
            // over the slab, under the words (the moments' layer is clipped to the bar for the glint: the flash sits beside it)
            _root.Children.Insert(_root.Children.IndexOf(_fxLayer) + 1, _flash);
            return _flash;
        }

        void FlashOff() { if (_flash != null) { _root.Children.Remove(_flash); _flash = null; } }

        /// <summary>
        /// The crown slams down: it falls from twice its size, bites in a touch small, settles; a pale flash and a crimson ring
        /// burst round it and the slab flashes. Each piece made once and moved by scale and opacity.
        /// </summary>
        public void Slam()
        {
            var flash = Flash();
            var ring = new System.Windows.Shapes.Ellipse { Width = 16, Height = 13, Stroke = BurstStroke, StrokeThickness = 2.2, IsHitTestVisible = false, Opacity = 0, RenderTransformOrigin = new Point(.5, .5) };
            var glow = new System.Windows.Shapes.Ellipse { Width = 34, Height = 28, Fill = BurstGlow, IsHitTestVisible = false, Opacity = 0, RenderTransformOrigin = new Point(.5, .5) };
            var rs = new ScaleTransform(1, 1); ring.RenderTransform = rs;
            _rankHost.Children.Insert(0, glow); _rankHost.Children.Insert(1, ring);
            Motion.Play(_rankHost, 760, t =>
            {
                double k;
                if (t < .3) { double a = Seg(t, 0, .3); k = 2.3 - 1.46 * a * a; }
                else if (t < .55) k = .84 + .22 * Out(Seg(t, .3, .55));
                else k = 1.06 - .06 * Out(Seg(t, .55, 1));
                _crownScale.ScaleX = _crownScale.ScaleY = k;
                _crown.Opacity = Math.Min(1, Seg(t, 0, .14) * 1.2);
                double b = Seg(t, .22, .95);
                glow.Opacity = t < .22 ? 0 : b < .15 ? b / .15 : 1 - b;
                ring.Opacity = t < .22 ? 0 : 1 - b;
                rs.ScaleX = rs.ScaleY = 1 + 2.2 * Out(b);
                flash.Opacity = t is > .25 and < .8 ? 1 - Seg(t, .25, .8) : 0;
            }, () => { _crownScale.ScaleX = _crownScale.ScaleY = 1; _crown.Opacity = 1; _rankHost.Children.Remove(glow); _rankHost.Children.Remove(ring); FlashOff(); });
        }

        /// <summary>The row climbed a rank: its slab flashes and a glint runs along its bar (with the glide, not against it).</summary>
        void Climb()
        {
            var flash = Flash();
            Motion.Play(flash, 620, t => { flash.Opacity = .85 * (1 - t); SetGlint(t); }, () => { SetGlint(-1); FlashOff(); });
        }

        static double Seg(double p, double a, double b) => Math.Clamp((p - a) / (b - a), 0, 1);
        static double Out(double p) => Motion.Out(p);

        void Draw(DrawingContext dc, Size s)
        {
            if (_m is not { } m || s.Width < 30) return;
            var r = new Rect(.5, .5, s.Width - 1, s.Height - 1);
            var slab = Plate(r, 6, 0);
            dc.DrawGeometry(SlabFill, null, slab);
            double fx = r.Width * Math.Clamp(_bar, 0, 1);
            dc.PushClip(slab);
            // the figures' well (the DPS, the share; expanded, the damage and the crit), when the bar reaches under them; from the
            // heal figure on, the bar's end burns low with no hot line through a digit
            var cols = _words.ColumnDefinitions;
            double figL = r.Right - _words.Margin.Right - cols[4].ActualWidth - cols[5].ActualWidth - cols[6].ActualWidth - cols[7].ActualWidth + 2;
            if (cols[4].ActualWidth <= 0) figL = r.Right;
            double quiet = figL - (_heal.Visibility == Visibility.Visible ? cols[3].ActualWidth : 0) - 8;
            Forged(dc, r, fx, m.ClassColor, 1, quiet);
            if (fx > figL - 18) Well(dc, r, figL);
            // a healer's share of the group's healing as a mint line along the slab's foot (Settings' heal bars)
            if (ParserRows.HealBarsPref && m.HealShare > 0)
                dc.DrawRectangle(HealLine, null, new Rect(r.X, r.Bottom - 2, Math.Max(3, r.Width * Math.Clamp(m.HealShare, 0, 1)), 2));
            // you: a hot crimson spine at the left
            if (m.You) dc.DrawRectangle(Spine, null, new Rect(r.Left, r.Top, 3, r.Height));
            dc.Pop();
            dc.DrawGeometry(null, m.You ? YouRim : Rim, slab);
        }
    }

    // the rows' frozen brushes and pens, made once for every row
    static readonly Brush SlabFill = Mod.Grad(true, (Color.FromArgb(0xE6, 0x21, 0x11, 0x14), 0), (Color.FromArgb(0xEE, 0x10, 0x08, 0x0A), 1));
    static readonly Brush HealLine = Frozen(Color.FromArgb(0xE6, 0x5D, 0xF2, 0xA6));
    static readonly Brush Spine = Mod.Grad(true, (Color.FromRgb(0xFF, 0xD0, 0x8A), 0), (Color.FromRgb(0xFF, 0x4A, 0x3D), .35), (Color.FromRgb(0xC2, 0x1F, 0x2E), 1));
    static readonly Pen Rim = Mod.P(Frozen(Color.FromRgb(0x4A, 0x20, 0x26)), 1), YouRim = Mod.P(Frozen(Color.FromArgb(0xE6, 0xFF, 0x4A, 0x3D)), 1.2);
    static readonly Brush GlintFill = Mod.Grad(false, (Color.FromArgb(0, 0xC2, 0x1F, 0x2E), 0), (Color.FromArgb(0x80, 0xFF, 0x8A, 0x3D), .55), (Color.FromArgb(0xF2, 0xFF, 0xD0, 0x8A), .86), (Color.FromArgb(0, 0xFF, 0x8A, 0x3D), 1));
    static readonly Brush BurstStroke = Frozen(Color.FromRgb(0xFF, 0x4A, 0x3D));
    static readonly Brush BurstGlow = MakeRadial(Color.FromArgb(0xE6, 0xFF, 0xD0, 0x8A), Color.FromArgb(0, 0xC2, 0x1F, 0x2E), Color.FromArgb(0x90, 0xFF, 0x4A, 0x3D));
}
