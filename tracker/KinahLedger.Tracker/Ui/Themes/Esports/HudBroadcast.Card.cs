using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

sealed partial class HudBroadcast : IThemeCard
{
    /// <summary>
    /// A member's skills card as a broadcast's player-stats panel: a navy panel under a cyan bar, its foot cut on the slant;
    /// the member's name plate on their class's colour (the art, the name, CP, YOU, the class small at its end); three slanted
    /// stat tiles (DPS on cyan's edge, share, damage), each with its word in cyan over its figure; the rates; each ability on a
    /// slanted track filled in the class's colour to its part of the top ability, with a hard leading edge; the rest and the
    /// foot after hairlines. Entrance: it wipes in on the slant as its bars slide out (240 ms), transforms only.
    /// </summary>
    public FrameworkElement DrawCard(MemberCard c)
    {
        const double In = 12;
        double w = 280 + (c.CritCol ? Tac.CardRate : 0) + (c.BackCol ? Tac.CardRate : 0);
        var tone = c.ClassColor;
        var stack = new StackPanel { Margin = new Thickness(In, 12, In, 12) };

        // the name plate on the class's colour: art, name, CP, YOU ... class
        var hg = new Grid();
        hg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); hg.ColumnDefinitions.Add(new ColumnDefinition()); hg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var icon = ThemeText.Icon(c.Class, 20, c.Name);
        if (icon is Border ib) { ib.BorderBrush = Frozen(Mod.A(Colors.White, .8)); ib.BorderThickness = new Thickness(1); }
        icon.Margin = new Thickness(0, 0, 7, 0);
        hg.Children.Add(icon);
        var who = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var name = Bc(c.Name, 17, White, FontWeights.Bold); name.TextTrimming = TextTrimming.CharacterEllipsis;
        who.Children.Add(name);
        double extra = 0;
        if (c.Cp.Length > 0)
        {
            var cp = new TextBlock { FontFamily = Cond, FontStretch = FontStretches.Condensed, FontSize = 11.5, Foreground = Frozen(Mod.A(Colors.White, .78)), Margin = new Thickness(6, 3, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            cp.Inlines.Add(new System.Windows.Documents.Run(L.T("CP") + " ") { FontWeight = FontWeights.SemiBold });
            cp.Inlines.Add(new System.Windows.Documents.Run(c.Cp) { FontWeight = FontWeights.Bold, Foreground = White });
            who.Children.Add(cp); extra += Mod.W(cp);
        }
        if (c.You) { var y = Mark(L.T("YOU"), filled: true); y.Margin = new Thickness(5, 0, 0, 0); who.Children.Add(y); extra += Mod.W(y) + 5; }
        Grid.SetColumn(who, 1); hg.Children.Add(who);
        var cls = Bc(c.ClassName, 11.5, Soft, FontWeights.SemiBold); cls.Margin = new Thickness(8, 1, 0, 0);
        Grid.SetColumn(cls, 2); hg.Children.Add(cls);
        var head = new Slab(Shaded(hg), Mod.Mix(tone, InkC, .35), 1, CyanC, 32, padX: 6, accentW: 4) { Margin = new Thickness(-4, 0, -4, 8) };
        head.Paint = (dc, s, shape, l) => dc.DrawGeometry(Mod.Grad(false, (Mod.A(InkC, .55), 0), (Mod.A(InkC, 0), .7)), null, shape);
        // the card widens for a long name (up to 60 more) before the name is cut
        double need = 20 + 7 + Mod.W(name) + extra + Mod.W(cls) + 8 + 2 * In + 32 * Tan + 24;
        w = Math.Max(w, Math.Min(w + 60, need));
        double inner = w - 2 * In;
        stack.Width = inner;
        name.MaxWidth = Math.Max(60, inner + 8 - 32 * Tan - 4 - 12 - 27 - extra - Mod.W(cls) - 8);
        stack.Children.Add(head);

        // the stat tiles: DPS, share, damage, each its word in cyan over its figure
        var read = new Grid();
        read.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.25, GridUnitType.Star) });
        read.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        read.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var tiles = new List<Slab>();
        void Part(int col, string word, string fig, double size, bool lead)
        {
            var sp = new StackPanel();
            sp.Children.Add(Bc(word.ToUpperInvariant(), 9.5, Frozen(Mod.Mix(CyanC, Colors.White, .2)), FontWeights.Bold));
            var fv = Bc(fig, size, White); fv.Margin = new Thickness(0, -2, 0, -1);
            sp.Children.Add(fv);
            var t = new Slab(Shaded(sp), lead ? Ink2C : InkC, .95, lead ? CyanC : null, 38, padX: 4, padY: 2) { Margin = new Thickness(col == 0 ? 0 : 3 - 38 * Tan, 0, 0, 0) };
            Grid.SetColumn(t, col); read.Children.Add(t);
            tiles.Add(t);
        }
        Part(0, L.T("DPS"), c.Dps.Length > 0 ? c.Dps : "–", 22, true);
        Part(1, L.T("SHARE"), c.Share, 17, false);
        Part(2, L.T("DAMAGE"), c.Damage, 17, false);
        stack.Children.Add(read);

        // the rates: "crit 34%   from crits 41%   from behind 12%" (the whole sentence on hover)
        var rates = Tac.Rates(c);
        if (rates.Count > 0)
        {
            var rl = new TextBlock { FontFamily = Cond, FontSize = 11.5, Margin = new Thickness(2, 8, 0, 0), ToolTip = c.RatesLine, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = Quiet };
            System.Windows.Documents.Typography.SetNumeralAlignment(rl, FontNumeralAlignment.Tabular);
            foreach (var (word, fig) in rates)
            {
                if (rl.Inlines.Count > 0) rl.Inlines.Add(new System.Windows.Documents.Run("  /  ") { Foreground = Frozen(Mod.A(CyanC, .7)) });
                rl.Inlines.Add(new System.Windows.Documents.Run(word + " ") { FontWeight = FontWeights.SemiBold });
                rl.Inlines.Add(new System.Windows.Documents.Run(fig) { FontWeight = FontWeights.Bold, Foreground = White, FontSize = 13 });
            }
            stack.Children.Add(rl);
        }

        // the abilities: each on a slanted track, filled in the class's colour to its part of the top one
        // each bar: its track drawn once; its fill drawn the track's whole length once, slid into place by a transform under
        // the track's clip (the entrance runs the slide, nothing is drawn again)
        var bars = new List<(TranslateTransform X, Grid Host, double Of)>();
        if (c.Abilities.Count > 0)
        {
            var hd = Tac.CardGrid(c, 46, 58, 16);
            TextBlock H(string s, bool right = true) { var t = Bc(s.ToUpperInvariant(), 9.5, Frozen(Mod.Mix(CyanC, Colors.White, .2)), FontWeights.Bold); if (right) t.HorizontalAlignment = HorizontalAlignment.Right; return t; }
            Tac.Cell(hd, H(L.T("SKILL"), false), 0, c);
            Tac.Cell(hd, H(L.T("SHARE")), 1, c);
            Tac.Cell(hd, H(L.T("DMG")), 2, c);
            if (c.CritCol) Tac.Cell(hd, H(L.T("CRIT")), 3, c);
            if (c.BackCol) Tac.Cell(hd, H(L.T("BACK")), 4, c);
            hd.Margin = new Thickness(2, 9, 0, 2);
            stack.Children.Add(hd);
            foreach (var a in c.Abilities)
            {
                var row = new Grid { Height = 21, Margin = new Thickness(0, 2, 0, 0), ToolTip = Tac.AbilityTip(a) };
                double of = Math.Clamp(a.OfTop, 0, 1);
                const double l = 21 * Tan;
                var track = new TacSketch((dc, s) => dc.DrawGeometry(Frozen(Mod.A(Ink2C, .85)), null, Cut(new Rect(0, 0, s.Width, s.Height), l)));
                var fx = new TranslateTransform();
                var fill = new TacSketch((dc, s) =>
                {
                    var fg = Cut(new Rect(0, 0, s.Width, s.Height), l);
                    dc.DrawGeometry(Mod.Grad(false, (Mod.A(Mod.Mix(tone, InkC, .55), .9), 0), (Mod.A(Mod.Mix(tone, InkC, .15), .92), 1)), null, fg);
                    dc.PushClip(fg);
                    dc.DrawGeometry(Frozen(Mod.Mix(tone, Colors.White, .55)), null, Cut(new Rect(s.Width - 2 - l, 0, 2 + l, s.Height), l));
                    dc.Pop();
                }) { RenderTransform = fx };
                var host = new Grid { IsHitTestVisible = false, Children = { fill } };
                host.SizeChanged += (_, e) =>
                {
                    var g = Cut(new Rect(0, 0, e.NewSize.Width, e.NewSize.Height), l); g.Freeze();
                    host.Clip = g;
                    fx.X = -(1 - of) * (e.NewSize.Width + l);
                };
                bars.Add((fx, host, of));
                row.Children.Add(track);
                row.Children.Add(host);
                var g = Tac.CardGrid(c, 46, 58);
                g.Margin = new Thickness(l / 2 + 4, 0, l + 4, 0);
                var nm = Tac.AbilityName(a, 12.5, White, Cond, FontWeights.SemiBold, Frozen(Mod.Mix(tone, Colors.White, .55)), 10.5, FontStretches.Condensed);
                nm.Margin = new Thickness(0, 0, 6, 0);
                Tac.Cell(g, nm, 0, c);
                var sh = Bc(a.ShareText, 12, Soft, FontWeights.SemiBold); sh.HorizontalAlignment = HorizontalAlignment.Right; Tac.Cell(g, sh, 1, c);
                var dm = Bc(a.Damage, 13, White); dm.HorizontalAlignment = HorizontalAlignment.Right; Tac.Cell(g, dm, 2, c);
                if (c.CritCol) { var cr = Bc(a.Crit, 12, Soft, FontWeights.SemiBold); cr.HorizontalAlignment = HorizontalAlignment.Right; Tac.Cell(g, cr, 3, c); }
                if (c.BackCol) { var bk = Bc(a.Back, 12, Soft, FontWeights.SemiBold); bk.HorizontalAlignment = HorizontalAlignment.Right; Tac.Cell(g, bk, 4, c); }
                row.Children.Add(Shaded(g));
                stack.Children.Add(row);
            }
            if (c.MoreLine.Length > 0) { var ml = Bc(c.MoreLine, 11.5, Quiet, FontWeights.SemiBold, tight: false); ml.Margin = new Thickness(2, 5, 0, 0); stack.Children.Add(ml); }
        }
        if (c.FootLine.Length > 0)
        {
            stack.Children.Add(new Border { Height = 1, Background = Frozen(Mod.A(CyanC, .3)), Margin = new Thickness(0, 8, 0, 5) });
            var fl = Bc(c.FootLine, 11.5, Soft, FontWeights.Medium, tight: false); fl.TextTrimming = TextTrimming.CharacterEllipsis; fl.ToolTip = c.FootLine;
            stack.Children.Add(fl);
        }

        // the panel: navy, a cyan bar along its top, its foot's right corner cut on the slant
        const double cutC = 14;
        var panel = new Grid();
        var ground = new TacSketch((dc, s) =>
        {
            var g = new StreamGeometry();
            using (var x = g.Open())
            {
                x.BeginFigure(new Point(.5, .5), true, true);
                x.LineTo(new Point(s.Width - .5, .5), true, false);
                x.LineTo(new Point(s.Width - .5, s.Height - cutC), true, false);
                x.LineTo(new Point(s.Width - cutC * Tan * 4.7, s.Height - .5), true, false);
                x.LineTo(new Point(.5, s.Height - .5), true, false);
            }
            g.Freeze();
            dc.DrawGeometry(Mod.Grad(true, (Mod.A(Color.FromRgb(0x0E, 0x16, 0x2B), .96), 0), (Mod.A(InkC, .95), 1)), Mod.P(Frozen(Mod.A(Colors.White, .1)), 1), g);
            dc.DrawRectangle(Cyan, null, new Rect(0, 0, s.Width, 3));
        });
        panel.Children.Add(ground);
        panel.Children.Add(stack);
        var card = new Border
        {
            Child = panel, Margin = new Thickness(4, 4, 10, 14),
            Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 14, ShadowDepth = 2, Direction = 270, Opacity = .42 },
        };
        TextOptions.SetTextFormattingMode(card, TextFormattingMode.Display);
        System.Windows.Documents.TextElement.SetFontFamily(card, Cond);
        var wiper = new Wiper(ground, 40);
        Tac.CardIn(card, 240, t =>
        {
            wiper.At(Out(Seg(t, 0, .7)));
            stack.Opacity = Seg(t, .3, .8);
            double grow = Out(Seg(t, .3, 1));
            foreach (var (x, host, of) in bars) x.X = -(1 - of * grow) * (host.ActualWidth + 21 * Tan);
        });
        return card;
    }
}
