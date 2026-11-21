using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using KinahLedger.Parser;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

sealed partial class HudTide
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

    /// <summary>The fight's foot under the sea line: "Total 622.4M" at the left, the group's word and figure at the right, the AVG mark on a held average.</summary>
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
        EnergyMark.After(tp, 11, Frozen(Mod.A(Color.FromRgb(0xCF, 0xF2, 0xF2), .72)));   // Odyle Energy (Ui/EnergyMark.cs)
        g.Children.Add(tp);
        var right = new StackPanel { Orientation = Orientation.Horizontal };
        if (word.Length > 0) right.Children.Add(Pair(word, group));
        if (held)
        {
            var avg = Mark(L.T("AVG"), size: 8.5); avg.Tag = "avg-badge"; avg.ToolTip = L.T(Encounter.AvgBadge); avg.Margin = new Thickness(5, 0, 0, 0);
            right.Children.Add(avg);
        }
        Grid.SetColumn(right, 1); g.Children.Add(right);
        return AsFoot(Lit(g), e, now);
    }

    /// <summary>The line between the party and you pinned below it: "2 MORE", small, between two short swells of light.</summary>
    static FrameworkElement GapLine(int left)
    {
        var t = Tt(left > 0 ? L.F("{0} more", left).ToUpperInvariant() : "·", 9, Quiet, FontWeights.SemiBold);
        t.HorizontalAlignment = HorizontalAlignment.Center;
        return Lit(t);
    }

    /// <summary>
    /// One member as a dark channel their water fills: the channel the row's full width, the water from its left for the
    /// member's part of the top (Encounter.BarOf: the top bar full), in the sea's tint of their class colour, deep at the left
    /// and lit toward its leading edge, a crest curling forward with white foam on its lip; the leader's water carries the
    /// caustic light, and a pearl in an open shell stands in rank 1's place. On it, under one tight dark shade: the rank, the
    /// class's art in an aqua ring, the name, Combat Power small, the marks (★ DPS, a healer's ✚, deaths, YOU), a healer's
    /// healing a second in mint, the DPS big (a faint aqua halo behind it) and the share small at the right. You: a line of
    /// foam at the row's left edge. Short of room the CP goes first, then the share; a name keeps at least ten characters; a
    /// figure is never cut (Mod.Fit).
    /// </summary>
    internal sealed class TideRow : IThemeRow
    {
        public const double H = 28, Pitch = 31;
        const double Pad = 8, RankW = 30;
        readonly HudTide _look;
        readonly Grid _root = new() { Height = H };
        readonly TacSketch _art, _crown;
        readonly Grid _words = new() { Margin = new Thickness(Pad, 0, Pad, 0) };
        readonly TextBlock _rank = Tt("", 11.5, Aqua, FontWeights.Bold), _name = Tt("", 13, White, FontWeights.Bold), _cp = Tt("", 9.5, Frozen(Mod.A(Color.FromRgb(0xCF, 0xF2, 0xF2), .72)), FontWeights.SemiBold),
            _heal = Tt("", 11, Mint2, FontWeights.Bold), _dps = Tt("", 16.5, White, FontWeights.Bold), _share = Tt("", 10.5, White, FontWeights.Bold),
            _dmg = Tt("", 11, Soft, FontWeights.SemiBold), _crit = Tt("", 10.5, Soft, FontWeights.SemiBold);
        readonly StackPanel _line = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        readonly Border _iconHost = new() { Width = 18, Height = 18, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
        readonly List<FrameworkElement> _marks = new();
        readonly double _dpsW, _shareW, _dmgW, _critW;
        string _cls = "\0";
        MeterMember? _m;
        double _bar, _climb = -1;
        int _was;
        bool _expanded, _shareOn = true, _ledDrawn;
        string _drawSig = "";
        // the moments' parts: the band of light in the water (clipped to it), the crest's glow, over the words the pearl's shine and ripples
        readonly Canvas _fx = new() { IsHitTestVisible = false }, _bandHost = new() { Visibility = Visibility.Collapsed }, _top = new() { IsHitTestVisible = false };
        readonly System.Windows.Shapes.Rectangle _band = new() { Width = 60, Height = H, Fill = BandB, Opacity = 0 };
        readonly System.Windows.Shapes.Ellipse _glow = new() { Width = 22, Height = 19, Fill = CrestGlowB, Opacity = 0, Visibility = Visibility.Collapsed };
        readonly TranslateTransform _bandT = new(), _glowT = new(), _crownT = new();
        readonly ScaleTransform _crownS = new();
        RippleSet? _ripples;
        System.Windows.Shapes.Ellipse? _shine;

        public FrameworkElement Root => _root;

        /// <summary>
        /// Where the DPS bar is drawn, for main's PB ghost (IThemeBarSpan, added at integration): the water's channel, the
        /// row's full width (a full bar's water runs to its right end).
        /// </summary>
        public (FrameworkElement On, Rect Track)? BarTrack => _art.ActualWidth <= 0 ? null : (_art, new Rect(0, 0, _art.ActualWidth, _art.ActualHeight));

        public TideRow(HudTide look)
        {
            _look = look;
            _dps.Tag = "row-dps"; _share.Tag = "row-share"; _heal.Tag = "heal-figures"; _cp.Tag = "cp";
            _dps.HorizontalAlignment = _share.HorizontalAlignment = _dmg.HorizontalAlignment = _crit.HorizontalAlignment = HorizontalAlignment.Right;
            _share.Margin = new Thickness(0, 2, 0, 0); _dmg.Margin = new Thickness(0, 1, 0, 0); _crit.Margin = new Thickness(0, 1, 0, 0);
            _heal.Margin = new Thickness(6, 1, 0, 0);
            // the columns: rank, the class art, the name's line (the rest), heal, the DPS, the share; expanded, the damage and the crit rate
            _dpsW = FigCol(_dps, "888.8K") + 4; _shareW = FigCol(_share, "88.8%", "100%", L.T("CP")) + 6;
            _dmgW = FigCol(_dmg, "888.88M") + 8; _critW = FigCol(_crit, "88%") + 8;
            foreach (var w in new[] { RankW, 23, double.NaN, 0, _dpsW, _shareW, 0, 0 })
                _words.ColumnDefinitions.Add(new ColumnDefinition { Width = double.IsNaN(w) ? new GridLength(1, GridUnitType.Star) : w == 0 ? GridLength.Auto : new GridLength(w) });
            _words.ColumnDefinitions[6].Width = new GridLength(0); _words.ColumnDefinitions[7].Width = new GridLength(0);
            _art = new TacSketch(Draw);
            _crown = new TacSketch(DrawCrown);
            _band.RenderTransform = _bandT; _glow.RenderTransform = _glowT;
            _bandHost.Children.Add(_band);
            _fx.Children.Add(_bandHost); _fx.Children.Add(_glow);
            _crown.RenderTransform = new TransformGroup { Children = { _crownS, _crownT } };
            _root.Children.Add(_art);
            _root.Children.Add(_fx);
            _root.Children.Add(_words);
            _root.Children.Add(_crown);
            _root.Children.Add(_top);
            _words.Effect = Shade();
            _rank.HorizontalAlignment = HorizontalAlignment.Center;
            Put(_rank, 0); Put(_iconHost, 1); Put(_line, 2); Put(_heal, 3); Put(_dps, 4); Put(_share, 5); Put(_dmg, 6); Put(_crit, 7);
            _name.TextTrimming = TextTrimming.CharacterEllipsis;
            _cp.Margin = new Thickness(5, 2, 0, 0);
            _line.Children.Add(_name); _line.Children.Add(_cp);
            _root.SizeChanged += (_, _) => Fit();
        }

        void Put(FrameworkElement el, int col) { Grid.SetColumn(el, col); _words.Children.Add(el); }

        bool Leads => _m is { Rank: 1, CpOnly: false, Pvp: false };

        public void Set(MeterMember m, MeterContext c, bool fresh)
        {
            // a rank won with effects playing: a glint runs the water's surface and the crest flares as the row glides up
            if (!fresh && c.Moves && _was > 0 && m.Rank < _was) Climb();
            _was = m.Rank;
            _m = m;
            if (_expanded != c.Expanded)
            {
                _expanded = c.Expanded;
                _words.ColumnDefinitions[6].Width = new GridLength(c.Expanded ? _dmgW : 0);
                _words.ColumnDefinitions[7].Width = new GridLength(c.Expanded ? _critW : 0);
            }
            _rank.Text = m.Rank.ToString(CultureInfo.InvariantCulture);
            // the leader's rank is the pearl (drawn over the row); the number stays for reading aloud
            _rank.Opacity = Leads ? 0 : 1;
            if (m.Class != _cls)
            {
                _cls = m.Class;
                var icon = ThemeText.Icon(m.Class, 18, m.Name);
                if (icon is Border b) { b.BorderBrush = Frozen(Mod.A(AquaC, .8)); b.BorderThickness = new Thickness(1); }
                _iconHost.Child = icon;
            }
            _name.Text = m.Name;
            _cp.Text = m.Cp;
            // the marks after the name: ★ DPS, a healer's ✚, deaths, YOU (kept whole: the name and the CP give way first)
            foreach (var mk in _marks) _line.Children.Remove(mk);
            _marks.Clear();
            void Add(FrameworkElement el) { el.Margin = new Thickness(5, 0, 0, 0); _line.Children.Add(el); _marks.Add(el); }
            if (m.TopDps) { var t = Mark("★ " + L.T("DPS")); t.Tag = "top-dps"; t.ToolTip = L.T("Top DPS"); Add(t); }
            if (m.TopHeals) { var t = Mark("✚", MarkMint, MarkMintC); t.Tag = "top-heals"; t.ToolTip = L.T("Top heals"); Add(t); }
            if (m.Deaths > 0) { var d = Tt($"✕{m.Deaths}", 10.5, Coral, FontWeights.Bold); d.Tag = "deaths"; Add(d); }
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
            // filled against the top member's (the top bar full, the rest in proportion, 2026-11-20: Encounter.BarOf)
            SlideBar(_art, c, "bar", m.CpOnly ? 0 : c.E.BarOf(m.F), v => { if (v == _bar) return; _bar = v; _art.InvalidateVisual(); });
            // drawn again only when what it shows changed (a tick that changes nothing redraws nothing)
            var sig = $"{m.ClassColor}|{m.You}|{Leads}|{m.Rank}|{m.Figure}|{_shareOn}|{c.Expanded}";
            if (sig != _drawSig) { _drawSig = sig; _art.InvalidateVisual(); }
            if (Leads != _ledDrawn) { _ledDrawn = Leads; _crown.InvalidateVisual(); }
            if (Leads) _look.Lead(this, m, c);
        }

        /// <summary>The crowding rule (Mod.Fit): the CP goes first, then the share; the name keeps at least ten characters.</summary>
        void Fit()
        {
            double total = _root.ActualWidth;
            if (total <= 0) return;
            var cols = _words.ColumnDefinitions;
            double heal = _heal.Visibility == Visibility.Visible ? Mod.W(_heal) + _heal.Margin.Left : 0;
            double room = total - 2 * Pad - cols[0].Width.Value - cols[1].Width.Value - heal - cols[4].Width.Value - cols[6].Width.Value - cols[7].Width.Value - 2;
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

        // ---- the row's moments: only transforms and opacity change while they play

        static readonly Brush BandB = Mod.Grad(false, (Mod.A(FoamC, 0), 0), (Mod.A(FoamC, .42), .62), (Mod.A(FoamC, 0), 1)), CrestGlowB = GlowBrush(FoamC, .8),
            HaloB = GlowBrush(AquaC, .22), ShineB = GlowBrush(FoamC, .6), ChannelB = Mod.Grad(true, (Mod.A(DarkC, .55), 0), (Mod.A(AbyssC, .75), 1)),
            YouB = Mod.Grad(true, (Mod.A(FoamC, .95), 0), (Mod.A(AquaC, .9), 1)), EbbB = Mod.Grad(false, (Mod.A(DarkC, 0), 0), (Mod.A(DarkC, .5), .35), (Mod.A(DarkC, .62), 1));
        static readonly Pen ChannelP = Mod.P(Mod.Br(AquaC, .08), 1), ChannelYouP = Mod.P(Mod.Br(AquaC, .2), 1);

        /// <summary>The light on the water (a swell's band, a climb's glint), inside the water's shape (its clip set when a moment starts).</summary>
        void LightOn()
        {
            double w = _root.ActualWidth, fx = w * Math.Clamp(_bar, 0, 1);
            if (_bandHost.Visibility != Visibility.Visible)
            {
                _bandHost.Clip = fx > 1.5 ? Water(0, fx, 0, H, (_m?.Rank ?? 1) * 1.3) : Geometry.Empty;
                _bandHost.Visibility = Visibility.Visible; _glow.Visibility = Visibility.Visible;
            }
            _glowT.X = fx - 12; _glowT.Y = H * .3 - 9.5;
        }

        void LightOff()
        {
            _bandHost.Visibility = Visibility.Collapsed; _glow.Visibility = Visibility.Collapsed;
            _band.Opacity = 0; _glow.Opacity = 0;
        }

        /// <summary>The swell at its course's point (the look's, 0 to 1; outside it: none): a band of light rolls along this row a beat after the row above, the crest lighting as it passes.</summary>
        internal void SwellStep(double sw)
        {
            if (_m is not { } m || _climb is > 0 and < 1) return;
            double d = Math.Min(.3, .07 * (m.Rank - 1)), p = Seg(sw, d, d + .62);
            if (sw is <= 0 or >= 1 || p is <= 0 or >= 1 || _bar <= .005) { LightOff(); return; }
            LightOn();
            double w = _root.ActualWidth, fx = w * Math.Clamp(_bar, 0, 1), e = p * p * (3 - 2 * p), x = -30 + (w + 60) * e, k = Math.Sin(Math.PI * p);
            _bandT.X = x - 34;
            _band.Opacity = .85 * k;
            _glow.Opacity = Math.Min(1, Math.Exp(-Math.Pow((x - fx) / 26, 2)) * k * 1.3);
        }

        /// <summary>The pearl surfaces: it rises from under the water as its shell opens, a shine round it, rings of ripples spreading from it.</summary>
        internal void Surface()
        {
            double cx = Pad + RankW / 2 - 1, cy = H / 2 - 3.2;
            if (_ripples == null)
            {
                _ripples = new RippleSet(30, .4, FoamC, 3, .9, 1.5);
                _top.Children.Add(_ripples.Layer);
                _shine = new System.Windows.Shapes.Ellipse { Width = 30, Height = 30, Fill = ShineB, Opacity = 0, IsHitTestVisible = false };
                Canvas.SetLeft(_shine, cx - 15); Canvas.SetTop(_shine, cy - 15 + 1.6 * PearlScale);
                _top.Children.Insert(0, _shine);
            }
            var ripples = _ripples;
            ripples.At(cx, cy + 6);
            _crownS.CenterX = cx; _crownS.CenterY = cy + 8 * PearlScale;
            var shine = _shine!;
            Motion.Play(_crown, 1200, t =>
            {
                double up = Out(Seg(t, 0, .45)), open = .35 + .65 * Out(Seg(t, .05, .4));
                _crownT.Y = 6 * (1 - up);
                _crownS.ScaleX = .6 + .4 * open; _crownS.ScaleY = open;
                _crown.Opacity = Seg(t, 0, .2);
                shine.Opacity = Math.Sin(Math.PI * Seg(t, .2, .9));
                ripples.Step(Seg(t, .2, 1));
            }, () => { _crownT.Y = 0; _crownS.ScaleX = _crownS.ScaleY = 1; _crown.Opacity = 1; shine.Opacity = 0; ripples.Step(-1); });
        }

        /// <summary>A rank won: a glint runs the water's surface to its crest, which flares, as the row glides up.</summary>
        void Climb()
        {
            Motion.Play(_words, 700, t =>
            {
                _climb = t;
                if (_bar <= .02) return;
                LightOn();
                double fx = _root.ActualWidth * Math.Clamp(_bar, 0, 1);
                _bandT.X = fx * Out(t) - 50;
                _band.Opacity = 1 - t;
                _glow.Opacity = 1 - Seg(t, .3, 1);
            }, () => { _climb = -1; LightOff(); });
        }

        void Draw(DrawingContext dc, Size s)
        {
            if (_m is not { } m || s.Width < 20) return;
            var r = new Rect(0, 0, s.Width, s.Height);
            // the channel: deep water darker than the glass round it, a faint aqua hairline
            dc.DrawGeometry(ChannelB, m.You ? ChannelYouP : ChannelP, Mod.Pill(new Rect(.5, .5, s.Width - 1, s.Height - 1), 7));
            dc.PushClip(Mod.Pill(r, 7));
            WaterBar(dc, r, s.Width * Math.Clamp(_bar, 0, 1), m.ClassColor, Leads ? .55 : 0, phase: m.Rank * 1.3);
            // the faint aqua halo behind the DPS figure
            if (_dps.ActualWidth > 0 && _dps.IsDescendantOf(_root))
            {
                var at = _dps.TranslatePoint(new Point(_dps.ActualWidth / 2, _dps.ActualHeight / 2), _root);
                dc.DrawEllipse(HaloB, null, at, _dps.ActualWidth / 2 + 10, 12);
            }
            // the share's ground: the water darkens under its figure at the row's right end
            if (_share.Visibility == Visibility.Visible && _share.ActualWidth > 0)
                dc.DrawRectangle(EbbB, null, new Rect(s.Width - _shareW - 14, 0, _shareW + 14, s.Height));
            // you: a line of foam at the row's left edge
            if (m.You) dc.DrawRectangle(YouB, null, new Rect(0, 0, 3, s.Height));
            dc.Pop();
        }

        /// <summary>The pearl in its shell in rank 1's place (drawn once: PearlArt).</summary>
        void DrawCrown(DrawingContext dc, Size s)
        {
            if (!Leads) return;
            dc.PushTransform(new TranslateTransform(Pad + RankW / 2 - 1, s.Height / 2 - 3.2));
            dc.DrawDrawing(PearlArt);
            dc.Pop();
        }
    }
}
