using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using KinahLedger.Parser;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

sealed partial class HudStorm
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

    /// <summary>The fight's foot under its charged hairline: "Total 622.4M" at the left, the group's word and figure at the right, the AVG mark on a held average.</summary>
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
            t.Inlines.Add(new System.Windows.Documents.Run(fig) { FontWeight = FontWeights.Bold, Foreground = Arc, FontSize = 12 });
            return t;
        }
        var tp = Pair(L.T("Total"), total);
        EnergyMark.After(tp, 11, Frozen(Mod.A(Colors.White, .72)));   // Odyle Energy (Ui/EnergyMark.cs)
        g.Children.Add(tp);
        var right = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 0, 0, 0) };
        if (word.Length > 0) right.Children.Add(Pair(word, group));
        if (held)
        {
            var avg = Mark(L.T("AVG"), size: 8.5); avg.Tag = "avg-badge"; avg.ToolTip = L.T(Encounter.AvgBadge); avg.Margin = new Thickness(5, 0, 0, 0);
            right.Children.Add(avg);
        }
        Grid.SetColumn(right, 1); g.Children.Add(right);
        return AsFoot(g, e, now);
    }

    /// <summary>The line between the party and you pinned below it: "2 MORE", small and quiet between two blue hairlines.</summary>
    static FrameworkElement GapLine(int left)
    {
        var g = new Grid { VerticalAlignment = VerticalAlignment.Center };
        g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); g.ColumnDefinitions.Add(new ColumnDefinition());
        FrameworkElement Rule(bool toRight) => new TacSketch((dc, s) => dc.DrawRectangle(Mod.Grad(false, (Mod.A(VoltC, toRight ? 0 : .4), 0), (Mod.A(VoltC, toRight ? .4 : 0), 1)), null, new Rect(0, Math.Round(s.Height / 2), s.Width, 1))) { Height = 3, Margin = new Thickness(toRight ? 6 : 8, 0, toRight ? 8 : 6, 0) };
        var a = Rule(true); g.Children.Add(a);
        var t = Tt(left > 0 ? L.F("{0} more", left).ToUpperInvariant() : "·", 8.5, Frozen(Mod.A(VoltC, .9)), FontWeights.Bold);
        Grid.SetColumn(t, 1); g.Children.Add(t);
        var b = Rule(false); Grid.SetColumn(b, 2); g.Children.Add(b);
        return g;
    }

    /// <summary>The conduit's windings: faint dark ticks every 7 px, a frozen tile drawn once.</summary>
    static readonly DrawingBrush Windings = MakeWindings();

    static DrawingBrush MakeWindings()
    {
        var g = new DrawingGroup();
        using (var dc = g.Open())
        {
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, 7, 20));
            dc.DrawRectangle(Mod.Br(Deep0C, .22), null, new Rect(6, 0, 1, 20));
        }
        g.Freeze();
        var b = new DrawingBrush(g) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 7, 20), ViewportUnits = BrushMappingMode.Absolute, Stretch = Stretch.None };
        b.Freeze();
        return b;
    }

    /// <summary>The charge that runs a bar on the shimmer: a band of white-blue light with a hot line at its front (frozen, moved by its transform).</summary>
    static readonly LinearGradientBrush SweepBand = Mod.Grad(false, (Mod.A(VoltC, 0), 0), (Mod.A(ArcC, .42), .78), (Mod.A(ArcC, .95), .86), (Mod.A(ArcC, .9), .9), (Mod.A(ArcC, 0), 1));

    /// <summary>
    /// One member as a charged conduit: a cut-cornered slot of night glass, filled to their part of the top in their class's
    /// colour, darker at the left and charging up to a white-hot leading edge, an electric core line along its foot, and a
    /// static spark crown at the edge (the edge a jagged filament, seeded bolts leaping off its corners into the gap between
    /// rows; the leader's crackles brightest, and the lead's bolt glyph stands by their rank). On it: the rank, the class's
    /// art in a blue ring, the name in white with the Combat Power after it and the marks (★ DPS, ✚, deaths, YOU), a healer's
    /// healing a second in mint, the DPS big and the share small. You: a white charge at the slot's left and a bright blue rim.
    /// Short of room the CP goes first, then the share; a name keeps at least ten characters; a figure is never cut (Mod.Fit).
    /// Cheap: the conduit is one drawing (its windings a frozen tile, its spark crown a frozen drawing made once for the edge's
    /// kind and moved there); the moments only move or fade three small parts (the charge band, the flash, the glyph).
    /// </summary>
    sealed class StormRow : IThemeRow, IThemeBarSpan
    {
        public const double H = 26, Pitch = 29;
        const double Pad = 6, RankW = 20;
        readonly Grid _root = new() { Height = H };
        readonly TacSketch _art, _glyph;
        readonly Canvas _sweepHost = new() { IsHitTestVisible = false };
        readonly System.Windows.Shapes.Rectangle _sweepEl = new() { Width = 40, Height = H, Fill = SweepBand, Visibility = Visibility.Collapsed };
        readonly TranslateTransform _sweepAt = new();
        readonly RectangleGeometry _sweepClip = new();
        readonly System.Windows.Shapes.Rectangle _flashEl = new() { Fill = Frozen(Mod.A(ArcC, .66)), Opacity = 0, Visibility = Visibility.Collapsed, IsHitTestVisible = false };
        readonly ScaleTransform _glyphScale = new(1, 1);
        readonly Grid _words = new() { Margin = new Thickness(Pad, 0, 8, 0) };
        readonly TextBlock _rank = Tt("", 12, Frozen(ArcC), FontWeights.Bold), _name = Tt("", 13, White, FontWeights.Bold), _cp = Tt("", 9.5, Frozen(Mod.A(Colors.White, .8)), FontWeights.SemiBold),
            _heal = Tt("", 10.5, Mint2, FontWeights.Bold), _dps = Tt("", 16.5, White, FontWeights.Bold), _share = Tt("", 10.5, Frozen(Mod.A(Colors.White, .92)), FontWeights.SemiBold),
            _dmg = Tt("", 11, Soft, FontWeights.SemiBold), _crit = Tt("", 10.5, Soft, FontWeights.SemiBold);
        readonly StackPanel _line = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        readonly Border _iconHost = new() { Width = 18, Height = 18, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
        readonly List<FrameworkElement> _marks = new();
        readonly double _dpsW, _shareW, _dmgW, _critW;
        string _cls = "\0";
        MeterMember? _m;
        double _bar;
        int _rankWas;
        bool _expanded, _shareOn = true;
        // the drawing's parts kept between draws: the slot's shape for its size, the class's brushes, the crown
        Size _slotSize;
        Geometry? _slot;
        Color _tone;
        Brush? _fill, _coreGlow, _core, _edgeGlow, _edge;
        Pen? _edgePen, _edgeGlowPen;

        /// <summary>The bar's bolt-cut leading edge at <paramref name="ex"/>: four fixed points down the row, three px either way (seeded by the member, so it never twitches).</summary>
        List<Point> Zig(double ex, double h, double fx)
        {
            int seed = (int)(Math.Abs(_m!.Id) % 997);
            double k = fx < 56 ? 0 : 1;   // a short bar (its end by the class art) ends straight
            double[] o = (seed % 3) switch { 0 => new[] { 2.5, -3, 2, -2.5 }, 1 => new[] { -2.0, 3, -3, 2 }, _ => new[] { 3.0, -2, 3, -3 } };
            return new List<Point> { new(ex + o[0] * k, .5), new(ex + o[1] * k, h * .34), new(ex + o[2] * k, h * .64), new(ex + o[3] * k, h - .5) };
        }

        /// <summary>A class colour a touch richer (about 15% more saturated), so the conduits read as charged.</summary>
        static Color Rich(Color c)
        {
            double g = (c.R + c.G + c.B) / 3.0;
            byte F(byte v) => (byte)Math.Clamp(Math.Round(g + (v - g) * 1.15), 0, 255);
            return Color.FromArgb(c.A, F(c.R), F(c.G), F(c.B));
        }
        string _crownKey = "";
        DrawingGroup? _crown;
        double _scrimW;

        public FrameworkElement Root => _root;
        public MeterMember? Member => _m;

        /// <summary>The bar's leading edge, px from the row's left.</summary>
        public double EdgeX => Math.Max(0, (_root.ActualWidth - 1) * Math.Clamp(_bar, 0, 1));

        /// <summary>Where a full bar is drawn (the PB ghost, IThemeBarSpan): the conduit's whole slot, from its half-pixel inset.</summary>
        public (FrameworkElement On, Rect Track)? BarTrack => _art.ActualWidth <= 0 ? null : (_art, new Rect(.5, 0, Math.Max(0, _art.ActualWidth - 1), _art.ActualHeight));

        public StormRow()
        {
            _dps.Tag = "row-dps"; _share.Tag = "row-share"; _heal.Tag = "heal-figures"; _cp.Tag = "cp";
            _dps.HorizontalAlignment = _share.HorizontalAlignment = _dmg.HorizontalAlignment = _crit.HorizontalAlignment = HorizontalAlignment.Right;
            _share.Margin = new Thickness(0, 2, 0, 0); _dmg.Margin = new Thickness(0, 1, 0, 0); _crit.Margin = new Thickness(0, 1, 0, 0);
            _heal.Margin = new Thickness(6, 1, 0, 0);
            // the columns: rank, the class art, the name's line (the rest), heal, the DPS, the share; expanded, the damage and the crit rate
            _dpsW = FigCol(_dps, "888.8K") + 4; _shareW = FigCol(_share, "88.8%", "100%", L.T("CP")) + 6;
            _dmgW = FigCol(_dmg, "888.88M") + 8; _critW = FigCol(_crit, "88%") + 8;
            foreach (var w in new[] { RankW, 25, double.NaN, 0, _dpsW, _shareW, 0, 0 })
                _words.ColumnDefinitions.Add(new ColumnDefinition { Width = double.IsNaN(w) ? new GridLength(1, GridUnitType.Star) : w == 0 ? GridLength.Auto : new GridLength(w) });
            _words.ColumnDefinitions[6].Width = new GridLength(0); _words.ColumnDefinitions[7].Width = new GridLength(0);
            _art = new TacSketch(Draw);
            _sweepEl.RenderTransform = _sweepAt;
            _sweepHost.Children.Add(_sweepEl);
            _sweepHost.Clip = _sweepClip;
            // the lead's bolt glyph: drawn once, landed by its scale
            _glyph = new TacSketch((dc, s) => Glyph(dc, new Rect(0, 0, s.Width, s.Height)), 10, 17)
            {
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(Pad + 9, 0, 0, 0),
                RenderTransformOrigin = new Point(.5, .5), RenderTransform = _glyphScale, Visibility = Visibility.Collapsed,
            };
            _root.Children.Add(_art); _root.Children.Add(_sweepHost); _root.Children.Add(_flashEl); _root.Children.Add(_glyph);
            _rank.HorizontalAlignment = HorizontalAlignment.Left; _rank.Margin = new Thickness(3, 0, 0, 0);
            Put(_rank, 0); Put(_iconHost, 1); Put(_line, 2); Put(_heal, 3); Put(_dps, 4); Put(_share, 5); Put(_dmg, 6); Put(_crit, 7);
            _name.TextTrimming = TextTrimming.CharacterEllipsis;
            _cp.Margin = new Thickness(5, 2, 0, 0);
            _line.Children.Add(_name); _line.Children.Add(_cp);
            _root.Children.Add(_words);
            _root.SizeChanged += (_, _) => { Fit(); Clip(); _flashEl.Clip = Cut(new Rect(.5, .5, Math.Max(1, _root.ActualWidth - 1), H - 1), 6, 2); };
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
            if (m.Class != _cls)
            {
                _cls = m.Class;
                var icon = ThemeText.Icon(m.Class, 18, m.Name);
                if (icon is Border b) { b.BorderBrush = Frozen(Mod.A(VoltC, .85)); b.BorderThickness = new Thickness(1); }
                _iconHost.Child = icon;
            }
            _name.Text = m.Name;
            _cp.Text = m.Cp;
            // the marks after the name: ★ DPS, a healer's ✚, deaths, YOU (kept whole: the name and the CP give way first)
            foreach (var mk in _marks) _line.Children.Remove(mk);
            _marks.Clear();
            void Add(FrameworkElement el) { el.Margin = new Thickness(5, 0, 0, 0); _line.Children.Add(el); _marks.Add(el); }
            if (m.TopDps) { var t = Mark("★ " + L.T("DPS"), VoltInk, VoltC); t.Tag = "top-dps"; t.ToolTip = L.T("Top DPS"); Add(t); }
            if (m.TopHeals) { var t = Mark("✚", MarkMint, MarkMintC); t.Tag = "top-heals"; t.ToolTip = L.T("Top heals"); Add(t); }
            if (m.Deaths > 0) { var d = Tt($"✕{m.Deaths}", 10, Coral, FontWeights.Bold); d.Tag = "deaths"; Add(d); }
            if (m.You) { var y = Mark(L.T("YOU"), filled: true); y.Tag = "you"; Add(y); }
            _heal.Text = m.Heal.Length > 0 ? "+" + m.Heal : "";
            _heal.Visibility = m.Heal.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            _dps.Text = m.Figure;
            _dps.FontSize = m.CpOnly ? 14 : 16.5;
            // a wider figure (Combat Power before the first hit, "167,548") widens its column: never a digit cut
            _words.ColumnDefinitions[4].Width = new GridLength(Math.Max(_dpsW, TextW(_dps, m.Figure) + 4));
            _share.Text = m.ShareText;
            _dmg.Text = c.Expanded ? m.Damage : "";
            _crit.Text = c.Expanded ? m.Crit : "";
            Fit();
            _glyph.Visibility = Lead ? Visibility.Visible : Visibility.Collapsed;
            _rank.Margin = new Thickness(Lead ? 0 : 3, 0, 0, 0);
            SlideBar(_art, c, "bar", BarOf(m, c), v => { _bar = v; Clip(); _art.InvalidateVisual(); });
            _art.InvalidateVisual();
            // a rank won: a charge runs along the bar of the row that climbed (the glide carries it up)
            if (!fresh && c.Moves && _rankWas > 0 && m.Rank < _rankWas)
                Motion.Play(_sweepHost, 520, Sweep, () => Sweep(-1));
            _rankWas = m.Rank;
        }

        /// <summary>The charge band's room: the bar as filled now.</summary>
        void Clip() => _sweepClip.Rect = new Rect(0, 0, Math.Max(0, EdgeX + 1), H);

        /// <summary>The shimmer's charge running along this bar (0 to 1; -1 off): the band moved, never redrawn.</summary>
        public void Sweep(double t)
        {
            bool on = t is > 0 and < 1 && EdgeX > 4;
            _sweepEl.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            if (!on) return;
            _sweepAt.X = -40 + (EdgeX + 46) * t;
            _sweepEl.Opacity = 1 - Math.Pow(t, 3) * .6;
        }

        /// <summary>The thunderclap on this row: its white flash (0 to 1) and the bolt glyph landing (0 to 1, a pop past 1).</summary>
        public void Strike(double flash, double glyph)
        {
            _flashEl.Opacity = .4 * Math.Clamp(flash, 0, 1);
            _flashEl.Visibility = flash > .01 ? Visibility.Visible : Visibility.Collapsed;
            _glyphScale.ScaleX = _glyphScale.ScaleY = Math.Max(0, glyph);
        }

        /// <summary>The crowding rule (Mod.Fit): the CP goes first, then the share; the name keeps at least ten characters.</summary>
        void Fit()
        {
            double total = _root.ActualWidth;
            if (total <= 0) return;
            var cols = _words.ColumnDefinitions;
            double heal = _heal.Visibility == Visibility.Visible ? Mod.W(_heal) + _heal.Margin.Left : 0;
            double room = total - Pad - 8 - cols[0].Width.Value - cols[1].Width.Value - heal - cols[4].Width.Value - cols[6].Width.Value - cols[7].Width.Value - 2;
            double min = _name.Text.Length <= NameKeep ? TextW(_name, _name.Text) : TextW(_name, _name.Text[..NameKeep] + "…") + 1;
            var (cp, share) = Mod.Fit(_name, room, _cp.Text.Length > 0 ? _cp : null, _marks, _shareW, min);
            _cp.Visibility = cp ? Visibility.Visible : Visibility.Collapsed;
            if (share != _shareOn || cols[5].Width.Value != (share ? _shareW : 0))
            {
                _shareOn = share;
                cols[5].Width = new GridLength(share ? _shareW : 0);
            }
            _share.Visibility = share ? Visibility.Visible : Visibility.Collapsed;
            double sw = 8 + _dpsW + _shareW + (_expanded ? _dmgW + _critW : 0) + 10;   // the same on every row: one clean column
            if (Math.Abs(sw - _scrimW) > .5) { _scrimW = sw; _art.InvalidateVisual(); }
        }

        /// <summary>Whether this row holds the lead's glyph: first with damage done.</summary>
        bool Lead => _m is { Rank: 1, CpOnly: false } m && !double.IsNaN(m.FigureValue) && m.FigureValue > 0;

        static readonly Brush SlotGround = Mod.Grad(true, (Mod.A(Deep1C, .72), 0), (Mod.A(Deep0C, .8), 1)), TopLight = Mod.Grad(true, (Mod.A(Colors.White, .14), 0), (Mod.A(Colors.White, 0), .5)),
            HealLine = Mod.Br(MintC2, .95), ScrimIn = Mod.Grad(false, (Mod.A(Deep0C, 0), 0), (Mod.A(Deep0C, .6), 1)), ScrimFill = Mod.Br(Deep0C, .6), ScrimEdge = Mod.Br(VoltC, .16);
        static readonly Pen Rim = Mod.P(Mod.Br(VoltC, .14), 1), YouRim = Mod.P(Mod.Br(VoltC, .7), 1.2);

        void Draw(DrawingContext dc, Size s)
        {
            if (_m is not { } m || s.Width < 30) return;
            double h = s.Height, w = s.Width - 1;
            if (_slot == null || _slotSize != s)
            {
                _slotSize = s;
                _slot = Cut(new Rect(.5, .5, Math.Max(1, s.Width - 1), Math.Max(1, h - 1)), 6, 2);
            }
            var slot = _slot;
            var tone = Rich(m.ClassColor);
            if (_fill == null || tone != _tone)
            {
                _tone = tone;
                _fill = Mod.Grad(false, (Mod.A(tone, .18), 0), (Mod.A(tone, .42), .7), (Mod.A(tone, .6), 1));
                _coreGlow = Mod.Grad(false, (Mod.A(tone, .1), 0), (Mod.A(Mod.Mix(tone, Colors.White, .3), .6), 1));
                _core = Mod.Grad(false, (Mod.A(Mod.Mix(tone, Colors.White, .55), .25), 0), (Mod.A(Mod.Mix(tone, Colors.White, .75), 1), 1));
                _edgeGlow = Mod.Grad(false, (Mod.A(tone, 0), 0), (Mod.A(Mod.Mix(tone, Colors.White, .35), .55), 1));
                _edge = Mod.Br(Mod.Mix(tone, Colors.White, .7), 1);
                _edgePen = new Pen(Mod.Br(Mod.Mix(tone, Colors.White, .75), 1), 1.3) { LineJoin = PenLineJoin.Round }; _edgePen.Freeze();
                _edgeGlowPen = new Pen(Mod.Br(Mod.Mix(tone, VoltC, .5), .55), 4) { LineJoin = PenLineJoin.Round }; _edgeGlowPen.Freeze();
            }
            dc.DrawGeometry(SlotGround, null, slot);
            double fx = w * Math.Clamp(_bar, 0, 1);
            if (fx > 1)
            {
                dc.PushClip(slot);
                double ex = .5 + fx;
                // the leading edge cut as a bolt: a fixed zigzag (seeded per member), three px either way
                var zig = Zig(ex, h, fx);
                var shapeG = new StreamGeometry();
                using (var g = shapeG.Open())
                {
                    g.BeginFigure(new Point(.5, .5), true, true);
                    foreach (var p in zig) g.LineTo(p, true, false);
                    g.LineTo(new Point(.5, h - .5), true, false);
                }
                shapeG.Freeze();
                dc.PushClip(shapeG);
                var bar = new Rect(.5, .5, fx + 4, h - 1);
                // the conduit: charging from deep at the left to its full colour at the edge, its windings, a light along its top
                dc.DrawRectangle(_fill, null, bar);
                dc.DrawRectangle(Windings, null, new Rect(.5, 1, Math.Max(0, fx - 4), h - 6));
                dc.DrawRectangle(TopLight, null, bar);
                // the electric core along its foot: a glow and a white-hot line
                double cy = h - 4;
                dc.DrawRectangle(_coreGlow, null, new Rect(1, cy - 2.5, Math.Max(0, fx + 3), 5));
                dc.DrawRectangle(_core, null, new Rect(1, cy - 1, Math.Max(0, fx + 3), 2));
                // charge building into the edge
                dc.DrawRectangle(_edgeGlow, null, new Rect(Math.Max(.5, ex - 16), .5, Math.Min(19, fx + 3), h - 1));
                dc.Pop();
                if (fx > 8)
                {
                    // the bolt edge itself: a glow and a white-hot line along the zigzag
                    var edgeLine = new StreamGeometry();
                    using (var g = edgeLine.Open()) { g.BeginFigure(zig[0], false, false); for (int i = 1; i < zig.Count; i++) g.LineTo(zig[i], true, true); }
                    edgeLine.Freeze();
                    dc.DrawGeometry(null, _edgeGlowPen, edgeLine);
                    dc.DrawGeometry(null, _edgePen, edgeLine);
                }
                dc.Pop();
            }
            if (m.You || (ParserRows.HealBarsPref && m.HealShare > 0))
            {
                dc.PushClip(slot);
                if (m.You) dc.DrawRectangle(Arc, null, new Rect(0, 0, 2.5, h));
                // a healer's share of the group's healing as a mint line along the slot's foot (Settings' heal bars)
                if (ParserRows.HealBarsPref && m.HealShare > 0)
                    dc.DrawRectangle(HealLine, null, new Rect(1, h - 2.5, Math.Max(3, w * Math.Clamp(m.HealShare, 0, 1)), 2));
                dc.Pop();
            }
            dc.DrawGeometry(null, m.You ? YouRim : Rim, slot);
            if (fx > 6 && !m.CpOnly)
            {
                double ex = Math.Min(.5 + fx, s.Width - 3);
                bool lead = Lead;
                int dir = ex > s.Width - 16 ? -1 : 1;
                double healW = _heal.Visibility == Visibility.Visible ? _heal.ActualWidth + _heal.Margin.Left : 0;
                bool sparks = ex < s.Width - _scrimW - healW - 10;
                var key = $"{lead}|{dir}|{tone}|{h}|{sparks}";
                if (_crown == null || key != _crownKey)
                {
                    _crownKey = key;
                    var g = new DrawingGroup();
                    using (var gc = g.Open()) Crown(gc, h, dir, lead, tone, (int)(Math.Abs(m.Id) % 100003) + 17, sparks);
                    g.Freeze();
                    _crown = g;
                }
                dc.PushTransform(new TranslateTransform(ex, 0));
                dc.DrawDrawing(_crown);
                dc.Pop();
            }
            // the figures' column: one even night scrim behind the DPS and the share on every row (over the bar and its sparks)
            if (_scrimW > 0)
            {
                double x0 = Math.Max(6, s.Width - _scrimW);
                dc.PushClip(slot);
                dc.DrawRectangle(ScrimIn, null, new Rect(x0 - 6, 0, 6, h));
                dc.DrawRectangle(ScrimFill, null, new Rect(x0, 0, s.Width - x0, h));
                dc.DrawRectangle(ScrimEdge, null, new Rect(x0, 2, 1, h - 4));
                dc.Pop();
            }
        }

        /// <summary>
        /// The static spark crown, drawn at the edge (x 0): seeded sparks leaping off its top and foot corners out into the gaps
        /// between rows (never across the words), a glow on each corner; the lead's brighter, with longer arcs running back
        /// along both gaps.
        /// </summary>
        static void Crown(DrawingContext dc, double h, int dir, bool lead, Color tone, int seed, bool sparks = true)
        {
            double a = lead ? 1 : .8, k = lead ? 1.2 : 1, sx = dir > 0 ? 5 * k : -2;
            var glow = Mod.Mix(tone, VoltC, .6);
            if (sparks)
            {
                DrawBolt(dc, BoltPts(new Point(0, 2.5), new Point(sx, -2.8), seed, 1.8, 3), a, lead ? .62 : .5, glow);
                DrawBolt(dc, BoltPts(new Point(0, h - 2.5), new Point(sx * .8, h + 2.8), seed + 7, 1.6, 3), a, lead ? .62 : .5, glow);
            }
            if (lead)
            {
                DrawBolt(dc, BoltPts(new Point(-1, -.5), new Point(-34, -1.4), seed + 3, 1.4, 4), .95, .55);
                DrawBolt(dc, BoltPts(new Point(-1, h + .5), new Point(-20, h + 1.4), seed + 5, 1.2, 3), .8, .5);
            }
            if (!sparks) return;
            var g = new RadialGradientBrush(Mod.A(ArcC, lead ? .75 : .5), Mod.A(VoltC, 0));
            g.Freeze();
            dc.DrawEllipse(g, null, new Point(0, 1), lead ? 7 : 5, 3.5);
            dc.DrawEllipse(g, null, new Point(0, h - 1), lead ? 6 : 4.5, 3);
        }
    }
}
