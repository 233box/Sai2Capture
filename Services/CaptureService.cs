using CommunityToolkit.Mvvm.ComponentModel;
using OpenCvSharp;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace Sai2Capture.Services
{
    /// <summary>
    /// 窗口录制服务 - 负责将 SAI2 窗口内容录制为 MP4 视频
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
        private readonly Stopwatch _recordingStopwatch = new();

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
        /// 初始化录制服务的 Dispatcher
        /// </summary>
        public void Initialize(Dispatcher dispatcher)
        {
            _dispatcher = dispatcher;
            _logService.AddLog("录制服务已就绪", LogLevel.Info, "UI");
        }

        /// <summary>
        /// 开始录制指定窗口的内容
        /// </summary>
        public void StartCapture(string? windowTitle, bool useExactMatch, double interval)
        {
            try
            {
                if (_sharedState.Running)
                {
                    _logService.AddLog("录制已在运行中，忽略启动请求", LogLevel.Warning, "UI");
                    return;
                }

                if (string.IsNullOrEmpty(windowTitle))
                {
                    _logService.AddLog("窗口标题为空，无法启动录制", LogLevel.Error, "UI");
                    throw new ArgumentException("窗口标题不能为空", nameof(windowTitle));
                }

                _sharedState.Hwnd = _windowCaptureService.FindWindowByTitle(windowTitle);
                _sharedState.Interval = interval;

                if (!_sharedState.IsInitialized)
                {
                    _sharedState.OutputFolder = _settingsService.SavePath;

                    if (!Directory.Exists(_sharedState.OutputFolder))
                    {
                        Directory.CreateDirectory(_sharedState.OutputFolder);
                    }

                    _sharedState.VideoPath = _utilityService.GetUniqueVideoPath(_sharedState.OutputFolder, "recording", ".mp4");

                    _windowCaptureService.InitializeCaptureAsync(_sharedState.Hwnd);

                    InitializeVideoWriter();
                    _sharedState.IsInitialized = true;

                    _logService.AddLogStructured(
                        "首次录制会话初始化完成",
                        LogLevel.Info,
                        "UI",
                        new Dictionary<string, object>
                        {
                            ["WindowTitle"] = windowTitle,
                            ["OutputPath"] = _sharedState.VideoPath,
                            ["FPS"] = 20,
                            ["Resolution"] = "待定"
                        });
                }

                _sharedState.Running = true;
                _recordingStopwatch.Restart();
                Status = "开始录制";
                _logService.AddLogStructured(
                    "BG-录制线程已启动",
                    LogLevel.Info,
                    "BG",
                    new Dictionary<string, object>
                    {
                        ["WindowHwnd"] = $"0x{_sharedState.Hwnd:X}",
                        ["Interval"] = $"{interval}s"
                    });

                _captureThread = new Thread(CaptureLoop) { IsBackground = true, Name = "RecordingThread" };
                _captureThread.Start();
            }
            catch (Exception ex)
            {
                _logService.AddLogStructured(
                    $"启动录制失败：{ex.Message}",
                    LogLevel.Error,
                    "UI",
                    new Dictionary<string, object>
                    {
                        ["WindowTitle"] = windowTitle ?? "null",
                        ["Exception"] = ex.GetType().Name
                    });
                System.Windows.MessageBox.Show(ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                Status = $"错误：{ex.Message}";
            }
        }

        /// <summary>
        /// 暂停当前录制过程
        /// </summary>
        public void PauseCapture()
        {
            _sharedState.Running = false;
            _recordingStopwatch.Stop();
            Status = "录制已暂停";
            _logService.AddLogStructured(
                "BG-录制已暂停",
                LogLevel.Info,
                "BG",
                new Dictionary<string, object>
                {
                    ["TotalFrames"] = _sharedState.FrameNumber,
                    ["SavedFrames"] = _sharedState.SavedCount,
                    ["Elapsed"] = $"{_recordingStopwatch.Elapsed.TotalSeconds:F1}s"
                });
        }

        /// <summary>
        /// 取消录制且不保存视频
        /// </summary>
        public void CancelCapture()
        {
            _sharedState.Running = false;
            _recordingStopwatch.Stop();

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
            Status = "录制已取消（未保存）";
            _logService.AddLog("录制已取消，未保存视频数据", LogLevel.Info, "UI");
        }

        /// <summary>
        /// 完全停止录制过程并保存视频
        /// </summary>
        public void StopCapture()
        {
            _sharedState.Running = false;
            _recordingStopwatch.Stop();

            string? savedVideoPath = _sharedState.VideoPath;
            bool hasVideo = _sharedState.VideoWriter != null;
            int totalFrames = _sharedState.FrameNumber;
            int savedFrames = _sharedState.SavedCount;
            var elapsed = _recordingStopwatch.Elapsed;

            if (hasVideo)
            {
                try
                {
                    if (_sharedState.VideoWriter != null)
                    {
                        _sharedState.VideoWriter.Release();
                        _sharedState.VideoWriter.Dispose();
                        _sharedState.VideoWriter = null;

                        Thread.Sleep(100);

                        if (!string.IsNullOrEmpty(savedVideoPath) && File.Exists(savedVideoPath))
                        {
                            var fileInfo = new FileInfo(savedVideoPath);
                            var bitrateKbps = elapsed.TotalSeconds > 0 
                                ? fileInfo.Length * 8 / elapsed.TotalSeconds / 1000 
                                : 0;

                            _logService.AddLogStructured(
                                "MP4 保存成功",
                                LogLevel.Info,
                                "UI",
                                new Dictionary<string, object>
                                {
                                    ["FilePath"] = savedVideoPath,
                                    ["FileSize"] = $"{fileInfo.Length / 1024.0 / 1024.0:F2} MB",
                                    ["Duration"] = $"{elapsed.TotalSeconds:F1}s",
                                    ["FrameCount"] = totalFrames,
                                    ["SavedFrames"] = savedFrames,
                                    ["AvgBitrate"] = $"{bitrateKbps:F0} kbps"
                                });
                            Status = $"录制停止，视频已保存";
                        }
                        else
                        {
                            _logService.AddLog($"视频文件不存在：{savedVideoPath}", LogLevel.Warning, "UI");
                            Status = "录制停止，但视频文件未找到";
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logService.AddLogStructured(
                        $"保存视频失败：{ex.Message}",
                        LogLevel.Error,
                        "UI",
                        new Dictionary<string, object>
                        {
                            ["SavedPath"] = savedVideoPath ?? "null",
                            ["Exception"] = ex.GetType().Name
                        });
                    Status = "录制停止，但保存视频时出错";
                }
            }
            else
            {
                Status = "录制停止";
                _logService.AddLog("录制停止 - 未生成视频文件", LogLevel.Info, "UI");
            }

            _sharedState.ResetCaptureState();
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

                var directory = Path.GetDirectoryName(_sharedState.VideoPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var firstFrame = _windowCaptureService.CaptureWindowContent(_sharedState.Hwnd);

                if (firstFrame == null || firstFrame.Empty())
                {
                    throw new Exception("无法捕获第一帧或帧为空");
                }

                int width = firstFrame.Width;
                int height = firstFrame.Height;

                if (width <= 0 || height <= 0)
                {
                    throw new Exception($"无效的帧尺寸：{width}x{height}");
                }

                double fps = 20;
                var fourcc = VideoWriter.FourCC('m', 'p', '4', 'v');

                _sharedState.VideoWriter = new VideoWriter(
                    _sharedState.VideoPath,
                    fourcc,
                    fps,
                    new OpenCvSharp.Size(width, height));

                if (!_sharedState.VideoWriter.IsOpened())
                {
                    _logService.AddLogStructured(
                        "VideoWriter 初始化失败",
                        LogLevel.Error,
                        "UI",
                        new Dictionary<string, object>
                        {
                            ["VideoPath"] = _sharedState.VideoPath,
                            ["Resolution"] = $"{width}x{height}",
                            ["FPS"] = fps,
                            ["FourCC"] = "mp4v"
                        });
                    throw new Exception("无法创建视频写入器 - VideoWriter 初始化失败");
                }

                _logService.AddLogStructured(
                    "MP4 视频写入器已创建",
                    LogLevel.Info,
                    "BG",
                    new Dictionary<string, object>
                    {
                        ["VideoPath"] = _sharedState.VideoPath,
                        ["Resolution"] = $"{width}x{height}",
                        ["FPS"] = fps,
                        ["FourCC"] = "mp4v"
                    });
                firstFrame.Dispose();
            }
            catch (Exception ex)
            {
                _logService.AddLogStructured(
                    $"创建视频写入器失败：{ex.Message}",
                    LogLevel.Error,
                    "UI",
                    new Dictionary<string, object>
                    {
                        ["ExceptionType"] = ex.GetType().Name,
                        ["StackTrace"] = ex.StackTrace ?? "null"
                    });
                throw;
            }
        }

        /// <summary>
        /// 录制循环核心实现（后台线程）
        /// </summary>
        private void CaptureLoop()
        {
            _logService.AddLogStructured("BG-录制循环启动", LogLevel.Debug);
            int errorCount = 0;
            Mat? image = null;

            try
            {
                while (_sharedState.Running)
                {
                    image = null;
                    try
                    {
                        if (_sharedState.Hwnd == nint.Zero)
                        {
                            _logService.AddLog("BG-窗口句柄无效，退出录制循环", LogLevel.Error, "BG");
                            break;
                        }

                        image = _windowCaptureService.CaptureWindowContent(_sharedState.Hwnd);

                        if (image == null || image.Empty())
                        {
                            _logService.AddLog("BG-捕获到空图像，跳过此帧", LogLevel.Warning, "BG");
                            image?.Dispose();
                            image = null;
                            continue;
                        }

                        if (_sharedState.LastImage?.IsDisposed == true)
                        {
                            _sharedState.LastImage = null;
                        }

                        _windowCaptureService.SaveIfModified(image);

                        _dispatcher?.Invoke(() =>
                        {
                            Status = $"已录制 {_sharedState.FrameNumber} 帧";
                        });

                        if (_sharedState.FrameNumber % 1000 == 0 && _sharedState.FrameNumber > 0)
                        {
                            _logService.AddLogStructured(
                                "BG-录制进度",
                                LogLevel.Debug,
                                "BG",
                                new Dictionary<string, object>
                                {
                                    ["TotalFrames"] = _sharedState.FrameNumber,
                                    ["SavedFrames"] = _sharedState.SavedCount,
                                    ["Elapsed"] = $"{_recordingStopwatch.Elapsed.TotalSeconds:F1}s",
                                    ["MemoryMB"] = GC.GetTotalMemory(false) / 1024 / 1024
                                });
                        }

                        _sharedState.FrameNumber++;
                        Thread.Sleep((int)(_sharedState.Interval * 1000));
                    }
                    catch (AccessViolationException ex)
                    {
                        errorCount++;
                        _logService.AddLogStructured(
                            $"BG-访问违规错误 (累计：{errorCount})",
                            errorCount > 3 ? LogLevel.Error : LogLevel.Warning,
                            "BG",
                            new Dictionary<string, object>
                            {
                                ["Message"] = ex.Message,
                                ["StackTrace"] = ex.StackTrace ?? "null"
                            });

                        _dispatcher?.Invoke(() =>
                        {
                            Status = $"访问违规：{ex.Message}";
                        });
                        Thread.Sleep(1000);
                    }
                    catch (Exception ex)
                    {
                        errorCount++;
                        _logService.AddLogStructured(
                            $"BG-录制错误 (累计：{errorCount})",
                            LogLevel.Warning,
                            "BG",
                            new Dictionary<string, object>
                            {
                                ["ExceptionType"] = ex.GetType().Name,
                                ["Message"] = ex.Message,
                                ["StackTrace"] = ex.StackTrace ?? "null"
                            });

                        _dispatcher?.Invoke(() =>
                        {
                            Status = $"录制错误：{ex.Message}";
                        });
                        Thread.Sleep(1000);
                    }
                    finally
                    {
                        try
                        {
                            if (image != null && !image.IsDisposed)
                            {
                                image.Dispose();
                            }
                        }
                        catch { }
                        image = null;
                    }
                }

                _logService.AddLogStructured(
                    "BG-录制循环正常结束",
                    LogLevel.Info,
                    "BG",
                    new Dictionary<string, object>
                    {
                        ["TotalFrames"] = _sharedState.FrameNumber,
                        ["SavedFrames"] = _sharedState.SavedCount,
                        ["ErrorCount"] = errorCount,
                        ["Elapsed"] = $"{_recordingStopwatch.Elapsed.TotalSeconds:F1}s"
                    });
            }
            catch (Exception ex)
            {
                _logService.AddLogStructured(
                    "BG-录制循环致命错误",
                    LogLevel.Error,
                    "BG",
                    new Dictionary<string, object>
                    {
                        ["ExceptionType"] = ex.GetType().Name,
                        ["Message"] = ex.Message,
                        ["StackTrace"] = ex.StackTrace ?? "null"
                    });

                _dispatcher?.Invoke(() =>
                {
                    Status = $"录制致命错误：{ex.Message}";
                });
            }
            finally
            {
                try
                {
                    if (image != null && !image.IsDisposed)
                    {
                        image.Dispose();
                    }
                }
                catch { }
            }
        }
    }
}
