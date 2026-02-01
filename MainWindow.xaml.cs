using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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

        // 标题栏拖动
        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                // 双击最大化/还原
                MaximizeButton_Click(sender, e);
            }
            else
            {
                // 单击拖动
                DragMove();
            }
        }

        // 最小化按钮
        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        // 最大化/还原按钮
        private void MaximizeButton_Click(object sender, RoutedEventArgs e)
        {
            if (WindowState == WindowState.Maximized)
            {
                WindowState = WindowState.Normal;
                // 还原图标：单个方框
                MaximizeIcon.Data = System.Windows.Media.Geometry.Parse("M 0,0 H 10 V 10 H 0 Z");
            }
            else
            {
                WindowState = WindowState.Maximized;
                // 最大化图标：双层方框
                MaximizeIcon.Data = System.Windows.Media.Geometry.Parse("M 0,2 H 8 V 10 H 0 Z M 2,0 H 10 V 8");
            }
        }

        // 关闭按钮
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}