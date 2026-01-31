using System.Windows;
using System.Windows.Controls;
using CommunityToolkit.Mvvm.DependencyInjection;
using Sai2Capture.ViewModels;
using Wpf.Ui.Controls; // 包含 FluentWindow, Button, Card 等

namespace Sai2Capture
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            DataContext = Ioc.Default.GetRequiredService<MainViewModel>();
            InitializeComponent();
            
            // 在窗口加载后设置ScrollViewer引用
            Loaded += MainWindow_Loaded;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // 查找日志ScrollViewer并传递给ViewModel
            if (DataContext is MainViewModel viewModel)
            {
                var scrollViewer = FindName("LogScrollViewer") as ScrollViewer;
                if (scrollViewer != null)
                {
                    viewModel.SetLogScrollViewer(scrollViewer);
                }
            }
        }

        private bool _isClosing = false;
        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_isClosing) return;
            
            _isClosing = true;
            try
            {
                if (DataContext is MainViewModel viewModel)
                {
                    viewModel.OnWindowClosing();
                }
            }
            finally
            {
                _isClosing = false;
            }
        }
    }
}