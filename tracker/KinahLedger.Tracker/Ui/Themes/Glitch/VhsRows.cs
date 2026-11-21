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
/// The tape looks' meter rows (Ui/Themes/Glitch/HudVhs.cs), Viewfinder and Deck (Steven, 2026-11-17: "VHS concept is cool as
/// well but not legible enough"; he picked these two of three concepts: "Build viewfinder and b deck"). Both make the bar each
/// row's hero, thick and in the class's colour across the row (his "prioritizes the bar ... doesn't crowd everything"), with one
/// big DPS figure; every name and figure in Inter (the app's own face), crisp, the tape's effects kept to the frame and ground.
/// The share small at the bar's end; CP small and quiet after the name, the first thing to go to the hover card when room is
/// short (the whole name before it); a healer's figure only on healers; TOP DPS and TOP HEAL small tags; an expanded meter's
/// damage and crit before the share.
/// <list type="bullet">
/// <item>Viewfinder (<see cref="ViewRow"/>): the name line (rank, name, CP, marks) with the DPS big at its end; under it the bar
/// the row's whole width, lit from above in the class's colour with a soft halo, a bright tip.</item>
/// <item>Deck (<see cref="DeckRow"/>): the row is the bar: a deep track the row's width, the class's colour taken down enough
/// for white words to read on it; the rank in a chip, the name, CP and marks on it, the share at its right; the DPS in a black
/// counter window at the row's end (ringed yellow on the top DPS).</item>
/// </list>
/// </summary>
static class VhsRows
{
    public static readonly FontFamily Sans = HudKit.Text, Osd = VhsFace;
    const uint RedC = 0xFF3B30, YellowC = 0xFFD23F, GreenC = 0x45FF86, MintC = 0x5CFFB0;
    public const uint VfdC = 0x7CF3FF;
    static readonly Brush White = Br(0xFFFFFF), Dim = Br(0xFFFFFF, .66), Faint = Br(0xFFFFFF, .42), Black = Br(0x000000),
        Red = Br(RedC), Yellow = Br(YellowC), Green = Br(GreenC);

    public static Color Lift(Color c, int by) => Color.FromRgb((byte)Math.Min(255, c.R + by), (byte)Math.Min(255, c.G + by), (byte)Math.Min(255, c.B + by));
    public static Color Mix(Color c, Color to, double t) => Color.FromRgb((byte)(c.R + (to.R - c.R) * t), (byte)(c.G + (to.G - c.G) * t), (byte)(c.B + (to.B - c.B) * t));
    public static Brush Vertical(params (Color C, double At)[] stops)
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

    /// <summary>A row's pitch, and the room for the line before you pinned below: Viewfinder's and Deck's.</summary>
    public const double ViewPitch = 32, DeckPitch = 30, ViewGap = 14, DeckGap = 16;
    /// <summary>Viewfinder's pitch before the first hit: one line a row (the CP after the figure, no bar).</summary>
    public const double ViewReadyPitch = 24;

    /// <summary>"· · ·  2 MORE  · · ·": the party's members left out between it and you.</summary>
    public static FrameworkElement Gap(int more)
    {
        var t = Txt(more > 0 ? "· · ·   " + L.F("{0} more", more).ToUpper(CultureInfo.CurrentCulture) + "   · · ·" : "· · · · · ·", 9.5, Faint, Osd);
        t.HorizontalAlignment = HorizontalAlignment.Center;
        return t;
    }

    /// <summary>A small mark: words on a block of colour, in Inter ("TOP DPS", "✕2", "AVG").</summary>
    public static Border Mark(string text, Brush bg, Brush fg, double size, Thickness margin) => new()
    {
        Background = bg, Padding = new Thickness(4, 0, 4, 0), Margin = margin, VerticalAlignment = VerticalAlignment.Center, CornerRadius = new CornerRadius(1.5),
        Child = new TextBlock { Text = text, FontFamily = Sans, FontSize = size, FontWeight = FontWeights.Bold, Foreground = fg, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 0.5) },
    };

    /// <summary>A bar: a track, the fill a share of its width (kept on the track's size; it slides while the look moves).</summary>
    public sealed class Bar
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
        /// <summary>The share shown: slid from the last one under <paramref name="key"/> (at once in a render or a first draw).</summary>
        public void Slide(string key, double share) => Motion.Slide(Root, key, Math.Clamp(double.IsFinite(share) ? share : 0, 0, 1), v => Share = v);
    }

    /// <summary>A row of either tape: its parts kept, set again each draw; a kill's glint over it.</summary>
    public abstract class TapeRow : IThemeRow, IThemeBarSpan
    {
        protected readonly Grid Box = new();
        protected readonly Border GlintLayer = new() { Background = Br(0xFFFFFF), Opacity = 0, IsHitTestVisible = false, Margin = new Thickness(-4, -2, -4, -2) };
        protected readonly Ellipse Dot = new() { Width = 7, Height = 7, Fill = Red, Margin = new Thickness(0, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center };
        protected readonly TextBlock Name = Txt("", 12.5, White, Sans, FontWeights.Bold, trim: true), Cp = Txt("", 9.5, Br(0xFFFFFF, .62), Sans, FontWeights.SemiBold);
        protected readonly StackPanel Tags = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        protected readonly Border HealBox = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0), CornerRadius = new CornerRadius(1.5) };
        protected readonly TextBlock Heal = Txt("", 10.5, Green, Sans);
        protected readonly TextBlock Small = Txt("", 9.5, Br(0xFFFFFF, .74), Sans, FontWeights.SemiBold);
        protected readonly Bar Level, HealLevel = new(2);
        Color _cc = Colors.Transparent;

        public FrameworkElement Root => Box;

        /// <summary>The level bar's track: its fill is the track's width (less the fill's margins) * share and its full height (Bar.Apply).</summary>
        public (FrameworkElement On, Rect Track)? BarTrack
        {
            get
            {
                var r = Level.Root;
                if (r.ActualWidth <= 0) return null;
                var mg = Level.Fill.Margin;
                return (r, new Rect(mg.Left, 0, Math.Max(0, r.ActualWidth - mg.Left - mg.Right), r.ActualHeight));
            }
        }

        protected TapeRow(double barH)
        {
            Level = new Bar(barH);
            Cp.Margin = new Thickness(7, 1, 0, 0);
            HealBox.Child = Heal;
            HealLevel.Fill.Children.Add(new Border { Background = Green });
            Small.TextAlignment = TextAlignment.Right; Small.HorizontalAlignment = HorizontalAlignment.Right; Small.VerticalAlignment = VerticalAlignment.Center;
        }

        /// <summary>The name line: you're red dot, the name (the whole of it before the CP: short of room the CP goes to the hover card first), CP, marks, a healer's figure.</summary>
        protected GlitchNameLine NameLine() => new()
        {
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Text = Name, Yields = Cp, NameMin = 10_000,
            Children = { Dot, Name, Cp, Tags, HealBox },
        };

        /// <summary>The bar's fill in the class's colour (its parts made again only when the class changes).</summary>
        protected abstract void Paint(Color cc);

        public virtual void Set(MeterMember m, MeterContext c, bool fresh)
        {
            Dot.Visibility = m.You ? Visibility.Visible : Visibility.Collapsed;
            Name.Text = Caps(m.Name);
            Name.Foreground = m.You ? Br(0xFFF1C2) : White;
            Cp.Inlines.Clear();
            if (m.Cp.Length > 0) { if (!m.Pvp) Cp.Inlines.Add(Rn(L.T("CP") + " ", Br(0xFFFFFF, .48), 8.5)); Cp.Inlines.Add(Rn(m.Cp, Cp.Foreground)); }
            Cp.Visibility = m.Cp.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            // the top healer's figure in the green tag (TOP HEAL said by it); any other healer's green after the marks
            bool healTag = m.TopHeals && m.Heal.Length > 0;
            Heal.Text = m.Heal.Length > 0 ? "+" + m.Heal : "";
            Heal.Foreground = healTag ? Black : HealInk;
            HealBox.Background = healTag ? Green : null;
            HealBox.Padding = healTag ? new Thickness(4, 0, 4, 0) : new Thickness(0);
            HealBox.Visibility = m.Heal.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            Tags.Children.Clear();
            var tm = new Thickness(5, 0, 0, 0);
            if (m.Deaths > 0) Tags.Children.Add(Mark("✕" + (m.Deaths > 1 ? m.Deaths.ToString(CultureInfo.InvariantCulture) : ""), Br(0xE5322A), White, 9, tm));
            if (m.TopDps) Tags.Children.Add(Mark(L.T("TOP DPS"), Yellow, Black, 8.5, tm));
            if (m.TopHeals && !healTag) Tags.Children.Add(Mark(L.T("TOP HEAL"), Green, Black, 8.5, tm));
            if (m.ClassColor != _cc) { _cc = m.ClassColor; Paint(_cc); }
            Level.Root.Visibility = m.CpOnly ? Visibility.Hidden : Visibility.Visible;
            Level.Slide(c.Key + "|bar", BarOf(m, c));
            HealLevel.Root.Visibility = m.HealShare > 0 ? Visibility.Visible : Visibility.Collapsed;
            HealLevel.Slide(c.Key + "|heal", m.HealShare);
            // the small figure at the bar's end: the share; expanded, the damage and crit before it
            bool wide = c.Expanded && c.E.HasDamage;
            Small.Text = wide && m.Damage.Length > 0 ? m.Damage + (m.Crit.Length > 0 ? "  " + m.Crit : "") + "  " + m.ShareText : m.ShareText;
            Small.MinWidth = Math.Ceiling(TextW(wide ? "888.88M  88%  100.0%" : "100.0%", Sans, 9.5, FontWeights.SemiBold) + 2);
        }

        protected virtual Brush HealInk => Green;

        /// <summary>A kill's glint on this member's row: three white flashes, the last fading.</summary>
        public void Glint(double ms) => Motion.Play(GlintLayer, ms, t =>
        {
            int beat = (int)(t * 8);
            GlintLayer.Opacity = t < .45 ? (beat % 2 == 0 ? .5 : 0) : t < .6 ? .5 * (1 - (t - .45) / .15) : 0;
        }, () => GlintLayer.Opacity = 0);
    }

    /// <summary>Viewfinder's row: the name line with the DPS big at its end, the bar under it the row's whole width (2026-11-20: slimmer, 29 of a 32 pitch).</summary>
    public sealed class ViewRow : TapeRow
    {
        const double Line1 = 18, BarH = 9;
        readonly Border _band = new() { Margin = new Thickness(-7, -3, -7, -3), Visibility = Visibility.Collapsed, CornerRadius = new CornerRadius(2),
            Background = Horizontal((Rgb(0xFFFFFF, .13), 0), (Rgb(0xFFFFFF, .04), 1)) };
        readonly Border _rankBox = new() { MinWidth = 21, Height = 15, Padding = new Thickness(2, 0, 2, 0), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 7, 0),
            CornerRadius = new CornerRadius(1.5), BorderThickness = new Thickness(1) };
        readonly TextBlock _rank = Txt("", 10, White, Osd);
        readonly TextBlock _fig = FigCell(18, White, Sans, 0, FontWeights.Bold);

        public ViewRow() : base(BarH)
        {
            Box.Height = Line1 + 2 + BarH;
            Box.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Box.ColumnDefinitions.Add(new ColumnDefinition());
            Box.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Box.RowDefinitions.Add(new RowDefinition { Height = new GridLength(Line1) });
            Box.RowDefinitions.Add(new RowDefinition { Height = new GridLength(2) });
            Box.RowDefinitions.Add(new RowDefinition { Height = new GridLength(BarH) });
            Grid.SetColumnSpan(_band, 3); Grid.SetRowSpan(_band, 3); Box.Children.Add(_band);
            _rank.HorizontalAlignment = HorizontalAlignment.Center; _rankBox.Child = _rank;
            Box.Children.Add(_rankBox);
            var line = NameLine();
            Grid.SetColumn(line, 1); Box.Children.Add(line);
            // the figure's line box overhangs the name's line a little (never laid out cut)
            _fig.Margin = new Thickness(10, -3, 0, -3); _fig.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(_fig, 2); Box.Children.Add(_fig);
            // the bar's line: the bar (a fine scan over it, a healer's green trace under it) and the small figure at its end
            var bl = new Grid();
            bl.ColumnDefinitions.Add(new ColumnDefinition()); bl.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var level = new Grid();
            Level.Track.Background = Br(0xFFFFFF, .09); Level.Track.CornerRadius = new CornerRadius(1);
            level.Children.Add(Level.Root);
            level.Children.Add(new Rectangle { Fill = Lines(Rgb(0x000000, .16), 3, 1), IsHitTestVisible = false });
            HealLevel.Root.VerticalAlignment = VerticalAlignment.Bottom; HealLevel.Root.Margin = new Thickness(0, 0, 0, -3);
            level.Children.Add(HealLevel.Root);
            bl.Children.Add(level);
            Small.Margin = new Thickness(8, 0, 0, 0);
            Grid.SetColumn(Small, 1); bl.Children.Add(Small);
            Grid.SetRow(bl, 2); Grid.SetColumnSpan(bl, 3); Box.Children.Add(bl);
            Grid.SetColumnSpan(GlintLayer, 3); Grid.SetRowSpan(GlintLayer, 3); Box.Children.Add(GlintLayer);
        }

        protected override void Paint(Color cc)
        {
            // the class's colour lit from above, a hot line along its top, a bright tip; a halo of its colour round it (no blur: two soft rims)
            Level.Fill.Children.Clear();
            Level.Fill.Children.Add(new Border { Margin = new Thickness(-3), CornerRadius = new CornerRadius(4), Background = Frozen(Color.FromArgb(34, cc.R, cc.G, cc.B)) });
            Level.Fill.Children.Add(new Border { Margin = new Thickness(-1.5), CornerRadius = new CornerRadius(2.5), Background = Frozen(Color.FromArgb(60, cc.R, cc.G, cc.B)) });
            Level.Fill.Children.Add(new Border { Background = Vertical((Lift(cc, 45), 0), (cc, .45), (Mix(cc, Colors.Black, .28), 1)), CornerRadius = new CornerRadius(1) });
            Level.Fill.Children.Add(new Border { Height = 1, VerticalAlignment = VerticalAlignment.Top, Background = Br(0xFFFFFF, .55), Margin = new Thickness(1, 0, 1, 0) });
            Level.Fill.Children.Add(new Border { Width = 2, HorizontalAlignment = HorizontalAlignment.Right, Background = Br(0xFFFFFF, .9) });
        }

        public override void Set(MeterMember m, MeterContext c, bool fresh)
        {
            base.Set(m, c, fresh);
            bool you = m.You;
            _rank.Text = m.Rank.ToString("00", CultureInfo.InvariantCulture);
            _rankBox.Background = you ? White : Br(0xFFFFFF, .1);
            _rankBox.BorderBrush = you ? White : Br(0xFFFFFF, .4);
            _rank.Foreground = you ? Black : White;
            _band.Visibility = you ? Visibility.Visible : Visibility.Collapsed;
            // before any damage the figure is the CP: one line, "CP" small after it, the bar's line folded away
            bool cpOnly = m.CpOnly;
            _fig.Inlines.Clear();
            _fig.Inlines.Add(Rn(m.Figure, cpOnly ? Dim : White));
            if (cpOnly) _fig.Inlines.Add(Rn(" " + L.T("CP"), Br(0xFFFFFF, .48), 9.5, FontWeights.SemiBold));
            Box.RowDefinitions[1].Height = new GridLength(cpOnly ? 0 : 2);
            Box.RowDefinitions[2].Height = new GridLength(cpOnly ? 0 : BarH);
            Box.Height = cpOnly ? Line1 : Line1 + 2 + BarH;
            Small.Visibility = cpOnly ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    /// <summary>Deck's row: the bar the row's width with the rank, name and share on it, the DPS in a counter window at its end.</summary>
    public sealed class DeckRow : TapeRow
    {
        const double H = 25;
        readonly Border _chip = new() { MinWidth = 19, Height = 17, Padding = new Thickness(3, 0, 3, 0), CornerRadius = new CornerRadius(2), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(3, 0, 6, 0) };
        readonly TextBlock _rank = Txt("", 11, White, Sans);
        readonly Border _counter = new() { CornerRadius = new CornerRadius(2), BorderThickness = new Thickness(1), Margin = new Thickness(5, 0, 0, 0),
            Background = Vertical((Rgb(0x000000, .92), 0), (Rgb(0x0A0C10, .92), 1)) };
        readonly TextBlock _fig = FigCell(16, White, Sans, Math.Ceiling(TextW("199.9K", Sans, 16, FontWeights.Bold)), FontWeights.Bold);
        static readonly Brush DeckHeal = Br(0x7DFFAA);

        public DeckRow() : base(H)
        {
            Heal.FontSize = 10;
            Cp.Foreground = Br(0xFFFFFF, .82);
            Small.Foreground = Br(0xFFFFFF, .85);
            Box.Height = H;
            Box.ColumnDefinitions.Add(new ColumnDefinition());
            Box.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Level.Track.Background = Vertical((Rgb(0x000000, .62), 0), (Rgb(0x000000, .5), 1));
            Level.Track.BorderBrush = Br(0xFFFFFF, .09); Level.Track.BorderThickness = new Thickness(1); Level.Track.CornerRadius = new CornerRadius(2);
            Box.Children.Add(Level.Root);
            HealLevel.Root.Height = 3; HealLevel.Root.VerticalAlignment = VerticalAlignment.Bottom; HealLevel.Root.Margin = new Thickness(1, 0, 1, 1);
            Box.Children.Add(HealLevel.Root);
            // what reads on the bar: the rank's chip, the name line, the share at the right
            var on = new Grid();
            on.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            on.ColumnDefinitions.Add(new ColumnDefinition());
            on.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _rank.HorizontalAlignment = HorizontalAlignment.Center; _chip.Child = _rank;
            on.Children.Add(_chip);
            var line = NameLine();
            Grid.SetColumn(line, 1); on.Children.Add(line);
            // the share on a dark plate, so the fill's end never cuts through its digits
            var plate = new Border { Background = Br(0x000000, .45), CornerRadius = new CornerRadius(2), Padding = new Thickness(3, 0, 3, 0), Margin = new Thickness(4, 0, 4, 0),
                VerticalAlignment = VerticalAlignment.Center, Child = Small };
            Grid.SetColumn(plate, 2); on.Children.Add(plate);
            Box.Children.Add(on);
            _fig.Margin = new Thickness(7, 0, 6, 0); _fig.VerticalAlignment = VerticalAlignment.Center;
            _counter.Child = _fig;
            Grid.SetColumn(_counter, 1); Box.Children.Add(_counter);
            Grid.SetColumnSpan(GlintLayer, 2); Box.Children.Add(GlintLayer);
        }

        protected override Brush HealInk => DeckHeal;

        protected override void Paint(Color cc)
        {
            // the class's colour taken down so white words read on it, a lit top edge
            Level.Fill.Children.Clear();
            Level.Fill.Children.Add(new Border { CornerRadius = new CornerRadius(2), Background = Vertical((Mix(cc, Colors.Black, .12), 0), (Mix(cc, Colors.Black, .38), .55), (Mix(cc, Colors.Black, .55), 1)) });
            Level.Fill.Children.Add(new Border { Height = 2, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(1, 1, 1, 0), Background = Frozen(Lift(cc, 60)) });
        }

        public override void Set(MeterMember m, MeterContext c, bool fresh)
        {
            base.Set(m, c, fresh);
            bool you = m.You;
            _rank.Text = m.Rank.ToString(CultureInfo.InvariantCulture);
            _chip.Background = you ? White : Br(0x000000, .78);
            _chip.BorderBrush = you ? null : Br(0xFFFFFF, .25); _chip.BorderThickness = new Thickness(you ? 0 : 1);
            _rank.Foreground = you ? Black : White;
            _fig.Text = m.Figure;
            _fig.Foreground = m.CpOnly ? Dim : White;
            _counter.BorderBrush = m.TopDps ? Br(YellowC, .85) : you ? Br(0xFFFFFF, .5) : Br(0xFFFFFF, .16);
        }
    }

    /// <summary>The foot: a rule, "TOTAL 766.26M" and "GROUP 999K" (alone: your best or peak), AVG stamped once the fight is held.</summary>
    public static FrameworkElement Foot(Encounter e, DateTime now, bool deck)
    {
        Brush label = deck ? Br(VfdC, .7) : Faint, value = White, tag = deck ? Br(VfdC) : Yellow, rule = Br(0xFFFFFF, deck ? .14 : .22);
        var ff = FootOf(e, now);
        var sp = new StackPanel { Margin = new Thickness(0, deck ? 9 : 6, 0, 0) };
        sp.Children.Add(new Rectangle { Height = 1, Fill = rule, Margin = new Thickness(0, 0, 0, deck ? 7 : 5) });
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = Txt("", 14, value, Sans);
        left.Inlines.Add(Rn(L.T("Total ").Trim().ToUpper(CultureInfo.CurrentCulture) + "  ", label, 9.5, FontWeights.SemiBold));
        left.Inlines.Add(Rn(ff.Total, value));
        EnergyMark.After(left, 12, label, Sans);   // Odyle Energy (Ui/EnergyMark.cs)
        g.Children.Add(left);
        var right = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 0, 0, 0) };
        if (ff.Label.Length > 0)
        {
            var r = Txt("", 14, value, Sans);
            r.Inlines.Add(Rn(ff.Label.ToUpper(CultureInfo.CurrentCulture) + "  ", label, 9.5, FontWeights.SemiBold));
            r.Inlines.Add(Rn(ff.Dps, value));
            right.Children.Add(r);
        }
        if (ff.Held) right.Children.Add(Mark(L.T("AVG"), tag, Black, 8.5, new Thickness(6, 0, 0, 0)));
        Grid.SetColumn(right, 1); g.Children.Add(right);
        sp.Children.Add(g);
        TagFoot(sp, e, now);
        return sp;
    }
}
