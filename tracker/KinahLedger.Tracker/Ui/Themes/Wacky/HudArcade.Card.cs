using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

sealed partial class HudArcade : IThemeCard
{
    /// <summary>
    /// A member's skills card as a player's stats screen: a black CRT screen with scanlines in a double pixel frame in the
    /// class's colour, the member's name in LED-lit white with the class art, CP, YOU and the class small at the right over a
    /// dotted rule; the DPS big in LED yellow, the share and damage beside it under small red labels; the rates; each ability
    /// with an LED strip under it lit to its part of the top ability, its head cell hot; the rest and the foot after dotted
    /// rules. Entrance: the screen switches on as a tube does (a line opening out, 240 ms), the strips filling after.
    /// </summary>
    public FrameworkElement DrawCard(MemberCard c)
    {
        const double In = 13;
        double w = 270 + (c.CritCol ? Tac.CardRate : 0) + (c.BackCol ? Tac.CardRate : 0);
        var tone = c.ClassColor;
        var label = Frozen(Color.FromRgb(0xFF, 0x6A, 0x6A));
        var stack = new StackPanel { Margin = new Thickness(In, 12, In, 11) };

        // the head: art, name, CP, YOU ... class, over a dotted rule in the class's colour
        var hg = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        hg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); hg.ColumnDefinitions.Add(new ColumnDefinition()); hg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var icon = ThemeText.Icon(c.Class, 20, c.Name);
        if (icon is Border ib) { ib.BorderBrush = Frozen(Mod.Mix(tone, Colors.White, .35)); ib.BorderThickness = new Thickness(1); }
        icon.Margin = new Thickness(0, 0, 8, 0);
        hg.Children.Add(icon);
        var who = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var name = Tt(c.Name, 15, White, FontWeights.Bold); name.TextTrimming = TextTrimming.CharacterEllipsis; name.Effect = Shade();
        who.Children.Add(name);
        double extra = 0;
        if (c.Cp.Length > 0)
        {
            var cp = new TextBlock { FontFamily = Face, FontSize = 10, Foreground = Frozen(Mod.A(Colors.White, .7)), Margin = new Thickness(6, 3, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            cp.Inlines.Add(new System.Windows.Documents.Run(L.T("CP") + " ") { FontWeight = FontWeights.Medium });
            cp.Inlines.Add(new System.Windows.Documents.Run(c.Cp) { FontWeight = FontWeights.Bold, Foreground = White });
            who.Children.Add(cp); extra += Mod.W(cp);
        }
        if (c.You) { var y = Mark(L.T("YOU"), filled: true); y.Margin = new Thickness(6, 0, 0, 0); who.Children.Add(y); extra += Mod.W(y); }
        Grid.SetColumn(who, 1); hg.Children.Add(who);
        var cls = Tt(c.ClassName, 10, Frozen(Mod.Mix(tone, Colors.White, .45)), FontWeights.SemiBold); cls.Margin = new Thickness(8, 1, 0, 0);
        Grid.SetColumn(cls, 2); hg.Children.Add(cls);
        // the card widens for a long name (up to 60 more) before the name is cut
        double need = 20 + 8 + Mod.W(name) + extra + Mod.W(cls) + 8 + 2 * In + 4;
        w = Math.Max(w, Math.Min(w + 60, need));
        double inner = w - 2 * In;
        stack.Width = inner;
        name.MaxWidth = Math.Max(60, inner - 28 - extra - Mod.W(cls) - 8);
        stack.Children.Add(hg);
        stack.Children.Add(Dots(new Thickness(0, 0, 0, 7), tone, .7));

        // the figures: DPS big in LED yellow, share and damage, each under its small red label
        var read = new Grid();
        read.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
        read.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        read.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        void Part(int col, string word, string fig, double size, Brush ink, Effect? fx = null)
        {
            var sp = new Grid { Height = 38 };
            var lb = Tt(word, 8.5, label, FontWeights.Bold); lb.VerticalAlignment = VerticalAlignment.Top;
            sp.Children.Add(lb);
            var fv = Tt(fig, size, ink, FontWeights.Bold); fv.VerticalAlignment = VerticalAlignment.Bottom; fv.Margin = new Thickness(0, 0, 0, -size * .18 + 1);
            fv.Effect = fx;
            sp.Children.Add(fv);
            Grid.SetColumn(sp, col); read.Children.Add(sp);
        }
        Part(0, L.T("DPS"), c.Dps.Length > 0 ? c.Dps : "–", 22, Led, Glow(LedC, 9, .5));
        Part(1, L.T("SHARE"), c.Share, 15, White);
        Part(2, L.T("DAMAGE"), c.Damage, 15, White);
        stack.Children.Add(read);

        // the rates: "crit 34%   from crits 41%   from behind 12%" (the whole sentence on hover)
        var rates = Tac.Rates(c);
        if (rates.Count > 0)
        {
            var rl = new TextBlock { FontFamily = Face, FontSize = 10, Margin = new Thickness(0, 8, 0, 0), ToolTip = c.RatesLine, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = Quiet };
            System.Windows.Documents.Typography.SetNumeralAlignment(rl, FontNumeralAlignment.Tabular);
            foreach (var (word, fig) in rates)
            {
                if (rl.Inlines.Count > 0) rl.Inlines.Add(new System.Windows.Documents.Run("   "));
                rl.Inlines.Add(new System.Windows.Documents.Run(word + " ") { FontWeight = FontWeights.Medium });
                rl.Inlines.Add(new System.Windows.Documents.Run(fig) { FontWeight = FontWeights.Bold, Foreground = White, FontSize = 11.5 });
            }
            stack.Children.Add(rl);
        }

        // the abilities: each with an LED strip under it, as long as its part of the top one
        var bars = new List<TacSketch>();
        double grow = 1;
        if (c.Abilities.Count > 0)
        {
            stack.Children.Add(Dots(new Thickness(0, 8, 0, 6), MazeHiC, .5));
            var hd = Tac.CardGrid(c, 46, 56, 14);
            TextBlock H(string s, bool right = true) { var t = Tt(s, 8, label, FontWeights.Bold); if (right) t.HorizontalAlignment = HorizontalAlignment.Right; return t; }
            Tac.Cell(hd, H(L.T("SKILL"), false), 0, c);
            Tac.Cell(hd, H(L.T("SHARE")), 1, c);
            Tac.Cell(hd, H(L.T("DMG")), 2, c);
            if (c.CritCol) Tac.Cell(hd, H(L.T("CRIT")), 3, c);
            if (c.BackCol) Tac.Cell(hd, H(L.T("BACK")), 4, c);
            hd.Margin = new Thickness(0, 0, 0, 2);
            stack.Children.Add(hd);
            foreach (var a in c.Abilities)
            {
                var row = new Grid { Height = 22, Margin = new Thickness(0, 1, 0, 0), ToolTip = Tac.AbilityTip(a) };
                double of = a.OfTop;
                var art = new TacSketch((dc, s) => DrawLed(dc, new Rect(0, s.Height - 3, s.Width, 3), of * grow, tone, 4, 1.5, .08));
                bars.Add(art);
                row.Children.Add(art);
                var g = Tac.CardGrid(c, 46, 56);
                g.Margin = new Thickness(0, 0, 0, 3);
                var nm = Tac.AbilityName(a, 11.5, White, Face, FontWeights.SemiBold, Frozen(Mod.Mix(tone, Colors.White, .55)), 9.5);
                nm.Margin = new Thickness(0, 0, 6, 0);
                Tac.Cell(g, nm, 0, c);
                var sh = Tt(a.ShareText, 10.5, Soft, FontWeights.SemiBold); sh.HorizontalAlignment = HorizontalAlignment.Right; Tac.Cell(g, sh, 1, c);
                var dm = Tt(a.Damage, 11.5, White, FontWeights.Bold); dm.HorizontalAlignment = HorizontalAlignment.Right; Tac.Cell(g, dm, 2, c);
                if (c.CritCol) { var cr = Tt(a.Crit, 10.5, Soft, FontWeights.SemiBold); cr.HorizontalAlignment = HorizontalAlignment.Right; Tac.Cell(g, cr, 3, c); }
                if (c.BackCol) { var bk = Tt(a.Back, 10.5, Soft, FontWeights.SemiBold); bk.HorizontalAlignment = HorizontalAlignment.Right; Tac.Cell(g, bk, 4, c); }
                row.Children.Add(g);
                stack.Children.Add(row);
            }
            if (c.MoreLine.Length > 0) { var ml = Tt(c.MoreLine, 10, Quiet, FontWeights.Medium); ml.Margin = new Thickness(0, 5, 0, 0); stack.Children.Add(ml); }
        }
        if (c.FootLine.Length > 0)
        {
            stack.Children.Add(Dots(new Thickness(0, 7, 0, 6), MazeHiC, .5));
            var fl = Tt(c.FootLine, 10, Soft, FontWeights.Medium); fl.TextTrimming = TextTrimming.CharacterEllipsis; fl.ToolTip = c.FootLine;
            stack.Children.Add(fl);
        }

        // the screen: black with scanlines in a double pixel frame in the class's colour
        double open = 1;
        var ground = Ground;
        var screen = new TacSketch((dc, s) =>
        {
            if (open >= 1) { DrawScreen(dc, new Rect(0, 0, s.Width, s.Height), Mod.Mix(tone, MazeC, .25), Math.Max(.93, ground)); return; }
            double h = Math.Max(2, s.Height * open), y = (s.Height - h) / 2;
            if (h < 14) { dc.DrawRectangle(Frozen(Mod.Mix(tone, Colors.White, .6)), null, new Rect(0, s.Height / 2 - 1, s.Width, 2)); return; }
            DrawScreen(dc, new Rect(0, y, s.Width, h), Mod.Mix(tone, MazeC, .25), Math.Max(.93, ground));
        });
        var body = new Grid();
        body.Children.Add(screen);
        body.Children.Add(stack);
        var card = new Border { Child = body, Margin = new Thickness(4, 4, 10, 14), Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 14, ShadowDepth = 3, Direction = 270, Opacity = .5 } };
        TextOptions.SetTextFormattingMode(card, TextFormattingMode.Display);
        System.Windows.Documents.TextElement.SetFontFamily(card, Face);
        Tac.CardIn(card, 240, t =>
        {
            open = Out(Seg(t, 0, .55));
            screen.InvalidateVisual();
            stack.Opacity = Seg(t, .35, .7);
            grow = Out(Seg(t, .45, 1));
            foreach (var art in bars) art.InvalidateVisual();
        });
        return card;
    }
}
