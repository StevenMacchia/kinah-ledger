using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

sealed partial class HudModernGlass
{
    /// <summary>
    /// A member's skills card as a pane of the same frosted glass: the class's emblem in a light ring, the name in white with a
    /// CP chip and the class quietly after it; DPS big, the share and the damage beside it, each over its small word; the rates
    /// as a line; the abilities each as a slim glass pill filled to its share of the top one in the class's colour (bright
    /// leading edge), its name on the pill and its figures at the right; the rest and the foot after hairlines. Entrance: the
    /// pane fades up and the pills fill out after it (220 ms).
    /// </summary>
    public FrameworkElement DrawCard(MemberCard c)
    {
        const double Out0 = 12, In = 14;
        double w = 280 + (c.CritCol ? Tac.CardRate : 0) + (c.BackCol ? Tac.CardRate : 0);
        var tone = c.ClassColor;
        double grow = 1;
        var bars = new List<TacSketch>();
        var stack = new StackPanel { Margin = new Thickness(In, 13, In, 12) };

        // (emblem) Swoly [CP 154.1K]                       Gladiator
        var head = new Grid { Margin = new Thickness(0, 0, 0, 9) };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); head.ColumnDefinitions.Add(new ColumnDefinition()); head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var ring = new Border { Width = 28, Height = 28, CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1.5), BorderBrush = Mod.Br(Colors.White, .35), Margin = new Thickness(0, 0, 9, 0) };
        var icon = ThemeText.Icon(c.Class, 25, c.Name); icon.HorizontalAlignment = HorizontalAlignment.Center;
        ring.Child = icon;
        head.Children.Add(ring);
        var who = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var name = Tt(c.Name, 16, White, FontWeights.Bold); name.TextTrimming = TextTrimming.CharacterEllipsis;
        who.Children.Add(name);
        double cpW = 0;
        if (c.You) { var y = Chip(L.T("You"), White, Frozen(YouInkC), 9.5); y.Margin = new Thickness(7, 1, 0, 0); who.Children.Add(y); y.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity)); cpW += y.DesiredSize.Width; }
        if (c.Cp.Length > 0)
        {
            var chip = Chip(L.T("CP") + " " + c.Cp, Mod.Br(Colors.White, .12), Soft, 10, Mod.Br(Colors.White, .2), caps: false);
            chip.Margin = new Thickness(7, 1, 0, 0);
            chip.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            cpW += chip.DesiredSize.Width;
            who.Children.Add(chip);
        }
        Grid.SetColumn(who, 1); head.Children.Add(who);
        var cls = Tt(c.ClassName, 11, Dim, FontWeights.SemiBold); cls.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(cls, 2); head.Children.Add(cls);
        cls.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        name.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        // the card widens for a long name (to 64 more) before the name is cut
        double need = 37 + name.DesiredSize.Width + cpW + cls.DesiredSize.Width + 4 + 2 * In;
        w = Math.Max(w, Math.Min(w + 64, need));
        double inner = w - 2 * In;
        stack.Width = inner;
        name.MaxWidth = Math.Max(40, inner - 37 - cpW - cls.DesiredSize.Width - 4);
        stack.Children.Add(head);

        // the figures: DPS big, then the share and the damage, each with its word over it
        var read = new Grid();
        foreach (var star in new[] { 1.25, 1.0, 1.0 }) read.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(star, GridUnitType.Star) });
        void Part(int col, string word, string fig, double size, Brush fg)
        {
            var sp = new StackPanel { Margin = new Thickness(col == 0 ? 0 : 8, 0, 0, 0) };
            sp.Children.Add(Tt(word, 10, Dim, FontWeights.SemiBold));
            var fv = Tt(fig, size, fg, FontWeights.Bold); fv.Margin = new Thickness(0, 1, 0, 0);
            sp.Children.Add(fv);
            Grid.SetColumn(sp, col); read.Children.Add(sp);
        }
        Part(0, L.T("DPS"), c.Dps.Length > 0 ? c.Dps : "–", 22, White);
        Part(1, L.T("Share"), c.Share, 16, Soft);
        Part(2, L.T("Damage"), c.Damage, 16, Soft);
        stack.Children.Add(read);

        var rates = Tac.Rates(c);
        if (rates.Count > 0)
        {
            var rl = Tt("", 11, Dim, FontWeights.Medium);
            rl.Margin = new Thickness(0, 7, 0, 0); rl.ToolTip = c.RatesLine; rl.TextTrimming = TextTrimming.CharacterEllipsis;
            foreach (var (word, fig) in rates)
            {
                if (rl.Inlines.Count > 0) rl.Inlines.Add(new System.Windows.Documents.Run("  ·  ") { Foreground = Faint });
                rl.Inlines.Add(new System.Windows.Documents.Run(fig) { Foreground = White, FontWeight = FontWeights.Bold });
                rl.Inlines.Add(new System.Windows.Documents.Run(" " + word));
            }
            stack.Children.Add(rl);
        }

        // the abilities, each a slim glass pill filled to its share of the top one
        if (c.Abilities.Count > 0)
        {
            stack.Children.Add(Hairline(new Thickness(0, 10, 0, 7)));
            var hd = Tac.CardGrid(c, 50, 54, 16);
            TextBlock H(string s, bool right = true) { var t = Tt(s, 9.5, Faint, FontWeights.SemiBold); if (right) t.HorizontalAlignment = HorizontalAlignment.Right; t.Margin = new Thickness(right ? 0 : 8, 0, right ? 6 : 0, 0); return t; }
            hd.Children.Add(H(L.T("Skill"), false));
            Tac.Cell(hd, H(L.T("Share")), 1, c); Tac.Cell(hd, H(L.T("DMG")), 2, c);
            if (c.CritCol) Tac.Cell(hd, H(L.T("Crit")), 3, c);
            if (c.BackCol) Tac.Cell(hd, H(L.T("Back")), 4, c);
            stack.Children.Add(hd);
            int i = 0;
            foreach (var a in c.Abilities)
            {
                int k = i++;
                var row = Tac.CardGrid(c, 50, 54, 24);
                row.Margin = new Thickness(0, 0, 0, 3);
                row.Background = Brushes.Transparent;
                row.ToolTip = Tac.AbilityTip(a);
                var bar = new TacSketch((dc, s) =>
                {
                    var r = new Rect(.5, .5, s.Width - 1, s.Height - 1);
                    var pill = Mod.Pill(r, 8);
                    dc.DrawGeometry(Mod.Br(Colors.White, .05), null, pill);
                    double g = Out(Math.Clamp(grow * 1.6 - k * .1, 0, 1));
                    double fx = Math.Max(3, r.Width * a.OfTop) * g;
                    dc.PushClip(pill);
                    dc.DrawRectangle(Mod.Grad(false, (Mod.A(tone, .2), 0), (Mod.A(tone, .5), 1)), null, new Rect(r.X, r.Y, fx, r.Height));
                    dc.DrawRectangle(Mod.Br(Colors.White, .2), null, new Rect(r.X, r.Y, fx, 1));
                    dc.DrawRectangle(Frozen(Mod.Mix(tone, Colors.White, .25)), null, new Rect(Math.Max(r.X, r.X + fx - 2), r.Y, Math.Min(2, fx), r.Height));
                    dc.Pop();
                    dc.DrawGeometry(null, Mod.P(Mod.Br(Colors.White, .08), 1), pill);
                });
                // the pill under the skill's name only: its bright edge never crosses a figure
                bar.Margin = new Thickness(0, 0, 4, 0);
                bars.Add(bar);
                row.Children.Add(bar);
                var an = Tac.AbilityName(a, 12, White, Mod.Face, FontWeights.SemiBold, Green, 10.5);
                an.Margin = new Thickness(8, 0, 6, 0);
                row.Children.Add(an);
                TextBlock F(string s, Brush fg, double size, FontWeight wt) { var t = Tt(s, size, fg, wt); t.HorizontalAlignment = HorizontalAlignment.Right; t.Margin = new Thickness(0, 0, 6, 0); return t; }
                Tac.Cell(row, F(a.ShareText, Soft, 11, FontWeights.SemiBold), 1, c);
                Tac.Cell(row, F(a.Damage, White, 12, FontWeights.Bold), 2, c);
                if (c.CritCol) Tac.Cell(row, F(a.Crit, a.Crit == "–" ? Faint : White, 11, FontWeights.SemiBold), 3, c);
                if (c.BackCol) Tac.Cell(row, F(a.Back, a.Back == "–" ? Faint : Soft, 11, FontWeights.SemiBold), 4, c);
                stack.Children.Add(row);
            }
            if (c.MoreLine.Length > 0)
            {
                var more = Tt(c.MoreLine, 11, Dim, FontWeights.SemiBold); more.Margin = new Thickness(8, 3, 0, 0);
                stack.Children.Add(more);
            }
        }

        // the foot: hits · biggest · healed (green) · deaths (red)
        if (c.FootLine.Length > 0)
        {
            stack.Children.Add(Hairline(new Thickness(0, 8, 0, 6)));
            var foot = Tt("", 11, Dim, FontWeights.Medium);
            foot.TextWrapping = TextWrapping.Wrap;
            foreach (var (s, fg) in new[] { (c.Hits, (Brush)Dim), (c.Biggest, Dim), (c.Healed, Green), (c.DeathsText, Red) })
            {
                if (s.Length == 0) continue;
                if (foot.Inlines.Count > 0) foot.Inlines.Add(new System.Windows.Documents.Run("  ·  ") { Foreground = Faint });
                foot.Inlines.Add(new System.Windows.Documents.Run(s) { Foreground = fg });
            }
            stack.Children.Add(foot);
        }

        // the glass, with room round it for its drop (inside the card's own room)
        var glass = new TacSketch((dc, s) =>
        {
            dc.PushTransform(new TranslateTransform(Out0, Out0 - 4));
            GlassPanel(dc, new Size(s.Width - 2 * Out0, s.Height - 2 * Out0), 16, 1, -1);
            dc.Pop();
        });
        var pane = new Grid { Children = { glass, new Border { Margin = new Thickness(Out0, Out0 - 4, Out0, Out0 + 4), Child = stack } } };
        var card = new Grid { Width = w + 2 * Out0, Children = { pane } };
        System.Windows.Documents.TextElement.SetFontFamily(card, Mod.Face);
        TextOptions.SetTextFormattingMode(card, TextFormattingMode.Display);
        Tac.CardIn(card, 220, p =>
        {
            pane.Opacity = .3 + .7 * Out(Math.Clamp(p / .6, 0, 1));
            grow = p;
            foreach (var b in bars) b.InvalidateVisual();
        });
        return card;
    }
}
