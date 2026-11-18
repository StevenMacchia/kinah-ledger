using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using KinahLedger.Parser;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

sealed partial class HudModernMono
{
    /// <summary>The fight's foot: "Total 622.4M" at the left, the group's word and figure at the right, the AVG mark when held.</summary>
    FrameworkElement Foot(Encounter e, DateTime now)
    {
        var (total, word, group, held) = FootOf(e, now);
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = new StackPanel { Orientation = Orientation.Horizontal };
        left.Children.Add(Tt(L.T("Total"), 11, Grey, FontWeights.Medium));
        var tv = Tt(total, 12, Paper, FontWeights.Bold); tv.Margin = new Thickness(6, 0, 0, 0); left.Children.Add(tv);
        g.Children.Add(left);
        var right = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 0, 0, 0) };
        if (word.Length > 0)
        {
            right.Children.Add(Tt(word, 11, Grey, FontWeights.Medium));
            var gv = Tt(group, 12, Paper, FontWeights.Bold); gv.Margin = new Thickness(6, 0, 0, 0); right.Children.Add(gv);
        }
        if (held)
        {
            var avg = Mark(L.T("AVG"), Paper, filled: false);
            avg.Tag = "avg-badge"; avg.ToolTip = L.T(Encounter.AvgBadge); avg.Margin = new Thickness(8, 0, 0, 0);
            right.Children.Add(avg);
        }
        Grid.SetColumn(right, 1); g.Children.Add(right);
        return AsFoot(g, e, now);
    }

    /// <summary>The line between the party and you pinned below it: "2 MORE", small, grey, centred.</summary>
    static FrameworkElement GapLine(int left)
    {
        var t = Mod.Text(left > 0 ? L.F("{0} more", left).ToUpperInvariant() : "", 9.5, Grey, FontWeights.SemiBold);
        t.HorizontalAlignment = HorizontalAlignment.Center;
        t.Margin = new Thickness(0, 0, 0, 4);
        return t;
    }

    /// <summary>A small uppercase mark with 2 px corners: outlined in its colour, or filled with dark words (YOU, KILLED).</summary>
    static Border Mark(string s, SolidColorBrush c, bool filled)
    {
        var t = Mod.Text(s, 8.5, filled ? Dark : c, FontWeights.Bold);
        t.Margin = new Thickness(0, 0, 0, 1);
        return new Border
        {
            Child = t, BorderBrush = c, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(Corner2), Background = filled ? c : null,
            Padding = new Thickness(4, 1, 4, 0), VerticalAlignment = VerticalAlignment.Center,
        };
    }

    static readonly Dictionary<string, ImageSource?> Greys = new();

    /// <summary>The class's emblem in greyscale (the game's art, its colour taken out), 2 px corners; an initial in grey when the class isn't known.</summary>
    static FrameworkElement ClassMark(string cls, string name, double size)
    {
        var key = ClassArt.Key(cls);
        if (!Greys.TryGetValue(key, out var grey))
        {
            grey = null;
            if (ClassArt.Image(cls) is BitmapSource src)
            {
                try { var g = new FormatConvertedBitmap(src, PixelFormats.Gray8, null, 0); g.Freeze(); grey = g; } catch (Exception) { grey = null; }
            }
            Greys[key] = grey;
        }
        var host = new Grid { Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center };
        if (grey != null && ClassArt.Image(cls) is { } colour)
        {
            // the grey copy drawn through the art's own shape (Gray8 has no alpha), a touch brighter
            var img = new Image { Source = grey, Width = size, Height = size, Stretch = Stretch.Uniform, OpacityMask = new ImageBrush(colour) { Stretch = Stretch.Uniform } };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            host.Children.Add(img);
            host.Children.Add(new Border { Background = Frozen(Mod.A(PaperC, .22)), OpacityMask = new ImageBrush(colour) { Stretch = Stretch.Uniform } });
        }
        else
        {
            host.Children.Add(new Border { CornerRadius = new CornerRadius(Corner2), BorderBrush = Grey, BorderThickness = new Thickness(1) });
            var letter = Mod.Text(name.Trim().Length > 0 ? name.Trim()[..1].ToUpperInvariant() : "·", size * .55, Grey, FontWeights.Bold);
            letter.HorizontalAlignment = HorizontalAlignment.Center;
            host.Children.Add(letter);
        }
        if (key.Length > 0) host.ToolTip = ThemeText.Class(cls);
        return host;
    }

    /// <summary>
    /// One member: the rank small and grey, the class's emblem in grey, the name in off-white with its CP after it and the
    /// marks (★ DPS, ✚ HEAL, YOU in the accent, ✕ deaths in red), a healer's heals a second in green, the DPS big and the share
    /// small at the right; under it all, a flat 4 px bar (off-white; yours in the accent) as long as the member's DPS against
    /// the top's. Expanded: the damage and the crit rate after the share. The crowding rule (<see cref="Mod.Fit"/>): the CP goes
    /// first, then the share; the name keeps ten characters at least (the marks fold to their glyphs before it is cut further);
    /// figures are never cut.
    /// </summary>
    sealed class MonoRow : IThemeRow
    {
        public const double H = 34, Pitch = 40, BarH = 4;
        const double RankW = 16, IconW = 24, ShareW = 44, DmgW = 56, CritW = 36;
        readonly Grid _root = new() { Height = H, Background = Brushes.Transparent };
        readonly TacSketch _bar;
        readonly TextBlock _rank = Mod.Text("", 11, Grey, FontWeights.Medium), _name = Mod.Text("", 14, Paper, FontWeights.SemiBold),
            _cp = Mod.Text("", 10.5, Grey, FontWeights.Medium), _heal = Mod.Text("", 11, Green, FontWeights.SemiBold),
            _dps = Mod.Text("", 17, Paper, FontWeights.Bold), _share = Mod.Text("", 10.5, Soft, FontWeights.SemiBold),
            _dmg = Mod.Text("", 12, Soft, FontWeights.SemiBold), _crit = Mod.Text("", 10.5, Grey, FontWeights.SemiBold);
        readonly Grid _icon = new() { Width = 16, Height = 16, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
        readonly StackPanel _line = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, ClipToBounds = false };
        readonly List<FrameworkElement> _marks = new();
        readonly List<(FrameworkElement Full, FrameworkElement Short)> _folds = new();
        string _iconKey = "\0";
        MeterMember? _m;
        double _len;
        bool _you, _expanded;

        public FrameworkElement Root => _root;

        public MonoRow()
        {
            // rank | emblem | name, CP, marks | heals | DPS | share | (damage | crit)
            foreach (var w in new[] { RankW, IconW, double.NaN, -1, -1, ShareW, 0, 0 })
                _root.ColumnDefinitions.Add(new ColumnDefinition { Width = double.IsNaN(w) ? new GridLength(1, GridUnitType.Star) : w < 0 ? GridLength.Auto : new GridLength(w) });
            _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(H - BarH - 4) });
            _root.RowDefinitions.Add(new RowDefinition());
            _bar = new TacSketch(DrawBar) { Height = BarH, VerticalAlignment = VerticalAlignment.Bottom };
            Grid.SetRow(_bar, 1); Grid.SetColumnSpan(_bar, 8);
            _root.Children.Add(_bar);
            Put(_rank, 0); Put(_icon, 1);
            _name.TextTrimming = TextTrimming.CharacterEllipsis;
            _line.Children.Add(_name);
            Put(_line, 2);
            _heal.Margin = new Thickness(8, 1, 0, 0); _heal.Tag = "heal-figures"; Put(_heal, 3);
            _dps.Margin = new Thickness(8, 0, 0, 0); _dps.Tag = "row-dps"; _dps.MinWidth = 52; _dps.TextAlignment = TextAlignment.Right; Put(_dps, 4);
            foreach (var (t, col) in new[] { (_share, 5), (_dmg, 6), (_crit, 7) }) { t.HorizontalAlignment = HorizontalAlignment.Right; t.Margin = new Thickness(0, 2, 0, 0); Put(t, col); }
            _cp.Margin = new Thickness(6, 2, 0, 0);
            _root.SizeChanged += (_, _) => Fit();
        }

        void Put(FrameworkElement el, int col) { Grid.SetColumn(el, col); _root.Children.Add(el); }

        public void Set(MeterMember m, MeterContext c, bool fresh)
        {
            _m = m; _you = m.You;
            if (_expanded != c.Expanded)
            {
                _expanded = c.Expanded;
                _root.ColumnDefinitions[6].Width = new GridLength(c.Expanded ? DmgW : 0);
                _root.ColumnDefinitions[7].Width = new GridLength(c.Expanded ? CritW : 0);
            }
            _rank.Text = m.Rank.ToString(CultureInfo.InvariantCulture);
            var ik = m.Class + "|" + (m.Name.Length > 0 ? m.Name[..1] : "");
            if (ik != _iconKey) { _iconKey = ik; _icon.Children.Clear(); _icon.Children.Add(ClassMark(m.Class, m.Name, 16)); }
            _name.Text = m.Name;
            _name.Foreground = Paper;
            while (_line.Children.Count > 1) _line.Children.RemoveAt(1);
            _marks.Clear(); _folds.Clear();
            _cp.Text = m.Cp;
            if (m.Cp.Length > 0) _line.Children.Add(_cp);
            // the marks after the name, each folding to its glyph when the row is short of room
            void Fold(string full, string glyph, SolidColorBrush tone, string tag, string tip, bool filled = false)
            {
                var f = Mark(full, tone, filled); f.Tag = tag; f.ToolTip = tip; f.Margin = new Thickness(6, 0, 0, 0);
                var s = Mark(glyph, tone, filled); s.Tag = tag; s.ToolTip = tip; s.Margin = new Thickness(6, 0, 0, 0); s.Visibility = Visibility.Collapsed;
                _line.Children.Add(f); _line.Children.Add(s);
                _folds.Add((f, s));
            }
            if (m.TopDps) Fold("★ " + L.T("DPS"), "★", Paper, "top-dps", L.T("Top DPS"));
            if (m.TopHeals) Fold("✚ " + L.T("HEAL"), "✚", Green, "top-heals", L.T("Top heals"));
            if (m.You) Fold(L.T("YOU"), "●", Accent, "you", L.T("You"), filled: true);
            if (m.Deaths > 0)
            {
                var d = Mod.Text(m.Deaths > 1 ? $"✕{m.Deaths}" : "✕", 11, Red, FontWeights.Bold); d.Margin = new Thickness(6, 1, 0, 0);
                d.ToolTip = L.F(m.Deaths == 1 ? "{0} death" : "{0} deaths", m.Deaths);
                _line.Children.Add(d); _marks.Add(d);
            }
            _heal.Text = m.Heal.Length > 0 ? "+" + m.Heal : "";
            _heal.Visibility = m.Heal.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            _dps.Text = m.Figure;
            _dps.FontSize = m.CpOnly ? 13 : 17;
            _dps.Foreground = m.CpOnly ? Soft : Paper;
            _share.Text = m.ShareText;
            _dmg.Text = c.Expanded ? m.Damage : "";
            _crit.Text = c.Expanded ? m.Crit : "";
            Fit();
            // the bar: the member's DPS against the top's (the mock-up's lengths), none before any damage
            double top = 0;
            if (!m.CpOnly) foreach (var f in c.E.Shown(c.Now)) if (!f.Other) top = Math.Max(top, c.E.Share(f));
            double len = m.CpOnly || top <= 0 ? 0 : Math.Clamp(m.Share / top, 0, 1);
            SlideBar(_bar, c, "bar", len, v => { _len = v; _bar.InvalidateVisual(); });
            _bar.InvalidateVisual();
        }

        /// <summary>
        /// The crowding rule over the row's room (Mod.Fit's order, kept so the DPS column stays one column): the CP goes first;
        /// then the marks fold to their glyphs (★, ✚, ●; their words on hover); then the share, a healer's heals moving into the
        /// share's place; only then is the name cut, never below ten characters. Figures are never cut.
        /// </summary>
        void Fit()
        {
            if (_m is not { } m) return;
            double total = _root.ActualWidth;
            if (total <= 0) return;
            Tight(false);
            bool healer = m.Heal.Length > 0;
            // the heals measured where they'd sit (the share's place takes them a size smaller)
            PlaceHeal(false);
            double healW = healer ? Mod.W(_heal) : 0;
            // from the name's start to the DPS figure, the share's column and the heals' counted in
            double room = total - RankW - IconW - Mod.W(_dps) - (_expanded ? DmgW + CritW : 0) - 4;
            _name.ClearValue(FrameworkElement.MaxWidthProperty);
            double nw = Mod.W(_name), cpw = m.Cp.Length > 0 ? Mod.W(_cp) : 0;
            double marks = _marks.Sum(Mod.W), full = _folds.Sum(x => Mod.W(x.Full)), glyphs = _folds.Sum(x => Mod.W(x.Short));
            double nameMin = Math.Min(nw, Mod.W(Mod.Text(m.Name.Length > 10 ? m.Name[..10] + "…" : m.Name, _name.FontSize, Paper, _name.FontWeight)));
            // (CP, folded, share): the first that fits, else the last with the name cut. The share goes only where a healer's
            // heals take its place: anywhere else its going would move that row's DPS off the column the others line up on
            var steps = healer ? new[] { (true, false, true), (false, false, true), (false, true, true), (false, true, false) }
                : new[] { (true, false, true), (false, false, true), (false, true, true) };
            (bool Cp, bool Fold, bool Share) pick = steps[^1];
            double Used((bool Cp, bool Fold, bool Share) s) => (s.Cp ? cpw : 0) + marks + (s.Fold ? glyphs : full) + (s.Share ? ShareW + healW : healer ? Math.Max(ShareW, healW) : 0);
            bool fits = false;
            foreach (var s in steps) if (nw + Used(s) <= room) { pick = s; fits = true; break; }
            // still short (a wider face, as Korean and Chinese draw in): the air between the marks and before the DPS narrows
            // before the name is cut, so every part stays shown
            if (!fits)
            {
                Tight(true);
                room = total - RankW - IconW - Mod.W(_dps) - (_expanded ? DmgW + CritW : 0) - 4;
                marks = _marks.Sum(Mod.W); full = _folds.Sum(x => Mod.W(x.Full)); glyphs = _folds.Sum(x => Mod.W(x.Short));
                foreach (var s in steps) if (nw + Used(s) <= room) { pick = s; break; }
            }
            _cp.Visibility = pick.Cp && cpw > 0 ? Visibility.Visible : Visibility.Collapsed;
            foreach (var (f, s) in _folds) { f.Visibility = pick.Fold ? Visibility.Collapsed : Visibility.Visible; s.Visibility = pick.Fold ? Visibility.Visible : Visibility.Collapsed; }
            _share.Visibility = pick.Share ? Visibility.Visible : Visibility.Collapsed;
            PlaceHeal(healer && !pick.Share);
            _root.ColumnDefinitions[5].Width = new GridLength(pick.Share ? ShareW : healer ? Math.Max(ShareW, healW) : 0);
            _name.MaxWidth = Math.Max(nameMin, room - Used(pick));
        }

        /// <summary>The air between the marks (6 px, 3 when tight) and before the DPS figure (8 px, 4 when tight).</summary>
        void Tight(bool on)
        {
            double g = on ? 3 : 6;
            foreach (var (f, s) in _folds) { f.Margin = new Thickness(g, 0, 0, 0); s.Margin = new Thickness(g, 0, 0, 0); }
            foreach (var d in _marks) d.Margin = new Thickness(g, 1, 0, 0);
            _dps.Margin = new Thickness(on ? 4 : 8, 0, 0, 0);
        }

        /// <summary>The heals a second after the name (column 3), or in the share's place right of the DPS when the share has gone.</summary>
        void PlaceHeal(bool inShare)
        {
            Grid.SetColumn(_heal, inShare ? 5 : 3);
            _heal.HorizontalAlignment = inShare ? HorizontalAlignment.Right : HorizontalAlignment.Left;
            _heal.Margin = inShare ? new Thickness(0, 2, 0, 0) : new Thickness(8, 1, 0, 0);
            _heal.FontSize = inShare ? 10.5 : 11;
        }

        void DrawBar(DrawingContext dc, Size s)
        {
            if (s.Width < 10) return;
            dc.DrawRectangle(TrackB, null, new Rect(0, 0, s.Width, s.Height));
            double fx = Math.Round(s.Width * Math.Clamp(_len, 0, 1));
            if (fx >= 1) dc.DrawRectangle(_you ? Accent : Frozen(Mod.A(PaperC, .82)), null, new Rect(0, 0, fx, s.Height));
        }
    }
}
