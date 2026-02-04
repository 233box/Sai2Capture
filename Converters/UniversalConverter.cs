using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using MediaColor = System.Windows.Media.Color;

namespace Sai2Capture.Converters
{
    /// <summary>
    /// 通用值转换器
    /// 支持多种常见的UI转换场景，通过参数指定转换模式
    /// </summary>
    public class UniversalConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string mode = parameter?.ToString() ?? "";

            switch (mode)
            {
                case "BoolToVisible":
                case "BoolToVisibility":
                    return ConvertToVisibility(value, true);
                case "BoolToCollapsed":
                case "BoolToInvertedVisibility":
                    return ConvertToVisibility(value, false);
                case "BoolToEnabledText":
                case "BoolToToggleText":
                    return ConvertToToggleText(value);
                case "BoolToEnabledBackground":
                    return ConvertToEnabledColor(value, true);
                case "BoolToEnabledForeground":
                    return ConvertToEnabledColor(value, false);
                case "StringToVisibility":
                    return ConvertStringToVisibility(value);
                case "StringToDisplayText":
                    return ConvertStringToDisplayText(value);
                case "CommandToTooltip":
                    return ConvertCommandToTooltip(value);
                case "Invert":
                case "Not":
                    return ConvertInverse(value);
                default:
                    return value;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string mode = parameter?.ToString() ?? "";

            switch (mode)
            {
                case "BoolToVisible":
                case "BoolToVisibility":
                    return ConvertBackToVisibility(value, true);
                case "BoolToCollapsed":
                case "BoolToInvertedVisibility":
                    return ConvertBackToVisibility(value, false);
                case "Invert":
                case "Not":
                    return ConvertInverse(value);
                default:
                    return value;
            }
        }

        #region 转换方法

        private static object ConvertToVisibility(object value, bool visibleWhenTrue)
        {
            if (value is bool boolValue)
            {
                return visibleWhenTrue ? 
                    (boolValue ? Visibility.Visible : Visibility.Collapsed) :
                    (boolValue ? Visibility.Collapsed : Visibility.Visible);
            }
            return Visibility.Collapsed;
        }

        private static object ConvertBackToVisibility(object value, bool visibleWhenTrue)
        {
            if (value is Visibility visibility)
            {
                return visibleWhenTrue ? 
                    (visibility == Visibility.Visible) :
                    (visibility == Visibility.Collapsed);
            }
            return false;
        }

        private static object ConvertToToggleText(object value)
        {
            if (value is bool boolValue)
            {
                return boolValue ? "已启用" : "已禁用";
            }
            return "未知状态";
        }

        private static object ConvertToEnabledColor(object value, bool isBackground)
        {
            if (value is bool boolValue)
            {
                var alpha = (byte)(isBackground ? 50 : 255);

                if (boolValue)
                {
                    // 绿色
                    return new SolidColorBrush(MediaColor.FromArgb(alpha, 76, 175, 80));
                }
                else
                {
                    // 红色
                    return new SolidColorBrush(MediaColor.FromArgb(alpha, 244, 67, 54));
                }
            }

            // 灰色（未知状态）
            var grayAlpha = (byte)(isBackground ? 50 : 255);
            return new SolidColorBrush(MediaColor.FromArgb(grayAlpha, 158, 158, 158));
        }

        private static object ConvertStringToVisibility(object value)
        {
            if (value is string str)
            {
                return string.IsNullOrEmpty(str) ? Visibility.Collapsed : Visibility.Visible;
            }
            return Visibility.Collapsed;
        }

        private static object ConvertStringToDisplayText(object value)
        {
            if (value is string hotkey)
            {
                return string.IsNullOrEmpty(hotkey) ? "未设置" : hotkey;
            }
            return "未设置";
        }

        private static object ConvertCommandToTooltip(object value)
        {
            if (value is string commandName)
            {
                return $"对应命令: {commandName}";
            }
            return "无对应命令";
        }

        private static object ConvertInverse(object value)
        {
            if (value is bool boolValue)
            {
                return !boolValue;
            }
            return false;
        }

        #endregion
    }
}