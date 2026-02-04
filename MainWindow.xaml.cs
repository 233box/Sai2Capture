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

        public MainWindow()
        {
            DataContext = Ioc.Default.GetRequiredService<MainViewModel>();
            InitializeComponent();

            // 窗口样式已通过基类自动应用

            // 在窗口加载后设置ScrollViewer引用和热键服务
            Loaded += MainWindow_Loaded;

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

            // 检查是否正在录制
            if (DataContext is MainViewModel mainViewModel)
            {
                // 通过捕获服务检查录制状态
                var isRecording = mainViewModel.CaptureService?.SharedState?.Running ?? false;

                if (isRecording)
                {
                    // 正在录制，显示确认对话框
                    var result = Sai2Capture.Services.CustomDialogService.ShowDialog(
                        "正在录制中，是否确认关闭？\n\n确认关闭将停止录制并保存当前视频。",
                        "确认关闭",
                        "关闭",
                        "再想想");

                    if (!result)
                    {
                        // 用户选择不关闭，取消关闭事件
                        e.Cancel = true;
                        return;
                    }

                    // 用户确认关闭，继续执行关闭逻辑
                    AddLogWithContext(mainViewModel, "用户确认关闭 - 停止录制并关闭应用程序");
                }
            }

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