using CommunityToolkit.Mvvm.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Sai2Capture.Services;
using Sai2Capture.ViewModels;
using System.Windows;

namespace Sai2Capture
{
    public partial class App : System.Windows.Application
    {
        public App()
        {
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

            // 注册Dispatcher - 使用延迟初始化
            services.AddSingleton(provider =>
                Current?.Dispatcher ?? System.Windows.Threading.Dispatcher.CurrentDispatcher);

            // 注册ViewModel
            services.AddSingleton<MainViewModel>();
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            
            var mainWindow = new MainWindow();
            mainWindow.DataContext = Ioc.Default.GetRequiredService<MainViewModel>();
            mainWindow.Show();
        }
    }
}
