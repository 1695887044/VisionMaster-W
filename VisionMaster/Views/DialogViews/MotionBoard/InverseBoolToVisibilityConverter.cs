using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace VisionMaster.Views.DialogViews.MotionBoard
{
    /// <summary>true → Collapsed / false → Visible（隐藏当真）。反向布尔最常见的两处用途：
    /// 左栏卡行的"离线灰点"（在线时藏灰点、露呼吸点）与类似的两态显示切换。</summary>
    public sealed class InverseBoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is true ? Visibility.Collapsed : Visibility.Visible;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
