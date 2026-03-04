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
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        private const uint PROCESS_QUERY_INFORMATION = 0x0400;
        private const uint PROCESS_VM_READ = 0x0010;

        private readonly SharedStateService _sharedState;
        private readonly LogService _logService;
        private nint? _selfWindowHandle;

        /// <summary>
        /// 初始化窗口捕获服务
        /// </summary>
        /// <param name="sharedState">共享状态服务</param>
        /// <param name="logService">日志服务</param>
        public WindowCaptureService(SharedStateService sharedState, LogService logService)
        {
            _sharedState = sharedState;
            _logService = logService;
            _logService.AddLog("窗口捕获服务已初始化");
        }

        /// <summary>
        /// 设置当前程序的主窗口句柄，用于在窗口扫描时排除自己
        /// </summary>
        /// <param name="windowHandle">程序主窗口句柄</param>
        public void SetSelfWindowHandle(nint windowHandle)
        {
            _selfWindowHandle = windowHandle;
            _logService.AddLog($"已设置程序自身窗口句柄：0x{windowHandle:X}");
        }

        /// <summary>
        /// 获取进程可执行文件路径
        /// </summary>
        /// <param name="hWnd">窗口句柄</param>
        /// <returns>可执行文件路径，获取失败时返回 null</returns>
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
                    System.Text.StringBuilder pathBuilder = new System.Text.StringBuilder(1024);
                    int size = pathBuilder.Capacity;

                    if (QueryFullProcessImageName(hProcess, 0, pathBuilder, ref size) != 0)
                    {
                        return pathBuilder.ToString();
                    }
                    return null;
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

        /// <summary>
        /// 检查窗口是否与 SAI2 相关
        /// 通过检查进程文件名来判断
        /// </summary>
        /// <param name="hWnd">窗口句柄</param>
        /// <returns>如果是 SAI2 相关窗口返回 true</returns>
        private bool IsSai2RelatedWindow(nint hWnd)
        {
            try
            {
                string? processPath = GetProcessPath(hWnd);
                if (string.IsNullOrEmpty(processPath))
                    return false;

                string exeName = System.IO.Path.GetFileNameWithoutExtension(processPath);

                // 检查是否为 SAI2 相关进程
                return exeName.Equals("sai", StringComparison.OrdinalIgnoreCase) ||
                       exeName.Equals("sai2", StringComparison.OrdinalIgnoreCase) ||
                       exeName.Contains("sai", StringComparison.OrdinalIgnoreCase) ||
                       processPath.Contains("PaintToolSAI", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 枚举 SAI2 相关的可见窗口标题
        /// 使用 Windows API EnumWindows 遍历窗口
        /// 仅返回与 SAI2 进程相关的可见窗口
        /// 排除当前程序自己的窗口
        /// </summary>
        /// <returns>SAI2 相关窗口标题列表</returns>
        public List<string> EnumSai2WindowTitles()
        {
            _logService.AddLog("扫描系统进程，查找 SAI2 相关窗口");
            List<string> sai2WindowTitles = new List<string>();
            List<string> allProcessInfo = new List<string>();

            EnumWindows((hWnd, _) =>
            {
                if (IsWindowVisible(hWnd))
                {
                    // 排除当前程序自己的窗口
                    if (_selfWindowHandle.HasValue && hWnd == _selfWindowHandle.Value)
                    {
                        return true; // 跳过自己的窗口
                    }

                    System.Text.StringBuilder sb = new System.Text.StringBuilder(256);
                    GetWindowText(hWnd, sb, 256);
                    string title = sb.ToString();

                    if (!string.IsNullOrEmpty(title) && IsSai2RelatedWindow(hWnd))
                    {
                        sai2WindowTitles.Add(title);

                        // 记录详细进程信息用于调试
                        string? processPath = GetProcessPath(hWnd);
                        string exeName = System.IO.Path.GetFileName(processPath ?? "未知");
                        allProcessInfo.Add($"窗口：'{title}' - 进程：{exeName}");
                    }
                }
                return true;
            }, IntPtr.Zero);

            // 记录所有找到的 SAI2 相关窗口信息
            if (allProcessInfo.Count > 0)
            {
                _logService.AddLog($"找到 {allProcessInfo.Count} 个 SAI2 相关窗口:");
                foreach (string info in allProcessInfo)
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
        /// 枚举当前所有可见窗口的标题
        /// 使用 Windows API EnumWindows 遍历窗口
        /// 仅返回具有非空标题的可见窗口
        /// 排除当前程序自己的窗口
        /// </summary>
        /// <returns>可见窗口标题列表</returns>
        public List<string> EnumWindowTitles()
        {
            _logService.AddLog("开始枚举窗口标题");
            List<string> windowTitles = new List<string>();
            EnumWindows((hWnd, _) =>
            {
                if (IsWindowVisible(hWnd))
                {
                    // 排除当前程序自己的窗口
                    if (_selfWindowHandle.HasValue && hWnd == _selfWindowHandle.Value)
                    {
                        return true; // 跳过自己的窗口
                    }

                    System.Text.StringBuilder sb = new System.Text.StringBuilder(256);
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
        /// 使用 Windows API FindWindow 进行精确匹配
        /// </summary>
        /// <param name="windowTitle">目标窗口标题</param>
        /// <param name="silent">静默模式：true 时不记录日志，不抛异常，未找到时返回 IntPtr.Zero</param>
        /// <returns>窗口句柄，未找到时返回 IntPtr.Zero（静默模式）或抛异常（非静默模式）</returns>
        public nint FindWindowByTitle(string windowTitle, bool silent = false)
        {
            if (!silent)
            {
                _logService.AddLog($"查找窗口：'{windowTitle}'");
            }
            nint hWnd = FindWindow(null, windowTitle);
            if (hWnd == IntPtr.Zero)
            {
                if (!silent)
                {
                    _logService.AddLog($"未找到窗口：'{windowTitle}'", LogLevel.Error);
                    throw new Exception($"Window with title '{windowTitle}' not found");
                }
                return IntPtr.Zero;
            }
            if (!silent)
            {
                _logService.AddLog($"找到窗口句柄：0x{hWnd:X}");
            }
            return hWnd;
        }

        /// <summary>
        /// 初始化捕获会话（占位符，默认使用 PrintWindow API）
        /// </summary>
        /// <param name="hWnd">目标窗口句柄</param>
        public System.Threading.Tasks.Task<bool> InitializeCaptureAsync(nint hWnd)
        {
            _logService.AddLog("使用传统 PrintWindow API，初始化成功");
            return System.Threading.Tasks.Task.FromResult(true);
        }

        /// <summary>
        /// 捕获指定窗口的内容并转换为 OpenCV Mat 对象
        /// 使用 PrintWindow API
        /// </summary>
        /// <param name="hWnd">目标窗口句柄</param>
        /// <returns>窗口内容的 Mat 图像</returns>
        /// <exception cref="Win32Exception">Windows API 调用失败时抛出</exception>
        public Mat CaptureWindowContent(nint hWnd)
        {
            return CaptureWindowContentLegacy(hWnd);
        }

        /// <summary>
        /// 使用 PrintWindow API 捕获窗口内容
        /// </summary>
        public Mat CaptureWindowContentLegacy(nint hWnd)
        {
            if (!GetWindowRect(hWnd, out RECT windowRect))
            {
                _logService.AddLog($"获取窗口矩形失败：{Marshal.GetLastWin32Error()}", LogLevel.Error);
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            int width = windowRect.Right - windowRect.Left;
            int height = windowRect.Bottom - windowRect.Top;

            using var bitmap = new System.Drawing.Bitmap(width, height);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                IntPtr hdc = graphics.GetHdc();
                try
                {
                    if (!PrintWindow(hWnd, hdc, 0))
                    {
                        _logService.AddLog($"PrintWindow 调用失败：{Marshal.GetLastWin32Error()}", LogLevel.Error);
                        throw new Win32Exception(Marshal.GetLastWin32Error());
                    }
                }
                finally
                {
                    graphics.ReleaseHdc(hdc);
                }
            }

            // 手动将 Bitmap 转换为 Mat
            var rect = new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height);
            var bitmapData = bitmap.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadOnly, bitmap.PixelFormat);

            try
            {
                var mat = new Mat(bitmap.Height, bitmap.Width, MatType.CV_8UC4, bitmapData.Scan0);
                // 转换 BGR 到 RGB
                Cv2.CvtColor(mat, mat, ColorConversionCodes.BGRA2BGR);
                return mat.Clone();
            }
            finally
            {
                bitmap.UnlockBits(bitmapData);
            }
        }

        /// <summary>
        /// 检查并保存有变化的帧
        /// </summary>
        public void SaveIfModified(Mat currentImage)
        {
            bool shouldSave = _sharedState.VideoWriter == null
                || _sharedState.LastImage == null
                || !ImagesEqual(_sharedState.LastImage, currentImage);

            if (shouldSave)
                SaveFrame(currentImage);
        }

        /// <summary>
        /// 比较两幅图像是否相同
        /// </summary>
        private bool ImagesEqual(Mat? img1, Mat img2)
        {
            if (img1 == null) return false;
            if (img1.Size() != img2.Size()) return false;
            if (img1.Channels() != img2.Channels()) return false;

            using Mat diff = new Mat();
            Cv2.Absdiff(img1, img2, diff);

            // 对于多通道图像，需要先转换为灰度图再计数非零像素
            if (diff.Channels() > 1)
            {
                using Mat gray = new Mat();
                Cv2.CvtColor(diff, gray, ColorConversionCodes.BGR2GRAY);
                return Cv2.CountNonZero(gray) == 0;
            }
            else
            {
                return Cv2.CountNonZero(diff) == 0;
            }
        }

        /// <summary>
        /// 保存帧到视频文件并更新状态
        /// 1. 写入视频帧
        /// 2. 缓存最后一帧作为比较基准
        /// 3. 增加已保存帧计数
        /// </summary>
        /// <param name="frame">要保存的帧</param>
        private void SaveFrame(Mat frame)
        {
            if (_sharedState.VideoWriter != null && _sharedState.VideoWriter.IsOpened())
            {
                _sharedState.VideoWriter.Write(frame);
                
                // 释放旧的 LastImage，防止内存泄漏
                _sharedState.LastImage?.Dispose();
                
                // 克隆当前帧作为下一帧的比较基准
                _sharedState.LastImage = frame.Clone();
                _sharedState.SavedCount++;
            }
            else
            {
                _logService.AddLog("警告：VideoWriter 未打开，无法保存帧", LogLevel.Warning);
            }
        }


        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            _logService.AddLog("释放窗口捕获服务资源");
            GC.SuppressFinalize(this);
        }
    }
}
