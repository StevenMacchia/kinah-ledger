using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Text.Json;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace KinahLedger.Ocr;

/// <summary>A recognized word and where it sits on screen, in screen pixels.</summary>
public sealed record OcrWordBox(string Text, Rectangle Box);

/// <summary>A recognized line and where it sits on screen, in screen pixels. Words carry their own boxes, so a table row can be split into columns.</summary>
public sealed record OcrLineBox(string Text, Rectangle Box, IReadOnlyList<OcrWordBox>? Words = null);

/// <summary>
/// Reads text from one rectangle of the screen using the text recognition built into Windows 10/11.
/// Nothing but that rectangle is captured, and nothing touches the game.
/// </summary>
public sealed class ScreenReader
{
    static OcrEngine? _shared;
    static OcrEngine NewEngine() => OcrEngine.TryCreateFromLanguage(new Language("en-US")) ?? OcrEngine.TryCreateFromUserProfileLanguages() ?? Engine;
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

    /// <summary>
    /// The pixels of a screen rectangle: cut from the game window's own frames when window capture runs (so a window on
    /// top of the game is never read), else copied from the screen. Settings: ocr.windowCapture (on by default).
    /// </summary>
    public static Bitmap Capture(Rectangle region)
    {
        if (UseWindowCapture)
        {
            WindowCapture.Track(GameWindow.Handle);
            if (WindowCapture.TryCapture(region) is { } fromGame) return fromGame;
        }
        var bmp = new Bitmap(region.Width, region.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.CopyFromScreen(region.Location, Point.Empty, region.Size, CopyPixelOperation.SourceCopy);
        return bmp;
    }

    public Bitmap Capture() => Capture(_region);

    /// <summary>Read the game window's own pixels when Windows allows it (Ocr/WindowCapture.cs). Off: copy the screen.</summary>
    public static bool UseWindowCapture { get; set; } = true;

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

    /// <summary>
    /// The target bar's enemy name is gold or yellow, often over a bright, busy sky or rock where plain brightness has no
    /// contrast. This keeps only warm, saturated, bright pixels (the name; white UI text and the scenery drop out) as
    /// black on white for the recognizer.
    /// </summary>
    public static unsafe Bitmap NameMask(Bitmap src)
    {
        var dst = new Bitmap(src.Width, src.Height, PixelFormat.Format24bppRgb);
        var rect = new Rectangle(0, 0, src.Width, src.Height);
        var rs = src.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var ds = dst.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
        try
        {
            for (int y = 0; y < src.Height; y++)
            {
                byte* s = (byte*)rs.Scan0 + y * rs.Stride;
                byte* d = (byte*)ds.Scan0 + y * ds.Stride;
                for (int x = 0; x < src.Width; x++, s += 4, d += 3)
                {
                    int b = s[0], g = s[1], r = s[2];
                    bool gold = r >= 190 && g >= 140 && b <= 110 && r - b >= 110 && g - b >= 70;
                    byte o = gold ? (byte)0 : (byte)255;
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

    // up to three recognitions at once, each on its own recognizer: the chat and the loot list beside the character are
    // read side by side, so a pass takes the time of the slower one instead of both (2026-10-22: rows up for three seconds
    // were read once during fast kills). The Market loop takes a free one like the others.
    static readonly SemaphoreSlim Gate = new(3, 3);
    static readonly System.Collections.Concurrent.ConcurrentBag<OcrEngine> Pool = new();

    static async Task<OcrResult> Recognize(Bitmap bmp)
    {
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Bmp);
        ms.Position = 0;
        using var ras = ms.AsRandomAccessStream();
        var decoder = await BitmapDecoder.CreateAsync(ras);
        using var soft = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
        await Gate.WaitAsync();
        var eng = Pool.TryTake(out var free) ? free : _shared == null ? Engine : NewEngine();
        try { return await eng.RecognizeAsync(soft); }
        finally { Pool.Add(eng); Gate.Release(); }
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

    /// <summary>Reads any screen rectangle and returns each line with its position, for finding the chat, the zone label and the Market.</summary>
    public static async Task<List<OcrLineBox>> ReadBoxesAsync(Rectangle region, int scale, bool boost = false)
    {
        using var raw = Capture(region);
        return await ReadBoxesAsync(raw, region.Location, scale, boost);
    }

    /// <summary>The same for a bitmap already in hand (a crop of a screenshot, in tests); origin is where its top-left sits on screen.</summary>
    public static async Task<List<OcrLineBox>> ReadBoxesAsync(Bitmap raw, Point origin, int scale, bool boost = false)
    {
        using var src = boost ? Boost(raw) : (Bitmap)raw.Clone();
        var (bmp, f) = Prepare(src, scale);
        using (bmp)
        {
            var result = await Recognize(bmp);
            var list = new List<OcrLineBox>();
            foreach (var line in result.Lines)
            {
                double l = double.MaxValue, t = double.MaxValue, r = 0, b = 0;
                var words = new List<OcrWordBox>();
                foreach (var w in line.Words)
                {
                    var wb = w.BoundingRect;
                    l = Math.Min(l, wb.X); t = Math.Min(t, wb.Y); r = Math.Max(r, wb.X + wb.Width); b = Math.Max(b, wb.Y + wb.Height);
                    var wt = w.Text.Trim();
                    if (wt.Length > 0) words.Add(new OcrWordBox(wt, Rectangle.FromLTRB(origin.X + (int)(wb.X / f), origin.Y + (int)(wb.Y / f), origin.X + (int)Math.Ceiling((wb.X + wb.Width) / f), origin.Y + (int)Math.Ceiling((wb.Y + wb.Height) / f))));
                }
                if (r <= 0) continue;
                var box = Rectangle.FromLTRB(origin.X + (int)(l / f), origin.Y + (int)(t / f), origin.X + (int)Math.Ceiling(r / f), origin.Y + (int)Math.Ceiling(b / f));
                var text = line.Text.Trim();
                if (text.Length > 0) list.Add(new OcrLineBox(text, box, words));
            }
            return list;
        }
    }
}

/// <summary>A picked rectangle (the chat, or the Market), kept per user so choosing it is a one-time step.</summary>
public static class RegionStore
{
    public const string ChatFile = "chat-region.json", MarketFile = "market-region.json";

    static string PathFor(string file)
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KinahLedger");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, file);
    }

    public static Rectangle? Load(string file = ChatFile)
    {
        var p = PathFor(file);
        if (!File.Exists(p)) return null;
        var r = JsonSerializer.Deserialize<int[]>(File.ReadAllText(p));
        return r is { Length: 4 } ? new Rectangle(r[0], r[1], r[2], r[3]) : null;
    }

    public static void Save(Rectangle r, string file = ChatFile) => File.WriteAllText(PathFor(file), JsonSerializer.Serialize(new[] { r.X, r.Y, r.Width, r.Height }));
    public static void Clear(string file = ChatFile) { var p = PathFor(file); if (File.Exists(p)) File.Delete(p); }

    public static Rectangle? Parse(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var parts = s.Split(',', StringSplitOptions.TrimEntries);
        return parts.Length == 4 && parts.All(x => int.TryParse(x, out _)) ? new Rectangle(int.Parse(parts[0]), int.Parse(parts[1]), int.Parse(parts[2]), int.Parse(parts[3])) : null;
    }
}
