using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace KinahLedger.Ui;

/// <summary>
/// How the panel over the game looks (Options › In game › Panel look, 2026-10-25): the Strip it has always been, the Card
/// (a game tooltip: run, rate, session and the last three pickups) or the Ticker (a small pill and pickups as toasts, the
/// way the game shows loot). The Strip stays the default, so nothing changes until a player picks another.
/// </summary>
public static class HudLooks
{
    public const string Strip = "strip", Card = "card", Ticker = "ticker";

    /// <param name="Key">Kept in hud.json (HudPrefs.Look).</param>
    public sealed record Look(string Key, string Name, string Hint);

    public static readonly Look[] All =
    {
        new(Strip, "Strip", "One line, with the figures you pick"),
        new(Card, "Card", "Run, rate, session and the last three pickups"),
        new(Ticker, "Ticker", "Pickups slide in like the game's loot"),
    };

    /// <summary>A saved look, or the Strip for anything unknown (an older or edited hud.json).</summary>
    public static string Parse(string? saved) => All.FirstOrDefault(l => string.Equals(l.Key, saved, StringComparison.OrdinalIgnoreCase))?.Key ?? Strip;

    /// <summary>How many pickups the window hands the looks ("pick0" to "pick3"): the Card shows three, the Ticker four.</summary>
    public const int Picks = 4;

    /// <summary>The Ticker's first place: this far left of the screen's centre (at 1x), clear of the target's name and health bar.</summary>
    public const double TickerLeftOfCentre = 560;
}

/// <summary>
/// A panel look the panel window hosts in place of its strip (the Card and the Ticker). Everything it shows comes in
/// through Update's values (the window's _hudValues, see MainWindow.HudPickValues), so it keeps no figures of its own.
/// </summary>
public interface IHudLook
{
    /// <summary>What the window shows; transparent around the look by <see cref="Inset"/>, so a glow has room.</summary>
    FrameworkElement Root { get; }
    /// <summary>The transparent room around the look itself (the rare drop's glow), so the move outline sits on its edge.</summary>
    Thickness Inset { get; }
    double Corner { get; }
    void Update(IReadOnlyDictionary<string, string> values);
    void SetAccuracy(string? text, bool toCheck);
    /// <summary>How solid the background is, 0.3 to 1, as the strip's (Options › In game › Panel opacity).</summary>
    void SetOpacity(double opacity);
}

/// <summary>
/// The looks' shared parts, drawn as the approved mock (kinah-bench/mock-hud/hud.css): Night's colours, item tiles with
/// grade frames and count badges (as the window's ItemTile), the run dot and the warning sign. Every brush is frozen:
/// the panel class is touched from the readers' threads (see HudWindow.Muted).
/// </summary>
static class HudKit
{
    public static SolidColorBrush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
    public static Color Rgb(uint rgb, double alpha = 1) => Color.FromArgb((byte)Math.Round(255 * Math.Clamp(alpha, 0, 1)), (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
    public static SolidColorBrush B(uint rgb, double alpha = 1) => Frozen(Rgb(rgb, alpha));

    public const uint InkC = 0xEEECF6, MutedC = 0xB3B7D3, FaintC = 0x7D84A8, GoldC = 0xF8D598, SecGoldC = 0xE9B45A, WarnC = 0xF2A35E,
        RecC = 0xFF6B5E, SlotC = 0x0B0E1E, LineC = 0x323A66, BadgeLineC = 0x4A5277, CommonC = 0xCDD3E2, PanelC = 0x0A0C18;

    public static readonly SolidColorBrush Ink = B(InkC), Muted = B(MutedC), Faint = B(FaintC), Gold = B(GoldC), Warn = B(WarnC),
        Rec = B(RecC), DotOff = B(0x5B6285), Slot = B(SlotC), FrameLine = B(LineC), BadgeLine = B(BadgeLineC), White = B(0xFFFFFF),
        SlotFrame = B(CommonC, .28), Sep = Frozen(Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF));

    public static readonly FontFamily Text = new("Segoe UI Variable Text, Segoe UI");
    public static readonly FontFamily Mono = new("Cascadia Mono, Consolas");
    /// <summary>The mock's mono weight (350): Cascadia's semi-light; Consolas has only one and ignores it.</summary>
    public static readonly FontWeight MonoWeight = FontWeight.FromOpenTypeWeight(350);

    /// <summary>The game's grade colours, as the window's (MainWindow.GradeColor): Rare green, Legend blue, Unique yellow, Epic violet.</summary>
    public static Color GradeColor(string grade) => grade switch
    {
        "Rare" => Rgb(0x62D98B), "Legend" => Rgb(0x5AA6FF), "Unique" => Rgb(0xF3D35E), "Epic" => Rgb(0xB48BFF), _ => Rgb(CommonC),
    };

    /// <summary>The grades a rare drop alert is for (MainWindow.AlertTick): their tiles glow.</summary>
    public static bool Glows(string grade) => grade is "Legend" or "Unique" or "Epic";

    /// <summary>"#5AA6FF" from the window's alert, else the grade's colour.</summary>
    public static Color ColorOf(string hex, string grade)
    {
        if (hex.Length == 7 && hex[0] == '#' && uint.TryParse(hex[1..], System.Globalization.NumberStyles.HexNumber, null, out var v)) return Rgb(v);
        return GradeColor(grade);
    }

    public static TextBlock T(string text, double size, Brush fg, bool mono = false, FontWeight? weight = null) => new()
    {
        Text = text, FontSize = size, Foreground = fg, FontFamily = mono ? Mono : Text, FontWeight = weight ?? (mono ? MonoWeight : FontWeights.Normal),
        VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
    };

    public static System.Windows.Documents.Run R(string text, double size, Brush fg, bool mono = false, FontWeight? weight = null) => new(text)
    {
        FontSize = size, Foreground = fg, FontFamily = mono ? Mono : Text, FontWeight = weight ?? (mono ? MonoWeight : FontWeights.Normal),
    };

    /// <summary>The run dot: red with a soft glow during a run, grey otherwise.</summary>
    public static Ellipse Dot(double size) => new()
    {
        Width = size, Height = size, Fill = DotOff, VerticalAlignment = VerticalAlignment.Center,
        Effect = new DropShadowEffect { Color = Rgb(RecC), BlurRadius = 7, ShadowDepth = 0, Opacity = 0 },
    };

    public static void SetDot(Ellipse dot, bool on)
    {
        dot.Fill = on ? Rec : DotOff;
        if (dot.Effect is DropShadowEffect e) e.Opacity = on ? 0.7 : 0;
    }

    /// <summary>A 1 px rule between figures.</summary>
    public static Border Rule(double height, double margin) => new() { Width = 1, Height = height, Background = Sep, Margin = new Thickness(margin, 0, margin, 0), VerticalAlignment = VerticalAlignment.Center };

    /// <summary>The warning sign the notice line starts with (the mock's 16-unit triangle), drawn once.</summary>
    public static readonly DrawingImage WarnSign = MakeWarn();
    static DrawingImage MakeWarn()
    {
        var warn = Rgb(WarnC);
        var g = new DrawingGroup();
        g.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, 16, 16))));
        g.Children.Add(new GeometryDrawing(Frozen(Rgb(WarnC, .16)), new Pen(Frozen(warn), 1.4) { LineJoin = PenLineJoin.Round }, Geometry.Parse("M8,1.8 L15,14 L1,14 Z")));
        g.Children.Add(new GeometryDrawing(null, new Pen(Frozen(warn), 1.6) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, Geometry.Parse("M8,6 L8,9.6")));
        g.Children.Add(new GeometryDrawing(Frozen(warn), null, new EllipseGeometry(new Point(8, 11.9), .95, .95)));
        var img = new DrawingImage(g); img.Freeze();
        return img;
    }

    public static Image WarnIcon() => new() { Source = WarnSign, Width = 15, Height = 15, VerticalAlignment = VerticalAlignment.Center };

    /// <summary>A glow around a rounded shape: a ring in the colour, blurred, drawn under the shape (the shape itself stays sharp).</summary>
    public static Border Glow(Color c, double radius, double blur, double opacity, double thickness = 1) => new()
    {
        BorderBrush = Frozen(c), BorderThickness = new Thickness(thickness), CornerRadius = new CornerRadius(radius), IsHitTestVisible = false,
        Effect = new DropShadowEffect { Color = c, BlurRadius = blur, ShadowDepth = 0, Opacity = opacity },
    };

    /// <summary>One pickup as the looks show it, from the window's "pick{i}…" values.</summary>
    public sealed record Pick(string Id, string Name, string Count, string Worth, string Grade, string Icon, string Note, bool Kinah, DateTime At, bool Rare = false, string Color = "");

    /// <summary>The pickups in the values, newest first (pick0 to pick3); empty names are no pickup.</summary>
    public static List<Pick> Picks(IReadOnlyDictionary<string, string> v)
    {
        string V(string k) => v.TryGetValue(k, out var s) ? s : "";
        var list = new List<Pick>();
        for (int i = 0; i < HudLooks.Picks; i++)
        {
            var p = "pick" + i;
            if (V(p).Length == 0) continue;
            var at = long.TryParse(V(p + "At"), out var ticks) && ticks > 0 ? new DateTime(ticks, DateTimeKind.Utc) : DateTime.MinValue;
            list.Add(new Pick(V(p + "Id").Length > 0 ? V(p + "Id") : V(p) + "|" + i, V(p), V(p + "Count"), V(p + "Worth"), V(p + "Grade"), V(p + "Icon"), V(p + "Note"), V(p + "Kind") == "kinah", at));
        }
        return list;
    }

    /// <summary>The rare drop the window holds for a few seconds ("rare" values), or null.</summary>
    public static Pick? Rare(IReadOnlyDictionary<string, string> v)
    {
        string V(string k) => v.TryGetValue(k, out var s) ? s : "";
        if (V("rare").Length == 0) return null;
        return new Pick("rare|" + V("rareId") + "|" + V("rare"), V("rare"), V("rareCount"), "", V("rareGrade"), V("rareIcon"), "", false, DateTime.UtcNow, Rare: true, Color: V("rareColor"));
    }

    /// <summary>The session's top drop ("top" values, MainWindow.HudTopValues), or null before anything priced was picked up.</summary>
    public static Pick? Top(IReadOnlyDictionary<string, string> v)
    {
        string V(string k) => v.TryGetValue(k, out var s) ? s : "";
        if (V("top").Length == 0) return null;
        return new Pick("top|" + V("top") + "|" + V("topCount") + "|" + V("topWorth"), V("top"), V("topCount"), V("topWorth"), V("topGrade"), V("topIcon"), "", false, DateTime.MinValue);
    }

    /// <summary>
    /// An item's tile as the window draws it (ItemTile): the icon from the tracker's icon cache in a slot framed in its
    /// grade's colour, the item's initial while the icon loads or when there is none, and the count badge on its corner.
    /// </summary>
    public static FrameworkElement Tile(Pick p, double size, bool glow = false)
    {
        var grade = p.Grade;
        bool common = grade is "" or "Common";
        Color c = p.Rare ? ColorOf(p.Color, grade) : GradeColor(grade);
        Brush frame, bg;
        if (p.Kinah) { frame = B(GoldC, .45); bg = Radial(Rgb(CommonC, .10), .72); }
        else if (common && !p.Rare) { frame = SlotFrame; bg = Radial(Rgb(CommonC, .10), .72); }
        else { frame = Frozen(Color.FromArgb(Glows(grade) || p.Rare ? (byte)0xFF : (byte)0xB3, c.R, c.G, c.B)); bg = Radial(Color.FromArgb(0x59, c.R, c.G, c.B), .75); }
        var g = new Grid { Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center };
        if (glow || Glows(grade) || p.Rare) g.Children.Add(Glow(c, 5, 10, .6));
        var box = new Border { CornerRadius = new CornerRadius(5), BorderThickness = new Thickness(1), BorderBrush = frame, Background = bg };
        var face = new Grid();
        var slot = IconCache.Get(p.Icon);
        double inner = size - 4;
        var pic = new Image { Source = slot.Image, Stretch = Stretch.UniformToFill, Margin = new Thickness(1), Clip = new RectangleGeometry(new Rect(0, 0, inner, inner), 3, 3) };
        RenderOptions.SetBitmapScalingMode(pic, BitmapScalingMode.HighQuality);
        var letter = new TextBlock
        {
            Text = Initial(p.Kinah ? "Kinah" : p.Name), FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Muted, HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 3), Visibility = slot.Image == null ? Visibility.Visible : Visibility.Collapsed,
        };
        if (slot.Image == null && !ReferenceEquals(slot, IconCache.None))
        {
            System.ComponentModel.PropertyChangedEventHandler? on = null;
            on = (_, _) => { if (slot.Image == null) return; pic.Source = slot.Image; letter.Visibility = Visibility.Collapsed; slot.PropertyChanged -= on; };
            slot.PropertyChanged += on;
        }
        face.Children.Add(letter); face.Children.Add(pic);
        box.Child = face;
        g.Children.Add(box);
        if (p.Count.Length > 0)
        {
            // on a canvas, so a badge wider than a small tile ("1192" on 24 px) hangs past its left edge instead of being cut
            var badge = new Border
            {
                Padding = new Thickness(3, 0, 3, 0), CornerRadius = new CornerRadius(4), Background = Slot, BorderBrush = BadgeLine, BorderThickness = new Thickness(1),
                Child = new TextBlock { Text = p.Count, FontFamily = Mono, FontSize = 9.5, Foreground = White, LineHeight = 11, LineStackingStrategy = LineStackingStrategy.BlockLineHeight },
            };
            Canvas.SetRight(badge, -4); Canvas.SetBottom(badge, -4);
            g.Children.Add(new Canvas { Width = size, Height = size, IsHitTestVisible = false, Children = { badge } });
        }
        return g;
    }

    /// <summary>The tile's lit slot: a soft light in the centre fading to the slot's dark (the mock's radial gradient at 50% 38%).</summary>
    static Brush Radial(Color centre, double to)
    {
        var b = new RadialGradientBrush { Center = new Point(.5, .38), GradientOrigin = new Point(.5, .38), RadiusX = .75, RadiusY = .75 };
        b.GradientStops.Add(new GradientStop(centre, 0));
        // CSS sizes the circle to the farthest corner (0.8 of the side from 50% 38%); WPF's radius here is 0.75 of it
        b.GradientStops.Add(new GradientStop(Rgb(SlotC), Math.Min(1, to * .797 / .75)));
        b.Freeze();
        return b;
    }

    static string Initial(string name) { foreach (var ch in name) if (char.IsLetter(ch)) return char.ToUpperInvariant(ch).ToString(); return "·"; }

    /// <summary>"RUN 42:17" during a run: the word and the clock apart. The session clock alone otherwise.</summary>
    public static (bool Run, string Clock) Time(string time) => time.StartsWith("RUN ") ? (true, time[4..]) : (false, time);
}
