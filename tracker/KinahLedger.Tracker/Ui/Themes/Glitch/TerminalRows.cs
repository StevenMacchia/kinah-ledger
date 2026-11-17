using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using KinahLedger.Parser;
using static KinahLedger.Ui.HudKit;
using static KinahLedger.Ui.GlitchKit;

namespace KinahLedger.Ui;

/// <summary>
/// Terminal's meter: a `top` table. An inverted header bar ("  #  NAME   LOAD   DPS   SHR%"), then one line a member in
/// Cascadia Mono: the rank, a square in their class's colour, the name in phosphor green, the load as a bracketed bar of
/// block cells in their class's colour, a healer's "+1.1K/s", the DPS in pale green and the share, right-aligned. You:
/// the rank inverted, your name bright, a lit band behind the line. Deaths a red †, [top] / [heal] in amber / mint at a
/// kill. The foot: a dashed rule, "total 2.03M" and "group dps 12.7K", [avg] once the fight is held.
/// </summary>
static class TerminalRows
{
    static readonly FontFamily Face = CrtFace;
    const uint PhosC = 0x5CFF9A, PaleC = 0xD6FFE4, AmberC = 0xFFB641, GroundC = 0x031008, RedC = 0xFF6B5E, MintC2 = 0x7DFFC4;
    static readonly Brush Phos = Br(PhosC), PhosDim = Br(PhosC, .72), Pale = Br(PaleC), Amber = Br(AmberC), Ground = Br(GroundC), Red = Br(RedC), Mint2 = Br(MintC2);
    static readonly Brush Cells = Segments(Rgb(GroundC), 5, 1.5);

    public const double Pitch = 19, Height = 17, GapHeight = 15;
    const double RankW = 20, DpsW = 46, ShareW = 44;

    /// <summary>The columns every line and the header share: rank, mark, name, load, heal, extra, DPS, share.</summary>
    static Grid Columns(double height)
    {
        var g = new Grid { Height = height };
        foreach (var w in new[] { new GridLength(RankW), new GridLength(30), new GridLength(1.45, GridUnitType.Star), new GridLength(1, GridUnitType.Star), new GridLength(54), GridLength.Auto, new GridLength(DpsW), new GridLength(ShareW) })
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = w });
        return g;
    }

    static TextBlock At(Grid g, int col, TextBlock t) { Grid.SetColumn(t, col); g.Children.Add(t); return t; }

    /// <summary>The table's head, inverted as `top` draws it.</summary>
    public static FrameworkElement Header()
    {
        var g = Columns(15);
        var bar = new Border { Background = Br(PhosC, .85) };
        Grid.SetColumnSpan(bar, 8); g.Children.Add(bar);
        var rank = At(g, 0, Txt("#", 10, Ground, Face)); rank.HorizontalAlignment = HorizontalAlignment.Right;
        var cls = At(g, 1, Txt(L.T("CLS"), 10, Ground, Face)); cls.Margin = new Thickness(6, 0, 0, 0);
        At(g, 2, Txt(L.T("NAME"), 10, Ground, Face));
        var load = At(g, 3, Txt(L.T("LOAD"), 10, Ground, Face)); load.Margin = new Thickness(6, 0, 0, 0);
        var dps = At(g, 6, Txt(L.T("DPS"), 10, Ground, Face)); dps.HorizontalAlignment = HorizontalAlignment.Right;
        var shr = At(g, 7, Txt(L.T("SHR%"), 10, Ground, Face)); shr.HorizontalAlignment = HorizontalAlignment.Right; shr.Margin = new Thickness(0, 0, 2, 0);
        g.Margin = new Thickness(0, 6, 0, 3);
        return g;
    }

    public static FrameworkElement Gap(int more)
    {
        var t = Txt(more > 0 ? "  … " + L.F("{0} more", more) : "  …", 10, PhosDim, Face, FontWeights.SemiBold);
        return t;
    }

    public sealed class Row : IThemeRow
    {
        readonly Grid _root = Columns(TerminalRows.Height);
        readonly Border _band = new() { Visibility = Visibility.Collapsed, Margin = new Thickness(-3, -1, -3, -1) };
        readonly Border _bloom = new() { IsHitTestVisible = false, Margin = new Thickness(-3, -1, -3, -1) };
        readonly Border _rankBox = new() { HorizontalAlignment = HorizontalAlignment.Right, Padding = new Thickness(2, 0, 2, 0), VerticalAlignment = VerticalAlignment.Center };
        readonly TextBlock _rank = Txt("", 11, PhosDim, Face, FontWeights.SemiBold);
        readonly TextBlock _cls = Txt("", 10.5, Phos, Face);
        readonly Border _death = new() { Background = Br(0x3A0D0D), Padding = new Thickness(2, 0, 2, 0), Margin = new Thickness(4, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
        readonly Border _rules = new() { BorderBrush = Br(PhosC, .55), BorderThickness = new Thickness(0, 1, 0, 1), Margin = new Thickness(-3, -1, -3, -1), Visibility = Visibility.Collapsed };
        readonly TextBlock _name = Txt("", 11.5, Phos, Face, FontWeights.SemiBold, trim: true);
        readonly Border _tags = new() { Padding = new Thickness(2, 0, 2, 0) };
        readonly StackPanel _marks = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 9, 0) };
        readonly GlitchLevel _bar = new(9, Br(PhosC, .1), Cells);
        readonly TextBlock _heal = Txt("", 10.5, Mint2, Face, FontWeights.SemiBold), _extra = Txt("", 10.5, PhosDim, Face, FontWeights.SemiBold);
        readonly TextBlock _fig = FigCell(12, Pale, Face, DpsW), _share = FigCell(10.5, Br(PhosC, .82), Face, ShareW, FontWeights.SemiBold);

        public FrameworkElement Root => _root;

        public Row()
        {
            Grid.SetColumnSpan(_band, 8); _root.Children.Add(_band);
            _rankBox.Child = _rank; _root.Children.Add(_rankBox);
            _cls.Margin = new Thickness(6, 0, 0, 0); Grid.SetColumn(_cls, 1); _root.Children.Add(_cls);
            Grid.SetColumnSpan(_rules, 8); _root.Children.Add(_rules);
            var name = new DockPanel { LastChildFill = true, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
            name.Children.Add(_name);
            Grid.SetColumn(name, 2); _root.Children.Add(name);
            // the load: [cells] in the class's colour
            var load = new Grid { Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            load.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); load.ColumnDefinitions.Add(new ColumnDefinition()); load.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            load.Children.Add(Txt("[", 11.5, PhosDim, Face, FontWeights.SemiBold));
            _bar.Root.Margin = new Thickness(1, 0, 1, 0); _bar.Root.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(_bar.Root, 1); load.Children.Add(_bar.Root);
            var close = Txt("]", 11.5, PhosDim, Face, FontWeights.SemiBold); Grid.SetColumn(close, 2); load.Children.Add(close);
            Grid.SetColumn(load, 3); _root.Children.Add(load);
            // a kill's tags over the load's start, on the screen's ground (the name keeps its room)
            // a death and a kill's top sit at the load's end, where its empty cells are (the name keeps its room)
            _tags.VerticalAlignment = VerticalAlignment.Center; _death.Margin = new Thickness(0, 0, 3, 0);
            _marks.Children.Add(_death); _marks.Children.Add(_tags);
            Grid.SetColumn(_marks, 3); _root.Children.Add(_marks);
            _heal.Margin = new Thickness(5, 0, 0, 0); At(_root, 4, _heal);
            _extra.Margin = new Thickness(5, 0, 0, 0); At(_root, 5, _extra);
            At(_root, 6, _fig); At(_root, 7, _share);
            _share.Margin = new Thickness(0, 0, 2, 0);
            Grid.SetColumnSpan(_bloom, 8); _root.Children.Add(_bloom);
        }

        public void Set(MeterMember m, MeterContext c, bool fresh)
        {
            bool you = m.You;
            var cc = m.ClassColor;
            _rank.Text = m.Rank.ToString(CultureInfo.InvariantCulture);
            _rankBox.Background = you ? Phos : null;
            _rank.Foreground = you ? Ground : PhosDim;
            _band.Visibility = you ? Visibility.Visible : Visibility.Collapsed;
            _band.Background = Br(PhosC, .22);
            _rules.Visibility = you ? Visibility.Visible : Visibility.Collapsed;
            _cls.Text = ClassCode(m.Class);
            _cls.Foreground = Frozen(cc);
            _name.Foreground = you ? Pale : Phos;
            _name.FontWeight = you ? FontWeights.Bold : FontWeights.SemiBold;
            // a death: a red dagger after the name; a kill's tops: an inverted block at the load's end, where its empty cells are
            _name.Inlines.Clear();
            _name.Inlines.Add(Rn(m.Name, you ? Br(0xE8FFEE) : Phos));
            if (you) _name.Inlines.Add(Rn("*", Amber));
            _death.Child = m.Deaths > 0 ? Txt("†" + m.Deaths.ToString(CultureInfo.InvariantCulture), 11, Red, Face) : null;
            _death.Visibility = m.Deaths > 0 ? Visibility.Visible : Visibility.Collapsed;
            bool top = m.TopDps || m.TopHeals;
            _tags.Background = m.TopDps ? Amber : Mint2;
            _tags.Child = top ? Txt(L.T(m.TopDps ? "TOP" : "HEAL"), 10, Ground, Face) : null;
            _tags.Visibility = top ? Visibility.Visible : Visibility.Collapsed;
            _bar.Fill.Background = Frozen(cc);
            _bar.Root.Visibility = m.CpOnly ? Visibility.Hidden : Visibility.Visible;
            _bar.Slide(c.Key + "|bar", m.Share);
            _heal.Text = m.Heal.Length > 0 ? "+" + m.Heal : "";
            _heal.Visibility = m.Heal.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            _extra.Text = c.Expanded && m.Damage.Length > 0 ? m.Damage + (m.Crit.Length > 0 ? " " + m.Crit : "") : "";
            _extra.Visibility = _extra.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            _fig.Text = m.Figure;
            _fig.FontWeight = FontWeights.Bold;
            _share.Text = m.ShareText;
        }

        /// <summary>A phosphor bloom on this line (a kill's top DPS), decaying as phosphor does.</summary>
        public void Bloom(Color c, double ms)
        {
            var bg = new SolidColorBrush(Color.FromArgb(0, c.R, c.G, c.B));
            _bloom.Background = bg;
            Motion.Play(_bloom, ms, t =>
            {
                double a = t < .06 ? t / .06 : Math.Pow(1 - (t - .06) / .94, 2.2);
                bg.Color = Color.FromArgb((byte)(255 * .4 * a), c.R, c.G, c.B);
            }, () => _bloom.Background = null);
        }
    }

    /// <summary>The foot: a dashed rule, "total 2.03M" and "group dps 12.7K", [avg] once the fight is held.</summary>
    public static FrameworkElement Foot(Encounter e, DateTime now)
    {
        var ff = FootOf(e, now);
        var sp = new StackPanel { Margin = new Thickness(0, 5, 0, 0) };
        sp.Children.Add(new Rectangle { Height = 1, Fill = Dashes, Margin = new Thickness(0, 0, 0, 4) });
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = Txt("", 13, Pale, Face);
        left.Inlines.Add(Rn(L.T("Total ").Trim().ToLower(CultureInfo.CurrentCulture) + " ", PhosDim, 11.5, FontWeights.SemiBold));
        left.Inlines.Add(Rn(ff.Total, Pale));
        g.Children.Add(left);
        var r = Txt("", 13, Pale, Face);
        r.Inlines.Add(Rn(ff.Label.ToLower(CultureInfo.CurrentCulture) + " ", PhosDim, 11.5, FontWeights.SemiBold));
        r.Inlines.Add(Rn(ff.Dps, Pale));
        if (ff.Held) r.Inlines.Add(Rn(" [" + L.T("avg") + "]", Amber, 11.5));
        Grid.SetColumn(r, 1); g.Children.Add(r);
        sp.Children.Add(g);
        TagFoot(sp, e, now);
        return sp;
    }

    static readonly Brush Dashes = MakeDashes();
    static Brush MakeDashes()
    {
        var d = new DrawingGroup();
        d.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, 6, 1))));
        d.Children.Add(new GeometryDrawing(Br(PhosC, .45), null, new RectangleGeometry(new Rect(0, 0, 4, 1))));
        var b = new DrawingBrush(d) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 6, 1), ViewportUnits = BrushMappingMode.Absolute, Stretch = Stretch.Fill };
        b.Freeze();
        return b;
    }
}
