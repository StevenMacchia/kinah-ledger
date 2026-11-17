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
/// Goo's three concepts (Steven, 2026-11-17: "clear, legible and fucking incredible cinematic production quality"): the
/// candy jelly kept, each row's bar the hero. Render only (ui-shot --parser concepts); nothing here is offered in Settings.
/// <list type="bullet">
/// <item>A, Glass tubes: plum glass; each member a lab tube filled with lit jelly to their DPS, a gumball for the rank.</item>
/// <item>B, Sugar frost: a frosted cream plate, dark ink; a thick glossy gummy strip under each name, the full row's width.</item>
/// <item>C, Licorice glow: black licorice; sugar-coated gummy worms that glow in the class's colour, the DPS in its own column.</item>
/// </list>
/// </summary>
public static partial class MeterConcepts
{
    static partial void AddGoo(List<Concept> l)
    {
        l.Add(new Concept("goo", "A", "Glass tubes", (e, now, w) => GooC.Tubes(e, now, w)));
        l.Add(new Concept("goo", "B", "Sugar frost", (e, now, w) => GooC.Frost(e, now, w)));
        l.Add(new Concept("goo", "C", "Licorice glow", (e, now, w) => GooC.Licorice(e, now, w)));
    }
}

/// <summary>The Goo concepts' drawing (Ui/Concepts/GooConcepts.cs).</summary>
static class GooC
{
    // ---- type and colour ----

    static readonly FontFamily Black = new("Segoe UI Black, Segoe UI"), Ui = new("Segoe UI"), Bahn = new("Bahnschrift, Segoe UI");

    static Color Rgb(uint c, double a = 1) => Color.FromArgb((byte)Math.Round(255 * Math.Clamp(a, 0, 1)), (byte)(c >> 16), (byte)(c >> 8), (byte)c);
    static SolidColorBrush S(uint c, double a = 1) { var b = new SolidColorBrush(Rgb(c, a)); b.Freeze(); return b; }
    static LinearGradientBrush V(params (uint C, double A, double At)[] stops) => Lg(0, 0, 0, 1, stops);
    static LinearGradientBrush H(params (uint C, double A, double At)[] stops) => Lg(0, 0, 1, 0, stops);
    static LinearGradientBrush Lg(double x0, double y0, double x1, double y1, params (uint C, double A, double At)[] stops)
    {
        var b = new LinearGradientBrush { StartPoint = new Point(x0, y0), EndPoint = new Point(x1, y1) };
        foreach (var (c, a, at) in stops) b.GradientStops.Add(new GradientStop(Rgb(c, a), at));
        b.Freeze(); return b;
    }
    static RadialGradientBrush R(double cx, double cy, double rx, double ry, params (uint C, double A, double At)[] stops)
    {
        var b = new RadialGradientBrush { Center = new Point(cx, cy), GradientOrigin = new Point(cx, cy), RadiusX = rx, RadiusY = ry };
        foreach (var (c, a, at) in stops) b.GradientStops.Add(new GradientStop(Rgb(c, a), at));
        b.Freeze(); return b;
    }

    static uint Hex(Color c) => (uint)(c.R << 16 | c.G << 8 | c.B);
    static uint Candy(Color c) => Wk.Vivid(Hex(c), .72, .95);
    static uint Shade(uint c, double t) => Wk.Shade(c, t);

    static TextBlock T(string s, double size, FontWeight w, Brush fg, FontFamily f, bool tab = true, FontStretch? st = null)
    {
        var t = new TextBlock { Text = s, FontSize = size, FontWeight = w, Foreground = fg, FontFamily = f, VerticalAlignment = VerticalAlignment.Center, FontStretch = st ?? FontStretches.Normal };
        if (tab) Typography.SetNumeralAlignment(t, FontNumeralAlignment.Tabular);
        TextOptions.SetTextFormattingMode(t, TextFormattingMode.Display);
        return t;
    }

    static DropShadowEffect Halo(double blur = 4, double op = .9, uint c = 0x000000) => new() { Color = Rgb(c), BlurRadius = blur, ShadowDepth = 0, Opacity = op };
    static DropShadowEffect Drop(double blur, double depth, double op, uint c = 0x000000) => new() { Color = Rgb(c), BlurRadius = blur, ShadowDepth = depth, Direction = 270, Opacity = op };

    /// <summary>A text's width laid out (the screen may be at 125%: a little over, never under).</summary>
    static double Wd(string s, double size, FontWeight w, FontFamily f, FontStretch? st = null) =>
        new FormattedText(s, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface(f, FontStyles.Normal, w, st ?? FontStretches.Normal), size, Brushes.Black, 1.25).WidthIncludingTrailingWhitespace * 1.03 + 1;

    static readonly string[] DpsForms = { "888.8K", "8.88M", "888,888" }, ShareForms = { "88.8%", "100%" };
    static double Widest(string[] forms, double size, FontWeight w, FontFamily f, FontStretch? st = null) => forms.Select(x => Wd(x, size, w, f, st)).Max();

    // ---- the fight's parts, the same in each concept ----

    /// <summary>The rows (the meter's rules: five, you pinned), each one's bar 0 to 1 against the top member, and how many are left out.</summary>
    static (List<MeterMember> Rows, Dictionary<long, double> Bar, int Left) Rows(Encounter e, DateTime now)
    {
        var rows = ThemeRows.Of(e, now, expanded: false);
        double V(MeterMember m) => m.CpOnly ? m.F.CombatPower : double.IsNaN(m.FigureValue) ? 0 : m.FigureValue;
        double top = Math.Max(1, rows.Select(V).DefaultIfEmpty(1).Max());
        int left = Math.Max(0, e.Shown(now).Count(f => !f.Other) - rows.Count);
        return (rows, rows.ToDictionary(m => m.Id, m => Math.Clamp(V(m) / top, 0, 1)), rows.Any(m => m.Pinned) ? left : 0);
    }

    /// <summary>"Total 766.26M", "Group 999K", and the held figure's badge ("Avg. DPS") when it has one: the foot's three parts.</summary>
    static (string Label, string Fig)[] FootParts(Encounter e, DateTime now)
    {
        var parts = e.FootText(now).Split(" · ");
        return parts.Select(p => { int i = p.LastIndexOf(' '); return i > 0 && p.Any(char.IsDigit) ? (p[..i], p[(i + 1)..]) : (p, ""); }).ToArray();
    }

    /// <summary>
    /// The name's line: the name, then Combat Power and a healer's healing a second, in <paramref name="room"/> px. A name of up to
    /// 10 letters always shows whole: short of room, Combat Power goes first, then the heal (the hover card keeps both);
    /// a longer name trims with "…".
    /// </summary>
    static FrameworkElement NameLine(MeterMember m, double room, double size, Brush ink, Brush cpInk, Brush healInk, bool halo, FontWeight? weight = null, Brush? healBg = null, Brush? healEdge = null)
    {
        var w = weight ?? FontWeights.Bold;
        var name = T(m.Name, size, w, ink, Ui, false); name.TextTrimming = TextTrimming.CharacterEllipsis;
        string cp = m.CpOnly || m.Cp.Length == 0 ? "" : m.Cp, heal = m.Heal.Length > 0 ? "+" + m.Heal : "";
        double cpW = cp.Length > 0 ? Wd("CP ", size - 4, FontWeights.Bold, Bahn, FontStretches.Condensed) + Wd(cp, size - 2, FontWeights.SemiBold, Ui) + 7 : 0;
        double healW = heal.Length > 0 ? Wd(heal, size - 2.5, FontWeights.Bold, Ui) + 7 + 12 : 0;
        double keep = Wd(m.Name.Length <= 10 ? m.Name : m.Name[..6] + "…", size, w, Ui);
        if (room - cpW - healW < keep) cpW = 0;
        if (room - cpW - healW < keep) healW = 0;
        var line = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        name.MaxWidth = Math.Max(keep, room - cpW - healW);
        if (halo) name.Effect = Halo();
        line.Children.Add(name);
        if (cpW > 0)
        {
            var t = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(7, 1, 0, 0), FontFamily = Ui };
            Typography.SetNumeralAlignment(t, FontNumeralAlignment.Tabular);
            t.Inlines.Add(new Run("CP ") { FontSize = size - 4, FontWeight = FontWeights.Bold, FontFamily = Bahn, FontStretch = FontStretches.Condensed, Foreground = cpInk });
            t.Inlines.Add(new Run(cp) { FontSize = size - 2, FontWeight = FontWeights.SemiBold, Foreground = cpInk });
            if (halo) t.Effect = Halo(4, .9);
            line.Children.Add(t);
        }
        if (healW > 0)
        {
            // a healer's healing a second on a little pill of its own, so it reads on any colour under it
            var h = T(heal, size - 2.5, FontWeights.Bold, healInk, Ui); h.Margin = new Thickness(0, 0, 0, 1);
            var pill = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(6, 0, 6, 0), Margin = new Thickness(7, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
                Background = healBg ?? S(0x06140C, .72), BorderBrush = healEdge ?? S(0x7CFFBE, .55), BorderThickness = new Thickness(1), Child = h };
            line.Children.Add(pill);
        }
        return line;
    }

    static string Title(Encounter e) => e.Title;
    static string Clock(Encounter e, DateTime now) => BossParser.Clock(e.Duration(now));

    /// <summary>A small round candy mark: a star (TOP DPS), a cross (TOP HEALS), or a death count, lit from the top.</summary>
    static FrameworkElement Mark(string glyph, uint c, uint ink, double d, string tip)
    {
        var g = new Grid { Width = glyph.Length > 1 ? double.NaN : d, Height = d, ToolTip = tip };
        g.Children.Add(new Border { CornerRadius = new CornerRadius(d / 2), Background = R(.4, .3, .8, .9, (Shade(c, .45), 1, 0), (c, 1, .55), (Shade(c, -.3), 1, 1)), BorderBrush = S(Shade(c, -.45), .9), BorderThickness = new Thickness(1), Effect = Drop(3, 1, .55) });
        g.Children.Add(new Border { CornerRadius = new CornerRadius(d / 2), Margin = new Thickness(d * .18, 1.5, d * .18, d * .5), Background = V((0xFFFFFF, .55, 0), (0xFFFFFF, 0, 1)) });
        var t = T(glyph, d * .58, FontWeights.Black, S(ink), Black); t.HorizontalAlignment = HorizontalAlignment.Center; t.Margin = new Thickness(glyph.Length > 1 ? 4 : 0, 0, glyph.Length > 1 ? 4 : 0, 1);
        g.Children.Add(t);
        return g;
    }

    /// <summary>The figure columns' widths for a look's DPS and share type.</summary>
    static (double Dps, double Share) Cols(double dpsSize, FontFamily dpsFace, double shareSize, FontFamily shareFace, FontStretch? shareSt = null) =>
        (Widest(DpsForms, dpsSize, FontWeights.Black, dpsFace) + 4, Math.Max(Widest(ShareForms, shareSize, FontWeights.SemiBold, shareFace, shareSt), Wd(L.T("CP"), shareSize, FontWeights.SemiBold, shareFace, shareSt)) + 6);

    static Grid Cols(params GridLength[] ws) { var g = new Grid(); foreach (var w in ws) g.ColumnDefinitions.Add(new ColumnDefinition { Width = w }); return g; }
    static T At<T>(Grid g, T el, int col, int span = 1) where T : UIElement { Grid.SetColumn(el, col); Grid.SetColumnSpan(el, span); g.Children.Add(el); return el; }
    static GridLength Px(double v) => new(v);
    static GridLength Star(double v = 1) => new(Math.Max(0.0001, v), GridUnitType.Star);

    /// <summary>A fill to <paramref name="share"/> of its track: two star columns, so it needs no size to be known.</summary>
    static Grid Fill(UIElement fill, double share, double min)
    {
        var g = Cols(Star(share), Star(1 - share));
        g.ColumnDefinitions[0].MinWidth = min;
        At(g, fill, 0);
        return g;
    }

    // =====================================================================================================================
    // A, Glass tubes
    // =====================================================================================================================

    public static FrameworkElement Tubes(Encounter e, DateTime now, double width)
    {
        double W = width + 10, pad = 12, inner = W - 2 * pad - 3;
        var root = new StackPanel();
        var panel = new Border
        {
            Width = W, CornerRadius = new CornerRadius(18), Padding = new Thickness(pad, 10, pad, 10), BorderThickness = new Thickness(1.5),
            Background = V((0x3C1670, .95, 0), (0x271050, .95, .45), (0x170834, .96, 1)),
            BorderBrush = V((0xD3B2FF, .95, 0), (0x7A45C8, .8, .35), (0x3A1670, .9, 1)),
            Effect = Drop(22, 6, .6), Child = root,
        };
        var face = new Grid { Children = { panel } };
        // the glass's sheen: a soft bright band across the top, a faint violet bloom low on the left
        var sheen = new Border { CornerRadius = new CornerRadius(17, 17, 40, 40), Height = 38, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(2, 2, 2, 0), IsHitTestVisible = false, Background = V((0xFFFFFF, .13, 0), (0xFFFFFF, 0, 1)) };
        face.Children.Add(sheen);

        // the head: title, then the clock in a butterscotch lozenge (mint when it ended)
        var head = Cols(Star(), GridLength.Auto, GridLength.Auto);
        var title = T(Title(e), 15, FontWeights.Black, S(0xFFFFFF), Black, false); title.TextTrimming = TextTrimming.CharacterEllipsis; title.Effect = Drop(3, 1, .6);
        At(head, title, 0);
        if (!e.Live)
        {
            var (_, ink) = ParserPanel.OutcomeOf(e, now);
            var chip = Lozenge(L.T("Killed"), e.Outcome is "kill" or "cleared" ? 0x5CF2A8u : 0xFF7A8Au, 0x07301Au, 12);
            chip.Margin = new Thickness(8, 0, 6, 0); At(head, chip, 1);
        }
        var clock = Lozenge(Clock(e, now), 0xFFC94Au, 0x3A1800u, 15); clock.Margin = new Thickness(e.Live ? 8 : 0, 0, 0, 0);
        At(head, clock, 2);
        root.Children.Add(head);

        // the boss's HP: a coral-to-gold jelly tube
        if (e.Live && e.BossHpLeft is { } hp)
        {
            var g = Cols(Star(), GridLength.Auto); g.Margin = new Thickness(0, 9, 0, 1);
            At(g, Tube(hp, hp < .34 ? 0xFF5E7Au : 0xFFB54Au, 12, 0, false), 0);
            var t = ParserPanel.HpText(e, 12, S(0xFFE6A8), S(0xD9C6F2)); t.Margin = new Thickness(10, 0, 0, 0); t.Effect = Halo(3, .6);
            At(g, t, 1);
            root.Children.Add(g);
        }

        // the rows: the rank's gumball, then the tube the row's width, the words on it
        var (rows, bars, left) = Rows(e, now);
        var (dpsW, shareW) = Cols(17, Black, 11, Bahn, FontStretches.SemiCondensed);
        const double rowH = 30, ball = 26;
        var list = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        foreach (var m in rows)
        {
            if (m.Pinned && left > 0) list.Children.Add(MoreLine(left, 0xCDB6F2, .55));
            uint c = Candy(m.ClassColor);
            // the tube the row's width (the share after it, on the glass), the rank's gumball over its left end
            var row = Cols(Star(), Px(shareW + 4));
            row.Height = rowH; row.Margin = new Thickness(0, 0, 0, 5);
            var tube = Tube(bars[m.Id], c, rowH, ball - 6, m.You);
            tube.Margin = new Thickness(ball - 8, 0, 0, 0);
            At(row, tube, 0);
            // the words on the tube: the name's line, the DPS big at its end
            double nameRoom = inner - shareW - 4 - (ball + 6) - 10 - dpsW - 6;
            var words = Cols(Star(), Px(dpsW)); words.Margin = new Thickness(ball + 6, 0, 10, 0);
            At(words, NameLine(m, nameRoom, 12.5, S(0xFFFFFF), S(0xFFFFFF, .9), S(0x8CFFC4), true), 0);
            var dps = T(m.Figure, m.CpOnly ? 14 : 17, FontWeights.Black, S(0xFFFFFF), Black); dps.HorizontalAlignment = HorizontalAlignment.Right; dps.Effect = Halo(5, .95);
            At(words, dps, 1);
            At(row, words, 0);
            var share = T(m.ShareText, 11, FontWeights.SemiBold, S(0xD9C6F2), Bahn, true, FontStretches.SemiCondensed); share.HorizontalAlignment = HorizontalAlignment.Right; share.Margin = new Thickness(0, 1, 0, 0);
            At(row, share, 1);
            At(row, Gumball(m, c, ball), 0);
            list.Children.Add(row);
        }
        root.Children.Add(list);
        root.Children.Add(Foot(e, now, S(0xCDB6F2, .85), S(0xFFFFFF), 0x5CF2A8, V((0xFFFFFF, 0, 0), (0xC9A6FF, .5, .5), (0xFFFFFF, 0, 1))));
        return face;
    }

    /// <summary>A rounded label lit from the top (the clock, Killed).</summary>
    static Border Lozenge(string s, uint c, uint ink, double size)
    {
        var t = T(s, size, FontWeights.Black, S(ink), Black); t.HorizontalAlignment = HorizontalAlignment.Center;
        var g = new Grid { Children = { t } };
        g.Children.Insert(0, new Border { CornerRadius = new CornerRadius(8), Margin = new Thickness(-6, -1, -6, size * .55), Background = V((0xFFFFFF, .55, 0), (0xFFFFFF, 0, 1)) });
        return new Border
        {
            CornerRadius = new CornerRadius(10), Padding = new Thickness(9, 1, 9, 2), VerticalAlignment = VerticalAlignment.Center, Child = g,
            Background = V((Shade(c, .35), 1, 0), (c, 1, .5), (Shade(c, -.25), 1, 1)), BorderBrush = S(Shade(c, -.5), .9), BorderThickness = new Thickness(1),
            Effect = Drop(6, 2, .5),
        };
    }

    /// <summary>A glass tube with jelly to <paramref name="share"/>: the dark glass, the lit jelly (bright core, deep foot, a meniscus), the gloss over it.</summary>
    static Grid Tube(double share, uint c, double h, double padLeft, bool you)
    {
        double r = h / 2;
        var g = new Grid { Height = h };
        g.Children.Add(new Border { CornerRadius = new CornerRadius(r), Background = V((0x0A0316, .85, 0), (0x1C0A36, .8, .6), (0x2E1652, .8, 1)),
            BorderBrush = you ? V((0xFFE59A, 1, 0), (0xE7A93A, 1, 1)) : V((0xFFFFFF, .22, 0), (0xFFFFFF, .06, 1)), BorderThickness = new Thickness(you ? 1.6 : 1) });
        if (you) g.Children[0].SetValue(UIElement.EffectProperty, Halo(9, .55, 0xFFC24A));
        var jelly = new Grid();
        jelly.Children.Add(new Border { CornerRadius = new CornerRadius(r - 1), Background = V((Shade(c, .32), 1, 0), (c, 1, .42), (Shade(c, -.38), 1, 1)), Effect = Halo(12, .5, c) });
        jelly.Children.Add(new Border { CornerRadius = new CornerRadius(r - 1), Height = h * .3, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(r * .5, 0, r * .5, 2), Background = V((Shade(c, .5), 0, 0), (Shade(c, .55), .45, 1)) });   // light caught low in the jelly
        jelly.Children.Add(new Border { CornerRadius = new CornerRadius(r - 1), Background = R(.5, .38, .55, .9, (0xFFFFFF, .28, 0), (0xFFFFFF, 0, 1)) });
        jelly.Children.Add(new Border { CornerRadius = new CornerRadius(r - 1), Background = H((Shade(c, -.45), .5, 0), (c, 0, .2), (c, 0, .85), (0xFFFFFF, .35, 1)) });   // dark at the start, the meniscus lit at the end
        var fill = Fill(jelly, share, h + padLeft);
        fill.Margin = new Thickness(1.5);
        g.Children.Add(fill);
        // the glass's gloss: a bright streak along the top, a faint rim low
        g.Children.Add(new Border { CornerRadius = new CornerRadius(r - 2, r - 2, 4, 4), Height = h * .36, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(r * .6, 2.5, r * .6, 0), Background = V((0xFFFFFF, .5, 0), (0xFFFFFF, .05, 1)), IsHitTestVisible = false });
        g.Children.Add(new Border { Height = 1, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(r, 0, r, 2.5), Background = H((0xFFFFFF, 0, 0), (0xFFFFFF, .25, .5), (0xFFFFFF, 0, 1)) });
        return g;
    }

    /// <summary>The rank as a gumball in the class's candy colour (gold for you), TOP DPS's star and TOP HEALS's cross on it, deaths under it.</summary>
    static FrameworkElement Gumball(MeterMember m, uint c, double d)
    {
        uint b = m.You ? 0xFFC23Au : c;
        var g = new Grid { HorizontalAlignment = HorizontalAlignment.Left };
        var ball = new Grid { Width = d, Height = d, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        ball.Children.Add(new Ellipse { Fill = R(.38, .32, .75, .75, (Shade(b, .55), 1, 0), (b, 1, .5), (Shade(b, -.45), 1, 1)), Stroke = S(Shade(b, -.55)), StrokeThickness = 1, Effect = Drop(5, 2, .65) });
        ball.Children.Add(new Ellipse { Width = d * .42, Height = d * .26, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(d * .2, d * .1, 0, 0), Fill = V((0xFFFFFF, .85, 0), (0xFFFFFF, 0, 1)) });
        var n = T(m.Rank.ToString(CultureInfo.InvariantCulture), 12.5, FontWeights.Black, S(0xFFFFFF), Black); n.HorizontalAlignment = HorizontalAlignment.Center; n.Effect = Halo(3, .9); n.Margin = new Thickness(0, 0, 0, 1);
        ball.Children.Add(n);
        g.Children.Add(ball);
        AddMarks(g, m, d, 15);
        return g;
    }

    /// <summary>TOP DPS's star and TOP HEALS's cross at the rank's top right, deaths at its foot.</summary>
    static void AddMarks(Grid g, MeterMember m, double d, double md)
    {
        var top = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(d - md * .55, -4, 0, 0) };
        if (m.TopDps) top.Children.Add(Mark("★", 0xFFC93C, 0x4A2600, md, L.T("TOP DPS")));
        if (m.TopHeals) top.Children.Add(Mark("✚", 0x4CEB9A, 0x05351C, md, L.T("TOP HEALS")));
        if (top.Children.Count > 0) g.Children.Add(top);
        if (m.Deaths > 0)
        {
            var dm = Mark("✕" + m.Deaths.ToString(CultureInfo.InvariantCulture), 0xFF4D6A, 0xFFFFFF, md, "");
            dm.HorizontalAlignment = HorizontalAlignment.Left; dm.VerticalAlignment = VerticalAlignment.Bottom; dm.Margin = new Thickness(d - md * .7, 0, 0, -4);
            g.Children.Add(dm);
        }
    }

    static FrameworkElement MoreLine(int left, uint ink, double a)
    {
        var g = Cols(Star(), GridLength.Auto, Star()); g.Margin = new Thickness(6, -1, 6, 4);
        At(g, new Border { Height = 1, Background = H((ink, 0, 0), (ink, a * .6, 1)) }, 0);
        var t = T(L.F("{0} more", left), 10, FontWeights.SemiBold, S(ink, a + .2), Ui); t.Margin = new Thickness(8, 0, 8, 1);
        At(g, t, 1);
        At(g, new Border { Height = 1, Background = H((ink, a * .6, 0), (ink, 0, 1)) }, 2);
        return g;
    }

    /// <summary>The foot: a lit rule, Total and Group, the held badge.</summary>
    static FrameworkElement Foot(Encounter e, DateTime now, Brush label, Brush fig, uint badge, Brush rule)
    {
        var st = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
        st.Children.Add(new Border { Height = 1, Background = rule, Margin = new Thickness(0, 0, 0, 6) });
        var g = Cols(Star(), GridLength.Auto, GridLength.Auto);
        var parts = FootParts(e, now);
        TextBlock Part((string Label, string Fig) p)
        {
            var t = new TextBlock { VerticalAlignment = VerticalAlignment.Center, FontFamily = Ui };
            Typography.SetNumeralAlignment(t, FontNumeralAlignment.Tabular);
            t.Inlines.Add(new Run(p.Label + " ") { FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = label });
            t.Inlines.Add(new Run(p.Fig) { FontSize = 13, FontWeight = FontWeights.Black, FontFamily = Black, Foreground = fig });
            return t;
        }
        if (parts.Length > 0) At(g, Part(parts[0]), 0);
        if (parts.Length > 1) At(g, Part(parts[1]), 1);
        if (parts.Length > 2)
        {
            var b = new Border { CornerRadius = new CornerRadius(6), Padding = new Thickness(6, 0, 6, 1), Margin = new Thickness(7, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
                Background = S(badge, .16), BorderBrush = S(badge, .7), BorderThickness = new Thickness(1), Child = T(parts[2].Label, 9.5, FontWeights.Bold, S(badge), Ui) };
            At(g, b, 2);
        }
        st.Children.Add(g);
        return st;
    }

    // =====================================================================================================================
    // B, Sugar frost
    // =====================================================================================================================

    public static FrameworkElement Frost(Encounter e, DateTime now, double width)
    {
        double W = width + 10, pad = 13, inner = W - 2 * pad - 4;
        uint ink = 0x2A1238, soft = 0x7A6288;
        var root = new StackPanel();
        var panel = new Border
        {
            Width = W, CornerRadius = new CornerRadius(20), Padding = new Thickness(pad, 11, pad, 10), BorderThickness = new Thickness(2),
            Background = Lg(0, 0, .35, 1, (0xFFFBF4, .97, 0), (0xFFF0F5, .97, .6), (0xFCE3EE, .97, 1)),
            BorderBrush = V((0xFFFFFF, 1, 0), (0xFFB3D4, 1, 1)), Effect = Drop(20, 6, .55, 0x2A0A2A), Child = root,
        };
        var face = new Grid { Children = { panel } };
        // the frosting's sheen and a sugar-pink inner edge at the foot
        face.Children.Add(new Border { CornerRadius = new CornerRadius(19, 19, 60, 60), Height = 30, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(3, 3, 3, 0), Background = V((0xFFFFFF, .8, 0), (0xFFFFFF, 0, 1)), IsHitTestVisible = false });

        // the head: the title in deep plum, the clock on a cherry candy (mint when it ended)
        var head = Cols(Star(), GridLength.Auto, GridLength.Auto);
        var title = T(Title(e), 15, FontWeights.Black, S(ink), Black, false); title.TextTrimming = TextTrimming.CharacterEllipsis;
        At(head, title, 0);
        if (!e.Live)
        {
            var k = T(L.T("Killed"), 11, FontWeights.Black, S(e.Outcome is "kill" or "cleared" ? 0x0E8A50u : 0xD8304Bu), Black);
            k.Margin = new Thickness(8, 1, 8, 0); At(head, k, 1);
        }
        var clock = Lozenge(Clock(e, now), 0xFF4F8Bu, 0xFFFFFFu, 15);
        ((TextBlock)((Grid)clock.Child).Children[1]).Effect = Halo(2, .45, 0x6A0028);
        At(head, clock, 2);
        root.Children.Add(head);

        if (e.Live && e.BossHpLeft is { } hp)
        {
            var g = Cols(Star(), GridLength.Auto); g.Margin = new Thickness(0, 9, 0, 0);
            At(g, Gummy(hp, hp < .34 ? 0xFF4D6Au : 0xFF9F2Eu, 10), 0);
            var t = ParserPanel.HpText(e, 12, S(hp < .34 ? 0xC81E44u : 0xB85A00u), S(soft)); t.Margin = new Thickness(10, 0, 0, 0);
            At(g, t, 1);
            root.Children.Add(g);
        }

        // the rows: a line of words (rank, name, CP, heal | DPS, share), then the gummy strip the row's width under it
        var (rows, bars, left) = Rows(e, now);
        var (dpsW, shareW) = Cols(17, Black, 11, Bahn, FontStretches.SemiCondensed);
        var list = new StackPanel { Margin = new Thickness(0, 7, 0, 0) };
        foreach (var m in rows)
        {
            if (m.Pinned && left > 0) list.Children.Add(MoreLine(left, 0x9A7AA8, .7));
            uint c = Candy(m.ClassColor);
            var row = new StackPanel { Margin = new Thickness(0, 0, 0, 4) };
            var host = new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(5, 2, 6, 5), Child = row };
            if (m.You) { host.Background = V((0xFFF3C4, .95, 0), (0xFFE08A, .55, 1)); host.BorderBrush = S(0xF0B02E); host.BorderThickness = new Thickness(1.5); host.Padding = new Thickness(3.5, 0.5, 4.5, 3.5); }
            var top = Cols(Px(24), Star(), Px(dpsW), Px(shareW));
            // the rank: a small candy disc in the class's colour, the marks after it
            var rank = new Grid { Width = 20, Height = 20, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
            rank.Children.Add(new Ellipse { Fill = R(.38, .3, .8, .8, (Shade(c, .4), 1, 0), (c, 1, .55), (Shade(c, -.35), 1, 1)), Effect = Drop(3, 1, .4, 0x3A0A2A) });
            var rn = T(m.Rank.ToString(CultureInfo.InvariantCulture), 11, FontWeights.Black, S(0xFFFFFF), Black); rn.HorizontalAlignment = HorizontalAlignment.Center; rn.Effect = Halo(2, .6);
            rank.Children.Add(rn);
            At(top, rank, 0);
            var nameLine = new StackPanel { Orientation = Orientation.Horizontal };
            double marks = (m.TopDps ? 17 : 0) + (m.TopHeals ? 17 : 0) + (m.Deaths > 0 ? 28 : 0);
            nameLine.Children.Add(NameLine(m, inner - 24 - dpsW - shareW - marks - (m.You ? 3 : 0) - 11, 12.5, S(ink), S(soft), S(0x0B6E3E), false, FontWeights.Bold, S(0xD9FBE8), S(0x3CC985)));
            if (m.TopDps) { var mk = Mark("★", 0xFFC23C, 0x4A2600, 15, L.T("TOP DPS")); mk.Margin = new Thickness(6, 0, 0, 0); nameLine.Children.Add(mk); }
            if (m.TopHeals) { var mk = Mark("✚", 0x34D98A, 0x04351C, 15, L.T("TOP HEALS")); mk.Margin = new Thickness(m.TopDps ? 2 : 6, 0, 0, 0); nameLine.Children.Add(mk); }
            if (m.Deaths > 0) { var mk = Mark("✕" + m.Deaths.ToString(CultureInfo.InvariantCulture), 0xFF4D6A, 0xFFFFFF, 15, ""); mk.Margin = new Thickness(5, 0, 0, 0); nameLine.Children.Add(mk); }
            At(top, nameLine, 1);
            var dps = T(m.Figure, m.CpOnly ? 14 : 17, FontWeights.Black, S(ink), Black); dps.HorizontalAlignment = HorizontalAlignment.Right; dps.Margin = new Thickness(0, -2, 0, -2);
            At(top, dps, 2);
            var share = T(m.ShareText, 11, FontWeights.SemiBold, S(soft), Bahn, true, FontStretches.SemiCondensed); share.HorizontalAlignment = HorizontalAlignment.Right; share.Margin = new Thickness(0, 3, 0, 0);
            At(top, share, 3);
            row.Children.Add(top);
            var strip = Gummy(bars[m.Id], m.You ? 0xFFB21Eu : c, 14); strip.Margin = new Thickness(0, 2, 0, 0);
            row.Children.Add(strip);
            list.Children.Add(host);
        }
        root.Children.Add(list);
        root.Children.Add(Foot(e, now, S(soft), S(ink), 0x0E8A50, H((0xFF8AB8, 0, 0), (0xFF8AB8, .7, .5), (0xFF8AB8, 0, 1))));
        return face;
    }

    /// <summary>A gummy strip in a sugar mould: the mould pressed into the frosting, the candy in it to <paramref name="share"/>, glossy and lit.</summary>
    static Grid Gummy(double share, uint c, double h)
    {
        double r = h / 2;
        var g = new Grid { Height = h };
        g.Children.Add(new Border { CornerRadius = new CornerRadius(r), Background = V((0xD9C2D2, 1, 0), (0xEEDDE8, 1, .5), (0xFFF7FB, 1, 1)), BorderBrush = V((0xB898AE, .9, 0), (0xFFFFFF, .9, 1)), BorderThickness = new Thickness(1) });
        var candy = new Grid();
        candy.Children.Add(new Border { CornerRadius = new CornerRadius(r), Background = V((Shade(c, .25), 1, 0), (c, 1, .45), (Shade(c, -.32), 1, 1)), Effect = Drop(4, 1.5, .45, Shade(c, -.6)) });
        candy.Children.Add(new Border { CornerRadius = new CornerRadius(r * .8), Height = h * .38, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(r * .7, 1.5, r * .7, 0), Background = V((0xFFFFFF, .75, 0), (0xFFFFFF, .1, 1)) });
        candy.Children.Add(new Border { CornerRadius = new CornerRadius(r), Background = H((0xFFFFFF, 0, 0), (0xFFFFFF, 0, .82), (0xFFFFFF, .32, 1)) });
        g.Children.Add(Fill(candy, share, h));
        return g;
    }

    // =====================================================================================================================
    // C, Licorice glow
    // =====================================================================================================================

    public static FrameworkElement Licorice(Encounter e, DateTime now, double width)
    {
        double W = width + 10, pad = 12, inner = W - 2 * pad - 2;
        var root = new StackPanel();
        var panel = new Border
        {
            Width = W, CornerRadius = new CornerRadius(16), Padding = new Thickness(pad, 10, pad, 10), BorderThickness = new Thickness(1),
            Background = V((0x1A1120, .95, 0), (0x0E0912, .96, .5), (0x08060A, .97, 1)),
            BorderBrush = V((0xFF6FB5, .7, 0), (0x5A2A55, .5, .5), (0x2A1630, .8, 1)), Effect = Drop(20, 5, .65), Child = root,
        };
        var face = new Grid { Children = { panel } };
        // a strawberry rim light along the top edge
        face.Children.Add(new Border { Height = 2, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(26, 1, 26, 0), Background = H((0xFF6FB5, 0, 0), (0xFFB3DA, .9, .5), (0xFF6FB5, 0, 1)), Effect = Halo(8, .9, 0xFF4FA3), IsHitTestVisible = false });

        var head = Cols(Star(), GridLength.Auto, GridLength.Auto);
        var title = T(Title(e), 15, FontWeights.Black, S(0xFFF2F8), Black, false); title.TextTrimming = TextTrimming.CharacterEllipsis;
        At(head, title, 0);
        if (!e.Live)
        {
            var k = T(L.T("Killed"), 11.5, FontWeights.Black, S(e.Outcome is "kill" or "cleared" ? 0x6CFFB8u : 0xFF6A84u), Black); k.Effect = Halo(8, .8, e.Outcome is "kill" or "cleared" ? 0x1FD47Au : 0xFF2050u);
            k.Margin = new Thickness(8, 1, 10, 0); At(head, k, 1);
        }
        var clock = T(Clock(e, now), 18, FontWeights.Black, S(0xFFE7A0), Black); clock.Effect = Halo(10, .75, 0xFFA21E);
        At(head, clock, 2);
        root.Children.Add(head);

        if (e.Live && e.BossHpLeft is { } hp)
        {
            var g = Cols(Star(), GridLength.Auto); g.Margin = new Thickness(0, 8, 0, 0);
            At(g, Worm(hp, hp < .34 ? 0xFF4D6Au : 0xFFAA2Eu, 10, false), 0);
            var t = ParserPanel.HpText(e, 12, S(0xFFE7A0), S(0xC8B7CF)); t.Margin = new Thickness(10, 0, 0, 0);
            At(g, t, 1);
            root.Children.Add(g);
        }

        // the rows: the rank in lit numerals, the glowing worm with the name on it, the DPS and share in their own columns
        var (rows, bars, left) = Rows(e, now);
        var (dpsW, shareW) = Cols(17, Black, 11, Bahn, FontStretches.SemiCondensed);
        const double rowH = 28, rankW = 30;
        var list = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        foreach (var m in rows)
        {
            if (m.Pinned && left > 0) list.Children.Add(MoreLine(left, 0xC8B7CF, .5));
            uint c = Candy(m.ClassColor);
            var row = Cols(Px(rankW), Star(), Px(dpsW + 6), Px(shareW));
            row.Height = rowH; row.Margin = new Thickness(0, 0, 0, 7);
            var rk = new Grid { Width = rankW, HorizontalAlignment = HorizontalAlignment.Left };
            var rn = T(m.Rank.ToString(CultureInfo.InvariantCulture), 17, FontWeights.Black, S(m.You ? 0xFFD66Bu : Shade(c, .55)), Black); rn.HorizontalAlignment = HorizontalAlignment.Left; rn.Effect = Halo(9, .9, m.You ? 0xFFA21Eu : c);
            rk.Children.Add(rn);
            AddMarks(rk, m, 20, 12);
            At(row, rk, 0);
            var worm = Worm(bars[m.Id], m.You ? 0xFFB21Eu : c, rowH, m.You);
            At(row, worm, 1);
            double nameRoom = inner - rankW - dpsW - 6 - shareW - 22;
            var nl = NameLine(m, nameRoom, 12.5, S(0xFFFFFF), S(0xFFFFFF, .78), S(0x9CFFCF), true);
            ((FrameworkElement)nl).Margin = new Thickness(11, 0, 4, 1);
            At(row, nl, 1);
            var dps = T(m.Figure, m.CpOnly ? 14 : 17, FontWeights.Black, S(0xFFFFFF), Black); dps.HorizontalAlignment = HorizontalAlignment.Right; dps.Effect = Halo(6, .55, m.You ? 0xFFA21Eu : c);
            At(row, dps, 2);
            var share = T(m.ShareText, 11, FontWeights.SemiBold, S(0xC8B7CF), Bahn, true, FontStretches.SemiCondensed); share.HorizontalAlignment = HorizontalAlignment.Right; share.Margin = new Thickness(0, 2, 0, 0);
            At(row, share, 3);
            list.Children.Add(row);
        }
        root.Children.Add(list);
        root.Children.Add(Foot(e, now, S(0xB9A6C2), S(0xFFFFFF), 0x6CFFB8, H((0xFF6FB5, 0, 0), (0xFF6FB5, .55, .5), (0xFF6FB5, 0, 1))));
        return face;
    }

    /// <summary>A sugar-coated gummy worm that glows: a dark groove the row's width, the worm in it to <paramref name="share"/>, lit from inside, sugar on it.</summary>
    static Grid Worm(double share, uint c, double h, bool you)
    {
        double r = h / 2;
        var g = new Grid { Height = h };
        g.Children.Add(new Border { CornerRadius = new CornerRadius(r), Background = V((0x000000, .55, 0), (0x1E1424, .7, 1)), BorderBrush = you ? S(0xFFC24A, .9) : S(0xFFFFFF, .07), BorderThickness = new Thickness(you ? 1.4 : 1) });
        var worm = new Grid();
        worm.Children.Add(new Border { CornerRadius = new CornerRadius(r), Background = V((Shade(c, .2), 1, 0), (c, 1, .4), (Shade(c, -.5), 1, 1)), Effect = Halo(14, .75, c) });
        worm.Children.Add(new Border { CornerRadius = new CornerRadius(r), Background = R(.5, .45, .6, 1.1, (Shade(c, .6), .55, 0), (c, 0, 1)) });   // the glow inside
        worm.Children.Add(new Border { CornerRadius = new CornerRadius(r), Background = Sugar(), Opacity = .55 });
        worm.Children.Add(new Border { CornerRadius = new CornerRadius(r * .7), Height = h * .3, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(r * .8, 1.5, r * .8, 0), Background = V((0xFFFFFF, .55, 0), (0xFFFFFF, 0, 1)) });
        worm.Children.Add(new Border { CornerRadius = new CornerRadius(r), Background = H((0x000000, .35, 0), (0x000000, 0, .45)) });   // the start in shadow, so the name reads
        var fill = Fill(worm, share, h * 1.4);
        fill.Margin = new Thickness(1.5);
        g.Children.Add(fill);
        return g;
    }

    static DrawingBrush? _sugar;
    /// <summary>Sugar crystals: a tile of tiny bright specks.</summary>
    static DrawingBrush Sugar()
    {
        if (_sugar != null) return _sugar;
        var dg = new DrawingGroup();
        var rnd = new Random(11);
        dg.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, 18, 12))));
        for (int i = 0; i < 9; i++)
        {
            double x = rnd.NextDouble() * 18, y = rnd.NextDouble() * 12, s = .5 + rnd.NextDouble() * .7;
            dg.Children.Add(new GeometryDrawing(S(0xFFFFFF, .45 + rnd.NextDouble() * .5), null, new RectangleGeometry(new Rect(x, y, s, s))));
        }
        dg.Freeze();
        _sugar = new DrawingBrush(dg) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 18, 12), ViewportUnits = BrushMappingMode.Absolute, Stretch = Stretch.None };
        _sugar.Freeze();
        return _sugar;
    }
}
