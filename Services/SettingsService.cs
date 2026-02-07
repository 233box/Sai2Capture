using CommunityToolkit.Mvvm.ComponentModel;
using Sai2Capture.Models;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;

namespace Sai2Capture.Services
{
    /// <summary>
    /// 设置管理服务
    /// 负责应用程序配置的加载、保存和管理
    /// 使用JSON格式持久化用户设置
    /// </summary>
    public partial class SettingsService : ObservableObject
    {
        private const string SettingsFileName = "settings.json";
        private readonly SharedStateService _sharedState;
        private readonly LogService _logService;

        /// <summary>
        /// 目标窗口名称
        /// 用于标识要捕获的具体窗口
        /// 默认值："导航器"
        /// </summary>
        [ObservableProperty]
        private string _windowName = "导航器";

        /// <summary>
        /// 捕获间隔时间(秒)
        /// 控制帧捕获频率，影响性能与流畅度
        /// 默认值：0.1秒(10FPS)
        /// </summary>
        [ObservableProperty]
        private double _captureInterval = 0.1;

        /// <summary>
        /// 界面缩放级别
        /// 支持值："100%", "125%", "150%", "200%"
        /// 影响窗口裁剪区域计算
        /// 默认值："125%"
        /// </summary>
        [ObservableProperty]
        private string _zoomLevel = "125%";

        /// <summary>
        /// 保存路径
        /// 用于指定捕获图像的保存位置
        /// 默认值：程序根目录下的output文件夹
        /// </summary>
        [ObservableProperty]
        private string _savePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "output");

        /// <summary>
        /// 热键配置列表
        /// 存储用户自定义的热键配置
        /// </summary>
        [ObservableProperty]
        private ObservableCollection<HotkeyModel> _hotkeys = new();

        /// <summary>
        /// SAI2程序路径
        /// 用于快速启动SAI2应用程序
        /// 默认值：空字符串（用户需要手动配置）
        /// </summary>
        [ObservableProperty]
        private string _sai2Path = "";

        /// <summary>
        /// 窗口宽度
        /// 记录主窗口的宽度
        /// 默认值：600
        /// </summary>
        [ObservableProperty]
        private double _windowWidth = 800;

        /// <summary>
        /// 窗口高度
        /// 记录主窗口的高度
        /// 默认值：800
        /// </summary>
        [ObservableProperty]
        private double _windowHeight = 600;

        /// <summary>
        /// 窗口左边距
        /// 记录主窗口的水平位置
        /// 默认值：-1（居中）
        /// </summary>
        [ObservableProperty]
        private double _windowLeft = -1;

        /// <summary>
        /// 窗口上边距
        /// 记录主窗口的垂直位置
        /// 默认值：-1（居中）
        /// </summary>
        [ObservableProperty]
        private double _windowTop = -1;

        /// <summary>
        /// 初始化设置服务
        /// </summary>
        /// <param name="sharedState">共享状态服务，用于配置同步</param>
        /// <param name="logService">日志服务</param>
        public SettingsService(SharedStateService sharedState, LogService logService)
        {
            _sharedState = sharedState;
            _logService = logService;
        }

        /// <summary>
        /// 获取设置文件的绝对路径
        /// </summary>
        /// <returns>设置文件的完整路径</returns>
        public string GetSettingsFilePath()
        {
            return Path.GetFullPath(SettingsFileName);
        }

        /// <summary>
        /// 从settings.json文件加载应用程序设置
        /// 1. 检查配置文件是否存在
        /// 2. 读取并反序列化JSON内容
        /// 3. 更新所有配置属性
        /// 4. 同步共享状态服务
        /// 5. 确保保存目录存在
        /// 6. 处理并显示可能的错误
        /// </summary>
        public void LoadSettings()
        {
            try
            {
                if (File.Exists(SettingsFileName))
                {
                    _logService.AddLog($"加载设置文件: {GetSettingsFilePath()}");
                    string json = File.ReadAllText(SettingsFileName);
                    var settings = JsonSerializer.Deserialize<SettingsModel>(json);

                    if (settings != null)
                    {
                        WindowName = settings.WindowName ?? WindowName;
                        CaptureInterval = settings.CaptureInterval > 0 ? settings.CaptureInterval : 0.1;
                        ZoomLevel = settings.ZoomLevel ?? ZoomLevel;
                        SavePath = !string.IsNullOrEmpty(settings.SavePath) ? settings.SavePath : SavePath;
                        Sai2Path = settings.Sai2Path ?? Sai2Path;
                        
                        // 加载窗口设置
                        WindowWidth = settings.WindowWidth > 0 ? settings.WindowWidth : WindowWidth;
                        WindowHeight = settings.WindowHeight > 0 ? settings.WindowHeight : WindowHeight;
                        WindowLeft = settings.WindowLeft;
                        WindowTop = settings.WindowTop;

                        // 加载热键配置
                        if (settings.Hotkeys != null && settings.Hotkeys.Any())
                        {
                            Hotkeys.Clear();
                            foreach (var savedHotkey in settings.Hotkeys)
                            {
                                Hotkeys.Add(savedHotkey);
                            }
                            _logService.AddLog($"加载了 {settings.Hotkeys.Count} 个热键配置");
                        }
                        else
                        {
                            // 使用默认热键配置
                            InitializeDefaultHotkeys();
                        }

                        // 更新共享状态
                        _sharedState.Interval = CaptureInterval;

                        // 确保保存目录存在
                        EnsureSavePathExists();

                        _logService.AddLog($"设置加载成功 - 窗口: {WindowName}, 间隔: {CaptureInterval}秒, 缩放: {ZoomLevel}, 保存路径: {SavePath}, 窗口大小: {WindowWidth}x{WindowHeight}");
                    }
                }
                else
                {
                    _logService.AddLog("设置文件不存在，使用默认设置", LogLevel.Warning);
                    InitializeDefaultHotkeys();
                    // 确保默认保存目录存在
                    EnsureSavePathExists();
                }
            }
            catch (Exception ex)
            {
                _logService.AddLog($"加载设置失败: {ex.Message}", LogLevel.Error);
                System.Windows.MessageBox.Show($"加载设置失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Warning);

                // 出现错误时初始化默认热键
                InitializeDefaultHotkeys();
                // 确保默认保存目录存在
                EnsureSavePathExists();
            }
        }

        /// <summary>
        /// 保存当前设置到settings.json文件
        /// 1. 创建SettingsModel对象
        /// 2. 序列化为JSON格式
        /// 3. 写入到配置文件
        /// 4. 确保保存目录存在
        /// 5. 处理并显示可能的错误
        /// </summary>
        public void SaveSettings()
        {
            try
            {
                // 确保保存路径目录存在
                EnsureSavePathExists();

                var settings = new SettingsModel
                {
                    WindowName = WindowName,
                    CaptureInterval = CaptureInterval,
                    ZoomLevel = ZoomLevel,
                    SavePath = SavePath,
                    Sai2Path = Sai2Path,
                    Hotkeys = Hotkeys.ToList(),
                    WindowWidth = WindowWidth,
                    WindowHeight = WindowHeight,
                    WindowLeft = WindowLeft,
                    WindowTop = WindowTop
                };

                string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsFileName, json);

                _logService.AddLog($"设置已保存到: {GetSettingsFilePath()}");
            }
            catch (Exception ex)
            {
                _logService.AddLog($"保存设置失败: {ex.Message}", LogLevel.Error);
                System.Windows.MessageBox.Show($"保存设置失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 单独保存热键配置
        /// </summary>
        public void SaveHotkeys(ObservableCollection<HotkeyModel> hotkeys)
        {
            try
            {
                Hotkeys = new ObservableCollection<HotkeyModel>(hotkeys);
                SaveSettings();
                _logService.AddLog("热键配置已保存");
            }
            catch (Exception ex)
            {
                _logService.AddLog($"保存热键配置失败: {ex.Message}", LogLevel.Error);
                throw;
            }
        }

        /// <summary>
        /// 确保保存路径目录存在
        /// 如果不存在则创建该目录
        /// </summary>
        private void EnsureSavePathExists()
        {
            try
            {
                if (!Directory.Exists(SavePath))
                {
                    Directory.CreateDirectory(SavePath);
                    _logService.AddLog($"创建保存目录: {SavePath}");
                }
            }
            catch (Exception ex)
            {
                _logService.AddLog($"创建保存目录失败: {ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>
        /// 初始化默认热键配置
        /// </summary>
        private void InitializeDefaultHotkeys()
        {
            Hotkeys.Clear();
            var defaultHotkeys = HotkeyModel.CreateDefaultHotkeys();
            foreach (var hotkey in defaultHotkeys)
            {
                Hotkeys.Add(hotkey);
            }
            _logService.AddLog("初始化默认热键配置");
        }

        /// <summary>
        /// 设置数据模型类
        /// 用于JSON序列化和反序列化
        /// 与ObservableProperty属性保持同步
        /// </summary>
        private class SettingsModel
        {
            /// <summary>
            /// 目标窗口名称
            /// </summary>
            public string? WindowName { get; set; }

            /// <summary>
            /// 捕获间隔时间(秒)
            /// </summary>
            public double CaptureInterval { get; set; }

            /// <summary>
            /// 界面缩放级别
            /// </summary>
            public string? ZoomLevel { get; set; }

            /// <summary>
            /// 保存路径
            /// </summary>
            public string? SavePath { get; set; }

            /// <summary>
            /// 热键配置列表
            /// </summary>
            public List<HotkeyModel>? Hotkeys { get; set; }

            /// <summary>
            /// SAI2程序路径
            /// </summary>
            public string? Sai2Path { get; set; }

            /// <summary>
            /// 窗口宽度
            /// </summary>
            public double WindowWidth { get; set; }

            /// <summary>
            /// 窗口高度
            /// </summary>
            public double WindowHeight { get; set; }

            /// <summary>
            /// 窗口左边距
            /// </summary>
            public double WindowLeft { get; set; }

            /// <summary>
            /// 窗口上边距
            /// </summary>
            public double WindowTop { get; set; }
        }
    }
}
