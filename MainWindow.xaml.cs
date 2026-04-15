using CommunityToolkit.Mvvm.DependencyInjection;
using Sai2Capture.Helpers;
using Sai2Capture.Services;
using Sai2Capture.Styles;
using Sai2Capture.ViewModels;
using Sai2Capture.Views;
using System.Windows;
using System.Windows.Interop;

namespace Sai2Capture
{
    public partial class MainWindow : CustomMainWindow
    {
        private HotkeyService? _hotkeyService;
        private SettingsService? _settingsService;

        public MainWindow()
        {
            DataContext = Ioc.Default.GetRequiredService<MainViewModel>();
            InitializeComponent();

            if (FindName("RecordingManagerPageControl") is RecordingManagerPage recordingManagerPage)
            {
                recordingManagerPage.DataContext = Ioc.Default.GetRequiredService<RecordingManagerViewModel>();
            }

            SourceInitialized += MainWindow_SourceInitialized;
            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;

            _settingsService = Ioc.Default.GetService<SettingsService>();
            RestoreWindowSettings();

            _hotkeyService = Ioc.Default.GetService<HotkeyService>();
            if (_hotkeyService != null)
            {
                _hotkeyService.OnHotkeyTriggered += OnHotkeyTriggered;
            }

            // 订阅 ViewModel 的置顶切换请求
            if (DataContext is MainViewModel viewModel)
            {
                viewModel.ToggleWindowTopmostRequested += OnToggleWindowTopmostRequested;
            }
        }

        private void OnToggleWindowTopmostRequested()
        {
            try
            {
                Topmost = !Topmost;
                Sai2Capture.Styles.WindowTemplateHelper.UpdateWindowTopmostState(this);

                if (DataContext is MainViewModel viewModel)
                {
                    viewModel.AddLog($"窗口置顶状态已切换为：{Topmost}");
                    viewModel.Status = $"窗口已{(Topmost ? "置顶" : "取消置顶")}";
                }
            }
            catch (Exception ex)
            {
                if (DataContext is MainViewModel viewModel)
                {
                    viewModel.AddLog($"切换窗口置顶状态失败：{ex.Message}", LogLevel.Error);
                }
            }
        }

        private void MainWindow_SourceInitialized(object? sender, EventArgs e)
        {
            var windowHandle = new WindowInteropHelper(this).Handle;
            if (windowHandle == IntPtr.Zero) return;

            var windowCaptureService = Ioc.Default.GetService<WindowCaptureService>();
            windowCaptureService?.SetSelfWindowHandle(windowHandle);

            if (_hotkeyService != null)
            {
                _hotkeyService.Initialize(windowHandle);
                HwndSource.FromHwnd(windowHandle)?.AddHook(WndProcHook);
            }
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel viewModel)
            {
                var scrollViewer = LogPageControl.GetLogScrollViewer();
                if (scrollViewer != null)
                {
                    viewModel.SetLogScrollViewer(scrollViewer);
                }
            }
        }

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            if (DataContext is MainViewModel mainViewModel)
            {
                var captureService = mainViewModel.CaptureService;
                if (captureService != null)
                {
                    var isRecording = captureService.SharedState?.Running ?? false;
                    var isPaused = captureService.Status == "捕获已暂停";

                    if (isRecording || isPaused)
                    {
                        string message = isRecording
                            ? "正在录制中，请选择关闭方式：\n\n• 保存并关闭：停止录制并保存当前视频\n• 不保存关闭：放弃录制数据并直接关闭\n• 取消：返回继续录制"
                            : "录制已暂停但尚未保存，请选择关闭方式：\n\n• 保存并关闭：将暂停的数据保存为视频\n• 不保存关闭：放弃录制数据并直接关闭\n• 取消：返回继续编辑";

                        var result = CustomDialogService.ShowThreeButtonDialog(
                            message, "选择关闭方式", "保存并关闭", "不保存关闭", "取消");

                        if (result == 2 || result == -1)
                        {
                            e.Cancel = true;
                            return;
                        }

                        if (result == 1)
                        {
                            captureService.CancelCapture();
                            mainViewModel.AddLog($"用户选择不保存 - {(isRecording ? "停止录制" : "放弃暂停的录制")}并关闭应用程序");
                        }
                        else
                        {
                            mainViewModel.AddLog($"用户选择保存 - {(isRecording ? "停止录制" : "保存暂停的录制")}并关闭应用程序");
                        }
                    }
                }
            }

            SaveWindowSettings();

            if (DataContext is MainViewModel closingViewModel)
            {
                closingViewModel.OnWindowClosing();
            }
        }

        private void OnHotkeyTriggered(object? sender, HotkeyEventArgs e)
        {
            if (DataContext is MainViewModel viewModel)
            {
                viewModel.ExecuteHotkeyCommand(e.HotkeyId, e.CommandName);
            }
        }

        private IntPtr WndProcHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (_hotkeyService?.ProcessWndProc(hwnd, msg, wParam, lParam, out IntPtr result) == true)
            {
                handled = true;
                return result;
            }
            return IntPtr.Zero;
        }

        private void SaveWindowSettings()
        {
            try
            {
                if (WindowState == WindowState.Normal && IsLoaded && _settingsService != null)
                {
                    _settingsService.WindowWidth = ActualWidth;
                    _settingsService.WindowHeight = ActualHeight;
                    _settingsService.WindowLeft = Left;
                    _settingsService.WindowTop = Top;
                    _settingsService.SaveSettings();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"保存窗口设置失败：{ex.Message}");
            }
        }

        private void RestoreWindowSettings()
        {
            try
            {
                if (_settingsService != null)
                {
                    Width = Math.Max(_settingsService.WindowWidth, 400);
                    Height = Math.Max(_settingsService.WindowHeight, 300);

                    if (_settingsService.WindowLeft >= 0 && _settingsService.WindowTop >= 0)
                    {
                        if (Width + _settingsService.WindowLeft <= SystemParameters.PrimaryScreenWidth &&
                            Height + _settingsService.WindowTop <= SystemParameters.PrimaryScreenHeight)
                        {
                            Left = _settingsService.WindowLeft;
                            Top = _settingsService.WindowTop;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"恢复窗口设置失败：{ex.Message}");
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);

            if (this.FindChild<MainPage>() is { } mainPage)
            {
                mainPage.SaveSettings();
            }

            _hotkeyService?.Dispose();
            _hotkeyService = null;

            if (DataContext is MainViewModel viewModel)
            {
                viewModel.StopCanvasPolling();
            }
        }
    }
}
