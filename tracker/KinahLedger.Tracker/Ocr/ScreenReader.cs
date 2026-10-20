using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Text.Json;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace KinahLedger.Ocr;

/// <summary>
/// Reads text from one rectangle of the screen using the text recognition built into Windows 10/11.
/// Nothing but that rectangle is captured, and nothing touches the game.
/// </summary>
public sealed class ScreenReader
{
    readonly OcrEngine _engine;
    readonly Rectangle _region;
    readonly int _scale;
    readonly object _lock = new();
    Bitmap? _thumb;

    public Rectangle Region => _region;

    /// <summary>A small copy of the last capture, so the window can show exactly what's being read.</summary>
    public Bitmap? Thumb { get { lock (_lock) return _thumb == null ? null : (Bitmap)_thumb.Clone(); } }

    public ScreenReader(Rectangle region, int scale)
    {
        if (region.Width < 20 || region.Height < 10) throw new ArgumentException("the chat region is too small; run `pick` again");
        _region = region;
        _scale = Math.Clamp(scale, 1, 4);
        _engine = OcrEngine.TryCreateFromLanguage(new Language("en-US"))
                  ?? OcrEngine.TryCreateFromUserProfileLanguages()
                  ?? throw new InvalidOperationException("Windows text recognition isn't available. In Settings > Time & language > Language, add English (United States) with the optional features.");
    }

    public Bitmap Capture()
    {
        var bmp = new Bitmap(_region.Width, _region.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.CopyFromScreen(_region.Location, Point.Empty, _region.Size, CopyPixelOperation.SourceCopy);
        return bmp;
    }

    /// <summary>Upscale with smooth interpolation: chat text is small and the recognizer does much better at 2x.</summary>
    Bitmap Prepare(Bitmap src)
    {
        int max = (int)OcrEngine.MaxImageDimension;
        int w = Math.Min(src.Width * _scale, max), h = Math.Min(src.Height * _scale, max);
        var dst = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(dst);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.SmoothingMode = SmoothingMode.HighQuality;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.DrawImage(src, new Rectangle(0, 0, w, h));
        return dst;
    }

    public async Task<List<string>> ReadAsync(string? savePng = null)
    {
        using var raw = Capture();
        if (savePng != null) raw.Save(savePng, ImageFormat.Png);
        var k = Math.Min(1.0, Math.Min(166.0 / raw.Width, 76.0 / raw.Height));
        var small = new Bitmap(raw, new Size(Math.Max(1, (int)(raw.Width * k)), Math.Max(1, (int)(raw.Height * k))));
        lock (_lock) { _thumb?.Dispose(); _thumb = small; }
        using var bmp = Prepare(raw);
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Bmp);
        ms.Position = 0;
        using var ras = ms.AsRandomAccessStream();
        var decoder = await BitmapDecoder.CreateAsync(ras);
        using var soft = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
        var result = await _engine.RecognizeAsync(soft);
        return result.Lines.Select(l => l.Text.Trim()).Where(t => t.Length > 0).ToList();
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

    public static Rectangle? Parse(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var parts = s.Split(',', StringSplitOptions.TrimEntries);
        return parts.Length == 4 && parts.All(x => int.TryParse(x, out _)) ? new Rectangle(int.Parse(parts[0]), int.Parse(parts[1]), int.Parse(parts[2]), int.Parse(parts[3])) : null;
    }
}
