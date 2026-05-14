using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace UI.Converters
{
    public class BooleanToVisibilityConverter : BaseMarkupConverter
    {
        public override object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // 检查是否传入了 ConverterParameter="Inverse"
            bool isInverse = parameter?.ToString()?.Equals("Inverse", StringComparison.OrdinalIgnoreCase) == true;

            // 🎯 场景 1：输入是 bool，输出 Visibility (常规用法)
            if (value is bool b)
            {
                // 巧妙利用异或(^)运算：如果是 True 且不反转，则为 Visible
                return (b ^ isInverse) ? Visibility.Visible : Visibility.Collapsed;
            }

            // 🎯 场景 2：输入是 Visibility，输出 bool (修复崩溃的核心：用于 ToggleButton.IsChecked)
            if (value is Visibility vis)
            {
                bool isVisible = (vis == Visibility.Visible);
                return isVisible ^ isInverse;
            }

            // 兜底返回，防止由于初始值为 null 导致的类型异常
            if (targetType == typeof(bool) || targetType == typeof(bool?))
                return false;

            return Visibility.Collapsed;
        }

        public override object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // 在双向绑定 (TwoWay) 中，UI 控件变化写回数据源时会调用 ConvertBack
            // 因为 bool 和 Visibility 的转换是对称的，所以直接复用 Convert 的逻辑即可！
            return Convert(value, targetType, parameter, culture);
        }
    }
}
