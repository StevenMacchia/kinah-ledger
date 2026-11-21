using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using KinahLedger.Parser;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

sealed partial class HudBroadcast
{
    /// <summary>A name of up to this many characters always shows whole (the crowding rule's floor).</summary>
    const int NameKeep = 10;

    /// <summary>A text's width in a block's own face, size, weight and stretch (a figure column sized for its widest form).</summary>
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

    /// <summary>The fight's foot: one slanted plate, "TOTAL 622.4M" at its left, the group's word and figure at its right, the AVG mark on a held average.</summary>
    FrameworkElement Foot(Encounter e, DateTime now)
    {
        var (total, word, group, held) = FootOf(e, now);
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        TextBlock Pair(string w, string fig)
        {
            var t = new TextBlock { FontFamily = Cond, FontStretch = FontStretches.Condensed, FontSize = 11.5, FontWeight = FontWeights.SemiBold, Foreground = Frozen(Mod.A(CyanC, .9)), VerticalAlignment = VerticalAlignment.Center };
            System.Windows.Documents.Typography.SetNumeralAlignment(t, FontNumeralAlignment.Tabular);
            t.Inlines.Add(new System.Windows.Documents.Run(w.ToUpper(CultureInfo.CurrentCulture) + "  "));
            t.Inlines.Add(new System.Windows.Documents.Run(fig) { FontWeight = FontWeights.Bold, Foreground = White, FontSize = 14 });
            return t;
        }
        var tp = Pair(L.T("Total"), total);
        EnergyMark.After(tp, 12, Frozen(Mod.A(Colors.White, .72)));   // Odyle Energy (Ui/EnergyMark.cs)
        g.Children.Add(tp);
        var right = new StackPanel { Orientation = Orientation.Horizontal };
        if (word.Length > 0) right.Children.Add(Pair(word, group));
        if (held)
        {
            var avg = Mark(L.T("AVG"), size: 10); avg.Tag = "avg-badge"; avg.ToolTip = L.T(Encounter.AvgBadge); avg.Margin = new Thickness(5, 0, 0, 0);
            right.Children.Add(avg);
        }
        Grid.SetColumn(right, 1); g.Children.Add(right);
        var foot = new Slab(Shaded(g), InkC, .9, CyanC, 22, padX: 6) { Margin = new Thickness(0, 4, 0, 0), Shadow = 2.5 };
        _footSlab = foot;
        return AsFoot(foot, e, now);
    }

    /// <summary>The line between the party and you pinned below it: "2 MORE", small condensed caps.</summary>
    static FrameworkElement GapLine(int left)
    {
        var t = Bc(left > 0 ? L.F("{0} more", left).ToUpper(CultureInfo.CurrentCulture) : "· · ·", 10.5, Frozen(Mod.A(Colors.White, .86)), FontWeights.SemiBold);
        t.HorizontalAlignment = HorizontalAlignment.Center;
        var lit = Lit(t);
        lit.HorizontalAlignment = HorizontalAlignment.Center;
        return lit;
    }

    /// <summary>
    /// One member as a broadcast's player plate, three parts on the package's slant: the rank in its own chip (the leader's
    /// cyan, with a crown); the plate, whose fill is the bar (the class's colour from deep at the left to full at a hard angled
    /// leading edge with three speed lines behind it, the member's part of the top), the cyan bar on its left edge (white for
    /// you), the class's art, the name in condensed white, CP small and the marks (★ DPS, the heal mark, deaths, YOU); the
    /// score tile at the right with the DPS big and the share. A healer's healing a second sits at the plate's end in mint;
    /// Settings' heal bars run a mint line along its foot. Crowding (Mod.Fit): the CP goes first, then the share; a name
    /// keeps at least ten characters; a figure is never cut.
    ///
    /// Drawn once per change of its figures; the bar slides by moving its fill under the plate's clip, the build-in is a
    /// slanted clip slid in, the stinger a band slid across, the crown a cyan chip wiped in: transforms and opacities only.
    /// </summary>
    sealed class BcRow : IThemeRow, IThemeBarSpan
    {
        public const double H = 26, Pitch = 29;
        const double ChipW = 31, ChipGap = 3, Lead = 8, IconW = 17, CutW = 14, Ln = H * Tan;
        readonly Grid _root = new() { Height = H };
        readonly Grid _layers = new() { IsHitTestVisible = false }, _fillHost = new() { IsHitTestVisible = false };
        readonly TacSketch _base, _fill, _over, _crown;
        readonly TranslateTransform _fillX = new();
        readonly ScaleTransform _crownPunch = new(1, 1);
        readonly Wiper _enter, _crownWipe;
        readonly Sweep _sweep = new(Ln);
        readonly Grid _words = new();
        readonly TextBlock _rank = Bc("", 15, White), _name = Bc("", 15, White, FontWeights.SemiBold), _cp = Bc("", 11, Frozen(Mod.A(Colors.White, .7)), FontWeights.SemiBold),
            _heal = Bc("", 12.5, Mint2), _dps = Bc("", 19, White), _share = Bc("", 12.5, Frozen(Color.FromRgb(0xC4, 0xD2, 0xE2)), FontWeights.SemiBold),
            _dmg = Bc("", 12.5, Soft, FontWeights.SemiBold), _crit = Bc("", 12, Soft, FontWeights.SemiBold);
        readonly StackPanel _line = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        readonly Grid _nameTag = new() { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(-4, 0, 0, 0) };
        const double TagPad = 4;
        static readonly Brush NameTagB = Mod.Grad(false, (Mod.A(InkC, .62), 0), (Mod.A(InkC, .5), .8), (Mod.A(InkC, 0), 1));
        readonly Border _iconHost = new() { Width = IconW, Height = IconW, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
        readonly List<FrameworkElement> _marks = new();
        readonly double _dpsW, _shareW, _dmgW, _critW;
        string _cls = "\0";
        MeterMember? _m;
        double _bar, _bodyW = -1, _bodyR = -1, _w = -1;
        Color _fillC;
        string _drawn = "";
        int _lastRank;
        bool _expanded, _shareOn = true;

        public bool Leader => _m is { Rank: 1 };

        public FrameworkElement Root => _root;

        /// <summary>Where a full bar is drawn (the PB ghost's track, IThemeBarSpan): the plate from its left foot to the score tile, in the row's base layer.</summary>
        public (FrameworkElement On, Rect Track)? BarTrack => _base.ActualWidth <= 0 || _bodyW <= 0 ? null : (_base, new Rect(ChipW + ChipGap - Ln, 0, _bodyW, _base.ActualHeight));

        public BcRow()
        {
            _dps.Tag = "row-dps"; _share.Tag = "row-share"; _heal.Tag = "heal-figures"; _cp.Tag = "cp";
            _dps.HorizontalAlignment = _share.HorizontalAlignment = _dmg.HorizontalAlignment = _crit.HorizontalAlignment = HorizontalAlignment.Right;
            _share.Margin = new Thickness(0, 3, 0, 0); _dmg.Margin = new Thickness(0, 1, 0, 0); _crit.Margin = new Thickness(0, 1, 0, 0);
            _heal.Margin = new Thickness(6, 1, 9, 0);
            // the columns: the chip, the plate's lead, the class art, a gap, the name's line (the rest), heal, the cut to the
            // score tile, the DPS, the share; expanded, the damage and the crit rate; the tile's end
            _dpsW = FigCol(_dps, "888.8K") + 2; _shareW = FigCol(_share, "88.8%", "100%", L.T("CP")) + 6;
            _dmgW = FigCol(_dmg, "888.88M") + 8; _critW = FigCol(_crit, "88%") + 8;
            foreach (var w in new[] { ChipW, Lead, IconW, 6, double.NaN, 0, CutW, _dpsW, _shareW, 0, 0, Ln / 2 + 6 })
                _words.ColumnDefinitions.Add(new ColumnDefinition { Width = double.IsNaN(w) ? new GridLength(1, GridUnitType.Star) : w == 0 ? GridLength.Auto : new GridLength(w) });
            _words.ColumnDefinitions[9].Width = new GridLength(0); _words.ColumnDefinitions[10].Width = new GridLength(0);
            // the drawn layers: the chip, plate and tile; the fill under the plate's clip; the edge and the lines over it; the crown's chip
            _base = new TacSketch(DrawBase);
            _fill = new TacSketch(DrawFill) { RenderTransform = _fillX };
            _over = new TacSketch(DrawOver);
            _crown = new TacSketch(DrawCrown) { RenderTransform = _crownPunch, Visibility = Visibility.Collapsed };
            _fillHost.Children.Add(_fill);
            _layers.Children.Add(_base); _layers.Children.Add(_fillHost); _layers.Children.Add(_over); _layers.Children.Add(_crown);
            _root.Children.Add(_layers);
            _enter = new Wiper(_layers, Ln);
            _crownWipe = new Wiper(_crown, Ln);
            _crownPunch.CenterX = ChipW / 2; _crownPunch.CenterY = H / 2;
            _rank.HorizontalAlignment = HorizontalAlignment.Center;
            Put(_rank, 0); Put(_iconHost, 2); Put(_nameTag, 4); Put(_heal, 5); Put(_dps, 7); Put(_share, 8); Put(_dmg, 9); Put(_crit, 10);
            _name.TextTrimming = TextTrimming.CharacterEllipsis;
            _cp.Margin = new Thickness(5, 2, 0, 0);
            _line.Children.Add(_name); _line.Children.Add(_cp);
            // the name's tag: a slanted navy backing under the name, CP and marks, so they read on any class's fill
            _line.Margin = new Thickness(TagPad, 0, TagPad + 2, 0);
            _nameTag.Children.Add(new TacSketch((dc, s) =>
            {
                if (s.Width < 8) return;
                double h = 19, y = (s.Height - h) / 2;
                dc.DrawGeometry(NameTagB, null, Cut(new Rect(-2, y, s.Width + 2, h), h * Tan));
            }));
            _nameTag.Children.Add(_line);
            _root.Children.Add(Shaded(_words));
            _sweep.Shape = s => { var p = Parts(s); var g = new GeometryGroup { Children = { p.Chip, p.Plate } }; return g; };
            _root.Children.Add(_sweep);
            _root.SizeChanged += (_, _) => { Fit(); Shape(); };
        }

        void Put(FrameworkElement el, int col) { Grid.SetColumn(el, col); _words.Children.Add(el); }

        /// <summary>The build-in: the plates on their slant so far (0 none, 1 all), the words fading in after them.</summary>
        public double Enter { set { _enter.At(value); _words.Opacity = Seg(value, .45, 1); } }

        /// <summary>The stinger across the row (-1: none).</summary>
        public void SweepAt(double p, Color c, double strength = .9) => _sweep.At(p, c, strength);

        /// <summary>The crown landing on the leader's chip: the cyan wiping in (to .5), then the chip punching in (1: landed).</summary>
        /// <summary>A pulse of the leader's chip (0 to 1: up and back), the crown kept on it.</summary>
        public double Pulse { set { _crownWipe.At(1); _crownPunch.ScaleX = _crownPunch.ScaleY = 1 + .28 * Math.Sin(Math.PI * Math.Clamp(value, 0, 1)); } }

        public double Crown
        {
            set
            {
                _crownWipe.At(Out(Seg(value, 0, .5)));
                double k = value >= 1 ? 1 : Back(Seg(value, .45, 1), 3);
                _crownPunch.ScaleX = _crownPunch.ScaleY = value < .45 ? 1 : .82 + .18 * k;
            }
        }

        public void Set(MeterMember m, MeterContext c, bool fresh)
        {
            bool climbed = !fresh && _lastRank > 0 && m.Rank < _lastRank && c.Moves;
            _lastRank = m.Rank;
            _m = m;
            if (_expanded != c.Expanded)
            {
                _expanded = c.Expanded;
                _words.ColumnDefinitions[9].Width = new GridLength(c.Expanded ? _dmgW : 0);
                _words.ColumnDefinitions[10].Width = new GridLength(c.Expanded ? _critW : 0);
            }
            _rank.Text = m.Rank.ToString(CultureInfo.InvariantCulture);
            // the leader's digit sits under the crown, in ink on the cyan
            bool lead = m.Rank == 1;
            _rank.Margin = new Thickness(lead ? 2 : 0, lead ? 9 : 0, 0, 0);
            _rank.FontSize = lead ? 14 : m.Rank >= 10 ? 13 : 15;
            _rank.Foreground = lead ? InkB : White;
            _crown.Visibility = lead ? Visibility.Visible : Visibility.Collapsed;
            if (m.Class != _cls)
            {
                _cls = m.Class;
                var icon = ThemeText.Icon(m.Class, IconW, m.Name);
                if (icon is Border b) { b.BorderBrush = Frozen(Mod.A(Colors.White, .8)); b.BorderThickness = new Thickness(1); }
                _iconHost.Child = icon;
            }
            _name.Text = m.Name;
            _cp.Text = m.Cp;
            // the marks after the name: ★ DPS, a healer's ✚, deaths, YOU (kept whole: the name and the CP give way first)
            foreach (var mk in _marks) _line.Children.Remove(mk);
            _marks.Clear();
            void Add(FrameworkElement el) { el.Margin = new Thickness(4, 0, 0, 0); _line.Children.Add(el); _marks.Add(el); }
            if (m.TopDps) { var t = Mark("★ " + L.T("DPS")); t.Tag = "top-dps"; t.ToolTip = L.T("Top DPS"); Add(t); }
            if (m.TopHeals) { var t = Mark("✚", MintC2); t.Tag = "top-heals"; t.ToolTip = L.T("Top heals"); Add(t); }
            if (m.Deaths > 0) { var d = Bc($"✕{m.Deaths}", 12, Coral); d.Tag = "deaths"; d.ToolTip = L.F(m.Deaths == 1 ? "{0} death" : "{0} deaths", m.Deaths); Add(d); }
            if (m.You) { var y = Mark(L.T("YOU"), filled: true); y.Tag = "you"; Add(y); }
            _heal.Text = m.Heal.Length > 0 ? "+" + m.Heal : "";
            _heal.Visibility = m.Heal.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            _dps.Text = m.Figure;
            _dps.FontSize = m.CpOnly ? 16 : 19;
            // a wider figure (Combat Power before the first hit, "167,548") widens its column: never a digit cut
            _words.ColumnDefinitions[7].Width = new GridLength(Math.Max(_dpsW, TextW(_dps, m.Figure) + 2));
            _share.Text = m.ShareText;
            _dmg.Text = c.Expanded ? m.Damage : "";
            _crit.Text = c.Expanded ? m.Crit : "";
            Fit();
            if (Vivid(m.ClassColor) is var vc && vc != _fillC) { _fillC = vc; _fill.InvalidateVisual(); }
            Shape();
            // the drawn layers again only when what they show moved (most ticks change a figure, not a plate)
            var sig = $"{m.You}|{Math.Round(m.HealShare, 3)}|{ParserRows.HealBarsPref}|{_bodyR:0.#}|{_w:0.#}|{Ground:0.###}";
            if (sig != _drawn) { _drawn = sig; _base.InvalidateVisual(); _over.InvalidateVisual(); }
            SlideBar(_fill, c, "bar", BarOf(m, c), v => { _bar = v; PlaceFill(); });
            // a row that climbed: a quick white light wipes across it on the slant
            if (climbed)
                Motion.Play(_sweep, 520, t => _sweep.At(Out(t), Colors.White, 1 - Seg(t, .7, 1)), () => _sweep.At(-1, Colors.White));
        }

        /// <summary>The crowding rule (Mod.Fit): the CP goes first, then the share; the name keeps at least ten characters.</summary>
        void Fit()
        {
            double total = _root.ActualWidth;
            if (total <= 0) return;
            var cols = _words.ColumnDefinitions;
            double heal = _heal.Visibility == Visibility.Visible ? Mod.W(_heal) + _heal.Margin.Left : 0;
            double room = total - cols[0].Width.Value - cols[1].Width.Value - cols[2].Width.Value - cols[3].Width.Value - heal - cols[6].Width.Value
                - cols[7].Width.Value - cols[9].Width.Value - cols[10].Width.Value - cols[11].Width.Value - 2;
            double min = _name.Text.Length <= NameKeep ? TextW(_name, _name.Text) : TextW(_name, _name.Text[..NameKeep] + "…") + 1;
            var (cp, share) = Mod.Fit(_name, room - 2 * TagPad - 2 + 4, _cp.Text.Length > 0 ? _cp : null, _marks, _shareW, min);
            _cp.Visibility = cp ? Visibility.Visible : Visibility.Collapsed;
            if (share != _shareOn || cols[8].Width.Value != (share ? _shareW : 0))
            {
                _shareOn = share;
                cols[8].Width = new GridLength(share ? _shareW : 0);
            }
            _share.Visibility = share ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>The plate's right end at its top: the score tile's columns from the row's right, worked out from the widths set (no layout needed).</summary>
        double BodyRight(double w)
        {
            var cols = _words.ColumnDefinitions;
            double tile = CutW + cols[7].Width.Value + cols[8].Width.Value + cols[9].Width.Value + cols[10].Width.Value + cols[11].Width.Value;
            return w - tile + 4 + Ln / 2;
        }

        /// <summary>The row's three parts at width <paramref name="s"/>: the chip, the plate (its rect and shape) and the tile.</summary>
        (Geometry Chip, Rect Body, Geometry Plate, Geometry Tile) Parts(Size s)
        {
            double bx = ChipW + ChipGap - Ln, br = BodyRight(s.Width), tx = br + 2 - Ln;
            var body = new Rect(bx, 0, Math.Max(1, br - bx), s.Height);
            return (Cut(new Rect(0, 0, ChipW, s.Height), Ln), body, Cut(body, Ln), Cut(new Rect(tx, 0, Math.Max(1, s.Width - tx), s.Height), Ln));
        }

        /// <summary>The plate's shape set again where it moved (a width, a column given or taken): the fill's clip and the stinger's.</summary>
        void Shape()
        {
            var s = _root.RenderSize;
            if (s.Width < 60) return;
            double br = BodyRight(s.Width);
            if (Math.Abs(br - _bodyR) < .1 && Math.Abs(s.Width - _w) < .1) return;
            _bodyR = br; _w = s.Width;
            var p = Parts(s);
            _bodyW = p.Body.Width;
            _fillHost.Clip = p.Plate;
            _fill.InvalidateVisual();
            _sweep.Refit();
            PlaceFill();
        }

        /// <summary>The bar: the fill, drawn the plate's whole length, slid left by what the member's part leaves out.</summary>
        void PlaceFill() { if (_bodyW > 0) _fillX.X = -(1 - Math.Clamp(_bar, 0, 1)) * (_bodyW + Ln); }

        /// <summary>The chip, the plate's navy and the score tile (drawn when the figures change, never by a moment).</summary>
        void DrawBase(DrawingContext dc, Size s)
        {
            if (_m == null || s.Width < 60) return;
            var (chip, _, plate, tile) = Parts(s);
            double g = Ground;
            // the depth: a hard offset shadow under the three plates
            dc.PushTransform(new TranslateTransform(2.5, 2.5));
            dc.DrawGeometry(ShadowB, null, chip); dc.DrawGeometry(ShadowB, null, plate); dc.DrawGeometry(ShadowB, null, tile);
            dc.Pop();
            dc.DrawGeometry(Mod.Grad(true, (Mod.A(Ink2C, .96 * g), 0), (Mod.A(InkC, .96 * g), 1)), null, chip);
            dc.DrawGeometry(Mod.Grad(true, (Mod.A(Color.FromRgb(0x10, 0x18, 0x2D), .9 * g), 0), (Mod.A(InkC, .9 * g), .6)), null, plate);
            dc.DrawGeometry(Mod.Grad(true, (Mod.A(Color.FromRgb(0x1A, 0x25, 0x44), .97 * g), 0), (Mod.A(Ink2C, .97 * g), .55), (Mod.A(InkC, .97 * g), 1)), null, tile);
            dc.DrawGeometry(GlossB, null, chip); dc.DrawGeometry(GlossB, null, tile);
            dc.PushClip(tile);
            dc.DrawRectangle(Frozen(Mod.A(Colors.White, .2)), null, new Rect(0, 0, s.Width, 1));
            dc.Pop();
        }

        /// <summary>
        /// The bar's fill over the plate's whole length (slid into place by its transform): the class's colour from deep to full,
        /// a gloss on its top, three speed lines fading behind the hard leading edge, the edge's light.
        /// </summary>
        void DrawFill(DrawingContext dc, Size s)
        {
            if (_m == null || _bodyW <= 0) return;
            var c = _fillC;
            double bx = ChipW + ChipGap - Ln, w = _bodyW + Ln;
            var fr = new Rect(bx, 0, w, s.Height);
            var fill = new LinearGradientBrush { MappingMode = BrushMappingMode.Absolute, StartPoint = new Point(bx, 0), EndPoint = new Point(bx + w, 0) };
            fill.GradientStops.Add(new GradientStop(Mod.A(Mod.Mix(c, InkC, .55), .92), 0));
            fill.GradientStops.Add(new GradientStop(Mod.A(Mod.Mix(c, InkC, .3), .92), .55));
            fill.GradientStops.Add(new GradientStop(Mod.A(c, .96), 1));
            fill.Freeze();
            var fg = Cut(fr, Ln);
            dc.DrawGeometry(fill, null, fg);
            // a darker lower half under the gloss: the fill reads as a lit slab, not a flat wash
            dc.DrawGeometry(Mod.Grad(true, (Mod.A(Colors.Black, 0), .5), (Mod.A(Colors.Black, .26), 1)), null, fg);
            dc.PushClip(fg);
            dc.DrawRectangle(Mod.Grad(true, (Mod.A(Colors.White, .16), 0), (Mod.A(Colors.White, .03), .45), (Mod.A(Colors.White, 0), .5)), null, fr);
            double ex = bx + w;
            // the speed lines: three thin slanted strokes behind the edge, fading back
            for (int k = 0; k < 3; k++)
            {
                double x = ex - 13 - k * 6;
                dc.DrawGeometry(Frozen(Mod.A(Colors.White, .26 - k * .08)), null, Cut(new Rect(x - Ln, 0, 1.6 + Ln, s.Height), Ln));
            }
            // the leading edge: a 6 px glow into a 2 px white line
            dc.DrawGeometry(Mod.Grad(false, (Mod.A(Mod.Mix(c, Colors.White, .5), 0), 0), (Mod.A(Mod.Mix(c, Colors.White, .6), .85), 1)), null, Cut(new Rect(ex - 8 - Ln, 0, 6 + Ln, s.Height), Ln));
            dc.DrawGeometry(White, null, Cut(new Rect(ex - 2 - Ln, 0, 2 + Ln, s.Height), Ln));
            dc.Pop();
        }

        /// <summary>Over the fill: the plate's hairline of light, the heal line, the bar on its left edge (white for you), your outline.</summary>
        void DrawOver(DrawingContext dc, Size s)
        {
            if (_m is not { } m || s.Width < 60) return;
            var (_, body, plate, _) = Parts(s);
            dc.PushClip(plate);
            // a healer's share of the group's healing: a mint line along the plate's foot (Settings' heal bars)
            if (ParserRows.HealBarsPref && m.HealShare > 0)
                dc.DrawRectangle(Frozen(Mod.A(MintC2, .95)), null, new Rect(body.X, s.Height - 2.5, Math.Max(4, body.Width * Math.Clamp(m.HealShare, 0, 1)), 2.5));
            dc.DrawRectangle(Frozen(Mod.A(Colors.White, .14)), null, new Rect(body.X, 0, body.Width + Ln, 1));
            dc.Pop();
            dc.DrawGeometry(m.You ? White : Cyan, null, Cut(new Rect(body.X, 0, 3.5 + Ln, s.Height), Ln));
            if (m.You) dc.DrawGeometry(null, Mod.P(Frozen(Mod.A(Colors.White, .55)), 1), plate);
        }

        /// <summary>The leader's chip: cyan, and a small three-pointed crown in ink over the digit.</summary>
        void DrawCrown(DrawingContext dc, Size s)
        {
            var chip = Cut(new Rect(0, 0, ChipW, s.Height), Ln);
            dc.DrawGeometry(Mod.Grad(true, (Mod.Mix(CyanC, Colors.White, .35), 0), (CyanC, .5), (Mod.Mix(CyanC, InkC, .15), 1)), null, chip);
            double cx = ChipW / 2 + Ln * .3, cy = 9.2, w = 15, hh = 7.4;
            var crown = new StreamGeometry();
            using (var x = crown.Open())
            {
                x.BeginFigure(new Point(cx - w / 2, cy), true, true);
                x.LineTo(new Point(cx - w / 2, cy - hh * .55), true, false);
                x.LineTo(new Point(cx - w / 4, cy - hh * .2), true, false);
                x.LineTo(new Point(cx, cy - hh), true, false);
                x.LineTo(new Point(cx + w / 4, cy - hh * .2), true, false);
                x.LineTo(new Point(cx + w / 2, cy - hh * .55), true, false);
                x.LineTo(new Point(cx + w / 2, cy), true, false);
            }
            crown.Freeze();
            dc.DrawGeometry(InkB, null, crown);
        }
    }
}
