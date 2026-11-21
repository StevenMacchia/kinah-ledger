using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using KinahLedger.Parser;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

sealed partial class HudTelestrator
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
    static double BarOf(MeterMember m, MeterContext c) => c.E.BarOf(m.F);

    /// <summary>The tight shade under the words on a lit bar: one effect for a whole row's words.</summary>
    static DropShadowEffect Shade() => new() { Color = Colors.Black, Direction = 270, ShadowDepth = 1, BlurRadius = 2.5, Opacity = .85 };

    /// <summary>The fight's foot under a dashed chalk line: "Total 622.4M" at the left, the group's word and figure at the right, the AVG mark on a held average.</summary>
    FrameworkElement Foot(Encounter e, DateTime now)
    {
        var (total, word, group, held) = FootOf(e, now);
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        TextBlock Pair(string w, string fig)
        {
            var t = new TextBlock { FontFamily = Mod.Face, FontSize = 10.5, FontWeight = FontWeights.Medium, Foreground = Quiet, VerticalAlignment = VerticalAlignment.Center };
            System.Windows.Documents.Typography.SetNumeralAlignment(t, FontNumeralAlignment.Tabular);
            t.Inlines.Add(new System.Windows.Documents.Run(w + " "));
            t.Inlines.Add(new System.Windows.Documents.Run(fig) { FontWeight = FontWeights.Bold, Foreground = White, FontSize = 11.5 });
            return t;
        }
        var tp = Pair(L.T("Total"), total);
        EnergyMark.After(tp, 11, Frozen(Mod.A(Colors.White, .72)));   // Odyle Energy (Ui/EnergyMark.cs)
        g.Children.Add(tp);
        var right = new StackPanel { Orientation = Orientation.Horizontal };
        if (word.Length > 0) right.Children.Add(Pair(word, group));
        if (held)
        {
            var avg = Chip(L.T("AVG"), size: 8.5); avg.Tag = "avg-badge"; avg.ToolTip = L.T(Encounter.AvgBadge); avg.Margin = new Thickness(5, 0, 0, 0);
            right.Children.Add(avg);
        }
        Grid.SetColumn(right, 2); g.Children.Add(right);
        // the pen tray between them: three markers lying at an angle, the yellow one nearest (drawn only with room to spare)
        var tray = new TacSketch((dc, s) =>
        {
            if (s.Width < 78) return;
            dc.PushTransform(new TranslateTransform(Math.Round(s.Width / 2), Math.Round(s.Height / 2)));
            dc.DrawDrawing(TrayArt);
            dc.Pop();
        });
        Grid.SetColumn(tray, 1); g.Children.Add(tray);
        return AsFoot(g, e, now);
    }

    /// <summary>The line between the party and you pinned below it: "2 MORE" between two short chalk dashes.</summary>
    static FrameworkElement GapLine(int left)
    {
        var t = Tt(left > 0 ? L.F("{0} more", left).ToUpperInvariant() : "·", 8.5, Quiet, FontWeights.Bold);
        t.HorizontalAlignment = HorizontalAlignment.Center;
        return t;
    }

    // ================================================================ the lead and the swipe

    string _leadFight = "";
    long _leadId;
    /// <summary>The highlighter swipe's moment in ms from its start (-1: none); each row takes its turn <see cref="SwipeStep"/> after the one above.</summary>
    double _swipeMs = -1;
    const double SwipeMs = 330, SwipeStep = 35;

    /// <summary>The leader's row as drawn: a new leader in the fight seen (with effects playing) is circled and the bars get a swipe.</summary>
    internal void Lead(TeleRow row, MeterMember m, MeterContext c)
    {
        bool changed = _leadFight == c.E.Id && _leadId != 0 && _leadId != m.Id;
        _leadFight = c.E.Id; _leadId = m.Id;
        if (changed && c.Moves) { row.Crown(); Swipe(); NewLead(m); }
    }

    TeleRow? LeaderRow() => Meter is { } m && m.Members.FirstOrDefault(x => x.Rank == 1 && !x.CpOnly && !x.Pvp) is { } top ? m.RowOf(top.Id) as TeleRow : null;

    /// <summary>The highlighter swipe: a yellow chisel-tip band drawn along each bar, top row first, its ink fading after it.</summary>
    void Swipe()
    {
        int n = Math.Max(1, Meter?.Shown ?? 1);
        double total = SwipeMs + SwipeStep * (n - 1);
        Motion.Play(_swipeHost, total, t => { _swipeMs = t * total; RedrawRows(); }, () => { _swipeMs = -1; RedrawRows(); });
    }

    /// <summary>The swipe's point (0 to 1) on the row at <paramref name="i"/> from the top; -1 with none under way.</summary>
    internal double SwipeAt(int i) => _swipeMs < 0 ? -1 : Math.Clamp((_swipeMs - SwipeStep * i) / SwipeMs, 0, 1);

    void RedrawRows()
    {
        if (Meter is not { } m) return;
        foreach (var x in m.Members) if (m.RowOf(x.Id) is TeleRow r) r.SwipeTo(SwipeAt(r.Index));
    }

    /// <summary>The pen tray: three marker silhouettes (a body, a darker cap band, a chisel tip), tilted, centred on 0,0; drawn once.</summary>
    static readonly DrawingGroup TrayArt = MakeTray();

    static DrawingGroup MakeTray()
    {
        var dg = new DrawingGroup();
        using (var dc = dg.Open())
        {
            dc.DrawRectangle(Mod.Br(Colors.White, .16), null, new Rect(-27, 5, 54, 1));
            var cols = new[] { PinkC, PenMintC, PenC };
            for (int k = 0; k < 3; k++)
            {
                double x = -17 + k * 14, y = k == 2 ? -1 : 0;
                dc.PushTransform(new RotateTransform(-20, x, y));
                var body = Mod.Pill(new Rect(x - 8, y - 2, 13, 4), 1.6);
                dc.DrawGeometry(Mod.Br(Colors.Black, .5), Mod.P(Mod.Br(Colors.Black, .5), 1.6), body);
                dc.DrawGeometry(Frozen(cols[k]), null, body);
                dc.DrawRectangle(Frozen(Mod.Mix(cols[k], Colors.Black, .45)), null, new Rect(x - 8, y - 2, 3.5, 4));
                var tip = new StreamGeometry();
                using (var g = tip.Open()) { g.BeginFigure(new Point(x + 5, y - 2), true, true); g.LineTo(new Point(x + 8.5, y - .6), true, false); g.LineTo(new Point(x + 8.5, y + 1.2), true, false); g.LineTo(new Point(x + 5, y + 2), true, false); }
                tip.Freeze();
                dc.DrawGeometry(Frozen(Mod.Mix(cols[k], Colors.White, .35)), null, tip);
                dc.Pop();
            }
        }
        dg.Freeze();
        return dg;
    }

    static readonly SolidColorBrush LaneBr = Mod.Br(Colors.Black, .3), BarTopBr = Mod.Br(Colors.White, .07), StripeBr = Frozen(Color.FromArgb(0xD0, 0xFF, 0xEC, 0x7A)), SwipeEdgeBr = Frozen(Color.FromRgb(0xFF, 0xE1, 0x4A)), PlayheadBr = Frozen(Colors.White);

    /// <summary>
    /// One member on the board: a dark lane the width of the meter, the bar in the member's class colour, flat and clean, as
    /// long as their part of the top, its end marked at its top and foot (a playhead clear of the words). On it in crisp
    /// white: the rank, the class's art, the name, Combat Power small, the marks (★ DPS on a highlighter label, a healer's ✚,
    /// deaths, YOU on a pink label), a healer's healing a second after a scribbled plus, the DPS big and the share small at
    /// the right. Over it, the caster's pen: the leader's rank and art circled in yellow, a quick pink underline under your
    /// name, an arrow drawn up beside a row that just climbed. Short of room the CP goes first, then the share; a name keeps
    /// at least ten characters; a figure is never cut (Mod.Fit). No stroke ever crosses a figure.
    /// Light by design: brushes made once per class, the pen's marks made once and replayed, the marks after the name built
    /// again only when they change; the swipe and the climb's arrow are their own small parts, moved by scale and opacity.
    /// </summary>
    internal sealed class TeleRow : IThemeRow
    {
        public const double H = 26, Pitch = 29;
        const double Pad = 8;
        readonly HudTelestrator _look;
        readonly Grid _root = new() { Height = H };
        readonly TacSketch _bar, _ink, _swipe, _arrow;
        readonly TranslateTransform _swipeShift = new(-30, 0);
        readonly Grid _words = new() { Margin = new Thickness(Pad, 0, Pad, 0) };
        readonly TextBlock _rank = Tt("", 12, White, FontWeights.Bold), _name = Tt("", 12.5, White, FontWeights.Bold), _cp = Tt("", 9.5, Frozen(Mod.A(Colors.White, .78)), FontWeights.SemiBold),
            _heal = Tt("", 10.5, Mint2, FontWeights.Bold), _dps = Tt("", 15.5, White, FontWeights.Bold), _share = Tt("", 10, Frozen(Mod.A(Colors.White, .86)), FontWeights.SemiBold),
            _dmg = Tt("", 10.5, Soft, FontWeights.SemiBold), _crit = Tt("", 10, Soft, FontWeights.SemiBold);
        readonly StackPanel _line = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
        readonly StackPanel _healBox = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 4, 0) };
        readonly Border _iconHost = new() { Width = 17, Height = 17, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
        readonly List<FrameworkElement> _marks = new();
        readonly double _dpsW, _shareW, _dmgW, _critW;
        string _cls = "\0", _markSig = "";
        Brush _fillBr = Brushes.Transparent, _headBr = Brushes.Transparent;
        MeterMember? _m;
        double _barV, _crown = 1, _climb = -1;
        int _wasRank, _seed;
        bool _expanded, _shareOn = true, _lead, _you, _swipeOn;

        public FrameworkElement Root => _root;
        /// <summary>Where the DPS bar is drawn (IThemeBarSpan, for the PB ghost): a full bar fills the whole lane.</summary>
        public (FrameworkElement On, Rect Track)? BarTrack => _bar.ActualWidth <= 0 ? null : (_bar, new Rect(0, 0, _bar.ActualWidth, _bar.ActualHeight));
        /// <summary>The row's place from the top (its turn in the swipe).</summary>
        public int Index { get; private set; }

        public TeleRow(HudTelestrator look)
        {
            _look = look;
            _dps.Tag = "row-dps"; _share.Tag = "row-share"; _heal.Tag = "heal-figures"; _cp.Tag = "cp";
            _dps.HorizontalAlignment = _share.HorizontalAlignment = _dmg.HorizontalAlignment = _crit.HorizontalAlignment = HorizontalAlignment.Right;
            _share.Margin = new Thickness(0, 2, 0, 0); _dmg.Margin = new Thickness(0, 1, 0, 0); _crit.Margin = new Thickness(0, 1, 0, 0);
            // the healer's note: a scribbled plus, then the figure
            var plus = new TacSketch((dc, s) => Plus(dc, new Point(.5, .5), 9, PenMintC, 11, 1.9), 10.5, 10.5) { Margin = new Thickness(0, 0, 3, 0), VerticalAlignment = VerticalAlignment.Center };
            _healBox.Children.Add(plus); _healBox.Children.Add(_heal);
            // the columns: rank, the class art, the name's line (the rest), heal, the DPS, the share; expanded, the damage and the crit rate
            _dpsW = FigCol(_dps, "888.8K") + 4; _shareW = FigCol(_share, "88.8%", "100%", L.T("CP")) + 6;
            _dmgW = FigCol(_dmg, "888.88M") + 8; _critW = FigCol(_crit, "88%") + 8;
            foreach (var w in new[] { 17.0, 25, double.NaN, 0, _dpsW, _shareW, 0, 0 })
                _words.ColumnDefinitions.Add(new ColumnDefinition { Width = double.IsNaN(w) ? new GridLength(1, GridUnitType.Star) : w == 0 ? GridLength.Auto : new GridLength(w) });
            _words.ColumnDefinitions[6].Width = new GridLength(0); _words.ColumnDefinitions[7].Width = new GridLength(0);
            _bar = new TacSketch(DrawBar);
            _swipe = new TacSketch(DrawSwipe) { RenderTransform = _swipeShift, Visibility = Visibility.Collapsed };
            _ink = new TacSketch(DrawInk);
            _arrow = new TacSketch(DrawArrow) { Visibility = Visibility.Collapsed };
            _root.Children.Add(_bar); _root.Children.Add(_swipe);
            _rank.HorizontalAlignment = HorizontalAlignment.Center;
            Put(_rank, 0); Put(_iconHost, 1); Put(_line, 2); Put(_healBox, 3); Put(_dps, 4); Put(_share, 5); Put(_dmg, 6); Put(_crit, 7);
            _name.TextTrimming = TextTrimming.CharacterEllipsis;
            _cp.Margin = new Thickness(5, 2, 0, 0);
            _line.Children.Add(_name); _line.Children.Add(_cp);
            _words.Effect = Shade();
            _root.Children.Add(_words);
            _root.Children.Add(_ink);
            _root.Children.Add(_arrow);
            _root.SizeChanged += (_, _) => Fit();
        }

        void Put(FrameworkElement el, int col) { Grid.SetColumn(el, col); _words.Children.Add(el); }

        public void Set(MeterMember m, MeterContext c, bool fresh)
        {
            _m = m;
            _seed = SeedOf(m.Id);
            Index = Math.Max(0, Math.Min(m.Rank, c.Count) - 1);
            if (!fresh && c.Moves && _wasRank > 0 && m.Rank < _wasRank) Climb();
            _wasRank = m.Rank;
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
                var icon = ThemeText.Icon(m.Class, 17, m.Name);
                if (icon is Border b) { b.BorderBrush = Frozen(Mod.A(Colors.White, .7)); b.BorderThickness = new Thickness(1); }
                _iconHost.Child = icon;
                _fillBr = Mod.Br(Mod.Mix(m.ClassColor, BoardDeepC, .3), .94);
                _headBr = Frozen(Mod.Mix(m.ClassColor, Colors.White, .5));
                _bar.InvalidateVisual();
            }
            _name.Text = m.Name;
            _cp.Text = m.Cp;
            // the marks after the name: ★ DPS, a healer's ✚, deaths, YOU (kept whole: the name and the CP give way first); built again only when they change
            var sig = $"{m.TopDps}|{m.TopHeals}|{m.Deaths}|{m.You}";
            if (sig != _markSig)
            {
                _markSig = sig;
                foreach (var mk in _marks) _line.Children.Remove(mk);
                _marks.Clear();
                void Add(FrameworkElement el) { el.Margin = new Thickness(5, 0, 0, 0); _line.Children.Add(el); _marks.Add(el); }
                if (m.TopDps) { var t = Chip("★ " + L.T("DPS"), PenC); t.Tag = "top-dps"; t.ToolTip = L.T("Top DPS"); Add(t); }
                if (m.TopHeals) { var t = Chip("✚", PenMintC); t.Tag = "top-heals"; t.ToolTip = L.T("Top heals"); Add(t); }
                if (m.Deaths > 0) { var d = Tt($"✕{m.Deaths}", 10, Coral, FontWeights.Bold); d.Tag = "deaths"; Add(d); }
                if (m.You) { var y = Chip(L.T("YOU"), PinkC, light: true); y.Tag = "you"; Add(y); }
            }
            _heal.Text = m.Heal;
            _healBox.Visibility = m.Heal.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            _dps.Text = m.Figure;
            // a wider figure (Combat Power before the first hit, "167,548") widens its column: never a digit cut
            double dw = Math.Max(_dpsW, TextW(_dps, m.Figure) + 4);
            if (Math.Abs(_words.ColumnDefinitions[4].Width.Value - dw) > .1) _words.ColumnDefinitions[4].Width = new GridLength(dw);
            _share.Text = m.ShareText;
            _dmg.Text = c.Expanded ? m.Damage : "";
            _crit.Text = c.Expanded ? m.Crit : "";
            bool lead = m.Rank == 1 && !m.CpOnly && !m.Pvp && c.E.HasDamage;
            if (lead != _lead || m.You != _you) { _lead = lead; _you = m.You; _ink.InvalidateVisual(); }
            Fit();
            SlideBar(_bar, c, "bar", BarOf(m, c), v => { _barV = v; _bar.InvalidateVisual(); });
            if (_lead) _look.Lead(this, m, c);
        }

        /// <summary>The crowding rule (Mod.Fit): the CP goes first, then the share; the name keeps at least ten characters.</summary>
        void Fit()
        {
            double total = _root.ActualWidth;
            if (total <= 0) return;
            var cols = _words.ColumnDefinitions;
            double heal = _healBox.Visibility == Visibility.Visible ? Mod.W(_healBox) + _healBox.Margin.Left : 0;
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
            if (_you) _ink.InvalidateVisual();   // the underline follows the name's width (its geometry is cached by place)
        }

        /// <summary>The leader's circle drawn on (a new leader, a fight starting).</summary>
        internal void Crown()
        {
            Motion.Play(_ink, 560, t => { _crown = Out(Seg(t, 0, .9)); _ink.InvalidateVisual(); }, () => { _crown = 1; _ink.InvalidateVisual(); });
        }

        /// <summary>The row climbed a rank: the pen draws an arrow up beside its name (redrawn only while it is drawn on), holds it, and it fades by opacity.</summary>
        void Climb()
        {
            _arrow.Visibility = Visibility.Visible; _arrow.Opacity = 1;
            Motion.Play(_arrow, 1700, t =>
            {
                if (t <= .32 || _climb < .3) { _climb = t; _arrow.InvalidateVisual(); }
                _arrow.Opacity = 1 - Seg(t, .75, 1);
            }, () => { _climb = -1; _arrow.Visibility = Visibility.Collapsed; });
        }

        /// <summary>The swipe on this row at its point (0 to 1; -1 none): its band scaled out along the bar, fading after it passes.</summary>
        internal void SwipeTo(double sw)
        {
            if (sw is <= 0 or >= 1)
            {
                if (_swipeOn) { _swipeOn = false; _swipe.Visibility = Visibility.Collapsed; }
                return;
            }
            if (!_swipeOn)
            {
                _swipeOn = true;
                _swipe.Visibility = Visibility.Visible;
                _swipe.InvalidateVisual();
            }
            // the stripe travels the bar's length (and a little past its end), fading in and out at its two ends
            double run = Math.Max(Math.Round((_root.ActualWidth > 0 ? _root.ActualWidth : 300) * Math.Clamp(_barV, 0, 1)), 60) + 8;
            _swipeShift.X = -24 + (run + 24) * Out(sw);
            _swipe.Opacity = Math.Min(Seg(sw, 0, .12), 1 - Seg(sw, .8, 1));
        }

        void DrawBar(DrawingContext dc, Size s)
        {
            if (_m == null || s.Width < 20) return;
            // the lane: the meter's width, a dark well on the board
            dc.DrawRoundedRectangle(LaneBr, null, new Rect(0, 0, s.Width, s.Height), 3, 3);
            double fx = Math.Round(s.Width * Math.Clamp(_barV, 0, 1));
            if (fx > .5)
            {
                // the bar: the class's colour, flat, a touch deep so white reads on it
                dc.DrawRoundedRectangle(_fillBr, null, new Rect(0, 0, fx, s.Height), 3, 3);
                if (fx > 5) dc.DrawRectangle(BarTopBr, null, new Rect(2, 0, fx - 4, 1));
                // the playhead: the bar's end marked at its top and foot only, clear of the words between
                if (fx > 4)
                {
                    // a scrubber's head: a white cap pointing down onto the bar's end, a stub at its foot
                    dc.DrawGeometry(PlayheadBr, null, PlayCap(fx - 1));
                    dc.DrawRectangle(PlayheadBr, null, new Rect(fx - 2, 0, 2, 4.5));
                    dc.DrawRectangle(PlayheadBr, null, new Rect(fx - 2, s.Height - 4.5, 2, 6.5));
                }
            }
        }

        /// <summary>The scrubber head's cap: a 6x4 triangle sitting just over the bar, its point on the bar's end.</summary>
        static Geometry PlayCap(double x)
        {
            var g = new StreamGeometry();
            using (var c = g.Open()) { c.BeginFigure(new Point(x - 3.5, -3), true, true); c.LineTo(new Point(x + 3.5, -3), true, false); c.LineTo(new Point(x, 1.5), true, false); }
            g.Freeze();
            return g;
        }

        /// <summary>The highlighter's band along the bar, drawn once as a swipe starts (a chisel tip); scaled out by the swipe.</summary>
        void DrawSwipe(DrawingContext dc, Size s)
        {
            // a highlighter's light run along the bar's top and foot edges (clear of the words between): a 24 px slanted streak
            // of yellow at each edge with a solid leading tip, moved along the bar by its transform (it leaves nothing behind)
            double h = s.Height;
            foreach (var (y0, y1) in new[] { (0.0, 4.5), (h - 4.5, h) })
            {
                dc.DrawGeometry(StripeBr, null, Stripe(0, 24, y0, y1, 1.5));
                dc.DrawGeometry(SwipeEdgeBr, null, Stripe(20, 24, y0, y1, 1.5));
            }
        }

        static Geometry Stripe(double x0, double x1, double y0, double y1, double slant)
        {
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(new Point(x0 + slant, y0), true, true);
                c.LineTo(new Point(x1 + slant, y0), true, false);
                c.LineTo(new Point(x1, y1), true, false);
                c.LineTo(new Point(x0, y1), true, false);
            }
            g.Freeze();
            return g;
        }

        void DrawInk(DrawingContext dc, Size s)
        {
            if (_m == null || s.Width < 20) return;
            // the leader: rank and art circled in yellow marker (a few px into the gap round the row); the whole loop made once
            if (_lead && _crown > 0)
            {
                var rect = new Rect(.5, -4.5, Pad + 17 + 24 - 1, H + 9);
                int seed = _seed;
                Ink(dc, _crown >= 1 ? Cached($"crown|{seed}", () => Poly(LoopPts(rect, seed))) : Poly(LoopPts(rect, seed), _crown), PenC, 2.6);
            }
            // you: a quick pink underline under your name (made again only when the name's place or width changes)
            if (_you && _name.ActualWidth > 4 && _name.IsVisible)
            {
                var p = _name.TranslatePoint(new Point(0, 0), _root);
                double y = Math.Round(p.Y + _name.ActualHeight + 1, 1), x = Math.Round(p.X, 1), w = Math.Round(_name.ActualWidth, 1);
                int seed = _seed;
                Ink(dc, Cached($"you|{seed}|{x}|{y}|{w}", () => Poly(LinePts(new Point(x - 1, y + .6), new Point(x + w + 1, y - .9), -.8, seed + 5, .35))), PinkC, 2.2);
            }
        }

        /// <summary>The climb's arrow, drawn up in the room between the name's line and the figures.</summary>
        void DrawArrow(DrawingContext dc, Size s)
        {
            if (_climb < 0 || _line.ActualWidth <= 0) return;
            double from = _line.TranslatePoint(new Point(_line.ActualWidth, 0), _root).X + 6;
            double to = _healBox.Visibility == Visibility.Visible ? _healBox.TranslatePoint(new Point(0, 0), _root).X - 6
                : _dps.TranslatePoint(new Point(_dps.ActualWidth - TextW(_dps, _dps.Text), 0), _root).X - 6;
            if (to - from < 16) return;
            double x = from + Math.Min(8, (to - from - 14) / 2);
            Arrow(dc, LinePts(new Point(x, H - 4), new Point(x + 11, 3.5), 3.5, _seed + 9, .3), PenC, Out(Seg(_climb, 0, .3)), 1, 2.4, 5);
        }
    }
}
