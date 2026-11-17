using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

sealed partial class HudScorebug
{
    /// <summary>
    /// One member as a broadcast leaderboard row: a rank tab (gold, silver, bronze for the podium), the class's colour as the
    /// team stripe, a name plate (the class emblem, the name, its marks, Combat Power at its end) whose foot is the share's
    /// bar, and stat cells for the DPS and the share (the damage and crit when expanded). You: the plate goes light and your
    /// DPS cell red, the way a broadcast features its player. Every shape is drawn in one pass; the text sits on top.
    /// </summary>
    sealed class ScoreRow : IThemeRow, IThemeRowGrows
    {
        public const double H = 30, Pitch = 33;
        const double Sl = H * .1763, Gap = 2.5;
        static readonly Color PlateC = Color.FromRgb(0x17, 0x1A, 0x23), DpsC = Color.FromRgb(0x0A, 0x0C, 0x11), PctC = Color.FromRgb(0x20, 0x24, 0x2E);
        readonly Grid _root = new() { Height = H };
        readonly Grid _text = new();
        readonly TacSketch _art;
        readonly TextBlock _rank = T("", 18, InkB), _name = T("", 15.5, White), _dps = T("", 17.5, White), _pct = T("", 13, Grey, FontWeights.SemiBold),
            _cp = T("", 11, Frozen(Color.FromRgb(0x8A, 0x91, 0xA3)), FontWeights.SemiBold), _dmg = T("", 14, White), _crit = T("", 12.5, Grey, FontWeights.SemiBold);
        readonly StackPanel _nameLine = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left };
        /// <summary>The marks that don't fit beside the name, on a line under it in the plate (Tac.FitLines): the row grows by its height.</summary>
        readonly StackPanel _sub = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 2, 0, 0) };
        readonly StackPanel _lines = new() { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
        StackPanel _cpHost = null!;
        List<FrameworkElement> _order = new();
        readonly double _dmgW, _critW;
        public double Extra { get; private set; }
        public event Action? Grew;
        readonly Border _iconHost = new() { Width = 22, Height = 22, Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center };
        string _cls = "\0";
        /// <summary>The CP's short form when the plate is short of room: the word smaller, tight after the figure (never a bare figure).</summary>
        readonly TextBlock _cpShort = T("", 11, Frozen(Color.FromRgb(0x8A, 0x91, 0xA3)), FontWeights.SemiBold);
        readonly List<FrameworkElement> _keep = new();
        readonly List<(FrameworkElement, FrameworkElement?)> _give = new();
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
            // the stat cells sized for the widest real figure (Steven, 2026-11-17: "a lot of numbers ... you can't see, it's cut off!")
            _dmgW = Math.Max(60, Tac.FigCol(_dmg, "888.88M")); _critW = Math.Max(46, Tac.FigCol(_crit, "88%"));
            var widths = new[] { 30.0, 9, double.NaN, Math.Max(62, Tac.FigCol(_dps, "888.8K")), Math.Max(50, Tac.FigCol(_pct, "88.8%", "100%")), 0, 0, Sl };
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
            plate.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); plate.ColumnDefinitions.Add(new ColumnDefinition()); plate.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            plate.Children.Add(_iconHost);
            _name.TextTrimming = TextTrimming.CharacterEllipsis;
            _nameLine.Children.Add(_name);
            _lines.Children.Add(_nameLine); _lines.Children.Add(_sub);
            Grid.SetColumn(_lines, 1); Grid.SetColumnSpan(_lines, 2); plate.Children.Add(_lines);   // the line under the name may run on under the CP
            _cp.Margin = new Thickness(5, 2, 0, 0); _cpShort.Margin = new Thickness(5, 2, 0, 0);
            var cpHost = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Children = { _cp, _cpShort } };
            Grid.SetColumn(cpHost, 2); plate.Children.Add(cpHost);
            _cpHost = cpHost;
            Put(plate, 2);
            _dps.Tag = "row-dps"; _dps.HorizontalAlignment = HorizontalAlignment.Right; Put(_dps, 3);
            _pct.HorizontalAlignment = HorizontalAlignment.Right; Put(_pct, 4);
            _dmg.HorizontalAlignment = HorizontalAlignment.Right; Put(_dmg, 5);
            _crit.HorizontalAlignment = HorizontalAlignment.Right; Put(_crit, 6);
            _plate = plate;
            _root.SizeChanged += (_, _) => Fit();
        }

        readonly Grid _plate;

        /// <summary>The name gives way to its marks (YOU, a death, the heal, a kill's plates): it is cut, never they.</summary>
        void Fit()
        {
            double room = _root.ColumnDefinitions[2].ActualWidth;
            if (room <= 0) { Tac.FitName(_name, double.PositiveInfinity, _keep, _give); return; }
            // the plate's own width as laid out (its emblem off, a hair before its end), else the column's less the plate's margins
            double plate = _plate.ActualWidth > 0 ? _plate.ActualWidth : room - _plate.Margin.Left - _plate.Margin.Right + Sl;
            double icon = _iconHost.Visibility == Visibility.Visible ? _iconHost.Width + _iconHost.Margin.Right : 0;
            bool two = Tac.FitLines(_name, plate - 2, plate - 2 - icon, _nameLine, _sub, _order, _keep, _give);
            // two lines: the CP at the name's line (the plate's top), not between them
            _cpHost.VerticalAlignment = two ? VerticalAlignment.Top : VerticalAlignment.Center;
            _lines.VerticalAlignment = two ? VerticalAlignment.Top : VerticalAlignment.Center;
            _iconHost.VerticalAlignment = two ? VerticalAlignment.Top : VerticalAlignment.Center;
            var inf = new Size(double.PositiveInfinity, double.PositiveInfinity);
            _nameLine.Measure(inf); _cpHost.Measure(inf); _sub.Measure(inf);
            double extra = two ? Math.Ceiling(_sub.DesiredSize.Height) + 4 : 0, top = (H - _nameLine.DesiredSize.Height) / 2;
            _lines.Margin = new Thickness(0, two ? top : 0, 0, 0);
            _cpHost.Margin = new Thickness(0, two ? top + (_nameLine.DesiredSize.Height - _cpHost.DesiredSize.Height) / 2 : 0, 0, 0);
            _iconHost.Margin = new Thickness(0, two ? (H - _iconHost.Height) / 2 : 0, 7, 0);
            if (Math.Abs(extra - Extra) < 0.5) return;
            Extra = extra;
            _root.Height = H + extra;
            Grew?.Invoke();
        }

        /// <summary>A healer's healing a second after the name ("+1.1K/s").</summary>
        static TextBlock HealFig(string heal, Color c, double left = 7, double top = 1, bool plus = true)
        {
            var h = T((plus ? "+" : "") + heal, 12, Frozen(c), FontWeights.SemiBold);
            h.Tag = "heal-figures"; h.Margin = new Thickness(left, top, 0, 0);
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
            _name.Foreground = m.You ? InkB : Frozen(m.NameColor);
            // the marks after the name: YOU, deaths, the heal figure, the end of a kill's plates
            _sub.Children.Clear();
            while (_nameLine.Children.Count > 1) _nameLine.Children.RemoveAt(1);
            _keep.Clear(); _give.Clear();
            FrameworkElement Add(FrameworkElement el) { _nameLine.Children.Add(el); return el; }
            var you = m.You ? Add(Mark(L.T("YOU"), RedC, Colors.White)) : null;
            if (m.Deaths > 0) _keep.Add(Add(Mark(m.Deaths > 1 ? $"✖{m.Deaths}" : "✖", Color.FromRgb(0x3A, 0x0C, 0x12), Color.FromRgb(0xFF, 0x6B, 0x78))));
            // short of room, in turn: TOP DPS goes to its star, TOP HEALS folds into the heal figure (a green plate), the YOU tag
            // goes (the light plate and the red DPS cell say it), the CP's word shrinks; only then is the name cut
            var healC = m.You ? Color.FromRgb(0x0E, 0x8A, 0x4C) : GreenC;
            if (m.Heal.Length > 0 && m.TopHeals)
            {
                var full = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Children = { HealFig(m.Heal, healC), Plate(true) } };
                var plateFig = new StackPanel { Orientation = Orientation.Horizontal, Children = { T("✚", 10.5, InkB), HealFig(m.Heal, InkC0, 3, 0, plus: false) } };
                var b = new Block(GreenC, plateFig, 16, new Thickness(5, 0, 5, 0));
                b.Root.Tag = "top-heals"; b.Root.ToolTip = L.T("Top heals"); b.Root.Margin = new Thickness(6, 0, 0, 0); b.Root.VerticalAlignment = VerticalAlignment.Center;
                _give.Add((Add(full), Add(b.Root)));
            }
            else if (m.Heal.Length > 0) _keep.Add(Add(HealFig(m.Heal, healC)));
            else if (m.TopHeals) _give.Add((Add(Plate(true)), Add(Plate(true, glyph: true))));
            if (m.TopDps) _give.Insert(0, (Add(Plate(false)), Add(Plate(false, glyph: true))));
            if (you != null) _give.Add((you, null));
            // last before the name is cut: the class's emblem (the team stripe and the name's colour still say the class)
            _give.Add((_iconHost, null));
            _order = _nameLine.Children.Cast<FrameworkElement>().Skip(1).ToList();
            // Combat Power at the plate's end, always (Steven, 2026-11-16: "dont forget the combat power next to ppls names");
            // before any damage the figure itself is the CP
            _cp.Text = m.Cp.Length > 0 ? m.Cp + " " + L.T("CP") : "";
            _cpShort.Inlines.Clear();
            if (m.Cp.Length > 0) { _cpShort.Inlines.Add(new System.Windows.Documents.Run(m.Cp)); _cpShort.Inlines.Add(new System.Windows.Documents.Run(" " + L.T("CP")) { FontSize = 8 }); }
            // on your light plate a darker ink, so the CP still reads there
            _cp.Foreground = _cpShort.Foreground = Frozen(m.You ? Color.FromRgb(0x4A, 0x4F, 0x58) : Color.FromRgb(0x8A, 0x91, 0xA3));
            // "CP" a fixed slot after the figure, the same on every row: the figures end on one edge
            _cpShort.Visibility = Visibility.Collapsed;
            if (m.Cp.Length > 0) { _cp.Visibility = Visibility.Visible; _keep.Add(_cp); }
            else _cp.Visibility = Visibility.Collapsed;
            _dps.Text = m.Figure;
            _pct.Text = m.ShareText;
            _pct.Foreground = m.You ? Frozen(Color.FromRgb(0xFF, 0xD6, 0xDB)) : Grey;
            _dmg.Text = c.Expanded ? m.Damage : "";
            _crit.Text = c.Expanded ? m.Crit : "";
            Fit();
            SlideBar(_art, c, "bar", m.Share, v => { _bar = v; _art.InvalidateVisual(); });
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
            // the name plate, its foot the share's bar; a wash of the class's colour behind the name as far as the share
            var plate = Cell(xs[2], xs[3], h);
            dc.DrawGeometry(Frozen(m.You ? LightC : PlateC), null, plate);
            dc.PushClip(plate);
            double pw = xs[3] - xs[2] + Sl;
            dc.DrawRectangle(Tac.Grad(false, (Tac.A(m.ClassColor, .30), 0), (Tac.A(m.ClassColor, m.You ? .12 : .07), 1)), null, new Rect(xs[2], 0, pw * _bar, h));
            dc.DrawRectangle(Frozen(Color.FromArgb(m.You ? (byte)0x50 : (byte)0x14, 0xFF, 0xFF, 0xFF)), null, new Rect(xs[2], 0, pw, 1));
            dc.DrawRectangle(Frozen(Color.FromArgb(0x70, 0, 0, 0)), null, new Rect(xs[2], h - 3, pw, 3));
            dc.DrawRectangle(Frozen(m.ClassColor), null, new Rect(xs[2], h - 3, pw * _bar, 3));
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
