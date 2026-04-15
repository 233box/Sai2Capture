using CommunityToolkit.Mvvm.DependencyInjection;
using Sai2Capture.Services;
using Sai2Capture.ViewModels;
using System.Windows;
using WpfUserControl = System.Windows.Controls.UserControl;

namespace Sai2Capture.Views
{
    public partial class MainPage : WpfUserControl
    {
        private MainViewModel? _viewModel;
        private SettingsService? _settingsService;
        private Action? _restartPreviewHandler;
        private bool _isLoaded = false;

        public MainPage()
        {
            InitializeComponent();
            Loaded += MainPage_Loaded;
            Unloaded += MainPage_Unloaded;
            DataContextChanged += MainPage_DataContextChanged;

            _settingsService = Ioc.Default.GetService<SettingsService>();
        }

        private void MainPage_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (_viewModel != null && _restartPreviewHandler != null)
            {
                _viewModel.RestartPreviewRequested -= _restartPreviewHandler;
                _restartPreviewHandler = null;
            }

            _viewModel = DataContext as MainViewModel;

            if (_viewModel != null)
            {
                _restartPreviewHandler = OnRestartPreviewRequested;
                _viewModel.RestartPreviewRequested += _restartPreviewHandler;
            }
        }

        private void OnRestartPreviewRequested()
        {
            _viewModel?.StartEmbeddedPreview(PreviewImage);
        }

        private void MainPage_Loaded(object sender, RoutedEventArgs e)
        {
            if (_isLoaded) return;
            _isLoaded = true;

            LoadPreviewColumnWidth();

            if (_viewModel != null)
            {
                _viewModel.StartEmbeddedPreview(PreviewImage);
            }
        }

        private void MainPage_Unloaded(object sender, RoutedEventArgs e)
        {
            if (_viewModel != null && _restartPreviewHandler != null)
            {
                _viewModel.RestartPreviewRequested -= _restartPreviewHandler;
                _restartPreviewHandler = null;
            }
        }

        private void LoadPreviewColumnWidth()
        {
            if (_settingsService != null && _settingsService.PreviewColumnWidth > 0)
            {
                double width = _settingsService.PreviewColumnWidth;
                width = Math.Max(200, Math.Min(width, ActualWidth / 2));
                PreviewColumn.Width = new GridLength(width);
            }
        }

        public void SaveSettings()
        {
            if (_settingsService != null && PreviewColumn.ActualWidth > 0)
            {
                _settingsService.PreviewColumnWidth = PreviewColumn.ActualWidth;
                _settingsService.SaveSettings();
            }
        }
    }
}
