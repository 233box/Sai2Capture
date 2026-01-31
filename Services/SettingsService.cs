using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.IO;
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
        /// 生成视频时长(秒)
        /// 控制从图像序列创建视频的总长度
        /// 必须大于0
        /// 默认值：10秒
        /// </summary>
        [ObservableProperty]
        private double _videoDuration = 10;

        /// <summary>
        /// 是否使用下拉框选择窗口
        /// true: 使用ComboBox选择已枚举窗口
        /// false: 使用TextBox手动输入窗口标题
        /// 默认值：true
        /// </summary>
        [ObservableProperty]
        private bool _useComboBox = true;

        /// <summary>
        /// 初始化设置服务
        /// </summary>
        /// <param name="sharedState">共享状态服务，用于配置同步</param>
        public SettingsService(SharedStateService sharedState)
        {
            _sharedState = sharedState;
        }

        /// <summary>
        /// 从settings.json文件加载应用程序设置
        /// 1. 检查配置文件是否存在
        /// 2. 读取并反序列化JSON内容
        /// 3. 更新所有配置属性
        /// 4. 同步共享状态服务
        /// 5. 处理并显示可能的错误
        /// </summary>
        public void LoadSettings()
        {
            try
            {
                if (File.Exists(SettingsFileName))
                {
                    string json = File.ReadAllText(SettingsFileName);
                    var settings = JsonSerializer.Deserialize<SettingsModel>(json);

                    if (settings != null)
                    {
                        WindowName = settings.WindowName ?? WindowName;
                        CaptureInterval = settings.CaptureInterval;
                        ZoomLevel = settings.ZoomLevel ?? ZoomLevel;
                        VideoDuration = settings.VideoDuration;
                        UseComboBox = settings.UseComboBox;

                        // 更新共享状态
                        _sharedState.Interval = CaptureInterval;
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"加载设置失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        /// <summary>
        /// 保存当前设置到settings.json文件
        /// 1. 创建SettingsModel对象
        /// 2. 序列化为JSON格式
        /// 3. 写入到配置文件
        /// 4. 处理并显示可能的错误
        /// </summary>
        public void SaveSettings()
        {
            try
            {
                var settings = new SettingsModel
                {
                    WindowName = WindowName,
                    CaptureInterval = CaptureInterval,
                    ZoomLevel = ZoomLevel,
                    VideoDuration = VideoDuration,
                    UseComboBox = UseComboBox
                };

                string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsFileName, json);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"保存设置失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
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
            /// 生成视频时长(秒)
            /// </summary>
            public double VideoDuration { get; set; }

            /// <summary>
            /// 是否使用下拉框选择窗口
            /// </summary>
            public bool UseComboBox { get; set; }
        }
    }
}
