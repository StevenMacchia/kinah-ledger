using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using KinahLedger.Parser;

namespace KinahLedger.Ui;

/// <summary>
/// Auto's fight meter, three concepts (Steven, 2026-11-17: "clear, legible and fucking incredible cinematic production
/// quality"): render-only samples, the bar the hero of every row, in Auto's own language (the Classic dark HUD, its pill,
/// Kinah gold, the class colours, Inter).
/// <list type="bullet">
/// <item>A, Capsule run: every part a lit capsule; each member's bar a glossy pill the row's whole height, marks on the rank disc.</item>
/// <item>B, Light beam: the name and DPS on a line, a thick beam of light under it with a white-hot head and its bloom.</item>
/// <item>C, Podium: the leader on a hero slab with a big figure, the rest on bevelled slabs, the DPS in a readout.</item>
/// </list>
/// </summary>
public static partial class MeterConcepts
{
    static partial void AddAuto(List<Concept> l)
    {
        l.Add(new Concept("auto", "A", "Capsule run", (e, now, w) => AutoKit.Capsules(e, now, w)));
        l.Add(new Concept("auto", "B", "Light beam", (e, now, w) => AutoKit.Beams(e, now, w)));
        l.Add(new Concept("auto", "C", "Podium", (e, now, w) => AutoKit.Podium(e, now, w)));
    }
}

static class AutoKit
{
    // ---- the shared parts: palette, type, the figures ----

    static readonly Color Gold = HudKit.Rgb(HudKit.GoldC), Gold2 = HudKit.Rgb(HudKit.SecGoldC), Mint = HudKit.Rgb(HudKit.MintC), Coral = HudKit.Rgb(HudKit.RecC),
        Ink = HudKit.Rgb(HudKit.InkC), Panel = HudKit.Rgb(HudKit.PanelC), White = Colors.White, Black = Colors.Black;

    static Color A(Color c, double a) => Color.FromArgb((byte)Math.Round(255 * Math.Clamp(a, 0, 1)), c.R, c.G, c.B);
    static Color Mix(Color a, Color b, double t) => Color.FromArgb((byte)(a.A + (b.A - a.A) * t), (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
    static SolidColorBrush S(Color c, double a = 1) { var b = new SolidColorBrush(A(c, a * c.A / 255.0)); b.Freeze(); return b; }
    static LinearGradientBrush V(params (Color C, double At)[] stops) => Grad(new Point(0, 0), new Point(0, 1), stops);
    static LinearGradientBrush H(params (Color C, double At)[] stops) => Grad(new Point(0, 0), new Point(1, 0), stops);
    static LinearGradientBrush Grad(Point a, Point b, (Color C, double At)[] stops)
    {
        var g = new LinearGradientBrush { StartPoint = a, EndPoint = b };
        foreach (var (c, at) in stops) g.GradientStops.Add(new GradientStop(c, at));
        g.Freeze();
        return g;
    }

    static TextBlock T(string s, double size, FontWeight w, Brush ink)
    {
        var t = new TextBlock { Text = s, FontSize = size, FontWeight = w, Foreground = ink, VerticalAlignment = VerticalAlignment.Center, FontFamily = HudKit.Text };
        Typography.SetNumeralAlignment(t, FontNumeralAlignment.Tabular);
        return t;
    }

    /// <summary>A tight dark halo: words over a lit bar or a bright scene still read.</summary>
    static DropShadowEffect Halo(double blur = 4, double opacity = 0.9) => new() { Color = Black, BlurRadius = blur, ShadowDepth = 0, Opacity = opacity };
    static DropShadowEffect Glow(Color c, double blur, double opacity, double depth = 0) => new() { Color = c, BlurRadius = blur, ShadowDepth = depth, Opacity = opacity, Direction = 270 };

    /// <summary>A text's width as the screen lays it out (display mode at 125%, the widest of the usual scales), a hair over.</summary>
    static double Wd(string s, double size, FontWeight w) => s.Length == 0 ? 0 :
        new FormattedText(s, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface(HudKit.Text, FontStyles.Normal, w, FontStretches.Normal), size, Brushes.White, null, TextFormattingMode.Display, 1.25)
            .WidthIncludingTrailingWhitespace * 1.03 + 1.5;

    /// <summary>The members as the rows show them, how many are left out, and the top figure the bars are measured against.</summary>
    static (List<MeterMember> Rows, int More, double Top) Members(Encounter e, DateTime now)
    {
        var rows = ThemeRows.Of(e, now, expanded: false);
        int more = Math.Max(0, e.Shown(now).Count(f => !f.Other) - rows.Count);
        double top = rows.Select(m => double.IsNaN(m.FigureValue) ? 0 : m.FigureValue).DefaultIfEmpty(1).Max();
        return (rows, more, Math.Max(1, top));
    }

    static double Ratio(MeterMember m, double top) => double.IsNaN(m.FigureValue) ? 0 : Math.Clamp(m.FigureValue / top, 0, 1);

    static string Clock(Encounter e, DateTime now) => BossParser.Clock(e.Duration(now));

    /// <summary>A finished fight's one word ("Killed", "Wiped") in its tone; the whole result for any other ending.</summary>
    static (string Text, Color Tone) Ending(Encounter e, DateTime now) => e.Outcome switch
    {
        "kill" or "cleared" => (L.T("Killed"), Mint),
        "wipe" => (L.T("Wiped"), Coral),
        _ => (ParserPanel.OutcomeOf(e, now).Text, (ParserPanel.OutcomeOf(e, now).Ink as SolidColorBrush)?.Color ?? Ink),
    };

    static string HealText(MeterMember m) => m.Heal.Length == 0 ? "" : "+" + m.Heal;

    /// <summary>
    /// The name's line: the name, then Combat Power (quiet), then a healer's heal (mint). A name of up to 10 letters is never
    /// cut: Combat Power gives way first (the hover card has it), then the heal's "/s". A longer name trims with "…".
    /// </summary>
    static FrameworkElement NameLine(MeterMember m, double room, double nameSize, FontWeight nameW, double small, Brush cpInk, bool halo)
    {
        string heal = HealText(m);
        double nameW0 = Wd(m.Name, nameSize, nameW), cpW = Wd(m.Cp, small, FontWeights.Medium), healW = Wd(heal, small + 0.5, FontWeights.Bold);
        bool showCp = m.Cp.Length > 0;
        double Need() => Math.Min(nameW0, m.Name.Length > 10 ? Wd(m.Name[..7] + "…", nameSize, nameW) : nameW0) + (showCp ? 6 + cpW : 0) + (heal.Length > 0 ? 7 + healW : 0);
        if (showCp && Need() > room) showCp = false;
        var g = new Grid { VerticalAlignment = VerticalAlignment.Center, ClipToBounds = false };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MaxWidth = nameW0 + 2 });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.0001, GridUnitType.Star) });
        var name = T(m.Name, nameSize, nameW, S(White)); name.TextTrimming = TextTrimming.CharacterEllipsis;
        if (halo) name.Effect = Halo();
        g.Children.Add(name);
        if (showCp)
        {
            var cp = T(m.Cp, small, FontWeights.Medium, cpInk); cp.Margin = new Thickness(6, 1, 0, 0);
            if (halo) cp.Effect = Halo(4, 0.95);
            Grid.SetColumn(cp, 1); g.Children.Add(cp);
        }
        if (heal.Length > 0)
        {
            var h = T(heal, small + 0.5, FontWeights.Bold, S(Mix(Mint, White, 0.25))); h.Margin = new Thickness(7, 1, 0, 0);
            h.Effect = Halo(4, 1);
            Grid.SetColumn(h, 2); g.Children.Add(h);
        }
        return g;
    }

    /// <summary>"1 more", centred between two hairlines.</summary>
    static FrameworkElement MoreLine(int more, Color line, Brush ink, double size = 10)
    {
        var g = new Grid { Margin = new Thickness(6, 1, 6, 1), Height = 14 };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var l1 = new Border { Height = 1, Background = H((A(line, 0), 0), (line, 1)), VerticalAlignment = VerticalAlignment.Center };
        var l2 = new Border { Height = 1, Background = H((line, 0), (A(line, 0), 1)), VerticalAlignment = VerticalAlignment.Center };
        var t = T(L.F("{0} more", more), size, FontWeights.SemiBold, ink); t.Margin = new Thickness(8, 0, 8, 0);
        Grid.SetColumn(t, 1); Grid.SetColumn(l2, 2);
        g.Children.Add(l1); g.Children.Add(t); g.Children.Add(l2);
        return g;
    }

    /// <summary>The meter's frame: the root every concept hands back (Inter, display text, tabular figures).</summary>
    static Border Frame(double width)
    {
        var b = new Border { Width = width + 10, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, SnapsToDevicePixels = true };
        TextElement.SetFontFamily(b, HudKit.Text);
        TextElement.SetFontWeight(b, FontWeights.Medium);
        TextOptions.SetTextFormattingMode(b, TextFormattingMode.Display);
        Typography.SetNumeralAlignment(b, FontNumeralAlignment.Tabular);
        return b;
    }

    /// <summary>The foot's two figures, "Total" and the group's DPS (or the held average with its badge).</summary>
    static (string Total, string GroupLabel, string Group, bool Avg) Foot(Encounter e, DateTime now) =>
        (Amounts.Meter(e.Credited), e.HeldAverage ? L.T("Group") : L.T("Group DPS"), e.GroupDpsText(e.HeldAverage ? e.End ?? now : now), e.HeldAverage);

    static Border Pill(string text, double size, Color ink, Color fill, double fillA, double lineA, Thickness pad)
    {
        var t = T(text, size, FontWeights.Bold, S(ink));
        return new Border { CornerRadius = new CornerRadius(20), Padding = pad, Background = S(fill, fillA), BorderBrush = S(ink, lineA), BorderThickness = new Thickness(1), Child = t, VerticalAlignment = VerticalAlignment.Center };
    }

    // ============================================================================================================
    // A · Capsule run
    // ============================================================================================================

    public static FrameworkElement Capsules(Encounter e, DateTime now, double width)
    {
        var root = Frame(width);
        root.CornerRadius = new CornerRadius(20);
        root.Padding = new Thickness(10, 10, 10, 10);
        root.Background = V((Color.FromArgb(0xF2, 0x17, 0x1A, 0x23), 0), (Color.FromArgb(0xF4, 0x0C, 0x0D, 0x12), 0.45), (Color.FromArgb(0xF6, 0x08, 0x09, 0x0C), 1));
        root.BorderBrush = V((A(White, 0.22), 0), (A(White, 0.06), 0.35), (A(White, 0.04), 1));
        root.BorderThickness = new Thickness(1);
        root.Effect = new DropShadowEffect { Color = Black, BlurRadius = 22, ShadowDepth = 5, Direction = 270, Opacity = 0.65 };
        double inner = width + 10 - 22;
        var stack = new StackPanel();
        root.Child = stack;

        // the head: a capsule of its own; the live dot or the result, the title, the clock in a gold-rimmed pill
        var head = new Grid { Height = 30, Margin = new Thickness(0, 0, 0, 7) };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var headBg = new Border { CornerRadius = new CornerRadius(15), Background = H((A(Gold, 0.13), 0), (A(Gold, 0.03), 0.6), (A(White, 0.02), 1)), BorderBrush = H((A(Gold, 0.45), 0), (A(Gold, 0.08), 1)), BorderThickness = new Thickness(1) };
        Grid.SetColumnSpan(headBg, 4); head.Children.Add(headBg);
        var dot = new Ellipse { Width = 8, Height = 8, Fill = S(e.Live ? Gold : Ending(e, now).Tone), Margin = new Thickness(12, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, Effect = Glow(e.Live ? Gold : Ending(e, now).Tone, 10, 0.95) };
        head.Children.Add(dot);
        var title = T(e.Title, 14, FontWeights.Bold, S(Ink)); title.TextTrimming = TextTrimming.CharacterEllipsis; title.Effect = Halo(3, 0.6);
        Grid.SetColumn(title, 1); head.Children.Add(title);
        if (!e.Live)
        {
            var (word, tone) = Ending(e, now);
            var res = Pill(word, 11, tone, tone, 0.14, 0.55, new Thickness(9, 1, 9, 2)); res.Margin = new Thickness(8, 0, 6, 0);
            Grid.SetColumn(res, 2); head.Children.Add(res);
        }
        var clock = T(Clock(e, now), 15, FontWeights.Bold, S(e.Live ? Gold : Ink));
        var clockPill = new Border { CornerRadius = new CornerRadius(13), Padding = new Thickness(10, 1, 10, 2), Margin = new Thickness(0, 2, 2, 2), Background = V((Color.FromArgb(0xE6, 0x05, 0x06, 0x08), 0), (Color.FromArgb(0xE6, 0x10, 0x11, 0x16), 1)),
            BorderBrush = S(Gold, e.Live ? 0.75 : 0.3), BorderThickness = new Thickness(1), Child = clock, Effect = e.Live ? Glow(Gold, 8, 0.35) : null };
        Grid.SetColumn(clockPill, 3); head.Children.Add(clockPill);
        stack.Children.Add(head);

        // the boss's HP: a slim lit capsule, its figure after it
        if (e.Live && e.BossHpLeft is { } hpl)
        {
            var hp = new Grid { Margin = new Thickness(4, 0, 4, 8) };
            hp.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            hp.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var track = new Grid { Height = 7, VerticalAlignment = VerticalAlignment.Center };
            track.Children.Add(new Border { CornerRadius = new CornerRadius(4), Background = S(White, 0.07), BorderBrush = S(Black, 0.5), BorderThickness = new Thickness(0, 1, 0, 0) });
            var hpTone = hpl < 0.34 ? Coral : Gold;
            var fill = new Border { CornerRadius = new CornerRadius(4), HorizontalAlignment = HorizontalAlignment.Left, Background = V((Mix(hpTone, White, 0.45), 0), (hpTone, 0.5), (Mix(hpTone, Black, 0.3), 1)), Effect = Glow(hpTone, 8, 0.6) };
            track.SizeChanged += (_, _) => fill.Width = Math.Max(6, track.ActualWidth * hpl);
            track.Children.Add(fill);
            hp.Children.Add(track);
            var pct = ParserPanel.HpText(e, 11.5, S(hpTone), S(Ink, 0.7)); pct.Margin = new Thickness(10, 0, 0, 0); pct.Effect = Halo(3, 0.6);
            Grid.SetColumn(pct, 1); hp.Children.Add(pct);
            stack.Children.Add(hp);
        }

        // the rows: a glossy capsule each
        var (rows, more, top) = Members(e, now);
        double dpsW = Math.Max(Wd("888.8K", 16.5, FontWeights.Bold), rows.Max(m => Wd(m.Figure, 16.5, FontWeights.Bold)));
        double shareW = Math.Max(Wd("88.8%", 10.5, FontWeights.SemiBold), rows.Max(m => Wd(m.ShareText, 10.5, FontWeights.SemiBold)));
        const double rowH = 30, disc = 22;
        foreach (var m in rows)
        {
            if (m.Pinned) stack.Children.Add(MoreLine(more, A(White, 0.16), S(White, 0.42)));
            var c = m.ClassColor;
            var row = new Grid { Height = rowH, Margin = new Thickness(0, 0, 0, 5) };
            // the track: dark glass, a capsule the row's width
            row.Children.Add(new Border { CornerRadius = new CornerRadius(rowH / 2), Background = V((A(Black, 0.55), 0), (A(White, 0.04), 1)), BorderBrush = m.You ? S(Gold, 0.85) : S(White, 0.07), BorderThickness = new Thickness(m.You ? 1.4 : 1) });
            // the fill: the class's colour lit from above, a gloss on its upper half, a bright head
            double fw = Math.Max(rowH + 6, inner * Ratio(m, top));
            var fill = new Grid { Width = fw, HorizontalAlignment = HorizontalAlignment.Left };
            fill.Children.Add(new Border { CornerRadius = new CornerRadius(rowH / 2), Background = V((Mix(c, White, 0.2), 0), (Mix(c, Black, 0.12), 0.45), (Mix(c, Black, 0.55), 1)), Opacity = 0.9 });
            fill.Children.Add(new Border { CornerRadius = new CornerRadius(rowH / 2, rowH / 2, 4, 4), Height = rowH * 0.46, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(5, 1.5, 5, 0), Background = V((A(White, 0.26), 0), (A(White, 0.03), 1)) });
            // the head's light: a soft white cap at the fill's end
            fill.Children.Add(new Border { Width = 26, HorizontalAlignment = HorizontalAlignment.Right, CornerRadius = new CornerRadius(0, rowH / 2, rowH / 2, 0), Background = H((A(White, 0), 0), (A(Mix(c, White, 0.7), 0.55), 1)) });
            fill.Children.Add(new Border { CornerRadius = new CornerRadius(rowH / 2), BorderBrush = V((A(White, 0.45), 0), (A(White, 0), 0.5), (A(Black, 0.4), 1)), BorderThickness = new Thickness(1) });
            fill.Effect = Glow(c, 12, 0.45);
            // the darker right of the row behind the figures, so a long bar never washes them out
            row.Children.Add(fill);
            row.Children.Add(new Border { CornerRadius = new CornerRadius(0, rowH / 2, rowH / 2, 0), HorizontalAlignment = HorizontalAlignment.Right, Width = dpsW + shareW + 40, Background = H((A(Black, 0), 0), (A(Black, 0.42), 0.45), (A(Black, 0.5), 1)), IsHitTestVisible = false });
            if (m.You) row.Effect = Glow(Gold, 14, 0.5);

            var cols = new Grid { Margin = new Thickness(4, 0, 10, 0) };
            cols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(disc + 11) });
            cols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
            cols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            cols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(dpsW + 8) });
            cols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(shareW + 4) });
            cols.Children.Add(RankDisc(m, disc));
            var icon = ClassArt.Icon(m.Class, 17, m.Name); icon.VerticalAlignment = VerticalAlignment.Center; icon.HorizontalAlignment = HorizontalAlignment.Left; icon.Effect = Halo(3, 0.7);
            Grid.SetColumn(icon, 1); cols.Children.Add(icon);
            double room = inner - 14 - (disc + 11) - 22 - (dpsW + 8) - (shareW + 4) - 6;
            var nl = NameLine(m, room, 13, m.You ? FontWeights.Bold : FontWeights.SemiBold, 10.5, S(White, 0.78), true); nl.Margin = new Thickness(0, 0, 6, 0);
            Grid.SetColumn(nl, 2); cols.Children.Add(nl);
            var dps = T(m.Figure, 16.5, FontWeights.Bold, S(White)); dps.HorizontalAlignment = HorizontalAlignment.Right; dps.TextAlignment = TextAlignment.Right; dps.Effect = Halo(5, 0.95);
            Grid.SetColumn(dps, 3); cols.Children.Add(dps);
            var sh = T(m.ShareText, 10.5, FontWeights.SemiBold, S(White, 0.6)); sh.HorizontalAlignment = HorizontalAlignment.Right; sh.Margin = new Thickness(0, 2, 0, 0); sh.Effect = Halo(3, 0.7);
            Grid.SetColumn(sh, 4); cols.Children.Add(sh);
            row.Children.Add(cols);
            stack.Children.Add(row);
        }

        // the foot: a dark capsule, the total on the left, the group's DPS (gold) on the right
        var (total, gl, gv, avg) = Foot(e, now);
        var foot = new Grid { Height = 28, Margin = new Thickness(0, 3, 0, 0) };
        foot.Children.Add(new Border { CornerRadius = new CornerRadius(14), Background = S(Black, 0.4), BorderBrush = S(White, 0.06), BorderThickness = new Thickness(1) });
        var fl = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        fl.Children.Add(T(L.T("Total"), 10.5, FontWeights.SemiBold, S(White, 0.5)));
        var tv = T(total, 13.5, FontWeights.Bold, S(Ink)); tv.Margin = new Thickness(6, 0, 0, 0); fl.Children.Add(tv);
        foot.Children.Add(fl);
        var fr = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, avg ? 5 : 14, 0), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        fr.Children.Add(T(gl, 10.5, FontWeights.SemiBold, S(White, 0.5)));
        var gvt = T(gv, 13.5, FontWeights.Bold, S(Gold)); gvt.Margin = new Thickness(6, 0, 0, 0); gvt.Effect = Glow(Gold, 8, 0.35); fr.Children.Add(gvt);
        if (avg) { var b = Pill(L.T(Encounter.AvgBadge), 9.5, Mint, Mint, 0.14, 0.6, new Thickness(7, 1, 7, 1)); b.Margin = new Thickness(7, 0, 0, 0); fr.Children.Add(b); }
        foot.Children.Add(fr);
        stack.Children.Add(foot);
        return root;
    }

    /// <summary>The rank on a disc in the class's ring (gold for you), the marks as pips on it: TOP DPS a gold star, TOP HEALS a mint cross, deaths a coral count.</summary>
    static FrameworkElement RankDisc(MeterMember m, double d)
    {
        var g = new Grid { Width = d, Height = d, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        var c = m.ClassColor;
        g.Children.Add(new Ellipse { Fill = m.You ? (Brush)V((Mix(Gold, White, 0.35), 0), (Gold2, 1)) : V((A(Color.FromRgb(0x1C, 0x1F, 0x29), 0.95), 0), (A(Color.FromRgb(0x07, 0x08, 0x0B), 0.95), 1)),
            Stroke = S(m.You ? Mix(Gold, White, 0.4) : Mix(c, White, 0.25)), StrokeThickness = 1.6, Effect = Glow(m.You ? Gold : c, 8, 0.55) });
        var n = T(m.Rank.ToString(CultureInfo.InvariantCulture), 11.5, FontWeights.Bold, m.You ? S(Color.FromRgb(0x1D, 0x13, 0x00)) : S(White));
        n.HorizontalAlignment = HorizontalAlignment.Center; n.Margin = new Thickness(0, 0, 0, 1);
        g.Children.Add(n);
        if (m.TopDps || m.TopHeals)
        {
            var tone = m.TopDps ? Gold : Mint;
            var pip = new Border { Width = 13, Height = 13, CornerRadius = new CornerRadius(6.5), Background = S(tone), BorderBrush = S(Black, 0.7), BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, -6, -9, 0), Effect = Glow(tone, 6, 0.9),
                Child = new TextBlock { Text = m.TopDps ? "★" : "✚", FontSize = 8.5, FontWeight = FontWeights.Bold, Foreground = S(Color.FromRgb(0x14, 0x10, 0x04)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, -1, 0, 0) } };
            System.Windows.Automation.AutomationProperties.SetName(pip, m.TopDps ? L.T("TOP DPS") : L.T("TOP HEALS"));
            pip.ToolTip = m.TopDps ? L.T("TOP DPS") : L.T("TOP HEALS");
            g.Children.Add(pip);
        }
        if (m.Deaths > 0)
        {
            var pip = new Border { MinWidth = 13, Height = 13, CornerRadius = new CornerRadius(6.5), Padding = new Thickness(3, 0, 3, 0), Background = S(Coral), BorderBrush = S(Black, 0.7), BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, -10, -6), Effect = Glow(Coral, 6, 0.8),
                Child = new TextBlock { Text = "✕" + m.Deaths.ToString(CultureInfo.InvariantCulture), FontSize = 8.5, FontWeight = FontWeights.Bold, Foreground = S(White), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, -1, 0, 0) } };
            g.Children.Add(pip);
        }
        return g;
    }

    // ============================================================================================================
    // B · Light beam
    // ============================================================================================================

    public static FrameworkElement Beams(Encounter e, DateTime now, double width)
    {
        var root = Frame(width);
        root.CornerRadius = new CornerRadius(12);
        root.Padding = new Thickness(14, 12, 14, 12);
        // a key light from above: warm, falling off down the panel
        var bg = new DrawingBrush { Stretch = Stretch.Fill };
        var dg = new DrawingGroup();
        dg.Children.Add(new GeometryDrawing(V((Color.FromArgb(0xF0, 0x10, 0x11, 0x17), 0), (Color.FromArgb(0xF6, 0x07, 0x08, 0x0B), 1)), null, new RectangleGeometry(new Rect(0, 0, 1, 1))));
        var key = new RadialGradientBrush { Center = new Point(0.5, 0), GradientOrigin = new Point(0.5, -0.05), RadiusX = 0.75, RadiusY = 0.42 };
        key.GradientStops.Add(new GradientStop(A(Gold, 0.16), 0)); key.GradientStops.Add(new GradientStop(A(Gold, 0.04), 0.55)); key.GradientStops.Add(new GradientStop(A(Gold, 0), 1));
        dg.Children.Add(new GeometryDrawing(key, null, new RectangleGeometry(new Rect(0, 0, 1, 1))));
        bg.Drawing = dg; bg.Freeze();
        root.Background = bg;
        root.BorderBrush = S(White, 0.08); root.BorderThickness = new Thickness(1);
        root.Effect = new DropShadowEffect { Color = Black, BlurRadius = 20, ShadowDepth = 4, Direction = 270, Opacity = 0.6 };
        double inner = width + 10 - 30;
        var outer = new Grid();
        root.Child = outer;
        // the gold hairline along the top edge, brightest in the middle
        outer.Children.Add(new Border { Height = 1, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(10, -12, 10, 0), Background = H((A(Gold, 0), 0), (A(Gold, 0.9), 0.5), (A(Gold, 0), 1)) });
        var stack = new StackPanel();
        outer.Children.Add(stack);

        // the head: the title over the place, the clock big at the right; a finished fight's result over the clock
        var head = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var tl = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var title = T(e.Title, 15, FontWeights.Bold, S(Ink)); title.TextTrimming = TextTrimming.CharacterEllipsis;
        tl.Children.Add(title);
        var place = T(e.Place.Length > 0 ? e.Place : e.Kind, 10.5, FontWeights.Medium, S(White, 0.45)); place.TextTrimming = TextTrimming.CharacterEllipsis; place.Margin = new Thickness(0, 1, 0, 0);
        tl.Children.Add(place);
        head.Children.Add(tl);
        var cr = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
        if (!e.Live)
        {
            var (word, tone) = Ending(e, now);
            var w = T(word.ToUpper(CultureInfo.CurrentUICulture), 9.5, FontWeights.Bold, S(tone)); w.HorizontalAlignment = HorizontalAlignment.Right; w.Effect = Glow(tone, 6, 0.6);
            cr.Children.Add(w);
        }
        var clock = T(Clock(e, now), 21, FontWeights.Bold, S(e.Live ? Gold : Ink)); clock.HorizontalAlignment = HorizontalAlignment.Right; clock.Margin = new Thickness(0, -2, 0, -2);
        if (e.Live) clock.Effect = Glow(Gold, 12, 0.45);
        cr.Children.Add(clock);
        Grid.SetColumn(cr, 1); head.Children.Add(cr);
        stack.Children.Add(head);

        // the boss's HP: a coral-to-gold beam
        if (e.Live && e.BossHpLeft is { } hpl)
        {
            var hp = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            hp.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            hp.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var hpLabel = ParserPanel.HpText(e, 11, S(hpl < 0.34 ? Coral : Gold), S(White, 0.6)); hpLabel.HorizontalAlignment = HorizontalAlignment.Right; hpLabel.Margin = new Thickness(0, 0, 0, 3);
            hp.Children.Add(hpLabel);
            var beam = Beam(hpl < 0.34 ? Coral : Gold, inner, hpl, 5, false);
            Grid.SetRow(beam, 1); hp.Children.Add(beam);
            stack.Children.Add(hp);
        }

        var (rows, more, top) = Members(e, now);
        double dpsW = Math.Max(Wd("888.8K", 17, FontWeights.Bold), rows.Max(m => Wd(m.Figure, 17, FontWeights.Bold)));
        double shareW = Math.Max(Wd("88.8%", 10, FontWeights.SemiBold), rows.Max(m => Wd(m.ShareText, 10, FontWeights.SemiBold)));
        double rankW = 20;
        foreach (var m in rows)
        {
            if (m.Pinned) stack.Children.Add(MoreLine(more, A(Gold, 0.22), S(White, 0.42)));
            var c = m.ClassColor;
            var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(rankW + 4) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.RowDefinitions.Add(new RowDefinition { Height = new GridLength(20) });
            row.RowDefinitions.Add(new RowDefinition { Height = new GridLength(14) });
            if (m.You)
            {
                // your band: a gold wash behind both tiers, a gold edge at the left
                var band = new Border { Margin = new Thickness(-8, -3, -6, -3), CornerRadius = new CornerRadius(6), Background = H((A(Gold, 0.16), 0), (A(Gold, 0.03), 0.7), (A(Gold, 0), 1)), BorderBrush = H((A(Gold, 0.6), 0), (A(Gold, 0), 0.8)), BorderThickness = new Thickness(1) };
                Grid.SetRowSpan(band, 2); Grid.SetColumnSpan(band, 2); row.Children.Add(band);
            }
            // the rank: a tall numeral in the class's colour, the marks under it
            var rk = T(m.Rank.ToString(CultureInfo.InvariantCulture), 18, FontWeights.Bold, S(m.You ? Gold : Mix(c, White, 0.3))); rk.HorizontalAlignment = HorizontalAlignment.Center; rk.VerticalAlignment = VerticalAlignment.Center;
            rk.Effect = Glow(m.You ? Gold : c, 8, 0.6);
            Grid.SetRowSpan(rk, 2); row.Children.Add(rk);
            // the line: icon, name, marks, the DPS big at the right
            var line = new Grid();
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(dpsW + 6) });
            var icon = ClassArt.Icon(m.Class, 15, m.Name); icon.HorizontalAlignment = HorizontalAlignment.Left; icon.VerticalAlignment = VerticalAlignment.Center;
            line.Children.Add(icon);
            var marks = Marks(m, 9.5);
            double marksW = MarksWidth(m, 9.5);
            double room = inner - (rankW + 4) - 20 - marksW - (dpsW + 6) - 6;
            var nl = NameLine(m, room, 13, m.You ? FontWeights.Bold : FontWeights.SemiBold, 10.5, S(White, 0.45), false);
            Grid.SetColumn(nl, 1); line.Children.Add(nl);
            Grid.SetColumn(marks, 2); line.Children.Add(marks);
            var dps = T(m.Figure, 17, FontWeights.Bold, S(White)); dps.HorizontalAlignment = HorizontalAlignment.Right; dps.TextAlignment = TextAlignment.Right;
            dps.Effect = Glow(Mix(c, White, 0.3), 10, 0.35);
            Grid.SetColumn(dps, 3); line.Children.Add(dps);
            Grid.SetColumn(line, 1); row.Children.Add(line);
            // the beam, the share at its end
            var bl = new Grid { VerticalAlignment = VerticalAlignment.Center };
            bl.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            bl.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(shareW + 8) });
            bl.Children.Add(Beam(c, inner - (rankW + 4) - (shareW + 8), Ratio(m, top), 9, true));
            var sh = T(m.ShareText, 10, FontWeights.SemiBold, S(White, 0.55)); sh.HorizontalAlignment = HorizontalAlignment.Right; sh.TextAlignment = TextAlignment.Right;
            Grid.SetColumn(sh, 1); bl.Children.Add(sh);
            Grid.SetColumn(bl, 1); Grid.SetRow(bl, 1); row.Children.Add(bl);
            stack.Children.Add(row);
        }

        // the foot: a hairline, then the two figures with small caps labels
        stack.Children.Add(new Border { Height = 1, Margin = new Thickness(0, 2, 0, 8), Background = H((A(White, 0), 0), (A(White, 0.16), 0.5), (A(White, 0), 1)) });
        var (total, gl, gv, avg) = Foot(e, now);
        var foot = new Grid();
        foot.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        foot.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        foot.Children.Add(Figure(L.T("Total"), total, Ink, false));
        var gr = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        if (avg) { var b = Pill(L.T(Encounter.AvgBadge), 9, Mint, Mint, 0.12, 0.55, new Thickness(6, 1, 6, 1)); b.Margin = new Thickness(0, 0, 8, 0); b.VerticalAlignment = VerticalAlignment.Bottom; gr.Children.Add(b); }
        gr.Children.Add(Figure(gl, gv, Gold, true));
        Grid.SetColumn(gr, 1); foot.Children.Add(gr);
        stack.Children.Add(foot);
        return root;

        static FrameworkElement Figure(string label, string value, Color ink, bool right)
        {
            var p = new StackPanel { HorizontalAlignment = right ? HorizontalAlignment.Right : HorizontalAlignment.Left };
            var l = T(label.ToUpper(CultureInfo.CurrentUICulture), 9, FontWeights.Bold, S(White, 0.42)); l.HorizontalAlignment = p.HorizontalAlignment;
            var v = T(value, 15, FontWeights.Bold, S(ink)); v.HorizontalAlignment = p.HorizontalAlignment; v.Margin = new Thickness(0, -1, 0, 0);
            if (ink == Gold) v.Effect = Glow(Gold, 10, 0.4);
            p.Children.Add(l); p.Children.Add(v);
            return p;
        }
    }

    /// <summary>A beam of light: a dark channel, the fill brightening toward a white-hot head, its bloom under it.</summary>
    static FrameworkElement Beam(Color c, double w, double ratio, double h, bool head)
    {
        var g = new Grid { Height = h + 4, VerticalAlignment = VerticalAlignment.Center };
        g.Children.Add(new Border { Height = h, CornerRadius = new CornerRadius(h / 2), Background = S(White, 0.06), BorderBrush = S(Black, 0.6), BorderThickness = new Thickness(0, 1, 0, 0) });
        double fw = Math.Max(h * 2, w * ratio);
        var bloom = new Border { Width = fw, Height = h, HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(h / 2), Background = H((A(c, 0.2), 0), (A(c, 0.9), 1)), Effect = new BlurEffect { Radius = 9 }, Opacity = 0.9 };
        g.Children.Add(bloom);
        var fill = new Border { Width = fw, Height = h, HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(h / 2),
            Background = H((Mix(c, Black, 0.45), 0), (c, 0.7), (Mix(c, White, 0.45), 1)) };
        g.Children.Add(fill);
        g.Children.Add(new Border { Width = fw, Height = h / 2, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, -h / 2, 0, 0), CornerRadius = new CornerRadius(h / 2), Background = H((A(White, 0), 0), (A(White, 0.35), 1)) });
        if (head && ratio > 0)
            g.Children.Add(new Ellipse { Width = h + 3, Height = h + 3, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(fw - h - 2, 0, 0, 0), Fill = S(Mix(c, White, 0.85)), Effect = Glow(Mix(c, White, 0.5), 12, 1) });
        return g;
    }

    /// <summary>The small marks after a name: TOP DPS a gold star, TOP HEALS a mint cross, deaths a coral ✕2.</summary>
    static FrameworkElement Marks(MeterMember m, double size)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0) };
        if (m.TopDps) p.Children.Add(Mark("★", L.T("TOP DPS"), Gold, size));
        if (m.TopHeals) p.Children.Add(Mark("✚", L.T("TOP HEALS"), Mint, size));
        if (m.Deaths > 0) p.Children.Add(Mark("✕" + m.Deaths.ToString(CultureInfo.InvariantCulture), null, Coral, size));
        return p;
    }

    static double MarksWidth(MeterMember m, double size) => 8 + (m.TopDps ? 20 : 0) + (m.TopHeals ? 20 : 0) + (m.Deaths > 0 ? 26 : 0);

    static Border Mark(string glyph, string? tip, Color tone, double size)
    {
        var b = new Border { Height = 15, MinWidth = 16, CornerRadius = new CornerRadius(4), Padding = new Thickness(3, 0, 3, 0), Margin = new Thickness(0, 0, 4, 0), Background = V((Mix(Color.FromRgb(0x0B, 0x0C, 0x10), tone, 0.22), 0), (Mix(Color.FromRgb(0x0B, 0x0C, 0x10), tone, 0.1), 1)), BorderBrush = S(tone, 0.8), Effect = Glow(Colors.Black, 4, 0.6), BorderThickness = new Thickness(1),
            Child = new TextBlock { Text = glyph, FontSize = size, FontWeight = FontWeights.Bold, Foreground = S(Mix(tone, White, 0.2)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, -1, 0, 0) } };
        if (tip != null) { b.ToolTip = tip; System.Windows.Automation.AutomationProperties.SetName(b, tip); }
        return b;
    }

    // ============================================================================================================
    // C · Podium
    // ============================================================================================================

    public static FrameworkElement Podium(Encounter e, DateTime now, double width)
    {
        var root = Frame(width);
        root.CornerRadius = new CornerRadius(14);
        root.Padding = new Thickness(8, 8, 8, 9);
        root.Background = V((Color.FromArgb(0xF2, 0x14, 0x16, 0x1E), 0), (Color.FromArgb(0xF6, 0x09, 0x0A, 0x0E), 1));
        root.BorderBrush = V((A(White, 0.16), 0), (A(White, 0.05), 1)); root.BorderThickness = new Thickness(1);
        root.Effect = new DropShadowEffect { Color = Black, BlurRadius = 20, ShadowDepth = 5, Direction = 270, Opacity = 0.62 };
        double inner = width + 10 - 18;
        var stack = new StackPanel();
        root.Child = stack;

        // the head: the title, then a readout capsule with the result and the clock
        var head = new Grid { Margin = new Thickness(6, 2, 0, 8) };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = T(e.Title, 14, FontWeights.Bold, S(Ink)); title.TextTrimming = TextTrimming.CharacterEllipsis;
        head.Children.Add(title);
        var ro = new StackPanel { Orientation = Orientation.Horizontal };
        if (!e.Live) { var (word, tone) = Ending(e, now); var w = T(word, 11, FontWeights.Bold, S(tone)); w.Margin = new Thickness(0, 0, 8, 0); ro.Children.Add(w); }
        ro.Children.Add(T(Clock(e, now), 15, FontWeights.Bold, S(e.Live ? Gold : Ink)));
        var readout = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 2, 10, 3), Margin = new Thickness(10, 0, 0, 0), Background = V((Color.FromArgb(0xFF, 0x04, 0x05, 0x07), 0), (Color.FromArgb(0xFF, 0x0E, 0x10, 0x15), 1)),
            BorderBrush = V((A(Black, 0.9), 0), (A(White, 0.14), 1)), BorderThickness = new Thickness(1), Child = ro };
        Grid.SetColumn(readout, 1); head.Children.Add(readout);
        stack.Children.Add(head);

        if (e.Live && e.BossHpLeft is { } hpl)
        {
            var hp = new Grid { Margin = new Thickness(4, 0, 4, 8) };
            hp.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            hp.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var tone = hpl < 0.34 ? Coral : Gold;
            hp.Children.Add(Slab(tone, inner - 120, hpl, 8, 3, false));
            var pct = ParserPanel.HpText(e, 11.5, S(tone), S(White, 0.62)); pct.Margin = new Thickness(10, 0, 0, 0);
            Grid.SetColumn(pct, 1); hp.Children.Add(pct);
            stack.Children.Add(hp);
        }

        var (rows, more, top) = Members(e, now);
        double dpsW = Math.Max(Wd("888.8K", 16, FontWeights.Bold), rows.Max(m => Wd(m.Figure, 16, FontWeights.Bold)));
        double shareW = Math.Max(Wd("88.8%", 10, FontWeights.SemiBold), rows.Max(m => Wd(m.ShareText, 10, FontWeights.SemiBold)));
        double readW = Math.Max(dpsW, Wd(rows.FirstOrDefault()?.Figure ?? "", 19, FontWeights.Bold)) + 16;
        bool first = true;
        foreach (var m in rows)
        {
            if (m.Pinned) stack.Children.Add(MoreLine(more, A(White, 0.14), S(White, 0.4)));
            bool hero = first && m.Rank == 1 && !m.Pinned;
            first = false;
            double h = hero ? 46 : 28;
            var c = m.ClassColor;
            var row = new Grid { Height = h, Margin = new Thickness(0, 0, 0, hero ? 6 : 4) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(readW) });
            // the slab: the bar, the row's whole height, bevelled, measured over the row less the readout
            double barW = inner - readW - 4;
            var slab = Slab(c, barW, Ratio(m, top), h, hero ? 7 : 5, true, m.You);
            row.Children.Add(slab);
            // the content on the slab
            var on = new Grid { Margin = new Thickness(hero ? 10 : 7, 0, 8, 0) };
            on.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(hero ? 30 : 20) });
            on.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(hero ? 24 : 21) });
            on.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            on.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            on.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var rk = T(m.Rank.ToString(CultureInfo.InvariantCulture), hero ? 26 : 13, FontWeights.Bold, m.You ? S(Gold) : S(White, hero ? 0.95 : 0.85)); rk.Effect = Halo(hero ? 6 : 4, 0.9);
            rk.Margin = new Thickness(0, hero ? -2 : 0, 0, 0);
            on.Children.Add(rk);
            var icon = ClassArt.Icon(m.Class, hero ? 20 : 16, m.Name); icon.HorizontalAlignment = HorizontalAlignment.Left; icon.VerticalAlignment = VerticalAlignment.Center; icon.Effect = Halo(3, 0.7);
            Grid.SetColumn(icon, 1); on.Children.Add(icon);
            double marksW = MarksWidth(m, 9);
            double room = barW - (hero ? 10 : 7) - 8 - (hero ? 54 : 41) - marksW - 4;
            FrameworkElement nameEl;
            if (hero)
            {
                // the leader: the name big, Combat Power and the heal on a line under it
                var p = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                var nm = T(m.Name, 15.5, FontWeights.Bold, S(White)); nm.TextTrimming = TextTrimming.CharacterEllipsis; nm.Effect = Halo(5, 0.95);
                p.Children.Add(nm);
                var sub = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 0) };
                if (m.Cp.Length > 0) { var cp = T(L.T("CP") + " " + m.Cp, 10.5, FontWeights.SemiBold, S(White, 0.7)); cp.Effect = Halo(3, 0.8); sub.Children.Add(cp); }
                if (m.Heal.Length > 0) { var hh = T(HealText(m), 11, FontWeights.Bold, S(Mix(Mint, White, 0.25))); hh.Margin = new Thickness(8, 0, 0, 0); hh.Effect = Halo(3, 0.85); sub.Children.Add(hh); }
                p.Children.Add(sub);
                nameEl = p;
            }
            else nameEl = NameLine(m, room, 13, m.You ? FontWeights.Bold : FontWeights.SemiBold, 10.5, S(White, 0.65), true);
            nameEl.MaxWidth = Math.Max(40, room);
            Grid.SetColumn(nameEl, 2); on.Children.Add(nameEl);
            var marks = Marks(m, 9); Grid.SetColumn(marks, 3); on.Children.Add(marks);
            row.Children.Add(on);
            // the readout: the DPS lit in a dark well, the share under it
            var well = new Border { CornerRadius = new CornerRadius(hero ? 8 : 6), Margin = new Thickness(4, 0, 0, 0),
                Background = V((Color.FromArgb(0xFA, 0x03, 0x04, 0x06), 0), (Color.FromArgb(0xF0, 0x0F, 0x11, 0x16), 1)), BorderBrush = m.You ? S(Gold, 0.8) : V((A(Black, 1), 0), (A(White, 0.13), 1)), BorderThickness = new Thickness(1) };
            var wp = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 7, 0) };
            var dps = T(m.Figure, hero ? 19 : 15.5, FontWeights.Bold, S(m.You ? Gold : White)); dps.HorizontalAlignment = HorizontalAlignment.Right; dps.Effect = Glow(m.You ? Gold : Mix(c, White, 0.5), hero ? 12 : 8, 0.45);
            if (hero) { dps.FontSize = 19; }
            wp.Children.Add(dps);
            if (hero || h >= 28)
            {
                var sh = T(m.ShareText, hero ? 10.5 : 9, FontWeights.SemiBold, S(White, 0.5)); sh.HorizontalAlignment = HorizontalAlignment.Right; sh.Margin = new Thickness(0, hero ? 0 : -3, 0, 0);
                if (hero) wp.Children.Add(sh);
                else { dps.FontSize = 15.5; }
            }
            well.Child = wp;
            Grid.SetColumn(well, 1); row.Children.Add(well);
            stack.Children.Add(row);
        }
        // the foot: two readouts
        var (total, gl, gv, avg) = Foot(e, now);
        var foot = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        foot.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        foot.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) });
        foot.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.25, GridUnitType.Star) });
        foot.Children.Add(Readout(L.T("Total"), total, Ink, false));
        var g = Readout(gl, gv, Gold, avg);
        Grid.SetColumn(g, 2); foot.Children.Add(g);
        stack.Children.Add(foot);
        return root;

        static FrameworkElement Readout(string label, string value, Color ink, bool avg)
        {
            var b = new Border { CornerRadius = new CornerRadius(7), Height = 28, Padding = new Thickness(10, 0, 10, 0), Background = V((Color.FromArgb(0xF0, 0x04, 0x05, 0x07), 0), (Color.FromArgb(0xE8, 0x10, 0x12, 0x18), 1)),
                BorderBrush = V((A(Black, 1), 0), (A(White, 0.12), 1)), BorderThickness = new Thickness(1) };
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.Children.Add(T(label, 10, FontWeights.SemiBold, S(White, 0.5)));
            var r = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            if (avg) { var p = Pill(L.T(Encounter.AvgBadge), 8.5, Mint, Mint, 0.12, 0.55, new Thickness(5, 0, 5, 1)); p.Margin = new Thickness(6, 0, 6, 0); r.Children.Add(p); }
            var v = T(value, 14, FontWeights.Bold, S(ink)); if (ink == Gold) v.Effect = Glow(Gold, 8, 0.4);
            r.Children.Add(v);
            Grid.SetColumn(r, 1); g.Children.Add(r);
            b.Child = g;
            return b;
        }
    }

    /// <summary>A bevelled slab: a recessed track, the fill in the colour lit from its top left with a sheen and a bright end.</summary>
    static FrameworkElement Slab(Color c, double w, double ratio, double h, double r, bool sheen, bool you = false)
    {
        var g = new Grid { Height = h, VerticalAlignment = VerticalAlignment.Center };
        g.Children.Add(new Border { CornerRadius = new CornerRadius(r), Background = V((A(Black, 0.6), 0), (A(White, 0.035), 1)), BorderBrush = you ? S(Gold, 0.85) : V((A(Black, 0.8), 0), (A(White, 0.09), 1)), BorderThickness = new Thickness(you ? 1.3 : 1) });
        double fw = Math.Max(r * 2 + 4, w * ratio);
        var fill = new Grid { Width = fw, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(1) };
        fill.Children.Add(new Border { CornerRadius = new CornerRadius(r), Background = Grad(new Point(0, 0), new Point(0.35, 1), new[] { (Mix(c, White, 0.25), 0.0), (Mix(c, Black, 0.08), 0.45), (Mix(c, Black, 0.5), 1.0) }) });
        if (sheen) fill.Children.Add(new Border { CornerRadius = new CornerRadius(r), Background = Grad(new Point(0, 0), new Point(1, 0.2), new[] { (A(White, 0), 0.0), (A(White, 0.10), 0.62), (A(White, 0), 0.7), (A(White, 0), 1.0) }) });
        fill.Children.Add(new Border { CornerRadius = new CornerRadius(r), BorderBrush = V((A(White, 0.42), 0), (A(White, 0), 0.4), (A(Black, 0.5), 1)), BorderThickness = new Thickness(1) });
        fill.Children.Add(new Border { Width = 3, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 2, 1, 2), CornerRadius = new CornerRadius(1.5), Background = S(Mix(c, White, 0.75)), Effect = Glow(Mix(c, White, 0.4), 8, 0.9) });
        // the bar darkens under where the figures would sit on a near-full slab: never needed here, the readout is apart
        g.Children.Add(fill);
        g.Width = w;
        g.HorizontalAlignment = HorizontalAlignment.Left;
        return g;
    }
}
