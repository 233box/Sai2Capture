using System.Windows;
using CommunityToolkit.Mvvm.DependencyInjection;
using Sai2Capture.ViewModels;

namespace Sai2Capture
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            DataContext = Ioc.Default.GetRequiredService<MainViewModel>();
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (DataContext is MainViewModel viewModel)
            {
                viewModel.OnWindowClosing();
            }
        }
    }
}