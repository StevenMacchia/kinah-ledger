using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

sealed partial class HudHype : IThemeCard
{
    /// <summary>
    /// A member's skills card in the Hype package: a black plate with cut corners, the chrome bevel, the lime stripe and the
    /// magenta tab, speed lines behind its head. The head on an angled bar in the member's class colour (dark to full, as their
    /// meter row has it): the art in a chrome ring, the name, CP, YOU, the class small at the right. DPS huge in italics, share
    /// and damage beside it, each under a small lime word; the rates; each ability on its own angled class bar (dark, so the figures on it read),
    /// its length its part of the top ability; the rest and the foot after chrome hairlines. Entrance: it snaps in from the
    /// left with a small overshoot while the ability bars shoot out (240 ms).
    /// </summary>
    public FrameworkElement DrawCard(MemberCard c)
    {
        const double In = 13;
        double w = 272 + (c.CritCol ? Tac.CardRate : 0) + (c.BackCol ? Tac.CardRate : 0);
        var tone = c.ClassColor;
        var stack = new StackPanel { Margin = new Thickness(In, 12, In + 14, 14) };

        // the head on the class bar: art, name, CP, YOU ... class
        var head = new Grid { Height = 32, Margin = new Thickness(-4, 0, -4, 8) };
        head.Children.Add(new TacSketch((dc, s) =>
        {
            var g = Para(new Rect(0, 0, s.Width, s.Height), 7);
            dc.DrawGeometry(Mod.Grad(false, (Mod.Mix(tone, Colors.Black, .7), 0), (Mod.Mix(tone, Colors.Black, .35), .65), (tone, 1)), null, g);
            dc.PushClip(g);
            dc.DrawRectangle(Frozen(Mod.A(Colors.White, .3)), null, new Rect(0, 0, s.Width, 1));
            dc.Pop();
            dc.DrawGeometry(Chrome, Mod.P(Frozen(Mod.A(Colors.Black, .6)), .8), Para(new Rect(s.Width - 8, 0, 8, s.Height), 7));
        }));
        var hg = new Grid { Margin = new Thickness(9, 0, 14, 0) };
        hg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); hg.ColumnDefinitions.Add(new ColumnDefinition()); hg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var icon = ThemeText.Icon(c.Class, 20, c.Name);
        if (icon is Border ib) { ib.BorderBrush = Chrome; ib.BorderThickness = new Thickness(1.3); }
        icon.Margin = new Thickness(0, 0, 8, 0);
        hg.Children.Add(icon);
        var who = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var name = Ht(c.Name, 16, White, FontWeights.Bold); name.TextTrimming = TextTrimming.CharacterEllipsis;
        who.Children.Add(name);
        double extra = 0;
        if (c.Cp.Length > 0)
        {
            var cp = Ht("", 10.5, Frozen(Mod.A(Colors.White, .74)), FontWeights.SemiBold); cp.Margin = new Thickness(6, 3, 0, 0);
            cp.Inlines.Add(new System.Windows.Documents.Run(L.T("CP") + " "));
            cp.Inlines.Add(new System.Windows.Documents.Run(c.Cp) { FontWeight = FontWeights.Bold, Foreground = White });
            who.Children.Add(cp); extra += Mod.W(cp);
        }
        if (c.You) { var y = Mark(L.T("YOU"), filled: true); y.Margin = new Thickness(6, 0, 0, 0); who.Children.Add(y); extra += Mod.W(y); }
        Grid.SetColumn(who, 1); hg.Children.Add(who);
        var cls = Ht(c.ClassName, 11, Soft, FontWeights.SemiBold); cls.Margin = new Thickness(8, 1, 0, 0);
        Grid.SetColumn(cls, 2); hg.Children.Add(cls);
        head.Children.Add(Shaded(hg));
        // the card widens for a long name (up to 60 more) before the name is cut
        double need = 20 + 8 + Mod.W(name) + extra + Mod.W(cls) + 8 + 2 * In + 14 + 4 + 14;
        w = Math.Max(w, Math.Min(w + 60, need));
        double inner = w - 2 * In - 14;
        stack.Width = inner;
        name.MaxWidth = Math.Max(60, inner + 8 - 9 - 14 - 28 - extra - Mod.W(cls) - 8);
        stack.Children.Add(head);

        // the figures: DPS huge, share and damage, each under its small lime word
        var read = new Grid();
        read.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.25, GridUnitType.Star) });
        read.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        read.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        void Part(int col, string word, string fig, double size, Brush ink)
        {
            var sp = new Grid { Height = 40 };
            var lb = Ht(word.ToUpperInvariant(), 10.5, Frozen(Mod.A(LimeInkC, .85)), FontWeights.Bold); lb.VerticalAlignment = VerticalAlignment.Top;
            sp.Children.Add(lb);
            var fv = Ht(fig, size, ink, FontWeights.Bold); fv.VerticalAlignment = VerticalAlignment.Bottom; fv.Margin = new Thickness(0, 0, 0, -size * .16 + 1);
            sp.Children.Add(fv);
            Grid.SetColumn(sp, col); read.Children.Add(sp);
        }
        Part(0, L.T("DPS"), c.Dps.Length > 0 ? c.Dps : "–", 25, White);
        Part(1, L.T("SHARE"), c.Share, 17, White);
        Part(2, L.T("DAMAGE"), c.Damage, 17, White);
        stack.Children.Add(Shaded(read));

        // the rates: "crit 34%   from crits 41%   from behind 12%" (the whole sentence on hover)
        var rates = Tac.Rates(c);
        if (rates.Count > 0)
        {
            var rl = Ht("", 11, Quiet, FontWeights.SemiBold); rl.Margin = new Thickness(0, 7, 0, 0); rl.ToolTip = c.RatesLine; rl.TextTrimming = TextTrimming.CharacterEllipsis;
            foreach (var (word, fig) in rates)
            {
                if (rl.Inlines.Count > 0) rl.Inlines.Add(new System.Windows.Documents.Run("   "));
                rl.Inlines.Add(new System.Windows.Documents.Run(word + " "));
                rl.Inlines.Add(new System.Windows.Documents.Run(fig) { FontWeight = FontWeights.Bold, Foreground = White, FontSize = 12.5 });
            }
            stack.Children.Add(Shaded(rl));
        }

        // the abilities: each on its own angled class bar, as long as its part of the top one, a chrome cap at its end
        var bars = new List<ScaleTransform>();
        if (c.Abilities.Count > 0)
        {
            stack.Children.Add(Hairline(new Thickness(0, 8, 0, 5)));
            var hd = Tac.CardGrid(c, 46, 56, 14);
            TextBlock Hd(string s, bool right = true) { var t = Ht(s, 10, Frozen(Mod.A(LimeInkC, .85)), FontWeights.Bold); if (right) t.HorizontalAlignment = HorizontalAlignment.Right; return t; }
            Tac.Cell(hd, Hd(L.T("SKILL"), false), 0, c);
            Tac.Cell(hd, Hd(L.T("SHARE")), 1, c);
            Tac.Cell(hd, Hd(L.T("DMG")), 2, c);
            if (c.CritCol) Tac.Cell(hd, Hd(L.T("CRIT")), 3, c);
            if (c.BackCol) Tac.Cell(hd, Hd(L.T("BACK")), 4, c);
            hd.Margin = new Thickness(2, 0, 13, 2);
            stack.Children.Add(hd);
            foreach (var a in c.Abilities)
            {
                var row = new Grid { Height = 21, Margin = new Thickness(-4, 1, 0, 0), ToolTip = Tac.AbilityTip(a) };
                double of = a.OfTop;
                double figW = 46 + 56 + (c.CritCol ? Tac.CardRate : 0) + (c.BackCol ? Tac.CardRate : 0) + 13 + 6;
                var art = new TacSketch((dc, s) =>
                {
                    dc.DrawGeometry(Frozen(Mod.A(TrackC, .7)), null, Para(new Rect(0, 1, s.Width, s.Height - 2), 4));
                    // the bar stops short of the figure columns: its full length is the skill name's room
                    double bw = Math.Max(0, s.Width - figW) * Math.Clamp(of, 0, 1);
                    if (bw < 1.5) return;
                    dc.DrawGeometry(Mod.Grad(false, (Mod.Mix(tone, Colors.Black, .74), 0), (Mod.Mix(tone, Colors.Black, .5), .8), (Mod.Mix(tone, Colors.Black, .34), 1)), null, Para(new Rect(0, 1, bw, s.Height - 2), 4));
                });
                var grow = new ScaleTransform(1, 1); art.RenderTransform = grow; bars.Add(grow);
                row.Children.Add(art);
                var g = Tac.CardGrid(c, 46, 56);
                g.Margin = new Thickness(6, 0, 13, 0);
                var nm = Tac.AbilityName(a, 12, White, Face, FontWeights.SemiBold, Frozen(Mod.Mix(tone, Colors.White, .55)), 10, FontStretches.Condensed);
                nm.FontStyle = FontStyles.Italic;
                nm.Margin = new Thickness(2, 0, 6, 0);
                Tac.Cell(g, nm, 0, c);
                var sh = Ht(a.ShareText, 11, Soft, FontWeights.SemiBold); sh.HorizontalAlignment = HorizontalAlignment.Right; Tac.Cell(g, sh, 1, c);
                var dm = Ht(a.Damage, 12.5, White, FontWeights.Bold); dm.HorizontalAlignment = HorizontalAlignment.Right; Tac.Cell(g, dm, 2, c);
                if (c.CritCol) { var cr = Ht(a.Crit, 11, Soft, FontWeights.SemiBold); cr.HorizontalAlignment = HorizontalAlignment.Right; Tac.Cell(g, cr, 3, c); }
                if (c.BackCol) { var bk = Ht(a.Back, 11, Soft, FontWeights.SemiBold); bk.HorizontalAlignment = HorizontalAlignment.Right; Tac.Cell(g, bk, 4, c); }
                row.Children.Add(Shaded(g));
                stack.Children.Add(row);
            }
            if (c.MoreLine.Length > 0) { var ml = Ht(c.MoreLine, 11, Quiet, FontWeights.SemiBold); ml.Margin = new Thickness(0, 4, 0, 0); stack.Children.Add(ml); }
        }
        if (c.FootLine.Length > 0)
        {
            stack.Children.Add(Hairline(new Thickness(0, 7, 0, 5)));
            var fl = Ht(c.FootLine, 11, Soft, FontWeights.SemiBold); fl.TextTrimming = TextTrimming.CharacterEllipsis; fl.ToolTip = c.FootLine;
            stack.Children.Add(fl);
        }

        // the plate behind it all
        var card = new Grid { Margin = new Thickness(4, 4, 10, 10) };
        card.Children.Add(new TacSketch((dc, s) => Plate(dc, s, 1, LimeC, 46)));
        card.Children.Add(stack);
        TextOptions.SetTextFormattingMode(card, TextFormattingMode.Display);
        System.Windows.Documents.TextElement.SetFontFamily(card, Face);
        var shift = new TranslateTransform();
        card.RenderTransform = shift;
        Tac.CardIn(card, 240, t =>
        {
            card.Opacity = Math.Min(1, Out(t) * 1.6); shift.X = -12 * (1 - Back(t, 2));
            foreach (var g in bars) g.ScaleX = Out(Seg(t, .15, 1));
        });
        return card;
    }
}
