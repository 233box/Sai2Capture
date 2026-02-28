using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using Application = System.Windows.Application;
using Border = System.Windows.Controls.Border;
using Button = System.Windows.Controls.Button;
using Path = System.Windows.Shapes.Path;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Sai2Capture.Styles
{
    /// <summary>
    /// 窗口模板辅助类
    /// </summary>
    public static class WindowTemplateHelper
    {
        private static void ApplyBaseStyle(Window window)
        {
            window.WindowStyle = WindowStyle.None;
            window.AllowsTransparency = true;
            window.Background = System.Windows.Media.Brushes.Transparent;

            WindowChrome.SetWindowChrome(window, new WindowChrome
            {
                CaptionHeight = 28,
                CornerRadius = new CornerRadius(8),
                GlassFrameThickness = new Thickness(0),
                ResizeBorderThickness = new Thickness(6),
                UseAeroCaptionButtons = false
            });
        }

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
                                titleBar.MouseLeftButtonDown += TitleBar_MouseLeftButtonDown;
                            }

                            if (w.Template.FindName("WindowTitleText", w) is TextBlock titleText)
                                titleText.Text = w.Title;

                            BindWindowControlButtons(w, w.Template);
                        }
                    }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                };
                w.StateChanged += (s, e) => UpdateMaximizeIcon(w);
            }
        }

        private static void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not Border titleBar || titleBar.TemplatedParent is not Window w) return;

            if (e.ClickCount == 2)
            {
                w.WindowState = w.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            }
            else if (e.LeftButton == MouseButtonState.Pressed)
            {
                // 优化：使用 Mouse.GetPosition 避免 DragMove 的性能问题
                w.DragMove();
            }
        }

        private static void BindWindowControlButtons(Window window, ControlTemplate template)
        {
            if (template.FindName("CustomCloseButton", window) is Button closeButton)
                closeButton.Click += (s, e) => window.Close();

            if (template.FindName("MinimizeButton", window) is Button minimizeButton)
                minimizeButton.Click += (s, e) => window.WindowState = WindowState.Minimized;

            if (template.FindName("MaximizeButton", window) is Button maximizeButton)
            {
                maximizeButton.Click += (s, e) =>
                {
                    window.WindowState = window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
                };
                UpdateMaximizeIcon(window);
            }

            if (template.FindName("PinButton", window) is Button pinButton)
            {
                pinButton.Click += (s, e) => ToggleWindowTopmost(window);
                UpdatePinButtonState(window, template);
            }
        }

        private static void UpdateMaximizeIcon(Window window)
        {
            if (window.Template?.FindName("MaximizeIcon", window) is Path maximizeIcon)
                maximizeIcon.Data = window.WindowState == WindowState.Maximized
                    ? Geometry.Parse("M 0,4 H 10 V 14 H 0 Z M 2,0 H 12 V 10 H 10 V 2 H 2 Z")
                    : Geometry.Parse("M 0,0 H 10 V 10 H 0 Z");
        }

        private static void ToggleWindowTopmost(Window window)
        {
            window.Topmost = !window.Topmost;
            UpdatePinButtonState(window, window.Template);
        }

        private static void UpdatePinButtonState(Window window, ControlTemplate? template)
        {
            try
            {
                if (template?.FindName("PinButton", window) is Button pinButton)
                {
                    pinButton.Tag = window.Topmost ? "Pinned" : "Unpinned";
                    pinButton.InvalidateProperty(Button.TagProperty);
                }
                if (template?.FindName("PinRotation", window) is RotateTransform pinRotation)
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

        public static void UpdateWindowTopmostState(Window window)
        {
            if (window?.Template != null)
            {
                window.Dispatcher.BeginInvoke(new Action(() =>
                {
                    UpdatePinButtonState(window, window.Template);
                }), System.Windows.Threading.DispatcherPriority.Background);
            }
        }

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
                            if (w.Template.FindName("DialogTitleBarBorder", w) is Border titleBar)
                                titleBar.MouseLeftButtonDown += TitleBar_MouseLeftButtonDown;

                            if (w.Template.FindName("DialogCloseButton", w) is Button closeButton)
                                closeButton.Click += (s, e) => w.Close();

                            if (w.Template.FindName("DialogTitleText", w) is TextBlock titleText)
                                titleText.Text = w.Title;
                        }
                    }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                };
            }
        }

        public static void ApplyCustomWindowStyle(Window window)
        {
            ApplyBaseStyle(window);
            if (Application.Current.FindResource("CustomWindowTemplate") is ControlTemplate template)
                window.Template = template;
            BindTitleBarEvents(window);
        }

        public static void ApplyCustomDialogStyle(Window window)
        {
            ApplyBaseStyle(window);
            if (Application.Current.FindResource("CustomDialogTemplate") is ControlTemplate template)
                window.Template = template;
            BindDialogEvents(window);
        }
    }
}
