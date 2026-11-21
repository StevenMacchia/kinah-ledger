using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

sealed partial class HudMagma : IThemeCard
{
    /// <summary>
    /// A member's skills card in Magma's manner: a basalt slab with its lava cracks; the member's head in a stone channel with
    /// the class's stripe (the art in its ring of the class's colour, the name, CP, YOU white-hot, the class small at the
    /// right); DPS big in white-hot over its underglow, share and damage with small words over them; the rates; each ability
    /// as its own small molten flow, its length its part of the top ability, the top one hottest; the rest and the foot after
    /// lava seams. Entrance: it rises a few pixels as it fades in and the flows pour out to their lengths (240 ms).
    /// </summary>
    public FrameworkElement DrawCard(MemberCard c)
    {
        const double In = 12;
        double w = 270 + (c.CritCol ? Tac.CardRate : 0) + (c.BackCol ? Tac.CardRate : 0);
        var tone = c.ClassColor;
        var stack = new StackPanel { Margin = new Thickness(In, 10, In, 10) };

        // the head in its channel: the class's stripe, art, name, CP, YOU ... class
        var head = new Grid { Height = 30, Margin = new Thickness(-4, 0, -4, 8) };
        head.Children.Add(new TacSketch((dc, s) =>
        {
            var r = new Rect(.5, .5, s.Width - 1, s.Height - 1);
            var ch = Mod.Pill(r, 4);
            dc.DrawGeometry(Frozen(Mod.A(TrenchC, .96)), Mod.P(Mod.Grad(true, (RimC, 0), (Mod.A(BevelC, .8), 1)), 1), ch);
            dc.PushClip(ch);
            dc.DrawRectangle(Mod.Grad(false, (Mod.A(tone, .34), 0), (Mod.A(tone, 0), .6)), null, r);
            dc.DrawRectangle(Mod.Grad(true, (Mod.Mix(tone, Colors.White, .25), 0), (Mod.Mix(tone, Colors.Black, .35), 1)), null, new Rect(r.X, r.Y, 3, r.Height));
            dc.Pop();
        }));
        var hg = new Grid { Margin = new Thickness(9, 0, 8, 0) };
        hg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); hg.ColumnDefinitions.Add(new ColumnDefinition()); hg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var icon = ThemeText.Icon(c.Class, 20, c.Name);
        if (icon is Border ib) { ib.BorderBrush = Frozen(Mod.Mix(tone, Colors.White, .2)); ib.BorderThickness = new Thickness(1.3); }
        icon.Margin = new Thickness(0, 0, 8, 0);
        hg.Children.Add(icon);
        var who = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var name = Tt(c.Name, 14.5, White, FontWeights.Bold); name.TextTrimming = TextTrimming.CharacterEllipsis;
        who.Children.Add(name);
        double extra = 0;
        if (c.Cp.Length > 0)
        {
            var cp = new TextBlock { FontFamily = Mod.Face, FontSize = 10, Foreground = Quiet, Margin = new Thickness(6, 3, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            cp.Inlines.Add(new System.Windows.Documents.Run(L.T("CP") + " ") { FontWeight = FontWeights.Medium });
            cp.Inlines.Add(new System.Windows.Documents.Run(c.Cp) { FontWeight = FontWeights.Bold, Foreground = White });
            who.Children.Add(cp); extra += Mod.W(cp);
        }
        if (c.You) { var y = Chip(L.T("YOU"), White, LavaHotC, filled: true); y.Margin = new Thickness(6, 0, 0, 0); who.Children.Add(y); extra += Mod.W(y); }
        Grid.SetColumn(who, 1); hg.Children.Add(who);
        var cls = Tt(c.ClassName, 10, Soft, FontWeights.SemiBold); cls.Margin = new Thickness(8, 1, 0, 0);
        Grid.SetColumn(cls, 2); hg.Children.Add(cls);
        head.Children.Add(hg);
        // the card widens for a long name (up to 60 more) before the name is cut
        double need = 20 + 8 + Mod.W(name) + extra + Mod.W(cls) + 8 + 2 * In + 6;
        w = Math.Max(w, Math.Min(w + 60, need));
        double inner = w - 2 * In;
        stack.Width = inner;
        name.MaxWidth = Math.Max(60, inner + 8 - 9 - 8 - 28 - extra - Mod.W(cls) - 8);
        stack.Children.Add(head);

        // the figures: DPS big in white-hot, share and damage, each under its small word
        var read = new Grid();
        read.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
        read.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        read.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        void Part(int col, string word, string fig, double size, bool glow)
        {
            var sp = new Grid { Height = 38 };
            var lb = Tt(word, 8.5, Frozen(Mod.Mix(LavaHotC, LavaYelC, .5)), FontWeights.Bold); lb.VerticalAlignment = VerticalAlignment.Top;
            sp.Children.Add(lb);
            var fv = Tt(fig, size, glow ? WhiteHot : White, FontWeights.Bold);
            var host = new Border { Child = fv, VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, -size * .18 + 1), Effect = glow ? Underglow(.8) : null };
            sp.Children.Add(host);
            Grid.SetColumn(sp, col); read.Children.Add(sp);
        }
        Part(0, L.T("DPS"), c.Dps.Length > 0 ? c.Dps : "–", 21, true);
        Part(1, L.T("SHARE"), c.Share, 15, false);
        Part(2, L.T("DAMAGE"), c.Damage, 15, false);
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

        // the abilities: each its own small molten flow, as long as its part of the top one
        var bars = new List<TacSketch>();
        double grow = 1;
        if (c.Abilities.Count > 0)
        {
            stack.Children.Add(Seam(new Thickness(0, 6, 0, 3), .8));
            var hd = Tac.CardGrid(c, 46, 56, 14);
            TextBlock H(string s, bool right = true) { var t = Tt(s, 8.5, Frozen(Mod.Mix(LavaHotC, LavaYelC, .5)), FontWeights.Bold); if (right) t.HorizontalAlignment = HorizontalAlignment.Right; return t; }
            Tac.Cell(hd, H(L.T("SKILL"), false), 0, c);
            Tac.Cell(hd, H(L.T("SHARE")), 1, c);
            Tac.Cell(hd, H(L.T("DMG")), 2, c);
            if (c.CritCol) Tac.Cell(hd, H(L.T("CRIT")), 3, c);
            if (c.BackCol) Tac.Cell(hd, H(L.T("BACK")), 4, c);
            hd.Margin = new Thickness(0, 0, 0, 2);
            stack.Children.Add(hd);
            int n = 0;
            foreach (var a in c.Abilities)
            {
                var row = new Grid { Height = 22, Margin = new Thickness(-4, 0, 0, 0), ToolTip = Tac.AbilityTip(a) };
                double of = a.OfTop;
                int seed = 300 + n;
                bool topOne = n++ == 0;
                var art = new TacSketch((dc, s) =>
                {
                    // a slim molten flow in a stone groove along the row's foot, the words on the stone over it
                    var r = new Rect(.5, s.Height - 6.5, s.Width - 1, 5);
                    dc.DrawRoundedRectangle(Frozen(Mod.A(TrenchC, .95)), Mod.P(Mod.Grad(true, (RimC, 0), (Mod.A(BevelC, .7), 1)), .7), r, 2.5, 2.5);
                    Flow(dc, new Rect(r.X + 1, r.Y + 1, r.Width - 2, r.Height - 2), (r.Width - 2) * Math.Clamp(of * grow, 0, 1), topOne ? 1 : .25 + .5 * of, seed, plates: false, radius: 1.5);
                });
                bars.Add(art);
                row.Children.Add(art);
                var g = Tac.CardGrid(c, 46, 56);
                g.Margin = new Thickness(6, 0, 0, 5);
                var nm = Tac.AbilityName(a, 11.5, White, Mod.Face, FontWeights.SemiBold, Frozen(Color.FromRgb(0xFF, 0xD9, 0xB0)), 9.5);
                nm.Margin = new Thickness(2, 0, 6, 0);
                Tac.Cell(g, nm, 0, c);
                var sh = Tt(a.ShareText, 10.5, Soft, FontWeights.SemiBold); sh.HorizontalAlignment = HorizontalAlignment.Right; Tac.Cell(g, sh, 1, c);
                var dm = Tt(a.Damage, 11.5, White, FontWeights.Bold); dm.HorizontalAlignment = HorizontalAlignment.Right; Tac.Cell(g, dm, 2, c);
                if (c.CritCol) { var cr = Tt(a.Crit, 10.5, Soft, FontWeights.SemiBold); cr.HorizontalAlignment = HorizontalAlignment.Right; Tac.Cell(g, cr, 3, c); }
                if (c.BackCol) { var bk = Tt(a.Back, 10.5, Soft, FontWeights.SemiBold); bk.HorizontalAlignment = HorizontalAlignment.Right; Tac.Cell(g, bk, 4, c); }
                g.Effect = Shade();
                row.Children.Add(g);
                stack.Children.Add(row);
            }
            if (c.MoreLine.Length > 0) { var ml = Tt(c.MoreLine, 10, Soft, FontWeights.Medium); ml.Margin = new Thickness(0, 4, 0, 0); stack.Children.Add(ml); }
        }
        if (c.FootLine.Length > 0)
        {
            stack.Children.Add(Seam(new Thickness(0, 5, 0, 3), .8));
            var fl = Tt(c.FootLine, 10, Soft, FontWeights.Medium); fl.TextTrimming = TextTrimming.CharacterEllipsis; fl.ToolTip = c.FootLine;
            stack.Children.Add(fl);
        }

        // the ground: one basalt slab with its cracks
        var card = new Grid { Margin = new Thickness(4, 4, 10, 14) };
        card.Children.Add(new TacSketch((dc, s) => Slab(dc, s, .97, 41)));
        card.Children.Add(stack);
        TextOptions.SetTextFormattingMode(card, TextFormattingMode.Display);
        System.Windows.Documents.TextElement.SetFontFamily(card, Mod.Face);
        var shift = new TranslateTransform();
        card.RenderTransform = shift;
        Tac.CardIn(card, 240, t =>
        {
            card.Opacity = Out(t); shift.Y = 5 * (1 - Out(t));
            grow = Out(Seg(t, .2, 1));
            foreach (var art in bars) art.InvalidateVisual();
        });
        return card;
    }
}
