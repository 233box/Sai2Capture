using System.Windows;
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