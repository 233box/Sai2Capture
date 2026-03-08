using System.Windows;

namespace Sai2Capture.Styles
{
    /// <summary>
    /// 统一的窗口基类
    /// </summary>
    public class BaseWindow : Window
    {
        protected enum WindowType { MainWindow, Dialog }
        protected WindowType WindowKind { get; private set; }

        public BaseWindow()
        {
            WindowKind = WindowType.MainWindow;
            InitializeWindow();
        }

        protected BaseWindow(WindowType windowType)
        {
            WindowKind = windowType;
            InitializeWindow();
        }

        private void InitializeWindow()
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = System.Windows.Media.Brushes.Transparent;

            if (WindowKind == WindowType.MainWindow)
                WindowTemplateHelper.ApplyCustomWindowStyle(this);
            else
                WindowTemplateHelper.ApplyCustomDialogStyle(this);
        }
    }

    /// <summary>
    /// 主窗口基类
    /// </summary>
    public class CustomMainWindow : BaseWindow
    {
        public CustomMainWindow() : base(WindowType.MainWindow) { }
    }

    /// <summary>
    /// 对话框基类
    /// </summary>
    public class CustomDialogWindow : BaseWindow
    {
        public CustomDialogWindow() : base(WindowType.Dialog)
        {
            ShowInTaskbar = false;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
    }
}
