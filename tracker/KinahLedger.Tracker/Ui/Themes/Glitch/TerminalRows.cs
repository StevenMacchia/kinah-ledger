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

    public const double Pitch = 21, Height = 19, GapHeight = 15;
    const double RankW = 11, Gap0 = 5;

    /// <summary>A text's width in the screen's face (one line, no trimming).</summary>
    static double W(string s, double size, FontWeight w) => TextW(s, Face, size, w);

    /// <summary>
    /// The columns' widths from the widest form each figure takes in a real fight (Steven, 2026-11-17: "a lot of numbers in all
    /// the meters you can't see, it's cut off!"): DPS "199.9K", Combat Power "167.5K", a heal "+12.3K/s" (in its TOP HEALS block),
    /// a share "33.3%", an expanded row's "888.88M 88%", the class codes in the language shown; each <see cref="Gap0"/> px clear
    /// of its neighbour. Worked out once per language, so its own words count.
    /// </summary>
    static (double Cls, double Cp, double Heal, double Extra, double Dps, double Share) Widths()
    {
        if (_widths is { } w0 && _widthsLang == L.Code) return w0;
        double cls = new[] { "gladiator", "templar", "assassin", "ranger", "sorcerer", "spiritmaster", "cleric", "chanter" }.Max(c => W(ClassCode(c), 10.5, FontWeights.Bold));
        string per = L.F("{0}/s", "");
        var w = (Math.Ceiling(cls + 4), Math.Ceiling(Gap0 - 1 + W("167.5K", 10, FontWeights.SemiBold) + 2), Math.Ceiling(4 + 2 + W("+12.3K", 10, FontWeights.SemiBold) + W(per, 8.5, FontWeights.SemiBold) + 4),
            Math.Ceiling(Gap0 - 1 + W("888.88M 88%", 10.5, FontWeights.SemiBold)), Math.Ceiling(Gap0 + W("199.9K", 12, FontWeights.Bold)), Math.Ceiling(Gap0 + W("33.3%", 10.5, FontWeights.SemiBold) + 2));
        _widths = w; _widthsLang = L.Code;
        return w;
    }
    static (double, double, double, double, double, double)? _widths;
    static string _widthsLang = "";

    /// <summary>The columns every line and the header share: rank, class, name, Combat Power, load, heal, extra (expanded), DPS, share.</summary>
    static Grid Columns(double height, bool expanded)
    {
        var g = new Grid { Height = height };
        var (cls, cp, heal, extra, dps, share) = Widths();
        // the name takes the room: the load is a third of it, never under 36 px (a few cells between its brackets)
        foreach (var w in new[] { new GridLength(RankW), new GridLength(cls), new GridLength(3, GridUnitType.Star), new GridLength(cp), new GridLength(2, GridUnitType.Star), new GridLength(heal), new GridLength(expanded ? extra : 0), new GridLength(dps), new GridLength(share) })
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = w });
        // a 10-character name whole (its own margin in), and the [load] brackets never squeezed small (Steven, 2026-11-17: "too small")
        g.ColumnDefinitions[2].MinWidth = Math.Ceiling(4 + W("Mordwulfen", 11, FontWeights.Bold)); g.ColumnDefinitions[4].MinWidth = 40;
        return g;
    }

    static TextBlock At(Grid g, int col, TextBlock t) { Grid.SetColumn(t, col); g.Children.Add(t); return t; }

    /// <summary>The table's head, inverted as `top` draws it.</summary>
    public static FrameworkElement Header(bool expanded)
    {
        var g = Columns(15, expanded);
        var bar = new Border { Background = Br(PhosC, .85) };
        Grid.SetColumnSpan(bar, 9); g.Children.Add(bar);
        var rank = At(g, 0, Txt("#", 10, Ground, Face)); rank.HorizontalAlignment = HorizontalAlignment.Right;
        var cls = At(g, 1, Txt(L.T("CLS"), 10, Ground, Face)); cls.Margin = new Thickness(4, 0, 0, 0);
        At(g, 2, Txt(L.T("NAME"), 10, Ground, Face)).Margin = new Thickness(4, 0, 0, 0);
        var cp = At(g, 3, Txt(L.T("CP"), 10, Ground, Face)); cp.HorizontalAlignment = HorizontalAlignment.Right; cp.Margin = new Thickness(0, 0, 2, 0);
        var load = At(g, 4, Txt(L.T("LOAD"), 10, Ground, Face)); load.Margin = new Thickness(4, 0, 0, 0);
        var dps = At(g, 7, Txt(L.T("DPS"), 10, Ground, Face)); dps.HorizontalAlignment = HorizontalAlignment.Right;
        var shr = At(g, 8, Txt(L.T("SHR%"), 10, Ground, Face)); shr.HorizontalAlignment = HorizontalAlignment.Right; shr.Margin = new Thickness(0, 0, 2, 0);
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
        readonly Grid _root = Columns(TerminalRows.Height, false);
        readonly Border _band = new() { Visibility = Visibility.Collapsed, Margin = new Thickness(-3, -1, -3, -1) };
        readonly Border _bloom = new() { IsHitTestVisible = false, Margin = new Thickness(-3, -1, -3, -1) };
        readonly Border _rankBox = new() { HorizontalAlignment = HorizontalAlignment.Right, Padding = new Thickness(2, 0, 2, 0), VerticalAlignment = VerticalAlignment.Center };
        readonly TextBlock _rank = Txt("", 11, PhosDim, Face, FontWeights.SemiBold);
        readonly TextBlock _cls = Txt("", 10.5, Phos, Face);
        readonly Border _death = new() { Background = Br(0x3A0D0D), Padding = new Thickness(1.5, 0, 1.5, 0), VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
        readonly Border _rules = new() { BorderBrush = Br(PhosC, .55), BorderThickness = new Thickness(0, 1, 0, 1), Margin = new Thickness(-3, -1, -3, -1), Visibility = Visibility.Collapsed };
        readonly TextBlock _name = Txt("", 11, Phos, Face, FontWeights.SemiBold, trim: true);
        /// <summary>The Combat Power in the CP column beside the name, smaller and dimmer than it.</summary>
        readonly TextBlock _cp = Txt("", 10, PhosDim, Face, FontWeights.SemiBold);
        readonly Border _tags = new() { Padding = new Thickness(1.5, 0, 1.5, 0) };
        // a death and a tag side by side fit the load's least width (a healer's "†2 HEAL"): from its left, never past either edge
        readonly StackPanel _marks = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(3, 0, 0, 0) };
        /// <summary>A healer's figure; the top healer's inverted in mint (the TOP HEALS tag, its figure in it: a HEAL tag in the load beside a death had no room).</summary>
        readonly Border _healBox = new() { Padding = new Thickness(2, 0, 3, 0), Margin = new Thickness(4, 0, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        /// <summary>The load: [cells] in the class's colour; a death or a kill's top takes its place (the share stays in SHR%).</summary>
        readonly Grid _load = new() { Margin = new Thickness(4, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        readonly GlitchLevel _bar = new(13, Br(PhosC, .12), Cells);
        readonly TextBlock _heal = Txt("", 10, Mint2, Face, FontWeights.SemiBold), _extra = Txt("", 10.5, PhosDim, Face, FontWeights.SemiBold);
        readonly TextBlock _fig = FigCell(12, Pale, Face, 0), _share = FigCell(10.5, Br(PhosC, .82), Face, 0, FontWeights.SemiBold);

        public FrameworkElement Root => _root;

        public Row()
        {
            Grid.SetColumnSpan(_band, 9); _root.Children.Add(_band);
            _rankBox.Child = _rank; _root.Children.Add(_rankBox);
            _cls.Margin = new Thickness(4, 0, 0, 0); Grid.SetColumn(_cls, 1); _root.Children.Add(_cls);
            Grid.SetColumnSpan(_rules, 9); _root.Children.Add(_rules);
            var name = new DockPanel { LastChildFill = true, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) };
            name.Children.Add(_name);
            // the Combat Power in its own column right after the name, as `top` lists a figure a column
            _cp.TextAlignment = TextAlignment.Right; _cp.HorizontalAlignment = HorizontalAlignment.Right; _cp.Margin = new Thickness(0, 0, 2, 0);
            Grid.SetColumn(_cp, 3); _root.Children.Add(_cp);
            Grid.SetColumn(name, 2); _root.Children.Add(name);
            // the load: [cells] in the class's colour
            var load = _load;
            load.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); load.ColumnDefinitions.Add(new ColumnDefinition()); load.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            load.Children.Add(Txt("[", 14, PhosDim, Face, FontWeights.SemiBold));
            _bar.Root.Margin = new Thickness(1, 0, 1, 0); _bar.Root.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(_bar.Root, 1); load.Children.Add(_bar.Root);
            var close = Txt("]", 14, PhosDim, Face, FontWeights.SemiBold); Grid.SetColumn(close, 2); load.Children.Add(close);
            Grid.SetColumn(load, 4); _root.Children.Add(load);
            // a kill's tags over the load's start, on the screen's ground (the name keeps its room)
            // a death and a kill's top sit at the load's end, where its empty cells are (the name keeps its room)
            _tags.VerticalAlignment = VerticalAlignment.Center; _death.Margin = new Thickness(0, 0, 2, 0);
            _marks.Children.Add(_death); _marks.Children.Add(_tags);
            Grid.SetColumn(_marks, 4); _root.Children.Add(_marks);
            _healBox.Child = _heal; Grid.SetColumn(_healBox, 5); _root.Children.Add(_healBox);
            _extra.Margin = new Thickness(Gap0 - 1, 0, 0, 0); At(_root, 6, _extra);
            At(_root, 7, _fig); At(_root, 8, _share);
            _share.Margin = new Thickness(0, 0, 2, 0);
            Grid.SetColumnSpan(_bloom, 9); _root.Children.Add(_bloom);
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
            _cp.Inlines.Clear();
            if (m.Cp.Length > 0) _cp.Inlines.Add(Rn(m.Cp, you ? Br(PhosC, .9) : PhosDim));
            _cp.Visibility = m.Cp.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            _death.Child = m.Deaths > 0 ? Txt("†" + m.Deaths.ToString(CultureInfo.InvariantCulture), 11, Red, Face) : null;
            _death.Visibility = m.Deaths > 0 ? Visibility.Visible : Visibility.Collapsed;
            bool healTag = m.TopHeals && !m.TopDps && m.Heal.Length > 0;   // the top healer's tag is their heal figure, inverted
            bool top = m.TopDps || (m.TopHeals && !healTag);
            _load.Visibility = m.Deaths > 0 || top ? Visibility.Hidden : Visibility.Visible;
            _tags.Background = m.TopDps ? Amber : Mint2;
            _tags.Child = top ? Txt(L.T(m.TopDps ? "TOP" : "HEAL"), 10, Ground, Face) : null;
            _tags.Visibility = top ? Visibility.Visible : Visibility.Collapsed;
            _bar.Fill.Background = Frozen(cc);
            _bar.Root.Visibility = m.CpOnly ? Visibility.Hidden : Visibility.Visible;
            _bar.Slide(c.Key + "|bar", m.Share);
            _heal.Inlines.Clear();
            if (m.Heal.Length > 0)
            {
                // "+1.1K" whole, its "/s" dimmer and smaller, so the rate fits its column
                int sl = m.Heal.LastIndexOf('/'); bool ps = sl > 0;   // "/s" in English, "/초" in Korean
                _heal.Inlines.Add(Rn("+" + (ps ? m.Heal[..sl] : m.Heal), healTag ? Ground : Mint2));
                if (ps) _heal.Inlines.Add(Rn(m.Heal[sl..], healTag ? Br(GroundC, .75) : Br(MintC2, .6), 8.5));
            }
            _healBox.Background = healTag ? Mint2 : null;
            _healBox.Visibility = m.Heal.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            _root.ColumnDefinitions[6].Width = new GridLength(c.Expanded ? Widths().Extra : 0);
            _extra.Text = c.Expanded && m.Damage.Length > 0 ? m.Damage + (m.Crit.Length > 0 ? " " + m.Crit : "") : "";
            _extra.Visibility = _extra.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            // before a hit: the Combat Power short, as the CP column writes it ("167.5K"), so it keeps the DPS's column
            _fig.Text = m.CpOnly && m.FigureValue > 0 ? Amounts.Meter(m.FigureValue) : m.Figure;
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
        if (ff.Label.Length > 0)
        {
            r.Inlines.Add(Rn(ff.Label.ToLower(CultureInfo.CurrentCulture) + " ", PhosDim, 11.5, FontWeights.SemiBold));
            r.Inlines.Add(Rn(ff.Dps, Pale));
        }
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
