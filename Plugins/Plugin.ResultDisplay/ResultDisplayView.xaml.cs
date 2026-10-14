using System;
using System.Globalization;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace Plugin.ResultDisplay
{
    /// <summary>
    /// 连线桥接：把"结果项 Id + 宿主插件"转换为该项对应的动态输入端口（IInputPort），
    /// 让结果项行内的 LinkableValueEditor 能按名挂到插件动态端口上，实现在表格内连线。
    /// （与 C# 脚本插件 / 数据记录插件的 InputPortConverter 同一范式，仅宿主类型不同）
    ///
    /// 第三个绑定值 PortsVersion 只是**重算触发器**，转换器本身不用它：
    /// 增删结果项会重建动态端口，MultiBinding 靠 PortsVersion 变化重新取一次新端口实例。
    /// </summary>
    public class InputPortConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            string? id = values?.Length > 0 ? values[0] as string : null;
            var plugin = values?.Length > 1 ? values[1] as ResultDisplayPlugin : null;
            return plugin?.GetInputPort(id) ?? null!;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// 显示窗口序号 → 一句话说明。0 是"只烧录不发布"，与 1~9 语义不同，
    /// 新手最容易在这里以为"填 0 就是第一个窗口"，所以就地写出后果。
    /// </summary>
    public class ViewIndexHintConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            int index = value is int i ? i : 0;
            return index <= 0
                ? "0 = 只烧录不发布（缩略图/导出图/存图仍带标注，不抢占窗口）"
                : $"发布到主界面第 {index} 个图像窗口";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// 枚举 → 中文显示名（读成员上的 <c>[Display(Name)]</c>）。
    /// 中文名与枚举成员是一对信息，硬写在 XAML 里就有了第二份真相（加一种位置容易只改枚举、忘了改界面）；
    /// 与 BlobDetect / CaliperMeasure / BeadInspect 三家同一范式，各自留在自己插件里
    /// （插件之间不许互相引用，见 <c>docs\新插件端口速查.md</c>）。
    /// </summary>
    public class EnumDisplayNameConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null) return string.Empty;
            var field = value.GetType().GetField(value.ToString()!);
            return field?.GetCustomAttribute<DisplayAttribute>()?.Name ?? value.ToString()!;
        }

        // 下拉框回写的是"选中项对象"本身（SelectedItem），不靠反向转换
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// 「结果显示」配置视图：底图 / 结果项表（行内连线）/ 显示与判定 / 高级。
    /// 本视图不含"执行"与"实时预览"按钮——试运行由主程序外壳提供；
    /// 配置期不出预览是有意的（离屏烧录要开 HALCON 窗口，只能在流程线程上做）。
    /// </summary>
    public partial class ResultDisplayView : UserControl
    {
        private ResultDisplayPlugin? Plugin => DataContext as ResultDisplayPlugin;

        public ResultDisplayView()
        {
            InitializeComponent();
        }

        private void AddItem_Click(object sender, RoutedEventArgs e)
        {
            Plugin?.AddItem();
        }

        private void DelItem_Click(object sender, RoutedEventArgs e)
        {
            if (Plugin?.Items == null) return;
            if ((sender as FrameworkElement)?.Tag is DisplayItemDef item)
                Plugin.Items.Remove(item);
        }
    }
}
