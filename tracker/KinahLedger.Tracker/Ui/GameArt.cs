using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace KinahLedger.Ui;

/// <summary>
/// The game's own UI pictures for concepts (Art/*.png, the site's /img/ui icons: Kinah, a greatsword, a recipe scroll, a
/// dungeon gate, a medal, a loot chest...). Since 2026-11-10 they stand where the window drew line icons: Steven wants real
/// AION 2 art only, never drawn or made-up icons. Interface marks (a chevron, a check, a cross, a pin toggle, the gear,
/// the globe, share) stay as lines: they are controls, as on the site. Every picture is frozen, so the panel over the
/// game, which readers' threads touch, can use them too.
/// </summary>
static class GameArt
{
    /// <summary>The line icon's short name (Ui/Theme.xaml's Ic* keys) and the picture that now stands for it.</summary>
    static readonly Dictionary<string, string> ForIcon = new(StringComparer.Ordinal)
    {
        ["Coin"] = "kinah", ["Sword"] = "greatsword", ["Hammer"] = "scroll", ["Potion"] = "scroll", ["Flag"] = "medal",
        ["Trophy"] = "medalgold", ["Star"] = "daily", ["Door"] = "dungeon", ["Timer"] = "dungeon", ["Bolt"] = "rift",
        ["Pin"] = "rift", ["Map"] = "rift", ["Bag"] = "attendance", ["Sack"] = "attendance", ["Spark"] = "attendance",
        ["Q"] = "checklist", ["Shield"] = "profile", ["Pick"] = "ore", ["Leaf"] = "ore", ["Hide"] = "ore", ["Gem"] = "morph",
    };

    static readonly Dictionary<string, ImageSource?> Loaded = new(StringComparer.Ordinal);

    /// <summary>The picture for a line icon's name, or null when the name is an interface mark that stays a line.</summary>
    public static string? For(string icon) => ForIcon.TryGetValue(icon, out var a) ? a : null;

    /// <summary>A picture by its file name (no extension), loaded once and frozen; null if it isn't in the exe.</summary>
    public static ImageSource? Image(string art)
    {
        lock (Loaded)
        {
            if (Loaded.TryGetValue(art, out var have)) return have;
            ImageSource? img = null;
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri($"pack://application:,,,/Art/{art}.png", UriKind.Absolute);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();
                img = bmp;
            }
            catch (Exception) { img = null; }
            Loaded[art] = img;
            return img;
        }
    }

    /// <summary>The picture as an element, <paramref name="size"/> square, scaled smoothly.</summary>
    public static Image Element(string art, double size)
    {
        var i = new Image { Source = Image(art), Width = size, Height = size, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, IsHitTestVisible = false };
        RenderOptions.SetBitmapScalingMode(i, BitmapScalingMode.HighQuality);
        return i;
    }
}
