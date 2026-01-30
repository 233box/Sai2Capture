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
    public partial class UtilityService : ObservableObject
    {
        private readonly SharedStateService _sharedState;
        private readonly WindowCaptureService _windowCaptureService;

        public UtilityService(SharedStateService sharedState, WindowCaptureService windowCaptureService)
        {
            _sharedState = sharedState;
            _windowCaptureService = windowCaptureService;
        }

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

        public void StartPreview(string windowTitle, System.Windows.Window previewWindow)
        {
            if (string.IsNullOrEmpty(windowTitle))
            {
                MessageBox.Show("未选择窗口名称", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            try
            {
                nint hwnd = _windowCaptureService.FindWindowByTitle(windowTitle);
                var timer = new System.Windows.Threading.DispatcherTimer();
                timer.Interval = TimeSpan.FromMilliseconds(500);
                timer.Tick += (sender, e) => UpdatePreview(hwnd, previewWindow);
                timer.Start();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"找不到指定窗口: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

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

        public void ToggleTopmost(System.Windows.Window window)
        {
            _sharedState.IsTopmost = !_sharedState.IsTopmost;
            window.Topmost = _sharedState.IsTopmost;
        }

        public string CreateOutputFolder()
        {
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            string outputFolder = Path.Combine("output_frames", timestamp);
            Directory.CreateDirectory(outputFolder);
            return outputFolder;
        }

        private void UpdatePreview(nint hwnd, System.Windows.Window previewWindow)
        {
            try
            {
                Mat image = _windowCaptureService.CaptureWindowContent(hwnd);
                var bitmap = MatToBitmapSource(image);
                
                if (previewWindow.Content is System.Windows.Controls.Image imageControl)
                {
                    imageControl.Source = bitmap;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"预览更新失败: {ex.Message}");
            }
        }
        private BitmapSource MatToBitmapSource(Mat image)
        {
            using (var memoryStream = new System.IO.MemoryStream())
            {
                Cv2.ImWrite("temp.bmp", image);
                var bytes = File.ReadAllBytes("temp.bmp");
                memoryStream.Write(bytes, 0, bytes.Length);
                File.Delete("temp.bmp");
                memoryStream.Seek(0, System.IO.SeekOrigin.Begin);

                var bitmapImage = new BitmapImage();
                bitmapImage.BeginInit();
                bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                bitmapImage.StreamSource = memoryStream;
                bitmapImage.EndInit();
                bitmapImage.Freeze();

                return bitmapImage;
            }
        }
    }
}
