using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using KinahLedger.Parser;
using static KinahLedger.Ui.HudKit;
using static KinahLedger.Ui.GlitchKit;

namespace KinahLedger.Ui;

public static partial class MeterConcepts
{
    // VHS (Steven, 2026-11-17: "VHS concept is cool as well but not legible enough"): three tapes, each with the bar as the row's
    // hero, one big DPS figure, crisp Inter figures over the tape's effects (which stay on the frame and the ground)
    static partial void AddVhs(List<Concept> l)
    {
        l.Add(new(HudThemes.Vhs, "A", "Viewfinder: a camcorder's black glass, the name and DPS over a full-width glowing bar", (e, now, w) => VhsConcepts.Make(VhsConcepts.Kind.Viewfinder, e, now, w)));
        l.Add(new(HudThemes.Vhs, "B", "Deck: a VCR's front, its display on top; each row is the bar, the DPS in a counter window", (e, now, w) => VhsConcepts.Make(VhsConcepts.Kind.Deck, e, now, w)));
        l.Add(new(HudThemes.Vhs, "C", "Blue screen: the VCR's menu, rank tiles and white type on the tape's blue", (e, now, w) => VhsConcepts.Make(VhsConcepts.Kind.BlueScreen, e, now, w)));
    }
}

/// <summary>
/// The VHS concepts (render only, Ui/MeterConcepts.cs). What they share: Inter (the app's own face) for every name and figure, the
/// tape's Lucida Console only for its small OSD words (REC, STOP, SP, CH); the bar thick and class-coloured across the row; the DPS
/// the one big figure; the share small at the bar's end; CP small and quiet after the name (the first to go to the hover card);
/// the heal only on healers; TOP DPS and TOP HEAL small tags; crit and damage only when the meter is expanded. Scanlines, noise,
/// vignette and the red/cyan split sit on the ground and the frame, never on a figure.
/// </summary>
static class VhsConcepts
{
    public enum Kind { Viewfinder, Deck, BlueScreen }

    static readonly FontFamily Sans = HudKit.Text, Osd = VhsFace;
    const uint RedC = 0xFF3B30, CyanC = 0x28E0FF, YellowC = 0xFFD23F, GreenC = 0x45FF86, MintC = 0x5CFFB0, VfdC = 0x7CF3FF;
    static readonly Brush White = Br(0xFFFFFF), Dim = Br(0xFFFFFF, .66), Faint = Br(0xFFFFFF, .42), Black = Br(0x000000),
        Red = Br(RedC), Yellow = Br(YellowC), Green = Br(GreenC), Mint = Br(MintC);

    static Color Lift(Color c, int by) => Color.FromRgb((byte)Math.Min(255, c.R + by), (byte)Math.Min(255, c.G + by), (byte)Math.Min(255, c.B + by));
    static Color Mix(Color c, Color to, double t) => Color.FromRgb((byte)(c.R + (to.R - c.R) * t), (byte)(c.G + (to.G - c.G) * t), (byte)(c.B + (to.B - c.B) * t));
    static Brush Vertical(params (Color C, double At)[] stops)
    {
        var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        foreach (var (c, at) in stops) b.GradientStops.Add(new GradientStop(c, at));
        b.Freeze();
        return b;
    }
    static Brush Horizontal(params (Color C, double At)[] stops)
    {
        var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        foreach (var (c, at) in stops) b.GradientStops.Add(new GradientStop(c, at));
        b.Freeze();
        return b;
    }

    /// <summary>The fight's face in a concept, <paramref name="w"/> wide: the frame, the head, the HP, the rows, the foot.</summary>
    public static FrameworkElement Make(Kind k, Encounter e, DateTime now, double w)
    {
        var s = Tape.Of(k);
        var root = new Grid { Width = w, HorizontalAlignment = HorizontalAlignment.Left, ClipToBounds = false };
        TextOptions.SetTextFormattingMode(root, TextFormattingMode.Display);
        var ground = new Grid { IsHitTestVisible = false, Margin = new Thickness(1) };
        var body = new StackPanel { Margin = s.Pad };
        var over = new Grid { IsHitTestVisible = false };
        root.Children.Add(ground); root.Children.Add(body); root.Children.Add(over);
        s.Frame(ground, over);
        double inner = w - s.Pad.Left - s.Pad.Right;

        body.Children.Add(s.Head(e, now));
        if (!e.Waiting && e.Live && e.BossHpLeft is double hp) body.Children.Add(s.Hp(e, hp));
        if (e.Waiting && e.ReadyText.Length > 0)
            body.Children.Add(new TextBlock { Text = e.ReadyText, FontFamily = Sans, FontSize = 11.5, FontWeight = FontWeights.SemiBold, Foreground = Dim, Margin = new Thickness(0, 8, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis });
        if (!e.Waiting || HudStackLook.PartyWaiting(e))
        {
            var rows = new ThemeRows(s.Pitch, _ => s.Row(), s.GapH, s.Gap) { Width = inner, Moves = false, Card = c => GlitchCards.Vhs(c, false), Margin = new Thickness(0, s.RowsTop, 0, 0) };
            body.Children.Add(rows);
            rows.Show(e, now);
        }
        if (!e.Waiting && e.HasDamage) body.Children.Add(s.Foot(e, now));
        return root;
    }

    /// <summary>A concept's parts.</summary>
    sealed class Tape
    {
        public required Thickness Pad;
        public required double Pitch, GapH, RowsTop;
        public required Func<IThemeRow> Row;
        public required Func<int, FrameworkElement> Gap;
        public required Action<Grid, Grid> Frame;
        public required Func<Encounter, DateTime, FrameworkElement> Head, Foot;
        public required Func<Encounter, double, FrameworkElement> Hp;

        public static Tape Of(Kind k) => k switch
        {
            Kind.Viewfinder => new Tape
            {
                Pad = new Thickness(15, 11, 15, 11), Pitch = 41, GapH = 18, RowsTop = 12, Row = () => new ViewRow(), Gap = n => GapLine(n, Faint),
                Frame = ViewFrame, Head = (e, now) => ViewHead(e, now), Foot = (e, now) => FootLine(e, now, Faint, White, Yellow, Black, Br(0xFFFFFF, .22)), Hp = (e, hp) => HpLine(e, hp, Br(0xFFFFFF, .14), Red, White, White, Dim, 7),
            },
            Kind.Deck => new Tape
            {
                Pad = new Thickness(12, 10, 12, 10), Pitch = 30, GapH = 16, RowsTop = 10, Row = () => new DeckRow(), Gap = n => GapLine(n, Faint),
                Frame = DeckFrame, Head = (e, now) => DeckHead(e, now), Foot = (e, now) => FootLine(e, now, Br(VfdC, .7), White, Br(VfdC), Black, Br(0xFFFFFF, .14)), Hp = (e, hp) => HpLine(e, hp, Br(0xFFFFFF, .1), Br(0xFF5A3C), Br(VfdC), Br(0xDFFBFF), Br(VfdC, .7), 8, segments: true),
            },
            _ => new Tape
            {
                Pad = new Thickness(15, 12, 15, 12), Pitch = 39, GapH = 17, RowsTop = 12, Row = () => new BlueRow(), Gap = n => GapLine(n, Br(0xFFFFFF, .6)),
                Frame = BlueFrame, Head = (e, now) => BlueHead(e, now), Foot = (e, now) => FootLine(e, now, Br(0xFFFFFF, .72), White, White, Br(0x1530B8), Br(0xFFFFFF, .5)), Hp = (e, hp) => HpLine(e, hp, Br(0x040C3A, .85), Br(RedC), White, White, Br(0xFFFFFF, .75), 9, outline: true),
            },
        };
    }

    // ---- shared parts

    static string Outcome(Encounter e) => e.Live || e.Outcome.Length == 0 ? ""
        : L.T(e.Outcome == "kill" ? "Killed" : e.Outcome == "wipe" ? "Wiped" : e.Outcome == "cleared" ? "Cleared" : "Over");
    static Brush OutcomeInk(Encounter e) => e.Outcome == "wipe" ? Br(0xFF6A5C) : e.Outcome is "kill" or "cleared" ? Mint : Dim;
    static string ClockOf(Encounter e, DateTime now) => e.Waiting ? "‖ " + L.T("READY") : BossParser.Clock(e.Duration(now));
    static string Up(string s) => s.ToUpper(CultureInfo.CurrentCulture);
    static string Channel(Encounter e) => $"CH {1 + (int)(Hash(e.Title) * 12):00}";

    static TextBlock T(string s, double size, Brush fg, FontFamily face, FontWeight? w = null, bool trim = false) => Txt(s, size, fg, face, w ?? FontWeights.Bold, trim);

    static FrameworkElement GapLine(int more, Brush ink)
    {
        var t = T(more > 0 ? "· · ·   " + Up(L.F("{0} more", more)) + "   · · ·" : "· · · · · ·", 9.5, ink, Osd);
        t.HorizontalAlignment = HorizontalAlignment.Center;
        return t;
    }

    /// <summary>The boss's HP: a thick level with ticks at the thirds standing out of it, "47%" and "12.84M LEFT" at its end.</summary>
    static FrameworkElement HpLine(Encounter e, double hp, Brush track, Brush fill, Brush ticks, Brush pct, Brush left, double h, bool outline = false, bool segments = false)
    {
        var g = new Grid { Margin = new Thickness(0, 9, 0, 0) };
        g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var bar = new Bar(h);
        bar.Track.Background = track;
        if (outline) { bar.Track.BorderBrush = Br(0xFFFFFF, .8); bar.Track.BorderThickness = new Thickness(1); bar.Fill.Margin = new Thickness(1); }
        bar.Fill.Children.Add(new Border { Background = hp < .34 ? Br(0xFF2020) : fill });
        bar.Fill.Children.Add(new Border { Height = 1, VerticalAlignment = VerticalAlignment.Top, Background = Br(0xFFFFFF, .35) });
        bar.Share = hp;
        if (segments) bar.Root.Children.Add(new Rectangle { Fill = Segments(Rgb(0x000000, .85), 4, 1.2), IsHitTestVisible = false });
        var cell = new Grid { Height = h + 6, VerticalAlignment = VerticalAlignment.Center };
        cell.Children.Add(bar.Root);
        bar.Root.VerticalAlignment = VerticalAlignment.Center;
        for (int i = 1; i <= 2; i++)
        {
            var tick = new Border { Width = 1.6, Background = ticks, HorizontalAlignment = HorizontalAlignment.Left, IsHitTestVisible = false };
            int third = i;
            cell.SizeChanged += (_, _) => tick.Margin = new Thickness(cell.ActualWidth * third / 3 - 0.8, 0, 0, 0);
            cell.Children.Add(tick);
        }
        g.Children.Add(cell);
        var t = T("", 12, pct, Sans);
        t.Inlines.Add(Rn($"{Math.Round(hp * 100):0}%", hp < .34 ? Br(0xFF8A7C) : pct, 12.5));
        var l = ParserPanel.HpLeft(e);
        if (l.Length > 0) t.Inlines.Add(Rn("  " + Up(l), left, 10, FontWeights.SemiBold));
        t.Margin = new Thickness(10, 0, 0, 0);
        Grid.SetColumn(t, 1); g.Children.Add(t);
        return g;
    }

    /// <summary>The foot: a rule, "TOTAL 766.26M" and "GROUP 999K" (alone: your best or peak), AVG stamped once the fight is held.</summary>
    static FrameworkElement FootLine(Encounter e, DateTime now, Brush label, Brush value, Brush tag, Brush tagInk, Brush rule)
    {
        var ff = FootOf(e, now);
        var sp = new StackPanel { Margin = new Thickness(0, 9, 0, 0) };
        sp.Children.Add(new Rectangle { Height = 1, Fill = rule, Margin = new Thickness(0, 0, 0, 7) });
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = T("", 14, value, Sans);
        left.Inlines.Add(Rn(Up(L.T("Total ").Trim()) + "  ", label, 9.5, FontWeights.SemiBold));
        left.Inlines.Add(Rn(ff.Total, value));
        g.Children.Add(left);
        var right = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 0, 0, 0) };
        if (ff.Label.Length > 0)
        {
            var r = T("", 14, value, Sans);
            r.Inlines.Add(Rn(Up(ff.Label) + "  ", label, 9.5, FontWeights.SemiBold));
            r.Inlines.Add(Rn(ff.Dps, value));
            right.Children.Add(r);
        }
        if (ff.Held) right.Children.Add(Mark(L.T("AVG"), tag, tagInk, 8.5, new Thickness(6, 0, 0, 0)));
        Grid.SetColumn(right, 1); g.Children.Add(right);
        sp.Children.Add(g);
        TagFoot(sp, e, now);
        return sp;
    }

    /// <summary>A small mark: words on a block of colour, in Inter ("TOP DPS", "✕2", "AVG").</summary>
    static Border Mark(string text, Brush bg, Brush fg, double size, Thickness margin) => new()
    {
        Background = bg, Padding = new Thickness(4, 0, 4, 0), Margin = margin, VerticalAlignment = VerticalAlignment.Center, CornerRadius = new CornerRadius(1.5),
        Child = new TextBlock { Text = text, FontFamily = Sans, FontSize = size, FontWeight = FontWeights.Bold, Foreground = fg, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 0.5) },
    };

    /// <summary>A row's bar: a track, the fill a share of its width (kept on the track's size), its tip a bright line.</summary>
    sealed class Bar
    {
        public readonly Grid Root = new() { IsHitTestVisible = false };
        public readonly Border Track = new();
        public readonly Grid Fill = new() { HorizontalAlignment = HorizontalAlignment.Left };
        double _share;
        public Bar(double h)
        {
            Root.Height = h;
            Root.Children.Add(Track); Root.Children.Add(Fill);
            Root.SizeChanged += (_, _) => Apply();
        }
        public double Share { get => _share; set { _share = Math.Clamp(double.IsFinite(value) ? value : 0, 0, 1); Apply(); } }
        void Apply() => Fill.Width = Math.Max(0, Math.Round((Root.ActualWidth - Fill.Margin.Left - Fill.Margin.Right) * _share));
    }

    /// <summary>A member's marks after the name: deaths, TOP DPS, TOP HEAL (a top healer's figure in the green tag).</summary>
    static void Marks(StackPanel tags, MeterMember m, bool healTag)
    {
        tags.Children.Clear();
        var tm = new Thickness(5, 0, 0, 0);
        if (m.Deaths > 0) tags.Children.Add(Mark("✕" + (m.Deaths > 1 ? m.Deaths.ToString(CultureInfo.InvariantCulture) : ""), Br(0xE5322A), White, 9, tm));
        if (m.TopDps) tags.Children.Add(Mark(L.T("TOP DPS"), Yellow, Black, 8.5, tm));
        if (m.TopHeals && !healTag) tags.Children.Add(Mark(L.T("TOP HEAL"), Green, Black, 8.5, tm));
    }

    static void SetHeal(Border box, TextBlock text, MeterMember m, Brush ink)
    {
        bool tag = m.TopHeals && m.Heal.Length > 0;
        text.Text = m.Heal.Length > 0 ? "+" + m.Heal : "";
        text.Foreground = tag ? Black : ink;
        box.Background = tag ? Green : null;
        box.Padding = tag ? new Thickness(4, 0, 4, 0) : new Thickness(0);
        box.CornerRadius = new CornerRadius(1.5);
        box.Visibility = m.Heal.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    static void SetCp(TextBlock cp, MeterMember m, Brush word, Brush ink)
    {
        cp.Inlines.Clear();
        if (m.Cp.Length > 0) { cp.Inlines.Add(Rn(L.T("CP") + " ", word, 8.5)); cp.Inlines.Add(Rn(m.Cp, ink)); }
        cp.Visibility = m.Cp.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>The small figure at a bar's end: the share; expanded, the damage and crit before it.</summary>
    static string Small(MeterMember m, MeterContext c) =>
        c.Expanded && m.Damage.Length > 0 ? m.Damage + (m.Crit.Length > 0 ? "  " + m.Crit : "") + "  " + m.ShareText : m.ShareText;
    static double SmallW(double size, bool expanded) => Math.Ceiling(TextW(expanded ? "888.88M  88%  100.0%" : "100.0%", Sans, size, FontWeights.SemiBold) + 2);

    static GlitchNameLine NameLine(TextBlock name, TextBlock cp, double nameSize, params UIElement[] rest)
    {
        var line = new GlitchNameLine { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Text = name, Yields = cp,
            NameMin = 10_000 };   // the whole name before the CP: short of room, the CP goes to the hover card first
        line.Children.Add(name);
        line.Children.Add(cp);
        foreach (var r in rest) line.Children.Add(r);
        return line;
    }

    // ==== A: the viewfinder

    static void ViewFrame(Grid ground, Grid over)
    {
        ground.Children.Add(new Border { Background = Vertical((Rgb(0x0B0D10, .9), 0), (Rgb(0x000000, .86), 1)), CornerRadius = new CornerRadius(3) });
        ground.Children.Add(new Rectangle { Fill = Vignette(Colors.Black, .5), RadiusX = 3, RadiusY = 3 });
        ground.Children.Add(new Rectangle { Fill = Vertical((Rgb(0xFFFFFF, .07), 0), (Rgb(0xFFFFFF, 0), .3)), RadiusX = 3, RadiusY = 3 });
        ground.Children.Add(new Rectangle { Fill = Lines(Rgb(0xFFFFFF, .035), 3, 1) });
        // two faint tracking lines rolling through the picture (under every word), the head-switching noise at its foot
        ground.Children.Add(new Rectangle { Fill = NoiseBrush(.16), Height = 2, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 64, 0, 0) });
        ground.Children.Add(new Rectangle { Fill = NoiseBrush(.12), Height = 1, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 92) });
        ground.Children.Add(new Rectangle { Fill = NoiseBrush(.4), Height = 3, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 1) });
        // the corner marks, split red and cyan a pixel either side behind the white
        var red = Corners(Br(RedC, .75), 10, 1.6, 5); red.RenderTransform = new TranslateTransform(-1.2, 0);
        var cyan = Corners(Br(CyanC, .75), 10, 1.6, 5); cyan.RenderTransform = new TranslateTransform(1.2, 0);
        over.Children.Add(red); over.Children.Add(cyan); over.Children.Add(Corners(Br(0xFFFFFF, .92), 10, 1.6, 5));
    }

    static FrameworkElement ViewHead(Encounter e, DateTime now)
    {
        var sp = new StackPanel();
        // the OSD's top line: ● REC (or ■ STOP and how it ended) and the tape's speed and channel
        var osd = new Grid();
        osd.ColumnDefinitions.Add(new ColumnDefinition()); osd.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var st = new StackPanel { Orientation = Orientation.Horizontal };
        if (e.Live && !e.Waiting)
        {
            st.Children.Add(new Ellipse { Width = 8, Height = 8, Fill = Red, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center, Effect = new DropShadowEffect { Color = Rgb(RedC), BlurRadius = 7, ShadowDepth = 0, Opacity = .8 } });
            st.Children.Add(T(L.T("REC"), 10.5, Red, Osd));
        }
        else
        {
            st.Children.Add(T(e.Waiting ? "‖ " + L.T("STBY") : "■ " + L.T("STOP"), 10.5, White, Osd));
            if (Outcome(e) is { Length: > 0 } o) st.Children.Add(T("   " + Up(o), 10.5, OutcomeInk(e), Osd));
        }
        osd.Children.Add(st);
        var ch = T("SP  " + Channel(e), 10, Dim, Osd);
        Grid.SetColumn(ch, 1); osd.Children.Add(ch);
        sp.Children.Add(osd);
        // the boss and the clock, big
        var title = new Grid { Margin = new Thickness(0, 5, 0, 0) };
        title.ColumnDefinitions.Add(new ColumnDefinition()); title.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var boss = T(e.Title, 15.5, White, Sans, trim: true);
        title.Children.Add(boss);
        var clock = T(ClockOf(e, now), e.Waiting ? 13 : 19, e.Live && !e.Waiting ? Yellow : White, Sans);
        clock.Margin = new Thickness(12, 0, 0, 0);
        Grid.SetColumn(clock, 1); title.Children.Add(clock);
        sp.Children.Add(title);
        return sp;
    }

    /// <summary>
    /// A: the name line (rank, name, CP, marks, a healer's figure) with the DPS big at its end; under it the bar across the whole
    /// row, thick and lit in the class's colour, the share small at its end.
    /// </summary>
    sealed class ViewRow : IThemeRow
    {
        const double Line1 = 19, BarH = 12;
        readonly Grid _root = new() { Height = Line1 + 2 + BarH };
        readonly Border _band = new() { Margin = new Thickness(-7, -4, -7, -4), Visibility = Visibility.Collapsed, CornerRadius = new CornerRadius(2),
            Background = Horizontal((Rgb(0xFFFFFF, .13), 0), (Rgb(0xFFFFFF, .04), 1)) };
        readonly Border _rankBox = new() { MinWidth = 21, Height = 15, Padding = new Thickness(2, 0, 2, 0), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 7, 0), CornerRadius = new CornerRadius(1.5) };
        readonly TextBlock _rank = T("", 10, White, Osd);
        readonly Ellipse _dot = new() { Width = 7, Height = 7, Fill = Red, Margin = new Thickness(0, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center };
        readonly TextBlock _name = T("", 12.5, White, Sans, trim: true), _cp = T("", 9.5, Br(0xFFFFFF, .6), Sans, FontWeights.SemiBold);
        readonly StackPanel _tags = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        readonly Border _healBox = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
        readonly TextBlock _heal = T("", 10.5, Green, Sans);
        readonly TextBlock _fig = FigCell(18, White, Sans, 0, FontWeights.Bold), _small = T("", 9.5, Br(0xFFFFFF, .72), Sans, FontWeights.SemiBold);
        readonly Bar _bar = new(BarH), _healBar = new(2);
        readonly Grid _barLine = new();
        readonly Border _glow = new() { IsHitTestVisible = false };

        public FrameworkElement Root => _root;

        public ViewRow()
        {
            _root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _root.ColumnDefinitions.Add(new ColumnDefinition());
            _root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(Line1) });
            _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(2) });
            _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(BarH) });
            Grid.SetColumnSpan(_band, 3); Grid.SetRowSpan(_band, 3); _root.Children.Add(_band);
            _rank.HorizontalAlignment = HorizontalAlignment.Center; _rankBox.Child = _rank;
            _root.Children.Add(_rankBox);
            _cp.Margin = new Thickness(7, 1, 0, 0);
            _healBox.Child = _heal;
            var line = NameLine(_name, _cp, 12.5, _tags, _healBox);
            line.Children.Insert(0, _dot);
            Grid.SetColumn(line, 1); _root.Children.Add(line);
            _fig.Margin = new Thickness(10, 0, 0, 0); _fig.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(_fig, 2); _root.Children.Add(_fig);
            // the bar's line: the bar (its glow behind, a healer's green trace under it) and the small figure
            _barLine.ColumnDefinitions.Add(new ColumnDefinition());
            _barLine.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var level = new Grid();
            _bar.Track.Background = Br(0xFFFFFF, .09);
            _bar.Track.CornerRadius = new CornerRadius(1);
            level.Children.Add(_bar.Root);
            level.Children.Add(new Rectangle { Fill = Lines(Rgb(0x000000, .16), 3, 1), IsHitTestVisible = false });
            _healBar.Root.VerticalAlignment = VerticalAlignment.Bottom; _healBar.Root.Margin = new Thickness(0, 0, 0, -4);
            _healBar.Fill.Children.Add(new Border { Background = Green });
            level.Children.Add(_healBar.Root);
            _barLine.Children.Add(level);
            _small.Margin = new Thickness(8, 0, 0, 0); _small.TextAlignment = TextAlignment.Right; _small.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetColumn(_small, 1); _barLine.Children.Add(_small);
            Grid.SetRow(_barLine, 2); Grid.SetColumnSpan(_barLine, 3); _root.Children.Add(_barLine);
        }

        public void Set(MeterMember m, MeterContext c, bool fresh)
        {
            bool you = m.You;
            var cc = m.ClassColor;
            _rank.Text = m.Rank.ToString("00", CultureInfo.InvariantCulture);
            _rankBox.Background = you ? White : Br(0xFFFFFF, .1);
            _rankBox.BorderBrush = you ? White : Br(0xFFFFFF, .4); _rankBox.BorderThickness = new Thickness(1);
            _rank.Foreground = you ? Black : White;
            _band.Visibility = you ? Visibility.Visible : Visibility.Collapsed;
            _dot.Visibility = you ? Visibility.Visible : Visibility.Collapsed;
            _name.Text = Caps(m.Name);
            _name.Foreground = you ? Br(0xFFF1C2) : White;
            SetCp(_cp, m, Br(0xFFFFFF, .45), Br(0xFFFFFF, .62));
            SetHeal(_healBox, _heal, m, Green);
            Marks(_tags, m, m.TopHeals && m.Heal.Length > 0);
            _fig.Text = m.Figure;
            _fig.Foreground = m.CpOnly ? Dim : White;
            // the bar: the class's colour lit from above, a hot line along its top and a bright tip, a soft glow of its colour round it
            _bar.Fill.Children.Clear();
            _bar.Fill.Children.Add(new Border { Background = Vertical((Lift(cc, 45), 0), (cc, .45), (Mix(cc, Colors.Black, .28), 1)), CornerRadius = new CornerRadius(1) });
            _bar.Fill.Children.Add(new Border { Height = 1, VerticalAlignment = VerticalAlignment.Top, Background = Br(0xFFFFFF, .55), Margin = new Thickness(1, 0, 1, 0) });
            _bar.Fill.Children.Add(new Border { Width = 2, HorizontalAlignment = HorizontalAlignment.Right, Background = Br(0xFFFFFF, .9) });
            _bar.Fill.Effect = new DropShadowEffect { Color = Lift(cc, 30), BlurRadius = 14, ShadowDepth = 0, Opacity = .85 };
            _bar.Root.Visibility = m.CpOnly ? Visibility.Hidden : Visibility.Visible;
            _bar.Share = BarOf(m, c);
            _healBar.Root.Visibility = m.HealShare > 0 ? Visibility.Visible : Visibility.Collapsed;
            _healBar.Share = m.HealShare;
            _small.Text = Small(m, c);
            _small.MinWidth = SmallW(9.5, c.Expanded && c.E.HasDamage);
        }
    }

    // ==== B: the deck

    static void DeckFrame(Grid ground, Grid over)
    {
        // the deck's brushed front: dark gunmetal, a lit top edge, fine horizontal grain
        ground.Children.Add(new Border { Background = Vertical((Rgb(0x23262D, .95), 0), (Rgb(0x121419, .95), .3), (Rgb(0x08090B, .95), 1)), CornerRadius = new CornerRadius(4),
            BorderBrush = Vertical((Rgb(0xFFFFFF, .22), 0), (Rgb(0xFFFFFF, .05), .2), (Rgb(0x000000, .6), 1)), BorderThickness = new Thickness(1) });
        ground.Children.Add(new Rectangle { Fill = Lines(Rgb(0xFFFFFF, .022), 2, 1), RadiusX = 4, RadiusY = 4 });
        ground.Children.Add(new Rectangle { Fill = NoiseBrush(.3), Height = 2, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(3, 0, 3, 2) });
    }

    static FrameworkElement DeckHead(Encounter e, DateTime now)
    {
        // the display window: black glass recessed in the deck, a cyan fluorescent display behind it, its rim split red and cyan
        var vfd = Br(VfdC);
        var window = new Grid();
        var glassR = new Border { BorderBrush = Br(RedC, .6), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3), RenderTransform = new TranslateTransform(-1, 0) };
        var glassC = new Border { BorderBrush = Br(CyanC, .6), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3), RenderTransform = new TranslateTransform(1, 0) };
        var glass = new Border
        {
            CornerRadius = new CornerRadius(3), BorderBrush = Br(0x000000, .9), BorderThickness = new Thickness(1),
            Background = Vertical((Rgb(0x02090B, .97), 0), (Rgb(0x041216, .97), 1)),
        };
        window.Children.Add(glassR); window.Children.Add(glassC); window.Children.Add(glass);
        window.Children.Add(new Rectangle { Fill = Lines(Rgb(VfdC, .045), 2, 1), RadiusX = 3, RadiusY = 3, IsHitTestVisible = false, Margin = new Thickness(1) });
        // the display's haze behind the clock (light on the glass, never on the figures)
        window.Children.Add(new Rectangle { IsHitTestVisible = false, Margin = new Thickness(1), RadiusX = 3, RadiusY = 3,
            Fill = new RadialGradientBrush { Center = new Point(.86, .4), GradientOrigin = new Point(.86, .4), RadiusX = .35, RadiusY = 1, GradientStops = { new GradientStop(Rgb(VfdC, .16), 0), new GradientStop(Rgb(VfdC, 0), 1) } } });
        window.Children.Add(new Border { Height = 1, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(4, 1, 4, 0), Background = Br(0xFFFFFF, .14) });
        var sp = new StackPanel { Margin = new Thickness(10, 6, 10, 7) };
        var top = new Grid();
        top.ColumnDefinitions.Add(new ColumnDefinition()); top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var st = new StackPanel { Orientation = Orientation.Horizontal };
        if (e.Live && !e.Waiting) st.Children.Add(T(L.T("▶ PLAY"), 10.5, vfd, Osd));
        else
        {
            st.Children.Add(T(e.Waiting ? "‖ " + L.T("STBY") : "■ " + L.T("STOP"), 10.5, vfd, Osd));
            if (Outcome(e) is { Length: > 0 } o) st.Children.Add(T("   " + Up(o), 10.5, OutcomeInk(e), Osd));
        }
        top.Children.Add(st);
        var ch = T("SP  " + Channel(e), 10, Br(VfdC, .6), Osd);
        Grid.SetColumn(ch, 1); top.Children.Add(ch);
        sp.Children.Add(top);
        var title = new Grid { Margin = new Thickness(0, 3, 0, 0) };
        title.ColumnDefinitions.Add(new ColumnDefinition()); title.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var boss = T(e.Title, 15, Br(0xE6FDFF), Sans, trim: true);
        title.Children.Add(boss);
        var clock = T(ClockOf(e, now), e.Waiting ? 13 : 20, vfd, Sans);
        clock.Margin = new Thickness(12, 0, 0, 0);
        Grid.SetColumn(clock, 1); title.Children.Add(clock);
        sp.Children.Add(title);
        window.Children.Add(sp);
        return window;
    }

    /// <summary>
    /// B: the row is the bar. A thick track the row's width, the class's colour filling it from the left, deep enough that the
    /// white name reads on it; on it the rank in a black chip, the name, CP and marks, the share at the right; the DPS in a
    /// counter window of its own at the end.
    /// </summary>
    sealed class DeckRow : IThemeRow
    {
        const double H = 25;
        readonly Grid _root = new() { Height = H };
        readonly Bar _bar = new(H), _healBar = new(3);
        readonly Border _chip = new() { MinWidth = 19, Height = 17, Padding = new Thickness(3, 0, 3, 0), CornerRadius = new CornerRadius(2), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(3, 0, 6, 0) };
        readonly TextBlock _rank = T("", 11, White, Sans);
        readonly Ellipse _dot = new() { Width = 7, Height = 7, Fill = Red, Margin = new Thickness(0, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center };
        readonly TextBlock _name = T("", 12.5, White, Sans, trim: true), _cp = T("", 9.5, Br(0xFFFFFF, .78), Sans, FontWeights.SemiBold);
        readonly StackPanel _tags = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        readonly Border _healBox = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
        readonly TextBlock _heal = T("", 10, Br(0x7DFFAA), Sans);
        readonly TextBlock _small = T("", 9.5, Br(0xFFFFFF, .85), Sans, FontWeights.SemiBold);
        readonly Border _counter = new() { CornerRadius = new CornerRadius(2), BorderThickness = new Thickness(1), Margin = new Thickness(5, 0, 0, 0) };
        readonly TextBlock _fig = FigCell(16, White, Sans, 0, FontWeights.Bold);

        public FrameworkElement Root => _root;

        public DeckRow()
        {
            _root.ColumnDefinitions.Add(new ColumnDefinition());
            _root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _bar.Track.Background = Vertical((Rgb(0x000000, .62), 0), (Rgb(0x000000, .5), 1));
            _bar.Track.BorderBrush = Br(0xFFFFFF, .09); _bar.Track.BorderThickness = new Thickness(1); _bar.Track.CornerRadius = new CornerRadius(2);
            _root.Children.Add(_bar.Root);
            _healBar.Root.VerticalAlignment = VerticalAlignment.Bottom; _healBar.Root.Margin = new Thickness(1, 0, 1, 1);
            _healBar.Fill.Children.Add(new Border { Background = Green });
            _root.Children.Add(_healBar.Root);
            // what reads on the bar, each word with a tight dark halo so it holds on any class colour
            var on = new Grid();
            on.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            on.ColumnDefinitions.Add(new ColumnDefinition());
            on.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _rank.HorizontalAlignment = HorizontalAlignment.Center; _chip.Child = _rank;
            on.Children.Add(_chip);
            _cp.Margin = new Thickness(7, 1, 0, 0);
            _healBox.Child = _heal;
            var line = NameLine(_name, _cp, 12.5, _tags, _healBox);
            line.Children.Insert(0, _dot);
            line.Effect = Halo();
            Grid.SetColumn(line, 1); on.Children.Add(line);
            _small.Margin = new Thickness(6, 0, 6, 0); _small.TextAlignment = TextAlignment.Right; _small.Effect = Halo();
            Grid.SetColumn(_small, 2); on.Children.Add(_small);
            _root.Children.Add(on);
            // the counter: a black window, the DPS white in it
            _counter.Background = Vertical((Rgb(0x000000, .92), 0), (Rgb(0x0A0C10, .92), 1));
            _counter.BorderBrush = Br(0xFFFFFF, .16);
            _fig.Margin = new Thickness(7, 0, 6, 0); _fig.VerticalAlignment = VerticalAlignment.Center;
            _counter.Child = _fig;
            Grid.SetColumn(_counter, 1); _root.Children.Add(_counter);
        }

        static DropShadowEffect Halo() => new() { Color = Colors.Black, BlurRadius = 4, ShadowDepth = 0, Opacity = .9 };

        public void Set(MeterMember m, MeterContext c, bool fresh)
        {
            bool you = m.You;
            var cc = m.ClassColor;
            _rank.Text = m.Rank.ToString(CultureInfo.InvariantCulture);
            _chip.Background = you ? Br(0xFFFFFF) : Br(0x000000, .78);
            _chip.BorderBrush = you ? null : Br(0xFFFFFF, .25); _chip.BorderThickness = new Thickness(you ? 0 : 1);
            _rank.Foreground = you ? Black : White;
            _dot.Visibility = you ? Visibility.Visible : Visibility.Collapsed;
            _name.Text = Caps(m.Name);
            _name.Foreground = you ? Br(0xFFF1C2) : White;
            SetCp(_cp, m, Br(0xFFFFFF, .6), Br(0xFFFFFF, .82));
            SetHeal(_healBox, _heal, m, Br(0x7DFFAA));
            Marks(_tags, m, m.TopHeals && m.Heal.Length > 0);
            // the fill: the class's colour taken down so white words read on it, a lit top edge, a bright tip
            _bar.Fill.Children.Clear();
            _bar.Fill.Children.Add(new Border { CornerRadius = new CornerRadius(2), Background = Vertical((Mix(cc, Colors.Black, .12), 0), (Mix(cc, Colors.Black, .38), .55), (Mix(cc, Colors.Black, .55), 1)) });
            _bar.Fill.Children.Add(new Border { Height = 2, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(1, 1, 1, 0), Background = Frozen(Lift(cc, 60)) });
            _bar.Root.Visibility = m.CpOnly ? Visibility.Hidden : Visibility.Visible;
            _bar.Share = BarOf(m, c);
            _healBar.Root.Visibility = m.HealShare > 0 ? Visibility.Visible : Visibility.Collapsed;
            _healBar.Share = m.HealShare;
            _small.Text = Small(m, c);
            _small.MinWidth = SmallW(9.5, c.Expanded && c.E.HasDamage);
            _fig.Text = m.Figure;
            _fig.Foreground = m.CpOnly ? Dim : White;
            _fig.MinWidth = Math.Ceiling(TextW("199.9K", Sans, 16, FontWeights.Bold));
            _counter.BorderBrush = m.TopDps ? Br(YellowC, .85) : you ? Br(0xFFFFFF, .5) : Br(0xFFFFFF, .16);
        }
    }

    // ==== C: the blue screen

    static readonly Color BlueTop = Rgb(0x1838D0), BlueFoot = Rgb(0x0A1B8C);

    static void BlueFrame(Grid ground, Grid over)
    {
        ground.Children.Add(new Border { Background = Vertical((Color.FromArgb(244, BlueTop.R, BlueTop.G, BlueTop.B), 0), (Color.FromArgb(244, BlueFoot.R, BlueFoot.G, BlueFoot.B), 1)) });
        ground.Children.Add(new Rectangle { Fill = Lines(Rgb(0xFFFFFF, .04), 3, 1) });
        ground.Children.Add(new Rectangle { Fill = Vignette(Rgb(0x000020), .35) });
        ground.Children.Add(new Rectangle { Fill = NoiseBrush(.22), Height = 2, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 1) });
        // the menu's double rule: the outer one split red and cyan, a fainter one inside it
        var red = new Border { BorderBrush = Br(RedC, .7), BorderThickness = new Thickness(1.5), RenderTransform = new TranslateTransform(-1.2, 0) };
        var cyan = new Border { BorderBrush = Br(CyanC, .7), BorderThickness = new Thickness(1.5), RenderTransform = new TranslateTransform(1.2, 0) };
        over.Children.Add(red); over.Children.Add(cyan);
        over.Children.Add(new Border { BorderBrush = Br(0xFFFFFF, .95), BorderThickness = new Thickness(1.5) });
        over.Children.Add(new Border { BorderBrush = Br(0xFFFFFF, .35), BorderThickness = new Thickness(1), Margin = new Thickness(4) });
    }

    static FrameworkElement BlueHead(Encounter e, DateTime now)
    {
        var blue = Br(0x1530B8);
        var sp = new StackPanel();
        // the menu's title bar: white, the tape's state and the boss in the blue, the clock at its end
        var bar = new Border { Background = White, Padding = new Thickness(7, 3, 8, 3), Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 0, ShadowDepth = 2, Direction = 315, Opacity = .45 } };
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var state = T(e.Live && !e.Waiting ? L.T("▶ PLAY") : e.Waiting ? "‖ " + L.T("STBY") : "■ " + L.T("STOP"), 10.5, blue, Sans);
        state.Margin = new Thickness(0, 0, 9, 0);
        g.Children.Add(state);
        var boss = T(e.Title, 14, blue, Sans, trim: true);
        Grid.SetColumn(boss, 1); g.Children.Add(boss);
        var clock = T(ClockOf(e, now), e.Waiting ? 12 : 17, e.Live && !e.Waiting ? Br(0xC0120A) : blue, Sans);
        clock.Margin = new Thickness(10, 0, 0, 0);
        Grid.SetColumn(clock, 2); g.Children.Add(clock);
        bar.Child = g;
        sp.Children.Add(bar);
        if (Outcome(e) is { Length: > 0 } o)
        {
            var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            line.Children.Add(new Border { Width = 8, Height = 8, Background = OutcomeInk(e), Margin = new Thickness(1, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center });
            line.Children.Add(T(Up(o), 12, White, Sans));
            sp.Children.Add(line);
        }
        return sp;
    }

    /// <summary>
    /// C: a white rank tile down both lines; the name line over a thick bar in the class's colour on a deep navy track ruled in
    /// white; the DPS big at the right of both lines, the share small under it.
    /// </summary>
    sealed class BlueRow : IThemeRow
    {
        const double Line1 = 17, BarH = 12;
        readonly Grid _root = new() { Height = Line1 + 4 + BarH };
        readonly Border _band = new() { Margin = new Thickness(-6, -3, -6, -3), Visibility = Visibility.Collapsed, Background = Horizontal((Rgb(0xFFFFFF, .16), 0), (Rgb(0xFFFFFF, .05), 1)) };
        readonly Border _tile = new() { Width = 27, Margin = new Thickness(0, 0, 9, 0) };
        readonly TextBlock _rank = T("", 15, Br(0x1530B8), Sans);
        readonly Ellipse _dot = new() { Width = 7, Height = 7, Fill = Br(0xFF4A3D), Margin = new Thickness(0, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center };
        readonly TextBlock _name = T("", 12.5, White, Sans, trim: true), _cp = T("", 9.5, Br(0xFFFFFF, .72), Sans, FontWeights.SemiBold);
        readonly StackPanel _tags = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        readonly Border _healBox = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
        readonly TextBlock _heal = T("", 10.5, Br(0x7DFFAA), Sans);
        readonly Bar _bar = new(BarH), _healBar = new(2);
        readonly TextBlock _fig = FigCell(18.5, White, Sans, 0, FontWeights.Bold), _small = T("", 9.5, Br(0xFFFFFF, .78), Sans, FontWeights.SemiBold);

        public FrameworkElement Root => _root;

        public BlueRow()
        {
            _root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _root.ColumnDefinitions.Add(new ColumnDefinition());
            _root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(Line1) });
            _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(4) });
            _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(BarH) });
            Grid.SetColumnSpan(_band, 3); Grid.SetRowSpan(_band, 3); _root.Children.Add(_band);
            _rank.HorizontalAlignment = HorizontalAlignment.Center; _tile.Child = _rank;
            Grid.SetRowSpan(_tile, 3); _root.Children.Add(_tile);
            _cp.Margin = new Thickness(7, 1, 0, 0);
            _healBox.Child = _heal;
            var line = NameLine(_name, _cp, 12.5, _tags, _healBox);
            line.Children.Insert(0, _dot);
            Grid.SetColumn(line, 1); _root.Children.Add(line);
            _bar.Track.Background = Br(0x040C3A, .9); _bar.Track.BorderBrush = Br(0xFFFFFF, .45); _bar.Track.BorderThickness = new Thickness(1);
            _bar.Fill.Margin = new Thickness(1);
            var level = new Grid();
            level.Children.Add(_bar.Root);
            _healBar.Root.VerticalAlignment = VerticalAlignment.Bottom; _healBar.Root.Margin = new Thickness(1, 0, 1, 1);
            _healBar.Fill.Children.Add(new Border { Background = Br(0x45FF86) });
            level.Children.Add(_healBar.Root);
            Grid.SetRow(level, 2); Grid.SetColumn(level, 1); _root.Children.Add(level);
            var right = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(11, 0, 0, 0) };
            _fig.HorizontalAlignment = HorizontalAlignment.Right;
            _small.HorizontalAlignment = HorizontalAlignment.Right; _small.TextAlignment = TextAlignment.Right; _small.Margin = new Thickness(0, -1, 0, 0);
            right.Children.Add(_fig); right.Children.Add(_small);
            Grid.SetColumn(right, 2); Grid.SetRowSpan(right, 3); _root.Children.Add(right);
        }

        public void Set(MeterMember m, MeterContext c, bool fresh)
        {
            bool you = m.You;
            var cc = m.ClassColor;
            _rank.Text = m.Rank.ToString(CultureInfo.InvariantCulture);
            // the menu's cursor: your tile inverted (white, the blue in it), the others ruled in white
            _tile.Background = you ? White : Br(0x040C3A, .45);
            _tile.BorderBrush = you ? White : Br(0xFFFFFF, .9); _tile.BorderThickness = new Thickness(1.5);
            _rank.Foreground = you ? Br(0x1530B8) : White;
            _band.Visibility = you ? Visibility.Visible : Visibility.Collapsed;
            _dot.Visibility = you ? Visibility.Visible : Visibility.Collapsed;
            _name.Text = Caps(m.Name);
            _name.Foreground = you ? Br(0xFFF1C2) : White;
            SetCp(_cp, m, Br(0xFFFFFF, .55), Br(0xFFFFFF, .78));
            SetHeal(_healBox, _heal, m, Br(0x7DFFAA));
            Marks(_tags, m, m.TopHeals && m.Heal.Length > 0);
            _bar.Fill.Children.Clear();
            _bar.Fill.Children.Add(new Border { Background = Vertical((Lift(cc, 40), 0), (cc, .5), (Mix(cc, Colors.Black, .25), 1)) });
            _bar.Fill.Children.Add(new Border { Height = 1, VerticalAlignment = VerticalAlignment.Top, Background = Br(0xFFFFFF, .5) });
            _bar.Fill.Children.Add(new Border { Width = 2, HorizontalAlignment = HorizontalAlignment.Right, Background = White });
            _bar.Root.Visibility = m.CpOnly ? Visibility.Hidden : Visibility.Visible;
            _bar.Share = BarOf(m, c);
            _healBar.Root.Visibility = m.HealShare > 0 ? Visibility.Visible : Visibility.Collapsed;
            _healBar.Share = m.HealShare;
            _fig.Text = m.Figure;
            _fig.Foreground = m.CpOnly ? Dim : White;
            _fig.MinWidth = Math.Ceiling(TextW("199.9K", Sans, 18.5, FontWeights.Bold));
            _small.Text = Small(m, c);
        }
    }
}
