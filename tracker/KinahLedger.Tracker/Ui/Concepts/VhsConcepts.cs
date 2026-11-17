using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using KinahLedger.Parser;

namespace KinahLedger.Ui;

/// <summary>
/// VHS reworked (Steven, 2026-11-17: "concept is cool but not legible enough"): three concepts that keep the tape's idea (the
/// on-screen display, REC, the counter, tracking) with crisp, high-contrast figures, the damage bar the hero of each row.
/// The scanlines, noise and tracking stay off the figures. Render-only (ui-shot --parser concepts) until Steven picks one.
/// <list type="bullet">
/// <item>A, Viewfinder: a camcorder's OSD on smoked glass; each member a thick lit bar with the name and DPS on it.</item>
/// <item>B, Deck: a VCR's vacuum-fluorescent display behind a glass bezel; each member a channel with a full-width LED level meter.</item>
/// <item>C, Home video: no panel, the footage's own overlay; amber date-stamp figures with a hard outline, chroma-fringed bars.</item>
/// </list>
/// </summary>
public static partial class MeterConcepts
{
    static partial void AddVhs(List<Concept> l)
    {
        l.Add(new("vhs", "A", "Viewfinder", VhsConcept.Viewfinder));
        l.Add(new("vhs", "B", "Deck", VhsConcept.Deck));
        l.Add(new("vhs", "C", "Home video", VhsConcept.HomeVideo));
    }
}

static class VhsConcept
{
    // ---- the type: a condensed grotesk for figures (crisp, tabular), the OSD's monospace for its labels
    static readonly FontFamily Fig = new("Bahnschrift SemiCondensed, Bahnschrift, Segoe UI");
    static readonly FontFamily Osd = new("Consolas, Lucida Console");
    static readonly FontWeight Bold = FontWeights.Bold, Semi = FontWeights.SemiBold;

    static SolidColorBrush C(uint rgb, double a = 1) { var b = new SolidColorBrush(Col(rgb, a)); b.Freeze(); return b; }
    static Color Col(uint rgb, double a = 1) => Color.FromArgb((byte)Math.Round(255 * a), (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
    static Color A(Color c, double a) => Color.FromArgb((byte)Math.Round(255 * a), c.R, c.G, c.B);
    static Color Mix(Color a, Color b, double t) => Color.FromRgb((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

    static TextBlock T(string s, double size, Brush fg, FontFamily face, FontWeight? w = null)
    {
        var t = new TextBlock { Text = s, FontSize = size, Foreground = fg, FontFamily = face, FontWeight = w ?? FontWeights.Normal, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.NoWrap };
        System.Windows.Documents.Typography.SetNumeralAlignment(t, FontNumeralAlignment.Tabular);
        return t;
    }

    static double W(string s, FontFamily face, double size, FontWeight w) => Math.Ceiling(GlitchKit.TextW(s, face, size, w));
    static DropShadowEffect Glow(Color c, double blur, double opacity, double depth = 0) => new() { Color = c, BlurRadius = blur, Opacity = opacity, ShadowDepth = depth, Direction = 270 };
    /// <summary>Text with a soft dark halo under it (reads over any bar or scene).</summary>
    static TextBlock Haloed(TextBlock t, double blur = 4, double opacity = 0.95) { t.Effect = Glow(Colors.Black, blur, opacity); return t; }

    /// <summary>A text with a hard outline: eight copies a hair off on every side, the fill on top (the camcorder's date stamp).</summary>
    static Grid Outlined(string s, double size, Brush fill, FontFamily face, FontWeight w, double reach = 1.1, Color? edge = null, double maxWidth = double.PositiveInfinity)
    {
        var g = new Grid { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(reach) };
        var ink = new SolidColorBrush(edge ?? Colors.Black); ink.Freeze();
        foreach (var (x, y) in new[] { (reach, 0.0), (-reach, 0.0), (0.0, reach), (0.0, -reach), (reach, reach), (-reach, reach), (reach, -reach), (-reach, -reach) })
        {
            var c = T(s, size, ink, face, w); c.RenderTransform = new TranslateTransform(x, y); c.MaxWidth = maxWidth; c.TextTrimming = TextTrimming.CharacterEllipsis;
            g.Children.Add(c);
        }
        var top = T(s, size, fill, face, w); top.MaxWidth = maxWidth; top.TextTrimming = TextTrimming.CharacterEllipsis;
        g.Children.Add(top);
        return g;
    }

    /// <summary>The members as the meter shows them, and the top figure their bars are measured against.</summary>
    static (IReadOnlyList<MeterMember> Rows, double Top) Members(Encounter e, DateTime now)
    {
        var rows = ThemeRows.Of(e, now, expanded: false);
        double top = rows.Where(m => double.IsFinite(m.FigureValue)).Select(m => m.FigureValue).DefaultIfEmpty(0).Max();
        if (e.HasDamage) top = Math.Max(top, e.Members.Select(f => e.Dps(f, now)).DefaultIfEmpty(0).Max());
        return (rows, top);
    }
    static double Frac(MeterMember m, double top) => top > 0 && double.IsFinite(m.FigureValue) && m.FigureValue > 0 ? Math.Clamp(m.FigureValue / top, 0, 1) : 0;

    static string Name(MeterMember m) => GlitchKit.Caps(m.Name);
    static string More(int n) => L.F("{0} more", n).ToUpper(CultureInfo.CurrentCulture);
    static string Clock(Encounter e, DateTime now) => BossParser.Clock(e.Duration(now));
    static int Left(Encounter e, DateTime now, int shown) => Math.Max(0, e.Shown(now).Count(f => !f.Other) - shown);

    /// <summary>
    /// A name line's parts after the name: what each is, its width; the name keeps a ten-letter name whole, so short of room
    /// the CP goes first (the hover card keeps it), then the heal's figure.
    /// </summary>
    static (bool Cp, bool Heal, double NameRoom) Fit(MeterMember m, double room, double nameW, double cpW, double healW, double marksW)
    {
        bool cp = m.Cp.Length > 0, heal = m.Heal.Length > 0;
        double others() => (cp ? cpW : 0) + (heal ? healW : 0) + marksW;
        double need = nameW;   // the whole name when it is ten letters or fewer
        if (m.Name.Length <= 10)
        {
            if (cp && room - others() < need) cp = false;
            if (heal && room - others() < need) heal = false;
        }
        else if (cp && room - others() < 64) cp = false;   // a long name keeps at least a few letters before its CP
        return (cp, heal, Math.Max(24, room - others()));
    }

    // =====================================================================================================================
    // A. Viewfinder: the camcorder's OSD on smoked glass
    // =====================================================================================================================

    public static FrameworkElement Viewfinder(Encounter e, DateTime now, double width)
    {
        double outer = width + 10, pad = 14;
        var (rows, top) = Members(e, now);
        var white = C(0xFFFFFF); var dim = C(0xFFFFFF, .62); var faint = C(0xFFFFFF, .42);
        var red = Col(0xFF3B30); var amber = Col(0xFFC24A); var mint = Col(0x5CFFA4);

        var root = new Grid { Width = outer, SnapsToDevicePixels = true };
        root.Effect = Glow(Colors.Black, 18, .55, 3);
        // the glass: smoked, a touch lighter at the top, the tape's lines faint behind everything, a vignette, the camcorder's corners
        var glassBg = new LinearGradientBrush(Col(0x14171D, .90), Col(0x07080B, .95), 90); glassBg.Freeze();
        root.Children.Add(new Border { CornerRadius = new CornerRadius(5), Background = glassBg, BorderBrush = C(0xFFFFFF, .11), BorderThickness = new Thickness(1) });
        root.Children.Add(new Border { CornerRadius = new CornerRadius(5), Background = GlitchKit.Lines(Col(0xFFFFFF, .028), 3, 1), IsHitTestVisible = false });
        root.Children.Add(new Border { CornerRadius = new CornerRadius(5), Background = GlitchKit.Vignette(Colors.Black, .5), IsHitTestVisible = false });
        var sheen = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1), GradientStops = { new GradientStop(Col(0xFFFFFF, .07), 0), new GradientStop(Col(0xFFFFFF, 0), .22) } }; sheen.Freeze();
        root.Children.Add(new Border { CornerRadius = new CornerRadius(5), Background = sheen, IsHitTestVisible = false });
        root.Children.Add(GlitchKit.Corners(C(0xFFFFFF, .6), 11, 1.6, 5));
        var body = new StackPanel { Margin = new Thickness(pad, 12, pad, 11) };
        root.Children.Add(body);

        // the head: REC (or STOP) and the tape's counter, big; the fight's name under them
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var rec = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (e.Live)
        {
            rec.Children.Add(new Ellipse { Width = 9, Height = 9, Fill = new SolidColorBrush(red), Effect = Glow(red, 10, .9), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
            rec.Children.Add(Haloed(T(L.T("REC"), 12, white, Osd, Bold)));
        }
        else
        {
            rec.Children.Add(new Rectangle { Width = 8, Height = 8, Fill = white, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
            rec.Children.Add(Haloed(T(L.T("STOP"), 12, white, Osd, Bold)));
        }
        head.Children.Add(rec);
        var tc = T(GlitchKit.Timecode(Clock(e, now)), 22, white, Fig, Bold); tc.Effect = Glow(Colors.White, 12, .35);
        var tcHost = new Border { Child = tc, Effect = Glow(Colors.Black, 6, .9) };
        Grid.SetColumn(tcHost, 2); head.Children.Add(tcHost);
        body.Children.Add(head);
        var title = Haloed(T(GlitchKit.Caps(e.Title), 12.5, C(0xFFFFFF, .9), Osd, Bold)); title.TextTrimming = TextTrimming.CharacterEllipsis; title.Margin = new Thickness(0, 2, 0, 0);
        body.Children.Add(title);

        // the boss's HP as the tape's position, or how it ended
        if (e.BossHpLeft is { } left && e.Live)
        {
            var hp = new Grid { Margin = new Thickness(0, 8, 0, 2) };
            hp.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            hp.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var track = new Grid { Height = 6, VerticalAlignment = VerticalAlignment.Center };
            track.Children.Add(new Border { Background = C(0xFFFFFF, .12), CornerRadius = new CornerRadius(1) });
            var fillB = new LinearGradientBrush(Mix(amber, red, .55), amber, 0); fillB.Freeze();
            var fill = new Border { Background = fillB, CornerRadius = new CornerRadius(1), HorizontalAlignment = HorizontalAlignment.Left, Effect = Glow(amber, 8, .6) };
            track.SizeChanged += (_, _) => fill.Width = Math.Max(2, track.ActualWidth * left);
            track.Children.Add(fill);
            for (int i = 1; i < 4; i++) track.Children.Add(new Border { Width = 1, Background = C(0x000000, .6), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(i * 0.25 * (outer - 2 * pad - 150), 0, 0, 0) });
            hp.Children.Add(track);
            var pct = ParserPanel.HpText(e, 12, white, dim); pct.FontFamily = Fig; pct.Margin = new Thickness(10, 0, 0, 0); Haloed(pct);
            Grid.SetColumn(pct, 1); hp.Children.Add(pct);
            body.Children.Add(hp);
        }
        else if (!e.Live)
        {
            var (text, ink) = ParserPanel.OutcomeOf(e, now);
            var o = T(text.ToUpper(CultureInfo.CurrentCulture), 12, ink, Fig, Bold); o.Margin = new Thickness(0, 6, 0, 0); o.Effect = Glow(ink.Color, 8, .45);
            body.Children.Add(o);
        }

        // the rows: a thick lit bar each, the rank before it and the share after it; the name and the DPS on the bar
        const double rankW = 26, shareW = 44, rowH = 26;
        double trackW = outer - 2 * pad - rankW - shareW;
        var list = new StackPanel { Margin = new Thickness(0, 9, 0, 0) };
        foreach (var m in rows)
        {
            if (m.Pinned)
            {
                int more = Left(e, now, rows.Count);
                var gap = T(more > 0 ? $"·  ·  ·   {More(more)}   ·  ·  ·" : "·  ·  ·", 9.5, faint, Osd, Bold); gap.HorizontalAlignment = HorizontalAlignment.Center; gap.Margin = new Thickness(0, 0, 0, 4);
                list.Children.Add(gap);
            }
            var row = new Grid { Height = rowH, Margin = new Thickness(0, 0, 0, 4) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(rankW) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(trackW) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(shareW) });
            var rank = new Border { Width = 21, Height = 18, BorderThickness = new Thickness(1.2), BorderBrush = m.You ? white : C(0xFFFFFF, .55), Background = m.You ? white : null, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center,
                Child = T(m.Rank.ToString("00", CultureInfo.InvariantCulture), 10.5, m.You ? C(0x000000) : white, Osd, Bold) };
            ((TextBlock)rank.Child).HorizontalAlignment = HorizontalAlignment.Center;
            row.Children.Add(rank);
            // the bar: a dark track the full width, the class's light across their share of the top figure, lit at its head
            var cc = m.ClassColor;
            var track = new Grid { Width = trackW };
            track.Children.Add(new Border { CornerRadius = new CornerRadius(2), Background = C(0xFFFFFF, .05), BorderBrush = m.You ? C(0xFFFFFF, .55) : C(0xFFFFFF, .08), BorderThickness = new Thickness(1) });
            double fw = Math.Round(trackW * Frac(m, top));
            if (fw > 0)
            {
                var g = new LinearGradientBrush(A(Mix(cc, Colors.Black, .45), .92), A(Mix(cc, Colors.Black, .12), .95), 0); g.Freeze();
                var fill = new Grid { Width = fw, HorizontalAlignment = HorizontalAlignment.Left };
                fill.Children.Add(new Border { CornerRadius = new CornerRadius(2), Background = g });
                fill.Children.Add(new Border { Height = 1, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(1, 1, 1, 0), Background = C(0xFFFFFF, .28) });
                // its lit head, unless it would sit under the DPS (a cursor beside the figure)
                if (fw < trackW - 22 - W("199.9K", Fig, 16, Bold)) fill.Children.Add(new Border { Width = 2.5, HorizontalAlignment = HorizontalAlignment.Right, Background = new SolidColorBrush(Mix(cc, Colors.White, .45)), Effect = Glow(cc, 10, .9) });
                track.Children.Add(fill);
            }
            // a healer's share of the group's healing along the foot, mint
            if (m.HealShare > 0) track.Children.Add(new Border { Height = 2, VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Left, Width = Math.Max(3, trackW * m.HealShare), Background = new SolidColorBrush(mint), Margin = new Thickness(1, 0, 0, 1) });
            // on the bar: the name and its marks on the left, the DPS at the right end
            var dps = Haloed(T(m.Figure, 16, white, Fig, Bold), 5, 1);
            double dpsW = Math.Max(W("199.9K", Fig, 16, Bold), W(m.Figure, Fig, 16, Bold));
            dps.HorizontalAlignment = HorizontalAlignment.Right; dps.Margin = new Thickness(0, 0, 7, 0);
            track.Children.Add(dps);
            var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(7, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            var marks = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            if (m.TopDps) marks.Children.Add(Mark("★", C(0xFFD23F), C(0x1A1400)));
            if (m.TopHeals) marks.Children.Add(Mark("✚", new SolidColorBrush(mint), C(0x002611)));
            if (m.Deaths > 0) marks.Children.Add(Mark("✕" + (m.Deaths > 1 ? m.Deaths.ToString(CultureInfo.InvariantCulture) : ""), C(0xFF3B30), white));
            string cpText = m.Cp.Length > 0 ? L.T("CP") + " " + m.Cp : "";
            string healText = m.Heal.Length > 0 ? "+" + m.Heal : "";
            double dot = m.You ? 12 : 0;
            double marksW = marks.Children.Count * 17 + dot;
            double room = trackW - 14 - dpsW - 16;
            var (showCp, showHeal, nameRoom) = Fit(m, room, W(Name(m), Fig, 13, Semi), W(cpText, Fig, 10, Semi) + 6, W(healText, Fig, 11, Bold) + 6, marksW);
            if (m.You) line.Children.Add(new Ellipse { Width = 7, Height = 7, Fill = new SolidColorBrush(red), Effect = Glow(red, 8, .9), Margin = new Thickness(0, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center });
            var name = Haloed(T(Name(m), 13, white, Fig, Semi)); name.MaxWidth = nameRoom; name.TextTrimming = TextTrimming.CharacterEllipsis;
            line.Children.Add(name);
            if (showCp) { var cp = Haloed(T(cpText, 10, C(0xFFFFFF, .66), Fig, Semi), 3, .9); cp.Margin = new Thickness(6, 1, 0, 0); line.Children.Add(cp); }
            if (marks.Children.Count > 0) { marks.Margin = new Thickness(4, 0, 0, 0); line.Children.Add(marks); }
            // a healer's figure on a dark pill, so the mint reads on any class's bar
            if (showHeal) line.Children.Add(new Border { Background = C(0x000000, .55), CornerRadius = new CornerRadius(2), Padding = new Thickness(3, 0, 3, 0), Margin = new Thickness(4, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
                Child = T(healText, 11, new SolidColorBrush(mint), Fig, Bold) });
            track.Children.Add(line);
            Grid.SetColumn(track, 1); row.Children.Add(track);
            var share = T(m.ShareText, 11, dim, Fig, Semi); share.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetColumn(share, 2); row.Children.Add(share);
            list.Children.Add(row);
        }
        body.Children.Add(list);

        // the foot: TOTAL and the group's DPS, the OSD's words small, the figures big
        body.Children.Add(new Border { Height = 1, Background = C(0xFFFFFF, .14), Margin = new Thickness(0, 4, 0, 7) });
        body.Children.Add(Foot(e, now, white, faint, Fig, Osd, C(0xFFD23F), C(0x1A1400)));
        return root;

        static Border Mark(string s, Brush bg, Brush fg) => new()
        {
            Background = bg, CornerRadius = new CornerRadius(2), Width = 15, Height = 14, Margin = new Thickness(2, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = s, FontSize = 9, FontWeight = FontWeights.Bold, Foreground = fg, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FontFamily = new FontFamily("Segoe UI Symbol, Segoe UI") },
        };
    }

    /// <summary>The foot every concept shares the words of: TOTAL 766.26M on the left, the group's DPS (and AVG once held) on the right.</summary>
    static Grid Foot(Encounter e, DateTime now, Brush fig, Brush label, FontFamily figFace, FontFamily labelFace, Brush avgBg, Brush avgFg, Func<TextBlock, FrameworkElement>? wrap = null)
    {
        var f = GlitchKit.FootOf(e, now);
        wrap ??= t => Haloed(t);
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var l = new StackPanel { Orientation = Orientation.Horizontal };
        l.Children.Add(wrap(T(L.T("TOTAL"), 10, label, labelFace, Bold)));
        var tot = wrap(T(f.Total, 15, fig, figFace, Bold)); tot.Margin = new Thickness(6, 0, 0, 0); l.Children.Add(tot);
        g.Children.Add(l);
        var r = new StackPanel { Orientation = Orientation.Horizontal };
        if (f.Label.Length > 0) r.Children.Add(wrap(T(f.Label.ToUpper(CultureInfo.CurrentCulture), 10, label, labelFace, Bold)));
        var d = wrap(T(f.Dps, 15, fig, figFace, Bold)); d.Margin = new Thickness(6, 0, 0, 0); r.Children.Add(d);
        if (f.Held) r.Children.Add(new Border { Background = avgBg, CornerRadius = new CornerRadius(2), Padding = new Thickness(4, 0, 4, 0), Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
            Child = T(L.T("AVG"), 9.5, avgFg, labelFace, Bold) });
        GlitchKit.TagFoot(g, e, now);
        Grid.SetColumn(r, 1); g.Children.Add(r);
        return g;
    }

    // =====================================================================================================================
    // B. Deck: a VCR's vacuum-fluorescent display behind glass
    // =====================================================================================================================

    public static FrameworkElement Deck(Encounter e, DateTime now, double width)
    {
        double outer = width + 10;
        var (rows, top) = Members(e, now);
        var vfd = Col(0x8CF6FF); var vfdHot = Col(0xDDFDFF);
        var cyan = new SolidColorBrush(vfd); cyan.Freeze();
        var hot = new SolidColorBrush(vfdHot); hot.Freeze();
        var cyanDim = C(0x8CF6FF, .5); var cyanFaint = C(0x8CF6FF, .3);
        var amber = Col(0xFFB347); var mint = Col(0x6CFFB4); var red = Col(0xFF5A4E);

        // the bezel: brushed gunmetal, a light top edge, a shadow under it over the game
        var bezelBg = new LinearGradientBrush(Col(0x2B3038), Col(0x111317), 90); bezelBg.Freeze();
        var bezelEdge = new LinearGradientBrush(Col(0x5A616C), Col(0x0A0B0D), 90); bezelEdge.Freeze();
        var bezel = new Border { Width = outer, CornerRadius = new CornerRadius(8), Background = bezelBg, BorderBrush = bezelEdge, BorderThickness = new Thickness(1), Padding = new Thickness(5), Effect = Glow(Colors.Black, 16, .6, 4), SnapsToDevicePixels = true };
        // the glass: near-black, the display's dot grid faint behind the figures, a reflection across its top
        var glass = new Grid();
        glass.Children.Add(new Border { CornerRadius = new CornerRadius(4), Background = C(0x03070A, .97), BorderBrush = C(0x000000), BorderThickness = new Thickness(1) });
        glass.Children.Add(new Border { CornerRadius = new CornerRadius(4), Background = GlitchKit.Lines(Col(0x8CF6FF, .025), 2, 1), IsHitTestVisible = false });
        var refl = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0.35, 1), GradientStops = { new GradientStop(Col(0xFFFFFF, .085), 0), new GradientStop(Col(0xFFFFFF, .02), .38), new GradientStop(Col(0xFFFFFF, 0), .4) } }; refl.Freeze();
        glass.Children.Add(new Border { CornerRadius = new CornerRadius(4), Background = refl, IsHitTestVisible = false });
        bezel.Child = glass;
        var body = new StackPanel { Margin = new Thickness(11, 9, 11, 9) };
        glass.Children.Add(body);
        TextBlock V(string s, double size, Brush b, FontFamily face, FontWeight w, double glow = .55) { var t = T(s, size, b, face, w); t.Effect = Glow(vfd, 8, glow); return t; }

        // the head: the deck's mode lit, the counter big; the tape's title under
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var mode = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        mode.Children.Add(V(e.Live ? L.T("▶ PLAY") : "■ " + L.T("STOP"), 11.5, hot, Osd, Bold, .8));
        var title = V(GlitchKit.Caps(e.Title), 11.5, C(0x8CF6FF, .78), Osd, Bold, .35); title.TextTrimming = TextTrimming.CharacterEllipsis; title.Margin = new Thickness(0, 3, 0, 0); title.MaxWidth = outer - 150;
        mode.Children.Add(title);
        head.Children.Add(mode);
        var counter = V(Clock(e, now), 27, hot, Fig, Bold, .85); counter.Effect = Glow(vfd, 14, .85);
        Grid.SetColumn(counter, 2); head.Children.Add(counter);
        body.Children.Add(head);

        // the boss's HP: an amber level of 48 segments, its figure after it
        if (e.BossHpLeft is { } left && e.Live)
        {
            var hp = new Grid { Margin = new Thickness(0, 7, 0, 0) };
            hp.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            hp.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            hp.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            hp.Children.Add(V(L.T("HP"), 10, C(0xFFB347), Osd, Bold, .3));
            var lvl = Level(7, left, amber, A(amber, .14), 3);
            lvl.Margin = new Thickness(7, 0, 9, 0); Grid.SetColumn(lvl, 1); hp.Children.Add(lvl);
            var pct = ParserPanel.HpText(e, 12, new SolidColorBrush(amber), C(0xFFB347, .7)); pct.FontFamily = Fig; pct.Effect = Glow(amber, 8, .45);
            Grid.SetColumn(pct, 2); hp.Children.Add(pct);
            body.Children.Add(hp);
        }
        else if (!e.Live)
        {
            var (text, ink) = ParserPanel.OutcomeOf(e, now);
            var o = T(text.ToUpper(CultureInfo.CurrentCulture), 12, ink, Fig, Bold); o.Margin = new Thickness(0, 5, 0, 0); o.Effect = Glow(ink.Color, 9, .7);
            body.Children.Add(o);
        }
        body.Children.Add(new Border { Height = 1, Background = cyanFaint, Margin = new Thickness(0, 8, 0, 6), Opacity = .5 });

        // the channels: a member's line (channel, name, CP, marks, DPS, share), the full-width level meter under it
        double inner = outer - 10 - 2 - 22;
        const double chW = 22;
        foreach (var m in rows)
        {
            if (m.Pinned)
            {
                int more = Left(e, now, rows.Count);
                var gap = V(more > 0 ? $"- - -  {More(more)}  - - -" : "- - -", 9.5, cyanFaint, Osd, Bold, .2); gap.HorizontalAlignment = HorizontalAlignment.Center; gap.Margin = new Thickness(0, -1, 0, 4);
                body.Children.Add(gap);
            }
            var cc = Mix(m.ClassColor, Colors.White, .12);
            var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(chW) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.RowDefinitions.Add(new RowDefinition { Height = new GridLength(18) });
            row.RowDefinitions.Add(new RowDefinition { Height = new GridLength(13) });
            // the channel: its number lit; yours inverted
            var ch = new Border { Background = m.You ? hot : null, CornerRadius = new CornerRadius(2), Height = 15, Width = 19, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center,
                Child = V(m.Rank.ToString("00", CultureInfo.InvariantCulture), 10.5, m.You ? C(0x03070A) : cyanDim, Osd, Bold, m.You ? 0 : .3) };
            ((TextBlock)ch.Child).HorizontalAlignment = HorizontalAlignment.Center;
            if (m.You) ch.Effect = Glow(vfd, 10, .7);
            Grid.SetRowSpan(ch, 2); row.Children.Add(ch);
            var line = new Grid();
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });
            double dpsW = Math.Max(W("199.9K", Fig, 16.5, Bold), W(m.Figure, Fig, 16.5, Bold));
            var dps = V(m.Figure, 16.5, hot, Fig, Bold, .6); dps.HorizontalAlignment = HorizontalAlignment.Right; dps.MinWidth = dpsW; dps.TextAlignment = TextAlignment.Right;
            Grid.SetColumn(dps, 1); line.Children.Add(dps);
            var share = V(m.ShareText, 10.5, cyanDim, Fig, Semi, .25); share.HorizontalAlignment = HorizontalAlignment.Right; share.Margin = new Thickness(0, 3, 0, 0);
            Grid.SetColumn(share, 2); line.Children.Add(share);
            var lhs = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var marks = new List<FrameworkElement>();
            if (m.TopDps) marks.Add(Lit("★", C(0xFFD23F), Col(0xFFD23F)));
            if (m.TopHeals) marks.Add(Lit("✚", new SolidColorBrush(mint), mint));
            if (m.Deaths > 0) marks.Add(Lit("✕" + (m.Deaths > 1 ? m.Deaths.ToString(CultureInfo.InvariantCulture) : ""), new SolidColorBrush(red), red));
            string cpText = m.Cp.Length > 0 ? L.T("CP") + " " + m.Cp : "";
            string healText = m.Heal.Length > 0 ? "+" + m.Heal : "";
            double room = inner - dpsW - 42 - 8;
            var (showCp, showHeal, nameRoom) = Fit(m, room, W(Name(m), Osd, 12.5, Bold), W(cpText, Fig, 10.5, Semi) + 7, W(healText, Fig, 11, Bold) + 7, marks.Count * 16);
            var name = V(Name(m), 12.5, m.You ? hot : new SolidColorBrush(Mix(vfdHot, cc, .35)), Osd, Bold, .45); name.MaxWidth = nameRoom; name.TextTrimming = TextTrimming.CharacterEllipsis;
            lhs.Children.Add(name);
            if (showCp) { var cp = V(cpText, 10.5, cyanDim, Fig, Semi, .2); cp.Margin = new Thickness(7, 1, 0, 0); lhs.Children.Add(cp); }
            foreach (var mk in marks) lhs.Children.Add(mk);
            if (showHeal) { var h = T(healText, 11, new SolidColorBrush(mint), Fig, Bold); h.Effect = Glow(mint, 8, .6); h.Margin = new Thickness(7, 0, 0, 0); lhs.Children.Add(h); }
            line.Children.Add(lhs);
            Grid.SetColumn(line, 1); row.Children.Add(line);
            // the level: segments in the class's colour, lit to their share of the top figure, a peak mark held at its head
            var lvl = Level(12, Frac(m, top), cc, A(cc, .13), 3.5, peak: true);
            if (m.HealShare > 0) { var hl = new Grid { Children = { lvl, new Border { Height = 1.5, VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Left, Width = Math.Max(3, (inner) * m.HealShare), Background = new SolidColorBrush(mint), Margin = new Thickness(0, 0, 0, -3) } } }; Grid.SetRow(hl, 1); Grid.SetColumn(hl, 1); row.Children.Add(hl); }
            else { Grid.SetRow(lvl, 1); Grid.SetColumn(lvl, 1); row.Children.Add(lvl); }
            body.Children.Add(row);
        }

        body.Children.Add(new Border { Height = 1, Background = cyanFaint, Margin = new Thickness(0, 2, 0, 6), Opacity = .5 });
        body.Children.Add(Foot(e, now, hot, cyanDim, Fig, Osd, C(0xFFB347), C(0x1A0E00), t => { t.Effect = Glow(vfd, 8, .5); return t; }));
        return bezel;

        static Border Lit(string s, Brush b, Color c) => new()
        {
            Margin = new Thickness(5, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = s, FontSize = 11, FontWeight = FontWeights.Bold, Foreground = b, FontFamily = new FontFamily("Segoe UI Symbol, Segoe UI"), Effect = new DropShadowEffect { Color = c, BlurRadius = 8, ShadowDepth = 0, Opacity = .8 } },
        };
    }

    /// <summary>An LED level: segments <paramref name="seg"/> px apart, lit to <paramref name="frac"/> (glowing), unlit dim; a peak mark past its head.</summary>
    static FrameworkElement Level(double h, double frac, Color lit, Color unlit, double seg, bool peak = false)
    {
        var g = new Grid { Height = h, VerticalAlignment = VerticalAlignment.Center, ClipToBounds = false };
        var cut = GlitchKit.Segments(Col(0x03070A), seg, 1);
        g.Children.Add(new Border { Background = new SolidColorBrush(unlit) });
        var hotC = Mix(lit, Colors.White, .25);
        var fillB = new LinearGradientBrush(A(lit, .85), hotC, 0); fillB.Freeze();
        var fill = new Border { Background = fillB, HorizontalAlignment = HorizontalAlignment.Left, Effect = new DropShadowEffect { Color = lit, BlurRadius = 9, ShadowDepth = 0, Opacity = .75 } };
        g.Children.Add(fill);
        Border? pk = peak && frac > 0 ? new Border { Width = 2.5, HorizontalAlignment = HorizontalAlignment.Left, Background = new SolidColorBrush(Mix(lit, Colors.White, .6)) } : null;
        if (pk != null) g.Children.Add(pk);
        g.Children.Add(new Border { Background = cut, IsHitTestVisible = false });
        g.SizeChanged += (_, _) =>
        {
            double w = g.ActualWidth;
            fill.Width = Math.Max(frac > 0 ? seg : 0, Math.Round(w * frac / seg) * seg);
            if (pk != null) pk.Margin = new Thickness(Math.Min(w - 2.5, fill.Width + seg * 1.5), 0, 0, 0);
        };
        return g;
    }

    // =====================================================================================================================
    // C. Home video: the footage's own overlay, no panel
    // =====================================================================================================================

    public static FrameworkElement HomeVideo(Encounter e, DateTime now, double width)
    {
        double outer = width + 10;
        var (rows, top) = Members(e, now);
        var stamp = Col(0xFFC23A);
        var stampB = new SolidColorBrush(stamp); stampB.Freeze();
        var white = C(0xFFFFFF); var soft = C(0xF2F2F2, .92);
        var mint = Col(0x63FFA8); var red = Col(0xFF4A3D);

        var root = new Grid { Width = outer, SnapsToDevicePixels = true };
        // no panel: a soft dark scrim under the figures (the footage shows through), wider than the meter so it has no edge
        var scrim = new RadialGradientBrush { Center = new Point(.5, .5), GradientOrigin = new Point(.5, .5), RadiusX = .62, RadiusY = .6,
            GradientStops = { new GradientStop(Col(0x000000, .55), 0), new GradientStop(Col(0x000000, .42), .6), new GradientStop(Col(0x000000, 0), 1) } }; scrim.Freeze();
        root.Children.Add(new Border { Background = scrim, Margin = new Thickness(-26, -20, -26, -20), IsHitTestVisible = false });
        var body = new StackPanel { Margin = new Thickness(4, 2, 4, 2) };
        root.Children.Add(body);

        // the tracking band at the very top, over nothing but the footage
        var band = new Grid { Height = 7, Margin = new Thickness(-14, 0, -14, 6), Opacity = .55, IsHitTestVisible = false };
        band.Children.Add(new Border { Background = GlitchKit.NoiseBrush(.6) });
        band.Children.Add(new Border { Height = 1, VerticalAlignment = VerticalAlignment.Top, Background = C(0xFFFFFF, .5), Margin = new Thickness(0, 2, 0, 0) });
        body.Children.Add(band);

        // the head: PLAY ▶ and SP as the camcorder says them, the counter in the date stamp's amber
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var hl = new StackPanel();
        hl.Children.Add(Outlined(e.Live ? L.T("▶ PLAY") : "■ " + L.T("STOP"), 13, white, Osd, Bold));
        hl.Children.Add(Outlined(GlitchKit.Caps(e.Title), 12.5, soft, Osd, Bold, maxWidth: outer - 120));
        head.Children.Add(hl);
        var counter = Outlined(GlitchKit.Timecode(Clock(e, now)), 21, stampB, Fig, Bold, 1.4); counter.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn(counter, 1); head.Children.Add(counter);
        body.Children.Add(head);

        if (e.BossHpLeft is { } left && e.Live)
        {
            var hp = new Grid { Margin = new Thickness(0, 6, 0, 0) };
            hp.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            hp.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var track = new Grid { Height = 6, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(1, 0, 0, 0) };
            track.Children.Add(new Border { Background = C(0x000000, .55), BorderBrush = C(0xFFFFFF, .55), BorderThickness = new Thickness(1) });
            var fill = new Border { Background = stampB, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(1) };
            track.SizeChanged += (_, _) => fill.Width = Math.Max(2, (track.ActualWidth - 2) * left);
            track.Children.Add(fill);
            hp.Children.Add(track);
            string pctText = $"{Math.Round(left * 100):0}%  {ParserPanel.HpLeft(e)}";
            var pct = Outlined(pctText, 12, white, Fig, Bold); pct.Margin = new Thickness(9, 0, 0, 0);
            Grid.SetColumn(pct, 1); hp.Children.Add(pct);
            body.Children.Add(hp);
        }
        else if (!e.Live)
        {
            var (text, ink) = ParserPanel.OutcomeOf(e, now);
            var o = Outlined(text.ToUpper(CultureInfo.CurrentCulture), 12.5, ink, Fig, Bold); o.HorizontalAlignment = HorizontalAlignment.Left; o.Margin = new Thickness(0, 4, 0, 0);
            body.Children.Add(o);
        }

        // the rows: the rank big in the stamp's amber; the name over a thick bar with a chroma fringe; the DPS big after the bar
        const double rankW = 24, dpsCol = 64;
        double barW = outer - 8 - rankW - dpsCol - 6;
        var list = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        foreach (var m in rows)
        {
            if (m.Pinned)
            {
                int more = Left(e, now, rows.Count);
                var gap = Outlined(more > 0 ? $"▼ {More(more)}" : "▼", 9.5, C(0xFFFFFF, .8), Osd, Bold, .9); gap.HorizontalAlignment = HorizontalAlignment.Center; gap.Margin = new Thickness(0, -2, 0, 3);
                list.Children.Add(gap);
            }
            var row = new Grid { Margin = new Thickness(0, 0, 0, 5) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(rankW) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(barW) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(dpsCol) });
            var rank = Outlined(m.Rank.ToString(CultureInfo.InvariantCulture), 17, m.You ? white : stampB, Fig, Bold, 1.3); rank.HorizontalAlignment = HorizontalAlignment.Left;
            row.Children.Add(rank);
            var col = new StackPanel();
            // the name's line
            var line = new StackPanel { Orientation = Orientation.Horizontal, Height = 17 };
            var marks = new List<FrameworkElement>();
            if (m.TopDps) marks.Add(Outlined("★", 11, stampB, new FontFamily("Segoe UI Symbol"), Bold));
            if (m.TopHeals) marks.Add(Outlined("✚", 11, new SolidColorBrush(mint), new FontFamily("Segoe UI Symbol"), Bold));
            if (m.Deaths > 0) marks.Add(Outlined("✕" + (m.Deaths > 1 ? m.Deaths.ToString(CultureInfo.InvariantCulture) : ""), 11, new SolidColorBrush(red), new FontFamily("Segoe UI Symbol, Segoe UI"), Bold));
            string cpText = m.Cp.Length > 0 ? L.T("CP") + " " + m.Cp : "";
            string healText = m.Heal.Length > 0 ? "+" + m.Heal : "";
            var (showCp, showHeal, nameRoom) = Fit(m, barW - 4, W(Name(m), Osd, 12.5, Bold) + 3, W(cpText, Fig, 10.5, Semi) + 9, W(healText, Fig, 11, Bold) + 9, marks.Count * 18);
            if (m.You) line.Children.Add(new Ellipse { Width = 7, Height = 7, Fill = new SolidColorBrush(red), Stroke = Brushes.Black, StrokeThickness = 1, Margin = new Thickness(0, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center });
            line.Children.Add(Outlined(Name(m), 12.5, white, Osd, Bold, maxWidth: nameRoom - (m.You ? 11 : 0)));
            if (showCp) { var cp = Outlined(cpText, 10.5, C(0xFFFFFF, .85), Fig, Semi, .9); cp.Margin = new Thickness(6, 1, 0, 0); line.Children.Add(cp); }
            foreach (var mk in marks) { mk.Margin = new Thickness(5, 0, 0, 0); line.Children.Add(mk); }
            if (showHeal) { var h = Outlined(healText, 11, new SolidColorBrush(mint), Fig, Bold); h.Margin = new Thickness(6, 0, 0, 0); line.Children.Add(h); }
            col.Children.Add(line);
            // the bar: thick, the class's colour solid, its edges fringed red and cyan like a tape's chroma, a black keyline round it
            double fw = Math.Round(barW * Frac(m, top));
            var bar = new Grid { Height = 14, Margin = new Thickness(0, 2, 0, 0) };
            bar.Children.Add(new Border { Background = C(0x000000, .5), BorderBrush = C(0x000000, .85), BorderThickness = new Thickness(1) });
            if (fw > 0)
            {
                var cc = m.ClassColor;
                bar.Children.Add(new Border { Width = fw, HorizontalAlignment = HorizontalAlignment.Left, Background = C(0xFF2D55, .55), Margin = new Thickness(-1.5, 0, 0, 0) });
                bar.Children.Add(new Border { Width = fw, HorizontalAlignment = HorizontalAlignment.Left, Background = C(0x2DE2FF, .5), Margin = new Thickness(1.5, 0, 0, 0) });
                var g = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1), GradientStops = { new GradientStop(Mix(cc, Colors.White, .35), 0), new GradientStop(cc, .45), new GradientStop(Mix(cc, Colors.Black, .3), 1) } }; g.Freeze();
                bar.Children.Add(new Border { Width = fw, HorizontalAlignment = HorizontalAlignment.Left, Background = g, BorderBrush = C(0x000000, .85), BorderThickness = new Thickness(1) });
            }
            if (m.HealShare > 0) bar.Children.Add(new Border { Height = 2, VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Left, Width = Math.Max(3, barW * m.HealShare), Background = new SolidColorBrush(mint), Margin = new Thickness(1, 0, 0, 1) });
            col.Children.Add(bar);
            Grid.SetColumn(col, 1); row.Children.Add(col);
            // the DPS big in the stamp's amber (yours white), the share small under it
            var figs = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right };
            var dps = Outlined(m.Figure, 17, m.You ? white : stampB, Fig, Bold, 1.3); dps.HorizontalAlignment = HorizontalAlignment.Right;
            figs.Children.Add(dps);
            var share = Outlined(m.ShareText, 10, C(0xFFFFFF, .85), Fig, Semi, .9); share.HorizontalAlignment = HorizontalAlignment.Right; share.Margin = new Thickness(0, -3, 1, 0);
            figs.Children.Add(share);
            Grid.SetColumn(figs, 3); row.Children.Add(figs);
            list.Children.Add(row);
        }
        body.Children.Add(list);

        // the foot, then the camcorder's date stamp in the corner: the day the fight was taped
        body.Children.Add(Foot(e, now, white, C(0xFFFFFF, .85), Fig, Osd, stampB, C(0x1A0E00), t => { var s = Outlined(t.Text, t.FontSize, t.Foreground, t.FontFamily, t.FontWeight); return s; }));
        var date = e.Start.ToLocalTime().ToString("MMM. d yyyy", CultureInfo.CurrentCulture).ToUpper(CultureInfo.CurrentCulture);
        var ds = Outlined(date, 13, stampB, Fig, Bold, 1.2); ds.HorizontalAlignment = HorizontalAlignment.Right; ds.Margin = new Thickness(0, 4, 0, 0);
        body.Children.Add(ds);
        return root;
    }
}
