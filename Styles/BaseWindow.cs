using System.Windows;

namespace Sai2Capture.Styles
{
    /// <summary>
    /// 统一的窗口基类，自动应用标准样式
    /// </summary>
    public class BaseWindow : Window
    {
        /// <summary>
        /// 指示窗口类型：主窗口或对话框
        /// </summary>
        protected enum WindowType
        {
            MainWindow,
            Dialog
        }

        protected WindowType WindowKind { get; private set; }

        /// <summary>
        /// 初始化基础窗口（自动应用主窗口样式）
        /// </summary>
        public BaseWindow()
        {
            WindowKind = WindowType.MainWindow;
            InitializeWindow();
        }

        /// <summary>
        /// 初始化基础窗口并指定窗口类型
        /// </summary>
        /// <param name="windowType">窗口类型</param>
        protected BaseWindow(WindowType windowType)
        {
            WindowKind = windowType;
            InitializeWindow();
        }

        /// <summary>
        /// 初始化窗口设置
        /// </summary>
        private void InitializeWindow()
        {
            // 设置基本属性
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = System.Windows.Media.Brushes.Transparent;

            // 根据窗口类型应用样式
            if (WindowKind == WindowType.MainWindow)
            {
                WindowTemplateHelper.ApplyCustomWindowStyle(this);
            }
            else
            {
                WindowTemplateHelper.ApplyCustomDialogStyle(this);
            }
        }

        /// <summary>
        /// 设置窗口为对话框样式
        /// </summary>
        protected void SetAsDialog()
        {
            WindowKind = WindowType.Dialog;
            WindowTemplateHelper.ApplyCustomDialogStyle(this);
        }

        /// <summary>
        /// 设置窗口为主窗口样式
        /// </summary>
        protected void SetAsMainWindow()
        {
            WindowKind = WindowType.MainWindow;
            WindowTemplateHelper.ApplyCustomWindowStyle(this);
        }
    }

    /// <summary>
    /// 主窗口基类，自动应用主窗口样式
    /// </summary>
    public class CustomMainWindow : BaseWindow
    {
        public CustomMainWindow() : base(WindowType.MainWindow)
        {
        }
    }

    /// <summary>
    /// 对话框基类，自动应用对话框样式
    /// </summary>
    public class CustomDialogWindow : BaseWindow
    {
        public CustomDialogWindow() : base(WindowType.Dialog)
        {
            // 对话框的默认设置
            ShowInTaskbar = false;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
    }
}