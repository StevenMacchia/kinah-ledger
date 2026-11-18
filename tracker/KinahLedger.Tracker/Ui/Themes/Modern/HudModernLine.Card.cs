using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

sealed partial class HudModernLine : IThemeCard
{
    /// <summary>
    /// A member's skills card in the Line look: the same hairline frame and faint wash (a touch darker: it is dense), the
    /// class's art in its white ring by the class over the name (underlined for you) and Combat Power at the right; the DPS
    /// big with the share and the damage beside it; the rates in one line; then the skills, each with its figures over a
    /// slim class-coloured bar on a faint track, as the meter's rows; the foot line. Entrance: it fades in a few px from the
    /// left as the bars grow (260 ms); settled in a render.
    /// </summary>
    public FrameworkElement DrawCard(MemberCard c)
    {
        const double Pad = 10;
        double w = 270 + (c.CritCol ? Tac.CardRate : 0) + (c.BackCol ? Tac.CardRate : 0);
        double grow = 1;
        var bars = new List<TacSketch>();
        var stack = new StackPanel { Effect = ShadeFx };
        var tone = c.ClassColor;

        // the head: art in its ring | GLADIATOR over the name | CP at the right
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); head.ColumnDefinitions.Add(new ColumnDefinition()); head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var art = new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(15), BorderThickness = new Thickness(1), BorderBrush = Mod.Br(WhiteC, .72), Background = Mod.Br(Colors.Black, .55), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 9, 0) };
        if (ClassArt.Image(c.Class) is { } img) art.Child = new System.Windows.Controls.Image { Source = img, Width = 23, Height = 23, Stretch = Stretch.Uniform };
        art.ToolTip = c.ClassName;
        head.Children.Add(art);
        var who = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        who.Children.Add(Tt(c.ClassName.ToUpper(CultureInfo.CurrentCulture), 8.5, DimB, FontWeights.Bold));
        var name = Tt(c.Name, 16, WhiteB, FontWeights.Bold);
        name.TextTrimming = TextTrimming.CharacterEllipsis; name.Margin = new Thickness(0, -1, 0, 0);
        if (c.You) name.TextDecorations = LineRow_Under;
        who.Children.Add(name);
        Grid.SetColumn(who, 1); head.Children.Add(who);
        if (c.Cp.Length > 0)
        {
            var cp = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
            var cw = Tt(L.T("CP"), 8.5, DimB, FontWeights.Bold); cw.HorizontalAlignment = HorizontalAlignment.Right;
            var cv = Tt(c.Cp, 14, WhiteB, FontWeights.Bold); cv.HorizontalAlignment = HorizontalAlignment.Right; cv.Margin = new Thickness(0, -1, 0, 0);
            cp.Children.Add(cw); cp.Children.Add(cv);
            Grid.SetColumn(cp, 2); head.Children.Add(cp);
        }
        stack.Children.Add(head);

        // the three figures: the DPS the big one, share and damage beside it; each its word small above
        var stats = new Grid { Margin = new Thickness(0, 9, 0, 0) };
        for (int i = 0; i < 3; i++) stats.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(i == 0 ? 1.25 : 1, GridUnitType.Star) });
        void Stat(int col, string word, string fig, double size)
        {
            var sp = new StackPanel { HorizontalAlignment = col == 0 ? HorizontalAlignment.Left : HorizontalAlignment.Right };
            var wd = Tt(word.ToUpper(CultureInfo.CurrentCulture), 8.5, DimB, FontWeights.Bold); wd.HorizontalAlignment = sp.HorizontalAlignment;
            var fv = Tt(fig, size, WhiteB, FontWeights.Bold); fv.HorizontalAlignment = sp.HorizontalAlignment; fv.Margin = new Thickness(0, -1, 0, 0);
            sp.Children.Add(wd); sp.Children.Add(fv);
            Grid.SetColumn(sp, col); stats.Children.Add(sp);
        }
        Stat(0, L.T("DPS"), c.Dps.Length > 0 ? c.Dps : "–", 22);
        Stat(1, L.T("Share"), c.Share, 15);
        Stat(2, L.T("Damage"), c.Damage, 15);
        stack.Children.Add(stats);

        // the rates in one line: 34% crit · 41% from crits · 12% from behind
        var rates = Tac.Rates(c);
        if (rates.Count > 0)
        {
            var rl = Tt("", 11, SoftB, FontWeights.SemiBold);
            rl.Margin = new Thickness(0, 6, 0, 0); rl.ToolTip = c.RatesLine;
            foreach (var (word, fig) in rates)
            {
                if (rl.Inlines.Count > 0) rl.Inlines.Add(R("  ·  ", 10.5, FaintB));
                rl.Inlines.Add(R(fig, 11.5, WhiteB, FontWeights.Bold)); rl.Inlines.Add(R(" " + word, 11, SoftB));
            }
            stack.Children.Add(rl);
        }

        // the skills: a hairline rule, SKILL · SHARE · DMG (· CRIT · BACK), each skill a row over its slim bar
        if (c.Abilities.Count > 0)
        {
            stack.Children.Add(new Border { Height = 1, Background = RuleB, Margin = new Thickness(0, 8, 0, 4) });
            var hd = Tac.CardGrid(c, 48, 56, 14);
            TextBlock Hd(string s, bool right = true) { var t = Tt(s.ToUpper(CultureInfo.CurrentCulture), 8.5, DimB, FontWeights.Bold); if (right) t.HorizontalAlignment = HorizontalAlignment.Right; return t; }
            hd.Children.Add(Hd(L.T("Skill"), false));
            Tac.Cell(hd, Hd(L.T("Share")), 1, c); Tac.Cell(hd, Hd(L.T("Dmg")), 2, c);
            if (c.CritCol) Tac.Cell(hd, Hd(L.T("Crit")), 3, c);
            if (c.BackCol) Tac.Cell(hd, Hd(L.T("Back")), 4, c);
            stack.Children.Add(hd);
            int i = 0;
            foreach (var a in c.Abilities)
            {
                int k = i++;
                var row = Tac.CardGrid(c, 48, 56, 25);
                row.Background = Brushes.Transparent;
                row.ToolTip = Tac.AbilityTip(a);
                var bar = new TacSketch((dc, s) =>
                {
                    double y = s.Height - 3, bw = s.Width;
                    dc.DrawRoundedRectangle(TrackB, null, new Rect(0, y, bw, 2.5), 1.25, 1.25);
                    double g = Out(Tac.Clamp01(grow * 1.6 - k * .12));
                    double fx = Math.Max(3, bw * a.OfTop) * g;
                    if (fx <= .5) return;
                    dc.DrawRoundedRectangle(Mod.Br(tone, .16), null, new Rect(-1.5, y - 1.5, fx + 3, 5.5), 2.75, 2.75);
                    dc.DrawRoundedRectangle(Frozen(tone), null, new Rect(0, y, fx, 2.5), 1.25, 1.25);
                });
                Grid.SetColumnSpan(bar, 5);
                bars.Add(bar);
                row.Children.Add(bar);
                var an = Tac.AbilityName(a with { Picks = a.Picks.Replace("·", " · ") }, 12, WhiteB, Mod.Face, FontWeights.SemiBold, MintB, 10);
                an.Margin = new Thickness(0, 0, 8, 3);
                row.Children.Add(an);
                TextBlock F(string s, Brush fg, double size, FontWeight wt) { var t = Tt(s, size, fg, wt); t.HorizontalAlignment = HorizontalAlignment.Right; t.Margin = new Thickness(0, 0, 0, 3); return t; }
                Tac.Cell(row, F(a.ShareText, SoftB, 11, FontWeights.SemiBold), 1, c);
                Tac.Cell(row, F(a.Damage, WhiteB, 12, FontWeights.Bold), 2, c);
                if (c.CritCol) Tac.Cell(row, F(a.Crit, a.Crit == "–" ? FaintB : WhiteB, 11, FontWeights.SemiBold), 3, c);
                if (c.BackCol) Tac.Cell(row, F(a.Back, a.Back == "–" ? FaintB : SoftB, 11, FontWeights.SemiBold), 4, c);
                stack.Children.Add(row);
            }
            if (c.MoreLine.Length > 0)
            {
                var more = Tt("+ " + c.MoreLine, 11, DimB, FontWeights.SemiBold); more.Margin = new Thickness(0, 4, 0, 0);
                stack.Children.Add(more);
            }
        }

        // the foot: hits · biggest · healed (mint) · deaths (red)
        if (c.FootLine.Length > 0)
        {
            var foot = Tt("", 11, SoftB, FontWeights.SemiBold);
            foot.TextWrapping = TextWrapping.Wrap;
            foreach (var (s, fg) in new[] { (c.Hits, SoftB), (c.Biggest, SoftB), (c.Healed, MintB), (c.DeathsText, RedB) })
            {
                if (s.Length == 0) continue;
                if (foot.Inlines.Count > 0) foot.Inlines.Add(R("  ·  ", 10.5, FaintB));
                foot.Inlines.Add(R(s, 11, fg));
            }
            stack.Children.Add(new Border { Height = 1, Background = RuleB, Margin = new Thickness(0, 7, 0, 5) });
            stack.Children.Add(foot);
        }

        var frame = new Border
        {
            Width = w, BorderBrush = EdgeB, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12),
            Background = Mod.Br(Colors.Black, .58), Padding = new Thickness(12, 10, 12, 11), Child = stack,
        };
        var slide = new TranslateTransform();
        var body = new Border { Margin = new Thickness(Pad), Child = frame, RenderTransform = slide };
        var card = new Grid { Width = w + 2 * Pad, Children = { body } };
        TextElement.SetFontFamily(card, Mod.Face);
        TextOptions.SetTextFormattingMode(card, TextFormattingMode.Display);
        Tac.CardIn(card, 260, p =>
        {
            double k = Out(Tac.Clamp01(p / .5));
            slide.X = -6 * (1 - k);
            body.Opacity = p <= 0 ? 0 : k;
            grow = p;
            foreach (var b in bars) b.InvalidateVisual();
        });
        return card;
    }

    static TextDecorationCollection LineRow_Under => _cardUnder ??= MakeCardUnder();
    static TextDecorationCollection? _cardUnder;
    static TextDecorationCollection MakeCardUnder()
    {
        var pen = new Pen(WhiteB, 1); pen.Freeze();
        var c = new TextDecorationCollection { new TextDecoration(TextDecorationLocation.Underline, pen, 2.5, TextDecorationUnit.Pixel, TextDecorationUnit.Pixel) };
        c.Freeze();
        return c;
    }
}
