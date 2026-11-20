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
///
/// The newest frame stays on the graphics card; it is copied into memory only when a read asks for it (2026-11-18). Until then
/// every frame the game drew was copied as it came: a whole 4K frame, about 20 ms and 33 MB each, up to ~45 times a second,
/// which was most of the tracker's CPU in a fight (2026-11-18: Steven's "super laggy" meter, 0.7 of a core on it alone)
/// while the reader used one frame in thirty.
///
/// Since 1.0.31 a read never brings the whole frame into memory: the graphics card copies just the rectangle asked for
/// into a small texture of the tracker's own, and only that comes back (<see cref="ReadOnCard"/>). The whole-frame copy
/// held two 44 MB pictures at 5120x2160 (ours and the driver's own copy behind CreateCopyFromSurfaceAsync) and made
/// another 44 MB of garbage each time (Steven 2026-11-20: "700 MB of RAM"). It stays as the fallback if the card's way
/// fails.
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
    /// <summary>The newest frame, still on the graphics card, and what frees it (the capture's frame).</summary>
    static IDirect3DSurface? _frame;
    static IDisposable? _frameOwner;
    /// <summary>The newest frame copied into memory, the frame number it is, and when it was copied.</summary>
    static SoftwareBitmap? _last;
    static long _lastNo = -1;
    static long _copiedAt;
    static DateTime _lastAt;
    static bool _broken;
    /// <summary>Set when cropping on the graphics card failed once: reads copy the whole frame from then on, as before 1.0.31.</summary>
    static bool _cardBroken;

    /// <summary>
    /// A copy this young serves the next read too: a pass's crops come one after another (the chat, the loot list, the
    /// target...), and each would otherwise wait ~20 ms for a whole frame of its own. A tenth of a second is well under the
    /// time between two passes (~0.5 s) and the settles that read again (0.3 s apart); before, a crop was up to ~40 ms old.
    /// </summary>
    const double ReuseMs = 100;

    /// <summary>Whole frames copied into memory so far (for measuring: copy-load). Zero while the card crops.</summary>
    public static long Copies { get; private set; }
    /// <summary>Reads the graphics card cropped, and the bytes they brought back (copy-load, accuracy-test).</summary>
    public static long CardReads { get; private set; }
    public static long CardBytes { get; private set; }

    /// <summary>
    /// The hook for reading several frames per line (multi-frame voting): counts every frame the game window delivers.
    /// A reader that saw frame N can skip the read until this moves on; <see cref="NewFrame"/> says the same as it
    /// happens, on a capture thread (keep the handler short: note it, read on your own loop).
    /// </summary>
    public static long FrameNumber { get; private set; }
    public static event Action<long>? NewFrame;

    /// <summary>True when frames are arriving from the game window.</summary>
    public static bool Active { get { lock (Lock) return _session != null && (_frame != null || _last != null) && (DateTime.UtcNow - _lastAt).TotalSeconds < 3; } }

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
        DropFrameLocked();
        _last?.Dispose(); _last = null; _lastNo = -1;
        CropStage.Release(); RowStage.Release();
    }

    static void DropFrameLocked()
    {
        try { _frameOwner?.Dispose(); } catch (Exception) { }
        _frame = null; _frameOwner = null;
    }

    /// <summary>A frame came: kept on the graphics card as the newest (the one before it freed); nothing is copied here.</summary>
    static void OnFrame(Direct3D11CaptureFramePool pool, object _)
    {
        try
        {
            var frame = pool.TryGetNextFrame();
            if (frame == null) return;
            lock (Lock)
            {
                Offer(frame.Surface, frame);
                if (frame.ContentSize.Width != _size.Width || frame.ContentSize.Height != _size.Height)
                {
                    _size = frame.ContentSize;
                    _pool?.Recreate(_device, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, _size);
                }
            }
        }
        catch (Exception) { /* the next frame tries again */ return; }
        try { NewFrame?.Invoke(FrameNumber); } catch (Exception) { /* a handler's error is not the capture's */ }
    }

    /// <summary>The newest frame: <paramref name="surface"/> on the graphics card, <paramref name="owner"/> freeing it (under the lock).</summary>
    static void Offer(IDirect3DSurface surface, IDisposable owner)
    {
        DropFrameLocked();
        _frame = surface; _frameOwner = owner; _lastAt = DateTime.UtcNow; FrameNumber++;
    }

    /// <summary>
    /// The newest frame in memory (under the lock): copied from the graphics card now unless the copy made last is of the
    /// newest frame or under <see cref="ReuseMs"/> old. Null when there is no frame or the copy failed.
    /// </summary>
    static SoftwareBitmap? NewestLocked()
    {
        if (_frame == null || _lastNo == FrameNumber) return _last;
        if (_last != null && System.Diagnostics.Stopwatch.GetElapsedTime(_copiedAt).TotalMilliseconds < ReuseMs) return _last;
        // the old copy goes first, so a whole frame (44 MB at 5120x2160) is held once, not twice during the copy (2026-11-20);
        // a failed copy then leaves none and the read copies the screen instead
        _last?.Dispose(); _last = null; _lastNo = -1;
        try
        {
            _last = SoftwareBitmap.CreateCopyFromSurfaceAsync(_frame, BitmapAlphaMode.Premultiplied).AsTask().GetAwaiter().GetResult();
            _lastNo = FrameNumber; _copiedAt = System.Diagnostics.Stopwatch.GetTimestamp();
            Copies++;
        }
        catch (Exception) { /* no frame in memory: the caller copies the screen */ }
        return _last;
    }

    /// <summary>For copy-load: frames from a surface of the test's own, as the capture's come, and crops of the newest as reads take them.</summary>
    internal static void OfferForTest(IDirect3DSurface surface) { lock (Lock) { Offer(surface, NoOwner.Instance); _hwnd = new IntPtr(1); } }
    internal static Bitmap? CropForTest(Rectangle r)
    {
        lock (Lock)
        {
            if (FrameSizeLocked() is not { } size) return null;
            r.Intersect(new Rectangle(Point.Empty, size));
            return r.Width > 0 && r.Height > 0 ? CropLocked(r) : null;
        }
    }
    internal static Bitmap? ThumbForTest(Rectangle r, int w, int h) { lock (Lock) return ThumbLocked(r, w, h); }
    internal static Bitmap CropOfForTest(SoftwareBitmap src, Rectangle r) => Crop(src, r);
    /// <summary>accuracy-test: true makes reads take the whole-frame way, as when the card's way fails.</summary>
    internal static bool NoCardForTest { get; set; }
    internal static void ResetForTest() { lock (Lock) { DropFrameLocked(); _last?.Dispose(); _last = null; _lastNo = -1; _hwnd = IntPtr.Zero; Copies = 0; CardReads = 0; CardBytes = 0; CropStage.Release(); RowStage.Release(); } }

    /// <summary>A picture as a frame on a graphics card of the test's own (accuracy-test); null when there is no Direct3D device.</summary>
    internal static unsafe IDirect3DSurface? SurfaceForTest(Bitmap pixels)
    {
        if (D3D11CreateDevice(IntPtr.Zero, 1, IntPtr.Zero, 0x20, IntPtr.Zero, 0, 7, out var dev, out _, out var ctx) < 0) return null;
        var data = pixels.LockBits(new Rectangle(0, 0, pixels.Width, pixels.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            Protect(dev);
            var desc = new TexDesc { Width = (uint)pixels.Width, Height = (uint)pixels.Height, MipLevels = 1, ArraySize = 1, Format = 87, SampleCount = 1, BindFlags = 0x8 /* shader resource */ };
            // D3D11_SUBRESOURCE_DATA: the pixels, the row pitch, the slice pitch
            var init = stackalloc IntPtr[2];
            init[0] = data.Scan0; *(uint*)(init + 1) = (uint)data.Stride;
            IntPtr tex;
            if (((delegate* unmanaged[Stdcall]<IntPtr, TexDesc*, IntPtr*, IntPtr*, int>)Slot(dev, 5))(dev, &desc, init, &tex) < 0) return null;
            try
            {
                var sid = new Guid("cafcb56c-6ac3-4889-bf47-9e23bbd260ec");   // IDXGISurface
                Marshal.ThrowExceptionForHR(Marshal.QueryInterface(tex, ref sid, out var dxgi));
                try
                {
                    Marshal.ThrowExceptionForHR(CreateDirect3D11SurfaceFromDXGISurface(dxgi, out var insp));
                    var surface = MarshalInterface<IDirect3DSurface>.FromAbi(insp);
                    Marshal.Release(insp);
                    return surface;
                }
                finally { Marshal.Release(dxgi); }
            }
            finally { Marshal.Release(tex); }
        }
        finally { pixels.UnlockBits(data); Marshal.Release(dev); if (ctx != IntPtr.Zero) Marshal.Release(ctx); }
    }
    [DllImport("d3d11.dll", ExactSpelling = true)]
    static extern int CreateDirect3D11SurfaceFromDXGISurface(IntPtr dxgiSurface, out IntPtr graphicsSurface);
    sealed class NoOwner : IDisposable { public static readonly NoOwner Instance = new(); public void Dispose() { } }

    /// <summary>The screen rectangle cut from the newest frame of the game window; null when there is none to use.</summary>
    public static Bitmap? TryCapture(Rectangle screen)
    {
        lock (Lock)
        {
            if (_hwnd == IntPtr.Zero || (DateTime.UtcNow - _lastAt).TotalSeconds > 3 || (_frame == null && _last == null)) return null;
            // the frame starts at the window's visible bounds (DWM's extended frame), not at its outer rectangle
            if (DwmGetWindowAttribute(_hwnd, 9, out var wr, Marshal.SizeOf<RECT>()) != 0 && !GetWindowRect(_hwnd, out wr)) return null;
            return CropLocked(new Rectangle(screen.X - wr.Left, screen.Y - wr.Top, screen.Width, screen.Height));
        }
    }

    /// <summary>
    /// The screen rectangle (the game window) shrunk to <paramref name="w"/> by <paramref name="h"/> straight out of the
    /// newest frame, four samples per pixel, never copied at full size (FrameThumb, the scene classifier). Null when there
    /// is no frame to use.
    /// </summary>
    public static Bitmap? TryThumb(Rectangle screen, int w, int h)
    {
        lock (Lock)
        {
            if (_hwnd == IntPtr.Zero || (DateTime.UtcNow - _lastAt).TotalSeconds > 3 || (_frame == null && _last == null)) return null;
            if (DwmGetWindowAttribute(_hwnd, 9, out var wr, Marshal.SizeOf<RECT>()) != 0 && !GetWindowRect(_hwnd, out wr)) return null;
            return ThumbLocked(new Rectangle(screen.X - wr.Left, screen.Y - wr.Top, screen.Width, screen.Height), w, h);
        }
    }

    static bool UseCard => !_cardBroken && !NoCardForTest;

    /// <summary>The newest frame's size (under the lock), from the card or the copy in memory; null with no frame.</summary>
    static Size? FrameSizeLocked()
    {
        if (UseCard && _frame != null)
        {
            var tex = TextureOf(_frame, out var d);
            if (tex != IntPtr.Zero) { Marshal.Release(tex); return new Size((int)d.Width, (int)d.Height); }
        }
        return NewestLocked() is { } last ? new Size(last.PixelWidth, last.PixelHeight) : null;
    }

    /// <summary>Frame pixels <paramref name="local"/> of the newest frame (under the lock): null when outside it or there is none.</summary>
    static unsafe Bitmap? CropLocked(Rectangle local)
    {
        if (UseCard && _frame != null)
        {
            var tex = TextureOf(_frame, out var d);
            if (tex != IntPtr.Zero)
            {
                try
                {
                    if (!new Rectangle(0, 0, (int)d.Width, (int)d.Height).Contains(local)) return null;
                    var dst = new Bitmap(local.Width, local.Height, PixelFormat.Format32bppArgb);
                    var data = dst.LockBits(new Rectangle(0, 0, local.Width, local.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                    bool ok;
                    try
                    {
                        int rows = local.Height, rowBytes = local.Width * 4, stride = data.Stride; byte* to = (byte*)data.Scan0;
                        ok = ReadOnCard(tex, d, CropStage, local, null, (p, pitch) =>
                        {
                            for (int y = 0; y < rows; y++) Buffer.MemoryCopy(p + (long)y * pitch, to + (long)y * stride, stride, rowBytes);
                        });
                    }
                    finally { dst.UnlockBits(data); }
                    if (ok) return dst;
                    dst.Dispose();
                }
                finally { Marshal.Release(tex); }
            }
            CardFailed();
        }
        if (NewestLocked() is not { } last || !new Rectangle(0, 0, last.PixelWidth, last.PixelHeight).Contains(local)) return null;
        return Crop(last, local);
    }

    /// <summary>The thumbnail of frame pixels <paramref name="local"/> (under the lock); null when too small or there is no frame.</summary>
    static unsafe Bitmap? ThumbLocked(Rectangle local, int w, int h)
    {
        if (UseCard && _frame != null)
        {
            var tex = TextureOf(_frame, out var d);
            if (tex != IntPtr.Zero)
            {
                try
                {
                    local.Intersect(new Rectangle(0, 0, (int)d.Width, (int)d.Height));
                    if (local.Width < w || local.Height < h) return null;
                    // only the two rows each thumbnail row samples come back: 2h rows of the window's width, not the whole frame
                    var rows = new int[2 * h];
                    for (int y = 0; y < h; y++) { var (y0, y1) = FrameThumb.RowsOf(y, local.Height, h); rows[2 * y] = local.Y + y0; rows[2 * y + 1] = local.Y + y1; }
                    var dst = new Bitmap(w, h, PixelFormat.Format32bppArgb);
                    var data = dst.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                    bool ok;
                    try
                    {
                        byte* to = (byte*)data.Scan0; int stride = data.Stride, width = local.Width;
                        ok = ReadOnCard(tex, d, RowStage, local, rows, (p, pitch) =>
                        {
                            for (int y = 0; y < h; y++) FrameThumb.SampleRow(p + (long)(2 * y) * pitch, p + (long)(2 * y + 1) * pitch, 0, width, to + (long)y * stride, w);
                        });
                    }
                    finally { dst.UnlockBits(data); }
                    if (ok) return dst;
                    dst.Dispose();
                }
                finally { Marshal.Release(tex); }
            }
            CardFailed();
        }
        if (NewestLocked() is not { } last) return null;
        local.Intersect(new Rectangle(0, 0, last.PixelWidth, last.PixelHeight));
        if (local.Width < w || local.Height < h) return null;
        var full = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        var fd = full.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            using var buf = last.LockBuffer(BitmapBufferAccessMode.Read);
            using var reference = buf.CreateReference();
            var plane = buf.GetPlaneDescription(0);
            reference.As<IMemoryBufferByteAccess>().GetBuffer(out byte* p, out _);
            FrameThumb.Sample(p + plane.StartIndex, plane.Stride, local, (byte*)fd.Scan0, fd.Stride, w, h);
        }
        catch (Exception) { full.UnlockBits(fd); full.Dispose(); return null; }
        full.UnlockBits(fd);
        return full;
    }

    static void CardFailed()
    {
        if (_cardBroken || NoCardForTest) return;
        _cardBroken = true;
        CrashLog.Note("window capture: the graphics card can't crop, copying whole frames instead");
    }

    // ---- cropping on the graphics card (1.0.31): Direct3D 11 through the interfaces' vtables, no wrapper library ----

    [StructLayout(LayoutKind.Sequential)] struct TexDesc { public uint Width, Height, MipLevels, ArraySize, Format, SampleCount, SampleQuality, Usage, BindFlags, CpuAccess, Misc; }
    [StructLayout(LayoutKind.Sequential)] struct Box { public uint Left, Top, Front, Right, Bottom, Back; }
    [StructLayout(LayoutKind.Sequential)] struct MappedRes { public IntPtr Data; public uint RowPitch, DepthPitch; }
    unsafe delegate void RowsReader(byte* data, int pitch);

    static readonly Guid DxgiAccessIid = new("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1");   // IDirect3DDxgiInterfaceAccess
    static readonly Guid Texture2DIid = new("6F15AAF2-D208-4E89-9AB4-489535D34F9C");    // ID3D11Texture2D
    static readonly Guid MultithreadIid = new("9B7E4E00-342C-4106-A19F-4F2704F689F0");  // ID3D10Multithread

    static unsafe IntPtr Slot(IntPtr o, int i) => (*(IntPtr**)o)[i];

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr CreateWaitableTimerExW(IntPtr sa, string? name, uint flags, uint access);
    [DllImport("kernel32.dll")] static extern bool SetWaitableTimer(IntPtr timer, ref long due, int period, IntPtr completion, IntPtr arg, bool resume);
    [DllImport("kernel32.dll")] static extern uint WaitForSingleObject(IntPtr h, uint ms);
    [ThreadStatic] static IntPtr _napTimer;

    /// <summary>
    /// Half a millisecond's sleep on a high-resolution timer (Windows 10 1803+), for waiting on the card: Thread.Sleep(1)
    /// sleeps a whole timer tick (up to 15.6 ms: 32 ms a crop) and a blocking map spins the driver's thread (0.11 of a core
    /// at 20 crops a second). copy-load at 5120x2160 with this: ~10 ms a crop, 0.02 of a core. Falls back to Sleep(1)
    /// where there is no such timer.
    /// </summary>
    static void Nap()
    {
        if (_napTimer == IntPtr.Zero) _napTimer = CreateWaitableTimerExW(IntPtr.Zero, null, 0x2 /* HIGH_RESOLUTION */, 0x1F0003);
        long due = -5000;   // 0.5 ms, relative, in 100 ns
        if (_napTimer == IntPtr.Zero || !SetWaitableTimer(_napTimer, ref due, 0, IntPtr.Zero, IntPtr.Zero, false)) { Thread.Sleep(1); return; }
        WaitForSingleObject(_napTimer, 50);
    }

    /// <summary>
    /// A staging texture of the tracker's own that a crop is copied into and read back from: kept between reads (made
    /// again only when a bigger crop or another device comes), released when the capture stops. One for crops, one for the
    /// thumbnail's rows, so neither grows to the other's shape.
    /// </summary>
    sealed class Stage
    {
        public IntPtr Tex, Dev; public int W, H; public uint Format;
        public void Release() { if (Tex != IntPtr.Zero) Marshal.Release(Tex); Tex = IntPtr.Zero; Dev = IntPtr.Zero; W = H = 0; }
    }
    static readonly Stage CropStage = new(), RowStage = new();

    /// <summary>The Direct3D texture behind a capture surface (a reference the caller releases), and its description; zero when there is none.</summary>
    static unsafe IntPtr TextureOf(IDirect3DSurface surface, out TexDesc desc)
    {
        desc = default;
        if (surface is not IWinRTObject w) return IntPtr.Zero;
        var iid = DxgiAccessIid;
        if (Marshal.QueryInterface(w.NativeObject.ThisPtr, ref iid, out var access) != 0) return IntPtr.Zero;
        try
        {
            var tiid = Texture2DIid; IntPtr tex;
            if (((delegate* unmanaged[Stdcall]<IntPtr, Guid*, IntPtr*, int>)Slot(access, 3))(access, &tiid, &tex) < 0 || tex == IntPtr.Zero) return IntPtr.Zero;
            TexDesc d;
            ((delegate* unmanaged[Stdcall]<IntPtr, TexDesc*, void>)Slot(tex, 10))(tex, &d);   // ID3D11Texture2D::GetDesc
            desc = d;
            return tex;
        }
        catch (Exception) { return IntPtr.Zero; }
        finally { Marshal.Release(access); }
    }

    /// <summary>
    /// Copies <paramref name="area"/> of the frame texture (or, with <paramref name="rows"/>, just those rows of it, one
    /// under another) into <paramref name="stage"/> on the card, maps it and hands the rows to <paramref name="read"/>.
    /// False when the card refused any step (the caller copies the whole frame instead).
    /// </summary>
    static unsafe bool ReadOnCard(IntPtr tex, in TexDesc d, Stage stage, Rectangle area, int[]? rows, RowsReader read)
    {
        int w = area.Width, h = rows?.Length ?? area.Height;
        IntPtr dev = IntPtr.Zero, ctx = IntPtr.Zero;
        try
        {
            ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, void>)Slot(tex, 3))(tex, &dev);     // ID3D11DeviceChild::GetDevice
            if (dev == IntPtr.Zero) return false;
            ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, void>)Slot(dev, 40))(dev, &ctx);    // ID3D11Device::GetImmediateContext
            if (ctx == IntPtr.Zero || !EnsureStage(stage, dev, d.Format, w, h)) return false;
            var copy = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, uint, uint, uint, uint, IntPtr, uint, Box*, void>)Slot(ctx, 46);   // CopySubresourceRegion
            if (rows == null)
            {
                var b = new Box { Left = (uint)area.X, Top = (uint)area.Y, Right = (uint)area.Right, Bottom = (uint)area.Bottom, Back = 1 };
                copy(ctx, stage.Tex, 0, 0, 0, 0, tex, 0, &b);
            }
            else
                for (int i = 0; i < rows.Length; i++)
                {
                    var b = new Box { Left = (uint)area.X, Top = (uint)rows[i], Right = (uint)area.Right, Bottom = (uint)rows[i] + 1, Back = 1 };
                    copy(ctx, stage.Tex, 0, 0, (uint)i, 0, tex, 0, &b);
                }
            // sent now, then asked for without blocking until the card has done it: a blocking map spins the driver's thread
            ((delegate* unmanaged[Stdcall]<IntPtr, void>)Slot(ctx, 111))(ctx);   // Flush
            var map = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, uint, int, uint, MappedRes*, int>)Slot(ctx, 14);
            MappedRes m;
            int hr;
            var waited = System.Diagnostics.Stopwatch.StartNew();
            while ((hr = map(ctx, stage.Tex, 0, 1 /* READ */, 0x100000 /* DO_NOT_WAIT */, &m)) == unchecked((int)0x887A000A) /* still drawing */ && waited.ElapsedMilliseconds < 500)
                Nap();
            if (hr < 0 || m.Data == IntPtr.Zero) return false;
            try { read((byte*)m.Data, (int)m.RowPitch); }
            finally { ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, uint, void>)Slot(ctx, 15))(ctx, stage.Tex, 0); }   // Unmap
            CardReads++; CardBytes += (long)w * h * 4;
            return true;
        }
        catch (Exception) { return false; }
        finally { if (ctx != IntPtr.Zero) Marshal.Release(ctx); if (dev != IntPtr.Zero) Marshal.Release(dev); }
    }

    /// <summary>The stage at least w by h on this device in this format, made (bigger) when it isn't.</summary>
    static unsafe bool EnsureStage(Stage s, IntPtr dev, uint format, int w, int h)
    {
        if (s.Tex != IntPtr.Zero && s.Dev == dev && s.Format == format && s.W >= w && s.H >= h) return true;
        bool same = s.Tex != IntPtr.Zero && s.Dev == dev && s.Format == format;
        int nw = same ? Math.Max(w, s.W) : w, nh = same ? Math.Max(h, s.H) : h;
        s.Release();
        var desc = new TexDesc { Width = (uint)nw, Height = (uint)nh, MipLevels = 1, ArraySize = 1, Format = format, SampleCount = 1, Usage = 3 /* STAGING */, CpuAccess = 0x20000 /* READ */ };
        IntPtr tex;
        if (((delegate* unmanaged[Stdcall]<IntPtr, TexDesc*, IntPtr, IntPtr*, int>)Slot(dev, 5))(dev, &desc, IntPtr.Zero, &tex) < 0 || tex == IntPtr.Zero) return false;   // CreateTexture2D
        s.Tex = tex; s.Dev = dev; s.W = nw; s.H = nh; s.Format = format;
        return true;
    }

    /// <summary>
    /// Lets the capture's own threads and the reader share the device's one context (ID3D10Multithread): the reader's
    /// copies run on it while Windows delivers frames on its threads.
    /// </summary>
    static unsafe void Protect(IntPtr d3d)
    {
        var iid = MultithreadIid;
        if (Marshal.QueryInterface(d3d, ref iid, out var mt) != 0) return;
        try { ((delegate* unmanaged[Stdcall]<IntPtr, int, int>)Slot(mt, 5))(mt, 1); }   // SetMultithreadProtected(TRUE)
        finally { Marshal.Release(mt); }
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

    /// <summary>
    /// A bitmap as a Bgra8 SoftwareBitmap for the recognizer, its rows copied straight into the SoftwareBitmap's own
    /// memory: no managed buffer, no stream, nothing pinned (ScreenReader.Recognize).
    /// </summary>
    public static unsafe SoftwareBitmap ToSoftware(Bitmap src)
    {
        int w = src.Width, h = src.Height;
        var soft = new SoftwareBitmap(BitmapPixelFormat.Bgra8, w, h, BitmapAlphaMode.Premultiplied);
        var data = src.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            using var buf = soft.LockBuffer(BitmapBufferAccessMode.Write);
            using var reference = buf.CreateReference();
            var plane = buf.GetPlaneDescription(0);
            reference.As<IMemoryBufferByteAccess>().GetBuffer(out byte* p, out _);
            for (int y = 0; y < h; y++)
                Buffer.MemoryCopy((byte*)data.Scan0 + y * data.Stride, p + plane.StartIndex + y * plane.Stride, plane.Stride, w * 4);
        }
        catch { soft.Dispose(); throw; }
        finally { src.UnlockBits(data); }
        return soft;
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
            Protect(d3d);
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
