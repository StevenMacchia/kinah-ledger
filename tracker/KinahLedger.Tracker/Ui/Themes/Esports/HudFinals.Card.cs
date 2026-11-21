using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

sealed partial class HudFinals : IThemeCard
{
    /// <summary>
    /// A member's skills card as a player card on the finals' stage: the same black plate under its stage light and gold rim.
    /// The class's art in a gold ring, the name in tall condensed type, YOU in gold and the Combat Power after it, the class in
    /// small gold caps at the right, a gold rule; DPS big in white, the share and the damage beside it, each under a small gold
    /// label; the rates as a line; each ability on a graphite bar lit from inside in the class's colour (the top ability's
    /// brushed gold), its figures at the right; the foot after a faint gold rule. Entrance: the plate fades up as a band of
    /// gold crosses it, and the bars fill out after it.
    /// </summary>
    public FrameworkElement DrawCard(MemberCard c)
    {
        const double Out0 = 12, In = 15;
        double w = 280 + (c.CritCol ? Tac.CardRate : 0) + (c.BackCol ? Tac.CardRate : 0);
        var tone = c.ClassColor;
        double grow = 1;
        var bars = new List<TacSketch>();
        var stack = new StackPanel { Margin = new Thickness(In, 13, In, 12) };

        // (art) Swoly [YOU] CP 154.1K                       GLADIATOR
        var head = new Grid { Margin = new Thickness(0, 0, 0, 7) };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); head.ColumnDefinitions.Add(new ColumnDefinition()); head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var ring = new Border { Width = 28, Height = 28, CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1.5), BorderBrush = GoldMetal, Background = Mod.Br(Black0, .7), Margin = new Thickness(0, 0, 9, 0) };
        var icon = ThemeText.Icon(c.Class, 23, c.Name); icon.HorizontalAlignment = HorizontalAlignment.Center; icon.VerticalAlignment = VerticalAlignment.Center;
        ring.Child = icon;
        head.Children.Add(ring);
        var who = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var name = T(c.Name, 19, White, FontWeights.SemiBold, cond: true); name.TextTrimming = TextTrimming.CharacterEllipsis;
        who.Children.Add(name);
        double extra = 0;
        if (c.You) { var y = Chip(L.T("YOU"), filled: true); y.Margin = new Thickness(7, 1, 0, 0); who.Children.Add(y); extra += Mod.W(y) + 7; }
        if (c.Cp.Length > 0)
        {
            var cp = T("", 12, White, FontWeights.SemiBold);
            cp.Margin = new Thickness(8, 2, 0, 0);
            cp.Inlines.Add(new System.Windows.Documents.Run(L.T("CP") + " ") { FontSize = 10.5, FontStretch = FontStretches.Condensed, Foreground = AuSoft });
            cp.Inlines.Add(new System.Windows.Documents.Run(c.Cp));
            who.Children.Add(cp); extra += Mod.W(cp) + 8;
        }
        Grid.SetColumn(who, 1); head.Children.Add(who);
        var cls = Label(c.ClassName, 11, AuSoft); cls.Margin = new Thickness(8, 1, 0, 0);
        Grid.SetColumn(cls, 2); head.Children.Add(cls);
        // the card widens for a long name (to 64 more) before the name is cut
        double clsW = Mod.W(cls) + 8;
        double need = 37 + Mod.W(name) + extra + clsW + 4 + 2 * In;
        w = Math.Max(w, Math.Min(w + 64, need));
        double inner = w - 2 * In;
        stack.Width = inner;
        name.MaxWidth = Math.Max(40, inner - 37 - extra - clsW - 4);
        stack.Children.Add(head);
        stack.Children.Add(GoldRule(new Thickness(-4, 0, -4, 9)));

        // the figures: DPS big, then the share and the damage, each under its small gold label
        var read = new Grid();
        foreach (var star in new[] { 1.25, 1.0, 1.0 }) read.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(star, GridUnitType.Star) });
        void Part(int col, string word, string fig, double size, Brush fg)
        {
            var sp = new StackPanel { Margin = new Thickness(col == 0 ? 0 : 8, 0, 0, 0) };
            sp.Children.Add(Label(word, 10, AuSoft));
            var fv = T(fig, size, fg, FontWeights.SemiBold); fv.Margin = new Thickness(0, 1, 0, 0);
            sp.Children.Add(fv);
            Grid.SetColumn(sp, col); read.Children.Add(sp);
        }
        Part(0, L.T("DPS"), c.Dps.Length > 0 ? c.Dps : "–", 25, White);
        Part(1, L.T("Share"), c.Share, 17, Soft);
        Part(2, L.T("Damage"), c.Damage, 17, Soft);
        stack.Children.Add(read);

        var rates = Tac.Rates(c);
        if (rates.Count > 0)
        {
            var rl = T("", 11.5, Quiet, FontWeights.Normal);
            rl.Margin = new Thickness(0, 7, 0, 0); rl.ToolTip = c.RatesLine; rl.TextTrimming = TextTrimming.CharacterEllipsis;
            foreach (var (word, fig) in rates)
            {
                if (rl.Inlines.Count > 0) rl.Inlines.Add(new System.Windows.Documents.Run("  ·  ") { Foreground = Au });
                rl.Inlines.Add(new System.Windows.Documents.Run(fig) { Foreground = White, FontWeight = FontWeights.SemiBold });
                rl.Inlines.Add(new System.Windows.Documents.Run(" " + word));
            }
            stack.Children.Add(rl);
        }

        // the abilities, each a graphite bar lit in the class's colour (the top one's brushed gold), as long as its part of the top
        if (c.Abilities.Count > 0)
        {
            stack.Children.Add(GoldRule(new Thickness(0, 10, 0, 7), .45));
            var hd = Tac.CardGrid(c, 50, 54, 16);
            TextBlock H(string s, bool right = true) { var t = Label(s, 9.5, Mod.Br(AuLightC, .62)); if (right) t.HorizontalAlignment = HorizontalAlignment.Right; t.Margin = new Thickness(right ? 0 : 8, 0, right ? 6 : 0, 0); return t; }
            hd.Children.Add(H(L.T("Skill"), false));
            Tac.Cell(hd, H(L.T("Share")), 1, c); Tac.Cell(hd, H(L.T("DMG")), 2, c);
            if (c.CritCol) Tac.Cell(hd, H(L.T("Crit")), 3, c);
            if (c.BackCol) Tac.Cell(hd, H(L.T("Back")), 4, c);
            stack.Children.Add(hd);
            int i = 0;
            foreach (var a in c.Abilities)
            {
                int k = i++;
                bool top = k == 0;
                var row = Tac.CardGrid(c, 50, 54, 24);
                row.Margin = new Thickness(0, 0, 0, 3);
                row.Background = Brushes.Transparent;
                row.ToolTip = Tac.AbilityTip(a);
                var bar = new TacSketch((dc, s) =>
                {
                    var r = new Rect(.5, .5, s.Width - 1, s.Height - 1);
                    var tile = Mod.Pill(r, 3);
                    dc.DrawGeometry(Mod.Grad(true, (Color.FromRgb(0x20, 0x20, 0x24), 0), (Color.FromRgb(0x16, 0x16, 0x19), 1)), null, tile);
                    double g = Out(Math.Clamp(grow * 1.6 - k * .1, 0, 1));
                    double fx = Math.Max(3, r.Width * a.OfTop) * g;
                    var fill = new Rect(r.X, r.Y, fx, r.Height);
                    dc.PushClip(tile);
                    if (top)
                    {
                        dc.DrawRectangle(Mod.Grad(false, (Color.FromRgb(0x4A, 0x36, 0x12), 0), (Color.FromRgb(0x9E, 0x7A, 0x30), .55), (Color.FromRgb(0x6A, 0x4E, 0x1A), 1)), null, fill);
                        dc.PushOpacity(.6); dc.DrawRectangle(Brushed, null, fill); dc.Pop();
                        dc.DrawRectangle(Mod.Br(AuPaleC, .7), null, new Rect(r.X, r.Y, fx, 1));
                    }
                    else
                    {
                        dc.DrawRectangle(Mod.Grad(false, (Mod.A(tone, .1), 0), (Mod.A(tone, .36), 1)), null, fill);
                        dc.DrawRectangle(Mod.Grad(true, (Mod.A(tone, .45), 0), (Mod.A(tone, 0), .3), (Mod.A(tone, 0), .7), (Mod.A(tone, .35), 1)), null, fill);
                        dc.DrawRectangle(Mod.Br(Mod.Mix(tone, Colors.White, .2), .75), null, new Rect(Math.Max(r.X, r.X + fx - 1.5), r.Y, Math.Min(1.5, fx), r.Height));
                    }
                    dc.Pop();
                    dc.DrawGeometry(null, Mod.P(Mod.Br(Colors.White, .07), 1), tile);
                });
                // the bar under the skill's name only: its edge never crosses a figure
                bar.Margin = new Thickness(0, 0, 4, 0);
                bars.Add(bar);
                row.Children.Add(bar);
                var an = Tac.AbilityName(a, 12.5, White, Bahn, FontWeights.SemiBold, Mint2, 10.5);
                an.Margin = new Thickness(8, 0, 6, 0);
                an.Effect = Shade();
                row.Children.Add(an);
                TextBlock F(string s, Brush fg, double size) { var t = T(s, size, fg, FontWeights.SemiBold); t.HorizontalAlignment = HorizontalAlignment.Right; t.Margin = new Thickness(0, 0, 6, 0); return t; }
                Tac.Cell(row, F(a.ShareText, Soft, 11.5), 1, c);
                Tac.Cell(row, F(a.Damage, top ? AuPale : White, 12.5), 2, c);
                if (c.CritCol) Tac.Cell(row, F(a.Crit, a.Crit == "–" ? Faint : White, 11.5), 3, c);
                if (c.BackCol) Tac.Cell(row, F(a.Back, a.Back == "–" ? Faint : Soft, 11.5), 4, c);
                stack.Children.Add(row);
            }
            if (c.MoreLine.Length > 0)
            {
                var more = T(c.MoreLine, 11.5, Quiet, FontWeights.Normal); more.Margin = new Thickness(8, 3, 0, 0);
                stack.Children.Add(more);
            }
        }

        // the foot: hits · biggest · healed (mint) · deaths (coral)
        if (c.FootLine.Length > 0)
        {
            stack.Children.Add(GoldRule(new Thickness(0, 8, 0, 6), .4));
            var foot = T("", 11.5, Quiet, FontWeights.Normal);
            foot.TextWrapping = TextWrapping.Wrap;
            foreach (var (s, fg) in new[] { (c.Hits, (Brush)Quiet), (c.Biggest, Quiet), (c.Healed, Mint2), (c.DeathsText, Coral) })
            {
                if (s.Length == 0) continue;
                if (foot.Inlines.Count > 0) foot.Inlines.Add(new System.Windows.Documents.Run("  ·  ") { Foreground = Au });
                foot.Inlines.Add(new System.Windows.Documents.Run(s) { Foreground = fg });
            }
            stack.Children.Add(foot);
        }

        // the plate, with room round it for its drop (inside the card's own room)
        var plate = new TacSketch((dc, s) =>
        {
            dc.PushTransform(new TranslateTransform(Out0, Out0 - 4));
            Plate(dc, new Size(s.Width - 2 * Out0, s.Height - 2 * Out0), 5, 1);
            dc.Pop();
        });
        // the entrance's light: a band crossing the plate on its transform (the plate itself is drawn once)
        var light = new Band(AuPaleC, .22, 46) { Margin = new Thickness(Out0 + 2, Out0 - 2, Out0 + 2, Out0 + 6) };
        var pane = new Grid { Children = { plate, light, new Border { Margin = new Thickness(Out0, Out0 - 4, Out0, Out0 + 4), Child = stack } } };
        var card = new Grid { Width = w + 2 * Out0, Children = { pane } };
        System.Windows.Documents.TextElement.SetFontFamily(card, Bahn);
        TextOptions.SetTextFormattingMode(card, TextFormattingMode.Display);
        Tac.CardIn(card, 420, p =>
        {
            pane.Opacity = .3 + .7 * Out(Math.Clamp(p / .4, 0, 1));
            grow = Math.Clamp(p / .55, 0, 1);
            light.At(p is > 0 and < 1 ? Seg(p, .1, 1) : -1);
            foreach (var b in bars) b.InvalidateVisual();
        });
        return card;
    }
}
