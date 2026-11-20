using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace KinahLedger.Ui;

/// <summary>
/// The game's eight classes as the Parser draws them (Steven, 2026-11-14: "make sure you know and show the icon for what
/// class they are in the game, its super important. Color coordinate it"): the game's own class emblem (Art/class-*.png,
/// 64 px copies of the site's /img/mb/classes art, MetaBot's copies of the game's) on a ring in the class's colour, and one
/// colour per class for its meter bar, the same in the Parser tab, its overlay and the list: the game's own colour for the
/// class, as its party list draws the class's glyph (Ocr/PartyFrame.cs); the emblem tells the class apart where two are near.
/// </summary>
public static class ClassArt
{
    /// <summary>The classes, by the key their art and colour go under.</summary>
    public static readonly string[] Keys = { "gladiator", "templar", "assassin", "ranger", "sorcerer", "spiritmaster", "cleric", "chanter" };

    /// <summary>"Spirit Master", "spiritmaster", " Gladiator " are spiritmaster and gladiator; "" when the class isn't one of the eight.</summary>
    public static string Key(string? cls)
    {
        var k = (cls ?? "").Replace(" ", "").Replace("-", "").Trim().ToLowerInvariant();
        return Keys.Contains(k) ? k : "";
    }

    /// <summary>The class's colour, the game's own (its party list's): Gladiator light blue, Templar blue, Assassin green, Ranger teal, Sorcerer purple, Spiritmaster magenta, Cleric gold, Chanter orange.</summary>
    public static Color ColorOf(string? cls) => cls == PvpKill ? Color.FromRgb(0xE8, 0x6A, 0x5E) : cls == PvpAssist ? Color.FromRgb(0xE0, 0xA8, 0x4E) : Key(cls) switch
    {
        // the glyph colours of the game's own party list (Ocr/PartyFrame.Refs), so the Parser's colours are the game's
        "gladiator" => Color.FromRgb(0x7A, 0xC5, 0xD9),     // light blue
        "templar" => Color.FromRgb(0x71, 0x8D, 0xD5),       // blue
        "assassin" => Color.FromRgb(0x89, 0xCE, 0x79),      // green
        "ranger" => Color.FromRgb(0x60, 0xAB, 0x8B),        // teal
        "sorcerer" => Color.FromRgb(0xA2, 0x7D, 0xD8),      // purple
        "spiritmaster" => Color.FromRgb(0xB7, 0x66, 0xB7),  // magenta
        "cleric" => Color.FromRgb(0xCC, 0xB9, 0x72),        // gold
        "chanter" => Color.FromRgb(0xCC, 0x99, 0x5C),       // orange
        _ => Color.FromRgb(0x86, 0x8D, 0xA1),               // unknown: the quiet grey
    };

    /// <summary>
    /// The PvP panel's rows (Parser/PvpKills.cs) are players whose class isn't known: their row is tinted by what it was, a
    /// kill coral, an assist amber, under these keys (no class: the disc carries the name's initial, no class name is shown).
    /// </summary>
    public const string PvpKill = "pvp:kill", PvpAssist = "pvp:assist";

    /// <summary>The class's colour at <paramref name="alpha"/> (0 to 1), frozen: a meter bar behind text is the colour at about a third.</summary>
    public static SolidColorBrush Brush(string? cls, double alpha = 1)
    {
        var c = ColorOf(cls);
        var b = new SolidColorBrush(System.Windows.Media.Color.FromArgb((byte)Math.Round(255 * Math.Clamp(alpha, 0, 1)), c.R, c.G, c.B));
        b.Freeze();
        return b;
    }

    /// <summary>The class's emblem, loaded once; null for a class that isn't one of the eight.</summary>
    public static ImageSource? Image(string? cls) => Key(cls) is { Length: > 0 } k ? GameArt.Image("class-" + k) : null;

    /// <summary>
    /// The class's emblem on a dark disc with a ring in its colour, <paramref name="size"/> square; the class's name on
    /// hover. A class the decoder hasn't named yet is a quiet disc with the player's initial (no class art is made up).
    /// </summary>
    public static FrameworkElement Icon(string? cls, double size, string name = "")
    {
        var c = ColorOf(cls);
        var img = Image(cls);
        var disc = new Border
        {
            Width = size, Height = size, CornerRadius = new CornerRadius(size / 2), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left,
            Background = Frozen(System.Windows.Media.Color.FromArgb(0xE6, 0x0E, 0x10, 0x16)),
            BorderBrush = Frozen(System.Windows.Media.Color.FromArgb(img != null ? (byte)0xC0 : (byte)0x60, c.R, c.G, c.B)), BorderThickness = new Thickness(Math.Max(1, size / 14)),
            ToolTip = Key(cls) is { Length: > 0 } ? L.T(Title(cls!)) : null,
        };
        if (img != null)
        {
            var i = new System.Windows.Controls.Image { Source = img, Width = size * 0.78, Height = size * 0.78, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
            RenderOptions.SetBitmapScalingMode(i, BitmapScalingMode.HighQuality);
            disc.Child = i;
        }
        else
        {
            var letter = name.Trim().Length > 0 ? name.Trim()[..1].ToUpperInvariant() : "·";
            disc.Child = new TextBlock { Text = letter, FontSize = size * 0.45, FontWeight = FontWeights.Bold, Foreground = Frozen(c), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        }
        return disc;
    }

    /// <summary>"Spiritmaster" from any spelling; the text as given when it isn't one of the eight.</summary>
    public static string Title(string cls) => Key(cls) is { Length: > 0 } k ? char.ToUpperInvariant(k[0]) + k[1..] : cls.Trim();

    /// <summary>
    /// A member's name in their class's colour, kept readable (the parser polish, 2026-11-14: one colour per member wherever
    /// they appear): on a dark surface the colour lifted halfway to white, on a light one taken halfway to black.
    /// </summary>
    public static Color NameColor(string? cls, bool dark = true)
    {
        if (Key(cls).Length == 0) return dark ? Color.FromRgb(0xED, 0xEF, 0xF5) : Color.FromRgb(0x1A, 0x1D, 0x26);
        var c = ColorOf(cls);
        byte Mix(byte v, byte to, double t) => (byte)Math.Round(v + (to - v) * t);
        return dark ? Color.FromRgb(Mix(c.R, 255, 0.55), Mix(c.G, 255, 0.55), Mix(c.B, 255, 0.55)) : Color.FromRgb(Mix(c.R, 0, 0.5), Mix(c.G, 0, 0.5), Mix(c.B, 0, 0.5));
    }

    /// <summary>The name's colour as a frozen brush, for the window's skin (dark or light) or the overlays (always dark).</summary>
    public static SolidColorBrush NameBrush(string? cls, bool dark = true) => Frozen(NameColor(cls, dark));

    /// <summary>
    /// A row with a meter bar behind it (Steven, 2026-11-14: "thicker bars that act as a background for the character"):
    /// the bar's length is the row's share of the top row's figure. Since the parser polish it is drawn as the window's
    /// other bars are (QBar): a quiet track the row's full width so every row reads as one table, the fill in the class's
    /// colour fading from its solid edge at the left, and a light gloss on its top edge; the row's own content on top.
    /// <paramref name="reserveRight"/>: the bar stays behind the member, ending before the figure columns at the right (the
    /// review's first finding: a bar's end cut through the Damage figure), so the figures sit on the plain track.
    /// <paramref name="light"/>: a light skin, where the track needs more colour to keep the rows' bounds.
    /// </summary>
    public static Grid MeterRow(FrameworkElement content, string? cls, double share, bool other = false, double radius = 8, string? slideKey = null, double reserveRight = 0, bool light = false)
    {
        var host = new Grid();
        var c = other ? Color.FromRgb(0x86, 0x8D, 0xA1) : ColorOf(cls);
        var track = new Border { CornerRadius = new CornerRadius(radius), Background = Frozen(Color.FromArgb(light ? (byte)0x24 : (byte)0x12, c.R, c.G, c.B)), IsHitTestVisible = false };
        var fade = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        fade.GradientStops.Add(new GradientStop(Color.FromArgb(other ? (byte)0x2C : (byte)0x4A, c.R, c.G, c.B), 0));
        fade.GradientStops.Add(new GradientStop(Color.FromArgb(other ? (byte)0x14 : (byte)0x1E, c.R, c.G, c.B), 1));
        fade.Freeze();
        var bar = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(radius), Background = fade, IsHitTestVisible = false,
            // the gloss: a hairline of light along the top, as the window's progress bars have
            Child = new Border { CornerRadius = new CornerRadius(radius, radius, 0, 0), Height = Math.Max(2, radius), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(1, 1, 1, 0),
                Background = Gloss },
        };
        var cap = new Border { Width = 3, HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(radius, 0, 0, radius), Background = Frozen(Color.FromArgb(0xF2, c.R, c.G, c.B)), IsHitTestVisible = false };
        var frac = Math.Clamp(double.IsFinite(share) ? share : 0, 0, 1);
        double shown = frac;
        void Apply(double v) { shown = v; double room = host.ActualWidth - reserveRight; if (room > 0) bar.Width = v <= 0 ? 0 : Math.Max(8, room * v); }
        host.SizeChanged += (_, _) => Apply(shown);
        // the bar slides to its new length as damage lands (Ui/Motion.cs), when the caller names it
        if (slideKey != null) host.Loaded += (_, _) => Motion.Slide(bar, slideKey, frac, Apply);
        host.Children.Add(track);
        host.Children.Add(bar);
        host.Children.Add(cap);
        host.Children.Add(content);
        host.Tag = frac;   // the share, for the tests
        return host;
    }

    static readonly Brush Gloss = MakeGloss();
    static Brush MakeGloss()
    {
        var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        b.GradientStops.Add(new GradientStop(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF), 0));
        b.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 1));
        b.Freeze();
        return b;
    }

    /// <summary>
    /// The glass's row (Ui/HudGlass.cs): the row's content, then under it a 3 px meter in the class's colour on a faint
    /// track, the share of the top figure. Where the old row lay a translucent block of colour behind the name.
    /// </summary>
    public static Grid ThinMeterRow(FrameworkElement content, string? cls, double share, bool other = false)
    {
        var c = other ? System.Windows.Media.Color.FromRgb(0x86, 0x8D, 0xA1) : ColorOf(cls);
        var host = new Grid();
        host.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        host.RowDefinitions.Add(new RowDefinition { Height = new GridLength(3) });
        host.Children.Add(content);
        var track = new Grid { Margin = new Thickness(29, 1, 4, 0), IsHitTestVisible = false };
        Grid.SetRow(track, 1);
        track.Children.Add(new Border { Background = HudGlass.Track, CornerRadius = new CornerRadius(1.5) });
        var bar = new Border { HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(1.5), Background = Frozen(System.Windows.Media.Color.FromArgb(other ? (byte)0x80 : (byte)0xF2, c.R, c.G, c.B)) };
        var frac = Math.Clamp(double.IsFinite(share) ? share : 0, 0, 1);
        track.SizeChanged += (_, _) => bar.Width = frac <= 0 ? 0 : Math.Max(3, track.ActualWidth * frac);
        track.Children.Add(bar);
        host.Children.Add(track);
        host.Tag = frac;   // the share, for the tests
        return host;
    }

    static SolidColorBrush Frozen(System.Windows.Media.Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
}
