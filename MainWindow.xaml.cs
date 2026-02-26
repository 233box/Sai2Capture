using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CommunityToolkit.Mvvm.DependencyInjection;
using Sai2Capture.Services;
using Sai2Capture.ViewModels;
using Sai2Capture.Views;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace Sai2Capture
{
    public partial class MainWindow : Sai2Capture.Styles.CustomMainWindow
    {
        private HotkeyService? _hotkeyService;
        private SettingsService? _settingsService;

        public MainWindow()
        {
            DataContext = Ioc.Default.GetRequiredService<MainViewModel>();
            InitializeComponent();

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
        }

        private void MainWindow_SourceInitialized(object? sender, EventArgs e)
        {
            // 设置窗口捕获服务的自身窗口句柄
            var windowCaptureService = Ioc.Default.GetService<WindowCaptureService>();
            if (windowCaptureService != null)
            {
                var handle = new WindowInteropHelper(this).Handle;
                if (handle != IntPtr.Zero)
                {
                    windowCaptureService.SetSelfWindowHandle(handle);
                }
            }

            // 初始化热键服务
            if (_hotkeyService != null)
            {
                var windowHandle = new WindowInteropHelper(this).Handle;
                _hotkeyService.Initialize(windowHandle);

                HwndSource source = HwndSource.FromHwnd(windowHandle);
                source?.AddHook(new HwndSourceHook(WndProcHook));
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

        private bool _isClosing = false;
        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_isClosing) return;

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

                        if (result == 2 || result == -1) // 取消或关闭对话框
                        {
                            e.Cancel = true;
                            return;
                        }
                        else if (result == 1) // 不保存关闭
                        {
                            captureService.CancelCapture();
                            AddLogWithContext(mainViewModel, $"用户选择不保存 - {(isRecording ? "停止录制" : "放弃暂停的录制")}并关闭应用程序");
                        }
                        else // 保存并关闭
                        {
                            AddLogWithContext(mainViewModel, $"用户选择保存 - {(isRecording ? "停止录制" : "保存暂停的录制")}并关闭应用程序");
                        }
                    }
                }
            }

            SaveWindowSettings();

            _isClosing = true;
            try
            {
                if (DataContext is MainViewModel closingViewModel)
                {
                    closingViewModel.OnWindowClosing();
                }
            }
            finally
            {
                _isClosing = false;
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
            if (_hotkeyService != null)
            {
                if (_hotkeyService.ProcessWndProc(hwnd, msg, wParam, lParam, out IntPtr result))
                {
                    handled = true;
                    return result;
                }
            }
            return IntPtr.Zero;
        }

        private void AddLogWithContext(MainViewModel viewModel, string message)
        {
            try
            {
                viewModel.AddLog(message);
            }
            catch
            {
                // 日志记录失败时不阻止关闭流程
            }
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
                        var screenWidth = SystemParameters.PrimaryScreenWidth;
                        var screenHeight = SystemParameters.PrimaryScreenHeight;

                        if (Width + _settingsService.WindowLeft <= screenWidth &&
                            Height + _settingsService.WindowTop <= screenHeight)
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

            // 保存 MainPage 的预览区域宽度设置
            var mainPage = FindChild<MainPage>(this);
            mainPage?.SaveSettings();

            if (_hotkeyService != null)
            {
                _hotkeyService.Dispose();
                _hotkeyService = null;
            }

            if (DataContext is MainViewModel viewModel)
            {
                viewModel.StopCanvasPolling();
            }
        }

        /// <summary>
        /// 在视觉树中查找子控件
        /// </summary>
        private static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null) return null;

            for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
                if (child is T result)
                    return result;

                var grandChild = FindChild<T>(child);
                if (grandChild != null)
                    return grandChild;
            }

            return null;
        }
    }
}
