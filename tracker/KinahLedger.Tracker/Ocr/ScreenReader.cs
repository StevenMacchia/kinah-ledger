using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Text.Json;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace KinahLedger.Ocr;

/// <summary>A recognized line and where it sits on screen, in screen pixels.</summary>
public sealed record OcrLineBox(string Text, Rectangle Box);

/// <summary>
/// Reads text from one rectangle of the screen using the text recognition built into Windows 10/11.
/// Nothing but that rectangle is captured, and nothing touches the game.
/// </summary>
public sealed class ScreenReader
{
    static OcrEngine? _shared;
    static OcrEngine Engine => _shared ??= OcrEngine.TryCreateFromLanguage(new Language("en-US"))
        ?? OcrEngine.TryCreateFromUserProfileLanguages()
        ?? throw new InvalidOperationException("Windows text recognition isn't available. In Settings > Time & language > Language, add English (United States) with the optional features.");

    readonly Rectangle _region;
    readonly int _scale;
    readonly object _lock = new();
    Bitmap? _thumb;

    public Rectangle Region => _region;

    /// <summary>A small copy of the last capture, so the window can show exactly what's being read.</summary>
    public Bitmap? Thumb { get { lock (_lock) return _thumb == null ? null : (Bitmap)_thumb.Clone(); } }

    public ScreenReader(Rectangle region, int scale)
    {
        if (region.Width < 20 || region.Height < 10) throw new ArgumentException("the chat region is too small; detect or pick it again");
        _region = region;
        _scale = Math.Clamp(scale, 1, 4);
        _ = Engine;
    }

    public static Bitmap Capture(Rectangle region)
    {
        var bmp = new Bitmap(region.Width, region.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.CopyFromScreen(region.Location, Point.Empty, region.Size, CopyPixelOperation.SourceCopy);
        return bmp;
    }

    public Bitmap Capture() => Capture(_region);

    /// <summary>Upscale with smooth interpolation, keeping the aspect ratio and the recognizer's size limit. Returns the factor used.</summary>
    static (Bitmap bmp, double factor) Prepare(Bitmap src, int scale)
    {
        double max = OcrEngine.MaxImageDimension;
        double f = Math.Min(scale, Math.Min(max / src.Width, max / src.Height));
        int w = Math.Max(1, (int)(src.Width * f)), h = Math.Max(1, (int)(src.Height * f));
        var dst = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(dst);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.SmoothingMode = SmoothingMode.HighQuality;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.DrawImage(src, new Rectangle(0, 0, w, h));
        return (dst, f);
    }

    /// <summary>
    /// Makes coloured chat text readable: each pixel becomes its brightest channel (so green, blue and yellow
    /// item names are as bright as white text), the contrast is stretched, and the result is inverted to
    /// dark text on a light field, which the recognizer handles best.
    /// </summary>
    public static unsafe Bitmap Boost(Bitmap src)
    {
        var dst = new Bitmap(src.Width, src.Height, PixelFormat.Format24bppRgb);
        var rect = new Rectangle(0, 0, src.Width, src.Height);
        var rs = src.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var ds = dst.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
        try
        {
            const int lo = 70, hi = 235;
            for (int y = 0; y < src.Height; y++)
            {
                byte* s = (byte*)rs.Scan0 + y * rs.Stride;
                byte* d = (byte*)ds.Scan0 + y * ds.Stride;
                for (int x = 0; x < src.Width; x++, s += 4, d += 3)
                {
                    int v = Math.Max(s[0], Math.Max(s[1], s[2]));
                    v = (v - lo) * 255 / (hi - lo);
                    v = v < 0 ? 0 : v > 255 ? 255 : v;
                    byte o = (byte)(255 - v);
                    d[0] = o; d[1] = o; d[2] = o;
                }
            }
        }
        finally { src.UnlockBits(rs); dst.UnlockBits(ds); }
        return dst;
    }

    /// <summary>Recognizes a bitmap the way the live reader does: optional boost, then upscale.</summary>
    public static async Task<List<string>> ReadBitmapAsync(Bitmap raw, bool boost, int scale)
    {
        using var src = boost ? Boost(raw) : (Bitmap)raw.Clone();
        var (bmp, _) = Prepare(src, scale);
        using (bmp)
        {
            var result = await Recognize(bmp);
            return result.Lines.Select(l => l.Text.Trim()).Where(t => t.Length > 0).ToList();
        }
    }

    static async Task<OcrResult> Recognize(Bitmap bmp)
    {
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Bmp);
        ms.Position = 0;
        using var ras = ms.AsRandomAccessStream();
        var decoder = await BitmapDecoder.CreateAsync(ras);
        using var soft = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
        return await Engine.RecognizeAsync(soft);
    }

    /// <summary>Loads an image file (PNG, JPEG, WebP where Windows has the codec) into a 32-bit bitmap.</summary>
    public static async Task<Bitmap> LoadAsync(string path)
    {
        using var fs = File.OpenRead(path);
        using var ras = fs.AsRandomAccessStream();
        var decoder = await BitmapDecoder.CreateAsync(ras);
        using var soft = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
        var bmp = new Bitmap(soft.PixelWidth, soft.PixelHeight, PixelFormat.Format32bppArgb);
        var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            var buffer = new Windows.Storage.Streams.Buffer((uint)(data.Stride * bmp.Height));
            soft.CopyToBuffer(buffer);
            var bytes = System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions.ToArray(buffer);
            System.Runtime.InteropServices.Marshal.Copy(bytes, 0, data.Scan0, Math.Min(bytes.Length, data.Stride * bmp.Height));
        }
        finally { bmp.UnlockBits(data); }
        return bmp;
    }

    public async Task<List<string>> ReadAsync(string? savePng = null)
    {
        using var raw = Capture();
        if (savePng != null) raw.Save(savePng, ImageFormat.Png);
        var k = Math.Min(1.0, Math.Min(166.0 / raw.Width, 76.0 / raw.Height));
        var small = new Bitmap(raw, new Size(Math.Max(1, (int)(raw.Width * k)), Math.Max(1, (int)(raw.Height * k))));
        lock (_lock) { _thumb?.Dispose(); _thumb = small; }
        using var boosted = Boost(raw);
        var (bmp, _) = Prepare(boosted, _scale);
        using (bmp)
        {
            var result = await Recognize(bmp);
            return result.Lines.Select(l => l.Text.Trim()).Where(t => t.Length > 0).ToList();
        }
    }

    /// <summary>The whole game frame, scaled down, with the detected areas outlined: what the player sees as the live preview.</summary>
    public static Bitmap Frame(Rectangle game, int maxWidth, params (Rectangle box, Color color)[] marks)
    {
        using var raw = Capture(game);
        double k = Math.Min(1.0, maxWidth / (double)raw.Width);
        var bmp = new Bitmap(Math.Max(1, (int)(raw.Width * k)), Math.Max(1, (int)(raw.Height * k)), PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.InterpolationMode = InterpolationMode.HighQualityBilinear;
        g.DrawImage(raw, new Rectangle(0, 0, bmp.Width, bmp.Height));
        foreach (var (box, color) in marks)
        {
            if (box.IsEmpty) continue;
            using var pen = new Pen(color, 2f);
            using var fill = new SolidBrush(Color.FromArgb(40, color));
            var r = new Rectangle((int)((box.X - game.X) * k), (int)((box.Y - game.Y) * k), Math.Max(3, (int)(box.Width * k)), Math.Max(3, (int)(box.Height * k)));
            g.FillRectangle(fill, r);
            g.DrawRectangle(pen, r);
        }
        return bmp;
    }

    /// <summary>Reads any screen rectangle and returns each line with its position, for finding the chat and the zone label.</summary>
    public static async Task<List<OcrLineBox>> ReadBoxesAsync(Rectangle region, int scale)
    {
        using var raw = Capture(region);
        var (bmp, f) = Prepare(raw, scale);
        using (bmp)
        {
            var result = await Recognize(bmp);
            var list = new List<OcrLineBox>();
            foreach (var line in result.Lines)
            {
                double l = double.MaxValue, t = double.MaxValue, r = 0, b = 0;
                foreach (var w in line.Words) { l = Math.Min(l, w.BoundingRect.X); t = Math.Min(t, w.BoundingRect.Y); r = Math.Max(r, w.BoundingRect.X + w.BoundingRect.Width); b = Math.Max(b, w.BoundingRect.Y + w.BoundingRect.Height); }
                if (r <= 0) continue;
                var box = Rectangle.FromLTRB(region.X + (int)(l / f), region.Y + (int)(t / f), region.X + (int)Math.Ceiling(r / f), region.Y + (int)Math.Ceiling(b / f));
                var text = line.Text.Trim();
                if (text.Length > 0) list.Add(new OcrLineBox(text, box));
            }
            return list;
        }
    }
}

/// <summary>The picked chat rectangle, kept per user so `pick` is a one-time step.</summary>
public static class RegionStore
{
    static string PathFor()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KinahLedger");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "chat-region.json");
    }

    public static Rectangle? Load()
    {
        var p = PathFor();
        if (!File.Exists(p)) return null;
        var r = JsonSerializer.Deserialize<int[]>(File.ReadAllText(p));
        return r is { Length: 4 } ? new Rectangle(r[0], r[1], r[2], r[3]) : null;
    }

    public static void Save(Rectangle r) => File.WriteAllText(PathFor(), JsonSerializer.Serialize(new[] { r.X, r.Y, r.Width, r.Height }));
    public static void Clear() { var p = PathFor(); if (File.Exists(p)) File.Delete(p); }

    public static Rectangle? Parse(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var parts = s.Split(',', StringSplitOptions.TrimEntries);
        return parts.Length == 4 && parts.All(x => int.TryParse(x, out _)) ? new Rectangle(int.Parse(parts[0]), int.Parse(parts[1]), int.Parse(parts[2]), int.Parse(parts[3])) : null;
    }
}
