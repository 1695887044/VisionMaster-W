using Core.Halcon.Controls;
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace Plugin.CreateRoi
{
    /// <summary>布尔反转转换器（"原图/掩膜预览" RadioButton 互斥绑定用）</summary>
    public class InverseBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is bool b ? !b : value;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => value is bool b ? !b : value;
    }

    /// <summary>
    /// 涂擦模式枚举 ↔ 单选框勾选态：勾选时回写模式值；
    /// 组互斥引发的取消勾选回写 Binding.DoNothing（不污染源值），与 WPF 处理顺序无关、结果确定
    /// </summary>
    public class SmearModeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => Enum.TryParse<SmearModeType>(parameter as string, out var mode) && Equals(value, mode);

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => value is true && Enum.TryParse<SmearModeType>(parameter as string, out var mode)
                ? mode
                : Binding.DoNothing;
    }

    /// <summary>
    /// ListBox 滚动跟随附加属性：选中项变化（含 VM 回写，如画布点选定位）时滚动到可见
    /// </summary>
    public static class ListBoxAutoScroll
    {
        public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
            "Enabled", typeof(bool), typeof(ListBoxAutoScroll),
            new PropertyMetadata(false, (d, _) =>
            {
                if (d is ListBox lb)
                    lb.SelectionChanged += (_, __) =>
                        lb.Dispatcher.BeginInvoke(() => lb.ScrollIntoView(lb.SelectedItem));
            }));

        public static void SetEnabled(DependencyObject o, bool v) => o.SetValue(EnabledProperty, v);
        public static bool GetEnabled(DependencyObject o) => (bool)o.GetValue(EnabledProperty);
    }

    /// <summary>
    /// ROI 配置视图——纯绑定层（MVVM）：功能全部由 ImageEdit 控件 DP 提供，
    /// 业务逻辑全在 CreateRoiPlugin（ViewModel），本文件仅剩生命周期桥接与值转换器
    /// </summary>
    public partial class CreateRoiView : UserControl
    {
        public CreateRoiView()
        {
            InitializeComponent();
            // 唯一保留的 code-behind：视图就绪信号 → VM 回填输入图像
            Loaded += (_, _) => (DataContext as CreateRoiPlugin)?.OnViewLoaded();
        }
    }
}
