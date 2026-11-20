using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Effects;
using KinahLedger.Parser;

namespace KinahLedger.Ui;

/// <summary>
/// The Parser panel's own rows in its three looks (Steven, 2026-11-17, of the Standard concepts: "Forge, Podium and Reactor
/// are fantastic too, build them"). The rows move as every look's do (ParserRows: the bar slides, a row glides to its rank,
/// the chip flips, the big hits pop, the finale and the fire), drawn in the skin's own parts:
/// <list type="bullet">
/// <item>Forge (the default, the "standard" key): every row one thick bar lit from above (a bevel's highlight along its top,
/// its colour deepening down, a hot edge at its head where it ends clear of the DPS); on it the rank, the class's mark, the name
/// over its Combat Power (and a healer's healing), the DPS large at the right; the share small after the bar.</item>
/// <item>Podium: every row one line (the name, its Combat Power quiet after it, the figures) over a long glowing rail the
/// row's width; the leader on a card lit in their class's colour, their rank in gold, as tall as any row.</item>
/// <item>Reactor: an instrument panel. The rank on a solid chip in the class's colour, a recessed track lit cell by cell,
/// brightest at its head, the name over its Combat Power on a dark scrim; the DPS in an inset readout with the share under it.</item>
/// </list>
/// </summary>
public sealed partial class ParserRows
{
    /// <summary>The rows' look: Classic (every fight look's rows) or one of the Parser panel's own three.</summary>
    public enum Skin { Classic, Forge, Podium, Reactor }
    readonly Skin _skin;
    /// <summary>Whether the rows rank by damage now (Podium's medal ranks only then; before a hit they rank by Combat Power).</summary>
    bool _medals;
    /// <summary>Podium's top three: the rank in gold, silver and bronze (the podium the look is named for).</summary>
    static readonly Color Medal1 = Color.FromRgb(0xFF, 0xD3, 0x5C), Medal2 = Color.FromRgb(0xD6, 0xDD, 0xEA), Medal3 = Color.FromRgb(0xE3, 0x9B, 0x62);
    public Skin RowSkin => _skin;

    /// <summary>A row's pitch (its height and the space under it), and its height.</summary>
    /// <summary>
    /// (Podium, 2026-11-20, "bulky as f***": one line over a rail a few px under it, the leader's card as tall as any row; was 41 / 36,
    /// the card 62 tall)
    /// </summary>
    static double PitchOf(Skin s) => s switch { Skin.Forge => 38, Skin.Podium => 35, Skin.Reactor => 36, _ => 30 };
    static double RowHOf(Skin s, bool hero) => s switch { Skin.Forge => 34, Skin.Podium => 33, Skin.Reactor => 32, _ => 28 };
    /// <summary>Whether row <paramref name="i"/> stands on Podium's leader card (the top row, once there is damage).</summary>
    bool HeroAt(Encounter e, int i) => _skin == Skin.Podium && i == 0 && e.HasDamage;

    static readonly Color Ink = Color.FromRgb(0xF4, 0xF5, 0xF9), GoldHi = Color.FromRgb(0xFF, 0xD9, 0x7A), Panel0 = Color.FromRgb(0x0B, 0x0C, 0x10), MintHi = Color.FromRgb(0x8C, 0xF5, 0xC0);
    static Color Lit(Color c) => Mix(c, Colors.White, 0.28);
    static Color Deep(Color c) => Mix(c, Panel0, 0.45);
    static Color A(Color c, double a) => Color.FromArgb((byte)Math.Round(255 * Math.Clamp(a, 0, 1)), c.R, c.G, c.B);
    static LinearGradientBrush Across(params (Color C, double At)[] stops) => Grad(new Point(0, 0), new Point(1, 0), stops);
    static LinearGradientBrush Down(params (Color C, double At)[] stops) => Grad(new Point(0, 0), new Point(0, 1), stops);
    static LinearGradientBrush Grad(Point a, Point b, (Color C, double At)[] stops)
    {
        var g = new LinearGradientBrush { StartPoint = a, EndPoint = b };
        foreach (var (c, at) in stops) g.GradientStops.Add(new GradientStop(c, at));
        g.Freeze();
        return g;
    }
    /// <summary>Words lifted off whatever is under them: a tight dark shadow straight down.</summary>
    static DropShadowEffect Lift(double blur = 4, double opacity = 0.9) => new() { Color = Colors.Black, BlurRadius = blur, ShadowDepth = 1, Direction = 270, Opacity = opacity };

    /// <summary>Reactor's cells: a tile <paramref name="cell"/> px wide and a 1 px gap, as an opacity mask.</summary>
    static DrawingBrush Cells(double cell)
    {
        var d = new GeometryDrawing(Brushes.White, null, new RectangleGeometry(new Rect(0, 0, cell, 1)));
        var b = new DrawingBrush(d) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, cell + 1, 1), ViewportUnits = BrushMappingMode.Absolute, Viewbox = new Rect(0, 0, cell + 1, 1), ViewboxUnits = BrushMappingMode.Absolute, Stretch = Stretch.None };
        b.Freeze();
        return b;
    }

    /// <summary>A member's row in the panel's skin: the same parts as a Classic row (RowEl), laid out and lit the skin's way.</summary>
    RowEl BuildSkin(Fighter f, bool expanded, bool dmgCol, bool crit, bool back, bool spark, bool hero)
    {
        var c = ClassArt.ColorOf(f.Class);
        double rowH = RowHOf(_skin, hero);
        bool forge = _skin == Skin.Forge, podium = _skin == Skin.Podium, reactor = _skin == Skin.Reactor;

        // ---- the parts every skin has (as a Classic row's: the tests and the motion find them the same way)
        var chipText = new TextBlock { FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var chip = new Border
        {
            VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left, Child = chipText,
            RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new TransformGroup { Children = { new SkewTransform(0, 0), new ScaleTransform(1, 1) } },
        };
        var tick = new Border { Width = 15, Height = 15, CornerRadius = new CornerRadius(2), Background = PK.C(PK.Coral), Margin = new Thickness(0, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed,
            RenderTransformOrigin = new Point(0.5, 0.5), Effect = new DropShadowEffect { Color = PK.Coral, BlurRadius = 9, ShadowDepth = 0, Opacity = 0.85 },
            Child = new TextBlock { Text = "✕", FontSize = 8.5, FontWeight = FontWeights.Bold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
        double nameSize = podium ? (hero ? 14 : 13) : 13.5;
        var name = new TextBlock { Text = NameOf(f), FontSize = nameSize, FontWeight = f.You ? FontWeights.Bold : FontWeights.SemiBold, Foreground = f.Name.Length > 0 ? PK.C(f.You && !forge ? GoldHi : Ink) : HudKit.Muted, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        if (!podium) name.Effect = Lift(4, reactor ? 1 : 0.9);
        var nm = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        nm.Children.Add(tick);
        nm.Children.Add(name);
        // Combat Power: under the name (Forge, Reactor), after it (Podium); "CP" small before it, there only with it
        var cp = new TextBlock { FontSize = podium ? 10 : 9.5, FontWeight = FontWeights.SemiBold, Foreground = PK.C(podium ? Color.FromRgb(0x9A, 0xA0, 0xB4) : Ink, podium ? 1 : 0.72), VerticalAlignment = VerticalAlignment.Center, Tag = "cp", Visibility = Visibility.Collapsed, Margin = new Thickness(3, 0, 0, 0) };
        System.Windows.Documents.Typography.SetNumeralAlignment(cp, FontNumeralAlignment.Tabular);
        var cpWord = new TextBlock { Text = L.T("CP"), FontSize = podium ? 8 : 8, FontWeight = FontWeights.Bold, Foreground = PK.C(podium ? Color.FromRgb(0x6A, 0x70, 0x84) : Ink, podium ? 1 : 0.5), VerticalAlignment = VerticalAlignment.Center };
        cpWord.SetBinding(VisibilityProperty, new Binding(nameof(Visibility)) { Source = cp });
        var cpPair = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Children = { cpWord, cp } };
        if (!podium) { cp.Effect = Lift(2, 0.8); cpWord.Effect = Lift(2, 0.8); }
        Border? you = null;
        if (f.You && !forge && !reactor) { you = HudGlass.YouChip(); if (you.Child is TextBlock yt) yt.Text = L.T(yt.Text); you.Tag = "you"; you.Margin = new Thickness(6, 0, 0, 0); }
        // the heal chip (a healer's row): a green cross and the healing a second
        var heal = new TextBlock { FontSize = podium ? 10.5 : 9.5, FontWeight = FontWeights.Bold, Foreground = PK.C(podium ? PK.Mint : MintHi), Tag = "heal-figures", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 0, 0) };
        if (!podium) heal.Effect = Lift(2, 0.9);
        System.Windows.Documents.Typography.SetNumeralAlignment(heal, FontNumeralAlignment.Tabular);
        var cross = new Border { Tag = "heal-label", Width = 12, Height = 12, CornerRadius = new CornerRadius(3), VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = "✚", FontSize = 9.5, FontWeight = FontWeights.Bold, Foreground = HudKit.B(0x3DD68C), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
        var healPill = new Border { Tag = "heal-pill", VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed, Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { cross, heal } } };
        var pb = new Border { CornerRadius = new CornerRadius(3), Padding = new Thickness(5, 0, 5, 1), Margin = new Thickness(6, 0, 0, 0), Background = HudKit.Gold, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed,
            RenderTransformOrigin = new Point(0, 0.5), Effect = new DropShadowEffect { Color = PK.Gold, BlurRadius = 12, ShadowDepth = 0, Opacity = 0.9 },
            Child = new TextBlock { FontSize = 9.5, FontWeight = FontWeights.Bold, Foreground = HudKit.B(0x1D1300) } };
        var plates = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
        TextBlock Fig(double size, Brush b, FontWeight w) { var t = new TextBlock { FontSize = size, Foreground = b, FontWeight = w, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center }; System.Windows.Documents.Typography.SetNumeralAlignment(t, FontNumeralAlignment.Tabular); return t; }
        double dpsSize = forge ? 17 : 15;
        var dps = Fig(dpsSize, PK.C(Ink), FontWeights.Bold); dps.Tag = "row-dps";
        if (forge) dps.Effect = Lift(4, 0.95);
        var share = Fig(reactor ? 8.5 : forge ? 10.5 : 10, PK.C(Ink, reactor ? 0.55 : 0.6), FontWeights.SemiBold);
        var damage = Fig(11, HudKit.B(0xC9CDDA), FontWeights.SemiBold);
        var critT = Fig(11, HudKit.B(0xC9CDDA), FontWeights.SemiBold); critT.ToolTip = L.T("Crit rate");
        var backT = Fig(11, HudKit.B(0x9FA6B8), FontWeights.Medium); backT.ToolTip = L.T("Hits from behind");
        var sparkG = new Grid { Width = 60, Height = 18, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        var odo = new Odometer(dpsSize, FontWeights.Bold, Ink) { Visibility = Visibility.Collapsed };
        var dpsHost = new Grid { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Children = { dps, odo } };
        var shareHost = new Grid { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Children = { share } };
        var ghost = new Border { HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(4), IsHitTestVisible = false, Visibility = Visibility.Collapsed };
        var heat = new Border { IsHitTestVisible = false, Visibility = Visibility.Collapsed, BorderThickness = new Thickness(0, 0, 0, 1.5), CornerRadius = new CornerRadius(4),
            Background = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0), GradientStops = { new GradientStop(Colors.Transparent, 0), new GradientStop(Colors.Transparent, 1) } }, BorderBrush = new SolidColorBrush(Colors.Transparent) };
        var healFade = Across((Color.FromArgb(0xB0, 0x2F, 0xC4, 0x7A), 0), (Color.FromRgb(0x7D, 0xF5, 0xB5), 1));
        var under = new Border { Height = HealBarPx, VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Left, IsHitTestVisible = false, Visibility = Visibility.Collapsed,
            Tag = "heal-line", Width = 0, CornerRadius = new CornerRadius(2), Background = healFade, Effect = new DropShadowEffect { Color = PK.Mint, BlurRadius = 5, ShadowDepth = 0, Opacity = 0.7 } };
        var underTrack = new Border { Height = HealBarPx, VerticalAlignment = VerticalAlignment.Bottom, IsHitTestVisible = false, Visibility = Visibility.Collapsed, Tag = "heal-track",
            CornerRadius = new CornerRadius(2), Background = new SolidColorBrush(Color.FromArgb(0x38, 0x2F, 0xC4, 0x7A)) };
        var edge = new Border { Width = 0, Visibility = Visibility.Collapsed };
        FrameworkElement? hot = null;

        var widths = new List<double>();
        void Extras() { if (expanded) { if (dmgCol) widths.Add(DamageCol); if (crit) widths.Add(RateCol); if (back) widths.Add(RateCol); if (spark) widths.Add(SparkCol); } }
        void AddExtras(Grid g, int k) { if (expanded) { if (dmgCol) g.Children.Add(PK.Col(damage, k++)); if (crit) g.Children.Add(PK.Col(critT, k++)); if (back) g.Children.Add(PK.Col(backT, k++)); if (spark) g.Children.Add(PK.Col(sparkG, k++)); } }
        double dpsCol = Math.Max(DpsCol, Widest(dpsSize, FontWeights.Bold, "888.8K", "8.88M") + 4);
        double shareCol = Math.Max(reactor ? 30 : 38, Widest(reactor ? 8.5 : 10.5, FontWeights.SemiBold, "88.8%", L.T("CP")) + 6);

        Action? rankInk = null;
        Grid cols, inner;
        Border track, fill;
        FrameworkElement nameHostChild;
        if (forge)
        {
            // the bar, the row's height up to the share's column; the share small after it
            track = new Border { CornerRadius = new CornerRadius(5), Background = Down((Color.FromArgb(0x70, 0x02, 0x03, 0x06), 0), (A(c, 0.10), 1)), BorderBrush = PK.C(Colors.White, 0.05), BorderThickness = new Thickness(0, 0, 0, 1) };
            var bar = new Grid();
            bar.Children.Add(new Border { CornerRadius = new CornerRadius(5), Background = Down((Color.FromArgb(0x42, 0xFF, 0xFF, 0xFF), 0), (Color.FromArgb(0x10, 0xFF, 0xFF, 0xFF), 0.42), (Color.FromArgb(0x00, 0, 0, 0), 0.55), (Color.FromArgb(0x55, 0, 0, 0), 1)) });
            bar.Children.Add(new Border { Height = 1, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(4, 0, 4, 0), Background = PK.C(Colors.White, 0.38) });
            var edgeHot = new Border { Width = 3, HorizontalAlignment = HorizontalAlignment.Right, CornerRadius = new CornerRadius(0, 5, 5, 0), Background = PK.C(Mix(c, Colors.White, 0.6)), Effect = new DropShadowEffect { Color = Lit(c), BlurRadius = 12, ShadowDepth = 0, Opacity = 0.9 }, Visibility = Visibility.Collapsed };
            bar.Children.Add(edgeHot);
            hot = edgeHot;
            fill = new Border { HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(5), Width = 0, Child = bar,
                Background = Across((A(Deep(c), 0.95), 0), (A(c, 0.92), 0.75), (A(Lit(c), 0.95), 1)) };
            widths.AddRange(new[] { 18.0, 26, -1, dpsCol });
            cols = PK.Cols(widths.ToArray());
            cols.Margin = new Thickness(7, 0, 9, 0);
            chipText.FontSize = 11.5; chipText.Foreground = PK.C(f.You ? GoldHi : Ink, f.You ? 1 : 0.62); chipText.Effect = Lift(3, 0.8);
            chip.Width = 16; chip.Height = 18;
            if (f.You) chip.Effect = new DropShadowEffect { Color = PK.Gold, BlurRadius = 10, ShadowDepth = 0, Opacity = 0.6 };
            cols.Children.Add(chip);
            var icon = ClassArt.Icon(f.Class, 20, f.Name); icon.Margin = new Thickness(0, 0, 6, 0);
            cols.Children.Add(PK.Col(icon, 1));
            // the name over its Combat Power and a healer's healing
            var sub = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, -1, 0, 0), Children = { cpPair } };
            healPill.Margin = new Thickness(8, 0, 0, 0);
            sub.Children.Add(healPill);
            nm.Children.Add(pb); nm.Children.Add(plates);
            nameHostChild = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { nm, sub } };
            cols.Children.Add(PK.Col(dpsHost, 2, 2));
            // after the bar, on the panel: an expanded meter's columns, then the share
            Extras();
            inner = new Grid { Height = rowH - (f.You ? 2 : 0), ClipToBounds = true };
            inner.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            foreach (var w in widths.Skip(4)) inner.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(w) });
            inner.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(shareCol + 8) });
            foreach (var el in new UIElement[] { track, fill, ghost, edge, heat, underTrack, under, cols }) inner.Children.Add(el);
            AddExtras(inner, 1);
            shareHost.Margin = new Thickness(0, 2, 0, 0);
            inner.Children.Add(PK.Col(shareHost, inner.ColumnDefinitions.Count - 1));
        }
        else if (podium)
        {
            // one line (rank, mark, the name, its Combat Power and marks, the DPS, the share), the rail under it; a framed row (the
            // leader's card, yours) is 2 px shorter inside, its rail 1 px higher
            bool framed = hero || f.You;
            double railH = 6, lineH = 20, railTop = framed ? lineH : lineH + 1;
            track = new Border { CornerRadius = new CornerRadius(railH / 2), Background = Down((Color.FromArgb(0x90, 0, 0, 0), 0), (A(c, 0.14), 1)) };
            var rail = new Grid();
            rail.Children.Add(new Border { CornerRadius = new CornerRadius(railH / 2), Background = Down((Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF), 0), (Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 0.5), (Color.FromArgb(0x40, 0, 0, 0), 1)) });
            fill = new Border { HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(railH / 2), Width = 0, Child = rail,
                Background = Across((A(Deep(c), 0.9), 0), (c, 0.7), (Lit(c), 1)), Effect = new DropShadowEffect { Color = c, BlurRadius = 10, ShadowDepth = 0, Opacity = 0.55 } };
            // the name line: rank, mark, the name and its marks, the DPS, the share
            widths.AddRange(new[] { 14.0, 23, -1, dpsCol, shareCol }); Extras();
            cols = PK.Cols(widths.ToArray());
            cols.Height = lineH; cols.VerticalAlignment = VerticalAlignment.Top;
            // the leader's rank in gold (the top step), as yours
            chipText.FontSize = 10.5; chipText.Foreground = PK.C(f.You || hero ? PK.Gold : Ink, f.You || hero ? 1 : 0.5); chipText.HorizontalAlignment = HorizontalAlignment.Left;
            chip.Width = 13; chip.Height = 16;
            // the top three's ranks larger, in gold, silver and bronze, the leader's lit (their colour follows the number as it flips)
            int inked = -1;
            void Ink3()
            {
                int n = _medals && int.TryParse(chipText.Text, out var k) && k is >= 1 and <= 3 ? k : 0;
                if (n == inked) return;
                inked = n;
                chipText.FontSize = n > 0 ? 12.5 : 10.5;
                chipText.FontWeight = n == 1 ? FontWeights.ExtraBold : FontWeights.Bold;
                chipText.Foreground = n switch { 1 => PK.C(Medal1), 2 => PK.C(Medal2), 3 => PK.C(Medal3), _ => PK.C(f.You || hero ? PK.Gold : Ink, f.You || hero ? 1 : 0.5) };
                chipText.Effect = n == 1 ? new DropShadowEffect { Color = PK.Gold, BlurRadius = 8, ShadowDepth = 0, Opacity = 0.75 } : n > 0 ? Lift(2, 0.8) : null;
            }
            rankInk = Ink3;
            cols.Children.Add(chip);
            var icon = ClassArt.Icon(f.Class, hero ? 18 : 17, f.Name); icon.Margin = new Thickness(0, 0, 6, 0);
            cols.Children.Add(PK.Col(icon, 1));
            // Combat Power quiet after the name (it gives way first when the line is short: FitName)
            cpPair.Margin = new Thickness(6, 1, 0, 0);
            cpPair.SetBinding(VisibilityProperty, new Binding(nameof(Visibility)) { Source = cp });   // gone with it, its margin too
            nm.Children.Add(cpPair);
            healPill.Margin = new Thickness(7, 1, 0, 0);
            if (you != null) nm.Children.Add(you);
            nm.Children.Add(healPill); nm.Children.Add(pb); nm.Children.Add(plates);
            nameHostChild = nm;
            dpsHost.Margin = new Thickness(8, 0, 0, 0);
            cols.Children.Add(PK.Col(dpsHost, 2, 2)); cols.Children.Add(PK.Col(shareHost, 4));
            shareHost.Margin = new Thickness(0, 1, 0, 0);
            AddExtras(cols, 5);
            // the rail the row's whole width, right under the line; the heal bar at the row's foot under it
            var barArea = new Grid { Height = railH, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, railTop, 0, 0) };
            foreach (var el in new UIElement[] { track, fill, ghost, edge }) barArea.Children.Add(el);
            inner = new Grid { Height = rowH - (framed ? 2 : 0) };
            foreach (var el in new UIElement[] { heat, barArea, underTrack, under, cols }) inner.Children.Add(el);
        }
        else
        {
            // the readout's width: the DPS's widest form and its frame
            double readW = dpsCol + 14;
            track = new Border { CornerRadius = new CornerRadius(3), Background = Down((Color.FromArgb(0xD0, 0x01, 0x01, 0x03), 0), (Color.FromArgb(0xB0, 0x0A, 0x0C, 0x10), 1)),
                BorderBrush = Down((Color.FromArgb(0x80, 0, 0, 0), 0), (Color.FromArgb(0x1C, 0xFF, 0xFF, 0xFF), 1)), BorderThickness = new Thickness(1) };
            var unlit = new Border { Margin = new Thickness(2), Background = PK.C(c, 0.10), OpacityMask = Cells(3), IsHitTestVisible = false };
            var lit = new Grid();
            lit.Children.Add(new Border { Background = Across((Deep(c), 0), (c, 0.65), (Mix(c, Colors.White, 0.5), 1)), OpacityMask = Cells(3) });
            lit.Children.Add(new Border { Background = Down((Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF), 0), (Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 0.5), (Color.FromArgb(0x30, 0, 0, 0), 1)), OpacityMask = Cells(3) });
            fill = new Border { HorizontalAlignment = HorizontalAlignment.Left, Width = 0, Margin = new Thickness(2), Child = lit, Effect = new DropShadowEffect { Color = c, BlurRadius = 9, ShadowDepth = 0, Opacity = 0.75 } };
            // a dark scrim under the name, so it reads on the brightest cells
            var scrim = new Border { Margin = new Thickness(3), CornerRadius = new CornerRadius(2), IsHitTestVisible = false,
                Background = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(185, 0), MappingMode = BrushMappingMode.Absolute,
                    GradientStops = { new GradientStop(Color.FromArgb(0xE6, 0x05, 0x06, 0x09), 0), new GradientStop(Color.FromArgb(0xC8, 0x05, 0x06, 0x09), 0.72), new GradientStop(Color.FromArgb(0x00, 0x05, 0x06, 0x09), 1) } } };
            widths.AddRange(new[] { 25.0, 33, -1, readW + 6 }); Extras();
            cols = PK.Cols(widths.ToArray());
            var barArea = new Grid { Margin = new Thickness(1, 0, 0, 0) };
            foreach (var el in new UIElement[] { track, unlit, fill, ghost, edge, scrim, heat, underTrack, under }) barArea.Children.Add(el);
            underTrack.Margin = under.Margin = new Thickness(2, 0, 2, 2);
            cols.Children.Add(PK.Col(barArea, 1, 2));
            // the rank on a solid chip in the class's colour (gold for you), lit from above
            var chipC = f.You ? PK.Gold : c;
            chip.Width = 22; chip.Height = rowH; chip.CornerRadius = new CornerRadius(5, 2, 2, 5);
            chip.Background = Down((Lit(chipC), 0), (chipC, 0.5), (Deep(chipC), 1)); chip.BorderBrush = PK.C(Colors.White, 0.25); chip.BorderThickness = new Thickness(0, 1, 0, 0);
            chipText.FontSize = 12; chipText.Foreground = PK.C(Color.FromRgb(0x10, 0x0E, 0x08));
            cols.Children.Add(chip);
            var icon = ClassArt.Icon(f.Class, 18, f.Name); icon.Margin = new Thickness(9, 0, 6, 0);
            cols.Children.Add(PK.Col(icon, 1));
            var sub = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 0), Children = { cpPair } };
            healPill.Margin = new Thickness(7, 0, 0, 0);
            sub.Children.Add(healPill);
            nm.Children.Add(pb); nm.Children.Add(plates);
            nameHostChild = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { nm, sub } };
            // the readout: the DPS, the share under it, sunk into the panel
            var fig = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { dpsHost, shareHost } };
            dpsHost.HorizontalAlignment = HorizontalAlignment.Right; shareHost.Margin = new Thickness(0, -3, 0, 0);
            var readout = new Border
            {
                Child = fig, CornerRadius = new CornerRadius(5), Padding = new Thickness(7, 2, 7, 2), Margin = new Thickness(6, 0, 0, 0), Height = rowH,
                Background = Down((Color.FromArgb(0xF0, 0x03, 0x04, 0x06), 0), (Color.FromArgb(0xE0, 0x0D, 0x0F, 0x14), 1)),
                BorderBrush = Down((Color.FromArgb(0x90, 0, 0, 0), 0), (A(f.You ? PK.Gold : c, f.You ? 0.6 : 0.35), 1)), BorderThickness = new Thickness(1),
            };
            cols.Children.Add(PK.Col(readout, 3));
            AddExtras(cols, 4);
            inner = new Grid { Height = rowH - (f.You ? 2 : 0) };
            inner.Children.Add(cols);
        }
        cols.Height = double.IsNaN(cols.Height) ? inner.Height : cols.Height;
        // the name gives way to the marks after it (it trims), so the row never widens and the figures never move
        var nameHost = new Border { Child = nameHostChild, ClipToBounds = true, VerticalAlignment = VerticalAlignment.Center };
        if (forge && you == null && f.You) { /* Forge says you with the gold frame and your gold rank */ }
        cols.Children.Add(PK.Col(nameHost, 2));
        Border frame;
        if (podium && hero)
        {
            // the leader's card: lit from the left in the class's colour (gold-rimmed when it's you), as tall as any row
            frame = new Border
            {
                Child = inner, Margin = new Thickness(-7, 0, -7, 0), Padding = new Thickness(6, 0, 6, 0), CornerRadius = new CornerRadius(7),
                Background = Across((A(c, 0.22), 0), (A(c, 0.05), 0.7), (Color.FromArgb(0x10, 0xFF, 0xFF, 0xFF), 1)),
                BorderBrush = f.You ? PK.C(PK.Gold, 0.6) : Down((Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF), 0), (A(c, 0.25), 1)), BorderThickness = new Thickness(1),
            };
        }
        else if (f.You)
        {
            frame = podium ? PK.YouFrame(inner, 7, new Thickness(6, 0, 6, 0)) : PK.YouFrame(inner, forge ? 5 : 4, new Thickness(0));
            if (podium) frame.Margin = new Thickness(-7, 0, -7, 0);
        }
        else frame = new Border { Child = inner };
        var lift = new TranslateTransform(); var rise = new ScaleTransform(1, 1);
        var root = new Grid { VerticalAlignment = VerticalAlignment.Top, Height = rowH, RenderTransformOrigin = new Point(1, 0.5), RenderTransform = new TransformGroup { Children = { rise, lift } } };
        root.Children.Add(frame);
        if (f.You) Panel.SetZIndex(root, 2);
        var r = new RowEl { Root = root, Cols = cols, Chip = chip, ChipText = chipText, Name = name, Dps = dps, Share = share, Damage = damage, Crit = critT, Back = backT, Spark = sparkG, BarFill = fill, Track = track, Edge = edge, Tick = tick, Pb = pb, Frame = frame, You = f.You, Cls = f.Class, NameLine = nm, Plates = plates, NameHost = nameHost,
            Odo = odo, Lift = lift, Rise = rise, Ghost = ghost, Heat = heat, Under = under, UnderTrack = underTrack, Cp = cp, Heal = heal, HealPill = healPill, HealCross = cross, Hero = hero, HotEdge = hot, RankInk = rankInk };
        r.ChipOwn = chip.Effect as DropShadowEffect; r.ChipBlur0 = r.ChipOwn?.BlurRadius ?? 0; r.ChipOpacity0 = r.ChipOwn?.Opacity ?? 0;
        r.FrameGlow = frame.Effect as DropShadowEffect; r.FrameBlur0 = r.FrameGlow?.BlurRadius ?? 0; r.FrameOpacity0 = r.FrameGlow?.Opacity ?? 0;
        var card = new ToolTip { Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(0), HasDropShadow = false, Placement = System.Windows.Controls.Primitives.PlacementMode.Right, HorizontalOffset = 6 };
        root.ToolTip = card;
        ToolTipService.SetInitialShowDelay(root, 350);
        ToolTipService.SetShowDuration(root, 60_000);
        long fid = f.Id;
        root.ToolTipOpening += (_, ev) =>
        {
            if (Window.GetWindow(this) is ParserOverlay { ClickThroughNow: true }) { ev.Handled = true; return; }
            if (!FillCard(card, fid)) ev.Handled = true;
        };
        card.Closed += (_, _) => { if (_openCard == card) _openCard = null; };
        nameHost.SizeChanged += (_, _) => { FitName(r); FitHeal(r); };
        track.SizeChanged += (_, _) =>
        {
            if (track.Tag is double v) SetBar(r, v); else SetBar(r, fill.Width > 0 ? fill.Width / Math.Max(1, track.ActualWidth) : 0);
            if (!double.IsNaN(r.HealShare)) SetHealBar(r, double.IsNaN(r.HealShown) ? r.HealShare : r.HealShown);
        };
        return r;
    }
}
