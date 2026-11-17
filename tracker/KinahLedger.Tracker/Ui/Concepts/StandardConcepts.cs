using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Effects;
using KinahLedger.Parser;

namespace KinahLedger.Ui;

/// <summary>
/// Standard's three concepts (render only; Steven, 2026-11-17: "clear, legible and fucking incredible cinematic production
/// quality"). Each keeps the Parser's own panel: its dark glass, the gold accent, class-coloured bars and Inter.
/// A "Forge": every row is one thick lit bar, the words on it. B "Podium": the leader on a hero card, each row a name line
/// over a long glowing rail. C "Reactor": recessed tracks lit cell by cell, the figures in inset readouts.
/// </summary>
public static partial class MeterConcepts
{
    static partial void AddStandard(List<Concept> l)
    {
        l.Add(new Concept(ParserPrefs.StandardLook, "A", "Forge", (e, now, w) => StdA.Make(e, now, w)));
        l.Add(new Concept(ParserPrefs.StandardLook, "B", "Podium", (e, now, w) => StdB.Make(e, now, w)));
        l.Add(new Concept(ParserPrefs.StandardLook, "C", "Reactor", (e, now, w) => StdC.Make(e, now, w)));
    }

    /// <summary>What the three concepts draw from: the rows as the meter ranks them, the top figure (bars are measured on it), the members left out.</summary>
    sealed record StdView(Encounter E, DateTime Now, List<MeterMember> Rows, double Top, int More)
    {
        public static StdView Of(Encounter e, DateTime now)
        {
            var rows = ThemeRows.Of(e, now, false);
            double top = rows.Select(r => double.IsNaN(r.FigureValue) ? 0 : r.FigureValue).DefaultIfEmpty(0).Max();
            int more = Math.Max(0, e.Shown(now).Count(f => !f.Other) - rows.Count);
            return new StdView(e, now, rows, top, more);
        }
        public double Fill(MeterMember m) => Top > 0 && !double.IsNaN(m.FigureValue) ? Math.Clamp(m.FigureValue / Top, 0.04, 1) : 0;
        public bool Held => !E.Live && !E.Waiting;
    }

    /// <summary>The concepts' kit: type, colour, light.</summary>
    static class SK
    {
        public static readonly Color Gold = Color.FromRgb(0xF5, 0xB9, 0x42), GoldHi = Color.FromRgb(0xFF, 0xD9, 0x7A), Mint = Color.FromRgb(0x3D, 0xD6, 0x8C),
            Coral = Color.FromRgb(0xF2, 0x6D, 0x6D), Ink = Color.FromRgb(0xF4, 0xF5, 0xF9), Panel = Color.FromRgb(0x0B, 0x0C, 0x10), Panel2 = Color.FromRgb(0x16, 0x18, 0x20);

        public static SolidColorBrush B(Color c, double a = 1) { var b = new SolidColorBrush(Color.FromArgb((byte)Math.Round(255 * a), c.R, c.G, c.B)); b.Freeze(); return b; }
        public static Color A(Color c, double a) => Color.FromArgb((byte)Math.Round(255 * a), c.R, c.G, c.B);
        public static Color Mix(Color a, Color b, double t) => Color.FromRgb((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
        /// <summary>A class colour pushed to read as light: brighter and a touch more saturated.</summary>
        public static Color Lit(Color c) => Mix(c, Colors.White, 0.28);
        public static Color Deep(Color c) => Mix(c, Panel, 0.45);

        public static LinearGradientBrush H(params (Color C, double At)[] stops) => Grad(new Point(0, 0), new Point(1, 0), stops);
        public static LinearGradientBrush V(params (Color C, double At)[] stops) => Grad(new Point(0, 0), new Point(0, 1), stops);
        static LinearGradientBrush Grad(Point a, Point b, (Color C, double At)[] stops)
        {
            var g = new LinearGradientBrush { StartPoint = a, EndPoint = b };
            foreach (var (c, at) in stops) g.GradientStops.Add(new GradientStop(c, at));
            g.Freeze();
            return g;
        }

        public static TextBlock T(string s, double size, FontWeight w, Color c, double a = 1)
        {
            var t = new TextBlock { Text = s, FontSize = size, FontWeight = w, Foreground = B(c, a), FontFamily = HudKit.Text, VerticalAlignment = VerticalAlignment.Center };
            Typography.SetNumeralAlignment(t, FontNumeralAlignment.Tabular);
            return t;
        }
        /// <summary>Words lifted off whatever is under them: a tight dark shadow straight down.</summary>
        public static T2 Lift<T2>(T2 el, double blur = 4, double opacity = 0.9) where T2 : UIElement
        {
            el.Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = blur, ShadowDepth = 1, Direction = 270, Opacity = opacity };
            return el;
        }
        public static DropShadowEffect Glow(Color c, double blur, double opacity) => new() { Color = c, BlurRadius = blur, ShadowDepth = 0, Opacity = opacity };

        /// <summary>The width a text takes in Inter at a size and weight (the figure columns are sized on the widest form).</summary>
        public static double W(string s, double size, FontWeight w) =>
            Math.Ceiling(new FormattedText(s, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface(HudKit.Text, FontStyles.Normal, w, FontStretches.Normal), size, Brushes.Black, 1.25).WidthIncludingTrailingWhitespace) + 1;
        /// <summary>The widest a figure column ever holds at this size: "888.8K" (DPS) or "88.8%" (share), or the figure shown if wider.</summary>
        public static double Col(IEnumerable<string> shown, string widest, double size, FontWeight w) => shown.Append(widest).Max(s => W(s, size, w));

        public static ColumnDefinition Star() => new() { Width = new GridLength(1, GridUnitType.Star) };
        public static ColumnDefinition Auto() => new() { Width = GridLength.Auto };
        public static ColumnDefinition Px(double w) => new() { Width = new GridLength(w) };
        public static T2 At<T2>(T2 el, int col, int row = 0, int span = 1) where T2 : UIElement { Grid.SetColumn(el, col); Grid.SetRow(el, row); if (span > 1) Grid.SetColumnSpan(el, span); return el; }

        /// <summary>A small mark: TOP DPS in gold, TOP HEALS in mint, a death in coral ("✕2").</summary>
        public static Border Mark(string text, Color c, bool solid, double size = 8)
        {
            var t = T(text, size, FontWeights.Black, solid ? Color.FromRgb(0x14, 0x10, 0x04) : c);
            return new Border
            {
                Child = t, CornerRadius = new CornerRadius(3), Padding = new Thickness(4, 0.5, 4, 1), Margin = new Thickness(5, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
                Background = solid ? V((Lit(c), 0), (c, 1)) : B(c, 0.16), BorderBrush = B(c, solid ? 0.9 : 0.6), BorderThickness = new Thickness(solid ? 0 : 1),
                Effect = solid ? Glow(c, 8, 0.55) : null,
            };
        }
        public static IEnumerable<Border> Marks(MeterMember m, double size = 8)
        {
            if (m.Deaths > 0) yield return Mark("✕" + m.Deaths.ToString(CultureInfo.InvariantCulture), Coral, true, size);
            if (m.TopDps) yield return Mark(L.T("TOP DPS"), Gold, true, size);
            if (m.TopHeals) yield return Mark(L.T("TOP HEALS"), Mint, true, size);
        }

        /// <summary>"Total 766.26M · Group 999K": each label quiet and small, each figure bright, the held average badged.</summary>
        public static FrameworkElement Foot(StdView v, double labelSize, double figSize, Color label, Color fig, bool outcome = false)
        {
            var e = v.E;
            var g = new Grid();
            g.ColumnDefinitions.Add(Auto()); g.ColumnDefinitions.Add(Star()); g.ColumnDefinitions.Add(Auto());
            if (!e.HasDamage) return g;
            StackPanel Pair(string l, string f, Color fc)
            {
                var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                var lt = T(l, labelSize, FontWeights.SemiBold, label); lt.Margin = new Thickness(0, 0, 6, 0);
                sp.Children.Add(lt); sp.Children.Add(T(f, figSize, FontWeights.Bold, fc));
                return sp;
            }
            var left = new StackPanel { Orientation = Orientation.Horizontal };
            if (v.Held && outcome)
            {
                var (text, ink) = ParserPanel.OutcomeOf(e, v.Now);
                var o = T(text, figSize - 0.5, FontWeights.Bold, ink.Color); o.Margin = new Thickness(0, 0, 12, 0);
                left.Children.Add(o);
            }
            left.Children.Add(Pair(L.T("Total"), Amounts.Meter(e.Credited), fig));
            g.Children.Add(At(left, 0));
            var right = Pair(v.Held ? L.T("Group") : L.T("Group DPS"), e.GroupDpsText(e.End ?? v.Now), Gold);
            if (v.Held) { var badge = Mark(L.T(Encounter.AvgBadge), Mint, false, 8); right.Children.Add(badge); }
            g.Children.Add(At(right, 2));
            return g;
        }

        /// <summary>The line between the party and you below it: "1 more", faint, with hairlines either side.</summary>
        public static FrameworkElement More(int n, Color c, double size = 9.5)
        {
            var g = new Grid { Height = 15, Margin = new Thickness(0, 1, 0, 1) };
            g.ColumnDefinitions.Add(Star()); g.ColumnDefinitions.Add(Auto()); g.ColumnDefinitions.Add(Star());
            g.Children.Add(At(new Border { Height = 1, Background = H((A(c, 0), 0), (A(c, 0.35), 1)), VerticalAlignment = VerticalAlignment.Center }, 0));
            var t = T(L.F("{0} more", n), size, FontWeights.SemiBold, c, 0.75); t.Margin = new Thickness(8, 0, 8, 1);
            g.Children.Add(At(t, 1));
            g.Children.Add(At(new Border { Height = 1, Background = H((A(c, 0.35), 0), (A(c, 0), 1)), VerticalAlignment = VerticalAlignment.Center }, 2));
            return g;
        }

        /// <summary>The panel: dark glass lit from above (a cool sheen at the top, a hairline highlight on its top edge), a soft shadow under it.</summary>
        public static Border Glass(UIElement body, double width, double radius, Thickness pad, Color edge, double edgeA = 0.10)
        {
            var sheen = new Border { CornerRadius = new CornerRadius(radius), IsHitTestVisible = false, Background = V((Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF), 0), (Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 0.35)) };
            var rim = new Border { CornerRadius = new CornerRadius(radius), BorderThickness = new Thickness(1), IsHitTestVisible = false,
                BorderBrush = V((Color.FromArgb(0x38, 0xFF, 0xFF, 0xFF), 0), (A(edge, edgeA + 0.08), 0.5), (Color.FromArgb(0x50, 0, 0, 0), 1)) };
            var inner = new Grid { Children = { sheen, new Border { Padding = pad, Child = body }, rim } };
            return new Border
            {
                Width = width, CornerRadius = new CornerRadius(radius), Child = inner,
                Background = V((Color.FromArgb(0xF2, 0x15, 0x17, 0x1F), 0), (Color.FromArgb(0xF4, 0x0B, 0x0C, 0x10), 0.55), (Color.FromArgb(0xF6, 0x08, 0x09, 0x0C), 1)),
                Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 22, ShadowDepth = 4, Direction = 270, Opacity = 0.55 },
            };
        }

        public static FrameworkElement Host(FrameworkElement face)
        {
            var host = new Border { Child = face };
            TextElement.SetFontFamily(host, HudKit.Text);
            TextOptions.SetTextFormattingMode(host, TextFormattingMode.Display);
            Typography.SetNumeralAlignment(host, FontNumeralAlignment.Tabular);
            return host;
        }
    }

    // ---------------------------------------------------------------------------------------------------------------------
    // A: Forge. Every row is one thick bar lit from above: a bevel's highlight along its top, its colour deepening down,
    // a hot leading edge. The words sit on the bar: rank, the class's mark, the name, its Combat Power under it; the DPS big at
    // the right, the share small after it.
    // ---------------------------------------------------------------------------------------------------------------------
    static class StdA
    {
        const double RowH = 34, Gap = 4;

        public static FrameworkElement Make(Encounter e, DateTime now, double width)
        {
            var v = StdView.Of(e, now);
            double inner = width + 10 - 24;
            var body = new StackPanel();
            body.Children.Add(Head(v));
            body.Children.Add(Hp(v));
            double dpsW = SK.Col(v.Rows.Select(r => r.Figure), "888.8K", 17, FontWeights.Bold);
            double shareW = SK.Col(v.Rows.Select(r => r.ShareText), "88.8%", 10.5, FontWeights.SemiBold);
            var rows = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
            foreach (var m in v.Rows)
            {
                if (m.Pinned && v.More > 0) rows.Children.Add(SK.More(v.More, Color.FromRgb(0xB1, 0xB7, 0xC8)));
                rows.Children.Add(Row(v, m, inner, dpsW, shareW));
            }
            body.Children.Add(rows);
            if (!e.Waiting)
            {
                var foot = SK.Foot(v, 10.5, 12.5, Color.FromRgb(0x8A, 0x90, 0xA4), SK.Ink);
                foot.Margin = new Thickness(2, 8, 2, 0);
                body.Children.Add(new Border { Height = 1, Margin = new Thickness(0, 6, 0, 0), Background = SK.H((SK.A(SK.Gold, 0), 0), (SK.A(SK.Gold, 0.28), 0.5), (SK.A(SK.Gold, 0), 1)) });
                body.Children.Add(foot);
            }
            return SK.Host(SK.Glass(body, width + 10, 12, new Thickness(12, 11, 12, 11), SK.Gold));
        }

        static FrameworkElement Head(StdView v)
        {
            var e = v.E;
            var g = new Grid();
            g.ColumnDefinitions.Add(SK.Auto()); g.ColumnDefinitions.Add(SK.Star()); g.ColumnDefinitions.Add(SK.Auto());
            var art = GameArt.Element("boss", 24); art.Margin = new Thickness(0, 0, 9, 0); art.Effect = SK.Glow(SK.Gold, 10, 0.35);
            g.Children.Add(SK.At(art, 0));
            var title = SK.Lift(SK.T(e.Title, 14.5, FontWeights.Bold, SK.Ink), 3, 0.7); title.TextTrimming = TextTrimming.CharacterEllipsis;
            g.Children.Add(SK.At(title, 1));
            var clock = SK.T(e.Waiting ? L.T("Ready") : BossParser.Clock(e.Duration(v.Now)), 20, FontWeights.Bold, e.Live ? SK.Gold : SK.Ink);
            clock.Margin = new Thickness(10, -2, 0, 0);
            if (e.Live) clock.Effect = SK.Glow(SK.Gold, 12, 0.55);
            g.Children.Add(SK.At(clock, 2));
            return g;
        }

        /// <summary>The boss's HP: a lit gold rail with ticks at the quarters (coral under a third); a kill's full mint rail.</summary>
        static FrameworkElement Hp(StdView v)
        {
            var e = v.E;
            var g = new Grid { Margin = new Thickness(0, 9, 0, 0) };
            g.ColumnDefinitions.Add(SK.Star()); g.ColumnDefinitions.Add(SK.Auto());
            double left = e.BossHpLeft ?? (e.Outcome == "kill" ? 0 : 1);
            bool live = e.Live && e.BossHpLeft != null;
            if (!live && !v.Held) return new Border();
            var c = !live ? SK.Mint : left < 0.34 ? SK.Coral : SK.Gold;
            var track = new Grid { Height = 6, VerticalAlignment = VerticalAlignment.Center };
            track.Children.Add(new Border { CornerRadius = new CornerRadius(3), Background = SK.V((Color.FromArgb(0x60, 0, 0, 0), 0), (Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF), 1)) });
            double share = live ? left : 1;
            var fill = new Border { CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left, Background = SK.V((SK.Lit(c), 0), (c, 0.55), (SK.Deep(c), 1)), Effect = SK.Glow(c, 9, 0.6) };
            track.SizeChanged += (_, _) => fill.Width = Math.Max(4, track.ActualWidth * share);
            track.Children.Add(fill);
            var ticks = new UniformGrid4();
            track.Children.Add(ticks);
            g.Children.Add(track);
            FrameworkElement text = live ? ParserPanel.HpText(e, 11.5, SK.B(left < 0.34 ? SK.Coral : SK.Ink), SK.B(Color.FromRgb(0xB1, 0xB7, 0xC8)))
                : SK.T(ParserPanel.OutcomeOf(e, v.Now).Text, 11.5, FontWeights.Bold, SK.Mint);
            text.Margin = new Thickness(10, 0, 0, 0);
            g.Children.Add(SK.At(text, 1));
            return g;
        }

        static FrameworkElement Row(StdView v, MeterMember m, double width, double dpsW, double shareW)
        {
            var c = m.ClassColor;
            double fill = v.Fill(m);
            var row = new Grid { Height = RowH, Margin = new Thickness(0, 0, 0, Gap) };
            row.ColumnDefinitions.Add(SK.Star()); row.ColumnDefinitions.Add(SK.Px(shareW + 8));
            var root = new Grid();
            width -= shareW + 8;
            // the track: the bar's full length in the class's shade, recessed
            root.Children.Add(new Border { CornerRadius = new CornerRadius(5), Background = SK.V((Color.FromArgb(0x70, 0x02, 0x03, 0x06), 0), (SK.A(c, 0.10), 1)),
                BorderBrush = SK.B(Colors.White, 0.05), BorderThickness = new Thickness(0, 0, 0, 1) });
            // the bar: deep at its root, lit toward its head, a bevel's highlight on top, a shade along its foot, a hot edge at its end
            if (fill > 0)
            {
                var bar = new Grid { HorizontalAlignment = HorizontalAlignment.Left, Width = Math.Max(10, width * fill) };
                bar.Children.Add(new Border { CornerRadius = new CornerRadius(5), Background = SK.H((SK.A(SK.Deep(c), 0.95), 0), (SK.A(c, 0.92), 0.75), (SK.A(SK.Lit(c), 0.95), 1)) });
                bar.Children.Add(new Border { CornerRadius = new CornerRadius(5), Background = SK.V((Color.FromArgb(0x42, 0xFF, 0xFF, 0xFF), 0), (Color.FromArgb(0x10, 0xFF, 0xFF, 0xFF), 0.42), (Color.FromArgb(0x00, 0, 0, 0), 0.55), (Color.FromArgb(0x55, 0, 0, 0), 1)) });
                bar.Children.Add(new Border { CornerRadius = new CornerRadius(5, 5, 0, 0), Height = 1, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(4, 0, 4, 0), Background = SK.B(Colors.White, 0.38) });
                // the hot edge, never through the DPS (a bar that ends under the figure ends soft)
                double end = Math.Max(10, width * fill);
                if (end < width - dpsW - 16 || fill >= 0.999)
                bar.Children.Add(new Border { Width = 3, HorizontalAlignment = HorizontalAlignment.Right, CornerRadius = new CornerRadius(0, 5, 5, 0), Background = SK.B(SK.Mix(c, Colors.White, 0.6)), Effect = SK.Glow(SK.Lit(c), 12, 0.9) });
                root.Children.Add(bar);
            }
            if (m.Pinned || m.You) root.Children.Add(new Border { CornerRadius = new CornerRadius(5), BorderThickness = new Thickness(1.2), BorderBrush = SK.V((SK.GoldHi, 0), (SK.Gold, 1)), Effect = SK.Glow(SK.Gold, 10, 0.5) });
            // the words on it
            var cols = new Grid { Margin = new Thickness(7, 0, 9, 0) };
            cols.ColumnDefinitions.Add(SK.Px(16)); cols.ColumnDefinitions.Add(SK.Px(26)); cols.ColumnDefinitions.Add(SK.Star());
            cols.ColumnDefinitions.Add(SK.Px(dpsW));
            var rank = SK.Lift(SK.T(m.Rank.ToString(CultureInfo.InvariantCulture), 11.5, FontWeights.Black, m.You ? SK.GoldHi : SK.Ink, m.You ? 1 : 0.62), 3, 0.8);
            cols.Children.Add(SK.At(rank, 0));
            var icon = ClassArt.Icon(m.Class, 20, m.Name); icon.Margin = new Thickness(0, 0, 6, 0);
            cols.Children.Add(SK.At(icon, 1));
            // the name over its Combat Power (and a healer's healing): the name keeps its room, the marks after it
            var line = new Grid();
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 0 });
            line.ColumnDefinitions.Add(SK.Auto());
            var name = SK.Lift(SK.T(m.Name, 13.5, m.You ? FontWeights.Bold : FontWeights.SemiBold, SK.Ink));
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            line.Children.Add(SK.At(new Border { Child = name, HorizontalAlignment = HorizontalAlignment.Left }, 0));
            var marks = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            foreach (var mk in SK.Marks(m)) marks.Children.Add(mk);
            line.Children.Add(SK.At(marks, 1));
            var sub = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, -1, 0, 0) };
            if (m.Cp.Length > 0 || m.CpOnly)
            {
                sub.Children.Add(SK.Lift(SK.T(L.T("CP"), 8, FontWeights.Bold, SK.Ink, 0.5), 2, 0.8));
                var cp = SK.Lift(SK.T(m.CpOnly ? m.Figure : m.Cp, 9.5, FontWeights.SemiBold, SK.Ink, 0.72), 2, 0.8); cp.Margin = new Thickness(3, 0, 0, 0);
                sub.Children.Add(cp);
            }
            if (m.Heal.Length > 0)
            {
                var h = SK.Lift(SK.T("+" + m.Heal, 9.5, FontWeights.Bold, Color.FromRgb(0x8C, 0xF5, 0xC0)), 2, 0.9); h.Margin = new Thickness(sub.Children.Count > 0 ? 8 : 0, 0, 0, 0);
                sub.Children.Add(h);
            }
            var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { line } };
            if (sub.Children.Count > 0) stack.Children.Add(sub);
            cols.Children.Add(SK.At(stack, 2));
            if (!m.CpOnly)
            {
                var dps = SK.Lift(SK.T(m.Figure, 17, FontWeights.Bold, SK.Ink), 4, 0.95); dps.HorizontalAlignment = HorizontalAlignment.Right;
                cols.Children.Add(SK.At(dps, 3));
                var share = SK.T(m.ShareText, 10.5, FontWeights.SemiBold, SK.Ink, 0.62); share.HorizontalAlignment = HorizontalAlignment.Right; share.Margin = new Thickness(0, 2, 0, 0);
                row.Children.Add(SK.At(share, 1));
            }
            root.Children.Add(cols);
            row.Children.Add(SK.At(root, 0));
            return row;
        }
    }

    /// <summary>The HP rail's quarter ticks (a faint dark notch at 25, 50 and 75%).</summary>
    sealed class UniformGrid4 : Grid
    {
        public UniformGrid4()
        {
            for (int i = 0; i < 4; i++) ColumnDefinitions.Add(SK.Star());
            for (int i = 0; i < 3; i++) Children.Add(SK.At(new Border { Width = 1, HorizontalAlignment = HorizontalAlignment.Right, Background = SK.B(Colors.Black, 0.55) }, i));
            IsHitTestVisible = false;
        }
    }

    // ---------------------------------------------------------------------------------------------------------------------
    // B: Podium. The leader stands on a hero card (a gold-lit slab, their DPS large); everyone after them reads as a name line
    // over a long glowing rail. The bar runs the row's full width under the words, so the ranking reads as a race.
    // ---------------------------------------------------------------------------------------------------------------------
    static class StdB
    {
        public static FrameworkElement Make(Encounter e, DateTime now, double width)
        {
            var v = StdView.Of(e, now);
            var body = new StackPanel();
            body.Children.Add(Head(v));
            double shareW = SK.Col(v.Rows.Select(r => r.ShareText), "88.8%", 10, FontWeights.SemiBold);
            var rows = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
            for (int i = 0; i < v.Rows.Count; i++)
            {
                var m = v.Rows[i];
                if (m.Pinned && v.More > 0) rows.Children.Add(SK.More(v.More, Color.FromRgb(0xB1, 0xB7, 0xC8)));
                rows.Children.Add(i == 0 && !m.CpOnly ? Hero(v, m, shareW) : Row(v, m, shareW));
            }
            body.Children.Add(rows);
            if (!e.Waiting)
            {
                var foot = SK.Foot(v, 10, 12.5, Color.FromRgb(0x8A, 0x90, 0xA4), SK.Ink);
                foot.Margin = new Thickness(0, 9, 0, 0);
                body.Children.Add(new Border { Height = 1, Margin = new Thickness(0, 4, 0, 0), Background = SK.B(Colors.White, 0.08) });
                body.Children.Add(foot);
            }
            return SK.Host(SK.Glass(body, width + 10, 14, new Thickness(14, 12, 14, 12), SK.Gold));
        }

        /// <summary>The head: the boss in caps over the place; the clock in a lit gold capsule; the HP as a ribbon across under it.</summary>
        static FrameworkElement Head(StdView v)
        {
            var e = v.E;
            var st = new StackPanel();
            var g = new Grid();
            g.ColumnDefinitions.Add(SK.Star()); g.ColumnDefinitions.Add(SK.Auto());
            var title = SK.Lift(SK.T(e.Title, 15, FontWeights.Bold, SK.Ink), 3, 0.7); title.TextTrimming = TextTrimming.CharacterEllipsis;
            var place = SK.T(e.Place.Length > 0 ? e.Place : PK.WhereOf(e), 10, FontWeights.Medium, Color.FromRgb(0x8A, 0x90, 0xA4)); place.TextTrimming = TextTrimming.CharacterEllipsis;
            g.Children.Add(SK.At(new StackPanel { Children = { title, place }, VerticalAlignment = VerticalAlignment.Center }, 0));
            var clockT = SK.T(e.Waiting ? L.T("Ready") : BossParser.Clock(e.Duration(v.Now)), 17, FontWeights.Bold, e.Live ? Color.FromRgb(0x1C, 0x13, 0x02) : SK.Ink);
            var cap = new Border
            {
                CornerRadius = new CornerRadius(9), Padding = new Thickness(10, 1, 10, 2), Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Child = clockT,
                Background = e.Live ? SK.V((SK.GoldHi, 0), (SK.Gold, 0.6), (Color.FromRgb(0xC8, 0x8A, 0x1E), 1)) : SK.B(Colors.White, 0.07),
                BorderBrush = e.Live ? SK.B(Colors.White, 0.35) : SK.B(Colors.White, 0.12), BorderThickness = new Thickness(0, 1, 0, 0),
                Effect = e.Live ? SK.Glow(SK.Gold, 14, 0.5) : null,
            };
            g.Children.Add(SK.At(cap, 1));
            st.Children.Add(g);
            // the ribbon: the HP left, or the result once it's over
            bool live = e.Live && e.BossHpLeft != null;
            if (live || v.Held)
            {
                double left = live ? e.BossHpLeft!.Value : 1;
                var c = !live ? SK.Mint : left < 0.34 ? SK.Coral : SK.Gold;
                var rib = new Grid { Margin = new Thickness(0, 9, 0, 0), Height = 16 };
                rib.Children.Add(new Border { CornerRadius = new CornerRadius(3), Background = SK.B(Colors.Black, 0.45), BorderBrush = SK.B(Colors.White, 0.06), BorderThickness = new Thickness(1) });
                var fill = new Border { CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left, Background = SK.V((SK.A(SK.Lit(c), 0.85), 0), (SK.A(c, 0.55), 0.5), (SK.A(SK.Deep(c), 0.6), 1)) };
                rib.SizeChanged += (_, _) => fill.Width = Math.Max(4, rib.ActualWidth * left);
                rib.Children.Add(fill);
                var words = new Grid { Margin = new Thickness(7, 0, 7, 0) };
                words.ColumnDefinitions.Add(SK.Star()); words.ColumnDefinitions.Add(SK.Auto());
                if (live) words.Children.Add(SK.At(SK.Lift(ParserPanel.HpText(e, 10.5, SK.B(SK.Ink), SK.B(SK.Ink, 0.85)), 3, 0.9), 0));
                else words.Children.Add(SK.At(SK.Lift(SK.T(ParserPanel.OutcomeOf(e, v.Now).Text, 10, FontWeights.Bold, SK.Ink), 3, 0.9), 0));
                rib.Children.Add(words);
                st.Children.Add(rib);
            }
            return st;
        }

        /// <summary>The rail under a row: a recessed groove its full width, the fill in the class's colour with a glowing head.</summary>
        static FrameworkElement Rail(MeterMember m, double fill, double h, double share)
        {
            var c = m.ClassColor;
            var g = new Grid { Height = h, Margin = new Thickness(0, 3, 0, 0) };
            g.Children.Add(new Border { CornerRadius = new CornerRadius(h / 2), Background = SK.V((Color.FromArgb(0x90, 0, 0, 0), 0), (SK.A(c, 0.14), 1)) });
            if (fill > 0)
            {
                var bar = new Grid { HorizontalAlignment = HorizontalAlignment.Left };
                bar.Children.Add(new Border { CornerRadius = new CornerRadius(h / 2), Background = SK.H((SK.A(SK.Deep(c), 0.9), 0), (SK.A(c, 1), 0.7), (SK.Lit(c), 1)), Effect = SK.Glow(c, 10, 0.55) });
                bar.Children.Add(new Border { CornerRadius = new CornerRadius(h / 2), Background = SK.V((Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF), 0), (Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 0.5), (Color.FromArgb(0x40, 0, 0, 0), 1)) });
                g.SizeChanged += (_, _) => bar.Width = Math.Max(h, g.ActualWidth * fill);
                g.Children.Add(bar);
            }
            return g;
        }

        static FrameworkElement NameLine(MeterMember m, double nameSize, string figure, double figSize, double shareW, double cpSize = 10)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(SK.Auto()); g.ColumnDefinitions.Add(SK.Auto());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(SK.Auto()); g.ColumnDefinitions.Add(SK.Px(shareW + 6));
            var rank = SK.T(m.Rank.ToString(CultureInfo.InvariantCulture), 10.5, FontWeights.Black, m.You ? SK.Gold : SK.Ink, m.You ? 1 : 0.5);
            rank.Width = 13;
            g.Children.Add(SK.At(rank, 0));
            var icon = ClassArt.Icon(m.Class, 17, m.Name); icon.Margin = new Thickness(0, 0, 6, 0);
            g.Children.Add(SK.At(icon, 1));
            var who = new Grid();
            who.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); who.ColumnDefinitions.Add(SK.Auto());
            var nameRow = new StackPanel { Orientation = Orientation.Horizontal };
            var name = SK.T(m.Name, nameSize, m.You ? FontWeights.Bold : FontWeights.SemiBold, m.You ? SK.GoldHi : SK.Ink);
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            var nameBox = new Border { Child = name };
            who.Children.Add(SK.At(nameBox, 0));
            var after = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            if (m.Heal.Length > 0) { var h = SK.T("+" + m.Heal, cpSize, FontWeights.Bold, SK.Mint); h.Margin = new Thickness(7, 1, 0, 0); after.Children.Add(h); }
            foreach (var mk in Glyphs(m)) after.Children.Add(mk);
            who.Children.Add(SK.At(after, 1));
            g.Children.Add(SK.At(who, 2));
            var fig = SK.T(figure, figSize, FontWeights.Bold, SK.Ink); fig.HorizontalAlignment = HorizontalAlignment.Right; fig.Margin = new Thickness(8, 0, 0, 0);
            g.Children.Add(SK.At(fig, 3));
            var share = SK.T(m.CpOnly ? m.ShareText : m.ShareText, 10, FontWeights.SemiBold, SK.Ink, 0.55); share.HorizontalAlignment = HorizontalAlignment.Right; share.Margin = new Thickness(0, 1, 0, 0);
            g.Children.Add(SK.At(share, 4));
            return g;
        }

        /// <summary>B's marks: glyphs, not plates (a gold star for TOP DPS, a mint cross for TOP HEALS, a coral skull-cross and count for deaths).</summary>
        static IEnumerable<FrameworkElement> Glyphs(MeterMember m)
        {
            FrameworkElement G(string s, Color c, string tip) { var t = SK.T(s, 11, FontWeights.Black, c); t.Margin = new Thickness(6, 0, 0, 0); t.Effect = SK.Glow(c, 7, 0.6); t.ToolTip = tip; return t; }
            if (m.Deaths > 0) yield return G("✕" + m.Deaths.ToString(CultureInfo.InvariantCulture), SK.Coral, "");
            if (m.TopDps) yield return G("★", SK.Gold, L.T("TOP DPS"));
            if (m.TopHeals) yield return G("✚", SK.Mint, L.T("TOP HEALS"));
        }

        /// <summary>The rail with the member's Combat Power at its end, small and quiet (under the share's column).</summary>
        static FrameworkElement RailLine(MeterMember m, double fill, double h, double shareW, double cpSize)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(SK.Star()); g.ColumnDefinitions.Add(SK.Px(shareW + 40));
            g.Children.Add(Rail(m, fill, h, 0));
            if (m.Cp.Length > 0)
            {
                var cp = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, -2) };
                cp.Children.Add(SK.T(L.T("CP"), cpSize - 2, FontWeights.Bold, Color.FromRgb(0x6A, 0x70, 0x84)));
                var f = SK.T(m.Cp, cpSize, FontWeights.SemiBold, Color.FromRgb(0x9A, 0xA0, 0xB4)); f.Margin = new Thickness(3, 0, 0, 0); cp.Children.Add(f);
                g.Children.Add(SK.At(cp, 1));
            }
            return g;
        }

        static FrameworkElement Row(StdView v, MeterMember m, double shareW)
        {
            var st = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
            st.Children.Add(NameLine(m, 13, m.Figure, 15, shareW));
            st.Children.Add(RailLine(m, v.Fill(m), 7, shareW, 9.5));
            if (m.You)
                return new Border { Child = st, Margin = new Thickness(-7, 0, -7, 2), Padding = new Thickness(7, 5, 7, 0), CornerRadius = new CornerRadius(7),
                    Background = SK.H((SK.A(SK.Gold, 0.13), 0), (SK.A(SK.Gold, 0.03), 1)), BorderBrush = SK.B(SK.Gold, 0.45), BorderThickness = new Thickness(1) };
            return st;
        }

        /// <summary>The leader: a slab lit gold from its left, their DPS large, their rail thicker.</summary>
        static FrameworkElement Hero(StdView v, MeterMember m, double shareW)
        {
            var st = new StackPanel();
            st.Children.Add(NameLine(m, 14, m.Figure, 21, shareW, 10.5));
            st.Children.Add(RailLine(m, v.Fill(m), 10, shareW, 10));
            var card = new Border
            {
                Child = st, Margin = new Thickness(-7, 0, -7, 10), Padding = new Thickness(7, 6, 7, 8), CornerRadius = new CornerRadius(8),
                Background = SK.H((SK.A(m.ClassColor, 0.22), 0), (SK.A(m.ClassColor, 0.05), 0.7), (Color.FromArgb(0x10, 0xFF, 0xFF, 0xFF), 1)),
                BorderBrush = SK.V((Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF), 0), (SK.A(m.ClassColor, 0.25), 1)), BorderThickness = new Thickness(1),
            };
            if (m.You) card.BorderBrush = SK.B(SK.Gold, 0.6);
            return card;
        }
    }

    // ---------------------------------------------------------------------------------------------------------------------
    // C: Reactor. An instrument panel: each row a recessed track lit cell by cell in the class's colour, brightest at its
    // head, the rank on a solid class chip at its left; the DPS in an inset readout at its right with the share under it.
    // ---------------------------------------------------------------------------------------------------------------------
    static class StdC
    {
        const double RowH = 32;

        public static FrameworkElement Make(Encounter e, DateTime now, double width)
        {
            var v = StdView.Of(e, now);
            double dpsW = SK.Col(v.Rows.Select(r => r.CpOnly ? "" : r.Figure), "888.8K", 15, FontWeights.Bold);
            double readW = dpsW + 14;
            var body = new StackPanel();
            body.Children.Add(Head(v, readW));
            var rows = new StackPanel { Margin = new Thickness(0, 9, 0, 0) };
            foreach (var m in v.Rows)
            {
                if (m.Pinned && v.More > 0) rows.Children.Add(SK.More(v.More, Color.FromRgb(0xB1, 0xB7, 0xC8)));
                rows.Children.Add(Row(v, m, readW));
            }
            body.Children.Add(rows);
            if (!e.Waiting) body.Children.Add(Foot(v));
            return SK.Host(SK.Glass(body, width + 10, 10, new Thickness(10, 10, 10, 10), Color.FromRgb(0x8A, 0x90, 0xA4), 0.06));
        }

        /// <summary>An inset readout: a dark window sunk into the panel, a hairline of light along its lower lip.</summary>
        static Border Readout(UIElement child, Color edge, double a = 0.35) => new()
        {
            Child = child, CornerRadius = new CornerRadius(5), Padding = new Thickness(7, 2, 7, 2),
            Background = SK.V((Color.FromArgb(0xF0, 0x03, 0x04, 0x06), 0), (Color.FromArgb(0xE0, 0x0D, 0x0F, 0x14), 1)),
            BorderBrush = SK.V((Color.FromArgb(0x90, 0, 0, 0), 0), (SK.A(edge, a), 1)), BorderThickness = new Thickness(1),
        };

        static FrameworkElement Head(StdView v, double readW)
        {
            var e = v.E;
            var g = new Grid();
            g.ColumnDefinitions.Add(SK.Auto()); g.ColumnDefinitions.Add(SK.Star()); g.ColumnDefinitions.Add(SK.Auto());
            var art = GameArt.Element("boss", 22); art.Margin = new Thickness(0, 0, 8, 0);
            g.Children.Add(SK.At(art, 0));
            var title = SK.T(e.Title, 14, FontWeights.Bold, SK.Ink); title.TextTrimming = TextTrimming.CharacterEllipsis;
            var sub = SK.T(e.Live ? L.T(BossParser.Name(e.ModeKind)) : ParserPanel.OutcomeOf(e, v.Now).Text, 10, FontWeights.SemiBold, e.Live ? Color.FromRgb(0x8A, 0x90, 0xA4) : SK.Mint);
            g.Children.Add(SK.At(new StackPanel { Children = { title, sub }, VerticalAlignment = VerticalAlignment.Center }, 1));
            var clock = SK.T(e.Waiting ? L.T("Ready") : BossParser.Clock(e.Duration(v.Now)), 17, FontWeights.Bold, e.Live ? SK.Gold : SK.Ink);
            clock.HorizontalAlignment = HorizontalAlignment.Center;
            if (e.Live) clock.Effect = SK.Glow(SK.Gold, 10, 0.6);
            var ro = Readout(clock, e.Live ? SK.Gold : Colors.White, e.Live ? 0.45 : 0.15); ro.MinWidth = readW; ro.Margin = new Thickness(8, 0, 0, 0);
            g.Children.Add(SK.At(ro, 2));
            var st = new StackPanel { Children = { g } };
            if (e.Live && e.BossHpLeft is { } left)
            {
                var c = left < 0.34 ? SK.Coral : SK.Gold;
                var hp = new Grid { Margin = new Thickness(0, 8, 0, 0) };
                hp.ColumnDefinitions.Add(SK.Star()); hp.ColumnDefinitions.Add(SK.Auto());
                hp.Children.Add(Track(c, left, 8, 4, 3));
                var t = ParserPanel.HpText(e, 11, SK.B(left < 0.34 ? SK.Coral : SK.Ink), SK.B(Color.FromRgb(0xB1, 0xB7, 0xC8))); t.Margin = new Thickness(9, 0, 0, 0);
                hp.Children.Add(SK.At(t, 1));
                st.Children.Add(hp);
            }
            return st;
        }

        /// <summary>A recessed track lit cell by cell: cells <paramref name="cell"/> px wide with a 1 px dark gap, its head cells brightest.</summary>
        static Grid Track(Color c, double fill, double h, double cell, double radius)
        {
            var g = new Grid { Height = h, VerticalAlignment = VerticalAlignment.Center };
            g.Children.Add(new Border { CornerRadius = new CornerRadius(radius), Background = SK.V((Color.FromArgb(0xD0, 0x01, 0x01, 0x03), 0), (Color.FromArgb(0xB0, 0x0A, 0x0C, 0x10), 1)),
                BorderBrush = SK.V((Color.FromArgb(0x80, 0, 0, 0), 0), (Color.FromArgb(0x1C, 0xFF, 0xFF, 0xFF), 1)), BorderThickness = new Thickness(1) });
            // the unlit cells, faint, so the track reads as a scale
            g.Children.Add(new Border { Margin = new Thickness(2), Background = SK.B(c, 0.10), OpacityMask = Cells(cell) });
            if (fill > 0)
            {
                var lit = new Grid { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(2), Effect = SK.Glow(c, 9, 0.75) };
                lit.Children.Add(new Border { Background = SK.H((SK.A(SK.Deep(c), 1), 0), (c, 0.65), (SK.Mix(c, Colors.White, 0.5), 1)), OpacityMask = Cells(cell) });
                lit.Children.Add(new Border { Background = SK.V((Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF), 0), (Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 0.5), (Color.FromArgb(0x30, 0, 0, 0), 1)), OpacityMask = Cells(cell) });
                g.SizeChanged += (_, _) => lit.Width = Math.Max(cell, Math.Floor((g.ActualWidth - 4) * fill / (cell + 1)) * (cell + 1));
                g.Children.Add(lit);
            }
            return g;
        }

        static DrawingBrush Cells(double cell)
        {
            var d = new GeometryDrawing(Brushes.White, null, new RectangleGeometry(new Rect(0, 0, cell, 1)));
            var b = new DrawingBrush(d) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, cell + 1, 1), ViewportUnits = BrushMappingMode.Absolute, Viewbox = new Rect(0, 0, cell + 1, 1), ViewboxUnits = BrushMappingMode.Absolute, Stretch = Stretch.None };
            b.Freeze();
            return b;
        }

        static FrameworkElement Row(StdView v, MeterMember m, double readW)
        {
            var c = m.ClassColor;
            var g = new Grid { Height = RowH, Margin = new Thickness(0, 0, 0, 4) };
            g.ColumnDefinitions.Add(SK.Px(24)); g.ColumnDefinitions.Add(SK.Star()); g.ColumnDefinitions.Add(SK.Px(readW + 6));
            // the rank on a solid chip in the class's colour (gold for you), lit from above
            var chipC = m.You ? SK.Gold : c;
            var chip = new Border
            {
                Width = 22, Height = RowH, CornerRadius = new CornerRadius(5, 2, 2, 5), HorizontalAlignment = HorizontalAlignment.Left,
                Background = SK.V((SK.Lit(chipC), 0), (chipC, 0.5), (SK.Deep(chipC), 1)), BorderBrush = SK.B(Colors.White, 0.25), BorderThickness = new Thickness(0, 1, 0, 0),
                Child = SK.T(m.Rank.ToString(CultureInfo.InvariantCulture), 12, FontWeights.Black, Color.FromRgb(0x10, 0x0E, 0x08)),
            };
            ((TextBlock)chip.Child).HorizontalAlignment = HorizontalAlignment.Center;
            g.Children.Add(SK.At(chip, 0));
            // the track across the row, the words over it
            var track = Track(c, v.Fill(m), RowH, 3, 3);
            track.Margin = new Thickness(1, 0, 0, 0);
            var words = new Grid { Margin = new Thickness(8, 0, 8, 0) };
            words.ColumnDefinitions.Add(SK.Auto()); words.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); words.ColumnDefinitions.Add(SK.Auto());
            var icon = ClassArt.Icon(m.Class, 18, m.Name); icon.Margin = new Thickness(0, 0, 6, 0);
            words.Children.Add(SK.At(icon, 0));
            var nameSt = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var name = SK.Lift(SK.T(m.Name, 13, m.You ? FontWeights.Bold : FontWeights.SemiBold, m.You ? SK.GoldHi : SK.Ink), 4, 1);
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            nameSt.Children.Add(name);
            var sub = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, -2, 0, 0) };
            if (m.Cp.Length > 0) sub.Children.Add(SK.Lift(SK.T(L.T("CP") + " " + m.Cp, 9, FontWeights.SemiBold, SK.Ink, 0.62), 3, 1));
            if (m.Heal.Length > 0) { var h = SK.Lift(SK.T("+" + m.Heal, 9, FontWeights.Bold, Color.FromRgb(0x8C, 0xF5, 0xC0)), 3, 1); h.Margin = new Thickness(sub.Children.Count > 0 ? 7 : 0, 0, 0, 0); sub.Children.Add(h); }
            if (sub.Children.Count > 0) nameSt.Children.Add(sub);
            words.Children.Add(SK.At(nameSt, 1));
            var marks = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            foreach (var mk in SK.Marks(m, 7.5)) marks.Children.Add(mk);
            words.Children.Add(SK.At(marks, 2));
            var scrim = new Border { Margin = new Thickness(3), CornerRadius = new CornerRadius(2), IsHitTestVisible = false,
                Background = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(185, 0), MappingMode = BrushMappingMode.Absolute,
                    GradientStops = { new GradientStop(Color.FromArgb(0xE6, 0x05, 0x06, 0x09), 0), new GradientStop(Color.FromArgb(0xC8, 0x05, 0x06, 0x09), 0.72), new GradientStop(Color.FromArgb(0x00, 0x05, 0x06, 0x09), 1) } } };
            g.Children.Add(SK.At(new Grid { Children = { track, scrim, words } }, 1));
            // the readout: the DPS, the share under it (the CP before any damage)
            var fig = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var dps = SK.T(m.Figure, m.CpOnly ? 11.5 : 15, FontWeights.Bold, m.TopDps ? SK.GoldHi : SK.Ink); dps.HorizontalAlignment = HorizontalAlignment.Right;
            if (m.TopDps) dps.Effect = SK.Glow(SK.Gold, 8, 0.6);
            fig.Children.Add(dps);
            var share = SK.T(m.ShareText, 8.5, FontWeights.SemiBold, SK.Ink, 0.55); share.HorizontalAlignment = HorizontalAlignment.Right; share.Margin = new Thickness(0, -3, 0, 0);
            fig.Children.Add(share);
            var ro = Readout(fig, m.You ? SK.Gold : c, m.You ? 0.6 : 0.35); ro.Margin = new Thickness(6, 0, 0, 0); ro.Height = RowH;
            g.Children.Add(SK.At(ro, 2));
            if (m.You) g.Children.Add(SK.At(new Border { CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1), BorderBrush = SK.B(SK.Gold, 0.7), Margin = new Thickness(1, 0, 0, 0), Effect = SK.Glow(SK.Gold, 8, 0.45) }, 1));
            return g;
        }

        static FrameworkElement Foot(StdView v)
        {
            var e = v.E;
            var g = new Grid { Margin = new Thickness(0, 6, 0, 0) };
            g.ColumnDefinitions.Add(SK.Star()); g.ColumnDefinitions.Add(SK.Px(8)); g.ColumnDefinitions.Add(SK.Star());
            if (!e.HasDamage) return g;
            FrameworkElement Gauge(string label, string fig, Color fc, string? badge)
            {
                var row = new Grid();
                row.ColumnDefinitions.Add(SK.Auto()); row.ColumnDefinitions.Add(SK.Star()); row.ColumnDefinitions.Add(SK.Auto());
                row.Children.Add(SK.At(SK.T(label, 9.5, FontWeights.Bold, Color.FromRgb(0x8A, 0x90, 0xA4)), 0));
                var f = SK.T(fig, 13.5, FontWeights.Bold, fc); f.HorizontalAlignment = HorizontalAlignment.Right; f.Margin = new Thickness(8, 0, 0, 0);
                row.Children.Add(SK.At(f, 1));
                if (badge != null) row.Children.Add(SK.At(SK.Mark(badge, SK.Mint, false, 7.5), 2));
                var ro = Readout(row, Colors.White, 0.12); ro.Padding = new Thickness(8, 3, 8, 3);
                return ro;
            }
            g.Children.Add(SK.At(Gauge(L.T("Total"), Amounts.Meter(e.Credited), SK.Ink, null), 0));
            g.Children.Add(SK.At(Gauge(v.Held ? L.T("Group") : L.T("Group DPS"), e.GroupDpsText(e.End ?? v.Now), SK.Gold, v.Held ? L.T(Encounter.AvgBadge) : null), 2));
            return g;
        }
    }
}
