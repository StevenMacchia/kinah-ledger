using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

sealed partial class HudScorebug
{
    /// <summary>
    /// One member as a broadcast leaderboard row (Steven, 2026-11-17: "better looks that doesn't crowd everything and
    /// prioritizes the bar"): a rank tab (gold, silver, bronze for the podium), the class's colour as the team stripe, and the
    /// name plate as the bar: filled in the class's colour to the member's part of the top, a bright strip along its foot. On it:
    /// the class emblem, the name and its marks, Combat Power small under the name (and a healer's healing a second). Then the
    /// stat cells: the DPS big, the share small (the damage and crit when expanded). You: the plate goes light and your DPS cell
    /// red, the way a broadcast features its player. Short of room a plate goes to its glyph, then YOU, the emblem, then the CP
    /// (the skills card says them all): never a figure cut. Every shape is drawn in one pass; the text sits on top.
    /// </summary>
    sealed class ScoreRow : IThemeRow
    {
        public const double H = 38, Pitch = 41;
        const double Sl = H * .1763, Gap = 2.5;
        static readonly Color PlateC = Color.FromRgb(0x17, 0x1A, 0x23), DpsC = Color.FromRgb(0x0A, 0x0C, 0x11), PctC = Color.FromRgb(0x20, 0x24, 0x2E);
        readonly Grid _root = new() { Height = H };
        readonly Grid _text = new();
        readonly TacSketch _art;
        readonly TextBlock _rank = T("", 18, InkB), _name = T("", 15.5, White), _dps = T("", 17.5, White), _pct = T("", 13, Grey, FontWeights.SemiBold),
            _cp = T("", 11, Frozen(Color.FromRgb(0x8A, 0x91, 0xA3)), FontWeights.SemiBold), _dmg = T("", 14, White), _crit = T("", 12.5, Grey, FontWeights.SemiBold);
        /// <summary>The plate's two lines of words: the name and its marks; under it, Combat Power and a healer's healing.</summary>
        readonly StackPanel _top = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left };
        readonly StackPanel _under = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, -1, 0, 0) };
        /// <summary>The two lines, centred on the plate above its foot strip.</summary>
        readonly StackPanel _tiers = new() { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 4) };
        readonly Border _iconHost = new() { Width = 22, Height = 22, Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center };
        readonly TextBlock _none = new();
        string _cls = "\0";
        readonly List<FrameworkElement> _keep = new(), _keepUnder = new();
        readonly List<(FrameworkElement, FrameworkElement?)> _give = new(), _giveUnder = new();
        readonly double _dmgW, _critW;
        readonly Grid _plate;
        readonly System.Windows.Media.Effects.Effect _halo;
        MeterMember? _m;
        double _bar;
        bool _expanded;
        /// <summary>A light crossing the row (a kill's glint over the winner), 0..1; -1 none.</summary>
        public double Sweep = -1;
        /// <summary>The row's entrance: its plates wipe in behind a slanted edge (0..1), the words come on once they're in.</summary>
        public double Enter = 1;

        public FrameworkElement Root => _root;

        public ScoreRow()
        {
            _dps.Margin = new Thickness(0, 0, 9, 0); _pct.Margin = new Thickness(0, 1, 9, 0); _dmg.Margin = new Thickness(0, 0, 9, 0); _crit.Margin = new Thickness(0, 1, 9, 0);
            // the stat cells sized for the widest real figure; the plate (the bar) gets the rest
            _dmgW = Math.Max(60, Tac.FigCol(_dmg, "888.88M")); _critW = Math.Max(46, Tac.FigCol(_crit, "88%"));
            var widths = new[] { 30.0, 9, double.NaN, Math.Max(62, Tac.FigCol(_dps, "888.8K")), Tac.FigCol(_pct, "88.8%", "100%", L.T("CP")), 0, 0, Sl };
            foreach (var w in widths)
            {
                _root.ColumnDefinitions.Add(new ColumnDefinition { Width = double.IsNaN(w) ? new GridLength(1, GridUnitType.Star) : new GridLength(w) });
                _text.ColumnDefinitions.Add(new ColumnDefinition { Width = double.IsNaN(w) ? new GridLength(1, GridUnitType.Star) : new GridLength(w) });
            }
            _art = new TacSketch(Draw);
            Grid.SetColumnSpan(_art, widths.Length);
            _root.Children.Add(_art);
            Grid.SetColumnSpan(_text, widths.Length);
            _root.Children.Add(_text);
            _rank.HorizontalAlignment = HorizontalAlignment.Center; _rank.Margin = new Thickness(Sl - 1, 0, 0, 0);
            Put(_rank, 0);
            var plate = new Grid { Margin = new Thickness(Sl + 5, 0, 6, 0) };
            plate.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); plate.ColumnDefinitions.Add(new ColumnDefinition());
            plate.Children.Add(_iconHost);
            _name.TextTrimming = TextTrimming.CharacterEllipsis;
            _top.Children.Add(_name);
            _tiers.Children.Add(_top); _tiers.Children.Add(_under);
            // a soft dark halo under the words: they read on any class's fill
            _tiers.Effect = _halo = new System.Windows.Media.Effects.DropShadowEffect { Color = Colors.Black, BlurRadius = 4, ShadowDepth = 0, Opacity = .75 };
            Grid.SetColumn(_tiers, 1); plate.Children.Add(_tiers);
            Put(plate, 2);
            _dps.Tag = "row-dps"; _dps.HorizontalAlignment = HorizontalAlignment.Right; Put(_dps, 3);
            _pct.HorizontalAlignment = HorizontalAlignment.Right; Put(_pct, 4);
            _dmg.HorizontalAlignment = HorizontalAlignment.Right; Put(_dmg, 5);
            _crit.HorizontalAlignment = HorizontalAlignment.Right; Put(_crit, 6);
            _plate = plate;
            _root.SizeChanged += (_, _) => Fit();
        }

        /// <summary>The name gives way first (a 10-character name whole), then its marks to their glyphs, YOU, the emblem; under it, the CP.</summary>
        void Fit()
        {
            double room = _root.ColumnDefinitions[2].ActualWidth;
            if (room <= 0) { Tac.FitName(_name, double.PositiveInfinity, _keep, _give); return; }
            // the plate's own width as laid out (its emblem off, a hair before its end), else the column's less the plate's margins
            double plate = (_plate.ActualWidth > 0 ? _plate.ActualWidth : room - _plate.Margin.Left - _plate.Margin.Right + Sl) - 2, floor = Tac.NameFloor(_name);
            Tac.FitName(_name, plate, _keep, _give, floor, floor);
            double icon = _iconHost.Visibility == Visibility.Visible ? _iconHost.Width + _iconHost.Margin.Right : 0;
            Tac.FitName(_none, plate - icon, _keepUnder, _giveUnder, 0, 0);
        }

        /// <summary>A healer's healing a second ("+1.1K/s").</summary>
        static TextBlock HealFig(string heal, Color c)
        {
            var h = T("+" + heal, 12, Frozen(c), FontWeights.SemiBold);
            h.Tag = "heal-figures";
            return h;
        }

        void Put(FrameworkElement el, int col) { Grid.SetColumn(el, col); _text.Children.Add(el); }

        public void Set(MeterMember m, MeterContext c, bool fresh)
        {
            _m = m;
            if (_expanded != c.Expanded)
            {
                _expanded = c.Expanded;
                _root.ColumnDefinitions[5].Width = _text.ColumnDefinitions[5].Width = new GridLength(c.Expanded ? _dmgW : 0);
                _root.ColumnDefinitions[6].Width = _text.ColumnDefinitions[6].Width = new GridLength(c.Expanded ? _critW : 0);
            }
            bool podium = m.Rank <= 3 && !m.CpOnly;
            _rank.Text = m.Rank.ToString(CultureInfo.InvariantCulture);
            _rank.Foreground = podium ? InkB : White;
            if (_cls != m.Class + "|" + m.Name) { _cls = m.Class + "|" + m.Name; _iconHost.Child = ThemeText.Icon(m.Class, 22, m.Name); }
            _name.Text = m.Name;
            // on its own class's fill a light tint of the class reads; on your light plate the dark ink
            _name.Foreground = m.You ? InkB : Frozen(Tac.Mix(m.NameColor, Colors.White, .45));
            while (_top.Children.Count > 1) _top.Children.RemoveAt(1);
            _under.Children.Clear();
            _keep.Clear(); _give.Clear(); _keepUnder.Clear(); _giveUnder.Clear();
            FrameworkElement Add(FrameworkElement el) { _top.Children.Add(el); return el; }
            FrameworkElement Under(FrameworkElement el) { el.Margin = new Thickness(_under.Children.Count > 0 ? 8 : 0, 0, 0, 0); _under.Children.Add(el); return el; }
            // the name's line: a kill's plates (to their glyph when short of room), deaths, YOU (it goes: the light plate and red
            // DPS cell say it), then the emblem (the stripe and the name's colour still say the class)
            if (m.TopDps) _give.Add((Add(Plate(false)), Add(Plate(false, glyph: true))));
            if (m.TopHeals) _give.Add((Add(Plate(true)), Add(Plate(true, glyph: true))));
            if (m.Deaths > 0) _keep.Add(Add(Mark(m.Deaths > 1 ? $"✖{m.Deaths}" : "✖", Color.FromRgb(0x3A, 0x0C, 0x12), Color.FromRgb(0xFF, 0x6B, 0x78))));
            if (m.You) _give.Add((Add(Mark(L.T("YOU"), RedC, Colors.White)), null));
            _give.Add((_iconHost, null));
            // under it, quiet: Combat Power (Steven, 2026-11-16: "dont forget the combat power next to ppls names") once there's
            // damage (before it, the figure itself is the CP), and a healer's healing
            _cp.Foreground = Frozen(m.You ? Color.FromRgb(0x4A, 0x4F, 0x58) : Color.FromRgb(0xA4, 0xAA, 0xBA));
            if (m.Cp.Length > 0) { _cp.Text = m.Cp + " " + L.T("CP"); _giveUnder.Add((Under(_cp), null)); }
            if (m.Heal.Length > 0) _keepUnder.Add(Under(HealFig(m.Heal, m.You ? Color.FromRgb(0x0E, 0x8A, 0x4C) : GreenC)));
            // nothing under it (before the first hit): the name in the plate's middle
            _under.Visibility = _under.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            _tiers.Effect = m.You ? null : _halo;
            _dps.Text = m.Figure;
            _pct.Text = m.ShareText;
            _pct.Foreground = m.You ? Frozen(Color.FromRgb(0xFF, 0xD6, 0xDB)) : Grey;
            _dmg.Text = c.Expanded ? m.Damage : "";
            _crit.Text = c.Expanded ? m.Crit : "";
            Fit();
            SlideBar(_art, c, "bar", Tac.BarOf(m, c), v => { _bar = v; _art.InvalidateVisual(); });
            _art.InvalidateVisual();
        }

        /// <summary>A small slanted mark in the name's line (YOU, a death).</summary>
        static FrameworkElement Mark(string s, Color bg, Color fg)
        {
            var b = new Block(bg, Tt(s, 10.5, Frozen(fg), Face, FontWeights.Bold, FontStretches.Normal), 16, new Thickness(7, 0, 7, 0));
            b.Root.Margin = new Thickness(6, 0, 0, 0); b.Root.VerticalAlignment = VerticalAlignment.Center;
            return b.Root;
        }

        /// <summary>The end of a boss kill's plate (TOP DPS gold, TOP HEALS green), slanted as the board's.</summary>
        static FrameworkElement Plate(bool heals, bool glyph = false)
        {
            var b = new Block(heals ? GreenC : YellowC, Tt(glyph ? (heals ? "✚" : "★") : L.T(heals ? "TOP HEALS" : "TOP DPS"), glyph ? 10.5 : 9.5, InkB, Face, FontWeights.Bold, FontStretches.Normal), 16, new Thickness(6, 0, 6, 0));
            b.Root.Tag = heals ? "top-heals" : "top-dps";
            if (glyph) b.Root.ToolTip = L.T(heals ? "Top heals" : "Top DPS");
            b.Root.Margin = new Thickness(6, 0, 0, 0); b.Root.VerticalAlignment = VerticalAlignment.Center;
            return b.Root;
        }

        /// <summary>A cell of the row from x0 to x1 at its foot, slanted as the bug's blocks, a seam before the next.</summary>
        static Geometry Cell(double x0, double x1, double h)
        {
            var g = new StreamGeometry();
            using (var x = g.Open())
            {
                x.BeginFigure(new Point(x0 + Sl, 0), true, true);
                x.LineTo(new Point(x1 + Sl - Gap, 0), true, false);
                x.LineTo(new Point(x1 - Gap, h), true, false);
                x.LineTo(new Point(x0, h), true, false);
            }
            g.Freeze();
            return g;
        }

        void Draw(DrawingContext dc, Size s)
        {
            if (_m is not { } m || s.Width < 40) return;
            var cols = _root.ColumnDefinitions;
            var xs = new double[cols.Count + 1];
            for (int i = 0; i < cols.Count; i++) xs[i + 1] = xs[i] + cols[i].ActualWidth;
            double h = s.Height;
            bool clipped = Enter < 1;
            if (clipped) dc.PushClip(Wipe(-10, (s.Width + 20) * Enter, h));
            // the rank tab: metal for the podium, slate below it
            Color tone = m.CpOnly ? SlateC : m.Rank == 1 ? GoldTab : m.Rank == 2 ? SilverTab : m.Rank == 3 ? BronzeTab : SlateC;
            bool podium = m.Rank <= 3 && !m.CpOnly;
            dc.DrawGeometry(Tac.Grad(true, (Tac.Mix(tone, Colors.White, podium ? .38 : .1), 0), (tone, .5), (Tac.Mix(tone, Colors.Black, .2), 1)), null, Cell(xs[0], xs[1], h));
            // the team stripe: the class's colour
            dc.DrawGeometry(Frozen(m.ClassColor), null, Cell(xs[1], xs[2], h));
            // the name plate is the bar: filled in the class's colour to the member's part of the top, a bright strip along its foot
            var plate = Cell(xs[2], xs[3], h);
            dc.DrawGeometry(Frozen(m.You ? LightC : PlateC), null, plate);
            dc.PushClip(plate);
            double pw = xs[3] - xs[2] + Sl, fw = pw * Math.Clamp(_bar, 0, 1);
            if (fw > .5)
            {
                dc.DrawGeometry(Tac.Grad(false, (Tac.A(m.ClassColor, m.You ? .42 : .55), 0), (Tac.A(m.ClassColor, m.You ? .26 : .34), 1)), null, Tac.Lean(new Rect(xs[2] - Sl, 0, fw + Sl, h), Sl));
                dc.DrawGeometry(Frozen(Tac.A(Tac.Mix(m.ClassColor, Colors.White, .5), .85)), null, Tac.Lean(new Rect(xs[2] + fw - 2 - Sl, 0, 2 + Sl, h), Sl));
            }
            dc.DrawRectangle(Frozen(Color.FromArgb(m.You ? (byte)0x50 : (byte)0x14, 0xFF, 0xFF, 0xFF)), null, new Rect(xs[2], 0, pw, 1));
            dc.DrawRectangle(Frozen(Color.FromArgb(0x70, 0, 0, 0)), null, new Rect(xs[2], h - 4, pw, 4));
            dc.DrawRectangle(Frozen(Tac.Mix(m.ClassColor, Colors.White, .15)), null, new Rect(xs[2], h - 4, Math.Max(0, fw - Sl * .9), 4));
            dc.Pop();
            // the stat cells: the DPS on the darkest (red for you), the share on slate; the damage and crit when expanded
            dc.DrawGeometry(Frozen(m.You ? RedC : DpsC), null, Cell(xs[3], xs[4], h));
            dc.DrawGeometry(Frozen(m.You ? Color.FromRgb(0xA8, 0x14, 0x26) : PctC), null, Cell(xs[4], xs[5], h));
            if (_expanded)
            {
                dc.DrawGeometry(Frozen(PlateC), null, Cell(xs[5], xs[6], h));
                dc.DrawGeometry(Frozen(PctC), null, Cell(xs[6], xs[7], h));
            }
            if (Sweep is > 0 and < 1)
            {
                double x = -60 + (s.Width + 120) * Sweep;
                dc.DrawGeometry(Tac.Grad(false, (Color.FromArgb(0, 0xFF, 0xFF, 0xFF), 0), (Color.FromArgb(0x9A, 0xFF, 0xF4, 0xC8), .5), (Color.FromArgb(0, 0xFF, 0xFF, 0xFF), 1)), null, Tac.Lean(new Rect(x - 30, 0, 60, h), 10));
            }
            if (clipped) dc.Pop();
            _text.Opacity = Enter >= 1 ? 1 : Enter > .85 ? (Enter - .85) / .15 : 0;
        }
    }
}
