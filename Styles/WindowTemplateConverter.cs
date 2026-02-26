using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Shell;
using Application = System.Windows.Application;
using Button = System.Windows.Controls.Button;
using Border = System.Windows.Controls.Border;
using TextBlock = System.Windows.Controls.TextBlock;
using Path = System.Windows.Shapes.Path;

namespace Sai2Capture.Styles
{
    /// <summary>
    /// 窗口模板辅助类
    /// </summary>
    public static class WindowTemplateHelper
    {
        /// <summary>
        /// 应用基础窗口样式
        /// </summary>
        private static void ApplyBaseStyle(Window window)
        {
            window.WindowStyle = WindowStyle.None;
            window.AllowsTransparency = true;
            window.Background = System.Windows.Media.Brushes.Transparent;

            var chrome = new WindowChrome
            {
                CaptionHeight = 28,
                CornerRadius = new CornerRadius(8),
                GlassFrameThickness = new Thickness(0),
                ResizeBorderThickness = new Thickness(6),
                UseAeroCaptionButtons = false
            };
            WindowChrome.SetWindowChrome(window, chrome);
        }

        /// <summary>
        /// 绑定标题栏拖动事件
        /// </summary>
        public static void BindTitleBarEvents(DependencyObject window)
        {
            if (window is Window w)
            {
                w.Loaded += (s, e) =>
                {
                    w.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (w.Template != null)
                        {
                            var titleBar = w.Template.FindName("TitleBarBorder", w) as Border;
                            if (titleBar != null)
                            {
                                titleBar.MouseLeftButtonDown += (sender, args) =>
                                {
                                    if (args.ClickCount == 2)
                                        w.WindowState = w.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
                                    else
                                        w.DragMove();
                                };
                            }

                            var titleText = w.Template.FindName("WindowTitleText", w) as TextBlock;
                            if (titleText != null)
                                titleText.Text = w.Title;

                            BindWindowControlButtons(w, w.Template);
                        }
                    }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                };

                w.StateChanged += (s, e) => UpdateMaximizeIcon(w);
            }
        }

        /// <summary>
        /// 绑定窗口控制按钮事件
        /// </summary>
        private static void BindWindowControlButtons(Window window, ControlTemplate template)
        {
            var closeButton = template.FindName("CustomCloseButton", window) as Button;
            if (closeButton != null)
                closeButton.Click += (s, e) => window.Close();

            var minimizeButton = template.FindName("MinimizeButton", window) as Button;
            if (minimizeButton != null)
                minimizeButton.Click += (s, e) => window.WindowState = WindowState.Minimized;

            var maximizeButton = template.FindName("MaximizeButton", window) as Button;
            if (maximizeButton != null)
            {
                maximizeButton.Click += (s, e) =>
                {
                    window.WindowState = window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
                };
                UpdateMaximizeIcon(window);
            }

            var pinButton = template.FindName("PinButton", window) as Button;
            if (pinButton != null)
            {
                pinButton.Click += (s, e) => ToggleWindowTopmost(window);
                UpdatePinButtonState(window, template);
            }
        }

        /// <summary>
        /// 更新最大化按钮图标
        /// </summary>
        private static void UpdateMaximizeIcon(Window window)
        {
            if (window.Template != null)
            {
                var maximizeIcon = window.Template.FindName("MaximizeIcon", window) as Path;
                if (maximizeIcon != null)
                {
                    maximizeIcon.Data = window.WindowState == WindowState.Maximized
                        ? Geometry.Parse("M 0,4 H 10 V 14 H 0 Z M 2,0 H 12 V 10 H 10 V 2 H 2 Z")
                        : Geometry.Parse("M 0,0 H 10 V 10 H 0 Z");
                }
            }
        }

        /// <summary>
        /// 切换窗口置顶状态
        /// </summary>
        private static void ToggleWindowTopmost(Window window)
        {
            window.Topmost = !window.Topmost;
            UpdatePinButtonState(window, window.Template);
        }

        /// <summary>
        /// 更新置顶按钮状态
        /// </summary>
        private static void UpdatePinButtonState(Window window, ControlTemplate? template)
        {
            try
            {
                var pinButton = template?.FindName("PinButton", window) as Button;
                var pinRotation = template?.FindName("PinRotation", window) as RotateTransform;

                if (pinButton != null)
                {
                    pinButton.Tag = window.Topmost ? "Pinned" : "Unpinned";
                    pinButton.InvalidateProperty(Button.TagProperty);
                }

                if (pinRotation != null)
                {
                    pinRotation.Angle = window.Topmost ? 45 : 0;
                    pinRotation.InvalidateProperty(RotateTransform.AngleProperty);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"更新置顶按钮状态失败：{ex.Message}");
            }
        }

        /// <summary>
        /// 手动更新窗口的置顶按钮状态
        /// </summary>
        public static void UpdateWindowTopmostState(Window window)
        {
            if (window?.Template != null)
            {
                window.Dispatcher.BeginInvoke(new Action(() =>
                {
                    UpdatePinButtonState(window, window.Template);
                    window.InvalidateArrange();
                    window.InvalidateVisual();
                }), System.Windows.Threading.DispatcherPriority.Background);
            }
        }

        /// <summary>
        /// 绑定对话框事件
        /// </summary>
        public static void BindDialogEvents(DependencyObject window)
        {
            if (window is Window w)
            {
                w.Loaded += (s, e) =>
                {
                    w.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (w.Template != null)
                        {
                            var titleBar = w.Template.FindName("DialogTitleBarBorder", w) as Border;
                            if (titleBar != null)
                            {
                                titleBar.MouseLeftButtonDown += (sender, args) =>
                                {
                                    if (args.ClickCount == 2)
                                        w.WindowState = w.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
                                    else
                                        w.DragMove();
                                };
                            }

                            var closeButton = w.Template.FindName("DialogCloseButton", w) as Button;
                            if (closeButton != null)
                                closeButton.Click += (s, e) => w.Close();

                            var titleText = w.Template.FindName("DialogTitleText", w) as TextBlock;
                            if (titleText != null)
                                titleText.Text = w.Title;
                        }
                    }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                };
            }
        }

        /// <summary>
        /// 应用自定义窗口样式
        /// </summary>
        public static void ApplyCustomWindowStyle(Window window)
        {
            ApplyBaseStyle(window);

            var template = Application.Current.FindResource("CustomWindowTemplate") as ControlTemplate;
            if (template != null)
                window.Template = template;

            BindTitleBarEvents(window);
        }

        /// <summary>
        /// 应用自定义对话框样式
        /// </summary>
        public static void ApplyCustomDialogStyle(Window window)
        {
            ApplyBaseStyle(window);

            var template = Application.Current.FindResource("CustomDialogTemplate") as ControlTemplate;
            if (template != null)
                window.Template = template;

            BindDialogEvents(window);
        }
    }
}
