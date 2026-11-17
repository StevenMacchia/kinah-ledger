using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Effects;
using KinahLedger.Parser;

namespace KinahLedger.Ui;

public static partial class MeterConcepts
{
    static partial void AddAuto(List<Concept> l)
    {
        l.Add(new Concept(HudLooks.Auto, "A", "Lumen: a glass panel, each member a lit blade carrying the name and the DPS", AutoConcepts.Lumen));
        l.Add(new Concept(HudLooks.Auto, "B", "Broadcast: no panel, floating plates: a rank plate, a slanted bar, a score tab", AutoConcepts.Broadcast));
        l.Add(new Concept(HudLooks.Auto, "C", "Marquee: the name over a thick bar, the DPS stacked large beside it", AutoConcepts.Marquee));
    }
}

/// <summary>
/// Auto's three concepts (Steven, 2026-11-17: a "HARDCORE rework ... clear, legible and fucking incredible cinematic production
/// quality" that "prioritizes the bar ... doesn't crowd everything"), drawn for renders only (Ui/MeterConcepts.cs). Each member is
/// a thick bar in the class's colour near the meter's width; one big figure a row (the DPS), the share small, Combat Power small
/// and quiet by the name, the heal figure on healers only, TOP DPS / TOP HEALS as small marks; crit and back on the hover card.
/// Short of room a mark goes to its glyph, then YOU, then the CP (the card says them all): never a figure cut.
/// <list type="bullet">
/// <item>A, Lumen: a smoked glass panel; each member a lit glass blade with a rank notch, the name on it and the DPS at its end.</item>
/// <item>B, Broadcast: no panel, plates floating over the game as a broadcast's: a rank plate, a slanted bar, a score tab.</item>
/// <item>C, Marquee: the name over a thick glowing bar, the DPS large beside both with the share under it.</item>
/// </list>
/// </summary>
static class AutoConcepts
{
    // ---- the palette: one ink, two greys, gold for you and the clock, mint for a kill, red for the boss's HP

    static readonly Color Night = Rgb(0x0A0C11), Ink = Rgb(0xF6F8FD), Soft = Rgb(0xBAC1D1), Dim = Rgb(0x8B93A6), Gold = Rgb(0xF7C64F), GoldDeep = Rgb(0xD8961F),
        Mint = Rgb(0x46E3A0), Red = Rgb(0xFF4D5E), Coral = Rgb(0xFF6A5C), Heal = Rgb(0x7DF7B8), Plum = Rgb(0x10121A);

    static Color Rgb(uint rgb) => HudKit.Rgb(rgb);
    static Color A(Color c, double a) => Color.FromArgb((byte)Math.Round(255 * Math.Clamp(a, 0, 1)), c.R, c.G, c.B);
    static Color Mix(Color a, Color b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return Color.FromArgb((byte)(a.A + (b.A - a.A) * t), (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
    }
    static SolidColorBrush S(Color c, double a = 1) => HudKit.Frozen(A(c, a));
    static LinearGradientBrush G(bool down, params (Color C, double At)[] stops)
    {
        var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = down ? new Point(0, 1) : new Point(1, 0) };
        foreach (var (c, at) in stops) b.GradientStops.Add(new GradientStop(c, at));
        b.Freeze();
        return b;
    }
    static Pen P(Color c, double w) { var p = new Pen(new SolidColorBrush(c), w) { LineJoin = PenLineJoin.Round }; p.Freeze(); return p; }

    /// <summary>A text in the meter's face (Inter, shipped in Fonts/), tabular digits, never trimmed unless a name.</summary>
    static TextBlock T(string s, double size, Color c, FontWeight w, bool halo = false)
    {
        var t = new TextBlock { Text = s, FontSize = size, Foreground = S(c, c.A / 255.0), FontWeight = w, FontFamily = HudKit.Text, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.None };
        Typography.SetNumeralAlignment(t, FontNumeralAlignment.Tabular);
        if (halo) t.Effect = Halo();
        return t;
    }

    /// <summary>A tight dark edge round each letter: words and figures read over the brightest bar or the game.</summary>
    static DropShadowEffect Halo(double blur = 3, double opacity = .9) => new() { Color = Colors.Black, BlurRadius = blur, ShadowDepth = 0, Opacity = opacity };

    /// <summary>A label in capitals, tracked out with hair spaces in Latin (a Korean or Chinese label as it is).</summary>
    static string Caps(string s)
    {
        var u = s.ToUpper(CultureInfo.CurrentCulture);
        return u.All(ch => ch < 128) ? string.Join(" ", u.ToCharArray()) : u;
    }

    static double W(FrameworkElement e)
    {
        var v = e.Visibility; e.Visibility = Visibility.Visible;
        e.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double w = e.DesiredSize.Width; e.Visibility = v;
        return w;
    }

    /// <summary>The widest of <paramref name="forms"/> in a block's face, size and weight.</summary>
    static double TW(TextBlock like, params string[] forms) => forms.Max(f =>
    {
        var p = T(f, like.FontSize, Colors.White, like.FontWeight);
        TextOptions.SetTextFormattingMode(p, TextFormattingMode.Display);
        return Math.Ceiling(W(p));
    });

    /// <summary>A part drawn in code (a bar, a plate), as wide and tall as its cell; never takes the pointer.</summary>
    sealed class Sketch : FrameworkElement
    {
        readonly Action<DrawingContext, Size> _draw;
        public Sketch(Action<DrawingContext, Size> draw) { _draw = draw; IsHitTestVisible = false; }
        protected override void OnRender(DrawingContext dc) => _draw(dc, RenderSize);
    }

    /// <summary>
    /// A row's name line fitted to its room: the name whole when it fits (up to ten letters always; a longer one keeps six and
    /// its "…"); else the <paramref name="steps"/> are taken in turn (a mark to its glyph, YOU away, the CP away, the glyph away)
    /// until the line fits; the deaths and a healer's figure never go.
    /// </summary>
    static void Fit(StackPanel line, TextBlock name, double room, IReadOnlyList<Action> steps)
    {
        name.ClearValue(FrameworkElement.MaxWidthProperty);
        double nw = W(name);
        double floor = name.Text.Length <= 10 ? nw : Math.Min(nw, TW(name, name.Text[..6] + "…") + 1);
        double Marks() => line.Children.OfType<FrameworkElement>().Where(c => !ReferenceEquals(c, name) && c.Visibility == Visibility.Visible).Sum(W);
        // a long name gives way first, down to its floor; only then the marks, step by step
        int i = 0;
        while (floor + Marks() > room && i < steps.Count) steps[i++]();
        name.MaxWidth = Math.Max(floor, room - Marks());
    }

    /// <summary>The member's bar, 0 to 1, against the fight's top member (before a hit, by Combat Power).</summary>
    static double BarOf(MeterMember m, MeterContext c) { double top = c.E.TopMeasure(); return top > 0 ? Math.Clamp(c.E.Measure(m.F) / top, 0, 1) : 0; }

    /// <summary>The class's colour a shade deeper, so white words stand on it.</summary>
    static Color Deep(Color cls) => Jewel(cls, .62);

    /// <summary>
    /// The class's colour as a jewel: the game's party colours are pastel, so the bar's body takes the same hue more saturated
    /// and darker (<paramref name="light"/> of its lightness); its head keeps the pastel as the light.
    /// </summary>
    static Color Jewel(Color c, double light, double sat = 1.45)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0, max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        double l = (max + min) / 2, s = 0, h = 0, d = max - min;
        if (d > 1e-6)
        {
            s = l > .5 ? d / (2 - max - min) : d / (max + min);
            h = max == r ? (g - b) / d + (g < b ? 6 : 0) : max == g ? (b - r) / d + 2 : (r - g) / d + 4;
            h /= 6;
        }
        s = Math.Min(1, s * sat + (d > 1e-6 ? .06 : 0)); l = Math.Clamp(l * light, 0, 1);
        double q = l < .5 ? l * (1 + s) : l + s - l * s, p = 2 * l - q;
        double F(double t) { t = t < 0 ? t + 1 : t > 1 ? t - 1 : t; return t < 1 / 6.0 ? p + (q - p) * 6 * t : t < .5 ? q : t < 2 / 3.0 ? p + (q - p) * (2 / 3.0 - t) * 6 : p; }
        return Color.FromRgb((byte)Math.Round(F(h + 1 / 3.0) * 255), (byte)Math.Round(F(h) * 255), (byte)Math.Round(F(h - 1 / 3.0) * 255));
    }

    static (string Word, Color Tone)? Outcome(Encounter e) => e.Live || e.Waiting || e.Outcome.Length == 0 ? null : e.Outcome switch
    {
        "kill" => (L.T("Killed"), Mint), "cleared" => (L.T("Cleared"), Mint), "wipe" => (L.T("Wiped"), Red), _ => (L.T("Over"), Soft),
    };

    static string ClockOf(Encounter e, DateTime now) => e.Waiting ? L.T("Ready") : BossParser.Clock(e.Duration(now));
    static double? HpOf(Encounter e) => e.Live && !e.Waiting && e.BossHpLeft is { } h ? h : null;

    /// <summary>"47%" in red, "· 12.84M left" quiet.</summary>
    static TextBlock HpWords(Encounter e, double hp, double size)
    {
        var t = T("", size, Soft, FontWeights.SemiBold);
        t.Inlines.Add(new Run($"{Math.Round(hp * 100):0}%") { Foreground = S(hp < .34 ? Red : Mix(Red, Colors.White, .25)), FontWeight = FontWeights.Bold });
        var left = ParserPanel.HpLeft(e);
        if (left.Length > 0) t.Inlines.Add(new Run("  ·  " + left));
        return t;
    }

    /// <summary>
    /// The boss's HP: a red bar lit from above with a bright head, and its thirds as engraved notches that reach just past it.
    /// </summary>
    static void DrawHp(DrawingContext dc, Rect r, double hp, double radius, Color track, double notch = 2)
    {
        dc.DrawRoundedRectangle(S(track, track.A / 255.0), P(A(Colors.Black, .35), 1), r, radius, radius);
        double fw = r.Width * Math.Clamp(hp, 0, 1);
        if (fw > .5)
        {
            var fr = new Rect(r.X, r.Y, fw, r.Height);
            // a soft red light round the fill
            for (int i = 3; i >= 1; i--) dc.DrawRoundedRectangle(S(Red, .06 * (4 - i)), null, new Rect(fr.X - i, fr.Y - i, fr.Width + 2 * i, fr.Height + 2 * i), radius + i, radius + i);
            dc.PushClip(new RectangleGeometry(r, radius, radius));
            dc.DrawRectangle(G(false, (Rgb(0x6E0A18), 0), (Rgb(0xD9223A), .75), (Rgb(0xFF4A5C), 1)), null, fr);
            dc.DrawRectangle(G(true, (A(Colors.White, .40), 0), (A(Colors.White, .06), .42), (A(Colors.White, 0), .45), (A(Colors.Black, .25), 1)), null, fr);
            dc.DrawRectangle(S(Mix(Red, Colors.White, .75)), null, new Rect(r.X + fw - 1.5, r.Y, 1.5, r.Height));
            dc.Pop();
        }
        foreach (var third in new[] { 1 / 3.0, 2 / 3.0 })
        {
            double x = Math.Round(r.X + r.Width * third);
            dc.DrawRectangle(S(Colors.Black, .7), null, new Rect(x - 1, r.Y - notch, 1, r.Height + 2 * notch));
            dc.DrawRectangle(S(Colors.White, .45), null, new Rect(x, r.Y - notch, 1, r.Height + 2 * notch));
        }
    }

    /// <summary>A small mark at the end of a boss fight: gold TOP DPS, green TOP HEALS (or their glyph, short of room).</summary>
    static Border Plate(string s, bool heals, double size = 8)
    {
        Color hi = heals ? Rgb(0x9BF5C8) : Rgb(0xFFE6A3), mid = heals ? Rgb(0x3DD68C) : Rgb(0xF5B942), lo = heals ? Rgb(0x1E9E62) : Rgb(0xC98A1C);
        var t = T(s, size, heals ? Rgb(0x062A17) : Rgb(0x2A1A00), FontWeights.Bold);
        return new Border
        {
            Child = t, Tag = heals ? "top-heals" : "top-dps", Height = 14, CornerRadius = new CornerRadius(3), Padding = new Thickness(4, 0, 4, 0), Margin = new Thickness(6, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center, Background = G(true, (hi, 0), (mid, .5), (lo, 1)), BorderBrush = S(Colors.White, .45), BorderThickness = new Thickness(0, .8, 0, 0),
            Effect = new DropShadowEffect { Color = mid, BlurRadius = 7, ShadowDepth = 0, Opacity = .55 },
            ToolTip = L.T(heals ? "Top heals" : "Top DPS"),
        };
    }

    /// <summary>YOU: a gold outline tag.</summary>
    static Border You() => new()
    {
        Child = T(L.T("YOU"), 8.5, Gold, FontWeights.Bold), Tag = "you", CornerRadius = new CornerRadius(3), Padding = new Thickness(4, 0, 4, 0), Height = 14, Margin = new Thickness(6, 0, 0, 0),
        VerticalAlignment = VerticalAlignment.Center, Background = S(Gold, .16), BorderBrush = S(Gold, .85), BorderThickness = new Thickness(1),
    };

    /// <summary>The name's line on every concept's row: name, CP (quiet), deaths, a healer's healing, the marks, YOU; fitted later.</summary>
    sealed class NameLine
    {
        public readonly StackPanel Line = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        public readonly TextBlock Name;
        /// <summary>The line put back whole (every mark in its full form), and the steps it gives way by, in order.</summary>
        readonly List<Action> _reset = new(), _steps = new();
        readonly double _cpSize, _healSize;
        readonly bool _halo;
        public NameLine(double size, double cpSize, double healSize, bool halo)
        {
            Name = T("", size, Ink, FontWeights.SemiBold, halo); Name.TextTrimming = TextTrimming.CharacterEllipsis;
            _cpSize = cpSize; _healSize = healSize; _halo = halo;
        }

        public void Set(MeterMember m, Color nameTone)
        {
            Name.Text = m.Name;
            Name.Foreground = S(nameTone);
            Name.FontWeight = m.You ? FontWeights.Bold : FontWeights.SemiBold;
            Line.Children.Clear(); _reset.Clear(); _steps.Clear();
            Line.Children.Add(Name);
            FrameworkElement Add(FrameworkElement el) { Line.Children.Add(el); return el; }
            void Goes(FrameworkElement el) { _reset.Add(() => el.Visibility = Visibility.Visible); _steps.Add(() => el.Visibility = Visibility.Collapsed); }
            // Combat Power by the name, small and quiet; the deaths and a healer's figure, which never go; the marks; YOU
            FrameworkElement? cp = null;
            if (m.Cp.Length > 0) { var c = T(m.Cp, _cpSize, A(Ink, .70), FontWeights.SemiBold, _halo); c.Margin = new Thickness(6, 1, 0, 0); c.Tag = "cp"; c.ToolTip = L.F("Combat Power {0:N0}", m.F.CombatPower); cp = Add(c); }
            if (m.Deaths > 0) { var d = T(m.Deaths > 1 ? $"✕{m.Deaths}" : "✕", _cpSize + .5, Coral, FontWeights.Bold, _halo); d.Margin = new Thickness(6, 0, 0, 0); Add(d); }
            if (m.Heal.Length > 0) { var h = T("+" + m.Heal, _healSize, _halo ? Mix(Heal, Colors.White, .35) : Heal, FontWeights.Bold); if (_halo) h.Effect = Halo(4, 1); h.Margin = new Thickness(6, 0, 0, 0); h.Tag = "heal-figures"; Add(h); }
            var glyphs = new List<FrameworkElement>();
            foreach (var (on, heals) in new[] { (m.TopDps, false), (m.TopHeals, true) })
            {
                if (!on) continue;
                var full = Add(Plate(L.T(heals ? "TOP HEALS" : "TOP DPS"), heals)); var glyph = Add(Plate(heals ? "✚" : "★", heals, 9));
                _reset.Add(() => { full.Visibility = Visibility.Visible; glyph.Visibility = Visibility.Collapsed; });
                _steps.Add(() => { full.Visibility = Visibility.Collapsed; glyph.Visibility = Visibility.Visible; });
                glyphs.Add(glyph);
            }
            if (m.You) Goes(Add(You()));
            if (cp != null) Goes(cp);
            // last of all the marks' glyphs (the hover card says them)
            foreach (var g in glyphs) _steps.Add(() => g.Visibility = Visibility.Collapsed);
        }

        public void Fit(double room)
        {
            foreach (var r in _reset) r();
            AutoConcepts.Fit(Line, Name, room, _steps);
        }
    }

    /// <summary>The members' rows for a concept's row kind, with the line between the party and you pinned below it.</summary>
    static ThemeRows Rows(Encounter e, DateTime now, double pitch, Func<MeterMember, IThemeRow> make, double gapH, Func<int, FrameworkElement> gap)
    {
        var rows = new ThemeRows(pitch, make, gapH, gap);
        rows.Show(e, now);
        return rows;
    }

    /// <summary>"1 MORE" between two hairlines that fade out.</summary>
    static FrameworkElement GapLine(int left, Color tone)
    {
        var g = new Grid { VerticalAlignment = VerticalAlignment.Center };
        g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); g.ColumnDefinitions.Add(new ColumnDefinition());
        var l = new Border { Height = 1, Background = G(false, (A(tone, 0), 0), (A(tone, .35), 1)), VerticalAlignment = VerticalAlignment.Center };
        var r = new Border { Height = 1, Background = G(false, (A(tone, .35), 0), (A(tone, 0), 1)), VerticalAlignment = VerticalAlignment.Center };
        var t = T(left > 0 ? Caps(L.F("{0} more", left)) : "", 9, A(tone, .9), FontWeights.Bold); t.Margin = new Thickness(8, 0, 8, 0);
        g.Children.Add(l); Grid.SetColumn(t, 1); g.Children.Add(t); Grid.SetColumn(r, 2); g.Children.Add(r);
        return g;
    }

    /// <summary>The fight's foot as the Parser says it: TOTAL and the figure; the group's word and figure (alone: best or peak), the AVG. DPS badge once held.</summary>
    static (string Total, string Word, string Value, bool Held) FootOf(Encounter e, DateTime now)
    {
        var (word, value, held) = ParserFoot.Right(e, now);
        return (Amounts.Meter(e.Credited), word, value, held);
    }

    static T AsFoot<T>(T foot, Encounter e, DateTime now) where T : FrameworkElement
    {
        foot.Tag = "parser-foot";
        System.Windows.Automation.AutomationProperties.SetName(foot, e.FootText(now));
        return foot;
    }

    /// <summary>A panel: its shadow under the game-facing glass, the glass's fill and edge, a light along its top, the content.</summary>
    static FrameworkElement Glass(UIElement content, double width, double radius, Brush fill, Brush edge)
    {
        var g = new Grid { Width = width, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        g.Children.Add(new Border { CornerRadius = new CornerRadius(radius), Background = S(Night, .55), Margin = new Thickness(2),
            Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 24, ShadowDepth = 5, Direction = 270, Opacity = .6 } });
        g.Children.Add(new Border { CornerRadius = new CornerRadius(radius), Background = fill, BorderBrush = edge, BorderThickness = new Thickness(1) });
        // the light along the glass's top: a bright hairline fading out to the corners, a faint wash under it
        g.Children.Add(new Sketch((dc, s) =>
        {
            dc.PushClip(new RectangleGeometry(new Rect(s), radius, radius));
            dc.DrawRectangle(G(true, (A(Colors.White, .06), 0), (A(Colors.White, 0), 1)), null, new Rect(0, 0, s.Width, Math.Min(46, s.Height)));
            dc.DrawRectangle(G(false, (A(Colors.White, 0), 0), (A(Colors.White, .32), .5), (A(Colors.White, 0), 1)), null, new Rect(radius, 1, Math.Max(0, s.Width - 2 * radius), 1));
            dc.Pop();
        }));
        g.Children.Add(content);
        TextOptions.SetTextFormattingMode(g, TextFormattingMode.Display);
        TextElement.SetFontFamily(g, HudKit.Text);
        return g;
    }

    /// <summary>The ready card's line, when no party waits under it.</summary>
    static TextBlock ReadyLine(Encounter e, Thickness m) { var w = T(e.ReadyText, 12.5, Dim, FontWeights.SemiBold); w.Margin = m; return w; }

    // ================================================================ A, Lumen

    /// <summary>
    /// A, Lumen: a smoked glass panel. The header: the boss in white, the outcome in mint, the clock large in gold while it runs;
    /// under them the boss's HP as a lit red bar with its thirds notched, the % and what's left at its end. Each member a glass
    /// blade the row's width, lit in the class's colour to their part of the top, a bright head; the rank cut into a dark notch at
    /// its left (gold for you), the name on it, the DPS large at its end, the share small after. The foot: TOTAL and the group.
    /// </summary>
    public static FrameworkElement Lumen(Encounter e, DateTime now, double width)
    {
        const double px = 10;
        var body = new StackPanel();
        // the header
        var head = new Grid { Margin = new Thickness(14, 11, 14, 0) };
        head.ColumnDefinitions.Add(new ColumnDefinition()); head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = T(e.Title, 15.5, Ink, FontWeights.Bold); title.TextTrimming = TextTrimming.CharacterEllipsis;
        head.Children.Add(title);
        if (Outcome(e) is { } o)
        {
            var tag = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 1, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            tag.Children.Add(new System.Windows.Shapes.Ellipse { Width = 6, Height = 6, Fill = S(o.Tone), Margin = new Thickness(0, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center, Effect = new DropShadowEffect { Color = o.Tone, BlurRadius = 6, ShadowDepth = 0, Opacity = .9 } });
            tag.Children.Add(T(Caps(o.Word), 10, o.Tone, FontWeights.Bold));
            Grid.SetColumn(tag, 1); head.Children.Add(tag);
        }
        var clock = T(ClockOf(e, now), e.Waiting ? 13 : 19, e.Live && !e.Waiting ? Gold : e.Waiting ? Dim : Ink, FontWeights.Bold); clock.Margin = new Thickness(12, 0, 0, 0);
        if (e.Live && !e.Waiting) clock.Effect = new DropShadowEffect { Color = GoldDeep, BlurRadius = 10, ShadowDepth = 0, Opacity = .45 };
        Grid.SetColumn(clock, 2); head.Children.Add(clock);
        body.Children.Add(head);
        if (HpOf(e) is { } hp)
        {
            var hpRow = new Grid { Margin = new Thickness(14, 8, 14, 0), Height = 16 };
            hpRow.ColumnDefinitions.Add(new ColumnDefinition()); hpRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            hpRow.Children.Add(new Sketch((dc, s) => DrawHp(dc, new Rect(0, (s.Height - 7) / 2, s.Width, 7), hp, 3.5, A(Colors.Black, .45))));
            var words = HpWords(e, hp, 11.5); words.Margin = new Thickness(10, 0, 0, 0); Grid.SetColumn(words, 1); hpRow.Children.Add(words);
            body.Children.Add(hpRow);
        }
        body.Children.Add(new Border { Height = 1, Margin = new Thickness(px, 10, px, 0), Background = G(false, (A(Colors.White, 0), 0), (A(Colors.White, .14), .5), (A(Colors.White, 0), 1)) });
        // the members
        double rowW = width - 2 * px;
        if (e.Waiting && !HudStackLook.PartyWaiting(e)) body.Children.Add(ReadyLine(e, new Thickness(14, 10, 14, 12)));
        else
        {
            if (e.Waiting) body.Children.Add(ReadyLine(e, new Thickness(14, 8, 14, 0)));
            var rows = Rows(e, now, LumenRow.Pitch, m => new LumenRow(rowW), 16, left => GapLine(left, Soft));
            rows.Margin = new Thickness(px, 9, px, 0);
            body.Children.Add(rows);
        }
        // the foot
        if (e.HasDamage && !e.Waiting)
        {
            var (total, word, value, held) = FootOf(e, now);
            body.Children.Add(new Border { Height = 1, Margin = new Thickness(px, 7, px, 0), Background = G(false, (A(Colors.White, 0), 0), (A(Colors.White, .12), .5), (A(Colors.White, 0), 1)) });
            var foot = new Grid { Margin = new Thickness(14, 7, 14, 10) };
            foot.ColumnDefinitions.Add(new ColumnDefinition()); foot.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var l = new StackPanel { Orientation = Orientation.Horizontal };
            var lw = T(Caps(L.T("TOTAL")), 9.5, Dim, FontWeights.Bold); lw.Margin = new Thickness(0, 1, 7, 0); l.Children.Add(lw);
            l.Children.Add(T(total, 15, Ink, FontWeights.Bold));
            foot.Children.Add(l);
            var r = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 0, 0, 0) };
            if (word.Length > 0)
            {
                var rw = T(Caps(word), 9.5, Dim, FontWeights.Bold); rw.Margin = new Thickness(0, 1, 7, 0); r.Children.Add(rw);
                var rv = T(value, 15, Gold, FontWeights.Bold); rv.Tag = "foot-right"; r.Children.Add(rv);
            }
            if (held) r.Children.Add(ParserFoot.AvgBadge());
            Grid.SetColumn(r, 1); foot.Children.Add(r);
            body.Children.Add(AsFoot(foot, e, now));
        }
        else body.Children.Add(new Border { Height = 10 });
        return Glass(body, width, 12, G(true, (A(Rgb(0x171B26), .94), 0), (A(Rgb(0x0B0D13), .95), 1)), G(true, (A(Colors.White, .20), 0), (A(Colors.White, .05), 1)));
    }

    /// <summary>A's row: a glass blade the row's width; the rank notch, the name on it, the DPS at its end, the share after it.</summary>
    sealed class LumenRow : IThemeRow
    {
        public const double H = 30, Pitch = 35, Notch = 28, NameIn = 9;
        readonly double _w, _dpsCol, _shareCol;
        readonly Grid _root = new() { Height = H, Background = Brushes.Transparent };
        readonly TextBlock _rank = T("", 13, Ink, FontWeights.Bold), _dps = T("", 18, Ink, FontWeights.Bold), _share = T("", 11, A(Ink, .74), FontWeights.SemiBold, true);
        readonly NameLine _line = new(14, 10.5, 11.5, true);
        MeterMember? _m;
        double _bar;
        public FrameworkElement Root => _root;

        public LumenRow(double w)
        {
            _w = w; _root.Width = w;
            _dpsCol = TW(_dps, "888.8K", "8.88M"); _shareCol = TW(_share, "88.8%", "100%", L.T("CP"));
            foreach (var cw in new[] { Notch, double.NaN, _dpsCol + 8, _shareCol + 10 })
                _root.ColumnDefinitions.Add(new ColumnDefinition { Width = double.IsNaN(cw) ? new GridLength(1, GridUnitType.Star) : new GridLength(cw) });
            // the blade ends before the share, which sits on the glass: no figure on the bar's brightest part
            var art = new Sketch(Draw); Grid.SetColumnSpan(art, 3); _root.Children.Add(art);
            _rank.HorizontalAlignment = HorizontalAlignment.Center; _root.Children.Add(_rank);
            _line.Line.Margin = new Thickness(NameIn, 0, 0, 0); Grid.SetColumn(_line.Line, 1); Grid.SetColumnSpan(_line.Line, 2); _root.Children.Add(_line.Line);
            _dps.HorizontalAlignment = HorizontalAlignment.Right; _dps.Margin = new Thickness(0, 0, 8, 0); _dps.Tag = "row-dps"; _dps.Effect = Halo(4.5, 1); Grid.SetColumn(_dps, 1); Grid.SetColumnSpan(_dps, 2); _root.Children.Add(_dps);
            _share.HorizontalAlignment = HorizontalAlignment.Right; _share.Margin = new Thickness(0, 1, 2, 0); _share.Foreground = S(Soft); Grid.SetColumn(_share, 3); _root.Children.Add(_share);
        }

        public void Set(MeterMember m, MeterContext c, bool fresh)
        {
            _m = m;
            _rank.Text = m.Rank.ToString(CultureInfo.InvariantCulture);
            _rank.Foreground = S(m.You ? Rgb(0x2A1A00) : Ink, m.You ? 1 : .92);
            _line.Set(m, Ink);
            _dps.Text = m.Figure; _share.Text = m.ShareText;
            _bar = BarOf(m, c);
            // the words run up to the DPS, 10 px short of it
            double room = _w - Notch - NameIn - (_shareCol + 10) - W(_dps) - 10;   // W(_dps) has its 8 px margin
            _line.Fit(room);
            _root.InvalidateVisual();
            foreach (var ch in _root.Children.OfType<Sketch>()) ch.InvalidateVisual();
        }

        void Draw(DrawingContext dc, Size s)
        {
            if (_m is not { } m || s.Width < 40) return;
            var r = new Rect(0, 0, s.Width, s.Height);
            const double rad = 6;
            var cls = m.ClassColor; var deep = Deep(cls);
            // you: a gold rim with a soft glow round the blade
            if (m.You)
                for (int i = 3; i >= 1; i--) dc.DrawRoundedRectangle(null, P(A(Gold, .07 * (4 - i)), 2), new Rect(-i, -i, s.Width + 2 * i, s.Height + 2 * i), rad + i, rad + i);
            dc.PushClip(new RectangleGeometry(r, rad, rad));
            // the track: dark glass with the class's colour faint in it
            dc.DrawRectangle(G(true, (A(Colors.Black, .30), 0), (A(Colors.Black, .45), 1)), null, r);
            dc.DrawRectangle(G(false, (A(cls, .16), 0), (A(cls, .06), 1)), null, r);
            double fx = _bar <= 0 ? 0 : Math.Max(Notch + 10, s.Width * _bar);
            if (fx > 0)
            {
                var fr = new Rect(0, 0, fx, s.Height);
                // the body: the class's jewel, deeper at its root, lit toward its head
                dc.DrawRectangle(G(false, (Jewel(cls, .42), 0), (deep, .62), (Jewel(cls, .82), 1)), null, fr);
                // the glass: a bright band along its top, its foot in shade
                dc.DrawRectangle(G(true, (A(Colors.White, .30), 0), (A(Colors.White, .10), .30), (A(Colors.White, 0), .42), (A(Colors.Black, 0), .6), (A(Colors.Black, .30), 1)), null, fr);
                // the head: the light it throws back along the blade and on to the track past it, a fine edge (soft under the
                // DPS's figure: no hard line through a digit)
                var lit = Mix(cls, Colors.White, .45);
                bool underFigure = fx > s.Width - _dpsCol - 14;
                dc.DrawRectangle(G(false, (A(lit, 0), 0), (A(lit, underFigure ? .12 : .32), 1)), null, new Rect(Math.Max(0, fx - 26), 0, Math.Min(26, fx), s.Height));
                if (fx < s.Width - 2) dc.DrawRectangle(G(false, (A(cls, .34), 0), (A(cls, 0), 1)), null, new Rect(fx, 0, Math.Min(16, s.Width - fx), s.Height));
                if (!underFigure) dc.DrawRectangle(G(true, (A(Mix(cls, Colors.White, .8), .95), 0), (A(Mix(cls, Colors.White, .8), .7), 1)), null, new Rect(fx - 1.5, 0, 1.5, s.Height));
                else { var tip = S(Mix(cls, Colors.White, .8), .9); dc.DrawRectangle(tip, null, new Rect(fx - 1.5, 0, 1.5, 5)); dc.DrawRectangle(tip, null, new Rect(fx - 1.5, s.Height - 5, 1.5, 5)); }
            }
            // the rank's notch: dark glass (gold for you)
            dc.DrawRectangle(m.You ? G(true, (Rgb(0xFFE08A), 0), (Rgb(0xF2B53A), .55), (Rgb(0xC98A1C), 1)) : G(true, (A(Colors.Black, .30), 0), (A(Colors.Black, .48), 1)), null, new Rect(0, 0, Notch, s.Height));
            if (!m.You) dc.DrawRectangle(S(Colors.White, .10), null, new Rect(Notch, 0, 1, s.Height));
            dc.DrawRectangle(S(Colors.White, .16), null, new Rect(0, 0, s.Width, 1));
            dc.Pop();
            dc.DrawRoundedRectangle(null, P(m.You ? Gold : A(Mix(cls, Colors.White, .2), .40), m.You ? 1.4 : 1), new Rect(.5, .5, s.Width - 1, s.Height - 1), rad, rad);
        }
    }

    // ================================================================ B, Broadcast

    /// <summary>
    /// B, Broadcast: no panel; plates float over the game as a broadcast's lower third, each with its shadow. The header slab: a
    /// tone bar at its left (gold while it runs, mint for a kill), the boss and under it the HP's words; the clock in its own
    /// slanted tab (gold, or mint with KILLED over it). The boss's HP a stripe under the slab. Each member: a rank plate, then the
    /// bar (a dark track, the class's colour slanted at its head, the name on it), then a score tab with the DPS and the share.
    /// </summary>
    public static FrameworkElement Broadcast(Encounter e, DateTime now, double width)
    {
        var body = new StackPanel { Width = width, HorizontalAlignment = HorizontalAlignment.Left };
        TextOptions.SetTextFormattingMode(body, TextFormattingMode.Display);
        TextElement.SetFontFamily(body, HudKit.Text);
        var o = Outcome(e);
        bool live = e.Live && !e.Waiting;
        Color tone = live ? Gold : o?.Tone ?? Soft;
        // the header slab and its clock tab
        const double headH = 44, lean = 9;
        var clock = T(ClockOf(e, now), e.Waiting ? 13 : 18, e.Waiting ? Ink : Rgb(0x17110A), FontWeights.Bold);
        var status = o is { } oo ? T(Caps(oo.Word), 8.5, A(Rgb(0x062A17), .85), FontWeights.Bold) : null;
        if (o is { Tone: var ot } && ot == Red) status!.Foreground = S(Rgb(0x2A0006), .85);
        double tabW = Math.Max(W(clock), status != null ? W(status) : 0) + 2 * lean + 16;
        double tx = width - tabW;
        var head = new Grid { Height = headH };
        head.Children.Add(new Sketch((dc, s) =>
        {
            var slab = Poly(new Point(0, 0), new Point(tx + lean - 3, 0), new Point(tx - 3, s.Height), new Point(0, s.Height));
            dc.DrawGeometry(G(true, (A(Rgb(0x1B1F29), .96), 0), (A(Rgb(0x0C0E14), .97), 1)), null, slab);
            dc.PushClip(slab);
            dc.DrawRectangle(S(Colors.White, .14), null, new Rect(0, 0, s.Width, 1));
            dc.DrawRectangle(G(false, (A(tone, .16), 0), (A(tone, 0), .55)), null, new Rect(0, 0, tx, s.Height));
            dc.DrawRectangle(G(true, (Mix(tone, Colors.White, .35), 0), (tone, .5), (Mix(tone, Colors.Black, .3), 1)), null, new Rect(0, 0, 4, s.Height));
            dc.Pop();
            var tab = Poly(new Point(tx + lean, 0), new Point(s.Width, 0), new Point(s.Width, s.Height), new Point(tx, s.Height));
            Color tc = e.Waiting ? Rgb(0x2A2F3B) : tone;
            dc.DrawGeometry(G(true, (Mix(tc, Colors.White, .38), 0), (tc, .5), (Mix(tc, Colors.Black, .28), 1)), null, tab);
            dc.PushClip(tab);
            dc.DrawRectangle(S(Colors.White, .5), null, new Rect(0, 0, s.Width, 1));
            dc.Pop();
        }) { Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 12, ShadowDepth = 3, Direction = 270, Opacity = .6 } });
        var words = new StackPanel { Margin = new Thickness(16, 0, tabW + 6, 0), VerticalAlignment = VerticalAlignment.Center };
        var title = T(e.Title, 15, Ink, FontWeights.Bold); title.TextTrimming = TextTrimming.CharacterEllipsis;
        words.Children.Add(title);
        double? hp = HpOf(e);
        if (hp is { } h0) { var hw = HpWords(e, h0, 11); hw.Margin = new Thickness(0, 1, 0, 0); words.Children.Add(hw); }
        head.Children.Add(words);
        var tabWords = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Width = tabW - lean, Margin = new Thickness(0, 0, 0, 0) };
        if (status != null) { status.HorizontalAlignment = HorizontalAlignment.Center; status.Margin = new Thickness(0, 0, 0, -1); tabWords.Children.Add(status); }
        clock.HorizontalAlignment = HorizontalAlignment.Center; tabWords.Children.Add(clock);
        head.Children.Add(tabWords);
        body.Children.Add(head);
        if (hp is { } h1)
            body.Children.Add(new Sketch((dc, s) => DrawHp(dc, new Rect(0, 0, s.Width, s.Height), h1, 1.5, A(Colors.Black, .74), 1.5))
            { Height = 7, Margin = new Thickness(0, 4, 0, 0), Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 6, ShadowDepth = 1, Opacity = .5 } });
        // the members
        if (e.Waiting && !HudStackLook.PartyWaiting(e)) body.Children.Add(Plain(ReadyLine(e, new Thickness(12, 6, 12, 7)), width));
        else
        {
            var rows = Rows(e, now, BroadcastRow.Pitch, m => new BroadcastRow(width), 18, left =>
            {
                var t = T(left > 0 ? "+" + Caps(L.F("{0} more", left)) : "", 9, Soft, FontWeights.Bold);
                return new Border { Child = t, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Padding = new Thickness(9, 1, 9, 1), CornerRadius = new CornerRadius(8),
                    Background = S(Rgb(0x0C0E14), .88), BorderBrush = S(Colors.White, .14), BorderThickness = new Thickness(1) };
            });
            rows.Margin = new Thickness(0, 7, 0, 0);
            body.Children.Add(rows);
        }
        // the foot slab
        if (e.HasDamage && !e.Waiting)
        {
            var (total, word, value, held) = FootOf(e, now);
            var foot = new Grid { Height = 30, Margin = new Thickness(0, 2, 0, 0) };
            foot.Children.Add(new Sketch((dc, s) =>
            {
                var slab = Poly(new Point(0, 0), new Point(s.Width, 0), new Point(s.Width - lean, s.Height), new Point(0, s.Height));
                dc.DrawGeometry(G(true, (A(Rgb(0x181C25), .95), 0), (A(Rgb(0x0B0D12), .96), 1)), null, slab);
                dc.PushClip(slab);
                dc.DrawRectangle(S(Colors.White, .12), null, new Rect(0, 0, s.Width, 1));
                dc.DrawRectangle(S(Soft, .7), null, new Rect(0, 0, 4, s.Height));
                dc.Pop();
            }) { Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 10, ShadowDepth = 2, Direction = 270, Opacity = .55 } });
            var g = new Grid { Margin = new Thickness(14, 0, lean + 10, 0) };
            g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var l = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var lw = T(Caps(L.T("TOTAL")), 9, Dim, FontWeights.Bold); lw.Margin = new Thickness(0, 1, 7, 0); l.Children.Add(lw);
            l.Children.Add(T(total, 14.5, Ink, FontWeights.Bold));
            g.Children.Add(l);
            var r = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
            if (word.Length > 0)
            {
                var rw = T(Caps(word), 9, Dim, FontWeights.Bold); rw.Margin = new Thickness(0, 1, 7, 0); r.Children.Add(rw);
                var rv = T(value, 14.5, Gold, FontWeights.Bold); rv.Tag = "foot-right"; r.Children.Add(rv);
            }
            if (held) r.Children.Add(ParserFoot.AvgBadge());
            Grid.SetColumn(r, 1); g.Children.Add(r);
            foot.Children.Add(g);
            body.Children.Add(AsFoot(foot, e, now));
        }
        return body;
    }

    static Geometry Poly(params Point[] pts)
    {
        var g = new StreamGeometry();
        using (var x = g.Open()) { x.BeginFigure(pts[0], true, true); foreach (var p in pts.Skip(1)) x.LineTo(p, true, false); }
        g.Freeze();
        return g;
    }

    /// <summary>A line on a plate of its own, for B (which has no panel).</summary>
    static FrameworkElement Plain(UIElement child, double width) => new Border
    {
        Child = child, Width = width, Margin = new Thickness(0, 6, 0, 0), CornerRadius = new CornerRadius(2), Background = S(Rgb(0x0C0E14), .9),
        Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 10, ShadowDepth = 2, Direction = 270, Opacity = .5 },
    };

    /// <summary>B's row: the rank plate, the bar (a dark track, the class's colour slanted at its head), the score tab.</summary>
    sealed class BroadcastRow : IThemeRow
    {
        public const double H = 30, Pitch = 34, RankW = 30, Gap = 3, Lean = 8, NameIn = 11;
        readonly double _w, _dpsCol, _shareCol, _scoreW;
        readonly Grid _root = new() { Height = H, Background = Brushes.Transparent };
        readonly TextBlock _rank = T("", 15, Ink, FontWeights.Bold), _dps = T("", 17, Ink, FontWeights.Bold), _share = T("", 10.5, Soft, FontWeights.SemiBold);
        readonly NameLine _line = new(13.5, 10.5, 11.5, true);
        readonly Sketch _art;
        MeterMember? _m;
        double _bar;
        public FrameworkElement Root => _root;

        public BroadcastRow(double w)
        {
            _w = w; _root.Width = w;
            _dpsCol = TW(_dps, "888.8K", "8.88M", "888,888"); _shareCol = TW(_share, "88.8%", "100%", L.T("CP"));
            _scoreW = 10 + _dpsCol + 7 + _shareCol + 9;
            foreach (var cw in new[] { RankW, Gap, double.NaN, Gap, 10 + _dpsCol, 7 + _shareCol + 9 })
                _root.ColumnDefinitions.Add(new ColumnDefinition { Width = double.IsNaN(cw) ? new GridLength(1, GridUnitType.Star) : new GridLength(cw) });
            _art = new Sketch(Draw) { Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 9, ShadowDepth = 2, Direction = 270, Opacity = .55 } };
            Grid.SetColumnSpan(_art, 6); _root.Children.Add(_art);
            _rank.HorizontalAlignment = HorizontalAlignment.Center; _root.Children.Add(_rank);
            _line.Line.Margin = new Thickness(NameIn, 0, 0, 0); Grid.SetColumn(_line.Line, 2); _root.Children.Add(_line.Line);
            _dps.HorizontalAlignment = HorizontalAlignment.Right; _dps.Tag = "row-dps"; Grid.SetColumn(_dps, 4); _root.Children.Add(_dps);
            _share.HorizontalAlignment = HorizontalAlignment.Right; _share.Margin = new Thickness(0, 1, 9, 0); Grid.SetColumn(_share, 5); _root.Children.Add(_share);
        }

        public void Set(MeterMember m, MeterContext c, bool fresh)
        {
            _m = m;
            _rank.Text = m.Rank.ToString(CultureInfo.InvariantCulture);
            _rank.Foreground = S(m.You ? Rgb(0x2A1A00) : Mix(m.ClassColor, Colors.White, m.Rank <= 3 ? .45 : .2));
            _line.Set(m, Ink);
            _dps.Text = m.Figure; _share.Text = m.ShareText;
            _bar = BarOf(m, c);
            double room = _w - RankW - 2 * Gap - _scoreW - NameIn - Lean - 6;
            _line.Fit(room);
            _art.InvalidateVisual();
        }

        void Draw(DrawingContext dc, Size s)
        {
            if (_m is not { } m || s.Width < 60) return;
            var cls = m.ClassColor; var deep = Deep(cls);
            double h = s.Height, bx = RankW + Gap, sx = s.Width - _scoreW, bw = sx - Gap - bx;
            // the rank plate (gold for you)
            var plate = new Rect(0, 0, RankW, h);
            dc.DrawRectangle(m.You ? G(true, (Rgb(0xFFE08A), 0), (Rgb(0xF2B53A), .55), (Rgb(0xC98A1C), 1)) : G(true, (A(Rgb(0x1C202A), .96), 0), (A(Rgb(0x0B0D12), .97), 1)), null, plate);
            dc.DrawRectangle(S(Colors.White, m.You ? .55 : .14), null, new Rect(0, 0, RankW, 1));
            if (!m.You) dc.DrawRectangle(S(cls, .9), null, new Rect(0, h - 2, RankW, 2));
            // the bar: a dark track, the class's colour to the member's part of the top, slanted at its head
            var track = new Rect(bx, 0, bw, h);
            dc.DrawRectangle(G(true, (A(Rgb(0x10131A), .80), 0), (A(Rgb(0x07080C), .84), 1)), null, track);
            double fx = _bar <= 0 ? 0 : Math.Max(Lean + 12, bw * _bar);
            if (fx > 0)
            {
                var fill = Poly(new Point(bx, 0), new Point(bx + fx, 0), new Point(bx + fx - Lean, h), new Point(bx, h));
                dc.PushClip(new RectangleGeometry(track));
                dc.DrawGeometry(G(false, (Jewel(cls, .42), 0), (deep, .65), (Jewel(cls, .85), 1)), null, fill);
                dc.PushClip(fill);
                dc.DrawRectangle(G(true, (A(Colors.White, .30), 0), (A(Colors.White, .10), .30), (A(Colors.White, 0), .42), (A(Colors.Black, 0), .6), (A(Colors.Black, .32), 1)), null, track);
                dc.Pop();
                // the slanted head: a bright edge
                var lit = Mix(cls, Colors.White, .7);
                dc.DrawLine(P(lit, 2), new Point(bx + fx - 1, 0), new Point(bx + fx - Lean - 1, h));
                dc.Pop();
            }
            dc.DrawRectangle(S(Colors.White, .10), null, new Rect(bx, 0, bw, 1));
            if (m.You) dc.DrawRectangle(G(false, (Gold, 0), (A(Gold, .5), 1)), null, new Rect(bx, h - 2, bw, 2));
            // the score tab
            var tab = new Rect(sx, 0, _scoreW, h);
            dc.DrawRectangle(G(true, (A(Rgb(0x1C202A), .96), 0), (A(Rgb(0x0B0D12), .97), 1)), null, tab);
            dc.DrawRectangle(S(Colors.White, .14), null, new Rect(sx, 0, _scoreW, 1));
            dc.DrawRectangle(S(m.You ? Gold : cls), null, new Rect(sx, 0, 2, h));
        }
    }

    // ================================================================ C, Marquee

    /// <summary>
    /// C, Marquee: a matte panel lit from above. The header: the state (the HP's % and what's left, or KILLED in mint) small over the
    /// boss's name, the clock large on the right, the boss's HP a thick glossy bar the panel's width with its thirds notched. Each
    /// member on two tiers: the name and its marks over a thick bar that glows in the class's colour, the rank large at the left,
    /// the DPS large on the right with the share under it. The foot: TOTAL and the group, each a label over its figure.
    /// </summary>
    public static FrameworkElement Marquee(Encounter e, DateTime now, double width)
    {
        const double px = 14;
        var body = new StackPanel();
        var head = new Grid { Margin = new Thickness(16, 12, 16, 0) };
        head.ColumnDefinitions.Add(new ColumnDefinition()); head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        double? hp = HpOf(e);
        if (hp is { } h0) left.Children.Add(HpWords(e, h0, 11));
        else if (Outcome(e) is { } o)
        {
            var st = T("", 11, o.Tone, FontWeights.Bold);
            st.Inlines.Add(new Run("● ") { FontSize = 8 });
            st.Inlines.Add(new Run(Caps(o.Word)));
            st.Effect = new DropShadowEffect { Color = o.Tone, BlurRadius = 8, ShadowDepth = 0, Opacity = .45 };
            left.Children.Add(st);
        }
        else if (e.Waiting) left.Children.Add(T(Caps(L.T("Ready")), 10, Dim, FontWeights.Bold));
        var title = T(e.Title, 16.5, Ink, FontWeights.Bold); title.TextTrimming = TextTrimming.CharacterEllipsis; title.Margin = new Thickness(0, 1, 0, 0);
        left.Children.Add(title);
        head.Children.Add(left);
        if (!e.Waiting)
        {
            var clock = T(ClockOf(e, now), 25, e.Live ? Gold : Ink, FontWeights.Bold); clock.Margin = new Thickness(14, 0, 0, 0); clock.VerticalAlignment = VerticalAlignment.Bottom;
            if (e.Live) clock.Effect = new DropShadowEffect { Color = GoldDeep, BlurRadius = 12, ShadowDepth = 0, Opacity = .5 };
            Grid.SetColumn(clock, 1); head.Children.Add(clock);
        }
        body.Children.Add(head);
        if (hp is { } h1) body.Children.Add(new Sketch((dc, s) => DrawHp(dc, new Rect(0, 2, s.Width, 10), h1, 5, A(Colors.Black, .5), 2.5)) { Height = 14, Margin = new Thickness(16, 8, 16, 0) });
        else body.Children.Add(new Border { Height = 1, Margin = new Thickness(px, 11, px, 0), Background = G(false, (A(Colors.White, 0), 0), (A(Colors.White, .16), .5), (A(Colors.White, 0), 1)) });
        double rowW = width - 2 * px;
        if (e.Waiting && !HudStackLook.PartyWaiting(e)) body.Children.Add(ReadyLine(e, new Thickness(16, 10, 16, 12)));
        else
        {
            if (e.Waiting) body.Children.Add(ReadyLine(e, new Thickness(16, 8, 16, 0)));
            var rows = Rows(e, now, MarqueeRow.Pitch, m => new MarqueeRow(rowW), 16, l => GapLine(l, Soft));
            rows.Margin = new Thickness(px, 12, px, 0);
            body.Children.Add(rows);
        }
        if (e.HasDamage && !e.Waiting)
        {
            var (total, word, value, held) = FootOf(e, now);
            body.Children.Add(new Border { Height = 1, Margin = new Thickness(px, 6, px, 0), Background = G(false, (A(Colors.White, 0), 0), (A(Colors.White, .14), .5), (A(Colors.White, 0), 1)) });
            var foot = new Grid { Margin = new Thickness(16, 9, 16, 12) };
            foot.ColumnDefinitions.Add(new ColumnDefinition()); foot.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var l = new StackPanel();
            l.Children.Add(T(Caps(L.T("TOTAL")), 9, Dim, FontWeights.Bold));
            var tv = T(total, 17, Ink, FontWeights.Bold); tv.Margin = new Thickness(0, 1, 0, 0); l.Children.Add(tv);
            foot.Children.Add(l);
            var r = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(12, 0, 0, 0) };
            var lab = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Height = 13 };
            if (word.Length > 0) lab.Children.Add(T(Caps(word), 9, Dim, FontWeights.Bold));
            if (held) { var b = ParserFoot.AvgBadge(); b.Margin = new Thickness(6, 0, 0, 0); lab.Children.Add(b); }
            r.Children.Add(lab);
            if (word.Length > 0) { var rv = T(value, 17, Gold, FontWeights.Bold); rv.Tag = "foot-right"; rv.HorizontalAlignment = HorizontalAlignment.Right; rv.Margin = new Thickness(0, 1, 0, 0); r.Children.Add(rv); }
            Grid.SetColumn(r, 1); foot.Children.Add(r);
            body.Children.Add(AsFoot(foot, e, now));
        }
        else body.Children.Add(new Border { Height = 12 });
        return Glass(body, width, 14, G(true, (A(Rgb(0x1D212C), .95), 0), (A(Rgb(0x0E1117), .96), 1)), G(true, (A(Colors.White, .17), 0), (A(Colors.White, .04), 1)));
    }

    /// <summary>C's row: the rank large; the name and its marks over a thick glowing bar; the DPS large with the share under it.</summary>
    sealed class MarqueeRow : IThemeRow
    {
        public const double H = 40, Pitch = 44, RankW = 30, BarY = 22, BarH = 14;
        readonly double _w, _figCol;
        readonly Grid _root = new() { Height = H, Background = Brushes.Transparent };
        readonly TextBlock _rank = T("", 20, Ink, FontWeights.Bold), _dps = T("", 20, Ink, FontWeights.Bold), _share = T("", 10.5, Dim, FontWeights.SemiBold);
        readonly NameLine _line = new(14, 10.5, 11.5, false);
        readonly Sketch _art;
        MeterMember? _m;
        double _bar;
        public FrameworkElement Root => _root;

        public MarqueeRow(double w)
        {
            _w = w; _root.Width = w;
            _figCol = TW(_dps, "888.8K", "8.88M") + 14;
            foreach (var cw in new[] { RankW, double.NaN, _figCol })
                _root.ColumnDefinitions.Add(new ColumnDefinition { Width = double.IsNaN(cw) ? new GridLength(1, GridUnitType.Star) : new GridLength(cw) });
            _art = new Sketch(Draw); Grid.SetColumnSpan(_art, 3); _root.Children.Add(_art);
            _rank.HorizontalAlignment = HorizontalAlignment.Left; _rank.Margin = new Thickness(4, 0, 0, 0); _root.Children.Add(_rank);
            _line.Line.VerticalAlignment = VerticalAlignment.Top; _line.Line.Margin = new Thickness(0, 1, 0, 0); _line.Line.Height = 19;
            Grid.SetColumn(_line.Line, 1); Grid.SetColumnSpan(_line.Line, 2); _root.Children.Add(_line.Line);
            _dps.HorizontalAlignment = HorizontalAlignment.Right; _dps.VerticalAlignment = VerticalAlignment.Top; _dps.Margin = new Thickness(0, -1, 2, 0); _dps.Tag = "row-dps";
            Grid.SetColumn(_dps, 1); Grid.SetColumnSpan(_dps, 2); _root.Children.Add(_dps);
            _share.HorizontalAlignment = HorizontalAlignment.Right; _share.VerticalAlignment = VerticalAlignment.Bottom; _share.Margin = new Thickness(0, 0, 2, 1);
            Grid.SetColumn(_share, 2); _root.Children.Add(_share);
        }

        public void Set(MeterMember m, MeterContext c, bool fresh)
        {
            _m = m;
            _rank.Text = m.Rank.ToString(CultureInfo.InvariantCulture);
            _rank.Foreground = S(m.You ? Gold : m.Rank <= 3 && !m.CpOnly ? Mix(m.ClassColor, Colors.White, .4) : Dim);
            if (m.You) _rank.Effect = new DropShadowEffect { Color = GoldDeep, BlurRadius = 10, ShadowDepth = 0, Opacity = .6 };
            _line.Set(m, Ink);
            _dps.Text = m.Figure; _share.Text = m.ShareText;
            _bar = BarOf(m, c);
            // the name's line runs over the bar's tier as far as the DPS leaves free
            double room = _w - RankW - 2 - W(_dps) - 12;
            _line.Fit(room);
            _art.InvalidateVisual();
        }

        void Draw(DrawingContext dc, Size s)
        {
            if (_m is not { } m || s.Width < 60) return;
            var cls = m.ClassColor; var deep = Deep(cls);
            // you: a gold wash from the left and a gold edge
            if (m.You)
            {
                var wash = new Rect(-6, -3, s.Width + 12, s.Height + 6);
                dc.DrawRoundedRectangle(G(false, (A(Gold, .17), 0), (A(Gold, .04), .7), (A(Gold, .0), 1)), P(A(Gold, .28), 1), wash, 8, 8);
                dc.DrawRoundedRectangle(S(Gold), null, new Rect(-6, 3, 2.5, s.Height - 6), 1.2, 1.2);
            }
            double bx = RankW, bw = s.Width - RankW - _figCol;
            var track = new Rect(bx, BarY, bw, BarH);
            const double r = BarH / 2;
            dc.DrawRoundedRectangle(S(Colors.White, .07), P(A(Colors.Black, .45), 1), track, r, r);
            double fx = _bar <= 0 ? 0 : Math.Max(BarH + 2, bw * _bar);
            if (fx > 0)
            {
                var fr = new Rect(bx, BarY, fx, BarH);
                // the light the bar throws on the panel under it
                dc.DrawRectangle(G(true, (A(cls, .22), 0), (A(cls, 0), 1)), null, new Rect(bx + 4, BarY + BarH, Math.Max(0, fx - 8), 5));
                for (int i = 2; i >= 1; i--) dc.DrawRoundedRectangle(S(cls, .08 * (3 - i)), null, new Rect(fr.X - i, fr.Y - i, fr.Width + 2 * i, fr.Height + 2 * i), r + i, r + i);
                dc.PushClip(new RectangleGeometry(fr, r, r));
                dc.DrawRectangle(G(false, (Jewel(cls, .45), 0), (deep, .55), (Jewel(cls, .95), 1)), null, fr);
                dc.DrawRectangle(G(true, (A(Colors.White, .42), 0), (A(Colors.White, .10), .38), (A(Colors.White, 0), .46), (A(Colors.Black, .28), 1)), null, fr);
                dc.DrawRectangle(G(false, (A(Colors.White, 0), 0), (A(Colors.White, .45), 1)), null, new Rect(bx + fx - Math.Min(16, fx), BarY, Math.Min(16, fx), BarH));
                dc.Pop();
            }
        }
    }
}
