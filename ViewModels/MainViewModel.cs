using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sai2Capture.Services;
using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.IO;

namespace Sai2Capture.ViewModels
{
    /// <summary>
    /// 主窗口视图模型
    /// 协调各服务组件，处理UI交互逻辑
    /// 包含以下核心功能：
    /// 1. 窗口捕获控制
    /// 2. 视频生成管理
    /// 3. 用户设置持久化
    /// 4. 状态信息维护
    /// </summary>
    public partial class MainViewModel : ObservableObject
    {
        private readonly WindowCaptureService _windowCaptureService;
        private readonly UtilityService _utilityService;
        private readonly CaptureService _captureService;
        private readonly SettingsService _settingsService;
        private readonly LogService _logService;
        private readonly System.Windows.Threading.DispatcherTimer _statusTimer;

        /// <summary>
        /// 可用窗口标题列表
        /// 通过EnumWindowTitles获取的系统窗口集合
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

        /// <summary>
        /// 帧捕获间隔时间(秒)
        /// 控制两次捕获之间的等待时间
        /// 默认值：0.1秒(10FPS)
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
        public MainViewModel(
            WindowCaptureService windowCaptureService,
            UtilityService utilityService,
            CaptureService captureService,
            SettingsService settingsService,
            LogService logService)
        {
            _windowCaptureService = windowCaptureService;
            _utilityService = utilityService;
            _captureService = captureService;
            _settingsService = settingsService;
            _logService = logService;

            // 订阅日志更新事件
            _logService.LogUpdated += OnLogUpdated;

            // 初始化状态更新定时器
            _statusTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(100)
            };
            _statusTimer.Tick += UpdateStatus;

            InitializeServices();
        }

        /// <summary>
        /// 日志更新事件处理
        /// </summary>
        private void OnLogUpdated(object? sender, LogEventArgs e)
        {
            LogContent = e.FullLog;
            LogStatistics = $"日志行数: {e.LineCount} | 最后更新: {e.LastUpdate}";
        }

        /// <summary>
        /// 初始化各服务组件
        /// 1. 初始化捕获服务的Dispatcher
        /// 2. 枚举可用窗口标题列表
        /// 3. 加载并应用用户设置
        /// </summary>
        private void InitializeServices()
        {
            _captureService.Initialize(System.Windows.Application.Current.Dispatcher);
            WindowTitles = new ObservableCollection<string>(_windowCaptureService.EnumWindowTitles());
            _settingsService.LoadSettings();

            // 同步设置
            SelectedWindowTitle = _settingsService.WindowName;
            CaptureInterval = _settingsService.CaptureInterval;
            ZoomLevel = _settingsService.ZoomLevel;
            SavePath = _settingsService.SavePath;

            // 初始化日志
            AddLog("应用程序启动");
            AddLog($"设置文件路径: {_settingsService.GetSettingsFilePath()}");
            AddLog($"加载设置 - 窗口: {SelectedWindowTitle}, 间隔: {CaptureInterval}秒");
            AddLog($"保存路径: {SavePath}");
            
            // 初始化状态
            Status = "未录制";
        }

        /// <summary>
        /// 开始捕获窗口内容的命令
        /// 使用 ComboBox 中显示的窗口标题（无论是选择还是输入）
        /// 启动捕获服务并传递间隔参数
        /// </summary>
        [RelayCommand]
        private void StartCapture()
        {
            AddLog($"开始捕获 - 窗口: {SelectedWindowTitle}, 间隔: {CaptureInterval}秒");
            _captureStartTime = DateTime.Now;
            _statusTimer.Start();
            _captureService.StartCapture(SelectedWindowTitle, true, CaptureInterval);
        }

        /// <summary>
        /// 暂停捕获命令
        /// 暂停当前正在进行的窗口捕获过程
        /// 保持捕获状态但不继续捕获新帧
        /// </summary>
        [RelayCommand]
        private void PauseCapture()
        {
            AddLog("暂停捕获", "WARNING");
            _captureService.PauseCapture();
            _statusTimer.Stop();
        }

        /// <summary>
        /// 停止捕获命令
        /// 完全停止窗口捕获过程
        /// 释放资源并完成视频文件保存
        /// </summary>
        /// <summary>
        /// 停止捕获命令
        /// 完全停止窗口捕获过程
        /// 释放资源并完成视频文件保存
        /// 重置所有状态到初始值
        /// </summary>
        [RelayCommand]
        private void StopCapture()
        {
            AddLog("停止捕获");
            _statusTimer.Stop();
            _captureService.StopCapture();
            _captureStartTime = DateTime.Now; // 重置开始时间
            Status = "未录制";
        }

        private DateTime _captureStartTime;
        
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
                }
            }
        }

        /// <summary>
        /// 更新状态显示
        /// 根据捕获服务的运行状态显示不同的信息
        /// </summary>
        private void UpdateStatus(object? sender, EventArgs e)
        {
            var sharedState = _captureService.SharedState;
            var elapsed = DateTime.Now - _captureStartTime;
            var elapsedStr = $"{(int)elapsed.TotalMinutes:D2}:{elapsed.Seconds:D2}";

            if (sharedState.Running)
            {
                Status = $"正在录制（当前已经过：{elapsedStr}，有效捕获：{sharedState.SavedCount}）";
            }
            else if (sharedState.FrameNumber > 0)
            {
                Status = $"已暂停（当前已经过：{elapsedStr}，有效捕获：{sharedState.SavedCount}）";
            }
        }


        /// <summary>
        /// 获取捕获服务实例
        /// 用于UI绑定的只读属性
        /// 提供对捕获进度和状态的访问
        /// </summary>
        public CaptureService CaptureService => _captureService;

        /// <summary>
        /// 预览窗口命令
        /// 创建一个新窗口实时预览指定窗口的内容
        /// 当窗口标题未选择时显示错误状态
        /// param name="windowTitle">待预览的窗口标题</param>
        /// exception cref="Exception">捕获并显示预览过程中的错误</exception>
        /// </summary>
        [RelayCommand]
        private void PreviewWindow()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(SelectedWindowTitle))
                {
                    Status = "请先选择或输入窗口标题";
                    AddLog("预览失败: 未选择窗口标题", "WARNING");
                    return;
                }

                AddLog($"打开预览窗口: {SelectedWindowTitle}");
                var previewWindow = new System.Windows.Window
                {
                    Title = "窗口预览 - " + SelectedWindowTitle,
                    Width = 640,
                    Height = 480,
                    Content = new System.Windows.Controls.Image()
                };

                _utilityService.StartPreview(SelectedWindowTitle, previewWindow);
                previewWindow.Show();
            }
            catch (Exception ex)
            {
                Status = $"预览窗口错误: {ex.Message}";
                AddLog($"预览窗口错误: {ex.Message}", "ERROR");
            }
        }

        /// <summary>
        /// 窗口关闭前处理
        /// 1. 保存当前所有设置值
        /// 2. 确保停止所有捕获操作
        /// 3. 保留应用状态的完整性
        /// </summary>
        public void OnWindowClosing()
        {
            AddLog("保存设置并关闭应用程序");
            _settingsService.WindowName = SelectedWindowTitle ?? "导航器";
            _settingsService.CaptureInterval = CaptureInterval;
            _settingsService.ZoomLevel = ZoomLevel;
            _settingsService.SavePath = SavePath;
            _settingsService.SaveSettings();

            _captureService.StopCapture();
        }

        /// <summary>
        /// 获取或设置保存路径
        /// </summary>
        [ObservableProperty]
        private string _savePath = string.Empty;

        /// <summary>
        /// 日志内容
        /// </summary>
        [ObservableProperty]
        private string _logContent = string.Empty;

        /// <summary>
        /// 日志统计信息
        /// </summary>
        [ObservableProperty]
        private string _logStatistics = "日志行数: 0";

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
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var fileName = $"Sai2Capture_Log_{timestamp}.txt";
                var filePath = Path.Combine(SavePath ?? Environment.GetFolderPath(Environment.SpecialFolder.Desktop), fileName);
                
                File.WriteAllText(filePath, LogContent);
                AddLog($"日志已导出到: {filePath}");
                Status = $"日志已导出: {fileName}";
            }
            catch (Exception ex)
            {
                AddLog($"导出日志失败: {ex.Message}", "ERROR");
                Status = $"导出日志失败: {ex.Message}";
            }
        }
    }
}
