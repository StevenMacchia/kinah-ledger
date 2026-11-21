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
        TextBlock Pair(string w, string fig, Color figC)
        {
            var t = new TextBlock { FontFamily = Face, FontSize = 11, FontWeight = FontWeights.Medium, Foreground = Frozen(Mod.A(Colors.White, .78)), VerticalAlignment = VerticalAlignment.Center };
            System.Windows.Documents.Typography.SetNumeralAlignment(t, FontNumeralAlignment.Tabular);
            t.Inlines.Add(new System.Windows.Documents.Run(w + " "));
            t.Inlines.Add(new System.Windows.Documents.Run(fig) { FontWeight = FontWeights.Bold, Foreground = Frozen(figC), FontSize = 12.5 });
            return t;
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
    sealed class ArcadeRow : IThemeRow
    {
        public const double H = 28, Pitch = 30;
        const double PadL = 9, PadR = 6, RankW = 30, IconW = 23, BarH = 5, BarCell = 5, BarGap = 2;
        readonly Grid _root = new() { Height = H };
        readonly TacSketch _art, _fx, _crown;
        readonly Grid _words = new() { Margin = new Thickness(PadL, 0, PadR, 0) };
        readonly Border _rankHost = new() { HorizontalAlignment = HorizontalAlignment.Center };
        readonly TextBlock _name = Tt("", 13, White, FontWeights.SemiBold), _cp = Tt("", 9.5, Frozen(Mod.A(Colors.White, .66)), FontWeights.SemiBold),
            _heal = Tt("", 10.5, Mint2, FontWeights.Bold), _dps = Tt("", 17, White, FontWeights.Bold), _share = Tt("", 10, Frozen(Mod.A(Colors.White, .8)), FontWeights.Bold),
            _dmg = Tt("", 11, Soft, FontWeights.SemiBold), _crit = Tt("", 10.5, Soft, FontWeights.SemiBold);
        readonly StackPanel _line = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 0, 0) };
        readonly Border _iconHost = new() { Width = 17, Height = 17, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
        readonly List<FrameworkElement> _marks = new();
        readonly double _dpsW, _shareW, _dmgW, _critW;
        readonly TranslateTransform _crownDrop = new();
        string _cls = "\0";
        MeterMember? _m;
        int _rank, _lastRank;
        double _bar, _chase = -1, _land;
        Color _chaseC = Colors.White;
        bool _expanded, _shareOn = true;

        public FrameworkElement Root => _root;

        public ArcadeRow()
        {
            _dps.Tag = "row-dps"; _share.Tag = "row-share"; _heal.Tag = "heal-figures"; _cp.Tag = "cp";
            _dps.HorizontalAlignment = _share.HorizontalAlignment = _dmg.HorizontalAlignment = _crit.HorizontalAlignment = HorizontalAlignment.Right;
            _dps.VerticalAlignment = VerticalAlignment.Center; _dps.Margin = new Thickness(0, 0, 0, 1);
            _share.Margin = new Thickness(0, 3, 0, 0); _dmg.Margin = new Thickness(0, 1, 0, 0); _crit.Margin = new Thickness(0, 1, 0, 0);
            _heal.Margin = new Thickness(6, 3, 0, 0); _heal.VerticalAlignment = VerticalAlignment.Top;
            // the columns: rank, the class art, the name's line over its bar (the rest), heal, the DPS, the share; expanded, the damage and the crit rate
            _dpsW = FigCol(_dps, "888.8K") + 6; _shareW = FigCol(_share, "88.8%", "100%", L.T("CP")) + 6;
            _dmgW = FigCol(_dmg, "888.88M") + 8; _critW = FigCol(_crit, "88%") + 8;
            foreach (var w in new[] { RankW, IconW, double.NaN, 0, _dpsW, _shareW, 0, 0 })
                _words.ColumnDefinitions.Add(new ColumnDefinition { Width = double.IsNaN(w) ? new GridLength(1, GridUnitType.Star) : w == 0 ? GridLength.Auto : new GridLength(w) });
            _words.ColumnDefinitions[6].Width = new GridLength(0); _words.ColumnDefinitions[7].Width = new GridLength(0);
            _art = new TacSketch(Draw);
            _fx = new TacSketch(DrawFx);
            _crown = new TacSketch(DrawCrownArt, RankW, 12) { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 0, 0), RenderTransform = _crownDrop, Visibility = Visibility.Collapsed };
            _root.Children.Add(_art);
            Put(_rankHost, 0); Put(_crown, 0); Put(_iconHost, 1); Put(_line, 2); Put(_heal, 3); Put(_dps, 4); Put(_share, 5); Put(_dmg, 6); Put(_crit, 7);
            _name.TextTrimming = TextTrimming.CharacterEllipsis;
            _cp.Margin = new Thickness(5, 3, 0, 0);
            _line.Children.Add(_name); _line.Children.Add(_cp);
            // the words' tight shade: one effect for the row's words
            _words.Effect = Shade();
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
            var tone = RankC(m.Rank);
            if (m.Rank != _rank)
            {
                _rank = m.Rank;
                // the rank word in LED letters (a longer one a dot smaller, to keep the column); "1위" as a text in Korean
                var word = RankWord(m.Rank);
                var rw = Word(word, word.Length > 3 ? 1.2 : 1.5, tone, .38, textSize: 12);
                _rankHost.Child = rw;
                _crown.Visibility = m.Rank == 1 ? Visibility.Visible : Visibility.Collapsed;
                _rankHost.VerticalAlignment = m.Rank == 1 ? VerticalAlignment.Bottom : VerticalAlignment.Center;
                _rankHost.Margin = new Thickness(0, 0, 0, m.Rank == 1 ? 2 : 0);
                _dps.Foreground = Frozen(Mod.Mix(tone, Colors.White, .42));
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
            if (m.TopHeals) { var t = Mark("✚", MarkMint, MarkMintC); t.Tag = "top-heals"; t.ToolTip = L.T("Top heals"); Add(t); }
            if (m.Deaths > 0) { var d = Tt($"✕{m.Deaths}", 10.5, Coral, FontWeights.Bold); d.Tag = "deaths"; Add(d); d.Margin = new Thickness(5, 2, 0, 0); }
            if (m.You) { var y = Mark(L.T("YOU"), filled: true); y.Tag = "you"; Add(y); }
            _heal.Text = m.Heal.Length > 0 ? "+" + m.Heal : "";
            _heal.Visibility = m.Heal.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            _dps.Text = m.Figure;
            _dps.FontSize = m.CpOnly ? 14 : 17;
            // a wider figure (Combat Power before the first hit, "167,548") widens its column: never a digit cut
            _words.ColumnDefinitions[4].Width = new GridLength(Math.Max(_dpsW, TextW(_dps, m.Figure) + 6));
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
                if (m.Rank == 1) CrownDrop(0);
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

        /// <summary>The LED bar's place: under the name's line, from the name's start to the DPS column (heal included).</summary>
        Rect BarRect(Size s)
        {
            var cols = _words.ColumnDefinitions;
            double x0 = PadL + cols[0].ActualWidth + cols[1].ActualWidth;
            double x1 = x0 + cols[2].ActualWidth + cols[3].ActualWidth - 8;
            return new Rect(x0, s.Height - BarH - 3, Math.Max(0, x1 - x0), BarH);
        }

        void Draw(DrawingContext dc, Size s)
        {
            if (_m is not { } m || s.Width < 40) return;
            var tone = RankC(m.Rank);
            // you: the player-one glow: a light band, a dotted selection box round the row, the pixel cursor at its left
            if (m.You)
            {
                dc.DrawRectangle(Mod.Grad(false, (Mod.A(LedC, .16), 0), (Mod.A(LedC, .05), .6), (Mod.A(LedC, .1), 1)), null, new Rect(0, 0, s.Width, s.Height));
                var dot = Frozen(Mod.A(LedC, .75));
                for (double x = 0; x + 2 <= s.Width; x += 4) { dc.DrawRectangle(dot, null, new Rect(x, 0, 2, 1.5)); dc.DrawRectangle(dot, null, new Rect(x, s.Height - 1.5, 2, 1.5)); }
                DrawCursor(dc, 1.5, s.Height / 2, 1.1, Frozen(Mod.Mix(LedC, Colors.White, .4)));
            }
            // the DPS's LED bloom: a soft glow in the rank's colour behind the figure
            var cols = _words.ColumnDefinitions;
            double dx = PadL + cols[0].ActualWidth + cols[1].ActualWidth + cols[2].ActualWidth + cols[3].ActualWidth;
            double dw = cols[4].ActualWidth;
            if (dw > 0)
            {
                var bloom = new RadialGradientBrush(Mod.A(tone, .26), Mod.A(tone, 0));
                bloom.Freeze();
                dc.DrawEllipse(bloom, null, new Point(dx + dw - Math.Min(dw, TextW(_dps, _dps.Text)) / 2, s.Height / 2), dw * .62, s.Height * .5);
            }
            // the bar: LED cells lit to the share in the class's colour
            var bar = BarRect(s);
            DrawLed(dc, bar, m.CpOnly ? 0 : _bar, m.ClassColor, BarCell, BarGap);
            // a healer's share of the group's healing as a mint pixel line under the cells (Settings' heal bars)
            if (ParserRows.HealBarsPref && m.HealShare > 0)
                dc.DrawRectangle(Frozen(Mod.A(MintC2, .95)), null, new Rect(bar.X, bar.Bottom + 1.5, Math.Max(3, bar.Width * Math.Clamp(m.HealShare, 0, 1)), 1.5));
        }

        /// <summary>The chase over the cells (and a flash of the rank word's light while it runs).</summary>
        void DrawFx(DrawingContext dc, Size s)
        {
            if (_chase < 0 || _chase > 1 || s.Width < 40) return;
            DrawChase(dc, BarRect(s), _chase, _chaseC, BarCell, BarGap);
        }

        /// <summary>The crown over 1ST, and on its landing four pixel sparkles round it, fading.</summary>
        void DrawCrownArt(DrawingContext dc, Size s)
        {
            double x = Math.Round((s.Width - CrownW) / 2), y = 1;
            DrawCrown(dc, x, y, CrownPx);
            if (_land is > 0 and < 1)
            {
                var b = Frozen(Mod.A(Color.FromRgb(0xFF, 0xF4, 0xB0), 1 - _land));
                double spread = 2 + 3 * _land;
                foreach (var (px, py) in new[] { (x - spread, y + 1), (x + CrownW + spread - 1, y + 1), (x - spread + 1, y + CrownH - 2), (x + CrownW + spread - 2, y + CrownH - 2) })
                {
                    if (px < 0 || px + 3 > s.Width) continue;
                    dc.DrawRectangle(b, null, new Rect(px, py + 1, 3, 1));
                    dc.DrawRectangle(b, null, new Rect(px + 1, py, 1, 3));
                }
            }
        }

        /// <summary>A chase of light along this row's cells, in <paramref name="c"/>, after <paramref name="delay"/> ms.</summary>
        internal void Chase(double delay, Color c)
        {
            const double Run = 560;
            double total = delay + Run;
            _chaseC = c;
            Motion.Play(_fx, total, p =>
            {
                double l = (p * total - delay) / Run;
                _chase = l < 0 || l >= 1 ? -1 : l;
                _fx.InvalidateVisual();
            }, () => { _chase = -1; _fx.InvalidateVisual(); });
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
                _land = l < .55 ? 0 : Seg(l, .55, .95);
                _crown.InvalidateVisual();
            }, () => { _crownDrop.Y = 0; _crown.Opacity = 1; _land = 0; _crown.InvalidateVisual(); });
        }
    }
}
