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
    /// 提供各种辅助功能：
    /// 1. 路径和目录管理
    /// 2. 窗口预览功能
    /// 3. 窗口置顶控制
    /// 4. 图像格式转换
    /// </summary>
    public partial class UtilityService : ObservableObject
    {
        private readonly SharedStateService _sharedState;
        private readonly WindowCaptureService _windowCaptureService;
        private readonly LogService _logService;
        private System.Windows.Threading.DispatcherTimer? _previewTimer;

        /// <summary>
        /// 初始化实用工具服务
        /// </summary>
        /// <param name="sharedState">共享状态服务</param>
        /// <param name="windowCaptureService">窗口捕获服务</param>
        /// <param name="logService">日志服务</param>
        public UtilityService(SharedStateService sharedState, WindowCaptureService windowCaptureService, LogService logService)
        {
            _sharedState = sharedState;
            _windowCaptureService = windowCaptureService;
            _logService = logService;
        }

        /// <summary>
        /// 生成唯一的视频文件路径
        /// 格式：{baseName}_{timestamp}[_{index}]{extension}
        /// 自动递增索引直到找到不存在的文件名
        /// </summary>
        /// <param name="folder">目标文件夹</param>
        /// <param name="baseName">文件名基础部分(默认"output")</param>
        /// <param name="extension">文件扩展名(默认".mp4")</param>
        /// <returns>唯一的文件路径</returns>
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
        /// 启动窗口内容预览
        /// 创建定时器定期更新预览窗口内容
        /// </summary>
        /// <param name="windowTitle">要预览的窗口标题</param>
        /// <param name="previewWindow">预览窗口对象</param>
        /// <exception cref="Exception">窗口查找失败时显示错误消息</exception>
        public async void StartPreview(string windowTitle, System.Windows.Window previewWindow)
        {
            if (string.IsNullOrEmpty(windowTitle))
            {
                System.Windows.MessageBox.Show("未选择窗口名称", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            try
            {
                // 停止之前的定时器
                StopPreview();

                _logService.AddLog($"启动预览窗口: {windowTitle}");
                nint hwnd = _windowCaptureService.FindWindowByTitle(windowTitle);
                
                // 初始化捕获会话用于预览
                _logService.AddLog("为预览初始化捕获会话");
                bool captureInitialized = await _windowCaptureService.InitializeCaptureAsync(hwnd);
                
                if (captureInitialized)
                {
                    _logService.AddLog("预览将使用 PrintWindow API 进行截图");
                }

                _previewTimer = new System.Windows.Threading.DispatcherTimer();
                _previewTimer.Interval = TimeSpan.FromMilliseconds(50);
                _previewTimer.Tick += (sender, e) => UpdatePreview(hwnd, previewWindow);
                _previewTimer.Start();

                // 窗口关闭时停止定时器
                previewWindow.Closed += (sender, e) => StopPreview();
                
                _logService.AddLog("预览已启动", LogLevel.Info);
            }
            catch (Exception ex)
            {
                _logService.AddLog($"启动预览失败: {ex.Message}", LogLevel.Error);
                System.Windows.MessageBox.Show($"找不到指定窗口: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 停止窗口预览
        /// 清理定时器资源和 WGC 捕获会话
        /// </summary>
        public void StopPreview()
        {
            if (_previewTimer != null)
            {
                _previewTimer.Stop();
                _previewTimer = null;
                _logService.AddLog("预览定时器已停止");
            }

            // 预览捕获会话已停止
            _logService.AddLog("预览捕获会话已停止");
        }

        private System.Windows.Controls.Image? _embeddedPreviewImage;

        /// <summary>
        /// 启动嵌入式预览
        /// 在主界面内显示指定窗口的实时预览
        /// </summary>
        /// <param name="windowTitle">要预览的窗口标题</param>
        /// <param name="previewImage">预览图像控件</param>
        public async void StartEmbeddedPreview(string windowTitle, System.Windows.Controls.Image previewImage)
        {
            if (string.IsNullOrEmpty(windowTitle) || previewImage == null)
            {
                _logService.AddLog("未选择窗口名称或预览控件为空", LogLevel.Error);
                return;
            }

            try
            {
                // 停止之前的定时器
                StopEmbeddedPreview();

                _embeddedPreviewImage = previewImage;
                _logService.AddLog($"启动嵌入式预览: {windowTitle}");
                
                nint hwnd = _windowCaptureService.FindWindowByTitle(windowTitle);

                // 初始化捕获会话用于预览
                _logService.AddLog("为嵌入式预览初始化捕获会话");
                bool captureInitialized = await _windowCaptureService.InitializeCaptureAsync(hwnd);

                if (captureInitialized)
                {
                    _logService.AddLog("嵌入式预览将使用 PrintWindow API 进行截图");
                }

                _previewTimer = new System.Windows.Threading.DispatcherTimer();
                _previewTimer.Interval = TimeSpan.FromMilliseconds(50);
                _previewTimer.Tick += (sender, e) => UpdateEmbeddedPreview(hwnd);
                _previewTimer.Start();

                _logService.AddLog("嵌入式预览已启动", LogLevel.Info);
            }
            catch (Exception ex)
            {
                _logService.AddLog($"启动嵌入式预览失败: {ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>
        /// 停止嵌入式预览
        /// 清理定时器资源
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
        /// 更新嵌入式预览内容
        /// 捕获指定窗口内容并直接更新预览控件
        /// </summary>
        /// <param name="hwnd">目标窗口句柄</param>
        private void UpdateEmbeddedPreview(nint hwnd)
        {
            try
            {
                Mat image = _windowCaptureService.CaptureWindowContent(hwnd);
                var bitmap = MatToBitmapSource(image);

                if (_embeddedPreviewImage != null)
                {
                    _embeddedPreviewImage.Source = bitmap;
                }
            }
            catch (Exception ex)
            {
                _logService.AddLog($"嵌入式预览更新失败: {ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>
        /// 根据缩放级别更新裁剪窗口大小
        /// 不同缩放级别对应不同的裁剪像素值
        /// </summary>
        /// <param name="zoomLevel">缩放级别("100%", "125%", etc.)</param>
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
        /// 修改目标窗口的Topmost属性
        /// </summary>
        /// <param name="window">目标窗口对象</param>
        public void ToggleTopmost(System.Windows.Window window)
        {
            _sharedState.IsTopmost = !_sharedState.IsTopmost;
            window.Topmost = _sharedState.IsTopmost;
        }

        /// <summary>
        /// 创建带时间戳的输出文件夹
        /// 格式：output_frames/yyyy-MM-dd_HH-mm-ss/
        /// </summary>
        /// <returns>创建的文件夹路径</returns>
        public string CreateOutputFolder()
        {
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            string outputFolder = Path.Combine("output_frames", timestamp);
            Directory.CreateDirectory(outputFolder);
            return outputFolder;
        }

        /// <summary>
        /// 更新预览窗口内容
        /// 捕获指定窗口内容并转换为WPF图像显示
        /// </summary>
        /// <param name="hwnd">目标窗口句柄</param>
        /// <param name="previewWindow">预览窗口对象</param>
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
                
                // _logService.AddLog("预览窗口内容已更新", LogLevel.Info);
            }
            catch (Exception ex)
            {
                _logService.AddLog($"预览更新失败: {ex.Message}", LogLevel.Error);
            }
        }
        /// <summary>
        /// 将OpenCV Mat转换为WPF BitmapSource
        /// 使用内存中的PNG格式转换，避免文件I/O操作
        /// </summary>
        /// <param name="image">OpenCV Mat图像</param>
        /// <returns>WPF BitmapSource对象</returns>
        private BitmapSource MatToBitmapSource(Mat image)
        {
            try
            {
                using (var memoryStream = new System.IO.MemoryStream())
                {
                    // 使用内存中的PNG编码而不是文件
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
            }
            catch (Exception ex)
            {
                _logService.AddLog($"Mat转BitmapSource失败: {ex.Message}", LogLevel.Error);
                
                // 创建一个默认的空图像
                return new BitmapImage();
            }
        }

    }
}
