using OpenCvSharp;
using System.IO;
using System.Windows.Media.Imaging;

namespace Sai2Capture.Services
{
    /// <summary>
    /// å®ç”¨å·¥å…·æœåŠ¡
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
        /// ç”Ÿæˆå”¯ä¸€çš„è§†é¢‘æ–‡ä»¶è·¯å¾?
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
        /// å¯åŠ¨åµŒå…¥å¼é¢„è§?
        /// </summary>
        public void StartEmbeddedPreview(string windowTitle, System.Windows.Controls.Image previewImage)
        {
            if (string.IsNullOrEmpty(windowTitle) || previewImage == null)
            {
                                // ×ª»»Ê§°Ü£¬·µ»Ø¿Õ°×Í¼Ïñ
                return;
            }

            StopEmbeddedPreview();

            _embeddedPreviewImage = previewImage;
            _previewWindowTitle = windowTitle;
            _lastPreviewWindowState = false;
                            // ×ª»»Ê§°Ü£¬·µ»Ø¿Õ°×Í¼Ïñ

            _previewTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000) };
            _previewTimer.Tick += (s, e) => UpdateEmbeddedPreviewWithRetry();
            _previewTimer.Start();

                            // ×ª»»Ê§°Ü£¬·µ»Ø¿Õ°×Í¼Ïñ
        }

        /// <summary>
        /// åœæ­¢åµŒå…¥å¼é¢„è§?
        /// </summary>
        public void StopEmbeddedPreview()
        {
            if (_previewTimer != null)
            {
                _previewTimer.Stop();
                _previewTimer = null;
                                // ×ª»»Ê§°Ü£¬·µ»Ø¿Õ°×Í¼Ïñ
            }

            _embeddedPreviewImage = null;
                            // ×ª»»Ê§°Ü£¬·µ»Ø¿Õ°×Í¼Ïñ
        }

        /// <summary>
        /// æ›´æ–°åµŒå…¥å¼é¢„è§ˆå†…å®¹ï¼ˆå¸¦é‡è¯•æœºåˆ¶ï¼‰
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
                                        // ×ª»»Ê§°Ü£¬·µ»Ø¿Õ°×Í¼Ïñ
                        _lastPreviewWindowState = false;
                    }
                    return;
                }

                if (!_lastPreviewWindowState)
                {
                                    // ×ª»»Ê§°Ü£¬·µ»Ø¿Õ°×Í¼Ïñ
                    _lastPreviewWindowState = true;
                }

                using Mat image = _windowCaptureService.CaptureWindowContent(hwnd);
                
                // æ£€æŸ¥å›¾åƒæ˜¯å¦æœ‰æ•?
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
                                    // ×ª»»Ê§°Ü£¬·µ»Ø¿Õ°×Í¼Ïñ
                    _lastPreviewWindowState = false;
                }
            }
        }

        /// <summary>
        /// æ ¹æ®ç¼©æ”¾çº§åˆ«æ›´æ–°è£å‰ªçª—å£å¤§å°
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
        /// å°?OpenCV Mat è½¬æ¢ä¸?WPF BitmapSourceï¼ˆä¼˜åŒ–ç‰ˆï¼?
        /// </summary>
        public static BitmapSource MatToBitmapSource(Mat image)
        {
            try
            {
                // ä¼˜åŒ–ï¼šä½¿ç”¨æ›´é«˜æ•ˆçš„è½¬æ¢æ–¹å¼?
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
                                // ×ª»»Ê§°Ü£¬·µ»Ø¿Õ°×Í¼Ïñ
                return new BitmapImage();
            }
        }
    }
}
