using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.IO;
using System.Text.Json;
using System.Windows;

namespace Sai2Capture.Services
{
    public partial class SettingsService : ObservableObject
    {
        private const string SettingsFileName = "settings.json";
        private readonly SharedStateService _sharedState;

        [ObservableProperty]
        private string _windowName = "导航器";

        [ObservableProperty]
        private double _captureInterval = 0.1;

        [ObservableProperty]
        private string _zoomLevel = "125%";

        [ObservableProperty]
        private double _videoDuration = 10;

        [ObservableProperty]
        private bool _useComboBox = true;

        public SettingsService(SharedStateService sharedState)
        {
            _sharedState = sharedState;
        }

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

        private class SettingsModel
        {
            public string? WindowName { get; set; }
            public double CaptureInterval { get; set; }
            public string? ZoomLevel { get; set; }
            public double VideoDuration { get; set; }
            public bool UseComboBox { get; set; }
        }
    }
}
