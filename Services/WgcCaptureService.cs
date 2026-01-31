using OpenCvSharp;
using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using WinRT;
using WinRT.Interop;

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
                    _logService.AddLog("无法创建 IDirect3DDevice", LogLevel.Error);
                    return false;
                }
                _logService.AddLog("IDirect3DDevice 已创建");

                // 从窗口句柄创建捕获项
                _captureItem = CaptureHelper.CreateItemForWindow(hWnd, _logService);
                if (_captureItem == null)
                {
                    _logService.AddLog($"无法创建捕获项 - 窗口句柄: 0x{hWnd:X}", LogLevel.Error);
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
                _logService.AddLog("已订阅帧到达事件");

                // 创建捕获会话
                _session = _framePool.CreateCaptureSession(_captureItem);
                _logService.AddLog("捕获会话已创建");
                
                // 设置捕获选项
                _session.IsCursorCaptureEnabled = false; // 不捕获光标
                _logService.AddLog("捕获选项已设置");

                // 启动捕获
                _session.StartCapture();
                _isCapturing = true;
                _logService.AddLog("WGC 捕获会话已启动");

                // 等待足够的时间让第一帧到达
                _logService.AddLog("等待第一帧到达...");
                
                // 尝试多次等待，每次检查是否收到帧
                for (int i = 0; i < 10; i++)
                {
                    await Task.Delay(100);
                    
                    lock (_frameLock)
                    {
                        if (_lastCapturedFrame != null)
                        {
                            _logService.AddLog($"已收到第一帧 (等待 {(i + 1) * 100}ms)");
                            return true;
                        }
                    }
                }
                
                _logService.AddLog("等待超时，未收到帧", LogLevel.Warning);
                _logService.AddLog("提示：某些窗口可能不支持 WGC 捕获，或窗口需要在前台", LogLevel.Warning);

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
                    _logService.AddLog("TryGetNextFrame 返回 null", LogLevel.Warning);
                    return;
                }

                _logService.AddLog($"收到新帧 - 尺寸: {frame.ContentSize.Width}x{frame.ContentSize.Height}");
                
                // 处理帧数据
                ProcessFrame(frame);
            }
            catch (Exception ex)
            {
                _logService.AddLog($"处理帧时出错: {ex.Message}", LogLevel.Error);
                _logService.AddLog($"异常堆栈: {ex.StackTrace}", LogLevel.Error);
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
                    _logService.AddLog("D3D 设备为 null，无法处理帧", LogLevel.Warning);
                    return;
                }

                _logService.AddLog("开始处理帧数据...");

                // 获取 Surface
                var surface = frame.Surface;
                var surfaceInterop = surface.As<IDirect3DDxgiInterfaceAccess>();
                var pResource = surfaceInterop.GetInterface(typeof(SharpDX.DXGI.Resource).GUID);
                using var resource = new SharpDX.DXGI.Resource(pResource);
                using var texture2D = resource.QueryInterface<Texture2D>();

                // 获取纹理描述
                var desc = texture2D.Description;
                _logService.AddLog($"纹理描述 - 宽度: {desc.Width}, 高度: {desc.Height}, 格式: {desc.Format}");
                
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
                        
                        _logService.AddLog($"帧处理完成 - Mat 尺寸: {_lastCapturedFrame.Width}x{_lastCapturedFrame.Height}");
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
                _logService.AddLog($"异常堆栈: {ex.StackTrace}", LogLevel.Error);
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
                if (_lastCapturedFrame == null)
                {
                    _logService.AddLog("GetLatestFrame: 没有可用的帧", LogLevel.Warning);
                }
                return _lastCapturedFrame?.Clone();
            }
        }
        
        /// <summary>
        /// 手动捕获一帧（用于调试）
        /// </summary>
        public Mat? CaptureFrame()
        {
            try
            {
                if (_framePool == null || !_isCapturing)
                {
                    _logService.AddLog("捕获会话未激活", LogLevel.Warning);
                    return null;
                }
                
                _logService.AddLog("尝试手动捕获帧...");
                using var frame = _framePool.TryGetNextFrame();
                
                if (frame == null)
                {
                    _logService.AddLog("TryGetNextFrame 返回 null", LogLevel.Warning);
                    return null;
                }
                
                ProcessFrame(frame);
                
                lock (_frameLock)
                {
                    return _lastCapturedFrame?.Clone();
                }
            }
            catch (Exception ex)
            {
                _logService.AddLog($"手动捕获帧失败: {ex.Message}", LogLevel.Error);
                return null;
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
        public static GraphicsCaptureItem? CreateItemForWindow(IntPtr hWnd, LogService? logService = null)
        {
            try
            {
                logService?.AddLog($"开始创建捕获项 - 窗口句柄: 0x{hWnd:X}");

                // 获取根窗口
                var rootWindow = GetAncestor(hWnd, GA_ROOT);
                if (rootWindow == IntPtr.Zero)
                {
                    logService?.AddLog("无法获取根窗口，使用原始句柄", LogLevel.Warning);
                    rootWindow = hWnd;
                }
                else
                {
                    logService?.AddLog($"根窗口句柄: 0x{rootWindow:X}");
                }

                // 创建捕获项
                logService?.AddLog("正在创建 GraphicsCaptureItem...");
                
                // GraphicsCaptureItem 的 IID
                var itemIID = new Guid("79C3F95B-31F7-4EC2-A464-632EF5D30760");
                
                // 使用 WinRT Interop 获取工厂
                var factoryGuid = new Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356");
                
                var hr = WindowsCreateString("Windows.Graphics.Capture.GraphicsCaptureItem", 
                    (uint)"Windows.Graphics.Capture.GraphicsCaptureItem".Length, 
                    out IntPtr hString);
                
                if (hr != 0)
                {
                    logService?.AddLog($"创建字符串失败: 0x{hr:X}", LogLevel.Error);
                    return null;
                }
                
                hr = RoGetActivationFactory(hString, ref factoryGuid, out IntPtr factoryPtr);
                WindowsDeleteString(hString);
                
                if (hr != 0 || factoryPtr == IntPtr.Zero)
                {
                    logService?.AddLog($"获取激活工厂失败: 0x{hr:X}", LogLevel.Error);
                    return null;
                }
                
                try
                {
                    var interop = Marshal.GetObjectForIUnknown(factoryPtr) as IGraphicsCaptureItemInterop;
                    
                    if (interop == null)
                    {
                        logService?.AddLog("无法获取 IGraphicsCaptureItemInterop 接口", LogLevel.Error);
                        return null;
                    }
                    
                    // 调用 CreateForWindow
                    hr = interop.CreateForWindow(rootWindow, ref itemIID, out object itemObj);
                    
                    if (hr != 0)
                    {
                        logService?.AddLog($"CreateForWindow 失败: HRESULT = 0x{hr:X}", LogLevel.Error);
                        return null;
                    }
                    
                    if (itemObj == null)
                    {
                        logService?.AddLog("CreateForWindow 返回 null", LogLevel.Error);
                        return null;
                    }
                    
                    // 使用 WinRT 投影转换
                    var itemPtr = Marshal.GetIUnknownForObject(itemObj);
                    var item = GraphicsCaptureItem.FromAbi(itemPtr);
                    Marshal.Release(itemPtr);
                    
                    if (itemObj is IDisposable disposable)
                    {
                        disposable.Dispose();
                    }

                    logService?.AddLog("捕获项创建成功");
                    return item;
                }
                finally
                {
                    Marshal.Release(factoryPtr);
                }
            }
            catch (Exception ex)
            {
                logService?.AddLog($"创建捕获项失败: {ex.Message}", LogLevel.Error);
                logService?.AddLog($"异常详情: {ex.StackTrace}", LogLevel.Error);
                return null;
            }
        }
        
        [DllImport("api-ms-win-core-winrt-string-l1-1-0.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern int WindowsCreateString([MarshalAs(UnmanagedType.LPWStr)] string sourceString, 
            uint length, out IntPtr hString);
        
        [DllImport("api-ms-win-core-winrt-string-l1-1-0.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern int WindowsDeleteString(IntPtr hString);
        
        [DllImport("api-ms-win-core-winrt-l1-1-0.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern int RoGetActivationFactory(IntPtr activatableClassId, 
            ref Guid iid, out IntPtr factory);
    }

    [ComImport]
    [Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IGraphicsCaptureItemInterop
    {
        [PreserveSig]
        int CreateForWindow(
            [In] IntPtr window,
            [In] ref Guid iid,
            [Out, MarshalAs(UnmanagedType.IUnknown)] out object result);

        [PreserveSig]
        int CreateForMonitor(
            [In] IntPtr monitor,
            [In] ref Guid iid,
            [Out, MarshalAs(UnmanagedType.IUnknown)] out object result);
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
