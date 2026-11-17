using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

sealed partial class HudFighter : IThemeCard
{
    /// <summary>
    /// A member's skills card as the jet's target-data page (an MFD): dark glass with fine phosphor scan lines inside corner
    /// brackets, a TGT DATA strip with the class, the callsign in its class's colour (amber for you) and its Combat Power, a
    /// readout of DPS, share and damage in bracketed cells, the rates in amber, then the abilities as a target list (each one's
    /// share of the top ability a vector line at its foot with a bright head), the rest and the foot under a dashed rule.
    /// Entrance: the brackets lock on from 6 px out and the lines run out, one after another (260 ms); settled in a render.
    /// </summary>
    public FrameworkElement DrawCard(MemberCard c)
    {
        const double Pad = 12, In = 11;
        double w = 262 + (c.CritCol ? Tac.CardRate : 0) + (c.BackCol ? Tac.CardRate : 0), inner = w - 2 * In;
        var tone = c.ClassColor;
        var nameBrush = c.You ? Amber : Frozen(c.NameColor);
        double spread = 0, grow = 1;
        var lines = new List<TacSketch>();

        var stack = new StackPanel { Width = inner, Margin = new Thickness(In, 8, In, 9) };

        // TGT DATA ---------------------------------- GLADIATOR
        var strip = new Grid { Margin = new Thickness(0, 0, 0, 5) };
        strip.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); strip.ColumnDefinitions.Add(new ColumnDefinition()); strip.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        strip.Children.Add(Tag(L.T("TGT DATA"), Phos, Dark, 9));
        var dash = new TacSketch((dc, s) => dc.DrawLine(Tac.P(PhosFaint, 1, dash: new DashStyle(new double[] { 2, 3 }, 0)), new Point(0, Math.Round(s.Height / 2) + .5), new Point(s.Width, Math.Round(s.Height / 2) + .5))) { Margin = new Thickness(7, 0, 7, 0) };
        Grid.SetColumn(dash, 1); strip.Children.Add(dash);
        var cls = Tt(c.Class.ToUpperInvariant(), 9.5, PhosDim, Face, FontWeights.Bold);
        Grid.SetColumn(cls, 2); strip.Children.Add(cls);
        stack.Children.Add(strip);

        // ◆ Swoly  CP 4.8K: the name gives way to the CP, never the other way
        var who = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 7) };
        who.Children.Add(new TacSketch((dc, s) =>
        {
            double cy = s.Height / 2;
            var g = new StreamGeometry();
            using (var x = g.Open()) { x.BeginFigure(new Point(6, cy - 6), true, true); x.LineTo(new Point(12, cy), true, false); x.LineTo(new Point(6, cy + 6), true, false); x.LineTo(new Point(0, cy), true, false); }
            g.Freeze();
            dc.DrawGeometry(Frozen(tone), Tac.P(Frozen(Tac.A(Colors.White, .45)), .8), g);
        }, 12, 18) { Margin = new Thickness(0, 0, 8, 0) });
        var name = Tt(c.Name, 15, nameBrush, Face, FontWeights.Bold); name.TextTrimming = TextTrimming.CharacterEllipsis;
        who.Children.Add(name);
        double cpW = 0;
        if (c.Cp.Length > 0)
        {
            var cp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(9, 2, 0, 0) };
            var cw = Tt(L.T("CP"), 9, PhosDim, Face, FontWeights.Bold); cw.Margin = new Thickness(0, 1, 4, 0); cp.Children.Add(cw);
            cp.Children.Add(Tt(c.Cp, 12, Frozen(Tac.A(PaleC, .8)), Face, FontWeights.SemiBold));
            cp.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            cpW = cp.DesiredSize.Width + 9;
            who.Children.Add(cp);
        }
        name.MaxWidth = Math.Max(40, inner - 20 - cpW);
        stack.Children.Add(who);

        // the readout: DPS | SHARE | DAMAGE, each cell bracketed, its word small over the figure (right-aligned, tabular)
        var read = new Grid { Height = 40 };
        for (int i = 0; i < 3; i++) read.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(i == 0 ? 1.15 : 1, GridUnitType.Star) });
        void Cell(int col, string word, string fig, Brush fg, double size)
        {
            var g = new Grid { Margin = new Thickness(col == 0 ? 0 : 4, 0, col == 2 ? 0 : 4, 0) };
            g.Children.Add(new TacSketch((dc, s) =>
            {
                dc.DrawRectangle(Frozen(Tac.A(PhosC, .05)), null, new Rect(0, 0, s.Width, s.Height));
                var b = Tac.P(PhosFaint, 1);
                dc.DrawLine(b, new Point(.5, 0), new Point(.5, s.Height)); dc.DrawLine(b, new Point(.5, .5), new Point(5, .5)); dc.DrawLine(b, new Point(.5, s.Height - .5), new Point(5, s.Height - .5));
                dc.DrawLine(b, new Point(s.Width - .5, 0), new Point(s.Width - .5, s.Height)); dc.DrawLine(b, new Point(s.Width - .5, .5), new Point(s.Width - 5, .5)); dc.DrawLine(b, new Point(s.Width - .5, s.Height - .5), new Point(s.Width - 5, s.Height - .5));
            }));
            var wd = Tt(word, 9.5, Frozen(Tac.A(PhosC, .85)), Face, FontWeights.Bold); wd.VerticalAlignment = VerticalAlignment.Top; wd.Margin = new Thickness(5, 3, 0, 0);
            g.Children.Add(wd);
            var fv = Tt(fig, size, fg, Face, FontWeights.Bold); fv.VerticalAlignment = VerticalAlignment.Bottom; fv.HorizontalAlignment = HorizontalAlignment.Right; fv.Margin = new Thickness(0, 0, 6, 2);
            g.Children.Add(fv);
            Grid.SetColumn(g, col); read.Children.Add(g);
        }
        Cell(0, L.T("DPS"), c.Dps.Length > 0 ? c.Dps : "–", c.You ? Amber : Pale, 19);
        Cell(1, L.T("SHARE"), c.Share, Phos, 15);
        Cell(2, L.T("DAMAGE"), c.Damage, Pale, 15);
        stack.Children.Add(read);

        // CRIT 34% · FROM CRITS 41% · FROM BEHIND 12% (the whole sentence on hover)
        var rates = Tac.Rates(c);
        if (rates.Count > 0)
        {
            var rl = new TextBlock { FontFamily = Face, FontSize = 9.5, FontWeight = FontWeights.Bold, Margin = new Thickness(1, 6, 0, 0), ToolTip = c.RatesLine, TextTrimming = TextTrimming.CharacterEllipsis };
            System.Windows.Documents.Typography.SetNumeralAlignment(rl, FontNumeralAlignment.Tabular);
            foreach (var (word, fig) in rates)
            {
                if (rl.Inlines.Count > 0) rl.Inlines.Add(new System.Windows.Documents.Run("  ·  ") { Foreground = PhosFaint });
                rl.Inlines.Add(new System.Windows.Documents.Run(word.ToUpperInvariant() + " ") { Foreground = PhosDim });
                rl.Inlines.Add(new System.Windows.Documents.Run(fig) { Foreground = Amber, FontSize = 11 });
            }
            stack.Children.Add(rl);
        }

        // the target list: SKILL · SHARE · DMG (· CRIT · BACK), a dashed rule, one line each
        if (c.Abilities.Count > 0)
        {
            var hd = Tac.CardGrid(c, 44, 52, 14); hd.Margin = new Thickness(0, 8, 0, 0);
            TextBlock H(string s, bool right = true) { var t = Tt(s, 8.5, PhosDim, Face, FontWeights.Bold); if (right) { t.HorizontalAlignment = HorizontalAlignment.Right; t.Margin = new Thickness(0, 0, 2, 0); } return t; }
            hd.Children.Add(H(L.T("SKILL"), false));
            Tac.Cell(hd, H(L.T("SHARE")), 1, c); Tac.Cell(hd, H(L.T("DMG")), 2, c);
            if (c.CritCol) Tac.Cell(hd, H(L.T("CRIT")), 3, c);
            if (c.BackCol) Tac.Cell(hd, H(L.T("BACK")), 4, c);
            stack.Children.Add(hd);
            stack.Children.Add(new TacSketch((dc, s) => dc.DrawLine(Tac.P(PhosFaint, 1, dash: new DashStyle(new double[] { 2, 2 }, 0)), new Point(0, .5), new Point(s.Width, .5))) { Height = 1, Margin = new Thickness(0, 2, 0, 3) });
            var light = Tac.Mix(tone, Colors.White, .25);
            int i = 0;
            foreach (var a in c.Abilities)
            {
                int k = i++;
                var row = Tac.CardGrid(c, 44, 52, 21);
                row.Background = Brushes.Transparent;   // the hover line over the whole row
                row.ToolTip = Tac.AbilityTip(a);
                // its share of the top ability: a vector line at the foot of the name's cell, a tick each quarter, a bright head
                var bar = new TacSketch((dc, s) =>
                {
                    double y = s.Height - 3, bw = s.Width - 6;
                    dc.DrawRectangle(Frozen(Tac.A(PhosC, .16)), null, new Rect(0, y, bw, 1));
                    for (int q = 0; q <= 4; q++) dc.DrawRectangle(PhosFaint, null, new Rect(Math.Round(bw * q / 4), y - 2, 1, 3));
                    double g = Out(Tac.Clamp01(grow * 1.6 - k * .12));
                    double fx = Math.Max(2, bw * a.OfTop) * g;
                    if (fx <= .5) return;
                    dc.DrawRectangle(Frozen(Tac.A(tone, .35)), null, new Rect(0, y - 1.5, fx, 4));
                    dc.DrawRectangle(Frozen(light), null, new Rect(0, y - .5, fx, 2));
                    dc.DrawRectangle(Frozen(Colors.White), null, new Rect(fx - 1.2, y - 3, 1.4, 6));
                });
                lines.Add(bar);
                row.Children.Add(bar);
                var an = Tac.AbilityName(a, 11, Pale, Face, FontWeights.SemiBold, Amber, 9.5);
                an.Margin = new Thickness(0, 0, 8, 4);
                row.Children.Add(an);
                TextBlock F(string s, Brush fg, double size) { var t = Tt(s, size, fg, Face, FontWeights.Bold); t.HorizontalAlignment = HorizontalAlignment.Right; t.Margin = new Thickness(0, 0, 2, 4); return t; }
                Tac.Cell(row, F(a.ShareText, PhosDim, 10.5), 1, c);
                Tac.Cell(row, F(a.Damage, Pale, 11.5), 2, c);
                if (c.CritCol) Tac.Cell(row, F(a.Crit, a.Crit == "–" ? PhosFaint : Amber, 10.5), 3, c);
                if (c.BackCol) Tac.Cell(row, F(a.Back, a.Back == "–" ? PhosFaint : Frozen(Tac.A(AmberC, .78)), 10.5), 4, c);
                stack.Children.Add(row);
            }
            if (c.MoreLine.Length > 0)
            {
                var more = Tt("+ " + c.MoreLine.ToUpperInvariant(), 9.5, PhosDim, Face, FontWeights.Bold); more.Margin = new Thickness(0, 2, 0, 0);
                stack.Children.Add(more);
            }
        }

        // the foot: hits · biggest · healed (green) · deaths (red), after a dashed rule
        if (c.FootLine.Length > 0)
        {
            stack.Children.Add(new TacSketch((dc, s) => dc.DrawLine(Tac.P(PhosDim, 1, dash: new DashStyle(new double[] { 3, 3 }, 0)), new Point(0, .5), new Point(s.Width, .5))) { Height = 1, Margin = new Thickness(0, 7, 0, 5) });
            var foot = new TextBlock { FontFamily = Face, FontSize = 10, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
            System.Windows.Documents.Typography.SetNumeralAlignment(foot, FontNumeralAlignment.Tabular);
            foreach (var (s, fg) in new[] { (c.Hits, PhosDim), (c.Biggest, PhosDim), (c.Healed, Frozen(Color.FromRgb(0x5E, 0xF0, 0xA0))), (c.DeathsText, Red) })
            {
                if (s.Length == 0) continue;
                if (foot.Inlines.Count > 0) foot.Inlines.Add(new System.Windows.Documents.Run("  ·  ") { Foreground = PhosFaint });
                foot.Inlines.Add(new System.Windows.Documents.Run(s) { Foreground = fg });
            }
            stack.Children.Add(foot);
        }

        // the glass, its scan lines and the brackets (out by spread while it locks on); a dark ground round it for a bright scene
        var glass = new TacSketch((dc, s) =>
        {
            var r = new Rect(Pad, Pad, s.Width - 2 * Pad, s.Height - 2 * Pad);
            if (r.Width <= 0 || r.Height <= 0) return;
            dc.DrawRectangle(Frozen(Tac.A(InkC0, .35)), null, new Rect(r.X - 3, r.Y - 2, r.Width + 6, r.Height + 6));
            dc.DrawRectangle(Tac.Grad(true, (Tac.A(InkC0, .93), 0), (Tac.A(InkC0, .95), 1)), Tac.P(Frozen(Tac.A(PhosC, .24)), 1), new Rect(r.X + .5, r.Y + .5, r.Width - 1, r.Height - 1));
            var line = Frozen(Tac.A(PhosC, .04));
            for (double y = r.Top + 2; y < r.Bottom; y += 3) dc.DrawRectangle(line, null, new Rect(r.Left + 1, y, r.Width - 2, 1));
            dc.DrawRectangle(Frozen(Tac.A(PhosC, .3)), null, new Rect(r.Left + 1, r.Top + 1, r.Width - 2, 1));
            dc.PushTransform(new TranslateTransform(r.X, r.Y));
            Brackets(dc, r.Size, spread, spread > 0 ? .5 : 0, 11);
            dc.Pop();
        });
        var card = new Grid { Width = w + 2 * Pad };
        card.Children.Add(glass);
        var body = new Border { Margin = new Thickness(Pad), Child = stack };
        card.Children.Add(body);
        System.Windows.Documents.TextElement.SetFontFamily(card, Face);
        TextOptions.SetTextFormattingMode(card, TextFormattingMode.Display);
        Tac.CardIn(card, 260, p =>
        {
            double k = Tac.Clamp01(p / .5);
            spread = 6 * (1 - k * k * (3 - 2 * k));
            grow = p;
            body.Opacity = p <= 0 ? 0 : Math.Min(1, .35 + p * 3);
            glass.InvalidateVisual();
            foreach (var l in lines) l.InvalidateVisual();
        });
        return card;
    }
}
