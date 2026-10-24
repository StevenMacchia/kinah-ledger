using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace KinahLedger.Ocr;

/// <summary>
/// Copies the pixels of a locked rectangle out of a bitmap, one row at a time. When <c>LockBits</c> is given a
/// rectangle smaller than the bitmap, <c>Scan0</c> points into the bitmap's own memory and <c>Stride</c> is the whole
/// bitmap's row length, so copying <c>Stride x Height</c> bytes from <c>Scan0</c> reads past the bitmap's end on a
/// rectangle near its bottom (an access violation that killed the tracker on 2026-10-24). The copy returned here is
/// packed: <c>width * 4</c> bytes per row, top row first, whatever the bitmap's own stride or orientation.
/// </summary>
public static class PixelRows
{
    public static byte[] Copy(BitmapData data, int width, int height)
    {
        int rowBytes = width * 4;
        var buf = new byte[rowBytes * height];
        for (int y = 0; y < height; y++)
            Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), buf, y * rowBytes, rowBytes);
        return buf;
    }
}
