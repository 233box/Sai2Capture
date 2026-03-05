using System.Windows;
using Sai2Capture.Styles;

namespace Sai2Capture.Views
{
    /// <summary>
    /// ConfirmDialog.xaml 的交互逻辑
    /// </summary>
    public partial class ConfirmDialog : CustomDialogWindow
    {
        public bool Confirmed { get; private set; }

        public ConfirmDialog()
        {
            InitializeComponent();
        }

        public ConfirmDialog(string message, string? subMessage = null, string title = "确认操作") : this()
        {
            MessageText.Text = message;
            
            if (!string.IsNullOrEmpty(subMessage))
            {
                SubMessageText.Text = subMessage;
                SubMessageText.Visibility = Visibility.Visible;
            }

            Title = title;
        }

        private void ConfirmButton_Click(object sender, RoutedEventArgs e)
        {
            Confirmed = true;
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            Confirmed = false;
            DialogResult = false;
            Close();
        }
    }
}
