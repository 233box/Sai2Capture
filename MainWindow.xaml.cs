using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CommunityToolkit.Mvvm.DependencyInjection;
using Sai2Capture.Services;
using Sai2Capture.ViewModels;
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

            // 窗口样式已通过基类自动应用

            // 在窗口加载后设置ScrollViewer引用和热键服务
            Loaded += MainWindow_Loaded;

            // 获取设置服务
            _settingsService = Ioc.Default.GetService<SettingsService>();
            
            // 恢复窗口大小和位置
            RestoreWindowSettings();

            // 订阅热键触发事件
            var hotkeyService = Ioc.Default.GetService<HotkeyService>();
            if (hotkeyService != null)
            {
                _hotkeyService = hotkeyService;
                hotkeyService.OnHotkeyTriggered += OnHotkeyTriggered;
            }
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // 查找日志ScrollViewer并传递给ViewModel
            if (DataContext is MainViewModel viewModel)
            {
                var scrollViewer = LogPageControl.GetLogScrollViewer();
                if (scrollViewer != null)
                {
                    viewModel.SetLogScrollViewer(scrollViewer);
                }
            }

            // 初始化热键服务
            InitializeHotkeyService();
        }

        private bool _isClosing = false;
        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_isClosing) return;

            // 检查是否正在录制或暂停
            if (DataContext is MainViewModel mainViewModel)
            {
                var captureService = mainViewModel.CaptureService;
                if (captureService != null)
                {
                    // 检查录制状态
                    var isRecording = captureService.SharedState?.Running ?? false;
                    var captureStatus = captureService.Status;
                    var isPaused = captureStatus == "捕获已暂停";

                    if (isRecording || isPaused)
                    {
                        string message;
                        if (isRecording)
                        {
                            message = "正在录制中，请选择关闭方式：\n\n• 保存并关闭：停止录制并保存当前视频\n• 不保存关闭：放弃录制数据并直接关闭\n• 取消：返回继续录制";
                        }
                        else // isPaused
                        {
                            message = "录制已暂停但尚未保存，请选择关闭方式：\n\n• 保存并关闭：将暂停的数据保存为视频\n• 不保存关闭：放弃录制数据并直接关闭\n• 取消：返回继续编辑";
                        }

                        // 显示三选项对话框
                        var result = Sai2Capture.Services.CustomDialogService.ShowThreeButtonDialog(
                            message,
                            "选择关闭方式",
                            "保存并关闭",
                            "不保存关闭",
                            "取消");

                        if (result == 2) // 用户选择了"取消"
                        {
                            e.Cancel = true;
                            return;
                        }
                        else if (result == 1) // 用户选择了"不保存关闭"
                        {
                            // 强制停止捕获但不保存
                            ForceStopWithoutSave(mainViewModel);
                            AddLogWithContext(mainViewModel, $"用户选择不保存 - {(isRecording ? "停止录制" : "放弃暂停的录制")}并关闭应用程序");
                        }
                        else if (result == 0) // 用户选择了"保存并关闭"
                        {
                            // 正常保存并关闭
                            AddLogWithContext(mainViewModel, $"用户选择保存 - {(isRecording ? "停止录制" : "保存暂停的录制")}并关闭应用程序");
                        }
                        else if (result == -1) // ESC或关闭对话框
                        {
                            e.Cancel = true;
                            return;
                        }
                    }
                }
            }

            // 保存窗口大小和位置
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

        private void InitializeHotkeyService()
        {
            try
            {
                if (_hotkeyService != null)
                {
                    var windowHandle = new WindowInteropHelper(this).Handle;
                    _hotkeyService.Initialize(windowHandle);

                    // 设置消息钩子
                    HwndSource source = HwndSource.FromHwnd(windowHandle);
                    if (source != null)
                    {
                        source.AddHook(new HwndSourceHook(WndProcHook));
                    }
                }
            }
            catch (Exception ex)
            {
                // 热键初始化失败不影响主程序运行
                System.Diagnostics.Debug.WriteLine($"初始化热键服务失败: {ex.Message}");
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

        private void OnHotkeyTriggered(object? sender, HotkeyEventArgs e)
        {
            if (DataContext is MainViewModel viewModel)
            {
                viewModel.ExecuteHotkeyCommand(e.HotkeyId, e.CommandName);
            }
        }

        /// <summary>
        /// 强制停止捕获但不保存数据
        /// </summary>
        private void ForceStopWithoutSave(MainViewModel viewModel)
        {
            try
            {
                var captureService = viewModel.CaptureService;
                if (captureService != null)
                {
                    // 强制停止捕获服务
                    var sharedState = captureService.SharedState;
                    if (sharedState != null)
                    {
                        // 立即设置为停止状态
                        sharedState.Running = false;

                        // 清理VideoWriter引用但不保存（直接Dispose）
                        if (sharedState.VideoWriter != null)
                        {
                            try
                            {
                                sharedState.VideoWriter.Release(); // 释放资源但不保存
                            }
                            catch { /* 忽略释放时的错误 */ }

                            sharedState.VideoWriter.Dispose();
                            sharedState.VideoWriter = null;
                            sharedState.VideoPath = null;
                        }

                        // 清理其他状态
                        sharedState.Hwnd = IntPtr.Zero;
                        // 更新状态显示 timers 在 MainViewModel 中处理
                    }

                    captureService.Status = "捕获已取消（未保存）";
                }

                AddLogWithContext(viewModel, "已强制停止捕获，未保存视频数据");
            }
            catch (Exception ex)
            {
                AddLogWithContext(viewModel, $"强制停止捕获时出错: {ex.Message}");
            }
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

        /// <summary>
        /// 保存窗口大小和位置到设置服务
        /// </summary>
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

                    // 保存到设置文件
                    _settingsService.SaveSettings();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"保存窗口设置失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 从设置服务恢复窗口大小和位置
        /// </summary>
        private void RestoreWindowSettings()
        {
            try
            {
                if (_settingsService != null)
                {
                    double width = Math.Max(_settingsService.WindowWidth, 400);
                    double height = Math.Max(_settingsService.WindowHeight, 300);
                    double left = _settingsService.WindowLeft;
                    double top = _settingsService.WindowTop;

                    Width = width;
                    Height = height;

                    // 只有当位置合理时才设置位置
                    if (left >= 0 && top >= 0)
                    {
                        // 确保窗口完全在屏幕内
                        var screenWidth = SystemParameters.PrimaryScreenWidth;
                        var screenHeight = SystemParameters.PrimaryScreenHeight;
                        
                        if (left + width <= screenWidth && top + height <= screenHeight)
                        {
                            Left = left;
                            Top = top;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"恢复窗口设置失败: {ex.Message}");
                // 失败时使用默认大小，已在XAML中设置
            }
        }

        // 窗口控制按钮功能现在由WindowTemplateHelper自动处理

        private void MainPage_Loaded(object sender, RoutedEventArgs e)
        {

        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);

            // 清理热键服务
            if (_hotkeyService != null)
            {
                _hotkeyService.Dispose();
                _hotkeyService = null;
            }
        }
    }
}