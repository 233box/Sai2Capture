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
    /// 2. 定位特定窗口并捕获其内容
    /// 3. 比较和保存修改的帧
    /// </summary>
    public partial class WindowCaptureService : ObservableObject
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

        /// <summary>
        /// 初始化窗口捕获服务
        /// </summary>
        /// <param name="sharedState">共享状态服务</param>
        public WindowCaptureService(SharedStateService sharedState)
        {
            _sharedState = sharedState;
        }

        /// <summary>
        /// 枚举当前所有可见窗口的标题
        /// 使用Windows API EnumWindows遍历窗口
        /// 仅返回具有非空标题的可见窗口
        /// </summary>
        /// <returns>可见窗口标题列表</returns>
        public List<string> EnumWindowTitles()
        {
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
            nint hWnd = FindWindow(null, windowTitle);
            if (hWnd == IntPtr.Zero)
            {
                throw new Exception($"Window with title '{windowTitle}' not found");
            }
            return hWnd;
        }

        /// <summary>
        /// 捕获指定窗口的内容并转换为OpenCV Mat对象
        /// 1. 获取窗口尺寸
        /// 2. 使用PrintWindow直接获取窗口内容
        /// 3. 将Bitmap转换为Mat格式
        /// 4. 转换颜色空间(BGRA到BGR)
        /// </summary>
        /// <param name="hWnd">目标窗口句柄</param>
        /// <returns>窗口内容的Mat图像</returns>
        /// <exception cref="Win32Exception">Windows API调用失败时抛出</exception>
        public Mat CaptureWindowContent(nint hWnd)
        {
            if (!GetWindowRect(hWnd, out RECT windowRect))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            int width = windowRect.Right - windowRect.Left;
            int height = windowRect.Bottom - windowRect.Top;

            using var bitmap = new System.Drawing.Bitmap(width, height);
            using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
            {
                IntPtr hdc = graphics.GetHdc();
                try
                {
                    if (!PrintWindow(hWnd, hdc, 0))
                    {
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
        /// 2. 首次启动捕获
        /// 3. 当前帧与上一帧有差异
        /// </summary>
        /// <param name="currentImage">当前捕获的帧</param>
        public void SaveIfModified(Mat currentImage)
        {
            if (_sharedState.VideoWriter == null ||
                _sharedState.FirstStart ||
                !ImagesEqual(_sharedState.LastImage, currentImage))
            {
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

            using Mat diff = new Mat();
            Cv2.Absdiff(img1, img2, diff);
            return Cv2.CountNonZero(diff) == 0;
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
            _sharedState.VideoWriter?.Write(frame);
            _sharedState.LastImage = frame.Clone();
            _sharedState.SavedCount++;
        }
    }
}
