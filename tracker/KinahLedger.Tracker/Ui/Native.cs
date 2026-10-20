using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace KinahLedger.Ui;

/// <summary>Small Windows calls the window needs: dark title bar, a global hotkey, and bitmap conversions.</summary>
internal static class Native
{
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mods, uint vk);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr h);

    public const int WM_HOTKEY = 0x0312, MOD_CONTROL = 0x2, MOD_SHIFT = 0x4;

    /// <summary>Asks Windows 10/11 to draw this window's title bar dark, to match the content.</summary>
    public static void DarkTitleBar(IntPtr hwnd)
    {
        int on = 1;
        if (DwmSetWindowAttribute(hwnd, 20, ref on, sizeof(int)) != 0) DwmSetWindowAttribute(hwnd, 19, ref on, sizeof(int));
    }

    public static BitmapSource ToSource(Bitmap bmp)
    {
        var h = bmp.GetHbitmap(Color.Black);
        try
        {
            var s = Imaging.CreateBitmapSourceFromHBitmap(h, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            s.Freeze();
            return s;
        }
        finally { DeleteObject(h); }
    }

    static Icon? _icon;
    /// <summary>A gold hex mark drawn in code, so the window, tray and taskbar have an icon without shipping an .ico yet.</summary>
    public static Icon AppIcon()
    {
        if (_icon != null) return _icon;
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var dark = new SolidBrush(Color.FromArgb(26, 39, 64));
            using var gold = new SolidBrush(Color.FromArgb(227, 181, 91));
            using var edge = new Pen(Color.FromArgb(241, 208, 140), 1.5f);
            var hex = new[] { new PointF(16, 2), new PointF(29, 9), new PointF(29, 23), new PointF(16, 30), new PointF(3, 23), new PointF(3, 9) };
            g.FillPolygon(dark, hex); g.DrawPolygon(edge, hex);
            var inner = new[] { new PointF(16, 9), new PointF(22, 12.5f), new PointF(22, 19.5f), new PointF(16, 23), new PointF(10, 19.5f), new PointF(10, 12.5f) };
            g.FillPolygon(gold, inner);
        }
        _icon = Icon.FromHandle(bmp.GetHicon());
        return _icon;
    }

    public static BitmapSource AppIconSource()
    {
        var s = Imaging.CreateBitmapSourceFromHIcon(AppIcon().Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        s.Freeze();
        return s;
    }
}
