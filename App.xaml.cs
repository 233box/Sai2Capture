using CommunityToolkit.Mvvm.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Sai2Capture.Services;
using Sai2Capture.ViewModels;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace Sai2Capture
{
    public partial class App : System.Windows.Application
    {
        public App()
        {
            // 注册全局异常处理器
            DispatcherUnhandledException += App_DispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;

            var services = new ServiceCollection();
            ConfigureServices(services);
            Ioc.Default.ConfigureServices(services.BuildServiceProvider());
        }

        private void ConfigureServices(IServiceCollection services)
        {
            // 注册服务
            services.AddSingleton<SharedStateService>();
            services.AddSingleton<WindowCaptureService>();
            services.AddSingleton<UtilityService>();
            services.AddSingleton<CaptureService>();
            services.AddSingleton<SettingsService>();
            services.AddSingleton<LogService>();
            services.AddSingleton<RecordingDataService>();
            services.AddSingleton<HotkeyService>();
            services.AddSingleton<HotkeyViewModel>();

            // 注册 Dispatcher - 使用延迟初始化
            services.AddSingleton(provider =>
                Current?.Dispatcher ?? System.Windows.Threading.Dispatcher.CurrentDispatcher);

            // 注册 ViewModel
            services.AddSingleton<MainViewModel>();
            services.AddSingleton<RecordingManagerViewModel>();
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            var logService = Ioc.Default.GetService<LogService>();
            logService?.AddLog("应用程序启动");
            logService?.AddLog($"日志文件路径：{logService.GetLogFilePath()}");

            var mainWindow = new MainWindow();
            mainWindow.DataContext = Ioc.Default.GetRequiredService<MainViewModel>();
            mainWindow.Show();
        }

        /// <summary>
        /// UI 线程未处理异常
        /// </summary>
        private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            try
            {
                var logService = Ioc.Default.GetService<LogService>();
                logService?.AddLog($"[崩溃] UI 线程未处理异常：{e.Exception.Message}", LogLevel.Error);
                logService?.AddLog($"[崩溃] 堆栈跟踪：{e.Exception.StackTrace}", LogLevel.Error);

                System.Windows.MessageBox.Show(
                    $"发生未处理的错误：{e.Exception.Message}\n\n日志已保存到：{logService?.GetLogFilePath()}",
                    "错误",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }
            catch { }
            finally
            {
                e.Handled = true;
            }
        }

        /// <summary>
        /// 非 UI 线程未处理异常
        /// </summary>
        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            try
            {
                var exception = e.ExceptionObject as Exception;
                var logService = Ioc.Default.GetService<LogService>();
                
                if (exception != null)
                {
                    logService?.AddLog($"[崩溃] 非 UI 线程未处理异常：{exception.Message}", LogLevel.Error);
                    logService?.AddLog($"[崩溃] 堆栈跟踪：{exception.StackTrace}", LogLevel.Error);
                    
                    // 强制刷新日志到文件
                    var logFilePath = logService?.GetLogFilePath();
                    if (!string.IsNullOrEmpty(logFilePath))
                    {
                        File.AppendAllText(logFilePath, 
                            $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [崩溃] 应用程序即将终止{Environment.NewLine}");
                    }
                }
            }
            catch { }
        }

        /// <summary>
        /// 未观察到的 Task 异常
        /// </summary>
        private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            try
            {
                var logService = Ioc.Default.GetService<LogService>();
                logService?.AddLog($"[崩溃] 未观察到的 Task 异常：{e.Exception.Message}", LogLevel.Error);
                logService?.AddLog($"[崩溃] 堆栈跟踪：{e.Exception.StackTrace}", LogLevel.Error);
            }
            catch { }
            finally
            {
                e.SetObserved();
            }
        }
    }
}
