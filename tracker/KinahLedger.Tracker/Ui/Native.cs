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

    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

    /// <summary>The process that owns the window the player is using now (0 when none, as during a switch).</summary>
    public static uint ForegroundPid() { GetWindowThreadProcessId(GetForegroundWindow(), out var pid); return pid; }

    delegate void WinEventProc(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);
    [DllImport("user32.dll")] static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEventProc proc, uint pid, uint thread, uint flags);
    [DllImport("user32.dll")] static extern bool UnhookWinEvent(IntPtr hook);
    const uint EVENT_SYSTEM_FOREGROUND = 0x3, WINEVENT_OUTOFCONTEXT = 0x0;

    /// <summary>
    /// Calls <paramref name="changed"/> on this thread whenever another window comes to the front (alt-tab, a click, the
    /// game closing), so the panel over the game can follow at once instead of on the next half-second tick. Needs this
    /// thread's message loop (the window's). Dispose to stop; null when Windows refused the hook.
    /// </summary>
    public static IDisposable? WatchForeground(Action changed)
    {
        var w = new ForegroundWatch(changed);
        return w.Hooked ? w : null;
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int index, IntPtr value);
    const int GWL_EXSTYLE = -20;
    const long WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;

    /// <summary>
    /// A locked panel: clicks pass through it to the game and it never takes focus (WS_EX_TRANSPARENT, WS_EX_NOACTIVATE).
    /// Off, it can be clicked and dragged again. Either way it stays out of Alt+Tab (WS_EX_TOOLWINDOW). The readers let
    /// our own click-through window through (GameWindow.Covered).
    /// </summary>
    public static void SetClickThrough(IntPtr hwnd, bool on)
    {
        long ex = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64() | WS_EX_TOOLWINDOW;
        ex = on ? ex | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE : ex & ~(WS_EX_TRANSPARENT | WS_EX_NOACTIVATE);
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(ex));
    }

    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    const uint SWP_NOSIZE = 0x1, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10;

    /// <summary>Moves a window to a spot in screen pixels, keeping its size, place in the stack and focus (right across monitors of different scaling).</summary>
    public static void MoveTo(IntPtr hwnd, int x, int y) => SetWindowPos(hwnd, IntPtr.Zero, x, y, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);

    sealed class ForegroundWatch : IDisposable
    {
        readonly WinEventProc _proc;   // kept here: Windows holds only a pointer to it
        IntPtr _hook;
        public bool Hooked => _hook != IntPtr.Zero;
        public ForegroundWatch(Action changed)
        {
            _proc = (_, _, _, _, _, _, _) => { try { changed(); } catch (Exception) { /* the window is closing */ } };
            _hook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, _proc, 0, 0, WINEVENT_OUTOFCONTEXT);
        }
        public void Dispose() { if (_hook != IntPtr.Zero) { UnhookWinEvent(_hook); _hook = IntPtr.Zero; } }
    }

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
