using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using KinahLedger.Parser;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

sealed partial class HudDragon
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

    /// <summary>The fight's foot: "Total 622.4M" at the left, the group's word and figure at the right, the AVG mark on a held average.</summary>
    FrameworkElement Foot(Encounter e, DateTime now)
    {
        var (total, word, group, held) = FootOf(e, now);
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        TextBlock Pair(string w, string fig)
        {
            var t = new TextBlock { FontFamily = Mod.Face, FontSize = 11, FontWeight = FontWeights.Medium, Foreground = BronzeInk, VerticalAlignment = VerticalAlignment.Center };
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
        return AsFoot(Lit(g), e, now);
    }

    /// <summary>The line between the party and you pinned below it: "2 MORE" in bronze between two short rules.</summary>
    static FrameworkElement GapLine(int left)
    {
        var t = Tt(left > 0 ? L.F("{0} more", left).ToUpperInvariant() : "·", 9, BronzeInk, FontWeights.SemiBold);
        t.HorizontalAlignment = HorizontalAlignment.Center;
        var lit = Lit(t);
        lit.HorizontalAlignment = HorizontalAlignment.Center;
        return lit;
    }

    /// <summary>
    /// One member as the dragon's fire: a stream in their class's colour out of the coil's side for their part of the top,
    /// darker at its source, a hot core running along it and a flame front of three tongues at its end (the leader's
    /// rimmed in gold). On it, in white with a tight shade: the rank on a scale crest (molten gold for the leader), the
    /// class's art, the name, Combat Power small, the marks (★ DPS, a healer's ✚, deaths, YOU), a healer's healing a second
    /// in mint, the DPS big and the share small at the right. You: an ivory talon at the stream's source. Short of room the
    /// CP goes first, then the share; a name keeps at least ten characters; a figure is never cut (Mod.Fit).
    /// Moments: fire racing down the bar (a band of hot gold, under the words), a breath bursting along it, the crest landing.
    /// </summary>
    internal sealed class DragonRow : IThemeRow, IThemeBarSpan
    {
        public const double H = 26, Pitch = 29;
        static readonly LinearGradientBrush NameBand = Mod.Grad(false, (Mod.A(Colors.Black, .34), 0), (Mod.A(Colors.Black, .26), .6), (Mod.A(Colors.Black, 0), 1));
        static readonly Geometry Talon = G("M 0,3 C 3.5,6 4.8,10 4.6,13 C 4.4,17 3,20.5 0,23 Z");
        const double Pad = 3;
        readonly HudDragon _look;
        readonly Grid _root = new() { Height = H };
        readonly TacSketch _art;
        readonly Canvas _fx = new() { IsHitTestVisible = false };
        readonly RectangleGeometry _fxClip = new(new Rect(0, 0, 0, H));
        readonly Rectangle _race = new() { Width = 56, Height = H - 4, Opacity = 0, IsHitTestVisible = false };
        readonly TranslateTransform _raceAt = new();
        readonly Rectangle _burst = new() { Width = 150, Height = H, Fill = Burst, Opacity = 0, IsHitTestVisible = false, RenderTransformOrigin = new Point(0, .5) };
        readonly ScaleTransform _burstScale = new(0, 1);
        readonly Grid _words = new() { Margin = new Thickness(Pad, 0, 5, 0) };
        readonly Grid _crestHost = new() { Width = 18, Height = H, HorizontalAlignment = HorizontalAlignment.Center };
        readonly TacSketch _crest;
        readonly ScaleTransform _crestPop = new(1, 1, 9, H / 2);
        readonly TextBlock _rank = Tt("", 10.5, White, FontWeights.Bold), _name = Tt("", 12.5, White, FontWeights.Bold), _cp = Tt("", 9, Frozen(Mod.A(Colors.White, .86)), FontWeights.SemiBold),
            _heal = Tt("", 10.5, Mint2, FontWeights.Bold), _dps = Tt("", 15.5, White, FontWeights.Bold), _share = Tt("", 10, Frozen(Mod.A(Colors.White, .86)), FontWeights.SemiBold),
            _dmg = Tt("", 10.5, Soft, FontWeights.SemiBold), _crit = Tt("", 10, Soft, FontWeights.SemiBold);
        readonly StackPanel _line = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        /// <summary>The CP on a small dark plate of its own, so it reads on the fire's front as on the dark.</summary>
        readonly Border _cpBox = new() { Background = Frozen(Mod.A(Color.FromRgb(0x08, 0x06, 0x0A), .62)), CornerRadius = new CornerRadius(3), Padding = new Thickness(3, 0, 3, 0), Margin = new Thickness(4, 2, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        readonly Border _iconHost = new() { Width = 17, Height = 17, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
        readonly List<FrameworkElement> _marks = new();
        readonly double _dpsW, _shareW, _dmgW, _critW;
        string _cls = "\0";
        MeterMember? _m;
        double _bar;
        int _lastRank;
        bool _expanded, _shareOn = true, _lead;

        public FrameworkElement Root => _root;

        /// <summary>Where a full bar's fire runs (the PB ghost's track, IThemeBarSpan on main): the row up to its figures, inside the channel.</summary>
        public (FrameworkElement On, Rect Track)? BarTrack => _art.ActualWidth <= 0 ? null : (_art, new Rect(0, 2, FireW(_art.ActualWidth), Math.Max(0, _art.ActualHeight - 4)));

        public DragonRow(HudDragon look)
        {
            _look = look;
            _dps.Tag = "row-dps"; _share.Tag = "row-share"; _heal.Tag = "heal-figures"; _cp.Tag = "cp";
            _dps.HorizontalAlignment = _share.HorizontalAlignment = _dmg.HorizontalAlignment = _crit.HorizontalAlignment = HorizontalAlignment.Right;
            _share.Margin = new Thickness(0, 2, 0, 0); _dmg.Margin = new Thickness(0, 1, 0, 0); _crit.Margin = new Thickness(0, 1, 0, 0);
            _heal.Margin = new Thickness(5, 1, 0, 0);
            // the columns: the crest, the class art, the name's line (the rest), heal, the DPS, the share; expanded, the damage and the crit rate
            _dpsW = FigCol(_dps, "888.8K") + 3; _shareW = FigCol(_share, "88.8%", "100%", L.T("CP")) + 5;
            _dmgW = FigCol(_dmg, "888.88M") + 8; _critW = FigCol(_crit, "88%") + 8;
            foreach (var w in new[] { 19.0, 22, double.NaN, 0, _dpsW, _shareW, 0, 0 })
                _words.ColumnDefinitions.Add(new ColumnDefinition { Width = double.IsNaN(w) ? new GridLength(1, GridUnitType.Star) : w == 0 ? GridLength.Auto : new GridLength(w) });
            _words.ColumnDefinitions[6].Width = new GridLength(0); _words.ColumnDefinitions[7].Width = new GridLength(0);
            _art = new TacSketch(Draw);
            _root.Children.Add(_art);
            // the moments' layer: under the words, clipped to the bar
            _race.Fill = RaceBrush; _race.RenderTransform = _raceAt; Canvas.SetTop(_race, 2);
            _burst.RenderTransform = _burstScale;
            _fx.Clip = _fxClip;
            _fx.Children.Add(_burst); _fx.Children.Add(_race);
            _root.Children.Add(_fx);
            // the rank on its crest
            _crest = new TacSketch((dc, s) => Crest(dc, new Point(s.Width / 2, s.Height / 2 + .5), 15.5, _lead));
            _crestHost.Children.Add(_crest);
            _rank.HorizontalAlignment = HorizontalAlignment.Center; _rank.Margin = new Thickness(0, 0, 0, 1);
            _crestHost.Children.Add(_rank);
            _crestHost.RenderTransform = _crestPop;
            Put(_crestHost, 0); Put(_iconHost, 1); Put(_line, 2); Put(_heal, 3); Put(_dps, 4); Put(_share, 5); Put(_dmg, 6); Put(_crit, 7);
            _iconHost.Margin = new Thickness(3, 0, 0, 0);
            _name.TextTrimming = TextTrimming.CharacterEllipsis;
            _cp.Margin = new Thickness(0, 0, 0, 0);
            _cpBox.Child = _cp;
            _line.Children.Add(_name); _line.Children.Add(_cpBox);
            _line.Margin = new Thickness(4, 0, 0, 0);
            _root.Children.Add(Lit(_words));
            _root.SizeChanged += (_, _) => { Fit(); Clip(); };
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
            bool lead = m is { Rank: 1, CpOnly: false, Pvp: false };
            if (lead != _lead) { _lead = lead; _crest.InvalidateVisual(); }
            _rank.Text = m.Rank.ToString(CultureInfo.InvariantCulture);
            _rank.Foreground = lead ? Frozen(Color.FromRgb(0x2A, 0x14, 0x02)) : White;
            _rank.Effect = null;
            if (m.Class != _cls)
            {
                _cls = m.Class;
                var icon = ThemeText.Icon(m.Class, 17, m.Name);
                if (icon is Border b) { b.BorderBrush = Frozen(Mod.A(Color.FromRgb(0xE2, 0xC4, 0x8A), .85)); b.BorderThickness = new Thickness(1); }
                _iconHost.Child = icon;
            }
            _name.Text = m.Name;
            _cp.Text = m.Cp;
            // the marks after the name: ★ DPS, a healer's ✚, deaths, YOU (kept whole: the name and the CP give way first)
            foreach (var mk in _marks) _line.Children.Remove(mk);
            _marks.Clear();
            void Add(FrameworkElement el) { el.Margin = new Thickness(5, 0, 0, 0); _line.Children.Add(el); _marks.Add(el); }
            if (m.TopDps) { var t = Mark("★ " + L.T("DPS"), GoldInk, DragonGoldC); t.Tag = "top-dps"; t.ToolTip = L.T("Top DPS"); Add(t); }
            if (m.TopHeals) { var t = Mark("✚", EmeraldInk, EmeraldC); t.Tag = "top-heals"; t.ToolTip = L.T("Top heals"); Add(t); }
            if (m.Deaths > 0) { var d = Tt($"✕{m.Deaths}", 10, Coral, FontWeights.Bold); d.Tag = "deaths"; Add(d); }
            if (m.You) { var y = Mark(L.T("YOU"), filled: true); y.Tag = "you"; Add(y); }
            _heal.Text = m.Heal.Length > 0 ? "+" + m.Heal : "";
            _heal.Visibility = m.Heal.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            _dps.Text = m.Figure;
            // a wider figure (Combat Power before the first hit, "167,548") widens its column: never a digit cut
            _words.ColumnDefinitions[4].Width = new GridLength(Math.Max(_dpsW, TextW(_dps, m.Figure) + 3));
            _share.Text = m.ShareText;
            _dmg.Text = c.Expanded ? m.Damage : "";
            _crit.Text = c.Expanded ? m.Crit : "";
            Fit();
            SlideBar(_art, c, "bar", BarOf(m, c), v => { _bar = v; _art.InvalidateVisual(); Clip(); });
            _art.InvalidateVisual();
            // the leader (a new one breathes fire); a row that climbed glints along its fire
            if (lead) _look.Lead(this, m, c);
            if (!fresh && c.Moves && _lastRank > 0 && m.Rank < _lastRank && !lead) Glint();
            _lastRank = m.Rank;
        }

        /// <summary>The crowding rule (Mod.Fit): the CP goes first, then the share; the name keeps at least ten characters.</summary>
        void Fit()
        {
            double total = _root.ActualWidth;
            if (total <= 0) return;
            var cols = _words.ColumnDefinitions;
            double heal = _heal.Visibility == Visibility.Visible ? Mod.W(_heal) + _heal.Margin.Left : 0;
            double room = total - Pad - 5 - cols[0].Width.Value - cols[1].Width.Value - _line.Margin.Left - heal - cols[4].Width.Value - cols[6].Width.Value - cols[7].Width.Value - 2;
            double min = _name.Text.Length <= NameKeep ? TextW(_name, _name.Text) : TextW(_name, _name.Text[..NameKeep] + "…") + 1;
            var (cp, share) = Mod.Fit(_name, room, _cp.Text.Length > 0 ? _cpBox : null, _marks, _shareW, min);
            _cpBox.Visibility = cp && _cp.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (share != _shareOn || cols[5].Width.Value != (share ? _shareW : 0))
            {
                _shareOn = share;
                cols[5].Width = new GridLength(share ? _shareW : 0);
            }
            _share.Visibility = share ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>The fire's room: the row up to its figures (the DPS and the share sit on the dark at its end).</summary>
        double FireW(double w)
        {
            var cols = _words.ColumnDefinitions;
            return Math.Max(20, w - 5 - cols[4].Width.Value - cols[5].Width.Value - cols[6].Width.Value - cols[7].Width.Value - 2);
        }

        /// <summary>The bar's end, px from the row's left.</summary>
        double Fx => Math.Max(0, FireW(_root.ActualWidth) * Math.Clamp(_bar, 0, 1));

        /// <summary>The moments' layer clipped to the fire (and a little past its front).</summary>
        void Clip() => _fxClip.Rect = new Rect(0, 0, Math.Min(_root.ActualWidth, Fx + 4), H);

        void Draw(DrawingContext dc, Size s)
        {
            if (_m is not { } m || s.Width < 20) return;
            double room = FireW(s.Width), fx = room * Math.Clamp(_bar, 0, 1);
            // the track: a darker channel the fire runs in, the figures' end darker still
            dc.DrawRoundedRectangle(Frozen(Mod.A(Colors.Black, .3)), null, new Rect(0, 2, s.Width, s.Height - 4), 3, 3);
            dc.DrawRectangle(Mod.Grad(false, (Mod.A(Colors.Black, 0), 0), (Mod.A(Colors.Black, .3), 1)), null, new Rect(room - 10, 2, s.Width - room + 10, s.Height - 4));
            if (fx > .5) Fire(dc, new Rect(0, 2, room, s.Height - 4), Math.Max(fx, 6), m.ClassColor, _lead, m.Id);
            if (fx > 24) dc.DrawRectangle(NameBand, null, new Rect(0, 2, Math.Min(fx - 10, 170), s.Height - 4));
            // you: an ivory talon at the stream's source
            if (m.You) dc.DrawGeometry(Mod.Grad(true, (Color.FromRgb(0xFF, 0xF8, 0xE6), 0), (IvoryC, .5), (Color.FromRgb(0x9C, 0x8A, 0x66), 1)), Mod.P(Frozen(Color.FromArgb(0xCC, 0x10, 0x0C, 0x08)), .6),
                Talon);
        }

        /// <summary>Fire racing down the bar at <paramref name="t"/> (0 to 1; outside it, gone), gold on a kill.</summary>
        internal void Race(double t, bool gold)
        {
            if (t <= 0 || t >= 1) { _race.Opacity = 0; return; }
            double end = Fx + 4;
            _raceAt.X = -_race.Width + (end + _race.Width) * Motion.Out(t);
            _race.Opacity = (gold ? 1 : .85) * (t < .85 ? 1 : 1 - (t - .85) / .15);
        }

        /// <summary>A breath of fire bursting along the bar at <paramref name="t"/> (the new leader's), the crest landing with it.</summary>
        internal void Breath(double t)
        {
            Blaze(t);
            Crown(t < 0 || t >= 1 ? -1 : Math.Clamp((t - .15) / .5, 0, 1));
        }

        /// <summary>The burst's length set once before a moment (never each frame): the fire's length and a little past.</summary>
        internal void Arm(bool ash = false) { _burst.Width = Math.Max(60, Fx + 12); _burst.Fill = ash ? BurstAsh : Burst; }

        /// <summary>A burst of fire pouring along the bar from its source at <paramref name="t"/>, hot gold running out to its front.</summary>
        internal void Blaze(double t)
        {
            if (t <= 0 || t >= 1) { _burst.Opacity = 0; _burstScale.ScaleX = 0; Race(-1, false); return; }
            _burstScale.ScaleX = Motion.Out(Math.Clamp(t / .4, 0, 1));
            _burst.Opacity = t < .45 ? .95 : .95 * (1 - (t - .45) / .55);
            Race(Math.Clamp((t - .1) / .7, 0, 1), true);
        }

        /// <summary>The crest landing at <paramref name="t"/>: dropped in large, overshooting, settling.</summary>
        internal void Crown(double t)
        {
            if (t <= 0 || t >= 1) { _crestPop.ScaleX = _crestPop.ScaleY = 1; return; }
            double k = Back(t, 2.8);
            _crestPop.ScaleX = _crestPop.ScaleY = 1.9 - .9 * k;
        }

        /// <summary>A row that climbed: a quick run of fire along it.</summary>
        void Glint() => Motion.Play(_race, 520, t => Race(t, false), () => Race(-1, false));
    }
}
