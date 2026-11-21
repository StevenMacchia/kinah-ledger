using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using KinahLedger.Parser;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

sealed partial class HudFrost
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

    /// <summary>A member's bar, 0 to 1, against the fight's top (none by Combat Power before any damage).</summary>
    static double BarOf(MeterMember m, MeterContext c) => m.CpOnly ? 0 : c.E.BarOf(m.F);

    /// <summary>The fight's foot under its frost line: "Total 622.4M" at the left, the group's word and figure at the right, the AVG chip on a held average.</summary>
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
        var right = new StackPanel { Orientation = Orientation.Horizontal };
        if (word.Length > 0) right.Children.Add(Pair(word, group));
        if (held)
        {
            var avg = Chip(L.T("AVG"), size: 8.5); avg.Tag = "avg-badge"; avg.ToolTip = L.T(Encounter.AvgBadge); avg.Margin = new Thickness(5, 0, 0, 0);
            right.Children.Add(avg);
        }
        Grid.SetColumn(right, 1); g.Children.Add(right);
        return AsFoot(g, e, now);
    }

    /// <summary>The line between the party and you pinned below it: "2 MORE" small between two frost lines.</summary>
    static FrameworkElement GapLine(int left)
    {
        var g = new Grid { VerticalAlignment = VerticalAlignment.Center };
        g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); g.ColumnDefinitions.Add(new ColumnDefinition());
        var t = Tt(left > 0 ? L.F("{0} more", left).ToUpperInvariant() : "·", 8.5, Quiet, FontWeights.SemiBold);
        t.Margin = new Thickness(8, 0, 8, 0);
        Grid.SetColumn(t, 1); g.Children.Add(t);
        for (int i = 0; i < 2; i++)
        {
            bool l = i == 0;
            var line = new TacSketch((dc, s) => dc.DrawRectangle(Mod.Grad(false, (Mod.A(RimC, l ? 0 : .35), 0), (Mod.A(RimC, l ? .35 : 0), 1)), null, new Rect(0, Math.Round(s.Height / 2), s.Width, 1)));
            Grid.SetColumn(line, l ? 0 : 2); g.Children.Add(line);
        }
        return g;
    }

    /// <summary>The meter's rows now, top first (for a moment that runs over them all).</summary>
    List<FrostRow> RowsNow() => Meter?.Members.Select(m => Meter.RowOf(m.Id)).OfType<FrostRow>().ToList() ?? new();

    /// <summary>
    /// One member as a bar of ice on a slab of dark ice (a shard: cut at its top-left and bottom-right). The bar is translucent
    /// crystal in a cool tint of the member's class colour, as long as their part of the top: crystal planes through it,
    /// hoarfrost along its top edge, a slanted icy-white leading edge with a cold glow behind it; the leader's the clearest and
    /// brightest with a glint at its tip. On it, in white with a tight dark shade: the rank (the top row wears the snowflake
    /// crown in its place), the class's art in a ring of ice, the name, Combat Power small, the marks (★ DPS, ✚, deaths, YOU),
    /// a healer's healing a second in mint, the DPS big in ice-white with a faint cyan halo, the share small at the right. You:
    /// an ice-white spine at the slab's left and a lit rim. Short of room the CP goes first, then the share; a name keeps
    /// at least ten characters; a figure is never cut (Mod.Fit).
    /// Its moments: the frost creeps along the bar anew and a cold glint follows (<see cref="SetFrost"/>), the crown
    /// crystallises (<see cref="Crystallise"/>) when the row takes the lead, and a row that climbs leaves a frost trail.
    /// </summary>
    sealed class FrostRow : IThemeRow, IThemeBarSpan
    {
        public const double H = 26, Pitch = 29;
        const double Pad = 7;
        readonly HudFrost _look;
        readonly Grid _root = new() { Height = H };
        readonly TacSketch _art, _rime;
        readonly Grid _words = new() { Margin = new Thickness(Pad, 0, Pad, 0) };
        readonly Grid _rankHost = new() { Width = 19, Height = 19, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
        readonly TextBlock _rank = Tt("", 12, White, FontWeights.Bold), _name = Tt("", 13, White, FontWeights.Bold), _cp = Tt("", 9.5, Frozen(Mod.A(Colors.White, .74)), FontWeights.SemiBold),
            _heal = Tt("", 11, Mint2, FontWeights.Bold), _dps = Tt("", 16.5, White, FontWeights.Bold), _share = Tt("", 10.5, Soft, FontWeights.SemiBold),
            _dmg = Tt("", 11, Soft, FontWeights.SemiBold), _crit = Tt("", 10.5, Quiet, FontWeights.SemiBold);
        readonly StackPanel _line = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        readonly Border _iconHost = new() { Width = 18, Height = 18, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
        readonly List<FrameworkElement> _marks = new();
        readonly double _dpsW, _shareW, _dmgW, _critW;
        string _cls = "\0", _sig = "";
        MeterMember? _m;
        double _bar, _frost = -1;
        int _lastRank;
        bool _expanded, _shareOn = true;

        // the crown, in parts its moment grows one after another (painted once: HudFrost.Ice.cs)
        readonly Grid _crown = new() { Visibility = Visibility.Collapsed, IsHitTestVisible = false };
        readonly Border _flakeGlow = Piece(FlakeGlow, 29, 29, .4), _arms = Piece(FlakeArms, 21, 21), _branches = Piece(FlakeBranches, 21, 21), _ring = Piece(FrostRing, 32, 32, 0), _tip = Piece(SparkleBrush, 10, 10, 0),
            _bloom = Piece(FlakeBloom, 66, 66, 0), _bloomHot = Piece(FlakeBloomHot, 66, 66, 0);
        readonly ScaleTransform _armsK = new(1, 1), _branchesK = new(1, 1), _ringK = new(1, 1), _bloomK = new(1, 1);
        readonly RotateTransform _bloomTurn = new();
        readonly RotateTransform _turn = new();

        // the frost creeping along the bar, made on its first course: the rime's reveal, the veil at its front, the glint, two motes
        Canvas? _fx;
        RectangleGeometry? _fxClip;
        readonly RectangleGeometry _rimeClip = new();
        readonly ScaleTransform _rimeK = new(1, 1);
        Border? _veil, _front, _mote1, _mote2;
        System.Windows.Shapes.Path? _blade;
        // the climb's rim flash and frost trail, made on the first climb
        TacSketch? _climbRim;
        Border? _climbTrail;

        public FrameworkElement Root => _root;

        public FrostRow(HudFrost look)
        {
            _look = look;
            _dps.Tag = "row-dps"; _share.Tag = "row-share"; _heal.Tag = "heal-figures"; _cp.Tag = "cp";
            _dps.HorizontalAlignment = _share.HorizontalAlignment = _dmg.HorizontalAlignment = _crit.HorizontalAlignment = HorizontalAlignment.Right;
            _share.Margin = new Thickness(0, 2, 0, 0); _dmg.Margin = new Thickness(0, 1, 0, 0); _crit.Margin = new Thickness(0, 1, 0, 0);
            _heal.Margin = new Thickness(6, 1, 0, 0);
            // the columns: rank, the class art, the name's line (the rest), heal, the DPS, the share; expanded, the damage and the crit rate
            _dpsW = FigCol(_dps, "888.8K") + 4; _shareW = FigCol(_share, "88.8%", "100%", L.T("CP")) + 6;
            _dmgW = FigCol(_dmg, "888.88M") + 8; _critW = FigCol(_crit, "88%") + 8;
            foreach (var w in new[] { 21.0, 25, double.NaN, 0, _dpsW, _shareW, 0, 0 })
                _words.ColumnDefinitions.Add(new ColumnDefinition { Width = double.IsNaN(w) ? new GridLength(1, GridUnitType.Star) : w == 0 ? GridLength.Auto : new GridLength(w) });
            _words.ColumnDefinitions[6].Width = new GridLength(0); _words.ColumnDefinitions[7].Width = new GridLength(0);
            _art = new TacSketch(Draw);
            _rime = new TacSketch(DrawRime);
            _rimeClip.Transform = _rimeK;
            _root.Children.Add(_art); _root.Children.Add(_rime);
            // the crown's parts overhang the 17 px cell (negative margins: never cut by its layout)
            foreach (var (p, w) in new[] { (_flakeGlow, 29.0), (_ring, 32), (_arms, 21), (_branches, 21), (_bloom, 66), (_bloomHot, 66) }) p.Margin = new Thickness(-(w - 19) / 2);
            _arms.RenderTransform = _armsK; _branches.RenderTransform = _branchesK; _ring.RenderTransform = _ringK;
            _bloom.RenderTransform = new TransformGroup { Children = { _bloomK, _bloomTurn } };
            _bloomHot.RenderTransform = _bloom.RenderTransform; _bloomHot.Visibility = Visibility.Hidden;
            _ring.Visibility = Visibility.Hidden; _tip.Visibility = Visibility.Hidden; _bloom.Visibility = Visibility.Hidden;
            _tip.VerticalAlignment = VerticalAlignment.Top; _tip.Margin = new Thickness(0, -6, 0, 0);
            var flake = new Grid { RenderTransform = _turn, RenderTransformOrigin = new Point(.5, .5), Children = { _arms, _branches } };
            _crown.Children.Add(_bloom); _crown.Children.Add(_bloomHot); _crown.Children.Add(_flakeGlow); _crown.Children.Add(_ring); _crown.Children.Add(flake); _crown.Children.Add(_tip);
            // the heart the numeral sits on: a dark hexagon of deep ice with a lit rim, over the flake's own heart
            var heart = Piece(FlakeHeart, 15, 15); heart.Margin = new Thickness(2);
            _crown.Children.Add(heart);
            _rank.HorizontalAlignment = HorizontalAlignment.Center;
            _rankHost.Children.Add(_crown); _rankHost.Children.Add(_rank);
            Put(_rankHost, 0); Put(_iconHost, 1); Put(_line, 2); Put(_heal, 3); Put(_dps, 4); Put(_share, 5); Put(_dmg, 6); Put(_crit, 7);
            _name.TextTrimming = TextTrimming.CharacterEllipsis;
            _cp.Margin = new Thickness(5, 2, 0, 0);
            _line.Children.Add(_name); _line.Children.Add(_cp);
            _words.Effect = Shade();
            _root.Children.Add(_words);
            _root.SizeChanged += (_, _) => { Fit(); PlaceFrost(); };
            // the plates under the figures and the bar's clear end follow the words' places: drawn again once they are laid out
            foreach (var el in new FrameworkElement[] { _name, _cp, _heal, _dps, _share, _dmg, _crit, _line }) el.SizeChanged += (_, _) => _art.InvalidateVisual();
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
            // the snowflake crown on the top row once there is damage (by Combat Power before it, the rank stays a number)
            bool crown = m.Rank == 1 && !m.CpOnly && !m.Pvp;
            _crown.Visibility = crown ? Visibility.Visible : Visibility.Collapsed;   // the rank's numeral always shows (on the crown's heart at rank 1)
            _rank.FontSize = crown ? 11 : 12;
            if (!fresh && c.Moves && _lastRank > 0)
            {
                if (crown && _lastRank > 1) { Crystallise(); _look.Lead(); }
                else if (m.Rank < _lastRank) Climb();
            }
            _lastRank = m.Rank;
            if (m.Class != _cls)
            {
                _cls = m.Class;
                var icon = ThemeText.Icon(m.Class, 18, m.Name);
                if (icon is Border b) { b.BorderBrush = Frozen(Mod.A(IceC, .85)); b.BorderThickness = new Thickness(1); }
                _iconHost.Child = icon;
            }
            _name.Text = m.Name;
            _cp.Text = m.Cp;
            // the marks after the name: ★ DPS, a healer's ✚, deaths, YOU (kept whole: the name and the CP give way first)
            foreach (var mk in _marks) _line.Children.Remove(mk);
            _marks.Clear();
            void Add(FrameworkElement el) { el.Margin = new Thickness(5, 0, 0, 0); _line.Children.Add(el); _marks.Add(el); }
            if (m.TopDps) { var t = Chip("★ " + L.T("DPS"), White, FrostC); t.Tag = "top-dps"; t.ToolTip = L.T("Top DPS"); Add(t); }
            if (m.TopHeals) { var t = Chip("✚", MarkMint, MintC2); t.Tag = "top-heals"; t.ToolTip = L.T("Top heals"); Add(t); }
            if (m.Deaths > 0) { var d = Tt($"✕{m.Deaths}", 10.5, Coral, FontWeights.Bold); d.Tag = "deaths"; d.ToolTip = L.F(m.Deaths == 1 ? "{0} death" : "{0} deaths", m.Deaths); Add(d); }
            if (m.You) { var y = Chip(L.T("YOU"), filled: true); y.Tag = "you"; Add(y); }
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
            SlideBar(_art, c, "bar", BarOf(m, c), v => { if (v == _bar) return; _bar = v; _art.InvalidateVisual(); _rime.InvalidateVisual(); });
            // the ice is drawn again only when what it shows changed (not on every tick of the meter)
            var sig = $"{m.Rank}|{m.You}|{m.CpOnly}|{m.ClassColor}|{m.HealShare:0.000}|{ParserRows.HealBarsPref}|{m.Figure}|{_shareOn}|{_expanded}";
            if (sig != _sig) { _sig = sig; _art.InvalidateVisual(); _rime.InvalidateVisual(); }
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
            var cpv = cp ? Visibility.Visible : Visibility.Collapsed;
            if (_cp.Visibility != cpv) { _cp.Visibility = cpv; _art.InvalidateVisual(); }
            if (share != _shareOn || cols[5].Width.Value != (share ? _shareW : 0))
            {
                _shareOn = share;
                cols[5].Width = new GridLength(share ? _shareW : 0);
                _art.InvalidateVisual();
            }
            _share.Visibility = share ? Visibility.Visible : Visibility.Collapsed;
        }

        static double Seg(double p, double a, double b) => Math.Clamp((p - a) / (b - a), 0, 1);
        static double Out(double p) => Motion.Out(p);
        static Rect Slab(Size s) => new(.5, .5, Math.Max(1, s.Width - 1), Math.Max(1, s.Height - 1));

        /// <summary>
        /// The bar's full length: to just past the DPS figure, so the share (and the expanded columns) always sit on the dark
        /// slab, clear of the bar's bright icy edge.
        /// </summary>
        double Track(Rect r) => Math.Max(20, r.Width - 2);

        /// <summary>
        /// Whether the bar's bright icy end can be drawn at <paramref name="x"/>: never under a word or figure (4 px of air
        /// round each); there, the bar ends soft instead.
        /// </summary>
        bool EdgeClear(double x)
        {
            foreach (var el in Words())
            {
                if (Box(el) is not { } b) continue;
                if (x > b.Left - 6 && x < b.Right + 6) return false;
            }
            return true;
        }

        /// <summary>The row's words and figures, each on its own (for the bar's end and the plates under the figures).</summary>
        IEnumerable<FrameworkElement> Words()
        {
            yield return _rankHost; yield return _iconHost; yield return _name; yield return _cp;
            foreach (var mk in _marks) yield return mk;
            yield return _heal; yield return _dps; yield return _share; yield return _dmg; yield return _crit;
        }

        /// <summary>An element's box in the row, or null when it isn't shown.</summary>
        Rect? Box(FrameworkElement el)
        {
            if (el.Visibility != Visibility.Visible || el.ActualWidth <= 0 || !el.IsDescendantOf(_root)) return null;
            var p = el.TranslatePoint(new Point(0, 0), _root);
            return new Rect(p.X, p.Y, el.ActualWidth, el.ActualHeight);
        }

        /// <summary>For the PB ghost (Ui/GhostMarks.cs, IThemeBarSpan): the element the bar is drawn in and the rect a full bar fills.</summary>
        public (FrameworkElement On, Rect Track)? BarTrack => _art.ActualWidth <= 0 ? null : (_art, new Rect(.5, .5, Track(Slab(_art.RenderSize)), Math.Max(1, _art.ActualHeight - 1)));

        // ---- the ice (drawn on a change only)

        void Draw(DrawingContext dc, Size s)
        {
            if (_m is not { } m || s.Width < 30) return;
            var r = Slab(s);
            var slab = Shard(r, 6, 1.5);
            bool lead = m.Rank == 1 && !m.CpOnly;
            // the slab of dark ice the bar lies in (the dark ground the words read on)
            if (lead) dc.DrawGeometry(null, LeadHalo, slab);
            dc.DrawGeometry(SlabFill, null, slab);
            double fx = Track(r) * Math.Clamp(_bar, 0, 1);
            dc.PushClip(slab);
            IceBar(dc, r, fx, m.ClassColor, lead, 0, edge: EdgeClear(r.Left + fx), tip: EdgeClear(r.Left + fx));
            // a healer's share of the group's healing as a mint line along the slab's foot (Settings' heal bars)
            if (ParserRows.HealBarsPref && m.HealShare > 0)
                dc.DrawRectangle(HealLine, null, new Rect(r.X, r.Bottom - 2, Math.Max(3, r.Width * Math.Clamp(m.HealShare, 0, 1)), 2));
            // you: an ice-white spine at the left
            if (m.You) dc.DrawRectangle(YouSpine, null, new Rect(r.Left, r.Top, 3, r.Height));
            // the figures that sit on the ice (the CP, the heal rate, the DPS, the share, the expanded columns) each on a dark
            // plate of deep ice with a faint cold rim, so no bright ice lies under a digit
            double barEnd = r.Left + fx + 4;
            foreach (var el in new FrameworkElement[] { _cp, _heal, _dps, _share, _dmg, _crit })
            {
                if (Box(el) is not { } b || b.Left > barEnd) continue;
                var plate = new Rect(b.Left - 3, r.Top + 4, b.Width + 6, r.Height - 8);
                if (el == _dps) plate = new Rect(b.Left - 4, r.Top + 3, b.Width + 8, r.Height - 6);
                dc.DrawGeometry(FigurePlate, FigureRim, Shard(plate, 3, 1));
            }
            dc.Pop();
            dc.DrawGeometry(null, m.You ? YouRim : lead ? LeadRim : RowRim, slab);
        }

        /// <summary>The hoarfrost along the bar's top edge, on its own layer (the frost's creep reveals it, left to right, by its clip's scale).</summary>
        void DrawRime(DrawingContext dc, Size s)
        {
            if (_m is null || s.Width < 30) return;
            var r = Slab(s);
            double fx = Track(r) * Math.Clamp(_bar, 0, 1);
            if (fx < 2) return;
            double sl = Math.Min(3, fx / 2);
            dc.PushClip(new RectangleGeometry(new Rect(r.Left, r.Top, fx + sl, r.Height)));
            // a soft white band of frost along the top, a few larger crystals; the leader's bar the full hoarfrost
            dc.DrawRectangle(RimeBand, null, new Rect(r.Left, r.Top + .5, fx + sl, 3));
            dc.PushTransform(new TranslateTransform(r.Left, r.Top));
            dc.DrawGeometry(null, TuftPen, RimeTufts);
            if (_m.Rank == 1 && !_m.CpOnly) dc.DrawGeometry(null, RimeEdgePen, RimeEdge);
            dc.Pop(); dc.Pop();
        }

        // ---- the moments (transforms and opacities only)

        /// <summary>The frost creeping along the bar anew, 0 to 1 (the rime regrows left to right under a fresh veil, a cold glint follows); -1: done.</summary>
        public void SetFrost(double t)
        {
            _frost = t;
            if (t >= 0 && _fx == null)
            {
                _fxClip = new RectangleGeometry();
                _fx = new Canvas { IsHitTestVisible = false, Clip = _fxClip };
                _veil = new Border { Width = 32, Height = H, Background = VeilBrush, RenderTransform = new TranslateTransform() };
                _blade = Glint(BladeGeo, BladeBrush);
                _front = Piece(SparkleBrush, 18, 18); _front.RenderTransform = new TranslateTransform();
                _mote1 = Piece(SparkleBrush, 6, 6); _mote1.RenderTransform = new TranslateTransform();
                _mote2 = Piece(SparkleBrush, 7, 7); _mote2.RenderTransform = new TranslateTransform();
                foreach (var e in new UIElement[] { _veil, _blade, _front, _mote1, _mote2 }) _fx.Children.Add(e);
                _root.Children.Insert(2, _fx);
            }
            PlaceFrost();
        }

        void PlaceFrost()
        {
            if (_fx == null) return;
            if (_frost < 0) { _fx.Visibility = Visibility.Hidden; _rime.Clip = null; return; }
            double w = _root.ActualWidth;
            if (w <= 0) return;
            var r = Slab(new Size(w, H));
            double fx = Track(r) * Math.Clamp(_bar, 0, 1), t = _frost;
            _fx.Visibility = Visibility.Visible;
            var bar = new Rect(r.Left, r.Top, fx + 3, r.Height);
            if (_fxClip!.Rect != bar) _fxClip.Rect = bar;
            var all = new Rect(0, 0, w, H);
            if (_rimeClip.Rect != all) _rimeClip.Rect = all;
            if (_rime.Clip != _rimeClip) _rime.Clip = _rimeClip;
            double front = r.Left + (fx + 3) * Out(Seg(t, 0, .6)), veil = t > 0 ? 1 - Seg(t, .5, .75) : 0;
            _rimeK.ScaleX = Math.Max(.0001, front / w);
            Show(_veil!, veil); ((TranslateTransform)_veil!.RenderTransform).X = front - 30;
            Show(_front!, veil); var ft = (TranslateTransform)_front!.RenderTransform; ft.X = front - 10; ft.Y = r.Top - 6;
            double g = Seg(t, .55, 1);
            Show(_blade!, Math.Sin(Math.PI * g)); var bt = (TranslateTransform)_blade!.RenderTransform; bt.X = r.Left - 20 + (fx + 40) * Out(g); bt.Y = -6;
            int i = 0;
            foreach (var mote in new[] { _mote1!, _mote2! })
            {
                double l = Seg(t, .2 + .15 * i, .85 + .1 * i);
                Show(mote, l is > 0 and < 1 ? Math.Sin(Math.PI * l) * .95 : 0);
                var mt = (TranslateTransform)mote.RenderTransform;
                mt.X = r.Left + fx * (.35 + .4 * i) + 4 * Math.Sin(l * 5 + i) - mote.Width / 2; mt.Y = r.Bottom - 6 - (H - 10) * l - mote.Height / 2;
                i++;
            }
        }

        /// <summary>
        /// The flash freeze: a great snowflake of cold light blooms out behind the crown and fades; the crown crystallises in it
        /// (its six arms grow out turning into place, its branches sprout after), a ring of frost flung out.
        /// </summary>
        public void Crystallise()
        {
            if (_crown.Visibility != Visibility.Visible) return;
            Motion.Play(_crown, 950, t =>
            {
                double b = Seg(t, .02, .7);
                // white-hot for its first frames, then thinning to the outline as it grows and fades
                Show(_bloomHot, b is > 0 and < 1 ? (b < .08 ? b / .08 : 1 - Seg(b, .14, .4)) : 0);
                Show(_bloom, b is > 0 and < 1 ? Seg(b, .12, .26) * (1 - Seg(b, .4, 1)) : 0);
                _bloomK.ScaleX = _bloomK.ScaleY = .3 + .95 * Out(b);
                _bloomTurn.Angle = 30 * (1 - Out(b));
                _armsK.ScaleX = _armsK.ScaleY = Math.Max(.01, Out(Seg(t, 0, .42)));
                _branchesK.ScaleX = _branchesK.ScaleY = Math.Max(.01, Out(Seg(t, .28, .7)));
                _turn.Angle = -40 * (1 - Out(Seg(t, 0, .6)));
                double glow = t < .55 ? Seg(t, .35, .55) : 1 - Seg(t, .55, 1);
                _flakeGlow.Opacity = .4 + .6 * glow;
                Show(_tip, glow);
                double k = Seg(t, .4, 1);
                Show(_ring, k is > 0 and < 1 ? .9 * (1 - k) : 0);
                _ringK.ScaleX = _ringK.ScaleY = .4 + .6 * Out(k);
            }, () => { _armsK.ScaleX = _armsK.ScaleY = _branchesK.ScaleX = _branchesK.ScaleY = 1; _turn.Angle = 0; _flakeGlow.Opacity = .4; Show(_tip, 0); Show(_ring, 0); Show(_bloom, 0); Show(_bloomHot, 0); });
        }

        /// <summary>The row climbed a rank: its rim flashes ice-white and a frost trail runs along its foot.</summary>
        void Climb()
        {
            if (_climbRim == null)
            {
                _climbRim = new TacSketch((dc, s) => { if (s.Width > 30) dc.DrawGeometry(null, ClimbPen, Shard(Slab(s), 6, 1.5)); }) { Visibility = Visibility.Hidden };
                _climbTrail = new Border { Height = 2, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(1, 0, 1, 1), Background = ClimbTrail, IsHitTestVisible = false,
                    RenderTransformOrigin = new Point(0, .5), RenderTransform = new ScaleTransform(0, 1), Visibility = Visibility.Hidden };
                _root.Children.Insert(2, _climbRim); _root.Children.Insert(3, _climbTrail);
            }
            var rim = _climbRim; var trail = _climbTrail!; var k0 = (ScaleTransform)trail.RenderTransform;
            Motion.Play(rim, 640, t =>
            {
                Show(rim, 1 - t); Show(trail, 1 - t);
                k0.ScaleX = Out(Seg(t, 0, .55));
            }, () => { Show(rim, 0); Show(trail, 0); });
        }
    }

    // ================================================================ the meter's frost

    DateTime _lastLead;
    /// <summary>The driver of the meter's frost (one course on it at a time).</summary>
    readonly Border _frostRun = new() { Width = 0, Height = 0, IsHitTestVisible = false };

    /// <summary>A new leader took the top row: the frost creeps along the bars (at most once in a while, however often the lead swaps).</summary>
    internal void Lead()
    {
        if (!Moves || (DateTime.UtcNow - _lastLead).TotalMilliseconds < 1500) return;
        _lastLead = DateTime.UtcNow;
        Frost();
    }

    /// <summary>
    /// The meter's shimmer: on each bar in turn, top to bottom, the rime creeps anew from left to right under a fresh white
    /// veil, a cold glint runs along it after, a couple of ice motes lift off; the panel takes a breath of frost. One course.
    /// </summary>
    void Frost()
    {
        var rows = RowsNow();
        Motion.Play(_frostRun, 1500, t =>
        {
            for (int i = 0; i < rows.Count; i++) rows[i].SetFrost(Seg(t, .07 * i, .7 + .07 * i));
            Show(_fightBreath, t < .15 ? Seg(t, 0, .15) * .7 : .7 * (1 - Seg(t, .15, .7)));
        }, () =>
        {
            foreach (var r in rows) r.SetFrost(-1);
            Show(_fightBreath, 0);
        });
    }
}
