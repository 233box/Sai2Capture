using OpenCvSharp;
using System.IO;
using System.Windows.Media.Imaging;

namespace Sai2Capture.src.Services
{
    /// <summary>
    /// 实用工具服务
    /// </summary>
    public partial class UtilityService
    {
        private readonly SharedStateService _sharedState;
        private readonly WindowCaptureService _windowCaptureService;
        private readonly LogService _logService;
        private System.Windows.Threading.DispatcherTimer? _previewTimer;
        private System.Windows.Controls.Image? _embeddedPreviewImage;
        private string? _previewWindowTitle;
        private bool _lastPreviewWindowState;

        public UtilityService(SharedStateService sharedState, WindowCaptureService windowCaptureService, LogService logService)
        {
            _sharedState = sharedState;
            _windowCaptureService = windowCaptureService;
            _logService = logService;
        }

        /// <summary>
        /// 生成唯一的视频文件路�?
        /// </summary>
        public string GetUniqueVideoPath(string folder, string baseName = "output", string extension = ".mp4")
        {
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            int index = 0;
            while (true)
            {
                string fileName = index == 0 ? $"{baseName}_{timestamp}{extension}" : $"{baseName}_{timestamp}_{index}{extension}";
                string path = Path.Combine(folder, fileName);
                if (!File.Exists(path)) return path;
                index++;
            }
        }

        /// <summary>
        /// 启动嵌入式预�?
        /// </summary>
        public void StartEmbeddedPreview(string windowTitle, System.Windows.Controls.Image previewImage)
        {
            if (string.IsNullOrEmpty(windowTitle) || previewImage == null)
            {
                                // ת��ʧ�ܣ����ؿհ�ͼ��
                return;
            }

            StopEmbeddedPreview();

            _embeddedPreviewImage = previewImage;
            _previewWindowTitle = windowTitle;
            _lastPreviewWindowState = false;
                            // ת��ʧ�ܣ����ؿհ�ͼ��

            _previewTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000) };
            _previewTimer.Tick += (s, e) => UpdateEmbeddedPreviewWithRetry();
            _previewTimer.Start();

                            // ת��ʧ�ܣ����ؿհ�ͼ��
        }

        /// <summary>
        /// 停止嵌入式预�?
        /// </summary>
        public void StopEmbeddedPreview()
        {
            if (_previewTimer != null)
            {
                _previewTimer.Stop();
                _previewTimer = null;
                                // ת��ʧ�ܣ����ؿհ�ͼ��
            }

            _embeddedPreviewImage = null;
                            // ת��ʧ�ܣ����ؿհ�ͼ��
        }

        /// <summary>
        /// 更新嵌入式预览内容（带重试机制）
        /// </summary>
        private void UpdateEmbeddedPreviewWithRetry()
        {
            if (string.IsNullOrEmpty(_previewWindowTitle) || _embeddedPreviewImage == null) return;

            try
            {
                nint hwnd = _windowCaptureService.FindWindowByTitle(_previewWindowTitle, silent: true);
                if (hwnd == nint.Zero)
                {
                    if (_lastPreviewWindowState)
                    {
                                        // ת��ʧ�ܣ����ؿհ�ͼ��
                        _lastPreviewWindowState = false;
                    }
                    return;
                }

                if (!_lastPreviewWindowState)
                {
                                    // ת��ʧ�ܣ����ؿհ�ͼ��
                    _lastPreviewWindowState = true;
                }

                using Mat image = _windowCaptureService.CaptureWindowContent(hwnd);
                
                // 检查图像是否有�?
                if (image == null || image.IsDisposed || image.Empty())
                {
                    return;
                }
                
                var bitmap = MatToBitmapSource(image);

                if (_embeddedPreviewImage != null)
                    _embeddedPreviewImage.Source = bitmap;
            }
            catch
            {
                if (_lastPreviewWindowState)
                {
                                    // ת��ʧ�ܣ����ؿհ�ͼ��
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
        /// �?OpenCV Mat 转换�?WPF BitmapSource（优化版�?
        /// </summary>
        public static BitmapSource MatToBitmapSource(Mat image)
        {
            try
            {
                // 优化：使用更高效的转换方�?
                using var memoryStream = new MemoryStream();
                Cv2.ImEncode(".bmp", image, out var imageData);
                memoryStream.Write(imageData, 0, imageData.Length);
                memoryStream.Seek(0, SeekOrigin.Begin);

                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = memoryStream;
                bitmap.EndInit();
                bitmap.Freeze();

                return bitmap;
            }
            catch
            {
                                // ת��ʧ�ܣ����ؿհ�ͼ��
                return new BitmapImage();
            }
        }
    }
}
