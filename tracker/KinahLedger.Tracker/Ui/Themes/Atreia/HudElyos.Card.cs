using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

sealed partial class HudElyos : IThemeCard
{
    /// <summary>
    /// A member's skills card on the Elyos plate (gold-leaf frame, cut corners with their diamonds, the crest): the member's
    /// head on their class's colour lit warm from above with a gold edge along its foot (the art in its gold ring, the name,
    /// CP, YOU, the class in the serif's italic at the right); DPS big, share and damage under small gold capitals; the rates;
    /// each ability on its own class bar with a gold top edge, its length its part of the top ability; the rest and the foot
    /// after gold rules. Entrance: it fades in rising a few pixels, the bars growing out from the left (240 ms).
    /// </summary>
    public FrameworkElement DrawCard(MemberCard c)
    {
        const double In = 13;
        double w = 272 + (c.CritCol ? Tac.CardRate : 0) + (c.BackCol ? Tac.CardRate : 0);
        var tone = c.ClassColor;
        // the plate sits 3-5 px inside the card's box, its drop in that room (nothing drawn past the card)
        var stack = new StackPanel { Margin = new Thickness(In + 3, 15, In + 5, 19) };

        // the head on the class's colour: art, name, CP, YOU ... class
        var head = new Grid { Height = 32, Margin = new Thickness(-5, 0, -5, 9) };
        head.Children.Add(new TacSketch((dc, s) =>
        {
            var r = new Rect(0, 0, s.Width, s.Height);
            dc.DrawRectangle(Mod.Grad(false, (Mod.A(Mod.Mix(tone, WarmC, .2), .78), 0), (Mod.A(tone, .45), .6), (Mod.A(tone, .12), 1)), null, r);
            dc.DrawRectangle(Mod.Grad(true, (Mod.A(WarmC, .18), 0), (Mod.A(WarmC, 0), .55)), null, r);
            dc.DrawRectangle(Lin(new Point(0, 0), new Point(1, 0), (Mod.A(AbyssC, .4), 0), (Mod.A(AbyssC, 0), 1)), null, new Rect(0, 0, Math.Min(s.Width, 170), s.Height));
            dc.DrawRectangle(Lin(new Point(0, 0), new Point(1, 0), (GoldHiC, 0), (GoldMidC, .6), (Mod.A(GoldLoC, 0), 1)), null, new Rect(0, s.Height - 1.2, s.Width, 1.2));
        }));
        var hg = new Grid { Margin = new Thickness(7, 0, 8, 0), Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Colors.Black, Direction = 270, ShadowDepth = 1, BlurRadius = 3, Opacity = .9 } };
        hg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); hg.ColumnDefinitions.Add(new ColumnDefinition()); hg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var icon = ThemeText.Icon(c.Class, 21, c.Name);
        if (icon is Border ib) { ib.BorderBrush = Leaf; ib.BorderThickness = new Thickness(1.2); }
        icon.Margin = new Thickness(0, 0, 8, 0);
        hg.Children.Add(icon);
        var who = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var name = Tt(c.Name, 15, Ivory, FontWeights.Bold); name.TextTrimming = TextTrimming.CharacterEllipsis;
        who.Children.Add(name);
        double extra = 0;
        if (c.Cp.Length > 0)
        {
            var cp = new TextBlock { FontFamily = Mod.Face, FontSize = 10, Foreground = Frozen(Mod.A(IvoryC, .74)), Margin = new Thickness(7, 3, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            cp.Inlines.Add(new System.Windows.Documents.Run(L.T("CP") + " ") { FontWeight = FontWeights.Medium });
            cp.Inlines.Add(new System.Windows.Documents.Run(c.Cp) { FontWeight = FontWeights.SemiBold, Foreground = Ivory, FontFamily = Fig, FontSize = 11 });
            who.Children.Add(cp); extra += Mod.W(cp);
        }
        if (c.You) { var y = Mark(L.T("YOU"), filled: true); y.Margin = new Thickness(7, 0, 0, 0); who.Children.Add(y); extra += Mod.W(y) + 7; }
        Grid.SetColumn(who, 1); hg.Children.Add(who);
        var cls = Tt(c.ClassName, 11, GoldInk, FontWeights.SemiBold, Serif); cls.FontStyle = FontStyles.Italic; cls.Margin = new Thickness(8, 1, 0, 0);
        Grid.SetColumn(cls, 2); hg.Children.Add(cls);
        head.Children.Add(hg);
        // the card widens for a long name (up to 60 more) before the name is cut
        double need = 21 + 8 + Mod.W(name) + extra + Mod.W(cls) + 8 + 2 * In + 4;
        w = Math.Max(w, Math.Min(w + 60, need));
        double inner = w - 2 * In;
        stack.Width = inner;
        name.MaxWidth = Math.Max(60, inner + 10 - 7 - 8 - 29 - extra - Mod.W(cls) - 8);
        stack.Children.Add(head);

        // the figures: DPS big, share and damage, each under its small gold capitals
        var read = new Grid();
        read.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
        read.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        read.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        void Part(int col, string word, string fig, double size)
        {
            var sp = new Grid { Height = 39 };
            var lb = Caps(word, 10); lb.VerticalAlignment = VerticalAlignment.Top;
            sp.Children.Add(lb);
            var fv = F(fig, size, Ivory); fv.VerticalAlignment = VerticalAlignment.Bottom; fv.Margin = new Thickness(0, 0, 0, -size * .16 + 1);
            sp.Children.Add(fv);
            Grid.SetColumn(sp, col); read.Children.Add(sp);
        }
        Part(0, L.T("DPS"), c.Dps.Length > 0 ? c.Dps : "–", 23);
        Part(1, L.T("SHARE"), c.Share, 16);
        Part(2, L.T("DAMAGE"), c.Damage, 16);
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
                rl.Inlines.Add(new System.Windows.Documents.Run(fig) { FontWeight = FontWeights.SemiBold, Foreground = Ivory, FontFamily = Fig, FontSize = 12 });
            }
            stack.Children.Add(rl);
        }

        // the abilities: each on its own class bar with a gold top edge, as long as its part of the top one
        // the bars grow in on the entrance by one shared scale (no bar redrawn)
        var grow = new ScaleTransform(1, 1);
        var list = new StackPanel { Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Colors.Black, Direction = 270, ShadowDepth = 1, BlurRadius = 2, Opacity = .85 } };
        if (c.Abilities.Count > 0)
        {
            stack.Children.Add(Rule(new Thickness(0, 6, 0, 4)));
            var hd = Tac.CardGrid(c, 46, 56, 14);
            TextBlock H(string s, bool right = true) { var t = Caps(s, 9.5); if (right) t.HorizontalAlignment = HorizontalAlignment.Right; return t; }
            Tac.Cell(hd, H(L.T("SKILL"), false), 0, c);
            Tac.Cell(hd, H(L.T("SHARE")), 1, c);
            Tac.Cell(hd, H(L.T("DMG")), 2, c);
            if (c.CritCol) Tac.Cell(hd, H(L.T("CRIT")), 3, c);
            if (c.BackCol) Tac.Cell(hd, H(L.T("BACK")), 4, c);
            hd.Margin = new Thickness(0, 0, 0, 3);
            stack.Children.Add(hd);
            foreach (var a in c.Abilities)
            {
                var row = new Grid { Height = 21, Margin = new Thickness(-4, 1, 0, 0), ToolTip = Tac.AbilityTip(a) };
                double of = a.OfTop;
                row.Children.Add(new TacSketch((dc, s) => dc.DrawRectangle(Frozen(Mod.A(AbyssC, .55)), null, new Rect(0, 1, s.Width, s.Height - 2))));
                var art = new TacSketch((dc, s) =>
                {
                    double bw = s.Width * Math.Clamp(of, 0, 1);
                    if (bw < .5) return;
                    var b = new Rect(0, 1, bw, s.Height - 2);
                    dc.DrawRectangle(Mod.Grad(true, (Mod.A(Mod.Mix(tone, WarmC, .25), .62), 0), (Mod.A(tone, .48), .5), (Mod.A(Mod.Mix(tone, AbyssC, .4), .55), 1)), null, b);
                    dc.DrawRectangle(Lin(new Point(0, 0), new Point(1, 0), (Mod.A(AbyssC, .35), 0), (Mod.A(AbyssC, 0), 1)), null, new Rect(0, 1, Math.Min(bw, 120), s.Height - 2));
                    dc.DrawRectangle(Frozen(Mod.A(GoldHiC, .75)), null, new Rect(0, 1, bw, 1));
                }) { RenderTransform = grow, RenderTransformOrigin = new Point(0, .5) };
                row.Children.Add(art);
                var g = Tac.CardGrid(c, 46, 56);
                g.Margin = new Thickness(4, 0, 0, 0);
                var nm = Tac.AbilityName(a, 11.5, Ivory, Mod.Face, FontWeights.SemiBold, GoldInk, 9.5);
                nm.Margin = new Thickness(3, 0, 6, 0);
                Tac.Cell(g, nm, 0, c);
                var sh = F(a.ShareText, 11, Soft, FontWeights.Medium); sh.HorizontalAlignment = HorizontalAlignment.Right; Tac.Cell(g, sh, 1, c);
                var dm = F(a.Damage, 12, Ivory); dm.HorizontalAlignment = HorizontalAlignment.Right; Tac.Cell(g, dm, 2, c);
                if (c.CritCol) { var cr = F(a.Crit, 11, Soft, FontWeights.Medium); cr.HorizontalAlignment = HorizontalAlignment.Right; Tac.Cell(g, cr, 3, c); }
                if (c.BackCol) { var bk = F(a.Back, 11, Soft, FontWeights.Medium); bk.HorizontalAlignment = HorizontalAlignment.Right; Tac.Cell(g, bk, 4, c); }
                row.Children.Add(g);
                list.Children.Add(row);
            }
            stack.Children.Add(list);
            if (c.MoreLine.Length > 0) { var ml = Tt(c.MoreLine, 10, Quiet, FontWeights.Medium); ml.Margin = new Thickness(0, 4, 0, 0); stack.Children.Add(ml); }
        }
        if (c.FootLine.Length > 0)
        {
            stack.Children.Add(Rule(new Thickness(0, 6, 0, 3)));
            var fl = Tt(c.FootLine, 10, Soft, FontWeights.Medium); fl.TextTrimming = TextTrimming.CharacterEllipsis; fl.ToolTip = c.FootLine;
            stack.Children.Add(fl);
        }

        // the plate under it all
        var card = new Grid { Margin = new Thickness(2, 4, 6, 4) };
        card.Children.Add(new TacSketch((dc, s) => PlateArt(dc, s, 1, crest: true, cut: 7)) { Margin = new Thickness(3, 4, 5, 8) });
        card.Children.Add(stack);
        TextOptions.SetTextFormattingMode(card, TextFormattingMode.Display);
        System.Windows.Documents.TextElement.SetFontFamily(card, Mod.Face);
        var shift = new TranslateTransform();
        card.RenderTransform = shift;
        Tac.CardIn(card, 240, t =>
        {
            card.Opacity = Out(t); shift.Y = 5 * (1 - Out(t));
            grow.ScaleX = Math.Max(.01, Out(Seg(t, .2, 1)));
        });
        return card;
    }
}
