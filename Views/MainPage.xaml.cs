using CommunityToolkit.Mvvm.DependencyInjection;
using Sai2Capture.Services;
using System.Windows;
using WpfUserControl = System.Windows.Controls.UserControl;

namespace Sai2Capture.Views
{
    public partial class MainPage : WpfUserControl
    {
        private ViewModels.MainViewModel? _viewModel;
        private SettingsService? _settingsService;
        private bool _isLoaded = false;

        public MainPage()
        {
            InitializeComponent();
            Loaded += MainPage_Loaded;
            DataContextChanged += MainPage_DataContextChanged;

            // 获取设置服务
            _settingsService = Ioc.Default.GetService<SettingsService>();
        }

        private void MainPage_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (_viewModel != null)
            {
                _viewModel.RestartPreviewRequested -= OnRestartPreviewRequested;
            }

            _viewModel = DataContext as ViewModels.MainViewModel;

            if (_viewModel != null)
            {
                _viewModel.RestartPreviewRequested += OnRestartPreviewRequested;
            }
        }

        private void OnRestartPreviewRequested()
        {
            RestartPreview();
        }

        private void MainPage_Loaded(object sender, RoutedEventArgs e)
        {
            if (_isLoaded) return;
            _isLoaded = true;

            // 加载保存的预览区域宽度
            LoadPreviewColumnWidth();

            // 页面加载时自动启动预览
            if (_viewModel != null)
            {
                _viewModel.StartEmbeddedPreview(PreviewImage);
            }
        }

        /// <summary>
        /// 加载保存的预览区域宽度
        /// </summary>
        private void LoadPreviewColumnWidth()
        {
            if (_settingsService != null && _settingsService.PreviewColumnWidth > 0)
            {
                double width = _settingsService.PreviewColumnWidth;
                // 确保宽度在合理范围内
                width = Math.Max(200, Math.Min(width, ActualWidth / 2));
                PreviewColumn.Width = new GridLength(width);
            }
        }

        /// <summary>
        /// 保存预览区域宽度
        /// </summary>
        private void SavePreviewColumnWidth()
        {
            if (_settingsService != null && PreviewColumn.ActualWidth > 0)
            {
                _settingsService.PreviewColumnWidth = PreviewColumn.ActualWidth;
                _settingsService.SaveSettings();
            }
        }

        /// <summary>
        /// 在卸载时保存预览区域宽度
        /// </summary>
        public void SaveSettings()
        {
            SavePreviewColumnWidth();
        }

        /// <summary>
        /// 公开方法供外部调用
        /// </summary>
        public void RestartPreview()
        {
            if (_viewModel != null)
            {
                _viewModel.StartEmbeddedPreview(PreviewImage);
            }
        }
    }
}
