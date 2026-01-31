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
        private readonly SharedStateService _sharedState;
        private readonly WindowCaptureService _windowCaptureService;
        private readonly UtilityService _utilityService;
        private readonly CaptureService _captureService;
        private readonly VideoCreatorService _videoCreatorService;
        private readonly SettingsService _settingsService;

        /// <summary>
        /// 可用窗口标题列表
        /// 通过EnumWindowTitles获取的系统窗口集合
        /// </summary>
        [ObservableProperty]
        private ObservableCollection<string> _windowTitles = new();

        /// <summary>
        /// 当前选中的窗口标题
        /// 当UseComboBox=true时使用的目标窗口标题
        /// 默认值："从列表选择或手动输入"
        /// </summary>
        [ObservableProperty]
        public string? _selectedWindowTitle = null;

        /// <summary>
        /// 手动输入的窗口名称  
        /// 当UseComboBox=false时使用的目标窗口名称
        /// 默认值："导航器"
        /// 与设置服务同步
        /// </summary>
        [ObservableProperty]
        public string _windowName = "导航器";

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
        /// 生成视频时长(秒)
        /// 控制最终视频的长度
        /// 必须大于0
        /// 默认值：10秒
        /// </summary>
        [ObservableProperty]
        public double _videoDuration = 10;

        /// <summary>
        /// 是否使用下拉框选择窗口
        /// true: 使用ComboBox选择窗口
        /// false: 使用文本框手动输入
        /// 默认值：true
        /// </summary>
        [ObservableProperty]
        public bool _useComboBox = true;

        /// <summary>
        /// 初始化主视图模型
        /// </summary>
        /// <param name="sharedState">共享状态服务</param>
        /// <param name="windowCaptureService">窗口捕获服务</param>
        /// <param name="utilityService">实用工具服务</param>
        /// <param name="captureService">捕获服务</param>
        /// <param name="videoCreatorService">视频创建服务</param>
        /// <param name="settingsService">设置服务</param>
        public MainViewModel(
            SharedStateService sharedState,
            WindowCaptureService windowCaptureService,
            UtilityService utilityService,
            CaptureService captureService,
            VideoCreatorService videoCreatorService,
            SettingsService settingsService)
        {
            _sharedState = sharedState;
            _windowCaptureService = windowCaptureService;
            _utilityService = utilityService;
            _captureService = captureService;
            _videoCreatorService = videoCreatorService;
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
            WindowName = _settingsService.WindowName;
            CaptureInterval = _settingsService.CaptureInterval;
            ZoomLevel = _settingsService.ZoomLevel;
            VideoDuration = _settingsService.VideoDuration;
            UseComboBox = _settingsService.UseComboBox;
            SavePath = _settingsService.SavePath;
        }

        /// <summary>
        /// 开始捕获窗口内容的命令
        /// 根据UI选择自动确定窗口标题来源：
        /// - UserComboBox=true时使用下拉框选中的标题
        /// - UserComboBox=false时使用手动输入的窗口名
        /// 启动捕获服务并传递间隔参数
        /// </summary>
        [RelayCommand]
        private void StartCapture()
        {
            string? windowTitle = UseComboBox ? SelectedWindowTitle : WindowName;
            _captureService.StartCapture(windowTitle, UseComboBox, CaptureInterval);
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

        /// <summary>
        /// 生成视频命令
        /// 调用视频创建服务，提示用户选择输出文件夹
        /// 根据设定的视频时长创建最终视频文件
        /// </summary>
        [RelayCommand]
        private void CreateVideo()
        {
            _videoCreatorService.SelectFolderAndCreateVideo(VideoDuration);
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
                string? windowTitle = UseComboBox ? SelectedWindowTitle : WindowName;
                if (string.IsNullOrWhiteSpace(windowTitle))
                {
                    Status = "请先选择窗口标题";
                    return;
                }

                var previewWindow = new System.Windows.Window
                {
                    Title = "窗口预览 - " + windowTitle,
                    Width = 640,
                    Height = 480,
                    Content = new System.Windows.Controls.Image()
                };

                _utilityService.StartPreview(windowTitle, previewWindow);
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
            _settingsService.WindowName = WindowName;
            _settingsService.CaptureInterval = CaptureInterval;
            _settingsService.ZoomLevel = ZoomLevel;
            _settingsService.VideoDuration = VideoDuration;
            _settingsService.UseComboBox = UseComboBox;
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
