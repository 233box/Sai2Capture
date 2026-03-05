using CommunityToolkit.Mvvm.ComponentModel;
using OpenCvSharp;
using System.IO;
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
        private readonly RecordingDataService _recordingDataService;
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
            SettingsService settingsService,
            RecordingDataService recordingDataService)
        {
            _sharedState = sharedState;
            _windowCaptureService = windowCaptureService;
            _utilityService = utilityService;
            _logService = logService;
            _settingsService = settingsService;
            _recordingDataService = recordingDataService;
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
        public void StartCapture(string? windowTitle, bool useExactMatch, double interval, string? resumeFromFilePath = null)
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
                    _logService.AddLog("首次启动，创建输出目录和录制数据服务");

                    _sharedState.OutputFolder = _settingsService.SavePath;
                    _logService.AddLog($"输出文件夹完整路径：{_sharedState.OutputFolder}");

                    if (!Directory.Exists(_sharedState.OutputFolder))
                    {
                        Directory.CreateDirectory(_sharedState.OutputFolder);
                        _logService.AddLog($"已创建输出目录：{_sharedState.OutputFolder}");
                    }

                    _logService.AddLog("初始化捕获会话");
                    var captureInitialized = _windowCaptureService.InitializeCaptureAsync(_sharedState.Hwnd).Result;
                    if (captureInitialized)
                    {
                        _logService.AddLog("捕获会话初始化成功，将使用 PrintWindow API 进行截图");
                    }

                    // 启动录制数据服务（结构化数据存储）
                    _recordingDataService.StartRecording(
                        windowTitle,
                        _sharedState.Hwnd,
                        interval,
                        _sharedState.CanvasWidth,
                        _sharedState.CanvasHeight,
                        resumeFromFilePath);

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
        /// 取消捕获且不保存录制数据
        /// </summary>
        public void CancelCapture()
        {
            _sharedState.Running = false;

            // 取消录制数据服务，删除文件
            _recordingDataService.CancelRecording();

            _sharedState.ResetCaptureState();
            Status = "捕获已取消（未保存）";
            _logService.AddLog("捕获已取消，未保存录制数据");
        }

        /// <summary>
        /// 完全停止捕获过程并保存录制数据
        /// </summary>
        public void StopCapture()
        {
            _sharedState.Running = false;

            int totalFrames = _sharedState.FrameNumber;
            int savedFrames = _sharedState.SavedCount;

            _logService.AddLog("捕获会话已完成");

            // 停止录制数据服务并保存结构化数据文件
            var recordingFilePath = _recordingDataService.StopRecording();

            if (!string.IsNullOrEmpty(recordingFilePath))
            {
                _logService.AddLog($"✓ 录制文件已保存：{recordingFilePath}", LogLevel.Warning);
                _logService.AddLog($"  总帧数：{totalFrames}, 有效帧：{savedFrames}", LogLevel.Warning);
                Status = $"捕获停止，数据已保存：{Path.GetFileName(recordingFilePath)}";
            }
            else
            {
                Status = "捕获停止";
                _logService.AddLog("捕获停止 - 未生成录制数据文件", LogLevel.Warning);
            }

            _sharedState.ResetCaptureState();
            _logService.AddLog("捕获状态已重置到初始值");
        }

        /// <summary>
        /// 捕获循环核心实现
        /// </summary>
        private void CaptureLoop()
        {
            _logService.AddLog("捕获循环开始");
            int errorCount = 0;
            Mat? image = null;

            try
            {
                while (_sharedState.Running)
                {
                    image = null;
                    try
                    {
                        // 检查窗口句柄是否有效
                        if (_sharedState.Hwnd == nint.Zero)
                        {
                            _logService.AddLog("窗口句柄无效，退出捕获循环", LogLevel.Error);
                            break;
                        }

                        image = _windowCaptureService.CaptureWindowContent(_sharedState.Hwnd);

                        if (image == null || image.Empty())
                        {
                            _logService.AddLog("捕获到空图像，跳过此帧", LogLevel.Warning);
                            image?.Dispose();
                            image = null;
                            continue;
                        }

                        // 在调用 SaveIfModified 之前检查 LastImage 是否已释放
                        // 防止在暂停/停止操作后访问已释放的对象
                        if (_sharedState.LastImage?.IsDisposed == true)
                        {
                            _logService.AddLog("LastImage 已被释放，重置为 null", LogLevel.Warning);
                            _sharedState.LastImage = null;
                        }

                        _windowCaptureService.SaveIfModified(image);

                        // 使用录制数据服务保存帧（结构化数据存储）
                        _recordingDataService.SaveFrame(image, _sharedState.FrameNumber, isKeyFrame: _sharedState.FrameNumber == 0);

                        _dispatcher?.Invoke(() =>
                        {
                            Status = $"已捕获帧 #{_sharedState.FrameNumber}";
                        });

                        if (_sharedState.FrameNumber % 1000 == 0)
                        {
                            _logService.AddLog($"捕获进度：{_sharedState.FrameNumber} 帧，已保存：{_sharedState.SavedCount} 帧");
                            _logService.AddLog($"录制数据服务帧数：{_recordingDataService.GetCurrentFrameCount()}");
                        }

                        _sharedState.FrameNumber++;
                        Thread.Sleep((int)(_sharedState.Interval * 1000));
                    }
                    catch (AccessViolationException ex)
                    {
                        errorCount++;
                        _logService.AddLog($"[严重] 访问违规错误 (总计：{errorCount}): {ex.Message}", LogLevel.Error);
                        _logService.AddLog($"[严重] 堆栈跟踪：{ex.StackTrace}", LogLevel.Error);

                        _dispatcher?.Invoke(() =>
                        {
                            Status = $"访问违规：{ex.Message}";
                        });
                        Thread.Sleep(1000);
                    }
                    catch (Exception ex)
                    {
                        errorCount++;
                        _logService.AddLog($"捕获错误 (总计：{errorCount}): {ex.Message}", LogLevel.Error);
                        _logService.AddLog($"错误详情：{ex.GetType().FullName} - {ex.StackTrace}", LogLevel.Error);

                        _dispatcher?.Invoke(() =>
                        {
                            Status = $"捕获错误：{ex.Message}";
                        });
                        Thread.Sleep(1000);
                    }
                    finally
                    {
                        // 确保每帧都被释放，防止内存泄漏
                        try
                        {
                            if (image != null && !image.IsDisposed)
                            {
                                image.Dispose();
                            }
                        }
                        catch (Exception ex)
                        {
                            _logService.AddLog($"释放帧图像失败：{ex.Message}", LogLevel.Warning);
                        }
                        image = null;
                    }
                }

                _logService.AddLog($"捕获循环结束 - 总帧数：{_sharedState.FrameNumber}, 已保存：{_sharedState.SavedCount} 帧，错误次数：{errorCount}");
            }
            catch (Exception ex)
            {
                _logService.AddLog($"捕获循环致命错误：{ex.Message}", LogLevel.Error);
                _logService.AddLog($"异常堆栈：{ex.StackTrace}", LogLevel.Error);
                _logService.AddLog($"异常类型：{ex.GetType().FullName}", LogLevel.Error);

                _dispatcher?.Invoke(() =>
                {
                    Status = $"捕获致命错误：{ex.Message}";
                });
            }
            finally
            {
                // 确保退出循环时释放最后一帧
                try
                {
                    if (image != null && !image.IsDisposed)
                    {
                        image.Dispose();
                    }
                }
                catch (Exception ex)
                {
                    _logService.AddLog($"释放最后一帧失败：{ex.Message}", LogLevel.Warning);
                }
            }
        }
    }
}
