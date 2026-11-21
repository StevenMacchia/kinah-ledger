using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

sealed partial class HudManga : IThemeCard
{
    /// <summary>
    /// A member's skills card as a manga panel: a sheet of paper with a brushed ink edge and screentone in its lower right
    /// corner. The head is a black ink panel (the class's art in a white ring, the name in the heavy face, CP, YOU in red,
    /// the class at the right); DPS, share and damage in Impact ink on the paper, each under its small word; the rates; the
    /// abilities on a black panel, each with a brush stroke in the class's colour under it as long as its part of the top
    /// ability; the rest and the foot in ink. Entrance: it slaps down, a few pixels from the left and a touch large (200 ms).
    /// </summary>
    public FrameworkElement DrawCard(MemberCard c)
    {
        const double In = 11;
        double w = 274 + (c.CritCol ? Tac.CardRate : 0) + (c.BackCol ? Tac.CardRate : 0);
        var tone = c.ClassColor;
        var stack = new StackPanel { Margin = new Thickness(In, In, In, In) };

        // the head: a black ink panel
        var head = new Border { Background = InkB, Height = 34, Margin = new Thickness(-4, -3, -4, 8), Padding = new Thickness(8, 0, 9, 0) };
        var hg = new Grid();
        hg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); hg.ColumnDefinitions.Add(new ColumnDefinition()); hg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var icon = ThemeText.Icon(c.Class, 22, c.Name);
        if (icon is Border ib) { ib.BorderBrush = White; ib.BorderThickness = new Thickness(1.5); }
        icon.Margin = new Thickness(0, 0, 8, 0);
        hg.Children.Add(icon);
        var who = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var name = Tt(c.Name, 15, White, FontWeights.Black, NameFace); name.TextTrimming = TextTrimming.CharacterEllipsis;
        who.Children.Add(name);
        double extra = 0;
        if (c.Cp.Length > 0)
        {
            var cp = new TextBlock { FontFamily = Mod.Face, FontSize = 10, Foreground = OnInkSoft, Margin = new Thickness(6, 3, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            cp.Inlines.Add(new System.Windows.Documents.Run(L.T("CP") + " ") { FontWeight = FontWeights.Bold });
            cp.Inlines.Add(new System.Windows.Documents.Run(c.Cp) { FontWeight = FontWeights.Black, Foreground = White });
            who.Children.Add(cp); extra += Mod.W(cp);
        }
        if (c.You) { var y = Mark(L.T("YOU"), red: true); y.Margin = new Thickness(6, 0, 0, 0); who.Children.Add(y); extra += Mod.W(y); }
        Grid.SetColumn(who, 1); hg.Children.Add(who);
        var cls = Tt(c.ClassName, 10, OnInkSoft, FontWeights.Bold); cls.Margin = new Thickness(8, 1, 0, 0);
        Grid.SetColumn(cls, 2); hg.Children.Add(cls);
        head.Child = hg;
        // the card widens for a long name (up to 60 more) before the name is cut
        double need = 22 + 8 + Mod.W(name) + extra + Mod.W(cls) + 8 + 2 * In + 17 - 8;
        w = Math.Max(w, Math.Min(w + 60, need));
        double inner = w - 2 * In;
        stack.Width = inner;
        name.MaxWidth = Math.Max(60, inner + 8 - 17 - 30 - extra - Mod.W(cls) - 8);
        stack.Children.Add(head);

        // the figures: DPS big, share and damage, each under its small word, Impact ink on the paper
        var read = new Grid();
        read.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
        read.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        read.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        void Part(int col, string word, string fig, double size, Brush fg)
        {
            var sp = new StackPanel();
            sp.Children.Add(Tt(word, 8.5, OnPaperQuiet, FontWeights.Black));
            var fv = Fig(fig, size, fg); fv.HorizontalAlignment = HorizontalAlignment.Left; fv.Margin = new Thickness(0, -1, 0, 0);
            sp.Children.Add(fv);
            Grid.SetColumn(sp, col); read.Children.Add(sp);
        }
        Part(0, L.T("DPS"), c.Dps.Length > 0 ? c.Dps : "–", 25, InkB);
        Part(1, L.T("SHARE"), c.Share, 18, InkB);
        Part(2, L.T("DAMAGE"), c.Damage, 18, InkB);
        stack.Children.Add(read);

        // the rates: "crit 34%   from crits 41%   from behind 12%" (the whole sentence on hover)
        var rates = Tac.Rates(c);
        if (rates.Count > 0)
        {
            var rl = new TextBlock { FontFamily = Mod.Face, FontSize = 10, Margin = new Thickness(0, 5, 0, 0), ToolTip = c.RatesLine, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = OnPaperSoft };
            System.Windows.Documents.Typography.SetNumeralAlignment(rl, FontNumeralAlignment.Tabular);
            foreach (var (word, fig) in rates)
            {
                if (rl.Inlines.Count > 0) rl.Inlines.Add(new System.Windows.Documents.Run("   "));
                rl.Inlines.Add(new System.Windows.Documents.Run(word + " ") { FontWeight = FontWeights.SemiBold });
                rl.Inlines.Add(new System.Windows.Documents.Run(fig) { FontWeight = FontWeights.Black, Foreground = InkB, FontSize = 11.5 });
            }
            stack.Children.Add(rl);
        }

        // the abilities on a black panel, each over a brush stroke in the class's colour
        var bars = new List<TacSketch>();
        double grow = 1;
        if (c.Abilities.Count > 0)
        {
            var panel = new StackPanel { Margin = new Thickness(6, 4, 6, 5) };
            var hd = Tac.CardGrid(c, 46, 56, 14);
            TextBlock H(string s, bool right = true) { var t = Tt(s, 8, OnInkQuiet, FontWeights.Black); if (right) t.HorizontalAlignment = HorizontalAlignment.Right; return t; }
            Tac.Cell(hd, H(L.T("SKILL"), false), 0, c);
            Tac.Cell(hd, H(L.T("SHARE")), 1, c);
            Tac.Cell(hd, H(L.T("DMG")), 2, c);
            if (c.CritCol) Tac.Cell(hd, H(L.T("CRIT")), 3, c);
            if (c.BackCol) Tac.Cell(hd, H(L.T("BACK")), 4, c);
            hd.Margin = new Thickness(0, 0, 0, 1);
            panel.Children.Add(hd);
            var stroke = Frozen(tone);
            foreach (var a in c.Abilities)
            {
                var row = new Grid { Height = 21, ToolTip = Tac.AbilityTip(a) };
                double of = a.OfTop;
                var art = new TacSketch((dc, s) =>
                {
                    double x1 = (s.Width - 2) * Math.Clamp(of * grow, 0, 1);
                    if (x1 < 2) return;
                    dc.DrawGeometry(stroke, null, new PolygonG(new[] { P(0, s.Height - 5), P(x1 - 3, s.Height - 5.5), P(x1 + 2, s.Height - 3.2), P(x1 - 4, s.Height - 1), P(0, s.Height - 1.2) }).Geo);
                });
                bars.Add(art);
                row.Children.Add(art);
                var g = Tac.CardGrid(c, 46, 56);
                g.Margin = new Thickness(0, 0, 0, 4);
                var nm = Tac.AbilityName(a, 11.5, White, Mod.Face, FontWeights.Bold, Frozen(Mod.Mix(tone, Colors.White, .55)), 9.5);
                nm.Margin = new Thickness(0, 0, 6, 0);
                Tac.Cell(g, nm, 0, c);
                var sh = Tt(a.ShareText, 10.5, OnInkSoft, FontWeights.Bold); sh.HorizontalAlignment = HorizontalAlignment.Right; Tac.Cell(g, sh, 1, c);
                var dm = Tt(a.Damage, 11.5, White, FontWeights.Black); dm.HorizontalAlignment = HorizontalAlignment.Right; Tac.Cell(g, dm, 2, c);
                if (c.CritCol) { var cr = Tt(a.Crit, 10.5, OnInkSoft, FontWeights.Bold); cr.HorizontalAlignment = HorizontalAlignment.Right; Tac.Cell(g, cr, 3, c); }
                if (c.BackCol) { var bk = Tt(a.Back, 10.5, OnInkSoft, FontWeights.Bold); bk.HorizontalAlignment = HorizontalAlignment.Right; Tac.Cell(g, bk, 4, c); }
                row.Children.Add(g);
                panel.Children.Add(row);
            }
            if (c.MoreLine.Length > 0) { var ml = Tt(c.MoreLine, 10, OnInkQuiet, FontWeights.Bold); ml.Margin = new Thickness(0, 3, 0, 0); panel.Children.Add(ml); }
            stack.Children.Add(new Border { Background = InkB, Child = panel, Margin = new Thickness(-4, 8, -4, 0) });
        }
        if (c.FootLine.Length > 0)
        {
            var fl = Tt(c.FootLine, 10, OnPaperSoft, FontWeights.Bold); fl.TextTrimming = TextTrimming.CharacterEllipsis; fl.ToolTip = c.FootLine; fl.Margin = new Thickness(0, 6, 22, 0);
            stack.Children.Add(fl);
        }

        // the sheet: paper in a brushed ink edge, tone in its lower right corner, a hard ink shadow stamped under it
        stack.Margin = new Thickness(In, In, In + 5, In + 6);
        var card = new Grid { Margin = new Thickness(4, 4, 5, 6) };
        card.Children.Add(new TacSketch((dc, s) =>
        {
            if (s.Width < 20) return;
            // the sheet inside the part, its stamped shadow in the last few pixels at its right and foot
            double W = s.Width - 5, Hh = s.Height - 6;
            dc.DrawGeometry(Frozen(Mod.A(InkC, .55)), null, Brushed(new[] { P(5, 6), P(W + 4, 5), P(W + 4, Hh + 5), P(5, Hh + 5) }, 101, .8, 14));
            var sheet = Brushed(new[] { P(0, 1), P(W, 0), P(W - .5, Hh), P(.5, Hh - .5) }, 102, .7, 14);
            dc.DrawGeometry(PaperB, Pn(InkB, 2.4), sheet);
            Tone(dc, new RectangleGeometry(new Rect(W - 34, Hh - 22, 32, 20)), P(W, Hh), 34, 4, 1.7);
        }));
        card.Children.Add(stack);
        TextOptions.SetTextFormattingMode(card, TextFormattingMode.Display);
        System.Windows.Documents.TextElement.SetFontFamily(card, Mod.Face);
        var shift = new TranslateTransform();
        var sc = new ScaleTransform(1, 1);
        var tg = new TransformGroup(); tg.Children.Add(sc); tg.Children.Add(shift);
        card.RenderTransformOrigin = new Point(0, .5);
        card.RenderTransform = tg;
        Tac.CardIn(card, 200, t =>
        {
            card.Opacity = Out(Seg(t, 0, .5)); shift.X = -6 * (1 - Out(t));
            sc.ScaleX = sc.ScaleY = 1 + .05 * (1 - Out(t));
            grow = Out(Seg(t, .2, 1));
            foreach (var art in bars) art.InvalidateVisual();
        });
        return card;
    }
}
