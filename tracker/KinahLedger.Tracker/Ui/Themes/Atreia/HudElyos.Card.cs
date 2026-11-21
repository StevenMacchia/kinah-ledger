using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

sealed partial class HudElyos : IThemeCard
{
    /// <summary>
    /// A member's skills card in Fade's manner: a dark smoked ground that fades lighter to the right (rounded, a faint hairline
    /// round it, a soft shadow), the member's head on their class wash as their meter row has it (the art in its white ring,
    /// the name, CP, YOU, the class small at the right); DPS big, share and damage with small words over them; the rates; each
    /// ability on its own class wash, its length its part of the top ability; the rest and the foot after faint hairlines.
    /// Entrance: it fades in sliding a few pixels from the left (220 ms).
    /// </summary>
    public FrameworkElement DrawCard(MemberCard c)
    {
        const double In = 12;
        double w = 270 + (c.CritCol ? Tac.CardRate : 0) + (c.BackCol ? Tac.CardRate : 0);
        var tone = c.ClassColor;
        var stack = new StackPanel { Margin = new Thickness(In, 10, In, 10) };

        // the head on the class wash: art, name, CP, YOU ... class
        var head = new Grid { Height = 32, Margin = new Thickness(-6, 0, -6, 8) };
        head.Children.Add(new TacSketch((dc, s) => Wash(dc, new Rect(0, 0, s.Width, s.Height), tone, .62, .34, 7)));
        var hg = new Grid { Margin = new Thickness(7, 0, 8, 0) };
        hg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); hg.ColumnDefinitions.Add(new ColumnDefinition()); hg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var icon = ThemeText.Icon(c.Class, 20, c.Name);
        if (icon is Border ib) { ib.BorderBrush = Frozen(Mod.A(Colors.White, .75)); ib.BorderThickness = new Thickness(1); }
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
        if (c.You) { var y = Mark(L.T("YOU"), filled: true); y.Margin = new Thickness(6, 0, 0, 0); who.Children.Add(y); extra += Mod.W(y); }
        Grid.SetColumn(who, 1); hg.Children.Add(who);
        var cls = Tt(c.ClassName, 10, Soft, FontWeights.SemiBold); cls.Margin = new Thickness(8, 1, 0, 0);
        Grid.SetColumn(cls, 2); hg.Children.Add(cls);
        head.Children.Add(Lit(hg));
        // the card widens for a long name (up to 60 more) before the name is cut
        double need = 20 + 8 + Mod.W(name) + extra + Mod.W(cls) + 8 + 2 * In + 4;
        w = Math.Max(w, Math.Min(w + 60, need));
        double inner = w - 2 * In;
        stack.Width = inner;
        name.MaxWidth = Math.Max(60, inner + 12 - 7 - 8 - 28 - extra - Mod.W(cls) - 8);
        stack.Children.Add(head);

        // the figures: DPS big, share and damage, each under its small word
        var read = new Grid();
        read.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
        read.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        read.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        void Part(int col, string word, string fig, double size)
        {
            var sp = new Grid { Height = 38 };
            var lb = Tt(word, 8.5, Quiet, FontWeights.Bold); lb.VerticalAlignment = VerticalAlignment.Top;
            sp.Children.Add(lb);
            var fv = Tt(fig, size, White, FontWeights.Bold); fv.VerticalAlignment = VerticalAlignment.Bottom; fv.Margin = new Thickness(0, 0, 0, -size * .18 + 1);
            sp.Children.Add(fv);
            Grid.SetColumn(sp, col); read.Children.Add(sp);
        }
        Part(0, L.T("DPS"), c.Dps.Length > 0 ? c.Dps : "–", 21);
        Part(1, L.T("SHARE"), c.Share, 15);
        Part(2, L.T("DAMAGE"), c.Damage, 15);
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

        // the abilities: each on its own class wash, as long as its part of the top one
        var bars = new List<(TacSketch Art, double Of)>();
        double grow = 1;
        if (c.Abilities.Count > 0)
        {
            stack.Children.Add(Hairline(new Thickness(0, 8, 0, 5), .18));
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
                var art = new TacSketch((dc, s) => Wash(dc, new Rect(0, 1, s.Width * Math.Clamp(of * grow, 0, 1), s.Height - 2), tone, .5, .3, 5));
                bars.Add((art, of));
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
                row.Children.Add(Lit(g));
                stack.Children.Add(row);
            }
            if (c.MoreLine.Length > 0) { var ml = Tt(c.MoreLine, 10, Quiet, FontWeights.Medium); ml.Margin = new Thickness(0, 4, 0, 0); stack.Children.Add(ml); }
        }
        if (c.FootLine.Length > 0)
        {
            stack.Children.Add(Hairline(new Thickness(0, 7, 0, 5), .18));
            var fl = Tt(c.FootLine, 10, Soft, FontWeights.Medium); fl.TextTrimming = TextTrimming.CharacterEllipsis; fl.ToolTip = c.FootLine;
            stack.Children.Add(fl);
        }

        // the ground: smoked glass, darker at the left where the words start
        var ground = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        ground.GradientStops.Add(new GradientStop(Mod.A(DeepC, .92), 0));
        ground.GradientStops.Add(new GradientStop(Mod.A(DeepC, .8), 1));
        ground.Freeze();
        var card = new Border
        {
            Child = stack, Background = ground, CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), BorderBrush = Frozen(Mod.A(Colors.White, .14)),
            Margin = new Thickness(4, 4, 10, 14), Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 16, ShadowDepth = 3, Direction = 270, Opacity = .45 },
        };
        TextOptions.SetTextFormattingMode(card, TextFormattingMode.Display);
        System.Windows.Documents.TextElement.SetFontFamily(card, Mod.Face);
        var shift = new TranslateTransform();
        card.RenderTransform = shift;
        Tac.CardIn(card, 220, t =>
        {
            card.Opacity = Out(t); shift.X = -6 * (1 - Out(t));
            grow = Out(Seg(t, .2, 1));
            foreach (var (art, _) in bars) art.InvalidateVisual();
        });
        return card;
    }
}
