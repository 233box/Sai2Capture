using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sai2Capture.Services;
using System;
using System.Collections.ObjectModel;
using System.Windows;

namespace Sai2Capture.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        private readonly SharedStateService _sharedState;
        private readonly WindowCaptureService _windowCaptureService;
        private readonly UtilityService _utilityService;
        private readonly CaptureService _captureService;
        private readonly VideoCreatorService _videoCreatorService;
        private readonly SettingsService _settingsService;

        [ObservableProperty]
        private ObservableCollection<string> _windowTitles = new();

        [ObservableProperty]
        public string _selectedWindowTitle = "从列表选择或手动输入";

        [ObservableProperty]
        public string _windowName = "导航器";

        [ObservableProperty]
        public double _captureInterval = 0.1;

        [ObservableProperty]
        public string _zoomLevel = "125%";

        [ObservableProperty]
        public double _videoDuration = 10;

        [ObservableProperty]
        public bool _useComboBox = true;

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

        private void InitializeServices()
        {
            _captureService.Initialize(Application.Current.Dispatcher);
            WindowTitles = new ObservableCollection<string>(_windowCaptureService.EnumWindowTitles());
            _settingsService.LoadSettings();

            // 同步设置
            WindowName = _settingsService.WindowName;
            CaptureInterval = _settingsService.CaptureInterval;
            ZoomLevel = _settingsService.ZoomLevel;
            VideoDuration = _settingsService.VideoDuration;
            UseComboBox = _settingsService.UseComboBox;
        }

        [RelayCommand]
        private void StartCapture()
        {
            string windowTitle = UseComboBox ? SelectedWindowTitle : WindowName;
            _captureService.StartCapture(windowTitle, UseComboBox, CaptureInterval);
        }

        [RelayCommand]
        private void PauseCapture()
        {
            _captureService.PauseCapture();
        }

        [RelayCommand]
        private void StopCapture()
        {
            _captureService.StopCapture();
        }

        [RelayCommand]
        private void CreateVideo()
        {
            _videoCreatorService.SelectFolderAndCreateVideo(VideoDuration);
        }

        [RelayCommand]
        private void ToggleTopmost(System.Windows.Window window)
        {
            _utilityService.ToggleTopmost(window);
        }

        [ObservableProperty]
        private string _status = "准备就绪";

        [RelayCommand]
        private void PreviewWindow()
        {
            try
            {
                string windowTitle = UseComboBox ? SelectedWindowTitle : WindowName;
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

        public void OnWindowClosing()
        {
            _settingsService.WindowName = WindowName;
            _settingsService.CaptureInterval = CaptureInterval;
            _settingsService.ZoomLevel = ZoomLevel;
            _settingsService.VideoDuration = VideoDuration;
            _settingsService.UseComboBox = UseComboBox;
            _settingsService.SaveSettings();

            _captureService.StopCapture();
        }
    }
}
