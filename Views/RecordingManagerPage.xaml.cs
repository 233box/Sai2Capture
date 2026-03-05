namespace Sai2Capture.Views
{
    public partial class RecordingManagerPage : System.Windows.Controls.UserControl
    {
        private bool _isInitialized = false;

        public RecordingManagerPage()
        {
            InitializeComponent();
            Loaded += RecordingManagerPage_Loaded;
        }

        private void RecordingManagerPage_Loaded(object sender, System.Windows.RoutedEventArgs e)
        {
            // 只在第一次加载时刷新
            if (_isInitialized) return;
            _isInitialized = true;

            // 页面加载时自动刷新文件列表
            if (DataContext is ViewModels.RecordingManagerViewModel viewModel)
            {
                viewModel.RefreshFileListCommand.Execute(null);
            }
        }
    }
}
