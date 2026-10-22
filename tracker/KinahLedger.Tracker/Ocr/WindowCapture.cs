using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;
using WinRT;

namespace KinahLedger.Ocr;

/// <summary>
/// The game window's own pixels through Windows Graphics Capture, the way OBS and Discord capture a window (0.8,
/// Steven 2026-10-22). A plain screen copy reads whatever is on top, so a browser over the game turned into chat lines
/// and loot rows; this reads the game itself, covered or not (not minimized: a minimized window draws nothing).
///
/// One capture session runs while the game window is known, keeping only the newest frame; <see cref="TryCapture"/>
/// crops a screen rectangle out of it. Any failure (an older Windows, no GPU device, the window gone) returns null and
/// the caller copies the screen as before. On Windows 11 the yellow capture border is switched off; Windows 10 shows it.
/// </summary>
public static class WindowCapture
{
    [ComImport, Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown), ComVisible(true)]
    interface IGraphicsCaptureItemInterop
    {
        IntPtr CreateForWindow([In] IntPtr window, [In] ref Guid iid);
        IntPtr CreateForMonitor([In] IntPtr monitor, [In] ref Guid iid);
    }

    [DllImport("d3d11.dll", ExactSpelling = true)]
    static extern int D3D11CreateDevice(IntPtr adapter, int driverType, IntPtr software, uint flags, IntPtr featureLevels, uint levels, uint sdk, out IntPtr device, out int level, out IntPtr context);
    [DllImport("d3d11.dll", ExactSpelling = true)]
    static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr hWnd, int attr, out RECT value, int size);
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }

    static readonly Guid ItemIid = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");
    static readonly Guid DxgiDeviceIid = new("54EC77FA-1377-44E6-8C32-88FD5F44C84C");
    static readonly Guid Session3Iid = new("F2CDD966-22AE-5EA1-9596-3A289344C3BE");   // IGraphicsCaptureSession3: IsBorderRequired

    static readonly object Lock = new();
    static IntPtr _hwnd;
    static IDirect3DDevice? _device;
    static Direct3D11CaptureFramePool? _pool;
    static GraphicsCaptureSession? _session;
    static Windows.Graphics.SizeInt32 _size;
    static SoftwareBitmap? _last;
    static DateTime _lastAt;
    static bool _broken;

    /// <summary>True when frames are arriving from the game window.</summary>
    public static bool Active { get { lock (Lock) return _session != null && _last != null && (DateTime.UtcNow - _lastAt).TotalSeconds < 3; } }

    /// <summary>Starts (or moves) the capture to this window. Safe to call on every pass; does nothing when unsupported.</summary>
    public static void Track(IntPtr hwnd)
    {
        if (_broken || hwnd == IntPtr.Zero || !GraphicsCaptureSession.IsSupported()) return;
        lock (Lock)
        {
            if (hwnd == _hwnd && _session != null) return;
            StopLocked();
            try
            {
                _device ??= CreateDevice();
                var interop = GraphicsCaptureItem.As<IGraphicsCaptureItemInterop>();
                var iid = ItemIid;
                var ptr = interop.CreateForWindow(hwnd, ref iid);
                var item = GraphicsCaptureItem.FromAbi(ptr);
                Marshal.Release(ptr);
                _size = item.Size;
                _pool = Direct3D11CaptureFramePool.CreateFreeThreaded(_device, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, _size);
                _pool.FrameArrived += OnFrame;
                _session = _pool.CreateCaptureSession(item);
                _session.IsCursorCaptureEnabled = false;
                NoBorder(_session);
                _session.StartCapture();
                _hwnd = hwnd;
            }
            catch (Exception ex) { CrashLog.Note("window capture unavailable, copying the screen instead: " + ex.Message); StopLocked(); _broken = true; }
        }
    }

    public static void Stop() { lock (Lock) StopLocked(); }

    static void StopLocked()
    {
        try { _session?.Dispose(); } catch (Exception) { }
        try { if (_pool != null) { _pool.FrameArrived -= OnFrame; _pool.Dispose(); } } catch (Exception) { }
        _session = null; _pool = null; _hwnd = IntPtr.Zero;
        _last?.Dispose(); _last = null;
    }

    static void OnFrame(Direct3D11CaptureFramePool pool, object _)
    {
        try
        {
            using var frame = pool.TryGetNextFrame();
            if (frame == null) return;
            var bmp = SoftwareBitmap.CreateCopyFromSurfaceAsync(frame.Surface, BitmapAlphaMode.Premultiplied).AsTask().GetAwaiter().GetResult();
            lock (Lock)
            {
                _last?.Dispose(); _last = bmp; _lastAt = DateTime.UtcNow;
                if (frame.ContentSize.Width != _size.Width || frame.ContentSize.Height != _size.Height)
                {
                    _size = frame.ContentSize;
                    _pool?.Recreate(_device, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, _size);
                }
            }
        }
        catch (Exception) { /* the next frame tries again */ }
    }

    /// <summary>The screen rectangle cut from the newest frame of the game window; null when there is none to use.</summary>
    public static Bitmap? TryCapture(Rectangle screen)
    {
        lock (Lock)
        {
            if (_last == null || _hwnd == IntPtr.Zero || (DateTime.UtcNow - _lastAt).TotalSeconds > 3) return null;
            // the frame starts at the window's visible bounds (DWM's extended frame), not at its outer rectangle
            if (DwmGetWindowAttribute(_hwnd, 9, out var wr, Marshal.SizeOf<RECT>()) != 0 && !GetWindowRect(_hwnd, out wr)) return null;
            var local = new Rectangle(screen.X - wr.Left, screen.Y - wr.Top, screen.Width, screen.Height);
            var bounds = new Rectangle(0, 0, _last.PixelWidth, _last.PixelHeight);
            if (!bounds.Contains(local)) return null;
            return Crop(_last, local);
        }
    }

    static unsafe Bitmap Crop(SoftwareBitmap src, Rectangle r)
    {
        var dst = new Bitmap(r.Width, r.Height, PixelFormat.Format32bppArgb);
        var data = dst.LockBits(new Rectangle(0, 0, r.Width, r.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            using var buf = src.LockBuffer(BitmapBufferAccessMode.Read);
            using var reference = buf.CreateReference();
            var plane = buf.GetPlaneDescription(0);
            reference.As<IMemoryBufferByteAccess>().GetBuffer(out byte* p, out _);
            for (int y = 0; y < r.Height; y++)
            {
                byte* from = p + plane.StartIndex + (r.Y + y) * plane.Stride + r.X * 4;
                Buffer.MemoryCopy(from, (byte*)data.Scan0 + y * data.Stride, data.Stride, r.Width * 4);
            }
        }
        finally { dst.UnlockBits(data); }
        return dst;
    }

    [ComImport, Guid("5B0D3235-4DBA-4D44-865E-8F1D0E4FD04D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    unsafe interface IMemoryBufferByteAccess { void GetBuffer(out byte* buffer, out uint capacity); }

    static IDirect3DDevice CreateDevice()
    {
        // hardware device with BGRA support (0x20), the format the frames come in
        int hr = D3D11CreateDevice(IntPtr.Zero, 1, IntPtr.Zero, 0x20, IntPtr.Zero, 0, 7, out var d3d, out _, out var ctx);
        if (hr < 0) throw new COMException("no Direct3D device", hr);
        try
        {
            var iid = DxgiDeviceIid;
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(d3d, ref iid, out var dxgi));
            try
            {
                Marshal.ThrowExceptionForHR(CreateDirect3D11DeviceFromDXGIDevice(dxgi, out var inspectable));
                var dev = MarshalInterface<IDirect3DDevice>.FromAbi(inspectable);
                Marshal.Release(inspectable);
                return dev;
            }
            finally { Marshal.Release(dxgi); }
        }
        finally { Marshal.Release(d3d); if (ctx != IntPtr.Zero) Marshal.Release(ctx); }
    }

    /// <summary>
    /// Windows 11 lets an app capture without the yellow border (IGraphicsCaptureSession3.IsBorderRequired). The SDK this
    /// build targets doesn't project it, so it is set through the interface's vtable; on Windows 10 there is no such
    /// interface and the border stays.
    /// </summary>
    static unsafe void NoBorder(GraphicsCaptureSession session)
    {
        try
        {
            var unk = ((IWinRTObject)session).NativeObject.ThisPtr;
            var iid = Session3Iid;
            if (Marshal.QueryInterface(unk, ref iid, out var s3) != 0 || s3 == IntPtr.Zero) { CrashLog.Note("window capture: this Windows can't hide the capture border"); return; }
            try
            {
                // IInspectable's six slots, then get_IsBorderRequired (6) and put_IsBorderRequired (7)
                var put = ((delegate* unmanaged[Stdcall]<IntPtr, byte, int>*)(*(IntPtr*)s3))[7];
                int hr = put(s3, 0);
                if (hr != 0) CrashLog.Note($"window capture: Windows kept its capture border (0x{hr:X8})");
            }
            finally { Marshal.Release(s3); }
        }
        catch (Exception) { /* the border stays */ }
    }
}
