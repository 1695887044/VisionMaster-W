using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace Plugin.ResultUpload
{
    /// <summary>
    /// 通用枚举 → 中文标签（一套转换器覆盖本插件四个枚举；未知值退回 ToString）。
    /// 下拉条目与选中框都显示中文，SelectedItem 绑定的仍是枚举本身。
    /// </summary>
    public class EnumLabelConverter : IValueConverter
    {
        private static readonly Dictionary<object, string> Labels =
            new Dictionary<object, string>
            {
                // 报文预设
                { PayloadKind.FieldTable, "MES字段表" },
                { PayloadKind.DingTalkText, "钉钉文本" },
                { PayloadKind.WeComText, "企业微信文本" },
                { PayloadKind.RawJson, "原始JSON" },
                // 上报时机
                { SendTiming.EachRun, "每次执行" },
                { SendTiming.OnlyNG, "仅NG上报" },
                { SendTiming.OnTrigger, "触发端口" },
                // 取值来源
                { FieldSource.Port, "端口" },
                { FieldSource.Variable, "变量" },
                { FieldSource.BuiltIn, "内置" },
                // 内置字段
                { BuiltInField.Timestamp, "时间戳" },
                { BuiltInField.StepName, "步骤名" },
            };

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            value != null && Labels.TryGetValue(value, out var s) ? s : (value?.ToString() ?? "");

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// 枚举 ∈ 参数集合 → 可见性。ConverterParameter 支持逗号分隔多值（如 "DingTalkText,WeComText"）。
    /// 用于"选了某几种报文/时机才出现的区块"。
    /// </summary>
    public class EnumInToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null || !(parameter is string names)) return Visibility.Collapsed;
            var text = value.ToString();
            foreach (var n in names.Split(','))
                if (string.Equals(text, n.Trim(), StringComparison.Ordinal))
                    return Visibility.Visible;
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>布尔取反（IsTesting=true 时"立即发送"按钮置灰）。</summary>
    public class InverseBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            !(value is bool b && b);

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// 结果上报配置视图：报文预设 / 上传内容 / 发送策略 / 测试发送。
    /// 预览与字段联动的刷新由插件自身发 PropertyChanged，这里只管行增删、订阅与结果着色。
    /// </summary>
    public partial class ResultUploadView : UserControl
    {
        private ResultUploadPlugin Plugin => DataContext as ResultUploadPlugin;

        public ResultUploadView()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            var plugin = Plugin;
            if (plugin == null) return;
            // InstanceName（引擎注入）与 StepData.StepName 都是构造视图之后才齐的，故在此刷新一次预览
            plugin.RefreshPreview();
            plugin.PropertyChanged += OnPluginPropertyChanged;
            RefreshResultColor();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            var plugin = Plugin;
            if (plugin != null) plugin.PropertyChanged -= OnPluginPropertyChanged;
        }

        private void OnPluginPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ResultUploadPlugin.LastResult))
                RefreshResultColor();
        }

        /// <summary>按 "✔" 前缀给结果文本着色：成功绿、失败红，一眼分清。</summary>
        private void RefreshResultColor()
        {
            if (ResultText == null) return;
            string s = Plugin?.LastResult;
            ResultText.Foreground = s != null && s.StartsWith("✔")
                ? new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32))
                : new SolidColorBrush(Color.FromRgb(0xC6, 0x28, 0x28));
        }

        // 行内按钮的 DataContext 就是行对象（UploadFieldDef / HttpHeaderDef），直接从集合移除

        private void AddField_Click(object sender, RoutedEventArgs e) => Plugin?.AddField();

        private void RemoveField_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is UploadFieldDef f)
                Plugin?.Fields?.Remove(f);
        }

        private void AddHeader_Click(object sender, RoutedEventArgs e) => Plugin?.AddHeader();

        private void RemoveHeader_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is HttpHeaderDef h)
                Plugin?.Headers?.Remove(h);
        }

        private void TestSend_Click(object sender, RoutedEventArgs e) => Plugin?.TestSend();
    }
}
