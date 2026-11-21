using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

sealed partial class HudTelestrator : IThemeCard
{
    /// <summary>
    /// A member's skills card as the analyst's breakdown: the board (charcoal glass, the tactics grid, the replay frame's
    /// brackets); the member's head on a flat lane in their class colour (the art, the name, CP, YOU, the class small at the
    /// right); DPS big, share and damage each under its small word; the rates; each
    /// ability on its own flat class lane as long as its part of the top one;
    /// the rest and the foot after chalk lines. Entrance: it settles in from the left, the lanes run out, the pen underlines the DPS.
    /// </summary>
    public FrameworkElement DrawCard(MemberCard c)
    {
        const double In = 12;
        double w = 270 + (c.CritCol ? Tac.CardRate : 0) + (c.BackCol ? Tac.CardRate : 0);
        var tone = c.ClassColor;
        var lane = Mod.Mix(tone, BoardDeepC, .3);
        double grow = 1, pen = 1;
        var stack = new StackPanel { Margin = new Thickness(In, 11, In, 11) };
        var inks = new List<TacSketch>();

        // the head on the class lane: art, name, CP, YOU ... class
        var head = new Grid { Height = 32, Margin = new Thickness(-4, 0, -4, 9) };
        head.Children.Add(new TacSketch((dc, s) =>
        {
            var shape = Mod.Pill(new Rect(0, 0, s.Width, s.Height), 3);
            dc.DrawGeometry(Mod.Br(lane, .94), null, shape);
            dc.DrawRectangle(Frozen(Mod.Mix(tone, Colors.White, .45)), null, new Rect(0, 3, 2, s.Height - 6));
        }));
        var hg = new Grid { Margin = new Thickness(8, 0, 8, 0) };
        hg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); hg.ColumnDefinitions.Add(new ColumnDefinition()); hg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var icon = ThemeText.Icon(c.Class, 20, c.Name);
        if (icon is Border ib) { ib.BorderBrush = Frozen(Mod.A(Colors.White, .7)); ib.BorderThickness = new Thickness(1); }
        icon.Margin = new Thickness(0, 0, 8, 0);
        hg.Children.Add(icon);
        var who = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var name = Tt(c.Name, 15, White, FontWeights.Bold); name.TextTrimming = TextTrimming.CharacterEllipsis;
        who.Children.Add(name);
        double extra = 0;
        if (c.Cp.Length > 0)
        {
            var cp = new TextBlock { FontFamily = Mod.Face, FontSize = 10, Foreground = Frozen(Mod.A(Colors.White, .72)), Margin = new Thickness(6, 3, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            cp.Inlines.Add(new System.Windows.Documents.Run(L.T("CP") + " ") { FontWeight = FontWeights.Medium });
            cp.Inlines.Add(new System.Windows.Documents.Run(c.Cp) { FontWeight = FontWeights.Bold, Foreground = White });
            who.Children.Add(cp); extra += Mod.W(cp);
        }
        if (c.You) { var y = Chip(L.T("YOU"), PinkC, light: true); y.Margin = new Thickness(6, 0, 0, 0); who.Children.Add(y); extra += Mod.W(y); }
        Grid.SetColumn(who, 1); hg.Children.Add(who);
        var cls = Tt(c.ClassName, 10, Soft, FontWeights.SemiBold); cls.Margin = new Thickness(8, 1, 0, 0);
        Grid.SetColumn(cls, 2); hg.Children.Add(cls);
        hg.Effect = Shade();
        head.Children.Add(hg);
        // the card widens for a long name (up to 60 more) before the name is cut
        double need = 20 + 8 + Mod.W(name) + extra + Mod.W(cls) + 8 + 2 * In + 4;
        w = Math.Max(w, Math.Min(w + 60, need));
        double inner = w - 2 * In;
        stack.Width = inner;
        name.MaxWidth = Math.Max(60, inner + 8 - 8 - 8 - 28 - extra - Mod.W(cls) - 8);
        stack.Children.Add(head);

        // the figures: DPS big, share and damage, each under its small word; the DPS underlined by the pen in yellow
        var read = new Grid();
        read.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
        read.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        read.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        TextBlock Part(int col, string word, string fig, double size)
        {
            var sp = new Grid { Height = 38 };
            var lb = Tt(word, 8.5, Quiet, FontWeights.Bold); lb.VerticalAlignment = VerticalAlignment.Top;
            sp.Children.Add(lb);
            var fv = Tt(fig, size, White, FontWeights.Bold); fv.VerticalAlignment = VerticalAlignment.Bottom; fv.HorizontalAlignment = HorizontalAlignment.Left; fv.Margin = new Thickness(0, 0, 0, -size * .18 + 1);
            sp.Children.Add(fv);
            Grid.SetColumn(sp, col); read.Children.Add(sp);
            return fv;
        }
        var dps = Part(0, L.T("DPS"), c.Dps.Length > 0 ? c.Dps : "–", 21);
        Part(1, L.T("SHARE"), c.Share, 15);
        Part(2, L.T("DAMAGE"), c.Damage, 15);
        var dpsInk = new TacSketch((dc, s) =>
        {
            if (pen <= 0 || dps.ActualWidth <= 0) return;
            var p = dps.TranslatePoint(new Point(0, 0), read);
            double y = Math.Min(s.Height + 3, p.Y + dps.ActualHeight - 1);
            Ink(dc, Poly(LinePts(new Point(p.X - 1, y + .4), new Point(p.X + dps.ActualWidth + 3, y - 1), -.8, 97, .35), pen), PenC, 1.9);
        });
        Grid.SetColumnSpan(dpsInk, 3);
        read.Children.Add(dpsInk); inks.Add(dpsInk);
        stack.Children.Add(read);

        // the rates: "crit 34%   from crits 41%   from behind 12%" (the whole sentence on hover)
        var rates = Tac.Rates(c);
        if (rates.Count > 0)
        {
            var rl = new TextBlock { FontFamily = Mod.Face, FontSize = 10, Margin = new Thickness(0, 9, 0, 0), ToolTip = c.RatesLine, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = Quiet };
            System.Windows.Documents.Typography.SetNumeralAlignment(rl, FontNumeralAlignment.Tabular);
            foreach (var (word, fig) in rates)
            {
                if (rl.Inlines.Count > 0) rl.Inlines.Add(new System.Windows.Documents.Run("   "));
                rl.Inlines.Add(new System.Windows.Documents.Run(word + " ") { FontWeight = FontWeights.Medium });
                rl.Inlines.Add(new System.Windows.Documents.Run(fig) { FontWeight = FontWeights.Bold, Foreground = White, FontSize = 11.5 });
            }
            stack.Children.Add(rl);
        }

        // the abilities: each on its own flat class lane, as long as its part of the top one
        var bars = new List<TacSketch>();
        if (c.Abilities.Count > 0)
        {
            stack.Children.Add(Chalk(new Thickness(0, 8, 0, 5)));
            var hd = Tac.CardGrid(c, 46, 56, 14);
            TextBlock H(string s, bool right = true) { var t = Tt(s, 8, Quiet, FontWeights.Bold); if (right) t.HorizontalAlignment = HorizontalAlignment.Right; return t; }
            Tac.Cell(hd, H(L.T("SKILL"), false), 0, c);
            Tac.Cell(hd, H(L.T("SHARE")), 1, c);
            Tac.Cell(hd, H(L.T("DMG")), 2, c);
            if (c.CritCol) Tac.Cell(hd, H(L.T("CRIT")), 3, c);
            if (c.BackCol) Tac.Cell(hd, H(L.T("BACK")), 4, c);
            hd.Margin = new Thickness(0, 0, 0, 2);
            stack.Children.Add(hd);
            foreach (var a in c.Abilities)
            {
                var row = new Grid { Height = 21, Margin = new Thickness(-4, 1, 0, 0), ToolTip = Tac.AbilityTip(a) };
                double of = a.OfTop;
                var art = new TacSketch((dc, s) =>
                {
                    dc.DrawRoundedRectangle(Mod.Br(Colors.Black, .26), null, new Rect(0, 1, s.Width, s.Height - 2), 2.5, 2.5);
                    double x = Math.Round(s.Width * Math.Clamp(of * grow, 0, 1));
                    if (x < 1) return;
                    var shape = Mod.Pill(new Rect(0, 1, x, s.Height - 2), 2.5);
                    dc.DrawGeometry(Mod.Br(lane, .8), null, shape);
                });
                bars.Add(art);
                row.Children.Add(art);
                var g = Tac.CardGrid(c, 46, 56);
                g.Margin = new Thickness(4, 0, 0, 0);
                var nm = Tac.AbilityName(a, 11.5, White, Mod.Face, FontWeights.SemiBold, Frozen(Mod.Mix(tone, Colors.White, .55)), 9.5);
                nm.Margin = new Thickness(2, 0, 6, 0);
                Tac.Cell(g, nm, 0, c);
                var sh = Tt(a.ShareText, 10.5, Soft, FontWeights.SemiBold); sh.HorizontalAlignment = HorizontalAlignment.Right; Tac.Cell(g, sh, 1, c);
                var dm = Tt(a.Damage, 11.5, White, FontWeights.Bold); dm.HorizontalAlignment = HorizontalAlignment.Right; Tac.Cell(g, dm, 2, c);
                if (c.CritCol) { var cr = Tt(a.Crit, 10.5, Soft, FontWeights.SemiBold); cr.HorizontalAlignment = HorizontalAlignment.Right; Tac.Cell(g, cr, 3, c); }
                if (c.BackCol) { var bk = Tt(a.Back, 10.5, Soft, FontWeights.SemiBold); bk.HorizontalAlignment = HorizontalAlignment.Right; Tac.Cell(g, bk, 4, c); }
                row.Children.Add(g);
                stack.Children.Add(row);
            }
            if (c.MoreLine.Length > 0) { var ml = Tt(c.MoreLine, 10, Quiet, FontWeights.Medium); ml.Margin = new Thickness(0, 4, 0, 0); stack.Children.Add(ml); }
        }
        if (c.FootLine.Length > 0)
        {
            stack.Children.Add(Chalk(new Thickness(0, 7, 0, 5)));
            var fl = Tt(c.FootLine, 10, Soft, FontWeights.Medium); fl.TextTrimming = TextTrimming.CharacterEllipsis; fl.ToolTip = c.FootLine;
            stack.Children.Add(fl);
        }

        // the board under it all (its drop inside the card's own room)
        var board = new Grid { Margin = new Thickness(4, 4, 10, 12) };
        board.Children.Add(new TacSketch((dc, s) => Board(dc, s, 6, 1)));
        board.Children.Add(stack);
        var card = new Grid { Children = { board } };
        TextOptions.SetTextFormattingMode(card, TextFormattingMode.Display);
        System.Windows.Documents.TextElement.SetFontFamily(card, Mod.Face);
        var shift = new TranslateTransform();
        card.RenderTransform = shift;
        Tac.CardIn(card, 260, t =>
        {
            card.Opacity = Out(Seg(t, 0, .7)); shift.X = -6 * (1 - Out(t));
            grow = Out(Seg(t, .15, .85));
            pen = Out(Seg(t, .55, 1));
            foreach (var b in bars) b.InvalidateVisual();
            foreach (var i in inks) i.InvalidateVisual();
        });
        return card;
    }
}
