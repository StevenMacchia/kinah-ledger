using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using KinahLedger.Parser;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

sealed partial class HudArcade
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

    /// <summary>The fight's foot under its dotted rule: "Total 622.4M" at the left, the group's word and figure at the right, the AVG mark on a held average.</summary>
    FrameworkElement Foot(Encounter e, DateTime now)
    {
        var (total, word, group, held) = FootOf(e, now);
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        StackPanel Pair(string w, string fig, Color figC)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var t = Tt(w + " ", 11, Frozen(Mod.A(Colors.White, .78)), FontWeights.Medium);
            sp.Children.Add(t);
            var f = Fig(fig, 1.6, figC, .3, textSize: 12.5); f.Margin = new Thickness(2, 0, 0, 0);
            sp.Children.Add(f);
            return sp;
        }
        var tp = Pair(L.T("Total"), total, Colors.White);
        EnergyMark.After(tp, 11, Frozen(Mod.A(Colors.White, .72)));   // Odyle Energy (Ui/EnergyMark.cs)
        g.Children.Add(tp);
        var right = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 0, 0) };
        if (word.Length > 0) right.Children.Add(Pair(word, group, LedC));
        if (held)
        {
            var avg = Mark(L.T("AVG"), size: 8.5); avg.Tag = "avg-badge"; avg.ToolTip = L.T(Encounter.AvgBadge); avg.Margin = new Thickness(5, 0, 0, 0);
            right.Children.Add(avg);
        }
        Grid.SetColumn(right, 1); g.Children.Add(right);
        return AsFoot(g, e, now);
    }

    /// <summary>The line between the party and you pinned below it: "2 MORE" between two dotted rules.</summary>
    static FrameworkElement GapLine(int left)
    {
        var g = new Grid { VerticalAlignment = VerticalAlignment.Center };
        g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); g.ColumnDefinitions.Add(new ColumnDefinition());
        var a = Dots(new Thickness(30, 0, 6, 0), MazeHiC, .45); a.VerticalAlignment = VerticalAlignment.Center; g.Children.Add(a);
        var t = Tt(left > 0 ? L.F("{0} more", left).ToUpperInvariant() : "▪", 9, Frozen(Mod.A(Colors.White, .75)), FontWeights.Bold);
        Grid.SetColumn(t, 1); g.Children.Add(t);
        var b = Dots(new Thickness(6, 0, 6, 0), MazeHiC, .45); b.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(b, 2); g.Children.Add(b);
        return g;
    }

    /// <summary>A rank in Korean or Chinese: the numeral in the LED face, the language's word for "place" after it, bold in the rank's colour.</summary>
    static FrameworkElement RankNative(int rank, Color tone)
    {
        var n = rank.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        sp.Children.Add(Fig(n, 1.5, tone, .38));
        var suffix = RankWord(rank)[n.Length..];
        var t = Tt(suffix, 11, Ink(tone), FontWeights.Bold); t.Margin = new Thickness(1, 0, 0, 0); t.VerticalAlignment = VerticalAlignment.Center;
        sp.Children.Add(t);
        return sp;
    }

    /// <summary>
    /// One line of the high-score table. The rank word in LED letters in its rank's colour (1ST gold, 2ND silver-cyan, 3RD
    /// orange, 4TH pink, 5TH green; a pixel crown over 1ST), the class's art, the name in white with its Combat Power small and
    /// the marks (★ DPS, a healer's ✚, deaths, YOU), and under the name an LED bar: discrete cells lit to the member's part of
    /// the top in the class's colour, the head cell hot. At the right the DPS big in the rank's colour with a soft LED bloom
    /// behind it, the share small. You: the player-one glow (a light band, a dotted selection box, a pixel cursor at the left).
    /// Short of room the CP goes first, then the share; a name keeps at least ten characters; a figure is never cut (Mod.Fit).
    /// Moments: a chase of light along the cells (<see cref="Chase"/>), the crown dropping onto 1ST (<see cref="CrownDrop"/>);
    /// a row that climbs runs its own chase, and the one that takes 1ST gets the crown.
    /// </summary>
    sealed class ArcadeRow : IThemeRow, IThemeBarSpan
    {
        public const double H = 30, Pitch = 32;
        const double PadL = 9, PadR = 6, RankW = 30, IconW = 23, BarH = 8, BarCell = 6, BarGap = 2;
        readonly HudArcade _look;
        readonly Grid _root = new() { Height = H };
        readonly TacSketch _art, _light, _crown, _spark;
        /// <summary>The chase's window over the bar (clips the light), and the light's place in it.</summary>
        readonly Canvas _chaseHost = new() { ClipToBounds = true, IsHitTestVisible = false, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        readonly TranslateTransform _chaseAt = new();
        readonly ScaleTransform _sparkOut = new(1, 1);
        Rect _barRect = Rect.Empty;
        double _dpsInk;
        readonly Grid _words = new() { Margin = new Thickness(PadL, 0, PadR, 0) };
        readonly Border _rankHost = new() { HorizontalAlignment = HorizontalAlignment.Center };
        readonly TextBlock _name = Tt("", 13, White, FontWeights.SemiBold), _cp = Tt("", 9.5, Frozen(Mod.A(Colors.White, .66)), FontWeights.SemiBold),
            _heal = Tt("", 10.5, Mint2, FontWeights.Bold), _share = Tt("", 10, Frozen(Mod.A(Colors.White, .8)), FontWeights.Bold),
            _dmg = Tt("", 11, Soft, FontWeights.SemiBold), _crit = Tt("", 10.5, Soft, FontWeights.SemiBold);
        readonly StackPanel _line = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 0, 0) };
        readonly Border _iconHost = new() { Width = 17, Height = 17, VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 1, 0, 0) };
        readonly List<FrameworkElement> _marks = new();
        readonly double _dpsW, _shareW, _dmgW, _critW;
        /// <summary>The DPS in the score face (LED digits), its host in the DPS column; rebuilt only when the figure or the rank changes.</summary>
        readonly Border _dps = new() { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        string _dpsSig = "";
        const double DpsPx = 2.1;
        readonly TranslateTransform _crownDrop = new();
        string _cls = "\0";
        MeterMember? _m;
        int _rank, _lastRank;
        double _bar;
        Color _chaseC = Colors.White;
        bool _expanded, _shareOn = true;

        public FrameworkElement Root => _root;

        public ArcadeRow(HudArcade look)
        {
            _look = look;
            _share.Tag = "row-share"; _heal.Tag = "heal-figures"; _cp.Tag = "cp";
            _share.HorizontalAlignment = _dmg.HorizontalAlignment = _crit.HorizontalAlignment = HorizontalAlignment.Right;
            _share.Margin = new Thickness(0, 3, 0, 0); _dmg.Margin = new Thickness(0, 1, 0, 0); _crit.Margin = new Thickness(0, 1, 0, 0);
            _heal.Margin = new Thickness(6, 3, 0, 0); _heal.VerticalAlignment = VerticalAlignment.Top;
            // the columns: rank, the class art, the name's line over its bar (the rest), heal, the DPS, the share; expanded, the damage and the crit rate
            _dpsW = FigCol(Fig("", DpsPx, Colors.White, 0, textSize: 17), "888.8K") + 6; _shareW = FigCol(_share, "88.8%", "100%", L.T("CP")) + 6;
            _dmgW = FigCol(_dmg, "888.88M") + 8; _critW = FigCol(_crit, "88%") + 8;
            foreach (var w in new[] { RankW, IconW, double.NaN, 0, _dpsW, _shareW, 0, 0 })
                _words.ColumnDefinitions.Add(new ColumnDefinition { Width = double.IsNaN(w) ? new GridLength(1, GridUnitType.Star) : w == 0 ? GridLength.Auto : new GridLength(w) });
            _words.ColumnDefinitions[6].Width = new GridLength(0); _words.ColumnDefinitions[7].Width = new GridLength(0);
            _art = new TacSketch(Draw);
            _light = new TacSketch((dc, _) => DrawChaseLight(dc, _chaseC, BarCell, BarGap, BarH), 4 * (BarCell + BarGap) + 2, BarH + 4) { RenderTransform = _chaseAt, Visibility = Visibility.Hidden };
            _chaseHost.Children.Add(_light);
            // the crown: a frozen drawing, placed once; its drop and its sparkles are transforms and opacity
            _crown = new TacSketch((dc, s) => { dc.PushTransform(new TranslateTransform(Math.Round((s.Width - CrownW) / 2), 1)); dc.DrawDrawing(Crown); dc.Pop(); }, RankW, 13)
                { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 0, 0), RenderTransform = _crownDrop, Visibility = Visibility.Collapsed };
            _spark = new TacSketch(DrawSparks, RankW, 13) { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 0, 0),
                RenderTransformOrigin = new Point(.5, .5), RenderTransform = _sparkOut, Visibility = Visibility.Hidden };
            _root.Children.Add(_art);
            Put(_rankHost, 0); Put(_crown, 0); Put(_spark, 0); Put(_iconHost, 1); Put(_line, 2); Put(_heal, 3); Put(_dps, 4); Put(_share, 5); Put(_dmg, 6); Put(_crit, 7);
            _name.TextTrimming = TextTrimming.CharacterEllipsis;
            _cp.Margin = new Thickness(5, 3, 0, 0);
            _line.Children.Add(_name); _line.Children.Add(_cp);
            // no effect on the row's words: they sit on the screen's black, where a shade adds nothing but cost
            _root.Children.Add(_words);
            _root.Children.Add(_chaseHost);
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
            var tone = RankC(m.Rank);
            if (m.Rank != _rank)
            {
                _rank = m.Rank;
                // the rank word in LED letters (a longer one a dot smaller, to keep the column); "1위" as a text in Korean
                var word = RankWord(m.Rank);
                FrameworkElement rw = Spells(word) ? Word(word, word.Length > 3 ? 1.2 : 1.5, tone, .38) : RankNative(m.Rank, tone);
                _rankHost.Child = rw;
                _crown.Visibility = m.Rank == 1 ? Visibility.Visible : Visibility.Collapsed;
                _rankHost.VerticalAlignment = m.Rank == 1 ? VerticalAlignment.Bottom : VerticalAlignment.Center;
                _rankHost.Margin = new Thickness(0, 0, 0, m.Rank == 1 ? 1 : 0);
            }
            if (m.Class != _cls)
            {
                _cls = m.Class;
                var icon = ThemeText.Icon(m.Class, 17, m.Name);
                if (icon is Border b) { b.BorderBrush = Frozen(Mod.Mix(m.ClassColor, Colors.White, .35)); b.BorderThickness = new Thickness(1); }
                _iconHost.Child = icon;
            }
            _name.Text = m.Name;
            _cp.Text = m.Cp;
            // the marks after the name: ★ DPS, a healer's ✚, deaths, YOU (kept whole: the name and the CP give way first)
            foreach (var mk in _marks) _line.Children.Remove(mk);
            _marks.Clear();
            void Add(FrameworkElement el) { el.Margin = new Thickness(5, 1, 0, 0); el.VerticalAlignment = VerticalAlignment.Top; _line.Children.Add(el); _marks.Add(el); }
            if (m.TopDps) { var t = Mark("★ " + L.T("DPS"), Frozen(Mod.Mix(LedC, Colors.White, .25)), LedC); t.Tag = "top-dps"; t.ToolTip = L.T("Top DPS"); Add(t); }
            if (m.TopHeals) { var t = Mark("✚ " + L.T("Heal"), MarkMint, MarkMintC); t.Tag = "top-heals"; t.ToolTip = L.T("Top heals"); Add(t); }
            if (m.Deaths > 0) { var d = Tt($"✕{m.Deaths}", 10.5, Coral, FontWeights.Bold); d.Tag = "deaths"; Add(d); d.Margin = new Thickness(5, 2, 0, 0); }
            if (m.You) { var y = Mark(L.T("YOU"), filled: true); y.Tag = "you"; Add(y); }
            _heal.Text = m.Heal.Length > 0 ? "+" + m.Heal : "";
            _heal.Visibility = m.Heal.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            double px = m.CpOnly ? 1.6 : DpsPx;
            var sig = m.Figure + "|" + m.Rank + "|" + px;
            if (sig != _dpsSig)
            {
                _dpsSig = sig;
                var fig = Fig(m.Figure, px, Mod.Mix(tone, Colors.White, .35), 0, textSize: m.CpOnly ? 14 : 17);
                fig.Tag = "row-dps";
                _dps.Child = fig;
            }
            // a wider figure (Combat Power before the first hit, "167,548") widens its column: never a digit cut
            _dpsInk = TextW(Tt("", m.CpOnly ? 14 : 17, White, FontWeights.Bold), m.Figure);
            _words.ColumnDefinitions[4].Width = new GridLength(Math.Max(_dpsW, _dpsInk + 6));
            _share.Text = m.ShareText;
            _dmg.Text = c.Expanded ? m.Damage : "";
            _crit.Text = c.Expanded ? m.Crit : "";
            Fit();
            SlideBar(_art, c, "bar", BarOf(m, c), v => { _bar = v; _art.InvalidateVisual(); });
            _art.InvalidateVisual();
            // a climb: a chase along this row's cells; taking 1ST, the crown drops onto it
            if (!fresh && c.Moves && _lastRank > 0 && m.Rank < _lastRank)
            {
                Chase(0, Mod.Mix(tone, Colors.White, .5));
                if (m.Rank == 1) { CrownDrop(0); _look.NewLeader(this, m.You); }
            }
            _lastRank = m.Rank;
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
            _art.InvalidateVisual();
        }

        /// <summary>
        /// Where the DPS bar is drawn, for main's PB ghost (IThemeBarSpan, added at integration): the row's art and, in its
        /// coordinates, the run of cells a FULL bar lights (the LED bar under the name, flush to its last whole cell).
        /// </summary>
        public (FrameworkElement On, Rect Track)? BarTrack => _art.ActualWidth <= 0 ? null : (_art, FullBar(_art.RenderSize));

        Rect FullBar(Size s) { var b = BarRect(s); return new Rect(b.X, b.Y, Cells(b.Width, BarCell, BarGap).W, b.Height); }

        /// <summary>The LED bar's place: under the name's line, from the name's start to the DPS column (heal included).</summary>
        Rect BarRect(Size s)
        {
            var cols = _words.ColumnDefinitions;
            double x0 = PadL + cols[0].ActualWidth + 4;
            double x1 = x0 + cols[1].ActualWidth + cols[2].ActualWidth + cols[3].ActualWidth - 8;
            return new Rect(x0, s.Height - BarH - 3, Math.Max(0, x1 - x0), BarH);
        }

        void Draw(DrawingContext dc, Size s)
        {
            if (_m is not { } m || s.Width < 40) return;
            var tone = RankC(m.Rank);
            // you: the player-one glow: a light band, a dotted selection box round the row (frozen tiles), the pixel cursor at its left
            if (m.You)
            {
                dc.DrawRectangle(YouBand, null, new Rect(0, 0, s.Width, s.Height));
                dc.DrawRectangle(YouDots, null, new Rect(0, 0, s.Width, 1.5));
                dc.PushTransform(new TranslateTransform(0, s.Height - 1.5)); dc.DrawRectangle(YouDots, null, new Rect(0, 0, s.Width, 1.5)); dc.Pop();
                DrawCursor(dc, 1.5, s.Height / 2, 1.1, YouCursor);
            }
            // the DPS's LED bloom: a soft glow in the rank's colour behind the figure
            var cols = _words.ColumnDefinitions;
            double dx = PadL + cols[0].ActualWidth + cols[1].ActualWidth + cols[2].ActualWidth + cols[3].ActualWidth;
            double dw = cols[4].ActualWidth;
            if (dw > 0) dc.DrawEllipse(Bloom(tone), null, new Point(dx + dw - Math.Min(dw, _dpsInk) / 2, s.Height / 2), dw * .62, s.Height * .5);
            // the bar: LED cells lit to the share in the class's colour
            var bar = BarRect(s);
            DrawLed(dc, bar, m.CpOnly ? 0 : _bar, Vivid(m.ClassColor), BarCell, BarGap, .14);
            // a healer's share of the group's healing as a mint pixel line under the cells (Settings' heal bars)
            if (ParserRows.HealBarsPref && m.HealShare > 0)
                dc.DrawRectangle(Ink(MintC2, .95), null, new Rect(bar.X, bar.Bottom + 1, Math.Max(3, bar.Width * Math.Clamp(m.HealShare, 0, 1)), 1.5));
            // the chase's window follows the bar (set only when the bar's place changes)
            if (bar != _barRect)
            {
                _barRect = bar;
                _chaseHost.Margin = new Thickness(bar.X, bar.Y - 2, 0, 0);
                _chaseHost.Width = Cells(bar.Width, BarCell, BarGap).W + 2; _chaseHost.Height = BarH + 4;
            }
        }

        static readonly Brush YouBand = Mod.Grad(false, (Mod.A(LedC, .16), 0), (Mod.A(LedC, .05), .6), (Mod.A(LedC, .1), 1)), YouCursor = Frozen(Mod.Mix(LedC, Colors.White, .4));
        static readonly DrawingBrush YouDots = CellTile(Mod.A(LedC, .75), 2, 2, 1.5, false);
        static readonly Dictionary<Color, Brush> Blooms = new();
        static Brush Bloom(Color c)
        {
            if (Blooms.TryGetValue(c, out var b)) return b;
            var r = new RadialGradientBrush(Mod.A(c, .26), Mod.A(c, 0)); r.Freeze();
            return Blooms[c] = r;
        }

        /// <summary>The crown's landing sparkles: four pixel crosses round it, drawn once; the landing scales them out and fades them.</summary>
        static void DrawSparks(DrawingContext dc, Size s)
        {
            double x = Math.Round((s.Width - CrownW) / 2), y = 1;
            var b = Ink(Color.FromRgb(0xFF, 0xF4, 0xB0));
            foreach (var (px, py) in new[] { (Math.Max(0, x - 5), y), (Math.Min(s.Width - 3, x + CrownW + 2), y), (Math.Max(0, x - 4), y + CrownH - 3), (Math.Min(s.Width - 3, x + CrownW + 1), y + CrownH - 3) })
            {
                dc.DrawRectangle(b, null, new Rect(px, py + 1, 3, 1));
                dc.DrawRectangle(b, null, new Rect(px + 1, py, 1, 3));
            }
        }

        /// <summary>
        /// A chase of light along this row's cells, in <paramref name="c"/>, after <paramref name="delay"/> ms: the light (drawn
        /// once) steps along the bar a whole cell at a time by its transform; nothing is drawn again while it runs.
        /// </summary>
        internal void Chase(double delay, Color c)
        {
            const double Run = 560;
            double total = delay + Run, pitch = BarCell + BarGap;
            if (_chaseC != c) { _chaseC = c; _light.InvalidateVisual(); }
            int n = _barRect.IsEmpty ? 30 : Cells(_barRect.Width, BarCell, BarGap).N;
            Motion.Play(_light, total, p =>
            {
                double l = (p * total - delay) / Run;
                if (l < 0 || l >= 1) { Fade(_light, 0); return; }
                int c0 = (int)Math.Floor(-3 + (n + 6) * l);
                _chaseAt.X = (c0 - 3) * pitch - 2;
                Fade(_light, 1);
            }, () => Fade(_light, 0));
        }

        /// <summary>The crown's middle on <paramref name="on"/> (where a new leader's sparks fly from), or null before it is laid out.</summary>
        internal Point? CrownAt(UIElement on)
        {
            try
            {
                if (_crown.Visibility != Visibility.Visible) return null;
                if (_crown.ActualWidth <= 0) _root.UpdateLayout();
                return _crown.ActualWidth > 0 ? _crown.TranslatePoint(new Point(_crown.ActualWidth / 2, 6), on) : null;
            }
            catch (InvalidOperationException) { return null; }
        }

        /// <summary>The crown drops onto 1ST from above with a bounce, and sparkles as it lands (after <paramref name="delay"/> ms).</summary>
        internal void CrownDrop(double delay)
        {
            if (_crown.Visibility != Visibility.Visible) return;
            const double Run = 620;
            double total = delay + Run;
            Motion.Play(_crown, total, p =>
            {
                double l = Math.Clamp((p * total - delay) / Run, 0, 1);
                double fall = Seg(l, 0, .55);
                // falling in, then two hops: a bounce, a smaller one, settled (in whole pixels, as a sprite moves)
                double y = fall < 1 ? -11 * (1 - fall * fall) : l < .75 ? -3 * Math.Sin(Math.PI * Seg(l, .55, .75)) : -1 * Math.Sin(Math.PI * Seg(l, .75, .9));
                _crownDrop.Y = Math.Round(y);
                _crown.Opacity = Seg(l, 0, .15);
                double land = l < .55 ? 0 : Seg(l, .55, .95);
                _sparkOut.ScaleX = _sparkOut.ScaleY = .8 + .5 * land;
                Fade(_spark, land > 0 && land < 1 ? 1 - land : 0);
            }, () => { _crownDrop.Y = 0; _crown.Opacity = 1; Fade(_spark, 0); });
        }
    }
}
