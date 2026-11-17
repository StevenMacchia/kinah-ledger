using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

sealed partial class HudHolo : IThemeCard
{
    /// <summary>
    /// A member's skills card as a holographic readout: the look's cut-cornered glass (faint grid, lit cuts, a soft cyan halo
    /// round it), a hex in the class's colour with its emblem, the name in its class's colour (amber for you) and a CP chip, the
    /// class in the small mono; DPS, share and damage as a three-part readout between glowing dividers; the rates in a line of
    /// diamonds; the abilities each over a glowing line bar on a dashed track (its share of the top one), the rest and the foot
    /// after cyan hairlines. Entrance: projected down out of a bright scan line, the bars running out after it (240 ms).
    /// </summary>
    public FrameworkElement DrawCard(MemberCard c)
    {
        const double Pad = 12, In = 12;
        double w = 266 + (c.CritCol ? Tac.CardRate : 0) + (c.BackCol ? Tac.CardRate : 0);
        var tone = c.ClassColor;
        double grow = 1, scan = -1;
        var bars = new List<TacSketch>();
        var stack = new StackPanel { Margin = new Thickness(In, 13, In, 10) };

        // ⬡ Swoly [CP 4.8K]                       GLADIATOR
        var head = new Grid { Margin = new Thickness(0, 0, 0, 7) };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); head.ColumnDefinitions.Add(new ColumnDefinition()); head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var hex = new Grid { Width = 30, Height = 30, Margin = new Thickness(0, 0, 9, 0) };
        hex.Children.Add(new TacSketch((dc, s) =>
        {
            double cx = s.Width / 2, cy = s.Height / 2, rr = 14;
            var pts = Enumerable.Range(0, 6).Select(i => { double a = Math.PI / 3 * i - Math.PI / 2; return new Point(cx + rr * Math.Cos(a), cy + rr * Math.Sin(a)); }).ToArray();
            var g = new StreamGeometry();
            using (var x = g.Open()) { x.BeginFigure(pts[0], true, true); foreach (var p in pts.Skip(1)) x.LineTo(p, true, false); }
            g.Freeze();
            dc.DrawGeometry(null, Tac.P(Frozen(Tac.A(tone, .35)), 4, round: true), g);
            dc.DrawGeometry(Frozen(Tac.A(Tac.Mix(DeepC, tone, .3), .95)), Tac.P(Frozen(Tac.Mix(tone, Colors.White, .2)), 1.3, round: true), g);
        }));
        var icon = ThemeText.Icon(c.Class, 18, c.Name); icon.HorizontalAlignment = HorizontalAlignment.Center; icon.VerticalAlignment = VerticalAlignment.Center;
        hex.Children.Add(icon);
        head.Children.Add(hex);
        var who = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var name = Tt(c.Name, 17, c.You ? Amber : Frozen(c.NameColor), Face, FontWeights.SemiBold); name.TextTrimming = TextTrimming.CharacterEllipsis;
        who.Children.Add(name);
        double cpW = 0;
        if (c.Cp.Length > 0)
        {
            var chip = new Grid { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 1, 0, 0) };
            chip.Children.Add(new TacSketch((dc, s) => dc.DrawGeometry(Frozen(Tac.A(DeepC, .7)), Tac.P(Frozen(Tac.A(LiteC, .6)), 1), Tac.Cut(new Rect(.5, .5, s.Width - 1, s.Height - 1), 4))));
            var cp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 1, 7, 1) };
            var cw = Tt(L.T("CP"), 8.5, LiteDim, Small, FontWeights.Bold); cw.Margin = new Thickness(0, 1, 4, 0); cp.Children.Add(cw);
            cp.Children.Add(Tt(c.Cp, 12, Pale, Face, FontWeights.SemiBold));
            chip.Children.Add(cp);
            chip.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            cpW = chip.DesiredSize.Width;
            who.Children.Add(chip);
        }
        Grid.SetColumn(who, 1); head.Children.Add(who);
        var cls = Tt(c.ClassName.ToUpperInvariant(), 9.5, Lite, Small, FontWeights.SemiBold); cls.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(cls, 2); head.Children.Add(cls);
        cls.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        name.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        // the card widens for a long name (to 64 more) before the name is cut
        double need = 39 + name.DesiredSize.Width + cpW + cls.DesiredSize.Width + 4 + 2 * In;
        w = Math.Max(w, Math.Min(w + 64, need));
        double inner = w - 2 * In;
        stack.Width = inner;
        name.MaxWidth = Math.Max(40, inner - 39 - cpW - cls.DesiredSize.Width - 4);
        stack.Children.Add(head);
        stack.Children.Add(Hairline(new Thickness(0, 0, 0, 7)));

        // the readout: DPS | SHARE | DAMAGE between glowing dividers, the word small over the figure (right-aligned)
        var read = new Grid();
        read.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.15, GridUnitType.Star) });
        read.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1) });
        read.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        read.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1) });
        read.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        void Part(int col, string word, string fig, Brush fg, double size)
        {
            // the word at the top, the figure at the foot: the three figures share one baseline whatever their size
            var sp = new Grid { Height = 40, Margin = new Thickness(col == 0 ? 0 : 10, 0, col == 4 ? 0 : 10, 0) };
            var lb = Label(word); lb.VerticalAlignment = VerticalAlignment.Top; sp.Children.Add(lb);
            var fv = Tt(fig, size, fg, Face, FontWeights.Bold); fv.HorizontalAlignment = HorizontalAlignment.Right; fv.VerticalAlignment = VerticalAlignment.Bottom;
            fv.Margin = new Thickness(0, 0, 0, -(size * .21) + 2);
            sp.Children.Add(fv);
            Grid.SetColumn(sp, col); read.Children.Add(sp);
        }
        foreach (int col in new[] { 1, 3 })
        {
            var d = new TacSketch((dc, s) => dc.DrawRectangle(Tac.Grad(true, (Tac.A(CyanC, 0), 0), (Tac.A(CyanC, .6), .5), (Tac.A(CyanC, 0), 1)), null, new Rect(0, 0, 1, s.Height)));
            Grid.SetColumn(d, col); read.Children.Add(d);
        }
        Part(0, L.T("DPS"), c.Dps.Length > 0 ? c.Dps : "–", White, 22);
        Part(2, L.T("SHARE"), c.Share, Pale, 17);
        Part(4, L.T("DAMAGE"), c.Damage, Pale, 17);
        stack.Children.Add(read);

        // ◇ CRIT 34%  ◇ FROM CRITS 41%  ◇ FROM BEHIND 12% (the whole sentence on hover)
        var rates = Tac.Rates(c);
        if (rates.Count > 0)
        {
            var rl = new TextBlock { FontFamily = Small, FontSize = 9.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 6, 0, 0), ToolTip = c.RatesLine, TextTrimming = TextTrimming.CharacterEllipsis };
            System.Windows.Documents.Typography.SetNumeralAlignment(rl, FontNumeralAlignment.Tabular);
            foreach (var (word, fig) in rates)
            {
                rl.Inlines.Add(new System.Windows.Documents.Run((rl.Inlines.Count > 0 ? "   ◇ " : "◇ ")) { Foreground = Cyan, FontSize = 8 });
                rl.Inlines.Add(new System.Windows.Documents.Run(word.ToUpperInvariant() + " ") { Foreground = LiteDim });
                rl.Inlines.Add(new System.Windows.Documents.Run(fig) { Foreground = Amber, FontFamily = Face, FontSize = 12.5, FontWeight = FontWeights.Bold });
            }
            stack.Children.Add(rl);
        }

        // the abilities: SKILL · SHARE · DMG (· CRIT · BACK), each over a glowing line bar
        if (c.Abilities.Count > 0)
        {
            stack.Children.Add(Hairline(new Thickness(0, 8, 0, 5)));
            var hd = Tac.CardGrid(c, 46, 54, 13);
            TextBlock H(string s, bool right = true) { var t = Label(s); t.FontSize = 8.5; if (right) t.HorizontalAlignment = HorizontalAlignment.Right; return t; }
            hd.Children.Add(H(L.T("SKILL"), false));
            Tac.Cell(hd, H(L.T("SHARE")), 1, c); Tac.Cell(hd, H(L.T("DMG")), 2, c);
            if (c.CritCol) Tac.Cell(hd, H(L.T("CRIT")), 3, c);
            if (c.BackCol) Tac.Cell(hd, H(L.T("BACK")), 4, c);
            stack.Children.Add(hd);
            int i = 0;
            foreach (var a in c.Abilities)
            {
                int k = i++;
                var row = Tac.CardGrid(c, 46, 54, 25);
                row.Background = Brushes.Transparent;
                row.ToolTip = Tac.AbilityTip(a);
                var bar = new TacSketch((dc, s) =>
                {
                    double bw = s.Width - 8, by = s.Height - 6.5;
                    dc.DrawLine(Tac.P(Frozen(Tac.A(CyanC, .22)), 1, dash: new DashStyle(new double[] { 2, 2 }, 0)), new Point(0, by), new Point(bw, by));
                    double g = Out(Tac.Clamp01(grow * 1.6 - k * .12));
                    double fx = Math.Max(3, bw * a.OfTop) * g;
                    if (fx <= .5) return;
                    dc.DrawRectangle(Frozen(Tac.A(tone, .22)), null, new Rect(0, by - 3, fx, 6));
                    dc.DrawRectangle(Frozen(Tac.A(tone, .6)), null, new Rect(0, by - 1.5, fx, 3));
                    dc.DrawRectangle(Frozen(Tac.Mix(tone, Colors.White, .45)), null, new Rect(0, by - .75, fx, 1.5));
                    dc.DrawRectangle(Tac.Grad(false, (Tac.A(Colors.White, 0), 0), (Tac.A(Colors.White, .95), 1)), null, new Rect(Math.Max(0, fx - 10), by - 1, Math.Min(10, fx), 2));
                });
                bars.Add(bar);
                row.Children.Add(bar);
                var an = Tac.AbilityName(a, 12.5, White, Face, FontWeights.SemiBold, Amber, 10.5);
                an.Margin = new Thickness(1, 0, 8, 9);
                row.Children.Add(an);
                TextBlock F(string s, Brush fg, double size, FontWeight wt) { var t = Tt(s, size, fg, Face, wt); t.HorizontalAlignment = HorizontalAlignment.Right; t.Margin = new Thickness(0, 0, 0, 9); return t; }
                Tac.Cell(row, F(a.ShareText, Lite, 11.5, FontWeights.SemiBold), 1, c);
                Tac.Cell(row, F(a.Damage, White, 12.5, FontWeights.Bold), 2, c);
                if (c.CritCol) Tac.Cell(row, F(a.Crit, a.Crit == "–" ? LiteDim : Amber, 11.5, FontWeights.Bold), 3, c);
                if (c.BackCol) Tac.Cell(row, F(a.Back, a.Back == "–" ? LiteDim : Frozen(Tac.A(AmberC, .78)), 11.5, FontWeights.SemiBold), 4, c);
                stack.Children.Add(row);
            }
            if (c.MoreLine.Length > 0)
            {
                var more = Tt("◇ " + c.MoreLine, 11, LiteDim, Face, FontWeights.SemiBold); more.Margin = new Thickness(0, 2, 0, 0);
                stack.Children.Add(more);
            }
        }

        // the foot: hits · biggest · healed (green) · deaths (coral)
        if (c.FootLine.Length > 0)
        {
            stack.Children.Add(Hairline(new Thickness(0, 7, 0, 5)));
            var foot = new TextBlock { FontFamily = Face, FontSize = 11.5, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
            System.Windows.Documents.Typography.SetNumeralAlignment(foot, FontNumeralAlignment.Tabular);
            foreach (var (s, fg) in new[] { (c.Hits, Lite), (c.Biggest, Lite), (c.Healed, Green), (c.DeathsText, Coral) })
            {
                if (s.Length == 0) continue;
                if (foot.Inlines.Count > 0) foot.Inlines.Add(new System.Windows.Documents.Run("  ◇  ") { Foreground = Frozen(Tac.A(CyanC, .6)), FontSize = 8 });
                foot.Inlines.Add(new System.Windows.Documents.Run(s) { Foreground = fg });
            }
            stack.Children.Add(foot);
        }

        // the glass: a soft cyan halo round it (inside the card's room), the look's cut glass, and the projector's scan line
        var glass = new TacSketch((dc, s) =>
        {
            var r = new Rect(Pad + .5, Pad + .5, s.Width - 2 * Pad - 1, s.Height - 2 * Pad - 1);
            if (r.Width <= 0 || r.Height <= 0) return;
            var g = Tac.Cut(r, Cut);
            // the halo round the cut shape (bevel joins: the chamfers stay cut, never squared off by the glow)
            Pen Halo(Color col, double width) { var pn = new Pen(Frozen(col), width) { LineJoin = PenLineJoin.Bevel }; pn.Freeze(); return pn; }
            dc.DrawGeometry(null, Halo(Tac.A(CyanC, .08), 9), g);
            dc.DrawGeometry(null, Halo(Tac.A(CyanC, .12), 5), g);
            dc.DrawGeometry(null, Halo(Tac.A(DeepC, .7), 3), g);
            dc.DrawGeometry(Tac.Grad(true, (Tac.A(Ground2C, .95), 0), (Tac.A(GroundC, .95), .45), (Tac.A(DeepC, .96), 1)), null, g);
            dc.PushClip(g);
            var grid = Frozen(Tac.A(CyanC, .05));
            for (double x = r.X + 8; x < r.Right; x += 12) dc.DrawRectangle(grid, null, new Rect(x, r.Y, 1, r.Height));
            for (double y = r.Y + 8; y < r.Bottom; y += 12) dc.DrawRectangle(grid, null, new Rect(r.X, y, r.Width, 1));
            dc.DrawRectangle(Tac.Grad(true, (Tac.A(CyanC, .16), 0), (Tac.A(CyanC, 0), 1)), null, new Rect(r.X, r.Y, r.Width, 22));
            dc.Pop();
            dc.DrawGeometry(null, Tac.P(Frozen(Tac.A(CyanC, .6)), 1), g);
            var lit = new StreamGeometry();
            using (var x = lit.Open())
            {
                x.BeginFigure(new Point(r.Left, r.Top + Cut + 12), false, false); x.LineTo(new Point(r.Left, r.Top + Cut), true, false);
                x.LineTo(new Point(r.Left + Cut, r.Top), true, false); x.LineTo(new Point(r.Left + Cut + 22, r.Top), true, false);
                x.BeginFigure(new Point(r.Right, r.Bottom - Cut - 12), false, false); x.LineTo(new Point(r.Right, r.Bottom - Cut), true, false);
                x.LineTo(new Point(r.Right - Cut, r.Bottom), true, false); x.LineTo(new Point(r.Right - Cut - 22, r.Bottom), true, false);
            }
            lit.Freeze();
            dc.DrawGeometry(null, Tac.P(Frozen(Tac.A(CyanC, .35)), 4), lit);
            dc.DrawGeometry(null, Tac.P(Cyan, 1.6), lit);
        });
        var line = new TacSketch((dc, s) =>
        {
            if (scan is < 0 or >= 1) return;
            double y = Pad + (s.Height - 2 * Pad) * scan;
            dc.DrawRectangle(Tac.Grad(true, (Tac.A(CyanC, 0), 0), (Tac.A(CyanC, .3), 1)), null, new Rect(Pad, y - 16, s.Width - 2 * Pad, 16));
            dc.DrawRectangle(Frozen(Tac.A(PaleC, .9)), null, new Rect(Pad - 2, y - 1, s.Width - 2 * Pad + 4, 2));
        });
        var reveal = new RectangleGeometry(new Rect(0, 0, 4000, 4000));
        var inner0 = new Grid { Children = { glass, new Border { Margin = new Thickness(Pad), Child = stack } } };
        var card = new Grid { Width = w + 2 * Pad, Children = { inner0, line } };
        System.Windows.Documents.TextElement.SetFontFamily(card, Face);
        TextOptions.SetTextFormattingMode(card, TextFormattingMode.Display);
        Tac.CardIn(card, 240, p =>
        {
            double h = card.ActualHeight > 0 ? card.ActualHeight : 400;
            scan = p >= 1 ? -1 : Out(Tac.Clamp01(p / .7));
            if (p >= 1) inner0.ClearValue(UIElement.ClipProperty);
            else { reveal.Rect = new Rect(0, 0, 4000, Pad + (h - 2 * Pad) * Math.Max(0, scan) + 1); inner0.Clip = reveal; }
            grow = p;
            line.InvalidateVisual();
            foreach (var b in bars) b.InvalidateVisual();
        });
        return card;
    }
}
