using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

sealed partial class HudStorm
{
    /// <summary>A short charged conduit in <paramref name="tone"/> (the card's head, an ability's bar): deep at the left, its colour at the edge, a hot core along its foot and a white edge.</summary>
    static void Conduit(DrawingContext dc, Rect r, Color tone, double a0, double a1, double cut = 5)
    {
        if (r.Width < 2 || r.Height < 2) return;
        var shape = Cut(r, cut, Math.Min(2, cut));
        dc.DrawGeometry(Mod.Grad(false, (Mod.A(tone, a0), 0), (Mod.A(tone, a1), 1)), null, shape);
        dc.PushClip(shape);
        dc.DrawRectangle(Mod.Grad(true, (Mod.A(Colors.White, .12), 0), (Mod.A(Colors.White, 0), .5)), null, r);
        dc.DrawRectangle(Mod.Grad(false, (Mod.A(Mod.Mix(tone, Colors.White, .5), .2), 0), (Mod.A(Mod.Mix(tone, Colors.White, .75), .95), 1)), null, new Rect(r.X, r.Bottom - 3, r.Width, 1.2));
        dc.DrawRectangle(Mod.Br(Mod.Mix(tone, Colors.White, .7), 1), null, new Rect(Math.Max(r.X, r.Right - 1.5), r.Y, Math.Min(1.5, r.Width), r.Height));
        dc.Pop();
    }

    /// <summary>
    /// A member's skills card in Storm: a panel of storm-cloud glass (its charged rim, the white charge on its cut corner), the
    /// member's head on a conduit in their class colour as their meter row has it (the art in its blue ring, the name, CP,
    /// YOU, the class small at the right); DPS big in electric white, share and damage under small blue words; the rates; each
    /// ability on its own conduit, its length its part of the top ability; the rest and the foot after charged hairlines.
    /// Entrance: it fades in sliding a few pixels from the left as the conduits charge out (220 ms).
    /// </summary>
    public FrameworkElement DrawCard(MemberCard c)
    {
        const double In = 12;
        double w = 270 + (c.CritCol ? Tac.CardRate : 0) + (c.BackCol ? Tac.CardRate : 0);
        var tone = c.ClassColor;
        var stack = new StackPanel { Margin = new Thickness(In, 10 + LineCrest, In, 10) };
        var label = Frozen(Mod.A(VoltC, .85));

        // the head on the class conduit: art, name, CP, YOU ... class
        var head = new Grid { Height = 32, Margin = new Thickness(-5, 0, -5, 8) };
        head.Children.Add(new TacSketch((dc, s) =>
        {
            var r = new Rect(.5, .5, s.Width - 1, s.Height - 1);
            dc.DrawGeometry(Mod.Br(Deep0C, .7), null, Cut(r, 7, 2));
            Conduit(dc, r, tone, .22, .62, 7);
            dc.DrawGeometry(null, Mod.P(Mod.Br(VoltC, .25), 1), Cut(r, 7, 2));
        }));
        var hg = new Grid { Margin = new Thickness(7, 0, 9, 0) };
        hg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); hg.ColumnDefinitions.Add(new ColumnDefinition()); hg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var icon = ThemeText.Icon(c.Class, 20, c.Name);
        if (icon is Border ib) { ib.BorderBrush = Frozen(Mod.A(VoltC, .85)); ib.BorderThickness = new Thickness(1); }
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
        hg.Effect = Shade();
        head.Children.Add(hg);
        // the card widens for a long name (up to 60 more) before the name is cut
        double need = 20 + 8 + Mod.W(name) + extra + Mod.W(cls) + 8 + 2 * In + 4;
        w = Math.Max(w, Math.Min(w + 60, need));
        double inner = w - 2 * In;
        stack.Width = inner;
        name.MaxWidth = Math.Max(60, inner + 10 - 7 - 9 - 28 - extra - Mod.W(cls) - 8);
        stack.Children.Add(head);

        // the figures: DPS big, share and damage, each under its small blue word
        var read = new Grid();
        read.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
        read.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        read.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        void Part(int col, string word, string fig, double size, Brush ink)
        {
            var sp = new Grid { Height = 38 };
            var lb = Tt(word, 8.5, label, FontWeights.Bold); lb.VerticalAlignment = VerticalAlignment.Top;
            sp.Children.Add(lb);
            var fv = Tt(fig, size, ink, FontWeights.Bold); fv.VerticalAlignment = VerticalAlignment.Bottom; fv.Margin = new Thickness(0, 0, 0, -size * .18 + 1);
            sp.Children.Add(fv);
            Grid.SetColumn(sp, col); read.Children.Add(sp);
        }
        Part(0, L.T("DPS"), c.Dps.Length > 0 ? c.Dps : "–", 21, Arc);
        Part(1, L.T("SHARE"), c.Share, 15, White);
        Part(2, L.T("DAMAGE"), c.Damage, 15, White);
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

        // the abilities: each on its own conduit, as long as its part of the top one
        var bars = new List<TacSketch>();
        double grow = 1;
        if (c.Abilities.Count > 0)
        {
            stack.Children.Add(Hairline(new Thickness(0, 8, 0, 5), .45));
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
                var row = new Grid { Height = 22, Margin = new Thickness(-4, 1, 0, 0), ToolTip = Tac.AbilityTip(a) };
                double of = a.OfTop;
                var art = new TacSketch((dc, s) =>
                {
                    // a slim charged conduit along the row's foot, under the words (its edge never crosses a figure)
                    var r = new Rect(.5, s.Height - 5, s.Width - 1, 4);
                    dc.DrawRectangle(Mod.Br(Deep0C, .7), null, r);
                    double fw = r.Width * Math.Clamp(of * grow, 0, 1);
                    if (fw < 1) return;
                    var bar = new Rect(r.X, r.Y, fw, r.Height);
                    dc.DrawRectangle(Mod.Grad(false, (Mod.A(tone, .35), 0), (Mod.A(Mod.Mix(tone, Colors.White, .25), .95), 1)), null, bar);
                    dc.DrawRectangle(Mod.Br(Mod.Mix(tone, Colors.White, .75), .9), null, new Rect(r.X, r.Y + 1.4, fw, 1.1));
                    var g = new RadialGradientBrush(Mod.A(ArcC, .8), Mod.A(VoltC, 0)); g.Freeze();
                    dc.DrawEllipse(g, null, new Point(r.X + fw, r.Y + 2), 4, 3);
                });
                bars.Add(art);
                row.Children.Add(art);
                var g = Tac.CardGrid(c, 46, 56);
                g.Margin = new Thickness(4, 0, 0, 4);
                var nm = Tac.AbilityName(a, 11.5, White, Mod.Face, FontWeights.SemiBold, Frozen(Mod.Mix(tone, Colors.White, .55)), 9.5);
                nm.Margin = new Thickness(2, 0, 6, 0);
                Tac.Cell(g, nm, 0, c);
                var sh = Tt(a.ShareText, 10.5, Soft, FontWeights.SemiBold); sh.HorizontalAlignment = HorizontalAlignment.Right; Tac.Cell(g, sh, 1, c);
                var dm = Tt(a.Damage, 11.5, Arc, FontWeights.Bold); dm.HorizontalAlignment = HorizontalAlignment.Right; Tac.Cell(g, dm, 2, c);
                if (c.CritCol) { var cr = Tt(a.Crit, 10.5, Soft, FontWeights.SemiBold); cr.HorizontalAlignment = HorizontalAlignment.Right; Tac.Cell(g, cr, 3, c); }
                if (c.BackCol) { var bk = Tt(a.Back, 10.5, Soft, FontWeights.SemiBold); bk.HorizontalAlignment = HorizontalAlignment.Right; Tac.Cell(g, bk, 4, c); }
                g.Effect = Shade();
                row.Children.Add(g);
                stack.Children.Add(row);
            }
            if (c.MoreLine.Length > 0) { var ml = Tt(c.MoreLine, 10, Quiet, FontWeights.Medium); ml.Margin = new Thickness(0, 4, 0, 0); stack.Children.Add(ml); }
        }
        if (c.FootLine.Length > 0)
        {
            stack.Children.Add(Hairline(new Thickness(0, 7, 0, 5), .45));
            var fl = Tt(c.FootLine, 10, Soft, FontWeights.Medium); fl.TextTrimming = TextTrimming.CharacterEllipsis; fl.ToolTip = c.FootLine;
            stack.Children.Add(fl);
        }

        // the ground: storm-cloud glass (its own drop drawn round it)
        var card = new Grid { Margin = new Thickness(6, 4, 10, 12) };
        card.Children.Add(new TacSketch((dc, s) => StormPanel(dc, s, 1, 12, 3, drop: false, crest: LineCrest)));
        card.Children.Add(stack);
        TextOptions.SetTextFormattingMode(card, TextFormattingMode.Display);
        System.Windows.Documents.TextElement.SetFontFamily(card, Mod.Face);
        var shift = new TranslateTransform();
        card.RenderTransform = shift;
        Tac.CardIn(card, 220, t =>
        {
            card.Opacity = Out(t); shift.X = -6 * (1 - Out(t));
            grow = Out(Seg(t, .2, 1));
            foreach (var art in bars) art.InvalidateVisual();
        });
        return card;
    }
}
