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
    public partial class WindowCaptureService : ObservableObject
    {
        // Windows API相关声明
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

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        private readonly SharedStateService _sharedState;

        public WindowCaptureService(SharedStateService sharedState)
        {
            _sharedState = sharedState;
        }

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

        public nint FindWindowByTitle(string windowTitle)
        {
            nint hWnd = FindWindow(null, windowTitle);
            if (hWnd == IntPtr.Zero)
            {
                throw new Exception($"Window with title '{windowTitle}' not found");
            }
            return hWnd;
        }

        public Mat CaptureWindowContent(nint hWnd)
        {
            if (!GetWindowRect(hWnd, out RECT windowRect))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            int width = windowRect.Right - windowRect.Left;
            int height = windowRect.Bottom - windowRect.Top - _sharedState.CutWindow;

            using var bitmap = new System.Drawing.Bitmap(width, height);
            using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
            {
                graphics.CopyFromScreen(
                    windowRect.Left,
                    windowRect.Top,
                    0,
                    0,
                    new System.Drawing.Size(width, height),
                    System.Drawing.CopyPixelOperation.SourceCopy);
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

        public void SaveIfModified(Mat currentImage)
        {
            if (_sharedState.VideoWriter == null ||
                _sharedState.FirstStart ||
                !ImagesEqual(_sharedState.LastImage, currentImage))
            {
                SaveFrame(currentImage);
            }
        }

        private bool ImagesEqual(Mat? img1, Mat img2)
        {
            if (img1 == null) return false;
            if (img1.Size() != img2.Size()) return false;

            using Mat diff = new Mat();
            Cv2.Absdiff(img1, img2, diff);
            return Cv2.CountNonZero(diff) == 0;
        }

        private void SaveFrame(Mat frame)
        {
            _sharedState.VideoWriter?.Write(frame);
            _sharedState.LastImage = frame.Clone();
            _sharedState.SavedCount++;
        }
    }
}
