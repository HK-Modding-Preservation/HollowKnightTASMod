using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace HollowKnightTAS.Companion.Services
{
    internal static class GameWindowCapture
    {
        // WGC captures the selected HWND's surface even when another app covers it.
        public static async Task<BitmapSource> CaptureAsync(IntPtr window)
        {
            if (!GraphicsCaptureSession.IsSupported()) throw new InvalidOperationException("系统不支持游戏窗口捕获。");
            var interop = GraphicsCaptureItem.As<ICaptureItemInterop>();
            var itemId = new Guid("79C3F95B-31F7-4EC2-A464-632EF5D30760");
            var pointer = interop.CreateForWindow(window, ref itemId);
            GraphicsCaptureItem item;
            try { item = WinRT.MarshalInterface<GraphicsCaptureItem>.FromAbi(pointer); }
            finally { Marshal.Release(pointer); }
            using var device = CreateDevice();
            using var pool = Direct3D11CaptureFramePool.CreateFreeThreaded(device,
                DirectXPixelFormat.B8G8R8A8UIntNormalized, 1, item.Size);
            using var capture = pool.CreateCaptureSession(item);
            capture.IsCursorCaptureEnabled = false;
            var ready = new TaskCompletionSource<BitmapSource>(TaskCreationOptions.RunContinuationsAsynchronously);
            var processing = 0;
            pool.FrameArrived += async (sender, _) =>
            {
                if (Interlocked.Exchange(ref processing, 1) != 0) return;
                try
                {
                    using var frame = sender.TryGetNextFrame();
                    if (frame == null) throw new InvalidOperationException("未收到游戏画面。");
                    using var bitmap = await SoftwareBitmap.CreateCopyFromSurfaceAsync(frame.Surface);
                    var length = checked(bitmap.PixelWidth * bitmap.PixelHeight * 4);
                    var buffer = new Windows.Storage.Streams.Buffer((uint)length);
                    bitmap.CopyToBuffer(buffer);
                    var bytes = new byte[length];
                    using var reader = DataReader.FromBuffer(buffer);
                    reader.ReadBytes(bytes);
                    var image = BitmapSource.Create(bitmap.PixelWidth, bitmap.PixelHeight, 96, 96,
                        PixelFormats.Bgra32, null, bytes, bitmap.PixelWidth * 4);
                    image.Freeze();
                    ready.TrySetResult(image);
                }
                catch (Exception exception) { ready.TrySetException(exception); }
            };
            capture.StartCapture();
            return await ready.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }

        private static IDirect3DDevice CreateDevice()
        {
            Marshal.ThrowExceptionForHR(D3D11CreateDevice(IntPtr.Zero, 1, IntPtr.Zero, 0x20,
                IntPtr.Zero, 0, 7, out var d3d, out _, out var context));
            IntPtr dxgi = IntPtr.Zero, inspectable = IntPtr.Zero;
            try
            {
                var iid = new Guid("54EC77FA-1377-44E6-8C32-88FD5F44C84C");
                Marshal.ThrowExceptionForHR(Marshal.QueryInterface(d3d, ref iid, out dxgi));
                Marshal.ThrowExceptionForHR(CreateDirect3D11DeviceFromDXGIDevice(dxgi, out inspectable));
                return WinRT.MarshalInterface<IDirect3DDevice>.FromAbi(inspectable);
            }
            finally
            {
                if (inspectable != IntPtr.Zero) Marshal.Release(inspectable);
                if (dxgi != IntPtr.Zero) Marshal.Release(dxgi);
                Marshal.Release(context);
                Marshal.Release(d3d);
            }
        }

        [ComImport, Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ICaptureItemInterop { IntPtr CreateForWindow(IntPtr window, ref Guid iid); }
        [DllImport("d3d11.dll")] private static extern int D3D11CreateDevice(IntPtr adapter, int driverType,
            IntPtr software, uint flags, IntPtr levels, uint count, uint sdk, out IntPtr device, out int level, out IntPtr context);
        [DllImport("d3d11.dll")] private static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgi, out IntPtr device);
    }
}
