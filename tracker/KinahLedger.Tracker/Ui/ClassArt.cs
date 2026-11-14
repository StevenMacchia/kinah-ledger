using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace KinahLedger.Ui;

/// <summary>
/// The game's eight classes as the Parser draws them (Steven, 2026-11-14: "make sure you know and show the icon for what
/// class they are in the game, its super important. Color coordinate it"): the game's own class emblem (Art/class-*.png,
/// 64 px copies of the site's /img/mb/classes art, MetaBot's copies of the game's) on a ring in the class's colour, and one
/// colour per class for its meter bar, the same in the Parser tab, its overlay and the list. The colours follow the
/// Okabe-Ito colour-blind-safe set, lightened for the dark looks; the emblem tells the class apart where two colours are near.
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

    /// <summary>The class's colour: Gladiator vermillion, Templar blue, Assassin reddish purple, Ranger bluish green, Sorcerer sky blue, Spiritmaster violet, Cleric yellow, Chanter lime.</summary>
    public static Color ColorOf(string? cls) => Key(cls) switch
    {
        "gladiator" => Color.FromRgb(0xE3, 0x6A, 0x2E),     // vermillion
        "templar" => Color.FromRgb(0x3A, 0x7B, 0xD5),       // blue
        "assassin" => Color.FromRgb(0xD0, 0x7C, 0xAB),      // reddish purple
        "ranger" => Color.FromRgb(0x18, 0xAD, 0x86),        // bluish green
        "sorcerer" => Color.FromRgb(0x5B, 0xC0, 0xEB),      // sky blue
        "spiritmaster" => Color.FromRgb(0x9B, 0x7D, 0xEB),  // violet
        "cleric" => Color.FromRgb(0xE6, 0xD3, 0x4E),        // yellow
        "chanter" => Color.FromRgb(0x9C, 0xC8, 0x3C),       // lime
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
