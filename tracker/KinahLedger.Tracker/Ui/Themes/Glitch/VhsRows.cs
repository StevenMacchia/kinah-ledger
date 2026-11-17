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
/// VHS's meter: the camcorder's on-screen display listing the party, two OSD lines a member in the camcorder's type. The
/// first: a boxed two-digit rank, a tick in their class's colour, the name in capitals with its Combat Power small after it,
/// the marks, and the DPS big at the end. The second is the bar, the row's hero (Steven, 2026-11-17: "figure out better looks
/// that doesn't crowd everything and prioritizes the bar"): the class's code, then a thick segmented level in their class's
/// colour across the row, as long as their DPS against the top member's (a green trace under it for a healer's share), an
/// expanded meter's damage and crit, and the share small at its end. You: the rank inverted, the red REC dot before your
/// name, a lit band behind the lines. Deaths a red ✕, TOP DPS stamped yellow, the top healer's figure stamped green at a
/// kill. The foot: TOTAL and GROUP DPS in the OSD's words, AVG stamped once the fight is held.
/// </summary>
static class VhsRows
{
    static readonly FontFamily Face = VhsFace;
    const uint RedC = 0xFF3B30, YellowC = 0xFFD23F, GreenC = 0x45FF86;
    static readonly Brush White = Br(0xFFFFFF), Dim = Br(0xFFFFFF, .62), Faint = Br(0xFFFFFF, .38), Black = Br(0x000000), Red = Br(RedC), Yellow = Br(YellowC), Green = Br(GreenC);
    static readonly Brush Cells = Segments(Rgb(0x000000, .82), 4, 1.25), CpInk = Br(0xFFFFFF, .7);

    /// <summary>A row's pitch, its height, the room for the line before you pinned below.</summary>
    public const double Pitch = 33, Height = 30, GapHeight = 16;
    const double NameLine = 17;

    /// <summary>"· · ·  2 MORE  · · ·": the party's members left out between it and you.</summary>
    public static FrameworkElement Gap(int more)
    {
        var t = Txt(more > 0 ? "· · ·  " + L.F("{0} more", more).ToUpper(CultureInfo.CurrentCulture) + "  · · ·" : "· · · · · ·", 9.5, Faint, Face);
        t.HorizontalAlignment = HorizontalAlignment.Center;
        return t;
    }

    /// <summary>The bar line's fixed parts, so every row's level starts and ends at one place: the code, an expanded row's damage and crit, the share.</summary>
    static double CodeW => Math.Ceiling(new[] { "gladiator", "templar", "assassin", "ranger", "sorcerer", "spiritmaster", "cleric", "chanter" }.Max(k => TextW(ClassCode(k), Face, 9.5, FontWeights.Bold)) + 6);
    static double ExtraW => Math.Ceiling(TextW("888.88M 88%", Face, 10, FontWeights.Bold) + 7);
    static double ShareW => Math.Ceiling(TextW("33.3%", Face, 10, FontWeights.Bold) + 11);   // clear of the crit's own "%"

    public sealed class Row : IThemeRow
    {
        readonly Grid _root = new() { Height = VhsRows.Height };
        readonly Border _band = new() { Margin = new Thickness(-4, -1, -4, -1), Visibility = Visibility.Collapsed };
        readonly Border _glint = new() { Background = Br(0xFFFFFF), Opacity = 0, IsHitTestVisible = false, Margin = new Thickness(-4, -1, -4, -1) };
        readonly Border _rankBox = new() { Width = 22, Height = 18, BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Center };
        readonly TextBlock _rank = Txt("", 10.5, White, Face);
        readonly Border _tick = new() { Width = 3, Margin = new Thickness(6, 2, 6, 2) };
        readonly Ellipse _dot = new() { Width = 6, Height = 6, Fill = Red, Margin = new Thickness(0, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
        readonly TextBlock _name = Txt("", 12, White, Face, trim: true);
        /// <summary>The Combat Power after the name, small and quiet ("CP 167.5K").</summary>
        readonly TextBlock _cp = Txt("", 9.5, CpInk, Face);
        readonly StackPanel _tags = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        readonly TextBlock _healText = Txt("", 9.5, Green, Face);
        /// <summary>A healer's "+12.3K/s" after the marks; the top healer's stamped green, the TOP HEAL tag with its figure in it.</summary>
        readonly Border _healBox = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(5, 0, 0, 0) };
        readonly GlitchLevel _bar = new(9, Br(0xFFFFFF, .1), Cells), _heal = new(2, Br(GreenC, .12));
        readonly TextBlock _code = Txt("", 9.5, White, Face);
        readonly Grid _barLine = new();
        readonly TextBlock _extra = Txt("", 10, Dim, Face), _fig = FigCell(13.5, White, Face, Math.Ceiling(TextW("199.9K", Face, 13.5, FontWeights.Bold))), _share = FigCell(10, Dim, Face, 0);

        public FrameworkElement Root => _root;

        public Row()
        {
            // the rank and the tick down both lines; the name's line; the bar's line under it, as wide as the row past the tick
            foreach (var w in new[] { GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto })
                _root.ColumnDefinitions.Add(new ColumnDefinition { Width = w });
            _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(NameLine) });
            _root.RowDefinitions.Add(new RowDefinition());
            Grid.SetColumnSpan(_band, 4); Grid.SetRowSpan(_band, 2); _root.Children.Add(_band);
            _rank.HorizontalAlignment = HorizontalAlignment.Center; _rankBox.Child = _rank;
            Grid.SetRowSpan(_rankBox, 2); _root.Children.Add(_rankBox);
            Grid.SetColumn(_tick, 1); Grid.SetRowSpan(_tick, 2); _root.Children.Add(_tick);
            // the name's line: the name gives way first (it trims), its CP, the marks and a healer's figure packed after it
            _cp.Margin = new Thickness(5, 1, 0, 0);
            _healBox.Child = _healText;
            // short of room (a 10-letter name, two marks and a heal, in Korean's wider words) the CP goes to the hover card before the name is cut
            var name = new GlitchNameLine { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Text = _name, Yields = _cp,
                NameMin = Math.Ceiling(TextW("MORDWULFEN", Face, 12, FontWeights.Bold)), Children = { _dot, _name, _cp, _tags, _healBox } };
            Grid.SetColumn(name, 2); _root.Children.Add(name);
            _fig.Margin = new Thickness(8, 0, 0, 0); Grid.SetColumn(_fig, 3); _root.Children.Add(_fig);
            // the bar's line: the code, the level (segmented cells, a healer's green trace under them), the damage and crit, the share
            var bl = _barLine;
            foreach (var w in new[] { new GridLength(CodeW), new GridLength(1, GridUnitType.Star), new GridLength(0), new GridLength(ShareW) })
                bl.ColumnDefinitions.Add(new ColumnDefinition { Width = w });
            _code.VerticalAlignment = VerticalAlignment.Center; bl.Children.Add(_code);
            var level = new Grid { VerticalAlignment = VerticalAlignment.Center, Height = 12 };
            _bar.Root.VerticalAlignment = VerticalAlignment.Top; level.Children.Add(_bar.Root);
            _heal.Fill.Background = Green; _heal.Root.VerticalAlignment = VerticalAlignment.Bottom; level.Children.Add(_heal.Root);
            Grid.SetColumn(level, 1); bl.Children.Add(level);
            _extra.HorizontalAlignment = HorizontalAlignment.Right; _extra.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(_extra, 2); bl.Children.Add(_extra);
            _share.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(_share, 3); bl.Children.Add(_share);
            Grid.SetRow(bl, 1); Grid.SetColumn(bl, 2); Grid.SetColumnSpan(bl, 2); _root.Children.Add(bl);
            Grid.SetColumnSpan(_glint, 4); Grid.SetRowSpan(_glint, 2); _root.Children.Add(_glint);
        }

        public void Set(MeterMember m, MeterContext c, bool fresh)
        {
            bool you = m.You;
            _rank.Text = m.Rank.ToString("00", CultureInfo.InvariantCulture);
            _rankBox.Background = you ? White : Br(0x000000, .35);
            _rankBox.BorderBrush = you ? White : Br(0xFFFFFF, .55);
            _rank.Foreground = you ? Black : White;
            _band.Visibility = you ? Visibility.Visible : Visibility.Collapsed;
            _band.Background = YouBand;
            _dot.Visibility = you ? Visibility.Visible : Visibility.Collapsed;
            var cc = m.ClassColor;
            _tick.Background = Frozen(cc);
            _code.Text = ClassCode(m.Class);
            _code.Foreground = Frozen(Color.FromRgb((byte)Math.Min(255, cc.R + 30), (byte)Math.Min(255, cc.G + 30), (byte)Math.Min(255, cc.B + 30)));
            _name.Text = Caps(m.Name);
            _name.Foreground = you ? Br(0xFFF4C4) : White;
            _cp.Inlines.Clear();
            if (m.Cp.Length > 0) { _cp.Inlines.Add(Rn(L.T("CP") + " ", Faint, 8)); _cp.Inlines.Add(Rn(m.Cp, CpInk)); }
            _cp.Visibility = m.Cp.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            _tags.Children.Clear();
            bool healTag = m.TopHeals && m.Heal.Length > 0;
            _healText.Text = m.Heal.Length > 0 ? "+" + m.Heal : "";
            _healText.Foreground = healTag ? Black : Green;
            _healBox.Background = healTag ? Green : null;
            _healBox.Padding = healTag ? new Thickness(2, 0, 2, 0) : new Thickness(0, 1, 0, 0);
            _healBox.Visibility = m.Heal.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            var tm = new Thickness(4, 0, 0, 0);
            if (m.Deaths > 0) _tags.Children.Add(Tag("✕" + (m.Deaths > 1 ? m.Deaths.ToString(CultureInfo.InvariantCulture) : ""), Red, White, Face, 9, tm));
            if (m.TopDps) _tags.Children.Add(Tag(L.T("TOP DPS"), Yellow, Black, Face, 8.5, tm));
            if (m.TopHeals && !healTag) _tags.Children.Add(Tag(L.T("TOP HEAL"), Green, Black, Face, 8.5, tm));
            _bar.Fill.Background = Frozen(Color.FromRgb((byte)Math.Min(255, cc.R + 25), (byte)Math.Min(255, cc.G + 25), (byte)Math.Min(255, cc.B + 25)));
            _bar.Root.Visibility = m.CpOnly ? Visibility.Hidden : Visibility.Visible;
            _bar.Slide(c.Key + "|bar", BarOf(m, c));
            _heal.Root.Visibility = m.HealShare > 0 ? Visibility.Visible : Visibility.Hidden;
            _heal.Slide(c.Key + "|heal", m.HealShare);
            _extra.Text = c.Expanded && m.Damage.Length > 0 ? m.Damage + (m.Crit.Length > 0 ? " " + m.Crit : "") : "";
            _extra.Visibility = _extra.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            _barLine.ColumnDefinitions[2].Width = new GridLength(c.Expanded && c.E.HasDamage ? ExtraW : 0);
            _fig.Text = m.Figure;
            _fig.Foreground = m.CpOnly ? Dim : White;
            _share.Text = m.ShareText;
        }

        /// <summary>A kill's glint on this member's lines: three white flashes, the last fading.</summary>
        public void Glint(double ms) => Motion.Play(_glint, ms, t =>
        {
            int beat = (int)(t * 8);
            _glint.Opacity = t < .45 ? (beat % 2 == 0 ? .55 : 0) : t < .6 ? .55 * (1 - (t - .45) / .15) : 0;
        }, () => _glint.Opacity = 0);
    }

    static readonly Brush YouBand = MakeBand();
    static Brush MakeBand()
    {
        var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        b.GradientStops.Add(new GradientStop(Rgb(0xFFFFFF, .16), 0));
        b.GradientStops.Add(new GradientStop(Rgb(0xFFFFFF, .06), 1));
        b.Freeze();
        return b;
    }

    /// <summary>The foot: a dotted rule, "TOTAL 2.03M" and "GROUP DPS 12.7K", AVG stamped once the fight is held.</summary>
    public static FrameworkElement Foot(Encounter e, DateTime now)
    {
        var ff = FootOf(e, now);
        var sp = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
        sp.Children.Add(new Rectangle { Height = 1, Fill = Dots, Margin = new Thickness(0, 0, 0, 5) });
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = Txt("", 13, White, Face);
        left.Inlines.Add(Rn(L.T("Total ").Trim().ToUpper(CultureInfo.CurrentCulture) + " ", Dim, 10));
        left.Inlines.Add(Rn(ff.Total, White));
        g.Children.Add(left);
        var right = new StackPanel { Orientation = Orientation.Horizontal };
        var r = Txt("", 13, White, Face);
        if (ff.Label.Length > 0)
        {
            r.Inlines.Add(Rn(ff.Label.ToUpper(CultureInfo.CurrentCulture) + " ", Dim, 10));
            r.Inlines.Add(Rn(ff.Dps, White));
        }
        right.Children.Add(r);
        if (ff.Held) right.Children.Add(Tag(L.T("AVG"), Yellow, Black, Face, 9, new Thickness(6, 0, 0, 0)));
        Grid.SetColumn(right, 1); g.Children.Add(right);
        sp.Children.Add(g);
        TagFoot(sp, e, now);
        return sp;
    }

    static readonly Brush Dots = MakeDots();
    static Brush MakeDots()
    {
        var d = new DrawingGroup();
        d.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, 4, 1))));
        d.Children.Add(new GeometryDrawing(Br(0xFFFFFF, .4), null, new RectangleGeometry(new Rect(0, 0, 2, 1))));
        var b = new DrawingBrush(d) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 4, 1), ViewportUnits = BrushMappingMode.Absolute, Stretch = Stretch.Fill };
        b.Freeze();
        return b;
    }
}
