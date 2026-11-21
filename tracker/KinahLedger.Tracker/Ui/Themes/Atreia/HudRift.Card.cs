using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

sealed partial class HudRift : IThemeCard
{
    /// <summary>
    /// A member's skills card on the Abyss glass (the tear along its top, the stars in it): the member's head on their bar as
    /// the meter draws it (violet rising into the class colour, the fissure of rift light at its end; the emblem in its lilac
    /// ring, the name, CP, YOU, the class small at the right); DPS big in starlight with its violet glow, share and damage
    /// with small words over them; the rates; each ability on its own small bar out of the Abyss, its length its part of the
    /// top ability, a fissure at its end; the rest and the foot after faint seams. Entrance: it fades in rising a few pixels,
    /// the ability bars growing out to their length, a pulse running along the tear (260 ms).
    /// </summary>
    public FrameworkElement DrawCard(MemberCard c)
    {
        const double In = 12;
        double w = 272 + (c.CritCol ? Tac.CardRate : 0) + (c.BackCol ? Tac.CardRate : 0);
        var tone = c.ClassColor;
        var stack = new StackPanel { Margin = new Thickness(In, 11, In, 10) };

        // the head on the member's bar: emblem, name, CP, YOU ... class
        var head = new Grid { Height = 32, Margin = new Thickness(-5, 0, -5, 8) };
        head.Children.Add(new TacSketch((dc, s) =>
        {
            var r = new Rect(0, 0, s.Width, s.Height);
            var shape = Mod.Pill(r, 6);
            dc.PushClip(shape);
            dc.DrawRectangle(Mod.Grad(false, (Mod.A(Mod.Mix(DeepVioletC, tone, .12), .95), 0), (Mod.A(Mod.Mix(tone, VioletC, .3), .72), .6), (Mod.A(Mod.Mix(tone, Colors.White, .06), .88), 1)), null, r);
            dc.PushOpacity(.5); dc.DrawRectangle(Stars, null, r); dc.Pop();
            dc.DrawRectangle(Mod.Grad(true, (Mod.A(Colors.White, .13), 0), (Mod.A(Colors.White, 0), .55)), null, r);
            dc.Pop();
            Fissure(dc, s.Width - 1.5, 0, s.Height, tone, 1.1, 4);
        }));
        var hg = new Grid { Margin = new Thickness(7, 0, 12, 0) };
        hg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); hg.ColumnDefinitions.Add(new ColumnDefinition()); hg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var icon = new Border
        {
            Width = 22, Height = 22, CornerRadius = new CornerRadius(11), BorderThickness = new Thickness(1.5), BorderBrush = Frozen(Mod.A(LilacC, .7)),
            Background = Frozen(Mod.A(VoidC, .55)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0),
        };
        var ic = ThemeText.Icon(c.Class, 18, c.Name); ic.HorizontalAlignment = HorizontalAlignment.Center;
        icon.Child = ic;
        hg.Children.Add(icon);
        var who = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var name = Tt(c.Name, 15, Frozen(Colors.White), FontWeights.Bold); name.TextTrimming = TextTrimming.CharacterEllipsis;
        who.Children.Add(name);
        double extra = 0;
        if (c.Cp.Length > 0)
        {
            var cp = Tt("", 10, Frozen(Mod.A(Color.FromRgb(0xE4, 0xDA, 0xFF), .74)), FontWeights.Medium);
            cp.Margin = new Thickness(6, 3, 0, 0);
            cp.Inlines.Add(new System.Windows.Documents.Run(L.T("CP") + " "));
            cp.Inlines.Add(new System.Windows.Documents.Run(c.Cp) { FontWeight = FontWeights.Bold, Foreground = White });
            who.Children.Add(cp); extra += Mod.W(cp) + 6;
        }
        if (c.You) { var y = Mark(L.T("YOU"), filled: true, size: 9); y.Margin = new Thickness(6, 0, 0, 0); who.Children.Add(y); extra += Mod.W(y) + 6; }
        Grid.SetColumn(who, 1); hg.Children.Add(who);
        var cls = Tt(c.ClassName, 10, Soft, FontWeights.SemiBold); cls.Margin = new Thickness(8, 1, 0, 0);
        Grid.SetColumn(cls, 2); hg.Children.Add(cls);
        head.Children.Add(hg);
        // the card widens for a long name (up to 60 more) before the name is cut
        double need = 22 + 8 + Mod.W(name) + extra + Mod.W(cls) + 8 + 2 * In + 12;
        w = Math.Max(w, Math.Min(w + 60, need));
        double inner = w - 2 * In;
        stack.Width = inner;
        name.MaxWidth = Math.Max(60, inner + 10 - 7 - 12 - 30 - extra - Mod.W(cls) - 8);
        stack.Children.Add(head);

        // the figures: DPS big in starlight, share and damage, each under its small word
        var read = new Grid();
        read.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
        read.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        read.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        void Part(int col, string word, string fig, double size, bool hero)
        {
            var sp = new Grid { Height = 38 };
            var lb = Tt(word, 8.5, Lilac, FontWeights.Bold); lb.VerticalAlignment = VerticalAlignment.Top;
            sp.Children.Add(lb);
            var fv = Tt(fig, size, White, FontWeights.Bold); fv.VerticalAlignment = VerticalAlignment.Bottom; fv.Margin = new Thickness(0, 0, 0, -size * .18 + 1);
            if (hero) fv.Effect = Glow(9, .75);
            sp.Children.Add(fv);
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
            var rl = Tt("", 10, Quiet, FontWeights.Medium);
            rl.Margin = new Thickness(0, 7, 0, 0); rl.ToolTip = c.RatesLine; rl.TextTrimming = TextTrimming.CharacterEllipsis;
            foreach (var (word, fig) in rates)
            {
                if (rl.Inlines.Count > 0) rl.Inlines.Add(new System.Windows.Documents.Run("   "));
                rl.Inlines.Add(new System.Windows.Documents.Run(word + " "));
                rl.Inlines.Add(new System.Windows.Documents.Run(fig) { FontWeight = FontWeights.Bold, Foreground = White, FontSize = 11.5 });
            }
            stack.Children.Add(rl);
        }

        // the abilities: each on its own small bar out of the Abyss, as long as its part of the top one, a fissure at its end
        var grows = new List<ScaleTransform>();
        if (c.Abilities.Count > 0)
        {
            stack.Children.Add(Seam(new Thickness(0, 8, 0, 5), .8));
            var hd = Tac.CardGrid(c, 46, 56, 14);
            TextBlock H(string s, bool right = true) { var t = Tt(s, 8, Lilac, FontWeights.Bold); if (right) t.HorizontalAlignment = HorizontalAlignment.Right; return t; }
            Tac.Cell(hd, H(L.T("SKILL"), false), 0, c);
            Tac.Cell(hd, H(L.T("SHARE")), 1, c);
            Tac.Cell(hd, H(L.T("DMG")), 2, c);
            if (c.CritCol) Tac.Cell(hd, H(L.T("CRIT")), 3, c);
            if (c.BackCol) Tac.Cell(hd, H(L.T("BACK")), 4, c);
            hd.Margin = new Thickness(0, 0, 7, 2);
            stack.Children.Add(hd);
            int n = 0;
            foreach (var a in c.Abilities)
            {
                var row = new Grid { Height = 21, Margin = new Thickness(-4, 1, 0, 0), ToolTip = Tac.AbilityTip(a) };
                double of = a.OfTop;
                int seed = n++;
                row.Children.Add(new TacSketch((dc, s) => dc.DrawRoundedRectangle(Mod.Br(VoidC, .55), null, new Rect(0, 1.5, s.Width, s.Height - 3), 4, 4)));
                // the bar drawn once at its length; on the card's entrance it only grows out (a scale) from its root
                var grow = new ScaleTransform(1, 1);
                grows.Add(grow);
                var art = new TacSketch((dc, s) =>
                {
                    var r = new Rect(0, 1.5, s.Width, s.Height - 3);
                    double fx = s.Width * Math.Clamp(of, 0, 1);
                    if (fx < 1) return;
                    var fill = new Rect(0, r.Y, fx, r.Height);
                    dc.PushClip(new RectangleGeometry(r, 4, 4));
                    dc.DrawRectangle(Mod.Grad(false, (Mod.A(Mod.Mix(DeepVioletC, tone, .12), .9), 0), (Mod.A(Mod.Mix(tone, VioletC, .35), .55), 1)), null, fill);
                    dc.DrawRectangle(Mod.Grad(true, (Mod.A(Colors.White, .1), 0), (Mod.A(Colors.White, 0), .6)), null, fill);
                    dc.Pop();
                    Fissure(dc, Math.Max(1.5, fx - .5), r.Y, r.Height, tone, .32, seed);   // faint: the figures sit over it
                });
                art.RenderTransform = grow;
                row.Children.Add(art);
                var g = Tac.CardGrid(c, 46, 56);
                g.Margin = new Thickness(4, 0, 7, 0);   // the last figure clear of the top ability's fissure
                var nm = Tac.AbilityName(a, 11.5, Frozen(Colors.White), Mod.Face, FontWeights.SemiBold, Frozen(Mod.Mix(tone, Colors.White, .55)), 9.5);
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
            stack.Children.Add(Seam(new Thickness(0, 7, 0, 5), .8));
            var fl = Tt(c.FootLine, 10, Soft, FontWeights.Medium); fl.TextTrimming = TextTrimming.CharacterEllipsis; fl.ToolTip = c.FootLine;
            stack.Children.Add(fl);
        }

        // the glass under it all, its tear along the top
        // (inset in the card so the drop under the glass stays inside the card's bounds; the pulse a layer over it)
        var glass = new TacSketch((dc, s) => RiftPanel(dc, s, 12, 1)) { Margin = new Thickness(7, 4, 8, 14) };
        var pulse = new RiftPulse(12) { Margin = glass.Margin };
        stack.Margin = new Thickness(stack.Margin.Left + 7, stack.Margin.Top + 4, stack.Margin.Right + 8, stack.Margin.Bottom + 14);
        var card = new Grid();
        card.Children.Add(glass);
        card.Children.Add(pulse);
        card.Children.Add(stack);
        TextOptions.SetTextFormattingMode(card, TextFormattingMode.Display);
        System.Windows.Documents.TextElement.SetFontFamily(card, Mod.Face);
        var shift = new TranslateTransform();
        card.RenderTransform = shift;
        Tac.CardIn(card, 260, t =>
        {
            card.Opacity = Out(t); shift.Y = 5 * (1 - Out(t));
            foreach (var g in grows) g.ScaleX = Math.Max(.01, Out(Seg(t, .2, 1)));
            pulse.At(t);
        });
        return card;
    }
}
