using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;
using KinahLedger.Parser;

namespace KinahLedger.Ui;

/// <summary>
/// Three new looks to take Goo's place in the Wacky theme (Steven, 2026-11-17, of the candy and slime concepts: "abysmal dont
/// even do this theme find a new one"), loud and playful beside Sticker and Neon. In each the damage bar is the row, the DPS
/// the one big figure, share small, Combat Power small and quiet by the name, the heal on healers only, TOP DPS and TOP
/// HEALS small marks; crit and back behind Details; anything else in the hover card.
/// <list type="bullet">
/// <item>A, Backglass: a pinball machine's backglass. A chrome-framed black cabinet, a red marquee ringed with chasing bulbs,
/// each row a lit lane of lamp inserts in the class's colour, the rank an amber insert light, the DPS in an amber score
/// window.</item>
/// <item>B, Versus: a fighting game's health bars. Slanted cel-shaded bars with heavy black outlines, the boss on a red
/// banner, the clock as the round timer, names and figures in outlined italics.</item>
/// <item>C, Holo card: a trading card. A holographic foil frame round a midnight card, each member an attack line (an energy
/// orb with the rank, the name, the DPS big as the attack's damage) over a foil bar that shimmers in rainbow stripes.</item>
/// </list>
/// Drawn for renders only (MeterConcepts, under Goo's key, the look they would replace), never offered in Settings.
/// </summary>
public static partial class MeterConcepts
{
    static partial void AddGoo(List<Concept> l)
    {
        l.Add(new Concept(HudThemes.Goo, "A", "Backglass", (e, now, w) => WackyConcept.Face(WackyConcept.Kind.Backglass, e, now, w)));
        l.Add(new Concept(HudThemes.Goo, "B", "Versus", (e, now, w) => WackyConcept.Face(WackyConcept.Kind.Versus, e, now, w)));
        l.Add(new Concept(HudThemes.Goo, "C", "Holo card", (e, now, w) => WackyConcept.Face(WackyConcept.Kind.Holo, e, now, w)));
    }
}

/// <summary>The Wacky concepts' drawing: the parts every concept dresses, and each concept's frame and rows.</summary>
static class WackyConcept
{
    public enum Kind { Backglass, Versus, Holo }

    static readonly FontFamily Inter = HudKit.Text;
    static readonly Brush White = Wk.S(0xFFFFFF);
    const uint Black = 0x000000;

    /// <summary>A neutral skills card (the Parser's): the hover card holds what a row leaves out.</summary>
    public static FrameworkElement Face(Kind k, Encounter e, DateTime now, double w)
    {
        var face = k switch { Kind.Backglass => BackglassFace(e, now, w), Kind.Versus => VersusFace(e, now, w), _ => HoloFace(e, now, w) };
        var host = new Border { Child = face, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Padding = new Thickness(0, 0, 0, 6) };
        TextElement.SetFontFamily(host, Inter);
        TextElement.SetFontWeight(host, FontWeights.Medium);
        TextOptions.SetTextFormattingMode(host, TextFormattingMode.Display);
        Typography.SetNumeralAlignment(host, FontNumeralAlignment.Tabular);
        return host;
    }

    // ---- shared parts

    /// <summary>
    /// Text with a dark line under it (a copy a pixel down), or, <paramref name="outline"/>, a full outline (copies all round),
    /// never an effect: reads on any colour and over the game.
    /// </summary>
    sealed class Inked
    {
        public readonly Grid Root = new() { VerticalAlignment = VerticalAlignment.Center };
        public readonly TextBlock Main;
        readonly List<TextBlock> _under = new();
        public Inked(double size, FontWeight w, Brush fg, uint drop = Black, double dropA = .85, double dy = 1.2, bool figure = false, bool trim = false, bool outline = false, bool italic = false, double ow = 1.1)
        {
            TextBlock Make(Brush b) => new()
            {
                FontSize = size, FontWeight = w, Foreground = b, FontFamily = Inter, VerticalAlignment = VerticalAlignment.Center, FontStyle = italic ? FontStyles.Italic : FontStyles.Normal,
                TextTrimming = trim ? TextTrimming.CharacterEllipsis : TextTrimming.None,
            };
            var offsets = outline ? new[] { (-ow, 0.0), (ow, 0.0), (0.0, -ow), (0.0, ow), (ow, ow + .6), (-ow * .7, -ow * .7), (ow * .7, -ow * .7), (-ow * .7, ow * .7) } : new[] { (0.0, dy) };
            foreach (var (x, y) in offsets)
            {
                var u = Make(Wk.S(drop, dropA)); u.RenderTransform = new TranslateTransform(x, y); u.IsHitTestVisible = false;
                if (figure) Typography.SetNumeralAlignment(u, FontNumeralAlignment.Tabular);
                _under.Add(u); Root.Children.Add(u);
            }
            Main = Make(fg);
            if (figure) Typography.SetNumeralAlignment(Main, FontNumeralAlignment.Tabular);
            Root.Children.Add(Main);
        }
        public string Text { get => Main.Text; set { Main.Text = value; foreach (var u in _under) u.Text = value; } }
        public Brush Fg { set => Main.Foreground = value; }
        public HorizontalAlignment Align
        {
            set
            {
                Root.HorizontalAlignment = value;
                var ta = value == HorizontalAlignment.Right ? TextAlignment.Right : value == HorizontalAlignment.Center ? TextAlignment.Center : TextAlignment.Left;
                foreach (var t in _under.Append(Main)) { t.HorizontalAlignment = value; t.TextAlignment = ta; }
            }
        }
    }

    /// <summary>A small tag: <paramref name="bg"/> behind, a dark rim, the words in <paramref name="fg"/>; skewed for Versus.</summary>
    static (Border Tag, TextBlock Text) Tag(string s, Brush bg, uint fg, double size = 8.5, double radius = 2, double skew = 0, uint rim = Black, bool italic = false)
    {
        var t = new TextBlock { Text = s, FontFamily = Inter, FontSize = size, FontWeight = FontWeights.Bold, Foreground = Wk.S(fg), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight, LineHeight = size + 3, FontStyle = italic ? FontStyles.Italic : FontStyles.Normal };
        Typography.SetNumeralAlignment(t, FontNumeralAlignment.Tabular);
        var g = new Grid();
        if (skew != 0)
        {
            g.Children.Add(new Border { Background = bg, BorderBrush = Wk.S(rim), BorderThickness = new Thickness(1.2), CornerRadius = new CornerRadius(radius), RenderTransformOrigin = new Point(.5, .5), RenderTransform = new SkewTransform(skew, 0) });
            g.Children.Add(new Border { Child = t, Padding = new Thickness(6, 0, 6, .5) });
            return (new Border { Child = g, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false }, t);
        }
        var b = new Border { Background = bg, BorderBrush = Wk.S(rim), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(radius), Padding = new Thickness(5, 0, 5, .5), VerticalAlignment = VerticalAlignment.Center, Child = t, IsHitTestVisible = false };
        return (b, t);
    }

    static string OutcomeText(Encounter e) => e.Live || e.Outcome.Length == 0 ? ""
        : L.T(e.Outcome == "kill" ? "Killed" : e.Outcome == "wipe" ? "Wiped" : e.Outcome == "cleared" ? "Cleared" : "Over");
    static bool Won(Encounter e) => e.Outcome is "kill" or "cleared";
    static double? HpOf(Encounter e) => e.Live && e.BossHpLeft is { } hp ? hp : null;
    static bool HasRows(Encounter e) => !e.Waiting || HudStackLook.PartyWaiting(e);
    static string Clock(Encounter e, DateTime now) => e.Waiting ? L.T("Ready") : BossParser.Clock(e.Duration(now));

    /// <summary>The foot: the fight's total on the left; the group's DPS (or your best or peak) on the right, the average's tag once held.</summary>
    static Grid Foot(Encounter e, DateTime now, Brush label, Brush value, Func<string, FrameworkElement> avg, bool italic = false)
    {
        var g = new Grid { Tag = "parser-foot" };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        TextBlock Pair(string word, string fig)
        {
            var t = new TextBlock { VerticalAlignment = VerticalAlignment.Center, FontFamily = Inter, FontStyle = italic ? FontStyles.Italic : FontStyles.Normal };
            Typography.SetNumeralAlignment(t, FontNumeralAlignment.Tabular);
            t.Inlines.Add(new Run(word) { FontSize = 10.5, FontWeight = FontWeights.SemiBold, Foreground = label });
            t.Inlines.Add(new Run(fig) { FontSize = 13.5, FontWeight = FontWeights.Bold, Foreground = value });
            return t;
        }
        g.Children.Add(Pair(L.T("Total "), Amounts.Meter(e.Credited)));
        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
        var (word, fig, held) = ParserFoot.Right(e, now);
        if (word.Length > 0) right.Children.Add(Pair(word + " ", fig));
        if (held) { var a = avg(L.T(Encounter.AvgBadge).ToUpperInvariant()); a.Margin = new Thickness(7, 0, 0, 0); right.Children.Add(a); }
        Grid.SetColumn(right, 1); g.Children.Add(right);
        return g;
    }

    /// <summary>"1 more": a small tag between the party and you pinned below it, a rule either side.</summary>
    static FrameworkElement Gap(int n, Brush rule, Func<string, FrameworkElement> tag)
    {
        var g = new Grid { VerticalAlignment = VerticalAlignment.Center };
        g.Children.Add(new Border { Height = 1.5, Margin = new Thickness(14, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center, Background = rule });
        if (n > 0) { var t = tag(L.F("{0} more", n)); t.HorizontalAlignment = HorizontalAlignment.Center; g.Children.Add(t); }
        return g;
    }

    /// <summary>The rows (ThemeRows: the Parser's members, ranks and pin), each drawn by <paramref name="make"/>.</summary>
    static ThemeRows Rows(Encounter e, DateTime now, double width, double pitch, Func<IThemeRow> make, double gapH, Func<int, FrameworkElement> gap)
    {
        var rows = new ThemeRows(pitch, _ => make(), gapH, gap) { Moves = false, Width = width };
        rows.Show(e, now);
        return rows;
    }

    /// <summary>The expanded row's detail ("127.81M · 38% crit"), else collapsed.</summary>
    static void Detail(Inked t, MeterMember m, MeterContext c)
    {
        if (!c.Expanded || m.Damage.Length == 0) { t.Root.Visibility = Visibility.Collapsed; return; }
        t.Text = m.Damage + (m.Crit.Length > 0 ? " · " + m.Crit + " " + L.T("crit") : "");
        t.Root.Visibility = Visibility.Visible;
    }

    static double FigRoom(bool cpOnly, double size, FontWeight w, double gap, bool italic = false) => Wk.Room(Inter, size, w, cpOnly ? new[] { "888,888" } : Wk.DpsForms, gap) + (italic ? 2 : 0);
    static double ShareRoom(bool cpOnly, double size, FontWeight w, double gap) => Wk.Room(Inter, size, w, cpOnly ? new[] { L.T("CP") } : Wk.ShareForms, gap);

    /// <summary>Combat Power, quiet: a small "CP" then the figure.</summary>
    static (FrameworkElement Box, Run Value) Cp(double size, uint label, double labelA, uint value, double valueA, Brush? chip = null, bool italic = false)
    {
        var t = new TextBlock { FontFamily = Inter, VerticalAlignment = VerticalAlignment.Center, FontStyle = italic ? FontStyles.Italic : FontStyles.Normal };
        Typography.SetNumeralAlignment(t, FontNumeralAlignment.Tabular);
        t.Inlines.Add(new Run(L.T("CP") + " ") { FontSize = size - 2, FontWeight = FontWeights.Bold, Foreground = Wk.S(label, labelA) });
        var v = new Run { FontSize = size, FontWeight = FontWeights.SemiBold, Foreground = Wk.S(value, valueA) };
        t.Inlines.Add(v);
        var b = new Border { Child = t, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
        if (chip != null) { b.Background = chip; b.CornerRadius = new CornerRadius(3); b.Padding = new Thickness(4, 0, 4, .5); }
        return (b, v);
    }

    /// <summary>The boss's HP: a bar in <paramref name="track"/> filled <paramref name="fill"/>, notched at the thirds, and "47% · 12.84M left".</summary>
    static Grid HpBar(Encounter e, double hp, double h, Brush track, Brush border, Brush fill, Brush pct, Brush left, double radius, double skew = 0, bool italic = false)
    {
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var bar = new Grid { Height = h, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(skew != 0 ? 3 : 0, 0, skew != 0 ? 3 : 0, 0) };
        if (skew != 0) { bar.RenderTransformOrigin = new Point(.5, .5); bar.RenderTransform = new SkewTransform(skew, 0); }
        bar.Children.Add(new Border { Background = track, BorderBrush = border, BorderThickness = new Thickness(1.5), CornerRadius = new CornerRadius(radius) });
        var inner = new Grid { Margin = new Thickness(2) };
        var f = new Border { Background = fill, CornerRadius = new CornerRadius(Math.Max(0, radius - 1.5)), HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
        inner.Children.Add(f);
        inner.Children.Add(new Border { Height = Math.Max(1.5, (h - 4) * .3), VerticalAlignment = VerticalAlignment.Top, Background = Wk.S(0xFFFFFF, .35), CornerRadius = new CornerRadius(1) , Margin = new Thickness(0, 0, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, Width = 0, Tag = "shine" });
        inner.SizeChanged += (_, _) => { f.Width = hp <= 0 ? 0 : Math.Max(h, inner.ActualWidth * hp); ((Border)inner.Children[1]).Width = f.Width; };
        bar.Children.Add(inner);
        var ticks = HudKit.HpTicks();
        foreach (var t in ticks.Children.OfType<Border>()) { t.Width = 2.5; t.Margin = new Thickness(0, 0, -1.25, 0); t.Background = Wk.S(Black, .95); }
        bar.Children.Add(ticks);
        row.Children.Add(bar);
        var words = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0), FontFamily = Inter, FontStyle = italic ? FontStyles.Italic : FontStyles.Normal };
        Typography.SetNumeralAlignment(words, FontNumeralAlignment.Tabular);
        words.Inlines.Add(new Run($"{Math.Round(hp * 100):0}%") { FontSize = 13.5, FontWeight = FontWeights.Bold, Foreground = pct });
        if (ParserPanel.HpLeft(e) is { Length: > 0 } l) words.Inlines.Add(new Run(" · " + l) { FontSize = 10.5, FontWeight = FontWeights.SemiBold, Foreground = left });
        Grid.SetColumn(words, 1); row.Children.Add(words);
        return row;
    }

    static string TopWord(MeterMember m) => m.TopDps ? L.T("TOP DPS") : L.T("TOP HEALS");
    static string Deaths(MeterMember m) => "✖ " + m.Deaths.ToString(CultureInfo.InvariantCulture);
    static Visibility Shown(bool on) => on ? Visibility.Visible : Visibility.Collapsed;

    // ---- A: backglass

    const uint Amber = 0xFFB21F, Cream = 0xFFF1C8, Gold = 0xD9A441;

    static FrameworkElement BackglassFace(Encounter e, DateTime now, double w)
    {
        var stack = new StackPanel();
        // the marquee: a red band ringed by chasing bulbs, the boss in cream, the clock in amber
        var marquee = new Grid { Margin = new Thickness(0, 0, 0, 0) };
        marquee.Children.Add(new Border { Background = Wk.Lin(0, 0, 1, 0, (0x4A030C, 1, 0), (0xA3101F, 1, .5), (0x4A030C, 1, 1)), CornerRadius = new CornerRadius(7, 7, 0, 0) });
        marquee.Children.Add(new Border { Background = Wk.Lin(0, 0, 0, 1, (0xFFFFFF, .14, 0), (0xFFFFFF, 0, .5), (Black, .35, 1)), CornerRadius = new CornerRadius(7, 7, 0, 0) });
        marquee.Children.Add(Bulbs(true)); marquee.Children.Add(Bulbs(false));
        var head = new Grid { Margin = new Thickness(14, 12, 13, 12) };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = new Inked(15, FontWeights.Bold, Wk.S(Cream), trim: true) { Text = e.Title };
        head.Children.Add(title.Root);
        if (OutcomeText(e) is { Length: > 0 } o)
        {
            var (tag, _) = Tag(o.ToUpperInvariant(), Wk.Lin(0, 0, 0, 1, (0xFFE27A, 1, 0), (0xE0A020, 1, 1)), 0x1A0800, 9.5, 3);
            tag.Margin = new Thickness(8, 0, 8, 0); Grid.SetColumn(tag, 1); head.Children.Add(tag);
        }
        var clock = new Inked(18, FontWeights.Bold, Wk.S(Amber), figure: !e.Waiting) { Text = Clock(e, now) };
        clock.Root.Margin = new Thickness(OutcomeText(e).Length == 0 ? 8 : 0, 0, 0, 0);
        Grid.SetColumn(clock.Root, 2); head.Children.Add(clock.Root);
        marquee.Children.Add(head);
        stack.Children.Add(marquee);
        stack.Children.Add(new Border { Height = 2, Background = Wk.Lin(0, 0, 1, 0, (Gold, .2, 0), (Gold, 1, .5), (Gold, .2, 1)) });
        if (!e.Waiting && HpOf(e) is double hp)
        {
            var t = HpBar(e, hp, 13, Wk.S(0x0A0508), Wk.S(Gold, .7), Wk.Lin(0, 0, 1, 0, (0xC81E2A, 1, 0), (0xFF6A1F, 1, .7), (0xFFC23A, 1, 1)), Wk.S(Amber), Wk.S(Cream, .8), 6.5);
            t.Margin = new Thickness(13, 10, 12, 0); stack.Children.Add(t);
        }
        if (e.Waiting) stack.Children.Add(new TextBlock { Text = e.ReadyText, FontFamily = Inter, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Wk.S(Cream, .85), Margin = new Thickness(13, 10, 12, 0), TextWrapping = TextWrapping.Wrap });
        if (HasRows(e))
        {
            var rows = Rows(e, now, w - 32, LaneRow.Pitch + LaneRow.Extra, () => new LaneRow(), 20, n => Gap(n, Wk.Lin(0, 0, 1, 0, (Gold, 0, 0), (Gold, .45, .5), (Gold, 0, 1)), s =>
            {
                var (t, _) = Tag(s, Wk.S(0x140A10), Gold, 9.5, 3, rim: Gold); return t;
            }));
            rows.Margin = new Thickness(10, 16, 10, 0);
            stack.Children.Add(rows);
        }
        if (!e.Waiting && e.HasDamage)
        {
            var f = Foot(e, now, Wk.S(Gold), Wk.S(Cream), s => Tag(s, Wk.Lin(0, 0, 0, 1, (0xFFE27A, 1, 0), (0xE0A020, 1, 1)), 0x1A0800, 8, 3).Tag);
            f.Margin = new Thickness(14, 6, 14, 11); stack.Children.Add(f);
        }
        else stack.Children.Add(new Border { Height = 10 });
        // the cabinet: black glass in a chrome bevel, a gold line inside it
        var inner = new Border { CornerRadius = new CornerRadius(7), Background = Wk.Lin(0, 0, 0, 1, (0x170A1C, .97, 0), (0x07030A, .97, 1)), BorderBrush = Wk.S(Gold, .85), BorderThickness = new Thickness(1), Child = stack };
        var chrome = new Border
        {
            CornerRadius = new CornerRadius(11), Padding = new Thickness(4), Child = inner, Width = w,
            Background = Wk.Lin(0, 0, .35, 1, (0xFFFFFF, 1, 0), (0x7A7A88, 1, .18), (0xF4F4FA, 1, .34), (0x9898A6, 1, .5), (0xFFFFFF, 1, .66), (0x50505C, 1, .84), (0xC8C8D2, 1, 1)),
            BorderBrush = Wk.S(Black, .9), BorderThickness = new Thickness(1),
        };
        return chrome;
    }

    /// <summary>A row of chasing bulbs along the marquee's top or foot edge, every other one lit.</summary>
    static Canvas Bulbs(bool top)
    {
        var c = new Canvas { IsHitTestVisible = false, VerticalAlignment = top ? VerticalAlignment.Top : VerticalAlignment.Bottom, Height = 0 };
        c.Loaded += (_, _) => { };
        void Fill(double width)
        {
            c.Children.Clear();
            int n = (int)Math.Floor((width - 16) / 11);
            double step = (width - 16) / Math.Max(1, n);
            for (int i = 0; i <= n; i++)
            {
                bool lit = i % 2 == 0;
                double x = 8 + i * step;
                if (lit) { var halo = new Ellipse { Width = 9, Height = 9, Fill = Wk.Rad(.5, .5, .5, (0xFFC23A, .55, 0), (0xFFC23A, 0, 1)) }; Canvas.SetLeft(halo, x - 4.5); Canvas.SetTop(halo, top ? -.5 : -8.5); c.Children.Add(halo); }
                var b = new Ellipse { Width = 4, Height = 4, Fill = lit ? Wk.Rad(.4, .4, .6, (0xFFFFFF, 1, 0), (0xFFE08A, 1, .5), (0xFFA21F, 1, 1)) : Wk.S(0x6A3A12) };
                Canvas.SetLeft(b, x - 2); Canvas.SetTop(b, top ? 2 : -6); c.Children.Add(b);
            }
        }
        c.SizeChanged += (_, ev) => Fill(ev.NewSize.Width);
        return c;
    }

    /// <summary>
    /// A row as a lit lane of the playfield: a recessed black lane with a gold rail, the class's colour lit up it to the member's
    /// damage against the top one's (a grid of lamp inserts, white-hot at its head); the rank an amber insert light, the name
    /// and quiet Combat Power on the lane, the share at its end; the DPS big in an amber score window at the right. TOP DPS,
    /// TOP HEALS and a healer's heal are lit tags on the lane's top edge, deaths a red one; your lane rails in white gold.
    /// </summary>
    sealed class LaneRow : IThemeRow
    {
        public const double H = 31, Pitch = 41;
        public static double Extra => ParserRows.ExpandedPref ? 9 : 0;
        readonly Grid _root = new() { Height = H, Background = Brushes.Transparent };
        readonly Border _lane = new() { CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1.2) };
        readonly Grid _track = new() { Margin = new Thickness(2) };
        readonly Grid _fill = new() { HorizontalAlignment = HorizontalAlignment.Left, Width = 0, IsHitTestVisible = false };
        readonly Border _body = new() { CornerRadius = new CornerRadius(4) }, _head = new() { Width = 3, HorizontalAlignment = HorizontalAlignment.Right, CornerRadius = new CornerRadius(1.5) }, _halo = new() { Width = 16, HorizontalAlignment = HorizontalAlignment.Right };
        readonly Wk.BarFill _bar;
        readonly ColumnDefinition _scoreCol = new();
        readonly Border _score = new() { CornerRadius = new CornerRadius(5), BorderThickness = new Thickness(1.2), Margin = new Thickness(5, 0, 0, 0) };
        readonly Inked _fig = new(16.5, FontWeights.Bold, Wk.S(Amber), figure: true);
        readonly Grid _insert = new() { Width = 20, Height = 20, VerticalAlignment = VerticalAlignment.Center };
        readonly Inked _rank = new(10.5, FontWeights.Bold, Wk.S(0x1A0800), drop: 0xFFFFFF, dropA: .35, figure: true);
        readonly Border _icon = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 5, 0) };
        readonly Inked _name = new(13, FontWeights.Bold, White, trim: true);
        readonly FrameworkElement _you, _cpBox;
        readonly Run _cp;
        readonly Inked _detail = new(9.5, FontWeights.SemiBold, Wk.S(Cream, .9));
        readonly Inked _share = new(10, FontWeights.SemiBold, Wk.S(Cream, .95), figure: true);
        readonly ColumnDefinition _shareCol = new();
        readonly Border _top, _heal, _dead;
        readonly TextBlock _topT, _healT, _deadT;
        string _cls = "\0";
        bool? _cpOnly;

        public FrameworkElement Root => _root;

        static readonly Brush Lamps = MakeLamps();
        static Brush MakeLamps()
        {
            var g = new GeometryDrawing(Wk.S(0xFFFFFF, .16), null, new EllipseGeometry(new Point(3, 3), 1.15, 1.15));
            var d = new DrawingGroup(); d.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, 6, 6)))); d.Children.Add(g);
            var b = new DrawingBrush(d) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 6, 6), ViewportUnits = BrushMappingMode.Absolute, Stretch = Stretch.None };
            b.Freeze(); return b;
        }

        public LaneRow()
        {
            _root.Height = H + Extra;
            _root.ColumnDefinitions.Add(new ColumnDefinition()); _root.ColumnDefinitions.Add(_scoreCol);
            _root.Children.Add(new Border { CornerRadius = new CornerRadius(6), Background = Wk.S(Black, .7), RenderTransform = new TranslateTransform(0, 2), IsHitTestVisible = false });
            _root.Children.Add(_lane);
            _root.Children.Add(new Border { CornerRadius = new CornerRadius(6), Margin = new Thickness(1.2), Background = Wk.Lin(0, 0, 0, 1, (Black, .6, 0), (Black, 0, .4)), IsHitTestVisible = false });
            _fill.Children.Add(_body);
            _fill.Children.Add(new Border { CornerRadius = new CornerRadius(4), Background = Lamps });
            _fill.Children.Add(new Border { CornerRadius = new CornerRadius(4), Background = Wk.Lin(0, 0, 0, 1, (0xFFFFFF, .28, 0), (0xFFFFFF, 0, .4), (Black, .35, 1)) });
            _fill.Children.Add(_halo); _fill.Children.Add(_head);
            _track.Children.Add(_fill);
            _root.Children.Add(_track);
            _bar = new Wk.BarFill(_track, _fill, 24);
            // the score window
            Grid.SetColumn(_score, 1); _root.Children.Add(_score);
            _fig.Align = HorizontalAlignment.Right; _fig.Root.Margin = new Thickness(0, 0, 7.5, 0);
            _score.Child = _fig.Root;
            // the words on the lane
            var row = new Grid { Margin = new Thickness(4, 0, 6, 0) };
            foreach (var cd in new[] { new ColumnDefinition { Width = GridLength.Auto }, new ColumnDefinition { Width = GridLength.Auto }, new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }, new ColumnDefinition { Width = GridLength.Auto }, _shareCol })
                row.ColumnDefinitions.Add(cd);
            _insert.Children.Add(new Ellipse { Fill = Wk.S(Black, .8), Margin = new Thickness(-1.5) });
            _insert.Children.Add(new Ellipse { Fill = Wk.Rad(.5, .42, .62, (0xFFF6D0, 1, 0), (0xFFC23A, 1, .55), (0xB0600A, 1, 1)), Stroke = Wk.S(0x5A2A00), StrokeThickness = 1 });
            _rank.Align = HorizontalAlignment.Center; _rank.Root.HorizontalAlignment = HorizontalAlignment.Center;
            _insert.Children.Add(_rank.Root);
            row.Children.Add(_insert);
            Grid.SetColumn(_icon, 1); row.Children.Add(_icon);
            var (you, _) = Tag(L.T("YOU"), Wk.S(Cream), 0x1A0800, 8.5, 3); you.Margin = new Thickness(5, 0, 0, 0); _you = you;
            (_cpBox, _cp) = Cp(9.5, Cream, .6, Cream, .9, Wk.S(Black, .5)); ((Border)_cpBox).Padding = new Thickness(3, 0, 3, .5); ((Border)_cpBox).Margin = new Thickness(5, 0, 0, 0);
            var nameLine = WkCard.NameLine(_name.Root, _you, _cpBox);
            var nameCol = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { nameLine, _detail.Root } };
            _detail.Root.HorizontalAlignment = HorizontalAlignment.Left; _detail.Root.Margin = new Thickness(1, 2.5, 0, 0);   // expanded: the detail under the name
            Grid.SetColumn(nameCol, 2); row.Children.Add(nameCol);
            _share.Align = HorizontalAlignment.Right;
            var shareChip = new Border { Child = _share.Root, Background = Wk.S(Black, .55), CornerRadius = new CornerRadius(3), Padding = new Thickness(3, 0, 3, .5), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
            Grid.SetColumn(shareChip, 4); row.Children.Add(shareChip);
            _root.Children.Add(row);
            (_heal, _healT) = Tag("", Wk.Lin(0, 0, 0, 1, (0xC8FF9A, 1, 0), (0x52D63A, 1, 1)), 0x0A1A00, 8.5, 3);
            (_top, _topT) = Tag("", Wk.S(Amber), 0x1A0800, 8.5, 3);
            _heal.Margin = new Thickness(0, 0, 4, 0);
            var marks = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, -8, 10, 0), Children = { _heal, _top } };
            Grid.SetColumnSpan(marks, 2); _root.Children.Add(marks);
            (_dead, _deadT) = Tag("", Wk.Lin(0, 0, 0, 1, (0xFF6A6A, 1, 0), (0xC8101E, 1, 1)), 0xFFFFFF, 8.5, 3);
            _dead.HorizontalAlignment = HorizontalAlignment.Left; _dead.VerticalAlignment = VerticalAlignment.Top; _dead.Margin = new Thickness(20, -8, 0, 0);
            _root.Children.Add(_dead);
        }

        public void Set(MeterMember m, MeterContext c, bool fresh)
        {
            uint v = Wk.Vivid(Wk.Hex(m.ClassColor));
            _lane.Background = Wk.Lin(0, 0, 0, 1, (0x0C060E, .97, 0), (0x1C1020, .97, 1));
            _lane.BorderBrush = m.You ? Wk.Lin(0, 0, 0, 1, (0xFFFFFF, 1, 0), (0xFFD86A, 1, 1)) : Wk.Lin(0, 0, 0, 1, (Gold, .95, 0), (0x6A4A1A, .9, 1));
            _lane.BorderThickness = new Thickness(m.You ? 1.8 : 1.2);
            _body.Background = Wk.Lin(0, 0, 1, 0, (Wk.Shade(v, -.62), 1, 0), (Wk.Shade(v, -.32), 1, .65), (Wk.Shade(v, -.05), 1, 1));
            _head.Background = Wk.S(Wk.Shade(v, .75));
            _halo.Background = Wk.Lin(0, 0, 1, 0, (v, 0, 0), (Wk.Shade(v, .4), .55, 1));
            _score.Background = Wk.Lin(0, 0, 0, 1, (0x060306, .98, 0), (0x140A08, .98, 1));
            _score.BorderBrush = m.You ? Wk.S(0xFFE8A0) : Wk.S(Amber, .75);
            _rank.Text = m.Rank.ToString(CultureInfo.InvariantCulture);
            if (_cls != m.Class) { _cls = m.Class; _icon.Child = ThemeText.Icon(m.Class, 16, m.Name); }
            _name.Text = m.Name; _name.Fg = m.You ? Wk.S(0xFFE8A0) : White;
            _you.Visibility = Shown(m.You);
            _cp.Text = m.Cp; _cpBox.Visibility = Shown(m.Cp.Length > 0 && !m.CpOnly);
            Detail(_detail, m, c);
            if (_cpOnly != m.CpOnly) { _cpOnly = m.CpOnly; _scoreCol.Width = new GridLength(FigRoom(m.CpOnly, 16.5, FontWeights.Bold, 0) + 20); _shareCol.Width = new GridLength(ShareRoom(m.CpOnly, 10, FontWeights.SemiBold, 7)); }
            _fig.Text = m.Figure; _share.Text = m.ShareText;
            _healT.Text = m.Heal.Length > 0 ? "✚ " + m.Heal : ""; _heal.Visibility = Shown(m.Heal.Length > 0);
            _top.Visibility = Shown(m.TopDps || m.TopHeals); _topT.Text = TopWord(m);
            _top.Background = m.TopDps ? Wk.Lin(0, 0, 0, 1, (0xFFE27A, 1, 0), (0xE0A020, 1, 1)) : Wk.Lin(0, 0, 0, 1, (0xC8FFE0, 1, 0), (0x3DD68C, 1, 1));
            _dead.Visibility = Shown(m.Deaths > 0); _deadT.Text = Deaths(m);
            Panel.SetZIndex(_root, m.Deaths > 0 || m.Heal.Length > 0 || m.TopDps || m.TopHeals ? 2 : 0);
            _fill.Visibility = m.CpOnly ? Visibility.Hidden : Visibility.Visible;
            _bar.Set(Wk.Bar(m, c), c, "lane", fresh);
        }
    }

    // ---- B: versus

    const uint Yellow = 0xFFD21F, Red = 0xE8202A;
    const double Skew = -14;

    static FrameworkElement VersusFace(Encounter e, DateTime now, double w)
    {
        var stack = new StackPanel();
        // the boss on a red banner, the outcome a yellow slash, the clock the round timer
        var head = new Grid { Margin = new Thickness(12, 10, 12, 0) };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var banner = new Grid();
        banner.Children.Add(new Border { Background = Wk.Lin(0, 0, 1, 0, (Red, 1, 0), (0x9A0A14, 1, .8), (0x9A0A14, 0, 1)), BorderBrush = Wk.S(Black), BorderThickness = new Thickness(0, 2, 0, 2), RenderTransformOrigin = new Point(.5, .5), RenderTransform = new SkewTransform(Skew, 0), Margin = new Thickness(4, 0, 0, 0) });
        banner.Children.Add(new Border { Width = 5, HorizontalAlignment = HorizontalAlignment.Left, Background = Wk.S(Yellow), RenderTransformOrigin = new Point(.5, .5), RenderTransform = new SkewTransform(Skew, 0), Margin = new Thickness(-3, 0, 0, 0) });
        var title = new Inked(14.5, FontWeights.Bold, White, trim: true, outline: true, italic: true, ow: 1.2) { Text = e.Title };
        var tl = new Grid { Margin = new Thickness(12, 5, 10, 6) };
        tl.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        tl.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        tl.Children.Add(title.Root);
        if (OutcomeText(e) is { Length: > 0 } o)
        {
            var (tag, _) = Tag(o.ToUpperInvariant(), Wk.Lin(0, 0, 0, 1, (0xFFF27A, 1, 0), (Yellow, 1, 1)), Black, 10, 0, Skew, italic: true);
            tag.Margin = new Thickness(8, 0, 2, 0); Grid.SetColumn(tag, 1); tl.Children.Add(tag);
        }
        banner.Children.Add(tl);
        head.Children.Add(banner);
        var timer = new Grid { Margin = new Thickness(10, 0, 0, 0), MinWidth = 58 };
        timer.Children.Add(new Border { Background = Wk.Lin(0, 0, 0, 1, (0x1A1C26, 1, 0), (0x05060A, 1, 1)), BorderBrush = Wk.S(Yellow), BorderThickness = new Thickness(2), RenderTransformOrigin = new Point(.5, .5), RenderTransform = new SkewTransform(Skew, 0) });
        var clock = new Inked(19, FontWeights.Bold, Wk.S(e.Live ? Yellow : 0xFFFFFFu), figure: !e.Waiting, outline: true, italic: true, ow: 1.2) { Text = Clock(e, now) };
        clock.Align = HorizontalAlignment.Center; clock.Root.Margin = new Thickness(9, 2, 9, 3);
        timer.Children.Add(clock.Root);
        Grid.SetColumn(timer, 1); head.Children.Add(timer);
        stack.Children.Add(head);
        if (!e.Waiting && HpOf(e) is double hp)
        {
            var t = HpBar(e, hp, 14, Wk.S(0x3A0A10), Wk.S(Black), Wk.Lin(0, 0, 0, 1, (0xFFF27A, 1, 0), (0xFFD21F, 1, .5), (0xF0A000, 1, .51), (0xD08000, 1, 1)), Wk.S(Yellow), Wk.S(0xFFFFFF, .8), 0, Skew, true);
            t.Margin = new Thickness(14, 10, 13, 0); stack.Children.Add(t);
        }
        if (e.Waiting) stack.Children.Add(new TextBlock { Text = e.ReadyText, FontFamily = Inter, FontSize = 12, FontWeight = FontWeights.SemiBold, FontStyle = FontStyles.Italic, Foreground = Wk.S(0xFFFFFF, .85), Margin = new Thickness(14, 10, 12, 0), TextWrapping = TextWrapping.Wrap });
        if (HasRows(e))
        {
            var rows = Rows(e, now, w - 24, VsRow.Pitch + VsRow.Extra, () => new VsRow(), 20, n => Gap(n, Wk.Lin(0, 0, 1, 0, (0xFFFFFF, 0, 0), (0xFFFFFF, .3, .5), (0xFFFFFF, 0, 1)), s =>
            {
                var (t, _) = Tag(s, Wk.S(0x14161F), 0xFFFFFF, 9.5, 0, Skew, rim: 0x6A6E80, italic: true); return t;
            }));
            rows.Margin = new Thickness(12, 16, 12, 0);
            stack.Children.Add(rows);
        }
        if (!e.Waiting && e.HasDamage)
        {
            var f = Foot(e, now, Wk.S(0xB8BCCB), White, s => Tag(s, Wk.S(Yellow), Black, 8, 0, Skew, italic: true).Tag, italic: true);
            f.Margin = new Thickness(15, 5, 15, 11); stack.Children.Add(f);
        }
        else stack.Children.Add(new Border { Height = 12 });
        // the slab: dark steel with a hard black edge, a red-to-yellow strike along its top
        var g = new Grid { Width = w };
        g.Children.Add(new Border { CornerRadius = new CornerRadius(3), Background = Wk.S(Black, .85), RenderTransform = new TranslateTransform(3, 3) });
        g.Children.Add(new Border { CornerRadius = new CornerRadius(3), Background = Wk.Lin(0, 0, 1, 1, (0x1C1F2B, .97, 0), (0x0B0C12, .97, .6), (0x14080C, .97, 1)), BorderBrush = Wk.S(Black), BorderThickness = new Thickness(2) });
        g.Children.Add(new Border { Height = 3, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(2, 2, 2, 0), Background = Wk.Lin(0, 0, 1, 0, (Red, 1, 0), (Yellow, 1, 1)) });
        g.Children.Add(stack);
        return g;
    }

    /// <summary>
    /// A row as a fighting game's health bar: a slanted bar with a heavy black outline, the class's colour cel-shaded (hard light
    /// top, hard shade foot) over a dark red ground, to the member's damage against the top one's. The rank a yellow slash, the
    /// name and DPS in outlined italics on the bar (the DPS big at its right end), share and Combat Power small; TOP DPS, TOP
    /// HEALS, a healer's heal and deaths are slashes on its top edge; your bar outlined in yellow.
    /// </summary>
    sealed class VsRow : IThemeRow
    {
        public const double H = 30, Pitch = 40;
        public static double Extra => ParserRows.ExpandedPref ? 11 : 0;
        readonly Grid _root = new() { Height = H, Background = Brushes.Transparent };
        readonly Grid _skewed = new() { Margin = new Thickness(5, 0, 5, 0), RenderTransformOrigin = new Point(.5, .5), RenderTransform = new SkewTransform(Skew, 0) };
        readonly Border _frame = new() { BorderThickness = new Thickness(2.2) };
        readonly Grid _track = new() { Margin = new Thickness(2.2) };
        readonly Grid _fill = new() { HorizontalAlignment = HorizontalAlignment.Left, Width = 0, IsHitTestVisible = false };
        readonly Border _body = new();
        readonly Wk.BarFill _bar;
        readonly Grid _rankTag = new() { Width = 22, Height = 20, VerticalAlignment = VerticalAlignment.Center };
        readonly Border _rankBg = new() { BorderBrush = Wk.S(Black), BorderThickness = new Thickness(1.4), RenderTransformOrigin = new Point(.5, .5), RenderTransform = new SkewTransform(Skew, 0) };
        readonly Inked _rank = new(12, FontWeights.Bold, Wk.S(Black), drop: 0xFFFFFF, dropA: .4, figure: true, italic: true);
        readonly Border _icon = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(5, 0, 5, 0) };
        readonly Inked _name = new(13, FontWeights.Bold, White, trim: true, outline: true, italic: true);
        readonly FrameworkElement _you, _cpBox;
        readonly Run _cp;
        readonly Inked _detail = new(9.5, FontWeights.SemiBold, White, outline: true, italic: true, ow: .9);
        readonly Inked _fig = new(18, FontWeights.Bold, White, figure: true, outline: true, italic: true, ow: 1.3), _share = new(10, FontWeights.Bold, Wk.S(0xE8EAF2), figure: true, outline: true, italic: true, ow: .9);
        readonly ColumnDefinition _figCol = new(), _shareCol = new();
        readonly Border _top, _heal, _dead;
        readonly TextBlock _topT, _healT, _deadT;
        string _cls = "\0";
        bool? _cpOnly;

        public FrameworkElement Root => _root;

        public VsRow()
        {
            _root.Height = H + Extra;
            _skewed.Children.Add(new Border { Background = Wk.S(Black, .8), RenderTransform = new TranslateTransform(2.5, 2.5), IsHitTestVisible = false });
            _skewed.Children.Add(_frame);
            _fill.Children.Add(_body);
            _fill.Children.Add(new Border { Height = 1.6, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1.5, 0, 0), Background = Wk.S(0xFFFFFF, .7) });
            _fill.Children.Add(new Border { Width = 3, HorizontalAlignment = HorizontalAlignment.Right, Background = Wk.S(Black, .55) });
            _track.Children.Add(_fill);
            _skewed.Children.Add(_track);
            _bar = new Wk.BarFill(_track, _fill, 12);
            _root.Children.Add(_skewed);
            var row = new Grid { Margin = new Thickness(0, 0, 14, 0) };
            foreach (var cd in new[] { new ColumnDefinition { Width = GridLength.Auto }, new ColumnDefinition { Width = GridLength.Auto }, new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }, new ColumnDefinition { Width = GridLength.Auto }, _figCol, _shareCol })
                row.ColumnDefinitions.Add(cd);
            _rankTag.Children.Add(_rankBg);
            _rank.Align = HorizontalAlignment.Center; _rankTag.Children.Add(_rank.Root);
            row.Children.Add(_rankTag);
            Grid.SetColumn(_icon, 1); row.Children.Add(_icon);
            var (you, _) = Tag(L.T("YOU"), Wk.S(Yellow), Black, 8.5, 0, Skew, italic: true); you.Margin = new Thickness(6, 0, 0, 0); _you = you;
            (_cpBox, _cp) = Cp(9.5, 0xFFFFFF, .65, 0xFFFFFF, .92, Wk.S(Black, .55), italic: true);
            var nameLine = WkCard.NameLine(_name.Root, _you, _cpBox);
            var nameCol = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { nameLine, _detail.Root } };
            _detail.Root.HorizontalAlignment = HorizontalAlignment.Left; _detail.Root.Margin = new Thickness(1, 2.5, 0, 0);   // expanded: the detail under the name
            Grid.SetColumn(nameCol, 2); row.Children.Add(nameCol);
            _fig.Align = HorizontalAlignment.Right; Grid.SetColumn(_fig.Root, 4); row.Children.Add(_fig.Root);
            _share.Align = HorizontalAlignment.Right; _share.Root.Margin = new Thickness(0, 2, 0, 0); Grid.SetColumn(_share.Root, 5); row.Children.Add(_share.Root);
            _root.Children.Add(row);
            (_heal, _healT) = Tag("", Wk.S(0x6CF04A), Black, 8.5, 0, Skew, italic: true);
            (_top, _topT) = Tag("", Wk.S(Yellow), Black, 8.5, 0, Skew, italic: true);
            _heal.Margin = new Thickness(0, 0, 5, 0);
            _root.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, -8, 16, 0), Children = { _heal, _top } });
            (_dead, _deadT) = Tag("", Wk.S(Red), 0xFFFFFF, 8.5, 0, Skew, italic: true);
            _dead.HorizontalAlignment = HorizontalAlignment.Left; _dead.VerticalAlignment = VerticalAlignment.Top; _dead.Margin = new Thickness(26, -8, 0, 0);
            _root.Children.Add(_dead);
        }

        public void Set(MeterMember m, MeterContext c, bool fresh)
        {
            uint v = Wk.Vivid(Wk.Hex(m.ClassColor), .85, .95);
            _frame.Background = Wk.Lin(0, 0, 0, 1, (0x3A0C12, .97, 0), (0x22060A, .97, 1));
            _frame.BorderBrush = m.You ? Wk.S(Yellow) : Wk.S(Black);
            _body.Background = Wk.Lin(0, 0, 0, 1, (Wk.Shade(v, .1), 1, 0), (Wk.Shade(v, .1), 1, .48), (Wk.Shade(v, -.3), 1, .49), (Wk.Shade(v, -.5), 1, 1));
            _rankBg.Background = m.You ? Wk.S(0xFFFFFF) : Wk.Lin(0, 0, 0, 1, (0xFFF27A, 1, 0), (Yellow, 1, 1));
            _rank.Text = m.Rank.ToString(CultureInfo.InvariantCulture);
            if (_cls != m.Class) { _cls = m.Class; _icon.Child = ThemeText.Icon(m.Class, 16, m.Name); }
            _name.Text = m.Name; _name.Fg = m.You ? Wk.S(Yellow) : White;
            _you.Visibility = Shown(m.You);
            _cp.Text = m.Cp; _cpBox.Visibility = Shown(m.Cp.Length > 0 && !m.CpOnly);
            Detail(_detail, m, c);
            if (_cpOnly != m.CpOnly) { _cpOnly = m.CpOnly; _figCol.Width = new GridLength(FigRoom(m.CpOnly, 18, FontWeights.Bold, 8, true)); _shareCol.Width = new GridLength(ShareRoom(m.CpOnly, 10, FontWeights.Bold, 7)); }
            _fig.Text = m.Figure; _fig.Fg = m.You ? Wk.S(Yellow) : White;
            _share.Text = m.ShareText;
            _healT.Text = m.Heal.Length > 0 ? "✚ " + m.Heal : ""; _heal.Visibility = Shown(m.Heal.Length > 0);
            _top.Visibility = Shown(m.TopDps || m.TopHeals); _topT.Text = TopWord(m);
            _dead.Visibility = Shown(m.Deaths > 0); _deadT.Text = Deaths(m);
            Panel.SetZIndex(_root, m.Deaths > 0 || m.Heal.Length > 0 || m.TopDps || m.TopHeals ? 2 : 0);
            _fill.Visibility = m.CpOnly ? Visibility.Hidden : Visibility.Visible;
            _bar.Set(Wk.Bar(m, c), c, "vs", fresh);
        }
    }

    // ---- C: holo card

    const uint Navy = 0x0A0F22, Ice = 0xDCE6FF;

    /// <summary>A holographic foil: rainbow bands at a slant, repeating every <paramref name="period"/> DIPs.</summary>
    static Brush Foil(double a, double period = 46)
    {
        var b = new LinearGradientBrush { MappingMode = BrushMappingMode.Absolute, StartPoint = new Point(0, 0), EndPoint = new Point(period, period * .55), SpreadMethod = GradientSpreadMethod.Repeat };
        foreach (var (c, at) in new[] { (0xFF6BD5u, 0.0), (0x7AE8FFu, .22), (0xFFF07Au, .44), (0xA98BFFu, .66), (0x7AFFC4u, .84), (0xFF6BD5u, 1.0) })
            b.GradientStops.Add(new GradientStop(HudKit.Rgb(c, a), at));
        b.Freeze();
        return b;
    }

    static FrameworkElement HoloFace(Encounter e, DateTime now, double w)
    {
        var stack = new StackPanel();
        var head = new Grid { Margin = new Thickness(13, 9, 12, 6) };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = new Inked(15.5, FontWeights.Bold, White, trim: true) { Text = e.Title };
        head.Children.Add(title.Root);
        if (OutcomeText(e) is { Length: > 0 } o)
        {
            var g = new Grid { Margin = new Thickness(8, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            g.Children.Add(new Border { CornerRadius = new CornerRadius(9), Background = Wk.Lin(0, 0, 1, 1, (0xFFFFFF, 1, 0), (0xDCE6FF, 1, 1)) });
            g.Children.Add(new Border { CornerRadius = new CornerRadius(9), Background = Foil(.55, 30) });
            g.Children.Add(new Border { CornerRadius = new CornerRadius(9), BorderBrush = Wk.S(0xFFFFFF, .9), BorderThickness = new Thickness(1), Padding = new Thickness(8, 0, 8, .5),
                Child = new TextBlock { Text = o, FontFamily = Inter, FontSize = 10.5, FontWeight = FontWeights.Bold, Foreground = Wk.S(Navy) } });
            Grid.SetColumn(g, 1); head.Children.Add(g);
        }
        var clock = new Inked(18, FontWeights.Bold, Wk.S(e.Live ? 0x9FF0FFu : 0xFFFFFFu), figure: !e.Waiting) { Text = Clock(e, now) };
        clock.Root.Margin = new Thickness(OutcomeText(e).Length == 0 ? 8 : 0, 0, 0, 0);
        Grid.SetColumn(clock.Root, 2); head.Children.Add(clock.Root);
        stack.Children.Add(head);
        stack.Children.Add(new Border { Height = 2, Margin = new Thickness(12, 0, 12, 0), Background = Foil(.9, 60), CornerRadius = new CornerRadius(1) });
        if (!e.Waiting && HpOf(e) is double hp)
        {
            var t = HpBar(e, hp, 12, Wk.S(0x050812), Wk.S(0xFFFFFF, .18), Wk.Lin(0, 0, 1, 0, (0xFF3A6A, 1, 0), (0xFF8A3A, 1, 1)), Wk.S(0xFF9AB0), Wk.S(Ice, .8), 3);
            t.Margin = new Thickness(13, 10, 12, 0); stack.Children.Add(t);
        }
        if (e.Waiting) stack.Children.Add(new TextBlock { Text = e.ReadyText, FontFamily = Inter, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Wk.S(Ice, .85), Margin = new Thickness(13, 10, 12, 0), TextWrapping = TextWrapping.Wrap });
        if (HasRows(e))
        {
            var rows = Rows(e, now, w - 30, CardRow.Pitch + CardRow.Extra, () => new CardRow(), 18, n => Gap(n, Wk.S(0xFFFFFF, .1), s =>
            {
                var (t, _) = Tag(s, Wk.S(0x141B38), Ice, 9.5, 8, rim: 0x3A4A80); return t;
            }));
            rows.Margin = new Thickness(9, 11, 9, 0);
            stack.Children.Add(rows);
        }
        if (!e.Waiting && e.HasDamage)
        {
            var f = Foot(e, now, Wk.S(0x9AA8D8), White, s =>
            {
                var g = new Grid { VerticalAlignment = VerticalAlignment.Center };
                g.Children.Add(new Border { CornerRadius = new CornerRadius(3), Background = Wk.S(0xFFFFFF) });
                g.Children.Add(new Border { CornerRadius = new CornerRadius(3), Background = Foil(.6, 24) });
                g.Children.Add(new Border { Padding = new Thickness(5, 0, 5, .5), Child = new TextBlock { Text = s, FontFamily = Inter, FontSize = 8, FontWeight = FontWeights.Bold, Foreground = Wk.S(Navy) } });
                return g;
            });
            f.Margin = new Thickness(14, 2, 14, 10); stack.Children.Add(f);
        }
        else stack.Children.Add(new Border { Height = 10 });
        // the card: midnight inside a holographic foil frame, a fine foil sheen over the card's face
        var face = new Grid();
        face.Children.Add(new Border { CornerRadius = new CornerRadius(10), Background = Wk.Lin(0, 0, 0, 1, (0x141C3C, .97, 0), (Navy, .97, .5), (0x070A18, .97, 1)) });
        face.Children.Add(new Border { CornerRadius = new CornerRadius(10), Background = Foil(.045, 90), IsHitTestVisible = false });
        face.Children.Add(new Border { CornerRadius = new CornerRadius(10), BorderBrush = Wk.Lin(0, 0, 0, 1, (Black, .7, 0), (0xFFFFFF, .12, 1)), BorderThickness = new Thickness(1), IsHitTestVisible = false });
        face.Children.Add(stack);
        var frame = new Grid { Width = w };
        frame.Children.Add(new Border { CornerRadius = new CornerRadius(14), Background = Wk.S(Black, .55), RenderTransform = new TranslateTransform(0, 3) });
        frame.Children.Add(new Border { CornerRadius = new CornerRadius(14), Background = Wk.Lin(0, 0, 1, 1, (0xE8ECF8, 1, 0), (0x9AA4C0, 1, .5), (0xF4F6FF, 1, 1)) });
        frame.Children.Add(new Border { CornerRadius = new CornerRadius(14), Background = Foil(.85, 70) });
        frame.Children.Add(new Border { CornerRadius = new CornerRadius(14), BorderBrush = Wk.S(0xFFFFFF, .85), BorderThickness = new Thickness(1) });
        frame.Children.Add(new Border { Padding = new Thickness(5), Child = face });
        return frame;
    }

    /// <summary>
    /// A member as an attack line on the card: an energy orb in the class's colour with the rank, the icon, the name, quiet
    /// Combat Power and deaths; the DPS big at the right as the attack's damage, the share small after it; under them a full-width
    /// bar of the class's colour washed in shifting holographic foil. A healer's heal and TOP DPS / TOP HEALS are foil stamps at
    /// the bar's right end; you a gold-ruled bar and gold name.
    /// </summary>
    sealed class CardRow : IThemeRow
    {
        public const double Line = 21, BarH = 15, H = Line + 3 + BarH, Pitch = H + 8;
        public static double Extra => ParserRows.ExpandedPref ? 11 : 0;
        readonly Grid _root = new() { Height = H, Background = Brushes.Transparent };
        readonly Grid _orb = new() { Width = 17, Height = 17, VerticalAlignment = VerticalAlignment.Center };
        readonly Ellipse _orbFill = new() { StrokeThickness = 1 };
        readonly Inked _rank = new(10, FontWeights.Bold, White, figure: true, dy: .8);
        readonly Border _icon = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(5, 0, 5, 0) };
        readonly Inked _name = new(13, FontWeights.SemiBold, White, trim: true);
        readonly FrameworkElement _you, _cpBox;
        readonly Run _cp;
        readonly Border _dead;
        readonly TextBlock _deadT;
        readonly Inked _detail = new(9.5, FontWeights.SemiBold, Wk.S(Ice, .9));
        readonly Inked _fig = new(17.5, FontWeights.Bold, White, figure: true), _share = new(10, FontWeights.SemiBold, Wk.S(0xA8B4E0), figure: true);
        readonly ColumnDefinition _figCol = new(), _shareCol = new();
        readonly Border _groove = new() { CornerRadius = new CornerRadius(3), BorderThickness = new Thickness(1) };
        readonly Grid _track = new() { Margin = new Thickness(1.5) };
        readonly Grid _fill = new() { HorizontalAlignment = HorizontalAlignment.Left, Width = 0, IsHitTestVisible = false };
        readonly Border _body = new() { CornerRadius = new CornerRadius(2) };
        readonly Wk.BarFill _bar;
        readonly Border _top, _heal;
        readonly TextBlock _topT, _healT;
        string _cls = "\0";
        bool? _cpOnly;

        public FrameworkElement Root => _root;

        static FrameworkElement Stamp(out TextBlock t)
        {
            var g = new Grid { VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
            g.Children.Add(new Border { CornerRadius = new CornerRadius(2), Background = Wk.S(0xFFFFFF) });
            g.Children.Add(new Border { CornerRadius = new CornerRadius(2), Background = Foil(.55, 22) });
            t = new TextBlock { FontFamily = Inter, FontSize = 8.5, FontWeight = FontWeights.Bold, Foreground = Wk.S(Navy), LineStackingStrategy = LineStackingStrategy.BlockLineHeight, LineHeight = 11 };
            Typography.SetNumeralAlignment(t, FontNumeralAlignment.Tabular);
            g.Children.Add(new Border { CornerRadius = new CornerRadius(2), BorderBrush = Wk.S(Navy, .9), BorderThickness = new Thickness(1), Padding = new Thickness(4, 0, 4, 0), Child = t });
            return g;
        }

        public CardRow()
        {
            _root.Height = H + Extra;
            var line = new Grid { Height = Line + Extra, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(1, 0, 3, 0) };
            foreach (var cd in new[] { new ColumnDefinition { Width = GridLength.Auto }, new ColumnDefinition { Width = GridLength.Auto }, new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }, new ColumnDefinition { Width = GridLength.Auto }, _figCol, _shareCol })
                line.ColumnDefinitions.Add(cd);
            _orb.Children.Add(new Ellipse { Fill = Wk.S(0xFFFFFF, .35), Margin = new Thickness(-1.5) });
            _orb.Children.Add(_orbFill);
            _orb.Children.Add(new Ellipse { Width = 6, Height = 3, Fill = Wk.S(0xFFFFFF, .75), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(3.5, 2.2, 0, 0), IsHitTestVisible = false });
            _rank.Align = HorizontalAlignment.Center; _rank.Root.HorizontalAlignment = HorizontalAlignment.Center;
            _orb.Children.Add(_rank.Root);
            line.Children.Add(_orb);
            Grid.SetColumn(_icon, 1); line.Children.Add(_icon);
            var you = new Grid { Margin = new Thickness(5, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            you.Children.Add(new Border { CornerRadius = new CornerRadius(3), Background = Wk.Lin(0, 0, 0, 1, (0xFFF0B0, 1, 0), (0xE8B53A, 1, 1)) });
            you.Children.Add(new Border { Padding = new Thickness(5, 0, 5, .5), Child = new TextBlock { Text = L.T("YOU"), FontFamily = Inter, FontSize = 8.5, FontWeight = FontWeights.Bold, Foreground = Wk.S(0x2A1A00) } });
            _you = you;
            (_cpBox, _cp) = Cp(9.5, 0x9AA8D8, .85, Ice, .85);
            (_dead, _deadT) = Tag("", Wk.S(0xE8344E), 0xFFFFFF, 8.5, 3, rim: 0x5A0A14);
            _dead.Margin = new Thickness(6, 0, 0, 0);
            var nameLine = WkCard.NameLine(_name.Root, _you, _cpBox, _dead);
            var nameCol = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { nameLine, _detail.Root } };
            _detail.Root.HorizontalAlignment = HorizontalAlignment.Left; _detail.Root.Margin = new Thickness(1, 2.5, 0, 0);   // expanded: the detail under the name
            Grid.SetColumn(nameCol, 2); line.Children.Add(nameCol);
            _fig.Align = HorizontalAlignment.Right; Grid.SetColumn(_fig.Root, 4); line.Children.Add(_fig.Root);
            _share.Align = HorizontalAlignment.Right; _share.Root.Margin = new Thickness(0, 3, 0, 0); Grid.SetColumn(_share.Root, 5); line.Children.Add(_share.Root);
            _root.Children.Add(line);
            var bar = new Grid { Height = BarH, VerticalAlignment = VerticalAlignment.Bottom };
            bar.Children.Add(_groove);
            _fill.Children.Add(_body);
            _fill.Children.Add(new Border { CornerRadius = new CornerRadius(2), Background = Foil(.16, 40) });
            _fill.Children.Add(new Border { CornerRadius = new CornerRadius(2), Background = Wk.Lin(0, 0, 0, 1, (0xFFFFFF, .4, 0), (0xFFFFFF, 0, .45), (Black, .3, 1)) });
            _fill.Children.Add(new Border { Width = 2, HorizontalAlignment = HorizontalAlignment.Right, Background = Wk.S(0xFFFFFF, .9) });
            _track.Children.Add(_fill);
            bar.Children.Add(_track);
            _bar = new Wk.BarFill(_track, _fill, 8);
            var healStamp = Stamp(out _healT); _heal = new Border { Child = healStamp, Margin = new Thickness(0, 0, 3, 0) };
            var topStamp = Stamp(out _topT); _top = new Border { Child = topStamp };
            bar.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 3, 0), Children = { _heal, _top } });
            _root.Children.Add(bar);
            _root.Children.Add(new Border { Height = 1, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(20, 0, 20, -4.5), Background = Wk.Lin(0, 0, 1, 0, (0xFFFFFF, 0, 0), (0xFFFFFF, .07, .5), (0xFFFFFF, 0, 1)), IsHitTestVisible = false });
        }

        public void Set(MeterMember m, MeterContext c, bool fresh)
        {
            uint v = Wk.Vivid(Wk.Hex(m.ClassColor));
            _orbFill.Fill = Wk.Rad(.4, .32, .75, (Wk.Shade(v, .5), 1, 0), (v, 1, .5), (Wk.Shade(v, -.5), 1, 1));
            _orbFill.Stroke = Wk.S(Wk.Shade(v, -.6));
            _rank.Text = m.Rank.ToString(CultureInfo.InvariantCulture);
            if (_cls != m.Class) { _cls = m.Class; _icon.Child = ThemeText.Icon(m.Class, 15, m.Name); }
            _name.Text = m.Name; _name.Fg = m.You ? Wk.S(0xFFE08A) : White;
            _you.Visibility = Shown(m.You);
            _cp.Text = m.Cp; _cpBox.Visibility = Shown(m.Cp.Length > 0 && !m.CpOnly);
            _dead.Visibility = Shown(m.Deaths > 0); _deadT.Text = Deaths(m);
            Detail(_detail, m, c);
            if (_cpOnly != m.CpOnly) { _cpOnly = m.CpOnly; _figCol.Width = new GridLength(FigRoom(m.CpOnly, 17.5, FontWeights.Bold, 8)); _shareCol.Width = new GridLength(ShareRoom(m.CpOnly, 10, FontWeights.SemiBold, 6)); }
            _fig.Text = m.Figure; _share.Text = m.ShareText;
            _groove.Background = Wk.Lin(0, 0, 0, 1, (0x03050C, .95, 0), (0x101632, .95, 1));
            _groove.BorderBrush = m.You ? Wk.Lin(0, 0, 0, 1, (0xFFF0B0, 1, 0), (0xE8B53A, 1, 1)) : Wk.S(0xFFFFFF, .14);
            _body.Background = Wk.Lin(0, 0, 1, 0, (Wk.Shade(v, -.35), 1, 0), (Wk.Shade(v, -.05), 1, .7), (Wk.Shade(v, .1), 1, 1));
            _healT.Text = m.Heal.Length > 0 ? "✚ " + m.Heal : ""; _heal.Visibility = Shown(m.Heal.Length > 0);
            _top.Visibility = Shown(m.TopDps || m.TopHeals); _topT.Text = TopWord(m);
            _fill.Visibility = m.CpOnly ? Visibility.Hidden : Visibility.Visible;
            _bar.Set(Wk.Bar(m, c), c, "card", fresh);
        }
    }
}
