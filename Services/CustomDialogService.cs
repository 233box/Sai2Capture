using System;
using System.Windows;
using System.Windows.Controls;
using Sai2Capture.Styles;
using Orientation = System.Windows.Controls.Orientation;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using VerticalAlignment = System.Windows.VerticalAlignment;
using Application = System.Windows.Application;
using Button = System.Windows.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;
using Grid = System.Windows.Controls.Grid;
using RowDefinition = System.Windows.Controls.RowDefinition;
using GridLength = System.Windows.GridLength;
using GridUnitType = System.Windows.GridUnitType;
using StackPanel = System.Windows.Controls.StackPanel;
using Thickness = System.Windows.Thickness;
using TextWrapping = System.Windows.TextWrapping;
using Style = System.Windows.Style;
using Panel = System.Windows.Controls.Panel;

namespace Sai2Capture.Services
{
    /// <summary>
    /// 自定义对话框服务
    /// </summary>
    public class CustomDialogService
    {
        /// <summary>
        /// 显示确认对话框
        /// </summary>
        /// <param name="owner">父窗口</param>
        /// <param name="message">消息内容</param>
        /// <param name="title">对话框标题</param>
        /// <param name="buttonText">确认按钮文本（默认为"确认"）</param>
        /// <param name="cancelText">取消按钮文本（默认为"取消"）</param>
        /// <returns>true表示确认，false表示取消</returns>
        public static bool ShowConfirmDialog(Window owner, string message, string title = "提示", 
            string buttonText = "确认", string cancelText = "取消")
        {
            var dialog = new Window
            {
                Title = title,
                Width = 400,
                Height = 200,
                Owner = owner,
                WindowStartupLocation = owner != null 
                    ? WindowStartupLocation.CenterOwner 
                    : WindowStartupLocation.CenterScreen,
                ShowInTaskbar = false,
                ResizeMode = ResizeMode.NoResize
            };

            // 应用自定义样式
            WindowTemplateHelper.ApplyCustomDialogStyle(dialog);

            bool result = false;
            bool isConfirmed = false;

            // 创建对话框内容
            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // 消息文本
            var textBlock = new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(16),
                FontSize = 14
            };
            Grid.SetRow(textBlock, 0);
            grid.Children.Add(textBlock);

            // 按钮区域
            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 16)
            };
            Grid.SetRow(buttonPanel, 1);
            grid.Children.Add(buttonPanel);

            // 取消按钮
            var cancelButton = new Button
            {
                Content = cancelText,
                Width = 80,
                Margin = new Thickness(0, 0, 8, 0),
                Tag = "DialogButton",
                Style = (Style)Application.Current.FindResource("SecondaryButtonStyle")
            };
            cancelButton.Click += (s, e) =>
            {
                result = false;
                isConfirmed = true;
                dialog.Close();
            };
            buttonPanel.Children.Add(cancelButton);

            // 确认按钮
            var confirmButton = new Button
            {
                Content = buttonText,
                Width = 80,
                Tag = "DialogButton",
                Style = (Style)Application.Current.FindResource("DefaultButtonStyle")
            };
            confirmButton.Click += (s, e) =>
            {
                result = true;
                isConfirmed = true;
                dialog.Close();
            };
            buttonPanel.Children.Add(confirmButton);

            dialog.Content = grid;

            // 设置按钮样式
            dialog.Loaded += (s, e) =>
            {
                if (dialog.Template != null)
                {
                    var cancelBtn = dialog.Template.FindName("CancelButton", dialog) as Button;
                    var confirmBtn = dialog.Template.FindName("ConfirmButton", dialog) as Button;

                    if (cancelBtn != null && confirmBtn != null)
                    {
                        // 移除模板中的按钮，使用我们自定义的按钮
                        var parentPanel = cancelBtn.Parent as Panel;
                        if (parentPanel != null)
                        {
                            int cancelIndex = parentPanel.Children.IndexOf(cancelBtn);
                            int confirmIndex = parentPanel.Children.IndexOf(confirmBtn);

                            // 移除模板按钮
                            parentPanel.Children.Remove(cancelBtn);
                            parentPanel.Children.Remove(confirmBtn);

                            // 添加自定义按钮到正确位置
                            if (cancelIndex >= 0 && cancelIndex < parentPanel.Children.Count)
                            {
                                parentPanel.Children.Insert(cancelIndex, cancelButton);
                            }
                            else
                            {
                                parentPanel.Children.Add(cancelButton);
                            }

                            if (confirmIndex >= 0 && confirmIndex < parentPanel.Children.Count)
                            {
                                parentPanel.Children.Insert(confirmIndex, confirmButton);
                            }
                            else
                            {
                                parentPanel.Children.Add(confirmButton);
                            }
                        }
                    }
                }
            };

            // 设置回车键确认、ESC键取消
            dialog.PreviewKeyDown += (s, e) =>
            {
                if (e.Key == System.Windows.Input.Key.Enter)
                {
                    result = true;
                    isConfirmed = true;
                    dialog.Close();
                }
                else if (e.Key == System.Windows.Input.Key.Escape)
                {
                    result = false;
                    isConfirmed = true;
                    dialog.Close();
                }
            };

            dialog.Closed += (s, e) =>
            {
                if (!isConfirmed)
                {
                    result = false;
                }
            };

            dialog.ShowDialog();
            return result;
        }

        /// <summary>
        /// 显示警告对话框
        /// </summary>
        /// <param name="owner">父窗口</param>
        /// <param name="message">消息内容</param>
        /// <param name="title">对话框标题</param>
        /// <returns>true表示确认</returns>
        public static bool ShowWarningDialog(Window owner, string message, string title = "警告")
        {
            return ShowConfirmDialog(owner, message, title, "确认");
        }

        /// <summary>
        /// 显示确认对话框（与MessageBox兼容）
        /// </summary>
        public static bool ShowDialog(string message, string caption, string confirmText = "是", string cancelText = "否")
        {
            var owner = Application.Current.MainWindow;
            return ShowConfirmDialog(owner, message, caption, confirmText, cancelText);
        }

        /// <summary>
        /// 显示确认对话框（简化版）
        /// </summary>
        public static bool ShowDialog(string message, string caption = "提示")
        {
            return ShowDialog(message, caption, "确认", "取消");
        }
    }
}