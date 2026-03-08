using CommunityToolkit.Mvvm.ComponentModel;
using Sai2Capture.Models;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using WpfMessageBox = System.Windows.MessageBox;
using WpfMessageBoxButton = System.Windows.MessageBoxButton;
using WpfMessageBoxImage = System.Windows.MessageBoxImage;

namespace Sai2Capture.Services
{
    /// <summary>
    /// 设置管理服务 - 负责应用程序配置的加载、保存和管理
    /// </summary>
    public partial class SettingsService : ObservableObject
    {
        private const string SettingsFileName = "settings.json";
        private readonly SharedStateService _sharedState;
        private readonly LogService _logService;

        [ObservableProperty] private string _windowName = "导航器";
        [ObservableProperty] private double _captureInterval = 0.1;
        [ObservableProperty] private string _zoomLevel = "125%";
        [ObservableProperty] private string _savePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "output");
        [ObservableProperty] private ObservableCollection<HotkeyModel> _hotkeys = new();
        [ObservableProperty] private string _sai2Path = "";
        [ObservableProperty] private double _windowWidth = 800;
        [ObservableProperty] private double _windowHeight = 600;
        [ObservableProperty] private double _windowLeft = -1;
        [ObservableProperty] private double _windowTop = -1;
        [ObservableProperty] private double _previewColumnWidth = 320;
        
        // 视频导出设置
        [ObservableProperty] private VideoCodec _exportCodec = VideoCodec.H264;
        [ObservableProperty] private double _exportFps = 20;
        [ObservableProperty] private int _exportQualityLevel = 2;

        public SettingsService(SharedStateService sharedState, LogService logService)
        {
            _sharedState = sharedState;
            _logService = logService;
        }

        public string GetSettingsFilePath() => Path.GetFullPath(SettingsFileName);

        public void LoadSettings()
        {
            try
            {
                if (File.Exists(SettingsFileName))
                {
                    _logService.AddLog($"加载设置文件：{GetSettingsFilePath()}");
                    string json = File.ReadAllText(SettingsFileName);
                    var settings = JsonSerializer.Deserialize<SettingsData>(json);

                    if (settings != null)
                    {
                        WindowName = settings.WindowName ?? WindowName;
                        CaptureInterval = settings.CaptureInterval > 0 ? settings.CaptureInterval : 0.1;
                        ZoomLevel = settings.ZoomLevel ?? ZoomLevel;
                        SavePath = !string.IsNullOrEmpty(settings.SavePath) ? settings.SavePath : SavePath;
                        Sai2Path = settings.Sai2Path ?? Sai2Path;
                        WindowWidth = settings.WindowWidth > 0 ? settings.WindowWidth : WindowWidth;
                        WindowHeight = settings.WindowHeight > 0 ? settings.WindowHeight : WindowHeight;
                        WindowLeft = settings.WindowLeft;
                        WindowTop = settings.WindowTop;
                        PreviewColumnWidth = settings.PreviewColumnWidth > 0 ? settings.PreviewColumnWidth : PreviewColumnWidth;
                        
                        // 加载视频导出设置
                        ExportCodec = settings.ExportCodec;
                        ExportFps = settings.ExportFps > 0 ? settings.ExportFps : ExportFps;
                        ExportQualityLevel = settings.ExportQualityLevel > 0 ? settings.ExportQualityLevel : ExportQualityLevel;

                        if (settings.Hotkeys != null && settings.Hotkeys.Any())
                        {
                            Hotkeys.Clear();
                            foreach (var savedHotkey in settings.Hotkeys)
                                Hotkeys.Add(savedHotkey);
                            _logService.AddLog($"加载了 {settings.Hotkeys.Count} 个热键配置");
                        }
                        else
                        {
                            InitializeDefaultHotkeys();
                        }

                        _sharedState.Interval = CaptureInterval;
                        EnsureSavePathExists();
                        _logService.AddLog($"设置加载成功 - 窗口：{WindowName}, 间隔：{CaptureInterval}秒，缩放：{ZoomLevel}, 保存路径：{SavePath}, 窗口大小：{WindowWidth}x{WindowHeight}");
                    }
                }
                else
                {
                    _logService.AddLog("设置文件不存在，使用默认设置", LogLevel.Warning);
                    InitializeDefaultHotkeys();
                    EnsureSavePathExists();
                }
            }
            catch (Exception ex)
            {
                _logService.AddLog($"加载设置失败：{ex.Message}", LogLevel.Error);
                WpfMessageBox.Show($"加载设置失败：{ex.Message}", "错误", WpfMessageBoxButton.OK, WpfMessageBoxImage.Warning);
                InitializeDefaultHotkeys();
                EnsureSavePathExists();
            }
        }

        public void SaveSettings()
        {
            try
            {
                EnsureSavePathExists();

                var settings = new SettingsData
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
                    WindowTop = WindowTop,
                    PreviewColumnWidth = PreviewColumnWidth,
                    
                    // 视频导出设置
                    ExportCodec = ExportCodec,
                    ExportFps = ExportFps,
                    ExportQualityLevel = ExportQualityLevel
                };

                string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsFileName, json);
                _logService.AddLog($"设置已保存到：{GetSettingsFilePath()}");
            }
            catch (Exception ex)
            {
                _logService.AddLog($"保存设置失败：{ex.Message}", LogLevel.Error);
                WpfMessageBox.Show($"保存设置失败：{ex.Message}", "错误", WpfMessageBoxButton.OK, WpfMessageBoxImage.Error);
            }
        }

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
                _logService.AddLog($"保存热键配置失败：{ex.Message}", LogLevel.Error);
                throw;
            }
        }

        private void EnsureSavePathExists()
        {
            try
            {
                if (!Directory.Exists(SavePath))
                {
                    Directory.CreateDirectory(SavePath);
                    _logService.AddLog($"创建保存目录：{SavePath}");
                }
            }
            catch (Exception ex)
            {
                _logService.AddLog($"创建保存目录失败：{ex.Message}", LogLevel.Error);
            }
        }

        private void InitializeDefaultHotkeys()
        {
            Hotkeys.Clear();
            foreach (var hotkey in HotkeyModel.CreateDefaultHotkeys())
                Hotkeys.Add(hotkey);
            _logService.AddLog("初始化默认热键配置");
        }

        private class SettingsData
        {
            public string? WindowName { get; set; }
            public double CaptureInterval { get; set; }
            public string? ZoomLevel { get; set; }
            public string? SavePath { get; set; }
            public List<HotkeyModel>? Hotkeys { get; set; }
            public string? Sai2Path { get; set; }
            public double WindowWidth { get; set; }
            public double WindowHeight { get; set; }
            public double WindowLeft { get; set; }
            public double WindowTop { get; set; }
            public double PreviewColumnWidth { get; set; }
            
            // 视频导出设置
            public VideoCodec ExportCodec { get; set; }
            public double ExportFps { get; set; }
            public int ExportQualityLevel { get; set; }
        }
    }
}
