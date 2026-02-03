using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
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

                            // 查找关闭按钮
                            var closeButton = template.FindName("CustomCloseButton", w) as Button;
                            if (closeButton != null)
                            {
                                closeButton.Click += (sender, args) => w.Close();
                            }

                            // 查找窗口标题文本
                            var titleText = template.FindName("WindowTitleText", w) as TextBlock;
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