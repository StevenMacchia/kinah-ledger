using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace KinahLedger.Ui;

/// <summary>
/// A character's class icon as the window draws it (the header's chip, the switcher's rows): the icon the tracker read off
/// the medallion between the health and mana bars (Ocr/CharacterBadge.cs) drawn back from its fingerprint. The fingerprint is
/// the icon's light on a 16 × 16 grid inside the medallion's diamond, so it is a small picture of the icon itself: silver on
/// navy in the game, drawn here the same way, the cells outside the diamond left clear. A character with no icon read yet (one
/// split off by hand) gets its initial instead. Fixed colours, as the game's own medallion: the same tile on every look.
/// </summary>
public static class CharacterIcon
{
    static readonly Color Navy = Color.FromRgb(0x16, 0x1D, 0x2E), Silver = Color.FromRgb(0xEE, 0xEA, 0xDE);
    static readonly Color TileBg = Color.FromRgb(0x0D, 0x0F, 0x14), TileLine = Color.FromRgb(0x3A, 0x40, 0x55), TileLit = Color.FromRgb(0xF5, 0xB9, 0x42);
    static readonly Dictionary<string, BitmapSource?> Drawn = new();

    /// <summary>
    /// The icon drawn from its fingerprint (<see cref="Ocr.CharacterBadge.Fingerprint"/>), or null when there is none or it
    /// can't be read. Each cell's light is stretched between the icon's darker and brightest cells, so a dim reading (a reward
    /// screen over the game) draws as bright as a plain one.
    /// </summary>
    public static BitmapSource? Picture(string? print)
    {
        const int n = Ocr.CharacterBadge.Grid;
        if (print == null || print.Length != n * n * 2) return null;
        lock (Drawn) if (Drawn.TryGetValue(print, out var known)) return known;
        BitmapSource? pic = null;
        try
        {
            var v = new int[n * n];
            for (int i = 0; i < v.Length; i++) v[i] = Convert.ToInt32(print.Substring(i * 2, 2), 16);
            var inside = v.Where(x => x > 0).OrderBy(x => x).ToList();
            if (inside.Count >= 20)
            {
                double lo = inside[(int)(inside.Count * 0.2)], hi = inside[(int)(inside.Count * 0.97)];
                if (hi - lo < 8) hi = lo + 8;
                var px = new byte[n * n * 4];
                for (int i = 0; i < v.Length; i++)
                {
                    if (v[i] == 0) continue;   // outside the diamond: clear
                    // a soft threshold: the icon's silver strokes stand out from the medallion's navy as they do in the game
                    double t = Math.Clamp((v[i] - lo) / (hi - lo), 0, 1);
                    t = Math.Clamp((t - 0.22) / 0.6, 0, 1); t = t * t * (3 - 2 * t);
                    px[i * 4] = (byte)(Navy.B + (Silver.B - Navy.B) * t);
                    px[i * 4 + 1] = (byte)(Navy.G + (Silver.G - Navy.G) * t);
                    px[i * 4 + 2] = (byte)(Navy.R + (Silver.R - Navy.R) * t);
                    px[i * 4 + 3] = 255;
                }
                var bmp = BitmapSource.Create(n, n, 96, 96, PixelFormats.Bgra32, null, px, n * 4);
                bmp.Freeze();
                pic = bmp;
            }
        }
        catch (FormatException) { pic = null; }
        lock (Drawn) Drawn[print] = pic;
        return pic;
    }

    /// <summary>
    /// The tile: a dark rounded square with the icon in it (smoothed as it is drawn larger than its 16 cells), or the
    /// character's initial in gold. <paramref name="lit"/>: a gold edge, the character counted for in the switcher.
    /// </summary>
    public static Border Tile(string? print, string label, double size, bool lit = false)
    {
        var tile = new Border
        {
            Width = size, Height = size, CornerRadius = new CornerRadius(Math.Round(size * 0.24)), BorderThickness = new Thickness(1),
            Background = new SolidColorBrush(TileBg), BorderBrush = new SolidColorBrush(lit ? TileLit : TileLine), SnapsToDevicePixels = true,
        };
        if (Picture(print) is { } pic)
        {
            var img = new Image { Source = pic, Stretch = Stretch.Uniform, Margin = new Thickness(Math.Max(0.5, size * 0.04)) };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            tile.Child = img;
        }
        else
        {
            var initial = label.Trim().Length > 0 ? char.ToUpperInvariant(label.Trim()[0]).ToString() : "?";
            tile.Child = new TextBlock
            {
                Text = initial, FontSize = Math.Round(size * 0.52), FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(TileLit),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, size * 0.04),
            };
        }
        return tile;
    }
}

/// <summary>
/// A line of a name and what follows it (the switcher's "Lysandra  Counting now"): side by side, the name taking only the
/// room the rest leaves it, so a long name trims and the words after it never do.
/// </summary>
public sealed class NameLine : Panel
{
    protected override Size MeasureOverride(Size available)
    {
        double rest = 0, high = 0;
        for (int i = 1; i < InternalChildren.Count; i++)
        {
            var c = InternalChildren[i];
            c.Measure(new Size(double.PositiveInfinity, available.Height));
            rest += c.DesiredSize.Width; high = Math.Max(high, c.DesiredSize.Height);
        }
        if (InternalChildren.Count > 0)
        {
            var first = InternalChildren[0];
            first.Measure(new Size(Math.Max(0, available.Width - rest), available.Height));
            high = Math.Max(high, first.DesiredSize.Height);
            return new Size(first.DesiredSize.Width + rest, high);
        }
        return new Size(0, 0);
    }

    protected override Size ArrangeOverride(Size final)
    {
        double x = 0;
        foreach (UIElement c in InternalChildren)
        {
            double w = c.DesiredSize.Width;
            c.Arrange(new Rect(x, (final.Height - c.DesiredSize.Height) / 2, w, c.DesiredSize.Height));
            x += w;
        }
        return final;
    }
}
