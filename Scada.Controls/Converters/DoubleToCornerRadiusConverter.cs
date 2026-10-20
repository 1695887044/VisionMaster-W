using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace VisionMaster.Scada.Controls
{
    /// <summary>
    /// 把 <see cref="ScadaElementBase.CornerRadius"/>（double）转成 WPF 的 <see cref="CornerRadius"/>。
    ///
    /// 为什么必须有这一层：`Border.CornerRadius` 是 `CornerRadius` 类型，
    /// 而图元的 `CornerRadius` 是 `double`。直接 `TemplateBinding` 时 WPF 不做数值转换，
    /// 实测会**静默落到 0**（PanelElement 的旧注释就踩过这个坑）。
    /// 所有 `Border` 的圆角都必须经过本转换器；`Rectangle.RadiusX/RadiusY` 是 double，不受影响。
    ///
    /// 负数/NaN/Infinity 一律按 0 处理：手工改坏的 .vms 不该在布局阶段抛异常。
    /// </summary>
    public sealed class DoubleToCornerRadiusConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            double radius = value is double d && double.IsFinite(d) ? Math.Max(0d, d) : 0d;
            return new CornerRadius(radius);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException("CornerRadius 是单向派生值，不支持反向转换。");
    }

    /// <summary>
    /// 同上，但只圆上面两个角：底板标题栏只需要顶部跟随底板圆角，底部是内容区的直线边界。
    /// </summary>
    public sealed class DoubleToCornerRadiusTopConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            double radius = value is double d && double.IsFinite(d) ? Math.Max(0d, d) : 0d;
            return new CornerRadius(radius, radius, 0d, 0d);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException("CornerRadius 是单向派生值，不支持反向转换。");
    }
}
