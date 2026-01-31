using OpenCvSharp;
using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using WinRT;

namespace Sai2Capture.Services
{
    /// <summary>
    /// Windows Graphics Capture API 截图服务
    /// 使用现代 WGC API 进行高性能窗口捕获
    /// 支持硬件加速和更好的兼容性
    /// </summary>
    public class WgcCaptureService : IDisposable
    {
        private readonly LogService _logService;
        private GraphicsCaptureItem? _captureItem;
        private Direct3D11CaptureFramePool? _framePool;
        private GraphicsCaptureSession? _session;
        private IDirect3DDevice? _device;
        private SharpDX.Direct3D11.Device? _d3dDevice;
        private bool _isCapturing;
        private Mat? _lastCapturedFrame;
        private readonly object _frameLock = new object();

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern int GetWindowThreadProcessId(IntPtr hWnd, out int lpdwProcessId);

        /// <summary>
        /// 初始化 WGC 截图服务
        /// </summary>
        /// <param name="logService">日志服务</param>
        public WgcCaptureService(LogService logService)
        {
            _logService = logService;
            _logService.AddLog("WGC 截图服务已创建");
        }

        /// <summary>
        /// 初始化捕获会话
        /// </summary>
        /// <param name="hWnd">目标窗口句柄</param>
        public async Task<bool> InitializeCaptureAsync(IntPtr hWnd)
        {
            try
            {
                _logService.AddLog($"开始初始化 WGC 捕获会话 - 窗口句柄: 0x{hWnd:X}");

                // 创建 D3D11 设备
                _d3dDevice = new SharpDX.Direct3D11.Device(
                    SharpDX.Direct3D.DriverType.Hardware,
                    DeviceCreationFlags.BgraSupport);
                _logService.AddLog("D3D11 设备已创建");

                // 创建 IDirect3DDevice
                _device = CreateDirect3DDeviceFromSharpDXDevice(_d3dDevice);
                if (_device == null)
                {
                    _logService.AddLog($"无法创建 IDirect3DDevice: {1}", LogLevel.Error);
                    return false;
                }
                _logService.AddLog("IDirect3DDevice 已创建");

                // 从窗口句柄创建捕获项
                _captureItem = CaptureHelper.CreateItemForWindow(hWnd);
                if (_captureItem == null)
                {
                    _logService.AddLog($"无法创建捕获项: {1}", LogLevel.Error);
                    return false;
                }

                _logService.AddLog($"捕获项已创建 - 尺寸: {_captureItem.Size.Width}x{_captureItem.Size.Height}");

                // 创建帧池
                _framePool = Direct3D11CaptureFramePool.Create(
                    _device,
                    DirectXPixelFormat.B8G8R8A8UIntNormalized,
                    2,
                    _captureItem.Size);

                _logService.AddLog("帧池已创建");

                // 订阅帧到达事件
                _framePool.FrameArrived += OnFrameArrived;

                // 创建捕获会话
                _session = _framePool.CreateCaptureSession(_captureItem);
                _logService.AddLog("捕获会话已创建");

                // 启动捕获
                _session.StartCapture();
                _isCapturing = true;
                _logService.AddLog("WGC 捕获会话已启动");

                // 等待一小段时间让第一帧到达
                await Task.Delay(100);

                return true;
            }
            catch (Exception ex)
            {
                _logService.AddLog($"初始化 WGC 捕获失败: {ex.Message}", LogLevel.Error);
                _logService.AddLog($"异常详情: {ex.StackTrace}", LogLevel.Error);
                return false;
            }
        }

        /// <summary>
        /// 从 SharpDX Device 创建 IDirect3DDevice
        /// </summary>
        private IDirect3DDevice? CreateDirect3DDeviceFromSharpDXDevice(SharpDX.Direct3D11.Device device)
        {
            try
            {
                var dxgiDevice = device.QueryInterface<SharpDX.DXGI.Device>();
                var inspectable = CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice.NativePointer);
                dxgiDevice.Dispose();

                return MarshalInterface<IDirect3DDevice>.FromAbi(inspectable);
            }
            catch (Exception ex)
            {
                _logService.AddLog($"创建 IDirect3DDevice 失败: {ex.Message}", LogLevel.Error);
                return null;
            }
        }

        [DllImport("d3d11.dll", EntryPoint = "CreateDirect3D11DeviceFromDXGIDevice", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
        private static extern uint CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);

        private static IntPtr CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice)
        {
            CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice, out IntPtr device);
            return device;
        }

        /// <summary>
        /// 帧到达事件处理
        /// </summary>
        private void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
        {
            try
            {
                using var frame = sender.TryGetNextFrame();
                if (frame == null)
                {
                    return;
                }

                // 处理帧数据
                ProcessFrame(frame);
            }
            catch (Exception ex)
            {
                _logService.AddLog($"处理帧时出错: {ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>
        /// 处理捕获的帧
        /// </summary>
        private void ProcessFrame(Direct3D11CaptureFrame frame)
        {
            try
            {
                if (_d3dDevice == null)
                {
                    return;
                }

                // 获取 Surface
                var surface = frame.Surface;
                var surfaceInterop = surface.As<IDirect3DDxgiInterfaceAccess>();
                var pResource = surfaceInterop.GetInterface(typeof(SharpDX.DXGI.Resource).GUID);
                using var resource = new SharpDX.DXGI.Resource(pResource);
                using var texture2D = resource.QueryInterface<Texture2D>();

                // 获取纹理描述
                var desc = texture2D.Description;
                desc.Usage = ResourceUsage.Staging;
                desc.BindFlags = BindFlags.None;
                desc.CpuAccessFlags = CpuAccessFlags.Read;
                desc.OptionFlags = ResourceOptionFlags.None;

                // 创建临时纹理
                using var stagingTexture = new Texture2D(_d3dDevice, desc);

                // 复制纹理数据
                _d3dDevice.ImmediateContext.CopyResource(texture2D, stagingTexture);

                // 映射纹理数据
                var dataBox = _d3dDevice.ImmediateContext.MapSubresource(
                    stagingTexture,
                    0,
                    MapMode.Read,
                    SharpDX.Direct3D11.MapFlags.None);

                try
                {
                    // 转换为 OpenCV Mat
                    lock (_frameLock)
                    {
                        _lastCapturedFrame?.Dispose();
                        _lastCapturedFrame = new Mat(desc.Height, desc.Width, MatType.CV_8UC4);

                        // 复制像素数据
                        unsafe
                        {
                            byte* srcPtr = (byte*)dataBox.DataPointer;
                            byte* dstPtr = (byte*)_lastCapturedFrame.Data;

                            for (int y = 0; y < desc.Height; y++)
                            {
                                System.Buffer.MemoryCopy(
                                    srcPtr + y * dataBox.RowPitch,
                                    dstPtr + y * desc.Width * 4,
                                    desc.Width * 4,
                                    desc.Width * 4);
                            }
                        }

                        // 转换 BGRA 到 BGR
                        Cv2.CvtColor(_lastCapturedFrame, _lastCapturedFrame, ColorConversionCodes.BGRA2BGR);
                    }
                }
                finally
                {
                    _d3dDevice.ImmediateContext.UnmapSubresource(stagingTexture, 0);
                }
            }
            catch (Exception ex)
            {
                _logService.AddLog($"处理帧数据失败: {ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>
        /// 获取最新捕获的帧
        /// </summary>
        /// <returns>最新的帧图像，如果没有则返回 null</returns>
        public Mat? GetLatestFrame()
        {
            lock (_frameLock)
            {
                return _lastCapturedFrame?.Clone();
            }
        }

        /// <summary>
        /// 停止捕获
        /// </summary>
        public void StopCapture()
        {
            if (!_isCapturing)
            {
                return;
            }

            _logService.AddLog("停止 WGC 捕获会话");

            try
            {
                _session?.Dispose();
                _session = null;

                if (_framePool != null)
                {
                    _framePool.FrameArrived -= OnFrameArrived;
                    _framePool.Dispose();
                    _framePool = null;
                }

                _captureItem = null;
                _isCapturing = false;

                _logService.AddLog("WGC 捕获会话已停止");
            }
            catch (Exception ex)
            {
                _logService.AddLog($"停止捕获时出错: {ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            _logService.AddLog("释放 WGC 截图服务资源");

            StopCapture();

            lock (_frameLock)
            {
                _lastCapturedFrame?.Dispose();
                _lastCapturedFrame = null;
            }

            _device = null;
            _d3dDevice?.Dispose();
            _d3dDevice = null;

            GC.SuppressFinalize(this);
            _logService.AddLog("WGC 截图服务资源已释放");
        }
    }

    /// <summary>
    /// WGC 捕获辅助类
    /// </summary>
    internal static class CaptureHelper
    {
        [ComImport]
        [Guid("3E68D4BD-7135-4D10-8018-9FB6D9F33FA1")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        [ComVisible(true)]
        interface IInitializeWithWindow
        {
            void Initialize(IntPtr hwnd);
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);

        private const uint GA_ROOT = 2;

        /// <summary>
        /// 从窗口句柄创建捕获项
        /// </summary>
        public static GraphicsCaptureItem? CreateItemForWindow(IntPtr hWnd)
        {
            try
            {
                // 获取根窗口
                var rootWindow = GetAncestor(hWnd, GA_ROOT);
                if (rootWindow == IntPtr.Zero)
                {
                    rootWindow = hWnd;
                }

                // 创建捕获项
                var interop = GraphicsCaptureItem.As<IGraphicsCaptureItemInterop>();
                var itemPointer = interop.CreateForWindow(rootWindow);
                var item = GraphicsCaptureItem.FromAbi(itemPointer);
                Marshal.Release(itemPointer);

                return item;
            }
            catch
            {
                return null;
            }
        }
    }

    [ComImport]
    [Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [ComVisible(true)]
    interface IGraphicsCaptureItemInterop
    {
        IntPtr CreateForWindow([In] IntPtr window);
        IntPtr CreateForMonitor([In] IntPtr monitor);
    }

    [ComImport]
    [Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [ComVisible(true)]
    interface IDirect3DDxgiInterfaceAccess
    {
        IntPtr GetInterface([In] Guid iid);
    }
}
