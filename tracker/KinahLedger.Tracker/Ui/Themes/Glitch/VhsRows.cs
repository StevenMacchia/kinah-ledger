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
/// VHS's meter: the camcorder's on-screen display listing the party. Each member is one OSD line in the camcorder's type:
/// a boxed two-digit rank, a tick in their class's colour, the name in capitals, a segmented level bar in their class's
/// colour (a green trace under it for a healer's share), the DPS and the share right-aligned. You: the rank inverted, the
/// red REC dot before your name, a lit band behind the line. Deaths a red ✕, TOP DPS / TOP HEAL stamped as yellow / green
/// tags at a kill. The foot: TOTAL and GROUP DPS in the OSD's words, AVG stamped once the fight is held.
/// </summary>
static class VhsRows
{
    static readonly FontFamily Face = VhsFace;
    const uint RedC = 0xFF3B30, YellowC = 0xFFD23F, GreenC = 0x45FF86;
    static readonly Brush White = Br(0xFFFFFF), Dim = Br(0xFFFFFF, .62), Faint = Br(0xFFFFFF, .38), Black = Br(0x000000), Red = Br(RedC), Yellow = Br(YellowC), Green = Br(GreenC);
    static readonly Brush Cells = Segments(Rgb(0x000000, .82), 4, 1.25), CpInk = Br(0xFFFFFF, .74);

    /// <summary>A row's pitch, its height, the room for the line before you pinned below.</summary>
    public const double Pitch = 27, Height = 24, GapHeight = 16;

    /// <summary>"· · ·  2 MORE  · · ·": the party's members left out between it and you.</summary>
    public static FrameworkElement Gap(int more)
    {
        var t = Txt(more > 0 ? "· · ·  " + L.F("{0} more", more).ToUpper(CultureInfo.CurrentCulture) + "  · · ·" : "· · · · · ·", 9.5, Faint, Face);
        t.HorizontalAlignment = HorizontalAlignment.Center;
        return t;
    }

    public sealed class Row : IThemeRow
    {
        readonly Grid _root = new() { Height = VhsRows.Height };
        readonly Border _band = new() { Margin = new Thickness(-4, -1, -4, -1), Visibility = Visibility.Collapsed };
        readonly Border _glint = new() { Background = Br(0xFFFFFF), Opacity = 0, IsHitTestVisible = false, Margin = new Thickness(-4, -1, -4, -1) };
        readonly Border _rankBox = new() { Width = 22, Height = 18, BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Center };
        readonly TextBlock _rank = Txt("", 10.5, White, Face);
        readonly Border _tick = new() { Width = 3, Height = 13, Margin = new Thickness(6, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
        readonly Ellipse _dot = new() { Width = 6, Height = 6, Fill = Red, Margin = new Thickness(0, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
        readonly TextBlock _name = Txt("", 12, White, Face, trim: true);
        readonly StackPanel _tags = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        readonly TextBlock _healText = Txt("", 9.5, Green, Face);
        readonly GlitchLevel _bar = new(5, Br(0xFFFFFF, .11), Cells), _heal = new(2, Br(GreenC, .12));
        readonly TextBlock _code = Txt("", 9.5, White, Face);
        readonly TextBlock _extra = Txt("", 10, Dim, Face), _fig = FigCell(13.5, White, Face, 40), _share = FigCell(10, Dim, Face, 42);

        public FrameworkElement Root => _root;
        /// <summary>Where the level starts on the second line: past the class's code and the CP ("GLA  CP 38.2K").</summary>
        const double LevelAt = 82;

        public Row()
        {
            // two lines: the rank, the class's tick, the name and the figures; under the name the level, as long as the line
            foreach (var w in new[] { GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto, GridLength.Auto, GridLength.Auto })
                _root.ColumnDefinitions.Add(new ColumnDefinition { Width = w });
            _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(15) });
            _root.RowDefinitions.Add(new RowDefinition());
            Grid.SetColumnSpan(_band, 7); Grid.SetRowSpan(_band, 2); _root.Children.Add(_band);
            _rank.HorizontalAlignment = HorizontalAlignment.Center; _rankBox.Child = _rank;
            Grid.SetRowSpan(_rankBox, 2); _root.Children.Add(_rankBox);
            _tick.Height = 18; Grid.SetColumn(_tick, 1); Grid.SetRowSpan(_tick, 2); _root.Children.Add(_tick);
            DockPanel.SetDock(_dot, Dock.Left); DockPanel.SetDock(_tags, Dock.Right);
            var name = new DockPanel { LastChildFill = true, VerticalAlignment = VerticalAlignment.Center, Children = { _dot, _tags, _name } };
            Grid.SetColumn(name, 2); _root.Children.Add(name);
            _healText.Margin = new Thickness(8, 1, 0, 0); _healText.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetColumn(_healText, 3); _root.Children.Add(_healText);
            _extra.Margin = new Thickness(8, 1, 0, 0); Grid.SetColumn(_extra, 4); _root.Children.Add(_extra);
            _fig.Margin = new Thickness(8, 0, 0, 0); Grid.SetColumn(_fig, 5); Grid.SetRowSpan(_fig, 2); _root.Children.Add(_fig);
            _share.Margin = new Thickness(5, 1, 0, 0); Grid.SetColumn(_share, 6); Grid.SetRowSpan(_share, 2); _root.Children.Add(_share);
            // the level: segmented cells in the class's colour, a healer's green trace under them
            _code.VerticalAlignment = VerticalAlignment.Top; _code.Margin = new Thickness(0, -1, 0, 0);
            Grid.SetRow(_code, 1); Grid.SetColumn(_code, 2); _root.Children.Add(_code);
            // under the name: the class's code and the Combat Power ("GLA  CP 4.8K"), then the level, every row's starting at one place
            var level = new Grid { Margin = new Thickness(LevelAt, 1, 0, 0), VerticalAlignment = VerticalAlignment.Top, Height = 8 };
            _bar.Root.VerticalAlignment = VerticalAlignment.Top; level.Children.Add(_bar.Root);
            _heal.Fill.Background = Green; _heal.Root.VerticalAlignment = VerticalAlignment.Bottom; level.Children.Add(_heal.Root);
            Grid.SetRow(level, 1); Grid.SetColumn(level, 2); Grid.SetColumnSpan(level, 3); _root.Children.Add(level);
            Grid.SetColumnSpan(_glint, 7); Grid.SetRowSpan(_glint, 2); _root.Children.Add(_glint);
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
            var codeFg = Frozen(Color.FromRgb((byte)Math.Min(255, cc.R + 30), (byte)Math.Min(255, cc.G + 30), (byte)Math.Min(255, cc.B + 30)));
            _code.Inlines.Clear();
            _code.Inlines.Add(Rn(ClassCode(m.Class), codeFg));
            if (m.Cp.Length > 0) { _code.Inlines.Add(Rn("  " + L.T("CP") + " ", Faint, 8.5)); _code.Inlines.Add(Rn(m.Cp, CpInk)); }
            _name.Text = Caps(m.Name);
            _name.Foreground = you ? Br(0xFFF4C4) : White;
            _tags.Children.Clear();
            _healText.Text = m.Heal.Length > 0 ? "+" + m.Heal : "";
            _healText.Visibility = m.Heal.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (m.Deaths > 0) _tags.Children.Add(Tag("✕" + (m.Deaths > 1 ? m.Deaths.ToString(CultureInfo.InvariantCulture) : ""), Red, White, Face, 9));
            if (m.TopDps) _tags.Children.Add(Tag(L.T("TOP DPS"), Yellow, Black, Face, 8.5));
            if (m.TopHeals) _tags.Children.Add(Tag(L.T("TOP HEAL"), Green, Black, Face, 8.5));
            _bar.Fill.Background = Frozen(Color.FromRgb((byte)Math.Min(255, cc.R + 25), (byte)Math.Min(255, cc.G + 25), (byte)Math.Min(255, cc.B + 25)));
            _bar.Root.Visibility = m.CpOnly ? Visibility.Hidden : Visibility.Visible;
            _bar.Slide(c.Key + "|bar", m.Share);
            _heal.Root.Visibility = m.HealShare > 0 ? Visibility.Visible : Visibility.Hidden;
            _heal.Slide(c.Key + "|heal", m.HealShare);
            _extra.Text = c.Expanded && m.Damage.Length > 0 ? m.Damage + (m.Crit.Length > 0 ? " " + m.Crit : "") : "";
            _extra.Visibility = _extra.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            _fig.Text = m.Figure;
            _fig.Foreground = m.CpOnly ? Dim : White;
            _share.Text = m.ShareText;
        }

        /// <summary>A kill's glint on this line: three white flashes, the last fading.</summary>
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
        r.Inlines.Add(Rn(ff.Label.ToUpper(CultureInfo.CurrentCulture) + " ", Dim, 10));
        r.Inlines.Add(Rn(ff.Dps, White));
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
