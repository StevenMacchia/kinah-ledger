using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using KinahLedger.Parser;

namespace KinahLedger.Ui;

public static partial class MeterConcepts
{
    /// <summary>Scorebug's three concepts (Ui/MeterConcepts.cs): render-only until Steven picks one.</summary>
    static partial void AddScorebug(List<Concept> l)
    {
        l.Add(new Concept("scorebug", "A", "Primetime", ScorebugConcept.Primetime));
        l.Add(new Concept("scorebug", "B", "Studio", ScorebugConcept.Studio));
        l.Add(new Concept("scorebug", "C", "Arena", ScorebugConcept.Arena));
    }
}

/// <summary>
/// Scorebug, three ways (Steven, 2026-11-17: "clear, legible and fucking incredible cinematic production quality"), each a
/// tournament broadcast's graphics package with the damage bar as each row's hero:
/// <list type="bullet">
/// <item>A, Primetime: a network's night broadcast. Floating slanted glass slabs; each member's class-coloured bar fills
/// their slab behind the name and the DPS, lit from above with a specular sheen; gold, silver and bronze rank tabs.</item>
/// <item>B, Studio: a daytime studio package. White glossy cards that float on soft shadows, dark crisp type, and a thick
/// glossy band across each card's foot as the bar.</item>
/// <item>C, Arena: the stadium's video board. One black tower, uppercase names, LED segment bars glowing in the class's
/// colour, amber figures; a healer's heals as a second, green LED line under their damage.</item>
/// </list>
/// Drawn from the same figures as the real look (ThemeRows.Of); nothing here is offered in Settings.
/// </summary>
static class ScorebugConcept
{
    static readonly FontFamily Face = HudThemes.Bahn;
    static readonly FontStretch Tight = FontStretches.Condensed, Semi = FontStretches.SemiCondensed;
    static readonly Color Red = Rgb(0xE8263A), RedDeep = Rgb(0x9E0F20), Ink = Rgb(0x0E1016), White = Colors.White, Yellow = Rgb(0xFFD23F),
        Green = Rgb(0x2BD47A), Mint = Rgb(0x8DF5BF), Grey = Rgb(0xA9AFBE), Slate = Rgb(0x2A2F3B), Coral = Rgb(0xFF6B78),
        Gold = Rgb(0xF5C542), Silver = Rgb(0xC9D1DC), Bronze = Rgb(0xD0864A);
    const double Lean = 7;

    // ================================================================ shared parts

    static Color Rgb(uint c) => Color.FromRgb((byte)(c >> 16), (byte)(c >> 8 & 0xFF), (byte)(c & 0xFF));
    static Color A(Color c, double a) => Tac.A(c, a);
    static Color Mix(Color a, Color b, double t) => Tac.Mix(a, b, t);
    static SolidColorBrush B(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

    static TextBlock T(string s, double size, Color c, FontWeight w, FontStretch? st = null)
    {
        var t = new TextBlock { Text = s, FontSize = size, Foreground = B(c), FontWeight = w, FontFamily = Face, FontStretch = st ?? Tight, VerticalAlignment = VerticalAlignment.Center };
        System.Windows.Documents.Typography.SetNumeralAlignment(t, FontNumeralAlignment.Tabular);
        return t;
    }

    static DropShadowEffect Shadow(double blur, double depth, double opacity, Color? c = null)
    {
        var d = new DropShadowEffect { BlurRadius = blur, ShadowDepth = depth, Direction = 270, Opacity = opacity, Color = c ?? Colors.Black };
        d.Freeze();
        return d;
    }

    /// <summary>A text with a soft dark shadow under it (figures over a bar or the game).</summary>
    static TextBlock Lit(TextBlock t, double blur = 3, double op = .85) { t.Effect = Shadow(blur, 1, op); return t; }

    /// <summary>A block whose left edge leans by <paramref name="l"/> and right edge by <paramref name="r"/> (its top further right).</summary>
    static Geometry Slant(Rect r, double l, double rr)
    {
        var g = new StreamGeometry();
        using (var x = g.Open())
        {
            x.BeginFigure(new Point(r.Left + l, r.Top), true, true);
            x.LineTo(new Point(r.Right, r.Top), true, false);
            x.LineTo(new Point(r.Right - rr, r.Bottom), true, false);
            x.LineTo(new Point(r.Left, r.Bottom), true, false);
        }
        g.Freeze();
        return g;
    }

    /// <summary>A lit panel: its colour from <paramref name="top"/> to <paramref name="bottom"/>, a sheen over its upper half, a bright top line and a dark foot.</summary>
    static void Glossy(DrawingContext dc, Geometry g, Color top, Color bottom, double sheen = .2, double edge = .45)
    {
        var r = g.Bounds;
        dc.DrawGeometry(Tac.Grad(true, (top, 0), (bottom, 1)), null, g);
        dc.PushClip(g);
        if (sheen > 0) dc.DrawRectangle(Tac.Grad(true, (A(White, sheen), 0), (A(White, sheen * .25), .5), (A(White, 0), .52)), null, new Rect(r.Left, r.Top, r.Width, r.Height));
        if (edge > 0) dc.DrawRectangle(B(A(White, edge)), null, new Rect(r.Left, r.Top, r.Width, 1));
        dc.DrawRectangle(B(A(Colors.Black, .45)), null, new Rect(r.Left, r.Bottom - 1, r.Width, 1));
        dc.Pop();
    }

    /// <summary>A cell: <paramref name="draw"/> as its ground, <paramref name="child"/> on it.</summary>
    static Grid Cell(Action<DrawingContext, Size> draw, UIElement? child, double h, Thickness pad)
    {
        var g = new Grid { Height = h };
        g.Children.Add(new TacSketch(draw));
        if (child is FrameworkElement fe) { fe.Margin = pad; g.Children.Add(fe); }
        return g;
    }

    /// <summary>A slanted glossy cell in one colour.</summary>
    static Grid Block(Color c, UIElement? child, double h, Thickness pad, double l = Lean, double r = Lean, double sheen = .2) =>
        Cell((dc, s) => Glossy(dc, Slant(new Rect(s), l, r), Mix(c, White, .12), Mix(c, Colors.Black, .22), sheen), child, h, pad);

    static FrameworkElement Root(double w, UIElement body)
    {
        var host = new Border { Width = w, Child = body, HorizontalAlignment = HorizontalAlignment.Left };
        System.Windows.Documents.TextElement.SetFontFamily(host, Face);
        TextOptions.SetTextFormattingMode(host, TextFormattingMode.Display);
        System.Windows.Documents.Typography.SetNumeralAlignment(host, FontNumeralAlignment.Tabular);
        return host;
    }

    static string Up(string s) => s.ToUpper(CultureInfo.CurrentCulture);

    static string OutcomeWord(Encounter e) => e.Live || e.Outcome.Length == 0 ? ""
        : L.T(e.Outcome == "kill" ? "Killed" : e.Outcome == "wipe" ? "Wiped" : e.Outcome == "cleared" ? "Cleared" : "Over");

    static double? HpOf(Encounter e) => e.Live && !e.Waiting && e.BossHpLeft is { } hp ? hp : null;

    /// <summary>A member's bar, 0 to 1: their figure against the fight's top member (Combat Power before a hit).</summary>
    static double Bar(Encounter e, MeterMember m) { double top = e.TopMeasure(); return top > 0 ? Math.Clamp(e.Measure(m.F) / top, 0, 1) : 0; }

    static int LeftOut(Encounter e, DateTime now, int shown) => Math.Max(0, e.Shown(now).Count(f => !f.Other) - shown);

    static Color RankColor(MeterMember m) => m.You ? Red : m.Rank switch { 1 => Gold, 2 => Silver, 3 => Bronze, _ => Slate };

    /// <summary>
    /// A line of parts fitted to the room its host is given: while it is too wide, each part in <paramref name="give"/> takes
    /// its short form (or goes, with none) in turn; <paramref name="name"/> (if any) trims first, never below a 10-letter name.
    /// </summary>
    static void Fit(FrameworkElement host, TextBlock? name, IReadOnlyList<FrameworkElement> keep, IReadOnlyList<(FrameworkElement Full, FrameworkElement? Short)> give)
    {
        void Apply()
        {
            double room = host.ActualWidth;
            if (room <= 0) return;
            if (name != null) { Tac.FitName(name, room, keep, give, nameMin: 22, nameFloor: Tac.NameFloor(name)); return; }
            var inf = new Size(double.PositiveInfinity, double.PositiveInfinity);
            double Wd(FrameworkElement e) { var v = e.Visibility; e.Visibility = Visibility.Visible; e.Measure(inf); double w = e.DesiredSize.Width; e.Visibility = v; return w; }
            double fixedW = keep.Sum(Wd);
            for (int level = 0; level <= give.Count; level++)
            {
                double w = fixedW;
                for (int j = 0; j < give.Count; j++) w += j < level ? (give[j].Short is { } s ? Wd(s) : 0) : Wd(give[j].Full);
                if (w <= room || level == give.Count)
                {
                    for (int j = 0; j < give.Count; j++)
                    {
                        give[j].Full.Visibility = j < level ? Visibility.Collapsed : Visibility.Visible;
                        if (give[j].Short is { } s) s.Visibility = j < level ? Visibility.Visible : Visibility.Collapsed;
                    }
                    return;
                }
            }
        }
        host.SizeChanged += (_, _) => Apply();
    }

    /// <summary>The line between the party and you pinned under it: "▼ 1 more".</summary>
    /// <param name="pill">The plate under the words (over the game, where there is no board to read them on); null: none.</param>
    static FrameworkElement GapLine(int left, Color ink, Color rule, double h, Color? pill = null)
    {
        var g = new Grid { Height = h };
        if (pill == null) g.Children.Add(new Border { Height = 1, Background = B(rule), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(30, 0, 30, 0) });
        var t = T(left > 0 ? "▼  " + L.F("{0} more", left) : "▼", 10.5, ink, FontWeights.SemiBold, Semi);
        var plate = new Border { Padding = new Thickness(10, 0, 10, 1), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Child = t, Background = B(pill ?? Colors.Transparent), CornerRadius = new CornerRadius(8) };
        if (pill != null) plate.Effect = Shadow(6, 1, .45);
        g.Children.Add(plate);
        return g;
    }

    // ================================================================ A: Primetime

    /// <summary>A network's night broadcast: floating slanted glass slabs, each filled by its member's lit class bar.</summary>
    public static FrameworkElement Primetime(Encounter e, DateTime now, double width)
    {
        double w = width + 10;
        var body = new StackPanel();
        // the bug: the boss on network red, the clock on black glass (a red dot while it runs; KILLED on green once over)
        var head = new Grid { Height = 36 };
        head.ColumnDefinitions.Add(new ColumnDefinition()); head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = Lit(T(e.Title, 17, White, FontWeights.SemiBold), 4, .6); title.TextTrimming = TextTrimming.CharacterEllipsis;
        var titleCell = Cell((dc, s) =>
        {
            var g = Slant(new Rect(s), 0, Lean);
            Glossy(dc, g, Mix(Red, White, .1), RedDeep, .22, .5);
            dc.PushClip(g);   // a light falling from the left onto the bug
            dc.DrawRectangle(Tac.Grad(false, (A(White, .16), 0), (A(White, 0), .45)), null, new Rect(s));
            dc.Pop();
        }, title, 36, new Thickness(13, 0, 16, 1));
        titleCell.Effect = Shadow(10, 2, .55);
        head.Children.Add(titleCell);
        var clockLine = new StackPanel { Orientation = Orientation.Horizontal };
        if (OutcomeWord(e) is { Length: > 0 } ow)
        {
            var tag = Block(e.Outcome == "wipe" ? Coral : Green, T(Up(ow), 11.5, Ink, FontWeights.Bold, Semi), 20, new Thickness(9, 0, 9, 0), 4, 4, .3);
            tag.VerticalAlignment = VerticalAlignment.Center; tag.Margin = new Thickness(0, 0, 9, 0);
            clockLine.Children.Add(tag);
        }
        else if (e.Live && !e.Waiting)
        {
            var dot = new System.Windows.Shapes.Ellipse { Width = 8, Height = 8, Fill = B(Red), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, Effect = Shadow(8, 0, .9, Red) };
            clockLine.Children.Add(dot);
        }
        clockLine.Children.Add(e.Waiting ? T(L.T("Ready"), 15, Grey, FontWeights.SemiBold) : Lit(T(BossParser.Clock(e.Duration(now)), 21, e.Live ? Yellow : White, FontWeights.Bold), 4, .5));
        var clockCell = Cell((dc, s) => Glossy(dc, Slant(new Rect(s), Lean, 0), Rgb(0x23262F), Rgb(0x0A0B0F), .16, .3), clockLine, 36, new Thickness(Lean + 11, 0, 12, 0));
        clockCell.Margin = new Thickness(-Lean + 2, 0, 0, 0); clockCell.Effect = Shadow(10, 2, .55);
        Grid.SetColumn(clockCell, 1); head.Children.Add(clockCell);
        body.Children.Add(head);

        // the boss's HP on a strip of black glass under the bug: a red bar with its thirds, "47%" and what's left
        if (HpOf(e) is { } hp)
        {
            var g = new Grid { Height = 24 };
            g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.Children.Add(new TacSketch((dc, s) =>
            {
                double y = Math.Round((s.Height - 7) / 2);
                dc.DrawRectangle(B(Rgb(0x23262F)), null, new Rect(0, y, s.Width, 7));
                dc.DrawRectangle(Tac.Grad(true, (Mix(Red, White, .25), 0), (RedDeep, 1)), null, new Rect(0, y, s.Width * hp, 7));
                dc.DrawRectangle(B(A(White, .5)), null, new Rect(0, y, s.Width * hp, 1));
                for (int k = 1; k <= 2; k++) dc.DrawRectangle(B(Ink), null, new Rect(Math.Round(s.Width * k / 3) - 1, y - 2, 2, 11));
            }) { Margin = new Thickness(0, 0, 10, 0) });
            var words = new StackPanel { Orientation = Orientation.Horizontal };
            words.Children.Add(T($"{Math.Round(hp * 100):0}%", 15, hp < .34 ? Coral : White, FontWeights.Bold));
            if (ParserPanel.HpLeft(e) is { Length: > 0 } left) words.Children.Add(T("  " + left, 12.5, Grey, FontWeights.SemiBold, Semi));
            Grid.SetColumn(words, 1); g.Children.Add(words);
            var strip = Cell((dc, s) => Glossy(dc, Slant(new Rect(s), 0, Lean), A(Rgb(0x15171E), .94), A(Rgb(0x0A0B0F), .94), .08, .14), g, 26, new Thickness(12, 0, 14, 0));
            strip.Margin = new Thickness(0, 3, 30, 0); strip.Effect = Shadow(8, 2, .5);
            body.Children.Add(strip);
        }

        var members = ThemeRows.Of(e, now, expanded: false);
        if (e.Waiting && members.Count == 0)
        {
            var wt = Cell((dc, s) => Glossy(dc, Slant(new Rect(s), 0, Lean), A(Rgb(0x15171E), .94), A(Rgb(0x0A0B0F), .94), .08, .14), T(e.ReadyText, 14, Grey, FontWeights.SemiBold, Semi), 32, new Thickness(14, 0, 14, 0));
            wt.Margin = new Thickness(0, 4, 0, 0);
            body.Children.Add(wt);
        }
        var rows = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
        foreach (var m in members)
        {
            if (m.Pinned) rows.Children.Add(GapLine(LeftOut(e, now, members.Count), Rgb(0xC9CDD8), A(White, .35), 20, A(Rgb(0x0A0B0F), .9)));
            rows.Children.Add(PrimeRow(e, m));
        }
        body.Children.Add(rows);
        if (e.HasDamage && !e.Waiting) body.Children.Add(PrimeFoot(e, now));
        return Root(w, body);
    }

    static FrameworkElement PrimeRow(Encounter e, MeterMember m)
    {
        const double H = 34;
        double bar = Bar(e, m);
        var c = m.ClassColor;
        var row = new Grid { Height = H, Margin = new Thickness(0, 0, 0, 4) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
        // the rank tab: gold, silver, bronze, then slate; yours red
        var rc = RankColor(m);
        var rank = Block(rc, T(m.Rank.ToString(CultureInfo.InvariantCulture), 19, rc == Slate || m.You ? White : Ink, FontWeights.Bold), H, new Thickness(0), 0, Lean, .3);
        ((TextBlock)rank.Children[1]).HorizontalAlignment = HorizontalAlignment.Center; ((TextBlock)rank.Children[1]).Margin = new Thickness(-3, 0, 0, 1);
        rank.Effect = Shadow(8, 2, .55);
        row.Children.Add(rank);
        // the slab: black glass, the member's bar filling it from the left (lit from above, its end a bright slanted edge)
        var slab = new TacSketch((dc, s) =>
        {
            var g = Slant(new Rect(s), Lean, Lean);
            Glossy(dc, g, A(Rgb(0x1A1D25), .95), A(Rgb(0x07080B), .95), .1, .18);
            dc.PushClip(g);
            double fw = Lean + (s.Width - Lean) * bar;
            if (bar > 0)
            {
                var fg = Slant(new Rect(0, 0, fw, s.Height), Lean, Lean);
                dc.DrawGeometry(Tac.Grad(true, (Mix(c, White, .1), 0), (Mix(c, Colors.Black, .18), .55), (Mix(c, Colors.Black, .45), 1)), null, fg);
                dc.PushClip(fg);
                dc.DrawRectangle(Tac.Grad(true, (A(White, .28), 0), (A(White, .06), .48), (A(White, 0), .5)), null, new Rect(0, 0, fw, s.Height));
                dc.DrawRectangle(Tac.Grad(false, (A(Colors.Black, 0), 0), (A(Colors.Black, .22), 1)), null, new Rect(0, 0, fw, s.Height));
                dc.Pop();
                // the bar's head: a bright slanted line and a little light spilling past it (never under the DPS: there the shade takes it)
                if (fw < s.Width - 100)
                {
                    var p = new Pen(B(Mix(c, White, .6)), 2); p.Freeze();
                    dc.DrawLine(p, new Point(fw - 1, 0), new Point(fw - 1 - Lean, s.Height));
                    dc.DrawRectangle(Tac.Grad(false, (A(Mix(c, White, .4), .35), 0), (A(c, 0), 1)), null, new Rect(fw, 0, 14, s.Height));
                }
            }
            // the figures' end in shade, so the DPS reads on any class's colour
            dc.DrawRectangle(Tac.Grad(false, (A(Colors.Black, 0), 0), (A(Colors.Black, .62), .7), (A(Colors.Black, .66), 1)), null, new Rect(s.Width - 130, 0, 130, s.Height));
            dc.DrawRectangle(B(A(White, .4)), null, new Rect(0, 0, s.Width, 1));
            dc.Pop();
            if (m.You) { var o = new Pen(B(A(White, .9)), 1.4); o.Freeze(); dc.DrawGeometry(null, o, g); }
        });
        var slabHost = new Grid { Margin = new Thickness(-Lean + 2, 0, 0, 0), Effect = m.You ? Shadow(14, 0, .75, Red) : Shadow(10, 2, .55) };
        slabHost.Children.Add(slab);
        var on = new Grid { Margin = new Thickness(Lean + 4, 0, 12, 0) };
        on.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        on.ColumnDefinitions.Add(new ColumnDefinition());
        on.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var icon = ClassArt.Icon(m.Class, 20, m.Name); icon.Margin = new Thickness(0, 0, 7, 0);
        on.Children.Add(icon);
        // the name, and under it the quiet line: Combat Power, a healer's heals, the marks
        var name = Lit(T(m.Name, 15, White, FontWeights.Bold, Semi), 3, .9); name.TextTrimming = TextTrimming.CharacterEllipsis;
        var sub = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, -1, 0, 0) };
        var keep = new List<FrameworkElement>(); var give = new List<(FrameworkElement, FrameworkElement?)>();
        // a tight dark ring under the small words, so they read on the lightest class colour
        TextBlock Small(string s, Color col, FontWeight fw, double size = 10) { var t = T(s, size, col, fw, Semi); t.Effect = Shadow(3, 0, 1); t.Margin = new Thickness(0, 0, 7, 0); return t; }
        if (m.Heal.Length > 0) keep.Add(Small("+" + m.Heal, Mint, FontWeights.Bold));
        if (m.Deaths > 0) keep.Add(Small("✕" + m.Deaths, Coral, FontWeights.Bold));
        if (m.TopDps) give.Add((Small("★ " + Up(L.T("TOP DPS")), Yellow, FontWeights.Bold, 9.5), Small("★", Yellow, FontWeights.Bold)));
        if (m.TopHeals) give.Add((Small("✚ " + Up(L.T("TOP HEALS")), Mint, FontWeights.Bold, 9.5), Small("✚", Mint, FontWeights.Bold)));
        if (m.Cp.Length > 0) give.Add((Small($"{m.Cp} {L.T("CP")}", A(White, .88), FontWeights.SemiBold), null));
        // Combat Power first on the line, then the heals and the marks
        foreach (var (f, s) in give.Where(x => ((TextBlock)x.Item1).Text.EndsWith(L.T("CP")))) { sub.Children.Add(f); }
        foreach (var k in keep) sub.Children.Add(k);
        foreach (var (f, s) in give.Where(x => !((TextBlock)x.Item1).Text.EndsWith(L.T("CP")))) { sub.Children.Add(f); if (s != null) sub.Children.Add(s); }
        var nameCol = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        nameCol.Children.Add(name);
        if (sub.Children.Count > 0) { var subHost = new Border { Child = sub }; nameCol.Children.Add(subHost); Fit(subHost, null, keep, give); }
        Grid.SetColumn(nameCol, 1); on.Children.Add(nameCol);
        var dps = Lit(T(m.Figure, m.CpOnly ? 17 : 21, White, FontWeights.Bold), 5, .9);
        dps.Margin = new Thickness(8, 0, 0, 1);
        Grid.SetColumn(dps, 2); on.Children.Add(dps);
        slabHost.Children.Add(on);
        Grid.SetColumn(slabHost, 1); row.Children.Add(slabHost);
        // the share, small, on its own dark cell after the slab
        var share = T(m.ShareText, 12, Rgb(0xC9CDD8), FontWeights.SemiBold, Semi); share.HorizontalAlignment = HorizontalAlignment.Center;
        var sc = Cell((dc, s) => Glossy(dc, Slant(new Rect(s), Lean, Lean), A(Rgb(0x15171E), .92), A(Rgb(0x07080B), .92), .06, .12), share, H, new Thickness(Lean - 2, 0, 0, 0));
        sc.Margin = new Thickness(-Lean + 4, 0, 0, 0); sc.Effect = Shadow(8, 2, .5);
        Grid.SetColumn(sc, 2); row.Children.Add(sc);
        return row;
    }

    static FrameworkElement PrimeFoot(Encounter e, DateTime now)
    {
        var (word, group, held) = ParserFoot.Right(e, now);
        var g = new Grid { Height = 28, Margin = new Thickness(0, 2, 0, 0) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var tl = Block(Red, T(Up(L.T("Total")), 11.5, White, FontWeights.Bold, Semi), 28, new Thickness(12, 0, 13, 0), 0, Lean, .25);
        g.Children.Add(tl);
        var tv = Cell((dc, s) => Glossy(dc, Slant(new Rect(s), Lean, Lean), A(Rgb(0x1A1D25), .95), A(Rgb(0x07080B), .95), .1, .2), T(Amounts.Meter(e.Credited), 18, White, FontWeights.Bold), 28, new Thickness(Lean + 8, 0, 12, 0));
        tv.Margin = new Thickness(-Lean + 2, 0, 0, 0); tv.HorizontalAlignment = HorizontalAlignment.Left;
        Grid.SetColumn(tv, 1); g.Children.Add(tv);
        var right = new StackPanel { Orientation = Orientation.Horizontal };
        if (word.Length > 0)
        {
            right.Children.Add(Block(Slate, T(Up(word), 11, Rgb(0xD5DBE6), FontWeights.Bold, Semi), 28, new Thickness(Lean + 8, 0, 10, 0), Lean, Lean, .14));
            var gv = Cell((dc, s) => Glossy(dc, Slant(new Rect(s), Lean, held ? Lean : 0), A(Rgb(0x1A1D25), .95), A(Rgb(0x07080B), .95), .1, .2), T(group, 18, Yellow, FontWeights.Bold), 28, new Thickness(Lean + 8, 0, held ? 10 : 12, 0));
            gv.Margin = new Thickness(-Lean + 2, 0, 0, 0);
            right.Children.Add(gv);
        }
        if (held)
        {
            var badge = Block(Green, T(Up(L.T(Encounter.AvgBadge)), 11, Ink, FontWeights.Bold, Semi), 28, new Thickness(Lean + 8, 0, 10, 0), Lean, 0, .3);
            badge.Margin = new Thickness(-Lean + 2, 0, 0, 0);
            right.Children.Add(badge);
        }
        Grid.SetColumn(right, 2); g.Children.Add(right);
        g.Effect = Shadow(10, 2, .55);
        return g;
    }

    // ================================================================ B: Studio

    /// <summary>A daytime studio package: white glossy cards floating on soft shadows, dark crisp type, a thick glossy band as each bar.</summary>
    public static FrameworkElement Studio(Encounter e, DateTime now, double width)
    {
        double w = width + 10;
        Color paperTop = Rgb(0xFFFFFF), paperBot = Rgb(0xE6E9EF), ink = Rgb(0x12151C), sub = Rgb(0x5D6474);
        var body = new StackPanel();
        // the head card: red spine, the boss in dark type, the clock inset on black glass
        var head = new Grid { Height = 38 };
        head.Children.Add(new TacSketch((dc, s) =>
        {
            var g = Slant(new Rect(s), 0, 9);
            Glossy(dc, g, paperTop, paperBot, .0, 0);
            dc.PushClip(g);
            dc.DrawRectangle(Tac.Grad(true, (Mix(Red, White, .15), 0), (RedDeep, 1)), null, new Rect(0, 0, 7, s.Height));
            dc.DrawRectangle(B(A(White, 1)), null, new Rect(0, 0, s.Width, 1));
            dc.Pop();
        }) { Effect = Shadow(12, 3, .5) });
        var hg = new Grid { Margin = new Thickness(17, 0, 6, 0) };
        hg.ColumnDefinitions.Add(new ColumnDefinition()); hg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); hg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = T(e.Title, 17.5, ink, FontWeights.Bold); title.TextTrimming = TextTrimming.CharacterEllipsis;
        hg.Children.Add(title);
        if (OutcomeWord(e) is { Length: > 0 } ow)
        {
            var o = T(Up(ow), 12, e.Outcome == "wipe" ? Rgb(0xC81E30) : Rgb(0x14935A), FontWeights.Bold, Semi); o.Margin = new Thickness(10, 1, 9, 0);
            Grid.SetColumn(o, 1); hg.Children.Add(o);
        }
        var clockT = e.Waiting ? T(L.T("Ready"), 14, Grey, FontWeights.SemiBold) : T(BossParser.Clock(e.Duration(now)), 20, e.Live ? Yellow : White, FontWeights.Bold);
        var clock = Cell((dc, s) => Glossy(dc, Slant(new Rect(s), 6, 6), Rgb(0x2A2E38), Rgb(0x0B0C10), .18, .35), clockT, 28, new Thickness(14, 0, 14, 0));
        clock.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(clock, 2); hg.Children.Add(clock);
        head.Children.Add(hg);
        body.Children.Add(head);

        if (HpOf(e) is { } hp)
        {
            var strip = new Grid { Height = 24, Margin = new Thickness(0, 4, 12, 0) };
            strip.Children.Add(new TacSketch((dc, s) => Glossy(dc, Slant(new Rect(s), 0, 7), A(paperTop, .97), A(paperBot, .97), 0, 0)) { Effect = Shadow(10, 2, .4) });
            var g = new Grid { Margin = new Thickness(12, 0, 14, 0) };
            g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.Children.Add(new TacSketch((dc, s) =>
            {
                double y = Math.Round((s.Height - 7) / 2);
                dc.DrawRoundedRectangle(Tac.Grad(true, (Rgb(0xC3C8D2), 0), (Rgb(0xDCE0E7), 1)), null, new Rect(0, y, s.Width, 7), 3.5, 3.5);
                dc.DrawRoundedRectangle(Tac.Grad(true, (Mix(Red, White, .25), 0), (RedDeep, 1)), null, new Rect(0, y, Math.Max(7, s.Width * hp), 7), 3.5, 3.5);
                for (int k = 1; k <= 2; k++) dc.DrawRectangle(B(White), null, new Rect(Math.Round(s.Width * k / 3) - 1, y - 1, 2, 9));
            }) { Margin = new Thickness(0, 0, 12, 0) });
            var words = new StackPanel { Orientation = Orientation.Horizontal };
            words.Children.Add(T($"{Math.Round(hp * 100):0}%", 14, hp < .34 ? Rgb(0xC81E30) : ink, FontWeights.Bold));
            if (ParserPanel.HpLeft(e) is { Length: > 0 } left) words.Children.Add(T("  " + left, 12, sub, FontWeights.SemiBold, Semi));
            Grid.SetColumn(words, 1); g.Children.Add(words);
            strip.Children.Add(g);
            body.Children.Add(strip);
        }

        var members = ThemeRows.Of(e, now, expanded: false);
        if (e.Waiting && members.Count == 0)
        {
            var wt = T(e.ReadyText, 14, sub, FontWeights.SemiBold, Semi); wt.Margin = new Thickness(16, 0, 0, 0);
            var card = new Grid { Height = 32, Margin = new Thickness(0, 5, 0, 0) };
            card.Children.Add(new TacSketch((dc, s) => Glossy(dc, Slant(new Rect(s), 0, 8), paperTop, paperBot, 0, 0)) { Effect = Shadow(10, 2, .4) });
            card.Children.Add(wt);
            body.Children.Add(card);
        }
        var rows = new StackPanel { Margin = new Thickness(0, 7, 0, 0) };
        foreach (var m in members)
        {
            if (m.Pinned) rows.Children.Add(GapLine(LeftOut(e, now, members.Count), Rgb(0x3A404E), A(White, .55), 20, A(White, .94)));
            rows.Children.Add(StudioRow(e, m, paperTop, paperBot, ink, sub));
        }
        body.Children.Add(rows);

        if (e.HasDamage && !e.Waiting)
        {
            var (word, group, held) = ParserFoot.Right(e, now);
            var foot = new Grid { Height = 30, Margin = new Thickness(0, 2, 0, 0) };
            foot.Children.Add(new TacSketch((dc, s) =>
            {
                Glossy(dc, Slant(new Rect(s), 0, 9), Rgb(0x1C1F27), Rgb(0x0A0B0F), .14, .3);
                dc.DrawRectangle(Tac.Grad(true, (Mix(Red, White, .15), 0), (RedDeep, 1)), null, new Rect(0, 0, 7, s.Height));
            }) { Effect = Shadow(12, 3, .5) });
            var fg = new Grid { Margin = new Thickness(17, 0, 14, 0) };
            fg.ColumnDefinitions.Add(new ColumnDefinition()); fg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var tl = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            tl.Children.Add(T(Up(L.T("Total")), 11, Grey, FontWeights.Bold, Semi));
            var tv = T(Amounts.Meter(e.Credited), 17, White, FontWeights.Bold); tv.Margin = new Thickness(8, 0, 0, 0); tl.Children.Add(tv);
            fg.Children.Add(tl);
            var rt = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            if (word.Length > 0) { rt.Children.Add(T(Up(word), 11, Grey, FontWeights.Bold, Semi)); var gv = T(group, 17, Yellow, FontWeights.Bold); gv.Margin = new Thickness(8, 0, 0, 0); rt.Children.Add(gv); }
            if (held)
            {
                var badge = new Border { Background = B(Green), CornerRadius = new CornerRadius(3), Padding = new Thickness(6, 1, 6, 2), Margin = new Thickness(9, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
                    Child = T(Up(L.T(Encounter.AvgBadge)), 10, Ink, FontWeights.Bold, Semi) };
                rt.Children.Add(badge);
            }
            Grid.SetColumn(rt, 1); fg.Children.Add(rt);
            foot.Children.Add(fg);
            body.Children.Add(foot);
        }
        return Root(w, body);
    }

    static FrameworkElement StudioRow(Encounter e, MeterMember m, Color paperTop, Color paperBot, Color ink, Color sub)
    {
        const double H = 40, Band = 9, ShareW = 40;
        double bar = Bar(e, m);
        var c = m.ClassColor;
        var row = new Grid { Height = H, Margin = new Thickness(0, 0, 0, 5) };
        // the card: white, glossy, floating; your card has a red spine and a red ring
        row.Children.Add(new TacSketch((dc, s) =>
        {
            var g = Slant(new Rect(s), 0, 9);
            Glossy(dc, g, m.You ? Rgb(0xFFF6F7) : paperTop, m.You ? Rgb(0xF0DDE0) : paperBot, 0, 0);
            dc.PushClip(g);
            dc.DrawRectangle(B(White), null, new Rect(0, 0, s.Width, 1));
            // the band: a thick glossy bar along the card's foot, its track the card's own groove
            double x0 = 38, bw = s.Width - x0 - ShareW - 16, by = s.Height - Band - 6;
            dc.DrawRoundedRectangle(Tac.Grad(true, (Rgb(0xC3C8D2), 0), (Rgb(0xDCE0E7), 1)), null, new Rect(x0, by, bw, Band), Band / 2, Band / 2);
            if (bar > 0)
            {
                var fr = new Rect(x0, by, Math.Max(Band, bw * bar), Band);
                dc.DrawRoundedRectangle(Tac.Grad(true, (Mix(c, White, .25), 0), (Mix(c, Colors.Black, .12), .55), (Mix(c, Colors.Black, .35), 1)), null, fr, Band / 2, Band / 2);
                dc.DrawRoundedRectangle(B(A(White, .55)), null, new Rect(fr.X + 3, fr.Y + 1.2, Math.Max(0, fr.Width - 6), 2), 1, 1);
                var p = new Pen(B(A(Mix(c, Colors.Black, .45), .6)), .8); p.Freeze();
                dc.DrawRoundedRectangle(null, p, fr, Band / 2, Band / 2);
            }
            dc.Pop();
        }) { Effect = m.You ? Shadow(14, 2, .6, Red) : Shadow(10, 3, .45) });
        // the rank block on the card's left, as tall as the card
        var rc = RankColor(m);
        var rank = new Grid { Width = 30, HorizontalAlignment = HorizontalAlignment.Left };
        rank.Children.Add(new TacSketch((dc, s) => Glossy(dc, new RectangleGeometry(new Rect(s)), m.You ? Mix(Red, White, .1) : rc == Slate ? Rgb(0x2A2E38) : Mix(rc, White, .15), m.You ? RedDeep : rc == Slate ? Rgb(0x111318) : Mix(rc, Colors.Black, .2), .25, .5)));
        var rn = T(m.Rank.ToString(CultureInfo.InvariantCulture), 19, rc == Slate || m.You ? White : Ink, FontWeights.Bold); rn.HorizontalAlignment = HorizontalAlignment.Center;
        rank.Children.Add(rn);
        row.Children.Add(rank);
        // the line over the band: icon, name, Combat Power and the marks; the share and the DPS at the right
        var line = new Grid { Height = H - Band - 8, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(38, 1, 16, 0) };
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        line.ColumnDefinitions.Add(new ColumnDefinition());
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var icon = ClassArt.Icon(m.Class, 18, m.Name); icon.Margin = new Thickness(0, 0, 6, 0);
        line.Children.Add(icon);
        var name = T(m.Name, 15, ink, FontWeights.Bold, Semi); name.TextTrimming = TextTrimming.CharacterEllipsis;
        var nl = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        nl.Children.Add(name);
        var keep = new List<FrameworkElement>(); var give = new List<(FrameworkElement, FrameworkElement?)>();
        FrameworkElement Tag(string s, Color bg, Color fg) => new Border { Background = Tac.Grad(true, (Mix(bg, White, .2), 0), (bg, 1)), CornerRadius = new CornerRadius(2.5), Padding = new Thickness(4, 0, 4, 1), Margin = new Thickness(6, 1, 0, 0), VerticalAlignment = VerticalAlignment.Center, Child = T(s, 9.5, fg, FontWeights.Bold, Semi) };
        TextBlock Small(string s, Color col) { var t = T(s, 11, col, FontWeights.SemiBold, Semi); t.Margin = new Thickness(6, 2, 0, 0); return t; }
        var cp = m.Cp.Length > 0 ? Small(m.Cp, sub) : null;
        if (cp != null) nl.Children.Add(cp);
        if (m.Heal.Length > 0) { var h = Small("+" + m.Heal, Rgb(0x0F8A4F)); h.FontWeight = FontWeights.Bold; nl.Children.Add(h); keep.Add(h); }
        if (m.Deaths > 0) { var d = Tag("✕" + m.Deaths, Rgb(0xD62538), White); nl.Children.Add(d); keep.Add(d); }
        if (m.TopDps) { var f = Tag(Up(L.T("TOP DPS")), Gold, Ink); var s = Tag("★", Gold, Ink); nl.Children.Add(f); nl.Children.Add(s); give.Add((f, s)); }
        if (m.TopHeals) { var f = Tag(Up(L.T("TOP HEALS")), Rgb(0x1FB86A), White); var s = Tag("✚", Rgb(0x1FB86A), White); nl.Children.Add(f); nl.Children.Add(s); give.Add((f, s)); }
        if (cp != null) give.Add((cp, null));
        var nlHost = new Border { Child = nl, ClipToBounds = false };
        Fit(nlHost, name, keep, give);
        Grid.SetColumn(nlHost, 1); line.Children.Add(nlHost);
        var dps = T(m.Figure, m.CpOnly ? 17 : 21, ink, FontWeights.Bold); dps.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(dps, 3); line.Children.Add(dps);
        row.Children.Add(line);
        // the share, small, at the band's end
        var share = T(m.ShareText, 10.5, sub, FontWeights.Bold, Semi);
        share.HorizontalAlignment = HorizontalAlignment.Right; share.VerticalAlignment = VerticalAlignment.Bottom; share.Margin = new Thickness(0, 0, 18, 3);
        row.Children.Add(share);
        return row;
    }

    // ================================================================ C: Arena

    /// <summary>The stadium's video board: a black tower, uppercase names, LED segment bars glowing in the class's colour, amber figures.</summary>
    public static FrameworkElement Arena(Encounter e, DateTime now, double width)
    {
        double w = width + 10;
        Color amber = Rgb(0xFFC23D);
        var body = new StackPanel();
        // the board's top: the boss on a red slanted block, the clock in amber digits on black
        var head = new Grid { Height = 34 };
        head.ColumnDefinitions.Add(new ColumnDefinition()); head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = Lit(T(Up(e.Title), 15, White, FontWeights.Bold), 3, .6); title.TextTrimming = TextTrimming.CharacterEllipsis;
        var tc = Block(Red, title, 34, new Thickness(12, 0, 14, 1), 0, 10, .22);
        head.Children.Add(tc);
        var cl = new StackPanel { Orientation = Orientation.Horizontal };
        if (OutcomeWord(e) is { Length: > 0 } ow) { var o = T(Up(ow), 11.5, e.Outcome == "wipe" ? Coral : Green, FontWeights.Bold, Semi); o.Margin = new Thickness(0, 1, 9, 0); o.Effect = Shadow(8, 0, .8, e.Outcome == "wipe" ? Coral : Green); cl.Children.Add(o); }
        var ct = e.Waiting ? T(L.T("Ready"), 14, Grey, FontWeights.SemiBold) : T(BossParser.Clock(e.Duration(now)), 21, amber, FontWeights.Bold);
        if (!e.Waiting) ct.Effect = Shadow(10, 0, .75, amber);
        cl.Children.Add(ct);
        var cc = Cell((dc, s) => Glossy(dc, Slant(new Rect(s), 10, 0), Rgb(0x16181E), Rgb(0x050608), .1, .2), cl, 34, new Thickness(18, 0, 12, 0));
        cc.Margin = new Thickness(-8, 0, 0, 0);
        Grid.SetColumn(cc, 1); head.Children.Add(cc);

        // the tower: one black board, a faint dot grid, every row on it
        var board = new StackPanel { Margin = new Thickness(0, 0, 0, 0) };
        if (HpOf(e) is { } hp)
        {
            var g = new Grid { Height = 24, Margin = new Thickness(10, 6, 10, 0) };
            g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.Children.Add(new TacSketch((dc, s) => Leds(dc, new Rect(0, Math.Round((s.Height - 8) / 2), s.Width, 8), hp, Red, 3, 1.4)) { Margin = new Thickness(0, 0, 10, 0), Effect = Shadow(8, 0, .7, Red) });
            var words = new StackPanel { Orientation = Orientation.Horizontal };
            words.Children.Add(T($"{Math.Round(hp * 100):0}%", 15, hp < .34 ? Coral : White, FontWeights.Bold));
            if (ParserPanel.HpLeft(e) is { Length: > 0 } left) words.Children.Add(T("  " + left, 12.5, Grey, FontWeights.SemiBold, Semi));
            Grid.SetColumn(words, 1); g.Children.Add(words);
            board.Children.Add(g);
        }
        var members = ThemeRows.Of(e, now, expanded: false);
        if (e.Waiting)
        {
            var wt = T(Up(e.ReadyText), 13, Grey, FontWeights.SemiBold, Semi); wt.Margin = new Thickness(12, 8, 12, members.Count == 0 ? 10 : 2);
            board.Children.Add(wt);
        }
        var rows = new StackPanel { Margin = new Thickness(6, 6, 6, 4) };
        foreach (var m in members)
        {
            if (m.Pinned) rows.Children.Add(GapLine(LeftOut(e, now, members.Count), Grey, Rgb(0x2A2F3B), 16));
            rows.Children.Add(ArenaRow(e, m, amber));
        }
        board.Children.Add(rows);
        if (e.HasDamage && !e.Waiting)
        {
            var (word, group, held) = ParserFoot.Right(e, now);
            var fg = new Grid { Height = 30, Margin = new Thickness(0, 0, 0, 0) };
            fg.ColumnDefinitions.Add(new ColumnDefinition()); fg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            fg.Children.Add(new TacSketch((dc, s) =>
            {
                dc.DrawRectangle(Tac.Grad(true, (Rgb(0x15171D), 0), (Rgb(0x0B0C10), 1)), null, new Rect(s));
                dc.DrawRectangle(B(Rgb(0x2A2F3B)), null, new Rect(0, 0, s.Width, 1));
            }) { });
            Grid.SetColumnSpan(fg.Children[0], 2);
            var tl = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
            tl.Children.Add(T(Up(L.T("Total")), 10.5, Grey, FontWeights.Bold, Semi));
            var tv = T(Amounts.Meter(e.Credited), 17, White, FontWeights.Bold); tv.Margin = new Thickness(8, 0, 0, 0); tl.Children.Add(tv);
            fg.Children.Add(tl);
            var rt = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
            if (word.Length > 0) { rt.Children.Add(T(Up(word), 10.5, Grey, FontWeights.Bold, Semi)); var gv = T(group, 17, amber, FontWeights.Bold); gv.Margin = new Thickness(8, 0, 0, 0); gv.Effect = Shadow(8, 0, .6, amber); rt.Children.Add(gv); }
            if (held) rt.Children.Add(new Border { Background = B(Green), Padding = new Thickness(5, 1, 5, 2), Margin = new Thickness(9, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Child = T(Up(L.T(Encounter.AvgBadge)), 10, Ink, FontWeights.Bold, Semi) });
            Grid.SetColumn(rt, 1); fg.Children.Add(rt);
            board.Children.Add(fg);
        }
        var boardHost = new Grid();
        boardHost.Children.Add(new TacSketch((dc, s) =>
        {
            dc.DrawRectangle(Tac.Grad(true, (A(Rgb(0x0D0F14), .96), 0), (A(Rgb(0x050608), .96), 1)), null, new Rect(s));
            // the board's dot grid and a soft light from above
            var dot = B(A(White, .035));
            for (double y = 2; y < s.Height; y += 4) for (double x = 2; x < s.Width; x += 4) dc.DrawRectangle(dot, null, new Rect(x, y, 1, 1));
            dc.DrawRectangle(Tac.Grad(true, (A(White, .06), 0), (A(White, 0), 1)), null, new Rect(0, 0, s.Width, Math.Min(60, s.Height)));
            var p = new Pen(B(Rgb(0x2A2F3B)), 1); p.Freeze();
            dc.DrawRectangle(null, p, new Rect(.5, -.5, s.Width - 1, s.Height));
        }));
        boardHost.Children.Add(board);
        var all = new StackPanel();
        all.Children.Add(head);
        all.Children.Add(boardHost);
        all.Effect = Shadow(14, 3, .6);
        body.Children.Add(all);
        return Root(w, body);
    }

    /// <summary>A row of LED segments in <paramref name="rect"/>, the first <paramref name="lit"/> of them lit in <paramref name="c"/>.</summary>
    static void Leds(DrawingContext dc, Rect rect, double lit, Color c, double seg = 4, double gap = 1.6)
    {
        int n = Math.Max(1, (int)Math.Floor((rect.Width + gap) / (seg + gap)));
        double step = (rect.Width + gap) / n, sw = step - gap;
        int on = (int)Math.Round(n * Math.Clamp(lit, 0, 1));
        if (lit > 0 && on == 0) on = 1;
        var offB = B(A(c, .13));
        var onB = Tac.Grad(true, (Mix(c, White, .45), 0), (c, .45), (Mix(c, Colors.Black, .25), 1));
        var head = Tac.Grad(true, (Mix(c, White, .8), 0), (Mix(c, White, .4), 1));
        for (int i = 0; i < n; i++)
            dc.DrawRoundedRectangle(i < on ? (i == on - 1 ? head : onB) : offB, null, new Rect(rect.X + i * step, rect.Y, sw, rect.Height), 1, 1);
    }

    static FrameworkElement ArenaRow(Encounter e, MeterMember m, Color amber)
    {
        double bar = Bar(e, m);
        var c = m.ClassColor;
        bool healer = m.Heal.Length > 0;
        const double H = 34;
        var row = new Grid { Height = H, Margin = new Thickness(0, 0, 0, 3) };
        if (m.You) row.Children.Add(new Border { Background = Tac.Grad(false, (A(Red, .32), 0), (A(Red, .08), 1)), BorderBrush = B(A(Red, .8)), BorderThickness = new Thickness(0, 1, 0, 1) });
        else row.Children.Add(new Border { Background = B(A(White, .025)) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(102) });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumnSpan(row.Children[0], 4);
        // the position: a slanted chip, gold, silver, bronze, then dark; yours red
        var rc = RankColor(m);
        var rank = Block(rc == Slate ? Rgb(0x23272F) : rc, T(m.Rank.ToString(CultureInfo.InvariantCulture), 17, rc == Slate || m.You ? White : Ink, FontWeights.Bold), 24, new Thickness(0), 0, 5, .3);
        ((TextBlock)rank.Children[1]).HorizontalAlignment = HorizontalAlignment.Center; ((TextBlock)rank.Children[1]).Margin = new Thickness(-2, 0, 0, 1);
        rank.Width = 26; rank.HorizontalAlignment = HorizontalAlignment.Left; rank.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(rank);
        // the team stripe and the name over its quiet line (Combat Power, the marks)
        var nameCol = new Grid { Margin = new Thickness(0, 0, 6, 0) };
        nameCol.Children.Add(new Border { Width = 3, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 4), Background = Tac.Grad(true, (Mix(c, White, .3), 0), (c, 1)), Effect = Shadow(6, 0, .8, c) });
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        var name = T(Up(m.Name), 14, White, FontWeights.Bold, Semi); name.TextTrimming = TextTrimming.CharacterEllipsis;
        stack.Children.Add(name);
        var sub = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, -1, 0, 0) };
        var keep = new List<FrameworkElement>(); var give = new List<(FrameworkElement, FrameworkElement?)>();
        TextBlock Small(string s, Color col, FontWeight fw) { var t = T(s, 10, col, fw, Semi); t.Margin = new Thickness(0, 0, 6, 0); return t; }
        TextBlock? cp = m.Cp.Length > 0 ? Small(m.Cp, Rgb(0x8B92A3), FontWeights.SemiBold) : null;
        if (cp != null) sub.Children.Add(cp);
        if (m.Deaths > 0) { var d = Small("✕" + m.Deaths, Coral, FontWeights.Bold); sub.Children.Add(d); keep.Add(d); }
        if (m.TopDps) { var f = Small("★ " + Up(L.T("TOP DPS")), amber, FontWeights.Bold); var s = Small("★", amber, FontWeights.Bold); sub.Children.Add(f); sub.Children.Add(s); give.Add((f, s)); }
        if (m.TopHeals) { var f = Small("✚ " + Up(L.T("TOP HEALS")), Mint, FontWeights.Bold); var s = Small("✚", Mint, FontWeights.Bold); sub.Children.Add(f); sub.Children.Add(s); give.Add((f, s)); }
        if (cp != null) give.Add((cp, null));
        if (sub.Children.Count > 0) { var sh = new Border { Child = sub }; stack.Children.Add(sh); Fit(sh, null, keep, give); }
        nameCol.Children.Add(stack);
        Grid.SetColumn(nameCol, 1); row.Children.Add(nameCol);
        // the bar: LED segments in the class's colour, glowing; a healer's heals as a green line of segments under it
        var barCol = new Grid { Margin = new Thickness(0, 0, 8, 0) };
        var leds = new TacSketch((dc, s) =>
        {
            double bh = healer ? 13 : 16, y = healer ? 5 : Math.Round((s.Height - bh) / 2);
            Leds(dc, new Rect(0, y, s.Width, bh), bar, c, 4, 1.6);
            if (healer) Leds(dc, new Rect(0, y + bh + 3, s.Width, 4), m.HealShare / Math.Max(.01, e.Members.Max(f => f.HealingEffective) / (double)Math.Max(1, e.Members.Sum(f => f.HealingEffective))), Green, 4, 1.6);
        }) { Effect = Shadow(9, 0, .7, c) };
        barCol.Children.Add(leds);
        // a healer's heals at the end of the green line
        if (healer)
        {
            var heal = T("+" + m.Heal, 10.5, Mint, FontWeights.Bold, Semi); heal.Effect = Shadow(4, 0, 1);
            var hb = new Border { Background = B(A(Colors.Black, .72)), Padding = new Thickness(4, 0, 4, 0), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 0), Child = heal };
            barCol.Children.Add(hb);
        }
        Grid.SetColumn(barCol, 2); row.Children.Add(barCol);
        var dps = T(m.Figure, m.CpOnly ? 16 : 20, m.You ? Rgb(0xFFE3A3) : White, FontWeights.Bold);
        // a fixed column for the widest figure, so every row's bar ends at the same place
        row.ColumnDefinitions[3].Width = new GridLength(Tac.FigCol(dps, "888.8K", "888,888", m.Figure) + 6);
        dps.Effect = Shadow(8, 0, .5, m.You ? amber : White); dps.HorizontalAlignment = HorizontalAlignment.Right;
        dps.LineHeight = 20; dps.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
        // the share, small and quiet, under the DPS
        var share = T(m.ShareText, 10, Rgb(0x8B92A3), FontWeights.SemiBold, Semi); share.HorizontalAlignment = HorizontalAlignment.Right;
        var fig = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0), Children = { dps, share } };
        Grid.SetColumn(fig, 3); row.Children.Add(fig);
        return row;
    }
}
