using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sai2Capture.Services;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using MessageBox = System.Windows.MessageBox;

namespace Sai2Capture.ViewModels
{
    /// <summary>
    /// 录制状态枚举
    /// </summary>
    public enum RecordingState
    {
        Stopped,    // 未录制
        Recording,  // 正在录制
        Paused      // 已暂停
    }

    /// <summary>
    /// 主窗口视图模型
    /// 协调各服务组件，处理 UI 交互逻辑
    /// </summary>
    public partial class MainViewModel : ObservableObject
    {
        private readonly WindowCaptureService _windowCaptureService;
        private readonly UtilityService _utilityService;
        private readonly CaptureService _captureService;
        private readonly SettingsService _settingsService;
        private readonly LogService _logService;
        private readonly HotkeyViewModel _hotkeyViewModel;
        private readonly RecordingManagerViewModel _recordingManagerViewModel;
        private readonly DispatcherTimer _statusTimer;
        private readonly DispatcherTimer _canvasPollingTimer;
        private System.Windows.Controls.ScrollViewer? _logScrollViewer;
        private string? _lastKnownWindowTitle;

        [ObservableProperty]
        private ObservableCollection<string> _windowTitles = new();

        [ObservableProperty]
        public string? _selectedWindowTitle = "导航器";

        partial void OnSelectedWindowTitleChanged(string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                RestartPreviewRequested?.Invoke();
            }
        }

        public event Action? RestartPreviewRequested;

        [ObservableProperty]
        public double _captureInterval = 0.1;

        [ObservableProperty]
        public string _zoomLevel = "125%";

        public MainViewModel(
            WindowCaptureService windowCaptureService,
            UtilityService utilityService,
            CaptureService captureService,
            SettingsService settingsService,
            LogService logService,
            HotkeyViewModel hotkeyViewModel,
            RecordingManagerViewModel recordingManagerViewModel)
        {
            _windowCaptureService = windowCaptureService;
            _utilityService = utilityService;
            _captureService = captureService;
            _settingsService = settingsService;
            _logService = logService;
            _hotkeyViewModel = hotkeyViewModel;
            _recordingManagerViewModel = recordingManagerViewModel;

            _logService.LogUpdated += OnLogUpdated;

            _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _statusTimer.Tick += UpdateStatus;

            _canvasPollingTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _canvasPollingTimer.Tick += (s, e) => UpdateCanvasSize();

            InitializeServices();
        }

        public HotkeyViewModel HotkeyViewModel => _hotkeyViewModel;

        public void StopCanvasPolling() => _canvasPollingTimer?.Stop();

        [ObservableProperty]
        private string _canvasSizeDisplay = "未检测到 SAI2";

        private void OnLogUpdated(object? sender, LogEventArgs e)
        {
            System.Windows.Application.Current?.Dispatcher.Invoke(UpdateLogDisplay);
        }

        private void InitializeServices()
        {
            _captureService.Initialize(System.Windows.Application.Current.Dispatcher);
            WindowTitles = new ObservableCollection<string>(_windowCaptureService.EnumSai2WindowTitles());
            _settingsService.LoadSettings();

            SelectedWindowTitle = _settingsService.WindowName;
            CaptureInterval = _settingsService.CaptureInterval;
            ZoomLevel = _settingsService.ZoomLevel;
            SavePath = _settingsService.SavePath;
            Sai2Path = _settingsService.Sai2Path;

            UpdateCanvasSize();
            _canvasPollingTimer.Start();

            AddLog("应用程序启动");
            AddLog($"设置文件路径：{_settingsService.GetSettingsFilePath()}");
            AddLog($"加载设置 - 窗口：{SelectedWindowTitle}, 间隔：{CaptureInterval}秒");
            AddLog($"保存路径：{SavePath}");
            AddLog("画布尺寸监控已启动");

            Status = "未录制";
        }

        private void UpdateCanvasSize()
        {
            try
            {
                var sai2Processes = Process.GetProcessesByName("sai2");
                if (sai2Processes.Length == 0)
                {
                    sai2Processes = Process.GetProcessesByName("sai");
                }

                if (sai2Processes.Length == 0)
                {
                    CanvasSizeDisplay = "未检测到 SAI2";
                    return;
                }

                foreach (var proc in sai2Processes)
                {
                    try
                    {
                        var title = proc.MainWindowTitle;
                        if (string.IsNullOrEmpty(title)) continue;

                        var parts = title.Split(new[] { " - " }, StringSplitOptions.None);
                        if (parts.Length < 2) continue;

                        var potentialPath = parts[parts.Length - 1].Trim();
                        if (!potentialPath.EndsWith(".sai2", StringComparison.OrdinalIgnoreCase) || !File.Exists(potentialPath))
                            continue;

                        if (Sai2FileParser.TryParseCanvasSize(potentialPath, out int width, out int height))
                        {
                            bool sizeChanged = (_captureService.SharedState.CanvasWidth != width ||
                                               _captureService.SharedState.CanvasHeight != height);

                            _captureService.SharedState.CanvasWidth = width;
                            _captureService.SharedState.CanvasHeight = height;
                            CanvasSizeDisplay = $"SAI2 画布：{width} x {height}";

                            if (sizeChanged)
                            {
                                AddLog($"SAI2 画布尺寸：{width} x {height}");
                                Status = $"SAI2 画布：{width} x {height}";
                            }

                            _lastKnownWindowTitle = title;
                            return;
                        }

                        AddLog($"SAI2 文件解析失败：{potentialPath}", "WARNING");
                        _lastKnownWindowTitle = title;
                    }
                    catch (Exception ex)
                    {
                        AddLog($"检查 SAI2 窗口失败：{ex.Message}", "WARNING");
                    }
                }

                CanvasSizeDisplay = "SAI2 已运行（未检测到画布）";
            }
            catch (Exception ex)
            {
                CanvasSizeDisplay = "未检测到 SAI2";
                AddLog($"更新画布尺寸失败：{ex.Message}", "WARNING");
            }
        }

        /// <summary>
        /// 开始捕获窗口内容的命令
        /// </summary>
        [RelayCommand]
        private void StartCapture()
        {
            AddLog($"开始捕获 - 窗口：{SelectedWindowTitle}, 间隔：{CaptureInterval}秒");

            if (!_captureService.SharedState.IsInitialized && !_captureService.SharedState.Running)
            {
                _elapsedSeconds = 0;
            }

            _statusTimer.Start();
            _canvasPollingTimer.Start();

            _captureService.StartCapture(SelectedWindowTitle, true, CaptureInterval);
        }

        /// <summary>
        /// 暂停/继续录制命令（切换）
        /// 根据当前状态决定是暂停还是继续
        /// </summary>
        [RelayCommand]
        private void TogglePause()
        {
            if (IsPaused)
            {
                AddLog("继续捕获", "WARNING");
                _captureService.StartCapture(SelectedWindowTitle, false, CaptureInterval);
                _statusTimer.Start();
            }
            else if (IsRecording)
            {
                AddLog("暂停捕获", "WARNING");
                _captureService.PauseCapture();
                _statusTimer.Stop();
            }
            else
            {
                StartCaptureCommand.Execute(null);
                return;
            }

            OnPropertyChanged(nameof(IsRecording));
            OnPropertyChanged(nameof(IsPaused));
            OnPropertyChanged(nameof(RecordButtonText));
            OnPropertyChanged(nameof(CanStop));
        }

        /// <summary>
        /// 停止捕获命令
        /// 完全停止窗口捕获过程
        /// </summary>
        [RelayCommand]
        private void StopCapture()
        {
            AddLog("停止捕获");
            _statusTimer.Stop();
            _canvasPollingTimer.Stop();
            _captureService.StopCapture();
            Status = "未录制";
        }

        /// <summary>
        /// 执行热键命令
        /// </summary>
        public void ExecuteHotkeyCommand(string hotkeyId, string commandName)
        {
            try
            {
                AddLog($"执行热键命令：{hotkeyId} -> {commandName}");

                if (commandName == "ToggleWindowTopmost")
                {
                    ToggleWindowTopmostRequested?.Invoke();
                    return;
                }

                switch (commandName)
                {
                    case "StartCaptureCommand":
                        StartCaptureCommand.Execute(null); break;
                    case "PauseCaptureCommand":
                        TogglePauseCommand.Execute(null); break;
                    case "StopCaptureCommand":
                        StopCaptureCommand.Execute(null); break;
                    case "RefreshWindowListCommand":
                        RefreshWindowListCommand.Execute(null); break;
                    case "ExportLogCommand":
                        ExportLogCommand.Execute(null); break;
                }
            }
            catch (Exception ex)
            {
                AddLog($"执行热键命令失败：{ex.Message}", "ERROR");
            }
        }

        /// <summary>
        /// 当请求切换窗口置顶时触发
        /// </summary>
        public event Action? ToggleWindowTopmostRequested;

        private int _elapsedSeconds = 0;

        private string _status = "未录制";
        /// <summary>
        /// 状态显示文本
        /// 显示当前录制状态、已经过时间和有效捕获数量
        /// </summary>
        public string Status
        {
            get => _status;
            set
            {
                if (_status != value)
                {
                    _status = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(CurrentRecordingState));
                    OnPropertyChanged(nameof(RecordButtonText));
                    OnPropertyChanged(nameof(CanStop));
                }
            }
        }

        public RecordingState CurrentRecordingState
        {
            get
            {
                var state = _captureService.SharedState;
                return state.Running ? RecordingState.Recording
                    : state.IsInitialized ? RecordingState.Paused
                    : RecordingState.Stopped;
            }
        }

        public bool IsRecording => CurrentRecordingState != RecordingState.Stopped;
        public bool IsPaused => CurrentRecordingState == RecordingState.Paused;
        public bool IsRecordingRunning => CurrentRecordingState == RecordingState.Recording;

        public string RecordButtonText => CurrentRecordingState switch
        {
            RecordingState.Paused => "▶️ 继续录制",
            RecordingState.Recording => "⏸️ 暂停录制",
            _ => "🟢 开始录制"
        };

        public bool CanStop => IsRecording;

        private void UpdateStatus(object? sender, EventArgs e)
        {
            var sharedState = _captureService.SharedState;
            if (!sharedState.Running && sharedState.FrameNumber == 0) return;

            var elapsed = TimeSpan.FromSeconds(_elapsedSeconds++ / 10.0);
            var elapsedStr = $"{(int)elapsed.TotalMinutes:D2}:{elapsed.Seconds:D2}";

            Status = sharedState.Running
                ? $"正在录制（当前已经过：{elapsedStr}，有效捕获：{sharedState.SavedCount}）"
                : $"已暂停（当前已经过：{elapsedStr}，有效捕获：{sharedState.SavedCount}）";
        }


        public CaptureService CaptureService => _captureService;

        [RelayCommand]
        private void RefreshWindowList()
        {
            var currentSelection = SelectedWindowTitle;
            WindowTitles = new ObservableCollection<string>(_windowCaptureService.EnumSai2WindowTitles());
            AddLog($"SAI2 窗口列表已刷新，共 {WindowTitles.Count} 个 SAI2 相关窗口");

            if (!string.IsNullOrEmpty(currentSelection) && WindowTitles.Contains(currentSelection))
            {
                SelectedWindowTitle = currentSelection;
            }
        }

        public void StartEmbeddedPreview(System.Windows.Controls.Image? previewImage = null)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(SelectedWindowTitle))
                {
                    Status = "请先选择或输入窗口标题";
                    AddLog("预览失败：未选择窗口标题", "WARNING");
                    return;
                }

                AddLog($"启动嵌入式预览：{SelectedWindowTitle}");
                _utilityService.StopEmbeddedPreview();
                if (previewImage != null)
                {
                    _utilityService.StartEmbeddedPreview(SelectedWindowTitle, previewImage);
                }
            }
            catch (Exception ex)
            {
                Status = $"启动嵌入式预览错误：{ex.Message}";
                AddLog($"启动嵌入式预览错误：{ex.Message}", "ERROR");
            }
        }

        public void StopEmbeddedPreview()
        {
            try
            {
                _utilityService.StopEmbeddedPreview();
                AddLog("已停止嵌入式预览");
            }
            catch (Exception ex)
            {
                AddLog($"停止嵌入式预览错误：{ex.Message}", "ERROR");
            }
        }

        public void OnWindowClosing()
        {
            AddLog("保存设置并关闭应用程序");
            _settingsService.WindowName = SelectedWindowTitle ?? "导航器";
            _settingsService.CaptureInterval = CaptureInterval;
            _settingsService.ZoomLevel = ZoomLevel;
            _settingsService.SavePath = SavePath;
            _settingsService.Sai2Path = Sai2Path;
            _settingsService.SaveSettings();

            _captureService.StopCapture();
            StopEmbeddedPreview();
        }

        [ObservableProperty]
        private string _savePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "output");

        [ObservableProperty]
        private string _sai2Path = "";

        [ObservableProperty]
        private string _logContent = string.Empty;

        [ObservableProperty]
        private string _logStatistics = "日志行数：0";

        [ObservableProperty]
        private bool _autoScrollLog = true;

        [ObservableProperty]
        private string _logFilterLevel = "全部";

        partial void OnLogFilterLevelChanged(string value)
        {
            UpdateLogDisplay();
        }

        private void UpdateLogDisplay()
        {
            LogLevel? filterLevel = LogFilterLevel switch
            {
                "INFO" => LogLevel.Info,
                "WARNING" => LogLevel.Warning,
                "ERROR" => LogLevel.Error,
                _ => null
            };

            LogContent = _logService.GetFullLog(filterLevel);
            var count = _logService.GetLineCount(filterLevel);
            var totalCount = _logService.GetLineCount();

            LogStatistics = filterLevel.HasValue
                ? $"显示：{count} / 总计：{totalCount} / 1000 | 过滤：{LogFilterLevel}"
                : $"日志行数：{totalCount} / 1000 | 最后更新：{DateTime.Now:yyyy-MM-dd HH:mm:ss}";

            if (AutoScrollLog && _logScrollViewer != null)
            {
                try { _logScrollViewer.ScrollToEnd(); } catch { }
            }
        }

        public void SetLogScrollViewer(System.Windows.Controls.ScrollViewer scrollViewer)
        {
            _logScrollViewer = scrollViewer;
        }

        /// <summary>
        /// 浏览保存路径命令
        /// </summary>
        [RelayCommand]
        private void BrowseSavePath()
        {
            var dialog = new FolderBrowserDialog();
            if (dialog.ShowDialog() == DialogResult.OK)
            {
                SavePath = dialog.SelectedPath;
            }
        }

        /// <summary>
        /// 浏览 SAI2 程序路径命令
        /// </summary>
        [RelayCommand]
        private void BrowseSai2Path()
        {
            var dialog = new OpenFileDialog();
            dialog.Title = "选择 SAI2 可执行文件";
            dialog.Filter = "可执行文件 (*.exe)|*.exe|所有文件 (*.*)|*.*";
            dialog.FileName = "sai2.exe";

            if (dialog.ShowDialog() == DialogResult.OK)
            {
                Sai2Path = dialog.FileName;
                AddLog($"已选择 SAI2 程序路径：{Sai2Path}");
            }
        }

        /// <summary>
        /// 启动 SAI2 命令
        /// </summary>
        [RelayCommand]
        private void LaunchSai2()
        {
            try
            {
                if (string.IsNullOrEmpty(Sai2Path))
                {
                    System.Windows.MessageBox.Show("请先配置 SAI2 程序路径", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (!File.Exists(Sai2Path))
                {
                    System.Windows.MessageBox.Show($"SAI2 程序文件不存在：{Sai2Path}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // 启动 SAI2 应用程序
                var processStartInfo = new ProcessStartInfo
                {
                    FileName = Sai2Path,
                    UseShellExecute = true
                };

                Process.Start(processStartInfo);
                AddLog($"SAI2 已启动：{Sai2Path}");
            }
            catch (Exception ex)
            {
                var errorMessage = $"启动 SAI2 失败：{ex.Message}";
                AddLog(errorMessage, "ERROR");
                System.Windows.MessageBox.Show(errorMessage, "启动失败", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 添加日志消息
        /// </summary>
        /// <param name="message">日志消息</param>
        /// <param name="level">日志级别（INFO, WARNING, ERROR）</param>
        public void AddLog(string message, string level = "INFO")
        {
            var logLevel = level.ToUpper() switch
            {
                "WARNING" => LogLevel.Warning,
                "ERROR" => LogLevel.Error,
                _ => LogLevel.Info
            };
            _logService.AddLog(message, logLevel);
        }

        /// <summary>
        /// 清空日志命令
        /// </summary>
        [RelayCommand]
        private void ClearLog()
        {
            _logService.ClearLog();
            AddLog("日志已清空");
        }

        /// <summary>
        /// 导出日志命令
        /// </summary>
        [RelayCommand]
        private void ExportLog()
        {
            try
            {
                // 使用固定的日志导出目录，而不是用户设置的保存路径
                var logExportPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");

                // 确保日志目录存在
                if (!Directory.Exists(logExportPath))
                {
                    Directory.CreateDirectory(logExportPath);
                }

                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var fileName = $"Sai2Capture_Log_{timestamp}.txt";
                var filePath = Path.Combine(logExportPath, fileName);

                File.WriteAllText(filePath, LogContent);
                AddLog($"日志已导出到：{filePath}");
                Status = $"日志已导出：{fileName}";
            }
            catch (Exception ex)
            {
                AddLog($"导出日志失败：{ex.Message}", "ERROR");
                Status = $"导出日志失败：{ex.Message}";
            }
        }
    }
}
