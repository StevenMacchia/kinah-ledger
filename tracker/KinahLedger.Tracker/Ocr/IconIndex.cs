using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Text.Json;

namespace KinahLedger.Ocr;

/// <summary>
/// Item icons as small colour thumbnails, to name an item from its picture alone: the reward screens of events and
/// expeditions show items as icons with a count and no name (Steven, 2026-10-22, "Jump Jump" rewards). Built from the
/// game's own icons (the item list's icon addresses), one entry per distinct picture with every item that uses it; many
/// gear tiers share one picture, so a match can name a family of items rather than one. Only the middle of each icon
/// is compared, since the game frames a slot by its grade.
/// </summary>
public sealed class IconIndex
{
    public const int Size = 12;
    const double Inner = 0.72;   // the part of the slot compared: inside the grade frame and the count

    /// <summary>Pixels: Size x Size RGB; Mask: one byte per pixel, the icon's own opacity (the slot behind it differs by grade, so only the drawn part is compared).</summary>
    public sealed record Entry(string Icon, string[] Items, byte[] Pixels, byte[] Mask);
    public List<Entry> Entries { get; } = new();

    /// <summary>The thumbnail of the middle of a picture: Size x Size, RGB.</summary>
    public static byte[] Thumb(Bitmap src, Rectangle? area = null) => ThumbMask(src, area).Pixels;

    public static (byte[] Pixels, byte[] Mask) ThumbMask(Bitmap src, Rectangle? area = null)
    {
        var a = area ?? new Rectangle(0, 0, src.Width, src.Height);
        int cw = (int)(a.Width * Inner), ch = (int)(a.Height * Inner);
        var inner = new Rectangle(a.X + (a.Width - cw) / 2, a.Y + (a.Height - ch) / 2, cw, ch);
        using var small = new Bitmap(Size, Size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(small))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBilinear;
            g.DrawImage(src, new Rectangle(0, 0, Size, Size), inner, GraphicsUnit.Pixel);
        }
        var px = new byte[Size * Size * 3]; var mask = new byte[Size * Size];
        int k = 0;
        for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++) { var c = small.GetPixel(x, y); mask[y * Size + x] = c.A; px[k++] = c.R; px[k++] = c.G; px[k++] = c.B; }
        return (px, mask);
    }

    /// <summary>The closest pictures, best first, with their distance (mean squared difference per channel).</summary>
    public List<(Entry Entry, double Distance)> Match(byte[] thumb, int top = 3)
    {
        var res = new List<(Entry, double)>(Entries.Count);
        foreach (var e in Entries)
        {
            // only where the icon is drawn; an icon that covers too little of the slot says too little to compare
            long sum = 0; int n = 0;
            for (int p = 0; p < Size * Size; p++)
            {
                if (e.Mask[p] < 160) continue;
                for (int c = 0; c < 3; c++) { int d = thumb[p * 3 + c] - e.Pixels[p * 3 + c]; sum += d * d; }
                n += 3;
            }
            if (n < Size * Size * 3 / 5) continue;
            res.Add((e, sum / (double)n));
        }
        return res.OrderBy(r => r.Item2).Take(top).ToList();
    }

    /// <summary>Builds the index from a folder of icon files and the item list (items' "icon" addresses name the files).</summary>
    public static IconIndex Build(string iconsDir, string itemsJson)
    {
        var byFile = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        using (var doc = JsonDocument.Parse(File.ReadAllText(itemsJson)))
            foreach (var it in doc.RootElement.GetProperty("items").EnumerateArray())
            {
                if (!it.TryGetProperty("icon", out var ic) || ic.GetString() is not { Length: > 0 } url) continue;
                var f = Path.GetFileName(new Uri(url).AbsolutePath);
                if (!byFile.TryGetValue(f, out var l)) byFile[f] = l = new List<string>();
                var name = it.GetProperty("name").GetString() ?? "";
                if (!l.Contains(name)) l.Add(name);
            }
        var idx = new IconIndex();
        foreach (var (file, names) in byFile)
        {
            var p = Path.Combine(iconsDir, file);
            if (!File.Exists(p)) continue;
            try { using var bmp = new Bitmap(p); var (px, mask) = ThumbMask(bmp); idx.Entries.Add(new Entry(file, names.ToArray(), px, mask)); }
            catch (Exception) { /* a damaged download: left out */ }
        }
        return idx;
    }

    public void Save(string path) => File.WriteAllText(path, JsonSerializer.Serialize(new { size = Size, icons = Entries.Select(e => new { icon = e.Icon, items = e.Items, px = Convert.ToBase64String(e.Pixels), m = Convert.ToBase64String(e.Mask) }) }));

    public static IconIndex Load(string json)
    {
        var idx = new IconIndex();
        using var doc = JsonDocument.Parse(json);
        foreach (var e in doc.RootElement.GetProperty("icons").EnumerateArray())
            idx.Entries.Add(new Entry(e.GetProperty("icon").GetString() ?? "", e.GetProperty("items").EnumerateArray().Select(x => x.GetString() ?? "").ToArray(), Convert.FromBase64String(e.GetProperty("px").GetString() ?? ""), Convert.FromBase64String(e.GetProperty("m").GetString() ?? "")));
        return idx;
    }
}
