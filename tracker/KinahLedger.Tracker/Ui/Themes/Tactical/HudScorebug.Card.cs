using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

sealed partial class HudScorebug : IThemeCard
{
    /// <summary>
    /// A member's skills card as a broadcast's player-stats card: a slanted name bar (the class's emblem on its colour, the
    /// class over the name on dark, Combat Power on slate beside it), the three big stats as slanted cells (DPS on red, the
    /// share on dark, the damage on light), the rates as small chips, then the skills board on a dark ground under a red rule
    /// (each skill's share of the top one a wash in the class's colour with a bar at its foot), and a dark foot strip.
    /// Entrance: the card slides in and fades up as a light crosses the name bar, the skills' bars filling one after another
    /// (280 ms); settled in a render.
    /// </summary>
    public FrameworkElement DrawCard(MemberCard c)
    {
        const double Pad = 12;
        double w = 274 + (c.CritCol ? Tac.CardRate : 0) + (c.BackCol ? Tac.CardRate : 0);
        double grow = 1;
        var bars = new List<TacSketch>();
        var stack = new StackPanel { Width = w };

        // the name bar: emblem on the class's colour | GLADIATOR over the name on dark | CP on slate
        var head = new Grid { Height = 42 };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); head.ColumnDefinitions.Add(new ColumnDefinition()); head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var emblem = new Block(c.ClassColor, ClassArt.Icon(c.Class, 26, c.Name), 42, new Thickness(10, 0, 12, 0), leanLeft: false);
        head.Children.Add(emblem.Root);
        var who = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var cls = Cap(c.Class, Grey, 9.5); cls.Margin = new Thickness(0, 0, 0, -3);
        who.Children.Add(cls);
        var name = T(c.Name, 20, c.You ? Yellow : Frozen(c.NameColor)); name.TextTrimming = TextTrimming.CharacterEllipsis;
        who.Children.Add(name);
        var plate = new Block(InkC0, who, 42, new Thickness(14, 0, 12, 0)) { };
        plate.Root.Margin = new Thickness(-Lean + 2.5, 0, 0, 0);
        Grid.SetColumn(plate.Root, 1); head.Children.Add(plate.Root);
        if (c.Cp.Length > 0)
        {
            var cp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var cw = Cap(L.T("CP"), Frozen(Color.FromRgb(0xC9, 0xD1, 0xDC)), 9.5); cw.HorizontalAlignment = HorizontalAlignment.Right; cw.Margin = new Thickness(0, 0, 0, -3);
            cp.Children.Add(cw);
            var cv = T(c.Cp, 17, White); cv.HorizontalAlignment = HorizontalAlignment.Right;
            cp.Children.Add(cv);
            var cb = new Block(SlateC, cp, 42, new Thickness(13, 0, 11, 0), leanRight: false);
            cb.Root.Margin = new Thickness(-Lean + 2.5, 0, 0, 0);
            Grid.SetColumn(cb.Root, 2); head.Children.Add(cb.Root);
        }
        stack.Children.Add(head);

        // the three stats: DPS on red (the card's lead), share on dark, damage on light; the word small, the figure big at the right
        var stats = new Grid { Height = 40, Margin = new Thickness(0, 3, 0, 0) };
        for (int i = 0; i < 3; i++) stats.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(i == 0 ? 1.12 : 1, GridUnitType.Star) });
        void Stat(int col, Color bg, string word, Brush wordFg, string fig, Brush fg, double size)
        {
            var g = new Grid();
            var wd = Cap(word, wordFg, 9.5); wd.VerticalAlignment = VerticalAlignment.Top; wd.Margin = new Thickness(0, 3, 0, 0);
            g.Children.Add(wd);
            var fv = T(fig, size, fg); fv.HorizontalAlignment = HorizontalAlignment.Right; fv.VerticalAlignment = VerticalAlignment.Bottom; fv.Margin = new Thickness(0, 0, 0, 1);
            g.Children.Add(fv);
            var b = new Block(bg, g, 40, new Thickness(col == 0 ? 10 : 13, 0, 12, 0), leanLeft: col != 0, leanRight: col != 2);
            b.Root.Children[1].SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Stretch);
            if (col > 0) b.Root.Margin = new Thickness(-Lean + 2.5, 0, 0, 0);
            Grid.SetColumn(b.Root, col); stats.Children.Add(b.Root);
        }
        // the DPS leads: the biggest figure on the only bright block; share and damage on dark and slate
        Stat(0, RedC, L.T("DPS"), Frozen(Color.FromArgb(0xE0, 0xFF, 0xFF, 0xFF)), c.Dps.Length > 0 ? c.Dps : "–", White, 26);
        Stat(1, InkC0, L.T("Share"), Grey, c.Share, White, 18);
        Stat(2, SlateC, L.T("Damage"), Frozen(Color.FromRgb(0xC9, 0xD1, 0xDC)), c.Damage, White, 18);
        stack.Children.Add(stats);

        // the rates as chips: CRIT 34% · FROM CRITS 41% · FROM BEHIND 12% (the whole sentence on hover)
        var rates = Tac.Rates(c);
        if (rates.Count > 0)
        {
            // the chips run the card's whole width (no empty strip at the right), the word at the left, the figure at the right
            var rr = new Grid { Margin = new Thickness(0, 3, 0, 0), ToolTip = c.RatesLine };
            int n = 0;
            foreach (var (word, fig) in rates)
            {
                rr.ColumnDefinitions.Add(new ColumnDefinition());
                var sp = new DockPanel { LastChildFill = false };
                var fv = T(fig, 14, Yellow); DockPanel.SetDock(fv, Dock.Right); sp.Children.Add(fv);
                var wd = Cap(word, Grey, 9.5); wd.Margin = new Thickness(0, 1, 6, 0); sp.Children.Add(wd);
                var b = new Block(InkC0, sp, 22, new Thickness(n == 0 ? 9 : 12, 0, n == rates.Count - 1 ? 9 : 11, 0), leanLeft: n > 0, leanRight: n < rates.Count - 1);
                b.Root.Children[1].SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
                if (n > 0) b.Root.Margin = new Thickness(-Lean + 2.5, 0, 0, 0);
                Grid.SetColumn(b.Root, n++); rr.Children.Add(b.Root);
            }
            stack.Children.Add(rr);
        }

        // the skills board: a red rule, SKILL · SHARE · DMG (· CRIT · BACK), one row each
        if (c.Abilities.Count > 0)
        {
            var board = new StackPanel { Margin = new Thickness(9, 4, 9, 5) };
            var hd = Tac.CardGrid(c, 46, 56, 15);
            TextBlock H(string s, bool right = true) { var t = Cap(s, Grey, 9); if (right) t.HorizontalAlignment = HorizontalAlignment.Right; return t; }
            hd.Children.Add(H(L.T("Skill"), false));
            Tac.Cell(hd, H(L.T("Share")), 1, c); Tac.Cell(hd, H(L.T("Dmg")), 2, c);
            if (c.CritCol) Tac.Cell(hd, H(L.T("Crit")), 3, c);
            if (c.BackCol) Tac.Cell(hd, H(L.T("Back")), 4, c);
            board.Children.Add(hd);
            int i = 0;
            foreach (var a in c.Abilities)
            {
                int k = i++;
                var row = Tac.CardGrid(c, 46, 56, 23);
                row.Margin = new Thickness(0, 1, 0, 0);
                row.Background = Brushes.Transparent;
                row.ToolTip = Tac.AbilityTip(a);
                var tone = c.ClassColor;
                var bar = new TacSketch((dc, s) =>
                {
                    double bw = s.Width - 4, h = s.Height;
                    dc.DrawRectangle(Frozen(Color.FromArgb(0x1C, 0xFF, 0xFF, 0xFF)), null, new Rect(0, h - 2, bw, 2));
                    double g = Out(Tac.Clamp01(grow * 1.6 - k * .12));
                    double fx = Math.Max(3, bw * a.OfTop) * g;
                    if (fx <= .5) return;
                    dc.DrawRectangle(Tac.Grad(false, (Tac.A(tone, .34), 0), (Tac.A(tone, .08), 1)), null, new Rect(0, 0, fx, h - 2));
                    dc.DrawRectangle(Frozen(tone), null, new Rect(0, h - 2, fx, 2));
                    dc.DrawRectangle(Frozen(tone), null, new Rect(0, 0, 2, h - 2));
                });
                bars.Add(bar);
                row.Children.Add(bar);
                var an = Tac.AbilityName(a, 14, White, Face, FontWeights.SemiBold, Yellow, 11.5, Tight);
                an.Margin = new Thickness(7, 0, 8, 1);
                row.Children.Add(an);
                TextBlock F(string s, Brush fg, double size, FontWeight? wt = null) { var t = T(s, size, fg, wt); t.HorizontalAlignment = HorizontalAlignment.Right; t.Margin = new Thickness(0, 0, 0, 1); return t; }
                Tac.Cell(row, F(a.ShareText, Frozen(Color.FromRgb(0xB8, 0xC0, 0xCC)), 13, FontWeights.SemiBold), 1, c);
                Tac.Cell(row, F(a.Damage, White, 14.5), 2, c);
                if (c.CritCol) Tac.Cell(row, F(a.Crit, a.Crit == "–" ? Sub : Yellow, 13), 3, c);
                if (c.BackCol) Tac.Cell(row, F(a.Back, a.Back == "–" ? Sub : Frozen(Tac.A(YellowC, .78)), 13, FontWeights.SemiBold), 4, c);
                board.Children.Add(row);
            }
            if (c.MoreLine.Length > 0)
            {
                var more = T("+ " + c.MoreLine, 12, Grey, FontWeights.SemiBold); more.Margin = new Thickness(7, 3, 0, 0);
                board.Children.Add(more);
            }
            var ground = new Border { Margin = new Thickness(0, 3, 0, 0), Background = Frozen(Tac.A(InkC0, .96)), BorderBrush = Red, BorderThickness = new Thickness(0, 2, 0, 0), Child = board };
            stack.Children.Add(ground);
        }

        // the foot strip: hits · biggest · healed (green) · deaths (red)
        if (c.FootLine.Length > 0)
        {
            var foot = new TextBlock { FontFamily = Face, FontStretch = Tight, FontSize = 12.5, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
            System.Windows.Documents.Typography.SetNumeralAlignment(foot, FontNumeralAlignment.Tabular);
            foreach (var (s, fg) in new[] { (c.Hits, Grey), (c.Biggest, Grey), (c.Healed, Green), (c.DeathsText, Frozen(Color.FromRgb(0xFF, 0x6B, 0x78))) })
            {
                if (s.Length == 0) continue;
                if (foot.Inlines.Count > 0) foot.Inlines.Add(new System.Windows.Documents.Run("   ·   ") { Foreground = Sub });
                foot.Inlines.Add(new System.Windows.Documents.Run(s) { Foreground = fg });
            }
            stack.Children.Add(new Border { Background = Frozen(Color.FromRgb(0x08, 0x09, 0x0D)), BorderBrush = Frozen(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)), BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(16, 4, 10, 5), Child = foot });
        }

        // the card's shadow, cast down-right on the game (inside its own room: the pop-up is cut at the card's bounds)
        var shade = new TacSketch((dc, s) =>
        {
            var r = new Rect(Pad + 4, Pad + 5, s.Width - 2 * Pad, s.Height - 2 * Pad);
            dc.DrawRectangle(Frozen(Color.FromArgb(0x30, 0, 0, 0)), null, new Rect(r.X - 2, r.Y - 2, r.Width + 4, r.Height + 4));
            dc.DrawRectangle(Frozen(Color.FromArgb(0x60, 0, 0, 0)), null, r);
        });
        var slide = new TranslateTransform();
        var body = new Border { Margin = new Thickness(Pad), Child = stack, RenderTransform = slide };
        var card = new Grid { Width = w + 2 * Pad, Children = { shade, body } };
        System.Windows.Documents.TextElement.SetFontFamily(card, Face);
        TextOptions.SetTextFormattingMode(card, TextFormattingMode.Display);
        Tac.CardIn(card, 280, p =>
        {
            double k = Out(Tac.Clamp01(p / .45));
            slide.X = -8 * (1 - k);
            body.Opacity = shade.Opacity = p <= 0 ? 0 : k;
            grow = p;
            plate.Sweep = p >= 1 ? -1 : Tac.Clamp01((p - .1) / .8); plate.Art.InvalidateVisual();
            foreach (var b in bars) b.InvalidateVisual();
        });
        return card;
    }
}
