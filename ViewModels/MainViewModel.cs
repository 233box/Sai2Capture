using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sai2Capture.Services;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.IO;
using System.Diagnostics;
using System.Windows.Forms;

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
        private readonly System.Windows.Threading.DispatcherTimer _statusTimer;
        private readonly System.Windows.Threading.DispatcherTimer _canvasPollingTimer;
        private System.Windows.Controls.ScrollViewer? _logScrollViewer;
        private string? _lastKnownWindowTitle;

        /// <summary>
        /// 可用窗口标题列表
        /// 通过 EnumWindowTitles 获取的系统窗口集合
        /// </summary>
        [ObservableProperty]
        private ObservableCollection<string> _windowTitles = new();

        /// <summary>
        /// 当前选中或输入的窗口标题
        /// 支持从下拉列表选择或手动输入
        /// 默认值："导航器"
        /// </summary>
        [ObservableProperty]
        public string? _selectedWindowTitle = "导航器";

        partial void OnSelectedWindowTitleChanged(string? value)
        {
            // 当窗口标题变更时，重新启动预览
            if (!string.IsNullOrWhiteSpace(value))
            {
                // 通知 MainPage 重启预览
                RestartPreviewRequested?.Invoke();
            }
        }

        /// <summary>
        /// 当需要重启预览时触发的事件
        /// </summary>
        public event Action? RestartPreviewRequested;

        /// <summary>
        /// 帧捕获间隔时间 (秒)
        /// 控制两次捕获之间的等待时间
        /// 默认值：0.1 秒 (10FPS)
        /// </summary>
        [ObservableProperty]
        public double _captureInterval = 0.1;

        /// <summary>
        /// 界面缩放级别
        /// 支持值："100%""125%""150%""200%"
        /// 默认值："125%"
        /// </summary>
        [ObservableProperty]
        public string _zoomLevel = "125%";




        /// <summary>
        /// 初始化主视图模型
        /// </summary>
        /// <param name="windowCaptureService">窗口捕获服务</param>
        /// <param name="utilityService">实用工具服务</param>
        /// <param name="captureService">捕获服务</param>
        /// <param name="settingsService">设置服务</param>
        /// <param name="logService">日志服务</param>
        /// <param name="hotkeyViewModel">热键视图模型</param>
        public MainViewModel(
            WindowCaptureService windowCaptureService,
            UtilityService utilityService,
            CaptureService captureService,
            SettingsService settingsService,
            LogService logService,
            HotkeyViewModel hotkeyViewModel)
        {
            _windowCaptureService = windowCaptureService;
            _utilityService = utilityService;
            _captureService = captureService;
            _settingsService = settingsService;
            _logService = logService;
            _hotkeyViewModel = hotkeyViewModel;

            // 订阅日志更新事件
            _logService.LogUpdated += OnLogUpdated;

            // 初始化状态更新定时器
            _statusTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(100)
            };
            _statusTimer.Tick += UpdateStatus;

            // 初始化画布尺寸轮询定时器（每 2 秒检查一次）
            _canvasPollingTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2)
            };
            _canvasPollingTimer.Tick += CanvasPollingTimer_Tick;

            InitializeServices();
        }

        /// <summary>
        /// 画布尺寸轮询定时器回调
        /// </summary>
        private void CanvasPollingTimer_Tick(object? sender, EventArgs e)
        {
            UpdateCanvasSize();
        }

        /// <summary>
        /// 热键视图模型
        /// </summary>
        public HotkeyViewModel HotkeyViewModel => _hotkeyViewModel;

        /// <summary>
        /// 停止画布尺寸轮询
        /// 在窗口关闭时调用
        /// </summary>
        public void StopCanvasPolling()
        {
            _canvasPollingTimer?.Stop();
        }

        /// <summary>
        /// SAI2 画布尺寸显示文本
        /// </summary>
        [ObservableProperty]
        private string _canvasSizeDisplay = "未检测到 SAI2";

        /// <summary>
        /// 日志更新事件处理
        /// </summary>
        private void OnLogUpdated(object? sender, LogEventArgs e)
        {
            // 确保在 UI 线程更新显示
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                UpdateLogDisplay();
            });
        }

        /// <summary>
        /// 初始化各服务组件
        /// 1. 初始化捕获服务的 Dispatcher
        /// 2. 枚举 SAI2 相关窗口标题列表
        /// 3. 加载并应用用户设置
        /// 4. 获取 SAI2 画布尺寸
        /// </summary>
        private void InitializeServices()
        {
            _captureService.Initialize(System.Windows.Application.Current.Dispatcher);
            WindowTitles = new ObservableCollection<string>(_windowCaptureService.EnumSai2WindowTitles());
            _settingsService.LoadSettings();

            // 同步设置
            SelectedWindowTitle = _settingsService.WindowName;
            CaptureInterval = _settingsService.CaptureInterval;
            ZoomLevel = _settingsService.ZoomLevel;
            SavePath = _settingsService.SavePath;
            Sai2Path = _settingsService.Sai2Path;

            // 获取 SAI2 画布尺寸
            UpdateCanvasSize();

            // 启动画布尺寸轮询（每 2 秒检查一次）
            _canvasPollingTimer.Start();

            // 初始化日志
            AddLog("应用程序启动");
            AddLog($"设置文件路径：{_settingsService.GetSettingsFilePath()}");
            AddLog($"加载设置 - 窗口：{SelectedWindowTitle}, 间隔：{CaptureInterval}秒");
            AddLog($"保存路径：{SavePath}");
            AddLog("画布尺寸监控已启动");

            // 初始化状态
            Status = "未录制";
        }

        /// <summary>
        /// 更新 SAI2 画布尺寸
        /// 从窗口标题获取 .sai2 文件路径并解析
        /// 只在窗口标题变化时才重新解析
        /// </summary>
        private void UpdateCanvasSize()
        {
            try
            {
                var sai2Processes = System.Diagnostics.Process.GetProcessesByName("sai2");
                if (sai2Processes.Length == 0)
                {
                    sai2Processes = System.Diagnostics.Process.GetProcessesByName("sai");
                }

                if (sai2Processes.Length == 0)
                {
                    // 未找到 SAI2 进程
                    CanvasSizeDisplay = "未检测到 SAI2";
                    return;
                }

                bool foundCanvas = false;

                foreach (var proc in sai2Processes)
                {
                    try
                    {
                        var title = proc.MainWindowTitle;
                        
                        if (!string.IsNullOrEmpty(title))
                        {
                            // 从标题中提取 .sai2 文件路径
                            var parts = title.Split(new[] { " - " }, StringSplitOptions.None);
                            
                            if (parts.Length >= 2)
                            {
                                var potentialPath = parts[parts.Length - 1].Trim();
                                
                                if (potentialPath.EndsWith(".sai2", StringComparison.OrdinalIgnoreCase) && File.Exists(potentialPath))
                                {
                                    // 检查窗口标题是否变化（已检测到画布的情况下）
                                    if (foundCanvas && _lastKnownWindowTitle == title)
                                    {
                                        continue;
                                    }

                                    if (Sai2FileParser.TryParseCanvasSize(potentialPath, out int width, out int height))
                                    {
                                        // 检查尺寸是否变化
                                        bool sizeChanged = (_captureService.SharedState.CanvasWidth != width || 
                                                           _captureService.SharedState.CanvasHeight != height);
                                        
                                        _captureService.SharedState.CanvasWidth = width;
                                        _captureService.SharedState.CanvasHeight = height;
                                        CanvasSizeDisplay = $"SAI2 画布：{width} x {height}";
                                        
                                        // 只在尺寸变化时输出日志
                                        if (sizeChanged)
                                        {
                                            AddLog($"SAI2 画布尺寸：{width} x {height}");
                                            Status = $"SAI2 画布：{width} x {height}";
                                        }
                                        
                                        _lastKnownWindowTitle = title;
                                        foundCanvas = true;
                                        return;
                                    }
                                    else
                                    {
                                        AddLog($"SAI2 文件解析失败：{potentialPath}", "WARNING");
                                    }
                                }
                            }

                            // 更新最后已知窗口标题
                            _lastKnownWindowTitle = title;
                        }
                    }
                    catch (Exception ex)
                    {
                        AddLog($"检查 SAI2 窗口失败：{ex.Message}", "WARNING");
                    }
                }

                // 找到 SAI2 但无法解析画布尺寸
                if (!foundCanvas)
                {
                    CanvasSizeDisplay = "SAI2 已运行（未检测到画布）";
                }
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

                switch (commandName)
                {
                    case "StartCaptureCommand":
                        StartCaptureCommand.Execute(null);
                        break;
                    case "PauseCaptureCommand":
                        TogglePauseCommand.Execute(null);
                        break;
                    case "StopCaptureCommand":
                        StopCaptureCommand.Execute(null);
                        break;
                    case "RefreshWindowListCommand":
                        RefreshWindowListCommand.Execute(null);
                        break;
                    case "ExportLogCommand":
                        ExportLogCommand.Execute(null);
                        break;
                    default:
                        // 特殊处理一些没有直接命令的热键
                        HandleSpecialHotkey(hotkeyId);
                        break;
                }
            }
            catch (Exception ex)
            {
                AddLog($"执行热键命令失败：{ex.Message}", "ERROR");
            }
        }

        /// <summary>
        /// 处理特殊热键
        /// </summary>
        private void HandleSpecialHotkey(string hotkeyId)
        {
            switch (hotkeyId)
            {
                case "toggle_window_topmost":
                    // 切换窗口置顶状态
                    // 这个需要在 MainWindow 中处理
                    ToggleWindowTopmost();
                    break;
            }
        }

        /// <summary>
        /// 切换窗口置顶状态
        /// </summary>
        private void ToggleWindowTopmost()
        {
            try
            {
                // 通过 Application.Current.MainWindow 访问主窗口
                if (System.Windows.Application.Current.MainWindow != null)
                {
                    var mainWindow = System.Windows.Application.Current.MainWindow;
                    mainWindow.Topmost = !mainWindow.Topmost;

                    // 手动更新置顶按钮状态以同步按钮样式
                    Sai2Capture.Styles.WindowTemplateHelper.UpdateWindowTopmostState(mainWindow);

                    AddLog($"窗口置顶状态已切换为：{mainWindow.Topmost}");
                    Status = $"窗口已{ (mainWindow.Topmost ? "置顶" : "取消置顶") }";
                }
            }
            catch (Exception ex)
            {
                AddLog($"切换窗口置顶状态失败：{ex.Message}", "ERROR");
            }
        }

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

        /// <summary>
        /// 当前录制状态
        /// </summary>
        public RecordingState CurrentRecordingState
        {
            get
            {
                if (_captureService.SharedState.Running)
                    return RecordingState.Recording;
                if (_captureService.SharedState.IsInitialized)
                    return RecordingState.Paused;
                return RecordingState.Stopped;
            }
        }

        /// <summary>
        /// 是否正在录制（包括录制中和暂停状态）
        /// </summary>
        public bool IsRecording => CurrentRecordingState != RecordingState.Stopped;

        /// <summary>
        /// 是否处于暂停状态
        /// </summary>
        public bool IsPaused => CurrentRecordingState == RecordingState.Paused;

        /// <summary>
        /// 是否正在录制中（非暂停状态）
        /// </summary>
        public bool IsRecordingRunning => CurrentRecordingState == RecordingState.Recording;

        /// <summary>
        /// 主录制按钮的显示文本
        /// </summary>
        public string RecordButtonText =>
            CurrentRecordingState switch
            {
                RecordingState.Paused => "▶️ 继续录制",
                RecordingState.Recording => "⏸️ 暂停录制",
                _ => "🟢 开始录制"
            };

        /// <summary>
        /// 停止按钮是否可用
        /// </summary>
        public bool CanStop => IsRecording;

        /// <summary>
        /// 更新状态显示
        /// 根据捕获服务的运行状态显示不同的信息
        /// 只在录制时计数器递增
        /// </summary>
        private void UpdateStatus(object? sender, EventArgs e)
        {
            var sharedState = _captureService.SharedState;

            if (sharedState.Running)
            {
                _elapsedSeconds++;
                var minutes = _elapsedSeconds / 10 / 60;
                var seconds = (_elapsedSeconds / 10) % 60;
                var elapsedStr = $"{minutes:D2}:{seconds:D2}";
                Status = $"正在录制（当前已经过：{elapsedStr}，有效捕获：{sharedState.SavedCount}）";
            }
            else if (sharedState.FrameNumber > 0)
            {
                var minutes = _elapsedSeconds / 10 / 60;
                var seconds = (_elapsedSeconds / 10) % 60;
                var elapsedStr = $"{minutes:D2}:{seconds:D2}";
                Status = $"已暂停（当前已经过：{elapsedStr}，有效捕获：{sharedState.SavedCount}）";
            }
        }


        /// <summary>
        /// 获取捕获服务实例
        /// 用于 UI 绑定的只读属性
        /// 提供对捕获进度和状态的访问
        /// </summary>
        public CaptureService CaptureService => _captureService;

        /// <summary>
        /// 刷新窗口列表命令
        /// 重新扫描系统进程，查找 SAI2 相关的窗口标题
        /// </summary>
        [RelayCommand]
        private void RefreshWindowList()
        {
            var currentSelection = SelectedWindowTitle;
            WindowTitles = new ObservableCollection<string>(_windowCaptureService.EnumSai2WindowTitles());
            AddLog($"SAI2 窗口列表已刷新，共 {WindowTitles.Count} 个 SAI2 相关窗口");

            // 如果之前的选择仍然存在，保持选择
            if (!string.IsNullOrEmpty(currentSelection) && WindowTitles.Contains(currentSelection))
            {
                SelectedWindowTitle = currentSelection;
            }
        }

        /// <summary>
        /// 启动嵌入式预览
        /// 在主界面左侧开始显示指定窗口的实时预览
        /// </summary>
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

                // 停止之前的预览（如果有）
                _utilityService.StopEmbeddedPreview();

                // 只有在提供了 Image 控件时才启动预览
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

        /// <summary>
        /// 停止嵌入式预览
        /// </summary>
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

        /// <summary>
        /// 窗口关闭前处理
        /// 1. 保存当前所有设置值
        /// 2. 确保停止所有捕获操作
        /// 3. 停止嵌入式预览
        /// 4. 保留应用状态的完整性
        /// </summary>
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

            // 停止嵌入式预览
            StopEmbeddedPreview();
        }

        /// <summary>
        /// 获取或设置保存路径
        /// 默认为程序根目录下的 output 文件夹
        /// </summary>
        [ObservableProperty]
        private string _savePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "output");

        /// <summary>
        /// 获取或设置 SAI2 程序路径
        /// 用于快速启动 SAI2 应用程序
        /// </summary>
        [ObservableProperty]
        private string _sai2Path = "";

        /// <summary>
        /// 日志内容
        /// </summary>
        [ObservableProperty]
        private string _logContent = string.Empty;

        /// <summary>
        /// 日志统计信息
        /// </summary>
        [ObservableProperty]
        private string _logStatistics = "日志行数：0";

        /// <summary>
        /// 自动滚动日志
        /// </summary>
        [ObservableProperty]
        private bool _autoScrollLog = true;

        /// <summary>
        /// 日志过滤级别
        /// </summary>
        [ObservableProperty]
        private string _logFilterLevel = "全部";

        /// <summary>
        /// 当日志过滤级别改变时更新显示
        /// </summary>
        partial void OnLogFilterLevelChanged(string value)
        {
            UpdateLogDisplay();
        }

        /// <summary>
        /// 更新日志显示（应用过滤）
        /// </summary>
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

            if (filterLevel.HasValue)
            {
                LogStatistics = $"显示：{count} / 总计：{totalCount} / 1000 | 过滤：{LogFilterLevel}";
            }
            else
            {
                LogStatistics = $"日志行数：{totalCount} / 1000 | 最后更新：{DateTime.Now:yyyy-MM-dd HH:mm:ss}";
            }

            // 过滤后也自动滚动到底部（确保在 UI 线程执行）
            if (AutoScrollLog && _logScrollViewer != null)
            {
                try
                {
                    _logScrollViewer.ScrollToEnd();
                }
                catch
                {
                    // 忽略滚动错误
                }
            }
        }

        /// <summary>
        /// 设置日志滚动视图引用
        /// </summary>
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
