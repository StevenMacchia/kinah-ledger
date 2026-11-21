using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using KinahLedger.Parser;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

sealed partial class HudAura
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

    /// <summary>A thin line of energy across (the foot's, the card's): azure, white at its heart, gone at both ends.</summary>
    static FrameworkElement EnergyLine(Thickness m, Color? tone = null, double a = 1)
    {
        var c = tone ?? KiC;
        return new TacSketch((dc, s) =>
        {
            if (s.Width < 4) return;
            double y = s.Height / 2;
            dc.DrawRectangle(Mod.Grad(false, (Mod.A(c, 0), 0), (Mod.A(c, .5 * a), .25), (Mod.A(c, .5 * a), .75), (Mod.A(c, 0), 1)), null, new Rect(0, y - 1.5, s.Width, 3));
            dc.DrawRectangle(Mod.Grad(false, (Mod.A(Colors.White, 0), 0), (Mod.A(Mod.Mix(c, Colors.White, .7), .9 * a), .5), (Mod.A(Colors.White, 0), 1)), null, new Rect(0, y - .5, s.Width, 1));
        }) { Height = 3, Margin = m };
    }

    /// <summary>The fight's foot under a line of energy: "Total 622.4M" at the left, the group's word and figure at the right, the AVG mark on a held average.</summary>
    FrameworkElement Foot(Encounter e, DateTime now)
    {
        var (total, word, group, held) = FootOf(e, now);
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        TextBlock Pair(string w, string fig)
        {
            var t = new TextBlock { FontFamily = Mod.Face, FontSize = 11, FontWeight = FontWeights.Medium, Foreground = Frozen(Mod.A(Colors.White, .86)), VerticalAlignment = VerticalAlignment.Center };
            System.Windows.Documents.Typography.SetNumeralAlignment(t, FontNumeralAlignment.Tabular);
            t.Inlines.Add(new System.Windows.Documents.Run(w + " "));
            t.Inlines.Add(new System.Windows.Documents.Run(fig) { FontWeight = FontWeights.Bold, Foreground = White, FontSize = 12 });
            return t;
        }
        var tp = Pair(L.T("Total"), total);
        EnergyMark.After(tp, 11, Frozen(Mod.A(Colors.White, .72)));   // Odyle Energy (Ui/EnergyMark.cs)
        g.Children.Add(tp);
        var right = new StackPanel { Orientation = Orientation.Horizontal };
        if (word.Length > 0) right.Children.Add(Pair(word, group));
        if (held)
        {
            var avg = Mark(L.T("AVG"), size: 8.5); avg.Tag = "avg-badge"; avg.ToolTip = L.T(Encounter.AvgBadge); avg.Margin = new Thickness(5, 0, 0, 0);
            right.Children.Add(avg);
        }
        Grid.SetColumn(right, 1); g.Children.Add(right);
        g.Margin = new Thickness(6, 0, 6, 0);
        return AsFoot(Lit(g), e, now);
    }

    /// <summary>The line between the party and you pinned below it: "2 MORE", small, between two short sparks of energy.</summary>
    static FrameworkElement GapLine(int left)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        var t = Tt(left > 0 ? L.F("{0} more", left).ToUpperInvariant() : "·", 9, Frozen(Mod.A(Colors.White, .85)), FontWeights.SemiBold);
        sp.Children.Add(Sized(EnergyLine(new Thickness(0, 0, 6, 0)), 26));
        sp.Children.Add(Lit(t));
        sp.Children.Add(Sized(EnergyLine(new Thickness(6, 0, 0, 0)), 26));
        static FrameworkElement Sized(FrameworkElement e, double w) { e.Width = w; e.VerticalAlignment = VerticalAlignment.Center; return e; }
        return sp;
    }

    /// <summary>A new leader took the top: the power-up burst behind the meter (at most once in a while, however often the lead swaps).</summary>
    internal void Lead(AuraRow row)
    {
        if (!Moves || (DateTime.UtcNow - _lastLead).TotalMilliseconds < 1500) return;
        _lastLead = DateTime.UtcNow;
        var burst = Burst(row);
        Motion.Play(_under, 1000, t => { row.PowerUp(t); burst?.Invoke(t); }, () => { row.PowerUp(-1); burst?.Invoke(-1); });
    }

    DateTime _lastLead = DateTime.MinValue;

    /// <summary>
    /// One member on a dark core plate (an energy cell, slanted ends), the aura burning round it: flame tongues in the class's
    /// colour rising off the plate's top edge along the member's part of the top, taller and brighter the more damage they
    /// do; inside the plate the bar is a gauge of pure energy (a soft body, a white-hot core line along its foot, a bright
    /// front with one tongue licking up). On it, in shaded white: the rank, the class's art, the name, Combat Power small,
    /// the marks (★ DPS, a healer's ✚, deaths, YOU), a healer's healing a second in mint, the DPS big, the share small.
    /// The leader's aura goes gold-white with static lightning crackling over it and a POWER reading floats over their
    /// figure. Short of room the CP goes first, then the share; a name keeps at least ten characters; a figure is never cut.
    /// Its moments (driven by the look): ignite, flare, gutter, the power-up, a climb's flare.
    /// </summary>
    internal sealed class AuraRow : IThemeRow
    {
        public const double Z = 13, PH = 23, H = Z + PH + 1, Pitch = H + 1, Stage = 16;
        const double In = 2, Sl = 6, PadL = 11, PadR = 17, FrontW = 22, SurgeW = 46, TrailW = 80;
        readonly HudAura _look;
        readonly Grid _root = new() { Height = H };
        readonly Grid _fireHost = new() { IsHitTestVisible = false }, _fill = new() { IsHitTestVisible = false };
        readonly RectangleGeometry _fireClip = new(new Rect(0, -30, 0, H + 30)), _fillClip = new(new Rect(0, 0, 0, H));
        readonly TacSketch _fire, _gold, _bolts, _strike, _plate, _energy, _surge, _flash, _front, _healLine, _ring, _trail, _wedge;
        readonly ScaleTransform _fireS = new(1, 1, 0, Z + 3), _goldS = new(1, 1, 0, Z + 3), _ringS = new(1, 1);
        readonly TranslateTransform _frontX = new(), _surgeX = new(), _trailX = new();
        readonly Grid _words = new() { Margin = new Thickness(PadL, Z, PadR, 1), Height = PH };
        readonly TextBlock _rank = Tt("", 12, White, FontWeights.Bold), _name = Tt("", 13, White, FontWeights.Bold), _cp = Tt("", 9.5, Frozen(Mod.A(Colors.White, .9)), FontWeights.SemiBold),
            _heal = Tt("", 11, Mint2, FontWeights.Bold), _dps = Tt("", 16.5, White, FontWeights.Bold), _share = Tt("", 10.5, Frozen(Mod.A(Colors.White, .88)), FontWeights.SemiBold),
            _dmg = Tt("", 11, Soft, FontWeights.SemiBold), _crit = Tt("", 10.5, Soft, FontWeights.SemiBold), _chipFig = Tt("", 11, White, FontWeights.Bold);
        readonly StackPanel _line = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        readonly Border _iconHost = new() { Width = 17, Height = 17, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
        readonly Border _chip, _cpPill;
        readonly List<FrameworkElement> _marks = new();
        readonly double _dpsW, _shareW, _dmgW, _critW;
        string _cls = "\0";
        MeterMember? _m;
        Color _tone = Colors.Transparent;
        int _seed, _lastRank;
        double _bar, _fk = 1, _gk = 1, _ek = 1, _rightW;
        bool _expanded, _shareOn = true, _lead, _you;

        public FrameworkElement Root => _root;

        /// <summary>Where the gauge is drawn (main's PB ghost, IThemeBarSpan): a full bar fills the plate from its left foot to its right end.</summary>
        public (FrameworkElement On, Rect Track)? BarTrack => _energy.ActualWidth <= 0 ? null : (_energy, new Rect(In, Z, Math.Max(0, _energy.ActualWidth - 2 * In), PH));

        public AuraRow(HudAura look)
        {
            _look = look;
            _dps.Tag = "row-dps"; _share.Tag = "row-share"; _heal.Tag = "heal-figures"; _cp.Tag = "cp";
            _dps.HorizontalAlignment = _share.HorizontalAlignment = _dmg.HorizontalAlignment = _crit.HorizontalAlignment = HorizontalAlignment.Right;
            _share.Margin = new Thickness(0, 2, 0, 0); _dmg.Margin = new Thickness(0, 1, 0, 0); _crit.Margin = new Thickness(0, 1, 0, 0);
            _heal.Margin = new Thickness(6, 1, 0, 0);
            _dpsW = FigCol(_dps, "888.8K") + 4; _shareW = FigCol(_share, "88.8%", "100%", L.T("CP")) + 6;
            _dmgW = FigCol(_dmg, "888.88M") + 8; _critW = FigCol(_crit, "88%") + 8;
            foreach (var w in new[] { 17.0, 25, double.NaN, 0, _dpsW, _shareW, 0, 0 })
                _words.ColumnDefinitions.Add(new ColumnDefinition { Width = double.IsNaN(w) ? new GridLength(1, GridUnitType.Star) : w == 0 ? GridLength.Auto : new GridLength(w) });
            _words.ColumnDefinitions[6].Width = new GridLength(0); _words.ColumnDefinitions[7].Width = new GridLength(0);
            _words.VerticalAlignment = VerticalAlignment.Top;

            // the aura (behind the plate): the class's fire, the leader's gold, both clipped to the member's part
            _fire = new TacSketch(DrawFire) { RenderTransform = _fireS };
            _gold = new TacSketch(DrawGold) { RenderTransform = _goldS, Visibility = Visibility.Collapsed };
            _fireHost.Children.Add(_fire); _fireHost.Children.Add(_gold);
            _fireHost.Clip = _fireClip;
            _bolts = new TacSketch(DrawBolts) { Visibility = Visibility.Collapsed };
            _strike = new TacSketch(DrawStrike) { Opacity = 0, Visibility = Visibility.Collapsed };
            _plate = new TacSketch(DrawPlate);
            // the gauge: its energy clipped to the member's part, the surge running along it
            _energy = new TacSketch(DrawEnergy);
            _surge = new TacSketch(DrawSurge) { Width = SurgeW, HorizontalAlignment = HorizontalAlignment.Left, RenderTransform = _surgeX, Opacity = 0 };
            _trail = new TacSketch(DrawTrail) { Width = TrailW, HorizontalAlignment = HorizontalAlignment.Left, RenderTransform = _trailX };
            _fill.Children.Add(_energy); _fill.Children.Add(_trail); _fill.Children.Add(_surge);
            _fill.Clip = _fillClip;
            _flash = new TacSketch(DrawFlash) { Opacity = 0 };
            _front = new TacSketch(DrawFront) { Width = FrontW, HorizontalAlignment = HorizontalAlignment.Left, RenderTransform = _frontX, Visibility = Visibility.Collapsed };
            _healLine = new TacSketch(DrawHeal) { Visibility = Visibility.Collapsed };
            // the dark wedge behind the figures at the plate's right, over the energy
            _wedge = new TacSketch(DrawWedge);
            // the power-up's shockwave: a ring round the plate, behind it, so it shows as it clears the plate's edges
            _ring = new TacSketch(DrawRing) { Opacity = 0, RenderTransform = _ringS, Visibility = Visibility.Collapsed };
            foreach (var el in new UIElement[] { _ring, _fireHost, _bolts, _plate, _fill, _wedge, _healLine, _flash, _front, _strike }) _root.Children.Add(el);

            _rank.HorizontalAlignment = HorizontalAlignment.Center;
            Put(_rank, 0); Put(_iconHost, 1); Put(_line, 2); Put(_heal, 3); Put(_dps, 4); Put(_share, 5); Put(_dmg, 6); Put(_crit, 7);
            _name.TextTrimming = TextTrimming.CharacterEllipsis;
            _cpPill = new Border { Child = _cp, Background = Frozen(Mod.A(Color.FromRgb(0x0E, 0x12, 0x1C), .75)), CornerRadius = new CornerRadius(2), Padding = new Thickness(3, 0, 3, 0), Margin = new Thickness(5, 1, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            _line.Children.Add(_name); _line.Children.Add(_cpPill);
            _words.Effect = Shade();
            _root.Children.Add(_words);

            // the leader's power reading, floating over their figure
            var cp = new StackPanel { Orientation = Orientation.Horizontal };
            var (sf, sw) = ShoutFace();
            var pw = Tt(L.T("POWER"), 10.5, Frozen(AuGoldC), sw, sf); pw.Margin = new Thickness(0, 0, 5, 0);
            cp.Children.Add(pw); cp.Children.Add(_chipFig);
            _chip = new Border
            {
                Child = cp, Padding = new Thickness(6, 0, 6, 0), Height = 17, CornerRadius = new CornerRadius(2), Background = Frozen(Mod.A(CoreBotC, .92)),
                BorderThickness = new Thickness(1), BorderBrush = Frozen(Mod.A(AuGoldC, .9)), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, -Stage + 1, PadR - 2, 0), Visibility = Visibility.Collapsed, IsHitTestVisible = false, Tag = "power-chip",
            };
            _root.Children.Add(_chip);
            _root.SizeChanged += (_, _) => { Fit(); Place(); };
        }

        void Put(FrameworkElement el, int col) { Grid.SetColumn(el, col); _words.Children.Add(el); }

        public void Set(MeterMember m, MeterContext c, bool fresh)
        {
            _m = m;
            bool lead = m.Rank == 1 && !m.CpOnly && !m.Pvp && c.E.HasDamage;
            int seed = (int)(Math.Abs(m.Id) % 99991) + 17;
            if (m.ClassColor != _tone || lead != _lead || m.You != _you || seed != _seed)
            {
                _tone = m.ClassColor; _lead = lead; _you = m.You; _seed = seed;
                _fire.InvalidateVisual(); _gold.InvalidateVisual(); _plate.InvalidateVisual(); _energy.InvalidateVisual(); _front.InvalidateVisual(); _flash.InvalidateVisual(); _bolts.InvalidateVisual();
            }
            _fire.Visibility = lead ? Visibility.Collapsed : Visibility.Visible;
            _gold.Visibility = _bolts.Visibility = _chip.Visibility = lead ? Visibility.Visible : Visibility.Collapsed;
            _strike.Visibility = _ring.Visibility = lead ? Visibility.Visible : Visibility.Collapsed;
            // a lower row sits under the one above it: its aura rises behind that row's plate, never over its words
            Panel.SetZIndex(_root, 1000 - m.Rank);
            if (!fresh && c.Moves && _lastRank > 0)
            {
                if (lead && _lastRank > 1) _look.Lead(this);
                else if (m.Rank < _lastRank) Climb();
            }
            _lastRank = m.Rank;
            if (_expanded != c.Expanded)
            {
                _expanded = c.Expanded;
                _words.ColumnDefinitions[6].Width = new GridLength(c.Expanded ? _dmgW : 0);
                _words.ColumnDefinitions[7].Width = new GridLength(c.Expanded ? _critW : 0);
            }
            _rank.Text = m.Rank.ToString(CultureInfo.InvariantCulture);
            _rank.Foreground = lead ? Frozen(AuGoldHotC) : White;
            if (m.Class != _cls)
            {
                _cls = m.Class;
                var icon = ThemeText.Icon(m.Class, 17, m.Name);
                if (icon is Border b) { b.BorderBrush = Frozen(Mod.Mix(m.ClassColor, Colors.White, .35)); b.BorderThickness = new Thickness(1.2); }
                _iconHost.Child = icon;
            }
            _name.Text = m.Name;
            _cp.Text = m.Cp;
            foreach (var mk in _marks) _line.Children.Remove(mk);
            _marks.Clear();
            void Add(FrameworkElement el) { el.Margin = new Thickness(5, 0, 0, 0); _line.Children.Add(el); _marks.Add(el); }
            if (m.TopDps) { var t = Mark("★ " + L.T("DPS"), Frozen(AuGoldHotC), AuGoldC); t.Tag = "top-dps"; t.ToolTip = L.T("Top DPS"); Add(t); }
            if (m.TopHeals) { var t = Mark("✚", MarkMint, MarkMintC); t.Tag = "top-heals"; t.ToolTip = L.T("Top heals"); Add(t); }
            if (m.Deaths > 0) { var d = Tt($"✕{m.Deaths}", 10, Coral, FontWeights.Bold); d.Tag = "deaths"; Add(d); }
            if (m.You) { var y = Mark(L.T("YOU"), filled: true); y.Tag = "you"; Add(y); }
            _heal.Text = m.Heal.Length > 0 ? "+" + m.Heal : "";
            _heal.Visibility = m.Heal.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            _dps.Text = m.Figure;
            _dps.FontSize = m.CpOnly ? 14 : 16.5;
            _words.ColumnDefinitions[4].Width = new GridLength(Math.Max(_dpsW, TextW(_dps, m.Figure) + 4));
            _share.Text = m.ShareText;
            _dmg.Text = c.Expanded ? m.Damage : "";
            _crit.Text = c.Expanded ? m.Crit : "";
            _chipFig.Text = m.Figure;
            bool heal = ParserRows.HealBarsPref && m.HealShare > 0;
            _healLine.Visibility = heal ? Visibility.Visible : Visibility.Collapsed;
            if (heal) _healLine.InvalidateVisual();
            Fit();
            SlideBar(_energy, c, "bar", m.CpOnly ? 0 : BarOf(m, c), v => { _bar = v; Place(); });
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
            var (cp, share) = Mod.Fit(_name, room, _cp.Text.Length > 0 ? _cpPill : null, _marks, _shareW, min);
            _cpPill.Visibility = cp ? Visibility.Visible : Visibility.Collapsed;
            if (share != _shareOn || cols[5].Width.Value != (share ? _shareW : 0))
            {
                _shareOn = share;
                cols[5].Width = new GridLength(share ? _shareW : 0);
            }
            _share.Visibility = share ? Visibility.Visible : Visibility.Collapsed;
            double right = heal + cols[4].Width.Value + cols[6].Width.Value + cols[7].Width.Value + (share ? _shareW : 0);
            if (Math.Abs(right - _rightW) > .5) { _rightW = right; _wedge.InvalidateVisual(); Place(); }
        }

        /// <summary>The gauge's front x now (the member's part of the plate, as far as a moment has let it fill).</summary>
        double FrontX(double w) => In + (w - 2 * In) * Math.Clamp(_bar, 0, 1) * Math.Clamp(_ek, 0, 1);

        /// <summary>Lays the parts out for the bar and the moment's factors: clips, the front's place, the fire's height (no redraw).</summary>
        void Place()
        {
            double w = _root.ActualWidth;
            if (w <= 0) return;
            double fx = FrontX(w), b = Math.Clamp(_bar, 0, 1);
            _fillClip.Rect = new Rect(0, 0, Math.Max(0, fx), H);
            _fireClip.Rect = new Rect(0, -30, Math.Max(0, fx + 3), H + 30);
            bool front = b > .015 && _ek > .02;
            _front.Visibility = front ? Visibility.Visible : Visibility.Collapsed;
            _frontX.X = Math.Min(fx, w - In - Sl * .5) - FrontW / 2;
            _trailX.X = fx - TrailW;
            // the front fades out as it reaches the dark wedge behind the figures: never a bright line through a digit
            double wedgeX = w - PadR - _rightW - 4 - 24;
            _front.Opacity = (.6 + .4 * b) * (_rightW > 0 ? Math.Clamp((wedgeX - fx) / 16, 0, 1) : 1);
            _fireS.ScaleY = Math.Max(0, (.55 + .45 * b) * _fk);
            _fire.Opacity = Math.Clamp(.7 + .3 * b, 0, 1) * Math.Clamp(_fk, 0, 1);
            _goldS.ScaleY = Math.Max(0, _gk);
            _gold.Opacity = Math.Clamp(_gk, 0, 1);
            _bolts.Opacity = _boltsOff ? 0 : Math.Clamp(_gk, 0, 1);
        }

        // ---------------------------------------------------------------- the drawn parts (each drawn once per size or member)

        Rect PlateRect(Size s) => new(In, Z, Math.Max(10, s.Width - 2 * In), PH);

        void DrawFire(DrawingContext dc, Size s)
        {
            if (_m == null || s.Width < 30) return;
            Flames(dc, In + Sl, s.Width - In, Z + 3, Z + 3, _seed, _tone, Mod.Mix(_tone, Colors.White, .65), 1, 24);
        }

        void DrawGold(DrawingContext dc, Size s)
        {
            if (_m == null || s.Width < 30) return;
            Flames(dc, In + Sl, s.Width - In, Z + 3, Z + 3 + Stage, _seed + 5, AuGoldC, AuGoldHotC, 1, 28);
            // the gold halo round the plate
            var g = Plate(PlateRect(s), Sl);
            dc.DrawGeometry(null, Pn(AuGoldC, .16, 7), g);
            dc.DrawGeometry(null, Pn(AuGoldC, .38, 3), g);
        }

        void DrawBolts(DrawingContext dc, Size s)
        {
            if (s.Width < 60) return;
            double w = s.Width;
            DrawBolt(dc, Zig(new Point(w * .22, -Stage + 4), new Point(w * .25, Z + 2), 5), 1, 2.4);
        }

        /// <summary>The power-up's strikes: three bolts down into the plate's top edge (shown only while it plays).</summary>
        void DrawStrike(DrawingContext dc, Size s)
        {
            if (s.Width < 60) return;
            double w = s.Width;
            DrawBolt(dc, Bolt(new Point(w * .06, -Stage + 2), new Point(w * .17, Z + 3), _seed + 11, 4.5, 6), 1, 2.6);
            DrawBolt(dc, Bolt(new Point(w * .4, -Stage + 2), new Point(w * .35, Z + 3), _seed + 12, 4, 5), 1, 2.6);
            DrawBolt(dc, Bolt(new Point(w * .64, -Stage + 2), new Point(w * .54, Z + 3), _seed + 13, 4.5, 6), 1, 2.6);
        }

        void DrawPlate(DrawingContext dc, Size s)
        {
            if (_m == null || s.Width < 30) return;
            var rim = _you ? Colors.White : _lead ? AuGoldC : Mod.Mix(_tone, Colors.White, .2);
            CorePlate(dc, PlateRect(s), Sl, _lead ? AuGoldC : _tone, _lead ? 1 : .7, rim, _you ? .95 : _lead ? .8 : .5);
        }

        void DrawEnergy(DrawingContext dc, Size s)
        {
            if (_m == null || s.Width < 30) return;
            Energy(dc, PlateRect(s), Sl, _tone);
        }

        void DrawSurge(DrawingContext dc, Size s) =>
            dc.DrawRectangle(Mod.Grad(false, (Mod.A(Colors.White, 0), 0), (Mod.A(Colors.White, .62), .6), (Mod.A(Colors.White, 0), 1)), null, new Rect(0, Z + 1, s.Width, PH - 2));

        void DrawFlash(DrawingContext dc, Size s)
        {
            if (s.Width < 30) return;
            var c = _lead ? AuGoldHotC : Mod.Mix(_tone, Colors.White, .7);
            dc.DrawGeometry(VGrad(Z, Z + PH, (Mod.A(c, .55), 0), (Mod.A(c, .08), .6), (Mod.A(c, 0), 1)), null, Plate(PlateRect(s), Sl));
        }

        void DrawFront(DrawingContext dc, Size s)
        {
            if (_m == null) return;
            Front(dc, s, Z, PH, _lead ? AuGoldC : _tone, 0);
        }

        /// <summary>The energy thickening toward the front: a bright wash that rides along just behind it (inside the gauge).</summary>
        void DrawTrail(DrawingContext dc, Size s)
        {
            if (_m == null) return;
            var c = Mod.Mix(_tone, Colors.White, .45);
            dc.DrawRectangle(Mod.Grad(false, (Mod.A(c, 0), 0), (Mod.A(c, .22), .7), (Mod.A(c, .5), 1)), null, new Rect(0, Z + 2, s.Width, PH - 4));
        }

        /// <summary>The shockwave ring round the plate at its widest (the power-up scales it out from the plate's middle).</summary>
        void DrawRing(DrawingContext dc, Size s)
        {
            if (s.Width < 40) return;
            var c = new Point(s.Width / 2, Z + PH / 2);
            double rx = s.Width / 2 - 2, ry = PH / 2 + 9;
            _ringS.CenterX = c.X; _ringS.CenterY = c.Y;
            dc.DrawEllipse(null, Pn(AuGoldC, .28, 6), c, rx, ry);
            dc.DrawEllipse(null, Pn(AuGoldC, .95, 2.4), c, rx, ry);
            dc.DrawEllipse(null, Pn(Colors.White, 1, 1), c, rx, ry);
        }

        /// <summary>The dark wedge behind the DPS and share: 72% dark from just before the figures to the plate's end.</summary>
        /// <summary>A sharp zig-zag: three segments, out to one side, back across, down to the end.</summary>
        static Geometry Zig(Point a, Point b, double amp)
        {
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                double dx = b.X - a.X, dy = b.Y - a.Y;
                c.BeginFigure(a, false, false);
                c.LineTo(new Point(a.X + dx * .38 + amp, a.Y + dy * .38), true, false);
                c.LineTo(new Point(a.X + dx * .56 - amp, a.Y + dy * .56), true, false);
                c.LineTo(b, true, false);
            }
            g.Freeze();
            return g;
        }

        void DrawWedge(DrawingContext dc, Size s)
        {
            if (_m == null || s.Width < 60 || _rightW <= 0) return;
            var r = PlateRect(s);
            double x = s.Width - PadR - _rightW - 4;
            dc.PushClip(Plate(r, Sl));
            var b = new LinearGradientBrush { MappingMode = BrushMappingMode.Absolute, StartPoint = new Point(x - 24, 0), EndPoint = new Point(x, 0) };
            b.GradientStops.Add(new GradientStop(Mod.A(CoreBotC, 0), 0)); b.GradientStops.Add(new GradientStop(Mod.A(CoreBotC, .72), 1)); b.Freeze();
            dc.DrawRectangle(b, null, new Rect(x - 24, r.Y + 2, r.Right - x + 24, r.Height - 2));
            dc.Pop();
        }

        void DrawHeal(DrawingContext dc, Size s)
        {
            if (_m is not { } m || s.Width < 30) return;
            var r = PlateRect(s);
            dc.DrawRectangle(Mod.Br(MintC2, .95), null, new Rect(r.X + Sl, r.Bottom - 2, Math.Max(3, (r.Width - Sl) * Math.Clamp(m.HealShare, 0, 1)), 2));
        }

        // ---------------------------------------------------------------- the moments (0..1; outside: rest)

        /// <summary>The surge: a light running along the gauge to its front.</summary>
        void Surge(double p)
        {
            if (p is < 0 or >= 1) { _surge.Opacity = 0; return; }
            double fx = FrontX(Math.Max(1, _root.ActualWidth));
            _surgeX.X = -SurgeW + (fx + SurgeW * .4) * Out(p);
            _surge.Opacity = Math.Sin(Math.PI * p);
        }

        /// <summary>The fight starting: the gauge fills from nothing as the aura ignites up from the plate, with a flash.</summary>
        public void Ignite(double p)
        {
            if (p is < 0 or >= 1) { _ek = 1; _fk = 1; if (!_powering) _gk = 1; _flash.Opacity = 0; Surge(-1); Place(); return; }
            _ek = Out(Seg(p, 0, .45));
            _fk = Back(Seg(p, .1, .7), 2.4);
            if (!_powering) _gk = Math.Min(1.04, _fk);
            _flash.Opacity = p < .12 ? .6 * Seg(p, 0, .12) : .6 * (1 - Seg(p, .12, .5));
            Surge(Seg(p, .3, 1));
            Place();
        }

        /// <summary>A kill: every aura flares up tall and settles back.</summary>
        public void Flare(double p)
        {
            if (p is < 0 or >= 1) { _fk = 1; _gk = 1; _flash.Opacity = 0; _boltsOff = false; Surge(-1); Place(); return; }
            _boltsOff = true;   // the end's banner lands over the stage
            double k = p < .18 ? Out(p / .18) : 1 - Out(Seg(p, .18, 1));
            _fk = 1 + .9 * k; _gk = 1 + .25 * k;
            _flash.Opacity = 0;
            Surge(Seg(p, .05, .7));
            Place();
        }

        /// <summary>A wipe: the auras gutter down nearly out, then come back low.</summary>
        public void Gutter(double p)
        {
            if (p is < 0 or >= 1) { _fk = 1; _gk = 1; _boltsOff = false; Place(); return; }
            _boltsOff = true;
            double k = p < .25 ? Out(p / .25) : 1 - Seg(p, .6, 1);
            _fk = 1 - .85 * k; _gk = 1 - .85 * k;
            Place();
        }

        bool _powering, _boltsOff;

        /// <summary>
        /// The power-up (this row took the lead): the gold aura bursts up from nothing past its height and settles, the plate
        /// flashes gold-white, lightning strikes the plate's edge three times and a surge runs the gauge.
        /// </summary>
        public void PowerUp(double p)
        {
            if (p is < 0 or >= 1) { _powering = false; _gk = 1; _flash.Opacity = 0; _strike.Opacity = 0; _ring.Opacity = 0; Surge(-1); Place(); return; }
            _powering = true;
            _gk = Math.Min(1.05, Back(Seg(p, 0, .35), 3.2));
            _flash.Opacity = p < .06 ? .55 * Seg(p, 0, .06) : .55 * (1 - Seg(p, .06, .45));
            _strike.Opacity = (p is > .02 and < .18) || (p is > .24 and < .32) || (p is > .42 and < .5) || (p is > .6 and < .7) ? 1 : 0;
            double rk = Out(Seg(p, .04, .6));
            _ringS.ScaleX = .3 + .7 * rk; _ringS.ScaleY = .5 + .5 * rk;
            _ring.Opacity = p < .06 ? Seg(p, 0, .06) : 1 - Seg(p, .5, .92);
            Surge(Seg(p, .15, .8));
            Place();
        }

        /// <summary>The row climbed a rank: its aura flares and a surge runs the gauge (with the glide, not against it).</summary>
        void Climb()
        {
            Motion.Play(_flash, 650, t =>
            {
                double k = Math.Sin(Math.PI * t);
                _fk = 1 + .45 * k;
                _flash.Opacity = .25 * k;
                Surge(t);
                Place();
            }, () => { _fk = 1; _flash.Opacity = 0; Surge(-1); Place(); });
        }

        static double Seg(double p, double a, double b) => Math.Clamp((p - a) / (b - a), 0, 1);
        static double Out(double p) => Motion.Out(p);
        static double Back(double p, double s = 1.7) { p = Math.Clamp(p, 0, 1) - 1; return p * p * ((s + 1) * p + s) + 1; }

        /// <summary>The row's plate rect in the row (for the look's burst behind it).</summary>
        public Rect PlateIn() => PlateRect(new Size(_root.ActualWidth, H));
    }
}
