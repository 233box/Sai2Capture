using CommunityToolkit.Mvvm.ComponentModel;
using OpenCvSharp;
using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Sai2Capture.Services
{
    /// <summary>
    /// 实用工具服务
    /// </summary>
    public partial class UtilityService : ObservableObject
    {
        private readonly SharedStateService _sharedState;
        private readonly WindowCaptureService _windowCaptureService;
        private readonly LogService _logService;
        private System.Windows.Threading.DispatcherTimer? _previewTimer;
        private System.Windows.Controls.Image? _embeddedPreviewImage;
        private string? _previewWindowTitle;
        private bool _lastPreviewWindowState;

        public UtilityService(
            SharedStateService sharedState,
            WindowCaptureService windowCaptureService,
            LogService logService)
        {
            _sharedState = sharedState;
            _windowCaptureService = windowCaptureService;
            _logService = logService;
        }

        /// <summary>
        /// 生成唯一的视频文件路径
        /// </summary>
        public string GetUniqueVideoPath(string folder, string baseName = "output", string extension = ".mp4")
        {
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            int index = 0;

            while (true)
            {
                string fileName = index == 0
                    ? $"{baseName}_{timestamp}{extension}"
                    : $"{baseName}_{timestamp}_{index}{extension}";

                string path = Path.Combine(folder, fileName);
                if (!File.Exists(path))
                {
                    return path;
                }
                index++;
            }
        }

        /// <summary>
        /// 启动嵌入式预览
        /// </summary>
        public void StartEmbeddedPreview(string windowTitle, System.Windows.Controls.Image previewImage)
        {
            if (string.IsNullOrEmpty(windowTitle) || previewImage == null)
            {
                _logService.AddLog("未选择窗口名称或预览控件为空", LogLevel.Error);
                return;
            }

            StopEmbeddedPreview();

            _embeddedPreviewImage = previewImage;
            _previewWindowTitle = windowTitle;
            _lastPreviewWindowState = false;
            _logService.AddLog($"启动嵌入式预览：{windowTitle}");

            _previewTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(50)
            };
            _previewTimer.Tick += (s, e) => UpdateEmbeddedPreviewWithRetry();
            _previewTimer.Start();

            _logService.AddLog("嵌入式预览已启动", LogLevel.Info);
        }

        /// <summary>
        /// 停止嵌入式预览
        /// </summary>
        public void StopEmbeddedPreview()
        {
            if (_previewTimer != null)
            {
                _previewTimer.Stop();
                _previewTimer = null;
                _logService.AddLog("嵌入式预览定时器已停止");
            }

            _embeddedPreviewImage = null;
            _logService.AddLog("嵌入式预览已停止");
        }

        /// <summary>
        /// 更新嵌入式预览内容（带重试机制）
        /// </summary>
        private void UpdateEmbeddedPreviewWithRetry()
        {
            if (string.IsNullOrEmpty(_previewWindowTitle) || _embeddedPreviewImage == null)
                return;

            try
            {
                nint hwnd = _windowCaptureService.FindWindowByTitle(_previewWindowTitle, silent: true);

                if (hwnd == nint.Zero)
                {
                    if (_lastPreviewWindowState)
                    {
                        _logService.AddLog($"预览窗口未找到：{_previewWindowTitle}", LogLevel.Warning);
                        _lastPreviewWindowState = false;
                    }
                    return;
                }

                if (!_lastPreviewWindowState)
                {
                    _logService.AddLog($"预览窗口已连接：{_previewWindowTitle}", LogLevel.Info);
                    _lastPreviewWindowState = true;
                }

                Mat image = _windowCaptureService.CaptureWindowContent(hwnd);
                var bitmap = MatToBitmapSource(image);

                if (_embeddedPreviewImage != null)
                {
                    _embeddedPreviewImage.Source = bitmap;
                }
            }
            catch (Exception ex)
            {
                if (_lastPreviewWindowState)
                {
                    _logService.AddLog($"预览更新异常：{ex.Message}", LogLevel.Error);
                    _lastPreviewWindowState = false;
                }
            }
        }

        /// <summary>
        /// 根据缩放级别更新裁剪窗口大小
        /// </summary>
        public void UpdateCutWindow(string zoomLevel)
        {
            _sharedState.CutWindow = zoomLevel switch
            {
                "100%" => 55,
                "125%" => 70,
                "150%" => 85,
                "200%" => 110,
                _ => 70
            };
        }

        /// <summary>
        /// 切换窗口置顶状态
        /// </summary>
        public void ToggleTopmost(System.Windows.Window window)
        {
            window.Topmost = !window.Topmost;
        }

        /// <summary>
        /// 将 OpenCV Mat 转换为 WPF BitmapSource
        /// </summary>
        private BitmapSource MatToBitmapSource(Mat image)
        {
            try
            {
                using var memoryStream = new MemoryStream();
                Cv2.ImEncode(".png", image, out var imageData);
                memoryStream.Write(imageData, 0, imageData.Length);
                memoryStream.Seek(0, SeekOrigin.Begin);

                var bitmapImage = new BitmapImage();
                bitmapImage.BeginInit();
                bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                bitmapImage.StreamSource = memoryStream;
                bitmapImage.EndInit();
                bitmapImage.Freeze();

                return bitmapImage;
            }
            catch (Exception ex)
            {
                _logService.AddLog($"Mat 转 BitmapSource 失败：{ex.Message}", LogLevel.Error);
                return new BitmapImage();
            }
        }
    }
}
