using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace Plugin.DataRecord
{
    /// <summary>
    /// 连线桥接：把"列名 + 宿主插件"转换为该列对应的动态输入端口（IInputPort），
    /// 让列定义行内的 LinkableValueEditor 能按名挂到插件动态端口上，实现在表格内连线。
    /// （与 C# 脚本插件的 InputPortConverter 同一范式，仅宿主类型不同）
    /// </summary>
    public class InputPortConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            string name = values?.Length > 0 ? values[0] as string : null;
            var plugin = values?.Length > 1 ? values[1] as DataRecordPlugin : null;
            return plugin?.GetInputPort(name);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>枚举 → 中文标签（新手友好；未知枚举退回 ToString）。</summary>
    public class CnEnumLabelConverter : IValueConverter
    {
        private static readonly System.Collections.Generic.Dictionary<object, string> Labels = new System.Collections.Generic.Dictionary<object, string>
        {
            { ColumnSource.Port, "端口" },
            { ColumnSource.Variable, "变量" },
            { ColumnSource.BuiltIn, "内置" },
            { BuiltInField.Timestamp, "时间戳" },
            { BuiltInField.Sequence, "序列号" },
            { BuiltInField.StepName, "步骤名" },
            { BuiltInField.ElapsedMs, "本轮耗时ms" },
            { BuiltInField.ImagePath, "图片路径" },
            { ImageSaveMode.Off, "不存图" },
            { ImageSaveMode.OnlyNG, "仅NG存图" },
            { ImageSaveMode.Every, "每件存图" },
        };

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            value != null && Labels.TryGetValue(value, out var s) ? s : (value?.ToString() ?? "");

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>来源枚举 → 单元格可见性：ConverterParameter 写目标值（如 "Port"）。</summary>
    public class SourceToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string want = parameter as string;
            return value != null && string.Equals(value.ToString(), want, StringComparison.Ordinal)
                ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// 图片记录方式 → 可见性：只有"不存图"才隐藏（"仅NG/每件"都要暴露图片连线行）。
    /// 与 SourceToVisibilityConverter 的"等值匹配"语义相反，故单独一个转换器。
    /// </summary>
    public class NotOffToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is ImageSaveMode mode && mode != ImageSaveMode.Off
                ? Visibility.Visible : Visibility.Collapsed;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// CSV 记录配置视图：账本文件 / 列定义表（行内连线）/ 图片记录 / 测试写一行。
    /// 本视图不含"执行"按钮——试运行由主程序外壳提供。
    /// </summary>
    public partial class DataRecordView : UserControl
    {
        private DataRecordPlugin Plugin => DataContext as DataRecordPlugin;

        public DataRecordView()
        {
            InitializeComponent();
        }

        private void AddColumn_Click(object sender, RoutedEventArgs e)
        {
            Plugin?.AddColumn();
        }

        private void DelColumn_Click(object sender, RoutedEventArgs e)
        {
            if (Plugin?.Columns == null) return;
            if ((sender as FrameworkElement)?.Tag is RecordColumnDef col)
                Plugin.Columns.Remove(col);
        }

        private void TestWrite_Click(object sender, RoutedEventArgs e)
        {
            if (Plugin == null) return;
            string result = Plugin.TestWriteOneRow();
            TestResultText.Foreground = result != null && result.StartsWith("✔")
                ? new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32))
                : Brushes.Red;
        }
    }
}
