using System.Globalization;
using System.Reflection;
using System.Windows.Data;
using System.ComponentModel.DataAnnotations;

namespace Plugin.CaliperMeasure
{
    /// <summary>
    /// 枚举 → 中文显示名转换器：读枚举成员上的 [Display(Name)]。
    ///
    /// 为什么不在界面上硬写"宽度/间隙、两点距"这些中文：
    /// 枚举成员和它的中文名是一对信息，硬写在 XAML 里就有了第二份真相，
    /// 以后加测量类型时容易只改枚举、忘了改界面。这里统一从特性取，新增成员界面自动多一项。
    /// </summary>
    public class EnumDisplayNameConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null) return string.Empty;

            var field = value.GetType().GetField(value.ToString()!);
            var attr = field?.GetCustomAttribute<DisplayAttribute>();
            return attr?.Name ?? value.ToString()!;
        }

        // 下拉框回写的是"选中项对象"本身（SelectedItem），不靠反向转换
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
