using Sai2Capture.ViewModels;
using UserControl = System.Windows.Controls.UserControl;

namespace Sai2Capture.Views
{
    /// <summary>
    /// VideoRepairPage.xaml 的交互逻辑
    /// </summary>
    public partial class VideoRepairPage : UserControl
    {
        public VideoRepairPage()
        {
            InitializeComponent();
        }

        public VideoRepairPage(VideoRepairViewModel viewModel) : this()
        {
            DataContext = viewModel;
        }

        public void Cleanup()
        {
            if (DataContext is VideoRepairViewModel viewModel)
            {
                viewModel.Cleanup();
            }
        }
    }
}
