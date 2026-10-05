using System.Globalization;
using System.Reflection;
using System.Windows.Data;
using System.ComponentModel.DataAnnotations;

namespace Plugin.BeadInspect
{
    /// <summary>
    /// 枚举 → 中文显示名转换器：读枚举成员上的 [Display(Name)]。
    /// 与 BlobDetect / CaliperMeasure 同款（各插件自持一份，不进公共契约）。
    /// </summary>
    public class EnumDisplayNameConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null)
                return string.Empty;

            var field = value.GetType().GetField(value.ToString()!);
            var attr = field?.GetCustomAttribute<DisplayAttribute>();
            return attr?.Name ?? value.ToString()!;
        }

        // 下拉框回写的是"选中项对象"本身（SelectedItem），不靠反向转换
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
