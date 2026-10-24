using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace KinahLedger.Ocr;

/// <summary>
/// The whole game window shrunk to a small picture once per pass (Prompt 9, the scene classifier): <see cref="Width"/>
/// pixels wide, its height in proportion. From the game's own frames when window capture runs (four samples averaged
/// per pixel, straight out of the newest frame, nothing copied at full size); otherwise shrunk from the screen by GDI,
/// and then only while the game is the active window, so another window is never what gets classified. Nothing here
/// is kept or sent anywhere: the picture lives for one pass.
/// </summary>
public static class FrameThumb
{
    public const int Width = 256;

    /// <summary>The thumbnail's size for a game window of <paramref name="game"/>'s size.</summary>
    public static Size SizeFor(Size game) => new(Width, Math.Max(32, (int)Math.Round(Width * game.Height / (double)Math.Max(1, game.Width))));

    /// <summary>The game window <paramref name="game"/> (screen pixels) as a thumbnail, or null when it can't be had now.</summary>
    public static Bitmap? Capture(Rectangle game, IEnumerable<string> processHints)
    {
        if (game.Width < 64 || game.Height < 64) return null;
        var size = SizeFor(game.Size);
        if (ScreenReader.UseWindowCapture)
        {
            WindowCapture.Track(GameWindow.Handle);
            if (WindowCapture.TryThumb(game, size.Width, size.Height) is { } fromGame) return fromGame;
        }
        // from the screen only while the game is in front: anything over it would be classified instead
        if (!GameWindow.InFront(processHints)) return null;
        return FromScreen(game, size);
    }

    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("gdi32.dll")] static extern int SetStretchBltMode(IntPtr dc, int mode);
    [DllImport("gdi32.dll")] static extern bool StretchBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, int sw, int sh, int rop);
    const int ColorOnColor = 3, SrcCopy = 0x00CC0020;

    /// <summary>The screen's pixels under <paramref name="game"/>, shrunk by GDI in one step (no full-size copy).</summary>
    static Bitmap? FromScreen(Rectangle game, Size size)
    {
        var dst = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppArgb);
        IntPtr screen = GetDC(IntPtr.Zero);
        try
        {
            using var g = Graphics.FromImage(dst);
            var hdc = g.GetHdc();
            try
            {
                SetStretchBltMode(hdc, ColorOnColor);
                if (!StretchBlt(hdc, 0, 0, size.Width, size.Height, screen, game.X, game.Y, game.Width, game.Height, SrcCopy)) { dst.Dispose(); return null; }
            }
            finally { g.ReleaseHdc(hdc); }
            return dst;
        }
        catch (Exception) { dst.Dispose(); return null; }
        finally { ReleaseDC(IntPtr.Zero, screen); }
    }

    /// <summary>A whole-window picture (a test's synthetic frame, a saved screenshot) shrunk the same way window capture shrinks the game.</summary>
    public static unsafe Bitmap Shrink(Bitmap full)
    {
        var size = SizeFor(full.Size);
        var dst = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppArgb);
        var sd = full.LockBits(new Rectangle(0, 0, full.Width, full.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var dd = dst.LockBits(new Rectangle(0, 0, size.Width, size.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try { Sample((byte*)sd.Scan0, sd.Stride, new Rectangle(0, 0, full.Width, full.Height), (byte*)dd.Scan0, dd.Stride, size.Width, size.Height); }
        finally { full.UnlockBits(sd); dst.UnlockBits(dd); }
        return dst;
    }

    /// <summary>
    /// Shrinks <paramref name="area"/> of a BGRA picture (<paramref name="src"/>, <paramref name="srcStride"/>) into a w by h
    /// BGRA picture: each output pixel the average of four samples at the quarter points of its cell.
    /// </summary>
    internal static unsafe void Sample(byte* src, int srcStride, Rectangle area, byte* dst, int dstStride, int w, int h)
    {
        double cw = area.Width / (double)w, ch = area.Height / (double)h;
        for (int y = 0; y < h; y++)
        {
            int y0 = area.Y + Math.Min(area.Height - 1, (int)((y + 0.25) * ch)), y1 = area.Y + Math.Min(area.Height - 1, (int)((y + 0.75) * ch));
            byte* r0 = src + (long)y0 * srcStride, r1 = src + (long)y1 * srcStride;
            byte* o = dst + (long)y * dstStride;
            for (int x = 0; x < w; x++)
            {
                int x0 = (area.X + Math.Min(area.Width - 1, (int)((x + 0.25) * cw))) * 4, x1 = (area.X + Math.Min(area.Width - 1, (int)((x + 0.75) * cw))) * 4;
                o[x * 4] = (byte)((r0[x0] + r0[x1] + r1[x0] + r1[x1] + 2) >> 2);
                o[x * 4 + 1] = (byte)((r0[x0 + 1] + r0[x1 + 1] + r1[x0 + 1] + r1[x1 + 1] + 2) >> 2);
                o[x * 4 + 2] = (byte)((r0[x0 + 2] + r0[x1 + 2] + r1[x0 + 2] + r1[x1 + 2] + 2) >> 2);
                o[x * 4 + 3] = 255;
            }
        }
    }
}
