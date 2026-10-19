// SPDX-License-Identifier: MIT
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace WorkTrail.Services;

/// <summary>Acquires one compositor frame of a window on the serialized capture worker.</summary>
internal static class WindowsGraphicsCaptureService
{
    private static readonly Guid CaptureInteropId = new("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356");
    private static readonly Guid CaptureItemId = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");
    private static readonly Guid DxgiDeviceId = new("54EC77FA-1377-44E6-8C32-88FD5F44C84C");
    private static readonly TimeSpan FrameTimeout = TimeSpan.FromSeconds(3);

    internal static bool TryReadWindowBounds(IntPtr window, out NativeMethods.Rect bounds)
    {
        // DWM bounds describe the compositor surface without GetWindowRect's invisible resize margins.
        if (DwmGetWindowAttribute(window, 9, out bounds, Marshal.SizeOf<NativeMethods.Rect>()) == 0
            && bounds.Right > bounds.Left && bounds.Bottom > bounds.Top) return true;
        return NativeMethods.GetWindowRect(window, out bounds);
    }

    internal static Bitmap Capture(IntPtr window)
    {
        if (!GraphicsCaptureSession.IsSupported())
            throw new NotSupportedException("Windows Graphics Capture is unavailable.");
        if (NativeMethods.IsIconic(window))
            throw new ScreenshotSceneChangedException("The selected window was minimized during capture.");

        // Capture the HWND surface directly. Never request WM_PRINT or activate/uncover a window.
        var item = CreateItem(window);
        var size = item.Size;
        if (size.Width <= 0 || size.Height <= 0)
            throw new ScreenshotSceneChangedException("The selected window has no capture surface.");
        using var device = CreateDevice();
        using var pool = Direct3D11CaptureFramePool.CreateFreeThreaded(device,
            DirectXPixelFormat.B8G8R8A8UIntNormalized, 1, size);
        using var session = pool.CreateCaptureSession(item);
        session.IsCursorCaptureEnabled = false;
        session.StartCapture();

        var started = Stopwatch.GetTimestamp();
        while (Stopwatch.GetElapsedTime(started) < FrameTimeout)
        {
            using var frame = pool.TryGetNextFrame();
            if (frame is null)
            {
                Thread.Sleep(10);
                continue;
            }
            if (frame.ContentSize.Width != size.Width || frame.ContentSize.Height != size.Height)
                throw new ScreenshotSceneChangedException("The selected window resized during capture.");

            // The asynchronous GPU copy retains its surface until completion, including on timeout.
            var copy = CopyFrameAsync(frame.Surface, size.Width, size.Height);
            try
            {
                return copy.WaitAsync(FrameTimeout).GetAwaiter().GetResult();
            }
            catch (TimeoutException)
            {
                _ = copy.ContinueWith(completed =>
                {
                    if (completed.IsCompletedSuccessfully) completed.Result.Dispose();
                    else _ = completed.Exception;
                }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                throw;
            }
        }
        // Minimized, protected, closed or stalled targets must not fall back to desktop pixels.
        throw new TimeoutException("Windows did not provide a window capture frame within three seconds.");
    }

    private static async Task<Bitmap> CopyFrameAsync(IDirect3DSurface surface, int width, int height)
    {
        // Preserve compositor color channels and normalize opacity before WEBP encoding below.
        using var software = await SoftwareBitmap.CreateCopyFromSurfaceAsync(surface, BitmapAlphaMode.Premultiplied);
        if (software.PixelWidth != width || software.PixelHeight != height)
            throw new ScreenshotSceneChangedException("The captured surface dimensions changed.");
        var pixels = new byte[checked(width * height * 4)];
        var buffer = new Windows.Storage.Streams.Buffer((uint)pixels.Length);
        software.CopyToBuffer(buffer);
        using (var reader = DataReader.FromBuffer(buffer)) reader.ReadBytes(pixels);
        return CreateOpaqueBitmap(pixels, width, height);
    }

    internal static Bitmap CreateOpaqueBitmap(byte[] pixels, int width, int height)
    {
        // Window capture omits Desktop Acrylic's backdrop. Composite premultiplied foreground pixels
        // over neutral gray, readable with either light or dark text, instead of making transparent black opaque.
        // Opaque pixels, including genuine black content, remain byte-for-byte unchanged.
        const int neutralBackdrop = 117;
        for (var offset = 0; offset < pixels.Length; offset += 4)
        {
            var alpha = pixels[offset + 3];
            if (alpha == byte.MaxValue) continue;
            var backdrop = (neutralBackdrop * (byte.MaxValue - alpha) + 127) / byte.MaxValue;
            for (var channel = 0; channel < 3; channel++)
                pixels[offset + channel] = (byte)Math.Min(byte.MaxValue, pixels[offset + channel] + backdrop);
            pixels[offset + 3] = byte.MaxValue;
        }
        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppRgb);
        try
        {
            var data = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppRgb);
            try
            {
                for (var row = 0; row < height; row++)
                    Marshal.Copy(pixels, row * width * 4, IntPtr.Add(data.Scan0, row * data.Stride), width * 4);
            }
            finally { bitmap.UnlockBits(data); }
            return bitmap;
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
    }

    private static GraphicsCaptureItem CreateItem(IntPtr window)
    {
        using var factory = WinRT.ActivationFactory.Get("Windows.Graphics.Capture.GraphicsCaptureItem", CaptureInteropId);
        // IGraphicsCaptureItemInterop derives from IUnknown; CreateForWindow is its first method.
        var vtable = Marshal.ReadIntPtr(factory.ThisPtr);
        var create = Marshal.GetDelegateForFunctionPointer<CreateForWindow>(Marshal.ReadIntPtr(vtable, 3 * IntPtr.Size));
        var iid = CaptureItemId;
        IntPtr item = IntPtr.Zero;
        try
        {
            Marshal.ThrowExceptionForHR(create(factory.ThisPtr, window, ref iid, out item));
            return WinRT.MarshalInterface<GraphicsCaptureItem>.FromAbi(item)
                ?? throw new InvalidOperationException("Windows returned no capture item.");
        }
        finally { if (item != IntPtr.Zero) Marshal.Release(item); }
    }

    private static IDirect3DDevice CreateDevice()
    {
        IntPtr device = IntPtr.Zero, context = IntPtr.Zero, dxgi = IntPtr.Zero, inspectable = IntPtr.Zero;
        try
        {
            // Hardware device with BGRA support; native/driver failures propagate to the capture error path.
            Marshal.ThrowExceptionForHR(D3D11CreateDevice(IntPtr.Zero, 1, IntPtr.Zero, 0x20,
                IntPtr.Zero, 0, 7, out device, out _, out context));
            var iid = DxgiDeviceId;
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(device, in iid, out dxgi));
            Marshal.ThrowExceptionForHR(CreateDirect3D11DeviceFromDXGIDevice(dxgi, out inspectable));
            return WinRT.MarshalInterface<IDirect3DDevice>.FromAbi(inspectable)
                ?? throw new InvalidOperationException("Windows returned no graphics device.");
        }
        finally
        {
            if (inspectable != IntPtr.Zero) Marshal.Release(inspectable);
            if (dxgi != IntPtr.Zero) Marshal.Release(dxgi);
            if (context != IntPtr.Zero) Marshal.Release(context);
            if (device != IntPtr.Zero) Marshal.Release(device);
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CreateForWindow(IntPtr factory, IntPtr window, ref Guid iid, out IntPtr item);

    [DllImport("d3d11.dll", ExactSpelling = true)]
    private static extern int D3D11CreateDevice(IntPtr adapter, int driverType, IntPtr software, uint flags,
        IntPtr featureLevels, uint featureLevelCount, uint sdkVersion, out IntPtr device, out int featureLevel, out IntPtr immediateContext);

    [DllImport("d3d11.dll", ExactSpelling = true)]
    private static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);

    [DllImport("dwmapi.dll", ExactSpelling = true)]
    private static extern int DwmGetWindowAttribute(IntPtr window, uint attribute, out NativeMethods.Rect value, int size);
}
