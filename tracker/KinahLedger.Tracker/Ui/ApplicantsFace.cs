using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using KinahLedger.Session;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

/// <summary>
/// The Applicants window's face (Steven, 2026-11-18: "make sure it fits the themes"): the people asking to join the group,
/// drawn in the look family of the overlay's theme (Settings › Buddy › Theme; HudThemes.Of the look in use) with that
/// family's own parts: Classic the Kinah strip's surface (the glass when it is on), Tactical a cut glass plate with brackets
/// (Tac), Wacky a sticker with a hard shadow and outlined words (Wk), Glitch a phosphor terminal with scan lines and corner
/// marks (GlitchKit), Modern a soft frosted panel (Mod). One row per applicant, oldest at the top, newest at the bottom: the
/// class's emblem, the name in its class's colour, the home server, then the level, the Combat Power and how long ago they
/// asked. Reading only: the game keeps accepting and declining.
/// </summary>
static class ApplicantsFace
{
    /// <summary>The rows shown at most; the oldest beyond them are counted on a line at the top.</summary>
    public const int MaxRows = 12;

    /// <summary>The face for <paramref name="theme"/> (a HudThemes key; Classic for any other) at the overlay's opacity.</summary>
    public static FrameworkElement Build(string theme, IReadOnlyList<PartyApplicants.Applicant> items, DateTime now, double opacity = 0.94)
    {
        var shown = items.Skip(Math.Max(0, items.Count - MaxRows)).ToList();
        int older = items.Count - shown.Count;
        FrameworkElement face = theme switch
        {
            HudThemes.Tactical => Tactical(shown, older, items.Count, now, opacity),
            HudThemes.Wacky => Wacky(shown, older, items.Count, now, opacity),
            HudThemes.Glitch => Glitch(shown, older, items.Count, now, opacity),
            HudThemes.Modern => Modern(shown, older, items.Count, now, opacity),
            _ => Classic(shown, older, items.Count, now, opacity),
        };
        face.Tag = "applicants:" + (theme is HudThemes.Tactical or HudThemes.Wacky or HudThemes.Glitch or HudThemes.Modern ? theme : HudThemes.Classic);
        TextOptions.SetTextFormattingMode(face, TextFormattingMode.Display);
        System.Windows.Automation.AutomationProperties.SetName(face, L.F("{0} asking to join", items.Count));
        return face;
    }

    /// <summary>The window's title: "Asking to join".</summary>
    static string Title => L.T("Asking to join");

    /* ---------------- the rows, the same columns in every theme ---------------- */

    /// <summary>How a theme writes a row: its faces, sizes and inks.</summary>
    sealed record RowInk(FontFamily Face, double Size, Brush Figure, Brush Label, Brush Dim, Brush Ago, FontWeight NameWeight, FontWeight FigWeight, bool Caps = false, double Icon = 18, bool ClassCode = false, Brush? CodeBg = null, Brush? CodeFg = null);

    static TextBlock Text(string s, double size, Brush fg, FontFamily face, FontWeight w)
    {
        var t = new TextBlock { Text = s, FontSize = size, Foreground = fg, FontFamily = face, FontWeight = w, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.None };
        System.Windows.Documents.Typography.SetNumeralAlignment(t, FontNumeralAlignment.Tabular);
        return t;
    }

    /// <summary>A label and its figure on one baseline ("Lv 45", "CP 133.8K").</summary>
    static TextBlock Labelled(string label, string fig, RowInk k)
    {
        var t = Text("", k.Size, k.Figure, k.Face, k.FigWeight);
        t.Inlines.Add(new System.Windows.Documents.Run((k.Caps ? label.ToUpperInvariant() : label) + " ") { Foreground = k.Label, FontSize = k.Size - 2, FontWeight = FontWeights.SemiBold });
        t.Inlines.Add(new System.Windows.Documents.Run(fig));
        return t;
    }

    /// <summary>
    /// The list: one grid row per applicant, its columns shared (icon, name and server, level, Combat Power, ago), the newest
    /// last. <paramref name="rowGround"/> draws a row's own ground (null: none); <paramref name="rule"/> a hairline between rows.
    /// </summary>
    static Grid Rows(IReadOnlyList<PartyApplicants.Applicant> rows, DateTime now, RowInk k, Func<int, bool, Brush?>? rowGround = null, Brush? rule = null, double rowPad = 3, CornerRadius? rowCorner = null)
    {
        var g = new Grid();
        foreach (var w in new[] { GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto, GridLength.Auto })
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = w });
        for (int i = 0; i < rows.Count; i++)
        {
            var a = rows[i];
            bool newest = i == rows.Count - 1;
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            if (rowGround?.Invoke(i, newest) is { } bg)
            {
                var ground = new Border { Background = bg, CornerRadius = rowCorner ?? new CornerRadius(0), Margin = new Thickness(-4, 1, -4, 1), IsHitTestVisible = false };
                Grid.SetRow(ground, i); Grid.SetColumnSpan(ground, 5); g.Children.Add(ground);
            }
            if (rule != null && i > 0)
            {
                var line = new Border { Height = 1, Background = rule, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false };
                Grid.SetRow(line, i); Grid.SetColumnSpan(line, 5); g.Children.Add(line);
            }
            var pad = new Thickness(0, rowPad, 0, rowPad);

            // the class: its emblem (a quiet disc with the initial when the class isn't known), the code tag on the terminal
            var icon = ThemeText.Icon(a.Class, k.Icon, a.Name);
            FrameworkElement cls = icon;
            if (k.ClassCode && GlitchKit.ClassCode(a.Class) is { Length: > 0 } code)
                cls = new StackPanel { Orientation = Orientation.Horizontal, Children = { icon, GlitchKit.Tag(code, k.CodeBg ?? k.Label, k.CodeFg ?? Brushes.Black, k.Face, k.Size - 3) } };
            cls.Margin = new Thickness(0, rowPad, 8, rowPad);
            cls.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetRow(cls, i); g.Children.Add(cls);

            // the name in its class's colour, the server after it, quieter
            var who = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = pad, FontFamily = k.Face, FontSize = k.Size + 0.5, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 260 };
            who.Inlines.Add(new System.Windows.Documents.Run(a.Name) { Foreground = ClassArt.NameBrush(a.Class), FontWeight = k.NameWeight });
            if (a.Server.Length > 0) who.Inlines.Add(new System.Windows.Documents.Run("  " + (k.Caps ? a.Server.ToUpperInvariant() : a.Server)) { Foreground = k.Dim, FontSize = k.Size - 1.5, FontWeight = FontWeights.Medium });
            who.ToolTip = string.Join("  ·  ", new[] { a.Name, ThemeText.Class(a.Class), a.Server, a.ItemLevel > 0 ? L.F("item level {0:N0}", a.ItemLevel) : "" }.Where(s => s.Length > 0));
            Grid.SetRow(who, i); Grid.SetColumn(who, 1); g.Children.Add(who);

            // Lv 45 · CP 133.8K · 2m
            var lv = Labelled(L.T("Lv"), a.Level > 0 ? a.Level.ToString(System.Globalization.CultureInfo.InvariantCulture) : "–", k);
            lv.Margin = new Thickness(14, rowPad, 0, rowPad); lv.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetRow(lv, i); Grid.SetColumn(lv, 2); g.Children.Add(lv);
            var cp = Labelled(L.T("CP"), a.CombatPower > 0 ? Amounts.Meter(a.CombatPower) : "–", k);
            cp.Margin = new Thickness(12, rowPad, 0, rowPad); cp.HorizontalAlignment = HorizontalAlignment.Right;
            if (a.CombatPower > 0) cp.ToolTip = Amounts.Full(a.CombatPower);
            Grid.SetRow(cp, i); Grid.SetColumn(cp, 3); g.Children.Add(cp);
            var ago = Text(PartyApplicants.Ago(a.At, now), k.Size - 0.5, newest ? k.Ago : k.Dim, k.Face, FontWeights.SemiBold);
            ago.Margin = new Thickness(12, rowPad, 0, rowPad); ago.HorizontalAlignment = HorizontalAlignment.Right; ago.TextAlignment = TextAlignment.Right; ago.MinWidth = 26;
            Grid.SetRow(ago, i); Grid.SetColumn(ago, 4); g.Children.Add(ago);
        }
        g.Tag = "applicant-rows";
        return g;
    }

    /// <summary>"3 more asked earlier" over the rows when more than <see cref="MaxRows"/> wait.</summary>
    static TextBlock? Older(int older, RowInk k) => older <= 0 ? null : new TextBlock
    {
        Text = L.F("{0} more asked earlier", older), FontFamily = k.Face, FontSize = k.Size - 1.5, Foreground = k.Dim, Margin = new Thickness(0, 0, 0, 3),
    };

    /* ---------------- Classic: the Kinah strip's surface ---------------- */

    static FrameworkElement Classic(IReadOnlyList<PartyApplicants.Applicant> rows, int older, int total, DateTime now, double opacity)
    {
        var k = new RowInk(HudKit.Text, 12, Ink, Faint, Muted, Gold, FontWeights.SemiBold, FontWeights.SemiBold);
        var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 5) };
        head.Children.Add(new System.Windows.Shapes.Ellipse { Width = 6, Height = 6, Fill = Gold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        head.Children.Add(Text(Title, 12, Ink, HudKit.Text, FontWeights.Bold));
        head.Children.Add(new Border
        {
            Background = Frozen(Color.FromArgb(0x24, 0xF5, 0xB9, 0x42)), BorderBrush = Frozen(Color.FromArgb(0x8C, 0xF5, 0xB9, 0x42)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4),
            Padding = new Thickness(5, 0, 5, 0), Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
            Child = Text(total.ToString(System.Globalization.CultureInfo.InvariantCulture), 11, Gold, HudKit.Text, FontWeights.Bold),
        });
        var body = new StackPanel { Children = { head } };
        if (Older(older, k) is { } o) body.Children.Add(o);
        body.Children.Add(Rows(rows, now, k, rule: HudGlass.Rule));
        var frame = new Border
        {
            Background = Frozen(Color.FromArgb((byte)Math.Round(255 * Math.Clamp(opacity, 0.3, 1)), 0x0B, 0x0C, 0x10)), BorderBrush = FrameLine, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10), Padding = new Thickness(11, 8, 12, 8), Child = body,
        };
        if (!HudGlass.On) return frame;
        HudGlass.Apply(frame, opacity);
        return HudGlass.Ring(frame);
    }

    /* ---------------- Tactical: a cut glass plate, brackets, phosphor and amber ---------------- */

    static readonly Color TacPhos = Color.FromRgb(0x7C, 0xFF, 0x9E), TacAmber = Color.FromRgb(0xFF, 0xB8, 0x3D), TacGround = Color.FromRgb(0x01, 0x0A, 0x04), TacPale = Color.FromRgb(0xDC, 0xFF, 0xE6);

    static FrameworkElement Tactical(IReadOnlyList<PartyApplicants.Applicant> rows, int older, int total, DateTime now, double opacity)
    {
        var face = HudThemes.Mono;
        var k = new RowInk(face, 11.5, Frozen(TacPale), Frozen(Tac.A(TacPhos, .66)), Frozen(Tac.A(TacPhos, .55)), Frozen(TacAmber), FontWeights.Bold, FontWeights.Bold, Caps: true, Icon: 17);
        // REQ // ASKING TO JOIN ------------------- 04
        var head = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); head.ColumnDefinitions.Add(new ColumnDefinition()); head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.Children.Add(new Border { Background = Frozen(TacPhos), Padding = new Thickness(4, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center,
            Child = Tac.Text(Title.ToUpperInvariant(), 10, Frozen(TacGround), face, FontWeights.Bold) });
        var dash = new TacSketch((dc, s) => dc.DrawLine(Tac.P(Frozen(Tac.A(TacPhos, .3)), 1, dash: new DashStyle(new double[] { 2, 3 }, 0)), new Point(0, Math.Round(s.Height / 2) + .5), new Point(s.Width, Math.Round(s.Height / 2) + .5))) { Margin = new Thickness(8, 0, 8, 0), MinWidth = 24 };
        Grid.SetColumn(dash, 1); head.Children.Add(dash);
        var count = Tac.Text(total.ToString("00", System.Globalization.CultureInfo.InvariantCulture), 14, Frozen(TacAmber), face, FontWeights.Bold);
        Grid.SetColumn(count, 2); head.Children.Add(count);
        var body = new StackPanel { Children = { head } };
        if (Older(older, k) is { } o) body.Children.Add(o);
        // the newest row is the one locked: a faint amber wash, as the Fighter's target
        body.Children.Add(Rows(rows, now, k, (i, newest) => newest ? Frozen(Tac.A(TacAmber, .10)) : null, Frozen(Tac.A(TacPhos, .14)), rowPad: 3));
        var content = new Border { Padding = new Thickness(13, 9, 14, 10), Child = body };
        // the plate: dark glass with its corners cut, a phosphor hairline, brackets on the two uncut corners
        double a = Math.Clamp(opacity, 0.3, 1);
        var plate = new TacSketch((dc, s) =>
        {
            var r = new Rect(1, 1, Math.Max(1, s.Width - 2), Math.Max(1, s.Height - 2));   // the brackets' 1.6 px pen stays inside
            dc.DrawGeometry(Frozen(Tac.A(TacGround, a * 0.9)), Tac.P(Frozen(Tac.A(TacPhos, .35)), 1), Tac.Cut(r, 10));
            var br = Tac.P(Frozen(TacPhos), 1.6);
            double L0 = 9;
            dc.DrawLine(br, new Point(r.Right, r.Top), new Point(r.Right - L0, r.Top)); dc.DrawLine(br, new Point(r.Right, r.Top), new Point(r.Right, r.Top + L0));
            dc.DrawLine(br, new Point(r.Left, r.Bottom), new Point(r.Left + L0, r.Bottom)); dc.DrawLine(br, new Point(r.Left, r.Bottom), new Point(r.Left, r.Bottom - L0));
        });
        return new Grid { Children = { plate, content }, HorizontalAlignment = HorizontalAlignment.Left };
    }

    /* ---------------- Wacky: a sticker with a hard shadow and outlined words ---------------- */

    const uint WkYel = 0xFFE14D, WkInk = 0x111111, WkPink = 0xFF3DA6, WkCyan = 0x3DF5C8, WkCard = 0x141414, WkPaper = 0xFFF8E8;

    static FrameworkElement Wacky(IReadOnlyList<PartyApplicants.Applicant> rows, int older, int total, DateTime now, double opacity)
    {
        var k = new RowInk(Wk.Segoe, 12, Wk.S(WkPaper), Wk.S(WkYel), Wk.S(WkPaper, .62), Wk.S(WkCyan), FontWeights.Black, FontWeights.Bold);
        // the title as a tilted yellow sticker, the count in a pink burst
        var title = Wk.Tilt(Wk.Outlined(Title.ToUpperInvariant(), Wk.Heavy, FontWeights.Black, 15, Wk.S(WkYel), Wk.S(WkInk), 2.6, shadow: Wk.S(WkInk), shadowAt: 2), -2.5);
        var burst = Wk.Burst(15, 13, 11, .72, 7, Wk.S(WkPink), Wk.S(WkInk), 2, 2, Wk.Words(total.ToString(System.Globalization.CultureInfo.InvariantCulture), 13, Wk.S(0xFFFFFF), Wk.Heavy, FontWeights.Black));
        burst.Margin = new Thickness(8, -4, 0, -2); burst.VerticalAlignment = VerticalAlignment.Center;
        var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6), Children = { title, burst } };
        var body = new StackPanel { Children = { head } };
        if (Older(older, k) is { } o) body.Children.Add(o);
        // rows on chunky dark pills, the newest one pinker
        body.Children.Add(Rows(rows, now, k, (i, newest) => newest ? Wk.S(WkPink, .26) : Wk.S(0xFFFFFF, .06), null, rowPad: 4, rowCorner: new CornerRadius(8)));
        double a = Math.Clamp(opacity, 0.3, 1);
        var card = new Border { Background = Wk.S(WkCard, a), BorderBrush = Wk.S(WkInk), BorderThickness = new Thickness(3), CornerRadius = new CornerRadius(14), Padding = new Thickness(12, 8, 14, 10), Child = body };
        // the hard offset shadow under it
        var shadow = new Border { Background = Wk.S(WkInk, .9), CornerRadius = new CornerRadius(14), Margin = new Thickness(5, 5, 0, 0), IsHitTestVisible = false };
        card.Margin = new Thickness(0, 0, 5, 5);
        return new Grid { HorizontalAlignment = HorizontalAlignment.Left, Children = { shadow, card } };
    }

    /* ---------------- Glitch: a phosphor terminal, scan lines and corner marks ---------------- */

    const uint GlPhos = 0x5CFF9A, GlPale = 0xD6FFE4, GlAmber = 0xFFB641, GlGround = 0x031008;

    static FrameworkElement Glitch(IReadOnlyList<PartyApplicants.Applicant> rows, int older, int total, DateTime now, double opacity)
    {
        var face = GlitchKit.CrtFace;
        var k = new RowInk(face, 11.5, GlitchKit.Br(GlPale), GlitchKit.Br(GlPhos, .7), GlitchKit.Br(GlPhos, .55), GlitchKit.Br(GlAmber), FontWeights.Bold, FontWeights.Bold, Caps: true, Icon: 16, ClassCode: true, CodeBg: GlitchKit.Br(GlPhos, .85), CodeFg: GlitchKit.Br(GlGround));
        // > ASKING TO JOIN [04]_
        var head = GlitchKit.Phosphor("> " + Title.ToUpperInvariant() + " [" + total.ToString("00", System.Globalization.CultureInfo.InvariantCulture) + "]", 12, face, FontWeights.Bold, GlitchKit.Br(GlPhos), Rgb(GlPhos));
        head.Margin = new Thickness(0, 0, 0, 6); head.HorizontalAlignment = HorizontalAlignment.Left;
        var body = new StackPanel { Children = { head } };
        if (Older(older, k) is { } o) body.Children.Add(o);
        body.Children.Add(Rows(rows, now, k, (i, newest) => newest ? GlitchKit.Br(GlPhos, .10) : null, GlitchKit.Br(GlPhos, .12), rowPad: 3));
        double a = Math.Clamp(opacity, 0.3, 1);
        var screen = new Border { Background = GlitchKit.Br(GlGround, a * 0.94), BorderBrush = GlitchKit.Br(GlPhos, .28), BorderThickness = new Thickness(1), Padding = new Thickness(12, 9, 14, 10), Child = body };
        // the scan lines over it (never hit) and a camcorder's corner marks
        var lines = new Border { Background = GlitchKit.Lines(Color.FromArgb(0x1C, 0x00, 0x00, 0x00), 3, 1), IsHitTestVisible = false };
        var corners = GlitchKit.Corners(GlitchKit.Br(GlPhos, .8), 8, 1.5, 3);
        return new Grid { HorizontalAlignment = HorizontalAlignment.Left, Children = { screen, lines, corners } };
    }

    /* ---------------- Modern: soft frosted glass, one accent ---------------- */

    static readonly Color ModTint = Color.FromRgb(0x12, 0x16, 0x1E), ModDeep = Color.FromRgb(0x06, 0x08, 0x0C), ModGreen = Color.FromRgb(0x5D, 0xF2, 0xA6);

    static FrameworkElement Modern(IReadOnlyList<PartyApplicants.Applicant> rows, int older, int total, DateTime now, double opacity)
    {
        var k = new RowInk(Mod.Face, 12, Mod.Br(Colors.White, .92), Mod.Br(Colors.White, .45), Mod.Br(Colors.White, .58), Frozen(ModGreen), FontWeights.SemiBold, FontWeights.SemiBold, Icon: 18);
        var head = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        head.ColumnDefinitions.Add(new ColumnDefinition()); head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.Children.Add(Mod.Text(Title, 12.5, Mod.Br(Colors.White, .85), FontWeights.SemiBold));
        var pill = new Border { Background = Mod.Br(ModGreen, .16), BorderBrush = Mod.Br(ModGreen, .45), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(9), Padding = new Thickness(7, 0, 7, 0), Margin = new Thickness(14, 0, 0, 0),
            Child = Mod.Text(total.ToString(System.Globalization.CultureInfo.InvariantCulture), 11, Frozen(ModGreen), FontWeights.Bold) };
        Grid.SetColumn(pill, 1); head.Children.Add(pill);
        var body = new StackPanel { Children = { head } };
        if (Older(older, k) is { } o) body.Children.Add(o);
        body.Children.Add(Rows(rows, now, k, (i, newest) => newest ? Mod.Br(ModGreen, .09) : null, Mod.Br(Colors.White, .07), rowPad: 3.5, rowCorner: new CornerRadius(8)));
        double a = Math.Clamp(opacity, 0.3, 1);
        return new Border
        {
            Background = Mod.Grad(true, (Mod.A(ModTint, a * 0.82), 0), (Mod.A(ModDeep, a * 0.9), 1)), BorderBrush = Mod.Br(Colors.White, .14), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16), Padding = new Thickness(14, 10, 15, 11), Child = body, HorizontalAlignment = HorizontalAlignment.Left,
        };
    }

    /* ---------------- samples, for the renders and the tests ---------------- */

    /// <summary>
    /// <paramref name="n"/> made-up applicants, oldest first, at their widest real forms: a 16-character name, CP from "98,410" to
    /// "188.4K", every class, a server of 11 letters, the newest a few seconds ago and the oldest minutes ago.
    /// </summary>
    public static List<PartyApplicants.Applicant> Sample(int n, DateTime now)
    {
        var pool = new (string Name, string Class, long Cp, string Server)[]
        {
            ("Thornecrestwarde", "Templar", 188_420, "Meslamtaeda"), ("Rezo", "Gladiator", 128_195, "Israphel"), ("mz", "Gladiator", 133_774, "Zikel"),
            ("RubyDAD", "Sorcerer", 125_846, "Lumiel"), ("Aerinwynne", "Cleric", 151_906, "Siel"), ("Kaelith", "Assassin", 162_340, "Fregion"),
            ("Seraphimdawnlite", "Chanter", 165_432, "Ereshkigal"), ("Vintzi", "Ranger", 98_410, "Nezekan"), ("Goldlemon", "Spiritmaster", 141_344, "Siel"),
            ("Bode", "Cleric", 110_552, "Triniel"), ("Avenia", "Ranger", 115_076, "Siel"), ("Jonhiboi", "Chanter", 92_650, "Zikel"),
        };
        var list = new List<PartyApplicants.Applicant>();
        for (int i = 0; i < n; i++)
        {
            var p = pool[i % pool.Length];
            int secsAgo = n == 1 ? 12 : (int)Math.Round(4 + (n - 1 - i) * (420.0 / Math.Max(1, n - 1)));
            list.Add(new PartyApplicants.Applicant("s" + i, p.Name, p.Class, 45, 2_400 + (int)(p.Cp % 700), p.Cp, p.Server, now.AddSeconds(-secsAgo)));
        }
        return list;
    }
}
