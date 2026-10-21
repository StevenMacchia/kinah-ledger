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
    public static void DarkTitleBar(IntPtr hwnd) => DarkTitleBar(hwnd, true);

    /// <summary>Dark or light title bar to match the skin.</summary>
    public static void DarkTitleBar(IntPtr hwnd, bool dark)
    {
        int on = dark ? 1 : 0;
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

    static byte[]? _iconBytes;
    static MemoryStream AppIconStream()
    {
        if (_iconBytes == null)
        {
            using var r = typeof(Native).Assembly.GetManifestResourceStream("app.ico")!;
            using var m = new MemoryStream(); r.CopyTo(m); _iconBytes = m.ToArray();
        }
        return new MemoryStream(_iconBytes, false);
    }

    static Icon? _icon;
    /// <summary>The K tile from app.ico (tools/brand/make-icons.js), at the small-icon size this screen's DPI asks for, for the tray.</summary>
    public static Icon AppIcon()
    {
        if (_icon != null) return _icon;
        using var s = AppIconStream();
        _icon = new Icon(s, System.Windows.Forms.SystemInformation.SmallIconSize);
        return _icon;
    }

    /// <summary>The whole app.ico for a window: WPF takes the title-bar and Alt+Tab sizes from its frames.</summary>
    public static BitmapSource AppIconSource()
    {
        var s = BitmapFrame.Create(AppIconStream(), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        s.Freeze();
        return s;
    }
}
