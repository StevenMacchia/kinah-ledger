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
        // the Quest look's names (2026-11-07): Badge is the strip, Buddy the card, Pop the ticker; the keys stay, so hud.json keeps its choice
        new(Strip, "Badge", "One line with the figures you pick, and the ring of your best here"),
        new(Card, "Buddy", "The ring, the hour, your best here and the last three pickups"),
        new(Ticker, "Pop", "Pickups pop in like the game's loot, the top drop pinned"),
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

    // the v2 palette (2026-11-10, Ui/Skins.cs night, the site's tokens): near-black neutral under Kinah gold, mint for what pays, coral for a run.
    // Over the game the panel is the site's darkest (0B0C10, not the window's 111319) and the muted and faint tones a step lighter, so every text
    // keeps at least the plum panel's contrast over bright scenes at the usual opacities; the text sits a weight above the window's
    public const uint InkC = 0xEDEFF5, MutedC = 0xB1B7C8, FaintC = 0x7A8094, GoldC = 0xF5B942, SecGoldC = 0xD99A24, WarnC = 0xF7A072, MintC = 0x3DD68C,
        RecC = 0xF26D6D, SlotC = 0x0A0B10, LineC = 0x232736, BadgeLineC = 0x2E3346, CommonC = 0xD9DDE8, PanelC = 0x0B0C10, Panel2C = 0x171A22;

    public static readonly SolidColorBrush Ink = B(InkC), Muted = B(MutedC), Faint = B(FaintC), Gold = B(GoldC), Warn = B(WarnC), Mint = B(MintC),
        Rec = B(RecC), DotOff = B(0x4A5066), Slot = B(SlotC), FrameLine = B(LineC), BadgeLine = B(BadgeLineC), White = B(0xFFFFFF),
        SlotFrame = B(CommonC, .28), Sep = Frozen(Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF));

    /// <summary>Inter (the window's face since the v2 look); figures in the same face, a weight heavier than the text.</summary>
    public static readonly FontFamily Text = new("pack://application:,,,/Fonts/#Inter, Segoe UI Variable Text, Segoe UI");
    public static readonly FontFamily Mono = Text;
    public static readonly FontWeight MonoWeight = FontWeights.SemiBold;

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
        Text = text, FontSize = size, Foreground = fg, FontFamily = mono ? Mono : Text, FontWeight = weight ?? (mono ? MonoWeight : FontWeights.Medium),
        VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
    };

    public static System.Windows.Documents.Run R(string text, double size, Brush fg, bool mono = false, FontWeight? weight = null) => new(text)
    {
        FontSize = size, Foreground = fg, FontFamily = mono ? Mono : Text, FontWeight = weight ?? (mono ? MonoWeight : FontWeights.Medium),
    };

    /// <summary>
    /// The ring (the window's QRing, with frozen brushes): the share round a dark track, clockwise from the top, a figure in
    /// the middle. One meter everywhere: this hour against your best here, in the window, on Badge and on Buddy.
    /// </summary>
    public static Grid Ring(double size, double stroke, double share, Color tone, string big, string small = "")
    {
        var g = new Grid { Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        g.Children.Add(new Ellipse { Width = size - stroke, Height = size - stroke, StrokeThickness = stroke, Stroke = Frozen(Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        double s = Math.Clamp(share, 0, 1);
        var brush = Frozen(tone);
        if (s >= 0.999)
            g.Children.Add(new Ellipse { Width = size - stroke, Height = size - stroke, StrokeThickness = stroke, Stroke = brush, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Effect = new DropShadowEffect { Color = tone, BlurRadius = 8, ShadowDepth = 0, Opacity = .5 } });
        else if (s > 0.004)
        {
            double c = size / 2, r = (size - stroke) / 2, a = 2 * Math.PI * s;
            var fig = new PathFigure { StartPoint = new Point(c, c - r), IsClosed = false, IsFilled = false };
            fig.Segments.Add(new ArcSegment(new Point(c + r * Math.Sin(a), c - r * Math.Cos(a)), new Size(r, r), 0, a > Math.PI, SweepDirection.Clockwise, true));
            var geo = new PathGeometry(); geo.Figures.Add(fig); geo.Freeze();
            g.Children.Add(new System.Windows.Shapes.Path { Data = geo, Stroke = brush, StrokeThickness = stroke, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, Width = size, Height = size, Effect = new DropShadowEffect { Color = tone, BlurRadius = 8, ShadowDepth = 0, Opacity = .5 } });
        }
        var text = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var b = T(big, size >= 40 ? 12.5 : 9.5, Ink, weight: FontWeights.Bold); b.HorizontalAlignment = HorizontalAlignment.Center; b.TextTrimming = TextTrimming.None;
        b.LineHeight = size >= 40 ? 13 : 10; b.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
        text.Children.Add(b);
        if (small.Length > 0) { var sm = T(small.ToUpperInvariant(), 7, Muted, weight: FontWeights.SemiBold); sm.HorizontalAlignment = HorizontalAlignment.Center; sm.TextTrimming = TextTrimming.None; text.Children.Add(sm); }
        g.Children.Add(text);
        return g;
    }

    /// <summary>The chase from the window's values: the share of your best here (-1 when there is none) and the words ("130K to your best here", "New best here").</summary>
    public static (double Share, string Words, bool Beat) Chase(IReadOnlyDictionary<string, string> v)
    {
        string V(string k) => v.TryGetValue(k, out var s) ? s : "";
        double share = double.TryParse(V("chase"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var sh) ? sh : -1;
        var words = V("toBest");
        return (share, words, words.StartsWith("New", StringComparison.Ordinal));
    }

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

    // Badge's state signs (2026-11-08 audit: its notice, rare drop, stop and accuracy lines differed by text colour alone).
    // Each is a 16-unit drawing in the warning sign's manner: a tinted body under a 1.4 px line.

    /// <summary>A waiting notice (an update ready, a price alert): an "i" in a gold ring.</summary>
    public static readonly DrawingImage NoticeSign = Sign(g =>
    {
        var gold = Frozen(Rgb(GoldC));
        g.Children.Add(new GeometryDrawing(Frozen(Rgb(GoldC, .14)), new Pen(gold, 1.4), new EllipseGeometry(new Point(8, 8), 6.3, 6.3)));
        g.Children.Add(new GeometryDrawing(null, new Pen(gold, 1.6) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, Geometry.Parse("M8,7.3 L8,11.2")));
        g.Children.Add(new GeometryDrawing(gold, null, new EllipseGeometry(new Point(8, 4.9), .95, .95)));
    });

    /// <summary>A run started or resumed: the run dot, coral as the panel's run dot is.</summary>
    public static readonly DrawingImage StartSign = Sign(g =>
    {
        g.Children.Add(new GeometryDrawing(Frozen(Rgb(RecC, .18)), null, new EllipseGeometry(new Point(8, 8), 6.5, 6.5)));
        g.Children.Add(new GeometryDrawing(Rec, null, new EllipseGeometry(new Point(8, 8), 3.6, 3.6)));
    });

    /// <summary>A run stopped: a recorder's stop square in a ring, so the line reads as an ending, not one more sentence.</summary>
    public static readonly DrawingImage StopSign = Sign(g =>
    {
        g.Children.Add(new GeometryDrawing(Frozen(Rgb(InkC, .08)), new Pen(Muted, 1.4), new EllipseGeometry(new Point(8, 8), 6.3, 6.3)));
        g.Children.Add(new GeometryDrawing(Ink, null, new RectangleGeometry(new Rect(5.4, 5.4, 5.2, 5.2), .8, .8)));
    });

    /// <summary>Nothing looked wrong: a mint tick in a ring.</summary>
    public static readonly DrawingImage GoodSign = Sign(g =>
    {
        g.Children.Add(new GeometryDrawing(Frozen(Rgb(MintC, .14)), new Pen(Mint, 1.4), new EllipseGeometry(new Point(8, 8), 6.3, 6.3)));
        g.Children.Add(new GeometryDrawing(null, new Pen(Mint, 1.6) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round }, Geometry.Parse("M5.2,8.3 L7.2,10.3 L10.9,6.2")));
    });

    /// <summary>A rare drop or a goal reached: the window's four-point spark (Theme.xaml IcSpark), filled in the alert's colour.</summary>
    public static DrawingImage DropSign(Color c) => Sign(g =>
        g.Children.Add(new GeometryDrawing(Frozen(Color.FromArgb(0x55, c.R, c.G, c.B)), new Pen(Frozen(c), 1.3) { LineJoin = PenLineJoin.Round },
            Geometry.Parse("M8,1.5 L9.3,6.4 L14.2,7.7 L9.3,9 L8,13.9 L6.7,9 L1.8,7.7 L6.7,6.4 Z"))));

    static DrawingImage Sign(Action<DrawingGroup> draw)
    {
        var g = new DrawingGroup();
        g.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, 16, 16))));
        draw(g);
        var img = new DrawingImage(g); img.Freeze();
        return img;
    }

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
        // no picture: the kind's line icon from the window's set (2026-11-08, as Ui/Quest.cs KindGlyph), the coin for Kinah and
        // the loot sack for an item, where the name's first letter read as a broken picture
        var letter = new System.Windows.Shapes.Path
        {
            Data = KindGeo(p.Kinah ? "Coin" : "Sack"), Width = Math.Round(size * 0.5), Height = Math.Round(size * 0.5), Stretch = Stretch.Uniform, Stroke = Muted, StrokeThickness = 1.8,
            StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Visibility = slot.Image == null ? Visibility.Visible : Visibility.Collapsed,
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
                Child = new TextBlock { Text = p.Count, FontFamily = Mono, FontWeight = FontWeights.Bold, FontSize = 9.5, Foreground = White, LineHeight = 11, LineStackingStrategy = LineStackingStrategy.BlockLineHeight },
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

    /// <summary>
    /// Theme.xaml's IcSack and IcCoin, parsed here and frozen: the panel is drawn where the application's resources may
    /// not be in reach (its layout renders found none), and its drawing is frozen throughout.
    /// </summary>
    static readonly Geometry SackGeo = FrozenGeo("M9,4 H15 L14,7 H10 Z M10,7 C5,9 4,14 5,18 C6,20.5 18,20.5 19,18 C20,14 19,9 14,7"),
        CoinGeo = FrozenGeo("M12,3 A9,9 0 1 0 12,21 A9,9 0 1 0 12,3 Z M9.5,8 V16 M9.5,12 L14.5,8 M9.5,12 L14.5,16");
    static Geometry FrozenGeo(string data) { var g = Geometry.Parse(data); g.Freeze(); return g; }
    static Geometry KindGeo(string name) => name == "Coin" ? CoinGeo : SackGeo;

    /// <summary>"RUN 42:17" during a run: the word and the clock apart. The session clock alone otherwise.</summary>
    public static (bool Run, string Clock) Time(string time) => time.StartsWith("RUN ") ? (true, time[4..]) : (false, time);
}
