using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using KinahLedger.Parser;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

sealed partial class HudManga
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

    /// <summary>A member's bar, 0 to 1, against the fight's top member's.</summary>
    static double BarOf(MeterMember m, MeterContext c) => c.E.BarOf(m.F);

    /// <summary>The fight's foot in ink on the page's paper: "Total 622.4M" at the left, the group's word and figure at the right, AVG on a held average.</summary>
    FrameworkElement Foot(Encounter e, DateTime now)
    {
        var (total, word, group, held) = FootOf(e, now);
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        TextBlock Pair(string w, string fig)
        {
            var t = new TextBlock { FontFamily = Mod.Face, FontSize = 11, FontWeight = FontWeights.Bold, Foreground = OnPaperSoft, VerticalAlignment = VerticalAlignment.Center };
            System.Windows.Documents.Typography.SetNumeralAlignment(t, FontNumeralAlignment.Tabular);
            t.Inlines.Add(new System.Windows.Documents.Run(w + " "));
            t.Inlines.Add(new System.Windows.Documents.Run(fig) { FontFamily = FigFace, FontWeight = FontWeights.Normal, Foreground = InkB, FontSize = 15 });
            return t;
        }
        var tp = Pair(L.T("Total"), total);
        EnergyMark.After(tp, 11, OnPaperSoft);   // Odyle Energy (Ui/EnergyMark.cs)
        g.Children.Add(tp);
        var right = new StackPanel { Orientation = Orientation.Horizontal };
        if (word.Length > 0) right.Children.Add(Pair(word, group));
        if (held)
        {
            var avg = Mark(L.T("AVG"), size: 8.5); avg.Tag = "avg-badge"; avg.ToolTip = L.T(Encounter.AvgBadge); avg.Margin = new Thickness(5, 0, 0, 0);
            right.Children.Add(avg);
        }
        Grid.SetColumn(right, 1); g.Children.Add(right);
        return AsFoot(g, e, now);
    }

    /// <summary>The line between the party and you pinned below it: "2 MORE", white on the rows' ink.</summary>
    static FrameworkElement GapLine(int left)
    {
        var t = Tt(left > 0 ? L.F("{0} more", left).ToUpperInvariant() : "·", 9, OnInkSoft, FontWeights.Black);
        t.HorizontalAlignment = HorizontalAlignment.Center;
        return t;
    }

    /// <summary>
    /// One member on the rows' black ink: the rank (the leader's on a red explosion balloon, the crown), the class's art in a
    /// white ring, the name in a heavy face with the Combat Power and the marks (★ DPS, a healer's ✚, deaths in red, YOU in
    /// red), and under the name the bar: a bold brush stroke in the class's colour, a dry streak along it, a sharp flick at
    /// its end; a healer's healing a second, the DPS huge in Impact and the share at the right. The leader's row is the page's
    /// climax panel: white, concentration lines driving in from its edges, its words in ink. Short of room the CP goes first,
    /// then the share; a name keeps at least ten characters; a figure is never cut (Mod.Fit).
    /// </summary>
    sealed class MangaRow : IThemeRow
    {
        public const double H = 32, Pitch = 34, BarH = 11, LineH = 19;
        const double Pad = 7;
        readonly Grid _root = new() { Height = H, Background = Brushes.Transparent };
        readonly TacSketch _ground;
        readonly Grid _words = new() { Margin = new Thickness(Pad, 0, Pad, 0) };
        readonly TextBlock _rank = Fig("", 16.5, White), _name = Tt("", 12.5, White, FontWeights.Black, NameFace), _cp = Tt("", 10, OnInkSoft, FontWeights.Bold),
            _heal = Tt("", 11, White, FontWeights.Black), _dps = Fig("", 21, White), _share = Tt("", 10.5, OnInkSoft, FontWeights.Bold),
            _dmg = Tt("", 11, OnInkSoft, FontWeights.Bold), _crit = Tt("", 10.5, OnInkSoft, FontWeights.Bold);
        readonly StackPanel _line = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 0, 0) };
        readonly Border _iconHost = new() { Width = 17, Height = 17, VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 1.5, 0, 0) };
        readonly List<FrameworkElement> _marks = new();
        readonly double _dpsW, _shareW, _dmgW, _critW;
        // the bar: its body clipped to the member's part (the clip scales), its brush flick moved to the clip's edge, the glint in it
        readonly Grid _barHost = new() { Height = BarH, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(-2, LineH + .5, 6, 0) };
        readonly Grid _barFill = new();
        readonly InkArt _barBody = new(), _barCap = new(), _glint;
        readonly ScaleTransform _clipScale = new(0, 1);
        readonly TranslateTransform _capShift = new(), _glintShift = new(-30, 0);
        readonly Canvas _crownHost = new() { Width = 0, Height = 0, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, LineH / 2 + .5, 0, 0) };
        readonly InkArt _crown = new(CrownArt, 0, 0);
        readonly ScaleTransform _crownScale = new(1, 1);
        readonly InkArt _trail = new() { Opacity = 0, VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Left };
        string _cls = "\0", _barSig = "";
        MeterMember? _m;
        double _bar, _barW;
        bool _expanded, _shareOn = true, _lead;
        int _lastRank;

        public FrameworkElement Root => _root;

        /// <summary>Where the DPS bar is drawn (the PB ghost, IThemeBarSpan on main): the stroke's host, a full bar filling its whole width.</summary>
        public (FrameworkElement On, Rect Track)? BarTrack => _barHost.ActualWidth <= 0 ? null : (_barHost, new Rect(0, 0, _barHost.ActualWidth, _barHost.ActualHeight));

        public MangaRow()
        {
            _dps.Tag = "row-dps"; _share.Tag = "row-share"; _heal.Tag = "heal-figures"; _cp.Tag = "cp";
            _dps.HorizontalAlignment = _share.HorizontalAlignment = _dmg.HorizontalAlignment = _crit.HorizontalAlignment = HorizontalAlignment.Right;
            _share.Margin = new Thickness(0, 3, 0, 0); _dmg.Margin = new Thickness(0, 1, 0, 0); _crit.Margin = new Thickness(0, 1, 0, 0);
            _heal.Margin = new Thickness(6, 1, 0, 0);
            _dps.Margin = new Thickness(0, 0, 0, 1);
            _dpsW = FigCol(_dps, "888.8K") + 4; _shareW = FigCol(_share, "88.8%", "100%", L.T("CP")) + 6;
            _dmgW = FigCol(_dmg, "888.88M") + 8; _critW = FigCol(_crit, "88%") + 8;
            foreach (var w in new[] { 18.0, 24, double.NaN, 0, _dpsW, _shareW, 0, 0 })
                _words.ColumnDefinitions.Add(new ColumnDefinition { Width = double.IsNaN(w) ? new GridLength(1, GridUnitType.Star) : w == 0 ? GridLength.Auto : new GridLength(w) });
            _words.ColumnDefinitions[6].Width = new GridLength(0); _words.ColumnDefinitions[7].Width = new GridLength(0);
            _ground = new TacSketch(DrawGround);
            _root.Children.Add(_ground);
            _root.Children.Add(_trail);
            // the bar under the name's line
            var clip = new RectangleGeometry(new Rect(0, -3, 1, BarH + 6)) { Transform = _clipScale };
            _barFill.Clip = clip;
            _barFill.Children.Add(_barBody);
            _glint = new InkArt(GlintArt, 16, BarH) { HorizontalAlignment = HorizontalAlignment.Left, RenderTransform = _glintShift, Opacity = 0 };
            _barFill.Children.Add(_glint);
            _barHost.Children.Add(_barFill);
            _barCap.HorizontalAlignment = HorizontalAlignment.Left; _barCap.RenderTransform = _capShift;
            _barHost.Children.Add(_barCap);
            _barHost.SizeChanged += (_, _) => { BarArt(); ShowBar(); };
            _rank.HorizontalAlignment = HorizontalAlignment.Center; _rank.VerticalAlignment = VerticalAlignment.Top; _rank.Margin = new Thickness(0, 0, 0, 0); _rank.Height = LineH;
            Grid.SetColumnSpan(_barHost, 3);
            _crown.RenderTransform = _crownScale;
            _crownHost.Children.Add(_crown);
            Put(_barHost, 0); Put(_crownHost, 0); Put(_rank, 0); Put(_iconHost, 1); Put(_line, 2); Put(_heal, 3); Put(_dps, 4); Put(_share, 5); Put(_dmg, 6); Put(_crit, 7);
            _name.TextTrimming = TextTrimming.CharacterEllipsis;
            _cp.Margin = new Thickness(5, 3, 0, 0);
            _line.Children.Add(_name); _line.Children.Add(_cp);
            _root.Children.Add(_words);
            _root.SizeChanged += (_, _) => { Fit(); _ground.InvalidateVisual(); };
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
            bool lead = m.Rank == 1 && !m.CpOnly && !m.Pvp;
            bool climbed = !fresh && c.Moves && _lastRank > 0 && m.Rank < _lastRank;
            _lastRank = m.Rank;
            if (lead != _lead || m.Class != _cls)
            {
                _lead = lead;
                // the climax panel's words are ink on white; the rest white on the rows' ink
                var main = lead ? InkB : White;
                var soft = lead ? OnPaperSoft : OnInkSoft;
                _name.Foreground = _dps.Foreground = _heal.Foreground = main;
                _cp.Foreground = _share.Foreground = _dmg.Foreground = _crit.Foreground = soft;
                _rank.Foreground = White;
                _crown.Visibility = lead ? Visibility.Visible : Visibility.Collapsed;
                _cls = m.Class;
                var icon = ThemeText.Icon(m.Class, 18, m.Name);
                if (icon is Border b) { b.BorderBrush = lead ? InkB : White; b.BorderThickness = new Thickness(1.4); }
                _iconHost.Child = icon;
                _ground.InvalidateVisual();
            }
            _rank.Text = m.Rank.ToString(CultureInfo.InvariantCulture);
            _name.Text = m.Name;
            _cp.Text = m.Cp;
            foreach (var mk in _marks) _line.Children.Remove(mk);
            _marks.Clear();
            void Add(FrameworkElement el) { el.Margin = new Thickness(5, 1, 0, 0); el.VerticalAlignment = VerticalAlignment.Top; _line.Children.Add(el); _marks.Add(el); }
            if (m.TopDps) { var t = Mark("★ " + L.T("DPS"), inverse: !lead); t.Tag = "top-dps"; t.ToolTip = L.T("Top DPS"); Add(t); }
            if (m.TopHeals) { var t = Mark("✚", inverse: !lead); t.Tag = "top-heals"; t.ToolTip = L.T("Top heals"); Add(t); }
            if (m.Deaths > 0) { var d = Tt($"✕{m.Deaths}", 10.5, RedB, FontWeights.Black); d.Tag = "deaths"; Add(d); d.Margin = new Thickness(5, 2, 0, 0); }
            if (m.You) { var y = Mark(L.T("YOU"), red: true); y.Tag = "you"; Add(y); }
            _heal.Text = m.Heal.Length > 0 ? "+" + m.Heal : "";
            _heal.Visibility = m.Heal.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            _dps.Text = m.Figure;
            _words.ColumnDefinitions[4].Width = new GridLength(Math.Max(_dpsW, TextW(_dps, m.Figure) + 4));
            _share.Text = m.ShareText;
            _dmg.Text = c.Expanded ? m.Damage : "";
            _crit.Text = c.Expanded ? m.Crit : "";
            Fit();
            var sig = m.ClassColor + "|" + lead;
            if (sig != _barSig) { _barSig = sig; BarArt(); }
            SlideBar(_barHost, c, "bar", BarOf(m, c), v => { _bar = v; ShowBar(); });
            if (climbed) Climb();
        }

        /// <summary>The crowding rule (Mod.Fit): the CP goes first, then the share; the name keeps at least ten characters.</summary>
        void Fit()
        {
            double total = _root.ActualWidth;
            if (total <= 0) return;
            var cols = _words.ColumnDefinitions;
            // (a block's measured width already holds its margin)
            double heal = _heal.Visibility == Visibility.Visible ? Mod.W(_heal) : 0;
            double room = total - 2 * Pad - cols[0].Width.Value - cols[1].Width.Value - heal - cols[4].Width.Value - cols[6].Width.Value - cols[7].Width.Value - 1;
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

        /// <summary>The bar's frozen art for its width and the member's colour: the brush body and its end's flick.</summary>
        void BarArt()
        {
            if (_m is not { } m) return;
            double w = _barHost.ActualWidth;
            if (w < 4) return;
            _barW = w;
            var c = m.ClassColor;
            bool lead = _lead;
            var fill = Frozen(c);
            var edge = lead ? Pn(InkB, 1.8) : null;
            _barBody.Art = Drawn(dc =>
            {
                // the stroke: ragged top and foot, its start frayed into the bristles' dry tail
                var body = Brushed(new[] { P(4, 1.2), P(w + 4, .7), P(w + 4, BarH - .7), P(2, BarH - .5), P(-.5, BarH * .62), P(1.5, BarH * .3) }, 23 + (int)w % 7, .55, 8);
                dc.DrawGeometry(fill, edge, body);
                var dry = Frozen(Mod.A(Mod.Mix(c, Colors.Black, .5), .85));
                var gap = lead ? PaperB : InkB;
                // the bristles' gaps at its start (the ground showing through), dry streaks along it, a light catch on its top
                for (int i = 0; i < 4; i++) { double y = 2.6 + i * (BarH - 5) / 3, len = 7 + 9 * (J(26, i) + 1) / 2; dc.DrawGeometry(gap, null, Streak(P(len + 2, y), P(-1, y), 1.3)); }
                for (int i = 0; i < 5; i++) { double x = 16 + i * w / 5 + 6 * J(24, i), len = w / 5 * (.4 + .35 * (J(25, i) + 1) / 2); dc.DrawRectangle(dry, null, new Rect(x, BarH * .52 + (i % 2) * 1.6, len, 1.1)); }
                dc.DrawRectangle(Frozen(Mod.A(Mod.Mix(c, Colors.White, .6), .9)), null, new Rect(5, 2.2, Math.Max(0, w - 6), 1.1));
            });
            _barBody.Width = w + 5; _barBody.Height = BarH; _barBody.HorizontalAlignment = HorizontalAlignment.Left;
            _barCap.Art = Drawn(dc =>
            {
                var g = new PolygonG(new[] { P(-2, .9), P(5, 1.7), P(13, BarH * .4), P(5, BarH - 1.2), P(-2, BarH - .5) }).Geo;
                dc.DrawGeometry(fill, edge, g);
            });
            _barCap.Width = 13; _barCap.Height = BarH;
            ShowBar();
        }

        void ShowBar()
        {
            double w = _barW > 0 ? _barW : _barHost.ActualWidth;
            double v = Math.Clamp(_bar, 0, 1);
            _clipScale.ScaleX = Math.Max(0, (w * v - 1)) ;
            _capShift.X = w * v - 1;
            _barCap.Visibility = v > .004 ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>The ground: nothing on an ordinary row (the panel's ink); the leader's white climax panel with its concentration lines.</summary>
        void DrawGround(DrawingContext dc, Size s)
        {
            if (!_lead || s.Width < 20) return;
            dc.DrawDrawing(FocusArt(s.Width, s.Height));
        }

        static readonly Dictionary<double, Drawing> Focus = new();

        /// <summary>The climax panel: paper, and ink lines driving in from every edge toward its middle (short where words sit, long in its corners).</summary>
        static Drawing FocusArt(double w, double h)
        {
            if (Focus.TryGetValue(w, out var d)) return d;
            d = Drawn(dc =>
            {
                dc.DrawGeometry(PaperB, null, Brushed(new[] { P(0, .5), P(w, 0), P(w - .5, h), P(.5, h - .5) }, 27, .5, 14));
                // faint concentration lines out of the rank's badge, across the whole panel, behind its words
                dc.PushClip(new RectangleGeometry(new Rect(0, 0, w, h)));
                dc.DrawGeometry(Frozen(Mod.A(InkC, .14)), null, Burst(P(Pad + 9, LineH / 2 + .5), 13, w * 1.1, 34, 77, 1.6));
                dc.Pop();
                var mid = P(w / 2, h / 2);
                var g = new StreamGeometry();
                using (var x = g.Open())
                {
                    int k = 0;
                    void Line(Point at, double len, double wide)
                    {
                        var dir = mid - at; dir.Normalize();
                        var n = new Vector(-dir.Y, dir.X) * wide / 2;
                        x.BeginFigure(at + dir * len, true, true);
                        x.LineTo(at + n, true, false); x.LineTo(at - n, true, false);
                    }
                    for (double xx = 1.5; xx < w - 1; xx += 2.6)
                    {
                        double edgeK = Math.Min(xx, w - xx) < 22 ? 1.8 : 1;
                        Line(P(xx, -.5), (2.6 + 3 * (J(28, k) + 1) / 2) * edgeK, 1.5 + .6 * J(29, k)); k++;
                        Line(P(xx, h + .5), (2.4 + 2.6 * (J(30, k) + 1) / 2) * edgeK, 1.5 + .6 * J(31, k)); k++;
                    }
                    for (double yy = 1.5; yy < h - 1; yy += 2.6)
                    {
                        Line(P(-.5, yy), 3.5 + 3.5 * (J(32, k) + 1) / 2, 1.5); k++;
                        Line(P(w + .5, yy), 4 + 4 * (J(33, k) + 1) / 2, 1.5); k++;
                    }
                    foreach (var cpt in new[] { P(0, 0), P(w, 0), P(0, h), P(w, h) })
                        for (int i = 0; i < 4; i++) Line(cpt + new Vector((cpt.X == 0 ? 1 : -1) * i * 3, 0), 9 + 2.5 * i, 2.4);
                }
                g.Freeze();
                dc.DrawGeometry(InkB, null, g);
            });
            if (Focus.Count > 24) Focus.Clear();
            Focus[w] = d;
            return d;
        }

        /// <summary>The crown landing: the red balloon behind the leader's rank slams in from big and turned (the rank itself stays still).</summary>
        public void Crown()
        {
            if (!_lead) return;
            var rot = new RotateTransform(-30);
            var tg = new TransformGroup(); tg.Children.Add(_crownScale); tg.Children.Add(rot);
            _crown.RenderTransform = tg;
            Motion.Play(_crown, 420, t =>
            {
                double k = Back(Seg(t, 0, .7), 2.4);
                _crownScale.ScaleX = _crownScale.ScaleY = 2.2 - 1.2 * k;
                rot.Angle = -30 * (1 - Out(Seg(t, 0, .7)));
                _crown.Opacity = Seg(t, 0, .12);
            }, () => { _crownScale.ScaleX = _crownScale.ScaleY = 1; rot.Angle = 0; _crown.Opacity = 1; _crown.RenderTransform = _crownScale; });
        }

        /// <summary>The bar's shimmer: a white ink glint slashes along the stroke, after <paramref name="delayMs"/>.</summary>
        public void Glint(double delayMs)
        {
            double total = 520 + delayMs, d0 = delayMs / total;
            Motion.Play(_glint, total, t =>
            {
                double k = Seg(t, d0, 1);
                _glintShift.X = -18 + (_barW * Math.Clamp(_bar, 0, 1) + 24) * Out(k);
                _glint.Opacity = k <= 0 || k >= 1 ? 0 : k < .15 ? k / .15 : 1 - Seg(k, .75, 1);
            }, () => _glint.Opacity = 0);
        }

        /// <summary>A rank won: speed lines streak under the row as it glides up to its new place.</summary>
        void Climb()
        {
            double w = _root.ActualWidth > 0 ? _root.ActualWidth : 260;
            _trail.Art = TrailArt(w);
            _trail.Width = w; _trail.Height = H + 18;
            Motion.Play(_trail, 420, t => _trail.Opacity = t < .1 ? Seg(t, 0, .1) : 1 - Seg(t, .35, 1), () => _trail.Opacity = 0);
            Glint(60);
        }

        static Drawing TrailArt(double w) => Drawn(dc =>
        {
            for (int i = 0; i < 9; i++)
            {
                double x = 14 + (w * .62) * i / 8 + 5 * J(34, i), len = 8 + 9 * (J(35, i) + 1) / 2;
                dc.DrawGeometry(Frozen(Mod.A(Colors.White, .75)), null, Streak(P(x, H + len), P(x, H - 2), 1.6));
            }
        });

        /// <summary>The glint: a slanted bar of white light, brighter at its middle.</summary>
        static readonly Drawing GlintArt = Drawn(dc =>
        {
            var g = new PolygonG(new[] { P(6, -1), P(16, -1), P(10, BarH + 1), P(0, BarH + 1) }).Geo;
            dc.DrawGeometry(Mod.Grad(false, (Mod.A(Colors.White, 0), 0), (Mod.A(Colors.White, .95), .5), (Mod.A(Colors.White, 0), 1)), null, g);
        });

        /// <summary>The leader's crown: a red explosion balloon with an ink edge, round its origin, behind the rank.</summary>
        static readonly Drawing CrownArt = Drawn(dc =>
        {
            dc.DrawGeometry(RedB, Pn(InkB, 1.4), Balloon(P(0, 0), 11.5, 7.5, 9, 91));
        });
    }
}
