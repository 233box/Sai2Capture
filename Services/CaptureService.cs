using CommunityToolkit.Mvvm.ComponentModel;
using OpenCvSharp;
using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace Sai2Capture.Services
{
    /// <summary>
    /// 窗口内容捕获服务
    /// </summary>
    public partial class CaptureService : ObservableObject
    {
        private readonly SharedStateService _sharedState;
        private readonly WindowCaptureService _windowCaptureService;
        private readonly UtilityService _utilityService;
        private readonly LogService _logService;
        private readonly SettingsService _settingsService;
        private Thread? _captureThread;
        private Dispatcher? _dispatcher;

        /// <summary>
        /// 获取共享状态服务
        /// </summary>
        public SharedStateService SharedState => _sharedState;

        [ObservableProperty]
        private string _status = "未开始";

        public CaptureService(
            SharedStateService sharedState,
            WindowCaptureService windowCaptureService,
            UtilityService utilityService,
            LogService logService,
            SettingsService settingsService)
        {
            _sharedState = sharedState;
            _windowCaptureService = windowCaptureService;
            _utilityService = utilityService;
            _logService = logService;
            _settingsService = settingsService;
        }

        /// <summary>
        /// 初始化捕获服务的 Dispatcher
        /// </summary>
        public void Initialize(Dispatcher dispatcher)
        {
            _dispatcher = dispatcher;
            _logService.AddLog("捕获服务已初始化");
        }

        /// <summary>
        /// 开始捕获指定窗口的内容
        /// </summary>
        public void StartCapture(string? windowTitle, bool useExactMatch, double interval)
        {
            try
            {
                if (_sharedState.Running)
                {
                    _logService.AddLog("捕获已在运行中，忽略启动请求", LogLevel.Warning);
                    return;
                }

                if (string.IsNullOrEmpty(windowTitle))
                {
                    _logService.AddLog("窗口标题为空，无法启动捕获", LogLevel.Error);
                    throw new ArgumentException("窗口标题不能为空", nameof(windowTitle));
                }

                _logService.AddLog($"查找窗口：{windowTitle}");
                _sharedState.Hwnd = _windowCaptureService.FindWindowByTitle(windowTitle);
                _sharedState.Interval = interval;

                if (!_sharedState.IsInitialized)
                {
                    _logService.AddLog("首次启动，创建输出目录和视频写入器");

                    _sharedState.OutputFolder = _settingsService.SavePath;
                    _logService.AddLog($"输出文件夹完整路径：{_sharedState.OutputFolder}");

                    if (!Directory.Exists(_sharedState.OutputFolder))
                    {
                        Directory.CreateDirectory(_sharedState.OutputFolder);
                        _logService.AddLog($"已创建输出目录：{_sharedState.OutputFolder}");
                    }

                    _sharedState.VideoPath = _utilityService.GetUniqueVideoPath(_sharedState.OutputFolder, "output", ".mp4");
                    _logService.AddLog($"视频完整路径：{_sharedState.VideoPath}");

                    _logService.AddLog("初始化捕获会话");
                    var captureInitialized = _windowCaptureService.InitializeCaptureAsync(_sharedState.Hwnd).Result;
                    if (captureInitialized)
                    {
                        _logService.AddLog("捕获会话初始化成功，将使用 PrintWindow API 进行截图");
                    }

                    InitializeVideoWriter();
                    _sharedState.IsInitialized = true;
                    _logService.AddLog("首次启动初始化完成，IsInitialized 标志已设置为 true");
                }

                _sharedState.Running = true;
                Status = "开始捕获";
                _logService.AddLog($"捕获线程启动 - 窗口句柄：{_sharedState.Hwnd}, 间隔：{interval}秒");

                _captureThread = new Thread(CaptureLoop) { IsBackground = true };
                _captureThread.Start();
            }
            catch (Exception ex)
            {
                _logService.AddLog($"启动捕获失败：{ex.Message}", LogLevel.Error);
                System.Windows.MessageBox.Show(ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                Status = $"错误：{ex.Message}";
            }
        }

        /// <summary>
        /// 暂停当前捕获过程
        /// </summary>
        public void PauseCapture()
        {
            _sharedState.Running = false;
            Status = "捕获已暂停";
            _logService.AddLog($"捕获已暂停 - 已捕获 {_sharedState.FrameNumber} 帧", LogLevel.Warning);
        }

        /// <summary>
        /// 取消捕获且不保存视频
        /// </summary>
        public void CancelCapture()
        {
            _sharedState.Running = false;

            if (_sharedState.VideoWriter != null)
            {
                try
                {
                    _sharedState.VideoWriter.Release();
                    _sharedState.VideoWriter.Dispose();
                }
                catch { }
                finally
                {
                    _sharedState.VideoWriter = null;
                }
            }

            _sharedState.VideoPath = null;
            _sharedState.ResetCaptureState();
            Status = "捕获已取消（未保存）";
            _logService.AddLog("捕获已取消，未保存视频数据");
        }

        /// <summary>
        /// 完全停止捕获过程并保存视频
        /// </summary>
        public void StopCapture()
        {
            _sharedState.Running = false;

            string? savedVideoPath = _sharedState.VideoPath;
            bool hasVideo = _sharedState.VideoWriter != null;
            int totalFrames = _sharedState.FrameNumber;
            int savedFrames = _sharedState.SavedCount;

            _logService.AddLog("捕获会话已完成");

            if (hasVideo)
            {
                _logService.AddLog($"释放视频写入器 - 总帧数：{totalFrames}, 有效捕获：{savedFrames}");

                try
                {
                    if (_sharedState.VideoWriter != null)
                    {
                        _logService.AddLog("调用 VideoWriter.Release() 刷新缓冲区...");
                        _sharedState.VideoWriter.Release();

                        _logService.AddLog("调用 VideoWriter.Dispose() 释放资源...");
                        _sharedState.VideoWriter.Dispose();
                        _sharedState.VideoWriter = null;

                        Thread.Sleep(100);

                        if (!string.IsNullOrEmpty(savedVideoPath) && File.Exists(savedVideoPath))
                        {
                            var fileInfo = new FileInfo(savedVideoPath);
                            _logService.AddLog($"✓ 视频文件已成功保存：{savedVideoPath}");
                            _logService.AddLog($"  文件大小：{fileInfo.Length / 1024.0:F2} KB");
                            Status = $"捕获停止，视频已保存：{savedVideoPath}";
                        }
                        else
                        {
                            _logService.AddLog($"⚠ 警告：视频文件不存在：{savedVideoPath}", LogLevel.Warning);
                            Status = "捕获停止，但视频文件未找到";
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logService.AddLog($"释放视频写入器时出错：{ex.Message}", LogLevel.Error);
                    Status = "捕获停止，但保存视频时出错";
                }
            }
            else
            {
                Status = "捕获停止";
                _logService.AddLog("捕获停止 - 未生成视频文件");
            }

            _sharedState.ResetCaptureState();
            _logService.AddLog("捕获状态已重置到初始值");
        }

        /// <summary>
        /// 初始化视频写入器
        /// </summary>
        private void InitializeVideoWriter()
        {
            try
            {
                if (string.IsNullOrEmpty(_sharedState.VideoPath))
                {
                    throw new Exception("视频路径未设置");
                }

                _logService.AddLog($"准备创建视频写入器 - 路径：{_sharedState.VideoPath}");

                var directory = Path.GetDirectoryName(_sharedState.VideoPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    _logService.AddLog($"输出目录不存在，正在创建：{directory}");
                    Directory.CreateDirectory(directory);
                }

                _logService.AddLog("捕获第一帧以确定视频尺寸...");
                var firstFrame = _windowCaptureService.CaptureWindowContent(_sharedState.Hwnd);

                if (firstFrame == null || firstFrame.Empty())
                {
                    throw new Exception("无法捕获第一帧或帧为空");
                }

                int width = firstFrame.Width;
                int height = firstFrame.Height;
                _logService.AddLog($"第一帧尺寸：{width}x{height}");

                if (width <= 0 || height <= 0)
                {
                    throw new Exception($"无效的帧尺寸：{width}x{height}");
                }

                double fps = 20;
                _logService.AddLog($"计算帧率：{fps:F2} FPS (间隔：{_sharedState.Interval}秒)");

                var fourcc = VideoWriter.FourCC('m', 'p', '4', 'v');
                _logService.AddLog($"使用编码器：MP4V (FourCC: {fourcc})");

                _sharedState.VideoWriter = new VideoWriter(
                    _sharedState.VideoPath,
                    fourcc,
                    fps,
                    new OpenCvSharp.Size(width, height));

                if (!_sharedState.VideoWriter.IsOpened())
                {
                    _logService.AddLog("VideoWriter.IsOpened() 返回 false", LogLevel.Error);
                    _logService.AddLog("可能的原因:", LogLevel.Error);
                    _logService.AddLog("1. 输出目录不存在或无写入权限", LogLevel.Error);
                    _logService.AddLog("2. MP4V 编码器不可用", LogLevel.Error);
                    _logService.AddLog("3. 视频尺寸无效", LogLevel.Error);
                    _logService.AddLog($"4. 文件路径过长或包含非法字符：{_sharedState.VideoPath}", LogLevel.Error);
                    throw new Exception("无法创建视频写入器 - VideoWriter 初始化失败");
                }

                _logService.AddLog($"✓ 视频写入器已成功创建 - 尺寸：{width}x{height}, FPS: {fps:F2}");
                firstFrame.Dispose();
            }
            catch (Exception ex)
            {
                _logService.AddLog($"创建视频写入器失败：{ex.Message}", LogLevel.Error);
                _logService.AddLog($"异常堆栈：{ex.StackTrace}", LogLevel.Error);
                throw;
            }
        }

        /// <summary>
        /// 捕获循环核心实现
        /// </summary>
        private void CaptureLoop()
        {
            _logService.AddLog("捕获循环开始");
            int errorCount = 0;

            try
            {
                while (_sharedState.Running)
                {
                    try
                    {
                        var image = _windowCaptureService.CaptureWindowContent(_sharedState.Hwnd);
                        _windowCaptureService.SaveIfModified(image);

                        _dispatcher?.Invoke(() =>
                        {
                            Status = $"已捕获帧 #{_sharedState.FrameNumber}";
                        });

                        if (_sharedState.FrameNumber % 1000 == 0)
                        {
                            _logService.AddLog($"捕获进度：{_sharedState.FrameNumber} 帧，已保存：{_sharedState.SavedCount} 帧");
                        }

                        _sharedState.FrameNumber++;
                        Thread.Sleep((int)(_sharedState.Interval * 1000));
                    }
                    catch (Exception ex)
                    {
                        errorCount++;
                        _logService.AddLog($"捕获错误 (总计：{errorCount}): {ex.Message}", LogLevel.Error);

                        _dispatcher?.Invoke(() =>
                        {
                            Status = $"捕获错误：{ex.Message}";
                        });
                        Thread.Sleep(1000);
                    }
                }

                _logService.AddLog($"捕获循环结束 - 总帧数：{_sharedState.FrameNumber}, 已保存：{_sharedState.SavedCount} 帧，错误次数：{errorCount}");
            }
            catch (Exception ex)
            {
                _logService.AddLog($"捕获循环致命错误：{ex.Message}", LogLevel.Error);
                _logService.AddLog($"异常堆栈：{ex.StackTrace}", LogLevel.Error);

                _dispatcher?.Invoke(() =>
                {
                    Status = $"捕获致命错误：{ex.Message}";
                });
            }
        }
    }
}
