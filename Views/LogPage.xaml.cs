using System.Windows.Controls;

namespace Sai2Capture.Views
{
    public partial class LogPage : System.Windows.Controls.UserControl
    {
        public LogPage()
        {
            InitializeComponent();
        }

        // 提供访问 LogScrollViewer 的方法
        public ScrollViewer GetLogScrollViewer()
        {
            return LogScrollViewer;
        }
    }
}
