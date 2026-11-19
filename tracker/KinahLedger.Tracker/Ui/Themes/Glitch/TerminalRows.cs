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
/// Terminal's meter: `htop` over `top`. An inverted header bar ("  #  NAME   LOAD   DPS"), then two lines a member in Cascadia
/// Mono. The first is the bar line, the row's hero (Steven, 2026-11-17: "figure out better looks that doesn't crowd everything
/// and prioritizes the bar"): the rank, the name in phosphor green, the load as a wide bracketed bar of block cells in their
/// class's colour, as long as their DPS against the top member's, the share inside its brackets as htop prints a meter's
/// percent, and the DPS big in pale green at the end. The second, quiet, under the name: the class's code, the Combat Power,
/// a death's red †, [top] / [heal] in amber / mint at a kill, a healer's "+12.3K/s", and an expanded meter's damage and crit
/// under the DPS. You: the rank inverted, your name bright, a lit band behind both lines. The foot: a dashed rule,
/// "total 2.03M" and "group dps 12.7K", [avg] once the fight is held.
/// </summary>
static class TerminalRows
{
    static readonly FontFamily Face = CrtFace;
    const uint PhosC = 0x5CFF9A, PaleC = 0xD6FFE4, AmberC = 0xFFB641, GroundC = 0x031008, RedC = 0xFF6B5E, MintC2 = 0x7DFFC4;
    static readonly Brush Phos = Br(PhosC), PhosDim = Br(PhosC, .72), PhosFaint = Br(PhosC, .5), Pale = Br(PaleC), Amber = Br(AmberC), Ground = Br(GroundC), Red = Br(RedC), Mint2 = Br(MintC2);
    static readonly Brush Cells = Segments(Rgb(GroundC), 5, 1.5);

    /// <summary>A member's two lines (the bar's, then the quiet one), the pitch with the space under them, the line before you pinned below.</summary>
    public const double Pitch = 35, Height = 32, GapHeight = 15;
    const double BarLine = 18, RankW = 12, Gap0 = 6;

    /// <summary>A text's width in the screen's face (one line, no trimming).</summary>
    static double W(string s, double size, FontWeight w) => TextW(s, Face, size, w);

    /// <summary>
    /// The columns every bar line and the header share: the rank, the name (a 10-letter name whole, so every bar starts at one
    /// place), the bar (the rest), the DPS (its widest real form, "199.9K"). Worked out once per language.
    /// </summary>
    static (double Name, double Dps) Widths()
    {
        if (_widths is { } w0 && _widthsLang == L.Code) return w0;
        var w = (Math.Ceiling(4 + W("Mordwulfen", 11.5, FontWeights.Bold) + Gap0), Math.Ceiling(Gap0 + W("199.9K", 13, FontWeights.Bold)));
        _widths = w; _widthsLang = L.Code;
        return w;
    }
    static (double, double)? _widths;
    static string _widthsLang = "";

    static Grid Columns(double height)
    {
        var g = new Grid { Height = height };
        var (name, dps) = Widths();
        // a wider meter gives the name the room past the default width (up to the game's longest, 16 letters), the bar the rest
        // (the share picture widens the meter until nothing trims: "Thornecrest" stayed cut at any width)
        double longest = Math.Ceiling(4 + W("Thornecrestwarde", 11.5, FontWeights.Bold) + Gap0);
        name = Math.Min(longest, name + Math.Max(0, HudStackLook.FightWidth - HudStackLook.FightWidthDefault) * 0.5);
        foreach (var w in new[] { new GridLength(RankW), new GridLength(name), new GridLength(1, GridUnitType.Star), new GridLength(dps) })
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = w });
        return g;
    }

    static TextBlock At(Grid g, int col, TextBlock t) { Grid.SetColumn(t, col); g.Children.Add(t); return t; }

    /// <summary>The table's head, inverted as `top` draws it.</summary>
    public static FrameworkElement Header(bool expanded)
    {
        var g = Columns(15);
        var bar = new Border { Background = Br(PhosC, .85) };
        Grid.SetColumnSpan(bar, 4); g.Children.Add(bar);
        var rank = At(g, 0, Txt("#", 10, Ground, Face)); rank.HorizontalAlignment = HorizontalAlignment.Right;
        At(g, 1, Txt(L.T("NAME"), 10, Ground, Face)).Margin = new Thickness(4, 0, 0, 0);
        At(g, 2, Txt(L.T("LOAD"), 10, Ground, Face)).Margin = new Thickness(2, 0, 0, 0);
        var dps = At(g, 3, Txt(L.T("DPS"), 10, Ground, Face)); dps.HorizontalAlignment = HorizontalAlignment.Right; dps.Margin = new Thickness(0, 0, 2, 0);
        g.Margin = new Thickness(0, 6, 0, 4);
        return g;
    }

    public static FrameworkElement Gap(int more)
    {
        var t = Txt(more > 0 ? "  … " + L.F("{0} more", more) : "  …", 10, PhosDim, Face, FontWeights.SemiBold);
        return t;
    }

    public sealed class Row : IThemeRow
    {
        readonly Grid _root = new() { Height = TerminalRows.Height };
        readonly Grid _line = Columns(BarLine);
        readonly Border _band = new() { Visibility = Visibility.Collapsed, Margin = new Thickness(-3, -1, -3, -1) };
        readonly Border _bloom = new() { IsHitTestVisible = false, Margin = new Thickness(-3, -1, -3, -1) };
        readonly Border _rules = new() { BorderBrush = Br(PhosC, .55), BorderThickness = new Thickness(0, 1, 0, 1), Margin = new Thickness(-3, -1, -3, -1), Visibility = Visibility.Collapsed };
        readonly Border _rankBox = new() { HorizontalAlignment = HorizontalAlignment.Right, Padding = new Thickness(2, 0, 2, 0), VerticalAlignment = VerticalAlignment.Center };
        readonly TextBlock _rank = Txt("", 11.5, PhosDim, Face, FontWeights.SemiBold);
        readonly TextBlock _name = Txt("", 11.5, Phos, Face, FontWeights.SemiBold, trim: true);
        /// <summary>The load: [cells    20.0%] in the class's colour, as long as the member's DPS against the top member's.</summary>
        readonly Grid _load = new() { VerticalAlignment = VerticalAlignment.Center };
        readonly GlitchLevel _bar = new(13, Br(PhosC, .12), Cells);
        readonly TextBlock _share = Txt("", 10.5, Br(PhosC, .82), Face, FontWeights.SemiBold);
        readonly TextBlock _fig = FigCell(13, Pale, Face, 0);
        // the quiet line: the class, the CP, the marks, a healer's figure; an expanded meter's damage and crit at its end
        readonly TextBlock _cls = Txt("", 10.5, Phos, Face);
        readonly TextBlock _cp = Txt("", 10, PhosDim, Face, FontWeights.SemiBold);
        readonly Border _death = new() { Background = Br(0x3A0D0D), Padding = new Thickness(2, 0, 2, 0), Margin = new Thickness(Gap0, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
        readonly Border _tags = new() { Padding = new Thickness(2, 0, 2, 0), Margin = new Thickness(Gap0, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
        readonly TextBlock _heal = Txt("", 10, Mint2, Face, FontWeights.SemiBold);
        readonly TextBlock _extra = Txt("", 10.5, PhosDim, Face, FontWeights.SemiBold);

        public FrameworkElement Root => _root;

        public Row()
        {
            _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(BarLine) });
            _root.RowDefinitions.Add(new RowDefinition());
            Grid.SetRowSpan(_band, 2); _root.Children.Add(_band);
            Grid.SetRowSpan(_rules, 2); _root.Children.Add(_rules);
            _root.Children.Add(_line);
            // the bar line: the rank, the name, [the load  share], the DPS
            _rankBox.Child = _rank; _line.Children.Add(_rankBox);
            _name.Margin = new Thickness(4, 0, Gap0, 0); Grid.SetColumn(_name, 1); _line.Children.Add(_name);
            var load = _load;
            foreach (var w in new[] { GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto })
                load.ColumnDefinitions.Add(new ColumnDefinition { Width = w });
            load.Children.Add(Txt("[", 14, PhosDim, Face, FontWeights.SemiBold));
            _bar.Root.Margin = new Thickness(1, 0, 0, 0); _bar.Root.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(_bar.Root, 1); load.Children.Add(_bar.Root);
            _share.Margin = new Thickness(5, 0, 1, 0); Grid.SetColumn(_share, 2); load.Children.Add(_share);
            var close = Txt("]", 14, PhosDim, Face, FontWeights.SemiBold); Grid.SetColumn(close, 3); load.Children.Add(close);
            Grid.SetColumn(load, 2); _line.Children.Add(load);
            _fig.Margin = new Thickness(0, 0, 2, 0); Grid.SetColumn(_fig, 3); _line.Children.Add(_fig);
            // the quiet line under the name
            var quiet = new DockPanel { LastChildFill = false, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(RankW + 4, 0, 2, 0) };
            _extra.HorizontalAlignment = HorizontalAlignment.Right; DockPanel.SetDock(_extra, Dock.Right); quiet.Children.Add(_extra);
            _cls.Margin = new Thickness(0, 0, Gap0, 0);
            foreach (var el in new UIElement[] { _cls, _cp, _death, _tags, _heal }) { DockPanel.SetDock(el, Dock.Left); quiet.Children.Add(el); }
            _heal.Margin = new Thickness(Gap0, 0, 0, 0);
            Grid.SetRow(quiet, 1); _root.Children.Add(quiet);
            Grid.SetRowSpan(_bloom, 2); _root.Children.Add(_bloom);
        }

        public void Set(MeterMember m, MeterContext c, bool fresh)
        {
            bool you = m.You;
            var cc = m.ClassColor;
            _rank.Text = m.Rank.ToString(CultureInfo.InvariantCulture);
            _rankBox.Background = you ? Phos : null;
            _rank.Foreground = you ? Ground : PhosDim;
            _band.Visibility = you ? Visibility.Visible : Visibility.Collapsed;
            _band.Background = Br(PhosC, .2);
            _rules.Visibility = you ? Visibility.Visible : Visibility.Collapsed;
            _name.Inlines.Clear();
            _name.Inlines.Add(Rn(m.Name, you ? Br(0xE8FFEE) : Phos));
            if (you) _name.Inlines.Add(Rn("*", Amber));
            _name.FontWeight = you ? FontWeights.Bold : FontWeights.SemiBold;
            _bar.Fill.Background = Frozen(cc);
            _bar.Slide(c.Key + "|bar", BarOf(m, c));
            _share.Text = m.ShareText;
            // before a hit no load: the Combat Power short ("cp 167.5K") in the DPS's place, free to run into the empty load
            _load.Visibility = m.CpOnly ? Visibility.Hidden : Visibility.Visible;
            Grid.SetColumn(_fig, m.CpOnly ? 2 : 3); Grid.SetColumnSpan(_fig, m.CpOnly ? 2 : 1);
            _fig.Inlines.Clear();
            if (m.CpOnly && m.FigureValue > 0) { _fig.Inlines.Add(Rn(L.T("CP").ToLower(CultureInfo.CurrentCulture) + " ", PhosFaint, 10, FontWeights.SemiBold)); _fig.Inlines.Add(Rn(Amounts.Meter(m.FigureValue), PhosDim)); }
            else _fig.Inlines.Add(Rn(m.Figure, Pale));
            _fig.FontWeight = FontWeights.Bold;
            // the quiet line
            _cls.Text = ClassCode(m.Class);
            _cls.Foreground = Frozen(cc);
            _cp.Inlines.Clear();
            if (m.Cp.Length > 0) { _cp.Inlines.Add(Rn(L.T("CP").ToLower(CultureInfo.CurrentCulture) + " ", PhosFaint, 9.5)); _cp.Inlines.Add(Rn(m.Cp, you ? Br(PhosC, .9) : PhosDim)); }
            _cp.Visibility = m.Cp.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            _death.Child = m.Deaths > 0 ? Txt("†" + m.Deaths.ToString(CultureInfo.InvariantCulture), 11, Red, Face) : null;
            _death.Visibility = m.Deaths > 0 ? Visibility.Visible : Visibility.Collapsed;
            bool top = m.TopDps || m.TopHeals;
            _tags.Background = m.TopDps ? Amber : Mint2;
            _tags.Child = top ? Txt(L.T(m.TopDps ? "TOP" : "HEAL"), 10, Ground, Face) : null;
            _tags.Visibility = top ? Visibility.Visible : Visibility.Collapsed;
            _heal.Inlines.Clear();
            if (m.Heal.Length > 0)
            {
                // "+1.1K" whole, its "/s" dimmer and smaller
                int sl = m.Heal.LastIndexOf('/'); bool ps = sl > 0;   // "/s" in English, "/초" in Korean
                _heal.Inlines.Add(Rn("+" + (ps ? m.Heal[..sl] : m.Heal), Mint2));
                if (ps) _heal.Inlines.Add(Rn(m.Heal[sl..], Br(MintC2, .6), 8.5));
            }
            _heal.Visibility = m.Heal.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            _extra.Text = c.Expanded && m.Damage.Length > 0 ? m.Damage + (m.Crit.Length > 0 ? " " + m.Crit : "") : "";
            _extra.Visibility = _extra.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>A phosphor bloom on this member's lines (a kill's top DPS), decaying as phosphor does.</summary>
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
        EnergyMark.After(left, 12, PhosDim, Face);   // Odyle Energy (Ui/EnergyMark.cs)
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
