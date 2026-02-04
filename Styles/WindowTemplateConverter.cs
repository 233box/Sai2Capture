using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Shell;
using Application = System.Windows.Application;
using Button = System.Windows.Controls.Button;
using Border = System.Windows.Controls.Border;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Sai2Capture.Styles
{
    /// <summary>
    /// 窗口模板辅助类
    /// </summary>
    public static class WindowTemplateHelper
    {
        /// <summary>
        /// 绑定标题栏拖动事件
        /// </summary>
        public static void BindTitleBarEvents(DependencyObject window)
        {
            if (window is Window w)
            {
                w.Loaded += (s, e) =>
                {
                    // 延迟绑定，确保模板已完全加载
                    w.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        var template = w.Template;
                        if (template != null)
                        {
                            // 查找标题栏
                            var titleBar = template.FindName("TitleBarBorder", w) as Border;
                            if (titleBar != null)
                            {
                                titleBar.MouseLeftButtonDown += (sender, args) =>
                                {
                                    if (args.ClickCount == 2)
                                    {
                                        // 双击最大化/还原
                                        w.WindowState = w.WindowState == WindowState.Maximized
                                            ? WindowState.Normal
                                            : WindowState.Maximized;
                                    }
                                    else
                                    {
                                        // 单击拖动
                                        w.DragMove();
                                    }
                                };
                            }

                            // 查找窗口标题文本
                            var titleText = template.FindName("WindowTitleText", w) as TextBlock;
                            if (titleText != null)
                            {
                                titleText.Text = w.Title;
                            }

                            // 绑定窗口控制按钮事件
                            BindWindowControlButtons(w, template);
                        }
                    }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                };

                // 监听窗口状态变化，更新最大化按钮图标
                w.StateChanged += (s, e) => UpdateMaximizeIcon(w);
            }
        }

        /// <summary>
        /// 绑定窗口控制按钮事件
        /// </summary>
        private static void BindWindowControlButtons(Window window, ControlTemplate template)
        {
            // 关闭按钮
            var closeButton = template.FindName("CustomCloseButton", window) as Button;
            if (closeButton != null)
            {
                closeButton.Click += (sender, args) => window.Close();
            }

            // 最小化按钮
            var minimizeButton = template.FindName("MinimizeButton", window) as Button;
            if (minimizeButton != null)
            {
                minimizeButton.Click += (sender, args) => window.WindowState = WindowState.Minimized;
            }

            // 最大化按钮
            var maximizeButton = template.FindName("MaximizeButton", window) as Button;
            if (maximizeButton != null)
            {
                maximizeButton.Click += (sender, args) =>
                {
                    window.WindowState = window.WindowState == WindowState.Maximized
                        ? WindowState.Normal
                        : WindowState.Maximized;
                };

                // 初始化图标状态
                UpdateMaximizeIcon(window);
            }

            // 置顶按钮
            var pinButton = template.FindName("PinButton", window) as Button;
            if (pinButton != null)
            {
                pinButton.Click += (sender, args) => ToggleWindowTopmost(window);

                // 初始化置顶状态
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
                    if (window.WindowState == WindowState.Maximized)
                    {
                        // 还原图标
                        maximizeIcon.Data = Geometry.Parse("M 0,4 H 10 V 14 H 0 Z M 2,0 H 12 V 10 H 10 V 2 H 2 Z");
                    }
                    else
                    {
                        // 最大化图标
                        maximizeIcon.Data = Geometry.Parse("M 0,0 H 10 V 10 H 0 Z");
                    }
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
        private static void UpdatePinButtonState(Window window, ControlTemplate template)
        {
            try
            {
                var pinButton = template?.FindName("PinButton", window) as Button;
                var pinRotation = template?.FindName("PinRotation", window) as RotateTransform;

                if (pinButton != null)
                {
                    string newTag = window.Topmost ? "Pinned" : "Unpinned";
                    pinButton.Tag = newTag;
                    // 强制重新评估数据触发器
                    pinButton.InvalidateProperty(Button.TagProperty);
                }

                if (pinRotation != null)
                {
                    double newAngle = window.Topmost ? 45 : 0;
                    pinRotation.Angle = newAngle;
                    pinRotation.InvalidateProperty(RotateTransform.AngleProperty);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"更新置顶按钮状态失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 手动更新窗口的置顶按钮状态（用于外部程序调用）
        /// </summary>
        /// <param name="window">要更新的窗口</param>
        public static void UpdateWindowTopmostState(Window window)
        {
            if (window?.Template != null)
            {
                // 确保在UI线程上执行
                if (window.Dispatcher.CheckAccess())
                {
                    // 延迟执行以确保模板完全加载
                    window.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        UpdatePinButtonState(window, window.Template);
                        // 强制重新应用模板
                        window.InvalidateArrange();
                        window.InvalidateVisual();
                    }), System.Windows.Threading.DispatcherPriority.Background);
                }
                else
                {
                    window.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        UpdatePinButtonState(window, window.Template);
                        window.InvalidateArrange();
                        window.InvalidateVisual();
                    }), System.Windows.Threading.DispatcherPriority.Normal);
                }
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
                    // 延迟绑定
                    w.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        var template = w.Template;
                        if (template != null)
                        {
                            // 查找标题栏
                            var titleBar = template.FindName("DialogTitleBarBorder", w) as Border;
                            if (titleBar != null)
                            {
                                titleBar.MouseLeftButtonDown += (sender, args) =>
                                {
                                    if (args.ClickCount == 2)
                                    {
                                        w.WindowState = w.WindowState == WindowState.Maximized 
                                            ? WindowState.Normal 
                                            : WindowState.Maximized;
                                    }
                                    else
                                    {
                                        w.DragMove();
                                    }
                                };
                            }

                            // 查找关闭按钮
                            var closeButton = template.FindName("DialogCloseButton", w) as Button;
                            if (closeButton != null)
                            {
                                closeButton.Click += (sender, args) => w.Close();
                            }

                            // 查找对话框标题文本
                            var titleText = template.FindName("DialogTitleText", w) as TextBlock;
                            if (titleText != null)
                            {
                                titleText.Text = w.Title;
                            }
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
            window.WindowStyle = WindowStyle.None;
            window.AllowsTransparency = true;
            window.Background = System.Windows.Media.Brushes.Transparent;

            // 应用WindowChrome
            var chrome = new WindowChrome
            {
                CaptionHeight = 28,
                CornerRadius = new CornerRadius(8),
                GlassFrameThickness = new Thickness(0),
                ResizeBorderThickness = new Thickness(6),
                UseAeroCaptionButtons = false
            };
            WindowChrome.SetWindowChrome(window, chrome);

            // 应用模板
            var template = Application.Current.FindResource("CustomWindowTemplate") as ControlTemplate;
            if (template != null)
            {
                window.Template = template;
            }

            // 绑定事件
            BindTitleBarEvents(window);
        }

        /// <summary>
        /// 应用自定义对话框样式
        /// </summary>
        public static void ApplyCustomDialogStyle(Window window)
        {
            window.WindowStyle = WindowStyle.None;
            window.AllowsTransparency = true;
            window.Background = System.Windows.Media.Brushes.Transparent;

            // 应用WindowChrome
            var chrome = new WindowChrome
            {
                CaptionHeight = 28,
                CornerRadius = new CornerRadius(8),
                GlassFrameThickness = new Thickness(0),
                ResizeBorderThickness = new Thickness(6),
                UseAeroCaptionButtons = false
            };
            WindowChrome.SetWindowChrome(window, chrome);

            // 应用模板
            var template = Application.Current.FindResource("CustomDialogTemplate") as ControlTemplate;
            if (template != null)
            {
                window.Template = template;
            }

            // 绑定事件
            BindDialogEvents(window);
        }
    }
}