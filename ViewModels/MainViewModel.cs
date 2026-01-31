using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sai2Capture.Services;
using System;
using System.Collections.ObjectModel;
using System.Windows;

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
        public MainViewModel(
            WindowCaptureService windowCaptureService,
            UtilityService utilityService,
            CaptureService captureService,
            SettingsService settingsService)
        {
            _windowCaptureService = windowCaptureService;
            _utilityService = utilityService;
            _captureService = captureService;
            _settingsService = settingsService;

            InitializeServices();
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
        }

        /// <summary>
        /// 开始捕获窗口内容的命令
        /// 使用 ComboBox 中显示的窗口标题（无论是选择还是输入）
        /// 启动捕获服务并传递间隔参数
        /// </summary>
        [RelayCommand]
        private void StartCapture()
        {
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
            _captureService.PauseCapture();
        }

        /// <summary>
        /// 停止捕获命令
        /// 完全停止窗口捕获过程
        /// 释放资源并完成视频文件保存
        /// </summary>
        [RelayCommand]
        private void StopCapture()
        {
            _captureService.StopCapture();
        }

        
        private string _status = "准备就绪";
        /// <summary>
        /// 预览窗口命令  
        /// 创建一个新窗口实时预览指定窗口的内容
        /// 当窗口标题未选择时显示错误状态
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
                    return;
                }

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
    }
}
