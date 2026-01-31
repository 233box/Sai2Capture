using CommunityToolkit.Mvvm.ComponentModel;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Sai2Capture.Services
{
    /// <summary>
    /// 窗口捕获服务
    /// 封装Windows API和图像处理逻辑，
    /// 主要功能：
    /// 1. 枚举可见窗口及其标题
    /// 2. 定位特定窗口并捕获其内容（使用 WGC API）
    /// 3. 比较和保存修改的帧
    /// </summary>
    public partial class WindowCaptureService : ObservableObject, IDisposable
    {
    // Windows API P/Invoke声明
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

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        private readonly SharedStateService _sharedState;
        private readonly LogService _logService;
        private WgcCaptureService? _wgcCapture;
        private bool _useWgcApi = false; // 默认使用 WGC API

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
        /// 枚举当前所有可见窗口的标题
        /// 使用Windows API EnumWindows遍历窗口
        /// 仅返回具有非空标题的可见窗口
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

            _sharedState.WindowTitles = windowTitles;
            _logService.AddLog($"枚举完成，找到 {windowTitles.Count} 个可见窗口");
            return windowTitles;
        }

        /// <summary>
        /// 根据窗口标题查找窗口句柄
        /// 使用Windows API FindWindow进行精确匹配
        /// </summary>
        /// <param name="windowTitle">目标窗口标题</param>
        /// <returns>窗口句柄</returns>
        /// <exception cref="Exception">窗口未找到时抛出异常</exception>
        public nint FindWindowByTitle(string windowTitle)
        {
            _logService.AddLog($"查找窗口: '{windowTitle}'");
            nint hWnd = FindWindow(null, windowTitle);
            if (hWnd == IntPtr.Zero)
            {
                _logService.AddLog($"未找到窗口: '{windowTitle}'", LogLevel.Error);
                throw new Exception($"Window with title '{windowTitle}' not found");
            }
            _logService.AddLog($"找到窗口句柄: 0x{hWnd:X}");
            return hWnd;
        }

        /// <summary>
        /// 初始化 WGC 捕获会话
        /// </summary>
        /// <param name="hWnd">目标窗口句柄</param>
        public async System.Threading.Tasks.Task<bool> InitializeWgcCaptureAsync(nint hWnd)
        {
            if (_useWgcApi == false)
            {
                _logService.AddLog("WGC API 未启用，跳过初始化");
                return false;
            }
            try
            {
                _logService.AddLog("尝试初始化 WGC 捕获会话");
                
                // 释放旧的捕获会话
                if (_wgcCapture != null)
                {
                    _logService.AddLog("释放现有的 WGC 捕获会话");
                    _wgcCapture.Dispose();
                    _wgcCapture = null;
                }

                // 创建新的 WGC 捕获服务
                _wgcCapture = new WgcCaptureService(_logService);
                bool success = await _wgcCapture.InitializeCaptureAsync(hWnd);

                if (success)
                {
                    _useWgcApi = true;
                    _logService.AddLog("WGC 捕获会话初始化成功");
                }
                else
                {
                    _logService.AddLog("WGC 捕获会话初始化失败，将回退到 PrintWindow API", LogLevel.Warning);
                    _useWgcApi = false;
                    _wgcCapture?.Dispose();
                    _wgcCapture = null;
                }

                return success;
            }
            catch (Exception ex)
            {
                _logService.AddLog($"初始化 WGC 捕获时发生异常: {ex.Message}", LogLevel.Error);
                _useWgcApi = false;
                _wgcCapture?.Dispose();
                _wgcCapture = null;
                return false;
            }
        }

        /// <summary>
        /// 捕获指定窗口的内容并转换为OpenCV Mat对象
        /// 优先使用 WGC API，失败时回退到 PrintWindow API
        /// </summary>
        /// <param name="hWnd">目标窗口句柄</param>
        /// <returns>窗口内容的Mat图像</returns>
        /// <exception cref="Win32Exception">Windows API调用失败时抛出</exception>
        public Mat CaptureWindowContent(nint hWnd)
        {
            // 尝试使用 WGC API
            if (_useWgcApi && _wgcCapture != null)
            {
                // 先尝试获取最新帧
                var frame = _wgcCapture.GetLatestFrame();
                if (frame != null)
                {
                    return frame;
                }
                
                // 如果没有帧，尝试手动捕获
                _logService.AddLog("尝试手动捕获 WGC 帧...");
                frame = _wgcCapture.CaptureFrame();
                if (frame != null)
                {
                    return frame;
                }
                
                _logService.AddLog("WGC API 未返回帧，回退到 PrintWindow API", LogLevel.Warning);
            }

            // 回退到传统的 PrintWindow API
            return CaptureWindowContentLegacy(hWnd);
        }

        /// <summary>
        /// 使用传统 PrintWindow API 捕获窗口内容
        /// </summary>
        private Mat CaptureWindowContentLegacy(nint hWnd)
        {
            if (!GetWindowRect(hWnd, out RECT windowRect))
            {
                _logService.AddLog($"获取窗口矩形失败: {Marshal.GetLastWin32Error()}", LogLevel.Error);
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            int width = windowRect.Right - windowRect.Left;
            int height = windowRect.Bottom - windowRect.Top;

            _logService.AddLog($"使用 PrintWindow API 捕获窗口 - 尺寸: {width}x{height}");

            using var bitmap = new System.Drawing.Bitmap(width, height);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                IntPtr hdc = graphics.GetHdc();
                try
                {
                    if (!PrintWindow(hWnd, hdc, 0))
                    {
                        _logService.AddLog($"PrintWindow 调用失败: {Marshal.GetLastWin32Error()}", LogLevel.Error);
                        throw new Win32Exception(Marshal.GetLastWin32Error());
                    }
                }
                finally
                {
                    graphics.ReleaseHdc(hdc);
                }
            }

            // 手动将Bitmap转换为Mat
            var rect = new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height);
            var bitmapData = bitmap.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadOnly, bitmap.PixelFormat);
            
            try
            {
                var mat = new Mat(bitmap.Height, bitmap.Width, MatType.CV_8UC4, bitmapData.Scan0);
                // 转换BGR到RGB
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
        /// 在以下情况下保存帧：
        /// 1. 视频写入器未初始化
        /// 2. 首次启动捕获（仅第一帧）
        /// 3. 当前帧与上一帧有差异
        /// </summary>
        /// <param name="currentImage">当前捕获的帧</param>
        public void SaveIfModified(Mat currentImage)
        {
            bool shouldSave = false;
            string reason = "";

            if (_sharedState.VideoWriter == null)
            {
                shouldSave = true;
                reason = "视频写入器未初始化";
            }
            else if (_sharedState.IsInitialized)
            {
                // IsInitialized=true 表示刚完成初始化，这是第一帧
                shouldSave = true;
                reason = "首次启动捕获";
                // 保存第一帧后立即重置标志，后续帧将通过帧差异检测来决定是否保存
                _sharedState.IsInitialized = false;
                _logService.AddLog("第一帧已保存，IsInitialized 标志已重置为 false");
            }
            else if (!ImagesEqual(_sharedState.LastImage, currentImage))
            {
                shouldSave = true;
                reason = "帧内容发生变化";
            }

            if (shouldSave)
            {
                _logService.AddLog($"保存帧 #{_sharedState.SavedCount + 1} - 原因: {reason}");
                SaveFrame(currentImage);
            }
        }

        /// <summary>
        /// 比较两幅图像是否相同
        /// 使用OpenCV绝对差值计算差异像素数
        /// </summary>
        /// <param name="img1">第一幅图像(null视为不同)</param>
        /// <param name="img2">第二幅图像</param>
        /// <returns>当图像尺寸相同且所有像素相同时返回true</returns>
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
        /// 3. 增加已保存帧计数器
        /// </summary>
        /// <param name="frame">要保存的帧</param>
        private void SaveFrame(Mat frame)
        {
            if (_sharedState.VideoWriter != null && _sharedState.VideoWriter.IsOpened())
            {
                _sharedState.VideoWriter.Write(frame);
                _sharedState.LastImage = frame.Clone();
                _sharedState.SavedCount++;
            }
            else
            {
                _logService.AddLog("警告: VideoWriter 未打开，无法保存帧", LogLevel.Warning);
            }
        }

        /// <summary>
        /// 停止 WGC 捕获会话
        /// </summary>
        public void StopWgcCapture()
        {
            if (_wgcCapture != null)
            {
                _logService.AddLog("停止 WGC 捕获会话");
                _wgcCapture.StopCapture();
            }
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            _logService.AddLog("释放窗口捕获服务资源");
            
            if (_wgcCapture != null)
            {
                _wgcCapture.Dispose();
                _wgcCapture = null;
            }

            GC.SuppressFinalize(this);
        }
    }
}
