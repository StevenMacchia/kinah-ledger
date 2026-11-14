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
    public static Color ColorOf(string? cls) => Key(cls) switch
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
            ToolTip = Key(cls) is { Length: > 0 } ? Title(cls!) : null,
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
    /// A row with a meter bar behind it (a classic meter: the bar's length is the row's share of the top row's figure), in
    /// the class's colour at about a third, with a solid edge at its left; the row's own content drawn on top.
    /// </summary>
    public static Grid MeterRow(FrameworkElement content, string? cls, double share, bool other = false, double radius = 6)
    {
        var host = new Grid();
        var c = other ? System.Windows.Media.Color.FromRgb(0x86, 0x8D, 0xA1) : ColorOf(cls);
        var bar = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(radius),
            Background = Frozen(System.Windows.Media.Color.FromArgb(other ? (byte)0x30 : (byte)0x55, c.R, c.G, c.B)),
            BorderBrush = Frozen(System.Windows.Media.Color.FromArgb(0xE6, c.R, c.G, c.B)), BorderThickness = new Thickness(3, 0, 0, 0),
            IsHitTestVisible = false,
        };
        var frac = Math.Clamp(double.IsFinite(share) ? share : 0, 0, 1);
        host.SizeChanged += (_, _) => bar.Width = frac <= 0 ? 0 : Math.Max(6, host.ActualWidth * frac);
        host.Children.Add(bar);
        host.Children.Add(content);
        host.Tag = frac;   // the share, for the tests
        return host;
    }

    static SolidColorBrush Frozen(System.Windows.Media.Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
}
