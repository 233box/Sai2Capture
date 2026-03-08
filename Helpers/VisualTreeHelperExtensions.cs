using System.Windows;
using System.Windows.Media;

namespace Sai2Capture.Helpers
{
    /// <summary>
    /// VisualTree 辅助扩展方法
    /// </summary>
    public static class VisualTreeHelperExtensions
    {
        /// <summary>
        /// 在视觉树中查找子控件
        /// </summary>
        public static T? FindChild<T>(this DependencyObject? parent) where T : DependencyObject
        {
            if (parent == null) return null;

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T result)
                    return result;

                if (child.FindChild<T>() is { } grandChild)
                    return grandChild;
            }

            return null;
        }
    }
}
