using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

sealed partial class HudTide : IThemeCard
{
    /// <summary>
    /// A member's skills card as a pane of the sea glass (the caustic net at its top, the surface light, the aqua rim, a soft
    /// drop): the member's head on their own water as their meter row has it (the art in its aqua ring, the name, CP, YOU,
    /// the class small at the right, the crest's foam at its end); DPS big in white with its aqua halo, share and damage with
    /// small words over them; the rates; each ability a channel its water fills to its part of the top ability, a small crest
    /// on each; the rest and the foot after sea lines. Entrance: it settles a few pixels as it fades in, the abilities' water
    /// flowing in after it (240 ms).
    /// </summary>
    static readonly Brush CardEbb = Mod.Grad(false, (Mod.A(DarkC, 0), 0), (Mod.A(DarkC, .45), .3), (Mod.A(DarkC, .55), 1));

    public FrameworkElement DrawCard(MemberCard c)
    {
        const double In = 12;
        // the room over the glass for its breaking wave, inside the card (nothing is drawn past its edges)
        double Room = Math.Ceiling(CrestRoom(.7));
        double w = 274 + (c.CritCol ? Tac.CardRate : 0) + (c.BackCol ? Tac.CardRate : 0);
        var tone = c.ClassColor;
        var stack = new StackPanel { Margin = new Thickness(In, 13 + Room, In, 11) };

        // the head on the member's water: art, name, CP, YOU ... class
        var head = new Grid { Height = 32, Margin = new Thickness(-5, 0, -5, 9) };
        head.Children.Add(new TacSketch((dc, s) =>
        {
            var r = new Rect(0, 0, s.Width, s.Height);
            dc.DrawGeometry(Mod.Br(DarkC, .6), Mod.P(Mod.Br(AquaC, .14), 1), Mod.Pill(new Rect(.5, .5, s.Width - 1, s.Height - 1), 8));
            dc.PushClip(Mod.Pill(r, 8));
            WaterBar(dc, r, s.Width - 6, tone, .45, phase: 1.1);
            dc.Pop();
        }));
        var hg = new Grid { Margin = new Thickness(7, 0, 22, 0) };
        hg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); hg.ColumnDefinitions.Add(new ColumnDefinition()); hg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var icon = ThemeText.Icon(c.Class, 20, c.Name);
        if (icon is Border ib) { ib.BorderBrush = Frozen(Mod.A(AquaC, .8)); ib.BorderThickness = new Thickness(1); }
        icon.Margin = new Thickness(0, 0, 8, 0);
        hg.Children.Add(icon);
        var who = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var name = Tt(c.Name, 15, White, FontWeights.Bold); name.TextTrimming = TextTrimming.CharacterEllipsis;
        who.Children.Add(name);
        double extra = 0;
        if (c.Cp.Length > 0)
        {
            var cp = new TextBlock { FontFamily = Mod.Face, FontSize = 10, Foreground = Quiet, Margin = new Thickness(6, 3, 0, 0), VerticalAlignment = VerticalAlignment.Center };
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
        double need = 20 + 8 + Mod.W(name) + extra + Mod.W(cls) + 8 + 2 * In + 4 + 15;
        w = Math.Max(w, Math.Min(w + 60, need));
        double inner = w - 2 * In;
        stack.Width = inner;
        name.MaxWidth = Math.Max(60, inner + 10 - 7 - 22 - 28 - extra - Mod.W(cls) - 8);
        stack.Children.Add(head);

        // the figures: DPS big with its aqua halo, share and damage, each under its small word
        var read = new Grid();
        read.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
        read.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        read.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        void Part(int col, string word, string fig, double size, bool halo)
        {
            var sp = new Grid { Height = 38 };
            var lb = Tt(word, 8.5, Aqua, FontWeights.Bold); lb.VerticalAlignment = VerticalAlignment.Top;
            sp.Children.Add(lb);
            var fv = Tt(fig, size, White, FontWeights.Bold); fv.VerticalAlignment = VerticalAlignment.Bottom; fv.Margin = new Thickness(0, 0, 0, -size * .18 + 1);
            if (halo) fv.Effect = Halo(9, .6);
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

        // the abilities: each a channel its water fills, as long as its part of the top one
        // the abilities' water flows in on the entrance: one scale shared by every bar (drawn once, never redrawn)
        var grow = new ScaleTransform(1, 1);
        double ebbW = 34 + (c.CritCol ? Tac.CardRate : 0) + (c.BackCol ? Tac.CardRate : 0);
        if (c.Abilities.Count > 0)
        {
            stack.Children.Add(SeaLine(new Thickness(0, 7, 0, 4), .9));
            var hd = Tac.CardGrid(c, 46, 56, 14);
            TextBlock H(string s, bool right = true) { var t = Tt(s, 8, Aqua, FontWeights.Bold); if (right) t.HorizontalAlignment = HorizontalAlignment.Right; return t; }
            Tac.Cell(hd, H(L.T("SKILL"), false), 0, c);
            Tac.Cell(hd, H(L.T("SHARE")), 1, c);
            Tac.Cell(hd, H(L.T("DMG")), 2, c);
            if (c.CritCol) Tac.Cell(hd, H(L.T("CRIT")), 3, c);
            if (c.BackCol) Tac.Cell(hd, H(L.T("BACK")), 4, c);
            hd.Margin = new Thickness(0, 0, 0, 3);
            stack.Children.Add(hd);
            int n = 0;
            foreach (var a in c.Abilities)
            {
                var row = new Grid { Height = 21, Margin = new Thickness(-4, 1, -7, 0), ToolTip = Tac.AbilityTip(a) };
                double of = a.OfTop;
                double ph = n++ * .9;
                var art = new TacSketch((dc, s) =>
                {
                    var r = new Rect(0, 1, s.Width, s.Height - 2);
                    dc.DrawGeometry(Mod.Br(DarkC, .45), null, Mod.Pill(r, 5));
                    dc.PushClip(Mod.Pill(r, 5));
                    WaterBar(dc, r, s.Width * Math.Clamp(of, 0, 1), tone, phase: ph, alpha: .85);
                    dc.DrawRectangle(CardEbb, null, new Rect(s.Width - ebbW, 0, ebbW, s.Height));
                    dc.Pop();
                });
                art.RenderTransform = grow;
                row.Children.Add(art);
                var g = Tac.CardGrid(c, 46, 56);
                g.Margin = new Thickness(5, 0, 7, 0);
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
            stack.Children.Add(SeaLine(new Thickness(0, 6, 0, 4), .9));
            var fl = Tt(c.FootLine, 10, Soft, FontWeights.Medium); fl.TextTrimming = TextTrimming.CharacterEllipsis; fl.ToolTip = c.FootLine;
            stack.Children.Add(fl);
        }

        // the glass: the deep-sea pane with its caustics, the surface light and the aqua rim
        var card = new Grid { Margin = new Thickness(4, 4, 10, 14) };
        // the glass under the wave's room, a pixel short of the card's foot
        card.Children.Add(new TacSketch((dc, s) => SeaPanel(dc, s, 12, 1, 1.1, 20, shadow: false, surface: true, seabed: true, crest: .7)) { Margin = new Thickness(0, Room, 0, 1) });
        card.Children.Add(stack);
        TextOptions.SetTextFormattingMode(card, TextFormattingMode.Display);
        card.UseLayoutRounding = true;   // a whole-pixel height: its foot never lands part-way into a pixel past its edge
        System.Windows.Documents.TextElement.SetFontFamily(card, Mod.Face);
        var shift = new TranslateTransform();
        card.RenderTransform = shift;
        Tac.CardIn(card, 240, t =>
        {
            card.Opacity = Out(t); shift.Y = -5 * (1 - Out(t));   // settling down into place (its room is above it, under the wave)
            grow.ScaleX = .2 + .8 * Out(Seg(t, .2, 1));
        });
        return card;
    }
}
