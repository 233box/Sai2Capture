using System;
using System.Globalization;
using System.Windows.Data;

namespace Sai2Capture.Converters
{
    /// <summary>
    /// 布尔值取反转换器
    /// 实现IValueConverter接口
    /// 将true转换为false，false转换为true
    /// 用于WPF数据绑定中的布尔值反转
    /// </summary>
    public class InverseBooleanConverter : IValueConverter
    {
        /// <summary>
        /// 正向转换：布尔值取反
        /// </summary>
        /// <param name="value">源值，应为布尔类型</param>
        /// <param name="targetType">目标类型</param>
        /// <param name="parameter">转换参数</param>
        /// <param name="culture">区域文化信息</param>
        /// <returns>取反后的布尔值，非布尔输入返回false</returns>
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                return !boolValue;
            }
            return false;
        }

        /// <summary>
        /// 反向转换：布尔值取反
        /// 与Convert方法逻辑相同
        /// </summary>
        /// <param name="value">源值，应为布尔类型</param>
        /// <param name="targetType">目标类型</param>
        /// <param name极="parameter">转换参数</param>
        /// <param name="culture">区域文化信息</param>
        /// <returns>取反后的布尔值，非布尔输入返回false</returns>
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                return !boolValue;
            }
            return false;
        }
    }
}
