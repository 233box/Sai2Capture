using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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
                var scrollViewer = LogPageControl.GetLogScrollViewer();
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

        // 置顶按钮
        private void PinButton_Click(object sender, RoutedEventArgs e)
        {
            Topmost = !Topmost;

            // 更新图钉图标的旋转角度和置顶状态
            if (Topmost)
            {
                // 置顶时，图钉旋转45度（钉住状态），设置Tag表示置顶状态
                PinRotation.Angle = 45;
                PinButton.ToolTip = "取消置顶";
                PinButton.Tag = "Pinned";
            }
            else
            {
                // 取消置顶时，图钉恢复原位，清除Tag
                PinRotation.Angle = 0;
                PinButton.ToolTip = "置顶";
                PinButton.Tag = null;
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

        private void MainPage_Loaded(object sender, RoutedEventArgs e)
        {

        }
    }
}