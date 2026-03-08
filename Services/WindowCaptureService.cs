using CommunityToolkit.Mvvm.ComponentModel;
using OpenCvSharp;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Sai2Capture.Services
{
    /// <summary>
    /// 窗口捕获服务
    /// 封装 Windows API 和图像处理逻辑
    /// 主要功能：
    /// 1. 枚举可见窗口及其标题
    /// 2. 定位特定窗口并捕获其内容（使用 WGC API）
    /// 3. 比较和保存修改的帧
    /// </summary>
    public partial class WindowCaptureService : ObservableObject, IDisposable
    {
        // Windows API P/Invoke 声明
        [DllImport("user32.dll")]
        private static extern nint FindWindow(string? lpClassName, string lpWindowName);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc enumProc, nint lParam);
        private delegate bool EnumWindowsProc(nint hWnd, nint lParam);

        [DllImport("user32.dll")]
        private static extern int GetWindowText(nint hWnd, System.Text.StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(nint hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(nint hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool PrintWindow(nint hWnd, nint hdcBlt, int nFlags);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

        [DllImport("kernel32.dll")]
        private static extern nint OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(nint hObject);

        [DllImport("kernel32.dll")]
        private static extern int QueryFullProcessImageName(nint hProcess, int dwFlags, System.Text.StringBuilder lpExeName, ref int lpdwSize);

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left; public int Top; public int Right; public int Bottom;
        }

        private const uint PROCESS_QUERY_INFORMATION = 0x0400;
        private const uint PROCESS_VM_READ = 0x0010;

        private readonly SharedStateService _sharedState;
        private readonly LogService _logService;
        private nint? _selfWindowHandle;

        public WindowCaptureService(SharedStateService sharedState, LogService logService)
        {
            _sharedState = sharedState;
            _logService = logService;
            _logService.AddLog("窗口捕获服务已初始化");
        }

        public void SetSelfWindowHandle(nint windowHandle)
        {
            _selfWindowHandle = windowHandle;
            _logService.AddLog($"已设置程序自身窗口句柄：0x{windowHandle:X}");
        }

        private string? GetProcessPath(nint hWnd)
        {
            try
            {
                if (GetWindowThreadProcessId(hWnd, out uint processId) == 0)
                    return null;

                nint hProcess = OpenProcess(PROCESS_QUERY_INFORMATION | PROCESS_VM_READ, false, processId);
                if (hProcess == IntPtr.Zero)
                    return null;

                try
                {
                    System.Text.StringBuilder pathBuilder = new(1024);
                    int size = pathBuilder.Capacity;
                    return QueryFullProcessImageName(hProcess, 0, pathBuilder, ref size) != 0 ? pathBuilder.ToString() : null;
                }
                finally
                {
                    CloseHandle(hProcess);
                }
            }
            catch
            {
                return null;
            }
        }

        private bool IsSai2RelatedWindow(nint hWnd)
        {
            try
            {
                string? processPath = GetProcessPath(hWnd);
                if (string.IsNullOrEmpty(processPath))
                    return false;

                string exeName = System.IO.Path.GetFileNameWithoutExtension(processPath);
                return exeName.Contains("sai", StringComparison.OrdinalIgnoreCase) ||
                       processPath.Contains("PaintToolSAI", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private bool IsOwnWindow(nint hWnd) => _selfWindowHandle.HasValue && hWnd == _selfWindowHandle.Value;

        /// <summary>
        /// 枚举 SAI2 相关的可见窗口标题
        /// </summary>
        public List<string> EnumSai2WindowTitles()
        {
            _logService.AddLog("扫描系统进程，查找 SAI2 相关窗口");
            List<string> sai2WindowTitles = new();
            List<string> processInfo = new();

            EnumWindows((hWnd, _) =>
            {
                if (IsWindowVisible(hWnd) && !IsOwnWindow(hWnd))
                {
                    System.Text.StringBuilder sb = new(256);
                    GetWindowText(hWnd, sb, 256);
                    string title = sb.ToString();

                    if (!string.IsNullOrEmpty(title) && IsSai2RelatedWindow(hWnd))
                    {
                        sai2WindowTitles.Add(title);
                        string? processPath = GetProcessPath(hWnd);
                        string exeName = System.IO.Path.GetFileName(processPath ?? "未知");
                        processInfo.Add($"窗口：'{title}' - 进程：{exeName}");
                    }
                }
                return true;
            }, IntPtr.Zero);

            if (processInfo.Count > 0)
            {
                _logService.AddLog($"找到 {processInfo.Count} 个 SAI2 相关窗口:");
                foreach (string info in processInfo)
                {
                    _logService.AddLog($"  {info}");
                }
            }
            else
            {
                _logService.AddLog("未找到 SAI2 相关窗口，请确保 SAI2 程序正在运行", LogLevel.Warning);
            }

            _logService.AddLog($"SAI2 窗口枚举完成，找到{sai2WindowTitles.Count} 个 SAI2 相关窗口");
            return sai2WindowTitles;
        }

        /// <summary>
        /// 枚举所有可见窗口标题
        /// </summary>
        public List<string> EnumWindowTitles()
        {
            _logService.AddLog("开始枚举窗口标题");
            List<string> windowTitles = new();

            EnumWindows((hWnd, _) =>
            {
                if (IsWindowVisible(hWnd) && !IsOwnWindow(hWnd))
                {
                    System.Text.StringBuilder sb = new(256);
                    GetWindowText(hWnd, sb, 256);
                    string title = sb.ToString();
                    if (!string.IsNullOrEmpty(title))
                    {
                        windowTitles.Add(title);
                    }
                }
                return true;
            }, IntPtr.Zero);

            _logService.AddLog($"枚举完成，找到{windowTitles.Count} 个可见窗口");
            return windowTitles;
        }

        /// <summary>
        /// 根据窗口标题查找窗口句柄
        /// </summary>
        public nint FindWindowByTitle(string windowTitle, bool silent = false)
        {
            if (!silent)
            {
                _logService.AddLog($"查找窗口：'{windowTitle}'");
            }

            nint hWnd = FindWindow(null, windowTitle);
            if (hWnd == IntPtr.Zero && !silent)
            {
                _logService.AddLog($"未找到窗口：'{windowTitle}'", LogLevel.Error);
                throw new Exception($"Window with title '{windowTitle}' not found");
            }

            if (!silent)
            {
                _logService.AddLog($"找到窗口句柄：0x{hWnd:X}");
            }
            return hWnd;
        }

        /// <summary>
        /// 初始化捕获会话
        /// </summary>
        public Task<bool> InitializeCaptureAsync(nint hWnd)
        {
            _logService.AddLog("使用传统 PrintWindow API，初始化成功");
            return Task.FromResult(true);
        }

        /// <summary>
        /// 捕获窗口内容
        /// </summary>
        public Mat CaptureWindowContent(nint hWnd)
        {
            if (!GetWindowRect(hWnd, out RECT windowRect))
            {
                _logService.AddLog($"获取窗口矩形失败：{Marshal.GetLastWin32Error()}", LogLevel.Error);
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            int width = windowRect.Right - windowRect.Left;
            int height = windowRect.Bottom - windowRect.Top;

            if (width <= 0 || height <= 0)
            {
                _logService.AddLog($"无效的窗口尺寸：{width}x{height}", LogLevel.Error);
                throw new Exception($"无效的窗口尺寸：{width}x{height}");
            }

            System.Drawing.Bitmap? bitmap = null;
            Graphics? graphics = null;
            IntPtr hdc = IntPtr.Zero;

            try
            {
                bitmap = new(width, height);
                graphics = Graphics.FromImage(bitmap);
                hdc = graphics.GetHdc();

                if (!PrintWindow(hWnd, hdc, 0))
                {
                    int errorCode = Marshal.GetLastWin32Error();
                    _logService.AddLog($"PrintWindow 调用失败：{errorCode}", LogLevel.Error);
                    throw new Win32Exception(errorCode);
                }

                graphics.ReleaseHdc(hdc);
                hdc = IntPtr.Zero;

                var rect = new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height);
                var bitmapData = bitmap.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadOnly, bitmap.PixelFormat);

                try
                {
                    var tempMat = new Mat(bitmap.Height, bitmap.Width, MatType.CV_8UC4, bitmapData.Scan0);
                    var resultMat = new Mat();
                    Cv2.CvtColor(tempMat, resultMat, ColorConversionCodes.BGRA2BGR);
                    return resultMat;
                }
                finally
                {
                    bitmap.UnlockBits(bitmapData);
                }
            }
            finally
            {
                if (hdc != IntPtr.Zero && graphics != null)
                {
                    try { graphics.ReleaseHdc(hdc); } catch { }
                }
                graphics?.Dispose();
                bitmap?.Dispose();
            }
        }

        /// <summary>
        /// 检查并保存有变化的帧
        /// </summary>
        public void SaveIfModified(Mat currentImage)
        {
            if (_sharedState.LastImage == null || !ImagesEqual(_sharedState.LastImage, currentImage))
            {
                SaveFrame(currentImage);
            }
        }

        private bool ImagesEqual(Mat? img1, Mat img2)
        {
            if (img1 == null) return false;

            if (img1.IsDisposed || img2.IsDisposed)
            {
                _logService.AddLog("图像对象已被释放，跳过比较", LogLevel.Warning);
                return false;
            }

            if (img1.Size() != img2.Size() || img1.Channels() != img2.Channels())
                return false;

            using Mat diff = new();
            Cv2.Absdiff(img1, img2, diff);

            if (diff.Channels() > 1)
            {
                using Mat gray = new();
                Cv2.CvtColor(diff, gray, ColorConversionCodes.BGR2GRAY);
                return Cv2.CountNonZero(gray) == 0;
            }

            return Cv2.CountNonZero(diff) == 0;
        }

        private void SaveFrame(Mat frame)
        {
            if (_sharedState.LastImage != null && !_sharedState.LastImage.IsDisposed)
            {
                _sharedState.LastImage.Dispose();
            }

            _sharedState.LastImage = frame.Clone();
            _sharedState.SavedCount++;
        }

        public void Dispose()
        {
            _logService.AddLog("释放窗口捕获服务资源");
            GC.SuppressFinalize(this);
        }
    }
}
