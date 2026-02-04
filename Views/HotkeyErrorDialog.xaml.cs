using System.Windows;

namespace Sai2Capture.Views
{
    /// <summary>
    /// 热键错误对话框
    /// </summary>
    public partial class HotkeyErrorDialog : Sai2Capture.Styles.CustomDialogWindow
    {
        public static readonly DependencyProperty MessageProperty =
            DependencyProperty.Register(nameof(Message), typeof(string), typeof(HotkeyErrorDialog));

        public string Message
        {
            get => (string)GetValue(MessageProperty);
            set => SetValue(MessageProperty, value);
        }

        public HotkeyErrorDialog()
        {
            InitializeComponent();
            DataContext = this;
        }

        private void OKButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }
    }
}