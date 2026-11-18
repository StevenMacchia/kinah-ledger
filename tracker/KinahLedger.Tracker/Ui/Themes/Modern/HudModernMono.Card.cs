using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

sealed partial class HudModernMono : IThemeCard
{
    /// <summary>
    /// A member's skills card, Swiss: the near-black panel with its hairline edge; the grey emblem, the name (yours in the
    /// accent) and its CP, the class small at the right; DPS, share and damage as three figures under small labels, split by
    /// hairlines; the rates as labelled figures; the abilities as a table, each over a 2 px bar (its share of the top one,
    /// off-white, yours in the accent); the rest and the foot after hairlines. No entrance: the card is there.
    /// </summary>
    public FrameworkElement DrawCard(MemberCard c)
    {
        const double Pad = 14;
        double w = 270 + (c.CritCol ? Tac.CardRate : 0) + (c.BackCol ? Tac.CardRate : 0);
        var stack = new StackPanel();

        // [emblem] Swoly  CP 154.1K                 GLADIATOR
        var head = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); head.ColumnDefinitions.Add(new ColumnDefinition()); head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var icon = ClassMark(c.Class, c.Name, 20); icon.Margin = new Thickness(0, 0, 10, 0);
        head.Children.Add(icon);
        var who = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var name = Tt(c.Name, 15, c.You ? Accent : Paper, FontWeights.SemiBold); name.TextTrimming = TextTrimming.CharacterEllipsis;
        who.Children.Add(name);
        FrameworkElement? cp = null;
        if (c.Cp.Length > 0)
        {
            var cpl = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 2, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            cpl.Children.Add(Tt(L.T("CP"), 9, Grey, FontWeights.SemiBold));
            var cv = Tt(c.Cp, 11, Soft, FontWeights.SemiBold); cv.Margin = new Thickness(4, 0, 0, 0); cpl.Children.Add(cv);
            who.Children.Add(cpl);
            cp = cpl;
        }
        Grid.SetColumn(who, 1); head.Children.Add(who);
        var cls = Label(c.ClassName.ToUpperInvariant()); cls.Margin = new Thickness(10, 1, 0, 0);
        Grid.SetColumn(cls, 2); head.Children.Add(cls);
        // the card widens for a long name (to 64 more) before the name is cut
        double cpW = cp != null ? Mod.W(cp) : 0, clsW = Mod.W(cls), iconW = 30;
        double need = iconW + Mod.W(name) + cpW + clsW + 4;
        double inner = Math.Max(w - 2 * Pad, Math.Min(w - 2 * Pad + 64, need));
        name.MaxWidth = Math.Max(60, inner - iconW - cpW - clsW - 4);
        stack.Width = inner;
        stack.Children.Add(head);
        stack.Children.Add(Hairline(new Thickness(0, 0, 0, 10)));

        // DPS | SHARE | DAMAGE: the label small over each figure, the figures on one baseline
        var read = new Grid();
        foreach (var (wd, star) in new[] { (1.2, true), (1.0, false), (1.0, true), (1.0, false), (1.0, true) })
            read.ColumnDefinitions.Add(new ColumnDefinition { Width = star ? new GridLength(wd, GridUnitType.Star) : new GridLength(1) });
        void Part(int col, string word, string fig, Brush fg, double size)
        {
            var g = new Grid { Height = 38, Margin = new Thickness(col == 0 ? 0 : 12, 0, 0, 0) };
            var lb = Label(word); lb.VerticalAlignment = VerticalAlignment.Top; g.Children.Add(lb);
            var fv = Tt(fig, size, fg, FontWeights.Bold); fv.VerticalAlignment = VerticalAlignment.Bottom; fv.Margin = new Thickness(0, 0, 0, -(size * .2) + 2);
            g.Children.Add(fv);
            Grid.SetColumn(g, col); read.Children.Add(g);
        }
        foreach (int col in new[] { 1, 3 }) { var d = new Border { Background = RuleB, Width = 1 }; Grid.SetColumn(d, col); read.Children.Add(d); }
        Part(0, L.T("DPS"), c.Dps.Length > 0 ? c.Dps : "–", Paper, 22);
        Part(2, L.T("SHARE"), c.Share, Soft, 16);
        Part(4, L.T("DAMAGE"), c.Damage, Soft, 16);
        stack.Children.Add(read);

        // CRIT 34%   FROM CRITS 41%   FROM BEHIND 12% (the whole sentence on hover)
        var rates = Tac.Rates(c);
        if (rates.Count > 0)
        {
            var rl = new TextBlock { FontSize = 9, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 0), ToolTip = c.RatesLine, TextTrimming = TextTrimming.CharacterEllipsis };
            System.Windows.Documents.Typography.SetNumeralAlignment(rl, FontNumeralAlignment.Tabular);
            foreach (var (word, fig) in rates)
            {
                if (rl.Inlines.Count > 0) rl.Inlines.Add(new System.Windows.Documents.Run("    "));
                rl.Inlines.Add(new System.Windows.Documents.Run(word.ToUpperInvariant() + "  ") { Foreground = Grey });
                rl.Inlines.Add(new System.Windows.Documents.Run(fig) { Foreground = Paper, FontSize = 12, FontWeight = FontWeights.Bold });
            }
            stack.Children.Add(rl);
        }

        // the abilities: SKILL · SHARE · DMG (· CRIT · BACK), each over a 2 px bar
        if (c.Abilities.Count > 0)
        {
            stack.Children.Add(Hairline(new Thickness(0, 10, 0, 8)));
            var hd = Tac.CardGrid(c, 48, 56, 14);
            TextBlock H(string s, bool right = true) { var t = Label(s); t.FontSize = 8.5; if (right) t.HorizontalAlignment = HorizontalAlignment.Right; return t; }
            hd.Children.Add(H(L.T("SKILL"), false));
            Tac.Cell(hd, H(L.T("SHARE")), 1, c); Tac.Cell(hd, H(L.T("DMG")), 2, c);
            if (c.CritCol) Tac.Cell(hd, H(L.T("CRIT")), 3, c);
            if (c.BackCol) Tac.Cell(hd, H(L.T("BACK")), 4, c);
            stack.Children.Add(hd);
            var fill = c.You ? Accent : Frozen(Mod.A(PaperC, .82));
            foreach (var a in c.Abilities)
            {
                var row = Tac.CardGrid(c, 48, 56, 24);
                row.Background = Brushes.Transparent;
                row.ToolTip = Tac.AbilityTip(a);
                var bar = new TacSketch((dc, s) =>
                {
                    double by = s.Height - 2;
                    dc.DrawRectangle(TrackB, null, new Rect(0, by, s.Width, 2));
                    double fx = Math.Round(Math.Max(2, s.Width * a.OfTop));
                    dc.DrawRectangle(fill, null, new Rect(0, by, fx, 2));
                });
                Grid.SetColumnSpan(bar, row.ColumnDefinitions.Count);   // under the whole row, as the meter's rows
                row.Children.Add(bar);
                var an = Tac.AbilityName(a, 12, Paper, Mod.Face, FontWeights.Medium, Accent, 10);
                an.Margin = new Thickness(0, 0, 8, 6);
                row.Children.Add(an);
                TextBlock F(string s, Brush fg, double size, FontWeight wt) { var t = Tt(s, size, fg, wt); t.HorizontalAlignment = HorizontalAlignment.Right; t.Margin = new Thickness(0, 0, 0, 6); return t; }
                Tac.Cell(row, F(a.ShareText, Grey, 11, FontWeights.Medium), 1, c);
                Tac.Cell(row, F(a.Damage, Paper, 12, FontWeights.Bold), 2, c);
                if (c.CritCol) Tac.Cell(row, F(a.Crit, a.Crit == "–" ? Grey : Soft, 11, FontWeights.SemiBold), 3, c);
                if (c.BackCol) Tac.Cell(row, F(a.Back, a.Back == "–" ? Grey : Soft, 11, FontWeights.SemiBold), 4, c);
                stack.Children.Add(row);
            }
            if (c.MoreLine.Length > 0)
            {
                var more = Tt(c.MoreLine, 11, Grey, FontWeights.Medium); more.Margin = new Thickness(0, 4, 0, 0);
                stack.Children.Add(more);
            }
        }

        // the foot: hits · biggest · healed (green) · deaths (red)
        if (c.FootLine.Length > 0)
        {
            stack.Children.Add(Hairline(new Thickness(0, 9, 0, 7)));
            var foot = new TextBlock { FontSize = 11, FontWeight = FontWeights.Medium, TextWrapping = TextWrapping.Wrap };
            System.Windows.Documents.Typography.SetNumeralAlignment(foot, FontNumeralAlignment.Tabular);
            foreach (var (s, fg) in new[] { (c.Hits, Grey), (c.Biggest, Grey), (c.Healed, Green), (c.DeathsText, Red) })
            {
                if (s.Length == 0) continue;
                if (foot.Inlines.Count > 0) foot.Inlines.Add(new System.Windows.Documents.Run("  ·  ") { Foreground = Grey });
                foot.Inlines.Add(new System.Windows.Documents.Run(s) { Foreground = fg });
            }
            stack.Children.Add(foot);
        }

        var card = new Border
        {
            Child = stack, Padding = new Thickness(Pad, 13, Pad, 12), Background = Frozen(Mod.A(BgC, .98)), BorderBrush = Edge, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(Radius), HorizontalAlignment = HorizontalAlignment.Left,
        };
        System.Windows.Documents.TextElement.SetFontFamily(card, Mod.Face);
        TextOptions.SetTextFormattingMode(card, TextFormattingMode.Display);
        return card;
    }
}
