using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

sealed partial class HudVersus : IThemeCard
{
    /// <summary>
    /// A member's skills card as a fighting game's character sheet and move list: a navy-black panel with two corners cut, a
    /// black rim and a chrome one, a hot slash across its top-left corner. At its head the fighter's own life gauge (their class
    /// colour) with the art in its portrait chip, the name in bold italic, CP and YOU; the class small at the right. DPS big in
    /// Impact yellow, share and damage under small words; the rates. Each ability a small leaning gauge, as long as its part of
    /// the top one. Entrance: it slides in from the left as a white flash crosses its head (240 ms).
    /// </summary>
    public FrameworkElement DrawCard(MemberCard c)
    {
        const double In = 14;
        double w = 270 + (c.CritCol ? Tac.CardRate : 0) + (c.BackCol ? Tac.CardRate : 0);
        var tone = c.ClassColor;
        var stack = new StackPanel { Margin = new Thickness(In, 12, In, 12) };

        // the head on the fighter's gauge: art in its chip, name, CP, YOU ... class
        var head = new Grid { Height = 32, Margin = new Thickness(-4, 0, -4, 8) };
        head.Children.Add(new TacSketch((dc, s) =>
        {
            dc.DrawGeometry(Mod.Grad(true, (Mod.Mix(tone, Colors.White, .15), 0), (Mod.Mix(tone, Colors.Black, .55), 1)), EdgeOut, Slab(new Rect(1.5, 1.5, 33, s.Height - 3)));
            dc.DrawGeometry(null, EdgeIn, Slab(new Rect(1.5, 1.5, 33, s.Height - 3)));
            Gauge(dc, new Rect(36, 1.5, s.Width - 37.5, s.Height - 3), 1, FillOf(Mod.Mix(tone, Colors.Black, .2)), ticks: false);
        }));
        var flash = new TacSketch((dc, s) => dc.DrawGeometry(Frozen(Mod.A(Colors.White, .9)), null, Slab(new Rect(1.5, 1.5, s.Width - 3, s.Height - 3)))) { Opacity = 0 };
        head.Children.Add(flash);
        var hg = new Grid { Margin = new Thickness(7, 0, 12, 0) };
        hg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); hg.ColumnDefinitions.Add(new ColumnDefinition()); hg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var icon = ThemeText.Icon(c.Class, 20, c.Name);
        icon.Margin = new Thickness(1, 0, 16, 0);
        hg.Children.Add(icon);
        var who = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var name = T(c.Name, 15, White, FontWeights.Bold, NameFace, italic: true); name.TextTrimming = TextTrimming.CharacterEllipsis;
        who.Children.Add(name);
        double extra = 0;
        if (c.Cp.Length > 0)
        {
            var cp = T("", 10, Frozen(Mod.A(Colors.White, .78)), FontWeights.Medium); cp.Margin = new Thickness(6, 3, 0, 0);
            cp.Inlines.Add(new System.Windows.Documents.Run(L.T("CP") + " "));
            cp.Inlines.Add(new System.Windows.Documents.Run(c.Cp) { FontWeight = FontWeights.Bold, Foreground = White });
            who.Children.Add(cp); extra += Mod.W(cp);
        }
        if (c.You) { var y = Badge(L.T("YOU"), CyanC); y.Margin = new Thickness(7, 0, 0, 0); who.Children.Add(y); extra += Mod.W(y); }
        Grid.SetColumn(who, 1); hg.Children.Add(who);
        var cls = T(c.ClassName, 10, Soft, FontWeights.SemiBold, italic: true); cls.Margin = new Thickness(8, 1, 0, 0);
        Grid.SetColumn(cls, 2); hg.Children.Add(cls);
        head.Children.Add(Lit(hg));
        // the card widens for a long name (up to 60 more) before the name is cut
        double need = 20 + 17 + Mod.W(name) + extra + Mod.W(cls) + 8 + 12 + 2 * In + 6;
        w = Math.Max(w, Math.Min(w + 60, need));
        double inner = w - 2 * In;
        stack.Width = inner;
        name.MaxWidth = Math.Max(60, inner + 8 - 7 - 12 - 37 - extra - Mod.W(cls) - 8);
        stack.Children.Add(head);

        // the figures: DPS big, share and damage, each under its small word
        var read = new Grid();
        read.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
        read.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        read.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        void Part(int col, string word, string fig, double size, Brush fg)
        {
            var sp = new Grid { Height = 40 };
            var lb = T(word, 8.5, Quiet, FontWeights.Bold, italic: true); lb.VerticalAlignment = VerticalAlignment.Top;
            sp.Children.Add(lb);
            var fv = Big(fig, size, fg); fv.VerticalAlignment = VerticalAlignment.Bottom; fv.Margin = new Thickness(0, 0, 2, -size * .14);
            sp.Children.Add(fv);
            Grid.SetColumn(sp, col); read.Children.Add(sp);
        }
        Part(0, L.T("DPS"), c.Dps.Length > 0 ? c.Dps : "–", 25, Yellow);
        Part(1, L.T("SHARE"), c.Share, 17, White);
        Part(2, L.T("DAMAGE"), c.Damage, 17, White);
        stack.Children.Add(Lit(read));

        // the rates: "crit 34%   from crits 41%   from behind 12%" (the whole sentence on hover)
        var rates = Tac.Rates(c);
        if (rates.Count > 0)
        {
            var rl = T("", 10, Quiet, FontWeights.Medium); rl.Margin = new Thickness(0, 8, 0, 0); rl.ToolTip = c.RatesLine; rl.TextTrimming = TextTrimming.CharacterEllipsis;
            foreach (var (word, fig) in rates)
            {
                if (rl.Inlines.Count > 0) rl.Inlines.Add(new System.Windows.Documents.Run("   "));
                rl.Inlines.Add(new System.Windows.Documents.Run(word + " "));
                rl.Inlines.Add(new System.Windows.Documents.Run(fig) { FontWeight = FontWeights.Bold, Foreground = PaleY, FontSize = 11.5 });
            }
            stack.Children.Add(rl);
        }

        // the move list: each ability a small leaning gauge, as long as its part of the top one
        var bars = new List<TacSketch>();
        double grow = 1;
        if (c.Abilities.Count > 0)
        {
            stack.Children.Add(new Border { Height = 2, Margin = new Thickness(0, 9, 0, 6), Background = HotStripe });
            var hd = Tac.CardGrid(c, 46, 56, 14);
            TextBlock H(string s, bool right = true) { var t = T(s, 8, Quiet, FontWeights.Bold, italic: true); if (right) t.HorizontalAlignment = HorizontalAlignment.Right; return t; }
            Tac.Cell(hd, H(L.T("SKILL"), false), 0, c);
            Tac.Cell(hd, H(L.T("SHARE")), 1, c);
            Tac.Cell(hd, H(L.T("DMG")), 2, c);
            if (c.CritCol) Tac.Cell(hd, H(L.T("CRIT")), 3, c);
            if (c.BackCol) Tac.Cell(hd, H(L.T("BACK")), 4, c);
            hd.Margin = new Thickness(0, 0, 0, 3);
            stack.Children.Add(hd);
            var fill = FillOf(Mod.Mix(tone, Colors.Black, .55));
            foreach (var a in c.Abilities)
            {
                var row = new Grid { Height = 21, Margin = new Thickness(-4, 1, 0, 0), ToolTip = Tac.AbilityTip(a) };
                double of = a.OfTop;
                // the gauge spans the skill's name column only: the figures beside it sit on the card's dark ground
                double nameW = Math.Max(60, inner + 4 - 46 - 56 - (c.CritCol ? Tac.CardRate : 0) - (c.BackCol ? Tac.CardRate : 0) - 2);
                var art = new TacSketch((dc, s) => Gauge(dc, new Rect(1.5, 1.5, Math.Min(s.Width, nameW) - 3, s.Height - 3), of * grow, fill, ticks: false));
                bars.Add(art);
                row.Children.Add(art);
                var g = Tac.CardGrid(c, 46, 56);
                g.Margin = new Thickness(4, 0, 0, 0);
                var nm = Tac.AbilityName(a, 11.5, White, NameFace, FontWeights.SemiBold, Frozen(Mod.Mix(tone, Colors.White, .6)), 9.5);
                nm.Margin = new Thickness(5, 0, 6, 0);
                Tac.Cell(g, nm, 0, c);
                var sh = T(a.ShareText, 10.5, Soft, FontWeights.SemiBold); sh.HorizontalAlignment = HorizontalAlignment.Right; Tac.Cell(g, sh, 1, c);
                var dm = T(a.Damage, 11.5, White, FontWeights.Bold); dm.HorizontalAlignment = HorizontalAlignment.Right; Tac.Cell(g, dm, 2, c);
                if (c.CritCol) { var cr = T(a.Crit, 10.5, Soft, FontWeights.SemiBold); cr.HorizontalAlignment = HorizontalAlignment.Right; Tac.Cell(g, cr, 3, c); }
                if (c.BackCol) { var bk = T(a.Back, 10.5, Soft, FontWeights.SemiBold); bk.HorizontalAlignment = HorizontalAlignment.Right; Tac.Cell(g, bk, 4, c); }
                row.Children.Add(Lit(g));
                stack.Children.Add(row);
            }
            if (c.MoreLine.Length > 0) { var ml = T(c.MoreLine, 10, Quiet, FontWeights.Medium, italic: true); ml.Margin = new Thickness(0, 4, 0, 0); stack.Children.Add(ml); }
        }
        if (c.FootLine.Length > 0)
        {
            stack.Children.Add(new Border { Height = 1, Margin = new Thickness(0, 8, 0, 5), Background = Frozen(Mod.A(SteelC, .25)) });
            var fl = T(c.FootLine, 10, Soft, FontWeights.Medium); fl.TextTrimming = TextTrimming.CharacterEllipsis; fl.ToolTip = c.FootLine;
            stack.Children.Add(fl);
        }

        // the panel: navy-black with two corners cut, a black rim and a chrome one, the hot slash across its top-left corner
        var card = new Grid { Margin = new Thickness(4, 4, 10, 14) };
        card.Children.Add(new TacSketch((dc, s) =>
        {
            if (s.Width < 30 || s.Height < 30) return;
            var r = new Rect(1.5, 1.5, s.Width - 3, s.Height - 3);
            var box = Chop(r, 16);
            dc.DrawGeometry(Mod.Grad(true, (Mod.A(Ink2C, .97), 0), (Mod.A(InkC, .97), 1)), EdgeOut, box);
            dc.PushClip(box);
            var slash = new StreamGeometry();
            using (var x = slash.Open())
            {
                x.BeginFigure(new Point(r.Left, r.Top + 24), true, true);
                x.LineTo(new Point(r.Left + 24, r.Top), true, false);
                x.LineTo(new Point(r.Left + 31, r.Top), true, false);
                x.LineTo(new Point(r.Left, r.Top + 31), true, false);
            }
            slash.Freeze();
            dc.DrawGeometry(HotStripe, null, slash);
            dc.Pop();
            dc.DrawGeometry(null, EdgeIn, Chop(new Rect(r.Left + 2.5, r.Top + 2.5, r.Width - 5, r.Height - 5), 14));
        }));
        card.Children.Add(stack);
        card.Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 14, ShadowDepth = 3, Direction = 270, Opacity = .5 };
        TextOptions.SetTextFormattingMode(card, TextFormattingMode.Display);
        System.Windows.Documents.TextElement.SetFontFamily(card, Print);
        var shift = new TranslateTransform();
        card.RenderTransform = shift;
        Tac.CardIn(card, 240, t =>
        {
            card.Opacity = Out(Seg(t, 0, .6)); shift.X = -12 * (1 - Out(t));
            flash.Opacity = .6 * Flash(t, .25, .5);
            grow = Out(Seg(t, .2, 1));
            foreach (var art in bars) art.InvalidateVisual();
        });
        return card;
    }
}
