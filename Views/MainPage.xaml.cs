using System.Windows.Controls;

namespace Sai2Capture.Views
{
    public partial class MainPage : System.Windows.Controls.UserControl
    {
        private ViewModels.MainViewModel? _viewModel;

        public MainPage()
        {
            InitializeComponent();
            Loaded += MainPage_Loaded;
            DataContextChanged += MainPage_DataContextChanged;
        }

        private void MainPage_DataContextChanged(object sender, System.Windows.DependencyPropertyChangedEventArgs e)
        {
            // 取消之前的事件订阅
            if (_viewModel != null)
            {
                _viewModel.RestartPreviewRequested -= OnRestartPreviewRequested;
            }

            _viewModel = DataContext as ViewModels.MainViewModel;

            // 订阅新的事件
            if (_viewModel != null)
            {
                _viewModel.RestartPreviewRequested += OnRestartPreviewRequested;
            }
        }

        private void OnRestartPreviewRequested()
        {
            RestartPreview();
        }

        private void MainPage_Loaded(object sender, System.Windows.RoutedEventArgs e)
        {
            // 页面加载时自动启动预览
            if (_viewModel != null)
            {
                _viewModel.StartEmbeddedPreview(PreviewImage);
            }
        }

        // 公开方法供外部调用
        public void RestartPreview()
        {
            if (_viewModel != null)
            {
                _viewModel.StartEmbeddedPreview(PreviewImage);
            }
        }
    }
}
