using System;
using System.Windows;
using System.Windows.Controls;
using Sai2Capture.Styles;
using WpfApplication = System.Windows.Application;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;
using WpfOrientation = System.Windows.Controls.Orientation;

namespace Sai2Capture.Services
{
    /// <summary>
    /// 自定义对话框服务
    /// </summary>
    public static class CustomDialogService
    {
        /// <summary>
        /// 显示确认对话框
        /// </summary>
        public static bool ShowConfirmDialog(Window? owner, string message, string title = "提示",
            string buttonText = "确认", string cancelText = "取消")
        {
            var dialog = CreateDialog(owner, message, title, 400, 200);
            var buttonPanel = CreateButtonPanel(dialog);

            var cancelButton = CreateButton(cancelText, "SecondaryButtonStyle", () => CloseDialog(dialog, false));
            var confirmButton = CreateButton(buttonText, "DefaultButtonStyle", () => CloseDialog(dialog, true));

            buttonPanel.Children.Add(cancelButton);
            buttonPanel.Children.Add(confirmButton);

            SetupDialogKeyboard(dialog);
            return dialog.ShowDialog() ?? false;
        }

        /// <summary>
        /// 显示三选项对话框
        /// </summary>
        /// <returns>按钮索引：0=第一个按钮，1=第二个按钮，2=第三个按钮，-1=关闭/ESC</returns>
        public static int ShowThreeButtonDialog(string message, string caption,
            string button1Text, string button2Text, string button3Text)
        {
            var dialog = CreateDialog(WpfApplication.Current.MainWindow, message, caption, 400, 200);
            var buttonPanel = CreateButtonPanel(dialog);

            int result = -1;
            var button1 = CreateButton(button1Text, "DefaultButtonStyle", () => { result = 0; dialog.Close(); }, new Thickness(0, 0, 8, 0));
            var button2 = CreateButton(button2Text, "DangerButtonStyle", () => { result = 1; dialog.Close(); }, new Thickness(8, 0, 8, 0));
            var button3 = CreateButton(button3Text, "SecondaryButtonStyle", () => { result = 2; dialog.Close(); }, new Thickness(8, 0, 0, 0));

            buttonPanel.Children.Add(button1);
            buttonPanel.Children.Add(button2);
            buttonPanel.Children.Add(button3);

            dialog.PreviewKeyDown += (s, e) =>
            {
                if (e.Key == System.Windows.Input.Key.Enter) { result = 0; dialog.Close(); }
                else if (e.Key == System.Windows.Input.Key.Escape) { result = -1; dialog.Close(); }
            };

            dialog.ShowDialog();
            return result;
        }

        public static bool ShowDialog(string message, string caption, string confirmText = "是", string cancelText = "否")
            => ShowConfirmDialog(WpfApplication.Current.MainWindow, message, caption, confirmText, cancelText);

        public static bool ShowDialog(string message, string caption = "提示")
            => ShowDialog(message, caption, "确认", "取消");

        private static Window CreateDialog(Window? owner, string message, string title, int width, int height)
        {
            var dialog = new Window
            {
                Title = title,
                Width = width,
                Height = height,
                Owner = owner,
                WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
                ShowInTaskbar = false,
                ResizeMode = ResizeMode.NoResize
            };
            WindowTemplateHelper.ApplyCustomDialogStyle(dialog);

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            grid.Children.Add(new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap,
                HorizontalAlignment = WpfHorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(16),
                FontSize = 14
            });

            dialog.Content = grid;
            return dialog;
        }

        private static StackPanel CreateButtonPanel(Window dialog)
        {
            var buttonPanel = new StackPanel
            {
                Orientation = WpfOrientation.Horizontal,
                HorizontalAlignment = WpfHorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 16)
            };
            Grid.SetRow(buttonPanel, 1);
            ((Grid)dialog.Content).Children.Add(buttonPanel);
            return buttonPanel;
        }

        private static System.Windows.Controls.Button CreateButton(string content, string styleKey, Action onClick, Thickness margin = default)
        {
            var button = new System.Windows.Controls.Button
            {
                Content = content,
                Style = (Style)WpfApplication.Current.FindResource(styleKey),
                Margin = margin == default ? new Thickness(0, 0, 8, 0) : margin
            };
            button.Click += (s, e) => onClick();
            return button;
        }

        private static void CloseDialog(Window dialog, bool result)
        {
            dialog.DialogResult = result;
            dialog.Close();
        }

        private static void SetupDialogKeyboard(Window dialog)
        {
            dialog.PreviewKeyDown += (s, e) =>
            {
                if (e.Key == System.Windows.Input.Key.Enter) { dialog.DialogResult = true; dialog.Close(); }
                else if (e.Key == System.Windows.Input.Key.Escape) { dialog.DialogResult = false; dialog.Close(); }
            };
        }
    }
}
