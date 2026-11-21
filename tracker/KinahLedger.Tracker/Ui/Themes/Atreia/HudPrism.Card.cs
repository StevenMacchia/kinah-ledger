using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

sealed partial class HudPrism : IThemeCard
{
    /// <summary>
    /// A member's skills card cut from the same crystal as the meter: the smoked slab with its bevel and the spectrum glint at
    /// its top-left cut; the member's head on a band of glass tinted in their class's colour (the class notch at its left, the
    /// art in its white ring, the name, CP, YOU, the class small at the right); DPS big, share and damage under small words;
    /// the rates; each ability as a clear glass bar as long as its part of the top one, its edge splitting the light as the
    /// meter's bars do; the rest and the foot after faint hairlines. Entrance: it fades in rising a few pixels while the
    /// ability bars fill (220 ms).
    /// </summary>
    public FrameworkElement DrawCard(MemberCard c)
    {
        const double In = 13;
        double w = 270 + (c.CritCol ? Tac.CardRate : 0) + (c.BackCol ? Tac.CardRate : 0);
        var tone = c.ClassColor;
        var stack = new StackPanel { Margin = new Thickness(In, 14, In, 12) };

        // the head on its class-tinted glass band: notch, art, name, CP, YOU ... class
        var head = new Grid { Height = 32, Margin = new Thickness(-5, 0, -5, 8) };
        head.Children.Add(new TacSketch((dc, s) =>
        {
            var r = new Rect(.5, .5, s.Width - 1, s.Height - 1);
            var g = Octa(r, 4);
            dc.DrawGeometry(Mod.Grad(false, (Mod.A(tone, .34), 0), (Mod.A(tone, .12), .7), (Mod.A(tone, .04), 1)), null, g);
            dc.DrawGeometry(Mod.Grad(true, (Mod.A(Colors.White, .14), 0), (Mod.A(Colors.White, .02), 1)), null, g);
            dc.DrawGeometry(null, Mod.P(Mod.Grad(true, (Mod.A(Colors.White, .4), 0), (Mod.A(Colors.White, .08), 1)), 1), g);
            dc.DrawRectangle(Mod.Grad(true, (Mod.Mix(tone, Colors.White, .2), 0), (tone, 1)), null, new Rect(r.X, r.Y + 4, 2.5, r.Height - 8));
        }));
        var hg = new Grid { Margin = new Thickness(9, 0, 8, 0) };
        hg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); hg.ColumnDefinitions.Add(new ColumnDefinition()); hg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var icon = ThemeText.Icon(c.Class, 20, c.Name);
        if (icon is Border ib) { ib.BorderBrush = Frozen(Mod.A(Colors.White, .8)); ib.BorderThickness = new Thickness(1); }
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
        head.Children.Add(hg);
        // the card widens for a long name (up to 60 more) before the name is cut
        double need = 20 + 8 + Mod.W(name) + extra + Mod.W(cls) + 8 + 2 * In + 6;
        w = Math.Max(w, Math.Min(w + 60, need));
        double inner = w - 2 * In;
        stack.Width = inner;
        name.MaxWidth = Math.Max(60, inner + 10 - 9 - 8 - 28 - extra - Mod.W(cls) - 8);
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

        // the abilities: each a clear glass bar as long as its part of the top one, its edge splitting the light
        var grow = new ScaleTransform(1, 1);   // the bars fill in by one shared scale from the left (drawn once)
        // the bars run under the names and the shares: the top one's edge splits the light in the room left of the damage figures
        double rightCols = 56 + (c.CritCol ? Tac.CardRate : 0) + (c.BackCol ? Tac.CardRate : 0) - 5;
        if (c.Abilities.Count > 0)
        {
            stack.Children.Add(Hairline(new Thickness(0, 8, 0, 5), .2));
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
                var art = new TacSketch((dc, s) => AbilityBar(dc, s, tone, Math.Clamp(of, 0, 1), rightCols)) { RenderTransform = grow };
                row.Children.Add(art);
                var g = Tac.CardGrid(c, 46, 56);
                g.Margin = new Thickness(4, 0, 0, 0);
                var nm = Tac.AbilityName(a, 11.5, White, Mod.Face, FontWeights.SemiBold, Frozen(Mod.Mix(tone, Colors.White, .55)), 9.5);
                nm.Margin = new Thickness(3, 0, 6, 0);
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
            stack.Children.Add(Hairline(new Thickness(0, 7, 0, 5), .2));
            var fl = Tt(c.FootLine, 10, Soft, FontWeights.Medium); fl.TextTrimming = TextTrimming.CharacterEllipsis; fl.ToolTip = c.FootLine;
            stack.Children.Add(fl);
        }

        // the ground: the crystal itself, under a soft drop
        var card = new Grid { Margin = new Thickness(4, 4, 10, 14), Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 16, ShadowDepth = 3, Direction = 270, Opacity = .5 } };
        card.Children.Add(new TacSketch((dc, s) => Crystal(dc, s, 1, -1, Colors.White, cut: 7)));
        card.Children.Add(stack);
        TextOptions.SetTextFormattingMode(card, TextFormattingMode.Display);
        System.Windows.Documents.TextElement.SetFontFamily(card, Mod.Face);
        var shift = new TranslateTransform();
        card.RenderTransform = shift;
        Tac.CardIn(card, 220, t =>
        {
            card.Opacity = Out(t); shift.Y = 5 * (1 - Out(t));
            grow.ScaleX = Math.Max(.001, Out(Seg(t, .2, 1)));
        });
        return card;
    }

    /// <summary>An ability's bar on the card: clear glass with the class's breath in it, a light along its top and foot, a short white edge low at its end and a small fan of spectrum past it.</summary>
    static void AbilityBar(DrawingContext dc, Size s, Color tone, double part, double rightCols)
    {
        if (s.Width < 20) return;
        var r = new Rect(.5, 1.5, s.Width - 1, s.Height - 3);
        const double fan = 2 + 5 * 1.1 + 2;
        double fx = Math.Max(0, r.Width - rightCols) * part;
        if (fx < 1) return;
        var fill = new Rect(r.X, r.Y, fx, r.Height);
        dc.PushClip(Octa(new Rect(r.X, r.Y, fx + fan, r.Height), 2.5));
        dc.DrawRectangle(Mod.Grad(true, (Mod.A(Colors.White, .16), 0), (Mod.A(Colors.White, .07), 1)), null, fill);
        dc.DrawRectangle(Mod.Grad(false, (Mod.A(tone, .12), 0), (Mod.A(Mod.Mix(tone, Colors.White, .25), .3), 1)), null, fill);
        dc.DrawRectangle(Mod.Br(Colors.White, .4), null, new Rect(r.X, r.Y, fx, 1));
        double ex = r.X + fx;
        if (fx > 4)
        {
            // the edge and its split light low in the bar, under the words' baseline, so a figure beside it is never crossed
            double y0 = r.Bottom - 6;
            for (int i = 0; i < Spec.Length; i++)
                dc.DrawLine(new Pen(Frozen(Mod.A(Spec[i], .85)), .9), new Point(ex + .4 + i * .2, y0), new Point(ex + 1 + i * 1.1, r.Bottom - .5));
            dc.DrawRectangle(Mod.Br(Colors.White, .9), null, new Rect(ex - 1.2, y0 - 1, 1.2, r.Bottom - y0 + 1));
            dc.DrawRectangle(Mod.Br(Colors.White, .5), null, new Rect(r.X, r.Bottom - 1, fx, 1));
        }
        dc.Pop();
    }
}
