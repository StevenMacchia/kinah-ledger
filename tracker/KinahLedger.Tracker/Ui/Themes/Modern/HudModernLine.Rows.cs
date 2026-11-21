using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

sealed partial class HudModernLine
{
    /// <summary>The rows' width (the frame's inside) and whether the share has gone from every row to make room (one rule for all: the DPS column stays put).</summary>
    double _rowW = 300;
    bool _shareOff;

    /// <summary>
    /// The crowding rule over the whole meter: each row tries its name whole with Combat Power, then without it, then with
    /// its marks at their short forms; if a name still doesn't fit, the share column goes from every row (so the DPS column
    /// never moves) and only then is a name cut, never below about ten characters.
    /// </summary>
    void FitAll()
    {
        if (Meter is not { } m) return;
        var rows = m.Members.Select(x => m.RowOf(x.Id)).OfType<LineRow>().ToList();
        _shareOff = rows.Any(r => r.NeedsShareOff(_rowW));
        foreach (var r in rows) r.Fit(_rowW, _shareOff);
    }

    /// <summary>The line between the party and you below it: "1 MORE", small and quiet, centred.</summary>
    static FrameworkElement GapLine(int left)
    {
        var t = Tt(left > 0 ? L.F("{0} more", left).ToUpper(CultureInfo.CurrentCulture) : "· · ·", 9.5, DimB, FontWeights.SemiBold);
        t.HorizontalAlignment = HorizontalAlignment.Center; t.Effect = ShadeFx; t.Margin = new Thickness(0, 0, 0, 2);
        return t;
    }

    /// <summary>
    /// One member as a Line row: the rank, the class's art in a thin white ring, the name (underlined for you) with its
    /// Combat Power and marks after it, a healer's healing a second, the DPS big at the right, the share small after it;
    /// under them, the class-coloured 4 px bar on a faint track, glowing softly. Every word has the look's shade.
    /// </summary>
    sealed class LineRow : IThemeRow, IThemeBarSpan
    {
        public const double H = 34, Pitch = 36, TextH = 27, BarH = 4, Pad = 3, RankW = 14, IconS = 17, IconW = IconS + 6, ShareW = 37, DmgW = 52, CritW = 38, DpsMin = 54;
        static readonly TextDecorationCollection Under = MakeUnder();
        static TextDecorationCollection MakeUnder()
        {
            var pen = new Pen(WhiteB, 1); pen.Freeze();
            var c = new TextDecorationCollection { new TextDecoration(TextDecorationLocation.Underline, pen, 2.5, TextDecorationUnit.Pixel, TextDecorationUnit.Pixel) };
            c.Freeze();
            return c;
        }

        readonly HudModernLine _look;
        readonly Grid _root = new() { Height = H };
        readonly TacSketch _bar;
        readonly Grid _text = new() { Height = TextH, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(Pad, 0, Pad, 0), Effect = ShadeFx };
        readonly TextBlock _rank = Tt("", 12, Mod.Br(Colors.White, .92), FontWeights.Bold), _name = Tt("", 13, WhiteB, FontWeights.Bold), _cp = Tt("", 10, Mod.Br(Colors.White, .62), FontWeights.SemiBold),
            _heal = Tt("", 10.5, MintB, FontWeights.Bold), _dps = Tt("", 16, WhiteB, FontWeights.Bold), _share = Tt("", 10.5, Mod.Br(Colors.White, .75), FontWeights.SemiBold),
            _dmg = Tt("", 12, SoftB, FontWeights.SemiBold), _crit = Tt("", 10.5, DimB, FontWeights.SemiBold);
        readonly Border _icon = new() { Width = IconS, Height = IconS, CornerRadius = new CornerRadius(IconS / 2), BorderThickness = new Thickness(1), BorderBrush = Mod.Br(Colors.White, .72), Background = Mod.Br(Colors.Black, .55), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
        readonly StackPanel _line = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        /// <summary>The marks after the name: each its full form and its short one (null: it stays as it is).</summary>
        readonly List<(FrameworkElement Full, FrameworkElement? Short)> _marks = new();
        FrameworkElement? _you;
        string _cls = "\0";
        MeterMember? _m;
        double _fill, _glint = -1;
        bool _expanded;
        Brush _barB = WhiteB, _glowNear = WhiteB, _glowMid = WhiteB, _glowFar = WhiteB;

        public FrameworkElement Root => _root;

        /// <summary>The 4 px bar under the text: DrawBar's track (Pad in from each side, BarH tall at the row's foot).</summary>
        public (FrameworkElement On, Rect Track)? BarTrack => _bar.ActualWidth <= 0 ? null : (_bar, new Rect(Pad, H - BarH, _bar.ActualWidth - 2 * Pad, BarH));

        public LineRow(HudModernLine look)
        {
            _look = look;
            foreach (var w in new[] { RankW, IconW, double.NaN, 0, 0, ShareW, 0, 0 })
                _text.ColumnDefinitions.Add(new ColumnDefinition { Width = double.IsNaN(w) ? new GridLength(1, GridUnitType.Star) : w == 0 ? GridLength.Auto : new GridLength(w) });
            _text.ColumnDefinitions[6].Width = _text.ColumnDefinitions[7].Width = new GridLength(0);
            _bar = new TacSketch(DrawBar);
            _root.Children.Add(_bar);
            _root.Children.Add(_text);
            _rank.HorizontalAlignment = HorizontalAlignment.Left;
            Put(_rank, 0); Put(_icon, 1);
            _name.TextTrimming = TextTrimming.CharacterEllipsis;
            _cp.Margin = new Thickness(5, 1.5, 0, 0);
            _cp.ToolTip = L.T("CP");
            _line.Children.Add(_name);
            _line.Children.Add(_cp);
            Put(_line, 2);
            _heal.Tag = "heal-figures"; _heal.Margin = new Thickness(6, 1, 0, 0); Put(_heal, 3);
            _dps.Tag = "row-dps"; _dps.HorizontalAlignment = HorizontalAlignment.Right; _dps.MinWidth = DpsMin; _dps.TextAlignment = TextAlignment.Right; _dps.Margin = new Thickness(5, 0, 0, 0); Put(_dps, 4);
            _share.HorizontalAlignment = HorizontalAlignment.Right; _share.Margin = new Thickness(0, 2, 0, 0); Put(_share, 5);
            _dmg.HorizontalAlignment = HorizontalAlignment.Right; _dmg.Margin = new Thickness(0, 1, 0, 0); Put(_dmg, 6);
            _crit.HorizontalAlignment = HorizontalAlignment.Right; _crit.Margin = new Thickness(0, 2, 0, 0); Put(_crit, 7);
        }

        void Put(FrameworkElement el, int col) { Grid.SetColumn(el, col); _text.Children.Add(el); }

        public void Set(MeterMember m, MeterContext c, bool fresh)
        {
            _m = m;
            _expanded = c.Expanded;
            _text.ColumnDefinitions[6].Width = new GridLength(c.Expanded ? DmgW : 0);
            _text.ColumnDefinitions[7].Width = new GridLength(c.Expanded ? CritW : 0);
            _rank.Text = m.Rank.ToString(CultureInfo.InvariantCulture);
            if (_cls != m.Class + "|" + m.Name)
            {
                _cls = m.Class + "|" + m.Name;
                _icon.Child = IconArt(m.Class, m.Name);
                _icon.ToolTip = ThemeText.Class(m.Class);
                var cc = m.ClassColor;
                _barB = Frozen(cc); _glowNear = Mod.Br(cc, .3); _glowMid = Mod.Br(cc, .17); _glowFar = Mod.Br(cc, .08);
            }
            _name.Text = m.Name;
            _name.TextDecorations = m.You ? Under : null;
            _cp.Text = m.Cp;

            // the marks: TOP DPS and TOP HEALS as outline chips (their glyphs alone when short of room), deaths in red, YOU solid white
            while (_line.Children.Count > 2) _line.Children.RemoveAt(2);
            _marks.Clear(); _you = null;
            FrameworkElement Add(FrameworkElement el) { el.Margin = new Thickness(5, 0, 0, 0); _line.Children.Add(el); return el; }
            if (m.TopDps) _marks.Add((Add(Mark("★ " + L.T("DPS"), "top-dps", false)), Add(Mark("★", "top-dps", false))));
            if (m.TopHeals) _marks.Add((Add(Mark("✚ " + L.T("Heal", "line"), "top-heals", true)), Add(Mark("✚", "top-heals", true))));
            if (m.Deaths > 0)
            {
                var d = Tt("✕" + m.Deaths.ToString(CultureInfo.InvariantCulture), 10, RedB, FontWeights.Bold);
                d.ToolTip = L.F(m.Deaths == 1 ? "{0} death" : "{0} deaths", m.Deaths);
                _marks.Add((Add(d), null));
            }
            if (m.You)
            {
                _you = Add(Chip(L.T("YOU"), WhiteB, InkB, 8.5, WhiteB, 4));
                _marks.Add((_you, null));
            }

            _heal.Text = m.Heal.Length > 0 ? "+" + m.Heal : "";
            _heal.Visibility = m.Heal.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            // before any damage the figure is the Combat Power ("167,548"), quieter
            _dps.Text = m.Figure;
            _dps.FontSize = m.CpOnly ? 13 : 16;
            _dps.FontWeight = m.CpOnly ? FontWeights.SemiBold : FontWeights.Bold;
            _dps.Foreground = m.CpOnly ? SoftB : WhiteB;
            _share.Text = m.ShareText;
            _dmg.Text = c.Expanded ? m.Damage : "";
            _crit.Text = c.Expanded ? m.Crit : "";
            Fit(_look._rowW, _look._shareOff);
            SlideBar(_bar, c, "bar", m.CpOnly ? 0 : c.E.BarOf(m.F), v => { _fill = v; _bar.InvalidateVisual(); });
            _bar.InvalidateVisual();
        }

        /// <summary>A mark's outline chip: white for TOP DPS, mint for TOP HEALS.</summary>
        static FrameworkElement Mark(string s, string tag, bool heals)
        {
            var b = Chip(s, heals ? Mod.Br(MintC0, .9) : ChipEdgeB, heals ? MintB : WhiteB, 8.5);
            b.Tag = tag;
            b.ToolTip = L.T(heals ? "Top heals" : "Top DPS");
            return b;
        }

        /// <summary>The class's art in its dark disc (the ring is the row's thin white one), or the name's letter.</summary>
        static FrameworkElement IconArt(string cls, string name)
        {
            if (ClassArt.Image(cls) is { } img)
            {
                var i = new System.Windows.Controls.Image { Source = img, Width = IconS - 4, Height = IconS - 4, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
                RenderOptions.SetBitmapScalingMode(i, BitmapScalingMode.HighQuality);
                return i;
            }
            var t = Tt(name.Trim().Length > 0 ? name.Trim()[..1].ToUpperInvariant() : "·", 9, WhiteB, FontWeights.Bold);
            t.HorizontalAlignment = HorizontalAlignment.Center;
            return t;
        }

        /// <summary>The room the name's line has: the row less the rank, the icon, the heal figure, the DPS, the share and the expanded columns.</summary>
        double Room(double rowW, bool share, bool icon)
        {
            double heal = _heal.Visibility == Visibility.Visible ? Mod.W(_heal) + _heal.Margin.Left : 0;
            double dps = Math.Max(DpsMin, Mod.W(_dps)) + _dps.Margin.Left;
            return rowW - 2 * Pad - RankW - (icon ? IconW : 0) - heal - dps - (share ? ShareW : 0) - (_expanded ? DmgW + CritW : 0) - 2;
        }

        double NameW() { _name.ClearValue(FrameworkElement.MaxWidthProperty); return Mod.W(_name); }
        double CpW() => _cp.Text.Length > 0 ? Mod.W(_cp) + _cp.Margin.Left : 0;
        double MarksW(bool shrt) => _marks.Sum(p => Mod.W(shrt && p.Short != null ? p.Short : p.Full) + 5);

        /// <summary>The name would be cut with the share kept, even without CP and with the marks short.</summary>
        public bool NeedsShareOff(double rowW) => _m is { } && NameW() + MarksW(true) > Room(rowW, true, true) + .5;

        /// <summary>Lays the name's line out in <paramref name="rowW"/>: CP first to go, then the marks to their short forms, then (all rows) the share, then the name is cut (never below ten characters before YOU and the icon go).</summary>
        public void Fit(double rowW, bool shareOff)
        {
            if (_m == null) return;
            bool share = !shareOff;
            _text.ColumnDefinitions[5].Width = new GridLength(share ? ShareW : 0);
            _share.Visibility = share ? Visibility.Visible : Visibility.Collapsed;
            double room = Room(rowW, share, true), nw = NameW(), cp = CpW();
            bool cpOn = cp > 0 && nw + cp + MarksW(false) <= room + .5;
            bool shrt = !cpOn && nw + MarksW(false) > room + .5;
            double marks = MarksW(shrt);
            // about ten characters of the name at the least: past that, YOU goes (the underline says it), then the icon
            double min = Math.Min(nw, NameMin());
            bool youOn = true, iconOn = true;
            if (min + marks > room + .5 && _you != null) { youOn = false; marks -= Mod.W(_you) + 5; }
            if (min + marks > room + .5) { iconOn = false; room += IconW; }
            foreach (var (full, s) in _marks)
            {
                bool useShort = shrt && s != null;
                full.Visibility = useShort ? Visibility.Collapsed : Visibility.Visible;
                if (s != null) s.Visibility = useShort ? Visibility.Visible : Visibility.Collapsed;
            }
            if (_you != null) _you.Visibility = youOn ? Visibility.Visible : Visibility.Collapsed;
            _cp.Visibility = cpOn ? Visibility.Visible : Visibility.Collapsed;
            _icon.Visibility = iconOn ? Visibility.Visible : Visibility.Collapsed;
            _text.ColumnDefinitions[1].Width = new GridLength(iconOn ? IconW : 0);
            _name.MaxWidth = Math.Max(Math.Min(nw, 24), room - marks - (cpOn ? cp : 0));
        }

        /// <summary>The width of the name's first ten characters and an ellipsis (the whole name when shorter).</summary>
        double NameMin()
        {
            var n = _name.Text;
            if (n.Length <= 10) return NameW();
            var t = Tt(n[..10] + "…", _name.FontSize, WhiteB, _name.FontWeight);
            return Mod.W(t);
        }

        /// <summary>A light running along the bar (a kill's glint on the top row), 0..1; -1 none.</summary>
        public void SetGlint(double at) { _glint = at; _bar.InvalidateVisual(); }

        void DrawBar(DrawingContext dc, Size s)
        {
            if (_m is not { } m || s.Width < 20) return;
            double x0 = Pad, y = H - BarH, w = s.Width - 2 * Pad;
            dc.DrawRoundedRectangle(TrackB, null, new Rect(x0, y, w, BarH), BarH / 2, BarH / 2);
            if (m.CpOnly || _fill <= 0) return;
            double fw = Math.Max(BarH, w * Math.Clamp(_fill, 0, 1));
            // the soft glow: three rounded halos round the fill, fainter outward (box-shadow 0 0 8px of the class's colour)
            foreach (var (g, b) in new[] { (4.5, _glowFar), (3.0, _glowMid), (1.5, _glowNear) })
                dc.DrawRoundedRectangle(b, null, new Rect(x0 - g, y - g, fw + 2 * g, BarH + 2 * g), BarH / 2 + g, BarH / 2 + g);
            var fill = new Rect(x0, y, fw, BarH);
            dc.DrawRoundedRectangle(_barB, null, fill, BarH / 2, BarH / 2);
            if (_glint is > 0 and < 1)
            {
                double gx = x0 - 30 + (fw + 60) * _glint;
                dc.PushClip(new RectangleGeometry(new Rect(x0, y - 3, fw, BarH + 6), 3, 3));
                var lg = Mod.Grad(false, (Color.FromArgb(0, 255, 255, 255), 0), (Color.FromArgb(0xF0, 255, 255, 255), .5), (Color.FromArgb(0, 255, 255, 255), 1));
                dc.DrawRectangle(lg, null, new Rect(gx - 22, y - 3, 44, BarH + 6));
                dc.Pop();
            }
        }
    }
}
