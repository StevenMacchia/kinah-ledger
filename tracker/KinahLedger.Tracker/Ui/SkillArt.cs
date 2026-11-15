using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace KinahLedger.Ui;

/// <summary>
/// Each ability's own icon in the Parser's inspect view (Steven, 2026-11-14: the parser should look like the rest of the
/// app; ability icons beside their names): the game's skill art, MetaBot's copies, for the skills the Parser knows
/// (data/skills.json), packed by tools/skill-icons.py into one small palette sheet (Art/skill-icons.png, an embedded stream) with its cell list
/// (data/skill-icons.txt; a cell's ids share its picture). An ability with no icon gets the class's emblem, damage over time a
/// quiet glyph in the member's class colour: never a broken picture.
/// </summary>
static class SkillArt
{
    static readonly object Gate = new();
    static Dictionary<int, ImageSource>? _icons;

    /// <summary>The ability's icon, cut from the sheet once and frozen; null when the sheet has none for it.</summary>
    public static ImageSource? Image(int skill)
    {
        lock (Gate)
        {
            _icons ??= Load();
            if (_icons.TryGetValue(skill, out var i)) return i;
            // a class skill's rank or variant: its base's icon (KinahLedger.Parser.Abilities.Resolve)
            return KinahLedger.Parser.Abilities.Resolve(skill) is { } id && _icons.TryGetValue(id, out var b) ? b : null;
        }
    }

    /// <summary>How many abilities have an icon (for the tests).</summary>
    public static int Count { get { lock (Gate) return (_icons ??= Load()).Count; } }

    static Dictionary<int, ImageSource> Load()
    {
        var map = new Dictionary<int, ImageSource>();
        try
        {
            using var png = typeof(SkillArt).Assembly.GetManifestResourceStream("skill-icons.png");
            if (png == null) return map;
            var sheet = new BitmapImage();
            sheet.BeginInit(); sheet.StreamSource = png; sheet.CacheOption = BitmapCacheOption.OnLoad; sheet.EndInit(); sheet.Freeze();
            using var s = typeof(SkillArt).Assembly.GetManifestResourceStream("skill-icons.txt");
            if (s == null) return map;
            using var r = new StreamReader(s);
            var lines = r.ReadToEnd().Split('\n').Select(x => x.Trim()).Where(x => x.Length > 0 && !x.StartsWith('#')).ToList();
            if (lines.Count < 2 || !int.TryParse(lines[0], out var cell) || !int.TryParse(lines[1], out var cols) || cell <= 0 || cols <= 0) return map;
            for (int n = 0; n + 2 < lines.Count; n++)
            {
                // a cell's ids: one, or several that share the picture ("100014 100018")
                var ids = lines[n + 2].Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(x => int.TryParse(x, out var v) ? v : 0).Where(v => v != 0).ToList();
                if (ids.Count == 0) continue;
                var rect = new Int32Rect((n % cols) * cell, (n / cols) * cell, cell, cell);
                if (rect.X + cell > sheet.PixelWidth || rect.Y + cell > sheet.PixelHeight) break;
                var crop = new CroppedBitmap(sheet, rect);
                crop.Freeze();
                foreach (var id in ids) map[id] = crop;
            }
        }
        catch (Exception ex) { CrashLog.Write("ability icons", ex); }
        return map;
    }

    /// <summary>
    /// The ability's icon, <paramref name="size"/> square, on a rounded tile with a hairline ring in the member's class
    /// colour; an ability the list doesn't name, the class's emblem (from the id's family, else the member's); damage over
    /// time and an ability with neither, the tile in the class's tint with a small diamond in its colour.
    /// </summary>
    public static FrameworkElement Icon(int skill, string? cls, double size)
    {
        var c = ClassArt.ColorOf(cls);
        var img = skill != 0 ? Image(skill) : null;
        if (img == null && skill != 0 && KinahLedger.Parser.Abilities.Resolve(skill) == null)
            img = ClassArt.Image(KinahLedger.Parser.Abilities.ClassOfFamily(skill) is { Length: > 0 } fam ? fam : cls);
        var radius = Math.Round(size * 0.24);
        var tile = new Border
        {
            Width = size, Height = size, CornerRadius = new CornerRadius(radius), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left,
            BorderThickness = new Thickness(1), BorderBrush = Frozen(Color.FromArgb(img != null ? (byte)0x8C : (byte)0x66, c.R, c.G, c.B)),
            Background = Frozen(img != null ? Color.FromRgb(0x0E, 0x10, 0x16) : Color.FromArgb(0x26, c.R, c.G, c.B)),
            IsHitTestVisible = false,
        };
        if (img != null)
        {
            // the picture clipped to the tile's rounded inside
            var inner = size - 2;
            var pic = new Border
            {
                CornerRadius = new CornerRadius(Math.Max(0, radius - 1)), Width = inner, Height = inner,
                Background = new ImageBrush(img) { Stretch = Stretch.UniformToFill },
            };
            RenderOptions.SetBitmapScalingMode(pic, BitmapScalingMode.HighQuality);
            tile.Child = pic;
        }
        else
        {
            double g = size * 0.36;
            var diamond = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse($"M{g / 2},0 L{g},{g / 2} L{g / 2},{g} L0,{g / 2} Z"), Fill = Frozen(Color.FromArgb(0xD9, c.R, c.G, c.B)),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            };
            tile.Child = diamond;
        }
        return tile;
    }

    static SolidColorBrush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
}
