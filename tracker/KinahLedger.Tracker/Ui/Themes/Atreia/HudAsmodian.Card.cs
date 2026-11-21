using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

sealed partial class HudAsmodian : IThemeCard
{
    /// <summary>
    /// A member's skills card as a black-iron plate (its crimson edge, its thorns): the member's head on a bar forged in their
    /// class's colour, as their meter row has it (the art in its ring, the name in the heavy face, CP, YOU, the class small at
    /// the right); DPS big, share and damage with small words over them; the rates; each ability on its own forged bar, its
    /// length its part of the top ability, its end burning; the rest and the foot after crimson rules.
    /// Entrance: it slides in from the left as its edge flares and cools and the bars forge out (240 ms, transforms and opacity only).
    /// </summary>
    public FrameworkElement DrawCard(MemberCard c)
    {
        const double In = 11;
        double w = 270 + (c.CritCol ? Tac.CardRate : 0) + (c.BackCol ? Tac.CardRate : 0);
        var tone = c.ClassColor;
        var stack = new StackPanel { Margin = new Thickness(G + In, G + 9, G + In, G + 9) };

        // the head on a forged bar: art, name, CP, YOU ... class
        var head = new Grid { Height = 30, Margin = new Thickness(-5, 0, -5, 7) };
        head.Children.Add(new TacSketch((dc, s) =>
        {
            var r = new Rect(.5, .5, s.Width - 1, s.Height - 1);
            var slab = Plate(r, 6, 0);
            dc.DrawGeometry(Mod.Br(Color.FromRgb(0x1A, 0x0D, 0x10), .95), null, slab);
            dc.PushClip(slab);
            Forged(dc, r, r.Width, tone, .8);
            dc.Pop();
            dc.DrawGeometry(null, Mod.P(Mod.Br(c.You ? HotC : RimC), c.You ? 1.2 : 1), slab);
        }));
        var hg = new Grid { Margin = new Thickness(6, 0, 9, 0) };
        hg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); hg.ColumnDefinitions.Add(new ColumnDefinition()); hg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var icon = ThemeText.Icon(c.Class, 20, c.Name);
        if (icon is Border ib) { ib.BorderBrush = Frozen(Mod.Mix(tone, Colors.White, .25)); ib.BorderThickness = new Thickness(1.2); }
        icon.Margin = new Thickness(0, 0, 8, 0);
        hg.Children.Add(icon);
        var who = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var name = Head(c.Name, 17, White, FontWeights.Bold); name.TextTrimming = TextTrimming.CharacterEllipsis;
        who.Children.Add(name);
        double extra = 0;
        if (c.Cp.Length > 0)
        {
            var cp = new TextBlock { FontFamily = Mod.Face, FontSize = 10, Foreground = Quiet, Margin = new Thickness(7, 2, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            cp.Inlines.Add(new System.Windows.Documents.Run(L.T("CP") + " ") { FontWeight = FontWeights.Medium });
            cp.Inlines.Add(new System.Windows.Documents.Run(c.Cp) { FontWeight = FontWeights.Bold, Foreground = White });
            who.Children.Add(cp); extra += Mod.W(cp);
        }
        if (c.You) { var y = Chip(L.T("YOU"), White, CrimC, filled: true); y.Margin = new Thickness(7, 0, 0, 0); who.Children.Add(y); extra += Mod.W(y); }
        Grid.SetColumn(who, 1); hg.Children.Add(who);
        var cls = Head(c.ClassName, 12, Soft); cls.Margin = new Thickness(8, 1, 0, 0);
        Grid.SetColumn(cls, 2); hg.Children.Add(cls);
        head.Children.Add(hg);
        // the card widens for a long name (up to 60 more) before the name is cut
        double need = 20 + 8 + Mod.W(name) + extra + Mod.W(cls) + 8 + 2 * (G + In) + 6;
        w = Math.Max(w, Math.Min(w + 60, need));
        double inner = w - 2 * (G + In);
        stack.Width = inner;
        name.MaxWidth = Math.Max(60, inner + 10 - 6 - 9 - 28 - extra - Mod.W(cls) - 8);
        stack.Children.Add(head);

        // the figures: DPS big, share and damage, each under its small word, iron rules between them
        var read = new Grid();
        read.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
        read.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        read.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        void Part(int col, string word, string fig, double size, bool hero)
        {
            var sp = new Grid { Height = 38 };
            var lb = Tt(word, 9, Frozen(Mod.Mix(CrimC, BoneC, .72)), FontWeights.Bold); lb.VerticalAlignment = VerticalAlignment.Top;
            sp.Children.Add(lb);
            var fv = Big(fig, size, White, hero ? FontWeights.Bold : FontWeights.SemiBold); fv.VerticalAlignment = VerticalAlignment.Bottom; fv.Margin = new Thickness(0, 0, 0, -size * .16 + 1);
            if (hero) sp.Children.Add(new Border { Child = fv, Effect = Underglow(.7), VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Left });
            else sp.Children.Add(fv);
            if (col > 0) sp.Children.Add(new Border { Width = 1, Margin = new Thickness(-9, 4, 0, 2), HorizontalAlignment = HorizontalAlignment.Left, Background = Mod.Grad(true, (Mod.A(CrimC, 0), 0), (Mod.A(CrimC, .7), 1)) });
            Grid.SetColumn(sp, col); read.Children.Add(sp);
        }
        Part(0, L.T("DPS"), c.Dps.Length > 0 ? c.Dps : "–", 23, true);
        Part(1, L.T("SHARE"), c.Share, 16, false);
        Part(2, L.T("DAMAGE"), c.Damage, 16, false);
        stack.Children.Add(read);

        // the rates: "crit 34%   from crits 41%   from behind 12%" (the whole sentence on hover)
        var rates = Tac.Rates(c);
        if (rates.Count > 0)
        {
            var rl = new TextBlock { FontFamily = Mod.Face, FontSize = 10, Margin = new Thickness(0, 7, 0, 0), ToolTip = c.RatesLine, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = Quiet };
            System.Windows.Documents.Typography.SetNumeralAlignment(rl, FontNumeralAlignment.Tabular);
            foreach (var (word, fig) in rates)
            {
                if (rl.Inlines.Count > 0) rl.Inlines.Add(new System.Windows.Documents.Run("   "));
                rl.Inlines.Add(new System.Windows.Documents.Run(word + " ") { FontWeight = FontWeights.Medium });
                rl.Inlines.Add(new System.Windows.Documents.Run(fig) { FontWeight = FontWeights.Bold, Foreground = White, FontSize = 11.5 });
            }
            stack.Children.Add(rl);
        }

        // the abilities: each on its own forged bar, as long as its part of the top one
        var bars = new List<TacSketch>();
        double figW = 46 + 56 + (c.CritCol ? Tac.CardRate : 0) + (c.BackCol ? Tac.CardRate : 0);
        if (c.Abilities.Count > 0)
        {
            stack.Children.Add(Rule(new Thickness(0, 5, 0, 3)));
            var hd = Tac.CardGrid(c, 46, 56, 14);
            TextBlock H(string s, bool right = true) { var t = Tt(s, 8.5, Frozen(Mod.Mix(CrimC, BoneC, .72)), FontWeights.Bold); if (right) t.HorizontalAlignment = HorizontalAlignment.Right; return t; }
            Tac.Cell(hd, H(L.T("SKILL"), false), 0, c);
            Tac.Cell(hd, H(L.T("SHARE")), 1, c);
            Tac.Cell(hd, H(L.T("DMG")), 2, c);
            if (c.CritCol) Tac.Cell(hd, H(L.T("CRIT")), 3, c);
            if (c.BackCol) Tac.Cell(hd, H(L.T("BACK")), 4, c);
            hd.Margin = new Thickness(0, 0, 0, 2);
            stack.Children.Add(hd);
            foreach (var a in c.Abilities)
            {
                var row = new Grid { Height = 20, Margin = new Thickness(-4, 1, 0, 0), ToolTip = Tac.AbilityTip(a) };
                double of = a.OfTop;
                var art = new TacSketch((dc, s) =>
                {
                    var r = new Rect(0, 1.5, s.Width, s.Height - 3);
                    dc.DrawRectangle(Mod.Br(Color.FromRgb(0x1C, 0x0D, 0x10), .8), null, r);
                    double fx = r.Width * Math.Clamp(of, 0, 1);
                    Forged(dc, r, fx, tone, .55, r.Width - figW - 6, .75);
                });
                // the bar forges out on the card's entrance by its transform (drawn once)
                art.RenderTransformOrigin = new Point(0, .5); art.RenderTransform = new ScaleTransform(1, 1);
                bars.Add(art);
                row.Children.Add(art);
                var g = Tac.CardGrid(c, 46, 56);
                g.Margin = new Thickness(5, 0, 0, 0);
                var nm = Tac.AbilityName(a, 11.5, White, Mod.Face, FontWeights.SemiBold, Frozen(Mod.Mix(tone, Colors.White, .55)), 9.5);
                nm.Margin = new Thickness(1, 0, 6, 0);
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
            stack.Children.Add(Rule(new Thickness(0, 5, 0, 3), false));
            var fl = Tt(c.FootLine, 10, Soft, FontWeights.Medium); fl.TextTrimming = TextTrimming.CharacterEllipsis; fl.ToolTip = c.FootLine;
            stack.Children.Add(fl);
        }

        // the plate behind it all
        var plate = new TacSketch((dc, s) => Iron(dc, s, 1, big: 12));
        var heat = HeatLayer(12);
        var card = new Grid { Margin = new Thickness(2, 2, 6, 6) };
        card.Children.Add(plate); card.Children.Add(heat);
        card.Children.Add(stack);
        TextOptions.SetTextFormattingMode(card, TextFormattingMode.Display);
        System.Windows.Documents.TextElement.SetFontFamily(card, Mod.Face);
        var shift = new TranslateTransform();
        card.RenderTransform = shift;
        Tac.CardIn(card, 240, t =>
        {
            card.Opacity = Out(t); shift.X = -6 * (1 - Out(t));
            // the entrance moves transforms and opacity only: the bars forge out, the edge's heat cools
            double grow = Math.Max(.01, Out(Seg(t, .2, 1)));
            foreach (var art in bars) ((ScaleTransform)art.RenderTransform).ScaleX = grow;
            Heat(heat, 1 - Out(t));
        });
        return card;
    }
}
